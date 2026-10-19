using System.Net;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    /// <summary>
    /// عروض الأسعار. يعتمد على الأعمدة الأساسية التي يستخدمها ربط أوامر البيع،
    /// ويكتشف الحقول الاختيارية من مخطط قاعدة البيانات بدلاً من افتراض وجودها.
    /// </summary>
    public sealed class SalesQuotationService : BaseDbService
    {
        private const string HeaderTable = "SalesQuotations";
        private const string DetailTable = "SalesQuotationDetails";
        private const string ModuleCode = "SCR_QUOT";

        private readonly AuditService _audit;
        private readonly NotificationService _notifications;
        private readonly ModuleAuthorizationService _authorization;

        public SalesQuotationService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notifications,
            ModuleAuthorizationService authorization) : base(configuration)
        {
            _audit = audit;
            _notifications = notifications;
            _authorization = authorization;
        }

        public async Task<List<SalesQuotationListDto>> GetQuotationsListAsync()
        {
            using var connection = CreateConnection();
            var headerSchema = await LoadTableSchemaAsync(connection, null, HeaderTable);
            var detailSchema = await LoadTableSchemaAsync(connection, null, DetailTable);
            EnsureColumns(headerSchema, HeaderTable, "QuotationID", "QuotationNumber", "QuotationDate", "CustomerID", "QuotationStatus", "TotalAmount");
            EnsureColumns(detailSchema, DetailTable, "QuotationID", "Quantity", "UnitPrice");

            var validUntil = Projection(headerSchema, "sq", "CAST(NULL AS date)", "ValidUntilDate", "ValidUntil", "ExpiryDate", "ExpirationDate");
            var quantity = NumberProjection(detailSchema, "qd", "CAST(0 AS decimal(18,4))", "Quantity");
            var unitPrice = NumberProjection(detailSchema, "qd", "CAST(0 AS decimal(18,4))", "UnitPrice");
            var discountRate = NumberProjection(detailSchema, "qd", "CAST(0 AS decimal(5,2))", "DiscountPercent");
            var taxRate = NumberProjection(detailSchema, "qd", "CAST(14 AS decimal(5,2))", "TaxRate");
            var grossLine = $"({quantity} * {unitPrice})";
            var discountLine = $"ROUND({grossLine} * {discountRate} / 100.0, 2)";
            var netLine = $"({grossLine} - {discountLine})";
            var taxLine = $"ROUND({netLine} * {taxRate} / 100.0, 2)";
            var detailSubTotal = $"ISNULL((SELECT SUM({netLine}) FROM dbo.{DetailTable} qd WHERE qd.[QuotationID] = sq.[QuotationID]), 0)";
            var detailDiscount = $"ISNULL((SELECT SUM({discountLine}) FROM dbo.{DetailTable} qd WHERE qd.[QuotationID] = sq.[QuotationID]), 0)";
            var detailTax = $"ISNULL((SELECT SUM({taxLine}) FROM dbo.{DetailTable} qd WHERE qd.[QuotationID] = sq.[QuotationID]), 0)";
            var subTotal = NumberProjection(headerSchema, "sq", detailSubTotal, "SubTotal", "Subtotal");
            var discount = NumberProjection(headerSchema, "sq", detailDiscount, "DiscountAmount");
            var tax = NumberProjection(headerSchema, "sq", detailTax, "TaxAmount");
            var createdDate = Projection(headerSchema, "sq", "CAST(NULL AS datetime2)", "CreatedDate");

            var sql = $@"
SELECT
    sq.[QuotationID],
    sq.[QuotationNumber],
    sq.[QuotationDate],
    {validUntil} AS ValidUntilDate,
    sq.[QuotationStatus],
    sq.[CustomerID],
    c.[CustomerCode],
    c.[CustomerNameAr],
    {subTotal} AS SubTotal,
    {discount} AS DiscountAmount,
    {tax} AS TaxAmount,
    ISNULL(sq.[TotalAmount], 0) AS TotalAmount,
    (SELECT COUNT(1) FROM dbo.{DetailTable} qd WHERE qd.[QuotationID] = sq.[QuotationID]) AS ItemCount,
    (SELECT ISNULL(SUM({quantity}), 0) FROM dbo.{DetailTable} qd WHERE qd.[QuotationID] = sq.[QuotationID]) AS TotalQuantity,
    {createdDate} AS CreatedDate
FROM dbo.{HeaderTable} sq
INNER JOIN dbo.Customers c ON c.[CustomerID] = sq.[CustomerID]
ORDER BY sq.[QuotationDate] DESC, sq.[QuotationID] DESC";

            var rows = await connection.QueryAsync<SalesQuotationListDto>(sql);
            return rows.ToList();
        }

        public async Task<SalesQuotationHeaderDto?> GetQuotationByIdAsync(int quotationId)
        {
            using var connection = CreateConnection();
            var schema = await LoadTableSchemaAsync(connection, null, HeaderTable);
            EnsureColumns(schema, HeaderTable, "QuotationID", "QuotationNumber", "QuotationDate", "CustomerID", "QuotationStatus", "TotalAmount");
            var validUntil = Projection(schema, "sq", "CAST(NULL AS date)", "ValidUntilDate", "ValidUntil", "ExpiryDate", "ExpirationDate");
            var subTotal = NumberProjection(schema, "sq", "ISNULL(sq.[TotalAmount], 0)", "SubTotal", "Subtotal");
            var discountPercent = NumberProjection(schema, "sq", "CAST(0 AS decimal(5,2))", "DiscountPercent");
            var discountAmount = NumberProjection(schema, "sq", "CAST(0 AS decimal(18,2))", "DiscountAmount");
            var taxAmount = NumberProjection(schema, "sq", "CAST(0 AS decimal(18,2))", "TaxAmount");
            var shipping = NumberProjection(schema, "sq", "CAST(0 AS decimal(18,2))", "ShippingCost");
            var salesRep = Projection(schema, "sq", "CAST(NULL AS int)", "SalesRepID");
            var notes = Projection(schema, "sq", "CAST(NULL AS nvarchar(max))", "Notes");
            var createdBy = Projection(schema, "sq", "CAST(NULL AS int)", "CreatedBy");
            var createdDate = Projection(schema, "sq", "CAST(NULL AS datetime2)", "CreatedDate");
            var modifiedBy = Projection(schema, "sq", "CAST(NULL AS int)", "ModifiedBy");
            var modifiedDate = Projection(schema, "sq", "CAST(NULL AS datetime2)", "ModifiedDate");
            var submittedBy = Projection(schema, "sq", "CAST(NULL AS int)", "SubmittedBy");
            var submittedDate = Projection(schema, "sq", "CAST(NULL AS datetime2)", "SubmittedDate");
            var approvedBy = Projection(schema, "sq", "CAST(NULL AS int)", "ApprovedBy");
            var approvedDate = Projection(schema, "sq", "CAST(NULL AS datetime2)", "ApprovedDate");
            var rejectedBy = Projection(schema, "sq", "CAST(NULL AS int)", "RejectedBy");
            var rejectedDate = Projection(schema, "sq", "CAST(NULL AS datetime2)", "RejectedDate");
            var rejectionReason = Projection(schema, "sq", "CAST(NULL AS nvarchar(500))", "RejectionReason", "RejectReason");
            var sentBy = Projection(schema, "sq", "CAST(NULL AS int)", "SentBy");
            var sentDate = Projection(schema, "sq", "CAST(NULL AS datetime2)", "SentDate");

            var sql = $@"
SELECT
    sq.[QuotationID], sq.[QuotationNumber], sq.[QuotationDate], {validUntil} AS ValidUntilDate,
    sq.[QuotationStatus], sq.[CustomerID], c.[CustomerCode], c.[CustomerNameAr],
    {salesRep} AS SalesRepID,
    {subTotal} AS SubTotal, {discountPercent} AS DiscountPercent, {discountAmount} AS DiscountAmount,
    {taxAmount} AS TaxAmount, {shipping} AS ShippingCost, ISNULL(sq.[TotalAmount], 0) AS TotalAmount,
    {notes} AS Notes,
    {createdBy} AS CreatedBy, {createdDate} AS CreatedDate,
    {modifiedBy} AS ModifiedBy, {modifiedDate} AS ModifiedDate,
    {submittedBy} AS SubmittedBy, {submittedDate} AS SubmittedDate,
    {approvedBy} AS ApprovedBy, {approvedDate} AS ApprovedDate,
    {rejectedBy} AS RejectedBy, {rejectedDate} AS RejectedDate, {rejectionReason} AS RejectionReason,
    {sentBy} AS SentBy, {sentDate} AS SentDate
FROM dbo.{HeaderTable} sq
INNER JOIN dbo.Customers c ON c.[CustomerID] = sq.[CustomerID]
WHERE sq.[QuotationID] = @ID";

            var quotation = await connection.QueryFirstOrDefaultAsync<SalesQuotationHeaderDto>(sql, new { ID = quotationId });
            if (quotation is null) return null;

            if (!schema.HasAny("SubTotal", "Subtotal") || !schema.HasAny("DiscountAmount") || !schema.HasAny("TaxAmount"))
            {
                var detailLines = await GetQuotationDetailsAsync(quotationId);
                var totals = CalculateTotals(detailLines);
                if (!schema.HasAny("SubTotal", "Subtotal")) quotation.SubTotal = totals.SubTotal;
                if (!schema.HasAny("DiscountAmount")) quotation.DiscountAmount = totals.DiscountAmount;
                if (!schema.HasAny("TaxAmount")) quotation.TaxAmount = totals.TaxAmount;
            }

            return quotation;
        }

        public async Task<List<SalesQuotationDetailDto>> GetQuotationDetailsAsync(int quotationId)
        {
            using var connection = CreateConnection();
            var schema = await LoadTableSchemaAsync(connection, null, DetailTable);
            EnsureColumns(schema, DetailTable, "QuotationDetailID", "QuotationID", "LineNumber", "ItemID", "UnitID", "Quantity", "UnitPrice");

            var discount = NumberProjection(schema, "qd", "CAST(0 AS decimal(5,2))", "DiscountPercent");
            var taxRate = NumberProjection(schema, "qd", "CAST(14 AS decimal(5,2))", "TaxRate");
            var notes = Projection(schema, "qd", "CAST(NULL AS nvarchar(200))", "Notes");
            var sql = $@"
SELECT
    qd.[QuotationDetailID], qd.[QuotationID], qd.[LineNumber],
    qd.[ItemID], i.[ItemCode], i.[ItemNameAr],
    qd.[UnitID], u.[UnitNameAr] AS UnitName,
    ISNULL(qd.[Quantity], 0) AS Quantity,
    ISNULL(qd.[UnitPrice], 0) AS UnitPrice,
    {discount} AS DiscountPercent,
    {taxRate} AS TaxRate,
    {notes} AS Notes
FROM dbo.{DetailTable} qd
INNER JOIN dbo.Items i ON i.[ItemID] = qd.[ItemID]
INNER JOIN dbo.Units u ON u.[UnitID] = qd.[UnitID]
WHERE qd.[QuotationID] = @ID
ORDER BY qd.[LineNumber], qd.[QuotationDetailID]";

            var rows = await connection.QueryAsync<SalesQuotationDetailDto>(sql, new { ID = quotationId });
            return rows.ToList();
        }

        public async Task<string> GenerateQuotationNumberAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var generated = await connection.QueryFirstOrDefaultAsync<string>(
                    @"DECLARE @NextNum NVARCHAR(50);
                      EXEC dbo.sp_GetNextNumber N'SQ', @NextNum OUTPUT;
                      SELECT @NextNum;");
                if (string.IsNullOrWhiteSpace(generated))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لعرض السعر.");

                return generated;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم عرض السعر (SQ). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل SQ والإجراء المخزن.", ex);
            }
        }

        public async Task<int> SaveQuotationAsync(
            SalesQuotationHeaderDto header,
            IReadOnlyList<SalesQuotationDetailDto> details,
            int userId)
        {
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(details);

            if (header.CustomerID <= 0) throw new InvalidOperationException("اختر العميل أولاً");
            if (header.QuotationDate == default) header.QuotationDate = DateTime.Today;
            if (header.ValidUntilDate.HasValue && header.ValidUntilDate.Value.Date < header.QuotationDate.Date)
                throw new InvalidOperationException("تاريخ الصلاحية لا يمكن أن يسبق تاريخ العرض");
            ValidateDetails(details);

            var isNew = header.QuotationID <= 0;
            if (isNew && string.IsNullOrWhiteSpace(header.QuotationNumber))
                header.QuotationNumber = await GenerateQuotationNumberAsync();

            var totals = CalculateTotals(details);
            header.SubTotal = totals.SubTotal;
            header.DiscountAmount = totals.DiscountAmount;
            header.TaxAmount = totals.TaxAmount;
            header.TotalAmount = totals.TotalAmount;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            int quotationId;

            try
            {
                var headerSchema = await LoadTableSchemaAsync(connection, transaction, HeaderTable);
                var detailSchema = await LoadTableSchemaAsync(connection, transaction, DetailTable);
                EnsureColumns(headerSchema, HeaderTable, "QuotationID", "QuotationNumber", "QuotationDate", "CustomerID", "QuotationStatus", "TotalAmount");
                EnsureColumns(detailSchema, DetailTable, "QuotationDetailID", "QuotationID", "LineNumber", "ItemID", "UnitID", "Quantity", "UnitPrice");
                ValidateOptionalFields(header, details, headerSchema, detailSchema);

                if (isNew)
                {
                    header.QuotationStatus = 1;
                    var values = BuildHeaderValues(headerSchema, header, totals, userId, isNew: true);
                    EnsureRequiredValues(headerSchema, HeaderTable, values);
                    var insertSql = BuildInsertSql(HeaderTable, headerSchema, values, "QuotationID");
                    quotationId = await connection.QuerySingleAsync<int>(insertSql, ToParameters(values), transaction);
                    header.QuotationID = quotationId;
                }
                else
                {
                    var currentStatus = await connection.QueryFirstOrDefaultAsync<int?>(
                        $"SELECT [QuotationStatus] FROM dbo.{HeaderTable} WITH (UPDLOCK, HOLDLOCK) WHERE [QuotationID] = @ID",
                        new { ID = header.QuotationID }, transaction);
                    if (!currentStatus.HasValue) throw new InvalidOperationException("عرض السعر غير موجود");
                    if (currentStatus.Value != 1) throw new InvalidOperationException("يمكن تعديل عروض الأسعار المسودة فقط");

                    header.QuotationStatus = currentStatus.Value;
                    quotationId = header.QuotationID;
                    var values = BuildHeaderValues(headerSchema, header, totals, userId, isNew: false);
                    var updateSql = BuildUpdateSql(HeaderTable, headerSchema, values, "QuotationID");
                    var affected = await connection.ExecuteAsync(updateSql, ToParameters(values, quotationId), transaction);
                    if (affected == 0) throw new InvalidOperationException("تعذر حفظ بيانات العرض");
                    await connection.ExecuteAsync(
                        $"DELETE FROM dbo.{DetailTable} WHERE [QuotationID] = @ID",
                        new { ID = quotationId }, transaction);
                }

                var lineNumber = 0;
                foreach (var line in details)
                {
                    lineNumber++;
                    var values = BuildDetailValues(detailSchema, line, quotationId, lineNumber);
                    EnsureRequiredValues(detailSchema, DetailTable, values);
                    var insertSql = BuildInsertSql(DetailTable, detailSchema, values, outputColumn: null);
                    await connection.ExecuteAsync(insertSql, ToParameters(values), transaction);
                    line.LineNumber = lineNumber;
                    line.QuotationID = quotationId;
                }

                transaction.Commit();
                header.QuotationID = quotationId;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId,
                isNew ? (byte)1 : (byte)2,
                HeaderTable,
                quotationId.ToString(),
                moduleName: ModuleCode,
                description: isNew ? $"إنشاء عرض سعر: {header.QuotationNumber}" : $"تعديل عرض سعر: {header.QuotationNumber}");

            return quotationId;
        }

        public async Task DeleteQuotationAsync(int quotationId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            string quotationNumber;

            try
            {
                var headerSchema = await LoadTableSchemaAsync(connection, transaction, HeaderTable);
                var detailSchema = await LoadTableSchemaAsync(connection, transaction, DetailTable);
                EnsureColumns(headerSchema, HeaderTable, "QuotationID", "QuotationNumber", "QuotationStatus");
                EnsureColumns(detailSchema, DetailTable, "QuotationID");

                var row = await connection.QueryFirstOrDefaultAsync<QuotationWorkflowInfo>(
                    $"SELECT [QuotationNumber], [QuotationStatus], CAST(NULL AS int) AS CreatedBy, 0 AS DetailCount FROM dbo.{HeaderTable} WITH (UPDLOCK, HOLDLOCK) WHERE [QuotationID]=@ID",
                    new { ID = quotationId }, transaction);
                if (row is null) throw new InvalidOperationException("عرض السعر غير موجود");
                if (row.QuotationStatus != 1) throw new InvalidOperationException("يمكن حذف عروض الأسعار المسودة فقط");
                quotationNumber = row.QuotationNumber ?? quotationId.ToString();

                await connection.ExecuteAsync($"DELETE FROM dbo.{DetailTable} WHERE [QuotationID]=@ID", new { ID = quotationId }, transaction);
                var deleted = await connection.ExecuteAsync($"DELETE FROM dbo.{HeaderTable} WHERE [QuotationID]=@ID", new { ID = quotationId }, transaction);
                if (deleted == 0) throw new InvalidOperationException("تعذر حذف عرض السعر");
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, HeaderTable, quotationId.ToString(), moduleName: ModuleCode, description: $"حذف عرض سعر: {quotationNumber}");
        }

        public async Task SubmitForApprovalAsync(int quotationId, int userId)
        {
            QuotationWorkflowInfo info;
            using (var connection = CreateConnection())
            {
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();
                try
                {
                    var headerSchema = await LoadTableSchemaAsync(connection, transaction, HeaderTable);
                    var detailSchema = await LoadTableSchemaAsync(connection, transaction, DetailTable);
                    EnsureColumns(headerSchema, HeaderTable, "QuotationID", "QuotationNumber", "QuotationStatus", "TotalAmount");
                    EnsureColumns(detailSchema, DetailTable, "QuotationID");
                    info = await LoadWorkflowInfoAsync(connection, transaction, headerSchema, quotationId)
                        ?? throw new InvalidOperationException("عرض السعر غير موجود");
                    if (info.QuotationStatus != 1) throw new InvalidOperationException("يمكن إرسال المسودة للاعتماد فقط");
                    info.DetailCount = await connection.QueryFirstOrDefaultAsync<int>(
                        $"SELECT COUNT(1) FROM dbo.{DetailTable} WHERE [QuotationID]=@ID", new { ID = quotationId }, transaction);
                    if (info.DetailCount == 0) throw new InvalidOperationException("أضف صنفاً واحداً على الأقل قبل الإرسال للاعتماد");

                    await UpdateWorkflowFieldsAsync(connection, transaction, headerSchema, quotationId, userId, 2, WorkflowAction.Submit, null);
                    transaction.Commit();
                }
                catch
                {
                    try { transaction.Rollback(); } catch { }
                    throw;
                }
            }

            try
            {
                await _notifications.CreateForPermissionHoldersAsync(
                    notificationType: 3,
                    title: "عرض سعر بانتظار الاعتماد",
                    message: $"عرض السعر رقم {info.QuotationNumber} بانتظار الاعتماد",
                    priority: 1,
                    permissionModuleCode: ModuleCode,
                    requiredPermission: ModulePermissionRequirement.Approve,
                    relatedModule: ModuleCode,
                    relatedRecordId: quotationId,
                    createdBy: userId);
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, HeaderTable, quotationId.ToString(), moduleName: ModuleCode, description: $"إرسال عرض سعر للاعتماد: {info.QuotationNumber}");
        }

        public async Task ApproveAsync(int quotationId, int userId)
        {
            var info = await ChangeWorkflowStatusAsync(quotationId, userId, expectedStatus: 2, newStatus: 3, WorkflowAction.Approve, null);
            try { await _notifications.MarkRelatedAsActionedAsync(ModuleCode, quotationId); } catch { }
            try
            {
                if (info.CreatedBy.HasValue && info.CreatedBy.Value > 0)
                {
                    await _notifications.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم اعتماد عرض السعر",
                        message: $"تم اعتماد عرض السعر رقم {info.QuotationNumber}",
                        priority: 2,
                        targetUserId: info.CreatedBy.Value,
                        permissionModuleCode: ModuleCode,
                        relatedModule: ModuleCode,
                        relatedRecordId: quotationId,
                        createdBy: userId);
                }
            }
            catch { }
            await _audit.WriteAuditLogAsync(userId, 2, HeaderTable, quotationId.ToString(), moduleName: ModuleCode, description: $"اعتماد عرض سعر: {info.QuotationNumber}");
        }

        public async Task RejectAsync(int quotationId, string reason, int userId)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("أدخل سبب الرفض");
            if (reason.Trim().Length > 500) throw new InvalidOperationException("سبب الرفض يتجاوز الحد المسموح (500 حرف)");

            var info = await ChangeWorkflowStatusAsync(quotationId, userId, expectedStatus: 2, newStatus: 4, WorkflowAction.Reject, reason.Trim());
            try { await _notifications.MarkRelatedAsActionedAsync(ModuleCode, quotationId); } catch { }
            try
            {
                if (info.CreatedBy.HasValue && info.CreatedBy.Value > 0)
                {
                    await _notifications.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم رفض عرض السعر",
                        message: $"تم رفض عرض السعر رقم {info.QuotationNumber}. السبب: {reason.Trim()}",
                        priority: 1,
                        targetUserId: info.CreatedBy.Value,
                        permissionModuleCode: ModuleCode,
                        relatedModule: ModuleCode,
                        relatedRecordId: quotationId,
                        createdBy: userId);
                }
            }
            catch { }
            await _audit.WriteAuditLogAsync(userId, 2, HeaderTable, quotationId.ToString(), moduleName: ModuleCode, description: $"رفض عرض سعر: {info.QuotationNumber} - {reason.Trim()}");
        }

        /// <summary>يجلب بيانات التواصل فقط لمن يستطيع إرسال عرض السعر المعتمد.</summary>
        public async Task<SalesQuotationCustomerContactDto> GetCustomerContactForManualSendAsync(int quotationId, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, ModuleCode, permission => permission.CanEdit || permission.CanApprove,
                "لا تملك صلاحية إرسال عروض الأسعار للعميل.");
            if (quotationId <= 0) throw new ArgumentOutOfRangeException(nameof(quotationId));

            using var connection = CreateConnection();
            var headerSchema = await LoadTableSchemaAsync(connection, null, HeaderTable);
            var customerSchema = await LoadTableSchemaAsync(connection, null, "Customers");
            EnsureColumns(headerSchema, HeaderTable, "QuotationID", "CustomerID", "QuotationStatus");
            EnsureColumns(customerSchema, "Customers", "CustomerID");

            var phoneExpressions = new[] { "Mobile", "Phone1", "Phone" }
                .Where(customerSchema.Contains)
                .Select(name => $"NULLIF(LTRIM(RTRIM(c.{QuoteIdentifier(name)})), N'')")
                .ToArray();
            var phoneProjection = phoneExpressions.Length == 0
                ? "CAST(NULL AS nvarchar(100))"
                : $"COALESCE({string.Join(", ", phoneExpressions)}, CAST(NULL AS nvarchar(100)))";
            var emailProjection = Projection(customerSchema, "c", "CAST(NULL AS nvarchar(255))", "Email");
            var sql = $@"SELECT {phoneProjection} AS Phone, {emailProjection} AS Email
                         FROM dbo.{HeaderTable} sq
                         INNER JOIN dbo.Customers c ON c.[CustomerID]=sq.[CustomerID]
                         WHERE sq.[QuotationID]=@ID AND sq.[QuotationStatus]=3";
            return await connection.QueryFirstOrDefaultAsync<SalesQuotationCustomerContactDto>(sql, new { ID = quotationId })
                ?? throw new InvalidOperationException("يجب أن يكون عرض السعر معتمداً قبل بدء الإرسال");
        }

        /// <summary>يسجل الإرسال اليدوي بعد تأكيد المستخدم، من دون إرسال خارجي آلي.</summary>
        public async Task MarkSentToCustomerAsync(int quotationId, int userId, string channel, string recipient)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, ModuleCode, permission => permission.CanEdit || permission.CanApprove,
                "لا تملك صلاحية تسجيل إرسال عروض الأسعار.");
            var channelValue = channel?.Trim();
            var isEmail = string.Equals(channelValue, "Email", StringComparison.OrdinalIgnoreCase);
            var isWhatsApp = string.Equals(channelValue, "WhatsApp", StringComparison.OrdinalIgnoreCase);
            if (!isEmail && !isWhatsApp)
                throw new InvalidOperationException("اختر قناة إرسال صحيحة (واتساب أو بريد إلكتروني)");
            if (string.IsNullOrWhiteSpace(recipient))
                throw new InvalidOperationException("بيانات مستلم الإرسال مطلوبة");

            var normalizedRecipient = recipient.Trim();
            if (isEmail)
            {
                var atIndex = normalizedRecipient.IndexOf('@');
                if (atIndex <= 0 || normalizedRecipient.LastIndexOf('.') <= atIndex + 1)
                    throw new InvalidOperationException("عنوان البريد الإلكتروني غير صالح");
            }
            else
            {
                var phoneDigits = new string(normalizedRecipient.Where(character => character is >= '0' and <= '9').ToArray());
                if (phoneDigits.Length < 8)
                    throw new InvalidOperationException("رقم الهاتف غير صالح؛ أدخل الرقم كاملاً مع مفتاح الدولة");
            }

            var normalizedChannel = isEmail ? "البريد الإلكتروني" : "واتساب";
            var info = await ChangeWorkflowStatusAsync(quotationId, userId, expectedStatus: 3, newStatus: 5, WorkflowAction.MarkSent, null);
            await _audit.WriteAuditLogAsync(
                userId, 2, HeaderTable, quotationId.ToString(), moduleName: ModuleCode,
                description: $"تسجيل إرسال يدوي لعرض السعر {info.QuotationNumber} | القناة: {normalizedChannel} | المستلم: {normalizedRecipient}");
        }

        public async Task<byte[]> ExportToExcelAsync(IReadOnlyList<SalesQuotationListDto> quotations, int userId)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("عروض الأسعار");
            sheet.RightToLeft = true;
            sheet.Cell(1, 1).Value = "رقم العرض";
            sheet.Cell(1, 2).Value = "التاريخ";
            sheet.Cell(1, 3).Value = "العميل";
            sheet.Cell(1, 4).Value = "الحالة";
            sheet.Cell(1, 5).Value = "عدد الأصناف";
            sheet.Cell(1, 6).Value = "قبل الضريبة";
            sheet.Cell(1, 7).Value = "الخصم";
            sheet.Cell(1, 8).Value = "الضريبة";
            sheet.Cell(1, 9).Value = "الإجمالي";

            var header = sheet.Range(1, 1, 1, 9);
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#181818");
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var row = 2;
            foreach (var quotation in quotations)
            {
                sheet.Cell(row, 1).Value = quotation.QuotationNumber ?? string.Empty;
                sheet.Cell(row, 2).Value = quotation.QuotationDate;
                sheet.Cell(row, 2).Style.DateFormat.Format = "dd/mm/yyyy";
                sheet.Cell(row, 3).Value = quotation.CustomerNameAr ?? string.Empty;
                sheet.Cell(row, 4).Value = GetStatusName(quotation.QuotationStatus);
                sheet.Cell(row, 5).Value = quotation.ItemCount;
                sheet.Cell(row, 6).Value = quotation.SubTotal;
                sheet.Cell(row, 7).Value = quotation.DiscountAmount;
                sheet.Cell(row, 8).Value = quotation.TaxAmount;
                sheet.Cell(row, 9).Value = quotation.TotalAmount;
                row++;
            }

            sheet.Columns().AdjustToContents();
            sheet.SheetView.FreezeRows(1);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, HeaderTable, moduleName: ModuleCode, description: $"تصدير {quotations.Count} عرض سعر إلى Excel");
            return stream.ToArray();
        }

        public async Task<string> GenerateReportHtmlAsync(int quotationId, int userId)
        {
            var header = await GetQuotationByIdAsync(quotationId)
                ?? throw new InvalidOperationException("عرض السعر غير موجود");
            var details = await GetQuotationDetailsAsync(quotationId);
            var reportTotals = CalculateTotals(details);
            var grossTotal = Math.Round(details.Sum(x => x.Quantity * x.UnitPrice), 2, MidpointRounding.AwayFromZero);
            var html = new StringBuilder();
            html.Append(@"<!doctype html><html lang='ar' dir='rtl'><head><meta charset='utf-8'><title>عرض سعر</title>
<style>
*{box-sizing:border-box}body{font-family:Arial,Tahoma,sans-serif;color:#161616;margin:0;padding:28px;background:#fff}
.sheet{max-width:950px;margin:auto;border:1px solid #ded8c8;border-radius:14px;overflow:hidden}.top{padding:24px 30px;background:#171717;color:#f5e6b8;border-bottom:4px solid #d4af37}
.brand{font-size:12px;letter-spacing:2px;color:#d4af37;font-weight:700}.top h1{font-size:26px;margin:10px 0 4px}.top p{margin:0;color:#d0c8b2;font-size:13px}
.meta{display:grid;grid-template-columns:repeat(3,1fr);gap:12px;padding:20px 30px;background:#fbfaf7}.meta div{padding:10px 12px;border:1px solid #ebe6d9;border-radius:8px}.label{display:block;color:#817b6c;font-size:11px;margin-bottom:5px}.value{font-weight:700;font-size:14px}
section{padding:20px 30px}table{width:100%;border-collapse:collapse;font-size:12px}th{background:#f4f0e4;color:#51472b;text-align:right;padding:10px;border-bottom:1px solid #d8cfae}td{padding:10px;border-bottom:1px solid #eee}tfoot td{font-weight:700}
.totals{width:340px;margin:20px 30px 24px auto}.totals div{display:flex;justify-content:space-between;padding:7px 0;border-bottom:1px solid #eee}.totals .grand{font-size:18px;color:#8a6b12;border-bottom:2px solid #d4af37;margin-top:4px;padding-top:11px}
.notes{margin:0 30px 24px;padding:14px;border:1px solid #ebe6d9;border-radius:8px;white-space:pre-wrap;font-size:12px}.footer{padding:14px 30px;border-top:1px solid #eee;color:#8a8478;font-size:11px}
@media print{body{padding:0}.sheet{border:0;border-radius:0}.top{print-color-adjust:exact;-webkit-print-color-adjust:exact}th{print-color-adjust:exact;-webkit-print-color-adjust:exact}}
</style></head><body><main class='sheet'>");
            html.Append("<header class='top'><div class='brand'>YK COATINGS</div><h1>عرض سعر</h1><p>عرض تجاري غير ملزم حتى اعتماده وفق إجراءات الشركة</p></header>");
            html.Append("<div class='meta'>");
            html.Append($"<div><span class='label'>رقم العرض</span><span class='value'>{Html(header.QuotationNumber)}</span></div>");
            html.Append($"<div><span class='label'>تاريخ العرض</span><span class='value'>{header.QuotationDate:dd/MM/yyyy}</span></div>");
            html.Append($"<div><span class='label'>صالح حتى</span><span class='value'>{(header.ValidUntilDate.HasValue ? header.ValidUntilDate.Value.ToString("dd/MM/yyyy") : "—")}</span></div>");
            html.Append($"<div><span class='label'>العميل</span><span class='value'>{Html(header.CustomerNameAr)}</span></div>");
            html.Append($"<div><span class='label'>الحالة</span><span class='value'>{GetStatusName(header.QuotationStatus)}</span></div>");
            html.Append($"<div><span class='label'>كود العميل</span><span class='value'>{Html(header.CustomerCode)}</span></div>");
            html.Append("</div><section><table><thead><tr><th>م</th><th>كود الصنف</th><th>الصنف</th><th>الوحدة</th><th>الكمية</th><th>سعر الوحدة</th><th>خصم</th><th>ضريبة</th><th>الإجمالي</th></tr></thead><tbody>");
            foreach (var line in details)
            {
                html.Append($"<tr><td>{line.LineNumber}</td><td>{Html(line.ItemCode)}</td><td>{Html(line.ItemNameAr)}</td><td>{Html(line.UnitName)}</td><td>{line.Quantity:N2}</td><td>{line.UnitPrice:N2}</td><td>{line.DiscountAmount:N2}</td><td>{line.TaxAmount:N2}</td><td>{line.LineTotalWithTax:N2}</td></tr>");
            }
            html.Append("</tbody></table></section><div class='totals'>");
            html.Append($"<div><span>الإجمالي قبل الخصم والضريبة</span><strong>{grossTotal:N2}</strong></div>");
            html.Append($"<div><span>الخصم</span><strong>{reportTotals.DiscountAmount:N2}</strong></div>");
            html.Append($"<div><span>الإجمالي قبل الضريبة</span><strong>{reportTotals.SubTotal:N2}</strong></div>");
            html.Append($"<div><span>الضريبة</span><strong>{reportTotals.TaxAmount:N2}</strong></div>");
            html.Append($"<div class='grand'><span>الإجمالي شامل الضريبة</span><strong>{reportTotals.TotalAmount:N2} ج.م</strong></div></div>");
            if (!string.IsNullOrWhiteSpace(header.Notes)) html.Append($"<div class='notes'><strong>ملاحظات</strong><br>{Html(header.Notes)}</div>");
            html.Append("<div class='footer'>تم إنشاء هذا المستند من نظام واي كي كوتينج.</div></main></body></html>");

            await _audit.WriteAuditLogAsync(userId, 5, HeaderTable, quotationId.ToString(), moduleName: ModuleCode, description: $"طباعة عرض سعر: {header.QuotationNumber}");
            return html.ToString();
        }

        public static string GetStatusName(int status) => status switch
        {
            1 => "مسودة",
            2 => "بانتظار الاعتماد",
            3 => "معتمد",
            4 => "مرفوض",
            5 => "مُرسل للعميل",
            _ => "غير محدد"
        };

        public static string GetStatusColor(int status) => status switch
        {
            1 => "#64748b",
            2 => "#d97706",
            3 => "#059669",
            4 => "#dc2626",
            5 => "#2563eb",
            _ => "#64748b"
        };

        private async Task<QuotationWorkflowInfo> ChangeWorkflowStatusAsync(
            int quotationId,
            int userId,
            int expectedStatus,
            int newStatus,
            WorkflowAction action,
            string? rejectionReason)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            QuotationWorkflowInfo info;

            try
            {
                var headerSchema = await LoadTableSchemaAsync(connection, transaction, HeaderTable);
                EnsureColumns(headerSchema, HeaderTable, "QuotationID", "QuotationNumber", "QuotationStatus");
                if (action == WorkflowAction.Reject && !headerSchema.HasAny("RejectionReason", "RejectReason"))
                    throw new InvalidOperationException("لا يحتوي مخطط عروض الأسعار على حقل لحفظ سبب الرفض؛ لم يتم تغيير الحالة");
                info = await LoadWorkflowInfoAsync(connection, transaction, headerSchema, quotationId)
                    ?? throw new InvalidOperationException("عرض السعر غير موجود");
                if (info.QuotationStatus != expectedStatus)
                    throw new InvalidOperationException("تغيرت حالة العرض؛ حدّث الصفحة ثم أعد المحاولة");

                await UpdateWorkflowFieldsAsync(connection, transaction, headerSchema, quotationId, userId, newStatus, action, rejectionReason);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            return info;
        }

        private async Task<QuotationWorkflowInfo?> LoadWorkflowInfoAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            TableSchema schema,
            int quotationId)
        {
            var createdBy = Projection(schema, "sq", "CAST(NULL AS int)", "CreatedBy");
            var sql = $@"
SELECT sq.[QuotationNumber], sq.[QuotationStatus], {createdBy} AS CreatedBy,
       (SELECT COUNT(1) FROM dbo.{DetailTable} qd WHERE qd.[QuotationID]=sq.[QuotationID]) AS DetailCount
FROM dbo.{HeaderTable} sq WITH (UPDLOCK, HOLDLOCK)
WHERE sq.[QuotationID]=@ID";
            return await connection.QueryFirstOrDefaultAsync<QuotationWorkflowInfo>(sql, new { ID = quotationId }, transaction);
        }

        private static async Task UpdateWorkflowFieldsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            TableSchema schema,
            int quotationId,
            int userId,
            int newStatus,
            WorkflowAction action,
            string? rejectionReason)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            AddColumn(values, schema, "QuotationStatus", newStatus);
            AddColumn(values, schema, "ModifiedBy", userId);
            AddColumn(values, schema, "ModifiedDate", DateTime.Now);

            switch (action)
            {
                case WorkflowAction.Submit:
                    AddColumn(values, schema, "SubmittedBy", userId);
                    AddColumn(values, schema, "SubmittedDate", DateTime.Now);
                    break;
                case WorkflowAction.Approve:
                    AddColumn(values, schema, "ApprovedBy", userId);
                    AddColumn(values, schema, "ApprovedDate", DateTime.Now);
                    break;
                case WorkflowAction.Reject:
                    AddColumn(values, schema, "RejectedBy", userId);
                    AddColumn(values, schema, "RejectedDate", DateTime.Now);
                    AddFirstAvailableColumn(values, schema, rejectionReason, "RejectionReason", "RejectReason");
                    break;
                case WorkflowAction.MarkSent:
                    AddColumn(values, schema, "SentBy", userId);
                    AddColumn(values, schema, "SentDate", DateTime.Now);
                    break;
            }

            var updateSql = BuildUpdateSql(HeaderTable, schema, values, "QuotationID");
            var affected = await connection.ExecuteAsync(updateSql, ToParameters(values, quotationId), transaction);
            if (affected == 0) throw new InvalidOperationException("تعذر تحديث حالة عرض السعر");
        }

        private static Dictionary<string, object?> BuildHeaderValues(
            TableSchema schema,
            SalesQuotationHeaderDto header,
            QuotationTotals totals,
            int userId,
            bool isNew)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            AddColumn(values, schema, "QuotationNumber", header.QuotationNumber);
            AddColumn(values, schema, "QuotationDate", header.QuotationDate.Date);
            AddColumn(values, schema, "CustomerID", header.CustomerID);
            AddColumn(values, schema, "QuotationStatus", header.QuotationStatus);
            AddColumn(values, schema, "TotalAmount", totals.TotalAmount);
            AddFirstAvailableColumn(values, schema, header.ValidUntilDate?.Date, "ValidUntilDate", "ValidUntil", "ExpiryDate", "ExpirationDate");
            AddFirstAvailableColumn(values, schema, totals.SubTotal, "SubTotal", "Subtotal");
            AddColumn(values, schema, "DiscountPercent", header.DiscountPercent);
            AddColumn(values, schema, "DiscountAmount", totals.DiscountAmount);
            AddColumn(values, schema, "TaxAmount", totals.TaxAmount);
            AddColumn(values, schema, "ShippingCost", header.ShippingCost);
            AddColumn(values, schema, "SalesRepID", header.SalesRepID);
            AddFirstAvailableColumn(values, schema, header.Notes ?? string.Empty, "Notes", "Remarks");
            AddColumn(values, schema, "ModifiedBy", userId);
            AddColumn(values, schema, "ModifiedDate", DateTime.Now);

            if (isNew)
            {
                AddColumn(values, schema, "CreatedBy", userId);
                AddColumn(values, schema, "CreatedDate", DateTime.Now);
            }

            return values;
        }

        private static Dictionary<string, object?> BuildDetailValues(
            TableSchema schema,
            SalesQuotationDetailDto detail,
            int quotationId,
            int lineNumber)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            AddColumn(values, schema, "QuotationID", quotationId);
            AddColumn(values, schema, "LineNumber", lineNumber);
            AddColumn(values, schema, "ItemID", detail.ItemID);
            AddColumn(values, schema, "UnitID", detail.UnitID);
            AddColumn(values, schema, "Quantity", detail.Quantity);
            AddColumn(values, schema, "UnitPrice", detail.UnitPrice);
            AddColumn(values, schema, "DiscountPercent", detail.DiscountPercent);
            AddColumn(values, schema, "DiscountAmount", detail.DiscountAmount);
            AddColumn(values, schema, "TaxRate", detail.TaxRate);
            AddColumn(values, schema, "TaxAmount", detail.TaxAmount);
            AddColumn(values, schema, "LineTotal", detail.LineTotal);
            AddFirstAvailableColumn(values, schema, detail.LineTotalWithTax, "LineTotalWithTax", "TotalWithTax");
            AddFirstAvailableColumn(values, schema, detail.Notes ?? string.Empty, "Notes", "Remarks");
            AddColumn(values, schema, "LineStatus", 1);
            return values;
        }

        private static void ValidateOptionalFields(
            SalesQuotationHeaderDto header,
            IReadOnlyList<SalesQuotationDetailDto> details,
            TableSchema headerSchema,
            TableSchema detailSchema)
        {
            if (header.ValidUntilDate.HasValue && !headerSchema.HasAny("ValidUntilDate", "ValidUntil", "ExpiryDate", "ExpirationDate"))
                throw new InvalidOperationException("مخطط عروض الأسعار لا يحتوي على حقل لتاريخ الصلاحية؛ لم يتم حفظ هذا التاريخ");
            if (header.SalesRepID.HasValue && !headerSchema.HasAny("SalesRepID"))
                throw new InvalidOperationException("مخطط عروض الأسعار لا يحتوي على حقل لمندوب المبيعات");
            if (!string.IsNullOrWhiteSpace(header.Notes) && !headerSchema.HasAny("Notes", "Remarks"))
                throw new InvalidOperationException("مخطط عروض الأسعار لا يحتوي على حقل للملاحظات؛ أزل الملاحظة أو حدّث المخطط");
            if (details.Any(x => x.DiscountPercent != 0) && !detailSchema.HasAny("DiscountPercent"))
                throw new InvalidOperationException("مخطط تفاصيل العروض لا يحتوي على حقل لنسبة الخصم؛ لا يمكن حفظ الخصم بأمان");
            if (details.Any(x => x.TaxRate != 14m) && !detailSchema.HasAny("TaxRate"))
                throw new InvalidOperationException("مخطط تفاصيل العروض لا يحتوي على حقل لمعدل الضريبة؛ لا يمكن حفظ المعدل المختلف بأمان");
            if (details.Any(x => !string.IsNullOrWhiteSpace(x.Notes)) && !detailSchema.HasAny("Notes", "Remarks"))
                throw new InvalidOperationException("مخطط تفاصيل العروض لا يحتوي على حقل لملاحظات السطر");
        }

        private static void ValidateDetails(IReadOnlyList<SalesQuotationDetailDto> details)
        {
            for (var index = 0; index < details.Count; index++)
            {
                var line = details[index];
                if (line.ItemID <= 0) throw new InvalidOperationException($"اختر الصنف في السطر {index + 1}");
                if (line.UnitID <= 0) throw new InvalidOperationException($"اختر الوحدة في السطر {index + 1}");
                if (line.Quantity <= 0) throw new InvalidOperationException($"الكمية يجب أن تكون أكبر من صفر في السطر {index + 1}");
                if (line.UnitPrice < 0) throw new InvalidOperationException($"السعر لا يمكن أن يكون سالباً في السطر {index + 1}");
                if (line.DiscountPercent is < 0 or > 100) throw new InvalidOperationException($"نسبة الخصم يجب أن تكون بين 0 و100 في السطر {index + 1}");
                if (line.TaxRate is < 0 or > 100) throw new InvalidOperationException($"معدل الضريبة يجب أن يكون بين 0 و100 في السطر {index + 1}");
            }
        }

        private static QuotationTotals CalculateTotals(IReadOnlyList<SalesQuotationDetailDto> details)
        {
            static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            return new QuotationTotals(
                Round(details.Sum(x => x.LineTotal)),
                Round(details.Sum(x => x.DiscountAmount)),
                Round(details.Sum(x => x.TaxAmount)),
                Round(details.Sum(x => x.LineTotalWithTax)));
        }

        private static async Task<TableSchema> LoadTableSchemaAsync(
            SqlConnection connection,
            SqlTransaction? transaction,
            string tableName)
        {
            var rows = await connection.QueryAsync<SchemaColumn>(@"
SELECT
    c.name AS Name,
    c.is_nullable AS IsNullable,
    c.is_identity AS IsIdentity,
    c.is_computed AS IsComputed,
    c.system_type_id AS SystemTypeId,
    CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit) AS HasDefault
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(@ObjectName, N'U')",
                new { ObjectName = $"dbo.{tableName}" }, transaction);
            var schema = new TableSchema(rows);
            if (schema.Columns.Count == 0)
                throw new InvalidOperationException($"جدول dbo.{tableName} غير موجود أو لا يمكن قراءة مخططه");
            return schema;
        }

        private static void EnsureColumns(TableSchema schema, string tableName, params string[] required)
        {
            var missing = required.Where(name => !schema.Contains(name)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"مخطط dbo.{tableName} لا يحتوي الأعمدة المطلوبة: {string.Join(", ", missing)}");
        }

        private static void EnsureRequiredValues(TableSchema schema, string tableName, IReadOnlyDictionary<string, object?> values)
        {
            var missing = schema.Columns.Values
                .Where(column => !column.IsNullable && !column.HasDefault && !column.IsIdentity && !column.IsComputed && column.SystemTypeId != 189)
                .Where(column => !values.TryGetValue(column.Name, out var value) || value is null)
                .Select(column => column.Name)
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"تعذر الحفظ: dbo.{tableName} يتطلب أعمدة غير معروفة لهذه الشاشة: {string.Join(", ", missing)}");
        }

        private static void AddColumn(IDictionary<string, object?> values, TableSchema schema, string name, object? value)
        {
            var actual = schema.FindWritable(name);
            if (actual is not null) values[actual] = value;
        }

        private static void AddFirstAvailableColumn(IDictionary<string, object?> values, TableSchema schema, object? value, params string[] names)
        {
            var actual = schema.FindFirstWritable(names);
            if (actual is not null) values[actual] = value;
        }

        private static string Projection(TableSchema schema, string alias, string fallback, params string[] names)
        {
            var column = schema.Find(names);
            return column is null ? fallback : $"{alias}.{QuoteIdentifier(column)}";
        }

        private static string NumberProjection(TableSchema schema, string alias, string fallback, params string[] names)
        {
            var column = schema.Find(names);
            return column is null ? fallback : $"ISNULL({alias}.{QuoteIdentifier(column)}, {fallback})";
        }

        private static string BuildInsertSql(string tableName, TableSchema schema, IReadOnlyDictionary<string, object?> values, string? outputColumn)
        {
            var columns = values.Keys.Where(schema.IsWritable).ToArray();
            if (columns.Length == 0) throw new InvalidOperationException($"لا توجد أعمدة قابلة للإدخال في dbo.{tableName}");
            var columnSql = string.Join(", ", columns.Select(QuoteIdentifier));
            var valueSql = string.Join(", ", columns.Select(name => "@" + name));
            var outputSql = outputColumn is null ? string.Empty : $" OUTPUT INSERTED.{QuoteIdentifier(outputColumn)}";
            return $"INSERT INTO dbo.{tableName} ({columnSql}){outputSql} VALUES ({valueSql})";
        }

        private static string BuildUpdateSql(string tableName, TableSchema schema, IReadOnlyDictionary<string, object?> values, string keyColumn)
        {
            var columns = values.Keys.Where(name => schema.IsWritable(name) && !string.Equals(name, keyColumn, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (columns.Length == 0) throw new InvalidOperationException($"لا توجد أعمدة قابلة للتعديل في dbo.{tableName}");
            var setSql = string.Join(", ", columns.Select(name => $"{QuoteIdentifier(name)} = @{name}"));
            return $"UPDATE dbo.{tableName} SET {setSql} WHERE {QuoteIdentifier(keyColumn)} = @RecordID";
        }

        private static DynamicParameters ToParameters(IReadOnlyDictionary<string, object?> values, int? recordId = null)
        {
            var parameters = new DynamicParameters();
            foreach (var (name, value) in values) parameters.Add(name, value);
            if (recordId.HasValue) parameters.Add("RecordID", recordId.Value);
            return parameters;
        }

        private static string QuoteIdentifier(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
        private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private enum WorkflowAction
        {
            Submit,
            Approve,
            Reject,
            MarkSent
        }

        private sealed record QuotationTotals(decimal SubTotal, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount);

        private sealed class QuotationWorkflowInfo
        {
            public QuotationWorkflowInfo() { }
            public string? QuotationNumber { get; set; }
            public int QuotationStatus { get; set; }
            public int? CreatedBy { get; set; }
            public int DetailCount { get; set; }
        }

        private sealed class SchemaColumn
        {
            public SchemaColumn() { }
            public string Name { get; set; } = string.Empty;
            public bool IsNullable { get; set; }
            public bool IsIdentity { get; set; }
            public bool IsComputed { get; set; }
            public int SystemTypeId { get; set; }
            public bool HasDefault { get; set; }
        }

        private sealed class TableSchema
        {
            public Dictionary<string, SchemaColumn> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

            public TableSchema(IEnumerable<SchemaColumn> columns)
            {
                foreach (var column in columns) Columns[column.Name] = column;
            }

            public bool Contains(string name) => Columns.ContainsKey(name);
            public bool HasAny(params string[] names) => names.Any(Contains);
            public bool IsWritable(string name) => FindWritable(name) is not null;

            public string? Find(params string[] names)
            {
                foreach (var name in names)
                    if (Columns.TryGetValue(name, out var column)) return column.Name;
                return null;
            }

            public string? FindWritable(string name)
            {
                if (!Columns.TryGetValue(name, out var column)) return null;
                return column.IsIdentity || column.IsComputed || column.SystemTypeId == 189 ? null : column.Name;
            }

            public string? FindFirstWritable(params string[] names)
            {
                foreach (var name in names)
                {
                    var writable = FindWritable(name);
                    if (writable is not null) return writable;
                }
                return null;
            }
        }
    }
}
