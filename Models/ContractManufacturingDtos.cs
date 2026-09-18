using System;

namespace YKCoatings.Models
{
    // ==========================================
    // قائمة عقود التصنيع
    // ==========================================
    public class ContractOrderListDto
    {
        public int ContractOrderID { get; set; }
        public string ContractNumber { get; set; } = "";
        public DateTime ContractDate { get; set; }
        public string CustomerName { get; set; } = "";
        public int ContractStatus { get; set; }
        public string ContractStatusName { get; set; } = "";
        public int MaterialSource { get; set; }
        public string MaterialSourceName { get; set; } = "";
        public DateTime? DeliveryDate { get; set; }
        public decimal TotalContractValue { get; set; }
        public int TotalLines { get; set; }
        public int CompletedLines { get; set; }
        public string CreatedByName { get; set; } = "";
    }

    // ==========================================
    // رأس عقد التصنيع (للتعديل)
    // ==========================================
    public class ContractOrderEditDto
    {
        public int ContractOrderID { get; set; }
        public string ContractNumber { get; set; } = "";
        public DateTime ContractDate { get; set; } = DateTime.Today;

        public int CustomerID { get; set; }
        public string CustomerName { get; set; } = "";

        public int MaterialSource { get; set; } = 1;

        public int ContractStatus { get; set; } = 1;
        public string ContractStatusName { get; set; } = "";

        public DateTime? DeliveryDate { get; set; }
        public string? CustomerPONumber { get; set; }

        public decimal TotalManufacturingFee { get; set; }
        public decimal TotalMaterialCost { get; set; }
        public decimal TotalContractValue { get; set; }

        public string? Notes { get; set; }
        public int? SourceWarehouseID { get; set; }
        public int? TargetWarehouseID { get; set; }

        public int? ApprovedBy { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedDate { get; set; }

        public List<ContractOrderDetailDto> Details { get; set; } = new();
    }

    // ==========================================
    // سطر تفاصيل عقد التصنيع
    // ==========================================
    public class ContractOrderDetailDto
    {
        public int ContractDetailID { get; set; }
        public int ContractOrderID { get; set; }
        public int LineNumber { get; set; }

        public int ProductItemID { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";

        public int? BOMID { get; set; }
        public string? BOMName { get; set; }

        public decimal OrderedQty { get; set; }
        public int UnitID { get; set; }
        public string UnitName { get; set; } = "";

        public decimal ProducedQty { get; set; }
        public decimal DeliveredQty { get; set; }

        public decimal ManufacturingFeePerUnit { get; set; }
        public decimal TotalManufacturingFee { get; set; }

        public decimal MaterialCostPerUnit { get; set; }
        public decimal TotalMaterialCost { get; set; }

        public decimal LineTotal { get; set; }

        public int? ProductionOrderID { get; set; }
        public string? ProductionOrderNumber { get; set; }

        public int LineStatus { get; set; } = 1;
        public string LineStatusName { get; set; } = "";

        public string? Notes { get; set; }
            // ==========================================
    // معلومات صنف للبحث
    // ==========================================
    public class ItemInfoDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int UnitID { get; set; }
        public int? ActiveBOMID { get; set; }
    }
    }
}