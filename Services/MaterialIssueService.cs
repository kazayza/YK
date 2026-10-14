using System.Data;
using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public partial class MaterialIssueService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notification;
        private readonly ModuleAuthorizationService _authorization;

        private sealed class IssueApproveHeadDto
        {
            public int IssueID { get; set; }
            public string IssueNumber { get; set; } = "";
            public int IssueStatus { get; set; }
            public int ProductionOrderID { get; set; }
            public int WarehouseID { get; set; }
            public int IssueType { get; set; }
            public int? CreatedBy { get; set; }
        }

        private sealed class IssueApproveDetailDto
        {
            public int IssueDetailID { get; set; }
            public int LineNumber { get; set; }
            public int? POMaterialID { get; set; }
            public int ItemID { get; set; }
            public int UnitID { get; set; }
            public decimal IssuedQty { get; set; }
            public decimal UnitCost { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public string? Notes { get; set; }
        }

        private sealed class POMaterialIssueState
        {
            public int POMaterialID { get; set; }
            public int ProductionOrderID { get; set; }
            public int ItemID { get; set; }
            public int UnitID { get; set; }
            public decimal RequiredQty { get; set; }
            public decimal AlreadyIssuedQty { get; set; }
        }

        private sealed class InventoryBalanceDto
        {
            public int BalanceID { get; set; }
            public int ItemID { get; set; }
            public int WarehouseID { get; set; }
            public decimal CurrentQty { get; set; }
            public decimal ReservedQty { get; set; }
            public decimal AvailableQty { get; set; }
            public decimal AverageCost { get; set; }
            public decimal TotalValue { get; set; }
        }

        public MaterialIssueService(
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
        // قائمة أذون الصرف
        // ==========================================
        public async Task<List<MaterialIssueListDto>> GetListAsync(
            string? search = null,
            int? statusFilter = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض أذون صرف التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    n.IssueID AS MaterialIssueID,
    n.IssueNumber,
    n.IssueDate,
    n.ProductionOrderID,
    po.OrderNumber,
    i.ItemNameAr AS ProductName,
    n.WarehouseID AS SourceWarehouseID,
    w.WarehouseNameAr AS WarehouseName,
    n.IssueStatus,
    CASE n.IssueStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'ملغي'
    END AS StatusName,
    ISNULL(ag.TotalItems, 0) AS TotalItems,
    ISNULL(ag.TotalQty, 0) AS TotalQty,
    ISNULL(ag.TotalCost, 0) AS TotalCost,
    ISNULL(uc.FullName, N'') AS CreatedByName
FROM dbo.MaterialIssueNotes n
INNER JOIN dbo.ProductionOrders po ON n.ProductionOrderID = po.ProductionOrderID
INNER JOIN dbo.Items i ON po.ProductItemID = i.ItemID
INNER JOIN dbo.Warehouses w ON n.WarehouseID = w.WarehouseID
LEFT JOIN dbo.SystemUsers uc ON n.CreatedBy = uc.UserID
LEFT JOIN (
    SELECT
        IssueID,
        COUNT(*) AS TotalItems,
        SUM(IssuedQty) AS TotalQty,
        SUM(ISNULL(LineCost, IssuedQty * ISNULL(UnitCost, 0))) AS TotalCost
    FROM dbo.MaterialIssueDetails
    GROUP BY IssueID
) ag ON n.IssueID = ag.IssueID
WHERE 1 = 1
    AND (@StatusFilter IS NULL OR n.IssueStatus = @StatusFilter)
    AND (@FromDate IS NULL OR n.IssueDate >= @FromDate)
    AND (@ToDate IS NULL OR n.IssueDate <= @ToDate)
    AND (
        @Search IS NULL OR @Search = N'' OR
        n.IssueNumber LIKE N'%' + @Search + N'%' OR
        po.OrderNumber LIKE N'%' + @Search + N'%' OR
        i.ItemNameAr LIKE N'%' + @Search + N'%'
    )
ORDER BY n.IssueDate DESC, n.IssueID DESC";

            var result = await connection.QueryAsync<MaterialIssueListDto>(sql, new
            {
                Search = search,
                StatusFilter = statusFilter,
                FromDate = fromDate,
                ToDate = toDate
            });

            return result.ToList();
        }

        // ==========================================
        // جلب إذن صرف واحد
        // ==========================================
        public async Task<MaterialIssueEditDto?> GetByIdAsync(int id)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض أذون صرف التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    n.IssueID AS MaterialIssueID,
    n.IssueNumber,
    n.IssueDate,
    n.ProductionOrderID,
    po.OrderNumber,
    i.ItemNameAr AS ProductName,
    n.WarehouseID AS SourceWarehouseID,
    w.WarehouseNameAr AS WarehouseName,
    n.IssueStatus,
    n.IssueType,
    n.IssuedBy,
    n.ReceivedBy,
    n.ApprovedBy,
    n.ApprovedDate,
    n.Notes,
    ISNULL(ag.TotalItems, 0) AS TotalItems,
    ISNULL(ag.TotalQty, 0) AS TotalQty,
    ISNULL(ag.TotalCost, 0) AS TotalCost
FROM dbo.MaterialIssueNotes n
INNER JOIN dbo.ProductionOrders po ON n.ProductionOrderID = po.ProductionOrderID
INNER JOIN dbo.Items i ON po.ProductItemID = i.ItemID
INNER JOIN dbo.Warehouses w ON n.WarehouseID = w.WarehouseID
LEFT JOIN (
    SELECT
        IssueID,
        COUNT(*) AS TotalItems,
        SUM(IssuedQty) AS TotalQty,
        SUM(ISNULL(LineCost, IssuedQty * ISNULL(UnitCost, 0))) AS TotalCost
    FROM dbo.MaterialIssueDetails
    GROUP BY IssueID
) ag ON n.IssueID = ag.IssueID
WHERE n.IssueID = @ID";

            return await connection.QueryFirstOrDefaultAsync<MaterialIssueEditDto>(
                sql, new { ID = id });
        }

        // ==========================================
        // تفاصيل إذن الصرف
        // ==========================================
        public async Task<List<MaterialIssueDetailDto>> GetDetailsAsync(int issueId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض تفاصيل إذن الصرف.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    d.IssueDetailID,
    d.IssueID AS MaterialIssueID,
    d.LineNumber,
    d.ItemID,
    i.ItemCode,
    i.ItemNameAr AS ItemName,
    d.UnitID,
    u.UnitNameAr AS UnitName,
    CASE WHEN ISNULL(pom.MaterialType, 0) = 1 AND i.ItemType = 4 THEN 3
         ELSE ISNULL(pom.MaterialType, 0) END AS MaterialType,
    CASE
        WHEN ISNULL(pom.MaterialType, 0) = 1 AND i.ItemType = 4 THEN N'منتج تام'
        WHEN ISNULL(pom.MaterialType, 0) = 1 THEN N'مادة خام'
        WHEN ISNULL(pom.MaterialType, 0) = 2 THEN N'مادة تعبئة'
        ELSE N'غير معروف'
    END AS MaterialTypeName,
    ISNULL(pom.RequiredQty, d.IssuedQty) AS RequiredQty,
    ISNULL(pom.IssuedQty, 0) AS IssuedQty,
    d.IssuedQty AS IssueQty,
    ISNULL(d.UnitCost, 0) AS UnitCost,
    ISNULL(d.LineCost, 0) AS LineCost,
    d.POMaterialID,
    d.BatchNumber,
    d.ExpiryDate,
    d.Notes,
    CAST(0 AS DECIMAL(18,4)) AS AvailableStock
FROM dbo.MaterialIssueDetails d
INNER JOIN dbo.Items i ON d.ItemID = i.ItemID
INNER JOIN dbo.Units u ON d.UnitID = u.UnitID
LEFT JOIN dbo.ProductionOrderMaterials pom ON d.POMaterialID = pom.POMaterialID
WHERE d.IssueID = @IssueID
ORDER BY d.LineNumber";

            var result = await connection.QueryAsync<MaterialIssueDetailDto>(
                sql, new { IssueID = issueId });

            return result.ToList();
        }

        // ==========================================
        // المواد المعلقة من أمر التصنيع
        // ==========================================
        public async Task<List<PendingMaterialDto>> GetPendingMaterialsAsync(int productionOrderId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض مواد أمر التصنيع.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    pom.POMaterialID,
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
    pom.RequiredQty,
    ISNULL(pom.IssuedQty, 0) AS AlreadyIssuedQty,
    ISNULL(pom.UnitCost, 0) AS UnitCost
FROM dbo.ProductionOrderMaterials pom
INNER JOIN dbo.Items i ON pom.ItemID = i.ItemID
INNER JOIN dbo.Units u ON pom.UnitID = u.UnitID
WHERE pom.ProductionOrderID = @OrderID
  AND pom.RequiredQty > ISNULL(pom.IssuedQty, 0)
ORDER BY pom.MaterialType, pom.LineNumber";

            var result = await connection.QueryAsync<PendingMaterialDto>(
                sql, new { OrderID = productionOrderId });

            return result.ToList();
        }

        // ==========================================
        // نوع الصرف المقترح
        // ==========================================
        public async Task<int> GetSuggestedIssueTypeAsync(int productionOrderId, int excludeIssueId = 0)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض أذون الصرف.");

            using var connection = CreateConnection();

            var count = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM dbo.MaterialIssueNotes
                  WHERE ProductionOrderID = @ProductionOrderID
                    AND IssueStatus <> 3
                    AND IssueID <> @ExcludeIssueID",
                new
                {
                    ProductionOrderID = productionOrderId,
                    ExcludeIssueID = excludeIssueId
                });

            return count > 0 ? 2 : 1;
        }

        // ==========================================
        // إضافة إذن صرف جديد
        // ==========================================
        public async Task<int> InsertAsync(
            MaterialIssueEditDto header,
            List<MaterialIssueDetailDto> details,
            int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_MATISS", p => p.CanAdd,
                "لا تملك صلاحية إنشاء أذون الصرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                await ValidateIssueDraftAsync(
                    connection, transaction, header.ProductionOrderID, header.SourceWarehouseID, details);
                var issueNumber = await GenerateIssueNumberAsync(connection, transaction);

                var sql = @"
INSERT INTO dbo.MaterialIssueNotes
(
    IssueNumber, IssueDate, ProductionOrderID, WarehouseID,
    IssueType, IssueStatus, IssuedBy, ReceivedBy,
    Notes, CreatedBy, CreatedDate
)
VALUES
(
    @IssueNumber, @IssueDate, @ProductionOrderID, @WarehouseID,
    @IssueType, 1, NULLIF(@IssuedBy, 0), NULLIF(@ReceivedBy, 0),
    @Notes, @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    IssueNumber = issueNumber,
                    header.IssueDate,
                    header.ProductionOrderID,
                    WarehouseID = header.SourceWarehouseID,
                    header.IssueType,
                    header.IssuedBy,
                    header.ReceivedBy,
                    header.Notes,
                    UserID = userId
                }, transaction);

                await InsertDetailsAsync(connection, transaction, newId, details);
                await _notification.CreateForPermissionHoldersAsync(
                    connection, transaction, notificationType: 1,
                    title: $"إذن صرف بانتظار الاعتماد #{issueNumber}",
                    message: $"تم إنشاء إذن صرف مكونات التصنيع رقم {issueNumber} ويحتاج إلى مراجعة",
                    priority: 2, permissionModuleCode: "SCR_MATISS",
                    requiredPermission: ModulePermissionRequirement.Approve,
                    relatedModule: "SCR_MATISS", relatedRecordId: newId, createdBy: userId);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 1,
                    tableName: "MaterialIssueNotes",
                    recordId: newId.ToString(),
                    moduleName: "SCR_MATISS",
                    description: $"إنشاء إذن صرف مكونات تصنيع: {issueNumber}"
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
        // تعديل إذن صرف قائم
        // ==========================================
        public async Task UpdateAsync(
            MaterialIssueEditDto header,
            List<MaterialIssueDetailDto> details,
            int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_MATISS", p => p.CanEdit,
                "لا تملك صلاحية تعديل أذون الصرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<IssueApproveHeadDto>(
                    @"SELECT IssueID,IssueNumber,IssueStatus,ProductionOrderID,WarehouseID,IssueType,CreatedBy
                      FROM dbo.MaterialIssueNotes WITH (UPDLOCK,HOLDLOCK)
                      WHERE IssueID=@IssueID",
                    new { IssueID = header.MaterialIssueID }, transaction);

                if (current is null)
                    throw new InvalidOperationException("إذن الصرف غير موجود");
                if (current.IssueStatus != 1)
                    throw new InvalidOperationException("لا يمكن تعديل إذن صرف غير مسودة");
                if (header.ProductionOrderID != current.ProductionOrderID)
                    throw new InvalidOperationException("لا يمكن تغيير أمر التصنيع المرتبط بإذن قائم");

                await ValidateIssueDraftAsync(
                    connection, transaction, current.ProductionOrderID, header.SourceWarehouseID, details);

                var changed = await connection.ExecuteAsync(@"
UPDATE dbo.MaterialIssueNotes SET
    IssueDate = @IssueDate,
    WarehouseID = @WarehouseID,
    IssueType = @IssueType,
    IssuedBy = NULLIF(@IssuedBy, 0),
    ReceivedBy = NULLIF(@ReceivedBy, 0),
    Notes = @Notes,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE IssueID = @IssueID AND IssueStatus=1",
                    new
                    {
                        IssueID = header.MaterialIssueID,
                        header.IssueDate,
                        WarehouseID = header.SourceWarehouseID,
                        header.IssueType,
                        header.IssuedBy,
                        header.ReceivedBy,
                        header.Notes,
                        UserID = userId
                    }, transaction);
                if (changed != 1)
                    throw new InvalidOperationException("تغيرت حالة الإذن؛ أعد تحميله قبل التعديل");

                await connection.ExecuteAsync(
                    @"DELETE FROM dbo.MaterialIssueDetails WHERE IssueID=@IssueID",
                    new { IssueID = header.MaterialIssueID }, transaction);
                await InsertDetailsAsync(connection, transaction, header.MaterialIssueID, details);
                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "MaterialIssueNotes",
                    recordId: header.MaterialIssueID.ToString(),
                    moduleName: "SCR_MATISS",
                    description: $"تعديل إذن صرف مكونات تصنيع: {header.IssueNumber}"
                );
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        // ==========================================
        // اعتماد إذن الصرف
        // ==========================================
        public async Task<(bool Success, string Message)> ApproveAsync(int issueId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_MATISS", p => p.CanApprove,
                "لا تملك صلاحية اعتماد أذون الصرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            IssueApproveHeadDto? approvedIssue = null;

            try
            {
                approvedIssue = await connection.QueryFirstOrDefaultAsync<IssueApproveHeadDto>(
                    @"SELECT IssueID,IssueNumber,IssueStatus,ProductionOrderID,WarehouseID,IssueType,CreatedBy
                      FROM dbo.MaterialIssueNotes WITH (UPDLOCK,HOLDLOCK)
                      WHERE IssueID=@ID",
                    new { ID = issueId }, transaction);
                if (approvedIssue is null)
                    return (false, "إذن الصرف غير موجود");
                if (approvedIssue.IssueStatus != 1)
                    return (false, "لا يمكن اعتماد إذن غير مسودة");

                var orderStatus = await connection.ExecuteScalarAsync<int?>(
                    @"SELECT OrderStatus
                      FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ProductionOrderID",
                    new { approvedIssue.ProductionOrderID }, transaction);
                if (!orderStatus.HasValue || orderStatus.Value is < 2 or > 4)
                    return (false, "لا يمكن صرف مواد لأمر تصنيع غير نشط أو ملغى");

                var warehouseIsActive = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK)
                      WHERE WarehouseID=@WarehouseID AND IsActive=1",
                    new { approvedIssue.WarehouseID }, transaction);
                if (warehouseIsActive != 1)
                    return (false, "مخزن الصرف غير موجود أو غير نشط");

                var details = (await connection.QueryAsync<IssueApproveDetailDto>(
                    @"SELECT IssueDetailID,LineNumber,POMaterialID,ItemID,UnitID,IssuedQty,
                             ISNULL(UnitCost,0) AS UnitCost,BatchNumber,ExpiryDate,Notes
                      FROM dbo.MaterialIssueDetails WITH (UPDLOCK,HOLDLOCK)
                      WHERE IssueID=@IssueID
                      ORDER BY ItemID,LineNumber,IssueDetailID",
                    new { IssueID = issueId }, transaction)).ToList();
                if (details.Count == 0)
                    return (false, "لا يمكن اعتماد إذن صرف بدون مواد");

                var priorMovements = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM dbo.InventoryTransactions WITH (UPDLOCK,HOLDLOCK)
                      WHERE SourceDocType=@SourceDocType AND SourceDocID=@IssueID
                        AND TransactionType=@TransactionType",
                    new
                    {
                        SourceDocType = "MI",
                        IssueID = issueId,
                        TransactionType = (byte)2
                    }, transaction);
                if (priorMovements != 0)
                    return (false, "يوجد ترحيل مخزني سابق لهذا الإذن؛ راجع سجل الحركة قبل إعادة الاعتماد");

                foreach (var detail in details)
                {
                    if (detail.IssuedQty <= 0 || !detail.POMaterialID.HasValue)
                        return (false, $"سطر الصرف {detail.LineNumber} غير صالح أو غير مرتبط بمادة أمر التصنيع");
                }

                foreach (var group in details.GroupBy(x => x.POMaterialID!.Value))
                {
                    var material = await connection.QueryFirstOrDefaultAsync<POMaterialIssueState>(
                        @"SELECT POMaterialID,ProductionOrderID,ItemID,UnitID,RequiredQty,
                                 ISNULL(IssuedQty,0) AS AlreadyIssuedQty
                          FROM dbo.ProductionOrderMaterials WITH (UPDLOCK,HOLDLOCK)
                          WHERE POMaterialID=@POMaterialID AND ProductionOrderID=@ProductionOrderID",
                        new { POMaterialID = group.Key, approvedIssue.ProductionOrderID }, transaction);
                    if (material is null)
                        return (false, "إحدى مواد الصرف لا تتبع أمر التصنيع المحدد");
                    if (group.Any(x => x.ItemID != material.ItemID || x.UnitID != material.UnitID))
                        return (false, $"الصنف أو الوحدة في تفاصيل مادة الأمر {group.Key} لا يطابقان الوصفة");
                    if (material.RequiredQty - material.AlreadyIssuedQty + 0.0001m < group.Sum(x => x.IssuedQty))
                        return (false, $"كمية الصرف تتجاوز المتبقي لمادة الأمر رقم {group.Key}");
                }

                // Allocation, batch balances, inventory balance and cost ledger are one transaction.
                await PostIssueStockFifoAsync(connection, transaction, approvedIssue, userId, details);

                foreach (var group in details.GroupBy(x => x.POMaterialID!.Value))
                {
                    var first = group.First();
                    var quantity = group.Sum(x => x.IssuedQty);
                    var affected = await connection.ExecuteAsync(
                        @"UPDATE dbo.ProductionOrderMaterials
                          SET IssuedQty=ISNULL(IssuedQty,0)+@IssuedQty,
                              LineStatus=CASE
                                  WHEN RequiredQty-(ISNULL(IssuedQty,0)+@IssuedQty)<=0.0001 THEN 2
                                  ELSE 3
                              END
                          WHERE POMaterialID=@POMaterialID
                            AND ProductionOrderID=@ProductionOrderID
                            AND ItemID=@ItemID AND UnitID=@UnitID
                            AND ISNULL(IssuedQty,0)+@IssuedQty<=RequiredQty+0.0001",
                        new
                        {
                            IssuedQty = quantity,
                            POMaterialID = group.Key,
                            approvedIssue.ProductionOrderID,
                            first.ItemID,
                            first.UnitID
                        }, transaction);
                    if (affected != 1)
                        throw new InvalidOperationException($"تغير رصيد صرف مادة الأمر {group.Key}؛ أُلغيت العملية كاملة.");
                }

                var issueUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.MaterialIssueNotes
                      SET IssueStatus=2,ApprovedBy=@UserID,ApprovedDate=GETDATE(),
                          ModifiedBy=@UserID,ModifiedDate=GETDATE()
                      WHERE IssueID=@IssueID AND IssueStatus=1",
                    new { IssueID = issueId, UserID = userId }, transaction);
                if (issueUpdated != 1)
                    throw new InvalidOperationException("تغيرت حالة إذن الصرف أثناء الاعتماد؛ أُلغيت العملية كاملة.");

                // The order has exact FIFO issue costs, but the schema has no link from an issue
                // line to a production batch. Store the order-level issued cost only when no
                // returned quantity exists; never spread it over batches or load it onto rejects.
                var hasMaterialReturns = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.ProductionOrderMaterials WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductionOrderID=@ProductionOrderID AND ISNULL(ReturnedQty,0)>0",
                    new { approvedIssue.ProductionOrderID }, transaction);
                if (hasMaterialReturns == 0)
                {
                    var actualIssuedMaterialCost = await connection.ExecuteScalarAsync<decimal>(
                        @"SELECT ISNULL(SUM(ISNULL(mid.LineCost,mid.IssuedQty*ISNULL(mid.UnitCost,0))),0)
                          FROM dbo.MaterialIssueNotes mi
                          INNER JOIN dbo.MaterialIssueDetails mid ON mid.IssueID=mi.IssueID
                          WHERE mi.ProductionOrderID=@ProductionOrderID AND mi.IssueStatus=2",
                        new { approvedIssue.ProductionOrderID }, transaction);
                    await connection.ExecuteAsync(
                        @"UPDATE dbo.ProductionOrders
                          SET ActualMaterialCost=@ActualMaterialCost,
                              ActualTotalCost=@ActualMaterialCost+ISNULL(ActualLaborCost,0)+ISNULL(ActualOverheadCost,0),
                              ModifiedBy=@UserID,ModifiedDate=GETDATE()
                          WHERE ProductionOrderID=@ProductionOrderID",
                        new
                        {
                            approvedIssue.ProductionOrderID,
                            ActualMaterialCost = actualIssuedMaterialCost,
                            UserID = userId
                        }, transaction);
                }

                var orderPrepared = await connection.ExecuteAsync(
                    @"UPDATE dbo.ProductionOrders
                      SET OrderStatus=3,ModifiedBy=@UserID,ModifiedDate=GETDATE()
                      WHERE ProductionOrderID=@ProductionOrderID AND OrderStatus=2",
                    new { approvedIssue.ProductionOrderID, UserID = userId }, transaction);
                if (orderPrepared == 1)
                {
                    await _notification.CreateForPermissionHoldersAsync(
                        connection, transaction, notificationType: 1,
                        title: "صرف مواد معتمد؛ التصنيع متاح",
                        message: $"تم اعتماد إذن الصرف {approvedIssue.IssueNumber} وبدأ تجهيز أمر التصنيع؛ يمكن إنشاء دفعة إنتاج.",
                        priority: 2, permissionModuleCode: "SCR_BATCH",
                        requiredPermission: ModulePermissionRequirement.Add,
                        relatedModule: "SCR_BATCH", relatedRecordId: 0, createdBy: userId,
                        excludeUserId: userId);
                }

                if (approvedIssue.CreatedBy.HasValue)
                {
                    await _notification.CreateForAuthorizedUserAsync(
                        connection, transaction, notificationType: 3,
                        title: $"اعتماد إذن صرف #{approvedIssue.IssueNumber}",
                        message: $"تم اعتماد إذن صرف مكونات التصنيع رقم {approvedIssue.IssueNumber}",
                        priority: 2, targetUserId: approvedIssue.CreatedBy.Value,
                        permissionModuleCode: "SCR_MATISS", relatedModule: "SCR_MATISS",
                        relatedRecordId: issueId, createdBy: userId);
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
                tableName: "MaterialIssueNotes",
                recordId: issueId.ToString(),
                changedColumns: "IssueStatus,InventoryTransactions,InventoryBatchBalance,InventoryBalance,ProductionOrderMaterials",
                moduleName: "SCR_MATISS",
                description: $"اعتماد إذن صرف FIFO: {approvedIssue!.IssueNumber}"
            );

            return (true, "تم اعتماد إذن الصرف وترحيل حركات FIFO وتحديث أرصدة المخزون داخل معاملة واحدة");
        }

        // ==========================================
        // إلغاء إذن الصرف
        // ==========================================
        public async Task<(bool Success, string Message)> CancelAsync(int issueId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_MATISS", p => p.CanCancel,
                "لا تملك صلاحية إلغاء أذون الصرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            IssueApproveHeadDto? issue = null;
            try
            {
                issue = await connection.QueryFirstOrDefaultAsync<IssueApproveHeadDto>(
                    @"SELECT IssueID,IssueNumber,IssueStatus,ProductionOrderID,WarehouseID,IssueType,CreatedBy
                      FROM dbo.MaterialIssueNotes WITH (UPDLOCK,HOLDLOCK)
                      WHERE IssueID=@ID",
                    new { ID = issueId }, transaction);
                if (issue is null) return (false, "إذن الصرف غير موجود");
                if (issue.IssueStatus == 2) return (false, "لا يمكن إلغاء إذن معتمد؛ استخدم مستند عكسي معتمد");
                if (issue.IssueStatus == 3) return (false, "الإذن ملغي بالفعل");
                if (issue.IssueStatus != 1) return (false, "لا يمكن إلغاء الإذن من حالته الحالية");

                var changed = await connection.ExecuteAsync(
                    @"UPDATE dbo.MaterialIssueNotes
                      SET IssueStatus=3,ModifiedBy=@UserID,ModifiedDate=GETDATE()
                      WHERE IssueID=@ID AND IssueStatus=1",
                    new { ID = issueId, UserID = userId }, transaction);
                if (changed != 1) throw new InvalidOperationException("تغيرت حالة الإذن؛ أعد تحميله قبل الإلغاء.");

                if (issue.CreatedBy.HasValue)
                {
                    await _notification.CreateForAuthorizedUserAsync(
                        connection, transaction, notificationType: 3,
                        title: $"إلغاء إذن صرف #{issue.IssueNumber}",
                        message: $"تم إلغاء إذن الصرف رقم {issue.IssueNumber}",
                        priority: 2, targetUserId: issue.CreatedBy.Value,
                        permissionModuleCode: "SCR_MATISS", relatedModule: "SCR_MATISS",
                        relatedRecordId: issueId, createdBy: userId);
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
                tableName: "MaterialIssueNotes",
                recordId: issueId.ToString(),
                moduleName: "SCR_MATISS",
                description: $"إلغاء مسودة إذن صرف: {issue!.IssueNumber}"
            );
            return (true, "تم إلغاء مسودة إذن الصرف بنجاح");
        }

        // ==========================================
        // Lookups
        // ==========================================
        public async Task<List<ProductionLookupDto>> GetProductionOrdersForIssueAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض أوامر التصنيع لأذون الصرف.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    po.ProductionOrderID AS Id,
    (po.OrderNumber + N' - ' + i.ItemNameAr) AS Name
FROM dbo.ProductionOrders po
INNER JOIN dbo.Items i ON po.ProductItemID = i.ItemID
WHERE po.OrderStatus IN (2, 3, 4)
ORDER BY po.OrderDate DESC";

            return (await connection.QueryAsync<ProductionLookupDto>(sql)).ToList();
        }

        public async Task<List<ProductionLookupDto>> GetWarehousesLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض المخازن لأذون الصرف.");

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
            await _authorization.RequireCurrentUserPermissionAsync("SCR_MATISS", p => p.CanView, "لا تملك صلاحية عرض الموظفين لأذون الصرف.");

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
        // توليد رقم الإذن
        // ==========================================
        private async Task<string> GenerateIssueNumberAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction)
        {
            try
            {
                var sql = @"
DECLARE @NextNum NVARCHAR(50);
EXEC dbo.sp_GetNextNumber 'MI', @NextNum OUTPUT;
SELECT @NextNum;";

                var result = await connection.QueryFirstOrDefaultAsync<string>(
                    sql, transaction: transaction);

                return result ?? $"MI-{DateTime.Now:yyMMddHHmmss}";
            }
            catch
            {
                return $"MI-{DateTime.Now:yyMMddHHmmss}";
            }
        }

        private async Task ValidateIssueDraftAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int productionOrderId,
            int warehouseId,
            List<MaterialIssueDetailDto> details)
        {
            if (productionOrderId <= 0 || warehouseId <= 0)
                throw new InvalidOperationException("حدد أمر التصنيع ومخزن الصرف.");
            if (details is null || details.Count == 0 || details.Any(x => x.IssueQty <= 0))
                throw new InvalidOperationException("أدخل كمية موجبة لمادة واحدة على الأقل.");

            var orderStatus = await connection.ExecuteScalarAsync<int?>(
                @"SELECT OrderStatus FROM dbo.ProductionOrders WITH (UPDLOCK,HOLDLOCK)
                  WHERE ProductionOrderID=@ProductionOrderID",
                new { ProductionOrderID = productionOrderId }, transaction);
            if (!orderStatus.HasValue || orderStatus.Value is < 2 or > 4)
                throw new InvalidOperationException("لا يمكن إنشاء إذن صرف لأمر تصنيع غير نشط أو ملغى.");

            var activeWarehouse = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK)
                  WHERE WarehouseID=@WarehouseID AND IsActive=1",
                new { WarehouseID = warehouseId }, transaction);
            if (activeWarehouse != 1)
                throw new InvalidOperationException("مخزن الصرف غير موجود أو غير نشط.");

            foreach (var group in details.GroupBy(x => x.POMaterialID))
            {
                if (!group.Key.HasValue || group.Key.Value <= 0)
                    throw new InvalidOperationException("يجب ربط كل سطر بمادة من مواد أمر التصنيع.");
                if (group.Any(x => x.ItemID <= 0 || x.UnitID <= 0 || x.IssueQty <= 0))
                    throw new InvalidOperationException("توجد بيانات صنف أو وحدة أو كمية غير صالحة في تفاصيل الإذن.");

                var material = await connection.QueryFirstOrDefaultAsync<POMaterialIssueState>(
                    @"SELECT POMaterialID,ProductionOrderID,ItemID,UnitID,RequiredQty,
                             ISNULL(IssuedQty,0) AS AlreadyIssuedQty
                      FROM dbo.ProductionOrderMaterials WITH (UPDLOCK,HOLDLOCK)
                      WHERE POMaterialID=@POMaterialID AND ProductionOrderID=@ProductionOrderID",
                    new { POMaterialID = group.Key.Value, ProductionOrderID = productionOrderId }, transaction);
                if (material is null)
                    throw new InvalidOperationException("إحدى مواد الصرف لا تتبع أمر التصنيع المحدد.");
                if (group.Any(x => x.ItemID != material.ItemID || x.UnitID != material.UnitID))
                    throw new InvalidOperationException($"الصنف أو الوحدة لا يطابقان مادة الأمر {group.Key.Value}.");
                if (material.RequiredQty - material.AlreadyIssuedQty + 0.0001m < group.Sum(x => x.IssueQty))
                    throw new InvalidOperationException($"كمية الصرف تتجاوز المتبقي لمادة الأمر رقم {group.Key.Value}.");
            }
        }

        // ==========================================
        // إدراج التفاصيل
        // ==========================================
        private async Task InsertDetailsAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int issueId,
            List<MaterialIssueDetailDto> details)
        {
            int lineNum = 0;

            foreach (var d in details.Where(x => x.IssueQty > 0))
            {
                lineNum++;

                await connection.ExecuteAsync(@"
INSERT INTO dbo.MaterialIssueDetails
(
    IssueID, LineNumber, POMaterialID, ItemID, UnitID,
    IssuedQty, UnitCost, LineCost, BatchNumber, ExpiryDate, Notes
)
VALUES
(
    @IssueID, @LineNumber, @POMaterialID, @ItemID, @UnitID,
    @IssuedQty, @UnitCost, @LineCost, NULLIF(@BatchNumber, N''), @ExpiryDate, @Notes
)",
                    new
                    {
                        IssueID = issueId,
                        LineNumber = lineNum,
                        d.POMaterialID,
                        d.ItemID,
                        d.UnitID,
                        IssuedQty = d.IssueQty,
                        d.UnitCost,
                        LineCost = decimal.Round(d.IssueQty * d.UnitCost, 8, MidpointRounding.AwayFromZero),
                        d.BatchNumber,
                        d.ExpiryDate,
                        d.Notes
                    }, transaction);
            }
        }

        // ==========================================
        // تصدير إكسيل
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(
            List<MaterialIssueListDto> items, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_MATISS", p => p.CanExport, "لا تملك صلاحية تصدير أذون الصرف.");

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("أذون الصرف");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير أذون صرف مكونات التصنيع — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 9).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 9).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[]
            {
                "#", "رقم الإذن", "التاريخ", "أمر التصنيع",
                "المنتج", "المخزن", "عدد الأصناف",
                "الكمية", "التكلفة"
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
                ws.Cell(row, 2).Value = item.IssueNumber;
                ws.Cell(row, 3).Value = item.IssueDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = item.OrderNumber;
                ws.Cell(row, 5).Value = item.ProductName;
                ws.Cell(row, 6).Value = item.WarehouseName;
                ws.Cell(row, 7).Value = item.TotalItems;
                ws.Cell(row, 8).Value = item.TotalQty;
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.####";
                ws.Cell(row, 9).Value = item.TotalCost;
                ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";

                for (int i = 1; i <= 9; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e8e2d0");
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 5,
                tableName: "MaterialIssueNotes",
                moduleName: "SCR_MATISS",
                description: $"تصدير {items.Count} إذن صرف إلى Excel"
            );

            return stream.ToArray();
        }
    }
}