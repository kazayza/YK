using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public class ProductionBatchService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notification;
        private readonly ModuleAuthorizationService _authorization;
        private const decimal ProductionQuantityTolerance = 0.0001m;

        private sealed class BatchHeadDbDto
        {
            public int BatchID { get; set; }
            public string BatchNumber { get; set; } = "";
            public int ProductionOrderID { get; set; }
            public int BatchStatus { get; set; }
            public int OrderStatus { get; set; }
            public int? QCResult { get; set; }
        }
        private sealed class BatchWorkflowGuardDto
        {
            public int BatchID { get; set; }
            public string BatchNumber { get; set; } = "";
            public int ProductionOrderID { get; set; }
            public int ProductItemID { get; set; }
            public int UnitID { get; set; }
            public int BatchStatus { get; set; }
            public int OrderStatus { get; set; }
            public int OrderProductItemID { get; set; }
            public int OrderUnitID { get; set; }
        }

        private sealed class BatchOrderGuardDto
        {
            public int ProductionOrderID { get; set; }
            public int ProductItemID { get; set; }
            public int PlannedUnitID { get; set; }
            public int? TargetWarehouseID { get; set; }
            public decimal PlannedQty { get; set; }
            public int OrderStatus { get; set; }
        }

        private sealed class BatchProductUnitMapDto
        {
            public int PrimaryUnitID { get; set; }
            public int? SecondaryUnitID { get; set; }
            public decimal? ConversionFactor { get; set; }
        }
        private sealed class BatchOrderSummaryStateDto
        {
            public decimal PlannedQty { get; set; }
            public int OrderStatus { get; set; }
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
    public int UnitID { get; set; }
    public int OrderProductItemID { get; set; }
    public int OrderUnitID { get; set; }
    public int BatchStatus { get; set; }
    public int? QCResult { get; set; }
    public int? TargetWarehouseID { get; set; }
    public decimal PlannedQty { get; set; }
    public decimal GoodQty { get; set; }
    public decimal UnitCost { get; set; }
    public int OrderStatus { get; set; }
    public int PrimaryUnitID { get; set; }
    public int? SecondaryUnitID { get; set; }
    public decimal? ConversionFactor { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

private sealed class BatchStageDbDto
{
    public int BatchStageID { get; set; }
    public int BatchID { get; set; }
    public DateTime? StartTime { get; set; }
    public int StageStatus { get; set; }
    public int StageOrder { get; set; }
}

private sealed class BatchQcDbDto
{
    public int BatchID { get; set; }
    public string BatchNumber { get; set; } = "";
    public int ProductionOrderID { get; set; }
    public int BatchStatus { get; set; }
    public int OrderStatus { get; set; }
    public int UnitID { get; set; }
    public decimal PlannedQty { get; set; }
    public decimal ProducedQty { get; set; }
    public decimal GoodQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal SampleQty { get; set; }
    public decimal WasteQty { get; set; }
    public int? QCResult { get; set; }
    public int? QCInspectedBy { get; set; }
    public string? QCNotes { get; set; }
}

        public ProductionBatchService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notification,
            ModuleAuthorizationService authorization)
            : base(configuration)
        {
            _audit = audit;
            _notification = notification;
            _authorization = authorization;
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
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView, "لا تملك صلاحية عرض دفعات التصنيع.");

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
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView, "لا تملك صلاحية عرض دفعات التصنيع.");

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
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView, "لا تملك صلاحية عرض مراحل دفعات التصنيع.");

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
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView, "لا تملك صلاحية عرض سجل دفعات التصنيع.");

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
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView, "لا تملك صلاحية عرض معلومات الدفعات.");

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

        public async Task<List<ProductionLookupDto>> GetProductionOrdersLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView,
                "لا تملك صلاحية عرض أوامر التصنيع للدفعات.");
            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<ProductionLookupDto>(@"
SELECT po.ProductionOrderID AS Id,
       CONCAT(po.OrderNumber,N' - ',i.ItemNameAr) AS Name
FROM dbo.ProductionOrders po
INNER JOIN dbo.Items i ON i.ItemID=po.ProductItemID
WHERE po.OrderStatus IN (2,3,4)
  AND po.PlannedQty>ISNULL((
      SELECT SUM(CASE
          WHEN pb.BatchStatus IN (1,2,3,4) THEN ISNULL(pb.PlannedQty,0)
          WHEN pb.BatchStatus=5 THEN ISNULL(pb.GoodQty,0)
          ELSE 0
      END)
      FROM dbo.ProductionBatches pb
      WHERE pb.ProductionOrderID=po.ProductionOrderID
  ),0)+@Tolerance
ORDER BY po.OrderDate DESC,po.ProductionOrderID DESC",
                new { Tolerance = ProductionQuantityTolerance });
            return rows.ToList();
        }

        public async Task<List<ProductionLookupDto>> GetWarehousesLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView,
                "لا تملك صلاحية عرض مخازن دفعات التصنيع.");
            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<ProductionLookupDto>(@"
SELECT WarehouseID AS Id,WarehouseNameAr AS Name
FROM dbo.Warehouses
WHERE IsActive=1 AND WarehouseType=4
ORDER BY WarehouseNameAr");
            return rows.ToList();
        }

        public async Task<List<ProductionLookupDto>> GetEmployeesLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView,
                "لا تملك صلاحية عرض الموظفين المرتبطين بدفعات التصنيع.");
            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<ProductionLookupDto>(@"
SELECT EmployeeID AS Id,FullNameAr AS Name
FROM dbo.Employees
WHERE IsActive=1
ORDER BY FullNameAr");
            return rows.ToList();
        }

        // ==========================================
        // إضافة باتش
        // ==========================================
        public async Task<int> InsertAsync(ProductionBatchEditDto batch, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanAdd,
                "لا تملك صلاحية إنشاء دفعات التصنيع.");
            if (batch.PlannedQty <= 0 || batch.ProductionOrderID <= 0)
                throw new InvalidOperationException("حدد أمر تصنيع وكمية مخططة موجبة للدفعة.");
            if (batch.MaterialCost < 0 || batch.LaborCost < 0 || batch.OverheadCost < 0 || batch.UnitCost < 0)
                throw new InvalidOperationException("لا يمكن حفظ تكلفة سالبة للدفعة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

            try
            {
                var order = await connection.QueryFirstOrDefaultAsync<BatchOrderGuardDto>(
                    @"SELECT ProductionOrderID,ProductItemID,PlannedUnitID,TargetWarehouseID,PlannedQty,OrderStatus
                      FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ID",
                    new { ID = batch.ProductionOrderID }, transaction);
                if (order is null)
                    throw new InvalidOperationException("أمر التصنيع المرتبط بالدفعة غير موجود.");
                if (order.OrderStatus is not (2 or 3 or 4))
                    throw new InvalidOperationException("لا يمكن إنشاء دفعة إلا لأمر تصنيع معتمد ونشط.");
                if (batch.ProductItemID != order.ProductItemID || batch.UnitID != order.PlannedUnitID)
                    throw new InvalidOperationException("المنتج أو وحدة الدفعة لا يطابقان أمر التصنيع.");
                var committedQty = await GetCommittedOrderOutputQtyAsync(
                    connection, transaction, batch.ProductionOrderID, excludeBatchId: 0);
                if (batch.PlannedQty - (order.PlannedQty - committedQty) > ProductionQuantityTolerance)
                    throw new InvalidOperationException("الكمية المخططة للدفعة تتجاوز المتبقي من الناتج المخطط لأمر التصنيع.");
                if (order.TargetWarehouseID.HasValue && order.TargetWarehouseID.Value > 0 &&
                    batch.TargetWarehouseID != order.TargetWarehouseID)
                    throw new InvalidOperationException("مخزن الدفعة لا يطابق مخزن المنتج المحدد في أمر التصنيع.");
                if (batch.TargetWarehouseID.HasValue && batch.TargetWarehouseID.Value > 0)
                {
                    var activeWarehouse = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK)
                          WHERE WarehouseID=@ID AND IsActive=1 AND WarehouseType=4",
                        new { ID = batch.TargetWarehouseID.Value }, transaction);
                    if (activeWarehouse != 1)
                        throw new InvalidOperationException("مخزن المنتج غير موجود أو غير نشط.");
                }
                await ValidateBatchEmployeesAsync(connection, transaction, batch.ProducedBy, batch.SupervisorID);

                var batchNumber = await GenerateBatchNumberAsync(connection, transaction);
                var totalCost = batch.MaterialCost + batch.LaborCost + batch.OverheadCost;
                var newId = await connection.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ProductionBatches
(
    BatchNumber,ProductionOrderID,ProductItemID,BatchDate,ExpiryDate,
    PlannedQty,ProducedQty,GoodQty,RejectedQty,WasteQty,SampleQty,
    UnitID,TargetWarehouseID,MaterialCost,LaborCost,OverheadCost,TotalCost,UnitCost,
    BatchStatus,StartTime,EndTime,ProducedBy,SupervisorID,
    QCInspectedBy,QCInspectionDate,QCResult,QCNotes,Notes,CreatedBy,CreatedDate
)
VALUES
(
    @BatchNumber,@ProductionOrderID,@ProductItemID,@BatchDate,@ExpiryDate,
    @PlannedQty,0,0,0,0,0,
    @UnitID,@TargetWarehouseID,@MaterialCost,@LaborCost,@OverheadCost,@TotalCost,NULL,
    1,NULL,NULL,NULLIF(@ProducedBy,0),NULLIF(@SupervisorID,0),
    NULL,NULL,NULL,NULL,@Notes,@UserID,GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        BatchNumber = batchNumber,
                        batch.ProductionOrderID,
                        batch.ProductItemID,
                        batch.BatchDate,
                        batch.ExpiryDate,
                        batch.PlannedQty,
                        batch.UnitID,
                        batch.TargetWarehouseID,
                        batch.MaterialCost,
                        batch.LaborCost,
                        batch.OverheadCost,
                        TotalCost = totalCost,
                        batch.ProducedBy,
                        batch.SupervisorID,
                        batch.Notes,
                        UserID = userId
                    }, transaction);

                await CreateDefaultStagesAsync(connection, transaction, newId, batch.ProductionOrderID);
                await _notification.CreateForPermissionHoldersAsync(
                    connection, transaction, notificationType: 1,
                    title: "دفعة إنتاج جديدة",
                    message: $"تم إنشاء دفعة إنتاج جديدة رقم {batchNumber}",
                    priority: 2, permissionModuleCode: "SCR_BATCH",
                    requiredPermission: ModulePermissionRequirement.View,
                    relatedModule: "SCR_BATCH", relatedRecordId: newId,
                    createdBy: userId, excludeUserId: userId);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 1,
                    tableName: "ProductionBatches",
                    recordId: newId.ToString(),
                    moduleName: "SCR_BATCH",
                    description: $"إنشاء دفعة إنتاج جديدة: {batchNumber}"
                );

                return newId;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        // ==========================================
        // تعديل باتش
        // ==========================================
        public async Task UpdateAsync(ProductionBatchEditDto batch, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanEdit,
                "لا تملك صلاحية تعديل دفعات التصنيع.");
            if (batch.PlannedQty <= 0 || batch.MaterialCost < 0 || batch.LaborCost < 0 ||
                batch.OverheadCost < 0 || batch.UnitCost < 0 || batch.ProducedQty < 0)
                throw new InvalidOperationException("تحقق من الكميات والتكاليف؛ يجب أن تكون غير سالبة والكمية المخططة موجبة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<BatchWorkflowGuardDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.ProductItemID,pb.UnitID,pb.BatchStatus,
       po.OrderStatus,po.ProductItemID AS OrderProductItemID,po.PlannedUnitID AS OrderUnitID
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
WHERE pb.BatchID=@BatchID",
                    new { batch.BatchID }, transaction);
                if (current is null)
                    throw new InvalidOperationException("دفعة الإنتاج غير موجودة.");
                if (current.BatchStatus is not (1 or 2))
                    throw new InvalidOperationException("لا تعدّل البيانات الأساسية بعد اكتمال التصنيع؛ سجّل بيانات الجودة من مسار الفحص.");
                if (current.OrderStatus is not (2 or 3 or 4))
                    throw new InvalidOperationException("لا يمكن تعديل دفعة إلا لأمر تصنيع نشط قبل فحص الجودة.");
                if (batch.ProductionOrderID != current.ProductionOrderID ||
                    batch.ProductItemID != current.OrderProductItemID ||
                    batch.UnitID != current.OrderUnitID)
                    throw new InvalidOperationException("لا يمكن تغيير ارتباط الدفعة أو المنتج أو وحدته بعد إنشائها.");
                var orderPlan = await connection.QueryFirstOrDefaultAsync<BatchOrderGuardDto>(@"
SELECT ProductionOrderID,ProductItemID,PlannedUnitID,TargetWarehouseID,PlannedQty,OrderStatus
FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ID",
                    new { ID = current.ProductionOrderID }, transaction);
                if (orderPlan is null)
                    throw new InvalidOperationException("أمر التصنيع المرتبط بالدفعة غير موجود.");
                if (orderPlan.OrderStatus is not (2 or 3 or 4))
                    throw new InvalidOperationException("أمر التصنيع غير نشط؛ لا يمكن تعديل الدفعة.");
                if (orderPlan.TargetWarehouseID.HasValue && batch.TargetWarehouseID != orderPlan.TargetWarehouseID)
                    throw new InvalidOperationException("مخزن الدفعة لا يطابق مخزن المنتج المحدد في أمر التصنيع.");
                var committedQty = await GetCommittedOrderOutputQtyAsync(
                    connection, transaction, current.ProductionOrderID, batch.BatchID);
                if (batch.PlannedQty - (orderPlan.PlannedQty - committedQty) > ProductionQuantityTolerance)
                    throw new InvalidOperationException("الكمية المخططة للدفعة تتجاوز المتبقي من الناتج المخطط لأمر التصنيع.");

                if (batch.TargetWarehouseID.HasValue && batch.TargetWarehouseID.Value > 0)
                {
                    var activeWarehouse = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK)
                          WHERE WarehouseID=@ID AND IsActive=1 AND WarehouseType=4",
                        new { ID = batch.TargetWarehouseID.Value }, transaction);
                    if (activeWarehouse != 1)
                        throw new InvalidOperationException("مخزن المنتج غير موجود أو غير نشط.");
                }
                await ValidateBatchEmployeesAsync(connection, transaction, batch.ProducedBy, batch.SupervisorID);

                var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches SET
    BatchDate=@BatchDate,ExpiryDate=@ExpiryDate,PlannedQty=@PlannedQty,
    ProducedQty=CASE WHEN @CurrentStatus=2 THEN @ProducedQty ELSE ProducedQty END,
    UnitID=@UnitID,TargetWarehouseID=@TargetWarehouseID,
    MaterialCost=@MaterialCost,LaborCost=@LaborCost,OverheadCost=@OverheadCost,
    TotalCost=@TotalCost,
    ProducedBy=NULLIF(@ProducedBy,0),SupervisorID=NULLIF(@SupervisorID,0),
    Notes=@Notes,ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE BatchID=@BatchID AND BatchStatus=@CurrentStatus",
                    new
                    {
                        batch.BatchID,
                        CurrentStatus = current.BatchStatus,
                        batch.BatchDate,
                        batch.ExpiryDate,
                        batch.PlannedQty,
                        batch.ProducedQty,
                        batch.UnitID,
                        batch.TargetWarehouseID,
                        batch.MaterialCost,
                        batch.LaborCost,
                        batch.OverheadCost,
                        TotalCost = batch.MaterialCost + batch.LaborCost + batch.OverheadCost,
                        batch.ProducedBy,
                        batch.SupervisorID,
                        batch.Notes,
                        UserID = userId
                    }, transaction);
                if (changed != 1)
                    throw new InvalidOperationException("تغيرت حالة الدفعة أثناء الحفظ؛ أعد تحميلها.");

                await SyncOrderSummaryFromBatchesAsync(connection, transaction, current.ProductionOrderID, userId);
                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "ProductionBatches",
                    recordId: batch.BatchID.ToString(),
                    changedColumns: "BatchDate,ExpiryDate,PlannedQty,ProducedQty,Costs,TargetWarehouseID,Notes",
                    moduleName: "SCR_BATCH",
                    description: $"تعديل دفعة إنتاج: {current.BatchNumber}"
                );
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<(bool Success, string Message)> SetOutputUnitCostAsync(
            int batchId, decimal unitCost, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanPost,
                "لا تملك صلاحية تسجيل تكلفة وحدة المنتج قبل الترحيل.");
            if (unitCost <= 0)
                return (false, "أدخل تكلفة موجبة للوحدة المنتجة؛ لن تُشتق التكلفة تلقائياً من الهالك أو المرفوض.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                var batch = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.BatchStatus,pb.QCResult,po.OrderStatus
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
WHERE pb.BatchID=@BatchID",
                    new { BatchID = batchId }, transaction);
                if (batch is null) return (false, "دفعة الإنتاج غير موجودة.");
                if (batch.OrderStatus is 6 or 7)
                    return (false, "لا يمكن تسجيل تكلفة دفعة مرتبطة بأمر مكتمل أو ملغي.");
                if (batch.BatchStatus != 5)
                    return (false, "لا تُسجل تكلفة الترحيل إلا بعد قبول الدفعة من الجودة.");
                if (batch.QCResult != 1)
                    return (false, "لا تُسجل تكلفة ترحيل لدفعة بلا نتيجة قبول جودة صريحة.");

                var alreadyPosted = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.InventoryTransactions WITH (UPDLOCK,HOLDLOCK)
WHERE SourceDocType=N'PB' AND SourceDocID=@BatchID AND TransactionType=3",
                    new { BatchID = batchId }, transaction);
                if (alreadyPosted > 0)
                    return (false, "لا يمكن تعديل تكلفة الوحدة بعد ترحيل الدفعة إلى المخزون.");

                var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET UnitCost=@UnitCost,ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE BatchID=@BatchID AND BatchStatus=5 AND QCResult=1",
                    new { BatchID = batchId, UnitCost = unitCost, UserID = userId }, transaction);
                if (changed != 1)
                    throw new InvalidOperationException("تغيرت حالة الدفعة أثناء حفظ التكلفة؛ أعد تحميلها.");

                transaction.Commit();
                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "ProductionBatches",
                    recordId: batchId.ToString(),
                    changedColumns: "UnitCost",
                    moduleName: "SCR_BATCH",
                    description: $"تحديد يدوي لتكلفة ترحيل وحدة الدفعة: {batch.BatchNumber}"
                );
                return (true, "تم حفظ تكلفة وحدة المنتج كما أُدخلت يدوياً.");
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); } catch { }
                return (false, ex.Message);
            }
        }

        // ==========================================
        // تغيير حالة الباتش
        // ==========================================
        public async Task<(bool Success, string Message)> ChangeStatusAsync(int batchId, int newStatus, int userId)
        {
            var needsApproval = newStatus is 5 or 6;
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH",
                p => needsApproval ? p.CanApprove : p.CanEdit,
                "لا تملك صلاحية تغيير حالة دفعة التصنيع.");

            return newStatus is 5 or 6
                ? (false, "تغيير نتيجة الجودة يتطلب مسار الفحص الذي يتحقق من المفتش والكميات.")
                : (false, "استخدم مساري بدء التصنيع أو اكتماله؛ لا يُسمح بتغيير حالة الدفعة مباشرة.");
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
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

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
            var order = await connection.QueryFirstOrDefaultAsync<BatchOrderSummaryStateDto>(@"
SELECT PlannedQty,OrderStatus
FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID",
                new { ProductionOrderID = productionOrderId }, transaction);
            if (order is null) return;

            await connection.ExecuteAsync(@"
UPDATE po
SET ActualQty=ISNULL(x.AcceptedQty,0),
    WasteQty=ISNULL(x.WasteQty,0),
    ModifiedBy=@UserID,ModifiedDate=GETDATE()
FROM dbo.ProductionOrders po
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN pb.BatchStatus=5 THEN ISNULL(pb.GoodQty,0) ELSE 0 END) AS AcceptedQty,
        SUM(ISNULL(pb.WasteQty,0)) AS WasteQty
    FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
    WHERE pb.ProductionOrderID=@ProductionOrderID
) x
WHERE po.ProductionOrderID=@ProductionOrderID",
                new { ProductionOrderID = productionOrderId, UserID = userId }, transaction);

            var totalBatches = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID",
                new { ProductionOrderID = productionOrderId }, transaction);
            if (totalBatches == 0) return;

            var unfinishedBatches = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID AND BatchStatus NOT IN (5,6)",
                new { ProductionOrderID = productionOrderId }, transaction);
            var acceptedQty = await connection.ExecuteScalarAsync<decimal>(@"
SELECT ISNULL(SUM(CASE WHEN BatchStatus=5 THEN ISNULL(GoodQty,0) ELSE 0 END),0)
FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID",
                new { ProductionOrderID = productionOrderId }, transaction);
            var acceptedNotPosted = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
WHERE pb.ProductionOrderID=@ProductionOrderID AND pb.BatchStatus=5
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.InventoryTransactions it WITH (UPDLOCK,HOLDLOCK)
      WHERE it.SourceDocType=N'PB' AND it.SourceDocID=pb.BatchID AND it.TransactionType=3
  )",
                new { ProductionOrderID = productionOrderId }, transaction);
            var pendingQc = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID AND BatchStatus IN (3,4)",
                new { ProductionOrderID = productionOrderId }, transaction);
            var inProduction = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID AND BatchStatus=2",
                new { ProductionOrderID = productionOrderId }, transaction);

            int? newOrderStatus = null;
            if (unfinishedBatches == 0 && acceptedQty + ProductionQuantityTolerance >= order.PlannedQty && acceptedNotPosted == 0)
                newOrderStatus = 6;
            else if (pendingQc > 0 || acceptedNotPosted > 0)
                newOrderStatus = 5;
            else if (inProduction > 0 || unfinishedBatches == 0)
                newOrderStatus = 4;

            if (newOrderStatus.HasValue && order.OrderStatus is not (6 or 7))
            {
                await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders
SET OrderStatus=@OrderStatus,ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE ProductionOrderID=@ProductionOrderID AND OrderStatus NOT IN (6,7)",
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
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لدفعة التصنيع.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم دفعة التصنيع (BT). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل BT والإجراء المخزن.", ex);
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
        private static async Task ValidateBatchEmployeesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            params int?[] employeeIds)
        {
            foreach (var employeeId in employeeIds)
            {
                if (!employeeId.HasValue || employeeId.Value <= 0) continue;
                var active = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.Employees WITH (UPDLOCK,HOLDLOCK)
WHERE EmployeeID=@EmployeeID AND IsActive=1",
                    new { EmployeeID = employeeId.Value }, transaction);
                if (active != 1)
                    throw new InvalidOperationException($"الموظف {employeeId.Value} غير موجود أو غير نشط.");
            }
        }

        private static async Task<decimal> GetCommittedOrderOutputQtyAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction? transaction,
            int productionOrderId,
            int excludeBatchId)
        {
            return await connection.ExecuteScalarAsync<decimal>(@"
SELECT ISNULL(SUM(CASE
    WHEN BatchStatus IN (1,2,3,4) THEN ISNULL(PlannedQty,0)
    WHEN BatchStatus=5 THEN ISNULL(GoodQty,0)
    ELSE 0
END),0)
FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID AND BatchID<>@ExcludeBatchID",
                new { ProductionOrderID = productionOrderId, ExcludeBatchID = excludeBatchId }, transaction);
        }

        public async Task<decimal> GetSuggestedBatchQtyAsync(int productionOrderId, int excludeBatchId = 0)
{
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView, "لا تملك صلاحية عرض بيانات الدفعات.");

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

    var previousQty = await GetCommittedOrderOutputQtyAsync(connection, null, productionOrderId, excludeBatchId);

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
    await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanEdit,
        "لا تملك صلاحية تحميل مراحل الوصفة للدفعة.");
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

    try
    {
        var batch = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.BatchStatus,po.OrderStatus
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
WHERE pb.BatchID=@BatchID",
            new { BatchID = batchId }, transaction);

        if (batch is null)
            return (false, "دفعة الإنتاج غير موجودة", 0);
        if (batch.OrderStatus is not (2 or 3 or 4))
            return (false, "أمر التصنيع غير نشط؛ لا يمكن تحميل مراحله.", 0);

        if (batch.BatchStatus != 1)
            return (false, "لا يمكن تحميل أو استبدال مراحل الوصفة بعد بدء الدفعة.", 0);
        var startedStages = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatchStages WITH (UPDLOCK,HOLDLOCK)
WHERE BatchID=@BatchID AND StageStatus<>1",
            new { BatchID = batchId }, transaction);
        if (startedStages > 0)
            return (false, "لا يمكن استبدال مراحل سبق بدء إحداها.", 0);

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
    await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanEdit,
        "لا تملك صلاحية بدء تصنيع الدفعة.");
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
    BatchWorkflowGuardDto? batch = null;

    try
    {
        batch = await connection.QueryFirstOrDefaultAsync<BatchWorkflowGuardDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.ProductItemID,pb.UnitID,pb.BatchStatus,
       po.OrderStatus,po.ProductItemID AS OrderProductItemID,po.PlannedUnitID AS OrderUnitID
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
WHERE pb.BatchID=@BatchID",
            new { BatchID = batchId }, transaction);
        if (batch is null) return (false, "دفعة الإنتاج غير موجودة");
        if (batch.BatchStatus != 1)
            return (false, "لا يمكن بدء التصنيع إلا من حالة مسودة");
        if (batch.OrderStatus is not (2 or 3 or 4))
            return (false, "أمر التصنيع غير نشط أو دخل مرحلة الجودة؛ لا يمكن بدء الدفعة.");
        if (batch.ProductItemID != batch.OrderProductItemID || batch.UnitID != batch.OrderUnitID)
            return (false, "المنتج أو وحدة الدفعة لا يطابقان أمر التصنيع.");

        var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus=2,StartTime=ISNULL(StartTime,GETDATE()),ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE BatchID=@BatchID AND BatchStatus=1",
            new { BatchID = batchId, UserID = userId }, transaction);
        if (changed != 1)
            throw new InvalidOperationException("تغيرت حالة الدفعة أثناء بدء التصنيع؛ أعد تحميلها.");

        await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders
SET OrderStatus=4,ActualStartDate=ISNULL(ActualStartDate,GETDATE()),
    ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE ProductionOrderID=@ProductionOrderID AND OrderStatus IN (2,3,4)",
            new { batch.ProductionOrderID, UserID = userId }, transaction);
        await SyncOrderSummaryFromBatchesAsync(connection, transaction, batch.ProductionOrderID, userId);
        await _notification.MarkRelatedAsActionedAsync(
            connection, transaction, "SCR_BATCH", batchId);
        await _notification.CreateForPermissionHoldersAsync(
            connection, transaction, notificationType: 1,
            title: "بدء التصنيع",
            message: $"تم بدء التصنيع لدفعة الإنتاج رقم {batch.BatchNumber}",
            priority: 2, permissionModuleCode: "SCR_BATCH",
            requiredPermission: ModulePermissionRequirement.View,
            relatedModule: "SCR_BATCH", relatedRecordId: batchId,
            createdBy: userId, excludeUserId: userId);
        transaction.Commit();
    }
    catch (Exception ex)
    {
        try { transaction.Rollback(); } catch { }
        return (false, ex.Message);
    }

    await _audit.WriteAuditLogAsync(
        userId: userId,
        actionType: 2,
        tableName: "ProductionBatches",
        recordId: batchId.ToString(),
        changedColumns: "BatchStatus,StartTime",
        moduleName: "SCR_BATCH",
        description: $"بدء تصنيع دفعة: {batch!.BatchNumber}"
    );
    return (true, "تم بدء التصنيع بنجاح");
}
public async Task<(bool Success, string Message)> CompleteProductionAsync(int batchId, int userId)
{
    await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanEdit,
        "لا تملك صلاحية تسجيل اكتمال التصنيع.");

    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
    BatchQcDbDto? batch = null;
    try
    {
        batch = await connection.QueryFirstOrDefaultAsync<BatchQcDbDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.BatchStatus,po.OrderStatus,pb.PlannedQty,
       ISNULL(pb.ProducedQty,0) AS ProducedQty,ISNULL(pb.GoodQty,0) AS GoodQty,
       ISNULL(pb.RejectedQty,0) AS RejectedQty,ISNULL(pb.SampleQty,0) AS SampleQty,
       ISNULL(pb.WasteQty,0) AS WasteQty
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
WHERE pb.BatchID=@BatchID",
            new { BatchID = batchId }, transaction);
        if (batch is null) return (false, "دفعة الإنتاج غير موجودة");
        if (batch.OrderStatus is not (2 or 3 or 4))
            return (false, "أمر التصنيع غير نشط؛ لا يمكن إكمال الدفعة.");
        if (batch.BatchStatus != 2)
            return (false, "لا يمكن تسجيل اكتمال التصنيع إلا من حالة قيد التصنيع");
        if (batch.ProducedQty <= 0)
            return (false, "يجب إدخال الكمية المنتجة أولاً");
        if (batch.ProducedQty - batch.PlannedQty > ProductionQuantityTolerance)
            return (false, "الكمية المنتجة تتجاوز الكمية المخططة للدفعة؛ راجع الكميات قبل الإكمال.");

        var stageCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatchStages WITH (UPDLOCK,HOLDLOCK)
WHERE BatchID=@BatchID",
            new { BatchID = batchId }, transaction);
        var incompleteStages = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatchStages WITH (UPDLOCK,HOLDLOCK)
WHERE BatchID=@BatchID AND StageStatus<>3",
            new { BatchID = batchId }, transaction);
        if (stageCount > 0 && incompleteStages > 0)
            return (false, "لا يمكن إكمال التصنيع قبل إنهاء جميع مراحل الدفعة.");

        var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus=3,EndTime=GETDATE(),ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE BatchID=@BatchID AND BatchStatus=2",
            new { BatchID = batchId, UserID = userId }, transaction);
        if (changed != 1)
            throw new InvalidOperationException("تغيرت حالة الدفعة أثناء الإكمال؛ أعد تحميلها.");

        await SyncOrderSummaryFromBatchesAsync(connection, transaction, batch.ProductionOrderID, userId);
        await _notification.MarkRelatedAsActionedAsync(
            connection, transaction, "SCR_BATCH", batchId);
        await _notification.CreateForPermissionHoldersAsync(
            connection, transaction, notificationType: 1,
            title: "دفعة جاهزة لفحص الجودة",
            message: $"اكتمل تصنيع الدفعة {batch.BatchNumber} وأصبحت جاهزة لفحص الجودة",
            priority: 2, permissionModuleCode: "SCR_BATCH",
            requiredPermission: ModulePermissionRequirement.Approve,
            relatedModule: "SCR_BATCH", relatedRecordId: batchId,
            createdBy: userId, excludeUserId: userId);
        transaction.Commit();
    }
    catch (Exception ex)
    {
        try { transaction.Rollback(); } catch { }
        return (false, ex.Message);
    }

    await _audit.WriteAuditLogAsync(
        userId: userId,
        actionType: 2,
        tableName: "ProductionBatches",
        recordId: batchId.ToString(),
        changedColumns: "BatchStatus,EndTime",
        moduleName: "SCR_BATCH",
        description: $"اكتمال تصنيع دفعة: {batch!.BatchNumber}"
    );
    return (true, "تم تسجيل اكتمال التصنيع بنجاح");
}
public async Task<(bool Success, string Message)> ApplyQcAsync(
    int batchId,
    int qcResult,
    int qcInspectedBy,
    string? qcNotes,
    decimal goodQty,
    decimal rejectedQty,
    decimal sampleQty,
    decimal wasteQty,
    int userId)
{
    await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanApprove,
        "لا تملك صلاحية اعتماد نتيجة جودة الدفعة.");
    if (qcResult is < 1 or > 3)
        return (false, "يجب اختيار نتيجة فحص جودة صالحة.");
    if (qcInspectedBy <= 0)
        return (false, "يجب اختيار مفتش الجودة.");
    if (goodQty < 0 || rejectedQty < 0 || sampleQty < 0 || wasteQty < 0)
        return (false, "كميات الجودة لا يمكن أن تكون سالبة.");
    if (qcResult != 2 && goodQty <= 0)
        return (false, "نتيجة الفحص الناجحة تتطلب كمية صالحة موجبة.");
    if (qcResult == 2 && goodQty > ProductionQuantityTolerance)
        return (false, "نتيجة الفحص الفاشلة لا يمكن أن تتضمن كمية صالحة للترحيل.");
    if (qcResult == 3 && string.IsNullOrWhiteSpace(qcNotes))
        return (false, "يجب توضيح شرط/تحفظ نتيجة الفحص في ملاحظات الجودة.");

    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
    BatchQcDbDto? batch = null;
    int newStatus = 0;
    try
    {
        batch = await connection.QueryFirstOrDefaultAsync<BatchQcDbDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.BatchStatus,po.OrderStatus,pb.PlannedQty,
       ISNULL(pb.ProducedQty,0) AS ProducedQty,ISNULL(pb.GoodQty,0) AS GoodQty,
       ISNULL(pb.RejectedQty,0) AS RejectedQty,ISNULL(pb.SampleQty,0) AS SampleQty,
       ISNULL(pb.WasteQty,0) AS WasteQty,pb.QCResult,pb.QCInspectedBy,pb.QCNotes
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
WHERE pb.BatchID=@BatchID",
            new { BatchID = batchId }, transaction);
        if (batch is null) return (false, "دفعة الإنتاج غير موجودة");
        if (batch.OrderStatus is not (2 or 3 or 4 or 5))
            return (false, "أمر التصنيع غير نشط؛ لا يمكن تسجيل نتيجة جودة للدفعة.");
        var activeInspector = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.Employees WITH (UPDLOCK,HOLDLOCK)
WHERE EmployeeID=@EmployeeID AND IsActive=1",
            new { EmployeeID = qcInspectedBy }, transaction);
        if (activeInspector != 1)
            return (false, "مفتش الجودة غير موجود أو غير نشط.");
        if (batch.BatchStatus is not (3 or 4))
            return (false, "يجب أن تكون الدفعة مكتملة التصنيع أو في فحص الجودة");

        if (batch.ProducedQty <= 0)
            return (false, "لا يمكن فحص دفعة بلا كمية إنتاج فعلية موجبة.");

        var classifiedQty = goodQty + rejectedQty + sampleQty + wasteQty;
        if (Math.Abs(classifiedQty - batch.ProducedQty) > ProductionQuantityTolerance)
            return (false, "يجب أن يساوي مجموع الصالح والمرفوض والعينات والهالك كمية الإنتاج الفعلية؛ أدخل كل كمية صراحةً.");

        // QC 3 is a conditional pass: keep the batch in the existing QC/hold status (4).
        // Only an unconditional pass (1) may become stock-eligible status 5.
        newStatus = qcResult switch
        {
            1 => 5,
            2 => 6,
            3 => 4,
            _ => throw new InvalidOperationException("نتيجة الجودة غير صالحة.")
        };

        var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus=@BatchStatus,GoodQty=@GoodQty,RejectedQty=@RejectedQty,
    SampleQty=@SampleQty,WasteQty=@WasteQty,QCInspectedBy=@QCInspectedBy,
    QCInspectionDate=GETDATE(),QCResult=@QCResult,QCNotes=@QCNotes,
    ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE BatchID=@BatchID AND BatchStatus IN (3,4)",
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
            }, transaction);
        if (changed != 1)
            throw new InvalidOperationException("تغيرت حالة الدفعة أثناء تسجيل نتيجة الجودة؛ أعد تحميلها.");

        await SyncOrderSummaryFromBatchesAsync(connection, transaction, batch.ProductionOrderID, userId);
        await _notification.MarkRelatedAsActionedAsync(
            connection, transaction, "SCR_BATCH", batchId);
        await _notification.CreateForPermissionHoldersAsync(
            connection, transaction, notificationType: 1,
            title: newStatus switch
            {
                5 => "نتيجة فحص الجودة",
                4 => "دفعة محجوزة بعد فحص الجودة",
                _ => "دفعة مرفوضة من الجودة"
            },
            message: newStatus switch
            {
                5 => $"تم قبول دفعة الإنتاج {batch.BatchNumber}؛ أدخل تكلفة وحدة المنتج قبل الترحيل.",
                4 => $"نتيجة فحص الدفعة {batch.BatchNumber} ناجح بتحفظ؛ بقيت في حالة فحص الجودة ولن تُرحّل للمخزون قبل إعادة الفحص وتسجيل قبول صريح.",
                _ => $"تم رفض دفعة الإنتاج {batch.BatchNumber} من الجودة؛ لم تُرحّل إلى المخزون."
            },
            priority: newStatus == 6 ? (byte)2 : (byte)1,
            permissionModuleCode: "SCR_BATCH",
            requiredPermission: ModulePermissionRequirement.View,
            relatedModule: "SCR_BATCH", relatedRecordId: batchId,
            createdBy: userId, excludeUserId: userId);
        transaction.Commit();
    }
    catch (Exception ex)
    {
        try { transaction.Rollback(); } catch { }
        return (false, ex.Message);
    }

    var auditedBatch = batch!;
    await _audit.WriteAuditLogAsync(
        userId: userId,
        actionType: 2,
        tableName: "ProductionBatches",
        recordId: batchId.ToString(),
        oldValues: System.Text.Json.JsonSerializer.Serialize(new
        {
            auditedBatch.BatchStatus,
            auditedBatch.QCResult,
            auditedBatch.QCInspectedBy,
            auditedBatch.GoodQty,
            auditedBatch.RejectedQty,
            auditedBatch.SampleQty,
            auditedBatch.WasteQty,
            auditedBatch.QCNotes
        }),
        newValues: System.Text.Json.JsonSerializer.Serialize(new
        {
            BatchStatus = newStatus,
            QCResult = qcResult,
            QCInspectedBy = qcInspectedBy,
            GoodQty = goodQty,
            RejectedQty = rejectedQty,
            SampleQty = sampleQty,
            WasteQty = wasteQty,
            QCNotes = qcNotes
        }),
        changedColumns: "BatchStatus,QCResult,QCInspectionDate,QCInspectedBy,GoodQty,RejectedQty,SampleQty,WasteQty,QCNotes",
        moduleName: "SCR_BATCH",
        description: $"فحص جودة دفعة: {auditedBatch.BatchNumber}"
    );
    return (true, "تم تسجيل نتيجة فحص الجودة بنجاح");
}
public async Task<bool> HasInventoryEntryAsync(int batchId)
{
    await _authorization.RequireCurrentUserPermissionAsync("SCR_BATCH", p => p.CanView,
        "لا تملك صلاحية عرض دفعات التصنيع.");
    using var connection = CreateConnection();
    var count = await connection.ExecuteScalarAsync<int>(@"
SELECT CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.InventoryTransactions
    WHERE SourceDocType=N'PB' AND SourceDocID=@BatchID AND TransactionType=3
) OR EXISTS
(
    SELECT 1
    FROM dbo.ProductionBatches pb
    INNER JOIN dbo.InventoryBatchBalance ib
        ON ib.ItemID=pb.ProductItemID AND ib.BatchNumber=pb.BatchNumber
       AND (pb.TargetWarehouseID IS NULL OR ib.WarehouseID=pb.TargetWarehouseID)
    WHERE pb.BatchID=@BatchID
) THEN 1 ELSE 0 END",
        new { BatchID = batchId });
    return count > 0;
}
public async Task<(bool Success, string Message)> AddToInventoryAsync(int batchId, int userId)
{
    await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanPost,
        "لا تملك صلاحية ترحيل دفعات التصنيع إلى المخزون.");
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
    BatchInventoryDbDto? batch = null;
    decimal primaryQuantity = 0m;
    decimal primaryUnitCost = 0m;

    try
    {
        batch = await connection.QueryFirstOrDefaultAsync<BatchInventoryDbDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.ProductItemID,pb.UnitID,
       po.ProductItemID AS OrderProductItemID,po.PlannedUnitID AS OrderUnitID,po.OrderStatus,
       pb.BatchStatus,pb.QCResult,pb.TargetWarehouseID,pb.PlannedQty,ISNULL(pb.GoodQty,0) AS GoodQty,
       ISNULL(pb.UnitCost,0) AS UnitCost,pb.ExpiryDate,
       i.PrimaryUnitID,i.SecondaryUnitID,i.ConversionFactor
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
INNER JOIN dbo.Items i WITH (UPDLOCK,HOLDLOCK)
    ON i.ItemID=pb.ProductItemID
WHERE pb.BatchID=@BatchID",
            new { BatchID = batchId }, transaction);
        if (batch is null) return (false, "دفعة الإنتاج غير موجودة");
        if (batch.BatchStatus != 5)
            return (false, "يجب أن تكون الدفعة مقبولة من الجودة");
        if (batch.QCResult != 1)
            return (false, "لا يمكن ترحيل دفعة دون نتيجة قبول جودة صريحة.");
        if (batch.OrderStatus is 6 or 7)
            return (false, "لا يمكن ترحيل دفعة مرتبطة بأمر مكتمل أو ملغي.");
        if (batch.ProductItemID != batch.OrderProductItemID || batch.UnitID != batch.OrderUnitID)
            return (false, "المنتج أو وحدة الدفعة لا يطابقان أمر التصنيع.");
        if (!batch.TargetWarehouseID.HasValue || batch.TargetWarehouseID.Value <= 0)
            return (false, "يجب اختيار مخزن المنتج");
        if (batch.GoodQty <= 0)
            return (false, "يجب إدخال كمية صالحة موجبة");
        if (batch.UnitCost <= 0)
            return (false, "أدخل تكلفة وحدة المنتج يدوياً بعد اعتماد الجودة وقبل الترحيل؛ لن نوزع تكلفة المواد أو الهالك/المرفوض تلقائياً.");
        if (batch.PrimaryUnitID <= 0)
            return (false, "لا توجد وحدة مخزون أساسية صالحة للمنتج.");

        var warehouseIsActive = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK)
WHERE WarehouseID=@WarehouseID AND IsActive=1 AND WarehouseType=4",
            new { WarehouseID = batch.TargetWarehouseID.Value }, transaction);
        if (warehouseIsActive != 1)
            return (false, "مخزن المنتج غير موجود أو غير نشط.");

        var alreadyPosted = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.InventoryTransactions WITH (UPDLOCK,HOLDLOCK)
WHERE SourceDocType=N'PB' AND SourceDocID=@BatchID AND TransactionType=3",
            new { BatchID = batchId }, transaction);
        if (alreadyPosted > 0)
            return (false, "تم ترحيل هذه الدفعة إلى المخزون مسبقاً.");
        var orphanBatchBalance = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.InventoryBatchBalance WITH (UPDLOCK,HOLDLOCK)
WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID AND BatchNumber=@BatchNumber",
            new
            {
                ItemID = batch.ProductItemID,
                WarehouseID = batch.TargetWarehouseID.Value,
                BatchNumber = batch.BatchNumber
            }, transaction);
        if (orphanBatchBalance > 0)
            return (false, "يوجد رصيد باتش سابق بلا حركة مصدر موثوقة؛ أوقفنا الترحيل للمطابقة اليدوية.");

        var factor = await ResolveBatchToPrimaryUnitFactorAsync(connection, transaction,
            new BatchProductUnitMapDto
            {
                PrimaryUnitID = batch.PrimaryUnitID,
                SecondaryUnitID = batch.SecondaryUnitID,
                ConversionFactor = batch.ConversionFactor
            }, batch.UnitID);
        primaryQuantity = decimal.Round(batch.GoodQty * factor, 4, MidpointRounding.AwayFromZero);
        primaryUnitCost = decimal.Round(batch.UnitCost / factor, 4, MidpointRounding.AwayFromZero);
        if (primaryQuantity <= 0 || primaryUnitCost <= 0)
            return (false, "كمية أو تكلفة المنتج أقل من دقة وحدة المخزون الأساسية.");

        await connection.ExecuteAsync(@"
EXEC dbo.sp_UpdateInventoryBalance
    @ItemID=@ItemID,@WarehouseID=@WarehouseID,@Quantity=@Quantity,@UnitCost=@UnitCost,
    @TransactionType=@TransactionType,@BatchNumber=@BatchNumber,@ExpiryDate=@ExpiryDate,
    @SourceDocType=@SourceDocType,@SourceDocID=@SourceDocID,@SourceDocNumber=@SourceDocNumber,
    @SourceDocLineID=@SourceDocLineID,@Notes=@Notes,@UserID=@UserID",
            new
            {
                ItemID = batch.ProductItemID,
                WarehouseID = batch.TargetWarehouseID.Value,
                Quantity = primaryQuantity,
                UnitCost = primaryUnitCost,
                TransactionType = (byte)3,
                BatchNumber = batch.BatchNumber,
                batch.ExpiryDate,
                SourceDocType = "PB",
                SourceDocID = batch.BatchID,
                SourceDocNumber = batch.BatchNumber,
                SourceDocLineID = (int?)null,
                Notes = $"إضافة ناتج دفعة إنتاج رقم {batch.BatchNumber}",
                UserID = userId
            }, transaction);

        await UpdateProductionOrderActualsAsync(connection, transaction, batch.ProductionOrderID, userId);
        await _notification.MarkRelatedAsActionedAsync(
            connection, transaction, "SCR_BATCH", batchId);
        await _notification.CreateForPermissionHoldersAsync(
            connection, transaction, notificationType: 1,
            title: "ترحيل دفعة إلى المخزون",
            message: $"تم ترحيل دفعة الإنتاج رقم {batch.BatchNumber} إلى المخزون",
            priority: 2, permissionModuleCode: "SCR_BATCH",
            requiredPermission: ModulePermissionRequirement.View,
            relatedModule: "SCR_BATCH", relatedRecordId: batchId,
            createdBy: userId, excludeUserId: userId);

        transaction.Commit();
    }
    catch (Exception ex)
    {
        try { transaction.Rollback(); } catch { }
        return (false, ex.Message);
    }

    await _audit.WriteAuditLogAsync(
        userId: userId,
        actionType: 2,
        tableName: "ProductionBatches",
        recordId: batchId.ToString(),
        changedColumns: "InventoryTransactions,InventoryBalance,InventoryBatchBalance,ProductionOrderSummary",
        moduleName: "SCR_BATCH",
        description: $"إدخال المخزن لدفعة: {batch!.BatchNumber}؛ الكمية الأساسية {primaryQuantity:#,##0.####} بتكلفة وحدة {primaryUnitCost:#,##0.####}"
    );
    return (true, "تم ترحيل الكمية المقبولة إلى المخزون بالتكلفة المدخلة يدوياً.");
}

private static async Task<decimal> ResolveBatchToPrimaryUnitFactorAsync(
    System.Data.IDbConnection connection,
    System.Data.IDbTransaction transaction,
    BatchProductUnitMapDto item,
    int unitId)
{
    if (unitId == item.PrimaryUnitID) return 1m;
    if (item.SecondaryUnitID == unitId && item.ConversionFactor.GetValueOrDefault() > 0)
        return item.ConversionFactor!.Value;

    var directFactor = await connection.QueryFirstOrDefaultAsync<decimal?>(@"
SELECT TOP (1) ConversionFactor FROM dbo.UnitConversions
WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
ORDER BY ConversionID DESC",
        new { FromUnitID = unitId, ToUnitID = item.PrimaryUnitID }, transaction);
    if (directFactor.GetValueOrDefault() > 0) return directFactor!.Value;

    var reverseFactor = await connection.QueryFirstOrDefaultAsync<decimal?>(@"
SELECT TOP (1) ConversionFactor FROM dbo.UnitConversions
WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
ORDER BY ConversionID DESC",
        new { FromUnitID = item.PrimaryUnitID, ToUnitID = unitId }, transaction);
    if (reverseFactor.GetValueOrDefault() > 0) return 1m / reverseFactor!.Value;

    throw new InvalidOperationException(
        $"لا يوجد تحويل صريح من وحدة الدفعة إلى وحدة المخزون الأساسية للمنتج {item.PrimaryUnitID}؛ لم يُرحّل أي مخزون.");
}

        public async Task<(bool Success, string Message)> StartStageAsync(
            int batchStageId,
            string? temperature,
            string? speed,
            int? operatorId,
            int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanEdit,
                "لا تملك صلاحية بدء مراحل الدفعات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            BatchStageDbDto? stage = null;
            BatchWorkflowGuardDto? batch = null;

            try
            {
                stage = await connection.QueryFirstOrDefaultAsync<BatchStageDbDto>(@"
SELECT BatchStageID,BatchID,StartTime,StageStatus,StageOrder
FROM dbo.ProductionBatchStages WITH (UPDLOCK,HOLDLOCK)
WHERE BatchStageID=@BatchStageID",
                    new { BatchStageID = batchStageId }, transaction);
                if (stage is null) return (false, "المرحلة غير موجودة");
                if (stage.StageStatus is not (1 or 4))
                    return (false, "لا يمكن بدء المرحلة من حالتها الحالية.");

                batch = await connection.QueryFirstOrDefaultAsync<BatchWorkflowGuardDto>(@"
SELECT pb.BatchID,pb.BatchNumber,pb.ProductionOrderID,pb.ProductItemID,pb.UnitID,pb.BatchStatus,
       po.OrderStatus,po.ProductItemID AS OrderProductItemID,po.PlannedUnitID AS OrderUnitID
FROM dbo.ProductionBatches pb WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ProductionOrders po WITH (UPDLOCK,HOLDLOCK)
    ON po.ProductionOrderID=pb.ProductionOrderID
WHERE pb.BatchID=@BatchID",
                    new { BatchID = stage.BatchID }, transaction);
                if (batch is null) return (false, "دفعة الإنتاج غير موجودة");
                if (batch.BatchStatus is not (1 or 2))
                    return (false, "لا يمكن بدء مرحلة إلا لدفعة مسودة أو قيد التصنيع.");
                if (batch.OrderStatus is not (2 or 3 or 4))
                    return (false, "أمر التصنيع غير نشط؛ لا يمكن بدء مراحل الدفعة.");
                if (batch.ProductItemID != batch.OrderProductItemID || batch.UnitID != batch.OrderUnitID)
                    return (false, "المنتج أو وحدة الدفعة لا يطابقان أمر التصنيع.");
                await ValidateBatchEmployeesAsync(connection, transaction, operatorId);

                var earlierIncomplete = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatchStages WITH (UPDLOCK,HOLDLOCK)
WHERE BatchID=@BatchID AND StageOrder<@StageOrder AND StageStatus<>3",
                    new { BatchID = stage.BatchID, stage.StageOrder }, transaction);
                if (earlierIncomplete > 0)
                    return (false, "أكمل المراحل السابقة قبل بدء هذه المرحلة.");

                var runningStages = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionBatchStages WITH (UPDLOCK,HOLDLOCK)
WHERE BatchID=@BatchID AND StageStatus=2 AND BatchStageID<>@BatchStageID",
                    new { BatchID = stage.BatchID, BatchStageID = batchStageId }, transaction);
                if (runningStages > 0)
                    return (false, "أنه المرحلة الجارية قبل بدء مرحلة أخرى.");

                if (batch.BatchStatus == 1)
                {
                    var started = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatches
SET BatchStatus=2,StartTime=ISNULL(StartTime,GETDATE()),ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE BatchID=@BatchID AND BatchStatus=1",
                        new { BatchID = batch.BatchID, UserID = userId }, transaction);
                    if (started != 1)
                        throw new InvalidOperationException("تغيرت حالة الدفعة أثناء بدء المرحلة.");

                    await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders
SET OrderStatus=4,ActualStartDate=ISNULL(ActualStartDate,GETDATE()),
    ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE ProductionOrderID=@ProductionOrderID AND OrderStatus IN (2,3,4)",
                        new { batch.ProductionOrderID, UserID = userId }, transaction);
                }

                var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatchStages
SET StartTime=GETDATE(),StageStatus=2,Temperature=@Temperature,Speed=@Speed,
    OperatorID=NULLIF(@OperatorID,0)
WHERE BatchStageID=@BatchStageID AND StageStatus IN (1,4)",
                    new
                    {
                        BatchStageID = batchStageId,
                        Temperature = temperature,
                        Speed = speed,
                        OperatorID = operatorId
                    }, transaction);
                if (changed != 1)
                    throw new InvalidOperationException("تغيرت حالة المرحلة أثناء بدء التشغيل.");

                await SyncOrderSummaryFromBatchesAsync(connection, transaction, batch.ProductionOrderID, userId);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); } catch { }
                return (false, ex.Message);
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "ProductionBatchStages",
                recordId: batchStageId.ToString(),
                changedColumns: "StartTime,StageStatus,Temperature,Speed,OperatorID",
                moduleName: "SCR_BATCH",
                description: $"بدء مرحلة في دفعة: {batch!.BatchNumber}"
            );
            return (true, "تم تسجيل بداية المرحلة بنجاح");
        }

        public async Task<(bool Success, string Message)> EndStageAsync(int batchStageId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanEdit,
                "لا تملك صلاحية إنهاء مراحل الدفعات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            BatchStageDbDto? stage = null;
            BatchHeadDbDto? batch = null;
            try
            {
                stage = await connection.QueryFirstOrDefaultAsync<BatchStageDbDto>(@"
SELECT BatchStageID,BatchID,StartTime,StageStatus,StageOrder
FROM dbo.ProductionBatchStages WITH (UPDLOCK,HOLDLOCK)
WHERE BatchStageID=@BatchStageID",
                    new { BatchStageID = batchStageId }, transaction);
                if (stage is null) return (false, "المرحلة غير موجودة");
                if (stage.StageStatus != 2 || !stage.StartTime.HasValue)
                    return (false, "لا يمكن إنهاء مرحلة لم تبدأ أو ليست قيد التنفيذ.");

                batch = await connection.QueryFirstOrDefaultAsync<BatchHeadDbDto>(@"
SELECT BatchID,BatchNumber,ProductionOrderID,BatchStatus
FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
WHERE BatchID=@BatchID",
                    new { BatchID = stage.BatchID }, transaction);
                if (batch is null) return (false, "دفعة الإنتاج غير موجودة");
                if (batch.BatchStatus != 2)
                    return (false, "لا يمكن إنهاء مرحلة لدفعة ليست قيد التصنيع.");
                var orderStatus = await connection.ExecuteScalarAsync<int?>(@"
SELECT OrderStatus FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ProductionOrderID",
                    new { batch.ProductionOrderID }, transaction);
                if (!orderStatus.HasValue || orderStatus.Value is not (2 or 3 or 4))
                    return (false, "أمر التصنيع غير نشط؛ لا يمكن إنهاء المرحلة.");

                var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionBatchStages
SET EndTime=GETDATE(),StageStatus=3
WHERE BatchStageID=@BatchStageID AND StageStatus=2",
                    new { BatchStageID = batchStageId }, transaction);
                if (changed != 1)
                    throw new InvalidOperationException("تغيرت حالة المرحلة أثناء إنهائها.");
                transaction.Commit();
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); } catch { }
                return (false, ex.Message);
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "ProductionBatchStages",
                recordId: batchStageId.ToString(),
                changedColumns: "EndTime,StageStatus",
                moduleName: "SCR_BATCH",
                description: $"إنهاء مرحلة في دفعة: {batch!.BatchNumber}"
            );
            return (true, "تم تسجيل نهاية المرحلة بنجاح");
        }

private async Task UpdateProductionOrderActualsAsync(
    System.Data.IDbConnection connection,
    System.Data.IDbTransaction transaction,
    int productionOrderId,
    int userId)
{
    // Material issue cost is retained at order level. No per-batch or reject/waste
    // allocation is inferred because the current schema has no issue-to-batch link.
    await SyncOrderSummaryFromBatchesAsync(connection, transaction, productionOrderId, userId);
}

        // ==========================================
        // تصدير إكسيل
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<ProductionBatchListDto> items, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BATCH", p => p.CanExport, "لا تملك صلاحية تصدير دفعات التصنيع.");

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