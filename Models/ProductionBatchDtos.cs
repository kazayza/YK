namespace YKCoatings.Services
{
    // ==========================================
    // قائمة دفعات الإنتاج
    // ==========================================
    public class ProductionBatchListDto
    {
        public int BatchID { get; set; }
        public string BatchNumber { get; set; } = "";
        public DateTime BatchDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public int ProductionOrderID { get; set; }
        public string ProductionOrderNumber { get; set; } = "";

        public int ProductItemID { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";

        public decimal PlannedQty { get; set; }
        public decimal ProducedQty { get; set; }
        public decimal GoodQty { get; set; }
        public decimal RejectedQty { get; set; }
        public decimal WasteQty { get; set; }
        public decimal SampleQty { get; set; }

        public int UnitID { get; set; }
        public string UnitName { get; set; } = "";

        public int? TargetWarehouseID { get; set; }
        public string TargetWarehouse { get; set; } = "";

        public decimal MaterialCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal OverheadCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal UnitCost { get; set; }

        public int BatchStatus { get; set; }
        public string StatusName { get; set; } = "";

        public int? QCResult { get; set; }
        public string QCResultName { get; set; } = "";

        public decimal WastePercent { get; set; }
    }

    // ==========================================
    // رأس دفعة الإنتاج
    // ==========================================
    public class ProductionBatchEditDto
    {
        public int BatchID { get; set; }
        public string BatchNumber { get; set; } = "";

        public int ProductionOrderID { get; set; }
        public string ProductionOrderNumber { get; set; } = "";

        public int ProductItemID { get; set; }
        public string ProductName { get; set; } = "";

        public DateTime BatchDate { get; set; } = DateTime.Today;
        public DateTime? ExpiryDate { get; set; }

        public decimal PlannedQty { get; set; }
        public decimal ProducedQty { get; set; }
        public decimal GoodQty { get; set; }
        public decimal RejectedQty { get; set; }
        public decimal WasteQty { get; set; }
        public decimal SampleQty { get; set; }

        public int UnitID { get; set; }
        public string UnitName { get; set; } = "";

        public int? TargetWarehouseID { get; set; }
        public string TargetWarehouseName { get; set; } = "";

        public decimal MaterialCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal OverheadCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal UnitCost { get; set; }

        public int BatchStatus { get; set; } = 1;

        public int? QCInspectedBy { get; set; }
        public DateTime? QCInspectionDate { get; set; }
        public int? QCResult { get; set; }
        public string? QCNotes { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public int? ProducedBy { get; set; }
        public int? SupervisorID { get; set; }

        public string? Notes { get; set; }
    }

    // ==========================================
    // مراحل دفعة الإنتاج
    // ==========================================
    public class ProductionBatchStageDto
    {
        public int BatchStageID { get; set; }
        public int BatchID { get; set; }
        public int StageID { get; set; }
        public string StageName { get; set; } = "";
        public int StageOrder { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int? Duration { get; set; }

        public string? Temperature { get; set; }
        public string? Speed { get; set; }
        public string? Humidity { get; set; }

        public int? OperatorID { get; set; }
        public string OperatorName { get; set; } = "";

        public int StageStatus { get; set; }
        public string StageStatusName { get; set; } = "";

        public bool QualityCheck { get; set; }
        public string? QualityResult { get; set; }
        public string? Notes { get; set; }
    }

    // ==========================================
    // معلومات النظام
    // ==========================================
    public class ProductionBatchAuditDto
    {
        public string CreatedByName { get; set; } = "";
        public DateTime? CreatedDate { get; set; }

        public string ModifiedByName { get; set; } = "";
        public DateTime? ModifiedDate { get; set; }

        public string ProducedByName { get; set; } = "";
        public string SupervisorName { get; set; } = "";
        public string QCInspectedByName { get; set; } = "";
        public DateTime? QCInspectionDate { get; set; }
    }

    // ==========================================
    // بيانات أمر التصنيع لإنشاء الباتش
    // ==========================================
    public class ProductionBatchOrderInfoDto
    {
        public int ProductionOrderID { get; set; }
        public string OrderNumber { get; set; } = "";

        public int ProductItemID { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";

        public decimal PlannedQty { get; set; }
        public int PlannedUnitID { get; set; }
        public string UnitName { get; set; } = "";

        public int NumberOfBatches { get; set; }
        public int? TargetWarehouseID { get; set; }
        public string TargetWarehouseName { get; set; } = "";
    }
}