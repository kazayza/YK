using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services;

public sealed class CategoryImportService : BaseDbService
{
    private const int MaxImportRows = 500;
    private const int MaxHeaderSearchRows = 10;

    private static readonly string[] RequiredHeaders = { "CategoryCode", "CategoryNameAr" };

    public CategoryImportService(IConfiguration configuration) : base(configuration) { }

    public sealed class CatRow { public int RowNumber { get; set; } public string? CategoryCode { get; set; } public string? CategoryNameAr { get; set; } public string? CategoryNameEn { get; set; } public string? ParentCategoryCode { get; set; } public int? SortOrder { get; set; } public bool? IsActive { get; set; } public List<string> Errors { get; set; } = new(); public bool IsValid => Errors.Count == 0; }

    public async Task<(List<CatRow> rows, List<string> globalErrors)> PreviewWorkbookAsync(Stream source)
    {
        var rows = new List<CatRow>();
        var globalErrors = new List<string>();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        buffer.Position = 0;
        using var workbook = new XLWorkbook(buffer);
        var ws = workbook.Worksheets.FirstOrDefault(x => x.Name == "التصنيفات") ?? workbook.Worksheet(1);
        var lastRow = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
        if (lastRow == 0) { globalErrors.Add("الورقة فارغة."); return (rows, globalErrors); }

        int headerRow = 0; Dictionary<string,int>? headers = null;
        for (int c = 1; c <= Math.Min(lastRow, MaxHeaderSearchRows); c++)
        {
            var cur = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            int lc = ws.Row(c).LastCellUsed(XLCellsUsedOptions.Contents)?.Address.ColumnNumber ?? 0;
            for (int col = 1; col <= lc; col++)
            {
                var h = ws.Cell(c, col).GetString().Trim();
                if (h.Length == 0) continue;
                cur[h] = col;
            }
            if (RequiredHeaders.All(cur.ContainsKey)) { headerRow = c; headers = cur; break; }
        }
        if (headerRow == 0 || headers is null) { globalErrors.Add("لم أجد رؤوس القالب المطلوبة: CategoryCode و CategoryNameAr."); return (rows, globalErrors); }

        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var rowCells = ws.Row(r).Cells(1, headers.Values.Max());
            if (rowCells.All(cell => cell.IsEmpty())) continue;
            var row = new CatRow { RowNumber = r };
            string? T(string h) { if (!headers.TryGetValue(h, out var col)) return null; var cell = ws.Cell(r, col); if (cell.IsEmpty()) return null; var t = cell.GetFormattedString().Trim(); return t.Length == 0 ? null : t; }
            row.CategoryCode = T("CategoryCode");
            row.CategoryNameAr = T("CategoryNameAr");
            row.CategoryNameEn = T("CategoryNameEn");
            row.ParentCategoryCode = T("ParentCategoryCode");
            var so = T("SortOrder"); if (so != null && int.TryParse(so, out var si)) row.SortOrder = si;
            var ia = T("IsActive"); if (ia != null) row.IsActive = ia == "1" || ia.Equals("true", StringComparison.OrdinalIgnoreCase) || ia == "نعم";
            if (string.IsNullOrWhiteSpace(row.CategoryCode)) row.Errors.Add("CategoryCode مطلوب.");
            if (string.IsNullOrWhiteSpace(row.CategoryNameAr)) row.Errors.Add("CategoryNameAr مطلوب.");
            rows.Add(row);
        }
        return (rows, globalErrors);
    }

    public async Task<int> CommitAsync(List<CatRow> rows, int userId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // load existing
            var existing = (await conn.QueryAsync<(int Id, string Code, int? ParentId, int Level)>("SELECT CategoryID, CategoryCode, ParentCategoryID, CategoryLevel FROM dbo.ItemCategories", transaction: tx)).ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (var row in rows)
            {
                if (!row.IsValid) continue;
                int level = 1;
                int? parentId = null;
                if (!string.IsNullOrWhiteSpace(row.ParentCategoryCode))
                {
                    if (existing.TryGetValue(row.ParentCategoryCode!, out var p)) { parentId = p.Id; level = p.Level + 1; }
                    else
                    {
                        // maybe created earlier in this batch
                        var found = existing.FirstOrDefault(kv => kv.Value.Code.Equals(row.ParentCategoryCode, StringComparison.OrdinalIgnoreCase));
                        // fallback search by code in dict keys already covers
                    }
                }
                if (existing.TryGetValue(row.CategoryCode!, out var ex))
                {
                    await conn.ExecuteAsync("UPDATE dbo.ItemCategories SET CategoryNameAr=@n, CategoryNameEn=@e, ParentCategoryID=@p, CategoryLevel=@l, SortOrder=@s, IsActive=@a WHERE CategoryID=@id",
                        new { n = row.CategoryNameAr, e = row.CategoryNameEn, p = parentId, l = level, s = row.SortOrder ?? 0, a = row.IsActive ?? true, id = ex.Id }, tx);
                }
                else
                {
                    var newId = await conn.ExecuteScalarAsync<int>("INSERT INTO dbo.ItemCategories (CategoryCode, CategoryNameAr, CategoryNameEn, ParentCategoryID, CategoryLevel, SortOrder, IsActive, CreatedDate) VALUES (@c,@n,@e,@p,@l,@s,@a,GETDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new { c = row.CategoryCode, n = row.CategoryNameAr, e = row.CategoryNameEn, p = parentId, l = level, s = row.SortOrder ?? 0, a = row.IsActive ?? true }, tx);
                    existing[row.CategoryCode!] = (newId, row.CategoryCode!, parentId, level);
                }
                count++;
            }
            tx.Commit();
            return count;
        }
        catch { try { tx.Rollback(); } catch { } throw; }
    }
}
