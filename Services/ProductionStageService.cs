using Dapper;
using System.Data;

namespace YKCoatings.Services
{
    public sealed class ProductionStageAdminDto
    {
        public int StageID { get; set; }
        public string StageNameAr { get; set; } = "";
        public int StageOrder { get; set; }
        public bool IsActive { get; set; }
        public int BOMReferenceCount { get; set; }
        public int BatchReferenceCount { get; set; }

        public bool IsReferenced => BOMReferenceCount > 0 || BatchReferenceCount > 0;
    }

    public sealed class ProductionStageService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly ModuleAuthorizationService _authorization;

        public ProductionStageService(
            IConfiguration configuration,
            AuditService audit,
            ModuleAuthorizationService authorization)
            : base(configuration)
        {
            _audit = audit;
            _authorization = authorization;
        }

        public async Task<List<ProductionStageAdminDto>> GetAllAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض مراحل التصنيع.");

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<ProductionStageAdminDto>(@"
SELECT
    ps.StageID,
    ps.StageNameAr,
    ps.StageOrder,
    ISNULL(ps.IsActive,0) AS IsActive,
    (SELECT COUNT(*) FROM dbo.BOMStages bs WHERE bs.StageID=ps.StageID) AS BOMReferenceCount,
    (SELECT COUNT(*) FROM dbo.ProductionBatchStages pbs WHERE pbs.StageID=ps.StageID) AS BatchReferenceCount
FROM dbo.ProductionStages ps
ORDER BY ISNULL(ps.IsActive,0) DESC,ps.StageOrder,ps.StageID");
            return rows.ToList();
        }

        public async Task<int> AddAsync(string stageNameAr, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_BOM", p => p.CanAdd, "لا تملك صلاحية إضافة مراحل التصنيع.");

            var name = NormalizeName(stageNameAr);
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            int stageId;
            int stageOrder;
            try
            {
                await EnsureNameIsUniqueAsync(connection, transaction, name, null);
                stageOrder = await connection.ExecuteScalarAsync<int>(@"
SELECT ISNULL(MAX(StageOrder),0)+1
FROM dbo.ProductionStages WITH (UPDLOCK,HOLDLOCK)", transaction: transaction);
                stageId = await connection.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ProductionStages (StageNameAr,StageOrder,IsActive)
VALUES (@StageNameAr,@StageOrder,1);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new { StageNameAr = name, StageOrder = stageOrder }, transaction);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 1,
                tableName: "ProductionStages",
                recordId: stageId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new { StageNameAr = name, StageOrder = stageOrder, IsActive = true }),
                changedColumns: "StageNameAr,StageOrder,IsActive",
                moduleName: "SCR_BOM",
                description: $"إضافة مرحلة تصنيع: {name}");

            return stageId;
        }

        public async Task UpdateNameAsync(int stageId, string stageNameAr, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تعديل مراحل التصنيع.");
            if (stageId <= 0) throw new ArgumentOutOfRangeException(nameof(stageId));

            var name = NormalizeName(stageNameAr);
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            ProductionStageAdminDto? current;

            try
            {
                current = await connection.QueryFirstOrDefaultAsync<ProductionStageAdminDto>(@"
SELECT ps.StageID,ps.StageNameAr,ps.StageOrder,ISNULL(ps.IsActive,0) AS IsActive,
       (SELECT COUNT(*) FROM dbo.BOMStages bs WHERE bs.StageID=ps.StageID) AS BOMReferenceCount,
       (SELECT COUNT(*) FROM dbo.ProductionBatchStages pbs WHERE pbs.StageID=ps.StageID) AS BatchReferenceCount
FROM dbo.ProductionStages ps WITH (UPDLOCK,HOLDLOCK)
WHERE ps.StageID=@StageID",
                    new { StageID = stageId }, transaction);
                if (current is null) throw new InvalidOperationException("مرحلة التصنيع غير موجودة.");

                if (current.IsReferenced && !string.Equals(current.StageNameAr.Trim(), name, StringComparison.Ordinal))
                    throw new InvalidOperationException("لا يمكن تغيير اسم مرحلة مستخدمة في وصفة أو دفعة؛ أضف مرحلة جديدة للحفاظ على السجلات التاريخية.");

                await EnsureNameIsUniqueAsync(connection, transaction, name, stageId);
                if (!string.Equals(current.StageNameAr.Trim(), name, StringComparison.Ordinal))
                {
                    await connection.ExecuteAsync(@"
UPDATE dbo.ProductionStages SET StageNameAr=@StageNameAr WHERE StageID=@StageID",
                        new { StageID = stageId, StageNameAr = name }, transaction);
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
                actionType: 2,
                tableName: "ProductionStages",
                recordId: stageId.ToString(),
                oldValues: System.Text.Json.JsonSerializer.Serialize(new { current!.StageNameAr }),
                newValues: System.Text.Json.JsonSerializer.Serialize(new { StageNameAr = name }),
                changedColumns: "StageNameAr",
                moduleName: "SCR_BOM",
                description: $"تعديل اسم مرحلة التصنيع: {current!.StageNameAr} إلى {name}");
        }

        public async Task SetActiveAsync(int stageId, bool isActive, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_BOM", p => p.CanEdit, "لا تملك صلاحية تعديل مراحل التصنيع.");
            if (stageId <= 0) throw new ArgumentOutOfRangeException(nameof(stageId));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            ProductionStageAdminDto? current;

            try
            {
                current = await connection.QueryFirstOrDefaultAsync<ProductionStageAdminDto>(@"
SELECT ps.StageID,ps.StageNameAr,ps.StageOrder,ISNULL(ps.IsActive,0) AS IsActive,
       (SELECT COUNT(*) FROM dbo.BOMStages bs WHERE bs.StageID=ps.StageID) AS BOMReferenceCount,
       (SELECT COUNT(*) FROM dbo.ProductionBatchStages pbs WHERE pbs.StageID=ps.StageID) AS BatchReferenceCount
FROM dbo.ProductionStages ps WITH (UPDLOCK,HOLDLOCK)
WHERE ps.StageID=@StageID",
                    new { StageID = stageId }, transaction);
                if (current is null) throw new InvalidOperationException("مرحلة التصنيع غير موجودة.");
                if (current.IsActive == isActive)
                {
                    transaction.Commit();
                    return;
                }
                if (!isActive && current.IsReferenced)
                    throw new InvalidOperationException("لا يمكن تعطيل مرحلة مرتبطة بوصفة أو دفعة؛ أزل ارتباطها من الوصفات غير المستخدمة أولاً. لن تُحذف أي بيانات تاريخية.");

                await connection.ExecuteAsync(@"
UPDATE dbo.ProductionStages SET IsActive=@IsActive WHERE StageID=@StageID",
                    new { StageID = stageId, IsActive = isActive }, transaction);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId: userId,
                actionType: 2,
                tableName: "ProductionStages",
                recordId: stageId.ToString(),
                oldValues: System.Text.Json.JsonSerializer.Serialize(new { current!.IsActive }),
                newValues: System.Text.Json.JsonSerializer.Serialize(new { IsActive = isActive }),
                changedColumns: "IsActive",
                moduleName: "SCR_BOM",
                description: $"{(isActive ? "تفعيل" : "تعطيل")} مرحلة التصنيع: {current!.StageNameAr}");
        }

        private static string NormalizeName(string? stageNameAr)
        {
            var name = stageNameAr?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("أدخل اسم المرحلة.");
            return name;
        }

        private static async Task EnsureNameIsUniqueAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            string name,
            int? excludeStageId)
        {
            var exists = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.ProductionStages WITH (UPDLOCK,HOLDLOCK)
WHERE LTRIM(RTRIM(StageNameAr))=@StageNameAr
  AND (@ExcludeStageID IS NULL OR StageID<>@ExcludeStageID)",
                new { StageNameAr = name, ExcludeStageID = excludeStageId }, transaction);
            if (exists > 0)
                throw new InvalidOperationException("اسم المرحلة مسجل بالفعل.");
        }
    }
}
