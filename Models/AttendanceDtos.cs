namespace YKCoatings.Models
{
    // ==========================================
    // سجلات الحضور
    // ==========================================
       public class AttendanceListDto
    {
        public int AttendanceID { get; set; }
        public int EmployeeID { get; set; }
        public string? EmployeeCode { get; set; }
        public string? EmployeeName { get; set; }
        public string? DepartmentName { get; set; }
        public DateTime AttendanceDate { get; set; }
        public int AttendanceStatus { get; set; }
        public string? StatusName { get; set; }
         public string? ScheduledIn { get; set; }
        public string? ScheduledOut { get; set; }
        public string? ActualIn { get; set; }
        public string? ActualOut { get; set; }
        public decimal? WorkedHours { get; set; }
        public decimal? OvertimeHours { get; set; }
        public int? LateMinutes { get; set; }
        public int? EarlyLeaveMinutes { get; set; }
        public string? Notes { get; set; }
    }

       public class AttendanceEditDto
    {
        public int AttendanceID { get; set; }
        public int EmployeeID { get; set; }
        public DateTime AttendanceDate { get; set; }
        public int AttendanceStatus { get; set; } = 1;
        public TimeSpan? ScheduledIn { get; set; }
        public TimeSpan? ScheduledOut { get; set; }
        public TimeSpan? ActualIn { get; set; }
        public TimeSpan? ActualOut { get; set; }
        public decimal OvertimeHours { get; set; }
        public int? LeaveRequestID { get; set; }
        public string? Notes { get; set; }
    }

    public class AttendanceFilterDto
    {
        public string? SearchText { get; set; }
        public int? DepartmentID { get; set; }
        public int? AttendanceStatus { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class AttendancePagedResult
    {
        public List<AttendanceListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
    }

    // ==========================================
    // إحصائيات الحضور
    // ==========================================
    public class AttendanceStatsDto
    {
        public int TotalRecords { get; set; }
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public int LeaveCount { get; set; }
        public int LateCount { get; set; }
        public int MissionCount { get; set; }
        public decimal TotalOvertimeHours { get; set; }
        public int TotalLateMinutes { get; set; }
    }
       public class FingerprintRecord
{
    public int EmployeeID { get; set; }
    public DateTime AttendanceDate { get; set; }
    public TimeSpan? ActualIn { get; set; }
    public TimeSpan? ActualOut { get; set; }
    public TimeSpan? ScheduledIn { get; set; }
    public TimeSpan? ScheduledOut { get; set; }
    public int AttendanceStatus { get; set; }
    public decimal OvertimeHours { get; set; }
    public string? Notes { get; set; }
}
        public class LeaveBalanceViewDto
{
    public int BalanceID { get; set; }
    public int EmployeeID { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }
    public string? DepartmentName { get; set; }
    public int LeaveTypeID { get; set; }
    public string? LeaveTypeName { get; set; }
    public int FiscalYearID { get; set; }
    public string? YearName { get; set; }
    public decimal EntitledDays { get; set; }
    public decimal CarriedForward { get; set; }
    public decimal AdditionalDays { get; set; }
    public decimal TotalEntitled { get; set; }
    public decimal UsedDays { get; set; }
    public decimal PendingDays { get; set; }
    public decimal RemainingDays { get; set; }
}
}