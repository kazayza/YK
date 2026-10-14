using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace YKCoatings.Services
{
    public partial class MaterialIssueService
    {
        private const byte MaterialIssueStockTransactionType = 2;
        private const string MaterialIssueStockSourceType = "MI";
        private const decimal StockQuantityTolerance = 0.0001m;

        private sealed class IssueItemUnitMap
        {
            public int PrimaryUnitID { get; set; }
            public int? SecondaryUnitID { get; set; }
            public decimal? ConversionFactor { get; set; }
        }

        private sealed class IssueBalanceForPosting
        {
            public decimal CurrentQty { get; set; }
            public decimal ReservedQty { get; set; }
        }

        private sealed class IssueInventoryMovement
        {
            public int TransactionID { get; set; }
            public DateTime TransactionDate { get; set; }
            public decimal Quantity { get; set; }
            public decimal? UnitCost { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
        }

        private sealed class IssueBatchBalance
        {
            public int BatchBalanceID { get; set; }
            public string BatchNumber { get; set; } = "";
            public DateTime? ExpiryDate { get; set; }
            public DateTime? ReceiptDate { get; set; }
            public decimal CurrentQty { get; set; }
            public decimal? UnitCost { get; set; }
        }

        private sealed class IssueFifoLayer
        {
            public int? SourceTransactionID { get; set; }
            public int? BatchBalanceID { get; set; }
            public DateTime ReceivedAt { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public decimal UnitCost { get; set; }
            public decimal RemainingQty { get; set; }
        }

        private sealed class PreparedIssueLine
        {
            public required IssueApproveDetailDto Detail { get; init; }
            public required int PrimaryUnitID { get; init; }
            public required decimal ToPrimaryFactor { get; init; }
            public required decimal PrimaryQuantity { get; init; }
        }

        private sealed class IssueLayerConsumption
        {
            public required PreparedIssueLine Prepared { get; init; }
            public required IssueFifoLayer Layer { get; init; }
            public required decimal PrimaryQuantity { get; init; }
            public required decimal IssueUnitQuantity { get; init; }
            public required decimal LineCost { get; init; }
            public required decimal BalanceBefore { get; init; }
            public required decimal BalanceAfter { get; init; }
            public required decimal CostBefore { get; init; }
            public required decimal CostAfter { get; init; }
            public int IssueDetailID { get; set; }
        }

        private async Task PostIssueStockFifoAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            IssueApproveHeadDto issue,
            int userId,
            IReadOnlyList<IssueApproveDetailDto> details)
        {
            var preparedLines = new List<PreparedIssueLine>(details.Count);
            foreach (var detail in details)
            {
                if (detail.IssueDetailID <= 0 || detail.ItemID <= 0 || detail.UnitID <= 0 || detail.IssuedQty <= 0)
                    throw new InvalidOperationException($"بيانات غير صالحة في سطر إذن الصرف {detail.LineNumber}.");
                if (!detail.POMaterialID.HasValue || detail.POMaterialID.Value <= 0)
                    throw new InvalidOperationException($"سطر الصرف {detail.LineNumber} غير مرتبط بمادة أمر تصنيع؛ لم يُخصم المخزون.");

                var unitMap = await connection.QueryFirstOrDefaultAsync<IssueItemUnitMap>(
                    @"SELECT PrimaryUnitID, SecondaryUnitID, ConversionFactor
                      FROM dbo.Items WITH (UPDLOCK, HOLDLOCK)
                      WHERE ItemID=@ItemID AND IsActive=1",
                    new { detail.ItemID }, transaction);
                if (unitMap is null || unitMap.PrimaryUnitID <= 0)
                    throw new InvalidOperationException($"لا توجد وحدة مخزون أساسية صالحة للصنف {detail.ItemID}.");

                var factor = await ResolveIssueToPrimaryUnitFactorAsync(
                    connection, transaction, unitMap, detail.UnitID);
                var primaryQuantity = decimal.Round(
                    detail.IssuedQty * factor, 4, MidpointRounding.AwayFromZero);
                if (primaryQuantity <= 0)
                    throw new InvalidOperationException($"كمية سطر الصرف {detail.LineNumber} أصغر من دقة وحدة المخزون الأساسية.");

                preparedLines.Add(new PreparedIssueLine
                {
                    Detail = detail,
                    PrimaryUnitID = unitMap.PrimaryUnitID,
                    ToPrimaryFactor = factor,
                    PrimaryQuantity = primaryQuantity
                });
            }

            var consumptions = new List<IssueLayerConsumption>();
            var layerSources = new Dictionary<int, bool>();
            var itemGroups = preparedLines
                .GroupBy(x => new { x.Detail.ItemID, x.PrimaryUnitID })
                .OrderBy(x => x.Key.ItemID)
                .ToList();

            foreach (var group in itemGroups)
            {
                var itemId = group.Key.ItemID;
                var primaryUnitId = group.Key.PrimaryUnitID;
                var orderedLines = group
                    .OrderBy(x => x.Detail.LineNumber)
                    .ThenBy(x => x.Detail.IssueDetailID)
                    .ToList();
                var requestedPrimaryQty = orderedLines.Sum(x => x.PrimaryQuantity);

                var balance = await connection.QueryFirstOrDefaultAsync<IssueBalanceForPosting>(
                    @"SELECT CurrentQty, ISNULL(ReservedQty,0) AS ReservedQty
                      FROM dbo.InventoryBalance WITH (UPDLOCK, HOLDLOCK)
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID",
                    new { ItemID = itemId, WarehouseID = issue.WarehouseID }, transaction);
                if (balance is null)
                    throw new InvalidOperationException($"لا يوجد رصيد للصنف {itemId} في المخزن المحدد.");
                if (balance.CurrentQty < 0 || balance.ReservedQty < 0 || balance.ReservedQty > balance.CurrentQty)
                    throw new InvalidOperationException($"رصيد الصنف {itemId} غير متزن؛ أوقفنا الترحيل.");

                var availableQty = balance.CurrentQty - balance.ReservedQty;
                if (requestedPrimaryQty - availableQty > StockQuantityTolerance)
                    throw new InvalidOperationException(
                        $"الرصيد المتاح للصنف {itemId} غير كافٍ. المتاح {availableQty:#,##0.####} والمطلوب {requestedPrimaryQty:#,##0.####}.");

                var layerSet = await LoadIssueFifoLayersAsync(
                    connection, transaction, itemId, issue.WarehouseID, balance.CurrentQty);
                var layers = layerSet.Layers;
                if (Math.Abs(layers.Sum(x => x.RemainingQty) - balance.CurrentQty) > StockQuantityTolerance)
                    throw new InvalidOperationException($"طبقات FIFO لا تطابق رصيد الصنف {itemId}؛ لم يُخصم أي مخزون.");

                layerSources[itemId] = layerSet.FromBatchBalances;
                var runningBalance = balance.CurrentQty;
                var runningAverageCost = CalculateIssueLayerAverageCost(layers);

                foreach (var prepared in orderedLines)
                {
                    var quantityToAllocate = prepared.PrimaryQuantity;
                    var issueQtyRemaining = prepared.Detail.IssuedQty;
                    var availableLayers = layers
                        .Where(x => x.RemainingQty > StockQuantityTolerance)
                        .OrderBy(x => x.ReceivedAt)
                        .ThenBy(x => x.SourceTransactionID ?? int.MaxValue)
                        .ThenBy(x => x.BatchBalanceID ?? int.MaxValue)
                        .ToList();

                    foreach (var layer in availableLayers)
                    {
                        if (quantityToAllocate <= StockQuantityTolerance) break;
                        var allocatedPrimaryQty = Math.Min(quantityToAllocate, layer.RemainingQty);
                        if (allocatedPrimaryQty <= 0) continue;
                        if (layer.UnitCost <= 0)
                            throw new InvalidOperationException(
                                $"طبقة FIFO للصنف {itemId} في المخزن {issue.WarehouseID} بلا تكلفة موجبة؛ أوقفنا الاعتماد.");

                        var isLastAllocation = quantityToAllocate - allocatedPrimaryQty <= StockQuantityTolerance;
                        var issueUnitQty = isLastAllocation
                            ? issueQtyRemaining
                            : decimal.Round(allocatedPrimaryQty / prepared.ToPrimaryFactor, 4, MidpointRounding.AwayFromZero);
                        if (issueUnitQty <= 0 || issueUnitQty > issueQtyRemaining)
                            throw new InvalidOperationException($"تعذر تمثيل توزيع FIFO بدقة وحدة السطر {prepared.Detail.LineNumber}.");

                        var balanceBefore = runningBalance;
                        var costBefore = runningAverageCost;
                        layer.RemainingQty = decimal.Round(
                            layer.RemainingQty - allocatedPrimaryQty, 4, MidpointRounding.AwayFromZero);
                        quantityToAllocate = decimal.Round(
                            quantityToAllocate - allocatedPrimaryQty, 4, MidpointRounding.AwayFromZero);
                        issueQtyRemaining = decimal.Round(issueQtyRemaining - issueUnitQty, 4, MidpointRounding.AwayFromZero);
                        runningBalance = decimal.Round(
                            runningBalance - allocatedPrimaryQty, 4, MidpointRounding.AwayFromZero);
                        runningAverageCost = CalculateIssueLayerAverageCost(layers);

                        consumptions.Add(new IssueLayerConsumption
                        {
                            Prepared = prepared,
                            Layer = layer,
                            PrimaryQuantity = allocatedPrimaryQty,
                            IssueUnitQuantity = issueUnitQty,
                            LineCost = decimal.Round(allocatedPrimaryQty * layer.UnitCost, 8, MidpointRounding.AwayFromZero),
                            BalanceBefore = balanceBefore,
                            BalanceAfter = runningBalance,
                            CostBefore = costBefore,
                            CostAfter = runningAverageCost
                        });
                    }

                    if (quantityToAllocate > StockQuantityTolerance || issueQtyRemaining > StockQuantityTolerance)
                        throw new InvalidOperationException($"تعذر تخصيص طبقات FIFO كاملة لسطر الصرف {prepared.Detail.LineNumber}.");
                }

                var finalQty = decimal.Round(balance.CurrentQty - requestedPrimaryQty, 4, MidpointRounding.AwayFromZero);
                var finalAverageCost = CalculateIssueLayerAverageCost(layers);
                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.InventoryBalance
                      SET CurrentQty=@CurrentQty,
                          AverageCost=@AverageCost,
                          LastMovementDate=GETDATE(),
                          ModifiedDate=GETDATE()
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID
                        AND CurrentQty=@StartingQty
                        AND ISNULL(ReservedQty,0)=@ReservedQty",
                    new
                    {
                        CurrentQty = finalQty,
                        AverageCost = finalAverageCost,
                        ItemID = itemId,
                        WarehouseID = issue.WarehouseID,
                        StartingQty = balance.CurrentQty,
                        ReservedQty = balance.ReservedQty
                    }, transaction);
                if (updated != 1)
                    throw new InvalidOperationException($"تغير رصيد الصنف {itemId} أثناء اعتماد إذن الصرف؛ أوقفنا الترحيل.");
            }

            // A detail can consume more than one FIFO lot. Split that row so the issue
            // records the exact lot, expiry and cost for every movement without a schema change.
            var nextLineNumber = await connection.ExecuteScalarAsync<int>(
                @"SELECT ISNULL(MAX(LineNumber),0)
                  FROM dbo.MaterialIssueDetails WITH (UPDLOCK,HOLDLOCK)
                  WHERE IssueID=@IssueID",
                new { IssueID = issue.IssueID }, transaction);

            foreach (var prepared in preparedLines.OrderBy(x => x.Detail.LineNumber).ThenBy(x => x.Detail.IssueDetailID))
            {
                var lineConsumptions = consumptions
                    .Where(x => x.Prepared.Detail.IssueDetailID == prepared.Detail.IssueDetailID)
                    .ToList();
                if (lineConsumptions.Count == 0)
                    throw new InvalidOperationException($"لم تُسجل تكلفة FIFO لسطر الصرف {prepared.Detail.LineNumber}.");

                for (var index = 0; index < lineConsumptions.Count; index++)
                {
                    var consumption = lineConsumptions[index];
                    var unitCost = decimal.Round(
                        consumption.LineCost / consumption.IssueUnitQuantity, 4, MidpointRounding.AwayFromZero);
                    if (unitCost <= 0)
                        throw new InvalidOperationException($"تعذر حفظ تكلفة FIFO لسطر الصرف {prepared.Detail.LineNumber}.");

                    if (index == 0)
                    {
                        var changed = await connection.ExecuteAsync(
                            @"UPDATE dbo.MaterialIssueDetails
                              SET IssuedQty=@IssuedQty,
                                  UnitCost=@UnitCost,
                                  LineCost=@LineCost,
                                  BatchNumber=@BatchNumber,
                                  ExpiryDate=@ExpiryDate
                              WHERE IssueDetailID=@IssueDetailID AND IssueID=@IssueID",
                            new
                            {
                                IssuedQty = consumption.IssueUnitQuantity,
                                UnitCost = unitCost,
                                LineCost = consumption.LineCost,
                                consumption.Layer.BatchNumber,
                                consumption.Layer.ExpiryDate,
                                consumption.Prepared.Detail.IssueDetailID,
                                IssueID = issue.IssueID
                            }, transaction);
                        if (changed != 1)
                            throw new InvalidOperationException($"تعذر تحديث تكلفة سطر الصرف {prepared.Detail.LineNumber}.");
                        consumption.IssueDetailID = prepared.Detail.IssueDetailID;
                    }
                    else
                    {
                        nextLineNumber++;
                        consumption.IssueDetailID = await connection.ExecuteScalarAsync<int>(
                            @"INSERT INTO dbo.MaterialIssueDetails
                              (IssueID,LineNumber,POMaterialID,ItemID,UnitID,IssuedQty,UnitCost,LineCost,BatchNumber,ExpiryDate,Notes)
                              VALUES
                              (@IssueID,@LineNumber,@POMaterialID,@ItemID,@UnitID,@IssuedQty,@UnitCost,@LineCost,@BatchNumber,@ExpiryDate,@Notes);
                              SELECT CAST(SCOPE_IDENTITY() AS INT);",
                            new
                            {
                                IssueID = issue.IssueID,
                                LineNumber = nextLineNumber,
                                consumption.Prepared.Detail.POMaterialID,
                                consumption.Prepared.Detail.ItemID,
                                consumption.Prepared.Detail.UnitID,
                                IssuedQty = consumption.IssueUnitQuantity,
                                UnitCost = unitCost,
                                LineCost = consumption.LineCost,
                                consumption.Layer.BatchNumber,
                                consumption.Layer.ExpiryDate,
                                consumption.Prepared.Detail.Notes
                            }, transaction);
                    }
                }
            }

            foreach (var consumption in consumptions)
            {
                var affected = await connection.ExecuteAsync(
                    @"INSERT INTO dbo.InventoryTransactions
                      (TransactionDate,TransactionType,ItemID,WarehouseID,Quantity,UnitID,UnitCost,
                       BalanceBefore,BalanceAfter,CostBefore,CostAfter,BatchNumber,ExpiryDate,
                       SourceDocType,SourceDocID,SourceDocNumber,SourceDocLineID,Notes,CreatedBy,CreatedDate)
                      VALUES
                      (GETDATE(),@TransactionType,@ItemID,@WarehouseID,@Quantity,@UnitID,@UnitCost,
                       @BalanceBefore,@BalanceAfter,@CostBefore,@CostAfter,@BatchNumber,@ExpiryDate,
                       @SourceDocType,@SourceDocID,@SourceDocNumber,@SourceDocLineID,@Notes,@UserID,GETDATE())",
                    new
                    {
                        TransactionType = MaterialIssueStockTransactionType,
                        ItemID = consumption.Prepared.Detail.ItemID,
                        WarehouseID = issue.WarehouseID,
                        Quantity = -consumption.PrimaryQuantity,
                        UnitID = consumption.Prepared.PrimaryUnitID,
                        consumption.Layer.UnitCost,
                        consumption.BalanceBefore,
                        consumption.BalanceAfter,
                        consumption.CostBefore,
                        consumption.CostAfter,
                        consumption.Layer.BatchNumber,
                        consumption.Layer.ExpiryDate,
                        SourceDocType = MaterialIssueStockSourceType,
                        SourceDocID = issue.IssueID,
                        SourceDocNumber = issue.IssueNumber,
                        SourceDocLineID = consumption.IssueDetailID,
                        Notes = $"FIFO; إذن الصرف {issue.IssueNumber}",
                        UserID = userId
                    }, transaction);
                if (affected != 1)
                    throw new InvalidOperationException("تعذر تسجيل حركة صرف FIFO؛ أُلغيت العملية كاملة.");
            }

            foreach (var group in itemGroups)
            {
                var itemId = group.Key.ItemID;
                var itemConsumptions = consumptions.Where(x => x.Prepared.Detail.ItemID == itemId).ToList();
                if (layerSources[itemId])
                {
                    foreach (var lot in itemConsumptions
                                 .Where(x => x.Layer.BatchBalanceID.HasValue)
                                 .GroupBy(x => x.Layer.BatchBalanceID!.Value))
                    {
                        var quantity = lot.Sum(x => x.PrimaryQuantity);
                        var affected = await connection.ExecuteAsync(
                            @"UPDATE dbo.InventoryBatchBalance
                              SET CurrentQty=CurrentQty-@Quantity, ModifiedDate=GETDATE()
                              WHERE BatchBalanceID=@BatchBalanceID AND ItemID=@ItemID AND WarehouseID=@WarehouseID
                                AND CurrentQty>=@Quantity",
                            new
                            {
                                Quantity = quantity,
                                BatchBalanceID = lot.Key,
                                ItemID = itemId,
                                WarehouseID = issue.WarehouseID
                            }, transaction);
                        if (affected != 1)
                            throw new InvalidOperationException($"تعذر تحديث رصيد باتش الصنف {itemId}؛ لم يكتمل اعتماد إذن الصرف.");
                    }
                }
                else
                {
                    foreach (var lot in itemConsumptions
                                 .Where(x => !string.IsNullOrWhiteSpace(x.Layer.BatchNumber))
                                 .GroupBy(x => x.Layer.BatchNumber!, StringComparer.OrdinalIgnoreCase))
                    {
                        var quantity = lot.Sum(x => x.PrimaryQuantity);
                        var batch = await connection.QueryFirstOrDefaultAsync<IssueBatchBalance>(
                            @"SELECT TOP (1) BatchBalanceID, BatchNumber, CurrentQty
                              FROM dbo.InventoryBatchBalance WITH (UPDLOCK,HOLDLOCK)
                              WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID AND BatchNumber=@BatchNumber
                              ORDER BY BatchBalanceID",
                            new { ItemID = itemId, WarehouseID = issue.WarehouseID, BatchNumber = lot.Key }, transaction);
                        if (batch is null)
                            throw new InvalidOperationException($"طبقة FIFO للباتش {lot.Key} بلا رصيد باتش مقابل؛ أُلغيت العملية.");
                        var affected = await connection.ExecuteAsync(
                            @"UPDATE dbo.InventoryBatchBalance
                              SET CurrentQty=CurrentQty-@Quantity, ModifiedDate=GETDATE()
                              WHERE BatchBalanceID=@BatchBalanceID AND CurrentQty>=@Quantity",
                            new { Quantity = quantity, BatchBalanceID = batch.BatchBalanceID }, transaction);
                        if (affected != 1)
                            throw new InvalidOperationException($"رصيد الباتش {lot.Key} لا يطابق حركة FIFO للصنف {itemId}.");
                    }
                }
            }
        }

        private static async Task<decimal> ResolveIssueToPrimaryUnitFactorAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            IssueItemUnitMap item,
            int unitId)
        {
            if (unitId == item.PrimaryUnitID) return 1m;
            if (item.SecondaryUnitID == unitId && item.ConversionFactor.GetValueOrDefault() > 0)
                return item.ConversionFactor!.Value;

            var direct = await connection.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT TOP (1) ConversionFactor
                  FROM dbo.UnitConversions
                  WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
                  ORDER BY ConversionID DESC",
                new { FromUnitID = unitId, ToUnitID = item.PrimaryUnitID }, transaction);
            if (direct.GetValueOrDefault() > 0) return direct!.Value;

            var reverse = await connection.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT TOP (1) ConversionFactor
                  FROM dbo.UnitConversions
                  WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
                  ORDER BY ConversionID DESC",
                new { FromUnitID = item.PrimaryUnitID, ToUnitID = unitId }, transaction);
            if (reverse.GetValueOrDefault() > 0) return 1m / reverse!.Value;

            throw new InvalidOperationException(
                $"لا يوجد تحويل وحدة صريح بين وحدة سطر الصرف والوحدة الأساسية للصنف {item.PrimaryUnitID}؛ لم يُجر تحويل صامت.");
        }

        private async Task<(List<IssueFifoLayer> Layers, bool FromBatchBalances)> LoadIssueFifoLayersAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            int itemId,
            int warehouseId,
            decimal currentQty)
        {
            string? ledgerFailure = null;
            List<IssueFifoLayer>? ledgerLayers = null;
            var ledgerIsConsistent = false;
            var movements = (await connection.QueryAsync<IssueInventoryMovement>(
                @"SELECT TransactionID,TransactionDate,Quantity,UnitCost,BatchNumber,ExpiryDate
                  FROM dbo.InventoryTransactions WITH (UPDLOCK,HOLDLOCK)
                  WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID
                  ORDER BY TransactionDate,TransactionID",
                new { ItemID = itemId, WarehouseID = warehouseId }, transaction)).ToList();

            try
            {
                ledgerLayers = ReplayIssueInventoryLedger(movements);
                var ledgerQty = ledgerLayers.Sum(x => x.RemainingQty);
                ledgerIsConsistent = Math.Abs(ledgerQty - currentQty) <= StockQuantityTolerance;
                if (!ledgerIsConsistent)
                    ledgerFailure = $"صافي سجل الحركات {ledgerQty:#,##0.####} لا يطابق الرصيد {currentQty:#,##0.####}";
            }
            catch (InvalidOperationException ex)
            {
                ledgerFailure = ex.Message;
            }

            // Prefer exact current lot balances when present: they carry the BatchBalanceID
            // required for a safe decrement. If lot tracking exists but does not reconcile
            // to the aggregate balance, fail closed instead of silently leaving stale lots.
            var batches = (await connection.QueryAsync<IssueBatchBalance>(
                @"SELECT BatchBalanceID,BatchNumber,ExpiryDate,ReceiptDate,CurrentQty,UnitCost
                  FROM dbo.InventoryBatchBalance WITH (UPDLOCK,HOLDLOCK)
                  WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID
                  ORDER BY CASE WHEN ReceiptDate IS NULL THEN 0 ELSE 1 END,ReceiptDate,BatchBalanceID",
                new { ItemID = itemId, WarehouseID = warehouseId }, transaction)).ToList();
            if (batches.Any(x => x.CurrentQty < -StockQuantityTolerance))
                throw new InvalidOperationException($"يوجد رصيد باتش سالب للصنف {itemId}؛ أوقفنا الترحيل.");

            var batchQty = batches.Sum(x => Math.Max(0m, x.CurrentQty));
            var batchBalancesMatch = batches.Count > 0 &&
                Math.Abs(batchQty - currentQty) <= StockQuantityTolerance;
            if (batchBalancesMatch)
            {
                return (batches.Where(x => x.CurrentQty > StockQuantityTolerance).Select(x => new IssueFifoLayer
                {
                    BatchBalanceID = x.BatchBalanceID,
                    ReceivedAt = x.ReceiptDate ?? DateTime.MinValue,
                    BatchNumber = x.BatchNumber,
                    ExpiryDate = x.ExpiryDate,
                    UnitCost = x.UnitCost ?? 0m,
                    RemainingQty = x.CurrentQty
                }).ToList(), true);
            }

            if (batches.Count > 0)
            {
                var batchFailure = $"مجموع أرصدة الباتش {batchQty:#,##0.####} لا يطابق الرصيد {currentQty:#,##0.####}";
                throw new InvalidOperationException(
                    $"تعذر اعتماد الصنف {itemId} في المخزن {warehouseId}: {ledgerFailure ?? (ledgerIsConsistent ? "سجل الحركات متزن لكن أرصدة الباتش لا تتطابق" : "سجل الحركات غير صالح")}؛ {batchFailure}. سوِّ الأرصدة أولاً؛ لم يُخصم أي مخزون.");
            }

            if (ledgerIsConsistent && ledgerLayers is not null)
                return (ledgerLayers, false);

            throw new InvalidOperationException(
                $"تعذر اعتماد الصنف {itemId} في المخزن {warehouseId}: تعذر مطابقة طبقات FIFO مع الرصيد. " +
                $"{ledgerFailure ?? "سجل الحركات غير صالح"}؛ لا توجد أرصدة باتش بديلة. سوِّ الأرصدة أولاً؛ لم يُخصم أي مخزون.");
        }

        private static List<IssueFifoLayer> ReplayIssueInventoryLedger(List<IssueInventoryMovement> movements)
        {
            var layers = new List<IssueFifoLayer>();
            foreach (var movement in movements)
            {
                if (movement.Quantity > StockQuantityTolerance)
                {
                    layers.Add(new IssueFifoLayer
                    {
                        SourceTransactionID = movement.TransactionID,
                        ReceivedAt = movement.TransactionDate,
                        BatchNumber = movement.BatchNumber,
                        ExpiryDate = movement.ExpiryDate,
                        UnitCost = movement.UnitCost ?? 0m,
                        RemainingQty = movement.Quantity
                    });
                }
                else if (movement.Quantity < -StockQuantityTolerance)
                {
                    var quantityToConsume = -movement.Quantity;
                    quantityToConsume = !string.IsNullOrWhiteSpace(movement.BatchNumber)
                        ? ConsumeIssueLayers(layers, quantityToConsume, x =>
                            string.Equals(x.BatchNumber, movement.BatchNumber, StringComparison.OrdinalIgnoreCase))
                        : ConsumeIssueLayers(layers, quantityToConsume, _ => true);
                    if (quantityToConsume > StockQuantityTolerance)
                        throw new InvalidOperationException(
                            $"حركة المخزون رقم {movement.TransactionID} تسحب كمية أكبر من طبقات الوارد أو لا تطابق الباتش {movement.BatchNumber ?? "—"}.");
                }
            }

            return layers
                .Where(x => x.RemainingQty > StockQuantityTolerance)
                .OrderBy(x => x.ReceivedAt)
                .ThenBy(x => x.SourceTransactionID ?? int.MaxValue)
                .ToList();
        }

        private static decimal ConsumeIssueLayers(
            List<IssueFifoLayer> layers,
            decimal quantityToConsume,
            Func<IssueFifoLayer, bool> predicate)
        {
            foreach (var layer in layers
                         .Where(x => x.RemainingQty > StockQuantityTolerance && predicate(x))
                         .OrderBy(x => x.ReceivedAt)
                         .ThenBy(x => x.SourceTransactionID ?? int.MaxValue)
                         .ThenBy(x => x.BatchBalanceID ?? int.MaxValue))
            {
                if (quantityToConsume <= StockQuantityTolerance) break;
                var consumed = Math.Min(quantityToConsume, layer.RemainingQty);
                layer.RemainingQty = decimal.Round(layer.RemainingQty - consumed, 4, MidpointRounding.AwayFromZero);
                quantityToConsume = decimal.Round(quantityToConsume - consumed, 4, MidpointRounding.AwayFromZero);
            }
            return quantityToConsume;
        }

        private static decimal CalculateIssueLayerAverageCost(IEnumerable<IssueFifoLayer> layers)
        {
            var activeLayers = layers.Where(x => x.RemainingQty > StockQuantityTolerance).ToList();
            var quantity = activeLayers.Sum(x => x.RemainingQty);
            if (quantity <= StockQuantityTolerance) return 0m;
            return decimal.Round(
                activeLayers.Sum(x => x.RemainingQty * x.UnitCost) / quantity,
                4, MidpointRounding.AwayFromZero);
        }
    }
}
