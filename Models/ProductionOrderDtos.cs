namespace YKCoatings.Services
{
    // ==========================================
    // قائمة أوامر التصنيع
    // ==========================================
    public class ProductionOrderListDto
    {
        public int ProductionOrderID { get; set; }
        public string OrderNumber { get; set; } = "";
        public DateTime OrderDate { get; set; }

        public int BOMID { get; set; }
        public string BOMName { get; set; } = "";
        public string BOMCode { get; set; } = "";

        public int ProductItemID { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";

        public decimal PlannedQty { get; set; }
        public int PlannedUnitID { get; set; }
        public string UnitName { get; set; } = "";

        public decimal ActualQty { get; set; }
        public decimal WasteQty { get; set; }
        public int NumberOfBatches { get; set; }

        public int OrderStatus { get; set; }
        public string StatusName { get; set; } = "";
        public int OrderPriority { get; set; }
        public string PriorityName { get; set; } = "";

        public DateTime? PlannedStartDate { get; set; }
        public DateTime? PlannedEndDate { get; set; }
        public DateTime? ActualStartDate { get; set; }
        public DateTime? ActualEndDate { get; set; }

        public decimal EstimatedTotalCost { get; set; }
        public decimal ActualTotalCost { get; set; }

        public string SourceWarehouse { get; set; } = "";
        public string TargetWarehouse { get; set; } = "";
        public string AssignedToName { get; set; } = "";

        public int BatchCount { get; set; }
        public int CompletedBatchCount { get; set; }

        // نسبة الإنجاز
        public decimal CompletionPercent =>
            PlannedQty > 0 ? Math.Round(ActualQty / PlannedQty * 100, 1) : 0;
    }

    // ==========================================
    // رأس أمر التصنيع (إضافة/تعديل)
    // ==========================================
    public class ProductionOrderEditDto
    {
        public int ProductionOrderID { get; set; }
        public string OrderNumber { get; set; } = "";
        public DateTime OrderDate { get; set; } = DateTime.Today;

        public int BOMID { get; set; }
        public int ProductItemID { get; set; }

        public decimal PlannedQty { get; set; }
        public int PlannedUnitID { get; set; }

        public decimal ActualQty { get; set; }
        public decimal WasteQty { get; set; }

        public int NumberOfBatches { get; set; } = 1;

        public DateTime? PlannedStartDate { get; set; }
        public DateTime? PlannedEndDate { get; set; }
        public DateTime? ActualStartDate { get; set; }
        public DateTime? ActualEndDate { get; set; }

        public int? SourceWarehouseID { get; set; }
        public int? TargetWarehouseID { get; set; }

        public int OrderStatus { get; set; } = 1;
        public int OrderPriority { get; set; } = 2;

        // التكاليف التقديرية
        public decimal EstimatedMaterialCost { get; set; }
        public decimal EstimatedLaborCost { get; set; }
        public decimal EstimatedOverheadCost { get; set; }
        public decimal EstimatedTotalCost { get; set; }

        // التكاليف الفعلية
        public decimal ActualMaterialCost { get; set; }
        public decimal ActualLaborCost { get; set; }
        public decimal ActualOverheadCost { get; set; }
        public decimal ActualTotalCost { get; set; }

        public int? AssignedTo { get; set; }
        public int? SupervisorID { get; set; }

        public string? Notes { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
    }

    // ==========================================
    // مواد أمر التصنيع
    // ==========================================
    public class ProductionOrderMaterialDto
    {
        public int POMaterialID { get; set; }
        public int ProductionOrderID { get; set; }
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
        public decimal ReturnedQty { get; set; }
        public decimal ConsumedQty { get; set; }

        public decimal UnitCost { get; set; }
        public decimal LineCost { get; set; }

        public decimal AvailableStock { get; set; }

        public int LineStatus { get; set; }
        public string LineStatusName { get; set; } = "";

        public int? BOMDetailID { get; set; }
        public string? Notes { get; set; }

        // نسبة الصرف
        public decimal IssuePercent =>
            RequiredQty > 0 ? Math.Round(IssuedQty / RequiredQty * 100, 1) : 0;

        // حالة التوفر
        public string AvailabilityStatus =>
            AvailableStock >= RequiredQty ? "متوفر" :
            AvailableStock > 0 ? "متوفر جزئياً" : "غير متوفر";

        public string AvailabilityClass =>
            AvailableStock >= RequiredQty ? "avail-ok" :
            AvailableStock > 0 ? "avail-partial" : "avail-no";
    }

    // ==========================================
    // معلومات النظام لأمر التصنيع
    // ==========================================
    public class ProductionOrderAuditDto
    {
        public string CreatedByName { get; set; } = "";
        public DateTime? CreatedDate { get; set; }
        public string ModifiedByName { get; set; } = "";
        public DateTime? ModifiedDate { get; set; }
        public string ApprovedByName { get; set; } = "";
        public DateTime? ApprovedDate { get; set; }
        public string AssignedToName { get; set; } = "";
        public string SupervisorName { get; set; } = "";
    }

    // ==========================================
    // ملخص تكاليف أمر التصنيع
    // ==========================================
    public class ProductionOrderCostSummaryDto
    {
        public decimal EstimatedMaterialCost { get; set; }
        public decimal EstimatedLaborCost { get; set; }
        public decimal EstimatedOverheadCost { get; set; }
        public decimal EstimatedTotalCost { get; set; }

        public decimal ActualMaterialCost { get; set; }
        public decimal ActualLaborCost { get; set; }
        public decimal ActualOverheadCost { get; set; }
        public decimal ActualTotalCost { get; set; }

        public decimal CostVariance => ActualTotalCost - EstimatedTotalCost;
        public decimal CostVariancePercent =>
            EstimatedTotalCost > 0
                ? Math.Round((ActualTotalCost - EstimatedTotalCost) / EstimatedTotalCost * 100, 1)
                : 0;
    }

    // ==========================================
    // Lookup عام للتصنيع
    // ==========================================
    public class ProductionLookupDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}