namespace YKCoatings.Models
{
    public class LeaveRequestListDto
    {
        public int LeaveRequestID { get; set; }
        public string? RequestNumber { get; set; }
        public int EmployeeID { get; set; }
        public string? EmployeeCode { get; set; }
        public string? EmployeeName { get; set; }
        public string? DepartmentName { get; set; }
        public int LeaveTypeID { get; set; }
        public string? LeaveTypeName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal NumberOfDays { get; set; }
        public string? Reason { get; set; }
        public int RequestStatus { get; set; }
        public string? StatusName { get; set; }
        public string? ApprovedByManagerName { get; set; }
        public DateTime? ManagerApprovalDate { get; set; }
        public string? ApprovedByHRName { get; set; }
        public DateTime? HRApprovalDate { get; set; }
        public string? RejectionReason { get; set; }
    }

    public class LeaveRequestEditDto
    {
        public int LeaveRequestID { get; set; }
        public string? RequestNumber { get; set; }
        public int EmployeeID { get; set; }
        public int LeaveTypeID { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal NumberOfDays { get; set; }
        public string? Reason { get; set; }
        public int RequestStatus { get; set; } = 1;
        public string? RejectionReason { get; set; }
        public int? SubstituteEmployeeID { get; set; }
        public string? Notes { get; set; }
    }

    public class LeaveRequestFilterDto
    {
        public string? SearchText { get; set; }
        public int? DepartmentID { get; set; }
        public int? LeaveTypeID { get; set; }
        public int? RequestStatus { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class LeaveRequestPagedResult
    {
        public List<LeaveRequestListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
    }

    public class LeaveBalanceDto
    {
        public int EmployeeID { get; set; }
        public int LeaveTypeID { get; set; }
        public string? LeaveTypeName { get; set; }
        public decimal EntitledDays { get; set; }
        public decimal CarriedForward { get; set; }
        public decimal AdditionalDays { get; set; }
        public decimal TotalEntitled { get; set; }
        public decimal UsedDays { get; set; }
        public decimal PendingDays { get; set; }
        public decimal RemainingDays { get; set; }
    }
    
public class FiscalYearLookupDto
{
    public int FiscalYearID { get; set; }
    public string? YearName { get; set; }
    public bool IsCurrent { get; set; }
}
    public class LeaveRequestApprovalDto
{
    public int LeaveRequestID { get; set; }
    public int RequestStatus { get; set; }
    public string? RejectionReason { get; set; }
    public string? ApprovedByManagerName { get; set; }
    public DateTime? ManagerApprovalDate { get; set; }
    public string? ApprovedByHRName { get; set; }
    public DateTime? HRApprovalDate { get; set; }
}
}