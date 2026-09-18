using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace YKCoatings.Services
{
    public class UserSessionService
    {
        private readonly ProtectedSessionStorage _storage;

        // قفل يمنع تكرار قراءة الجلسة من التخزين عند استدعاء EnsureRestoredAsync
        // من أكثر من مكوّن في نفس الوقت (Layout + Page) بعد عمل Refresh
        private readonly SemaphoreSlim _restoreLock = new(1, 1);

        public event Action? OnChange;

        /// <summary>
        /// هل تمت محاولة استرجاع الجلسة من التخزين في هذه الدائرة (circuit)؟
        /// يُستخدم لتجنّب إعادة القراءة أكثر من مرة واحدة.
        /// </summary>
        public bool IsRestoreAttempted { get; private set; }

        // === بيانات المستخدم ===
        public int UserID { get; private set; }
        public string Username { get; private set; } = "";
        public string FullName { get; private set; } = "";
        public int RoleID { get; private set; }
        public string RoleName { get; private set; } = "";
        public bool IsLoggedIn { get; private set; }

        // === بيانات الموظف ===
        public int? EmployeeID { get; private set; }
        // === إجبار تغيير كلمة المرور ===
public bool MustChangePassword { get; private set; }
public bool PasswordExpired { get; private set; }

/// <summary>
/// هل يجب إجبار المستخدم على تغيير كلمة المرور
/// يشمل: أول دخول + انتهاء الصلاحية + بعد Reset
/// </summary>
public bool RequiresPasswordChange => MustChangePassword || PasswordExpired;

// === RoleCode ===
public string RoleCode { get; private set; } = "";

        /// <summary>
        /// لاستخدامه في حقول وظيفية مثل ApprovedBy, RequestedBy, ReceivedBy
        /// لو المستخدم مش موظف يرجع null
        /// </summary>
        public int? CurrentEmployeeID => EmployeeID > 0 ? EmployeeID : null;

        /// <summary>
        /// لاستخدامه في CreatedBy, ModifiedBy, AuditLog, Notifications
        /// </summary>
        public int CurrentUserID => UserID;

        // === بيانات الجهاز ===
        public string IPAddress { get; set; } = "";
        public string MachineName { get; set; } = "";

        public UserSessionService(ProtectedSessionStorage storage)
        {
            _storage = storage;
        }

        // === تسجيل الدخول ===
        public async Task LoginAsync(
    int userId, string username, string fullName,
    int roleId, string roleName, string roleCode = "",
    int? employeeId = null,
    bool mustChangePassword = false,
    bool passwordExpired = false)
        {
            UserID = userId;
            Username = username;
            FullName = fullName;
            RoleID = roleId;
            RoleName = roleName;
            EmployeeID = employeeId;
            MustChangePassword = mustChangePassword;
            PasswordExpired = passwordExpired;
            RoleCode = roleCode;
            IsLoggedIn = true;
            IsRestoreAttempted = true;

            await _storage.SetAsync("s_uid", userId);
            await _storage.SetAsync("s_uname", username);
            await _storage.SetAsync("s_fname", fullName);
            await _storage.SetAsync("s_rid", roleId);
            await _storage.SetAsync("s_rname", roleName);
            await _storage.SetAsync("s_eid", employeeId ?? 0);
            await _storage.SetAsync("s_mcp", mustChangePassword);
await _storage.SetAsync("s_pex", passwordExpired);
await _storage.SetAsync("s_rcode", roleCode);
            await _storage.SetAsync("s_login", true);

            NotifyStateChanged();
        }

        // === توافق مع الكود القديم ===
        public void Login(
            int userId, string username, string fullName,
            int roleId, string roleName, int? employeeId = null)
        {
            UserID = userId;
            Username = username;
            FullName = fullName;
            RoleID = roleId;
            RoleName = roleName;
            EmployeeID = employeeId;
            IsLoggedIn = true;
            IsRestoreAttempted = true;
            NotifyStateChanged();
        }

        // === استعادة الجلسة ===
        /// <summary>
        /// يسترجع الجلسة من ProtectedSessionStorage مرة واحدة فقط لكل دائرة (circuit)
        /// آمن للاستدعاء المتزامن من عدة مكونات (Layout + Page) — single-flight
        /// </summary>
        /// <returns>true إذا كان المستخدم مسجّلاً للدخول</returns>
        /// <remarks>
        /// ⚠️ يجب استدعاؤه بعد الرندر فقط (OnAfterRenderAsync) لأنه يحتاج JS Interop.
        /// </remarks>
        public async Task<bool> EnsureRestoredAsync()
        {
            // الحالة معروفة سلفاً (مسجّل دخول أو تمت محاولة سابقة) → لا حاجة لقراءة التخزين
            if (IsLoggedIn) { IsRestoreAttempted = true; return true; }
            if (IsRestoreAttempted) return false;

            await _restoreLock.WaitAsync();
            try
            {
                if (IsLoggedIn) { IsRestoreAttempted = true; return true; }
                if (IsRestoreAttempted) return false;

                var ok = await RestoreFromStorageAsync();
                IsRestoreAttempted = true;
                return ok;
            }
            finally
            {
                _restoreLock.Release();
            }
        }

        /// <summary>توافق مع الكود القديم — نفس سلوك EnsureRestoredAsync</summary>
        public Task<bool> TryRestoreSessionAsync() => EnsureRestoredAsync();

        private async Task<bool> RestoreFromStorageAsync()
        {
            try
            {
                var isLoggedIn = await _storage.GetAsync<bool>("s_login");
                if (!isLoggedIn.Success || !isLoggedIn.Value) return false;

                var uid = await _storage.GetAsync<int>("s_uid");
                var uname = await _storage.GetAsync<string>("s_uname");
                var fname = await _storage.GetAsync<string>("s_fname");
                var rid = await _storage.GetAsync<int>("s_rid");
                var rname = await _storage.GetAsync<string>("s_rname");
                var eid = await _storage.GetAsync<int>("s_eid");
                var mcp = await _storage.GetAsync<bool>("s_mcp");
var pex = await _storage.GetAsync<bool>("s_pex");
var rcode = await _storage.GetAsync<string>("s_rcode");

                UserID = uid.Value;
                Username = uname.Value ?? "";
                FullName = fname.Value ?? "";
                RoleID = rid.Value;
                RoleName = rname.Value ?? "";
                EmployeeID = eid.Value > 0 ? eid.Value : null;
                MustChangePassword = mcp.Value;
PasswordExpired = pex.Value;
RoleCode = rcode.Value ?? "";
                IsLoggedIn = true;

                NotifyStateChanged();
                return true;
            }
            catch { return false; }
        }

        // === تسجيل الخروج ===
        public async Task LogoutAsync()
        {
            UserID = 0; Username = ""; FullName = "";
            RoleID = 0; RoleName = "";
            EmployeeID = null;
            MustChangePassword = false;
PasswordExpired = false;
RoleCode = "";
            IsLoggedIn = false;
            IsRestoreAttempted = true;
            IPAddress = ""; MachineName = "";

            await _storage.DeleteAsync("s_uid");
            await _storage.DeleteAsync("s_uname");
            await _storage.DeleteAsync("s_fname");
            await _storage.DeleteAsync("s_rid");
            await _storage.DeleteAsync("s_rname");
            await _storage.DeleteAsync("s_eid");
            await _storage.DeleteAsync("s_mcp");
await _storage.DeleteAsync("s_pex");
await _storage.DeleteAsync("s_rcode");
            await _storage.DeleteAsync("s_login");

            NotifyStateChanged();
        }

        public void Logout()
        {
            UserID = 0; Username = ""; FullName = "";
            RoleID = 0; RoleName = "";
            EmployeeID = null;
            MustChangePassword = false;
PasswordExpired = false;
RoleCode = "";
            IsLoggedIn = false;
            IsRestoreAttempted = true;
            IPAddress = ""; MachineName = "";
            NotifyStateChanged();
        }
        /// <summary>
/// يُستدعى بعد تغيير كلمة المرور بنجاح
/// </summary>
public async Task ClearPasswordChangeRequirementAsync()
{
    MustChangePassword = false;
    PasswordExpired = false;

    await _storage.SetAsync("s_mcp", false);
    await _storage.SetAsync("s_pex", false);

    NotifyStateChanged();
}
        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}