using Dapper;
using ClosedXML.Excel;

namespace YKCoatings.Services
{
    public class UnitService : BaseDbService
    {
        private readonly AuditService _audit;

        public UnitService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        // ==========================================
        // قائمة الوحدات
        // ==========================================
        public async Task<List<UnitListDto>> GetUnitsListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            u.UnitID,
                            u.UnitCode,
                            u.UnitNameAr,
                            u.UnitNameEn,
                            u.UnitType,
                            u.IsActive,
                            (SELECT COUNT(*) FROM dbo.Items i 
                             WHERE (i.PrimaryUnitID = u.UnitID OR i.SecondaryUnitID = u.UnitID) 
                             AND i.IsActive = 1) AS ItemCount,
                            (SELECT COUNT(*) FROM dbo.UnitConversions uc 
                             WHERE uc.FromUnitID = u.UnitID OR uc.ToUnitID = u.UnitID) AS ConversionCount
                        FROM dbo.Units u
                        WHERE u.IsActive = 1
                        ORDER BY u.UnitType, u.UnitNameAr";
            var result = await connection.QueryAsync<UnitListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب وحدة واحدة
        // ==========================================
        public async Task<UnitEditDto?> GetUnitByIdAsync(int unitId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT UnitID, UnitCode, UnitNameAr, UnitNameEn, UnitType, IsActive
                        FROM dbo.Units WHERE UnitID = @UnitID";
            return await connection.QueryFirstOrDefaultAsync<UnitEditDto>(sql, new { UnitID = unitId });
        }

        // ==========================================
        // إضافة وحدة
        // ==========================================
        public async Task<int> InsertUnitAsync(UnitEditDto unit, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.Units (UnitCode, UnitNameAr, UnitNameEn, UnitType, IsActive, CreatedDate)
                        VALUES (@UnitCode, @UnitNameAr, @UnitNameEn, @UnitType, 1, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                unit.UnitCode,
                unit.UnitNameAr,
                unit.UnitNameEn,
                unit.UnitType
            });

            await _audit.WriteAuditLogAsync(userId, 1, "Units", newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { unit.UnitCode, unit.UnitNameAr, unit.UnitType }),
                moduleName: "SCR_UNITS",
                description: $"إضافة وحدة قياس: {unit.UnitNameAr} ({unit.UnitCode})");

            return newId;
        }

        // ==========================================
        // تعديل وحدة
        // ==========================================
        public async Task UpdateUnitAsync(UnitEditDto unit, int userId)
        {
            using var connection = CreateConnection();

            var oldUnit = await connection.QueryFirstOrDefaultAsync<UnitEditDto>(
                "SELECT UnitCode, UnitNameAr, UnitNameEn, UnitType FROM dbo.Units WHERE UnitID = @UnitID",
                new { unit.UnitID });

            var sql = @"UPDATE dbo.Units SET
                            UnitCode = @UnitCode,
                            UnitNameAr = @UnitNameAr,
                            UnitNameEn = @UnitNameEn,
                            UnitType = @UnitType
                        WHERE UnitID = @UnitID";

            await connection.ExecuteAsync(sql, new
            {
                unit.UnitID,
                unit.UnitCode,
                unit.UnitNameAr,
                unit.UnitNameEn,
                unit.UnitType
            });

            var changes = new List<string>();
            if (oldUnit != null)
            {
                if (oldUnit.UnitCode != unit.UnitCode) changes.Add("UnitCode");
                if (oldUnit.UnitNameAr != unit.UnitNameAr) changes.Add("UnitNameAr");
                if (oldUnit.UnitNameEn != unit.UnitNameEn) changes.Add("UnitNameEn");
                if (oldUnit.UnitType != unit.UnitType) changes.Add("UnitType");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Units", unit.UnitID.ToString(),
                oldValues: oldUnit != null ? System.Text.Json.JsonSerializer.Serialize(new
                { oldUnit.UnitCode, oldUnit.UnitNameAr, oldUnit.UnitType }) : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { unit.UnitCode, unit.UnitNameAr, unit.UnitType }),
                changedColumns: changes.Any() ? string.Join(",", changes) : null,
                moduleName: "SCR_UNITS",
                description: $"تعديل وحدة قياس: {unit.UnitNameAr} — تغيير {changes.Count} حقل");
        }

        // ==========================================
        // حذف وحدة
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteUnitAsync(int unitId, int userId)
        {
            using var connection = CreateConnection();

            var itemCount = await connection.QueryFirstOrDefaultAsync<int>(
                @"SELECT COUNT(*) FROM dbo.Items 
                  WHERE (PrimaryUnitID = @ID OR SecondaryUnitID = @ID) AND IsActive = 1",
                new { ID = unitId });

            if (itemCount > 0)
                return (false, $"لا يمكن حذف الوحدة لوجود {itemCount} صنف مرتبط بها");

            var unitName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT UnitNameAr FROM dbo.Units WHERE UnitID = @ID",
                new { ID = unitId });

            await connection.ExecuteAsync(
                "UPDATE dbo.Units SET IsActive = 0 WHERE UnitID = @ID",
                new { ID = unitId });

            await _audit.WriteAuditLogAsync(userId, 3, "Units", unitId.ToString(),
                moduleName: "SCR_UNITS", description: $"حذف وحدة قياس: {unitName}");

            return (true, "تم حذف الوحدة بنجاح");
        }

        // ==========================================
        // تحويلات الوحدات
        // ==========================================
        public async Task<List<UnitConversionDto>> GetConversionsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            uc.ConversionID,
                            uc.FromUnitID,
                            uf.UnitNameAr AS FromUnitName,
                            uf.UnitCode AS FromUnitCode,
                            uc.ToUnitID,
                            ut.UnitNameAr AS ToUnitName,
                            ut.UnitCode AS ToUnitCode,
                            uc.ConversionFactor,
                            uc.IsActive
                        FROM dbo.UnitConversions uc
                        INNER JOIN dbo.Units uf ON uc.FromUnitID = uf.UnitID
                        INNER JOIN dbo.Units ut ON uc.ToUnitID = ut.UnitID
                        WHERE uc.IsActive = 1
                        ORDER BY uf.UnitType, uf.UnitNameAr";
            var result = await connection.QueryAsync<UnitConversionDto>(sql);
            return result.ToList();
        }

        public async Task<int> InsertConversionAsync(int fromUnitId, int toUnitId, decimal factor, int userId)
        {
            using var connection = CreateConnection();

            var exists = await connection.QueryFirstOrDefaultAsync<int>(
                @"SELECT COUNT(*) FROM dbo.UnitConversions 
                  WHERE FromUnitID = @From AND ToUnitID = @To",
                new { From = fromUnitId, To = toUnitId });

            if (exists > 0)
                throw new Exception("هذا التحويل موجود بالفعل");

            var sql = @"INSERT INTO dbo.UnitConversions (FromUnitID, ToUnitID, ConversionFactor, IsActive, CreatedDate)
                        VALUES (@FromUnitID, @ToUnitID, @Factor, 1, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                FromUnitID = fromUnitId,
                ToUnitID = toUnitId,
                Factor = factor
            });

            await _audit.WriteAuditLogAsync(userId, 1, "UnitConversions", newId.ToString(),
                moduleName: "SCR_UNITS",
                description: $"إضافة تحويل وحدة: {fromUnitId} → {toUnitId} (×{factor})");

            return newId;
        }

        public async Task DeleteConversionAsync(int conversionId, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                "DELETE FROM dbo.UnitConversions WHERE ConversionID = @ID",
                new { ID = conversionId });

            await _audit.WriteAuditLogAsync(userId, 3, "UnitConversions", conversionId.ToString(),
                moduleName: "SCR_UNITS", description: $"حذف تحويل وحدة رقم: {conversionId}");
        }

        // ==========================================
        // تصدير إلى Excel
        // ==========================================
        public async Task<byte[]> ExportUnitsToExcelAsync(List<UnitListDto> units, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("وحدات القياس");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير وحدات القياس — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 6).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 6).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 6).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 6).Style.Font.FontColor = XLColor.Gray;
            ws.Range(2, 1, 2, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 4;
            var headers = new[] { "#", "الكود", "اسم الوحدة (عربي)", "اسم الوحدة (إنجليزي)",
                                  "النوع", "عدد الأصناف" };
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
            foreach (var unit in units)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = unit.UnitCode ?? "";
                ws.Cell(row, 3).Value = unit.UnitNameAr ?? "";
                ws.Cell(row, 4).Value = unit.UnitNameEn ?? "";
                ws.Cell(row, 5).Value = GetUnitTypeName(unit.UnitType);
                ws.Cell(row, 6).Value = unit.ItemCount;

                for (int i = 1; i <= 6; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");
                }
                if (num % 2 == 0)
                    ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f7ff");
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "Units",
                moduleName: "SCR_UNITS", description: $"تصدير {units.Count} وحدة إلى Excel");

            return stream.ToArray();
        }

        public static string GetUnitTypeName(string? unitType)
        {
            return unitType switch
            {
                "Weight" => "وزن",
                "Volume" => "حجم",
                "Count" => "عدد",
                "Length" => "طول",
                _ => "أخرى"
            };
        }
    }
}