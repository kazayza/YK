using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class AuthService : BaseDbService
    {
        public AuthService(IConfiguration configuration) : base(configuration) { }

        public async Task<LoginResultDto> LoginAsync(
            string username, string password,
            string? ipAddress = null, string? machineName = null)
        {
            using var connection = CreateConnection();
            var passwordHash = HashPassword(password);

            var sql = @"
                SELECT
                    u.UserID, u.Username, u.FullName,
                    u.PasswordHash,
                    u.RoleID, r.RoleNameAr, r.RoleCode,
                    u.IsActive, u.IsLocked,
                    u.MustChangePassword,
                    u.PasswordChangedDate,
                    u.PasswordExpiryDays,
                    u.EmployeeID
                FROM dbo.SystemUsers u
                INNER JOIN dbo.UserRoles r ON u.RoleID = r.RoleID
                WHERE u.Username = @Username";

            var user = await connection.QueryFirstOrDefaultAsync<UserLoginDto>(
                sql, new { Username = username });

            if (user == null)
                return new LoginResultDto
                {
                    Success = false,
                    Message = "اسم المستخدم غير موجود"
                };

            if (!user.IsActive)
                return new LoginResultDto
                {
                    Success = false,
                    Message = "هذا الحساب غير نشط"
                };

            if (user.IsLocked)
                return new LoginResultDto
                {
                    Success = false,
                    Message = "هذا الحساب محظور مؤقتاً"
                };

            if (user.PasswordHash != passwordHash)
            {
                // تحديث عداد المحاولات الفاشلة مع Lockout
                await connection.ExecuteAsync(@"
                    UPDATE dbo.SystemUsers SET
                        FailedLoginAttempts = FailedLoginAttempts + 1,
                        IsLocked = CASE
                            WHEN FailedLoginAttempts + 1 >= MaxFailedAttempts THEN 1
                            ELSE 0 END,
                        LockoutEnd = CASE
                            WHEN FailedLoginAttempts + 1 >= MaxFailedAttempts
                            THEN DATEADD(MINUTE, 30, GETDATE())
                            ELSE NULL END
                    WHERE UserID = @UserID",
                    new { user.UserID });

                // تسجيل المحاولة الفاشلة
                await connection.ExecuteAsync(@"
                    INSERT INTO dbo.LoginHistory
                        (UserID, Username, LoginStatus, IPAddress, MachineName, FailureReason)
                    VALUES
                        (@UserID, @Username, 2, @IP, @Machine, N'كلمة مرور خطأ')",
                    new
                    {
                        user.UserID,
                        user.Username,
                        IP = ipAddress ?? "",
                        Machine = machineName ?? ""
                    });

                return new LoginResultDto
                {
                    Success = false,
                    Message = "كلمة المرور غير صحيحة"
                };
            }

            // دخول ناجح
            await connection.ExecuteAsync(@"
                UPDATE dbo.SystemUsers SET
                    LastLoginDate = GETDATE(),
                    FailedLoginAttempts = 0,
                    IsLocked = 0,
                    LockoutEnd = NULL
                WHERE UserID = @UserID",
                new { user.UserID });

            // تسجيل الدخول الناجح
            await connection.ExecuteAsync(@"
                INSERT INTO dbo.LoginHistory
                    (UserID, Username, LoginStatus, IPAddress, MachineName)
                VALUES
                    (@UserID, @Username, 1, @IP, @Machine)",
                new
                {
                    user.UserID,
                    user.Username,
                    IP = ipAddress ?? "",
                    Machine = machineName ?? ""
                });

            // حساب انتهاء صلاحية كلمة المرور
            var passwordExpired = IsPasswordExpired(
                user.PasswordChangedDate,
                user.PasswordExpiryDays);

            return new LoginResultDto
            {
                Success = true,
                Message = "مرحباً " + user.FullName,
                UserID = user.UserID,
                Username = user.Username,
                FullName = user.FullName,
                RoleID = user.RoleID,
                RoleName = user.RoleNameAr,
                RoleCode = user.RoleCode,
                MustChangePassword = user.MustChangePassword,
                PasswordExpired = passwordExpired,
                EmployeeID = user.EmployeeID
            };
        }

        public async Task<List<ModulePermissionDto>> GetUserModulesAsync(int userId)
        {
            using var connection = CreateConnection();
            var sql = @"
                SELECT
                    sm.ModuleID, sm.ParentModuleID, sm.ModuleCode, sm.ModuleNameAr,
                    sm.IconName, sm.ModuleType, sm.SortOrder,
                    CAST(ISNULL(v.CanView,   0) AS BIT) AS CanView,
                    CAST(ISNULL(v.CanAdd,    0) AS BIT) AS CanAdd,
                    CAST(ISNULL(v.CanEdit,   0) AS BIT) AS CanEdit,
                    CAST(ISNULL(v.CanDelete, 0) AS BIT) AS CanDelete,
                    CAST(ISNULL(v.CanPrint,  0) AS BIT) AS CanPrint,
                    CAST(ISNULL(v.CanExport, 0) AS BIT) AS CanExport,
                    CAST(ISNULL(v.CanApprove,0) AS BIT) AS CanApprove,
                    CAST(ISNULL(v.CanPost,   0) AS BIT) AS CanPost,
                    CAST(ISNULL(v.CanCancel, 0) AS BIT) AS CanCancel
                FROM dbo.SystemModules sm
                LEFT JOIN dbo.vw_UserEffectivePermissions v
                    ON sm.ModuleCode = v.ModuleCode AND v.UserID = @UserID
                WHERE sm.IsActive = 1
                ORDER BY ISNULL(sm.ParentModuleID, sm.ModuleID), sm.SortOrder, sm.ModuleID";

            var result = await connection.QueryAsync<ModulePermissionDto>(sql, new { UserID = userId });
            return result.ToList();
        }

        // ══════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════
        private string HashPassword(string password)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }

        private bool IsPasswordExpired(DateTime? passwordChangedDate, int expiryDays)
        {
            if (expiryDays <= 0) return false;
            if (!passwordChangedDate.HasValue) return false;
            return (DateTime.Now - passwordChangedDate.Value).TotalDays >= expiryDays;
        }
    }
}