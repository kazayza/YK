using Dapper;

namespace YKCoatings.Services
{
    public class AuditService : BaseDbService
    {
        private readonly UserSessionService _session;

        public AuditService(IConfiguration configuration, UserSessionService session) : base(configuration)
        {
            _session = session;
        }

        public async Task WriteAuditLogAsync(
            int userId,
            byte actionType,
            string tableName,
            string? recordId = null,
            string? oldValues = null,
            string? newValues = null,
            string? changedColumns = null,
            string? moduleName = null,
            string? description = null)
        {
            try
            {
                using var connection = CreateConnection();

                // تحقق من وجود المستخدم - لو غير موجود استخدم أول مستخدم نشط كـ fallback
                // لمنع خطأ FK__AuditLog__UserID
                int effectiveUserId = userId;
                if (userId <= 0)
                {
                    var fallback = await connection.ExecuteScalarAsync<int?>(
                        "SELECT TOP 1 UserID FROM dbo.SystemUsers WHERE IsActive=1 ORDER BY UserID");
                    if (fallback == null) return; // لا يوجد مستخدمين - تخطي التدقيق
                    effectiveUserId = fallback.Value;
                }
                else
                {
                    var exists = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM dbo.SystemUsers WHERE UserID=@ID", new { ID = userId });
                    if (exists == 0)
                    {
                        var fallback = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 UserID FROM dbo.SystemUsers WHERE IsActive=1 ORDER BY UserID");
                        if (fallback == null) return;
                        effectiveUserId = fallback.Value;
                    }
                }

                // محاولة الإدراج مع معالجة آمنة لأسماء الأعمدة المتغيرة
                var hasUserName = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.AuditLog') AND name='Username'") > 0;

                var sql = hasUserName
                    ? @"INSERT INTO dbo.AuditLog 
                        (UserID, Username, ActionType, TableName, RecordID,
                         OldValues, NewValues, ChangedColumns,
                         ModuleName, Description, 
                         IPAddress, MachineName,
                         AuditDate)
                        VALUES 
                        (@UserID, 
                         (SELECT Username FROM dbo.SystemUsers WHERE UserID = @UserID),
                         @ActionType, @TableName, @RecordID,
                         @OldValues, @NewValues, @ChangedColumns,
                         @ModuleName, @Description,
                         @IPAddress, @MachineName,
                         GETDATE())"
                    : @"INSERT INTO dbo.AuditLog 
                        (UserID, ActionType, TableName, RecordID,
                         OldValues, NewValues, ChangedColumns,
                         ModuleName, Description, 
                         IPAddress, MachineName,
                         AuditDate)
                        VALUES 
                        (@UserID, 
                         @ActionType, @TableName, @RecordID,
                         @OldValues, @NewValues, @ChangedColumns,
                         @ModuleName, @Description,
                         @IPAddress, @MachineName,
                         GETDATE())";

                await connection.ExecuteAsync(sql, new
                {
                    UserID = effectiveUserId,
                    ActionType = actionType,
                    TableName = tableName,
                    RecordID = recordId,
                    OldValues = oldValues,
                    NewValues = newValues,
                    ChangedColumns = changedColumns,
                    ModuleName = moduleName,
                    Description = description,
                    IPAddress = _session.IPAddress ?? "",
                    MachineName = _session.MachineName ?? ""
                });
            }
            catch
            {
                // التدقيق لا يجب أن يكسر العملية الأساسية أبداً
                // Swallow exception - audit failure should never break business flow
            }
        }
    }
}