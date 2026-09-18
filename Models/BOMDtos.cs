namespace YKCoatings.Services
{
    // ==========================================
    // قائمة الوصفات
    // ==========================================
    public class BOMListDto
    {
        public int BOMID { get; set; }
        public string BOMCode { get; set; } = "";
        public string BOMName { get; set; } = "";
        public int ProductItemID { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int BOMVersion { get; set; }

        public decimal BatchSize { get; set; }
        public int BatchUnitID { get; set; }
        public string BatchUnitName { get; set; } = "";

        public decimal OutputQty { get; set; }
        public int OutputUnitID { get; set; }
        public string OutputUnitName { get; set; } = "";

        public decimal WastePercent { get; set; }
        public decimal? EstimatedTime { get; set; }

        public int RawMaterialCount { get; set; }
        public int PackagingMaterialCount { get; set; }

        public decimal TotalMaterialCost { get; set; }
        public decimal LaborCostPerBatch { get; set; }
        public decimal OverheadCostPerBatch { get; set; }
        public decimal TotalBatchCost { get; set; }

        public int BOMStatus { get; set; }
        public string StatusName { get; set; } = "";
        public bool IsActive { get; set; }
    }

    // ==========================================
    // رأس الوصفة
    // ==========================================
    public class BOMEditDto
    {
        public int BOMID { get; set; }
        public string BOMCode { get; set; } = "";
        public string BOMName { get; set; } = "";
        public int ProductItemID { get; set; }
        public int BOMVersion { get; set; } = 1;

        public decimal BatchSize { get; set; }
        public int BatchUnitID { get; set; }

        public decimal OutputQty { get; set; }
        public int OutputUnitID { get; set; }

        public decimal WastePercent { get; set; } = 3;
        public decimal? EstimatedTime { get; set; }

        public decimal LaborCostPerBatch { get; set; }
        public decimal OverheadCostPerBatch { get; set; }

        public string? Instructions { get; set; }
        public string? SafetyNotes { get; set; }
        public string? QualityNotes { get; set; }
        public string? Notes { get; set; }

        public int BOMStatus { get; set; } = 1;
        public bool IsActive { get; set; } = true;

        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }

        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
    }

    // ==========================================
    // مكونات الوصفة
    // ==========================================
    public class BOMDetailDto
    {
        public int BOMDetailID { get; set; }
        public int BOMID { get; set; }
        public int LineNumber { get; set; }

        public int ItemID { get; set; }
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";

        public int MaterialType { get; set; }
        public string MaterialTypeName { get; set; } = "";

        public int UnitID { get; set; }
        public string UnitName { get; set; } = "";

        public decimal Quantity { get; set; }
        public decimal WastePercent { get; set; }
        public decimal NetQuantity { get; set; }

        public decimal UnitCost { get; set; }
        public decimal LineCost { get; set; }

        public bool IsOptional { get; set; }
        public int? SubstituteItemID { get; set; }
        public string? SubstituteItemName { get; set; }

        public int Sequence { get; set; }
        public int StageNumber { get; set; }

        public string? Notes { get; set; }
    }

    // ==========================================
    // مراحل الوصفة
    // ==========================================
    public class BOMStageDto
    {
        public int BOMStageID { get; set; }
        public int BOMID { get; set; }

        public int StageID { get; set; }
        public string StageName { get; set; } = "";

        public int StageOrder { get; set; }
        public decimal? Duration { get; set; }

        public string? Temperature { get; set; }
        public string? Speed { get; set; }

        public bool QualityCheckRequired { get; set; }
        public string? QualityCheckNotes { get; set; }

        public string? Instructions { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // معلومات النظام
    // ==========================================
    public class BOMAuditDto
    {
        public string CreatedByName { get; set; } = "";
        public DateTime? CreatedDate { get; set; }

        public string ModifiedByName { get; set; } = "";
        public DateTime? ModifiedDate { get; set; }

        public string ApprovedByName { get; set; } = "";
        public DateTime? ApprovedDate { get; set; }
    }

    // ==========================================
    // ملخص التكاليف
    // ==========================================
    public class BOMCostSummaryDto
    {
        public decimal RawMaterialCost { get; set; }
        public decimal PackagingCost { get; set; }
        public decimal TotalMaterialCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal OverheadCost { get; set; }
        public decimal TotalBatchCost { get; set; }
        public decimal UnitCost { get; set; }
    }

    // ==========================================
    // Lookup خاص بالوصفات
    // ==========================================
    public class BOMLookupDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

}