using Dapper;

namespace YKCoatings.Services
{
    public class NotificationService : BaseDbService
    {
        public NotificationService(IConfiguration configuration) : base(configuration) { }

                        public async Task<List<NotificationDto>> GetUserNotificationsAsync(int userId, int roleId, int take = 30)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT TOP (@Take)
                            NotificationID, NotificationDate, NotificationType,
                            Title, Message, Priority,
                            RelatedModule, RelatedRecordID,
                            IsRead, ReadDate, IsActioned, ActionDate
                        FROM dbo.Notifications
                        WHERE (TargetUserID = @UserID 
                               OR TargetRoleID = @RoleID 
                               OR (TargetUserID IS NULL AND TargetRoleID IS NULL))
                          AND (ExpiryDate IS NULL OR ExpiryDate > GETDATE())
                        ORDER BY 
                            IsActioned ASC,
                            IsRead ASC, 
                            Priority ASC, 
                            NotificationDate DESC";

            var result = await connection.QueryAsync<NotificationDto>(sql, new
            {
                Take = take,
                UserID = userId,
                RoleID = roleId
            });
            return result.ToList();
        }
                public async Task MarkRelatedAsActionedAsync(string relatedModule, int relatedRecordId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                @"UPDATE dbo.Notifications 
                  SET IsActioned = 1, ActionDate = GETDATE(), IsRead = 1, ReadDate = ISNULL(ReadDate, GETDATE())
                  WHERE RelatedModule = @Module 
                    AND RelatedRecordID = @RecordID 
                    AND IsActioned = 0",
                new { Module = relatedModule, RecordID = relatedRecordId });
        }

                public async Task<int> GetUnreadCountAsync(int userId, int roleId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT COUNT(*)
                        FROM dbo.Notifications
                        WHERE (TargetUserID = @UserID 
                               OR TargetRoleID = @RoleID 
                               OR (TargetUserID IS NULL AND TargetRoleID IS NULL))
                          AND (IsRead = 0 OR IsActioned = 0)
                          AND (ExpiryDate IS NULL OR ExpiryDate > GETDATE())";

            return await connection.QueryFirstOrDefaultAsync<int>(sql, new
            {
                UserID = userId,
                RoleID = roleId
            });
        }
        public async Task<bool> IsSoundEnabledAsync()
        {
            using var connection = CreateConnection();
            var value = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT SettingValue FROM dbo.SystemSettings WHERE SettingKey = N'NotificationSound'");
            return value == "1" || value == "true";
        }
        public async Task MarkAsReadAsync(int notificationId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE dbo.Notifications SET IsRead = 1, ReadDate = GETDATE() WHERE NotificationID = @ID",
                new { ID = notificationId });
        }

        public async Task MarkAllAsReadAsync(int userId, int roleId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                @"UPDATE dbo.Notifications SET IsRead = 1, ReadDate = GETDATE()
                  WHERE (TargetUserID = @UserID OR TargetRoleID = @RoleID OR (TargetUserID IS NULL AND TargetRoleID IS NULL))
                    AND IsRead = 0",
                new { UserID = userId, RoleID = roleId });
        }

        public async Task CreateNotificationAsync(
            byte notificationType, string title, string message,
            byte priority = 2, int? targetUserId = null, int? targetRoleId = null,
            string? relatedModule = null, int? relatedRecordId = null, int? createdBy = null)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                @"INSERT INTO dbo.Notifications 
                  (NotificationType, Title, Message, Priority, TargetUserID, TargetRoleID,
                   RelatedModule, RelatedRecordID, CreatedBy, CreatedDate)
                  VALUES 
                  (@Type, @Title, @Message, @Priority, @TargetUserID, @TargetRoleID,
                   @RelatedModule, @RelatedRecordID, @CreatedBy, GETDATE())",
                new
                {
                    Type = notificationType,
                    Title = title,
                    Message = message,
                    Priority = priority,
                    TargetUserID = targetUserId,
                    TargetRoleID = targetRoleId,
                    RelatedModule = relatedModule,
                    RelatedRecordID = relatedRecordId,
                    CreatedBy = createdBy
                });
        }

        public static string GetTypeIcon(int type)
        {
            return type switch
            {
                1 => "📦",
                2 => "💰",
                3 => "✅",
                4 => "⏰",
                5 => "📢",
                _ => "🔔"
            };
        }

        public static string GetPriorityColor(int priority)
        {
            return priority switch
            {
                1 => "#ef4444",
                2 => "#f59e0b",
                3 => "#6b7280",
                _ => "#6b7280"
            };
        }

        public static string GetPriorityName(int priority)
        {
            return priority switch
            {
                1 => "عاجل",
                2 => "عادي",
                3 => "منخفض",
                _ => "عادي"
            };
        }
    }
}