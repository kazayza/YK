namespace YKCoatings.Services
{
    // ==========================================
    // قائمة أذون الصرف
    // ==========================================
    public class MaterialIssueListDto
    {
        public int MaterialIssueID { get; set; }
        public string IssueNumber { get; set; } = "";
        public DateTime IssueDate { get; set; }

        public int ProductionOrderID { get; set; }
        public string OrderNumber { get; set; } = "";
        public string ProductName { get; set; } = "";

        public int SourceWarehouseID { get; set; }
        public string WarehouseName { get; set; } = "";

        public int IssueStatus { get; set; }
        public string StatusName { get; set; } = "";

        public int TotalItems { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalCost { get; set; }

        public string CreatedByName { get; set; } = "";
    }

    // ==========================================
    // رأس إذن الصرف
    // ==========================================
    public class MaterialIssueEditDto
    {
        public int MaterialIssueID { get; set; }
        public string IssueNumber { get; set; } = "";
        public DateTime IssueDate { get; set; } = DateTime.Today;

        public int ProductionOrderID { get; set; }
        public string OrderNumber { get; set; } = "";
        public string ProductName { get; set; } = "";

        public int SourceWarehouseID { get; set; }
        public string WarehouseName { get; set; } = "";

        public int IssueStatus { get; set; } = 1;
        public int IssueType { get; set; } = 1;
        public int? IssuedBy { get; set; }
        public int? ReceivedBy { get; set; }

        public int TotalItems { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalCost { get; set; }

        public string? Notes { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
    }

    // ==========================================
    // تفاصيل إذن الصرف
    // ==========================================
    public class MaterialIssueDetailDto
    {
        public int IssueDetailID { get; set; }
        public int MaterialIssueID { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";

        public int UnitID { get; set; }
        public string UnitName { get; set; } = "";

        public int MaterialType { get; set; }
        public string MaterialTypeName { get; set; } = "";

        public decimal RequiredQty { get; set; }
        public decimal IssuedQty { get; set; }
        public decimal RemainingQty => RequiredQty - IssuedQty;

        public decimal IssueQty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineCost { get; set; }

        public int? POMaterialID { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? Notes { get; set; }

        public decimal AvailableStock { get; set; }
        public string AvailabilityStatus =>
            AvailableStock >= IssueQty ? "متوفر" :
            AvailableStock > 0 ? "متوفر جزئياً" : "غير متوفر";
        public string AvailabilityClass =>
            AvailableStock >= IssueQty ? "avail-ok" :
            AvailableStock > 0 ? "avail-partial" : "avail-no";
    }

    // ==========================================
    // مادة معلقة للصرف (من أمر التصنيع)
    // ==========================================
    public class PendingMaterialDto
    {
        public int POMaterialID { get; set; }
        public int ItemID { get; set; }
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int UnitID { get; set; }
        public string UnitName { get; set; } = "";
        public int MaterialType { get; set; }
        public string MaterialTypeName { get; set; } = "";
        public decimal RequiredQty { get; set; }
        public decimal AlreadyIssuedQty { get; set; }
        public decimal RemainingQty => RequiredQty - AlreadyIssuedQty;
        public decimal UnitCost { get; set; }
    }
}