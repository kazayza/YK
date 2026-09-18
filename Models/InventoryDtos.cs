namespace YKCoatings.Models
{
    // ==========================================
    // أرصدة المخزون
    // ==========================================
    public class StockBalanceDto
    {
        public int BalanceID { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int ItemType { get; set; }
        public string? ItemTypeName { get; set; }
        public string? CategoryNameAr { get; set; }

        public int WarehouseID { get; set; }
        public string? WarehouseCode { get; set; }
        public string? WarehouseNameAr { get; set; }

        public string? PrimaryUnitName { get; set; }

        public decimal CurrentQty { get; set; }
        public decimal ReservedQty { get; set; }
        public decimal AvailableQty { get; set; }
        public decimal AverageCost { get; set; }
        public decimal TotalValue { get; set; }

        public decimal MinStockLevel { get; set; }
        public decimal MaxStockLevel { get; set; }
        public decimal ReorderLevel { get; set; }

        public int StockAlert { get; set; }
        public string? StockAlertName { get; set; }

        public DateTime? LastMovementDate { get; set; }
        public DateTime? LastCountDate { get; set; }
    }

    // ==========================================
    // أرصدة الباتشات
    // ==========================================
    public class BatchBalanceDto
    {
        public int BatchBalanceID { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public DateTime? ManufactureDate { get; set; }
        public decimal CurrentQty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal BatchValue { get; set; }
        public string? UnitName { get; set; }
        public DateTime? ReceiptDate { get; set; }
        public string? SourceTypeName { get; set; }
        public string? SourceDocNumber { get; set; }
        public int ExpiryAlert { get; set; }
        public string? ExpiryAlertName { get; set; }
        public int? DaysToExpiry { get; set; }
    }

    // ==========================================
    // حركات المخزون
    // ==========================================
    public class StockMovementDto
    {
        public int TransactionID { get; set; }
        public DateTime TransactionDate { get; set; }
        public int TransactionType { get; set; }
        public string? TransactionTypeName { get; set; }
        public string? Direction { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public decimal Quantity { get; set; }
        public decimal AbsQuantity { get; set; }
        public string? UnitName { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal BalanceAfter { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? SourceDocType { get; set; }
        public int? SourceDocID { get; set; }
        public string? SourceDocNumber { get; set; }
        public string? Notes { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    // ==========================================
    // إحصائيات المخزون
    // ==========================================
    public class InventoryDashboardDto
    {
        public int ItemsWithStock { get; set; }
        public decimal TotalStockValue { get; set; }
        public int ItemsBelowMin { get; set; }
        public int ItemsOutOfStock { get; set; }
        public int BatchesNearExpiry { get; set; }
        public int BatchesExpired { get; set; }
        public int TodayMovements { get; set; }
        public int ActiveWarehouses { get; set; }
    }

    // ==========================================
    // تحويلات المخزون
    // ==========================================
    public class StockTransferListDto
    {
        public int TransferID { get; set; }
        public string? TransferNumber { get; set; }
        public DateTime TransferDate { get; set; }
        public int TransferStatus { get; set; }
        public string? TransferStatusName { get; set; }
        public string? FromWarehouseName { get; set; }
        public string? ToWarehouseName { get; set; }
        public string? TransferReason { get; set; }
        public string? RequestedByName { get; set; }
        public string? ApprovedByName { get; set; }
        public int ItemCount { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalValue { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class StockTransferHeaderDto
    {
        public int TransferID { get; set; }
        public string? TransferNumber { get; set; }
        public DateTime TransferDate { get; set; }
        public int TransferStatus { get; set; }
        public int FromWarehouseID { get; set; }
        public string? FromWarehouseName { get; set; }
        public int ToWarehouseID { get; set; }
        public string? ToWarehouseName { get; set; }
        public string? TransferReason { get; set; }
        public int? RequestedBy { get; set; }
        public string? RequestedByName { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class StockTransferDetailDto
    {
        public int TransferDetailID { get; set; }
        public int TransferID { get; set; }
        public int LineNumber { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost => Quantity * UnitCost;
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public decimal AvailableQty { get; set; }
        public string? Notes { get; set; }
    }

    public class StockTransferDetailEditDto
    {
        public int TransferDetailID { get; set; }
        public int TransferID { get; set; }
        public int LineNumber { get; set; }
        public int ItemID { get; set; }
        public int UnitID { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // الجرد
    // ==========================================
    public class StockCountListDto
    {
        public int CountID { get; set; }
        public string? CountNumber { get; set; }
        public DateTime CountDate { get; set; }
        public int CountStatus { get; set; }
        public string? CountStatusName { get; set; }
        public int CountType { get; set; }
        public string? CountTypeName { get; set; }
        public string? WarehouseNameAr { get; set; }
        public string? CategoryNameAr { get; set; }
        public string? CountedByName { get; set; }
        public string? ApprovedByName { get; set; }
        public int ItemCount { get; set; }
        public int CountedItems { get; set; }
        public decimal TotalVarianceQty { get; set; }
        public decimal TotalVarianceValue { get; set; }
        public int SurplusItems { get; set; }
        public int ShortageItems { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class StockCountHeaderDto
    {
        public int CountID { get; set; }
        public string? CountNumber { get; set; }
        public DateTime CountDate { get; set; }
        public int CountStatus { get; set; }
        public int CountType { get; set; }
        public int WarehouseID { get; set; }
        public string? WarehouseNameAr { get; set; }
        public int? CategoryID { get; set; }
        public string? CategoryNameAr { get; set; }
        public int? CountedBy { get; set; }
        public int? SupervisorID { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class StockCountDetailDto
    {
        public int CountDetailID { get; set; }
        public int CountID { get; set; }
        public int LineNumber { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public decimal SystemQty { get; set; }
        public decimal? CountedQty { get; set; }
        public decimal Variance { get; set; }
        public decimal VarianceValue { get; set; }
        public decimal UnitCost { get; set; }
        public string? BatchNumber { get; set; }
        public bool AdjustmentApproved { get; set; }
        public string? Notes { get; set; }
    }
    public class OpeningBalanceDto
{
    public int ItemID { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemNameAr { get; set; }
    public int WarehouseID { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}
}