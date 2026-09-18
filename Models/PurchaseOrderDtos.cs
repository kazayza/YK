namespace YKCoatings.Models
{
    // ==========================================
    // قائمة أوامر الشراء
    // ==========================================
    public class PurchaseOrderListDto
    {
        public int PurchaseOrderID { get; set; }
        public string? PONumber { get; set; }
        public DateTime PODate { get; set; }
        public int POStatus { get; set; }
        public int POSource { get; set; }
        public string? POSourceName { get; set; }

        // المورد
        public int SupplierID { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierNameAr { get; set; }

        // طلب الشراء المرتبط
        public int? RequestID { get; set; }
        public string? LinkedPRNumber { get; set; }

        // المبالغ
        public decimal TotalAmount { get; set; }
        public decimal TaxAmount { get; set; }

        // التواريخ
        public DateTime? ExpectedDeliveryDate { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public DateTime? SentDate { get; set; }

        // إجماليات الأصناف
        public int ItemCount { get; set; }
        public decimal TotalOrderedQty { get; set; }
        public decimal TotalReceivedQty { get; set; }
        public decimal TotalRemainingQty { get; set; }

        // تنبيهات
        public bool IsOverdue { get; set; }
        public int DaysOverdue { get; set; }

        // بيانات إضافية
        public string? WarehouseNameAr { get; set; }
        public string? PaymentTermName { get; set; }
        public string? CurrencyCode { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    // ==========================================
    // رأس أمر الشراء (Header)
    // ==========================================
    public class PurchaseOrderHeaderDto
    {
        public int PurchaseOrderID { get; set; }
        public string? PONumber { get; set; }
        public DateTime PODate { get; set; }
        public int POStatus { get; set; }
        public int POSource { get; set; }

        // المورد
        public int SupplierID { get; set; }
        public string? SupplierNameAr { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierPhone { get; set; }
        public decimal SupplierCurrentBalance { get; set; }
        public decimal SupplierCreditLimit { get; set; }

        // طلب الشراء المرتبط
        public int? RequestID { get; set; }
        public string? LinkedPRNumber { get; set; }

        // بيانات الأمر
        public DateTime? ExpectedDeliveryDate { get; set; }
        public int? PaymentTermID { get; set; }
        public string? PaymentTermName { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CurrencySymbol { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public int? WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }

        // المبالغ
        public decimal SubTotal { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal OtherCosts { get; set; }
        public decimal TotalAmount { get; set; }

        // Workflow
        public int? SubmittedBy { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? ApprovedByName { get; set; }
        public int? RejectedBy { get; set; }
        public DateTime? RejectedDate { get; set; }
        public string? RejectionReason { get; set; }
        public int? SentBy { get; set; }
        public DateTime? SentDate { get; set; }
        public string? SentMethod { get; set; }

        // ملاحظات
        public string? Notes { get; set; }
        public string? InternalNotes { get; set; }

        // Audit
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    // ==========================================
    // تفاصيل أمر الشراء (للعرض)
    // ==========================================
    public class PurchaseOrderDetailDto
    {
        public int PODetailID { get; set; }
        public int PurchaseOrderID { get; set; }
        public int LineNumber { get; set; }

        // الصنف
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        // الوحدة
        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        // الكميات
        public decimal OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal RemainingQty { get; set; }

        // بيانات PR المرتبط
        public int? RequestDetailID { get; set; }
        public decimal? PRApprovedQty { get; set; }
        public decimal? PRRemainingToOrder { get; set; }

        // الأسعار
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }
        public decimal LineTotalWithTax { get; set; }

        // بيانات إضافية
        public DateTime? ExpectedDate { get; set; }
        public int LineStatus { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // تفاصيل أمر الشراء (للإدخال/التعديل)
    // ==========================================
    public class PurchaseOrderDetailEditDto
    {
        public int PODetailID { get; set; }
        public int PurchaseOrderID { get; set; }
        public int LineNumber { get; set; }

        // الصنف
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        // الوحدة
        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        // الكميات
        public decimal OrderedQty { get; set; }

        // بيانات PR المرتبط
        public int? RequestDetailID { get; set; }
        public decimal? PRApprovedQty { get; set; }
        public decimal? PRRemainingToOrder { get; set; }

        // الأسعار
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal TaxRate { get; set; } = 14;

        // محسوبة على الـ Client
        public decimal DiscountAmount =>
            UnitPrice * OrderedQty * DiscountPercent / 100;

        public decimal LineTotal =>
            UnitPrice * OrderedQty * (1 - DiscountPercent / 100);

        public decimal TaxAmount =>
            LineTotal * TaxRate / 100;

        public decimal LineTotalWithTax =>
            LineTotal * (1 + TaxRate / 100);

        // بيانات إضافية
        public DateTime? ExpectedDate { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // سطور PR المتاحة لتحميلها في PO
    // ==========================================
    public class PRLineForPODto
    {
        public int RequestDetailID { get; set; }
        public int RequestID { get; set; }
        public string? RequestNumber { get; set; }
        public int LineNumber { get; set; }

        // الصنف
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        // الوحدة
        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        // الكميات
        public decimal RequestedQty { get; set; }
        public decimal? ApprovedQty { get; set; }
        public decimal TotalOrderedQty { get; set; }
        public decimal RemainingToOrder { get; set; }

        // السعر التقديري من PR
        public decimal EstimatedPrice { get; set; }

        // الرصيد الحالي في المخزن
        public decimal CurrentStock { get; set; }

        // الغرض
        public string? Purpose { get; set; }

        // هل تم اختيارها لإضافتها في PO؟
        public bool IsSelected { get; set; } = true;

        // السعر الفعلي (بعد اختيار المورد)
        public decimal ActualUnitPrice { get; set; }
    }

    // ==========================================
    // بيانات المورد السريعة (عند الاختيار)
    // ==========================================
    public class SupplierQuickInfoDto
    {
        public int SupplierID { get; set; }
        public string? SupplierNameAr { get; set; }
        public string? SupplierCode { get; set; }
        public string? Phone1 { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? City { get; set; }
        public int? PaymentTermID { get; set; }
        public string? PaymentTermName { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public decimal CreditLimit { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal AvailableCredit => CreditLimit - CurrentBalance;
        public int Rating { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // سعر الصنف من المورد
    // ==========================================
    public class SupplierItemPriceDto
    {
        public int ItemID { get; set; }
        public int SupplierID { get; set; }
        public decimal UnitPrice { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal MinOrderQty { get; set; }
        public int LeadTimeDays { get; set; }
        public DateTime? LastPurchaseDate { get; set; }
        public decimal? LastPurchasePrice { get; set; }

        // السعر المقترح (الأفضل المتاح)
        public decimal SuggestedPrice { get; set; }
        public string? PriceSource { get; set; }
        // "SupplierPrice" / "LastPurchase" / "AverageCost" / "StandardCost"
    }

    // ==========================================
    // إجماليات أمر الشراء (للحساب)
    // ==========================================
    public class POTotalsDto
    {
        public decimal SubTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxableAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal OtherCosts { get; set; }
        public decimal TotalAmount { get; set; }
    }

    // ==========================================
    // DTO للطلبات المعتمدة (للاختيار في PO)
    // ==========================================
    public class ApprovedPRForPODto
    {
        public int RequestID { get; set; }
        public string? RequestNumber { get; set; }
        public DateTime RequestDate { get; set; }
        public DateTime? RequiredDate { get; set; }
        public string? RequestedByName { get; set; }
        public string? DepartmentName { get; set; }
        public int TotalItems { get; set; }
        public int RemainingItems { get; set; }
        public string? Notes { get; set; }
    }
}