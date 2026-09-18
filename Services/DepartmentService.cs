using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class DepartmentService : BaseDbService
    {
        private readonly AuditService _audit;

        public DepartmentService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        // ==========================================
        // قائمة الأقسام
        // ==========================================
        public async Task<List<DepartmentListDto>> GetDepartmentsListAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    d.DepartmentID,
                    d.DepartmentCode,
                    d.DepartmentNameAr,
                    d.DepartmentNameEn,
                    pd.DepartmentNameAr AS ParentDepartmentName,
                    mgr.FullNameAr AS ManagerName,
                    d.Phone,
                    ISNULL(emp.Count, 0) AS EmployeeCount,
                    d.IsActive
                FROM dbo.Departments d
                LEFT JOIN Departments pd ON d.ParentDepartmentID = pd.DepartmentID
                LEFT JOIN Employees mgr ON d.ManagerEmployeeID = mgr.EmployeeID
                LEFT JOIN (
                    SELECT DepartmentID, COUNT(*) AS Count
                    FROM Employees
                    WHERE IsActive = 1 AND EmployeeStatus = 1
                    GROUP BY DepartmentID
                ) emp ON d.DepartmentID = emp.DepartmentID
                ORDER BY d.SortOrder, d.DepartmentNameAr";

            var result = await connection.QueryAsync<DepartmentListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // بيانات قسم واحد
        // ==========================================
        public async Task<DepartmentEditDto?> GetDepartmentByIdAsync(int departmentId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT DepartmentID, DepartmentCode, DepartmentNameAr, DepartmentNameEn,
                       ParentDepartmentID, ManagerEmployeeID, Phone,
                       CostCenterCode, SortOrder, IsActive
                FROM dbo.Departments
                WHERE DepartmentID = @ID";

            return await connection.QueryFirstOrDefaultAsync<DepartmentEditDto>(sql, new { ID = departmentId });
        }

        // ==========================================
        // إضافة قسم
        // ==========================================
        public async Task<int> InsertDepartmentAsync(DepartmentEditDto dept, int userId)
        {
            using var connection = CreateConnection();

            if (string.IsNullOrWhiteSpace(dept.DepartmentNameAr))
                throw new Exception("اسم القسم بالعربي مطلوب");

            var code = dept.DepartmentCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                var seq = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT ISNULL(MAX(CAST(DepartmentCode AS INT)), 0) + 1
                    FROM Departments
                    WHERE ISNUMERIC(DepartmentCode) = 1");
                code = seq.ToString("D4");
            }

            var sql = @"
                INSERT INTO dbo.Departments
                    (DepartmentCode, DepartmentNameAr, DepartmentNameEn,
                     ParentDepartmentID, ManagerEmployeeID, Phone,
                     CostCenterCode, SortOrder, IsActive, CreatedDate)
                VALUES
                    (@DepartmentCode, @DepartmentNameAr, @DepartmentNameEn,
                     @ParentDepartmentID, @ManagerEmployeeID, @Phone,
                     @CostCenterCode, @SortOrder, @IsActive, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                DepartmentCode = code,
                dept.DepartmentNameAr,
                dept.DepartmentNameEn,
                dept.ParentDepartmentID,
                dept.ManagerEmployeeID,
                dept.Phone,
                dept.CostCenterCode,
                dept.SortOrder,
                dept.IsActive
            });

            await _audit.WriteAuditLogAsync(
                userId, 1, "Departments", newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                {
                    code,
                    dept.DepartmentNameAr
                }),
                moduleName: "SCR_DEPT",
                description: $"إضافة قسم: {dept.DepartmentNameAr}");

            return newId;
        }

        // ==========================================
        // تعديل قسم
        // ==========================================
        public async Task UpdateDepartmentAsync(DepartmentEditDto dept, int userId)
        {
            using var connection = CreateConnection();

            if (string.IsNullOrWhiteSpace(dept.DepartmentNameAr))
                throw new Exception("اسم القسم بالعربي مطلوب");

            var old = await connection.QueryFirstOrDefaultAsync<DepartmentEditDto>(
                @"SELECT DepartmentNameAr, ParentDepartmentID, ManagerEmployeeID, IsActive
                  FROM dbo.Departments WHERE DepartmentID = @ID",
                new { ID = dept.DepartmentID });

            var sql = @"
                UPDATE dbo.Departments
                SET DepartmentNameAr   = @DepartmentNameAr,
                    DepartmentNameEn   = @DepartmentNameEn,
                    ParentDepartmentID = @ParentDepartmentID,
                    ManagerEmployeeID  = @ManagerEmployeeID,
                    Phone              = @Phone,
                    CostCenterCode     = @CostCenterCode,
                    SortOrder          = @SortOrder,
                    IsActive           = @IsActive,
                    ModifiedDate       = GETDATE()
                WHERE DepartmentID = @DepartmentID";

            await connection.ExecuteAsync(sql, new
            {
                dept.DepartmentID,
                dept.DepartmentNameAr,
                dept.DepartmentNameEn,
                dept.ParentDepartmentID,
                dept.ManagerEmployeeID,
                dept.Phone,
                dept.CostCenterCode,
                dept.SortOrder,
                dept.IsActive
            });

            await _audit.WriteAuditLogAsync(
                userId, 2, "Departments", dept.DepartmentID.ToString(),
                oldValues: old != null ? System.Text.Json.JsonSerializer.Serialize(new
                {
                    old.DepartmentNameAr
                }) : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                {
                    dept.DepartmentNameAr
                }),
                moduleName: "SCR_DEPT",
                description: $"تعديل قسم: {dept.DepartmentNameAr}");
        }

        // ==========================================
        // تعطيل / تفعيل قسم
        // ==========================================
        public async Task<(bool Success, string Message)> SetDepartmentActiveStatusAsync(
            int departmentId, bool isActive, int userId)
        {
            using var connection = CreateConnection();

            var dept = await connection.QueryFirstOrDefaultAsync<DepartmentEditDto>(
                @"SELECT DepartmentID, DepartmentNameAr FROM dbo.Departments WHERE DepartmentID = @ID",
                new { ID = departmentId });

            if (dept == null)
                return (false, "القسم غير موجود");

            if (!isActive)
            {
                var empCount = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(*) FROM dbo.Employees
                    WHERE DepartmentID = @ID AND IsActive = 1 AND EmployeeStatus = 1",
                    new { ID = departmentId });

                if (empCount > 0)
                    return (false, $"لا يمكن تعطيل القسم لوجود {empCount} موظف نشط تابع له");
            }

            await connection.ExecuteAsync(@"
                UPDATE dbo.Departments
                SET IsActive = @IsActive, ModifiedDate = GETDATE()
                WHERE DepartmentID = @ID",
                new { ID = departmentId, IsActive = isActive });

            await _audit.WriteAuditLogAsync(
                userId, isActive ? (byte)2 : (byte)3,
                "Departments", departmentId.ToString(),
                moduleName: "SCR_DEPT",
                description: $"{(isActive ? "إعادة تفعيل" : "تعطيل")} قسم: {dept.DepartmentNameAr}");

            return (true, isActive ? "تمت إعادة تفعيل القسم بنجاح" : "تم تعطيل القسم بنجاح");
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportDepartmentsToExcelAsync(List<DepartmentListDto> departments, int userId)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("الأقسام");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير الأقسام — واي كي كوتينج";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 8).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 8).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 8).Style.Font.FontColor = ClosedXML.Excel.XLColor.Gray;

            var headers = new[] { "#", "الكود", "اسم القسم", "القسم الأب", "المدير", "الهاتف", "عدد الموظفين", "الحالة" };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1d143f");
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            }

            int row = 5;
            int num = 0;
            foreach (var d in departments)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = d.DepartmentCode ?? "";
                ws.Cell(row, 3).Value = d.DepartmentNameAr ?? "";
                ws.Cell(row, 4).Value = d.ParentDepartmentName ?? "—";
                ws.Cell(row, 5).Value = d.ManagerName ?? "—";
                ws.Cell(row, 6).Value = d.Phone ?? "—";
                ws.Cell(row, 7).Value = d.EmployeeCount;
                ws.Cell(row, 8).Value = d.IsActive ? "نشط" : "غير نشط";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId, 5, "Departments",
                moduleName: "SCR_DEPT",
                description: $"تصدير تقرير الأقسام ({departments.Count} سجل)");

            return stream.ToArray();
        }
    }
}