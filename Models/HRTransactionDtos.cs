namespace YKCoatings.Models
{
    // ==========================================
    // السلف
    // ==========================================
    public class LoanListDto
    {
        public int LoanID { get; set; }
        public string? LoanNumber { get; set; }
        public int EmployeeID { get; set; }
        public string? EmployeeCode { get; set; }
        public string? EmployeeName { get; set; }
        public string? DepartmentName { get; set; }
        public int LoanType { get; set; }
        public string? LoanTypeName { get; set; }
        public decimal LoanAmount { get; set; }
        public int NumberOfInstallments { get; set; }
        public decimal InstallmentAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public DateTime LoanDate { get; set; }
        public DateTime? StartDeductionDate { get; set; }
        public int LoanStatus { get; set; }
        public string? LoanStatusName { get; set; }
        public string? LoanReason { get; set; }
    }

    public class LoanEditDto
    {
        public int LoanID { get; set; }
        public string? LoanNumber { get; set; }
        public int EmployeeID { get; set; }
        public DateTime LoanDate { get; set; }
        public int LoanType { get; set; } = 1;
        public decimal LoanAmount { get; set; }
        public int NumberOfInstallments { get; set; } = 1;
        public decimal InstallmentAmount { get; set; }
        public DateTime? StartDeductionDate { get; set; }
        public string? LoanReason { get; set; }
        public int LoanStatus { get; set; } = 1;
        public string? Notes { get; set; }
    }

    public class LoanFilterDto
    {
        public string? SearchText { get; set; }
        public int? DepartmentID { get; set; }
        public int? LoanStatus { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class LoanPagedResult
    {
        public List<LoanListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
    }

    // ==========================================
    // الجزاءات والمكافآت
    // ==========================================
        public class PenaltyListDto
    {
        public int RecordID { get; set; }
        public int EmployeeID { get; set; }
        public string? EmployeeCode { get; set; }
        public string? EmployeeName { get; set; }
        public string? DepartmentName { get; set; }
        public int RecordType { get; set; }
        public string? RecordTypeName { get; set; }
        public DateTime RecordDate { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public int? DeductionType { get; set; }
        public decimal Amount { get; set; }
        public decimal Percentage { get; set; }
        public decimal Days { get; set; }
        public DateTime? EffectiveMonth { get; set; }
        public int RecordStatus { get; set; }
        public string? RecordStatusName { get; set; }
        public string? IssuedByName { get; set; }
    }

    public class PenaltyEditDto
    {
        public int RecordID { get; set; }
        public int EmployeeID { get; set; }
        public int RecordType { get; set; } = 1;
        public DateTime RecordDate { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public int? DeductionType { get; set; } = 1;
        public decimal Amount { get; set; }
        public decimal Percentage { get; set; }
        public decimal Days { get; set; }
        public DateTime? EffectiveMonth { get; set; }
        public int RecordStatus { get; set; } = 1;
        public int? IssuedBy { get; set; }
        public string? Notes { get; set; }
    }

    public class PenaltyFilterDto
    {
        public string? SearchText { get; set; }
        public int? DepartmentID { get; set; }
        public int? RecordType { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class PenaltyPagedResult
    {
        public List<PenaltyListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
    }

    // ==========================================
    // مسير المرتبات
    // ==========================================
    public class PayrollRunListDto
{
    public int PayrollID { get; set; }
    public string? PayrollNumber { get; set; }
    public DateTime PayrollMonth { get; set; }
    public int PeriodID { get; set; }
    public string? PeriodName { get; set; }
    public string? PayrollTitle { get; set; }
    public int EmployeeCount { get; set; }
    public decimal TotalBasicSalary { get; set; }
    public decimal TotalAllowances { get; set; }
    public decimal TotalGrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNetSalary { get; set; }
    public int PayrollStatus { get; set; }
    public string? PayrollStatusName { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? CalculatedByName { get; set; }
}

        public class PayrollRunEditDto
{
    public int PayrollID { get; set; }
    public string? PayrollNumber { get; set; }
    public DateTime PayrollMonth { get; set; }
    public int PeriodID { get; set; }
    public string? PeriodName { get; set; }
    public string? PayrollTitle { get; set; }
    public int EmployeeCount { get; set; }
    public decimal TotalGrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNetSalary { get; set; }
    public int PayrollStatus { get; set; } = 1;

    public string? PayrollStatusName => PayrollStatus switch
    {
        1 => "مسودة",
        2 => "محسوب",
        3 => "معتمد",
        4 => "مدفوع",
        5 => "ملغي",
        _ => "—"
    };
}
        // ==========================================
    // تفاصيل مسير المرتبات
    // ==========================================
    public class PayrollDetailDto
{
    public int PayrollDetailID { get; set; }
    public int PayrollID { get; set; }
    public int EmployeeID { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }
    public string? DepartmentName { get; set; }
    public string? JobTitleName { get; set; }
    public decimal WorkingDays { get; set; }
    public decimal PresentDays { get; set; }
    public decimal AbsentDays { get; set; }
    public decimal LeaveDays { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal TransportAllowance { get; set; }
    public decimal HousingAllowance { get; set; }
    public decimal PhoneAllowance { get; set; }
    public decimal FoodAllowance { get; set; }
    public decimal OtherAllowances { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal Incentives { get; set; }
    public decimal Commissions { get; set; }
    public decimal Rewards { get; set; }
    public decimal OtherEarnings { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal InsuranceEmployee { get; set; }
    public decimal InsuranceCompany { get; set; }
    public decimal IncomeTax { get; set; }
    public decimal LoanDeduction { get; set; }
    public decimal PenaltyDeduction { get; set; }
    public decimal AbsenceDeduction { get; set; }
    public decimal LateDeduction { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSalary { get; set; }
    public int PaymentMethod { get; set; }
    public string? Notes { get; set; }

    public string? PaymentMethodName => PaymentMethod switch
    {
        1 => "تحويل بنكي",
        2 => "كاش",
        3 => "شيك",
        _ => "—"
    };

    public bool IsPaid { get; set; }
    public DateTime? PaidDate { get; set; }
}
    public class PayrollDetailEditDto
{
    public int PayrollDetailID { get; set; }
    public int PayrollID { get; set; }
    public int EmployeeID { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }

    public decimal Incentives { get; set; }
    public decimal Commissions { get; set; }
    public decimal Rewards { get; set; }
    public decimal OtherEarnings { get; set; }
    public decimal OtherDeductions { get; set; }
    public string? Notes { get; set; }
}

    public class PayrollFilterDto
    {
        public int PayrollStatus { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class PayrollPagedResult
    {
        public List<PayrollRunListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
    }

    
}