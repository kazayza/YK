namespace YKCoatings.Models;

public sealed class ProductionTreeBOMOptionDto
{
    public int BOMID { get; set; }
    public string BOMCode { get; set; } = "";
    public string BOMName { get; set; } = "";
    public int ProductItemID { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int ProductItemType { get; set; }
    public int BOMVersion { get; set; }
    public decimal BatchSize { get; set; }
    public string BatchUnitName { get; set; } = "";
    public decimal OutputQty { get; set; }
    public string OutputUnitName { get; set; } = "";
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

/// <summary>
/// One item in the read-only production tree. Component quantities remain exactly as stored
/// for that item's own BOM; they are not scaled or converted across different BOM output units.
/// </summary>
public sealed class ProductionTreeNodeDto
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int ItemType { get; set; }
    public string ItemTypeName { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string? UnitName { get; set; }
    public decimal WastePercent { get; set; }
    public bool IsOptional { get; set; }
    public int StageNumber { get; set; }
    public string? Notes { get; set; }
    public string? BOMCode { get; set; }
    public int? BOMVersion { get; set; }
    public decimal? BatchSize { get; set; }
    public string? BatchUnitName { get; set; }
    public decimal? OutputQty { get; set; }
    public string? OutputUnitName { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? ExpansionNote { get; set; }
    public List<ProductionTreeNodeDto> Components { get; set; } = new();
}

public sealed class ProductionTreeResult
{
    public ProductionTreeNodeDto Root { get; set; } = new();
    public string QuantityScopeNote { get; set; } = "";
}
