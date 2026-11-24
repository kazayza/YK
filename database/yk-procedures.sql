-- YKCoatings — مستخرج آليًا من القاعدة الحية بتاريخ 2026-11-24 02:19
-- المصدر: الخادم db68995.public.databaseasp.net / القاعدة db68995
-- أعد توليده بتشغيل database/extract-schema.ps1 بعد أي تغيير في القاعدة.

-- ============================================================
-- SQL_SCALAR_FUNCTION: dbo.fn_GetNextSequence
-- ============================================================
CREATE FUNCTION dbo.fn_GetNextSequence(@SeqCode NVARCHAR(50)) RETURNS NVARCHAR(50) AS BEGIN DECLARE @Next NVARCHAR(50); SET @Next = @SeqCode + '-' + FORMAT(GETDATE(),'yyMMddHHmmss'); RETURN @Next; END
GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CalcInvoiceDueDate
-- ============================================================


-- =============================================
-- 8) حساب تاريخ الاستحقاق
-- =============================================
CREATE   PROCEDURE sp_CalcInvoiceDueDate
    @InvoiceID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE pi
    SET pi.DueDate = DATEADD(DAY, ISNULL(pt.DueDays, 0), pi.InvoiceDate)
    FROM dbo.PurchaseInvoices pi
    LEFT JOIN dbo.PaymentTerms pt ON pi.PaymentTermID = pt.PaymentTermID
    WHERE pi.InvoiceID = @InvoiceID
      AND pi.DueDate IS NULL;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CalculateBatchCost
-- ============================================================


-- SP: حساب تكلفة الباتش الفعلية
CREATE  PROCEDURE [dbo].[sp_CalculateBatchCost]
    @BatchID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @ProductionOrderID INT;
    
    SELECT @ProductionOrderID = ProductionOrderID 
    FROM ProductionBatches WHERE BatchID = @BatchID;
    
    -- حساب تكلفة المواد الفعلية
    UPDATE ProductionBatches
    SET MaterialCost = ISNULL((
            SELECT SUM(mid.IssuedQty * mid.UnitCost)
            FROM MaterialIssueDetails mid
            INNER JOIN MaterialIssueNotes min2 ON mid.IssueID = min2.IssueID
            WHERE min2.ProductionOrderID = @ProductionOrderID
              AND min2.IssueStatus = 2 -- معتمد فقط
        ), 0)
    WHERE BatchID = @BatchID;
    
    -- تحديث إجمالي التكلفة
    UPDATE ProductionBatches
    SET TotalCost = ISNULL(MaterialCost, 0) 
                  + ISNULL(LaborCost, 0) 
                  + ISNULL(OverheadCost, 0),
        ModifiedDate = GETDATE()
    WHERE BatchID = @BatchID;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CalculatePayroll
-- ============================================================
CREATE PROCEDURE [dbo].[sp_CalculatePayroll]
    @PayrollID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- ========================================
    -- 1) تجهيز متغيرات الشهر
    -- ========================================
    DECLARE @PayrollMonth DATE;
    SELECT @PayrollMonth = PayrollMonth FROM Payroll WHERE PayrollID = @PayrollID;

    IF @PayrollMonth IS NULL
    BEGIN
        RAISERROR(N'المسير غير موجود أو لا يحتوي على شهر', 16, 1);
        RETURN;
    END

    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@PayrollMonth), MONTH(@PayrollMonth), 1);
    DECLARE @MonthEnd DATE = EOMONTH(@PayrollMonth);
    DECLARE @NextMonthStart DATE = DATEADD(MONTH, 1, @MonthStart);
    DECLARE @TotalDaysInMonth INT = DAY(@MonthEnd);

    -- ========================================
    -- 2) حذف التفاصيل القديمة
    -- ========================================
    DELETE FROM PayrollDetails WHERE PayrollID = @PayrollID;

    -- ========================================
    -- 3) إدراج كل الموظفين النشطين
    -- ========================================
    INSERT INTO PayrollDetails (
        PayrollID, EmployeeID, BasicSalary,
        TransportAllowance, HousingAllowance, PhoneAllowance,
        FoodAllowance, OtherAllowances,
        InsuranceEmployee, InsuranceCompany, PaymentMethod,
        WorkingDays, PresentDays, AbsentDays, LeaveDays,
        OvertimeHours, LateDays,
        OvertimeAmount, Incentives, Commissions, Rewards, OtherEarnings,
        LoanDeduction, PenaltyDeduction, AbsenceDeduction, LateDeduction, OtherDeductions
    )
    SELECT
        @PayrollID,
        e.EmployeeID,
        e.BasicSalary,
        e.TransportAllowance,
        e.HousingAllowance,
        e.PhoneAllowance,
        e.FoodAllowance,
        e.OtherAllowances,
        ROUND(ISNULL(e.InsuranceSalary, 0) * ISNULL(e.InsurancePercEmployee, 0) / 100.0, 2),
        ROUND(ISNULL(e.InsuranceSalary, 0) * ISNULL(e.InsurancePercCompany, 0) / 100.0, 2),
        e.PaymentMethod,
        30, 0, 0, 0,
        0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0
    FROM Employees e
    WHERE e.IsActive = 1 AND e.EmployeeStatus = 1;

    -- ========================================
    -- 4) تجهيز جدول مؤقت لأيام الشهر
    -- ========================================
    CREATE TABLE #MonthDays (
        DayDate DATE NOT NULL PRIMARY KEY
    );

    DECLARE @d DATE = @MonthStart;
    WHILE @d <= @MonthEnd
    BEGIN
        INSERT INTO #MonthDays (DayDate) VALUES (@d);
        SET @d = DATEADD(DAY, 1, @d);
    END

    -- ========================================
    -- 5) تجهيز جدول مؤقت للعطلات الرسمية
    -- ========================================
    CREATE TABLE #Holidays (
        HolidayDate DATE NOT NULL PRIMARY KEY
    );

    INSERT INTO #Holidays (HolidayDate)
    SELECT DISTINCT HolidayDate
    FROM PublicHolidays
    WHERE HolidayDate BETWEEN @MonthStart AND @MonthEnd;

    -- ========================================
    -- 6) تجهيز جدول مؤقت للإجازات المعتمدة
    -- ========================================
    CREATE TABLE #ApprovedLeaves (
        EmployeeID INT NOT NULL,
        LeaveDate DATE NOT NULL,
        IsPaid BIT NOT NULL,
        PRIMARY KEY (EmployeeID, LeaveDate)
    );

    INSERT INTO #ApprovedLeaves (EmployeeID, LeaveDate, IsPaid)
    SELECT
        lr.EmployeeID,
        md.DayDate,
        ISNULL(lt.IsPaid, 1)
    FROM LeaveRequests lr
    INNER JOIN LeaveTypes lt ON lr.LeaveTypeID = lt.LeaveTypeID
    INNER JOIN #MonthDays md ON md.DayDate BETWEEN lr.StartDate AND lr.EndDate
    WHERE lr.RequestStatus = 3
    GROUP BY lr.EmployeeID, md.DayDate, lt.IsPaid;

    -- ========================================
    -- 7) تصنيف كل يوم لكل موظف
    -- ========================================
    CREATE TABLE #DailyStatus (
        EmployeeID INT NOT NULL,
        DayDate DATE NOT NULL,
        DayStatus TINYINT NOT NULL,
        -- 1=حاضر, 2=غائب, 3=إجازة مدفوعة,
        -- 4=إجازة غير مدفوعة, 5=مأمورية,
        -- 6=متأخر, 7=عطلة رسمية, 8=إذن
        LateMinutes INT DEFAULT 0,
        OvertimeHours DECIMAL(5,2) DEFAULT 0,
        PRIMARY KEY (EmployeeID, DayDate)
    );

    -- إدراج كل يوم لكل موظف في المسير
    INSERT INTO #DailyStatus (EmployeeID, DayDate, DayStatus, LateMinutes, OvertimeHours)
    SELECT
        pd.EmployeeID,
        md.DayDate,
        -- تصنيف اليوم بالأولوية
        CASE
            -- أولاً: عطلة رسمية
            WHEN EXISTS (SELECT 1 FROM #Holidays h WHERE h.HolidayDate = md.DayDate)
                THEN 7

            -- ثانياً: إجازة معتمدة مدفوعة
            WHEN EXISTS (SELECT 1 FROM #ApprovedLeaves al
                         WHERE al.EmployeeID = pd.EmployeeID
                           AND al.LeaveDate = md.DayDate
                           AND al.IsPaid = 1)
                THEN 3

            -- ثالثاً: إجازة معتمدة غير مدفوعة
            WHEN EXISTS (SELECT 1 FROM #ApprovedLeaves al
                         WHERE al.EmployeeID = pd.EmployeeID
                           AND al.LeaveDate = md.DayDate
                           AND al.IsPaid = 0)
                THEN 4

            -- رابعاً: مأمورية
            WHEN a.AttendanceStatus = 4 THEN 5

            -- خامساً: حاضر
            WHEN a.AttendanceStatus = 1 THEN 1

            -- سادساً: متأخر (يعتبر حضور)
            WHEN a.AttendanceStatus = 5 THEN 6

            -- سابعاً: إذن (لا يعتبر غياب)
            WHEN a.AttendanceStatus = 6 THEN 8

            -- ثامناً: غائب صريح
            WHEN a.AttendanceStatus = 2 THEN 2

            -- تاسعاً: حالة 3 في الحضور (إجازة مسجلة في الحضور)
            WHEN a.AttendanceStatus = 3 THEN 3

            -- عاشراً: حالة 7 في الحضور (عطلة مسجلة في الحضور)
            WHEN a.AttendanceStatus = 7 THEN 7

            -- أخيراً: لا يوجد سجل = غياب تلقائي
            ELSE 2
        END AS DayStatus,

        -- دقائق التأخير
        CASE
            WHEN a.AttendanceStatus = 5
                 AND a.ActualIn IS NOT NULL
                 AND a.ScheduledIn IS NOT NULL
                 AND a.ActualIn > a.ScheduledIn
            THEN DATEDIFF(MINUTE, a.ScheduledIn, a.ActualIn)
            ELSE 0
        END AS LateMinutes,

        -- ساعات الأوفرتايم
        ISNULL(a.OvertimeHours, 0)

    FROM PayrollDetails pd
    CROSS JOIN #MonthDays md
    LEFT JOIN AttendanceRecords a
        ON a.EmployeeID = pd.EmployeeID
        AND a.AttendanceDate = md.DayDate
    WHERE pd.PayrollID = @PayrollID;

    -- ========================================
    -- 8) تحديث بيانات الحضور من التصنيف اليومي
    -- ========================================
    UPDATE pd
    SET
        pd.PresentDays = ISNULL(ds.PresentDays, 0),
        pd.AbsentDays = ISNULL(ds.AbsentDays, 0),
        pd.LeaveDays = ISNULL(ds.LeaveDays, 0),
        pd.LateDays = ISNULL(ds.LateDays, 0),
        pd.OvertimeHours = ISNULL(ds.TotalOvertimeHours, 0)
    FROM PayrollDetails pd
    LEFT JOIN (
        SELECT
            EmployeeID,
            -- حضور = حاضر + متأخر + مأمورية
            SUM(CASE WHEN DayStatus IN (1, 5, 6) THEN 1 ELSE 0 END) AS PresentDays,

            -- غياب = غياب صريح + غياب تلقائي + إجازة غير مدفوعة
            SUM(CASE WHEN DayStatus IN (2, 4) THEN 1 ELSE 0 END) AS AbsentDays,

            -- إجازات = مدفوعة + غير مدفوعة
            SUM(CASE WHEN DayStatus IN (3, 4) THEN 1 ELSE 0 END) AS LeaveDays,

            -- أيام التأخير
            SUM(CASE WHEN DayStatus = 6 THEN 1 ELSE 0 END) AS LateDays,

            -- إجمالي ساعات الأوفرتايم
            SUM(ISNULL(OvertimeHours, 0)) AS TotalOvertimeHours
        FROM #DailyStatus
        GROUP BY EmployeeID
    ) ds ON pd.EmployeeID = ds.EmployeeID
    WHERE pd.PayrollID = @PayrollID;

    -- ========================================
    -- 9) حساب خصم الغياب (من الأساسي فقط)
    -- ========================================
    -- الأيام غير المستحقة = غياب صريح + إجازة غير مدفوعة + أيام غير مسجلة
    UPDATE pd
    SET pd.AbsenceDeduction = ROUND(
        (ISNULL(pd.BasicSalary, 0) / 30.0) * ISNULL(pd.AbsentDays, 0), 2)
    FROM PayrollDetails pd
    WHERE pd.PayrollID = @PayrollID
      AND ISNULL(pd.AbsentDays, 0) > 0;

    -- ========================================
    -- 10) حساب خصم التأخير (بالدقائق)
    -- ========================================
    UPDATE pd
    SET pd.LateDeduction = ROUND(
        (ISNULL(pd.BasicSalary, 0) / 30.0 / 8.0 / 60.0) * ISNULL(late.TotalLateMinutes, 0), 2)
    FROM PayrollDetails pd
    LEFT JOIN (
        SELECT EmployeeID, SUM(ISNULL(LateMinutes, 0)) AS TotalLateMinutes
        FROM #DailyStatus
        WHERE DayStatus = 6 AND LateMinutes > 0
        GROUP BY EmployeeID
    ) late ON pd.EmployeeID = late.EmployeeID
    WHERE pd.PayrollID = @PayrollID
      AND ISNULL(late.TotalLateMinutes, 0) > 0;

    -- ========================================
    -- 11) حساب الأوفرتايم (1.5 من أجر الساعة)
    -- ========================================
    UPDATE pd
    SET pd.OvertimeAmount = ROUND(
        (ISNULL(pd.BasicSalary, 0) / 30.0 / 8.0) * 1.5 * ISNULL(pd.OvertimeHours, 0), 2)
    FROM PayrollDetails pd
    WHERE pd.PayrollID = @PayrollID
      AND ISNULL(pd.OvertimeHours, 0) > 0;

    -- ========================================
    -- 12) خصم أقساط السلف المستحقة
    -- ========================================
    UPDATE pd
    SET pd.LoanDeduction = ISNULL(loans.TotalDue, 0)
    FROM PayrollDetails pd
    LEFT JOIN (
        SELECT el.EmployeeID, SUM(li.Amount) AS TotalDue
        FROM LoanInstallments li
        INNER JOIN EmployeeLoans el ON li.LoanID = el.LoanID
        WHERE li.InstallmentStatus = 1
          AND li.DueDate <= @MonthEnd
          AND el.LoanStatus IN (2, 3)
        GROUP BY el.EmployeeID
    ) loans ON pd.EmployeeID = loans.EmployeeID
    WHERE pd.PayrollID = @PayrollID;

   -- ========================================
-- 13) خصم الجزاءات وإضافة المكافآت
--     حسب DeductionType:
--     1 = مبلغ ثابت
--     2 = نسبة من الأساسي
--     3 = أيام (أيام × أساسي/30)
-- ========================================
UPDATE pd
SET pd.PenaltyDeduction = ISNULL(pen.TotalPenalties, 0),
    pd.Rewards = ISNULL(pen.TotalRewards, 0)
FROM PayrollDetails pd
LEFT JOIN (
    SELECT
        pd2.PayrollDetailID,
        SUM(
            CASE
                WHEN pr.RecordType = 1 THEN
                    CASE ISNULL(pr.DeductionType, 1)
                        WHEN 1 THEN ISNULL(pr.Amount, 0)
                        WHEN 2 THEN ROUND(ISNULL(pd2.BasicSalary, 0) * ISNULL(pr.Percentage, 0) / 100.0, 2)
                        WHEN 3 THEN ROUND((ISNULL(pd2.BasicSalary, 0) / 30.0) * ISNULL(pr.Days, 0), 2)
                        ELSE 0
                    END
                ELSE 0
            END
        ) AS TotalPenalties,
        SUM(
            CASE
                WHEN pr.RecordType = 2 THEN
                    CASE ISNULL(pr.DeductionType, 1)
                        WHEN 1 THEN ISNULL(pr.Amount, 0)
                        WHEN 2 THEN ROUND(ISNULL(pd2.BasicSalary, 0) * ISNULL(pr.Percentage, 0) / 100.0, 2)
                        WHEN 3 THEN ROUND((ISNULL(pd2.BasicSalary, 0) / 30.0) * ISNULL(pr.Days, 0), 2)
                        ELSE 0
                    END
                ELSE 0
            END
        ) AS TotalRewards
    FROM PayrollDetails pd2
    LEFT JOIN EmployeePenaltiesRewards pr
        ON pr.EmployeeID = pd2.EmployeeID
       AND pr.RecordStatus = 2
       AND pr.PayrollID IS NULL
       AND pr.EffectiveMonth >= @MonthStart
       AND pr.EffectiveMonth < @NextMonthStart
    WHERE pd2.PayrollID = @PayrollID
    GROUP BY pd2.PayrollDetailID
) pen ON pd.PayrollDetailID = pen.PayrollDetailID
WHERE pd.PayrollID = @PayrollID;

    -- ========================================
    -- 14) تحديث إجماليات مسير المرتبات
    -- ========================================
    UPDATE Payroll
    SET
        TotalBasicSalary       = ISNULL((SELECT SUM(ISNULL(BasicSalary, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalAllowances        = ISNULL((SELECT SUM(
                                    ISNULL(TransportAllowance, 0) + ISNULL(HousingAllowance, 0)
                                    + ISNULL(PhoneAllowance, 0) + ISNULL(FoodAllowance, 0)
                                    + ISNULL(OtherAllowances, 0))
                                 FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalOvertime          = ISNULL((SELECT SUM(ISNULL(OvertimeAmount, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalIncentives        = ISNULL((SELECT SUM(ISNULL(Incentives, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalCommissions       = ISNULL((SELECT SUM(ISNULL(Commissions, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalGrossEarnings     = ISNULL((SELECT SUM(ISNULL(GrossEarnings, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalInsuranceEmp      = ISNULL((SELECT SUM(ISNULL(InsuranceEmployee, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalInsuranceComp     = ISNULL((SELECT SUM(ISNULL(InsuranceCompany, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalTax               = ISNULL((SELECT SUM(ISNULL(IncomeTax, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalLoanDeductions    = ISNULL((SELECT SUM(ISNULL(LoanDeduction, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalPenalties         = ISNULL((SELECT SUM(ISNULL(PenaltyDeduction, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalAbsenceDeductions = ISNULL((SELECT SUM(ISNULL(AbsenceDeduction, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalOtherDeductions   = ISNULL((SELECT SUM(ISNULL(OtherDeductions, 0) + ISNULL(LateDeduction, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalDeductions        = ISNULL((SELECT SUM(ISNULL(TotalDeductions, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        TotalNetSalary         = ISNULL((SELECT SUM(ISNULL(NetSalary, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        EmployeeCount          = ISNULL((SELECT COUNT(*) FROM PayrollDetails WHERE PayrollID = @PayrollID), 0),

        PayrollStatus          = 2,
        CalculatedDate         = GETDATE(),
        ModifiedDate           = GETDATE()

    WHERE PayrollID = @PayrollID;

    -- ========================================
    -- 15) تنظيف الجداول المؤقتة
    -- ========================================
    DROP TABLE IF EXISTS #DailyStatus;
    DROP TABLE IF EXISTS #ApprovedLeaves;
    DROP TABLE IF EXISTS #Holidays;
    DROP TABLE IF EXISTS #MonthDays;

    -- ========================================
    -- 16) النتيجة
    -- ========================================
    SELECT
        N'تم حساب المرتبات بنجاح' AS [النتيجة],
        (SELECT COUNT(*) FROM PayrollDetails WHERE PayrollID = @PayrollID) AS [عدد الموظفين],
        (SELECT SUM(ISNULL(NetSalary, 0)) FROM PayrollDetails WHERE PayrollID = @PayrollID) AS [إجمالي صافي المرتبات];
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CalculatePOTotals
-- ============================================================
CREATE   PROCEDURE sp_CalculatePOTotals
    @PurchaseOrderID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) تحديث إجمالي كل سطر
    UPDATE dbo.PurchaseOrderDetails
    SET DiscountAmount = UnitPrice * OrderedQty * DiscountPercent / 100,
        LineTotal = UnitPrice * OrderedQty * (1 - DiscountPercent / 100),
        TaxAmount = UnitPrice * OrderedQty * (1 - DiscountPercent / 100) * TaxRate / 100,
        LineTotalWithTax = UnitPrice * OrderedQty * (1 - DiscountPercent / 100) * (1 + TaxRate / 100)
    WHERE PurchaseOrderID = @PurchaseOrderID;

    -- 2) حساب الإجماليات
    DECLARE @SubTotal DECIMAL(18,2);
    DECLARE @LineTaxAmount DECIMAL(18,2);
    DECLARE @DiscountPercent DECIMAL(5,2);
    DECLARE @GeneralDiscount DECIMAL(18,2);
    DECLARE @ShippingCost DECIMAL(18,2);
    DECLARE @OtherCosts DECIMAL(18,2);

    SELECT @SubTotal = ISNULL(SUM(LineTotal), 0),
           @LineTaxAmount = ISNULL(SUM(TaxAmount), 0)
    FROM dbo.PurchaseOrderDetails
    WHERE PurchaseOrderID = @PurchaseOrderID;

    SELECT @DiscountPercent = ISNULL(DiscountPercent, 0),
           @ShippingCost = ISNULL(ShippingCost, 0),
           @OtherCosts = ISNULL(OtherCosts, 0)
    FROM dbo.PurchaseOrders
    WHERE PurchaseOrderID = @PurchaseOrderID;

    -- 3) الخصم العام
    SET @GeneralDiscount = @SubTotal * @DiscountPercent / 100;

    -- 4) الضريبة بعد الخصم
    DECLARE @TaxableAfterDiscount DECIMAL(18,2);
    SET @TaxableAfterDiscount = @SubTotal - @GeneralDiscount;

    DECLARE @AvgTaxRate DECIMAL(5,2);
    SELECT @AvgTaxRate = CASE
        WHEN SUM(LineTotal) > 0
        THEN SUM(TaxAmount) * 100.0 / SUM(LineTotal)
        ELSE 0
    END
    FROM dbo.PurchaseOrderDetails
    WHERE PurchaseOrderID = @PurchaseOrderID;

    DECLARE @FinalTaxAmount DECIMAL(18,2);
    SET @FinalTaxAmount = @TaxableAfterDiscount * @AvgTaxRate / 100;

    -- 5) تحديث الأمر
    UPDATE dbo.PurchaseOrders
    SET SubTotal = @SubTotal,
        DiscountAmount = @GeneralDiscount,
        TaxAmount = @FinalTaxAmount,
        TotalAmount = @TaxableAfterDiscount + @FinalTaxAmount + @ShippingCost + @OtherCosts,
        ModifiedDate = GETDATE()
    WHERE PurchaseOrderID = @PurchaseOrderID;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CalculateProductionMaterials
-- ============================================================


-- =============================================
-- Stored Procedures خاصة بالتصنيع
-- =============================================

-- SP: حساب المواد المطلوبة لأمر التصنيع من الوصفة
CREATE   PROCEDURE [dbo].[sp_CalculateProductionMaterials]
    @ProductionOrderID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @BOMID INT, @PlannedQty DECIMAL(18,4), @BatchSize DECIMAL(18,4);
    DECLARE @Multiplier DECIMAL(18,6);
    
    -- جلب بيانات أمر التصنيع والوصفة
    SELECT @BOMID = po.BOMID,
           @PlannedQty = po.PlannedQty,
           @BatchSize = b.BatchSize
    FROM ProductionOrders po
    INNER JOIN BillOfMaterials b ON po.BOMID = b.BOMID
    WHERE po.ProductionOrderID = @ProductionOrderID;
    
    -- حساب المضاعف
    SET @Multiplier = @PlannedQty / @BatchSize;
    
    -- حذف المواد القديمة لو موجودة
    DELETE FROM ProductionOrderMaterials 
    WHERE ProductionOrderID = @ProductionOrderID;
    
    -- إدراج المواد المطلوبة من الوصفة
    INSERT INTO ProductionOrderMaterials (
        ProductionOrderID, LineNumber, ItemID, UnitID, MaterialType,
        RequiredQty, UnitCost, BOMDetailID, Notes
    )
    SELECT 
        @ProductionOrderID,
        ROW_NUMBER() OVER (ORDER BY bd.MaterialType, bd.Sequence, bd.LineNumber),
        bd.ItemID,
        bd.UnitID,
        bd.MaterialType,
        ROUND(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100) * @Multiplier, 4),
        ISNULL(i.AverageCost, ISNULL(i.LastPurchasePrice, ISNULL(i.StandardCost, 0))),
        bd.BOMDetailID,
        bd.Notes
    FROM BOMDetails bd
    INNER JOIN Items i ON bd.ItemID = i.ItemID
    WHERE bd.BOMID = @BOMID;
    
    -- تحديث التكاليف التقديرية لأمر التصنيع
    UPDATE ProductionOrders
    SET EstimatedMaterialCost = ISNULL((
            SELECT SUM(RequiredQty * UnitCost) 
            FROM ProductionOrderMaterials 
            WHERE ProductionOrderID = @ProductionOrderID), 0),
        EstimatedLaborCost = ISNULL((
            SELECT LaborCostPerBatch * @Multiplier 
            FROM BillOfMaterials WHERE BOMID = @BOMID), 0),
        EstimatedOverheadCost = ISNULL((
            SELECT OverheadCostPerBatch * @Multiplier 
            FROM BillOfMaterials WHERE BOMID = @BOMID), 0),
        ModifiedDate = GETDATE()
    WHERE ProductionOrderID = @ProductionOrderID;
    
    -- تحديث التكلفة الإجمالية التقديرية
    UPDATE ProductionOrders
    SET EstimatedTotalCost = ISNULL(EstimatedMaterialCost, 0) 
                           + ISNULL(EstimatedLaborCost, 0) 
                           + ISNULL(EstimatedOverheadCost, 0)
    WHERE ProductionOrderID = @ProductionOrderID;
    
    -- عدد المواد
    SELECT COUNT(*) AS MaterialCount,
           SUM(CASE WHEN MaterialType = 1 THEN 1 ELSE 0 END) AS RawMaterials,
           SUM(CASE WHEN MaterialType = 2 THEN 1 ELSE 0 END) AS PackagingMaterials
    FROM ProductionOrderMaterials
    WHERE ProductionOrderID = @ProductionOrderID;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CalculatePurchaseInvoiceTotals
-- ============================================================


-- SP: حساب إجماليات فاتورة المشتريات
CREATE   PROCEDURE [dbo].[sp_CalculatePurchaseInvoiceTotals]
    @InvoiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- تحديث إجمالي كل سطر
    UPDATE PurchaseInvoiceDetails
    SET DiscountAmount = UnitPrice * Quantity * DiscountPercent / 100,
        LineTotal = UnitPrice * Quantity * (1 - DiscountPercent / 100),
        TaxAmount = UnitPrice * Quantity * (1 - DiscountPercent / 100) * TaxRate / 100,
        LineTotalWithTax = UnitPrice * Quantity * (1 - DiscountPercent / 100) * (1 + TaxRate / 100)
    WHERE InvoiceID = @InvoiceID;
    
    -- تحديث إجمالي الفاتورة
    UPDATE PurchaseInvoices
    SET SubTotal = ISNULL((
            SELECT SUM(LineTotal) FROM PurchaseInvoiceDetails 
            WHERE InvoiceID = @InvoiceID), 0),
        TaxableAmount = ISNULL((
            SELECT SUM(LineTotal) FROM PurchaseInvoiceDetails 
            WHERE InvoiceID = @InvoiceID AND TaxRate > 0), 0),
        TaxAmount = ISNULL((
            SELECT SUM(TaxAmount) FROM PurchaseInvoiceDetails 
            WHERE InvoiceID = @InvoiceID), 0),
        TotalAmount = ISNULL((
            SELECT SUM(LineTotalWithTax) FROM PurchaseInvoiceDetails 
            WHERE InvoiceID = @InvoiceID), 0)
            + ISNULL(ShippingCost, 0) + ISNULL(OtherCosts, 0),
        ModifiedDate = GETDATE()
    WHERE InvoiceID = @InvoiceID;
    
    -- تحديث حالة الدفع
    UPDATE PurchaseInvoices
    SET PaymentStatus = CASE
            WHEN ISNULL(PaidAmount, 0) = 0 THEN 1                -- غير مدفوعة
            WHEN ISNULL(PaidAmount, 0) >= TotalAmount THEN 3      -- مدفوعة بالكامل
            ELSE 2                                                 -- مدفوعة جزئياً
        END,
        InvoiceStatus = CASE
            WHEN ISNULL(PaidAmount, 0) >= TotalAmount THEN 4      -- مدفوعة بالكامل
            WHEN ISNULL(PaidAmount, 0) > 0 THEN 3                 -- مدفوعة جزئياً
            WHEN DueDate < CAST(GETDATE() AS DATE) THEN 5         -- متأخرة
            ELSE InvoiceStatus
        END
    WHERE InvoiceID = @InvoiceID
      AND InvoiceStatus NOT IN (1, 6); -- لا تحدث المسودة أو الملغية
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CalculateSalesInvoiceTotals
-- ============================================================


-- =============================================
-- SPs خاصة بالمبيعات والمخزون
-- =============================================

-- SP: حساب إجماليات فاتورة البيع
CREATE   PROCEDURE [dbo].[sp_CalculateSalesInvoiceTotals]
    @InvoiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE SalesInvoiceDetails
    SET DiscountAmount = UnitPrice * Quantity * DiscountPercent / 100,
        LineTotal = UnitPrice * Quantity * (1 - DiscountPercent / 100),
        TaxAmount = UnitPrice * Quantity * (1 - DiscountPercent / 100) * TaxRate / 100,
        LineTotalWithTax = UnitPrice * Quantity * (1 - DiscountPercent / 100) * (1 + TaxRate / 100)
    WHERE InvoiceID = @InvoiceID;
    
    UPDATE SalesInvoices
    SET SubTotal = ISNULL((SELECT SUM(LineTotal) FROM SalesInvoiceDetails WHERE InvoiceID = @InvoiceID), 0),
        TaxableAmount = ISNULL((SELECT SUM(LineTotal) FROM SalesInvoiceDetails WHERE InvoiceID = @InvoiceID AND TaxRate > 0), 0),
        TaxAmount = ISNULL((SELECT SUM(TaxAmount) FROM SalesInvoiceDetails WHERE InvoiceID = @InvoiceID), 0),
        TotalCost = ISNULL((SELECT SUM(Quantity * ISNULL(UnitCost,0)) FROM SalesInvoiceDetails WHERE InvoiceID = @InvoiceID), 0),
        TotalAmount = ISNULL((SELECT SUM(LineTotalWithTax) FROM SalesInvoiceDetails WHERE InvoiceID = @InvoiceID), 0)
                    + ISNULL(ShippingCost, 0),
        ModifiedDate = GETDATE()
    WHERE InvoiceID = @InvoiceID;
    
    UPDATE SalesInvoices
    SET PaymentStatus = CASE
            WHEN ISNULL(PaidAmount, 0) = 0 THEN 1
            WHEN ISNULL(PaidAmount, 0) >= TotalAmount THEN 3
            ELSE 2
        END,
        InvoiceStatus = CASE
            WHEN ISNULL(PaidAmount, 0) >= TotalAmount THEN 4
            WHEN ISNULL(PaidAmount, 0) > 0 THEN 3
            WHEN DueDate < CAST(GETDATE() AS DATE) AND InvoiceStatus NOT IN (1,6) THEN 5
            ELSE InvoiceStatus
        END
    WHERE InvoiceID = @InvoiceID AND InvoiceStatus NOT IN (1, 6);
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CheckMaterialAvailability
-- ============================================================


-- SP: فحص توفر الخامات لأمر التصنيع
CREATE   PROCEDURE [dbo].[sp_CheckMaterialAvailability]
    @ProductionOrderID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        pom.LineNumber,
        i.ItemCode,
        i.ItemNameAr,
        u.UnitNameAr,
        CASE pom.MaterialType WHEN 1 THEN N'مادة خام' ELSE N'مادة تعبئة' END AS MaterialType,
        pom.RequiredQty,
        pom.AvailableStock,
        CASE 
            WHEN pom.AvailableStock >= pom.RequiredQty THEN N'متوفر ✅'
            WHEN pom.AvailableStock > 0 THEN N'متوفر جزئياً ⚠️'
            ELSE N'غير متوفر ❌'
        END AS AvailabilityStatus,
        CASE 
            WHEN pom.AvailableStock < pom.RequiredQty 
            THEN pom.RequiredQty - pom.AvailableStock
            ELSE 0
        END AS ShortageQty
    FROM ProductionOrderMaterials pom
    INNER JOIN Items i ON pom.ItemID = i.ItemID
    INNER JOIN Units u ON pom.UnitID = u.UnitID
    WHERE pom.ProductionOrderID = @ProductionOrderID
    ORDER BY pom.MaterialType, pom.LineNumber;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CheckPermission
-- ============================================================


-- SP: التحقق من صلاحية المستخدم
CREATE   PROCEDURE [dbo].[sp_CheckPermission]
    @UserID INT,
    @ModuleCode NVARCHAR(50),
    @Action NVARCHAR(20),
    -- View, Add, Edit, Delete, Print, Export, Approve, Post, Cancel
    @HasPermission BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @HasPermission = 0;
    
    -- مدير النظام دائماً عنده صلاحية
    IF EXISTS (SELECT 1 FROM SystemUsers u 
               INNER JOIN UserRoles r ON u.RoleID = r.RoleID 
               WHERE u.UserID = @UserID AND r.RoleCode = N'ADMIN')
    BEGIN
        SET @HasPermission = 1;
        RETURN;
    END
    
    SELECT @HasPermission = CASE @Action
        WHEN N'View'    THEN ISNULL(up.CanView,    rp.CanView)
        WHEN N'Add'     THEN ISNULL(up.CanAdd,     rp.CanAdd)
        WHEN N'Edit'    THEN ISNULL(up.CanEdit,    rp.CanEdit)
        WHEN N'Delete'  THEN ISNULL(up.CanDelete,  rp.CanDelete)
        WHEN N'Print'   THEN ISNULL(up.CanPrint,   rp.CanPrint)
        WHEN N'Export'  THEN ISNULL(up.CanExport,  rp.CanExport)
        WHEN N'Approve' THEN ISNULL(up.CanApprove, rp.CanApprove)
        WHEN N'Post'    THEN ISNULL(up.CanPost,    rp.CanPost)
        WHEN N'Cancel'  THEN ISNULL(up.CanCancel,  rp.CanCancel)
        ELSE 0
    END
    FROM SystemUsers u
    INNER JOIN UserRoles r ON u.RoleID = r.RoleID
    INNER JOIN SystemModules sm ON sm.ModuleCode = @ModuleCode
    LEFT JOIN RolePermissions rp ON r.RoleID = rp.RoleID AND sm.ModuleID = rp.ModuleID
    LEFT JOIN UserPermissions up ON u.UserID = up.UserID AND sm.ModuleID = up.ModuleID
    WHERE u.UserID = @UserID AND u.IsActive = 1;
    
    SET @HasPermission = ISNULL(@HasPermission, 0);
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CreateNotification
-- ============================================================


-- SP: إنشاء إشعار
CREATE   PROCEDURE [dbo].[sp_CreateNotification]
    @NotificationType TINYINT,
    @Title NVARCHAR(200),
    @Message NVARCHAR(MAX),
    @Priority TINYINT = 2,
    @TargetUserID INT = NULL,
    @TargetRoleID INT = NULL,
    @RelatedModule NVARCHAR(50) = NULL,
    @RelatedRecordID INT = NULL,
    @CreatedBy INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO Notifications (
        NotificationType, Title, Message, Priority,
        TargetUserID, TargetRoleID,
        RelatedModule, RelatedRecordID, CreatedBy
    ) VALUES (
        @NotificationType, @Title, @Message, @Priority,
        @TargetUserID, @TargetRoleID,
        @RelatedModule, @RelatedRecordID, @CreatedBy
    );
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CreatePayrollJournal
-- ============================================================
-- =====================================================================
-- إصلاح 16: دفع الرواتب مستحيل — إجراء قيد الرواتب ينتج قيدًا لا يجتاز
-- تحقق الكود أبدًا، وينهار أصلًا عند أي صافي سالب.
-- العيوب في sp_CreatePayrollJournal الأصلي (مثبتة حيًا):
--  1) ينشئ القيد مسودة (JournalStatus=1) بلا إجماليات رأس، والكود
--     يشترط قيدًا مرحّلًا بإجماليات مطابقة لمجموع السطور → الدفع مرفوض دائمًا.
--  2) سطر الصافي يُرحّل إلى الحساب 1102 وهو حساب تجميعي (البنوك) لا
--     تفصيلي، والتحقق يشترط حسابات تفصيلية → مرفوض أيضًا.
--  3) جانب المدين = الإجمالي كاملًا بينما الخصومات غير القابلة للصرف
--     (غياب/جزاءات) لا تُقيد دائنًا في أي مكان → القيد غير متوازن كلما
--     وُجد خصم غياب أو جزاء.
--  4) صافي سالب (خصومات تتجاوز الاستحقاق) يجعل مبلغ السطر سالبًا فينهار
--     الإدراج بقيد CHECK بدل رسالة واضحة.
-- الإصلاح (نفس هيكل الإجراء الأصلي):
--  - قيد مرحّل مباشرة بإجماليات رأس وسنة/فترة ماليتين من شهر المسير.
--  - المصروف المدين = الإجمالي − الخصومات غير القابلة للصرف + حصة
--    الشركة من التأمينات (الغياب والجزاء يخفّضان تكلفة الأجر، لا يخلقان التزامًا).
--  - سطر الصافي إلى 1101 «الصندوق» (تفصيلي، وهو حساب الخزينة الرئيسية)
--    مع تخفيض رصيد الخزينة المخزّن بنفس الحركة.
--  - رفض صريح عند صافي إجمالي سالب أو قيد غير متوازن.
--  - تحديث الأرصدة المخزّنة لشجرة الحسابات بنفس نمط إجراء فاتورة الشراء.
-- ملاحظة: يكمله في الكود (تصحيح 15) رفض دفع أي مسير فيه صافي موظف سالب.
-- التنفيذ: شغّل هذا الملف مرة واحدة على قاعدة البيانات (SSMS).
-- =====================================================================

CREATE   PROCEDURE dbo.sp_CreatePayrollJournal
    @PayrollID INT,
    @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        DECLARE @JournalID INT, @JournalNumber NVARCHAR(50);
        DECLARE @PayrollMonth DATE, @PayrollNumber NVARCHAR(50);

        SELECT @PayrollMonth = PayrollMonth, @PayrollNumber = PayrollNumber
        FROM Payroll WHERE PayrollID = @PayrollID;

        DECLARE @TotalGross DECIMAL(18,2), @TotalInsEmp DECIMAL(18,2);
        DECLARE @TotalInsComp DECIMAL(18,2), @TotalTax DECIMAL(18,2);
        DECLARE @TotalLoan DECIMAL(18,2), @TotalNet DECIMAL(18,2);
        DECLARE @TotalDeductions DECIMAL(18,2);

        SELECT @TotalGross = TotalGrossEarnings,
               @TotalInsEmp = TotalInsuranceEmp,
               @TotalInsComp = TotalInsuranceComp,
               @TotalTax = TotalTax,
               @TotalLoan = TotalLoanDeductions,
               @TotalNet = TotalNetSalary,
               @TotalDeductions = TotalDeductions
        FROM Payroll WHERE PayrollID = @PayrollID;

        IF ISNULL(@TotalNet, 0) < 0
            THROW 51016, N'لا يمكن إنشاء قيد الرواتب: صافي المسير الإجمالي سالب؛ صحّح الخصومات أولًا.', 1;

        -- الخصومات غير القابلة للصرف (غياب، جزاءات، تأخير، أخرى) تخفّض تكلفة الأجر
        DECLARE @NonPayable DECIMAL(18,2) =
            ISNULL(@TotalDeductions, 0) - ISNULL(@TotalInsEmp, 0) - ISNULL(@TotalTax, 0) - ISNULL(@TotalLoan, 0);
        DECLARE @ExpenseDebit DECIMAL(18,2) =
            ISNULL(@TotalGross, 0) - @NonPayable + ISNULL(@TotalInsComp, 0);
        DECLARE @TotalCredit DECIMAL(18,2) =
            (ISNULL(@TotalInsEmp, 0) + ISNULL(@TotalInsComp, 0))
            + ISNULL(@TotalTax, 0) + ISNULL(@TotalLoan, 0) + ISNULL(@TotalNet, 0);

        IF @ExpenseDebit < 0
            THROW 51017, N'لا يمكن إنشاء قيد الرواتب: مبلغ المصروف المحسوب سالب؛ راجع إجماليات المسير.', 1;
        IF ABS(@ExpenseDebit - @TotalCredit) > 0.01
            THROW 51018, N'لا يمكن إنشاء قيد الرواتب: القيد غير متوازن؛ راجع إجماليات المسير.', 1;

        DECLARE @FiscalYearID INT, @PeriodID INT;
        SELECT TOP 1 @FiscalYearID = FiscalYearID, @PeriodID = PeriodID
        FROM dbo.AccountingPeriods
        WHERE @PayrollMonth >= StartDate AND @PayrollMonth <= EndDate
        ORDER BY PeriodID;
        SELECT TOP 1 @FiscalYearID = FiscalYearID
        FROM dbo.FiscalYears
        WHERE @PayrollMonth >= StartDate AND @PayrollMonth <= EndDate
        ORDER BY FiscalYearID;
        IF @PeriodID IS NULL
            THROW 51019, N'لا توجد فترة محاسبية تغطي شهر المسير؛ راجع الفترات المالية.', 1;

        DECLARE @CashAccountID INT = (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '1101');
        DECLARE @PostedByEmployeeID INT = (SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID);

        EXEC sp_GetNextNumber N'JV', @JournalNumber OUTPUT;

        BEGIN TRANSACTION;

        INSERT INTO JournalEntries (
            JournalNumber, JournalDate, JournalType, Description,
            SourceDocType, SourceDocID, SourceDocNumber,
            IsAutoGenerated, JournalStatus, FiscalYearID, PeriodID,
            TotalDebit, TotalCredit, CreatedBy, PostedBy
        ) VALUES (
            @JournalNumber, @PayrollMonth, 8,
            N'مرتبات شهر ' + FORMAT(@PayrollMonth, 'yyyy-MM') + N' - ' + @PayrollNumber,
            N'PAY', @PayrollID, @PayrollNumber,
            1, 2, @FiscalYearID, @PeriodID,
            @ExpenseDebit, @TotalCredit, @UserID, @PostedByEmployeeID
        );

        SET @JournalID = SCOPE_IDENTITY();
        DECLARE @Line INT = 1;

        -- مدين: مرتبات (إجمالي مستحق فعليًا + حصة الشركة من التأمينات)
        INSERT INTO JournalEntryDetails (JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
        VALUES (@JournalID, @Line,
            (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '6201'),
            @ExpenseDebit, 0, N'إجمالي المرتبات');
        SET @Line = @Line + 1;

        -- دائن: تأمينات مستحقة
        IF ISNULL(@TotalInsEmp, 0) + ISNULL(@TotalInsComp, 0) > 0
        BEGIN
            INSERT INTO JournalEntryDetails (JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
            VALUES (@JournalID, @Line,
                (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '2104'),
                0, ISNULL(@TotalInsEmp, 0) + ISNULL(@TotalInsComp, 0), N'تأمينات مستحقة');
            SET @Line = @Line + 1;
        END

        -- دائن: ضرائب كسب عمل
        IF ISNULL(@TotalTax, 0) > 0
        BEGIN
            INSERT INTO JournalEntryDetails (JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
            VALUES (@JournalID, @Line,
                (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '2106'),
                0, @TotalTax, N'ضرائب كسب عمل');
            SET @Line = @Line + 1;
        END

        -- دائن: سلف موظفين
        IF ISNULL(@TotalLoan, 0) > 0
        BEGIN
            INSERT INTO JournalEntryDetails (JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
            VALUES (@JournalID, @Line,
                (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '1106'),
                0, @TotalLoan, N'أقساط سلف');
            SET @Line = @Line + 1;
        END

        -- دائن: الصندوق (صافي المرتبات) — كان 1102 التجميعي فأصبح 1101 التفصيلي
        INSERT INTO JournalEntryDetails (JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
        VALUES (@JournalID, @Line,
            @CashAccountID,
            0, @TotalNet, N'صافي المرتبات المدفوعة');

        -- ربط القيد بمسير المرتبات
        UPDATE Payroll SET JournalID = @JournalID, ModifiedDate = GETDATE()
        WHERE PayrollID = @PayrollID;

        -- الخزينة الرئيسية تُخفّض مخزّنًا بنفس حركة الصافي
        UPDATE dbo.CashBoxes
        SET CurrentBalance = ISNULL(CurrentBalance, 0) - ISNULL(@TotalNet, 0)
        WHERE AccountID = @CashAccountID;

        -- تحديث الأرصدة المخزّنة لشجرة الحسابات (نفس نمط إجراء فاتورة الشراء)
        ;WITH DirectDeltas AS (
            SELECT AccountID, SUM(ISNULL(DebitAmount,0) - ISNULL(CreditAmount,0)) AS Delta
            FROM dbo.JournalEntryDetails WHERE JournalID = @JournalID GROUP BY AccountID
        ), AccountTree AS (
            SELECT AccountID AS LeafID, AccountID AS NodeID, 0 AS Lvl FROM DirectDeltas
            UNION ALL
            SELECT t.LeafID, coa.ParentAccountID, t.Lvl + 1
            FROM AccountTree t INNER JOIN dbo.ChartOfAccounts coa ON coa.AccountID = t.NodeID
            WHERE coa.ParentAccountID IS NOT NULL AND t.Lvl < 20
        ), RolledDeltas AS (
            SELECT t.NodeID AS AccountID, SUM(d.Delta) AS Delta
            FROM AccountTree t INNER JOIN DirectDeltas d ON d.AccountID = t.LeafID
            GROUP BY t.NodeID
        )
        UPDATE coa SET CurrentBalance = ISNULL(coa.CurrentBalance,0) + rd.Delta
        FROM dbo.ChartOfAccounts coa INNER JOIN RolledDeltas rd ON rd.AccountID = coa.AccountID
        OPTION (MAXRECURSION 100);

        COMMIT TRANSACTION;

        SELECT @JournalID AS JournalID, @JournalNumber AS JournalNumber;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CreatePurchaseInvoiceJournal
-- ============================================================

CREATE PROCEDURE dbo.sp_CreatePurchaseInvoiceJournal
    @InvoiceID INT,
    @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE
            @JournalID INT,
            @JournalNumber NVARCHAR(50),
            @SupplierID INT,
            @SubTotal DECIMAL(18,2),
            @DiscountAmount DECIMAL(18,2),
            @TaxAmount DECIMAL(18,2),
            @TotalAmount DECIMAL(18,2),
            @ShippingCost DECIMAL(18,2),
            @OtherCosts DECIMAL(18,2),
            @NetPurchaseAmount DECIMAL(18,2),
            @InvoiceNumber NVARCHAR(50),
            @InvoiceDate DATE,
            @SupplierName NVARCHAR(200),
            @FiscalYearID INT,
            @PeriodID INT,
            @PeriodCount INT,
            @InventoryAccountID INT,
            @InputVatAccountID INT,
            @PayableAccountID INT,
            @DebitTotal DECIMAL(18,2),
            @CreditTotal DECIMAL(18,2),
            @PostedByEmployeeID INT,
            @LineNumber INT = 1;

        SELECT
            @SupplierID = pi.SupplierID,
            @SubTotal = ISNULL(pi.SubTotal,0),
            @DiscountAmount = ISNULL(pi.DiscountAmount,0),
            @TaxAmount = ISNULL(pi.TaxAmount,0),
            @TotalAmount = ISNULL(pi.TotalAmount,0),
            @ShippingCost = ISNULL(pi.ShippingCost,0),
            @OtherCosts = ISNULL(pi.OtherCosts,0),
            @InvoiceNumber = pi.InvoiceNumber,
            @InvoiceDate = CONVERT(date,pi.InvoiceDate),
            @SupplierName = ISNULL(s.SupplierNameAr,N'')
        FROM dbo.PurchaseInvoices pi WITH (UPDLOCK,HOLDLOCK)
        INNER JOIN dbo.Suppliers s ON s.SupplierID = pi.SupplierID
        WHERE pi.InvoiceID = @InvoiceID;

        IF @InvoiceNumber IS NULL OR @SupplierID IS NULL
            THROW 51001, N'فاتورة المشتريات أو المورد غير موجود؛ لم يُنشأ القيد.', 1;

        -- PostedBy references Employees.EmployeeID, while @UserID references SystemUsers.UserID.
        -- Resolve the employee linked to the currently logged-in user; keep CreatedBy as @UserID.
        SELECT @PostedByEmployeeID = su.EmployeeID
        FROM dbo.SystemUsers su
        INNER JOIN dbo.Employees e ON e.EmployeeID = su.EmployeeID
        WHERE su.UserID = @UserID
          AND su.EmployeeID > 0;

        IF @PostedByEmployeeID IS NULL
            THROW 51012, N'حساب المستخدم الحالي غير مرتبط بموظف صالح؛ لم يُنشأ القيد.', 1;

        IF EXISTS (
            SELECT 1
            FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
            WHERE SourceDocType=N'PI'
              AND (SourceDocID=@InvoiceID OR SourceDocNumber=@InvoiceNumber)
        )
            THROW 51002, N'يوجد قيد مشتريات سابق لهذه الفاتورة؛ أوقفنا التكرار.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseInvoiceDetails WHERE InvoiceID=@InvoiceID)
            THROW 51011, N'لا تحتوي فاتورة المشتريات على تفاصيل؛ لم يُنشأ القيد.', 1;

        SET @NetPurchaseAmount = @SubTotal - @DiscountAmount;
        IF @SubTotal < 0 OR @DiscountAmount < 0 OR @TaxAmount < 0 OR @TotalAmount < 0
           OR @ShippingCost < 0 OR @OtherCosts < 0 OR @NetPurchaseAmount < 0
            THROW 51004, N'توجد مبالغ سالبة أو خصم يتجاوز صافي المشتريات؛ لم يُنشأ القيد.', 1;

        IF ABS(@TotalAmount - (@NetPurchaseAmount + @TaxAmount + @ShippingCost + @OtherCosts)) > 0.01
            THROW 51005, N'إجمالي الفاتورة لا يطابق صافي المشتريات والضريبة والشحن والتكاليف الأخرى؛ راجع إعادة الحساب.', 1;

        SELECT @PeriodCount = COUNT(1)
        FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
        INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK)
            ON ap.FiscalYearID = fy.FiscalYearID
        WHERE @InvoiceDate BETWEEN CONVERT(date,fy.StartDate) AND CONVERT(date,fy.EndDate)
          AND @InvoiceDate BETWEEN CONVERT(date,ap.StartDate) AND CONVERT(date,ap.EndDate)
          AND fy.YearStatus = 1
          AND ap.PeriodStatus = 1;

        IF ISNULL(@PeriodCount,0) <> 1
            THROW 51006, N'لا توجد سنة وفترة مالية مفتوحتان وحيدتان تغطيان تاريخ فاتورة المشتريات؛ لم يُنشأ القيد.', 1;

        SELECT TOP (1)
            @FiscalYearID = fy.FiscalYearID,
            @PeriodID = ap.PeriodID
        FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
        INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK)
            ON ap.FiscalYearID = fy.FiscalYearID
        WHERE @InvoiceDate BETWEEN CONVERT(date,fy.StartDate) AND CONVERT(date,fy.EndDate)
          AND @InvoiceDate BETWEEN CONVERT(date,ap.StartDate) AND CONVERT(date,ap.EndDate)
          AND fy.YearStatus = 1
          AND ap.PeriodStatus = 1;

        DECLARE @RequiredPurchaseAccounts TABLE
        (
            AccountCode NVARCHAR(50) PRIMARY KEY,
            ExpectedAccountType INT NOT NULL
        );
        DECLARE @PurchaseAccounts TABLE (AccountCode NVARCHAR(50) PRIMARY KEY, AccountID INT NOT NULL);
        INSERT INTO @RequiredPurchaseAccounts(AccountCode,ExpectedAccountType)
        VALUES (N'110501',1),(N'2101',2);
        IF @TaxAmount > 0 INSERT INTO @RequiredPurchaseAccounts(AccountCode,ExpectedAccountType) VALUES (N'1108',1);

        INSERT INTO @PurchaseAccounts(AccountCode,AccountID)
        SELECT r.AccountCode, MIN(coa.AccountID)
        FROM @RequiredPurchaseAccounts r
        LEFT JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK)
            ON coa.AccountCode = r.AccountCode
        GROUP BY r.AccountCode,r.ExpectedAccountType
        HAVING COUNT(coa.AccountID) = 1
           AND MIN(CONVERT(int,ISNULL(coa.IsActive,0))) = 1
           AND MIN(CONVERT(int,ISNULL(coa.AccountType,0))) = r.ExpectedAccountType
           AND
           (
               MIN(CONVERT(int,ISNULL(coa.IsDetailAccount,0))) = 1
               OR
               (
                   r.AccountCode=N'2101'
                   AND MIN(CONVERT(int,ISNULL(coa.IsSystemAccount,0))) = 1
                   AND MIN(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(coa.LinkedEntityType,N''))))=N'SUPPLIER' THEN 1 ELSE 0 END) = 1
               )
           );

        IF (SELECT COUNT(1) FROM @PurchaseAccounts) <> (SELECT COUNT(1) FROM @RequiredPurchaseAccounts)
            THROW 51007, N'حسابات قيد المشتريات غير موجودة/غير نشطة/مكررة أو نوعها/ربطها النظامي غير مطابق؛ لم يُنشأ القيد.', 1;

        SELECT @InventoryAccountID = AccountID FROM @PurchaseAccounts WHERE AccountCode=N'110501';
        SELECT @PayableAccountID = AccountID FROM @PurchaseAccounts WHERE AccountCode=N'2101';
        SELECT @InputVatAccountID = AccountID FROM @PurchaseAccounts WHERE AccountCode=N'1108';

        SET @DebitTotal = @NetPurchaseAmount + @ShippingCost + @OtherCosts + @TaxAmount;
        SET @CreditTotal = @TotalAmount;
        IF ABS(@DebitTotal - @CreditTotal) > 0.01
            THROW 51008, N'قيد فاتورة المشتريات غير متوازن بعد الخصم وإضافة الشحن والتكاليف الأخرى؛ لم يُنشأ القيد.', 1;

        EXEC dbo.sp_GetNextNumber N'JV', @JournalNumber OUTPUT;
        IF NULLIF(@JournalNumber,N'') IS NULL
            THROW 51009, N'تعذر توليد رقم القيد JV؛ لم يُنشأ القيد.', 1;

        INSERT INTO dbo.JournalEntries
        (
            JournalNumber, JournalDate, FiscalYearID, PeriodID, JournalType,
            Description, Reference, TotalDebit, TotalCredit,
            SourceDocType, SourceDocID, SourceDocNumber,
            IsAutoGenerated, JournalStatus, PostedBy, PostedDate,
            CreatedBy, CreatedDate
        )
        VALUES
        (
            @JournalNumber, @InvoiceDate, @FiscalYearID, @PeriodID, 2,
            N'فاتورة مشتريات رقم ' + @InvoiceNumber + N' - ' + @SupplierName,
            @InvoiceNumber, @DebitTotal, @CreditTotal,
            N'PI', @InvoiceID, @InvoiceNumber,
            1, 2, @PostedByEmployeeID, GETDATE(), @UserID, GETDATE()
        );

        SET @JournalID = CONVERT(int,SCOPE_IDENTITY());

        INSERT INTO dbo.JournalEntryDetails
        (
            JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
        )
        VALUES
        (
            @JournalID,@LineNumber,@InventoryAccountID,@NetPurchaseAmount,0,
            N'مشتريات بعد الخصم - ' + @InvoiceNumber,@InvoiceNumber
        );
        SET @LineNumber += 1;

        IF @ShippingCost > 0
        BEGIN
            INSERT INTO dbo.JournalEntryDetails
            (
                JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
            )
            VALUES
            (
                @JournalID,@LineNumber,@InventoryAccountID,@ShippingCost,0,
                N'شحن مشتريات مضاف إلى تكلفة المخزون - ' + @InvoiceNumber,@InvoiceNumber
            );
            SET @LineNumber += 1;
        END;

        IF @OtherCosts > 0
        BEGIN
            INSERT INTO dbo.JournalEntryDetails
            (
                JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
            )
            VALUES
            (
                @JournalID,@LineNumber,@InventoryAccountID,@OtherCosts,0,
                N'تكاليف شراء أخرى مضافة إلى تكلفة المخزون - ' + @InvoiceNumber,@InvoiceNumber
            );
            SET @LineNumber += 1;
        END;

        IF @TaxAmount > 0
        BEGIN
            INSERT INTO dbo.JournalEntryDetails
            (
                JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
            )
            VALUES
            (
                @JournalID,@LineNumber,@InputVatAccountID,@TaxAmount,0,
                N'ضريبة مدخلات - ' + @InvoiceNumber,@InvoiceNumber
            );
            SET @LineNumber += 1;
        END;

        INSERT INTO dbo.JournalEntryDetails
        (
            JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference,EntityType,EntityID
        )
        VALUES
        (
            @JournalID,@LineNumber,@PayableAccountID,0,@TotalAmount,
            N'مورد - ' + @SupplierName,@InvoiceNumber,N'Supplier',@SupplierID
        );

        DECLARE @SavedDebit DECIMAL(18,2), @SavedCredit DECIMAL(18,2), @SavedLineCount INT;
        SELECT
            @SavedDebit = ISNULL(SUM(DebitAmount),0),
            @SavedCredit = ISNULL(SUM(CreditAmount),0),
            @SavedLineCount = COUNT(1)
        FROM dbo.JournalEntryDetails
        WHERE JournalID=@JournalID;

        IF @SavedLineCount < 2 OR ABS(@SavedDebit-@SavedCredit) > 0.01
            THROW 51010, N'فشل التحقق من توازن قيد فاتورة المشتريات؛ أُلغيت المعاملة.', 1;

        ;WITH DirectDeltas AS
        (
            SELECT AccountID, SUM(DebitAmount-CreditAmount) AS Delta
            FROM dbo.JournalEntryDetails
            WHERE JournalID=@JournalID
            GROUP BY AccountID
        ),
        AccountTree AS
        (
            SELECT coa.AccountID, coa.ParentAccountID, d.Delta
            FROM DirectDeltas d
            INNER JOIN dbo.ChartOfAccounts coa ON coa.AccountID=d.AccountID
            UNION ALL
            SELECT parent.AccountID, parent.ParentAccountID, tree.Delta
            FROM AccountTree tree
            INNER JOIN dbo.ChartOfAccounts parent ON parent.AccountID=tree.ParentAccountID
        ),
        RolledDeltas AS
        (
            SELECT AccountID, SUM(Delta) AS Delta
            FROM AccountTree
            GROUP BY AccountID
        )
        UPDATE coa
        SET CurrentBalance=ISNULL(coa.CurrentBalance,0)+rd.Delta
        FROM dbo.ChartOfAccounts coa
        INNER JOIN RolledDeltas rd ON rd.AccountID=coa.AccountID
        OPTION (MAXRECURSION 20);

        COMMIT TRANSACTION;
        SELECT @JournalID AS JournalID,@JournalNumber AS JournalNumber;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CreatePurchaseReturnJournal
-- ============================================================
-- =====================================================================
-- إصلاح 12 (جانب الإجراء المخزن): اعتماد مرتجعات المشتريات
-- المشكلة: sp_CreatePurchaseReturnJournal ينشئ القيد بحالة مسودة (1)
-- ودون إجماليات في رأس القيد ودون سنة/فترة مالية، بينما تحقق الاعتماد
-- في الكود يشترط قيدًا مرحّلًا (2) بإجماليات مطابقة لمجموع السطور.
-- الإصلاح: القيد يُنشأ مرحّلًا مباشرة بإجمالياته وسنته وفترته، وتُحدّث
-- أرصدة شجرة الحسابات المخزنة بنفس نمط إجراء فاتورة المشتريات.
-- يلزم معه تصحيح الكود 12ب (استثناء حساب الموردين 2101 في تحقق
-- التفاصيل، لأنه حساب تحكم غير تفصيلي بطبيعته).
-- التنفيذ: شغّل هذا الملف مرة واحدة على قاعدة البيانات (SSMS).
-- =====================================================================

CREATE   PROCEDURE dbo.sp_CreatePurchaseReturnJournal
    @ReturnID INT,
    @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        DECLARE @JournalID INT, @JournalNumber NVARCHAR(50);
        DECLARE @SupplierID INT, @SubTotal DECIMAL(18,2);
        DECLARE @TaxAmount DECIMAL(18,2), @TotalAmount DECIMAL(18,2);
        DECLARE @ReturnNumber NVARCHAR(50), @ReturnDate DATE;
        DECLARE @SupplierName NVARCHAR(200);
        DECLARE @FiscalYearID INT, @PeriodID INT;

        SELECT
            @SupplierID = pr.SupplierID,
            @SubTotal = pr.SubTotal,
            @TaxAmount = pr.TaxAmount,
            @TotalAmount = pr.TotalAmount,
            @ReturnNumber = pr.ReturnNumber,
            @ReturnDate = pr.ReturnDate,
            @SupplierName = s.SupplierNameAr
        FROM PurchaseReturns pr
        INNER JOIN Suppliers s ON pr.SupplierID = s.SupplierID
        WHERE pr.ReturnID = @ReturnID;

        SELECT TOP 1 @FiscalYearID = FiscalYearID FROM dbo.FiscalYears
        WHERE @ReturnDate BETWEEN StartDate AND EndDate ORDER BY FiscalYearID DESC;
        SELECT TOP 1 @PeriodID = PeriodID FROM dbo.AccountingPeriods
        WHERE @ReturnDate BETWEEN StartDate AND EndDate ORDER BY PeriodID DESC;

        EXEC sp_GetNextNumber N'JV', @JournalNumber OUTPUT;

        BEGIN TRANSACTION;

        INSERT INTO JournalEntries (
            JournalNumber,
            JournalDate,
            JournalType,
            Description,
            SourceDocType,
            SourceDocID,
            SourceDocNumber,
            IsAutoGenerated,
            JournalStatus,
            FiscalYearID,
            PeriodID,
            TotalDebit,
            TotalCredit,
            CreatedBy
        ) VALUES (
            @JournalNumber,
            @ReturnDate,
            2,
            N'مرتجع مشتريات رقم ' + @ReturnNumber + N' - ' + @SupplierName,
            N'PRET',
            @ReturnID,
            @ReturnNumber,
            1,
            2,
            @FiscalYearID,
            @PeriodID,
            @TotalAmount,
            ISNULL(@SubTotal, 0) + ISNULL(@TaxAmount, 0),
            @UserID
        );

        SET @JournalID = SCOPE_IDENTITY();

        -- مدين: المورد
        INSERT INTO JournalEntryDetails (
            JournalID,
            LineNumber,
            AccountID,
            DebitAmount,
            CreditAmount,
            Description,
            Reference,
            EntityType,
            EntityID
        )
        VALUES (
            @JournalID,
            1,
            (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '2101'),
            @TotalAmount,
            0,
            N'مورد - ' + @SupplierName,
            @ReturnNumber,
            N'Supplier',
            @SupplierID
        );

        -- دائن: مردودات مشتريات / مخزون
        INSERT INTO JournalEntryDetails (
            JournalID,
            LineNumber,
            AccountID,
            DebitAmount,
            CreditAmount,
            Description,
            Reference
        )
        VALUES (
            @JournalID,
            2,
            (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '110501'),
            0,
            @SubTotal,
            N'مرتجع مشتريات - ' + @ReturnNumber,
            @ReturnNumber
        );

        -- دائن: عكس ضريبة المدخلات
        IF @TaxAmount > 0
        BEGIN
            INSERT INTO JournalEntryDetails (
                JournalID,
                LineNumber,
                AccountID,
                DebitAmount,
                CreditAmount,
                Description,
                Reference
            )
            VALUES (
                @JournalID,
                3,
                (SELECT AccountID FROM ChartOfAccounts WHERE AccountCode = '1108'),
                0,
                @TaxAmount,
                N'عكس ضريبة مدخلات - ' + @ReturnNumber,
                @ReturnNumber
            );
        END

        UPDATE PurchaseReturns
        SET ModifiedDate = GETDATE()
        WHERE ReturnID = @ReturnID;

        -- تحديث الأرصدة المخزنة في شجرة الحسابات مع التجميع للأصول،
        -- بنفس نمط sp_CreatePurchaseInvoiceJournal.
        ;WITH DirectDeltas AS
        (
            SELECT AccountID, SUM(DebitAmount-CreditAmount) AS Delta
            FROM dbo.JournalEntryDetails
            WHERE JournalID=@JournalID
            GROUP BY AccountID
        ),
        AccountTree AS
        (
            SELECT coa.AccountID, coa.ParentAccountID, d.Delta
            FROM DirectDeltas d
            INNER JOIN dbo.ChartOfAccounts coa ON coa.AccountID=d.AccountID
            UNION ALL
            SELECT parent.AccountID, parent.ParentAccountID, tree.Delta
            FROM AccountTree tree
            INNER JOIN dbo.ChartOfAccounts parent ON parent.AccountID=tree.ParentAccountID
        ),
        RolledDeltas AS
        (
            SELECT AccountID, SUM(Delta) AS Delta
            FROM AccountTree
            GROUP BY AccountID
        )
        UPDATE coa
        SET CurrentBalance=ISNULL(coa.CurrentBalance,0)+rd.Delta
        FROM dbo.ChartOfAccounts coa
        INNER JOIN RolledDeltas rd ON rd.AccountID=coa.AccountID
        OPTION (MAXRECURSION 20);

        COMMIT TRANSACTION;

        SELECT @JournalID AS JournalID, @JournalNumber AS JournalNumber;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_CreateSalesInvoiceJournal
-- ============================================================
CREATE PROCEDURE dbo.sp_CreateSalesInvoiceJournal
    @InvoiceID INT,
    @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE
            @JournalID INT,
            @JournalNumber NVARCHAR(50),
            @CustomerID INT,
            @SubTotal DECIMAL(18,2),
            @DiscountAmount DECIMAL(18,2),
            @TaxAmount DECIMAL(18,2),
            @TotalAmount DECIMAL(18,2),
            @TotalCost DECIMAL(18,2),
            @ShippingCost DECIMAL(18,2),
            @OtherCosts DECIMAL(18,2),
            @NetSalesAmount DECIMAL(18,2),
            @InvoiceNumber NVARCHAR(50),
            @InvoiceDate DATE,
            @CustomerName NVARCHAR(200),
            @FiscalYearID INT,
            @PeriodID INT,
            @PeriodCount INT,
            @ReceivableAccountID INT,
            @RevenueAccountID INT,
            @OutputVatAccountID INT,
            @CogsAccountID INT,
            @InventoryAccountID INT,
            @DebitTotal DECIMAL(18,2),
            @CreditTotal DECIMAL(18,2),
            @LineNumber INT = 1,
            @PostedByEmployeeID INT;

        SELECT
            @CustomerID = si.CustomerID,
            @SubTotal = ISNULL(si.SubTotal,0),
            @DiscountAmount = ISNULL(si.DiscountAmount,0),
            @TaxAmount = ISNULL(si.TaxAmount,0),
            @TotalAmount = ISNULL(si.TotalAmount,0),
            @TotalCost = ISNULL(si.TotalCost,0),
            @ShippingCost = ISNULL(si.ShippingCost,0),
            @OtherCosts = ISNULL(si.OtherCosts,0),
            @InvoiceNumber = si.InvoiceNumber,
            @InvoiceDate = CONVERT(date,si.InvoiceDate),
            @CustomerName = ISNULL(c.CustomerNameAr,N'')
        FROM dbo.SalesInvoices si WITH (UPDLOCK,HOLDLOCK)
        INNER JOIN dbo.Customers c ON c.CustomerID=si.CustomerID
        WHERE si.InvoiceID=@InvoiceID;

        IF @InvoiceNumber IS NULL OR @CustomerID IS NULL
            THROW 51101, N'فاتورة المبيعات أو العميل غير موجود؛ لم يُنشأ القيد.', 1;

        IF EXISTS (
            SELECT 1
            FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
            WHERE SourceDocType=N'SI'
              AND (SourceDocID=@InvoiceID OR SourceDocNumber=@InvoiceNumber)
        )
            THROW 51102, N'يوجد قيد مبيعات سابق لهذه الفاتورة؛ أوقفنا التكرار.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.SalesInvoiceDetails WHERE InvoiceID=@InvoiceID)
            THROW 51111, N'لا تحتوي فاتورة المبيعات على تفاصيل؛ لم يُنشأ القيد.', 1;

        IF ABS(@ShippingCost) > 0.01 OR ABS(@OtherCosts) > 0.01
            THROW 51103, N'لا يمكن ترحيل الشحن أو التكاليف الأخرى قبل اعتماد تصنيفها المحاسبي/الضريبي؛ لم يُنشأ القيد.', 1;

        SET @NetSalesAmount = @SubTotal - @DiscountAmount;
        IF @SubTotal < 0 OR @DiscountAmount < 0 OR @TaxAmount < 0 OR @TotalAmount < 0
           OR @TotalCost < 0 OR @NetSalesAmount < 0
            THROW 51104, N'توجد مبالغ سالبة أو خصم يتجاوز صافي المبيعات؛ لم يُنشأ القيد.', 1;

        IF ABS(@TotalAmount - (@NetSalesAmount + @TaxAmount)) > 0.01
            THROW 51105, N'إجمالي فاتورة المبيعات لا يساوي صافي المبيعات بعد الخصم مضافاً إليه ضريبة المخرجات؛ راجع الإجماليات.', 1;

        SELECT @PeriodCount=COUNT(1)
        FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
        INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK)
            ON ap.FiscalYearID=fy.FiscalYearID
        WHERE @InvoiceDate BETWEEN CONVERT(date,fy.StartDate) AND CONVERT(date,fy.EndDate)
          AND @InvoiceDate BETWEEN CONVERT(date,ap.StartDate) AND CONVERT(date,ap.EndDate)
          AND fy.YearStatus=1 AND ap.PeriodStatus=1;

        IF ISNULL(@PeriodCount,0) <> 1
            THROW 51106, N'لا توجد سنة وفترة مالية مفتوحتان وحيدتان تغطيان تاريخ فاتورة المبيعات؛ لم يُنشأ القيد.', 1;

        SELECT TOP (1)
            @FiscalYearID=fy.FiscalYearID,
            @PeriodID=ap.PeriodID
        FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
        INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK)
            ON ap.FiscalYearID=fy.FiscalYearID
        WHERE @InvoiceDate BETWEEN CONVERT(date,fy.StartDate) AND CONVERT(date,fy.EndDate)
          AND @InvoiceDate BETWEEN CONVERT(date,ap.StartDate) AND CONVERT(date,ap.EndDate)
          AND fy.YearStatus=1 AND ap.PeriodStatus=1;

        DECLARE @RequiredSalesAccounts TABLE
        (
            AccountCode NVARCHAR(50) PRIMARY KEY,
            ExpectedAccountType INT NOT NULL
        );
        DECLARE @SalesAccounts TABLE (AccountCode NVARCHAR(50) PRIMARY KEY, AccountID INT NOT NULL);
        INSERT INTO @RequiredSalesAccounts(AccountCode,ExpectedAccountType)
        VALUES (N'1103',1),(N'41',4);
        IF @TaxAmount > 0 INSERT INTO @RequiredSalesAccounts(AccountCode,ExpectedAccountType) VALUES (N'2105',2);
        IF @TotalCost > 0 INSERT INTO @RequiredSalesAccounts(AccountCode,ExpectedAccountType) VALUES (N'5103',5),(N'110504',1);

        INSERT INTO @SalesAccounts(AccountCode,AccountID)
        SELECT r.AccountCode, MIN(coa.AccountID)
        FROM @RequiredSalesAccounts r
        LEFT JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK)
            ON coa.AccountCode=r.AccountCode
        GROUP BY r.AccountCode,r.ExpectedAccountType
        HAVING COUNT(coa.AccountID)=1
           AND MIN(CONVERT(int,ISNULL(coa.IsActive,0)))=1
           AND MIN(CONVERT(int,ISNULL(coa.AccountType,0)))=r.ExpectedAccountType
           AND
           (
               MIN(CONVERT(int,ISNULL(coa.IsDetailAccount,0)))=1
               OR
               (
                   r.AccountCode=N'1103'
                   AND MIN(CONVERT(int,ISNULL(coa.IsSystemAccount,0)))=1
                   AND MIN(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(coa.LinkedEntityType,N''))))=N'CUSTOMER' THEN 1 ELSE 0 END)=1
               )
               OR
               (
                   r.AccountCode=N'41'
                   AND r.ExpectedAccountType=4
               )
           );

        IF (SELECT COUNT(1) FROM @SalesAccounts) <> (SELECT COUNT(1) FROM @RequiredSalesAccounts)
            THROW 51107, N'حسابات قيد المبيعات غير موجودة/غير نشطة/مكررة أو نوعها/ربطها النظامي غير مطابق؛ لم يُنشأ القيد.', 1;

        -- PostedBy references Employees.EmployeeID, while @UserID references SystemUsers.UserID.
        -- Resolve the employee linked to the currently logged-in user; keep CreatedBy as @UserID.
        SELECT @PostedByEmployeeID = su.EmployeeID
        FROM dbo.SystemUsers su
        INNER JOIN dbo.Employees e ON e.EmployeeID = su.EmployeeID
        WHERE su.UserID = @UserID
          AND su.EmployeeID > 0;

        IF @PostedByEmployeeID IS NULL
            THROW 51012, N'حساب المستخدم الحالي غير مرتبط بموظف صالح؛ لم يُنشأ القيد.', 1;

        SELECT @ReceivableAccountID=AccountID FROM @SalesAccounts WHERE AccountCode=N'1103';
        SELECT @RevenueAccountID=AccountID FROM @SalesAccounts WHERE AccountCode=N'41';
        SELECT @OutputVatAccountID=AccountID FROM @SalesAccounts WHERE AccountCode=N'2105';
        SELECT @CogsAccountID=AccountID FROM @SalesAccounts WHERE AccountCode=N'5103';
        SELECT @InventoryAccountID=AccountID FROM @SalesAccounts WHERE AccountCode=N'110504';

        SET @DebitTotal=@TotalAmount+@TotalCost;
        SET @CreditTotal=@NetSalesAmount+@TaxAmount+@TotalCost;
        IF ABS(@DebitTotal-@CreditTotal)>0.01
            THROW 51108, N'قيد فاتورة المبيعات غير متوازن بعد الخصم؛ لم يُنشأ القيد.', 1;

        EXEC dbo.sp_GetNextNumber N'JV', @JournalNumber OUTPUT;
        IF NULLIF(@JournalNumber,N'') IS NULL
            THROW 51109, N'تعذر توليد رقم القيد JV؛ لم يُنشأ القيد.', 1;

        INSERT INTO dbo.JournalEntries
        (
            JournalNumber,JournalDate,FiscalYearID,PeriodID,JournalType,
            Description,Reference,TotalDebit,TotalCredit,
            SourceDocType,SourceDocID,SourceDocNumber,
            IsAutoGenerated,JournalStatus,PostedBy,PostedDate,
            CreatedBy,CreatedDate
        )
        VALUES
        (
            @JournalNumber,@InvoiceDate,@FiscalYearID,@PeriodID,3,
            N'فاتورة مبيعات رقم '+@InvoiceNumber+N' - '+@CustomerName,
            @InvoiceNumber,@DebitTotal,@CreditTotal,
            N'SI',@InvoiceID,@InvoiceNumber,
            1,2,@PostedByEmployeeID,GETDATE(),@UserID,GETDATE()
        );

        SET @JournalID=CONVERT(int,SCOPE_IDENTITY());

        INSERT INTO dbo.JournalEntryDetails
        (
            JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,EntityType,EntityID
        )
        VALUES
        (
            @JournalID,@LineNumber,@ReceivableAccountID,@TotalAmount,0,
            @CustomerName,N'Customer',@CustomerID
        );
        SET @LineNumber += 1;

        INSERT INTO dbo.JournalEntryDetails
        (
            JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
        )
        VALUES
        (
            @JournalID,@LineNumber,@RevenueAccountID,0,@NetSalesAmount,
            N'إيرادات مبيعات - '+@InvoiceNumber,@InvoiceNumber
        );
        SET @LineNumber += 1;

        IF @TaxAmount > 0
        BEGIN
            INSERT INTO dbo.JournalEntryDetails
            (
                JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
            )
            VALUES
            (
                @JournalID,@LineNumber,@OutputVatAccountID,0,@TaxAmount,
                N'ضريبة مخرجات - '+@InvoiceNumber,@InvoiceNumber
            );
            SET @LineNumber += 1;
        END;

        IF @TotalCost > 0
        BEGIN
            INSERT INTO dbo.JournalEntryDetails
            (
                JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
            )
            VALUES
            (
                @JournalID,@LineNumber,@CogsAccountID,@TotalCost,0,
                N'تكلفة بضاعة مباعة - '+@InvoiceNumber,@InvoiceNumber
            );
            SET @LineNumber += 1;

            INSERT INTO dbo.JournalEntryDetails
            (
                JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,Reference
            )
            VALUES
            (
                @JournalID,@LineNumber,@InventoryAccountID,0,@TotalCost,
                N'خصم مخزون - '+@InvoiceNumber,@InvoiceNumber
            );
        END;

        DECLARE @SavedDebit DECIMAL(18,2), @SavedCredit DECIMAL(18,2), @SavedLineCount INT;
        SELECT
            @SavedDebit=ISNULL(SUM(DebitAmount),0),
            @SavedCredit=ISNULL(SUM(CreditAmount),0),
            @SavedLineCount=COUNT(1)
        FROM dbo.JournalEntryDetails
        WHERE JournalID=@JournalID;

        IF @SavedLineCount < 2 OR ABS(@SavedDebit-@SavedCredit)>0.01
            THROW 51110, N'فشل التحقق من توازن قيد فاتورة المبيعات؛ أُلغيت المعاملة.', 1;

        ;WITH DirectDeltas AS
        (
            SELECT AccountID,SUM(DebitAmount-CreditAmount) AS Delta
            FROM dbo.JournalEntryDetails
            WHERE JournalID=@JournalID
            GROUP BY AccountID
        ),
        AccountTree AS
        (
            SELECT coa.AccountID,coa.ParentAccountID,d.Delta
            FROM DirectDeltas d
            INNER JOIN dbo.ChartOfAccounts coa ON coa.AccountID=d.AccountID
            UNION ALL
            SELECT parent.AccountID,parent.ParentAccountID,tree.Delta
            FROM AccountTree tree
            INNER JOIN dbo.ChartOfAccounts parent ON parent.AccountID=tree.ParentAccountID
        ),
        RolledDeltas AS
        (
            SELECT AccountID,SUM(Delta) AS Delta
            FROM AccountTree
            GROUP BY AccountID
        )
        UPDATE coa
        SET CurrentBalance=ISNULL(coa.CurrentBalance,0)+rd.Delta
        FROM dbo.ChartOfAccounts coa
        INNER JOIN RolledDeltas rd ON rd.AccountID=coa.AccountID
        OPTION (MAXRECURSION 20);

        COMMIT TRANSACTION;
        SELECT @JournalID AS JournalID,@JournalNumber AS JournalNumber;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;


GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_ExecuteStockCountAdjustments
-- ============================================================
-- =====================================================================
-- إصلاح 14: اعتماد تسويات الجرد ينهار بخطأ مفتاح خارجي
-- المشكلة: sp_ExecuteStockCountAdjustments يكتب ApprovedBy = @UserID
-- برقم المستخدم، والعمود مرتبط بجدول الموظفين — فينهار الاعتماد عند
-- المدير (لا موظف برقمه)، وقد ينجح صامتًا باسم موظف آخر عند تطابق
-- الرقمين مصادفة. نفس عائلة عيب اعتماد إذن صرف المواد (تصحيح 10).
-- الإصلاح: حلّ رقم موظف المعتمِد من SystemUsers داخل الإجراء (نفس
-- نمط إصلاح إجراء قيد المبيعات)، والرفض الصريح إن لم يكن مرتبطًا.
-- التنفيذ: شغّل هذا الملف مرة واحدة على قاعدة البيانات (SSMS).
-- =====================================================================

CREATE   PROCEDURE dbo.sp_ExecuteStockCountAdjustments
    @CountID INT,
    @UserID INT = NULL,
    @BatchAwareVersion TINYINT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- إصلاح 14: «اعتمد بواسطة» مرتبط بجدول الموظفين؛ يُحلّ رقم الموظف من حساب المستخدم
    DECLARE @ApprovedByEmployeeID INT =
        (SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID);

    IF ISNULL(@BatchAwareVersion, 0) <> 1
    BEGIN
        RAISERROR(N'Unsupported stock-count adjustment version.', 16, 1);
        RETURN;
    END;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @WarehouseID INT;
        DECLARE @CountNumber NVARCHAR(100);
        DECLARE @CountStatus TINYINT;

        SELECT
            @WarehouseID = sc.WarehouseID,
            @CountNumber = sc.CountNumber,
            @CountStatus = sc.CountStatus
        FROM dbo.StockCounts AS sc WITH (UPDLOCK, HOLDLOCK)
        WHERE sc.CountID = @CountID;

        IF @CountNumber IS NULL
        BEGIN
            RAISERROR(N'أمر الجرد غير موجود.', 16, 1);
        END;

        IF @CountStatus <> 3
        BEGIN
            RAISERROR(N'لا يمكن اعتماد جرد غير مكتمل أو سبق اعتماده.', 16, 1);
        END;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
            WHERE scd.CountID = @CountID
              AND ISNULL(scd.AdjustmentApproved, 0) = 1
              AND
              (
                  scd.CountedQty IS NULL
                  OR scd.SystemQty IS NULL
                  OR scd.CountedQty < 0
              )
        )
        BEGIN
            RAISERROR(N'يوجد سطر معتمد بلا كمية فعلية/دفترية أو بكمية سالبة؛ أوقفنا التسوية.', 16, 1);
        END;

        DECLARE @AffectedItems TABLE
        (
            ItemID INT NOT NULL PRIMARY KEY
        );

        INSERT INTO @AffectedItems (ItemID)
        SELECT DISTINCT scd.ItemID
        FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
        WHERE scd.CountID = @CountID
          AND ISNULL(scd.AdjustmentApproved, 0) = 1
          AND scd.CountedQty IS NOT NULL
          AND scd.CountedQty <> ISNULL(scd.SystemQty, 0);

        IF NOT EXISTS (SELECT 1 FROM @AffectedItems)
        BEGIN
            RAISERROR(N'لا توجد فروقات جرد معتمدة للتطبيق.', 16, 1);
        END;

        IF EXISTS
        (
            SELECT 1
            FROM @AffectedItems AS ai
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                WHERE ib.ItemID = ai.ItemID
                  AND ib.WarehouseID = @WarehouseID
            )
        )
        BEGIN
            RAISERROR(N'تعذر اعتماد الجرد: لا يوجد رصيد إجمالي للصنف/المخزن المتأثر.', 16, 1);
        END;

        /* A count must have at most one line for a given item/batch (NULL = untracked item). */
        IF EXISTS
        (
            SELECT scd.ItemID, scd.BatchNumber
            FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN @AffectedItems AS ai
                ON ai.ItemID = scd.ItemID
            WHERE scd.CountID = @CountID
            GROUP BY scd.ItemID, scd.BatchNumber
            HAVING COUNT(*) > 1
        )
        BEGIN
            RAISERROR(N'يوجد تكرار لسطر الصنف/الباتش داخل الجرد؛ أوقفنا الاعتماد.', 16, 1);
        END;

        /*
          For batch-managed items, require a complete, unchanged one-to-one snapshot:
          every current batch must have a detail line and every detail line must still
          refer to the same batch quantity/cost captured when the count was loaded.
        */
        IF EXISTS
        (
            SELECT 1
            FROM @AffectedItems AS ai
            WHERE EXISTS
            (
                SELECT 1
                FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                WHERE bb.ItemID = ai.ItemID
                  AND bb.WarehouseID = @WarehouseID
            )
              AND
              (
                  ISNULL
                  (
                      (
                          SELECT SUM(bb.CurrentQty)
                          FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                          WHERE bb.ItemID = ai.ItemID
                            AND bb.WarehouseID = @WarehouseID
                      ),
                      0
                  )
                  <>
                  ISNULL
                  (
                      (
                          SELECT ib.CurrentQty
                          FROM dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                          WHERE ib.ItemID = ai.ItemID
                            AND ib.WarehouseID = @WarehouseID
                      ),
                      0
                  )
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                      WHERE bb.ItemID = ai.ItemID
                        AND bb.WarehouseID = @WarehouseID
                        AND
                        (
                            bb.CurrentQty < 0
                            OR NULLIF(LTRIM(RTRIM(bb.BatchNumber)), N'') IS NULL
                        )
                  )
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
                      LEFT JOIN dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                        ON bb.ItemID = scd.ItemID
                       AND bb.WarehouseID = @WarehouseID
                       AND bb.BatchNumber = scd.BatchNumber
                      WHERE scd.CountID = @CountID
                        AND scd.ItemID = ai.ItemID
                        AND
                        (
                            scd.BatchNumber IS NULL
                            OR NULLIF(LTRIM(RTRIM(scd.BatchNumber)), N'') IS NULL
                            OR scd.SystemQty IS NULL
                            OR bb.BatchBalanceID IS NULL
                            OR scd.SystemQty <> bb.CurrentQty
                            OR ISNULL(scd.UnitCost, 0) <> ISNULL(bb.UnitCost, 0)
                        )
                  )
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                      WHERE bb.ItemID = ai.ItemID
                        AND bb.WarehouseID = @WarehouseID
                        AND NOT EXISTS
                        (
                            SELECT 1
                            FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
                            WHERE scd.CountID = @CountID
                              AND scd.ItemID = ai.ItemID
                              AND scd.BatchNumber = bb.BatchNumber
                        )
                  )
              )
        )
        BEGIN
            RAISERROR(N'تعذر اعتماد الجرد: تغيرت طبقات FIFO منذ تحميل الجرد، أو ينقص/يزيد سطر باتش، أو لا يطابق مجموع الباتشات الرصيد الإجمالي. أعد تحميل/مراجعة الجرد بعد تسوية الحركة.', 16, 1);
        END;

        /* Non-batch items keep exactly one aggregate line and must be unchanged since load. */
        IF EXISTS
        (
            SELECT 1
            FROM @AffectedItems AS ai
            INNER JOIN dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                ON ib.ItemID = ai.ItemID
               AND ib.WarehouseID = @WarehouseID
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                WHERE bb.ItemID = ai.ItemID
                  AND bb.WarehouseID = @WarehouseID
            )
              AND
              (
                  (SELECT COUNT(*)
                   FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
                   WHERE scd.CountID = @CountID
                     AND scd.ItemID = ai.ItemID) <> 1
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
                      WHERE scd.CountID = @CountID
                        AND scd.ItemID = ai.ItemID
                        AND
                        (
                            scd.BatchNumber IS NOT NULL
                            OR scd.SystemQty IS NULL
                            OR scd.SystemQty <> ib.CurrentQty
                            OR ISNULL(scd.UnitCost, 0) <> ISNULL(ib.AverageCost, 0)
                        )
                  )
              )
        )
        BEGIN
            RAISERROR(N'تعذر اعتماد الجرد: تغير رصيد الصنف غير المتتبع منذ تحميل الجرد أو تفاصيله غير مكتملة.', 16, 1);
        END;

        DECLARE @Adjustments TABLE
        (
            CountDetailID INT NOT NULL PRIMARY KEY,
            LineNumber INT NOT NULL,
            ItemID INT NOT NULL,
            UnitID INT NOT NULL,
            SystemQty DECIMAL(18,4) NOT NULL,
            CountedQty DECIMAL(18,4) NOT NULL,
            BatchNumber NVARCHAR(100) NULL,
            ExpiryDate DATE NULL,
            Variance DECIMAL(18,4) NOT NULL,
            UnitCost DECIMAL(18,4) NOT NULL,
            CostBefore DECIMAL(18,4) NOT NULL,
            BalanceBefore DECIMAL(18,4) NOT NULL,
            BalanceAfter DECIMAL(18,4) NOT NULL
        );

        ;WITH SelectedAdjustments AS
        (
            SELECT
                scd.CountDetailID,
                scd.LineNumber,
                scd.ItemID,
                scd.UnitID,
                ISNULL(scd.SystemQty, 0) AS SystemQty,
                scd.CountedQty,
                scd.BatchNumber,
                bb.ExpiryDate,
                CAST(scd.CountedQty - ISNULL(scd.SystemQty, 0) AS DECIMAL(18,4)) AS Variance,
                CAST
                (
                    CASE
                        WHEN scd.BatchNumber IS NOT NULL THEN ISNULL(bb.UnitCost, 0)
                        ELSE ISNULL(ib.AverageCost, 0)
                    END
                    AS DECIMAL(18,4)
                ) AS UnitCost,
                CAST(ISNULL(ib.AverageCost, 0) AS DECIMAL(18,4)) AS CostBefore,
                CAST(ib.CurrentQty AS DECIMAL(18,4)) AS ItemBalanceBefore
            FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN @AffectedItems AS ai
                ON ai.ItemID = scd.ItemID
            INNER JOIN dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                ON ib.ItemID = scd.ItemID
               AND ib.WarehouseID = @WarehouseID
            LEFT JOIN dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                ON bb.ItemID = scd.ItemID
               AND bb.WarehouseID = @WarehouseID
               AND bb.BatchNumber = scd.BatchNumber
            WHERE scd.CountID = @CountID
              AND ISNULL(scd.AdjustmentApproved, 0) = 1
              AND scd.CountedQty IS NOT NULL
              AND scd.CountedQty <> ISNULL(scd.SystemQty, 0)
        ),
        RunningAdjustments AS
        (
            SELECT
                sa.*,
                SUM(sa.Variance) OVER
                (
                    PARTITION BY sa.ItemID
                    ORDER BY sa.LineNumber, sa.CountDetailID
                    ROWS UNBOUNDED PRECEDING
                ) AS RunningVariance
            FROM SelectedAdjustments AS sa
        )
        INSERT INTO @Adjustments
        (
            CountDetailID, LineNumber, ItemID, UnitID, SystemQty, CountedQty,
            BatchNumber, ExpiryDate, Variance, UnitCost, CostBefore,
            BalanceBefore, BalanceAfter
        )
        SELECT
            CountDetailID,
            LineNumber,
            ItemID,
            UnitID,
            SystemQty,
            CountedQty,
            BatchNumber,
            ExpiryDate,
            Variance,
            UnitCost,
            CostBefore,
            CAST(ItemBalanceBefore + RunningVariance - Variance AS DECIMAL(18,4)),
            CAST(ItemBalanceBefore + RunningVariance AS DECIMAL(18,4))
        FROM RunningAdjustments;

        DECLARE @ExpectedBatchUpdates INT;
        SELECT @ExpectedBatchUpdates = COUNT(*)
        FROM @Adjustments
        WHERE BatchNumber IS NOT NULL;

        UPDATE bb
        SET bb.CurrentQty = a.CountedQty,
            bb.ModifiedDate = GETDATE()
        FROM dbo.InventoryBatchBalance AS bb
        INNER JOIN @Adjustments AS a
            ON a.ItemID = bb.ItemID
           AND a.BatchNumber = bb.BatchNumber
        WHERE bb.WarehouseID = @WarehouseID
          AND a.BatchNumber IS NOT NULL
          AND bb.CurrentQty = a.SystemQty
          AND ISNULL(bb.UnitCost, 0) = a.UnitCost;

        IF @@ROWCOUNT <> @ExpectedBatchUpdates
        BEGIN
            RAISERROR(N'تغير أحد أرصدة الباتش أثناء اعتماد الجرد؛ ألغينا العملية كاملة.', 16, 1);
        END;

        DECLARE @ExpectedItemUpdates INT;
        SELECT @ExpectedItemUpdates = COUNT(*) FROM @AffectedItems;

        ;WITH ItemDeltas AS
        (
            SELECT ItemID, SUM(Variance) AS TotalVariance
            FROM @Adjustments
            GROUP BY ItemID
        )
        UPDATE ib
        SET ib.CurrentQty = CAST(ib.CurrentQty + d.TotalVariance AS DECIMAL(18,4)),
            ib.LastCountDate = CAST(GETDATE() AS DATE),
            ib.LastMovementDate = GETDATE(),
            ib.ModifiedDate = GETDATE()
        FROM dbo.InventoryBalance AS ib
        INNER JOIN ItemDeltas AS d
            ON d.ItemID = ib.ItemID
        WHERE ib.WarehouseID = @WarehouseID;

        IF @@ROWCOUNT <> @ExpectedItemUpdates
        BEGIN
            RAISERROR(N'تعذر تحديث الرصيد الإجمالي لأحد الأصناف؛ ألغينا العملية كاملة.', 16, 1);
        END;

        /* Final invariant: every affected batch-managed item must still reconcile. */
        IF EXISTS
        (
            SELECT 1
            FROM @AffectedItems AS ai
            INNER JOIN dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                ON ib.ItemID = ai.ItemID
               AND ib.WarehouseID = @WarehouseID
            WHERE EXISTS
            (
                SELECT 1
                FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                WHERE bb.ItemID = ai.ItemID
                  AND bb.WarehouseID = @WarehouseID
            )
              AND
              (
                  ib.CurrentQty < 0
                  OR ISNULL
                  (
                      (
                          SELECT SUM(bb.CurrentQty)
                          FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                          WHERE bb.ItemID = ai.ItemID
                            AND bb.WarehouseID = @WarehouseID
                      ),
                      0
                  ) <> ib.CurrentQty
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                      WHERE bb.ItemID = ai.ItemID
                        AND bb.WarehouseID = @WarehouseID
                        AND bb.CurrentQty < 0
                  )
              )
        )
        BEGIN
            RAISERROR(N'فشل فحص المطابقة النهائي بين InventoryBalance وInventoryBatchBalance؛ ألغينا العملية كاملة.', 16, 1);
        END;

        /* Insert one movement per counted batch line in deterministic ledger order. */
        DECLARE @MoveCountDetailID INT;
        DECLARE @MoveItemID INT;
        DECLARE @MoveUnitID INT;
        DECLARE @MoveBatchNumber NVARCHAR(100);
        DECLARE @MoveExpiryDate DATE;
        DECLARE @MoveVariance DECIMAL(18,4);
        DECLARE @MoveUnitCost DECIMAL(18,4);
        DECLARE @MoveCostBefore DECIMAL(18,4);
        DECLARE @MoveBalanceBefore DECIMAL(18,4);
        DECLARE @MoveBalanceAfter DECIMAL(18,4);

        DECLARE adjustment_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT
                CountDetailID, ItemID, UnitID, BatchNumber, ExpiryDate,
                Variance, UnitCost, CostBefore, BalanceBefore, BalanceAfter
            FROM @Adjustments
            ORDER BY ItemID, LineNumber, CountDetailID;

        OPEN adjustment_cursor;
        FETCH NEXT FROM adjustment_cursor INTO
            @MoveCountDetailID, @MoveItemID, @MoveUnitID, @MoveBatchNumber,
            @MoveExpiryDate, @MoveVariance, @MoveUnitCost, @MoveCostBefore,
            @MoveBalanceBefore, @MoveBalanceAfter;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            INSERT INTO dbo.InventoryTransactions
            (
                TransactionDate, TransactionType, ItemID, WarehouseID,
                Quantity, UnitID, UnitCost, BalanceBefore, BalanceAfter,
                CostBefore, CostAfter, BatchNumber, ExpiryDate,
                SourceDocType, SourceDocID, SourceDocNumber, SourceDocLineID,
                Notes, CreatedBy, CreatedDate
            )
            VALUES
            (
                GETDATE(),
                CASE WHEN @MoveVariance > 0 THEN 9 ELSE 10 END,
                @MoveItemID,
                @WarehouseID,
                @MoveVariance,
                @MoveUnitID,
                @MoveUnitCost,
                @MoveBalanceBefore,
                @MoveBalanceAfter,
                @MoveCostBefore,
                @MoveCostBefore,
                @MoveBatchNumber,
                @MoveExpiryDate,
                N'SC',
                @CountID,
                @CountNumber,
                @MoveCountDetailID,
                CASE WHEN @MoveVariance > 0
                     THEN N'تسوية زيادة — جرد'
                     ELSE N'تسوية نقص — جرد'
                END,
                @UserID,
                GETDATE()
            );

            FETCH NEXT FROM adjustment_cursor INTO
                @MoveCountDetailID, @MoveItemID, @MoveUnitID, @MoveBatchNumber,
                @MoveExpiryDate, @MoveVariance, @MoveUnitCost, @MoveCostBefore,
                @MoveBalanceBefore, @MoveBalanceAfter;
        END;

        CLOSE adjustment_cursor;
        DEALLOCATE adjustment_cursor;

        IF @ApprovedByEmployeeID IS NULL
        BEGIN
            RAISERROR(N'لا يمكن اعتماد الجرد لأن المستخدم الحالي غير مرتبط بموظف.', 16, 1);
        END;

        UPDATE dbo.StockCounts
        SET CountStatus = 4,
            ApprovedBy = @ApprovedByEmployeeID,
            ApprovedDate = GETDATE(),
            ModifiedBy = @UserID,
            ModifiedDate = GETDATE()
        WHERE CountID = @CountID
          AND CountStatus = 3;

        IF @@ROWCOUNT <> 1
        BEGIN
            RAISERROR(N'تغيرت حالة أمر الجرد أثناء الاعتماد؛ ألغينا العملية كاملة.', 16, 1);
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        DECLARE @AdjustErrorMessage NVARCHAR(4000);
        SET @AdjustErrorMessage = ERROR_MESSAGE();
        RAISERROR(N'%s', 16, 1, @AdjustErrorMessage);
        RETURN;
    END CATCH;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_ExecuteStockTransfer
-- ============================================================


-- =============================================
-- 7) SP: تنفيذ تحويل المخزون
-- =============================================
CREATE   PROCEDURE dbo.sp_ExecuteStockTransfer
    @TransferID INT,
    @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @FromWarehouseID INT, @ToWarehouseID INT, @TransferNumber NVARCHAR(50);

        SELECT @FromWarehouseID = FromWarehouseID,
               @ToWarehouseID = ToWarehouseID,
               @TransferNumber = TransferNumber
        FROM dbo.StockTransfers
        WHERE TransferID = @TransferID;

        -- لكل سطر في التحويل
        DECLARE @ItemID INT, @Qty DECIMAL(18,4), @UnitCost DECIMAL(18,4),
                @UnitID INT, @BatchNumber NVARCHAR(50), @ExpiryDate DATE;

        DECLARE transfer_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT std.ItemID, std.Quantity, std.UnitCost, std.UnitID,
                   std.BatchNumber, std.ExpiryDate
            FROM dbo.StockTransferDetails std
            WHERE std.TransferID = @TransferID;

        OPEN transfer_cursor;
        FETCH NEXT FROM transfer_cursor INTO @ItemID, @Qty, @UnitCost, @UnitID, @BatchNumber, @ExpiryDate;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- التحقق من الرصيد
            DECLARE @CurrentQty DECIMAL(18,4);
            SELECT @CurrentQty = ISNULL(CurrentQty, 0)
            FROM dbo.InventoryBalance
            WHERE ItemID = @ItemID AND WarehouseID = @FromWarehouseID;

            IF @CurrentQty < @Qty
                RAISERROR(N'رصيد غير كافي للصنف %d في المخزن المصدر', 16, 1, @ItemID);

            -- خصم من المخزن المصدر
            UPDATE dbo.InventoryBalance
            SET CurrentQty = CurrentQty - @Qty,
                LastMovementDate = GETDATE(), ModifiedDate = GETDATE()
            WHERE ItemID = @ItemID AND WarehouseID = @FromWarehouseID;

            -- حركة صادر
            INSERT INTO dbo.InventoryTransactions
                (TransactionDate, TransactionType, ItemID, WarehouseID, Quantity, UnitID, UnitCost,
                 BalanceBefore, BalanceAfter, BatchNumber, ExpiryDate,
                 SourceDocType, SourceDocID, SourceDocNumber, Notes, CreatedBy, CreatedDate)
            VALUES
                (GETDATE(), 7, @ItemID, @FromWarehouseID, -@Qty, @UnitID, @UnitCost,
                 @CurrentQty, @CurrentQty - @Qty, @BatchNumber, @ExpiryDate,
                 N'ST', @TransferID, @TransferNumber, N'تحويل صادر', @UserID, GETDATE());

            -- إضافة للمخزن المستقبل
            IF EXISTS (SELECT 1 FROM dbo.InventoryBalance WHERE ItemID = @ItemID AND WarehouseID = @ToWarehouseID)
            BEGIN
                DECLARE @ToCurrentQty DECIMAL(18,4);
                SELECT @ToCurrentQty = CurrentQty FROM dbo.InventoryBalance
                WHERE ItemID = @ItemID AND WarehouseID = @ToWarehouseID;

                UPDATE dbo.InventoryBalance
                SET CurrentQty = CurrentQty + @Qty,
                    LastMovementDate = GETDATE(), ModifiedDate = GETDATE()
                WHERE ItemID = @ItemID AND WarehouseID = @ToWarehouseID;

                -- حركة وارد
                INSERT INTO dbo.InventoryTransactions
                    (TransactionDate, TransactionType, ItemID, WarehouseID, Quantity, UnitID, UnitCost,
                     BalanceBefore, BalanceAfter, BatchNumber, ExpiryDate,
                     SourceDocType, SourceDocID, SourceDocNumber, Notes, CreatedBy, CreatedDate)
                VALUES
                    (GETDATE(), 8, @ItemID, @ToWarehouseID, @Qty, @UnitID, @UnitCost,
                     @ToCurrentQty, @ToCurrentQty + @Qty, @BatchNumber, @ExpiryDate,
                     N'ST', @TransferID, @TransferNumber, N'تحويل وارد', @UserID, GETDATE());
            END
            ELSE
            BEGIN
                INSERT INTO dbo.InventoryBalance (ItemID, WarehouseID, CurrentQty, AverageCost, LastMovementDate)
                VALUES (@ItemID, @ToWarehouseID, @Qty, @UnitCost, GETDATE());

                INSERT INTO dbo.InventoryTransactions
                    (TransactionDate, TransactionType, ItemID, WarehouseID, Quantity, UnitID, UnitCost,
                     BalanceBefore, BalanceAfter, BatchNumber, ExpiryDate,
                     SourceDocType, SourceDocID, SourceDocNumber, Notes, CreatedBy, CreatedDate)
                VALUES
                    (GETDATE(), 8, @ItemID, @ToWarehouseID, @Qty, @UnitID, @UnitCost,
                     0, @Qty, @BatchNumber, @ExpiryDate,
                     N'ST', @TransferID, @TransferNumber, N'تحويل وارد', @UserID, GETDATE());
            END

            FETCH NEXT FROM transfer_cursor INTO @ItemID, @Qty, @UnitCost, @UnitID, @BatchNumber, @ExpiryDate;
        END

        CLOSE transfer_cursor;
        DEALLOCATE transfer_cursor;

        -- تحديث حالة التحويل
        UPDATE dbo.StockTransfers
        SET TransferStatus = 3, ModifiedBy = @UserID, ModifiedDate = GETDATE()
        WHERE TransferID = @TransferID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_GetNextNumber
-- ============================================================


-- =============================================
-- Stored Procedure: توليد رقم مستند
-- =============================================
CREATE   PROCEDURE [dbo].[sp_GetNextNumber]
    @SequenceCode NVARCHAR(20),
    @NextNumber NVARCHAR(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CurrentNum INT;
    DECLARE @Prefix NVARCHAR(10);
    DECLARE @NumLength INT;
    DECLARE @IncludeYear BIT;
    DECLARE @CurrentYear INT = YEAR(GETDATE());

    UPDATE NumberSequences
    SET @CurrentNum = CurrentNumber = 
        CASE 
            WHEN ResetYearly = 1 AND (LastResetYear IS NULL OR LastResetYear < @CurrentYear)
            THEN 1
            ELSE CurrentNumber + 1
        END,
        @Prefix = Prefix,
        @NumLength = NumberLength,
        @IncludeYear = IncludeYear,
        LastResetYear = @CurrentYear
    WHERE SequenceCode = @SequenceCode;

    IF @IncludeYear = 1
        SET @NextNumber = @Prefix + N'-' + CAST(@CurrentYear AS NVARCHAR(4)) + N'-' + 
                          RIGHT(REPLICATE('0', @NumLength) + CAST(@CurrentNum AS NVARCHAR(10)), @NumLength);
    ELSE
        SET @NextNumber = @Prefix + N'-' + 
                          RIGHT(REPLICATE('0', @NumLength) + CAST(@CurrentNum AS NVARCHAR(10)), @NumLength);
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_GetPRLinesForPO
-- ============================================================


-- =============================================
-- 6) SP: تحميل سطور PR المتبقية لأمر شراء
-- =============================================
CREATE   PROCEDURE sp_GetPRLinesForPO
    @RequestID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT
        RequestDetailID,
        RequestID,
        LineNumber,
        ItemID,
        ItemCode,
        ItemNameAr,
        UnitID,
        UnitName,
        RequestedQty,
        ApprovedQty,
        EstimatedPrice,
        CurrentStock,
        TotalOrderedQty,
        RemainingToOrder,
        IsFullyCovered,
        Purpose
    FROM vw_PRRemainingQty
    WHERE RequestID = @RequestID
      AND RemainingToOrder > 0  -- فقط السطور التي لم تتم تغطيتها
    ORDER BY LineNumber;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_GetReorderItems
-- ============================================================


-- SP: مواد تحتاج إعادة طلب
CREATE   PROCEDURE [dbo].[sp_GetReorderItems]
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        i.ItemCode,
        i.ItemNameAr,
        CASE i.ItemType 
            WHEN 1 THEN N'مادة خام' WHEN 2 THEN N'مادة تعبئة'
            WHEN 3 THEN N'نصف مصنع' WHEN 4 THEN N'منتج تام'
        END AS ItemType,
        u.UnitNameAr,
        ISNULL(ib.TotalQty, 0) AS CurrentStock,
        i.ReorderLevel,
        i.MinStockLevel,
        i.ReorderLevel - ISNULL(ib.TotalQty, 0) AS SuggestedOrderQty,
        i.LastPurchasePrice,
        (i.ReorderLevel - ISNULL(ib.TotalQty, 0)) * ISNULL(i.LastPurchasePrice, 0) AS EstimatedCost
    FROM Items i
    INNER JOIN Units u ON i.PrimaryUnitID = u.UnitID
    LEFT JOIN (
        SELECT ItemID, SUM(CurrentQty) AS TotalQty
        FROM InventoryBalance
        GROUP BY ItemID
    ) ib ON i.ItemID = ib.ItemID
    WHERE i.IsActive = 1
      AND i.ReorderLevel > 0
      AND ISNULL(ib.TotalQty, 0) <= i.ReorderLevel
    ORDER BY (ISNULL(ib.TotalQty, 0) / NULLIF(i.ReorderLevel, 0));
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_GetSellingPrice
-- ============================================================


-- =============================================
-- SP: الحصول على سعر البيع
-- =============================================
CREATE   PROCEDURE [dbo].[sp_GetSellingPrice]
    @ItemID INT,
    @CustomerID INT = NULL,
    @Quantity DECIMAL(18,4) = 1,
    @UnitPrice DECIMAL(18,4) OUTPUT,
    @DiscountPercent DECIMAL(5,2) OUTPUT,
    @NetPrice DECIMAL(18,4) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PriceListID INT;
    DECLARE @CustomerDiscount DECIMAL(5,2) = 0;
    
    IF @CustomerID IS NOT NULL
    BEGIN
        SELECT @PriceListID = COALESCE(c.PriceListID, cg.PriceListID),
               @CustomerDiscount = COALESCE(c.DiscountPercent, cg.DiscountPercent, 0)
        FROM Customers c
        LEFT JOIN CustomerGroups cg ON c.CustomerGroupID = cg.GroupID
        WHERE c.CustomerID = @CustomerID;
    END
    
    IF @PriceListID IS NOT NULL
    BEGIN
        SELECT TOP 1 
            @UnitPrice = pld.UnitPrice,
            @DiscountPercent = COALESCE(pld.DiscountPercent, 0)
        FROM PriceListDetails pld
        WHERE pld.PriceListID = @PriceListID
          AND pld.ItemID = @ItemID
          AND pld.IsActive = 1
          AND (@Quantity >= pld.MinQty)
          AND (@Quantity <= ISNULL(pld.MaxQty, 999999999))
          AND (pld.StartDate IS NULL OR pld.StartDate <= GETDATE())
          AND (pld.EndDate IS NULL OR pld.EndDate >= GETDATE())
        ORDER BY pld.MinQty DESC;
    END
    
    IF @UnitPrice IS NULL OR @UnitPrice = 0
        SELECT @UnitPrice = DefaultSellingPrice FROM Items WHERE ItemID = @ItemID;
    
    SET @DiscountPercent = CASE 
        WHEN @CustomerDiscount > ISNULL(@DiscountPercent, 0) 
        THEN @CustomerDiscount ELSE ISNULL(@DiscountPercent, 0) END;
    
    SET @NetPrice = ISNULL(@UnitPrice, 0) * (1 - ISNULL(@DiscountPercent, 0) / 100);
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_LinkGRNToInvoice
-- ============================================================


-- =============================================
-- 7) ربط GRN بالفاتورة (تحديث حالة GRN)
-- =============================================
CREATE   PROCEDURE sp_LinkGRNToInvoice
    @GRNID INT,
    @InvoiceID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.GoodsReceiptNotes
    SET GRNStatus = 3,
        LinkedInvoiceID = @InvoiceID,
        ModifiedDate = GETDATE()
    WHERE GRNID = @GRNID
      AND GRNStatus = 2;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_LoadItemsForStockCount
-- ============================================================
/*
    Stock-count FIFO/batch fix (reviewed against db68995 metadata supplied by the user).

    What this changes:
      - Loads one count line per existing batch balance, with its batch number,
        system quantity, and batch unit cost. Non-batch items remain one line per item.
      - Applies a count correction only to the exact batch line counted by the user.
      - Updates InventoryBalance once per item by the sum of approved line variances.
      - Fails closed on stale snapshots, missing/extra batch lines, negative batch
        quantities, or a batch/aggregate mismatch.
      - Writes InventoryTransactions.BatchNumber (the supplied schema has no
        BatchBalanceID column in InventoryTransactions).

    No table/schema changes are required. This file contains ALTER PROCEDURE only;
    it has NOT been executed by the assistant. Test/review it on a non-production
    database first. The optional @BatchAwareVersion parameter also makes a newer
    application fail safely against the old procedure signature. The default version
    is 0, so old application calls also fail closed after this patch until the
    batch-aware application version is deployed.
*/

CREATE PROCEDURE dbo.sp_LoadItemsForStockCount
    @CountID INT,
    @WarehouseID INT,
    @CategoryID INT = NULL,
    @BatchAwareVersion TINYINT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @CategoryID = NULLIF(@CategoryID, 0);

    IF ISNULL(@BatchAwareVersion, 0) <> 1
    BEGIN
        RAISERROR(N'Unsupported stock-count loader version.', 16, 1);
        RETURN;
    END;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @HeaderWarehouseID INT;
        DECLARE @CountStatus TINYINT;

        SELECT
            @HeaderWarehouseID = sc.WarehouseID,
            @CountStatus = sc.CountStatus
        FROM dbo.StockCounts AS sc WITH (UPDLOCK, HOLDLOCK)
        WHERE sc.CountID = @CountID;

        IF @HeaderWarehouseID IS NULL
        BEGIN
            RAISERROR(N'أمر الجرد غير موجود.', 16, 1);
        END;

        IF @HeaderWarehouseID <> @WarehouseID
        BEGIN
            RAISERROR(N'المخزن المرسل لا يطابق مخزن أمر الجرد.', 16, 1);
        END;

        IF @CountStatus <> 1
        BEGIN
            RAISERROR(N'تحميل أصناف الجرد مسموح فقط عند إنشاء أمر جرد جديد.', 16, 1);
        END;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.StockCountDetails AS scd WITH (UPDLOCK, HOLDLOCK)
            WHERE scd.CountID = @CountID
              AND (scd.CountedQty IS NOT NULL OR ISNULL(scd.AdjustmentApproved, 0) = 1)
        )
        BEGIN
            RAISERROR(N'لا يمكن إعادة تحميل تفاصيل جرد بدأ إدخال كمياته أو اعتماد تسوياته.', 16, 1);
        END;

        /*
          Check existing tracked balances before replacing the count lines.
          A batch-managed item must already reconcile exactly to InventoryBalance;
          this procedure will not invent or absorb an opening difference.
        */
        IF EXISTS
        (
            SELECT 1
            FROM dbo.Items AS i
            LEFT JOIN dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                ON ib.ItemID = i.ItemID
               AND ib.WarehouseID = @WarehouseID
            WHERE i.IsActive = 1
              AND (@CategoryID IS NULL OR i.CategoryID = @CategoryID)
              AND
              (
                  ISNULL(ib.CurrentQty, 0) <> 0
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                      WHERE bb.ItemID = i.ItemID
                        AND bb.WarehouseID = @WarehouseID
                        AND bb.CurrentQty <> 0
                  )
              )
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                  WHERE bb.ItemID = i.ItemID
                    AND bb.WarehouseID = @WarehouseID
              )
              AND
              (
                  ISNULL(ib.CurrentQty, 0) <>
                      ISNULL
                      (
                          (
                              SELECT SUM(bb.CurrentQty)
                              FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                              WHERE bb.ItemID = i.ItemID
                                AND bb.WarehouseID = @WarehouseID
                          ),
                          0
                      )
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                      WHERE bb.ItemID = i.ItemID
                        AND bb.WarehouseID = @WarehouseID
                        AND
                        (
                            bb.CurrentQty < 0
                            OR NULLIF(LTRIM(RTRIM(bb.BatchNumber)), N'') IS NULL
                        )
                  )
              )
        )
        BEGIN
            RAISERROR(N'تعذر تحميل الجرد: يوجد صنف متتبع لا تتطابق أرصدة باتشاته مع الرصيد الإجمالي، أو توجد طبقة سالبة/بلا رقم باتش. سوِّ البيانات أولاً.', 16, 1);
        END;

        DELETE FROM dbo.StockCountDetails
        WHERE CountID = @CountID;

        ;WITH CountRows AS
        (
            /* One physical count line for each existing batch layer. */
            SELECT
                i.ItemID,
                i.ItemCode,
                i.PrimaryUnitID AS UnitID,
                bb.BatchNumber,
                bb.CurrentQty AS SystemQty,
                bb.UnitCost
            FROM dbo.Items AS i
            INNER JOIN dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                ON bb.ItemID = i.ItemID
               AND bb.WarehouseID = @WarehouseID
            LEFT JOIN dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                ON ib.ItemID = i.ItemID
               AND ib.WarehouseID = @WarehouseID
            WHERE i.IsActive = 1
              AND (@CategoryID IS NULL OR i.CategoryID = @CategoryID)
              AND (ISNULL(ib.CurrentQty, 0) <> 0 OR bb.CurrentQty <> 0)

            UNION ALL

            /* Non-batch-managed stock remains one aggregate line per item. */
            SELECT
                i.ItemID,
                i.ItemCode,
                i.PrimaryUnitID AS UnitID,
                CAST(NULL AS NVARCHAR(100)) AS BatchNumber,
                ib.CurrentQty AS SystemQty,
                ISNULL(ib.AverageCost, 0) AS UnitCost
            FROM dbo.Items AS i
            INNER JOIN dbo.InventoryBalance AS ib WITH (UPDLOCK, HOLDLOCK)
                ON ib.ItemID = i.ItemID
               AND ib.WarehouseID = @WarehouseID
            WHERE i.IsActive = 1
              AND ib.CurrentQty > 0
              AND (@CategoryID IS NULL OR i.CategoryID = @CategoryID)
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.InventoryBatchBalance AS bb WITH (UPDLOCK, HOLDLOCK)
                  WHERE bb.ItemID = i.ItemID
                    AND bb.WarehouseID = @WarehouseID
              )
        )
        INSERT INTO dbo.StockCountDetails
            (CountID, LineNumber, ItemID, UnitID, SystemQty, UnitCost, BatchNumber)
        SELECT
            @CountID,
            CAST(ROW_NUMBER() OVER
            (
                ORDER BY ItemCode, ItemID,
                         CASE WHEN BatchNumber IS NULL THEN 0 ELSE 1 END,
                         BatchNumber
            ) AS INT),
            ItemID,
            UnitID,
            SystemQty,
            UnitCost,
            BatchNumber
        FROM CountRows;

        DECLARE @LoadedCount INT;
        SELECT @LoadedCount = COUNT(*)
        FROM dbo.StockCountDetails
        WHERE CountID = @CountID;

        COMMIT TRANSACTION;

        SELECT @LoadedCount AS LoadedCount;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        DECLARE @LoadErrorMessage NVARCHAR(4000);
        SET @LoadErrorMessage = ERROR_MESSAGE();
        RAISERROR(N'%s', 16, 1, @LoadErrorMessage);
        RETURN;
    END CATCH;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_PostPaymentVoucher
-- ============================================================
-- =====================================================================
-- إصلاح 11: اعتماد سندات التسوية (قبض وصرف) — كتابة ربط الجهة في القيد
-- المشكلة: إجراءا sp_PostPaymentVoucher و sp_PostReceiptVoucher يكتبان
-- سطور القيد دون EntityType/EntityID، بينما تحقق الاعتماد في الكود
-- (TreasuryService.VerifyVoucherJournalAsync) يشترط أن يحمل سطر حساب
-- الذمم (2101 للمورد / 1103 للعميل) نوعَ الجهة ورقمها، فيُرفض اعتماد
-- أي سند تسوية دائمًا.
-- الإصلاح: عند وجود تخصيصات فواتير للسند، يكتب الإجراء على سطر الحساب
-- المقابل: 'Supplier' ورقم المورد لسند الصرف، و 'Customer' ورقم العميل
-- لسند القبض — من عمودي EntityType/EntityID في السند نفسه، وهما ما
-- يتحقق منه الكود. السندات العامة (بلا تخصيصات) لا تتغير إطلاقًا.
-- التنفيذ: شغّل هذا الملف مرة واحدة على قاعدة البيانات (SSMS).
-- =====================================================================

CREATE   PROC dbo.sp_PostPaymentVoucher @PaymentID INT, @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Amount DECIMAL(18,2), @CashBoxID INT, @AccountID INT, @ExpenseAcc INT, @PNumber NVARCHAR(100), @PDate DATE, @Desc NVARCHAR(1000), @JID INT, @FYear INT, @Period INT, @JNo NVARCHAR(50);
    DECLARE @EntityType INT, @EntityID INT, @IsSettlement BIT;

    SELECT @Amount=ISNULL(AmountLocal, Amount), @CashBoxID=CashBoxID, @ExpenseAcc=ExpenseAccountID, @PNumber=PaymentNumber, @PDate=PaymentDate, @Desc=Description,
           @EntityType=EntityType, @EntityID=EntityID
    FROM dbo.PaymentVouchers WHERE PaymentID=@PaymentID;

    SET @IsSettlement = CASE WHEN EXISTS (SELECT 1 FROM dbo.PaymentAllocations WHERE PaymentID=@PaymentID) THEN 1 ELSE 0 END;

    SELECT @AccountID=AccountID FROM dbo.CashBoxes WHERE CashBoxID=@CashBoxID;
    IF @AccountID IS NULL RETURN;

    IF OBJECT_ID('dbo.FiscalYears','U') IS NOT NULL
        SELECT TOP 1 @FYear=FiscalYearID FROM dbo.FiscalYears WHERE @PDate BETWEEN StartDate AND EndDate ORDER BY FiscalYearID DESC;
    IF OBJECT_ID('dbo.AccountingPeriods','U') IS NOT NULL
        SELECT TOP 1 @Period=PeriodID FROM dbo.AccountingPeriods WHERE @PDate BETWEEN StartDate AND EndDate ORDER BY PeriodID DESC;

    IF EXISTS(SELECT 1 FROM dbo.JournalEntries WHERE SourceDocType='PaymentVoucher' AND SourceDocID=@PaymentID AND JournalStatus=2) RETURN;

    BEGIN TRY
        DECLARE @NextOut NVARCHAR(50);
        EXEC sp_GetNextNumber 'JV', @NextOut OUTPUT;
        SET @JNo = @NextOut;
    END TRY
    BEGIN CATCH
        SET @JNo = dbo.fn_GetNextSequence('JV');
    END CATCH

    BEGIN TRAN;
    BEGIN TRY
        INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate)
        VALUES (@JNo, @PDate, ISNULL(@Desc, N'سند صرف ' + @PNumber), 'PaymentVoucher', @PaymentID, @PNumber, @Amount, @Amount, @FYear, @Period, 2, 1, @UserID, GETDATE());

        SET @JID = SCOPE_IDENTITY();

        DECLARE @DebitAccToUse INT = ISNULL(@ExpenseAcc, (SELECT TOP 1 AccountID FROM dbo.ChartOfAccounts WHERE AccountType=6 AND IsDetailAccount=1));
        IF @DebitAccToUse IS NULL SET @DebitAccToUse = @AccountID;

        INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, EntityType, EntityID)
        VALUES (@JID, 1, @DebitAccToUse, @Amount, 0, @Desc,
                CASE WHEN @IsSettlement=1 AND @EntityType=1 AND @EntityID IS NOT NULL THEN N'Supplier' ELSE NULL END,
                CASE WHEN @IsSettlement=1 AND @EntityType=1 AND @EntityID IS NOT NULL THEN @EntityID ELSE NULL END);

        INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
        VALUES (@JID, 2, @AccountID, 0, @Amount, @Desc);

        UPDATE dbo.PaymentVouchers SET JournalID=@JID, IsPosted=1 WHERE PaymentID=@PaymentID;
        UPDATE dbo.CashBoxes SET CurrentBalance = CurrentBalance - @Amount WHERE CashBoxID=@CashBoxID;

        COMMIT;
    END TRY
    BEGIN CATCH
        ROLLBACK;
        THROW;
    END CATCH
END

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_PostReceiptVoucher
-- ============================================================

CREATE   PROC dbo.sp_PostReceiptVoucher @ReceiptID INT, @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Amount DECIMAL(18,2), @CashBoxID INT, @AccountID INT, @RevenueAcc INT, @RNumber NVARCHAR(100), @RDate DATE, @Desc NVARCHAR(1000), @JID INT, @FYear INT, @Period INT, @JNo NVARCHAR(50);
    DECLARE @EntityType INT, @EntityID INT, @IsSettlement BIT;

    SELECT @Amount=ISNULL(AmountLocal, Amount), @CashBoxID=CashBoxID, @RevenueAcc=RevenueAccountID, @RNumber=ReceiptNumber, @RDate=ReceiptDate, @Desc=Description,
           @EntityType=EntityType, @EntityID=EntityID
    FROM dbo.ReceiptVouchers WHERE ReceiptID=@ReceiptID;

    SET @IsSettlement = CASE WHEN EXISTS (SELECT 1 FROM dbo.ReceiptAllocations WHERE ReceiptID=@ReceiptID) THEN 1 ELSE 0 END;

    SELECT @AccountID=AccountID FROM dbo.CashBoxes WHERE CashBoxID=@CashBoxID;
    IF @AccountID IS NULL RETURN;

    -- FiscalYear/Period بدون IsActive
    IF OBJECT_ID('dbo.FiscalYears','U') IS NOT NULL
        SELECT TOP 1 @FYear=FiscalYearID FROM dbo.FiscalYears WHERE @RDate BETWEEN StartDate AND EndDate ORDER BY FiscalYearID DESC;
    IF OBJECT_ID('dbo.AccountingPeriods','U') IS NOT NULL
        SELECT TOP 1 @Period=PeriodID FROM dbo.AccountingPeriods WHERE @RDate BETWEEN StartDate AND EndDate ORDER BY PeriodID DESC;

    IF EXISTS(SELECT 1 FROM dbo.JournalEntries WHERE SourceDocType='ReceiptVoucher' AND SourceDocID=@ReceiptID AND JournalStatus=2) RETURN;

    BEGIN TRY
        DECLARE @NextOut NVARCHAR(50);
        EXEC sp_GetNextNumber 'JV', @NextOut OUTPUT;
        SET @JNo = @NextOut;
    END TRY
    BEGIN CATCH
        SET @JNo = dbo.fn_GetNextSequence('JV');
    END CATCH

    BEGIN TRAN;
    BEGIN TRY
        INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate)
        VALUES (@JNo, @RDate, ISNULL(@Desc, N'سند قبض ' + @RNumber), 'ReceiptVoucher', @ReceiptID, @RNumber, @Amount, @Amount, @FYear, @Period, 2, 1, @UserID, GETDATE());

        SET @JID = SCOPE_IDENTITY();

        INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
        VALUES (@JID, 1, @AccountID, @Amount, 0, @Desc);

        DECLARE @CreditAccToUse INT = ISNULL(@RevenueAcc, (SELECT TOP 1 AccountID FROM dbo.ChartOfAccounts WHERE LinkedEntityType='Customer' AND IsDetailAccount=1));
        IF @CreditAccToUse IS NULL SET @CreditAccToUse = (SELECT TOP 1 AccountID FROM dbo.ChartOfAccounts WHERE AccountType=4 AND IsDetailAccount=1);
        IF @CreditAccToUse IS NULL SET @CreditAccToUse = @AccountID;

        INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, EntityType, EntityID)
        VALUES (@JID, 2, @CreditAccToUse, 0, @Amount, @Desc,
                CASE WHEN @IsSettlement=1 AND @EntityType=1 AND @EntityID IS NOT NULL THEN N'Customer' ELSE NULL END,
                CASE WHEN @IsSettlement=1 AND @EntityType=1 AND @EntityID IS NOT NULL THEN @EntityID ELSE NULL END);

        UPDATE dbo.ReceiptVouchers SET JournalID=@JID, IsPosted=1 WHERE ReceiptID=@ReceiptID;
        UPDATE dbo.CashBoxes SET CurrentBalance = CurrentBalance + @Amount WHERE CashBoxID=@CashBoxID;

        COMMIT;
    END TRY
    BEGIN CATCH
        ROLLBACK;
        THROW;
    END CATCH
END

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_RecalcPurchaseInvoiceTotals
-- ============================================================
CREATE   PROCEDURE sp_RecalcPurchaseInvoiceTotals
    @InvoiceID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) تحديث إجمالي كل سطر
    UPDATE dbo.PurchaseInvoiceDetails
    SET DiscountAmount = UnitPrice * Quantity * DiscountPercent / 100,
        LineTotal = UnitPrice * Quantity * (1 - DiscountPercent / 100),
        TaxAmount = UnitPrice * Quantity * (1 - DiscountPercent / 100) * TaxRate / 100,
        LineTotalWithTax = UnitPrice * Quantity * (1 - DiscountPercent / 100) * (1 + TaxRate / 100)
    WHERE InvoiceID = @InvoiceID;

    -- 2) حساب الإجماليات
    DECLARE @SubTotal DECIMAL(18,2);
    DECLARE @LineTaxAmount DECIMAL(18,2);
    DECLARE @DiscountPercent DECIMAL(5,2);
    DECLARE @GeneralDiscount DECIMAL(18,2);
    DECLARE @ShippingCost DECIMAL(18,2);
    DECLARE @OtherCosts DECIMAL(18,2);

    SELECT @SubTotal = ISNULL(SUM(LineTotal), 0),
           @LineTaxAmount = ISNULL(SUM(TaxAmount), 0)
    FROM dbo.PurchaseInvoiceDetails
    WHERE InvoiceID = @InvoiceID;

    SELECT @DiscountPercent = ISNULL(DiscountPercent, 0),
           @ShippingCost = ISNULL(ShippingCost, 0),
           @OtherCosts = ISNULL(OtherCosts, 0)
    FROM dbo.PurchaseInvoices
    WHERE InvoiceID = @InvoiceID;

    -- 3) حساب الخصم العام
    SET @GeneralDiscount = @SubTotal * @DiscountPercent / 100;

    -- 4) إعادة حساب الضريبة بعد الخصم العام
    DECLARE @TaxableAfterDiscount DECIMAL(18,2);
    SET @TaxableAfterDiscount = @SubTotal - @GeneralDiscount;

    -- نسبة الضريبة المتوسطة من السطور
    DECLARE @AvgTaxRate DECIMAL(5,2);
    SELECT @AvgTaxRate = CASE
        WHEN SUM(LineTotal) > 0
        THEN SUM(TaxAmount) * 100.0 / SUM(LineTotal)
        ELSE 0
    END
    FROM dbo.PurchaseInvoiceDetails
    WHERE InvoiceID = @InvoiceID;

    DECLARE @FinalTaxAmount DECIMAL(18,2);
    SET @FinalTaxAmount = @TaxableAfterDiscount * @AvgTaxRate / 100;

    -- 5) تحديث الفاتورة
    UPDATE dbo.PurchaseInvoices
    SET SubTotal = @SubTotal,
        DiscountAmount = @GeneralDiscount,
        TaxableAmount = @TaxableAfterDiscount,
        TaxAmount = @FinalTaxAmount,
        TotalAmount = @TaxableAfterDiscount + @FinalTaxAmount + @ShippingCost + @OtherCosts,
        ModifiedDate = GETDATE()
    WHERE InvoiceID = @InvoiceID;

    -- 6) تحديث حالة الدفع
    UPDATE dbo.PurchaseInvoices
    SET PaymentStatus = CASE
            WHEN ISNULL(PaidAmount, 0) = 0 THEN 1
            WHEN ISNULL(PaidAmount, 0) >= TotalAmount THEN 3
            ELSE 2
        END,
        InvoiceStatus = CASE
            WHEN InvoiceStatus IN (1, 2, 7) THEN InvoiceStatus
            WHEN ISNULL(PaidAmount, 0) >= TotalAmount THEN 5
            WHEN ISNULL(PaidAmount, 0) > 0 THEN 4
            WHEN DueDate < CAST(GETDATE() AS DATE) THEN 6
            ELSE InvoiceStatus
        END
    WHERE InvoiceID = @InvoiceID
      AND InvoiceStatus NOT IN (1, 2, 7);
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_RecalcPurchaseReturnTotals
-- ============================================================
CREATE   PROCEDURE dbo.sp_RecalcPurchaseReturnTotals
    @ReturnID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.PurchaseReturnDetails
    SET LineTotal = UnitPrice * Quantity,
        TaxAmount = UnitPrice * Quantity * TaxRate / 100,
        LineTotalWithTax = UnitPrice * Quantity * (1 + TaxRate / 100)
    WHERE ReturnID = @ReturnID;

    UPDATE dbo.PurchaseReturns
    SET SubTotal = ISNULL((SELECT SUM(LineTotal) FROM dbo.PurchaseReturnDetails WHERE ReturnID = @ReturnID), 0),
        TaxAmount = ISNULL((SELECT SUM(TaxAmount) FROM dbo.PurchaseReturnDetails WHERE ReturnID = @ReturnID), 0),
        TotalAmount = ISNULL((SELECT SUM(LineTotalWithTax) FROM dbo.PurchaseReturnDetails WHERE ReturnID = @ReturnID), 0),
        ModifiedDate = GETDATE()
    WHERE ReturnID = @ReturnID;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_RecalcSalesInvoiceTotals
-- ============================================================
CREATE PROCEDURE dbo.sp_RecalcSalesInvoiceTotals @InvoiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @IsTaxable BIT, @TaxRate DECIMAL(5,2), @DiscountPercent DECIMAL(5,2), @Shipping DECIMAL(18,2), @Other DECIMAL(18,2);
    SELECT @IsTaxable=ISNULL(IsTaxable,1), @TaxRate=ISNULL(TaxRate,14), @DiscountPercent=ISNULL(DiscountPercent,0), @Shipping=ISNULL(ShippingCost,0), @Other=ISNULL(OtherCosts,0)
    FROM dbo.SalesInvoices WHERE InvoiceID=@InvoiceID;

    UPDATE dbo.SalesInvoiceDetails SET
        DiscountAmount = UnitPrice * Quantity * DiscountPercent / 100,
        LineTotal = UnitPrice * Quantity * (1 - DiscountPercent/100),
        TaxRate = CASE WHEN @IsTaxable=1 THEN ISNULL(TaxRate,@TaxRate) ELSE 0 END,
        TaxAmount = CASE WHEN @IsTaxable=1 THEN (UnitPrice * Quantity * (1 - DiscountPercent/100)) * ISNULL(TaxRate,@TaxRate) / 100 ELSE 0 END,
        LineTotalWithTax = (UnitPrice * Quantity * (1 - DiscountPercent/100)) + CASE WHEN @IsTaxable=1 THEN (UnitPrice * Quantity * (1 - DiscountPercent/100)) * ISNULL(TaxRate,@TaxRate) / 100 ELSE 0 END
    WHERE InvoiceID=@InvoiceID;

    DECLARE @SubTotal DECIMAL(18,2), @LineDiscount DECIMAL(18,2), @TaxAmt DECIMAL(18,2);
    SELECT @SubTotal=ISNULL(SUM(UnitPrice*Quantity),0), @LineDiscount=ISNULL(SUM(DiscountAmount),0), @TaxAmt=ISNULL(SUM(TaxAmount),0)
    FROM dbo.SalesInvoiceDetails WHERE InvoiceID=@InvoiceID;

    DECLARE @DiscountAmt DECIMAL(18,2) = (@SubTotal - @LineDiscount) * @DiscountPercent / 100;
    DECLARE @TaxableAmt DECIMAL(18,2) = @SubTotal - @LineDiscount - @DiscountAmt;
    DECLARE @TotalTax DECIMAL(18,2) = CASE WHEN @IsTaxable=1 THEN @TaxAmt ELSE 0 END;
    DECLARE @Total DECIMAL(18,2) = @TaxableAmt + @TotalTax + @Shipping + @Other;

    UPDATE dbo.SalesInvoices SET SubTotal=@SubTotal, DiscountAmount=@LineDiscount+@DiscountAmt, TaxableAmount=@TaxableAmt, TaxAmount=@TotalTax, TotalAmount=@Total WHERE InvoiceID=@InvoiceID;
END

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdateBOMCosts
-- ============================================================


-- SP: تحديث تكاليف الوصفة من أسعار المواد الحالية
CREATE   PROCEDURE [dbo].[sp_UpdateBOMCosts]
    @BOMID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- تحديث تكلفة كل مادة في الوصفة
    UPDATE bd
    SET bd.UnitCost = ISNULL(i.AverageCost, 
                     ISNULL(i.LastPurchasePrice, 
                     ISNULL(i.StandardCost, 0)))
    FROM BOMDetails bd
    INNER JOIN Items i ON bd.ItemID = i.ItemID
    WHERE bd.BOMID = @BOMID;
    
    PRINT N'تم تحديث تكاليف الوصفة رقم: ' + CAST(@BOMID AS NVARCHAR(10));
    
    -- عرض ملخص التكلفة
    SELECT 
        N'إجمالي تكلفة المواد الخام' AS [البيان],
        SUM(Quantity * (1 + ISNULL(WastePercent,0)/100) * ISNULL(UnitCost,0)) AS [القيمة]
    FROM BOMDetails WHERE BOMID = @BOMID AND MaterialType = 1
    UNION ALL
    SELECT 
        N'إجمالي تكلفة مواد التعبئة',
        SUM(Quantity * (1 + ISNULL(WastePercent,0)/100) * ISNULL(UnitCost,0))
    FROM BOMDetails WHERE BOMID = @BOMID AND MaterialType = 2
    UNION ALL
    SELECT N'تكلفة العمالة', LaborCostPerBatch FROM BillOfMaterials WHERE BOMID = @BOMID
    UNION ALL
    SELECT N'مصاريف غير مباشرة', OverheadCostPerBatch FROM BillOfMaterials WHERE BOMID = @BOMID;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdateInventoryAfterReturn
-- ============================================================
-- =====================================================================
-- إصلاح 13: مرتجع المشتريات لا يخصم أرصدة الباتشات (FIFO)
-- المشكلة: sp_UpdateInventoryAfterReturn يخفّض الرصيد الإجمالي للمخزون
-- ويسجل الحركة، لكنه لا يمسّ InventoryBatchBalance إطلاقًا. فبعد أي
-- مرتجع معتمد يصبح مجموع الباتشات أعلى من الرصيد الإجمالي بكمية
-- المرتجع، وحارس اعتماد أذون صرف المواد (يشترط تطابقهما) يرفض أي صرف
-- لذلك الصنف/المخزن حتى تُسوّى الأرصدة يدويًا.
-- الدليل الحي: مرتجع 2 وحدة من الصنف 82 خفّض الرصيد 518 ← 516 بينما
-- بقي مجموع الباتشات 518، فرفض اعتماد إذن الصرف برسالة عدم التطابق.
-- الإصلاح: خصم الكمية المرتجعة من الباتشات بنفس منطق البيع (الأقدم
-- أولًا) داخل الإجراء نفسه. بقية جسم الإجراء كما هو حرفيًا.
-- التنفيذ: شغّل هذا الملف مرة واحدة على قاعدة البيانات (SSMS).
-- =====================================================================

CREATE   PROCEDURE dbo.sp_UpdateInventoryAfterReturn
    @ItemID INT,
    @ReturnQty DECIMAL(18,4),
    @UnitCost DECIMAL(18,4),
    @WarehouseID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) تقليل رصيد المخزن
    IF EXISTS (
        SELECT 1 FROM dbo.InventoryBalance
        WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID
    )
    BEGIN
        UPDATE dbo.InventoryBalance
        SET CurrentQty = CurrentQty - @ReturnQty,
            LastMovementDate = GETDATE(),
            ModifiedDate = GETDATE()
        WHERE ItemID = @ItemID
          AND WarehouseID = @WarehouseID;
    END

    -- 2) تسجيل حركة مخزون
    DECLARE @BalanceAfter DECIMAL(18,4);
    SELECT @BalanceAfter = ISNULL(CurrentQty, 0)
    FROM dbo.InventoryBalance
    WHERE ItemID = @ItemID
      AND WarehouseID = @WarehouseID;

    INSERT INTO dbo.InventoryTransactions
    (
        TransactionDate, TransactionType, ItemID, WarehouseID,
        Quantity, UnitID, UnitCost,
        BalanceBefore, BalanceAfter,
        SourceDocType, Notes, CreatedDate
    )
    VALUES
    (
        GETDATE(),
        5,
        @ItemID,
        @WarehouseID,
        -@ReturnQty,
        (SELECT PrimaryUnitID FROM dbo.Items WHERE ItemID = @ItemID),
        @UnitCost,
        ISNULL(@BalanceAfter, 0) + @ReturnQty,
        ISNULL(@BalanceAfter, 0),
        N'PRET',
        N'مرتجع مشتريات — تحديث تلقائي',
        GETDATE()
    );

    -- 3) خصم الكمية المرتجعة من أرصدة الباتشات (الأقدم أولًا، كما يفعل البيع)
    DECLARE @Remaining DECIMAL(18,4) = @ReturnQty;
    DECLARE @BatchID INT, @BatchQty DECIMAL(18,4), @Deduct DECIMAL(18,4);

    DECLARE batch_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT BatchBalanceID, CurrentQty
        FROM dbo.InventoryBatchBalance
        WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID AND ISNULL(CurrentQty, 0) > 0
        ORDER BY BatchBalanceID ASC;

    OPEN batch_cursor;
    FETCH NEXT FROM batch_cursor INTO @BatchID, @BatchQty;
    WHILE @@FETCH_STATUS = 0 AND @Remaining > 0
    BEGIN
        SET @Deduct = CASE WHEN @BatchQty < @Remaining THEN @BatchQty ELSE @Remaining END;
        UPDATE dbo.InventoryBatchBalance
        SET CurrentQty = CurrentQty - @Deduct
        WHERE BatchBalanceID = @BatchID;
        SET @Remaining = @Remaining - @Deduct;
        FETCH NEXT FROM batch_cursor INTO @BatchID, @BatchQty;
    END;
    CLOSE batch_cursor;
    DEALLOCATE batch_cursor;
END

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdateInventoryBalance
-- ============================================================


-- SP: تحديث رصيد المخزون
CREATE PROCEDURE [dbo].[sp_UpdateInventoryBalance]
    @ItemID INT,
    @WarehouseID INT,
    @Quantity DECIMAL(18,4),
    -- موجب = إضافة, سالب = خصم
    @UnitCost DECIMAL(18,4) = 0,
    @TransactionType TINYINT,
    @BatchNumber NVARCHAR(50) = NULL,
    @ExpiryDate DATE = NULL,
    @ManufactureDate DATE = NULL,
    @SourceDocType NVARCHAR(20) = NULL,
    @SourceDocID INT = NULL,
    @SourceDocNumber NVARCHAR(50) = NULL,
    @SourceDocLineID INT = NULL,
    @Notes NVARCHAR(200) = NULL,
    @UserID INT = NULL,
    @BatchSourceType TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        
        DECLARE @BalanceBefore DECIMAL(18,4);
        DECLARE @CostBefore DECIMAL(18,4);
        DECLARE @BalanceAfter DECIMAL(18,4);
        DECLARE @CostAfter DECIMAL(18,4);
        DECLARE @UnitID INT;
        
        SELECT @UnitID = PrimaryUnitID FROM Items WHERE ItemID = @ItemID;
        
        -- جلب الرصيد الحالي
        SELECT @BalanceBefore = ISNULL(CurrentQty, 0),
               @CostBefore = ISNULL(AverageCost, 0)
        FROM InventoryBalance
        WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID;
        
        IF @BalanceBefore IS NULL SET @BalanceBefore = 0;
        IF @CostBefore IS NULL SET @CostBefore = 0;
        
        -- حساب الرصيد الجديد
        SET @BalanceAfter = @BalanceBefore + @Quantity;
        
        -- حساب متوسط التكلفة الجديد (للإضافة فقط)
        IF @Quantity > 0 AND @UnitCost > 0
            SET @CostAfter = CASE WHEN @BalanceAfter > 0
                THEN ((@BalanceBefore * @CostBefore) + (@Quantity * @UnitCost)) / @BalanceAfter
                ELSE @UnitCost END;
        ELSE
            SET @CostAfter = @CostBefore;
        
        -- تحديث أو إدراج رصيد المخزون
        IF EXISTS (SELECT 1 FROM InventoryBalance WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID)
        BEGIN
            UPDATE InventoryBalance
            SET CurrentQty = @BalanceAfter,
                AverageCost = @CostAfter,
                LastMovementDate = GETDATE(),
                ModifiedDate = GETDATE()
            WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID;
        END
        ELSE
        BEGIN
            INSERT INTO InventoryBalance (ItemID, WarehouseID, CurrentQty, AverageCost, LastMovementDate)
            VALUES (@ItemID, @WarehouseID, @BalanceAfter, @CostAfter, GETDATE());
        END
        
        -- تسجيل حركة المخزون
        INSERT INTO InventoryTransactions (
            TransactionDate, TransactionType, ItemID, WarehouseID,
            Quantity, UnitID, UnitCost, BalanceBefore, BalanceAfter,
            CostBefore, CostAfter, BatchNumber, ExpiryDate,
            SourceDocType, SourceDocID, SourceDocNumber, SourceDocLineID,
            Notes, CreatedBy
        ) VALUES (
            GETDATE(), @TransactionType, @ItemID, @WarehouseID,
            @Quantity, @UnitID, @UnitCost, @BalanceBefore, @BalanceAfter,
            @CostBefore, @CostAfter, @BatchNumber, @ExpiryDate,
            @SourceDocType, @SourceDocID, @SourceDocNumber, @SourceDocLineID,
            @Notes, @UserID
        );
        
        -- تحديث رصيد الباتش (لو فيه باتش)
        IF @BatchNumber IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM InventoryBatchBalance 
                       WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID AND BatchNumber = @BatchNumber)
            BEGIN
                UPDATE InventoryBatchBalance
                SET CurrentQty = CurrentQty + @Quantity,
                    ManufactureDate = COALESCE(ManufactureDate, @ManufactureDate),
                    ModifiedDate = GETDATE()
                WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID AND BatchNumber = @BatchNumber;
            END
            ELSE IF @Quantity > 0
            BEGIN
                INSERT INTO InventoryBatchBalance (
                    ItemID, WarehouseID, BatchNumber, ManufactureDate, ExpiryDate,
                    CurrentQty, UnitCost, ReceiptDate, SourceType, SourceDocNumber
                ) VALUES (
                    @ItemID, @WarehouseID, @BatchNumber, @ManufactureDate, @ExpiryDate,
                    @Quantity, @UnitCost, CAST(GETDATE() AS DATE),
                    @BatchSourceType, @SourceDocNumber
                );
            END
        END
        
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdateItemAverageCost
-- ============================================================
CREATE   PROCEDURE sp_UpdateItemAverageCost
    @ItemID INT,
    @NewQty DECIMAL(18,4),
    @NewUnitCost DECIMAL(18,4)
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CurrentStock DECIMAL(18,4) = 0;
    DECLARE @CurrentAvgCost DECIMAL(18,4) = 0;
    DECLARE @NewAvgCost DECIMAL(18,4) = 0;
    
    -- جلب الرصيد الحالي والمتوسط من InventoryBalance
    SELECT 
        @CurrentStock = ISNULL(SUM(CurrentQty), 0),
        @CurrentAvgCost = ISNULL(
            CASE 
                WHEN SUM(CurrentQty) > 0 
                THEN SUM(CurrentQty * AverageCost) / SUM(CurrentQty)
                ELSE 0 
            END, 0)
    FROM dbo.InventoryBalance
    WHERE ItemID = @ItemID;
    
    -- حساب المتوسط المرجح الجديد
    -- المعادلة: (رصيد قديم × تكلفة قديمة + كمية جديدة × تكلفة جديدة) / (رصيد قديم + كمية جديدة)
    IF (@CurrentStock + @NewQty) > 0
        SET @NewAvgCost = (
            (@CurrentStock * @CurrentAvgCost) + (@NewQty * @NewUnitCost)
        ) / (@CurrentStock + @NewQty);
    ELSE
        SET @NewAvgCost = @NewUnitCost;
    
    -- تحديث جدول الأصناف
    UPDATE dbo.Items
    SET 
        AverageCost = @NewAvgCost,
        LastPurchasePrice = @NewUnitCost,
        ModifiedDate = GETDATE()
    WHERE ItemID = @ItemID;
    
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdateItemCostAfterGRN
-- ============================================================

-- Prepared from the production procedure definition supplied by the user.
-- Apply together with the matching GoodsReceiptService.cs change.
-- This alters future GRN posting only; it does not backfill historic movements.
CREATE PROCEDURE [dbo].[sp_UpdateItemCostAfterGRN]
    @ItemID INT,
    @ReceivedQty DECIMAL(18,4),
    @UnitCost DECIMAL(18,4),
    @WarehouseID INT,
    @GRNID INT = NULL,
    @GRNDetailID INT = NULL,
    @GRNNumber NVARCHAR(100) = NULL,
    @GRNDate DATE = NULL,
    @UnitID INT = NULL,
    @BatchNumber NVARCHAR(100) = NULL,
    @ExpiryDate DATE = NULL,
    @ManufactureDate DATE = NULL,
    @UserID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- GRN approval calls this procedure inside its outer database transaction.
    IF @@TRANCOUNT = 0
    BEGIN
        RAISERROR(N'GRN inventory posting must run inside the approval transaction.', 16, 1);
        RETURN;
    END;

    -- Optional defaults retain a readable error for an old four-argument caller,
    -- but never allow it to post untracked inventory silently.
    IF @ItemID IS NULL OR @ItemID <= 0
       OR @ReceivedQty IS NULL OR @ReceivedQty <= 0
       OR @UnitCost IS NULL OR @UnitCost <= 0
       OR @WarehouseID IS NULL OR @WarehouseID <= 0
       OR @GRNID IS NULL OR @GRNID <= 0
       OR @GRNDetailID IS NULL OR @GRNDetailID <= 0
       OR NULLIF(LTRIM(RTRIM(@GRNNumber)), N'') IS NULL
       OR @GRNDate IS NULL
       OR @UnitID IS NULL OR @UnitID <= 0
    BEGIN
        RAISERROR(N'GRN posting data is incomplete or non-positive.', 16, 1);
        RETURN;
    END;

    SET @BatchNumber = NULLIF(LTRIM(RTRIM(@BatchNumber)), N'');

    IF @BatchNumber IS NULL AND (@ExpiryDate IS NOT NULL OR @ManufactureDate IS NOT NULL)
    BEGIN
        RAISERROR(N'Expiry/manufacture dates require a batch number.', 16, 1);
        RETURN;
    END;

    IF @ExpiryDate IS NOT NULL AND @ManufactureDate IS NOT NULL
       AND @ExpiryDate < @ManufactureDate
    BEGIN
        RAISERROR(N'Expiry date cannot precede manufacture date.', 16, 1);
        RETURN;
    END;

    DECLARE @PrimaryUnitID INT;
    DECLARE @CurrentStock DECIMAL(18,4) = 0;
    DECLARE @CurrentAvgCost DECIMAL(18,4) = 0;
    DECLARE @NewAvgCost DECIMAL(18,4) = 0;

    -- Preserve the existing calculation: item-level weighted average uses all warehouses.
    SELECT @PrimaryUnitID = i.PrimaryUnitID,
           @CurrentAvgCost = ISNULL(i.AverageCost, 0)
    FROM dbo.Items i WITH (UPDLOCK, HOLDLOCK)
    WHERE i.ItemID = @ItemID;

    IF @PrimaryUnitID IS NULL
    BEGIN
        RAISERROR(N'GRN item was not found or has no primary unit.', 16, 1);
        RETURN;
    END;

    SELECT @CurrentStock = ISNULL(SUM(ib.CurrentQty), 0)
    FROM dbo.InventoryBalance ib WITH (UPDLOCK, HOLDLOCK)
    WHERE ib.ItemID = @ItemID;

    IF @CurrentStock < 0
    BEGIN
        RAISERROR(N'Item has a negative aggregate stock balance; GRN posting stopped.', 16, 1);
        RETURN;
    END;

    IF (@CurrentStock + @ReceivedQty) > 0
        SET @NewAvgCost = ((@CurrentStock * @CurrentAvgCost) + (@ReceivedQty * @UnitCost))
                          / (@CurrentStock + @ReceivedQty);
    ELSE
        SET @NewAvgCost = @UnitCost;

    -- Confirm that the values being posted still match the approved GRN header and line.
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.GoodsReceiptNotes grn WITH (UPDLOCK, HOLDLOCK)
        WHERE grn.GRNID = @GRNID
          AND grn.GRNNumber = @GRNNumber
          AND grn.GRNStatus = 2
          AND CONVERT(date, grn.GRNDate) = @GRNDate
    )
    BEGIN
        RAISERROR(N'GRN header does not match the approved receipt being posted.', 16, 1);
        RETURN;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.GoodsReceiptDetails grd WITH (UPDLOCK, HOLDLOCK)
        WHERE grd.GRNDetailID = @GRNDetailID
          AND grd.GRNID = @GRNID
          AND grd.WarehouseID = @WarehouseID
          AND grd.ItemID = @ItemID
          AND grd.UnitID = @UnitID
          AND ISNULL(grd.AcceptedQty, grd.ReceivedQty) = @ReceivedQty
          AND grd.UnitCost = @UnitCost
          AND ISNULL(NULLIF(LTRIM(RTRIM(grd.BatchNumber)), N''), N'') = ISNULL(@BatchNumber, N'')
          AND ((grd.ExpiryDate IS NULL AND @ExpiryDate IS NULL)
               OR CONVERT(date, grd.ExpiryDate) = @ExpiryDate)
          AND ((grd.ManufactureDate IS NULL AND @ManufactureDate IS NULL)
               OR CONVERT(date, grd.ManufactureDate) = @ManufactureDate)
    )
    BEGIN
        RAISERROR(N'GRN detail does not match the accepted line being posted.', 16, 1);
        RETURN;
    END;

    -- Prevent duplicate posting of this GRN detail.
    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventoryTransactions WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceDocType = N'GRN'
          AND SourceDocID = @GRNID
          AND SourceDocLineID = @GRNDetailID
          AND TransactionType = 1
    )
    BEGIN
        RAISERROR(N'This GRN detail already has an inventory receipt transaction.', 16, 1);
        RETURN;
    END;

    -- Read the warehouse balance before updating it so the movement records the true before/after.
    DECLARE @BalanceBefore DECIMAL(18,4) = 0;
    DECLARE @BalanceExists BIT = 0;

    SELECT @BalanceBefore = ib.CurrentQty,
           @BalanceExists = 1
    FROM dbo.InventoryBalance ib WITH (UPDLOCK, HOLDLOCK)
    WHERE ib.ItemID = @ItemID
      AND ib.WarehouseID = @WarehouseID;

    SET @BalanceBefore = ISNULL(@BalanceBefore, 0);

    -- Keep FIFO layers reconcilable with the aggregate warehouse balance.
    -- Existing stock not represented by batch rows must be reconciled separately;
    -- this procedure deliberately does not absorb an opening difference.
    DECLARE @BatchQtyBefore DECIMAL(18,4) = 0;

    IF @BatchNumber IS NULL
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM dbo.InventoryBatchBalance WITH (UPDLOCK, HOLDLOCK)
            WHERE ItemID = @ItemID
              AND WarehouseID = @WarehouseID
        )
        BEGIN
            RAISERROR(N'This item/warehouse already uses batch balances; enter a batch number for this GRN line.', 16, 1);
            RETURN;
        END;
    END
    ELSE
    BEGIN
        SELECT @BatchQtyBefore = ISNULL(SUM(bb.CurrentQty), 0)
        FROM dbo.InventoryBatchBalance bb WITH (UPDLOCK, HOLDLOCK)
        WHERE bb.ItemID = @ItemID
          AND bb.WarehouseID = @WarehouseID;

        IF @BatchQtyBefore <> @BalanceBefore
        BEGIN
            RAISERROR(N'Existing batch balances do not equal InventoryBalance; reconcile the opening difference before receiving another tracked batch.', 16, 1);
            RETURN;
        END;
    END;

    -- 1) Update item cost, preserving the existing weighted-average behavior.
    UPDATE dbo.Items
    SET AverageCost = @NewAvgCost,
        LastPurchasePrice = @UnitCost,
        ModifiedDate = GETDATE()
    WHERE ItemID = @ItemID;

    -- 2) Update the warehouse stock balance.
    IF @BalanceExists = 1
    BEGIN
        UPDATE dbo.InventoryBalance
        SET CurrentQty = CurrentQty + @ReceivedQty,
            AverageCost = @NewAvgCost,
            LastMovementDate = GETDATE(),
            ModifiedDate = GETDATE()
        WHERE ItemID = @ItemID
          AND WarehouseID = @WarehouseID;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.InventoryBalance
            (ItemID, WarehouseID, CurrentQty, AverageCost, LastMovementDate, ModifiedDate)
        VALUES
            (@ItemID, @WarehouseID, @ReceivedQty, @NewAvgCost, GETDATE(), GETDATE());
    END;

    -- 3) Upsert the unique item/warehouse/batch balance.
    -- SourceType remains NULL because no numeric GRN enum was established.
    IF @BatchNumber IS NOT NULL
    BEGIN
        DECLARE @BatchBalanceID INT;
        DECLARE @BatchCurrentQty DECIMAL(18,4);
        DECLARE @BatchUnitCost DECIMAL(18,4);
        DECLARE @BatchExpiryDate DATE;
        DECLARE @BatchManufactureDate DATE;
        DECLARE @BatchReceiptDate DATE;
        DECLARE @BatchSourceDocNumber NVARCHAR(100);
        DECLARE @BatchNewQty DECIMAL(18,4);
        DECLARE @BatchNewUnitCost DECIMAL(18,4);

        SELECT @BatchBalanceID = bb.BatchBalanceID,
               @BatchCurrentQty = bb.CurrentQty,
               @BatchUnitCost = bb.UnitCost,
               @BatchExpiryDate = bb.ExpiryDate,
               @BatchManufactureDate = bb.ManufactureDate,
               @BatchReceiptDate = bb.ReceiptDate,
               @BatchSourceDocNumber = bb.SourceDocNumber
        FROM dbo.InventoryBatchBalance bb WITH (UPDLOCK, HOLDLOCK)
        WHERE bb.ItemID = @ItemID
          AND bb.WarehouseID = @WarehouseID
          AND bb.BatchNumber = @BatchNumber;

        IF @BatchBalanceID IS NULL
        BEGIN
            INSERT INTO dbo.InventoryBatchBalance
                (ItemID, WarehouseID, BatchNumber, ExpiryDate, ManufactureDate,
                 CurrentQty, UnitCost, ReceiptDate, SourceType, SourceDocNumber, ModifiedDate)
            VALUES
                (@ItemID, @WarehouseID, @BatchNumber, @ExpiryDate, @ManufactureDate,
                 @ReceivedQty, @UnitCost, @GRNDate, NULL, @GRNNumber, GETDATE());
        END
        ELSE
        BEGIN
            IF @BatchCurrentQty < 0
            BEGIN
                RAISERROR(N'Existing batch balance is negative; GRN posting stopped.', 16, 1);
                RETURN;
            END;

            IF @BatchCurrentQty > 0 AND ISNULL(@BatchUnitCost, 0) <= 0
            BEGIN
                RAISERROR(N'Existing batch has no positive unit cost; GRN posting stopped.', 16, 1);
                RETURN;
            END;

            IF @BatchExpiryDate IS NOT NULL AND @ExpiryDate IS NOT NULL
               AND @BatchExpiryDate <> @ExpiryDate
            BEGIN
                RAISERROR(N'The same batch number has conflicting expiry dates.', 16, 1);
                RETURN;
            END;

            IF @BatchManufactureDate IS NOT NULL AND @ManufactureDate IS NOT NULL
               AND @BatchManufactureDate <> @ManufactureDate
            BEGIN
                RAISERROR(N'The same batch number has conflicting manufacture dates.', 16, 1);
                RETURN;
            END;

            SET @BatchNewQty = @BatchCurrentQty + @ReceivedQty;
            IF @BatchNewQty <= 0
            BEGIN
                RAISERROR(N'Updated batch quantity is not positive.', 16, 1);
                RETURN;
            END;

            SET @BatchNewUnitCost = ROUND(
                ((@BatchCurrentQty * ISNULL(@BatchUnitCost, 0)) + (@ReceivedQty * @UnitCost)) / @BatchNewQty,
                4);

            UPDATE dbo.InventoryBatchBalance
            SET CurrentQty = @BatchNewQty,
                UnitCost = @BatchNewUnitCost,
                ExpiryDate = COALESCE(ExpiryDate, @ExpiryDate),
                ManufactureDate = COALESCE(ManufactureDate, @ManufactureDate),
                ReceiptDate = CASE
                    WHEN @BatchReceiptDate IS NULL OR @BatchReceiptDate > @GRNDate THEN @GRNDate
                    ELSE @BatchReceiptDate
                END,
                SourceDocNumber = COALESCE(NULLIF(LTRIM(RTRIM(@BatchSourceDocNumber)), N''), @GRNNumber),
                ModifiedDate = GETDATE()
            WHERE BatchBalanceID = @BatchBalanceID;
        END;
    END;

    -- 4) Record the GRN line, batch, and source references on the inventory movement.
    INSERT INTO dbo.InventoryTransactions
    (
        TransactionDate, TransactionType, ItemID, WarehouseID,
        Quantity, UnitID, UnitCost,
        BalanceBefore, BalanceAfter, CostBefore, CostAfter,
        BatchNumber, ExpiryDate,
        SourceDocType, SourceDocID, SourceDocNumber, SourceDocLineID,
        Notes, CreatedBy, CreatedDate
    )
    VALUES
    (
        GETDATE(),
        1,
        @ItemID,
        @WarehouseID,
        @ReceivedQty,
        @PrimaryUnitID,
        @UnitCost,
        @BalanceBefore,
        @BalanceBefore + @ReceivedQty,
        @CurrentAvgCost,
        @NewAvgCost,
        @BatchNumber,
        @ExpiryDate,
        N'GRN',
        @GRNID,
        @GRNNumber,
        @GRNDetailID,
        N'استلام مشتريات — تحديث تلقائي',
        NULLIF(@UserID, 0),
        GETDATE()
    );
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdatePOAfterGRN
-- ============================================================

-- =============================================
-- 5) SP: تحديث أمر الشراء بعد الاستلام
-- =============================================
CREATE PROCEDURE sp_UpdatePOAfterGRN
    @PurchaseOrderID INT
AS
BEGIN
    SET NOCOUNT ON;

    -- تحديث الكمية المستلمة لكل سطر
    UPDATE pod
    SET pod.ReceivedQty = ISNULL(grn_totals.TotalReceived, 0),
        pod.LineStatus = CASE
            WHEN ISNULL(grn_totals.TotalReceived, 0) = 0 THEN 1
            WHEN ISNULL(grn_totals.TotalReceived, 0) < pod.OrderedQty THEN 2
            ELSE 3
        END
    FROM PurchaseOrderDetails pod
    LEFT JOIN (
        SELECT
            grd.PODetailID,
            SUM(ISNULL(grd.AcceptedQty, grd.ReceivedQty)) AS TotalReceived
        FROM GoodsReceiptDetails grd
        INNER JOIN GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
        WHERE grn.GRNStatus != 4 -- exclude cancelled
          AND grd.PODetailID IS NOT NULL
        GROUP BY grd.PODetailID
    ) grn_totals ON pod.PODetailID = grn_totals.PODetailID
    WHERE pod.PurchaseOrderID = @PurchaseOrderID;

    -- تحديث حالة أمر الشراء
    DECLARE @TotalLines INT, @FullyReceived INT, @PartiallyReceived INT;

    SELECT
        @TotalLines = COUNT(*),
        @FullyReceived = SUM(CASE WHEN LineStatus = 3 THEN 1 ELSE 0 END),
        @PartiallyReceived = SUM(CASE WHEN LineStatus = 2 THEN 1 ELSE 0 END)
    FROM PurchaseOrderDetails
    WHERE PurchaseOrderID = @PurchaseOrderID
      AND LineStatus != 4;

    UPDATE PurchaseOrders
    SET POStatus = CASE
            WHEN @FullyReceived = @TotalLines THEN 7          -- استلام كامل
            WHEN @FullyReceived > 0 OR @PartiallyReceived > 0 THEN 6  -- استلام جزئي
            ELSE POStatus
        END,
        ModifiedDate = GETDATE()
    WHERE PurchaseOrderID = @PurchaseOrderID
      AND POStatus IN (3,5, 6); -- فقط المرسل أو الاستلام الجزئي
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdatePOReceivedQty
-- ============================================================


-- SP: تحديث الكمية المستلمة في أمر الشراء
CREATE   PROCEDURE [dbo].[sp_UpdatePOReceivedQty]
    @PurchaseOrderID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- تحديث الكمية المستلمة لكل سطر
    UPDATE pod
    SET pod.ReceivedQty = ISNULL(grn.TotalReceived, 0),
        pod.LineStatus = CASE
            WHEN ISNULL(grn.TotalReceived, 0) = 0 THEN 1           -- مفتوح
            WHEN ISNULL(grn.TotalReceived, 0) < pod.OrderedQty THEN 2 -- جزئي
            ELSE 3                                                    -- كامل
        END
    FROM PurchaseOrderDetails pod
    LEFT JOIN (
        SELECT grd.PODetailID, SUM(grd.AcceptedQty) AS TotalReceived
        FROM GoodsReceiptDetails grd
        INNER JOIN GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
        WHERE grn.GRNStatus != 4 -- exclude cancelled
        GROUP BY grd.PODetailID
    ) grn ON pod.PODetailID = grn.PODetailID
    WHERE pod.PurchaseOrderID = @PurchaseOrderID;
    
    -- تحديث حالة أمر الشراء
    DECLARE @TotalLines INT, @FullyReceived INT, @PartiallyReceived INT;
    
    SELECT @TotalLines = COUNT(*),
           @FullyReceived = SUM(CASE WHEN LineStatus = 3 THEN 1 ELSE 0 END),
           @PartiallyReceived = SUM(CASE WHEN LineStatus = 2 THEN 1 ELSE 0 END)
    FROM PurchaseOrderDetails
    WHERE PurchaseOrderID = @PurchaseOrderID
      AND LineStatus != 4; -- exclude cancelled lines
    
    UPDATE PurchaseOrders
    SET POStatus = CASE
            WHEN @FullyReceived = @TotalLines THEN 5     -- استلام كامل
            WHEN @FullyReceived > 0 OR @PartiallyReceived > 0 THEN 4  -- استلام جزئي
            ELSE POStatus
        END,
        ModifiedDate = GETDATE()
    WHERE PurchaseOrderID = @PurchaseOrderID
      AND POStatus NOT IN (1, 6, 7); -- لا تحدث المسودة أو المغلق أو الملغي
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdatePRStatusAfterPO
-- ============================================================


-- =============================================
-- 7) SP: تحديث حالة PR بعد إنشاء PO
-- =============================================
CREATE   PROCEDURE sp_UpdatePRStatusAfterPO
    @RequestID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- هل كل السطور المعتمدة تم تغطيتها بالكامل؟
    DECLARE @TotalLines INT, @CoveredLines INT;
    
    SELECT 
        @TotalLines = COUNT(*),
        @CoveredLines = SUM(CASE WHEN IsFullyCovered = 1 THEN 1 ELSE 0 END)
    FROM vw_PRRemainingQty
    WHERE RequestID = @RequestID;
    
    -- لو كل السطور متغطية → تم التنفيذ
    IF @TotalLines > 0 AND @CoveredLines = @TotalLines
    BEGIN
        UPDATE PurchaseRequests
        SET RequestStatus = 5, -- تم التنفيذ
            ModifiedDate = GETDATE()
        WHERE RequestID = @RequestID
          AND RequestStatus = 3; -- معتمد فقط
          
        PRINT N'  ✅ PR Status → تم التنفيذ (5)';
    END
    ELSE IF @CoveredLines > 0
    BEGIN
        -- بعض السطور متغطية (اختياري: نضيف حالة جزئي مستقبلاً)
        PRINT N'  ℹ️ PR مغطى جزئياً: ' + CAST(@CoveredLines AS NVARCHAR(5)) 
              + ' من ' + CAST(@TotalLines AS NVARCHAR(5));
    END
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UpdateSupplierBalanceAfterInvoice
-- ============================================================


-- =============================================
-- 6) SP: تحديث رصيد المورد بعد الفاتورة
-- =============================================
CREATE   PROCEDURE sp_UpdateSupplierBalanceAfterInvoice
    @SupplierID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @InvoiceTotal DECIMAL(18,2);
    DECLARE @PaidTotal DECIMAL(18,2);
    DECLARE @ReturnTotal DECIMAL(18,2);

    -- إجمالي الفواتير غير الملغية
    SELECT @InvoiceTotal = ISNULL(SUM(TotalAmount), 0),
           @PaidTotal = ISNULL(SUM(PaidAmount), 0)
    FROM dbo.PurchaseInvoices
    WHERE SupplierID = @SupplierID
      AND InvoiceStatus NOT IN (7);

    -- إجمالي المرتجعات غير الملغية
    SELECT @ReturnTotal = ISNULL(SUM(TotalAmount), 0)
    FROM dbo.PurchaseReturns
    WHERE SupplierID = @SupplierID
      AND ReturnStatus NOT IN (4);

    -- تحديث رصيد المورد
    UPDATE dbo.Suppliers
    SET CurrentBalance = @InvoiceTotal - @PaidTotal - @ReturnTotal,
        ModifiedDate = GETDATE()
    WHERE SupplierID = @SupplierID;
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_UserLogin
-- ============================================================


-- =============================================
-- SPs خاصة بالأمان
-- =============================================

-- SP: تسجيل الدخول
CREATE   PROCEDURE [dbo].[sp_UserLogin]
    @Username NVARCHAR(50),
    @PasswordHash NVARCHAR(256),
    @IPAddress NVARCHAR(50) = NULL,
    @MachineName NVARCHAR(100) = NULL,
    @Success BIT OUTPUT,
    @UserID INT OUTPUT,
    @FullName NVARCHAR(100) OUTPUT,
    @RoleName NVARCHAR(100) OUTPUT,
    @Message NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    SET @Success = 0;
    SET @UserID = 0;
    SET @FullName = N'';
    SET @RoleName = N'';
    
    DECLARE @StoredHash NVARCHAR(256);
    DECLARE @IsActive BIT, @IsLocked BIT;
    DECLARE @FailedAttempts INT, @MaxAttempts INT;
    DECLARE @LockoutEnd DATETIME2;
    DECLARE @RoleID INT;
    
    -- البحث عن المستخدم
    SELECT @UserID = UserID, @StoredHash = PasswordHash,
           @IsActive = IsActive, @IsLocked = IsLocked,
           @FailedAttempts = FailedLoginAttempts,
           @MaxAttempts = MaxFailedAttempts,
           @LockoutEnd = LockoutEnd,
           @FullName = FullName, @RoleID = RoleID
    FROM SystemUsers
    WHERE Username = @Username;
    
    -- المستخدم غير موجود
    IF @UserID IS NULL OR @UserID = 0
    BEGIN
        SET @Message = N'اسم المستخدم أو كلمة المرور غير صحيحة';
        INSERT INTO LoginHistory (Username, LoginStatus, IPAddress, MachineName, FailureReason)
        VALUES (@Username, 2, @IPAddress, @MachineName, N'مستخدم غير موجود');
        RETURN;
    END
    
    -- الحساب غير نشط
    IF @IsActive = 0
    BEGIN
        SET @Message = N'هذا الحساب غير نشط. تواصل مع مدير النظام';
        INSERT INTO LoginHistory (UserID, Username, LoginStatus, IPAddress, MachineName, FailureReason)
        VALUES (@UserID, @Username, 3, @IPAddress, @MachineName, N'حساب غير نشط');
        SET @UserID = 0;
        RETURN;
    END
    
    -- الحساب محظور
    IF @IsLocked = 1 AND (@LockoutEnd IS NULL OR @LockoutEnd > GETDATE())
    BEGIN
        SET @Message = N'هذا الحساب محظور مؤقتاً. حاول لاحقاً';
        INSERT INTO LoginHistory (UserID, Username, LoginStatus, IPAddress, MachineName, FailureReason)
        VALUES (@UserID, @Username, 3, @IPAddress, @MachineName, N'حساب محظور');
        SET @UserID = 0;
        RETURN;
    END
    
    -- إلغاء الحظر لو انتهت المدة
    IF @IsLocked = 1 AND @LockoutEnd <= GETDATE()
    BEGIN
        UPDATE SystemUsers SET IsLocked = 0, FailedLoginAttempts = 0, LockoutEnd = NULL
        WHERE UserID = @UserID;
        SET @IsLocked = 0;
        SET @FailedAttempts = 0;
    END
    
    -- التحقق من كلمة المرور
    IF @StoredHash = @PasswordHash
    BEGIN
        -- دخول ناجح
        SET @Success = 1;
        SELECT @RoleName = RoleNameAr FROM UserRoles WHERE RoleID = @RoleID;
        SET @Message = N'تم تسجيل الدخول بنجاح. مرحباً ' + @FullName;
        
        UPDATE SystemUsers
        SET LastLoginDate = GETDATE(),
            LastLoginIP = @IPAddress,
            LastLoginMachine = @MachineName,
            FailedLoginAttempts = 0,
            IsLocked = 0,
            LockoutEnd = NULL
        WHERE UserID = @UserID;
        
        INSERT INTO LoginHistory (UserID, Username, LoginStatus, IPAddress, MachineName)
        VALUES (@UserID, @Username, 1, @IPAddress, @MachineName);
    END
    ELSE
    BEGIN
        -- كلمة مرور خطأ
        SET @FailedAttempts = @FailedAttempts + 1;
        
        IF @FailedAttempts >= @MaxAttempts
        BEGIN
            UPDATE SystemUsers
            SET FailedLoginAttempts = @FailedAttempts,
                IsLocked = 1,
                LockoutEnd = DATEADD(MINUTE, 30, GETDATE())
            WHERE UserID = @UserID;
            SET @Message = N'تم حظر الحساب لمدة 30 دقيقة بسبب محاولات فاشلة متكررة';
        END
        ELSE
        BEGIN
            UPDATE SystemUsers SET FailedLoginAttempts = @FailedAttempts WHERE UserID = @UserID;
            SET @Message = N'كلمة المرور غير صحيحة. المحاولات المتبقية: ' 
                         + CAST(@MaxAttempts - @FailedAttempts AS NVARCHAR(5));
        END
        
        INSERT INTO LoginHistory (UserID, Username, LoginStatus, IPAddress, MachineName, FailureReason)
        VALUES (@UserID, @Username, 2, @IPAddress, @MachineName, N'كلمة مرور خطأ');
        
        SET @UserID = 0;
    END
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_ValidatePOQtyAgainstPR
-- ============================================================


-- =============================================
-- 8) SP: التحقق من صحة كمية PO مقارنة بـ PR
-- =============================================
CREATE   PROCEDURE sp_ValidatePOQtyAgainstPR
    @RequestDetailID INT,
    @OrderedQty DECIMAL(18,4),
    @ExcludePOID INT = 0,
    -- لو تعديل، نستثني الـ PO الحالي
    @IsValid BIT OUTPUT,
    @Message NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @RemainingQty DECIMAL(18,4);
    DECLARE @ApprovedQty DECIMAL(18,4);
    
    SELECT 
        @RemainingQty = RemainingToOrder,
        @ApprovedQty = ApprovedQty
    FROM vw_PRRemainingQty
    WHERE RequestDetailID = @RequestDetailID;
    
    -- لو بنعدل PO موجود، نرجع الكمية المطلوبة فيه للحساب
    IF @ExcludePOID > 0
    BEGIN
        DECLARE @ExistingQty DECIMAL(18,4);
        SELECT @ExistingQty = ISNULL(OrderedQty, 0)
        FROM PurchaseOrderDetails
        WHERE RequestDetailID = @RequestDetailID
          AND PurchaseOrderID = @ExcludePOID;
        
        SET @RemainingQty = @RemainingQty + ISNULL(@ExistingQty, 0);
    END
    
    IF @RemainingQty IS NULL
    BEGIN
        SET @IsValid = 1; -- Manual PO, no PR constraint
        SET @Message = N'';
        RETURN;
    END
    
    IF @OrderedQty > @RemainingQty
    BEGIN
        SET @IsValid = 0;
        SET @Message = N'الكمية المطلوبة (' + CAST(@OrderedQty AS NVARCHAR(20)) 
                     + N') تتجاوز الكمية المتبقية من طلب الشراء (' 
                     + CAST(@RemainingQty AS NVARCHAR(20)) + N')';
    END
    ELSE
    BEGIN
        SET @IsValid = 1;
        SET @Message = N'';
    END
END;

GO

-- ============================================================
-- SQL_STORED_PROCEDURE: dbo.sp_WriteAuditLog
-- ============================================================


-- SP: تسجيل عملية في سجل المراقبة
CREATE   PROCEDURE [dbo].[sp_WriteAuditLog]
    @UserID INT = NULL,
    @ActionType TINYINT,
    @TableName NVARCHAR(100),
    @RecordID NVARCHAR(50) = NULL,
    @OldValues NVARCHAR(MAX) = NULL,
    @NewValues NVARCHAR(MAX) = NULL,
    @ChangedColumns NVARCHAR(MAX) = NULL,
    @ModuleName NVARCHAR(100) = NULL,
    @Description NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @Username NVARCHAR(50);
    SELECT @Username = Username FROM SystemUsers WHERE UserID = @UserID;
    
    INSERT INTO AuditLog (
        UserID, Username, ActionType, TableName, RecordID,
        OldValues, NewValues, ChangedColumns,
        ModuleName, Description
    ) VALUES (
        @UserID, @Username, @ActionType, @TableName, @RecordID,
        @OldValues, @NewValues, @ChangedColumns,
        @ModuleName, @Description
    );
END;

GO

-- ============================================================
-- VIEW: dbo.vw_AccountLedger
-- ============================================================
CREATE VIEW dbo.vw_AccountLedger AS
SELECT 
    jd.JournalDetailID,
    je.JournalID,
    je.JournalNumber,
    je.JournalDate,
    je.Description,
    jd.AccountID,
    coa.AccountCode,
    coa.AccountNameAr,
    jd.DebitAmount,
    jd.CreditAmount,
    SUM(jd.DebitAmount - jd.CreditAmount) OVER (PARTITION BY jd.AccountID ORDER BY je.JournalDate, je.JournalID ROWS UNBOUNDED PRECEDING) AS RunningBalance
FROM dbo.JournalEntryDetails jd
JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID
JOIN dbo.ChartOfAccounts coa ON jd.AccountID=coa.AccountID
WHERE ISNULL(je.JournalStatus,1)!=3;

GO

-- ============================================================
-- VIEW: dbo.vw_BatchBalances
-- ============================================================


-- =============================================
-- 2) View: أرصدة الباتشات مع الصلاحية
-- =============================================
CREATE   VIEW dbo.vw_BatchBalances
AS
SELECT
    bb.BatchBalanceID,
    bb.ItemID,
    i.ItemCode,
    i.ItemNameAr,
    bb.WarehouseID,
    w.WarehouseNameAr,
    bb.BatchNumber,
    bb.ExpiryDate,
    bb.ManufactureDate,
    bb.CurrentQty,
    bb.UnitCost,
    (bb.CurrentQty * bb.UnitCost) AS BatchValue,
    u.UnitNameAr AS UnitName,
    bb.ReceiptDate,
    bb.SourceType,
    CASE bb.SourceType
        WHEN 1 THEN N'شراء'
        WHEN 2 THEN N'إنتاج'
        WHEN 3 THEN N'مرتجع'
        WHEN 4 THEN N'تحويل'
        WHEN 5 THEN N'جرد'
    END AS SourceTypeName,
    bb.SourceDocNumber,

    -- تنبيه الصلاحية
    CASE
        WHEN bb.ExpiryDate IS NULL THEN 0
        WHEN bb.ExpiryDate <= CAST(GETDATE() AS DATE) THEN 3       -- منتهي
        WHEN bb.ExpiryDate <= DATEADD(DAY, 30, GETDATE()) THEN 2   -- أقل من شهر
        WHEN bb.ExpiryDate <= DATEADD(DAY, 90, GETDATE()) THEN 1   -- أقل من 3 شهور
        ELSE 0
    END AS ExpiryAlert,
    CASE
        WHEN bb.ExpiryDate IS NULL THEN N'بدون صلاحية'
        WHEN bb.ExpiryDate <= CAST(GETDATE() AS DATE) THEN N'منتهي الصلاحية ❌'
        WHEN bb.ExpiryDate <= DATEADD(DAY, 30, GETDATE()) THEN N'أقل من شهر ⚠️'
        WHEN bb.ExpiryDate <= DATEADD(DAY, 90, GETDATE()) THEN N'أقل من 3 شهور 🔶'
        ELSE N'صالح ✅'
    END AS ExpiryAlertName,
    DATEDIFF(DAY, GETDATE(), bb.ExpiryDate) AS DaysToExpiry

FROM dbo.InventoryBatchBalance bb
INNER JOIN dbo.Items i ON bb.ItemID = i.ItemID
INNER JOIN dbo.Units u ON i.PrimaryUnitID = u.UnitID
INNER JOIN dbo.Warehouses w ON bb.WarehouseID = w.WarehouseID
WHERE bb.CurrentQty > 0;

GO

-- ============================================================
-- VIEW: dbo.vw_BOMReport
-- ============================================================
CREATE   VIEW vw_BOMReport
AS
-- البيانات الأساسية + المكونات
SELECT 
    'D' AS RecordType,
    b.BOMID,
    b.BOMCode,
    b.BOMName,
    b.BOMVersion,
    p.ItemCode AS ProductCode,
    p.ItemNameAr AS ProductName,
    b.BatchSize,
    bu.UnitNameAr AS BatchUnitName,
    b.OutputQty,
    ou.UnitNameAr AS OutputUnitName,
    b.WastePercent AS BOMWastePercent,
    b.EstimatedTime,
    CASE b.BOMStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'نشط'
        WHEN 3 THEN N'متوقف'
        WHEN 4 THEN N'ملغي'
    END AS StatusName,
    b.EffectiveFrom,
    b.EffectiveTo,
    b.LaborCostPerBatch,
    b.OverheadCostPerBatch,
    b.Instructions,
    b.SafetyNotes,
    b.QualityNotes,
    b.Notes AS BOMNotes,
    bd.LineNumber,
    bd.MaterialType,
    CASE bd.MaterialType WHEN 1 THEN N'خام' ELSE N'تعبئة' END AS MaterialTypeName,
    i.ItemCode,
    i.ItemNameAr AS ItemName,
    bd.Quantity,
    u.UnitNameAr AS UnitName,
    bd.WastePercent AS DetailWastePercent,
    bd.UnitCost,
    (bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100) * ISNULL(bd.UnitCost, 0)) AS LineCost,
    bd.Notes AS DetailNotes,
    1 AS SectionOrder,
    bd.MaterialType AS SubOrder1,
    bd.LineNumber AS SubOrder2
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items p ON b.ProductItemID = p.ItemID
INNER JOIN dbo.Units bu ON b.BatchUnitID = bu.UnitID
INNER JOIN dbo.Units ou ON b.OutputUnitID = ou.UnitID
LEFT JOIN dbo.BOMDetails bd ON b.BOMID = bd.BOMID
LEFT JOIN dbo.Items i ON bd.ItemID = i.ItemID
LEFT JOIN dbo.Units u ON bd.UnitID = u.UnitID

UNION ALL

-- المراحل
SELECT 
    'S' AS RecordType,
    b.BOMID,
    b.BOMCode,
    b.BOMName,
    b.BOMVersion,
    p.ItemCode AS ProductCode,
    p.ItemNameAr AS ProductName,
    b.BatchSize,
    bu.UnitNameAr AS BatchUnitName,
    b.OutputQty,
    ou.UnitNameAr AS OutputUnitName,
    b.WastePercent AS BOMWastePercent,
    b.EstimatedTime,
    CASE b.BOMStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'نشط'
        WHEN 3 THEN N'متوقف'
        WHEN 4 THEN N'ملغي'
    END AS StatusName,
    b.EffectiveFrom,
    b.EffectiveTo,
    b.LaborCostPerBatch,
    b.OverheadCostPerBatch,
    b.Instructions,
    b.SafetyNotes,
    b.QualityNotes,
    b.Notes AS BOMNotes,
    bs.StageOrder AS LineNumber,
    0 AS MaterialType,
    ps.StageNameAr AS MaterialTypeName,
    CAST(bs.Duration AS NVARCHAR(50)) AS ItemCode,
    bs.Temperature AS ItemName,
    bs.Duration AS Quantity,
    bs.Speed AS UnitName,
    CASE WHEN bs.QualityCheckRequired = 1 THEN 100 ELSE 0 END AS DetailWastePercent,
    0 AS UnitCost,
    0 AS LineCost,
    bs.Instructions AS DetailNotes,
    2 AS SectionOrder,
    bs.StageOrder AS SubOrder1,
    0 AS SubOrder2
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items p ON b.ProductItemID = p.ItemID
INNER JOIN dbo.Units bu ON b.BatchUnitID = bu.UnitID
INNER JOIN dbo.Units ou ON b.OutputUnitID = ou.UnitID
LEFT JOIN dbo.BOMStages bs ON b.BOMID = bs.BOMID
LEFT JOIN dbo.ProductionStages ps ON bs.StageID = ps.StageID;

GO

-- ============================================================
-- VIEW: dbo.vw_BOMSummary
-- ============================================================


-- =============================================
-- Views خاصة بالتصنيع
-- =============================================

-- View: ملخص الوصفات
CREATE   VIEW [dbo].[vw_BOMSummary]
AS
SELECT 
    b.BOMID,
    b.BOMCode,
    b.BOMName,
    b.BOMVersion,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    b.BatchSize,
    bu.UnitNameAr AS BatchUnitName,
    b.OutputQty,
    ou.UnitNameAr AS OutputUnitName,
    b.WastePercent,
    b.EstimatedTime,
    CASE b.BOMStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'نشط'
        WHEN 3 THEN N'متوقف'
        WHEN 4 THEN N'ملغي'
    END AS StatusName,
    -- عدد المكونات
    (SELECT COUNT(*) FROM BOMDetails bd WHERE bd.BOMID = b.BOMID AND bd.MaterialType = 1) AS RawMaterialCount,
    (SELECT COUNT(*) FROM BOMDetails bd WHERE bd.BOMID = b.BOMID AND bd.MaterialType = 2) AS PackagingMaterialCount,
    -- إجمالي تكلفة المكونات
    ISNULL((SELECT SUM(Quantity * (1 + ISNULL(WastePercent,0)/100) * ISNULL(UnitCost,0)) 
            FROM BOMDetails bd WHERE bd.BOMID = b.BOMID), 0) AS TotalMaterialCost,
    b.LaborCostPerBatch,
    b.OverheadCostPerBatch,
    ISNULL((SELECT SUM(Quantity * (1 + ISNULL(WastePercent,0)/100) * ISNULL(UnitCost,0)) 
            FROM BOMDetails bd WHERE bd.BOMID = b.BOMID), 0) 
        + ISNULL(b.LaborCostPerBatch, 0) 
        + ISNULL(b.OverheadCostPerBatch, 0) AS TotalBatchCost,
    b.IsActive
FROM BillOfMaterials b
INNER JOIN Items i ON b.ProductItemID = i.ItemID
INNER JOIN Units bu ON b.BatchUnitID = bu.UnitID
INNER JOIN Units ou ON b.OutputUnitID = ou.UnitID;

GO

-- ============================================================
-- VIEW: dbo.vw_CashBoxStatement
-- ============================================================
CREATE VIEW dbo.vw_CashBoxStatement AS
SELECT 
    cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
    rv.ReceiptDate AS TransactionDate,
    rv.ReceiptNumber AS TransactionNo,
    'Receipt' AS TransactionType,
    N'سند قبض' AS TransactionTypeAr,
    rv.Amount AS DebitAmount,
    0 AS CreditAmount,
    rv.Description,
    rv.ReceiptStatus AS Status,
    rv.ReceivedFrom AS EntityName,
    0 AS Balance
FROM dbo.CashBoxes cb
JOIN dbo.ReceiptVouchers rv ON cb.CashBoxID = rv.CashBoxID AND rv.ReceiptStatus=2
UNION ALL
SELECT 
    cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
    pv.PaymentDate,
    pv.PaymentNumber,
    'Payment',
    N'سند صرف',
    0,
    pv.Amount,
    pv.Description,
    pv.PaymentStatus,
    pv.PaidTo,
    0
FROM dbo.CashBoxes cb
JOIN dbo.PaymentVouchers pv ON cb.CashBoxID = pv.CashBoxID AND pv.PaymentStatus=2
UNION ALL
SELECT 
    cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
    ct.TransferDate,
    ct.TransferNo,
    CASE WHEN ct.FromCashBoxID=cb.CashBoxID THEN 'TransferOut' ELSE 'TransferIn' END,
    CASE WHEN ct.FromCashBoxID=cb.CashBoxID THEN N'تحويل صادر' ELSE N'تحويل وارد' END,
    CASE WHEN ct.ToCashBoxID=cb.CashBoxID THEN ct.Amount ELSE 0 END,
    CASE WHEN ct.FromCashBoxID=cb.CashBoxID THEN ct.Amount ELSE 0 END,
    ct.Description,
    ct.Status,
    NULL,
    0
FROM dbo.CashBoxes cb
JOIN dbo.CashTransfers ct ON (cb.CashBoxID=ct.FromCashBoxID OR cb.CashBoxID=ct.ToCashBoxID) AND ct.Status=2;

GO

-- ============================================================
-- VIEW: dbo.vw_ContractDeliveryReport
-- ============================================================

CREATE VIEW vw_ContractDeliveryReport
AS
SELECT
    cd.DeliveryID,
    cd.DeliveryNumber,
    cd.DeliveryDate,
    cd.DeliveryStatus,
    CASE cd.DeliveryStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'ملغي'
    END AS DeliveryStatusName,
    cd.ContractOrderID,
    co.ContractNumber,
    cd.CustomerID,
    c.CustomerNameAr AS CustomerName,
    cd.WarehouseID,
    w.WarehouseNameAr AS WarehouseName,
    cd.DeliveredBy,
    e.FullNameAr AS DeliveredByName,
    cd.ReceivedByCustomer,
    cd.Notes AS DeliveryNotes,
    cd.CreatedBy,
    uc.FullName AS CreatedByName,
    cd.CreatedDate,
    dd.DeliveryDetailID,
    dd.LineNumber,
    dd.ContractDetailID,
    dd.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    dd.UnitID,
    u.UnitNameAr AS UnitName,
    cod.OrderedQty,
    cod.ProducedQty,
    cod.DeliveredQty AS PreviousDelivered,
    dd.DeliveredQty,
    (SELECT COUNT(*) FROM ContractDeliveryDetails
     WHERE DeliveryID = cd.DeliveryID) AS TotalItems
FROM ContractDeliveries cd
INNER JOIN ContractOrders co ON cd.ContractOrderID = co.ContractOrderID
INNER JOIN Customers c ON cd.CustomerID = c.CustomerID
INNER JOIN Warehouses w ON cd.WarehouseID = w.WarehouseID
LEFT JOIN Employees e ON cd.DeliveredBy = e.EmployeeID
LEFT JOIN SystemUsers uc ON cd.CreatedBy = uc.UserID
LEFT JOIN ContractDeliveryDetails dd ON cd.DeliveryID = dd.DeliveryID
LEFT JOIN ContractOrderDetails cod ON dd.ContractDetailID = cod.ContractDetailID
LEFT JOIN Items i ON dd.ProductItemID = i.ItemID
LEFT JOIN Units u ON dd.UnitID = u.UnitID;

GO

-- ============================================================
-- VIEW: dbo.vw_ContractOrderFullReport
-- ============================================================

CREATE VIEW vw_ContractOrderFullReport
AS
SELECT
    -- ==========================================
    -- بيانات العقد
    -- ==========================================
    co.ContractOrderID,
    co.ContractNumber,
    co.ContractDate,
    co.ContractStatus,
    CASE co.ContractStatus
         WHEN 1 THEN N'مسودة'
         WHEN 2 THEN N'معتمد'
         WHEN 3 THEN N'جاري التنفيذ'
         WHEN 4 THEN N'جاهز للتسليم'
         WHEN 5 THEN N'مكتمل'
         WHEN 6 THEN N'ملغي'
    END AS ContractStatusName,
    co.MaterialSource,
    CASE co.MaterialSource
        WHEN 1 THEN N'المصنع يوفر الخامات'
        WHEN 2 THEN N'العميل يوفر الخامات'
        WHEN 3 THEN N'مشترك'
    END AS MaterialSourceName,
    co.DeliveryDate,
    co.CustomerPONumber,
    co.TotalManufacturingFee,
    co.TotalMaterialCost,
    co.TotalContractValue,
    co.Notes AS ContractNotes,

    -- ==========================================
    -- العميل
    -- ==========================================
    co.CustomerID,
    c.CustomerNameAr AS CustomerName,
    c.Phone1 AS CustomerPhone1,
     c.Phone2 AS CustomerPhone2,
 c.Mobile AS CustomerMobile,
    -- ==========================================
    -- الاعتماد والإنشاء
    -- ==========================================
    co.ApprovedBy,
    ea.FullNameAr AS ApprovedByName,
    co.ApprovedDate,
    co.CreatedBy,
    uc.Username AS CreatedByName,
    co.CreatedDate,
    co.ModifiedBy,
    um.Username AS ModifiedByName,
    co.ModifiedDate,

    -- ==========================================
    -- تفاصيل المنتجات
    -- ==========================================
    cod.ContractDetailID,
    cod.LineNumber,
    cod.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    cod.BOMID,
    b.BOMName,
    cod.UnitID,
    u.UnitNameAr AS UnitName,
    cod.OrderedQty,
    cod.ProducedQty,
    cod.DeliveredQty,
    cod.ManufacturingFeePerUnit,
    cod.TotalManufacturingFee AS LineTotalManufacturingFee,
    cod.MaterialCostPerUnit,
    cod.TotalMaterialCost AS LineTotalMaterialCost,
    cod.LineTotal,
    cod.ProductionOrderID,
    po.OrderNumber AS ProductionOrderNumber,
    po.OrderStatus AS ProductionOrderStatus,
    cod.LineStatus,
    CASE cod.LineStatus
        WHEN 1 THEN N'في الانتظار'
        WHEN 2 THEN N'جاري التصنيع'
        WHEN 3 THEN N'مكتمل'
        WHEN 4 THEN N'ملغي'
    END AS LineStatusName,
    cod.Notes AS DetailNotes,

    -- ==========================================
    -- إحصائيات العقد
    -- ==========================================
    (SELECT COUNT(*) FROM ContractOrderDetails cod2
     WHERE cod2.ContractOrderID = co.ContractOrderID) AS TotalLines,

    (SELECT COUNT(*) FROM ContractOrderDetails cod2
     WHERE cod2.ContractOrderID = co.ContractOrderID
     AND cod2.LineStatus = 3) AS CompletedLines,

    (SELECT COUNT(*) FROM CustomerMaterialReceipts cmr
     WHERE cmr.ContractOrderID = co.ContractOrderID
     AND cmr.ReceiptStatus = 2) AS TotalReceipts,

    (SELECT COUNT(*) FROM ContractDeliveries cd2
     WHERE cd2.ContractOrderID = co.ContractOrderID
     AND cd2.DeliveryStatus = 2) AS TotalDeliveries

FROM ContractOrders co
INNER JOIN Customers c ON co.CustomerID = c.CustomerID
LEFT JOIN Employees ea ON co.ApprovedBy = ea.EmployeeID
LEFT JOIN SystemUsers uc ON co.CreatedBy = uc.UserID
LEFT JOIN SystemUsers um ON co.ModifiedBy = um.UserID
LEFT JOIN ContractOrderDetails cod ON co.ContractOrderID = cod.ContractOrderID
LEFT JOIN Items i ON cod.ProductItemID = i.ItemID
LEFT JOIN Units u ON cod.UnitID = u.UnitID
LEFT JOIN BillOfMaterials b ON cod.BOMID = b.BOMID
LEFT JOIN ProductionOrders po ON cod.ProductionOrderID = po.ProductionOrderID;

GO

-- ============================================================
-- VIEW: dbo.vw_CurrentStock
-- ============================================================


-- =============================================
-- Views خاصة بالمخزون والمبيعات
-- =============================================

-- View: أرصدة المخزون الحالية
CREATE   VIEW [dbo].[vw_CurrentStock]
AS
SELECT 
    ib.BalanceID,
    i.ItemCode,
    i.ItemNameAr,
    CASE i.ItemType 
        WHEN 1 THEN N'مادة خام' WHEN 2 THEN N'مادة تعبئة'
        WHEN 3 THEN N'نصف مصنع' WHEN 4 THEN N'منتج تام'
        WHEN 5 THEN N'مادة تشغيل'
    END AS ItemTypeName,
    cat.CategoryNameAr,
    w.WarehouseNameAr,
    u.UnitNameAr,
    ib.CurrentQty,
    ib.ReservedQty,
    ib.AvailableQty,
    ib.AverageCost,
    ib.TotalValue,
    i.MinStockLevel,
    i.MaxStockLevel,
    i.ReorderLevel,
    CASE 
        WHEN ib.CurrentQty <= 0 THEN N'نفد المخزون ❌'
        WHEN ib.CurrentQty <= i.MinStockLevel THEN N'تحت الحد الأدنى ⚠️'
        WHEN ib.CurrentQty <= i.ReorderLevel THEN N'يحتاج إعادة طلب 🔄'
        WHEN ib.CurrentQty >= i.MaxStockLevel THEN N'فوق الحد الأقصى 📈'
        ELSE N'طبيعي ✅'
    END AS StockStatus,
    ib.LastMovementDate
FROM InventoryBalance ib
INNER JOIN Items i ON ib.ItemID = i.ItemID
INNER JOIN Warehouses w ON ib.WarehouseID = w.WarehouseID
INNER JOIN Units u ON i.PrimaryUnitID = u.UnitID
LEFT JOIN ItemCategories cat ON i.CategoryID = cat.CategoryID;

GO

-- ============================================================
-- VIEW: dbo.vw_CustomerBalances
-- ============================================================


-- View: أرصدة العملاء
CREATE   VIEW [dbo].[vw_CustomerBalances]
AS
SELECT 
    c.CustomerID,
    c.CustomerCode,
    c.CustomerNameAr,
    c.OpeningBalance,
    ISNULL(inv.TotalInvoices, 0) AS TotalInvoices,
    ISNULL(inv.TotalInvoiceAmount, 0) AS TotalInvoiceAmount,
    ISNULL(inv.TotalPaid, 0) AS TotalPaid,
    ISNULL(inv.TotalRemaining, 0) AS TotalRemaining,
    ISNULL(ret.TotalReturns, 0) AS TotalReturns,
    ISNULL(ret.TotalReturnAmount, 0) AS TotalReturnAmount,
    c.CreditLimit,
    CASE WHEN ISNULL(inv.TotalRemaining, 0) > c.CreditLimit AND c.CreditLimit > 0
         THEN N'تجاوز الحد ⚠️' ELSE N'ضمن الحد ✅'
    END AS CreditStatus,
    c.IsActive
FROM Customers c
LEFT JOIN (
    SELECT CustomerID,
           COUNT(*) AS TotalInvoices,
           SUM(TotalAmount) AS TotalInvoiceAmount,
           SUM(PaidAmount) AS TotalPaid,
           SUM(ISNULL(TotalAmount,0) - ISNULL(PaidAmount,0)) AS TotalRemaining
    FROM SalesInvoices WHERE InvoiceStatus NOT IN (6)
    GROUP BY CustomerID
) inv ON c.CustomerID = inv.CustomerID
LEFT JOIN (
    SELECT CustomerID,
           COUNT(*) AS TotalReturns,
           SUM(TotalAmount) AS TotalReturnAmount
    FROM SalesReturns WHERE ReturnStatus NOT IN (4)
    GROUP BY CustomerID
) ret ON c.CustomerID = ret.CustomerID;

GO

-- ============================================================
-- VIEW: dbo.vw_CustomerDetails
-- ============================================================


-- View: بيانات العملاء الكاملة
CREATE   VIEW [dbo].[vw_CustomerDetails]
AS
SELECT 
    c.CustomerID,
    c.CustomerCode,
    c.CustomerNameAr,
    c.CustomerNameEn,
    CASE c.CustomerType 
        WHEN 1 THEN N'جملة' WHEN 2 THEN N'نص جملة' 
        WHEN 3 THEN N'تجزئة' WHEN 4 THEN N'أونلاين' 
        WHEN 5 THEN N'مؤسسي'
    END AS CustomerTypeName,
    cg.GroupNameAr AS CustomerGroupName,
    c.Phone1,
    c.Mobile,
    c.Email,
    c.Address,
    c.City,
    pt.TermNameAr AS PaymentTermName,
    c.CreditLimit,
    c.CurrentBalance,
    c.DiscountPercent,
    pl.PriceListNameAr,
    c.Rating,
    c.IsActive
FROM Customers c
LEFT JOIN CustomerGroups cg ON c.CustomerGroupID = cg.GroupID
LEFT JOIN PaymentTerms pt ON c.PaymentTermID = pt.PaymentTermID
LEFT JOIN PriceLists pl ON c.PriceListID = pl.PriceListID;

GO

-- ============================================================
-- VIEW: dbo.vw_CustomerMaterialReceiptReport
-- ============================================================

CREATE VIEW vw_CustomerMaterialReceiptReport
AS
SELECT
    r.ReceiptID,
    r.ReceiptNumber,
    r.ReceiptDate,
    r.ReceiptStatus,
    CASE r.ReceiptStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'ملغي'
    END AS ReceiptStatusName,
    r.ContractOrderID,
    co.ContractNumber,
    r.CustomerID,
    c.CustomerNameAr AS CustomerName,
    r.WarehouseID,
    w.WarehouseNameAr AS WarehouseName,
    r.ReceivedBy,
    e.FullNameAr AS ReceivedByName,
    r.Notes AS ReceiptNotes,
    r.CreatedBy,
    uc.FullName AS CreatedByName,
    r.CreatedDate,
    d.ReceiptDetailID,
    d.LineNumber,
    d.ItemID,
    i.ItemCode,
    i.ItemNameAr AS ItemName,
    d.UnitID,
    u.UnitNameAr AS UnitName,
    d.ReceivedQty,
    d.BatchNumber,
    d.ExpiryDate,
    (SELECT COUNT(*) FROM CustomerMaterialReceiptDetails
     WHERE ReceiptID = r.ReceiptID) AS TotalItems
FROM CustomerMaterialReceipts r
INNER JOIN ContractOrders co ON r.ContractOrderID = co.ContractOrderID
INNER JOIN Customers c ON r.CustomerID = c.CustomerID
INNER JOIN Warehouses w ON r.WarehouseID = w.WarehouseID
LEFT JOIN Employees e ON r.ReceivedBy = e.EmployeeID
LEFT JOIN SystemUsers uc ON r.CreatedBy = uc.UserID
LEFT JOIN CustomerMaterialReceiptDetails d ON r.ReceiptID = d.ReceiptID
LEFT JOIN Items i ON d.ItemID = i.ItemID
LEFT JOIN Units u ON d.UnitID = u.UnitID;

GO

-- ============================================================
-- VIEW: dbo.vw_CustomerMaterialReturnReport
-- ============================================================

CREATE VIEW vw_CustomerMaterialReturnReport
AS
SELECT
    r.ReturnID,
    r.ReturnNumber,
    r.ReturnDate,
    r.ReturnStatus,
    CASE r.ReturnStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'ملغي'
    END AS ReturnStatusName,
    r.ContractOrderID,
    co.ContractNumber,
    r.CustomerID,
    c.CustomerNameAr AS CustomerName,
    r.WarehouseID,
    w.WarehouseNameAr AS WarehouseName,
    r.ReturnedBy,
    e.FullNameAr AS ReturnedByName,
    r.Notes AS ReturnNotes,
    r.CreatedBy,
    uc.FullName AS CreatedByName,
    r.CreatedDate,
    d.ReturnDetailID,
    d.LineNumber,
    d.ItemID,
    i.ItemCode,
    i.ItemNameAr AS ItemName,
    d.UnitID,
    u.UnitNameAr AS UnitName,
    d.ReceivedQty,
    d.ConsumedQty,
    d.SurplusQty,
    d.ReturnedQty,
    (SELECT COUNT(*) FROM CustomerMaterialReturnDetails
     WHERE ReturnID = r.ReturnID) AS TotalItems
FROM CustomerMaterialReturns r
INNER JOIN ContractOrders co ON r.ContractOrderID = co.ContractOrderID
INNER JOIN Customers c ON r.CustomerID = c.CustomerID
INNER JOIN Warehouses w ON r.WarehouseID = w.WarehouseID
LEFT JOIN Employees e ON r.ReturnedBy = e.EmployeeID
LEFT JOIN SystemUsers uc ON r.CreatedBy = uc.UserID
LEFT JOIN CustomerMaterialReturnDetails d ON r.ReturnID = d.ReturnID
LEFT JOIN Items i ON d.ItemID = i.ItemID
LEFT JOIN Units u ON d.UnitID = u.UnitID;

GO

-- ============================================================
-- VIEW: dbo.vw_EmployeeDetails
-- ============================================================


-- =============================================
-- Views
-- =============================================

-- View: بيانات الموظفين الكاملة
CREATE   VIEW [dbo].[vw_EmployeeDetails]
AS
SELECT 
    e.EmployeeID,
    e.EmployeeCode,
    e.FullNameAr,
    e.FullNameEn,
    e.NationalID,
    e.BirthDate,
    DATEDIFF(YEAR, e.BirthDate, GETDATE()) AS Age,
    CASE e.Gender WHEN 'M' THEN N'ذكر' WHEN 'F' THEN N'أنثى' END AS GenderName,
    CASE e.MaritalStatus 
        WHEN 1 THEN N'أعزب' WHEN 2 THEN N'متزوج' 
        WHEN 3 THEN N'مطلق' WHEN 4 THEN N'أرمل' 
    END AS MaritalStatusName,
    e.Mobile,
    e.WorkEmail,
    d.DepartmentNameAr,
    j.JobTitleNameAr,
    mgr.FullNameAr AS ManagerName,
    e.HireDate,
    DATEDIFF(YEAR, e.HireDate, GETDATE()) AS YearsOfService,
    CASE e.ContractType 
        WHEN 1 THEN N'دائم' WHEN 2 THEN N'مؤقت' 
        WHEN 3 THEN N'يومي' WHEN 4 THEN N'تدريب' 
    END AS ContractTypeName,
    CASE e.EmployeeStatus 
        WHEN 1 THEN N'نشط' WHEN 2 THEN N'إجازة طويلة' 
        WHEN 3 THEN N'موقوف' WHEN 4 THEN N'مستقيل' 
        WHEN 5 THEN N'مفصول' WHEN 6 THEN N'متقاعد' 
    END AS StatusName,
    e.BasicSalary,
    e.TotalSalary,
    e.IsActive
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentID = d.DepartmentID
LEFT JOIN JobTitles j ON e.JobTitleID = j.JobTitleID
LEFT JOIN Employees mgr ON e.ManagerID = mgr.EmployeeID;

GO

-- ============================================================
-- VIEW: dbo.vw_EmployeeLeaveBalances
-- ============================================================


-- View: أرصدة إجازات الموظفين
CREATE   VIEW [dbo].[vw_EmployeeLeaveBalances]
AS
SELECT 
    e.EmployeeID,
    e.EmployeeCode,
    e.FullNameAr,
    d.DepartmentNameAr,
    lt.LeaveTypeNameAr,
    elb.EntitledDays,
    elb.CarriedForward,
    elb.AdditionalDays,
    elb.TotalEntitled,
    elb.UsedDays,
    elb.PendingDays,
    elb.RemainingDays,
    fy.YearName
FROM EmployeeLeaveBalances elb
INNER JOIN Employees e ON elb.EmployeeID = e.EmployeeID
INNER JOIN LeaveTypes lt ON elb.LeaveTypeID = lt.LeaveTypeID
INNER JOIN FiscalYears fy ON elb.FiscalYearID = fy.FiscalYearID
LEFT JOIN Departments d ON e.DepartmentID = d.DepartmentID;

GO

-- ============================================================
-- VIEW: dbo.vw_EmployeeLoansSummary
-- ============================================================


-- View: ملخص السلف
CREATE   VIEW [dbo].[vw_EmployeeLoansSummary]
AS
SELECT 
    el.LoanID,
    el.LoanNumber,
    e.EmployeeCode,
    e.FullNameAr,
    d.DepartmentNameAr,
    CASE el.LoanType WHEN 1 THEN N'سلفة' WHEN 2 THEN N'قرض' END AS LoanTypeName,
    el.LoanDate,
    el.LoanAmount,
    el.NumberOfInstallments,
    el.InstallmentAmount,
    el.PaidAmount,
    el.RemainingAmount,
    CASE el.LoanStatus
        WHEN 1 THEN N'مقدم' WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'جاري السداد' WHEN 4 THEN N'مسدد بالكامل'
        WHEN 5 THEN N'مرفوض'
    END AS StatusName,
    (SELECT COUNT(*) FROM LoanInstallments li WHERE li.LoanID = el.LoanID AND li.InstallmentStatus = 2) AS PaidInstallments,
    (SELECT COUNT(*) FROM LoanInstallments li WHERE li.LoanID = el.LoanID AND li.InstallmentStatus = 1) AS RemainingInstallments
FROM EmployeeLoans el
INNER JOIN Employees e ON el.EmployeeID = e.EmployeeID
LEFT JOIN Departments d ON e.DepartmentID = d.DepartmentID;

GO

-- ============================================================
-- VIEW: dbo.vw_GoodsReceiptSummary
-- ============================================================


-- =============================================
-- 3) View: ملخص إذن الاستلام
-- =============================================
CREATE   VIEW vw_GoodsReceiptSummary
AS
SELECT
    grn.GRNID,
    grn.GRNNumber,
    grn.GRNDate,
    grn.GRNSource,
    CASE grn.GRNSource
        WHEN 1 THEN N'من أمر شراء'
        WHEN 2 THEN N'استلام مباشر'
    END AS GRNSourceName,

    -- المورد
    s.SupplierID,
    s.SupplierCode,
    s.SupplierNameAr,

    -- أمر الشراء
    grn.PurchaseOrderID,
    po.PONumber AS LinkedPONumber,

    -- المخزن
    w.WarehouseID,
    w.WarehouseNameAr,

    -- الحالة
    grn.GRNStatus,
    CASE grn.GRNStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'مرتبط بفاتورة'
        WHEN 4 THEN N'ملغي'
    END AS GRNStatusName,

    -- الفحص
    grn.InspectionStatus,
    CASE grn.InspectionStatus
        WHEN 1 THEN N'لم يتم الفحص'
        WHEN 2 THEN N'مقبول'
        WHEN 3 THEN N'مرفوض'
    END AS InspectionStatusName,

    -- بيانات المورد
    grn.SupplierInvoiceNo,
    grn.SupplierInvoiceDate,
    grn.DeliveryNoteNo,

    -- المستلم والفاحص
    receiver.FullNameAr AS ReceivedByName,
    inspector.FullNameAr AS InspectedByName,
    approver.FullNameAr AS ApprovedByName,

    -- الإجماليات
    COUNT(grd.GRNDetailID) AS ItemCount,
    SUM(grd.ReceivedQty) AS TotalReceivedQty,
    SUM(grd.AcceptedQty) AS TotalAcceptedQty,
    SUM(grd.RejectedQty) AS TotalRejectedQty,
    SUM(grd.LineTotalCost) AS TotalCost,

    grn.Notes,
    grn.CreatedDate,
    grn.ModifiedDate

FROM GoodsReceiptNotes grn
INNER JOIN Suppliers s ON grn.SupplierID = s.SupplierID
LEFT JOIN PurchaseOrders po ON grn.PurchaseOrderID = po.PurchaseOrderID
LEFT JOIN Warehouses w ON grn.WarehouseID = w.WarehouseID
LEFT JOIN Employees receiver ON grn.ReceivedBy = receiver.EmployeeID
LEFT JOIN Employees inspector ON grn.InspectedBy = inspector.EmployeeID
LEFT JOIN Employees approver ON grn.ApprovedBy = approver.EmployeeID
LEFT JOIN GoodsReceiptDetails grd ON grn.GRNID = grd.GRNID
GROUP BY
    grn.GRNID, grn.GRNNumber, grn.GRNDate, grn.GRNSource,
    s.SupplierID, s.SupplierCode, s.SupplierNameAr,
    grn.PurchaseOrderID, po.PONumber,
    w.WarehouseID, w.WarehouseNameAr,
    grn.GRNStatus, grn.InspectionStatus,
    grn.SupplierInvoiceNo, grn.SupplierInvoiceDate, grn.DeliveryNoteNo,
    receiver.FullNameAr, inspector.FullNameAr, approver.FullNameAr,
    grn.Notes, grn.CreatedDate, grn.ModifiedDate;

GO

-- ============================================================
-- VIEW: dbo.vw_GRNsForInvoice
-- ============================================================


-- =============================================
-- 4) View: أذونات الاستلام المعتمدة المتاحة لفاتورة
-- =============================================
CREATE   VIEW vw_GRNsForInvoice
AS
SELECT
    grn.GRNID,
    grn.GRNNumber,
    grn.GRNDate,
    grn.SupplierID,
    s.SupplierNameAr,
    grn.PurchaseOrderID,
    po.PONumber,
    grn.WarehouseID,
    w.WarehouseNameAr,

    COUNT(grd.GRNDetailID) AS TotalItems,
    SUM(ISNULL(grd.AcceptedQty, grd.ReceivedQty)) AS TotalAcceptedQty,
    SUM(grd.LineTotalCost) AS TotalCost,

    -- هل مرتبط بفاتورة بالفعل؟
    CASE WHEN grn.GRNStatus = 3 THEN 1 ELSE 0 END AS IsLinkedToInvoice

FROM GoodsReceiptNotes grn
INNER JOIN Suppliers s ON grn.SupplierID = s.SupplierID
LEFT JOIN PurchaseOrders po ON grn.PurchaseOrderID = po.PurchaseOrderID
LEFT JOIN Warehouses w ON grn.WarehouseID = w.WarehouseID
LEFT JOIN GoodsReceiptDetails grd ON grn.GRNID = grd.GRNID
WHERE grn.GRNStatus = 2  -- معتمد فقط
GROUP BY
    grn.GRNID, grn.GRNNumber, grn.GRNDate,
    grn.SupplierID, s.SupplierNameAr,
    grn.PurchaseOrderID, po.PONumber,
    grn.WarehouseID, w.WarehouseNameAr,
    grn.GRNStatus;

GO

-- ============================================================
-- VIEW: dbo.vw_InventoryDashboard
-- ============================================================
CREATE   VIEW dbo.vw_InventoryDashboard
AS
SELECT
    (SELECT COUNT(DISTINCT ItemID) FROM dbo.InventoryBalance WHERE CurrentQty > 0) AS ItemsWithStock,

    (SELECT ISNULL(SUM(CurrentQty * ISNULL(AverageCost, 0)), 0) FROM dbo.InventoryBalance WHERE CurrentQty > 0) AS TotalStockValue,

    -- أصناف تحت الحد الأدنى (رصيد موجود لكن أقل من الحد)
    (SELECT COUNT(*)
     FROM dbo.InventoryBalance ib
     INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
     WHERE ib.CurrentQty > 0 
       AND i.MinStockLevel > 0 
       AND ib.CurrentQty <= i.MinStockLevel
    ) AS ItemsBelowMin,

    -- أصناف نفدت (رصيد صفر أو سالب في InventoryBalance)
    (SELECT COUNT(*)
     FROM dbo.InventoryBalance ib
     INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
     WHERE ib.CurrentQty <= 0 
       AND i.IsActive = 1
    ) AS ItemsOutOfStock,

    (SELECT COUNT(*)
     FROM dbo.InventoryBatchBalance bb
     WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
       AND bb.ExpiryDate <= DATEADD(DAY, 30, GETDATE())
       AND bb.ExpiryDate > CAST(GETDATE() AS DATE)
    ) AS BatchesNearExpiry,

    (SELECT COUNT(*)
     FROM dbo.InventoryBatchBalance bb
     WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
       AND bb.ExpiryDate <= CAST(GETDATE() AS DATE)
    ) AS BatchesExpired,

    (SELECT COUNT(*) FROM dbo.InventoryTransactions
     WHERE CAST(TransactionDate AS DATE) = CAST(GETDATE() AS DATE)
    ) AS TodayMovements,

    (SELECT COUNT(*) FROM dbo.Warehouses WHERE IsActive = 1) AS ActiveWarehouses;

GO

-- ============================================================
-- VIEW: dbo.vw_InvoicesForReturn
-- ============================================================
-- =============================================
-- Purchase Returns — Full Patch (مصحح)
-- =============================================



-- =============================================
-- 1) vw_InvoicesForReturn
-- =============================================
CREATE   VIEW dbo.vw_InvoicesForReturn
AS
SELECT
    pi.InvoiceID,
    pi.InvoiceNumber,
    pi.InvoiceDate,
    pi.SupplierID,
    s.SupplierNameAr,
    ISNULL(
        (SELECT TOP 1 pid.WarehouseID
         FROM dbo.PurchaseInvoiceDetails pid
         WHERE pid.InvoiceID = pi.InvoiceID
           AND pid.WarehouseID IS NOT NULL),
        (SELECT TOP 1 grn.WarehouseID
         FROM dbo.GoodsReceiptNotes grn
         WHERE grn.GRNID = pi.GRNID)
    ) AS WarehouseID,
    pi.TotalAmount,
    pi.PaidAmount,
    COUNT(pid2.InvoiceDetailID) AS TotalItems
FROM dbo.PurchaseInvoices pi
INNER JOIN dbo.Suppliers s ON pi.SupplierID = s.SupplierID
LEFT JOIN dbo.PurchaseInvoiceDetails pid2 ON pi.InvoiceID = pid2.InvoiceID
WHERE pi.InvoiceStatus IN (3, 4, 5)
GROUP BY
    pi.InvoiceID, pi.InvoiceNumber, pi.InvoiceDate,
    pi.SupplierID, s.SupplierNameAr,
    pi.TotalAmount, pi.PaidAmount, pi.GRNID;

GO

-- ============================================================
-- VIEW: dbo.vw_LoginHistory
-- ============================================================


-- View: سجل الدخول
CREATE   VIEW [dbo].[vw_LoginHistory]
AS
SELECT 
    lh.LoginID,
    lh.Username,
    u.FullName,
    r.RoleNameAr,
    lh.LoginTime,
    lh.LogoutTime,
    lh.SessionDuration,
    CASE lh.LoginStatus
        WHEN 1 THEN N'ناجح ✅'
        WHEN 2 THEN N'فاشل - كلمة مرور ❌'
        WHEN 3 THEN N'فاشل - محظور 🔒'
    END AS LoginStatusName,
    lh.IPAddress,
    lh.MachineName,
    lh.FailureReason
FROM LoginHistory lh
LEFT JOIN SystemUsers u ON lh.UserID = u.UserID
LEFT JOIN UserRoles r ON u.RoleID = r.RoleID;

GO

-- ============================================================
-- VIEW: dbo.vw_MaterialIssueFullReport
-- ============================================================
-- =============================================
-- View: vw_MaterialIssueFullReport
-- إذن صرف الخامات الشامل
-- =============================================
CREATE   VIEW vw_MaterialIssueFullReport
AS
SELECT 
    -- ==========================================
    -- بيانات رأس الإذن
    -- ==========================================
    mi.IssueID,
    mi.IssueNumber,
    mi.IssueDate,
    mi.IssueType,
    CASE mi.IssueType
        WHEN 1 THEN N'عادي'
        WHEN 2 THEN N'إضافي'
    END AS IssueTypeName,
    mi.IssueStatus,
    CASE mi.IssueStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'ملغي'
    END AS IssueStatusName,
    mi.Notes AS IssueNotes,
    
    -- ==========================================
    -- إجماليات الإذن
    -- ==========================================
    (SELECT SUM(mid2.IssuedQty * ISNULL(mid2.UnitCost, 0)) 
     FROM MaterialIssueDetails mid2 
     WHERE mid2.IssueID = mi.IssueID) AS TotalIssueCost,
    
    (SELECT COUNT(*) 
     FROM MaterialIssueDetails mid2 
     WHERE mid2.IssueID = mi.IssueID) AS TotalItemCount,
    
    (SELECT SUM(mid2.IssuedQty) 
     FROM MaterialIssueDetails mid2 
     WHERE mid2.IssueID = mi.IssueID) AS TotalIssuedQty,
    
    -- ==========================================
    -- أمر التصنيع
    -- ==========================================
    mi.ProductionOrderID,
    po.OrderNumber,
    po.OrderDate,
    po.ProductItemID,
    pi.ItemCode AS ProductCode,
    pi.ItemNameAr AS ProductName,
    po.PlannedQty,
    pu.UnitNameAr AS PlannedUnitName,
    
    -- ==========================================
    -- المخزن
    -- ==========================================
    mi.WarehouseID,
    w.WarehouseNameAr AS WarehouseName,
    
    -- ==========================================
    -- الموظفين
    -- ==========================================
    mi.IssuedBy,
    ei.FullNameAr AS IssuedByName,
    mi.ReceivedBy,
    er.FullNameAr AS ReceivedByName,
    mi.ApprovedBy,
    ea.FullNameAr AS ApprovedByName,
    mi.ApprovedDate,
    
    -- ==========================================
    -- الإنشاء والتعديل
    -- ==========================================
    mi.CreatedBy,
    uc.FullName AS CreatedByName,
    mi.CreatedDate,
    mi.ModifiedBy,
    um.FullName AS ModifiedByName,
    mi.ModifiedDate,
    
    -- ==========================================
    -- تفاصيل الصرف
    -- ==========================================
    mid.IssueDetailID,
    mid.LineNumber,
    mid.POMaterialID,
    mid.ItemID AS MaterialItemID,
    it.ItemCode AS MaterialItemCode,
    it.ItemNameAr AS MaterialItemName,
    pom.MaterialType,
    CASE pom.MaterialType
        WHEN 1 THEN N'خام'
        WHEN 2 THEN N'تعبئة'
    END AS MaterialTypeName,
    mid.UnitID AS MaterialUnitID,
    ut.UnitNameAr AS MaterialUnitName,
    pom.RequiredQty,
    ISNULL(pom.IssuedQty, 0) AS PreviousIssuedQty,
    (pom.RequiredQty - ISNULL(pom.IssuedQty, 0)) AS RemainingQty,
    mid.IssuedQty,
    mid.UnitCost,
    (mid.IssuedQty * ISNULL(mid.UnitCost, 0)) AS LineCost,
    mid.BatchNumber,
    mid.ExpiryDate,
    mid.Notes AS DetailNotes

FROM MaterialIssueNotes mi
-- أمر التصنيع
INNER JOIN ProductionOrders po ON mi.ProductionOrderID = po.ProductionOrderID
INNER JOIN Items pi ON po.ProductItemID = pi.ItemID
INNER JOIN Units pu ON po.PlannedUnitID = pu.UnitID
-- المخزن
INNER JOIN Warehouses w ON mi.WarehouseID = w.WarehouseID
-- الموظفين
LEFT JOIN Employees ei ON mi.IssuedBy = ei.EmployeeID
LEFT JOIN Employees er ON mi.ReceivedBy = er.EmployeeID
LEFT JOIN Employees ea ON mi.ApprovedBy = ea.EmployeeID
-- المستخدمين
LEFT JOIN SystemUsers uc ON mi.CreatedBy = uc.UserID
LEFT JOIN SystemUsers um ON mi.ModifiedBy = um.UserID
-- تفاصيل الصرف
LEFT JOIN MaterialIssueDetails mid ON mi.IssueID = mid.IssueID
LEFT JOIN Items it ON mid.ItemID = it.ItemID
LEFT JOIN Units ut ON mid.UnitID = ut.UnitID
LEFT JOIN ProductionOrderMaterials pom ON mid.POMaterialID = pom.POMaterialID;

GO

-- ============================================================
-- VIEW: dbo.vw_MonthlyAttendanceSummary
-- ============================================================


-- =============================================
-- Views خاصة بالموارد البشرية
-- =============================================

-- View: ملخص الحضور الشهري
CREATE   VIEW [dbo].[vw_MonthlyAttendanceSummary]
AS
SELECT 
    e.EmployeeID,
    e.EmployeeCode,
    e.FullNameAr,
    d.DepartmentNameAr,
    YEAR(a.AttendanceDate) AS AttYear,
    MONTH(a.AttendanceDate) AS AttMonth,
    COUNT(*) AS TotalDays,
    SUM(CASE WHEN a.AttendanceStatus = 1 THEN 1 ELSE 0 END) AS PresentDays,
    SUM(CASE WHEN a.AttendanceStatus = 2 THEN 1 ELSE 0 END) AS AbsentDays,
    SUM(CASE WHEN a.AttendanceStatus = 3 THEN 1 ELSE 0 END) AS LeaveDays,
    SUM(CASE WHEN a.AttendanceStatus = 4 THEN 1 ELSE 0 END) AS MissionDays,
    SUM(CASE WHEN a.AttendanceStatus = 5 THEN 1 ELSE 0 END) AS LateDays,
    SUM(CASE WHEN a.AttendanceStatus = 7 THEN 1 ELSE 0 END) AS HolidayDays,
    SUM(ISNULL(a.OvertimeHours, 0)) AS TotalOvertimeHours,
    SUM(CASE WHEN a.LateMinutes > 0 THEN a.LateMinutes ELSE 0 END) AS TotalLateMinutes
FROM AttendanceRecords a
INNER JOIN Employees e ON a.EmployeeID = e.EmployeeID
LEFT JOIN Departments d ON e.DepartmentID = d.DepartmentID
GROUP BY e.EmployeeID, e.EmployeeCode, e.FullNameAr, d.DepartmentNameAr,
         YEAR(a.AttendanceDate), MONTH(a.AttendanceDate);

GO

-- ============================================================
-- VIEW: dbo.vw_OverheadExpenses
-- ============================================================
CREATE VIEW dbo.vw_OverheadExpenses AS
SELECT 
    oe.ExpenseID,
    ISNULL(oe.ExpenseNumber, 'EXP-'+CAST(oe.ExpenseID AS NVARCHAR)) AS ExpenseNumber,
    oe.ExpenseDate,
    oe.PeriodID,
    ap.PeriodName,
    ap.PeriodNumber,
    oe.FiscalYearID,
    fy.YearName AS FiscalYearName,
    oe.AccountID,
    coa.AccountCode AS ExpenseAccountCode,
    coa.AccountNameAr AS ExpenseAccountNameAr,
    oe.CreditAccountID,
    coa2.AccountCode AS CreditAccountCode,
    coa2.AccountNameAr AS CreditAccountNameAr,
    oe.CostCenterID,
    cc.CostCenterCode,
    cc.CostCenterNameAr,
    oe.Description,
    oe.Amount,
    oe.Reference,
    ISNULL(oe.ExpenseStatus,1) AS ExpenseStatus,
    CASE ISNULL(oe.ExpenseStatus,1) WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
    ISNULL(oe.IsPosted,0) AS IsPosted,
    oe.JournalID,
    je.JournalNumber,
    oe.PaymentMethod,
    oe.CashBoxID,
    cb.CashBoxNameAr,
    oe.BankAccountID,
    ba.BankName,
    oe.Notes,
    oe.CreatedBy,
    su.Username AS CreatedByName,
    oe.CreatedDate,
    oe.ApprovedBy,
    oe.ApprovedDate
FROM dbo.OverheadExpenses oe
LEFT JOIN dbo.AccountingPeriods ap ON oe.PeriodID=ap.PeriodID
LEFT JOIN dbo.FiscalYears fy ON oe.FiscalYearID=fy.FiscalYearID
LEFT JOIN dbo.ChartOfAccounts coa ON oe.AccountID=coa.AccountID
LEFT JOIN dbo.ChartOfAccounts coa2 ON oe.CreditAccountID=coa2.AccountID
LEFT JOIN dbo.CostCenters cc ON oe.CostCenterID=cc.CostCenterID
LEFT JOIN dbo.JournalEntries je ON oe.JournalID=je.JournalID
LEFT JOIN dbo.CashBoxes cb ON oe.CashBoxID=cb.CashBoxID
LEFT JOIN dbo.BankAccounts ba ON oe.BankAccountID=ba.BankAccountID
LEFT JOIN dbo.SystemUsers su ON oe.CreatedBy=su.UserID;

GO

-- ============================================================
-- VIEW: dbo.vw_PayrollSummary
-- ============================================================


-- View: ملخص مسير المرتبات
CREATE   VIEW [dbo].[vw_PayrollSummary]
AS
SELECT 
    pd.PayrollDetailID,
    p.PayrollNumber,
    p.PayrollMonth,
    CASE p.PayrollStatus
        WHEN 1 THEN N'مسودة' WHEN 2 THEN N'محسوب'
        WHEN 3 THEN N'معتمد' WHEN 4 THEN N'مدفوع'
        WHEN 5 THEN N'ملغي'
    END AS PayrollStatusName,
    e.EmployeeCode,
    e.FullNameAr,
    d.DepartmentNameAr,
    j.JobTitleNameAr,
    pd.WorkingDays,
    pd.PresentDays,
    pd.AbsentDays,
    pd.BasicSalary,
    pd.GrossEarnings,
    pd.InsuranceEmployee,
    pd.IncomeTax,
    pd.LoanDeduction,
    pd.PenaltyDeduction,
    pd.AbsenceDeduction,
    pd.TotalDeductions,
    pd.NetSalary,
    CASE pd.PaymentMethod
        WHEN 1 THEN N'تحويل بنكي' WHEN 2 THEN N'كاش' WHEN 3 THEN N'شيك'
    END AS PaymentMethodName,
    pd.IsPaid
FROM PayrollDetails pd
INNER JOIN Payroll p ON pd.PayrollID = p.PayrollID
INNER JOIN Employees e ON pd.EmployeeID = e.EmployeeID
LEFT JOIN Departments d ON e.DepartmentID = d.DepartmentID
LEFT JOIN JobTitles j ON e.JobTitleID = j.JobTitleID;

GO

-- ============================================================
-- VIEW: dbo.vw_POLinesForGRN
-- ============================================================
CREATE   VIEW vw_POLinesForGRN
AS
SELECT
    pod.PODetailID,
    pod.PurchaseOrderID,
    po.PONumber,
    pod.LineNumber,
    pod.ItemID,
    i.ItemCode,
    i.ItemNameAr,
    i.ItemNameEn,
    pod.UnitID,
    u.UnitNameAr AS UnitName,
    pod.OrderedQty,
    pod.ReceivedQty,
    (pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) AS RemainingToReceive,
    pod.UnitPrice,
    pod.TaxRate,
    pod.LineStatus,

    CASE
        WHEN ISNULL(pod.ReceivedQty, 0) >= pod.OrderedQty THEN 1
        ELSE 0
    END AS IsFullyReceived,

    i.ShelfLifeDays,
    i.ItemType

FROM PurchaseOrderDetails pod
INNER JOIN PurchaseOrders po ON pod.PurchaseOrderID = po.PurchaseOrderID
INNER JOIN Items i ON pod.ItemID = i.ItemID
INNER JOIN Units u ON pod.UnitID = u.UnitID
WHERE po.POStatus IN (3, 5, 6) -- ✅ تم التعديل هنا
  AND pod.LineStatus IN (1, 2)
  AND (pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) > 0;

GO

-- ============================================================
-- VIEW: dbo.vw_ProductionBatchFullReport
-- ============================================================
-- =============================================
-- View: vw_ProductionBatchFullReport
-- دفعات الإنتاج الشامل
-- =============================================
CREATE   VIEW vw_ProductionBatchFullReport
AS
SELECT
    -- ==========================================
    -- بيانات الدفعة
    -- ==========================================
    pb.BatchID,
    pb.BatchNumber,
    pb.BatchDate,
    pb.ExpiryDate,
    pb.BatchStatus,
    CASE pb.BatchStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'قيد التصنيع'
        WHEN 3 THEN N'اكتمل التصنيع'
        WHEN 4 THEN N'في فحص الجودة'
        WHEN 5 THEN N'مقبول'
        WHEN 6 THEN N'مرفوض'
    END AS BatchStatusName,
    
    -- ==========================================
    -- الكميات
    -- ==========================================
    pb.PlannedQty,
    pb.ProducedQty,
    pb.GoodQty,
    pb.RejectedQty,
    pb.WasteQty,
    pb.SampleQty,
    
    -- ==========================================
    -- التكاليف
    -- ==========================================
    pb.MaterialCost,
    pb.LaborCost,
    pb.OverheadCost,
    pb.TotalCost,
    pb.UnitCost,
    
    -- ==========================================
    -- أوقات التصنيع
    -- ==========================================
    pb.StartTime,
    pb.EndTime,
    DATEDIFF(MINUTE, pb.StartTime, pb.EndTime) AS DurationMinutes,
    
    -- ==========================================
    -- فحص الجودة
    -- ==========================================
    pb.QCResult,
    CASE pb.QCResult
        WHEN 1 THEN N'ناجح'
        WHEN 2 THEN N'فاشل'
        WHEN 3 THEN N'ناجح بتحفظ'
    END AS QCResultName,
    pb.QCInspectedBy,
    qce.FullNameAr AS QCInspectorName,
    pb.QCInspectionDate,
    pb.QCNotes,
    pb.Notes AS BatchNotes,
    
    -- ==========================================
    -- أمر التصنيع
    -- ==========================================
    pb.ProductionOrderID,
    po.OrderNumber,
    po.OrderStatus,
    
    -- ==========================================
    -- المنتج
    -- ==========================================
    pb.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    
    -- ==========================================
    -- الوحدة
    -- ==========================================
    pb.UnitID,
    u.UnitNameAr AS UnitName,
    
    -- ==========================================
    -- المخزن
    -- ==========================================
    pb.TargetWarehouseID,
    w.WarehouseNameAr AS TargetWarehouseName,
    
    -- ==========================================
    -- الموظفين
    -- ==========================================
    pb.ProducedBy,
    ep.FullNameAr AS ProducedByName,
    pb.SupervisorID,
    es.FullNameAr AS SupervisorName,
    
    -- ==========================================
    -- الإنشاء والتعديل
    -- ==========================================
    pb.CreatedBy,
    uc.FullName AS CreatedByName,
    pb.CreatedDate,
    pb.ModifiedBy,
    um.FullName AS ModifiedByName,
    pb.ModifiedDate,
    
    -- ==========================================
    -- مراحل الدفعة
    -- ==========================================
    pbs.BatchStageID,
    pbs.StageID,
    ps.StageNameAr AS StageName,
    ps.StageCode,
    pbs.StageOrder,
    pbs.StartTime AS StageStartTime,
    pbs.EndTime AS StageEndTime,
    DATEDIFF(MINUTE, pbs.StartTime, pbs.EndTime) AS StageDurationMinutes,
    pbs.Temperature,
    pbs.Speed,
    pbs.Humidity,
    pbs.OperatorID,
    eo.FullNameAr AS OperatorName,
    pbs.StageStatus,
    CASE pbs.StageStatus
        WHEN 1 THEN N'في الانتظار'
        WHEN 2 THEN N'قيد التنفيذ'
        WHEN 3 THEN N'مكتمل'
        WHEN 4 THEN N'مشكلة'
    END AS StageStatusName,
    pbs.QualityCheck AS StageQualityCheck,
    pbs.QualityResult AS StageQualityResult,
    pbs.Notes AS StageNotes,
    
    -- ==========================================
    -- إحصائيات الدفعة
    -- ==========================================
    (SELECT COUNT(*) FROM ProductionBatchStages pbs2
     WHERE pbs2.BatchID = pb.BatchID) AS TotalStages,
    (SELECT COUNT(*) FROM ProductionBatchStages pbs2
     WHERE pbs2.BatchID = pb.BatchID 
     AND pbs2.StageStatus = 3) AS CompletedStages,
    CASE WHEN pb.ProducedQty > 0
         THEN CAST(pb.WasteQty * 100.0 / pb.ProducedQty AS DECIMAL(5,2))
         ELSE 0
    END AS WastePercent

FROM ProductionBatches pb
INNER JOIN ProductionOrders po ON pb.ProductionOrderID = po.ProductionOrderID
INNER JOIN Items i ON pb.ProductItemID = i.ItemID
INNER JOIN Units u ON pb.UnitID = u.UnitID
LEFT JOIN Warehouses w ON pb.TargetWarehouseID = w.WarehouseID
LEFT JOIN Employees ep ON pb.ProducedBy = ep.EmployeeID
LEFT JOIN Employees es ON pb.SupervisorID = es.EmployeeID
LEFT JOIN Employees qce ON pb.QCInspectedBy = qce.EmployeeID
LEFT JOIN SystemUsers uc ON pb.CreatedBy = uc.UserID
LEFT JOIN SystemUsers um ON pb.ModifiedBy = um.UserID
LEFT JOIN ProductionBatchStages pbs ON pb.BatchID = pbs.BatchID
LEFT JOIN ProductionStages ps ON pbs.StageID = ps.StageID
LEFT JOIN Employees eo ON pbs.OperatorID = eo.EmployeeID;

GO

-- ============================================================
-- VIEW: dbo.vw_ProductionBatchSummary
-- ============================================================


-- View: ملخص الباتشات
CREATE   VIEW [dbo].[vw_ProductionBatchSummary]
AS
SELECT 
    pb.BatchID,
    pb.BatchNumber,
    pb.BatchDate,
    pb.ExpiryDate,
    po.OrderNumber AS ProductionOrderNumber,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    pb.PlannedQty,
    pb.ProducedQty,
    pb.GoodQty,
    pb.RejectedQty,
    pb.WasteQty,
    pb.SampleQty,
    u.UnitNameAr AS UnitName,
    CASE pb.BatchStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'قيد التصنيع'
        WHEN 3 THEN N'اكتمل التصنيع'
        WHEN 4 THEN N'في فحص الجودة'
        WHEN 5 THEN N'مقبول ✅'
        WHEN 6 THEN N'مرفوض ❌'
    END AS StatusName,
    CASE pb.QCResult
        WHEN 1 THEN N'ناجح ✅'
        WHEN 2 THEN N'فاشل ❌'
        WHEN 3 THEN N'ناجح بتحفظ ⚠️'
    END AS QCResultName,
    pb.MaterialCost,
    pb.LaborCost,
    pb.OverheadCost,
    pb.TotalCost,
    pb.UnitCost,
    CASE WHEN pb.ProducedQty > 0 
         THEN CAST(pb.WasteQty * 100.0 / pb.ProducedQty AS DECIMAL(5,2))
         ELSE 0 
    END AS WastePercent,
    w.WarehouseNameAr AS TargetWarehouse
FROM ProductionBatches pb
INNER JOIN ProductionOrders po ON pb.ProductionOrderID = po.ProductionOrderID
INNER JOIN Items i ON pb.ProductItemID = i.ItemID
INNER JOIN Units u ON pb.UnitID = u.UnitID
LEFT JOIN Warehouses w ON pb.TargetWarehouseID = w.WarehouseID;

GO

-- ============================================================
-- VIEW: dbo.vw_ProductionOrderFullReport
-- ============================================================

-- =============================================
-- View: vw_ProductionOrderFullReport
-- تقرير أمر التصنيع الشامل
-- =============================================
CREATE VIEW vw_ProductionOrderFullReport
AS
SELECT 
    -- ==========================================
    -- البيانات الرئيسية لأمر التصنيع
    -- ==========================================
    po.ProductionOrderID,
    po.OrderNumber,
    po.OrderDate,
    po.PlannedQty,
    po.ActualQty,
    po.WasteQty,
    po.NumberOfBatches,
    po.PlannedStartDate,
    po.PlannedEndDate,
    po.ActualStartDate,
    po.ActualEndDate,
    po.OrderStatus,
    CASE po.OrderStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'جاري التجهيز'
        WHEN 4 THEN N'قيد التصنيع'
        WHEN 5 THEN N'فحص الجودة'
        WHEN 6 THEN N'مكتمل'
        WHEN 7 THEN N'ملغي'
    END AS OrderStatusName,
    po.OrderPriority,
    CASE po.OrderPriority
        WHEN 1 THEN N'عاجل'
        WHEN 2 THEN N'عادي'
        WHEN 3 THEN N'غير عاجل'
    END AS OrderPriorityName,
    po.Notes AS OrderNotes,
    
    -- ==========================================
    -- الوصفة والمنتج
    -- ==========================================
    po.BOMID,
    b.BOMCode,
    b.BOMName,
    po.ProductItemID,
    pi.ItemCode AS ProductCode,
    pi.ItemNameAr AS ProductName,
    
    -- ==========================================
    -- الوحدة
    -- ==========================================
    po.PlannedUnitID,
    pu.UnitNameAr AS PlannedUnitName,
    
    -- ==========================================
    -- المخازن
    -- ==========================================
    po.SourceWarehouseID,
    sw.WarehouseNameAr AS SourceWarehouseName,
    po.TargetWarehouseID,
    tw.WarehouseNameAr AS TargetWarehouseName,
    
    -- ==========================================
    -- الموظفين
    -- ==========================================
    po.AssignedTo,
    ea.FullNameAr AS AssignedToName,
    po.SupervisorID,
    es.FullNameAr AS SupervisorName,
    
    -- ==========================================
    -- التكاليف التقديرية
    -- ==========================================
    po.EstimatedMaterialCost,
    po.EstimatedLaborCost,
    po.EstimatedOverheadCost,
    po.EstimatedTotalCost,
    
    -- ==========================================
    -- التكاليف الفعلية
    -- ==========================================
    po.ActualMaterialCost,
    po.ActualLaborCost,
    po.ActualOverheadCost,
    po.ActualTotalCost,
    
    -- ==========================================
    -- الاعتماد
    -- ==========================================
    po.ApprovedBy,
    eap.FullNameAr AS ApprovedByName,
    po.ApprovedDate,
    
    -- ==========================================
    -- الإنشاء والتعديل
    -- ==========================================
    po.CreatedBy,
    uc.FullName AS CreatedByName,
    po.CreatedDate,
    po.ModifiedBy,
    um.FullName AS ModifiedByName,
    po.ModifiedDate,
    
    -- ==========================================
    -- تفاصيل المواد
    -- ==========================================
    pom.POMaterialID,
    pom.LineNumber AS MaterialLineNumber,
    pom.MaterialType,
    CASE pom.MaterialType
        WHEN 1 THEN N'خام'
        WHEN 2 THEN N'تعبئة'
    END AS MaterialTypeName,
    pom.ItemID AS MaterialItemID,
    mi.ItemCode AS MaterialItemCode,
    mi.ItemNameAr AS MaterialItemName,
    pom.RequiredQty AS MaterialRequiredQty,
    pom.IssuedQty AS MaterialIssuedQty,
    pom.UnitID AS MaterialUnitID,
    mu.UnitNameAr AS MaterialUnitName,
    pom.UnitCost AS MaterialUnitCost,
    (pom.RequiredQty * ISNULL(pom.UnitCost, 0)) AS MaterialLineCost,
    pom.AvailableStock AS MaterialAvailableStock

FROM ProductionOrders po
-- الوصفة
INNER JOIN BillOfMaterials b ON po.BOMID = b.BOMID
-- المنتج النهائي
INNER JOIN Items pi ON po.ProductItemID = pi.ItemID
-- وحدة الإنتاج
INNER JOIN Units pu ON po.PlannedUnitID = pu.UnitID
-- المخازن
LEFT JOIN Warehouses sw ON po.SourceWarehouseID = sw.WarehouseID
LEFT JOIN Warehouses tw ON po.TargetWarehouseID = tw.WarehouseID
-- الموظفين
LEFT JOIN Employees ea ON po.AssignedTo = ea.EmployeeID
LEFT JOIN Employees es ON po.SupervisorID = es.EmployeeID
LEFT JOIN Employees eap ON po.ApprovedBy = eap.EmployeeID
-- المستخدمين
LEFT JOIN SystemUsers uc ON po.CreatedBy = uc.UserID
LEFT JOIN SystemUsers um ON po.ModifiedBy = um.UserID
-- تفاصيل المواد
LEFT JOIN ProductionOrderMaterials pom ON po.ProductionOrderID = pom.ProductionOrderID
LEFT JOIN Items mi ON pom.ItemID = mi.ItemID
LEFT JOIN Units mu ON pom.UnitID = mu.UnitID;

GO

-- ============================================================
-- VIEW: dbo.vw_ProductionOrderSummary
-- ============================================================


-- View: ملخص أوامر التصنيع
CREATE   VIEW [dbo].[vw_ProductionOrderSummary]
AS
SELECT 
    po.ProductionOrderID,
    po.OrderNumber,
    po.OrderDate,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    b.BOMName,
    po.PlannedQty,
    u.UnitNameAr AS UnitName,
    po.ActualQty,
    po.WasteQty,
    po.NumberOfBatches,
    CASE po.OrderStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'جاري التجهيز'
        WHEN 4 THEN N'قيد التصنيع'
        WHEN 5 THEN N'فحص الجودة'
        WHEN 6 THEN N'مكتمل'
        WHEN 7 THEN N'ملغي'
    END AS StatusName,
    CASE po.OrderPriority
        WHEN 1 THEN N'عاجل 🔴'
        WHEN 2 THEN N'عادي 🟡'
        WHEN 3 THEN N'غير عاجل 🟢'
    END AS PriorityName,
    po.PlannedStartDate,
    po.PlannedEndDate,
    po.ActualStartDate,
    po.ActualEndDate,
    po.EstimatedTotalCost,
    po.ActualTotalCost,
    sw.WarehouseNameAr AS SourceWarehouse,
    tw.WarehouseNameAr AS TargetWarehouse,
    emp.FullNameAr AS AssignedToName,
    -- عدد الباتشات
    (SELECT COUNT(*) FROM ProductionBatches pb WHERE pb.ProductionOrderID = po.ProductionOrderID) AS BatchCount,
    (SELECT COUNT(*) FROM ProductionBatches pb WHERE pb.ProductionOrderID = po.ProductionOrderID AND pb.BatchStatus = 5) AS CompletedBatchCount
FROM ProductionOrders po
INNER JOIN Items i ON po.ProductItemID = i.ItemID
INNER JOIN BillOfMaterials b ON po.BOMID = b.BOMID
INNER JOIN Units u ON po.PlannedUnitID = u.UnitID
LEFT JOIN Warehouses sw ON po.SourceWarehouseID = sw.WarehouseID
LEFT JOIN Warehouses tw ON po.TargetWarehouseID = tw.WarehouseID
LEFT JOIN Employees emp ON po.AssignedTo = emp.EmployeeID;

GO

-- ============================================================
-- VIEW: dbo.vw_PRRemainingQty
-- ============================================================


-- =============================================
-- 5) View: الكميات المتبقية من طلبات الشراء
-- (مهم جداً لتحميل سطور PR في PO)
-- =============================================
CREATE   VIEW vw_PRRemainingQty
AS
SELECT
    prd.RequestDetailID,
    prd.RequestID,
    pr.RequestNumber,
    prd.LineNumber,
    prd.ItemID,
    i.ItemCode,
    i.ItemNameAr,
    i.ItemNameEn,
    prd.UnitID,
    u.UnitNameAr AS UnitName,
    prd.RequestedQty,
    prd.ApprovedQty,
    prd.EstimatedPrice,
    prd.CurrentStock,
    prd.Purpose,

    -- الكمية التي دخلت أوامر شراء غير ملغية
    ISNULL(ordered.TotalOrdered, 0) AS TotalOrderedQty,

    -- الكمية المتبقية للطلب
    ISNULL(prd.ApprovedQty, prd.RequestedQty) 
        - ISNULL(ordered.TotalOrdered, 0) AS RemainingToOrder,

    -- هل تم تغطية الطلب بالكامل؟
    CASE 
        WHEN ISNULL(ordered.TotalOrdered, 0) >= 
             ISNULL(prd.ApprovedQty, prd.RequestedQty)
        THEN 1 ELSE 0
    END AS IsFullyCovered

FROM PurchaseRequestDetails prd
INNER JOIN PurchaseRequests pr ON prd.RequestID = pr.RequestID
INNER JOIN Items i ON prd.ItemID = i.ItemID
INNER JOIN Units u ON prd.UnitID = u.UnitID
LEFT JOIN (
    SELECT 
        pod.RequestDetailID,
        SUM(pod.OrderedQty) AS TotalOrdered
    FROM PurchaseOrderDetails pod
    INNER JOIN PurchaseOrders po ON pod.PurchaseOrderID = po.PurchaseOrderID
    WHERE po.POStatus NOT IN (4, 9) -- exclude rejected & cancelled
      AND pod.LineStatus != 4       -- exclude cancelled lines
      AND pod.RequestDetailID IS NOT NULL
    GROUP BY pod.RequestDetailID
) ordered ON prd.RequestDetailID = ordered.RequestDetailID

WHERE pr.RequestStatus = 3; -- معتمد فقط

GO

-- ============================================================
-- VIEW: dbo.vw_PurchaseInvoiceSummary
-- ============================================================


-- =============================================
-- 3) View: ملخص فواتير المشتريات
-- =============================================
CREATE   VIEW vw_PurchaseInvoiceSummary
AS
SELECT
    pi.InvoiceID,
    pi.InvoiceNumber,
    pi.InvoiceDate,
    ISNULL(pi.InvoiceSource, 1) AS InvoiceSource,
    CASE ISNULL(pi.InvoiceSource, 1)
        WHEN 1 THEN N'من إذن استلام'
        WHEN 2 THEN N'من أمر شراء'
        WHEN 3 THEN N'فاتورة مباشرة'
    END AS InvoiceSourceName,

    -- المورد
    s.SupplierID,
    s.SupplierCode,
    s.SupplierNameAr,

    -- المراجع
    pi.PurchaseOrderID,
    po.PONumber AS LinkedPONumber,
    pi.GRNID,
    grn.GRNNumber AS LinkedGRNNumber,
    pi.SupplierInvoiceNo,
    pi.SupplierInvoiceDate,

    -- الحالة
    pi.InvoiceStatus,
    CASE pi.InvoiceStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'مقدمة للاعتماد'
        WHEN 3 THEN N'معتمدة'
        WHEN 4 THEN N'مدفوعة جزئياً'
        WHEN 5 THEN N'مدفوعة بالكامل'
        WHEN 6 THEN N'متأخرة'
        WHEN 7 THEN N'ملغية'
    END AS InvoiceStatusName,

    pi.PaymentStatus,
    CASE pi.PaymentStatus
        WHEN 1 THEN N'غير مدفوعة'
        WHEN 2 THEN N'مدفوعة جزئياً'
        WHEN 3 THEN N'مدفوعة بالكامل'
    END AS PaymentStatusName,

    -- المبالغ
    pi.SubTotal,
    pi.DiscountAmount,
    pi.TaxAmount,
    pi.ShippingCost,
    pi.OtherCosts,
    pi.TotalAmount,
    pi.PaidAmount,
    (pi.TotalAmount - ISNULL(pi.PaidAmount, 0)) AS RemainingAmount,

    -- التواريخ
    pi.DueDate,
    pt.TermNameAr AS PaymentTermName,
    cur.CurrencyCode,
    cur.Symbol AS CurrencySymbol,
    pi.ExchangeRate,

    -- متأخر؟
    CASE
        WHEN pi.DueDate < CAST(GETDATE() AS DATE)
         AND pi.PaymentStatus != 3
         AND pi.InvoiceStatus NOT IN (1, 7)
        THEN 1 ELSE 0
    END AS IsOverdue,
    DATEDIFF(DAY, pi.DueDate, GETDATE()) AS DaysOverdue,

    -- المستخدمون
    creator.FullNameAr AS CreatedByName,
    approver.FullNameAr AS ApprovedByName,
    pi.RejectionReason,

    -- الإجماليات
    COUNT(pid.InvoiceDetailID) AS ItemCount,

    pi.Notes,
    pi.CreatedDate,
    pi.ModifiedDate

FROM PurchaseInvoices pi
INNER JOIN Suppliers s ON pi.SupplierID = s.SupplierID
LEFT JOIN PurchaseOrders po ON pi.PurchaseOrderID = po.PurchaseOrderID
LEFT JOIN GoodsReceiptNotes grn ON pi.GRNID = grn.GRNID
LEFT JOIN PaymentTerms pt ON pi.PaymentTermID = pt.PaymentTermID
LEFT JOIN Currencies cur ON pi.CurrencyID = cur.CurrencyID
LEFT JOIN Employees creator ON pi.CreatedBy = creator.EmployeeID
LEFT JOIN Employees approver ON pi.ApprovedBy = approver.EmployeeID
LEFT JOIN PurchaseInvoiceDetails pid ON pi.InvoiceID = pid.InvoiceID
GROUP BY
    pi.InvoiceID, pi.InvoiceNumber, pi.InvoiceDate, pi.InvoiceSource,
    s.SupplierID, s.SupplierCode, s.SupplierNameAr,
    pi.PurchaseOrderID, po.PONumber,
    pi.GRNID, grn.GRNNumber,
    pi.SupplierInvoiceNo, pi.SupplierInvoiceDate,
    pi.InvoiceStatus, pi.PaymentStatus,
    pi.SubTotal, pi.DiscountAmount, pi.TaxAmount,
    pi.ShippingCost, pi.OtherCosts, pi.TotalAmount, pi.PaidAmount,
    pi.DueDate, pt.TermNameAr, cur.CurrencyCode, cur.Symbol, pi.ExchangeRate,
    creator.FullNameAr, approver.FullNameAr, pi.RejectionReason,
    pi.Notes, pi.CreatedDate, pi.ModifiedDate;

GO

-- ============================================================
-- VIEW: dbo.vw_PurchaseOrderSummary
-- ============================================================


-- =============================================
-- 4) View: ملخص أوامر الشراء المطور
-- =============================================
CREATE   VIEW vw_PurchaseOrderSummary
AS
SELECT 
    po.PurchaseOrderID,
    po.PONumber,
    po.PODate,
    po.POSource,
    CASE po.POSource
        WHEN 1 THEN N'يدوي'
        WHEN 2 THEN N'من طلب شراء'
    END AS POSourceName,
    
    -- المورد
    s.SupplierID,
    s.SupplierCode,
    s.SupplierNameAr,
    s.Phone1 AS SupplierPhone,
    s.CurrentBalance AS SupplierCurrentBalance,
    s.CreditLimit AS SupplierCreditLimit,
    
    -- طلب الشراء المرتبط
    po.RequestID,
    pr.RequestNumber AS LinkedPRNumber,
    
    -- الحالة
    po.POStatus,
    CASE po.POStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'مقدم للاعتماد'
        WHEN 3 THEN N'معتمد'
        WHEN 4 THEN N'مرفوض'
        WHEN 5 THEN N'مرسل للمورد'
        WHEN 6 THEN N'استلام جزئي'
        WHEN 7 THEN N'استلام كامل'
        WHEN 8 THEN N'مغلق'
        WHEN 9 THEN N'ملغي'
    END AS POStatusName,
    
    -- المبالغ
    po.SubTotal,
    po.DiscountAmount,
    po.TaxAmount,
    po.ShippingCost,
    po.OtherCosts,
    po.TotalAmount,
    
    -- التواريخ المهمة
    po.ExpectedDeliveryDate,
    po.SubmittedDate,
    po.ApprovedDate,
    po.RejectedDate,
    po.SentDate,
    
    -- المستخدمون
    creator.FullNameAr AS CreatedByName,
    approver.FullNameAr AS ApprovedByName,
    rejecter.FullNameAr AS RejectedByName,
    sender.FullNameAr AS SentByName,
    
    -- سبب الرفض
    po.RejectionReason,
    
    -- المخزن
    w.WarehouseNameAr,
    
    -- شروط الدفع
    pt.TermNameAr AS PaymentTermName,
    
    -- العملة
    cur.CurrencyCode,
    cur.Symbol AS CurrencySymbol,
    po.ExchangeRate,
    
    -- إجماليات الأصناف
    COUNT(pod.PODetailID) AS ItemCount,
    SUM(pod.OrderedQty) AS TotalOrderedQty,
    SUM(pod.ReceivedQty) AS TotalReceivedQty,
    SUM(pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) AS TotalRemainingQty,
    
    -- متأخر؟
    CASE 
        WHEN po.ExpectedDeliveryDate < CAST(GETDATE() AS DATE)
         AND po.POStatus IN (3, 5, 6)
        THEN 1 ELSE 0
    END AS IsOverdue,
    DATEDIFF(DAY, po.ExpectedDeliveryDate, GETDATE()) AS DaysOverdue,
    
    po.Notes,
    po.CreatedDate,
    po.ModifiedDate

FROM PurchaseOrders po
INNER JOIN Suppliers s ON po.SupplierID = s.SupplierID
LEFT JOIN PurchaseRequests pr ON po.RequestID = pr.RequestID
LEFT JOIN Warehouses w ON po.WarehouseID = w.WarehouseID
LEFT JOIN PaymentTerms pt ON po.PaymentTermID = pt.PaymentTermID
LEFT JOIN Currencies cur ON po.CurrencyID = cur.CurrencyID
LEFT JOIN Employees creator ON po.CreatedBy = creator.EmployeeID
LEFT JOIN Employees approver ON po.ApprovedBy = approver.EmployeeID
LEFT JOIN Employees rejecter ON po.RejectedBy = rejecter.EmployeeID
LEFT JOIN Employees sender ON po.SentBy = sender.EmployeeID
LEFT JOIN PurchaseOrderDetails pod ON po.PurchaseOrderID = pod.PurchaseOrderID
    AND pod.LineStatus != 4 -- exclude cancelled lines
GROUP BY
    po.PurchaseOrderID, po.PONumber, po.PODate, po.POSource,
    s.SupplierID, s.SupplierCode, s.SupplierNameAr, 
    s.Phone1, s.CurrentBalance, s.CreditLimit,
    po.RequestID, pr.RequestNumber,
    po.POStatus,
    po.SubTotal, po.DiscountAmount, po.TaxAmount,
    po.ShippingCost, po.OtherCosts, po.TotalAmount,
    po.ExpectedDeliveryDate, po.SubmittedDate, 
    po.ApprovedDate, po.RejectedDate, po.SentDate,
    creator.FullNameAr, approver.FullNameAr,
    rejecter.FullNameAr, sender.FullNameAr,
    po.RejectionReason,
    w.WarehouseNameAr,
    pt.TermNameAr,
    cur.CurrencyCode, cur.Symbol,
    po.ExchangeRate,
    po.Notes, po.CreatedDate, po.ModifiedDate;

GO

-- ============================================================
-- VIEW: dbo.vw_PurchaseRequestSummary
-- ============================================================
CREATE   VIEW vw_PurchaseRequestSummary
AS
SELECT 
    pr.RequestID,
    pr.RequestNumber,
    pr.RequestDate,
    pr.RequiredDate,
    pr.RequestedBy,
    e.FullNameAr AS RequestedByName,
    pr.DepartmentID,
    d.DepartmentNameAr,
    pr.RequestStatus,
    CASE pr.RequestStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'مقدم للاعتماد'
        WHEN 3 THEN N'معتمد'
        WHEN 4 THEN N'مرفوض'
        WHEN 5 THEN N'تم التنفيذ'
    END AS RequestStatusName,
    pr.Priority,
    CASE pr.Priority
        WHEN 1 THEN N'عاجل'
        WHEN 2 THEN N'عادي'
        WHEN 3 THEN N'غير عاجل'
    END AS PriorityName,
    pr.Notes,
    pr.ApprovedBy,
    appr.FullNameAr AS ApprovedByName,
    pr.ApprovedDate,
    pr.RejectionReason,
    COUNT(prd.RequestDetailID) AS ItemCount,
    SUM(ISNULL(prd.RequestedQty,0)) AS TotalRequestedQty,
    SUM(ISNULL(prd.ApprovedQty,0)) AS TotalApprovedQty
FROM PurchaseRequests pr
LEFT JOIN Employees e ON pr.RequestedBy = e.EmployeeID
LEFT JOIN Departments d ON pr.DepartmentID = d.DepartmentID
LEFT JOIN Employees appr ON pr.ApprovedBy = appr.EmployeeID
LEFT JOIN PurchaseRequestDetails prd ON pr.RequestID = prd.RequestID
GROUP BY 
    pr.RequestID,
    pr.RequestNumber,
    pr.RequestDate,
    pr.RequiredDate,
    pr.RequestedBy,
    e.FullNameAr,
    pr.DepartmentID,
    d.DepartmentNameAr,
    pr.RequestStatus,
    pr.Priority,
    pr.Notes,
    pr.ApprovedBy,
    appr.FullNameAr,
    pr.ApprovedDate,
    pr.RejectionReason;

GO

-- ============================================================
-- VIEW: dbo.vw_PurchaseReturnSummary
-- ============================================================


-- =============================================
-- 2) vw_PurchaseReturnSummary
-- =============================================
CREATE   VIEW dbo.vw_PurchaseReturnSummary
AS
SELECT
    pr.ReturnID,
    pr.ReturnNumber,
    pr.ReturnDate,
    ISNULL(pr.ReturnSource, 1) AS ReturnSource,
    CASE ISNULL(pr.ReturnSource, 1)
        WHEN 1 THEN N'من فاتورة'
        WHEN 2 THEN N'مرتجع مباشر'
    END AS ReturnSourceName,

    s.SupplierID,
    s.SupplierCode,
    s.SupplierNameAr,

    pr.InvoiceID,
    pi.InvoiceNumber AS LinkedInvoiceNumber,

    pr.WarehouseID,
    w.WarehouseNameAr,

    pr.ReturnStatus,
    CASE pr.ReturnStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'مقدم للاعتماد'
        WHEN 3 THEN N'معتمد'
        WHEN 4 THEN N'مرفوض'
        WHEN 5 THEN N'ملغي'
    END AS ReturnStatusName,

    pr.ReturnType,
    CASE pr.ReturnType
        WHEN 1 THEN N'معيب'
        WHEN 2 THEN N'غير مطابق'
        WHEN 3 THEN N'زيادة'
    END AS ReturnTypeName,

    pr.ReturnReason,
    pr.SubTotal,
    pr.TaxAmount,
    pr.TotalAmount,

    creator.FullNameAr AS CreatedByName,
    approver.FullNameAr AS ApprovedByName,
    pr.RejectionReason,

    COUNT(prd.ReturnDetailID) AS ItemCount,

    pr.Notes,
    pr.CreatedDate,
    pr.ModifiedDate

FROM dbo.PurchaseReturns pr
INNER JOIN dbo.Suppliers s ON pr.SupplierID = s.SupplierID
LEFT JOIN dbo.PurchaseInvoices pi ON pr.InvoiceID = pi.InvoiceID
LEFT JOIN dbo.Warehouses w ON pr.WarehouseID = w.WarehouseID
LEFT JOIN dbo.Employees creator ON pr.CreatedBy = creator.EmployeeID
LEFT JOIN dbo.Employees approver ON pr.ApprovedBy = approver.EmployeeID
LEFT JOIN dbo.PurchaseReturnDetails prd ON pr.ReturnID = prd.ReturnID
GROUP BY
    pr.ReturnID, pr.ReturnNumber, pr.ReturnDate, pr.ReturnSource,
    s.SupplierID, s.SupplierCode, s.SupplierNameAr,
    pr.InvoiceID, pi.InvoiceNumber,
    pr.WarehouseID, w.WarehouseNameAr,
    pr.ReturnStatus, pr.ReturnType, pr.ReturnReason,
    pr.SubTotal, pr.TaxAmount, pr.TotalAmount,
    creator.FullNameAr, approver.FullNameAr, pr.RejectionReason,
    pr.Notes, pr.CreatedDate, pr.ModifiedDate;

GO

-- ============================================================
-- VIEW: dbo.vw_SalesInvoiceSummary
-- ============================================================
CREATE VIEW dbo.vw_SalesInvoiceSummary AS
SELECT 
    si.InvoiceID, si.InvoiceNumber, si.InvoiceDate, si.InvoiceStatus, 
    ISNULL(si.InvoiceSource,3) AS InvoiceSource,
    CASE ISNULL(si.InvoiceSource,3) WHEN 1 THEN N'من إذن تسليم' WHEN 2 THEN N'من أمر بيع' ELSE N'فاتورة مباشرة' END AS InvoiceSourceName,
    ISNULL(si.PaymentStatus,1) AS PaymentStatus,
    CASE ISNULL(si.PaymentStatus,1) WHEN 1 THEN N'غير مدفوعة' WHEN 2 THEN N'مدفوعة جزئياً' WHEN 3 THEN N'مدفوعة بالكامل' ELSE N'غير محدد' END AS PaymentStatusName,
    si.CustomerID, c.CustomerCode, c.CustomerNameAr,
    si.SalesOrderID, NULL AS LinkedSONumber,
    si.DeliveryID, NULL AS LinkedDeliveryNumber,
    ISNULL(si.IsTaxable,1) AS IsTaxable, ISNULL(si.TaxRate,14) AS TaxRate,
    ISNULL(si.SubTotal,0) AS SubTotal, ISNULL(si.DiscountAmount,0) AS DiscountAmount, ISNULL(si.TaxAmount,0) AS TaxAmount, ISNULL(si.TotalAmount,0) AS TotalAmount, ISNULL(si.PaidAmount,0) AS PaidAmount, ISNULL(si.TotalAmount,0)-ISNULL(si.PaidAmount,0) AS RemainingAmount,
    si.DueDate, 0 AS IsOverdue, 0 AS DaysOverdue, NULL AS PaymentTermName, NULL AS CurrencyCode,
    ISNULL((SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.InvoiceID=si.InvoiceID),0) AS ItemCount,
    si.Notes AS RejectionReason, si.CreatedDate
FROM dbo.SalesInvoices si
INNER JOIN dbo.Customers c ON si.CustomerID=c.CustomerID;

GO

-- ============================================================
-- VIEW: dbo.vw_StockBalances
-- ============================================================


-- =============================================
-- 1) View: أرصدة المخزون التفصيلية
-- =============================================
CREATE   VIEW dbo.vw_StockBalances
AS
SELECT
    ib.BalanceID,
    ib.ItemID,
    i.ItemCode,
    i.ItemNameAr,
    i.ItemNameEn,
    i.ItemType,
    CASE i.ItemType
        WHEN 1 THEN N'خام'
        WHEN 2 THEN N'تعبئة'
        WHEN 3 THEN N'نصف مصنع'
        WHEN 4 THEN N'تام'
        WHEN 5 THEN N'تشغيل'
    END AS ItemTypeName,
    cat.CategoryNameAr,
    cat.CategoryCode,

    ib.WarehouseID,
    w.WarehouseCode,
    w.WarehouseNameAr,
    w.WarehouseType,

    u.UnitNameAr AS PrimaryUnitName,

    ib.CurrentQty,
    ib.ReservedQty,
    ib.AvailableQty,
    ib.AverageCost,
    ib.TotalValue,

    i.MinStockLevel,
    i.MaxStockLevel,
    i.ReorderLevel,

    -- تنبيهات
    CASE
        WHEN ib.CurrentQty <= 0 THEN 3                    -- نفد
        WHEN ib.CurrentQty <= i.ReorderLevel THEN 2        -- تحت نقطة الطلب
        WHEN ib.CurrentQty <= i.MinStockLevel THEN 1       -- أقل من الحد الأدنى
        ELSE 0                                              -- طبيعي
    END AS StockAlert,
    CASE
        WHEN ib.CurrentQty <= 0 THEN N'نفد المخزون ❌'
        WHEN ib.CurrentQty <= i.ReorderLevel THEN N'تحت نقطة الطلب ⚠️'
        WHEN ib.CurrentQty <= i.MinStockLevel THEN N'أقل من الحد الأدنى 🔶'
        ELSE N'طبيعي ✅'
    END AS StockAlertName,

    ib.LastMovementDate,
    ib.LastCountDate,
    ib.ModifiedDate

FROM dbo.InventoryBalance ib
INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
INNER JOIN dbo.Units u ON i.PrimaryUnitID = u.UnitID
INNER JOIN dbo.Warehouses w ON ib.WarehouseID = w.WarehouseID
LEFT JOIN dbo.ItemCategories cat ON i.CategoryID = cat.CategoryID
WHERE i.IsActive = 1 AND w.IsActive = 1;

GO

-- ============================================================
-- VIEW: dbo.vw_StockCountSummary
-- ============================================================


-- =============================================
-- 5) View: ملخص أوامر الجرد
-- =============================================
CREATE   VIEW dbo.vw_StockCountSummary
AS
SELECT
    sc.CountID,
    sc.CountNumber,
    sc.CountDate,
    sc.CountStatus,
    CASE sc.CountStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'قيد الجرد'
        WHEN 3 THEN N'مكتمل'
        WHEN 4 THEN N'معتمد'
        WHEN 5 THEN N'ملغي'
    END AS CountStatusName,

    sc.CountType,
    CASE sc.CountType
        WHEN 1 THEN N'جرد شامل'
        WHEN 2 THEN N'جرد جزئي'
        WHEN 3 THEN N'جرد عشوائي'
    END AS CountTypeName,

    w.WarehouseID,
    w.WarehouseNameAr,

    cat.CategoryNameAr,

    counter.FullNameAr AS CountedByName,
    supervisor.FullNameAr AS SupervisorName,
    approver.FullNameAr AS ApprovedByName,
    sc.ApprovedDate,

    COUNT(scd.CountDetailID) AS ItemCount,
    SUM(CASE WHEN scd.CountedQty IS NOT NULL THEN 1 ELSE 0 END) AS CountedItems,
    SUM(ABS(ISNULL(scd.CountedQty, 0) - ISNULL(scd.SystemQty, 0))) AS TotalVarianceQty,
    SUM(ISNULL(scd.VarianceValue, 0)) AS TotalVarianceValue,
    SUM(CASE WHEN (ISNULL(scd.CountedQty, 0) - ISNULL(scd.SystemQty, 0)) > 0 THEN 1 ELSE 0 END) AS SurplusItems,
    SUM(CASE WHEN (ISNULL(scd.CountedQty, 0) - ISNULL(scd.SystemQty, 0)) < 0 THEN 1 ELSE 0 END) AS ShortageItems,

    sc.Notes,
    sc.CreatedDate

FROM dbo.StockCounts sc
INNER JOIN dbo.Warehouses w ON sc.WarehouseID = w.WarehouseID
LEFT JOIN dbo.ItemCategories cat ON sc.CategoryID = cat.CategoryID
LEFT JOIN dbo.Employees counter ON sc.CountedBy = counter.EmployeeID
LEFT JOIN dbo.Employees supervisor ON sc.SupervisorID = supervisor.EmployeeID
LEFT JOIN dbo.Employees approver ON sc.ApprovedBy = approver.EmployeeID
LEFT JOIN dbo.StockCountDetails scd ON sc.CountID = scd.CountID
GROUP BY
    sc.CountID, sc.CountNumber, sc.CountDate,
    sc.CountStatus, sc.CountType,
    w.WarehouseID, w.WarehouseNameAr,
    cat.CategoryNameAr,
    counter.FullNameAr, supervisor.FullNameAr,
    approver.FullNameAr, sc.ApprovedDate,
    sc.Notes, sc.CreatedDate;

GO

-- ============================================================
-- VIEW: dbo.vw_StockMovements
-- ============================================================


-- =============================================
-- 3) View: حركات المخزون التفصيلية
-- =============================================
CREATE   VIEW dbo.vw_StockMovements
AS
SELECT
    it.TransactionID,
    it.TransactionDate,
    it.TransactionType,
    CASE it.TransactionType
        WHEN 1 THEN N'استلام مشتريات'
        WHEN 2 THEN N'صرف للإنتاج'
        WHEN 3 THEN N'استلام من الإنتاج'
        WHEN 4 THEN N'صرف للبيع'
        WHEN 5 THEN N'مرتجع مشتريات'
        WHEN 6 THEN N'مرتجع مبيعات'
        WHEN 7 THEN N'تحويل صادر'
        WHEN 8 THEN N'تحويل وارد'
        WHEN 9 THEN N'تسوية زيادة'
        WHEN 10 THEN N'تسوية نقص'
        WHEN 11 THEN N'هالك/تالف'
    END AS TransactionTypeName,
    CASE
        WHEN it.Quantity > 0 THEN N'وارد'
        ELSE N'صادر'
    END AS Direction,

    it.ItemID,
    i.ItemCode,
    i.ItemNameAr,

    it.WarehouseID,
    w.WarehouseNameAr,

    it.Quantity,
    ABS(it.Quantity) AS AbsQuantity,
    u.UnitNameAr AS UnitName,
    it.UnitCost,
    it.TotalCost,

    it.BalanceBefore,
    it.BalanceAfter,
    it.CostBefore,
    it.CostAfter,

    it.BatchNumber,
    it.ExpiryDate,

    it.SourceDocType,
    it.SourceDocID,
    it.SourceDocNumber,

    it.Notes,
    creator.FullNameAr AS CreatedByName,
    it.CreatedDate

FROM dbo.InventoryTransactions it
INNER JOIN dbo.Items i ON it.ItemID = i.ItemID
INNER JOIN dbo.Units u ON it.UnitID = u.UnitID
INNER JOIN dbo.Warehouses w ON it.WarehouseID = w.WarehouseID
LEFT JOIN dbo.Employees creator ON it.CreatedBy = creator.EmployeeID;

GO

-- ============================================================
-- VIEW: dbo.vw_StockTransferSummary
-- ============================================================


-- =============================================
-- 4) View: ملخص تحويلات المخزون
-- =============================================
CREATE   VIEW dbo.vw_StockTransferSummary
AS
SELECT
    st.TransferID,
    st.TransferNumber,
    st.TransferDate,
    st.TransferStatus,
    CASE st.TransferStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'تم التحويل'
        WHEN 4 THEN N'ملغي'
    END AS TransferStatusName,

    fw.WarehouseID AS FromWarehouseID,
    fw.WarehouseNameAr AS FromWarehouseName,
    tw.WarehouseID AS ToWarehouseID,
    tw.WarehouseNameAr AS ToWarehouseName,

    st.TransferReason,

    req.FullNameAr AS RequestedByName,
    app.FullNameAr AS ApprovedByName,
    st.ApprovedDate,

    COUNT(std.TransferDetailID) AS ItemCount,
    SUM(std.Quantity) AS TotalQty,
    SUM(std.Quantity * ISNULL(std.UnitCost, 0)) AS TotalValue,

    st.Notes,
    st.CreatedDate,
    st.ModifiedDate

FROM dbo.StockTransfers st
INNER JOIN dbo.Warehouses fw ON st.FromWarehouseID = fw.WarehouseID
INNER JOIN dbo.Warehouses tw ON st.ToWarehouseID = tw.WarehouseID
LEFT JOIN dbo.Employees req ON st.RequestedBy = req.EmployeeID
LEFT JOIN dbo.Employees app ON st.ApprovedBy = app.EmployeeID
LEFT JOIN dbo.StockTransferDetails std ON st.TransferID = std.TransferID
GROUP BY
    st.TransferID, st.TransferNumber, st.TransferDate, st.TransferStatus,
    fw.WarehouseID, fw.WarehouseNameAr,
    tw.WarehouseID, tw.WarehouseNameAr,
    st.TransferReason,
    req.FullNameAr, app.FullNameAr, st.ApprovedDate,
    st.Notes, st.CreatedDate, st.ModifiedDate;

GO

-- ============================================================
-- VIEW: dbo.vw_SupplierBalances
-- ============================================================


-- View: أرصدة الموردين
CREATE   VIEW [dbo].[vw_SupplierBalances]
AS
SELECT 
    s.SupplierID,
    s.SupplierCode,
    s.SupplierNameAr,
    s.OpeningBalance,
    ISNULL(inv.TotalInvoices, 0) AS TotalInvoices,
    ISNULL(inv.TotalInvoiceAmount, 0) AS TotalInvoiceAmount,
    ISNULL(inv.TotalPaid, 0) AS TotalPaid,
    ISNULL(inv.TotalRemaining, 0) AS TotalRemaining,
    ISNULL(ret.TotalReturns, 0) AS TotalReturns,
    ISNULL(ret.TotalReturnAmount, 0) AS TotalReturnAmount,
    s.CreditLimit,
    s.IsActive
FROM Suppliers s
LEFT JOIN (
    SELECT SupplierID,
           COUNT(*) AS TotalInvoices,
           SUM(TotalAmount) AS TotalInvoiceAmount,
           SUM(PaidAmount) AS TotalPaid,
           SUM(TotalAmount - ISNULL(PaidAmount, 0)) AS TotalRemaining
    FROM PurchaseInvoices
    WHERE InvoiceStatus NOT IN (6) -- exclude cancelled
    GROUP BY SupplierID
) inv ON s.SupplierID = inv.SupplierID
LEFT JOIN (
    SELECT SupplierID,
           COUNT(*) AS TotalReturns,
           SUM(TotalAmount) AS TotalReturnAmount
    FROM PurchaseReturns
    WHERE ReturnStatus NOT IN (4) -- exclude cancelled
    GROUP BY SupplierID
) ret ON s.SupplierID = ret.SupplierID;

GO

-- ============================================================
-- VIEW: dbo.vw_SupplierDetails
-- ============================================================



-- View: بيانات الموردين الكاملة
CREATE    VIEW [dbo].[vw_SupplierDetails]
AS
SELECT 
    s.SupplierID,
    s.SupplierCode,
    s.SupplierNameAr,
    s.SupplierNameEn,
    CASE s.SupplierType 
        WHEN 1 THEN N'مورد خامات' WHEN 2 THEN N'مورد تعبئة' 
        WHEN 3 THEN N'مورد خدمات' WHEN 4 THEN N'مورد عام'
    END AS SupplierTypeName,
    s.Phone1,
    s.Mobile,
    s.Email,
    s.Address,
    s.City,
    pt.TermNameAr AS PaymentTermName,
    s.CreditLimit,
    s.CurrentBalance,
    Case s.Rating
	   When 1 THEN N'⭐ ضعيف' When 2 THEN N'⭐⭐ مقبول'
	   WHEN 3 THEN N'⭐⭐⭐ جيد' WHEN 4 THEN N'⭐⭐⭐⭐ جيد جداً' WHEN 5 THEN N'⭐⭐⭐⭐⭐ ممتاز'
	END AS RatingSupplier,  
    s.IsActive
FROM Suppliers s
LEFT JOIN PaymentTerms pt ON s.PaymentTermID = pt.PaymentTermID;

GO

-- ============================================================
-- VIEW: dbo.vw_SupplierFullDetails
-- ============================================================


-- =============================================
-- View: تفاصيل الموردين الشاملة
-- =============================================
CREATE   VIEW dbo.vw_SupplierFullDetails
AS
SELECT
    s.SupplierID,
    s.SupplierCode,
    s.SupplierNameAr,
    s.SupplierNameEn,
    s.SupplierType,
    CASE s.SupplierType
        WHEN 1 THEN N'مورد خامات'
        WHEN 2 THEN N'مورد تعبئة'
        WHEN 3 THEN N'مورد خدمات'
        WHEN 4 THEN N'مورد عام'
    END AS SupplierTypeName,
    s.TaxNumber,
    s.CommercialRegister,
    s.Address,
    s.City,
    s.Country,
    s.Phone1,
    s.Phone2,
    s.Mobile,
    s.Email,
    s.Website,
    s.PaymentTermID,
    pt.TermNameAr AS PaymentTermName,
    s.CurrencyID,
    c.CurrencyCode,
    s.CreditLimit,
    s.OpeningBalance,
    s.CurrentBalance,
    (s.CreditLimit - s.CurrentBalance) AS AvailableCredit,
    s.BankName,
    s.BankAccountNumber,
    s.IBAN,
    s.Rating,
    s.Notes,
    s.IsActive,

    -- إحصائيات
    (SELECT COUNT(*) FROM dbo.SupplierItems si WHERE si.SupplierID = s.SupplierID AND si.IsActive = 1) AS ItemCount,
    (SELECT COUNT(*) FROM dbo.SupplierContacts sc WHERE sc.SupplierID = s.SupplierID AND sc.IsActive = 1) AS ContactCount,
    (SELECT COUNT(*) FROM dbo.PurchaseOrders po WHERE po.SupplierID = s.SupplierID AND po.POStatus NOT IN (9)) AS TotalPOs,
    (SELECT COUNT(*) FROM dbo.PurchaseInvoices pi WHERE pi.SupplierID = s.SupplierID AND pi.InvoiceStatus NOT IN (7)) AS TotalInvoices,
    (SELECT ISNULL(SUM(pi.TotalAmount), 0) FROM dbo.PurchaseInvoices pi WHERE pi.SupplierID = s.SupplierID AND pi.InvoiceStatus NOT IN (7)) AS TotalPurchaseAmount,
    (SELECT COUNT(*) FROM dbo.PurchaseInvoices pi WHERE pi.SupplierID = s.SupplierID AND pi.PaymentStatus != 3 AND pi.InvoiceStatus NOT IN (1, 7)) AS UnpaidInvoices,

    s.CreatedDate,
    s.ModifiedDate

FROM dbo.Suppliers s
LEFT JOIN dbo.PaymentTerms pt ON s.PaymentTermID = pt.PaymentTermID
LEFT JOIN dbo.Currencies c ON s.CurrencyID = c.CurrencyID;

GO

-- ============================================================
-- VIEW: dbo.vw_SupplierStatement
-- ============================================================


-- =============================================
-- View: كشف حساب المورد
-- =============================================
CREATE   VIEW dbo.vw_SupplierStatement
AS
-- فواتير المشتريات
SELECT
    pi.SupplierID,
    pi.InvoiceDate AS TransDate,
    N'فاتورة مشتريات' AS TransType,
    pi.InvoiceNumber AS DocNumber,
    pi.TotalAmount AS DebitAmount,
    0 AS CreditAmount,
    pi.TotalAmount - ISNULL(pi.PaidAmount, 0) AS Balance,
    N'PI' AS DocType,
    pi.InvoiceID AS DocID
FROM dbo.PurchaseInvoices pi
WHERE pi.InvoiceStatus NOT IN (1, 7)

UNION ALL

-- مرتجعات المشتريات
SELECT
    pr.SupplierID,
    pr.ReturnDate,
    N'مرتجع مشتريات',
    pr.ReturnNumber,
    0,
    pr.TotalAmount,
    -pr.TotalAmount,
    N'PRET',
    pr.ReturnID
FROM dbo.PurchaseReturns pr
WHERE pr.ReturnStatus = 3;

GO

-- ============================================================
-- VIEW: dbo.vw_TreasurySummary
-- ============================================================
CREATE VIEW dbo.vw_TreasurySummary AS
SELECT 
    cb.CashBoxID,
    cb.CashBoxCode,
    cb.CashBoxNameAr,
    cb.CashBoxNameEn,
    ISNULL(cb.CashBoxType,1) AS CashBoxType,
    CASE ISNULL(cb.CashBoxType,1) WHEN 1 THEN N'رئيسية' WHEN 2 THEN N'فرعية' WHEN 3 THEN N'بنك' WHEN 4 THEN N'نقطة بيع' ELSE N'أخرى' END AS CashBoxTypeName,
    cb.AccountID,
    coa.AccountCode,
    coa.AccountNameAr AS AccountNameAr,
    ISNULL(cb.OpeningBalance,0) AS OpeningBalance,
    cb.CurrentBalance,
    ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE CashBoxID=cb.CashBoxID AND ReceiptStatus=2),0) AS TotalReceipts,
    ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE CashBoxID=cb.CashBoxID AND PaymentStatus=2),0) AS TotalPayments,
    ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE ToCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalIn,
    ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE FromCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalOut,
    ISNULL(cb.OpeningBalance,0) 
    + ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE CashBoxID=cb.CashBoxID AND ReceiptStatus=2),0)
    - ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE CashBoxID=cb.CashBoxID AND PaymentStatus=2),0)
    + ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE ToCashBoxID=cb.CashBoxID AND Status=2),0)
    - ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE FromCashBoxID=cb.CashBoxID AND Status=2),0) AS CurrentBalanceCalc,
    cb.CurrencyID,
    cur.CurrencyCode,
    CAST(1 AS BIT) AS IsActive,
    ISNULL(cb.IsDefault,0) AS IsDefault,
    cb.CreatedDate
FROM dbo.CashBoxes cb
LEFT JOIN dbo.ChartOfAccounts coa ON cb.AccountID=coa.AccountID
LEFT JOIN dbo.Currencies cur ON cb.CurrencyID=cur.CurrencyID;

GO

-- ============================================================
-- VIEW: dbo.vw_TrialBalance
-- ============================================================
CREATE VIEW dbo.vw_TrialBalance AS
SELECT 
    coa.AccountID,
    coa.AccountCode,
    coa.AccountNameAr,
    coa.AccountLevel,
    coa.AccountType,
    ISNULL(coa.OpeningBalance,0) AS OpeningBalance,
    ISNULL((SELECT SUM(jd.DebitAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND ISNULL(je.JournalStatus,1)!=3),0) AS TotalDebit,
    ISNULL((SELECT SUM(jd.CreditAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND ISNULL(je.JournalStatus,1)!=3),0) AS TotalCredit,
    ISNULL(coa.OpeningBalance,0) + ISNULL((SELECT SUM(jd.DebitAmount - jd.CreditAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND ISNULL(je.JournalStatus,1)!=3),0) AS CurrentBalance,
    ISNULL(coa.OpeningBalance,0) + ISNULL((SELECT SUM(jd.DebitAmount - jd.CreditAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND ISNULL(je.JournalStatus,1)!=3),0) AS Balance
FROM dbo.ChartOfAccounts coa
WHERE coa.IsActive=1;

GO

-- ============================================================
-- VIEW: dbo.vw_UserEffectivePermissions
-- ============================================================


-- =============================================
-- Views خاصة بالصلاحيات
-- =============================================

-- View: صلاحيات المستخدم الفعلية (دمج صلاحيات الدور + الخاصة)
CREATE   VIEW [dbo].[vw_UserEffectivePermissions]
AS
SELECT 
    u.UserID,
    u.Username,
    u.FullName,
    r.RoleNameAr,
    sm.ModuleCode,
    sm.ModuleNameAr,
    sm.ModuleType,
    -- الصلاحية الفعلية = صلاحية خاصة (لو موجودة) أو صلاحية الدور
    ISNULL(up.CanView,    rp.CanView)    AS CanView,
    ISNULL(up.CanAdd,     rp.CanAdd)     AS CanAdd,
    ISNULL(up.CanEdit,    rp.CanEdit)    AS CanEdit,
    ISNULL(up.CanDelete,  rp.CanDelete)  AS CanDelete,
    ISNULL(up.CanPrint,   rp.CanPrint)   AS CanPrint,
    ISNULL(up.CanExport,  rp.CanExport)  AS CanExport,
    ISNULL(up.CanApprove, rp.CanApprove) AS CanApprove,
    ISNULL(up.CanPost,    rp.CanPost)    AS CanPost,
    ISNULL(up.CanCancel,  rp.CanCancel)  AS CanCancel
FROM SystemUsers u
INNER JOIN UserRoles r ON u.RoleID = r.RoleID
CROSS JOIN SystemModules sm
LEFT JOIN RolePermissions rp ON r.RoleID = rp.RoleID AND sm.ModuleID = rp.ModuleID
LEFT JOIN UserPermissions up ON u.UserID = up.UserID AND sm.ModuleID = up.ModuleID
WHERE u.IsActive = 1;

GO

