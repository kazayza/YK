using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class DashboardService : BaseDbService
    {
        public DashboardService(IConfiguration configuration) : base(configuration) { }

        // ==========================================
        // إحصائيات عامة
        // ==========================================
        public async Task<DashboardStatsDto> GetStatsAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    (SELECT COUNT(*) FROM dbo.Items WHERE IsActive = 1) AS TotalItems,
                    (SELECT COUNT(*) FROM dbo.ItemCategories WHERE IsActive = 1) AS TotalCategories,
                    (SELECT COUNT(*) FROM dbo.Suppliers WHERE IsActive = 1) AS TotalSuppliers,
                    (SELECT COUNT(*) FROM dbo.Customers WHERE IsActive = 1) AS TotalCustomers,
                    (SELECT COUNT(*) FROM dbo.Warehouses WHERE IsActive = 1) AS TotalWarehouses,
                    (SELECT COUNT(*) FROM dbo.Employees WHERE IsActive = 1 AND EmployeeStatus = 1) AS TotalEmployees,
                    (SELECT COUNT(*) FROM dbo.SystemUsers WHERE IsActive = 1) AS TotalUsers,
                    (SELECT COUNT(*) FROM dbo.LoginHistory WHERE CAST(LoginTime AS DATE) = CAST(GETDATE() AS DATE) AND LoginStatus = 1) AS TodayLogins,
                    (SELECT COUNT(*) FROM dbo.AuditLog WHERE CAST(AuditDate AS DATE) = CAST(GETDATE() AS DATE)) AS TodayAuditActions,
                    (SELECT ISNULL(SUM(CurrentBalance), 0) FROM dbo.Suppliers WHERE IsActive = 1) AS SuppliersBalance,
                    (SELECT ISNULL(SUM(CurrentBalance), 0) FROM dbo.Customers WHERE IsActive = 1) AS CustomersBalance";

            return await connection.QueryFirstOrDefaultAsync<DashboardStatsDto>(sql) ?? new DashboardStatsDto();
        }

        // ==========================================
        // آخر العمليات
        // ==========================================
        public async Task<List<RecentActivityDto>> GetRecentActivitiesAsync(int take = 10)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT TOP (@Take)
                            AuditDate, Username,
                            CASE ActionType
                                WHEN 1 THEN N'إضافة' WHEN 2 THEN N'تعديل'
                                WHEN 3 THEN N'حذف' WHEN 4 THEN N'عرض' WHEN 5 THEN N'طباعة'
                            END AS ActionName,
                            TableName, Description
                        FROM dbo.AuditLog ORDER BY AuditID DESC";
            return (await connection.QueryAsync<RecentActivityDto>(sql, new { Take = take })).ToList();
        }

        // ==========================================
        // طلبات بانتظار الاعتماد
        // ==========================================
        public async Task<DashboardPendingDto> GetPendingApprovalsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"
                SELECT
                    (SELECT COUNT(*) FROM dbo.PurchaseRequests WHERE RequestStatus = 2) AS PendingPR,
                    (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE POStatus = 2) AS PendingPO,
                    (SELECT COUNT(*) FROM dbo.GoodsReceiptNotes WHERE GRNStatus = 1
                        AND (SELECT COUNT(*) FROM dbo.GoodsReceiptDetails WHERE GRNID = dbo.GoodsReceiptNotes.GRNID) > 0) AS PendingGRN,
                    (SELECT COUNT(*) FROM dbo.PurchaseInvoices WHERE InvoiceStatus = 2) AS PendingInvoice,
                    (SELECT COUNT(*) FROM dbo.PurchaseReturns WHERE ReturnStatus = 2) AS PendingReturn";

            return await connection.QueryFirstOrDefaultAsync<DashboardPendingDto>(sql) ?? new DashboardPendingDto();
        }

        // ==========================================
        // تنبيهات المخزون
        // ==========================================
        public async Task<DashboardInventoryAlertsDto> GetInventoryAlertsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"
                SELECT
                    (SELECT COUNT(*) FROM dbo.InventoryBalance ib
                     INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
                     WHERE ib.CurrentQty <= 0 AND i.IsActive = 1) AS OutOfStock,

                    (SELECT COUNT(*) FROM dbo.InventoryBalance ib
                     INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
                     WHERE ib.CurrentQty > 0 AND i.MinStockLevel > 0
                       AND ib.CurrentQty <= i.MinStockLevel AND i.IsActive = 1) AS BelowMinimum,

                    (SELECT COUNT(*) FROM dbo.InventoryBatchBalance bb
                     WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
                       AND bb.ExpiryDate > CAST(GETDATE() AS DATE)
                       AND bb.ExpiryDate <= DATEADD(DAY, 30, GETDATE())) AS NearExpiry,

                    (SELECT COUNT(*) FROM dbo.InventoryBatchBalance bb
                     WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
                       AND bb.ExpiryDate <= CAST(GETDATE() AS DATE)) AS Expired,

                    (SELECT ISNULL(SUM(CurrentQty * ISNULL(AverageCost, 0)), 0)
                     FROM dbo.InventoryBalance WHERE CurrentQty > 0) AS TotalStockValue,

                    (SELECT COUNT(DISTINCT ItemID) FROM dbo.InventoryBalance WHERE CurrentQty > 0) AS ItemsWithStock";

            return await connection.QueryFirstOrDefaultAsync<DashboardInventoryAlertsDto>(sql) ?? new DashboardInventoryAlertsDto();
        }

        // ==========================================
        // فواتير متأخرة
        // ==========================================
        public async Task<DashboardOverdueDto> GetOverdueInfoAsync()
        {
            using var connection = CreateConnection();
            var sql = @"
                SELECT
                    (SELECT COUNT(*) FROM dbo.PurchaseInvoices
                     WHERE DueDate < CAST(GETDATE() AS DATE)
                       AND PaymentStatus != 3 AND InvoiceStatus NOT IN (1, 7)) AS OverdueInvoices,

                    (SELECT ISNULL(SUM(TotalAmount - ISNULL(PaidAmount, 0)), 0) FROM dbo.PurchaseInvoices
                     WHERE DueDate < CAST(GETDATE() AS DATE)
                       AND PaymentStatus != 3 AND InvoiceStatus NOT IN (1, 7)) AS OverdueAmount,

                    (SELECT COUNT(*) FROM dbo.PurchaseInvoices
                     WHERE PaymentStatus != 3 AND InvoiceStatus NOT IN (1, 7)) AS UnpaidInvoices,

                    (SELECT ISNULL(SUM(TotalAmount - ISNULL(PaidAmount, 0)), 0) FROM dbo.PurchaseInvoices
                     WHERE PaymentStatus != 3 AND InvoiceStatus NOT IN (1, 7)) AS TotalUnpaidAmount";

            return await connection.QueryFirstOrDefaultAsync<DashboardOverdueDto>(sql) ?? new DashboardOverdueDto();
        }

        // ==========================================
        // إحصائيات المشتريات هذا الشهر
        // ==========================================
        public async Task<DashboardPurchaseStatsDto> GetMonthlyPurchaseStatsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"
                DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
                SELECT
                    (SELECT COUNT(*) FROM dbo.PurchaseOrders
                     WHERE PODate >= @MonthStart AND POStatus NOT IN (9)) AS MonthlyPOs,

                    (SELECT ISNULL(SUM(TotalAmount), 0) FROM dbo.PurchaseOrders
                     WHERE PODate >= @MonthStart AND POStatus NOT IN (9)) AS MonthlyPOAmount,

                    (SELECT COUNT(*) FROM dbo.PurchaseInvoices
                     WHERE InvoiceDate >= @MonthStart AND InvoiceStatus NOT IN (7)) AS MonthlyInvoices,

                    (SELECT ISNULL(SUM(TotalAmount), 0) FROM dbo.PurchaseInvoices
                     WHERE InvoiceDate >= @MonthStart AND InvoiceStatus NOT IN (7)) AS MonthlyInvoiceAmount,

                    (SELECT COUNT(*) FROM dbo.GoodsReceiptNotes
                     WHERE GRNDate >= @MonthStart AND GRNStatus != 4) AS MonthlyGRNs,

                    (SELECT COUNT(*) FROM dbo.PurchaseReturns
                     WHERE ReturnDate >= @MonthStart AND ReturnStatus != 5) AS MonthlyReturns";

            return await connection.QueryFirstOrDefaultAsync<DashboardPurchaseStatsDto>(sql) ?? new DashboardPurchaseStatsDto();
        }

        // ==========================================
        // أهم الأصناف اللي محتاجة شراء
        // ==========================================
        public async Task<List<DashboardItemAlertDto>> GetTopItemAlertsAsync(int take = 5)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT TOP (@Take)
                            i.ItemID, i.ItemCode, i.ItemNameAr,
                            CASE i.ItemType WHEN 1 THEN N'خام' WHEN 2 THEN N'تعبئة'
                                WHEN 3 THEN N'نصف مصنع' WHEN 4 THEN N'تام' ELSE N'تشغيل' END AS ItemTypeName,
                            w.WarehouseNameAr,
                            ib.CurrentQty,
                            i.MinStockLevel,
                            i.ReorderLevel,
                            CASE
                                WHEN ib.CurrentQty <= 0 THEN 3
                                WHEN i.ReorderLevel > 0 AND ib.CurrentQty <= i.ReorderLevel THEN 2
                                WHEN i.MinStockLevel > 0 AND ib.CurrentQty <= i.MinStockLevel THEN 1
                                ELSE 0
                            END AS AlertLevel,
                            CASE
                                WHEN ib.CurrentQty <= 0 THEN N'نفد ❌'
                                WHEN i.ReorderLevel > 0 AND ib.CurrentQty <= i.ReorderLevel THEN N'تحت نقطة الطلب ⚠️'
                                WHEN i.MinStockLevel > 0 AND ib.CurrentQty <= i.MinStockLevel THEN N'أقل من الحد 🔶'
                                ELSE N'طبيعي'
                            END AS AlertName
                        FROM dbo.InventoryBalance ib
                        INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
                        INNER JOIN dbo.Warehouses w ON ib.WarehouseID = w.WarehouseID
                        WHERE i.IsActive = 1
                          AND (ib.CurrentQty <= 0
                               OR (i.MinStockLevel > 0 AND ib.CurrentQty <= i.MinStockLevel)
                               OR (i.ReorderLevel > 0 AND ib.CurrentQty <= i.ReorderLevel))
                        ORDER BY
                            CASE WHEN ib.CurrentQty <= 0 THEN 0
                                 WHEN i.ReorderLevel > 0 AND ib.CurrentQty <= i.ReorderLevel THEN 1
                                 ELSE 2 END,
                            ib.CurrentQty ASC";
            return (await connection.QueryAsync<DashboardItemAlertDto>(sql, new { Take = take })).ToList();
        }
        // ==========================================
        // دلتا إحصائيات الكروت — مقارنة بالشهر السابق
        // ==========================================
        public async Task<DashboardDeltasDto> GetStatsDeltasAsync()
        {
            try
            {
                using var connection = CreateConnection();
                var monthStart = "DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)";
                var prevStart = "DATEADD(MONTH,-1,DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))";
                var sql = $@"
                    SELECT
                        (SELECT COUNT(*) FROM dbo.Items WHERE IsActive = 1 AND CAST(CreatedDate AS DATE) >= {monthStart}) AS NewItems,
                        (SELECT COUNT(*) FROM dbo.Items WHERE IsActive = 1 AND CAST(CreatedDate AS DATE) >= {prevStart} AND CAST(CreatedDate AS DATE) < {monthStart}) AS PrevItems,
                        (SELECT COUNT(*) FROM dbo.Suppliers WHERE IsActive = 1 AND CAST(CreatedDate AS DATE) >= {monthStart}) AS NewSuppliers,
                        (SELECT COUNT(*) FROM dbo.Suppliers WHERE IsActive = 1 AND CAST(CreatedDate AS DATE) >= {prevStart} AND CAST(CreatedDate AS DATE) < {monthStart}) AS PrevSuppliers,
                        (SELECT COUNT(*) FROM dbo.Customers WHERE IsActive = 1 AND CAST(CreatedDate AS DATE) >= {monthStart}) AS NewCustomers,
                        (SELECT COUNT(*) FROM dbo.Customers WHERE IsActive = 1 AND CAST(CreatedDate AS DATE) >= {prevStart} AND CAST(CreatedDate AS DATE) < {monthStart}) AS PrevCustomers,
                        (SELECT COUNT(*) FROM dbo.Employees WHERE IsActive = 1 AND EmployeeStatus = 1 AND CAST(HireDate AS DATE) >= {monthStart}) AS NewEmployees,
                        (SELECT COUNT(*) FROM dbo.Employees WHERE IsActive = 1 AND EmployeeStatus = 1 AND CAST(HireDate AS DATE) >= {prevStart} AND CAST(HireDate AS DATE) < {monthStart}) AS PrevEmployees,
                        (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE CAST(OrderDate AS DATE) >= {monthStart}) AS NewPOs,
                        (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE CAST(OrderDate AS DATE) >= {prevStart} AND CAST(OrderDate AS DATE) < {monthStart}) AS PrevPOs";
                return await connection.QueryFirstOrDefaultAsync<DashboardDeltasDto>(sql) ?? new DashboardDeltasDto();
            }
            catch { return new DashboardDeltasDto(); }
        }
        // ==========================================
        // مشتريات آخر N شهر (للرسم البياني) — 6 أو 12
        // ==========================================
public async Task<List<MonthlyChartDto>> GetMonthlyPurchaseChartAsync(int months = 6)
{
    if (months < 1) months = 6;
    if (months > 12) months = 12;
    using var connection = CreateConnection();
    var sql = @"
        ;WITH Months AS (
            SELECT 0 AS N UNION ALL SELECT 1 UNION ALL SELECT 2
            UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5
            UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8
            UNION ALL SELECT 9 UNION ALL SELECT 10 UNION ALL SELECT 11
        )
        SELECT
            FORMAT(DATEADD(MONTH, -m.N, GETDATE()), 'yyyy-MM') AS MonthKey,
            DATENAME(MONTH, DATEADD(MONTH, -m.N, GETDATE())) AS MonthName,
            ISNULL(SUM(pi.TotalAmount), 0) AS Amount
        FROM Months m
        LEFT JOIN dbo.PurchaseInvoices pi
            ON FORMAT(pi.InvoiceDate, 'yyyy-MM') = FORMAT(DATEADD(MONTH, -m.N, GETDATE()), 'yyyy-MM')
            AND pi.InvoiceStatus NOT IN (7)
        WHERE m.N < @Months
        GROUP BY m.N, FORMAT(DATEADD(MONTH, -m.N, GETDATE()), 'yyyy-MM'),
                 DATENAME(MONTH, DATEADD(MONTH, -m.N, GETDATE()))
        ORDER BY MonthKey ASC";
    return (await connection.QueryAsync<MonthlyChartDto>(sql, new { Months = months })).ToList();
}
        // ==========================================
        // أوامر التصنيع الحالية - متابعة لحظية
        // ==========================================
        public async Task<List<CurrentProductionOrderDto>> GetCurrentProductionTrackingAsync(int take = 8)
        {
            using var connection = CreateConnection();

            var sql = @"
;WITH ActiveBatches AS
(
    SELECT
        pb.ProductionOrderID,
        COUNT(*) AS ActiveBatchCount
    FROM dbo.ProductionBatches pb
    WHERE pb.BatchStatus IN (2, 3, 4)
    GROUP BY pb.ProductionOrderID
),
CurrentStagePerOrder AS
(
    SELECT
        pb.ProductionOrderID,
        pb.BatchID,
        pb.BatchNumber,
        pbs.StageStatus AS CurrentStageStatus,
        CASE pbs.StageStatus
            WHEN 1 THEN N'في الانتظار'
            WHEN 2 THEN N'قيد التنفيذ'
            WHEN 3 THEN N'مكتمل'
            WHEN 4 THEN N'مشكلة'
            ELSE N''
        END AS CurrentStageStatusName,
        ps.StageNameAr AS CurrentStageName,
        pbs.StartTime AS StageStartTime,
        CASE
            WHEN pbs.StartTime IS NOT NULL THEN DATEDIFF(MINUTE, pbs.StartTime, GETDATE())
            ELSE 0
        END AS ElapsedMinutes,
        ISNULL(e.FullNameAr, N'') AS OperatorName,
        ISNULL(pbs.Temperature, N'') AS Temperature,
        ISNULL(pbs.Speed, N'') AS Speed,
        ROW_NUMBER() OVER
        (
            PARTITION BY pb.ProductionOrderID
            ORDER BY
                CASE
                    WHEN pbs.StageStatus = 2 THEN 0
                    WHEN pbs.StageStatus = 4 THEN 1
                    WHEN pbs.StageStatus = 1 THEN 2
                    WHEN pbs.StageStatus = 3 THEN 3
                    ELSE 4
                END,
                ISNULL(pbs.StartTime, '9999-12-31'),
                pbs.StageOrder
        ) AS RN
    FROM dbo.ProductionBatches pb
    INNER JOIN dbo.ProductionBatchStages pbs ON pb.BatchID = pbs.BatchID
    INNER JOIN dbo.ProductionStages ps ON pbs.StageID = ps.StageID
    LEFT JOIN dbo.Employees e ON pbs.OperatorID = e.EmployeeID
    WHERE pb.BatchStatus IN (2, 3, 4)
      AND pbs.StageStatus IN (1, 2, 3, 4)
),
StageProgress AS
(
    SELECT
        pb.ProductionOrderID,
        SUM(CASE WHEN pbs.StageStatus = 3 THEN 1 ELSE 0 END) AS CompletedStages,
        COUNT(*) AS TotalStages
    FROM dbo.ProductionBatches pb
    INNER JOIN dbo.ProductionBatchStages pbs ON pb.BatchID = pbs.BatchID
    WHERE pb.BatchStatus IN (2, 3, 4)
    GROUP BY pb.ProductionOrderID
)
SELECT TOP (@Take)
    po.ProductionOrderID,
    po.OrderNumber,
    i.ItemNameAr AS ProductName,
    po.OrderStatus,
    CASE po.OrderStatus
        WHEN 3 THEN N'جاري التجهيز'
        WHEN 4 THEN N'قيد التصنيع'
        WHEN 5 THEN N'فحص الجودة'
        ELSE N''
    END AS OrderStatusName,
    ISNULL(ab.ActiveBatchCount, 0) AS ActiveBatchCount,
    cso.BatchID AS CurrentBatchID,
    ISNULL(cso.BatchNumber, N'') AS CurrentBatchNumber,
    ISNULL(cso.CurrentStageName, N'') AS CurrentStageName,
    ISNULL(cso.CurrentStageStatus, 0) AS CurrentStageStatus,
    ISNULL(cso.CurrentStageStatusName, N'') AS CurrentStageStatusName,
    cso.StageStartTime,
    ISNULL(cso.ElapsedMinutes, 0) AS ElapsedMinutes,
    ISNULL(cso.OperatorName, N'') AS OperatorName,
    ISNULL(cso.Temperature, N'') AS Temperature,
    ISNULL(cso.Speed, N'') AS Speed,
    ISNULL(sp.CompletedStages, 0) AS CompletedStages,
    ISNULL(sp.TotalStages, 0) AS TotalStages
FROM dbo.ProductionOrders po
INNER JOIN dbo.Items i ON po.ProductItemID = i.ItemID
LEFT JOIN ActiveBatches ab ON po.ProductionOrderID = ab.ProductionOrderID
LEFT JOIN CurrentStagePerOrder cso
    ON po.ProductionOrderID = cso.ProductionOrderID
   AND cso.RN = 1
LEFT JOIN StageProgress sp ON po.ProductionOrderID = sp.ProductionOrderID
WHERE po.OrderStatus IN (3, 4, 5)
ORDER BY
    CASE po.OrderStatus
        WHEN 4 THEN 0
        WHEN 5 THEN 1
        WHEN 3 THEN 2
        ELSE 3
    END,
    ISNULL(cso.StageStartTime, GETDATE()) ASC,
    po.OrderDate DESC;";

            var result = await connection.QueryAsync<CurrentProductionOrderDto>(sql, new { Take = take });
            return result.ToList();
        }

        // ==========================================
        // تفاصيل الباتشات الحية لأمر تصنيع واحد
        // ==========================================
        public async Task<List<CurrentProductionBatchDto>> GetProductionOrderLiveBatchesAsync(int productionOrderId)
        {
            using var connection = CreateConnection();

            var sql = @"
;WITH BatchBase AS
(
    SELECT
        pb.BatchID,
        pb.BatchNumber,
        pb.BatchStatus,
        CASE pb.BatchStatus
            WHEN 1 THEN N'مسودة'
            WHEN 2 THEN N'قيد التصنيع'
            WHEN 3 THEN N'اكتمل التصنيع'
            WHEN 4 THEN N'في فحص الجودة'
            WHEN 5 THEN N'مقبول'
            WHEN 6 THEN N'مرفوض'
            ELSE N''
        END AS BatchStatusName
    FROM dbo.ProductionBatches pb
    WHERE pb.ProductionOrderID = @ProductionOrderID
      AND pb.BatchStatus IN (2, 3, 4)
),
CurrentStagePerBatch AS
(
    SELECT
        pb.BatchID,
        ps.StageNameAr AS CurrentStageName,
        pbs.StageStatus AS CurrentStageStatus,
        CASE pbs.StageStatus
            WHEN 1 THEN N'في الانتظار'
            WHEN 2 THEN N'قيد التنفيذ'
            WHEN 3 THEN N'مكتمل'
            WHEN 4 THEN N'مشكلة'
            ELSE N''
        END AS CurrentStageStatusName,
        pbs.StartTime AS StageStartTime,
        CASE
            WHEN pbs.StartTime IS NOT NULL THEN DATEDIFF(MINUTE, pbs.StartTime, GETDATE())
            ELSE 0
        END AS ElapsedMinutes,
        ISNULL(e.FullNameAr, N'') AS OperatorName,
        ISNULL(pbs.Temperature, N'') AS Temperature,
        ISNULL(pbs.Speed, N'') AS Speed,
        ROW_NUMBER() OVER
        (
            PARTITION BY pb.BatchID
            ORDER BY
                CASE
                    WHEN pbs.StageStatus = 2 THEN 0
                    WHEN pbs.StageStatus = 4 THEN 1
                    WHEN pbs.StageStatus = 1 THEN 2
                    WHEN pbs.StageStatus = 3 THEN 3
                    ELSE 4
                END,
                ISNULL(pbs.StartTime, '9999-12-31'),
                pbs.StageOrder
        ) AS RN
    FROM dbo.ProductionBatches pb
    INNER JOIN dbo.ProductionBatchStages pbs ON pb.BatchID = pbs.BatchID
    INNER JOIN dbo.ProductionStages ps ON pbs.StageID = ps.StageID
    LEFT JOIN dbo.Employees e ON pbs.OperatorID = e.EmployeeID
    WHERE pb.ProductionOrderID = @ProductionOrderID
      AND pb.BatchStatus IN (2, 3, 4)
      AND pbs.StageStatus IN (1, 2, 3, 4)
),
StageProgress AS
(
    SELECT
        pbs.BatchID,
        SUM(CASE WHEN pbs.StageStatus = 3 THEN 1 ELSE 0 END) AS CompletedStages,
        COUNT(*) AS TotalStages
    FROM dbo.ProductionBatchStages pbs
    INNER JOIN dbo.ProductionBatches pb ON pbs.BatchID = pb.BatchID
    WHERE pb.ProductionOrderID = @ProductionOrderID
      AND pb.BatchStatus IN (2, 3, 4)
    GROUP BY pbs.BatchID
)
SELECT
    bb.BatchID,
    bb.BatchNumber,
    bb.BatchStatus,
    bb.BatchStatusName,
    ISNULL(cs.CurrentStageName, N'') AS CurrentStageName,
    ISNULL(cs.CurrentStageStatus, 0) AS CurrentStageStatus,
    ISNULL(cs.CurrentStageStatusName, N'') AS CurrentStageStatusName,
    cs.StageStartTime,
    ISNULL(cs.ElapsedMinutes, 0) AS ElapsedMinutes,
    ISNULL(cs.OperatorName, N'') AS OperatorName,
    ISNULL(cs.Temperature, N'') AS Temperature,
    ISNULL(cs.Speed, N'') AS Speed,
    ISNULL(sp.CompletedStages, 0) AS CompletedStages,
    ISNULL(sp.TotalStages, 0) AS TotalStages
FROM BatchBase bb
LEFT JOIN CurrentStagePerBatch cs
    ON bb.BatchID = cs.BatchID
   AND cs.RN = 1
LEFT JOIN StageProgress sp
    ON bb.BatchID = sp.BatchID
ORDER BY bb.BatchID DESC;";

            var result = await connection.QueryAsync<CurrentProductionBatchDto>(sql, new
            {
                ProductionOrderID = productionOrderId
            });

            return result.ToList();
        }
        // ==========================================
        // مراحل الباتشات النشطة - للخط الزمني
        // ==========================================
        public async Task<List<StageTimelineItemDto>> GetActiveBatchesStagesAsync(List<int> batchIds)
        {
            if (batchIds is null || !batchIds.Any())
                return new List<StageTimelineItemDto>();

            using var connection = CreateConnection();

            var sql = @"
SELECT
    pb.ProductionOrderID,
    pbs.BatchID,
    pbs.StageOrder,
    ps.StageNameAr AS StageName,
    pbs.StageStatus,
    CASE pbs.StageStatus
        WHEN 1 THEN N'في الانتظار'
        WHEN 2 THEN N'قيد التنفيذ'
        WHEN 3 THEN N'مكتمل'
        WHEN 4 THEN N'مشكلة'
        ELSE N''
    END AS StageStatusName,
    pbs.StartTime,
    pbs.EndTime,
    CASE
        WHEN pbs.StartTime IS NOT NULL AND pbs.EndTime IS NOT NULL
            THEN DATEDIFF(MINUTE, pbs.StartTime, pbs.EndTime)
        WHEN pbs.StartTime IS NOT NULL AND pbs.StageStatus = 2
            THEN DATEDIFF(MINUTE, pbs.StartTime, GETDATE())
        ELSE NULL
    END AS DurationMinutes,
    ISNULL(e.FullNameAr, N'') AS OperatorName
FROM dbo.ProductionBatchStages pbs
INNER JOIN dbo.ProductionBatches pb ON pbs.BatchID = pb.BatchID
INNER JOIN dbo.ProductionStages ps ON pbs.StageID = ps.StageID
LEFT JOIN dbo.Employees e ON pbs.OperatorID = e.EmployeeID
WHERE pbs.BatchID IN @BatchIDs
  AND pb.BatchStatus IN (2, 3, 4)
ORDER BY pb.ProductionOrderID, pbs.StageOrder";

            var result = await connection.QueryAsync<StageTimelineItemDto>(sql, new
            {
                BatchIDs = batchIds
            });

            return result.ToList();
        }

        public class GlobalSearchResultDto
        {
            public string Type { get; set; } = "";      // item/customer/supplier/employee/user/document
            public string TypeLabel { get; set; } = "";
            public string Title { get; set; } = "";
            public string? SubTitle { get; set; }
            public string Route { get; set; } = "";
        }

        public async Task<List<GlobalSearchResultDto>> GlobalSearchAsync(string term, bool items, bool customers, bool suppliers, bool employees, bool users, bool invoices, bool pos, bool prs)
        {
            var results = new List<GlobalSearchResultDto>();
            var t = (term ?? "").Trim();
            if (t.Length < 2) return results;

            using var connection = CreateConnection();
            var p = "%" + t + "%";

            async Task QueryAsync(string type, string typeLabel, string sub, string routeTpl, string sql)
            {
                try
                {
                    var rows = await connection.QueryAsync<SearchRowDto>(sql, new { p });
                    foreach (var r in rows)
                    {
                        if (string.IsNullOrWhiteSpace(r.T1)) continue;
                        results.Add(new GlobalSearchResultDto
                        {
                            Type = type,
                            TypeLabel = typeLabel,
                            Title = r.T1,
                            SubTitle = string.IsNullOrWhiteSpace(r.T2) ? sub : $"{r.T2} — {sub}",
                            Route = routeTpl.Replace("{id}", r.Id.ToString())
                        });
                    }
                }
                catch { /* الجدول أو العمود غير موجود → نتجاهل هذا النوع فقط */ }
            }

            var tasks = new List<Task>();

            if (items)
                tasks.Add(QueryAsync("item", "صنف", "الأصناف", "/items/edit/{id}",
                    @"SELECT TOP 5 ItemID AS Id, ItemNameAr AS T1, ItemCode AS T2 FROM dbo.Items
                      WHERE IsActive = 1 AND (ItemNameAr LIKE @p OR ItemCode LIKE @p) ORDER BY ItemNameAr"));

            if (customers)
                tasks.Add(QueryAsync("customer", "عميل", "العملاء", "/customers/view/{id}",
                    @"SELECT TOP 5 CustomerID AS Id, CustomerNameAr AS T1, CustomerCode AS T2 FROM dbo.Customers
                      WHERE IsActive = 1 AND (CustomerNameAr LIKE @p OR CustomerCode LIKE @p) ORDER BY CustomerNameAr"));

            if (suppliers)
                tasks.Add(QueryAsync("supplier", "مورد", "الموردين", "/suppliers/edit/{id}",
                    @"SELECT TOP 5 SupplierID AS Id, SupplierNameAr AS T1, SupplierCode AS T2 FROM dbo.Suppliers
                      WHERE IsActive = 1 AND (SupplierNameAr LIKE @p OR SupplierCode LIKE @p) ORDER BY SupplierNameAr"));

            if (employees)
                tasks.Add(QueryAsync("employee", "موظف", "الموظفين", "/employees/view/{id}",
                    @"SELECT TOP 5 e.EmployeeID AS Id, e.FullNameAr AS T1,
                             ISNULL(d.DepartmentNameAr, e.EmployeeCode) AS T2
                      FROM dbo.Employees e
                      LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                      WHERE e.IsActive = 1 AND (e.FullNameAr LIKE @p OR e.EmployeeCode LIKE @p) ORDER BY e.FullNameAr"));

            if (users)
                tasks.Add(QueryAsync("user", "مستخدم", "المستخدمين", "/users/edit/{id}",
                    @"SELECT TOP 5 UserID AS Id, FullName AS T1, Username AS T2 FROM dbo.SystemUsers
                      WHERE IsActive = 1 AND (FullName LIKE @p OR Username LIKE @p) ORDER BY FullName"));

            if (invoices)
                tasks.Add(QueryAsync("document", "فاتورة شراء", "المشتريات", "/purchase-invoices/view/{id}",
                    @"SELECT TOP 5 pi.InvoiceID AS Id, pi.InvoiceNumber AS T1, s.SupplierNameAr AS T2 FROM dbo.PurchaseInvoices pi
                      LEFT JOIN dbo.Suppliers s ON pi.SupplierID = s.SupplierID
                      WHERE pi.InvoiceNumber LIKE @p ORDER BY pi.InvoiceID DESC"));

            if (pos)
                tasks.Add(QueryAsync("document", "أمر شراء", "المشتريات", "/purchase-orders/view/{id}",
                    @"SELECT TOP 5 po.PurchaseOrderID AS Id, po.PONumber AS T1, s.SupplierNameAr AS T2 FROM dbo.PurchaseOrders po
                      LEFT JOIN dbo.Suppliers s ON po.SupplierID = s.SupplierID
                      WHERE po.PONumber LIKE @p ORDER BY po.PurchaseOrderID DESC"));

            if (prs)
                tasks.Add(QueryAsync("document", "طلب شراء", "المشتريات", "/purchase-requests/view/{id}",
                    @"SELECT TOP 5 pr.RequestID AS Id, pr.RequestNumber AS T1, e.FullNameAr AS T2 FROM dbo.PurchaseRequests pr
                      LEFT JOIN dbo.Employees e ON pr.RequestedBy = e.EmployeeID
                      WHERE pr.RequestNumber LIKE @p ORDER BY pr.RequestID DESC"));

            await Task.WhenAll(tasks);
            return results.Take(24).ToList();
        }

        private class SearchRowDto
        {
            public int Id { get; set; }
            public string? T1 { get; set; }
            public string? T2 { get; set; }
        }
    }
}