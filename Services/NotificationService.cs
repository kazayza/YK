using System.Data;
using Dapper;

namespace YKCoatings.Services
{
    public class NotificationService : BaseDbService
    {
        private readonly UserSessionService _userSession;

        public NotificationService(IConfiguration configuration, UserSessionService userSession) : base(configuration)
        {
            _userSession = userSession;
        }

        private void RequireCurrentSession(int userId, int roleId)
        {
            if (!_userSession.IsLoggedIn || userId <= 0 || _userSession.UserID != userId || _userSession.RoleID != roleId)
                throw new UnauthorizedAccessException("جلسة المستخدم غير صالحة للوصول إلى الإشعارات.");
        }

                        public async Task<List<NotificationDto>> GetUserNotificationsAsync(int userId, int roleId, int take = 30)
        {
            RequireCurrentSession(userId, roleId);
            take = Math.Clamp(take, 1, 100);
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
            RequireCurrentSession(userId, roleId);
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
        // Keep the one-argument API safe for existing callers by deriving identity from the active session.
        public Task MarkAsReadAsync(int notificationId)
            => MarkAsReadAsync(notificationId, _userSession.UserID, _userSession.RoleID);

        public async Task MarkAsReadAsync(int notificationId, int userId, int roleId)
        {
            RequireCurrentSession(userId, roleId);
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                @"UPDATE dbo.Notifications
                  SET IsRead=1,ReadDate=GETDATE()
                  WHERE NotificationID=@ID
                    AND (TargetUserID=@UserID OR TargetRoleID=@RoleID OR (TargetUserID IS NULL AND TargetRoleID IS NULL))",
                new { ID = notificationId, UserID = userId, RoleID = roleId });
        }

        public async Task MarkAllAsReadAsync(int userId, int roleId)
        {
            RequireCurrentSession(userId, roleId);
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

        /// <summary>
        /// Creates one private notification per active user who can view the related module
        /// and holds the requested effective permission. Role ids are deliberately not used:
        /// per-user overrides can remove a permission from a role member.
        /// </summary>
        public async Task<int> CreateForPermissionHoldersAsync(
            byte notificationType,
            string title,
            string message,
            byte priority,
            string permissionModuleCode,
            ModulePermissionRequirement requiredPermission,
            string relatedModule,
            int relatedRecordId,
            int createdBy,
            int? excludeUserId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var count = await CreateForPermissionHoldersAsync(
                    connection, transaction, notificationType, title, message, priority,
                    permissionModuleCode, requiredPermission, relatedModule, relatedRecordId,
                    createdBy, excludeUserId);
                transaction.Commit();
                return count;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<int> CreateForPermissionHoldersAsync(
            IDbConnection connection,
            IDbTransaction transaction,
            byte notificationType,
            string title,
            string message,
            byte priority,
            string permissionModuleCode,
            ModulePermissionRequirement requiredPermission,
            string relatedModule,
            int relatedRecordId,
            int createdBy,
            int? excludeUserId = null)
        {
            var permissionColumn = requiredPermission switch
            {
                ModulePermissionRequirement.View => "CanView",
                ModulePermissionRequirement.Add => "CanAdd",
                ModulePermissionRequirement.Edit => "CanEdit",
                ModulePermissionRequirement.Delete => "CanDelete",
                ModulePermissionRequirement.Approve => "CanApprove",
                ModulePermissionRequirement.Post => "CanPost",
                ModulePermissionRequirement.Cancel => "CanCancel",
                _ => throw new ArgumentOutOfRangeException(nameof(requiredPermission))
            };

            var userIds = (await connection.QueryAsync<int>(
                $@"SELECT DISTINCT UserID
                   FROM dbo.vw_UserEffectivePermissions
                   WHERE ModuleCode=@ModuleCode
                     AND CanView=1
                     AND {permissionColumn}=1
                     AND (@ExcludeUserID IS NULL OR UserID<>@ExcludeUserID)",
                new { ModuleCode = permissionModuleCode, ExcludeUserID = excludeUserId }, transaction)).ToList();

            foreach (var targetUserId in userIds)
            {
                await InsertPrivateNotificationAsync(
                    connection, transaction, notificationType, title, message, priority,
                    targetUserId, relatedModule, relatedRecordId, createdBy);
            }

            return userIds.Count;
        }

        /// <summary>Routes a notification to one user only when that user still has view access.</summary>
        public async Task<bool> CreateForAuthorizedUserAsync(
            byte notificationType,
            string title,
            string message,
            byte priority,
            int targetUserId,
            string permissionModuleCode,
            string relatedModule,
            int relatedRecordId,
            int createdBy)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var created = await CreateForAuthorizedUserAsync(
                    connection, transaction, notificationType, title, message, priority,
                    targetUserId, permissionModuleCode, relatedModule, relatedRecordId, createdBy);
                transaction.Commit();
                return created;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<bool> CreateForAuthorizedUserAsync(
            IDbConnection connection,
            IDbTransaction transaction,
            byte notificationType,
            string title,
            string message,
            byte priority,
            int targetUserId,
            string permissionModuleCode,
            string relatedModule,
            int relatedRecordId,
            int createdBy)
        {
            if (targetUserId <= 0) return false;

            var canView = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM dbo.vw_UserEffectivePermissions
                  WHERE UserID=@UserID AND ModuleCode=@ModuleCode AND CanView=1",
                new { UserID = targetUserId, ModuleCode = permissionModuleCode }, transaction);
            if (canView <= 0) return false;

            await InsertPrivateNotificationAsync(
                connection, transaction, notificationType, title, message, priority,
                targetUserId, relatedModule, relatedRecordId, createdBy);
            return true;
        }

        private static Task<int> InsertPrivateNotificationAsync(
            IDbConnection connection,
            IDbTransaction transaction,
            byte notificationType,
            string title,
            string message,
            byte priority,
            int targetUserId,
            string relatedModule,
            int relatedRecordId,
            int createdBy)
        {
            return connection.ExecuteAsync(
                @"INSERT INTO dbo.Notifications
                  (NotificationType,Title,Message,Priority,TargetUserID,TargetRoleID,
                   RelatedModule,RelatedRecordID,CreatedBy,CreatedDate)
                  VALUES
                  (@Type,@Title,@Message,@Priority,@TargetUserID,NULL,
                   @RelatedModule,@RelatedRecordID,@CreatedBy,GETDATE())",
                new
                {
                    Type = notificationType,
                    Title = title,
                    Message = message,
                    Priority = priority,
                    TargetUserID = targetUserId,
                    RelatedModule = relatedModule,
                    RelatedRecordID = relatedRecordId,
                    CreatedBy = createdBy
                }, transaction);
        }

        public static string GetTypeIcon(int type)
        {
            return type switch
            {
                1 => "bi bi-box-seam",
                2 => "bi bi-cash-coin",
                3 => "bi bi-check2-circle",
                4 => "bi bi-clock-history",
                5 => "bi bi-megaphone",
                _ => "bi bi-bell"
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

    public enum ModulePermissionRequirement
    {
        View, Add, Edit, Delete, Approve, Post, Cancel
    }
}
