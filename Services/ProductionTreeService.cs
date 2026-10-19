using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services;

/// <summary>Read-only recursive view of currently active, approved BOMs.</summary>
public sealed class ProductionTreeService : BaseDbService
{
    private const int MaximumDepth = 20;
    private readonly ModuleAuthorizationService _authorization;

    public ProductionTreeService(IConfiguration configuration, ModuleAuthorizationService authorization)
        : base(configuration)
    {
        _authorization = authorization;
    }

    public async Task<List<ProductionTreeBOMOptionDto>> GetApprovedActiveBomsAsync(int userId)
    {
        await _authorization.RequireUserPermissionAsync(
            userId, "SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض وصفات التصنيع.");

        using var connection = CreateConnection();
        var rows = await connection.QueryAsync<ProductionTreeBOMOptionDto>(@"
SELECT b.BOMID, b.BOMCode, b.BOMName,
       b.ProductItemID, i.ItemCode AS ProductCode, i.ItemNameAr AS ProductName,
       i.ItemType AS ProductItemType,
       b.BOMVersion, b.BatchSize, bu.UnitNameAr AS BatchUnitName,
       b.OutputQty, ou.UnitNameAr AS OutputUnitName,
       b.EffectiveFrom, b.EffectiveTo
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items i ON i.ItemID = b.ProductItemID
INNER JOIN dbo.Units bu ON bu.UnitID = b.BatchUnitID
INNER JOIN dbo.Units ou ON ou.UnitID = b.OutputUnitID
WHERE b.BOMStatus = 2 AND ISNULL(b.IsActive, 1) = 1
ORDER BY i.ItemCode, b.BOMVersion DESC, b.BOMID DESC");
        return rows.ToList();
    }

    public async Task<ProductionTreeResult> GetTreeAsync(int bomId, int userId)
    {
        await _authorization.RequireUserPermissionAsync(
            userId, "SCR_BOM", p => p.CanView, "لا تملك صلاحية عرض شجرة الإنتاج.");

        if (bomId <= 0)
            throw new ArgumentOutOfRangeException(nameof(bomId), "اختر وصفة صحيحة.");

        using var connection = CreateConnection();
        var rootBom = await connection.QueryFirstOrDefaultAsync<ProductionTreeBOMOptionDto>(@"
SELECT b.BOMID, b.BOMCode, b.BOMName,
       b.ProductItemID, i.ItemCode AS ProductCode, i.ItemNameAr AS ProductName,
       i.ItemType AS ProductItemType,
       b.BOMVersion, b.BatchSize, bu.UnitNameAr AS BatchUnitName,
       b.OutputQty, ou.UnitNameAr AS OutputUnitName,
       b.EffectiveFrom, b.EffectiveTo
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items i ON i.ItemID = b.ProductItemID
INNER JOIN dbo.Units bu ON bu.UnitID = b.BatchUnitID
INNER JOIN dbo.Units ou ON ou.UnitID = b.OutputUnitID
WHERE b.BOMID = @BOMID
  AND b.BOMStatus = 2
  AND ISNULL(b.IsActive, 1) = 1",
            new { BOMID = bomId });

        if (rootBom is null)
            throw new InvalidOperationException("الوصفة المختارة ليست نشطة ومعتمدة حالياً. حدّث القائمة واختر وصفة أخرى.");

        var root = CreateNode(rootBom);
        var path = new HashSet<int> { rootBom.ProductItemID };
        var headerCache = new Dictionary<int, ProductionTreeBOMOptionDto?>();
        var detailCache = new Dictionary<int, List<TreeLine>>();

        await ExpandAsync(connection, root, rootBom.BOMID, path, headerCache, detailCache, depth: 0);

        return new ProductionTreeResult
        {
            Root = root,
            QuantityScopeNote = "الكميات معروضة كما سُجلت في وصفة كل مستوى وعلى أساس كمية الدفعة/الإنتاج الموضحة معها؛ لا يتم تحويل الوحدات أو ضربها على كمية الوصفة الجذرية. لا تُعرض تكلفة ولا تُنشأ حركة."
        };
    }

    private async Task ExpandAsync(
        System.Data.IDbConnection connection,
        ProductionTreeNodeDto parent,
        int bomId,
        HashSet<int> currentPath,
        Dictionary<int, ProductionTreeBOMOptionDto?> headerCache,
        Dictionary<int, List<TreeLine>> detailCache,
        int depth)
    {
        if (!detailCache.TryGetValue(bomId, out var lines))
        {
            lines = (await connection.QueryAsync<TreeLine>(@"
SELECT bd.ItemID, i.ItemCode, i.ItemNameAr AS ItemName, i.ItemType,
       CASE WHEN bd.MaterialType = 1 AND i.ItemType = 4 THEN 3 ELSE bd.MaterialType END AS MaterialType,
       u.UnitNameAr AS UnitName,
       bd.Quantity,
       ISNULL(bd.WastePercent, 0) AS WastePercent,
       ISNULL(bd.IsOptional, 0) AS IsOptional,
       ISNULL(bd.StageNumber, 1) AS StageNumber,
       bd.Notes
FROM dbo.BOMDetails bd
INNER JOIN dbo.Items i ON i.ItemID = bd.ItemID
INNER JOIN dbo.Units u ON u.UnitID = bd.UnitID
WHERE bd.BOMID = @BOMID
ORDER BY ISNULL(bd.StageNumber, 1), bd.MaterialType, bd.LineNumber",
                new { BOMID = bomId })).ToList();
            detailCache[bomId] = lines;
        }

        if (lines.Count == 0)
            parent.ExpansionNote = "لا توجد مكونات مسجلة في هذه الوصفة.";

        foreach (var line in lines)
        {
            var child = new ProductionTreeNodeDto
            {
                ItemID = line.ItemID,
                ItemCode = line.ItemCode ?? "",
                ItemName = line.ItemName ?? "",
                ItemType = line.ItemType,
                ItemTypeName = GetItemTypeName(line.ItemType),
                Quantity = line.Quantity,
                UnitName = line.UnitName,
                WastePercent = line.WastePercent,
                IsOptional = line.IsOptional,
                StageNumber = line.StageNumber,
                Notes = line.Notes
            };
            parent.Components.Add(child);

            // The existing BOM editor treats a finished product component (logical type 3)
            // as the only recursively expandable component type. Semi-finished items remain leaves.
            if (line.ItemType != 4 || line.MaterialType != 3)
                continue;

            if (currentPath.Contains(line.ItemID))
            {
                child.ExpansionNote = "أُوقف التوسّع هنا لتجنّب دورة في الشجرة.";
                continue;
            }

            if (depth >= MaximumDepth)
            {
                child.ExpansionNote = $"بلغ عمق العرض الحد الوقائي ({MaximumDepth} مستويات).";
                continue;
            }

            if (!headerCache.TryGetValue(line.ItemID, out var childBom))
            {
                childBom = await connection.QueryFirstOrDefaultAsync<ProductionTreeBOMOptionDto>(@"
SELECT TOP (1) b.BOMID, b.BOMCode, b.BOMName,
       b.ProductItemID, i.ItemCode AS ProductCode, i.ItemNameAr AS ProductName,
       i.ItemType AS ProductItemType,
       b.BOMVersion, b.BatchSize, bu.UnitNameAr AS BatchUnitName,
       b.OutputQty, ou.UnitNameAr AS OutputUnitName,
       b.EffectiveFrom, b.EffectiveTo
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items i ON i.ItemID = b.ProductItemID
INNER JOIN dbo.Units bu ON bu.UnitID = b.BatchUnitID
INNER JOIN dbo.Units ou ON ou.UnitID = b.OutputUnitID
WHERE b.ProductItemID = @ProductItemID
  AND b.BOMStatus = 2
  AND ISNULL(b.IsActive, 1) = 1
ORDER BY b.BOMVersion DESC, b.BOMID DESC",
                    new { ProductItemID = line.ItemID });
                headerCache[line.ItemID] = childBom;
            }

            if (childBom is null)
            {
                child.ExpansionNote = "لا توجد وصفة نشطة ومعتمدة لهذا المنتج الفرعي.";
                continue;
            }

            ApplyBomMetadata(child, childBom);
            currentPath.Add(line.ItemID);
            await ExpandAsync(connection, child, childBom.BOMID, currentPath, headerCache, detailCache, depth + 1);
            currentPath.Remove(line.ItemID);
        }
    }

    private static ProductionTreeNodeDto CreateNode(ProductionTreeBOMOptionDto bom)
    {
        var node = new ProductionTreeNodeDto
        {
            ItemID = bom.ProductItemID,
            ItemCode = bom.ProductCode,
            ItemName = bom.ProductName,
            ItemType = bom.ProductItemType,
            ItemTypeName = GetItemTypeName(bom.ProductItemType)
        };
        ApplyBomMetadata(node, bom);
        return node;
    }

    private static void ApplyBomMetadata(ProductionTreeNodeDto node, ProductionTreeBOMOptionDto bom)
    {
        node.BOMCode = bom.BOMCode;
        node.BOMVersion = bom.BOMVersion;
        node.BatchSize = bom.BatchSize;
        node.BatchUnitName = bom.BatchUnitName;
        node.OutputQty = bom.OutputQty;
        node.OutputUnitName = bom.OutputUnitName;
        node.EffectiveFrom = bom.EffectiveFrom;
        node.EffectiveTo = bom.EffectiveTo;
    }

    private static string GetItemTypeName(int itemType) => itemType switch
    {
        1 => "مادة خام",
        2 => "مادة تعبئة",
        3 => "نصف مصنع",
        4 => "منتج تام",
        _ => "غير محدد"
    };

    public sealed class TreeLine
    {
        public int ItemID { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public int ItemType { get; set; }
        public int MaterialType { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal WastePercent { get; set; }
        public bool IsOptional { get; set; }
        public int StageNumber { get; set; }
        public string? Notes { get; set; }
    }
}
