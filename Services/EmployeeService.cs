using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class EmployeeService : BaseDbService
    {
        private readonly AuditService _audit;

        private readonly SequenceService _sequence;

        public EmployeeService(IConfiguration configuration, AuditService audit, SequenceService sequence) : base(configuration)
      {
       _audit = audit;
       _sequence = sequence;
      }

        // ==========================================
        // إحصائيات
        // ==========================================
        public async Task<EmployeeStatsDto> GetEmployeeStatsAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    COUNT(*) AS Total,
                    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS TotalActive,
                    SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) AS TotalInactive,
                    SUM(CASE WHEN ContractType = 1 THEN 1 ELSE 0 END) AS TotalPermanent,
                    SUM(CASE WHEN ContractType = 2 THEN 1 ELSE 0 END) AS TotalTemporary,
                    SUM(CASE WHEN ProbationEndDate IS NOT NULL AND ProbationEndDate > GETDATE() THEN 1 ELSE 0 END) AS TotalOnProbation,
                    ISNULL(SUM(CASE WHEN IsActive = 1 THEN TotalSalary ELSE 0 END), 0) AS TotalSalaries
                FROM dbo.Employees";

            return await connection.QueryFirstOrDefaultAsync<EmployeeStatsDto>(sql) ?? new EmployeeStatsDto();
        }

        // ==========================================
        // قائمة الموظفين مع صفحات
        // ==========================================
        public async Task<EmployeePagedResult> GetEmployeesPagedAsync(EmployeeFilterDto filter)
        {
            using var connection = CreateConnection();
            filter ??= new EmployeeFilterDto();

            var where = @"
                WHERE (@IsActive IS NULL OR e.IsActive = @IsActive)
                  AND (@DepartmentID IS NULL OR @DepartmentID = 0 OR e.DepartmentID = @DepartmentID)
                  AND (@JobTitleID IS NULL OR @JobTitleID = 0 OR e.JobTitleID = @JobTitleID)
                  AND (@ContractType IS NULL OR @ContractType = 0 OR e.ContractType = @ContractType)
                  AND (@EmployeeStatus IS NULL OR @EmployeeStatus = 0 OR e.EmployeeStatus = @EmployeeStatus)
                  AND (
                      ISNULL(@SearchText, N'') = N''
                      OR e.FullNameAr LIKE N'%' + @SearchText + N'%'
                      OR e.FullNameEn LIKE N'%' + @SearchText + N'%'
                      OR e.EmployeeCode LIKE N'%' + @SearchText + N'%'
                      OR e.NationalID LIKE N'%' + @SearchText + N'%'
                      OR e.Phone LIKE N'%' + @SearchText + N'%'
                      OR e.Mobile LIKE N'%' + @SearchText + N'%'
                      OR e.WorkEmail LIKE N'%' + @SearchText + N'%'
                  )";

            var countSql = $"SELECT COUNT(*) FROM dbo.Employees e {where}";
            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
            {
                filter.SearchText,
                filter.DepartmentID,
                filter.JobTitleID,
                filter.ContractType,
                filter.EmployeeStatus,
                filter.IsActive
            });

            var dataSql = $@"
                SELECT 
                    e.EmployeeID, e.EmployeeCode, e.FullNameAr, e.FullNameEn,
                    d.DepartmentNameAr, j.JobTitleNameAr, mgr.FullNameAr AS ManagerName,
                    CASE e.Gender WHEN 'M' THEN N'ذكر' WHEN 'F' THEN N'أنثى' END AS GenderName,
                    e.Mobile, e.WorkEmail, e.HireDate,
                    DATEDIFF(YEAR, e.HireDate, GETDATE()) AS YearsOfService,
                    CASE e.ContractType 
                        WHEN 1 THEN N'دائم' WHEN 2 THEN N'مؤقت' 
                        WHEN 3 THEN N'يومي' WHEN 4 THEN N'تدريب' 
                    END AS ContractTypeName,
                    CASE e.EmployeeStatus 
                        WHEN 1 THEN N'نشط' WHEN 2 THEN N'إجازة طويلة' 
                        WHEN 3 THEN N'موقوف' WHEN 4 THEN N'مستقيل' 
                        WHEN 5 THEN N'مفصول' WHEN 6 THEN N'متقاعد' 
                    END AS StatusName,
                    e.BasicSalary, e.TotalSalary, e.IsActive,
                    {totalCount} AS TotalCount
                FROM dbo.Employees e
                LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                LEFT JOIN dbo.JobTitles j ON e.JobTitleID = j.JobTitleID
                LEFT JOIN dbo.Employees mgr ON e.ManagerID = mgr.EmployeeID
                {where}
                ORDER BY e.FullNameAr
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var items = await connection.QueryAsync<EmployeeListDto>(dataSql, new
            {
                filter.SearchText,
                filter.DepartmentID,
                filter.JobTitleID,
                filter.ContractType,
                filter.EmployeeStatus,
                filter.IsActive,
                Offset = (filter.PageNumber - 1) * filter.PageSize,
                filter.PageSize
            });

            return new EmployeePagedResult
            {
                Items = items.ToList(),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        // ==========================================
        // بيانات موظف واحد
        // ==========================================
        public async Task<EmployeeEditDto?> GetEmployeeByIdAsync(int employeeId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT * FROM dbo.Employees WHERE EmployeeID = @ID";

            return await connection.QueryFirstOrDefaultAsync<EmployeeEditDto>(sql, new { ID = employeeId });
        }

        // ==========================================
        // توليد كود موظف
        // ==========================================
        public async Task<string> GenerateEmployeeCodeAsync()
{
    return await _sequence.GetNextCodeAsync("HR-EMP");
}

        // ==========================================
        // إضافة موظف
        // ==========================================
        public async Task<int> InsertEmployeeAsync(EmployeeEditDto emp, int userId)
        {
            using var connection = CreateConnection();

            ValidateEmployee(emp);

            var code = emp.EmployeeCode;
            if (string.IsNullOrWhiteSpace(code))
    code = await GenerateEmployeeCodeAsync();

            var sql = @"
                INSERT INTO dbo.Employees (
                    EmployeeCode, FirstNameAr, SecondNameAr, ThirdNameAr, LastNameAr,
                    FirstNameEn, LastNameEn, NationalID, BirthDate, Gender,
                    MaritalStatus, NumberOfChildren, Nationality, Religion,
                    Address, City, Phone, Mobile, PersonalEmail, WorkEmail,
                    EmergencyContactName, EmergencyContactPhone, EmergencyContactRelation,
                    DepartmentID, JobTitleID, ManagerID, HireDate, ContractType,
                    ContractStartDate, ContractEndDate, ProbationEndDate,
                    EmployeeStatus,
                    BasicSalary, TransportAllowance, HousingAllowance,
                    PhoneAllowance, FoodAllowance, OtherAllowances,
                    InsuranceNumber, InsuranceSalary, InsurancePercEmployee, InsurancePercCompany,
                    IsInsured, InsuranceStartDate,
                    BankName, BankAccountNumber, IBAN, PaymentMethod,
                    WorkingHoursPerDay, WeeklyDaysOff, AnnualLeaveBalance,
                    Notes, IsActive, CreatedBy, CreatedDate
                ) VALUES (
                    @EmployeeCode, @FirstNameAr, @SecondNameAr, @ThirdNameAr, @LastNameAr,
                    @FirstNameEn, @LastNameEn, @NationalID, @BirthDate, @Gender,
                    @MaritalStatus, @NumberOfChildren, @Nationality, @Religion,
                    @Address, @City, @Phone, @Mobile, @PersonalEmail, @WorkEmail,
                    @EmergencyContactName, @EmergencyContactPhone, @EmergencyContactRelation,
                    @DepartmentID, @JobTitleID, @ManagerID, @HireDate, @ContractType,
                    @ContractStartDate, @ContractEndDate, @ProbationEndDate,
                    @EmployeeStatus,
                    @BasicSalary, @TransportAllowance, @HousingAllowance,
                    @PhoneAllowance, @FoodAllowance, @OtherAllowances,
                    @InsuranceNumber, @InsuranceSalary, @InsurancePercEmployee, @InsurancePercCompany,
                    @IsInsured, @InsuranceStartDate,
                    @BankName, @BankAccountNumber, @IBAN, @PaymentMethod,
                    @WorkingHoursPerDay, @WeeklyDaysOff, @AnnualLeaveBalance,
                    @Notes, @IsActive, @CreatedBy, GETDATE()
                );
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                EmployeeCode = code,
                emp.FirstNameAr, emp.SecondNameAr, emp.ThirdNameAr, emp.LastNameAr,
                emp.FirstNameEn, emp.LastNameEn, emp.NationalID, emp.BirthDate, emp.Gender,
                emp.MaritalStatus, emp.NumberOfChildren, emp.Nationality, emp.Religion,
                emp.Address, emp.City, emp.Phone, emp.Mobile, emp.PersonalEmail, emp.WorkEmail,
                emp.EmergencyContactName, emp.EmergencyContactPhone, emp.EmergencyContactRelation,
                emp.DepartmentID, emp.JobTitleID, emp.ManagerID, emp.HireDate, emp.ContractType,
                emp.ContractStartDate, emp.ContractEndDate, emp.ProbationEndDate,
                emp.EmployeeStatus,
                emp.BasicSalary, emp.TransportAllowance, emp.HousingAllowance,
                emp.PhoneAllowance, emp.FoodAllowance, emp.OtherAllowances,
                emp.InsuranceNumber, emp.InsuranceSalary, emp.InsurancePercEmployee, emp.InsurancePercCompany,
                emp.IsInsured, emp.InsuranceStartDate,
                emp.BankName, emp.BankAccountNumber, emp.IBAN, emp.PaymentMethod,
                emp.WorkingHoursPerDay, emp.WeeklyDaysOff, emp.AnnualLeaveBalance,
                emp.Notes, emp.IsActive,
                CreatedBy = userId
            });

            await _audit.WriteAuditLogAsync(
                userId, 1, "Employees", newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new { code, emp.FirstNameAr, emp.LastNameAr }),
                moduleName: "SCR_EMP",
                description: $"إضافة موظف: {emp.FirstNameAr} {emp.LastNameAr}");

            return newId;
        }

        // ==========================================
        // تعديل موظف
        // ==========================================
        public async Task UpdateEmployeeAsync(EmployeeEditDto emp, int userId)
        {
            using var connection = CreateConnection();

            ValidateEmployee(emp);

            var sql = @"
                UPDATE dbo.Employees SET
                    FirstNameAr = @FirstNameAr, SecondNameAr = @SecondNameAr,
                    ThirdNameAr = @ThirdNameAr, LastNameAr = @LastNameAr,
                    FirstNameEn = @FirstNameEn, LastNameEn = @LastNameEn,
                    NationalID = @NationalID, BirthDate = @BirthDate, Gender = @Gender,
                    MaritalStatus = @MaritalStatus, NumberOfChildren = @NumberOfChildren,
                    Nationality = @Nationality, Religion = @Religion,
                    Address = @Address, City = @City, Phone = @Phone, Mobile = @Mobile,
                    PersonalEmail = @PersonalEmail, WorkEmail = @WorkEmail,
                    EmergencyContactName = @EmergencyContactName,
                    EmergencyContactPhone = @EmergencyContactPhone,
                    EmergencyContactRelation = @EmergencyContactRelation,
                    DepartmentID = @DepartmentID, JobTitleID = @JobTitleID,
                    ManagerID = @ManagerID, HireDate = @HireDate,
                    ContractType = @ContractType,
                    ContractStartDate = @ContractStartDate, ContractEndDate = @ContractEndDate,
                    ProbationEndDate = @ProbationEndDate,
                    EmployeeStatus = @EmployeeStatus,
                    BasicSalary = @BasicSalary, TransportAllowance = @TransportAllowance,
                    HousingAllowance = @HousingAllowance, PhoneAllowance = @PhoneAllowance,
                    FoodAllowance = @FoodAllowance, OtherAllowances = @OtherAllowances,
                    InsuranceNumber = @InsuranceNumber, InsuranceSalary = @InsuranceSalary,
                    InsurancePercEmployee = @InsurancePercEmployee,
                    InsurancePercCompany = @InsurancePercCompany,
                    IsInsured = @IsInsured, InsuranceStartDate = @InsuranceStartDate,
                    BankName = @BankName, BankAccountNumber = @BankAccountNumber,
                    IBAN = @IBAN, PaymentMethod = @PaymentMethod,
                    WorkingHoursPerDay = @WorkingHoursPerDay,
                    WeeklyDaysOff = @WeeklyDaysOff,
                    AnnualLeaveBalance = @AnnualLeaveBalance,
                    Notes = @Notes, IsActive = @IsActive,
                    ModifiedBy = @ModifiedBy, ModifiedDate = GETDATE()
                WHERE EmployeeID = @EmployeeID";

            await connection.ExecuteAsync(sql, new
            {
                emp.EmployeeID,
                emp.FirstNameAr, emp.SecondNameAr, emp.ThirdNameAr, emp.LastNameAr,
                emp.FirstNameEn, emp.LastNameEn,
                emp.NationalID, emp.BirthDate, emp.Gender,
                emp.MaritalStatus, emp.NumberOfChildren, emp.Nationality, emp.Religion,
                emp.Address, emp.City, emp.Phone, emp.Mobile,
                emp.PersonalEmail, emp.WorkEmail,
                emp.EmergencyContactName, emp.EmergencyContactPhone, emp.EmergencyContactRelation,
                emp.DepartmentID, emp.JobTitleID, emp.ManagerID, emp.HireDate,
                emp.ContractType, emp.ContractStartDate, emp.ContractEndDate, emp.ProbationEndDate,
                emp.EmployeeStatus,
                emp.BasicSalary, emp.TransportAllowance, emp.HousingAllowance,
                emp.PhoneAllowance, emp.FoodAllowance, emp.OtherAllowances,
                emp.InsuranceNumber, emp.InsuranceSalary,
                emp.InsurancePercEmployee, emp.InsurancePercCompany,
                emp.IsInsured, emp.InsuranceStartDate,
                emp.BankName, emp.BankAccountNumber, emp.IBAN, emp.PaymentMethod,
                emp.WorkingHoursPerDay, emp.WeeklyDaysOff, emp.AnnualLeaveBalance,
                emp.Notes, emp.IsActive,
                ModifiedBy = userId
            });

            await _audit.WriteAuditLogAsync(
                userId, 2, "Employees", emp.EmployeeID.ToString(),
                moduleName: "SCR_EMP",
                description: $"تعديل موظف: {emp.FirstNameAr} {emp.LastNameAr}");
        }

        // ==========================================
        // تعطيل / تفعيل
        // ==========================================
        public async Task<(bool Success, string Message)> SetEmployeeActiveStatusAsync(
            int employeeId, bool isActive, int userId)
        {
            using var connection = CreateConnection();

            var emp = await connection.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT EmployeeID, FirstNameAr, LastNameAr, EmployeeStatus 
                  FROM dbo.Employees WHERE EmployeeID = @ID", new { ID = employeeId });

            if (emp == null)
                return (false, "الموظف غير موجود");

            await connection.ExecuteAsync(@"
                UPDATE dbo.Employees 
                SET IsActive = @IsActive, ModifiedDate = GETDATE()
                WHERE EmployeeID = @ID",
                new { ID = employeeId, IsActive = isActive });

            var name = $"{emp.FirstNameAr} {emp.LastNameAr}";
            await _audit.WriteAuditLogAsync(
                userId, isActive ? (byte)2 : (byte)3,
                "Employees", employeeId.ToString(),
                moduleName: "SCR_EMP",
                description: $"{(isActive ? "إعادة تفعيل" : "تعطيل")} موظف: {name}");

            return (true, isActive ? "تمت إعادة التفعيل بنجاح" : "تم التعطيل بنجاح");
        }

        // ==========================================
        // Validation
        // ==========================================
        private void ValidateEmployee(EmployeeEditDto emp)
        {
            if (string.IsNullOrWhiteSpace(emp.FirstNameAr))
                throw new Exception("الاسم الأول بالعربي مطلوب");
            if (string.IsNullOrWhiteSpace(emp.LastNameAr))
                throw new Exception("اسم العائلة بالعربي مطلوب");
            if (emp.HireDate == null)
                throw new Exception("تاريخ التعيين مطلوب");
            if (emp.BasicSalary < 0)
                throw new Exception("الراتب الأساسي لا يمكن أن يكون سالباً");
        }

        // ==========================================
        // أسماء مساعدة
        // ==========================================
        public static string GetContractTypeName(int type) => type switch
        {
            1 => "دائم",
            2 => "مؤقت",
            3 => "يومي",
            4 => "تدريب",
            _ => "—"
        };

        public static string GetEmployeeStatusName(int status) => status switch
        {
            1 => "نشط",
            2 => "إجازة طويلة",
            3 => "موقوف",
            4 => "مستقيل",
            5 => "مفصول",
            6 => "متقاعد",
            _ => "—"
        };

        public static string GetPaymentMethodName(int method) => method switch
        {
            1 => "تحويل بنكي",
            2 => "كاش",
            3 => "شيك",
            _ => "—"
        };

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportEmployeesToExcelAsync(List<EmployeeListDto> employees, int userId)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("الموظفين");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير الموظفين — واي كي كوتينج";
            ws.Range(1, 1, 1, 12).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 12).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 12).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 12).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 12).Style.Font.FontColor = ClosedXML.Excel.XLColor.Gray;

            var headers = new[] { "#", "الكود", "الاسم", "القسم", "المسمى", "النوع", "الهاتف", "تاريخ التعيين", "سنوات الخدمة", "نوع العقد", "الراتب الأساسي", "إجمالي الراتب" };

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
            foreach (var e in employees)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = e.EmployeeCode ?? "";
                ws.Cell(row, 3).Value = e.FullNameAr ?? "";
                ws.Cell(row, 4).Value = e.DepartmentNameAr ?? "—";
                ws.Cell(row, 5).Value = e.JobTitleNameAr ?? "—";
                ws.Cell(row, 6).Value = e.GenderName ?? "";
                ws.Cell(row, 7).Value = e.Mobile ?? "—";
                ws.Cell(row, 8).Value = e.HireDate?.ToString("dd/MM/yyyy") ?? "";
                ws.Cell(row, 9).Value = e.YearsOfService;
                ws.Cell(row, 10).Value = e.ContractTypeName ?? "";
                ws.Cell(row, 11).Value = e.BasicSalary;
                ws.Cell(row, 12).Value = e.TotalSalary;

                ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }

            ws.Columns().AdjustToContents();
            ws.Column(3).Width = 30;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId, 5, "Employees",
                moduleName: "SCR_EMP",
                description: $"تصدير تقرير الموظفين ({employees.Count} سجل)");

            return stream.ToArray();
        }
    }
}