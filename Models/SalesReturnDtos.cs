namespace YKCoatings.Models
{
    public sealed class SalesReturnListDto
    {
        public int ReturnID { get; set; }
        public string ReturnNumber { get; set; } = "";
        public DateTime ReturnDate { get; set; }
        public int ReturnStatus { get; set; }
        public int ReturnType { get; set; }
        public string? ReturnReason { get; set; }
        public int CustomerID { get; set; }
        public string CustomerNameAr { get; set; } = "";
        public int? InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public int WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public decimal SubTotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public sealed class SalesReturnHeaderDto
    {
        public int ReturnID { get; set; }
        public string ReturnNumber { get; set; } = "";
        public DateTime ReturnDate { get; set; } = DateTime.Today;
        public int ReturnStatus { get; set; } = 1;
        public int ReturnType { get; set; } = 1;
        public string? ReturnReason { get; set; }

        public int CustomerID { get; set; }
        public string? CustomerNameAr { get; set; }
        public int? InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public int? SalesRepID { get; set; }
        public string? SalesRepNameAr { get; set; }
        public int WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }

        public decimal SubTotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? ApprovedByName { get; set; }
        public string? RejectionReason { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    public sealed class SalesReturnDetailDto
    {
        public int ReturnDetailID { get; set; }
        public int ReturnID { get; set; }
        public int LineNumber { get; set; }
        public int InvoiceDetailID { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal InvoiceQuantity { get; set; }
        public decimal PreviouslyReturnedQuantity { get; set; }
        public decimal RemainingReturnQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotalWithTax { get; set; }
        public string? BatchNumber { get; set; }
        public byte ItemCondition { get; set; } = 1;
        public string? ReturnReason { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class SalesReturnInvoiceContextDto
    {
        public int InvoiceID { get; set; }
        public string InvoiceNumber { get; set; } = "";
        public DateTime InvoiceDate { get; set; }
        public int CustomerID { get; set; }
        public string CustomerNameAr { get; set; } = "";
        public int? SalesRepID { get; set; }
        public int? WarehouseID { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TaxableAmount { get; set; }
        public bool IsTaxable { get; set; }
        public int DetailCount { get; set; }
    }

    /// <summary>
    /// سطر فاتورة مبيعات متاح للمرتجع. حقول السعر/الضريبة يحددها الخادم من الفاتورة الأصلية.
    /// </summary>
    public sealed class SalesReturnInvoiceLineDto
    {
        public int InvoiceDetailID { get; set; }
        public int InvoiceID { get; set; }
        public int LineNumber { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public decimal InvoiceQuantity { get; set; }
        public decimal PreviouslyReturnedQuantity { get; set; }
        public decimal RemainingReturnQuantity { get; set; }
        public decimal ReturnQuantity { get; set; }
        public decimal InvoiceUnitPrice { get; set; }
        public decimal EffectiveReturnUnitPrice { get; set; }
        public decimal TaxRate { get; set; }
        public decimal OriginalTaxAmount { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int? SourceWarehouseID { get; set; }
        public byte ItemCondition { get; set; } = 1;
        public string? ReturnReason { get; set; }
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// الحقول الوحيدة التي يسمح للمستخدم بتعديلها في سطر مرتجع مرتبط بفاتورة.
    /// السعر والضريبة يعاد احتسابهما على الخادم من حركة الفاتورة الأصلية.
    /// </summary>
    public sealed class SalesReturnDetailSaveDto
    {
        public int ReturnDetailID { get; set; }
        public int InvoiceDetailID { get; set; }
        public decimal Quantity { get; set; }
        public byte ItemCondition { get; set; } = 1;
        public string? ReturnReason { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class SalesReturnSaveDto
    {
        public int ReturnID { get; set; }
        public DateTime ReturnDate { get; set; } = DateTime.Today;
        public int ReturnType { get; set; } = 1;
        public int WarehouseID { get; set; }
        public string? ReturnReason { get; set; }
        public string? Notes { get; set; }
        public List<SalesReturnDetailSaveDto> Details { get; set; } = new();
    }
}
