using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class LeaveBalanceService : BaseDbService
    {
        private readonly AuditService _audit;

        public LeaveBalanceService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        public async Task<List<LeaveBalanceViewDto>> GetLeaveBalancesAsync(
            int? departmentId, int? leaveTypeId, int? fiscalYearId, string? searchText)
        {
            using var connection = CreateConnection();

            // لو مفيش سنة مالية محددة، نجيب الحالية
            if (!fiscalYearId.HasValue || fiscalYearId == 0)
            {
                fiscalYearId = await connection.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 FiscalYearID FROM dbo.FiscalYears WHERE IsCurrent = 1 AND YearStatus = 1");
            }

            var sql = @"
                SELECT
                    elb.BalanceID,
                    elb.EmployeeID,
                    e.EmployeeCode,
                    e.FullNameAr AS EmployeeName,
                    d.DepartmentNameAr AS DepartmentName,
                    elb.LeaveTypeID,
                    lt.LeaveTypeNameAr AS LeaveTypeName,
                    elb.FiscalYearID,
                    fy.YearName,
                    elb.EntitledDays,
                    elb.CarriedForward,
                    elb.AdditionalDays,
                    elb.TotalEntitled,
                    elb.UsedDays,
                    elb.PendingDays,
                    elb.RemainingDays
                FROM dbo.EmployeeLeaveBalances elb
                INNER JOIN dbo.Employees e ON elb.EmployeeID = e.EmployeeID
                INNER JOIN dbo.LeaveTypes lt ON elb.LeaveTypeID = lt.LeaveTypeID
                INNER JOIN dbo.FiscalYears fy ON elb.FiscalYearID = fy.FiscalYearID
                LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                WHERE e.IsActive = 1
                  AND (@DepartmentID IS NULL OR @DepartmentID = 0 OR e.DepartmentID = @DepartmentID)
                  AND (@LeaveTypeID IS NULL OR @LeaveTypeID = 0 OR elb.LeaveTypeID = @LeaveTypeID)
                  AND (@FiscalYearID IS NULL OR @FiscalYearID = 0 OR elb.FiscalYearID = @FiscalYearID)
                  AND (ISNULL(@SearchText, N'') = N''
                      OR e.FullNameAr LIKE N'%' + @SearchText + N'%'
                      OR e.EmployeeCode LIKE N'%' + @SearchText + N'%')
                ORDER BY e.FullNameAr, lt.LeaveTypeNameAr";

            var result = await connection.QueryAsync<LeaveBalanceViewDto>(sql, new
            {
                DepartmentID = departmentId,
                LeaveTypeID = leaveTypeId,
                FiscalYearID = fiscalYearId,
                SearchText = searchText
            });

            return result.ToList();
        }

        public async Task<List<LeaveBalanceViewDto>> GetEmployeeBalancesAsync(int employeeId, int? fiscalYearId)
        {
            using var connection = CreateConnection();

            if (!fiscalYearId.HasValue || fiscalYearId == 0)
            {
                fiscalYearId = await connection.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 FiscalYearID FROM dbo.FiscalYears WHERE IsCurrent = 1 AND YearStatus = 1");
            }

            var sql = @"
                SELECT
                    elb.BalanceID,
                    elb.EmployeeID,
                    e.EmployeeCode,
                    e.FullNameAr AS EmployeeName,
                    d.DepartmentNameAr AS DepartmentName,
                    elb.LeaveTypeID,
                    lt.LeaveTypeNameAr AS LeaveTypeName,
                    elb.FiscalYearID,
                    fy.YearName,
                    elb.EntitledDays,
                    elb.CarriedForward,
                    elb.AdditionalDays,
                    elb.TotalEntitled,
                    elb.UsedDays,
                    elb.PendingDays,
                    elb.RemainingDays
                FROM dbo.EmployeeLeaveBalances elb
                INNER JOIN dbo.Employees e ON elb.EmployeeID = e.EmployeeID
                INNER JOIN dbo.LeaveTypes lt ON elb.LeaveTypeID = lt.LeaveTypeID
                INNER JOIN dbo.FiscalYears fy ON elb.FiscalYearID = fy.FiscalYearID
                LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                WHERE elb.EmployeeID = @EmployeeID
                  AND (@FiscalYearID IS NULL OR @FiscalYearID = 0 OR elb.FiscalYearID = @FiscalYearID)
                ORDER BY lt.LeaveTypeNameAr";

            var result = await connection.QueryAsync<LeaveBalanceViewDto>(sql, new
            {
                EmployeeID = employeeId,
                FiscalYearID = fiscalYearId
            });

            return result.ToList();
        }

        public async Task UpdateBalanceAsync(int balanceId, decimal entitledDays,
            decimal carriedForward, decimal additionalDays, int userId)
        {
            using var connection = CreateConnection();

            if (entitledDays < 0 || carriedForward < 0 || additionalDays < 0)
                throw new Exception("لا يسمح بقيم سالبة");

            var exists = await connection.ExecuteScalarAsync<int?>(
                "SELECT BalanceID FROM dbo.EmployeeLeaveBalances WHERE BalanceID = @BalanceID",
                new { BalanceID = balanceId });

            if (!exists.HasValue)
                throw new Exception("سجل الرصيد غير موجود");

            await connection.ExecuteAsync(@"
                UPDATE dbo.EmployeeLeaveBalances SET
                    EntitledDays = @EntitledDays,
                    CarriedForward = @CarriedForward,
                    AdditionalDays = @AdditionalDays,
                    ModifiedDate = GETDATE()
                WHERE BalanceID = @BalanceID",
                new
                {
                    BalanceID = balanceId,
                    EntitledDays = entitledDays,
                    CarriedForward = carriedForward,
                    AdditionalDays = additionalDays
                });

            await _audit.WriteAuditLogAsync(userId, 2, "EmployeeLeaveBalances",
                balanceId.ToString(),
                moduleName: "SCR_LV_BALANCE",
                description: $"تعديل رصيد إجازة #{balanceId}: مستحق={entitledDays}, مرحّل={carriedForward}, إضافي={additionalDays}");
        }
        public async Task ExcludeBalanceAsync(int balanceId, int userId)
{
    using var connection = CreateConnection();

    var balance = await connection.QueryFirstOrDefaultAsync<dynamic>(
        @"SELECT BalanceID, EmployeeID, LeaveTypeID, UsedDays, PendingDays
          FROM dbo.EmployeeLeaveBalances
          WHERE BalanceID = @BalanceID",
        new { BalanceID = balanceId });

    if (balance == null)
        throw new Exception("سجل الرصيد غير موجود");

    if ((decimal)balance.UsedDays > 0)
        throw new Exception("لا يمكن استبعاد رصيد تم استخدامه بالفعل");

    if ((decimal)balance.PendingDays > 0)
        throw new Exception("لا يمكن استبعاد رصيد عليه طلبات معلقة");

    // التحقق من عدم وجود طلبات إجازة مرتبطة
    var hasRequests = await connection.ExecuteScalarAsync<int>(
        @"SELECT COUNT(*)
          FROM dbo.LeaveRequests
          WHERE EmployeeID = @EmployeeID
            AND LeaveTypeID = @LeaveTypeID
            AND RequestStatus NOT IN (4, 5)",
        new
        {
            EmployeeID = (int)balance.EmployeeID,
            LeaveTypeID = (int)balance.LeaveTypeID
        });

    if (hasRequests > 0)
        throw new Exception("لا يمكن استبعاد رصيد مرتبط بطلبات إجازة نشطة");

    await connection.ExecuteAsync(
        "DELETE FROM dbo.EmployeeLeaveBalances WHERE BalanceID = @BalanceID",
        new { BalanceID = balanceId });

    await _audit.WriteAuditLogAsync(userId, 3, "EmployeeLeaveBalances",
        balanceId.ToString(),
        moduleName: "SCR_LV_BALANCE",
        description: $"استبعاد رصيد إجازة #{balanceId} - موظف {balance.EmployeeID}");
}
        public async Task InitializeBalancesAsync(int employeeId, int fiscalYearId, int userId)
        {
            using var connection = CreateConnection();

            var sql = @"
                INSERT INTO dbo.EmployeeLeaveBalances
                    (EmployeeID, LeaveTypeID, FiscalYearID, EntitledDays)
                SELECT @EmployeeID, lt.LeaveTypeID, @FiscalYearID,
                    ISNULL(lt.MaxDaysPerYear, 0)
                FROM dbo.LeaveTypes lt
                WHERE lt.IsActive = 1
                  AND NOT EXISTS (
                    SELECT 1 FROM dbo.EmployeeLeaveBalances elb
                    WHERE elb.EmployeeID = @EmployeeID
                      AND elb.LeaveTypeID = lt.LeaveTypeID
                      AND elb.FiscalYearID = @FiscalYearID
                  )";

            await connection.ExecuteAsync(sql, new
            {
                EmployeeID = employeeId,
                FiscalYearID = fiscalYearId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "EmployeeLeaveBalances",
                employeeId.ToString(),
                moduleName: "SCR_LV_BALANCE",
                description: $"تهيئة أرصدة إجازات: موظف {employeeId} - سنة {fiscalYearId}");
        }
        public async Task<int> InitializeAllBalancesAsync(int userId)
{
    using var connection = CreateConnection();

    var fiscalYearId = await connection.ExecuteScalarAsync<int?>(
        "SELECT TOP 1 FiscalYearID FROM dbo.FiscalYears WHERE IsCurrent = 1 AND YearStatus = 1");

    if (!fiscalYearId.HasValue || fiscalYearId == 0)
        throw new Exception("لا توجد سنة مالية حالية نشطة");

    // جلب كل الموظفين النشطين مع الجنس
    var employees = await connection.QueryAsync<EmployeeGenderInfo>(
        "SELECT EmployeeID, Gender FROM dbo.Employees WHERE IsActive = 1 AND EmployeeStatus = 1");

    var employeeList = employees.ToList();

    if (!employeeList.Any())
        throw new Exception("لا يوجد موظفين نشطين");

    // جلب أنواع الإجازات مع الجنس المطبق
    var leaveTypes = await connection.QueryAsync<LeaveTypeGenderInfo>(
        @"SELECT LeaveTypeID, LeaveTypeNameAr, MaxDaysPerYear,
                 ISNULL(ApplicableGender, 'A') AS ApplicableGender
          FROM dbo.LeaveTypes
          WHERE IsActive = 1");

    var leaveTypeList = leaveTypes.ToList();
    var totalCreated = 0;

    foreach (var emp in employeeList)
    {
        foreach (var lt in leaveTypeList)
        {
            // التهيئة الذكية: تحقق من الجنس
            if (lt.ApplicableGender != "A")
            {
                if (!string.IsNullOrEmpty(emp.Gender) && emp.Gender != lt.ApplicableGender)
                    continue;
            }

            var exists = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM dbo.EmployeeLeaveBalances
                  WHERE EmployeeID = @EmployeeID
                    AND LeaveTypeID = @LeaveTypeID
                    AND FiscalYearID = @FiscalYearID",
                new
                {
                    EmployeeID = emp.EmployeeID,
                    LeaveTypeID = lt.LeaveTypeID,
                    FiscalYearID = fiscalYearId.Value
                });

            if (exists == 0)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO dbo.EmployeeLeaveBalances
                        (EmployeeID, LeaveTypeID, FiscalYearID, EntitledDays,
                         CarriedForward, AdditionalDays, UsedDays, PendingDays)
                    VALUES
                        (@EmployeeID, @LeaveTypeID, @FiscalYearID, @EntitledDays,
                         0, 0, 0, 0)",
                    new
                    {
                        EmployeeID = emp.EmployeeID,
                        LeaveTypeID = lt.LeaveTypeID,
                        FiscalYearID = fiscalYearId.Value,
                        EntitledDays = lt.MaxDaysPerYear
                    });

                totalCreated++;
            }
        }
    }

    await _audit.WriteAuditLogAsync(userId, 1, "EmployeeLeaveBalances",
        "ALL",
        moduleName: "SCR_LV_BALANCE",
        description: $"تهيئة ذكية للأرصدة: {employeeList.Count} موظف - {totalCreated} سجل جديد - سنة {fiscalYearId}");

    return totalCreated;
}

        public async Task<List<FiscalYearLookupDto>> GetFiscalYearsLookupAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT FiscalYearID, YearName, IsCurrent
                FROM dbo.FiscalYears
                WHERE YearStatus = 1
                ORDER BY StartDate DESC";

            var result = await connection.QueryAsync<FiscalYearLookupDto>(sql);
            return result.ToList();
        }

        public async Task<byte[]> ExportToExcelAsync(List<LeaveBalanceViewDto> items)
        {
            await Task.CompletedTask;

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("أرصدة الإجازات");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير أرصدة الإجازات — واي كي كوتينج";
            ws.Range(1, 1, 1, 13).Merge().Style.Font.Bold = true;

            var headers = new[]
            {
                "#", "الكود", "الموظف", "القسم", "نوع الإجازة", "السنة",
                "مستحق", "مرحّل", "إضافي", "إجمالي", "مستخدم", "معلق", "متبقي"
            };

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
                ws.Cell(row, 5).Value = item.LeaveTypeName ?? "";
                ws.Cell(row, 6).Value = item.YearName ?? "";
                ws.Cell(row, 7).Value = item.EntitledDays;
                ws.Cell(row, 8).Value = item.CarriedForward;
                ws.Cell(row, 9).Value = item.AdditionalDays;
                ws.Cell(row, 10).Value = item.TotalEntitled;
                ws.Cell(row, 11).Value = item.UsedDays;
                ws.Cell(row, 12).Value = item.PendingDays;
                ws.Cell(row, 13).Value = item.RemainingDays;
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        private sealed class EmployeeGenderInfo
{
    public int EmployeeID { get; set; }
    public string? Gender { get; set; }
}

private sealed class LeaveTypeGenderInfo
{
    public int LeaveTypeID { get; set; }
    public string? LeaveTypeNameAr { get; set; }
    public int MaxDaysPerYear { get; set; }
    public string ApplicableGender { get; set; } = "A";
}
    }
}