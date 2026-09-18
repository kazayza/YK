namespace YKCoatings.Models
{
    public class ModulePermissionDto
    {
        public int ModuleID { get; set; }
        public int? ParentModuleID { get; set; }
        public string ModuleCode { get; set; } = "";
        public string ModuleNameAr { get; set; } = "";
        public string? IconName { get; set; }
        public byte ModuleType { get; set; }
        public int SortOrder { get; set; }

        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanPrint { get; set; }
        public bool CanExport { get; set; }
        public bool CanApprove { get; set; }
        public bool CanPost { get; set; }
        public bool CanCancel { get; set; }
    }
}