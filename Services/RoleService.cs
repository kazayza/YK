using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class RoleService : BaseDbService
    {
        public RoleService(IConfiguration configuration) : base(configuration) { }

        // ══════════════════════════════════════
        // 1) جلب قائمة الأدوار مع الإحصائيات
        // ══════════════════════════════════════
        public async Task<List<RoleListDto>> GetAllAsync(string? searchText = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    r.RoleID,
                    r.RoleCode,
                    r.RoleNameAr,
                    r.RoleNameEn,
                    r.Description,
                    r.IsSystemRole,
                    r.IsActive,
                    ISNULL(uc.UserCount, 0) AS UserCount,
                    ISNULL(pc.PermissionCount, 0) AS PermissionCount
                FROM dbo.UserRoles r
                LEFT JOIN (
                    SELECT RoleID, COUNT(*) AS UserCount
                    FROM dbo.SystemUsers
                    GROUP BY RoleID
                ) uc ON r.RoleID = uc.RoleID
                LEFT JOIN (
                    SELECT RoleID, COUNT(*) AS PermissionCount
                    FROM dbo.RolePermissions
                    WHERE CanView = 1
                    GROUP BY RoleID
                ) pc ON r.RoleID = pc.RoleID
                WHERE 1=1";

            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                sql += @" AND (
                    r.RoleCode LIKE @Search
                    OR r.RoleNameAr LIKE @Search
                    OR r.RoleNameEn LIKE @Search
                    OR r.Description LIKE @Search
                )";
                parameters.Add("Search", $"%{searchText}%");
            }

            sql += " ORDER BY r.RoleID";

            var result = await connection.QueryAsync<RoleListDto>(sql, parameters);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 2) جلب دور واحد للتحرير
        // ══════════════════════════════════════
        public async Task<RoleEditDto?> GetByIdAsync(int roleId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    RoleID,
                    RoleCode,
                    RoleNameAr,
                    RoleNameEn,
                    Description,
                    IsSystemRole,
                    IsActive
                FROM dbo.UserRoles
                WHERE RoleID = @RoleID";

            return await connection.QueryFirstOrDefaultAsync<RoleEditDto>(sql, new { RoleID = roleId });
        }

        // ══════════════════════════════════════
        // 3) إنشاء دور جديد
        // ══════════════════════════════════════
        public async Task<int> CreateAsync(RoleEditDto dto)
        {
            using var connection = CreateConnection();

            // التحقق من عدم تكرار الكود
            var exists = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(1) FROM dbo.UserRoles WHERE RoleCode = @RoleCode",
                new { dto.RoleCode });

            if (exists > 0)
                throw new Exception("كود الدور موجود بالفعل");

            var sql = @"
                INSERT INTO dbo.UserRoles (
                    RoleCode, RoleNameAr, RoleNameEn,
                    Description, IsSystemRole, IsActive, CreatedDate
                ) VALUES (
                    @RoleCode, @RoleNameAr, @RoleNameEn,
                    @Description, 0, @IsActive, GETDATE()
                );
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.QuerySingleAsync<int>(sql, new
            {
                dto.RoleCode,
                dto.RoleNameAr,
                dto.RoleNameEn,
                dto.Description,
                dto.IsActive
            });

            // إنشاء سجلات الصلاحيات الفارغة لكل الشاشات
            await connection.ExecuteAsync(@"
                INSERT INTO dbo.RolePermissions (RoleID, ModuleID, CanView, CanAdd, CanEdit, CanDelete, CanPrint, CanExport, CanApprove, CanPost, CanCancel)
                SELECT @RoleID, ModuleID, 0, 0, 0, 0, 0, 0, 0, 0, 0
                FROM dbo.SystemModules
                WHERE IsActive = 1",
                new { RoleID = newId });

            return newId;
        }

        // ══════════════════════════════════════
        // 4) تعديل دور
        // ══════════════════════════════════════
        public async Task<bool> UpdateAsync(RoleEditDto dto)
        {
            using var connection = CreateConnection();

            // التحقق من عدم تكرار الكود مع دور آخر
            var exists = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(1) FROM dbo.UserRoles WHERE RoleCode = @RoleCode AND RoleID != @RoleID",
                new { dto.RoleCode, dto.RoleID });

            if (exists > 0)
                throw new Exception("كود الدور موجود بالفعل لدور آخر");

            var sql = @"
                UPDATE dbo.UserRoles SET
                    RoleCode = @RoleCode,
                    RoleNameAr = @RoleNameAr,
                    RoleNameEn = @RoleNameEn,
                    Description = @Description,
                    IsActive = @IsActive
                WHERE RoleID = @RoleID";

            var rows = await connection.ExecuteAsync(sql, new
            {
                dto.RoleID,
                dto.RoleCode,
                dto.RoleNameAr,
                dto.RoleNameEn,
                dto.Description,
                dto.IsActive
            });

            return rows > 0;
        }

        // ══════════════════════════════════════
        // 5) حذف دور (غير نظامي فقط)
        // ══════════════════════════════════════
        public async Task<bool> DeleteAsync(int roleId)
        {
            using var connection = CreateConnection();

            // التحقق من أن الدور ليس نظامي
            var role = await connection.QueryFirstOrDefaultAsync<RoleEditDto>(
                "SELECT RoleID, IsSystemRole FROM dbo.UserRoles WHERE RoleID = @RoleID",
                new { RoleID = roleId });

            if (role == null)
                throw new Exception("الدور غير موجود");

            if (role.IsSystemRole)
                throw new Exception("لا يمكن حذف دور نظامي");

            // التحقق من عدم وجود مستخدمين مرتبطين
            var userCount = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(1) FROM dbo.SystemUsers WHERE RoleID = @RoleID",
                new { RoleID = roleId });

            if (userCount > 0)
                throw new Exception($"لا يمكن حذف الدور - مرتبط بـ {userCount} مستخدم");

            // حذف الصلاحيات أولاً
            await connection.ExecuteAsync(
                "DELETE FROM dbo.RolePermissions WHERE RoleID = @RoleID",
                new { RoleID = roleId });

            // حذف الدور
            var rows = await connection.ExecuteAsync(
                "DELETE FROM dbo.UserRoles WHERE RoleID = @RoleID",
                new { RoleID = roleId });

            return rows > 0;
        }

        // ══════════════════════════════════════
        // 6) تفعيل / تعطيل دور
        // ══════════════════════════════════════
        public async Task<bool> ToggleActiveAsync(int roleId, bool isActive)
        {
            using var connection = CreateConnection();

            // حماية الدور النظامي من التعطيل
            var isSystem = await connection.QueryFirstOrDefaultAsync<bool>(
                "SELECT IsSystemRole FROM dbo.UserRoles WHERE RoleID = @RoleID",
                new { RoleID = roleId });

            if (isSystem && !isActive)
                throw new Exception("لا يمكن تعطيل دور نظامي");

            var rows = await connection.ExecuteAsync(
                "UPDATE dbo.UserRoles SET IsActive = @IsActive WHERE RoleID = @RoleID",
                new { RoleID = roleId, IsActive = isActive });

            return rows > 0;
        }

        // ══════════════════════════════════════
        // 7) جلب صلاحيات الدور (Permission Matrix)
        // ══════════════════════════════════════
        public async Task<List<RolePermissionEditDto>> GetPermissionsAsync(int roleId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    ISNULL(rp.PermissionID, 0) AS PermissionID,
                    @RoleID AS RoleID,
                    sm.ModuleID,
                    sm.ModuleCode,
                    sm.ModuleNameAr,
                    sm.ModuleType,
                    sm.ParentModuleID,
                    pm.ModuleNameAr AS ParentModuleNameAr,
                    sm.SortOrder,
                    ISNULL(rp.CanView, 0) AS CanView,
                    ISNULL(rp.CanAdd, 0) AS CanAdd,
                    ISNULL(rp.CanEdit, 0) AS CanEdit,
                    ISNULL(rp.CanDelete, 0) AS CanDelete,
                    ISNULL(rp.CanPrint, 0) AS CanPrint,
                    ISNULL(rp.CanExport, 0) AS CanExport,
                    ISNULL(rp.CanApprove, 0) AS CanApprove,
                    ISNULL(rp.CanPost, 0) AS CanPost,
                    ISNULL(rp.CanCancel, 0) AS CanCancel
                FROM dbo.SystemModules sm
                LEFT JOIN dbo.RolePermissions rp ON sm.ModuleID = rp.ModuleID AND rp.RoleID = @RoleID
                LEFT JOIN dbo.SystemModules pm ON sm.ParentModuleID = pm.ModuleID
                WHERE sm.IsActive = 1
                ORDER BY ISNULL(sm.ParentModuleID, sm.ModuleID), sm.SortOrder, sm.ModuleID";

            var result = await connection.QueryAsync<RolePermissionEditDto>(sql, new { RoleID = roleId });
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 8) حفظ صلاحيات الدور (كاملة)
        // ══════════════════════════════════════
        public async Task SavePermissionsAsync(int roleId, List<RolePermissionEditDto> permissions)
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                // حذف الصلاحيات القديمة
                await connection.ExecuteAsync(
                    "DELETE FROM dbo.RolePermissions WHERE RoleID = @RoleID",
                    new { RoleID = roleId }, transaction);

                // إدراج الصلاحيات الجديدة
                foreach (var perm in permissions)
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO dbo.RolePermissions (
                            RoleID, ModuleID,
                            CanView, CanAdd, CanEdit, CanDelete,
                            CanPrint, CanExport, CanApprove, CanPost, CanCancel,
                            CreatedDate
                        ) VALUES (
                            @RoleID, @ModuleID,
                            @CanView, @CanAdd, @CanEdit, @CanDelete,
                            @CanPrint, @CanExport, @CanApprove, @CanPost, @CanCancel,
                            GETDATE()
                        )",
                        new
                        {
                            RoleID = roleId,
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
        // 9) نسخ صلاحيات من دور آخر
        // ══════════════════════════════════════
        public async Task CopyPermissionsAsync(int sourceRoleId, int targetRoleId)
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                // حذف صلاحيات الدور المستهدف
                await connection.ExecuteAsync(
                    "DELETE FROM dbo.RolePermissions WHERE RoleID = @RoleID",
                    new { RoleID = targetRoleId }, transaction);

                // نسخ من المصدر
                await connection.ExecuteAsync(@"
                    INSERT INTO dbo.RolePermissions (
                        RoleID, ModuleID,
                        CanView, CanAdd, CanEdit, CanDelete,
                        CanPrint, CanExport, CanApprove, CanPost, CanCancel,
                        CreatedDate
                    )
                    SELECT 
                        @TargetRoleID, ModuleID,
                        CanView, CanAdd, CanEdit, CanDelete,
                        CanPrint, CanExport, CanApprove, CanPost, CanCancel,
                        GETDATE()
                    FROM dbo.RolePermissions
                    WHERE RoleID = @SourceRoleID",
                    new { SourceRoleID = sourceRoleId, TargetRoleID = targetRoleId }, transaction);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ══════════════════════════════════════
        // 10) جلب الأدوار للـ Dropdown (للنسخ)
        // ══════════════════════════════════════
        public async Task<List<LookupDto>> GetRolesForCopyAsync(int excludeRoleId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    RoleID AS Id,
                    RoleNameAr + ' (' + RoleCode + ')' AS Name,
                    RoleCode AS Code
                FROM dbo.UserRoles
                WHERE IsActive = 1 AND RoleID != @ExcludeID
                ORDER BY RoleNameAr";

            var result = await connection.QueryAsync<LookupDto>(sql, new { ExcludeID = excludeRoleId });
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 11) التحقق من أن الدور نظامي
        // ══════════════════════════════════════
        public async Task<bool> IsSystemRoleAsync(int roleId)
        {
            using var connection = CreateConnection();

            var isSystem = await connection.QueryFirstOrDefaultAsync<bool>(
                "SELECT ISNULL(IsSystemRole, 0) FROM dbo.UserRoles WHERE RoleID = @RoleID",
                new { RoleID = roleId });

            return isSystem;
        }
    }
}