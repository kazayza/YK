using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public class ProductionOrderService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notification;
        private readonly ModuleAuthorizationService _authorization;
        private sealed class ProductionOrderBomCalcDto
        {
            public int ProductItemID { get; set; }
            public int OutputUnitID { get; set; }
            public decimal OutputQty { get; set; }
            public decimal LaborCostPerBatch { get; set; }
            public decimal OverheadCostPerBatch { get; set; }
            public int BOMStatus { get; set; }
            public bool IsActive { get; set; }
        }
        private sealed class ProductionOrderWorkflowDto
        {
            public int ProductionOrderID { get; set; }
            public string OrderNumber { get; set; } = "";
            public int OrderStatus { get; set; }
            public int BOMID { get; set; }
            public int ProductItemID { get; set; }
            public int PlannedUnitID { get; set; }
            public decimal PlannedQty { get; set; }
            public int? CreatedBy { get; set; }
        }
        public ProductionOrderService(
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

        private static async Task ValidateOrderReferencesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int productItemId,
            int outputUnitId,
            int? assignedTo,
            int? supervisorId)
        {
            var activeReferences = await connection.ExecuteScalarAsync<int>(@"
SELECT CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.Items WITH (UPDLOCK,HOLDLOCK)
    WHERE ItemID=@ProductItemID AND IsActive=1
) AND EXISTS
(
    SELECT 1 FROM dbo.Units WITH (UPDLOCK,HOLDLOCK)
    WHERE UnitID=@OutputUnitID AND IsActive=1
) THEN 1 ELSE 0 END",
                new { ProductItemID = productItemId, OutputUnitID = outputUnitId }, transaction);
            if (activeReferences != 1)
                throw new InvalidOperationException("المنتج أو وحدة الناتج غير موجودين أو غير نشطين.");

            foreach (var employeeId in new[] { assignedTo, supervisorId })
            {
                if (!employeeId.HasValue || employeeId.Value <= 0) continue;
                var activeEmployee = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.Employees WITH (UPDLOCK,HOLDLOCK)
WHERE EmployeeID=@EmployeeID AND IsActive=1",
                    new { EmployeeID = employeeId.Value }, transaction);
                if (activeEmployee != 1)
                    throw new InvalidOperationException($"الموظف {employeeId.Value} غير موجود أو غير نشط.");
            }
        }

        private static async Task ValidateOrderWarehousesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int? sourceWarehouseId,
            int? targetWarehouseId)
        {
            var warehouseChecks = new[]
            {
                (Id: sourceWarehouseId, ExpectedType: 1, Name: "مخزن المواد"),
                (Id: targetWarehouseId, ExpectedType: 4, Name: "مخزن المنتج التام")
            };
            foreach (var warehouse in warehouseChecks)
            {
                if (!warehouse.Id.HasValue || warehouse.Id.Value <= 0) continue;
                var valid = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK)
WHERE WarehouseID=@WarehouseID AND IsActive=1 AND WarehouseType=@WarehouseType",
                    new { WarehouseID = warehouse.Id.Value, WarehouseType = warehouse.ExpectedType }, transaction);
                if (valid != 1)
                    throw new InvalidOperationException($"{warehouse.Name} غير موجود أو غير نشط أو من نوع غير مناسب.");
            }
        }

        // ==========================================
        // قائمة أوامر التصنيع
        // ==========================================
        public async Task<List<ProductionOrderListDto>> GetOrderListAsync(
            string? search = null,
            int? statusFilter = null,
            int? priorityFilter = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض أوامر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    po.ProductionOrderID,
    po.OrderNumber,
    po.OrderDate,
    po.BOMID,
    b.BOMName,
    b.BOMCode,
    po.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    po.PlannedQty,
    po.PlannedUnitID,
    u.UnitNameAr AS UnitName,
    ISNULL(po.ActualQty, 0) AS ActualQty,
    ISNULL(po.WasteQty, 0) AS WasteQty,
    ISNULL(po.NumberOfBatches, 1) AS NumberOfBatches,
    po.OrderStatus,
    CASE po.OrderStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'جاري التجهيز'
        WHEN 4 THEN N'قيد التصنيع'
        WHEN 5 THEN N'فحص الجودة'
        WHEN 6 THEN N'مكتمل'
        WHEN 7 THEN N'ملغي'
    END AS StatusName,
    ISNULL(po.OrderPriority, 2) AS OrderPriority,
    CASE po.OrderPriority
        WHEN 1 THEN N'عاجل'
        WHEN 2 THEN N'عادي'
        WHEN 3 THEN N'غير عاجل'
    END AS PriorityName,
    po.PlannedStartDate,
    po.PlannedEndDate,
    po.ActualStartDate,
    po.ActualEndDate,
    ISNULL(po.EstimatedTotalCost, 0) AS EstimatedTotalCost,
    ISNULL(po.ActualTotalCost, 0) AS ActualTotalCost,
    ISNULL(sw.WarehouseNameAr, N'') AS SourceWarehouse,
    ISNULL(tw.WarehouseNameAr, N'') AS TargetWarehouse,
    ISNULL(emp.FullNameAr, N'') AS AssignedToName,
    ISNULL((SELECT COUNT(*) FROM dbo.ProductionBatches pb 
            WHERE pb.ProductionOrderID = po.ProductionOrderID), 0) AS BatchCount,
    ISNULL((SELECT COUNT(*) FROM dbo.ProductionBatches pb 
            WHERE pb.ProductionOrderID = po.ProductionOrderID 
            AND pb.BatchStatus = 5), 0) AS CompletedBatchCount
FROM dbo.ProductionOrders po
INNER JOIN dbo.Items i ON po.ProductItemID = i.ItemID
INNER JOIN dbo.BillOfMaterials b ON po.BOMID = b.BOMID
INNER JOIN dbo.Units u ON po.PlannedUnitID = u.UnitID
LEFT JOIN dbo.Warehouses sw ON po.SourceWarehouseID = sw.WarehouseID
LEFT JOIN dbo.Warehouses tw ON po.TargetWarehouseID = tw.WarehouseID
LEFT JOIN dbo.Employees emp ON po.AssignedTo = emp.EmployeeID
WHERE 1 = 1
    AND (@StatusFilter IS NULL OR po.OrderStatus = @StatusFilter)
    AND (@PriorityFilter IS NULL OR po.OrderPriority = @PriorityFilter)
    AND (@FromDate IS NULL OR po.OrderDate >= @FromDate)
    AND (@ToDate IS NULL OR po.OrderDate <= @ToDate)
    AND (
        @Search IS NULL OR @Search = N'' OR
        po.OrderNumber LIKE N'%' + @Search + N'%' OR
        i.ItemCode LIKE N'%' + @Search + N'%' OR
        i.ItemNameAr LIKE N'%' + @Search + N'%' OR
        b.BOMName LIKE N'%' + @Search + N'%'
    )
ORDER BY 
    CASE WHEN po.OrderStatus IN (3,4) THEN 0 ELSE 1 END,
    po.OrderPriority,
    po.OrderDate DESC";

            var result = await connection.QueryAsync<ProductionOrderListDto>(sql, new
            {
                Search = search,
                StatusFilter = statusFilter,
                PriorityFilter = priorityFilter,
                FromDate = fromDate,
                ToDate = toDate
            });

            return result.ToList();
        }

        // ==========================================
        // جلب أمر تصنيع واحد
        // ==========================================
        public async Task<ProductionOrderEditDto?> GetOrderByIdAsync(int orderId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض أوامر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    ProductionOrderID,
    OrderNumber,
    OrderDate,
    BOMID,
    ProductItemID,
    PlannedQty,
    PlannedUnitID,
    ISNULL(ActualQty, 0) AS ActualQty,
    ISNULL(WasteQty, 0) AS WasteQty,
    ISNULL(NumberOfBatches, 1) AS NumberOfBatches,
    PlannedStartDate,
    PlannedEndDate,
    ActualStartDate,
    ActualEndDate,
    SourceWarehouseID,
    TargetWarehouseID,
    OrderStatus,
    ISNULL(OrderPriority, 2) AS OrderPriority,
    ISNULL(EstimatedMaterialCost, 0) AS EstimatedMaterialCost,
    ISNULL(EstimatedLaborCost, 0) AS EstimatedLaborCost,
    ISNULL(EstimatedOverheadCost, 0) AS EstimatedOverheadCost,
    ISNULL(EstimatedTotalCost, 0) AS EstimatedTotalCost,
    ISNULL(ActualMaterialCost, 0) AS ActualMaterialCost,
    ISNULL(ActualLaborCost, 0) AS ActualLaborCost,
    ISNULL(ActualOverheadCost, 0) AS ActualOverheadCost,
    ISNULL(ActualTotalCost, 0) AS ActualTotalCost,
    AssignedTo,
    SupervisorID,
    Notes,
    ApprovedBy,
    ApprovedDate
FROM dbo.ProductionOrders
WHERE ProductionOrderID = @OrderID";

            return await connection.QueryFirstOrDefaultAsync<ProductionOrderEditDto>(
                sql, new { OrderID = orderId });
        }

        // ==========================================
        // إضافة أمر تصنيع جديد
        // ==========================================
        public async Task<int> InsertOrderAsync(ProductionOrderEditDto order, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_PRODORD", p => p.CanAdd, "لا تملك صلاحية إنشاء أوامر التصنيع.");

            if (order.OrderPriority is < 1 or > 3)
                throw new InvalidOperationException("أولوية أمر التصنيع غير صالحة.");
            if (order.PlannedStartDate.HasValue && order.PlannedEndDate.HasValue && order.PlannedEndDate.Value < order.PlannedStartDate.Value)
                throw new InvalidOperationException("تاريخ نهاية الخطة لا يمكن أن يسبق تاريخ بدايتها.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

            try
            {
                var orderNumber = await GenerateOrderNumberAsync();

                // جلب بيانات الوصفة لحساب عدد الدفعات
                var bomData = await connection.QueryFirstOrDefaultAsync<ProductionOrderBomCalcDto>(
    @"SELECT ProductItemID,OutputUnitID,ISNULL(OutputQty,0) AS OutputQty,
          ISNULL(LaborCostPerBatch,0) AS LaborCostPerBatch,
          ISNULL(OverheadCostPerBatch,0) AS OverheadCostPerBatch,
          BOMStatus,ISNULL(IsActive,1) AS IsActive
      FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
      WHERE BOMID=@BOMID",
    new { order.BOMID }, transaction);
                if (order.BOMID <= 0 || order.ProductItemID <= 0 || order.PlannedUnitID <= 0 || order.PlannedQty <= 0)
                    throw new InvalidOperationException("حدد وصفة ومنتجاً ووحدة إنتاج وكمية مخططة موجبة.");
                if (bomData is null || bomData.BOMStatus != 2 || !bomData.IsActive)
                    throw new InvalidOperationException("لا يمكن إنشاء أمر إلا من وصفة نشطة ومعتمدة.");
                if (bomData.ProductItemID != order.ProductItemID || bomData.OutputUnitID != order.PlannedUnitID)
                    throw new InvalidOperationException("المنتج أو وحدة أمر التصنيع لا يطابقان مخرج الوصفة.");
                if (bomData.OutputQty <= 0)
                    throw new InvalidOperationException("كمية المخرج في الوصفة غير صالحة.");
                await ValidateOrderReferencesAsync(connection, transaction, order.ProductItemID, order.PlannedUnitID,
                    order.AssignedTo, order.SupervisorID);
                await ValidateOrderWarehousesAsync(connection, transaction, order.SourceWarehouseID, order.TargetWarehouseID);

                decimal bomOutputQty = bomData.OutputQty;
                decimal multiplier = bomOutputQty > 0 ? order.PlannedQty / bomOutputQty : 1m;

                int numberOfBatches = order.NumberOfBatches > 0
                    ? order.NumberOfBatches
                    : (int)Math.Ceiling((double)multiplier);

                var sql = @"
INSERT INTO dbo.ProductionOrders
(
    OrderNumber, OrderDate, BOMID, ProductItemID,
    PlannedQty, PlannedUnitID, NumberOfBatches,
    PlannedStartDate, PlannedEndDate,
    SourceWarehouseID, TargetWarehouseID,
    OrderStatus, OrderPriority,
    AssignedTo, SupervisorID, Notes,
    CreatedBy, CreatedDate
)
VALUES
(
    @OrderNumber, @OrderDate, @BOMID, @ProductItemID,
    @PlannedQty, @PlannedUnitID, @NumberOfBatches,
    @PlannedStartDate, @PlannedEndDate,
    @SourceWarehouseID, @TargetWarehouseID,
    1, @OrderPriority,
    NULLIF(@AssignedTo, 0), NULLIF(@SupervisorID, 0), @Notes,
    @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    OrderNumber = orderNumber,
                    order.OrderDate,
                    order.BOMID,
                    order.ProductItemID,
                    order.PlannedQty,
                    order.PlannedUnitID,
                    NumberOfBatches = numberOfBatches,
                    order.PlannedStartDate,
                    order.PlannedEndDate,
                    order.SourceWarehouseID,
                    order.TargetWarehouseID,
                    order.OrderPriority,
                    order.AssignedTo,
                    order.SupervisorID,
                    order.Notes,
                    UserID = userId
                }, transaction);

                // ===== حساب المواد المطلوبة من الوصفة =====
                await CalculateOrderMaterialsAsync(connection, transaction, newId, order.BOMID, order.PlannedQty);

                await _notification.CreateForPermissionHoldersAsync(
                    connection, transaction, notificationType: 1,
                    title: "أمر تصنيع جديد",
                    message: $"تم إنشاء أمر تصنيع جديد رقم {orderNumber} وهو بانتظار الاعتماد",
                    priority: order.OrderPriority == 1 ? (byte)1 : (byte)2,
                    permissionModuleCode: "SCR_PRODORD",
                    requiredPermission: ModulePermissionRequirement.Approve,
                    relatedModule: "SCR_PRODORD",
                    relatedRecordId: newId,
                    createdBy: userId,
                    excludeUserId: userId);

                transaction.Commit();

                // ===== Audit =====
                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 1,
                    tableName: "ProductionOrders",
                    recordId: newId.ToString(),
                    newValues: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        OrderNumber = orderNumber,
                        order.BOMID,
                        order.ProductItemID,
                        order.PlannedQty
                    }),
                    moduleName: "SCR_PRODORD",
                    description: $"إنشاء أمر تصنيع جديد: {orderNumber}"
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
        // تعديل أمر تصنيع
        // ==========================================
        public async Task UpdateOrderAsync(ProductionOrderEditDto order, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_PRODORD", p => p.CanEdit,
                "لا تملك صلاحية تعديل أوامر التصنيع.");
            if (order.ProductionOrderID <= 0 || order.BOMID <= 0 || order.ProductItemID <= 0 ||
                order.PlannedQty <= 0 || order.PlannedUnitID <= 0)
                throw new InvalidOperationException("بيانات أمر التصنيع غير مكتملة أو كميته المخططة غير موجبة.");
            if (order.OrderPriority is < 1 or > 3)
                throw new InvalidOperationException("أولوية أمر التصنيع غير صالحة.");
            if (order.PlannedStartDate.HasValue && order.PlannedEndDate.HasValue && order.PlannedEndDate.Value < order.PlannedStartDate.Value)
                throw new InvalidOperationException("تاريخ نهاية الخطة لا يمكن أن يسبق تاريخ بدايتها.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            ProductionOrderEditDto? oldOrder = null;
            try
            {
                oldOrder = await connection.QueryFirstOrDefaultAsync<ProductionOrderEditDto>(@"
SELECT ProductionOrderID,OrderNumber,OrderDate,BOMID,ProductItemID,
       PlannedQty,PlannedUnitID,NumberOfBatches,PlannedStartDate,PlannedEndDate,
       SourceWarehouseID,TargetWarehouseID,OrderPriority,AssignedTo,SupervisorID,Notes,OrderStatus
FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
WHERE ProductionOrderID=@ID",
                    new { ID = order.ProductionOrderID }, transaction);
                if (oldOrder is null)
                    throw new InvalidOperationException("أمر التصنيع غير موجود.");
                if (oldOrder.OrderStatus != 1)
                    throw new InvalidOperationException("لا يمكن تعديل أمر تصنيع بعد اعتماده أو بدء تنفيذه.");

                var bomData = await connection.QueryFirstOrDefaultAsync<ProductionOrderBomCalcDto>(@"
SELECT ProductItemID,OutputUnitID,ISNULL(OutputQty,0) AS OutputQty,
       ISNULL(LaborCostPerBatch,0) AS LaborCostPerBatch,
       ISNULL(OverheadCostPerBatch,0) AS OverheadCostPerBatch,
       BOMStatus,ISNULL(IsActive,1) AS IsActive
FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                    new { order.BOMID }, transaction);
                if (bomData is null || bomData.BOMStatus != 2 || !bomData.IsActive || bomData.OutputQty <= 0)
                    throw new InvalidOperationException("لا يمكن ربط أمر التصنيع إلا بوصفة نشطة ومعتمدة ذات مخرج صالح.");
                if (bomData.ProductItemID != order.ProductItemID || bomData.OutputUnitID != order.PlannedUnitID)
                    throw new InvalidOperationException("المنتج أو وحدة أمر التصنيع لا يطابقان مخرج الوصفة.");
                await ValidateOrderReferencesAsync(connection, transaction, order.ProductItemID, order.PlannedUnitID,
                    order.AssignedTo, order.SupervisorID);
                await ValidateOrderWarehousesAsync(connection, transaction, order.SourceWarehouseID, order.TargetWarehouseID);

                var changed = await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders SET
    OrderDate=@OrderDate,BOMID=@BOMID,ProductItemID=@ProductItemID,
    PlannedQty=@PlannedQty,PlannedUnitID=@PlannedUnitID,NumberOfBatches=@NumberOfBatches,
    PlannedStartDate=@PlannedStartDate,PlannedEndDate=@PlannedEndDate,
    SourceWarehouseID=@SourceWarehouseID,TargetWarehouseID=@TargetWarehouseID,
    OrderPriority=@OrderPriority,AssignedTo=NULLIF(@AssignedTo,0),
    SupervisorID=NULLIF(@SupervisorID,0),Notes=@Notes,
    ModifiedBy=@UserID,ModifiedDate=GETDATE()
WHERE ProductionOrderID=@ProductionOrderID AND OrderStatus=1",
                    new
                    {
                        order.ProductionOrderID,
                        order.OrderDate,
                        order.BOMID,
                        order.ProductItemID,
                        order.PlannedQty,
                        order.PlannedUnitID,
                        order.NumberOfBatches,
                        order.PlannedStartDate,
                        order.PlannedEndDate,
                        order.SourceWarehouseID,
                        order.TargetWarehouseID,
                        order.OrderPriority,
                        order.AssignedTo,
                        order.SupervisorID,
                        order.Notes,
                        UserID = userId
                    }, transaction);
                if (changed != 1)
                    throw new InvalidOperationException("تغيرت حالة أمر التصنيع أثناء الحفظ؛ أعد تحميله.");

                if (oldOrder.BOMID != order.BOMID || oldOrder.PlannedQty != order.PlannedQty)
                {
                    await CalculateOrderMaterialsAsync(
                        connection, transaction, order.ProductionOrderID, order.BOMID, order.PlannedQty);
                }

                await _notification.CreateForPermissionHoldersAsync(
                    connection, transaction, notificationType: 3,
                    title: $"تعديل مسودة أمر تصنيع #{oldOrder.OrderNumber}",
                    message: $"تم تعديل بيانات مسودة أمر التصنيع {oldOrder.OrderNumber}",
                    priority: 2, permissionModuleCode: "SCR_PRODORD",
                    requiredPermission: ModulePermissionRequirement.View,
                    relatedModule: "SCR_PRODORD",
                    relatedRecordId: order.ProductionOrderID,
                    createdBy: userId,
                    excludeUserId: userId);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            var changes = new List<string>();
            if (oldOrder!.OrderDate != order.OrderDate) changes.Add("OrderDate");
            if (oldOrder.BOMID != order.BOMID) changes.Add("BOMID");
            if (oldOrder.ProductItemID != order.ProductItemID) changes.Add("ProductItemID");
            if (oldOrder.PlannedQty != order.PlannedQty) changes.Add("PlannedQty");
            if (oldOrder.PlannedUnitID != order.PlannedUnitID) changes.Add("PlannedUnitID");
            if (oldOrder.NumberOfBatches != order.NumberOfBatches) changes.Add("NumberOfBatches");
            if (oldOrder.PlannedStartDate != order.PlannedStartDate) changes.Add("PlannedStartDate");
            if (oldOrder.PlannedEndDate != order.PlannedEndDate) changes.Add("PlannedEndDate");
            if (oldOrder.SourceWarehouseID != order.SourceWarehouseID) changes.Add("SourceWarehouseID");
            if (oldOrder.TargetWarehouseID != order.TargetWarehouseID) changes.Add("TargetWarehouseID");
            if (oldOrder.OrderPriority != order.OrderPriority) changes.Add("OrderPriority");
            if (oldOrder.AssignedTo != order.AssignedTo) changes.Add("AssignedTo");
            if (oldOrder.SupervisorID != order.SupervisorID) changes.Add("SupervisorID");
            if (oldOrder.Notes != order.Notes) changes.Add("Notes");

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "ProductionOrders",
                recordId: order.ProductionOrderID.ToString(),
                oldValues: System.Text.Json.JsonSerializer.Serialize(new
                {
                    oldOrder.BOMID,oldOrder.ProductItemID,oldOrder.PlannedQty,oldOrder.PlannedUnitID,
                    oldOrder.OrderPriority,oldOrder.SourceWarehouseID,oldOrder.TargetWarehouseID
                }),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                {
                    order.BOMID,order.ProductItemID,order.PlannedQty,order.PlannedUnitID,
                    order.OrderPriority,order.SourceWarehouseID,order.TargetWarehouseID
                }),
                changedColumns: changes.Count > 0 ? string.Join(",", changes) : null,
                moduleName: "SCR_PRODORD",
                description: $"تعديل أمر تصنيع: {oldOrder.OrderNumber}"
            );
        }

        // ==========================================
        // تغيير حالة أمر التصنيع
        // ==========================================
        public async Task<(bool Success, string Message)> ChangeStatusAsync(
            int orderId, int newStatus, int userId, int? employeeId = null)
        {
            if (newStatus == 7)
                return (false, "استخدم مسار إلغاء الأمر؛ فهو يتحقق من الصرف والدفعات.");
            if (newStatus == 6)
                return (false, "اكتمال الأمر يُسجل من مسار استلام الدفعات المقبولة.");
            if (newStatus is not (2 or 3 or 4 or 5))
                return (false, "حالة أمر التصنيع المطلوبة غير صالحة.");

            await _authorization.RequireUserPermissionAsync(userId, "SCR_PRODORD",
                p => newStatus == 2 ? p.CanApprove : (newStatus is 3 or 4 or 5) && p.CanEdit,
                "لا تملك صلاحية تغيير حالة أمر التصنيع.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            ProductionOrderWorkflowDto? current = null;
            var statusNames = new Dictionary<int, string>
            {
                {1, "مسودة"}, {2, "معتمد"}, {3, "جاري التجهيز"},
                {4, "قيد التصنيع"}, {5, "فحص الجودة"}, {6, "مكتمل"}, {7, "ملغي"}
            };

            try
            {
                current = await connection.QueryFirstOrDefaultAsync<ProductionOrderWorkflowDto>(
                    @"SELECT ProductionOrderID,OrderNumber,OrderStatus,BOMID,ProductItemID,PlannedUnitID,PlannedQty,CreatedBy
                      FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ID",
                    new { ID = orderId }, transaction);
                if (current is null) return (false, "أمر التصنيع غير موجود");

                var validTransitions = new Dictionary<int, int[]>
                {
                    { 1, new[] { 2 } },
                    { 2, new[] { 3 } },
                    { 3, new[] { 4 } },
                    { 4, new[] { 5 } },
                    { 5, new[] { 4 } }
                };
                if (!validTransitions.TryGetValue(current.OrderStatus, out var allowed) || !allowed.Contains(newStatus))
                    return (false, "لا يمكن الانتقال من هذه الحالة إلى الحالة المطلوبة");

                if (newStatus == 2)
                {
                    var bom = await connection.QueryFirstOrDefaultAsync<ProductionOrderBomCalcDto>(@"
SELECT ProductItemID,OutputUnitID,ISNULL(OutputQty,0) AS OutputQty,
       ISNULL(LaborCostPerBatch,0) AS LaborCostPerBatch,
       ISNULL(OverheadCostPerBatch,0) AS OverheadCostPerBatch,
       BOMStatus,ISNULL(IsActive,1) AS IsActive
FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                        new { current.BOMID }, transaction);
                    if (bom is null || bom.BOMStatus != 2 || !bom.IsActive || bom.OutputQty <= 0)
                        return (false, "لا يمكن اعتماد أمر التصنيع دون وصفة نشطة ومعتمدة ذات مخرج صالح.");
                    if (bom.ProductItemID != current.ProductItemID || bom.OutputUnitID != current.PlannedUnitID)
                        return (false, "المنتج أو وحدة أمر التصنيع لم يعودا يطابقان مخرج الوصفة.");
                    var activeReferences = await connection.ExecuteScalarAsync<int>(@"
SELECT CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.Items WITH (UPDLOCK,HOLDLOCK)
    WHERE ItemID=@ProductItemID AND IsActive=1
) AND EXISTS
(
    SELECT 1 FROM dbo.Units WITH (UPDLOCK,HOLDLOCK)
    WHERE UnitID=@OutputUnitID AND IsActive=1
) THEN 1 ELSE 0 END",
                        new { ProductItemID = current.ProductItemID, OutputUnitID = current.PlannedUnitID }, transaction);
                    if (activeReferences != 1)
                        return (false, "المنتج أو وحدة ناتج الوصفة غير نشطين؛ لا يمكن اعتماد الأمر.");

                    var invalidMaterialCount = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.ProductionOrderMaterials WITH (UPDLOCK,HOLDLOCK)
                          WHERE ProductionOrderID=@ID AND RequiredQty<=0",
                        new { ID = orderId }, transaction);
                    var materialCount = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.ProductionOrderMaterials WITH (UPDLOCK,HOLDLOCK)
                          WHERE ProductionOrderID=@ID AND RequiredQty>0",
                        new { ID = orderId }, transaction);
                    if (materialCount == 0 || invalidMaterialCount > 0)
                        return (false, "لا يمكن اعتماد أمر تصنيع بلا مواد مطلوبة صالحة.");
                }
                if (newStatus == 5)
                {
                    var batches = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
                          WHERE ProductionOrderID=@ID",
                        new { ID = orderId }, transaction);
                    if (batches == 0)
                        return (false, "لا يمكن نقل أمر التصنيع إلى فحص الجودة بلا دفعات.");
                    var unfinished = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
                          WHERE ProductionOrderID=@ID AND BatchStatus IN (1,2)",
                        new { ID = orderId }, transaction);
                    if (unfinished > 0)
                        return (false, "أنه الدفعات المفتوحة أو أوقفها قبل نقل الأمر إلى فحص الجودة.");
                    var readyForQc = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
                          WHERE ProductionOrderID=@ID AND BatchStatus IN (3,4,5)",
                        new { ID = orderId }, transaction);
                    if (readyForQc == 0)
                        return (false, "لا توجد دفعة مكتملة أو مقبولة للانتقال إلى فحص الجودة.");
                }

                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.ProductionOrders SET
                          OrderStatus=@NewStatus,
                          ActualStartDate=CASE WHEN @NewStatus=4 AND ActualStartDate IS NULL THEN GETDATE() ELSE ActualStartDate END,
                          ApprovedBy=CASE WHEN @NewStatus=2 AND @EmployeeID IS NOT NULL AND @EmployeeID>0 THEN @EmployeeID ELSE ApprovedBy END,
                          ApprovedDate=CASE WHEN @NewStatus=2 THEN GETDATE() ELSE ApprovedDate END,
                          ModifiedBy=@UserID,ModifiedDate=GETDATE()
                      WHERE ProductionOrderID=@OrderID AND OrderStatus=@CurrentStatus",
                    new
                    {
                        OrderID = orderId,
                        CurrentStatus = current.OrderStatus,
                        NewStatus = newStatus,
                        EmployeeID = employeeId,
                        UserID = userId
                    }, transaction);
                if (affected != 1)
                    throw new InvalidOperationException("تغيرت حالة أمر التصنيع؛ أعد تحميله قبل التعديل.");

                await _notification.MarkRelatedAsActionedAsync(
                    connection, transaction, "SCR_PRODORD", orderId);

                if (current.CreatedBy.HasValue)
                {
                    await _notification.CreateForAuthorizedUserAsync(
                        connection, transaction, notificationType: 3,
                        title: $"تحديث أمر تصنيع #{current.OrderNumber}",
                        message: $"تم تحويل حالة أمر التصنيع إلى: {statusNames[newStatus]}",
                        priority: 2, targetUserId: current.CreatedBy.Value,
                        permissionModuleCode: "SCR_PRODORD", relatedModule: "SCR_PRODORD",
                        relatedRecordId: orderId, createdBy: userId);
                }
                if (newStatus == 2)
                {
                    await _notification.CreateForPermissionHoldersAsync(
                        connection, transaction, notificationType: 1,
                        title: $"أمر تصنيع معتمد #{current.OrderNumber}",
                        message: $"اعتمد أمر التصنيع {current.OrderNumber} وأصبح جاهزاً لإنشاء أذون صرف المواد.",
                        priority: 2, permissionModuleCode: "SCR_MATISS",
                        requiredPermission: ModulePermissionRequirement.Add,
                        relatedModule: "SCR_MATISS", relatedRecordId: 0, createdBy: userId,
                        excludeUserId: userId);
                }

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
                tableName: "ProductionOrders",
                recordId: orderId.ToString(),
                changedColumns: "OrderStatus",
                moduleName: "SCR_PRODORD",
                description: $"تغيير حالة أمر {current!.OrderNumber} من [{statusNames[current.OrderStatus]}] إلى [{statusNames[newStatus]}]"
            );

            return (true, $"تم تحويل الحالة إلى [{statusNames[newStatus]}] بنجاح");
        }

        // ==========================================
        // إلغاء أمر التصنيع
        // ==========================================
        public async Task<(bool Success, string Message)> CancelOrderAsync(int orderId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_PRODORD", p => p.CanCancel,
                "لا تملك صلاحية إلغاء أوامر التصنيع.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            ProductionOrderWorkflowDto? current = null;
            try
            {
                current = await connection.QueryFirstOrDefaultAsync<ProductionOrderWorkflowDto>(
                    @"SELECT ProductionOrderID,OrderNumber,OrderStatus,BOMID,ProductItemID,PlannedUnitID,PlannedQty,CreatedBy
                      FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ID",
                    new { ID = orderId }, transaction);
                if (current is null) return (false, "أمر التصنيع غير موجود");
                if (current.OrderStatus == 6) return (false, "لا يمكن إلغاء أمر مكتمل");
                if (current.OrderStatus == 7) return (false, "الأمر ملغي بالفعل");

                var pendingIssues = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.MaterialIssueNotes WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ID AND IssueStatus=1",
                    new { ID = orderId }, transaction);
                if (pendingIssues > 0)
                    return (false, "ألغِ مسودات أذون الصرف المرتبطة بالأمر قبل إلغائه.");

                var approvedIssues = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.MaterialIssueNotes WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ID AND IssueStatus=2",
                    new { ID = orderId }, transaction);
                if (approvedIssues > 0)
                    return (false, "لا يمكن إلغاء أمر صُرفت له مواد معتمدة؛ يلزم مستند عكسي يعيد المخزون أولاً.");

                var linkedBatches = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.ProductionBatches WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ID",
                    new { ID = orderId }, transaction);
                if (linkedBatches > 0)
                    return (false, "لا يمكن إلغاء أمر مرتبط بدفعات؛ يجب تسوية الدفعات أولاً حتى لا تبقى دفعات مسودة تابعة لأمر ملغي.");

                var changed = await connection.ExecuteAsync(
                    @"UPDATE dbo.ProductionOrders
                      SET OrderStatus=7,ModifiedBy=@UserID,ModifiedDate=GETDATE()
                      WHERE ProductionOrderID=@ID AND OrderStatus=@CurrentStatus",
                    new { ID = orderId, CurrentStatus = current.OrderStatus, UserID = userId }, transaction);
                if (changed != 1)
                    throw new InvalidOperationException("تغيرت حالة أمر التصنيع؛ أعد تحميله قبل الإلغاء.");

                await _notification.MarkRelatedAsActionedAsync(
                    connection, transaction, "SCR_PRODORD", orderId);

                if (current.CreatedBy.HasValue)
                {
                    await _notification.CreateForAuthorizedUserAsync(
                        connection, transaction, notificationType: 3,
                        title: $"إلغاء أمر تصنيع #{current.OrderNumber}",
                        message: $"تم إلغاء أمر التصنيع رقم {current.OrderNumber}",
                        priority: 1, targetUserId: current.CreatedBy.Value,
                        permissionModuleCode: "SCR_PRODORD", relatedModule: "SCR_PRODORD",
                        relatedRecordId: orderId, createdBy: userId);
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); } catch { }
                return (false, ex.Message);
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "ProductionOrders",
                recordId: orderId.ToString(),
                moduleName: "SCR_PRODORD",
                description: $"إلغاء أمر تصنيع: {current!.OrderNumber}"
            );

            return (true, "تم إلغاء أمر التصنيع بنجاح");
        }

        // ==========================================
        // جلب مواد أمر التصنيع
        // ==========================================
        public async Task<List<ProductionOrderMaterialDto>> GetOrderMaterialsAsync(int orderId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض مواد أمر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    pom.POMaterialID,
    pom.ProductionOrderID,
    pom.LineNumber,
    pom.ItemID,
    i.ItemCode,
    i.ItemNameAr AS ItemName,
    pom.UnitID,
    u.UnitNameAr AS UnitName,
    CASE WHEN pom.MaterialType = 1 AND i.ItemType = 4 THEN 3 ELSE pom.MaterialType END AS MaterialType,
    CASE
        WHEN pom.MaterialType = 1 AND i.ItemType = 4 THEN N'منتج تام'
        WHEN pom.MaterialType = 1 THEN N'مادة خام'
        WHEN pom.MaterialType = 2 THEN N'مادة تعبئة'
        ELSE N'غير معروف'
    END AS MaterialTypeName,
    ISNULL(pom.RequiredQty, 0) AS RequiredQty,
    ISNULL(pom.IssuedQty, 0) AS IssuedQty,
    ISNULL(pom.ReturnedQty, 0) AS ReturnedQty,
    (ISNULL(pom.IssuedQty, 0) - ISNULL(pom.ReturnedQty, 0)) AS ConsumedQty,
    ISNULL(pom.UnitCost, 0) AS UnitCost,
    ((ISNULL(pom.IssuedQty, 0) - ISNULL(pom.ReturnedQty, 0)) * ISNULL(pom.UnitCost, 0)) AS LineCost,
    ISNULL(pom.AvailableStock, 0) AS AvailableStock,
    ISNULL(pom.LineStatus, 1) AS LineStatus,
    CASE pom.LineStatus
        WHEN 1 THEN N'في الانتظار'
        WHEN 2 THEN N'تم الصرف'
        WHEN 3 THEN N'صرف جزئي'
    END AS LineStatusName,
    pom.BOMDetailID,
    pom.Notes
FROM dbo.ProductionOrderMaterials pom
INNER JOIN dbo.Items i ON pom.ItemID = i.ItemID
INNER JOIN dbo.Units u ON pom.UnitID = u.UnitID
WHERE pom.ProductionOrderID = @OrderID
ORDER BY pom.MaterialType, pom.LineNumber";

            var result = await connection.QueryAsync<ProductionOrderMaterialDto>(
                sql, new { OrderID = orderId });
            return result.ToList();
        }

        // ==========================================
        // حساب المواد المطلوبة من الوصفة
        // ==========================================
        private async Task CalculateOrderMaterialsAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int orderId,
            int bomId,
            decimal plannedQty)
        {
            // PlannedQty is entered in the BOM output unit, so scale from the expected output quantity.
            var outputQty = await connection.ExecuteScalarAsync<decimal>(
                @"SELECT ISNULL(OutputQty, 0) FROM dbo.BillOfMaterials WHERE BOMID = @BOMID",
                new { BOMID = bomId }, transaction);

            decimal multiplier = outputQty > 0 ? plannedQty / outputQty : 1;

            // حذف المواد القديمة
            await connection.ExecuteAsync(
                @"DELETE FROM dbo.ProductionOrderMaterials
                  WHERE ProductionOrderID = @OrderID",
                new { OrderID = orderId }, transaction);

            // إدراج المواد من الوصفة
            await connection.ExecuteAsync(@"
INSERT INTO dbo.ProductionOrderMaterials
(
    ProductionOrderID, LineNumber, ItemID, UnitID, MaterialType,
    RequiredQty, UnitCost, BOMDetailID, Notes
)
SELECT
    @OrderID,
    ROW_NUMBER() OVER (ORDER BY bd.MaterialType, bd.Sequence, bd.LineNumber),
    bd.ItemID,
    bd.UnitID,
    bd.MaterialType,
    ROUND(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0) * @Multiplier, 4),
    ISNULL(i.AverageCost, ISNULL(i.LastPurchasePrice, ISNULL(i.StandardCost, 0))),
    bd.BOMDetailID,
    bd.Notes
FROM dbo.BOMDetails bd
INNER JOIN dbo.Items i ON bd.ItemID = i.ItemID
WHERE bd.BOMID = @BOMID",
                new
                {
                    OrderID = orderId,
                    BOMID = bomId,
                    Multiplier = multiplier
                }, transaction);

            // تحديث التكاليف التقديرية
            await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders SET
    EstimatedMaterialCost = ISNULL((
        SELECT SUM(RequiredQty * UnitCost)
        FROM dbo.ProductionOrderMaterials
        WHERE ProductionOrderID = @OrderID), 0),
    EstimatedLaborCost = ISNULL((
        SELECT LaborCostPerBatch * @Multiplier
        FROM dbo.BillOfMaterials WHERE BOMID = @BOMID), 0),
    EstimatedOverheadCost = ISNULL((
        SELECT OverheadCostPerBatch * @Multiplier
        FROM dbo.BillOfMaterials WHERE BOMID = @BOMID), 0),
    ModifiedDate = GETDATE()
WHERE ProductionOrderID = @OrderID",
                new { OrderID = orderId, BOMID = bomId, Multiplier = multiplier },
                transaction);

            // تحديث الإجمالي
            await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders SET
    EstimatedTotalCost = ISNULL(EstimatedMaterialCost, 0)
                       + ISNULL(EstimatedLaborCost, 0)
                       + ISNULL(EstimatedOverheadCost, 0)
WHERE ProductionOrderID = @OrderID",
                new { OrderID = orderId }, transaction);
        }

        // ==========================================
        // معلومات النظام
        // ==========================================
        public async Task<ProductionOrderAuditDto?> GetOrderAuditAsync(int orderId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض سجل أمر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    ISNULL(uc.FullName, N'غير محدد') AS CreatedByName,
    po.CreatedDate,
    ISNULL(um.FullName, N'') AS ModifiedByName,
    po.ModifiedDate,
    ISNULL(ea.FullNameAr, N'') AS ApprovedByName,
    po.ApprovedDate,
    ISNULL(eass.FullNameAr, N'') AS AssignedToName,
    ISNULL(esup.FullNameAr, N'') AS SupervisorName
FROM dbo.ProductionOrders po
LEFT JOIN dbo.SystemUsers uc ON po.CreatedBy = uc.UserID
LEFT JOIN dbo.SystemUsers um ON po.ModifiedBy = um.UserID
LEFT JOIN dbo.Employees ea ON po.ApprovedBy = ea.EmployeeID
LEFT JOIN dbo.Employees eass ON po.AssignedTo = eass.EmployeeID
LEFT JOIN dbo.Employees esup ON po.SupervisorID = esup.EmployeeID
WHERE po.ProductionOrderID = @OrderID";

            return await connection.QueryFirstOrDefaultAsync<ProductionOrderAuditDto>(
                sql, new { OrderID = orderId });
        }

        // ==========================================
        // ملخص التكاليف
        // ==========================================
        public async Task<ProductionOrderCostSummaryDto> GetCostSummaryAsync(int orderId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض تكلفة أمر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    ISNULL(EstimatedMaterialCost, 0) AS EstimatedMaterialCost,
    ISNULL(EstimatedLaborCost, 0) AS EstimatedLaborCost,
    ISNULL(EstimatedOverheadCost, 0) AS EstimatedOverheadCost,
    ISNULL(EstimatedTotalCost, 0) AS EstimatedTotalCost,
    ISNULL(ActualMaterialCost, 0) AS ActualMaterialCost,
    ISNULL(ActualLaborCost, 0) AS ActualLaborCost,
    ISNULL(ActualOverheadCost, 0) AS ActualOverheadCost,
    ISNULL(ActualTotalCost, 0) AS ActualTotalCost
FROM dbo.ProductionOrders
WHERE ProductionOrderID = @OrderID";

            return await connection.QueryFirstOrDefaultAsync<ProductionOrderCostSummaryDto>(
                sql, new { OrderID = orderId })
                ?? new ProductionOrderCostSummaryDto();
        }

        // ==========================================
        // Lookups
        // ==========================================
        public async Task<List<ProductionLookupDto>> GetActiveBOMsLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض بيانات أوامر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    b.BOMID AS Id,
    (b.BOMCode + N' - ' + b.BOMName + N' (v' + CAST(b.BOMVersion AS NVARCHAR) + N')') AS Name
FROM dbo.BillOfMaterials b
WHERE b.BOMStatus = 2 AND ISNULL(b.IsActive, 1) = 1
ORDER BY b.BOMName";

            return (await connection.QueryAsync<ProductionLookupDto>(sql)).ToList();
        }

        public async Task<List<ProductionLookupDto>> GetWarehousesLookupAsync(int? warehouseType = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض بيانات أوامر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    WarehouseID AS Id,
    (WarehouseCode + N' - ' + WarehouseNameAr) AS Name
FROM dbo.Warehouses
WHERE IsActive = 1
  AND (@WarehouseType IS NULL OR WarehouseType = @WarehouseType)
ORDER BY WarehouseNameAr";

            return (await connection.QueryAsync<ProductionLookupDto>(
                sql, new { WarehouseType = warehouseType })).ToList();
        }

        public async Task<List<ProductionLookupDto>> GetEmployeesLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض بيانات أوامر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    EmployeeID AS Id,
    (EmployeeCode + N' - ' + FullNameAr) AS Name
FROM dbo.Employees
WHERE IsActive = 1
ORDER BY FullNameAr";

            return (await connection.QueryAsync<ProductionLookupDto>(sql)).ToList();
        }

        public async Task<List<ProductionLookupDto>> GetUnitsLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض بيانات أوامر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT UnitID AS Id, UnitNameAr AS Name
FROM dbo.Units WHERE IsActive = 1
ORDER BY UnitNameAr";

            return (await connection.QueryAsync<ProductionLookupDto>(sql)).ToList();
        }

        // ===== جلب بيانات الوصفة عند اختيارها =====
        public async Task<dynamic?> GetBOMInfoAsync(int bomId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView, "لا تملك صلاحية عرض بيانات أوامر التصنيع.");

            using var connection = CreateConnection();

            return await connection.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT
    b.BOMID,
    b.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    b.BatchSize,
    b.BatchUnitID,
    bu.UnitNameAr AS BatchUnitName,
    b.OutputQty,
    b.OutputUnitID,
    ou.UnitNameAr AS OutputUnitName
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items i ON b.ProductItemID = i.ItemID
INNER JOIN dbo.Units bu ON b.BatchUnitID = bu.UnitID
INNER JOIN dbo.Units ou ON b.OutputUnitID = ou.UnitID
WHERE b.BOMID = @BOMID",
                new { BOMID = bomId });
        }

        // ==========================================
        // توليد رقم الأمر
        // ==========================================
        public async Task<string> GenerateOrderNumberAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_PRODORD", p => p.CanView || p.CanAdd, "لا تملك صلاحية إنشاء رقم أمر تصنيع.");
            using var connection = CreateConnection();

            try
            {
                var sql = @"
DECLARE @NextNum NVARCHAR(50);
EXEC dbo.sp_GetNextNumber 'MO', @NextNum OUTPUT;
SELECT @NextNum;";

                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لأمر التصنيع.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم أمر التصنيع (MO). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل MO والإجراء المخزن.", ex);
            }
        }

        // ==========================================
        // تصدير إكسيل
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<ProductionOrderListDto> items, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_PRODORD", p => p.CanExport, "لا تملك صلاحية تصدير أوامر التصنيع.");

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("أوامر التصنيع");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            // العنوان
            ws.Cell(1, 1).Value = "تقرير أوامر التصنيع — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 12).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 12).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 12).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 12).Style.Font.FontColor = XLColor.Gray;
            ws.Range(2, 1, 2, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 4;
            var headers = new[]
            {
                "#", "رقم الأمر", "التاريخ", "المنتج", "الوصفة",
                "الكمية المخططة", "الوحدة", "الأولوية", "الدفعات",
                "التكلفة التقديرية", "مخزن الصرف", "الحالة"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0A0F1E");
                cell.Style.Font.FontColor = XLColor.FromHtml("#F5E6C8");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = headerRow + 1;
            int num = 0;

            foreach (var item in items)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = item.OrderNumber;
                ws.Cell(row, 3).Value = item.OrderDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = item.ProductName;
                ws.Cell(row, 5).Value = item.BOMName;
                ws.Cell(row, 6).Value = item.PlannedQty;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.####";
                ws.Cell(row, 7).Value = item.UnitName;
                ws.Cell(row, 8).Value = item.PriorityName;
                ws.Cell(row, 9).Value = item.NumberOfBatches;
                ws.Cell(row, 10).Value = item.EstimatedTotalCost;
                ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 11).Value = item.SourceWarehouse;
                ws.Cell(row, 12).Value = item.StatusName;

                for (int i = 1; i <= 12; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e8e2d0");
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 12)
                      .Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");

                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(4).Width = 28;
            ws.Column(5).Width = 28;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 5,
                tableName: "ProductionOrders",
                moduleName: "SCR_PRODORD",
                description: $"تصدير {items.Count} أمر تصنيع إلى Excel"
            );

            return stream.ToArray();
        }
    }
}