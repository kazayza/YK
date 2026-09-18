using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public class MaterialIssueService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notification;

        private sealed class IssueApproveHeadDto
        {
            public int IssueID { get; set; }
            public string IssueNumber { get; set; } = "";
            public int IssueStatus { get; set; }
            public int ProductionOrderID { get; set; }
            public int WarehouseID { get; set; }
        }

        private sealed class IssueApproveDetailDto
        {
            public int? POMaterialID { get; set; }
            public int ItemID { get; set; }
            public decimal IssuedQty { get; set; }
            public decimal UnitCost { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
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
            NotificationService notification)
            : base(configuration)
        {
            _audit = audit;
            _notification = notification;
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
        SUM(LineCost) AS TotalCost
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
        SUM(LineCost) AS TotalCost
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
    ISNULL(pom.MaterialType, 0) AS MaterialType,
    CASE ISNULL(pom.MaterialType, 0)
        WHEN 1 THEN N'مادة خام'
        WHEN 2 THEN N'مادة تعبئة'
        ELSE N''
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
            using var connection = CreateConnection();

            var sql = @"
SELECT
    pom.POMaterialID,
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
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
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

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 1,
                    tableName: "MaterialIssueNotes",
                    recordId: newId.ToString(),
                    moduleName: "SCR_MATISS",
                    description: $"إنشاء إذن صرف خامات: {issueNumber}"
                );

                await _notification.CreateNotificationAsync(
                    notificationType: 1,
                    title: "إذن صرف خامات جديد",
                    message: $"تم إنشاء إذن صرف خامات رقم {issueNumber}",
                    priority: 2,
                    targetRoleId: 3,
                    relatedModule: "SCR_MATISS",
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
        // تعديل إذن صرف قائم
        // ==========================================
        public async Task UpdateAsync(
            MaterialIssueEditDto header,
            List<MaterialIssueDetailDto> details,
            int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var currentStatus = await connection.ExecuteScalarAsync<int?>(
                    @"SELECT IssueStatus
                      FROM dbo.MaterialIssueNotes
                      WHERE IssueID = @IssueID",
                    new { IssueID = header.MaterialIssueID }, transaction);

                if (!currentStatus.HasValue)
                    throw new Exception("إذن الصرف غير موجود");

                if (currentStatus.Value != 1)
                    throw new Exception("لا يمكن تعديل إذن صرف غير مسودة");

                await connection.ExecuteAsync(@"
UPDATE dbo.MaterialIssueNotes SET
    IssueDate = @IssueDate,
    WarehouseID = @WarehouseID,
    IssueType = @IssueType,
    IssuedBy = NULLIF(@IssuedBy, 0),
    ReceivedBy = NULLIF(@ReceivedBy, 0),
    Notes = @Notes,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE IssueID = @IssueID",
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

                await connection.ExecuteAsync(
                    @"DELETE FROM dbo.MaterialIssueDetails WHERE IssueID = @IssueID",
                    new { IssueID = header.MaterialIssueID }, transaction);

                await InsertDetailsAsync(connection, transaction, header.MaterialIssueID, details);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "MaterialIssueNotes",
                    recordId: header.MaterialIssueID.ToString(),
                    moduleName: "SCR_MATISS",
                    description: $"تعديل إذن صرف خامات: {header.IssueNumber}"
                );
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // اعتماد إذن الصرف
        // ==========================================
        public async Task<(bool Success, string Message)> ApproveAsync(int issueId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var issue = await connection.QueryFirstOrDefaultAsync<IssueApproveHeadDto>(
                    @"SELECT
                          IssueID,
                          IssueNumber,
                          IssueStatus,
                          ProductionOrderID,
                          WarehouseID
                      FROM dbo.MaterialIssueNotes
                      WHERE IssueID = @ID",
                    new { ID = issueId }, transaction);

                if (issue is null)
                    return (false, "إذن الصرف غير موجود");

                if (issue.IssueStatus != 1)
                    return (false, "لا يمكن اعتماد إذن غير مسودة");

                var detailCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*)
                      FROM dbo.MaterialIssueDetails
                      WHERE IssueID = @IssueID",
                    new { IssueID = issueId }, transaction);

                if (detailCount <= 0)
                    return (false, "لا يمكن اعتماد إذن صرف بدون مواد");

                var details = (await connection.QueryAsync<IssueApproveDetailDto>(
                    @"SELECT
                          POMaterialID,
                          ItemID,
                          IssuedQty,
                          UnitCost,
                          BatchNumber,
                          ExpiryDate
                      FROM dbo.MaterialIssueDetails
                      WHERE IssueID = @IssueID",
                    new { IssueID = issueId }, transaction)).ToList();

                // التحقق من الرصيد قبل الاعتماد
                foreach (var d in details)
                {
                    var balance = await connection.QueryFirstOrDefaultAsync<InventoryBalanceDto>(
                        @"SELECT TOP 1
                              BalanceID,
                              ItemID,
                              WarehouseID,
                              ISNULL(CurrentQty, 0) AS CurrentQty,
                              ISNULL(ReservedQty, 0) AS ReservedQty,
                              ISNULL(AvailableQty, 0) AS AvailableQty,
                              ISNULL(AverageCost, 0) AS AverageCost,
                              ISNULL(TotalValue, 0) AS TotalValue
                          FROM dbo.InventoryBalance
                          WHERE ItemID = @ItemID
                            AND WarehouseID = @WarehouseID",
                        new
                        {
                            d.ItemID,
                            WarehouseID = issue.WarehouseID
                        }, transaction);

                    if (balance is null)
                        return (false, $"لا يوجد رصيد مخزني للصنف رقم {d.ItemID} في المخزن المحدد");

                    var availableQty = balance.CurrentQty - balance.ReservedQty;
                    if (availableQty < d.IssuedQty)
                        return (false, $"الرصيد غير كافٍ للصنف رقم {d.ItemID} - المتاح {availableQty:N3}");
                }

                // اعتماد الإذن
                await connection.ExecuteAsync(@"
UPDATE dbo.MaterialIssueNotes SET
    IssueStatus = 2,
    ApprovedBy = @UserID,
    ApprovedDate = GETDATE(),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE IssueID = @ID",
                    new { ID = issueId, UserID = userId }, transaction);

                // تحديث مواد أمر التصنيع
                foreach (var d in details.Where(x => x.POMaterialID.HasValue))
                {
                    await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrderMaterials SET
    IssuedQty = ISNULL(IssuedQty, 0) + @IssuedQty,
    LineStatus = CASE
        WHEN (RequiredQty - (ISNULL(IssuedQty, 0) + @IssuedQty)) <= 0 THEN 2
        ELSE 3
    END
WHERE POMaterialID = @POMaterialID",
                        new
                        {
                            d.IssuedQty,
                            POMaterialID = d.POMaterialID!.Value
                        }, transaction);
                }

                // تحديث المخزون
                foreach (var d in details)
                {
                    await connection.ExecuteAsync(@"
UPDATE dbo.InventoryBalance
SET CurrentQty = ISNULL(CurrentQty, 0) - @IssuedQty,
    LastMovementDate = GETDATE(),
    ModifiedDate = GETDATE()
WHERE ItemID = @ItemID
  AND WarehouseID = @WarehouseID",
    new
    {
        d.ItemID,
        d.IssuedQty,
        WarehouseID = issue.WarehouseID
    }, transaction);
                }

                // تحديث حالة أمر التصنيع: معتمد -> جاري التجهيز
                await connection.ExecuteAsync(@"
UPDATE dbo.ProductionOrders
SET OrderStatus = 3,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ProductionOrderID = @ProductionOrderID
  AND OrderStatus = 2",
                    new
                    {
                        issue.ProductionOrderID,
                        UserID = userId
                    }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "MaterialIssueNotes",
                    recordId: issueId.ToString(),
                    changedColumns: "IssueStatus",
                    moduleName: "SCR_MATISS",
                    description: $"اعتماد إذن صرف: {issue.IssueNumber}"
                );

                await _notification.CreateNotificationAsync(
                    notificationType: 3,
                    title: $"اعتماد إذن صرف #{issue.IssueNumber}",
                    message: $"تم اعتماد إذن صرف خامات رقم {issue.IssueNumber}",
                    priority: 2,
                    targetRoleId: 3,
                    relatedModule: "SCR_MATISS",
                    relatedRecordId: issueId,
                    createdBy: userId
                );

                return (true, "تم اعتماد إذن الصرف وتحديث الكميات والمخزون بنجاح");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, ex.Message);
            }
        }

        // ==========================================
        // إلغاء إذن الصرف
        // ==========================================
        public async Task<(bool Success, string Message)> CancelAsync(int issueId, int userId)
        {
            using var connection = CreateConnection();

            var issue = await connection.QueryFirstOrDefaultAsync<IssueApproveHeadDto>(
                @"SELECT
                      IssueID,
                      IssueNumber,
                      IssueStatus,
                      ProductionOrderID,
                      WarehouseID
                  FROM dbo.MaterialIssueNotes
                  WHERE IssueID = @ID",
                new { ID = issueId });

            if (issue is null)
                return (false, "إذن الصرف غير موجود");

            if (issue.IssueStatus == 2)
                return (false, "لا يمكن إلغاء إذن معتمد");

            if (issue.IssueStatus == 3)
                return (false, "الإذن ملغي بالفعل");

            await connection.ExecuteAsync(@"
UPDATE dbo.MaterialIssueNotes SET
    IssueStatus = 3,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE IssueID = @ID",
                new { ID = issueId, UserID = userId });

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "MaterialIssueNotes",
                recordId: issueId.ToString(),
                moduleName: "SCR_MATISS",
                description: $"إلغاء إذن صرف: {issue.IssueNumber}"
            );

            return (true, "تم إلغاء إذن الصرف بنجاح");
        }

        // ==========================================
        // Lookups
        // ==========================================
        public async Task<List<ProductionLookupDto>> GetProductionOrdersForIssueAsync()
        {
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
    IssuedQty, UnitCost, BatchNumber, ExpiryDate, Notes
)
VALUES
(
    @IssueID, @LineNumber, @POMaterialID, @ItemID, @UnitID,
    @IssuedQty, @UnitCost, NULLIF(@BatchNumber, N''), @ExpiryDate, @Notes
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
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("أذون الصرف");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير أذون صرف الخامات — مصنع واي كي كوتينج";
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
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
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
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f7ff");

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