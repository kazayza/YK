using System.Data;
using Dapper;

namespace YKCoatings.Services
{
    public class NotificationService : BaseDbService
    {
        private readonly UserSessionService _userSession;

        private const string RecipientPredicate = @"(
            n.TargetUserID = @UserID
            OR n.TargetRoleID = @RoleID
            OR (n.TargetUserID IS NULL AND n.TargetRoleID IS NULL)
        )";

        private const string RelatedModuleViewPredicate = @"(
            NULLIF(LTRIM(RTRIM(n.RelatedModule)), N'') IS NULL
            OR EXISTS
            (
                SELECT 1
                FROM dbo.vw_UserEffectivePermissions p
                WHERE p.UserID = @UserID
                  AND p.ModuleCode = n.RelatedModule
                  AND p.CanView = 1
            )
        )";

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
            var sql = $@"SELECT TOP (@Take)
                            n.NotificationID, n.NotificationDate, n.NotificationType,
                            n.Title, n.Message, n.Priority,
                            n.RelatedModule, n.RelatedRecordID,
                            n.IsRead, n.ReadDate, n.IsActioned, n.ActionDate
                        FROM dbo.Notifications n
                        WHERE {RecipientPredicate}
                          AND (n.ExpiryDate IS NULL OR n.ExpiryDate > GETDATE())
                          AND {RelatedModuleViewPredicate}
                        ORDER BY n.IsActioned ASC,
                                 n.IsRead ASC,
                                 n.Priority ASC,
                                 n.NotificationDate DESC,
                                 n.NotificationID DESC;";

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
                  SET IsActioned = 1,
                      ActionDate = ISNULL(ActionDate, GETDATE()),
                      IsRead = 1,
                      ReadDate = ISNULL(ReadDate, GETDATE())
                  WHERE RelatedModule = @Module
                    AND RelatedRecordID = @RecordID
                    AND IsActioned = 0",
                new { Module = relatedModule, RecordID = relatedRecordId });
        }

        public Task MarkRelatedAsActionedAsync(
            IDbConnection connection,
            IDbTransaction transaction,
            string relatedModule,
            int relatedRecordId)
        {
            return connection.ExecuteAsync(
                @"UPDATE dbo.Notifications
                  SET IsActioned = 1,
                      ActionDate = ISNULL(ActionDate, GETDATE()),
                      IsRead = 1,
                      ReadDate = ISNULL(ReadDate, GETDATE())
                  WHERE RelatedModule = @Module
                    AND RelatedRecordID = @RecordID
                    AND IsActioned = 0",
                new { Module = relatedModule, RecordID = relatedRecordId }, transaction);
        }

        public async Task<int> GetUnreadCountAsync(int userId, int roleId)
        {
            RequireCurrentSession(userId, roleId);
            using var connection = CreateConnection();
            var sql = $@"SELECT COUNT(*)
                        FROM dbo.Notifications n
                        WHERE {RecipientPredicate}
                          AND n.IsRead = 0
                          AND (n.ExpiryDate IS NULL OR n.ExpiryDate > GETDATE())
                          AND {RelatedModuleViewPredicate};";

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

            // Keep the existing audible default when the optional setting has not been configured.
            if (string.IsNullOrWhiteSpace(value)) return true;
            return value.Trim() == "1" || bool.TryParse(value.Trim(), out var enabled) && enabled;
        }

        // Keep the one-argument API safe for existing callers by deriving identity from the active session.
        public Task MarkAsReadAsync(int notificationId)
            => MarkAsReadAsync(notificationId, _userSession.UserID, _userSession.RoleID);

        public async Task MarkAsReadAsync(int notificationId, int userId, int roleId)
        {
            RequireCurrentSession(userId, roleId);
            using var connection = CreateConnection();
            var sql = $@"UPDATE n
                        SET IsRead = 1,
                            ReadDate = ISNULL(n.ReadDate, GETDATE())
                        FROM dbo.Notifications n
                        WHERE n.NotificationID = @ID
                          AND {RecipientPredicate}
                          AND (n.ExpiryDate IS NULL OR n.ExpiryDate > GETDATE())
                          AND {RelatedModuleViewPredicate};";

            await connection.ExecuteAsync(sql, new
            {
                ID = notificationId,
                UserID = userId,
                RoleID = roleId
            });
        }

        public async Task MarkAllAsReadAsync(int userId, int roleId)
        {
            RequireCurrentSession(userId, roleId);
            using var connection = CreateConnection();
            var sql = $@"UPDATE n
                        SET IsRead = 1,
                            ReadDate = ISNULL(n.ReadDate, GETDATE())
                        FROM dbo.Notifications n
                        WHERE {RecipientPredicate}
                          AND n.IsRead = 0
                          AND (n.ExpiryDate IS NULL OR n.ExpiryDate > GETDATE())
                          AND {RelatedModuleViewPredicate};";

            await connection.ExecuteAsync(sql, new
            {
                UserID = userId,
                RoleID = roleId
            });
        }

        // Unrestricted notification inserts are intentionally not exposed. Use the
        // permission-aware recipient methods below for module-linked notifications.

        /// <summary>
        /// Creates a private notification per active user who can view the related module
        /// and holds the requested effective permission. An optional role filter preserves
        /// explicitly configured role routing while honoring per-user permission overrides.
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
            int? excludeUserId = null,
            int? targetRoleId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var count = await CreateForPermissionHoldersCoreAsync(
                    connection, transaction, notificationType, title, message, priority,
                    permissionModuleCode, requiredPermission, relatedModule, relatedRecordId,
                    createdBy, excludeUserId, targetRoleId);
                transaction.Commit();
                return count;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        public Task<int> CreateForPermissionHoldersAsync(
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
            int? excludeUserId = null,
            int? targetRoleId = null)
        {
            return CreateForPermissionHoldersCoreAsync(
                connection, transaction, notificationType, title, message, priority,
                permissionModuleCode, requiredPermission, relatedModule, relatedRecordId,
                createdBy, excludeUserId, targetRoleId);
        }

        private async Task<int> CreateForPermissionHoldersCoreAsync(
            IDbConnection connection,
            IDbTransaction? transaction,
            byte notificationType,
            string title,
            string message,
            byte priority,
            string permissionModuleCode,
            ModulePermissionRequirement requiredPermission,
            string relatedModule,
            int relatedRecordId,
            int createdBy,
            int? excludeUserId,
            int? targetRoleId)
        {
            if (string.IsNullOrWhiteSpace(permissionModuleCode))
                throw new ArgumentException("رمز وحدة الصلاحية مطلوب.", nameof(permissionModuleCode));
            if (string.IsNullOrWhiteSpace(relatedModule))
                throw new ArgumentException("رمز الوحدة المرتبطة مطلوب.", nameof(relatedModule));

            var userIds = await GetPermissionHolderUserIdsAsync(
                connection, transaction, permissionModuleCode, requiredPermission,
                relatedModule, excludeUserId, targetRoleId);

            foreach (var targetUserId in userIds)
            {
                await InsertPrivateNotificationAsync(
                    connection, transaction, notificationType, title, message, priority,
                    targetUserId, relatedModule, relatedRecordId, createdBy);
            }

            return userIds.Count;
        }

        /// <summary>
        /// Sends a current stock alert only when the same unread alert is not already present
        /// for that user. Existing notification history is never deleted by a refresh.
        /// </summary>
        public async Task<int> CreateAlertForPermissionHoldersIfMissingAsync(
            byte notificationType,
            string title,
            string message,
            byte priority,
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
                var userIds = await GetPermissionHolderUserIdsAsync(
                    connection, transaction, permissionModuleCode,
                    ModulePermissionRequirement.View, relatedModule,
                    excludeUserId: null, targetRoleId: null);

                var insertedCount = 0;
                foreach (var targetUserId in userIds)
                {
                    insertedCount += await connection.ExecuteAsync(
                        @"IF NOT EXISTS
                          (
                              SELECT 1
                              FROM dbo.Notifications WITH (UPDLOCK, HOLDLOCK)
                              WHERE TargetUserID = @TargetUserID
                                AND TargetRoleID IS NULL
                                AND NotificationType = @Type
                                AND Title = @Title
                                AND Message = @Message
                                AND RelatedModule = @RelatedModule
                                AND ISNULL(RelatedRecordID, 0) = @RelatedRecordID
                                AND IsRead = 0
                                AND IsActioned = 0
                                AND (ExpiryDate IS NULL OR ExpiryDate > GETDATE())
                          )
                          BEGIN
                              INSERT INTO dbo.Notifications
                                  (NotificationType, Title, Message, Priority, TargetUserID, TargetRoleID,
                                   RelatedModule, RelatedRecordID, CreatedBy, CreatedDate)
                              VALUES
                                  (@Type, @Title, @Message, @Priority, @TargetUserID, NULL,
                                   @RelatedModule, @RelatedRecordID, @CreatedBy, GETDATE());
                          END;",
                        new
                        {
                            TargetUserID = targetUserId,
                            Type = notificationType,
                            Title = title,
                            Message = message,
                            Priority = priority,
                            RelatedModule = relatedModule,
                            RelatedRecordID = relatedRecordId,
                            CreatedBy = createdBy
                        }, transaction);
                }

                transaction.Commit();
                return insertedCount;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        private static async Task<List<int>> GetPermissionHolderUserIdsAsync(
            IDbConnection connection,
            IDbTransaction? transaction,
            string permissionModuleCode,
            ModulePermissionRequirement requiredPermission,
            string relatedModule,
            int? excludeUserId,
            int? targetRoleId)
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

            var userIds = await connection.QueryAsync<int>(
                $@"SELECT DISTINCT u.UserID
                   FROM dbo.SystemUsers u
                   INNER JOIN dbo.vw_UserEffectivePermissions p ON p.UserID = u.UserID
                   WHERE u.IsActive = 1
                     AND p.ModuleCode = @ModuleCode
                     AND p.CanView = 1
                     AND p.{permissionColumn} = 1
                     AND (@TargetRoleID IS NULL OR u.RoleID = @TargetRoleID)
                     AND (@ExcludeUserID IS NULL OR u.UserID <> @ExcludeUserID)
                     AND EXISTS
                     (
                         SELECT 1
                         FROM dbo.vw_UserEffectivePermissions relatedPermission
                         WHERE relatedPermission.UserID = u.UserID
                           AND relatedPermission.ModuleCode = @RelatedModule
                           AND relatedPermission.CanView = 1
                     )
                   ORDER BY u.UserID;",
                new
                {
                    ModuleCode = permissionModuleCode,
                    RelatedModule = relatedModule,
                    ExcludeUserID = excludeUserId,
                    TargetRoleID = targetRoleId
                }, transaction);

            return userIds.ToList();
        }

        /// <summary>Routes a notification to one user only when they can view both modules involved.</summary>
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
            if (string.IsNullOrWhiteSpace(permissionModuleCode) || string.IsNullOrWhiteSpace(relatedModule))
                throw new ArgumentException("رموز الوحدات مطلوبة للتحقق من صلاحية مستلم الإشعار.");

            var canView = await connection.ExecuteScalarAsync<int>(
                @"SELECT CASE WHEN
                      EXISTS
                      (
                          SELECT 1 FROM dbo.SystemUsers
                          WHERE UserID=@UserID AND IsActive=1
                      )
                      AND EXISTS
                      (
                          SELECT 1 FROM dbo.vw_UserEffectivePermissions
                          WHERE UserID=@UserID AND ModuleCode=@ModuleCode AND CanView=1
                      )
                      AND EXISTS
                      (
                          SELECT 1 FROM dbo.vw_UserEffectivePermissions
                          WHERE UserID=@UserID AND ModuleCode=@RelatedModule AND CanView=1
                      )
                      THEN 1 ELSE 0 END",
                new { UserID = targetUserId, ModuleCode = permissionModuleCode, RelatedModule = relatedModule }, transaction);
            if (canView == 0) return false;

            await InsertPrivateNotificationAsync(
                connection, transaction, notificationType, title, message, priority,
                targetUserId, relatedModule, relatedRecordId, createdBy);
            return true;
        }

        private static Task<int> InsertPrivateNotificationAsync(
            IDbConnection connection,
            IDbTransaction? transaction,
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
                  (NotificationType, Title, Message, Priority, TargetUserID, TargetRoleID,
                   RelatedModule, RelatedRecordID, CreatedBy, CreatedDate)
                  VALUES
                  (@Type, @Title, @Message, @Priority, @TargetUserID, NULL,
                   @RelatedModule, @RelatedRecordID, @CreatedBy, GETDATE())",
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
