namespace YKCoatings.Models
{
    // ==========================================
    // قائمة الموظفين
    // ==========================================
    public class EmployeeListDto
    {
        public int EmployeeID { get; set; }
        public string? EmployeeCode { get; set; }
        public string? FullNameAr { get; set; }
        public string? FullNameEn { get; set; }
        public string? DepartmentNameAr { get; set; }
        public string? JobTitleNameAr { get; set; }
        public string? ManagerName { get; set; }
        public string? GenderName { get; set; }
        public string? Mobile { get; set; }
        public string? WorkEmail { get; set; }
        public DateTime? HireDate { get; set; }
        public int YearsOfService { get; set; }
        public string? ContractTypeName { get; set; }
        public string? StatusName { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal TotalSalary { get; set; }
        public bool IsActive { get; set; }
        public int TotalCount { get; set; }
    }

    // ==========================================
    // إضافة / تعديل موظف
    // ==========================================
    public class EmployeeEditDto
    {
        public int EmployeeID { get; set; }
        public string? EmployeeCode { get; set; }

        // البيانات الشخصية
        public string? FirstNameAr { get; set; }
        public string? SecondNameAr { get; set; }
        public string? ThirdNameAr { get; set; }
        public string? LastNameAr { get; set; }
        public string? FirstNameEn { get; set; }
        public string? LastNameEn { get; set; }
        public string? NationalID { get; set; }
        public DateTime? BirthDate { get; set; }
        public string? Gender { get; set; } = "M";
        public int MaritalStatus { get; set; } = 1;
        public int NumberOfChildren { get; set; }
        public string? Nationality { get; set; } = "مصري";
        public string? Religion { get; set; }

        // التواصل
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? PersonalEmail { get; set; }
        public string? WorkEmail { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? EmergencyContactRelation { get; set; }

        // البيانات الوظيفية
        public int? DepartmentID { get; set; }
        public int? JobTitleID { get; set; }
        public int? ManagerID { get; set; }
        public DateTime? HireDate { get; set; }
        public int ContractType { get; set; } = 1;
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public DateTime? ProbationEndDate { get; set; }
        public int EmployeeStatus { get; set; } = 1;
        public DateTime? TerminationDate { get; set; }
        public string? TerminationReason { get; set; }

        // البيانات المالية
        public decimal BasicSalary { get; set; }
        public decimal TransportAllowance { get; set; }
        public decimal HousingAllowance { get; set; }
        public decimal PhoneAllowance { get; set; }
        public decimal FoodAllowance { get; set; }
        public decimal OtherAllowances { get; set; }

        // التأمينات
        public string? InsuranceNumber { get; set; }
        public decimal InsuranceSalary { get; set; }
        public decimal InsurancePercEmployee { get; set; } = 11.00m;
        public decimal InsurancePercCompany { get; set; } = 18.75m;
        public bool IsInsured { get; set; } = true;
        public DateTime? InsuranceStartDate { get; set; }

        // البيانات البنكية
        public string? BankName { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? IBAN { get; set; }
        public int PaymentMethod { get; set; } = 1;

        // إضافية
        public decimal WorkingHoursPerDay { get; set; } = 8.00m;
        public int WeeklyDaysOff { get; set; } = 1;
        public decimal AnnualLeaveBalance { get; set; } = 21.00m;
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ==========================================
    // فلاتر الموظفين
    // ==========================================
    public class EmployeeFilterDto
    {
        public string? SearchText { get; set; }
        public int? DepartmentID { get; set; }
        public int? JobTitleID { get; set; }
        public int? ContractType { get; set; }
        public int? EmployeeStatus { get; set; }
        public bool? IsActive { get; set; } = true;
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ==========================================
    // نتيجة صفحة الموظفين
    // ==========================================
    public class EmployeePagedResult
    {
        public List<EmployeeListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
    }

    // ==========================================
    // إحصائيات الموظفين
    // ==========================================
    public class EmployeeStatsDto
    {
        public int Total { get; set; }
        public int TotalActive { get; set; }
        public int TotalInactive { get; set; }
        public int TotalPermanent { get; set; }
        public int TotalTemporary { get; set; }
        public int TotalOnProbation { get; set; }
        public decimal TotalSalaries { get; set; }
    }
}