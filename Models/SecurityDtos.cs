namespace YKCoatings.Models
{
    // ══════════════════════════════════════
    // المستخدمين - القائمة
    // ══════════════════════════════════════
    public class UserListDto
    {
        public int UserID { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // الدور
        public int RoleID { get; set; }
        public string RoleNameAr { get; set; } = "";
        public string? RoleCode { get; set; }

        // الموظف المرتبط
        public int? EmployeeID { get; set; }
        public string? EmployeeName { get; set; }
        public string? EmployeeCode { get; set; }

        // الحالة
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public bool MustChangePassword { get; set; }
        public int FailedLoginAttempts { get; set; }

        // آخر دخول
        public DateTime? LastLoginDate { get; set; }
        public string? LastLoginIP { get; set; }

        // الإعدادات الافتراضية
        public string? DefaultWarehouseName { get; set; }
        public string? DefaultCashBoxName { get; set; }
    }

    // ══════════════════════════════════════
    // المستخدمين - التحرير
    // ══════════════════════════════════════
    public class UserEditDto
    {
        public int UserID { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // الدور
        public int RoleID { get; set; }

        // الموظف المرتبط
        public int? EmployeeID { get; set; }

        // الحالة
        public bool IsActive { get; set; } = true;
        public bool IsLocked { get; set; }
        public bool MustChangePassword { get; set; } = true;

        // الأمان
        public int MaxFailedAttempts { get; set; } = 5;
        public int PasswordExpiryDays { get; set; } = 90;
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockoutEnd { get; set; }
        public DateTime? PasswordChangedDate { get; set; }

        // الإعدادات الافتراضية
        public string DefaultLanguage { get; set; } = "ar";
        public int? DefaultWarehouseID { get; set; }
        public int? DefaultCashBoxID { get; set; }

        // بيانات إضافية
        public string? ProfileImage { get; set; }
        public string? Notes { get; set; }

        // آخر دخول (عرض فقط)
        public DateTime? LastLoginDate { get; set; }
        public string? LastLoginIP { get; set; }
        public string? LastLoginMachine { get; set; }

        // Audit (عرض فقط)
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    // ══════════════════════════════════════
    // المستخدمين - كلمة المرور
    // ══════════════════════════════════════
    public class UserPasswordDto
    {
        public int UserID { get; set; }
        public string NewPassword { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
        public bool MustChangeOnLogin { get; set; } = true;
    }

    // ══════════════════════════════════════
    // الأدوار - القائمة
    // ══════════════════════════════════════
    public class RoleListDto
    {
        public int RoleID { get; set; }
        public string RoleCode { get; set; } = "";
        public string RoleNameAr { get; set; } = "";
        public string? RoleNameEn { get; set; }
        public string? Description { get; set; }
        public bool IsSystemRole { get; set; }
        public bool IsActive { get; set; }

        // إحصائيات
        public int UserCount { get; set; }
        public int PermissionCount { get; set; }
    }

    // ══════════════════════════════════════
    // الأدوار - التحرير
    // ══════════════════════════════════════
    public class RoleEditDto
    {
        public int RoleID { get; set; }
        public string RoleCode { get; set; } = "";
        public string RoleNameAr { get; set; } = "";
        public string? RoleNameEn { get; set; }
        public string? Description { get; set; }
        public bool IsSystemRole { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ══════════════════════════════════════
    // صلاحيات الدور - لكل شاشة
    // ══════════════════════════════════════
    public class RolePermissionEditDto
    {
        public int PermissionID { get; set; }
        public int RoleID { get; set; }
        public int ModuleID { get; set; }

        // بيانات الشاشة (عرض فقط)
        public string ModuleCode { get; set; } = "";
        public string ModuleNameAr { get; set; } = "";
        public int ModuleType { get; set; }
        public int? ParentModuleID { get; set; }
        public string? ParentModuleNameAr { get; set; }
        public int SortOrder { get; set; }

        // الصلاحيات
        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanPrint { get; set; }
        public bool CanExport { get; set; }
        public bool CanApprove { get; set; }
        public bool CanPost { get; set; }
        public bool CanCancel { get; set; }

        // مساعد للـ UI
        public bool IsExpanded { get; set; } = true;
    }

    // ══════════════════════════════════════
    // صلاحيات المستخدم الخاصة (Override)
    // ══════════════════════════════════════
    public class UserPermissionEditDto
    {
        public int UserPermissionID { get; set; }
        public int UserID { get; set; }
        public int ModuleID { get; set; }

        // بيانات الشاشة (عرض فقط)
        public string ModuleCode { get; set; } = "";
        public string ModuleNameAr { get; set; } = "";
        public int ModuleType { get; set; }
        public int? ParentModuleID { get; set; }

        // صلاحيات الدور الأصلية (عرض فقط للمقارنة)
        public bool? RoleCanView { get; set; }
        public bool? RoleCanAdd { get; set; }
        public bool? RoleCanEdit { get; set; }
        public bool? RoleCanDelete { get; set; }
        public bool? RoleCanPrint { get; set; }
        public bool? RoleCanExport { get; set; }
        public bool? RoleCanApprove { get; set; }
        public bool? RoleCanPost { get; set; }
        public bool? RoleCanCancel { get; set; }

        // Override الخاص بالمستخدم
        // null = يتبع الدور
        // true = سماح إضافي
        // false = منع خاص
        public bool? CanView { get; set; }
        public bool? CanAdd { get; set; }
        public bool? CanEdit { get; set; }
        public bool? CanDelete { get; set; }
        public bool? CanPrint { get; set; }
        public bool? CanExport { get; set; }
        public bool? CanApprove { get; set; }
        public bool? CanPost { get; set; }
        public bool? CanCancel { get; set; }

        // الصلاحية النهائية الفعلية (محسوبة)
        public bool EffectiveCanView => CanView ?? RoleCanView ?? false;
        public bool EffectiveCanAdd => CanAdd ?? RoleCanAdd ?? false;
        public bool EffectiveCanEdit => CanEdit ?? RoleCanEdit ?? false;
        public bool EffectiveCanDelete => CanDelete ?? RoleCanDelete ?? false;
        public bool EffectiveCanPrint => CanPrint ?? RoleCanPrint ?? false;
        public bool EffectiveCanExport => CanExport ?? RoleCanExport ?? false;
        public bool EffectiveCanApprove => CanApprove ?? RoleCanApprove ?? false;
        public bool EffectiveCanPost => CanPost ?? RoleCanPost ?? false;
        public bool EffectiveCanCancel => CanCancel ?? RoleCanCancel ?? false;
    }

    // ══════════════════════════════════════
    // سجل الدخول
    // ══════════════════════════════════════
    public class LoginHistoryDto
    {
        public int LoginID { get; set; }
        public int? UserID { get; set; }
        public string Username { get; set; } = "";
        public string? FullName { get; set; }
        public string? RoleNameAr { get; set; }

        public DateTime LoginTime { get; set; }
        public DateTime? LogoutTime { get; set; }
        public int? SessionDuration { get; set; }

        public int LoginStatus { get; set; }
        public string LoginStatusName { get; set; } = "";

        public string? IPAddress { get; set; }
        public string? MachineName { get; set; }
        public string? FailureReason { get; set; }
    }

    // ══════════════════════════════════════
    // سجل العمليات
    // ══════════════════════════════════════
    public class AuditLogDto
    {
        public long AuditID { get; set; }
        public DateTime AuditDate { get; set; }
        public int? UserID { get; set; }
        public string? Username { get; set; }

        public int ActionType { get; set; }
        public string ActionTypeName { get; set; } = "";

        public string TableName { get; set; } = "";
        public string? RecordID { get; set; }

        public string? ModuleName { get; set; }
        public string? FormName { get; set; }
        public string? Description { get; set; }

        public string? IPAddress { get; set; }
        public string? MachineName { get; set; }

        // التفاصيل (تُعرض عند فتح السجل)
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? ChangedColumns { get; set; }
    }

    // ══════════════════════════════════════
    // فلاتر البحث
    // ══════════════════════════════════════
    public class UserFilterDto
    {
        public string? SearchText { get; set; }
        public int? RoleID { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsLocked { get; set; }
        public bool? HasEmployee { get; set; }
    }

    public class LoginHistoryFilterDto
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Username { get; set; }
        public int? RoleID { get; set; }
        public int? LoginStatus { get; set; }
        public string? IPAddress { get; set; }
    }

    public class AuditLogFilterDto
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Username { get; set; }
        public int? ActionType { get; set; }
        public string? TableName { get; set; }
        public string? ModuleName { get; set; }
        public string? RecordID { get; set; }
    }
}