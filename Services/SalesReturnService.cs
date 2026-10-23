using System.Data;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    /// <summary>
    /// SCR_SRET workflow. A return is always linked to an approved sales invoice;
    /// pricing/tax are re-derived from the source invoice and posting is atomic.
    /// </summary>
    public partial class SalesReturnService : BaseDbService
    {
        private const string ModuleCode = "SCR_SRET";
        private const string ReturnSequenceCode = "SRET";
        private const string JournalSequenceCode = "JV";
        private const string RejectionNoteMarker = "\n[[YK-SRET-REJECTION]]\n";

        private readonly AuditService _audit;
        private readonly PermissionService _permissions;
        private readonly UserSessionService _session;

        public SalesReturnService(
            IConfiguration configuration,
            AuditService audit,
            PermissionService permissions,
            UserSessionService session) : base(configuration)
        {
            _audit = audit;
            _permissions = permissions;
            _session = session;
        }

        // ---------------------------------------------------------------------
        // Read models
        // ---------------------------------------------------------------------

        public async Task<List<SalesReturnListDto>> GetReturnsListAsync(int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanView,
                "لا تملك صلاحية عرض مرتجعات المبيعات.");

            using var connection = CreateConnection();
            const string sql = @"SELECT
                    sr.ReturnID, sr.ReturnNumber, sr.ReturnDate, sr.ReturnStatus, sr.ReturnType,
                    sr.ReturnReason, sr.CustomerID, c.CustomerNameAr,
                    sr.InvoiceID, si.InvoiceNumber, sr.WarehouseID, w.WarehouseNameAr,
                    ISNULL(sr.SubTotal,0) AS SubTotal, ISNULL(sr.TaxAmount,0) AS TaxAmount,
                    ISNULL(sr.TotalAmount,0) AS TotalAmount,
                    (SELECT COUNT(*) FROM dbo.SalesReturnDetails rd WHERE rd.ReturnID=sr.ReturnID) AS ItemCount,
                    sr.CreatedBy, sr.CreatedDate
                FROM dbo.SalesReturns sr
                INNER JOIN dbo.Customers c ON c.CustomerID=sr.CustomerID
                LEFT JOIN dbo.SalesInvoices si ON si.InvoiceID=sr.InvoiceID
                LEFT JOIN dbo.Warehouses w ON w.WarehouseID=sr.WarehouseID
                ORDER BY sr.ReturnDate DESC, sr.ReturnID DESC;";

            return (await connection.QueryAsync<SalesReturnListDto>(sql)).ToList();
        }

        public async Task<SalesReturnHeaderDto?> GetReturnByIdAsync(int returnId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanView,
                "لا تملك صلاحية عرض مرتجعات المبيعات.");
            return await GetReturnHeaderCoreAsync(returnId);
        }

        public async Task<List<SalesReturnDetailDto>> GetReturnDetailsAsync(int returnId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanView,
                "لا تملك صلاحية عرض تفاصيل مرتجعات المبيعات.");
            using var connection = CreateConnection();
            const string sql = @"SELECT
                    rd.ReturnDetailID, rd.ReturnID, rd.LineNumber, rd.InvoiceDetailID,
                    rd.ItemID, i.ItemCode, i.ItemNameAr, rd.UnitID, u.UnitNameAr AS UnitName,
                    rd.Quantity, ISNULL(id.Quantity,0) AS InvoiceQuantity,
                    ISNULL(prev.PreviouslyReturnedQuantity,0) AS PreviouslyReturnedQuantity,
                    CASE WHEN ISNULL(id.Quantity,0)-ISNULL(prev.PreviouslyReturnedQuantity,0)<0 THEN 0
                         ELSE ISNULL(id.Quantity,0)-ISNULL(prev.PreviouslyReturnedQuantity,0) END AS RemainingReturnQuantity,
                    rd.UnitPrice, rd.LineTotal, ISNULL(rd.TaxRate,0) AS TaxRate,
                    ISNULL(rd.TaxAmount,0) AS TaxAmount,
                    ISNULL(rd.LineTotalWithTax,ISNULL(rd.LineTotal,0)+ISNULL(rd.TaxAmount,0)) AS LineTotalWithTax,
                    rd.BatchNumber, ISNULL(rd.ItemCondition,1) AS ItemCondition,
                    rd.ReturnReason, rd.Notes
                FROM dbo.SalesReturnDetails rd
                INNER JOIN dbo.SalesReturns currentReturn ON currentReturn.ReturnID=rd.ReturnID
                INNER JOIN dbo.Items i ON i.ItemID=rd.ItemID
                INNER JOIN dbo.Units u ON u.UnitID=rd.UnitID
                LEFT JOIN dbo.SalesInvoiceDetails id ON id.InvoiceDetailID=rd.InvoiceDetailID
                OUTER APPLY
                (
                    SELECT SUM(otherDetail.Quantity) AS PreviouslyReturnedQuantity
                    FROM dbo.SalesReturnDetails otherDetail
                    INNER JOIN dbo.SalesReturns otherReturn ON otherReturn.ReturnID=otherDetail.ReturnID
                    WHERE otherDetail.InvoiceDetailID=rd.InvoiceDetailID
                      AND otherReturn.ReturnStatus IN (2,3)
                      AND otherReturn.ReturnID<>currentReturn.ReturnID
                ) prev
                WHERE rd.ReturnID=@ReturnID
                ORDER BY rd.LineNumber, rd.ReturnDetailID;";

            return (await connection.QueryAsync<SalesReturnDetailDto>(sql, new { ReturnID = returnId })).ToList();
        }

        /// <summary>Approved invoices only; delivery-based invoices are excluded because their original stock source is not linked here.</summary>
        public async Task<List<SearchableItem>> GetEligibleInvoicesAsync(int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanAdd || p.CanView,
                "لا تملك صلاحية اختيار فاتورة لمرتجع المبيعات.");
            using var connection = CreateConnection();
            const string sql = @"SELECT TOP (500)
                    si.InvoiceID AS Id,
                    CONCAT(si.InvoiceNumber,N' — ',c.CustomerNameAr) AS Name,
                    si.InvoiceNumber AS Code,
                    c.CustomerNameAr AS Extra
                FROM dbo.SalesInvoices si
                INNER JOIN dbo.Customers c ON c.CustomerID=si.CustomerID
                LEFT JOIN dbo.Currencies cur ON cur.CurrencyID=si.CurrencyID
                WHERE si.InvoiceStatus=3
                  AND ISNULL(si.InvoiceSource,3) IN (2,3)
                  AND si.DeliveryID IS NULL
                  AND ISNULL(si.ExchangeRate,1)=1
                  AND ISNULL(cur.IsDefault,1)=1
                  AND ISNULL(si.ShippingCost,0)=0
                  AND ISNULL(si.OtherCosts,0)=0
                  AND EXISTS
                  (
                    SELECT 1 FROM dbo.SalesInvoiceDetails d
                    WHERE d.InvoiceID=si.InvoiceID
                      AND d.Quantity > ISNULL
                      (
                        (SELECT SUM(rd.Quantity)
                         FROM dbo.SalesReturnDetails rd
                         INNER JOIN dbo.SalesReturns priorReturn ON priorReturn.ReturnID=rd.ReturnID
                         WHERE rd.InvoiceDetailID=d.InvoiceDetailID
                           AND priorReturn.ReturnStatus IN (2,3)), 0
                      )
                  )
                  AND NOT EXISTS
                  (
                    SELECT 1 FROM dbo.SalesInvoiceDetails d
                    WHERE d.InvoiceID=si.InvoiceID
                      AND NOT EXISTS
                      (
                        SELECT 1 FROM dbo.InventoryTransactions it
                        WHERE it.TransactionType=4
                          AND it.SourceDocType IN (N'SINV',N'SI')
                          AND it.SourceDocLineID=d.InvoiceDetailID
                          AND (it.SourceDocID=si.InvoiceID OR it.SourceDocNumber=si.InvoiceNumber)
                      )
                  )
                ORDER BY si.InvoiceDate DESC, si.InvoiceID DESC;";
            return (await connection.QueryAsync<SearchableItem>(sql)).ToList();
        }

        public async Task<SalesReturnInvoiceContextDto> GetInvoiceContextAsync(int invoiceId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanView || p.CanAdd,
                "لا تملك صلاحية الاطلاع على الفاتورة المرتبطة.");
            using var connection = CreateConnection();
            var invoice = await LoadReturnableInvoiceAsync(connection, null, invoiceId, 0, lockSource: false);
            return MapInvoiceContext(invoice);
        }

        public async Task<List<SalesReturnInvoiceLineDto>> GetInvoiceReturnLinesAsync(
            int invoiceId, int userId, int currentReturnId = 0)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanView || p.CanAdd,
                "لا تملك صلاحية الاطلاع على أصناف الفاتورة المرتبطة.");
            using var connection = CreateConnection();
            var invoice = await LoadReturnableInvoiceAsync(connection, null, invoiceId, currentReturnId, lockSource: false);
            return invoice.Lines.Select(line => new SalesReturnInvoiceLineDto
            {
                InvoiceDetailID = line.InvoiceDetailID,
                InvoiceID = line.InvoiceID,
                LineNumber = line.LineNumber,
                ItemID = line.ItemID,
                ItemCode = line.ItemCode,
                ItemNameAr = line.ItemNameAr,
                UnitID = line.UnitID,
                UnitName = line.UnitName,
                InvoiceQuantity = line.Quantity,
                PreviouslyReturnedQuantity = line.PreviouslyReturnedQuantity,
                RemainingReturnQuantity = Math.Max(0m, line.Quantity - line.PreviouslyReturnedQuantity),
                InvoiceUnitPrice = line.UnitPrice,
                EffectiveReturnUnitPrice = CalculateEffectiveUnitPrice(line, invoice.HeaderDiscountFactor),
                TaxRate = line.IsTaxable ? line.TaxRate : 0m,
                OriginalTaxAmount = line.IsTaxable
                    ? RoundMoney(line.TaxAmount * invoice.HeaderTaxFactor, 2)
                    : 0m,
                BatchNumber = line.BatchNumber,
                ExpiryDate = line.ExpiryDate,
                SourceWarehouseID = line.SourceWarehouseID,
                ReturnQuantity = 0,
                ItemCondition = 1
            }).ToList();
        }

        // ---------------------------------------------------------------------
        // Draft lifecycle
        // ---------------------------------------------------------------------

        public async Task<int> CreateDraftAsync(int invoiceId, SalesReturnSaveDto request, int userId)
        {
            ArgumentNullException.ThrowIfNull(request);
            await RequireReturnPermissionAsync(userId, p => p.CanAdd,
                "لا تملك صلاحية إنشاء مرتجع مبيعات.");
            if (invoiceId <= 0) throw new ArgumentException("اختر فاتورة مبيعات معتمدة.", nameof(invoiceId));
            ValidateReturnType(request.ReturnType);
            ValidateHeaderReason(request.ReturnReason);
            if (request.Details == null || request.Details.Count == 0)
                throw new InvalidOperationException("أضف كمية مرتجعة واحدة على الأقل قبل إنشاء المسودة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            int returnId;
            string returnNumber;
            try
            {
                var invoice = await LoadReturnableInvoiceAsync(connection, transaction, invoiceId, 0, lockSource: true);
                ValidateReturnDate(request.ReturnDate, invoice.InvoiceDate);
                await EnsureActiveWarehouseAsync(connection, transaction, request.WarehouseID);
                var pricedLines = await ValidateAndPriceRequestLinesAsync(
                    connection, transaction, invoice, request.Details, excludeReturnId: 0);

                returnNumber = await GetNextDocumentNumberAsync(connection, transaction, ReturnSequenceCode);
                returnId = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.SalesReturns
                      (ReturnNumber, ReturnDate, CustomerID, InvoiceID, SalesRepID, WarehouseID,
                       ReturnReason, ReturnType, ReturnStatus, SubTotal, TaxAmount, TotalAmount,
                       Notes, CreatedBy, CreatedDate)
                      VALUES
                      (@ReturnNumber, @ReturnDate, @CustomerID, @InvoiceID, @SalesRepID, @WarehouseID,
                       @ReturnReason, @ReturnType, 1, 0, 0, 0,
                       @Notes, @UserID, GETDATE());
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        ReturnNumber = returnNumber,
                        ReturnDate = request.ReturnDate.Date,
                        invoice.CustomerID,
                        InvoiceID = invoice.InvoiceID,
                        invoice.SalesRepID,
                        request.WarehouseID,
                        request.ReturnReason,
                        request.ReturnType,
                        request.Notes,
                        UserID = userId
                    }, transaction);

                var lineNumber = 0;
                foreach (var line in pricedLines)
                {
                    lineNumber++;
                    await InsertReturnDetailAsync(connection, transaction, returnId, lineNumber,
                        line.Source, line.Request, line.Pricing);
                }

                await RecalculateReturnTotalsAsync(connection, transaction, returnId);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 1, "SalesReturns", returnId.ToString(),
                moduleName: ModuleCode, description: $"إنشاء مسودة مرتجع مبيعات {returnNumber} من فاتورة معتمدة");
            return returnId;
        }

        public async Task SaveDraftAsync(SalesReturnSaveDto request, int userId)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ReturnID <= 0) throw new ArgumentException("رقم المرتجع غير صالح.", nameof(request));
            await RequireReturnPermissionAsync(userId, p => p.CanEdit || p.CanAdd,
                "لا تملك صلاحية تعديل مسودات مرتجعات المبيعات.");
            ValidateReturnType(request.ReturnType);
            ValidateHeaderReason(request.ReturnReason);
            if (request.Details == null || request.Details.Count == 0)
                throw new InvalidOperationException("يجب أن تحتوي المسودة على سطر مرتجع واحد على الأقل.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            string returnNumber;
            int previousStatus;
            try
            {
                var header = await LoadReturnStateAsync(connection, transaction, request.ReturnID, lockRow: true);
                if (header == null) throw new KeyNotFoundException("مرتجع المبيعات غير موجود.");
                var permission = await GetCurrentReturnPermissionsAsync(userId);
                await EnsureCanEditDraftAsync(connection, transaction, header, userId, permission);
                if (header.ReturnStatus is not (1 or 4))
                    throw new InvalidOperationException("لا يمكن تعديل مرتجع تم إرساله للاعتماد أو اعتماده.");

                var invoice = await LoadReturnableInvoiceAsync(connection, transaction, header.InvoiceID, request.ReturnID, lockSource: true);
                EnsureReturnMatchesInvoice(header, invoice);
                ValidateReturnDate(request.ReturnDate, invoice.InvoiceDate);
                await EnsureActiveWarehouseAsync(connection, transaction, request.WarehouseID);
                var pricedLines = await ValidateAndPriceRequestLinesAsync(
                    connection, transaction, invoice, request.Details ?? new(), excludeReturnId: request.ReturnID);

                var oldDetails = (await connection.QueryAsync<ExistingReturnDetail>(
                    @"SELECT ReturnDetailID, InvoiceDetailID, LineNumber
                      FROM dbo.SalesReturnDetails WITH (UPDLOCK,HOLDLOCK)
                      WHERE ReturnID=@ReturnID ORDER BY LineNumber, ReturnDetailID;",
                    new { request.ReturnID }, transaction)).ToDictionary(x => x.ReturnDetailID);
                var validInvoiceDetailIds = invoice.Lines.Select(x => x.InvoiceDetailID).ToHashSet();
                if (oldDetails.Values.Any(x => x.InvoiceDetailID <= 0 || !validInvoiceDetailIds.Contains(x.InvoiceDetailID)))
                    throw new InvalidOperationException("يحتوي هذا المستند على سطر لا يرتبط بفاتورته الأصلية؛ أوقفنا التعديل حتى لا نحذف أو نعيد ربط بياناته ضمنياً.");
                if (oldDetails.Values.GroupBy(x => x.InvoiceDetailID).Any(x => x.Count() > 1))
                    throw new InvalidOperationException("توجد سطور مكررة مرتبطة بالسطر نفسه من الفاتورة؛ يلزم تسويتها قبل التعديل.");

                var seenIds = new HashSet<int>();
                foreach (var line in pricedLines)
                {
                    var detailId = line.Request.ReturnDetailID;
                    if (detailId > 0)
                    {
                        if (!oldDetails.TryGetValue(detailId, out var existing))
                            throw new InvalidOperationException("أحد سطور التعديل لا ينتمي إلى هذا المرتجع.");
                        if (existing.InvoiceDetailID != line.Request.InvoiceDetailID)
                            throw new InvalidOperationException("لا يمكن تغيير سطر الفاتورة الأصلي المرتبط بسطر المرتجع.");
                        seenIds.Add(detailId);
                    }
                }

                foreach (var old in oldDetails.Values.Where(x => !seenIds.Contains(x.ReturnDetailID)))
                {
                    await connection.ExecuteAsync(
                        "DELETE FROM dbo.SalesReturnDetails WHERE ReturnDetailID=@ID AND ReturnID=@ReturnID",
                        new { ID = old.ReturnDetailID, request.ReturnID }, transaction);
                }

                var nextLineNumber = oldDetails.Values.Select(x => x.LineNumber).DefaultIfEmpty(0).Max();
                foreach (var line in pricedLines)
                {
                    if (line.Request.ReturnDetailID > 0)
                    {
                        var existing = oldDetails[line.Request.ReturnDetailID];
                        await UpdateReturnDetailAsync(connection, transaction,
                            line.Request.ReturnDetailID, existing.LineNumber, line.Source, line.Request, line.Pricing);
                    }
                    else
                    {
                        nextLineNumber++;
                        await InsertReturnDetailAsync(connection, transaction, request.ReturnID, nextLineNumber,
                            line.Source, line.Request, line.Pricing);
                    }
                }

                var preservedRejection = SplitRejectionNote(header.RawNotes).RejectionReason;
                var storedNotes = CombineNotesAndRejection(request.Notes, preservedRejection);
                const int statusToSave = 1; // Saving a rejected return deliberately reopens it as a draft.
                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesReturns
                      SET ReturnDate=@ReturnDate, ReturnType=@ReturnType, WarehouseID=@WarehouseID,
                          ReturnReason=@ReturnReason, Notes=@Notes, ReturnStatus=@ReturnStatus,
                          ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE ReturnID=@ReturnID AND ReturnStatus IN (1,4)",
                    new
                    {
                        ReturnDate = request.ReturnDate.Date,
                        request.ReturnType,
                        request.WarehouseID,
                        request.ReturnReason,
                        Notes = storedNotes,
                        ReturnStatus = statusToSave,
                        UserID = userId,
                        request.ReturnID
                    }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة المرتجع؛ أعد تحميله قبل الحفظ.");

                await RecalculateReturnTotalsAsync(connection, transaction, request.ReturnID);
                returnNumber = header.ReturnNumber;
                previousStatus = header.ReturnStatus;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesReturns", request.ReturnID.ToString(),
                moduleName: ModuleCode,
                description: previousStatus == 4
                    ? $"تعديل وإعادة فتح مرتجع مبيعات مرفوض {returnNumber} كمسودة"
                    : $"تعديل مسودة مرتجع مبيعات {returnNumber}");
        }

        public async Task DeleteDraftAsync(int returnId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanDelete,
                "لا تملك صلاحية حذف مرتجعات المبيعات.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            string number;
            try
            {
                var header = await LoadReturnStateAsync(connection, transaction, returnId, lockRow: true);
                if (header == null) throw new KeyNotFoundException("مرتجع المبيعات غير موجود.");
                if (header.ReturnStatus is not (1 or 4))
                    throw new InvalidOperationException("يمكن حذف المسودة أو المرتجع المرفوض فقط؛ لا يمكن حذف مستند دخل سير الاعتماد.");

                await connection.ExecuteAsync("DELETE FROM dbo.SalesReturnDetails WHERE ReturnID=@ID", new { ID = returnId }, transaction);
                var affected = await connection.ExecuteAsync(
                    "DELETE FROM dbo.SalesReturns WHERE ReturnID=@ID AND ReturnStatus IN (1,4)",
                    new { ID = returnId }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة المرتجع؛ لم يتم حذفه.");
                number = header.ReturnNumber;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "SalesReturns", returnId.ToString(),
                moduleName: ModuleCode, description: $"حذف مسودة مرتجع مبيعات {number}");
        }

        // ---------------------------------------------------------------------
        // Workflow: submit, approve, reject
        // ---------------------------------------------------------------------

        public async Task SubmitForApprovalAsync(int returnId, int userId)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanEdit || p.CanAdd,
                "لا تملك صلاحية إرسال المرتجع للاعتماد.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            string number;
            try
            {
                var header = await LoadReturnStateAsync(connection, transaction, returnId, lockRow: true);
                if (header == null) throw new KeyNotFoundException("مرتجع المبيعات غير موجود.");
                var permission = await GetCurrentReturnPermissionsAsync(userId);
                await EnsureCanEditDraftAsync(connection, transaction, header, userId, permission);
                if (header.ReturnStatus != 1)
                    throw new InvalidOperationException("يجب حفظ المرتجع كمسودة جديدة قبل إرساله للاعتماد.");

                var invoice = await LoadReturnableInvoiceAsync(connection, transaction, header.InvoiceID, returnId, lockSource: true);
                EnsureReturnMatchesInvoice(header, invoice);
                var details = await LoadReturnLinesForWorkflowAsync(connection, transaction, returnId);
                if (details.Count == 0) throw new InvalidOperationException("لا يمكن إرسال مرتجع بدون أصناف.");
                ValidateReturnDate(header.ReturnDate, invoice.InvoiceDate);
                await EnsureActiveWarehouseAsync(connection, transaction, header.WarehouseID);
                await ValidateAndRepriceExistingLinesAsync(connection, transaction, invoice, details, returnId);
                await RecalculateReturnTotalsAsync(connection, transaction, returnId);

                var approverRoles = (await connection.QueryAsync<int>(
                    @"SELECT DISTINCT ur.RoleID
                      FROM dbo.UserRoles ur
                      INNER JOIN dbo.RolePermissions rp ON rp.RoleID=ur.RoleID
                      INNER JOIN dbo.SystemModules sm ON sm.ModuleID=rp.ModuleID
                      WHERE sm.ModuleCode=@ModuleCode AND rp.CanApprove=1;",
                    new { ModuleCode }, transaction)).Distinct().ToList();
                if (approverRoles.Count == 0)
                    throw new InvalidOperationException("لا يوجد دور مستخدم لديه صلاحية اعتماد SCR_SRET؛ أبقينا المرتجع مسودة بدلاً من إرساله دون إشعار.");

                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesReturns
                      SET ReturnStatus=2, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE ReturnID=@ReturnID AND ReturnStatus=1",
                    new { ReturnID = returnId, UserID = userId }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة المرتجع؛ أعد تحميله قبل الإرسال.");
                foreach (var roleId in approverRoles)
                {
                    await InsertReturnWorkflowNotificationAsync(connection, transaction,
                        "مرتجع مبيعات بانتظار الاعتماد",
                        $"مرتجع المبيعات رقم {header.ReturnNumber} بانتظار الاعتماد",
                        priority: 1, targetUserId: null, targetRoleId: roleId,
                        returnId: returnId, userId: userId);
                }
                number = header.ReturnNumber;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }


            await _audit.WriteAuditLogAsync(userId, 2, "SalesReturns", returnId.ToString(),
                moduleName: ModuleCode, description: $"إرسال مرتجع مبيعات للاعتماد: {number}");
        }

        public async Task ApproveAsync(int returnId, int userId, int? employeeId = null)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanApprove,
                "لا تملك صلاحية اعتماد مرتجعات المبيعات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            string number;
            int? createdBy;
            int customerId;
            decimal totalAmount;
            decimal subtotal;
            decimal taxAmount;
            decimal restockedCost;
            try
            {
                var header = await LoadReturnStateAsync(connection, transaction, returnId, lockRow: true);
                if (header == null) throw new KeyNotFoundException("مرتجع المبيعات غير موجود.");
                if (header.ReturnStatus != 2)
                    throw new InvalidOperationException("لا يمكن اعتماد مرتجع ليس في انتظار الاعتماد.");

                var invoice = await LoadReturnableInvoiceAsync(connection, transaction, header.InvoiceID, returnId, lockSource: true);
                EnsureReturnMatchesInvoice(header, invoice);
                var details = await LoadReturnLinesForWorkflowAsync(connection, transaction, returnId);
                if (details.Count == 0) throw new InvalidOperationException("لا يمكن اعتماد مرتجع بدون أصناف.");
                ValidateReturnDate(header.ReturnDate, invoice.InvoiceDate);
                await EnsureActiveWarehouseAsync(connection, transaction, header.WarehouseID);
                await ValidateAndRepriceExistingLinesAsync(connection, transaction, invoice, details, returnId);
                await RecalculateReturnTotalsAsync(connection, transaction, returnId);
                var totals = await GetReturnTotalsAsync(connection, transaction, returnId);

                var duplicateMovements = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.InventoryTransactions WITH (UPDLOCK,HOLDLOCK)
                      WHERE SourceDocType=N'SRET' AND SourceDocID=@ReturnID AND TransactionType=6;",
                    new { ReturnID = returnId }, transaction);
                if (duplicateMovements > 0)
                    throw new InvalidOperationException("توجد حركات مخزون سابقة لهذا المرتجع؛ أوقفنا الاعتماد لمنع التكرار.");
                var duplicateJournal = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                      WHERE SourceDocType=N'SRET' AND SourceDocID=@ReturnID;",
                    new { ReturnID = returnId }, transaction);
                if (duplicateJournal > 0)
                    throw new InvalidOperationException("يوجد قيد محاسبي سابق لهذا المرتجع؛ راجعه قبل إعادة الاعتماد.");

                number = header.ReturnNumber;
                customerId = header.CustomerID;
                createdBy = header.CreatedBy;

                // Re-stock only saleable goods; damaged/expired goods remain out of available inventory.
                restockedCost = await PostReturnInventoryAsync(
                    connection, transaction, header, details, invoice, userId);

                if (totals.TotalAmount > 0)
                {
                    var updated = await connection.ExecuteAsync(
                        @"UPDATE dbo.Customers
                          SET CurrentBalance=ISNULL(CurrentBalance,0)-@CreditAmount,
                              ModifiedBy=@UserID, ModifiedDate=GETDATE()
                          WHERE CustomerID=@CustomerID;",
                        new { CustomerID = customerId, CreditAmount = totals.TotalAmount, UserID = userId }, transaction);
                    if (updated != 1) throw new InvalidOperationException("تعذر تخفيض رصيد العميل؛ تم إلغاء الاعتماد بالكامل.");
                }

                await CreateSalesReturnJournalAsync(
                    connection, transaction, header, totals.SubTotal, totals.TaxAmount,
                    totals.TotalAmount, restockedCost, employeeId, userId);

                var approved = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesReturns
                      SET ReturnStatus=3, ApprovedBy=@EmployeeID, ApprovedDate=GETDATE(),
                          ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE ReturnID=@ReturnID AND ReturnStatus=2;",
                    new { ReturnID = returnId, EmployeeID = employeeId, UserID = userId }, transaction);
                if (approved != 1) throw new InvalidOperationException("تغيرت حالة المرتجع؛ أعد تحميله قبل الاعتماد.");
                if (createdBy.HasValue)
                {
                    await InsertReturnWorkflowNotificationAsync(connection, transaction,
                        "تم اعتماد مرتجع المبيعات",
                        $"تم اعتماد مرتجع المبيعات رقم {number}. رصيد دائن للعميل: {totals.TotalAmount:#,##0.00} ج.م",
                        priority: 2, targetUserId: createdBy.Value, targetRoleId: null,
                        returnId: returnId, userId: userId);
                }
                await MarkReturnNotificationsActionedAsync(connection, transaction, returnId);

                subtotal = totals.SubTotal;
                taxAmount = totals.TaxAmount;
                totalAmount = totals.TotalAmount;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }


            await _audit.WriteAuditLogAsync(userId, 2, "SalesReturns", returnId.ToString(),
                moduleName: ModuleCode,
                description: $"اعتماد مرتجع مبيعات {number}; صافي {subtotal:#,##0.00}; ضريبة {taxAmount:#,##0.00}; إجمالي {totalAmount:#,##0.00}; تكلفة مخزون معادة {restockedCost:#,##0.00}");
        }

        public async Task RejectAsync(int returnId, string rejectionReason, int userId, int? employeeId = null)
        {
            await RequireReturnPermissionAsync(userId, p => p.CanApprove,
                "لا تملك صلاحية رفض مرتجعات المبيعات.");
            if (string.IsNullOrWhiteSpace(rejectionReason))
                throw new ArgumentException("أدخل سبب رفض واضحاً.", nameof(rejectionReason));
            if (rejectionReason.Trim().Length > 1000)
                throw new ArgumentOutOfRangeException(nameof(rejectionReason), "سبب الرفض لا يتجاوز 1000 حرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            string number;
            int? createdBy;
            try
            {
                var header = await LoadReturnStateAsync(connection, transaction, returnId, lockRow: true);
                if (header == null) throw new KeyNotFoundException("مرتجع المبيعات غير موجود.");
                if (header.ReturnStatus != 2)
                    throw new InvalidOperationException("لا يمكن رفض مرتجع ليس في انتظار الاعتماد.");

                var storedNotes = CombineNotesAndRejection(header.RawNotes, rejectionReason);
                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesReturns
                      SET ReturnStatus=4, Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE ReturnID=@ReturnID AND ReturnStatus=2;",
                    new { ReturnID = returnId, Notes = storedNotes, UserID = userId }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة المرتجع؛ لم يتم الرفض.");
                number = header.ReturnNumber;
                createdBy = header.CreatedBy;
                if (createdBy.HasValue)
                {
                    await InsertReturnWorkflowNotificationAsync(connection, transaction,
                        "تم رفض مرتجع المبيعات",
                        $"تم رفض مرتجع المبيعات رقم {number}. السبب: {rejectionReason.Trim()}",
                        priority: 1, targetUserId: createdBy.Value, targetRoleId: null,
                        returnId: returnId, userId: userId);
                }
                await MarkReturnNotificationsActionedAsync(connection, transaction, returnId);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesReturns", returnId.ToString(),
                moduleName: ModuleCode, description: $"رفض مرتجع مبيعات {number}: {rejectionReason.Trim()}");
        }

        // ---------------------------------------------------------------------
        // Print and export
        // ---------------------------------------------------------------------

        public async Task<byte[]> ExportReturnsToExcelAsync(int userId)
        {
            var permission = await GetCurrentReturnPermissionsAsync(userId);
            if (!permission.CanView || !permission.CanExport)
                throw new UnauthorizedAccessException("لا تملك صلاحية عرض وتصدير مرتجعات المبيعات.");

            var rows = await GetReturnsListCoreAsync();
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("مرتجعات المبيعات");
            worksheet.RightToLeft = true;
            worksheet.Style.Font.FontName = "Tajawal";
            worksheet.Style.Font.FontSize = 11;
            worksheet.Cell(1, 1).Value = "تقرير مرتجعات المبيعات — واي كي كوتينج   ";
            worksheet.Range(1, 1, 1, 10).Merge();
            worksheet.Range(1, 1, 1, 10).Style.Font.Bold = true;
            worksheet.Range(1, 1, 1, 10).Style.Font.FontSize = 14;
            worksheet.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var headers = new[] { "م", "رقم المرتجع", "التاريخ", "الفاتورة الأصلية", "العميل", "المخزن", "نوع المرتجع", "الحالة", "قبل الضريبة", "الإجمالي" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#070B14");
                cell.Style.Font.FontColor = XLColor.FromHtml("#D4AF37");
            }

            var row = 4;
            var index = 0;
            foreach (var item in rows)
            {
                index++;
                worksheet.Cell(row, 1).Value = index;
                worksheet.Cell(row, 2).Value = item.ReturnNumber;
                worksheet.Cell(row, 3).Value = item.ReturnDate.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 4).Value = item.InvoiceNumber ?? "—";
                worksheet.Cell(row, 5).Value = item.CustomerNameAr;
                worksheet.Cell(row, 6).Value = item.WarehouseNameAr ?? "—";
                worksheet.Cell(row, 7).Value = GetReturnTypeName(item.ReturnType);
                worksheet.Cell(row, 8).Value = GetReturnStatusName(item.ReturnStatus);
                worksheet.Cell(row, 9).Value = item.SubTotal;
                worksheet.Cell(row, 10).Value = item.TotalAmount;
                if (index % 2 == 0)
                    worksheet.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");
                row++;
            }
            worksheet.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "SalesReturns",
                moduleName: ModuleCode, description: $"تصدير {rows.Count} مرتجع مبيعات إلى Excel");
            return stream.ToArray();
        }

        public async Task<string> GenerateReturnHtmlAsync(int returnId, int userId)
        {
            var permission = await GetCurrentReturnPermissionsAsync(userId);
            if (!permission.CanView || !permission.CanPrint)
                throw new UnauthorizedAccessException("لا تملك صلاحية عرض وطباعة مرتجعات المبيعات.");

            var header = await GetReturnHeaderCoreAsync(returnId);
            if (header == null) return "<h3>مرتجع المبيعات غير موجود</h3>";
            var details = await GetReturnDetailsCoreAsync(returnId);
            var rows = new StringBuilder();
            var lineNo = 0;
            foreach (var detail in details)
            {
                lineNo++;
                rows.Append($@"<tr><td>{lineNo}</td><td>{WebUtility.HtmlEncode(detail.ItemCode ?? "")}</td>
                    <td>{WebUtility.HtmlEncode(detail.ItemNameAr ?? "")}</td>
                    <td>{WebUtility.HtmlEncode(detail.UnitName ?? "")}</td>
                    <td>{detail.Quantity:#,##0.####}</td><td>{detail.UnitPrice:#,##0.00}</td>
                    <td>{GetConditionName(detail.ItemCondition)}</td><td>{detail.TaxAmount:#,##0.00}</td>
                    <td>{detail.LineTotalWithTax:#,##0.00}</td></tr>");
            }

            var html = $@"<!doctype html><html lang='ar' dir='rtl'><head><meta charset='utf-8'>
<title>مرتجع مبيعات {WebUtility.HtmlEncode(header.ReturnNumber)}</title>
<style>
*{{box-sizing:border-box}}body{{margin:0;padding:32px;font-family:Tahoma,'Segoe UI',sans-serif;color:#070b14;background:#fff}}
.top{{text-align:center;border-bottom:3px solid #070b14;padding:0 0 18px;margin-bottom:20px;position:relative}}
.top:after{{content:'';position:absolute;bottom:-3px;left:0;right:0;height:3px;background:linear-gradient(90deg,transparent,#d4af37,#070b14,#d4af37,transparent)}}
.brand{{font-size:21px;font-weight:900}}.title{{color:#6b7280;margin:6px 0}}.number{{display:inline-block;color:#9c7c2e;border:1px solid #ead8a2;background:#fcfaf2;padding:6px 16px;border-radius:8px;font-weight:800}}
.grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:18px 0}}.box{{padding:10px;background:#fcfaf6;border:1px solid #e8e2d0;border-radius:8px}}.label{{font-size:10px;color:#6b7280}}.value{{font-size:13px;font-weight:700;margin-top:4px}}
table{{width:100%;border-collapse:collapse;margin-top:16px}}th{{background:#070b14;color:#d4af37;padding:10px;text-align:right;font-size:12px}}td{{padding:9px 10px;border-bottom:1px solid #e8e2d0;font-size:12px}}tr:nth-child(even){{background:#fcfaf6}}
.totals{{margin:18px 0 0 auto;max-width:320px;padding:14px;border:1px solid #e8e2d0;background:#fcfaf6;border-radius:10px}}.line{{display:flex;justify-content:space-between;padding:5px 0}}.grand{{border-top:2px solid #070b14;margin-top:6px;padding-top:10px;font-size:15px;font-weight:900}}
.foot{{text-align:center;color:#9ca3af;font-size:10px;border-top:1px solid #e8e2d0;margin-top:26px;padding-top:12px}}
@media print{{body{{padding:10mm}}}}
</style></head><body>
<div class='top'><div class='brand'>واي كي كوتينج   — YK Coatings</div>
<div class='title'>إشعار مرتجع مبيعات</div><span class='number'>{WebUtility.HtmlEncode(header.ReturnNumber)}</span></div>
<div class='grid'>
<div class='box'><div class='label'>التاريخ</div><div class='value'>{header.ReturnDate:dd/MM/yyyy}</div></div>
<div class='box'><div class='label'>العميل</div><div class='value'>{WebUtility.HtmlEncode(header.CustomerNameAr ?? "—")}</div></div>
<div class='box'><div class='label'>الفاتورة الأصلية</div><div class='value'>{WebUtility.HtmlEncode(header.InvoiceNumber ?? "—")}</div></div>
<div class='box'><div class='label'>المخزن المستلم</div><div class='value'>{WebUtility.HtmlEncode(header.WarehouseNameAr ?? "—")}</div></div>
<div class='box'><div class='label'>نوع المرتجع</div><div class='value'>{GetReturnTypeName(header.ReturnType)}</div></div>
<div class='box'><div class='label'>الحالة</div><div class='value'>{GetReturnStatusName(header.ReturnStatus)}</div></div>
<div class='box'><div class='label'>السبب</div><div class='value'>{WebUtility.HtmlEncode(header.ReturnReason ?? "—")}</div></div>
<div class='box'><div class='label'>ملاحظات</div><div class='value'>{WebUtility.HtmlEncode(header.Notes ?? "—")}</div></div></div>
<table><thead><tr><th>م</th><th>الكود</th><th>الصنف</th><th>الوحدة</th><th>الكمية</th><th>سعر المرتجع</th><th>الحالة</th><th>الضريبة</th><th>الإجمالي</th></tr></thead><tbody>{rows}</tbody></table>
<div class='totals'><div class='line'><span>قبل الضريبة</span><strong>{header.SubTotal:#,##0.00}</strong></div><div class='line'><span>الضريبة</span><strong>{header.TaxAmount:#,##0.00}</strong></div><div class='line grand'><span>إجمالي الإشعار الدائن</span><span>{header.TotalAmount:#,##0.00} ج.م</span></div></div>
<div class='foot'>طُبع في {DateTime.Now:dd/MM/yyyy HH:mm} — YK Coatings ERP   </div></body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "SalesReturns", returnId.ToString(),
                moduleName: ModuleCode, description: $"طباعة مرتجع مبيعات {header.ReturnNumber}");
            return html;
        }

        // ---------------------------------------------------------------------
        // Invoice lock/read and pricing helpers
        // ---------------------------------------------------------------------

        private async Task<InvoiceState> LoadReturnableInvoiceAsync(
            SqlConnection connection, IDbTransaction? transaction, int invoiceId,
            int excludeReturnId, bool lockSource)
        {
            var hint = lockSource ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
            var sql = $@"SELECT si.InvoiceID, si.InvoiceNumber, si.InvoiceDate, si.CustomerID,
                    c.CustomerNameAr, si.SalesRepID, si.WarehouseID, si.InvoiceStatus,
                    ISNULL(si.InvoiceSource,3) AS InvoiceSource, si.DeliveryID,
                    ISNULL(si.SubTotal,0) AS SubTotal, ISNULL(si.TaxableAmount,0) AS TaxableAmount,
                    ISNULL(si.TaxAmount,0) AS TaxAmount, ISNULL(si.TotalAmount,0) AS TotalAmount,
                    ISNULL(si.ShippingCost,0) AS ShippingCost, ISNULL(si.OtherCosts,0) AS OtherCosts,
                    CAST(ISNULL(si.IsTaxable,1) AS bit) AS IsTaxable,
                    ISNULL(si.ExchangeRate,1) AS ExchangeRate,
                    ISNULL(cur.IsDefault,1) AS IsDefaultCurrency
                FROM dbo.SalesInvoices si{hint}
                INNER JOIN dbo.Customers c ON c.CustomerID=si.CustomerID
                LEFT JOIN dbo.Currencies cur ON cur.CurrencyID=si.CurrencyID
                WHERE si.InvoiceID=@InvoiceID;";
            var invoice = await connection.QueryFirstOrDefaultAsync<InvoiceState>(
                sql, new { InvoiceID = invoiceId }, transaction);
            if (invoice == null) throw new KeyNotFoundException("الفاتورة الأصلية غير موجودة.");
            if (invoice.InvoiceStatus != 3)
                throw new InvalidOperationException("لا يقبل المرتجع إلا من فاتورة مبيعات معتمدة.");
            if (invoice.ShippingCost != 0 || invoice.OtherCosts != 0)
                throw new InvalidOperationException("الفاتورة تحتوي على شحن أو تكاليف إضافية لا تُحفظ كسطور قابلة للمرتجع؛ أوقفنا الإشعار حتى لا نُصدر دائنًا ناقصًا أو مبالغًا فيه.");
            if (!invoice.IsDefaultCurrency || Math.Abs(invoice.ExchangeRate - 1m) > 0.000001m)
                throw new InvalidOperationException("الفاتورة الأصلية بعملة أو سعر صرف مختلف؛ مسار الإشعار الدائن الحالي يعمل بعملة النظام الافتراضية فقط.");
            if (invoice.InvoiceSource is not (2 or 3) || invoice.DeliveryID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("الفاتورة مرتبطة بمسار إذن تسليم لا يدعمه ربط حركات المخزون المتاح؛ لم يتم إنشاء المرتجع.");

            var linesSql = $@"SELECT d.InvoiceDetailID, d.InvoiceID, d.LineNumber, d.ItemID,
                    i.ItemCode, i.ItemNameAr, d.UnitID, u.UnitNameAr AS UnitName,
                    d.Quantity, ISNULL(d.UnitPrice,0) AS UnitPrice,
                    ISNULL(d.DiscountPercent,0) AS DiscountPercent,
                    ISNULL(d.LineTotal,ISNULL(d.UnitPrice,0)*ISNULL(d.Quantity,0)) AS LineTotal,
                    ISNULL(d.TaxRate,0) AS TaxRate, ISNULL(d.TaxAmount,0) AS TaxAmount,
                    d.BatchNumber, d.ExpiryDate,
                    COALESCE(d.WarehouseID,si.WarehouseID) AS SourceWarehouseID,
                    CAST(ISNULL(si.IsTaxable,1) AS bit) AS IsTaxable,
                    ISNULL(used.PreviouslyReturnedQuantity,0) AS PreviouslyReturnedQuantity,
                    ISNULL(used.PreviouslyReturnedAmount,0) AS PreviouslyReturnedAmount,
                    ISNULL(used.PreviouslyReturnedTaxAmount,0) AS PreviouslyReturnedTaxAmount
                FROM dbo.SalesInvoiceDetails d
                INNER JOIN dbo.SalesInvoices si ON si.InvoiceID=d.InvoiceID
                INNER JOIN dbo.Items i ON i.ItemID=d.ItemID
                INNER JOIN dbo.Units u ON u.UnitID=d.UnitID
                OUTER APPLY
                (
                    SELECT SUM(rd.Quantity) AS PreviouslyReturnedQuantity,
                           SUM(ISNULL(rd.LineTotal,0)) AS PreviouslyReturnedAmount,
                           SUM(ISNULL(rd.TaxAmount,0)) AS PreviouslyReturnedTaxAmount
                    FROM dbo.SalesReturnDetails rd{(lockSource ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty)}
                    INNER JOIN dbo.SalesReturns sr{(lockSource ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty)} ON sr.ReturnID=rd.ReturnID
                    WHERE rd.InvoiceDetailID=d.InvoiceDetailID
                      AND sr.ReturnStatus IN (2,3)
                      AND sr.ReturnID<>@ExcludeReturnID
                ) used
                WHERE d.InvoiceID=@InvoiceID
                ORDER BY d.LineNumber,d.InvoiceDetailID;";
            invoice.Lines = (await connection.QueryAsync<InvoiceLineState>(
                linesSql, new { InvoiceID = invoiceId, ExcludeReturnID = excludeReturnId }, transaction)).ToList();
            if (invoice.Lines.Count == 0)
                throw new InvalidOperationException("الفاتورة الأصلية لا تحتوي على أصناف قابلة للمرتجع.");
            if (invoice.Lines.Any(x => x.Quantity <= 0))
                throw new InvalidOperationException("تحتوي الفاتورة الأصلية على كمية غير صالحة؛ أوقفنا إنشاء المرتجع.");
            foreach (var line in invoice.Lines)
            {
                if (line.UnitPrice < 0 || line.LineTotal < 0 || line.DiscountPercent < 0 || line.DiscountPercent > 100 ||
                    line.TaxRate < 0 || line.TaxRate > 100 || line.TaxAmount < 0)
                    throw new InvalidOperationException($"قيمة أو خصم أو ضريبة السطر {line.LineNumber} في الفاتورة الأصلية غير صالح.");
                var expectedLineTotal = RoundMoney(
                    line.UnitPrice * line.Quantity * (1m - line.DiscountPercent / 100m), 2);
                if (Math.Abs(expectedLineTotal - line.LineTotal) > 0.01m)
                    throw new InvalidOperationException($"إجمالي السطر {line.LineNumber} المخزن لا يطابق سعره وخصمه؛ أعد تسوية الفاتورة قبل المرتجع.");
            }

            var missingStockSource = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM dbo.SalesInvoiceDetails d
                  WHERE d.InvoiceID=@InvoiceID
                    AND NOT EXISTS
                    (
                        SELECT 1 FROM dbo.InventoryTransactions it
                        WHERE it.TransactionType=4
                          AND it.SourceDocType IN (N'SINV',N'SI')
                          AND it.SourceDocLineID=d.InvoiceDetailID
                          AND (it.SourceDocID=@InvoiceID OR it.SourceDocNumber=@InvoiceNumber)
                    );",
                new { InvoiceID = invoice.InvoiceID, invoice.InvoiceNumber }, transaction);
            if (missingStockSource > 0)
                throw new InvalidOperationException("لا توجد حركة صرف أصلية لكل سطر في الفاتورة؛ لا يمكن إرجاع تكلفة FIFO بأمان.");

            var sumOfLineTotals = invoice.Lines.Sum(x => x.LineTotal);
            if (sumOfLineTotals < 0 || invoice.TaxableAmount < 0 || invoice.TaxAmount < 0 || invoice.TotalAmount < 0)
                throw new InvalidOperationException("إجمالي الفاتورة الأصلية غير صالح لحساب الإشعار الدائن.");
            if (Math.Abs(invoice.TotalAmount - (invoice.TaxableAmount + invoice.TaxAmount)) > 0.02m)
                throw new InvalidOperationException("إجمالي الفاتورة لا يساوي الوعاء الضريبي والضريبة؛ لا يمكن إصدار إشعار دائن صحيح قبل مراجعة الفاتورة.");
            if (sumOfLineTotals == 0 && invoice.TaxableAmount > 0)
                throw new InvalidOperationException("إجمالي ضريبي للفاتورة الأصلية موجود دون قيمة أسطر مطابقة؛ يلزم مراجعة الحسابات قبل المرتجع.");
            invoice.HeaderDiscountFactor = sumOfLineTotals > 0
                ? invoice.TaxableAmount / sumOfLineTotals
                : 0m;
            if (invoice.HeaderDiscountFactor < 0 || invoice.HeaderDiscountFactor > 1.000001m)
                throw new InvalidOperationException("توزيع الخصم المخزن على الفاتورة الأصلية غير متسق؛ يلزم مراجعة الفاتورة قبل المرتجع.");
            if (invoice.HeaderDiscountFactor > 1m) invoice.HeaderDiscountFactor = 1m;

            if (!invoice.IsTaxable)
            {
                if (invoice.TaxAmount > 0)
                    throw new InvalidOperationException("الفاتورة معفاة لكن لها ضريبة إجمالية موجبة؛ أوقفنا إنشاء الإشعار حتى تُراجع بياناتها.");
                invoice.HeaderTaxFactor = 0m;
            }
            else
            {
                var sumOfLineTaxes = invoice.Lines.Sum(x => x.TaxAmount);
                var expectedHeaderTax = RoundMoney(sumOfLineTaxes * invoice.HeaderDiscountFactor, 2);
                var roundingAllowance = Math.Max(0.02m, invoice.Lines.Count * 0.01m);
                if ((sumOfLineTaxes == 0 && invoice.TaxAmount > 0) ||
                    Math.Abs(expectedHeaderTax - invoice.TaxAmount) > roundingAllowance)
                    throw new InvalidOperationException("ضريبة الفاتورة الإجمالية لا تتفق مع ضريبة أسطرها بعد الخصم؛ أوقفنا الإشعار لحماية قيمة الدائن.");
                invoice.HeaderTaxFactor = invoice.HeaderDiscountFactor;
            }
            return invoice;
        }

        private async Task<List<PricedReturnLine>> ValidateAndPriceRequestLinesAsync(
            SqlConnection connection, IDbTransaction transaction, InvoiceState invoice,
            List<SalesReturnDetailSaveDto> requestedLines, int excludeReturnId)
        {
            if (requestedLines == null) throw new ArgumentNullException(nameof(requestedLines));
            if (requestedLines.Count == 0) return new List<PricedReturnLine>();
            if (requestedLines.Any(x => x == null)) throw new InvalidOperationException("توجد بيانات سطر مرتجع فارغة.");
            if (requestedLines.Select(x => x.InvoiceDetailID).Distinct().Count() != requestedLines.Count)
                throw new InvalidOperationException("لا تسمح السياسة بتكرار سطر الفاتورة داخل مرتجع واحد.");

            var byId = invoice.Lines.ToDictionary(x => x.InvoiceDetailID);
            var ids = requestedLines.Select(x => x.InvoiceDetailID).Distinct().ToArray();
            if (ids.Any(id => !byId.ContainsKey(id)))
                throw new InvalidOperationException("أحد أصناف المرتجع لا ينتمي إلى الفاتورة الأصلية المختارة.");

            foreach (var line in requestedLines)
            {
                if (line.Quantity <= 0) throw new InvalidOperationException("كمية المرتجع يجب أن تكون أكبر من صفر.");
                if (line.Quantity != decimal.Round(line.Quantity, 4, MidpointRounding.AwayFromZero))
                    throw new InvalidOperationException("دقة كمية المرتجع لا تتجاوز أربع منازل عشرية حسب المخطط؛ لم يتم تقريب الكمية تلقائياً.");
                if (line.ItemCondition is < 1 or > 3)
                    throw new InvalidOperationException("حالة الصنف غير صالحة؛ اختر سليم أو تالف أو منتهي الصلاحية.");
                if (line.ReturnReason?.Length > 200 || line.Notes?.Length > 200)
                    throw new InvalidOperationException("سبب السطر وملاحظاته لا يتجاوز كل منهما 200 حرف حسب المخطط.");
            }

            var reserved = await GetReservedQuantityMapAsync(connection, transaction, ids, excludeReturnId);
            var result = new List<PricedReturnLine>(requestedLines.Count);
            foreach (var request in requestedLines)
            {
                var source = byId[request.InvoiceDetailID];
                var prior = reserved.TryGetValue(request.InvoiceDetailID, out var qty) ? qty : 0m;
                var available = Math.Max(0m, source.Quantity - prior);
                if (request.Quantity > available)
                    throw new InvalidOperationException(
                        $"الكمية المطلوبة للصنف {source.ItemNameAr} ({request.Quantity:#,##0.####}) تتجاوز المتاح للمرتجع ({available:#,##0.####}) بعد خصم المرتجعات المعتمدة والمعلقة.");

                var remainingLineAmount = RoundMoney(
                    RoundMoney(source.LineTotal * invoice.HeaderDiscountFactor, 2) - source.PreviouslyReturnedAmount, 2);
                var fullLineTax = source.IsTaxable
                    ? RoundMoney(source.TaxAmount * invoice.HeaderTaxFactor, 2)
                    : 0m;
                var remainingLineTax = RoundMoney(fullLineTax - source.PreviouslyReturnedTaxAmount, 2);
                if (remainingLineAmount < -0.01m || remainingLineTax < -0.01m)
                    throw new InvalidOperationException($"قيمة المرتجعات السابقة للصنف {source.ItemNameAr} تتجاوز قيمة سطر الفاتورة؛ يلزم تسوية السجلات قبل الإضافة.");
                var pricing = PriceReturnLine(source, request.Quantity, available,
                    invoice.HeaderDiscountFactor, invoice.HeaderTaxFactor,
                    Math.Max(0m, remainingLineAmount), Math.Max(0m, remainingLineTax));
                result.Add(new PricedReturnLine { Source = source, Request = request, Pricing = pricing });
            }
            return result;
        }

        private async Task ValidateAndRepriceExistingLinesAsync(
            SqlConnection connection, IDbTransaction transaction, InvoiceState invoice,
            List<SalesReturnDetailDto> details, int returnId)
        {
            var requestLines = details.Select(x => new SalesReturnDetailSaveDto
            {
                ReturnDetailID = x.ReturnDetailID,
                InvoiceDetailID = x.InvoiceDetailID,
                Quantity = x.Quantity,
                ItemCondition = x.ItemCondition,
                ReturnReason = x.ReturnReason,
                Notes = x.Notes
            }).ToList();
            var priced = await ValidateAndPriceRequestLinesAsync(
                connection, transaction, invoice, requestLines, excludeReturnId: returnId);
            foreach (var line in priced)
            {
                var existing = details.Single(x => x.ReturnDetailID == line.Request.ReturnDetailID);
                await UpdateReturnDetailAsync(connection, transaction, existing.ReturnDetailID,
                    existing.LineNumber, line.Source, line.Request, line.Pricing);
            }
        }

        private async Task<Dictionary<int, decimal>> GetReservedQuantityMapAsync(
            SqlConnection connection, IDbTransaction transaction, int[] invoiceDetailIds, int excludeReturnId)
        {
            if (invoiceDetailIds.Length == 0) return new Dictionary<int, decimal>();
            var rows = await connection.QueryAsync<ReservedReturnQuantity>(
                @"SELECT rd.InvoiceDetailID, SUM(rd.Quantity) AS Quantity
                  FROM dbo.SalesReturnDetails rd WITH (UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.SalesReturns sr WITH (UPDLOCK,HOLDLOCK) ON sr.ReturnID=rd.ReturnID
                  WHERE rd.InvoiceDetailID IN @InvoiceDetailIDs
                    AND sr.ReturnStatus IN (2,3)
                    AND sr.ReturnID<>@ExcludeReturnID
                  GROUP BY rd.InvoiceDetailID;",
                new { InvoiceDetailIDs = invoiceDetailIds, ExcludeReturnID = excludeReturnId }, transaction);
            return rows.ToDictionary(x => x.InvoiceDetailID, x => x.Quantity);
        }

        private async Task<InvoiceState?> LoadInvoiceByReturnAsync(
            SqlConnection connection, IDbTransaction? transaction, int returnId, bool lockRow)
        {
            var hint = lockRow ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
            var invoiceId = await connection.ExecuteScalarAsync<int?>(
                $"SELECT InvoiceID FROM dbo.SalesReturns{hint} WHERE ReturnID=@ReturnID;",
                new { ReturnID = returnId }, transaction);
            if (!invoiceId.HasValue) return null;
            return await LoadReturnableInvoiceAsync(connection, transaction, invoiceId.Value, returnId, lockRow);
        }

        private static SalesReturnInvoiceContextDto MapInvoiceContext(InvoiceState invoice) => new()
        {
            InvoiceID = invoice.InvoiceID,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            CustomerID = invoice.CustomerID,
            CustomerNameAr = invoice.CustomerNameAr,
            SalesRepID = invoice.SalesRepID,
            WarehouseID = invoice.WarehouseID,
            TotalAmount = invoice.TotalAmount,
            TaxableAmount = invoice.TaxableAmount,
            IsTaxable = invoice.IsTaxable,
            DetailCount = invoice.Lines.Count
        };

        private static decimal CalculateEffectiveUnitPrice(InvoiceLineState source, decimal headerDiscountFactor)
        {
            if (source.Quantity <= 0) return 0m;
            return RoundMoney((source.LineTotal / source.Quantity) * headerDiscountFactor, 4);
        }

        private static ReturnLinePricing PriceReturnLine(
            InvoiceLineState source,
            decimal quantity,
            decimal remainingQuantity,
            decimal headerDiscountFactor,
            decimal headerTaxFactor,
            decimal remainingLineAmount,
            decimal remainingLineTax)
        {
            if (source.Quantity <= 0) throw new InvalidOperationException("كمية الفاتورة الأصلية غير صالحة.");
            var netUnitPrice = CalculateEffectiveUnitPrice(source, headerDiscountFactor);
            var calculatedLineAmount = quantity == remainingQuantity
                ? remainingLineAmount
                : RoundMoney(netUnitPrice * quantity, 2);
            var lineTotal = Math.Min(calculatedLineAmount, remainingLineAmount);
            var taxRate = source.IsTaxable ? source.TaxRate : 0m;
            var calculatedTax = source.IsTaxable
                ? quantity == remainingQuantity
                    ? remainingLineTax
                    : RoundMoney(source.TaxAmount * headerTaxFactor * quantity / source.Quantity, 2)
                : 0m;
            var taxAmount = Math.Min(calculatedTax, remainingLineTax);
            return new ReturnLinePricing
            {
                UnitPrice = netUnitPrice,
                LineTotal = lineTotal,
                TaxRate = taxRate,
                TaxAmount = taxAmount,
                LineTotalWithTax = RoundMoney(lineTotal + taxAmount, 2)
            };
        }

        // ---------------------------------------------------------------------
        // Persisting rows and header totals
        // ---------------------------------------------------------------------

        private static async Task InsertReturnDetailAsync(
            SqlConnection connection, IDbTransaction transaction, int returnId, int lineNumber,
            InvoiceLineState source, SalesReturnDetailSaveDto request, ReturnLinePricing pricing)
        {
            await connection.ExecuteAsync(
                @"INSERT INTO dbo.SalesReturnDetails
                  (ReturnID, LineNumber, InvoiceDetailID, ItemID, UnitID, Quantity, UnitPrice,
                   LineTotal, TaxRate, TaxAmount, LineTotalWithTax, BatchNumber, ItemCondition,
                   ReturnReason, Notes)
                  VALUES
                  (@ReturnID,@LineNumber,@InvoiceDetailID,@ItemID,@UnitID,@Quantity,@UnitPrice,
                   @LineTotal,@TaxRate,@TaxAmount,@LineTotalWithTax,@BatchNumber,@ItemCondition,
                   @ReturnReason,@Notes);",
                new
                {
                    ReturnID = returnId,
                    LineNumber = lineNumber,
                    source.InvoiceDetailID,
                    source.ItemID,
                    source.UnitID,
                    request.Quantity,
                    pricing.UnitPrice,
                    pricing.LineTotal,
                    pricing.TaxRate,
                    pricing.TaxAmount,
                    pricing.LineTotalWithTax,
                    source.BatchNumber,
                    request.ItemCondition,
                    request.ReturnReason,
                    request.Notes
                }, transaction);
        }

        private static async Task UpdateReturnDetailAsync(
            SqlConnection connection, IDbTransaction transaction, int returnDetailId, int lineNumber,
            InvoiceLineState source, SalesReturnDetailSaveDto request, ReturnLinePricing pricing)
        {
            var affected = await connection.ExecuteAsync(
                @"UPDATE dbo.SalesReturnDetails
                  SET LineNumber=@LineNumber, InvoiceDetailID=@InvoiceDetailID, ItemID=@ItemID,
                      UnitID=@UnitID, Quantity=@Quantity, UnitPrice=@UnitPrice, LineTotal=@LineTotal,
                      TaxRate=@TaxRate, TaxAmount=@TaxAmount, LineTotalWithTax=@LineTotalWithTax,
                      BatchNumber=@BatchNumber, ItemCondition=@ItemCondition,
                      ReturnReason=@ReturnReason, Notes=@Notes
                  WHERE ReturnDetailID=@ReturnDetailID;",
                new
                {
                    ReturnDetailID = returnDetailId,
                    LineNumber = lineNumber,
                    source.InvoiceDetailID,
                    source.ItemID,
                    source.UnitID,
                    request.Quantity,
                    pricing.UnitPrice,
                    pricing.LineTotal,
                    pricing.TaxRate,
                    pricing.TaxAmount,
                    pricing.LineTotalWithTax,
                    source.BatchNumber,
                    request.ItemCondition,
                    request.ReturnReason,
                    request.Notes
                }, transaction);
            if (affected != 1) throw new InvalidOperationException("تعذر تحديث أحد أسطر مرتجع المبيعات.");
        }

        private static async Task RecalculateReturnTotalsAsync(
            SqlConnection connection, IDbTransaction transaction, int returnId)
        {
            var updated = await connection.ExecuteAsync(
                @"UPDATE sr
                  SET SubTotal=tot.SubTotal,
                      TaxAmount=tot.TaxAmount,
                      TotalAmount=tot.SubTotal+tot.TaxAmount
                  FROM dbo.SalesReturns sr
                  CROSS APPLY
                  (
                    SELECT CAST(ROUND(ISNULL(SUM(rd.LineTotal),0),2) AS decimal(18,2)) AS SubTotal,
                           CAST(ROUND(ISNULL(SUM(rd.TaxAmount),0),2) AS decimal(18,2)) AS TaxAmount
                    FROM dbo.SalesReturnDetails rd WHERE rd.ReturnID=sr.ReturnID
                  ) tot
                  WHERE sr.ReturnID=@ReturnID;",
                new { ReturnID = returnId }, transaction);
            if (updated != 1) throw new InvalidOperationException("تعذر إعادة احتساب إجمالي مرتجع المبيعات.");
        }

        private static async Task<ReturnTotals> GetReturnTotalsAsync(
            SqlConnection connection, IDbTransaction transaction, int returnId)
        {
            return await connection.QueryFirstOrDefaultAsync<ReturnTotals>(
                @"SELECT ISNULL(SubTotal,0) AS SubTotal, ISNULL(TaxAmount,0) AS TaxAmount,
                         ISNULL(TotalAmount,0) AS TotalAmount
                  FROM dbo.SalesReturns WHERE ReturnID=@ReturnID;",
                new { ReturnID = returnId }, transaction)
                ?? new ReturnTotals();
        }

        private async Task<List<SalesReturnListDto>> GetReturnsListCoreAsync()
        {
            using var connection = CreateConnection();
            const string sql = @"SELECT
                    sr.ReturnID, sr.ReturnNumber, sr.ReturnDate, sr.ReturnStatus, sr.ReturnType,
                    sr.ReturnReason, sr.CustomerID, c.CustomerNameAr,
                    sr.InvoiceID, si.InvoiceNumber, sr.WarehouseID, w.WarehouseNameAr,
                    ISNULL(sr.SubTotal,0) AS SubTotal, ISNULL(sr.TaxAmount,0) AS TaxAmount,
                    ISNULL(sr.TotalAmount,0) AS TotalAmount,
                    (SELECT COUNT(*) FROM dbo.SalesReturnDetails rd WHERE rd.ReturnID=sr.ReturnID) AS ItemCount,
                    sr.CreatedBy, sr.CreatedDate
                FROM dbo.SalesReturns sr
                INNER JOIN dbo.Customers c ON c.CustomerID=sr.CustomerID
                LEFT JOIN dbo.SalesInvoices si ON si.InvoiceID=sr.InvoiceID
                LEFT JOIN dbo.Warehouses w ON w.WarehouseID=sr.WarehouseID
                ORDER BY sr.ReturnDate DESC,sr.ReturnID DESC;";
            return (await connection.QueryAsync<SalesReturnListDto>(sql)).ToList();
        }

        private async Task<SalesReturnHeaderDto?> GetReturnHeaderCoreAsync(int returnId)
        {
            using var connection = CreateConnection();
            return await GetReturnHeaderCoreAsync(connection, null, returnId);
        }

        private static async Task<SalesReturnHeaderDto?> GetReturnHeaderCoreAsync(
            SqlConnection connection, IDbTransaction? transaction, int returnId)
        {
            var header = await connection.QueryFirstOrDefaultAsync<SalesReturnHeaderDto>(
                @"SELECT sr.ReturnID, sr.ReturnNumber, sr.ReturnDate, sr.ReturnStatus, sr.ReturnType,
                         sr.ReturnReason, sr.CustomerID, c.CustomerNameAr,
                         sr.InvoiceID, si.InvoiceNumber, sr.SalesRepID, sr.WarehouseID,
                         w.WarehouseNameAr, ISNULL(sr.SubTotal,0) AS SubTotal,
                         ISNULL(sr.TaxAmount,0) AS TaxAmount, ISNULL(sr.TotalAmount,0) AS TotalAmount,
                         sr.ApprovedBy, sr.ApprovedDate, emp.FullNameAr AS ApprovedByName,
                         sr.Notes, sr.CreatedBy, sr.CreatedDate, sr.ModifiedBy, sr.ModifiedDate
                  FROM dbo.SalesReturns sr
                  INNER JOIN dbo.Customers c ON c.CustomerID=sr.CustomerID
                  LEFT JOIN dbo.SalesInvoices si ON si.InvoiceID=sr.InvoiceID
                  LEFT JOIN dbo.Warehouses w ON w.WarehouseID=sr.WarehouseID
                  LEFT JOIN dbo.Employees emp ON emp.EmployeeID=sr.ApprovedBy
                  WHERE sr.ReturnID=@ReturnID;",
                new { ReturnID = returnId }, transaction);
            if (header != null)
            {
                var (notes, reason) = SplitRejectionNote(header.Notes);
                header.Notes = notes;
                header.RejectionReason = reason;
            }
            return header;
        }

        private async Task<List<SalesReturnDetailDto>> GetReturnDetailsCoreAsync(int returnId)
        {
            using var connection = CreateConnection();
            const string sql = @"SELECT
                    rd.ReturnDetailID, rd.ReturnID, rd.LineNumber, rd.InvoiceDetailID,
                    rd.ItemID, i.ItemCode, i.ItemNameAr, rd.UnitID, u.UnitNameAr AS UnitName,
                    rd.Quantity, ISNULL(id.Quantity,0) AS InvoiceQuantity,
                    ISNULL(prev.PreviouslyReturnedQuantity,0) AS PreviouslyReturnedQuantity,
                    CASE WHEN ISNULL(id.Quantity,0)-ISNULL(prev.PreviouslyReturnedQuantity,0)<0 THEN 0
                         ELSE ISNULL(id.Quantity,0)-ISNULL(prev.PreviouslyReturnedQuantity,0) END AS RemainingReturnQuantity,
                    rd.UnitPrice, rd.LineTotal, ISNULL(rd.TaxRate,0) AS TaxRate,
                    ISNULL(rd.TaxAmount,0) AS TaxAmount,
                    ISNULL(rd.LineTotalWithTax,ISNULL(rd.LineTotal,0)+ISNULL(rd.TaxAmount,0)) AS LineTotalWithTax,
                    rd.BatchNumber, ISNULL(rd.ItemCondition,1) AS ItemCondition,
                    rd.ReturnReason, rd.Notes
                FROM dbo.SalesReturnDetails rd
                INNER JOIN dbo.SalesReturns currentReturn ON currentReturn.ReturnID=rd.ReturnID
                INNER JOIN dbo.Items i ON i.ItemID=rd.ItemID
                INNER JOIN dbo.Units u ON u.UnitID=rd.UnitID
                LEFT JOIN dbo.SalesInvoiceDetails id ON id.InvoiceDetailID=rd.InvoiceDetailID
                OUTER APPLY
                (
                    SELECT SUM(otherDetail.Quantity) AS PreviouslyReturnedQuantity
                    FROM dbo.SalesReturnDetails otherDetail
                    INNER JOIN dbo.SalesReturns otherReturn ON otherReturn.ReturnID=otherDetail.ReturnID
                    WHERE otherDetail.InvoiceDetailID=rd.InvoiceDetailID
                      AND otherReturn.ReturnStatus IN (2,3)
                      AND otherReturn.ReturnID<>currentReturn.ReturnID
                ) prev
                WHERE rd.ReturnID=@ReturnID
                ORDER BY rd.LineNumber,rd.ReturnDetailID;";
            return (await connection.QueryAsync<SalesReturnDetailDto>(sql, new { ReturnID = returnId })).ToList();
        }

        private async Task<ReturnState?> LoadReturnStateAsync(
            SqlConnection connection, IDbTransaction transaction, int returnId, bool lockRow)
        {
            var hint = lockRow ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
            return await connection.QueryFirstOrDefaultAsync<ReturnState>(
                $@"SELECT ReturnID, ReturnNumber, ReturnDate, ReturnStatus, ReturnType,
                           CustomerID, InvoiceID, WarehouseID, ISNULL(SubTotal,0) AS SubTotal,
                           ISNULL(TaxAmount,0) AS TaxAmount, ISNULL(TotalAmount,0) AS TotalAmount,
                           CreatedBy, Notes AS RawNotes
                    FROM dbo.SalesReturns{hint} WHERE ReturnID=@ReturnID;",
                new { ReturnID = returnId }, transaction);
        }

        private static async Task<List<SalesReturnDetailDto>> LoadReturnLinesForWorkflowAsync(
            SqlConnection connection, IDbTransaction transaction, int returnId)
        {
            const string sql = @"SELECT rd.ReturnDetailID,rd.ReturnID,rd.LineNumber,rd.InvoiceDetailID,
                    rd.ItemID,rd.UnitID,rd.Quantity,rd.UnitPrice,rd.LineTotal,
                    ISNULL(rd.TaxRate,0) AS TaxRate,ISNULL(rd.TaxAmount,0) AS TaxAmount,
                    ISNULL(rd.LineTotalWithTax,ISNULL(rd.LineTotal,0)+ISNULL(rd.TaxAmount,0)) AS LineTotalWithTax,
                    rd.BatchNumber,ISNULL(rd.ItemCondition,1) AS ItemCondition,
                    rd.ReturnReason,rd.Notes
                FROM dbo.SalesReturnDetails rd WITH (UPDLOCK,HOLDLOCK)
                WHERE rd.ReturnID=@ReturnID ORDER BY rd.LineNumber,rd.ReturnDetailID;";
            return (await connection.QueryAsync<SalesReturnDetailDto>(sql, new { ReturnID = returnId }, transaction)).ToList();
        }

        // ---------------------------------------------------------------------
        // Policy and invariant helpers
        // ---------------------------------------------------------------------

        private async Task<ModulePermissionDto> GetCurrentReturnPermissionsAsync(int userId)
        {
            if (userId <= 0 || !_session.IsLoggedIn || _session.UserID != userId)
                throw new UnauthorizedAccessException("جلسة المستخدم غير صالحة؛ سجّل الدخول مرة أخرى.");
            await _permissions.LoadPermissionsAsync(userId);
            return _permissions.GetModule(ModuleCode)
                ?? throw new UnauthorizedAccessException("لا توجد صلاحيات مهيأة لمرتجعات المبيعات SCR_SRET.");
        }

        private async Task RequireReturnPermissionAsync(
            int userId, Func<ModulePermissionDto, bool> predicate, string message)
        {
            var permission = await GetCurrentReturnPermissionsAsync(userId);
            if (!predicate(permission)) throw new UnauthorizedAccessException(message);
        }

        private static async Task InsertReturnWorkflowNotificationAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            string title,
            string message,
            byte priority,
            int? targetUserId,
            int? targetRoleId,
            int returnId,
            int userId)
        {
            await connection.ExecuteAsync(
                @"INSERT INTO dbo.Notifications
                  (NotificationType,Title,Message,Priority,TargetUserID,TargetRoleID,
                   RelatedModule,RelatedRecordID,CreatedBy,CreatedDate)
                  VALUES
                  (3,@Title,@Message,@Priority,@TargetUserID,@TargetRoleID,
                   @ModuleCode,@ReturnID,@UserID,GETDATE());",
                new
                {
                    Title = title,
                    Message = message,
                    Priority = priority,
                    TargetUserID = targetUserId,
                    TargetRoleID = targetRoleId,
                    ModuleCode,
                    ReturnID = returnId,
                    UserID = userId
                }, transaction);
        }

        private static Task MarkReturnNotificationsActionedAsync(
            SqlConnection connection, IDbTransaction transaction, int returnId) =>
            connection.ExecuteAsync(
                @"UPDATE dbo.Notifications
                  SET IsActioned=1,ActionDate=GETDATE(),IsRead=1,ReadDate=ISNULL(ReadDate,GETDATE())
                  WHERE RelatedModule=@ModuleCode AND RelatedRecordID=@ReturnID AND IsActioned=0
                    AND TargetRoleID IS NOT NULL AND TargetUserID IS NULL;",
                new { ModuleCode, ReturnID = returnId }, transaction);

        private async Task EnsureCanEditDraftAsync(
            SqlConnection connection, IDbTransaction transaction, ReturnState header,
            int userId, ModulePermissionDto permission)
        {
            if (permission.CanEdit) return;
            if (permission.CanAdd && header.CreatedBy == userId && header.ReturnStatus is 1 or 4) return;
            throw new UnauthorizedAccessException("لا تملك صلاحية تعديل هذه المسودة؛ يلزم صلاحية التعديل أو أن تكون منشئ المسودة.");
        }

        private async Task EnsureActiveWarehouseAsync(
            SqlConnection connection, IDbTransaction transaction, int warehouseId)
        {
            if (warehouseId <= 0) throw new InvalidOperationException("اختر مخزن استلام نشطاً.");
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK) WHERE WarehouseID=@ID AND IsActive=1;",
                new { ID = warehouseId }, transaction);
            if (exists != 1) throw new InvalidOperationException("المخزن المختار غير موجود أو غير نشط.");
        }

        private static void ValidateReturnType(int returnType)
        {
            if (returnType is < 1 or > 4)
                throw new InvalidOperationException("نوع المرتجع غير صالح؛ اختر أحد الأنواع المسموح بها بالمخطط.");
        }

        private static void ValidateHeaderReason(string? reason)
        {
            if (reason?.Length > 500)
                throw new InvalidOperationException("سبب المرتجع لا يتجاوز 500 حرف حسب المخطط.");
        }

        private static void EnsureReturnMatchesInvoice(ReturnState header, InvoiceState invoice)
        {
            if (header.InvoiceID != invoice.InvoiceID || header.CustomerID != invoice.CustomerID)
                throw new InvalidOperationException("الفاتورة أو العميل في رأس المرتجع لا يطابق الفاتورة الأصلية؛ أوقفنا المعالجة.");
        }

        private static void ValidateReturnDate(DateTime returnDate, DateTime invoiceDate)
        {
            if (returnDate.Date < invoiceDate.Date)
                throw new InvalidOperationException("تاريخ المرتجع لا يمكن أن يسبق تاريخ فاتورة المبيعات الأصلية.");
            if (returnDate.Date > DateTime.Today)
                throw new InvalidOperationException("لا يقبل هذا المسار تاريخ مرتجع مستقبلياً؛ يجب أن يتطابق تاريخ المستند مع واقعة استلام البضاعة.");
        }

        private static async Task<string> GetNextDocumentNumberAsync(
            SqlConnection connection, IDbTransaction transaction, string sequenceCode)
        {
            var value = await connection.QueryFirstOrDefaultAsync<string>(
                @"DECLARE @NextNumber NVARCHAR(50);
                  EXEC dbo.sp_GetNextNumber @SequenceCode, @NextNumber OUTPUT;
                  SELECT @NextNumber;",
                new { SequenceCode = sequenceCode }, transaction);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"تعذر توليد رقم تسلسلي من {sequenceCode}؛ لم يتم حفظ المستند.");
            return value;
        }

        private static (string? Notes, string? RejectionReason) SplitRejectionNote(string? storedNotes)
        {
            if (string.IsNullOrEmpty(storedNotes)) return (storedNotes, null);
            var index = storedNotes.LastIndexOf(RejectionNoteMarker, StringComparison.Ordinal);
            if (index < 0) return (storedNotes, null);
            var notes = storedNotes[..index].TrimEnd('\r', '\n');
            var reason = storedNotes[(index + RejectionNoteMarker.Length)..].Trim();
            return (string.IsNullOrWhiteSpace(notes) ? null : notes,
                    string.IsNullOrWhiteSpace(reason) ? null : reason);
        }

        private static string? CombineNotesAndRejection(string? notes, string? rejectionReason)
        {
            var cleanNotes = SplitRejectionNote(notes).Notes;
            if (string.IsNullOrWhiteSpace(rejectionReason)) return cleanNotes;
            return string.IsNullOrWhiteSpace(cleanNotes)
                ? RejectionNoteMarker + rejectionReason.Trim()
                : cleanNotes.TrimEnd() + RejectionNoteMarker + rejectionReason.Trim();
        }

        private static decimal RoundMoney(decimal value, int decimals) =>
            decimal.Round(value, decimals, MidpointRounding.AwayFromZero);

        // Status meanings selected for this workflow because the existing CHECK constraint only lists 1-4.
        // 1=draft, 2=pending approval, 3=approved, 4=rejected; persist these meanings in SCR_SRET documentation.
        public static string GetReturnStatusName(int status) => status switch
        {
            1 => "مسودة — كود 1",
            2 => "بانتظار الاعتماد — كود 2",
            3 => "معتمد — كود 3",
            4 => "مرفوض — كود 4",
            _ => "غير محدد"
        };

        public static string GetReturnStatusColor(int status) => status switch
        {
            1 => "#6b7280",
            2 => "#D4AF37",
            3 => "#15803D",
            4 => "#B91C1C",
            _ => "#6b7280"
        };

        // The existing constraint permits codes 1-4 but supplies no meaning dictionary.
        // Keep the persisted code neutral instead of mislabelling legacy returns.
        public static string GetReturnTypeName(int type) => type is >= 1 and <= 4
            ? $"نوع مرتجع — كود {type} (غير موثق)"
            : "نوع غير محدد";

        // ItemCondition meanings selected for this module: only value 1 is saleable/restocked;
        // 2 and 3 are held out of available stock, conservatively preserving their FIFO cost as COGS.
        public static string GetConditionName(int condition) => condition switch
        {
            1 => "سليم — يعاد للمخزون",
            2 => "تالف — لا يعاد للمخزون المتاح",
            3 => "منتهي الصلاحية — لا يعاد للمخزون المتاح",
            _ => "غير محدد"
        };

        // ---------------------------------------------------------------------
        // Internal row types shared with FIFO posting partial
        // ---------------------------------------------------------------------

        private sealed class InvoiceState
        {
            public int InvoiceID { get; set; }
            public string InvoiceNumber { get; set; } = "";
            public DateTime InvoiceDate { get; set; }
            public int CustomerID { get; set; }
            public string CustomerNameAr { get; set; } = "";
            public int? SalesRepID { get; set; }
            public int? WarehouseID { get; set; }
            public int InvoiceStatus { get; set; }
            public int InvoiceSource { get; set; }
            public int? DeliveryID { get; set; }
            public decimal SubTotal { get; set; }
            public decimal TaxableAmount { get; set; }
            public decimal TaxAmount { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal ShippingCost { get; set; }
            public decimal OtherCosts { get; set; }
            public bool IsTaxable { get; set; }
            public decimal ExchangeRate { get; set; }
            public bool IsDefaultCurrency { get; set; }
            public decimal HeaderDiscountFactor { get; set; }
            public decimal HeaderTaxFactor { get; set; }
            public List<InvoiceLineState> Lines { get; set; } = new();
        }

        private sealed class InvoiceLineState
        {
            public int InvoiceDetailID { get; set; }
            public int InvoiceID { get; set; }
            public int LineNumber { get; set; }
            public int ItemID { get; set; }
            public string? ItemCode { get; set; }
            public string? ItemNameAr { get; set; }
            public int UnitID { get; set; }
            public string? UnitName { get; set; }
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountPercent { get; set; }
            public decimal LineTotal { get; set; }
            public decimal TaxRate { get; set; }
            public decimal TaxAmount { get; set; }
            public decimal PreviouslyReturnedAmount { get; set; }
            public decimal PreviouslyReturnedTaxAmount { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int? SourceWarehouseID { get; set; }
            public bool IsTaxable { get; set; }
            public decimal PreviouslyReturnedQuantity { get; set; }
        }

        private sealed class ReturnLinePricing
        {
            public decimal UnitPrice { get; set; }
            public decimal LineTotal { get; set; }
            public decimal TaxRate { get; set; }
            public decimal TaxAmount { get; set; }
            public decimal LineTotalWithTax { get; set; }
        }

        private sealed class PricedReturnLine
        {
            public InvoiceLineState Source { get; set; } = new();
            public SalesReturnDetailSaveDto Request { get; set; } = new();
            public ReturnLinePricing Pricing { get; set; } = new();
        }

        private sealed class ReturnState
        {
            public int ReturnID { get; set; }
            public string ReturnNumber { get; set; } = "";
            public DateTime ReturnDate { get; set; }
            public int ReturnStatus { get; set; }
            public int ReturnType { get; set; }
            public int CustomerID { get; set; }
            public int InvoiceID { get; set; }
            public int WarehouseID { get; set; }
            public decimal SubTotal { get; set; }
            public decimal TaxAmount { get; set; }
            public decimal TotalAmount { get; set; }
            public int? CreatedBy { get; set; }
            public string? RawNotes { get; set; }
        }

        private sealed class ExistingReturnDetail
        {
            public int ReturnDetailID { get; set; }
            public int InvoiceDetailID { get; set; }
            public int LineNumber { get; set; }
        }

        private sealed class ReservedReturnQuantity
        {
            public int InvoiceDetailID { get; set; }
            public decimal Quantity { get; set; }
        }

        private sealed class ReturnTotals
        {
            public decimal SubTotal { get; set; }
            public decimal TaxAmount { get; set; }
            public decimal TotalAmount { get; set; }
        }
    }
}
