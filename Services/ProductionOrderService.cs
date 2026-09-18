using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public class ProductionOrderService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notification;
        private sealed class ProductionOrderBomCalcDto
{
    public decimal BatchSize { get; set; }
    public decimal OutputQty { get; set; }
    public decimal LaborCostPerBatch { get; set; }
    public decimal OverheadCostPerBatch { get; set; }
}
        public ProductionOrderService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notification)
            : base(configuration)
        {
            _audit = audit;
            _notification = notification;
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
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var orderNumber = await GenerateOrderNumberAsync();

                // جلب بيانات الوصفة لحساب عدد الدفعات
                var bomData = await connection.QueryFirstOrDefaultAsync<ProductionOrderBomCalcDto>(
    @"SELECT
          ISNULL(BatchSize, 0) AS BatchSize,
          ISNULL(OutputQty, 0) AS OutputQty,
          ISNULL(LaborCostPerBatch, 0) AS LaborCostPerBatch,
          ISNULL(OverheadCostPerBatch, 0) AS OverheadCostPerBatch
      FROM dbo.BillOfMaterials
      WHERE BOMID = @BOMID",
    new { order.BOMID }, transaction);

decimal bomBatchSize = bomData?.BatchSize ?? 0m;
decimal multiplier = bomBatchSize > 0 ? order.PlannedQty / bomBatchSize : 1m;

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

                // ===== إشعار =====
                await _notification.CreateNotificationAsync(
                    notificationType: 1,
                    title: "أمر تصنيع جديد",
                    message: $"تم إنشاء أمر تصنيع جديد رقم {orderNumber}",
                    priority: order.OrderPriority == 1 ? (byte)1 : (byte)2,
                    relatedModule: "SCR_PRODORD",
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
        // تعديل أمر تصنيع
        // ==========================================
        public async Task UpdateOrderAsync(ProductionOrderEditDto order, int userId)
        {
            using var connection = CreateConnection();

            var oldOrder = await connection.QueryFirstOrDefaultAsync<ProductionOrderEditDto>(
                @"SELECT ProductionOrderID, OrderNumber, OrderDate, BOMID, ProductItemID,
                         PlannedQty, PlannedUnitID, NumberOfBatches,
                         PlannedStartDate, PlannedEndDate,
                         SourceWarehouseID, TargetWarehouseID,
                         OrderPriority, AssignedTo, SupervisorID, Notes
                  FROM dbo.ProductionOrders
                  WHERE ProductionOrderID = @ID",
                new { ID = order.ProductionOrderID });

            var sql = @"
UPDATE dbo.ProductionOrders SET
    OrderDate = @OrderDate,
    BOMID = @BOMID,
    ProductItemID = @ProductItemID,
    PlannedQty = @PlannedQty,
    PlannedUnitID = @PlannedUnitID,
    NumberOfBatches = @NumberOfBatches,
    PlannedStartDate = @PlannedStartDate,
    PlannedEndDate = @PlannedEndDate,
    SourceWarehouseID = @SourceWarehouseID,
    TargetWarehouseID = @TargetWarehouseID,
    OrderPriority = @OrderPriority,
    AssignedTo = NULLIF(@AssignedTo, 0),
    SupervisorID = NULLIF(@SupervisorID, 0),
    Notes = @Notes,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ProductionOrderID = @ProductionOrderID
  AND OrderStatus = 1";

            await connection.ExecuteAsync(sql, new
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
            });

            // ===== إعادة حساب المواد لو الوصفة أو الكمية اتغيرت =====
            if (oldOrder != null &&
                (oldOrder.BOMID != order.BOMID || oldOrder.PlannedQty != order.PlannedQty))
            {
                using var conn2 = CreateConnection();
                await conn2.OpenAsync();
                using var tx = conn2.BeginTransaction();
                try
                {
                    await CalculateOrderMaterialsAsync(
                        conn2, tx, order.ProductionOrderID, order.BOMID, order.PlannedQty);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }

            // ===== Audit =====
            var changes = new List<string>();
            if (oldOrder != null)
            {
                if (oldOrder.OrderDate != order.OrderDate) changes.Add("OrderDate");
                if (oldOrder.BOMID != order.BOMID) changes.Add("BOMID");
                if (oldOrder.ProductItemID != order.ProductItemID) changes.Add("ProductItemID");
                if (oldOrder.PlannedQty != order.PlannedQty) changes.Add("PlannedQty");
                if (oldOrder.NumberOfBatches != order.NumberOfBatches) changes.Add("NumberOfBatches");
                if (oldOrder.PlannedStartDate != order.PlannedStartDate) changes.Add("PlannedStartDate");
                if (oldOrder.PlannedEndDate != order.PlannedEndDate) changes.Add("PlannedEndDate");
                if (oldOrder.SourceWarehouseID != order.SourceWarehouseID) changes.Add("SourceWarehouseID");
                if (oldOrder.TargetWarehouseID != order.TargetWarehouseID) changes.Add("TargetWarehouseID");
                if (oldOrder.OrderPriority != order.OrderPriority) changes.Add("OrderPriority");
                if (oldOrder.AssignedTo != order.AssignedTo) changes.Add("AssignedTo");
                if (oldOrder.SupervisorID != order.SupervisorID) changes.Add("SupervisorID");
                if (oldOrder.Notes != order.Notes) changes.Add("Notes");
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "ProductionOrders",
                recordId: order.ProductionOrderID.ToString(),
                oldValues: oldOrder != null
                    ? System.Text.Json.JsonSerializer.Serialize(new
                    {
                        oldOrder.BOMID,
                        oldOrder.PlannedQty,
                        oldOrder.OrderPriority
                    })
                    : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                {
                    order.BOMID,
                    order.PlannedQty,
                    order.OrderPriority
                }),
                changedColumns: changes.Any() ? string.Join(",", changes) : null,
                moduleName: "SCR_PRODORD",
                description: $"تعديل أمر تصنيع: {order.OrderNumber}"
            );
        }

        // ==========================================
        // تغيير حالة أمر التصنيع
        // ==========================================
        public async Task<(bool Success, string Message)> ChangeStatusAsync(
            int orderId, int newStatus, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();

            var current = await connection.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT ProductionOrderID, OrderNumber, OrderStatus, ProductItemID
                  FROM dbo.ProductionOrders WHERE ProductionOrderID = @ID",
                new { ID = orderId });

            if (current == null)
                return (false, "أمر التصنيع غير موجود");

            int currentStatus = (int)current.OrderStatus;
            string orderNumber = (string)current.OrderNumber;

            // ===== التحقق من صحة التحويل =====
            var validTransitions = new Dictionary<int, int[]>
            {
                { 1, new[] { 2, 7 } },       // مسودة → معتمد أو ملغي
                { 2, new[] { 3, 7 } },       // معتمد → جاري التجهيز أو ملغي
                { 3, new[] { 4, 7 } },       // جاري التجهيز → قيد التصنيع أو ملغي
                { 4, new[] { 5, 7 } },       // قيد التصنيع → فحص الجودة أو ملغي
                { 5, new[] { 6, 4 } },       // فحص الجودة → مكتمل أو إرجاع للتصنيع
            };

            if (!validTransitions.ContainsKey(currentStatus) ||
                !validTransitions[currentStatus].Contains(newStatus))
            {
                return (false, "لا يمكن الانتقال من هذه الحالة إلى الحالة المطلوبة");
            }

            // ===== التحقق من الاعتماد =====
            if (newStatus == 2)
            {
                var materialCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.ProductionOrderMaterials
                      WHERE ProductionOrderID = @ID",
                    new { ID = orderId });

                if (materialCount == 0)
                    return (false, "لا يمكن اعتماد أمر تصنيع بدون مواد");
            }

            // ===== تحديث الحالة =====
            var updateSql = @"
UPDATE dbo.ProductionOrders SET
    OrderStatus = @NewStatus,
    ActualStartDate = CASE 
        WHEN @NewStatus = 4 AND ActualStartDate IS NULL THEN GETDATE()
        ELSE ActualStartDate 
    END,
    ActualEndDate = CASE 
        WHEN @NewStatus = 6 THEN GETDATE()
        ELSE ActualEndDate 
    END,
    ApprovedBy = CASE 
        WHEN @NewStatus = 2 AND @EmployeeID IS NOT NULL AND @EmployeeID > 0 
        THEN @EmployeeID ELSE ApprovedBy 
    END,
    ApprovedDate = CASE 
        WHEN @NewStatus = 2 THEN GETDATE()
        ELSE ApprovedDate 
    END,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ProductionOrderID = @OrderID";

            await connection.ExecuteAsync(updateSql, new
            {
                OrderID = orderId,
                NewStatus = newStatus,
                EmployeeID = employeeId,
                UserID = userId
            });

            // ===== الأسماء =====
            var statusNames = new Dictionary<int, string>
            {
                {1, "مسودة"}, {2, "معتمد"}, {3, "جاري التجهيز"},
                {4, "قيد التصنيع"}, {5, "فحص الجودة"},
                {6, "مكتمل"}, {7, "ملغي"}
            };
            var newStatusName = statusNames.GetValueOrDefault(newStatus, "");
            var oldStatusName = statusNames.GetValueOrDefault(currentStatus, "");

            // ===== Audit =====
            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "ProductionOrders",
                recordId: orderId.ToString(),
                changedColumns: "OrderStatus",
                moduleName: "SCR_PRODORD",
                description: $"تغيير حالة أمر {orderNumber} من [{oldStatusName}] إلى [{newStatusName}]"
            );

            // ===== إشعار =====
            byte notifPriority = newStatus == 7 ? (byte)1 : (byte)2;
            await _notification.CreateNotificationAsync(
                notificationType: 3,
                title: $"تحديث أمر تصنيع #{orderNumber}",
                message: $"تم تحويل حالة أمر التصنيع إلى: {newStatusName}",
                priority: notifPriority,
                relatedModule: "SCR_PRODORD",
                relatedRecordId: orderId,
                createdBy: userId
            );

            return (true, $"تم تحويل الحالة إلى [{newStatusName}] بنجاح");
        }

        // ==========================================
        // إلغاء أمر التصنيع
        // ==========================================
        public async Task<(bool Success, string Message)> CancelOrderAsync(int orderId, int userId)
        {
            using var connection = CreateConnection();

            var current = await connection.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT OrderNumber, OrderStatus
                  FROM dbo.ProductionOrders WHERE ProductionOrderID = @ID",
                new { ID = orderId });

            if (current == null) return (false, "أمر التصنيع غير موجود");
            if ((int)current.OrderStatus == 6) return (false, "لا يمكن إلغاء أمر مكتمل");
            if ((int)current.OrderStatus == 7) return (false, "الأمر ملغي بالفعل");

            // التحقق من عدم وجود دفعات قيد التصنيع
            var activeBatches = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.ProductionBatches
                  WHERE ProductionOrderID = @ID AND BatchStatus IN (2, 3, 4)",
                new { ID = orderId });

            if (activeBatches > 0)
                return (false, "لا يمكن إلغاء أمر يحتوي على دفعات قيد التصنيع");

            await connection.ExecuteAsync(
                @"UPDATE dbo.ProductionOrders
                  SET OrderStatus = 7, ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE ProductionOrderID = @ID",
                new { ID = orderId, UserID = userId });

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "ProductionOrders",
                recordId: orderId.ToString(),
                moduleName: "SCR_PRODORD",
                description: $"إلغاء أمر تصنيع: {current.OrderNumber}"
            );

            await _notification.CreateNotificationAsync(
                notificationType: 3,
                title: "إلغاء أمر تصنيع",
                message: $"تم إلغاء أمر التصنيع رقم {current.OrderNumber}",
                priority: 1,
                relatedModule: "SCR_PRODORD",
                relatedRecordId: orderId,
                createdBy: userId
            );

            return (true, "تم إلغاء أمر التصنيع بنجاح");
        }

        // ==========================================
        // جلب مواد أمر التصنيع
        // ==========================================
        public async Task<List<ProductionOrderMaterialDto>> GetOrderMaterialsAsync(int orderId)
        {
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
    pom.MaterialType,
    CASE pom.MaterialType
        WHEN 1 THEN N'مادة خام'
        WHEN 2 THEN N'مادة تعبئة'
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
            // جلب حجم الدفعة من الوصفة
            var batchSize = await connection.ExecuteScalarAsync<decimal>(
                @"SELECT ISNULL(BatchSize, 1) FROM dbo.BillOfMaterials WHERE BOMID = @BOMID",
                new { BOMID = bomId }, transaction);

            decimal multiplier = batchSize > 0 ? plannedQty / batchSize : 1;

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
            using var connection = CreateConnection();

            try
            {
                var sql = @"
DECLARE @NextNum NVARCHAR(50);
EXEC dbo.sp_GetNextNumber 'MO', @NextNum OUTPUT;
SELECT @NextNum;";

                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"PRD-{DateTime.Now:yyMMddHHmmss}";
            }
            catch
            {
                return $"MO-{DateTime.Now:yyMMddHHmmss}";
            }
        }

        // ==========================================
        // تصدير إكسيل
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<ProductionOrderListDto> items, int userId)
        {
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
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d143f");
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
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 12)
                      .Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f7ff");

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