using Dapper;
using System.Data;

namespace YKCoatings.Services
{
    public sealed class SystemModuleAdminDto
    {
        public int ModuleID { get; set; }
        public int? ParentModuleID { get; set; }
        public string ModuleCode { get; set; } = "";
        public string ModuleNameAr { get; set; } = "";
        public string? ParentModuleName { get; set; }
        public int ModuleType { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public bool ParentExists { get; set; }
        public bool ParentIsActive { get; set; }
        public int DuplicateCodeCount { get; set; }

        public bool IsMainGroup => ModuleType == 1;
        public bool IsSettingsModule => string.Equals(ModuleCode, "SCR_SETTINGS", StringComparison.OrdinalIgnoreCase);
        public bool CanDisable => !IsMainGroup && !IsSettingsModule && DuplicateCodeCount == 1;
        public bool CanEnable => CanDisable && (ParentModuleID is null || (ParentExists && ParentIsActive));
    }

    public sealed class SystemModuleAdminService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly ModuleAuthorizationService _authorization;

        public SystemModuleAdminService(
            IConfiguration configuration,
            AuditService audit,
            ModuleAuthorizationService authorization)
            : base(configuration)
        {
            _audit = audit;
            _authorization = authorization;
        }

        public async Task<List<SystemModuleAdminDto>> GetAllAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_SETTINGS", p => p.CanView, "لا تملك صلاحية عرض إعدادات الوحدات.");

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<SystemModuleAdminDto>(@"
SELECT
    sm.ModuleID,
    sm.ParentModuleID,
    sm.ModuleCode,
    sm.ModuleNameAr,
    parent.ModuleNameAr AS ParentModuleName,
    sm.ModuleType,
    sm.SortOrder,
    CAST(ISNULL(sm.IsActive,0) AS BIT) AS IsActive,
    CAST(CASE WHEN sm.ParentModuleID IS NULL OR parent.ModuleID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS ParentExists,
    CAST(CASE WHEN sm.ParentModuleID IS NULL THEN 1 ELSE ISNULL(parent.IsActive,0) END AS BIT) AS ParentIsActive,
    COUNT(*) OVER (PARTITION BY sm.ModuleCode) AS DuplicateCodeCount
FROM dbo.SystemModules sm
LEFT JOIN dbo.SystemModules parent ON parent.ModuleID=sm.ParentModuleID
ORDER BY ISNULL(sm.ParentModuleID,sm.ModuleID),
         CASE WHEN sm.ModuleType=1 THEN 0 ELSE 1 END,
         sm.SortOrder,sm.ModuleID");
            return rows.ToList();
        }

        public async Task SetActiveAsync(int moduleId, bool isActive, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_SETTINGS", p => p.CanEdit,
                "لا تملك صلاحية تفعيل أو تعطيل الوحدات.");
            if (moduleId <= 0) throw new ArgumentOutOfRangeException(nameof(moduleId));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            SystemModuleAdminDto? current;

            try
            {
                current = await connection.QueryFirstOrDefaultAsync<SystemModuleAdminDto>(@"
SELECT
    sm.ModuleID,
    sm.ParentModuleID,
    sm.ModuleCode,
    sm.ModuleNameAr,
    parent.ModuleNameAr AS ParentModuleName,
    sm.ModuleType,
    sm.SortOrder,
    CAST(ISNULL(sm.IsActive,0) AS BIT) AS IsActive,
    CAST(CASE WHEN sm.ParentModuleID IS NULL OR parent.ModuleID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS ParentExists,
    CAST(CASE WHEN sm.ParentModuleID IS NULL THEN 1 ELSE ISNULL(parent.IsActive,0) END AS BIT) AS ParentIsActive,
    (SELECT COUNT(*) FROM dbo.SystemModules sameCode WHERE sameCode.ModuleCode=sm.ModuleCode) AS DuplicateCodeCount
FROM dbo.SystemModules sm WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.SystemModules parent ON parent.ModuleID=sm.ParentModuleID
WHERE sm.ModuleID=@ModuleID",
                    new { ModuleID = moduleId }, transaction);

                if (current is null)
                    throw new InvalidOperationException("الوحدة غير موجودة.");
                if (current.IsMainGroup)
                    throw new InvalidOperationException("هذه مجموعة رئيسية للتنظيم؛ فعّل أو عطّل الشاشات التابعة بدلاً منها.");
                if (current.IsSettingsModule && !isActive)
                    throw new InvalidOperationException("لا يمكن تعطيل وحدة الإعدادات من شاشة إدارتها.");
                if (current.DuplicateCodeCount != 1)
                    throw new InvalidOperationException("كود هذه الوحدة مكرر؛ أصلح تكراره قبل تغيير حالتها.");
                if (isActive && current.ParentModuleID.HasValue && !current.ParentExists)
                    throw new InvalidOperationException("لا يمكن تفعيل وحدة يتيمة قبل تصحيح القسم الرئيسي المرتبط بها.");
                if (isActive && current.ParentModuleID.HasValue && !current.ParentIsActive)
                    throw new InvalidOperationException("لا يمكن تفعيل الشاشة قبل تفعيل مجموعتها الرئيسية.");
                if (current.IsActive == isActive)
                {
                    transaction.Commit();
                    return;
                }

                var affected = await connection.ExecuteAsync(@"
UPDATE dbo.SystemModules
SET IsActive=@IsActive
WHERE ModuleID=@ModuleID AND ISNULL(IsActive,0)=@CurrentIsActive",
                    new
                    {
                        ModuleID = moduleId,
                        IsActive = isActive,
                        CurrentIsActive = current.IsActive
                    }, transaction);
                if (affected != 1)
                    throw new InvalidOperationException("تغيرت حالة الوحدة أثناء التحديث؛ حدّث القائمة وحاول مرة أخرى.");

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
                tableName: "SystemModules",
                recordId: moduleId.ToString(),
                oldValues: System.Text.Json.JsonSerializer.Serialize(new { current!.IsActive }),
                newValues: System.Text.Json.JsonSerializer.Serialize(new { IsActive = isActive }),
                changedColumns: "IsActive",
                moduleName: "SCR_SETTINGS",
                description: $"{(isActive ? "تفعيل" : "تعطيل")} الوحدة {current!.ModuleCode} — {current.ModuleNameAr}");
        }
    }
}
