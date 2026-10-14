using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace YKCoatings.Services
{
    public partial class SalesInvoiceService
    {
        private const byte SalesInvoiceStockTransactionType = 4;
        private const string SalesInvoiceStockSourceType = "SINV";
        private const decimal StockQuantityTolerance = 0.0001m;

        private sealed class ItemUnitMap
        {
            public int PrimaryUnitID { get; set; }
            public int? SecondaryUnitID { get; set; }
            public decimal? ConversionFactor { get; set; }
        }

        private sealed class InvoicePostingLine
        {
            public int InvoiceDetailID { get; set; }
            public int LineNumber { get; set; }
            public int ItemID { get; set; }
            public int UnitID { get; set; }
            public int EffectiveWarehouseID { get; set; }
            public int PrimaryUnitID { get; set; }
            public int? SecondaryUnitID { get; set; }
            public decimal? ConversionFactor { get; set; }
            public decimal Quantity { get; set; }
        }

        private sealed class InventoryBalanceForPosting
        {
            public decimal CurrentQty { get; set; }
            public decimal ReservedQty { get; set; }
        }

        private sealed class InventoryTransactionForFifo
        {
            public int TransactionID { get; set; }
            public DateTime TransactionDate { get; set; }
            public decimal Quantity { get; set; }
            public decimal? UnitCost { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
        }

        private sealed class BatchBalanceForFifo
        {
            public int BatchBalanceID { get; set; }
            public string BatchNumber { get; set; } = "";
            public DateTime? ExpiryDate { get; set; }
            public DateTime? ReceiptDate { get; set; }
            public decimal CurrentQty { get; set; }
            public decimal? UnitCost { get; set; }
        }

        private sealed class FifoCostLayer
        {
            public int? SourceTransactionID { get; set; }
            public int? BatchBalanceID { get; set; }
            public DateTime ReceivedAt { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public decimal UnitCost { get; set; }
            public decimal RemainingQty { get; set; }
        }

        private sealed class PreparedInvoiceLine
        {
            public required InvoicePostingLine Line { get; init; }
            public decimal PrimaryQuantity { get; init; }
        }

        private sealed class LayerConsumption
        {
            public required FifoCostLayer Layer { get; init; }
            public required PreparedInvoiceLine InvoiceLine { get; init; }
            public decimal Quantity { get; init; }
            public decimal BalanceBefore { get; init; }
            public decimal BalanceAfter { get; init; }
            public decimal CostBefore { get; init; }
            public decimal CostAfter { get; init; }
        }

        private async Task<decimal> ResolveToPrimaryUnitFactorAsync(
            SqlConnection connection,
            IDbTransaction? transaction,
            int itemId,
            int unitId)
        {
            var item = await connection.QueryFirstOrDefaultAsync<ItemUnitMap>(
                @"SELECT PrimaryUnitID, SecondaryUnitID, ConversionFactor
                  FROM dbo.Items WHERE ItemID=@ItemID",
                new { ItemID = itemId }, transaction);

            if (item == null || item.PrimaryUnitID <= 0)
                throw new InvalidOperationException($"تعذر تحديد وحدة المخزون الأساسية للصنف {itemId}.");

            return await ResolveToPrimaryUnitFactorAsync(connection, transaction, item, unitId);
        }

        private async Task<decimal> ResolveToPrimaryUnitFactorAsync(
            SqlConnection connection,
            IDbTransaction? transaction,
            ItemUnitMap item,
            int unitId)
        {
            if (unitId == item.PrimaryUnitID) return 1m;

            // Item-level conversion follows the Units screen convention: one FromUnit equals factor ToUnits.
            if (item.SecondaryUnitID == unitId && item.ConversionFactor.GetValueOrDefault() > 0)
                return item.ConversionFactor!.Value;

            var directFactor = await connection.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT TOP (1) ConversionFactor
                  FROM dbo.UnitConversions
                  WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
                  ORDER BY ConversionID DESC",
                new { FromUnitID = unitId, ToUnitID = item.PrimaryUnitID }, transaction);
            if (directFactor.GetValueOrDefault() > 0) return directFactor!.Value;

            var reverseFactor = await connection.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT TOP (1) ConversionFactor
                  FROM dbo.UnitConversions
                  WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
                  ORDER BY ConversionID DESC",
                new { FromUnitID = item.PrimaryUnitID, ToUnitID = unitId }, transaction);
            if (reverseFactor.GetValueOrDefault() > 0) return 1m / reverseFactor!.Value;

            throw new InvalidOperationException(
                $"لا يوجد تحويل وحدة صالح للصنف بين الوحدة المختارة والوحدة الأساسية (الصنف {item.PrimaryUnitID}).");
        }

        private async Task PostInvoiceStockFifoAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            int invoiceId,
            string invoiceNumber,
            int userId)
        {
            var lines = (await connection.QueryAsync<InvoicePostingLine>(
                @"SELECT d.InvoiceDetailID, d.LineNumber, d.ItemID, d.UnitID,
                         COALESCE(d.WarehouseID,si.WarehouseID) AS EffectiveWarehouseID,
                         d.Quantity, i.PrimaryUnitID, i.SecondaryUnitID, i.ConversionFactor
                  FROM dbo.SalesInvoiceDetails d WITH (UPDLOCK, HOLDLOCK)
                  INNER JOIN dbo.SalesInvoices si ON si.InvoiceID=d.InvoiceID
                  INNER JOIN dbo.Items i ON i.ItemID=d.ItemID
                  WHERE d.InvoiceID=@InvoiceID
                  ORDER BY d.ItemID, COALESCE(d.WarehouseID,si.WarehouseID), d.LineNumber, d.InvoiceDetailID",
                new { InvoiceID = invoiceId }, transaction)).ToList();

            if (lines.Count == 0) throw new InvalidOperationException("لا يمكن ترحيل فاتورة بلا تفاصيل.");

            var preparedLines = new List<PreparedInvoiceLine>(lines.Count);
            foreach (var line in lines)
            {
                if (line.ItemID <= 0 || line.UnitID <= 0 || line.Quantity <= 0)
                    throw new InvalidOperationException($"بيانات كمية أو وحدة غير صالحة في سطر الفاتورة {line.LineNumber}.");
                if (line.EffectiveWarehouseID <= 0)
                    throw new InvalidOperationException($"حدد مخزناً لسطر الفاتورة {line.LineNumber}.");
                if (line.PrimaryUnitID <= 0)
                    throw new InvalidOperationException($"لا توجد وحدة مخزون أساسية للصنف {line.ItemID}.");

                var unitMap = new ItemUnitMap
                {
                    PrimaryUnitID = line.PrimaryUnitID,
                    SecondaryUnitID = line.SecondaryUnitID,
                    ConversionFactor = line.ConversionFactor
                };
                var factor = await ResolveToPrimaryUnitFactorAsync(connection, transaction, unitMap, line.UnitID);
                var primaryQty = decimal.Round(line.Quantity * factor, 4, MidpointRounding.AwayFromZero);
                if (primaryQty <= 0)
                    throw new InvalidOperationException($"كمية سطر الفاتورة {line.LineNumber} أصغر من دقة المخزون الأساسية.");

                preparedLines.Add(new PreparedInvoiceLine
                {
                    Line = line,
                    PrimaryQuantity = primaryQty
                });
            }

            var groups = preparedLines
                .GroupBy(x => new { x.Line.ItemID, x.Line.EffectiveWarehouseID, x.Line.PrimaryUnitID })
                .OrderBy(x => x.Key.ItemID)
                .ThenBy(x => x.Key.EffectiveWarehouseID)
                .ToList();

            foreach (var group in groups)
            {
                var itemId = group.Key.ItemID;
                var warehouseId = group.Key.EffectiveWarehouseID;
                var primaryUnitId = group.Key.PrimaryUnitID;
                var orderedLines = group.OrderBy(x => x.Line.LineNumber).ThenBy(x => x.Line.InvoiceDetailID).ToList();
                var requestedQty = orderedLines.Sum(x => x.PrimaryQuantity);

                var balance = await connection.QueryFirstOrDefaultAsync<InventoryBalanceForPosting>(
                    @"SELECT CurrentQty, ISNULL(ReservedQty,0) AS ReservedQty
                      FROM dbo.InventoryBalance WITH (UPDLOCK, HOLDLOCK)
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID",
                    new { ItemID = itemId, WarehouseID = warehouseId }, transaction);

                if (balance == null)
                    throw new InvalidOperationException($"لا يوجد رصيد للصنف {itemId} في المخزن {warehouseId}.");
                if (balance.CurrentQty < 0 || balance.ReservedQty < 0 || balance.ReservedQty > balance.CurrentQty)
                    throw new InvalidOperationException($"رصيد الصنف {itemId} في المخزن {warehouseId} غير متزن؛ أوقفنا الترحيل.");

                var availableQty = balance.CurrentQty - balance.ReservedQty;
                if (requestedQty - availableQty > StockQuantityTolerance)
                    throw new InvalidOperationException(
                        $"الرصيد المتاح للصنف {itemId} في المخزن {warehouseId} غير كافٍ. المتاح {availableQty:#,##0.####} والمطلوب {requestedQty:#,##0.####}.");

                var layerSet = await LoadFifoLayersAsync(
                    connection, transaction, itemId, warehouseId, balance.CurrentQty);
                var layers = layerSet.Layers;
                var remainingLayers = layers.Sum(x => x.RemainingQty);
                if (Math.Abs(remainingLayers - balance.CurrentQty) > StockQuantityTolerance)
                    throw new InvalidOperationException($"طبقات التكلفة لا تطابق رصيد الصنف {itemId} في المخزن {warehouseId}.");

                var startingBalance = balance.CurrentQty;
                var runningBalance = startingBalance;
                var runningAverageCost = CalculateLayerAverageCost(layers);
                var consumptions = new List<LayerConsumption>();

                foreach (var prepared in orderedLines)
                {
                    var quantityToAllocate = prepared.PrimaryQuantity;
                    var lineCost = 0m;

                    foreach (var layer in layers
                                 .Where(x => x.RemainingQty > StockQuantityTolerance)
                                 .OrderBy(x => x.ReceivedAt)
                                 .ThenBy(x => x.SourceTransactionID ?? int.MaxValue)
                                 .ThenBy(x => x.BatchBalanceID ?? int.MaxValue)
                                 .ToList())
                    {
                        if (quantityToAllocate <= StockQuantityTolerance) break;

                        var allocated = Math.Min(quantityToAllocate, layer.RemainingQty);
                        if (allocated <= 0) continue;
                        if (layer.UnitCost <= 0)
                            throw new InvalidOperationException(
                                $"طبقة FIFO للصنف {itemId} في المخزن {warehouseId} بلا تكلفة موجبة؛ صحّح تكلفة الوارد قبل اعتماد الفاتورة.");

                        var balanceBefore = runningBalance;
                        var costBefore = runningAverageCost;
                        layer.RemainingQty = decimal.Round(layer.RemainingQty - allocated, 4, MidpointRounding.AwayFromZero);
                        quantityToAllocate = decimal.Round(quantityToAllocate - allocated, 4, MidpointRounding.AwayFromZero);
                        runningBalance = decimal.Round(runningBalance - allocated, 4, MidpointRounding.AwayFromZero);
                        runningAverageCost = CalculateLayerAverageCost(layers);
                        lineCost += allocated * layer.UnitCost;
                        consumptions.Add(new LayerConsumption
                        {
                            Layer = layer,
                            InvoiceLine = prepared,
                            Quantity = allocated,
                            BalanceBefore = balanceBefore,
                            BalanceAfter = runningBalance,
                            CostBefore = costBefore,
                            CostAfter = runningAverageCost
                        });
                    }

                    if (quantityToAllocate > StockQuantityTolerance)
                        throw new InvalidOperationException($"تعذر تخصيص طبقات FIFO كاملة لسطر الفاتورة {prepared.Line.LineNumber}.");

                    var invoiceUnitCost = decimal.Round(lineCost / prepared.Line.Quantity, 4, MidpointRounding.AwayFromZero);
                    var detailUpdated = await connection.ExecuteAsync(
                        @"UPDATE dbo.SalesInvoiceDetails
                          SET UnitCost=@UnitCost
                          WHERE InvoiceDetailID=@InvoiceDetailID AND InvoiceID=@InvoiceID",
                        new
                        {
                            UnitCost = invoiceUnitCost,
                            prepared.Line.InvoiceDetailID,
                            InvoiceID = invoiceId
                        }, transaction);
                    if (detailUpdated != 1)
                        throw new InvalidOperationException($"تعذر حفظ تكلفة FIFO لسطر الفاتورة {prepared.Line.LineNumber}.");
                }

                foreach (var consumption in consumptions)
                {
                    var layerReference = consumption.Layer.SourceTransactionID.HasValue
                        ? $"FIFO transaction {consumption.Layer.SourceTransactionID.Value}"
                        : consumption.Layer.BatchBalanceID.HasValue
                            ? $"FIFO batch balance {consumption.Layer.BatchBalanceID.Value}"
                            : "FIFO layer";

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.InventoryTransactions
                          (TransactionDate, TransactionType, ItemID, WarehouseID, Quantity, UnitID, UnitCost,
                           BalanceBefore, BalanceAfter, CostBefore, CostAfter, BatchNumber, ExpiryDate,
                           SourceDocType, SourceDocID, SourceDocNumber, SourceDocLineID, Notes, CreatedBy, CreatedDate)
                          VALUES
                          (GETDATE(), @TransactionType, @ItemID, @WarehouseID, @Quantity, @UnitID, @UnitCost,
                           @BalanceBefore, @BalanceAfter, @CostBefore, @CostAfter, @BatchNumber, @ExpiryDate,
                           @SourceDocType, @SourceDocID, @SourceDocNumber, @SourceDocLineID, @Notes, @UserID, GETDATE())",
                        new
                        {
                            TransactionType = SalesInvoiceStockTransactionType,
                            ItemID = itemId,
                            WarehouseID = warehouseId,
                            Quantity = -consumption.Quantity,
                            UnitID = primaryUnitId,
                            consumption.Layer.UnitCost,
                            consumption.BalanceBefore,
                            consumption.BalanceAfter,
                            consumption.CostBefore,
                            consumption.CostAfter,
                            consumption.Layer.BatchNumber,
                            consumption.Layer.ExpiryDate,
                            SourceDocType = SalesInvoiceStockSourceType,
                            SourceDocID = invoiceId,
                            SourceDocNumber = invoiceNumber,
                            SourceDocLineID = consumption.InvoiceLine.Line.InvoiceDetailID,
                            Notes = layerReference,
                            UserID = userId
                        }, transaction);
                }

                if (layerSet.FromBatchBalances)
                {
                    foreach (var batchConsumption in consumptions
                                 .Where(x => x.Layer.BatchBalanceID.HasValue)
                                 .GroupBy(x => x.Layer.BatchBalanceID!.Value))
                    {
                        var quantity = batchConsumption.Sum(x => x.Quantity);
                        var affected = await connection.ExecuteAsync(
                            @"UPDATE dbo.InventoryBatchBalance
                              SET CurrentQty=CurrentQty-@Quantity, ModifiedDate=GETDATE()
                              WHERE BatchBalanceID=@BatchBalanceID AND ItemID=@ItemID AND WarehouseID=@WarehouseID
                                AND CurrentQty>=@Quantity",
                            new
                            {
                                Quantity = quantity,
                                BatchBalanceID = batchConsumption.Key,
                                ItemID = itemId,
                                WarehouseID = warehouseId
                            }, transaction);
                        if (affected != 1)
                            throw new InvalidOperationException($"تعذر تحديث رصيد الباتش للصنف {itemId} في المخزن {warehouseId}.");
                    }
                }
                else
                {
                    foreach (var batchConsumption in consumptions
                                 .Where(x => !string.IsNullOrWhiteSpace(x.Layer.BatchNumber))
                                 .GroupBy(x => x.Layer.BatchNumber!, StringComparer.OrdinalIgnoreCase))
                    {
                        var quantity = batchConsumption.Sum(x => x.Quantity);
                        var batch = await connection.QueryFirstOrDefaultAsync<BatchBalanceForFifo>(
                            @"SELECT TOP (1) BatchBalanceID, BatchNumber, CurrentQty
                              FROM dbo.InventoryBatchBalance WITH (UPDLOCK, HOLDLOCK)
                              WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID AND BatchNumber=@BatchNumber
                              ORDER BY BatchBalanceID",
                            new { ItemID = itemId, WarehouseID = warehouseId, BatchNumber = batchConsumption.Key }, transaction);
                        if (batch == null)
                            throw new InvalidOperationException($"طبقة FIFO للباتش {batchConsumption.Key} لا تملك رصيداً مقابلاً في سجل الباتش؛ لم يُعتمد البيع.");
                        var affected = await connection.ExecuteAsync(
                            @"UPDATE dbo.InventoryBatchBalance
                              SET CurrentQty=CurrentQty-@Quantity, ModifiedDate=GETDATE()
                              WHERE BatchBalanceID=@BatchBalanceID AND CurrentQty>=@Quantity",
                            new { Quantity = quantity, BatchBalanceID = batch.BatchBalanceID }, transaction);
                        if (affected != 1)
                            throw new InvalidOperationException($"رصيد الباتش {batchConsumption.Key} لا يطابق حركة FIFO للصنف {itemId}.");
                    }
                }

                var finalBalance = decimal.Round(startingBalance - requestedQty, 4, MidpointRounding.AwayFromZero);
                var finalAverageCost = CalculateLayerAverageCost(layers);
                var balanceUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.InventoryBalance
                      SET CurrentQty=@CurrentQty, AverageCost=@AverageCost,
                          LastMovementDate=GETDATE(), ModifiedDate=GETDATE()
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID AND CurrentQty=@StartingBalance",
                    new
                    {
                        CurrentQty = finalBalance,
                        AverageCost = finalAverageCost,
                        ItemID = itemId,
                        WarehouseID = warehouseId,
                        StartingBalance = startingBalance
                    }, transaction);
                if (balanceUpdated != 1)
                    throw new InvalidOperationException($"تغير رصيد الصنف {itemId} أثناء ترحيل الفاتورة؛ أوقفنا الاعتماد.");
            }
        }

        private async Task<(List<FifoCostLayer> Layers, bool FromBatchBalances)> LoadFifoLayersAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            int itemId,
            int warehouseId,
            decimal currentQty)
        {
            string? ledgerFailure = null;
            var movements = (await connection.QueryAsync<InventoryTransactionForFifo>(
                @"SELECT TransactionID, TransactionDate, Quantity, UnitCost, BatchNumber, ExpiryDate
                  FROM dbo.InventoryTransactions WITH (UPDLOCK, HOLDLOCK)
                  WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID
                  ORDER BY TransactionDate, TransactionID",
                new { ItemID = itemId, WarehouseID = warehouseId }, transaction)).ToList();

            try
            {
                var ledgerLayers = ReplayInventoryLedger(movements);
                var ledgerQty = ledgerLayers.Sum(x => x.RemainingQty);
                if (Math.Abs(ledgerQty - currentQty) <= StockQuantityTolerance)
                    return (ledgerLayers, false);
                ledgerFailure = $"صافي سجل الحركات {ledgerQty:#,##0.####} لا يطابق الرصيد {currentQty:#,##0.####}";
            }
            catch (InvalidOperationException ex)
            {
                ledgerFailure = ex.Message;
            }

            var batches = (await connection.QueryAsync<BatchBalanceForFifo>(
                @"SELECT BatchBalanceID, BatchNumber, ExpiryDate, ReceiptDate, CurrentQty, UnitCost
                  FROM dbo.InventoryBatchBalance WITH (UPDLOCK, HOLDLOCK)
                  WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID AND CurrentQty>0
                  ORDER BY CASE WHEN ReceiptDate IS NULL THEN 0 ELSE 1 END, ReceiptDate, BatchBalanceID",
                new { ItemID = itemId, WarehouseID = warehouseId }, transaction)).ToList();
            var batchQty = batches.Sum(x => x.CurrentQty);
            if (batches.Count > 0 && Math.Abs(batchQty - currentQty) <= StockQuantityTolerance)
            {
                var batchLayers = batches.Select(x => new FifoCostLayer
                {
                    BatchBalanceID = x.BatchBalanceID,
                    ReceivedAt = x.ReceiptDate ?? DateTime.MinValue,
                    BatchNumber = x.BatchNumber,
                    ExpiryDate = x.ExpiryDate,
                    UnitCost = x.UnitCost ?? 0m,
                    RemainingQty = x.CurrentQty
                }).ToList();
                return (batchLayers, true);
            }

            var batchFailure = batches.Count == 0
                ? "لا توجد طبقات باتش متاحة"
                : $"مجموع أرصدة الباتش {batchQty:#,##0.####} لا يطابق الرصيد {currentQty:#,##0.####}";
            throw new InvalidOperationException(
                $"تعذر اعتماد الصنف {itemId} في المخزن {warehouseId}: تعذر مطابقة طبقات FIFO مع الرصيد الحالي. " +
                $"{ledgerFailure ?? "سجل الحركات غير صالح"}؛ {batchFailure}. سوِّ سجل الحركة/الباتش أولاً؛ لم يُخصم أي مخزون.");
        }

        private static List<FifoCostLayer> ReplayInventoryLedger(List<InventoryTransactionForFifo> movements)
        {
            var layers = new List<FifoCostLayer>();
            foreach (var movement in movements)
            {
                if (movement.Quantity > StockQuantityTolerance)
                {
                    layers.Add(new FifoCostLayer
                    {
                        SourceTransactionID = movement.TransactionID,
                        ReceivedAt = movement.TransactionDate,
                        BatchNumber = movement.BatchNumber,
                        ExpiryDate = movement.ExpiryDate,
                        UnitCost = movement.UnitCost ?? 0m,
                        RemainingQty = movement.Quantity
                    });
                    continue;
                }

                if (movement.Quantity < -StockQuantityTolerance)
                {
                    var quantityToConsume = -movement.Quantity;
                    if (!string.IsNullOrWhiteSpace(movement.BatchNumber))
                    {
                        quantityToConsume = ConsumeFromLayers(
                            layers, quantityToConsume,
                            x => string.Equals(x.BatchNumber, movement.BatchNumber, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        quantityToConsume = ConsumeFromLayers(layers, quantityToConsume, _ => true);
                    }

                    if (quantityToConsume > StockQuantityTolerance)
                        throw new InvalidOperationException(
                            $"حركة المخزون رقم {movement.TransactionID} تسحب كمية أكبر من طبقات الوارد السابقة أو لا تطابق الباتش {movement.BatchNumber ?? "—"}.");
                }
            }

            return layers
                .Where(x => x.RemainingQty > StockQuantityTolerance)
                .OrderBy(x => x.ReceivedAt)
                .ThenBy(x => x.SourceTransactionID ?? int.MaxValue)
                .ToList();
        }

        private static decimal ConsumeFromLayers(
            List<FifoCostLayer> layers,
            decimal quantityToConsume,
            Func<FifoCostLayer, bool> predicate)
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

        private static decimal CalculateLayerAverageCost(IEnumerable<FifoCostLayer> layers)
        {
            var activeLayers = layers.Where(x => x.RemainingQty > StockQuantityTolerance).ToList();
            var quantity = activeLayers.Sum(x => x.RemainingQty);
            if (quantity <= StockQuantityTolerance) return 0m;
            var value = activeLayers.Sum(x => x.RemainingQty * x.UnitCost);
            return decimal.Round(value / quantity, 4, MidpointRounding.AwayFromZero);
        }
    }
}
