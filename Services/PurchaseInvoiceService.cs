using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class PurchaseInvoiceService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public PurchaseInvoiceService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        // ==========================================
        // قائمة الفواتير
        // ==========================================
        public async Task<List<PurchaseInvoiceListDto>> GetInvoicesListAsync()
        {
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
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                            EXEC sp_GetNextNumber N'PI', @NextNum OUTPUT;
                            SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"PI-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"PI-{DateTime.Now:yyMMddHHmmss}"; }
        }

        // ==========================================
        // إضافة فاتورة جديدة
        // ==========================================
        public async Task<int> InsertInvoiceAsync(PurchaseInvoiceHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
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

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
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
            });

            // حساب تاريخ الاستحقاق
            await connection.ExecuteAsync("EXEC dbo.sp_CalcInvoiceDueDate @InvoiceID", new { InvoiceID = newId });

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
            using var connection = CreateConnection();

            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT InvoiceStatus FROM dbo.PurchaseInvoices WHERE InvoiceID = @ID",
                new { ID = header.InvoiceID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل فاتورة ليست في حالة مسودة");

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
            });

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID", new { InvoiceID = header.InvoiceID });
            await connection.ExecuteAsync("EXEC dbo.sp_CalcInvoiceDueDate @InvoiceID", new { InvoiceID = header.InvoiceID });

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", header.InvoiceID.ToString(),
                moduleName: "SCR_PINV",
                description: $"تعديل فاتورة مشتريات: {header.InvoiceNumber}");
        }

        // ==========================================
        // إضافة سطر
        // ==========================================
        public async Task<int> InsertDetailAsync(PurchaseInvoiceDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();

            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT InvoiceStatus FROM dbo.PurchaseInvoices WHERE InvoiceID = @ID",
                new { ID = detail.InvoiceID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل فاتورة ليست في حالة مسودة");

            if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
            if (detail.UnitID <= 0) throw new Exception("اختر الوحدة");
            if (detail.Quantity <= 0) throw new Exception("أدخل الكمية");

            var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseInvoiceDetails WHERE InvoiceID = @ID",
                new { ID = detail.InvoiceID });

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
                TaxRate = detail.TaxRate <= 0 ? 14 : detail.TaxRate,
                detail.BatchNumber,
                detail.ExpiryDate,
                detail.GRNDetailID,
                detail.PODetailID,
                detail.Notes
            });

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID", new { InvoiceID = detail.InvoiceID });

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
            using var connection = CreateConnection();

            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                @"SELECT pi.InvoiceStatus FROM dbo.PurchaseInvoices pi
                  INNER JOIN dbo.PurchaseInvoiceDetails pid ON pi.InvoiceID = pid.InvoiceID
                  WHERE pid.InvoiceDetailID = @ID",
                new { ID = detail.InvoiceDetailID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل سطر في فاتورة ليست مسودة");

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
            });

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID", new { InvoiceID = detail.InvoiceID });

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoiceDetails", detail.InvoiceDetailID.ToString(),
                moduleName: "SCR_PINV",
                description: $"تعديل سطر في فاتورة مشتريات رقم {detail.InvoiceID}");
        }

        // ==========================================
        // حذف سطر
        // ==========================================
        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(int InvoiceID, int InvoiceStatus)>(
                @"SELECT pid.InvoiceID, pi.InvoiceStatus
                  FROM dbo.PurchaseInvoiceDetails pid
                  INNER JOIN dbo.PurchaseInvoices pi ON pid.InvoiceID = pi.InvoiceID
                  WHERE pid.InvoiceDetailID = @ID",
                new { ID = detailId });

            if (info.InvoiceID == 0) throw new Exception("السطر غير موجود");
            if (info.InvoiceStatus != 1) throw new Exception("لا يمكن حذف سطر من فاتورة ليست مسودة");

            await connection.ExecuteAsync(
                "DELETE FROM dbo.PurchaseInvoiceDetails WHERE InvoiceDetailID = @ID",
                new { ID = detailId });

            await connection.ExecuteAsync("EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID", new { InvoiceID = info.InvoiceID });

            await _audit.WriteAuditLogAsync(userId, 3, "PurchaseInvoiceDetails", detailId.ToString(),
                moduleName: "SCR_PINV", description: "حذف سطر من فاتورة مشتريات");
        }

        // ==========================================
        // تحميل سطور من GRN
        // ==========================================
        public async Task<int> ImportLinesFromGRNAsync(int invoiceId, int grnId, List<GRNLineForInvoiceDto> lines, int userId)
        {
            if (lines == null || !lines.Any(l => l.IsSelected)) return 0;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT InvoiceStatus FROM dbo.PurchaseInvoices WHERE InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                if (status != 1) throw new Exception("لا يمكن تحميل سطور على فاتورة ليست مسودة");

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
            catch { transaction.Rollback(); throw; }
        }

        // ==========================================
        // إرسال للاعتماد
        // ==========================================
        public async Task SubmitForApprovalAsync(int invoiceId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, int DetailCount)>(
                @"SELECT pi.InvoiceNumber, pi.InvoiceStatus,
                    (SELECT COUNT(*) FROM dbo.PurchaseInvoiceDetails d WHERE d.InvoiceID = pi.InvoiceID)
                  FROM dbo.PurchaseInvoices pi WHERE pi.InvoiceID = @ID",
                new { ID = invoiceId });

            if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
            if (info.InvoiceStatus != 1) throw new Exception("لا يمكن إرسال فاتورة غير مسودة");
            if (info.DetailCount == 0) throw new Exception("لا يمكن إرسال فاتورة بدون أصناف");

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
                var approverRoles = await connection.QueryAsync<int>(
                    @"SELECT DISTINCT r.RoleID FROM dbo.UserRoles r
                      INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
                      INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
                      WHERE sm.ModuleCode = N'SCR_PINV' AND rp.CanApprove = 1");

                foreach (var roleId in approverRoles)
                {
                    await _notif.CreateNotificationAsync(3,
                        "فاتورة مشتريات بانتظار الاعتماد 📄",
                        $"فاتورة المشتريات رقم {info.InvoiceNumber} بانتظار الاعتماد",
                        1, null, roleId, "SCR_PINV", invoiceId, userId);
                }
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"إرسال فاتورة مشتريات للاعتماد: {info.InvoiceNumber}");
        }

        // ==========================================
        // اعتماد الفاتورة + القيد المحاسبي
        // ==========================================
        public async Task ApproveAsync(int invoiceId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string invoiceNumber = "";
            int? grnId = null;
            int? createdBy = null;
            int supplierId = 0;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, int? GRNID, int? CreatedBy, int SupplierID, int DetailCount)>(
                    @"SELECT InvoiceNumber, InvoiceStatus, GRNID, CreatedBy, SupplierID,
                        (SELECT COUNT(*) FROM dbo.PurchaseInvoiceDetails d WHERE d.InvoiceID = pi.InvoiceID)
                      FROM dbo.PurchaseInvoices pi WHERE pi.InvoiceID = @ID",
                    new { ID = invoiceId }, transaction);

                if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
                if (info.InvoiceStatus != 2) throw new Exception("لا يمكن اعتماد فاتورة ليست في انتظار الاعتماد");
                if (info.DetailCount == 0) throw new Exception("لا يمكن اعتماد فاتورة بدون أصناف");

                invoiceNumber = info.InvoiceNumber;
                grnId = info.GRNID;
                createdBy = info.CreatedBy;
                supplierId = info.SupplierID;

                // 1) إعادة حساب الإجماليات
                await connection.ExecuteAsync(
                    "EXEC dbo.sp_RecalcPurchaseInvoiceTotals @InvoiceID",
                    new { InvoiceID = invoiceId }, transaction);

                // 2) تحديث حالة الفاتورة
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseInvoices SET
                        InvoiceStatus = 3,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID, ModifiedDate = GETDATE()
                      WHERE InvoiceID = @ID",
                    new { ID = invoiceId, EmployeeID = employeeId, UserID = userId }, transaction);

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

                // 5) إنشاء القيد المحاسبي
                try
                {
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_CreatePurchaseInvoiceJournal @InvoiceID, @UserID",
                        new { InvoiceID = invoiceId, UserID = userId }, transaction);
                }
                catch (Exception jex)
                {
                    // لو القيد فشل، نكمل الاعتماد ونسجل التحذير
                    await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                        moduleName: "SCR_PINV",
                        description: $"تحذير: فشل إنشاء القيد المحاسبي للفاتورة {invoiceNumber}: {jex.Message}");
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            // إشعارات
            try
            {
                if (createdBy.HasValue)
                {
                    await _notif.CreateNotificationAsync(3,
                        "تم اعتماد فاتورة المشتريات ✅",
                        $"تم اعتماد فاتورة المشتريات رقم {invoiceNumber}",
                        2, createdBy.Value, null, "SCR_PINV", invoiceId, userId);
                }
                await _notif.MarkRelatedAsActionedAsync("SCR_PINV", invoiceId);
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"اعتماد فاتورة مشتريات: {invoiceNumber}");
        }

        // ==========================================
        // رفض الفاتورة
        // ==========================================
        public async Task RejectAsync(int invoiceId, string rejectionReason, int userId, int? employeeId = null)
        {
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
                if (info.CreatedBy.HasValue)
                {
                    await _notif.CreateNotificationAsync(3,
                        "تم رفض فاتورة المشتريات ❌",
                        $"تم رفض فاتورة المشتريات رقم {info.InvoiceNumber}. السبب: {rejectionReason}",
                        1, info.CreatedBy.Value, null, "SCR_PINV", invoiceId, userId);
                }
                await _notif.MarkRelatedAsActionedAsync("SCR_PINV", invoiceId);
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"رفض فاتورة مشتريات: {info.InvoiceNumber} — {rejectionReason}");
        }

        // ==========================================
        // إلغاء الفاتورة
        // ==========================================
        public async Task CancelAsync(int invoiceId, int userId)
        {
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, int InvoiceStatus, decimal PaidAmount)>(
                "SELECT InvoiceNumber, InvoiceStatus, ISNULL(PaidAmount, 0) FROM dbo.PurchaseInvoices WHERE InvoiceID = @ID",
                new { ID = invoiceId });

            if (string.IsNullOrWhiteSpace(info.InvoiceNumber)) throw new Exception("الفاتورة غير موجودة");
            if (info.InvoiceStatus == 7) throw new Exception("الفاتورة ملغية بالفعل");
            if (info.PaidAmount > 0) throw new Exception("لا يمكن إلغاء فاتورة تم الدفع عليها");

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseInvoices SET
                    InvoiceStatus = 7,
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                  WHERE InvoiceID = @ID",
                new { ID = invoiceId, UserID = userId });

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseInvoices", invoiceId.ToString(),
                moduleName: "SCR_PINV",
                description: $"إلغاء فاتورة مشتريات: {info.InvoiceNumber}");
        }

        // ==========================================
        // حذف فاتورة (مسودة فقط)
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteInvoiceAsync(int invoiceId, int userId)
        {
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
            catch (Exception ex) { transaction.Rollback(); return (false, ex.Message); }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<PurchaseInvoiceListDto> invoices, int userId)
        {
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
    <div class='company'>مصنع واي كي كوتينج لمستحضرات التجميل</div>
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