using Dapper;
using ClosedXML.Excel;

namespace YKCoatings.Services
{
    public class ItemService : BaseDbService
    {
        private readonly AuditService _audit;

        public ItemService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        // ==========================================
        // قائمة الأصناف
        // ==========================================
        public async Task<List<ItemListDto>> GetItemsListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT i.ItemID, i.ItemCode, i.ItemNameAr, i.ItemNameEn,
                               i.ItemType, u.UnitNameAr AS UnitName,
                               ISNULL(i.AverageCost, 0) AS AverageCost,
                               ISNULL(i.DefaultSellingPrice, 0) AS DefaultSellingPrice,
                               i.IsActive
                        FROM dbo.Items i
                        LEFT JOIN dbo.Units u ON i.PrimaryUnitID = u.UnitID
                        WHERE i.IsActive = 1
                        ORDER BY i.ItemType, i.ItemNameAr";
            var result = await connection.QueryAsync<ItemListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب صنف واحد
        // ==========================================
        public async Task<ItemEditDto?> GetItemByIdAsync(int itemId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ItemID, ItemCode, Barcode, ItemNameAr, ItemNameEn,
                               CategoryID, ItemType, PrimaryUnitID,
                               ISNULL(SecondaryUnitID, 0) AS SecondaryUnitID,
                               ISNULL(ConversionFactor, 0) AS ConversionFactor,
                               ISNULL(MinStockLevel, 0) AS MinStockLevel,
                               ISNULL(MaxStockLevel, 0) AS MaxStockLevel,
                               ISNULL(ReorderLevel, 0) AS ReorderLevel,
                               ISNULL(ShelfLifeDays, 0) AS ShelfLifeDays,
                               ISNULL(StandardCost, 0) AS StandardCost,
                               ISNULL(LastPurchasePrice, 0) AS LastPurchasePrice,
                               ISNULL(AverageCost, 0) AS AverageCost,
                               ISNULL(DefaultSellingPrice, 0) AS DefaultSellingPrice,
                               ISNULL(TaxRate, 0) AS TaxRate,
                               ISNULL(IsTaxable, 0) AS IsTaxable,
                               ISNULL(Weight, 0) AS Weight,
                               ISNULL(Volume, 0) AS Volume,
                               Description, Notes, ImagePath, IsActive
                        FROM dbo.Items
                        WHERE ItemID = @ItemID";
            return await connection.QueryFirstOrDefaultAsync<ItemEditDto>(sql, new { ItemID = itemId });
        }

        // ==========================================
        // إضافة صنف
        // ==========================================
        public async Task<int> InsertItemAsync(ItemEditDto item, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.Items 
                        (ItemCode, Barcode, ItemNameAr, ItemNameEn, CategoryID, ItemType,
                         PrimaryUnitID, SecondaryUnitID, ConversionFactor,
                         MinStockLevel, MaxStockLevel, ReorderLevel, ShelfLifeDays,
                         StandardCost, LastPurchasePrice, AverageCost, DefaultSellingPrice,
                         TaxRate, IsTaxable, Weight, Volume,
                         Description, Notes, ImagePath,
                         IsActive, CreatedBy, CreatedDate)
                        VALUES
                        (@ItemCode, @Barcode, @ItemNameAr, @ItemNameEn, @CategoryID, @ItemType,
                         @PrimaryUnitID, NULLIF(@SecondaryUnitID, 0), @ConversionFactor,
                         @MinStockLevel, @MaxStockLevel, @ReorderLevel, @ShelfLifeDays,
                         @StandardCost, @LastPurchasePrice, @AverageCost, @DefaultSellingPrice,
                         @TaxRate, @IsTaxable, @Weight, @Volume,
                         @Description, @Notes, @ImagePath,
                         1, @UserID, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var parameters = new
            {
                item.ItemCode, item.Barcode, item.ItemNameAr, item.ItemNameEn,
                item.CategoryID, item.ItemType, item.PrimaryUnitID, item.SecondaryUnitID,
                item.ConversionFactor, item.MinStockLevel, item.MaxStockLevel,
                item.ReorderLevel, item.ShelfLifeDays, item.StandardCost,
                item.LastPurchasePrice, item.AverageCost, item.DefaultSellingPrice,
                item.TaxRate, item.IsTaxable, item.Weight, item.Volume,
                item.Description, item.Notes, item.ImagePath,
                UserID = userId
            };

            var newId = await connection.ExecuteScalarAsync<int>(sql, parameters);

            await _audit.WriteAuditLogAsync(
                userId: userId, actionType: 1, tableName: "Items",
                recordId: newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { item.ItemCode, item.ItemNameAr, item.ItemType, item.CategoryID }),
                moduleName: "SCR_ITEMS",
                description: $"إضافة صنف جديد: {item.ItemNameAr} ({item.ItemCode})"
            );

            return newId;
        }

        // ==========================================
        // تعديل صنف
        // ==========================================
        public async Task UpdateItemAsync(ItemEditDto item, int userId)
        {
            using var connection = CreateConnection();

            var oldItem = await connection.QueryFirstOrDefaultAsync<ItemEditDto>(
                @"SELECT ItemCode, Barcode, ItemNameAr, ItemNameEn, CategoryID, ItemType,
                         PrimaryUnitID, SecondaryUnitID, ConversionFactor,
                         MinStockLevel, MaxStockLevel, ReorderLevel, ShelfLifeDays,
                         StandardCost, LastPurchasePrice, AverageCost, DefaultSellingPrice,
                         TaxRate, IsTaxable, Weight, Volume, Description, Notes
                  FROM dbo.Items WHERE ItemID = @ItemID",
                new { item.ItemID });

            var sql = @"UPDATE dbo.Items SET
                        Barcode=@Barcode, ItemNameAr=@ItemNameAr, ItemNameEn=@ItemNameEn,
                        CategoryID=@CategoryID, ItemType=@ItemType, PrimaryUnitID=@PrimaryUnitID,
                        SecondaryUnitID=NULLIF(@SecondaryUnitID,0), ConversionFactor=@ConversionFactor,
                        MinStockLevel=@MinStockLevel, MaxStockLevel=@MaxStockLevel,
                        ReorderLevel=@ReorderLevel, ShelfLifeDays=@ShelfLifeDays,
                        StandardCost=@StandardCost, LastPurchasePrice=@LastPurchasePrice,
                        AverageCost=@AverageCost, DefaultSellingPrice=@DefaultSellingPrice,
                        TaxRate=@TaxRate, IsTaxable=@IsTaxable, Weight=@Weight, Volume=@Volume,
                        Description=@Description, Notes=@Notes, ImagePath=@ImagePath,
                        ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE ItemID=@ItemID";

            var parameters = new
            {
                item.ItemID, item.Barcode, item.ItemNameAr, item.ItemNameEn,
                item.CategoryID, item.ItemType, item.PrimaryUnitID, item.SecondaryUnitID,
                item.ConversionFactor, item.MinStockLevel, item.MaxStockLevel,
                item.ReorderLevel, item.ShelfLifeDays, item.StandardCost,
                item.LastPurchasePrice, item.AverageCost, item.DefaultSellingPrice,
                item.TaxRate, item.IsTaxable, item.Weight, item.Volume,
                item.Description, item.Notes, item.ImagePath,
                UserID = userId
            };

            await connection.ExecuteAsync(sql, parameters);

            var changes = new List<string>();
            if (oldItem != null)
            {
                if (oldItem.ItemNameAr != item.ItemNameAr) changes.Add("ItemNameAr");
                if (oldItem.ItemNameEn != item.ItemNameEn) changes.Add("ItemNameEn");
                if (oldItem.Barcode != item.Barcode) changes.Add("Barcode");
                if (oldItem.CategoryID != item.CategoryID) changes.Add("CategoryID");
                if (oldItem.ItemType != item.ItemType) changes.Add("ItemType");
                if (oldItem.PrimaryUnitID != item.PrimaryUnitID) changes.Add("PrimaryUnitID");
                if (oldItem.StandardCost != item.StandardCost) changes.Add("StandardCost");
                if (oldItem.DefaultSellingPrice != item.DefaultSellingPrice) changes.Add("DefaultSellingPrice");
                if (oldItem.TaxRate != item.TaxRate) changes.Add("TaxRate");
                if (oldItem.IsTaxable != item.IsTaxable) changes.Add("IsTaxable");
            }

            await _audit.WriteAuditLogAsync(
                userId: userId, actionType: 2, tableName: "Items",
                recordId: item.ItemID.ToString(),
                oldValues: oldItem != null ? System.Text.Json.JsonSerializer.Serialize(new
                { oldItem.ItemNameAr, oldItem.CategoryID, oldItem.StandardCost, oldItem.DefaultSellingPrice }) : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { item.ItemNameAr, item.CategoryID, item.StandardCost, item.DefaultSellingPrice }),
                changedColumns: changes.Any() ? string.Join(",", changes) : null,
                moduleName: "SCR_ITEMS",
                description: $"تعديل صنف: {item.ItemNameAr} ({item.ItemCode}) — تغيير {changes.Count} حقل"
            );
        }

        // ==========================================
        // إيقاف تنشيط
        // ==========================================
        public async Task DeactivateItemAsync(int itemId, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE dbo.Items SET IsActive=0, ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE ItemID=@ItemID",
                new { ItemID = itemId, UserID = userId });

            await _audit.WriteAuditLogAsync(userId, 3, "Items", itemId.ToString(),
                moduleName: "SCR_ITEMS", description: $"إيقاف تنشيط صنف رقم: {itemId}");
        }

        // ==========================================
        // حذف نهائي
        // ==========================================
        public async Task<bool> DeleteItemAsync(int itemId, int userId)
        {
            using var connection = CreateConnection();
            try
            {
                await connection.ExecuteAsync("DELETE FROM dbo.Items WHERE ItemID=@ItemID", new { ItemID = itemId });
                await _audit.WriteAuditLogAsync(userId, 3, "Items", itemId.ToString(),
                    moduleName: "SCR_ITEMS", description: $"حذف نهائي لصنف رقم: {itemId}");
                return true;
            }
            catch
            {
                await DeactivateItemAsync(itemId, userId);
                return false;
            }
        }

        // ==========================================
        // توليد كود الصنف
        // ==========================================
        public async Task<string> GenerateItemCodeAsync(int itemType)
        {
            using var connection = CreateConnection();
            string seqCode = itemType switch
            {
                1 => "RM", 2 => "PM", 3 => "SF", 4 => "FG", _ => "ITM"
            };
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                           EXEC sp_GetNextNumber @SeqCode, @NextNum OUTPUT;
                           SELECT @NextNum AS NewCode;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql, new { SeqCode = seqCode });
                return result ?? $"{seqCode}-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"{seqCode}-{DateTime.Now:yyMMddHHmmss}"; }
        }

        // ==========================================
        // معلومات الإنشاء والتعديل
        // ==========================================
        public async Task<ItemAuditDto?> GetItemAuditAsync(int itemId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ISNULL(uc.FullName, N'غير محدد') AS CreatedByName, i.CreatedDate,
                               ISNULL(um.FullName, N'') AS ModifiedByName, i.ModifiedDate
                        FROM dbo.Items i
                        LEFT JOIN dbo.SystemUsers uc ON i.CreatedBy = uc.UserID
                        LEFT JOIN dbo.SystemUsers um ON i.ModifiedBy = um.UserID
                        WHERE i.ItemID = @ItemID";
            return await connection.QueryFirstOrDefaultAsync<ItemAuditDto>(sql, new { ItemID = itemId });
        }

        // ==========================================
        // تصدير إلى Excel
        // ==========================================
        public async Task<byte[]> ExportItemsToExcelAsync(List<ItemListDto> items, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("الأصناف");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير الأصناف والمنتجات — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 8).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 8).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 8).Style.Font.FontColor = XLColor.Gray;
            ws.Range(2, 1, 2, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 4;
            var headers = new[] { "#", "الكود", "اسم الصنف (عربي)", "اسم الصنف (إنجليزي)",
                                  "النوع", "الوحدة", "متوسط التكلفة", "سعر البيع" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d143f");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = headerRow + 1;
            int num = 0;
            foreach (var item in items)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = item.ItemCode ?? "";
                ws.Cell(row, 3).Value = item.ItemNameAr ?? "";
                ws.Cell(row, 4).Value = item.ItemNameEn ?? "";
                ws.Cell(row, 5).Value = item.ItemType switch
                { 1 => "مادة خام", 2 => "مادة تعبئة", 3 => "نصف مصنع", 4 => "منتج تام", _ => "غير محدد" };
                ws.Cell(row, 6).Value = item.UnitName ?? "";
                ws.Cell(row, 7).Value = item.AverageCost;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 8).Value = item.DefaultSellingPrice;
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";

                for (int i = 1; i <= 8; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");
                }
                if (num % 2 == 0)
                    ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f7ff");
                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(3).Width = 30;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "Items",
                moduleName: "SCR_ITEMS", description: $"تصدير {items.Count} صنف إلى Excel");

            return stream.ToArray();
        }

        // ==========================================
        // تقرير HTML لصنف واحد
        // ==========================================
        public async Task<string> GenerateItemReportHtmlAsync(int itemId, int userId)
        {
            using var connection = CreateConnection();
            var item = await GetItemByIdAsync(itemId);
            if (item == null) return "<h3>الصنف غير موجود</h3>";

            var categoryName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT CategoryNameAr FROM dbo.ItemCategories WHERE CategoryID=@ID",
                new { ID = item.CategoryID }) ?? "غير محدد";
            var unitName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT UnitNameAr FROM dbo.Units WHERE UnitID=@ID",
                new { ID = item.PrimaryUnitID }) ?? "غير محدد";
            var typeName = item.ItemType switch
            { 1 => "مادة خام", 2 => "مادة تعبئة", 3 => "نصف مصنع", 4 => "منتج تام", _ => "غير محدد" };

            var html = $@"
<!DOCTYPE html>
<html dir='rtl' lang='ar'>
<head>
<meta charset='utf-8'><title>بطاقة صنف — {item.ItemNameAr}</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Cairo','Segoe UI',sans-serif;background:#fff;padding:30px;color:#1f2937}}
.report-header{{text-align:center;margin-bottom:30px;border-bottom:3px solid #1d4ed8;padding-bottom:20px}}
.company-name{{font-size:22px;font-weight:800;color:#1d143f}}
.report-title{{font-size:16px;color:#6b7280;margin-top:4px}}
.item-code{{display:inline-block;background:#f5f3ff;color:#1d4ed8;padding:4px 16px;border-radius:8px;font-weight:700;margin-top:10px}}
.section{{margin-bottom:24px}}.section-title{{font-size:14px;font-weight:800;color:#1d143f;background:#f8f7ff;padding:8px 16px;border-radius:8px;margin-bottom:12px;border-right:4px solid #1d4ed8}}
.info-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}}
.info-item{{background:#f9fafb;padding:12px;border-radius:10px;border:1px solid #f3f4f6}}
.info-label{{font-size:11px;color:#9ca3af;font-weight:600}}.info-value{{font-size:14px;font-weight:700;color:#1f2937;margin-top:2px}}
.footer{{text-align:center;margin-top:40px;padding-top:16px;border-top:1px solid #e5e7eb;font-size:11px;color:#9ca3af}}
</style></head><body>
<div class='report-header'><div class='company-name'>مصنع واي كي كوتينج لمستحضرات التجميل</div>
<div class='report-title'>بطاقة صنف</div><div class='item-code'>{item.ItemCode}</div></div>
<div class='section'><div class='section-title'>البيانات الأساسية</div><div class='info-grid'>
<div class='info-item'><div class='info-label'>اسم الصنف</div><div class='info-value'>{item.ItemNameAr}</div></div>
<div class='info-item'><div class='info-label'>إنجليزي</div><div class='info-value'>{item.ItemNameEn ?? "—"}</div></div>
<div class='info-item'><div class='info-label'>النوع</div><div class='info-value'>{typeName}</div></div>
<div class='info-item'><div class='info-label'>التصنيف</div><div class='info-value'>{categoryName}</div></div>
<div class='info-item'><div class='info-label'>الوحدة</div><div class='info-value'>{unitName}</div></div>
<div class='info-item'><div class='info-label'>الباركود</div><div class='info-value'>{item.Barcode ?? "—"}</div></div>
</div></div>
<div class='section'><div class='section-title'>التكاليف والأسعار</div><div class='info-grid'>
<div class='info-item'><div class='info-label'>التكلفة المعيارية</div><div class='info-value'>{item.StandardCost:#,##0.00} ج.م</div></div>
<div class='info-item'><div class='info-label'>آخر سعر شراء</div><div class='info-value'>{item.LastPurchasePrice:#,##0.00} ج.م</div></div>
<div class='info-item'><div class='info-label'>متوسط التكلفة</div><div class='info-value'>{item.AverageCost:#,##0.00} ج.م</div></div>
<div class='info-item'><div class='info-label'>سعر البيع</div><div class='info-value'>{item.DefaultSellingPrice:#,##0.00} ج.م</div></div>
</div></div>
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP v1.0.0</div>
</body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "Items", itemId.ToString(),
                moduleName: "SCR_ITEMS", description: $"طباعة بطاقة صنف: {item.ItemNameAr}");

            return html;
        }
    }
}