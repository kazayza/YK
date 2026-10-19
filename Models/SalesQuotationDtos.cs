namespace YKCoatings.Models
{
    public sealed class SalesQuotationListDto
    {
        public int QuotationID { get; set; }
        public string? QuotationNumber { get; set; }
        public DateTime QuotationDate { get; set; }
        public DateTime? ValidUntilDate { get; set; }
        public int QuotationStatus { get; set; }
        public int CustomerID { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerNameAr { get; set; }
        public decimal SubTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public DateTime? CreatedDate { get; set; }
    }

    public sealed class SalesQuotationHeaderDto
    {
        public int QuotationID { get; set; }
        public string? QuotationNumber { get; set; }
        public DateTime QuotationDate { get; set; } = DateTime.Today;
        public DateTime? ValidUntilDate { get; set; }
        public int QuotationStatus { get; set; } = 1;

        public int CustomerID { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerNameAr { get; set; }
        public int? SalesRepID { get; set; }

        public decimal SubTotal { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal TotalAmount { get; set; }

        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public int? SubmittedBy { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public int? RejectedBy { get; set; }
        public DateTime? RejectedDate { get; set; }
        public string? RejectionReason { get; set; }
        public int? SentBy { get; set; }
        public DateTime? SentDate { get; set; }
    }

    public sealed class SalesQuotationCustomerContactDto
    {
        public string? Phone { get; set; }
        public string? Email { get; set; }
    }

    public sealed class SalesQuotationDetailDto
    {
        public int QuotationDetailID { get; set; }
        public int QuotationID { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        public decimal Quantity { get; set; } = 1m;
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal TaxRate { get; set; } = 14m;
        public string? Notes { get; set; }

        public decimal DiscountAmount => RoundMoney(Quantity * UnitPrice * DiscountPercent / 100m);
        public decimal LineTotal => RoundMoney(Quantity * UnitPrice - DiscountAmount);
        public decimal TaxAmount => RoundMoney(LineTotal * TaxRate / 100m);
        public decimal LineTotalWithTax => RoundMoney(LineTotal + TaxAmount);

        private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
