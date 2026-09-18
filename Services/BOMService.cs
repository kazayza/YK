using ClosedXML.Excel;
using Dapper;

namespace YKCoatings.Services
{
    public class BOMService : BaseDbService
    {
        private readonly AuditService _audit;

        public BOMService(IConfiguration configuration, AuditService audit)
            : base(configuration)
        {
            _audit = audit;
        }

        // ==========================================
        // قائمة الوصفات
        // ==========================================
        public async Task<List<BOMListDto>> GetBOMListAsync(
            string? search = null,
            int? statusFilter = null,
            bool activeOnly = true)
        {
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
    ISNULL((SELECT COUNT(*) FROM dbo.BOMDetails bd WHERE bd.BOMID = b.BOMID AND bd.MaterialType = 1), 0) AS RawMaterialCount,
    ISNULL((SELECT COUNT(*) FROM dbo.BOMDetails bd WHERE bd.BOMID = b.BOMID AND bd.MaterialType = 2), 0) AS PackagingMaterialCount,
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
            using var connection = CreateConnection();

            var bomCode = await GenerateBOMCodeAsync();

            var nextVersion = await connection.ExecuteScalarAsync<int>(
                @"SELECT ISNULL(MAX(BOMVersion), 0) + 1
                  FROM dbo.BillOfMaterials
                  WHERE ProductItemID = @ProductItemID",
                new { bom.ProductItemID });

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

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
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
            });

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
            using var connection = CreateConnection();

            var oldBom = await connection.QueryFirstOrDefaultAsync<BOMEditDto>(
                @"SELECT
                      BOMID, BOMName, ProductItemID, BOMVersion,
                      BatchSize, BatchUnitID, OutputQty, OutputUnitID,
                      WastePercent, EstimatedTime,
                      LaborCostPerBatch, OverheadCostPerBatch,
                      Instructions, SafetyNotes, QualityNotes, Notes,
                      BOMStatus, IsActive, EffectiveFrom, EffectiveTo
                  FROM dbo.BillOfMaterials
                  WHERE BOMID = @BOMID",
                new { bom.BOMID });

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

            await connection.ExecuteAsync(sql, new
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
            });

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
            using var connection = CreateConnection();

            var current = await connection.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT BOMID, BOMCode, BOMName, BOMStatus, ProductItemID
                  FROM dbo.BillOfMaterials
                  WHERE BOMID = @BOMID",
                new { BOMID = bomId });

            if (current == null)
                return (false, "الوصفة غير موجودة");

            int currentStatus = current.BOMStatus;

            if (newStatus == 2)
            {
                if (currentStatus != 1 && currentStatus != 3)
                    return (false, "لا يمكن تنشيط الوصفة من هذه الحالة");

                var materialCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.BOMDetails WHERE BOMID = @BOMID",
                    new { BOMID = bomId });

                if (materialCount <= 0)
                    return (false, "لا يمكن تنشيط وصفة بدون مكونات");

                await connection.ExecuteAsync(
                    @"UPDATE dbo.BillOfMaterials
                      SET BOMStatus = 2,
                          ModifiedBy = @UserID,
                          ModifiedDate = GETDATE(),
                          ApprovedBy = CASE WHEN @EmployeeID IS NULL OR @EmployeeID = 0 THEN ApprovedBy ELSE @EmployeeID END,
                          ApprovedDate = CASE WHEN @EmployeeID IS NULL OR @EmployeeID = 0 THEN ApprovedDate ELSE GETDATE() END
                      WHERE BOMID = @BOMID",
                    new
                    {
                        BOMID = bomId,
                        UserID = userId,
                        EmployeeID = employeeId
                    });

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
                if (currentStatus != 2)
                    return (false, "لا يمكن إيقاف إلا وصفة نشطة");

                await connection.ExecuteAsync(
                    @"UPDATE dbo.BillOfMaterials
                      SET BOMStatus = 3,
                          ModifiedBy = @UserID,
                          ModifiedDate = GETDATE()
                      WHERE BOMID = @BOMID",
                    new
                    {
                        BOMID = bomId,
                        UserID = userId
                    });

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
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.BillOfMaterials
                      SET BOMStatus = 3,
                          ModifiedBy = @UserID,
                          ModifiedDate = GETDATE()
                      WHERE BOMID = @BOMID",
                    new
                    {
                        BOMID = oldBomId,
                        UserID = userId
                    },
                    transaction);

                await connection.ExecuteAsync(
                    @"UPDATE dbo.BillOfMaterials
                      SET BOMStatus = 2,
                          ModifiedBy = @UserID,
                          ModifiedDate = GETDATE(),
                          ApprovedBy = CASE WHEN @EmployeeID IS NULL OR @EmployeeID = 0 THEN ApprovedBy ELSE @EmployeeID END,
                          ApprovedDate = CASE WHEN @EmployeeID IS NULL OR @EmployeeID = 0 THEN ApprovedDate ELSE GETDATE() END
                      WHERE BOMID = @BOMID",
                    new
                    {
                        BOMID = newBomId,
                        UserID = userId,
                        EmployeeID = employeeId
                    },
                    transaction);

                transaction.Commit();

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
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, $"حدث خطأ أثناء تبديل الحالة: {ex.Message}");
            }
        }

        // ==========================================
        // نسخ الوصفة
        // ==========================================
        public async Task<(bool Success, int NewBOMID, string Message)> CopyBOMAsync(int sourceBomId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

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
                      FROM dbo.BillOfMaterials
                      WHERE BOMID = @BOMID",
                    new { BOMID = sourceBomId },
                    transaction);

                if (source == null)
                    return (false, 0, "الوصفة الأصلية غير موجودة");

                var nextVersion = await connection.ExecuteScalarAsync<int>(
                    @"SELECT ISNULL(MAX(BOMVersion), 0) + 1
                      FROM dbo.BillOfMaterials
                      WHERE ProductItemID = @ProductItemID",
                    new { source.ProductItemID },
                    transaction);

                var newCode = await GenerateBOMCodeAsync();

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
    Quantity, WastePercent, UnitCost,
    IsOptional, SubstituteItemID, Sequence, StageNumber, Notes
)
SELECT
    @NewBOMID, LineNumber, ItemID, MaterialType, UnitID,
    Quantity, WastePercent, UnitCost,
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
            using var connection = CreateConnection();

            var sql = @"
SELECT
    bd.BOMDetailID,
    bd.BOMID,
    bd.LineNumber,
    bd.ItemID,
    i.ItemCode,
    i.ItemNameAr AS ItemName,
    bd.MaterialType,
    CASE bd.MaterialType
        WHEN 1 THEN N'خام'
        WHEN 2 THEN N'تعبئة'
        ELSE N''
    END AS MaterialTypeName,
    bd.UnitID,
    u.UnitNameAr AS UnitName,
    bd.Quantity,
    ISNULL(bd.WastePercent, 0) AS WastePercent,
    (bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0)) AS NetQuantity,
    ISNULL(bd.UnitCost, 0) AS UnitCost,
    (bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100.0) * ISNULL(bd.UnitCost, 0)) AS LineCost,
    ISNULL(bd.IsOptional, 0) AS IsOptional,
    bd.SubstituteItemID,
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
            using var connection = CreateConnection();

            var nextLine = await connection.ExecuteScalarAsync<int>(
                @"SELECT ISNULL(MAX(LineNumber), 0) + 1
                  FROM dbo.BOMDetails
                  WHERE BOMID = @BOMID",
                new { detail.BOMID });

            var unitCost = await GetItemCostAsync(detail.ItemID);

            var sql = @"
INSERT INTO dbo.BOMDetails
(
    BOMID, LineNumber, ItemID, MaterialType, UnitID,
    Quantity, WastePercent, UnitCost,
    IsOptional, SubstituteItemID, Sequence, StageNumber, Notes
)
VALUES
(
    @BOMID, @LineNumber, @ItemID, @MaterialType, @UnitID,
    @Quantity, @WastePercent, @UnitCost,
    @IsOptional, NULLIF(@SubstituteItemID, 0), @Sequence, @StageNumber, @Notes
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                detail.BOMID,
                LineNumber = nextLine,
                detail.ItemID,
                detail.MaterialType,
                detail.UnitID,
                detail.Quantity,
                detail.WastePercent,
                UnitCost = unitCost,
                detail.IsOptional,
                detail.SubstituteItemID,
                Sequence = detail.Sequence <= 0 ? 0 : detail.Sequence,
                StageNumber = detail.StageNumber <= 0 ? 1 : detail.StageNumber,
                detail.Notes
            });

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
            using var connection = CreateConnection();

            var unitCost = await GetItemCostAsync(detail.ItemID);

            await connection.ExecuteAsync(@"
UPDATE dbo.BOMDetails SET
    ItemID = @ItemID,
    MaterialType = @MaterialType,
    UnitID = @UnitID,
    Quantity = @Quantity,
    WastePercent = @WastePercent,
    UnitCost = @UnitCost,
    IsOptional = @IsOptional,
    SubstituteItemID = NULLIF(@SubstituteItemID, 0),
    Sequence = @Sequence,
    StageNumber = @StageNumber,
    Notes = @Notes
WHERE BOMDetailID = @BOMDetailID",
                new
                {
                    detail.BOMDetailID,
                    detail.ItemID,
                    detail.MaterialType,
                    detail.UnitID,
                    detail.Quantity,
                    detail.WastePercent,
                    UnitCost = unitCost,
                    detail.IsOptional,
                    detail.SubstituteItemID,
                    Sequence = detail.Sequence <= 0 ? 0 : detail.Sequence,
                    StageNumber = detail.StageNumber <= 0 ? 1 : detail.StageNumber,
                    detail.Notes
                });

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
            using var connection = CreateConnection();

            await connection.ExecuteAsync(
                @"DELETE FROM dbo.BOMDetails WHERE BOMDetailID = @ID",
                new { ID = bomDetailId });

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "BOMDetails",
                recordId: bomDetailId.ToString(),
                moduleName: "SCR_BOM",
                description: $"حذف مكون من الوصفة رقم: {bomDetailId}"
            );
        }

        public async Task<bool> IsDuplicateItemAsync(int bomId, int itemId, int excludeDetailId = 0)
        {
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

        // ==========================================
        // مراحل الوصفة
        // ==========================================
        public async Task<List<BOMStageDto>> GetBOMStagesAsync(int bomId)
        {
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
            using var connection = CreateConnection();

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

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                stage.BOMID,
                stage.StageID,
                stage.StageOrder,
                stage.Duration,
                stage.Temperature,
                stage.Speed,
                stage.QualityCheckRequired,
                stage.QualityCheckNotes,
                stage.Instructions,
                stage.Notes
            });

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
            using var connection = CreateConnection();

            await connection.ExecuteAsync(@"
UPDATE dbo.BOMStages SET
    StageID = @StageID,
    StageOrder = @StageOrder,
    Duration = @Duration,
    Temperature = @Temperature,
    Speed = @Speed,
    QualityCheckRequired = @QualityCheckRequired,
    QualityCheckNotes = @QualityCheckNotes,
    Instructions = @Instructions,
    Notes = @Notes
WHERE BOMStageID = @BOMStageID",
                new
                {
                    stage.BOMStageID,
                    stage.StageID,
                    stage.StageOrder,
                    stage.Duration,
                    stage.Temperature,
                    stage.Speed,
                    stage.QualityCheckRequired,
                    stage.QualityCheckNotes,
                    stage.Instructions,
                    stage.Notes
                });

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
            using var connection = CreateConnection();

            await connection.ExecuteAsync(
                @"DELETE FROM dbo.BOMStages WHERE BOMStageID = @ID",
                new { ID = bomStageId });

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 3,
                tableName: "BOMStages",
                recordId: bomStageId.ToString(),
                moduleName: "SCR_BOM",
                description: $"حذف مرحلة من الوصفة رقم: {bomStageId}"
            );
        }

        public async Task<bool> IsDuplicateStageAsync(int bomId, int stageId, int excludeStageId = 0)
        {
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
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.BOMStages SET StageOrder = 999 WHERE BOMStageID = @ID",
                    new { ID = stageId1 },
                    transaction);

                await connection.ExecuteAsync(
                    @"UPDATE dbo.BOMStages SET StageOrder = @Order WHERE BOMStageID = @ID",
                    new { ID = stageId2, Order = order1 },
                    transaction);

                await connection.ExecuteAsync(
                    @"UPDATE dbo.BOMStages SET StageOrder = @Order WHERE BOMStageID = @ID",
                    new { ID = stageId1, Order = order2 },
                    transaction);

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
            using var connection = CreateConnection();

            var sql = @"
SELECT
    ISNULL((SELECT SUM(Quantity * (1 + ISNULL(WastePercent, 0) / 100.0) * ISNULL(UnitCost, 0))
            FROM dbo.BOMDetails
            WHERE BOMID = @BOMID AND MaterialType = 1), 0) AS RawMaterialCost,
    ISNULL((SELECT SUM(Quantity * (1 + ISNULL(WastePercent, 0) / 100.0) * ISNULL(UnitCost, 0))
            FROM dbo.BOMDetails
            WHERE BOMID = @BOMID AND MaterialType = 2), 0) AS PackagingCost,
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
            decimal labor = data.LaborCost;
            decimal overhead = data.OverheadCost;
            decimal output = data.OutputQty;

            var totalMaterial = raw + pack;
            var totalBatch = totalMaterial + labor + overhead;
            var unitCost = output > 0 ? totalBatch / output : 0;

            return new BOMCostSummaryDto
            {
                RawMaterialCost = raw,
                PackagingCost = pack,
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
            using var connection = CreateConnection();

            try
            {
                var sql = @"
DECLARE @NextNum NVARCHAR(50);
EXEC dbo.sp_GetNextNumber 'BOM', @NextNum OUTPUT;
SELECT @NextNum;";

                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"BOM-{DateTime.Now:yyMMddHHmmss}";
            }
            catch
            {
                return $"BOM-{DateTime.Now:yyMMddHHmmss}";
            }
        }

        // ==========================================
        // معلومات النظام
        // ==========================================
        public async Task<BOMAuditDto?> GetBOMAuditAsync(int bomId)
        {
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
            using var connection = CreateConnection();

            var sql = @"
SELECT
    UnitID AS Id,
    UnitNameAr AS Name
FROM dbo.Units
WHERE IsActive = 1
ORDER BY UnitNameAr";

            var result = await connection.QueryAsync<BOMLookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<BOMLookupDto>> GetStagesLookupAsync()
        {
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

        public async Task<List<BOMLookupDto>> GetMaterialItemsLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    ItemID AS Id,
    (ItemCode + N' - ' + ItemNameAr) AS Name
FROM dbo.Items
WHERE ItemType IN (1, 2)
  AND IsActive = 1
ORDER BY ItemType, ItemNameAr";

            var result = await connection.QueryAsync<BOMLookupDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // تصدير إكسيل
        // ==========================================
        public async Task<byte[]> ExportBOMListToExcelAsync(List<BOMListDto> items, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("الوصفات");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير وصفات التصنيع — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 11).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 11).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 11).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 11).Style.Font.FontColor = XLColor.Gray;
            ws.Range(2, 1, 2, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

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
                "إجمالي التكلفة",
                "الحالة"
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
                ws.Cell(row, 2).Value = item.BOMCode;
                ws.Cell(row, 3).Value = item.BOMName;
                ws.Cell(row, 4).Value = item.ProductName;
                ws.Cell(row, 5).Value = item.BOMVersion;
                ws.Cell(row, 6).Value = $"{item.BatchSize:#,##0.####} {item.BatchUnitName}";
                ws.Cell(row, 7).Value = $"{item.OutputQty:#,##0.####} {item.OutputUnitName}";
                ws.Cell(row, 8).Value = item.RawMaterialCount;
                ws.Cell(row, 9).Value = item.PackagingMaterialCount;
                ws.Cell(row, 10).Value = item.TotalBatchCost;
                ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 11).Value = item.StatusName;

                for (int i = 1; i <= 11; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f7ff");

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