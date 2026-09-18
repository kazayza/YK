namespace YKCoatings.Models
{
    // ==========================================
    // قائمة المسميات الوظيفية
    // ==========================================
    public class JobTitleListDto
    {
        public int JobTitleID { get; set; }
        public string? JobTitleCode { get; set; }
        public string? JobTitleNameAr { get; set; }
        public string? JobTitleNameEn { get; set; }
        public int JobLevel { get; set; }
        public string? JobLevelName { get; set; }
        public decimal MinSalary { get; set; }
        public decimal MaxSalary { get; set; }
        public int EmployeeCount { get; set; }
        public bool IsActive { get; set; }
    }

    // ==========================================
    // إضافة / تعديل مسمى وظيفي
    // ==========================================
    public class JobTitleEditDto
    {
        public int JobTitleID { get; set; }
        public string? JobTitleCode { get; set; }
        public string? JobTitleNameAr { get; set; }
        public string? JobTitleNameEn { get; set; }
        public int JobLevel { get; set; } = 1;
        public decimal MinSalary { get; set; }
        public decimal MaxSalary { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}