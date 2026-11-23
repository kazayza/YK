using Dapper;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class PurchaseInvoiceService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;
        private readonly ModuleAuthorizationService _authorization;
        private readonly ILogger<PurchaseInvoiceService> _logger;

        public PurchaseInvoiceService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif,
            ModuleAuthorizationService authorization,
            ILogger<PurchaseInvoiceService> logger)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
            _authorization = authorization;
            _logger = logger;
        }

        private Task RequireInvoicePermissionAsync(int userId, Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireUserPermissionAsync(userId, "SCR_PINV", predicate, deniedMessage);

        private Task RequireCurrentInvoicePermissionAsync(Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireCurrentUserPermissionAsync("SCR_PINV", predicate, deniedMessage);

        private static Task EnsureLocalCurrencyAsync(
            SqlConnection connection, SqlTransaction transaction, int? currencyId, decimal exchangeRate)
            => LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                connection, transaction, currencyId, exchangeRate);

        private static async Task EnsureLinkedPurchaseOrderLocalCurrencyAsync(
            SqlConnection connection, SqlTransaction? transaction,
            int? purchaseOrderId, int? grnId)
        {
            var purchaseOrderIds = new HashSet<int>();
            if (purchaseOrderId.GetValueOrDefault() > 0)
                purchaseOrderIds.Add(purchaseOrderId.GetValueOrDefault());

            if (grnId.GetValueOrDefault() > 0)
            {
                var grnPurchaseOrderId = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT PurchaseOrderID
                      FROM dbo.GoodsReceiptNotes WITH (UPDLOCK,HOLDLOCK)
                      WHERE GRNID=@GRNID",
                    new { GRNID = grnId }, transaction);
                var linkedPurchaseOrderId = grnPurchaseOrderId.GetValueOrDefault();
                if (linkedPurchaseOrderId > 0)
                    purchaseOrderIds.Add(linkedPurchaseOrderId);
            }

            foreach (var id in purchaseOrderIds)
            {
                var order = await connection.QueryFirstOrDefaultAsync<(
                    int? PurchaseOrderID, int? CurrencyID, decimal ExchangeRate)>(
                    @"SELECT PurchaseOrderID, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate
                      FROM dbo.PurchaseOrders WITH (UPDLOCK,HOLDLOCK)
                      WHERE PurchaseOrderID=@ID",
                    new { ID = id }, transaction);
                if (!order.PurchaseOrderID.HasValue)
                    throw new InvalidOperationException("أمر الشراء المرتبط بفاتورة المشتريات أو إذن الاستلام غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, transaction, order.CurrencyID, order.ExchangeRate);
            }
        }

        // ==========================================
        // قائمة الفواتير
        // ==========================================
        public async Task<List<PurchaseInvoiceListDto>> GetInvoicesListAsync()
        {
            await RequireCurrentInvoicePermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض فواتير المشتريات.");
            using var connection = CreateConnection();
            var sql = @"SELECT
                            InvoiceID, InvoiceNumber, InvoiceDate,
                            InvoiceStatus, InvoiceSource, InvoiceSourceName,
                            PaymentStatus, PaymentStatusName,
                            SupplierID, SupplierCode, SupplierNameAr,
                            PurchaseOrderID, LinkedPONumber,
                            GRNID, LinkedGRNNumber, SupplierInvoiceNo,
                            SubTotal, TaxAmount, TotalAmount, PaidAmount, RemainingAmount,
                            DueDate, IsOverdue, DaysOverdue,
                            PaymentTermName, CurrencyCode,
                            ItemCount, RejectionReason, CreatedDate
                        FROM dbo.vw_PurchaseInvoiceSummary
                        ORDER BY InvoiceDate DESC, InvoiceID DESC";
            var result = await connection.QueryAsync<PurchaseInvoiceListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب فاتورة واحدة
        // ==========================================
        public async Task<PurchaseInvoiceHeaderDto?> GetInvoiceByIdAsync(int invoiceId)
        {
            await RequireCurrentInvoicePermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض فواتير المشتريات.");
            using var connection = CreateConnection();
            var sql = @"SELECT
                            pi.InvoiceID, pi.InvoiceNumber, pi.InvoiceDate,
                            pi.InvoiceStatus,
                            ISNULL(pi.InvoiceSource, 1) AS InvoiceSource,
                            pi.PaymentStatus,
                            pi.SupplierID, s.SupplierNameAr, s.SupplierCode,
                            pi.PurchaseOrderID, po.PONumber AS LinkedPONumber,
                            pi.GRNID, grn.GRNNumber AS LinkedGRNNumber,
                            pi.SupplierInvoiceNo, pi.SupplierInvoiceDate,
                            pi.PaymentTermID, pt.TermNameAr AS PaymentTermName,
                            pi.DueDate,
                            pi.CurrencyID, c.CurrencyCode, c.Symbol AS CurrencySymbol,
                            pi.ExchangeRate,
                            pi.SubTotal, pi.DiscountPercent, pi.DiscountAmount,
                            pi.TaxableAmount, pi.TaxAmount,
                            pi.ShippingCost, pi.OtherCosts,
                            pi.TotalAmount, pi.PaidAmount,
                            pi.SubmittedBy, pi.SubmittedDate,
                            pi.ApprovedBy, pi.ApprovedDate,
                            app.FullNameAr AS ApprovedByName,
                            pi.RejectedBy, pi.RejectedDate, pi.RejectionReason,
                            pi.Notes,
                            pi.CreatedBy, pi.CreatedDate, pi.ModifiedDate
                        FROM dbo.PurchaseInvoices pi
                        INNER JOIN dbo.Suppliers s ON pi.SupplierID = s.SupplierID
                        LEFT JOIN dbo.PurchaseOrders po ON pi.PurchaseOrderID = po.PurchaseOrderID
                        LEFT JOIN dbo.GoodsReceiptNotes grn ON pi.GRNID = grn.GRNID
                        LEFT JOIN dbo.PaymentTerms pt ON pi.PaymentTermID = pt.PaymentTermID
                        LEFT JOIN dbo.Currencies c ON pi.CurrencyID = c.CurrencyID
                        LEFT JOIN dbo.Employees app ON pi.ApprovedBy = app.EmployeeID
                        WHERE pi.InvoiceID = @ID";
            return await connection.QueryFirstOrDefaultAsync<PurchaseInvoiceHeaderDto>(sql, new { ID = invoiceId });
        }

        // ==========================================
        // تفاصيل الفاتورة
        // ==========================================
        public async Task<List<PurchaseInvoiceDetailDto>> GetInvoiceDetailsAsync(int invoiceId)
        {
            await RequireCurrentInvoicePermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض فواتير المشتريات.");
            using var connection = CreateConnection();
            var sql = @"SELECT
                            pid.InvoiceDetailID, pid.InvoiceID, pid.LineNumber,
                            pid.ItemID, i.ItemCode, i.ItemNameAr,
                            pid.UnitID, u.UnitNameAr AS UnitName,
                            pid.WarehouseID, w.WarehouseNameAr,
                            pid.Quantity, pid.UnitPrice,
                            pid.DiscountPercent, pid.DiscountAmount,
                            pid.LineTotal, pid.TaxRate, pid.TaxAmount, pid.LineTotalWithTax,
                            pid.BatchNumber, pid.ExpiryDate,
                            pid.GRNDetailID, pid.PODetailID,
                            pid.Notes
                        FROM dbo.PurchaseInvoiceDetails pid
                        INNER JOIN dbo.Items i ON pid.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON pid.UnitID = u.UnitID
                        LEFT JOIN dbo.Warehouses w ON pid.WarehouseID = w.WarehouseID
                        WHERE pid.InvoiceID = @ID
                        ORDER BY pid.LineNumber";
            var result = await connection.QueryAsync<PurchaseInvoiceDetailDto>(sql, new { ID = invoiceId });
            return result.ToList();
        }

        // ==========================================
        // توليد رقم الفاتورة
        // ==========================================
        public async Task<string> GenerateInvoiceNumberAsync()
        {
            await RequireCurrentInvoicePermissionAsync(p => p.CanAdd, "ليس لديك صلاحية إنشاء فاتورة مشتريات.");
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                            EXEC sp_GetNextNumber N'PI', @NextNum OUTPUT;
                            SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لفاتورة المشتريات.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم فاتورة المشتريات (PI). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل PI والإجراء المخزن.", ex);
            }
        }

        // ==========================================
        // إضافة فاتورة جديدة
        // ==========================================
        public async Task<int> InsertInvoiceAsync(PurchaseInvoiceHeaderDto header, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إنشاء فاتورة مشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var sql = @"INSERT INTO dbo.PurchaseInvoices
                        (
                            InvoiceNumber, InvoiceDate, SupplierID,
                            PurchaseOrderID, GRNID,
                            SupplierInvoiceNo, SupplierInvoiceDate,
                            PaymentTermID, CurrencyID, ExchangeRate,
                            DiscountPercent, ShippingCost, OtherCosts,
                            InvoiceStatus, InvoiceSource, PaymentStatus,
                            Notes, CreatedBy, CreatedDate
                        )
                        VALUES
                        (
                            @InvoiceNumber, @InvoiceDate, @SupplierID,
                            NULLIF(@PurchaseOrderID, 0), NULLIF(@GRNID, 0),
                            @SupplierInvoiceNo, @SupplierInvoiceDate,
                            NULLIF(@PaymentTermID, 0), NULLIF(@CurrencyID, 0), @ExchangeRate,
                            @DiscountPercent, @ShippingCost, @OtherCosts,
                            @InvoiceStatus, @InvoiceSource, 1,
                            @Notes, @UserID, GETDATE()
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            int newId;
            try
            {
                await EnsureLocalCurrencyAsync(connection, transaction, header.CurrencyID, header.ExchangeRate);
                await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                    connection, transaction, header.PurchaseOrderID, header.GRNID);
                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    header.InvoiceNumber,
                    header.InvoiceDate,
                    header.SupplierID,
                    header.PurchaseOrderID,
                    header.GRNID,
                    header.SupplierInvoiceNo,
                    header.SupplierInvoiceDate,
                    header.PaymentTermID,
                    header.CurrencyID,
                    ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                    header.DiscountPercent,
                    header.ShippingCost,
                    header.OtherCosts,
                    InvoiceStatus = 1,
                    InvoiceSource = header.InvoiceSource == 0 ? (header.GRNID > 0 ? 1 : 3) : header.InvoiceSource,
                    header.Notes,
                    UserID = userId
                }, transaction);

                // حساب تاريخ الاستحقاق
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_CalcInvoiceDueDate @InvoiceID",
                    new { InvoiceID = newId }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after inserting purchase invoice {PurchaseInvoiceNumber}.",
                        header.InvoiceNumber);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 1, "PurchaseInvoices", newId.ToString(),
                moduleName: "SCR_PINV",
                description: $"إنشاء فاتورة مشتريات: {header.InvoiceNumber}");

            return newId;
        }

        // ==========================================
        // تحديث فاتورة
        // ==========================================
        public async Task UpdateInvoiceAsync(PurchaseInvoiceHeaderDto header, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل فواتير المشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var sql = @"UPDATE dbo.PurchaseInvoices SET
                            InvoiceDate = @InvoiceDate,
                            SupplierID = @SupplierID,
                            SupplierInvoiceNo = @SupplierInvoiceNo,
                            SupplierInvoiceDate = @SupplierInvoiceDate,
                            PaymentTermID = NULLIF(@PaymentTermID, 0),
                            CurrencyID = NULLIF(@CurrencyID, 0),
                            ExchangeRate = @ExchangeRate,
                            DiscountPercent = @DiscountPercent,
                            ShippingCost = @ShippingCost,
                            OtherCosts = @OtherCosts,
                            Notes = @Notes,
                            ModifiedBy = @UserID,
                            ModifiedDate = GETDATE()
                        WHERE InvoiceID = @InvoiceID";

            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(
                    int? InvoiceStatus, int? CurrencyID, decimal ExchangeRate,
                    int? PurchaseOrderID, int? GRNID)>(
                    @"SELECT InvoiceStatus, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate,
                             PurchaseOrderID, GRNID
                      FROM dbo.PurchaseInvoices WITH (UPDLOCK,HOLDLOCK)
                      WHERE InvoiceID=@ID",
                    new { ID = header.InvoiceID }, transaction);

                if (current.InvoiceStatus != 1)
                    throw new Exception("لا يمكن تعديل فاتورة ليست في حالة مسودة");

                await EnsureLocalCurrencyAsync(connection, transaction, current.CurrencyID, current.ExchangeRate);
                await EnsureLocalCurrencyAsync(connection, transaction, header.CurrencyID, header.ExchangeRate);
                await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                    connection, transaction, current.PurchaseOrderID, current.GRNID);
                await connection.ExecuteAsync(sql, new
                {
                    header.InvoiceID,
                    header.InvoiceDate,
                    header.SupplierID,
                    header.SupplierInvoiceNo,
                    header.SupplierInvoiceDate,
                    header.PaymentTermID,
                    header.CurrencyID,
                    ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                    header.DiscountPercent,
                    header.ShippingCost,
                    header.OtherCosts,
                    header.Notes,
                    UserID = userId
                }, transaction);

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID",
                    new { InvoiceID = header.InvoiceID }, transaction);
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_CalcInvoiceDueDate @InvoiceID",
                    new { InvoiceID = header.InvoiceID }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after updating purchase invoice {PurchaseInvoiceID}.",
                        header.InvoiceID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", header.InvoiceID.ToString(),
                moduleName: "SCR_PINV",
                description: $"تعديل فاتورة مشتريات: {header.InvoiceNumber}");
        }

        // ==========================================
        // إضافة سطر
        // ==========================================
        public async Task<int> InsertDetailAsync(PurchaseInvoiceDetailEditDto detail, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إضافة تفاصيل فاتورة مشتريات.");
            if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
            if (detail.UnitID <= 0) throw new Exception("اختر الوحدة");
            if (detail.Quantity <= 0) throw new Exception("أدخل الكمية");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            int newId;
            try
            {
                var invoice = await connection.QueryFirstOrDefaultAsync<(
                    int? InvoiceStatus, int? CurrencyID, decimal ExchangeRate,
                    int? PurchaseOrderID, int? GRNID)>(
                    @"SELECT InvoiceStatus, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate,
                             PurchaseOrderID, GRNID
                      FROM dbo.PurchaseInvoices WITH (UPDLOCK,HOLDLOCK) WHERE InvoiceID=@ID",
                    new { ID = detail.InvoiceID }, transaction);

                if (invoice.InvoiceStatus != 1)
                    throw new Exception("لا يمكن تعديل فاتورة ليست في حالة مسودة");
                await EnsureLocalCurrencyAsync(
                    connection, transaction, invoice.CurrencyID, invoice.ExchangeRate);
                await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                    connection, transaction, invoice.PurchaseOrderID, invoice.GRNID);

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseInvoiceDetails WHERE InvoiceID = @ID",
                    new { ID = detail.InvoiceID }, transaction);

                detail.LineNumber = maxLine + 1;

                var sql = @"INSERT INTO dbo.PurchaseInvoiceDetails
                            (
                                InvoiceID, LineNumber, ItemID, UnitID, WarehouseID,
                                Quantity, UnitPrice, DiscountPercent, TaxRate,
                                BatchNumber, ExpiryDate,
                                GRNDetailID, PODetailID, Notes
                            )
                            VALUES
                            (
                                @InvoiceID, @LineNumber, @ItemID, @UnitID, NULLIF(@WarehouseID, 0),
                                @Quantity, @UnitPrice, @DiscountPercent, @TaxRate,
                                @BatchNumber, @ExpiryDate,
                                @GRNDetailID, @PODetailID, @Notes
                            );
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    detail.InvoiceID,
                    detail.LineNumber,
                    detail.ItemID,
                    detail.UnitID,
                    detail.WarehouseID,
                    detail.Quantity,
                    detail.UnitPrice,
                    detail.DiscountPercent,
                    TaxRate = detail.TaxRate <= 0 ? 14 : detail.TaxRate,
                    detail.BatchNumber,
                    detail.ExpiryDate,
                    detail.GRNDetailID,
                    detail.PODetailID,
                    detail.Notes
                }, transaction);

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID",
                    new { InvoiceID = detail.InvoiceID }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after inserting a purchase invoice detail for invoice {PurchaseInvoiceID}.",
                        detail.InvoiceID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 1, "PurchaseInvoiceDetails", newId.ToString(),
                moduleName: "SCR_PINV",
                description: $"إضافة سطر في فاتورة مشتريات رقم {detail.InvoiceID}");

            return newId;
        }

        // ==========================================
        // تعديل سطر
        // ==========================================
        public async Task UpdateDetailAsync(PurchaseInvoiceDetailEditDto detail, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل تفاصيل فاتورة مشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            (int InvoiceID, int InvoiceStatus, int? CurrencyID, decimal ExchangeRate,
                int? PurchaseOrderID, int? GRNID) invoiceInfo;
            try
            {
                invoiceInfo = await connection.QueryFirstOrDefaultAsync<(
                    int InvoiceID, int InvoiceStatus, int? CurrencyID, decimal ExchangeRate,
                    int? PurchaseOrderID, int? GRNID)>(
                    @"SELECT pi.InvoiceID, pi.InvoiceStatus, pi.CurrencyID,
                             ISNULL(pi.ExchangeRate,1) AS ExchangeRate, pi.PurchaseOrderID, pi.GRNID
                      FROM dbo.PurchaseInvoices pi WITH (UPDLOCK,HOLDLOCK)
                      INNER JOIN dbo.PurchaseInvoiceDetails pid WITH (UPDLOCK,HOLDLOCK)
                        ON pi.InvoiceID=pid.InvoiceID
                      WHERE pid.InvoiceDetailID=@ID",
                    new { ID = detail.InvoiceDetailID }, transaction);

                if (invoiceInfo.InvoiceID == 0)
                    throw new Exception("السطر غير موجود");
                if (invoiceInfo.InvoiceStatus != 1)
                    throw new Exception("لا يمكن تعديل سطر في فاتورة ليست مسودة");
                await EnsureLocalCurrencyAsync(
                    connection, transaction, invoiceInfo.CurrencyID, invoiceInfo.ExchangeRate);
                await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                    connection, transaction, invoiceInfo.PurchaseOrderID, invoiceInfo.GRNID);

                var sql = @"UPDATE dbo.PurchaseInvoiceDetails SET
                                ItemID = @ItemID, UnitID = @UnitID,
                                WarehouseID = NULLIF(@WarehouseID, 0),
                                Quantity = @Quantity, UnitPrice = @UnitPrice,
                                DiscountPercent = @DiscountPercent, TaxRate = @TaxRate,
                                BatchNumber = @BatchNumber, ExpiryDate = @ExpiryDate,
                                Notes = @Notes
                            WHERE InvoiceDetailID = @InvoiceDetailID";

                await connection.ExecuteAsync(sql, new
                {
                    detail.InvoiceDetailID,
                    detail.ItemID,
                    detail.UnitID,
                    detail.WarehouseID,
                    detail.Quantity,
                    detail.UnitPrice,
                    detail.DiscountPercent,
                    TaxRate = detail.TaxRate <= 0 ? 14 : detail.TaxRate,
                    detail.BatchNumber,
                    detail.ExpiryDate,
                    detail.Notes
                }, transaction);

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID",
                    new { InvoiceID = invoiceInfo.InvoiceID }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after updating purchase invoice detail {PurchaseInvoiceDetailID}.",
                        detail.InvoiceDetailID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoiceDetails", detail.InvoiceDetailID.ToString(),
                moduleName: "SCR_PINV",
                description: $"تعديل سطر في فاتورة مشتريات رقم {invoiceInfo.InvoiceID}");
        }

        // ==========================================
        // حذف سطر
        // ==========================================
        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanDelete, "ليس لديك صلاحية حذف تفاصيل فاتورة مشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            (int InvoiceID, int InvoiceStatus) info;
            try
            {
                info = await connection.QueryFirstOrDefaultAsync<(int InvoiceID, int InvoiceStatus)>(
                    @"SELECT pid.InvoiceID, pi.InvoiceStatus
                      FROM dbo.PurchaseInvoiceDetails pid
                      INNER JOIN dbo.PurchaseInvoices pi ON pid.InvoiceID = pi.InvoiceID
                      WHERE pid.InvoiceDetailID = @ID",
                    new { ID = detailId }, transaction);

                if (info.InvoiceID == 0) throw new Exception("السطر غير موجود");
                if (info.InvoiceStatus != 1) throw new Exception("لا يمكن حذف سطر من فاتورة ليست مسودة");

                var deleted = await connection.ExecuteAsync(
                    "DELETE FROM dbo.PurchaseInvoiceDetails WHERE InvoiceDetailID = @ID",
                    new { ID = detailId }, transaction);
                if (deleted != 1) throw new Exception("السطر غير موجود أو تعذر حذفه");

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID",
                    new { InvoiceID = info.InvoiceID }, transaction);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after deleting purchase invoice detail {PurchaseInvoiceDetailID} failed.",
                        detailId);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "PurchaseInvoiceDetails", detailId.ToString(),
                moduleName: "SCR_PINV", description: "حذف سطر من فاتورة مشتريات");
        }

        // ==========================================
        // تحميل سطور من GRN
        // ==========================================
        public async Task<int> ImportLinesFromGRNAsync(int invoiceId, int grnId, List<GRNLineForInvoiceDto> lines, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إضافة أصناف لفاتورة مشتريات.");
            if (lines == null || !lines.Any(l => l.IsSelected)) return 0;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var invoice = await connection.QueryFirstOrDefaultAsync<(
                    int? InvoiceStatus, int? CurrencyID, decimal ExchangeRate,
                    int? PurchaseOrderID, int? GRNID)>(
                    @"SELECT InvoiceStatus, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate,
                             PurchaseOrderID, GRNID
                      FROM dbo.PurchaseInvoices WITH (UPDLOCK,HOLDLOCK) WHERE InvoiceID=@ID",
                    new { ID = invoiceId }, transaction);

                if (invoice.InvoiceStatus != 1) throw new Exception("لا يمكن تحميل سطور على فاتورة ليست مسودة");
                await EnsureLocalCurrencyAsync(
                    connection, transaction, invoice.CurrencyID, invoice.ExchangeRate);
                await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                    connection, transaction, invoice.PurchaseOrderID, invoice.GRNID);
                await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                    connection, transaction, null, grnId);

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseInvoiceDetails WHERE InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                var inserted = 0;

                foreach (var line in lines.Where(l => l.IsSelected))
                {
                    maxLine++;
                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.PurchaseInvoiceDetails
                          (InvoiceID, LineNumber, ItemID, UnitID, WarehouseID,
                           Quantity, UnitPrice, DiscountPercent, TaxRate,
                           BatchNumber, ExpiryDate, GRNDetailID, PODetailID)
                          VALUES
                          (@InvoiceID, @LineNumber, @ItemID, @UnitID, @WarehouseID,
                           @Quantity, @UnitPrice, 0, @TaxRate,
                           @BatchNumber, @ExpiryDate, @GRNDetailID, @PODetailID)",
                        new
                        {
                            InvoiceID = invoiceId,
                            LineNumber = maxLine,
                            line.ItemID,
                            line.UnitID,
                            line.WarehouseID,
                            Quantity = line.AcceptedQty ?? line.ReceivedQty,
                            UnitPrice = line.InvoiceUnitPrice > 0 ? line.InvoiceUnitPrice : line.UnitCost,
                            TaxRate = line.InvoiceTaxRate,
                            line.BatchNumber,
                            line.ExpiryDate,
                            line.GRNDetailID,
                            line.PODetailID
                        }, transaction);
                    inserted++;
                }

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID",
                    new { InvoiceID = invoiceId }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(userId, 1, "PurchaseInvoiceDetails", invoiceId.ToString(),
                    moduleName: "SCR_PINV",
                    description: $"تحميل {inserted} سطر من إذن استلام إلى فاتورة المشتريات");

                return inserted;
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after importing goods receipt {GoodsReceiptID} lines into purchase invoice {PurchaseInvoiceID} failed.",
                        grnId,
                        invoiceId);
                }
                throw;
            }
        }

        // ==========================================
        // إرسال للاعتماد
        // ==========================================
        public async Task SubmitForApprovalAsync(int invoiceId, int userId, int? employeeId = null)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية إرسال فواتير المشتريات للاعتماد.");
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(
                string InvoiceNumber, int InvoiceStatus, int DetailCount, int? CurrencyID, decimal ExchangeRate,
                int? PurchaseOrderID, int? GRNID)>(
                @"SELECT pi.InvoiceNumber, pi.InvoiceStatus,
                    (SELECT COUNT(*) FROM dbo.PurchaseInvoiceDetails d WHERE d.InvoiceID=pi.InvoiceID) AS DetailCount,
                    pi.CurrencyID, ISNULL(pi.ExchangeRate,1) AS ExchangeRate,
                    pi.PurchaseOrderID, pi.GRNID
                  FROM dbo.PurchaseInvoices pi WHERE pi.InvoiceID=@ID",
                new { ID = invoiceId });

            if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
            if (info.InvoiceStatus != 1) throw new Exception("لا يمكن إرسال فاتورة غير مسودة");
            if (info.DetailCount == 0) throw new Exception("لا يمكن إرسال فاتورة بدون أصناف");
            await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                connection, null, info.CurrencyID, info.ExchangeRate);
            await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                connection, null, info.PurchaseOrderID, info.GRNID);

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseInvoices SET
                    InvoiceStatus = 2,
                    SubmittedBy = @EmployeeID,
                    SubmittedDate = GETDATE(),
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE InvoiceID = @ID",
                new { ID = invoiceId, EmployeeID = employeeId, UserID = userId });

            try
            {
                await _notif.CreateForPermissionHoldersAsync(
                    notificationType: 3,
                    title: "فاتورة مشتريات بانتظار الاعتماد",
                    message: $"فاتورة المشتريات رقم {info.InvoiceNumber} بانتظار الاعتماد",
                    priority: 1,
                    permissionModuleCode: "SCR_PINV",
                    requiredPermission: ModulePermissionRequirement.Approve,
                    relatedModule: "SCR_PINV",
                    relatedRecordId: invoiceId,
                    createdBy: userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create the pending-approval notification for a purchase invoice.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"إرسال فاتورة مشتريات للاعتماد: {info.InvoiceNumber}");
        }

        // ==========================================
        // اعتماد الفاتورة + القيد المحاسبي
        // ==========================================
        public async Task ApproveAsync(int invoiceId, int userId, int? employeeId = null)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanApprove, "ليس لديك صلاحية اعتماد فواتير المشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string invoiceNumber = "";
            int? grnId = null;
            int? createdBy = null;
            int supplierId = 0;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(
                    string InvoiceNumber, int InvoiceStatus, int? GRNID, int? CreatedBy, int SupplierID,
                    decimal ExchangeRate, bool IsDefaultCurrency, int DetailCount, int? PurchaseOrderID)>(
                    @"SELECT pi.InvoiceNumber, pi.InvoiceStatus, pi.GRNID, pi.CreatedBy, pi.SupplierID,
                        ISNULL(pi.ExchangeRate,1) AS ExchangeRate,
                        CASE WHEN ISNULL(pi.CurrencyID,0)=0 THEN CONVERT(bit,1)
                             ELSE CONVERT(bit,ISNULL(cur.IsDefault,0)) END AS IsDefaultCurrency,
                        (SELECT COUNT(*) FROM dbo.PurchaseInvoiceDetails d WHERE d.InvoiceID=pi.InvoiceID) AS DetailCount,
                        pi.PurchaseOrderID
                      FROM dbo.PurchaseInvoices pi WITH (UPDLOCK, HOLDLOCK)
                      LEFT JOIN dbo.Currencies cur WITH (UPDLOCK, HOLDLOCK) ON cur.CurrencyID = pi.CurrencyID
                      WHERE pi.InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
                if (info.InvoiceStatus != 2) throw new Exception("لا يمكن اعتماد فاتورة ليست في انتظار الاعتماد");
                if (info.DetailCount == 0) throw new Exception("لا يمكن اعتماد فاتورة بدون أصناف");
                if (!info.IsDefaultCurrency || Math.Abs(info.ExchangeRate - 1m) > 0.000001m)
                    throw new InvalidOperationException("لا يمكن اعتماد فاتورة مشتريات بعملة غير العملة الافتراضية أو بسعر صرف مختلف عن 1.");
                await EnsureLinkedPurchaseOrderLocalCurrencyAsync(
                    connection, transaction, info.PurchaseOrderID, info.GRNID);

                invoiceNumber = info.InvoiceNumber;
                grnId = info.GRNID;
                createdBy = info.CreatedBy;
                supplierId = info.SupplierID;

                var existingJournalCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE SourceDocNumber=@InvoiceNumber",
                    new { InvoiceNumber = invoiceNumber }, transaction);
                if (existingJournalCount > 0)
                    throw new InvalidOperationException("يوجد قيد مرتبط برقم فاتورة المورد مسبقاً؛ أُوقف الاعتماد لمنع القيد المكرر.");

                // 1) إعادة حساب الإجماليات
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID",
                    new { InvoiceID = invoiceId }, transaction);

                // 2) تحديث حالة الفاتورة
                var invoiceStatusUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseInvoices SET
                        InvoiceStatus = 3,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID, ModifiedDate = GETDATE()
                      WHERE InvoiceID = @ID AND InvoiceStatus = 2",
                    new { ID = invoiceId, EmployeeID = employeeId, UserID = userId }, transaction);
                if (invoiceStatusUpdated != 1)
                    throw new InvalidOperationException("تغيرت حالة الفاتورة؛ أُوقف الاعتماد.");

                // 3) ربط GRN بالفاتورة
                if (grnId.HasValue && grnId.Value > 0)
                {
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_LinkGRNToInvoice @GRNID, @InvoiceID",
                        new { GRNID = grnId.Value, InvoiceID = invoiceId }, transaction);
                }

                // 4) تحديث رصيد المورد
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_UpdateSupplierBalanceAfterInvoice @SupplierID",
                    new { SupplierID = supplierId }, transaction);

                // 5) إنشاء القيد المحاسبي؛ أي فشل يمنع اعتماد الفاتورة ويُرجع كل آثار المعاملة.
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_CreatePurchaseInvoiceJournal @InvoiceID, @UserID",
                    new { InvoiceID = invoiceId, UserID = userId }, transaction);

                var linkedJournalIds = (await connection.QueryAsync<int>(
                    @"SELECT JournalID
                      FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE SourceDocID=@InvoiceID AND SourceDocNumber=@InvoiceNumber",
                    new { InvoiceID = invoiceId, InvoiceNumber = invoiceNumber }, transaction)).ToList();
                if (linkedJournalIds.Count != 1)
                    throw new InvalidOperationException(
                        $"يجب أن ينتج عن اعتماد فاتورة المشتريات قيد واحد مرتبط بها؛ وُجد {linkedJournalIds.Count}. تم إلغاء الاعتماد.");

                var journal = await connection.QueryFirstOrDefaultAsync<(
                    int JournalStatus, bool IsAutoGenerated, int? FiscalYearID, int? PeriodID,
                    DateTime JournalDate, decimal TotalDebit, decimal TotalCredit)>(
                    @"SELECT JournalStatus, ISNULL(IsAutoGenerated,0) AS IsAutoGenerated,
                             FiscalYearID, PeriodID, JournalDate,
                             ISNULL(TotalDebit,0) AS TotalDebit, ISNULL(TotalCredit,0) AS TotalCredit
                      FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE JournalID=@JournalID",
                    new { JournalID = linkedJournalIds[0] }, transaction);

                if (!journal.IsAutoGenerated)
                    throw new InvalidOperationException("القيد المرتبط بفاتورة المشتريات ليس قيداً أوتوماتيكياً؛ أُلغي الاعتماد.");
                if (journal.JournalStatus != 2)
                    throw new InvalidOperationException("قيد فاتورة المشتريات غير مُرحّل؛ أُلغي الاعتماد.");
                if (!journal.FiscalYearID.HasValue || journal.FiscalYearID.Value <= 0 ||
                    !journal.PeriodID.HasValue || journal.PeriodID.Value <= 0)
                    throw new InvalidOperationException("قيد فاتورة المشتريات غير مرتبط بسنة وفترة مالية؛ أُلغي الاعتماد.");

                var journalLines = await connection.QueryFirstOrDefaultAsync<(
                    int DetailCount, decimal DebitTotal, decimal CreditTotal, int InvalidDetailCount)>(
                    @"SELECT ISNULL(SUM(CASE WHEN ISNULL(jd.DebitAmount,0)>0 OR ISNULL(jd.CreditAmount,0)>0 THEN 1 ELSE 0 END),0) AS DetailCount,
                             ISNULL(SUM(jd.DebitAmount),0) AS DebitTotal,
                             ISNULL(SUM(jd.CreditAmount),0) AS CreditTotal,
                             ISNULL(SUM(CASE WHEN jd.DebitAmount IS NULL OR jd.CreditAmount IS NULL
                                      OR jd.DebitAmount<0 OR jd.CreditAmount<0
                                      OR (jd.DebitAmount>0 AND jd.CreditAmount>0)
                                      OR ((jd.DebitAmount>0 OR jd.CreditAmount>0)
                                          AND (jd.AccountID IS NULL OR jd.AccountID<=0
                                               OR coa.AccountID IS NULL
                                               OR ISNULL(coa.IsActive,1)<>1
                                               OR (
                                                    ISNULL(coa.IsDetailAccount,0)<>1
                                                    AND NOT (
                                                        ISNULL(coa.AccountCode,N'')=N'2101'
                                                        AND ISNULL(coa.IsSystemAccount,0)=1
                                                        AND ISNULL(coa.AccountType,0)=2
                                                        AND UPPER(LTRIM(RTRIM(ISNULL(coa.LinkedEntityType,N''))))=N'SUPPLIER'
                                                    )
                                               )
                                               OR (ISNULL(coa.CostCenterRequired,0)=1 AND ISNULL(jd.CostCenterID,0)<=0)
                                               OR jd.CostCenterID<0
                                               OR (jd.CostCenterID>0 AND (cc.CostCenterID IS NULL OR ISNULL(cc.IsActive,1)<>1)))
                                          )
                                      THEN 1 ELSE 0 END),0) AS InvalidDetailCount
                      FROM dbo.JournalEntryDetails jd WITH (UPDLOCK, HOLDLOCK)
                      LEFT JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK) ON coa.AccountID=jd.AccountID
                      LEFT JOIN dbo.CostCenters cc WITH (UPDLOCK,HOLDLOCK) ON cc.CostCenterID=jd.CostCenterID
                      WHERE jd.JournalID=@JournalID",
                    new { JournalID = linkedJournalIds[0] }, transaction);
                if (journalLines.DetailCount < 2 || journalLines.InvalidDetailCount > 0 ||
                    journal.TotalDebit <= 0m || journal.TotalCredit <= 0m ||
                    Math.Abs(journal.TotalDebit - journal.TotalCredit) > 0.01m ||
                    Math.Abs(journalLines.DebitTotal - journalLines.CreditTotal) > 0.01m ||
                    Math.Abs(journal.TotalDebit - journalLines.DebitTotal) > 0.01m ||
                    Math.Abs(journal.TotalCredit - journalLines.CreditTotal) > 0.01m)
                    throw new InvalidOperationException(
                        $"قيد فاتورة المشتريات غير مكتمل أو يحتوي تفاصيل غير صالحة أو لا يطابق رأسه (مدين {journalLines.DebitTotal:#,##0.00} / دائن {journalLines.CreditTotal:#,##0.00}). أُلغي الاعتماد.");

                var journalPeriodIsOpen = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.FiscalYears fy WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK, HOLDLOCK)
                        ON ap.FiscalYearID=fy.FiscalYearID
                      WHERE fy.FiscalYearID=@FiscalYearID
                        AND ap.PeriodID=@PeriodID
                        AND fy.YearStatus=1 AND ap.PeriodStatus=1
                        AND CONVERT(date,@JournalDate) BETWEEN CONVERT(date,fy.StartDate) AND CONVERT(date,fy.EndDate)
                        AND CONVERT(date,@JournalDate) BETWEEN CONVERT(date,ap.StartDate) AND CONVERT(date,ap.EndDate)",
                    new
                    {
                        FiscalYearID = journal.FiscalYearID.Value,
                        PeriodID = journal.PeriodID.Value,
                        JournalDate = journal.JournalDate.Date
                    }, transaction);
                if (journalPeriodIsOpen != 1)
                    throw new InvalidOperationException("سنة أو فترة قيد فاتورة المشتريات مغلقة أو لا تطابق تاريخ القيد؛ أُلغي الاعتماد.");

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after purchase-invoice approval failed for invoice {InvoiceID}.",
                        invoiceId);
                }
                throw;
            }

            try
            {
                await _notif.MarkRelatedAsActionedAsync("SCR_PINV", invoiceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after approving a purchase invoice.");
            }

            try
            {
                if (createdBy.HasValue)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم اعتماد فاتورة المشتريات",
                        message: $"تم اعتماد فاتورة المشتريات رقم {invoiceNumber}",
                        priority: 2,
                        targetUserId: createdBy.Value,
                        permissionModuleCode: "SCR_PINV",
                        relatedModule: "SCR_PINV",
                        relatedRecordId: invoiceId,
                        createdBy: userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify the creator after approving a purchase invoice.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"اعتماد فاتورة مشتريات: {invoiceNumber}");
        }

        // ==========================================
        // رفض الفاتورة
        // ==========================================
        public async Task RejectAsync(int invoiceId, string rejectionReason, int userId, int? employeeId = null)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanApprove, "ليس لديك صلاحية رفض فواتير المشتريات.");
            if (string.IsNullOrWhiteSpace(rejectionReason)) throw new Exception("أدخل سبب الرفض");

            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, int? CreatedBy)>(
                "SELECT InvoiceNumber, InvoiceStatus, CreatedBy FROM dbo.PurchaseInvoices WHERE InvoiceID = @ID",
                new { ID = invoiceId });

            if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
            if (info.InvoiceStatus != 2) throw new Exception("لا يمكن رفض فاتورة ليست في انتظار الاعتماد");

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseInvoices SET
                    InvoiceStatus = 1, -- ترجع مسودة عشان يعدلها
                    RejectedBy = @EmployeeID,
                    RejectedDate = GETDATE(),
                    RejectionReason = @Reason,
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE InvoiceID = @ID",
                new { ID = invoiceId, EmployeeID = employeeId, Reason = rejectionReason, UserID = userId });

            try
            {
                await _notif.MarkRelatedAsActionedAsync("SCR_PINV", invoiceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after rejecting a purchase invoice.");
            }

            try
            {
                if (info.CreatedBy.HasValue)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم رفض فاتورة المشتريات",
                        message: $"تم رفض فاتورة المشتريات رقم {info.InvoiceNumber}. السبب: {rejectionReason}",
                        priority: 1,
                        targetUserId: info.CreatedBy.Value,
                        permissionModuleCode: "SCR_PINV",
                        relatedModule: "SCR_PINV",
                        relatedRecordId: invoiceId,
                        createdBy: userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify the creator after rejecting a purchase invoice.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"رفض فاتورة مشتريات: {info.InvoiceNumber} — {rejectionReason}");
        }

        // ==========================================
        // إلغاء الفاتورة
        // ==========================================
        public async Task CancelAsync(int invoiceId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanCancel, "ليس لديك صلاحية إلغاء فواتير المشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string invoiceNumber = "";
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(
                    string InvoiceNumber, int InvoiceStatus, decimal PaidAmount, int SupplierID, int? GRNID)>(
                    @"SELECT InvoiceNumber, InvoiceStatus, ISNULL(PaidAmount,0) AS PaidAmount, SupplierID, GRNID
                      FROM dbo.PurchaseInvoices WITH (UPDLOCK, HOLDLOCK) WHERE InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
                if (info.InvoiceStatus == 7) throw new Exception("الفاتورة ملغية بالفعل");
                if (info.PaidAmount > 0) throw new Exception("لا يمكن إلغاء فاتورة تم الدفع عليها؛ ألغِ سندات الصرف المخصصة لها أولًا.");
                invoiceNumber = info.InvoiceNumber;

                var wasApproved = info.InvoiceStatus >= 3 && info.InvoiceStatus <= 6;
                if (wasApproved)
                {
                    // الفاتورة المعتمدة لها قيد مرحّل ورصيد مورد وأثر على إذن الاستلام؛
                    // الإلغاء بلا عكس كان يترك كل ذلك قائمًا. يُعكس القيد ويُحرَّر الاستلام هنا.
                    var employeeId = await connection.ExecuteScalarAsync<int?>(
                        "SELECT EmployeeID FROM dbo.SystemUsers WITH (UPDLOCK,HOLDLOCK) WHERE UserID=@UserID",
                        new { UserID = userId }, transaction);
                    if (!employeeId.HasValue || employeeId.Value <= 0)
                        throw new InvalidOperationException("لا يمكن إلغاء فاتورة معتمدة لأن المستخدم الحالي غير مرتبط بموظف.");

                    await ReversePurchaseInvoiceJournalAsync(
                        connection, transaction, invoiceId, info.InvoiceNumber, userId, employeeId.Value);
                }

                var cancelled = await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseInvoices SET
                        InvoiceStatus = 7,
                        ModifiedBy = @UserID, ModifiedDate = GETDATE()
                      WHERE InvoiceID = @ID AND InvoiceStatus = @OldStatus",
                    new { ID = invoiceId, UserID = userId, OldStatus = info.InvoiceStatus }, transaction);
                if (cancelled != 1)
                    throw new InvalidOperationException("تغيرت حالة الفاتورة أثناء الإلغاء؛ أُلغيت العملية.");

                if (wasApproved)
                {
                    var linkedGrnId = info.GRNID.GetValueOrDefault();
                    if (linkedGrnId > 0)
                    {
                        // تحرير إذن الاستلام المحبوس في حالة «مرتبط بفاتورة» ليعاد استخدامه أو إلغاؤه.
                        await connection.ExecuteAsync(
                            @"UPDATE dbo.GoodsReceiptNotes SET GRNStatus = 2,
                                ModifiedBy = @UserID, ModifiedDate = GETDATE()
                              WHERE GRNID = @GRNID AND GRNStatus = 3",
                            new { GRNID = linkedGrnId, UserID = userId }, transaction);
                    }

                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_UpdateSupplierBalanceAfterInvoice @SupplierID",
                        new { SupplierID = info.SupplierID }, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after cancelling purchase invoice {PurchaseInvoiceID} failed.",
                        invoiceId);
                }
                throw;
            }

            try
            {
                await _notif.MarkRelatedAsActionedAsync("SCR_PINV", invoiceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after cancelling a purchase invoice.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"إلغاء فاتورة مشتريات: {invoiceNumber}");
        }

        private async Task ReversePurchaseInvoiceJournalAsync(
            SqlConnection connection, SqlTransaction transaction,
            int invoiceId, string invoiceNumber, int userId, int employeeId)
        {
            var linkedJournalIds = (await connection.QueryAsync<int>(
                @"SELECT JournalID
                  FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                  WHERE SourceDocID=@InvoiceID AND SourceDocNumber=@InvoiceNumber",
                new { InvoiceID = invoiceId, InvoiceNumber = invoiceNumber }, transaction)).ToList();
            if (linkedJournalIds.Count != 1)
                throw new InvalidOperationException(
                    $"إلغاء الفاتورة المعتمدة يتطلب قيدًا واحدًا مرتبطًا بها؛ وُجد {linkedJournalIds.Count}. أُوقف الإلغاء حفاظًا على الأرصدة.");
            var originalJournalId = linkedJournalIds[0];

            var original = await connection.QueryFirstOrDefaultAsync<(
                string? JournalNumber, int JournalStatus, bool IsAutoGenerated, DateTime JournalDate)>(
                @"SELECT JournalNumber, JournalStatus, ISNULL(IsAutoGenerated,0) AS IsAutoGenerated, JournalDate
                  FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                  WHERE JournalID=@ID",
                new { ID = originalJournalId }, transaction);
            if (original.JournalStatus != 2 || !original.IsAutoGenerated)
                throw new InvalidOperationException("قيد الفاتورة الأصلي غير مرحّل أو غير أوتوماتيكي؛ أُوقف الإلغاء.");

            var alreadyReversed = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                  WHERE SourceDocType='REVERSAL' AND SourceDocID=@ID AND JournalStatus=2",
                new { ID = originalJournalId }, transaction);
            if (alreadyReversed > 0)
                throw new InvalidOperationException("قيد هذه الفاتورة معكوس مسبقًا؛ لا يمكن عكسه مرتين.");

            var details = (await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit, string? Description, int? CostCenterID)>(
                @"SELECT AccountID, DebitAmount AS Debit, CreditAmount AS Credit, Description, CostCenterID
                  FROM dbo.JournalEntryDetails WHERE JournalID=@ID ORDER BY LineNumber",
                new { ID = originalJournalId }, transaction)).ToList();
            if (details.Count < 2
                || details.Any(d => d.AccountID <= 0 || d.Debit < 0 || d.Credit < 0 || (d.Debit > 0 && d.Credit > 0)))
                throw new InvalidOperationException("سطور قيد الفاتورة الأصلي غير صالحة؛ أُوقف الإلغاء.");
            var totalDebit = details.Sum(d => d.Debit);
            if (totalDebit <= 0 || Math.Abs(totalDebit - details.Sum(d => d.Credit)) > 0.01m)
                throw new InvalidOperationException("قيد الفاتورة الأصلي غير متوازن؛ أُوقف الإلغاء.");

            // تاريخ العكس: تاريخ القيد الأصلي إن بقيت فترته مفتوحة، وإلا اليوم إن كانت فترته مفتوحة.
            async Task<(int FiscalYearID, int PeriodID)?> ResolveOpenPeriodAsync(DateTime date)
            {
                var period = await connection.QueryFirstOrDefaultAsync<(int? FiscalYearID, int? PeriodID)>(
                    @"SELECT TOP 1 fy.FiscalYearID, ap.PeriodID
                      FROM dbo.FiscalYears fy WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK, HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
                      WHERE fy.YearStatus=1 AND ap.PeriodStatus=1
                        AND @Date BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
                        AND @Date BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
                      ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
                    new { Date = date.Date }, transaction);
                return period.FiscalYearID.HasValue && period.PeriodID.HasValue
                    ? (period.FiscalYearID.Value, period.PeriodID.Value)
                    : null;
            }

            var reversalDate = original.JournalDate.Date;
            var resolvedPeriod = await ResolveOpenPeriodAsync(reversalDate);
            if (resolvedPeriod == null)
            {
                reversalDate = DateTime.Today;
                resolvedPeriod = await ResolveOpenPeriodAsync(reversalDate);
            }
            if (resolvedPeriod == null)
                throw new InvalidOperationException("لا توجد فترة مفتوحة لتاريخ القيد الأصلي ولا لتاريخ اليوم؛ افتح فترة أو أغلق الإلغاء لحين فتحها.");

            var reversalNumber = await connection.QueryFirstOrDefaultAsync<string>(
                "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;",
                transaction: transaction);
            if (string.IsNullOrWhiteSpace(reversalNumber))
                throw new InvalidOperationException("تعذر توليد رقم القيد العكسي؛ أُوقف الإلغاء.");

            var reversalId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate, PostedBy, PostedDate)
                  VALUES(@Number, @Date, @Description, @Reference, 'REVERSAL', @OriginalID, @OriginalNumber, @Total, @Total, @FiscalYearID, @PeriodID, 3, 2, 1, @UserID, GETDATE(), @PostedBy, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    Number = reversalNumber,
                    Date = reversalDate,
                    Description = $"عكس قيد فاتورة مشتريات {invoiceNumber} — إلغاء الفاتورة",
                    Reference = original.JournalNumber,
                    OriginalID = originalJournalId,
                    OriginalNumber = original.JournalNumber,
                    Total = totalDebit,
                    FiscalYearID = resolvedPeriod.Value.FiscalYearID,
                    PeriodID = resolvedPeriod.Value.PeriodID,
                    UserID = userId,
                    PostedBy = employeeId
                }, transaction);

            int line = 1;
            foreach (var d in details)
            {
                await connection.ExecuteAsync(
                    "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID) VALUES(@JID, @Line, @AccID, @Debit, @Credit, @Desc, NULLIF(@CCID,0))",
                    new { JID = reversalId, Line = line++, AccID = d.AccountID, Debit = d.Credit, Credit = d.Debit, Desc = d.Description, CCID = d.CostCenterID ?? 0 }, transaction);
            }
            // ملاحظة مقصودة: لا تُحدَّث أرصدة ChartOfAccounts المخزنة هنا، لأن مسار اعتماد فاتورة
            // المشتريات نفسه لا يحدّثها من C# (يتولاها إجراء إنشاء القيد أو لا تحدث)؛ ميزان المراجعة
            // ودفتر الأستاذ يُحسبان من سطور القيود المرحّلة فينعكس العكس فيهما مباشرة.
        }

        // ==========================================
        // حذف فاتورة (مسودة فقط)
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteInvoiceAsync(int invoiceId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanDelete, "ليس لديك صلاحية حذف فواتير المشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus)>(
                    "SELECT InvoiceNumber, InvoiceStatus FROM dbo.PurchaseInvoices WHERE InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) return (false, "الفاتورة غير موجودة");
                if (info.InvoiceStatus != 1) return (false, "لا يمكن حذف فاتورة غير مسودة");

                await connection.ExecuteAsync("DELETE FROM dbo.PurchaseInvoiceDetails WHERE InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);
                await connection.ExecuteAsync("DELETE FROM dbo.PurchaseInvoices WHERE InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(userId, 3, "PurchaseInvoices", invoiceId.ToString(),
                    moduleName: "SCR_PINV", description: $"حذف فاتورة مشتريات: {info.InvoiceNumber}");

                return (true, "تم حذف الفاتورة بنجاح");
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after deleting purchase invoice {PurchaseInvoiceID} failed.",
                        invoiceId);
                }
                return (false, ex.Message);
            }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<PurchaseInvoiceListDto> invoices, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanExport, "ليس لديك صلاحية تصدير فواتير المشتريات.");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("فواتير المشتريات");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير فواتير المشتريات — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 10).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 10).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[] { "#", "رقم الفاتورة", "التاريخ", "المورد", "فاتورة المورد", "الإجمالي", "المدفوع", "المتبقي", "الاستحقاق", "الحالة" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
                cell.Style.Font.FontColor = XLColor.White;
            }

            int row = headerRow + 1;
            int num = 0;
            foreach (var inv in invoices)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = inv.InvoiceNumber ?? "";
                ws.Cell(row, 3).Value = inv.InvoiceDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = inv.SupplierNameAr ?? "";
                ws.Cell(row, 5).Value = inv.SupplierInvoiceNo ?? "—";
                ws.Cell(row, 6).Value = inv.TotalAmount;
                ws.Cell(row, 7).Value = inv.PaidAmount;
                ws.Cell(row, 8).Value = inv.RemainingAmount;
                ws.Cell(row, 9).Value = inv.DueDate?.ToString("dd/MM/yyyy") ?? "—";
                ws.Cell(row, 10).Value = GetStatusName(inv.InvoiceStatus);
                if (num % 2 == 0)
                    ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
                row++;
            }

            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "PurchaseInvoices",
                moduleName: "SCR_PINV",
                description: $"تصدير {invoices.Count} فاتورة مشتريات إلى Excel");

            return stream.ToArray();
        }

        // ==========================================
        // طباعة
        // ==========================================
        public async Task<string> GenerateReportHtmlAsync(int invoiceId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanPrint, "ليس لديك صلاحية طباعة فواتير المشتريات.");
            var header = await GetInvoiceByIdAsync(invoiceId);
            if (header == null) return "<h3>الفاتورة غير موجودة</h3>";
            var details = await GetInvoiceDetailsAsync(invoiceId);

            var detailsHtml = "";
            int n = 0;
            foreach (var d in details)
            {
                n++;
                detailsHtml += $@"<tr>
                    <td style='text-align:center'>{n}</td>
                    <td>{d.ItemCode}</td><td>{d.ItemNameAr}</td><td>{d.UnitName}</td>
                    <td style='text-align:center'>{d.Quantity:#,##0.##}</td>
                    <td style='text-align:left'>{d.UnitPrice:#,##0.00}</td>
                    <td style='text-align:center'>{d.DiscountPercent:#,##0.##}%</td>
                    <td style='text-align:center'>{d.TaxRate:#,##0.##}%</td>
                    <td style='text-align:left'>{d.LineTotalWithTax:#,##0.00}</td></tr>";
            }

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'>
<title>فاتورة مشتريات — {header.InvoiceNumber}</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Cairo','Segoe UI',sans-serif;padding:30px;color:#0f172a;font-size:13px}}
.header{{text-align:center;border-bottom:3px solid #1e293b;padding-bottom:20px;margin-bottom:24px}}
.company{{font-size:20px;font-weight:800}}
.doc-title{{font-size:15px;color:#475569;margin-top:4px}}
.doc-num{{display:inline-block;background:#f1f5f9;padding:4px 16px;border-radius:8px;font-weight:700;color:#3b82f6;margin-top:8px}}
.info-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:20px}}
.info-item{{background:#f8fafc;padding:10px;border-radius:8px;border:1px solid #e2e8f0}}
.info-label{{font-size:10px;color:#64748b;font-weight:600}}
.info-value{{font-size:13px;font-weight:700;margin-top:2px}}
table{{width:100%;border-collapse:collapse;margin-bottom:16px}}
th{{background:#1e293b;color:white;padding:10px;font-size:12px;text-align:right}}
td{{padding:9px 10px;border-bottom:1px solid #e2e8f0;font-size:12px}}
tr:nth-child(even){{background:#f8fafc}}
.total-row{{font-weight:800;background:#f1f5f9 !important}}
.totals-section{{margin-top:16px;display:flex;justify-content:flex-end}}
.totals-box{{background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:16px;min-width:280px}}
.total-line{{display:flex;justify-content:space-between;padding:4px 0;font-size:12px}}
.total-line.grand{{font-weight:800;font-size:14px;border-top:2px solid #1e293b;margin-top:8px;padding-top:8px}}
.footer{{text-align:center;margin-top:30px;padding-top:12px;border-top:1px solid #e2e8f0;font-size:10px;color:#94a3b8}}
</style></head><body>
<div class='header'>
    <div class='company'>مصنع واي كي كوتينج  </div>
    <div class='doc-title'>فاتورة مشتريات</div>
    <div class='doc-num'>{header.InvoiceNumber}</div>
</div>
<div class='info-grid'>
    <div class='info-item'><div class='info-label'>تاريخ الفاتورة</div><div class='info-value'>{header.InvoiceDate:dd/MM/yyyy}</div></div>
    <div class='info-item'><div class='info-label'>المورد</div><div class='info-value'>{header.SupplierNameAr}</div></div>
    <div class='info-item'><div class='info-label'>فاتورة المورد</div><div class='info-value'>{header.SupplierInvoiceNo ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(header.InvoiceStatus)}</div></div>
    <div class='info-item'><div class='info-label'>شروط الدفع</div><div class='info-value'>{header.PaymentTermName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>تاريخ الاستحقاق</div><div class='info-value'>{header.DueDate?.ToString("dd/MM/yyyy") ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>إذن الاستلام</div><div class='info-value'>{header.LinkedGRNNumber ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>أمر الشراء</div><div class='info-value'>{header.LinkedPONumber ?? "—"}</div></div>
</div>
<table>
    <thead><tr><th>#</th><th>الكود</th><th>الصنف</th><th>الوحدة</th><th>الكمية</th><th>السعر</th><th>خصم</th><th>ضريبة</th><th>الإجمالي</th></tr></thead>
    <tbody>{detailsHtml}</tbody>
</table>
<div class='totals-section'>
    <div class='totals-box'>
        <div class='total-line'><span>الإجمالي الفرعي</span><span>{header.SubTotal:#,##0.00}</span></div>
        {(header.DiscountAmount > 0 ? $"<div class='total-line'><span>الخصم</span><span style='color:#ef4444'>- {header.DiscountAmount:#,##0.00}</span></div>" : "")}
        <div class='total-line'><span>الضريبة</span><span>{header.TaxAmount:#,##0.00}</span></div>
        {(header.ShippingCost > 0 ? $"<div class='total-line'><span>الشحن</span><span>{header.ShippingCost:#,##0.00}</span></div>" : "")}
        {(header.OtherCosts > 0 ? $"<div class='total-line'><span>تكاليف أخرى</span><span>{header.OtherCosts:#,##0.00}</span></div>" : "")}
        <div class='total-line grand'><span>الإجمالي الكلي</span><span>{header.TotalAmount:#,##0.00} {(header.CurrencySymbol ?? "ج.م")}</span></div>
        <div class='total-line'><span>المدفوع</span><span>{header.PaidAmount:#,##0.00}</span></div>
        <div class='total-line' style='color:#dc2626;font-weight:700'><span>المتبقي</span><span>{header.RemainingAmount:#,##0.00}</span></div>
    </div>
</div>
{(string.IsNullOrWhiteSpace(header.Notes) ? "" : $"<div style='margin-top:16px'><strong>ملاحظات:</strong> {header.Notes}</div>")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP v2.0</div>
</body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"طباعة فاتورة مشتريات: {header.InvoiceNumber}");

            return html;
        }

        // ==========================================
        // Helpers
        // ==========================================
        public static string GetStatusName(int status) => status switch
        {
            1 => "مسودة", 2 => "مقدمة للاعتماد", 3 => "معتمدة",
            4 => "مدفوعة جزئياً", 5 => "مدفوعة بالكامل",
            6 => "متأخرة", 7 => "ملغية", _ => "غير محدد"
        };

        public static string GetStatusColor(int status) => status switch
        {
            1 => "#64748b", 2 => "#f59e0b", 3 => "#10b981",
            4 => "#6366f1", 5 => "#14b8a6",
            6 => "#ef4444", 7 => "#dc2626", _ => "#6b7280"
        };

        public static string GetPaymentStatusName(int status) => status switch
        {
            1 => "غير مدفوعة", 2 => "مدفوعة جزئياً",
            3 => "مدفوعة بالكامل", _ => "غير محدد"
        };

        public static string GetPaymentStatusColor(int status) => status switch
        {
            1 => "#ef4444", 2 => "#f59e0b", 3 => "#10b981", _ => "#6b7280"
        };

        public static string GetSourceName(int source) => source switch
        {
            1 => "من إذن استلام", 2 => "من أمر شراء",
            3 => "فاتورة مباشرة", _ => "من إذن استلام"
        };

        public static string GetSourceColor(int source) => source switch
        {
            1 => "#0891b2", 2 => "#1d4ed8", 3 => "#f97316", _ => "#0891b2"
        };
    }
}