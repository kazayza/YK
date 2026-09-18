using Dapper;
using ClosedXML.Excel;

namespace YKCoatings.Services
{
    public class CategoryService : BaseDbService
    {
        private readonly AuditService _audit;

        public CategoryService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        public async Task<List<CategoryListDto>> GetCategoriesListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT c.CategoryID, c.CategoryCode, c.CategoryNameAr, c.CategoryNameEn,
                               c.ParentCategoryID, c.CategoryLevel, c.SortOrder, c.IsActive,
                               p.CategoryNameAr AS ParentCategoryName,
                               (SELECT COUNT(*) FROM dbo.Items i WHERE i.CategoryID = c.CategoryID AND i.IsActive = 1) AS ItemCount
                        FROM dbo.ItemCategories c
                        LEFT JOIN dbo.ItemCategories p ON c.ParentCategoryID = p.CategoryID
                        WHERE c.IsActive = 1
                        ORDER BY ISNULL(c.ParentCategoryID, c.CategoryID), c.ParentCategoryID, c.SortOrder, c.CategoryNameAr";
            var result = await connection.QueryAsync<CategoryListDto>(sql);
            return result.ToList();
        }

        public async Task<CategoryEditDto?> GetCategoryByIdAsync(int categoryId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT CategoryID, CategoryCode, CategoryNameAr, CategoryNameEn,
                               ParentCategoryID, CategoryLevel, SortOrder, IsActive
                        FROM dbo.ItemCategories WHERE CategoryID = @CategoryID";
            return await connection.QueryFirstOrDefaultAsync<CategoryEditDto>(sql, new { CategoryID = categoryId });
        }

        public async Task<int> InsertCategoryAsync(CategoryEditDto category, int userId)
        {
            using var connection = CreateConnection();

            int level = 1;
            if (category.ParentCategoryID.HasValue && category.ParentCategoryID > 0)
            {
                var parentLevel = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT CategoryLevel FROM dbo.ItemCategories WHERE CategoryID = @ID",
                    new { ID = category.ParentCategoryID });
                level = parentLevel + 1;
            }

            var sql = @"INSERT INTO dbo.ItemCategories 
                        (CategoryCode, CategoryNameAr, CategoryNameEn, 
                         ParentCategoryID, CategoryLevel, SortOrder, IsActive, CreatedDate)
                        VALUES
                        (@CategoryCode, @CategoryNameAr, @CategoryNameEn, 
                         NULLIF(@ParentCategoryID, 0), @CategoryLevel, @SortOrder, 1, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                category.CategoryCode, category.CategoryNameAr, category.CategoryNameEn,
                category.ParentCategoryID, CategoryLevel = level, category.SortOrder
            });

            await _audit.WriteAuditLogAsync(userId, 1, "ItemCategories", newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { category.CategoryCode, category.CategoryNameAr, category.ParentCategoryID }),
                moduleName: "SCR_CATEGORIES",
                description: $"إضافة تصنيف جديد: {category.CategoryNameAr} ({category.CategoryCode})");

            return newId;
        }

        public async Task UpdateCategoryAsync(CategoryEditDto category, int userId)
        {
            using var connection = CreateConnection();

            var oldCat = await connection.QueryFirstOrDefaultAsync<CategoryEditDto>(
                @"SELECT CategoryCode, CategoryNameAr, CategoryNameEn, ParentCategoryID, SortOrder
                  FROM dbo.ItemCategories WHERE CategoryID = @CategoryID",
                new { category.CategoryID });

            int level = 1;
            if (category.ParentCategoryID.HasValue && category.ParentCategoryID > 0)
            {
                var parentLevel = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT CategoryLevel FROM dbo.ItemCategories WHERE CategoryID = @ID",
                    new { ID = category.ParentCategoryID });
                level = parentLevel + 1;
            }

            var sql = @"UPDATE dbo.ItemCategories SET
                            CategoryCode=@CategoryCode, CategoryNameAr=@CategoryNameAr,
                            CategoryNameEn=@CategoryNameEn, ParentCategoryID=NULLIF(@ParentCategoryID, 0),
                            CategoryLevel=@CategoryLevel, SortOrder=@SortOrder
                        WHERE CategoryID=@CategoryID";

            await connection.ExecuteAsync(sql, new
            {
                category.CategoryID, category.CategoryCode, category.CategoryNameAr,
                category.CategoryNameEn, category.ParentCategoryID,
                CategoryLevel = level, category.SortOrder
            });

            var changes = new List<string>();
            if (oldCat != null)
            {
                if (oldCat.CategoryNameAr != category.CategoryNameAr) changes.Add("CategoryNameAr");
                if (oldCat.CategoryNameEn != category.CategoryNameEn) changes.Add("CategoryNameEn");
                if (oldCat.CategoryCode != category.CategoryCode) changes.Add("CategoryCode");
                if (oldCat.ParentCategoryID != category.ParentCategoryID) changes.Add("ParentCategoryID");
                if (oldCat.SortOrder != category.SortOrder) changes.Add("SortOrder");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "ItemCategories", category.CategoryID.ToString(),
                oldValues: oldCat != null ? System.Text.Json.JsonSerializer.Serialize(new
                { oldCat.CategoryCode, oldCat.CategoryNameAr, oldCat.ParentCategoryID }) : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { category.CategoryCode, category.CategoryNameAr, category.ParentCategoryID }),
                changedColumns: changes.Any() ? string.Join(",", changes) : null,
                moduleName: "SCR_CATEGORIES",
                description: $"تعديل تصنيف: {category.CategoryNameAr} — تغيير {changes.Count} حقل");
        }

        public async Task<(bool Success, string Message)> DeleteCategoryAsync(int categoryId, int userId)
        {
            using var connection = CreateConnection();

            var itemCount = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM dbo.Items WHERE CategoryID=@ID AND IsActive=1",
                new { ID = categoryId });
            if (itemCount > 0)
                return (false, $"لا يمكن حذف التصنيف لوجود {itemCount} صنف مرتبط به");

            var childCount = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM dbo.ItemCategories WHERE ParentCategoryID=@ID AND IsActive=1",
                new { ID = categoryId });
            if (childCount > 0)
                return (false, $"لا يمكن حذف التصنيف لوجود {childCount} تصنيف فرعي");

            var catName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT CategoryNameAr FROM dbo.ItemCategories WHERE CategoryID=@ID",
                new { ID = categoryId });

            await connection.ExecuteAsync(
                "UPDATE dbo.ItemCategories SET IsActive=0 WHERE CategoryID=@ID",
                new { ID = categoryId });

            await _audit.WriteAuditLogAsync(userId, 3, "ItemCategories", categoryId.ToString(),
                moduleName: "SCR_CATEGORIES", description: $"حذف تصنيف: {catName}");

            return (true, "تم حذف التصنيف بنجاح");
        }

        public async Task<byte[]> ExportCategoriesToExcelAsync(List<CategoryListDto> categories, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("التصنيفات");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير التصنيفات — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 7).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 7).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 7).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 7).Style.Font.FontColor = XLColor.Gray;
            ws.Range(2, 1, 2, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 4;
            var headers = new[] { "#", "الكود", "اسم التصنيف (عربي)", "اسم التصنيف (إنجليزي)",
                                  "التصنيف الأب", "المستوى", "عدد الأصناف" };
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
            foreach (var cat in categories)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = cat.CategoryCode ?? "";
                ws.Cell(row, 3).Value = (cat.CategoryLevel > 1 ? "  ↳ " : "") + (cat.CategoryNameAr ?? "");
                ws.Cell(row, 4).Value = cat.CategoryNameEn ?? "";
                ws.Cell(row, 5).Value = cat.ParentCategoryName ?? "—";
                ws.Cell(row, 6).Value = cat.CategoryLevel;
                ws.Cell(row, 7).Value = cat.ItemCount;

                for (int i = 1; i <= 7; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");
                }
                if (cat.CategoryLevel == 1)
                {
                    ws.Range(row, 1, row, 7).Style.Font.Bold = true;
                    ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#f5f3ff");
                }
                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(3).Width = 30;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "ItemCategories",
                moduleName: "SCR_CATEGORIES", description: $"تصدير {categories.Count} تصنيف إلى Excel");

            return stream.ToArray();
        }
    }
}