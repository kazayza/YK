using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class JobTitleService : BaseDbService
    {
        private readonly AuditService _audit;

        public JobTitleService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        // ==========================================
        // قائمة المسميات الوظيفية
        // ==========================================
        public async Task<List<JobTitleListDto>> GetJobTitlesListAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    j.JobTitleID,
                    j.JobTitleCode,
                    j.JobTitleNameAr,
                    j.JobTitleNameEn,
                    j.JobLevel,
                    CASE j.JobLevel
                        WHEN 1 THEN N'عمال'
                        WHEN 2 THEN N'فنيين'
                        WHEN 3 THEN N'متخصصين'
                        WHEN 4 THEN N'مشرفين'
                        WHEN 5 THEN N'مدراء'
                        WHEN 6 THEN N'إدارة عليا'
                    END AS JobLevelName,
                    j.MinSalary,
                    j.MaxSalary,
                    ISNULL(emp.Count, 0) AS EmployeeCount,
                    j.IsActive
                FROM dbo.JobTitles j
                LEFT JOIN (
                    SELECT JobTitleID, COUNT(*) AS Count
                    FROM Employees
                    WHERE IsActive = 1 AND EmployeeStatus = 1
                    GROUP BY JobTitleID
                ) emp ON j.JobTitleID = emp.JobTitleID
                ORDER BY j.JobLevel, j.JobTitleNameAr";

            var result = await connection.QueryAsync<JobTitleListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // بيانات مسمى واحد
        // ==========================================
        public async Task<JobTitleEditDto?> GetJobTitleByIdAsync(int jobTitleId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT JobTitleID, JobTitleCode, JobTitleNameAr, JobTitleNameEn,
                       JobLevel, MinSalary, MaxSalary, Description, IsActive
                FROM dbo.JobTitles
                WHERE JobTitleID = @ID";

            return await connection.QueryFirstOrDefaultAsync<JobTitleEditDto>(sql, new { ID = jobTitleId });
        }

        // ==========================================
        // إضافة مسمى وظيفي
        // ==========================================
        public async Task<int> InsertJobTitleAsync(JobTitleEditDto job, int userId)
        {
            using var connection = CreateConnection();

            if (string.IsNullOrWhiteSpace(job.JobTitleNameAr))
                throw new Exception("اسم المسمى الوظيفي بالعربي مطلوب");

            var code = job.JobTitleCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                var seq = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT ISNULL(MAX(CAST(JobTitleCode AS INT)), 0) + 1
                    FROM JobTitles
                    WHERE ISNUMERIC(JobTitleCode) = 1");
                code = seq.ToString("D4");
            }

            var sql = @"
                INSERT INTO dbo.JobTitles
                    (JobTitleCode, JobTitleNameAr, JobTitleNameEn,
                     JobLevel, MinSalary, MaxSalary, Description, IsActive, CreatedDate)
                VALUES
                    (@JobTitleCode, @JobTitleNameAr, @JobTitleNameEn,
                     @JobLevel, @MinSalary, @MaxSalary, @Description, @IsActive, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                JobTitleCode = code,
                job.JobTitleNameAr,
                job.JobTitleNameEn,
                job.JobLevel,
                job.MinSalary,
                job.MaxSalary,
                job.Description,
                job.IsActive
            });

            await _audit.WriteAuditLogAsync(
                userId, 1, "JobTitles", newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new { code, job.JobTitleNameAr }),
                moduleName: "SCR_JOBTITLE",
                description: $"إضافة مسمى وظيفي: {job.JobTitleNameAr}");

            return newId;
        }

        // ==========================================
        // تعديل مسمى وظيفي
        // ==========================================
        public async Task UpdateJobTitleAsync(JobTitleEditDto job, int userId)
        {
            using var connection = CreateConnection();

            if (string.IsNullOrWhiteSpace(job.JobTitleNameAr))
                throw new Exception("اسم المسمى الوظيفي بالعربي مطلوب");

            var old = await connection.QueryFirstOrDefaultAsync<JobTitleEditDto>(
                @"SELECT JobTitleNameAr, JobLevel, IsActive FROM dbo.JobTitles WHERE JobTitleID = @ID",
                new { ID = job.JobTitleID });

            var sql = @"
                UPDATE dbo.JobTitles
                SET JobTitleNameAr = @JobTitleNameAr,
                    JobTitleNameEn = @JobTitleNameEn,
                    JobLevel       = @JobLevel,
                    MinSalary      = @MinSalary,
                    MaxSalary      = @MaxSalary,
                    Description    = @Description,
                    IsActive       = @IsActive,
                    CreatedDate    = GETDATE()
                WHERE JobTitleID = @JobTitleID";

            await connection.ExecuteAsync(sql, new
            {
                job.JobTitleID,
                job.JobTitleNameAr,
                job.JobTitleNameEn,
                job.JobLevel,
                job.MinSalary,
                job.MaxSalary,
                job.Description,
                job.IsActive
            });

            await _audit.WriteAuditLogAsync(
                userId, 2, "JobTitles", job.JobTitleID.ToString(),
                oldValues: old != null ? System.Text.Json.JsonSerializer.Serialize(new { old.JobTitleNameAr }) : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new { job.JobTitleNameAr }),
                moduleName: "SCR_JOBTITLE",
                description: $"تعديل مسمى وظيفي: {job.JobTitleNameAr}");
        }

        // ==========================================
        // تعطيل / تفعيل
        // ==========================================
        public async Task<(bool Success, string Message)> SetJobTitleActiveStatusAsync(
            int jobTitleId, bool isActive, int userId)
        {
            using var connection = CreateConnection();

            var job = await connection.QueryFirstOrDefaultAsync<JobTitleEditDto>(
                @"SELECT JobTitleID, JobTitleNameAr FROM dbo.JobTitles WHERE JobTitleID = @ID",
                new { ID = jobTitleId });

            if (job == null)
                return (false, "المسمى الوظيفي غير موجود");

            if (!isActive)
            {
                var empCount = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(*) FROM dbo.Employees
                    WHERE JobTitleID = @ID AND IsActive = 1 AND EmployeeStatus = 1",
                    new { ID = jobTitleId });

                if (empCount > 0)
                    return (false, $"لا يمكن تعطيل المسمى لوجود {empCount} موظف نشط يحمله");
            }

            await connection.ExecuteAsync(@"
                UPDATE dbo.JobTitles SET IsActive = @IsActive, CreatedDate = GETDATE()
                WHERE JobTitleID = @ID",
                new { ID = jobTitleId, IsActive = isActive });

            await _audit.WriteAuditLogAsync(
                userId, isActive ? (byte)2 : (byte)3,
                "JobTitles", jobTitleId.ToString(),
                moduleName: "SCR_JOBTITLE",
                description: $"{(isActive ? "إعادة تفعيل" : "تعطيل")} مسمى: {job.JobTitleNameAr}");

            return (true, isActive ? "تمت إعادة التفعيل بنجاح" : "تم التعطيل بنجاح");
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportJobTitlesToExcelAsync(List<JobTitleListDto> items, int userId)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("المسميات الوظيفية");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير المسميات الوظيفية — واي كي كوتينج";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 8).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 8).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 8).Style.Font.FontColor = ClosedXML.Excel.XLColor.Gray;

            var headers = new[] { "#", "الكود", "المسمى", "المستوى", "الحد الأدنى", "الحد الأقصى", "عدد الموظفين", "الحالة" };

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
            foreach (var j in items)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = j.JobTitleCode ?? "";
                ws.Cell(row, 3).Value = j.JobTitleNameAr ?? "";
                ws.Cell(row, 4).Value = j.JobLevelName ?? "";
                ws.Cell(row, 5).Value = j.MinSalary;
                ws.Cell(row, 6).Value = j.MaxSalary;
                ws.Cell(row, 7).Value = j.EmployeeCount;
                ws.Cell(row, 8).Value = j.IsActive ? "نشط" : "غير نشط";

                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId, 5, "JobTitles",
                moduleName: "SCR_JOBTITLE",
                description: $"تصدير تقرير المسميات ({items.Count} سجل)");

            return stream.ToArray();
        }
    }
}