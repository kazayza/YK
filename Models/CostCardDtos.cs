namespace YKCoatings.Models
{
    public sealed class CostCardProductOptionDto
    {
        public int ItemID { get; set; }
        public string ItemCode { get; set; } = "";
        public string ItemNameAr { get; set; } = "";
        public string? UnitName { get; set; }
        public string DisplayName => $"{ItemCode} — {ItemNameAr}";
    }

    public sealed class CostCardPeriodOptionDto
    {
        public int PeriodID { get; set; }
        public string PeriodName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string DisplayName => $"{PeriodName} ({StartDate:dd/MM/yyyy}–{EndDate:dd/MM/yyyy})";
    }

    public sealed class CostCardSourceOptionDto
    {
        public int SourceID { get; set; }
        public string SourceCode { get; set; } = "";
        public string SourceName { get; set; } = "";
        public decimal OutputQty { get; set; }
        public string UnitName { get; set; } = "";
        public DateTime? SourceDate { get; set; }
        public string DisplayName => $"{SourceCode} — {SourceName} · {OutputQty:N4} {UnitName}";
    }

    public sealed class CostCardPreviewDto
    {
        public int ItemID { get; set; }
        public string ItemCode { get; set; } = "";
        public string ItemNameAr { get; set; } = "";
        public int CostType { get; set; }
        public int? PeriodID { get; set; }
        public string? PeriodName { get; set; }
        public int SourceID { get; set; }
        public string SourceReference { get; set; } = "";
        public string UnitName { get; set; } = "";
        public decimal OutputQty { get; set; }

        public decimal RawMaterialCost { get; set; }
        public decimal FinishedComponentCost { get; set; }
        public decimal PackagingCost { get; set; }
        public decimal DirectLaborCost { get; set; }
        public decimal IndirectMaterialCost { get; set; }
        public decimal IndirectLaborCost { get; set; }
        public decimal UtilityCost { get; set; }
        public decimal DepreciationCost { get; set; }
        public decimal MaintenanceCost { get; set; }
        public decimal OtherOverheadCost { get; set; }
        public decimal WastePercent { get; set; }
        public decimal WasteCost { get; set; }
        public decimal TargetProfitPercent { get; set; }
        public decimal CostPerUnit { get; set; }
        public decimal SuggestedPrice { get; set; }

        public decimal UnclassifiedMaterialCost { get; set; }
        public decimal ReturnedQty { get; set; }
        public bool HasApprovedIssueLines { get; set; }
        public bool MaterialCostMismatch { get; set; }
        public string PreviewFingerprint { get; set; } = "";
        public bool CanPersist { get; set; }
        public List<string> Warnings { get; set; } = new();

        public decimal TotalDirectCost => RawMaterialCost + FinishedComponentCost + PackagingCost + DirectLaborCost;
        public decimal TotalIndirectCost => IndirectMaterialCost + IndirectLaborCost + UtilityCost +
                                            DepreciationCost + MaintenanceCost + OtherOverheadCost;
        public decimal TotalCost => TotalDirectCost + TotalIndirectCost;
        public bool HasWarnings => Warnings.Count > 0;
        public string CostTypeName => CostType == 1 ? "تقديري من الوصفة" : "فعلي من أمر تصنيع";
    }

    public sealed class CostCardSaveRequest
    {
        public int ItemID { get; set; }
        public int? PeriodID { get; set; }
        public int CostType { get; set; }
        public int? BOMID { get; set; }
        public int? ProductionOrderID { get; set; }
        public decimal TargetProfitPercent { get; set; }
        public string PreviewFingerprint { get; set; } = "";
        public bool AcknowledgeWarnings { get; set; }
    }

    public sealed class CostCardHistoryDto
    {
        public int CostCardID { get; set; }
        public int ItemID { get; set; }
        public string ItemCode { get; set; } = "";
        public string ItemNameAr { get; set; } = "";
        public int? PeriodID { get; set; }
        public string? PeriodName { get; set; }
        public DateTime CalculationDate { get; set; }
        public int CostType { get; set; }
        public decimal RawMaterialCost { get; set; }
        public decimal PackagingCost { get; set; }
        public decimal DirectLaborCost { get; set; }
        public decimal IndirectMaterialCost { get; set; }
        public decimal IndirectLaborCost { get; set; }
        public decimal UtilityCost { get; set; }
        public decimal DepreciationCost { get; set; }
        public decimal MaintenanceCost { get; set; }
        public decimal OtherOverheadCost { get; set; }
        public decimal TotalDirectCost { get; set; }
        public decimal TotalIndirectCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal CostPerUnit { get; set; }
        public decimal WastePercent { get; set; }
        public decimal WasteCost { get; set; }
        public decimal TargetProfitPercent { get; set; }
        public decimal SuggestedPrice { get; set; }
        public string? Notes { get; set; }
        public string CostTypeName => CostType == 1 ? "تقديري من الوصفة" : "فعلي من أمر تصنيع";
    }
}
