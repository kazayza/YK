namespace YKCoatings.Models

{
    // ==========================================
    // قائمة العملاء
    // ==========================================
    public class CustomerListDto
    {
        public int CustomerID { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerNameAr { get; set; }
        public string? CustomerNameEn { get; set; }
        public int CustomerType { get; set; }
        public string? CustomerGroupName { get; set; }
        public string? Phone1 { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? City { get; set; }
        public string? PaymentTermName { get; set; }
        public string? PriceListName { get; set; }
        public string? SalesRepName { get; set; }
        public string? CurrencyName { get; set; }
        public decimal CreditLimit { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal DiscountPercent { get; set; }
        public int Rating { get; set; }
        public bool IsActive { get; set; }
        public int TotalCount { get; set; }
    }

    // ==========================================
    // فلاتر العملاء
    // ==========================================
    public class CustomerFilterDto
    {
        public string? SearchText { get; set; }
        public int CustomerType { get; set; } = 0;
        public int CustomerGroupID { get; set; } = 0;
        public int SalesRepID { get; set; } = 0;
        public bool? IsActive { get; set; } = true;
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ==========================================
    // نتيجة Paging
    // ==========================================
    public class CustomerPagedResult
    {
        public List<CustomerListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }

        public int TotalPages =>
            PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
    }

    // ==========================================
    // إضافة / تعديل عميل
    // ==========================================
    public class CustomerEditDto
    {
        public int CustomerID { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerNameAr { get; set; }
        public string? CustomerNameEn { get; set; }
        public int CustomerType { get; set; }
        public int? CustomerGroupID { get; set; }
        public string? TaxNumber { get; set; }
        public string? CommercialRegister { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }
        public string? Phone1 { get; set; }
        public string? Phone2 { get; set; }
        public string? Mobile { get; set; }
        public string? Fax { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public int? PaymentTermID { get; set; }
        public int? CurrencyID { get; set; }
        public decimal CreditLimit { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public int? PriceListID { get; set; }
        public decimal DiscountPercent { get; set; }
        public int? SalesRepID { get; set; }
        public string? BankName { get; set; }
        public string? BankAccountNumber { get; set; }
        public int Rating { get; set; } = 3;
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ==========================================
    // جهات اتصال العميل
    // ==========================================
    public class CustomerContactDto
    {
        public int ContactID { get; set; }
        public int CustomerID { get; set; }
        public string? ContactName { get; set; }
        public string? JobTitle { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public bool IsPrimary { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public DateTime? CreatedDate { get; set; }
    }

    // ==========================================
    // إحصائيات العملاء
    // ==========================================
    public class CustomerStatsDto
    {
        public int TotalActive { get; set; }
        public int TotalInactive { get; set; }
        public int TotalWholesale { get; set; }
        public int TotalRetail { get; set; }
        public int TotalOnline { get; set; }
        public int TotalInstitutional { get; set; }
        public decimal TotalBalance { get; set; }
        public int Total => TotalActive + TotalInactive;
    }
}