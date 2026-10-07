using Dapper;
using YKCoatings.Models;
using System.Data;

namespace YKCoatings.Services
{
    public class PayrollService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly SequenceService _seq;
        private readonly NotificationService _notif;

        public PayrollService(
            IConfiguration configuration,
            AuditService audit,
            SequenceService seq,
            NotificationService notif) : base(configuration)
        {
            _audit = audit;
            _seq = seq;
            _notif = notif;
        }

        public async Task<PayrollPagedResult> GetPayrollsPagedAsync(PayrollFilterDto filter)
        {
            using var connection = CreateConnection();
            filter ??= new PayrollFilterDto();

            var where = @"
                WHERE (@PayrollStatus = 0 OR p.PayrollStatus = @PayrollStatus)
                  AND (@DateFrom IS NULL OR p.PayrollMonth >= @DateFrom)
                  AND (@DateTo IS NULL OR p.PayrollMonth <= @DateTo)";

            var countSql = @"SELECT COUNT(*) FROM dbo.Payroll p " + where;

            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
            {
                filter.PayrollStatus,
                filter.DateFrom,
                filter.DateTo
            });

            var dataSql = @"
                SELECT * FROM (
                    SELECT
                        p.PayrollID,
                        p.PayrollNumber,
                        p.PayrollMonth,
                        p.PeriodID,
                        ap.PeriodName,
                        p.PayrollTitle,
                        p.PayrollStatus,
                        CASE p.PayrollStatus
                            WHEN 1 THEN N'مسودة'
                            WHEN 2 THEN N'محسوب'
                            WHEN 3 THEN N'معتمد'
                            WHEN 4 THEN N'مدفوع'
                            WHEN 5 THEN N'ملغي'
                        END AS PayrollStatusName,
                        p.EmployeeCount,
                        p.TotalBasicSalary,
                        p.TotalAllowances,
                        p.TotalGrossEarnings,
                        p.TotalDeductions,
                        p.TotalNetSalary,
                        p.CreatedDate,
                        ISNULL(calc.FullNameAr, N'') AS CalculatedByName,
                        ROW_NUMBER() OVER (ORDER BY p.PayrollMonth DESC, p.PayrollID DESC) AS RowNum
                    FROM dbo.Payroll p
                    LEFT JOIN dbo.AccountingPeriods ap ON p.PeriodID = ap.PeriodID
                    LEFT JOIN dbo.Employees calc ON p.CalculatedBy = calc.EmployeeID
                    " + where + @"
                ) ranked
                WHERE RowNum BETWEEN @StartRow AND @EndRow
                ORDER BY RowNum;";

            var items = await connection.QueryAsync<PayrollRunListDto>(dataSql, new
            {
                filter.PayrollStatus,
                filter.DateFrom,
                filter.DateTo,
                StartRow = (filter.PageNumber - 1) * filter.PageSize + 1,
                EndRow = filter.PageNumber * filter.PageSize
            });

            return new PayrollPagedResult
            {
                Items = items.ToList(),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        public async Task<PayrollRunEditDto?> GetPayrollByIdAsync(int id)
        {
            using var connection = CreateConnection();

            return await connection.QueryFirstOrDefaultAsync<PayrollRunEditDto>(@"
                SELECT
                    p.PayrollID,
                    p.PayrollNumber,
                    p.PayrollMonth,
                    p.PeriodID,
                    ap.PeriodName,
                    p.PayrollTitle,
                    p.EmployeeCount,
                    p.TotalGrossEarnings,
                    p.TotalDeductions,
                    p.TotalNetSalary,
                    p.PayrollStatus
                FROM dbo.Payroll p
                LEFT JOIN dbo.AccountingPeriods ap ON p.PeriodID = ap.PeriodID
                WHERE p.PayrollID = @ID",
                new { ID = id });
        }

        public async Task<List<PayrollDetailDto>> GetPayrollDetailsAsync(int payrollId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    pd.PayrollDetailID,
                    pd.PayrollID,
                    pd.EmployeeID,
                    e.EmployeeCode,
                    e.FullNameAr AS EmployeeName,
                    d.DepartmentNameAr AS DepartmentName,
                    j.JobTitleNameAr AS JobTitleName,
                    pd.WorkingDays,
                    pd.PresentDays,
                    pd.AbsentDays,
                    pd.LeaveDays,
                    pd.OvertimeHours,
                    pd.BasicSalary,
                    pd.TransportAllowance,
                    pd.HousingAllowance,
                    pd.PhoneAllowance,
                    pd.FoodAllowance,
                    pd.OtherAllowances,
                    pd.OvertimeAmount,
                    pd.Incentives,
                    pd.Commissions,
                    pd.Rewards,
                    pd.OtherEarnings,
                    pd.GrossEarnings,
                    pd.InsuranceEmployee,
                    pd.InsuranceCompany,
                    pd.IncomeTax,
                    pd.LoanDeduction,
                    pd.PenaltyDeduction,
                    pd.AbsenceDeduction,
                    pd.LateDeduction,
                    pd.OtherDeductions,
                    pd.TotalDeductions,
                    pd.NetSalary,
                    pd.PaymentMethod,
                    pd.IsPaid,
                    pd.PaidDate,
                    pd.Notes
                FROM dbo.PayrollDetails pd
                INNER JOIN dbo.Employees e ON pd.EmployeeID = e.EmployeeID
                LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                LEFT JOIN dbo.JobTitles j ON e.JobTitleID = j.JobTitleID
                WHERE pd.PayrollID = @PayrollID
                ORDER BY e.FullNameAr;";

            var result = await connection.QueryAsync<PayrollDetailDto>(sql, new { PayrollID = payrollId });
            return result.ToList();
        }

        public async Task<PayrollDetailDto?> GetPayrollDetailByEmployeeAsync(int payrollId, int employeeId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    pd.PayrollDetailID,
                    pd.PayrollID,
                    pd.EmployeeID,
                    e.EmployeeCode,
                    e.FullNameAr AS EmployeeName,
                    d.DepartmentNameAr AS DepartmentName,
                    j.JobTitleNameAr AS JobTitleName,
                    pd.WorkingDays,
                    pd.PresentDays,
                    pd.AbsentDays,
                    pd.LeaveDays,
                    pd.OvertimeHours,
                    pd.BasicSalary,
                    pd.TransportAllowance,
                    pd.HousingAllowance,
                    pd.PhoneAllowance,
                    pd.FoodAllowance,
                    pd.OtherAllowances,
                    pd.OvertimeAmount,
                    pd.Incentives,
                    pd.Commissions,
                    pd.Rewards,
                    pd.OtherEarnings,
                    pd.GrossEarnings,
                    pd.InsuranceEmployee,
                    pd.InsuranceCompany,
                    pd.IncomeTax,
                    pd.LoanDeduction,
                    pd.PenaltyDeduction,
                    pd.AbsenceDeduction,
                    pd.LateDeduction,
                    pd.OtherDeductions,
                    pd.TotalDeductions,
                    pd.NetSalary,
                    pd.PaymentMethod,
                    pd.IsPaid,
                    pd.PaidDate,
                    pd.Notes
                FROM dbo.PayrollDetails pd
                INNER JOIN dbo.Employees e ON pd.EmployeeID = e.EmployeeID
                LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                LEFT JOIN dbo.JobTitles j ON e.JobTitleID = j.JobTitleID
                WHERE pd.PayrollID = @PayrollID
                  AND pd.EmployeeID = @EmployeeID;";

            return await connection.QueryFirstOrDefaultAsync<PayrollDetailDto>(sql, new
            {
                PayrollID = payrollId,
                EmployeeID = employeeId
            });
        }

        public async Task<int> CreatePayrollAsync(PayrollRunEditDto item, int userId)
        {
            using var connection = CreateConnection();

            if (item.PayrollMonth == default)
                throw new Exception("يجب تحديد شهر المرتبات");

            var payrollMonth = new DateTime(item.PayrollMonth.Year, item.PayrollMonth.Month, 1);

            var existingCount = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*)
                FROM dbo.Payroll
                WHERE PayrollMonth = @PayrollMonth
                  AND PayrollStatus <> 5;",
                new { PayrollMonth = payrollMonth });

            if (existingCount > 0)
                throw new Exception("يوجد بالفعل مسير مرتبات لنفس الشهر");

            var periodId = await connection.ExecuteScalarAsync<int?>(@"
                SELECT TOP 1 PeriodID
                FROM dbo.AccountingPeriods
                WHERE @PayrollMonth BETWEEN StartDate AND EndDate
                  AND PeriodStatus = 1
                ORDER BY StartDate DESC;",
                new { PayrollMonth = payrollMonth });

            if (!periodId.HasValue)
                throw new Exception("لا توجد فترة محاسبية مفتوحة لهذا الشهر");

            var payrollNumber = await _seq.GetNextCodeAsync("PR");
            var payrollTitle = string.IsNullOrWhiteSpace(item.PayrollTitle)
                ? $"مرتبات شهر {payrollMonth:MM/yyyy}"
                : item.PayrollTitle.Trim();

            var sql = @"
                INSERT INTO dbo.Payroll
                    (PayrollNumber, PayrollMonth, PeriodID, PayrollTitle, PayrollStatus, CreatedBy, CreatedDate)
                VALUES
                    (@PayrollNumber, @PayrollMonth, @PeriodID, @PayrollTitle, 1, @CreatedBy, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                PayrollNumber = payrollNumber,
                PayrollMonth = payrollMonth,
                PeriodID = periodId.Value,
                PayrollTitle = payrollTitle,
                CreatedBy = userId
            });

            await _audit.WriteAuditLogAsync(
                userId, 1, "Payroll", newId.ToString(),
                moduleName: "SCR_PAYROLL",
                description: $"إنشاء مسير مرتبات: {payrollNumber} - {payrollMonth:MM/yyyy}");

            return newId;
        }

        public async Task CalculatePayrollAsync(int payrollId, int userId)
        {
            using var connection = CreateConnection();

            var payroll = await connection.QueryFirstOrDefaultAsync<PayrollActionInfo>(@"
                SELECT PayrollID, PayrollNumber, PayrollMonth, PayrollStatus
                FROM dbo.Payroll
                WHERE PayrollID = @PayrollID;",
                new { PayrollID = payrollId });

            if (payroll == null)
                throw new Exception("المسير غير موجود");

            if (payroll.PayrollStatus != 1)
                throw new Exception("يمكن حساب المسير فقط وهو في حالة مسودة");

            var employeeId = await connection.ExecuteScalarAsync<int?>(
                "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID",
                new { UserID = userId });

            await connection.ExecuteAsync(
                "sp_CalculatePayroll",
                new { PayrollID = payrollId },
                commandType: CommandType.StoredProcedure);

            await connection.ExecuteAsync(@"
                UPDATE dbo.Payroll
                SET CalculatedBy = COALESCE(@CalculatedBy, CalculatedBy),
                    CalculatedDate = GETDATE(),
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE PayrollID = @PayrollID;",
                new
                {
                    PayrollID = payrollId,
                    CalculatedBy = employeeId,
                    ModifiedBy = userId
                });

            var info = await connection.QueryFirstOrDefaultAsync<PayrollNotifyInfo>(@"
                SELECT PayrollNumber, PayrollMonth, EmployeeCount, TotalNetSalary
                FROM dbo.Payroll
                WHERE PayrollID = @PayrollID;",
                new { PayrollID = payrollId });

            if (info != null)
            {
                await _notif.CreateNotificationAsync(
                    notificationType: 2,
                    title: "تم احتساب مسير مرتبات بانتظار الاعتماد",
                    message: $"تم احتساب المسير {info.PayrollNumber} لشهر {info.PayrollMonth:MM/yyyy} بعدد {info.EmployeeCount} موظف وبصافي {info.TotalNetSalary:#,##0.00}",
                    priority: 2,
                    targetRoleId: 2,
                    relatedModule: "SCR_PAYROLL",
                    relatedRecordId: payrollId,
                    createdBy: userId);
            }

            await _audit.WriteAuditLogAsync(
                userId, 2, "Payroll", payrollId.ToString(),
                moduleName: "SCR_PAYROLL",
                description: $"حساب مسير مرتبات #{payroll.PayrollNumber}");
        }

        public async Task ApprovePayrollAsync(int payrollId, int userId)
        {
            using var connection = CreateConnection();

            var payroll = await connection.QueryFirstOrDefaultAsync<PayrollActionInfo>(@"
                SELECT PayrollID, PayrollNumber, PayrollMonth, PayrollStatus
                FROM dbo.Payroll
                WHERE PayrollID = @PayrollID;",
                new { PayrollID = payrollId });

            if (payroll == null)
                throw new Exception("المسير غير موجود");

            if (payroll.PayrollStatus != 2)
                throw new Exception("يمكن اعتماد المسير فقط بعد الحساب");

            var employeeId = await connection.ExecuteScalarAsync<int?>(
                "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID",
                new { UserID = userId });

            if (employeeId == null || employeeId == 0)
                throw new Exception("لم يتم العثور على بيانات الموظف المرتبط بحسابك");

            await connection.ExecuteAsync(@"
                UPDATE dbo.Payroll
                SET PayrollStatus = 3,
                    ApprovedBy = @ApprovedBy,
                    ApprovedDate = GETDATE(),
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE PayrollID = @PayrollID;",
                new
                {
                    PayrollID = payrollId,
                    ApprovedBy = employeeId.Value,
                    ModifiedBy = userId
                });

            await _notif.MarkRelatedAsActionedAsync("SCR_PAYROLL", payrollId);

            await _audit.WriteAuditLogAsync(
                userId, 2, "Payroll", payrollId.ToString(),
                moduleName: "SCR_PAYROLL",
                description: $"اعتماد مسير مرتبات #{payroll.PayrollNumber}");
        }

        public async Task PayPayrollAsync(int payrollId, int userId)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();

            try
            {
                var payroll = await connection.QueryFirstOrDefaultAsync<PayrollPayInfo>(@"
                    SELECT PayrollID, PayrollNumber, PayrollMonth, PayrollStatus, JournalID
                    FROM dbo.Payroll
                    WHERE PayrollID = @PayrollID;",
                    new { PayrollID = payrollId }, transaction);

                if (payroll == null)
                    throw new Exception("المسير غير موجود");

                if (payroll.PayrollStatus != 3)
                    throw new Exception("يمكن دفع المسير فقط بعد الاعتماد");

                var employeeId = await connection.ExecuteScalarAsync<int?>(
                    "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID",
                    new { UserID = userId }, transaction);

                if (employeeId == null || employeeId == 0)
                    throw new Exception("لم يتم العثور على بيانات الموظف المرتبط بحسابك");

                if (!payroll.JournalID.HasValue || payroll.JournalID.Value == 0)
                {
                    await connection.ExecuteAsync(
                        "sp_CreatePayrollJournal",
                        new { PayrollID = payrollId, UserID = userId },
                        transaction: transaction,
                        commandType: CommandType.StoredProcedure);
                }

                var paidDateTime = DateTime.Now;
                var paidDate = paidDateTime.Date;
                var monthEnd = new DateTime(
                    payroll.PayrollMonth.Year,
                    payroll.PayrollMonth.Month,
                    DateTime.DaysInMonth(payroll.PayrollMonth.Year, payroll.PayrollMonth.Month));

                await connection.ExecuteAsync(@"
                    UPDATE dbo.Payroll
                    SET PayrollStatus = 4,
                        PaidBy = @PaidBy,
                        PaidDate = @PaidDateTime,
                        ModifiedBy = @ModifiedBy,
                        ModifiedDate = GETDATE()
                    WHERE PayrollID = @PayrollID;",
                    new
                    {
                        PayrollID = payrollId,
                        PaidBy = employeeId.Value,
                        PaidDateTime = paidDateTime,
                        ModifiedBy = userId
                    }, transaction);

                await connection.ExecuteAsync(@"
                    UPDATE dbo.PayrollDetails
                    SET IsPaid = 1,
                        PaidDate = @PaidDate
                    WHERE PayrollID = @PayrollID;",
                    new
                    {
                        PayrollID = payrollId,
                        PaidDate = paidDate
                    }, transaction);

                await connection.ExecuteAsync(@"
                    UPDATE li
                    SET li.PaidAmount = li.Amount,
                        li.PaidDate = @PaidDate,
                        li.InstallmentStatus = 2,
                        li.PayrollID = @PayrollID
                    FROM dbo.LoanInstallments li
                    INNER JOIN dbo.EmployeeLoans el ON li.LoanID = el.LoanID
                    INNER JOIN dbo.PayrollDetails pd ON pd.EmployeeID = el.EmployeeID AND pd.PayrollID = @PayrollID
                    WHERE li.InstallmentStatus = 1
                      AND li.DueDate <= @MonthEnd
                      AND el.LoanStatus IN (2, 3)
                      AND ISNULL(pd.LoanDeduction, 0) > 0;",
                    new
                    {
                        PayrollID = payrollId,
                        MonthEnd = monthEnd,
                        PaidDate = paidDate
                    }, transaction);

                await connection.ExecuteAsync(@"
                    UPDATE el
                    SET el.PaidAmount = ISNULL(x.TotalPaid, 0),
                        el.LoanStatus = CASE
                            WHEN ISNULL(x.TotalPaid, 0) >= el.LoanAmount THEN 4
                            WHEN ISNULL(x.TotalPaid, 0) > 0 THEN 3
                            ELSE el.LoanStatus
                        END,
                        el.ModifiedBy = @ModifiedBy,
                        el.ModifiedDate = GETDATE()
                    FROM dbo.EmployeeLoans el
                    OUTER APPLY (
                        SELECT SUM(ISNULL(li.PaidAmount, 0)) AS TotalPaid
                        FROM dbo.LoanInstallments li
                        WHERE li.LoanID = el.LoanID
                    ) x
                    WHERE EXISTS (
                        SELECT 1
                        FROM dbo.LoanInstallments li
                        WHERE li.LoanID = el.LoanID
                          AND li.PayrollID = @PayrollID
                    );",
                    new
                    {
                        PayrollID = payrollId,
                        ModifiedBy = userId
                    }, transaction);

                await connection.ExecuteAsync(@"
                    UPDATE pr
                    SET pr.PayrollID = @PayrollID
                    FROM dbo.EmployeePenaltiesRewards pr
                    WHERE pr.RecordStatus = 2
                      AND pr.PayrollID IS NULL
                      AND YEAR(pr.EffectiveMonth) = YEAR(@PayrollMonth)
                      AND MONTH(pr.EffectiveMonth) = MONTH(@PayrollMonth)
                      AND EXISTS (
                          SELECT 1
                          FROM dbo.PayrollDetails pd
                          WHERE pd.PayrollID = @PayrollID
                            AND pd.EmployeeID = pr.EmployeeID
                      );",
                    new
                    {
                        PayrollID = payrollId,
                        PayrollMonth = payroll.PayrollMonth
                    }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId, 2, "Payroll", payrollId.ToString(),
                    moduleName: "SCR_PAYROLL",
                    description: $"دفع مسير مرتبات #{payroll.PayrollNumber}");
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        public async Task CancelPayrollAsync(int payrollId, int userId)
        {
            using var connection = CreateConnection();

            var payroll = await connection.QueryFirstOrDefaultAsync<PayrollActionInfo>(@"
                SELECT PayrollID, PayrollNumber, PayrollMonth, PayrollStatus
                FROM dbo.Payroll
                WHERE PayrollID = @PayrollID;",
                new { PayrollID = payrollId });

            if (payroll == null)
                throw new Exception("المسير غير موجود");

            if (payroll.PayrollStatus != 1 && payroll.PayrollStatus != 2)
                throw new Exception("يمكن إلغاء المسير فقط في حالة مسودة أو محسوب");

            await connection.ExecuteAsync(@"
                UPDATE dbo.Payroll
                SET PayrollStatus = 5,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE PayrollID = @PayrollID;",
                new
                {
                    PayrollID = payrollId,
                    ModifiedBy = userId
                });

            await _notif.MarkRelatedAsActionedAsync("SCR_PAYROLL", payrollId);

            await _audit.WriteAuditLogAsync(
                userId, 2, "Payroll", payrollId.ToString(),
                moduleName: "SCR_PAYROLL",
                description: $"إلغاء مسير مرتبات #{payroll.PayrollNumber}");
        }

        public async Task UpdatePayrollDetailManualAsync(PayrollDetailEditDto item, int userId)
        {
            using var connection = CreateConnection();

            if (item.PayrollDetailID <= 0)
                throw new Exception("تفصيل المسير غير صحيح");

            if (item.Incentives < 0 ||
                item.Commissions < 0 ||
                item.Rewards < 0 ||
                item.OtherEarnings < 0 ||
                item.OtherDeductions < 0)
                throw new Exception("لا يسمح بقيم سالبة في التعديل اليدوي");

            var current = await connection.QueryFirstOrDefaultAsync<PayrollDetailStatusInfo>(@"
                SELECT pd.PayrollDetailID, pd.PayrollID, p.PayrollStatus
                FROM dbo.PayrollDetails pd
                INNER JOIN dbo.Payroll p ON pd.PayrollID = p.PayrollID
                WHERE pd.PayrollDetailID = @PayrollDetailID;",
                new { item.PayrollDetailID });

            if (current == null)
                throw new Exception("تفصيل المسير غير موجود");

            if (current.PayrollStatus != 1 && current.PayrollStatus != 2)
                throw new Exception("التعديل اليدوي مسموح فقط في حالة مسودة أو محسوب");

            await connection.ExecuteAsync(@"
                UPDATE dbo.PayrollDetails
                SET Incentives = @Incentives,
                    Commissions = @Commissions,
                    Rewards = @Rewards,
                    OtherEarnings = @OtherEarnings,
                    OtherDeductions = @OtherDeductions,
                    Notes = @Notes
                WHERE PayrollDetailID = @PayrollDetailID;",
                new
                {
                    item.PayrollDetailID,
                    item.Incentives,
                    item.Commissions,
                    item.Rewards,
                    item.OtherEarnings,
                    item.OtherDeductions,
                    item.Notes
                });

            await connection.ExecuteAsync(@"
                UPDATE dbo.Payroll
                SET ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE PayrollID = @PayrollID;",
                new
                {
                    PayrollID = current.PayrollID,
                    ModifiedBy = userId
                });

            await RefreshPayrollTotalsAsync(current.PayrollID, connection);

            await _audit.WriteAuditLogAsync(
                userId, 2, "PayrollDetails", item.PayrollDetailID.ToString(),
                moduleName: "SCR_PAYROLL",
                description: $"تعديل يدوي لتفاصيل المسير - الموظف {item.EmployeeCode} / {item.EmployeeName}");
        }

        private async Task RefreshPayrollTotalsAsync(int payrollId, IDbConnection connection, IDbTransaction? transaction = null)
        {
            await connection.ExecuteAsync(@"
                UPDATE p
                SET TotalBasicSalary = ISNULL(x.TotalBasicSalary, 0),
                    TotalAllowances = ISNULL(x.TotalAllowances, 0),
                    TotalOvertime = ISNULL(x.TotalOvertime, 0),
                    TotalIncentives = ISNULL(x.TotalIncentives, 0),
                    TotalCommissions = ISNULL(x.TotalCommissions, 0),
                    TotalGrossEarnings = ISNULL(x.TotalGrossEarnings, 0),
                    TotalInsuranceEmp = ISNULL(x.TotalInsuranceEmp, 0),
                    TotalInsuranceComp = ISNULL(x.TotalInsuranceComp, 0),
                    TotalTax = ISNULL(x.TotalTax, 0),
                    TotalLoanDeductions = ISNULL(x.TotalLoanDeductions, 0),
                    TotalPenalties = ISNULL(x.TotalPenalties, 0),
                    TotalAbsenceDeductions = ISNULL(x.TotalAbsenceDeductions, 0),
                    TotalOtherDeductions = ISNULL(x.TotalOtherDeductions, 0),
                    TotalDeductions = ISNULL(x.TotalDeductions, 0),
                    TotalNetSalary = ISNULL(x.TotalNetSalary, 0),
                    EmployeeCount = ISNULL(x.EmployeeCount, 0),
                    ModifiedDate = GETDATE()
                FROM dbo.Payroll p
                OUTER APPLY (
                    SELECT
                        SUM(ISNULL(pd.BasicSalary, 0)) AS TotalBasicSalary,
                        SUM(
                            ISNULL(pd.TransportAllowance, 0) +
                            ISNULL(pd.HousingAllowance, 0) +
                            ISNULL(pd.PhoneAllowance, 0) +
                            ISNULL(pd.FoodAllowance, 0) +
                            ISNULL(pd.OtherAllowances, 0)
                        ) AS TotalAllowances,
                        SUM(ISNULL(pd.OvertimeAmount, 0)) AS TotalOvertime,
                        SUM(ISNULL(pd.Incentives, 0)) AS TotalIncentives,
                        SUM(ISNULL(pd.Commissions, 0)) AS TotalCommissions,
                        SUM(ISNULL(pd.GrossEarnings, 0)) AS TotalGrossEarnings,
                        SUM(ISNULL(pd.InsuranceEmployee, 0)) AS TotalInsuranceEmp,
                        SUM(ISNULL(pd.InsuranceCompany, 0)) AS TotalInsuranceComp,
                        SUM(ISNULL(pd.IncomeTax, 0)) AS TotalTax,
                        SUM(ISNULL(pd.LoanDeduction, 0)) AS TotalLoanDeductions,
                        SUM(ISNULL(pd.PenaltyDeduction, 0)) AS TotalPenalties,
                        SUM(ISNULL(pd.AbsenceDeduction, 0)) AS TotalAbsenceDeductions,
                        SUM(ISNULL(pd.OtherDeductions, 0) + ISNULL(pd.LateDeduction, 0)) AS TotalOtherDeductions,
                        SUM(ISNULL(pd.TotalDeductions, 0)) AS TotalDeductions,
                        SUM(ISNULL(pd.NetSalary, 0)) AS TotalNetSalary,
                        COUNT(*) AS EmployeeCount
                    FROM dbo.PayrollDetails pd
                    WHERE pd.PayrollID = p.PayrollID
                ) x
                WHERE p.PayrollID = @PayrollID;",
                new { PayrollID = payrollId }, transaction);
        }

        public async Task<byte[]> ExportPayrollToExcelAsync(List<PayrollDetailDto> details, string payrollNumber)
        {
            await Task.CompletedTask;

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("كشف المرتبات");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = $"كشف المرتبات — {payrollNumber}";
            ws.Range(1, 1, 1, 21).Merge().Style.Font.Bold = true;

            var headers = new[]
            {
                "#", "الكود", "الموظف", "القسم", "الوظيفة",
                "الحضور", "الأساسي", "البدلات", "الحوافز", "العمولات",
                "المكافآت", "استحقاقات أخرى", "إجمالي الاستحقاقات",
                "تأمينات", "ضرائب", "سلف", "جزاءات", "غياب",
                "خصومات أخرى/تأخير", "إجمالي الخصومات", "صافي المرتب"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1d143f");
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            }

            int row = 4, num = 0;
            foreach (var d in details)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = d.EmployeeCode ?? "";
                ws.Cell(row, 3).Value = d.EmployeeName ?? "";
                ws.Cell(row, 4).Value = d.DepartmentName ?? "";
                ws.Cell(row, 5).Value = d.JobTitleName ?? "";
                ws.Cell(row, 6).Value = d.PresentDays;
                ws.Cell(row, 7).Value = d.BasicSalary;
                ws.Cell(row, 8).Value = d.TransportAllowance + d.HousingAllowance + d.PhoneAllowance + d.FoodAllowance + d.OtherAllowances;
                ws.Cell(row, 9).Value = d.Incentives;
                ws.Cell(row, 10).Value = d.Commissions;
                ws.Cell(row, 11).Value = d.Rewards;
                ws.Cell(row, 12).Value = d.OtherEarnings;
                ws.Cell(row, 13).Value = d.GrossEarnings;
                ws.Cell(row, 14).Value = d.InsuranceEmployee;
                ws.Cell(row, 15).Value = d.IncomeTax;
                ws.Cell(row, 16).Value = d.LoanDeduction;
                ws.Cell(row, 17).Value = d.PenaltyDeduction;
                ws.Cell(row, 18).Value = d.AbsenceDeduction;
                ws.Cell(row, 19).Value = d.OtherDeductions + d.LateDeduction;
                ws.Cell(row, 20).Value = d.TotalDeductions;
                ws.Cell(row, 21).Value = d.NetSalary;
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private sealed class PayrollActionInfo
        {
            public int PayrollID { get; set; }
            public string? PayrollNumber { get; set; }
            public DateTime PayrollMonth { get; set; }
            public int PayrollStatus { get; set; }
        }

        private sealed class PayrollPayInfo
        {
            public int PayrollID { get; set; }
            public string? PayrollNumber { get; set; }
            public DateTime PayrollMonth { get; set; }
            public int PayrollStatus { get; set; }
            public int? JournalID { get; set; }
        }

        private sealed class PayrollNotifyInfo
        {
            public string? PayrollNumber { get; set; }
            public DateTime PayrollMonth { get; set; }
            public int EmployeeCount { get; set; }
            public decimal TotalNetSalary { get; set; }
        }

        private sealed class PayrollDetailStatusInfo
        {
            public int PayrollDetailID { get; set; }
            public int PayrollID { get; set; }
            public int PayrollStatus { get; set; }
        }
    }
}