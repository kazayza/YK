using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;

namespace YKCoatings.Services
{
    public class SalesInvoiceService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public SalesInvoiceService(IConfiguration configuration, AuditService audit, NotificationService notif) : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        // ==========================================
        // قائمة الفواتير
        // ==========================================
        public async Task<List<SalesInvoiceListDto>> GetInvoicesListAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT
                            InvoiceID, InvoiceNumber, InvoiceDate,
                            InvoiceStatus, InvoiceSource, InvoiceSourceName,
                            PaymentStatus, PaymentStatusName,
                            CustomerID, CustomerCode, CustomerNameAr,
                            SalesOrderID, LinkedSONumber,
                            DeliveryID, LinkedDeliveryNumber,
                            IsTaxable, TaxRate,
                            SubTotal, DiscountAmount, TaxAmount, TotalAmount, PaidAmount, RemainingAmount,
                            DueDate, IsOverdue, DaysOverdue,
                            PaymentTermName, CurrencyCode,
                            ItemCount, RejectionReason, CreatedDate
                        FROM dbo.vw_SalesInvoiceSummary
                        ORDER BY InvoiceDate DESC, InvoiceID DESC";
                var result = await connection.QueryAsync<SalesInvoiceListDto>(sql);
                return result.ToList();
            }
            catch
            {
                // Fallback إذا الـ View غير موجود — جدول مباشر — ملتزم بالجداول الموجودة فقط (لا DeliveryNotes)
                var sql2 = @"SELECT
                            si.InvoiceID, si.InvoiceNumber, si.InvoiceDate,
                            si.InvoiceStatus, ISNULL(si.InvoiceSource,3) as InvoiceSource,
                            CASE WHEN ISNULL(si.InvoiceSource,3)=2 THEN N'من أمر بيع' ELSE N'فاتورة مباشرة' END as InvoiceSourceName,
                            si.PaymentStatus,
                            CASE si.PaymentStatus WHEN 1 THEN N'غير مدفوعة' WHEN 2 THEN N'مدفوعة جزئياً' WHEN 3 THEN N'مدفوعة بالكامل' ELSE N'غير محدد' END as PaymentStatusName,
                            si.CustomerID, c.CustomerCode, c.CustomerNameAr,
                            si.SalesOrderID, so.OrderNumber as LinkedSONumber,
                            CAST(NULL AS INT) as DeliveryID, CAST(NULL AS NVARCHAR(50)) as LinkedDeliveryNumber,
                            ISNULL(si.IsTaxable,1) as IsTaxable, ISNULL(si.TaxRate,14) as TaxRate,
                            ISNULL(si.SubTotal,0) as SubTotal, ISNULL(si.DiscountAmount,0) as DiscountAmount, ISNULL(si.TaxAmount,0) as TaxAmount, ISNULL(si.TotalAmount,0) as TotalAmount, ISNULL(si.PaidAmount,0) as PaidAmount, ISNULL(si.TotalAmount,0)-ISNULL(si.PaidAmount,0) as RemainingAmount,
                            si.DueDate, 0 as IsOverdue, 0 as DaysOverdue,
                            pt.TermNameAr as PaymentTermName, cur.CurrencyCode,
                            ISNULL((SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.InvoiceID=si.InvoiceID),0) as ItemCount,
                            si.RejectionReason, si.CreatedDate
                        FROM dbo.SalesInvoices si
                        INNER JOIN dbo.Customers c ON si.CustomerID=c.CustomerID
                        LEFT JOIN dbo.SalesOrders so ON si.SalesOrderID=so.SalesOrderID
                        LEFT JOIN dbo.PaymentTerms pt ON si.PaymentTermID=pt.PaymentTermID
                        LEFT JOIN dbo.Currencies cur ON si.CurrencyID=cur.CurrencyID
                        ORDER BY si.InvoiceDate DESC, si.InvoiceID DESC";
                var result = await connection.QueryAsync<SalesInvoiceListDto>(sql2);
                return result.ToList();
            }
        }

        // ==========================================
        // جلب فاتورة واحدة
        // ==========================================
        public async Task<SalesInvoiceHeaderDto?> GetInvoiceByIdAsync(int invoiceId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            si.InvoiceID, si.InvoiceNumber, si.InvoiceDate,
                            si.InvoiceStatus, ISNULL(si.InvoiceSource,3) AS InvoiceSource,
                            si.PaymentStatus,
                            si.CustomerID, c.CustomerNameAr, c.CustomerCode,
                            si.SalesOrderID, so.OrderNumber AS LinkedSONumber,
                            CAST(NULL AS INT) AS DeliveryID, CAST(NULL AS NVARCHAR(50)) AS LinkedDeliveryNumber,
                            ISNULL(si.IsTaxable,1) AS IsTaxable, ISNULL(si.TaxRate,14) AS TaxRate,
                            si.PaymentTermID, pt.TermNameAr AS PaymentTermName,
                            si.DueDate,
                            si.CurrencyID, cur.CurrencyCode, cur.Symbol AS CurrencySymbol,
                            ISNULL(si.ExchangeRate,1) AS ExchangeRate,
                            si.WarehouseID, w.WarehouseNameAr,
                            ISNULL(si.SubTotal,0) AS SubTotal, ISNULL(si.DiscountPercent,0) AS DiscountPercent, ISNULL(si.DiscountAmount,0) AS DiscountAmount,
                            ISNULL(si.TaxableAmount,0) AS TaxableAmount, ISNULL(si.TaxAmount,0) AS TaxAmount,
                            ISNULL(si.ShippingCost,0) AS ShippingCost, ISNULL(si.OtherCosts,0) AS OtherCosts,
                            ISNULL(si.TotalAmount,0) AS TotalAmount, ISNULL(si.PaidAmount,0) AS PaidAmount,
                            si.SubmittedBy, si.SubmittedDate,
                            si.ApprovedBy, si.ApprovedDate,
                            app.FullNameAr AS ApprovedByName,
                            si.RejectedBy, si.RejectedDate, si.RejectionReason,
                            si.Notes,
                            si.CreatedBy, si.CreatedDate, si.ModifiedDate
                        FROM dbo.SalesInvoices si
                        INNER JOIN dbo.Customers c ON si.CustomerID = c.CustomerID
                        LEFT JOIN dbo.SalesOrders so ON si.SalesOrderID = so.SalesOrderID
                        LEFT JOIN dbo.PaymentTerms pt ON si.PaymentTermID = pt.PaymentTermID
                        LEFT JOIN dbo.Currencies cur ON si.CurrencyID = cur.CurrencyID
                        LEFT JOIN dbo.Warehouses w ON si.WarehouseID = w.WarehouseID
                        LEFT JOIN dbo.Employees app ON si.ApprovedBy = app.EmployeeID
                        WHERE si.InvoiceID = @ID";
            return await connection.QueryFirstOrDefaultAsync<SalesInvoiceHeaderDto>(sql, new { ID = invoiceId });
        }

        // ==========================================
        // تفاصيل الفاتورة
        // ==========================================
        public async Task<List<SalesInvoiceDetailDto>> GetInvoiceDetailsAsync(int invoiceId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            sid.InvoiceDetailID, sid.InvoiceID, sid.LineNumber,
                            sid.ItemID, i.ItemCode, i.ItemNameAr,
                            sid.UnitID, u.UnitNameAr AS UnitName,
                            sid.WarehouseID, w.WarehouseNameAr,
                            sid.Quantity, sid.UnitPrice,
                            ISNULL(sid.DiscountPercent,0) AS DiscountPercent, ISNULL(sid.DiscountAmount,0) AS DiscountAmount,
                            ISNULL(sid.LineTotal,0) AS LineTotal, ISNULL(sid.TaxRate,0) AS TaxRate, ISNULL(sid.TaxAmount,0) AS TaxAmount, ISNULL(sid.LineTotalWithTax, sid.LineTotal) AS LineTotalWithTax,
                            sid.BatchNumber, sid.ExpiryDate,
                            sid.DeliveryDetailID, sid.SalesOrderDetailID,
                            sid.Notes,
                            ISNULL((SELECT SUM(Quantity) FROM dbo.StockBalances WHERE ItemID=sid.ItemID AND WarehouseID=sid.WarehouseID),0) AS AvailableQty
                        FROM dbo.SalesInvoiceDetails sid
                        INNER JOIN dbo.Items i ON sid.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON sid.UnitID = u.UnitID
                        LEFT JOIN dbo.Warehouses w ON sid.WarehouseID = w.WarehouseID
                        WHERE sid.InvoiceID = @ID
                        ORDER BY sid.LineNumber";
            var result = await connection.QueryAsync<SalesInvoiceDetailDto>(sql, new { ID = invoiceId });
            return result.ToList();
        }

        // ==========================================
        // توليد رقم الفاتورة
        // ==========================================
        public async Task<string> GenerateInvoiceNumberAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                            EXEC sp_GetNextNumber N'SI', @NextNum OUTPUT;
                            SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"SI-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"SI-{DateTime.Now:yyMMddHHmmss}"; }
        }

        // ==========================================
        // إضافة فاتورة جديدة — مباشرة أو من أمر/تسليم
        // ==========================================
        public async Task<int> InsertInvoiceAsync(SalesInvoiceHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.SalesInvoices
                        (
                            InvoiceNumber, InvoiceDate, CustomerID,
                            SalesOrderID, DeliveryID,
                            IsTaxable, TaxRate,
                            PaymentTermID, CurrencyID, ExchangeRate, WarehouseID,
                            DiscountPercent, ShippingCost, OtherCosts,
                            InvoiceStatus, InvoiceSource, PaymentStatus,
                            Notes, CreatedBy, CreatedDate
                        )
                        VALUES
                        (
                            @InvoiceNumber, @InvoiceDate, @CustomerID,
                            NULLIF(@SalesOrderID, 0), NULL,
                            @IsTaxable, @TaxRate,
                            NULLIF(@PaymentTermID, 0), NULLIF(@CurrencyID, 0), @ExchangeRate, NULLIF(@WarehouseID,0),
                            @DiscountPercent, @ShippingCost, @OtherCosts,
                            1, @InvoiceSource, 1,
                            @Notes, @UserID, GETDATE()
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
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
                InvoiceSource = header.InvoiceSource == 0 ? (header.SalesOrderID.HasValue && header.SalesOrderID > 0 ? 2 : 3) : (header.InvoiceSource==1?3:header.InvoiceSource),
                header.Notes,
                UserID = userId
            });

            try { await connection.ExecuteAsync("EXEC dbo.sp_CalcSalesInvoiceDueDate @InvoiceID", new { InvoiceID = newId }); } catch { }

            await _audit.WriteAuditLogAsync(userId, 1, "SalesInvoices", newId.ToString(),
                moduleName: "SCR_SINV",
                description: $"إنشاء فاتورة مبيعات: {header.InvoiceNumber} {(header.IsTaxable ? "مع ضريبة" : "بدون ضريبة")}");

            return newId;
        }

        // ==========================================
        // تحديث فاتورة (مسودة فقط)
        // ==========================================
        public async Task UpdateInvoiceAsync(SalesInvoiceHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT InvoiceStatus FROM dbo.SalesInvoices WHERE InvoiceID = @ID",
                new { ID = header.InvoiceID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل فاتورة ليست في حالة مسودة");

            var sql = @"UPDATE dbo.SalesInvoices SET
                            InvoiceDate = @InvoiceDate,
                            CustomerID = @CustomerID,
                            IsTaxable = @IsTaxable,
                            TaxRate = @TaxRate,
                            PaymentTermID = NULLIF(@PaymentTermID, 0),
                            CurrencyID = NULLIF(@CurrencyID, 0),
                            ExchangeRate = @ExchangeRate,
                            WarehouseID = NULLIF(@WarehouseID,0),
                            DiscountPercent = @DiscountPercent,
                            ShippingCost = @ShippingCost,
                            OtherCosts = @OtherCosts,
                            Notes = @Notes,
                            ModifiedBy = @UserID,
                            ModifiedDate = GETDATE()
                        WHERE InvoiceID = @InvoiceID";

            await connection.ExecuteAsync(sql, new
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
                header.Notes,
                UserID = userId
            });

            try
            {
                await connection.ExecuteAsync("EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID", new { InvoiceID = header.InvoiceID });
                await connection.ExecuteAsync("EXEC dbo.sp_CalcSalesInvoiceDueDate @InvoiceID", new { InvoiceID = header.InvoiceID });
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", header.InvoiceID.ToString(),
                moduleName: "SCR_SINV",
                description: $"تعديل فاتورة مبيعات: {header.InvoiceNumber}");
        }

        // ==========================================
        // إضافة سطر — مع التحقق من الرصيد
        // ==========================================
        public async Task<int> InsertDetailAsync(SalesInvoiceDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT InvoiceStatus FROM dbo.SalesInvoices WHERE InvoiceID = @ID",
                new { ID = detail.InvoiceID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل فاتورة ليست في حالة مسودة");

            if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
            if (detail.UnitID <= 0) throw new Exception("اختر الوحدة");
            if (detail.Quantity <= 0) throw new Exception("أدخل الكمية");

            var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.SalesInvoiceDetails WHERE InvoiceID = @ID",
                new { ID = detail.InvoiceID });

            detail.LineNumber = maxLine + 1;

            // حساب المبالغ حسب IsTaxable
            var discountAmt = detail.UnitPrice * detail.Quantity * detail.DiscountPercent / 100;
            var lineTotal = detail.UnitPrice * detail.Quantity - discountAmt;
            var taxAmt = detail.IsTaxable ? lineTotal * detail.TaxRate / 100 : 0;
            var lineWithTax = lineTotal + taxAmt;

            var sql = @"INSERT INTO dbo.SalesInvoiceDetails
                        (
                            InvoiceID, LineNumber, ItemID, UnitID, WarehouseID,
                            Quantity, UnitPrice, DiscountPercent, DiscountAmount,
                            LineTotal, TaxRate, TaxAmount, LineTotalWithTax,
                            BatchNumber, ExpiryDate,
                            DeliveryDetailID, SalesOrderDetailID, Notes
                        )
                        VALUES
                        (
                            @InvoiceID, @LineNumber, @ItemID, @UnitID, NULLIF(@WarehouseID, 0),
                            @Quantity, @UnitPrice, @DiscountPercent, @DiscountAmount,
                            @LineTotal, @TaxRate, @TaxAmount, @LineTotalWithTax,
                            @BatchNumber, @ExpiryDate,
                            @DeliveryDetailID, @SalesOrderDetailID, @Notes
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                detail.InvoiceID,
                detail.LineNumber,
                detail.ItemID,
                detail.UnitID,
                detail.WarehouseID,
                detail.Quantity,
                detail.UnitPrice,
                detail.DiscountPercent,
                DiscountAmount = discountAmt,
                LineTotal = lineTotal,
                TaxRate = detail.IsTaxable ? detail.TaxRate : 0,
                TaxAmount = taxAmt,
                LineTotalWithTax = lineWithTax,
                detail.BatchNumber,
                detail.ExpiryDate,
                detail.DeliveryDetailID,
                detail.SalesOrderDetailID,
                detail.Notes
            });

            try { await connection.ExecuteAsync("EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID", new { InvoiceID = detail.InvoiceID }); } catch { }

            await _audit.WriteAuditLogAsync(userId, 1, "SalesInvoiceDetails", newId.ToString(),
                moduleName: "SCR_SINV",
                description: $"إضافة سطر في فاتورة مبيعات رقم {detail.InvoiceID}");

            return newId;
        }

        // ==========================================
        // حذف سطر
        // ==========================================
        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(int InvoiceID, int InvoiceStatus)>(
                @"SELECT sid.InvoiceID, si.InvoiceStatus
                  FROM dbo.SalesInvoiceDetails sid
                  INNER JOIN dbo.SalesInvoices si ON sid.InvoiceID = si.InvoiceID
                  WHERE sid.InvoiceDetailID = @ID",
                new { ID = detailId });

            if (info.InvoiceID == 0) throw new Exception("السطر غير موجود");
            if (info.InvoiceStatus != 1) throw new Exception("لا يمكن حذف سطر من فاتورة ليست مسودة");

            await connection.ExecuteAsync(
                "DELETE FROM dbo.SalesInvoiceDetails WHERE InvoiceDetailID = @ID",
                new { ID = detailId });

            try { await connection.ExecuteAsync("EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID", new { InvoiceID = info.InvoiceID }); } catch { }

            await _audit.WriteAuditLogAsync(userId, 3, "SalesInvoiceDetails", detailId.ToString(),
                moduleName: "SCR_SINV", description: "حذف سطر من فاتورة مبيعات");
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
            var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, int DetailCount)>(
                @"SELECT si.InvoiceNumber, si.InvoiceStatus,
                    (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.InvoiceID = si.InvoiceID)
                  FROM dbo.SalesInvoices si WHERE si.InvoiceID = @ID",
                new { ID = invoiceId });

            if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
            if (info.InvoiceStatus != 1) throw new Exception("لا يمكن إرسال فاتورة غير مسودة");
            if (info.DetailCount == 0) throw new Exception("لا يمكن إرسال فاتورة بدون أصناف");

            await connection.ExecuteAsync(
                @"UPDATE dbo.SalesInvoices SET
                    InvoiceStatus = 2,
                    SubmittedBy = @EmployeeID,
                    SubmittedDate = GETDATE(),
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE InvoiceID = @ID",
                new { ID = invoiceId, EmployeeID = employeeId, UserID = userId });

            try
            {
                var approverRoles = await connection.QueryAsync<int>(
                    @"SELECT DISTINCT r.RoleID FROM dbo.UserRoles r
                      INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
                      INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
                      WHERE sm.ModuleCode = N'SCR_SINV' AND rp.CanApprove = 1");
                foreach (var roleId in approverRoles)
                {
                    await _notif.CreateNotificationAsync(3,
                        "فاتورة مبيعات بانتظار الاعتماد 📄",
                        $"فاتورة المبيعات رقم {info.InvoiceNumber} بانتظار الاعتماد",
                        1, null, roleId, "SCR_SINV", invoiceId, userId);
                }
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV",
                description: $"إرسال فاتورة مبيعات للاعتماد: {info.InvoiceNumber}");
        }

        // ==========================================
        // اعتماد الفاتورة + خصم مخزون + قيد محاسبي أوتوماتيك
        // ==========================================
        public async Task ApproveAsync(int invoiceId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string invoiceNumber = "";
            int? deliveryId = null;
            int? createdBy = null;
            int customerId = 0;
            bool isTaxable = true;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, int? DeliveryID, int? CreatedBy, int CustomerID, bool IsTaxable, int DetailCount)>(
                    @"SELECT InvoiceNumber, InvoiceStatus, DeliveryID, CreatedBy, CustomerID, ISNULL(IsTaxable,1) as IsTaxable,
                        (SELECT COUNT(*) FROM dbo.SalesInvoiceDetails d WHERE d.InvoiceID = si.InvoiceID)
                      FROM dbo.SalesInvoices si WHERE si.InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
                if (info.InvoiceStatus != 2) throw new Exception("لا يمكن اعتماد فاتورة ليست في انتظار الاعتماد");
                if (info.DetailCount == 0) throw new Exception("لا يمكن اعتماد فاتورة بدون أصناف");

                invoiceNumber = info.InvoiceNumber;
                deliveryId = info.DeliveryID;
                createdBy = info.CreatedBy;
                customerId = info.CustomerID;
                isTaxable = info.IsTaxable;

                try { await connection.ExecuteAsync("EXEC dbo.sp_RecalcSalesInvoiceTotals @InvoiceID", new { InvoiceID = invoiceId }, transaction); } catch { }

                await connection.ExecuteAsync(
                    @"UPDATE dbo.SalesInvoices SET
                        InvoiceStatus = 3,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID, ModifiedDate = GETDATE()
                      WHERE InvoiceID = @ID",
                    new { ID = invoiceId, EmployeeID = employeeId, UserID = userId }, transaction);

                // خصم مخزون إذا فاتورة مباشرة (لا يوجد تسليم مسبق)
                if (!deliveryId.HasValue || deliveryId.Value == 0)
                {
                    try
                    {
                        await connection.ExecuteAsync(
                            "EXEC dbo.sp_DeductStockForSalesInvoice @InvoiceID, @UserID",
                            new { InvoiceID = invoiceId, UserID = userId }, transaction);
                    }
                    catch { /* قد لا يوجد SP بعد — نتجاهل مؤقتاً */ }
                }

                // تحديث رصيد العميل
                try
                {
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_UpdateCustomerBalanceAfterInvoice @CustomerID",
                        new { CustomerID = customerId }, transaction);
                }
                catch { }

                // إنشاء القيد المحاسبي — مع/بدون ضريبة
                try
                {
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_CreateSalesInvoiceJournal @InvoiceID, @UserID",
                        new { InvoiceID = invoiceId, UserID = userId }, transaction);
                }
                catch (Exception jex)
                {
                    await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                        moduleName: "SCR_SINV",
                        description: $"تحذير: فشل إنشاء القيد المحاسبي للفاتورة {invoiceNumber} (IsTaxable={isTaxable}): {jex.Message}");
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            try
            {
                if (createdBy.HasValue)
                {
                    await _notif.CreateNotificationAsync(3,
                        "تم اعتماد فاتورة المبيعات ✅",
                        $"تم اعتماد فاتورة المبيعات رقم {invoiceNumber} {(isTaxable ? "مع ضريبة" : "بدون ضريبة")}",
                        2, createdBy.Value, null, "SCR_SINV", invoiceId, userId);
                }
                await _notif.MarkRelatedAsActionedAsync("SCR_SINV", invoiceId);
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
            if (string.IsNullOrWhiteSpace(rejectionReason)) throw new Exception("أدخل سبب الرفض");
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, int? CreatedBy)>(
                "SELECT InvoiceNumber, InvoiceStatus, CreatedBy FROM dbo.SalesInvoices WHERE InvoiceID = @ID",
                new { ID = invoiceId });

            if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
            if (info.InvoiceStatus != 2) throw new Exception("لا يمكن رفض فاتورة ليست في انتظار الاعتماد");

            await connection.ExecuteAsync(
                @"UPDATE dbo.SalesInvoices SET
                    InvoiceStatus = 1,
                    RejectedBy = @EmployeeID,
                    RejectedDate = GETDATE(),
                    RejectionReason = @Reason,
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE InvoiceID = @ID",
                new { ID = invoiceId, EmployeeID = employeeId, Reason = rejectionReason, UserID = userId });

            try
            {
                if (info.CreatedBy.HasValue)
                {
                    await _notif.CreateNotificationAsync(3,
                        "تم رفض فاتورة المبيعات ❌",
                        $"تم رفض فاتورة المبيعات رقم {info.InvoiceNumber}. السبب: {rejectionReason}",
                        1, info.CreatedBy.Value, null, "SCR_SINV", invoiceId, userId);
                }
                await _notif.MarkRelatedAsActionedAsync("SCR_SINV", invoiceId);
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV",
                description: $"رفض فاتورة مبيعات: {info.InvoiceNumber} — {rejectionReason}");
        }

        public async Task CancelAsync(int invoiceId, int userId)
        {
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, decimal PaidAmount)>(
                "SELECT InvoiceNumber, InvoiceStatus, ISNULL(PaidAmount, 0) FROM dbo.SalesInvoices WHERE InvoiceID = @ID",
                new { ID = invoiceId });

            if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
            if (info.InvoiceStatus == 7) throw new Exception("الفاتورة ملغية بالفعل");
            if (info.PaidAmount > 0) throw new Exception("لا يمكن إلغاء فاتورة تم الدفع عليها");

            await connection.ExecuteAsync(
                @"UPDATE dbo.SalesInvoices SET InvoiceStatus = 7, ModifiedBy = @UserID, ModifiedDate = GETDATE() WHERE InvoiceID = @ID",
                new { ID = invoiceId, UserID = userId });

            await _audit.WriteAuditLogAsync(userId, 2, "SalesInvoices", invoiceId.ToString(),
                moduleName: "SCR_SINV",
                description: $"إلغاء فاتورة مبيعات: {info.InvoiceNumber}");
        }

        public async Task<(bool Success, string Message)> DeleteInvoiceAsync(int invoiceId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus)>(
                    "SELECT InvoiceNumber, InvoiceStatus FROM dbo.SalesInvoices WHERE InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) return (false, "الفاتورة غير موجودة");
                if (info.InvoiceStatus != 1) return (false, "لا يمكن حذف فاتورة غير مسودة");

                await connection.ExecuteAsync("DELETE FROM dbo.SalesInvoiceDetails WHERE InvoiceID = @ID", new { ID = invoiceId }, transaction);
                await connection.ExecuteAsync("DELETE FROM dbo.SalesInvoices WHERE InvoiceID = @ID", new { ID = invoiceId }, transaction);
                transaction.Commit();

                await _audit.WriteAuditLogAsync(userId, 3, "SalesInvoices", invoiceId.ToString(),
                    moduleName: "SCR_SINV", description: $"حذف فاتورة مبيعات: {info.InvoiceNumber}");

                return (true, "تم حذف الفاتورة بنجاح");
            }
            catch (Exception ex) { transaction.Rollback(); return (false, ex.Message); }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<SalesInvoiceListDto> invoices, int userId)
        {
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
            1 => "مسودة", 2 => "بانتظار الاعتماد", 3 => "معتمدة",
            4 => "مدفوعة جزئياً", 5 => "مدفوعة بالكامل",
            6 => "متأخرة", 7 => "ملغية", _ => "غير محدد"
        };

        public static string GetStatusColor(int status) => status switch
        {
            1 => "#6b7280", 2 => "#f59e0b", 3 => "#10b981",
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
            2 => "من أمر بيع", 3 => "فاتورة مباشرة",
            _ => "فاتورة مباشرة"
        };

        public static string GetSourceColor(int source) => source switch
        {
            2 => "#1d4ed8", 3 => "#D4AF37", _ => "#D4AF37"
        };
    }
}
