using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    /// <summary>
    /// Cost-card read, preview and snapshot persistence. CostType mapping is an explicit
    /// application assumption because the database only constrains values to 1 and 2:
    /// 1 = estimated cost from BOM (not a formally frozen standard); 2 = actual production-order issues.
    /// </summary>
    public sealed class CostCardService : BaseDbService
    {
        private const string ModuleCode = "SCR_COSTCARD";
        private readonly ModuleAuthorizationService _authorization;
        private readonly AuditService _audit;
        private readonly bool _snapshotPersistenceEnabled;

        /// <summary>
        /// Snapshot persistence stays off unless the business cost policy is approved and
        /// the deployment explicitly opts in via CostCard:EnableSnapshotPersistence=true.
        /// </summary>
        public bool SnapshotPersistenceEnabled => _snapshotPersistenceEnabled;

        public CostCardService(
            IConfiguration configuration,
            ModuleAuthorizationService authorization,
            AuditService audit) : base(configuration)
        {
            _authorization = authorization;
            _audit = audit;
            _snapshotPersistenceEnabled = bool.TryParse(
                configuration["CostCard:EnableSnapshotPersistence"], out var enabled) && enabled;
        }

        private class CostCardSourceHeader
        {
            public int SourceID { get; set; }
            public string SourceReference { get; set; } = "";
            public int ItemID { get; set; }
            public string ItemCode { get; set; } = "";
            public string ItemNameAr { get; set; } = "";
            public string UnitName { get; set; } = "";
            public decimal OutputQty { get; set; }
            public decimal WastePercent { get; set; }
            public decimal DirectLaborCost { get; set; }
            public decimal OtherOverheadCost { get; set; }
            public int SourceStatus { get; set; }
            public DateTime? SourceDate { get; set; }
        }

        private sealed class SourceMaterialCosts
        {
            public decimal RawMaterialCost { get; set; }
            public decimal FinishedComponentCost { get; set; }
            public decimal PackagingCost { get; set; }
            public decimal UnclassifiedMaterialCost { get; set; }
            public int ApprovedLineCount { get; set; }
        }

        private sealed class ActualOrderHeader : CostCardSourceHeader
        {
            public decimal ActualMaterialCost { get; set; }
            public decimal ReturnedQty { get; set; }
            public decimal WasteQty { get; set; }
        }

        public async Task<List<CostCardProductOptionDto>> GetProductsAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                ModuleCode, p => p.CanView, "لا تملك صلاحية عرض بطاقة التكلفة.");

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<CostCardProductOptionDto>(@"
SELECT i.ItemID, i.ItemCode, i.ItemNameAr, u.UnitNameAr AS UnitName
FROM dbo.Items i
LEFT JOIN dbo.Units u ON u.UnitID=i.PrimaryUnitID
WHERE i.IsActive=1 AND i.ItemType IN (3,4)
ORDER BY i.ItemNameAr, i.ItemCode;");
            return rows.ToList();
        }

        public async Task<List<CostCardPeriodOptionDto>> GetOpenPeriodsAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                ModuleCode, p => p.CanView, "لا تملك صلاحية عرض بطاقة التكلفة.");

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<CostCardPeriodOptionDto>(@"
SELECT PeriodID, PeriodName, StartDate, EndDate
FROM dbo.AccountingPeriods
WHERE PeriodStatus=1
ORDER BY CASE WHEN CAST(GETDATE() AS date) BETWEEN StartDate AND EndDate THEN 0 ELSE 1 END,
         StartDate DESC, PeriodNumber DESC;");
            return rows.ToList();
        }

        public async Task<List<CostCardSourceOptionDto>> GetEstimatedSourcesAsync(int itemId)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                ModuleCode, p => p.CanView, "لا تملك صلاحية عرض بطاقة التكلفة.");
            if (itemId <= 0) return new();

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<CostCardSourceOptionDto>(@"
SELECT b.BOMID AS SourceID,
       b.BOMCode AS SourceCode,
       b.BOMName + N' — إصدار ' + CONVERT(nvarchar(12),b.BOMVersion) AS SourceName,
       b.OutputQty,
       u.UnitNameAr AS UnitName,
       b.EffectiveFrom AS SourceDate
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Units u ON u.UnitID=b.OutputUnitID
WHERE b.ProductItemID=@ItemID
  AND b.BOMStatus=2
  AND ISNULL(b.IsActive,1)=1
  AND (b.EffectiveFrom IS NULL OR b.EffectiveFrom<=CAST(GETDATE() AS date))
  AND (b.EffectiveTo IS NULL OR b.EffectiveTo>=CAST(GETDATE() AS date))
ORDER BY b.EffectiveFrom DESC, b.BOMVersion DESC, b.BOMID DESC;",
                new { ItemID = itemId });
            return rows.ToList();
        }

        public async Task<List<CostCardSourceOptionDto>> GetActualSourcesAsync(int itemId)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                ModuleCode, p => p.CanView, "لا تملك صلاحية عرض بطاقة التكلفة.");
            if (itemId <= 0) return new();

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<CostCardSourceOptionDto>(@"
SELECT po.ProductionOrderID AS SourceID,
       po.OrderNumber AS SourceCode,
       ISNULL(b.BOMName,N'أمر تصنيع مكتمل') AS SourceName,
       ISNULL(po.ActualQty,0) AS OutputQty,
       u.UnitNameAr AS UnitName,
       COALESCE(po.ActualEndDate,po.OrderDate) AS SourceDate
FROM dbo.ProductionOrders po
LEFT JOIN dbo.BillOfMaterials b ON b.BOMID=po.BOMID
INNER JOIN dbo.Units u ON u.UnitID=po.PlannedUnitID
WHERE po.ProductItemID=@ItemID
  AND po.OrderStatus=6
  AND ISNULL(po.ActualQty,0)>0
  AND EXISTS
      (SELECT 1 FROM dbo.MaterialIssueNotes mi
       WHERE mi.ProductionOrderID=po.ProductionOrderID AND mi.IssueStatus=2)
ORDER BY COALESCE(po.ActualEndDate,po.OrderDate) DESC, po.ProductionOrderID DESC;",
                new { ItemID = itemId });
            return rows.ToList();
        }

        public async Task<CostCardPreviewDto> PreviewEstimatedAsync(
            int itemId, int bomId, int? periodId, decimal targetProfitPercent)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                ModuleCode, p => p.CanView, "لا تملك صلاحية عرض بطاقة التكلفة.");
            ValidatePreviewInputs(itemId, periodId, targetProfitPercent);

            using var connection = CreateConnection();
            return await BuildEstimatedPreviewAsync(connection, null, itemId, bomId, periodId, targetProfitPercent);
        }

        public async Task<CostCardPreviewDto> PreviewActualAsync(
            int itemId, int productionOrderId, int? periodId, decimal targetProfitPercent)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                ModuleCode, p => p.CanView, "لا تملك صلاحية عرض بطاقة التكلفة.");
            ValidatePreviewInputs(itemId, periodId, targetProfitPercent);

            using var connection = CreateConnection();
            return await BuildActualPreviewAsync(connection, null, itemId, productionOrderId, periodId, targetProfitPercent);
        }

        public async Task<List<CostCardHistoryDto>> GetHistoryAsync(int? itemId = null, int? costType = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                ModuleCode, p => p.CanView, "لا تملك صلاحية عرض بطاقة التكلفة.");

            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<CostCardHistoryDto>(@"
SELECT TOP (100)
       c.CostCardID, c.ItemID, i.ItemCode, i.ItemNameAr,
       c.PeriodID, ap.PeriodName, c.CalculationDate, c.CostType,
       ISNULL(c.RawMaterialCost,0) AS RawMaterialCost,
       ISNULL(c.PackagingCost,0) AS PackagingCost,
       ISNULL(c.DirectLaborCost,0) AS DirectLaborCost,
       ISNULL(c.IndirectMaterialCost,0) AS IndirectMaterialCost,
       ISNULL(c.IndirectLaborCost,0) AS IndirectLaborCost,
       ISNULL(c.UtilityCost,0) AS UtilityCost,
       ISNULL(c.DepreciationCost,0) AS DepreciationCost,
       ISNULL(c.MaintenanceCost,0) AS MaintenanceCost,
       ISNULL(c.OtherOverheadCost,0) AS OtherOverheadCost,
       ISNULL(c.TotalDirectCost,0) AS TotalDirectCost,
       ISNULL(c.TotalIndirectCost,0) AS TotalIndirectCost,
       ISNULL(c.TotalCost,0) AS TotalCost,
       ISNULL(c.CostPerUnit,0) AS CostPerUnit,
       ISNULL(c.WastePercent,0) AS WastePercent,
       ISNULL(c.WasteCost,0) AS WasteCost,
       ISNULL(c.TargetProfitPercent,0) AS TargetProfitPercent,
       ISNULL(c.SuggestedPrice,0) AS SuggestedPrice,
       c.Notes
FROM dbo.ProductCostCards c
INNER JOIN dbo.Items i ON i.ItemID=c.ItemID
LEFT JOIN dbo.AccountingPeriods ap ON ap.PeriodID=c.PeriodID
WHERE (@ItemID IS NULL OR c.ItemID=@ItemID)
  AND (@CostType IS NULL OR c.CostType=@CostType)
ORDER BY c.CalculationDate DESC, c.CostCardID DESC;",
                new { ItemID = itemId, CostType = costType });
            return rows.ToList();
        }

        public async Task<int> SaveSnapshotAsync(CostCardSaveRequest request, int userId)
        {
            if (!SnapshotPersistenceEnabled)
                throw new InvalidOperationException(
                    "حفظ لقطات بطاقة التكلفة متوقف مؤقتًا؛ الشاشة للمعاينة فقط حتى اعتماد سياسة التكلفة ومصدر التتبع.");

            await _authorization.RequireUserPermissionAsync(
                userId, ModuleCode, p => p.CanAdd, "لا تملك صلاحية حفظ بطاقة تكلفة.");
            ValidatePreviewInputs(request.ItemID, request.PeriodID, request.TargetProfitPercent);
            if (request.CostType is not (1 or 2))
                throw new InvalidOperationException("نوع بطاقة التكلفة غير صالح.");
            if (request.CostType == 1 && (!request.BOMID.HasValue || request.BOMID.Value <= 0))
                throw new InvalidOperationException("اختر وصفة نشطة لحساب البطاقة التقديرية.");
            if (request.CostType == 2 && (!request.ProductionOrderID.HasValue || request.ProductionOrderID.Value <= 0))
                throw new InvalidOperationException("اختر أمر تصنيع مكتملًا لحساب البطاقة الفعلية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                if (request.PeriodID.HasValue)
                {
                    var periodStatus = await connection.ExecuteScalarAsync<int?>(@"
SELECT PeriodStatus FROM dbo.AccountingPeriods WITH (UPDLOCK,HOLDLOCK)
WHERE PeriodID=@PeriodID;",
                        new { request.PeriodID }, transaction);
                    if (periodStatus != 1)
                        throw new InvalidOperationException("الفترة المختارة غير موجودة أو مغلقة. اختر فترة مفتوحة.");
                }

                var preview = request.CostType == 1
                    ? await BuildEstimatedPreviewAsync(connection, transaction, request.ItemID,
                        request.BOMID!.Value, request.PeriodID, request.TargetProfitPercent)
                    : await BuildActualPreviewAsync(connection, transaction, request.ItemID,
                        request.ProductionOrderID!.Value, request.PeriodID, request.TargetProfitPercent);

                if (string.IsNullOrWhiteSpace(request.PreviewFingerprint) ||
                    !string.Equals(request.PreviewFingerprint, preview.PreviewFingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("تغيرت بيانات المصدر بعد المعاينة. أعد حساب المعاينة وراجع التنبيهات قبل الحفظ.");
                if (!preview.CanPersist)
                    throw new InvalidOperationException("لا يمكن حفظ البطاقة قبل معالجة موانع الحفظ الظاهرة في المعاينة.");
                if (preview.HasWarnings && !request.AcknowledgeWarnings)
                    throw new InvalidOperationException("راجع تحذيرات المعاينة وأكد الاطلاع عليها قبل الحفظ.");

                preview.SourceReference = NormalizeNotes(preview.SourceReference, preview.Warnings);
                var cardId = await connection.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ProductCostCards
(
    ItemID, PeriodID, CalculationDate,
    RawMaterialCost, PackagingCost, DirectLaborCost,
    IndirectMaterialCost, IndirectLaborCost, UtilityCost,
    DepreciationCost, MaintenanceCost, OtherOverheadCost,
    CostPerUnit, WastePercent, WasteCost,
    TargetProfitPercent, SuggestedPrice, CostType, Notes, CreatedBy
)
OUTPUT INSERTED.CostCardID
VALUES
(
    @ItemID, @PeriodID, CAST(GETDATE() AS date),
    @RawMaterialCost, @PackagingCost, @DirectLaborCost,
    @IndirectMaterialCost, @IndirectLaborCost, @UtilityCost,
    @DepreciationCost, @MaintenanceCost, @OtherOverheadCost,
    @CostPerUnit, @WastePercent, @WasteCost,
    @TargetProfitPercent, @SuggestedPrice, @CostType, @Notes, @CreatedBy
);",
                    new
                    {
                        preview.ItemID,
                        preview.PeriodID,
                        RawMaterialCost = preview.RawMaterialCost + preview.FinishedComponentCost,
                        preview.PackagingCost,
                        preview.DirectLaborCost,
                        preview.IndirectMaterialCost,
                        preview.IndirectLaborCost,
                        preview.UtilityCost,
                        preview.DepreciationCost,
                        preview.MaintenanceCost,
                        preview.OtherOverheadCost,
                        preview.CostPerUnit,
                        preview.WastePercent,
                        preview.WasteCost,
                        preview.TargetProfitPercent,
                        preview.SuggestedPrice,
                        preview.CostType,
                        Notes = preview.SourceReference,
                        CreatedBy = userId
                    }, transaction);

                transaction.Commit();
                await _audit.WriteAuditLogAsync(
                    userId, 1, "ProductCostCards", cardId.ToString(),
                    moduleName: ModuleCode,
                    description: $"حفظ بطاقة تكلفة {preview.CostTypeName} للصنف {preview.ItemCode} — المصدر {preview.SourceReference}");
                return cardId;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        private static void ValidatePreviewInputs(int itemId, int? periodId, decimal targetProfitPercent)
        {
            if (itemId <= 0) throw new InvalidOperationException("اختر الصنف أولًا.");
            if (periodId.HasValue && periodId.Value <= 0) throw new InvalidOperationException("الفترة المحاسبية غير صالحة.");
            if (targetProfitPercent < 0 || targetProfitPercent > 999.99m)
                throw new InvalidOperationException("نسبة الزيادة على التكلفة يجب أن تكون بين 0 و999.99%.");
        }

        private static async Task<CostCardPreviewDto> BuildEstimatedPreviewAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction? transaction,
            int itemId,
            int bomId,
            int? periodId,
            decimal targetProfitPercent)
        {
            if (bomId <= 0) throw new InvalidOperationException("معرّف الوصفة غير صالح.");

            const string sql = @"
SELECT b.BOMID AS SourceID,
       b.BOMCode + N' / إصدار ' + CONVERT(nvarchar(12),b.BOMVersion) AS SourceReference,
       b.ProductItemID AS ItemID,
       i.ItemCode,
       i.ItemNameAr,
       ou.UnitNameAr AS UnitName,
       b.OutputQty,
       ISNULL(b.WastePercent,0) AS WastePercent,
       ISNULL(b.LaborCostPerBatch,0) AS DirectLaborCost,
       ISNULL(b.OverheadCostPerBatch,0) AS OtherOverheadCost,
       b.BOMStatus AS SourceStatus,
       b.EffectiveFrom AS SourceDate,
       ISNULL(SUM(CASE WHEN bd.MaterialType=1 AND ci.ItemType=1
                       THEN (bd.Quantity*(1+ISNULL(bd.WastePercent,0)/100.0)*ISNULL(bd.UnitCost,0))
                       ELSE 0 END),0) AS RawMaterialCost,
       ISNULL(SUM(CASE WHEN (bd.MaterialType=3 AND ci.ItemType=4) OR (bd.MaterialType=1 AND ci.ItemType=4)
                       THEN (bd.Quantity*(1+ISNULL(bd.WastePercent,0)/100.0)*ISNULL(bd.UnitCost,0))
                       ELSE 0 END),0) AS FinishedComponentCost,
       ISNULL(SUM(CASE WHEN bd.MaterialType=2 AND ci.ItemType=2
                       THEN (bd.Quantity*(1+ISNULL(bd.WastePercent,0)/100.0)*ISNULL(bd.UnitCost,0))
                       ELSE 0 END),0) AS PackagingCost,
       ISNULL(SUM(CASE WHEN bd.MaterialType NOT IN (1,2,3)
                             OR (bd.MaterialType=1 AND ci.ItemType NOT IN (1,4))
                             OR (bd.MaterialType=2 AND ci.ItemType<>2)
                             OR (bd.MaterialType=3 AND ci.ItemType<>4)
                       THEN (bd.Quantity*(1+ISNULL(bd.WastePercent,0)/100.0)*ISNULL(bd.UnitCost,0))
                       ELSE 0 END),0) AS UnclassifiedMaterialCost
FROM dbo.BillOfMaterials b
INNER JOIN dbo.Items i ON i.ItemID=b.ProductItemID
INNER JOIN dbo.Units ou ON ou.UnitID=b.OutputUnitID
LEFT JOIN dbo.BOMDetails bd ON bd.BOMID=b.BOMID
LEFT JOIN dbo.Items ci ON ci.ItemID=bd.ItemID
WHERE b.BOMID=@BOMID
  AND b.ProductItemID=@ItemID
  AND b.BOMStatus=2
  AND ISNULL(b.IsActive,1)=1
  AND (b.EffectiveFrom IS NULL OR b.EffectiveFrom<=CAST(GETDATE() AS date))
  AND (b.EffectiveTo IS NULL OR b.EffectiveTo>=CAST(GETDATE() AS date))
GROUP BY b.BOMID,b.BOMCode,b.BOMVersion,b.ProductItemID,i.ItemCode,i.ItemNameAr,
         ou.UnitNameAr,b.OutputQty,b.WastePercent,b.LaborCostPerBatch,
         b.OverheadCostPerBatch,b.BOMStatus,b.EffectiveFrom;";

            var source = await connection.QueryFirstOrDefaultAsync<CostCardPreviewDto>(
                sql, new { BOMID = bomId, ItemID = itemId }, transaction);
            if (source is null)
                throw new InvalidOperationException("الوصفة المختارة غير موجودة أو غير نشطة/سارية لهذا الصنف.");

            source.CostType = 1;
            source.PeriodID = periodId;
            source.TargetProfitPercent = targetProfitPercent;
            source.WasteCost = 0m; // تفاصيل الوصفة تتضمن فاقد السطر؛ لا نضيفه مرة أخرى.
            RoundCostFields(source);
            source.CanPersist = source.OutputQty > 0 && source.UnclassifiedMaterialCost == 0;
            source.Warnings.Add("المواد غير المباشرة والعمالة غير المباشرة والطاقة والإهلاك والصيانة لا يوفّر لها BOM مصدرًا منفصلًا؛ تُحفظ هذه الخانات بصفر.");
            if (source.OutputQty <= 0)
                source.Warnings.Add("كمية مخرج الوصفة غير صالحة؛ لا يمكن حساب تكلفة الوحدة.");
            if (source.UnclassifiedMaterialCost > 0)
                source.Warnings.Add("تحتوي الوصفة على نوع مكوّن غير معروف لخريطة بطاقة التكلفة؛ لم يُضمّن في الإجمالي.");
            if (source.FinishedComponentCost > 0)
                source.Warnings.Add("تكلفة مكوّنات المنتج النهائي ظاهرة منفصلة في المعاينة، لكنها ستُجمع مع المواد المباشرة عند الحفظ لعدم وجود خانة مستقلة لها في ProductCostCards.");
            if (source.RawMaterialCost == 0 && source.FinishedComponentCost == 0 && source.PackagingCost == 0)
                source.Warnings.Add("تكلفة المواد في الوصفة تساوي صفرًا؛ راجع تكاليف مكونات الوصفة.");

            source.CostPerUnit = source.OutputQty > 0
                ? decimal.Round(source.TotalCost / source.OutputQty, 4, MidpointRounding.AwayFromZero)
                : 0m;
            source.SuggestedPrice = decimal.Round(
                source.CostPerUnit * (1m + targetProfitPercent / 100m), 4, MidpointRounding.AwayFromZero);
            source.Warnings = source.Warnings.Distinct().ToList();
            source.PreviewFingerprint = BuildPreviewFingerprint(source);
            return source;
        }

        private static async Task<CostCardPreviewDto> BuildActualPreviewAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction? transaction,
            int itemId,
            int productionOrderId,
            int? periodId,
            decimal targetProfitPercent)
        {
            if (productionOrderId <= 0) throw new InvalidOperationException("معرّف أمر التصنيع غير صالح.");

            var order = await connection.QueryFirstOrDefaultAsync<ActualOrderHeader>(@"
SELECT po.ProductionOrderID AS SourceID,
       po.OrderNumber AS SourceReference,
       po.ProductItemID AS ItemID,
       i.ItemCode,
       i.ItemNameAr,
       u.UnitNameAr AS UnitName,
       ISNULL(po.ActualQty,0) AS OutputQty,
       ISNULL(po.WasteQty,0) AS WasteQty,
       CASE WHEN ISNULL(po.ActualQty,0)+ISNULL(po.WasteQty,0)>0
            THEN ISNULL(po.WasteQty,0)*100.0/(ISNULL(po.ActualQty,0)+ISNULL(po.WasteQty,0))
            ELSE 0 END AS WastePercent,
       ISNULL(po.ActualLaborCost,0) AS DirectLaborCost,
       ISNULL(po.ActualOverheadCost,0) AS OtherOverheadCost,
       ISNULL(po.ActualMaterialCost,0) AS ActualMaterialCost,
       po.OrderStatus AS SourceStatus,
       COALESCE(po.ActualEndDate,po.OrderDate) AS SourceDate,
       ISNULL((SELECT SUM(ISNULL(pom.ReturnedQty,0))
               FROM dbo.ProductionOrderMaterials pom
               WHERE pom.ProductionOrderID=po.ProductionOrderID),0) AS ReturnedQty
FROM dbo.ProductionOrders po
INNER JOIN dbo.Items i ON i.ItemID=po.ProductItemID
INNER JOIN dbo.Units u ON u.UnitID=po.PlannedUnitID
WHERE po.ProductionOrderID=@ProductionOrderID
  AND po.ProductItemID=@ItemID
  AND po.OrderStatus=6
  AND ISNULL(po.ActualQty,0)>0
  AND EXISTS (SELECT 1 FROM dbo.MaterialIssueNotes mi
              WHERE mi.ProductionOrderID=po.ProductionOrderID AND mi.IssueStatus=2);",
                new { ProductionOrderID = productionOrderId, ItemID = itemId }, transaction);
            if (order is null)
                throw new InvalidOperationException("اختر أمرًا مكتملًا لهذا الصنف وله إذن صرف معتمد وناتج مقبول.");

            var materialCosts = await connection.QueryFirstOrDefaultAsync<SourceMaterialCosts>(@"
SELECT
    ISNULL(SUM(CASE WHEN pom.MaterialType=1 AND ci.ItemType=1
                    THEN ISNULL(mid.LineCost,mid.IssuedQty*ISNULL(mid.UnitCost,0)) ELSE 0 END),0) AS RawMaterialCost,
    ISNULL(SUM(CASE WHEN (pom.MaterialType=3 AND ci.ItemType=4) OR (pom.MaterialType=1 AND ci.ItemType=4)
                    THEN ISNULL(mid.LineCost,mid.IssuedQty*ISNULL(mid.UnitCost,0)) ELSE 0 END),0) AS FinishedComponentCost,
    ISNULL(SUM(CASE WHEN pom.MaterialType=2 AND ci.ItemType=2
                    THEN ISNULL(mid.LineCost,mid.IssuedQty*ISNULL(mid.UnitCost,0)) ELSE 0 END),0) AS PackagingCost,
    ISNULL(SUM(CASE WHEN pom.POMaterialID IS NULL OR pom.MaterialType NOT IN (1,2,3)
                          OR (pom.MaterialType=1 AND ci.ItemType NOT IN (1,4))
                          OR (pom.MaterialType=2 AND ci.ItemType<>2)
                          OR (pom.MaterialType=3 AND ci.ItemType<>4)
                    THEN ISNULL(mid.LineCost,mid.IssuedQty*ISNULL(mid.UnitCost,0)) ELSE 0 END),0) AS UnclassifiedMaterialCost,
    COUNT(*) AS ApprovedLineCount
FROM dbo.MaterialIssueNotes mi
INNER JOIN dbo.MaterialIssueDetails mid ON mid.IssueID=mi.IssueID
LEFT JOIN dbo.ProductionOrderMaterials pom ON pom.POMaterialID=mid.POMaterialID
INNER JOIN dbo.Items ci ON ci.ItemID=mid.ItemID
WHERE mi.ProductionOrderID=@ProductionOrderID
  AND mi.IssueStatus=2;",
                new { ProductionOrderID = productionOrderId }, transaction) ?? new SourceMaterialCosts();

            var preview = new CostCardPreviewDto
            {
                ItemID = order.ItemID,
                ItemCode = order.ItemCode,
                ItemNameAr = order.ItemNameAr,
                CostType = 2,
                PeriodID = periodId,
                SourceID = order.SourceID,
                SourceReference = order.SourceReference,
                UnitName = order.UnitName,
                OutputQty = order.OutputQty,
                WastePercent = decimal.Round(order.WastePercent, 2, MidpointRounding.AwayFromZero),
                WasteCost = 0m, // التكلفة الفعلية تُحمّل على الكمية المقبولة؛ لا نضيف تكلفة فاقد ثانية.
                RawMaterialCost = materialCosts.RawMaterialCost,
                FinishedComponentCost = materialCosts.FinishedComponentCost,
                PackagingCost = materialCosts.PackagingCost,
                DirectLaborCost = order.DirectLaborCost,
                OtherOverheadCost = order.OtherOverheadCost,
                UnclassifiedMaterialCost = materialCosts.UnclassifiedMaterialCost,
                ReturnedQty = order.ReturnedQty,
                HasApprovedIssueLines = materialCosts.ApprovedLineCount > 0,
                TargetProfitPercent = targetProfitPercent
            };
            RoundCostFields(preview);

            if (order.ReturnedQty > 0)
                preview.Warnings.Add($"تم تسجيل مرتجعات مواد بكمية {order.ReturnedQty:N4}؛ لا يمكن حساب صافي تكلفة المواد من بيانات الصرف المتاحة.");
            if (preview.UnclassifiedMaterialCost > 0)
                preview.Warnings.Add("توجد تكلفة صرف غير مرتبطة بسطر مادة مصنف؛ لم تُضمّن في إجمالي البطاقة.");
            if (preview.FinishedComponentCost > 0)
                preview.Warnings.Add("تكلفة مكوّنات المنتج النهائي ظاهرة منفصلة في المعاينة، لكنها ستُجمع مع المواد المباشرة عند الحفظ لعدم وجود خانة مستقلة لها في ProductCostCards.");
            if (materialCosts.ApprovedLineCount == 0)
                preview.Warnings.Add("لم يُعثر على أسطر صرف معتمدة للأمر.");
            preview.Warnings.Add("المواد غير المباشرة والعمالة غير المباشرة والطاقة والإهلاك والصيانة لا تُسحب من مصدر مستقل هنا؛ تُحفظ هذه الخانات بصفر.");
            if (materialCosts.ApprovedLineCount > 0 && materialCosts.RawMaterialCost + materialCosts.FinishedComponentCost + materialCosts.PackagingCost == 0)
                preview.Warnings.Add("مجموع تكلفة المواد من أسطر الصرف المعتمدة يساوي صفرًا؛ تحقق من بيانات التكلفة.");
            if (order.DirectLaborCost == 0)
                preview.Warnings.Add("العمالة الفعلية مسجلة بصفر في أمر التصنيع؛ تأكد أن الصفر مقصود قبل اعتماد البطاقة.");
            if (order.OtherOverheadCost == 0)
                preview.Warnings.Add("المصاريف الصناعية الفعلية مسجلة بصفر في أمر التصنيع؛ تأكد أن الصفر مقصود قبل اعتماد البطاقة.");
            preview.MaterialCostMismatch = Math.Abs(order.ActualMaterialCost -
                (materialCosts.RawMaterialCost + materialCosts.FinishedComponentCost +
                 materialCosts.PackagingCost + materialCosts.UnclassifiedMaterialCost)) > 0.01m;
            if (preview.MaterialCostMismatch)
                preview.Warnings.Add("إجمالي مواد الأمر لا يطابق مجموع أسطر أذون الصرف المعتمدة؛ راجع الفرق قبل الحفظ.");

            preview.CanPersist = preview.OutputQty > 0 &&
                                 preview.ReturnedQty == 0 &&
                                 preview.UnclassifiedMaterialCost == 0 &&
                                 !preview.MaterialCostMismatch &&
                                 preview.HasApprovedIssueLines;
            preview.CostPerUnit = preview.OutputQty > 0
                ? decimal.Round(preview.TotalCost / preview.OutputQty, 4, MidpointRounding.AwayFromZero)
                : 0m;
            preview.SuggestedPrice = decimal.Round(
                preview.CostPerUnit * (1m + targetProfitPercent / 100m), 4, MidpointRounding.AwayFromZero);
            preview.Warnings = preview.Warnings.Distinct().ToList();
            preview.PreviewFingerprint = BuildPreviewFingerprint(preview);
            return preview;
        }

        private static void RoundCostFields(CostCardPreviewDto preview)
        {
            static decimal R(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
            preview.RawMaterialCost = R(preview.RawMaterialCost);
            preview.FinishedComponentCost = R(preview.FinishedComponentCost);
            preview.PackagingCost = R(preview.PackagingCost);
            preview.DirectLaborCost = R(preview.DirectLaborCost);
            preview.IndirectMaterialCost = R(preview.IndirectMaterialCost);
            preview.IndirectLaborCost = R(preview.IndirectLaborCost);
            preview.UtilityCost = R(preview.UtilityCost);
            preview.DepreciationCost = R(preview.DepreciationCost);
            preview.MaintenanceCost = R(preview.MaintenanceCost);
            preview.OtherOverheadCost = R(preview.OtherOverheadCost);
        }

        private static string BuildPreviewFingerprint(CostCardPreviewDto preview)
        {
            static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
            var payload = string.Join("|", new[]
            {
                preview.CostType.ToString(CultureInfo.InvariantCulture),
                preview.ItemID.ToString(CultureInfo.InvariantCulture),
                preview.PeriodID?.ToString(CultureInfo.InvariantCulture) ?? "null",
                preview.SourceID.ToString(CultureInfo.InvariantCulture),
                preview.SourceReference,
                preview.UnitName,
                N(preview.OutputQty), N(preview.RawMaterialCost), N(preview.FinishedComponentCost),
                N(preview.PackagingCost), N(preview.DirectLaborCost), N(preview.OtherOverheadCost),
                N(preview.IndirectMaterialCost), N(preview.IndirectLaborCost), N(preview.UtilityCost),
                N(preview.DepreciationCost), N(preview.MaintenanceCost), N(preview.WastePercent),
                N(preview.WasteCost), N(preview.TargetProfitPercent), N(preview.CostPerUnit),
                N(preview.SuggestedPrice), N(preview.UnclassifiedMaterialCost), N(preview.ReturnedQty),
                preview.HasApprovedIssueLines.ToString(), preview.MaterialCostMismatch.ToString(),
                preview.CanPersist.ToString(), string.Join("~", preview.Warnings)
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        }

        private static string NormalizeNotes(string sourceReference, IEnumerable<string> warnings)
        {
            const int maxLength = 450;
            var text = $"مصدر: {sourceReference}. المكونات النهائية ضمن المواد المباشرة لعدم وجود خانة مستقلة. السعر المقترح زيادة على التكلفة، لا هامش من سعر البيع. الفاقد لم يُضف ثانية.";
            var warningList = warnings.Where(w => !string.IsNullOrWhiteSpace(w))
                .Take(3)
                .Select(w => w.Length <= 70 ? w : w[..67] + "…")
                .ToList();
            if (warningList.Count > 0)
                text += " تحذيرات تمت مراجعتها: " + string.Join(" | ", warningList);
            return text.Length <= maxLength ? text : text[..maxLength];
        }
    }
}
