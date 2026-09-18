namespace YKCoatings.Models
{
    public class LeaveTypeListDto
    {
        public int LeaveTypeID { get; set; }
        public string? LeaveTypeCode { get; set; }
        public string? LeaveTypeNameAr { get; set; }
        public string? LeaveTypeNameEn { get; set; }
        public bool IsPaid { get; set; }
        public int MaxDaysPerYear { get; set; }
        public bool RequiresApproval { get; set; }
        public bool DeductFromBalance { get; set; }
        public bool AllowNegativeBalance { get; set; }
        public string? Color { get; set; }
        public bool IsActive { get; set; }
    }

    public class LeaveTypeEditDto
    {
        public int LeaveTypeID { get; set; }
        public string? LeaveTypeCode { get; set; }
        public string? LeaveTypeNameAr { get; set; }
        public string? LeaveTypeNameEn { get; set; }
        public bool IsPaid { get; set; } = true;
        public int MaxDaysPerYear { get; set; }
        public bool RequiresApproval { get; set; } = true;
        public bool DeductFromBalance { get; set; } = true;
        public bool AllowNegativeBalance { get; set; }
        public string? Color { get; set; } = "#1d4ed8";
        public bool IsActive { get; set; } = true;
    }

    // ==========================================
    // العطلات الرسمية
    // ==========================================
    public class PublicHolidayListDto
    {
        public int HolidayID { get; set; }
        public DateTime HolidayDate { get; set; }
        public string? HolidayNameAr { get; set; }
        public string? HolidayNameEn { get; set; }
        public bool IsRecurring { get; set; }
        public string? Notes { get; set; }
    }

    public class PublicHolidayEditDto
    {
        public int HolidayID { get; set; }
        public DateTime HolidayDate { get; set; }
        public string? HolidayNameAr { get; set; }
        public string? HolidayNameEn { get; set; }
        public int? FiscalYearID { get; set; }
        public bool IsRecurring { get; set; }
        public string? Notes { get; set; }
    }
}