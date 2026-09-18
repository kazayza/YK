namespace YKCoatings.Models
{
    // ==========================================
    // قائمة الأقسام
    // ==========================================
    public class DepartmentListDto
    {
        public int DepartmentID { get; set; }
        public string? DepartmentCode { get; set; }
        public string? DepartmentNameAr { get; set; }
        public string? DepartmentNameEn { get; set; }
        public string? ParentDepartmentName { get; set; }
        public string? ManagerName { get; set; }
        public string? Phone { get; set; }
        public int EmployeeCount { get; set; }
        public bool IsActive { get; set; }
    }

    // ==========================================
    // إضافة / تعديل قسم
    // ==========================================
    public class DepartmentEditDto
    {
        public int DepartmentID { get; set; }
        public string? DepartmentCode { get; set; }
        public string? DepartmentNameAr { get; set; }
        public string? DepartmentNameEn { get; set; }
        public int? ParentDepartmentID { get; set; }
        public int? ManagerEmployeeID { get; set; }
        public string? Phone { get; set; }
        public string? CostCenterCode { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}