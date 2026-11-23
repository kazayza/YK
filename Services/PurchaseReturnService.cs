using Dapper;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class PurchaseReturnService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;
        private readonly ModuleAuthorizationService _authorization;
        private readonly ILogger<PurchaseReturnService> _logger;

        public PurchaseReturnService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif,
            ModuleAuthorizationService authorization,
            ILogger<PurchaseReturnService> logger)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
            _authorization = authorization;
            _logger = logger;
        }

        private Task RequireReturnPermissionAsync(int userId, Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireUserPermissionAsync(userId, "SCR_PRET", predicate, deniedMessage);

        private Task RequireCurrentReturnPermissionAsync(Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireCurrentUserPermissionAsync("SCR_PRET", predicate, deniedMessage);

        private static async Task EnsureSourceInvoiceLocalCurrencyAsync(
            SqlConnection connection, SqlTransaction? transaction, int? invoiceId)
        {
            if (invoiceId.GetValueOrDefault() <= 0) return;

            var invoice = await connection.QueryFirstOrDefaultAsync<(
                int? InvoiceID, int? CurrencyID, decimal ExchangeRate,
                int? PurchaseOrderID, int? GRNID)>(
                @"SELECT InvoiceID, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate,
                         PurchaseOrderID, GRNID
                  FROM dbo.PurchaseInvoices WITH (UPDLOCK,HOLDLOCK)
                  WHERE InvoiceID=@ID",
                new { ID = invoiceId }, transaction);
            if (!invoice.InvoiceID.HasValue)
                throw new InvalidOperationException("فاتورة المشتريات الأصلية المرتبطة بالمرتجع غير موجودة.");
            await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                connection, transaction, invoice.CurrencyID, invoice.ExchangeRate);

            var purchaseOrderIds = new HashSet<int>();
            if (invoice.PurchaseOrderID.GetValueOrDefault() > 0)
                purchaseOrderIds.Add(invoice.PurchaseOrderID.GetValueOrDefault());
            if (invoice.GRNID.GetValueOrDefault() > 0)
            {
                var grnPurchaseOrderId = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT PurchaseOrderID FROM dbo.GoodsReceiptNotes WITH (UPDLOCK,HOLDLOCK)
                      WHERE GRNID=@ID",
                    new { ID = invoice.GRNID }, transaction);
                if (grnPurchaseOrderId.GetValueOrDefault() > 0)
                    purchaseOrderIds.Add(grnPurchaseOrderId.GetValueOrDefault());
            }

            foreach (var purchaseOrderId in purchaseOrderIds)
            {
                var order = await connection.QueryFirstOrDefaultAsync<(
                    int? PurchaseOrderID, int? CurrencyID, decimal ExchangeRate)>(
                    @"SELECT PurchaseOrderID, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate
                      FROM dbo.PurchaseOrders WITH (UPDLOCK,HOLDLOCK)
                      WHERE PurchaseOrderID=@ID",
                    new { ID = purchaseOrderId }, transaction);
                if (!order.PurchaseOrderID.HasValue)
                    throw new InvalidOperationException("أمر الشراء المرتبط بفاتورة المرتجع غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, transaction, order.CurrencyID, order.ExchangeRate);
            }
        }

        public async Task<List<PurchaseReturnListDto>> GetReturnsListAsync()
        {
            await RequireCurrentReturnPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض مرتجعات المشتريات.");
            using var connection = CreateConnection();
            var sql = @"SELECT ReturnID, ReturnNumber, ReturnDate, ReturnStatus, ReturnSource, ReturnSourceName,
                            ReturnType, ReturnTypeName, ReturnReason,
                            SupplierID, SupplierCode, SupplierNameAr,
                            InvoiceID, LinkedInvoiceNumber, WarehouseNameAr,
                            SubTotal, TaxAmount, TotalAmount, ItemCount,
                            RejectionReason, CreatedDate
                        FROM dbo.vw_PurchaseReturnSummary
                        ORDER BY ReturnDate DESC, ReturnID DESC";
            return (await connection.QueryAsync<PurchaseReturnListDto>(sql)).ToList();
        }

        public async Task<PurchaseReturnHeaderDto?> GetReturnByIdAsync(int returnId)
        {
            await RequireCurrentReturnPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض مرتجعات المشتريات.");
            using var connection = CreateConnection();
            var sql = @"SELECT pr.ReturnID, pr.ReturnNumber, pr.ReturnDate, pr.ReturnStatus,
                            ISNULL(pr.ReturnSource, 1) AS ReturnSource, pr.ReturnType, pr.ReturnReason,
                            pr.SupplierID, s.SupplierNameAr, s.SupplierCode,
                            pr.InvoiceID, pi.InvoiceNumber AS LinkedInvoiceNumber,
                            pr.WarehouseID, w.WarehouseNameAr,
                            pr.SubTotal, pr.TaxAmount, pr.TotalAmount,
                            pr.SubmittedBy, pr.SubmittedDate,
                            pr.ApprovedBy, pr.ApprovedDate, app.FullNameAr AS ApprovedByName,
                            pr.RejectedBy, pr.RejectedDate, pr.RejectionReason,
                            pr.Notes, pr.CreatedBy, pr.CreatedDate, pr.ModifiedDate
                        FROM dbo.PurchaseReturns pr
                        INNER JOIN dbo.Suppliers s ON pr.SupplierID = s.SupplierID
                        LEFT JOIN dbo.PurchaseInvoices pi ON pr.InvoiceID = pi.InvoiceID
                        LEFT JOIN dbo.Warehouses w ON pr.WarehouseID = w.WarehouseID
                        LEFT JOIN dbo.Employees app ON pr.ApprovedBy = app.EmployeeID
                        WHERE pr.ReturnID = @ID";
            return await connection.QueryFirstOrDefaultAsync<PurchaseReturnHeaderDto>(sql, new { ID = returnId });
        }

        public async Task<List<PurchaseReturnDetailDto>> GetReturnDetailsAsync(int returnId)
        {
            await RequireCurrentReturnPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض مرتجعات المشتريات.");
            using var connection = CreateConnection();
            var sql = @"SELECT prd.ReturnDetailID, prd.ReturnID, prd.LineNumber,
                            prd.ItemID, i.ItemCode, i.ItemNameAr,
                            prd.UnitID, u.UnitNameAr AS UnitName,
                            prd.Quantity, prd.UnitPrice,
                            prd.LineTotal, prd.TaxRate, prd.TaxAmount, prd.LineTotalWithTax,
                            prd.BatchNumber, prd.ReturnReason, prd.ItemCondition,
                            prd.InvoiceDetailID, prd.Notes
                        FROM dbo.PurchaseReturnDetails prd
                        INNER JOIN dbo.Items i ON prd.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON prd.UnitID = u.UnitID
                        WHERE prd.ReturnID = @ID
                        ORDER BY prd.LineNumber";
            return (await connection.QueryAsync<PurchaseReturnDetailDto>(sql, new { ID = returnId })).ToList();
        }

        public async Task<string> GenerateReturnNumberAsync()
        {
            await RequireCurrentReturnPermissionAsync(p => p.CanAdd, "ليس لديك صلاحية إنشاء مرتجع مشتريات.");
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @N NVARCHAR(50); EXEC sp_GetNextNumber N'PRET', @N OUTPUT; SELECT @N;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لمرتجع المشتريات.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم مرتجع المشتريات (PRET). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل PRET والإجراء المخزن.", ex);
            }
        }

        public async Task<int> InsertReturnAsync(PurchaseReturnHeaderDto header, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إنشاء مرتجع مشتريات.");
            using var connection = CreateConnection();
            await EnsureSourceInvoiceLocalCurrencyAsync(connection, null, header.InvoiceID);
            var sql = @"INSERT INTO dbo.PurchaseReturns
                        (ReturnNumber, ReturnDate, SupplierID, InvoiceID, WarehouseID,
                         ReturnReason, ReturnType, ReturnStatus, ReturnSource,
                         Notes, CreatedBy, CreatedDate)
                        VALUES
                        (@ReturnNumber, @ReturnDate, @SupplierID, NULLIF(@InvoiceID, 0), @WarehouseID,
                         @ReturnReason, @ReturnType, 1, @ReturnSource,
                         @Notes, @UserID, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                header.ReturnNumber, header.ReturnDate, header.SupplierID, header.InvoiceID,
                header.WarehouseID, header.ReturnReason,
                ReturnType = header.ReturnType == 0 ? 1 : header.ReturnType,
                ReturnSource = header.ReturnSource == 0 ? (header.InvoiceID > 0 ? 1 : 2) : header.ReturnSource,
                header.Notes, UserID = userId
            });
            await _audit.WriteAuditLogAsync(userId, 1, "PurchaseReturns", newId.ToString(),
                moduleName: "SCR_PRET", description: $"إنشاء مرتجع مشتريات: {header.ReturnNumber}");
            return newId;
        }

        public async Task UpdateReturnAsync(PurchaseReturnHeaderDto header, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل مرتجعات المشتريات.");
            using var connection = CreateConnection();
            var current = await connection.QueryFirstOrDefaultAsync<(
                int? ReturnStatus, int? InvoiceID)>(
                @"SELECT ReturnStatus, InvoiceID FROM dbo.PurchaseReturns WITH (UPDLOCK,HOLDLOCK)
                  WHERE ReturnID=@ID",
                new { ID = header.ReturnID });
            if (current.ReturnStatus != 1) throw new Exception("لا يمكن تعديل مرتجع ليس في حالة مسودة");
            await EnsureSourceInvoiceLocalCurrencyAsync(connection, null, current.InvoiceID);

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseReturns SET
                    ReturnDate = @ReturnDate, ReturnReason = @ReturnReason,
                    ReturnType = @ReturnType, Notes = @Notes,
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE ReturnID = @ReturnID",
                new { header.ReturnID, header.ReturnDate, header.ReturnReason, header.ReturnType, header.Notes, UserID = userId });

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseReturnTotals @ReturnID", new { header.ReturnID });
            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseReturns", header.ReturnID.ToString(),
                moduleName: "SCR_PRET", description: $"تعديل مرتجع: {header.ReturnNumber}");
        }

        public async Task<int> InsertDetailAsync(PurchaseReturnDetailEditDto detail, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إضافة تفاصيل مرتجع مشتريات.");
            using var connection = CreateConnection();
            var current = await connection.QueryFirstOrDefaultAsync<(
                int? ReturnStatus, int? InvoiceID)>(
                @"SELECT ReturnStatus, InvoiceID FROM dbo.PurchaseReturns WITH (UPDLOCK,HOLDLOCK)
                  WHERE ReturnID=@ID",
                new { ID = detail.ReturnID });
            if (current.ReturnStatus != 1) throw new Exception("لا يمكن تعديل مرتجع ليس في حالة مسودة");
            await EnsureSourceInvoiceLocalCurrencyAsync(connection, null, current.InvoiceID);
            if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
            if (detail.Quantity <= 0) throw new Exception("أدخل الكمية");

            var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseReturnDetails WHERE ReturnID = @ID",
                new { ID = detail.ReturnID });
            detail.LineNumber = maxLine + 1;

            var newId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.PurchaseReturnDetails
                  (ReturnID, LineNumber, ItemID, UnitID, Quantity, UnitPrice, TaxRate,
                   BatchNumber, ReturnReason, ItemCondition, InvoiceDetailID, Notes)
                  VALUES
                  (@ReturnID, @LineNumber, @ItemID, @UnitID, @Quantity, @UnitPrice, @TaxRate,
                   @BatchNumber, @ReturnReason, @ItemCondition, @InvoiceDetailID, @Notes);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    detail.ReturnID, detail.LineNumber, detail.ItemID, detail.UnitID,
                    detail.Quantity, detail.UnitPrice,
                    TaxRate = detail.TaxRate <= 0 ? 14 : detail.TaxRate,
                    detail.BatchNumber, detail.ReturnReason,
                    ItemCondition = detail.ItemCondition == 0 ? 1 : detail.ItemCondition,
                    detail.InvoiceDetailID, detail.Notes
                });

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseReturnTotals @ReturnID", new { detail.ReturnID });
            await _audit.WriteAuditLogAsync(userId, 1, "PurchaseReturnDetails", newId.ToString(),
                moduleName: "SCR_PRET", description: $"إضافة سطر في مرتجع رقم {detail.ReturnID}");
            return newId;
        }

        public async Task UpdateDetailAsync(PurchaseReturnDetailEditDto detail, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل تفاصيل مرتجع مشتريات.");
            using var connection = CreateConnection();
            var current = await connection.QueryFirstOrDefaultAsync<(
                int ReturnID, int? ReturnStatus, int? InvoiceID)>(
                @"SELECT pr.ReturnID, pr.ReturnStatus, pr.InvoiceID
                  FROM dbo.PurchaseReturns pr WITH (UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.PurchaseReturnDetails prd WITH (UPDLOCK,HOLDLOCK)
                    ON pr.ReturnID=prd.ReturnID
                  WHERE prd.ReturnDetailID=@ID",
                new { ID = detail.ReturnDetailID });
            if (current.ReturnStatus != 1) throw new Exception("لا يمكن تعديل سطر في مرتجع ليس مسودة");
            await EnsureSourceInvoiceLocalCurrencyAsync(connection, null, current.InvoiceID);

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseReturnDetails SET
                    ItemID = @ItemID, UnitID = @UnitID,
                    Quantity = @Quantity, UnitPrice = @UnitPrice, TaxRate = @TaxRate,
                    BatchNumber = @BatchNumber, ReturnReason = @ReturnReason,
                    ItemCondition = @ItemCondition, Notes = @Notes
                  WHERE ReturnDetailID = @ReturnDetailID",
                new
                {
                    detail.ReturnDetailID, detail.ItemID, detail.UnitID,
                    detail.Quantity, detail.UnitPrice, detail.TaxRate,
                    detail.BatchNumber, detail.ReturnReason, detail.ItemCondition, detail.Notes
                });

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseReturnTotals @ReturnID", new { detail.ReturnID });
            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseReturnDetails", detail.ReturnDetailID.ToString(),
                moduleName: "SCR_PRET", description: $"تعديل سطر في مرتجع رقم {detail.ReturnID}");
        }

        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanDelete, "ليس لديك صلاحية حذف تفاصيل مرتجع مشتريات.");
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(int ReturnID, int ReturnStatus)>(
                @"SELECT prd.ReturnID, pr.ReturnStatus FROM dbo.PurchaseReturnDetails prd
                  INNER JOIN dbo.PurchaseReturns pr ON prd.ReturnID = pr.ReturnID
                  WHERE prd.ReturnDetailID = @ID", new { ID = detailId });
            if (info.ReturnID == 0) throw new Exception("السطر غير موجود");
            if (info.ReturnStatus != 1) throw new Exception("لا يمكن حذف سطر من مرتجع ليس مسودة");

            await connection.ExecuteAsync("DELETE FROM dbo.PurchaseReturnDetails WHERE ReturnDetailID = @ID", new { ID = detailId });
            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseReturnTotals @ReturnID", new { info.ReturnID });
            await _audit.WriteAuditLogAsync(userId, 3, "PurchaseReturnDetails", detailId.ToString(),
                moduleName: "SCR_PRET", description: "حذف سطر من مرتجع مشتريات");
        }

        public async Task<int> ImportLinesFromInvoiceAsync(int returnId, List<InvoiceLineForReturnDto> lines, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إضافة أصناف لمرتجع مشتريات.");
            if (lines == null || !lines.Any(l => l.IsSelected && l.ReturnQty > 0)) return 0;
            using var connection = CreateConnection();
            var returnInvoiceId = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT InvoiceID FROM dbo.PurchaseReturns WITH (UPDLOCK,HOLDLOCK) WHERE ReturnID=@ID",
                new { ID = returnId });
            await EnsureSourceInvoiceLocalCurrencyAsync(connection, null, returnInvoiceId);

            var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseReturnDetails WHERE ReturnID = @ID",
                new { ID = returnId });
            var inserted = 0;

            foreach (var line in lines.Where(l => l.IsSelected && l.ReturnQty > 0))
            {
                maxLine++;
                await connection.ExecuteAsync(
                    @"INSERT INTO dbo.PurchaseReturnDetails
                      (ReturnID, LineNumber, ItemID, UnitID, Quantity, UnitPrice, TaxRate,
                       BatchNumber, ReturnReason, ItemCondition, InvoiceDetailID)
                      VALUES
                      (@ReturnID, @LineNumber, @ItemID, @UnitID, @Quantity, @UnitPrice, @TaxRate,
                       @BatchNumber, @ReturnReason, @ItemCondition, @InvoiceDetailID)",
                    new
                    {
                        ReturnID = returnId, LineNumber = maxLine,
                        line.ItemID, line.UnitID,
                        Quantity = line.ReturnQty, line.UnitPrice, line.TaxRate,
                        line.BatchNumber, line.ReturnReason,
                        ItemCondition = line.ItemCondition == 0 ? 1 : line.ItemCondition,
                        InvoiceDetailID = line.InvoiceDetailID
                    });
                inserted++;
            }

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseReturnTotals @ReturnID", new { ReturnID = returnId });
            await _audit.WriteAuditLogAsync(userId, 1, "PurchaseReturnDetails", returnId.ToString(),
                moduleName: "SCR_PRET", description: $"تحميل {inserted} سطر من فاتورة إلى المرتجع");
            return inserted;
        }

        public async Task SubmitForApprovalAsync(int returnId, int userId, int? employeeId = null)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية إرسال مرتجعات المشتريات للاعتماد.");
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(
                string ReturnNumber, int ReturnStatus, int DetailCount, int? InvoiceID)>(
                @"SELECT pr.ReturnNumber, pr.ReturnStatus,
                    (SELECT COUNT(*) FROM dbo.PurchaseReturnDetails WHERE ReturnID = pr.ReturnID) AS DetailCount,
                    pr.InvoiceID
                  FROM dbo.PurchaseReturns pr WHERE pr.ReturnID = @ID",
                new { ID = returnId });

            if (string.IsNullOrWhiteSpace(info.ReturnNumber)) throw new Exception("المرتجع غير موجود");
            if (info.ReturnStatus != 1) throw new Exception("لا يمكن إرسال مرتجع غير مسودة");
            if (info.DetailCount == 0) throw new Exception("لا يمكن إرسال مرتجع بدون أصناف");
            await EnsureSourceInvoiceLocalCurrencyAsync(connection, null, info.InvoiceID);

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseReturns SET
                    ReturnStatus = 2, SubmittedBy = @EmpID, SubmittedDate = GETDATE(),
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE ReturnID = @ID",
                new { ID = returnId, EmpID = employeeId, UserID = userId });

            try
            {
                await _notif.CreateForPermissionHoldersAsync(
                    notificationType: 3,
                    title: "مرتجع مشتريات بانتظار الاعتماد",
                    message: $"مرتجع المشتريات رقم {info.ReturnNumber} بانتظار الاعتماد",
                    priority: 1,
                    permissionModuleCode: "SCR_PRET",
                    requiredPermission: ModulePermissionRequirement.Approve,
                    relatedModule: "SCR_PRET",
                    relatedRecordId: returnId,
                    createdBy: userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create the pending-approval notification for a purchase return.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseReturns", returnId.ToString(),
                moduleName: "SCR_PRET", description: $"إرسال مرتجع للاعتماد: {info.ReturnNumber}");
        }

        public async Task ApproveAsync(int returnId, int userId, int? employeeId = null)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanApprove, "ليس لديك صلاحية اعتماد مرتجعات المشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string returnNumber = "";
            int? createdBy = null;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(
                    string ReturnNumber, int ReturnStatus, int? CreatedBy, int SupplierID,
                    int WarehouseID, int DetailCount, int? InvoiceID)>(
                    @"SELECT pr.ReturnNumber, pr.ReturnStatus, pr.CreatedBy, pr.SupplierID, pr.WarehouseID,
                        (SELECT COUNT(*) FROM dbo.PurchaseReturnDetails WHERE ReturnID = pr.ReturnID) AS DetailCount,
                        pr.InvoiceID
                      FROM dbo.PurchaseReturns pr WITH (UPDLOCK, HOLDLOCK) WHERE pr.ReturnID = @ID",
                    new { ID = returnId }, transaction);

                if (string.IsNullOrWhiteSpace(info.ReturnNumber)) throw new Exception("المرتجع غير موجود");
                if (info.ReturnStatus != 2) throw new Exception("لا يمكن اعتماد مرتجع ليس في انتظار الاعتماد");
                if (info.DetailCount == 0) throw new Exception("لا يمكن اعتماد مرتجع بدون أصناف");
                await EnsureSourceInvoiceLocalCurrencyAsync(connection, transaction, info.InvoiceID);

                returnNumber = info.ReturnNumber;
                createdBy = info.CreatedBy;

                // 1) إعادة حساب الإجماليات
                await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseReturnTotals @ReturnID",
                    new { ReturnID = returnId }, transaction);

                // 2) تحديث الحالة
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseReturns SET
                        ReturnStatus = 3, ApprovedBy = @EmpID, ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID, ModifiedDate = GETDATE()
                      WHERE ReturnID = @ID",
                    new { ID = returnId, EmpID = employeeId, UserID = userId }, transaction);

                // 3) تحديث المخزون
                var details = await connection.QueryAsync<(int ItemID, decimal Quantity, decimal UnitPrice)>(
                    "SELECT ItemID, Quantity, UnitPrice FROM dbo.PurchaseReturnDetails WHERE ReturnID = @ID",
                    new { ID = returnId }, transaction);

                foreach (var d in details)
                {
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_UpdateInventoryAfterReturn @ItemID, @ReturnQty, @UnitCost, @WarehouseID",
                        new { d.ItemID, ReturnQty = d.Quantity, UnitCost = d.UnitPrice, WarehouseID = info.WarehouseID },
                        transaction);
                }

                // 4) تحديث رصيد المورد
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_UpdateSupplierBalanceAfterInvoice @SupplierID",
                    new { SupplierID = info.SupplierID }, transaction);

                // 5) القيد المحاسبي: فشل الإجراء أو غياب قيد مرتبط/متوازن يلغي الاعتماد كله.
                var existingJournalCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1) FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE SourceDocID=@ReturnID AND SourceDocType IN ('PRET','PurchaseReturn')",
                    new { ReturnID = returnId }, transaction);
                if (existingJournalCount != 0)
                    throw new InvalidOperationException("يوجد قيد مرتبط مسبقاً بهذا المرتجع؛ أُوقف الاعتماد لمنع إنشاء قيد مكرر.");

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_CreatePurchaseReturnJournal @ReturnID, @UserID",
                    new { ReturnID = returnId, UserID = userId }, transaction);

                var linkedJournals = (await connection.QueryAsync<(
                    int JournalID, string? JournalNumber, int? JournalStatus, bool IsAutoGenerated,
                    string? SourceDocType, int? SourceDocID, decimal? HeaderDebit, decimal? HeaderCredit)>(
                    @"SELECT JournalID, JournalNumber, JournalStatus, ISNULL(IsAutoGenerated,0) AS IsAutoGenerated,
                             SourceDocType, SourceDocID, TotalDebit AS HeaderDebit, TotalCredit AS HeaderCredit
                      FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE SourceDocID=@ReturnID AND SourceDocType IN ('PRET','PurchaseReturn')",
                    new { ReturnID = returnId }, transaction)).ToList();
                if (linkedJournals.Count != 1)
                    throw new InvalidOperationException("لم ينشئ الإجراء قيداً واحداً فقط مرتبطاً بمرتجع المشتريات؛ أُلغيت العملية.");

                var linkedJournal = linkedJournals[0];
                if (string.IsNullOrWhiteSpace(linkedJournal.JournalNumber)
                    || !linkedJournal.IsAutoGenerated || linkedJournal.SourceDocID != returnId
                    || !string.Equals(linkedJournal.SourceDocType, "PRET", StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(linkedJournal.SourceDocType, "PurchaseReturn", StringComparison.OrdinalIgnoreCase)
                    || !linkedJournal.JournalStatus.HasValue || linkedJournal.JournalStatus.Value != 2)
                    throw new InvalidOperationException("القيد المرتبط بمرتجع المشتريات غير مرحّل أو لا يحمل ربطاً صالحاً؛ أُلغيت العملية.");

                var journalTotals = await connection.QueryFirstOrDefaultAsync<(
                    int DetailCount, decimal TotalDebit, decimal TotalCredit, int InvalidDetailCount)>(
                    @"SELECT ISNULL(SUM(CASE WHEN ISNULL(jd.DebitAmount,0)>0 OR ISNULL(jd.CreditAmount,0)>0 THEN 1 ELSE 0 END),0) AS DetailCount,
                             ISNULL(SUM(jd.DebitAmount),0) AS TotalDebit,
                             ISNULL(SUM(jd.CreditAmount),0) AS TotalCredit,
                             ISNULL(SUM(CASE WHEN jd.DebitAmount IS NULL OR jd.CreditAmount IS NULL
                                      OR jd.DebitAmount<0 OR jd.CreditAmount<0
                                      OR (jd.DebitAmount>0 AND jd.CreditAmount>0)
                                      OR ((jd.DebitAmount>0 OR jd.CreditAmount>0)
                                          AND (jd.AccountID IS NULL OR jd.AccountID<=0
                                               OR coa.AccountID IS NULL
                                               OR ISNULL(coa.IsActive,1)<>1
                                               OR (ISNULL(coa.IsDetailAccount,0)<>1
                                                   AND NOT (
                                                       coa.AccountCode=N'2101'
                                                       AND ISNULL(coa.IsSystemAccount,0)=1
                                                       AND ISNULL(coa.AccountType,0)=2
                                                       AND UPPER(LTRIM(RTRIM(ISNULL(coa.LinkedEntityType,N''))))=N'SUPPLIER'
                                                       AND UPPER(LTRIM(RTRIM(ISNULL(jd.EntityType,N''))))=N'SUPPLIER'
                                                       AND jd.EntityID=@SupplierID
                                                   ))
                                               OR (ISNULL(coa.CostCenterRequired,0)=1 AND ISNULL(jd.CostCenterID,0)<=0)
                                               OR jd.CostCenterID<0
                                               OR (jd.CostCenterID>0 AND (cc.CostCenterID IS NULL OR ISNULL(cc.IsActive,1)<>1)))
                                          )
                                      THEN 1 ELSE 0 END),0) AS InvalidDetailCount
                      FROM dbo.JournalEntryDetails jd WITH (UPDLOCK, HOLDLOCK)
                      LEFT JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK) ON coa.AccountID=jd.AccountID
                      LEFT JOIN dbo.CostCenters cc WITH (UPDLOCK,HOLDLOCK) ON cc.CostCenterID=jd.CostCenterID
                      WHERE jd.JournalID=@JournalID",
                    new { JournalID = linkedJournal.JournalID, SupplierID = info.SupplierID }, transaction);
                if (journalTotals.DetailCount < 2 || journalTotals.InvalidDetailCount > 0
                    || journalTotals.TotalDebit <= 0m || journalTotals.TotalDebit != journalTotals.TotalCredit)
                    throw new InvalidOperationException("تفاصيل قيد مرتجع المشتريات غير مكتملة أو تحتوي سطرًا محاسبيًا غير صالح أو غير متوازنة؛ أُلغيت العملية.");
                if (!linkedJournal.HeaderDebit.HasValue || !linkedJournal.HeaderCredit.HasValue
                    || linkedJournal.HeaderDebit.Value != journalTotals.TotalDebit
                    || linkedJournal.HeaderCredit.Value != journalTotals.TotalCredit)
                    throw new InvalidOperationException("إجماليات رأس قيد مرتجع المشتريات لا تطابق مجموع السطور؛ أُلغيت العملية.");

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after purchase-return approval failed for return {ReturnID}.",
                        returnId);
                }
                throw;
            }

            try
            {
                await _notif.MarkRelatedAsActionedAsync("SCR_PRET", returnId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after approving a purchase return.");
            }

            try
            {
                if (createdBy.HasValue)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم اعتماد المرتجع",
                        message: $"تم اعتماد مرتجع المشتريات رقم {returnNumber}",
                        priority: 2,
                        targetUserId: createdBy.Value,
                        permissionModuleCode: "SCR_PRET",
                        relatedModule: "SCR_PRET",
                        relatedRecordId: returnId,
                        createdBy: userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify the creator after approving a purchase return.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseReturns", returnId.ToString(),
                moduleName: "SCR_PRET", description: $"اعتماد مرتجع: {returnNumber}");
        }

        public async Task RejectAsync(int returnId, string reason, int userId, int? employeeId = null)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanApprove, "ليس لديك صلاحية رفض مرتجعات المشتريات.");
            if (string.IsNullOrWhiteSpace(reason)) throw new Exception("أدخل سبب الرفض");
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string ReturnNumber, int ReturnStatus, int? CreatedBy)>(
                "SELECT ReturnNumber, ReturnStatus, CreatedBy FROM dbo.PurchaseReturns WHERE ReturnID = @ID",
                new { ID = returnId });

            if (string.IsNullOrWhiteSpace(info.ReturnNumber)) throw new Exception("المرتجع غير موجود");
            if (info.ReturnStatus != 2) throw new Exception("لا يمكن رفض مرتجع ليس في انتظار الاعتماد");

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseReturns SET
                    ReturnStatus = 1, RejectedBy = @EmpID, RejectedDate = GETDATE(),
                    RejectionReason = @Reason, ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE ReturnID = @ID",
                new { ID = returnId, EmpID = employeeId, Reason = reason, UserID = userId });

            try
            {
                await _notif.MarkRelatedAsActionedAsync("SCR_PRET", returnId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after rejecting a purchase return.");
            }

            try
            {
                if (info.CreatedBy.HasValue)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم رفض المرتجع",
                        message: $"تم رفض مرتجع المشتريات رقم {info.ReturnNumber}. السبب: {reason}",
                        priority: 1,
                        targetUserId: info.CreatedBy.Value,
                        permissionModuleCode: "SCR_PRET",
                        relatedModule: "SCR_PRET",
                        relatedRecordId: returnId,
                        createdBy: userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify the creator after rejecting a purchase return.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseReturns", returnId.ToString(),
                moduleName: "SCR_PRET", description: $"رفض مرتجع: {info.ReturnNumber} — {reason}");
        }

        public async Task<(bool Success, string Message)> DeleteReturnAsync(int returnId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanDelete, "ليس لديك صلاحية حذف مرتجعات المشتريات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string ReturnNumber, int ReturnStatus)>(
                    "SELECT ReturnNumber, ReturnStatus FROM dbo.PurchaseReturns WHERE ReturnID = @ID",
                    new { ID = returnId }, transaction);
                if (string.IsNullOrWhiteSpace(info.ReturnNumber)) return (false, "المرتجع غير موجود");
                if (info.ReturnStatus != 1) return (false, "لا يمكن حذف مرتجع غير مسودة");

                await connection.ExecuteAsync("DELETE FROM dbo.PurchaseReturnDetails WHERE ReturnID = @ID",
                    new { ID = returnId }, transaction);
                await connection.ExecuteAsync("DELETE FROM dbo.PurchaseReturns WHERE ReturnID = @ID",
                    new { ID = returnId }, transaction);
                transaction.Commit();

                await _audit.WriteAuditLogAsync(userId, 3, "PurchaseReturns", returnId.ToString(),
                    moduleName: "SCR_PRET", description: $"حذف مرتجع: {info.ReturnNumber}");
                return (true, "تم حذف المرتجع بنجاح");
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after purchase-return deletion failed for return {ReturnID}.",
                        returnId);
                }
                return (false, ex.Message);
            }
        }

        public async Task<byte[]> ExportToExcelAsync(List<PurchaseReturnListDto> returns, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanExport, "ليس لديك صلاحية تصدير مرتجعات المشتريات.");
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("مرتجعات المشتريات");
            ws.RightToLeft = true; ws.Style.Font.FontName = "Cairo";
            var headers = new[] { "#", "رقم المرتجع", "التاريخ", "المورد", "الفاتورة", "النوع", "الإجمالي", "الحالة" };
            for (int i = 0; i < headers.Length; i++)
            { var c = ws.Cell(1, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
              c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b"); c.Style.Font.FontColor = XLColor.White; }
            int row = 2, n = 0;
            foreach (var r in returns)
            { n++; ws.Cell(row, 1).Value = n; ws.Cell(row, 2).Value = r.ReturnNumber ?? "";
              ws.Cell(row, 3).Value = r.ReturnDate.ToString("dd/MM/yyyy"); ws.Cell(row, 4).Value = r.SupplierNameAr ?? "";
              ws.Cell(row, 5).Value = r.LinkedInvoiceNumber ?? "—"; ws.Cell(row, 6).Value = GetReturnTypeName(r.ReturnType);
              ws.Cell(row, 7).Value = r.TotalAmount; ws.Cell(row, 8).Value = GetStatusName(r.ReturnStatus); row++; }
            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream(); wb.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, "PurchaseReturns", moduleName: "SCR_PRET",
                description: $"تصدير {returns.Count} مرتجع");
            return stream.ToArray();
        }

        public async Task<string> GenerateReportHtmlAsync(int returnId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanPrint, "ليس لديك صلاحية طباعة مرتجعات المشتريات.");
            var h = await GetReturnByIdAsync(returnId);
            if (h == null) return "<h3>المرتجع غير موجود</h3>";
            var d = await GetReturnDetailsAsync(returnId);
            var rows = ""; int n = 0;
            foreach (var line in d)
            { n++; rows += $"<tr><td style='text-align:center'>{n}</td><td>{line.ItemCode}</td><td>{line.ItemNameAr}</td><td>{line.UnitName}</td><td style='text-align:center'>{line.Quantity:#,##0.##}</td><td style='text-align:left'>{line.UnitPrice:#,##0.00}</td><td style='text-align:center'>{line.TaxRate:#,##0.##}%</td><td style='text-align:left'>{line.LineTotalWithTax:#,##0.00}</td><td>{GetConditionName(line.ItemCondition)}</td></tr>"; }

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'><title>مرتجع — {h.ReturnNumber}</title>
<style>*{{margin:0;padding:0;box-sizing:border-box}}body{{font-family:'Cairo',sans-serif;padding:30px;color:#0f172a;font-size:13px}}.header{{text-align:center;border-bottom:3px solid #1e293b;padding-bottom:20px;margin-bottom:24px}}.company{{font-size:20px;font-weight:800}}.doc-title{{font-size:15px;color:#475569}}.doc-num{{display:inline-block;background:#f1f5f9;padding:4px 16px;border-radius:8px;font-weight:700;color:#3b82f6;margin-top:8px}}.info-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:20px}}.info-item{{background:#f8fafc;padding:10px;border-radius:8px;border:1px solid #e2e8f0}}.info-label{{font-size:10px;color:#64748b;font-weight:600}}.info-value{{font-size:13px;font-weight:700;margin-top:2px}}table{{width:100%;border-collapse:collapse;margin-bottom:16px}}th{{background:#1e293b;color:white;padding:10px;font-size:12px;text-align:right}}td{{padding:9px 10px;border-bottom:1px solid #e2e8f0;font-size:12px}}tr:nth-child(even){{background:#f8fafc}}.total-row{{font-weight:800;background:#f1f5f9!important}}.footer{{text-align:center;margin-top:30px;padding-top:12px;border-top:1px solid #e2e8f0;font-size:10px;color:#94a3b8}}</style></head><body>
<div class='header'><div class='company'>مصنع واي كي كوتينج  </div><div class='doc-title'>مرتجع مشتريات</div><div class='doc-num'>{h.ReturnNumber}</div></div>
<div class='info-grid'>
<div class='info-item'><div class='info-label'>التاريخ</div><div class='info-value'>{h.ReturnDate:dd/MM/yyyy}</div></div>
<div class='info-item'><div class='info-label'>المورد</div><div class='info-value'>{h.SupplierNameAr}</div></div>
<div class='info-item'><div class='info-label'>الفاتورة</div><div class='info-value'>{h.LinkedInvoiceNumber ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>النوع</div><div class='info-value'>{GetReturnTypeName(h.ReturnType)}</div></div>
<div class='info-item'><div class='info-label'>السبب</div><div class='info-value'>{h.ReturnReason ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>المخزن</div><div class='info-value'>{h.WarehouseNameAr ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(h.ReturnStatus)}</div></div>
<div class='info-item'><div class='info-label'>الإجمالي</div><div class='info-value'>{h.TotalAmount:#,##0.00} ج.م</div></div>
</div>
<table><thead><tr><th>#</th><th>الكود</th><th>الصنف</th><th>الوحدة</th><th>الكمية</th><th>السعر</th><th>ضريبة</th><th>الإجمالي</th><th>الحالة</th></tr></thead>
<tbody>{rows}<tr class='total-row'><td colspan='7' style='text-align:left'>الإجمالي</td><td style='text-align:left'>{h.TotalAmount:#,##0.00} ج.م</td><td></td></tr></tbody></table>
{(string.IsNullOrWhiteSpace(h.Notes) ? "" : $"<div style='margin-bottom:16px'><strong>ملاحظات:</strong> {h.Notes}</div>")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP v2.0</div></body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "PurchaseReturns", returnId.ToString(),
                moduleName: "SCR_PRET", description: $"طباعة مرتجع: {h.ReturnNumber}");
            return html;
        }

        public static string GetStatusName(int s) => s switch { 1 => "مسودة", 2 => "مقدم للاعتماد", 3 => "معتمد", 4 => "مرفوض", 5 => "ملغي", _ => "غير محدد" };
        public static string GetStatusColor(int s) => s switch { 1 => "#64748b", 2 => "#f59e0b", 3 => "#10b981", 4 => "#ef4444", 5 => "#dc2626", _ => "#6b7280" };
        public static string GetReturnTypeName(int t) => t switch { 1 => "معيب", 2 => "غير مطابق", 3 => "زيادة", _ => "معيب" };
        public static string GetReturnTypeColor(int t) => t switch { 1 => "#ef4444", 2 => "#f59e0b", 3 => "#6366f1", _ => "#ef4444" };
        public static string GetConditionName(int c) => c switch { 1 => "سليم", 2 => "تالف", 3 => "منتهي الصلاحية", _ => "سليم" };
        public static string GetSourceName(int s) => s switch { 1 => "من فاتورة", 2 => "مرتجع مباشر", _ => "من فاتورة" };
    }
}