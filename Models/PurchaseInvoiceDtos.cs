namespace YKCoatings.Models
{
    // ==========================================
    // قائمة فواتير المشتريات
    // ==========================================
    public class PurchaseInvoiceListDto
    {
        public int InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public int InvoiceStatus { get; set; }
        public int InvoiceSource { get; set; }
        public string? InvoiceSourceName { get; set; }
        public int PaymentStatus { get; set; }
        public string? PaymentStatusName { get; set; }

        public int SupplierID { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierNameAr { get; set; }

        public int? PurchaseOrderID { get; set; }
        public string? LinkedPONumber { get; set; }
        public int? GRNID { get; set; }
        public string? LinkedGRNNumber { get; set; }
        public string? SupplierInvoiceNo { get; set; }

        public decimal SubTotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingAmount { get; set; }

        public DateTime? DueDate { get; set; }
        public bool IsOverdue { get; set; }
        public int DaysOverdue { get; set; }

        public string? PaymentTermName { get; set; }
        public string? CurrencyCode { get; set; }
        public int ItemCount { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    // ==========================================
    // رأس فاتورة المشتريات
    // ==========================================
    public class PurchaseInvoiceHeaderDto
    {
        public int InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public int InvoiceStatus { get; set; }
        public int InvoiceSource { get; set; }
        public int PaymentStatus { get; set; }

        // المورد
        public int SupplierID { get; set; }
        public string? SupplierNameAr { get; set; }
        public string? SupplierCode { get; set; }

        // المراجع
        public int? PurchaseOrderID { get; set; }
        public string? LinkedPONumber { get; set; }
        public int? GRNID { get; set; }
        public string? LinkedGRNNumber { get; set; }

        // بيانات فاتورة المورد
        public string? SupplierInvoiceNo { get; set; }
        public DateTime? SupplierInvoiceDate { get; set; }

        // الدفع
        public int? PaymentTermID { get; set; }
        public string? PaymentTermName { get; set; }
        public DateTime? DueDate { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CurrencySymbol { get; set; }
        public decimal ExchangeRate { get; set; } = 1;

        // المبالغ
        public decimal SubTotal { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxableAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal OtherCosts { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingAmount => TotalAmount - PaidAmount;

        // Workflow
        public int? SubmittedBy { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? ApprovedByName { get; set; }
        public int? RejectedBy { get; set; }
        public DateTime? RejectedDate { get; set; }
        public string? RejectionReason { get; set; }

        // ملاحظات
        public string? Notes { get; set; }

        // Audit
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    // ==========================================
    // تفاصيل فاتورة المشتريات (عرض)
    // ==========================================
    public class PurchaseInvoiceDetailDto
    {
        public int InvoiceDetailID { get; set; }
        public int InvoiceID { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public int? WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }

        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal LineTotal { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotalWithTax { get; set; }

        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public int? GRNDetailID { get; set; }
        public int? PODetailID { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // تفاصيل فاتورة المشتريات (إدخال)
    // ==========================================
    public class PurchaseInvoiceDetailEditDto
    {
        public int InvoiceDetailID { get; set; }
        public int InvoiceID { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public int? WarehouseID { get; set; }

        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal TaxRate { get; set; } = 14;

        // محسوبة
        public decimal DiscountAmount => UnitPrice * Quantity * DiscountPercent / 100;
        public decimal LineTotal => UnitPrice * Quantity * (1 - DiscountPercent / 100);
        public decimal TaxAmount => LineTotal * TaxRate / 100;
        public decimal LineTotalWithTax => LineTotal * (1 + TaxRate / 100);

        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public int? GRNDetailID { get; set; }
        public int? PODetailID { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // سطور GRN المتاحة لتحميلها في الفاتورة
    // ==========================================
    public class GRNLineForInvoiceDto
    {
        public int GRNDetailID { get; set; }
        public int GRNID { get; set; }
        public string? GRNNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public int? WarehouseID { get; set; }

        public decimal ReceivedQty { get; set; }
        public decimal? AcceptedQty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineTotalCost { get; set; }

        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public int? PODetailID { get; set; }

        // للإدخال
        public bool IsSelected { get; set; } = true;
        public decimal InvoiceUnitPrice { get; set; }
        public decimal InvoiceTaxRate { get; set; } = 14;
    }

    // ==========================================
    // GRN المتاحة للفاتورة
    // ==========================================
    public class GRNForInvoiceDto
    {
        public int GRNID { get; set; }
        public string? GRNNumber { get; set; }
        public DateTime GRNDate { get; set; }
        public string? SupplierNameAr { get; set; }
        public int SupplierID { get; set; }
        public int? PurchaseOrderID { get; set; }
        public string? PONumber { get; set; }
        public int? WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public int TotalItems { get; set; }
        public decimal TotalCost { get; set; }
    }
}