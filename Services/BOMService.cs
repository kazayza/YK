using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public class BOMService : BaseDbService
    {
        private sealed class BOMActivationState
        {
            public int BOMID { get; set; }
            public string BOMCode { get; set; } = "";
            public int ProductItemID { get; set; }
            public int BOMStatus { get; set; }
            public bool IsActive { get; set; }
        }

        private sealed class BOMDetailHeaderDto
        {
            public decimal OutputQty { get; set; }
            public int OutputUnitID { get; set; }
        }

        private sealed class BOMSubstituteItemState
        {
            public int ItemID { get; set; }
            public int ItemType { get; set; }
            public int PrimaryUnitID { get; set; }
            public bool IsActive { get; set; }
        }

        private readonly AuditService _audit;
        private readonly ModuleAuthorizationService _authorization;

        public BOMService(IConfiguration configuration, AuditService audit, ModuleAuthorizationService authorization)
            : base(configuration)
        {
            _audit = audit;
            _authorization = authorization;
        }

        private static void ValidateBOMHeader(BOMEditDto bom)
        {
            if (string.IsNullOrWhiteSpace(bom.BOMName) || bom.ProductItemID <= 0 ||
                bom.BatchSize <= 0 || bom.BatchUnitID <= 0 || bom.OutputQty <= 0 || bom.OutputUnitID <= 0)
                throw new InvalidOperationException("بيانات الوصفة الأساسية غير مكتملة أو كمياتها/وحداتها غير صالحة.");
            if (bom.EffectiveFrom.HasValue && bom.EffectiveTo.HasValue &&
                bom.EffectiveTo.Value.Date < bom.EffectiveFrom.Value.Date)
                throw new InvalidOperationException("تاريخ نهاية السريان لا يمكن أن يسبق تاريخ بدايته.");
        }

        private static async Task ValidateBOMReferencesAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            BOMEditDto bom)
        {
            var valid = await connection.ExecuteScalarAsync<int>(@"
SELECT CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.Items WITH (UPDLOCK,HOLDLOCK)
    WHERE ItemID=@ProductItemID AND ItemType IN (3,4) AND IsActive=1
) AND EXISTS
(
    SELECT 1 FROM dbo.Units WITH (UPDLOCK,HOLDLOCK)
    WHERE UnitID=@BatchUnitID AND IsActive=1
) AND EXISTS
(
    SELECT 1 FROM dbo.Units WITH (UPDLOCK,HOLDLOCK)
    WHERE UnitID=@OutputUnitID AND IsActive=1
) THEN 1 ELSE 0 END",
                new { bom.ProductItemID, bom.BatchUnitID, bom.OutputUnitID }, transaction);
            if (valid != 1)
                throw new InvalidOperationException("المنتج أو وحدات الدفعة/الناتج غير موجودة أو غير نشطة.");
        }

        private static async Task ValidateBOMForActivationAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int bomId)
        {
            var bom = await connection.QueryFirstOrDefaultAsync<BOMEditDto>(@"
SELECT BOMID,BOMName,ProductItemID,BatchSize,BatchUnitID,OutputQty,OutputUnitID,
       ISNULL(WastePercent,0) AS WastePercent,EstimatedTime,
       ISNULL(LaborCostPerBatch,0) AS LaborCostPerBatch,
       ISNULL(OverheadCostPerBatch,0) AS OverheadCostPerBatch,
       EffectiveFrom,EffectiveTo
FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                new { BOMID = bomId }, transaction);
            if (bom is null)
                throw new InvalidOperationException("الوصفة غير موجودة.");
            ValidateBOMHeader(bom);
            await ValidateBOMReferencesAsync(connection, transaction, bom);

            var materialCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                new { BOMID = bomId }, transaction);
            if (materialCount <= 0)
                throw new InvalidOperationException("لا يمكن تنشيط وصفة بدون مكونات.");

            var incompatiblePercentageLines = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.BOMDetails bd WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Units u ON u.UnitID=bd.UnitID
WHERE bd.BOMID=@BOMID AND bd.InputPercentage IS NOT NULL
  AND (bd.MaterialType=2 OR bd.UnitID<>@OutputUnitID OR ISNULL(u.UnitType,N'')<>N'Weight')",
                new { BOMID = bomId, bom.OutputUnitID }, transaction);
            if (incompatiblePercentageLines > 0)
                throw new InvalidOperationException("توجد مكونات محفوظة كنسبة لا تطابق وحدة وزن الناتج.");

            var totalInputPercentage = await connection.ExecuteScalarAsync<decimal>(@"
SELECT ISNULL(SUM(InputPercentage),0)
FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND InputPercentage IS NOT NULL",
                new { BOMID = bomId }, transaction);
            if (totalInputPercentage > 100m)
                throw new InvalidOperationException("مجموع نسب مكونات الوصفة لا يمكن أن يتجاوز 100%.");

            var invalidSubstitutes = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.BOMDetails bd WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Items sourceItem ON sourceItem.ItemID=bd.ItemID
WHERE bd.BOMID=@BOMID AND bd.SubstituteItemID IS NOT NULL
  AND (sourceItem.ItemID IS NULL OR bd.SubstituteItemID=bd.ItemID OR NOT EXISTS
       (SELECT 1 FROM dbo.Items substituteItem WITH (UPDLOCK,HOLDLOCK)
        WHERE substituteItem.ItemID=bd.SubstituteItemID
          AND substituteItem.IsActive=1
          AND substituteItem.ItemType=sourceItem.ItemType
          AND substituteItem.PrimaryUnitID=bd.UnitID))",
                new { BOMID = bomId }, transaction);
            if (invalidSubstitutes > 0)
                throw new InvalidOperationException("لا يمكن تنشيط الوصفة؛ يوجد بديل غير نشط أو من نوع/وحدة غير متوافقة.");
        }

        private static async Task EnsureEditableBOMAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int bomId)
        {
            if (bomId <= 0)
                throw new InvalidOperationException("معرّف الوصفة غير صالح.");
            var status = await connection.ExecuteScalarAsync<int?>(@"
SELECT BOMStatus FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                new { BOMID = bomId }, transaction);
            if (!status.HasValue)
                throw new InvalidOperationException("الوصفة غير موجودة.");
            if (status.Value != 1 && status.Value != 3)
                throw new InvalidOperationException("لا يمكن تعديل مكونات أو مراحل وصفة نشطة أو ملغاة.");
        }

        private static async Task ValidateBOMDetailAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            BOMDetailDto detail,
            int excludeDetailId = 0)
        {
            if (detail.ItemID <= 0 || detail.UnitID <= 0 || detail.Quantity <= 0 ||
                (detail.MaterialType != 1 && detail.MaterialType != 2 && detail.MaterialType != 3))
                throw new InvalidOperationException("بيانات مكوّن الوصفة غير مكتملة أو غير صالحة.");

            var expectedItemType = detail.MaterialType switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 };
            var itemType = await connection.ExecuteScalarAsync<int?>(@"
SELECT ItemType FROM dbo.Items WITH (UPDLOCK,HOLDLOCK)
WHERE ItemID=@ItemID AND IsActive=1",
                new { detail.ItemID }, transaction);
            if (!itemType.HasValue || itemType.Value != expectedItemType)
                throw new InvalidOperationException("نوع الصنف لا يطابق نوع مكوّن الوصفة أو أن الصنف غير نشط.");

            if (detail.SubstituteItemID.HasValue)
            {
                if (detail.SubstituteItemID.Value == detail.ItemID)
                    throw new InvalidOperationException("لا يمكن أن يكون الصنف بديلًا لنفسه.");

                var substitute = await connection.QueryFirstOrDefaultAsync<BOMSubstituteItemState>(@"
SELECT ItemID,ItemType,PrimaryUnitID,IsActive
FROM dbo.Items WITH (UPDLOCK,HOLDLOCK)
WHERE ItemID=@SubstituteItemID",
                    new { SubstituteItemID = detail.SubstituteItemID.Value }, transaction);
                if (substitute is null || !substitute.IsActive || substitute.ItemType != itemType.Value)
                    throw new InvalidOperationException("البديل يجب أن يكون صنفًا نشطًا من نفس نوع مكوّن الوصفة.");
                if (substitute.PrimaryUnitID != detail.UnitID)
                    throw new InvalidOperationException("البديل يجب أن يستخدم نفس الوحدة الأساسية للمكوّن؛ لا توجد نسبة استبدال محفوظة.");
            }

            var header = await connection.QueryFirstOrDefaultAsync<BOMDetailHeaderDto>(@"
SELECT OutputQty,OutputUnitID FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                new { detail.BOMID }, transaction);
            if (header is null || header.OutputQty <= 0)
                throw new InvalidOperationException("مخرج الوصفة غير صالح لحفظ المكوّن.");

            var unitType = await connection.QueryFirstOrDefaultAsync<string?>(@"
SELECT UnitType FROM dbo.Units WITH (UPDLOCK,HOLDLOCK)
WHERE UnitID=@UnitID AND IsActive=1",
                new { detail.UnitID }, transaction);
            if (unitType is null)
                throw new InvalidOperationException("وحدة المكوّن غير موجودة أو غير نشطة.");

            var duplicateCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND ItemID=@ItemID AND BOMDetailID<>@ExcludeID",
                new { detail.BOMID, detail.ItemID, ExcludeID = excludeDetailId }, transaction);
            if (duplicateCount > 0)
                throw new InvalidOperationException("هذا الصنف موجود بالفعل داخل الوصفة.");

            if (detail.InputPercentage.HasValue)
            {
                var percentage = detail.InputPercentage.Value;
                if ((detail.MaterialType != 1 && detail.MaterialType != 3) ||
                    detail.UnitID != header.OutputUnitID ||
                    !string.Equals(unitType, "Weight", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("لا يمكن حفظ نسبة لمكوّن لا يطابق وحدة وزن الناتج.");
                if (percentage <= 0 || percentage > 100)
                    throw new InvalidOperationException("نسبة المكوّن يجب أن تكون أكبر من صفر ولا تتجاوز 100%.");

                var expectedQuantity = decimal.Round(
                    header.OutputQty * percentage / 100m, 6, MidpointRounding.AwayFromZero);
                if (Math.Abs(detail.Quantity - expectedQuantity) > 0.000001m)
                    throw new InvalidOperationException("كمية المكوّن لا تطابق النسبة المحفوظة وحجم الناتج.");

                var existingPercentageTotal = await connection.ExecuteScalarAsync<decimal>(@"
SELECT ISNULL(SUM(InputPercentage),0)
FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND InputPercentage IS NOT NULL AND BOMDetailID<>@ExcludeID",
                    new { detail.BOMID, ExcludeID = excludeDetailId }, transaction);
                if (existingPercentageTotal + percentage > 100m)
                    throw new InvalidOperationException("مجموع نسب مكونات الوصفة لا يمكن أن يتجاوز 100%.");
            }
        }

        // ==========================================
        // قائمة الوصفات
        // ==========================================
        public async Task<List<BOMListDto>> GetBOMListAsync(
            string? search = null,
            int? statusFilter = null,
            bool activeOnly = true)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض الوصفات.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    b.BOMID,
    b.BOMCode,
    b.BOMName,
    b.ProductItemID,
    i.ItemCode AS ProductCode,
    i.ItemNameAr AS ProductName,
    b.BOMVersion,
    b.BatchSize,
    b.BatchUnitID,
    bu.UnitNameAr AS BatchUnitName,
    b.OutputQty,
    b.OutputUnitID,
    ou.UnitNameAr AS OutputUnitName,
    ISNULL(b.WastePercent, 0) AS WastePercent,
    b.EstimatedTime,
    b.BOMStatus,
    CASE b.BOMStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'نشط'
        WHEN 3 THEN N'متوقف'
        WHEN 4 THEN N'ملغي'
    END AS StatusName,
    ISNULL((SELECT COUNT(*)
            FROM dbo.BOMDetails bd
            INNER JOIN dbo.Items ci ON ci.ItemID = bd.ItemID
            WHERE bd.BOMID = b.BOMID AND bd.MaterialType = 1 AND ci.ItemType = 1), 0) AS RawMaterialCount,
    ISNULL((SELECT COUNT(*) FROM dbo.BOMDetails bd WHERE bd.BOMID = b.BOMID AND bd.MaterialType = 2), 0) AS PackagingMaterialCount,
    ISNULL((SELECT COUNT(*)
            FROM dbo.BOMDetails bd
            INNER JOIN dbo.Items ci ON ci.ItemID = bd.ItemID
            WHERE bd.BOMID = b.BOMID AND bd.MaterialType = 1 AND ci.ItemType = 4), 0) AS FinishedProductComponentCount,
    ISNULL((SELECT SUM(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100) * ISNULL(bd.UnitCost, 0))
            FROM dbo.BOMDetails bd WHERE bd.BOMID = b.BOMID), 0) AS TotalMaterialCost,
    ISNULL(b.LaborCostPerBatch, 0) AS LaborCostPerBatch,
    ISNULL(b.OverheadCostPerBatch, 0) AS OverheadCostPerBatch,
    ISNULL((SELECT SUM(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100) * ISNULL(bd.UnitCost, 0))
            FROM dbo.BOMDetails bd WHERE bd.BOMID = b.BOMID), 0)
        + ISNULL(b.LaborCostPerBatch, 0)
        + ISNULL(b.OverheadCostPerBatch, 0) AS TotalBatchCost,
    ISNULL(b.IsActive, 1) AS IsActive
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items i ON b.ProductItemID = i.ItemID
INNER JOIN dbo.Units bu ON b.BatchUnitID = bu.UnitID
INNER JOIN dbo.Units ou ON b.OutputUnitID = ou.UnitID
WHERE 1 = 1
    AND (@ActiveOnly = 0 OR ISNULL(b.IsActive, 1) = 1)
    AND (@StatusFilter IS NULL OR b.BOMStatus = @StatusFilter)
    AND (
        @Search IS NULL OR @Search = N'' OR
        b.BOMCode LIKE N'%' + @Search + N'%' OR
        b.BOMName LIKE N'%' + @Search + N'%' OR
        i.ItemCode LIKE N'%' + @Search + N'%' OR
        i.ItemNameAr LIKE N'%' + @Search + N'%'
    )
ORDER BY b.BOMStatus, b.BOMCode";

            var result = await connection.QueryAsync<BOMListDto>(sql, new
            {
                Search = search,
                StatusFilter = statusFilter,
                ActiveOnly = activeOnly ? 1 : 0
            });

            return result.ToList();
        }

        // ==========================================
        // جلب وصفة واحدة
        // ==========================================
        public async Task<BOMEditDto?> GetBOMByIdAsync(int bomId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض الوصفات.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    BOMID,
    BOMCode,
    BOMName,
    ProductItemID,
    BOMVersion,
    BatchSize,
    BatchUnitID,
    OutputQty,
    OutputUnitID,
    ISNULL(WastePercent, 3) AS WastePercent,
    EstimatedTime,
    ISNULL(LaborCostPerBatch, 0) AS LaborCostPerBatch,
    ISNULL(OverheadCostPerBatch, 0) AS OverheadCostPerBatch,
    Instructions,
    SafetyNotes,
    QualityNotes,
    Notes,
    BOMStatus,
    ISNULL(IsActive, 1) AS IsActive,
    EffectiveFrom,
    EffectiveTo,
    ApprovedBy,
    ApprovedDate
FROM dbo.BillOfMaterials
WHERE BOMID = @BOMID";

            return await connection.QueryFirstOrDefaultAsync<BOMEditDto>(sql, new { BOMID = bomId });
        }

        // ==========================================
        // إضافة وصفة جديدة
        // ==========================================
        public async Task<int> InsertBOMAsync(BOMEditDto bom, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanAdd, "لا تملك صلاحية إضافة الوصفات.");
            ValidateBOMHeader(bom);

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

            string bomCode;
            int nextVersion;
            int newId;
            try
            {
                await ValidateBOMReferencesAsync(connection, transaction, bom);
                bomCode = await GenerateBOMCodeAsync(connection, transaction);
                nextVersion = await connection.ExecuteScalarAsync<int>(
                    @"SELECT ISNULL(MAX(BOMVersion), 0) + 1
                      FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductItemID = @ProductItemID",
                    new { bom.ProductItemID }, transaction);

                var sql = @"
INSERT INTO dbo.BillOfMaterials
(
    BOMCode, BOMName, ProductItemID, BOMVersion,
    BatchSize, BatchUnitID, OutputQty, OutputUnitID,
    WastePercent, EstimatedTime,
    LaborCostPerBatch, OverheadCostPerBatch,
    Instructions, SafetyNotes, QualityNotes, Notes,
    BOMStatus, EffectiveFrom, EffectiveTo,
    IsActive, CreatedBy, CreatedDate
)
VALUES
(
    @BOMCode, @BOMName, @ProductItemID, @BOMVersion,
    @BatchSize, @BatchUnitID, @OutputQty, @OutputUnitID,
    @WastePercent, @EstimatedTime,
    @LaborCostPerBatch, @OverheadCostPerBatch,
    @Instructions, @SafetyNotes, @QualityNotes, @Notes,
    1, @EffectiveFrom, @EffectiveTo,
    1, @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    BOMCode = bomCode,
                    bom.BOMName,
                    bom.ProductItemID,
                    BOMVersion = nextVersion,
                    bom.BatchSize,
                    bom.BatchUnitID,
                    bom.OutputQty,
                    bom.OutputUnitID,
                    bom.WastePercent,
                    bom.EstimatedTime,
                    bom.LaborCostPerBatch,
                    bom.OverheadCostPerBatch,
                    bom.Instructions,
                    bom.SafetyNotes,
                    bom.QualityNotes,
                    bom.Notes,
                    bom.EffectiveFrom,
                    bom.EffectiveTo,
                    UserID = userId
                }, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 1,
                tableName: "BillOfMaterials",
                recordId: newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                {
                    BOMCode = bomCode,
                    bom.BOMName,
                    bom.ProductItemID,
                    BOMVersion = nextVersion
                }),
                moduleName: "SCR_BOM",
                description: $"إضافة وصفة جديدة: {bom.BOMName} ({bomCode})"
            );

            return newId;
        }

        // ==========================================
        // تعديل وصفة
        // ==========================================
        public async Task UpdateBOMAsync(BOMEditDto bom, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تعديل الوصفات.");
            ValidateBOMHeader(bom);

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var oldBom = await connection.QueryFirstOrDefaultAsync<BOMEditDto>(
                @"SELECT
                      BOMID, BOMName, ProductItemID, BOMVersion,
                      BatchSize, BatchUnitID, OutputQty, OutputUnitID,
                      WastePercent, EstimatedTime,
                      LaborCostPerBatch, OverheadCostPerBatch,
                      Instructions, SafetyNotes, QualityNotes, Notes,
                      BOMStatus, IsActive, EffectiveFrom, EffectiveTo
                  FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
                  WHERE BOMID = @BOMID",
                new { bom.BOMID },
                transaction);
            if (oldBom is null)
                throw new InvalidOperationException("الوصفة غير موجودة.");
            if (oldBom.BOMStatus != 1 && oldBom.BOMStatus != 3)
                throw new InvalidOperationException("لا يمكن تعديل وصفة غير مسودة أو متوقفة؛ أوقفها أو أنشئ نسخة جديدة.");
            await ValidateBOMReferencesAsync(connection, transaction, bom);
            if (oldBom.ProductItemID != bom.ProductItemID)
            {
                var detailCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                    new { bom.BOMID }, transaction);
                if (detailCount > 0)
                    throw new InvalidOperationException("لا يمكن تغيير منتج الوصفة بعد إضافة مكوناتها.");
            }

            var incompatiblePercentageLines = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.BOMDetails bd WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Units u ON u.UnitID=bd.UnitID
WHERE bd.BOMID=@BOMID AND bd.InputPercentage IS NOT NULL
  AND (bd.MaterialType=2 OR bd.UnitID<>@OutputUnitID OR ISNULL(u.UnitType,N'')<>N'Weight')",
                new { bom.BOMID, bom.OutputUnitID }, transaction);
            if (incompatiblePercentageLines > 0)
                throw new InvalidOperationException("توجد مكونات محفوظة كنسبة لا تطابق وحدة وزن الناتج؛ طابق الوحدة أو غيّر نمط المكوّن إلى كمية أولاً.");
            var totalInputPercentage = await connection.ExecuteScalarAsync<decimal>(@"
SELECT ISNULL(SUM(InputPercentage),0) FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND InputPercentage IS NOT NULL",
                new { bom.BOMID }, transaction);
            if (totalInputPercentage > 100m)
                throw new InvalidOperationException("مجموع نسب مكونات الوصفة لا يمكن أن يتجاوز 100%.");

            var sql = @"
UPDATE dbo.BillOfMaterials SET
    BOMName = @BOMName,
    ProductItemID = @ProductItemID,
    BatchSize = @BatchSize,
    BatchUnitID = @BatchUnitID,
    OutputQty = @OutputQty,
    OutputUnitID = @OutputUnitID,
    WastePercent = @WastePercent,
    EstimatedTime = @EstimatedTime,
    LaborCostPerBatch = @LaborCostPerBatch,
    OverheadCostPerBatch = @OverheadCostPerBatch,
    Instructions = @Instructions,
    SafetyNotes = @SafetyNotes,
    QualityNotes = @QualityNotes,
    Notes = @Notes,
    EffectiveFrom = @EffectiveFrom,
    EffectiveTo = @EffectiveTo,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE BOMID = @BOMID";

            var rowsUpdated = await connection.ExecuteAsync(sql, new
            {
                bom.BOMID,
                bom.BOMName,
                bom.ProductItemID,
                bom.BatchSize,
                bom.BatchUnitID,
                bom.OutputQty,
                bom.OutputUnitID,
                bom.WastePercent,
                bom.EstimatedTime,
                bom.LaborCostPerBatch,
                bom.OverheadCostPerBatch,
                bom.Instructions,
                bom.SafetyNotes,
                bom.QualityNotes,
                bom.Notes,
                bom.EffectiveFrom,
                bom.EffectiveTo,
                UserID = userId
            }, transaction);
            if (rowsUpdated != 1)
                throw new InvalidOperationException("لم يتم تعديل الوصفة؛ أعد تحميل الصفحة وحاول مجدداً.");

            var recalculatedPercentageLineCount = 0;
            if (oldBom != null &&
                (oldBom.OutputQty != bom.OutputQty || oldBom.OutputUnitID != bom.OutputUnitID))
            {
                recalculatedPercentageLineCount = await connection.ExecuteAsync(@"
UPDATE bd
SET Quantity = ROUND(@OutputQty * bd.InputPercentage / 100.0, 6)
FROM dbo.BOMDetails bd
WHERE bd.BOMID = @BOMID
  AND bd.InputPercentage IS NOT NULL",
                    new { bom.BOMID, bom.OutputQty },
                    transaction);
            }

            transaction.Commit();

            var changes = new List<string>();
            if (oldBom != null)
            {
                if (oldBom.BOMName != bom.BOMName) changes.Add("BOMName");
                if (oldBom.ProductItemID != bom.ProductItemID) changes.Add("ProductItemID");
                if (oldBom.BatchSize != bom.BatchSize) changes.Add("BatchSize");
                if (oldBom.BatchUnitID != bom.BatchUnitID) changes.Add("BatchUnitID");
                if (oldBom.OutputQty != bom.OutputQty) changes.Add("OutputQty");
                if (oldBom.OutputUnitID != bom.OutputUnitID) changes.Add("OutputUnitID");
                if (oldBom.WastePercent != bom.WastePercent) changes.Add("WastePercent");
                if (oldBom.EstimatedTime != bom.EstimatedTime) changes.Add("EstimatedTime");
                if (oldBom.LaborCostPerBatch != bom.LaborCostPerBatch) changes.Add("LaborCostPerBatch");
                if (oldBom.OverheadCostPerBatch != bom.OverheadCostPerBatch) changes.Add("OverheadCostPerBatch");
                if (oldBom.Instructions != bom.Instructions) changes.Add("Instructions");
                if (oldBom.SafetyNotes != bom.SafetyNotes) changes.Add("SafetyNotes");
                if (oldBom.QualityNotes != bom.QualityNotes) changes.Add("QualityNotes");
                if (oldBom.Notes != bom.Notes) changes.Add("Notes");
                if (oldBom.EffectiveFrom != bom.EffectiveFrom) changes.Add("EffectiveFrom");
                if (oldBom.EffectiveTo != bom.EffectiveTo) changes.Add("EffectiveTo");
            }
            if (recalculatedPercentageLineCount > 0)
                changes.Add($"BOMDetails.Quantity (percentage lines: {recalculatedPercentageLineCount})");

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "BillOfMaterials",
                recordId: bom.BOMID.ToString(),
                oldValues: oldBom != null
                    ? System.Text.Json.JsonSerializer.Serialize(new
                    {
                        oldBom.BOMName,
                        oldBom.ProductItemID,
                        oldBom.BatchSize,
                        oldBom.OutputQty
                    })
                    : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                {
                    bom.BOMName,
                    bom.ProductItemID,
                    bom.BatchSize,
                    bom.OutputQty
                }),
                changedColumns: changes.Any() ? string.Join(",", changes) : null,
                moduleName: "SCR_BOM",
                description: $"تعديل وصفة: {bom.BOMName}"
            );
        }

        // ==========================================
        // حذف منطقي / إلغاء
        // ==========================================
        public async Task<(bool Success, string Message)> SoftDeleteBOMAsync(int bomId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanDelete, "لا تملك صلاحية حذف الوصفات.");

            using var connection = CreateConnection();

            var current = await connection.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT BOMID, BOMCode, BOMName, BOMStatus
                  FROM dbo.BillOfMaterials
                  WHERE BOMID = @BOMID",
                new { BOMID = bomId });

            if (current == null)
                return (false, "الوصفة غير موجودة");

            if ((int)current.BOMStatus == 4)
                return (false, "الوصفة ملغاة بالفعل");

            if ((int)current.BOMStatus == 2)
                return (false, "لا يمكن إلغاء وصفة نشطة، يجب إيقافها أولاً");

            await connection.ExecuteAsync(
                @"UPDATE dbo.BillOfMaterials
                  SET BOMStatus = 4,
                      IsActive = 0,
                      ModifiedBy = @UserID,
                      ModifiedDate = GETDATE()
                  WHERE BOMID = @BOMID",
                new { BOMID = bomId, UserID = userId });

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "BillOfMaterials",
                recordId: bomId.ToString(),
                moduleName: "SCR_BOM",
                description: $"إلغاء وصفة: {current.BOMCode}"
            );

            return (true, "تم إلغاء الوصفة بنجاح");
        }

        // ==========================================
        // تغيير الحالة
        // 2 = نشط
        // 3 = متوقف
        // ==========================================
        public async Task<(bool Success, string Message)> ChangeStatusAsync(
            int bomId,
            int newStatus,
            int userId,
            int? employeeId = null)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM",
                p => newStatus switch
                {
                    2 => p.CanApprove,
                    3 => p.CanEdit,
                    4 => p.CanDelete,
                    _ => false
                }, "لا تملك صلاحية تغيير حالة الوصفة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            var current = await connection.QueryFirstOrDefaultAsync<BOMActivationState>(@"
SELECT BOMID,BOMCode,ProductItemID,BOMStatus,ISNULL(IsActive,1) AS IsActive
FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                new { BOMID = bomId }, transaction);
            if (current is null)
                return (false, "الوصفة غير موجودة");

            if (newStatus == 2)
            {
                if ((current.BOMStatus != 1 && current.BOMStatus != 3) || !current.IsActive)
                    return (false, "لا يمكن تنشيط الوصفة من هذه الحالة");

                var otherActiveVersion = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE ProductItemID=@ProductItemID AND BOMStatus=2 AND ISNULL(IsActive,1)=1 AND BOMID<>@BOMID",
                    new { current.ProductItemID, current.BOMID }, transaction);
                if (otherActiveVersion > 0)
                    return (false, "يوجد إصدار نشط آخر لهذا المنتج؛ استخدم مسار تبديل الإصدار للتحقق من الوصفتين.");

                try { await ValidateBOMForActivationAsync(connection, transaction, bomId); }
                catch (InvalidOperationException ex) { return (false, ex.Message); }

                var updated = await connection.ExecuteAsync(@"
UPDATE dbo.BillOfMaterials
SET BOMStatus=2, ModifiedBy=@UserID, ModifiedDate=GETDATE(),
    ApprovedBy=CASE WHEN @EmployeeID IS NULL OR @EmployeeID=0 THEN ApprovedBy ELSE @EmployeeID END,
    ApprovedDate=CASE WHEN @EmployeeID IS NULL OR @EmployeeID=0 THEN ApprovedDate ELSE GETDATE() END
WHERE BOMID=@BOMID AND BOMStatus IN (1,3) AND ISNULL(IsActive,1)=1",
                    new { BOMID = bomId, UserID = userId, EmployeeID = employeeId }, transaction);
                if (updated != 1)
                    return (false, "تغيرت حالة الوصفة أثناء التنشيط؛ حدّث الصفحة وحاول مجدداً.");

                transaction.Commit();
                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "BillOfMaterials",
                    recordId: bomId.ToString(),
                    moduleName: "SCR_BOM",
                    description: $"تنشيط وصفة: {current.BOMCode}"
                );
                return (true, "تم تنشيط الوصفة بنجاح");
            }

            if (newStatus == 3)
            {
                if (current.BOMStatus != 2)
                    return (false, "لا يمكن إيقاف إلا وصفة نشطة");

                var updated = await connection.ExecuteAsync(@"
UPDATE dbo.BillOfMaterials
SET BOMStatus=3, ModifiedBy=@UserID, ModifiedDate=GETDATE()
WHERE BOMID=@BOMID AND BOMStatus=2",
                    new { BOMID = bomId, UserID = userId }, transaction);
                if (updated != 1)
                    return (false, "تغيرت حالة الوصفة أثناء الإيقاف؛ حدّث الصفحة وحاول مجدداً.");

                transaction.Commit();
                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "BillOfMaterials",
                    recordId: bomId.ToString(),
                    moduleName: "SCR_BOM",
                    description: $"إيقاف وصفة: {current.BOMCode}"
                );
                return (true, "تم إيقاف الوصفة بنجاح");
            }

            return (false, "الحالة المطلوبة غير مدعومة");
        }

        // ==========================================
        // فحص وجود وصفة نشطة لنفس المنتج
        // ==========================================
        public async Task<(bool Exists, int ExistingBOMID, string ExistingBOMCode)> CheckActiveVersionExistsAsync(
            int productItemId,
            int excludeBomId = 0)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض الوصفات.");

            using var connection = CreateConnection();

            var existing = await connection.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT TOP 1 BOMID, BOMCode
                  FROM dbo.BillOfMaterials
                  WHERE ProductItemID = @ProductItemID
                    AND BOMStatus = 2
                    AND ISNULL(IsActive, 1) = 1
                    AND BOMID <> @ExcludeBOMID
                  ORDER BY BOMVersion DESC",
                new
                {
                    ProductItemID = productItemId,
                    ExcludeBOMID = excludeBomId
                });

            if (existing == null)
                return (false, 0, "");

            return (true, (int)existing.BOMID, (string)existing.BOMCode);
        }

        // ==========================================
        // تنشيط الجديدة + إيقاف القديمة
        // ==========================================
        public async Task<(bool Success, string Message)> ActivateAndDeactivateOldAsync(
            int newBomId,
            int oldBomId,
            int userId,
            int? employeeId = null)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanApprove, "لا تملك صلاحية اعتماد إصدار الوصفة.");
            if (newBomId <= 0 || oldBomId <= 0 || newBomId == oldBomId)
                return (false, "معرّفا الإصدارين غير صالحين.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            BOMActivationState? newBom = null;
            BOMActivationState? oldBom = null;
            try
            {
                // Lock in a stable key order to avoid reverse-order swaps deadlocking.
                foreach (var id in new[] { newBomId, oldBomId }.OrderBy(x => x))
                {
                    var state = await connection.QueryFirstOrDefaultAsync<BOMActivationState>(@"
SELECT BOMID,BOMCode,ProductItemID,BOMStatus,ISNULL(IsActive,1) AS IsActive
FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID",
                        new { BOMID = id }, transaction);
                    if (id == newBomId) newBom = state;
                    else oldBom = state;
                }

                if (newBom is null || oldBom is null)
                    throw new InvalidOperationException("الوصفة الجديدة أو الإصدار النشط غير موجود.");
                if (newBom.ProductItemID != oldBom.ProductItemID)
                    throw new InvalidOperationException("لا يمكن تبديل إصدارين لمنتجين مختلفين.");
                if (!newBom.IsActive || (newBom.BOMStatus != 1 && newBom.BOMStatus != 3))
                    throw new InvalidOperationException("الإصدار الجديد يجب أن يكون مسودة أو متوقفاً وغير ملغى.");
                if (!oldBom.IsActive || oldBom.BOMStatus != 2)
                    throw new InvalidOperationException("الإصدار القديم لم يعد نشطاً؛ حدّث القائمة وحاول مجدداً.");

                var thirdActiveVersion = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
WHERE ProductItemID=@ProductItemID AND BOMStatus=2 AND ISNULL(IsActive,1)=1
  AND BOMID NOT IN (@NewBOMID,@OldBOMID)",
                    new { newBom.ProductItemID, NewBOMID = newBomId, OldBOMID = oldBomId }, transaction);
                if (thirdActiveVersion > 0)
                    throw new InvalidOperationException("يوجد إصدار نشط ثالث لهذا المنتج؛ أوقف التعارض قبل التبديل.");

                await ValidateBOMForActivationAsync(connection, transaction, newBomId);

                var stopped = await connection.ExecuteAsync(@"
UPDATE dbo.BillOfMaterials
SET BOMStatus=3, ModifiedBy=@UserID, ModifiedDate=GETDATE()
WHERE BOMID=@BOMID AND BOMStatus=2 AND ISNULL(IsActive,1)=1",
                    new { BOMID = oldBomId, UserID = userId }, transaction);
                if (stopped != 1)
                    throw new InvalidOperationException("تعذر إيقاف الإصدار القديم؛ لم يُبدّل أي إصدار.");

                var activated = await connection.ExecuteAsync(@"
UPDATE dbo.BillOfMaterials
SET BOMStatus=2, ModifiedBy=@UserID, ModifiedDate=GETDATE(),
    ApprovedBy=CASE WHEN @EmployeeID IS NULL OR @EmployeeID=0 THEN ApprovedBy ELSE @EmployeeID END,
    ApprovedDate=CASE WHEN @EmployeeID IS NULL OR @EmployeeID=0 THEN ApprovedDate ELSE GETDATE() END
WHERE BOMID=@BOMID AND BOMStatus IN (1,3) AND ISNULL(IsActive,1)=1",
                    new { BOMID = newBomId, UserID = userId, EmployeeID = employeeId }, transaction);
                if (activated != 1)
                    throw new InvalidOperationException("تعذر تنشيط الإصدار الجديد؛ أُلغيت عملية التبديل كاملة.");

                transaction.Commit();
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); } catch { }
                return (false, $"حدث خطأ أثناء تبديل الحالة: {ex.Message}");
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "BillOfMaterials",
                recordId: newBomId.ToString(),
                moduleName: "SCR_BOM",
                description: $"تنشيط وصفة {newBomId} وإيقاف الوصفة القديمة {oldBomId}"
            );
            return (true, "تم تنشيط الوصفة الجديدة وإيقاف القديمة بنجاح");
        }

        // ==========================================
        // نسخ الوصفة
        // ==========================================
        public async Task<(bool Success, int NewBOMID, string Message)> CopyBOMAsync(int sourceBomId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanAdd, "لا تملك صلاحية نسخ الوصفات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

            try
            {
                var source = await connection.QueryFirstOrDefaultAsync<BOMEditDto>(
                    @"SELECT
                          BOMID, BOMCode, BOMName, ProductItemID, BOMVersion,
                          BatchSize, BatchUnitID, OutputQty, OutputUnitID,
                          WastePercent, EstimatedTime,
                          LaborCostPerBatch, OverheadCostPerBatch,
                          Instructions, SafetyNotes, QualityNotes, Notes,
                          EffectiveFrom, EffectiveTo
                      FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
                      WHERE BOMID = @BOMID",
                    new { BOMID = sourceBomId },
                    transaction);

                if (source == null)
                    return (false, 0, "الوصفة الأصلية غير موجودة");
                ValidateBOMHeader(source);
                await ValidateBOMReferencesAsync(connection, transaction, source);

                var nextVersion = await connection.ExecuteScalarAsync<int>(
                    @"SELECT ISNULL(MAX(BOMVersion), 0) + 1
                      FROM dbo.BillOfMaterials WITH (UPDLOCK,HOLDLOCK)
                      WHERE ProductItemID = @ProductItemID",
                    new { source.ProductItemID },
                    transaction);

                var newCode = await GenerateBOMCodeAsync(connection, transaction);

                var insertSql = @"
INSERT INTO dbo.BillOfMaterials
(
    BOMCode, BOMName, ProductItemID, BOMVersion,
    BatchSize, BatchUnitID, OutputQty, OutputUnitID,
    WastePercent, EstimatedTime,
    LaborCostPerBatch, OverheadCostPerBatch,
    Instructions, SafetyNotes, QualityNotes, Notes,
    BOMStatus, IsActive, EffectiveFrom, EffectiveTo,
    CreatedBy, CreatedDate
)
VALUES
(
    @BOMCode, @BOMName, @ProductItemID, @BOMVersion,
    @BatchSize, @BatchUnitID, @OutputQty, @OutputUnitID,
    @WastePercent, @EstimatedTime,
    @LaborCostPerBatch, @OverheadCostPerBatch,
    @Instructions, @SafetyNotes, @QualityNotes, @Notes,
    1, 1, @EffectiveFrom, @EffectiveTo,
    @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newBomId = await connection.ExecuteScalarAsync<int>(insertSql, new
                {
                    BOMCode = newCode,
                    BOMName = source.BOMName + " (نسخة)",
                    source.ProductItemID,
                    BOMVersion = nextVersion,
                    source.BatchSize,
                    source.BatchUnitID,
                    source.OutputQty,
                    source.OutputUnitID,
                    source.WastePercent,
                    source.EstimatedTime,
                    source.LaborCostPerBatch,
                    source.OverheadCostPerBatch,
                    source.Instructions,
                    source.SafetyNotes,
                    source.QualityNotes,
                    source.Notes,
                    source.EffectiveFrom,
                    source.EffectiveTo,
                    UserID = userId
                }, transaction);

                await connection.ExecuteAsync(@"
INSERT INTO dbo.BOMDetails
(
    BOMID, LineNumber, ItemID, MaterialType, UnitID,
    Quantity, InputPercentage, WastePercent, UnitCost,
    IsOptional, SubstituteItemID, Sequence, StageNumber, Notes
)
SELECT
    @NewBOMID, LineNumber, ItemID, MaterialType, UnitID,
    Quantity, InputPercentage, WastePercent, UnitCost,
    ISNULL(IsOptional, 0), SubstituteItemID, ISNULL(Sequence, 0), ISNULL(StageNumber, 1), Notes
FROM dbo.BOMDetails
WHERE BOMID = @SourceBOMID",
                    new
                    {
                        NewBOMID = newBomId,
                        SourceBOMID = sourceBomId
                    },
                    transaction);

                await connection.ExecuteAsync(@"
INSERT INTO dbo.BOMStages
(
    BOMID, StageID, StageOrder, Duration,
    Temperature, Speed, QualityCheckRequired, QualityCheckNotes,
    Instructions, Notes
)
SELECT
    @NewBOMID, StageID, StageOrder, Duration,
    Temperature, Speed, ISNULL(QualityCheckRequired, 0), QualityCheckNotes,
    Instructions, Notes
FROM dbo.BOMStages
WHERE BOMID = @SourceBOMID",
                    new
                    {
                        NewBOMID = newBomId,
                        SourceBOMID = sourceBomId
                    },
                    transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 1,
                    tableName: "BillOfMaterials",
                    recordId: newBomId.ToString(),
                    moduleName: "SCR_BOM",
                    description: $"نسخ وصفة من {sourceBomId} إلى {newBomId}"
                );

                return (true, newBomId, "تم نسخ الوصفة بنجاح");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, 0, $"خطأ أثناء نسخ الوصفة: {ex.Message}");
            }
        }

        // ==========================================
        // تفاصيل الوصفة
        // ==========================================
        public async Task<List<BOMDetailDto>> GetBOMDetailsAsync(int bomId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض تفاصيل الوصفات.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    bd.BOMDetailID,
    bd.BOMID,
    bd.LineNumber,
    bd.ItemID,
    i.ItemCode,
    i.ItemNameAr AS ItemName,
    CASE WHEN bd.MaterialType = 1 AND i.ItemType = 4 THEN 3 ELSE bd.MaterialType END AS MaterialType,
    CASE
        WHEN bd.MaterialType = 1 AND i.ItemType = 4 THEN N'منتج نهائي'
        WHEN bd.MaterialType = 1 THEN N'خام'
        WHEN bd.MaterialType = 2 THEN N'تعبئة'
        ELSE N'غير معروف'
    END AS MaterialTypeName,
    bd.UnitID,
    u.UnitNameAr AS UnitName,
    bd.Quantity,
    bd.InputPercentage,
    ISNULL(bd.WastePercent, 0) AS WastePercent,
    (bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0)) AS NetQuantity,
    ISNULL(bd.UnitCost, 0) AS UnitCost,
    (bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0) * ISNULL(bd.UnitCost, 0)) AS LineCost,
    ISNULL(bd.IsOptional, 0) AS IsOptional,
    bd.SubstituteItemID,
    si.ItemCode AS SubstituteItemCode,
    si.ItemNameAr AS SubstituteItemName,
    ISNULL(bd.Sequence, 0) AS Sequence,
    ISNULL(bd.StageNumber, 1) AS StageNumber,
    bd.Notes
FROM dbo.BOMDetails bd
INNER JOIN dbo.Items i ON bd.ItemID = i.ItemID
INNER JOIN dbo.Units u ON bd.UnitID = u.UnitID
LEFT JOIN dbo.Items si ON bd.SubstituteItemID = si.ItemID
WHERE bd.BOMID = @BOMID
ORDER BY bd.MaterialType, bd.LineNumber";

            var result = await connection.QueryAsync<BOMDetailDto>(sql, new { BOMID = bomId });
            return result.ToList();
        }

        public async Task<int> InsertBOMDetailAsync(BOMDetailDto detail, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تعديل مكونات الوصفة.");
            var unitCost = await GetItemCostAsync(detail.ItemID);
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            int newId;
            try
            {
                await EnsureEditableBOMAsync(connection, transaction, detail.BOMID);
                await ValidateBOMDetailAsync(connection, transaction, detail);
                var nextLine = await connection.ExecuteScalarAsync<int>(
                    @"SELECT ISNULL(MAX(LineNumber), 0) + 1
                      FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
                      WHERE BOMID = @BOMID",
                    new { detail.BOMID }, transaction);

                // Logical type 3 (finished product component) is persisted as material type 1.
                var sql = @"
INSERT INTO dbo.BOMDetails
(
    BOMID, LineNumber, ItemID, MaterialType, UnitID,
    Quantity, InputPercentage, WastePercent, UnitCost,
    IsOptional, SubstituteItemID, Sequence, StageNumber, Notes
)
VALUES
(
    @BOMID, @LineNumber, @ItemID, @MaterialType, @UnitID,
    @Quantity, @InputPercentage, @WastePercent, @UnitCost,
    @IsOptional, NULLIF(@SubstituteItemID, 0), @Sequence, @StageNumber, @Notes
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    detail.BOMID,
                    LineNumber = nextLine,
                    detail.ItemID,
                    MaterialType = detail.MaterialType == 3 ? 1 : detail.MaterialType,
                    detail.UnitID,
                    detail.Quantity,
                    detail.InputPercentage,
                    detail.WastePercent,
                    UnitCost = unitCost,
                    detail.IsOptional,
                    detail.SubstituteItemID,
                    Sequence = detail.Sequence <= 0 ? 0 : detail.Sequence,
                    StageNumber = detail.StageNumber <= 0 ? 1 : detail.StageNumber,
                    detail.Notes
                }, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 1,
                tableName: "BOMDetails",
                recordId: newId.ToString(),
                moduleName: "SCR_BOM",
                description: $"إضافة مكون لوصفة رقم: {detail.BOMID}"
            );
            return newId;
        }

        public async Task UpdateBOMDetailAsync(BOMDetailDto detail, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تعديل مكونات الوصفة.");
            if (detail.BOMDetailID <= 0 || detail.BOMID <= 0)
                throw new InvalidOperationException("معرّف مكوّن الوصفة غير صالح.");

            var unitCost = await GetItemCostAsync(detail.ItemID);
            using var connection = CreateConnection();
            await connection.OpenAsync();
            var actualBOMID = await connection.ExecuteScalarAsync<int?>(@"
SELECT BOMID FROM dbo.BOMDetails WHERE BOMDetailID=@BOMDetailID",
                new { detail.BOMDetailID });
            if (!actualBOMID.HasValue || actualBOMID.Value != detail.BOMID)
                throw new InvalidOperationException("مكوّن الوصفة غير موجود أو لا يتبع الوصفة المحددة.");

            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                await EnsureEditableBOMAsync(connection, transaction, actualBOMID.Value);
                var lockedBOMID = await connection.ExecuteScalarAsync<int?>(@"
SELECT BOMID FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMDetailID=@BOMDetailID",
                    new { detail.BOMDetailID }, transaction);
                if (!lockedBOMID.HasValue || lockedBOMID.Value != actualBOMID.Value)
                    throw new InvalidOperationException("تغير مكوّن الوصفة أثناء التعديل؛ أعد تحميل الصفحة.");
                await ValidateBOMDetailAsync(connection, transaction, detail, detail.BOMDetailID);

                var rowsUpdated = await connection.ExecuteAsync(@"
UPDATE dbo.BOMDetails SET
    ItemID = @ItemID,
    MaterialType = @MaterialType,
    UnitID = @UnitID,
    Quantity = @Quantity,
    InputPercentage = @InputPercentage,
    WastePercent = @WastePercent,
    UnitCost = @UnitCost,
    IsOptional = @IsOptional,
    SubstituteItemID = NULLIF(@SubstituteItemID, 0),
    Sequence = @Sequence,
    StageNumber = @StageNumber,
    Notes = @Notes
WHERE BOMDetailID = @BOMDetailID AND BOMID = @BOMID",
                    new
                    {
                        detail.BOMDetailID,
                        detail.BOMID,
                        detail.ItemID,
                        MaterialType = detail.MaterialType == 3 ? 1 : detail.MaterialType,
                        detail.UnitID,
                        detail.Quantity,
                        detail.InputPercentage,
                        detail.WastePercent,
                        UnitCost = unitCost,
                        detail.IsOptional,
                        detail.SubstituteItemID,
                        Sequence = detail.Sequence <= 0 ? 0 : detail.Sequence,
                        StageNumber = detail.StageNumber <= 0 ? 1 : detail.StageNumber,
                        detail.Notes
                    }, transaction);
                if (rowsUpdated != 1)
                    throw new InvalidOperationException("لم يتم تعديل المكوّن؛ أعد تحميل الصفحة وحاول مجدداً.");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "BOMDetails",
                recordId: detail.BOMDetailID.ToString(),
                moduleName: "SCR_BOM",
                description: $"تعديل مكون بوصفة رقم: {detail.BOMID}"
            );
        }

        public async Task DeleteBOMDetailAsync(int bomDetailId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanDelete, "لا تملك صلاحية حذف مكونات الوصفة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            var bomId = await connection.QueryFirstOrDefaultAsync<int?>(
                @"SELECT BOMID FROM dbo.BOMDetails WHERE BOMDetailID=@ID",
                new { ID = bomDetailId });
            if (!bomId.HasValue)
                return;

            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                await EnsureEditableBOMAsync(connection, transaction, bomId.Value);
                var lockedBOMID = await connection.QueryFirstOrDefaultAsync<int?>(@"
SELECT BOMID FROM dbo.BOMDetails WITH (UPDLOCK,HOLDLOCK)
WHERE BOMDetailID=@ID",
                    new { ID = bomDetailId }, transaction);
                if (!lockedBOMID.HasValue || lockedBOMID.Value != bomId.Value)
                    throw new InvalidOperationException("تغير مكوّن الوصفة أثناء الحذف؛ أعد تحميل الصفحة.");

                var rowsDeleted = await connection.ExecuteAsync(
                    @"DELETE FROM dbo.BOMDetails WHERE BOMDetailID = @ID AND BOMID = @BOMID",
                    new { ID = bomDetailId, BOMID = bomId.Value },
                    transaction);
                if (rowsDeleted != 1)
                    throw new InvalidOperationException("لم يتم حذف المكوّن؛ أعد تحميل الصفحة وحاول مجدداً.");

                var remainingCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.BOMDetails WHERE BOMID = @BOMID",
                    new { BOMID = bomId.Value },
                    transaction);

                if (remainingCount > 0)
                {
                    var maxLineNumber = await connection.ExecuteScalarAsync<int>(
                        @"SELECT ISNULL(MAX(LineNumber), 0)
                          FROM dbo.BOMDetails
                          WHERE BOMID = @BOMID",
                        new { BOMID = bomId.Value },
                        transaction);

                    // Move lines above the current range first so the unique (BOMID, LineNumber)
                    // index cannot collide while the visible line numbers are compacted.
                    await connection.ExecuteAsync(
                        @"UPDATE dbo.BOMDetails
                          SET LineNumber = LineNumber + @Offset
                          WHERE BOMID = @BOMID",
                        new { BOMID = bomId.Value, Offset = maxLineNumber + 1 },
                        transaction);

                    await connection.ExecuteAsync(@"
;WITH OrderedLines AS
(
    SELECT BOMDetailID,
           ROW_NUMBER() OVER (ORDER BY MaterialType, LineNumber) AS NewLineNumber
    FROM dbo.BOMDetails
    WHERE BOMID = @BOMID
)
UPDATE detail
SET LineNumber = ordered.NewLineNumber
FROM dbo.BOMDetails detail
INNER JOIN OrderedLines ordered ON ordered.BOMDetailID = detail.BOMDetailID;",
                        new { BOMID = bomId.Value },
                        transaction);
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "BOMDetails",
                recordId: bomDetailId.ToString(),
                moduleName: "SCR_BOM",
                description: $"حذف مكون من الوصفة رقم: {bomId.Value} مع إعادة ترقيم المكونات المتبقية"
            );
        }

        public async Task<bool> IsDuplicateItemAsync(int bomId, int itemId, int excludeDetailId = 0)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية فحص مكونات الوصفة.");

            using var connection = CreateConnection();

            var count = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM dbo.BOMDetails
                  WHERE BOMID = @BOMID
                    AND ItemID = @ItemID
                    AND BOMDetailID <> @ExcludeID",
                new
                {
                    BOMID = bomId,
                    ItemID = itemId,
                    ExcludeID = excludeDetailId
                });

            return count > 0;
        }

        public async Task<bool> WouldCreateCircularBOMAsync(
            int parentProductItemId,
            int childFinishedProductItemId,
            int excludeDetailId = 0)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية فحص الوصفة.");

            using var connection = CreateConnection();

            const string sql = @"
;WITH BOMGraph AS
(
    SELECT b.ProductItemID AS ParentItemID, bd.ItemID AS ChildItemID
    FROM dbo.BillOfMaterials b
    INNER JOIN dbo.BOMDetails bd ON bd.BOMID = b.BOMID
    INNER JOIN dbo.Items childItem ON childItem.ItemID = bd.ItemID
    WHERE bd.MaterialType = 1
      AND childItem.ItemType = 4
      AND bd.BOMDetailID <> @ExcludeDetailID
), Reachable AS
(
    SELECT @ChildItemID AS ItemID,
           CAST('/' + CONVERT(varchar(20), @ChildItemID) + '/' AS varchar(max)) AS ItemPath
    UNION ALL
    SELECT graph.ChildItemID,
           CAST(reachable.ItemPath + CONVERT(varchar(20), graph.ChildItemID) + '/' AS varchar(max))
    FROM Reachable reachable
    INNER JOIN BOMGraph graph ON graph.ParentItemID = reachable.ItemID
    WHERE CHARINDEX('/' + CONVERT(varchar(20), graph.ChildItemID) + '/', reachable.ItemPath) = 0
)
SELECT CAST(CASE WHEN EXISTS
    (SELECT 1 FROM Reachable WHERE ItemID = @ParentItemID)
    THEN 1 ELSE 0 END AS bit)
OPTION (MAXRECURSION 32767);";

            return await connection.ExecuteScalarAsync<bool>(sql, new
            {
                ParentItemID = parentProductItemId,
                ChildItemID = childFinishedProductItemId,
                ExcludeDetailID = excludeDetailId
            });
        }

        // ==========================================
        // مراحل الوصفة
        // ==========================================
        public async Task<List<BOMStageDto>> GetBOMStagesAsync(int bomId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض مراحل الوصفة.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    bs.BOMStageID,
    bs.BOMID,
    bs.StageID,
    ps.StageNameAr AS StageName,
    bs.StageOrder,
    bs.Duration,
    bs.Temperature,
    bs.Speed,
    ISNULL(bs.QualityCheckRequired, 0) AS QualityCheckRequired,
    bs.QualityCheckNotes,
    bs.Instructions,
    bs.Notes
FROM dbo.BOMStages bs
INNER JOIN dbo.ProductionStages ps ON bs.StageID = ps.StageID
WHERE bs.BOMID = @BOMID
ORDER BY bs.StageOrder";

            var result = await connection.QueryAsync<BOMStageDto>(sql, new { BOMID = bomId });
            return result.ToList();
        }

        public async Task<int> InsertBOMStageAsync(BOMStageDto stage, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تعديل مراحل الوصفة.");
            if (stage.StageID <= 0 || stage.StageOrder <= 0)
                throw new InvalidOperationException("حدد مرحلة وترتيباً صحيحاً.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            int newId;
            try
            {
                await EnsureEditableBOMAsync(connection, transaction, stage.BOMID);
                var activeStage = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionStages WITH (UPDLOCK,HOLDLOCK)
WHERE StageID=@StageID AND IsActive=1",
                    new { stage.StageID }, transaction);
                if (activeStage != 1)
                    throw new InvalidOperationException("المرحلة غير موجودة أو غير نشطة.");
                var duplicateStage = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMStages WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND StageID=@StageID",
                    new { stage.BOMID, stage.StageID }, transaction);
                var duplicateOrder = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMStages WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND StageOrder=@StageOrder",
                    new { stage.BOMID, stage.StageOrder }, transaction);
                if (duplicateStage > 0)
                    throw new InvalidOperationException("هذه المرحلة موجودة بالفعل داخل الوصفة.");
                if (duplicateOrder > 0)
                    throw new InvalidOperationException("رقم ترتيب المرحلة مستخدم مسبقاً.");

                var sql = @"
INSERT INTO dbo.BOMStages
(
    BOMID, StageID, StageOrder, Duration,
    Temperature, Speed, QualityCheckRequired, QualityCheckNotes,
    Instructions, Notes
)
VALUES
(
    @BOMID, @StageID, @StageOrder, @Duration,
    @Temperature, @Speed, @QualityCheckRequired, @QualityCheckNotes,
    @Instructions, @Notes
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    stage.BOMID, stage.StageID, stage.StageOrder, stage.Duration,
                    stage.Temperature, stage.Speed, stage.QualityCheckRequired,
                    stage.QualityCheckNotes, stage.Instructions, stage.Notes
                }, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 1,
                tableName: "BOMStages",
                recordId: newId.ToString(),
                moduleName: "SCR_BOM",
                description: $"إضافة مرحلة لوصفة رقم: {stage.BOMID}"
            );
            return newId;
        }

        public async Task UpdateBOMStageAsync(BOMStageDto stage, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تعديل مراحل الوصفة.");
            if (stage.BOMStageID <= 0 || stage.BOMID <= 0 || stage.StageID <= 0 || stage.StageOrder <= 0)
                throw new InvalidOperationException("بيانات مرحلة الوصفة غير مكتملة أو غير صالحة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            // Read the owner without retaining a child lock; the transaction locks the BOM header first and revalidates it.
            var actualBOMID = await connection.ExecuteScalarAsync<int?>(@"
SELECT BOMID FROM dbo.BOMStages
WHERE BOMStageID=@BOMStageID",
                new { stage.BOMStageID });
            if (!actualBOMID.HasValue || actualBOMID.Value != stage.BOMID)
                throw new InvalidOperationException("مرحلة الوصفة غير موجودة أو لا تتبع الوصفة المحددة.");

            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                await EnsureEditableBOMAsync(connection, transaction, actualBOMID.Value);
                var lockedBOMID = await connection.ExecuteScalarAsync<int?>(@"
SELECT BOMID FROM dbo.BOMStages WITH (UPDLOCK,HOLDLOCK)
WHERE BOMStageID=@BOMStageID",
                    new { stage.BOMStageID }, transaction);
                if (!lockedBOMID.HasValue || lockedBOMID.Value != actualBOMID.Value)
                    throw new InvalidOperationException("تغيرت مرحلة الوصفة أثناء التعديل؛ أعد تحميل الصفحة.");
                var activeStage = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.ProductionStages WITH (UPDLOCK,HOLDLOCK)
WHERE StageID=@StageID AND IsActive=1",
                    new { stage.StageID }, transaction);
                if (activeStage != 1)
                    throw new InvalidOperationException("المرحلة غير موجودة أو غير نشطة.");
                var duplicateStage = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMStages WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND StageID=@StageID AND BOMStageID<>@BOMStageID",
                    new { stage.BOMID, stage.StageID, stage.BOMStageID }, transaction);
                var duplicateOrder = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMStages WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND StageOrder=@StageOrder AND BOMStageID<>@BOMStageID",
                    new { stage.BOMID, stage.StageOrder, stage.BOMStageID }, transaction);
                if (duplicateStage > 0)
                    throw new InvalidOperationException("هذه المرحلة موجودة بالفعل داخل الوصفة.");
                if (duplicateOrder > 0)
                    throw new InvalidOperationException("رقم ترتيب المرحلة مستخدم مسبقاً.");

                var rowsUpdated = await connection.ExecuteAsync(@"
UPDATE dbo.BOMStages SET
    StageID = @StageID, StageOrder = @StageOrder, Duration = @Duration,
    Temperature = @Temperature, Speed = @Speed,
    QualityCheckRequired = @QualityCheckRequired, QualityCheckNotes = @QualityCheckNotes,
    Instructions = @Instructions, Notes = @Notes
WHERE BOMStageID = @BOMStageID AND BOMID = @BOMID",
                    new
                    {
                        stage.BOMID, stage.BOMStageID, stage.StageID, stage.StageOrder,
                        stage.Duration, stage.Temperature, stage.Speed,
                        stage.QualityCheckRequired, stage.QualityCheckNotes, stage.Instructions, stage.Notes
                    }, transaction);
                if (rowsUpdated != 1)
                    throw new InvalidOperationException("لم يتم تعديل المرحلة؛ أعد تحميل الصفحة وحاول مجدداً.");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "BOMStages",
                recordId: stage.BOMStageID.ToString(),
                moduleName: "SCR_BOM",
                description: $"تعديل مرحلة بوصفة رقم: {stage.BOMID}"
            );
        }

        public async Task DeleteBOMStageAsync(int bomStageId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanDelete, "لا تملك صلاحية حذف مراحل الوصفة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            // Read the owner without retaining a child lock; the transaction locks the BOM header first and revalidates it.
            var ownerBOMID = await connection.ExecuteScalarAsync<int?>(@"
SELECT BOMID FROM dbo.BOMStages
WHERE BOMStageID=@ID",
                new { ID = bomStageId });
            if (!ownerBOMID.HasValue)
                throw new InvalidOperationException("مرحلة الوصفة غير موجودة.");
            var bomId = ownerBOMID.Value;

            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                await EnsureEditableBOMAsync(connection, transaction, bomId);
                var lockedBOMID = await connection.ExecuteScalarAsync<int?>(@"
SELECT BOMID FROM dbo.BOMStages WITH (UPDLOCK,HOLDLOCK)
WHERE BOMStageID=@ID",
                    new { ID = bomStageId }, transaction);
                if (!lockedBOMID.HasValue || lockedBOMID.Value != bomId)
                    throw new InvalidOperationException("تغيرت مرحلة الوصفة أثناء الحذف؛ أعد تحميل الصفحة.");
                var rowsDeleted = await connection.ExecuteAsync(
                    @"DELETE FROM dbo.BOMStages WHERE BOMStageID = @ID AND BOMID = @BOMID",
                    new { ID = bomStageId, BOMID = bomId }, transaction);
                if (rowsDeleted != 1)
                    throw new InvalidOperationException("لم يتم حذف المرحلة؛ أعد تحميل الصفحة وحاول مجدداً.");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "BOMStages",
                recordId: bomStageId.ToString(),
                moduleName: "SCR_BOM",
                description: $"حذف مرحلة من الوصفة رقم: {bomId}"
            );
        }

        public async Task<bool> IsDuplicateStageAsync(int bomId, int stageId, int excludeStageId = 0)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية فحص مراحل الوصفة.");

            using var connection = CreateConnection();

            var count = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM dbo.BOMStages
                  WHERE BOMID = @BOMID
                    AND StageID = @StageID
                    AND BOMStageID <> @ExcludeID",
                new
                {
                    BOMID = bomId,
                    StageID = stageId,
                    ExcludeID = excludeStageId
                });

            return count > 0;
        }

        public async Task<bool> IsDuplicateStageOrderAsync(int bomId, int stageOrder, int excludeStageId = 0)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية فحص مراحل الوصفة.");

            using var connection = CreateConnection();

            var count = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM dbo.BOMStages
                  WHERE BOMID = @BOMID
                    AND StageOrder = @StageOrder
                    AND BOMStageID <> @ExcludeID",
                new
                {
                    BOMID = bomId,
                    StageOrder = stageOrder,
                    ExcludeID = excludeStageId
                });

            return count > 0;
        }

        public async Task SwapStageOrderAsync(int bomId, int stageId1, int order1, int stageId2, int order2)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanEdit, "لا تملك صلاحية ترتيب مراحل الوصفة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                await EnsureEditableBOMAsync(connection, transaction, bomId);
                var validStageCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) FROM dbo.BOMStages WITH (UPDLOCK,HOLDLOCK)
WHERE BOMID=@BOMID AND
      ((BOMStageID=@StageID1 AND StageOrder=@Order1) OR
       (BOMStageID=@StageID2 AND StageOrder=@Order2))",
                    new { BOMID = bomId, StageID1 = stageId1, StageID2 = stageId2, Order1 = order1, Order2 = order2 }, transaction);
                if (validStageCount != 2 || stageId1 == stageId2)
                    throw new InvalidOperationException("المرحلتان المحددتان لم تعودا متطابقتين مع ترتيب الوصفة الحالي.");

                await connection.ExecuteAsync(
                    @"UPDATE dbo.BOMStages SET StageOrder = 999 WHERE BOMStageID = @ID AND BOMID=@BOMID",
                    new { ID = stageId1, BOMID = bomId }, transaction);
                await connection.ExecuteAsync(
                    @"UPDATE dbo.BOMStages SET StageOrder = @Order WHERE BOMStageID = @ID AND BOMID=@BOMID",
                    new { ID = stageId2, BOMID = bomId, Order = order1 }, transaction);
                await connection.ExecuteAsync(
                    @"UPDATE dbo.BOMStages SET StageOrder = @Order WHERE BOMStageID = @ID AND BOMID=@BOMID",
                    new { ID = stageId1, BOMID = bomId, Order = order2 }, transaction);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // التكاليف
        // ==========================================
        public async Task UpdateBOMCostsAsync(int bomId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تحديث تكلفة الوصفة.");

            using var connection = CreateConnection();

            await connection.ExecuteAsync(@"
UPDATE bd
SET bd.UnitCost = ISNULL(i.AverageCost,
                  ISNULL(i.LastPurchasePrice,
                  ISNULL(i.StandardCost, 0)))
FROM dbo.BOMDetails bd
INNER JOIN dbo.Items i ON bd.ItemID = i.ItemID
WHERE bd.BOMID = @BOMID",
                new { BOMID = bomId });

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "BillOfMaterials",
                recordId: bomId.ToString(),
                moduleName: "SCR_BOM",
                description: $"تحديث تكاليف الوصفة رقم: {bomId}"
            );
        }

        public async Task<BOMCostSummaryDto> GetBOMCostSummaryAsync(int bomId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض تكلفة الوصفة.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    ISNULL((SELECT SUM(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0) * ISNULL(bd.UnitCost, 0))
            FROM dbo.BOMDetails bd
            INNER JOIN dbo.Items i ON i.ItemID = bd.ItemID
            WHERE bd.BOMID = @BOMID AND bd.MaterialType = 1 AND i.ItemType = 1), 0) AS RawMaterialCost,
    ISNULL((SELECT SUM(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0) * ISNULL(bd.UnitCost, 0))
            FROM dbo.BOMDetails bd
            WHERE bd.BOMID = @BOMID AND bd.MaterialType = 2), 0) AS PackagingCost,
    ISNULL((SELECT SUM(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0) * ISNULL(bd.UnitCost, 0))
            FROM dbo.BOMDetails bd
            INNER JOIN dbo.Items i ON i.ItemID = bd.ItemID
            WHERE bd.BOMID = @BOMID AND bd.MaterialType = 1 AND i.ItemType = 4), 0) AS FinishedProductComponentCost,
    ISNULL(LaborCostPerBatch, 0) AS LaborCost,
    ISNULL(OverheadCostPerBatch, 0) AS OverheadCost,
    ISNULL(OutputQty, 0) AS OutputQty
FROM dbo.BillOfMaterials
WHERE BOMID = @BOMID";

            var data = await connection.QueryFirstOrDefaultAsync<dynamic>(sql, new { BOMID = bomId });

            if (data == null)
                return new BOMCostSummaryDto();

            decimal raw = data.RawMaterialCost;
            decimal pack = data.PackagingCost;
            decimal finishedProductComponents = data.FinishedProductComponentCost;
            decimal labor = data.LaborCost;
            decimal overhead = data.OverheadCost;
            decimal output = data.OutputQty;

            var totalMaterial = raw + pack + finishedProductComponents;
            var totalBatch = totalMaterial + labor + overhead;
            var unitCost = output > 0 ? totalBatch / output : 0;

            return new BOMCostSummaryDto
            {
                RawMaterialCost = raw,
                PackagingCost = pack,
                FinishedProductComponentCost = finishedProductComponents,
                TotalMaterialCost = totalMaterial,
                LaborCost = labor,
                OverheadCost = overhead,
                TotalBatchCost = totalBatch,
                UnitCost = unitCost
            };
        }

        private async Task<decimal> GetItemCostAsync(int itemId)
        {
            using var connection = CreateConnection();

            var cost = await connection.ExecuteScalarAsync<decimal?>(
                @"SELECT CASE
                      WHEN ISNULL(AverageCost, 0) > 0 THEN AverageCost
                      WHEN ISNULL(LastPurchasePrice, 0) > 0 THEN LastPurchasePrice
                      ELSE ISNULL(StandardCost, 0)
                  END
                  FROM dbo.Items
                  WHERE ItemID = @ItemID",
                new { ItemID = itemId });

            return cost ?? 0;
        }

        // ==========================================
        // توليد كود الوصفة
        // ==========================================
        public async Task<string> GenerateBOMCodeAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView || p.CanAdd, "لا تملك صلاحية إنشاء رقم وصفة.");
            using var connection = CreateConnection();
            return await GenerateBOMCodeAsync(connection, transaction: null);
        }

        private static async Task<string> GenerateBOMCodeAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction? transaction)
        {
            try
            {
                var sql = @"
DECLARE @NextNum NVARCHAR(50);
EXEC dbo.sp_GetNextNumber 'BOM', @NextNum OUTPUT;
SELECT @NextNum;";

                var result = await connection.QueryFirstOrDefaultAsync<string>(sql, transaction: transaction);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber كوداً للوصفة.");
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد كود الوصفة (BOM). لم يُنشأ كود بديل؛ راجع إعداد التسلسل BOM والإجراء المخزن.", ex);
            }
        }

        // ==========================================
        // معلومات النظام
        // ==========================================
        public async Task<BOMAuditDto?> GetBOMAuditAsync(int bomId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض سجل الوصفة.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    ISNULL(uc.FullName, N'غير محدد') AS CreatedByName,
    b.CreatedDate,
    ISNULL(um.FullName, N'') AS ModifiedByName,
    b.ModifiedDate,
    ISNULL(ea.FullNameAr, N'') AS ApprovedByName,
    b.ApprovedDate
FROM dbo.BillOfMaterials b
LEFT JOIN dbo.SystemUsers uc ON b.CreatedBy = uc.UserID
LEFT JOIN dbo.SystemUsers um ON b.ModifiedBy = um.UserID
LEFT JOIN dbo.Employees ea ON b.ApprovedBy = ea.EmployeeID
WHERE b.BOMID = @BOMID";

            return await connection.QueryFirstOrDefaultAsync<BOMAuditDto>(sql, new { BOMID = bomId });
        }

        public async Task<decimal> GetTotalStagesDurationAsync(int bomId)
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض مراحل الوصفة.");

            using var connection = CreateConnection();

            var total = await connection.ExecuteScalarAsync<decimal?>(
                @"SELECT ISNULL(SUM(Duration), 0)
                  FROM dbo.BOMStages
                  WHERE BOMID = @BOMID",
                new { BOMID = bomId });

            return total ?? 0;
        }

        // ==========================================
        // Lookups
        // ==========================================
        public async Task<List<BOMLookupDto>> GetProductItemsLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض بيانات الوصفات.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    ItemID AS Id,
    (ItemCode + N' - ' + ItemNameAr) AS Name
FROM dbo.Items
WHERE ItemType IN (3, 4)
  AND IsActive = 1
ORDER BY ItemNameAr";

            var result = await connection.QueryAsync<BOMLookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<BOMLookupDto>> GetUnitsLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض بيانات الوصفات.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    UnitID AS Id,
    UnitNameAr AS Name,
    UnitType AS UnitType
FROM dbo.Units
WHERE IsActive = 1
ORDER BY UnitNameAr";

            var result = await connection.QueryAsync<BOMLookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<BOMLookupDto>> GetStagesLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض بيانات الوصفات.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    StageID AS Id,
    StageNameAr AS Name
FROM dbo.ProductionStages
WHERE IsActive = 1
ORDER BY StageOrder";

            var result = await connection.QueryAsync<BOMLookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetMaterialItemsLookupAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync("SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض بيانات الوصفات.");

            using var connection = CreateConnection();

            var sql = @"
SELECT
    i.ItemID AS Id,
    i.ItemCode AS Code,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(i.ItemNameEn, N''))), N'') IS NULL
            THEN i.ItemNameAr
        ELSE i.ItemNameAr + N' / ' + i.ItemNameEn
    END AS Name,
    i.PrimaryUnitID AS PrimaryUnitID,
    i.ItemType AS ItemType
FROM dbo.Items i
WHERE i.ItemType IN (1, 2, 4)
  AND i.IsActive = 1
ORDER BY i.ItemType, i.ItemNameAr";

            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        // ==========================================
        // تصدير إكسيل
        // ==========================================
        public async Task<byte[]> ExportBOMListToExcelAsync(List<BOMListDto> items, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_BOM", p => p.CanExport, "لا تملك صلاحية تصدير الوصفات.");

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("الوصفات");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير وصفات التصنيع — مصنع واي كي كوتينج";
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
                "#",
                "الكود",
                "اسم الوصفة",
                "المنتج",
                "الإصدار",
                "حجم الدفعة",
                "الكمية الناتجة",
                "خامات",
                "تعبئة",
                "منتجات تامة كمكونات",
                "إجمالي التكلفة",
                "الحالة"
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
                ws.Cell(row, 2).Value = item.BOMCode;
                ws.Cell(row, 3).Value = item.BOMName;
                ws.Cell(row, 4).Value = item.ProductName;
                ws.Cell(row, 5).Value = item.BOMVersion;
                ws.Cell(row, 6).Value = $"{item.BatchSize:#,##0.####} {item.BatchUnitName}";
                ws.Cell(row, 7).Value = $"{item.OutputQty:#,##0.####} {item.OutputUnitName}";
                ws.Cell(row, 8).Value = item.RawMaterialCount;
                ws.Cell(row, 9).Value = item.PackagingMaterialCount;
                ws.Cell(row, 10).Value = item.FinishedProductComponentCount;
                ws.Cell(row, 11).Value = item.TotalBatchCost;
                ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 12).Value = item.StatusName;

                for (int i = 1; i <= 12; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e8e2d0");
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");

                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(3).Width = 28;
            ws.Column(4).Width = 28;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 5,
                tableName: "BillOfMaterials",
                moduleName: "SCR_BOM",
                description: $"تصدير {items.Count} وصفة إلى Excel"
            );

            return stream.ToArray();
        }
    }
}