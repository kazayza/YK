using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public partial class SalesReturnService
    {
        private const byte SalesReturnStockTransactionType = 6;
        private const string SalesReturnStockSourceType = "SRET";
        private const decimal ReturnStockTolerance = 0.0001m;
        private const string FifoSaleMarker = "FIFO_SALE_TXN:";

        private sealed class ReturnItemUnitMap
        {
            public int PrimaryUnitID { get; set; }
            public int? SecondaryUnitID { get; set; }
            public decimal? ConversionFactor { get; set; }
        }

        private sealed class OriginalSaleMovement
        {
            public int TransactionID { get; set; }
            public DateTime TransactionDate { get; set; }
            public decimal Quantity { get; set; }
            public int UnitID { get; set; }
            public decimal? UnitCost { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
        }

        private sealed class PriorReturnMovement
        {
            public int TransactionID { get; set; }
            public decimal Quantity { get; set; }
            public string? Notes { get; set; }
        }

        private sealed class ReturnCostAllocation
        {
            public int ReturnDetailID { get; set; }
            public int InvoiceDetailID { get; set; }
            public int ItemID { get; set; }
            public int ReturnWarehouseID { get; set; }
            public int PrimaryUnitID { get; set; }
            public int LineNumber { get; set; }
            public int SaleTransactionID { get; set; }
            public decimal Quantity { get; set; }
            public decimal UnitCost { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
        }

        private sealed class InventoryBalanceForReturn
        {
            public decimal CurrentQty { get; set; }
            public decimal ReservedQty { get; set; }
            public decimal? AverageCost { get; set; }
        }

        private sealed class InventoryBatchForReturn
        {
            public int BatchBalanceID { get; set; }
            public string BatchNumber { get; set; } = "";
            public DateTime? ExpiryDate { get; set; }
            public decimal CurrentQty { get; set; }
            public decimal? UnitCost { get; set; }
        }

        private sealed class AccountIdRow
        {
            public string AccountCode { get; set; } = "";
            public int AccountID { get; set; }
            public int AccountType { get; set; }
            public bool IsDetailAccount { get; set; }
            public bool IsActive { get; set; }
        }

        private sealed class ReturnAccountingPeriod
        {
            public int FiscalYearID { get; set; }
            public int PeriodID { get; set; }
        }

        private sealed class ReturnJournalLine
        {
            public int AccountID { get; init; }
            public decimal Debit { get; init; }
            public decimal Credit { get; init; }
            public string Description { get; init; } = "";
            public string? EntityType { get; init; }
            public int? EntityID { get; init; }
        }

        /// <summary>
        /// Adds saleable quantities back into the selected warehouse. Cost comes from the exact
        /// negative FIFO movements posted by the source invoice, not today's average/item cost.
        /// </summary>
        private async Task<decimal> PostReturnInventoryAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            ReturnState header,
            List<SalesReturnDetailDto> returnLines,
            InvoiceState invoice,
            int userId)
        {
            var allocations = new List<ReturnCostAllocation>();
            var invoiceLinesById = invoice.Lines.ToDictionary(x => x.InvoiceDetailID);

            foreach (var returnLine in returnLines
                         .OrderBy(x => x.LineNumber)
                         .ThenBy(x => x.ReturnDetailID))
            {
                if (!invoiceLinesById.TryGetValue(returnLine.InvoiceDetailID, out var sourceLine))
                    throw new InvalidOperationException("أحد أسطر المرتجع لم يعد مرتبطاً بسطر صحيح في الفاتورة الأصلية.");
                if (sourceLine.ItemID != returnLine.ItemID || sourceLine.UnitID != returnLine.UnitID)
                    throw new InvalidOperationException("الصنف أو الوحدة في المرتجع لا يطابقان السطر الأصلي؛ لم يتم ترحيل المخزون.");

                var unitMap = await connection.QueryFirstOrDefaultAsync<ReturnItemUnitMap>(
                    @"SELECT PrimaryUnitID, SecondaryUnitID, ConversionFactor
                      FROM dbo.Items WITH (UPDLOCK,HOLDLOCK) WHERE ItemID=@ItemID;",
                    new { ItemID = returnLine.ItemID }, transaction);
                if (unitMap == null || unitMap.PrimaryUnitID <= 0)
                    throw new InvalidOperationException($"تعذر تحديد وحدة المخزون الأساسية للصنف {returnLine.ItemID}.");

                var factor = await ResolveReturnToPrimaryUnitFactorAsync(
                    connection, transaction, unitMap, returnLine.UnitID);
                var requiredPrimaryQty = decimal.Round(
                    returnLine.Quantity * factor, 4, MidpointRounding.AwayFromZero);
                if (requiredPrimaryQty <= 0)
                    throw new InvalidOperationException($"كمية المرتجع للصنف {returnLine.ItemID} أصغر من دقة المخزون الأساسية.");

                var saleMovements = (await connection.QueryAsync<OriginalSaleMovement>(
                    @"SELECT TransactionID, TransactionDate, ABS(Quantity) AS Quantity,
                             UnitID, UnitCost, BatchNumber, ExpiryDate
                      FROM dbo.InventoryTransactions WITH (UPDLOCK,HOLDLOCK)
                      WHERE TransactionType=4 AND Quantity<0
                        AND SourceDocType IN (N'SINV',N'SI')
                        AND SourceDocLineID=@InvoiceDetailID
                        AND (SourceDocID=@InvoiceID OR SourceDocNumber=@InvoiceNumber)
                      ORDER BY TransactionDate,TransactionID;",
                    new
                    {
                        returnLine.InvoiceDetailID,
                        InvoiceID = invoice.InvoiceID,
                        InvoiceNumber = invoice.InvoiceNumber
                    }, transaction)).ToList();
                if (saleMovements.Count == 0)
                    throw new InvalidOperationException($"لا توجد حركات FIFO أصلية لسطر الفاتورة {sourceLine.LineNumber}.");
                if (saleMovements.Any(x => x.UnitCost.GetValueOrDefault() <= 0))
                    throw new InvalidOperationException($"إحدى طبقات تكلفة سطر الفاتورة {sourceLine.LineNumber} بلا تكلفة موجبة؛ لم يتم اعتماد المرتجع.");
                if (saleMovements.Any(x => x.UnitID != unitMap.PrimaryUnitID))
                    throw new InvalidOperationException($"حركة مخزون الفاتورة لسطر {sourceLine.LineNumber} ليست بوحدة المخزون الأساسية؛ يلزم تصحيحها أولاً.");

                var soldPrimaryQty = saleMovements.Sum(x => x.Quantity);
                var invoicedPrimaryQty = decimal.Round(
                    sourceLine.Quantity * factor, 4, MidpointRounding.AwayFromZero);
                if (Math.Abs(soldPrimaryQty - invoicedPrimaryQty) > ReturnStockTolerance)
                    throw new InvalidOperationException(
                        $"كمية حركات FIFO لسطر الفاتورة {sourceLine.LineNumber} ({soldPrimaryQty:#,##0.####}) لا تطابق كمية الفاتورة بوحدة المخزون ({invoicedPrimaryQty:#,##0.####}).");

                // Damaged/expired goods retain their original COGS, but their invoice source still
                // has to reconcile exactly before a return can be approved.
                if (returnLine.ItemCondition != 1) continue;

                var priorMovements = (await connection.QueryAsync<PriorReturnMovement>(
                    @"SELECT it.TransactionID,it.Quantity,it.Notes
                      FROM dbo.InventoryTransactions it WITH (UPDLOCK,HOLDLOCK)
                      INNER JOIN dbo.SalesReturns sr WITH (UPDLOCK,HOLDLOCK)
                        ON sr.ReturnID=it.SourceDocID AND it.SourceDocType=N'SRET'
                      INNER JOIN dbo.SalesReturnDetails rd WITH (UPDLOCK,HOLDLOCK)
                        ON rd.ReturnID=sr.ReturnID AND rd.ReturnDetailID=it.SourceDocLineID
                      WHERE it.TransactionType=6 AND it.Quantity>0
                        AND sr.ReturnStatus=3 AND sr.ReturnID<>@ReturnID
                        AND rd.InvoiceDetailID=@InvoiceDetailID
                      ORDER BY it.TransactionDate,it.TransactionID;",
                    new { ReturnID = header.ReturnID, returnLine.InvoiceDetailID }, transaction)).ToList();

                var availableBySaleTransaction = saleMovements.ToDictionary(x => x.TransactionID, x => x.Quantity);
                foreach (var prior in priorMovements)
                {
                    if (!TryParseSaleMovementId(prior.Notes, out var saleTransactionId))
                        throw new InvalidOperationException(
                            $"حركة المرتجع السابقة رقم {prior.TransactionID} لا تحفظ مرجع طبقة بيع FIFO؛ يلزم تسويتها يدوياً قبل اعتماد مرتجع إضافي.");
                    if (!availableBySaleTransaction.TryGetValue(saleTransactionId, out var remaining))
                        throw new InvalidOperationException(
                            $"حركة مرتجع سابقة تشير إلى حركة بيع FIFO غير موجودة ({saleTransactionId})؛ أوقفنا اعتماد المرتجع.");
                    remaining = decimal.Round(remaining - prior.Quantity, 4, MidpointRounding.AwayFromZero);
                    if (remaining < -ReturnStockTolerance)
                        throw new InvalidOperationException("إجمالي المرتجعات المعتمدة يتجاوز كمية طبقة البيع الأصلية.");
                    availableBySaleTransaction[saleTransactionId] = Math.Max(0m, remaining);
                }

                var stillToAllocate = requiredPrimaryQty;
                foreach (var sale in saleMovements)
                {
                    if (stillToAllocate <= ReturnStockTolerance) break;
                    var remaining = availableBySaleTransaction[sale.TransactionID];
                    if (remaining <= ReturnStockTolerance) continue;
                    var allocated = Math.Min(stillToAllocate, remaining);
                    allocations.Add(new ReturnCostAllocation
                    {
                        ReturnDetailID = returnLine.ReturnDetailID,
                        InvoiceDetailID = returnLine.InvoiceDetailID,
                        ItemID = returnLine.ItemID,
                        ReturnWarehouseID = header.WarehouseID,
                        PrimaryUnitID = unitMap.PrimaryUnitID,
                        LineNumber = returnLine.LineNumber,
                        SaleTransactionID = sale.TransactionID,
                        Quantity = allocated,
                        UnitCost = sale.UnitCost!.Value,
                        BatchNumber = sale.BatchNumber,
                        ExpiryDate = sale.ExpiryDate
                    });
                    availableBySaleTransaction[sale.TransactionID] = decimal.Round(
                        remaining - allocated, 4, MidpointRounding.AwayFromZero);
                    stillToAllocate = decimal.Round(
                        stillToAllocate - allocated, 4, MidpointRounding.AwayFromZero);
                }

                if (stillToAllocate > ReturnStockTolerance)
                    throw new InvalidOperationException(
                        $"الكمية السليمة من سطر الفاتورة {sourceLine.LineNumber} أكبر من كمية FIFO التي لم تُرجع سابقاً.");
            }

            if (allocations.Count == 0) return 0m;

            await ApplyReturnBatchesAsync(connection, transaction, allocations, header);
            var cost = await ApplyReturnInventoryBalanceAndMovementsAsync(
                connection, transaction, allocations, header, userId);
            return decimal.Round(cost, 2, MidpointRounding.AwayFromZero);
        }

        private async Task<decimal> ResolveReturnToPrimaryUnitFactorAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            ReturnItemUnitMap item,
            int unitId)
        {
            if (unitId == item.PrimaryUnitID) return 1m;
            if (item.SecondaryUnitID == unitId && item.ConversionFactor.GetValueOrDefault() > 0)
                return item.ConversionFactor!.Value;

            var direct = await connection.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT TOP (1) ConversionFactor
                  FROM dbo.UnitConversions
                  WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
                  ORDER BY ConversionID DESC;",
                new { FromUnitID = unitId, ToUnitID = item.PrimaryUnitID }, transaction);
            if (direct.GetValueOrDefault() > 0) return direct!.Value;

            var reverse = await connection.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT TOP (1) ConversionFactor
                  FROM dbo.UnitConversions
                  WHERE FromUnitID=@FromUnitID AND ToUnitID=@ToUnitID AND IsActive=1
                  ORDER BY ConversionID DESC;",
                new { FromUnitID = item.PrimaryUnitID, ToUnitID = unitId }, transaction);
            if (reverse.GetValueOrDefault() > 0) return 1m / reverse!.Value;

            throw new InvalidOperationException(
                $"لا يوجد تحويل وحدة صالح للصنف بين الوحدة {unitId} ووحدة المخزون الأساسية {item.PrimaryUnitID}.");
        }

        private static bool TryParseSaleMovementId(string? notes, out int saleTransactionId)
        {
            saleTransactionId = 0;
            if (string.IsNullOrWhiteSpace(notes)) return false;
            var markerIndex = notes.IndexOf(FifoSaleMarker, StringComparison.Ordinal);
            if (markerIndex < 0) return false;
            var start = markerIndex + FifoSaleMarker.Length;
            var end = start;
            while (end < notes.Length && char.IsDigit(notes[end])) end++;
            return end > start && int.TryParse(notes[start..end], out saleTransactionId) && saleTransactionId > 0;
        }

        private static async Task ApplyReturnBatchesAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            List<ReturnCostAllocation> allocations,
            ReturnState header)
        {
            var withBatch = allocations
                .Where(x => !string.IsNullOrWhiteSpace(x.BatchNumber))
                .GroupBy(x => new
                {
                    x.ItemID,
                    x.ReturnWarehouseID,
                    BatchNumber = x.BatchNumber!.Trim().ToUpperInvariant()
                })
                .OrderBy(x => x.Key.ItemID)
                .ThenBy(x => x.Key.ReturnWarehouseID)
                .ThenBy(x => x.Key.BatchNumber, StringComparer.OrdinalIgnoreCase);

            foreach (var group in withBatch)
            {
                var batchNumber = group.First().BatchNumber!.Trim();
                var quantity = decimal.Round(group.Sum(x => x.Quantity), 4, MidpointRounding.AwayFromZero);
                var incomingValue = group.Sum(x => x.Quantity * x.UnitCost);
                var expiryDates = group.Select(x => x.ExpiryDate?.Date).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
                if (expiryDates.Count > 1)
                    throw new InvalidOperationException($"طبقات الباتش {batchNumber} تشير إلى تواريخ صلاحية مختلفة؛ لم يتم تحديث الباتش.");
                var sourceExpiry = expiryDates.Count == 1 ? expiryDates[0] : (DateTime?)null;

                var existingBatchRows = (await connection.QueryAsync<InventoryBatchForReturn>(
                    @"SELECT BatchBalanceID, BatchNumber, ExpiryDate, CurrentQty, UnitCost
                      FROM dbo.InventoryBatchBalance WITH (UPDLOCK,HOLDLOCK)
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID AND BatchNumber=@BatchNumber
                      ORDER BY BatchBalanceID;",
                    new { group.Key.ItemID, WarehouseID = group.Key.ReturnWarehouseID, BatchNumber = batchNumber }, transaction)).ToList();
                if (existingBatchRows.Count > 1)
                    throw new InvalidOperationException($"يوجد أكثر من رصيد باتش للصنف والمخزن والباتش {batchNumber}؛ أوقفنا الاعتماد حتى تُسوّى الازدواجية.");
                var existing = existingBatchRows.SingleOrDefault();

                if (existing == null)
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.InventoryBatchBalance
                          (ItemID,WarehouseID,BatchNumber,ExpiryDate,ManufactureDate,CurrentQty,UnitCost,
                           ReceiptDate,SourceType,SourceDocNumber,ModifiedDate)
                          VALUES
                          (@ItemID,@WarehouseID,@BatchNumber,@ExpiryDate,NULL,@Quantity,@UnitCost,
                           @ReceiptDate,NULL,@SourceDocNumber,GETDATE());",
                        new
                        {
                            group.Key.ItemID,
                            WarehouseID = group.Key.ReturnWarehouseID,
                            BatchNumber = batchNumber,
                            ExpiryDate = sourceExpiry,
                            Quantity = quantity,
                            UnitCost = decimal.Round(incomingValue / quantity, 4, MidpointRounding.AwayFromZero),
                            ReceiptDate = header.ReturnDate.Date,
                            SourceDocNumber = header.ReturnNumber
                        }, transaction);
                    continue;
                }

                if (existing.CurrentQty < 0)
                    throw new InvalidOperationException($"رصيد الباتش {batchNumber} سالب؛ لم تتم إضافة الكمية المرتجعة.");
                if (existing.ExpiryDate.HasValue && sourceExpiry.HasValue && existing.ExpiryDate.Value.Date != sourceExpiry.Value.Date)
                    throw new InvalidOperationException($"تاريخ صلاحية الباتش {batchNumber} في المخزن المستلم لا يطابق تاريخ طبقة البيع الأصلية.");
                if (existing.CurrentQty > ReturnStockTolerance && existing.UnitCost.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException($"تكلفة الباتش الموجود {batchNumber} غير صالحة؛ صححها قبل إضافة المرتجع.");

                var existingValue = existing.CurrentQty * existing.UnitCost.GetValueOrDefault();
                var newQty = decimal.Round(existing.CurrentQty + quantity, 4, MidpointRounding.AwayFromZero);
                var newCost = newQty > 0
                    ? decimal.Round((existingValue + incomingValue) / newQty, 4, MidpointRounding.AwayFromZero)
                    : 0m;
                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.InventoryBatchBalance
                      SET CurrentQty=@NewQty, UnitCost=@NewCost,
                          ExpiryDate=COALESCE(ExpiryDate,@ExpiryDate),
                          ReceiptDate=COALESCE(ReceiptDate,@ReceiptDate),
                          ModifiedDate=GETDATE()
                      WHERE BatchBalanceID=@BatchBalanceID AND CurrentQty=@CurrentQty;",
                    new
                    {
                        NewQty = newQty,
                        NewCost = newCost,
                        ExpiryDate = sourceExpiry,
                        ReceiptDate = header.ReturnDate.Date,
                        existing.BatchBalanceID,
                        CurrentQty = existing.CurrentQty
                    }, transaction);
                if (affected != 1)
                    throw new InvalidOperationException($"تغير رصيد الباتش {batchNumber} أثناء الاعتماد؛ أوقفنا العملية.");
            }
        }

        private static async Task<decimal> ApplyReturnInventoryBalanceAndMovementsAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            List<ReturnCostAllocation> allocations,
            ReturnState header,
            int userId)
        {
            decimal totalCost = 0m;
            var groups = allocations
                .GroupBy(x => new { x.ItemID, x.ReturnWarehouseID, x.PrimaryUnitID })
                .OrderBy(x => x.Key.ItemID)
                .ThenBy(x => x.Key.ReturnWarehouseID);

            foreach (var group in groups)
            {
                var itemId = group.Key.ItemID;
                var warehouseId = group.Key.ReturnWarehouseID;
                var balance = await connection.QueryFirstOrDefaultAsync<InventoryBalanceForReturn>(
                    @"SELECT CurrentQty, ISNULL(ReservedQty,0) AS ReservedQty, AverageCost
                      FROM dbo.InventoryBalance WITH (UPDLOCK,HOLDLOCK)
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID;",
                    new { ItemID = itemId, WarehouseID = warehouseId }, transaction);

                if (balance == null)
                {
                    var ledgerQty = await connection.ExecuteScalarAsync<decimal>(
                        @"SELECT ISNULL(SUM(Quantity),0)
                          FROM dbo.InventoryTransactions WITH (UPDLOCK,HOLDLOCK)
                          WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID;",
                        new { ItemID = itemId, WarehouseID = warehouseId }, transaction);
                    var batchQty = await connection.ExecuteScalarAsync<decimal>(
                        @"SELECT ISNULL(SUM(CurrentQty),0)
                          FROM dbo.InventoryBatchBalance WITH (UPDLOCK,HOLDLOCK)
                          WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID;",
                        new { ItemID = itemId, WarehouseID = warehouseId }, transaction);
                    if (Math.Abs(ledgerQty) > ReturnStockTolerance || Math.Abs(batchQty) > ReturnStockTolerance)
                        throw new InvalidOperationException($"لا يوجد صف رصيد للصنف {itemId} في مخزن الاستلام رغم وجود سجل حركة/باتش؛ أوقفنا الترحيل لتجنب إنشاء رصيد غير مطابق.");

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.InventoryBalance
                          (ItemID,WarehouseID,CurrentQty,ReservedQty,AverageCost,LastMovementDate,ModifiedDate)
                          VALUES (@ItemID,@WarehouseID,0,0,0,GETDATE(),GETDATE());",
                        new { ItemID = itemId, WarehouseID = warehouseId }, transaction);
                    balance = new InventoryBalanceForReturn { CurrentQty = 0m, ReservedQty = 0m, AverageCost = 0m };
                }

                if (balance.CurrentQty < 0 || balance.ReservedQty < 0 ||
                    balance.ReservedQty > balance.CurrentQty + ReturnStockTolerance)
                    throw new InvalidOperationException($"رصيد الصنف {itemId} في مخزن الاستلام غير صالح؛ لم يتم ترحيل المرتجع.");
                if (balance.CurrentQty > ReturnStockTolerance && balance.AverageCost.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException($"متوسط تكلفة الرصيد الحالي للصنف {itemId} في مخزن الاستلام غير صالح.");

                var startingQty = balance.CurrentQty;
                var runningQty = startingQty;
                var runningValue = startingQty * balance.AverageCost.GetValueOrDefault();
                var runningAverage = startingQty > ReturnStockTolerance
                    ? balance.AverageCost.GetValueOrDefault()
                    : 0m;

                foreach (var allocation in group
                             .OrderBy(x => x.LineNumber)
                             .ThenBy(x => x.ReturnDetailID)
                             .ThenBy(x => x.SaleTransactionID))
                {
                    if (allocation.Quantity <= 0 || allocation.UnitCost <= 0)
                        throw new InvalidOperationException("كمية أو تكلفة طبقة FIFO المرتجعة غير صالحة.");
                    var balanceBefore = runningQty;
                    var costBefore = runningAverage;
                    runningQty = decimal.Round(runningQty + allocation.Quantity, 4, MidpointRounding.AwayFromZero);
                    runningValue += allocation.Quantity * allocation.UnitCost;
                    runningAverage = runningQty > ReturnStockTolerance
                        ? decimal.Round(runningValue / runningQty, 4, MidpointRounding.AwayFromZero)
                        : 0m;

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.InventoryTransactions
                          (TransactionDate,TransactionType,ItemID,WarehouseID,Quantity,UnitID,UnitCost,
                           BalanceBefore,BalanceAfter,CostBefore,CostAfter,BatchNumber,ExpiryDate,
                           SourceDocType,SourceDocID,SourceDocNumber,SourceDocLineID,Notes,CreatedBy,CreatedDate)
                          VALUES
                          (GETDATE(),@TransactionType,@ItemID,@WarehouseID,@Quantity,@UnitID,@UnitCost,
                           @BalanceBefore,@BalanceAfter,@CostBefore,@CostAfter,@BatchNumber,@ExpiryDate,
                           @SourceDocType,@SourceDocID,@SourceDocNumber,@SourceDocLineID,@Notes,@UserID,GETDATE());",
                        new
                        {
                            TransactionType = SalesReturnStockTransactionType,
                            ItemID = itemId,
                            WarehouseID = warehouseId,
                            allocation.Quantity,
                            UnitID = group.Key.PrimaryUnitID,
                            allocation.UnitCost,
                            BalanceBefore = balanceBefore,
                            BalanceAfter = runningQty,
                            CostBefore = costBefore,
                            CostAfter = runningAverage,
                            allocation.BatchNumber,
                            allocation.ExpiryDate,
                            SourceDocType = SalesReturnStockSourceType,
                            SourceDocID = header.ReturnID,
                            SourceDocNumber = header.ReturnNumber,
                            SourceDocLineID = allocation.ReturnDetailID,
                            Notes = $"{FifoSaleMarker}{allocation.SaleTransactionID}",
                            UserID = userId
                        }, transaction);
                    totalCost += allocation.Quantity * allocation.UnitCost;
                }

                var newBalance = decimal.Round(runningQty, 4, MidpointRounding.AwayFromZero);
                var newAverage = decimal.Round(runningAverage, 4, MidpointRounding.AwayFromZero);
                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.InventoryBalance
                      SET CurrentQty=@CurrentQty, AverageCost=@AverageCost,
                          LastMovementDate=GETDATE(),ModifiedDate=GETDATE()
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID AND CurrentQty=@StartingQty;",
                    new
                    {
                        CurrentQty = newBalance,
                        AverageCost = newAverage,
                        ItemID = itemId,
                        WarehouseID = warehouseId,
                        StartingQty = startingQty
                    }, transaction);
                if (updated != 1)
                    throw new InvalidOperationException($"تغير رصيد الصنف {itemId} أثناء اعتماد المرتجع؛ أوقفنا العملية.");
            }

            return totalCost;
        }

        private async Task CreateSalesReturnJournalAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            ReturnState header,
            decimal subtotal,
            decimal taxAmount,
            decimal totalAmount,
            decimal restockedCost,
            int? employeeId,
            int userId)
        {
            subtotal = decimal.Round(subtotal, 2, MidpointRounding.AwayFromZero);
            taxAmount = decimal.Round(taxAmount, 2, MidpointRounding.AwayFromZero);
            totalAmount = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
            restockedCost = decimal.Round(restockedCost, 2, MidpointRounding.AwayFromZero);
            if (subtotal < 0 || taxAmount < 0 || totalAmount < 0 || restockedCost < 0)
                throw new InvalidOperationException("مبالغ مرتجع المبيعات لا يمكن أن تكون سالبة.");
            if (Math.Abs((subtotal + taxAmount) - totalAmount) > 0.01m)
                throw new InvalidOperationException("إجمالي المرتجع لا يساوي صافي السطور مضافاً إليه الضريبة.");

            var lines = new List<ReturnJournalLine>();
            // Canonical YK chart codes; verify account type/activity/detail status before posting.
            // This intentionally fails closed if the deployed chart differs from the assumed mapping.
            var accountCodes = new[] { "1103", "41", "2105", "5101", "110504" };
            var accountRows = (await connection.QueryAsync<AccountIdRow>(
                @"SELECT AccountCode,AccountID,AccountType,IsDetailAccount,ISNULL(IsActive,0) AS IsActive
                  FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK)
                  WHERE AccountCode IN @AccountCodes;",
                new { AccountCodes = accountCodes }, transaction)).ToList();
            if (accountRows.GroupBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
                throw new InvalidOperationException("يوجد كود حساب محاسبي مكرر في دليل الحسابات؛ لم يتم اعتماد المرتجع.");
            var accounts = accountRows.ToDictionary(x => x.AccountCode, StringComparer.OrdinalIgnoreCase);

            void RequireAccount(string code, int expectedType)
            {
                if (!accounts.TryGetValue(code, out var account))
                    throw new InvalidOperationException($"الحساب المحاسبي {code} المطلوب لعكس فاتورة البيع غير مهيأ؛ لم يتم اعتماد المرتجع.");
                if (!account.IsActive || !account.IsDetailAccount || account.AccountType != expectedType)
                    throw new InvalidOperationException($"الحساب {code} غير نشط أو ليس حساباً تفصيلياً من النوع المتوقع؛ راجع دليل الحسابات قبل اعتماد المرتجع.");
            }

            if (totalAmount > 0)
            {
                RequireAccount("1103", expectedType: 1); // receivables / asset
                if (subtotal > 0) RequireAccount("41", expectedType: 4); // sales revenue
                if (taxAmount > 0) RequireAccount("2105", expectedType: 2); // output tax liability
            }
            if (restockedCost > 0)
            {
                RequireAccount("110504", expectedType: 1); // inventory asset
                RequireAccount("5101", expectedType: 5); // cost of sales
            }

            var customerName = await connection.ExecuteScalarAsync<string>(
                "SELECT CustomerNameAr FROM dbo.Customers WHERE CustomerID=@CustomerID;",
                new { header.CustomerID }, transaction) ?? "العميل";

            if (subtotal > 0)
                lines.Add(new ReturnJournalLine
                {
                    AccountID = accounts["41"].AccountID, Debit = subtotal, Credit = 0m,
                    Description = TruncateJournalText($"عكس إيراد مبيعات - {header.ReturnNumber}", 200)
                });
            if (taxAmount > 0)
                lines.Add(new ReturnJournalLine
                {
                    AccountID = accounts["2105"].AccountID, Debit = taxAmount, Credit = 0m,
                    Description = TruncateJournalText($"عكس ضريبة مخرجات - {header.ReturnNumber}", 200)
                });
            if (totalAmount > 0)
                lines.Add(new ReturnJournalLine
                {
                    AccountID = accounts["1103"].AccountID, Debit = 0m, Credit = totalAmount,
                    Description = TruncateJournalText($"إشعار دائن للعميل {customerName} - {header.ReturnNumber}", 200),
                    EntityType = "Customer",
                    EntityID = header.CustomerID
                });
            if (restockedCost > 0)
            {
                lines.Add(new ReturnJournalLine
                {
                    AccountID = accounts["110504"].AccountID, Debit = restockedCost, Credit = 0m,
                    Description = TruncateJournalText($"إعادة مخزون بتكلفة FIFO - {header.ReturnNumber}", 200)
                });
                lines.Add(new ReturnJournalLine
                {
                    AccountID = accounts["5101"].AccountID, Debit = 0m, Credit = restockedCost,
                    Description = TruncateJournalText($"عكس تكلفة المبيعات - {header.ReturnNumber}", 200)
                });
            }

            if (lines.Count == 0) return;
            var openPeriods = (await connection.QueryAsync<ReturnAccountingPeriod>(
                @"SELECT fy.FiscalYearID,ap.PeriodID
                  FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
                  WHERE @JournalDate BETWEEN fy.StartDate AND fy.EndDate
                    AND @JournalDate BETWEEN ap.StartDate AND ap.EndDate
                    AND fy.YearStatus=1 AND ap.PeriodStatus=1
                  ORDER BY fy.FiscalYearID DESC,ap.PeriodNumber;",
                new { JournalDate = header.ReturnDate.Date }, transaction)).ToList();
            if (openPeriods.Count != 1)
                throw new InvalidOperationException("لا توجد فترة محاسبية مفتوحة واحدة فقط تغطي تاريخ المرتجع؛ لم يتم ترحيل المخزون أو اعتماد المرتجع.");
            var accountingPeriod = openPeriods[0];

            var debitTotal = decimal.Round(lines.Sum(x => x.Debit), 2, MidpointRounding.AwayFromZero);
            var creditTotal = decimal.Round(lines.Sum(x => x.Credit), 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(debitTotal - creditTotal) > 0.01m)
                throw new InvalidOperationException($"قيد مرتجع المبيعات غير متوازن (مدين {debitTotal:#,##0.00} / دائن {creditTotal:#,##0.00}).");

            var journalNumber = await GetNextDocumentNumberAsync(connection, transaction, JournalSequenceCode);
            var journalId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.JournalEntries
                  (JournalNumber,JournalDate,FiscalYearID,PeriodID,JournalType,Description,Reference,
                   TotalDebit,TotalCredit,SourceDocType,SourceDocID,SourceDocNumber,
                   IsAutoGenerated,JournalStatus,PostedBy,PostedDate,Notes,CreatedBy,CreatedDate)
                  VALUES
                  (@JournalNumber,@JournalDate,@FiscalYearID,@PeriodID,3,@Description,@Reference,
                   @TotalDebit,@TotalCredit,N'SRET',@ReturnID,@SourceDocNumber,
                   1,2,@EmployeeID,GETDATE(),@Notes,@UserID,GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    JournalNumber = journalNumber,
                    JournalDate = header.ReturnDate.Date,
                    accountingPeriod.FiscalYearID,
                    accountingPeriod.PeriodID,
                    Description = TruncateJournalText($"مرتجع مبيعات رقم {header.ReturnNumber} - {customerName}", 500),
                    Reference = header.ReturnNumber,
                    TotalDebit = debitTotal,
                    TotalCredit = creditTotal,
                    ReturnID = header.ReturnID,
                    SourceDocNumber = header.ReturnNumber,
                    Notes = "إشعار دائن للعميل؛ لا يتضمن رد نقدي تلقائي.",
                    EmployeeID = employeeId,
                    UserID = userId
                }, transaction);

            var lineNo = 0;
            foreach (var line in lines)
            {
                lineNo++;
                await connection.ExecuteAsync(
                    @"INSERT INTO dbo.JournalEntryDetails
                      (JournalID,LineNumber,AccountID,DebitAmount,CreditAmount,Description,
                       Reference,EntityType,EntityID)
                      VALUES
                      (@JournalID,@LineNumber,@AccountID,@Debit,@Credit,@Description,
                       @Reference,@EntityType,@EntityID);",
                    new
                    {
                        JournalID = journalId,
                        LineNumber = lineNo,
                        line.AccountID,
                        Debit = line.Debit,
                        Credit = line.Credit,
                        line.Description,
                        Reference = header.ReturnNumber,
                        line.EntityType,
                        line.EntityID
                    }, transaction);
            }

            var savedBalance = await connection.QueryFirstOrDefaultAsync<(int Count, decimal Debit, decimal Credit)>(
                @"SELECT COUNT(*) AS Count,ISNULL(SUM(DebitAmount),0) AS Debit,
                         ISNULL(SUM(CreditAmount),0) AS Credit
                  FROM dbo.JournalEntryDetails WHERE JournalID=@JournalID;",
                new { JournalID = journalId }, transaction);
            if (savedBalance.Count != lines.Count || Math.Abs(savedBalance.Debit - savedBalance.Credit) > 0.01m)
                throw new InvalidOperationException("تعذر التحقق من توازن قيد مرتجع المبيعات؛ تم إلغاء الاعتماد.");

            await ApplyPostedJournalBalancesAsync(connection, transaction, lines);
        }

        private static string TruncateJournalText(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        private static async Task ApplyPostedJournalBalancesAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            List<ReturnJournalLine> lines)
        {
            foreach (var entry in lines
                         .GroupBy(x => x.AccountID)
                         .Select(x => new { AccountID = x.Key, Delta = x.Sum(y => y.Debit - y.Credit) })
                         .Where(x => x.Delta != 0)
                         .OrderBy(x => x.AccountID))
            {
                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.ChartOfAccounts
                      SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta
                      WHERE AccountID=@AccountID;",
                    new { entry.AccountID, entry.Delta }, transaction);
                if (updated != 1)
                    throw new InvalidOperationException($"تعذر تحديث رصيد الحساب {entry.AccountID}؛ أُلغي اعتماد المرتجع بالكامل.");

                var parentId = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT ParentAccountID FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK)
                      WHERE AccountID=@AccountID;",
                    new { entry.AccountID }, transaction);
                var seen = new HashSet<int> { entry.AccountID };
                var depth = 0;
                while (parentId.GetValueOrDefault() > 0 && depth < 10)
                {
                    var id = parentId!.Value;
                    if (!seen.Add(id))
                        throw new InvalidOperationException("يوجد تسلسل دائري في شجرة الحسابات؛ أُلغي اعتماد المرتجع.");
                    var parentUpdated = await connection.ExecuteAsync(
                        @"UPDATE dbo.ChartOfAccounts
                          SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta
                          WHERE AccountID=@AccountID;",
                        new { AccountID = id, entry.Delta }, transaction);
                    if (parentUpdated != 1)
                        throw new InvalidOperationException($"تعذر تحديث رصيد الحساب الأب {id}؛ أُلغي اعتماد المرتجع.");
                    parentId = await connection.QueryFirstOrDefaultAsync<int?>(
                        @"SELECT ParentAccountID FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK)
                          WHERE AccountID=@AccountID;",
                        new { AccountID = id }, transaction);
                    depth++;
                }
                if (parentId.GetValueOrDefault() > 0)
                    throw new InvalidOperationException("تجاوز عمق دليل الحسابات الحد الآمن؛ أُلغي اعتماد المرتجع.");
            }
        }
    }
}
