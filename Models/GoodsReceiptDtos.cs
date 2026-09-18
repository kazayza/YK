namespace YKCoatings.Models
{
    // ==========================================
    // قائمة أذونات الاستلام
    // ==========================================
    public class GoodsReceiptListDto
    {
        public int GRNID { get; set; }
        public string? GRNNumber { get; set; }
        public DateTime GRNDate { get; set; }
        public int GRNStatus { get; set; }
        public int GRNSource { get; set; }
        public string? GRNSourceName { get; set; }

        public int SupplierID { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierNameAr { get; set; }

        public int? PurchaseOrderID { get; set; }
        public string? LinkedPONumber { get; set; }

        public string? WarehouseNameAr { get; set; }

        public int InspectionStatus { get; set; }
        public string? InspectionStatusName { get; set; }

        public string? SupplierInvoiceNo { get; set; }
        public string? ReceivedByName { get; set; }

        public int ItemCount { get; set; }
        public decimal TotalReceivedQty { get; set; }
        public decimal TotalAcceptedQty { get; set; }
        public decimal TotalRejectedQty { get; set; }
        public decimal TotalCost { get; set; }

        public DateTime CreatedDate { get; set; }
    }

    // ==========================================
    // رأس إذن الاستلام
    // ==========================================
    public class GoodsReceiptHeaderDto
    {
        public int GRNID { get; set; }
        public string? GRNNumber { get; set; }
        public DateTime GRNDate { get; set; }
        public int GRNStatus { get; set; }
        public int GRNSource { get; set; }

        // أمر الشراء
        public int? PurchaseOrderID { get; set; }
        public string? LinkedPONumber { get; set; }

        // المورد
        public int SupplierID { get; set; }
        public string? SupplierNameAr { get; set; }
        public string? SupplierCode { get; set; }

        // المخزن
        public int WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }

        // بيانات المورد
        public string? SupplierInvoiceNo { get; set; }
        public DateTime? SupplierInvoiceDate { get; set; }
        public string? DeliveryNoteNo { get; set; }

        // الاستلام والفحص
        public int? ReceivedBy { get; set; }
        public string? ReceivedByName { get; set; }
        public int? InspectedBy { get; set; }
        public string? InspectedByName { get; set; }
        public int InspectionStatus { get; set; }
        public string? InspectionNotes { get; set; }

        // الاعتماد
        public int? ApprovedBy { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedDate { get; set; }

        // ملاحظات
        public string? Notes { get; set; }

        // Audit
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    // ==========================================
    // تفاصيل إذن الاستلام (للعرض)
    // ==========================================
    public class GoodsReceiptDetailDto
    {
        public int GRNDetailID { get; set; }
        public int GRNID { get; set; }
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
        public decimal? AcceptedQty { get; set; }
        public decimal RejectedQty { get; set; }

        // الباتش والصلاحية
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime? ManufactureDate { get; set; }

        // التكلفة
        public decimal UnitCost { get; set; }
        public decimal LineTotalCost { get; set; }

        // الجودة
        public int QualityStatus { get; set; }
        public string? QualityNotes { get; set; }
        public string? StorageLocation { get; set; }

        // الربط
        public int? PODetailID { get; set; }
        public decimal? POOrderedQty { get; set; }
        public decimal? POReceivedQty { get; set; }
        public decimal? PORemainingQty { get; set; }

        public string? Notes { get; set; }
    }

    // ==========================================
    // تفاصيل إذن الاستلام (للإدخال)
    // ==========================================
    public class GoodsReceiptDetailEditDto
    {
        public int GRNDetailID { get; set; }
        public int GRNID { get; set; }
        public int LineNumber { get; set; }

        // الصنف
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        // الوحدة
        public int UnitID { get; set; }

        // الكميات
        public decimal OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal? AcceptedQty { get; set; }
        public decimal RejectedQty { get; set; }

        // الباتش والصلاحية
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime? ManufactureDate { get; set; }

        // التكلفة
        public decimal UnitCost { get; set; }

        // محسوبة
        public decimal LineTotalCost => (AcceptedQty ?? ReceivedQty) * UnitCost;

        // الجودة
        public int QualityStatus { get; set; } = 1;
        public string? QualityNotes { get; set; }
        public string? StorageLocation { get; set; }

        // الربط
        public int? PODetailID { get; set; }
        public decimal? PORemainingToReceive { get; set; }

        public string? Notes { get; set; }
    }

    // ==========================================
    // سطور PO المتاحة للاستلام
    // ==========================================
    public class POLineForGRNDto
    {
        public int PODetailID { get; set; }
        public int PurchaseOrderID { get; set; }
        public string? PONumber { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }

        public int UnitID { get; set; }
        public string? UnitName { get; set; }

        public decimal OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal RemainingToReceive { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TaxRate { get; set; }

        public int? ShelfLifeDays { get; set; }

        // للإدخال
        public bool IsSelected { get; set; } = true;
        public decimal QtyToReceive { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    // ==========================================
    // أوامر الشراء المتاحة للاستلام
    // ==========================================
    public class POForGRNDto
    {
        public int PurchaseOrderID { get; set; }
        public string? PONumber { get; set; }
        public DateTime PODate { get; set; }
        public string? SupplierNameAr { get; set; }
        public int SupplierID { get; set; }
        public int? WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public int TotalItems { get; set; }
        public int RemainingItems { get; set; }
    }
}