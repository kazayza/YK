namespace YKCoatings.Models
{
    // ==========================================
    // قائمة أوامر البيع - Gold Edition
    // الجدول موجود 28 عمود - لا جدول جديد
    // ==========================================
    public class SalesOrderListDto
    {
        public int SalesOrderID { get; set; }
        public string? OrderNumber { get; set; }
        public DateTime OrderDate { get; set; }
        public int OrderStatus { get; set; }
        public int OrderSource { get; set; } // 1=يدوي 2=من عرض سعر
        public string? OrderSourceName { get; set; }

        public int CustomerID { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerNameAr { get; set; }

        public int? QuotationID { get; set; }
        public string? LinkedQuotationNumber { get; set; }

        public int? SalesRepID { get; set; }
        public string? SalesRepName { get; set; }

        public decimal TotalAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal SubTotal { get; set; }

        public DateTime? ExpectedDeliveryDate { get; set; }
        public DateTime? ApprovedDate { get; set; }

        public int ItemCount { get; set; }
        public decimal TotalOrderedQty { get; set; }
        public decimal TotalDeliveredQty { get; set; }
        public decimal TotalRemainingQty { get; set; }

        public bool IsOverdue { get; set; }
        public int DaysOverdue { get; set; }

        public string? WarehouseNameAr { get; set; }
        public string? PaymentTermName { get; set; }
        public string? CurrencyCode { get; set; }
        public string? PriceListName { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    // ==========================================
    // رأس أمر البيع
    // ==========================================
    public class SalesOrderHeaderDto
    {
        public int SalesOrderID { get; set; }
        public string? OrderNumber { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.Today;
        public int OrderStatus { get; set; } = 1;
        public int OrderSource { get; set; } = 1;

        public int CustomerID { get; set; }
        public string? CustomerNameAr { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerPhone { get; set; }
        public decimal CustomerCurrentBalance { get; set; }
        public decimal CustomerCreditLimit { get; set; }

        public int? QuotationID { get; set; }
        public string? LinkedQuotationNumber { get; set; }

        public DateTime? ExpectedDeliveryDate { get; set; }
        public int? PaymentTermID { get; set; }
        public string? PaymentTermName { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public string? CurrencySymbol { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public int? WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public int? PriceListID { get; set; }
        public string? PriceListName { get; set; }
        public int? SalesRepID { get; set; }
        public string? SalesRepName { get; set; }

        public decimal SubTotal { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal OtherCosts { get; set; }
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
        public string? InternalNotes { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    // ==========================================
    // تفاصيل أمر البيع - عرض
    // ==========================================
    public class SalesOrderDetailDto
    {
        public int SODetailID { get; set; }
        public int SalesOrderID { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        public decimal OrderedQty { get; set; }
        public decimal DeliveredQty { get; set; }
        public decimal RemainingQty { get; set; }

        public int? QuotationDetailID { get; set; }
        public decimal? QuotationQty { get; set; }
        public decimal? QuotationRemaining { get; set; }

        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }
        public decimal LineTotalWithTax { get; set; }

        public DateTime? ExpectedDate { get; set; }
        public int LineStatus { get; set; }
        public string? Notes { get; set; }

        public decimal AvailableQty { get; set; }
    }

    // ==========================================
    // تفاصيل أمر البيع - إدخال
    // ==========================================
    public class SalesOrderDetailEditDto
    {
        public int SODetailID { get; set; }
        public int SalesOrderID { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        public decimal OrderedQty { get; set; }

        public int? QuotationDetailID { get; set; }
        public decimal? QuotationQty { get; set; }
        public decimal? QuotationRemaining { get; set; }

        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal TaxRate { get; set; } = 14;

        public decimal DiscountAmount => UnitPrice * OrderedQty * DiscountPercent / 100;
        public decimal LineTotal => UnitPrice * OrderedQty * (1 - DiscountPercent / 100);
        public decimal TaxAmount => LineTotal * TaxRate / 100;
        public decimal LineTotalWithTax => LineTotal * (1 + TaxRate / 100);

        public DateTime? ExpectedDate { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // سطور عرض السعر المتاحة لتحميلها في أمر بيع
    // ==========================================
    public class QuotationLineForOrderDto
    {
        public int QuotationDetailID { get; set; }
        public int QuotationID { get; set; }
        public string? QuotationNumber { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        public decimal QuotedQty { get; set; }
        public decimal TotalOrderedQty { get; set; }
        public decimal RemainingToOrder { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal CurrentStock { get; set; }

        public bool IsSelected { get; set; } = true;
        public decimal ActualUnitPrice { get; set; }
    }

    public class QuotationForOrderDto
    {
        public int QuotationID { get; set; }
        public string? QuotationNumber { get; set; }
        public DateTime QuotationDate { get; set; }
        public int CustomerID { get; set; }
        public string? CustomerNameAr { get; set; }
        public int TotalItems { get; set; }
        public int RemainingItems { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Notes { get; set; }
    }

    // تم إزالة التكرار - نستخدم CustomerQuickInfoDto من DbService + نكمل بيانات إضافية عبر LookupService
    public class SalesCustomerExtendedDto
    {
        public int CustomerID { get; set; }
        public string? CustomerNameAr { get; set; }
        public string? CustomerCode { get; set; }
        public string? Phone1 { get; set; }
        public string? City { get; set; }
        public decimal CreditLimit { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal AvailableCredit => CreditLimit - CurrentBalance;
        public int? PriceListID { get; set; }
        public string? PriceListName { get; set; }
        public int? SalesRepID { get; set; }
        public string? SalesRepName { get; set; }
    }
}
