using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class SupplierService : BaseDbService
    {
        private readonly AuditService _audit;

        public SupplierService(IConfiguration configuration, AuditService audit)
            : base(configuration) { _audit = audit; }

        // ==========================================
        // قائمة الموردين
        // ==========================================
        public async Task<List<SupplierListDto>> GetSuppliersListAsync(bool includeInactive = false)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            SupplierID, SupplierCode, SupplierNameAr, SupplierNameEn,
                            SupplierType, SupplierTypeName,
                            Phone1, Mobile, Email, City,
                            PaymentTermName,
                            CreditLimit, CurrentBalance, AvailableCredit,
                            Rating, IsActive,
                            ItemCount, ContactCount, TotalPOs, TotalInvoices, UnpaidInvoices
                        FROM dbo.vw_SupplierFullDetails
                        WHERE (@IncludeInactive = 1 OR IsActive = 1)
                        ORDER BY SupplierNameAr";
            return (await connection.QueryAsync<SupplierListDto>(sql, new { IncludeInactive = includeInactive })).ToList();
        }

        // ==========================================
        // جلب مورد واحد
        // ==========================================
        public async Task<SupplierEditDto?> GetSupplierByIdAsync(int supplierId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            SupplierID, SupplierCode, SupplierNameAr, SupplierNameEn,
                            SupplierType, TaxNumber, CommercialRegister,
                            Address, City, Country,
                            Phone1, Phone2, Mobile, Email, Website,
                            PaymentTermID, CurrencyID,
                            ISNULL(CreditLimit, 0) AS CreditLimit,
                            ISNULL(OpeningBalance, 0) AS OpeningBalance,
                            ISNULL(CurrentBalance, 0) AS CurrentBalance,
                            BankName, BankAccountNumber, IBAN,
                            ISNULL(Rating, 3) AS Rating,
                            Notes, IsActive
                        FROM dbo.Suppliers
                        WHERE SupplierID = @ID";
            return await connection.QueryFirstOrDefaultAsync<SupplierEditDto>(sql, new { ID = supplierId });
        }

        // ==========================================
        // توليد كود المورد
        // ==========================================
        public async Task<string> GenerateSupplierCodeAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @N NVARCHAR(50); EXEC sp_GetNextNumber N'SUP', @N OUTPUT; SELECT @N;";
                return await connection.QueryFirstOrDefaultAsync<string>(sql) ?? $"SUP-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"SUP-{DateTime.Now:yyMMddHHmmss}"; }
        }

        // ==========================================
        // إضافة مورد
        // ==========================================
        public async Task<int> InsertSupplierAsync(SupplierEditDto sup, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.Suppliers
                        (SupplierCode, SupplierNameAr, SupplierNameEn, SupplierType,
                         TaxNumber, CommercialRegister, Address, City, Country,
                         Phone1, Phone2, Mobile, Email, Website,
                         PaymentTermID, CurrencyID, CreditLimit, OpeningBalance, CurrentBalance,
                         BankName, BankAccountNumber, IBAN, Rating, Notes,
                         IsActive, CreatedBy, CreatedDate)
                        VALUES
                        (@SupplierCode, @SupplierNameAr, @SupplierNameEn, @SupplierType,
                         @TaxNumber, @CommercialRegister, @Address, @City, @Country,
                         @Phone1, @Phone2, @Mobile, @Email, @Website,
                         NULLIF(@PaymentTermID, 0), NULLIF(@CurrencyID, 0),
                         @CreditLimit, @OpeningBalance, @OpeningBalance,
                         @BankName, @BankAccountNumber, @IBAN, @Rating, @Notes,
                         1, @UserID, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                sup.SupplierCode, sup.SupplierNameAr, sup.SupplierNameEn, sup.SupplierType,
                sup.TaxNumber, sup.CommercialRegister, sup.Address, sup.City, sup.Country,
                sup.Phone1, sup.Phone2, sup.Mobile, sup.Email, sup.Website,
                sup.PaymentTermID, sup.CurrencyID, sup.CreditLimit, sup.OpeningBalance,
                sup.BankName, sup.BankAccountNumber, sup.IBAN, sup.Rating, sup.Notes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "Suppliers", newId.ToString(),
                moduleName: "SCR_SUPPLIERS",
                description: $"إضافة مورد: {sup.SupplierNameAr} ({sup.SupplierCode})");

            return newId;
        }

        // ==========================================
        // تعديل مورد
        // ==========================================
        public async Task UpdateSupplierAsync(SupplierEditDto sup, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.Suppliers SET
                            SupplierCode=@SupplierCode, SupplierNameAr=@SupplierNameAr,
                            SupplierNameEn=@SupplierNameEn, SupplierType=@SupplierType,
                            TaxNumber=@TaxNumber, CommercialRegister=@CommercialRegister,
                            Address=@Address, City=@City, Country=@Country,
                            Phone1=@Phone1, Phone2=@Phone2, Mobile=@Mobile,
                            Email=@Email, Website=@Website,
                            PaymentTermID=NULLIF(@PaymentTermID, 0),
                            CurrencyID=NULLIF(@CurrencyID, 0),
                            CreditLimit=@CreditLimit,
                            BankName=@BankName, BankAccountNumber=@BankAccountNumber, IBAN=@IBAN,
                            Rating=@Rating, Notes=@Notes,
                            ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE SupplierID=@SupplierID";

            await connection.ExecuteAsync(sql, new
            {
                sup.SupplierID, sup.SupplierCode, sup.SupplierNameAr, sup.SupplierNameEn,
                sup.SupplierType, sup.TaxNumber, sup.CommercialRegister,
                sup.Address, sup.City, sup.Country,
                sup.Phone1, sup.Phone2, sup.Mobile, sup.Email, sup.Website,
                sup.PaymentTermID, sup.CurrencyID, sup.CreditLimit,
                sup.BankName, sup.BankAccountNumber, sup.IBAN,
                sup.Rating, sup.Notes, UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 2, "Suppliers", sup.SupplierID.ToString(),
                moduleName: "SCR_SUPPLIERS",
                description: $"تعديل مورد: {sup.SupplierNameAr}");
        }

        // ==========================================
        // حذف مورد (تعطيل)
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteSupplierAsync(int supplierId, int userId)
        {
            using var connection = CreateConnection();

            var hasInvoices = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM dbo.PurchaseInvoices WHERE SupplierID=@ID AND InvoiceStatus NOT IN (7)", new { ID = supplierId });
            if (hasInvoices > 0) return (false, $"لا يمكن حذف المورد — يوجد {hasInvoices} فاتورة مرتبطة");

            var hasPOs = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE SupplierID=@ID AND POStatus NOT IN (9)", new { ID = supplierId });
            if (hasPOs > 0) return (false, $"لا يمكن حذف المورد — يوجد {hasPOs} أمر شراء مرتبط");

            var supName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT SupplierNameAr FROM dbo.Suppliers WHERE SupplierID=@ID", new { ID = supplierId });

            await connection.ExecuteAsync(
                "UPDATE dbo.Suppliers SET IsActive=0, ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE SupplierID=@ID",
                new { ID = supplierId, UserID = userId });

            await _audit.WriteAuditLogAsync(userId, 3, "Suppliers", supplierId.ToString(),
                moduleName: "SCR_SUPPLIERS", description: $"حذف مورد: {supName}");
            return (true, "تم حذف المورد بنجاح");
        }

        // ==========================================
        // جهات اتصال المورد
        // ==========================================
        public async Task<List<SupplierContactDto>> GetContactsAsync(int supplierId)
        {
            using var connection = CreateConnection();
            return (await connection.QueryAsync<SupplierContactDto>(
                @"SELECT ContactID, SupplierID, ContactName, JobTitle, Phone, Mobile, Email, IsPrimary, Notes, IsActive
                  FROM dbo.SupplierContacts WHERE SupplierID=@ID AND IsActive=1 ORDER BY IsPrimary DESC, ContactName",
                new { ID = supplierId })).ToList();
        }

        public async Task<int> InsertContactAsync(SupplierContactDto contact, int userId)
        {
            using var connection = CreateConnection();
            var newId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.SupplierContacts (SupplierID, ContactName, JobTitle, Phone, Mobile, Email, IsPrimary, Notes, IsActive, CreatedDate)
                  VALUES (@SupplierID, @ContactName, @JobTitle, @Phone, @Mobile, @Email, @IsPrimary, @Notes, 1, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);", contact);
            await _audit.WriteAuditLogAsync(userId, 1, "SupplierContacts", newId.ToString(),
                moduleName: "SCR_SUPPLIERS", description: $"إضافة جهة اتصال: {contact.ContactName}");
            return newId;
        }

        public async Task DeleteContactAsync(int contactId, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.SupplierContacts SET IsActive=0 WHERE ContactID=@ID", new { ID = contactId });
            await _audit.WriteAuditLogAsync(userId, 3, "SupplierContacts", contactId.ToString(),
                moduleName: "SCR_SUPPLIERS", description: "حذف جهة اتصال مورد");
        }

        // ==========================================
        // مواد المورد
        // ==========================================
        public async Task<List<SupplierItemDto>> GetSupplierItemsAsync(int supplierId)
        {
            using var connection = CreateConnection();
            return (await connection.QueryAsync<SupplierItemDto>(
                @"SELECT si.SupplierItemID, si.SupplierID, si.ItemID, i.ItemCode, i.ItemNameAr,
                         si.SupplierItemCode, si.UnitPrice, si.MinOrderQty, si.LeadTimeDays,
                         si.LastPurchaseDate, si.LastPurchasePrice, si.IsPrimary, si.IsActive
                  FROM dbo.SupplierItems si
                  INNER JOIN dbo.Items i ON si.ItemID = i.ItemID
                  WHERE si.SupplierID=@ID AND si.IsActive=1
                  ORDER BY i.ItemNameAr",
                new { ID = supplierId })).ToList();
        }

        public async Task<int> InsertSupplierItemAsync(SupplierItemEditDto item, int userId)
        {
            using var connection = CreateConnection();
            var exists = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM dbo.SupplierItems WHERE SupplierID=@SID AND ItemID=@IID AND IsActive=1",
                new { SID = item.SupplierID, IID = item.ItemID });
            if (exists > 0) throw new Exception("هذا الصنف مضاف بالفعل لهذا المورد");

            var newId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.SupplierItems (SupplierID, ItemID, SupplierItemCode, UnitPrice, MinOrderQty, LeadTimeDays, IsPrimary, IsActive, CreatedDate)
                  VALUES (@SupplierID, @ItemID, @SupplierItemCode, @UnitPrice, @MinOrderQty, @LeadTimeDays, @IsPrimary, 1, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);", item);
            await _audit.WriteAuditLogAsync(userId, 1, "SupplierItems", newId.ToString(),
                moduleName: "SCR_SUPPLIERS", description: $"إضافة صنف للمورد: {item.ItemID}");
            return newId;
        }

        public async Task DeleteSupplierItemAsync(int supplierItemId, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.SupplierItems SET IsActive=0 WHERE SupplierItemID=@ID", new { ID = supplierItemId });
            await _audit.WriteAuditLogAsync(userId, 3, "SupplierItems", supplierItemId.ToString(),
                moduleName: "SCR_SUPPLIERS", description: "حذف صنف من المورد");
        }

        // ==========================================
        // كشف حساب المورد
        // ==========================================
        public async Task<List<SupplierStatementDto>> GetStatementAsync(int supplierId)
        {
            using var connection = CreateConnection();
            return (await connection.QueryAsync<SupplierStatementDto>(
                @"SELECT TransDate, TransType, DocNumber, DebitAmount, CreditAmount, Balance, DocType, DocID
                  FROM dbo.vw_SupplierStatement WHERE SupplierID=@ID ORDER BY TransDate DESC",
                new { ID = supplierId })).ToList();
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<SupplierListDto> suppliers, int userId)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("الموردين");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";

            ws.Cell(1, 1).Value = "تقرير الموردين — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 10).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 10).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var headers = new[] { "#", "الكود", "المورد", "النوع", "الهاتف", "المدينة", "شروط الدفع", "حد الائتمان", "الرصيد", "التقييم" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(3, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b"); c.Style.Font.FontColor = XLColor.White;
            }

            int row = 4, n = 0;
            foreach (var s in suppliers)
            {
                n++;
                ws.Cell(row, 1).Value = n;
                ws.Cell(row, 2).Value = s.SupplierCode ?? "";
                ws.Cell(row, 3).Value = s.SupplierNameAr ?? "";
                ws.Cell(row, 4).Value = GetTypeName(s.SupplierType);
                ws.Cell(row, 5).Value = s.Phone1 ?? s.Mobile ?? "—";
                ws.Cell(row, 6).Value = s.City ?? "—";
                ws.Cell(row, 7).Value = s.PaymentTermName ?? "—";
                ws.Cell(row, 8).Value = s.CreditLimit;
                ws.Cell(row, 9).Value = s.CurrentBalance;
                ws.Cell(row, 10).Value = new string('⭐', s.Rating);
                if (n % 2 == 0) ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
                row++;
            }
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, "Suppliers", moduleName: "SCR_SUPPLIERS",
                description: $"تصدير {suppliers.Count} مورد إلى Excel");
            return stream.ToArray();
        }

        // ==========================================
        // طباعة بطاقة المورد
        // ==========================================
        public async Task<string> GenerateSupplierCardHtmlAsync(int supplierId, int userId)
        {
            var sup = await GetSupplierByIdAsync(supplierId);
            if (sup == null) return "<h3>المورد غير موجود</h3>";

            var contacts = await GetContactsAsync(supplierId);
            var items = await GetSupplierItemsAsync(supplierId);

            var contactsHtml = "";
            foreach (var c in contacts)
                contactsHtml += $"<tr><td>{c.ContactName}</td><td>{c.JobTitle ?? "—"}</td><td>{c.Phone ?? "—"}</td><td>{c.Mobile ?? "—"}</td><td>{c.Email ?? "—"}</td></tr>";

            var itemsHtml = "";
            foreach (var i in items)
                itemsHtml += $"<tr><td>{i.ItemCode}</td><td>{i.ItemNameAr}</td><td style='text-align:left'>{i.UnitPrice:#,##0.00}</td><td>{i.LeadTimeDays} يوم</td></tr>";

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'><title>بطاقة مورد — {sup.SupplierCode}</title>
<style>*{{margin:0;padding:0;box-sizing:border-box}}body{{font-family:'Cairo',sans-serif;padding:30px;color:#0f172a;font-size:12px}}
.header{{text-align:center;border-bottom:3px solid #1e293b;padding-bottom:18px;margin-bottom:20px}}.company{{font-size:18px;font-weight:800}}.doc-title{{font-size:14px;color:#475569;margin-top:4px}}
.info-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:18px}}.info-item{{background:#f8fafc;padding:10px;border-radius:8px;border:1px solid #e2e8f0}}.info-label{{font-size:9px;color:#64748b;font-weight:600}}.info-value{{font-size:12px;font-weight:700;margin-top:2px}}
.section-title{{font-weight:800;font-size:13px;margin:16px 0 8px;padding:6px 12px;background:#f1f5f9;border-radius:6px}}
table{{width:100%;border-collapse:collapse;margin-bottom:12px}}th{{background:#1e293b;color:white;padding:8px;font-size:10px;text-align:right}}td{{padding:7px 8px;border-bottom:1px solid #e2e8f0;font-size:10px}}tr:nth-child(even){{background:#f8fafc}}
.footer{{text-align:center;margin-top:24px;padding-top:10px;border-top:1px solid #e2e8f0;font-size:9px;color:#94a3b8}}</style></head><body>
<div class='header'><div class='company'>مصنع واي كي كوتينج لمستحضرات التجميل</div><div class='doc-title'>بطاقة مورد — {sup.SupplierCode}</div></div>
<div class='info-grid'>
<div class='info-item'><div class='info-label'>الكود</div><div class='info-value'>{sup.SupplierCode}</div></div>
<div class='info-item'><div class='info-label'>الاسم</div><div class='info-value'>{sup.SupplierNameAr}</div></div>
<div class='info-item'><div class='info-label'>النوع</div><div class='info-value'>{GetTypeName(sup.SupplierType)}</div></div>
<div class='info-item'><div class='info-label'>التقييم</div><div class='info-value'>{new string('⭐', sup.Rating)}</div></div>
<div class='info-item'><div class='info-label'>الهاتف</div><div class='info-value'>{sup.Phone1 ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>الموبايل</div><div class='info-value'>{sup.Mobile ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>البريد</div><div class='info-value'>{sup.Email ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>المدينة</div><div class='info-value'>{sup.City ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>الرقم الضريبي</div><div class='info-value'>{sup.TaxNumber ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>السجل التجاري</div><div class='info-value'>{sup.CommercialRegister ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>حد الائتمان</div><div class='info-value'>{sup.CreditLimit:#,##0.00}</div></div>
<div class='info-item'><div class='info-label'>الرصيد الحالي</div><div class='info-value'>{sup.CurrentBalance:#,##0.00}</div></div>
</div>
{(contacts.Any() ? $"<div class='section-title'>جهات الاتصال ({contacts.Count})</div><table><thead><tr><th>الاسم</th><th>المنصب</th><th>الهاتف</th><th>الموبايل</th><th>البريد</th></tr></thead><tbody>{contactsHtml}</tbody></table>" : "")}
{(items.Any() ? $"<div class='section-title'>الأصناف ({items.Count})</div><table><thead><tr><th>الكود</th><th>الصنف</th><th>السعر</th><th>مدة التوريد</th></tr></thead><tbody>{itemsHtml}</tbody></table>" : "")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP v2.0</div></body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "Suppliers", supplierId.ToString(),
                moduleName: "SCR_SUPPLIERS", description: $"طباعة بطاقة مورد: {sup.SupplierCode}");
            return html;
        }

        // ==========================================
        // سجل التدقيق - من أضاف ومن عدّل
        // ==========================================
        public async Task<ItemAuditDto?> GetSupplierAuditAsync(int supplierId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ISNULL(uc.FullName, ISNULL(uc.Username, N'غير محدد')) AS CreatedByName, s.CreatedDate,
                               ISNULL(um.FullName, ISNULL(um.Username, N'')) AS ModifiedByName, s.ModifiedDate
                        FROM dbo.Suppliers s
                        LEFT JOIN dbo.SystemUsers uc ON s.CreatedBy = uc.UserID
                        LEFT JOIN dbo.SystemUsers um ON s.ModifiedBy = um.UserID
                        WHERE s.SupplierID = @ID";
            return await connection.QueryFirstOrDefaultAsync<ItemAuditDto>(sql, new { ID = supplierId });
        }

        // ==========================================
        // Helpers
        // ==========================================
        public static string GetTypeName(int t) => t switch { 1 => "مورد خامات", 2 => "مورد تعبئة", 3 => "مورد خدمات", 4 => "مورد عام", _ => "غير محدد" };
        public static string GetTypeColor(int t) => t switch { 1 => "#1d4ed8", 2 => "#0891b2", 3 => "#f97316", 4 => "#64748b", _ => "#6b7280" };
    }
}