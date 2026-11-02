using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class SettingService : BaseDbService
    {
        private const string ModuleCode = "SCR_SETTINGS";
        private readonly AuditService _audit;

        private readonly ModuleAuthorizationService _authorization;
        private readonly UserSessionService _userSession;

        public SettingService(
            IConfiguration configuration,
            AuditService audit,
            ModuleAuthorizationService authorization,
            UserSessionService userSession) : base(configuration)
        {
            _audit = audit;
            _authorization = authorization;
            _userSession = userSession;
        }

        private Task RequireSettingPermissionAsync(int userId, Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireUserPermissionAsync(userId, ModuleCode, predicate, deniedMessage);
        private Task RequireCurrentSettingPermissionAsync(Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireCurrentUserPermissionAsync(ModuleCode, predicate, deniedMessage);

        public async Task<List<SettingDto>> GetAllSettingsAsync()
        {
            await RequireCurrentSettingPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض إعدادات النظام.");
            using var connection = CreateConnection();
            var sql = @"SELECT SettingID, SettingKey, SettingValue, SettingDescription, 
                               SettingGroup, DataType
                        FROM dbo.SystemSettings
                        ORDER BY SettingGroup, SettingKey";
            var result = await connection.QueryAsync<SettingDto>(sql);
            return result.ToList();
        }

        private sealed class CompanyPrintSettingRow
        {
            public string SettingKey { get; set; } = string.Empty;
            public string? SettingValue { get; set; }
        }

        /// <summary>
        /// Returns only the company identity fields approved for print headers/footers.
        /// This is read-only and requires an active session with at least one effective
        /// CanPrint permission. It intentionally does not require SCR_SETTINGS:CanView;
        /// individual print actions keep their own module-level authorization.
        /// </summary>
        public async Task<PrintCompanyProfileDto> GetCompanyPrintProfileAsync()
        {
            await _authorization.RequireCurrentUserAnyPrintPermissionAsync("لا تملك صلاحية الطباعة.");

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<CompanyPrintSettingRow>(@"
SELECT SettingKey, SettingValue
FROM dbo.SystemSettings
WHERE SettingGroup=N'Company'
  AND SettingKey IN
      (N'CompanyName',N'CompanyNameEn',N'CompanyAddress',N'CompanyPhone',
       N'CompanyEmail',N'TaxNumber',N'CommercialRegister',N'CompanyLogo');");

            var values = rows.ToDictionary(x => x.SettingKey, x => x.SettingValue ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            string Read(string key) => values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

            return new PrintCompanyProfileDto
            {
                CompanyName = Read("CompanyName"),
                CompanyNameEn = Read("CompanyNameEn"),
                CompanyAddress = Read("CompanyAddress"),
                CompanyPhone = Read("CompanyPhone"),
                CompanyEmail = Read("CompanyEmail"),
                TaxNumber = Read("TaxNumber"),
                CommercialRegister = Read("CommercialRegister"),
                CompanyLogo = NormalizeCompanyLogoPath(Read("CompanyLogo"))
            };
        }

        private static string NormalizeCompanyLogoPath(string? value)
        {
            const string fallback = "/images/yk-logo-gold.png";
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var path = value.Trim().Replace('\\', '/');
            if (path.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
                path = path["wwwroot/".Length..];
            path = path.TrimStart('/');
            if (path.Split('/').Any(segment => segment is ".." or ".") ||
                path.Contains(':') ||
                path.Any(char.IsControl))
                return fallback;

            return "/" + path;
        }

        public async Task<string?> GetSettingValueAsync(string key)
        {
            if (string.Equals(key, "SessionTimeout", StringComparison.OrdinalIgnoreCase))
            {
                if (!_userSession.IsLoggedIn || _userSession.UserID <= 0)
                    throw new UnauthorizedAccessException("يجب تسجيل الدخول لقراءة مهلة الجلسة.");
            }
            else
            {
                await RequireCurrentSettingPermissionAsync(p => p.CanView, "ليس لديك صلاحية قراءة إعدادات النظام.");
            }
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT SettingValue FROM dbo.SystemSettings WHERE SettingKey = @Key",
                new { Key = key });
        }

        public async Task UpdateSettingAsync(int settingId, string? newValue, int userId)
        {
            await RequireSettingPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل إعدادات النظام.");
            using var connection = CreateConnection();

            var old = await connection.QueryFirstOrDefaultAsync<SettingDto>(
                "SELECT SettingKey, SettingValue, SettingDescription FROM dbo.SystemSettings WHERE SettingID = @ID",
                new { ID = settingId });

            await connection.ExecuteAsync(
                "UPDATE dbo.SystemSettings SET SettingValue = @Value, ModifiedDate = GETDATE() WHERE SettingID = @ID",
                new { ID = settingId, Value = newValue });

            if (old != null)
            {
                await _audit.WriteAuditLogAsync(userId, 2, "SystemSettings", settingId.ToString(),
                    oldValues: System.Text.Json.JsonSerializer.Serialize(new { old.SettingKey, OldValue = old.SettingValue }),
                    newValues: System.Text.Json.JsonSerializer.Serialize(new { old.SettingKey, NewValue = newValue }),
                    changedColumns: "SettingValue",
                    moduleName: "SCR_SETTINGS",
                    description: $"تعديل إعداد: {old.SettingDescription ?? old.SettingKey} من [{old.SettingValue}] إلى [{newValue}]");
            }
        }

        public async Task<int> InsertSettingAsync(string key, string? value, string? description, string? group, int userId)
        {
            await RequireSettingPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل إعدادات النظام.");
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, SettingDescription, SettingGroup, CreatedDate)
                        VALUES (@Key, @Value, @Description, @Group, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                Key = key, Value = value, Description = description, Group = group
            });

            await _audit.WriteAuditLogAsync(userId, 1, "SystemSettings", newId.ToString(),
                moduleName: "SCR_SETTINGS",
                description: $"إضافة إعداد جديد: {key} = {value}");

            return newId;
        }

        public async Task<(bool Success, string Message)> DeleteSettingAsync(int settingId, int userId)
        {
            await RequireSettingPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل إعدادات النظام.");
            using var connection = CreateConnection();

            var setting = await connection.QueryFirstOrDefaultAsync<SettingDto>(
                "SELECT SettingKey, SettingDescription FROM dbo.SystemSettings WHERE SettingID = @ID",
                new { ID = settingId });

            await connection.ExecuteAsync(
                "DELETE FROM dbo.SystemSettings WHERE SettingID = @ID",
                new { ID = settingId });

            if (setting != null)
            {
                await _audit.WriteAuditLogAsync(userId, 3, "SystemSettings", settingId.ToString(),
                    moduleName: "SCR_SETTINGS",
                    description: $"حذف إعداد: {setting.SettingDescription ?? setting.SettingKey}");
            }

            return (true, "تم حذف الإعداد بنجاح");
        }

        public static string GetGroupNameAr(string? group)
        {
            return group switch
            {
                "Company" => "بيانات الشركة",
                "Accounting" => "المحاسبة",
                "Inventory" => "المخزون",
                "Manufacturing" => "التصنيع",
                "Security" => "الأمان",
                "System" => "النظام",
                _ => group ?? "عام"
            };
        }

        public static string GetGroupIcon(string? group)
        {
            return group switch
            {
                "Company" => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"><path d=\"M3 21h18\"/><path d=\"M5 21V7l7-4 7 4v14\"/><path d=\"M9 21v-4h6v4\"/><path d=\"M9 10h.01M15 10h.01M9 14h.01M15 14h.01\"/></svg>",
                "Accounting" => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"><line x1=\"12\" y1=\"1\" x2=\"12\" y2=\"23\"/><path d=\"M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6\"/></svg>",
                "Inventory" => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"><path d=\"M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z\"/><path d=\"m3.3 7 8.7 5 8.7-5\"/><path d=\"M12 22V12\"/></svg>",
                "Manufacturing" => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"><path d=\"M2 20a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8l-7 5V8l-7 5V4a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2Z\"/><path d=\"M17 18h1\"/><path d=\"M12 18h1\"/><path d=\"M7 18h1\"/></svg>",
                "Security" => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"><rect x=\"3\" y=\"11\" width=\"18\" height=\"11\" rx=\"2\"/><path d=\"M7 11V7a5 5 0 0 1 10 0v4\"/></svg>",
                _ => "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"><circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z\"/></svg>"
            };
        }

        public static string GetGroupColor(string? group)
        {
            return group switch
            {
                "Company" => "#D4AF37",
                "Accounting" => "#059669",
                "Inventory" => "#B45309",
                "Manufacturing" => "#0A0F1E",
                "Security" => "#DC2626",
                "System" => "#6B7280",
                _ => "#9C7C2E"
            };
        }
    }
}