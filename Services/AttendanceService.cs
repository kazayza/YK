using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class AttendanceService : BaseDbService
    {
        private readonly AuditService _audit;

        public AttendanceService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        public async Task<AttendanceStatsDto> GetAttendanceStatsAsync(DateTime? dateFrom, DateTime? dateTo)
        {
            using var connection = CreateConnection();
            var sql = @"
                SELECT 
                    COUNT(*) AS TotalRecords,
                    SUM(CASE WHEN AttendanceStatus = 1 THEN 1 ELSE 0 END) AS PresentCount,
                    SUM(CASE WHEN AttendanceStatus = 2 THEN 1 ELSE 0 END) AS AbsentCount,
                    SUM(CASE WHEN AttendanceStatus = 3 THEN 1 ELSE 0 END) AS LeaveCount,
                    SUM(CASE WHEN AttendanceStatus = 5 THEN 1 ELSE 0 END) AS LateCount,
                    SUM(CASE WHEN AttendanceStatus = 4 THEN 1 ELSE 0 END) AS MissionCount,
                    ISNULL(SUM(OvertimeHours), 0) AS TotalOvertimeHours,
                    ISNULL(SUM(CASE WHEN ActualIn IS NOT NULL AND ScheduledIn IS NOT NULL AND ActualIn > ScheduledIn
                        THEN DATEDIFF(MINUTE, ScheduledIn, ActualIn) ELSE 0 END), 0) AS TotalLateMinutes
                FROM dbo.AttendanceRecords
                WHERE (@DateFrom IS NULL OR AttendanceDate >= @DateFrom)
                  AND (@DateTo IS NULL OR AttendanceDate <= @DateTo)";

            return await connection.QueryFirstOrDefaultAsync<AttendanceStatsDto>(sql,
                new { DateFrom = dateFrom, DateTo = dateTo }) ?? new AttendanceStatsDto();
        }

        public async Task<AttendancePagedResult> GetAttendancePagedAsync(AttendanceFilterDto filter)
        {
            using var connection = CreateConnection();
            filter ??= new AttendanceFilterDto();

            var where = @"
                WHERE (@AttendanceStatus IS NULL OR @AttendanceStatus = 0 OR a.AttendanceStatus = @AttendanceStatus)
                  AND (@DateFrom IS NULL OR a.AttendanceDate >= @DateFrom)
                  AND (@DateTo IS NULL OR a.AttendanceDate <= @DateTo)
                  AND (@DepartmentID IS NULL OR @DepartmentID = 0 OR e.DepartmentID = @DepartmentID)
                  AND (
                      ISNULL(@SearchText, N'') = N''
                      OR e.FullNameAr LIKE N'%' + @SearchText + N'%'
                      OR e.EmployeeCode LIKE N'%' + @SearchText + N'%'
                  )";

            var countSql = @"SELECT COUNT(*) FROM dbo.AttendanceRecords a
                INNER JOIN dbo.Employees e ON a.EmployeeID = e.EmployeeID " + where;

            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
            {
                filter.SearchText, filter.DepartmentID, filter.AttendanceStatus,
                filter.DateFrom, filter.DateTo
            });

            var dataSql = @"
                SELECT * FROM (
                    SELECT a.AttendanceID, a.EmployeeID, e.EmployeeCode, e.FullNameAr AS EmployeeName,
                        d.DepartmentNameAr AS DepartmentName, a.AttendanceDate, a.AttendanceStatus,
                        CASE a.AttendanceStatus
                            WHEN 1 THEN N'حاضر' WHEN 2 THEN N'غائب' WHEN 3 THEN N'إجازة'
                            WHEN 4 THEN N'مأمورية' WHEN 5 THEN N'تأخير' WHEN 6 THEN N'إذن'
                            WHEN 7 THEN N'عطلة رسمية'
                        END AS StatusName,
                        CONVERT(NVARCHAR(8), a.ScheduledIn, 108) AS ScheduledIn,
                        CONVERT(NVARCHAR(8), a.ScheduledOut, 108) AS ScheduledOut,
                        CONVERT(NVARCHAR(8), a.ActualIn, 108) AS ActualIn,
                        CONVERT(NVARCHAR(8), a.ActualOut, 108) AS ActualOut,
                        a.WorkedHours, a.OvertimeHours, a.LateMinutes, a.EarlyLeaveMinutes, a.Notes,
                        ROW_NUMBER() OVER (ORDER BY a.AttendanceDate DESC, e.FullNameAr) AS RowNum
                    FROM dbo.AttendanceRecords a
                    INNER JOIN dbo.Employees e ON a.EmployeeID = e.EmployeeID
                    LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                    " + where + @"
                ) ranked
                WHERE RowNum BETWEEN @StartRow AND @EndRow
                ORDER BY RowNum";

            var items = await connection.QueryAsync<AttendanceListDto>(dataSql, new
            {
                filter.SearchText, filter.DepartmentID, filter.AttendanceStatus,
                filter.DateFrom, filter.DateTo,
                StartRow = (filter.PageNumber - 1) * filter.PageSize + 1,
                EndRow = filter.PageNumber * filter.PageSize
            });

            return new AttendancePagedResult
            {
                Items = items.ToList(),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        public async Task<AttendanceEditDto?> GetAttendanceByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<AttendanceEditDto>(
                @"SELECT AttendanceID, EmployeeID, AttendanceDate, AttendanceStatus,
                         ScheduledIn, ScheduledOut, ActualIn, ActualOut,
                         OvertimeHours, LeaveRequestID, Notes
                  FROM dbo.AttendanceRecords WHERE AttendanceID = @ID", new { ID = id });
        }

                public async Task<int> InsertAttendanceAsync(AttendanceEditDto item, int userId)
        {
            using var connection = CreateConnection();
            if (item.EmployeeID <= 0) throw new Exception("يجب اختيار الموظف");

            // حساب الأوفرتايم تلقائياً
            decimal overtimeHours = 0;
            if (item.ActualIn.HasValue && item.ActualOut.HasValue 
                && item.ScheduledIn.HasValue && item.ScheduledOut.HasValue)
            {
                var workedMin = (item.ActualOut.Value - item.ActualIn.Value).TotalMinutes;
                var scheduledMin = (item.ScheduledOut.Value - item.ScheduledIn.Value).TotalMinutes;
                if (workedMin > scheduledMin)
                    overtimeHours = Math.Round((decimal)(workedMin - scheduledMin) / 60m, 2);
            }

            var sql = @"
                INSERT INTO dbo.AttendanceRecords
                    (EmployeeID, AttendanceDate, AttendanceStatus,
                     ScheduledIn, ScheduledOut, ActualIn, ActualOut,
                     OvertimeHours, LeaveRequestID, Notes, CreatedBy, CreatedDate)
                VALUES
                    (@EmployeeID, @AttendanceDate, @AttendanceStatus,
                     @ScheduledIn, @ScheduledOut, @ActualIn, @ActualOut,
                     @OvertimeHours, @LeaveRequestID, @Notes, @CreatedBy, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                item.EmployeeID,
                item.AttendanceDate,
                item.AttendanceStatus,
                item.ScheduledIn,
                item.ScheduledOut,
                item.ActualIn,
                item.ActualOut,
                OvertimeHours = overtimeHours,
                item.LeaveRequestID,
                item.Notes,
                CreatedBy = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "AttendanceRecords", newId.ToString(),
                moduleName: "SCR_ATTEND",
                description: $"تسجيل حضور: موظف {item.EmployeeID} - {item.AttendanceDate:dd/MM/yyyy}");

            return newId;
        }

                public async Task UpdateAttendanceAsync(AttendanceEditDto item, int userId)
        {
            using var connection = CreateConnection();

            // حساب الأوفرتايم تلقائياً
            decimal overtimeHours = 0;
            if (item.ActualIn.HasValue && item.ActualOut.HasValue
                && item.ScheduledIn.HasValue && item.ScheduledOut.HasValue)
            {
                var workedMin = (item.ActualOut.Value - item.ActualIn.Value).TotalMinutes;
                var scheduledMin = (item.ScheduledOut.Value - item.ScheduledIn.Value).TotalMinutes;
                if (workedMin > scheduledMin)
                    overtimeHours = Math.Round((decimal)(workedMin - scheduledMin) / 60m, 2);
            }

            var sql = @"
                UPDATE dbo.AttendanceRecords SET
                    AttendanceStatus = @AttendanceStatus,
                    ScheduledIn = @ScheduledIn,
                    ScheduledOut = @ScheduledOut,
                    ActualIn = @ActualIn,
                    ActualOut = @ActualOut,
                    OvertimeHours = @OvertimeHours,
                    LeaveRequestID = @LeaveRequestID,
                    Notes = @Notes,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE AttendanceID = @AttendanceID";

            await connection.ExecuteAsync(sql, new
            {
                item.AttendanceID,
                item.AttendanceStatus,
                item.ScheduledIn,
                item.ScheduledOut,
                item.ActualIn,
                item.ActualOut,
                OvertimeHours = overtimeHours,
                item.LeaveRequestID,
                item.Notes,
                ModifiedBy = userId
            });

            await _audit.WriteAuditLogAsync(userId, 2, "AttendanceRecords", item.AttendanceID.ToString(),
                moduleName: "SCR_ATTEND", description: $"تعديل سجل حضور #{item.AttendanceID}");
        }

        public async Task<byte[]> ExportAttendanceToExcelAsync(List<AttendanceListDto> items, int userId)
        {
            await Task.CompletedTask;
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("الحضور");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير الحضور والانصراف — واي كي كوتينج";
            ws.Range(1, 1, 1, 11).Merge().Style.Font.Bold = true;

            var headers = new[] { "#", "الكود", "الموظف", "القسم", "التاريخ", "الحالة",
                "الدخول المفترض", "الدخول الفعلي", "الخروج الفعلي", "ساعات العمل", "أوفرتايم" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1d143f");
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            }

            int row = 4, num = 0;
            foreach (var item in items)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = item.EmployeeCode ?? "";
                ws.Cell(row, 3).Value = item.EmployeeName ?? "";
                ws.Cell(row, 4).Value = item.DepartmentName ?? "—";
                ws.Cell(row, 5).Value = item.AttendanceDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 6).Value = item.StatusName ?? "";
                ws.Cell(row, 7).Value = item.ScheduledIn ?? "—";
                ws.Cell(row, 8).Value = item.ActualIn ?? "—";
                ws.Cell(row, 9).Value = item.ActualOut ?? "—";
                ws.Cell(row, 10).Value = item.WorkedHours ?? 0;
                ws.Cell(row, 11).Value = item.OvertimeHours ?? 0;
                row++;
            }
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public static string GetStatusName(int status) => status switch
        {
            1 => "حاضر", 2 => "غائب", 3 => "إجازة",
            4 => "مأمورية", 5 => "تأخير", 6 => "إذن",
            7 => "عطلة رسمية", _ => "—"
        };

        public async Task<int> ImportFromFingerprintAsync(List<FingerprintRecord> records, int userId)
{
    using var connection = CreateConnection();
    var imported = 0;

    foreach (var rec in records)
    {
        if (rec.EmployeeID <= 0 || rec.AttendanceDate == default) continue;

        // حساب الأوفرتايم لو مش موجود
        decimal overtimeHours = rec.OvertimeHours;
        if (overtimeHours == 0 && rec.ActualIn.HasValue && rec.ActualOut.HasValue
            && rec.ScheduledIn.HasValue && rec.ScheduledOut.HasValue)
        {
            var workedMin = (rec.ActualOut.Value - rec.ActualIn.Value).TotalMinutes;
            var scheduledMin = (rec.ScheduledOut.Value - rec.ScheduledIn.Value).TotalMinutes;
            if (workedMin > scheduledMin)
                overtimeHours = Math.Round((decimal)(workedMin - scheduledMin) / 60m, 2);
        }

        // تحديد الحالة
        int status = rec.AttendanceStatus > 0 ? rec.AttendanceStatus : (rec.ActualIn.HasValue ? 1 : 2);

        var exists = await connection.QueryFirstOrDefaultAsync<int?>(
            @"SELECT AttendanceID FROM dbo.AttendanceRecords
              WHERE EmployeeID = @EmployeeID AND AttendanceDate = @AttendanceDate",
            new { rec.EmployeeID, rec.AttendanceDate });

        if (exists.HasValue)
        {
            await connection.ExecuteAsync(@"
                UPDATE dbo.AttendanceRecords SET
                    ActualIn = @ActualIn,
                    ActualOut = @ActualOut,
                    ScheduledIn = @ScheduledIn,
                    ScheduledOut = @ScheduledOut,
                    AttendanceStatus = @AttendanceStatus,
                    OvertimeHours = @OvertimeHours,
                    Notes = ISNULL(@Notes, Notes),
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE AttendanceID = @ID",
                new
                {
                    ID = exists.Value,
                    rec.ActualIn,
                    rec.ActualOut,
                    rec.ScheduledIn,
                    rec.ScheduledOut,
                    AttendanceStatus = status,
                    OvertimeHours = overtimeHours,
                    rec.Notes,
                    ModifiedBy = userId
                });
        }
        else
        {
            await connection.ExecuteAsync(@"
                INSERT INTO dbo.AttendanceRecords
                    (EmployeeID, AttendanceDate, AttendanceStatus,
                     ActualIn, ActualOut, ScheduledIn, ScheduledOut,
                     OvertimeHours, Notes, CreatedBy, CreatedDate)
                VALUES
                    (@EmployeeID, @AttendanceDate, @AttendanceStatus,
                     @ActualIn, @ActualOut, @ScheduledIn, @ScheduledOut,
                     @OvertimeHours, @Notes, @CreatedBy, GETDATE())",
                new
                {
                    rec.EmployeeID,
                    rec.AttendanceDate,
                    AttendanceStatus = status,
                    rec.ActualIn,
                    rec.ActualOut,
                    rec.ScheduledIn,
                    rec.ScheduledOut,
                    OvertimeHours = overtimeHours,
                    rec.Notes,
                    CreatedBy = userId
                });
        }

        imported++;
    }

    await _audit.WriteAuditLogAsync(userId, 1, "AttendanceRecords", "0",
        moduleName: "SCR_ATTEND",
        description: $"استيراد {imported} سجل حضور من ملف Excel");

    return imported;
}
    }
}