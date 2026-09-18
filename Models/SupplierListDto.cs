// ==========================================
// الموردين — DTOs
// ==========================================

public class SupplierListDto
{
    public int SupplierID { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierNameAr { get; set; }
    public string? SupplierNameEn { get; set; }
    public int SupplierType { get; set; }
    public string? SupplierTypeName { get; set; }
    public string? Phone1 { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? PaymentTermName { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal AvailableCredit { get; set; }
    public int Rating { get; set; }
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
    public int ContactCount { get; set; }
    public int TotalPOs { get; set; }
    public int TotalInvoices { get; set; }
    public int UnpaidInvoices { get; set; }
}

public class SupplierEditDto
{
    public int SupplierID { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierNameAr { get; set; }
    public string? SupplierNameEn { get; set; }
    public int SupplierType { get; set; }
    public string? TaxNumber { get; set; }
    public string? CommercialRegister { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public int? PaymentTermID { get; set; }
    public int? CurrencyID { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? IBAN { get; set; }
    public int Rating { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class SupplierContactDto
{
    public int ContactID { get; set; }
    public int SupplierID { get; set; }
    public string? ContactName { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class SupplierItemDto
{
    public int SupplierItemID { get; set; }
    public int SupplierID { get; set; }
    public int ItemID { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemNameAr { get; set; }
    public string? SupplierItemCode { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal MinOrderQty { get; set; }
    public int LeadTimeDays { get; set; }
    public DateTime? LastPurchaseDate { get; set; }
    public decimal? LastPurchasePrice { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
}

public class SupplierItemEditDto
{
    public int SupplierItemID { get; set; }
    public int SupplierID { get; set; }
    public int ItemID { get; set; }
    public string? SupplierItemCode { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal MinOrderQty { get; set; }
    public int LeadTimeDays { get; set; }
    public bool IsPrimary { get; set; }
}

public class SupplierStatementDto
{
    public DateTime TransDate { get; set; }
    public string? TransType { get; set; }
    public string? DocNumber { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal Balance { get; set; }
    public string? DocType { get; set; }
    public int DocID { get; set; }
}