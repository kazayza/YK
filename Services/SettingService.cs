using Dapper;

namespace YKCoatings.Services
{
    public class SettingService : BaseDbService
    {
        private readonly AuditService _audit;

        public SettingService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        public async Task<List<SettingDto>> GetAllSettingsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT SettingID, SettingKey, SettingValue, SettingDescription, 
                               SettingGroup, DataType
                        FROM dbo.SystemSettings
                        ORDER BY SettingGroup, SettingKey";
            var result = await connection.QueryAsync<SettingDto>(sql);
            return result.ToList();
        }

        public async Task<string?> GetSettingValueAsync(string key)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT SettingValue FROM dbo.SystemSettings WHERE SettingKey = @Key",
                new { Key = key });
        }

        public async Task UpdateSettingAsync(int settingId, string? newValue, int userId)
        {
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
                _ => group ?? "عام"
            };
        }

        public static string GetGroupIcon(string? group)
        {
            return group switch
            {
                "Company" => "🏢",
                "Accounting" => "💰",
                "Inventory" => "📦",
                "Manufacturing" => "🏭",
                "Security" => "🔒",
                _ => "⚙️"
            };
        }

        public static string GetGroupColor(string? group)
        {
            return group switch
            {
                "Company" => "#3b82f6",
                "Accounting" => "#10b981",
                "Inventory" => "#f59e0b",
                "Manufacturing" => "#3b82f6",
                "Security" => "#ef4444",
                _ => "#6b7280"
            };
        }
    }
}