using System.Data;
using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;

namespace YKCoatings.Services
{
    public partial class SalesInvoiceService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;
        private readonly PermissionService _permissionService;
        private readonly UserSessionService _userSession;

        public SalesInvoiceService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif,
            PermissionService permissionService,
            UserSessionService userSession) : base(configuration)
        {
            _audit = audit;
            _notif = notif;
            _permissionService = permissionService;
            _userSession = userSession;
        }

        // ==========================================
        // قائمة الفواتير
        // ==========================================
        public async Task<List<SalesInvoiceListDto>> GetInvoicesListAsync(int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanView,
                "لا تملك صلاحية عرض فواتير المبيعات.");

            using var connection = CreateConnection();
            var sql = @"SELECT
                            si.InvoiceID, si.InvoiceNumber, si.InvoiceDate,
                            si.InvoiceStatus, ISNULL(si.InvoiceSource,3) AS InvoiceSource,
                            CASE ISNULL(si.InvoiceSource,3)
                                WHEN 1 THEN N'من إذن تسليم'
                                WHEN 2 THEN N'من أمر بيع'
                                ELSE N'فاتورة مباشرة' END AS InvoiceSourceName,
                            ISNULL(si.PaymentStatus,1) AS PaymentStatus,
                            CASE ISNULL(si.PaymentStatus,1)
                                WHEN 1 THEN N'غير مدفوعة'
                                WHEN 2 THEN N'مدفوعة جزئياً'
                                WHEN 3 THEN N'مدفوعة بالكامل'
                                ELSE N'غير محدد' END AS PaymentStatusName,
                            si.CustomerID, c.CustomerCode, c.CustomerNameAr,
                            si.SalesOrderID, so.SONumber AS LinkedSONumber,
                            si.DeliveryID, CAST(NULL AS NVARCHAR(50)) AS LinkedDeliveryNumber,
                            ISNULL(si.IsTaxable,1) AS IsTaxable, ISNULL(si.TaxRate,14) AS TaxRate,
                            ISNULL(si.SubTotal,0) AS SubTotal, ISNULL(si.DiscountAmount,0) AS DiscountAmount,
                            ISNULL(si.TaxAmount,0) AS TaxAmount, ISNULL(si.TotalAmount,0) AS TotalAmount,
                            ISNULL(si.PaidAmount,0) AS PaidAmount,
                            ISNULL(si.TotalAmount,0)-ISNULL(si.PaidAmount,0) AS RemainingAmount,
                            si.DueDate,
                            CAST(CASE WHEN si.InvoiceStatus NOT IN (1,2,6)
                                      AND ISNULL(si.PaymentStatus,1) <> 3
                                      AND si.DueDate < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END AS BIT) AS IsOverdue,
                            CASE WHEN si.InvoiceStatus NOT IN (1,2,6)
                                      AND ISNULL(si.PaymentStatus,1) <> 3
                                      AND si.DueDate < CAST(GETDATE() AS DATE)
                                 THEN DATEDIFF(DAY, si.DueDate, CAST(GETDATE() AS DATE)) ELSE 0 END AS DaysOverdue,
                            pt.TermNameAr AS PaymentTermName, cur.CurrencyCode,
                            (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.InvoiceID=si.InvoiceID) AS ItemCount,
                            CAST(NULL AS NVARCHAR(500)) AS RejectionReason, si.CreatedBy, si.CreatedDate
                        FROM dbo.SalesInvoices si
                        INNER JOIN dbo.Customers c ON si.CustomerID=c.CustomerID
                        LEFT JOIN dbo.SalesOrders so ON si.SalesOrderID=so.SalesOrderID
                        LEFT JOIN dbo.PaymentTerms pt ON si.PaymentTermID=pt.PaymentTermID
                        LEFT JOIN dbo.Currencies cur ON si.CurrencyID=cur.CurrencyID
                        ORDER BY si.InvoiceDate DESC, si.InvoiceID DESC";

            return (await connection.QueryAsync<SalesInvoiceListDto>(sql)).ToList();
        }

        // ==========================================
        // جلب فاتورة واحدة
        // ==========================================
        public async Task<SalesInvoiceHeaderDto?> GetInvoiceByIdAsync(int invoiceId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanView,
                "لا تملك صلاحية عرض فواتير المبيعات.");
            return await GetInvoiceByIdCoreAsync(invoiceId);
        }

        private async Task<SalesInvoiceHeaderDto?> GetInvoiceByIdCoreAsync(int invoiceId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            si.InvoiceID, si.InvoiceNumber, si.InvoiceDate,
                            si.InvoiceStatus, ISNULL(si.InvoiceSource,3) AS InvoiceSource,
                            ISNULL(si.PaymentStatus,1) AS PaymentStatus,
                            si.CustomerID, c.CustomerNameAr, c.CustomerCode,
                            si.SalesOrderID, so.SONumber AS LinkedSONumber,
                            si.DeliveryID, CAST(NULL AS NVARCHAR(50)) AS LinkedDeliveryNumber,
                            ISNULL(si.IsTaxable,1) AS IsTaxable, ISNULL(si.TaxRate,14) AS TaxRate,
                            si.PaymentTermID, pt.TermNameAr AS PaymentTermName,
                            si.DueDate,
                            si.CurrencyID, cur.CurrencyCode, cur.Symbol AS CurrencySymbol,
                            ISNULL(si.ExchangeRate,1) AS ExchangeRate,
                            si.WarehouseID, w.WarehouseNameAr,
                            ISNULL(si.SubTotal,0) AS SubTotal, ISNULL(si.DiscountPercent,0) AS DiscountPercent,
                            ISNULL(si.DiscountAmount,0) AS DiscountAmount,
                            ISNULL(si.TaxableAmount,0) AS TaxableAmount, ISNULL(si.TaxAmount,0) AS TaxAmount,
                            ISNULL(si.ShippingCost,0) AS ShippingCost, ISNULL(si.OtherCosts,0) AS OtherCosts,
                            ISNULL(si.TotalAmount,0) AS TotalAmount, ISNULL(si.PaidAmount,0) AS PaidAmount,
                            si.ApprovedBy, si.ApprovedDate,
                            app.FullNameAr AS ApprovedByName,
                            si.Notes, si.CreatedBy, si.CreatedDate, si.ModifiedDate
                        FROM dbo.SalesInvoices si
                        INNER JOIN dbo.Customers c ON si.CustomerID = c.CustomerID
                        LEFT JOIN dbo.SalesOrders so ON si.SalesOrderID = so.SalesOrderID
                        LEFT JOIN dbo.PaymentTerms pt ON si.PaymentTermID = pt.PaymentTermID
                        LEFT JOIN dbo.Currencies cur ON si.CurrencyID = cur.CurrencyID
                        LEFT JOIN dbo.Warehouses w ON si.WarehouseID = w.WarehouseID
                        LEFT JOIN dbo.Employees app ON si.ApprovedBy = app.EmployeeID
                        WHERE si.InvoiceID = @ID";

            var header = await connection.QueryFirstOrDefaultAsync<SalesInvoiceHeaderDto>(sql, new { ID = invoiceId });
            if (header != null)
            {
                var (notes, rejectionReason) = SplitRejectionNote(header.Notes);
                header.Notes = notes;
                header.RejectionReason = rejectionReason;
            }
            return header;
        }

        // ==========================================
        // تفاصيل الفاتورة
        // ==========================================
        public async Task<List<SalesInvoiceDetailDto>> GetInvoiceDetailsAsync(int invoiceId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanView,
                "لا تملك صلاحية عرض تفاصيل فواتير المبيعات.");
            return await GetInvoiceDetailsCoreAsync(invoiceId);
        }

        private async Task<List<SalesInvoiceDetailDto>> GetInvoiceDetailsCoreAsync(int invoiceId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            sid.InvoiceDetailID, sid.InvoiceID, sid.LineNumber,
                            sid.ItemID, i.ItemCode, i.ItemNameAr,
                            sid.UnitID, u.UnitNameAr AS UnitName,
                            sid.WarehouseID, w.WarehouseNameAr,
                            sid.Quantity, sid.UnitPrice,
                            ISNULL(sid.DiscountPercent,0) AS DiscountPercent,
                            ISNULL(sid.DiscountAmount,0) AS DiscountAmount,
                            ISNULL(sid.LineTotal,0) AS LineTotal,
                            ISNULL(sid.TaxRate,0) AS TaxRate, ISNULL(sid.TaxAmount,0) AS TaxAmount,
                            ISNULL(sid.LineTotalWithTax,sid.LineTotal) AS LineTotalWithTax,
                            sid.BatchNumber, sid.ExpiryDate,
                            CAST(NULL AS INT) AS DeliveryDetailID,
                            sid.SODetailID AS SalesOrderDetailID,
                            sid.Notes,
                            ISNULL((SELECT ib.AvailableQty
                                    FROM dbo.InventoryBalance ib
                                    WHERE ib.ItemID=sid.ItemID
                                      AND ib.WarehouseID=COALESCE(sid.WarehouseID,si.WarehouseID)),0) AS AvailableQty
                        FROM dbo.SalesInvoiceDetails sid
                        INNER JOIN dbo.SalesInvoices si ON si.InvoiceID=sid.InvoiceID
                        INNER JOIN dbo.Items i ON sid.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON sid.UnitID = u.UnitID
                        LEFT JOIN dbo.Warehouses w ON w.WarehouseID=COALESCE(sid.WarehouseID,si.WarehouseID)
                        WHERE sid.InvoiceID = @ID
                        ORDER BY sid.LineNumber, sid.InvoiceDetailID";
            var result = await connection.QueryAsync<SalesInvoiceDetailDto>(sql, new { ID = invoiceId });
            return result.ToList();
        }

        // ==========================================
        // توليد رقم الفاتورة
        // ==========================================
        public async Task<string> GenerateInvoiceNumberAsync(int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanAdd,
                "لا تملك صلاحية إنشاء فاتورة مبيعات.");

            using var connection = CreateConnection();
            var sql = @"DECLARE @NextNum NVARCHAR(50);
                        EXEC dbo.sp_GetNextNumber N'SI', @NextNum OUTPUT;
                        SELECT @NextNum;";
            var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("تعذر توليد رقم فاتورة مبيعات فريد.");
            return result;
        }

        // ==========================================
        // أوامر البيع المعتمدة المتاحة لربط فاتورة جديدة
        // ==========================================
        public async Task<List<SalesOrderHeaderDto>> GetApprovedSalesOrdersForInvoiceAsync(int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanAdd,
                "لا تملك صلاحية إنشاء فاتورة مبيعات.");

            using var connection = CreateConnection();
            const string sql = @"
SELECT
    so.SalesOrderID,
    so.SONumber AS OrderNumber,
    so.SODate AS OrderDate,
    so.SOStatus AS OrderStatus,
    CASE WHEN so.QuotationID IS NOT NULL THEN 2 ELSE 1 END AS OrderSource,
    so.CustomerID,
    c.CustomerNameAr,
    c.CustomerCode,
    so.PaymentTermID,
    so.CurrencyID,
    ISNULL(so.ExchangeRate,1) AS ExchangeRate,
    so.WarehouseID
FROM dbo.SalesOrders so
INNER JOIN dbo.Customers c ON c.CustomerID=so.CustomerID
WHERE so.SOStatus=3
ORDER BY so.SODate DESC, so.SalesOrderID DESC;";

            var orders = await connection.QueryAsync<SalesOrderHeaderDto>(sql);
            return orders.ToList();
        }

        // ==========================================
        // سطور أمر البيع المعتمد المتاحة للتحميل إلى الفاتورة
        // ==========================================
        public async Task<List<SalesOrderLineForInvoiceDto>> GetSalesOrderLinesForInvoiceAsync(int salesOrderId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanView && (p.CanAdd || p.CanEdit),
                "لا تملك صلاحية تحميل سطور أمر البيع إلى فاتورة المبيعات.");
            if (salesOrderId <= 0) throw new ArgumentOutOfRangeException(nameof(salesOrderId));

            using var connection = CreateConnection();
            const string sql = @"
SELECT
    sod.SODetailID AS SalesOrderDetailID,
    sod.SalesOrderID,
    so.SONumber,
    sod.LineNumber,
    sod.ItemID,
    i.ItemCode,
    i.ItemNameAr,
    sod.UnitID,
    u.UnitNameAr AS UnitName,
    so.WarehouseID,
    sod.OrderedQty,
    ISNULL(sod.DeliveredQty,0) AS DeliveredQty,
    ISNULL(sod.RemainingQty, sod.OrderedQty-ISNULL(sod.DeliveredQty,0)) AS RemainingQty,
    ISNULL(sod.UnitPrice,0) AS UnitPrice,
    ISNULL(sod.DiscountPercent,0) AS DiscountPercent
FROM dbo.SalesOrders so
INNER JOIN dbo.SalesOrderDetails sod ON sod.SalesOrderID=so.SalesOrderID
INNER JOIN dbo.Items i ON i.ItemID=sod.ItemID
INNER JOIN dbo.Units u ON u.UnitID=sod.UnitID
WHERE so.SalesOrderID=@SalesOrderID AND so.SOStatus=3
ORDER BY sod.LineNumber, sod.SODetailID;";

            var lines = (await connection.QueryAsync<SalesOrderLineForInvoiceDto>(sql,
                new { SalesOrderID = salesOrderId })).ToList();
            foreach (var line in lines)
            {
                // لا نفترض أن كامل المتبقي أو كامل المطلوب هو كمية الفوترة.
                line.IsSelected = false;
                line.InvoiceQty = 0;
                line.InvoiceUnitPrice = line.UnitPrice;
            }
            return lines;
        }

        // ==========================================
        // إضافة فاتورة جديدة — أمر البيع يحفظ الرأس والسطور في معاملة واحدة
        // ==========================================
        public Task<int> InsertInvoiceAsync(SalesInvoiceHeaderDto header, int userId)
            => InsertInvoiceCoreAsync(header, null, userId);

        public Task<int> InsertInvoiceFromSalesOrderAsync(
            SalesInvoiceHeaderDto header,
            IReadOnlyCollection<SalesOrderLineForInvoiceDto> orderLines,
            int userId)
            => InsertInvoiceCoreAsync(header, orderLines, userId);

        private async Task<int> InsertInvoiceCoreAsync(
            SalesInvoiceHeaderDto header,
            IReadOnlyCollection<SalesOrderLineForInvoiceDto>? orderLines,
            int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanAdd,
                "لا تملك صلاحية إنشاء فاتورة مبيعات.");
            if (header.CustomerID <= 0) throw new ArgumentException("اختر العميل.", nameof(header));
            if (string.IsNullOrWhiteSpace(header.InvoiceNumber)) throw new ArgumentException("رقم الفاتورة مطلوب.", nameof(header));
            if (header.DiscountPercent < 0 || header.DiscountPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(header), "نسبة الخصم يجب أن تكون بين صفر و100.");
            if (header.TaxRate < 0 || header.TaxRate > 100)
                throw new ArgumentOutOfRangeException(nameof(header), "نسبة الضريبة يجب أن تكون بين صفر و100.");
            if (header.InvoiceSource == 1 || header.DeliveryID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("إنشاء فاتورة من إذن تسليم غير مدعوم بالمخطط ومسار الترحيل الحالي؛ استخدم الفاتورة المباشرة.");

            var invoiceSource = header.SalesOrderID.GetValueOrDefault() > 0 ? 2 : header.InvoiceSource;
            if (invoiceSource == 0) invoiceSource = 3;
            if (invoiceSource is not (2 or 3))
                throw new InvalidOperationException("مصدر الفاتورة غير مدعوم؛ اختر فاتورة مباشرة أو أمراً معتمداً.");
            if (invoiceSource == 2 && header.SalesOrderID.GetValueOrDefault() <= 0)
                throw new InvalidOperationException("الفاتورة من أمر بيع تتطلب اختيار أمر بيع معتمداً؛ استخدم الفاتورة المباشرة إذا لم يوجد أمر.");

            var selectedLines = orderLines?.Where(line => line.IsSelected).ToList();
            if (invoiceSource == 2)
            {
                if (orderLines is null)
                    throw new InvalidOperationException("حمّل سطور أمر البيع وحدد الأصناف وكميات الفوترة قبل إنشاء الفاتورة.");
                if (selectedLines is null || selectedLines.Count == 0)
                    throw new InvalidOperationException("حدد سطراً واحداً على الأقل من أمر البيع.");
                if (selectedLines.Any(line => line.SalesOrderDetailID <= 0 || line.InvoiceQty <= 0))
                    throw new InvalidOperationException("أدخل كمية فوترة أكبر من صفر لكل سطر محدد.");
                if (selectedLines.Select(line => line.SalesOrderDetailID).Distinct().Count() != selectedLines.Count)
                    throw new InvalidOperationException("لا يمكن اختيار سطر أمر البيع نفسه أكثر من مرة في عملية الإنشاء.");
            }
            else if (selectedLines?.Count > 0)
            {
                throw new InvalidOperationException("لا يمكن ربط سطور أمر بيع بفاتورة مباشرة.");
            }

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            var insertedDetailIds = new List<int>();
            int newId;
            try
            {
                List<SalesOrderLineForInvoiceDto> storedOrderLines = new();
                if (invoiceSource == 2)
                {
                    var orderId = header.SalesOrderID!.Value;
                    var order = await connection.QueryFirstOrDefaultAsync<(int CustomerID, int SOStatus)>(
                        @"SELECT CustomerID, SOStatus
                          FROM dbo.SalesOrders WITH (UPDLOCK, HOLDLOCK)
                          WHERE SalesOrderID=@SalesOrderID",
                        new { SalesOrderID = orderId }, transaction);
                    if (order.CustomerID <= 0)
                        throw new KeyNotFoundException("أمر البيع المرتبط غير موجود.");
                    if (order.SOStatus != 3)
                        throw new InvalidOperationException("لا يمكن إنشاء الفاتورة من أمر بيع غير معتمد.");
                    if (order.CustomerID != header.CustomerID)
                        throw new InvalidOperationException("العميل المحدد لا يطابق عميل أمر البيع.");

                    var requestedOrderLines = selectedLines
                        ?? throw new InvalidOperationException("حدد سطور أمر البيع قبل إنشاء الفاتورة.");
                    var orderDetailIds = requestedOrderLines.Select(line => line.SalesOrderDetailID).ToArray();
                    storedOrderLines = (await connection.QueryAsync<SalesOrderLineForInvoiceDto>(
                        @"SELECT
                              sod.SODetailID AS SalesOrderDetailID,
                              sod.SalesOrderID,
                              sod.LineNumber,
                              sod.ItemID,
                              i.ItemCode,
                              i.ItemNameAr,
                              sod.UnitID,
                              u.UnitNameAr AS UnitName,
                              so.WarehouseID,
                              sod.OrderedQty,
                              ISNULL(sod.DeliveredQty,0) AS DeliveredQty,
                              ISNULL(sod.RemainingQty, sod.OrderedQty-ISNULL(sod.DeliveredQty,0)) AS RemainingQty,
                              ISNULL(sod.UnitPrice,0) AS UnitPrice,
                              ISNULL(sod.DiscountPercent,0) AS DiscountPercent
                          FROM dbo.SalesOrderDetails sod WITH (UPDLOCK, HOLDLOCK)
                          INNER JOIN dbo.SalesOrders so WITH (UPDLOCK, HOLDLOCK) ON so.SalesOrderID=sod.SalesOrderID
                          INNER JOIN dbo.Items i ON i.ItemID=sod.ItemID
                          INNER JOIN dbo.Units u ON u.UnitID=sod.UnitID
                          WHERE sod.SalesOrderID=@SalesOrderID AND so.SOStatus=3
                            AND sod.SODetailID IN @OrderDetailIDs
                          ORDER BY sod.LineNumber, sod.SODetailID",
                        new { SalesOrderID = orderId, OrderDetailIDs = orderDetailIds }, transaction)).ToList();

                    if (storedOrderLines.Count != requestedOrderLines.Count)
                        throw new InvalidOperationException("تغيرت سطور أمر البيع أو لم تعد تتبع الأمر المحدد؛ أعد تحميل الأمر.");
                }

                var sql = @"INSERT INTO dbo.SalesInvoices
                            (InvoiceNumber, InvoiceDate, CustomerID, SalesOrderID, DeliveryID,
                             IsTaxable, TaxRate, PaymentTermID, CurrencyID, ExchangeRate, WarehouseID,
                             DiscountPercent, ShippingCost, OtherCosts, InvoiceStatus, InvoiceSource,
                             PaymentStatus, Notes, CreatedBy, CreatedDate)
                            VALUES
                            (@InvoiceNumber, @InvoiceDate, @CustomerID, NULLIF(@SalesOrderID,0), NULL,
                             @IsTaxable, @TaxRate, NULLIF(@PaymentTermID,0), NULLIF(@CurrencyID,0),
                             @ExchangeRate, NULLIF(@WarehouseID,0), @DiscountPercent, @ShippingCost,
                             @OtherCosts, 1, @InvoiceSource, 1, @Notes, @UserID, GETDATE());
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    header.InvoiceNumber,
                    header.InvoiceDate,
                    header.CustomerID,
                    header.SalesOrderID,
                    header.IsTaxable,
                    TaxRate = header.IsTaxable ? (header.TaxRate <= 0 ? 14 : header.TaxRate) : 0,
                    header.PaymentTermID,
                    header.CurrencyID,
                    ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                    header.WarehouseID,
                    header.DiscountPercent,
                    header.ShippingCost,
                    header.OtherCosts,
                    InvoiceSource = invoiceSource,
                    header.Notes,
                    UserID = userId
                }, transaction);

                if (invoiceSource == 2)
                {
                    var requestedById = selectedLines!.ToDictionary(line => line.SalesOrderDetailID);
                    var taxRate = header.IsTaxable ? (header.TaxRate <= 0 ? 14 : header.TaxRate) : 0;
                    var lineNumber = 0;
                    foreach (var orderLine in storedOrderLines)
                    {
                        var request = requestedById[orderLine.SalesOrderDetailID];
                        if (orderLine.UnitPrice < 0 || orderLine.DiscountPercent < 0 || orderLine.DiscountPercent > 100)
                            throw new InvalidOperationException($"سعر أو خصم سطر أمر البيع {orderLine.SalesOrderDetailID} غير صالح.");

                        await ResolveToPrimaryUnitFactorAsync(connection, transaction, orderLine.ItemID, orderLine.UnitID);
                        var invoiceLine = new SalesInvoiceDetailEditDto
                        {
                            InvoiceID = newId,
                            LineNumber = ++lineNumber,
                            ItemID = orderLine.ItemID,
                            UnitID = orderLine.UnitID,
                            WarehouseID = header.WarehouseID ?? orderLine.WarehouseID,
                            Quantity = request.InvoiceQty,
                            UnitPrice = orderLine.UnitPrice,
                            DiscountPercent = orderLine.DiscountPercent,
                            TaxRate = taxRate,
                            IsTaxable = header.IsTaxable,
                            SalesOrderDetailID = orderLine.SalesOrderDetailID
                        };
                        insertedDetailIds.Add(await InsertInvoiceDetailCoreAsync(
                            connection, transaction, invoiceLine, newId, lineNumber, header.IsTaxable, taxRate));
                    }
                }

                if (insertedDetailIds.Count > 0)
                    await connection.ExecuteAsync("EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID",
                        new { InvoiceID = newId }, transaction);
                await RecalculateSalesInvoiceDueDateAsync(connection, transaction, newId);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 1, "SalesInvoices", newId.ToString(),
                moduleName: "SCR_SINV",
                description: invoiceSource == 2
                    ? $"إنشاء فاتورة مبيعات من أمر بيع: {header.InvoiceNumber}، عدد السطور {insertedDetailIds.Count}"
                    : $"إنشاء فاتورة مبيعات: {header.InvoiceNumber} {(header.IsTaxable ? "مع ضريبة" : "بدون ضريبة")}");
            foreach (var detailId in insertedDetailIds)
            {
                await _audit.WriteAuditLogAsync(userId, 1, "SalesInvoiceDetails", detailId.ToString(),
                    moduleName: "SCR_SINV", description: $"ربط سطر أمر بيع بتفاصيل فاتورة المبيعات رقم {newId}");
            }
            return newId;
        }

        // ==========================================
        // تحديث فاتورة (مسودة فقط)
        // ==========================================
        public async Task UpdateInvoiceAsync(SalesInvoiceHeaderDto header, int userId)
        {
            if (header.InvoiceID <= 0) throw new ArgumentException("رقم الفاتورة غير صالح.", nameof(header));
            if (header.CustomerID <= 0) throw new ArgumentException("اختر العميل.", nameof(header));
            if (header.DiscountPercent < 0 || header.DiscountPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(header), "نسبة الخصم يجب أن تكون بين صفر و100.");
            if (header.TaxRate < 0 || header.TaxRate > 100)
                throw new ArgumentOutOfRangeException(nameof(header), "نسبة الضريبة يجب أن تكون بين صفر و100.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            try
            {
                var permissions = await GetCurrentInvoicePermissionsAsync(userId);
                await EnsureCanEditDraftAsync(connection, transaction, header.InvoiceID, userId, permissions);

                var current = await connection.QueryFirstOrDefaultAsync<(int InvoiceStatus, string? Notes, int? SalesOrderID, int InvoiceSource)>(
                    @"SELECT InvoiceStatus, Notes, SalesOrderID, ISNULL(InvoiceSource,3) AS InvoiceSource
                      FROM dbo.SalesInvoices WITH (UPDLOCK, HOLDLOCK)
                      WHERE InvoiceID=@ID",
                    new { ID = header.InvoiceID }, transaction);
                if (current.InvoiceStatus != 1)
                    throw new InvalidOperationException("لا يمكن تعديل فاتورة ليست في حالة مسودة.");
                if (current.InvoiceSource == 1)
                    throw new InvalidOperationException("فاتورة إذن التسليم غير مدعومة بمسار الترحيل الحالي؛ لا يمكن تعديلها.");
                if (current.InvoiceSource == 2 && current.SalesOrderID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("الفاتورة المرتبطة بأمر بيع لا تحتوي رقم الأمر.");
                if (current.SalesOrderID.GetValueOrDefault() > 0)
                {
                    var orderCustomerId = await connection.QueryFirstOrDefaultAsync<int?>(
                        "SELECT CustomerID FROM dbo.SalesOrders WHERE SalesOrderID=@SalesOrderID",
                        new { current.SalesOrderID }, transaction);
                    var orderCustomerIdValue = orderCustomerId.GetValueOrDefault();
                    if (orderCustomerIdValue <= 0 || orderCustomerIdValue != header.CustomerID)
                        throw new InvalidOperationException("لا يمكن تغيير عميل الفاتورة بما يخالف عميل أمر البيع المرتبط.");
                }

                var previousRejection = SplitRejectionNote(current.Notes).RejectionReason;
                var storedNotes = CombineNotesAndRejection(header.Notes, previousRejection);
                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices SET
                          InvoiceDate=@InvoiceDate,
                          CustomerID=@CustomerID,
                          IsTaxable=@IsTaxable,
                          TaxRate=@TaxRate,
                          PaymentTermID=NULLIF(@PaymentTermID,0),
                          CurrencyID=NULLIF(@CurrencyID,0),
                          ExchangeRate=@ExchangeRate,
                          WarehouseID=NULLIF(@WarehouseID,0),
                          DiscountPercent=@DiscountPercent,
                          ShippingCost=@ShippingCost,
                          OtherCosts=@OtherCosts,
                          Notes=@Notes,
                          ModifiedBy=@UserID,
                          ModifiedDate=GETDATE()
                      WHERE InvoiceID=@InvoiceID AND InvoiceStatus=1",
                    new
                    {
                        header.InvoiceID,
                        header.InvoiceDate,
                        header.CustomerID,
                        header.IsTaxable,
                        TaxRate = header.IsTaxable ? (header.TaxRate <= 0 ? 14 : header.TaxRate) : 0,
                        header.PaymentTermID,
                        header.CurrencyID,
                        ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                        header.WarehouseID,
                        header.DiscountPercent,
                        header.ShippingCost,
                        header.OtherCosts,
                        Notes = storedNotes,
                        UserID = userId
                    }, transaction);

                if (affected != 1) throw new InvalidOperationException("تغيرت حالة الفاتورة؛ أعد تحميلها قبل الحفظ.");

                await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoiceDetails
                      SET TaxRate=@TaxRate
                      WHERE InvoiceID=@InvoiceID",
                    new
                    {
                        InvoiceID = header.InvoiceID,
                        TaxRate = header.IsTaxable ? (header.TaxRate <= 0 ? 14 : header.TaxRate) : 0
                    }, transaction);

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID",
                    new { InvoiceID = header.InvoiceID }, transaction);
                await RecalculateSalesInvoiceDueDateAsync(connection, transaction, header.InvoiceID);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", header.InvoiceID.ToString(),
                moduleName: "SCR_SINV", description: $"تعديل فاتورة مبيعات: {header.InvoiceNumber}");
        }

        // ==========================================
        // إدراج تفصيل فاتورة ضمن المعاملة الحالية
        // ==========================================
        private async Task<int> InsertInvoiceDetailCoreAsync(
            SqlConnection connection,
            IDbTransaction transaction,
            SalesInvoiceDetailEditDto detail,
            int invoiceId,
            int lineNumber,
            bool isTaxable,
            decimal invoiceTaxRate)
        {
            if (detail.Quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(detail), "أدخل كمية أكبر من صفر.");
            if (decimal.Round(detail.Quantity, 4) != detail.Quantity)
                throw new ArgumentOutOfRangeException(nameof(detail), "دقة الكمية لا تتجاوز أربع خانات عشرية حسب مخطط الفاتورة.");
            if (detail.UnitPrice < 0)
                throw new ArgumentOutOfRangeException(nameof(detail), "سعر الوحدة لا يمكن أن يكون سالباً.");
            if (detail.DiscountPercent < 0 || detail.DiscountPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(detail), "نسبة الخصم يجب أن تكون بين صفر و100.");

            var discountAmount = detail.UnitPrice * detail.Quantity * detail.DiscountPercent / 100m;
            var lineTotal = detail.UnitPrice * detail.Quantity - discountAmount;
            var taxRate = isTaxable ? Math.Max(0, invoiceTaxRate) : 0;
            var taxAmount = isTaxable ? lineTotal * taxRate / 100m : 0;
            var lineTotalWithTax = lineTotal + taxAmount;

            return await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.SalesInvoiceDetails
                  (InvoiceID, LineNumber, ItemID, UnitID, WarehouseID, Quantity, UnitPrice,
                   DiscountPercent, DiscountAmount, LineTotal, TaxRate, TaxAmount, LineTotalWithTax,
                   BatchNumber, ExpiryDate, SODetailID, Notes)
                  VALUES
                  (@InvoiceID, @LineNumber, @ItemID, @UnitID, NULLIF(@WarehouseID,0), @Quantity, @UnitPrice,
                   @DiscountPercent, @DiscountAmount, @LineTotal, @TaxRate, @TaxAmount, @LineTotalWithTax,
                   @BatchNumber, @ExpiryDate, NULLIF(@SODetailID,0), @Notes);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    InvoiceID = invoiceId,
                    LineNumber = lineNumber,
                    detail.ItemID,
                    detail.UnitID,
                    detail.WarehouseID,
                    detail.Quantity,
                    detail.UnitPrice,
                    detail.DiscountPercent,
                    DiscountAmount = discountAmount,
                    LineTotal = lineTotal,
                    TaxRate = taxRate,
                    TaxAmount = taxAmount,
                    LineTotalWithTax = lineTotalWithTax,
                    detail.BatchNumber,
                    detail.ExpiryDate,
                    SODetailID = detail.SalesOrderDetailID,
                    detail.Notes
                }, transaction);
        }

        // ==========================================
        // إضافة سطر — مع صلاحيات الخدمة وربطه بسطر الأمر عند الاقتضاء
        // ==========================================
        public async Task<int> InsertDetailAsync(SalesInvoiceDetailEditDto detail, int userId)
        {
            if (detail.InvoiceID <= 0) throw new ArgumentException("رقم الفاتورة غير صالح.", nameof(detail));
            if (detail.ItemID <= 0) throw new ArgumentException("اختر الصنف.", nameof(detail));
            if (detail.UnitID <= 0) throw new ArgumentException("اختر الوحدة.", nameof(detail));
            if (detail.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(detail), "أدخل كمية أكبر من صفر.");
            if (detail.DeliveryDetailID.GetValueOrDefault() > 0)
                throw new InvalidOperationException("ربط سطر إذن التسليم غير مدعوم في المخطط الحالي.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            int newId;
            try
            {
                var permissions = await GetCurrentInvoicePermissionsAsync(userId);
                await EnsureCanEditDraftAsync(connection, transaction, detail.InvoiceID, userId, permissions);

                var invoice = await connection.QueryFirstOrDefaultAsync<(
                    int InvoiceStatus, int? SalesOrderID, int InvoiceSource, bool IsTaxable, decimal TaxRate)>(
                    @"SELECT InvoiceStatus, SalesOrderID, ISNULL(InvoiceSource,3) AS InvoiceSource,
                             ISNULL(IsTaxable,1) AS IsTaxable, ISNULL(TaxRate,14) AS TaxRate
                      FROM dbo.SalesInvoices WITH (UPDLOCK, HOLDLOCK)
                      WHERE InvoiceID=@ID",
                    new { ID = detail.InvoiceID }, transaction);
                if (invoice.InvoiceStatus != 1)
                    throw new InvalidOperationException("لا يمكن تعديل فاتورة ليست في حالة مسودة.");
                if (invoice.InvoiceSource == 2 && invoice.SalesOrderID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("الفاتورة من أمر بيع بلا أمر مرتبط؛ لا يمكن إضافة سطر غير مربوط.");

                var isOrderInvoice = invoice.SalesOrderID.GetValueOrDefault() > 0;
                if (isOrderInvoice)
                {
                    if (detail.SalesOrderDetailID.GetValueOrDefault() <= 0)
                        throw new InvalidOperationException("اختر سطر أمر البيع لربط تفصيل الفاتورة به.");

                    var orderLine = await connection.QueryFirstOrDefaultAsync<(
                        int SODetailID, int ItemID, int UnitID, decimal UnitPrice, decimal DiscountPercent)>(
                        @"SELECT sod.SODetailID, sod.ItemID, sod.UnitID,
                                 ISNULL(sod.UnitPrice,0) AS UnitPrice,
                                 ISNULL(sod.DiscountPercent,0) AS DiscountPercent
                          FROM dbo.SalesOrderDetails sod WITH (UPDLOCK, HOLDLOCK)
                          INNER JOIN dbo.SalesOrders so WITH (UPDLOCK, HOLDLOCK)
                              ON so.SalesOrderID=sod.SalesOrderID AND so.SOStatus=3
                          WHERE sod.SODetailID=@SODetailID AND sod.SalesOrderID=@SalesOrderID",
                        new { SODetailID = detail.SalesOrderDetailID, SalesOrderID = invoice.SalesOrderID }, transaction);

                    if (orderLine.SODetailID <= 0)
                        throw new InvalidOperationException("سطر أمر البيع لا يتبع أمر البيع المرتبط بهذه الفاتورة.");
                    if (orderLine.ItemID != detail.ItemID || orderLine.UnitID != detail.UnitID)
                        throw new InvalidOperationException("الصنف والوحدة يجب أن يطابقا سطر أمر البيع المرتبط.");

                    // السعر والخصم من السطر الأصلي؛ لا يقبل تعديل صامت لقيم الأمر من واجهة الفاتورة.
                    detail.UnitPrice = orderLine.UnitPrice;
                    detail.DiscountPercent = orderLine.DiscountPercent;
                }
                else if (detail.SalesOrderDetailID.HasValue)
                {
                    throw new InvalidOperationException("لا يمكن ربط سطر أمر بيع بفاتورة مباشرة.");
                }

                await ResolveToPrimaryUnitFactorAsync(connection, transaction, detail.ItemID, detail.UnitID);
                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ISNULL(MAX(LineNumber),0) FROM dbo.SalesInvoiceDetails WHERE InvoiceID=@ID",
                    new { ID = detail.InvoiceID }, transaction);
                detail.LineNumber = maxLine + 1;

                newId = await InsertInvoiceDetailCoreAsync(
                    connection, transaction, detail, detail.InvoiceID, detail.LineNumber,
                    invoice.IsTaxable, invoice.TaxRate);
                await connection.ExecuteAsync("EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID",
                    new { InvoiceID = detail.InvoiceID }, transaction);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 1, "SalesInvoiceDetails", newId.ToString(),
                moduleName: "SCR_SINV", description: $"إضافة سطر في فاتورة مبيعات رقم {detail.InvoiceID}");
            return newId;
        }

        // ==========================================
        // حذف سطر
        // ==========================================
        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanDelete,
                "لا تملك صلاحية حذف سطر من فاتورة المبيعات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            int invoiceId;
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(int InvoiceID, int InvoiceStatus)>(
                    @"SELECT sid.InvoiceID, si.InvoiceStatus
                      FROM dbo.SalesInvoiceDetails sid WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.SalesInvoices si WITH (UPDLOCK, HOLDLOCK) ON si.InvoiceID=sid.InvoiceID
                      WHERE sid.InvoiceDetailID=@ID",
                    new { ID = detailId }, transaction);

                if (info.InvoiceID == 0) throw new KeyNotFoundException("سطر الفاتورة غير موجود.");
                if (info.InvoiceStatus != 1)
                    throw new InvalidOperationException("لا يمكن حذف سطر من فاتورة ليست في حالة مسودة.");

                var affected = await connection.ExecuteAsync(
                    "DELETE FROM dbo.SalesInvoiceDetails WHERE InvoiceDetailID=@ID AND InvoiceID=@InvoiceID",
                    new { ID = detailId, InvoiceID = info.InvoiceID }, transaction);
                if (affected != 1) throw new InvalidOperationException("تعذر حذف سطر الفاتورة.");

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID",
                    new { InvoiceID = info.InvoiceID }, transaction);
                invoiceId = info.InvoiceID;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 3, "SalesInvoiceDetails", detailId.ToString(),
                moduleName: "SCR_SINV", description: $"حذف سطر من فاتورة مبيعات رقم {invoiceId}");
        }

        // ==========================================
        // تحميل سطور من Delivery - معطل لأن جدول DeliveryNotes غير موجود (ملتزم بالجداول الموجودة فقط)
        // ==========================================
        public async Task<int> ImportLinesFromDeliveryAsync(int invoiceId, int deliveryId, List<DeliveryLineForInvoiceDto> lines, int userId)
        {
            throw new Exception("جدول إذن التسليم غير موجود حالياً - ملتزم بالجداول الموجودة فقط. استخدم أمر بيع أو فاتورة مباشرة.");
        }

        // ==========================================
        // إرسال للاعتماد
        // ==========================================
        public async Task SubmitForApprovalAsync(int invoiceId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            string invoiceNumber;
            try
            {
                var permissions = await GetCurrentInvoicePermissionsAsync(userId);
                await EnsureCanEditDraftAsync(connection, transaction, invoiceId, userId, permissions);

                var info = await connection.QueryFirstOrDefaultAsync<(
                    string? InvoiceNumber, int InvoiceStatus, int InvoiceSource, int? SalesOrderID,
                    int DetailCount, int MissingWarehouseCount, int InvalidOrderLineCount)>(
                    @"SELECT si.InvoiceNumber, si.InvoiceStatus,
                             ISNULL(si.InvoiceSource,3) AS InvoiceSource, si.SalesOrderID,
                             (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.InvoiceID=si.InvoiceID) AS DetailCount,
                             (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d
                              WHERE d.InvoiceID=si.InvoiceID AND COALESCE(d.WarehouseID,si.WarehouseID) IS NULL) AS MissingWarehouseCount,
                             (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d
                              WHERE d.InvoiceID=si.InvoiceID
                                AND (d.SODetailID IS NULL OR NOT EXISTS
                                     (SELECT 1 FROM dbo.SalesOrderDetails sod
                                      WHERE sod.SODetailID=d.SODetailID
                                        AND sod.SalesOrderID=si.SalesOrderID
                                        AND sod.ItemID=d.ItemID AND sod.UnitID=d.UnitID))) AS InvalidOrderLineCount
                      FROM dbo.SalesInvoices si WITH (UPDLOCK, HOLDLOCK)
                      WHERE si.InvoiceID=@ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new KeyNotFoundException("الفاتورة غير موجودة.");
                if (info.InvoiceStatus != 1) throw new InvalidOperationException("لا يمكن إرسال فاتورة ليست في حالة مسودة.");
                if (info.DetailCount == 0) throw new InvalidOperationException("لا يمكن إرسال فاتورة بدون أصناف.");
                if (info.MissingWarehouseCount > 0)
                    throw new InvalidOperationException("اختر مخزناً للفواتير أو لسطورها قبل إرسالها للاعتماد.");
                if (info.InvoiceSource == 2 && info.SalesOrderID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("الفاتورة من أمر بيع بلا أمر مرتبط؛ لا يمكن إرسالها للاعتماد.");
                if (info.SalesOrderID.GetValueOrDefault() > 0 && info.InvalidOrderLineCount > 0)
                    throw new InvalidOperationException("يوجد سطر في فاتورة أمر البيع غير مرتبط بسطر مطابق من الأمر؛ أصلح الربط قبل الإرسال.");

                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices
                      SET InvoiceStatus=2, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE InvoiceID=@ID AND InvoiceStatus=1",
                    new { ID = invoiceId, UserID = userId }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة الفاتورة؛ أعد تحميلها قبل الإرسال.");

                invoiceNumber = info.InvoiceNumber;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            try
            {
                await _notif.CreateForPermissionHoldersAsync(
                    notificationType: 3,
                    title: "فاتورة مبيعات بانتظار الاعتماد",
                    message: $"فاتورة المبيعات رقم {invoiceNumber} بانتظار الاعتماد",
                    priority: 1,
                    permissionModuleCode: "SCR_SINV",
                    requiredPermission: ModulePermissionRequirement.Approve,
                    relatedModule: "SCR_SINV",
                    relatedRecordId: invoiceId,
                    createdBy: userId);
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV", description: $"إرسال فاتورة مبيعات للاعتماد: {invoiceNumber}");
        }

        // ==========================================
        // اعتماد الفاتورة + خصم مخزون + قيد محاسبي أوتوماتيك
        // ==========================================
        public async Task ApproveAsync(int invoiceId, int userId, int? employeeId = null)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanApprove,
                "لا تملك صلاحية اعتماد فواتير المبيعات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            string invoiceNumber;
            int? createdBy;
            int customerId;
            bool isTaxable;
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(
                    string? InvoiceNumber, int InvoiceStatus, int InvoiceSource, int? SalesOrderID,
                    int? DeliveryID, int? CreatedBy, int CustomerID, bool IsTaxable, decimal ExchangeRate,
                    bool IsDefaultCurrency, decimal PaidAmount, int DetailCount, int MissingWarehouseCount,
                    int InvalidOrderLineCount)>(
                    @"SELECT si.InvoiceNumber, si.InvoiceStatus, ISNULL(si.InvoiceSource,3) AS InvoiceSource,
                             si.SalesOrderID, si.DeliveryID, si.CreatedBy, si.CustomerID,
                             ISNULL(si.IsTaxable,1) AS IsTaxable,
                             ISNULL(si.ExchangeRate,1) AS ExchangeRate,
                             ISNULL(cur.IsDefault,1) AS IsDefaultCurrency,
                             ISNULL(si.PaidAmount,0) AS PaidAmount,
                             (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.InvoiceID=si.InvoiceID) AS DetailCount,
                             (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d
                              WHERE d.InvoiceID=si.InvoiceID AND COALESCE(d.WarehouseID,si.WarehouseID) IS NULL) AS MissingWarehouseCount,
                             (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d
                              WHERE d.InvoiceID=si.InvoiceID
                                AND (d.SODetailID IS NULL OR NOT EXISTS
                                     (SELECT 1 FROM dbo.SalesOrderDetails sod
                                      WHERE sod.SODetailID=d.SODetailID
                                        AND sod.SalesOrderID=si.SalesOrderID
                                        AND sod.ItemID=d.ItemID AND sod.UnitID=d.UnitID))) AS InvalidOrderLineCount
                      FROM dbo.SalesInvoices si WITH (UPDLOCK, HOLDLOCK)
                      LEFT JOIN dbo.Currencies cur ON cur.CurrencyID=si.CurrencyID
                      WHERE si.InvoiceID=@ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new KeyNotFoundException("الفاتورة غير موجودة.");
                if (info.InvoiceStatus != 2) throw new InvalidOperationException("لا يمكن اعتماد فاتورة ليست في انتظار الاعتماد.");
                if (!info.IsDefaultCurrency || Math.Abs(info.ExchangeRate - 1m) > 0.000001m)
                    throw new InvalidOperationException("القيد المحاسبي الحالي لا يدعم العملات غير الافتراضية أو سعر صرف مختلف عن 1؛ لم يتم اعتماد الفاتورة.");
                if (info.PaidAmount > 0)
                    throw new InvalidOperationException("يوجد مبلغ مدفوع مسبقاً لكن مسار ترحيل الدفعات غير متاح لهذه الفاتورة؛ لم يتم اعتمادها.");
                if (info.DetailCount == 0) throw new InvalidOperationException("لا يمكن اعتماد فاتورة بدون أصناف.");
                if (info.MissingWarehouseCount > 0)
                    throw new InvalidOperationException("يوجد سطر بلا مخزن؛ لم يتم اعتماد الفاتورة.");
                if (info.InvoiceSource == 2 && info.SalesOrderID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException("الفاتورة موسومة بأنها من أمر بيع لكن رقم الأمر غير مرتبط بها؛ لم يتم اعتمادها.");
                if (info.SalesOrderID.GetValueOrDefault() > 0 && info.InvalidOrderLineCount > 0)
                    throw new InvalidOperationException("يوجد سطر فاتورة غير مرتبط بسطر مطابق من أمر البيع؛ لم يتم اعتمادها.");
                if (info.DeliveryID.GetValueOrDefault() > 0 || info.InvoiceSource == 1)
                    throw new InvalidOperationException("لا يمكن ترحيل فاتورة من إذن تسليم قبل ربط مصدر خصم المخزون الخاص بذلك الإذن.");

                var alreadyPosted = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.InventoryTransactions WITH (UPDLOCK, HOLDLOCK)
                      WHERE SourceDocType IN (N'SINV',N'SI') AND TransactionType=4
                        AND (SourceDocID=@InvoiceID OR SourceDocNumber=@InvoiceNumber)",
                    new { InvoiceID = invoiceId, InvoiceNumber = info.InvoiceNumber }, transaction);
                if (alreadyPosted > 0)
                    throw new InvalidOperationException("توجد حركات مخزون سابقة لهذه الفاتورة؛ أوقفنا الاعتماد لمنع الخصم المكرر.");

                var existingJournal = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE SourceDocType=N'SI'
                        AND (SourceDocID=@InvoiceID OR SourceDocNumber=@InvoiceNumber)",
                    new { InvoiceID = invoiceId, InvoiceNumber = info.InvoiceNumber }, transaction);
                if (existingJournal > 0)
                    throw new InvalidOperationException("يوجد قيد محاسبي سابق لهذه الفاتورة؛ راجعها قبل إعادة الاعتماد.");

                invoiceNumber = info.InvoiceNumber;
                createdBy = info.CreatedBy;
                customerId = info.CustomerID;
                isTaxable = info.IsTaxable;

                var statusUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices
                      SET InvoiceStatus=3, PaymentStatus=1,
                          ApprovedBy=@EmployeeID, ApprovedDate=GETDATE(),
                          ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE InvoiceID=@ID AND InvoiceStatus=2",
                    new { ID = invoiceId, EmployeeID = employeeId, UserID = userId }, transaction);
                if (statusUpdated != 1)
                    throw new InvalidOperationException("تغيرت حالة الفاتورة؛ أعد تحميلها قبل الاعتماد.");

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID",
                    new { InvoiceID = invoiceId }, transaction);

                await PostInvoiceStockFifoAsync(connection, transaction, invoiceId, invoiceNumber, userId);

                var totalCost = await connection.ExecuteScalarAsync<decimal>(
                    "SELECT ISNULL(SUM(LineCost),0) FROM dbo.SalesInvoiceDetails WHERE InvoiceID=@InvoiceID",
                    new { InvoiceID = invoiceId }, transaction);
                var computedProfitColumns = (await connection.QueryAsync<string>(
                    @"SELECT [name]
                      FROM sys.columns
                      WHERE object_id=OBJECT_ID(N'dbo.SalesInvoices')
                        AND is_computed=1
                        AND [name] IN (N'GrossProfit',N'GrossProfitPercent')",
                    transaction: transaction))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var invoiceCostAssignments = new List<string> { "TotalCost=@TotalCost" };
                if (!computedProfitColumns.Contains("GrossProfit"))
                    invoiceCostAssignments.Add("GrossProfit=ISNULL(TaxableAmount,0)-@TotalCost");
                if (!computedProfitColumns.Contains("GrossProfitPercent"))
                    invoiceCostAssignments.Add(
                        "GrossProfitPercent=CASE WHEN ISNULL(TaxableAmount,0)=0 THEN 0 ELSE ((ISNULL(TaxableAmount,0)-@TotalCost)*100.0/TaxableAmount) END");

                await connection.ExecuteAsync(
                    $@"UPDATE dbo.SalesInvoices
                       SET {string.Join(", ", invoiceCostAssignments)}
                       WHERE InvoiceID=@InvoiceID",
                    new { InvoiceID = invoiceId, TotalCost = totalCost }, transaction);

                var receivableChange = await connection.ExecuteScalarAsync<decimal>(
                    @"SELECT ISNULL(TotalAmount,0)-ISNULL(PaidAmount,0)
                      FROM dbo.SalesInvoices WHERE InvoiceID=@InvoiceID",
                    new { InvoiceID = invoiceId }, transaction);
                if (receivableChange < 0)
                    throw new InvalidOperationException("المدفوع على الفاتورة أكبر من إجماليها؛ راجع الرصيد قبل الاعتماد.");

                var customerBalanceUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.Customers
                      SET CurrentBalance=ISNULL(CurrentBalance,0)+@BalanceChange,
                          ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE CustomerID=@CustomerID",
                    new { CustomerID = customerId, BalanceChange = receivableChange, UserID = userId }, transaction);
                if (customerBalanceUpdated != 1)
                    throw new InvalidOperationException("تعذر تحديث رصيد العميل؛ لم يتم اعتماد الفاتورة.");

                await connection.ExecuteAsync(
                    "EXEC dbo.sp_CreateSalesInvoiceJournal @InvoiceID, @UserID",
                    new { InvoiceID = invoiceId, UserID = userId }, transaction);

                var journalBalance = await connection.QueryFirstOrDefaultAsync<(int JournalCount, decimal DebitTotal, decimal CreditTotal)>(
                    @"SELECT COUNT(DISTINCT je.JournalID) AS JournalCount,
                             ISNULL(SUM(jed.DebitAmount),0) AS DebitTotal,
                             ISNULL(SUM(jed.CreditAmount),0) AS CreditTotal
                      FROM dbo.JournalEntries je
                      LEFT JOIN dbo.JournalEntryDetails jed ON jed.JournalID=je.JournalID
                      WHERE je.SourceDocType=N'SI' AND je.SourceDocID=@InvoiceID",
                    new { InvoiceID = invoiceId }, transaction);
                if (journalBalance.JournalCount != 1 ||
                    Math.Abs(journalBalance.DebitTotal - journalBalance.CreditTotal) > 0.01m)
                    throw new InvalidOperationException(
                        $"قيد الفاتورة المحاسبي غير متوازن (مدين {journalBalance.DebitTotal:#,##0.00} / دائن {journalBalance.CreditTotal:#,##0.00}). " +
                        "لم يتم اعتماد الفاتورة؛ راجع حسابات الشحن والتكاليف الأخرى وإعدادات الحسابات.");

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            try { await _notif.MarkRelatedAsActionedAsync("SCR_SINV", invoiceId); } catch { }
            try
            {
                if (createdBy.HasValue)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم اعتماد فاتورة المبيعات",
                        message: $"تم اعتماد فاتورة المبيعات رقم {invoiceNumber} {(isTaxable ? "مع ضريبة" : "بدون ضريبة")}",
                        priority: 2,
                        targetUserId: createdBy.Value,
                        permissionModuleCode: "SCR_SINV",
                        relatedModule: "SCR_SINV",
                        relatedRecordId: invoiceId,
                        createdBy: userId);
                }
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV",
                description: $"اعتماد فاتورة مبيعات: {invoiceNumber} {(isTaxable ? "مع ضريبة" : "بدون ضريبة")}");
        }

        // ==========================================
        // رفض / إلغاء / حذف
        // ==========================================
        public async Task RejectAsync(int invoiceId, string rejectionReason, int userId, int? employeeId = null)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanApprove,
                "لا تملك صلاحية رفض فواتير المبيعات.");
            if (string.IsNullOrWhiteSpace(rejectionReason)) throw new ArgumentException("أدخل سبب الرفض.", nameof(rejectionReason));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            string invoiceNumber;
            int? createdBy;
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string? InvoiceNumber, int InvoiceStatus, int? CreatedBy, string? Notes)>(
                    @"SELECT InvoiceNumber, InvoiceStatus, CreatedBy, Notes
                      FROM dbo.SalesInvoices WITH (UPDLOCK, HOLDLOCK)
                      WHERE InvoiceID=@ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new KeyNotFoundException("الفاتورة غير موجودة.");
                if (info.InvoiceStatus != 2)
                    throw new InvalidOperationException("لا يمكن رفض فاتورة ليست في انتظار الاعتماد.");

                var storedNotes = CombineNotesAndRejection(info.Notes, rejectionReason);
                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices
                      SET InvoiceStatus=1, Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE InvoiceID=@ID AND InvoiceStatus=2",
                    new { ID = invoiceId, Notes = storedNotes, UserID = userId }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة الفاتورة؛ أعد تحميلها قبل الرفض.");

                invoiceNumber = info.InvoiceNumber;
                createdBy = info.CreatedBy;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            try { await _notif.MarkRelatedAsActionedAsync("SCR_SINV", invoiceId); } catch { }
            try
            {
                if (createdBy.HasValue)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        notificationType: 3,
                        title: "تم رفض فاتورة المبيعات",
                        message: $"تم رفض فاتورة المبيعات رقم {invoiceNumber}. السبب: {rejectionReason}",
                        priority: 1,
                        targetUserId: createdBy.Value,
                        permissionModuleCode: "SCR_SINV",
                        relatedModule: "SCR_SINV",
                        relatedRecordId: invoiceId,
                        createdBy: userId);
                }
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV", description: $"رفض فاتورة مبيعات: {invoiceNumber} — {rejectionReason}");
        }

        public async Task CancelAsync(int invoiceId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanCancel,
                "لا تملك صلاحية إلغاء فواتير المبيعات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            string invoiceNumber;
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string? InvoiceNumber, int InvoiceStatus, decimal PaidAmount)>(
                    @"SELECT InvoiceNumber, InvoiceStatus, ISNULL(PaidAmount,0) AS PaidAmount
                      FROM dbo.SalesInvoices WITH (UPDLOCK, HOLDLOCK)
                      WHERE InvoiceID=@ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new KeyNotFoundException("الفاتورة غير موجودة.");
                if (info.InvoiceStatus == 6) throw new InvalidOperationException("الفاتورة ملغاة بالفعل.");
                if (info.InvoiceStatus is not (1 or 2))
                    throw new InvalidOperationException("لا يمكن إلغاء فاتورة تم اعتمادها؛ استخدم مسار مرتجع/عكس معتمد.");
                if (info.PaidAmount > 0) throw new InvalidOperationException("لا يمكن إلغاء فاتورة عليها دفعة.");

                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices SET InvoiceStatus=6, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE InvoiceID=@ID AND InvoiceStatus IN (1,2)",
                    new { ID = invoiceId, UserID = userId }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة الفاتورة؛ أعد تحميلها قبل الإلغاء.");

                invoiceNumber = info.InvoiceNumber;
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            try { await _notif.MarkRelatedAsActionedAsync("SCR_SINV", invoiceId); } catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV", description: $"إلغاء فاتورة مبيعات: {invoiceNumber}");
        }

        public async Task<(bool Success, string Message)> DeleteInvoiceAsync(int invoiceId, int userId)
        {
            await RequireInvoicePermissionAsync(userId, p => p.CanDelete,
                "لا تملك صلاحية حذف فواتير المبيعات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            string? invoiceNumber;
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string? InvoiceNumber, int InvoiceStatus)>(
                    @"SELECT InvoiceNumber, InvoiceStatus
                      FROM dbo.SalesInvoices WITH (UPDLOCK, HOLDLOCK)
                      WHERE InvoiceID=@ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber))
                {
                    transaction.Rollback();
                    return (false, "الفاتورة غير موجودة.");
                }
                if (info.InvoiceStatus != 1)
                {
                    transaction.Rollback();
                    return (false, "لا يمكن حذف فاتورة ليست في حالة مسودة.");
                }

                invoiceNumber = info.InvoiceNumber;
                await connection.ExecuteAsync(
                    "DELETE FROM dbo.SalesInvoiceDetails WHERE InvoiceID=@ID",
                    new { ID = invoiceId }, transaction);
                var affected = await connection.ExecuteAsync(
                    "DELETE FROM dbo.SalesInvoices WHERE InvoiceID=@ID AND InvoiceStatus=1",
                    new { ID = invoiceId }, transaction);
                if (affected != 1) throw new InvalidOperationException("تغيرت حالة الفاتورة؛ أعد تحميلها قبل الحذف.");

                transaction.Commit();
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); } catch { }
                return (false, ex.Message);
            }

            await _audit.WriteAuditLogAsync(userId, 3, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV", description: $"حذف فاتورة مبيعات: {invoiceNumber}");
            return (true, "تم حذف الفاتورة بنجاح.");
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<SalesInvoiceListDto> invoices, int userId)
        {
            await RequireViewAndExportAsync(userId);
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("فواتير المبيعات");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Tajawal";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير فواتير المبيعات — واي كي كوتينج Gold Edition";
            ws.Range(1, 1, 1, 12).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 12).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[] { "#", "رقم الفاتورة", "التاريخ", "العميل", "المصدر", "ضريبة؟", "الإجمالي", "المدفوع", "المتبقي", "الاستحقاق", "الحالة", "الحالة دفع" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#070B14");
                cell.Style.Font.FontColor = XLColor.FromHtml("#D4AF37");
            }

            int row = headerRow + 1;
            int num = 0;
            foreach (var inv in invoices)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = inv.InvoiceNumber ?? "";
                ws.Cell(row, 3).Value = inv.InvoiceDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = inv.CustomerNameAr ?? "";
                ws.Cell(row, 5).Value = inv.InvoiceSourceName ?? "";
                ws.Cell(row, 6).Value = inv.IsTaxable ? "مع ضريبة" : "بدون";
                ws.Cell(row, 7).Value = inv.TotalAmount;
                ws.Cell(row, 8).Value = inv.PaidAmount;
                ws.Cell(row, 9).Value = inv.RemainingAmount;
                ws.Cell(row, 10).Value = inv.DueDate?.ToString("dd/MM/yyyy") ?? "—";
                ws.Cell(row, 11).Value = GetStatusName(inv.InvoiceStatus);
                ws.Cell(row, 12).Value = inv.PaymentStatusName ?? "";
                if (num % 2 == 0)
                    ws.Range(row, 1, row, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");
                row++;
            }

            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "SalesInvoices",
                moduleName: "SCR_SINV",
                description: $"تصدير {invoices.Count} فاتورة مبيعات إلى Excel");

            return stream.ToArray();
        }

        // ==========================================
        // طباعة HTML
        // ==========================================
        public async Task<string> GenerateReportHtmlAsync(int invoiceId, int userId)
        {
            await RequireViewAndPrintAsync(userId);
            var header = await GetInvoiceByIdCoreAsync(invoiceId);
            if (header == null) return "<h3>الفاتورة غير موجودة</h3>";
            var details = await GetInvoiceDetailsCoreAsync(invoiceId);

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
                    <td style='text-align:center'>{(header.IsTaxable ? $"{d.TaxRate:#,##0.##}%" : "—")}</td>
                    <td style='text-align:left'>{d.LineTotalWithTax:#,##0.00}</td></tr>";
            }

            var taxRow = header.IsTaxable ? $"<div class='total-line'><span>الضريبة ({header.TaxRate:#,##0.##}%)</span><span>{header.TaxAmount:#,##0.00}</span></div>" : "<div class='total-line'><span>الضريبة</span><span>معفاة</span></div>";

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'>
<title>فاتورة مبيعات — {header.InvoiceNumber}</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Tajawal','Segoe UI',sans-serif;padding:30px;color:#070B14;font-size:13px;background:#fff}}
.header{{text-align:center;border-bottom:3px solid #070B14;padding-bottom:20px;margin-bottom:24px;position:relative}}
.header::after{{content:'';position:absolute;bottom:-3px;left:0;right:0;height:3px;background:linear-gradient(90deg,transparent,#D4AF37,#070B14,#D4AF37,transparent)}}
.company{{font-size:20px;font-weight:900;color:#070B14}}
.doc-title{{font-size:15px;color:#6b7280;margin-top:4px}}
.doc-num{{display:inline-block;background:rgba(212,175,55,.1);border:1px solid rgba(212,175,55,.2);padding:4px 16px;border-radius:8px;font-weight:800;color:#9C7C2E;margin-top:8px}}
.badge-tax{{display:inline-block;padding:3px 10px;border-radius:999px;font-size:.7rem;font-weight:800;margin-right:8px}}
.badge-tax.yes{{background:#ecfdf5;color:#059669;border:1px solid #a7f3d0}}
.badge-tax.no{{background:#f3f4f6;color:#6b7280;border:1px solid #e5e7eb}}
.info-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:20px}}
.info-item{{background:#fcfaf6;padding:10px;border-radius:8px;border:1px solid #e8e2d0}}
.info-label{{font-size:10px;color:#6b7280;font-weight:600}}
.info-value{{font-size:13px;font-weight:700;margin-top:2px}}
table{{width:100%;border-collapse:collapse;margin-bottom:16px}}
th{{background:#070B14;color:#D4AF37;padding:10px;font-size:12px;text-align:right}}
td{{padding:9px 10px;border-bottom:1px solid #e8e2d0;font-size:12px}}
tr:nth-child(even){{background:#fcfaf6}}
.total-row{{font-weight:800;background:#fcfaf6 !important}}
.totals-section{{margin-top:16px;display:flex;justify-content:flex-end}}
.totals-box{{background:#fcfaf6;border:1px solid #e8e2d0;border-radius:10px;padding:16px;min-width:280px;position:relative;overflow:hidden}}
.totals-box::before{{content:'';position:absolute;top:0;left:0;right:0;height:2px;background:linear-gradient(90deg,transparent,#D4AF37,transparent)}}
.total-line{{display:flex;justify-content:space-between;padding:4px 0;font-size:12px}}
.total-line.grand{{font-weight:800;font-size:14px;border-top:2px solid #070B14;margin-top:8px;padding-top:8px}}
.footer{{text-align:center;margin-top:30px;padding-top:12px;border-top:1px solid #e8e2d0;font-size:10px;color:#9ca3af}}
</style></head><body>
<div class='header'>
    <div class='company'>واي كي كوتينج لمستحضرات التجميل — YK Coatings</div>
    <div class='doc-title'>فاتورة مبيعات</div>
    <div><span class='doc-num'>{header.InvoiceNumber}</span><span class='badge-tax {(header.IsTaxable ? "yes" : "no")}'>{(header.IsTaxable ? $"مع ضريبة {header.TaxRate:#,##0.##}%" : "بدون ضريبة - معفاة")}</span></div>
</div>
<div class='info-grid'>
    <div class='info-item'><div class='info-label'>تاريخ الفاتورة</div><div class='info-value'>{header.InvoiceDate:dd/MM/yyyy}</div></div>
    <div class='info-item'><div class='info-label'>العميل</div><div class='info-value'>{header.CustomerNameAr}</div></div>
    <div class='info-item'><div class='info-label'>المصدر</div><div class='info-value'>{(header.InvoiceSource == 2 ? "من أمر بيع - اختياري" : "فاتورة مباشرة - الأساس")}</div></div>
    <div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(header.InvoiceStatus)}</div></div>
    <div class='info-item'><div class='info-label'>شروط الدفع</div><div class='info-value'>{header.PaymentTermName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>تاريخ الاستحقاق</div><div class='info-value'>{header.DueDate?.ToString("dd/MM/yyyy") ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>المخزن</div><div class='info-value'>{header.WarehouseNameAr ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>أمر البيع</div><div class='info-value'>{header.LinkedSONumber ?? "— (مباشرة)"}</div></div>
</div>
<table>
    <thead><tr><th>#</th><th>الكود</th><th>الصنف</th><th>الوحدة</th><th>الكمية</th><th>السعر</th><th>خصم</th><th>ضريبة</th><th>الإجمالي</th></tr></thead>
    <tbody>{detailsHtml}</tbody>
</table>
<div class='totals-section'>
    <div class='totals-box'>
        <div class='total-line'><span>الإجمالي الفرعي</span><span>{header.SubTotal:#,##0.00}</span></div>
        {(header.DiscountAmount > 0 ? $"<div class='total-line'><span>الخصم</span><span style='color:#ef4444'>- {header.DiscountAmount:#,##0.00}</span></div>" : "")}
        {taxRow}
        {(header.ShippingCost > 0 ? $"<div class='total-line'><span>الشحن</span><span>{header.ShippingCost:#,##0.00}</span></div>" : "")}
        {(header.OtherCosts > 0 ? $"<div class='total-line'><span>تكاليف أخرى</span><span>{header.OtherCosts:#,##0.00}</span></div>" : "")}
        <div class='total-line grand'><span>الإجمالي الكلي</span><span>{header.TotalAmount:#,##0.00} {(header.CurrencySymbol ?? "ج.م")}</span></div>
        <div class='total-line'><span>المدفوع</span><span>{header.PaidAmount:#,##0.00}</span></div>
        <div class='total-line' style='color:#dc2626;font-weight:700'><span>المتبقي</span><span>{header.RemainingAmount:#,##0.00}</span></div>
    </div>
</div>
{(string.IsNullOrWhiteSpace(header.Notes) ? "" : $"<div style='margin-top:16px'><strong>ملاحظات:</strong> {header.Notes}</div>")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP Gold Edition — {(header.IsTaxable ? "فاتورة ضريبية" : "فاتورة غير ضريبية")}</div>
</body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV",
                description: $"طباعة فاتورة مبيعات: {header.InvoiceNumber}");

            return html;
        }

        // Helpers
        public static string GetStatusName(int status) => status switch
        {
            1 => "مسودة",
            2 => "بانتظار الاعتماد",
            3 => "معتمدة",
            4 => "مدفوعة بالكامل",
            5 => "متأخرة",
            6 => "ملغية",
            _ => "غير محدد"
        };

        public static string GetStatusColor(int status) => status switch
        {
            1 => "#6b7280",
            2 => "#f59e0b",
            3 => "#10b981",
            4 => "#14b8a6",
            5 => "#ef4444",
            6 => "#dc2626",
            _ => "#6b7280"
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
            1 => "من إذن تسليم", 2 => "من أمر بيع", 3 => "فاتورة مباشرة",
            _ => "فاتورة مباشرة"
        };

        public static string GetSourceColor(int source) => source switch
        {
            1 => "#8b5cf6", 2 => "#1d4ed8", 3 => "#D4AF37", _ => "#D4AF37"
        };
    }
}
