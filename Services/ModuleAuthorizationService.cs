using YKCoatings.Models;

namespace YKCoatings.Services
{
    /// <summary>
    /// Checks effective permissions at the service boundary. The user id supplied by a
    /// component is never trusted unless it matches the active circuit session, and the
    /// permission is re-read from AuthService (vw_UserEffectivePermissions) for each call.
    /// </summary>
    public sealed class ModuleAuthorizationService
    {
        private readonly AuthService _authService;
        private readonly UserSessionService _userSession;

        public ModuleAuthorizationService(AuthService authService, UserSessionService userSession)
        {
            _authService = authService;
            _userSession = userSession;
        }

        public Task RequireCurrentUserPermissionAsync(
            string moduleCode,
            Func<ModulePermissionDto, bool> predicate,
            string deniedMessage)
        {
            if (!_userSession.IsLoggedIn || _userSession.UserID <= 0)
                throw new UnauthorizedAccessException("جلسة المستخدم غير صالحة؛ سجّل الدخول مرة أخرى.");

            return RequireUserPermissionAsync(_userSession.UserID, moduleCode, predicate, deniedMessage);
        }

        public Task RequireAnyCurrentUserPermissionAsync(
            string deniedMessage,
            params (string ModuleCode, Func<ModulePermissionDto, bool> Predicate)[] requirements)
        {
            if (!_userSession.IsLoggedIn || _userSession.UserID <= 0)
                throw new UnauthorizedAccessException("جلسة المستخدم غير صالحة؛ سجّل الدخول مرة أخرى.");

            return RequireAnyUserPermissionAsync(_userSession.UserID, deniedMessage, requirements);
        }

        /// <summary>
        /// Read-only access to the cash-box statement. The ADMIN role is recognized for this
        /// statement only; all other module and write permissions remain explicitly checked.
        /// </summary>
        public async Task RequireCurrentCashBoxStatementViewAsync(string deniedMessage)
        {
            if (!_userSession.IsLoggedIn || _userSession.UserID <= 0)
                throw new UnauthorizedAccessException("جلسة المستخدم غير صالحة؛ سجّل الدخول مرة أخرى.");

            // أعد التحقق من الدور الحالي في قاعدة البيانات؛ لا تعتمد الخدمة على قيمة الجلسة وحدها.
            if (await _authService.IsActiveAdministratorAsync(_userSession.UserID))
                return;

            await RequireUserPermissionAsync(
                _userSession.UserID,
                "SCR_CASHSTAT",
                permission => permission.CanView,
                deniedMessage);
        }

        public async Task RequireAnyUserPermissionAsync(
            int userId,
            string deniedMessage,
            params (string ModuleCode, Func<ModulePermissionDto, bool> Predicate)[] requirements)
        {
            if (userId <= 0 || !_userSession.IsLoggedIn || _userSession.UserID != userId)
                throw new UnauthorizedAccessException("جلسة المستخدم لا تطابق منفذ العملية؛ سجّل الدخول مرة أخرى.");

            if (requirements is null || requirements.Length == 0)
                throw new ArgumentException("يلزم تحديد عملية صلاحية واحدة على الأقل.", nameof(requirements));

            if (requirements.Any(r => string.IsNullOrWhiteSpace(r.ModuleCode) || r.Predicate is null))
                throw new ArgumentException("رمز الوحدة ودالة التحقق مطلوبان لكل صلاحية.", nameof(requirements));

            var permissions = await _authService.GetUserModulesAsync(userId);
            var allowed = requirements.Any(requirement => permissions.Any(module =>
                module.ModuleCode.Equals(requirement.ModuleCode, StringComparison.OrdinalIgnoreCase)
                && requirement.Predicate(module)));

            if (!allowed)
                throw new UnauthorizedAccessException(deniedMessage);
        }

        public async Task RequireUserPermissionAsync(
            int userId,
            string moduleCode,
            Func<ModulePermissionDto, bool> predicate,
            string deniedMessage)
        {
            if (userId <= 0 || !_userSession.IsLoggedIn || _userSession.UserID != userId)
                throw new UnauthorizedAccessException("جلسة المستخدم لا تطابق منفذ العملية؛ سجّل الدخول مرة أخرى.");

            if (string.IsNullOrWhiteSpace(moduleCode))
                throw new ArgumentException("رمز الوحدة مطلوب.", nameof(moduleCode));

            var permissions = await _authService.GetUserModulesAsync(userId);
            var module = permissions.FirstOrDefault(x =>
                x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase));

            if (module is null || !predicate(module))
                throw new UnauthorizedAccessException(deniedMessage);
        }

        public async Task<bool> HasCurrentUserPermissionAsync(
            string moduleCode,
            Func<ModulePermissionDto, bool> predicate)
        {
            try
            {
                await RequireCurrentUserPermissionAsync(moduleCode, predicate, "");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
