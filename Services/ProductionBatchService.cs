using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public class ProductionBatchService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notification;

        private sealed class BatchHeadDbDto
        {
            public int BatchID { get; set; }
            public string BatchNumber { get; set; } = "";
            public int ProductionOrderID { get; set; }
            public int BatchStatus { get; set; }
        }
        private sealed class BatchSuggestedQtyDto
{
    public decimal PlannedQty { get; set; }
    public decimal BatchSize { get; set; }
}

private sealed class BatchInventoryDbDto
{
    public int BatchID { get; set; }
    public string BatchNumber { get; set; } = "";
    public int ProductionOrderID { get; set; }
    public int ProductItemID { get; set; }
    public int BatchStatus { get; set; }
    public int? TargetWarehouseID { get; set; }
    public decimal GoodQty { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

private sealed class BatchStageDbDto
{
    public int BatchStageID { get; set; }
    public int BatchID { get; set; }
    public DateTime? StartTime { get; set; }
    public int StageStatus { get; set; }
}

private sealed class BatchQcDbDto
{
    public int BatchID { get; set; }
    public string BatchNumber { get; set; } = "";
    public int ProductionOrderID { get; set; }
    public int BatchStatus { get; set; }
    public decimal ProducedQty { get; set; }
    public decimal GoodQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal SampleQty { get; set; }
    public decimal WasteQty { get; set; }
}

        public ProductionBatchService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notification)
            : base(configuration)
        {
            _audit = audit;
            _notification = notification;
        }

        // ==========================================
        // قائمة الدفعات
        // ==========================================
        public async Task<List<ProductionBatchListDto>> GetListAsync(
            string? search = null,
            int? statusFilter = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    pb.BatchID,
    pb.BatchNumber,
    pb.BatchDate,
    pb.ExpiryDate,
    pb.ProductionOrderID,
    po.OrderNumber AS ProductionOrderNumber,
    pb.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    pb.PlannedQty,
    ISNULL(pb.ProducedQty, 0) AS ProducedQty,
    ISNULL(pb.GoodQty, 0) AS GoodQty,
    ISNULL(pb.RejectedQty, 0) AS RejectedQty,
    ISNULL(pb.WasteQty, 0) AS WasteQty,
    ISNULL(pb.SampleQty, 0) AS SampleQty,
    pb.UnitID,
    u.UnitNameAr AS UnitName,
    pb.TargetWarehouseID,
    ISNULL(w.WarehouseNameAr, N'') AS TargetWarehouse,
    ISNULL(pb.MaterialCost, 0) AS MaterialCost,
    ISNULL(pb.LaborCost, 0) AS LaborCost,
    ISNULL(pb.OverheadCost, 0) AS OverheadCost,
    ISNULL(pb.TotalCost, 0) AS TotalCost,
    ISNULL(pb.UnitCost, 0) AS UnitCost,
    pb.BatchStatus,
    CASE pb.BatchStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'قيد التصنيع'
        WHEN 3 THEN N'اكتمل التصنيع'
        WHEN 4 THEN N'في فحص الجودة'
        WHEN 5 THEN N'مقبول'
        WHEN 6 THEN N'مرفوض'
    END AS StatusName,
    pb.QCResult,
    CASE pb.QCResult
        WHEN 1 THEN N'ناجح'
        WHEN 2 THEN N'فاشل'
        WHEN 3 THEN N'ناجح بتحفظ'
        ELSE N''
    END AS QCResultName,
    CASE
        WHEN ISNULL(pb.ProducedQty, 0) > 0
            THEN CAST(ISNULL(pb.WasteQty, 0) * 100.0 / pb.ProducedQty AS DECIMAL(5,2))
        ELSE 0
    END AS WastePercent
FROM dbo.ProductionBatches pb
INNER JOIN dbo.ProductionOrders po ON pb.ProductionOrderID = po.ProductionOrderID
INNER JOIN dbo.Items i ON pb.ProductItemID = i.ItemID
INNER JOIN dbo.Units u ON pb.UnitID = u.UnitID
LEFT JOIN dbo.Warehouses w ON pb.TargetWarehouseID = w.WarehouseID
WHERE 1 = 1
    AND (@StatusFilter IS NULL OR pb.BatchStatus = @StatusFilter)
    AND (@FromDate IS NULL OR pb.BatchDate >= @FromDate)
    AND (@ToDate IS NULL OR pb.BatchDate <= @ToDate)
    AND (
        @Search IS NULL OR @Search = N'' OR
        pb.BatchNumber LIKE N'%' + @Search + N'%' OR
        po.OrderNumber LIKE N'%' + @Search + N'%' OR
        i.ItemCode LIKE N'%' + @Search + N'%' OR
        i.ItemNameAr LIKE N'%' + @Search + N'%'
    )
ORDER BY pb.BatchDate DESC, pb.BatchID DESC";

            var result = await connection.QueryAsync<ProductionBatchListDto>(sql, new
            {
                Search = search,
                StatusFilter = statusFilter,
                FromDate = fromDate,
                ToDate = toDate
            });

            return result.ToList();
        }

        // ==========================================
        // جلب باتش واحد
        // ==========================================
        public async Task<ProductionBatchEditDto?> GetByIdAsync(int batchId)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    pb.BatchID,
    pb.BatchNumber,
    pb.ProductionOrderID,
    po.OrderNumber AS ProductionOrderNumber,
    pb.ProductItemID,
    i.ItemNameAr AS ProductName,
    pb.BatchDate,
    pb.ExpiryDate,
    pb.PlannedQty,
    ISNULL(pb.ProducedQty, 0) AS ProducedQty,
    ISNULL(pb.GoodQty, 0) AS GoodQty,
    ISNULL(pb.RejectedQty, 0) AS RejectedQty,
    ISNULL(pb.WasteQty, 0) AS WasteQty,
    ISNULL(pb.SampleQty, 0) AS SampleQty,
    pb.UnitID,
    u.UnitNameAr AS UnitName,
    pb.TargetWarehouseID,
    ISNULL(w.WarehouseNameAr, N'') AS TargetWarehouseName,
    ISNULL(pb.MaterialCost, 0) AS MaterialCost,
    ISNULL(pb.LaborCost, 0) AS LaborCost,
    ISNULL(pb.OverheadCost, 0) AS OverheadCost,
    ISNULL(pb.TotalCost, 0) AS TotalCost,
    ISNULL(pb.UnitCost, 0) AS UnitCost,
    pb.BatchStatus,
    pb.QCInspectedBy,
    pb.QCInspectionDate,
    pb.QCResult,
    pb.QCNotes,
    pb.StartTime,
    pb.EndTime,
    pb.ProducedBy,
    pb.SupervisorID,
    pb.Notes
FROM dbo.ProductionBatches pb
INNER JOIN dbo.ProductionOrders po ON pb.ProductionOrderID = po.ProductionOrderID
INNER JOIN dbo.Items i ON pb.ProductItemID = i.ItemID
INNER JOIN dbo.Units u ON pb.UnitID = u.UnitID
LEFT JOIN dbo.Warehouses w ON pb.TargetWarehouseID = w.WarehouseID
WHERE pb.BatchID = @BatchID";

            return await connection.QueryFirstOrDefaultAsync<ProductionBatchEditDto>(
                sql, new { BatchID = batchId });
        }

        // ==========================================
        // مراحل الباتش
        // ==========================================
        public async Task<List<ProductionBatchStageDto>> GetStagesAsync(int batchId)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    s.BatchStageID,
    s.BatchID,
    s.StageID,
    ps.StageNameAr AS StageName,
    s.StageOrder,
    s.StartTime,
    s.EndTime,
    s.Duration,
    s.Temperature,
    s.Speed,
    s.Humidity,
    s.OperatorID,
    ISNULL(e.FullNameAr, N'') AS OperatorName,
    s.StageStatus,
    CASE s.StageStatus
        WHEN 1 THEN N'في الانتظار'
        WHEN 2 THEN N'قيد التنفيذ'
        WHEN 3 THEN N'مكتمل'
        WHEN 4 THEN N'مشكلة'
    END AS StageStatusName,
    ISNULL(s.QualityCheck, 0) AS QualityCheck,
    s.QualityResult,
    s.Notes
FROM dbo.ProductionBatchStages s
INNER JOIN dbo.ProductionStages ps ON s.StageID = ps.StageID
LEFT JOIN dbo.Employees e ON s.OperatorID = e.EmployeeID
WHERE s.BatchID = @BatchID
ORDER BY s.StageOrder";

            var result = await connection.QueryAsync<ProductionBatchStageDto>(
                sql, new { BatchID = batchId });

            return result.ToList();
        }

        // ==========================================
        // معلومات النظام
        // ==========================================
        public async Task<ProductionBatchAuditDto?> GetAuditAsync(int batchId)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    ISNULL(uc.FullName, N'غير محدد') AS CreatedByName,
    pb.CreatedDate,
    ISNULL(um.FullName, N'') AS ModifiedByName,
    pb.ModifiedDate,
    ISNULL(ep.FullNameAr, N'') AS ProducedByName,
    ISNULL(es.FullNameAr, N'') AS SupervisorName,
    ISNULL(eq.FullNameAr, N'') AS QCInspectedByName,
    pb.QCInspectionDate
FROM dbo.ProductionBatches pb
LEFT JOIN dbo.SystemUsers uc ON pb.CreatedBy = uc.UserID
LEFT JOIN dbo.SystemUsers um ON pb.ModifiedBy = um.UserID
LEFT JOIN dbo.Employees ep ON pb.ProducedBy = ep.EmployeeID
LEFT JOIN dbo.Employees es ON pb.SupervisorID = es.EmployeeID
LEFT JOIN dbo.Employees eq ON pb.QCInspectedBy = eq.EmployeeID
WHERE pb.BatchID = @BatchID";

            return await connection.QueryFirstOrDefaultAsync<ProductionBatchAuditDto>(
                sql, new { BatchID = batchId });
        }

        // ==========================================
        // بيانات أمر التصنيع
        // ==========================================
        public async Task<ProductionBatchOrderInfoDto?> GetOrderInfoAsync(int productionOrderId)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    po.ProductionOrderID,
    po.OrderNumber,
    po.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    po.PlannedQty,
    po.PlannedUnitID,
    u.UnitNameAr AS UnitName,
    ISNULL(po.NumberOfBatches, 1) AS NumberOfBatches,
    po.TargetWarehouseID,
    ISNULL(w.WarehouseNameAr, N'') AS TargetWarehouseName
FROM dbo.ProductionOrders po
INNER JOIN dbo.Items i ON po.ProductItemID = i.ItemID
INNER JOIN dbo.Units u ON po.PlannedUnitID = u.UnitID
LEFT JOIN dbo.Warehouses w ON po.TargetWarehouseID = w.WarehouseID
WHERE po.ProductionOrderID = @ProductionOrderID";

            return await connection.QueryFirstOrDefaultAsync<ProductionBatchOrderInfoDto>(
                sql, new { ProductionOrderID = productionOrderId });
        }

        // ==========================================
        // إضافة باتش
        // ==========================================
        public async Task<int> InsertAsync(ProductionBatchEditDto batch, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var batchNumber = await GenerateBatchNumberAsync(connection, transaction);
                var totalCost = batch.MaterialCost + batch.LaborCost + batch.OverheadCost;

                var sql = @"
INSERT INTO dbo.ProductionBatches
(
    BatchNumber, ProductionOrderID, ProductItemID,
    BatchDate, ExpiryDate,
    PlannedQty, ProducedQty, GoodQty, RejectedQty, WasteQty, SampleQty,
    UnitID, TargetWarehouseID,
    MaterialCost, LaborCost, OverheadCost, TotalCost,
    BatchStatus, StartTime, EndTime,
    ProducedBy, SupervisorID,
    QCInspectedBy, QCInspectionDate, QCResult, QCNotes,
    Notes, CreatedBy, CreatedDate
)
VALUES
(
    @BatchNumber, @ProductionOrderID, @ProductItemID,
    @BatchDate, @ExpiryDate,
    @PlannedQty, @ProducedQty, @GoodQty, @RejectedQty, @WasteQty, @SampleQty,
    @UnitID, @TargetWarehouseID,
    @MaterialCost, @LaborCost, @OverheadCost, @TotalCost,
    1, @StartTime, @EndTime,
    NULLIF(@ProducedBy, 0), NULLIF(@SupervisorID, 0),
    NULLIF(@QCInspectedBy, 0), @QCInspectionDate, @QCResult, @QCNotes,
    @Notes, @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    BatchNumber = batchNumber,
                    batch.ProductionOrderID,
                    batch.ProductItemID,
                    batch.BatchDate,
                    batch.ExpiryDate,
                    batch.PlannedQty,
                    batch.ProducedQty,
                    batch.GoodQty,
                    batch.RejectedQty,
                    batch.WasteQty,
                    batch.SampleQty,
                    batch.UnitID,
                    batch.TargetWarehouseID,
                    batch.MaterialCost,
                    batch.LaborCost,
                    batch.OverheadCost,
                    TotalCost = totalCost,
                    batch.StartTime,
                    batch.EndTime,
                    batch.ProducedBy,
                    batch.SupervisorID,
                    batch.QCInspectedBy,
                    batch.QCInspectionDate,
                    batch.QCResult,
                    batch.QCNotes,
                    batch.Notes,
                    UserID = userId
                }, transaction);

                await CreateDefaultStagesAsync(connection, transaction, newId, batch.ProductionOrderID);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 1,
                    tableName: "ProductionBatches",
                    recordId: newId.ToString(),
                    moduleName: "SCR_BATCH",
                    description: $"إنشاء دفعة إنتاج جديدة: {batchNumber}"
                );

                await _notification.CreateNotificationAsync(
                    notificationType: 1,
                    title: "دفعة إنتاج جديدة",
                    message: $"تم إنشاء دفعة إنتاج جديدة رقم {batchNumber}",
                    priority: 2,
                    targetRoleId: 3,
                    relatedModule: "SCR_BATCH",
                    relatedRecordId: newId,
                    createdBy: userId
                );

                return newId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // تعديل باتش
        // ==========================================
        public async Task UpdateAsync(ProductionBatchEditDto batch, int userId)
        {
            using var connection = CreateConnection();

            var currentStatus = await connection.ExecuteScalarAsync<int?>(
                @"SELECT BatchStatus
                  FROM dbo.ProductionBatches
                  WHERE BatchID = @BatchID",
                new { batch.BatchID });

            if (!currentStatus.HasValue)
                throw new Exception("دفعة الإنتاج غير موجودة");

            if (currentStatus.Value == 5 || currentStatus.Value == 6)
                throw new Exception("لا يمكن تعديل دفعة نهائية");

            var sql = @"
UPDATE dbo.ProductionBatches SET
    BatchDate = @BatchDate,
    ExpiryDate = @ExpiryDate,
    PlannedQty = @PlannedQty,
    ProducedQty = @ProducedQty,
    GoodQty = @GoodQty,
    RejectedQty = @RejectedQty,
    WasteQty = @WasteQty,
    SampleQty = @SampleQty,
    UnitID = @UnitID,
    TargetWarehouseID = @TargetWarehouseID,
    MaterialCost = @MaterialCost,
    LaborCost = @LaborCost,
    OverheadCost = @OverheadCost,
    TotalCost = @TotalCost,
    QCInspectedBy = NULLIF(@QCInspectedBy, 0),
    QCInspectionDate = @QCInspectionDate,
    QCResult = @QCResult,
    QCNotes = @QCNotes,
    StartTime = @StartTime,
    EndTime = @EndTime,
    ProducedBy = NULLIF(@ProducedBy, 0),
    SupervisorID = NULLIF(@SupervisorID, 0),
    Notes = @Notes,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE BatchID = @BatchID";

            await connection.ExecuteAsync(sql, new
            {
                batch.BatchID,
                batch.BatchDate,
                batch.ExpiryDate,
                batch.PlannedQty,
                batch.ProducedQty,
                batch.GoodQty,
                batch.RejectedQty,
                batch.WasteQty,
                batch.SampleQty,
                batch.UnitID,
                batch.TargetWarehouseID,
                batch.MaterialCost,
                batch.LaborCost,
                batch.OverheadCost,
                TotalCost = batch.MaterialCost + batch.LaborCost + batch.OverheadCost,
                batch.QCInspectedBy,
                batch.QCInspectionDate,
                batch.QCResult,
                batch.QCNotes,
                batch.StartTime,
                batch.EndTime,
                batch.ProducedBy,
                batch.SupervisorID,
                batch.Notes,
                UserID = userId
            });

            await SyncOrderSummaryFromBatchesAsync(batch.ProductionOrderID, userId);

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "ProductionBatches",
                recordId: batch.BatchID.ToString(),
                moduleName: "SCR_BATCH",
                description: $"تعديل دفعة إنتاج: {batch.BatchNumber}"
            );
        }

        // ==========================================
        // تغيير حالة الباتش
        // ==========================================
        public async Task<(bool Success, string Message)> ChangeStatusAsync(int batchId, int newStatus, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(
                    @"SELECT BatchID, BatchNumber, ProductionOrderID, BatchStatus
                      FROM dbo.ProductionBatches
                      WHERE BatchID = @BatchID",
                    new { BatchID = batchId }, transaction);

                if (current is null)
                    return (false, "دفعة الإنتاج غير موجودة");

                var allowed = current.BatchStatus switch
                {
                    1 => new[] { 2 },
                    2 => new[] { 3 },
                    3 => new[] { 4 },
                    4 => new[] { 5, 6 },
                    _ => Array.Empty<int>()
                };

                if (!allowed.Contains(newStatus))
                    return (false, "لا يمكن الانتقال لهذه الحالة");

                await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches SET
    BatchStatus = @NewStatus,
    StartTime = CASE WHEN @NewStatus = 2 AND StartTime IS NULL THEN GETDATE() ELSE StartTime END,
    EndTime = CASE WHEN @NewStatus = 3 AND EndTime IS NULL THEN GETDATE() ELSE EndTime END,
    QCInspectionDate = CASE WHEN @NewStatus IN (5,6) AND QCInspectionDate IS NULL THEN GETDATE() ELSE QCInspectionDate END,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE BatchID = @BatchID",
                    new
                    {
                        BatchID = batchId,
                        NewStatus = newStatus,
                        UserID = userId
                    }, transaction);

                await SyncOrderSummaryFromBatchesAsync(connection, transaction, current.ProductionOrderID, userId);

                transaction.Commit();

                var statusName = GetBatchStatusName(newStatus);

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "ProductionBatches",
                    recordId: batchId.ToString(),
                    changedColumns: "BatchStatus",
                    moduleName: "SCR_BATCH",
                    description: $"تغيير حالة الدفعة {current.BatchNumber} إلى {statusName}"
                );

                await _notification.CreateNotificationAsync(
                    notificationType: 3,
                    title: $"تحديث دفعة إنتاج #{current.BatchNumber}",
                    message: $"تم تحويل حالة دفعة الإنتاج إلى: {statusName}",
                    priority: 2,
                    targetRoleId: 3,
                    relatedModule: "SCR_BATCH",
                    relatedRecordId: batchId,
                    createdBy: userId
                );

                return (true, $"تم تحويل الحالة إلى [{statusName}] بنجاح");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, ex.Message);
            }
        }

        // ==========================================
        // Lookups
        // ==========================================
        public async Task<List<ProductionLookupDto>> GetProductionOrdersLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    po.ProductionOrderID AS Id,
    (po.OrderNumber + N' - ' + i.ItemNameAr) AS Name
FROM dbo.ProductionOrders po
INNER JOIN dbo.Items i ON po.ProductItemID = i.ItemID
WHERE po.OrderStatus IN (2,3,4,5)
ORDER BY po.OrderDate DESC";

            return (await connection.QueryAsync<ProductionLookupDto>(sql)).ToList();
        }

        public async Task<List<ProductionLookupDto>> GetWarehousesLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    WarehouseID AS Id,
    (WarehouseCode + N' - ' + WarehouseNameAr) AS Name
FROM dbo.Warehouses
WHERE IsActive = 1
ORDER BY WarehouseNameAr";

            return (await connection.QueryAsync<ProductionLookupDto>(sql)).ToList();
        }

        public async Task<List<ProductionLookupDto>> GetEmployeesLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    EmployeeID AS Id,
    FullNameAr AS Name
FROM dbo.Employees
WHERE IsActive = 1
ORDER BY FullNameAr";

            return (await connection.QueryAsync<ProductionLookupDto>(sql)).ToList();
        }

        // ==========================================
        // إنشاء مراحل افتراضية
        // ==========================================
        private async Task CreateDefaultStagesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int batchId,
            int productionOrderId)
        {
            await connection.ExecuteAsync(@"
INSERT INTO dbo.ProductionBatchStages
(
    BatchID, StageID, StageOrder,
    Temperature, Speed, StageStatus, Notes
)
SELECT
    @BatchID,
    bs.StageID,
    bs.StageOrder,
    bs.Temperature,
    bs.Speed,
    1,
    bs.Notes
FROM dbo.ProductionOrders po
INNER JOIN dbo.BOMStages bs ON po.BOMID = bs.BOMID
WHERE po.ProductionOrderID = @ProductionOrderID
ORDER BY bs.StageOrder",
                new
                {
                    BatchID = batchId,
                    ProductionOrderID = productionOrderId
                }, transaction);
        }

        // ==========================================
        // مزامنة ملخص أمر التصنيع
        // ==========================================
        private async Task SyncOrderSummaryFromBatchesAsync(int productionOrderId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                await SyncOrderSummaryFromBatchesAsync(connection, transaction, productionOrderId, userId);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private async Task SyncOrderSummaryFromBatchesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int productionOrderId,
            int userId)
        {
            await connection.ExecuteAsync(@"
UPDATE po
SET
    ActualQty = ISNULL(x.AcceptedQty, 0),
    WasteQty = ISNULL(x.TotalWaste, 0),
    ActualMaterialCost = ISNULL(x.MaterialCost, 0),
    ActualLaborCost = ISNULL(x.LaborCost, 0),
    ActualOverheadCost = ISNULL(x.OverheadCost, 0),
    ActualTotalCost = ISNULL(x.TotalCost, 0),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
FROM dbo.ProductionOrders po
OUTER APPLY (
    SELECT
        SUM(CASE WHEN BatchStatus = 5 THEN ISNULL(GoodQty, 0) ELSE 0 END) AS AcceptedQty,
        SUM(ISNULL(WasteQty, 0) + ISNULL(RejectedQty, 0)) AS TotalWaste,
        SUM(ISNULL(MaterialCost, 0)) AS MaterialCost,
        SUM(ISNULL(LaborCost, 0)) AS LaborCost,
        SUM(ISNULL(OverheadCost, 0)) AS OverheadCost,
        SUM(ISNULL(TotalCost, 0)) AS TotalCost
    FROM dbo.ProductionBatches
    WHERE ProductionOrderID = @ProductionOrderID
) x
WHERE po.ProductionOrderID = @ProductionOrderID",
                new
                {
                    ProductionOrderID = productionOrderId,
                    UserID = userId
                }, transaction);

            var totalBatches = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.ProductionBatches WHERE ProductionOrderID = @ProductionOrderID",
                new { ProductionOrderID = productionOrderId }, transaction);

            var finalBatches = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.ProductionBatches
                  WHERE ProductionOrderID = @ProductionOrderID
                    AND BatchStatus IN (5,6)",
                new { ProductionOrderID = productionOrderId }, transaction);

            var hasQc = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.ProductionBatches
                  WHERE ProductionOrderID = @ProductionOrderID
                    AND BatchStatus = 4",
                new { ProductionOrderID = productionOrderId }, transaction);

            var hasManufacturing = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.ProductionBatches
                  WHERE ProductionOrderID = @ProductionOrderID
                    AND BatchStatus IN (2,3)",
                new { ProductionOrderID = productionOrderId }, transaction);

            int? newOrderStatus = null;

            if (totalBatches > 0 && finalBatches == totalBatches)
                newOrderStatus = 6;
            else if (hasQc > 0)
                newOrderStatus = 5;
            else if (hasManufacturing > 0)
                newOrderStatus = 4;

            if (newOrderStatus.HasValue)
            {
                await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders
SET OrderStatus = @OrderStatus,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ProductionOrderID = @ProductionOrderID",
                    new
                    {
                        ProductionOrderID = productionOrderId,
                        OrderStatus = newOrderStatus.Value,
                        UserID = userId
                    }, transaction);
            }
        }

        // ==========================================
        // توليد رقم الباتش
        // ==========================================
        private async Task<string> GenerateBatchNumberAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction)
        {
            try
            {
                // لو عندك SequenceCode مختلف غير "PB" عدله هنا فقط
                var sql = @"
DECLARE @NextNum NVARCHAR(50);
EXEC dbo.sp_GetNextNumber 'BT', @NextNum OUTPUT;
SELECT @NextNum;";

                var result = await connection.QueryFirstOrDefaultAsync<string>(
                    sql, transaction: transaction);

                return result ?? $"BT-{DateTime.Now:yyMMddHHmmss}";
            }
            catch
            {
                return $"PB-{DateTime.Now:yyMMddHHmmss}";
            }
        }

        private static string GetBatchStatusName(int status) => status switch
        {
            1 => "مسودة",
            2 => "قيد التصنيع",
            3 => "اكتمل التصنيع",
            4 => "في فحص الجودة",
            5 => "مقبول",
            6 => "مرفوض",
            _ => "غير محدد"
        };
        public async Task<decimal> GetSuggestedBatchQtyAsync(int productionOrderId, int excludeBatchId = 0)
{
    using var connection = CreateConnection();

    var source = await connection.QueryFirstOrDefaultAsync<BatchSuggestedQtyDto>(@"
SELECT
    po.PlannedQty,
    ISNULL(b.BatchSize, 0) AS BatchSize
FROM dbo.ProductionOrders po
INNER JOIN dbo.BillOfMaterials b ON po.BOMID = b.BOMID
WHERE po.ProductionOrderID = @ProductionOrderID",
        new { ProductionOrderID = productionOrderId });

    if (source is null)
        return 0;

    var previousQty = await connection.ExecuteScalarAsync<decimal?>(@"
SELECT ISNULL(SUM(PlannedQty), 0)
FROM dbo.ProductionBatches
WHERE ProductionOrderID = @ProductionOrderID
  AND BatchID <> @ExcludeBatchID",
        new
        {
            ProductionOrderID = productionOrderId,
            ExcludeBatchID = excludeBatchId
        }) ?? 0m;

    var remainingQty = source.PlannedQty - previousQty;
    if (remainingQty <= 0)
        return 0;

    if (source.BatchSize <= 0)
        return remainingQty;

    return remainingQty >= source.BatchSize
        ? source.BatchSize
        : remainingQty;
}
public async Task<(bool Success, string Message, int Count)> LoadStagesFromBomAsync(
    int batchId,
    int userId,
    bool replaceExisting = true)
{
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction();

    try
    {
        var batch = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(@"
SELECT BatchID, BatchNumber, ProductionOrderID, BatchStatus
FROM dbo.ProductionBatches
WHERE BatchID = @BatchID",
            new { BatchID = batchId }, transaction);

        if (batch is null)
            return (false, "دفعة الإنتاج غير موجودة", 0);

        if (batch.BatchStatus > 2)
            return (false, "لا يمكن تحميل المراحل بعد تخطي قيد التصنيع", 0);

        if (replaceExisting)
        {
            await connection.ExecuteAsync(
                @"DELETE FROM dbo.ProductionBatchStages WHERE BatchID = @BatchID",
                new { BatchID = batchId }, transaction);
        }

        var inserted = await connection.ExecuteAsync(@"
INSERT INTO dbo.ProductionBatchStages
(
    BatchID, StageID, StageOrder,
    Temperature, Speed, StageStatus, Notes
)
SELECT
    @BatchID,
    bs.StageID,
    bs.StageOrder,
    bs.Temperature,
    bs.Speed,
    1,
    bs.Notes
FROM dbo.ProductionOrders po
INNER JOIN dbo.BOMStages bs ON po.BOMID = bs.BOMID
WHERE po.ProductionOrderID = @ProductionOrderID
ORDER BY bs.StageOrder",
            new
            {
                BatchID = batchId,
                batch.ProductionOrderID
            }, transaction);

        transaction.Commit();

        await _audit.WriteAuditLogAsync(
            userId: userId,
            actionType: 2,
            tableName: "ProductionBatchStages",
            recordId: batchId.ToString(),
            moduleName: "SCR_BATCH",
            description: $"تحميل مراحل الباتش من الوصفة: {batch.BatchNumber}"
        );

        return (true, $"تم تحميل {inserted} مرحلة بنجاح", inserted);
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return (false, ex.Message, 0);
    }
}
public async Task<(bool Success, string Message)> StartProductionAsync(int batchId, int userId)
{
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction();

    try
    {
        var batch = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(@"
SELECT BatchID, BatchNumber, ProductionOrderID, BatchStatus
FROM dbo.ProductionBatches
WHERE BatchID = @BatchID",
            new { BatchID = batchId }, transaction);

        if (batch is null)
            return (false, "دفعة الإنتاج غير موجودة");

        if (batch.BatchStatus != 1)
            return (false, "لا يمكن بدء التصنيع إلا من حالة مسودة");

        await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus = 2,
    StartTime = GETDATE(),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE BatchID = @BatchID",
            new { BatchID = batchId, UserID = userId }, transaction);

        await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders
SET OrderStatus = CASE WHEN OrderStatus < 4 THEN 4 ELSE OrderStatus END,
    ActualStartDate = ISNULL(ActualStartDate, GETDATE()),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ProductionOrderID = @ProductionOrderID",
            new
            {
                batch.ProductionOrderID,
                UserID = userId
            }, transaction);

        transaction.Commit();

        await _audit.WriteAuditLogAsync(
            userId: userId,
            actionType: 2,
            tableName: "ProductionBatches",
            recordId: batchId.ToString(),
            changedColumns: "BatchStatus,StartTime",
            moduleName: "SCR_BATCH",
            description: $"بدء تصنيع دفعة: {batch.BatchNumber}"
        );

        await _notification.CreateNotificationAsync(
            notificationType: 1,
            title: "بدء التصنيع",
            message: $"تم بدء التصنيع لدفعة الإنتاج رقم {batch.BatchNumber}",
            priority: 2,
            targetRoleId: 3,
            relatedModule: "SCR_BATCH",
            relatedRecordId: batchId,
            createdBy: userId
        );

        return (true, "تم بدء التصنيع بنجاح");
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return (false, ex.Message);
    }
}
public async Task<(bool Success, string Message)> CompleteProductionAsync(int batchId, int userId)
{
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction();

    try
    {
        var batch = await connection.QueryFirstOrDefaultAsync<BatchQcDbDto>(@"
SELECT
    BatchID,
    BatchNumber,
    ProductionOrderID,
    BatchStatus,
    ISNULL(ProducedQty, 0) AS ProducedQty,
    ISNULL(GoodQty, 0) AS GoodQty,
    ISNULL(RejectedQty, 0) AS RejectedQty,
    ISNULL(SampleQty, 0) AS SampleQty,
    ISNULL(WasteQty, 0) AS WasteQty
FROM dbo.ProductionBatches
WHERE BatchID = @BatchID",
            new { BatchID = batchId }, transaction);

        if (batch is null)
            return (false, "دفعة الإنتاج غير موجودة");

        if (batch.BatchStatus != 2)
            return (false, "لا يمكن تسجيل اكتمال التصنيع إلا من حالة قيد التصنيع");

        if (batch.ProducedQty <= 0)
            return (false, "يجب إدخال الكمية المنتجة أولاً");

        await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus = 3,
    EndTime = GETDATE(),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE BatchID = @BatchID",
            new { BatchID = batchId, UserID = userId }, transaction);

        await RecalculateMaterialCostAsync(connection, transaction, batchId);

        transaction.Commit();

        await _audit.WriteAuditLogAsync(
            userId: userId,
            actionType: 2,
            tableName: "ProductionBatches",
            recordId: batchId.ToString(),
            changedColumns: "BatchStatus,EndTime,MaterialCost,TotalCost",
            moduleName: "SCR_BATCH",
            description: $"اكتمال تصنيع دفعة: {batch.BatchNumber}"
        );

        await _notification.CreateNotificationAsync(
            notificationType: 1,
            title: "اكتمل التصنيع",
            message: $"تم تسجيل اكتمال التصنيع لدفعة رقم {batch.BatchNumber}",
            priority: 2,
            targetRoleId: 3,
            relatedModule: "SCR_BATCH",
            relatedRecordId: batchId,
            createdBy: userId
        );

        return (true, "تم تسجيل اكتمال التصنيع بنجاح");
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return (false, ex.Message);
    }
}
public async Task<(bool Success, string Message)> ApplyQcAsync(
    int batchId,
    int qcResult,
    int qcInspectedBy,
    string? qcNotes,
    decimal goodQty,
    decimal rejectedQty,
    decimal sampleQty,
    int userId)
{
    using var connection = CreateConnection();

    var batch = await connection.QueryFirstOrDefaultAsync<BatchQcDbDto>(@"
SELECT
    BatchID,
    BatchNumber,
    ProductionOrderID,
    BatchStatus,
    ISNULL(ProducedQty, 0) AS ProducedQty,
    ISNULL(GoodQty, 0) AS GoodQty,
    ISNULL(RejectedQty, 0) AS RejectedQty,
    ISNULL(SampleQty, 0) AS SampleQty,
    ISNULL(WasteQty, 0) AS WasteQty
FROM dbo.ProductionBatches
WHERE BatchID = @BatchID",
        new { BatchID = batchId });

    if (batch is null)
        return (false, "دفعة الإنتاج غير موجودة");

    if (batch.BatchStatus < 3 || batch.BatchStatus > 4)
        return (false, "يجب أن تكون الدفعة مكتملة التصنيع أو في فحص الجودة");

    if (goodQty <= 0)
        return (false, "يجب إدخال الكمية الصالحة");

    if (qcInspectedBy <= 0)
        return (false, "يجب اختيار مفتش الجودة");

    if (qcResult is < 1 or > 3)
        return (false, "يجب اختيار نتيجة فحص الجودة");

    var total = goodQty + rejectedQty + sampleQty;
    if (total > batch.ProducedQty)
        return (false, "مجموع الصالح + المرفوض + العينات أكبر من الكمية المنتجة");

    var wasteQty = batch.ProducedQty - total;
    if (wasteQty < 0)
        wasteQty = 0;

    var newStatus = qcResult == 2 ? 6 : 5;

    await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus = @BatchStatus,
    GoodQty = @GoodQty,
    RejectedQty = @RejectedQty,
    SampleQty = @SampleQty,
    WasteQty = @WasteQty,
    QCInspectedBy = @QCInspectedBy,
    QCInspectionDate = GETDATE(),
    QCResult = @QCResult,
    QCNotes = @QCNotes,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE BatchID = @BatchID",
        new
        {
            BatchID = batchId,
            BatchStatus = newStatus,
            GoodQty = goodQty,
            RejectedQty = rejectedQty,
            SampleQty = sampleQty,
            WasteQty = wasteQty,
            QCInspectedBy = qcInspectedBy,
            QCResult = qcResult,
            QCNotes = qcNotes,
            UserID = userId
        });

    await _audit.WriteAuditLogAsync(
        userId: userId,
        actionType: 2,
        tableName: "ProductionBatches",
        recordId: batchId.ToString(),
        changedColumns: "BatchStatus,QCResult,QCInspectionDate,QCInspectedBy,GoodQty,RejectedQty,SampleQty,WasteQty",
        moduleName: "SCR_BATCH",
        description: $"فحص جودة دفعة: {batch.BatchNumber}"
    );

    await _notification.CreateNotificationAsync(
        notificationType: 1,
        title: "نتيجة فحص الجودة",
        message: $"تم تسجيل نتيجة الجودة للدفعة {batch.BatchNumber}",
        priority: 1,
        targetRoleId: 3,
        relatedModule: "SCR_BATCH",
        relatedRecordId: batchId,
        createdBy: userId
    );

    return (true, "تم تسجيل نتيجة فحص الجودة بنجاح");
}
public async Task<bool> HasInventoryEntryAsync(string batchNumber)
{
    using var connection = CreateConnection();

    var count = await connection.ExecuteScalarAsync<int>(
        @"SELECT COUNT(*)
          FROM dbo.InventoryBatchBalance
          WHERE BatchNumber = @BatchNumber",
        new { BatchNumber = batchNumber });

    return count > 0;
}
public async Task<(bool Success, string Message)> AddToInventoryAsync(int batchId, int userId)
{
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction();

    try
    {
        var batch = await connection.QueryFirstOrDefaultAsync<BatchInventoryDbDto>(@"
SELECT
    BatchID,
    BatchNumber,
    ProductionOrderID,
    ProductItemID,
    BatchStatus,
    TargetWarehouseID,
    ISNULL(GoodQty, 0) AS GoodQty,
    ISNULL(UnitCost, 0) AS UnitCost,
    ExpiryDate
FROM dbo.ProductionBatches
WHERE BatchID = @BatchID",
            new { BatchID = batchId }, transaction);

        if (batch is null)
            return (false, "دفعة الإنتاج غير موجودة");

        if (batch.BatchStatus != 5)
            return (false, "يجب أن تكون الدفعة مقبولة من الجودة");

        if (!batch.TargetWarehouseID.HasValue || batch.TargetWarehouseID.Value <= 0)
            return (false, "يجب اختيار مخزن المنتج");

        if (batch.GoodQty <= 0)
            return (false, "يجب إدخال الكمية الصالحة");

        var alreadyAdded = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM dbo.InventoryBatchBalance WHERE BatchNumber = @BatchNumber",
            new { batch.BatchNumber }, transaction);

        if (alreadyAdded > 0)
            return (false, "تم إدخال هذه الدفعة إلى المخزون مسبقًا");

        await connection.ExecuteAsync(
            @"EXEC dbo.sp_UpdateInventoryBalance
                @ItemID = @ItemID,
                @WarehouseID = @WarehouseID,
                @Quantity = @Quantity,
                @UnitCost = @UnitCost,
                @TransactionType = @TransactionType,
                @BatchNumber = @BatchNumber,
                @ExpiryDate = @ExpiryDate,
                @SourceDocType = @SourceDocType,
                @SourceDocID = @SourceDocID,
                @SourceDocNumber = @SourceDocNumber,
                @SourceDocLineID = @SourceDocLineID,
                @Notes = @Notes,
                @UserID = @UserID",
            new
            {
                ItemID = batch.ProductItemID,
                WarehouseID = batch.TargetWarehouseID.Value,
                Quantity = batch.GoodQty,
                UnitCost = batch.UnitCost,
                TransactionType = 3,
                BatchNumber = batch.BatchNumber,
                batch.ExpiryDate,
                SourceDocType = "PB",
                SourceDocID = batch.BatchID,
                SourceDocNumber = batch.BatchNumber,
                SourceDocLineID = (int?)null,
                Notes = $"إضافة ناتج دفعة إنتاج رقم {batch.BatchNumber}",
                UserID = userId
            }, transaction);

        // تحديث تكلفة الصنف
        if (batch.UnitCost > 0)
        {
            await connection.ExecuteAsync(@"
UPDATE dbo.Items
SET AverageCost = @UnitCost,
    StandardCost = @UnitCost,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ItemID = @ItemID",
                new
                {
                    ItemID = batch.ProductItemID,
                    UnitCost = batch.UnitCost,
                    UserID = userId
                }, transaction);
        }

        // تحديث أمر التصنيع من الدفعات المقبولة
        await UpdateProductionOrderActualsAsync(connection, transaction, batch.ProductionOrderID, userId);

        transaction.Commit();

        await _audit.WriteAuditLogAsync(
            userId: userId,
            actionType: 2,
            tableName: "ProductionBatches",
            recordId: batchId.ToString(),
            moduleName: "SCR_BATCH",
            description: $"إدخال المخزن لدفعة: {batch.BatchNumber}"
        );

        await _notification.CreateNotificationAsync(
            notificationType: 1,
            title: "إضافة للمخزون",
            message: $"تم إدخال دفعة الإنتاج رقم {batch.BatchNumber} إلى المخزون",
            priority: 2,
            targetRoleId: 3,
            relatedModule: "SCR_BATCH",
            relatedRecordId: batchId,
            createdBy: userId
        );

        return (true, "تم إضافة المنتج للمخزون بنجاح");
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return (false, ex.Message);
    }
}
        public async Task<(bool Success, string Message)> StartStageAsync(
            int batchStageId,
            string? temperature,
            string? speed,
            int? operatorId,
            int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var stage = await connection.QueryFirstOrDefaultAsync<BatchStageDbDto>(@"
SELECT BatchStageID, BatchID, StartTime, StageStatus
FROM dbo.ProductionBatchStages
WHERE BatchStageID = @BatchStageID",
                    new { BatchStageID = batchStageId }, transaction);

                if (stage is null)
                    return (false, "المرحلة غير موجودة");

                var batch = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(@"
SELECT BatchID, BatchNumber, ProductionOrderID, BatchStatus
FROM dbo.ProductionBatches
WHERE BatchID = @BatchID",
                    new { BatchID = stage.BatchID }, transaction);

                if (batch is null)
                    return (false, "دفعة الإنتاج غير موجودة");

                // لو أول مرحلة والباتش لسه مسودة → بدء التصنيع تلقائيًا
                if (batch.BatchStatus == 1)
                {
                    // نتأكد إن ده فعلاً أول مرحلة
                    var isFirstStage = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.ProductionBatchStages
WHERE BatchID = @BatchID
  AND StageOrder < (
      SELECT StageOrder FROM dbo.ProductionBatchStages WHERE BatchStageID = @BatchStageID
  )",
                        new
                        {
                            BatchID = stage.BatchID,
                            BatchStageID = batchStageId
                        }, transaction);

                    if (isFirstStage == 0)
                    {
                        // ده أول مرحلة فعلاً → بدء التصنيع
                        await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus = 2,
    StartTime = GETDATE(),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE BatchID = @BatchID",
                            new { BatchID = stage.BatchID, UserID = userId }, transaction);

                        // تحديث أمر التصنيع
                        await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders
SET OrderStatus = CASE WHEN OrderStatus < 4 THEN 4 ELSE OrderStatus END,
    ActualStartDate = ISNULL(ActualStartDate, GETDATE()),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ProductionOrderID = @ProductionOrderID",
                            new
                            {
                                batch.ProductionOrderID,
                                UserID = userId
                            }, transaction);
                    }
                }

                // بدء المرحلة نفسها
                await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatchStages
SET StartTime = GETDATE(),
    StageStatus = 2,
    Temperature = @Temperature,
    Speed = @Speed,
    OperatorID = NULLIF(@OperatorID, 0)
WHERE BatchStageID = @BatchStageID",
                    new
                    {
                        BatchStageID = batchStageId,
                        Temperature = temperature,
                        Speed = speed,
                        OperatorID = operatorId
                    }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "ProductionBatchStages",
                    recordId: batchStageId.ToString(),
                    moduleName: "SCR_BATCH",
                    description: $"بدء مرحلة في دفعة: {batch.BatchNumber}"
                );

                return (true, "تم تسجيل بداية المرحلة بنجاح");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, ex.Message);
            }
        }
public async Task<(bool Success, string Message)> EndStageAsync(int batchStageId, int userId)
{
    using var connection = CreateConnection();

    var stage = await connection.QueryFirstOrDefaultAsync<BatchStageDbDto>(@"
SELECT BatchStageID, BatchID, StartTime, StageStatus
FROM dbo.ProductionBatchStages
WHERE BatchStageID = @BatchStageID",
        new { BatchStageID = batchStageId });

    if (stage is null)
        return (false, "المرحلة غير موجودة");

    if (!stage.StartTime.HasValue)
        return (false, "يجب تسجيل وقت البداية أولاً");

    await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatchStages
SET EndTime = GETDATE(),
    StageStatus = 3
WHERE BatchStageID = @BatchStageID",
        new { BatchStageID = batchStageId });

    return (true, "تم تسجيل نهاية المرحلة بنجاح");
}
private async Task RecalculateMaterialCostAsync(
    System.Data.IDbConnection connection,
    System.Data.IDbTransaction transaction,
    int batchId)
{
    var head = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(@"
SELECT BatchID, BatchNumber, ProductionOrderID, BatchStatus
FROM dbo.ProductionBatches
WHERE BatchID = @BatchID",
        new { BatchID = batchId }, transaction);

    if (head is null)
        return;

    var totalMaterialCost = await connection.ExecuteScalarAsync<decimal?>(@"
SELECT ISNULL(SUM(mid.IssuedQty * mid.UnitCost), 0)
FROM dbo.MaterialIssueNotes mi
INNER JOIN dbo.MaterialIssueDetails mid ON mi.IssueID = mid.IssueID
WHERE mi.ProductionOrderID = @ProductionOrderID
  AND mi.IssueStatus = 2",
        new { head.ProductionOrderID }, transaction) ?? 0m;

    var batchCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.ProductionBatches
WHERE ProductionOrderID = @ProductionOrderID",
        new { head.ProductionOrderID }, transaction);

    if (batchCount <= 0) batchCount = 1;

    var materialCostForBatch = Math.Round(totalMaterialCost / batchCount, 2);

    await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET MaterialCost = @MaterialCost,
    TotalCost = @MaterialCost + ISNULL(LaborCost, 0) + ISNULL(OverheadCost, 0)
WHERE BatchID = @BatchID",
        new
        {
            BatchID = batchId,
            MaterialCost = materialCostForBatch
        }, transaction);
}
private async Task UpdateProductionOrderActualsAsync(
    System.Data.IDbConnection connection,
    System.Data.IDbTransaction transaction,
    int productionOrderId,
    int userId)
{
    await connection.ExecuteAsync(@"
UPDATE po
SET
    ActualQty = ISNULL(x.TotalGood, 0),
    WasteQty = ISNULL(x.TotalWaste, 0),
    ActualMaterialCost = ISNULL(x.TotalMaterial, 0),
    ActualLaborCost = ISNULL(x.TotalLabor, 0),
    ActualOverheadCost = ISNULL(x.TotalOverhead, 0),
    ActualTotalCost = ISNULL(x.TotalMaterial, 0) + ISNULL(x.TotalLabor, 0) + ISNULL(x.TotalOverhead, 0),
    ActualEndDate = GETDATE(),
    OrderStatus = 6,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
FROM dbo.ProductionOrders po
OUTER APPLY
(
    SELECT
        SUM(ISNULL(GoodQty, 0)) AS TotalGood,
        SUM(ISNULL(WasteQty, 0)) AS TotalWaste,
        SUM(ISNULL(MaterialCost, 0)) AS TotalMaterial,
        SUM(ISNULL(LaborCost, 0)) AS TotalLabor,
        SUM(ISNULL(OverheadCost, 0)) AS TotalOverhead
    FROM dbo.ProductionBatches
    WHERE ProductionOrderID = @ProductionOrderID
      AND BatchStatus = 5
) x
WHERE po.ProductionOrderID = @ProductionOrderID",
        new
        {
            ProductionOrderID = productionOrderId,
            UserID = userId
        }, transaction);
}

        // ==========================================
        // تصدير إكسيل
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<ProductionBatchListDto> items, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("دفعات الإنتاج");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير دفعات الإنتاج — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 10).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 10).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[]
            {
                "#", "رقم الباتش", "التاريخ", "أمر التصنيع", "المنتج",
                "الكمية المخططة", "الكمية الصالحة", "الهالك", "التكلفة", "الحالة"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f766e");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = headerRow + 1;
            int num = 0;

            foreach (var item in items)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = item.BatchNumber;
                ws.Cell(row, 3).Value = item.BatchDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = item.ProductionOrderNumber;
                ws.Cell(row, 5).Value = item.ProductName;
                ws.Cell(row, 6).Value = item.PlannedQty;
                ws.Cell(row, 7).Value = item.GoodQty;
                ws.Cell(row, 8).Value = item.WasteQty;
                ws.Cell(row, 9).Value = item.TotalCost;
                ws.Cell(row, 10).Value = item.StatusName;

                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.####";
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.####";
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.####";
                ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 5,
                tableName: "ProductionBatches",
                moduleName: "SCR_BATCH",
                description: $"تصدير {items.Count} دفعة إنتاج إلى Excel"
            );

            return stream.ToArray();
        }
    }
}