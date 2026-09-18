namespace YKCoatings.Services
{
    public class ItemListDto
    {
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public string? ItemNameEn { get; set; }
        public int ItemType { get; set; }
        public string? UnitName { get; set; }
        public decimal AverageCost { get; set; }
        public decimal DefaultSellingPrice { get; set; }
        public bool IsActive { get; set; }
    }

    public class ItemEditDto
    {
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? Barcode { get; set; }
        public string? ItemNameAr { get; set; }
        public string? ItemNameEn { get; set; }
        public int CategoryID { get; set; }
        public int ItemType { get; set; }
        public int PrimaryUnitID { get; set; }
        public int SecondaryUnitID { get; set; }
        public decimal ConversionFactor { get; set; }
        public decimal MinStockLevel { get; set; }
        public decimal MaxStockLevel { get; set; }
        public decimal ReorderLevel { get; set; }
        public int ShelfLifeDays { get; set; }
        public decimal StandardCost { get; set; }
        public decimal LastPurchasePrice { get; set; }
        public decimal AverageCost { get; set; }
        public decimal DefaultSellingPrice { get; set; }
        public decimal TaxRate { get; set; }
        public bool IsTaxable { get; set; }
        public decimal Weight { get; set; }
        public decimal Volume { get; set; }
        public string? Description { get; set; }
        public string? Notes { get; set; }
        public string? ImagePath { get; set; }
        public bool IsActive { get; set; }
    }

    public class ItemAuditDto
    {
        public string? CreatedByName { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? ModifiedByName { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    public class LookupDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
        public int? ParentId { get; set; }
        public string? ParentCode { get; set; }
    }

    public class UserLoginDto
    {
        public int UserID { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public int RoleID { get; set; }
        public string RoleNameAr { get; set; } = "";
        public string RoleCode { get; set; } = "";
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? PasswordChangedDate { get; set; }
public int PasswordExpiryDays { get; set; }
        public int? EmployeeID { get; set; }
    }

    public class LoginResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int UserID { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public int RoleID { get; set; }
        public string RoleName { get; set; } = "";
        public string RoleCode { get; set; } = "";
        public bool MustChangePassword { get; set; }
        public bool PasswordExpired { get; set; }
public bool RequiresPasswordChange => MustChangePassword || PasswordExpired;
        public int? EmployeeID { get; set; }
    }

    public class CategoryListDto
    {
        public int CategoryID { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryNameAr { get; set; }
        public string? CategoryNameEn { get; set; }
        public int? ParentCategoryID { get; set; }
        public int CategoryLevel { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public string? ParentCategoryName { get; set; }
        public int ItemCount { get; set; }
    }

    public class CategoryEditDto
    {
        public int CategoryID { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryNameAr { get; set; }
        public string? CategoryNameEn { get; set; }
        public int? ParentCategoryID { get; set; }
        public int CategoryLevel { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
        // ==========================================
    // وحدات القياس
    // ==========================================
    public class UnitListDto
    {
        public int UnitID { get; set; }
        public string? UnitCode { get; set; }
        public string? UnitNameAr { get; set; }
        public string? UnitNameEn { get; set; }
        public string? UnitType { get; set; }
        public bool IsActive { get; set; }
        public int ItemCount { get; set; }
        public int ConversionCount { get; set; }
    }

    public class UnitEditDto
    {
        public int UnitID { get; set; }
        public string? UnitCode { get; set; }
        public string? UnitNameAr { get; set; }
        public string? UnitNameEn { get; set; }
        public string? UnitType { get; set; }
        public bool IsActive { get; set; }
    }

    public class UnitConversionDto
    {
        public int ConversionID { get; set; }
        public int FromUnitID { get; set; }
        public string? FromUnitName { get; set; }
        public string? FromUnitCode { get; set; }
        public int ToUnitID { get; set; }
        public string? ToUnitName { get; set; }
        public string? ToUnitCode { get; set; }
        public decimal ConversionFactor { get; set; }
        public bool IsActive { get; set; }
    }
  
    // ==========================================
    // المخازن
    // ==========================================
    public class WarehouseListDto
    {
        public int WarehouseID { get; set; }
        public string? WarehouseCode { get; set; }
        public string? WarehouseNameAr { get; set; }
        public string? WarehouseNameEn { get; set; }
        public int WarehouseType { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public int? ManagerID { get; set; }
        public string? ManagerName { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
    }

    public class WarehouseEditDto
    {
        public int WarehouseID { get; set; }
        public string? WarehouseCode { get; set; }
        public string? WarehouseNameAr { get; set; }
        public string? WarehouseNameEn { get; set; }
        public int WarehouseType { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public int? ManagerID { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
    }

    // ==========================================
    // قوائم الأسعار
    // ==========================================
       // ==========================================
    // قوائم الأسعار
    // ==========================================
    public class PriceListHeaderDto
    {
        public int PriceListID { get; set; }
        public string? PriceListCode { get; set; }
        public string? PriceListNameAr { get; set; }
        public string? PriceListNameEn { get; set; }
        public int PriceListType { get; set; }
        public string? CurrencyName { get; set; }
        public decimal DiscountPercent { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public int ItemCount { get; set; }
    }

    public class PriceListEditDto
    {
        public int PriceListID { get; set; }
        public string? PriceListCode { get; set; }
        public string? PriceListNameAr { get; set; }
        public string? PriceListNameEn { get; set; }
        public int PriceListType { get; set; }
        public int? CurrencyID { get; set; }
        public decimal DiscountPercent { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public string? Notes { get; set; }
    }

    public class PriceListDetailDto
    {
        public int PriceDetailID { get; set; }
        public int PriceListID { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal MinQty { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal MinSellingPrice { get; set; }
        public bool IsActive { get; set; }
    }

    public class PriceListDetailEditDto
    {
        public int PriceDetailID { get; set; }
        public int PriceListID { get; set; }
        public int ItemID { get; set; }
        public int UnitID { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal MinQty { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal MinSellingPrice { get; set; }
    }
        // ==========================================
    // إعدادات النظام
    // ==========================================
    public class SettingDto
    {
        public int SettingID { get; set; }
        public string SettingKey { get; set; } = "";
        public string? SettingValue { get; set; }
        public string? SettingDescription { get; set; }
        public string? SettingGroup { get; set; }
        public string? DataType { get; set; }
    }

    // ==========================================
    // الإشعارات
    // ==========================================
    public class NotificationDto
    {
        public int NotificationID { get; set; }
        public DateTime NotificationDate { get; set; }
        public int NotificationType { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public int Priority { get; set; }
        public string? RelatedModule { get; set; }
        public int? RelatedRecordID { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadDate { get; set; }
        public bool IsActioned { get; set; }
        public DateTime? ActionDate { get; set; }
    }
    public class DashboardStatsDto
    {
        public int TotalItems { get; set; }
        public int TotalCategories { get; set; }
        public int TotalSuppliers { get; set; }
        public int TotalCustomers { get; set; }
        public int TotalWarehouses { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalUsers { get; set; }
        public int TodayLogins { get; set; }
        public int TodayAuditActions { get; set; }
        public decimal SuppliersBalance { get; set; }
        public decimal CustomersBalance { get; set; }
    }

    public class RecentActivityDto
    {
        public DateTime AuditDate { get; set; }
        public string? Username { get; set; }
        public string? ActionName { get; set; }
        public string? TableName { get; set; }
        public string? Description { get; set; }
    }

    // ==========================================
    // طلبات الشراء
    // ==========================================
    public class PurchaseRequestListDto
    {
        public int RequestID { get; set; }
        public string? RequestNumber { get; set; }
        public DateTime RequestDate { get; set; }
        public DateTime? RequiredDate { get; set; }
        public string? RequestedByName { get; set; }
        public string? DepartmentName { get; set; }
        public int RequestStatus { get; set; }
        public int Priority { get; set; }
        public int ItemCount { get; set; }
        public string? Notes { get; set; }
    }

    public class PurchaseRequestHeaderDto
    {
        public int RequestID { get; set; }
        public string? RequestNumber { get; set; }
        public DateTime RequestDate { get; set; }
        public DateTime? RequiredDate { get; set; }
        public int? RequestedBy { get; set; }
        public int? DepartmentID { get; set; }
        public int RequestStatus { get; set; }
        public int Priority { get; set; }
        public string? Notes { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? RejectionReason { get; set; }
    }

    public class PurchaseRequestDetailDto
    {
        public int RequestDetailID { get; set; }
        public int RequestID { get; set; }
        public int LineNumber { get; set; }
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemNameAr { get; set; }
        public int UnitID { get; set; }
        public string? UnitName { get; set; }
        public decimal RequestedQty { get; set; }
        public decimal? ApprovedQty { get; set; }
        public decimal EstimatedPrice { get; set; }
        public decimal CurrentStock { get; set; }
        public string? Purpose { get; set; }
        public string? Notes { get; set; }
    }

    public class PurchaseRequestDetailEditDto
    {
        public int RequestDetailID { get; set; }
        public int RequestID { get; set; }
        public int LineNumber { get; set; }
        public int ItemID { get; set; }
        public int UnitID { get; set; }
        public decimal RequestedQty { get; set; }
        public decimal? ApprovedQty { get; set; }
        public decimal EstimatedPrice { get; set; }
        public decimal CurrentStock { get; set; }
        public string? Purpose { get; set; }
        public string? Notes { get; set; }
    }
        // ==========================================
    // عنصر قابل للبحث
    // ==========================================
    public class SearchableItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Code { get; set; }
        public string? Extra { get; set; }
    }
        public class ItemQuickInfoDto
    {
        public int UnitID { get; set; }
        public decimal EstimatedPrice { get; set; }
    }

    public class ApprovedQtyDto
    {
        public int DetailID { get; set; }
        public decimal ApprovedQty { get; set; }
    }
    public class DashboardPendingDto
{
    public int PendingPR { get; set; }
    public int PendingPO { get; set; }
    public int PendingGRN { get; set; }
    public int PendingInvoice { get; set; }
    public int PendingReturn { get; set; }
    public int TotalPending => PendingPR + PendingPO + PendingGRN + PendingInvoice + PendingReturn;
}

public class DashboardInventoryAlertsDto
{
    public int OutOfStock { get; set; }
    public int BelowMinimum { get; set; }
    public int NearExpiry { get; set; }
    public int Expired { get; set; }
    public decimal TotalStockValue { get; set; }
    public int ItemsWithStock { get; set; }
    public int TotalAlerts => OutOfStock + BelowMinimum + NearExpiry + Expired;
}

public class DashboardOverdueDto
{
    public int OverdueInvoices { get; set; }
    public decimal OverdueAmount { get; set; }
    public int UnpaidInvoices { get; set; }
    public decimal TotalUnpaidAmount { get; set; }
}

public class DashboardPurchaseStatsDto
{
    public int MonthlyPOs { get; set; }
    public decimal MonthlyPOAmount { get; set; }
    public int MonthlyInvoices { get; set; }
    public decimal MonthlyInvoiceAmount { get; set; }
    public int MonthlyGRNs { get; set; }
    public int MonthlyReturns { get; set; }
}

public class DashboardItemAlertDto
{
    public int ItemID { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemNameAr { get; set; }
    public string? ItemTypeName { get; set; }
    public string? WarehouseNameAr { get; set; }
    public decimal CurrentQty { get; set; }
    public decimal MinStockLevel { get; set; }
    public decimal ReorderLevel { get; set; }
    public int AlertLevel { get; set; }
    public string? AlertName { get; set; }
}
public class MonthlyChartDto
{
    public string? MonthKey { get; set; }
    public string? MonthName { get; set; }
    public decimal Amount { get; set; }
}
 // ==========================================
// العملات
// ==========================================

public class CurrencyListDto
{
    public int CurrencyID { get; set; }
    public string? CurrencyCode { get; set; }
    public string? CurrencyNameAr { get; set; }
    public string? CurrencyNameEn { get; set; }
    public string? Symbol { get; set; }
    public decimal ExchangeRate { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}

public class CurrencyEditDto
{
    public int CurrencyID { get; set; }
    public string? CurrencyCode { get; set; }
    public string? CurrencyNameAr { get; set; }
    public string? CurrencyNameEn { get; set; }
    public string? Symbol { get; set; }
    public decimal ExchangeRate { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}   
// ==========================================
// شروط الدفع
// ==========================================

public class PaymentTermListDto
{
    public int PaymentTermID { get; set; }
    public string? TermCode { get; set; }
    public string? TermNameAr { get; set; }
    public string? TermNameEn { get; set; }
    public int DueDays { get; set; }
    public decimal DiscountPercent { get; set; }
    public int DiscountDays { get; set; }
    public bool IsActive { get; set; }
    public int UsedCount { get; set; }
}

public class PaymentTermEditDto
{
    public int PaymentTermID { get; set; }
    public string? TermCode { get; set; }
    public string? TermNameAr { get; set; }
    public string? TermNameEn { get; set; }
    public int DueDays { get; set; }
    public decimal DiscountPercent { get; set; }
    public int DiscountDays { get; set; }
    public bool IsActive { get; set; }
}

}