using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class PermissionService
    {
        private readonly AuthService _authService;
        private List<ModulePermissionDto> _permissions = new();

        public PermissionService(AuthService authService)
        {
            _authService = authService;
        }

        public bool IsLoaded { get; private set; }
        public IReadOnlyList<ModulePermissionDto> Permissions => _permissions;

        public async Task LoadPermissionsAsync(int userId)
        {
            _permissions = await _authService.GetUserModulesAsync(userId);
            IsLoaded = true;
        }

        public void Clear()
        {
            _permissions.Clear();
            IsLoaded = false;
        }

        public bool CanView(string moduleCode) =>
            _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanView);
        public bool CanAdd(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanAdd);

public bool CanEdit(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanEdit);

public bool CanDelete(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanDelete);

public bool CanPrint(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanPrint);

public bool CanExport(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanExport);

public bool CanApprove(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanApprove);

public bool CanPost(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanPost);

public bool CanCancel(string moduleCode) =>
    _permissions.Any(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase) && x.CanCancel);

        public ModulePermissionDto? GetModule(string moduleCode) =>
            _permissions.FirstOrDefault(x => x.ModuleCode.Equals(moduleCode, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<ModulePermissionDto> GetMainModules() =>
            _permissions.Where(x => x.ModuleType == 1 &&
                (x.CanView || _permissions.Any(c => c.ParentModuleID == x.ModuleID && c.CanView)))
            .OrderBy(x => x.SortOrder);

        public IEnumerable<ModulePermissionDto> GetChildModules(int parentModuleId) =>
            _permissions.Where(x => x.ParentModuleID == parentModuleId && x.CanView)
            .OrderBy(x => x.SortOrder);

        public IEnumerable<ModulePermissionDto> GetQuickAccessModules(int take = 8) =>
            _permissions.Where(x => x.ModuleType == 2 && x.CanView)
            .OrderBy(x => x.SortOrder).Take(take);
    }
}