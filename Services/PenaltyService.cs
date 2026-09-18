using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
        public class PenaltyService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public PenaltyService(IConfiguration configuration, AuditService audit, NotificationService notif) : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        public async Task<PenaltyPagedResult> GetPenaltiesPagedAsync(PenaltyFilterDto filter)
        {
            using var connection = CreateConnection();
            filter ??= new PenaltyFilterDto();

            var where = @"
                WHERE (@RecordType IS NULL OR @RecordType = 0 OR pr.RecordType = @RecordType)
                  AND (@DateFrom IS NULL OR pr.RecordDate >= @DateFrom)
                  AND (@DateTo IS NULL OR pr.RecordDate <= @DateTo)
                  AND (@DepartmentID IS NULL OR @DepartmentID = 0 OR e.DepartmentID = @DepartmentID)
                  AND (ISNULL(@SearchText, N'') = N''
                      OR e.FullNameAr LIKE N'%' + @SearchText + N'%'
                      OR e.EmployeeCode LIKE N'%' + @SearchText + N'%')";

            var countSql = @"SELECT COUNT(*) FROM dbo.EmployeePenaltiesRewards pr
                INNER JOIN dbo.Employees e ON pr.EmployeeID = e.EmployeeID " + where;

            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
            {
                filter.SearchText, filter.DepartmentID, filter.RecordType,
                filter.DateFrom, filter.DateTo
            });

            var dataSql = @"
                SELECT * FROM (
                    SELECT pr.RecordID, pr.EmployeeID, e.EmployeeCode,
                        e.FullNameAr AS EmployeeName,
                        d.DepartmentNameAr AS DepartmentName,
                        pr.RecordType,
                        CASE pr.RecordType WHEN 1 THEN N'جزاء' WHEN 2 THEN N'مكافأة' END AS RecordTypeName,
                        pr.RecordDate, pr.Category, pr.Description,
                        pr.DeductionType, pr.Amount, pr.Percentage, pr.Days, pr.EffectiveMonth,
                        pr.RecordStatus,
                        CASE pr.RecordStatus WHEN 1 THEN N'مقدم' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'ملغي' END AS RecordStatusName,
                        ISNULL(iss.FullNameAr, N'') AS IssuedByName,
                        ROW_NUMBER() OVER (ORDER BY pr.RecordDate DESC, e.FullNameAr) AS RowNum
                    FROM dbo.EmployeePenaltiesRewards pr
                    INNER JOIN dbo.Employees e ON pr.EmployeeID = e.EmployeeID
                    LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                    LEFT JOIN dbo.Employees iss ON pr.IssuedBy = iss.EmployeeID
                    " + where + @"
                ) ranked
                WHERE RowNum BETWEEN @StartRow AND @EndRow
                ORDER BY RowNum";

            var items = await connection.QueryAsync<PenaltyListDto>(dataSql, new
            {
                filter.SearchText, filter.DepartmentID, filter.RecordType,
                filter.DateFrom, filter.DateTo,
                StartRow = (filter.PageNumber - 1) * filter.PageSize + 1,
                EndRow = filter.PageNumber * filter.PageSize
            });

            return new PenaltyPagedResult
            {
                Items = items.ToList(),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        public async Task<PenaltyEditDto?> GetPenaltyByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<PenaltyEditDto>(
                @"SELECT RecordID, EmployeeID, RecordDate, RecordType, Category,
                         Description, DeductionType, Amount, Percentage, Days,
                         EffectiveMonth, RecordStatus, IssuedBy, Notes
                  FROM dbo.EmployeePenaltiesRewards WHERE RecordID = @ID", new { ID = id });
        }

        public async Task<int> InsertPenaltyAsync(PenaltyEditDto item, int userId)
        {
            using var connection = CreateConnection();
            if (item.EmployeeID <= 0) throw new Exception("يجب اختيار الموظف");
            if (string.IsNullOrWhiteSpace(item.Description)) throw new Exception("الوصف مطلوب");

            var sql = @"
                INSERT INTO dbo.EmployeePenaltiesRewards
                    (EmployeeID, RecordDate, RecordType, Category, Description,
                     DeductionType, Amount, Percentage, Days, EffectiveMonth,
                     RecordStatus, IssuedBy, Notes, CreatedBy, CreatedDate)
                VALUES
                    (@EmployeeID, @RecordDate, @RecordType, @Category, @Description,
                     @DeductionType, @Amount, @Percentage, @Days, @EffectiveMonth,
                     @RecordStatus, @IssuedBy, @Notes, @CreatedBy, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                item.EmployeeID, item.RecordDate, item.RecordType,
                item.Category, item.Description, item.DeductionType,
                item.Amount, item.Percentage, item.Days, item.EffectiveMonth,
                item.RecordStatus, item.IssuedBy, item.Notes,
                CreatedBy = userId
            });

                        await _audit.WriteAuditLogAsync(userId, 1, "EmployeePenaltiesRewards",
                newId.ToString(), moduleName: "SCR_PENALTY",
                description: $"{(item.RecordType == 1 ? "جزاء" : "مكافأة")}: موظف {item.EmployeeID}");

            // إرسال إشعار للمسئولين (دور HR = 2 مثلاً)
            var empName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
                new { ID = item.EmployeeID });

            await _notif.CreateNotificationAsync(
                notificationType: item.RecordType == 1 ? (byte)4 : (byte)2,
                title: item.RecordType == 1 ? "جزاء جديد بانتظار الاعتماد" : "مكافأة جديدة بانتظار الاعتماد",
                message: $"{(item.RecordType == 1 ? "جزاء" : "مكافأة")} على الموظف {empName} - {item.Description}",
                priority: item.RecordType == 1 ? (byte)2 : (byte)2,
                targetRoleId: 1,
                relatedModule: "SCR_PENALTY",
                relatedRecordId: newId,
                createdBy: userId);

            return newId;
        }

        public async Task UpdatePenaltyAsync(PenaltyEditDto item, int userId)
        {
            using var connection = CreateConnection();
            if (item.EffectiveMonth.HasValue)
            item.EffectiveMonth = new DateTime(item.EffectiveMonth.Value.Year, item.EffectiveMonth.Value.Month, 1);

            await connection.ExecuteAsync(@"
                UPDATE dbo.EmployeePenaltiesRewards SET
                    RecordType = @RecordType, Category = @Category,
                    Description = @Description, DeductionType = @DeductionType,
                    Amount = @Amount, Percentage = @Percentage, Days = @Days,
                    EffectiveMonth = @EffectiveMonth, RecordStatus = @RecordStatus,
                    IssuedBy = @IssuedBy, Notes = @Notes
                WHERE RecordID = @RecordID",
                new
                {
                    item.RecordID, item.RecordType, item.Category,
                    item.Description, item.DeductionType, item.Amount,
                    item.Percentage, item.Days, item.EffectiveMonth,
                    item.RecordStatus, item.IssuedBy, item.Notes
                });

            await _audit.WriteAuditLogAsync(userId, 2, "EmployeePenaltiesRewards",
                item.RecordID.ToString(), moduleName: "SCR_PENALTY",
                description: $"تعديل جزاء/مكافأة #{item.RecordID}");

        }

        public async Task ApprovePenaltyAsync(int recordId, int userId)
        {
            using var connection = CreateConnection();

            // جلب EmployeeID من UserID
            var employeeId = await connection.ExecuteScalarAsync<int?>(
                "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID",
                new { UserID = userId });

            if (employeeId == null || employeeId == 0)
                throw new Exception("لم يتم العثور على بيانات الموظف المرتبط بحسابك");

            await connection.ExecuteAsync(@"
                UPDATE dbo.EmployeePenaltiesRewards SET
                    RecordStatus = 2, ApprovedBy = @ApprovedBy, ApprovedDate = GETDATE()
                WHERE RecordID = @RecordID",
                new { RecordID = recordId, ApprovedBy = employeeId });

            await _audit.WriteAuditLogAsync(userId, 2, "EmployeePenaltiesRewards",
                recordId.ToString(), moduleName: "SCR_PENALTY",
                description: $"اعتماد جزاء/مكافأة #{recordId}");
                // تحديد الإشعار كتم تنفيذه
            await _notif.MarkRelatedAsActionedAsync("SCR_PENALTY", recordId);
        }

                public async Task CancelPenaltyAsync(int recordId, int userId)
        {
            using var connection = CreateConnection();

            var employeeId = await connection.ExecuteScalarAsync<int?>(
                "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID",
                new { UserID = userId });

            await connection.ExecuteAsync(@"
                UPDATE dbo.EmployeePenaltiesRewards SET RecordStatus = 3
                WHERE RecordID = @RecordID",
                new { RecordID = recordId });

            await _audit.WriteAuditLogAsync(userId, 2, "EmployeePenaltiesRewards",
                recordId.ToString(), moduleName: "SCR_PENALTY",
                description: $"إلغاء جزاء/مكافأة #{recordId}");
        }

        public async Task<byte[]> ExportToExcelAsync(List<PenaltyListDto> items, int userId)
        {
            await Task.CompletedTask;
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("الجزاءات والمكافآت");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير الجزاءات والمكافآت — واي كي كوتينج";
            ws.Range(1, 1, 1, 10).Merge().Style.Font.Bold = true;

            var headers = new[] { "#", "الكود", "الموظف", "القسم", "النوع",
                "التصنيف", "الوصف", "المبلغ", "التاريخ", "الحالة" };
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
                ws.Cell(row, 4).Value = item.DepartmentName ?? "";
                ws.Cell(row, 5).Value = item.RecordTypeName ?? "";
                ws.Cell(row, 6).Value = item.Category ?? "";
                ws.Cell(row, 7).Value = item.Description ?? "";
                ws.Cell(row, 8).Value = item.Amount;
                ws.Cell(row, 9).Value = item.RecordDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 10).Value = item.RecordStatusName ?? "";
                row++;
            }
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}