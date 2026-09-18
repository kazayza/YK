using Dapper;
using ClosedXML.Excel;

namespace YKCoatings.Services
{
    public class WarehouseService : BaseDbService
    {
        private readonly AuditService _audit;

        public WarehouseService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        // ==========================================
        // قائمة المخازن
        // ==========================================
        public async Task<List<WarehouseListDto>> GetWarehousesListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            w.WarehouseID,
                            w.WarehouseCode,
                            w.WarehouseNameAr,
                            w.WarehouseNameEn,
                            w.WarehouseType,
                            w.Address,
                            w.Phone,
                            w.ManagerID,
                            e.FullNameAr AS ManagerName,
                            w.IsDefault,
                            w.IsActive
                        FROM dbo.Warehouses w
                        LEFT JOIN dbo.Employees e ON w.ManagerID = e.EmployeeID
                        WHERE w.IsActive = 1
                        ORDER BY w.WarehouseType, w.WarehouseNameAr";
            var result = await connection.QueryAsync<WarehouseListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب مخزن واحد
        // ==========================================
        public async Task<WarehouseEditDto?> GetWarehouseByIdAsync(int warehouseId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT WarehouseID, WarehouseCode, WarehouseNameAr, WarehouseNameEn,
                               WarehouseType, Address, Phone, ManagerID, IsDefault, IsActive
                        FROM dbo.Warehouses WHERE WarehouseID = @WarehouseID";
            return await connection.QueryFirstOrDefaultAsync<WarehouseEditDto>(sql, new { WarehouseID = warehouseId });
        }

        // ==========================================
        // إضافة مخزن
        // ==========================================
        public async Task<int> InsertWarehouseAsync(WarehouseEditDto wh, int userId)
        {
            using var connection = CreateConnection();

            if (wh.IsDefault)
            {
                await connection.ExecuteAsync(
                    "UPDATE dbo.Warehouses SET IsDefault = 0 WHERE IsDefault = 1");
            }

            var sql = @"INSERT INTO dbo.Warehouses 
                        (WarehouseCode, WarehouseNameAr, WarehouseNameEn, WarehouseType,
                         Address, Phone, ManagerID, IsDefault, IsActive, CreatedDate)
                        VALUES
                        (@WarehouseCode, @WarehouseNameAr, @WarehouseNameEn, @WarehouseType,
                         @Address, @Phone, NULLIF(@ManagerID, 0), @IsDefault, 1, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                wh.WarehouseCode,
                wh.WarehouseNameAr,
                wh.WarehouseNameEn,
                wh.WarehouseType,
                wh.Address,
                wh.Phone,
                wh.ManagerID,
                wh.IsDefault
            });

            await _audit.WriteAuditLogAsync(userId, 1, "Warehouses", newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { wh.WarehouseCode, wh.WarehouseNameAr, wh.WarehouseType, wh.ManagerID }),
                moduleName: "SCR_WAREHOUSES",
                description: $"إضافة مخزن جديد: {wh.WarehouseNameAr} ({wh.WarehouseCode})");

            return newId;
        }

        // ==========================================
        // تعديل مخزن
        // ==========================================
        public async Task UpdateWarehouseAsync(WarehouseEditDto wh, int userId)
        {
            using var connection = CreateConnection();

            var oldWh = await connection.QueryFirstOrDefaultAsync<WarehouseEditDto>(
                @"SELECT WarehouseCode, WarehouseNameAr, WarehouseNameEn, WarehouseType,
                         Address, Phone, ManagerID, IsDefault
                  FROM dbo.Warehouses WHERE WarehouseID = @WarehouseID",
                new { wh.WarehouseID });

            if (wh.IsDefault)
            {
                await connection.ExecuteAsync(
                    "UPDATE dbo.Warehouses SET IsDefault = 0 WHERE IsDefault = 1 AND WarehouseID != @ID",
                    new { ID = wh.WarehouseID });
            }

            var sql = @"UPDATE dbo.Warehouses SET
                            WarehouseCode = @WarehouseCode,
                            WarehouseNameAr = @WarehouseNameAr,
                            WarehouseNameEn = @WarehouseNameEn,
                            WarehouseType = @WarehouseType,
                            Address = @Address,
                            Phone = @Phone,
                            ManagerID = NULLIF(@ManagerID, 0),
                            IsDefault = @IsDefault
                        WHERE WarehouseID = @WarehouseID";

            await connection.ExecuteAsync(sql, new
            {
                wh.WarehouseID,
                wh.WarehouseCode,
                wh.WarehouseNameAr,
                wh.WarehouseNameEn,
                wh.WarehouseType,
                wh.Address,
                wh.Phone,
                wh.ManagerID,
                wh.IsDefault
            });

            var changes = new List<string>();
            if (oldWh != null)
            {
                if (oldWh.WarehouseNameAr != wh.WarehouseNameAr) changes.Add("WarehouseNameAr");
                if (oldWh.WarehouseNameEn != wh.WarehouseNameEn) changes.Add("WarehouseNameEn");
                if (oldWh.WarehouseCode != wh.WarehouseCode) changes.Add("WarehouseCode");
                if (oldWh.WarehouseType != wh.WarehouseType) changes.Add("WarehouseType");
                if (oldWh.Address != wh.Address) changes.Add("Address");
                if (oldWh.Phone != wh.Phone) changes.Add("Phone");
                if (oldWh.ManagerID != wh.ManagerID) changes.Add("ManagerID");
                if (oldWh.IsDefault != wh.IsDefault) changes.Add("IsDefault");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Warehouses", wh.WarehouseID.ToString(),
                oldValues: oldWh != null ? System.Text.Json.JsonSerializer.Serialize(new
                { oldWh.WarehouseCode, oldWh.WarehouseNameAr, oldWh.WarehouseType, oldWh.ManagerID }) : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { wh.WarehouseCode, wh.WarehouseNameAr, wh.WarehouseType, wh.ManagerID }),
                changedColumns: changes.Any() ? string.Join(",", changes) : null,
                moduleName: "SCR_WAREHOUSES",
                description: $"تعديل مخزن: {wh.WarehouseNameAr} — تغيير {changes.Count} حقل");
        }

        // ==========================================
        // حذف مخزن
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteWarehouseAsync(int warehouseId, int userId)
        {
            using var connection = CreateConnection();

            var userCount = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM dbo.SystemUsers WHERE DefaultWarehouseID = @ID",
                new { ID = warehouseId });
            if (userCount > 0)
                return (false, $"لا يمكن حذف المخزن لوجود {userCount} مستخدم مرتبط به");

            var isDefault = await connection.QueryFirstOrDefaultAsync<bool>(
                "SELECT IsDefault FROM dbo.Warehouses WHERE WarehouseID = @ID",
                new { ID = warehouseId });
            if (isDefault)
                return (false, "لا يمكن حذف المخزن الافتراضي. قم بتعيين مخزن آخر كافتراضي أولاً");

            var whName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT WarehouseNameAr FROM dbo.Warehouses WHERE WarehouseID = @ID",
                new { ID = warehouseId });

            await connection.ExecuteAsync(
                "UPDATE dbo.Warehouses SET IsActive = 0 WHERE WarehouseID = @ID",
                new { ID = warehouseId });

            await _audit.WriteAuditLogAsync(userId, 3, "Warehouses", warehouseId.ToString(),
                moduleName: "SCR_WAREHOUSES", description: $"حذف مخزن: {whName}");

            return (true, "تم حذف المخزن بنجاح");
        }

        // ==========================================
        // تصدير إلى Excel
        // ==========================================
        public async Task<byte[]> ExportWarehousesToExcelAsync(List<WarehouseListDto> warehouses, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("المخازن");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير المخازن — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 7).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 7).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 7).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 7).Style.Font.FontColor = XLColor.Gray;
            ws.Range(2, 1, 2, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 4;
            var headers = new[] { "#", "الكود", "اسم المخزن", "النوع", "المسؤول", "العنوان", "الافتراضي" };
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
            foreach (var wh in warehouses)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = wh.WarehouseCode ?? "";
                ws.Cell(row, 3).Value = wh.WarehouseNameAr ?? "";
                ws.Cell(row, 4).Value = GetWarehouseTypeName(wh.WarehouseType);
                ws.Cell(row, 5).Value = wh.ManagerName ?? "—";
                ws.Cell(row, 6).Value = wh.Address ?? "—";
                ws.Cell(row, 7).Value = wh.IsDefault ? "✅" : "";

                for (int i = 1; i <= 7; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");
                }
                if (num % 2 == 0)
                    ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f7ff");
                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(3).Width = 25;
            ws.Column(6).Width = 30;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "Warehouses",
                moduleName: "SCR_WAREHOUSES", description: $"تصدير {warehouses.Count} مخزن إلى Excel");

            return stream.ToArray();
        }

        // ==========================================
        // اسم نوع المخزن
        // ==========================================
        public static string GetWarehouseTypeName(int warehouseType)
        {
            return warehouseType switch
            {
                1 => "مواد خام",
                2 => "مواد تعبئة",
                3 => "تحت التصنيع",
                4 => "منتجات تامة",
                5 => "مرتجعات",
                6 => "عام",
                _ => "غير محدد"
            };
        }
    }
}