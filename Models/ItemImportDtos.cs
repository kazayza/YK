namespace YKCoatings.Models;

public enum ItemImportAction
{
    Add = 1,
    Skip = 2,
    Update = 3
}

/// <summary>Values read from one workbook row. Null optional values mean blank cells.</summary>
public sealed class ItemImportData
{
    public string? ItemCode { get; set; }
    public string? Barcode { get; set; }
    public string? ItemNameAr { get; set; }
    public string? ItemNameEn { get; set; }
    public int? ItemType { get; set; }
    public string? CategoryCode { get; set; }
    public string? PrimaryUnitCode { get; set; }
    public string? SecondaryUnitCode { get; set; }
    public decimal? ConversionFactor { get; set; }
    public decimal? MinStockLevel { get; set; }
    public decimal? MaxStockLevel { get; set; }
    public decimal? ReorderLevel { get; set; }
    public int? ShelfLifeDays { get; set; }
    public decimal? StandardCost { get; set; }
    public decimal? LastPurchasePrice { get; set; }
    public decimal? DefaultSellingPrice { get; set; }
    public decimal? TaxRate { get; set; }
    public bool? IsTaxable { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Volume { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class ItemImportDifference
{
    public string FieldNameAr { get; set; } = "";
    public string ExistingValue { get; set; } = "";
    public string IncomingValue { get; set; } = "";
}

public sealed class ItemImportExistingItem
{
    public int ItemID { get; set; }
    public string? ItemCode { get; set; }
    public string? Barcode { get; set; }
    public string? ItemNameAr { get; set; }
    public string? ItemNameEn { get; set; }
    public int CategoryID { get; set; }
    public string? CategoryCode { get; set; }
    public int ItemType { get; set; }
    public int PrimaryUnitID { get; set; }
    public string? PrimaryUnitCode { get; set; }
    public int? SecondaryUnitID { get; set; }
    public string? SecondaryUnitCode { get; set; }
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
    public bool IsActive { get; set; }
}

public sealed class ItemImportPreviewRow
{
    public int RowNumber { get; set; }
    public ItemImportData Data { get; set; } = new();
    public ItemImportAction Action { get; set; }
    public ItemImportExistingItem? ExistingItem { get; set; }
    public List<ItemImportDifference> Differences { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public bool HasConflict => ExistingItem is not null;
    public bool IsValid => Errors.Count == 0;
}

public sealed class ItemImportPreview
{
    public List<string> Errors { get; set; } = new();
    public List<ItemImportPreviewRow> Rows { get; set; } = new();
    public int ValidNewCount => Rows.Count(r => r.IsValid && !r.HasConflict);
    public int ConflictCount => Rows.Count(r => r.IsValid && r.HasConflict);
    public int InvalidCount => Rows.Count(r => !r.IsValid);
}

public sealed class ItemImportCommitResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
}
