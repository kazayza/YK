using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class SalesOrderService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public SalesOrderService(IConfiguration configuration, AuditService audit, NotificationService notif) : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        // ==========================================
        // قائمة أوامر البيع - السكيما الفعلية 28 عمود: SONumber, SODate, SOStatus, RequiredDate, DeliveryDate...
        // ==========================================
        public async Task<List<SalesOrderListDto>> GetOrdersListAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"
SELECT
    so.SalesOrderID,
    so.SONumber AS OrderNumber,
    so.SODate AS OrderDate,
    so.SOStatus AS OrderStatus,
    CASE WHEN so.QuotationID IS NOT NULL THEN 2 ELSE 1 END AS OrderSource,
    CASE WHEN so.QuotationID IS NOT NULL THEN N'من عرض سعر' ELSE N'يدوي' END AS OrderSourceName,
    so.CustomerID, c.CustomerCode, c.CustomerNameAr,
    so.QuotationID, sq.QuotationNumber AS LinkedQuotationNumber,
    so.SalesRepID, ISNULL(sr.RepName, e.FullNameAr) AS SalesRepName,
    ISNULL(so.TotalAmount,0) AS TotalAmount, ISNULL(so.TaxAmount,0) AS TaxAmount, ISNULL(so.SubTotal,0) AS SubTotal,
    so.RequiredDate AS ExpectedDeliveryDate, so.ApprovedDate,
    ISNULL((SELECT COUNT(*) FROM dbo.SalesOrderDetails d WHERE d.SalesOrderID=so.SalesOrderID),0) AS ItemCount,
    ISNULL((SELECT SUM(OrderedQty) FROM dbo.SalesOrderDetails d WHERE d.SalesOrderID=so.SalesOrderID),0) AS TotalOrderedQty,
    ISNULL((SELECT SUM(ISNULL(DeliveredQty,0)) FROM dbo.SalesOrderDetails d WHERE d.SalesOrderID=so.SalesOrderID),0) AS TotalDeliveredQty,
    ISNULL((SELECT SUM(ISNULL(RemainingQty, OrderedQty - ISNULL(DeliveredQty,0))) FROM dbo.SalesOrderDetails d WHERE d.SalesOrderID=so.SalesOrderID),0) AS TotalRemainingQty,
    CASE WHEN so.RequiredDate IS NOT NULL AND so.RequiredDate < CAST(GETDATE() AS DATE) AND so.SOStatus IN (1,2,3) THEN 1 ELSE 0 END AS IsOverdue,
    CASE WHEN so.RequiredDate IS NOT NULL AND so.RequiredDate < CAST(GETDATE() AS DATE) THEN DATEDIFF(DAY, so.RequiredDate, GETDATE()) ELSE 0 END AS DaysOverdue,
    w.WarehouseNameAr, pt.TermNameAr AS PaymentTermName, cur.CurrencyCode, pl.PriceListName,
    NULL AS RejectionReason,
    so.CreatedDate
FROM dbo.SalesOrders so
INNER JOIN dbo.Customers c ON so.CustomerID=c.CustomerID
LEFT JOIN dbo.SalesQuotations sq ON so.QuotationID=sq.QuotationID
LEFT JOIN dbo.SalesRepresentatives sr ON so.SalesRepID=sr.SalesRepID
LEFT JOIN dbo.Employees e ON sr.EmployeeID=e.EmployeeID
LEFT JOIN dbo.Warehouses w ON so.WarehouseID=w.WarehouseID
LEFT JOIN dbo.PaymentTerms pt ON so.PaymentTermID=pt.PaymentTermID
LEFT JOIN dbo.Currencies cur ON so.CurrencyID=cur.CurrencyID
LEFT JOIN dbo.PriceLists pl ON so.PriceListID=pl.PriceListID
ORDER BY so.SODate DESC, so.SalesOrderID DESC";
                var result = await connection.QueryAsync<SalesOrderListDto>(sql);
                return result.ToList();
            }
            catch (Exception ex)
            {
                // Fallback مبسط جداً
                try
                {
                    var sql2 = @"
SELECT
    so.SalesOrderID,
    so.SONumber AS OrderNumber,
    so.SODate AS OrderDate,
    so.SOStatus AS OrderStatus,
    1 AS OrderSource, N'يدوي' AS OrderSourceName,
    so.CustomerID, c.CustomerCode, c.CustomerNameAr,
    so.QuotationID, NULL AS LinkedQuotationNumber,
    so.SalesRepID, NULL AS SalesRepName,
    ISNULL(so.TotalAmount,0) AS TotalAmount, ISNULL(so.TaxAmount,0) AS TaxAmount, ISNULL(so.SubTotal,0) AS SubTotal,
    so.RequiredDate AS ExpectedDeliveryDate, so.ApprovedDate,
    0 AS ItemCount, 0 AS TotalOrderedQty, 0 AS TotalDeliveredQty, 0 AS TotalRemainingQty,
    0 AS IsOverdue, 0 AS DaysOverdue,
    NULL AS WarehouseNameAr, NULL AS PaymentTermName, NULL AS CurrencyCode, NULL AS PriceListName,
    NULL AS RejectionReason, so.CreatedDate
FROM dbo.SalesOrders so
INNER JOIN dbo.Customers c ON so.CustomerID=c.CustomerID
ORDER BY so.SalesOrderID DESC";
                    var result = await connection.QueryAsync<SalesOrderListDto>(sql2);
                    return result.ToList();
                }
                catch
                {
                    return new List<SalesOrderListDto>();
                }
            }
        }

        public async Task<SalesOrderHeaderDto?> GetOrderByIdAsync(int salesOrderId)
        {
            using var connection = CreateConnection();
            var sql = @"
SELECT
    so.SalesOrderID,
    so.SONumber AS OrderNumber,
    so.SODate AS OrderDate,
    so.SOStatus AS OrderStatus,
    CASE WHEN so.QuotationID IS NOT NULL THEN 2 ELSE 1 END AS OrderSource,
    so.CustomerID, c.CustomerNameAr, c.CustomerCode,
    c.Phone1 AS CustomerPhone, c.CurrentBalance AS CustomerCurrentBalance, c.CreditLimit AS CustomerCreditLimit,
    so.QuotationID, sq.QuotationNumber AS LinkedQuotationNumber,
    so.RequiredDate AS ExpectedDeliveryDate,
    so.DeliveryDate,
    so.DeliveryAddress,
    so.PaymentTermID, pt.TermNameAr AS PaymentTermName,
    so.CurrencyID, cur.CurrencyCode, cur.Symbol AS CurrencySymbol, ISNULL(so.ExchangeRate,1) AS ExchangeRate,
    so.WarehouseID, w.WarehouseNameAr,
    so.PriceListID, pl.PriceListName,
    so.SalesRepID, ISNULL(sr.RepName, e.FullNameAr) AS SalesRepName,
    ISNULL(so.SubTotal,0) AS SubTotal, ISNULL(so.DiscountPercent,0) AS DiscountPercent, ISNULL(so.DiscountAmount,0) AS DiscountAmount,
    ISNULL(so.TaxAmount,0) AS TaxAmount, ISNULL(so.ShippingCost,0) AS ShippingCost, 0 AS OtherCosts, ISNULL(so.TotalAmount,0) AS TotalAmount,
    NULL AS SubmittedBy, NULL AS SubmittedDate,
    so.ApprovedBy, so.ApprovedDate, ap.FullNameAr AS ApprovedByName,
    NULL AS RejectedBy, NULL AS RejectedDate, NULL AS RejectionReason,
    so.Notes, so.Notes AS InternalNotes,
    so.CreatedBy, so.CreatedDate, so.ModifiedBy, so.ModifiedDate
FROM dbo.SalesOrders so
INNER JOIN dbo.Customers c ON so.CustomerID=c.CustomerID
LEFT JOIN dbo.SalesQuotations sq ON so.QuotationID=sq.QuotationID
LEFT JOIN dbo.PaymentTerms pt ON so.PaymentTermID=pt.PaymentTermID
LEFT JOIN dbo.Currencies cur ON so.CurrencyID=cur.CurrencyID
LEFT JOIN dbo.Warehouses w ON so.WarehouseID=w.WarehouseID
LEFT JOIN dbo.PriceLists pl ON so.PriceListID=pl.PriceListID
LEFT JOIN dbo.SalesRepresentatives sr ON so.SalesRepID=sr.SalesRepID
LEFT JOIN dbo.Employees e ON sr.EmployeeID=e.EmployeeID
LEFT JOIN dbo.Employees ap ON so.ApprovedBy=ap.EmployeeID
WHERE so.SalesOrderID=@ID";
            return await connection.QueryFirstOrDefaultAsync<SalesOrderHeaderDto>(sql, new { ID = salesOrderId });
        }

        public async Task<List<SalesOrderDetailDto>> GetOrderDetailsAsync(int salesOrderId)
        {
            using var connection = CreateConnection();
            var sql = @"
SELECT
    sod.SODetailID AS SODetailID,
    sod.SalesOrderID,
    sod.LineNumber,
    sod.ItemID,
    i.ItemCode,
    i.ItemNameAr,
    sod.UnitID,
    u.UnitNameAr AS UnitName,
    sod.OrderedQty,
    ISNULL(sod.DeliveredQty,0) AS DeliveredQty,
    ISNULL(sod.RemainingQty, sod.OrderedQty - ISNULL(sod.DeliveredQty,0)) AS RemainingQty,
    NULL AS QuotationDetailID,
    NULL AS QuotationQty,
    NULL AS QuotationRemaining,
    sod.UnitPrice,
    ISNULL(sod.DiscountPercent,0) AS DiscountPercent,
    ISNULL(sod.DiscountAmount,0) AS DiscountAmount,
    ISNULL(sod.TaxRate,0) AS TaxRate,
    ISNULL(sod.TaxAmount,0) AS TaxAmount,
    ISNULL(sod.LineTotal,0) AS LineTotal,
    ISNULL(sod.LineTotalWithTax, sod.LineTotal) AS LineTotalWithTax,
    NULL AS ExpectedDate,
    ISNULL(sod.LineStatus,1) AS LineStatus,
    sod.Notes,
    0 AS AvailableQty
FROM dbo.SalesOrderDetails sod
INNER JOIN dbo.Items i ON sod.ItemID=i.ItemID
INNER JOIN dbo.Units u ON sod.UnitID=u.UnitID
WHERE sod.SalesOrderID=@ID
ORDER BY sod.LineNumber";
            var result = await connection.QueryAsync<SalesOrderDetailDto>(sql, new { ID = salesOrderId });
            return result.ToList();
        }

        public async Task<string> GenerateOrderNumberAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50); EXEC sp_GetNextNumber N'SO', @NextNum OUTPUT; SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"SO-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"SO-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertOrderAsync(SalesOrderHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(header.OrderNumber))
                header.OrderNumber = await GenerateOrderNumberAsync();

            var sql = @"
INSERT INTO dbo.SalesOrders
(
    SONumber, SODate, CustomerID, QuotationID, SalesRepID, CurrencyID, ExchangeRate, PriceListID, PaymentTermID, WarehouseID,
    SubTotal, DiscountPercent, DiscountAmount, TaxAmount, ShippingCost, TotalAmount,
    SOStatus, RequiredDate, DeliveryDate, DeliveryAddress, Notes,
    CreatedBy, CreatedDate
)
VALUES
(
    @SONumber, @SODate, @CustomerID, NULLIF(@QuotationID,0), NULLIF(@SalesRepID,0), NULLIF(@CurrencyID,0), @ExchangeRate, NULLIF(@PriceListID,0), NULLIF(@PaymentTermID,0), NULLIF(@WarehouseID,0),
    @SubTotal, @DiscountPercent, @DiscountAmount, @TaxAmount, @ShippingCost, @TotalAmount,
    1, @RequiredDate, @DeliveryDate, @DeliveryAddress, @Notes,
    @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                SONumber = header.OrderNumber,
                SODate = header.OrderDate == default ? DateTime.Today : header.OrderDate,
                header.CustomerID,
                header.QuotationID,
                header.SalesRepID,
                header.CurrencyID,
                ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                header.PriceListID,
                header.PaymentTermID,
                header.WarehouseID,
                SubTotal = header.SubTotal,
                DiscountPercent = header.DiscountPercent,
                DiscountAmount = header.DiscountAmount,
                TaxAmount = header.TaxAmount,
                ShippingCost = header.ShippingCost,
                TotalAmount = header.TotalAmount,
                RequiredDate = header.ExpectedDeliveryDate,
                DeliveryDate = header.ExpectedDeliveryDate,
                DeliveryAddress = "",
                Notes = header.Notes ?? header.InternalNotes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "SalesOrders", newId.ToString(), moduleName: "SCR_SO", description: $"إنشاء أمر بيع: {header.OrderNumber}");
            return newId;
        }

        public async Task UpdateOrderAsync(SalesOrderHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>(@"SELECT SOStatus FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = header.SalesOrderID });
            if (status.HasValue && status.Value != 1) throw new Exception("لا يمكن تعديل أمر بيع ليس في حالة مسودة");

            var sql = @"
UPDATE dbo.SalesOrders SET
    SODate=@SODate,
    CustomerID=@CustomerID,
    QuotationID=NULLIF(@QuotationID,0),
    SalesRepID=NULLIF(@SalesRepID,0),
    CurrencyID=NULLIF(@CurrencyID,0),
    PriceListID=NULLIF(@PriceListID,0),
    PaymentTermID=NULLIF(@PaymentTermID,0),
    WarehouseID=NULLIF(@WarehouseID,0),
    ExchangeRate=@ExchangeRate,
    SubTotal=@SubTotal,
    DiscountPercent=@DiscountPercent,
    DiscountAmount=@DiscountAmount,
    TaxAmount=@TaxAmount,
    ShippingCost=@ShippingCost,
    TotalAmount=@TotalAmount,
    RequiredDate=@RequiredDate,
    DeliveryDate=@DeliveryDate,
    Notes=@Notes,
    ModifiedBy=@UserID,
    ModifiedDate=GETDATE()
WHERE SalesOrderID=@SalesOrderID";

            await connection.ExecuteAsync(sql, new
            {
                header.SalesOrderID,
                SODate = header.OrderDate,
                header.CustomerID,
                header.QuotationID,
                header.SalesRepID,
                header.CurrencyID,
                PriceListID = header.PriceListID,
                PaymentTermID = header.PaymentTermID,
                WarehouseID = header.WarehouseID,
                ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                SubTotal = header.SubTotal,
                DiscountPercent = header.DiscountPercent,
                DiscountAmount = header.DiscountAmount,
                TaxAmount = header.TaxAmount,
                ShippingCost = header.ShippingCost,
                TotalAmount = header.TotalAmount,
                RequiredDate = header.ExpectedDeliveryDate,
                DeliveryDate = header.ExpectedDeliveryDate,
                Notes = header.Notes ?? header.InternalNotes,
                UserID = userId
            });

            try { await connection.ExecuteAsync("EXEC dbo.sp_CalculateSOTotals @SalesOrderID", new { SalesOrderID = header.SalesOrderID }); } catch { }
            await _audit.WriteAuditLogAsync(userId, 2, "SalesOrders", header.SalesOrderID.ToString(), moduleName: "SCR_SO", description: $"تعديل أمر بيع: {header.OrderNumber}");
        }

        public async Task<int> InsertDetailAsync(SalesOrderDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(@"SELECT SOStatus FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = detail.SalesOrderID }, transaction);
                if (status != 1) throw new Exception("لا يمكن تعديل تفاصيل أمر بيع ليس في حالة مسودة");
                if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
                if (detail.UnitID <= 0) throw new Exception("اختر الوحدة");
                if (detail.OrderedQty <= 0) throw new Exception("أدخل الكمية");

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(@"SELECT ISNULL(MAX(LineNumber),0) FROM dbo.SalesOrderDetails WHERE SalesOrderID=@ID", new { ID = detail.SalesOrderID }, transaction);
                detail.LineNumber = maxLine + 1;

                // حساب المبالغ
                var discountAmt = detail.UnitPrice * detail.OrderedQty * detail.DiscountPercent / 100m;
                var lineTotal = detail.UnitPrice * detail.OrderedQty - discountAmt;
                var taxAmt = lineTotal * detail.TaxRate / 100m;
                var lineWithTax = lineTotal + taxAmt;
                var remainingQty = detail.OrderedQty;

                var sql = @"
INSERT INTO dbo.SalesOrderDetails
(SalesOrderID, LineNumber, ItemID, UnitID, OrderedQty, DeliveredQty, RemainingQty, UnitPrice, DiscountPercent, DiscountAmount, LineTotal, TaxRate, TaxAmount, LineTotalWithTax, LineStatus, Notes)
VALUES
(@SalesOrderID, @LineNumber, @ItemID, @UnitID, @OrderedQty, 0, @RemainingQty, @UnitPrice, @DiscountPercent, @DiscountAmount, @LineTotal, @TaxRate, @TaxAmount, @LineTotalWithTax, 1, @Notes);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    detail.SalesOrderID,
                    detail.LineNumber,
                    detail.ItemID,
                    detail.UnitID,
                    detail.OrderedQty,
                    RemainingQty = remainingQty,
                    detail.UnitPrice,
                    detail.DiscountPercent,
                    DiscountAmount = discountAmt,
                    LineTotal = lineTotal,
                    TaxRate = detail.TaxRate,
                    TaxAmount = taxAmt,
                    LineTotalWithTax = lineWithTax,
                    detail.Notes
                }, transaction);

                // تحديث إجماليات الأمر
                try
                {
                    var totals = await connection.QueryFirstOrDefaultAsync<(decimal SubTotal, decimal TaxAmount, decimal TotalAmount)>(@"
                        SELECT ISNULL(SUM(LineTotal),0) AS SubTotal, ISNULL(SUM(TaxAmount),0) AS TaxAmount, ISNULL(SUM(LineTotalWithTax),0) AS TotalAmount
                        FROM dbo.SalesOrderDetails WHERE SalesOrderID=@ID", new { ID = detail.SalesOrderID }, transaction);

                    await connection.ExecuteAsync(@"
                        UPDATE dbo.SalesOrders SET SubTotal=@SubTotal, TaxAmount=@TaxAmount, TotalAmount=@TotalAmount, ModifiedDate=GETDATE()
                        WHERE SalesOrderID=@ID", new { ID = detail.SalesOrderID, SubTotal = totals.SubTotal, TaxAmount = totals.TaxAmount, TotalAmount = totals.TotalAmount }, transaction);
                }
                catch { }

                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 1, "SalesOrderDetails", newId.ToString(), moduleName: "SCR_SO", description: $"إضافة سطر في أمر بيع رقم {detail.SalesOrderID}");
                return newId;
            }
            catch { transaction.Rollback(); throw; }
        }

        public async Task UpdateDetailAsync(SalesOrderDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(@"SELECT so.SOStatus FROM dbo.SalesOrders so INNER JOIN dbo.SalesOrderDetails sod ON so.SalesOrderID=sod.SalesOrderID WHERE sod.SODetailID=@ID", new { ID = detail.SODetailID }, transaction);
                if (status != 1) throw new Exception("لا يمكن تعديل تفاصيل أمر بيع ليس في حالة مسودة");

                var discountAmt = detail.UnitPrice * detail.OrderedQty * detail.DiscountPercent / 100m;
                var lineTotal = detail.UnitPrice * detail.OrderedQty - discountAmt;
                var taxAmt = lineTotal * detail.TaxRate / 100m;
                var lineWithTax = lineTotal + taxAmt;

                var sql = @"UPDATE dbo.SalesOrderDetails SET ItemID=@ItemID, UnitID=@UnitID, OrderedQty=@OrderedQty, RemainingQty=@OrderedQty, UnitPrice=@UnitPrice, DiscountPercent=@DiscountPercent, DiscountAmount=@DiscountAmount, LineTotal=@LineTotal, TaxRate=@TaxRate, TaxAmount=@TaxAmount, LineTotalWithTax=@LineTotalWithTax, Notes=@Notes WHERE SODetailID=@SODetailID";

                await connection.ExecuteAsync(sql, new
                {
                    detail.SODetailID,
                    detail.ItemID,
                    detail.UnitID,
                    detail.OrderedQty,
                    detail.UnitPrice,
                    detail.DiscountPercent,
                    DiscountAmount = discountAmt,
                    LineTotal = lineTotal,
                    TaxRate = detail.TaxRate,
                    TaxAmount = taxAmt,
                    LineTotalWithTax = lineWithTax,
                    detail.Notes
                }, transaction);

                try
                {
                    var soId = await connection.QueryFirstOrDefaultAsync<int>(@"SELECT SalesOrderID FROM dbo.SalesOrderDetails WHERE SODetailID=@ID", new { ID = detail.SODetailID }, transaction);
                    var totals = await connection.QueryFirstOrDefaultAsync<(decimal SubTotal, decimal TaxAmount, decimal TotalAmount)>(@"SELECT ISNULL(SUM(LineTotal),0) AS SubTotal, ISNULL(SUM(TaxAmount),0) AS TaxAmount, ISNULL(SUM(LineTotalWithTax),0) AS TotalAmount FROM dbo.SalesOrderDetails WHERE SalesOrderID=@ID", new { ID = soId }, transaction);
                    await connection.ExecuteAsync(@"UPDATE dbo.SalesOrders SET SubTotal=@SubTotal, TaxAmount=@TaxAmount, TotalAmount=@TotalAmount WHERE SalesOrderID=@ID", new { ID = soId, SubTotal = totals.SubTotal, TaxAmount = totals.TaxAmount, TotalAmount = totals.TotalAmount }, transaction);
                }
                catch { }

                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "SalesOrderDetails", detail.SODetailID.ToString(), moduleName: "SCR_SO", description: $"تعديل سطر في أمر بيع رقم {detail.SalesOrderID}");
            }
            catch { transaction.Rollback(); throw; }
        }

        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(int SalesOrderID, int SOStatus)>(@"SELECT sod.SalesOrderID, so.SOStatus FROM dbo.SalesOrderDetails sod INNER JOIN dbo.SalesOrders so ON sod.SalesOrderID=so.SalesOrderID WHERE sod.SODetailID=@ID", new { ID = detailId }, transaction);
                if (info.SalesOrderID == 0) throw new Exception("السطر غير موجود");
                if (info.SOStatus != 1) throw new Exception("لا يمكن حذف سطر من أمر بيع ليس في حالة مسودة");

                await connection.ExecuteAsync(@"DELETE FROM dbo.SalesOrderDetails WHERE SODetailID=@ID", new { ID = detailId }, transaction);

                try
                {
                    var totals = await connection.QueryFirstOrDefaultAsync<(decimal SubTotal, decimal TaxAmount, decimal TotalAmount)>(@"SELECT ISNULL(SUM(LineTotal),0) AS SubTotal, ISNULL(SUM(TaxAmount),0) AS TaxAmount, ISNULL(SUM(LineTotalWithTax),0) AS TotalAmount FROM dbo.SalesOrderDetails WHERE SalesOrderID=@ID", new { ID = info.SalesOrderID }, transaction);
                    await connection.ExecuteAsync(@"UPDATE dbo.SalesOrders SET SubTotal=@SubTotal, TaxAmount=@TaxAmount, TotalAmount=@TotalAmount WHERE SalesOrderID=@ID", new { ID = info.SalesOrderID, SubTotal = totals.SubTotal, TaxAmount = totals.TaxAmount, TotalAmount = totals.TotalAmount }, transaction);
                }
                catch { }

                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 3, "SalesOrderDetails", detailId.ToString(), moduleName: "SCR_SO", description: "حذف سطر من أمر بيع");
            }
            catch { transaction.Rollback(); throw; }
        }

        public async Task SubmitForApprovalAsync(int salesOrderId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(string SONumber, int SOStatus, int DetailCount)>(@"SELECT SONumber, SOStatus, (SELECT COUNT(*) FROM dbo.SalesOrderDetails d WHERE d.SalesOrderID=so.SalesOrderID) AS DetailCount FROM dbo.SalesOrders so WHERE so.SalesOrderID=@ID", new { ID = salesOrderId });
            if (string.IsNullOrWhiteSpace(info.SONumber)) throw new Exception("أمر البيع غير موجود");
            if (info.SOStatus != 1) throw new Exception("لا يمكن إرسال أمر بيع غير مسودة");
            if (info.DetailCount == 0) throw new Exception("لا يمكن إرسال أمر بيع بدون أصناف");

            await connection.ExecuteAsync(@"UPDATE dbo.SalesOrders SET SOStatus=2, ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE SalesOrderID=@ID", new { ID = salesOrderId, UserID = userId });

            try
            {
                var approverRoles = await connection.QueryAsync<int>(@"SELECT DISTINCT r.RoleID FROM dbo.UserRoles r INNER JOIN dbo.RolePermissions rp ON r.RoleID=rp.RoleID INNER JOIN dbo.SystemModules sm ON rp.ModuleID=sm.ModuleID WHERE sm.ModuleCode=N'SCR_SO' AND rp.CanApprove=1");
                foreach (var roleId in approverRoles)
                {
                    await _notif.CreateNotificationAsync(3, "أمر بيع بانتظار الاعتماد", $"أمر البيع رقم {info.SONumber} بانتظار الاعتماد", 1, null, roleId, "SCR_SO", salesOrderId, userId);
                }
            }
            catch { }
            await _audit.WriteAuditLogAsync(userId, 2, "SalesOrders", salesOrderId.ToString(), moduleName: "SCR_SO", description: $"إرسال أمر بيع للاعتماد: {info.SONumber}");
        }

        public async Task ApproveAsync(int salesOrderId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            string orderNumber = "";
            int? createdBy = null;
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string SONumber, int SOStatus, int? CreatedBy)>(@"SELECT SONumber, SOStatus, CreatedBy FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = salesOrderId }, transaction);
                if (string.IsNullOrWhiteSpace(info.SONumber)) throw new Exception("أمر البيع غير موجود");
                if (info.SOStatus != 2) throw new Exception("لا يمكن اعتماد أمر بيع ليس في انتظار الاعتماد");
                orderNumber = info.SONumber;
                createdBy = info.CreatedBy;

                await connection.ExecuteAsync(@"UPDATE dbo.SalesOrders SET SOStatus=3, ApprovedBy=@EmployeeID, ApprovedDate=GETDATE(), ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE SalesOrderID=@ID", new { ID = salesOrderId, EmployeeID = employeeId, UserID = userId }, transaction);
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }

            try
            {
                if (createdBy.HasValue) await _notif.CreateNotificationAsync(3, "تم اعتماد أمر البيع", $"تم اعتماد أمر البيع رقم {orderNumber}", 2, createdBy.Value, null, "SCR_SO", salesOrderId, userId);
                await _notif.MarkRelatedAsActionedAsync("SCR_SO", salesOrderId);
            }
            catch { }
            await _audit.WriteAuditLogAsync(userId, 2, "SalesOrders", salesOrderId.ToString(), moduleName: "SCR_SO", description: $"اعتماد أمر بيع: {orderNumber}");
        }

        public async Task RejectAsync(int salesOrderId, string rejectionReason, int userId, int? employeeId = null)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason)) throw new Exception("أدخل سبب الرفض");
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(string SONumber, int SOStatus, int? CreatedBy)>(@"SELECT SONumber, SOStatus, CreatedBy FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = salesOrderId });
            if (string.IsNullOrWhiteSpace(info.SONumber)) throw new Exception("أمر البيع غير موجود");
            if (info.SOStatus != 2) throw new Exception("لا يمكن رفض أمر بيع ليس في انتظار الاعتماد");

            await connection.ExecuteAsync(@"UPDATE dbo.SalesOrders SET SOStatus=1, ModifiedBy=@UserID, ModifiedDate=GETDATE(), Notes=ISNULL(Notes,'') + @Reason WHERE SalesOrderID=@ID", new { ID = salesOrderId, Reason = " - رفض: " + rejectionReason, UserID = userId });

            try
            {
                if (info.CreatedBy.HasValue) await _notif.CreateNotificationAsync(3, "تم رفض أمر البيع", $"تم رفض أمر البيع رقم {info.SONumber}. السبب: {rejectionReason}", 1, info.CreatedBy.Value, null, "SCR_SO", salesOrderId, userId);
                await _notif.MarkRelatedAsActionedAsync("SCR_SO", salesOrderId);
            }
            catch { }
            await _audit.WriteAuditLogAsync(userId, 2, "SalesOrders", salesOrderId.ToString(), moduleName: "SCR_SO", description: $"رفض أمر بيع: {info.SONumber} - {rejectionReason}");
        }

        public async Task CancelAsync(int salesOrderId, int userId)
        {
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(string SONumber, int SOStatus)>(@"SELECT SONumber, SOStatus FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = salesOrderId });
            if (string.IsNullOrWhiteSpace(info.SONumber)) throw new Exception("أمر البيع غير موجود");
            if (info.SOStatus == 7) throw new Exception("أمر البيع ملغي بالفعل");

            await connection.ExecuteAsync(@"UPDATE dbo.SalesOrders SET SOStatus=7, ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE SalesOrderID=@ID", new { ID = salesOrderId, UserID = userId });
            await _audit.WriteAuditLogAsync(userId, 2, "SalesOrders", salesOrderId.ToString(), moduleName: "SCR_SO", description: $"إلغاء أمر بيع: {info.SONumber}");
        }

        public async Task<(bool Success, string Message)> DeleteOrderAsync(int salesOrderId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string SONumber, int SOStatus)>(@"SELECT SONumber, SOStatus FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = salesOrderId }, transaction);
                if (string.IsNullOrWhiteSpace(info.SONumber)) return (false, "أمر البيع غير موجود");
                if (info.SOStatus != 1) return (false, "لا يمكن حذف أمر بيع غير مسودة");

                await connection.ExecuteAsync(@"DELETE FROM dbo.SalesOrderDetails WHERE SalesOrderID=@ID", new { ID = salesOrderId }, transaction);
                await connection.ExecuteAsync(@"DELETE FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = salesOrderId }, transaction);
                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 3, "SalesOrders", salesOrderId.ToString(), moduleName: "SCR_SO", description: $"حذف أمر بيع: {info.SONumber}");
                return (true, "تم حذف أمر البيع بنجاح");
            }
            catch (Exception ex) { transaction.Rollback(); return (false, ex.Message); }
        }

        public async Task<byte[]> ExportToExcelAsync(List<SalesOrderListDto> orders, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("أوامر البيع");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Tajawal";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير أوامر البيع - واي كي كوتينج Gold Edition";
            ws.Range(1, 1, 1, 10).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 10).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[] { "#", "رقم الأمر", "التاريخ", "العميل", "عرض السعر", "المندوب", "الحالة", "الإجمالي", "التسليم المتوقع", "الأصناف" };
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
            foreach (var so in orders)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = so.OrderNumber ?? "";
                ws.Cell(row, 3).Value = so.OrderDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = so.CustomerNameAr ?? "";
                ws.Cell(row, 5).Value = so.LinkedQuotationNumber ?? "—";
                ws.Cell(row, 6).Value = so.SalesRepName ?? "—";
                ws.Cell(row, 7).Value = GetStatusName(so.OrderStatus);
                ws.Cell(row, 8).Value = so.TotalAmount;
                ws.Cell(row, 9).Value = so.ExpectedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—";
                ws.Cell(row, 10).Value = so.ItemCount;
                if (num % 2 == 0) ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");
                row++;
            }
            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, "SalesOrders", moduleName: "SCR_SO", description: $"تصدير {orders.Count} أمر بيع إلى Excel");
            return stream.ToArray();
        }

        public async Task<string> GenerateReportHtmlAsync(int salesOrderId, int userId)
        {
            var header = await GetOrderByIdAsync(salesOrderId);
            if (header == null) return "<h3>أمر البيع غير موجود</h3>";
            var details = await GetOrderDetailsAsync(salesOrderId);

            var detailsHtml = "";
            int n = 0;
            foreach (var d in details)
            {
                n++;
                detailsHtml += $@"<tr>
                    <td style='text-align:center'>{n}</td>
                    <td>{d.ItemCode}</td><td>{d.ItemNameAr}</td><td>{d.UnitName}</td>
                    <td style='text-align:center'>{d.OrderedQty:#,##0.##}</td>
                    <td style='text-align:left'>{d.UnitPrice:#,##0.00}</td>
                    <td style='text-align:center'>{d.DiscountPercent:#,##0.##}%</td>
                    <td style='text-align:center'>{d.TaxRate:#,##0.##}%</td>
                    <td style='text-align:left'>{d.LineTotalWithTax:#,##0.00}</td></tr>";
            }

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'>
<title>أمر بيع - {header.OrderNumber}</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Tajawal','Segoe UI',sans-serif;padding:30px;color:#070B14;font-size:13px;background:#fff}}
.header{{text-align:center;border-bottom:3px solid #070B14;padding-bottom:20px;margin-bottom:24px;position:relative}}
.header::after{{content:'';position:absolute;bottom:-3px;left:0;right:0;height:3px;background:linear-gradient(90deg,transparent,#D4AF37,#070B14,#D4AF37,transparent)}}
.company{{font-size:20px;font-weight:900;color:#070B14}}
.doc-title{{font-size:15px;color:#6b7280;margin-top:4px}}
.doc-num{{display:inline-block;background:rgba(212,175,55,.1);border:1px solid rgba(212,175,55,.2);padding:4px 16px;border-radius:8px;font-weight:800;color:#9C7C2E;margin-top:8px}}
.info-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:20px}}
.info-item{{background:#fcfaf6;padding:10px;border-radius:8px;border:1px solid #e8e2d0}}
.info-label{{font-size:10px;color:#6b7280;font-weight:600}}
.info-value{{font-size:13px;font-weight:700;margin-top:2px}}
table{{width:100%;border-collapse:collapse;margin-bottom:16px}}
th{{background:#070B14;color:#D4AF37;padding:10px;font-size:12px;text-align:right}}
td{{padding:9px 10px;border-bottom:1px solid #e8e2d0;font-size:12px}}
tr:nth-child(even){{background:#fcfaf6}}
.totals-section{{margin-top:16px;display:flex;justify-content:flex-end}}
.totals-box{{background:#fcfaf6;border:1px solid #e8e2d0;border-radius:10px;padding:16px;min-width:280px;position:relative;overflow:hidden}}
.totals-box::before{{content:'';position:absolute;top:0;left:0;right:0;height:2px;background:linear-gradient(90deg,transparent,#D4AF37,transparent)}}
.total-line{{display:flex;justify-content:space-between;padding:4px 0;font-size:12px}}
.total-line.grand{{font-weight:800;font-size:14px;border-top:2px solid #070B14;margin-top:8px;padding-top:8px}}
.footer{{text-align:center;margin-top:30px;padding-top:12px;border-top:1px solid #e8e2d0;font-size:10px;color:#9ca3af}}
</style></head><body>
<div class='header'>
    <div class='company'>واي كي كوتينج لمستحضرات التجميل - YK Coatings</div>
    <div class='doc-title'>أمر بيع</div>
    <div class='doc-num'>{header.OrderNumber}</div>
</div>
<div class='info-grid'>
    <div class='info-item'><div class='info-label'>تاريخ الأمر</div><div class='info-value'>{header.OrderDate:dd/MM/yyyy}</div></div>
    <div class='info-item'><div class='info-label'>العميل</div><div class='info-value'>{header.CustomerNameAr}</div></div>
    <div class='info-item'><div class='info-label'>عرض السعر</div><div class='info-value'>{header.LinkedQuotationNumber ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(header.OrderStatus)}</div></div>
    <div class='info-item'><div class='info-label'>المندوب</div><div class='info-value'>{header.SalesRepName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>المخزن</div><div class='info-value'>{header.WarehouseNameAr ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>شروط الدفع</div><div class='info-value'>{header.PaymentTermName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>التسليم المتوقع</div><div class='info-value'>{header.ExpectedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—"}</div></div>
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
        <div class='total-line grand'><span>الإجمالي الكلي</span><span>{header.TotalAmount:#,##0.00} {(header.CurrencySymbol ?? "ج.م")}</span></div>
    </div>
</div>
{(string.IsNullOrWhiteSpace(header.Notes) ? "" : $"<div style='margin-top:16px'><strong>ملاحظات:</strong> {header.Notes}</div>")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} - واي كي كوتينج ERP Gold Edition</div>
</body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "SalesOrders", salesOrderId.ToString(), moduleName: "SCR_SO", description: $"طباعة أمر بيع: {header.OrderNumber}");
            return html;
        }

        public async Task<ItemAuditDto?> GetOrderAuditAsync(int salesOrderId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ISNULL(uc.FullName, ISNULL(uc.Username, N'غير محدد')) AS CreatedByName, so.CreatedDate,
                               ISNULL(um.FullName, ISNULL(um.Username, N'')) AS ModifiedByName, so.ModifiedDate
                        FROM dbo.SalesOrders so
                        LEFT JOIN dbo.SystemUsers uc ON so.CreatedBy=uc.UserID
                        LEFT JOIN dbo.SystemUsers um ON so.ModifiedBy=um.UserID
                        WHERE so.SalesOrderID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ItemAuditDto>(sql, new { ID = salesOrderId });
        }

        public static string GetStatusName(int status) => status switch
        {
            1 => "مسودة", 2 => "بانتظار الاعتماد", 3 => "معتمد", 4 => "مرفوض", 5 => "قيد التجهيز", 6 => "تم التسليم جزئياً", 7 => "ملغي", 8 => "مغلق", _ => "غير محدد"
        };

        public static string GetStatusColor(int status) => status switch
        {
            1 => "#6b7280", 2 => "#f59e0b", 3 => "#10b981", 4 => "#ef4444", 5 => "#3b82f6", 6 => "#6366f1", 7 => "#dc2626", 8 => "#0f172a", _ => "#6b7280"
        };

        public static string GetSourceName(int source) => source switch
        {
            1 => "يدوي", 2 => "من عرض سعر", _ => "يدوي"
        };

        public static string GetSourceColor(int source) => source switch
        {
            1 => "#1d4ed8", 2 => "#0891b2", _ => "#1d4ed8"
        };

        // ==========================================
        // عروض الأسعار المعتمدة - للاختيار في أمر البيع
        // ==========================================
        public async Task<List<SearchableItem>> GetApprovedQuotationsSearchableAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT QuotationID AS Id, QuotationNumber + N' - ' + c.CustomerNameAr AS Name, QuotationNumber AS Code
                            FROM dbo.SalesQuotations sq
                            INNER JOIN dbo.Customers c ON sq.CustomerID=c.CustomerID
                            WHERE sq.QuotationStatus IN (3,5)
                            ORDER BY sq.QuotationDate DESC";
                var result = await connection.QueryAsync<SearchableItem>(sql);
                return result.ToList();
            }
            catch
            {
                try
                {
                    var sql2 = @"SELECT QuotationID AS Id, CAST(QuotationID AS NVARCHAR) AS Name, CAST(QuotationID AS NVARCHAR) AS Code FROM dbo.SalesQuotations ORDER BY QuotationID DESC";
                    var result = await connection.QueryAsync<SearchableItem>(sql2);
                    return result.ToList();
                }
                catch { return new List<SearchableItem>(); }
            }
        }

        public async Task<List<QuotationLineForOrderDto>> GetQuotationLinesForOrderAsync(int quotationId)
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT
                                sqd.QuotationDetailID,
                                sqd.QuotationID,
                                sq.QuotationNumber,
                                sqd.LineNumber,
                                sqd.ItemID,
                                i.ItemCode,
                                i.ItemNameAr,
                                sqd.UnitID,
                                u.UnitNameAr AS UnitName,
                                ISNULL(sqd.Quantity,0) AS QuotedQty,
                                0 AS TotalOrderedQty,
                                ISNULL(sqd.Quantity,0) AS RemainingToOrder,
                                ISNULL(sqd.UnitPrice,0) AS UnitPrice,
                                0 AS CurrentStock
                            FROM dbo.SalesQuotationDetails sqd
                            INNER JOIN dbo.SalesQuotations sq ON sqd.QuotationID=sq.QuotationID
                            INNER JOIN dbo.Items i ON sqd.ItemID=i.ItemID
                            INNER JOIN dbo.Units u ON sqd.UnitID=u.UnitID
                            WHERE sqd.QuotationID=@ID
                            ORDER BY sqd.LineNumber";
                var result = await connection.QueryAsync<QuotationLineForOrderDto>(sql, new { ID = quotationId });
                return result.ToList();
            }
            catch
            {
                return new List<QuotationLineForOrderDto>();
            }
        }

        public async Task<int> ImportLinesFromQuotationAsync(int salesOrderId, int quotationId, List<int> quotationDetailIds, int userId)
        {
            if (quotationDetailIds == null || !quotationDetailIds.Any()) return 0;
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var soStatus = await connection.QueryFirstOrDefaultAsync<int?>(@"SELECT SOStatus FROM dbo.SalesOrders WHERE SalesOrderID=@ID", new { ID = salesOrderId }, transaction);
                if (soStatus != 1) throw new Exception("لا يمكن تحميل سطور على أمر بيع ليس في حالة مسودة");

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(@"SELECT ISNULL(MAX(LineNumber),0) FROM dbo.SalesOrderDetails WHERE SalesOrderID=@ID", new { ID = salesOrderId }, transaction);

                var lines = await connection.QueryAsync(@"SELECT QuotationDetailID, ItemID, UnitID, Quantity, UnitPrice FROM dbo.SalesQuotationDetails WHERE QuotationID=@QID AND QuotationDetailID IN @IDs", new { QID = quotationId, IDs = quotationDetailIds }, transaction);

                int inserted = 0;
                foreach (var line in lines)
                {
                    maxLine++;
                    var discountAmt = 0m;
                    var lineTotal = line.Quantity * line.UnitPrice;
                    var taxAmt = lineTotal * 0.14m;
                    var lineWithTax = lineTotal + taxAmt;

                    await connection.ExecuteAsync(@"
INSERT INTO dbo.SalesOrderDetails (SalesOrderID, LineNumber, ItemID, UnitID, OrderedQty, RemainingQty, UnitPrice, DiscountPercent, DiscountAmount, LineTotal, TaxRate, TaxAmount, LineTotalWithTax, LineStatus)
VALUES (@SalesOrderID, @LineNumber, @ItemID, @UnitID, @OrderedQty, @OrderedQty, @UnitPrice, 0, @DiscountAmount, @LineTotal, 14, @TaxAmount, @LineTotalWithTax, 1)",
                        new { SalesOrderID = salesOrderId, LineNumber = maxLine, ItemID = line.ItemID, UnitID = line.UnitID, OrderedQty = line.Quantity, UnitPrice = line.UnitPrice, DiscountAmount = discountAmt, LineTotal = lineTotal, TaxAmount = taxAmt, LineTotalWithTax = lineWithTax }, transaction);
                    inserted++;
                }

                // تحديث الإجماليات
                var totals = await connection.QueryFirstOrDefaultAsync<(decimal SubTotal, decimal TaxAmount, decimal TotalAmount)>(@"SELECT ISNULL(SUM(LineTotal),0) AS SubTotal, ISNULL(SUM(TaxAmount),0) AS TaxAmount, ISNULL(SUM(LineTotalWithTax),0) AS TotalAmount FROM dbo.SalesOrderDetails WHERE SalesOrderID=@ID", new { ID = salesOrderId }, transaction);
                await connection.ExecuteAsync(@"UPDATE dbo.SalesOrders SET SubTotal=@SubTotal, TaxAmount=@TaxAmount, TotalAmount=@TotalAmount WHERE SalesOrderID=@ID", new { ID = salesOrderId, SubTotal = totals.SubTotal, TaxAmount = totals.TaxAmount, TotalAmount = totals.TotalAmount }, transaction);

                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 1, "SalesOrderDetails", salesOrderId.ToString(), moduleName: "SCR_SO", description: $"تحميل {inserted} سطر من عرض سعر {quotationId} إلى أمر بيع");
                return inserted;
            }
            catch { transaction.Rollback(); throw; }
        }
    }
}
