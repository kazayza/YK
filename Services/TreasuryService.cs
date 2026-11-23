using System.Data;
using Dapper;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class TreasuryService : BaseDbService
    {
        private const string CashBoxOpeningBalanceSourceDocType = "CashBoxOpeningBalance";

        private readonly AuditService _audit;
        private readonly NotificationService _notif;
        private readonly ModuleAuthorizationService _authorization;
        private readonly SequenceService _sequence;
        private readonly ILogger<TreasuryService> _logger;

        public TreasuryService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif,
            ModuleAuthorizationService authorization,
            SequenceService sequence,
            ILogger<TreasuryService> logger) : base(configuration)
        {
            _audit = audit;
            _notif = notif;
            _authorization = authorization;
            _sequence = sequence;
            _logger = logger;
        }

        private async Task RequireCurrentAnyModulePermissionAsync(
            string deniedMessage,
            Func<ModulePermissionDto, bool> predicate,
            params string[] moduleCodes)
        {
            if (moduleCodes.Length == 0)
                throw new ArgumentException("يجب تحديد وحدة واحدة على الأقل.", nameof(moduleCodes));

            foreach (var moduleCode in moduleCodes)
            {
                try
                {
                    await _authorization.RequireCurrentUserPermissionAsync(moduleCode, predicate, deniedMessage);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    // جرّب رمزاً بديلاً معروفاً لهذه الشاشة؛ تبقى النتيجة رفضاً إن لم ينجح أي رمز.
                }
            }

            throw new UnauthorizedAccessException(deniedMessage);
        }

        private async Task RequireAnyModulePermissionAsync(
            int userId,
            string deniedMessage,
            Func<ModulePermissionDto, bool> predicate,
            params string[] moduleCodes)
        {
            if (moduleCodes.Length == 0)
                throw new ArgumentException("يجب تحديد وحدة واحدة على الأقل.", nameof(moduleCodes));

            foreach (var moduleCode in moduleCodes)
            {
                try
                {
                    await _authorization.RequireUserPermissionAsync(userId, moduleCode, predicate, deniedMessage);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    // جرّب رمزاً بديلاً معروفاً لهذه الشاشة؛ تبقى النتيجة رفضاً إن لم ينجح أي رمز.
                }
            }

            throw new UnauthorizedAccessException(deniedMessage);
        }

        private Task RequireCashBoxPermissionAsync(
            int userId,
            Func<ModulePermissionDto, bool> predicate,
            string deniedMessage) =>
            RequireAnyModulePermissionAsync(userId, deniedMessage, predicate, "SCR_BANKS", "SCR_CASHBOX", "SCR_CASHBOXES");

        private Task RequireChequeApprovalPermissionAsync(int userId, string deniedMessage) =>
            RequireAnyModulePermissionAsync(
                userId, deniedMessage, p => p.CanApprove, "SCR_CHEQUES", "SCR_CHEQUE");

        private async Task NotifyVoucherPendingApprovalAsync(
            string moduleCode, string voucherType, string voucherNumber, decimal amount, int voucherId, int userId)
        {
            try
            {
                await _notif.CreateForPermissionHoldersAsync(
                    notificationType: 3,
                    title: $"{voucherType} بانتظار الاعتماد",
                    message: $"{voucherType} رقم {voucherNumber} بمبلغ {amount:N2} بانتظار الاعتماد.",
                    priority: 2,
                    permissionModuleCode: moduleCode,
                    requiredPermission: ModulePermissionRequirement.Approve,
                    relatedModule: moduleCode,
                    relatedRecordId: voucherId,
                    createdBy: userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create a pending-approval notification for a treasury voucher.");
            }
        }

        private async Task CompleteVoucherNotificationsAsync(
            string moduleCode, string voucherType, string voucherNumber, int voucherId, int? createdBy, int userId)
        {
            try
            {
                await _notif.MarkRelatedAsActionedAsync(moduleCode, voucherId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after a treasury voucher action.");
            }

            if (!createdBy.HasValue || createdBy.Value <= 0 || createdBy.Value == userId) return;

            try
            {
                await _notif.CreateForAuthorizedUserAsync(
                    notificationType: 3,
                    title: $"تم اعتماد {voucherType}",
                    message: $"تم اعتماد {voucherType} رقم {voucherNumber}.",
                    priority: 2,
                    targetUserId: createdBy.Value,
                    permissionModuleCode: moduleCode,
                    relatedModule: moduleCode,
                    relatedRecordId: voucherId,
                    createdBy: userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify the creator after approving a treasury voucher.");
            }
        }

        private async Task MarkVoucherNotificationsActionedAsync(string moduleCode, int voucherId)
        {
            try
            {
                await _notif.MarkRelatedAsActionedAsync(moduleCode, voucherId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete notifications for a treasury voucher.");
            }
        }

        private async Task<int> ValidateInvoiceAllocationsAsync(
            IDbConnection connection,
            IDbTransaction transaction,
            bool isSalesInvoice,
            int partyId,
            int? voucherCurrencyId,
            decimal voucherExchangeRate,
            decimal voucherAmount,
            IReadOnlyCollection<VoucherInvoiceAllocationDto> allocations,
            int? currentVoucherId)
        {
            if (partyId <= 0) throw new InvalidOperationException("اختر العميل أو المورد قبل تخصيص السند على الفواتير.");
            if (allocations.Count == 0) throw new InvalidOperationException("اختر فاتورة واحدة على الأقل للتسوية.");
            if (voucherAmount <= 0) throw new InvalidOperationException("يجب أن يكون مبلغ سند التسوية أكبر من صفر.");
            if (allocations.Any(x => x.InvoiceID <= 0 || x.AppliedAmount <= 0))
                throw new InvalidOperationException("يجب أن يكون لكل فاتورة مختارة مبلغ تخصيص موجب.");
            if (allocations.Any(x => decimal.Round(x.AppliedAmount, 2, MidpointRounding.AwayFromZero) != x.AppliedAmount))
                throw new InvalidOperationException("جداول تخصيص الفواتير الحالية تدعم منزلتين عشريتين فقط.");
            if (allocations.Select(x => x.InvoiceID).Distinct().Count() != allocations.Count)
                throw new InvalidOperationException("لا يمكن تكرار الفاتورة نفسها في تخصيصات السند.");

            var allocatedTotal = allocations.Sum(x => x.AppliedAmount);
            if (allocatedTotal != voucherAmount)
                throw new InvalidOperationException("إجمالي مبالغ تخصيص الفواتير يجب أن يساوي مبلغ السند.");

            int? controlAccountId = null;
            foreach (var allocation in allocations.OrderBy(x => x.InvoiceID))
            {
                var state = isSalesInvoice
                    ? await connection.QueryFirstOrDefaultAsync<SettlementInvoiceState>(
                        @"SELECT si.InvoiceID, si.InvoiceNumber, si.InvoiceDate, si.CustomerID AS PartyID,
                                 si.InvoiceStatus, ISNULL(si.PaymentStatus,1) AS PaymentStatus,
                                 ISNULL(si.TotalAmount,0) AS TotalAmount, ISNULL(si.PaidAmount,0) AS PaidAmount,
                                 si.CurrencyID, ISNULL(si.ExchangeRate,1) AS ExchangeRate,
                                 c.CustomerNameAr AS PartyName
                          FROM dbo.SalesInvoices si WITH (UPDLOCK,HOLDLOCK)
                          INNER JOIN dbo.Customers c ON c.CustomerID=si.CustomerID
                          WHERE si.InvoiceID=@InvoiceID",
                        new { allocation.InvoiceID }, transaction)
                    : await connection.QueryFirstOrDefaultAsync<SettlementInvoiceState>(
                        @"SELECT pi.InvoiceID, pi.InvoiceNumber, pi.InvoiceDate, pi.SupplierID AS PartyID,
                                 pi.InvoiceStatus, ISNULL(pi.PaymentStatus,1) AS PaymentStatus,
                                 ISNULL(pi.TotalAmount,0) AS TotalAmount, ISNULL(pi.PaidAmount,0) AS PaidAmount,
                                 pi.CurrencyID, ISNULL(pi.ExchangeRate,1) AS ExchangeRate,
                                 s.SupplierNameAr AS PartyName
                          FROM dbo.PurchaseInvoices pi WITH (UPDLOCK,HOLDLOCK)
                          INNER JOIN dbo.Suppliers s ON s.SupplierID=pi.SupplierID
                          WHERE pi.InvoiceID=@InvoiceID",
                        new { allocation.InvoiceID }, transaction);

                if (state is null) throw new InvalidOperationException($"الفاتورة رقم {allocation.InvoiceID} غير موجودة.");
                if (state.PartyID != partyId)
                    throw new InvalidOperationException("لا يمكن تخصيص السند لفواتير تخص عميلاً أو مورداً آخر.");

                var invoiceIsPayable = isSalesInvoice
                    ? state.InvoiceStatus is 3 or 5
                    : state.InvoiceStatus is 3 or 4 or 6;
                if (!invoiceIsPayable || state.PaymentStatus is not (1 or 2))
                    throw new InvalidOperationException($"الفاتورة {state.InvoiceNumber} ليست فاتورة معتمدة قابلة للسداد.");

                if ((state.CurrencyID ?? 0) != (voucherCurrencyId ?? 0))
                    throw new InvalidOperationException($"عملة الفاتورة {state.InvoiceNumber} لا تطابق عملة السند.");
                if (Math.Abs(state.ExchangeRate - (voucherExchangeRate <= 0 ? 1 : voucherExchangeRate)) > 0.000001m)
                    throw new InvalidOperationException("تعذر تسوية فاتورة بسعر صرف مختلف عن سعر السند؛ يلزم ضبط معالجة فروق العملة أولاً.");

                var reservedByOtherDrafts = isSalesInvoice
                    ? await connection.QueryFirstOrDefaultAsync<decimal>(
                        @"SELECT ISNULL(SUM(a.AllocatedAmount),0)
                          FROM dbo.ReceiptAllocations a WITH (UPDLOCK,HOLDLOCK)
                          INNER JOIN dbo.ReceiptVouchers rv WITH (UPDLOCK,HOLDLOCK) ON rv.ReceiptID=a.ReceiptID
                          WHERE a.InvoiceID=@InvoiceID AND rv.ReceiptStatus=1
                            AND (@CurrentVoucherID IS NULL OR rv.ReceiptID<>@CurrentVoucherID)",
                        new { allocation.InvoiceID, CurrentVoucherID = currentVoucherId }, transaction)
                    : await connection.QueryFirstOrDefaultAsync<decimal>(
                        @"SELECT ISNULL(SUM(a.AllocatedAmount),0)
                          FROM dbo.PaymentAllocations a WITH (UPDLOCK,HOLDLOCK)
                          INNER JOIN dbo.PaymentVouchers pv WITH (UPDLOCK,HOLDLOCK) ON pv.PaymentID=a.PaymentID
                          WHERE a.InvoiceID=@InvoiceID AND pv.PaymentStatus=1
                            AND (@CurrentVoucherID IS NULL OR pv.PaymentID<>@CurrentVoucherID)",
                        new { allocation.InvoiceID, CurrentVoucherID = currentVoucherId }, transaction);

                var remaining = state.TotalAmount - state.PaidAmount - reservedByOtherDrafts;
                if (remaining <= 0 || allocation.AppliedAmount > remaining)
                    throw new InvalidOperationException($"مبلغ التسوية على الفاتورة {state.InvoiceNumber} يتجاوز المتبقي المتاح ({Math.Max(0, remaining):#,##0.00}).");

                var candidateAccountId = await ResolveInvoiceControlAccountAsync(
                    connection, transaction, isSalesInvoice, state.InvoiceID, state.PartyID,
                    state.InvoiceNumber ?? "", state.TotalAmount, state.ExchangeRate);
                if (controlAccountId.HasValue && controlAccountId.Value != candidateAccountId)
                    throw new InvalidOperationException("الفواتير المختارة مرتبطة بحسابات ذمم مختلفة؛ أنشئ سنداً منفصلاً لكل حساب ذمم.");
                controlAccountId = candidateAccountId;

                allocation.InvoiceNumber = state.InvoiceNumber;
                allocation.InvoiceDate = state.InvoiceDate;
                allocation.PartyID = state.PartyID;
                allocation.PartyName = state.PartyName;
                allocation.TotalAmount = state.TotalAmount;
                allocation.PaidAmount = state.PaidAmount;
                allocation.RemainingAmount = state.TotalAmount - state.PaidAmount;
            }

            return controlAccountId ?? throw new InvalidOperationException("تعذر تحديد حساب الذمم المرتبط بالفاتورة.");
        }

        private async Task<int> ResolveInvoiceControlAccountAsync(
            IDbConnection connection,
            IDbTransaction transaction,
            bool isSalesInvoice,
            int invoiceId,
            int partyId,
            string invoiceNumber,
            decimal totalAmount,
            decimal exchangeRate)
        {
            var journalSql = isSalesInvoice
                ? @"SELECT JournalID FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                    WHERE SourceDocType IN (N'SI',N'SalesInvoice') AND SourceDocID=@InvoiceID
                      AND SourceDocNumber=@InvoiceNumber AND JournalStatus=2"
                : @"SELECT JournalID FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                    WHERE SourceDocType IN (N'PI',N'PurchaseInvoice') AND SourceDocID=@InvoiceID
                      AND SourceDocNumber=@InvoiceNumber AND JournalStatus=2";
            var journalIds = (await connection.QueryAsync<int>(journalSql,
                new { InvoiceID = invoiceId, InvoiceNumber = invoiceNumber }, transaction)).Distinct().ToList();
            if (journalIds.Count != 1)
                throw new InvalidOperationException($"تعذر إثبات قيد مرحّل واحد للفاتورة {invoiceNumber}؛ أُوقفت التسوية بأمان.");

            var accountType = isSalesInvoice ? 1 : 2;
            var sideAmount = isSalesInvoice ? "jd.DebitAmount" : "jd.CreditAmount";
            var oppositeAmount = isSalesInvoice ? "jd.CreditAmount" : "jd.DebitAmount";
            var candidates = (await connection.QueryAsync<int>(
                $@"SELECT jd.AccountID
                   FROM dbo.JournalEntryDetails jd
                   INNER JOIN dbo.ChartOfAccounts coa ON coa.AccountID=jd.AccountID
                   WHERE jd.JournalID=@JournalID AND coa.AccountType=@AccountType
                     AND UPPER(LTRIM(RTRIM(ISNULL(coa.LinkedEntityType,N''))))=UPPER(@LinkedEntityType)
                     AND (coa.LinkedEntityID IS NULL OR coa.LinkedEntityID=@PartyID)
                     AND (ISNULL(coa.IsDetailAccount,0)=1
                          OR (ISNULL(coa.IsSystemAccount,0)=1 AND coa.AccountCode=@ControlAccountCode))
                     AND {sideAmount}>0 AND ISNULL({oppositeAmount},0)=0
                     AND ABS({sideAmount}-@ExpectedLocalAmount)<=0.02",
                new
                {
                    JournalID = journalIds[0],
                    AccountType = accountType,
                    LinkedEntityType = isSalesInvoice ? "Customer" : "Supplier",
                    ControlAccountCode = isSalesInvoice ? "1103" : "2101",
                    PartyID = partyId,
                    ExpectedLocalAmount = Math.Round(totalAmount * (exchangeRate <= 0 ? 1 : exchangeRate), 2, MidpointRounding.AwayFromZero)
                }, transaction)).Distinct().ToList();
            if (candidates.Count != 1)
                throw new InvalidOperationException($"لم يمكن تحديد حساب الذمم بشكل وحيد من قيد الفاتورة {invoiceNumber}؛ لم يتم ترحيل السند.");
            return candidates[0];
        }

        private async Task<int?> PrepareReceiptSettlementAsync(
            IDbConnection connection, IDbTransaction transaction, ReceiptVoucherDto dto,
            int userId, int? currentReceiptId)
        {
            dto.InvoiceAllocations ??= new List<VoucherInvoiceAllocationDto>();
            dto.IsInvoiceSettlement = dto.InvoiceAllocations.Count > 0;
            if (!dto.IsInvoiceSettlement)
            {
                dto.InvoiceAllocations.Clear();
                return null;
            }

            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_SINV", p => p.CanView, "يلزم صلاحية عرض فواتير المبيعات لتسويتها.");
            if (dto.EntityType != 1 || !dto.EntityID.HasValue || dto.EntityID.Value <= 0)
                throw new InvalidOperationException("تسوية فاتورة المبيعات تتطلب تحديد عميل.");

            var accountId = await ValidateInvoiceAllocationsAsync(
                connection, transaction, true, dto.EntityID.Value, dto.CurrencyID,
                dto.ExchangeRate, dto.Amount, dto.InvoiceAllocations, currentReceiptId);
            dto.RevenueAccountID = accountId;
            return accountId;
        }

        private async Task<int?> PreparePaymentSettlementAsync(
            IDbConnection connection, IDbTransaction transaction, PaymentVoucherDto dto,
            int userId, int? currentPaymentId)
        {
            dto.InvoiceAllocations ??= new List<VoucherInvoiceAllocationDto>();
            dto.IsInvoiceSettlement = dto.InvoiceAllocations.Count > 0;
            if (!dto.IsInvoiceSettlement)
            {
                dto.InvoiceAllocations.Clear();
                return null;
            }

            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_PINV", p => p.CanView, "يلزم صلاحية عرض فواتير المشتريات لتسويتها.");
            if (dto.EntityType != 1 || !dto.EntityID.HasValue || dto.EntityID.Value <= 0)
                throw new InvalidOperationException("تسوية فاتورة المشتريات تتطلب تحديد مورد.");

            var accountId = await ValidateInvoiceAllocationsAsync(
                connection, transaction, false, dto.EntityID.Value, dto.CurrencyID,
                dto.ExchangeRate, dto.Amount, dto.InvoiceAllocations, currentPaymentId);
            dto.ExpenseAccountID = accountId;
            return accountId;
        }

        private static async Task EnsureBankAccountLocalCurrencyAsync(
            IDbConnection connection, IDbTransaction? transaction, int? bankAccountId, string context)
        {
            var id = bankAccountId.GetValueOrDefault();
            if (id <= 0) return;

            var bank = await connection.QueryFirstOrDefaultAsync<(int? BankAccountID, int? CurrencyID)>(
                @"SELECT BankAccountID, CurrencyID
                  FROM dbo.BankAccounts WITH (UPDLOCK,HOLDLOCK)
                  WHERE BankAccountID=@ID",
                new { ID = id }, transaction);
            if (!bank.BankAccountID.HasValue)
                throw new InvalidOperationException($"{context}: الحساب البنكي المحدد غير موجود.");
            await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                connection, transaction, bank.CurrencyID, 1m);
        }

        private static async Task EnsureCashBoxLocalCurrencyAsync(
            IDbConnection connection, IDbTransaction? transaction, int cashBoxId, string context)
        {
            if (cashBoxId <= 0) return;

            var cashBox = await connection.QueryFirstOrDefaultAsync<(int? CashBoxID, int? CurrencyID)>(
                @"SELECT CashBoxID, CurrencyID
                  FROM dbo.CashBoxes WITH (UPDLOCK,HOLDLOCK)
                  WHERE CashBoxID=@ID",
                new { ID = cashBoxId }, transaction);
            if (!cashBox.CashBoxID.HasValue)
                throw new InvalidOperationException($"{context}: الخزينة المحددة غير موجودة.");
            await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                connection, transaction, cashBox.CurrencyID, 1m);
        }

        private static async Task EnsureVoucherSourceLocalCurrencyAsync(
            IDbConnection connection, IDbTransaction? transaction, int cashBoxId,
            int? bankAccountId, string context)
        {
            if (cashBoxId < 0 || bankAccountId.GetValueOrDefault() < 0)
                throw new InvalidOperationException($"{context}: معرف مصدر النقدية غير صالح.");

            var hasCashBox = cashBoxId > 0;
            var hasBankAccount = bankAccountId.GetValueOrDefault() > 0;
            if (hasCashBox == hasBankAccount)
                throw new InvalidOperationException($"{context}: اختر مصدرًا واحدًا فقط — خزينة أو بنكًا، ولا تترك المصدر فارغًا.");

            if (hasBankAccount)
                await EnsureBankAccountLocalCurrencyAsync(connection, transaction, bankAccountId, context);
            else
                await EnsureCashBoxLocalCurrencyAsync(connection, transaction, cashBoxId, context);
        }

        private static async Task EnsureLinkedChequeVouchersLocalCurrencyAsync(
            IDbConnection connection, IDbTransaction? transaction, int? receiptId, int? paymentId)
        {
            if (receiptId.GetValueOrDefault() > 0)
            {
                var receipt = await connection.QueryFirstOrDefaultAsync<(
                    int? ReceiptID, int? CurrencyID, decimal ExchangeRate)>(
                    @"SELECT ReceiptID, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate
                      FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK)
                      WHERE ReceiptID=@ID",
                    new { ID = receiptId }, transaction);
                if (!receipt.ReceiptID.HasValue)
                    throw new InvalidOperationException("سند القبض المرتبط بالشيك غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, transaction, receipt.CurrencyID, receipt.ExchangeRate);
            }

            if (paymentId.GetValueOrDefault() > 0)
            {
                var payment = await connection.QueryFirstOrDefaultAsync<(
                    int? PaymentID, int? CurrencyID, decimal ExchangeRate)>(
                    @"SELECT PaymentID, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate
                      FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK)
                      WHERE PaymentID=@ID",
                    new { ID = paymentId }, transaction);
                if (!payment.PaymentID.HasValue)
                    throw new InvalidOperationException("سند الصرف المرتبط بالشيك غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, transaction, payment.CurrencyID, payment.ExchangeRate);
            }
        }

        private static async Task<(int AnyActivity, int PostedActivity)> GetCashBoxActivityCountsAsync(
            IDbConnection connection, IDbTransaction transaction, int cashBoxId)
        {
            var knownActivity = await connection.QueryFirstOrDefaultAsync<(int AnyActivity, int PostedActivity)>(
                @"SELECT
                    (SELECT COUNT(*) FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK) WHERE CashBoxID=@ID)
                    + (SELECT COUNT(*) FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK) WHERE CashBoxID=@ID)
                    + (SELECT COUNT(*) FROM dbo.CashTransfers WITH (UPDLOCK,HOLDLOCK) WHERE FromCashBoxID=@ID OR ToCashBoxID=@ID) AS AnyActivity,
                    (SELECT COUNT(*) FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK)
                     WHERE CashBoxID=@ID AND (ReceiptStatus=2 OR ISNULL(IsPosted,0)=1 OR JournalID IS NOT NULL))
                    + (SELECT COUNT(*) FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK)
                       WHERE CashBoxID=@ID AND (PaymentStatus=2 OR ISNULL(IsPosted,0)=1 OR JournalID IS NOT NULL))
                    + (SELECT COUNT(*) FROM dbo.CashTransfers WITH (UPDLOCK,HOLDLOCK)
                       WHERE (FromCashBoxID=@ID OR ToCashBoxID=@ID)
                         AND (Status=2 OR ISNULL(IsPosted,0)=1 OR JournalID IS NOT NULL)) AS PostedActivity",
                new { ID = cashBoxId }, transaction);

            var overheadActivity = await connection.QueryFirstOrDefaultAsync<(int AnyActivity, int PostedActivity)>(
                @"DECLARE @Count INT=0, @SQL NVARCHAR(MAX);
                  IF OBJECT_ID(N'dbo.OverheadExpenses',N'U') IS NOT NULL
                     AND COL_LENGTH(N'dbo.OverheadExpenses',N'CashBoxID') IS NOT NULL
                     AND COL_LENGTH(N'dbo.OverheadExpenses',N'IsPosted') IS NOT NULL
                  BEGIN
                      SET @SQL=N'SELECT @CountOut=COUNT(*) FROM dbo.OverheadExpenses WHERE CashBoxID=@CashBoxID AND ISNULL(IsPosted,0)=1';
                      IF COL_LENGTH(N'dbo.OverheadExpenses',N'ExpenseStatus') IS NOT NULL
                          SET @SQL=@SQL+N' AND ISNULL(ExpenseStatus,1)<>3';
                      EXEC sys.sp_executesql @SQL,
                           N'@CashBoxID INT,@CountOut INT OUTPUT',
                           @CashBoxID=@ID,@CountOut=@Count OUTPUT;
                  END;
                  SELECT @Count AS AnyActivity,@Count AS PostedActivity;",
                new { ID = cashBoxId }, transaction);

            return (knownActivity.AnyActivity + overheadActivity.AnyActivity,
                    knownActivity.PostedActivity + overheadActivity.PostedActivity);
        }

        private static async Task ValidateGenericVoucherCounterAccountAsync(
            IDbConnection connection, IDbTransaction transaction, int? accountId,
            int expectedAccountType, int? costCenterId, string voucherNumber, string accountLabel)
        {
            if (accountId.GetValueOrDefault() <= 0)
                throw new InvalidOperationException($"سند {voucherNumber}: اختر {accountLabel} صالحًا قبل الاعتماد.");

            var account = await connection.QueryFirstOrDefaultAsync<(
                int? AccountID, int AccountType, bool IsActive, bool IsDetailAccount, bool CostCenterRequired)>(
                @"SELECT AccountID, AccountType, ISNULL(IsActive,1) AS IsActive,
                         ISNULL(IsDetailAccount,0) AS IsDetailAccount,
                         ISNULL(CostCenterRequired,0) AS CostCenterRequired
                  FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK)
                  WHERE AccountID=@ID",
                new { ID = accountId }, transaction);
            if (!account.AccountID.HasValue || !account.IsActive
                || !account.IsDetailAccount || account.AccountType != expectedAccountType)
                throw new InvalidOperationException($"سند {voucherNumber}: {accountLabel} يجب أن يكون حسابًا تفصيليًا نشطًا من النوع المحاسبي المناسب.");

            if (costCenterId.GetValueOrDefault() < 0)
                throw new InvalidOperationException($"سند {voucherNumber}: معرف مركز التكلفة غير صالح.");

            if (costCenterId.GetValueOrDefault() > 0)
            {
                var costCenterIsActive = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT CONVERT(bit, CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.CostCenters WITH (UPDLOCK,HOLDLOCK)
                          WHERE CostCenterID=@ID AND ISNULL(IsActive,1)=1
                      ) THEN 1 ELSE 0 END)",
                    new { ID = costCenterId }, transaction);
                if (!costCenterIsActive)
                    throw new InvalidOperationException($"سند {voucherNumber}: مركز التكلفة المحدد غير نشط أو غير موجود.");
            }
            else if (account.CostCenterRequired)
            {
                throw new InvalidOperationException($"سند {voucherNumber}: الحساب {accountLabel} يتطلب مركز تكلفة.");
            }
        }

        private async Task<int> ResolveVoucherSourceAccountIdAsync(
            IDbConnection connection, IDbTransaction transaction, int cashBoxId,
            int? bankAccountId, string voucherNumber)
        {
            await EnsureVoucherSourceLocalCurrencyAsync(
                connection, transaction, cashBoxId, bankAccountId, $"سند {voucherNumber}");
            int? accountId;
            var selectedBankAccountId = bankAccountId.GetValueOrDefault();
            if (selectedBankAccountId > 0)
            {
                accountId = await connection.ExecuteScalarAsync<int?>(
                    @"SELECT TOP 1 coa.AccountID
                      FROM dbo.BankAccounts ba WITH (UPDLOCK,HOLDLOCK)
                      INNER JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK) ON coa.AccountID=ba.AccountID
                      WHERE ba.BankAccountID=@ID AND ISNULL(ba.IsActive,1)=1 AND coa.IsActive=1
                        AND coa.IsDetailAccount=1 AND coa.AccountType=1",
                    new { ID = selectedBankAccountId }, transaction);
                if (accountId is not int bankAccountLedgerId || bankAccountLedgerId <= 0)
                    throw new InvalidOperationException($"سند {voucherNumber}: الحساب البنكي المحدد غير نشط أو غير مرتبط بحساب أصل تفصيلي صالح.");
                return bankAccountLedgerId;
            }

            if (cashBoxId <= 0)
                throw new InvalidOperationException($"سند {voucherNumber}: اختر خزينة أو حساباً بنكياً صالحاً كمصدر.");

            accountId = await connection.ExecuteScalarAsync<int?>(
                @"SELECT TOP 1 coa.AccountID
                  FROM dbo.CashBoxes cb WITH (UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK) ON coa.AccountID=cb.AccountID
                  WHERE cb.CashBoxID=@ID AND ISNULL(cb.IsActive,1)=1 AND coa.IsActive=1
                    AND coa.IsDetailAccount=1 AND coa.AccountType=1",
                new { ID = cashBoxId }, transaction);
            if (accountId is not int cashBoxLedgerId || cashBoxLedgerId <= 0)
                throw new InvalidOperationException($"سند {voucherNumber}: الخزينة المحددة غير نشطة أو غير مرتبطة بحساب أصل تفصيلي صالح.");
            return cashBoxLedgerId;
        }

        private async Task VerifyVoucherJournalAsync(
            IDbConnection connection, IDbTransaction transaction, int journalId, int voucherId,
            string voucherNumber, DateTime voucherDate, decimal amountLocal,
            int sourceAccountId, bool isReceipt, int? expectedCounterAccountId,
            string? expectedCounterEntityType, int? expectedCounterEntityId)
        {
            if (journalId <= 0 || voucherId <= 0 || sourceAccountId <= 0 || amountLocal <= 0m
                || string.IsNullOrWhiteSpace(voucherNumber))
                throw new InvalidOperationException("بيانات ربط القيد أو مصدر المبلغ غير صالحة؛ أُلغي الاعتماد.");

            var counterAccountId = expectedCounterAccountId.GetValueOrDefault();
            if (counterAccountId > 0 && counterAccountId == sourceAccountId)
                throw new InvalidOperationException($"قيد {voucherNumber} يربط مصدر النقدية بالحساب المقابل نفسه؛ أُلغي الاعتماد.");
            if ((!string.IsNullOrWhiteSpace(expectedCounterEntityType) || expectedCounterEntityId.HasValue)
                && (counterAccountId <= 0 || string.IsNullOrWhiteSpace(expectedCounterEntityType)
                    || expectedCounterEntityId.GetValueOrDefault() <= 0))
                throw new InvalidOperationException($"قيد {voucherNumber} لا يحمل تعريف جهة صالحًا لحساب الذمم؛ أُلغي الاعتماد.");

            var journal = await connection.QueryFirstOrDefaultAsync<VoucherJournalValidationState>(
                @"SELECT je.JournalID, je.JournalStatus, je.JournalDate,
                         CONVERT(NVARCHAR(50),je.SourceDocID) AS SourceDocID,
                         je.SourceDocNumber, je.SourceDocType,
                         je.FiscalYearID, fy.YearStatus, fy.StartDate AS YearStart, fy.EndDate AS YearEnd,
                         je.PeriodID, ap.FiscalYearID AS PeriodFiscalYearID, ap.PeriodStatus,
                         ap.StartDate AS PeriodStart, ap.EndDate AS PeriodEnd,
                         ISNULL(je.TotalDebit,0) AS HeaderDebit,
                         ISNULL(je.TotalCredit,0) AS HeaderCredit,
                         ISNULL(SUM(CASE WHEN jd.JournalID IS NOT NULL
                                            AND (ISNULL(jd.DebitAmount,0)>0 OR ISNULL(jd.CreditAmount,0)>0)
                                         THEN 1 ELSE 0 END),0) AS DetailCount,
                         ISNULL(SUM(CASE WHEN jd.JournalID IS NOT NULL
                                            AND (jd.DebitAmount IS NULL OR jd.CreditAmount IS NULL
                                                 OR jd.DebitAmount<0 OR jd.CreditAmount<0
                                                 OR (jd.DebitAmount>0 AND jd.CreditAmount>0)
                                                 OR ((jd.DebitAmount>0 OR jd.CreditAmount>0)
                                                     AND (jd.AccountID IS NULL OR jd.AccountID<=0
                                                          OR coa.AccountID IS NULL
                                                          OR ISNULL(coa.IsActive,1)<>1
                                                          OR (ISNULL(coa.IsDetailAccount,0)<>1
                                                              AND NOT (
                                                                  UPPER(ISNULL(@ExpectedCounterEntityType,N'')) IN (N'CUSTOMER',N'SUPPLIER')
                                                                  AND jd.AccountID=@CounterAccountID
                                                                  AND ISNULL(coa.AccountType,0)=CASE UPPER(ISNULL(@ExpectedCounterEntityType,N''))
                                                                      WHEN N'CUSTOMER' THEN 1 WHEN N'SUPPLIER' THEN 2 ELSE -1 END
                                                                  AND ISNULL(coa.IsSystemAccount,0)=1
                                                                  AND ((UPPER(@ExpectedCounterEntityType)=N'CUSTOMER' AND coa.AccountCode=N'1103')
                                                                    OR (UPPER(@ExpectedCounterEntityType)=N'SUPPLIER' AND coa.AccountCode=N'2101'))
                                                                  AND UPPER(LTRIM(RTRIM(ISNULL(coa.LinkedEntityType,N''))))=UPPER(@ExpectedCounterEntityType)
                                                                  AND (coa.LinkedEntityID IS NULL OR coa.LinkedEntityID=@ExpectedCounterEntityID)
                                                                  AND UPPER(LTRIM(RTRIM(ISNULL(jd.EntityType,N''))))=UPPER(@ExpectedCounterEntityType)
                                                                  AND jd.EntityID=@ExpectedCounterEntityID
                                                              ))
                                                          OR (ISNULL(coa.CostCenterRequired,0)=1 AND ISNULL(jd.CostCenterID,0)<=0)
                                                          OR jd.CostCenterID<0
                                                          OR (jd.CostCenterID>0
                                                              AND (cc.CostCenterID IS NULL OR ISNULL(cc.IsActive,1)<>1)))))
                                         THEN 1 ELSE 0 END),0) AS InvalidDetailCount,
                         ISNULL(SUM(CASE WHEN @ExpectedCounterEntityType IS NOT NULL
                                               AND jd.AccountID=@CounterAccountID
                                               AND (UPPER(LTRIM(RTRIM(ISNULL(coa.LinkedEntityType,N''))))<>UPPER(@ExpectedCounterEntityType)
                                                    OR (coa.LinkedEntityID IS NOT NULL AND coa.LinkedEntityID<>@ExpectedCounterEntityID)
                                                    OR ISNULL(coa.AccountType,0)<>CASE UPPER(ISNULL(@ExpectedCounterEntityType,N''))
                                                        WHEN N'CUSTOMER' THEN 1 WHEN N'SUPPLIER' THEN 2 ELSE -1 END
                                                    OR (ISNULL(coa.IsDetailAccount,0)<>1 AND
                                                        (ISNULL(coa.IsSystemAccount,0)<>1
                                                         OR (UPPER(@ExpectedCounterEntityType)=N'CUSTOMER' AND coa.AccountCode<>N'1103')
                                                         OR (UPPER(@ExpectedCounterEntityType)=N'SUPPLIER' AND coa.AccountCode<>N'2101')))
                                                    OR UPPER(LTRIM(RTRIM(ISNULL(jd.EntityType,N''))))<>UPPER(@ExpectedCounterEntityType)
                                                    OR ISNULL(jd.EntityID,0)<>ISNULL(@ExpectedCounterEntityID,0))
                                          THEN 1 ELSE 0 END),0) AS InvalidCounterEntityCount,
                         ISNULL(SUM(jd.DebitAmount),0) AS DetailDebit,
                         ISNULL(SUM(jd.CreditAmount),0) AS DetailCredit,
                         ISNULL(SUM(CASE WHEN jd.AccountID=@SourceAccountID THEN jd.DebitAmount ELSE 0 END),0) AS SourceDebit,
                         ISNULL(SUM(CASE WHEN jd.AccountID=@SourceAccountID THEN jd.CreditAmount ELSE 0 END),0) AS SourceCredit,
                         ISNULL(SUM(CASE WHEN jd.AccountID=@CounterAccountID THEN jd.DebitAmount ELSE 0 END),0) AS CounterDebit,
                         ISNULL(SUM(CASE WHEN jd.AccountID=@CounterAccountID THEN jd.CreditAmount ELSE 0 END),0) AS CounterCredit
                  FROM dbo.JournalEntries je WITH (UPDLOCK,HOLDLOCK)
                  LEFT JOIN dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK) ON fy.FiscalYearID=je.FiscalYearID
                  LEFT JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK) ON ap.PeriodID=je.PeriodID
                  LEFT JOIN dbo.JournalEntryDetails jd WITH (UPDLOCK,HOLDLOCK) ON jd.JournalID=je.JournalID
                  LEFT JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK) ON coa.AccountID=jd.AccountID
                  LEFT JOIN dbo.CostCenters cc WITH (UPDLOCK,HOLDLOCK) ON cc.CostCenterID=jd.CostCenterID
                  WHERE je.JournalID=@JournalID
                  GROUP BY je.JournalID,je.JournalStatus,je.JournalDate,je.SourceDocID,je.SourceDocNumber,je.SourceDocType,
                           je.FiscalYearID,fy.YearStatus,fy.StartDate,fy.EndDate,je.PeriodID,
                           ap.FiscalYearID,ap.PeriodStatus,ap.StartDate,ap.EndDate,je.TotalDebit,je.TotalCredit",
                new
                {
                    JournalID = journalId,
                    SourceAccountID = sourceAccountId,
                    CounterAccountID = counterAccountId > 0 ? counterAccountId : (int?)null,
                    ExpectedCounterEntityType = string.IsNullOrWhiteSpace(expectedCounterEntityType) ? null : expectedCounterEntityType.Trim(),
                    ExpectedCounterEntityID = expectedCounterEntityId
                }, transaction);

            if (journal is null || journal.JournalStatus != 2)
                throw new InvalidOperationException($"لم ينتج ترحيل {voucherNumber} قيداً مرحّلاً؛ أُلغي الاعتماد.");
            if (journal.InvalidCounterEntityCount > 0)
                throw new InvalidOperationException($"قيد {voucherNumber} لا يربط حساب الذمم بالعميل/المورد الصحيح؛ أُلغي الاعتماد.");
            if (journal.JournalDate is not DateTime journalDate || journalDate.Date != voucherDate.Date)
                throw new InvalidOperationException($"تاريخ قيد {voucherNumber} لا يطابق تاريخ السند؛ أُلغي الاعتماد.");
            var expectedSourceDocType = isReceipt ? "ReceiptVoucher" : "PaymentVoucher";
            if (!string.Equals(journal.SourceDocType?.Trim(), expectedSourceDocType, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(journal.SourceDocID?.Trim(), voucherId.ToString(), StringComparison.Ordinal)
                || !string.Equals(journal.SourceDocNumber?.Trim(), voucherNumber.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"نوع أو رقم أو معرف القيد المرتبط لا يطابق سند {voucherNumber}؛ أُلغي الاعتماد.");

            var linkedJournalCount = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                  WHERE CONVERT(NVARCHAR(50),SourceDocID)=@SourceDocID
                    AND SourceDocNumber=@SourceDocNumber AND SourceDocType=@SourceDocType",
                new
                {
                    SourceDocID = voucherId.ToString(),
                    SourceDocNumber = voucherNumber,
                    SourceDocType = expectedSourceDocType
                }, transaction);
            if (linkedJournalCount != 1)
                throw new InvalidOperationException($"يجب أن يكون لسند {voucherNumber} قيد مصدر واحد فقط؛ أُلغي الاعتماد.");

            if (journal.FiscalYearID is not int fiscalYearId || journal.YearStatus != 1
                || journal.YearStart is not DateTime yearStart || journal.YearEnd is not DateTime yearEnd
                || journal.PeriodID is not > 0 || journal.PeriodFiscalYearID != fiscalYearId
                || journal.PeriodStatus != 1 || journal.PeriodStart is not DateTime periodStart
                || journal.PeriodEnd is not DateTime periodEnd)
                throw new InvalidOperationException($"قيد {voucherNumber} غير مرتبط بسنة وفترة محاسبيتين مفتوحتين؛ أُلغي الاعتماد.");

            if (journalDate.Date < yearStart.Date || journalDate.Date > yearEnd.Date
                || journalDate.Date < periodStart.Date || journalDate.Date > periodEnd.Date)
                throw new InvalidOperationException($"تاريخ قيد {voucherNumber} خارج نطاق السنة أو الفترة المفتوحة؛ أُلغي الاعتماد.");

            var expectedAmountLocal = Math.Round(amountLocal, 2, MidpointRounding.AwayFromZero);
            if (journal.DetailCount < 2 || journal.InvalidDetailCount > 0
                || journal.HeaderDebit <= 0m || journal.HeaderCredit <= 0m
                || Math.Abs(journal.HeaderDebit - journal.HeaderCredit) > 0.01m
                || Math.Abs(journal.DetailDebit - journal.DetailCredit) > 0.01m
                || Math.Abs(journal.HeaderDebit - journal.DetailDebit) > 0.01m
                || Math.Abs(journal.HeaderCredit - journal.DetailCredit) > 0.01m
                || Math.Abs(journal.HeaderDebit - expectedAmountLocal) > 0.02m)
                throw new InvalidOperationException($"قيد {voucherNumber} غير مكتمل أو يحتوي تفاصيل غير صالحة أو غير متوازن أو لا يطابق المبلغ المحلي للسند؛ أُلغي الاعتماد.");

            var expectedSourceAmount = isReceipt ? journal.SourceDebit : journal.SourceCredit;
            var oppositeSourceAmount = isReceipt ? journal.SourceCredit : journal.SourceDebit;
            if (Math.Abs(expectedSourceAmount - expectedAmountLocal) > 0.02m
                || Math.Abs(oppositeSourceAmount) > 0.02m)
                throw new InvalidOperationException($"قيد {voucherNumber} لا يستخدم حساب الخزينة/البنك المحدد كمصدر للمبلغ؛ أُلغي الاعتماد.");

            if (counterAccountId > 0)
            {
                var expectedCounterAmount = isReceipt ? journal.CounterCredit : journal.CounterDebit;
                var oppositeCounterAmount = isReceipt ? journal.CounterDebit : journal.CounterCredit;
                if (Math.Abs(expectedCounterAmount - expectedAmountLocal) > 0.02m
                    || Math.Abs(oppositeCounterAmount) > 0.02m)
                    throw new InvalidOperationException($"قيد {voucherNumber} لا يطابق الحساب المقابل المحدد بكامل المبلغ؛ أُلغي الاعتماد.");
            }
        }

        private async Task ApplyJournalAccountBalancesAsync(
            IDbConnection connection, IDbTransaction transaction, int journalId)
        {
            var details = (await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit)>(
                "SELECT AccountID, DebitAmount, CreditAmount FROM dbo.JournalEntryDetails WHERE JournalID=@JournalID",
                new { JournalID = journalId }, transaction)).ToList();
            if (details.Count < 2)
                throw new InvalidOperationException("تعذر تحديث أرصدة الحسابات لعدم وجود سطور قيد مكتملة؛ أُلغي الاعتماد.");

            foreach (var detail in details)
            {
                var delta = detail.Debit - detail.Credit;
                if (delta == 0m) continue;

                var accountUpdated = await connection.ExecuteAsync(
                    "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@AccountID",
                    new { Delta = delta, AccountID = detail.AccountID }, transaction);
                if (accountUpdated != 1)
                    throw new InvalidOperationException("تعذر تحديث رصيد حساب القيد؛ أُلغي اعتماد السند.");

                int? currentAccountId = detail.AccountID;
                for (var depth = 0; depth < 10; depth++)
                {
                    var parentId = await connection.ExecuteScalarAsync<int?>(
                        "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID",
                        new { ID = currentAccountId }, transaction);
                    if (parentId is not int parentAccountId || parentAccountId <= 0) break;

                    var parentUpdated = await connection.ExecuteAsync(
                        "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                        new { Delta = delta, ID = parentAccountId }, transaction);
                    if (parentUpdated != 1)
                        throw new InvalidOperationException("تعذر تحديث رصيد الحساب الأب؛ أُلغي اعتماد السند.");
                    currentAccountId = parentAccountId;
                }
            }
        }

        private async Task ApplyReceiptInvoicePaidAmountsAsync(
            IDbConnection connection, IDbTransaction transaction,
            IReadOnlyCollection<VoucherInvoiceAllocationDto> allocations, int userId)
        {
            foreach (var allocation in allocations.OrderBy(x => x.InvoiceID))
            {
                var invoice = await connection.QueryFirstOrDefaultAsync<(decimal TotalAmount, decimal PaidAmount)>(
                    @"SELECT ISNULL(TotalAmount,0) AS TotalAmount,ISNULL(PaidAmount,0) AS PaidAmount
                      FROM dbo.SalesInvoices WITH (UPDLOCK,HOLDLOCK) WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID }, transaction);
                var newPaidAmount = invoice.PaidAmount + allocation.AppliedAmount;
                if (invoice.TotalAmount <= 0 || newPaidAmount > invoice.TotalAmount)
                    throw new InvalidOperationException($"تغير المتبقي في فاتورة المبيعات {allocation.InvoiceNumber}؛ أُلغي اعتماد السند.");

                var isFullyPaid = newPaidAmount >= invoice.TotalAmount;
                var paymentStatus = isFullyPaid ? 3 : 2;
                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices
                      SET PaidAmount=@PaidAmount,PaymentStatus=@PaymentStatus,
                          InvoiceStatus=CASE WHEN @IsFullyPaid=1 THEN 4 ELSE InvoiceStatus END,
                          ModifiedBy=@UserID,ModifiedDate=GETDATE()
                      WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID, PaidAmount = newPaidAmount, PaymentStatus = paymentStatus, IsFullyPaid = isFullyPaid, UserID = userId }, transaction);
                if (updated != 1) throw new InvalidOperationException("تعذر تحديث مدفوعات فاتورة المبيعات؛ أُلغي اعتماد السند.");
            }
        }

        private async Task ApplyPaymentInvoicePaidAmountsAsync(
            IDbConnection connection, IDbTransaction transaction,
            IReadOnlyCollection<VoucherInvoiceAllocationDto> allocations, int userId)
        {
            foreach (var allocation in allocations.OrderBy(x => x.InvoiceID))
            {
                var invoice = await connection.QueryFirstOrDefaultAsync<(decimal TotalAmount, decimal PaidAmount, int InvoiceStatus)>(
                    @"SELECT ISNULL(TotalAmount,0) AS TotalAmount,ISNULL(PaidAmount,0) AS PaidAmount,InvoiceStatus
                      FROM dbo.PurchaseInvoices WITH (UPDLOCK,HOLDLOCK) WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID }, transaction);
                var newPaidAmount = invoice.PaidAmount + allocation.AppliedAmount;
                if (invoice.TotalAmount <= 0 || newPaidAmount > invoice.TotalAmount)
                    throw new InvalidOperationException($"تغير المتبقي في فاتورة المشتريات {allocation.InvoiceNumber}؛ أُلغي اعتماد السند.");

                var isFullyPaid = newPaidAmount >= invoice.TotalAmount;
                var paymentStatus = isFullyPaid ? 3 : 2;
                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseInvoices
                      SET PaidAmount=@PaidAmount,PaymentStatus=@PaymentStatus,
                          InvoiceStatus=CASE WHEN @IsFullyPaid=1 THEN 5 WHEN InvoiceStatus=6 THEN 6 ELSE 4 END,
                          ModifiedBy=@UserID,ModifiedDate=GETDATE()
                      WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID, PaidAmount = newPaidAmount, PaymentStatus = paymentStatus, IsFullyPaid = isFullyPaid, UserID = userId }, transaction);
                if (updated != 1) throw new InvalidOperationException("تعذر تحديث مدفوعات فاتورة المشتريات؛ أُلغي اعتماد السند.");
            }
        }

        private async Task ReplaceReceiptInvoiceAllocationsAsync(
            IDbConnection connection, IDbTransaction transaction, int receiptId,
            IReadOnlyCollection<VoucherInvoiceAllocationDto> allocations)
        {
            await connection.ExecuteAsync("DELETE FROM dbo.ReceiptAllocations WHERE ReceiptID=@ID", new { ID = receiptId }, transaction);
            foreach (var allocation in allocations)
                await connection.ExecuteAsync(
                    @"INSERT INTO dbo.ReceiptAllocations(ReceiptID,InvoiceID,AllocatedAmount,Notes,CreatedDate)
                      VALUES(@ReceiptID,@InvoiceID,@AppliedAmount,N'',GETDATE())",
                    new { ReceiptID = receiptId, InvoiceID = allocation.InvoiceID, allocation.AppliedAmount }, transaction);
        }

        private async Task ReplacePaymentInvoiceAllocationsAsync(
            IDbConnection connection, IDbTransaction transaction, int paymentId,
            IReadOnlyCollection<VoucherInvoiceAllocationDto> allocations)
        {
            await connection.ExecuteAsync("DELETE FROM dbo.PaymentAllocations WHERE PaymentID=@ID", new { ID = paymentId }, transaction);
            foreach (var allocation in allocations)
                await connection.ExecuteAsync(
                    @"INSERT INTO dbo.PaymentAllocations(PaymentID,InvoiceID,AllocatedAmount,Notes,CreatedDate)
                      VALUES(@PaymentID,@InvoiceID,@AppliedAmount,N'',GETDATE())",
                    new { PaymentID = paymentId, InvoiceID = allocation.InvoiceID, allocation.AppliedAmount }, transaction);
        }

        private async Task<List<VoucherInvoiceAllocationDto>> GetReceiptInvoiceAllocationsCoreAsync(IDbConnection connection, IDbTransaction? transaction, int receiptId) =>
            (await connection.QueryAsync<VoucherInvoiceAllocationDto>(
                @"SELECT a.InvoiceID, si.InvoiceNumber, si.InvoiceDate,
                         si.CustomerID AS PartyID, c.CustomerNameAr AS PartyName,
                         si.TotalAmount, ISNULL(si.PaidAmount,0) AS PaidAmount,
                         CASE WHEN ISNULL(si.TotalAmount,0)-ISNULL(si.PaidAmount,0)>0 THEN ISNULL(si.TotalAmount,0)-ISNULL(si.PaidAmount,0) ELSE 0 END AS RemainingAmount,
                         a.AllocatedAmount AS AppliedAmount, si.CurrencyID, ISNULL(si.ExchangeRate,1) AS ExchangeRate, cur.CurrencyCode
                  FROM dbo.ReceiptAllocations a
                  INNER JOIN dbo.SalesInvoices si ON si.InvoiceID=a.InvoiceID
                  LEFT JOIN dbo.Customers c ON c.CustomerID=si.CustomerID
                  LEFT JOIN dbo.Currencies cur ON cur.CurrencyID=si.CurrencyID
                  WHERE a.ReceiptID=@ID ORDER BY si.InvoiceDate,si.InvoiceID",
                new { ID = receiptId }, transaction)).ToList();

        private async Task<List<VoucherInvoiceAllocationDto>> GetPaymentInvoiceAllocationsCoreAsync(IDbConnection connection, IDbTransaction? transaction, int paymentId) =>
            (await connection.QueryAsync<VoucherInvoiceAllocationDto>(
                @"SELECT a.InvoiceID, pi.InvoiceNumber, pi.InvoiceDate,
                         pi.SupplierID AS PartyID, s.SupplierNameAr AS PartyName,
                         pi.TotalAmount, ISNULL(pi.PaidAmount,0) AS PaidAmount,
                         CASE WHEN ISNULL(pi.TotalAmount,0)-ISNULL(pi.PaidAmount,0)>0 THEN ISNULL(pi.TotalAmount,0)-ISNULL(pi.PaidAmount,0) ELSE 0 END AS RemainingAmount,
                         a.AllocatedAmount AS AppliedAmount, pi.CurrencyID, ISNULL(pi.ExchangeRate,1) AS ExchangeRate, cur.CurrencyCode
                  FROM dbo.PaymentAllocations a
                  INNER JOIN dbo.PurchaseInvoices pi ON pi.InvoiceID=a.InvoiceID
                  LEFT JOIN dbo.Suppliers s ON s.SupplierID=pi.SupplierID
                  LEFT JOIN dbo.Currencies cur ON cur.CurrencyID=pi.CurrencyID
                  WHERE a.PaymentID=@ID ORDER BY pi.InvoiceDate,pi.InvoiceID",
                new { ID = paymentId }, transaction)).ToList();

        private sealed class VoucherJournalValidationState
        {
            public int JournalID { get; set; }
            public int? JournalStatus { get; set; }
            public DateTime? JournalDate { get; set; }
            public string? SourceDocID { get; set; }
            public string? SourceDocNumber { get; set; }
            public string? SourceDocType { get; set; }
            public int? FiscalYearID { get; set; }
            public int? YearStatus { get; set; }
            public DateTime? YearStart { get; set; }
            public DateTime? YearEnd { get; set; }
            public int? PeriodID { get; set; }
            public int? PeriodFiscalYearID { get; set; }
            public int? PeriodStatus { get; set; }
            public DateTime? PeriodStart { get; set; }
            public DateTime? PeriodEnd { get; set; }
            public decimal HeaderDebit { get; set; }
            public decimal HeaderCredit { get; set; }
            public int DetailCount { get; set; }
            public int InvalidDetailCount { get; set; }
            public int InvalidCounterEntityCount { get; set; }
            public decimal DetailDebit { get; set; }
            public decimal DetailCredit { get; set; }
            public decimal SourceDebit { get; set; }
            public decimal SourceCredit { get; set; }
            public decimal CounterDebit { get; set; }
            public decimal CounterCredit { get; set; }
        }

        private sealed class SettlementInvoiceState
        {
            public int InvoiceID { get; set; }
            public string? InvoiceNumber { get; set; }
            public DateTime InvoiceDate { get; set; }
            public int PartyID { get; set; }
            public string? PartyName { get; set; }
            public int InvoiceStatus { get; set; }
            public int PaymentStatus { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal PaidAmount { get; set; }
            public int? CurrencyID { get; set; }
            public decimal ExchangeRate { get; set; }
        }

        private static bool HasNonInitialChequeApprovalState(ChequeDto cheque) =>
            cheque.ChequeStatus != 1 || cheque.CollectionDate.HasValue || cheque.BounceDate.HasValue ||
            !string.IsNullOrWhiteSpace(cheque.BounceReason);

        private static bool HasChequeApprovalStateChanged(ChequeDto current, ChequeDto proposed) =>
            current.ChequeStatus != proposed.ChequeStatus ||
            current.CollectionDate != proposed.CollectionDate ||
            current.BounceDate != proposed.BounceDate ||
            !string.Equals(current.BounceReason, proposed.BounceReason, StringComparison.Ordinal);

        // ==========================================
        // لوحة تحكم الخزينة - على الجداول الحقيقية
        // ==========================================
        public async Task<TreasuryDashboardDto> GetDashboardAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_TREASURY", p => p.CanView, "ليس لديك صلاحية عرض لوحة الخزينة.");

            using var connection = CreateConnection();
            var dto = new TreasuryDashboardDto();
            try
            {
                // استعلام مباشر شامل بدل vw_TreasurySummary المعطوب
                var sqlBoxesDirect = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, cb.CashBoxNameEn, 
                                    ISNULL(cb.CashBoxType,1) AS CashBoxType,
                                    CASE ISNULL(cb.CashBoxType,1) WHEN 1 THEN N'رئيسية' WHEN 2 THEN N'فرعية' WHEN 3 THEN N'بنك' WHEN 4 THEN N'نقطة بيع' ELSE N'أخرى' END AS CashBoxTypeName,
                                    cb.AccountID, coa.AccountCode, coa.AccountNameAr, 
                                    ISNULL(cb.OpeningBalance,0) AS OpeningBalance, 
                                    cb.CurrentBalance, 
                                    cb.CurrentBalance AS CurrentBalanceCalc,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE CashBoxID=cb.CashBoxID AND ReceiptStatus=2),0) AS TotalReceipts,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE CashBoxID=cb.CashBoxID AND PaymentStatus=2),0) 
                                    + ISNULL((SELECT SUM(Amount) FROM dbo.OverheadExpenses WHERE CashBoxID=cb.CashBoxID AND ISNULL(ExpenseStatus,1)!=3 AND ISNULL(IsPosted,0)=1),0) AS TotalPayments,
                                    ISNULL((SELECT SUM(ISNULL(NULLIF(AmountLocal,0),Amount)) FROM dbo.CashTransfers WHERE ToCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalIn,
                                    ISNULL((SELECT SUM(ISNULL(NULLIF(AmountLocal,0),Amount)) FROM dbo.CashTransfers WHERE FromCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalOut,
                                    cb.CurrencyID, cur.CurrencyCode, 
                                    CAST(1 AS BIT) AS IsActive, 
                                    ISNULL(cb.IsDefault,0) AS IsDefault, cb.CreatedDate
                             FROM dbo.CashBoxes cb
                             LEFT JOIN dbo.ChartOfAccounts coa ON cb.AccountID=coa.AccountID
                             LEFT JOIN dbo.Currencies cur ON cb.CurrencyID=cur.CurrencyID
                             ORDER BY cb.CashBoxNameAr";
                try
                {
                    var boxes = await connection.QueryAsync<CashBoxListDto>(sqlBoxesDirect);
                    dto.CashBoxes = boxes.ToList();
                }
                catch (Exception ex)
                {
                    // Keep the existing compatibility fallback, but make the primary-query failure observable.
                    _logger.LogWarning(ex, "The direct cash-box dashboard query failed; attempting the treasury view fallback.");
                    try
                    {
                        var viewBoxes = await connection.QueryAsync<CashBoxListDto>("SELECT * FROM dbo.vw_TreasurySummary ORDER BY CashBoxNameAr");
                        var vList = viewBoxes.ToList();
                        foreach (var cb in vList)
                        {
                            if (cb.TotalReceipts==0 && cb.TotalPayments==0) cb.CurrentBalanceCalc = cb.CurrentBalance;
                        }
                        dto.CashBoxes = vList;
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogError(fallbackEx, "Both cash-box dashboard queries failed; returning an empty cash-box list.");
                        dto.CashBoxes = new List<CashBoxListDto>();
                    }
                }

                dto.CashBoxesCount = dto.CashBoxes.Count;
                dto.TotalCashBalance = dto.CashBoxes.Sum(b => b.CurrentBalance);

                var today = DateTime.Today;
                try
                {
                    dto.TodayReceipts = await connection.ExecuteScalarAsync<decimal>(
                        "SELECT ISNULL(SUM(Amount),0) FROM dbo.ReceiptVouchers WHERE ReceiptDate=@D AND ReceiptStatus=2", new { D = today });
                    dto.TodayPayments = await connection.ExecuteScalarAsync<decimal>(
                        "SELECT ISNULL(SUM(Amount),0) FROM dbo.PaymentVouchers WHERE PaymentDate=@D AND PaymentStatus=2", new { D = today });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load today's treasury receipt/payment totals.");
                }
                dto.TodayNet = dto.TodayReceipts - dto.TodayPayments;

                try
                {
                    dto.PendingVouchers = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM (SELECT ReceiptStatus AS S FROM dbo.ReceiptVouchers WHERE ReceiptStatus=1 UNION ALL SELECT PaymentStatus FROM dbo.PaymentVouchers WHERE PaymentStatus=1) t");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load the pending treasury-voucher count.");
                }

                // آخر سندات قبض - على الجدول الحقيقي
                try
                {
                    var recentsR = await connection.QueryAsync<ReceiptVoucherListDto>(
                        @"SELECT TOP 5 rv.ReceiptID, rv.ReceiptNumber, rv.ReceiptDate, rv.ReceivedFrom, rv.EntityType, rv.EntityID,
                                 CASE WHEN rv.EntityType=1 THEN c.CustomerNameAr WHEN rv.EntityType=2 THEN s.SupplierNameAr ELSE rv.ReceivedFrom END AS EntityName,
                                 c.CustomerNameAr, c.CustomerCode, s.SupplierNameAr,
                                 rv.CashBoxID, cb.CashBoxNameAr, cb.CashBoxCode, rv.BankAccountID, ba.BankName AS BankAccountName,
                                 rv.Amount, ISNULL(rv.AmountLocal, rv.Amount) AS AmountLocal, rv.CurrencyID, cur.CurrencyCode,
                                 rv.PaymentMethod, CASE rv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' ELSE N'أخرى' END AS PaymentMethodName,
                                 rv.Description, rv.ReceiptStatus, CASE rv.ReceiptStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' ELSE N'ملغي' END AS StatusName,
                                 ISNULL(rv.IsPosted,0) AS IsPosted, rv.JournalID, je.JournalNumber, rv.CreatedDate, emp.FullNameAr AS CreatedByName
                          FROM dbo.ReceiptVouchers rv
                          LEFT JOIN dbo.CashBoxes cb ON rv.CashBoxID=cb.CashBoxID
                          LEFT JOIN dbo.Customers c ON rv.EntityType=1 AND rv.EntityID=c.CustomerID
                          LEFT JOIN dbo.Suppliers s ON rv.EntityType=2 AND rv.EntityID=s.SupplierID
                          LEFT JOIN dbo.BankAccounts ba ON rv.BankAccountID=ba.BankAccountID
                          LEFT JOIN dbo.Currencies cur ON rv.CurrencyID=cur.CurrencyID
                          LEFT JOIN dbo.JournalEntries je ON rv.JournalID=je.JournalID
                          LEFT JOIN dbo.SystemUsers su ON rv.CreatedBy=su.UserID
                          LEFT JOIN dbo.Employees emp ON su.EmployeeID=emp.EmployeeID
                          ORDER BY rv.ReceiptDate DESC, rv.ReceiptID DESC");
                    dto.RecentReceipts = recentsR.ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load recent treasury receipts for the dashboard.");
                }

                try
                {
                    var recentsP = await connection.QueryAsync<PaymentVoucherListDto>(
                        @"SELECT TOP 5 pv.PaymentID, pv.PaymentNumber, pv.PaymentDate, pv.PaidTo, pv.EntityType, pv.EntityID,
                                 CASE WHEN pv.EntityType=1 THEN s.SupplierNameAr WHEN pv.EntityType=2 THEN c.CustomerNameAr ELSE pv.PaidTo END AS EntityName,
                                 s.SupplierNameAr, c.CustomerNameAr,
                                 pv.CashBoxID, cb.CashBoxNameAr, pv.BankAccountID, pv.Amount, ISNULL(pv.AmountLocal, pv.Amount) AS AmountLocal,
                                 pv.CurrencyID, cur.CurrencyCode, pv.PaymentMethod,
                                 CASE pv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' ELSE N'أخرى' END AS PaymentMethodName,
                                 pv.Description, pv.PaymentStatus, CASE pv.PaymentStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' ELSE N'ملغي' END AS StatusName,
                                 ISNULL(pv.IsPosted,0) AS IsPosted, pv.JournalID, pv.CreatedDate
                          FROM dbo.PaymentVouchers pv
                          LEFT JOIN dbo.CashBoxes cb ON pv.CashBoxID=cb.CashBoxID
                          LEFT JOIN dbo.Suppliers s ON pv.EntityType=1 AND pv.EntityID=s.SupplierID
                          LEFT JOIN dbo.Customers c ON pv.EntityType=2 AND pv.EntityID=c.CustomerID
                          LEFT JOIN dbo.Currencies cur ON pv.CurrencyID=cur.CurrencyID
                          ORDER BY pv.PaymentDate DESC, pv.PaymentID DESC");
                    dto.RecentPayments = recentsP.ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load recent treasury payments for the dashboard.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not fully load the treasury dashboard; returning the partial dashboard data.");
            }
            return dto;
        }

        // ==========================================
        // الخزائن - على الجدول الحقيقي CashBoxes
        // ==========================================
        public async Task<List<CashBoxListDto>> GetCashBoxesAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض بيانات الخزائن.", p => p.CanView, "SCR_BANKS", "SCR_CASHBOX", "SCR_CASHBOXES");

            using var connection = CreateConnection();
            List<CashBoxListDto>? directList = null;
            try
            {
                // استعلام مباشر شامل يحسب الأرصدة من الجدول الفعلي CashBoxes.CurrentBalance (المحدث من المصروفات)
                // ويحسب المقبوضات/المدفوعات شاملة المصروفات
                var sqlDirect = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, cb.CashBoxNameEn, 
                                    ISNULL(cb.CashBoxType,1) AS CashBoxType,
                                    CASE ISNULL(cb.CashBoxType,1) WHEN 1 THEN N'رئيسية' WHEN 2 THEN N'فرعية' WHEN 3 THEN N'بنك' WHEN 4 THEN N'نقطة بيع' ELSE N'أخرى' END AS CashBoxTypeName,
                                    cb.AccountID, coa.AccountCode, coa.AccountNameAr, 
                                    ISNULL(cb.OpeningBalance,0) AS OpeningBalance, 
                                    cb.CurrentBalance, 
                                    cb.CurrentBalance AS CurrentBalanceCalc,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE CashBoxID=cb.CashBoxID AND ReceiptStatus=2),0) AS TotalReceipts,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE CashBoxID=cb.CashBoxID AND PaymentStatus=2),0) 
                                    + ISNULL((SELECT SUM(Amount) FROM dbo.OverheadExpenses WHERE CashBoxID=cb.CashBoxID AND ISNULL(ExpenseStatus,1)!=3 AND ISNULL(IsPosted,0)=1),0) AS TotalPayments,
                                    ISNULL((SELECT SUM(ISNULL(NULLIF(AmountLocal,0),Amount)) FROM dbo.CashTransfers WHERE ToCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalIn,
                                    ISNULL((SELECT SUM(ISNULL(NULLIF(AmountLocal,0),Amount)) FROM dbo.CashTransfers WHERE FromCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalOut,
                                    cb.CurrencyID, cur.CurrencyCode, CAST(1 AS BIT) AS IsActive, ISNULL(cb.IsDefault,0) AS IsDefault, cb.CreatedDate
                             FROM dbo.CashBoxes cb
                             LEFT JOIN dbo.ChartOfAccounts coa ON cb.AccountID=coa.AccountID
                             LEFT JOIN dbo.Currencies cur ON cb.CurrencyID=cur.CurrencyID
                             ORDER BY cb.CashBoxNameAr";
                var result = await connection.QueryAsync<CashBoxListDto>(sqlDirect);
                var list = result.ToList();
                if (list.Any())
                {
                    await PopulateCashBoxOpeningJournalFlagsAsync(connection, list);
                    return list;
                }
                directList = list;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The direct cash-box list query failed; attempting the treasury view fallback.");
            }

            // Fallback: vw_TreasurySummary مع تصحيح CurrentBalanceCalc لو خطأ
            try
            {
                var sqlView = "SELECT * FROM dbo.vw_TreasurySummary ORDER BY CashBoxNameAr";
                var viewResult = await connection.QueryAsync<CashBoxListDto>(sqlView);
                var viewList = viewResult.ToList();
                // تصحيح: لو CurrentBalanceCalc = Opening و CurrentBalance مختلف -> استخدم CurrentBalance
                foreach (var cb in viewList)
                {
                    if (cb.TotalReceipts==0 && cb.TotalPayments==0 && cb.TotalIn==0 && cb.TotalOut==0)
                    {
                        // View لا يحسب الحركات، استخدم CurrentBalance الحقيقي
                        cb.CurrentBalanceCalc = cb.CurrentBalance;
                    }
                }
                await PopulateCashBoxOpeningJournalFlagsAsync(connection, viewList);
                return viewList;
            }
            catch (Exception ex)
            {
                if (directList is not null)
                {
                    _logger.LogWarning(ex, "The treasury-view fallback failed after the direct cash-box list query returned no rows; returning the successful empty result.");
                    return directList;
                }

                _logger.LogError(ex, "Both cash-box list queries failed; propagating the cash-box list error.");
                throw;
            }
        }

        private async Task PopulateCashBoxOpeningJournalFlagsAsync(
            SqlConnection connection, List<CashBoxListDto> cashBoxes)
        {
            var cashBoxIds = cashBoxes
                .Select(cashBox => cashBox.CashBoxID)
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
            if (cashBoxIds.Length == 0) return;

            try
            {
                var journalCashBoxIds = await connection.QueryAsync<int>(
                    @"SELECT DISTINCT SourceDocID
                      FROM dbo.JournalEntries
                      WHERE SourceDocType=@SourceDocType
                        AND SourceDocID IN @CashBoxIDs",
                    new { SourceDocType = CashBoxOpeningBalanceSourceDocType, CashBoxIDs = cashBoxIds });
                var postedIds = journalCashBoxIds.ToHashSet();
                foreach (var cashBox in cashBoxes)
                    cashBox.HasOpeningBalanceJournal = postedIds.Contains(cashBox.CashBoxID);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not check opening-balance journals for the cash-box list.");
            }
        }

        public async Task<List<CashBoxListDto>> GetCashBoxLookupsAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية استخدام الخزائن في هذا المسار.",
                p => p.CanView || p.CanAdd || p.CanEdit,
                "SCR_RECV", "SCR_PAYV", "SCR_EXPENSES", "SCR_EXPENSE", "SCR_OVERHEAD", "SCR_OVERHEAD_EXPENSES", "SCR_CASHTRANS", "SCR_CASHSTAT");

            return await GetActiveCashBoxLookupsAsync();
        }

        public async Task<List<CashBoxListDto>> GetCashBoxStatementLookupsAsync()
        {
            await _authorization.RequireCurrentCashBoxStatementViewAsync(
                "ليس لديك صلاحية استخدام الخزائن في كشف الحركات.");

            return await GetActiveCashBoxLookupsAsync();
        }

        private async Task<List<CashBoxListDto>> GetActiveCashBoxLookupsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT CashBoxID, CashBoxCode, CashBoxNameAr, CashBoxNameEn,
                               ISNULL(CashBoxType,1) AS CashBoxType,
                               ISNULL(IsDefault,0) AS IsDefault,
                               ISNULL(IsActive,1) AS IsActive
                        FROM dbo.CashBoxes
                        WHERE ISNULL(IsActive,1)=1
                        ORDER BY CashBoxNameAr";
            var result = await connection.QueryAsync<CashBoxListDto>(sql);
            return result.ToList();
        }

        // أرصدة الخزائن تعرض فقط لصلاحيات العرض الخاصة بالخزينة/الكشف؛ لا توسّع بيانات lookup المشترك.
        public async Task<List<CashBoxListDto>> GetCashBoxBalanceLookupsAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض أرصدة الخزائن.", p => p.CanView,
                "SCR_CASHSTAT", "SCR_TREASURY", "SCR_BANKS", "SCR_CASHBOX", "SCR_CASHBOXES");

            using var connection = CreateConnection();
            const string sql = @"SELECT CashBoxID, CashBoxCode, CashBoxNameAr, CashBoxNameEn,
                                        ISNULL(CashBoxType,1) AS CashBoxType,
                                        ISNULL(IsDefault,0) AS IsDefault,
                                        ISNULL(IsActive,1) AS IsActive,
                                        ISNULL(CurrentBalance,0) AS CurrentBalance
                                 FROM dbo.CashBoxes
                                 WHERE ISNULL(IsActive,1)=1
                                 ORDER BY CashBoxNameAr";
            var rows = await connection.QueryAsync<CashBoxListDto>(sql);
            return rows.ToList();
        }

        public async Task<CashBoxDto?> GetCashBoxByIdAsync(int id)
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض تفاصيل الخزائن.", p => p.CanView, "SCR_BANKS", "SCR_CASHBOX", "SCR_CASHBOXES");

            using var connection = CreateConnection();
            var sql = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, cb.CashBoxNameEn, ISNULL(cb.CashBoxType,1) AS CashBoxType,
                               cb.AccountID, coa.AccountNameAr, ISNULL(cb.OpeningBalance,0) AS OpeningBalance, cb.CurrentBalance,
                               cb.CurrencyID, cur.CurrencyCode, cb.CustodianEmployeeID, emp.FullNameAr AS CustodianName,
                               cb.IsActive, ISNULL(cb.IsDefault,0) AS IsDefault, cb.MaxLimit, cb.MinLimit, cb.Notes, cb.CreatedDate
                        FROM dbo.CashBoxes cb
                        LEFT JOIN dbo.ChartOfAccounts coa ON cb.AccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON cb.CurrencyID=cur.CurrencyID
                        LEFT JOIN dbo.Employees emp ON cb.CustodianEmployeeID=emp.EmployeeID
                        WHERE cb.CashBoxID=@ID";
            return await connection.QueryFirstOrDefaultAsync<CashBoxDto>(sql, new { ID = id });
        }

        public async Task<string> GenerateCashBoxCodeAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية إنشاء خزينة.", p => p.CanAdd, "SCR_BANKS", "SCR_CASHBOX", "SCR_CASHBOXES");

            using var connection = CreateConnection();
            var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*)+1 FROM dbo.CashBoxes");
            return $"CB-{count:000}";
        }

        public async Task<int> InsertCashBoxAsync(CashBoxDto dto, int userId)
        {
            await RequireCashBoxPermissionAsync(
                userId, p => p.CanAdd, "ليس لديك صلاحية إضافة خزينة.");

            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(dto.CashBoxCode))
                dto.CashBoxCode = await GenerateCashBoxCodeAsync();

            // التأكد من أن الرصيد الافتتاحي له قيمة افتراضية 0 لو فارغ
            var opening = dto.OpeningBalance;

            var sql = @"INSERT INTO dbo.CashBoxes(CashBoxCode, CashBoxNameAr, CashBoxNameEn, AccountID, CurrencyID, CurrentBalance, OpeningBalance, CashBoxType, IsDefault, MaxLimit, MinLimit, Notes, CustodianEmployeeID, IsActive, CreatedDate)
                        VALUES(@CashBoxCode, @CashBoxNameAr, @CashBoxNameEn, NULLIF(@AccountID,0), NULLIF(@CurrencyID,0), @CurrentBalance, @OpeningBalance, @CashBoxType, @IsDefault, @MaxLimit, @MinLimit, @Notes, NULLIF(@CustodianEmployeeID,0), @IsActive, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.CashBoxCode,
                dto.CashBoxNameAr,
                dto.CashBoxNameEn,
                dto.AccountID,
                dto.CurrencyID,
                CurrentBalance = opening,
                OpeningBalance = opening,
                dto.CashBoxType,
                dto.IsDefault,
                dto.MaxLimit,
                dto.MinLimit,
                dto.Notes,
                dto.CustodianEmployeeID,
                dto.IsActive
            });

            try
            {
                await _audit.WriteAuditLogAsync(userId, 1, "CashBoxes", newId.ToString(), moduleName: "SCR_BANKS", description: $"إنشاء خزينة: {dto.CashBoxNameAr} برصيد افتتاحي {opening}");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after creating cash box {CashBoxID}.",
                    newId);
            }
            return newId;
        }

        public async Task UpdateCashBoxAsync(CashBoxDto dto, int userId)
        {
            await RequireCashBoxPermissionAsync(
                userId, p => p.CanEdit, "ليس لديك صلاحية تعديل الخزائن.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(
                    int? CashBoxID, int? AccountID, int? CurrencyID, decimal OpeningBalance)>(
                    @"SELECT CashBoxID, AccountID, CurrencyID, ISNULL(OpeningBalance,0) AS OpeningBalance
                      FROM dbo.CashBoxes WITH (UPDLOCK,HOLDLOCK)
                      WHERE CashBoxID=@ID",
                    new { ID = dto.CashBoxID }, trans);
                if (!current.CashBoxID.HasValue)
                    throw new InvalidOperationException("الخزينة غير موجودة أو تم حذفها.");

                var hasOpeningJournal = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                      WHERE SourceDocType=@SourceDocType AND SourceDocID=@CashBoxID",
                    new
                    {
                        SourceDocType = CashBoxOpeningBalanceSourceDocType,
                        CashBoxID = dto.CashBoxID
                    }, trans);
                if (hasOpeningJournal > 0
                    && (dto.OpeningBalance != current.OpeningBalance
                        || dto.AccountID.GetValueOrDefault() != current.AccountID.GetValueOrDefault()
                        || dto.CurrencyID.GetValueOrDefault() != current.CurrencyID.GetValueOrDefault()))
                    throw new InvalidOperationException(
                        "تم ترحيل افتتاح هذه الخزينة؛ لا تغيّر الرصيد أو الحساب أو العملة مباشرة. راجع المحاسب لإنشاء قيد تسوية.");

                var activityCounts = await GetCashBoxActivityCountsAsync(connection, trans, dto.CashBoxID);
                if (activityCounts.PostedActivity > 0
                    && (dto.OpeningBalance != current.OpeningBalance
                        || dto.AccountID.GetValueOrDefault() != current.AccountID.GetValueOrDefault()
                        || dto.CurrencyID.GetValueOrDefault() != current.CurrencyID.GetValueOrDefault()))
                    throw new InvalidOperationException(
                        "للخزينة حركات مالية مرحّلة؛ لا تغيّر الرصيد الافتتاحي أو الحساب أو العملة مباشرة. أنشئ قيد تسوية بعد مراجعة المحاسب.");

                // لا يُعاد ضبط CurrentBalance إلى الافتتاحي ما دامت هناك حركة مرتبطة بالخزينة.
                var hasTrans = activityCounts.AnyActivity;

                var sql = hasTrans == 0
                    ? @"UPDATE dbo.CashBoxes SET CashBoxNameAr=@CashBoxNameAr, CashBoxNameEn=@CashBoxNameEn, CashBoxType=@CashBoxType, AccountID=NULLIF(@AccountID,0),
                            CurrencyID=NULLIF(@CurrencyID,0), OpeningBalance=@OpeningBalance, CurrentBalance=@OpeningBalance,
                            IsActive=@IsActive, IsDefault=@IsDefault, MaxLimit=@MaxLimit, MinLimit=@MinLimit, Notes=@Notes,
                            CustodianEmployeeID=NULLIF(@CustodianEmployeeID,0), ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE CashBoxID=@CashBoxID"
                    : @"UPDATE dbo.CashBoxes SET CashBoxNameAr=@CashBoxNameAr, CashBoxNameEn=@CashBoxNameEn, CashBoxType=@CashBoxType, AccountID=NULLIF(@AccountID,0),
                            CurrencyID=NULLIF(@CurrencyID,0), OpeningBalance=@OpeningBalance,
                            IsActive=@IsActive, IsDefault=@IsDefault, MaxLimit=@MaxLimit, MinLimit=@MinLimit, Notes=@Notes,
                            CustodianEmployeeID=NULLIF(@CustodianEmployeeID,0), ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE CashBoxID=@CashBoxID";

                var updated = await connection.ExecuteAsync(sql, new
                {
                    dto.CashBoxID,
                    dto.CashBoxNameAr,
                    dto.CashBoxNameEn,
                    dto.CashBoxType,
                    dto.AccountID,
                    dto.CurrencyID,
                    dto.OpeningBalance,
                    dto.IsActive,
                    dto.IsDefault,
                    dto.MaxLimit,
                    dto.MinLimit,
                    dto.Notes,
                    dto.CustodianEmployeeID,
                    UserID = userId
                }, trans);
                if (updated != 1)
                    throw new InvalidOperationException("لم يتم تحديث بيانات الخزينة.");

                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while updating cash box {CashBoxID}.",
                        dto.CashBoxID);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 2, "CashBoxes", dto.CashBoxID.ToString(), moduleName: "SCR_BANKS", description: $"تعديل خزينة: {dto.CashBoxNameAr} - رصيد افتتاحي {dto.OpeningBalance}");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after updating cash box {CashBoxID}.",
                    dto.CashBoxID);
            }
        }

        public async Task DeleteCashBoxAsync(int id, int userId)
        {
            await RequireCashBoxPermissionAsync(
                userId, p => p.CanDelete, "ليس لديك صلاحية حذف الخزائن.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var cashBoxId = await connection.ExecuteScalarAsync<int?>(
                    "SELECT CashBoxID FROM dbo.CashBoxes WITH (UPDLOCK,HOLDLOCK) WHERE CashBoxID=@ID",
                    new { ID = id }, trans);
                if (!cashBoxId.HasValue)
                    throw new InvalidOperationException("الخزينة غير موجودة أو تم حذفها.");

                var hasTrans = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM (SELECT CashBoxID FROM dbo.ReceiptVouchers WHERE CashBoxID=@ID UNION ALL SELECT CashBoxID FROM dbo.PaymentVouchers WHERE CashBoxID=@ID) t",
                    new { ID = id }, trans);
                if (hasTrans > 0) throw new Exception("لا يمكن حذف خزينة بها حركات");

                var hasOpeningJournal = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                      WHERE SourceDocType=@SourceDocType AND SourceDocID=@CashBoxID",
                    new { SourceDocType = CashBoxOpeningBalanceSourceDocType, CashBoxID = id }, trans);
                if (hasOpeningJournal > 0)
                    throw new InvalidOperationException("لا يمكن حذف خزينة مرتبطة بقيد افتتاحي؛ احتفظ بسجلها المحاسبي.");

                var deleted = await connection.ExecuteAsync(
                    "DELETE FROM dbo.CashBoxes WHERE CashBoxID=@ID",
                    new { ID = id }, trans);
                if (deleted != 1)
                    throw new InvalidOperationException("لم يتم حذف الخزينة؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while deleting cash box {CashBoxID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "CashBoxes", id.ToString(), moduleName: "SCR_BANKS", description: "حذف خزينة");
        }

        public async Task<string> PostCashBoxOpeningBalanceAsync(
            int cashBoxId,
            int expectedCashAccountId,
            decimal expectedOpeningBalance,
            int offsetAccountId,
            DateTime journalDate,
            bool confirmedNoPreviousOpeningJournal,
            int userId)
        {
            await RequireCashBoxPermissionAsync(
                userId, p => p.CanPost, "ليس لديك صلاحية ترحيل رصيد افتتاحي للخزينة.");
            if (cashBoxId <= 0) throw new ArgumentOutOfRangeException(nameof(cashBoxId));
            if (expectedCashAccountId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedCashAccountId));
            if (offsetAccountId <= 0) throw new ArgumentOutOfRangeException(nameof(offsetAccountId));
            if (!confirmedNoPreviousOpeningJournal)
                throw new InvalidOperationException("راجع كشف حساب الخزينة أولاً وأكد أن الرصيد الافتتاحي لم يُسجل بقيد سابق.");

            var postingDate = journalDate.Date;
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var cashBox = await connection.QueryFirstOrDefaultAsync<(
                    int? CashBoxID, string? CashBoxCode, string? CashBoxNameAr,
                    int? AccountID, decimal OpeningBalance, int? CurrencyID, bool IsActive)>(
                    @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, cb.AccountID,
                             ISNULL(cb.OpeningBalance,0) AS OpeningBalance, cb.CurrencyID,
                             CONVERT(bit,ISNULL(cb.IsActive,1)) AS IsActive
                      FROM dbo.CashBoxes cb WITH (UPDLOCK,HOLDLOCK)
                      WHERE cb.CashBoxID=@ID",
                    new { ID = cashBoxId }, trans);
                if (!cashBox.CashBoxID.HasValue)
                    throw new InvalidOperationException("الخزينة غير موجودة.");
                if (!cashBox.IsActive)
                    throw new InvalidOperationException("لا يمكن ترحيل افتتاح خزينة غير نشطة.");
                if (!cashBox.AccountID.HasValue || cashBox.AccountID.Value <= 0)
                    throw new InvalidOperationException("اربط الخزينة أولاً بحساب صندوق تفصيلي في شجرة الحسابات.");
                if (cashBox.AccountID.Value != expectedCashAccountId
                    || cashBox.OpeningBalance != expectedOpeningBalance)
                    throw new InvalidOperationException("تغير الحساب أو الرصيد الافتتاحي بعد فتح نافذة المراجعة؛ أعد تحميل الصفحة وراجع القيد من جديد.");
                if (cashBox.OpeningBalance == 0m)
                    throw new InvalidOperationException("الرصيد الافتتاحي للخزينة صفر؛ لا يوجد مبلغ لترحيله.");
                if (cashBox.AccountID.Value == offsetAccountId)
                    throw new InvalidOperationException("لا يمكن استخدام حساب الصندوق نفسه كحساب مقابل.");

                var existingOpeningJournal = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                      WHERE SourceDocType=@SourceDocType AND SourceDocID=@CashBoxID",
                    new
                    {
                        SourceDocType = CashBoxOpeningBalanceSourceDocType,
                        CashBoxID = cashBoxId
                    }, trans);
                if (existingOpeningJournal > 0)
                    throw new InvalidOperationException("يوجد قيد افتتاحي مرتبط بهذه الخزينة بالفعل؛ لم يتم إنشاء قيد مكرر.");

                var accounts = (await connection.QueryAsync<(
                    int AccountID, int AccountType, int AccountNature,
                    bool IsDetailAccount, bool IsActive, bool CostCenterRequired, int? CurrencyID)>(
                    @"SELECT coa.AccountID, coa.AccountType,
                             ISNULL(coa.AccountNature,1) AS AccountNature,
                             CONVERT(bit,ISNULL(coa.IsDetailAccount,0)) AS IsDetailAccount,
                             CONVERT(bit,ISNULL(coa.IsActive,1)) AS IsActive,
                             CONVERT(bit,ISNULL(coa.CostCenterRequired,0)) AS CostCenterRequired,
                             coa.CurrencyID
                      FROM dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK)
                      WHERE coa.AccountID IN @AccountIDs",
                    new { AccountIDs = new[] { cashBox.AccountID.Value, offsetAccountId } }, trans))
                    .ToDictionary(account => account.AccountID);

                if (!accounts.TryGetValue(cashBox.AccountID.Value, out var cashAccount)
                    || !cashAccount.IsActive || !cashAccount.IsDetailAccount
                    || cashAccount.AccountType != 1 || cashAccount.AccountNature != 1)
                    throw new InvalidOperationException("الحساب المرتبط بالخزينة غير نشط أو ليس حساب أصل تفصيلياً ذا طبيعة مدينة.");
                if (!accounts.TryGetValue(offsetAccountId, out var offsetAccount)
                    || !offsetAccount.IsActive || !offsetAccount.IsDetailAccount
                    || offsetAccount.AccountType != 3 || offsetAccount.AccountNature != 2)
                    throw new InvalidOperationException("اختر حساب أرباح مرحلة/حقوق ملكية نشطاً وتفصيلياً ذا طبيعة دائنة.");
                if (cashAccount.CostCenterRequired || offsetAccount.CostCenterRequired)
                    throw new InvalidOperationException("أحد الحسابين يتطلب مركز تكلفة؛ لم يُنشأ قيد بلا مركز تكلفة.");

                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, cashBox.CurrencyID, 1m);
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, cashAccount.CurrencyID, 1m);
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, offsetAccount.CurrencyID, 1m);

                var accountingPeriod = await connection.QueryFirstOrDefaultAsync<(
                    int? FiscalYearID, int? PeriodID, int? YearStatus, int? PeriodStatus,
                    DateTime? YearStart, DateTime? YearEnd, DateTime? PeriodStart, DateTime? PeriodEnd)>(
                    @"SELECT TOP 1 fy.FiscalYearID, ap.PeriodID, fy.YearStatus, ap.PeriodStatus,
                             fy.StartDate AS YearStart, fy.EndDate AS YearEnd,
                             ap.StartDate AS PeriodStart, ap.EndDate AS PeriodEnd
                      FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
                      INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK)
                        ON ap.FiscalYearID=fy.FiscalYearID
                      WHERE fy.YearStatus=1 AND ap.PeriodStatus=1
                        AND @JournalDate BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
                        AND @JournalDate BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
                      ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
                    new { JournalDate = postingDate }, trans);
                if (!accountingPeriod.FiscalYearID.HasValue || !accountingPeriod.PeriodID.HasValue
                    || !accountingPeriod.YearStatus.HasValue || accountingPeriod.YearStatus.Value != 1
                    || !accountingPeriod.PeriodStatus.HasValue || accountingPeriod.PeriodStatus.Value != 1
                    || !accountingPeriod.YearStart.HasValue || !accountingPeriod.YearEnd.HasValue
                    || !accountingPeriod.PeriodStart.HasValue || !accountingPeriod.PeriodEnd.HasValue)
                    throw new InvalidOperationException("لا توجد سنة وفترة محاسبية مفتوحتان تغطيان تاريخ القيد؛ لم يتم الترحيل.");

                var journalNumber = await connection.QueryFirstOrDefaultAsync<string>(
                    @"DECLARE @NextNum NVARCHAR(50);
                      EXEC dbo.sp_GetNextNumber @SequenceCode=@SeqCode, @NextNumber=@NextNum OUTPUT;
                      SELECT @NextNum AS JournalNumber;",
                    new { SeqCode = "JV" }, trans);
                if (string.IsNullOrWhiteSpace(journalNumber))
                    throw new InvalidOperationException("تعذر توليد رقم قيد اليومية؛ أُلغيت عملية الترحيل.");

                var amount = Math.Abs(cashBox.OpeningBalance);
                var cashDebit = cashBox.OpeningBalance > 0m ? amount : 0m;
                var cashCredit = cashBox.OpeningBalance < 0m ? amount : 0m;
                var offsetDebit = cashCredit;
                var offsetCredit = cashDebit;
                var description = $"افتتاح رصيد الخزينة {cashBox.CashBoxCode} - {cashBox.CashBoxNameAr}";

                var journalId = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.JournalEntries
                        (JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID,
                         SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType,
                         JournalStatus, IsAutoGenerated, PostedBy, PostedDate, CreatedBy, CreatedDate)
                      VALUES
                        (@JournalNumber, @JournalDate, @Description, @Reference, @SourceDocType, @SourceDocID,
                         @SourceDocNumber, @TotalDebit, @TotalCredit, @FiscalYearID, @PeriodID, 1,
                         2, 1, @UserID, GETDATE(), @UserID, GETDATE());
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        JournalNumber = journalNumber,
                        JournalDate = postingDate,
                        Description = description,
                        Reference = cashBox.CashBoxCode,
                        SourceDocType = CashBoxOpeningBalanceSourceDocType,
                        SourceDocID = cashBoxId,
                        SourceDocNumber = cashBox.CashBoxCode,
                        TotalDebit = amount,
                        TotalCredit = amount,
                        accountingPeriod.FiscalYearID,
                        accountingPeriod.PeriodID,
                        UserID = userId
                    }, trans);
                if (journalId <= 0)
                    throw new InvalidOperationException("لم يتم إنشاء رأس القيد؛ أُلغيت عملية الترحيل.");

                await connection.ExecuteAsync(
                    @"INSERT INTO dbo.JournalEntryDetails
                        (JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
                      VALUES (@JournalID, 1, @CashAccountID, @CashDebit, @CashCredit, @Description);
                      INSERT INTO dbo.JournalEntryDetails
                        (JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description)
                      VALUES (@JournalID, 2, @OffsetAccountID, @OffsetDebit, @OffsetCredit, @Description);",
                    new
                    {
                        JournalID = journalId,
                        CashAccountID = cashBox.AccountID.Value,
                        OffsetAccountID = offsetAccountId,
                        CashDebit = cashDebit,
                        CashCredit = cashCredit,
                        OffsetDebit = offsetDebit,
                        OffsetCredit = offsetCredit,
                        Description = description
                    }, trans);

                await ApplyCashBoxOpeningAccountDeltaAsync(
                    connection, trans, cashBox.AccountID.Value, cashBox.OpeningBalance);
                await ApplyCashBoxOpeningAccountDeltaAsync(
                    connection, trans, offsetAccountId, -cashBox.OpeningBalance);

                trans.Commit();
                try
                {
                    await _audit.WriteAuditLogAsync(
                        userId, 2, "JournalEntries", journalId.ToString(), moduleName: "SCR_BANKS",
                        description: $"ترحيل افتتاح خزينة {cashBox.CashBoxNameAr} بمبلغ {cashBox.OpeningBalance:N2} - القيد {journalNumber}");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after posting the opening balance for cash box {CashBoxID}.",
                        cashBoxId);
                }

                return journalNumber;
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after posting the opening balance for cash box {CashBoxID}.",
                        cashBoxId);
                }
                throw;
            }
        }

        private static async Task ApplyCashBoxOpeningAccountDeltaAsync(
            SqlConnection connection, SqlTransaction transaction, int accountId, decimal delta)
        {
            var affected = await connection.ExecuteAsync(
                @"UPDATE dbo.ChartOfAccounts
                  SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta
                  WHERE AccountID=@ID",
                new { Delta = delta, ID = accountId }, transaction);
            if (affected != 1)
                throw new InvalidOperationException("تعذر تحديث رصيد حساب القيد؛ أُلغيت عملية الترحيل.");

            var visited = new HashSet<int> { accountId };
            var currentAccountId = accountId;
            for (var depth = 0; depth < 50; depth++)
            {
                var parentId = await connection.ExecuteScalarAsync<int?>(
                    @"SELECT ParentAccountID
                      FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK)
                      WHERE AccountID=@ID",
                    new { ID = currentAccountId }, transaction);
                if (!parentId.HasValue || parentId.Value <= 0) return;
                if (!visited.Add(parentId.Value))
                    throw new InvalidOperationException("توجد دورة في شجرة الحسابات؛ أُلغيت عملية ترحيل الافتتاح.");

                var parentAffected = await connection.ExecuteAsync(
                    @"UPDATE dbo.ChartOfAccounts
                      SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta
                      WHERE AccountID=@ID",
                    new { Delta = delta, ID = parentId.Value }, transaction);
                if (parentAffected != 1)
                    throw new InvalidOperationException("تعذر تحديث رصيد الحساب الأب؛ أُلغيت عملية الترحيل.");
                currentAccountId = parentId.Value;
            }

            throw new InvalidOperationException("تجاوز عمق شجرة الحسابات الحد الآمن؛ أُلغيت عملية الترحيل.");
        }

        public async Task<List<VoucherInvoiceAllocationDto>> GetSalesInvoicesForSettlementAsync(int customerId, int? currentReceiptId = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_SINV", p => p.CanView, "ليس لديك صلاحية عرض فواتير المبيعات لتسويتها.");
            if (customerId <= 0) return new List<VoucherInvoiceAllocationDto>();

            using var connection = CreateConnection();
            return (await connection.QueryAsync<VoucherInvoiceAllocationDto>(
                @"SELECT si.InvoiceID, si.InvoiceNumber, si.InvoiceDate, si.CustomerID AS PartyID,
                         c.CustomerNameAr AS PartyName, ISNULL(si.TotalAmount,0) AS TotalAmount,
                         ISNULL(si.PaidAmount,0) AS PaidAmount,
                         ISNULL(si.TotalAmount,0)-ISNULL(si.PaidAmount,0)-ISNULL(drafts.ReservedAmount,0) AS RemainingAmount,
                         CAST(0 AS decimal(18,2)) AS AppliedAmount, si.CurrencyID,
                         ISNULL(si.ExchangeRate,1) AS ExchangeRate, cur.CurrencyCode
                  FROM dbo.SalesInvoices si
                  INNER JOIN dbo.Customers c ON c.CustomerID=si.CustomerID
                  LEFT JOIN dbo.Currencies cur ON cur.CurrencyID=si.CurrencyID
                  OUTER APPLY
                  (
                      SELECT ISNULL(SUM(a.AllocatedAmount),0) AS ReservedAmount
                      FROM dbo.ReceiptAllocations a
                      INNER JOIN dbo.ReceiptVouchers rv ON rv.ReceiptID=a.ReceiptID
                      WHERE a.InvoiceID=si.InvoiceID AND rv.ReceiptStatus=1
                        AND (@CurrentReceiptID IS NULL OR rv.ReceiptID<>@CurrentReceiptID)
                  ) drafts
                  WHERE si.CustomerID=@CustomerID AND si.InvoiceStatus IN (3,5)
                    AND ISNULL(si.PaymentStatus,1) IN (1,2)
                    AND ISNULL(si.TotalAmount,0)-ISNULL(si.PaidAmount,0)-ISNULL(drafts.ReservedAmount,0)>0
                  ORDER BY si.InvoiceDate,si.InvoiceID",
                new { CustomerID = customerId, CurrentReceiptID = currentReceiptId })).ToList();
        }

        public async Task<List<VoucherInvoiceAllocationDto>> GetPurchaseInvoicesForSettlementAsync(int supplierId, int? currentPaymentId = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_PINV", p => p.CanView, "ليس لديك صلاحية عرض فواتير المشتريات لتسويتها.");
            if (supplierId <= 0) return new List<VoucherInvoiceAllocationDto>();

            using var connection = CreateConnection();
            return (await connection.QueryAsync<VoucherInvoiceAllocationDto>(
                @"SELECT pi.InvoiceID, pi.InvoiceNumber, pi.InvoiceDate, pi.SupplierID AS PartyID,
                         s.SupplierNameAr AS PartyName, ISNULL(pi.TotalAmount,0) AS TotalAmount,
                         ISNULL(pi.PaidAmount,0) AS PaidAmount,
                         ISNULL(pi.TotalAmount,0)-ISNULL(pi.PaidAmount,0)-ISNULL(drafts.ReservedAmount,0) AS RemainingAmount,
                         CAST(0 AS decimal(18,2)) AS AppliedAmount, pi.CurrencyID,
                         ISNULL(pi.ExchangeRate,1) AS ExchangeRate, cur.CurrencyCode
                  FROM dbo.PurchaseInvoices pi
                  INNER JOIN dbo.Suppliers s ON s.SupplierID=pi.SupplierID
                  LEFT JOIN dbo.Currencies cur ON cur.CurrencyID=pi.CurrencyID
                  OUTER APPLY
                  (
                      SELECT ISNULL(SUM(a.AllocatedAmount),0) AS ReservedAmount
                      FROM dbo.PaymentAllocations a
                      INNER JOIN dbo.PaymentVouchers pv ON pv.PaymentID=a.PaymentID
                      WHERE a.InvoiceID=pi.InvoiceID AND pv.PaymentStatus=1
                        AND (@CurrentPaymentID IS NULL OR pv.PaymentID<>@CurrentPaymentID)
                  ) drafts
                  WHERE pi.SupplierID=@SupplierID AND pi.InvoiceStatus IN (3,4,6)
                    AND ISNULL(pi.PaymentStatus,1) IN (1,2)
                    AND ISNULL(pi.TotalAmount,0)-ISNULL(pi.PaidAmount,0)-ISNULL(drafts.ReservedAmount,0)>0
                  ORDER BY pi.InvoiceDate,pi.InvoiceID",
                new { SupplierID = supplierId, CurrentPaymentID = currentPaymentId })).ToList();
        }

        // ==========================================
        // سندات القبض - على الجدول الحقيقي ReceiptVouchers
        // ==========================================
        public async Task<List<ReceiptVoucherListDto>> GetReceiptVouchersAsync(DateTime? from = null, DateTime? to = null, int? cashBoxId = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_RECV", p => p.CanView, "ليس لديك صلاحية عرض سندات القبض.");

            using var connection = CreateConnection();
            var sql = @"SELECT rv.ReceiptID, rv.ReceiptNumber, rv.ReceiptDate, rv.ReceivedFrom, rv.EntityType, rv.EntityID,
                               CASE WHEN rv.EntityType=1 THEN c.CustomerNameAr WHEN rv.EntityType=2 THEN s.SupplierNameAr ELSE rv.ReceivedFrom END AS EntityName,
                               c.CustomerNameAr, c.CustomerCode, s.SupplierNameAr,
                               rv.CashBoxID, cb.CashBoxNameAr, cb.CashBoxCode, rv.BankAccountID, ba.BankName AS BankAccountName,
                               rv.Amount, ISNULL(rv.AmountLocal, rv.Amount) AS AmountLocal, rv.CurrencyID, cur.CurrencyCode,
                               rv.PaymentMethod, CASE rv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' WHEN 4 THEN N'فيزا' ELSE N'أخرى' END AS PaymentMethodName,
                               rv.Description, rv.ReceiptStatus, CASE rv.ReceiptStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                               ISNULL(rv.IsPosted,0) AS IsPosted, rv.JournalID, je.JournalNumber, rv.CreatedDate, emp.FullNameAr AS CreatedByName
                        FROM dbo.ReceiptVouchers rv
                        LEFT JOIN dbo.CashBoxes cb ON rv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.Customers c ON rv.EntityType=1 AND rv.EntityID=c.CustomerID
                        LEFT JOIN dbo.Suppliers s ON rv.EntityType=2 AND rv.EntityID=s.SupplierID
                        LEFT JOIN dbo.BankAccounts ba ON rv.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.Currencies cur ON rv.CurrencyID=cur.CurrencyID
                        LEFT JOIN dbo.JournalEntries je ON rv.JournalID=je.JournalID
                        LEFT JOIN dbo.SystemUsers su ON rv.CreatedBy=su.UserID
                        LEFT JOIN dbo.Employees emp ON su.EmployeeID=emp.EmployeeID
                        WHERE 1=1
                        AND (@From IS NULL OR rv.ReceiptDate >= @From)
                        AND (@To IS NULL OR rv.ReceiptDate <= @To)
                        AND (@CashBoxID IS NULL OR rv.CashBoxID=@CashBoxID)
                        ORDER BY rv.ReceiptDate DESC, rv.ReceiptID DESC";

            var result = await connection.QueryAsync<ReceiptVoucherListDto>(sql, new { From = from, To = to, CashBoxID = cashBoxId });
            return result.ToList();
        }

        public async Task<ReceiptVoucherDto?> GetReceiptByIdAsync(int id)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_RECV", p => p.CanView, "ليس لديك صلاحية عرض سندات القبض.");

            using var connection = CreateConnection();
            var sql = @"SELECT rv.ReceiptID, rv.ReceiptNumber, rv.ReceiptDate, rv.ReceivedFrom, rv.EntityType, rv.EntityID,
                               CASE WHEN rv.EntityType=1 THEN c.CustomerNameAr WHEN rv.EntityType=2 THEN s.SupplierNameAr ELSE rv.ReceivedFrom END AS EntityName,
                               rv.CashBoxID, cb.CashBoxNameAr, rv.BankAccountID, ba.BankName AS BankAccountName,
                               rv.Amount, rv.CurrencyID, cur.CurrencyCode, rv.ExchangeRate, ISNULL(rv.AmountLocal, rv.Amount) AS AmountLocal,
                               rv.PaymentMethod, rv.ChequeNumber, rv.ChequeDate, rv.ChequeBankName, rv.TransferRef,
                               rv.Description, rv.RevenueAccountID, coa.AccountNameAr AS RevenueAccountNameAr, rv.CostCenterID,
                               rv.ReceiptStatus, rv.JournalID, ISNULL(rv.IsPosted,0) AS IsPosted, rv.Notes, rv.CreatedBy, rv.CreatedDate
                        FROM dbo.ReceiptVouchers rv
                        LEFT JOIN dbo.CashBoxes cb ON rv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.Customers c ON rv.EntityType=1 AND rv.EntityID=c.CustomerID
                        LEFT JOIN dbo.Suppliers s ON rv.EntityType=2 AND rv.EntityID=s.SupplierID
                        LEFT JOIN dbo.BankAccounts ba ON rv.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.ChartOfAccounts coa ON rv.RevenueAccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON rv.CurrencyID=cur.CurrencyID
                        WHERE rv.ReceiptID=@ID";
            var dto = await connection.QueryFirstOrDefaultAsync<ReceiptVoucherDto>(sql, new { ID = id });
            if (dto is not null)
            {
                dto.IsInvoiceSettlement = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT CONVERT(bit, CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.ReceiptAllocations WHERE ReceiptID=@ID
                      ) THEN 1 ELSE 0 END)", new { ID = id });
                if (dto.IsInvoiceSettlement)
                {
                    try
                    {
                        await _authorization.RequireCurrentUserPermissionAsync(
                            "SCR_SINV", p => p.CanView, "ليس لديك صلاحية عرض فواتير المبيعات.");
                        dto.InvoiceAllocations = await GetReceiptInvoiceAllocationsCoreAsync(connection, null, id);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        dto.InvoiceAllocations = new List<VoucherInvoiceAllocationDto>();
                    }
                }
            }
            return dto;
        }

        public async Task<int> InsertReceiptAsync(ReceiptVoucherDto dto, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_RECV", p => p.CanAdd, "ليس لديك صلاحية إنشاء سند قبض.");

            dto.ReceiptNumber = null;
            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            int newId;
            int? recordIdForRollbackLog = null;
            string receiptNumber = string.Empty;
            try
            {
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, dto.CurrencyID, dto.ExchangeRate);
                await EnsureVoucherSourceLocalCurrencyAsync(
                    connection, trans, dto.CashBoxID, dto.BankAccountID, "سند القبض");
                await PrepareReceiptSettlementAsync(connection, trans, dto, userId, null);
                if (string.IsNullOrWhiteSpace(dto.ReceivedFrom))
                {
                    if (dto.EntityType == 1 && dto.EntityID.HasValue)
                        dto.ReceivedFrom = await connection.QueryFirstOrDefaultAsync<string>(
                            "SELECT CustomerNameAr FROM dbo.Customers WHERE CustomerID=@ID", new { ID = dto.EntityID }, trans);
                    else if (dto.EntityType == 2 && dto.EntityID.HasValue)
                        dto.ReceivedFrom = await connection.QueryFirstOrDefaultAsync<string>(
                            "SELECT SupplierNameAr FROM dbo.Suppliers WHERE SupplierID=@ID", new { ID = dto.EntityID }, trans);
                }

                receiptNumber = await _sequence.GetNextCodeAsync(connection, trans, "RV");

                var sql = @"INSERT INTO dbo.ReceiptVouchers(ReceiptNumber, ReceiptDate, ReceivedFrom, EntityType, EntityID, PaymentMethod, CashBoxID, BankAccountID,
                            Amount, CurrencyID, ExchangeRate, AmountLocal, ChequeNumber, ChequeDate, ChequeBankName, TransferRef, Description, RevenueAccountID, CostCenterID,
                            ReceiptStatus, Notes, CreatedBy, CreatedDate)
                            VALUES(@ReceiptNumber, @ReceiptDate, @ReceivedFrom, @EntityType, NULLIF(@EntityID,0), @PaymentMethod, NULLIF(@CashBoxID,0), NULLIF(@BankAccountID,0),
                            @Amount, NULLIF(@CurrencyID,0), @ExchangeRate, @AmountLocal, @ChequeNumber, @ChequeDate, @ChequeBankName, @TransferRef, @Description,
                            NULLIF(@RevenueAccountID,0), NULLIF(@CostCenterID,0), 1, @Notes, @CreatedBy, GETDATE());
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";
                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    ReceiptNumber = receiptNumber,
                    dto.ReceiptDate,
                    dto.ReceivedFrom,
                    dto.EntityType,
                    dto.EntityID,
                    dto.PaymentMethod,
                    dto.CashBoxID,
                    dto.BankAccountID,
                    dto.Amount,
                    dto.CurrencyID,
                    ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                    dto.AmountLocal,
                    dto.ChequeNumber,
                    dto.ChequeDate,
                    dto.ChequeBankName,
                    dto.TransferRef,
                    dto.Description,
                    dto.RevenueAccountID,
                    dto.CostCenterID,
                    dto.Notes,
                    CreatedBy = userId
                }, trans);
                recordIdForRollbackLog = newId;

                await ReplaceReceiptInvoiceAllocationsAsync(
                    connection, trans, newId, dto.InvoiceAllocations);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling receipt-voucher creation for receipt {ReceiptID}.",
                        recordIdForRollbackLog);
                }
                throw;
            }

            dto.ReceiptNumber = receiptNumber;
            await _audit.WriteAuditLogAsync(userId, 1, "ReceiptVouchers", newId.ToString(), moduleName: "SCR_RECV", description: $"إنشاء سند قبض: {dto.ReceiptNumber} مبلغ {dto.Amount}");
            await NotifyVoucherPendingApprovalAsync("SCR_RECV", "سند القبض", dto.ReceiptNumber ?? newId.ToString(), dto.Amount, newId, userId);
            return newId;
        }

        public async Task UpdateReceiptAsync(ReceiptVoucherDto dto, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_RECV", p => p.CanEdit, "ليس لديك صلاحية تعديل سندات القبض.");

            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(
                    int? ReceiptStatus, int? CurrencyID, decimal ExchangeRate,
                    int? CashBoxID, int? BankAccountID)>(
                    @"SELECT ReceiptStatus, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate,
                             CashBoxID, BankAccountID
                      FROM dbo.ReceiptVouchers WITH (UPDLOCK, HOLDLOCK) WHERE ReceiptID=@ID",
                    new { ID = dto.ReceiptID }, trans);
                if (current.ReceiptStatus != 1) throw new Exception("لا يمكن تعديل سند معتمد أو غير موجود");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, current.CurrencyID, current.ExchangeRate);
                await EnsureVoucherSourceLocalCurrencyAsync(
                    connection, trans, current.CashBoxID.GetValueOrDefault(), current.BankAccountID, "سند القبض");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, dto.CurrencyID, dto.ExchangeRate);
                await EnsureVoucherSourceLocalCurrencyAsync(
                    connection, trans, dto.CashBoxID, dto.BankAccountID, "سند القبض");

                await PrepareReceiptSettlementAsync(connection, trans, dto, userId, dto.ReceiptID);
                var sql = @"UPDATE dbo.ReceiptVouchers SET ReceiptDate=@ReceiptDate, ReceivedFrom=@ReceivedFrom, EntityType=@EntityType, EntityID=NULLIF(@EntityID,0),
                            PaymentMethod=@PaymentMethod, CashBoxID=NULLIF(@CashBoxID,0), BankAccountID=NULLIF(@BankAccountID,0), Amount=@Amount, CurrencyID=NULLIF(@CurrencyID,0),
                            ExchangeRate=@ExchangeRate, AmountLocal=@AmountLocal, ChequeNumber=@ChequeNumber, ChequeDate=@ChequeDate, ChequeBankName=@ChequeBankName,
                            TransferRef=@TransferRef, Description=@Description, RevenueAccountID=NULLIF(@RevenueAccountID,0), CostCenterID=NULLIF(@CostCenterID,0),
                            Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                            WHERE ReceiptID=@ReceiptID";
                await connection.ExecuteAsync(sql, new
                {
                    dto.ReceiptID,
                    dto.ReceiptDate,
                    dto.ReceivedFrom,
                    dto.EntityType,
                    dto.EntityID,
                    dto.PaymentMethod,
                    dto.CashBoxID,
                    dto.BankAccountID,
                    dto.Amount,
                    dto.CurrencyID,
                    ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                    dto.AmountLocal,
                    dto.ChequeNumber,
                    dto.ChequeDate,
                    dto.ChequeBankName,
                    dto.TransferRef,
                    dto.Description,
                    dto.RevenueAccountID,
                    dto.CostCenterID,
                    dto.Notes,
                    UserID = userId
                }, trans);
                await ReplaceReceiptInvoiceAllocationsAsync(
                    connection, trans, dto.ReceiptID, dto.InvoiceAllocations);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling receipt-voucher update for receipt {ReceiptID}.",
                        dto.ReceiptID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "ReceiptVouchers", dto.ReceiptID.ToString(), moduleName: "SCR_RECV", description: $"تعديل سند قبض: {dto.ReceiptNumber}");
        }

        // ==========================================
        // عكس القيود المرحّلة وإلغاء المستندات المعتمدة (سندات وتحويلات)
        // ==========================================
        private async Task<int> ReversePostedJournalCoreAsync(
            SqlConnection connection, SqlTransaction transaction,
            int originalJournalId, int userId, int postedByEmployeeId,
            DateTime reversalDate, string? reason, string descriptionPrefix)
        {
            var original = await connection.QueryFirstOrDefaultAsync<(
                string? JournalNumber, int JournalStatus, string? SourceDocType)>(
                @"SELECT JournalNumber, JournalStatus, SourceDocType
                  FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                  WHERE JournalID=@ID",
                new { ID = originalJournalId }, transaction);
            if (original.JournalStatus == 0)
                throw new Exception("القيد الأصلي غير موجود.");
            if (original.JournalStatus != 2)
                throw new Exception("لا يمكن عكس قيد غير مرحّل.");
            if (string.Equals(original.SourceDocType, "REVERSAL", StringComparison.OrdinalIgnoreCase))
                throw new Exception("لا يمكن عكس قيد عكسي؛ اعكس القيد الأصلي أو أنشئ قيد تسوية جديدًا.");

            var alreadyReversed = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                  WHERE SourceDocType='REVERSAL' AND SourceDocID=@ID AND JournalStatus=2",
                new { ID = originalJournalId }, transaction);
            if (alreadyReversed > 0)
                throw new InvalidOperationException("هذا القيد معكوس مسبقًا بقيد عكسي مرحّل؛ لا يمكن عكسه مرتين.");

            var details = (await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit, string? Description, int? CostCenterID)>(
                @"SELECT AccountID, DebitAmount AS Debit, CreditAmount AS Credit, Description, CostCenterID
                  FROM dbo.JournalEntryDetails WHERE JournalID=@ID ORDER BY LineNumber",
                new { ID = originalJournalId }, transaction)).ToList();
            if (details.Count < 2)
                throw new InvalidOperationException("القيد الأصلي بلا سطور مكتملة؛ لا يمكن عكسه.");
            if (details.Any(d => d.AccountID <= 0 || d.Debit < 0 || d.Credit < 0 || (d.Debit > 0 && d.Credit > 0)))
                throw new InvalidOperationException("سطور القيد الأصلي غير صالحة؛ لا يمكن عكسه.");
            var totalDebit = details.Sum(d => d.Debit);
            var totalCredit = details.Sum(d => d.Credit);
            if (totalDebit <= 0 || Math.Abs(totalDebit - totalCredit) > 0.01m)
                throw new InvalidOperationException("القيد الأصلي غير متوازن؛ لا يمكن عكسه.");

            var accountingPeriod = await connection.QueryFirstOrDefaultAsync<(int? FiscalYearID, int? PeriodID)>(
                @"SELECT TOP 1 fy.FiscalYearID, ap.PeriodID
                  FROM dbo.FiscalYears fy WITH (UPDLOCK, HOLDLOCK)
                  INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK, HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
                  WHERE fy.YearStatus=1 AND ap.PeriodStatus=1
                    AND @RDate BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
                    AND @RDate BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
                  ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
                new { RDate = reversalDate.Date }, transaction);
            if (!accountingPeriod.FiscalYearID.HasValue || !accountingPeriod.PeriodID.HasValue)
                throw new InvalidOperationException("لا توجد سنة وفترة مفتوحتان تغطيان تاريخ العكس؛ اختر تاريخًا داخل فترة مفتوحة.");

            var journalNumberSql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;";
            var reversalNumber = await connection.QueryFirstOrDefaultAsync<string>(journalNumberSql, transaction: transaction);
            if (string.IsNullOrWhiteSpace(reversalNumber))
                throw new InvalidOperationException("تعذر توليد رقم القيد العكسي؛ أُلغي الإلغاء بالكامل.");

            var description = $"{descriptionPrefix} — عكس القيد {original.JournalNumber}";
            if (!string.IsNullOrWhiteSpace(reason)) description += $" — {reason.Trim()}";

            var reversalId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate, PostedBy, PostedDate)
                  VALUES(@Number, @Date, @Description, @Reference, 'REVERSAL', @OriginalID, @OriginalNumber, @Total, @Total, @FiscalYearID, @PeriodID, 3, 2, 1, @UserID, GETDATE(), @PostedBy, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    Number = reversalNumber,
                    Date = reversalDate.Date,
                    Description = description,
                    Reference = original.JournalNumber,
                    OriginalID = originalJournalId,
                    OriginalNumber = original.JournalNumber,
                    Total = totalDebit,
                    accountingPeriod.FiscalYearID,
                    accountingPeriod.PeriodID,
                    UserID = userId,
                    PostedBy = postedByEmployeeId
                }, transaction);

            int line = 1;
            foreach (var d in details)
            {
                await connection.ExecuteAsync(
                    "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID) VALUES(@JID, @Line, @AccID, @Debit, @Credit, @Desc, NULLIF(@CCID,0))",
                    new { JID = reversalId, Line = line++, AccID = d.AccountID, Debit = d.Credit, Credit = d.Debit, Desc = d.Description, CCID = d.CostCenterID ?? 0 }, transaction);
            }

            await ApplyJournalAccountBalancesAsync(connection, transaction, reversalId);
            return reversalId;
        }

        private async Task RestoreVoucherTreasuryBalanceAsync(
            SqlConnection connection, SqlTransaction transaction,
            int cashBoxId, int? bankAccountId, int paymentMethod, decimal amountLocal,
            bool isReceipt, string voucherNumber)
        {
            // اعتماد السند حرّك رصيد الخزينة/البنك عبر إجراء الترحيل لطريقتي النقدي والتحويل البنكي؛
            // الإلغاء يعيد الحركة نفسها هنا. طريقة الشيك لا تُمسّ هنا: أثرها يُعالج في دورة الشيكات.
            if (paymentMethod != 1 && paymentMethod != 2) return;
            var delta = isReceipt ? -amountLocal : amountLocal;
            if (bankAccountId.GetValueOrDefault() > 0)
            {
                var affected = await connection.ExecuteAsync(
                    "UPDATE dbo.BankAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE BankAccountID=@ID",
                    new { Delta = delta, ID = bankAccountId!.Value }, transaction);
                if (affected != 1)
                    throw new InvalidOperationException($"تعذر إعادة رصيد البنك لسند {voucherNumber}؛ أُلغي الإلغاء.");
                return;
            }
            if (cashBoxId > 0)
            {
                var affected = await connection.ExecuteAsync(
                    "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE CashBoxID=@ID",
                    new { Delta = delta, ID = cashBoxId }, transaction);
                if (affected != 1)
                    throw new InvalidOperationException($"تعذر إعادة رصيد الخزينة لسند {voucherNumber}؛ أُلغي الإلغاء.");
            }
        }

        private async Task GuardAndCancelLinkedChequesAsync(
            SqlConnection connection, SqlTransaction transaction,
            int? receiptId, int? paymentId, string voucherNumber)
        {
            var cheques = (await connection.QueryAsync<(int ChequeID, int ChequeStatus)>(
                @"SELECT ChequeID, ChequeStatus FROM dbo.Cheques WITH (UPDLOCK, HOLDLOCK)
                  WHERE (@ReceiptID IS NOT NULL AND ReceiptID=@ReceiptID)
                     OR (@PaymentID IS NOT NULL AND PaymentID=@PaymentID)",
                new { ReceiptID = receiptId, PaymentID = paymentId }, transaction)).ToList();
            if (cheques.Any(c => c.ChequeStatus == 2 || c.ChequeStatus == 3 || c.ChequeStatus == 4))
                throw new InvalidOperationException($"سند {voucherNumber} مرتبط بشيك خرج من الحافظة (مقدم للبنك أو محصل أو مرتجع)؛ عالج الشيك أولًا قبل إلغاء السند.");
            foreach (var cheque in cheques.Where(c => c.ChequeStatus == 1))
            {
                var updated = await connection.ExecuteAsync(
                    "UPDATE dbo.Cheques SET ChequeStatus=5 WHERE ChequeID=@ID AND ChequeStatus=1",
                    new { ID = cheque.ChequeID }, transaction);
                if (updated != 1)
                    throw new InvalidOperationException("تغيرت حالة شيك مرتبط بالسند أثناء الإلغاء؛ أُلغي الإلغاء.");
            }
        }

        private async Task ReverseReceiptInvoicePaidAmountsAsync(
            IDbConnection connection, IDbTransaction transaction,
            IReadOnlyCollection<VoucherInvoiceAllocationDto> allocations, int userId)
        {
            foreach (var allocation in allocations.OrderBy(x => x.InvoiceID))
            {
                var invoice = await connection.QueryFirstOrDefaultAsync<(decimal TotalAmount, decimal PaidAmount)>(
                    @"SELECT ISNULL(TotalAmount,0) AS TotalAmount, ISNULL(PaidAmount,0) AS PaidAmount
                      FROM dbo.SalesInvoices WITH (UPDLOCK,HOLDLOCK) WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID }, transaction);
                var newPaidAmount = invoice.PaidAmount - allocation.AppliedAmount;
                if (newPaidAmount < 0)
                    throw new InvalidOperationException($"مدفوعات فاتورة المبيعات {allocation.InvoiceNumber} أقل من التخصيص المراد عكسه؛ أُلغي الإلغاء.");
                var paymentStatus = newPaidAmount <= 0 ? 1 : (newPaidAmount >= invoice.TotalAmount ? 3 : 2);
                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices
                      SET PaidAmount=@PaidAmount, PaymentStatus=@PaymentStatus,
                          InvoiceStatus=CASE WHEN InvoiceStatus=4 AND @PaidAmount<@TotalAmount THEN 3 ELSE InvoiceStatus END,
                          ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID, PaidAmount = newPaidAmount, PaymentStatus = paymentStatus, TotalAmount = invoice.TotalAmount, UserID = userId }, transaction);
                if (updated != 1) throw new InvalidOperationException("تعذر عكس مدفوعات فاتورة المبيعات؛ أُلغي الإلغاء.");
            }
        }

        private async Task ReversePaymentInvoicePaidAmountsAsync(
            IDbConnection connection, IDbTransaction transaction,
            IReadOnlyCollection<VoucherInvoiceAllocationDto> allocations, int userId)
        {
            foreach (var allocation in allocations.OrderBy(x => x.InvoiceID))
            {
                var invoice = await connection.QueryFirstOrDefaultAsync<(decimal TotalAmount, decimal PaidAmount)>(
                    @"SELECT ISNULL(TotalAmount,0) AS TotalAmount, ISNULL(PaidAmount,0) AS PaidAmount
                      FROM dbo.PurchaseInvoices WITH (UPDLOCK,HOLDLOCK) WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID }, transaction);
                var newPaidAmount = invoice.PaidAmount - allocation.AppliedAmount;
                if (newPaidAmount < 0)
                    throw new InvalidOperationException($"مدفوعات فاتورة المشتريات {allocation.InvoiceNumber} أقل من التخصيص المراد عكسه؛ أُلغي الإلغاء.");
                var paymentStatus = newPaidAmount <= 0 ? 1 : (newPaidAmount >= invoice.TotalAmount ? 3 : 2);
                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseInvoices
                      SET PaidAmount=@PaidAmount, PaymentStatus=@PaymentStatus,
                          InvoiceStatus=CASE WHEN InvoiceStatus=6 THEN 6
                                             WHEN @PaidAmount<=0 THEN 3
                                             WHEN @PaidAmount>=@TotalAmount THEN 5
                                             ELSE 4 END,
                          ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE InvoiceID=@InvoiceID",
                    new { allocation.InvoiceID, PaidAmount = newPaidAmount, PaymentStatus = paymentStatus, TotalAmount = invoice.TotalAmount, UserID = userId }, transaction);
                if (updated != 1) throw new InvalidOperationException("تعذر عكس مدفوعات فاتورة المشتريات؛ أُلغي الإلغاء.");
            }
        }

        public async Task ApproveReceiptAsync(int id, int userId, int? empId = null)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_RECV", p => p.CanApprove, "ليس لديك صلاحية اعتماد سندات القبض.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var rv = await connection.QueryFirstOrDefaultAsync<(
                    string ReceiptNumber, int ReceiptStatus, decimal Amount, int CashBoxID, int? BankAccountID,
                    int? CreatedBy, int? EntityType, int? EntityID, int? CurrencyID, decimal ExchangeRate,
                    decimal AmountLocal, DateTime ReceiptDate, int? RevenueAccountID, int? CostCenterID,
                    int? JournalID, bool IsPosted)>(
                    @"SELECT ReceiptNumber,ReceiptStatus,Amount,CashBoxID,BankAccountID,CreatedBy,EntityType,EntityID,CurrencyID,
                             ISNULL(ExchangeRate,1) AS ExchangeRate,ISNULL(AmountLocal,Amount) AS AmountLocal,
                             ReceiptDate,RevenueAccountID,CostCenterID,JournalID,ISNULL(IsPosted,0) AS IsPosted
                      FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK) WHERE ReceiptID=@ID",
                    new { ID = id }, trans);
                if (rv.ReceiptNumber == null) throw new Exception("السند غير موجود");
                if (rv.ReceiptStatus != 1) throw new Exception("السند ليس مسودة");
                if (rv.IsPosted || rv.JournalID.GetValueOrDefault() > 0)
                    throw new InvalidOperationException("لا يمكن اعتماد مسودة مرتبطة مسبقاً بقيد أو معلّمة كمرحّلة؛ راجع ارتباطها أولاً.");
                var approverEmployeeId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID=@UserID",
                    new { UserID = userId }, trans);
                if (!approverEmployeeId.HasValue)
                    throw new InvalidOperationException("لا يمكن اعتماد سند القبض لأن المستخدم الحالي غير مرتبط بموظف.");
                if (empId.HasValue && empId.Value != approverEmployeeId.Value)
                    throw new InvalidOperationException("معرّف الموظف الممرر لا يطابق الموظف المرتبط بمستخدم الاعتماد.");

                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, rv.CurrencyID, rv.ExchangeRate);

                var isInvoiceSettlement = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT CONVERT(bit, CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.ReceiptAllocations WHERE ReceiptID=@ID
                      ) THEN 1 ELSE 0 END)", new { ID = id }, trans);
                List<VoucherInvoiceAllocationDto> invoiceAllocations = isInvoiceSettlement
                    ? await GetReceiptInvoiceAllocationsCoreAsync(connection, trans, id)
                    : new List<VoucherInvoiceAllocationDto>();
                int? controlAccountId = null;
                if (isInvoiceSettlement)
                {
                    await _authorization.RequireUserPermissionAsync(
                        userId, "SCR_SINV", p => p.CanView, "يلزم صلاحية عرض فواتير المبيعات لاعتماد تسويتها.");
                    if (rv.EntityType != 1 || !rv.EntityID.HasValue)
                        throw new InvalidOperationException("سند تسوية فواتير المبيعات لا يرتبط بعميل صالح.");

                    controlAccountId = await ValidateInvoiceAllocationsAsync(
                        connection, trans, true, rv.EntityID.Value, rv.CurrencyID, rv.ExchangeRate,
                        rv.Amount, invoiceAllocations, id);
                    await connection.ExecuteAsync(
                        "UPDATE dbo.ReceiptVouchers SET RevenueAccountID=@AccountID WHERE ReceiptID=@ID AND ReceiptStatus=1",
                        new { AccountID = controlAccountId.Value, ID = id }, trans);
                }

                if (!isInvoiceSettlement)
                    await ValidateGenericVoucherCounterAccountAsync(
                        connection, trans, rv.RevenueAccountID, 4,
                        rv.CostCenterID, rv.ReceiptNumber, "حساب الإيراد");

                var sourceAccountId = await ResolveVoucherSourceAccountIdAsync(
                    connection, trans, rv.CashBoxID, rv.BankAccountID, rv.ReceiptNumber);
                await connection.ExecuteAsync("EXEC dbo.sp_PostReceiptVoucher @ReceiptID, @UserID", new { ReceiptID = id, UserID = userId }, trans);

                var postedReceipt = await connection.QueryFirstOrDefaultAsync<(int? JournalID, bool IsPosted)>(
                    "SELECT JournalID,ISNULL(IsPosted,0) AS IsPosted FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK) WHERE ReceiptID=@ID",
                    new { ID = id }, trans);
                if (postedReceipt.JournalID is not int journalId || journalId <= 0 || !postedReceipt.IsPosted)
                    throw new InvalidOperationException("لم ينشئ إجراء الترحيل قيداً مرتبطاً أو لم يعلّم سند القبض كمرحّل؛ أُلغي الاعتماد.");

                int? expectedCounterAccountId = controlAccountId;
                if (!expectedCounterAccountId.HasValue && rv.RevenueAccountID.GetValueOrDefault() > 0)
                    expectedCounterAccountId = rv.RevenueAccountID;
                await VerifyVoucherJournalAsync(
                    connection, trans, journalId, id, rv.ReceiptNumber, rv.ReceiptDate,
                    rv.AmountLocal, sourceAccountId, true, expectedCounterAccountId,
                    isInvoiceSettlement ? "Customer" : null,
                    isInvoiceSettlement ? rv.EntityID : null);

                if (isInvoiceSettlement)
                {
                    await ApplyReceiptInvoicePaidAmountsAsync(connection, trans, invoiceAllocations, userId);

                    // SalesInvoiceService increases this balance when an invoice is approved;
                    // a receipt allocated to receivables must reduce it by the local amount.
                    var customerBalanceUpdated = await connection.ExecuteAsync(
                        @"UPDATE dbo.Customers
                          SET CurrentBalance=ISNULL(CurrentBalance,0)-@BalanceChange,
                              ModifiedBy=@UserID,ModifiedDate=GETDATE()
                          WHERE CustomerID=@CustomerID",
                        new
                        {
                            CustomerID = rv.EntityID!.Value,
                            BalanceChange = rv.AmountLocal,
                            UserID = userId
                        }, trans);
                    if (customerBalanceUpdated != 1)
                        throw new InvalidOperationException("تعذر تحديث رصيد العميل؛ أُلغي اعتماد سند القبض.");
                }

                await ApplyJournalAccountBalancesAsync(connection, trans, journalId);

                var approvedReceipt = await connection.ExecuteAsync(
                    "UPDATE dbo.ReceiptVouchers SET ReceiptStatus=2, ApprovedBy=@EmployeeID, ApprovedDate=GETDATE() WHERE ReceiptID=@ID AND ReceiptStatus=1",
                    new { EmployeeID = approverEmployeeId.Value, ID = id }, trans);
                if (approvedReceipt != 1)
                    throw new InvalidOperationException("تغيرت حالة سند القبض أثناء الاعتماد؛ أُلغيت العملية.");

                trans.Commit();

                await _audit.WriteAuditLogAsync(userId, 2, "ReceiptVouchers", id.ToString(), moduleName: "SCR_RECV", description: $"اعتماد سند قبض: {rv.ReceiptNumber}");
                await CompleteVoucherNotificationsAsync("SCR_RECV", "سند القبض", rv.ReceiptNumber, id, rv.CreatedBy, userId);
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after receipt-voucher approval failed for receipt {ReceiptID}.",
                        id);
                }
                throw;
            }
        }

        public async Task CancelReceiptAsync(int id, int userId, DateTime reversalDate, string? reason)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_RECV", p => p.CanApprove, "ليس لديك صلاحية إلغاء سندات القبض المعتمدة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var rv = await connection.QueryFirstOrDefaultAsync<(
                    string ReceiptNumber, int ReceiptStatus, decimal Amount, int CashBoxID, int? BankAccountID,
                    int PaymentMethod, int? EntityType, int? EntityID,
                    decimal AmountLocal, int? JournalID, bool IsPosted)>(
                    @"SELECT ReceiptNumber, ReceiptStatus, Amount, CashBoxID, BankAccountID, PaymentMethod,
                             EntityType, EntityID, ISNULL(AmountLocal,Amount) AS AmountLocal,
                             JournalID, ISNULL(IsPosted,0) AS IsPosted
                      FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK) WHERE ReceiptID=@ID",
                    new { ID = id }, trans);
                if (rv.ReceiptNumber == null) throw new Exception("السند غير موجود");
                if (rv.ReceiptStatus != 2)
                    throw new Exception("لا يمكن إلغاء سند غير معتمد؛ المسودة تُحذف والملغي لا يُلغى مرة أخرى.");
                if (!rv.IsPosted || rv.JournalID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("السند معتمد دون قيد مرحّل مرتبط؛ أُوقف الإلغاء حفاظًا على الأرصدة.");

                var approverEmployeeId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID=@UserID",
                    new { UserID = userId }, trans);
                if (!approverEmployeeId.HasValue || approverEmployeeId.Value <= 0)
                    throw new InvalidOperationException("لا يمكن إلغاء سند القبض لأن المستخدم الحالي غير مرتبط بموظف.");

                await GuardAndCancelLinkedChequesAsync(connection, trans, id, null, rv.ReceiptNumber);

                var allocations = await GetReceiptInvoiceAllocationsCoreAsync(connection, trans, id);
                var isInvoiceSettlement = allocations.Count > 0;

                await ReversePostedJournalCoreAsync(
                    connection, trans, rv.JournalID!.Value, userId, approverEmployeeId.Value,
                    reversalDate, reason, $"عكس سند قبض {rv.ReceiptNumber}");

                await ReverseLinkedChequeReclassJournalsAsync(
                    connection, trans, id, null, userId, approverEmployeeId.Value,
                    reversalDate, rv.ReceiptNumber);

                await RestoreVoucherTreasuryBalanceAsync(
                    connection, trans, rv.CashBoxID, rv.BankAccountID, rv.PaymentMethod,
                    rv.AmountLocal, true, rv.ReceiptNumber);

                if (isInvoiceSettlement)
                {
                    await ReverseReceiptInvoicePaidAmountsAsync(connection, trans, allocations, userId);
                    if (rv.EntityType == 1 && rv.EntityID.HasValue)
                    {
                        var customerBalanceUpdated = await connection.ExecuteAsync(
                            @"UPDATE dbo.Customers
                              SET CurrentBalance=ISNULL(CurrentBalance,0)+@BalanceChange,
                                  ModifiedBy=@UserID, ModifiedDate=GETDATE()
                              WHERE CustomerID=@CustomerID",
                            new { CustomerID = rv.EntityID.Value, BalanceChange = rv.AmountLocal, UserID = userId }, trans);
                        if (customerBalanceUpdated != 1)
                            throw new InvalidOperationException("تعذر إعادة رصيد العميل؛ أُلغي إلغاء السند.");
                    }
                }

                var cancelled = await connection.ExecuteAsync(
                    "UPDATE dbo.ReceiptVouchers SET ReceiptStatus=3 WHERE ReceiptID=@ID AND ReceiptStatus=2",
                    new { ID = id }, trans);
                if (cancelled != 1)
                    throw new InvalidOperationException("تغيرت حالة سند القبض أثناء الإلغاء؛ أُلغيت العملية.");

                trans.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "ReceiptVouchers", id.ToString(), moduleName: "SCR_RECV", description: $"إلغاء سند قبض معتمد وعكس قيده: {rv.ReceiptNumber}");
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after receipt-voucher cancellation failed for receipt {ReceiptID}.",
                        id);
                }
                throw;
            }
        }

        public async Task DeleteReceiptAsync(int id, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_RECV", p => p.CanDelete, "ليس لديك صلاحية حذف سندات القبض.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT ReceiptStatus FROM dbo.ReceiptVouchers WITH (UPDLOCK, HOLDLOCK) WHERE ReceiptID=@ID",
                    new { ID = id }, trans);
                if (status != 1) throw new Exception("لا يمكن حذف سند معتمد أو غير موجود");

                await connection.ExecuteAsync("DELETE FROM dbo.ReceiptAllocations WHERE ReceiptID=@ID", new { ID = id }, trans);
                await connection.ExecuteAsync("DELETE FROM dbo.ReceiptVouchers WHERE ReceiptID=@ID", new { ID = id }, trans);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling receipt-voucher deletion for receipt {ReceiptID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "ReceiptVouchers", id.ToString(), moduleName: "SCR_RECV", description: "حذف سند قبض");
            await MarkVoucherNotificationsActionedAsync("SCR_RECV", id);
        }

        public async Task<List<PaymentVoucherListDto>> GetPaymentVouchersAsync(DateTime? from = null, DateTime? to = null, int? cashBoxId = null)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_PAYV", p => p.CanView, "ليس لديك صلاحية عرض سندات الصرف.");

            using var connection = CreateConnection();
            var sql = @"SELECT pv.PaymentID, pv.PaymentNumber, pv.PaymentDate, pv.PaidTo, pv.EntityType, pv.EntityID,
                               CASE WHEN pv.EntityType=1 THEN s.SupplierNameAr WHEN pv.EntityType=2 THEN c.CustomerNameAr ELSE pv.PaidTo END AS EntityName,
                               s.SupplierNameAr, c.CustomerNameAr,
                               pv.CashBoxID, cb.CashBoxNameAr, pv.BankAccountID, ba.BankName AS BankAccountName,
                               pv.Amount, ISNULL(pv.AmountLocal, pv.Amount) AS AmountLocal,
                               pv.CurrencyID, cur.CurrencyCode, pv.PaymentMethod,
                               CASE pv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' ELSE N'أخرى' END AS PaymentMethodName,
                               pv.Description, pv.PaymentStatus, CASE pv.PaymentStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                               ISNULL(pv.IsPosted,0) AS IsPosted, pv.JournalID, pv.CreatedDate
                        FROM dbo.PaymentVouchers pv
                        LEFT JOIN dbo.CashBoxes cb ON pv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.BankAccounts ba ON pv.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.Suppliers s ON pv.EntityType=1 AND pv.EntityID=s.SupplierID
                        LEFT JOIN dbo.Customers c ON pv.EntityType=2 AND pv.EntityID=c.CustomerID
                        LEFT JOIN dbo.Currencies cur ON pv.CurrencyID=cur.CurrencyID
                        WHERE 1=1 AND (@From IS NULL OR pv.PaymentDate >= @From) AND (@To IS NULL OR pv.PaymentDate <= @To) AND (@CashBoxID IS NULL OR pv.CashBoxID=@CashBoxID)
                        ORDER BY pv.PaymentDate DESC, pv.PaymentID DESC";
            var result = await connection.QueryAsync<PaymentVoucherListDto>(sql, new { From = from, To = to, CashBoxID = cashBoxId });
            return result.ToList();
        }

        public async Task<PaymentVoucherDto?> GetPaymentByIdAsync(int id)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_PAYV", p => p.CanView, "ليس لديك صلاحية عرض سندات الصرف.");

            using var connection = CreateConnection();
            var sql = @"SELECT pv.PaymentID, pv.PaymentNumber, pv.PaymentDate, pv.PaidTo, pv.EntityType, pv.EntityID,
                               CASE WHEN pv.EntityType=1 THEN s.SupplierNameAr WHEN pv.EntityType=2 THEN c.CustomerNameAr ELSE pv.PaidTo END AS EntityName,
                               pv.CashBoxID, cb.CashBoxNameAr, pv.BankAccountID, ba.BankName AS BankAccountName,
                               pv.Amount, pv.CurrencyID, cur.CurrencyCode, pv.ExchangeRate, ISNULL(pv.AmountLocal, pv.Amount) AS AmountLocal,
                               pv.PaymentMethod, pv.ChequeNumber, pv.ChequeDate, pv.ChequeBankName, pv.TransferRef,
                               pv.Description, pv.ExpenseAccountID, coa.AccountNameAr AS ExpenseAccountNameAr, pv.CostCenterID,
                               pv.PaymentStatus, pv.JournalID, ISNULL(pv.IsPosted,0) AS IsPosted, pv.Notes, pv.CreatedBy, pv.CreatedDate
                        FROM dbo.PaymentVouchers pv
                        LEFT JOIN dbo.CashBoxes cb ON pv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.Suppliers s ON pv.EntityType=1 AND pv.EntityID=s.SupplierID
                        LEFT JOIN dbo.Customers c ON pv.EntityType=2 AND pv.EntityID=c.CustomerID
                        LEFT JOIN dbo.BankAccounts ba ON pv.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.ChartOfAccounts coa ON pv.ExpenseAccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON pv.CurrencyID=cur.CurrencyID
                        WHERE pv.PaymentID=@ID";
            var dto = await connection.QueryFirstOrDefaultAsync<PaymentVoucherDto>(sql, new { ID = id });
            if (dto is not null)
            {
                dto.IsInvoiceSettlement = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT CONVERT(bit, CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.PaymentAllocations WHERE PaymentID=@ID
                      ) THEN 1 ELSE 0 END)", new { ID = id });
                if (dto.IsInvoiceSettlement)
                {
                    try
                    {
                        await _authorization.RequireCurrentUserPermissionAsync(
                            "SCR_PINV", p => p.CanView, "ليس لديك صلاحية عرض فواتير المشتريات.");
                        dto.InvoiceAllocations = await GetPaymentInvoiceAllocationsCoreAsync(connection, null, id);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        dto.InvoiceAllocations = new List<VoucherInvoiceAllocationDto>();
                    }
                }
            }
            return dto;
        }

        public async Task<int> InsertPaymentAsync(PaymentVoucherDto dto, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_PAYV", p => p.CanAdd, "ليس لديك صلاحية إنشاء سند صرف.");

            dto.PaymentNumber = null;
            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            int newId;
            int? recordIdForRollbackLog = null;
            string paymentNumber = string.Empty;
            try
            {
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, dto.CurrencyID, dto.ExchangeRate);
                await EnsureVoucherSourceLocalCurrencyAsync(
                    connection, trans, dto.CashBoxID, dto.BankAccountID, "سند الصرف");
                await PreparePaymentSettlementAsync(connection, trans, dto, userId, null);
                if (string.IsNullOrWhiteSpace(dto.PaidTo))
                {
                    if (dto.EntityType == 1 && dto.EntityID.HasValue)
                        dto.PaidTo = await connection.QueryFirstOrDefaultAsync<string>(
                            "SELECT SupplierNameAr FROM dbo.Suppliers WHERE SupplierID=@ID", new { ID = dto.EntityID }, trans);
                    else if (dto.EntityType == 2 && dto.EntityID.HasValue)
                        dto.PaidTo = await connection.QueryFirstOrDefaultAsync<string>(
                            "SELECT CustomerNameAr FROM dbo.Customers WHERE CustomerID=@ID", new { ID = dto.EntityID }, trans);
                }

                paymentNumber = await _sequence.GetNextCodeAsync(connection, trans, "PV");

                var sql = @"INSERT INTO dbo.PaymentVouchers(PaymentNumber, PaymentDate, PaidTo, EntityType, EntityID, PaymentMethod, CashBoxID, BankAccountID,
                            Amount, CurrencyID, ExchangeRate, AmountLocal, ChequeNumber, ChequeDate, ChequeBankName, TransferRef, Description, ExpenseAccountID, CostCenterID,
                            PaymentStatus, Notes, CreatedBy, CreatedDate)
                            VALUES(@PaymentNumber, @PaymentDate, @PaidTo, @EntityType, NULLIF(@EntityID,0), @PaymentMethod, NULLIF(@CashBoxID,0), NULLIF(@BankAccountID,0),
                            @Amount, NULLIF(@CurrencyID,0), @ExchangeRate, @AmountLocal, @ChequeNumber, @ChequeDate, @ChequeBankName, @TransferRef, @Description,
                            NULLIF(@ExpenseAccountID,0), NULLIF(@CostCenterID,0), 1, @Notes, @CreatedBy, GETDATE());
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";
                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    PaymentNumber = paymentNumber,
                    dto.PaymentDate,
                    dto.PaidTo,
                    dto.EntityType,
                    dto.EntityID,
                    dto.PaymentMethod,
                    dto.CashBoxID,
                    dto.BankAccountID,
                    dto.Amount,
                    dto.CurrencyID,
                    ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                    dto.AmountLocal,
                    dto.ChequeNumber,
                    dto.ChequeDate,
                    dto.ChequeBankName,
                    dto.TransferRef,
                    dto.Description,
                    dto.ExpenseAccountID,
                    dto.CostCenterID,
                    dto.Notes,
                    CreatedBy = userId
                }, trans);
                recordIdForRollbackLog = newId;

                await ReplacePaymentInvoiceAllocationsAsync(
                    connection, trans, newId, dto.InvoiceAllocations);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling payment-voucher creation for payment {PaymentID}.",
                        recordIdForRollbackLog);
                }
                throw;
            }

            dto.PaymentNumber = paymentNumber;
            await _audit.WriteAuditLogAsync(userId, 1, "PaymentVouchers", newId.ToString(), moduleName: "SCR_PAYV", description: $"إنشاء سند صرف: {dto.PaymentNumber}");
            await NotifyVoucherPendingApprovalAsync("SCR_PAYV", "سند الصرف", dto.PaymentNumber ?? newId.ToString(), dto.Amount, newId, userId);
            return newId;
        }

        public async Task UpdatePaymentAsync(PaymentVoucherDto dto, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_PAYV", p => p.CanEdit, "ليس لديك صلاحية تعديل سندات الصرف.");

            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(
                    int? PaymentStatus, int? CurrencyID, decimal ExchangeRate,
                    int? CashBoxID, int? BankAccountID)>(
                    @"SELECT PaymentStatus, CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate,
                             CashBoxID, BankAccountID
                      FROM dbo.PaymentVouchers WITH (UPDLOCK, HOLDLOCK) WHERE PaymentID=@ID",
                    new { ID = dto.PaymentID }, trans);
                if (current.PaymentStatus != 1) throw new Exception("لا يمكن تعديل سند معتمد أو غير موجود");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, current.CurrencyID, current.ExchangeRate);
                await EnsureVoucherSourceLocalCurrencyAsync(
                    connection, trans, current.CashBoxID.GetValueOrDefault(), current.BankAccountID, "سند الصرف");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, dto.CurrencyID, dto.ExchangeRate);
                await EnsureVoucherSourceLocalCurrencyAsync(
                    connection, trans, dto.CashBoxID, dto.BankAccountID, "سند الصرف");

                await PreparePaymentSettlementAsync(connection, trans, dto, userId, dto.PaymentID);
                var sql = @"UPDATE dbo.PaymentVouchers SET PaymentDate=@PaymentDate, PaidTo=@PaidTo, EntityType=@EntityType, EntityID=NULLIF(@EntityID,0),
                            PaymentMethod=@PaymentMethod, CashBoxID=NULLIF(@CashBoxID,0), BankAccountID=NULLIF(@BankAccountID,0), Amount=@Amount, CurrencyID=NULLIF(@CurrencyID,0),
                            ExchangeRate=@ExchangeRate, AmountLocal=@AmountLocal, ChequeNumber=@ChequeNumber, ChequeDate=@ChequeDate, ChequeBankName=@ChequeBankName,
                            TransferRef=@TransferRef, Description=@Description, ExpenseAccountID=NULLIF(@ExpenseAccountID,0), CostCenterID=NULLIF(@CostCenterID,0),
                            Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                            WHERE PaymentID=@PaymentID";
                await connection.ExecuteAsync(sql, new
                {
                    dto.PaymentID,
                    dto.PaymentDate,
                    dto.PaidTo,
                    dto.EntityType,
                    dto.EntityID,
                    dto.PaymentMethod,
                    dto.CashBoxID,
                    dto.BankAccountID,
                    dto.Amount,
                    dto.CurrencyID,
                    ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                    dto.AmountLocal,
                    dto.ChequeNumber,
                    dto.ChequeDate,
                    dto.ChequeBankName,
                    dto.TransferRef,
                    dto.Description,
                    dto.ExpenseAccountID,
                    dto.CostCenterID,
                    dto.Notes,
                    UserID = userId
                }, trans);
                await ReplacePaymentInvoiceAllocationsAsync(
                    connection, trans, dto.PaymentID, dto.InvoiceAllocations);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling payment-voucher update for payment {PaymentID}.",
                        dto.PaymentID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "PaymentVouchers", dto.PaymentID.ToString(), moduleName: "SCR_PAYV", description: $"تعديل سند صرف: {dto.PaymentNumber}");
        }

        public async Task ApprovePaymentAsync(int id, int userId, int? empId = null)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_PAYV", p => p.CanApprove, "ليس لديك صلاحية اعتماد سندات الصرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var pv = await connection.QueryFirstOrDefaultAsync<(
                    string PaymentNumber, int PaymentStatus, int CashBoxID, int? BankAccountID, int? CreatedBy,
                    int? EntityType, int? EntityID, int? CurrencyID, decimal ExchangeRate, decimal Amount,
                    decimal AmountLocal, DateTime PaymentDate, int? ExpenseAccountID, int? CostCenterID,
                    int? JournalID, bool IsPosted)>(
                    @"SELECT PaymentNumber,PaymentStatus,CashBoxID,BankAccountID,CreatedBy,EntityType,EntityID,CurrencyID,
                             ISNULL(ExchangeRate,1) AS ExchangeRate,Amount,ISNULL(AmountLocal,Amount) AS AmountLocal,
                             PaymentDate,ExpenseAccountID,CostCenterID,JournalID,ISNULL(IsPosted,0) AS IsPosted
                      FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK) WHERE PaymentID=@ID",
                    new { ID = id }, trans);
                if (pv.PaymentNumber == null) throw new Exception("السند غير موجود");
                if (pv.PaymentStatus != 1) throw new Exception("السند ليس مسودة");
                if (pv.IsPosted || pv.JournalID.GetValueOrDefault() > 0)
                    throw new InvalidOperationException("لا يمكن اعتماد مسودة مرتبطة مسبقاً بقيد أو معلّمة كمرحّلة؛ راجع ارتباطها أولاً.");

                var approverEmployeeId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID=@UserID",
                    new { UserID = userId }, trans);
                if (!approverEmployeeId.HasValue)
                    throw new InvalidOperationException("لا يمكن اعتماد سند الصرف لأن المستخدم الحالي غير مرتبط بموظف.");
                if (empId.HasValue && empId.Value != approverEmployeeId.Value)
                    throw new InvalidOperationException("معرّف الموظف الممرر لا يطابق الموظف المرتبط بمستخدم الاعتماد.");

                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, pv.CurrencyID, pv.ExchangeRate);

                var isInvoiceSettlement = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT CONVERT(bit, CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.PaymentAllocations WHERE PaymentID=@ID
                      ) THEN 1 ELSE 0 END)", new { ID = id }, trans);
                List<VoucherInvoiceAllocationDto> invoiceAllocations = isInvoiceSettlement
                    ? await GetPaymentInvoiceAllocationsCoreAsync(connection, trans, id)
                    : new List<VoucherInvoiceAllocationDto>();
                int? controlAccountId = null;
                var invoiceSettlementSupplierId = 0;
                if (isInvoiceSettlement)
                {
                    await _authorization.RequireUserPermissionAsync(
                        userId, "SCR_PINV", p => p.CanView, "يلزم صلاحية عرض فواتير المشتريات لاعتماد تسويتها.");
                    invoiceSettlementSupplierId = pv.EntityID.GetValueOrDefault();
                    if (pv.EntityType != 1 || invoiceSettlementSupplierId <= 0)
                        throw new InvalidOperationException("سند تسوية فواتير المشتريات لا يرتبط بمورد صالح.");

                    controlAccountId = await ValidateInvoiceAllocationsAsync(
                        connection, trans, false, invoiceSettlementSupplierId, pv.CurrencyID, pv.ExchangeRate,
                        pv.Amount, invoiceAllocations, id);
                    await connection.ExecuteAsync(
                        "UPDATE dbo.PaymentVouchers SET ExpenseAccountID=@AccountID WHERE PaymentID=@ID AND PaymentStatus=1",
                        new { AccountID = controlAccountId.Value, ID = id }, trans);
                }

                if (!isInvoiceSettlement)
                    await ValidateGenericVoucherCounterAccountAsync(
                        connection, trans, pv.ExpenseAccountID, 6,
                        pv.CostCenterID, pv.PaymentNumber, "حساب المصروف");

                var sourceAccountId = await ResolveVoucherSourceAccountIdAsync(
                    connection, trans, pv.CashBoxID, pv.BankAccountID, pv.PaymentNumber);
                await connection.ExecuteAsync("EXEC dbo.sp_PostPaymentVoucher @PaymentID, @UserID", new { PaymentID = id, UserID = userId }, trans);

                var postedPayment = await connection.QueryFirstOrDefaultAsync<(int? JournalID, bool IsPosted)>(
                    "SELECT JournalID,ISNULL(IsPosted,0) AS IsPosted FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK) WHERE PaymentID=@ID",
                    new { ID = id }, trans);
                if (postedPayment.JournalID is not int journalId || journalId <= 0 || !postedPayment.IsPosted)
                    throw new InvalidOperationException("لم ينشئ إجراء الترحيل قيداً مرتبطاً أو لم يعلّم سند الصرف كمرحّل؛ أُلغي الاعتماد.");

                int? expectedCounterAccountId = controlAccountId;
                if (!expectedCounterAccountId.HasValue && pv.ExpenseAccountID.GetValueOrDefault() > 0)
                    expectedCounterAccountId = pv.ExpenseAccountID;
                await VerifyVoucherJournalAsync(
                    connection, trans, journalId, id, pv.PaymentNumber, pv.PaymentDate,
                    pv.AmountLocal, sourceAccountId, false, expectedCounterAccountId,
                    isInvoiceSettlement ? "Supplier" : null,
                    isInvoiceSettlement ? pv.EntityID : null);

                if (isInvoiceSettlement)
                {
                    await ApplyPaymentInvoicePaidAmountsAsync(connection, trans, invoiceAllocations, userId);
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_UpdateSupplierBalanceAfterInvoice @SupplierID",
                        new { SupplierID = invoiceSettlementSupplierId }, trans);
                }

                await ApplyJournalAccountBalancesAsync(connection, trans, journalId);

                var approvedPayment = await connection.ExecuteAsync(
                    "UPDATE dbo.PaymentVouchers SET PaymentStatus=2, ApprovedBy=@EmployeeID, ApprovedDate=GETDATE() WHERE PaymentID=@ID AND PaymentStatus=1",
                    new { EmployeeID = approverEmployeeId.Value, ID = id }, trans);
                if (approvedPayment != 1)
                    throw new InvalidOperationException("تغيرت حالة سند الصرف أثناء الاعتماد؛ أُلغيت العملية.");
                trans.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "PaymentVouchers", id.ToString(), moduleName: "SCR_PAYV", description: $"اعتماد سند صرف: {pv.PaymentNumber}");
                await CompleteVoucherNotificationsAsync("SCR_PAYV", "سند الصرف", pv.PaymentNumber, id, pv.CreatedBy, userId);
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after payment-voucher approval failed for payment {PaymentID}.",
                        id);
                }
                throw;
            }
        }

        public async Task CancelPaymentAsync(int id, int userId, DateTime reversalDate, string? reason)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_PAYV", p => p.CanApprove, "ليس لديك صلاحية إلغاء سندات الصرف المعتمدة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var pv = await connection.QueryFirstOrDefaultAsync<(
                    string PaymentNumber, int PaymentStatus, int CashBoxID, int? BankAccountID,
                    int PaymentMethod, int? EntityType, int? EntityID,
                    decimal AmountLocal, int? JournalID, bool IsPosted)>(
                    @"SELECT PaymentNumber, PaymentStatus, CashBoxID, BankAccountID, PaymentMethod,
                             EntityType, EntityID, ISNULL(AmountLocal,Amount) AS AmountLocal,
                             JournalID, ISNULL(IsPosted,0) AS IsPosted
                      FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK) WHERE PaymentID=@ID",
                    new { ID = id }, trans);
                if (pv.PaymentNumber == null) throw new Exception("السند غير موجود");
                if (pv.PaymentStatus != 2)
                    throw new Exception("لا يمكن إلغاء سند غير معتمد؛ المسودة تُحذف والملغي لا يُلغى مرة أخرى.");
                if (!pv.IsPosted || pv.JournalID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("السند معتمد دون قيد مرحّل مرتبط؛ أُوقف الإلغاء حفاظًا على الأرصدة.");

                var approverEmployeeId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID=@UserID",
                    new { UserID = userId }, trans);
                if (!approverEmployeeId.HasValue || approverEmployeeId.Value <= 0)
                    throw new InvalidOperationException("لا يمكن إلغاء سند الصرف لأن المستخدم الحالي غير مرتبط بموظف.");

                await GuardAndCancelLinkedChequesAsync(connection, trans, null, id, pv.PaymentNumber);

                var allocations = await GetPaymentInvoiceAllocationsCoreAsync(connection, trans, id);
                var isInvoiceSettlement = allocations.Count > 0;

                await ReversePostedJournalCoreAsync(
                    connection, trans, pv.JournalID!.Value, userId, approverEmployeeId.Value,
                    reversalDate, reason, $"عكس سند صرف {pv.PaymentNumber}");

                await ReverseLinkedChequeReclassJournalsAsync(
                    connection, trans, null, id, userId, approverEmployeeId.Value,
                    reversalDate, pv.PaymentNumber);

                await RestoreVoucherTreasuryBalanceAsync(
                    connection, trans, pv.CashBoxID, pv.BankAccountID, pv.PaymentMethod,
                    pv.AmountLocal, false, pv.PaymentNumber);

                if (isInvoiceSettlement)
                {
                    await ReversePaymentInvoicePaidAmountsAsync(connection, trans, allocations, userId);
                    if (pv.EntityType == 1 && pv.EntityID.GetValueOrDefault() > 0)
                    {
                        // رصيد المورد يُعاد حسابه من مستنداته بالإجراء نفسه المستخدم عند الاعتماد.
                        await connection.ExecuteAsync(
                            "EXEC dbo.sp_UpdateSupplierBalanceAfterInvoice @SupplierID",
                            new { SupplierID = pv.EntityID!.Value }, trans);
                    }
                }

                var cancelled = await connection.ExecuteAsync(
                    "UPDATE dbo.PaymentVouchers SET PaymentStatus=3 WHERE PaymentID=@ID AND PaymentStatus=2",
                    new { ID = id }, trans);
                if (cancelled != 1)
                    throw new InvalidOperationException("تغيرت حالة سند الصرف أثناء الإلغاء؛ أُلغيت العملية.");

                trans.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "PaymentVouchers", id.ToString(), moduleName: "SCR_PAYV", description: $"إلغاء سند صرف معتمد وعكس قيده: {pv.PaymentNumber}");
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after payment-voucher cancellation failed for payment {PaymentID}.",
                        id);
                }
                throw;
            }
        }

        public async Task DeletePaymentAsync(int id, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_PAYV", p => p.CanDelete, "ليس لديك صلاحية حذف سندات الصرف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT PaymentStatus FROM dbo.PaymentVouchers WITH (UPDLOCK, HOLDLOCK) WHERE PaymentID=@ID",
                    new { ID = id }, trans);
                if (status != 1) throw new Exception("لا يمكن حذف سند معتمد أو غير موجود");

                await connection.ExecuteAsync("DELETE FROM dbo.PaymentAllocations WHERE PaymentID=@ID", new { ID = id }, trans);
                await connection.ExecuteAsync("DELETE FROM dbo.PaymentVouchers WHERE PaymentID=@ID", new { ID = id }, trans);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling payment-voucher deletion for payment {PaymentID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "PaymentVouchers", id.ToString(), moduleName: "SCR_PAYV", description: "حذف سند صرف");
            await MarkVoucherNotificationsActionedAsync("SCR_PAYV", id);
        }

        public async Task<List<CashTransferListDto>> GetTransfersAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض تحويلات الخزينة.", p => p.CanView, "SCR_CASHTRANS");

            var sql = @"SELECT ct.TransferID, ct.TransferNo, ct.TransferDate, ct.FromCashBoxID, fcb.CashBoxNameAr AS FromCashBoxNameAr,
                        ct.ToCashBoxID, tcb.CashBoxNameAr AS ToCashBoxNameAr, ct.Amount, ct.Description, ct.Status,
                        CASE ct.Status WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' ELSE N'ملغي' END AS StatusName, ct.IsPosted, ct.CreatedDate
                        FROM dbo.CashTransfers ct
                        LEFT JOIN dbo.CashBoxes fcb ON ct.FromCashBoxID=fcb.CashBoxID
                        LEFT JOIN dbo.CashBoxes tcb ON ct.ToCashBoxID=tcb.CashBoxID
                        ORDER BY ct.TransferDate DESC, ct.TransferID DESC";
            try
            {
                using var connection = CreateConnection();
                var result = await connection.QueryAsync<CashTransferListDto>(sql);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to load the cash-transfer list; propagating the list-read error.");
                throw;
            }
        }

        public async Task<string> GenerateTransferNoAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية إنشاء تحويل خزينة.", p => p.CanAdd, "SCR_CASHTRANS");

            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'CT', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً للتحويل.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم التحويل. لم يتم إنشاء رقم بديل؛ راجع إعداد تسلسل CT والإجراء المخزن.", ex);
            }
        }

        public async Task<int> InsertTransferAsync(CashTransferDto dto, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية إنشاء تحويل خزينة.", p => p.CanAdd, "SCR_CASHTRANS");

            using var connection = CreateConnection();
            await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                connection, null, dto.CurrencyID, dto.ExchangeRate);
            await EnsureCashBoxLocalCurrencyAsync(
                connection, null, dto.FromCashBoxID, "تحويل الخزينة المصدر");
            await EnsureCashBoxLocalCurrencyAsync(
                connection, null, dto.ToCashBoxID, "تحويل الخزينة الوجهة");
            if (string.IsNullOrWhiteSpace(dto.TransferNo))
                dto.TransferNo = await GenerateTransferNoAsync();
            if (dto.FromCashBoxID == dto.ToCashBoxID) throw new Exception("لا يمكن التحويل لنفس الخزينة");
            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);

            var sql = @"INSERT INTO dbo.CashTransfers(TransferNo, TransferDate, FromCashBoxID, ToCashBoxID, Amount, CurrencyID, ExchangeRate, AmountLocal, Description, Status, CreatedBy, CreatedDate)
                        VALUES(@TransferNo, @TransferDate, @FromCashBoxID, @ToCashBoxID, @Amount, NULLIF(@CurrencyID,0), @ExchangeRate, @AmountLocal, @Description, 1, @CreatedBy, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.TransferNo,
                dto.TransferDate,
                dto.FromCashBoxID,
                dto.ToCashBoxID,
                dto.Amount,
                dto.CurrencyID,
                ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                dto.AmountLocal,
                dto.Description,
                CreatedBy = userId
            });
            await _audit.WriteAuditLogAsync(userId, 1, "CashTransfers", newId.ToString(), moduleName: "SCR_BANKS", description: $"تحويل خزينة: {dto.TransferNo}");
            return newId;
        }

        public async Task ApproveTransferAsync(int id, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية اعتماد تحويل الخزينة.", p => p.CanApprove, "SCR_CASHTRANS");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var tr = await connection.QueryFirstOrDefaultAsync<(
                    string? TransferNo, int? Status, int? FromID, int? ToID, decimal? AmountLocal,
                    DateTime? TransferDate, int? CurrencyID, decimal ExchangeRate, int? JournalID, bool IsPosted)>(
                    @"SELECT TransferNo, Status, FromCashBoxID AS FromID, ToCashBoxID AS ToID, AmountLocal, TransferDate,
                             CurrencyID, ISNULL(ExchangeRate,1) AS ExchangeRate,
                             JournalID, ISNULL(IsPosted,0) AS IsPosted
                      FROM dbo.CashTransfers WITH (UPDLOCK, HOLDLOCK) WHERE TransferID=@ID",
                    new { ID = id }, trans);
                if (string.IsNullOrWhiteSpace(tr.TransferNo) || !tr.Status.HasValue)
                    throw new Exception("التحويل غير موجود.");
                if (tr.Status.Value != 1 || tr.IsPosted || tr.JournalID.GetValueOrDefault() > 0)
                    throw new Exception("التحويل ليس مسودة غير مرحّلة.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, tr.CurrencyID, tr.ExchangeRate);
                if (!tr.FromID.HasValue || !tr.ToID.HasValue || tr.FromID.Value <= 0 || tr.ToID.Value <= 0
                    || tr.FromID.Value == tr.ToID.Value)
                    throw new InvalidOperationException("مصدر التحويل ووجهته غير صالحين أو متطابقين.");
                if (!tr.AmountLocal.HasValue || tr.AmountLocal.Value <= 0m)
                    throw new InvalidOperationException("قيمة التحويل المحلي يجب أن تكون أكبر من صفر.");
                if (!tr.TransferDate.HasValue)
                    throw new InvalidOperationException("لا يمكن اعتماد تحويل بلا تاريخ.");

                var approverEmployeeId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID=@UserID",
                    new { UserID = userId }, trans);
                if (!approverEmployeeId.HasValue || approverEmployeeId.Value <= 0)
                    throw new InvalidOperationException("لا يمكن اعتماد تحويل الخزينة لأن المستخدم الحالي غير مرتبط بموظف.");

                var transferDate = tr.TransferDate.Value.Date;
                var accountingPeriod = await connection.QueryFirstOrDefaultAsync<(
                    int? FiscalYearID, int? PeriodID, int? YearStatus, DateTime? YearStart, DateTime? YearEnd,
                    int? PeriodStatus, DateTime? PeriodStart, DateTime? PeriodEnd)>(
                    @"SELECT TOP 1 fy.FiscalYearID, ap.PeriodID, fy.YearStatus, fy.StartDate AS YearStart, fy.EndDate AS YearEnd,
                             ap.PeriodStatus, ap.StartDate AS PeriodStart, ap.EndDate AS PeriodEnd
                      FROM dbo.FiscalYears fy WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK, HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
                      WHERE fy.YearStatus=1 AND ap.PeriodStatus=1
                        AND @TransferDate BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
                        AND @TransferDate BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
                      ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
                    new { TransferDate = transferDate }, trans);
                if (!accountingPeriod.FiscalYearID.HasValue || !accountingPeriod.PeriodID.HasValue
                    || !accountingPeriod.YearStatus.HasValue || accountingPeriod.YearStatus.Value != 1
                    || !accountingPeriod.PeriodStatus.HasValue || accountingPeriod.PeriodStatus.Value != 1
                    || !accountingPeriod.YearStart.HasValue || !accountingPeriod.YearEnd.HasValue
                    || !accountingPeriod.PeriodStart.HasValue || !accountingPeriod.PeriodEnd.HasValue)
                    throw new InvalidOperationException("لا يمكن اعتماد التحويل دون سنة وفترة مفتوحتين تغطيان تاريخ التحويل نفسه.");

                var fromBox = await connection.QueryFirstOrDefaultAsync<(
                    int? AccountID, decimal? CurrentBalance, bool CostCenterRequired, int? CurrencyID)>(
                    @"SELECT TOP 1 coa.AccountID, ISNULL(cb.CurrentBalance,0) AS CurrentBalance,
                             CONVERT(bit,ISNULL(coa.CostCenterRequired,0)) AS CostCenterRequired, cb.CurrencyID
                      FROM dbo.CashBoxes cb WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK, HOLDLOCK) ON coa.AccountID=cb.AccountID
                      WHERE cb.CashBoxID=@ID AND ISNULL(cb.IsActive,1)=1 AND coa.IsActive=1
                        AND coa.IsDetailAccount=1 AND coa.AccountType=1",
                    new { ID = tr.FromID.Value }, trans);
                var toBox = await connection.QueryFirstOrDefaultAsync<(
                    int? AccountID, decimal? CurrentBalance, bool CostCenterRequired, int? CurrencyID)>(
                    @"SELECT TOP 1 coa.AccountID, ISNULL(cb.CurrentBalance,0) AS CurrentBalance,
                             CONVERT(bit,ISNULL(coa.CostCenterRequired,0)) AS CostCenterRequired, cb.CurrencyID
                      FROM dbo.CashBoxes cb WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK, HOLDLOCK) ON coa.AccountID=cb.AccountID
                      WHERE cb.CashBoxID=@ID AND ISNULL(cb.IsActive,1)=1 AND coa.IsActive=1
                        AND coa.IsDetailAccount=1 AND coa.AccountType=1",
                    new { ID = tr.ToID.Value }, trans);
                if (!fromBox.AccountID.HasValue || !toBox.AccountID.HasValue)
                    throw new InvalidOperationException("الخزينة المصدر أو الوجهة غير نشطة أو غير مرتبطة بحساب أصل تفصيلي صالح.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, fromBox.CurrencyID, 1m);
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, toBox.CurrencyID, 1m);
                if (fromBox.CostCenterRequired || toBox.CostCenterRequired)
                    throw new InvalidOperationException("حساب الخزينة المصدر أو الوجهة يتطلب مركز تكلفة، لكن سجل التحويل لا يحمل مركزًا؛ لم يتم الاعتماد.");
                if (fromBox.CurrentBalance.GetValueOrDefault() < tr.AmountLocal.Value)
                    throw new InvalidOperationException("رصيد خزينة المصدر غير كافٍ لاعتماد هذا التحويل.");

                var fromUpdated = await connection.ExecuteAsync(
                    "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amount WHERE CashBoxID=@ID",
                    new { Amount = tr.AmountLocal.Value, ID = tr.FromID.Value }, trans);
                var toUpdated = await connection.ExecuteAsync(
                    "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amount WHERE CashBoxID=@ID",
                    new { Amount = tr.AmountLocal.Value, ID = tr.ToID.Value }, trans);
                if (fromUpdated != 1 || toUpdated != 1)
                    throw new InvalidOperationException("تعذر تحديث رصيد إحدى الخزائن؛ أُلغيت عملية التحويل.");

                var journalNumberSql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;";
                var jNo = await connection.QueryFirstOrDefaultAsync<string>(journalNumberSql, transaction: trans);
                if (string.IsNullOrWhiteSpace(jNo))
                    throw new InvalidOperationException("تعذر توليد رقم القيد؛ سيُلغى ترحيل التحويل بالكامل.");

                var jId = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, FiscalYearID, PeriodID, Description, SourceDocType, SourceDocID,
                             SourceDocNumber, TotalDebit, TotalCredit, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate)
                      VALUES(@No, @Date, @FiscalYearID, @PeriodID, @Desc, 'CashTransfer', @RefID, @RefNo, @Amt, @Amt, 2, 1, @UserID, GETDATE());
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        No = jNo,
                        Date = transferDate,
                        accountingPeriod.FiscalYearID,
                        accountingPeriod.PeriodID,
                        Desc = $"تحويل خزينة {tr.TransferNo}",
                        RefID = id,
                        RefNo = tr.TransferNo,
                        Amt = tr.AmountLocal.Value,
                        UserID = userId
                    }, trans);

                await connection.ExecuteAsync(
                    "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description) VALUES(@JID, 1, @AccountID, @Amount, 0, @Description)",
                    new { JID = jId, AccountID = toBox.AccountID.Value, Amount = tr.AmountLocal.Value, Description = $"تحويل وارد {tr.TransferNo}" }, trans);
                await connection.ExecuteAsync(
                    "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description) VALUES(@JID, 2, @AccountID, 0, @Amount, @Description)",
                    new { JID = jId, AccountID = fromBox.AccountID.Value, Amount = tr.AmountLocal.Value, Description = $"تحويل صادر {tr.TransferNo}" }, trans);

                async Task ApplyTransferAccountDeltaAsync(int accountId, decimal delta)
                {
                    if (delta == 0m) return;
                    var affected = await connection.ExecuteAsync(
                        "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                        new { Delta = delta, ID = accountId }, trans);
                    if (affected != 1) throw new InvalidOperationException("تعذر تحديث رصيد حساب التحويل؛ أُلغيت العملية.");
                    int? currentAccountId = accountId;
                    for (var depth = 0; depth < 10; depth++)
                    {
                        var parentId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID",
                            new { ID = currentAccountId }, trans);
                        if (!parentId.HasValue || parentId.Value <= 0) break;
                        var parentAffected = await connection.ExecuteAsync(
                            "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                            new { Delta = delta, ID = parentId.Value }, trans);
                        if (parentAffected != 1) throw new InvalidOperationException("تعذر تحديث رصيد حساب الأب؛ أُلغيت العملية.");
                        currentAccountId = parentId.Value;
                    }
                }
                var accountDeltas = new Dictionary<int, decimal>();
                accountDeltas[toBox.AccountID.Value] = tr.AmountLocal.Value;
                if (accountDeltas.ContainsKey(fromBox.AccountID.Value))
                    accountDeltas[fromBox.AccountID.Value] -= tr.AmountLocal.Value;
                else
                    accountDeltas[fromBox.AccountID.Value] = -tr.AmountLocal.Value;
                foreach (var accountDelta in accountDeltas.OrderBy(x => x.Key))
                    await ApplyTransferAccountDeltaAsync(accountDelta.Key, accountDelta.Value);

                var postedTransfer = await connection.ExecuteAsync(
                    @"UPDATE dbo.CashTransfers SET JournalID=@JournalID, IsPosted=1, Status=2,
                             ApprovedBy=@EmployeeID, ApprovedDate=GETDATE()
                      WHERE TransferID=@ID AND Status=1 AND ISNULL(IsPosted,0)=0 AND JournalID IS NULL",
                    new { JournalID = jId, UserID = userId, EmployeeID = approverEmployeeId.Value, ID = id }, trans);
                if (postedTransfer != 1)
                    throw new InvalidOperationException("تغيرت حالة التحويل أثناء الاعتماد؛ أُلغيت العملية.");
                trans.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "CashTransfers", id.ToString(), moduleName: "SCR_BANKS", description: $"اعتماد تحويل: {tr.TransferNo}");
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after cash-transfer approval failed for transfer {TransferID}.",
                        id);
                }
                throw;
            }
        }

        public async Task CancelTransferAsync(int id, int userId, DateTime reversalDate, string? reason)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية إلغاء تحويلات الخزينة المعتمدة.", p => p.CanApprove, "SCR_CASHTRANS");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var tr = await connection.QueryFirstOrDefaultAsync<(
                    string? TransferNo, int? Status, int? FromID, int? ToID, decimal? AmountLocal,
                    int? JournalID, bool IsPosted)>(
                    @"SELECT TransferNo, Status, FromCashBoxID AS FromID, ToCashBoxID AS ToID, AmountLocal,
                             JournalID, ISNULL(IsPosted,0) AS IsPosted
                      FROM dbo.CashTransfers WITH (UPDLOCK, HOLDLOCK) WHERE TransferID=@ID",
                    new { ID = id }, trans);
                if (string.IsNullOrWhiteSpace(tr.TransferNo) || !tr.Status.HasValue)
                    throw new Exception("التحويل غير موجود.");
                if (tr.Status.Value != 2)
                    throw new Exception("لا يمكن إلغاء تحويل غير معتمد؛ المسودة تُحذف والملغي لا يُلغى مرة أخرى.");
                if (!tr.IsPosted || tr.JournalID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("التحويل معتمد دون قيد مرحّل مرتبط؛ أُوقف الإلغاء حفاظًا على الأرصدة.");
                if (!tr.FromID.HasValue || !tr.ToID.HasValue || !tr.AmountLocal.HasValue)
                    throw new InvalidOperationException("بيانات التحويل غير مكتملة؛ أُوقف الإلغاء.");

                var employeeId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID=@UserID",
                    new { UserID = userId }, trans);
                if (!employeeId.HasValue || employeeId.Value <= 0)
                    throw new InvalidOperationException("لا يمكن إلغاء التحويل لأن المستخدم الحالي غير مرتبط بموظف.");

                await ReversePostedJournalCoreAsync(
                    connection, trans, tr.JournalID!.Value, userId, employeeId.Value,
                    reversalDate, reason, $"عكس تحويل خزينة {tr.TransferNo}");

                var fromUpdated = await connection.ExecuteAsync(
                    "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amount WHERE CashBoxID=@ID",
                    new { Amount = tr.AmountLocal.Value, ID = tr.FromID.Value }, trans);
                var toUpdated = await connection.ExecuteAsync(
                    "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amount WHERE CashBoxID=@ID",
                    new { Amount = tr.AmountLocal.Value, ID = tr.ToID.Value }, trans);
                if (fromUpdated != 1 || toUpdated != 1)
                    throw new InvalidOperationException("تعذر إعادة رصيد إحدى الخزائن؛ أُلغي إلغاء التحويل.");

                var cancelled = await connection.ExecuteAsync(
                    "UPDATE dbo.CashTransfers SET Status=3 WHERE TransferID=@ID AND Status=2",
                    new { ID = id }, trans);
                if (cancelled != 1)
                    throw new InvalidOperationException("تغيرت حالة التحويل أثناء الإلغاء؛ أُلغي الإلغاء.");

                trans.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "CashTransfers", id.ToString(), moduleName: "SCR_BANKS", description: $"إلغاء تحويل خزينة معتمد وعكس قيده: {tr.TransferNo}");
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after cash-transfer cancellation failed for transfer {TransferID}.",
                        id);
                }
                throw;
            }
        }

        public async Task DeleteTransferAsync(int id, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية حذف تحويلات الخزينة.", p => p.CanDelete, "SCR_CASHTRANS");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT Status FROM dbo.CashTransfers WITH (UPDLOCK, HOLDLOCK) WHERE TransferID=@ID",
                    new { ID = id }, trans);
                if (status != 1) throw new Exception("لا يمكن حذف تحويل معتمد أو غير موجود");

                await connection.ExecuteAsync("DELETE FROM dbo.CashTransfers WHERE TransferID=@ID", new { ID = id }, trans);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cash-transfer deletion for transfer {TransferID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "CashTransfers", id.ToString(), moduleName: "SCR_BANKS", description: "حذف تحويل خزينة");
        }

        public async Task<List<CashBoxStatementDto>> GetCashBoxStatementAsync(int cashBoxId, DateTime? from, DateTime? to)
        {
            await _authorization.RequireCurrentCashBoxStatementViewAsync(
                "ليس لديك صلاحية عرض كشف حساب الخزينة.");

            if (cashBoxId <= 0)
                throw new ArgumentOutOfRangeException(nameof(cashBoxId), "يجب تحديد خزينة صالحة لعرض الكشف.");

            using var connection = CreateConnection();
            var toExclusive = to.HasValue ? to.Value.Date.AddDays(1) : (DateTime?)null;

            // اقرأ الحركات من الجداول المصدر مباشرة؛ لا تعتمد على اكتمال vw_CashBoxStatement.
            // تُقرأ الحركات السابقة لبداية الفترة أيضاً لحساب رصيد جارٍ صحيح، ثم تُخفى عن قائمة العرض.
            var sql = @"
                SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
                       rv.ReceiptDate AS TransactionDate, rv.ReceiptNumber AS TransactionNo, rv.ReceiptID AS TransactionID,
                       'Receipt' AS TransactionType, N'سند قبض' AS TransactionTypeAr,
                       ISNULL(rv.Amount,0) AS DebitAmount, 0 AS CreditAmount,
                       rv.Description, rv.ReceiptStatus AS Status, rv.ReceivedFrom AS EntityName, 0 AS Balance
                FROM dbo.CashBoxes cb
                INNER JOIN dbo.ReceiptVouchers rv ON rv.CashBoxID=cb.CashBoxID
                WHERE cb.CashBoxID=@CBID AND rv.ReceiptStatus=2
                  AND (@ToExclusive IS NULL OR rv.ReceiptDate < @ToExclusive)

                UNION ALL

                SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
                       pv.PaymentDate, pv.PaymentNumber, pv.PaymentID AS TransactionID,
                       'Payment', N'سند صرف',
                       0, ISNULL(pv.Amount,0),
                       pv.Description, pv.PaymentStatus, pv.PaidTo, 0
                FROM dbo.CashBoxes cb
                INNER JOIN dbo.PaymentVouchers pv ON pv.CashBoxID=cb.CashBoxID
                WHERE cb.CashBoxID=@CBID AND pv.PaymentStatus=2
                  AND (@ToExclusive IS NULL OR pv.PaymentDate < @ToExclusive)

                UNION ALL

                SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
                       oe.ExpenseDate, oe.ExpenseNumber, oe.ExpenseID AS TransactionID,
                       'Expense', N'مصروف',
                       0, ISNULL(oe.Amount,0),
                       oe.Description, ISNULL(oe.ExpenseStatus,1), coa.AccountNameAr, 0
                FROM dbo.CashBoxes cb
                INNER JOIN dbo.OverheadExpenses oe ON oe.CashBoxID=cb.CashBoxID
                LEFT JOIN dbo.ChartOfAccounts coa ON oe.AccountID=coa.AccountID
                WHERE cb.CashBoxID=@CBID AND ISNULL(oe.ExpenseStatus,1)<>3 AND ISNULL(oe.IsPosted,0)=1
                  AND (@ToExclusive IS NULL OR oe.ExpenseDate < @ToExclusive)

                UNION ALL

                SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
                       ct.TransferDate, ct.TransferNo, ct.TransferID AS TransactionID,
                       'Transfer',
                       CASE WHEN ct.FromCashBoxID=@CBID THEN N'تحويل صادر' ELSE N'تحويل وارد' END,
                       CASE WHEN ct.ToCashBoxID=@CBID THEN ISNULL(NULLIF(ct.AmountLocal,0),ct.Amount) ELSE 0 END,
                       CASE WHEN ct.FromCashBoxID=@CBID THEN ISNULL(NULLIF(ct.AmountLocal,0),ct.Amount) ELSE 0 END,
                       ct.Description, ct.Status,
                       CASE WHEN ct.FromCashBoxID=@CBID THEN tcb.CashBoxNameAr ELSE fcb.CashBoxNameAr END, 0
                FROM dbo.CashBoxes cb
                INNER JOIN dbo.CashTransfers ct
                    ON ct.FromCashBoxID=cb.CashBoxID OR ct.ToCashBoxID=cb.CashBoxID
                LEFT JOIN dbo.CashBoxes fcb ON ct.FromCashBoxID=fcb.CashBoxID
                LEFT JOIN dbo.CashBoxes tcb ON ct.ToCashBoxID=tcb.CashBoxID
                WHERE cb.CashBoxID=@CBID AND ct.Status=2
                  AND (@ToExclusive IS NULL OR ct.TransferDate < @ToExclusive)";

            var queryParameters = new DynamicParameters();
            queryParameters.Add("CBID", cashBoxId, System.Data.DbType.Int32);
            queryParameters.Add("ToExclusive", toExclusive, System.Data.DbType.DateTime);
            var rows = await connection.QueryAsync<CashBoxStatementDto>(sql, queryParameters);

            var opening = await connection.ExecuteScalarAsync<decimal>(
                "SELECT ISNULL(OpeningBalance,0) FROM dbo.CashBoxes WHERE CashBoxID=@ID",
                new { ID = cashBoxId });

            var ascending = rows
                .OrderBy(x => x.TransactionDate)
                .ThenBy(x => x.TransactionNo, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.TransactionType, StringComparer.OrdinalIgnoreCase)
                .ToList();

            decimal balance = opening;
            foreach (var item in ascending)
            {
                balance += item.DebitAmount - item.CreditAmount;
                item.Balance = balance;
            }

            return ascending
                .Where(x => !from.HasValue || x.TransactionDate.Date >= from.Value.Date)
                .OrderByDescending(x => x.TransactionDate)
                .ThenByDescending(x => x.TransactionNo, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(x => x.TransactionType, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<byte[]> ExportReceiptsToExcelAsync(List<ReceiptVoucherListDto> data, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_RECV", p => p.CanExport, "ليس لديك صلاحية تصدير سندات القبض.");

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("سندات القبض");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Tajawal";
            ws.Cell(1, 1).Value = "تقرير سندات القبض —   ";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 8).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int hr = 3;
            var headers = new[] { "#", "رقم السند", "التاريخ", "المصدر", "المستلم من", "المبلغ", "طريقة الدفع", "الحالة" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(hr, i + 1);
                c.Value = headers[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#070B14");
                c.Style.Font.FontColor = XLColor.FromHtml("#D4AF37");
            }
            int r = hr + 1;
            int n = 0;
            foreach (var item in data)
            {
                n++;
                ws.Cell(r, 1).Value = n;
                ws.Cell(r, 2).Value = item.ReceiptNumber ?? "";
                ws.Cell(r, 3).Value = item.ReceiptDate.ToString("dd/MM/yyyy");
                ws.Cell(r, 4).Value = item.BankAccountID.GetValueOrDefault() > 0 ? (item.BankAccountName ?? $"بنك #{item.BankAccountID}") : (item.CashBoxNameAr ?? "");
                ws.Cell(r, 5).Value = item.EntityName ?? item.ReceivedFrom ?? "";
                ws.Cell(r, 6).Value = item.Amount;
                ws.Cell(r, 7).Value = item.PaymentMethodName ?? "";
                ws.Cell(r, 8).Value = item.StatusName ?? "";
                if (n % 2 == 0) ws.Range(r, 1, r, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");
                r++;
            }
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            await _audit.WriteAuditLogAsync(userId, 5, "ReceiptVouchers", moduleName: "SCR_RECV", description: $"تصدير {data.Count} سند قبض");
            return ms.ToArray();
        }

        // ==========================================
        // البنوك - على الجدول الحقيقي BankAccounts 15 عمود
        // ==========================================
        public async Task<List<BankAccountListDto>> GetBankAccountsAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض الحسابات البنكية.", p => p.CanView, "SCR_BANKACC", "SCR_BANK_ACCOUNTS");

            using var connection = CreateConnection();
            var sql = @"SELECT ba.BankAccountID, ba.BankAccountCode, ba.BankName, ba.BranchName, ba.AccountNumber, ba.IBAN, ba.SwiftCode,
                               ba.AccountType, CASE ba.AccountType WHEN 1 THEN N'جاري' WHEN 2 THEN N'توفير' WHEN 3 THEN N'اعتماد' ELSE N'أخرى' END AS AccountTypeName,
                               ba.CurrencyID, cur.CurrencyCode, ba.AccountID, coa.AccountCode, coa.AccountNameAr, ba.CurrentBalance,
                               ba.ContactPerson, ba.Phone, ba.IsActive, ba.CreatedDate,
                               ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE BankAccountID=ba.BankAccountID AND ReceiptStatus=2),0) AS TotalReceipts,
                               ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE BankAccountID=ba.BankAccountID AND PaymentStatus=2),0) AS TotalPayments
                        FROM dbo.BankAccounts ba
                        LEFT JOIN dbo.ChartOfAccounts coa ON ba.AccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON ba.CurrencyID=cur.CurrencyID
                        ORDER BY ba.BankName";
            try
            {
                var result = await connection.QueryAsync<BankAccountListDto>(sql);
                return result.ToList();
            }
            catch
            {
                var fallback = @"SELECT BankAccountID, BankAccountCode, BankName, BranchName, AccountNumber, IBAN, SwiftCode, AccountType, CurrencyID, AccountID, CurrentBalance, ContactPerson, Phone, IsActive, CreatedDate,
                                        0 AS TotalReceipts, 0 AS TotalPayments FROM dbo.BankAccounts ORDER BY BankName";
                var result = await connection.QueryAsync<BankAccountListDto>(fallback);
                return result.ToList();
            }
        }

        public async Task<List<BankAccountListDto>> GetBankAccountLookupsAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية استخدام الحسابات البنكية في هذا المسار.",
                p => p.CanView || p.CanAdd || p.CanEdit,
                "SCR_CHEQUES", "SCR_CHEQUE", "SCR_RECV", "SCR_PAYV",
                "SCR_EXPENSES", "SCR_EXPENSE", "SCR_OVERHEAD", "SCR_OVERHEAD_EXPENSES");

            using var connection = CreateConnection();
            var sql = @"SELECT BankAccountID, BankAccountCode, BankName, BranchName, AccountNumber, IsActive
                        FROM dbo.BankAccounts
                        WHERE ISNULL(IsActive,1)=1
                        ORDER BY BankName";
            var result = await connection.QueryAsync<BankAccountListDto>(sql);
            return result.ToList();
        }

        public async Task<BankAccountDto?> GetBankAccountByIdAsync(int id)
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض تفاصيل الحسابات البنكية.", p => p.CanView, "SCR_BANKACC", "SCR_BANK_ACCOUNTS");

            using var connection = CreateConnection();
            var sql = @"SELECT ba.BankAccountID, ba.BankAccountCode, ba.BankName, ba.BranchName, ba.AccountNumber, ba.IBAN, ba.SwiftCode,
                               ba.AccountType, ba.CurrencyID, cur.CurrencyCode, ba.AccountID, coa.AccountNameAr, ba.CurrentBalance,
                               ba.ContactPerson, ba.Phone, ba.IsActive, ba.CreatedDate
                        FROM dbo.BankAccounts ba
                        LEFT JOIN dbo.ChartOfAccounts coa ON ba.AccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON ba.CurrencyID=cur.CurrencyID
                        WHERE ba.BankAccountID=@ID";
            return await connection.QueryFirstOrDefaultAsync<BankAccountDto>(sql, new { ID = id });
        }

        public async Task<string> GenerateBankAccountCodeAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية إنشاء حساب بنكي.", p => p.CanAdd, "SCR_BANKACC", "SCR_BANK_ACCOUNTS");

            using var connection = CreateConnection();
            var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*)+1 FROM dbo.BankAccounts");
            return $"BANK-{count:000}";
        }

        public async Task<int> InsertBankAccountAsync(BankAccountDto dto, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية إنشاء حساب بنكي.", p => p.CanAdd, "SCR_BANKACC", "SCR_BANK_ACCOUNTS");

            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(dto.BankAccountCode))
                dto.BankAccountCode = await GenerateBankAccountCodeAsync();

            var sql = @"INSERT INTO dbo.BankAccounts(BankAccountCode, BankName, BranchName, AccountNumber, IBAN, SwiftCode, AccountType, CurrencyID, AccountID, CurrentBalance, ContactPerson, Phone, IsActive, CreatedDate)
                        VALUES(@BankAccountCode, @BankName, @BranchName, @AccountNumber, @IBAN, @SwiftCode, @AccountType, NULLIF(@CurrencyID,0), NULLIF(@AccountID,0), @CurrentBalance, @ContactPerson, @Phone, @IsActive, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.BankAccountCode,
                dto.BankName,
                dto.BranchName,
                dto.AccountNumber,
                dto.IBAN,
                dto.SwiftCode,
                dto.AccountType,
                dto.CurrencyID,
                dto.AccountID,
                dto.CurrentBalance,
                dto.ContactPerson,
                dto.Phone,
                dto.IsActive
            });
            await _audit.WriteAuditLogAsync(userId, 1, "BankAccounts", newId.ToString(), moduleName: "SCR_BANKS", description: $"إنشاء حساب بنكي: {dto.BankName}");
            return newId;
        }

        public async Task UpdateBankAccountAsync(BankAccountDto dto, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية تعديل الحسابات البنكية.", p => p.CanEdit, "SCR_BANKACC", "SCR_BANK_ACCOUNTS");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(
                    int? BankAccountID, int? AccountID, int? CurrencyID)>(
                    @"SELECT BankAccountID,AccountID,CurrencyID
                      FROM dbo.BankAccounts WITH (UPDLOCK,HOLDLOCK)
                      WHERE BankAccountID=@ID",
                    new { ID = dto.BankAccountID }, trans);
                if (!current.BankAccountID.HasValue)
                    throw new InvalidOperationException("الحساب البنكي غير موجود أو تم حذفه.");

                var hasPostedActivity = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM (
                          SELECT rv.ReceiptID FROM dbo.ReceiptVouchers rv WITH (UPDLOCK,HOLDLOCK)
                          WHERE rv.BankAccountID=@ID AND (rv.ReceiptStatus=2 OR ISNULL(rv.IsPosted,0)=1 OR rv.JournalID IS NOT NULL)
                          UNION ALL
                          SELECT pv.PaymentID FROM dbo.PaymentVouchers pv WITH (UPDLOCK,HOLDLOCK)
                          WHERE pv.BankAccountID=@ID AND (pv.PaymentStatus=2 OR ISNULL(pv.IsPosted,0)=1 OR pv.JournalID IS NOT NULL)
                      ) activity",
                    new { ID = dto.BankAccountID }, trans);
                if (hasPostedActivity > 0
                    && (dto.AccountID.GetValueOrDefault() != current.AccountID.GetValueOrDefault()
                        || dto.CurrencyID.GetValueOrDefault() != current.CurrencyID.GetValueOrDefault()))
                    throw new InvalidOperationException(
                        "للحساب البنكي حركات مالية مرحّلة؛ لا تغيّر حسابه المحاسبي أو عملته مباشرة. أنشئ قيد تسوية بعد مراجعة المحاسب.");

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.BankAccounts SET BankName=@BankName, BranchName=@BranchName, AccountNumber=@AccountNumber, IBAN=@IBAN, SwiftCode=@SwiftCode,
                              AccountType=@AccountType, CurrencyID=NULLIF(@CurrencyID,0), AccountID=NULLIF(@AccountID,0),
                              ContactPerson=@ContactPerson, Phone=@Phone, IsActive=@IsActive
                      WHERE BankAccountID=@BankAccountID",
                    new
                    {
                        dto.BankAccountID,
                        dto.BankName,
                        dto.BranchName,
                        dto.AccountNumber,
                        dto.IBAN,
                        dto.SwiftCode,
                        dto.AccountType,
                        dto.CurrencyID,
                        dto.AccountID,
                        dto.ContactPerson,
                        dto.Phone,
                        dto.IsActive
                    }, trans);
                if (updated != 1)
                    throw new InvalidOperationException("لم يتم تحديث بيانات الحساب البنكي.");

                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while updating bank account {BankAccountID}.",
                        dto.BankAccountID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "BankAccounts", dto.BankAccountID.ToString(), moduleName: "SCR_BANKS", description: $"تعديل حساب بنكي: {dto.BankName}");
        }

        public async Task DeleteBankAccountAsync(int id, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية حذف الحسابات البنكية.", p => p.CanDelete, "SCR_BANKACC", "SCR_BANK_ACCOUNTS");

            using var connection = CreateConnection();
            var hasTrans = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM (SELECT BankAccountID FROM dbo.ReceiptVouchers WHERE BankAccountID=@ID UNION ALL SELECT BankAccountID FROM dbo.PaymentVouchers WHERE BankAccountID=@ID) t", new { ID = id });
            if (hasTrans > 0) throw new Exception("لا يمكن حذف حساب بنكي به حركات");
            await connection.ExecuteAsync("DELETE FROM dbo.BankAccounts WHERE BankAccountID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "BankAccounts", id.ToString(), moduleName: "SCR_BANKS", description: "حذف حساب بنكي");
        }

        // ==========================================
        // الشيكات - على الجدول الحقيقي Cheques 25 عمود
        // ==========================================
        // ==========================================
        // دورة الشيكات المحاسبية — حسابان وسيطان وقيود تُبنى من C#
        // الوارد: 1109 «شيكات تحت التحصيل» (أصول) — الصادر: 2108 «شيكات صادرة تحت التسوية» (التزامات)
        // اعتماد سند بشيك يبقى على حساب مصدر السند نفسه (سلوك إجراءات الترحيل القائمة)،
        // وإعادة التصنيف هنا تنقل الأثر إلى الحساب الوسيط عند ربط الشيك بسند معتمد ومرحّل.
        // ==========================================
        private const string ChequeClearingInAccountCode = "1109";
        private const string ChequeClearingOutAccountCode = "2108";
        private const string ChequeReceiptSourceType = "ChequeReceipt";
        private const string ChequePaymentSourceType = "ChequePayment";
        private const string ChequeCollectionSourceType = "ChequeCollection";
        private const string ChequeBounceSourceType = "ChequeBounce";
        private const string ChequeSettlementSourceType = "ChequeSettlement";

        private sealed class ChequeCycleRow
        {
            public int ChequeID { get; set; }
            public string? ChequeNumber { get; set; }
            public int ChequeType { get; set; }
            public int ChequeStatus { get; set; }
            public decimal Amount { get; set; }
            public int? CurrencyID { get; set; }
            public int? BankAccountID { get; set; }
            public int? EntityType { get; set; }
            public int? EntityID { get; set; }
            public int? ReceiptID { get; set; }
            public int? PaymentID { get; set; }
            public int? JournalID { get; set; }
        }

        private async Task<ChequeCycleRow?> LoadChequeForCycleAsync(
            IDbConnection connection, IDbTransaction transaction, int chequeId)
        {
            return await connection.QueryFirstOrDefaultAsync<ChequeCycleRow>(
                @"SELECT ChequeID, ChequeNumber, ChequeType, ChequeStatus, Amount, CurrencyID,
                         BankAccountID, EntityType, EntityID, ReceiptID, PaymentID, JournalID
                  FROM dbo.Cheques WITH (UPDLOCK, HOLDLOCK) WHERE ChequeID=@ID",
                new { ID = chequeId }, transaction);
        }

        private async Task<int> ResolveChequeClearingAccountIdAsync(
            IDbConnection connection, IDbTransaction transaction, bool incoming)
        {
            var code = incoming ? ChequeClearingInAccountCode : ChequeClearingOutAccountCode;
            var expectedType = incoming ? 1 : 2;
            var label = incoming ? "شيكات تحت التحصيل" : "شيكات صادرة تحت التسوية";
            var rows = (await connection.QueryAsync<(int AccountID, int AccountType, bool IsDetail, bool IsActive, bool CostCenterRequired)>(
                @"SELECT AccountID, AccountType,
                         CONVERT(bit,ISNULL(IsDetailAccount,0)) AS IsDetail,
                         CONVERT(bit,ISNULL(IsActive,0)) AS IsActive,
                         CONVERT(bit,ISNULL(CostCenterRequired,0)) AS CostCenterRequired
                  FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountCode=@Code",
                new { Code = code }, transaction)).ToList();
            if (rows.Count != 1)
                throw new InvalidOperationException($"حساب «{label}» بالكود {code} غير موجود أو مكرر في دليل الحسابات؛ أنشئه أولًا قبل تشغيل دورة الشيكات.");
            var account = rows[0];
            if (!account.IsActive || !account.IsDetail || account.AccountType != expectedType || account.CostCenterRequired)
                throw new InvalidOperationException($"حساب «{label}» {code} يجب أن يكون حسابًا {(incoming ? "أصوليًا (النوع 1)" : "التزامات (النوع 2)")} تفصيليًا نشطًا لا يتطلب مركز تكلفة.");
            return account.AccountID;
        }

        private async Task<int> CreateChequeJournalAsync(
            IDbConnection connection, IDbTransaction transaction,
            DateTime journalDate, int debitAccountId, int creditAccountId, decimal amount,
            string sourceDocType, int chequeId, string chequeNumber, string description,
            string? debitEntityType, int? debitEntityId,
            string? creditEntityType, int? creditEntityId, int userId)
        {
            var openPeriods = (await connection.QueryAsync<(int FiscalYearID, int PeriodID)>(
                @"SELECT fy.FiscalYearID, ap.PeriodID
                  FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
                  WHERE @JournalDate BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
                    AND @JournalDate BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
                    AND fy.YearStatus=1 AND ap.PeriodStatus=1
                  ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
                new { JournalDate = journalDate.Date }, transaction)).ToList();
            if (openPeriods.Count != 1)
                throw new InvalidOperationException("لا توجد فترة محاسبية مفتوحة واحدة تغطي تاريخ حركة الشيك؛ لم يُنشأ القيد وأُلغيت العملية.");
            var period = openPeriods[0];

            var journalNumber = await connection.QueryFirstOrDefaultAsync<string>(
                "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;",
                transaction: transaction);
            if (string.IsNullOrWhiteSpace(journalNumber))
                throw new InvalidOperationException("تعذر توليد رقم قيد الشيك؛ أُلغيت العملية.");

            var employeeId = await connection.ExecuteScalarAsync<int?>(
                "SELECT EmployeeID FROM dbo.SystemUsers WITH (UPDLOCK,HOLDLOCK) WHERE UserID=@UserID",
                new { UserID = userId }, transaction);
            var roundedAmount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

            var journalId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate, PostedBy, PostedDate)
                  VALUES(@Number, @Date, @Description, @Reference, @SourceDocType, @SourceDocID, @SourceDocNumber, @Amount, @Amount, @FiscalYearID, @PeriodID, 1, 2, 1, @UserID, GETDATE(), @PostedBy, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    Number = journalNumber,
                    Date = journalDate.Date,
                    Description = description,
                    Reference = chequeNumber,
                    SourceDocType = sourceDocType,
                    SourceDocID = chequeId,
                    SourceDocNumber = chequeNumber,
                    Amount = roundedAmount,
                    period.FiscalYearID,
                    period.PeriodID,
                    UserID = userId,
                    PostedBy = employeeId
                }, transaction);

            await connection.ExecuteAsync(
                @"INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, EntityType, EntityID)
                  VALUES(@JID, 1, @AccID, @Amount, 0, @Desc, @EntityType, @EntityID)",
                new { JID = journalId, AccID = debitAccountId, Amount = roundedAmount, Desc = description, EntityType = debitEntityType, EntityID = debitEntityId }, transaction);
            await connection.ExecuteAsync(
                @"INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, EntityType, EntityID)
                  VALUES(@JID, 2, @AccID, 0, @Amount, @Desc, @EntityType, @EntityID)",
                new { JID = journalId, AccID = creditAccountId, Amount = roundedAmount, Desc = description, EntityType = creditEntityType, EntityID = creditEntityId }, transaction);

            await ApplyJournalAccountBalancesAsync(connection, transaction, journalId);
            return journalId;
        }

        private async Task<int?> FindChequeReclassJournalIdAsync(
            IDbConnection connection, IDbTransaction transaction, ChequeCycleRow cheque)
        {
            var sourceType = cheque.ChequeType == 1 ? ChequeReceiptSourceType : ChequePaymentSourceType;
            return await connection.QueryFirstOrDefaultAsync<int?>(
                @"SELECT TOP 1 je.JournalID FROM dbo.JournalEntries je WITH (UPDLOCK,HOLDLOCK)
                  WHERE je.SourceDocType=@SourceType AND je.SourceDocID=@ChequeID AND je.JournalStatus=2
                    AND NOT EXISTS (SELECT 1 FROM dbo.JournalEntries r WITH (UPDLOCK,HOLDLOCK)
                                    WHERE r.SourceDocType='REVERSAL' AND r.SourceDocID=je.JournalID AND r.JournalStatus=2)
                  ORDER BY je.JournalID DESC",
                new { SourceType = sourceType, ChequeID = cheque.ChequeID }, transaction);
        }

        private async Task<bool> HasChequeCycleJournalsAsync(
            IDbConnection connection, IDbTransaction transaction, int chequeId)
        {
            var count = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
                  WHERE SourceDocType IN ('ChequeReceipt','ChequePayment','ChequeCollection','ChequeBounce','ChequeSettlement')
                    AND SourceDocID=@ID AND JournalStatus=2",
                new { ID = chequeId }, transaction);
            return count > 0;
        }

        private async Task<(int AccountID, string? EntityType, int? EntityID)> ResolveChequePartyAccountAsync(
            IDbConnection connection, IDbTransaction transaction, ChequeCycleRow cheque)
        {
            // من قيد السند المرتبط إن وجد: الحساب المقابل لحساب المصدر في القيد المرحّل نفسه.
            if (cheque.ReceiptID.GetValueOrDefault() > 0 || cheque.PaymentID.GetValueOrDefault() > 0)
            {
                var isReceipt = cheque.ReceiptID.GetValueOrDefault() > 0;
                var voucher = await connection.QueryFirstOrDefaultAsync<(
                    string? Number, int CashBoxID, int? BankAccountID, int? JournalID, bool IsPosted)>(
                    isReceipt
                        ? @"SELECT ReceiptNumber AS Number, CashBoxID, BankAccountID, JournalID, ISNULL(IsPosted,0) AS IsPosted
                            FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK) WHERE ReceiptID=@ID"
                        : @"SELECT PaymentNumber AS Number, CashBoxID, BankAccountID, JournalID, ISNULL(IsPosted,0) AS IsPosted
                            FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK) WHERE PaymentID=@ID",
                    new { ID = isReceipt ? cheque.ReceiptID : cheque.PaymentID }, transaction);
                if (voucher.Number == null || !voucher.IsPosted || voucher.JournalID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("السند المرتبط بالشيك غير معتمد ومرحّل؛ لا يمكن تحديد الحساب المقابل لحركة الشيك.");
                var sourceAccountId = await ResolveVoucherSourceAccountIdAsync(
                    connection, transaction, voucher.CashBoxID, voucher.BankAccountID, voucher.Number);
                var counterLines = (await connection.QueryAsync<(int AccountID, string? EntityType, int? EntityID)>(
                    @"SELECT DISTINCT jd.AccountID, jd.EntityType, jd.EntityID
                      FROM dbo.JournalEntryDetails jd WITH (UPDLOCK,HOLDLOCK)
                      WHERE jd.JournalID=@JournalID AND jd.AccountID<>@SourceAccountID",
                    new { JournalID = voucher.JournalID!.Value, SourceAccountID = sourceAccountId }, transaction)).ToList();
                var withEntity = counterLines.Where(x => x.EntityID.GetValueOrDefault() > 0).ToList();
                var candidates = withEntity.Count > 0 ? withEntity : counterLines;
                var distinctAccounts = candidates.Select(x => x.AccountID).Distinct().ToList();
                if (distinctAccounts.Count != 1)
                    throw new InvalidOperationException("قيد السند المرتبط بالشيك له أكثر من حساب مقابل متميز؛ حركة الشيك المقابلة تتطلب تسوية يدوية.");
                var chosen = candidates.First(x => x.AccountID == distinctAccounts[0]);
                return (chosen.AccountID, chosen.EntityType, chosen.EntityID);
            }
            // بلا سند: حساب التحكم النظامي بحسب نوع الجهة المدوّن على الشيك نفسه.
            if (cheque.EntityID.GetValueOrDefault() > 0 && (cheque.EntityType == 1 || cheque.EntityType == 2))
            {
                var code = cheque.EntityType == 1 ? "1103" : "2101";
                var expectedType = cheque.EntityType == 1 ? 1 : 2;
                var account = await connection.QueryFirstOrDefaultAsync<(int AccountID, int AccountType, bool IsActive)>(
                    @"SELECT AccountID, AccountType, CONVERT(bit,ISNULL(IsActive,0)) AS IsActive
                      FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountCode=@Code",
                    new { Code = code }, transaction);
                if (account.AccountID <= 0 || !account.IsActive || account.AccountType != expectedType)
                    throw new InvalidOperationException($"حساب التحكم {code} غير موجود أو غير نشط في دليل الحسابات؛ لا يمكن إتمام حركة الشيك.");
                return (account.AccountID,
                    cheque.EntityType == 1 ? "Customer" : "Supplier",
                    cheque.EntityID);
            }
            throw new InvalidOperationException("الشيك غير مرتبط بسند معتمد ولا بجهة محددة؛ لا يوجد حساب مقابل معروف لترحيل حركته.");
        }

        private async Task<int> ResolveChequeBankLedgerAccountIdAsync(
            IDbConnection connection, IDbTransaction transaction, int bankAccountId, string chequeNumber)
        {
            var accountId = await connection.ExecuteScalarAsync<int?>(
                @"SELECT TOP 1 coa.AccountID
                  FROM dbo.BankAccounts ba WITH (UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK,HOLDLOCK) ON coa.AccountID=ba.AccountID
                  WHERE ba.BankAccountID=@ID AND ISNULL(ba.IsActive,1)=1 AND coa.IsActive=1
                    AND coa.IsDetailAccount=1 AND coa.AccountType=1",
                new { ID = bankAccountId }, transaction);
            if (accountId is not int ledgerId || ledgerId <= 0)
                throw new InvalidOperationException($"شيك {chequeNumber}: الحساب البنكي المحدد غير نشط أو غير مرتبط بحساب أصل تفصيلي صالح.");
            return ledgerId;
        }

        private async Task AdjustChequeBankStoredBalanceAsync(
            IDbConnection connection, IDbTransaction transaction, int bankAccountId, decimal delta, string chequeNumber)
        {
            var affected = await connection.ExecuteAsync(
                "UPDATE dbo.BankAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE BankAccountID=@ID",
                new { Delta = delta, ID = bankAccountId }, transaction);
            if (affected != 1)
                throw new InvalidOperationException($"تعذر تحديث رصيد البنك المخزّن لشيك {chequeNumber}؛ أُلغيت العملية.");
        }

        private async Task<int> CreateChequeReclassJournalIfLinkedAsync(
            IDbConnection connection, IDbTransaction transaction, ChequeCycleRow cheque, int userId)
        {
            // يُستدعى عند تسجيل شيك مرتبط بسند أو ربطه: السند يجب أن يكون معتمدًا ومرحّلًا.
            var isReceipt = cheque.ReceiptID.GetValueOrDefault() > 0;
            var voucher = await connection.QueryFirstOrDefaultAsync<(
                string? Number, int Status, int CashBoxID, int? BankAccountID, int? JournalID, bool IsPosted, decimal AmountLocal)>(
                isReceipt
                    ? @"SELECT ReceiptNumber AS Number, ReceiptStatus AS Status, CashBoxID, BankAccountID, JournalID, ISNULL(IsPosted,0) AS IsPosted, ISNULL(AmountLocal,Amount) AS AmountLocal
                        FROM dbo.ReceiptVouchers WITH (UPDLOCK,HOLDLOCK) WHERE ReceiptID=@ID"
                    : @"SELECT PaymentNumber AS Number, PaymentStatus AS Status, CashBoxID, BankAccountID, JournalID, ISNULL(IsPosted,0) AS IsPosted, ISNULL(AmountLocal,Amount) AS AmountLocal
                        FROM dbo.PaymentVouchers WITH (UPDLOCK,HOLDLOCK) WHERE PaymentID=@ID",
                new { ID = isReceipt ? cheque.ReceiptID : cheque.PaymentID }, transaction);
            if (voucher.Number == null)
                throw new InvalidOperationException("السند المرتبط بالشيك غير موجود.");
            if (voucher.Status != 2 || !voucher.IsPosted || voucher.JournalID.GetValueOrDefault() <= 0)
                throw new InvalidOperationException($"السند {voucher.Number} غير معتمد ومرحّل؛ اعتمد السند أولًا ثم سجّل الشيك أو اربطه به.");
            if (cheque.Amount > voucher.AmountLocal)
                throw new InvalidOperationException($"مبلغ الشيك أكبر من مبلغ السند {voucher.Number}؛ لا يمكن إعادة تصنيف أكثر مما رحّله السند.");
            var sourceAccountId = await ResolveVoucherSourceAccountIdAsync(
                connection, transaction, voucher.CashBoxID, voucher.BankAccountID, voucher.Number);
            var clearingId = await ResolveChequeClearingAccountIdAsync(connection, transaction, cheque.ChequeType == 1);
            var description = cheque.ChequeType == 1
                ? $"إعادة تصنيف شيك وارد {cheque.ChequeNumber} — من حساب مصدر السند {voucher.Number} إلى شيكات تحت التحصيل"
                : $"إعادة تصنيف شيك صادر {cheque.ChequeNumber} — من حساب مصدر السند {voucher.Number} إلى شيكات صادرة تحت التسوية";
            int journalId;
            if (cheque.ChequeType == 1)
                journalId = await CreateChequeJournalAsync(connection, transaction, DateTime.Today,
                    clearingId, sourceAccountId, cheque.Amount,
                    ChequeReceiptSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "", description,
                    null, null, null, null, userId);
            else
                journalId = await CreateChequeJournalAsync(connection, transaction, DateTime.Today,
                    sourceAccountId, clearingId, cheque.Amount,
                    ChequePaymentSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "", description,
                    null, null, null, null, userId);
            var updated = await connection.ExecuteAsync(
                "UPDATE dbo.Cheques SET JournalID=@JournalID WHERE ChequeID=@ID",
                new { JournalID = journalId, ID = cheque.ChequeID }, transaction);
            if (updated != 1)
                throw new InvalidOperationException("تعذر حفظ قيد إعادة تصنيف الشيك؛ أُلغيت العملية.");
            return journalId;
        }

        private async Task ReverseLinkedChequeReclassJournalsAsync(
            SqlConnection connection, SqlTransaction transaction,
            int? receiptId, int? paymentId, int userId, int postedByEmployeeId,
            DateTime reversalDate, string voucherNumber)
        {
            var chequeIds = (await connection.QueryAsync<int>(
                @"SELECT ChequeID FROM dbo.Cheques WITH (UPDLOCK, HOLDLOCK)
                  WHERE (@ReceiptID IS NOT NULL AND ReceiptID=@ReceiptID)
                     OR (@PaymentID IS NOT NULL AND PaymentID=@PaymentID)",
                new { ReceiptID = receiptId, PaymentID = paymentId }, transaction)).ToList();
            foreach (var chequeId in chequeIds)
            {
                var reclassId = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT TOP 1 je.JournalID FROM dbo.JournalEntries je WITH (UPDLOCK,HOLDLOCK)
                      WHERE je.SourceDocType IN ('ChequeReceipt','ChequePayment') AND je.SourceDocID=@ChequeID AND je.JournalStatus=2
                        AND NOT EXISTS (SELECT 1 FROM dbo.JournalEntries r WITH (UPDLOCK,HOLDLOCK)
                                        WHERE r.SourceDocType='REVERSAL' AND r.SourceDocID=je.JournalID AND r.JournalStatus=2)
                      ORDER BY je.JournalID DESC",
                    new { ChequeID = chequeId }, transaction);
                if (reclassId is int journalId && journalId > 0)
                {
                    await ReversePostedJournalCoreAsync(
                        connection, transaction, journalId, userId, postedByEmployeeId,
                        reversalDate, null, $"عكس إعادة تصنيف شيك مرتبط بسند {voucherNumber}");
                }
            }
        }

        public async Task<List<ChequeListDto>> GetChequesAsync(DateTime? from = null, DateTime? to = null, int? type = null, int? status = null)
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض الشيكات.", p => p.CanView, "SCR_CHEQUES", "SCR_CHEQUE");

            var sql = @"SELECT ch.ChequeID, ch.ChequeNumber, ch.ChequeType, CASE ch.ChequeType WHEN 1 THEN N'وارد' WHEN 2 THEN N'صادر' ELSE N'غير محدد' END AS ChequeTypeName,
                               ch.ChequeDate, ch.DueDate, ch.Amount, ch.CurrencyID, cur.CurrencyCode, ch.BankName, ch.BranchName,
                               ch.BankAccountID, ba.BankName AS BankAccountName, ch.EntityType, ch.EntityID,
                               CASE WHEN ch.EntityType=1 THEN c.CustomerNameAr WHEN ch.EntityType=2 THEN s.SupplierNameAr ELSE ch.EntityName END AS EntityName,
                               ch.ChequeStatus, CASE ch.ChequeStatus WHEN 1 THEN N'بالحافظة' WHEN 2 THEN N'مقدم للبنك' WHEN 3 THEN N'محصل' WHEN 4 THEN N'مرتجع' WHEN 5 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                               ch.CollectionDate, ch.BounceDate, ch.BounceReason, ch.ReceiptID, ch.PaymentID, rv.ReceiptNumber, pv.PaymentNumber, ch.JournalID, ch.CreatedDate
                        FROM dbo.Cheques ch
                        LEFT JOIN dbo.BankAccounts ba ON ch.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.Currencies cur ON ch.CurrencyID=cur.CurrencyID
                        LEFT JOIN dbo.Customers c ON ch.EntityType=1 AND ch.EntityID=c.CustomerID
                        LEFT JOIN dbo.Suppliers s ON ch.EntityType=2 AND ch.EntityID=s.SupplierID
                        LEFT JOIN dbo.ReceiptVouchers rv ON ch.ReceiptID=rv.ReceiptID
                        LEFT JOIN dbo.PaymentVouchers pv ON ch.PaymentID=pv.PaymentID
                        WHERE 1=1 AND (@From IS NULL OR ch.DueDate >= @From) AND (@To IS NULL OR ch.DueDate <= @To)
                        AND (@Type IS NULL OR ch.ChequeType=@Type) AND (@Status IS NULL OR ch.ChequeStatus=@Status)
                        ORDER BY ch.DueDate DESC, ch.ChequeID DESC";
            try
            {
                using var connection = CreateConnection();
                var result = await connection.QueryAsync<ChequeListDto>(sql, new { From = from, To = to, Type = type, Status = status });
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load the cheque list.");
                throw;
            }
        }

        public async Task<ChequeDto?> GetChequeByIdAsync(int id)
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض بيانات الشيك.", p => p.CanView, "SCR_CHEQUES", "SCR_CHEQUE");

            using var connection = CreateConnection();
            var sql = @"SELECT ChequeID, ChequeNumber, ChequeType, ChequeDate, DueDate, Amount, CurrencyID, BankName, BranchName, BankAccountID, EntityType, EntityID, EntityName, ChequeStatus, CollectionDate, BounceDate, BounceReason, ReceiptID, PaymentID, Notes, CreatedBy, CreatedDate
                        FROM dbo.Cheques WHERE ChequeID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ChequeDto>(sql, new { ID = id });
        }

        public async Task<int> InsertChequeAsync(ChequeDto dto, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية إضافة الشيكات.", p => p.CanAdd, "SCR_CHEQUES", "SCR_CHEQUE");
            if (HasNonInitialChequeApprovalState(dto))
            {
                await RequireChequeApprovalPermissionAsync(
                    userId, "إنشاء شيك بحالة غير ابتدائية يتطلب صلاحية اعتماد الشيكات.");
            }
            if (dto.Amount <= 0)
                throw new InvalidOperationException("مبلغ الشيك يجب أن يكون أكبر من صفر.");
            if (dto.ReceiptID.GetValueOrDefault() > 0 && dto.PaymentID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("لا يمكن ربط الشيك بسند قبض وسند صرف معًا.");
            if (dto.ChequeType == 1 && dto.PaymentID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("الشيك الوارد يُربط بسند قبض لا بسند صرف.");
            if (dto.ChequeType == 2 && dto.ReceiptID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("الشيك الصادر يُربط بسند صرف لا بسند قبض.");
            var hasLink = dto.ReceiptID.GetValueOrDefault() > 0 || dto.PaymentID.GetValueOrDefault() > 0;
            if (hasLink && dto.ChequeStatus != 1)
                throw new InvalidOperationException("الشيك المرتبط بسند يُسجَّل بحالة الحافظة أولًا؛ إعادة التصنيف المحاسبية تُبنى على هذه الحالة.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            int newId;
            try
            {
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, dto.CurrencyID, 1m);
                await EnsureBankAccountLocalCurrencyAsync(
                    connection, trans, dto.BankAccountID, "الشيك");
                await EnsureLinkedChequeVouchersLocalCurrencyAsync(
                    connection, trans, dto.ReceiptID, dto.PaymentID);
                var sql = @"INSERT INTO dbo.Cheques(ChequeNumber, ChequeType, ChequeDate, DueDate, Amount, CurrencyID, BankName, BranchName, BankAccountID, EntityType, EntityID, EntityName, ChequeStatus, ReceiptID, PaymentID, Notes, CreatedBy, CreatedDate)
                            VALUES(@ChequeNumber, @ChequeType, @ChequeDate, @DueDate, @Amount, NULLIF(@CurrencyID,0), @BankName, @BranchName, NULLIF(@BankAccountID,0), @EntityType, NULLIF(@EntityID,0), @EntityName, @ChequeStatus, NULLIF(@ReceiptID,0), NULLIF(@PaymentID,0), @Notes, @CreatedBy, GETDATE());
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";
                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    dto.ChequeNumber,
                    dto.ChequeType,
                    dto.ChequeDate,
                    dto.DueDate,
                    dto.Amount,
                    dto.CurrencyID,
                    dto.BankName,
                    dto.BranchName,
                    dto.BankAccountID,
                    dto.EntityType,
                    dto.EntityID,
                    dto.EntityName,
                    dto.ChequeStatus,
                    dto.ReceiptID,
                    dto.PaymentID,
                    dto.Notes,
                    CreatedBy = userId
                }, trans);

                if (hasLink)
                {
                    var cheque = await LoadChequeForCycleAsync(connection, trans, newId);
                    await CreateChequeReclassJournalIfLinkedAsync(connection, trans, cheque!, userId);
                }
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cheque insert.");
                }
                throw;
            }
            await _audit.WriteAuditLogAsync(userId, 1, "Cheques", newId.ToString(), moduleName: "SCR_CHEQ", description: $"إنشاء شيك: {dto.ChequeNumber}");
            return newId;
        }

        public async Task UpdateChequeAsync(ChequeDto dto, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية تعديل الشيكات.", p => p.CanEdit, "SCR_CHEQUES", "SCR_CHEQUE");
            if (dto.ReceiptID.GetValueOrDefault() > 0 && dto.PaymentID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("لا يمكن ربط الشيك بسند قبض وسند صرف معًا.");
            if (dto.ChequeType == 1 && dto.PaymentID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("الشيك الوارد يُربط بسند قبض لا بسند صرف.");
            if (dto.ChequeType == 2 && dto.ReceiptID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("الشيك الصادر يُربط بسند صرف لا بسند قبض.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var currentState = await connection.QueryFirstOrDefaultAsync<ChequeDto>(
                    @"SELECT ChequeStatus, CollectionDate, BounceDate, BounceReason, CurrencyID, BankAccountID,
                             ReceiptID, PaymentID
                      FROM dbo.Cheques WITH (UPDLOCK, HOLDLOCK) WHERE ChequeID=@ID",
                    new { ID = dto.ChequeID }, trans);
                if (currentState is null)
                    throw new InvalidOperationException("الشيك المطلوب تعديله غير موجود.");
                var current = await LoadChequeForCycleAsync(connection, trans, dto.ChequeID);
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, currentState.CurrencyID, 1m);
                await EnsureBankAccountLocalCurrencyAsync(
                    connection, trans, currentState.BankAccountID, "الشيك");
                await EnsureLinkedChequeVouchersLocalCurrencyAsync(
                    connection, trans, currentState.ReceiptID, currentState.PaymentID);
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, dto.CurrencyID, 1m);
                await EnsureBankAccountLocalCurrencyAsync(
                    connection, trans, dto.BankAccountID, "الشيك");
                await EnsureLinkedChequeVouchersLocalCurrencyAsync(
                    connection, trans, dto.ReceiptID, dto.PaymentID);

                if (HasChequeApprovalStateChanged(currentState, dto))
                {
                    await RequireChequeApprovalPermissionAsync(
                        userId, "تغيير حالة الشيك أو بيانات التحصيل/الارتجاع يتطلب صلاحية اعتماد الشيكات.");
                }

                // حالات الدورة (مقدم للبنك/محصل/مرتجع) تُدار من إجراءات الدورة فقط، لا من التعديل العام.
                var statusChanged = dto.ChequeStatus != current!.ChequeStatus;
                if (statusChanged && current.ChequeStatus != 1 && current.ChequeStatus != 5)
                    throw new InvalidOperationException("حالة هذا الشيك لا تُغيَّر من شاشة التعديل؛ تُدار من إجراءات التقديم والتحصيل والارتجاع والتسوية.");
                if (statusChanged && dto.ChequeStatus != 1 && dto.ChequeStatus != 5)
                    throw new InvalidOperationException("لا يمكن ضبط حالة الشيك يدويًا إلى مقدم للبنك أو محصل أو مرتجع؛ استخدم إجراءات الدورة المخصصة.");

                var hasCycleJournals = await HasChequeCycleJournalsAsync(connection, trans, dto.ChequeID);
                if (hasCycleJournals && (dto.Amount != current.Amount || dto.ChequeType != current.ChequeType
                    || dto.ReceiptID.GetValueOrDefault() != current.ReceiptID.GetValueOrDefault()
                    || dto.PaymentID.GetValueOrDefault() != current.PaymentID.GetValueOrDefault()))
                    throw new InvalidOperationException("شيك له قيود دورة مرحّلة؛ لا يمكن تغيير مبلغه أو نوعه أو سنده المرتبط.");

                int? newJournalId = null;

                // إلغاء شيك بالحافظة له قيد إعادة تصنيف: تُعاد الذمة إلى حساب الطرف قبل الإلغاء.
                if (statusChanged && dto.ChequeStatus == 5 && current.ChequeStatus == 1)
                {
                    var reclassId = await FindChequeReclassJournalIdAsync(connection, trans, current);
                    if (reclassId != null)
                    {
                        var party = await ResolveChequePartyAccountAsync(connection, trans, current);
                        var clearingId = await ResolveChequeClearingAccountIdAsync(connection, trans, current.ChequeType == 1);
                        var description = current.ChequeType == 1
                            ? $"إلغاء شيك وارد {current.ChequeNumber} — إعادة الذمة من شيكات تحت التحصيل"
                            : $"إلغاء شيك صادر {current.ChequeNumber} — إعادة الذمة من شيكات صادرة تحت التسوية";
                        newJournalId = current.ChequeType == 1
                            ? await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                                party.AccountID, clearingId, current.Amount,
                                ChequeBounceSourceType, current.ChequeID, current.ChequeNumber ?? "", description,
                                party.EntityType, party.EntityID, null, null, userId)
                            : await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                                clearingId, party.AccountID, current.Amount,
                                ChequeBounceSourceType, current.ChequeID, current.ChequeNumber ?? "", description,
                                null, null, party.EntityType, party.EntityID, userId);
                    }
                }

                var sql = @"UPDATE dbo.Cheques SET ChequeNumber=@ChequeNumber, ChequeType=@ChequeType, ChequeDate=@ChequeDate, DueDate=@DueDate, Amount=@Amount, CurrencyID=NULLIF(@CurrencyID,0),
                            BankName=@BankName, BranchName=@BranchName, BankAccountID=NULLIF(@BankAccountID,0), EntityType=@EntityType, EntityID=NULLIF(@EntityID,0), EntityName=@EntityName,
                            ChequeStatus=@ChequeStatus, CollectionDate=@CollectionDate, BounceDate=@BounceDate, BounceReason=@BounceReason,
                            ReceiptID=NULLIF(@ReceiptID,0), PaymentID=NULLIF(@PaymentID,0),
                            JournalID=CASE WHEN @NewJournalID IS NULL THEN JournalID ELSE @NewJournalID END,
                            Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                            WHERE ChequeID=@ChequeID";
                await connection.ExecuteAsync(sql, new
                {
                    dto.ChequeID,
                    dto.ChequeNumber,
                    dto.ChequeType,
                    dto.ChequeDate,
                    dto.DueDate,
                    dto.Amount,
                    dto.CurrencyID,
                    dto.BankName,
                    dto.BranchName,
                    dto.BankAccountID,
                    dto.EntityType,
                    dto.EntityID,
                    dto.EntityName,
                    dto.ChequeStatus,
                    dto.CollectionDate,
                    dto.BounceDate,
                    dto.BounceReason,
                    dto.ReceiptID,
                    dto.PaymentID,
                    NewJournalID = newJournalId,
                    dto.Notes,
                    UserID = userId
                }, trans);

                // ربط جديد بسند أثناء التعديل (بلا قيود دورة سابقة): تُبنى إعادة التصنيف الآن.
                var linkAdded = !hasCycleJournals
                    && (dto.ReceiptID.GetValueOrDefault() > 0 || dto.PaymentID.GetValueOrDefault() > 0)
                    && (dto.ReceiptID.GetValueOrDefault() != current.ReceiptID.GetValueOrDefault()
                        || dto.PaymentID.GetValueOrDefault() != current.PaymentID.GetValueOrDefault());
                if (linkAdded && dto.ChequeStatus == 1)
                {
                    var updated = await LoadChequeForCycleAsync(connection, trans, dto.ChequeID);
                    await CreateChequeReclassJournalIfLinkedAsync(connection, trans, updated!, userId);
                }
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cheque update for cheque {ChequeID}.",
                        dto.ChequeID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", dto.ChequeID.ToString(), moduleName: "SCR_CHEQ", description: $"تعديل شيك: {dto.ChequeNumber}");
        }

        public async Task PostChequeToBankAsync(int id, int bankAccountId, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية اعتماد تقديم الشيك للبنك.", p => p.CanApprove, "SCR_CHEQUES", "SCR_CHEQUE");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (bankAccountId <= 0)
                throw new InvalidOperationException("اختر الحساب البنكي الذي سيُقدَّم إليه الشيك.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var cheque = await LoadChequeForCycleAsync(connection, trans, id);
                if (cheque is null)
                    throw new InvalidOperationException("الشيك غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, cheque.CurrencyID, 1m);
                await EnsureBankAccountLocalCurrencyAsync(
                    connection, trans, bankAccountId, "الشيك");
                await EnsureLinkedChequeVouchersLocalCurrencyAsync(
                    connection, trans, cheque.ReceiptID, cheque.PaymentID);
                if (cheque.ChequeStatus != 1)
                    throw new InvalidOperationException("يُقدَّم للبنك شيك بالحافظة فقط.");
                await ResolveChequeBankLedgerAccountIdAsync(connection, trans, bankAccountId, cheque.ChequeNumber ?? "");

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.Cheques SET ChequeStatus=2, BankAccountID=@BankAccountID
                      WHERE ChequeID=@ID AND ChequeStatus=1",
                    new { ID = id, BankAccountID = bankAccountId }, trans);
                if (updated != 1)
                    throw new InvalidOperationException("تغيرت حالة الشيك أثناء التقديم؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cheque bank presentation for cheque {ChequeID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: "تقديم شيك للبنك");
        }

        public async Task CollectChequeAsync(int id, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية اعتماد تحصيل الشيك.", p => p.CanApprove, "SCR_CHEQUES", "SCR_CHEQUE");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var cheque = await LoadChequeForCycleAsync(connection, trans, id);
                if (cheque is null)
                    throw new InvalidOperationException("الشيك غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, cheque.CurrencyID, 1m);
                await EnsureBankAccountLocalCurrencyAsync(
                    connection, trans, cheque.BankAccountID, "الشيك");
                await EnsureLinkedChequeVouchersLocalCurrencyAsync(
                    connection, trans, cheque.ReceiptID, cheque.PaymentID);
                if (cheque.ChequeType != 1)
                    throw new InvalidOperationException("لا يمكن تسجيل تحصيل لشيك صادر.");
                if (cheque.ChequeStatus == 1)
                    throw new InvalidOperationException("قدّم الشيك للبنك أولًا قبل تحصيله؛ التحصيل يكون من حالة «مقدم للبنك».");
                if (cheque.ChequeStatus != 2)
                    throw new InvalidOperationException("يمكن تحصيل الشيك الوارد من حالة «مقدم للبنك» فقط.");
                if (cheque.BankAccountID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("الشيك بلا حساب بنكي محدد للإيداع؛ حدّد الحساب من إجراء التقديم للبنك أولًا.");
                var bankLedgerId = await ResolveChequeBankLedgerAccountIdAsync(
                    connection, trans, cheque.BankAccountID!.Value, cheque.ChequeNumber ?? "");

                int journalId;
                var reclassId = await FindChequeReclassJournalIdAsync(connection, trans, cheque);
                if (reclassId != null)
                {
                    var clearingId = await ResolveChequeClearingAccountIdAsync(connection, trans, true);
                    journalId = await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                        bankLedgerId, clearingId, cheque.Amount,
                        ChequeCollectionSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "",
                        $"تحصيل شيك وارد {cheque.ChequeNumber} — إيداع بنكي من شيكات تحت التحصيل",
                        null, null, null, null, userId);
                }
                else
                {
                    var party = await ResolveChequePartyAccountAsync(connection, trans, cheque);
                    journalId = await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                        bankLedgerId, party.AccountID, cheque.Amount,
                        ChequeCollectionSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "",
                        $"تحصيل شيك وارد {cheque.ChequeNumber} — إيداع بنكي مقابل ذمة الطرف",
                        null, null, party.EntityType, party.EntityID, userId);
                }
                await AdjustChequeBankStoredBalanceAsync(
                    connection, trans, cheque.BankAccountID!.Value, cheque.Amount, cheque.ChequeNumber ?? "");

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.Cheques SET ChequeStatus=3, CollectionDate=GETDATE(), JournalID=@JournalID
                      WHERE ChequeID=@ID AND ChequeType=1 AND ChequeStatus=2",
                    new { ID = id, JournalID = journalId }, trans);
                if (updated != 1)
                    throw new InvalidOperationException("تغيرت حالة الشيك أثناء التحصيل؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cheque collection for cheque {ChequeID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: "تحصيل شيك");
        }

        public async Task SettleOutgoingChequeAsync(int id, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية اعتماد تسوية الشيك الصادر.", p => p.CanApprove, "SCR_CHEQUES", "SCR_CHEQUE");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var cheque = await LoadChequeForCycleAsync(connection, trans, id);
                if (cheque is null)
                    throw new InvalidOperationException("الشيك غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, cheque.CurrencyID, 1m);
                await EnsureBankAccountLocalCurrencyAsync(
                    connection, trans, cheque.BankAccountID, "الشيك");
                await EnsureLinkedChequeVouchersLocalCurrencyAsync(
                    connection, trans, cheque.ReceiptID, cheque.PaymentID);
                if (cheque.ChequeType != 2)
                    throw new InvalidOperationException("تسوية البنك خاصة بالشيكات الصادرة فقط.");
                if (cheque.ChequeStatus != 1 && cheque.ChequeStatus != 2)
                    throw new InvalidOperationException("تُسوّى من البنك شيكات صادرة بالحافظة أو مقدمة للبنك فقط.");
                if (cheque.BankAccountID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("الشيك بلا حساب بنكي محدد للخصم؛ حدّد الحساب البنكي على الشيك أولًا.");
                var bankLedgerId = await ResolveChequeBankLedgerAccountIdAsync(
                    connection, trans, cheque.BankAccountID!.Value, cheque.ChequeNumber ?? "");

                int journalId;
                var reclassId = await FindChequeReclassJournalIdAsync(connection, trans, cheque);
                if (reclassId != null)
                {
                    var clearingId = await ResolveChequeClearingAccountIdAsync(connection, trans, false);
                    journalId = await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                        clearingId, bankLedgerId, cheque.Amount,
                        ChequeSettlementSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "",
                        $"تسوية شيك صادر {cheque.ChequeNumber} — خصم بنكي مقابل شيكات صادرة تحت التسوية",
                        null, null, null, null, userId);
                }
                else
                {
                    var party = await ResolveChequePartyAccountAsync(connection, trans, cheque);
                    journalId = await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                        party.AccountID, bankLedgerId, cheque.Amount,
                        ChequeSettlementSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "",
                        $"تسوية شيك صادر {cheque.ChequeNumber} — خصم بنكي مقابل ذمة الطرف",
                        party.EntityType, party.EntityID, null, null, userId);
                }
                await AdjustChequeBankStoredBalanceAsync(
                    connection, trans, cheque.BankAccountID!.Value, -cheque.Amount, cheque.ChequeNumber ?? "");

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.Cheques SET ChequeStatus=3, CollectionDate=GETDATE(), JournalID=@JournalID
                      WHERE ChequeID=@ID AND ChequeType=2 AND ChequeStatus IN (1,2)",
                    new { ID = id, JournalID = journalId }, trans);
                if (updated != 1)
                    throw new InvalidOperationException("تغيرت حالة الشيك أثناء التسوية؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling outgoing cheque settlement for cheque {ChequeID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: "تسوية شيك صادر من البنك");
        }

        public async Task BounceChequeAsync(int id, string reason, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية اعتماد ارتجاع الشيك.", p => p.CanApprove, "SCR_CHEQUES", "SCR_CHEQUE");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("سبب ارتجاع الشيك مطلوب.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var cheque = await LoadChequeForCycleAsync(connection, trans, id);
                if (cheque is null)
                    throw new InvalidOperationException("الشيك غير موجود.");
                await LocalCurrencyGuard.EnsureLocalCurrencyAsync(
                    connection, trans, cheque.CurrencyID, 1m);
                await EnsureBankAccountLocalCurrencyAsync(
                    connection, trans, cheque.BankAccountID, "الشيك");
                await EnsureLinkedChequeVouchersLocalCurrencyAsync(
                    connection, trans, cheque.ReceiptID, cheque.PaymentID);
                if (cheque.ChequeType != 1 && cheque.ChequeType != 2)
                    throw new InvalidOperationException("نوع الشيك غير صالح للارتجاع.");
                var isIncoming = cheque.ChequeType == 1;
                if (isIncoming && cheque.ChequeStatus != 1 && cheque.ChequeStatus != 2 && cheque.ChequeStatus != 3)
                    throw new InvalidOperationException("لا يمكن ارتجاع شيك وارد بهذه الحالة.");
                if (!isIncoming && cheque.ChequeStatus != 1 && cheque.ChequeStatus != 2)
                    throw new InvalidOperationException("لا يمكن ارتجاع شيك صادر ليس بالحافظة أو مقدمًا للبنك.");

                // الارتجاع يعيد الذمة في الدفاتر فقط؛ لا يفتح تسويات الفواتير المرتبطة بالسند من جديد.
                int? journalId = null;
                var reclassId = await FindChequeReclassJournalIdAsync(connection, trans, cheque);
                if (isIncoming && cheque.ChequeStatus == 3)
                {
                    if (cheque.BankAccountID.GetValueOrDefault() <= 0)
                        throw new InvalidOperationException("الشيك المحصّل بلا حساب بنكي محدد؛ لا يمكن عكس التحصيل محاسبيًا.");
                    var bankLedgerId = await ResolveChequeBankLedgerAccountIdAsync(
                        connection, trans, cheque.BankAccountID!.Value, cheque.ChequeNumber ?? "");
                    var party = await ResolveChequePartyAccountAsync(connection, trans, cheque);
                    journalId = await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                        party.AccountID, bankLedgerId, cheque.Amount,
                        ChequeBounceSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "",
                        $"ارتجاع شيك وارد محصّل {cheque.ChequeNumber} — إعادة الذمة وخصم البنك: {reason.Trim()}",
                        party.EntityType, party.EntityID, null, null, userId);
                    await AdjustChequeBankStoredBalanceAsync(
                        connection, trans, cheque.BankAccountID!.Value, -cheque.Amount, cheque.ChequeNumber ?? "");
                }
                else if (reclassId != null)
                {
                    var party = await ResolveChequePartyAccountAsync(connection, trans, cheque);
                    var clearingId = await ResolveChequeClearingAccountIdAsync(connection, trans, isIncoming);
                    var description = isIncoming
                        ? $"ارتجاع شيك وارد {cheque.ChequeNumber} — إعادة الذمة من شيكات تحت التحصيل: {reason.Trim()}"
                        : $"ارتجاع شيك صادر {cheque.ChequeNumber} — إعادة الذمة من شيكات صادرة تحت التسوية: {reason.Trim()}";
                    journalId = isIncoming
                        ? await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                            party.AccountID, clearingId, cheque.Amount,
                            ChequeBounceSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "", description,
                            party.EntityType, party.EntityID, null, null, userId)
                        : await CreateChequeJournalAsync(connection, trans, DateTime.Today,
                            clearingId, party.AccountID, cheque.Amount,
                            ChequeBounceSourceType, cheque.ChequeID, cheque.ChequeNumber ?? "", description,
                            null, null, party.EntityType, party.EntityID, userId);
                }

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.Cheques SET ChequeStatus=4, BounceDate=GETDATE(), BounceReason=@Reason,
                            JournalID=CASE WHEN @JournalID IS NULL THEN JournalID ELSE @JournalID END
                      WHERE ChequeID=@ID AND ChequeType=@Type AND ChequeStatus=@Status",
                    new { ID = id, Type = cheque.ChequeType, Status = cheque.ChequeStatus, Reason = reason.Trim(), JournalID = journalId }, trans);
                if (updated != 1)
                    throw new InvalidOperationException("تغيرت حالة الشيك أثناء الارتجاع؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cheque bounce for cheque {ChequeID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: $"ارتجاع شيك: {reason.Trim()}");
        }

        public async Task DeleteChequeAsync(int id, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية حذف الشيكات.", p => p.CanDelete, "SCR_CHEQUES", "SCR_CHEQUE");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT ChequeStatus FROM dbo.Cheques WITH (UPDLOCK, HOLDLOCK) WHERE ChequeID=@ID",
                    new { ID = id }, trans);
                if (status != 1 && status != 5) throw new Exception("لا يمكن حذف شيك مقدم للبنك أو محصل أو غير موجود");
                if (await HasChequeCycleJournalsAsync(connection, trans, id))
                    throw new InvalidOperationException("شيك له قيود دورة مرحّلة؛ لا يمكن حذفه.");

                await connection.ExecuteAsync("DELETE FROM dbo.Cheques WHERE ChequeID=@ID", new { ID = id }, trans);
                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cheque deletion for cheque {ChequeID}.",
                        id);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: "حذف شيك");
        }

        public async Task<List<CostCenterListDto>> GetCostCentersTreeAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض مراكز التكلفة.",
                p => p.CanView,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS");

            using var connection = CreateConnection();
            var sql = @"SELECT cc.CostCenterID, cc.CostCenterCode, cc.CostCenterNameAr, cc.CostCenterNameEn, cc.ParentCostCenterID, p.CostCenterNameAr AS ParentNameAr,
                               cc.CenterLevel, cc.DepartmentID, d.DepartmentNameAr, cc.IsActive, cc.CreatedDate,
                               (SELECT COUNT(*) FROM dbo.CostCenters c WHERE c.ParentCostCenterID=cc.CostCenterID) AS ChildrenCount
                        FROM dbo.CostCenters cc
                        LEFT JOIN dbo.CostCenters p ON cc.ParentCostCenterID=p.CostCenterID
                        LEFT JOIN dbo.Departments d ON cc.DepartmentID=d.DepartmentID
                        ORDER BY cc.CostCenterCode";
            var all = (await connection.QueryAsync<CostCenterListDto>(sql)).ToList();
            var lookup = all.ToDictionary(x => x.CostCenterID);
            var roots = new List<CostCenterListDto>();
            foreach (var cc in all)
            {
                if (cc.ParentCostCenterID.HasValue && lookup.ContainsKey(cc.ParentCostCenterID.Value))
                    lookup[cc.ParentCostCenterID.Value].Children.Add(cc);
                else
                    roots.Add(cc);
            }
            return roots;
        }

        public async Task<List<CostCenterListDto>> GetCostCentersFlatAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية استخدام مراكز التكلفة.",
                p => p.CanView || p.CanAdd || p.CanEdit,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS", "SCR_EXPENSES", "SCR_EXPENSE", "SCR_OVERHEAD", "SCR_OVERHEAD_EXPENSES", "SCR_RECV", "SCR_PAYV");

            return await GetCostCentersFlatCoreAsync(rethrowFinalReadFailure: false);
        }

        public async Task<List<CostCenterListDto>> GetCostCentersForExpenseEditAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية استخدام مراكز التكلفة.",
                p => p.CanView || p.CanAdd || p.CanEdit,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS", "SCR_EXPENSES", "SCR_EXPENSE", "SCR_OVERHEAD", "SCR_OVERHEAD_EXPENSES", "SCR_RECV", "SCR_PAYV");

            return await GetCostCentersFlatCoreAsync(rethrowFinalReadFailure: true);
        }

        public async Task<List<CostCenterListDto>> GetCostCentersForCostCenterManagementAsync()
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية استخدام مراكز التكلفة.",
                p => p.CanView || p.CanAdd || p.CanEdit,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS");

            return await GetCostCentersFlatCoreAsync(rethrowFinalReadFailure: true);
        }

        private async Task<List<CostCenterListDto>> GetCostCentersFlatCoreAsync(bool rethrowFinalReadFailure)
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT CostCenterID, CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, IsActive, CreatedDate, 0 AS ChildrenCount
                        FROM dbo.CostCenters WHERE IsActive=1 ORDER BY CostCenterCode";
                var result = await connection.QueryAsync<CostCenterListDto>(sql);
                return result.ToList();
            }
            catch (Exception primaryQueryException)
            {
                _logger.LogWarning(primaryQueryException,
                    "Failed to load active cost centers with the IsActive filter; attempting the legacy query.");
                try
                {
                    var sql2 = @"SELECT CostCenterID, CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, 1 AS IsActive, CreatedDate, 0 AS ChildrenCount
                        FROM dbo.CostCenters ORDER BY CostCenterCode";
                    var result = await connection.QueryAsync<CostCenterListDto>(sql2);
                    return result.ToList();
                }
                catch (Exception fallbackException)
                {
                    _logger.LogError(fallbackException,
                        "Failed to load cost centers with both current and legacy queries.");
                    if (rethrowFinalReadFailure)
                        throw;

                    return new List<CostCenterListDto>();
                }
            }
        }

        public async Task<CostCenterDto?> GetCostCenterByIdAsync(int id)
        {
            await RequireCurrentAnyModulePermissionAsync(
                "ليس لديك صلاحية عرض تفاصيل مركز التكلفة.",
                p => p.CanView,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS");

            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<CostCenterDto>("SELECT CostCenterID, CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, IsActive, CreatedDate FROM dbo.CostCenters WHERE CostCenterID=@ID", new { ID = id });
        }

        public async Task<int> InsertCostCenterAsync(CostCenterDto dto, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية إنشاء مراكز التكلفة.", p => p.CanAdd,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS");

            using var connection = CreateConnection();
            var parentLevel = 0;
            if (dto.ParentCostCenterID.HasValue && dto.ParentCostCenterID > 0)
                parentLevel = await connection.ExecuteScalarAsync<int>("SELECT ISNULL(CenterLevel,1) FROM dbo.CostCenters WHERE CostCenterID=@ID", new { ID = dto.ParentCostCenterID });
            dto.CenterLevel = parentLevel + 1;

            var sql = @"INSERT INTO dbo.CostCenters(CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, IsActive, CreatedDate)
                        VALUES(@CostCenterCode, @CostCenterNameAr, @CostCenterNameEn, NULLIF(@ParentCostCenterID,0), @CenterLevel, NULLIF(@DepartmentID,0), @IsActive, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.CostCenterCode,
                dto.CostCenterNameAr,
                dto.CostCenterNameEn,
                dto.ParentCostCenterID,
                dto.CenterLevel,
                dto.DepartmentID,
                dto.IsActive
            });
            await _audit.WriteAuditLogAsync(userId, 1, "CostCenters", newId.ToString(), moduleName: "SCR_COST", description: $"إنشاء مركز تكلفة: {dto.CostCenterNameAr}");
            return newId;
        }

        public async Task UpdateCostCenterAsync(CostCenterDto dto, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية تعديل مراكز التكلفة.", p => p.CanEdit,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS");
            if (dto.CostCenterID <= 0) throw new ArgumentOutOfRangeException(nameof(dto.CostCenterID));
            if (string.IsNullOrWhiteSpace(dto.CostCenterNameAr))
                throw new InvalidOperationException("اسم مركز التكلفة العربي مطلوب.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var centers = (await connection.QueryAsync<(
                    int CostCenterID, string? CostCenterCode, int? ParentCostCenterID, bool IsActive)>(
                    @"SELECT CostCenterID, CostCenterCode, ParentCostCenterID,
                             CONVERT(bit,ISNULL(IsActive,1)) AS IsActive
                      FROM dbo.CostCenters WITH (UPDLOCK,HOLDLOCK)", transaction: trans))
                    .ToDictionary(center => center.CostCenterID);

                if (!centers.TryGetValue(dto.CostCenterID, out var current))
                    throw new InvalidOperationException("مركز التكلفة غير موجود.");

                var requestedCode = string.IsNullOrWhiteSpace(dto.CostCenterCode)
                    ? null
                    : dto.CostCenterCode.Trim();
                if (!string.IsNullOrWhiteSpace(requestedCode))
                {
                    var duplicateCode = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(1) FROM dbo.CostCenters WITH (UPDLOCK,HOLDLOCK)
                          WHERE LTRIM(RTRIM(CostCenterCode))=@Code AND CostCenterID<>@ID",
                        new { Code = requestedCode, ID = dto.CostCenterID }, trans);
                    if (duplicateCode > 0)
                        throw new InvalidOperationException("كود مركز التكلفة مستخدم بالفعل.");
                }

                int? newParentId = dto.ParentCostCenterID.GetValueOrDefault() > 0
                    ? dto.ParentCostCenterID.GetValueOrDefault()
                    : null;
                int? oldParentId = current.ParentCostCenterID.GetValueOrDefault() > 0
                    ? current.ParentCostCenterID.GetValueOrDefault()
                    : null;

                var childrenByParent = centers.Values
                    .Where(center => center.ParentCostCenterID.HasValue)
                    .GroupBy(center => center.ParentCostCenterID!.Value)
                    .ToDictionary(group => group.Key, group => group.ToList());
                var descendantDepth = new Dictionary<int, int> { [dto.CostCenterID] = 0 };
                var pending = new Queue<int>();
                pending.Enqueue(dto.CostCenterID);
                while (pending.Count > 0)
                {
                    var parentId = pending.Dequeue();
                    if (!childrenByParent.TryGetValue(parentId, out var children)) continue;
                    foreach (var child in children)
                    {
                        if (!descendantDepth.TryAdd(child.CostCenterID, descendantDepth[parentId] + 1))
                            throw new InvalidOperationException("توجد دورة في شجرة مراكز التكلفة؛ أُوقف التعديل.");
                        pending.Enqueue(child.CostCenterID);
                    }
                }

                var newCenterLevel = 1;
                if (newParentId.HasValue)
                {
                    if (!centers.TryGetValue(newParentId.Value, out var parent))
                        throw new InvalidOperationException("مركز التكلفة الأب غير موجود.");
                    if (!parent.IsActive && newParentId != oldParentId)
                        throw new InvalidOperationException("لا يمكن نقل المركز إلى أب غير نشط.");
                    if (descendantDepth.ContainsKey(newParentId.Value))
                        throw new InvalidOperationException("لا يمكن جعل المركز الحالي أو أحد أبنائه أبًا له.");

                    var ancestorPath = new HashSet<int>();
                    int? ancestorId = newParentId;
                    while (ancestorId.HasValue && ancestorId.Value > 0)
                    {
                        if (!ancestorPath.Add(ancestorId.Value))
                            throw new InvalidOperationException("توجد دورة سابقة في سلسلة الآباء؛ أُوقف التعديل.");
                        if (!centers.TryGetValue(ancestorId.Value, out var ancestor))
                            throw new InvalidOperationException("سلسلة الآباء تحتوي مركز تكلفة غير موجود.");
                        newCenterLevel++;
                        ancestorId = ancestor.ParentCostCenterID.GetValueOrDefault() > 0
                            ? ancestor.ParentCostCenterID
                            : null;
                    }
                }

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.CostCenters SET
                        CostCenterCode=@CostCenterCode, ParentCostCenterID=@ParentCostCenterID,
                        CenterLevel=@CenterLevel, CostCenterNameAr=@CostCenterNameAr,
                        CostCenterNameEn=@CostCenterNameEn, DepartmentID=NULLIF(@DepartmentID,0), IsActive=@IsActive
                      WHERE CostCenterID=@CostCenterID",
                    new
                    {
                        CostCenterCode = requestedCode,
                        ParentCostCenterID = newParentId,
                        CenterLevel = newCenterLevel,
                        dto.CostCenterNameAr,
                        dto.CostCenterNameEn,
                        dto.DepartmentID,
                        dto.IsActive,
                        dto.CostCenterID
                    }, trans);
                if (updated != 1) throw new InvalidOperationException("لم يتم تحديث مركز التكلفة؛ أُلغيت العملية.");

                foreach (var descendant in descendantDepth.Where(entry => entry.Key != dto.CostCenterID).OrderBy(entry => entry.Value))
                {
                    var levelUpdated = await connection.ExecuteAsync(
                        "UPDATE dbo.CostCenters SET CenterLevel=@Level WHERE CostCenterID=@ID",
                        new { Level = newCenterLevel + descendant.Value, ID = descendant.Key }, trans);
                    if (levelUpdated != 1)
                        throw new InvalidOperationException("تعذر تحديث مستوى أحد الأبناء؛ أُلغيت العملية.");
                }

                trans.Commit();
            }
            catch
            {
                try
                {
                    trans.Rollback();
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed while handling cost-center update for cost center {CostCenterID}.",
                        dto.CostCenterID);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "CostCenters", dto.CostCenterID.ToString(), moduleName: "SCR_COST", description: $"تعديل مركز تكلفة: {dto.CostCenterNameAr}");
        }

        public async Task DeleteCostCenterAsync(int id, int userId)
        {
            await RequireAnyModulePermissionAsync(
                userId, "ليس لديك صلاحية حذف مراكز التكلفة.", p => p.CanDelete,
                "SCR_COST", "SCR_COSTCENTERS", "SCR_COST_CENTERS");

            using var connection = CreateConnection();
            var hasChildren = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.CostCenters WHERE ParentCostCenterID=@ID", new { ID = id });
            if (hasChildren > 0) throw new Exception("لا يمكن حذف مركز له مراكز فرعية");
            var hasTrans = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.JournalEntryDetails WHERE CostCenterID=@ID", new { ID = id });
            if (hasTrans > 0) throw new Exception("لا يمكن حذف مركز به حركات محاسبية");
            await connection.ExecuteAsync("DELETE FROM dbo.CostCenters WHERE CostCenterID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "CostCenters", id.ToString(), moduleName: "SCR_COST", description: "حذف مركز تكلفة");
        }
    }
}
