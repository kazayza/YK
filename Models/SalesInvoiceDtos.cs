namespace YKCoatings.Models
{
    // ==========================================
    // قائمة فواتير المبيعات
    // ==========================================
    public class SalesInvoiceListDto
    {
        public int InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public int InvoiceStatus { get; set; }
        public int InvoiceSource { get; set; } // 1=Delivery 2=Order 3=Direct
        public string? InvoiceSourceName { get; set; }
        public int PaymentStatus { get; set; }
        public string? PaymentStatusName { get; set; }

        public int CustomerID { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerNameAr { get; set; }

        public int? SalesOrderID { get; set; }
        public string? LinkedSONumber { get; set; }
        public int? DeliveryID { get; set; }
        public string? LinkedDeliveryNumber { get; set; }

        public bool IsTaxable { get; set; } = true;
        public decimal TaxRate { get; set; }

        public decimal SubTotal { get; set; }
        public decimal DiscountAmount { get; set; }
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
    // رأس فاتورة المبيعات
    // ==========================================
    public class SalesInvoiceHeaderDto
    {
        public int InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; } = DateTime.Today;
        public int InvoiceStatus { get; set; } = 1;
        public int InvoiceSource { get; set; } = 3; // Direct default
        public int PaymentStatus { get; set; } = 1;

        // العميل
        public int CustomerID { get; set; }
        public string? CustomerNameAr { get; set; }
        public string? CustomerCode { get; set; }

        // المراجع
        public int? SalesOrderID { get; set; }
        public string? LinkedSONumber { get; set; }
        public int? DeliveryID { get; set; }
        public string? LinkedDeliveryNumber { get; set; }

        // الضريبة — الميزة المطلوبة: مع/بدون ضريبة اختياري
        public bool IsTaxable { get; set; } = true;
        public decimal TaxRate { get; set; } = 14; // نسبة افتراضية 14%

        // الدفع
        public int? PaymentTermID { get; set; }
        public string? PaymentTermName { get; set; }
        public DateTime? DueDate { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CurrencySymbol { get; set; }
        public decimal ExchangeRate { get; set; } = 1;

        // المخزن (للفاتورة المباشرة)
        public int? WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }

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
    // تفاصيل فاتورة المبيعات (عرض)
    // ==========================================
    public class SalesInvoiceDetailDto
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

        public int? DeliveryDetailID { get; set; }
        public int? SalesOrderDetailID { get; set; }
        public string? Notes { get; set; }

        public decimal AvailableQty { get; set; }
    }

    // ==========================================
    // تفاصيل فاتورة المبيعات (إدخال)
    // ==========================================
    public class SalesInvoiceDetailEditDto
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

        // للتحكم في الضريبة على مستوى السطر
        public bool IsTaxable { get; set; } = true;

        // محسوبة
        public decimal DiscountAmount => UnitPrice * Quantity * DiscountPercent / 100;
        public decimal LineTotal => UnitPrice * Quantity * (1 - DiscountPercent / 100);
        public decimal TaxAmount => IsTaxable ? LineTotal * TaxRate / 100 : 0;
        public decimal LineTotalWithTax => IsTaxable ? LineTotal * (1 + TaxRate / 100) : LineTotal;

        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public int? DeliveryDetailID { get; set; }
        public int? SalesOrderDetailID { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // سطور Delivery المتاحة لتحميلها في الفاتورة
    // ==========================================
    public class DeliveryLineForInvoiceDto
    {
        public int DeliveryDetailID { get; set; }
        public int DeliveryID { get; set; }
        public string? DeliveryNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public int? WarehouseID { get; set; }

        public decimal DeliveredQty { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }

        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public int? SalesOrderDetailID { get; set; }

        // للإدخال
        public bool IsSelected { get; set; } = true;
        public decimal InvoiceUnitPrice { get; set; }
        public decimal InvoiceTaxRate { get; set; } = 14;
    }

    // ==========================================
    // Delivery المتاحة للفاتورة
    // ==========================================
    public class DeliveryForInvoiceDto
    {
        public int DeliveryID { get; set; }
        public string? DeliveryNumber { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string? CustomerNameAr { get; set; }
        public int CustomerID { get; set; }
        public int? SalesOrderID { get; set; }
        public string? SONumber { get; set; }
        public int? WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public int TotalItems { get; set; }
        public decimal TotalAmount { get; set; }
    }

    // ==========================================
    // سطور SalesOrder المتاحة
    // ==========================================
    public class SalesOrderLineForInvoiceDto
    {
        public int SalesOrderDetailID { get; set; }
        public int SalesOrderID { get; set; }
        public string? SONumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public int? WarehouseID { get; set; }

        public decimal OrderedQty { get; set; }
        public decimal DeliveredQty { get; set; }
        public decimal RemainingQty { get; set; }
        public decimal UnitPrice { get; set; }

        public string? BatchNumber { get; set; }

        public bool IsSelected { get; set; } = true;
        public decimal InvoiceQty { get; set; }
        public decimal InvoiceUnitPrice { get; set; }
        public decimal InvoiceTaxRate { get; set; } = 14;
    }
}
