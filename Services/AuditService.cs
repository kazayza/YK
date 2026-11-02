using Dapper;
using Microsoft.Extensions.Logging;

namespace YKCoatings.Services
{
    public class AuditService : BaseDbService
    {
        private readonly UserSessionService _session;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            IConfiguration configuration,
            UserSessionService session,
            ILogger<AuditService> logger) : base(configuration)
        {
            _session = session;
            _logger = logger;
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
                // لا ننسب سجل التدقيق لمستخدم احتياطي عند غياب هوية الجلسة أو عدم تطابقها.
                if (userId <= 0 || !_session.IsLoggedIn || _session.UserID != userId)
                    return;

                using var connection = CreateConnection();
                var userExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.SystemUsers WHERE UserID=@ID", new { ID = userId });
                if (userExists == 0) return;

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
                    UserID = userId,
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
            catch (Exception ex)
            {
                // Preserve the committed business result, but do not hide the audit failure from operators.
                _logger.LogError(
                    ex,
                    "Failed to write audit event. Module={Module}, Table={Table}, Record={Record}, Action={ActionType}.",
                    moduleName,
                    tableName,
                    recordId,
                    actionType);
            }
        }
    }
}