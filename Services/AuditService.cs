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
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.AuditLog 
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
    }
}