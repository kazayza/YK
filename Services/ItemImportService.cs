using System.Data;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services;

/// <summary>
/// Safe, preview-first Excel import for item master data. All writes are authorized here,
/// revalidated under a serializable transaction, and exclude average cost and stock balances.
/// </summary>
public sealed class ItemImportService : BaseDbService
{
    private const int MaxImportRows = 500;
    private const int MaxHeaderSearchRows = 10;

    private static readonly string[] RequiredHeaders =
    {
        "ItemCode", "ItemNameAr", "ItemType", "CategoryCode", "PrimaryUnitCode"
    };

    private readonly AuditService _audit;
    private readonly ModuleAuthorizationService _authorization;

    public ItemImportService(
        IConfiguration configuration,
        AuditService audit,
        ModuleAuthorizationService authorization) : base(configuration)
    {
        _audit = audit;
        _authorization = authorization;
    }

    public async Task<ItemImportPreview> PreviewWorkbookAsync(Stream source, int userId)
    {
        await _authorization.RequireUserPermissionAsync(
            userId, "SCR_ITEMS", p => p.CanView, "لا تملك صلاحية عرض الأصناف ومعاينة الاستيراد.");

        var preview = new ItemImportPreview();
        List<ItemImportPreviewRow> rows;
        try
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;
            using var workbook = new XLWorkbook(buffer);
            var worksheet = workbook.Worksheets.FirstOrDefault(x => x.Name == "الأصناف")
                ?? workbook.Worksheet(1);
            rows = ParseWorksheet(worksheet, preview.Errors);
        }
        catch (Exception)
        {
            preview.Errors.Add("تعذر فتح ملف Excel. استخدم ملف .xlsx مستنداً إلى قالب استيراد الأصناف.");
            return preview;
        }

        if (preview.Errors.Count > 0 || rows.Count == 0)
        {
            if (rows.Count == 0 && preview.Errors.Count == 0)
                preview.Errors.Add("لم يتم العثور على صفوف بيانات في ورقة الأصناف.");
            return preview;
        }

        preview.Rows = rows;
        AddDuplicateWorkbookCodes(preview.Rows);

        using var connection = CreateConnection();
        var categories = (await connection.QueryAsync<CategoryReference>(@"
SELECT c.CategoryID AS Id, c.CategoryCode AS Code,
       p.CategoryCode AS ParentCode, ISNULL(c.IsActive, 0) AS IsActive
FROM dbo.ItemCategories c
LEFT JOIN dbo.ItemCategories p ON p.CategoryID = c.ParentCategoryID")).ToList();

        var units = (await connection.QueryAsync<UnitReference>(@"
SELECT UnitID AS Id, UnitCode AS Code, ISNULL(IsActive, 0) AS IsActive
FROM dbo.Units")).ToList();

        var codes = rows.Select(r => NormalizeCode(r.Data.ItemCode))
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingItems = codes.Length == 0
            ? new List<ItemImportExistingItem>()
            : (await connection.QueryAsync<ItemImportExistingItem>(ExistingItemsSql, new { Codes = codes })).ToList();
        var existingByCode = existingItems
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
            .GroupBy(x => x.ItemCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in preview.Rows)
        {
            var code = NormalizeCode(row.Data.ItemCode);
            if (code is not null && existingByCode.TryGetValue(code, out var matches))
            {
                if (matches.Count > 1)
                    AddError(row, "يوجد أكثر من صنف في النظام بهذا الكود؛ لا يمكن تحديثه آلياً.");
                else
                {
                    row.ExistingItem = matches[0];
                    row.Action = ItemImportAction.Skip;
                }
            }
            else
            {
                row.Action = ItemImportAction.Add;
            }

            ValidateAndDescribe(row, categories, units);
        }

        return preview;
    }

    public async Task<ItemImportCommitResult> CommitAsync(
        IReadOnlyCollection<ItemImportPreviewRow> rows,
        int userId)
    {
        if (rows is null || rows.Count == 0)
            throw new InvalidOperationException("لا توجد صفوف لإتمام الاستيراد.");

        if (rows.Count > MaxImportRows)
            throw new InvalidOperationException($"الحد الأقصى للاستيراد هو {MaxImportRows} صفاً.");

        var selected = rows.Where(r => r.Action != ItemImportAction.Skip && r.IsValid).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("لم يتم اختيار أي صف صالح للإضافة أو التحديث.");

        if (selected.Any(r => r.Action == ItemImportAction.Add))
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_ITEMS", p => p.CanAdd, "لا تملك صلاحية إضافة أصناف.");

        if (selected.Any(r => r.Action == ItemImportAction.Update))
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_ITEMS", p => p.CanEdit, "لا تملك صلاحية تحديث الأصناف الموجودة.");

        var selectedCodes = selected.Select(r => NormalizeCode(r.Data.ItemCode)
                ?? throw new InvalidOperationException($"صف {r.RowNumber}: كود الصنف مطلوب."))
            .ToArray();
        if (selectedCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedCodes.Length)
            throw new InvalidOperationException("يوجد كود مكرر ضمن الصفوف المختارة؛ أعد المعاينة وصحح الملف.");

        using var connection = CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var auditEntries = new List<ImportAuditEntry>();
        var result = new ItemImportCommitResult { Skipped = rows.Count - selected.Count };

        try
        {
            var categories = (await connection.QueryAsync<CategoryReference>(@"
SELECT c.CategoryID AS Id, c.CategoryCode AS Code,
       p.CategoryCode AS ParentCode, ISNULL(c.IsActive, 0) AS IsActive
FROM dbo.ItemCategories c
LEFT JOIN dbo.ItemCategories p ON p.CategoryID = c.ParentCategoryID",
                transaction: transaction)).ToList();

            var units = (await connection.QueryAsync<UnitReference>(@"
SELECT UnitID AS Id, UnitCode AS Code, ISNULL(IsActive, 0) AS IsActive
FROM dbo.Units",
                transaction: transaction)).ToList();

            var freshItems = (await connection.QueryAsync<ItemImportExistingItem>(
                ExistingItemsSql.Replace("FROM dbo.Items i", "FROM dbo.Items i WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal),
                new { Codes = selectedCodes },
                transaction: transaction)).ToList();
            var freshByCode = freshItems
                .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
                .GroupBy(x => x.ItemCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in selected)
            {
                var code = NormalizeCode(row.Data.ItemCode)
                    ?? throw new InvalidOperationException($"صف {row.RowNumber}: كود الصنف مطلوب.");
                if (row.Action == ItemImportAction.Add && row.ExistingItem is not null)
                    throw new InvalidOperationException($"صف {row.RowNumber}: كان هذا الكود متعارضاً في المعاينة؛ أعد المعاينة واختر التحديث أو التخطي.");
                if (row.Action == ItemImportAction.Update && row.ExistingItem is null)
                    throw new InvalidOperationException($"صف {row.RowNumber}: لم يظهر هذا الكود كتعارض في المعاينة؛ أعد المعاينة.");

                var matches = freshByCode.TryGetValue(code, out var found) ? found : new List<ItemImportExistingItem>();

                if (matches.Count > 1)
                    throw new InvalidOperationException($"صف {row.RowNumber}: الكود {code} مكرر في قاعدة البيانات؛ أُلغي الاستيراد بالكامل.");

                var existing = matches.SingleOrDefault();
                if (row.Action == ItemImportAction.Add && existing is not null)
                    throw new InvalidOperationException($"صف {row.RowNumber}: أصبح الكود {code} مستخدماً منذ المعاينة. أعد المعاينة قبل الحفظ.");
                if (row.Action == ItemImportAction.Update && existing is null)
                    throw new InvalidOperationException($"صف {row.RowNumber}: لم يعد الكود {code} موجوداً منذ المعاينة. أعد المعاينة.");
                if (row.Action == ItemImportAction.Update && (row.ExistingItem is null
                    || row.ExistingItem.ItemID != existing!.ItemID
                    || BuildDifferences(row.ExistingItem!, existing!).Count > 0))
                    throw new InvalidOperationException($"صف {row.RowNumber}: تغير سجل الكود {code} منذ المعاينة. أعد المعاينة لمراجعة القيم الحالية.");

                var errors = ValidateData(row.Data, existing, categories, units, out var candidate);
                if (errors.Count > 0 || candidate is null)
                    throw new InvalidOperationException($"صف {row.RowNumber}: {string.Join(" ؛ ", errors)}");

                if (row.Action == ItemImportAction.Add)
                {
                    var newId = await connection.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.Items
(
    ItemCode, Barcode, ItemNameAr, ItemNameEn, CategoryID, ItemType,
    PrimaryUnitID, SecondaryUnitID, ConversionFactor,
    MinStockLevel, MaxStockLevel, ReorderLevel, ShelfLifeDays,
    StandardCost, LastPurchasePrice, AverageCost, DefaultSellingPrice,
    TaxRate, IsTaxable, Weight, Volume, Description, Notes, ImagePath,
    IsActive, CreatedBy, CreatedDate
)
VALUES
(
    @ItemCode, @Barcode, @ItemNameAr, @ItemNameEn, @CategoryID, @ItemType,
    @PrimaryUnitID, NULLIF(@SecondaryUnitID, 0), @ConversionFactor,
    @MinStockLevel, @MaxStockLevel, @ReorderLevel, @ShelfLifeDays,
    @StandardCost, @LastPurchasePrice, 0, @DefaultSellingPrice,
    @TaxRate, @IsTaxable, @Weight, @Volume, @Description, @Notes, NULL,
    @IsActive, @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        BuildSqlParameters(candidate, userId), transaction);

                    result.Added++;
                    auditEntries.Add(new ImportAuditEntry(newId, true, null, candidate));
                }
                else if (row.Action == ItemImportAction.Update)
                {
                    var affected = await connection.ExecuteAsync(@"
UPDATE dbo.Items SET
    Barcode=@Barcode, ItemNameAr=@ItemNameAr, ItemNameEn=@ItemNameEn,
    CategoryID=@CategoryID, ItemType=@ItemType, PrimaryUnitID=@PrimaryUnitID,
    SecondaryUnitID=NULLIF(@SecondaryUnitID,0), ConversionFactor=@ConversionFactor,
    MinStockLevel=@MinStockLevel, MaxStockLevel=@MaxStockLevel,
    ReorderLevel=@ReorderLevel, ShelfLifeDays=@ShelfLifeDays,
    StandardCost=@StandardCost, LastPurchasePrice=@LastPurchasePrice,
    DefaultSellingPrice=@DefaultSellingPrice, TaxRate=@TaxRate, IsTaxable=@IsTaxable,
    Weight=@Weight, Volume=@Volume, Description=@Description, Notes=@Notes,
    IsActive=@IsActive, ModifiedBy=@UserID, ModifiedDate=GETDATE()
WHERE ItemID=@ItemID",
                        BuildSqlParameters(candidate, userId, existing!.ItemID), transaction);

                    if (affected != 1)
                        throw new InvalidOperationException($"تعذر تحديث الصنف {code}؛ أُلغي الاستيراد بالكامل.");

                    candidate.ItemID = existing.ItemID;
                    result.Updated++;
                    auditEntries.Add(new ImportAuditEntry(existing.ItemID, false, existing, candidate));
                }
                else
                {
                    throw new InvalidOperationException($"صف {row.RowNumber}: قرار الاستيراد غير صالح.");
                }
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }

        foreach (var entry in auditEntries)
        {
            var oldValues = entry.Existing is null ? null : SerializeAuditValues(entry.Existing);
            var newValues = SerializeAuditValues(entry.Current);
            var changed = entry.Existing is null
                ? null
                : string.Join(",", BuildDifferences(entry.Existing, entry.Current).Select(x => x.FieldNameAr));

            await _audit.WriteAuditLogAsync(
                userId,
                entry.IsNew ? (byte)1 : (byte)2,
                "Items",
                entry.ItemID.ToString(CultureInfo.InvariantCulture),
                oldValues,
                newValues,
                changed,
                "SCR_ITEMS",
                entry.IsNew
                    ? $"استيراد صنف جديد: {entry.Current.ItemNameAr} ({entry.Current.ItemCode})"
                    : $"تحديث صنف من استيراد Excel: {entry.Current.ItemNameAr} ({entry.Current.ItemCode})");
        }

        return result;
    }

    private static List<ItemImportPreviewRow> ParseWorksheet(IXLWorksheet worksheet, List<string> globalErrors)
    {
        var lastRow = worksheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
        if (lastRow == 0)
        {
            globalErrors.Add("ورقة الأصناف فارغة.");
            return new List<ItemImportPreviewRow>();
        }

        var headerRow = 0;
        Dictionary<string, int>? headers = null;
        for (var candidateRow = 1; candidateRow <= Math.Min(lastRow, MaxHeaderSearchRows); candidateRow++)
        {
            var current = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var lastColumn = worksheet.Row(candidateRow).LastCellUsed(XLCellsUsedOptions.Contents)?.Address.ColumnNumber ?? 0;
            for (var column = 1; column <= lastColumn; column++)
            {
                var header = worksheet.Cell(candidateRow, column).GetString().Trim();
                if (header.Length == 0)
                    continue;
                if (current.ContainsKey(header))
                {
                    globalErrors.Add($"رأس العمود «{header}» مكرر في الصف {candidateRow}.");
                    return new List<ItemImportPreviewRow>();
                }
                current[header] = column;
            }

            if (RequiredHeaders.All(current.ContainsKey))
            {
                headerRow = candidateRow;
                headers = current;
                break;
            }
        }

        if (headerRow == 0 || headers is null)
        {
            globalErrors.Add("لم أجد رؤوس القالب المطلوبة: ItemCode وItemNameAr وItemType وCategoryCode وPrimaryUnitCode.");
            return new List<ItemImportPreviewRow>();
        }

        if (lastRow - headerRow > MaxImportRows)
        {
            globalErrors.Add($"عدد صفوف البيانات يتجاوز الحد المسموح ({MaxImportRows}).");
            return new List<ItemImportPreviewRow>();
        }

        var lastColumnUsed = headers.Values.Max();
        var result = new List<ItemImportPreviewRow>();
        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var rowCells = worksheet.Row(rowNumber).Cells(1, lastColumnUsed);
            if (rowCells.All(cell => cell.IsEmpty()))
                continue;

            var row = new ItemImportPreviewRow { RowNumber = rowNumber };
            row.Data = ParseData(worksheet, rowNumber, headers, row.Errors);
            result.Add(row);
        }

        return result;
    }

    private static ItemImportData ParseData(
        IXLWorksheet worksheet,
        int rowNumber,
        IReadOnlyDictionary<string, int> headers,
        List<string> errors)
    {
        string? Text(string header)
        {
            if (!headers.TryGetValue(header, out var column))
                return null;
            var cell = worksheet.Cell(rowNumber, column);
            if (cell.HasFormula)
            {
                AddError(errors, $"الحقل {header}: المعادلات غير مسموحة.");
                return null;
            }
            if (cell.IsEmpty())
                return null;
            var text = cell.GetFormattedString().Trim();
            return text.Length == 0 ? null : text;
        }

        decimal? Number(string header)
        {
            if (!headers.TryGetValue(header, out var column))
                return null;
            var cell = worksheet.Cell(rowNumber, column);
            if (cell.HasFormula)
            {
                AddError(errors, $"الحقل {header}: المعادلات غير مسموحة.");
                return null;
            }
            if (cell.IsEmpty())
                return null;

            if (cell.DataType == XLDataType.Number)
                return Convert.ToDecimal(cell.GetDouble(), CultureInfo.InvariantCulture);

            var value = cell.GetFormattedString().Trim();
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
                || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            AddError(errors, $"الحقل {header}: أدخل رقماً صالحاً في الصف {rowNumber}.");
            return null;
        }

        int? WholeNumber(string header)
        {
            var number = Number(header);
            if (!number.HasValue)
                return null;
            if (decimal.Truncate(number.Value) != number.Value || number.Value < int.MinValue || number.Value > int.MaxValue)
            {
                AddError(errors, $"الحقل {header}: يجب أن يكون رقماً صحيحاً في الصف {rowNumber}.");
                return null;
            }
            return decimal.ToInt32(number.Value);
        }

        bool? Flag(string header)
        {
            if (!headers.TryGetValue(header, out var column))
                return null;
            var cell = worksheet.Cell(rowNumber, column);
            if (cell.HasFormula)
            {
                AddError(errors, $"الحقل {header}: المعادلات غير مسموحة.");
                return null;
            }
            if (cell.IsEmpty())
                return null;

            if (cell.DataType == XLDataType.Boolean)
                return cell.GetBoolean();

            var value = cell.GetFormattedString().Trim();
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "نعم")
                return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "لا")
                return false;

            AddError(errors, $"الحقل {header}: استخدم 1 أو 0.");
            return null;
        }

        return new ItemImportData
        {
            ItemCode = Text("ItemCode"),
            Barcode = Text("Barcode"),
            ItemNameAr = Text("ItemNameAr"),
            ItemNameEn = Text("ItemNameEn"),
            ItemType = WholeNumber("ItemType"),
            CategoryCode = Text("CategoryCode"),
            PrimaryUnitCode = Text("PrimaryUnitCode"),
            SecondaryUnitCode = Text("SecondaryUnitCode"),
            ConversionFactor = Number("ConversionFactor"),
            MinStockLevel = Number("MinStockLevel"),
            MaxStockLevel = Number("MaxStockLevel"),
            ReorderLevel = Number("ReorderLevel"),
            ShelfLifeDays = WholeNumber("ShelfLifeDays"),
            StandardCost = Number("StandardCost"),
            LastPurchasePrice = Number("LastPurchasePrice"),
            DefaultSellingPrice = Number("DefaultSellingPrice"),
            TaxRate = Number("TaxRate"),
            IsTaxable = Flag("IsTaxable"),
            Weight = Number("Weight"),
            Volume = Number("Volume"),
            Description = Text("Description"),
            Notes = Text("Notes"),
            IsActive = Flag("IsActive")
        };
    }

    private static void AddDuplicateWorkbookCodes(List<ItemImportPreviewRow> rows)
    {
        var duplicates = rows
            .Select(r => new { Row = r, Code = NormalizeCode(r.Data.ItemCode) })
            .Where(x => x.Code is not null)
            .GroupBy(x => x.Code!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicates)
        {
            foreach (var entry in group)
                AddError(entry.Row, $"كود الصنف {entry.Code} مكرر داخل الملف؛ صحح التكرار قبل الاستيراد.");
        }
    }

    private static void ValidateAndDescribe(
        ItemImportPreviewRow row,
        IReadOnlyCollection<CategoryReference> categories,
        IReadOnlyCollection<UnitReference> units)
    {
        var errors = ValidateData(row.Data, row.ExistingItem, categories, units, out var candidate);
        foreach (var error in errors)
            AddError(row, error);

        if (candidate is not null && row.ExistingItem is not null)
            row.Differences = BuildDifferences(row.ExistingItem, candidate);
    }

    private static List<string> ValidateData(
        ItemImportData data,
        ItemImportExistingItem? existing,
        IReadOnlyCollection<CategoryReference> categories,
        IReadOnlyCollection<UnitReference> units,
        out ItemImportExistingItem? candidate)
    {
        var errors = new List<string>();
        candidate = null;

        var code = NormalizeCode(data.ItemCode);
        if (code is null)
            errors.Add("كود الصنف مطلوب.");
        if (string.IsNullOrWhiteSpace(data.ItemNameAr))
            errors.Add("اسم الصنف بالعربية مطلوب.");
        if (!data.ItemType.HasValue || data.ItemType.Value is < 1 or > 4)
            errors.Add("نوع الصنف يجب أن يكون 1 أو 2 أو 3 أو 4.");
        if (string.IsNullOrWhiteSpace(data.CategoryCode))
            errors.Add("CategoryCode مطلوب.");
        if (string.IsNullOrWhiteSpace(data.PrimaryUnitCode))
            errors.Add("PrimaryUnitCode مطلوب.");

        var category = FindUniqueByCode(categories, data.CategoryCode, x => x.Code, "التصنيف", errors);
        var primaryUnit = FindUniqueByCode(units, data.PrimaryUnitCode, x => x.Code, "الوحدة الأساسية", errors);
        UnitReference? secondaryUnit = null;
        if (!string.IsNullOrWhiteSpace(data.SecondaryUnitCode))
            secondaryUnit = FindUniqueByCode(units, data.SecondaryUnitCode, x => x.Code, "الوحدة الثانوية", errors);

        if (category is not null)
        {
            if (!category.IsActive && existing?.CategoryID != category.Id)
                errors.Add("التصنيف غير نشط؛ لا يمكن تعيينه لصنف جديد أو تغيير تصنيف الصنف إليه.");

            if (data.ItemType.HasValue)
            {
                var expectedParent = data.ItemType.Value switch
                {
                    1 => "RAW",
                    2 => "PKG",
                    3 => "SEMI",
                    4 => "FIN",
                    _ => null
                };
                if (expectedParent is not null
                    && !string.Equals(category.ParentCode, expectedParent, StringComparison.OrdinalIgnoreCase))
                    errors.Add("كود التصنيف لا يتوافق مع نوع الصنف المحدد.");
            }
        }

        if (primaryUnit is not null && !primaryUnit.IsActive && existing?.PrimaryUnitID != primaryUnit.Id)
            errors.Add("الوحدة الأساسية غير نشطة؛ لا يمكن تعيينها لصنف جديد أو تغيير وحدة الصنف إليها.");
        if (secondaryUnit is not null && !secondaryUnit.IsActive && existing?.SecondaryUnitID != secondaryUnit.Id)
            errors.Add("الوحدة الثانوية غير نشطة؛ لا يمكن تعيينها لصنف جديد أو تغيير وحدة الصنف إليها.");

        ValidateNonNegative(data.ConversionFactor, "ConversionFactor", errors);
        ValidateNonNegative(data.MinStockLevel, "MinStockLevel", errors);
        ValidateNonNegative(data.MaxStockLevel, "MaxStockLevel", errors);
        ValidateNonNegative(data.ReorderLevel, "ReorderLevel", errors);
        ValidateNonNegative(data.StandardCost, "StandardCost", errors);
        ValidateNonNegative(data.LastPurchasePrice, "LastPurchasePrice", errors);
        ValidateNonNegative(data.DefaultSellingPrice, "DefaultSellingPrice", errors);
        ValidateNonNegative(data.TaxRate, "TaxRate", errors);
        ValidateNonNegative(data.Weight, "Weight", errors);
        ValidateNonNegative(data.Volume, "Volume", errors);
        if (data.ShelfLifeDays.HasValue && data.ShelfLifeDays.Value < 0)
            errors.Add("ShelfLifeDays لا يمكن أن يكون سالباً.");

        if (data.SecondaryUnitCode is not null && secondaryUnit is not null)
        {
            var secondaryChanged = existing is null || existing.SecondaryUnitID != secondaryUnit.Id;
            if (secondaryChanged && !data.ConversionFactor.HasValue)
                errors.Add("عند تعيين وحدة ثانوية جديدة أدخل ConversionFactor أيضاً.");
            var factor = data.ConversionFactor ?? (secondaryChanged ? 0 : existing?.ConversionFactor ?? 0);
            if (factor <= 0)
                errors.Add("ConversionFactor يجب أن يكون أكبر من صفر عند استخدام وحدة ثانوية.");
        }
        else if (data.ConversionFactor.HasValue)
        {
            var hasSecondaryUnit = (existing?.SecondaryUnitID ?? 0) > 0;
            if (!hasSecondaryUnit && data.ConversionFactor.Value > 0)
                errors.Add("لا يمكن إدخال ConversionFactor دون تحديد SecondaryUnitCode.");
            else if (hasSecondaryUnit && data.ConversionFactor.Value <= 0)
                errors.Add("ConversionFactor يجب أن يكون أكبر من صفر عند وجود وحدة ثانوية.");
        }

        if (errors.Count > 0 || category is null || primaryUnit is null || !data.ItemType.HasValue || code is null)
            return errors;

        var secondaryId = data.SecondaryUnitCode is not null
            ? secondaryUnit?.Id ?? 0
            : existing?.SecondaryUnitID ?? 0;

        candidate = new ItemImportExistingItem
        {
            ItemID = existing?.ItemID ?? 0,
            ItemCode = existing?.ItemCode ?? code,
            Barcode = data.Barcode ?? existing?.Barcode,
            ItemNameAr = data.ItemNameAr!.Trim(),
            ItemNameEn = data.ItemNameEn ?? existing?.ItemNameEn,
            CategoryID = category.Id,
            CategoryCode = category.Code,
            ItemType = data.ItemType.Value,
            PrimaryUnitID = primaryUnit.Id,
            PrimaryUnitCode = primaryUnit.Code,
            SecondaryUnitID = secondaryId == 0 ? null : secondaryId,
            SecondaryUnitCode = data.SecondaryUnitCode is not null ? secondaryUnit?.Code : existing?.SecondaryUnitCode,
            ConversionFactor = data.ConversionFactor ?? existing?.ConversionFactor ?? 0,
            MinStockLevel = data.MinStockLevel ?? existing?.MinStockLevel ?? 0,
            MaxStockLevel = data.MaxStockLevel ?? existing?.MaxStockLevel ?? 0,
            ReorderLevel = data.ReorderLevel ?? existing?.ReorderLevel ?? 0,
            ShelfLifeDays = data.ShelfLifeDays ?? existing?.ShelfLifeDays ?? 0,
            StandardCost = data.StandardCost ?? existing?.StandardCost ?? 0,
            LastPurchasePrice = data.LastPurchasePrice ?? existing?.LastPurchasePrice ?? 0,
            AverageCost = existing?.AverageCost ?? 0,
            DefaultSellingPrice = data.DefaultSellingPrice ?? existing?.DefaultSellingPrice ?? 0,
            TaxRate = data.TaxRate ?? existing?.TaxRate ?? 14,
            IsTaxable = data.IsTaxable ?? existing?.IsTaxable ?? true,
            Weight = data.Weight ?? existing?.Weight ?? 0,
            Volume = data.Volume ?? existing?.Volume ?? 0,
            Description = data.Description ?? existing?.Description,
            Notes = data.Notes ?? existing?.Notes,
            IsActive = data.IsActive ?? existing?.IsActive ?? true
        };

        if (candidate.ItemType == 4 && candidate.DefaultSellingPrice <= 0)
            errors.Add("المنتج التام يحتاج إلى DefaultSellingPrice أكبر من صفر.");

        return errors;
    }

    private static T? FindUniqueByCode<T>(
        IEnumerable<T> values,
        string? code,
        Func<T, string?> selector,
        string label,
        List<string> errors) where T : class
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var matches = values.Where(x => string.Equals(selector(x)?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            errors.Add($"{label}: الكود {code.Trim()} غير موجود.");
            return null;
        }
        if (matches.Count > 1)
        {
            errors.Add($"{label}: الكود {code.Trim()} غير فريد.");
            return null;
        }
        return matches[0];
    }

    private static void ValidateNonNegative(decimal? value, string fieldName, List<string> errors)
    {
        if (value.HasValue && value.Value < 0)
            errors.Add($"{fieldName} لا يمكن أن يكون سالباً.");
    }

    private static List<ItemImportDifference> BuildDifferences(
        ItemImportExistingItem oldItem,
        ItemImportExistingItem newItem)
    {
        var differences = new List<ItemImportDifference>();
        void Compare(string label, string? oldValue, string? newValue)
        {
            if (!string.Equals(oldValue ?? "", newValue ?? "", StringComparison.Ordinal))
                differences.Add(new ItemImportDifference { FieldNameAr = label, ExistingValue = Show(oldValue), IncomingValue = Show(newValue) });
        }
        void CompareDecimal(string label, decimal oldValue, decimal newValue)
        {
            if (oldValue != newValue)
                differences.Add(new ItemImportDifference { FieldNameAr = label, ExistingValue = oldValue.ToString("0.####", CultureInfo.InvariantCulture), IncomingValue = newValue.ToString("0.####", CultureInfo.InvariantCulture) });
        }
        void CompareBoolean(string label, bool oldValue, bool newValue)
        {
            if (oldValue != newValue)
                differences.Add(new ItemImportDifference { FieldNameAr = label, ExistingValue = oldValue ? "نعم" : "لا", IncomingValue = newValue ? "نعم" : "لا" });
        }

        Compare("الباركود", oldItem.Barcode, newItem.Barcode);
        Compare("الاسم العربي", oldItem.ItemNameAr, newItem.ItemNameAr);
        Compare("الاسم الإنجليزي", oldItem.ItemNameEn, newItem.ItemNameEn);
        Compare("نوع الصنف", TypeName(oldItem.ItemType), TypeName(newItem.ItemType));
        Compare("كود التصنيف", oldItem.CategoryCode, newItem.CategoryCode);
        Compare("الوحدة الأساسية", oldItem.PrimaryUnitCode, newItem.PrimaryUnitCode);
        Compare("الوحدة الثانوية", oldItem.SecondaryUnitCode, newItem.SecondaryUnitCode);
        CompareDecimal("معامل التحويل", oldItem.ConversionFactor, newItem.ConversionFactor);
        CompareDecimal("الحد الأدنى للمخزون", oldItem.MinStockLevel, newItem.MinStockLevel);
        CompareDecimal("الحد الأقصى للمخزون", oldItem.MaxStockLevel, newItem.MaxStockLevel);
        CompareDecimal("حد إعادة الطلب", oldItem.ReorderLevel, newItem.ReorderLevel);
        Compare("مدة الصلاحية بالأيام", oldItem.ShelfLifeDays.ToString(CultureInfo.InvariantCulture), newItem.ShelfLifeDays.ToString(CultureInfo.InvariantCulture));
        CompareDecimal("التكلفة المعيارية", oldItem.StandardCost, newItem.StandardCost);
        CompareDecimal("آخر سعر شراء", oldItem.LastPurchasePrice, newItem.LastPurchasePrice);
        CompareDecimal("سعر البيع الافتراضي", oldItem.DefaultSellingPrice, newItem.DefaultSellingPrice);
        CompareDecimal("نسبة الضريبة", oldItem.TaxRate, newItem.TaxRate);
        CompareBoolean("خاضع للضريبة", oldItem.IsTaxable, newItem.IsTaxable);
        CompareDecimal("الوزن", oldItem.Weight, newItem.Weight);
        CompareDecimal("الحجم", oldItem.Volume, newItem.Volume);
        Compare("الوصف", oldItem.Description, newItem.Description);
        Compare("الملاحظات", oldItem.Notes, newItem.Notes);
        CompareBoolean("نشط", oldItem.IsActive, newItem.IsActive);
        return differences;
    }

    private static object BuildSqlParameters(ItemImportExistingItem item, int userId, int itemId = 0) => new
    {
        item.ItemCode,
        item.Barcode,
        item.ItemNameAr,
        item.ItemNameEn,
        item.CategoryID,
        item.ItemType,
        item.PrimaryUnitID,
        SecondaryUnitID = item.SecondaryUnitID ?? 0,
        item.ConversionFactor,
        item.MinStockLevel,
        item.MaxStockLevel,
        item.ReorderLevel,
        item.ShelfLifeDays,
        item.StandardCost,
        item.LastPurchasePrice,
        item.DefaultSellingPrice,
        item.TaxRate,
        item.IsTaxable,
        item.Weight,
        item.Volume,
        item.Description,
        item.Notes,
        item.IsActive,
        UserID = userId,
        ItemID = itemId
    };

    private static string SerializeAuditValues(ItemImportExistingItem item) => JsonSerializer.Serialize(new
    {
        item.ItemCode,
        item.Barcode,
        item.ItemNameAr,
        item.ItemNameEn,
        item.CategoryCode,
        item.ItemType,
        item.PrimaryUnitCode,
        item.SecondaryUnitCode,
        item.ConversionFactor,
        item.StandardCost,
        item.LastPurchasePrice,
        item.DefaultSellingPrice,
        item.TaxRate,
        item.IsTaxable,
        item.IsActive
    });

    private static string? NormalizeCode(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string TypeName(int type) => type switch
    {
        1 => "مادة خام",
        2 => "مادة تعبئة",
        3 => "نصف مصنع",
        4 => "منتج تام",
        _ => "غير محدد"
    };

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static void AddError(ItemImportPreviewRow row, string error)
    {
        if (!row.Errors.Contains(error, StringComparer.Ordinal))
            row.Errors.Add(error);
    }

    private static void AddError(List<string> errors, string error)
    {
        if (!errors.Contains(error, StringComparer.Ordinal))
            errors.Add(error);
    }

    public sealed class CategoryReference
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? ParentCode { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class UnitReference
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed record ImportAuditEntry(
        int ItemID,
        bool IsNew,
        ItemImportExistingItem? Existing,
        ItemImportExistingItem Current);

    private const string ExistingItemsSql = @"
SELECT i.ItemID, i.ItemCode, i.Barcode, i.ItemNameAr, i.ItemNameEn,
       i.CategoryID, c.CategoryCode,
       i.ItemType, i.PrimaryUnitID, pu.UnitCode AS PrimaryUnitCode,
       i.SecondaryUnitID, su.UnitCode AS SecondaryUnitCode,
       ISNULL(i.ConversionFactor, 0) AS ConversionFactor,
       ISNULL(i.MinStockLevel, 0) AS MinStockLevel,
       ISNULL(i.MaxStockLevel, 0) AS MaxStockLevel,
       ISNULL(i.ReorderLevel, 0) AS ReorderLevel,
       ISNULL(i.ShelfLifeDays, 0) AS ShelfLifeDays,
       ISNULL(i.StandardCost, 0) AS StandardCost,
       ISNULL(i.LastPurchasePrice, 0) AS LastPurchasePrice,
       ISNULL(i.AverageCost, 0) AS AverageCost,
       ISNULL(i.DefaultSellingPrice, 0) AS DefaultSellingPrice,
       ISNULL(i.TaxRate, 0) AS TaxRate,
       ISNULL(i.IsTaxable, 0) AS IsTaxable,
       ISNULL(i.Weight, 0) AS Weight,
       ISNULL(i.Volume, 0) AS Volume,
       i.Description, i.Notes, ISNULL(i.IsActive, 1) AS IsActive
FROM dbo.Items i
LEFT JOIN dbo.ItemCategories c ON c.CategoryID = i.CategoryID
LEFT JOIN dbo.Units pu ON pu.UnitID = i.PrimaryUnitID
LEFT JOIN dbo.Units su ON su.UnitID = i.SecondaryUnitID
WHERE i.ItemCode IN @Codes";
}
