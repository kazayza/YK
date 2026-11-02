using Dapper;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class InventoryService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;
        private readonly ModuleAuthorizationService _authorization;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif,
            ModuleAuthorizationService authorization,
            ILogger<InventoryService> logger)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
            _authorization = authorization;
            _logger = logger;
        }

        private Task RequireInventoryPermissionAsync(
            int userId, string moduleCode, Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireUserPermissionAsync(userId, moduleCode, predicate, deniedMessage);

        private Task RequireCurrentInventoryPermissionAsync(
            string moduleCode, Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireCurrentUserPermissionAsync(moduleCode, predicate, deniedMessage);

        private sealed class TransferBalanceSnapshot
        {
            public TransferBalanceSnapshot() { }
            public decimal CurrentQty { get; set; }
            public decimal AvailableQty { get; set; }
            public decimal AverageCost { get; set; }
        }

        private sealed class TransferBatchSnapshot
        {
            public TransferBatchSnapshot() { }
            public int BatchBalanceID { get; set; }
            public string? BatchNumber { get; set; }
            public decimal CurrentQty { get; set; }
            public decimal? UnitCost { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public DateTime? ManufactureDate { get; set; }
            public DateTime? ReceiptDate { get; set; }
            public int? SourceType { get; set; }
            public string? SourceDocNumber { get; set; }
        }

        // ==========================================
        // إحصائيات المخزون
        // ==========================================
        public async Task<InventoryDashboardDto> GetDashboardAsync()
        {
            await RequireCurrentInventoryPermissionAsync("SCR_STOCK", p => p.CanView, "ليس لديك صلاحية عرض لوحة المخزون.");
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<InventoryDashboardDto>(
                "SELECT * FROM dbo.vw_InventoryDashboard") ?? new InventoryDashboardDto();
        }

        // ==========================================
        // أرصدة المخزون
        // ==========================================
        public async Task<List<StockBalanceDto>> GetStockBalancesAsync(int? warehouseId = null, int? itemType = null, int? alertLevel = null)
        {
            await RequireCurrentInventoryPermissionAsync("SCR_STOCK", p => p.CanView, "ليس لديك صلاحية عرض أرصدة المخزون.");
            using var connection = CreateConnection();
            var sql = @"SELECT * FROM dbo.vw_StockBalances
                        WHERE (@WarehouseID IS NULL OR WarehouseID = @WarehouseID)
                          AND (@ItemType IS NULL OR ItemType = @ItemType)
                          AND (@AlertLevel IS NULL OR StockAlert >= @AlertLevel)
                        ORDER BY StockAlert DESC, ItemNameAr";
            return (await connection.QueryAsync<StockBalanceDto>(sql,
                new { WarehouseID = warehouseId, ItemType = itemType, AlertLevel = alertLevel })).ToList();
        }

        // ==========================================
        // أرصدة الباتشات
        // ==========================================
        public async Task<List<BatchBalanceDto>> GetBatchBalancesAsync(int? itemId = null, int? warehouseId = null, int? expiryAlert = null)
        {
            await RequireCurrentInventoryPermissionAsync("SCR_STOCK", p => p.CanView, "ليس لديك صلاحية عرض أرصدة الباتشات.");
            using var connection = CreateConnection();
            var sql = @"SELECT * FROM dbo.vw_BatchBalances
                        WHERE (@ItemID IS NULL OR ItemID = @ItemID)
                          AND (@WarehouseID IS NULL OR WarehouseID = @WarehouseID)
                          AND (@ExpiryAlert IS NULL OR ExpiryAlert >= @ExpiryAlert)
                        ORDER BY ExpiryAlert DESC, ExpiryDate ASC";
            return (await connection.QueryAsync<BatchBalanceDto>(sql,
                new { ItemID = itemId, WarehouseID = warehouseId, ExpiryAlert = expiryAlert })).ToList();
        }

        // ==========================================
        // حركات المخزون
        // ==========================================
        public async Task<List<StockMovementDto>> GetMovementsAsync(
            int? itemId = null, int? warehouseId = null, int? transType = null,
            DateTime? fromDate = null, DateTime? toDate = null)
        {
            await RequireCurrentInventoryPermissionAsync("SCR_TRANS", p => p.CanView, "ليس لديك صلاحية عرض حركات المخزون.");
            using var connection = CreateConnection();
            var sql = @"SELECT * FROM dbo.vw_StockMovements
                        WHERE (@ItemID IS NULL OR ItemID = @ItemID)
                          AND (@WarehouseID IS NULL OR WarehouseID = @WarehouseID)
                          AND (@TransType IS NULL OR TransactionType = @TransType)
                          AND (@FromDate IS NULL OR CAST(TransactionDate AS DATE) >= @FromDate)
                          AND (@ToDate IS NULL OR CAST(TransactionDate AS DATE) <= @ToDate)
                        ORDER BY TransactionDate DESC, TransactionID DESC";
            return (await connection.QueryAsync<StockMovementDto>(sql,
                new { ItemID = itemId, WarehouseID = warehouseId, TransType = transType, FromDate = fromDate, ToDate = toDate })).ToList();
        }

        // ==========================================
        // كارت صنف
        // ==========================================
        public async Task<List<StockMovementDto>> GetItemCardAsync(int itemId, int? warehouseId = null)
        {
            return await GetMovementsAsync(itemId: itemId, warehouseId: warehouseId);
        }

        // ==========================================
        // تحويلات المخزون - القائمة
        // ==========================================
        public async Task<List<StockTransferListDto>> GetTransfersListAsync()
        {
            await RequireCurrentInventoryPermissionAsync("SCR_TRANSFER", p => p.CanView, "ليس لديك صلاحية عرض التحويلات المخزنية.");
            using var connection = CreateConnection();
            return (await connection.QueryAsync<StockTransferListDto>(
                "SELECT * FROM dbo.vw_StockTransferSummary ORDER BY TransferDate DESC, TransferID DESC")).ToList();
        }

        public async Task<StockTransferHeaderDto?> GetTransferByIdAsync(int transferId)
        {
            await RequireCurrentInventoryPermissionAsync("SCR_TRANSFER", p => p.CanView, "ليس لديك صلاحية عرض التحويلات المخزنية.");
            using var connection = CreateConnection();
            var sql = @"SELECT st.TransferID, st.TransferNumber, st.TransferDate, st.TransferStatus,
                            st.FromWarehouseID, fw.WarehouseNameAr AS FromWarehouseName,
                            st.ToWarehouseID, tw.WarehouseNameAr AS ToWarehouseName,
                            st.TransferReason, st.RequestedBy, req.FullNameAr AS RequestedByName,
                            st.ApprovedBy, st.ApprovedDate, st.Notes, st.CreatedBy, st.CreatedDate
                        FROM dbo.StockTransfers st
                        INNER JOIN dbo.Warehouses fw ON st.FromWarehouseID = fw.WarehouseID
                        INNER JOIN dbo.Warehouses tw ON st.ToWarehouseID = tw.WarehouseID
                        LEFT JOIN dbo.Employees req ON st.RequestedBy = req.EmployeeID
                        WHERE st.TransferID = @ID";
            return await connection.QueryFirstOrDefaultAsync<StockTransferHeaderDto>(sql, new { ID = transferId });
        }

        public async Task<List<StockTransferDetailDto>> GetTransferDetailsAsync(int transferId)
        {
            await RequireCurrentInventoryPermissionAsync("SCR_TRANSFER", p => p.CanView, "ليس لديك صلاحية عرض التحويلات المخزنية.");
            using var connection = CreateConnection();
            var sql = @"SELECT std.TransferDetailID, std.TransferID, std.LineNumber,
                            std.ItemID, i.ItemCode, i.ItemNameAr,
                            std.UnitID, u.UnitNameAr AS UnitName,
                            std.Quantity, std.UnitCost, std.BatchNumber, std.ExpiryDate, std.Notes,
                            ISNULL(ib.AvailableQty, 0) AS AvailableQty
                        FROM dbo.StockTransferDetails std
                        INNER JOIN dbo.Items i ON std.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON std.UnitID = u.UnitID
                        LEFT JOIN dbo.InventoryBalance ib ON std.ItemID = ib.ItemID
                            AND ib.WarehouseID = (SELECT FromWarehouseID FROM dbo.StockTransfers WHERE TransferID = std.TransferID)
                        WHERE std.TransferID = @ID ORDER BY std.LineNumber";
            return (await connection.QueryAsync<StockTransferDetailDto>(sql, new { ID = transferId })).ToList();
        }

        public async Task<string> GenerateTransferNumberAsync()
        {
            await RequireCurrentInventoryPermissionAsync("SCR_TRANSFER", p => p.CanAdd, "ليس لديك صلاحية إنشاء تحويل مخزني.");
            using var connection = CreateConnection();
            try
            {
                var result = await connection.QueryFirstOrDefaultAsync<string>(
                    "DECLARE @N NVARCHAR(50); EXEC sp_GetNextNumber N'ST', @N OUTPUT; SELECT @N;");
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لتحويل المخزون.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم تحويل المخزون (ST). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل ST والإجراء المخزن.", ex);
            }
        }

        public async Task<int> InsertTransferAsync(StockTransferHeaderDto header, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_TRANSFER", p => p.CanAdd, "ليس لديك صلاحية إنشاء تحويل مخزني.");
            using var connection = CreateConnection();
            var newId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.StockTransfers (TransferNumber, TransferDate, FromWarehouseID, ToWarehouseID,
                    TransferReason, TransferStatus, RequestedBy, Notes, CreatedBy, CreatedDate)
                  VALUES (@TransferNumber, @TransferDate, @FromWarehouseID, @ToWarehouseID,
                    @TransferReason, 1, NULLIF(@RequestedBy, 0), @Notes, @UserID, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { header.TransferNumber, header.TransferDate, header.FromWarehouseID, header.ToWarehouseID,
                    header.TransferReason, header.RequestedBy, header.Notes, UserID = userId });
            await _audit.WriteAuditLogAsync(userId, 1, "StockTransfers", newId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"إنشاء تحويل مخزني: {header.TransferNumber}");
            return newId;
        }

        public async Task UpdateTransferAsync(StockTransferHeaderDto header, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_TRANSFER", p => p.CanEdit, "ليس لديك صلاحية تعديل تحويل مخزني.");
            if (header.FromWarehouseID <= 0 || header.ToWarehouseID <= 0 || header.FromWarehouseID == header.ToWarehouseID)
                throw new Exception("اختر مخزنين مختلفين صالحين للتحويل.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            var transferNumber = string.Empty;
            try
            {
                var existing = await connection.QueryFirstOrDefaultAsync<
                    (string TransferNumber, int Status, int FromWarehouseID, int ToWarehouseID)>(
                    @"SELECT TransferNumber, TransferStatus, FromWarehouseID, ToWarehouseID
                      FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK)
                      WHERE TransferID=@ID",
                    new { ID = header.TransferID }, transaction);
                if (string.IsNullOrWhiteSpace(existing.TransferNumber)) throw new Exception("التحويل غير موجود");
                if (existing.Status != 1) throw new Exception("لا يمكن تعديل تحويل ليس في حالة مسودة");

                var detailCount = await connection.QueryFirstOrDefaultAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.StockTransferDetails WITH (UPDLOCK, HOLDLOCK)
                      WHERE TransferID=@ID",
                    new { ID = header.TransferID }, transaction);
                if (detailCount > 0 &&
                    (existing.FromWarehouseID != header.FromWarehouseID || existing.ToWarehouseID != header.ToWarehouseID))
                    throw new Exception("لا يمكن تغيير المخزنين بعد إضافة أصناف؛ احذف الأسطر أولًا ثم عدّل المخازن.");

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.StockTransfers SET TransferDate=@TransferDate, FromWarehouseID=@FromWarehouseID,
                        ToWarehouseID=@ToWarehouseID, TransferReason=@TransferReason, Notes=@Notes,
                        ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE TransferID=@TransferID AND TransferStatus=1",
                    new { header.TransferID, header.TransferDate, header.FromWarehouseID, header.ToWarehouseID,
                        header.TransferReason, header.Notes, UserID = userId }, transaction);
                if (updated != 1) throw new InvalidOperationException("تغيرت حالة التحويل أثناء التعديل؛ أُلغيت العملية.");

                transferNumber = existing.TransferNumber;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after updating stock transfer {TransferID}.",
                        header.TransferID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "StockTransfers", header.TransferID.ToString(),
                moduleName: "SCR_TRANSFER", description: $"تعديل تحويل: {transferNumber}");
        }

        public async Task<int> InsertTransferDetailAsync(StockTransferDetailEditDto detail, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_TRANSFER", p => p.CanAdd, "ليس لديك صلاحية إضافة تفاصيل تحويل مخزني.");
            if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
            if (detail.Quantity <= 0) throw new Exception("أدخل الكمية");

            var batchNumber = string.IsNullOrWhiteSpace(detail.BatchNumber)
                ? null
                : detail.BatchNumber.Trim();
            if (batchNumber?.Length > 100) throw new Exception("رقم الباتش يتجاوز الحد المسموح وهو 100 حرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            int newId;
            try
            {
                var header = await connection.QueryFirstOrDefaultAsync<
                    (string TransferNumber, int Status, int FromWarehouseID, int ToWarehouseID)>(
                    @"SELECT TransferNumber, TransferStatus, FromWarehouseID, ToWarehouseID
                      FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK)
                      WHERE TransferID=@TransferID",
                    new { detail.TransferID }, transaction);
                if (string.IsNullOrWhiteSpace(header.TransferNumber)) throw new Exception("التحويل غير موجود");
                if (header.Status != 1) throw new Exception("لا يمكن إضافة سطر إلى تحويل ليس مسودة");
                if (header.FromWarehouseID <= 0 || header.ToWarehouseID <= 0 || header.FromWarehouseID == header.ToWarehouseID)
                    throw new Exception("حدد مخزنين مختلفين صالحين قبل إضافة الأصناف.");

                var primaryUnitId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT PrimaryUnitID FROM dbo.Items WITH (HOLDLOCK) WHERE ItemID=@ItemID AND IsActive=1",
                    new { detail.ItemID }, transaction);
                if (!primaryUnitId.HasValue) throw new Exception("الصنف غير موجود أو غير نشط.");
                if (detail.UnitID != primaryUnitId.Value)
                    throw new Exception("تحويل المخزون يقبل الوحدة الأساسية للصنف فقط؛ اختر الصنف مجددًا لتحديث الوحدة.");

                var balance = await connection.QueryFirstOrDefaultAsync<TransferBalanceSnapshot>(
                    @"SELECT CurrentQty, ISNULL(AvailableQty,0) AS AvailableQty, ISNULL(AverageCost,0) AS AverageCost
                      FROM dbo.InventoryBalance WITH (UPDLOCK, HOLDLOCK)
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID",
                    new { detail.ItemID, WarehouseID = header.FromWarehouseID }, transaction);
                if (balance is null) throw new Exception("لا يوجد رصيد للصنف في المخزن المصدر.");
                if (balance.CurrentQty < 0 || balance.AvailableQty < 0 || balance.AvailableQty > balance.CurrentQty)
                    throw new Exception("رصيد الصنف أو المحجوز منه غير متزن؛ أوقفنا إضافة السطر.");

                var alreadyOnTransfer = await connection.QueryFirstOrDefaultAsync<decimal>(
                    @"SELECT ISNULL(SUM(Quantity),0)
                      FROM dbo.StockTransferDetails WITH (UPDLOCK, HOLDLOCK)
                      WHERE TransferID=@TransferID AND ItemID=@ItemID",
                    new { detail.TransferID, detail.ItemID }, transaction);
                if (alreadyOnTransfer < 0 || detail.Quantity > balance.AvailableQty - alreadyOnTransfer)
                {
                    var remaining = Math.Max(0m, balance.AvailableQty - alreadyOnTransfer);
                    throw new Exception($"الكمية تتجاوز المتاح بعد احتساب أسطر هذا التحويل. المتاح المتبقي {remaining:#,##0.####}.");
                }

                var batches = (await connection.QueryAsync<TransferBatchSnapshot>(
                    @"SELECT BatchBalanceID, BatchNumber, CurrentQty, UnitCost, ExpiryDate,
                             ManufactureDate, ReceiptDate, SourceType, SourceDocNumber
                      FROM dbo.InventoryBatchBalance WITH (UPDLOCK, HOLDLOCK)
                      WHERE ItemID=@ItemID AND WarehouseID=@WarehouseID
                      ORDER BY BatchBalanceID",
                    new { detail.ItemID, WarehouseID = header.FromWarehouseID }, transaction)).ToList();

                decimal unitCost;
                DateTime? expiryDate;
                if (batches.Count > 0)
                {
                    if (batchNumber is null)
                        throw new Exception("هذا الصنف متتبع بالباتش؛ اختر رقم باتش موجودًا في المخزن المصدر.");

                    if (batches.Any(x => x.CurrentQty < 0 || string.IsNullOrWhiteSpace(x.BatchNumber) ||
                        (x.CurrentQty > 0 && x.UnitCost.GetValueOrDefault() <= 0)))
                        throw new Exception("توجد طبقة باتش سالبة أو بلا رقم/تكلفة موجبة؛ أوقفنا إضافة السطر.");
                    var batchTotal = batches.Sum(x => x.CurrentQty);
                    if (Math.Abs(batchTotal - balance.CurrentQty) > 0.0001m)
                        throw new Exception("مجموع أرصدة الباتش لا يطابق الرصيد الإجمالي؛ سوِّ الفرق قبل إنشاء التحويل.");
                    var layerValue = batches.Sum(x => x.CurrentQty * x.UnitCost.GetValueOrDefault());
                    var layerAverage = batchTotal > 0 ? decimal.Round(layerValue / batchTotal, 4, MidpointRounding.AwayFromZero) : 0m;
                    if (Math.Abs(layerAverage - balance.AverageCost) > 0.0001m)
                        throw new Exception("متوسط تكلفة الرصيد لا يطابق تكلفة طبقات الباتش؛ سوِّ الفرق قبل إنشاء التحويل.");

                    var selected = batches.Where(x => string.Equals(
                        x.BatchNumber?.Trim(), batchNumber, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (selected.Count != 1)
                        throw new Exception("رقم الباتش غير موجود مرة واحدة فقط في المخزن المصدر.");
                    var sourceBatch = selected[0];
                    var pendingBatchQty = await connection.QueryFirstOrDefaultAsync<decimal>(
                        @"SELECT ISNULL(SUM(Quantity),0)
                          FROM dbo.StockTransferDetails WITH (UPDLOCK, HOLDLOCK)
                          WHERE TransferID=@TransferID AND ItemID=@ItemID
                            AND UPPER(LTRIM(RTRIM(BatchNumber)))=UPPER(@BatchNumber)",
                        new { detail.TransferID, detail.ItemID, BatchNumber = batchNumber }, transaction);
                    if (pendingBatchQty < 0 || detail.Quantity > sourceBatch.CurrentQty - pendingBatchQty)
                    {
                        var remaining = Math.Max(0m, sourceBatch.CurrentQty - pendingBatchQty);
                        throw new Exception($"الكمية تتجاوز رصيد الباتش المتبقي بعد احتساب أسطر التحويل: {remaining:#,##0.####}.");
                    }
                    var sourceBatchUnitCost = sourceBatch.UnitCost.GetValueOrDefault();
                    if (sourceBatchUnitCost <= 0)
                        throw new Exception("تكلفة الباتش المصدر غير موجبة؛ أوقفنا التحويل.");
                    if (detail.ExpiryDate.HasValue && detail.ExpiryDate.Value.Date != sourceBatch.ExpiryDate?.Date)
                        throw new Exception("تاريخ الصلاحية المدخل لا يطابق تاريخ الباتش في المخزن المصدر.");

                    unitCost = sourceBatchUnitCost;
                    expiryDate = sourceBatch.ExpiryDate;
                }
                else
                {
                    if (batchNumber is not null || detail.ExpiryDate.HasValue)
                        throw new Exception("لا يمكن تحديد باتش أو صلاحية لصنف لا يملك رصيد باتش في المخزن المصدر.");
                    if (balance.CurrentQty > 0 && balance.AverageCost <= 0)
                        throw new Exception("متوسط تكلفة رصيد المخزن المصدر غير موجب؛ أوقفنا التحويل.");

                    unitCost = balance.AverageCost;
                    expiryDate = null;
                }

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    @"SELECT ISNULL(MAX(LineNumber),0)
                      FROM dbo.StockTransferDetails WITH (UPDLOCK, HOLDLOCK)
                      WHERE TransferID=@TransferID",
                    new { detail.TransferID }, transaction);
                detail.LineNumber = maxLine + 1;

                newId = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.StockTransferDetails
                        (TransferID, LineNumber, ItemID, UnitID, Quantity, UnitCost, BatchNumber, ExpiryDate, Notes)
                      VALUES
                        (@TransferID, @LineNumber, @ItemID, @UnitID, @Quantity, @UnitCost, @BatchNumber, @ExpiryDate, @Notes);
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        detail.TransferID,
                        detail.LineNumber,
                        detail.ItemID,
                        detail.UnitID,
                        detail.Quantity,
                        UnitCost = unitCost,
                        BatchNumber = batchNumber,
                        ExpiryDate = expiryDate,
                        detail.Notes
                    }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after inserting stock transfer detail for transfer {TransferID}.",
                        detail.TransferID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 1, "StockTransferDetails", newId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"إضافة سطر في تحويل رقم {detail.TransferID}");
            return newId;
        }

        public async Task DeleteTransferDetailAsync(int detailId, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_TRANSFER", p => p.CanDelete, "ليس لديك صلاحية حذف تفاصيل تحويل مخزني.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            var transferId = 0;
            var transferNumber = string.Empty;
            try
            {
                var parentId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT TransferID FROM dbo.StockTransferDetails WHERE TransferDetailID=@ID",
                    new { ID = detailId }, transaction);
                if (!parentId.HasValue) throw new Exception("سطر التحويل غير موجود.");
                transferId = parentId.Value;

                var header = await connection.QueryFirstOrDefaultAsync<(string TransferNumber, int Status)>(
                    @"SELECT TransferNumber, TransferStatus
                      FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK)
                      WHERE TransferID=@TransferID",
                    new { TransferID = transferId }, transaction);
                if (string.IsNullOrWhiteSpace(header.TransferNumber)) throw new Exception("التحويل غير موجود.");
                if (header.Status != 1) throw new Exception("لا يمكن حذف سطر من تحويل ليس مسودة.");

                var deleted = await connection.ExecuteAsync(
                    "DELETE FROM dbo.StockTransferDetails WHERE TransferDetailID=@ID AND TransferID=@TransferID",
                    new { ID = detailId, TransferID = transferId }, transaction);
                if (deleted != 1) throw new InvalidOperationException("تغير سطر التحويل أثناء الحذف؛ أُلغيت العملية.");

                transferNumber = header.TransferNumber;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after deleting stock transfer detail {TransferDetailID}.",
                        detailId);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "StockTransferDetails", detailId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"حذف سطر من التحويل {transferNumber}");
        }

        public async Task ApproveAndExecuteTransferAsync(int transferId, int userId, int? employeeId = null)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_TRANSFER", p => p.CanApprove, "ليس لديك صلاحية اعتماد التحويل المخزني.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            (string TransferNumber, int Status, int DetailCount) info;
            try
            {
                // Keep the draft header locked while approval and stock execution are atomic.
                info = await connection.QueryFirstOrDefaultAsync<(string TransferNumber, int Status, int DetailCount)>(
                    @"SELECT st.TransferNumber, st.TransferStatus,
                        (SELECT COUNT(*) FROM dbo.StockTransferDetails WITH (UPDLOCK, HOLDLOCK)
                         WHERE TransferID=st.TransferID) AS DetailCount
                      FROM dbo.StockTransfers st WITH (UPDLOCK, HOLDLOCK)
                      WHERE st.TransferID=@ID",
                    new { ID = transferId }, transaction);
                if (string.IsNullOrWhiteSpace(info.TransferNumber)) throw new Exception("التحويل غير موجود");
                if (info.Status != 1) throw new Exception("لا يمكن اعتماد تحويل ليس مسودة");
                if (info.DetailCount == 0) throw new Exception("لا يمكن اعتماد تحويل بدون أصناف");

                var statusUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.StockTransfers SET TransferStatus=2, ApprovedBy=@EmpID, ApprovedDate=GETDATE(),
                        ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE TransferID=@ID AND TransferStatus=1",
                    new { ID = transferId, EmpID = employeeId, UserID = userId }, transaction);
                if (statusUpdated != 1)
                    throw new InvalidOperationException("تغيرت حالة التحويل أثناء الاعتماد؛ أُلغيت العملية.");

                // If the posting procedure fails, the status change is rolled back too.
                await connection.ExecuteAsync("EXEC dbo.sp_ExecuteStockTransfer @TransferID, @UserID",
                    new { TransferID = transferId, UserID = userId }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after stock-transfer approval failed for transfer {TransferID}.",
                        transferId);
                }
                throw;
            }

            try
            {
                await _notif.CreateForPermissionHoldersAsync(
                    notificationType: 1,
                    title: "تم تنفيذ تحويل مخزني",
                    message: $"تم تنفيذ التحويل المخزني رقم {info.TransferNumber}",
                    priority: 2,
                    permissionModuleCode: "SCR_TRANSFER",
                    requiredPermission: ModulePermissionRequirement.View,
                    relatedModule: "SCR_TRANSFER",
                    relatedRecordId: transferId,
                    createdBy: userId);
            }
            catch (Exception ex)
            {
                // Posting is already committed; keep it successful but make the lost notification observable.
                _logger.LogWarning(ex, "Stock transfer was committed, but its notification could not be created.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "StockTransfers", transferId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"اعتماد وتنفيذ تحويل: {info.TransferNumber}");
        }

        public async Task<(bool Success, string Message)> DeleteTransferAsync(int transferId, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_TRANSFER", p => p.CanDelete, "ليس لديك صلاحية حذف تحويل مخزني.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            var transferNumber = string.Empty;
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string TransferNumber, int Status)>(
                    @"SELECT TransferNumber, TransferStatus
                      FROM dbo.StockTransfers WITH (UPDLOCK, HOLDLOCK)
                      WHERE TransferID=@ID",
                    new { ID = transferId }, transaction);
                if (string.IsNullOrWhiteSpace(info.TransferNumber))
                {
                    try { transaction.Rollback(); }
                    catch (Exception rollbackException)
                    {
                        _logger.LogError(rollbackException,
                            "Rollback failed while returning not-found result for stock transfer {TransferID}.",
                            transferId);
                    }
                    return (false, "التحويل غير موجود");
                }
                if (info.Status != 1)
                {
                    try { transaction.Rollback(); }
                    catch (Exception rollbackException)
                    {
                        _logger.LogError(rollbackException,
                            "Rollback failed while rejecting deletion of non-draft stock transfer {TransferID}.",
                            transferId);
                    }
                    return (false, "لا يمكن حذف تحويل غير مسودة");
                }

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.StockTransferDetails WHERE TransferID=@ID",
                    new { ID = transferId }, transaction);
                var deletedHeader = await connection.ExecuteAsync(
                    "DELETE FROM dbo.StockTransfers WHERE TransferID=@ID AND TransferStatus=1",
                    new { ID = transferId }, transaction);
                if (deletedHeader != 1)
                    throw new InvalidOperationException("تغيرت حالة التحويل أثناء الحذف؛ أُلغيت العملية.");

                transferNumber = info.TransferNumber;
                transaction.Commit();
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after stock-transfer deletion failed for transfer {TransferID}.",
                        transferId);
                }
                return (false, ex.Message);
            }

            await _audit.WriteAuditLogAsync(userId, 3, "StockTransfers", transferId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"حذف تحويل: {transferNumber}");
            return (true, "تم الحذف بنجاح");
        }

        // ==========================================
        // الجرد
        // ==========================================
        public async Task<List<StockCountListDto>> GetCountsListAsync()
        {
            await RequireCurrentInventoryPermissionAsync("SCR_COUNT", p => p.CanView, "ليس لديك صلاحية عرض أوامر الجرد.");
            using var connection = CreateConnection();
            return (await connection.QueryAsync<StockCountListDto>(
                "SELECT * FROM dbo.vw_StockCountSummary ORDER BY CountDate DESC, CountID DESC")).ToList();
        }

        public async Task<StockCountHeaderDto?> GetCountByIdAsync(int countId)
        {
            await RequireCurrentInventoryPermissionAsync("SCR_COUNT", p => p.CanView, "ليس لديك صلاحية عرض أوامر الجرد.");
            using var connection = CreateConnection();
            var sql = @"SELECT sc.CountID, sc.CountNumber, sc.CountDate, sc.CountStatus, sc.CountType,
                            sc.WarehouseID, w.WarehouseNameAr, sc.CategoryID, cat.CategoryNameAr,
                            sc.CountedBy, sc.SupervisorID, sc.ApprovedBy, sc.ApprovedDate,
                            sc.Notes, sc.CreatedBy, sc.CreatedDate
                        FROM dbo.StockCounts sc
                        INNER JOIN dbo.Warehouses w ON sc.WarehouseID=w.WarehouseID
                        LEFT JOIN dbo.ItemCategories cat ON sc.CategoryID=cat.CategoryID
                        WHERE sc.CountID=@ID";
            return await connection.QueryFirstOrDefaultAsync<StockCountHeaderDto>(sql, new { ID = countId });
        }

        public async Task<List<StockCountDetailDto>> GetCountDetailsAsync(int countId)
        {
            await RequireCurrentInventoryPermissionAsync("SCR_COUNT", p => p.CanView, "ليس لديك صلاحية عرض أوامر الجرد.");
            using var connection = CreateConnection();
            var sql = @"SELECT scd.CountDetailID, scd.CountID, scd.LineNumber,
                            scd.ItemID, i.ItemCode, i.ItemNameAr,
                            scd.UnitID, u.UnitNameAr AS UnitName,
                            scd.SystemQty, scd.CountedQty,
                            ISNULL(scd.CountedQty, 0) - ISNULL(scd.SystemQty, 0) AS Variance,
                            (ISNULL(scd.CountedQty, 0) - ISNULL(scd.SystemQty, 0)) * ISNULL(scd.UnitCost, 0) AS VarianceValue,
                            scd.UnitCost, scd.BatchNumber, scd.AdjustmentApproved, scd.Notes
                        FROM dbo.StockCountDetails scd
                        INNER JOIN dbo.Items i ON scd.ItemID=i.ItemID
                        INNER JOIN dbo.Units u ON scd.UnitID=u.UnitID
                        WHERE scd.CountID=@ID ORDER BY scd.LineNumber";
            return (await connection.QueryAsync<StockCountDetailDto>(sql, new { ID = countId })).ToList();
        }

        public async Task<string> GenerateCountNumberAsync()
        {
            await RequireCurrentInventoryPermissionAsync("SCR_COUNT", p => p.CanAdd, "ليس لديك صلاحية إنشاء أمر جرد.");
            using var connection = CreateConnection();
            try
            {
                var result = await connection.QueryFirstOrDefaultAsync<string>(
                    "DECLARE @N NVARCHAR(50); EXEC sp_GetNextNumber N'SC', @N OUTPUT; SELECT @N;");
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً للجرد.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم الجرد (SC). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل SC والإجراء المخزن.", ex);
            }
        }

        public async Task<int> InsertCountAsync(StockCountHeaderDto header, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_COUNT", p => p.CanAdd, "ليس لديك صلاحية إنشاء أمر جرد.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            int newId;
            try
            {
                newId = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.StockCounts (CountNumber, CountDate, WarehouseID, CountType, CategoryID,
                        CountStatus, CountedBy, SupervisorID, Notes, CreatedBy, CreatedDate)
                      VALUES (@CountNumber, @CountDate, @WarehouseID, @CountType, NULLIF(@CategoryID, 0),
                        1, NULLIF(@CountedBy, 0), NULLIF(@SupervisorID, 0), @Notes, @UserID, GETDATE());
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new { header.CountNumber, header.CountDate, header.WarehouseID,
                        CountType = header.CountType == 0 ? 1 : header.CountType,
                        header.CategoryID, header.CountedBy, header.SupervisorID, header.Notes, UserID = userId },
                    transaction);

                // Version argument is a fail-closed handshake: an older loader rejects
                // the extra parameter, so an item-only count cannot be created by mistake.
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_LoadItemsForStockCount @CountID=@CountID, @WarehouseID=@WarehouseID, @CategoryID=@CategoryID, @BatchAwareVersion=@BatchAwareVersion",
                    new { CountID = newId, header.WarehouseID, header.CategoryID, BatchAwareVersion = (byte)1 },
                    transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after creating stock count {CountNumber}.",
                        header.CountNumber);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 1, "StockCounts", newId.ToString(),
                moduleName: "SCR_COUNT", description: $"إنشاء أمر جرد: {header.CountNumber}");
            return newId;
        }

        public async Task UpdateCountedQtyAsync(int countDetailId, decimal? countedQty, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_COUNT", p => p.CanEdit, "ليس لديك صلاحية تعديل كميات الجرد.");
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE dbo.StockCountDetails SET CountedQty=@Qty WHERE CountDetailID=@ID",
                new { ID = countDetailId, Qty = countedQty });
        }

        public async Task UpdateCountedQtiesBulkAsync(int countId, List<(int DetailID, decimal? CountedQty)> items, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_COUNT", p => p.CanEdit, "ليس لديك صلاحية تعديل كميات الجرد.");
            using var connection = CreateConnection();
            foreach (var item in items)
                await connection.ExecuteAsync(
                    "UPDATE dbo.StockCountDetails SET CountedQty=@Qty WHERE CountDetailID=@ID AND CountID=@CountID",
                    new { ID = item.DetailID, Qty = item.CountedQty, CountID = countId });

            await connection.ExecuteAsync(
                "UPDATE dbo.StockCounts SET CountStatus=3, ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE CountID=@ID AND CountStatus IN (1,2)",
                new { ID = countId, UserID = userId });

            await _audit.WriteAuditLogAsync(userId, 2, "StockCounts", countId.ToString(),
                moduleName: "SCR_COUNT", description: "تحديث كميات الجرد الفعلية");
        }

        public async Task ApproveAdjustmentsAsync(int countId, List<int> approvedDetailIds, int userId, int? employeeId = null)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_COUNT", p => p.CanApprove, "ليس لديك صلاحية اعتماد تسويات الجرد.");
            var detailIds = (approvedDetailIds ?? new List<int>()).Distinct().ToList();
            if (detailIds.Count == 0) throw new Exception("حدد تسوية واحدة على الأقل لاعتمادها.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            (string CountNumber, int Status) info;

            try
            {
                info = await connection.QueryFirstOrDefaultAsync<(string CountNumber, int Status)>(
                    "SELECT CountNumber, CountStatus FROM dbo.StockCounts WITH (UPDLOCK,HOLDLOCK) WHERE CountID=@ID",
                    new { ID = countId }, transaction);
                if (string.IsNullOrWhiteSpace(info.CountNumber)) throw new Exception("أمر الجرد غير موجود");
                if (info.Status != 3) throw new Exception("لا يمكن اعتماد جرد غير مكتمل");

                var validVarianceCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.StockCountDetails AS scd WITH (UPDLOCK,HOLDLOCK)
WHERE scd.CountID=@CountID
  AND scd.CountDetailID IN @DetailIDs
  AND scd.SystemQty IS NOT NULL
  AND scd.CountedQty IS NOT NULL
  AND scd.CountedQty >= 0
  AND scd.CountedQty <> scd.SystemQty",
                    new { CountID = countId, DetailIDs = detailIds }, transaction);
                if (validVarianceCount != detailIds.Count)
                    throw new InvalidOperationException("بعض أسطر التسوية لا تنتمي للجرد أو لا تحتوي فرقًا صالحًا؛ أعد تحميل الجرد قبل الاعتماد.");

                var unmappedBatchCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.StockCountDetails AS scd
INNER JOIN dbo.StockCounts AS sc ON sc.CountID=scd.CountID
WHERE scd.CountID=@CountID
  AND scd.CountDetailID IN @DetailIDs
  AND
  (
      (
          EXISTS
          (
              SELECT 1 FROM dbo.InventoryBatchBalance AS bb
              WHERE bb.ItemID=scd.ItemID AND bb.WarehouseID=sc.WarehouseID
          )
          AND
          (
              scd.BatchNumber IS NULL
              OR NOT EXISTS
              (
                  SELECT 1 FROM dbo.InventoryBatchBalance AS bb
                  WHERE bb.ItemID=scd.ItemID
                    AND bb.WarehouseID=sc.WarehouseID
                    AND bb.BatchNumber=scd.BatchNumber
              )
          )
      )
      OR
      (
          NOT EXISTS
          (
              SELECT 1 FROM dbo.InventoryBatchBalance AS bb
              WHERE bb.ItemID=scd.ItemID AND bb.WarehouseID=sc.WarehouseID
          )
          AND scd.BatchNumber IS NOT NULL
      )
  )",
                    new { CountID = countId, DetailIDs = detailIds }, transaction);
                if (unmappedBatchCount > 0)
                    throw new InvalidOperationException("تعذر اعتماد التسوية: سطر الصنف لا يحدد باتشًا قائمًا، أو أن رقم الباتش لم يعد مطابقًا للمخزون.");

                await connection.ExecuteAsync(
                    "UPDATE dbo.StockCountDetails SET AdjustmentApproved=0 WHERE CountID=@CountID",
                    new { CountID = countId }, transaction);

                var approvedCount = await connection.ExecuteAsync(@"
UPDATE dbo.StockCountDetails
SET AdjustmentApproved=1
WHERE CountID=@CountID
  AND CountDetailID IN @DetailIDs
  AND SystemQty IS NOT NULL
  AND CountedQty IS NOT NULL
  AND CountedQty >= 0
  AND CountedQty<>SystemQty",
                    new { CountID = countId, DetailIDs = detailIds }, transaction);
                if (approvedCount != detailIds.Count)
                    throw new InvalidOperationException("تعذر تثبيت اعتماد كل أسطر التسوية؛ لم يتم تطبيق الجرد.");

                // The version argument makes an updated application fail closed against
                // the old aggregate-only procedure signature. The SQL procedure performs
                // the batch and aggregate updates atomically.
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_ExecuteStockCountAdjustments @CountID=@CountID, @UserID=@UserID, @BatchAwareVersion=@BatchAwareVersion",
                    new { CountID = countId, UserID = userId, BatchAwareVersion = (byte)1 },
                    transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after stock-count adjustments approval failed for count {CountID}.",
                        countId);
                }
                throw;
            }

            try
            {
                await _notif.CreateForPermissionHoldersAsync(
                    notificationType: 1,
                    title: "تم اعتماد الجرد",
                    message: $"تم اعتماد أمر الجرد رقم {info.CountNumber} وتطبيق التسويات",
                    priority: 2,
                    permissionModuleCode: "SCR_COUNT",
                    requiredPermission: ModulePermissionRequirement.View,
                    relatedModule: "SCR_COUNT",
                    relatedRecordId: countId,
                    createdBy: userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create the notification after approving a stock count.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "StockCounts", countId.ToString(),
                moduleName: "SCR_COUNT", description: $"اعتماد جرد وتطبيق تسويات: {info.CountNumber}");
        }

        public async Task<(bool Success, string Message)> DeleteCountAsync(int countId, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_COUNT", p => p.CanDelete, "ليس لديك صلاحية حذف أمر جرد.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string CountNumber, int Status)>(
                    "SELECT CountNumber, CountStatus FROM dbo.StockCounts WHERE CountID=@ID", new { ID = countId }, transaction);
                if (string.IsNullOrWhiteSpace(info.CountNumber)) return (false, "أمر الجرد غير موجود");
                if (info.Status >= 4) return (false, "لا يمكن حذف جرد معتمد");
                await connection.ExecuteAsync("DELETE FROM dbo.StockCountDetails WHERE CountID=@ID", new { ID = countId }, transaction);
                await connection.ExecuteAsync("DELETE FROM dbo.StockCounts WHERE CountID=@ID", new { ID = countId }, transaction);
                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 3, "StockCounts", countId.ToString(),
                    moduleName: "SCR_COUNT", description: $"حذف جرد: {info.CountNumber}");
                return (true, "تم الحذف بنجاح");
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after stock-count deletion failed for count {CountID}.",
                        countId);
                }
                return (false, ex.Message);
            }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportBalancesToExcelAsync(List<StockBalanceDto> balances, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_STOCK", p => p.CanExport, "ليس لديك صلاحية تصدير أرصدة المخزون.");
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("أرصدة المخزون");
            ws.RightToLeft = true; ws.Style.Font.FontName = "Cairo";
            ws.Cell(1, 1).Value = "تقرير أرصدة المخزون — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 9).Merge().Style.Font.Bold = true;
            var headers = new[] { "#", "الكود", "الصنف", "المخزن", "الرصيد", "المتاح", "التكلفة", "القيمة", "الحالة" };
            for (int i = 0; i < headers.Length; i++)
            { var c = ws.Cell(3, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
              c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b"); c.Style.Font.FontColor = XLColor.White; }
            int row = 4, n = 0;
            foreach (var b in balances)
            { n++; ws.Cell(row, 1).Value = n; ws.Cell(row, 2).Value = b.ItemCode ?? "";
              ws.Cell(row, 3).Value = b.ItemNameAr ?? ""; ws.Cell(row, 4).Value = b.WarehouseNameAr ?? "";
              ws.Cell(row, 5).Value = b.CurrentQty; ws.Cell(row, 6).Value = b.AvailableQty;
              ws.Cell(row, 7).Value = b.AverageCost; ws.Cell(row, 8).Value = b.TotalValue;
              ws.Cell(row, 9).Value = b.StockAlertName ?? ""; row++; }
            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream(); wb.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, "InventoryBalance", moduleName: "SCR_STOCK",
                description: $"تصدير {balances.Count} رصيد مخزون");
            return stream.ToArray();
        }

        public async Task<byte[]> ExportMovementsToExcelAsync(List<StockMovementDto> movements, int userId)
        {
            await RequireInventoryPermissionAsync(userId, "SCR_TRANS", p => p.CanExport, "ليس لديك صلاحية تصدير حركات المخزون.");
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("حركات المخزون");
            ws.RightToLeft = true; ws.Style.Font.FontName = "Cairo";
            var headers = new[] { "#", "التاريخ", "النوع", "الاتجاه", "الكود", "الصنف", "المخزن", "الكمية", "التكلفة", "المستند" };
            for (int i = 0; i < headers.Length; i++)
            { var c = ws.Cell(1, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
              c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b"); c.Style.Font.FontColor = XLColor.White; }
            int row = 2, n = 0;
            foreach (var m in movements)
            { n++; ws.Cell(row, 1).Value = n; ws.Cell(row, 2).Value = m.TransactionDate.ToString("dd/MM/yyyy HH:mm");
              ws.Cell(row, 3).Value = m.TransactionTypeName ?? ""; ws.Cell(row, 4).Value = m.Direction ?? "";
              ws.Cell(row, 5).Value = m.ItemCode ?? ""; ws.Cell(row, 6).Value = m.ItemNameAr ?? "";
              ws.Cell(row, 7).Value = m.WarehouseNameAr ?? ""; ws.Cell(row, 8).Value = m.Quantity;
              ws.Cell(row, 9).Value = m.TotalCost; ws.Cell(row, 10).Value = m.SourceDocNumber ?? ""; row++; }
            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream(); wb.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, "InventoryTransactions", moduleName: "SCR_TRANS",
                description: $"تصدير {movements.Count} حركة مخزون");
            return stream.ToArray();
        }
        // ==========================================
// فحص وإرسال تنبيهات المخزون
// ==========================================
public async Task<int> CheckAndSendStockAlertsAsync(int userId)
{
            await RequireInventoryPermissionAsync(userId, "SCR_STOCK", p => p.CanView, "ليس لديك صلاحية عرض تنبيهات المخزون.");
    using var connection = CreateConnection();
    var alertCount = 0;

    try
    {
        var outOfStock = (await connection.QueryAsync<(int ItemID, string ItemCode, string ItemNameAr, string WarehouseNameAr)>(
            @"SELECT ib.ItemID, i.ItemCode, i.ItemNameAr, w.WarehouseNameAr
              FROM dbo.InventoryBalance ib
              INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON ib.WarehouseID = w.WarehouseID
              WHERE ib.CurrentQty <= 0 AND i.IsActive = 1 AND i.MinStockLevel > 0")).ToList();

        foreach (var item in outOfStock)
        {
            try
            {
                var created = await _notif.CreateAlertForPermissionHoldersIfMissingAsync(
                    notificationType: 1,
                    title: $"نفد المخزون — {item.ItemNameAr}",
                    message: $"الصنف {item.ItemCode} - {item.ItemNameAr} نفد في مخزن {item.WarehouseNameAr}",
                    priority: 1,
                    permissionModuleCode: "SCR_STOCK",
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: item.ItemID,
                    createdBy: userId);
                if (created > 0) alertCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create an out-of-stock inventory alert.");
            }
        }

        var belowMin = (await connection.QueryAsync<(int ItemID, string ItemCode, string ItemNameAr, string WarehouseNameAr, decimal CurrentQty, decimal MinStockLevel)>(
            @"SELECT ib.ItemID, i.ItemCode, i.ItemNameAr, w.WarehouseNameAr, ib.CurrentQty, i.MinStockLevel
              FROM dbo.InventoryBalance ib
              INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON ib.WarehouseID = w.WarehouseID
              WHERE ib.CurrentQty > 0 AND ib.CurrentQty <= i.MinStockLevel
                AND i.MinStockLevel > 0 AND i.IsActive = 1")).ToList();

        foreach (var item in belowMin)
        {
            try
            {
                var created = await _notif.CreateAlertForPermissionHoldersIfMissingAsync(
                    notificationType: 1,
                    title: $"مخزون منخفض — {item.ItemNameAr}",
                    message: $"الصنف {item.ItemCode} - {item.ItemNameAr} في {item.WarehouseNameAr}\nالرصيد: {item.CurrentQty:#,##0.##} | الحد الأدنى: {item.MinStockLevel:#,##0.##}",
                    priority: 2,
                    permissionModuleCode: "SCR_STOCK",
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: item.ItemID,
                    createdBy: userId);
                if (created > 0) alertCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create a low-stock inventory alert.");
            }
        }

        var nearExpiry = (await connection.QueryAsync<(string ItemNameAr, string ItemCode, string BatchNumber, DateTime ExpiryDate, string WarehouseNameAr, int DaysToExpiry)>(
            @"SELECT i.ItemNameAr, i.ItemCode, bb.BatchNumber, bb.ExpiryDate, w.WarehouseNameAr,
                     DATEDIFF(DAY, GETDATE(), bb.ExpiryDate) AS DaysToExpiry
              FROM dbo.InventoryBatchBalance bb
              INNER JOIN dbo.Items i ON bb.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON bb.WarehouseID = w.WarehouseID
              WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
                AND bb.ExpiryDate > CAST(GETDATE() AS DATE)
                AND bb.ExpiryDate <= DATEADD(DAY, 30, GETDATE())")).ToList();

        foreach (var batch in nearExpiry)
        {
            try
            {
                var created = await _notif.CreateAlertForPermissionHoldersIfMissingAsync(
                    notificationType: 4,
                    title: $"قرب انتهاء الصلاحية — {batch.ItemNameAr}",
                    message: $"الصنف {batch.ItemCode} باتش {batch.BatchNumber} في {batch.WarehouseNameAr}\nينتهي بعد {batch.DaysToExpiry} يوم ({batch.ExpiryDate:dd/MM/yyyy})",
                    priority: 1,
                    permissionModuleCode: "SCR_STOCK",
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: 0,
                    createdBy: userId);
                if (created > 0) alertCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create a near-expiry inventory alert.");
            }
        }

        var expired = (await connection.QueryAsync<(string ItemNameAr, string ItemCode, string BatchNumber, DateTime ExpiryDate, string WarehouseNameAr)>(
            @"SELECT i.ItemNameAr, i.ItemCode, bb.BatchNumber, bb.ExpiryDate, w.WarehouseNameAr
              FROM dbo.InventoryBatchBalance bb
              INNER JOIN dbo.Items i ON bb.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON bb.WarehouseID = w.WarehouseID
              WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
                AND bb.ExpiryDate <= CAST(GETDATE() AS DATE)")).ToList();

        foreach (var batch in expired)
        {
            try
            {
                var created = await _notif.CreateAlertForPermissionHoldersIfMissingAsync(
                    notificationType: 4,
                    title: $"مخزون منتهي الصلاحية — {batch.ItemNameAr}",
                    message: $"الصنف {batch.ItemCode} باتش {batch.BatchNumber} في {batch.WarehouseNameAr}\nانتهت صلاحيته في {batch.ExpiryDate:dd/MM/yyyy}",
                    priority: 1,
                    permissionModuleCode: "SCR_STOCK",
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: 0,
                    createdBy: userId);
                if (created > 0) alertCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create an expired-stock inventory alert.");
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Stock alert scan failed; returning the number of alerts created before failure.");
    }

    return alertCount;
}
    // ==========================================
// إضافة رصيد افتتاحي لصنف في مخزن
// ==========================================
public async Task SetOpeningBalanceAsync(int itemId, int warehouseId, decimal qty, decimal unitCost, int userId)
{
            await RequireInventoryPermissionAsync(userId, "SCR_STOCK", p => p.CanAdd, "ليس لديك صلاحية إدخال أرصدة افتتاحية للمخزون.");
    using var connection = CreateConnection();

    if (await ExistsBalanceAsync(connection, itemId, warehouseId))
    {
        throw new Exception("يوجد رصيد بالفعل لهذا الصنف في هذا المخزن. استخدم تسوية الجرد لتعديل الرصيد.");
    }

    await connection.ExecuteAsync(
        @"INSERT INTO dbo.InventoryBalance (ItemID, WarehouseID, CurrentQty, AverageCost, LastMovementDate, ModifiedDate)
          VALUES (@ItemID, @WarehouseID, @Qty, @UnitCost, GETDATE(), GETDATE())",
        new { ItemID = itemId, WarehouseID = warehouseId, Qty = qty, UnitCost = unitCost });

    // تحديث تكلفة الصنف
    await connection.ExecuteAsync(
        "UPDATE dbo.Items SET AverageCost = @Cost, ModifiedDate = GETDATE() WHERE ItemID = @ID AND ISNULL(AverageCost, 0) = 0",
        new { ID = itemId, Cost = unitCost });

    // حركة مخزون
    await connection.ExecuteAsync(
        @"INSERT INTO dbo.InventoryTransactions
          (TransactionDate, TransactionType, ItemID, WarehouseID, Quantity, UnitID, UnitCost,
           BalanceBefore, BalanceAfter, SourceDocType, Notes, CreatedBy, CreatedDate)
          VALUES (GETDATE(), 9, @ItemID, @WarehouseID, @Qty,
           (SELECT PrimaryUnitID FROM dbo.Items WHERE ItemID = @ItemID),
           @UnitCost, 0, @Qty, N'OB', N'رصيد افتتاحي', @UserID, GETDATE())",
        new { ItemID = itemId, WarehouseID = warehouseId, Qty = qty, UnitCost = unitCost, UserID = userId });

    await _audit.WriteAuditLogAsync(userId, 1, "InventoryBalance", itemId.ToString(),
        moduleName: "SCR_STOCK",
        description: $"رصيد افتتاحي: صنف {itemId} في مخزن {warehouseId} — كمية {qty:#,##0.##}");
}

private async Task<bool> ExistsBalanceAsync(SqlConnection conn, int itemId, int warehouseId)
{
    var count = await conn.QueryFirstOrDefaultAsync<int>(
        "SELECT COUNT(*) FROM dbo.InventoryBalance WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID",
        new { ItemID = itemId, WarehouseID = warehouseId });
    return count > 0;
}

// ==========================================
// إضافة أرصدة افتتاحية دفعة واحدة
// ==========================================
public async Task SetOpeningBalancesBulkAsync(List<OpeningBalanceDto> balances, int userId)
{
    await RequireInventoryPermissionAsync(userId, "SCR_STOCK", p => p.CanAdd, "ليس لديك صلاحية إدخال أرصدة افتتاحية للمخزون.");

    foreach (var b in balances)
    {
        try
        {
            await SetOpeningBalanceAsync(b.ItemID, b.WarehouseID, b.Quantity, b.UnitCost, userId);
        }
        catch { /* تخطي الأصناف الموجودة */ }
    }
}

        // ==========================================
        // Helpers
        // ==========================================
        public static string GetTransferStatusName(int s) => s switch { 1 => "مسودة", 2 => "معتمد", 3 => "تم التحويل", 4 => "ملغي", _ => "غير محدد" };
        public static string GetTransferStatusColor(int s) => s switch { 1 => "#64748b", 2 => "#f59e0b", 3 => "#10b981", 4 => "#ef4444", _ => "#6b7280" };
        public static string GetCountStatusName(int s) => s switch { 1 => "مسودة", 2 => "قيد الجرد", 3 => "مكتمل", 4 => "معتمد", 5 => "ملغي", _ => "غير محدد" };
        public static string GetCountStatusColor(int s) => s switch { 1 => "#64748b", 2 => "#3b82f6", 3 => "#f59e0b", 4 => "#10b981", 5 => "#ef4444", _ => "#6b7280" };
        public static string GetAlertColor(int a) => a switch { 0 => "#10b981", 1 => "#f59e0b", 2 => "#f97316", 3 => "#ef4444", _ => "#6b7280" };
        public static string GetExpiryColor(int a) => a switch { 0 => "#10b981", 1 => "#f59e0b", 2 => "#f97316", 3 => "#ef4444", _ => "#6b7280" };
    }
}