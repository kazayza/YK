using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class UserService : BaseDbService
    {
        public UserService(IConfiguration configuration) : base(configuration) { }

        // ══════════════════════════════════════
        // 1) جلب قائمة المستخدمين مع الفلاتر
        // ══════════════════════════════════════
        public async Task<List<UserListDto>> GetAllAsync(UserFilterDto? filter = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    u.UserID,
                    u.Username,
                    u.FullName,
                    u.Email,
                    u.Phone,
                    u.RoleID,
                    r.RoleNameAr,
                    r.RoleCode,
                    u.EmployeeID,
                    e.FullNameAr AS EmployeeName,
                    e.EmployeeCode,
                    u.IsActive,
                    u.IsLocked,
                    u.MustChangePassword,
                    u.FailedLoginAttempts,
                    u.LastLoginDate,
                    u.LastLoginIP,
                    w.WarehouseNameAr AS DefaultWarehouseName,
                    cb.CashBoxNameAr AS DefaultCashBoxName
                FROM dbo.SystemUsers u
                INNER JOIN dbo.UserRoles r ON u.RoleID = r.RoleID
                LEFT JOIN dbo.Employees e ON u.EmployeeID = e.EmployeeID
                LEFT JOIN dbo.Warehouses w ON u.DefaultWarehouseID = w.WarehouseID
                LEFT JOIN dbo.CashBoxes cb ON u.DefaultCashBoxID = cb.CashBoxID
                WHERE 1=1";

            var parameters = new DynamicParameters();

            // فلتر البحث العام
            if (!string.IsNullOrWhiteSpace(filter?.SearchText))
            {
                sql += @" AND (
                    u.Username LIKE @Search
                    OR u.FullName LIKE @Search
                    OR u.Email LIKE @Search
                    OR u.Phone LIKE @Search
                )";
                parameters.Add("Search", $"%{filter.SearchText}%");
            }

            // فلتر الدور
            if (filter?.RoleID.HasValue == true)
            {
                sql += " AND u.RoleID = @RoleID";
                parameters.Add("RoleID", filter.RoleID.Value);
            }

            // فلتر الحالة
            if (filter?.IsActive.HasValue == true)
            {
                sql += " AND u.IsActive = @IsActive";
                parameters.Add("IsActive", filter.IsActive.Value);
            }

            // فلتر الحظر
            if (filter?.IsLocked.HasValue == true)
            {
                sql += " AND u.IsLocked = @IsLocked";
                parameters.Add("IsLocked", filter.IsLocked.Value);
            }

            // فلتر الموظف
            if (filter?.HasEmployee.HasValue == true)
            {
                if (filter.HasEmployee.Value)
                    sql += " AND u.EmployeeID IS NOT NULL";
                else
                    sql += " AND u.EmployeeID IS NULL";
            }

            sql += " ORDER BY u.UserID";

            var result = await connection.QueryAsync<UserListDto>(sql, parameters);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 2) جلب مستخدم واحد للتحرير
        // ══════════════════════════════════════
        public async Task<UserEditDto?> GetByIdAsync(int userId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    u.UserID,
                    u.Username,
                    u.FullName,
                    u.Email,
                    u.Phone,
                    u.RoleID,
                    u.EmployeeID,
                    u.IsActive,
                    u.IsLocked,
                    u.MustChangePassword,
                    u.MaxFailedAttempts,
                    u.PasswordExpiryDays,
                    u.FailedLoginAttempts,
                    u.LockoutEnd,
                    u.PasswordChangedDate,
                    u.DefaultLanguage,
                    u.DefaultWarehouseID,
                    u.DefaultCashBoxID,
                    u.ProfileImage,
                    u.Notes,
                    u.LastLoginDate,
                    u.LastLoginIP,
                    u.LastLoginMachine,
                    u.CreatedBy,
                    u.CreatedDate,
                    u.ModifiedBy,
                    u.ModifiedDate
                FROM dbo.SystemUsers u
                WHERE u.UserID = @UserID";

            return await connection.QueryFirstOrDefaultAsync<UserEditDto>(sql, new { UserID = userId });
        }

        // ══════════════════════════════════════
        // 3) إنشاء مستخدم جديد
        // ══════════════════════════════════════
        public async Task<int> CreateAsync(UserEditDto dto, string password, int createdBy)
        {
            using var connection = CreateConnection();

            // التحقق من عدم تكرار اسم المستخدم
            var exists = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(1) FROM dbo.SystemUsers WHERE Username = @Username",
                new { dto.Username });

            if (exists > 0)
                throw new Exception("اسم المستخدم موجود بالفعل");

            var passwordHash = HashPassword(password);

            var sql = @"
                INSERT INTO dbo.SystemUsers (
                    Username, PasswordHash, FullName, Email, Phone,
                    RoleID, EmployeeID,
                    IsActive, MustChangePassword,
                    MaxFailedAttempts, PasswordExpiryDays,
                    DefaultLanguage, DefaultWarehouseID, DefaultCashBoxID,
                    ProfileImage, Notes,
                    PasswordChangedDate, CreatedBy, CreatedDate
                ) VALUES (
                    @Username, @PasswordHash, @FullName, @Email, @Phone,
                    @RoleID, @EmployeeID,
                    @IsActive, @MustChangePassword,
                    @MaxFailedAttempts, @PasswordExpiryDays,
                    @DefaultLanguage, @DefaultWarehouseID, @DefaultCashBoxID,
                    @ProfileImage, @Notes,
                    GETDATE(), @CreatedBy, GETDATE()
                );
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.QuerySingleAsync<int>(sql, new
            {
                dto.Username,
                PasswordHash = passwordHash,
                dto.FullName,
                dto.Email,
                dto.Phone,
                dto.RoleID,
                dto.EmployeeID,
                dto.IsActive,
                dto.MustChangePassword,
                dto.MaxFailedAttempts,
                dto.PasswordExpiryDays,
                dto.DefaultLanguage,
                dto.DefaultWarehouseID,
                dto.DefaultCashBoxID,
                dto.ProfileImage,
                dto.Notes,
                CreatedBy = createdBy
            });

            return newId;
        }

        // ══════════════════════════════════════
        // 4) تعديل مستخدم
        // ══════════════════════════════════════
        public async Task<bool> UpdateAsync(UserEditDto dto, int modifiedBy)
        {
            using var connection = CreateConnection();

            // التحقق من عدم تكرار اسم المستخدم مع مستخدم آخر
            var exists = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(1) FROM dbo.SystemUsers WHERE Username = @Username AND UserID != @UserID",
                new { dto.Username, dto.UserID });

            if (exists > 0)
                throw new Exception("اسم المستخدم موجود بالفعل لمستخدم آخر");

            var sql = @"
                UPDATE dbo.SystemUsers SET
                    Username = @Username,
                    FullName = @FullName,
                    Email = @Email,
                    Phone = @Phone,
                    RoleID = @RoleID,
                    EmployeeID = @EmployeeID,
                    IsActive = @IsActive,
                    MustChangePassword = @MustChangePassword,
                    MaxFailedAttempts = @MaxFailedAttempts,
                    PasswordExpiryDays = @PasswordExpiryDays,
                    DefaultLanguage = @DefaultLanguage,
                    DefaultWarehouseID = @DefaultWarehouseID,
                    DefaultCashBoxID = @DefaultCashBoxID,
                    ProfileImage = @ProfileImage,
                    Notes = @Notes,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE UserID = @UserID";

            var rows = await connection.ExecuteAsync(sql, new
            {
                dto.UserID,
                dto.Username,
                dto.FullName,
                dto.Email,
                dto.Phone,
                dto.RoleID,
                dto.EmployeeID,
                dto.IsActive,
                dto.MustChangePassword,
                dto.MaxFailedAttempts,
                dto.PasswordExpiryDays,
                dto.DefaultLanguage,
                dto.DefaultWarehouseID,
                dto.DefaultCashBoxID,
                dto.ProfileImage,
                dto.Notes,
                ModifiedBy = modifiedBy
            });

            return rows > 0;
        }

        // ══════════════════════════════════════
        // 5) تفعيل / تعطيل مستخدم
        // ══════════════════════════════════════
        public async Task<bool> ToggleActiveAsync(int userId, bool isActive, int modifiedBy)
        {
            using var connection = CreateConnection();

            var sql = @"
                UPDATE dbo.SystemUsers SET
                    IsActive = @IsActive,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE UserID = @UserID";

            var rows = await connection.ExecuteAsync(sql, new
            {
                UserID = userId,
                IsActive = isActive,
                ModifiedBy = modifiedBy
            });

            return rows > 0;
        }

        // ══════════════════════════════════════
        // 6) فك حظر مستخدم
        // ══════════════════════════════════════
        public async Task<bool> UnlockAsync(int userId, int modifiedBy)
        {
            using var connection = CreateConnection();

            var sql = @"
                UPDATE dbo.SystemUsers SET
                    IsLocked = 0,
                    FailedLoginAttempts = 0,
                    LockoutEnd = NULL,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE UserID = @UserID";

            var rows = await connection.ExecuteAsync(sql, new
            {
                UserID = userId,
                ModifiedBy = modifiedBy
            });

            return rows > 0;
        }

        // ══════════════════════════════════════
        // 7) إعادة تعيين كلمة المرور
        // ══════════════════════════════════════
        public async Task<bool> ResetPasswordAsync(UserPasswordDto dto, int modifiedBy)
        {
            if (dto.NewPassword != dto.ConfirmPassword)
                throw new Exception("كلمة المرور وتأكيدها غير متطابقتين");

            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
                throw new Exception("كلمة المرور يجب أن تكون 6 أحرف على الأقل");

            using var connection = CreateConnection();

            var passwordHash = HashPassword(dto.NewPassword);

            var sql = @"
                UPDATE dbo.SystemUsers SET
                    PasswordHash = @PasswordHash,
                    MustChangePassword = @MustChange,
                    PasswordChangedDate = GETDATE(),
                    FailedLoginAttempts = 0,
                    IsLocked = 0,
                    LockoutEnd = NULL,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE UserID = @UserID";

            var rows = await connection.ExecuteAsync(sql, new
            {
                dto.UserID,
                PasswordHash = passwordHash,
                MustChange = dto.MustChangeOnLogin,
                ModifiedBy = modifiedBy
            });

            return rows > 0;
        }

        // ══════════════════════════════════════
        // 8) التحقق من أن المستخدم هو Admin
        // ══════════════════════════════════════
        public async Task<bool> IsDefaultAdminAsync(int userId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT COUNT(1) FROM dbo.SystemUsers u
                INNER JOIN dbo.UserRoles r ON u.RoleID = r.RoleID
                WHERE u.UserID = @UserID
                AND r.RoleCode = 'ADMIN'
                AND r.IsSystemRole = 1
                AND u.Username = 'admin'";

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { UserID = userId });
            return count > 0;
        }

        // ══════════════════════════════════════
        // 9) جلب الأدوار للـ Dropdown
        // ══════════════════════════════════════
        public async Task<List<LookupDto>> GetRolesLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    RoleID AS Id,
                    RoleNameAr AS Name,
                    RoleCode AS Code
                FROM dbo.UserRoles
                WHERE IsActive = 1
                ORDER BY RoleNameAr";

            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 10) جلب الموظفين غير المرتبطين للـ Dropdown
        // ══════════════════════════════════════
        public async Task<List<LookupDto>> GetAvailableEmployeesLookupAsync(int? currentEmployeeId = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    e.EmployeeID AS Id,
                    e.FullNameAr + ' (' + e.EmployeeCode + ')' AS Name,
                    e.EmployeeCode AS Code
                FROM dbo.Employees e
                WHERE e.IsActive = 1
                AND (
                    e.EmployeeID NOT IN (
                        SELECT EmployeeID FROM dbo.SystemUsers 
                        WHERE EmployeeID IS NOT NULL
                    )
                    OR e.EmployeeID = @CurrentEmpID
                )
                ORDER BY e.FullNameAr";

            var result = await connection.QueryAsync<LookupDto>(sql, new { CurrentEmpID = currentEmployeeId ?? 0 });
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 11) جلب المخازن للـ Dropdown
        // ══════════════════════════════════════
        public async Task<List<LookupDto>> GetWarehousesLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    WarehouseID AS Id,
                    WarehouseNameAr AS Name,
                    WarehouseCode AS Code
                FROM dbo.Warehouses
                WHERE IsActive = 1
                ORDER BY WarehouseNameAr";

            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 12) جلب الصناديق للـ Dropdown
        // ══════════════════════════════════════
        public async Task<List<LookupDto>> GetCashBoxesLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    CashBoxID AS Id,
                    CashBoxNameAr AS Name,
                    CashBoxCode AS Code
                FROM dbo.CashBoxes
                WHERE IsActive = 1
                ORDER BY CashBoxNameAr";

            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }
        // ══════════════════════════════════════
// 13) جلب صلاحيات المستخدم الخاصة (Override)
// ══════════════════════════════════════
public async Task<List<UserPermissionEditDto>> GetUserPermissionsAsync(int userId)
{
    using var connection = CreateConnection();

    // جلب RoleID للمستخدم
    var roleId = await connection.QueryFirstOrDefaultAsync<int>(
        "SELECT RoleID FROM dbo.SystemUsers WHERE UserID = @UserID",
        new { UserID = userId });

    var sql = @"
        SELECT
            ISNULL(up.UserPermissionID, 0) AS UserPermissionID,
            @UserID AS UserID,
            sm.ModuleID,
            sm.ModuleCode,
            sm.ModuleNameAr,
            sm.ModuleType,
            sm.ParentModuleID,

            -- صلاحيات الدور الأصلية
            CAST(ISNULL(rp.CanView, 0) AS BIT)    AS RoleCanView,
            CAST(ISNULL(rp.CanAdd, 0) AS BIT)     AS RoleCanAdd,
            CAST(ISNULL(rp.CanEdit, 0) AS BIT)    AS RoleCanEdit,
            CAST(ISNULL(rp.CanDelete, 0) AS BIT)  AS RoleCanDelete,
            CAST(ISNULL(rp.CanPrint, 0) AS BIT)   AS RoleCanPrint,
            CAST(ISNULL(rp.CanExport, 0) AS BIT)  AS RoleCanExport,
            CAST(ISNULL(rp.CanApprove, 0) AS BIT) AS RoleCanApprove,
            CAST(ISNULL(rp.CanPost, 0) AS BIT)    AS RoleCanPost,
            CAST(ISNULL(rp.CanCancel, 0) AS BIT)  AS RoleCanCancel,

            -- Override الخاص بالمستخدم
            up.CanView,
            up.CanAdd,
            up.CanEdit,
            up.CanDelete,
            up.CanPrint,
            up.CanExport,
            up.CanApprove,
            up.CanPost,
            up.CanCancel

        FROM dbo.SystemModules sm
        LEFT JOIN dbo.RolePermissions rp
            ON sm.ModuleID = rp.ModuleID AND rp.RoleID = @RoleID
        LEFT JOIN dbo.UserPermissions up
            ON sm.ModuleID = up.ModuleID AND up.UserID = @UserID
        WHERE sm.IsActive = 1 AND sm.ModuleType = 2
        ORDER BY ISNULL(sm.ParentModuleID, sm.ModuleID), sm.SortOrder, sm.ModuleID";

    var result = await connection.QueryAsync<UserPermissionEditDto>(
        sql, new { UserID = userId, RoleID = roleId });

    return result.ToList();
}

// ══════════════════════════════════════
// 14) حفظ صلاحيات المستخدم الخاصة
// ══════════════════════════════════════
public async Task SaveUserPermissionsAsync(int userId, List<UserPermissionEditDto> permissions)
{
    using var connection = CreateConnection();
    using var transaction = connection.BeginTransaction();

    try
    {
        // حذف الصلاحيات القديمة
        await connection.ExecuteAsync(
            "DELETE FROM dbo.UserPermissions WHERE UserID = @UserID",
            new { UserID = userId }, transaction);

        // إدراج فقط السجلات اللي فيها Override (مش null)
        foreach (var perm in permissions)
        {
            // لو كل القيم null → مش محتاج نحفظ
            if (perm.CanView == null && perm.CanAdd == null &&
                perm.CanEdit == null && perm.CanDelete == null &&
                perm.CanPrint == null && perm.CanExport == null &&
                perm.CanApprove == null && perm.CanPost == null &&
                perm.CanCancel == null)
                continue;

            await connection.ExecuteAsync(@"
                INSERT INTO dbo.UserPermissions (
                    UserID, ModuleID,
                    CanView, CanAdd, CanEdit, CanDelete,
                    CanPrint, CanExport, CanApprove, CanPost, CanCancel,
                    CreatedDate
                ) VALUES (
                    @UserID, @ModuleID,
                    @CanView, @CanAdd, @CanEdit, @CanDelete,
                    @CanPrint, @CanExport, @CanApprove, @CanPost, @CanCancel,
                    GETDATE()
                )",
                new
                {
                    UserID = userId,
                    perm.ModuleID,
                    perm.CanView,
                    perm.CanAdd,
                    perm.CanEdit,
                    perm.CanDelete,
                    perm.CanPrint,
                    perm.CanExport,
                    perm.CanApprove,
                    perm.CanPost,
                    perm.CanCancel
                }, transaction);
        }

        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}

// ══════════════════════════════════════
// 15) مسح كل الـ Override للمستخدم
// ══════════════════════════════════════
public async Task ClearUserPermissionsAsync(int userId)
{
    using var connection = CreateConnection();
    await connection.ExecuteAsync(
        "DELETE FROM dbo.UserPermissions WHERE UserID = @UserID",
        new { UserID = userId });
}

        // ══════════════════════════════════════
        // Hash كلمة المرور
        // ══════════════════════════════════════
        private string HashPassword(string password)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }
    }
}