using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class LoanService : BaseDbService
    {
    private readonly AuditService _audit;
private readonly SequenceService _sequence;
private readonly NotificationService _notif;

private readonly ModuleAuthorizationService _authorization;

public LoanService(IConfiguration configuration, AuditService audit, SequenceService sequence, NotificationService notif, ModuleAuthorizationService authorization) : base(configuration)
{
    _audit = audit;
    _sequence = sequence;
    _notif = notif;
    _authorization = authorization;
}

        public async Task<LoanPagedResult> GetLoansPagedAsync(LoanFilterDto filter)
        {
            using var connection = CreateConnection();
            filter ??= new LoanFilterDto();

            var where = @"
                WHERE (@LoanStatus IS NULL OR @LoanStatus = 0 OR l.LoanStatus = @LoanStatus)
                  AND (@DepartmentID IS NULL OR @DepartmentID = 0 OR e.DepartmentID = @DepartmentID)
                  AND (ISNULL(@SearchText, N'') = N''
                      OR e.FullNameAr LIKE N'%' + @SearchText + N'%'
                      OR e.EmployeeCode LIKE N'%' + @SearchText + N'%'
                      OR l.LoanNumber LIKE N'%' + @SearchText + N'%')";

            var countSql = @"SELECT COUNT(*) FROM dbo.EmployeeLoans l
                INNER JOIN dbo.Employees e ON l.EmployeeID = e.EmployeeID " + where;

            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
            {
                filter.SearchText, filter.DepartmentID, filter.LoanStatus
            });

            var dataSql = @"
                SELECT * FROM (
                    SELECT l.LoanID, l.LoanNumber, l.EmployeeID, e.EmployeeCode,
                        e.FullNameAr AS EmployeeName, d.DepartmentNameAr AS DepartmentName,
                        l.LoanType, CASE l.LoanType WHEN 1 THEN N'سلفة' WHEN 2 THEN N'قرض' END AS LoanTypeName,
                        l.LoanAmount, l.NumberOfInstallments, l.InstallmentAmount,
                        l.PaidAmount, l.RemainingAmount, l.LoanDate, l.StartDeductionDate,
                        l.LoanStatus,
                        CASE l.LoanStatus
                            WHEN 1 THEN N'مقدم' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'جاري السداد'
                            WHEN 4 THEN N'مسدد' WHEN 5 THEN N'مرفوض'
                        END AS LoanStatusName, l.LoanReason,
                        ROW_NUMBER() OVER (ORDER BY l.LoanDate DESC) AS RowNum
                    FROM dbo.EmployeeLoans l
                    INNER JOIN dbo.Employees e ON l.EmployeeID = e.EmployeeID
                    LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID " + where + @"
                ) ranked
                WHERE RowNum BETWEEN @StartRow AND @EndRow
                ORDER BY RowNum";

            var items = await connection.QueryAsync<LoanListDto>(dataSql, new
            {
                filter.SearchText, filter.DepartmentID, filter.LoanStatus,
                StartRow = (filter.PageNumber - 1) * filter.PageSize + 1,
                EndRow = filter.PageNumber * filter.PageSize
            });

            return new LoanPagedResult
            {
                Items = items.ToList(),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        public async Task<LoanEditDto?> GetLoanByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<LoanEditDto>(
                @"SELECT LoanID, LoanNumber, EmployeeID, LoanDate, LoanType,
                         LoanAmount, NumberOfInstallments, InstallmentAmount,
                         StartDeductionDate, LoanReason, LoanStatus, Notes
                  FROM dbo.EmployeeLoans WHERE LoanID = @ID", new { ID = id });
        }

        public async Task<int> InsertLoanAsync(LoanEditDto item, int userId)
        {
            // إصلاح 15: فحص الصلاحية داخل الخدمة نفسها، لا في الصفحة فقط
            await _authorization.RequireUserPermissionAsync(userId, "SCR_LOANS", p => p.CanAdd,
                "ليس لديك صلاحية إضافة سلف.");
            using var connection = CreateConnection();
            if (item.EmployeeID <= 0) throw new Exception("يجب اختيار الموظف");
            if (item.LoanAmount <= 0) throw new Exception("مبلغ السلفة يجب أن يكون أكبر من صفر");

            if (item.NumberOfInstallments > 0 && item.InstallmentAmount == 0)
                item.InstallmentAmount = Math.Ceiling(item.LoanAmount / item.NumberOfInstallments);

            item.LoanNumber = await _sequence.GetNextCodeAsync("LN");
            // تعيين تاريخ بداية الخصم تلقائياً لأول الشهر الحالي لو مش محدد
if (!item.StartDeductionDate.HasValue)
    item.StartDeductionDate = new DateTime(item.LoanDate.Year, item.LoanDate.Month, 1);

            var sql = @"
                INSERT INTO dbo.EmployeeLoans (LoanNumber, EmployeeID, LoanDate, LoanType,
                    LoanAmount, NumberOfInstallments, InstallmentAmount,
                    StartDeductionDate, LoanReason, LoanStatus, Notes, CreatedBy)
                VALUES (@LoanNumber, @EmployeeID, @LoanDate, @LoanType,
                    @LoanAmount, @NumberOfInstallments, @InstallmentAmount,
                    @StartDeductionDate, @LoanReason, @LoanStatus, @Notes, @CreatedBy);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                item.LoanNumber, item.EmployeeID, item.LoanDate, item.LoanType,
                item.LoanAmount, item.NumberOfInstallments, item.InstallmentAmount,
                item.StartDeductionDate, item.LoanReason, item.LoanStatus, item.Notes,
                CreatedBy = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "EmployeeLoans", newId.ToString(),
                moduleName: "SCR_LOANS", description: $"إضافة سلفة {item.LoanNumber}: {item.LoanAmount}");
                // إرسال إشعار للمسؤول
var empName = await connection.QueryFirstOrDefaultAsync<string>(
    "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
    new { ID = item.EmployeeID });

await _notif.CreateForPermissionHoldersAsync(
    notificationType: 2,
    title: "سلفة جديدة بانتظار الاعتماد",
    message: $"سلفة جديدة للموظف {empName} بمبلغ {item.LoanAmount:#,##0} جنيه - {item.LoanNumber}",
    priority: 2,
    permissionModuleCode: "SCR_LOANS",
    requiredPermission: ModulePermissionRequirement.Approve,
    relatedModule: "SCR_LOANS",
    relatedRecordId: newId,
    createdBy: userId,
    targetRoleId: 1);

            return newId;
        }

        public async Task UpdateLoanAsync(LoanEditDto item, int userId)
        {
            await _authorization.RequireUserPermissionAsync(userId, "SCR_LOANS", p => p.CanEdit,
                "ليس لديك صلاحية تعديل السلف.");
            using var connection = CreateConnection();
            await connection.ExecuteAsync(@"
                UPDATE dbo.EmployeeLoans SET
                    InstallmentAmount = @InstallmentAmount,
                    LoanStatus = @LoanStatus, Notes = @Notes,
                    ModifiedBy = @ModifiedBy, ModifiedDate = GETDATE()
                WHERE LoanID = @LoanID", new
            {
                item.LoanID, item.InstallmentAmount, item.LoanStatus,
                item.Notes, ModifiedBy = userId
            });

            await _audit.WriteAuditLogAsync(userId, 2, "EmployeeLoans", item.LoanID.ToString(),
                moduleName: "SCR_LOANS", description: $"تعديل سلفة #{item.LoanID}");
        }
        public async Task ApproveLoanAsync(int loanId, int userId)
{
    await _authorization.RequireUserPermissionAsync(userId, "SCR_LOANS", p => p.CanApprove,
        "ليس لديك صلاحية اعتماد السلف.");
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction();

    LoanEditDto? loan = null;
    try
    {
    // جلب EmployeeID للمعتمد
    var approverEmployeeId = await connection.ExecuteScalarAsync<int?>(
        "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID",
        new { UserID = userId }, transaction);

    if (approverEmployeeId == null || approverEmployeeId == 0)
        throw new Exception("لم يتم العثور على بيانات الموظف المرتبط بحسابك");

    // جلب بيانات السلفة
    loan = await connection.QueryFirstOrDefaultAsync<LoanEditDto>(
        @"SELECT LoanID, LoanNumber, EmployeeID, LoanAmount, NumberOfInstallments,
                 InstallmentAmount, StartDeductionDate, LoanStatus
          FROM dbo.EmployeeLoans WHERE LoanID = @LoanID",
        new { LoanID = loanId }, transaction);

    if (loan == null) throw new Exception("السلفة غير موجودة");
    if (loan.LoanStatus != 1) throw new Exception("لا يمكن اعتماد سلفة ليست في حالة (مقدم)");

    // تحديث حالة السلفة
    await connection.ExecuteAsync(@"
        UPDATE dbo.EmployeeLoans SET
            LoanStatus = 2,
            ApprovedBy = @ApprovedBy,
            ApprovedDate = GETDATE(),
            ModifiedBy = @ModifiedBy,
            ModifiedDate = GETDATE()
        WHERE LoanID = @LoanID",
        new { LoanID = loanId, ApprovedBy = approverEmployeeId, ModifiedBy = userId }, transaction);

    // توليد الأقساط
    var startDate = loan.StartDeductionDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    var installmentAmount = Math.Floor(loan.LoanAmount / loan.NumberOfInstallments * 100) / 100;
    var totalGenerated = 0m;

    for (int i = 1; i <= loan.NumberOfInstallments; i++)
    {
        var amount = installmentAmount;

        // القسط الأخير = الباقي
        if (i == loan.NumberOfInstallments)
            amount = loan.LoanAmount - totalGenerated;

        var dueDate = new DateTime(startDate.Year, startDate.Month, 1).AddMonths(i - 1);

        await connection.ExecuteAsync(@"
            INSERT INTO dbo.LoanInstallments
                (LoanID, InstallmentNumber, DueDate, Amount, PaidAmount, InstallmentStatus)
            VALUES
                (@LoanID, @InstallmentNumber, @DueDate, @Amount, 0, 1)",
            new
            {
                LoanID = loanId,
                InstallmentNumber = i,
                DueDate = dueDate,
                Amount = amount
            }, transaction);

        totalGenerated += amount;
    }

    // تحديث قيمة القسط في السلفة
    await connection.ExecuteAsync(@"
        UPDATE dbo.EmployeeLoans SET InstallmentAmount = @InstallmentAmount
        WHERE LoanID = @LoanID",
        new { LoanID = loanId, InstallmentAmount = installmentAmount }, transaction);

    // إصلاح 17: صرف السلفة قيدًا محاسبيًا داخل نفس المعاملة — مدين سلف الموظفين
    // 1106 ودائن الصندوق 1101 مع تخفيض رصيد الخزينة المخزّن؛ فاعتماد سلفة بلا
    // أثر مالي كان يجعل حساب السلف ينحرف سالبًا مع أقساط المسير.
    await PostLoanDisbursementJournalAsync(connection, transaction, loan, userId);

    transaction.Commit();
    }
    catch
    {
        try { transaction.Rollback(); } catch { }
        throw;
    }

    // Audit
    await _audit.WriteAuditLogAsync(userId, 2, "EmployeeLoans", loanId.ToString(),
        moduleName: "SCR_LOANS",
        description: $"اعتماد سلفة #{loan.LoanNumber} وتوليد {loan.NumberOfInstallments} قسط");

    // تحديد الإشعار القديم كتم تنفيذه
    await _notif.MarkRelatedAsActionedAsync("SCR_LOANS", loanId);

    // إرسال إشعار رد للموظف اللي سجّل السلفة
    var createdByUserId = await connection.ExecuteScalarAsync<int?>(
        "SELECT CreatedBy FROM dbo.EmployeeLoans WHERE LoanID = @LoanID",
        new { LoanID = loanId });

    var empName = await connection.QueryFirstOrDefaultAsync<string>(
        "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
        new { ID = loan.EmployeeID });

    if (createdByUserId.HasValue)
    {
        await _notif.CreateForAuthorizedUserAsync(
    notificationType: 3,
    title: "تم اعتماد السلفة",
    message: $"تم اعتماد السلفة {loan.LoanNumber} للموظف {empName} بمبلغ {loan.LoanAmount:#,##0} جنيه - {loan.NumberOfInstallments} قسط",
    priority: 2,
    targetUserId: createdByUserId.Value,
    permissionModuleCode: "SCR_LOANS",
    relatedModule: "SCR_LOANS",
    relatedRecordId: loanId,
    createdBy: userId);
    }
}
// إصلاح 17: قيد صرف السلفة عند الاعتماد (مدين 1106 سلف موظفين / دائن 1101 الصندوق)
private async Task PostLoanDisbursementJournalAsync(
    System.Data.IDbConnection connection, System.Data.IDbTransaction transaction,
    LoanEditDto loan, int userId)
{
    var amount = decimal.Round(loan.LoanAmount, 2, MidpointRounding.AwayFromZero);
    if (amount <= 0) throw new InvalidOperationException("مبلغ السلفة يجب أن يكون أكبر من صفر.");

    async Task<int> ResolveAccountAsync(string code, string label)
    {
        var account = await connection.QueryFirstOrDefaultAsync<(
            int AccountID, int AccountType, bool IsDetail, bool IsActive, bool CostCenterRequired)>(
            @"SELECT AccountID, AccountType,
                     CONVERT(bit,ISNULL(IsDetailAccount,0)) AS IsDetail,
                     CONVERT(bit,ISNULL(IsActive,0)) AS IsActive,
                     CONVERT(bit,ISNULL(CostCenterRequired,0)) AS CostCenterRequired
              FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountCode=@Code",
            new { Code = code }, transaction);
        if (account.AccountID <= 0)
            throw new InvalidOperationException($"حساب {label} {code} غير موجود في دليل الحسابات.");
        if (!account.IsActive || !account.IsDetail || account.AccountType != 1 || account.CostCenterRequired)
            throw new InvalidOperationException($"حساب {label} {code} يجب أن يكون حساب أصول (النوع 1) تفصيليًا نشطًا لا يتطلب مركز تكلفة.");
        return account.AccountID;
    }

    var loanAccountId = await ResolveAccountAsync("1106", "سلف الموظفين");
    var cashAccountId = await ResolveAccountAsync("1101", "الصندوق");

    var existingJournal = await connection.ExecuteScalarAsync<int>(
        @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK,HOLDLOCK)
          WHERE SourceDocType='LOAN' AND SourceDocID=@LoanID AND JournalStatus=2",
        new { LoanID = loan.LoanID }, transaction);
    if (existingJournal > 0) return; // قيد الصرف موجود؛ لا تكرار.

    var journalDate = DateTime.Today;
    var openPeriods = (await connection.QueryAsync<(int FiscalYearID, int PeriodID)>(
        @"SELECT fy.FiscalYearID, ap.PeriodID
          FROM dbo.FiscalYears fy WITH (UPDLOCK,HOLDLOCK)
          INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK,HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
          WHERE @JournalDate BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
            AND @JournalDate BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
            AND fy.YearStatus=1 AND ap.PeriodStatus=1
          ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
        new { JournalDate = journalDate }, transaction)).ToList();
    if (openPeriods.Count != 1)
        throw new InvalidOperationException("لا توجد فترة محاسبية مفتوحة واحدة تغطي تاريخ الصرف؛ لم يُنشأ القيد وأُلغي الاعتماد.");
    var period = openPeriods[0];

    var journalNumber = await connection.QueryFirstOrDefaultAsync<string>(
        "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;",
        transaction: transaction);
    if (string.IsNullOrWhiteSpace(journalNumber))
        throw new InvalidOperationException("تعذر توليد رقم قيد الصرف؛ أُلغي الاعتماد.");

    var description = $"صرف سلفة {loan.LoanNumber}";
    var journalId = await connection.ExecuteScalarAsync<int>(
        @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate, PostedBy, PostedDate)
          VALUES(@Number, @Date, @Description, @Reference, 'LOAN', @SourceDocID, @SourceDocNumber, @Amount, @Amount, @FiscalYearID, @PeriodID, 1, 2, 1, @UserID, GETDATE(), @PostedBy, GETDATE());
          SELECT CAST(SCOPE_IDENTITY() AS INT);",
        new
        {
            Number = journalNumber,
            Date = journalDate,
            Description = description,
            Reference = loan.LoanNumber,
            SourceDocID = loan.LoanID,
            SourceDocNumber = loan.LoanNumber,
            Amount = amount,
            period.FiscalYearID,
            period.PeriodID,
            UserID = userId,
            PostedBy = (int?)null
        }, transaction);

    await connection.ExecuteAsync(
        "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, EntityType, EntityID) VALUES(@JID, 1, @AccID, @Amount, 0, @Desc, 'Employee', @EmpID)",
        new { JID = journalId, AccID = loanAccountId, Amount = amount, Desc = description, EmpID = loan.EmployeeID }, transaction);
    await connection.ExecuteAsync(
        "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description) VALUES(@JID, 2, @AccID, 0, @Amount, @Desc)",
        new { JID = journalId, AccID = cashAccountId, Amount = amount, Desc = description }, transaction);

    foreach (var (accountId, delta) in new[] { (loanAccountId, amount), (cashAccountId, -amount) })
    {
        await connection.ExecuteAsync(
            "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@AccountID",
            new { Delta = delta, AccountID = accountId }, transaction);
        int? current = accountId;
        for (var depth = 0; depth < 10; depth++)
        {
            var parentId = await connection.ExecuteScalarAsync<int?>(
                "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID",
                new { ID = current }, transaction);
            if (!parentId.HasValue || parentId.Value <= 0) break;
            await connection.ExecuteAsync(
                "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                new { Delta = delta, ID = parentId.Value }, transaction);
            current = parentId.Value;
        }
    }

    // الخزينة المرتبطة بحساب الصندوق تُخفّض مخزّنًا بنفس حركة الصرف
    await connection.ExecuteAsync(
        "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amount WHERE AccountID=@AccountID",
        new { Amount = amount, AccountID = cashAccountId }, transaction);
}

public async Task RejectLoanAsync(int loanId, int userId)
{
    await _authorization.RequireUserPermissionAsync(userId, "SCR_LOANS", p => p.CanApprove,
        "ليس لديك صلاحية رفض السلف.");
    using var connection = CreateConnection();

    var loan = await connection.QueryFirstOrDefaultAsync<LoanEditDto>(
        "SELECT LoanID, LoanNumber, EmployeeID, LoanAmount, LoanStatus FROM dbo.EmployeeLoans WHERE LoanID = @LoanID",
        new { LoanID = loanId });

    if (loan == null) throw new Exception("السلفة غير موجودة");
    if (loan.LoanStatus != 1) throw new Exception("لا يمكن رفض سلفة ليست في حالة (مقدم)");

    await connection.ExecuteAsync(@"
        UPDATE dbo.EmployeeLoans SET
            LoanStatus = 5,
            ModifiedBy = @ModifiedBy,
            ModifiedDate = GETDATE()
        WHERE LoanID = @LoanID",
        new { LoanID = loanId, ModifiedBy = userId });

    await _audit.WriteAuditLogAsync(userId, 2, "EmployeeLoans", loanId.ToString(),
        moduleName: "SCR_LOANS",
        description: $"رفض سلفة #{loan.LoanNumber}");

    // تحديد الإشعار القديم كتم تنفيذه
    await _notif.MarkRelatedAsActionedAsync("SCR_LOANS", loanId);

    // إرسال إشعار رد للموظف اللي سجّل
    var createdByUserId = await connection.ExecuteScalarAsync<int?>(
        "SELECT CreatedBy FROM dbo.EmployeeLoans WHERE LoanID = @LoanID",
        new { LoanID = loanId });

    var empName = await connection.QueryFirstOrDefaultAsync<string>(
        "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
        new { ID = loan.EmployeeID });

    if (createdByUserId.HasValue)
    {
        await _notif.CreateForAuthorizedUserAsync(
    notificationType: 4,
    title: "تم رفض السلفة",
    message: $"تم رفض السلفة {loan.LoanNumber} للموظف {empName} بمبلغ {loan.LoanAmount:#,##0} جنيه",
    priority: 2,
    targetUserId: createdByUserId.Value,
    permissionModuleCode: "SCR_LOANS",
    relatedModule: "SCR_LOANS",
    relatedRecordId: loanId,
    createdBy: userId);
    }
}

        public async Task<byte[]> ExportLoansToExcelAsync(List<LoanListDto> items, int userId)
        {
            await Task.CompletedTask;
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("السلف");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير السلف — واي كي كوتينج";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;

            var headers = new[] { "#", "الرقم", "الموظف", "المبلغ", "القسط", "الأقساط", "المدفوع", "المتبقي" };
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
                ws.Cell(row, 2).Value = item.LoanNumber ?? "";
                ws.Cell(row, 3).Value = item.EmployeeName ?? "";
                ws.Cell(row, 4).Value = item.LoanAmount;
                ws.Cell(row, 5).Value = item.InstallmentAmount;
                ws.Cell(row, 6).Value = item.NumberOfInstallments;
                ws.Cell(row, 7).Value = item.PaidAmount;
                ws.Cell(row, 8).Value = item.RemainingAmount;
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}