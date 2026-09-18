namespace YKCoatings.Models
{
    public class PurchaseReturnListDto
    {
        public int ReturnID { get; set; }
        public string? ReturnNumber { get; set; }
        public DateTime ReturnDate { get; set; }
        public int ReturnStatus { get; set; }
        public int ReturnSource { get; set; }
        public string? ReturnSourceName { get; set; }
        public int ReturnType { get; set; }
        public string? ReturnTypeName { get; set; }
        public string? ReturnReason { get; set; }

        public int SupplierID { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierNameAr { get; set; }

        public int? InvoiceID { get; set; }
        public string? LinkedInvoiceNumber { get; set; }

        public string? WarehouseNameAr { get; set; }

        public decimal SubTotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public int ItemCount { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class PurchaseReturnHeaderDto
    {
        public int ReturnID { get; set; }
        public string? ReturnNumber { get; set; }
        public DateTime ReturnDate { get; set; }
        public int ReturnStatus { get; set; }
        public int ReturnSource { get; set; }
        public int ReturnType { get; set; }
        public string? ReturnReason { get; set; }

        public int SupplierID { get; set; }
        public string? SupplierNameAr { get; set; }
        public string? SupplierCode { get; set; }

        public int? InvoiceID { get; set; }
        public string? LinkedInvoiceNumber { get; set; }

        public int WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }

        public decimal SubTotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public int? SubmittedBy { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? ApprovedByName { get; set; }
        public int? RejectedBy { get; set; }
        public DateTime? RejectedDate { get; set; }
        public string? RejectionReason { get; set; }

        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    public class PurchaseReturnDetailDto
{
    public int ReturnDetailID { get; set; }
    public int ReturnID { get; set; }
    public int LineNumber { get; set; }

    public int ItemID { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemNameAr { get; set; }
    public int UnitID { get; set; }
    public string? UnitName { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotalWithTax { get; set; }

    // بدون DiscountPercent
    public string? BatchNumber { get; set; }
    public string? ReturnReason { get; set; }
    public int ItemCondition { get; set; }
    public int? InvoiceDetailID { get; set; }
    public string? Notes { get; set; }
}

   public class PurchaseReturnDetailEditDto
{
    public int ReturnDetailID { get; set; }
    public int ReturnID { get; set; }
    public int LineNumber { get; set; }

    public int ItemID { get; set; }
    public int UnitID { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; } = 14;

    // محسوبة بدون خصم
    public decimal LineTotal => UnitPrice * Quantity;
    public decimal TaxAmount => LineTotal * TaxRate / 100;
    public decimal LineTotalWithTax => LineTotal * (1 + TaxRate / 100);

    public string? BatchNumber { get; set; }
    public string? ReturnReason { get; set; }
    public int ItemCondition { get; set; } = 1;
    public int? InvoiceDetailID { get; set; }
    public string? Notes { get; set; }
}

    public class InvoiceLineForReturnDto
    {
        public int InvoiceDetailID { get; set; }
        public int InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        
        public decimal TaxRate { get; set; }
        public string? BatchNumber { get; set; }

        public bool IsSelected { get; set; } = true;
        public decimal ReturnQty { get; set; }
        public int ItemCondition { get; set; } = 1;
        public string? ReturnReason { get; set; }
    }

    public class InvoiceForReturnDto
    {
        public int InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public string? SupplierNameAr { get; set; }
        public int SupplierID { get; set; }
        public int? WarehouseID { get; set; }
        public decimal TotalAmount { get; set; }
        public int TotalItems { get; set; }
    }
}