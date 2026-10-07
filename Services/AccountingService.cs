using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class AccountingService : BaseDbService
    {
        private readonly AuditService _audit;

        public AccountingService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        // ==========================================
        // شجرة الحسابات - على الجدول الحقيقي ChartOfAccounts
        // ==========================================
        // حساب الأرصدة الفعلية من القيود - الرصيد الحقيقي = افتتاحي + مدين - دائن
        public async Task<List<ChartOfAccountDto>> GetChartOfAccountsAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"
                WITH JournalSums AS (
                    SELECT jd.AccountID,
                           SUM(ISNULL(jd.DebitAmount,0)) AS TotalDebit,
                           SUM(ISNULL(jd.CreditAmount,0)) AS TotalCredit
                    FROM dbo.JournalEntryDetails jd
                    INNER JOIN dbo.JournalEntries je ON jd.JournalID = je.JournalID
                    WHERE ISNULL(je.JournalStatus,1) != 3
                    GROUP BY jd.AccountID
                )
                SELECT coa.AccountID, coa.AccountCode, coa.AccountNameAr, coa.AccountNameEn, coa.ParentAccountID, p.AccountNameAr AS ParentAccountNameAr,
                       coa.AccountLevel, coa.AccountType,
                       CASE coa.AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                       coa.AccountNature, coa.IsDetailAccount, coa.CurrencyID, coa.CostCenterRequired, 
                       ISNULL(coa.OpeningBalance,0) AS OpeningBalance,
                       ISNULL(coa.OpeningBalance,0) + ISNULL(js.TotalDebit,0) - ISNULL(js.TotalCredit,0) AS CurrentBalance,
                       coa.LinkedEntityType, coa.LinkedEntityID, coa.IsSystemAccount, coa.IsCashFlowAccount, coa.SortOrder, coa.Description, coa.IsActive,
                       (SELECT COUNT(*) FROM dbo.ChartOfAccounts c WHERE c.ParentAccountID=coa.AccountID) AS ChildrenCount
                FROM dbo.ChartOfAccounts coa
                LEFT JOIN dbo.ChartOfAccounts p ON coa.ParentAccountID=p.AccountID
                LEFT JOIN JournalSums js ON js.AccountID = coa.AccountID
                ORDER BY coa.AccountCode";
                var result = await connection.QueryAsync<ChartOfAccountDto>(sql);
                var all = result.ToList();

                var lookup = all.ToDictionary(x => x.AccountID);
                foreach (var acc in all.OrderByDescending(x => x.AccountLevel))
                {
                    if (acc.ParentAccountID.HasValue && lookup.ContainsKey(acc.ParentAccountID.Value))
                    {
                        lookup[acc.ParentAccountID.Value].CurrentBalance += acc.CurrentBalance;
                    }
                }

                var roots = new List<ChartOfAccountDto>();
                foreach (var acc in all)
                {
                    if (acc.ParentAccountID.HasValue && lookup.ContainsKey(acc.ParentAccountID.Value))
                        lookup[acc.ParentAccountID.Value].Children.Add(acc);
                    else
                        roots.Add(acc);
                }
                return roots;
            }
            catch
            {
                // Fallback بسيط بدون CTE لو فشل
                var sql2 = @"SELECT coa.AccountID, coa.AccountCode, coa.AccountNameAr, coa.AccountNameEn, coa.ParentAccountID, p.AccountNameAr AS ParentAccountNameAr,
                               coa.AccountLevel, coa.AccountType,
                               CASE coa.AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                               coa.AccountNature, coa.IsDetailAccount, coa.CurrencyID, coa.CostCenterRequired, coa.OpeningBalance, coa.CurrentBalance,
                               coa.LinkedEntityType, coa.LinkedEntityID, coa.IsSystemAccount, coa.IsCashFlowAccount, coa.SortOrder, coa.Description, coa.IsActive,
                               (SELECT COUNT(*) FROM dbo.ChartOfAccounts c WHERE c.ParentAccountID=coa.AccountID) AS ChildrenCount
                        FROM dbo.ChartOfAccounts coa
                        LEFT JOIN dbo.ChartOfAccounts p ON coa.ParentAccountID=p.AccountID
                        ORDER BY coa.AccountCode";
                var result = await connection.QueryAsync<ChartOfAccountDto>(sql2);
                var all = result.ToList();
                var lookup = all.ToDictionary(x => x.AccountID);
                var roots = new List<ChartOfAccountDto>();
                foreach (var acc in all)
                {
                    if (acc.ParentAccountID.HasValue && lookup.ContainsKey(acc.ParentAccountID.Value))
                        lookup[acc.ParentAccountID.Value].Children.Add(acc);
                    else
                        roots.Add(acc);
                }
                return roots;
            }
        }

        public async Task<List<ChartOfAccountDto>> GetFlatAccountsAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"
                WITH JournalSums AS (
                    SELECT jd.AccountID,
                           SUM(ISNULL(jd.DebitAmount,0)) AS TotalDebit,
                           SUM(ISNULL(jd.CreditAmount,0)) AS TotalCredit
                    FROM dbo.JournalEntryDetails jd
                    INNER JOIN dbo.JournalEntries je ON jd.JournalID = je.JournalID
                    WHERE ISNULL(je.JournalStatus,1) != 3
                    GROUP BY jd.AccountID
                )
                SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType,
                       CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                       AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, 
                       ISNULL(OpeningBalance,0) AS OpeningBalance,
                       ISNULL(OpeningBalance,0) + ISNULL(js.TotalDebit,0) - ISNULL(js.TotalCredit,0) AS CurrentBalance,
                       LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, 0 AS ChildrenCount
                FROM dbo.ChartOfAccounts coa
                LEFT JOIN JournalSums js ON js.AccountID = coa.AccountID
                WHERE IsActive=1 ORDER BY AccountCode";
                var result = await connection.QueryAsync<ChartOfAccountDto>(sql);
                return result.ToList();
            }
            catch
            {
                // Fallback بدون CTE وبدون IsActive لو العمود مش موجود
                try
                {
                    var sql2 = @"SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType,
                               CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                               AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, OpeningBalance, CurrentBalance,
                               LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, 0 AS ChildrenCount
                        FROM dbo.ChartOfAccounts WHERE IsActive=1 ORDER BY AccountCode";
                    var result = await connection.QueryAsync<ChartOfAccountDto>(sql2);
                    return result.ToList();
                }
                catch
                {
                    var sql3 = @"SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType,
                               CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                               AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, OpeningBalance, CurrentBalance,
                               LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, 1 AS IsActive, 0 AS ChildrenCount
                        FROM dbo.ChartOfAccounts ORDER BY AccountCode";
                    var result = await connection.QueryAsync<ChartOfAccountDto>(sql3);
                    return result.ToList();
                }
            }
        }

        public async Task<ChartOfAccountDto?> GetAccountByIdAsync(int id)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType, AccountNature, IsDetailAccount,
                               CurrencyID, CostCenterRequired, OpeningBalance, CurrentBalance, LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount,
                               SortOrder, Description, IsActive, 0 AS ChildrenCount
                        FROM dbo.ChartOfAccounts WHERE AccountID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ChartOfAccountDto>(sql, new { ID = id });
        }

        public async Task<int> InsertAccountAsync(ChartOfAccountDto dto, int userId)
        {
            using var connection = CreateConnection();
            var parentLevel = 0;
            if (dto.ParentAccountID.HasValue && dto.ParentAccountID > 0)
                parentLevel = await connection.ExecuteScalarAsync<int>("SELECT ISNULL(AccountLevel,1) FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = dto.ParentAccountID });

            dto.AccountLevel = parentLevel + 1;
            if (string.IsNullOrWhiteSpace(dto.AccountCode))
            {
                var prefix = dto.ParentAccountID.HasValue ? await connection.QueryFirstOrDefaultAsync<string>("SELECT AccountCode FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = dto.ParentAccountID }) : "1";
                var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*)+1 FROM dbo.ChartOfAccounts WHERE ParentAccountID=@PID", new { PID = dto.ParentAccountID });
                dto.AccountCode = $"{prefix}{count:00}";
            }

            var sql = @"INSERT INTO dbo.ChartOfAccounts(AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType, AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, OpeningBalance, CurrentBalance, LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, CreatedBy, CreatedDate)
                        VALUES(@AccountCode, @AccountNameAr, @AccountNameEn, NULLIF(@ParentAccountID,0), @AccountLevel, @AccountType, @AccountNature, @IsDetailAccount, NULLIF(@CurrencyID,0), @CostCenterRequired, @OpeningBalance, @OpeningBalance, @LinkedEntityType, @LinkedEntityID, @IsSystemAccount, @IsCashFlowAccount, @SortOrder, @Description, @IsActive, @UserID, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.AccountCode,
                dto.AccountNameAr,
                dto.AccountNameEn,
                dto.ParentAccountID,
                dto.AccountLevel,
                dto.AccountType,
                dto.AccountNature,
                dto.IsDetailAccount,
                dto.CurrencyID,
                dto.CostCenterRequired,
                dto.OpeningBalance,
                dto.LinkedEntityType,
                dto.LinkedEntityID,
                dto.IsSystemAccount,
                dto.IsCashFlowAccount,
                dto.SortOrder,
                dto.Description,
                dto.IsActive,
                UserID = userId
            });

            try { await _audit.WriteAuditLogAsync(userId, 1, "ChartOfAccounts", newId.ToString(), moduleName: "SCR_COA", description: $"إنشاء حساب: {dto.AccountCode} - {dto.AccountNameAr}"); } catch { }
            return newId;
        }

        public async Task UpdateAccountAsync(ChartOfAccountDto dto, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.ChartOfAccounts SET AccountNameAr=@AccountNameAr, AccountNameEn=@AccountNameEn, AccountType=@AccountType, AccountNature=@AccountNature,
                        IsDetailAccount=@IsDetailAccount, CurrencyID=NULLIF(@CurrencyID,0), CostCenterRequired=@CostCenterRequired,
                        LinkedEntityType=@LinkedEntityType, LinkedEntityID=@LinkedEntityID, Description=@Description, IsActive=@IsActive,
                        ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE AccountID=@AccountID";
            await connection.ExecuteAsync(sql, new
            {
                dto.AccountID,
                dto.AccountNameAr,
                dto.AccountNameEn,
                dto.AccountType,
                dto.AccountNature,
                dto.IsDetailAccount,
                dto.CurrencyID,
                dto.CostCenterRequired,
                dto.LinkedEntityType,
                dto.LinkedEntityID,
                dto.Description,
                dto.IsActive,
                UserID = userId
            });
            try { await _audit.WriteAuditLogAsync(userId, 2, "ChartOfAccounts", dto.AccountID.ToString(), moduleName: "SCR_COA", description: $"تعديل حساب: {dto.AccountCode}"); } catch { }
        }

        public async Task DeleteAccountAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var hasChildren = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ChartOfAccounts WHERE ParentAccountID=@ID", new { ID = id });
            if (hasChildren > 0) throw new Exception("لا يمكن حذف حساب له حسابات فرعية");

            var hasTrans = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.JournalEntryDetails WHERE AccountID=@ID", new { ID = id });
            if (hasTrans > 0) throw new Exception("لا يمكن حذف حساب به حركات محاسبية");

            await connection.ExecuteAsync("DELETE FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = id });
            try { await _audit.WriteAuditLogAsync(userId, 3, "ChartOfAccounts", id.ToString(), moduleName: "SCR_COA", description: "حذف حساب"); } catch { }
        }

        // ==========================================
        // القيود المحاسبية - على الجداول الحقيقية
        // ==========================================
        public async Task<List<JournalEntryListDto>> GetJournalsAsync(DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT je.JournalID, je.JournalNumber, je.JournalDate, je.Description, je.Reference, je.SourceDocType, je.SourceDocNumber,
                               je.TotalDebit, je.TotalCredit, je.JournalStatus, CASE je.JournalStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'مرحل' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                               je.IsAutoGenerated, ISNULL(je.JournalType,1) AS JournalType, je.CreatedDate, su.Username AS CreatedByName,
                               (SELECT COUNT(*) FROM dbo.JournalEntryDetails jd WHERE jd.JournalID=je.JournalID) AS DetailsCount
                        FROM dbo.JournalEntries je
                        LEFT JOIN dbo.SystemUsers su ON je.CreatedBy=su.UserID
                        WHERE 1=1 AND (@From IS NULL OR je.JournalDate >= @From) AND (@To IS NULL OR je.JournalDate <= @To)
                        ORDER BY je.JournalDate DESC, je.JournalID DESC";
            var result = await connection.QueryAsync<JournalEntryListDto>(sql, new { From = from, To = to });
            return result.ToList();
        }

        public async Task<JournalEntryDto?> GetJournalByIdAsync(int id)
        {
            using var connection = CreateConnection();
            try
            {
                var header = await connection.QueryFirstOrDefaultAsync<JournalEntryDto>(
                    @"SELECT je.JournalID, je.JournalNumber, je.JournalDate, je.Description, je.Reference, je.SourceDocType, 
                             CAST(je.SourceDocID AS NVARCHAR(50)) AS SourceDocID, je.SourceDocNumber,
                             je.TotalDebit, je.TotalCredit, je.FiscalYearID, fy.YearName AS FiscalYearName, je.PeriodID, ap.PeriodName,
                             je.JournalType, je.JournalStatus,
                             CASE je.JournalStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'مرحل' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                             je.IsAutoGenerated, je.CreatedBy, su.Username AS CreatedByName, je.CreatedDate
                      FROM dbo.JournalEntries je
                      LEFT JOIN dbo.FiscalYears fy ON je.FiscalYearID=fy.FiscalYearID
                      LEFT JOIN dbo.AccountingPeriods ap ON je.PeriodID=ap.PeriodID
                      LEFT JOIN dbo.SystemUsers su ON je.CreatedBy=su.UserID
                      WHERE je.JournalID=@ID",
                    new { ID = id });
                if (header == null) return null;

                var details = await connection.QueryAsync<JournalDetailDto>(
                    @"SELECT jd.JournalDetailID, jd.JournalID, jd.AccountID, coa.AccountCode, coa.AccountNameAr, jd.DebitAmount, jd.CreditAmount, jd.Description, jd.LineNumber, jd.CostCenterID, cc.CostCenterNameAr
                      FROM dbo.JournalEntryDetails jd 
                      INNER JOIN dbo.ChartOfAccounts coa ON jd.AccountID=coa.AccountID
                      LEFT JOIN dbo.CostCenters cc ON jd.CostCenterID=cc.CostCenterID
                      WHERE jd.JournalID=@ID ORDER BY jd.LineNumber",
                    new { ID = id });
                header.Details = details.ToList();
                return header;
            }
            catch
            {
                // Fallback بدون Joins
                var header = await connection.QueryFirstOrDefaultAsync<JournalEntryDto>(
                    "SELECT JournalID, JournalNumber, JournalDate, Description, Reference, SourceDocType, CAST(SourceDocID AS NVARCHAR(50)) AS SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate FROM dbo.JournalEntries WHERE JournalID=@ID",
                    new { ID = id });
                if (header == null) return null;
                var details = await connection.QueryAsync<JournalDetailDto>(
                    @"SELECT jd.JournalDetailID, jd.JournalID, jd.AccountID, coa.AccountCode, coa.AccountNameAr, jd.DebitAmount, jd.CreditAmount, jd.Description, jd.LineNumber, jd.CostCenterID, cc.CostCenterNameAr
                      FROM dbo.JournalEntryDetails jd 
                      INNER JOIN dbo.ChartOfAccounts coa ON jd.AccountID=coa.AccountID
                      LEFT JOIN dbo.CostCenters cc ON jd.CostCenterID=cc.CostCenterID
                      WHERE jd.JournalID=@ID ORDER BY jd.LineNumber",
                    new { ID = id });
                header.Details = details.ToList();
                return header;
            }
        }

        public async Task<string> GenerateJournalNoAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"JV-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"JV-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertJournalAsync(JournalEntryDto dto, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                if (string.IsNullOrWhiteSpace(dto.JournalNumber))
                    dto.JournalNumber = await GenerateJournalNoAsync();

                // فلترة الأسطر الفارغة قبل الحساب
                var validDetails = dto.Details.Where(d => d.AccountID > 0 && (d.DebitAmount > 0 || d.CreditAmount > 0)).ToList();
                if (validDetails.Count < 2) throw new Exception("يجب إدخال حسابين على الأقل بمبالغ");

                dto.TotalDebit = validDetails.Sum(d => d.DebitAmount);
                dto.TotalCredit = validDetails.Sum(d => d.CreditAmount);
                if (Math.Abs(dto.TotalDebit - dto.TotalCredit) > 0.01m) throw new Exception($"القيد غير متوازن - مدين: {dto.TotalDebit:#,##0.00} ≠ دائن: {dto.TotalCredit:#,##0.00}");
                if (dto.TotalDebit == 0) throw new Exception("لا يمكن حفظ قيد صفري");

                // تحديد السنة المالية والفترة المحاسبية تلقائياً من تاريخ القيد
                int? fiscalYearId = null;
                int? periodId = null;
                try
                {
                    fiscalYearId = await connection.ExecuteScalarAsync<int?>(
                        "SELECT TOP 1 FiscalYearID FROM dbo.FiscalYears WHERE @JDate BETWEEN StartDate AND EndDate ORDER BY FiscalYearID DESC",
                        new { JDate = dto.JournalDate }, trans);
                    if (fiscalYearId.HasValue)
                    {
                        periodId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 PeriodID FROM dbo.AccountingPeriods WHERE FiscalYearID=@FYID AND @JDate BETWEEN StartDate AND EndDate ORDER BY PeriodNumber",
                            new { FYID = fiscalYearId.Value, JDate = dto.JournalDate }, trans);
                    }
                }
                catch { /* تجاهل لو الجداول غير موجودة */ }

                int? srcIdInt = null;
                if (!string.IsNullOrWhiteSpace(dto.SourceDocID) && int.TryParse(dto.SourceDocID, out var parsedSrc)) srcIdInt = parsedSrc;

                var jId = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate)
                      VALUES(@JournalNumber, @JournalDate, @Description, @Reference, @SourceDocType, @SourceDocID, @SourceDocNumber, @TotalDebit, @TotalCredit, @FiscalYearID, @PeriodID, @JournalType, 1, @IsAutoGenerated, @UserID, GETDATE());
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        dto.JournalNumber,
                        dto.JournalDate,
                        dto.Description,
                        dto.Reference,
                        dto.SourceDocType,
                        SourceDocID = srcIdInt,
                        dto.SourceDocNumber,
                        dto.TotalDebit,
                        dto.TotalCredit,
                        FiscalYearID = fiscalYearId,
                        PeriodID = periodId,
                        dto.JournalType,
                        dto.IsAutoGenerated,
                        UserID = userId
                    }, trans);

                int line = 1;
                foreach (var d in validDetails)
                {
                    if (d.AccountID <= 0) continue;
                    if (d.DebitAmount == 0 && d.CreditAmount == 0) continue;
                    if (d.DebitAmount > 0 && d.CreditAmount > 0) throw new Exception($"السطر {line}: لا يمكن أن يكون مدين ودائن معاً");

                    await connection.ExecuteAsync(
                        "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID) VALUES(@JID, @Line, @AccID, @Debit, @Credit, @Desc, NULLIF(@CCID,0))",
                        new { JID = jId, Line = line++, AccID = d.AccountID, Debit = d.DebitAmount, Credit = d.CreditAmount, Desc = d.Description, CCID = d.CostCenterID ?? 0 }, trans);
                }

                trans.Commit();
                try { await _audit.WriteAuditLogAsync(userId, 1, "JournalEntries", jId.ToString(), moduleName: "SCR_JV", description: $"إنشاء قيد: {dto.JournalNumber}"); } catch { }
                return jId;
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task PostJournalAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var status = await connection.ExecuteScalarAsync<int?>("SELECT JournalStatus FROM dbo.JournalEntries WHERE JournalID=@ID", new { ID = id }, trans);
                if (status != 1) throw new Exception("القيد ليس مسودة");

                // جلب تفاصيل القيد لتحديث الأرصدة
                var details = await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit)>(
                    "SELECT AccountID, DebitAmount, CreditAmount FROM dbo.JournalEntryDetails WHERE JournalID=@ID",
                    new { ID = id }, trans);

                foreach (var d in details)
                {
                    var delta = d.Debit - d.Credit; // مدين + ، دائن -
                    if (delta != 0)
                    {
                        await connection.ExecuteAsync(
                            "UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Delta WHERE AccountID=@AccID",
                            new { Delta = delta, AccID = d.AccountID }, trans);

                        // تحديث الآباء
                        int? cur = d.AccountID;
                        for (int i = 0; i < 10; i++)
                        {
                            var pid = await connection.ExecuteScalarAsync<int?>(
                                "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = cur }, trans);
                            if (!pid.HasValue || pid.Value == 0) break;
                            await connection.ExecuteAsync(
                                "UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Delta WHERE AccountID=@PID",
                                new { Delta = delta, PID = pid.Value }, trans);
                            cur = pid.Value;
                        }
                    }
                }

                await connection.ExecuteAsync("UPDATE dbo.JournalEntries SET JournalStatus=2, PostedBy=@UserID, PostedDate=GETDATE() WHERE JournalID=@ID",
                    new { UserID = userId, ID = id }, trans);
                trans.Commit();
                try { await _audit.WriteAuditLogAsync(userId, 2, "JournalEntries", id.ToString(), moduleName: "SCR_JV", description: "ترحيل قيد محاسبي وتحديث الأرصدة"); } catch { }
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task DeleteJournalAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>("SELECT JournalStatus FROM dbo.JournalEntries WHERE JournalID=@ID", new { ID = id });
            if (status != 1) throw new Exception("لا يمكن حذف قيد مرحل");

            await connection.ExecuteAsync("DELETE FROM dbo.JournalEntryDetails WHERE JournalID=@ID; DELETE FROM dbo.JournalEntries WHERE JournalID=@ID", new { ID = id });
            try { await _audit.WriteAuditLogAsync(userId, 3, "JournalEntries", id.ToString(), moduleName: "SCR_JV", description: "حذف قيد محاسبي"); } catch { }
        }

        // ==========================================
        // السنوات المالية - على الجدول الحقيقي FiscalYears 11 عمود - بدون IsActive
        // ==========================================
        public async Task<List<FiscalYearListDto>> GetFiscalYearsAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT fy.FiscalYearID, fy.YearCode, fy.YearName, fy.StartDate, fy.EndDate, fy.YearStatus,
                               CASE fy.YearStatus WHEN 1 THEN N'مفتوحة' WHEN 2 THEN N'مغلقة' ELSE N'غير محدد' END AS StatusName,
                               fy.IsCurrent, fy.ClosedBy, fy.ClosedDate, fy.CreatedDate,
                               (SELECT COUNT(*) FROM dbo.AccountingPeriods ap WHERE ap.FiscalYearID=fy.FiscalYearID) AS PeriodsCount,
                               (SELECT COUNT(*) FROM dbo.JournalEntries je WHERE je.FiscalYearID=fy.FiscalYearID) AS JournalsCount
                        FROM dbo.FiscalYears fy ORDER BY fy.StartDate DESC";
                var result = await connection.QueryAsync<FiscalYearListDto>(sql);
                return result.ToList();
            }
            catch
            {
                try
                {
                    var sql2 = @"SELECT FiscalYearID, YearCode, YearName, StartDate, EndDate, YearStatus,
                               CASE YearStatus WHEN 1 THEN N'مفتوحة' WHEN 2 THEN N'مغلقة' ELSE N'غير محدد' END AS StatusName,
                               IsCurrent, ClosedBy, ClosedDate, CreatedDate, 0 AS PeriodsCount, 0 AS JournalsCount
                        FROM dbo.FiscalYears ORDER BY StartDate DESC";
                    var result = await connection.QueryAsync<FiscalYearListDto>(sql2);
                    return result.ToList();
                }
                catch
                {
                    return new List<FiscalYearListDto>();
                }
            }
        }

        public async Task<FiscalYearDto?> GetFiscalYearByIdAsync(int id)
        {
            using var connection = CreateConnection();
            try
            {
                return await connection.QueryFirstOrDefaultAsync<FiscalYearDto>("SELECT FiscalYearID, YearCode, YearName, StartDate, EndDate, YearStatus, IsCurrent, Notes, CreatedDate FROM dbo.FiscalYears WHERE FiscalYearID=@ID", new { ID = id });
            }
            catch
            {
                return await connection.QueryFirstOrDefaultAsync<FiscalYearDto>("SELECT FiscalYearID, YearCode, YearName, StartDate, EndDate, 1 AS YearStatus, IsCurrent, '' AS Notes, GETDATE() AS CreatedDate FROM dbo.FiscalYears WHERE FiscalYearID=@ID", new { ID = id });
            }
        }

        public async Task<int> InsertFiscalYearAsync(FiscalYearDto dto, int userId)
        {
            using var connection = CreateConnection();
            if (dto.EndDate <= dto.StartDate) throw new Exception("تاريخ النهاية يجب أن يكون بعد البداية");
            var sql = @"INSERT INTO dbo.FiscalYears(YearCode, YearName, StartDate, EndDate, YearStatus, IsCurrent, Notes, CreatedDate)
                        VALUES(@YearCode, @YearName, @StartDate, @EndDate, @YearStatus, @IsCurrent, @Notes, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new { dto.YearCode, dto.YearName, dto.StartDate, dto.EndDate, dto.YearStatus, dto.IsCurrent, dto.Notes });
            if (dto.IsCurrent)
                await connection.ExecuteAsync("UPDATE dbo.FiscalYears SET IsCurrent=0 WHERE FiscalYearID<>@ID", new { ID = newId });

            // إنشاء فترات شهرية تلقائياً - بأسماء عربية صحيحة: يناير 2027
            var arabicMonths = new string[] { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
            for (int m = 1; m <= 12; m++)
            {
                var pStart = new DateTime(dto.StartDate.Year, m, 1);
                if (pStart < dto.StartDate) continue;
                if (pStart > dto.EndDate) break;
                var pEnd = pStart.AddMonths(1).AddDays(-1);
                if (pEnd > dto.EndDate) pEnd = dto.EndDate;
                var periodName = $"{arabicMonths[m]} {pStart.Year}";
                await connection.ExecuteAsync(@"INSERT INTO dbo.AccountingPeriods(FiscalYearID, PeriodNumber, PeriodName, StartDate, EndDate, PeriodStatus)
                                                VALUES(@FYID, @Num, @Name, @S, @E, 1)",
                    new { FYID = newId, Num = m, Name = periodName, S = pStart, E = pEnd });
            }

            try { await _audit.WriteAuditLogAsync(userId, 1, "FiscalYears", newId.ToString(), moduleName: "SCR_FY", description: $"إنشاء سنة مالية: {dto.YearName}"); } catch { }
            return newId;
        }

        public async Task UpdateFiscalYearAsync(FiscalYearDto dto, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.FiscalYears SET YearCode=@YearCode, YearName=@YearName, StartDate=@StartDate, EndDate=@EndDate, YearStatus=@YearStatus, IsCurrent=@IsCurrent, Notes=@Notes WHERE FiscalYearID=@FiscalYearID";
            await connection.ExecuteAsync(sql, dto);
            if (dto.IsCurrent)
                await connection.ExecuteAsync("UPDATE dbo.FiscalYears SET IsCurrent=0 WHERE FiscalYearID<>@ID", new { ID = dto.FiscalYearID });
            try { await _audit.WriteAuditLogAsync(userId, 2, "FiscalYears", dto.FiscalYearID.ToString(), moduleName: "SCR_FY", description: $"تعديل سنة مالية: {dto.YearName}"); } catch { }
        }

        public async Task CloseFiscalYearAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var hasOpenPeriods = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingPeriods WHERE FiscalYearID=@ID AND PeriodStatus=1", new { ID = id });
            if (hasOpenPeriods > 0) throw new Exception("يجب إغلاق جميع الفترات أولاً");
            await connection.ExecuteAsync("UPDATE dbo.FiscalYears SET YearStatus=2, ClosedBy=@UserID, ClosedDate=GETDATE() WHERE FiscalYearID=@ID", new { UserID = userId, ID = id });
            try { await _audit.WriteAuditLogAsync(userId, 2, "FiscalYears", id.ToString(), moduleName: "SCR_FY", description: "إغلاق سنة مالية"); } catch { }
        }

        // ==========================================
        // الفترات المحاسبية - على الجدول الحقيقي AccountingPeriods 9 أعمدة - بدون IsActive
        // ==========================================
        public async Task<List<AccountingPeriodListDto>> GetAccountingPeriodsAsync(int? fiscalYearId = null)
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT ap.PeriodID, ap.FiscalYearID, fy.YearName AS FiscalYearName, ap.PeriodNumber, ap.PeriodName, ap.StartDate, ap.EndDate, ap.PeriodStatus,
                               CASE ap.PeriodStatus WHEN 1 THEN N'مفتوحة' WHEN 2 THEN N'مغلقة' ELSE N'غير محدد' END AS StatusName, ap.ClosedBy, ap.ClosedDate
                        FROM dbo.AccountingPeriods ap LEFT JOIN dbo.FiscalYears fy ON ap.FiscalYearID=fy.FiscalYearID
                        WHERE (@FYID IS NULL OR ap.FiscalYearID=@FYID) ORDER BY ap.FiscalYearID DESC, ap.PeriodNumber";
                var result = await connection.QueryAsync<AccountingPeriodListDto>(sql, new { FYID = fiscalYearId });
                return result.ToList();
            }
            catch
            {
                try
                {
                    var sql2 = @"SELECT PeriodID, FiscalYearID, '' AS FiscalYearName, PeriodNumber, PeriodName, StartDate, EndDate, PeriodStatus,
                               CASE PeriodStatus WHEN 1 THEN N'مفتوحة' WHEN 2 THEN N'مغلقة' ELSE N'غير محدد' END AS StatusName, NULL AS ClosedBy, NULL AS ClosedDate
                        FROM dbo.AccountingPeriods WHERE (@FYID IS NULL OR FiscalYearID=@FYID) ORDER BY FiscalYearID DESC, PeriodNumber";
                    var result = await connection.QueryAsync<AccountingPeriodListDto>(sql2, new { FYID = fiscalYearId });
                    return result.ToList();
                }
                catch
                {
                    return new List<AccountingPeriodListDto>();
                }
            }
        }

        public async Task ClosePeriodAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.AccountingPeriods SET PeriodStatus=2, ClosedBy=@UserID, ClosedDate=GETDATE() WHERE PeriodID=@ID", new { UserID = userId, ID = id });
            try { await _audit.WriteAuditLogAsync(userId, 2, "AccountingPeriods", id.ToString(), moduleName: "SCR_FY", description: "إغلاق فترة محاسبية"); } catch { }
        }

        public async Task OpenPeriodAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.AccountingPeriods SET PeriodStatus=1, ClosedBy=NULL, ClosedDate=NULL WHERE PeriodID=@ID", new { ID = id });
            try { await _audit.WriteAuditLogAsync(userId, 2, "AccountingPeriods", id.ToString(), moduleName: "SCR_FY", description: "فتح فترة محاسبية"); } catch { }
        }

        // ==========================================
        // المصروفات - OverheadExpenses Gold Edition Pro
        // مع ربط بمركز تكلفة اختياري + فترة محاسبية + قيد أوتوماتيك
        // ==========================================
        public async Task<List<ExpenseListDto>> GetExpensesAsync(DateTime? from = null, DateTime? to = null, int? costCenterId = null, int? periodId = null)
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT * FROM dbo.vw_OverheadExpenses 
                            WHERE 1=1 
                            AND (@From IS NULL OR ExpenseDate >= @From)
                            AND (@To IS NULL OR ExpenseDate <= @To)
                            AND (@CCID IS NULL OR CostCenterID = @CCID)
                            AND (@PID IS NULL OR PeriodID = @PID)
                            ORDER BY ExpenseDate DESC, ExpenseID DESC";
                var result = await connection.QueryAsync<ExpenseListDto>(sql, new { From = from, To = to, CCID = costCenterId, PID = periodId });
                return result.ToList();
            }
            catch
            {
                // Fallback بدون View
                var sql2 = @"SELECT oe.ExpenseID, ISNULL(oe.ExpenseNumber,'EXP-'+CAST(oe.ExpenseID AS NVARCHAR)) AS ExpenseNumber,
                                    oe.ExpenseDate, oe.PeriodID, ap.PeriodName, ap.PeriodNumber, oe.FiscalYearID, fy.YearName AS FiscalYearName,
                                    oe.AccountID, coa.AccountCode AS ExpenseAccountCode, coa.AccountNameAr AS ExpenseAccountNameAr,
                                    oe.CreditAccountID, coa2.AccountCode AS CreditAccountCode, coa2.AccountNameAr AS CreditAccountNameAr,
                                    oe.CostCenterID, cc.CostCenterCode, cc.CostCenterNameAr, oe.Description, oe.Amount, oe.Reference,
                                    ISNULL(oe.ExpenseStatus,1) AS ExpenseStatus,
                                    CASE ISNULL(oe.ExpenseStatus,1) WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                                    ISNULL(oe.IsPosted,0) AS IsPosted, oe.JournalID, je.JournalNumber,
                                    oe.PaymentMethod, oe.CashBoxID, cb.CashBoxNameAr, oe.BankAccountID, ba.BankName, oe.Notes,
                                    oe.CreatedBy, su.Username AS CreatedByName, oe.CreatedDate, oe.ApprovedBy, oe.ApprovedDate
                             FROM dbo.OverheadExpenses oe
                             LEFT JOIN dbo.AccountingPeriods ap ON oe.PeriodID=ap.PeriodID
                             LEFT JOIN dbo.FiscalYears fy ON oe.FiscalYearID=fy.FiscalYearID
                             LEFT JOIN dbo.ChartOfAccounts coa ON oe.AccountID=coa.AccountID
                             LEFT JOIN dbo.ChartOfAccounts coa2 ON oe.CreditAccountID=coa2.AccountID
                             LEFT JOIN dbo.CostCenters cc ON oe.CostCenterID=cc.CostCenterID
                             LEFT JOIN dbo.JournalEntries je ON oe.JournalID=je.JournalID
                             LEFT JOIN dbo.CashBoxes cb ON oe.CashBoxID=cb.CashBoxID
                             LEFT JOIN dbo.BankAccounts ba ON oe.BankAccountID=ba.BankAccountID
                             LEFT JOIN dbo.SystemUsers su ON oe.CreatedBy=su.UserID
                             WHERE 1=1
                             AND (@From IS NULL OR oe.ExpenseDate >= @From)
                             AND (@To IS NULL OR oe.ExpenseDate <= @To)
                             AND (@CCID IS NULL OR oe.CostCenterID = @CCID)
                             AND (@PID IS NULL OR oe.PeriodID = @PID)
                             ORDER BY oe.ExpenseDate DESC, oe.ExpenseID DESC";
                var result = await connection.QueryAsync<ExpenseListDto>(sql2, new { From = from, To = to, CCID = costCenterId, PID = periodId });
                return result.ToList();
            }
        }

        public async Task<ExpenseDto?> GetExpenseByIdAsync(int id)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ExpenseID, ExpenseNumber, ExpenseDate, PeriodID, FiscalYearID, AccountID, CreditAccountID, CostCenterID,
                               Description, Amount, Reference, ReferenceType, ExpenseStatus, IsPosted, JournalID, PaymentMethod, CashBoxID, BankAccountID, Notes, CreatedBy, CreatedDate
                        FROM dbo.OverheadExpenses WHERE ExpenseID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ExpenseDto>(sql, new { ID = id });
        }

        public async Task<string> GenerateExpenseNoAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'EXP', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"EXP-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"EXP-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertExpenseAsync(ExpenseDto dto, int userId)
        {
            // 1) توليد رقم وفحص الأعمدة قبل فتح الـ Transaction (لتجنب خطأ BeginExecuteReader requires transaction)
            if (string.IsNullOrWhiteSpace(dto.ExpenseNumber))
                dto.ExpenseNumber = await GenerateExpenseNoAsync();

            bool hasCreditCol, hasRefCol, hasNumCol, hasStatusCol, hasFiscalCol, hasCashCol, hasNotesCol;
            using (var checkConn = CreateConnection())
            {
                hasCreditCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='CreditAccountID'") > 0;
                hasRefCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='Reference'") > 0;
                hasNumCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='ExpenseNumber'") > 0;
                hasStatusCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='ExpenseStatus'") > 0;
                hasFiscalCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='FiscalYearID'") > 0;
                hasCashCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='CashBoxID'") > 0;
                hasNotesCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='Notes'") > 0;
            }

            string journalNumber = "";
            try { journalNumber = await GenerateJournalNoAsync(); } catch { journalNumber = $"JV-{DateTime.Now:yyMMddHHmmss}"; }

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                // تحديد الفترة والسنة تلقائياً من تاريخ المصروف إذا لم يحدد - مع إنشاء تلقائي لو مش موجود
                if (dto.PeriodID == 0)
                {
                    var period = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT TOP 1 PeriodID, FiscalYearID FROM dbo.AccountingPeriods WHERE @EDate BETWEEN StartDate AND EndDate ORDER BY PeriodNumber",
                        new { EDate = dto.ExpenseDate }, trans);
                    if (period != null)
                    {
                        dto.PeriodID = (int)period.PeriodID;
                        dto.FiscalYearID = (int?)period.FiscalYearID;
                    }
                }
                if (dto.FiscalYearID == null || dto.FiscalYearID == 0)
                {
                    dto.FiscalYearID = await connection.ExecuteScalarAsync<int?>(
                        "SELECT TOP 1 FiscalYearID FROM dbo.FiscalYears WHERE @EDate BETWEEN StartDate AND EndDate ORDER BY FiscalYearID DESC",
                        new { EDate = dto.ExpenseDate }, trans);
                }

                // إنشاء سنة مالية وفترات تلقائياً لو مش موجودة
                if (dto.PeriodID == 0 || dto.FiscalYearID == null || dto.FiscalYearID == 0)
                {
                    try
                    {
                        int year = dto.ExpenseDate.Year;
                        var existingFY = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 FiscalYearID FROM dbo.FiscalYears WHERE YearCode=@YC OR YearName LIKE '%'+@Y+'%' ORDER BY FiscalYearID DESC",
                            new { YC = year.ToString(), Y = year.ToString() }, trans);
                        int fyId;
                        if (existingFY.HasValue && existingFY.Value > 0)
                        {
                            fyId = existingFY.Value;
                        }
                        else
                        {
                            // إنشاء سنة مالية جديدة
                            fyId = await connection.ExecuteScalarAsync<int>(
                                @"INSERT INTO dbo.FiscalYears(YearCode, YearName, StartDate, EndDate, YearStatus, IsCurrent, Notes, CreatedDate)
                                  VALUES(@Code, @Name, @S, @E, 1, 1, N'إنشاء تلقائي', GETDATE());
                                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                                new { Code = year.ToString(), Name = $"السنة المالية {year}", S = new DateTime(year, 1, 1), E = new DateTime(year, 12, 31) }, trans);
                            await connection.ExecuteAsync("UPDATE dbo.FiscalYears SET IsCurrent=0 WHERE FiscalYearID<>@ID", new { ID = fyId }, trans);

                            var arabicMonths = new string[] { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
                            for (int m = 1; m <= 12; m++)
                            {
                                var pStart = new DateTime(year, m, 1);
                                var pEnd = pStart.AddMonths(1).AddDays(-1);
                                var periodName = $"{arabicMonths[m]} {year}";
                                try
                                {
                                    await connection.ExecuteAsync(@"INSERT INTO dbo.AccountingPeriods(FiscalYearID, PeriodNumber, PeriodName, StartDate, EndDate, PeriodStatus)
                                                VALUES(@FYID, @Num, @Name, @S, @E, 1)",
                                        new { FYID = fyId, Num = m, Name = periodName, S = pStart, E = pEnd }, trans);
                                }
                                catch { }
                            }
                        }
                        dto.FiscalYearID = fyId;
                        if (dto.PeriodID == 0)
                        {
                            var newPeriod = await connection.QueryFirstOrDefaultAsync<dynamic>(
                                "SELECT TOP 1 PeriodID FROM dbo.AccountingPeriods WHERE FiscalYearID=@FYID AND @EDate BETWEEN StartDate AND EndDate ORDER BY PeriodNumber",
                                new { FYID = fyId, EDate = dto.ExpenseDate }, trans);
                            if (newPeriod != null)
                                dto.PeriodID = (int)newPeriod.PeriodID;
                            else
                            {
                                // لو لسه مفيش فترة مطابقة، خد فترة الشهر نفسه
                                var monthPeriod = await connection.ExecuteScalarAsync<int?>(
                                    "SELECT TOP 1 PeriodID FROM dbo.AccountingPeriods WHERE FiscalYearID=@FYID AND PeriodNumber=@PN",
                                    new { FYID = fyId, PN = dto.ExpenseDate.Month }, trans);
                                if (monthPeriod.HasValue) dto.PeriodID = monthPeriod.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // لو فشل الإنشاء التلقائي، لا نرمي - سنرمي رسالة واضحة بعدين
                        Console.WriteLine($"Auto FY creation failed: {ex.Message}");
                    }
                }

                if (dto.PeriodID == 0) throw new Exception($"يجب تحديد الفترة المحاسبية - لا توجد فترة تغطي تاريخ {dto.ExpenseDate:yyyy-MM-dd}. أنشئ سنة مالية {dto.ExpenseDate.Year} أولاً");
                if (dto.AccountID == 0) throw new Exception("يجب اختيار حساب المصروف");
                if (dto.Amount <= 0) throw new Exception("المبلغ يجب أن يكون أكبر من صفر");

                // إدراج المصروف - بناء ديناميكي حسب الأعمدة الموجودة
                var cols = "PeriodID, ExpenseDate, AccountID, CostCenterID, Description, Amount, CreatedBy, CreatedDate";
                var vals = "@PeriodID, @ExpenseDate, @AccountID, NULLIF(@CostCenterID,0), @Description, @Amount, @UserID, GETDATE()";
                if (hasCreditCol) { cols += ", CreditAccountID"; vals += ", NULLIF(@CreditAccountID,0)"; }
                if (hasRefCol) { cols += ", Reference"; vals += ", @Reference"; }
                if (hasNumCol) { cols += ", ExpenseNumber"; vals += ", @ExpenseNumber"; }
                if (hasStatusCol) { cols += ", ExpenseStatus"; vals += ", 1"; }
                if (hasFiscalCol) { cols += ", FiscalYearID"; vals += ", @FiscalYearID"; }
                if (hasCashCol) { cols += ", CashBoxID, BankAccountID, PaymentMethod"; vals += ", NULLIF(@CashBoxID,0), NULLIF(@BankAccountID,0), @PaymentMethod"; }
                if (hasNotesCol) { cols += ", Notes"; vals += ", @Notes"; }

                var sql = $"INSERT INTO dbo.OverheadExpenses({cols}) VALUES({vals}); SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    dto.PeriodID,
                    dto.ExpenseDate,
                    dto.AccountID,
                    dto.CreditAccountID,
                    dto.CostCenterID,
                    dto.Description,
                    dto.Amount,
                    dto.Reference,
                    dto.ExpenseNumber,
                    dto.FiscalYearID,
                    dto.CashBoxID,
                    dto.BankAccountID,
                    dto.PaymentMethod,
                    dto.Notes,
                    UserID = userId
                }, trans);

                // إنشاء قيد محاسبي تلقائي: مدين مصروف - دائن خزينة/بنك/مستحق
                int? creditAccId = dto.CreditAccountID;
                if (creditAccId == null || creditAccId == 0)
                {
                    if (dto.CashBoxID.HasValue && dto.CashBoxID.Value > 0)
                    {
                        creditAccId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 AccountID FROM dbo.CashBoxes WHERE CashBoxID=@ID", new { ID = dto.CashBoxID.Value }, trans);
                    }
                    if (creditAccId == null || creditAccId == 0 && dto.BankAccountID.HasValue && dto.BankAccountID.Value > 0)
                    {
                        creditAccId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 AccountID FROM dbo.BankAccounts WHERE BankAccountID=@ID", new { ID = dto.BankAccountID.Value }, trans);
                    }
                    if (creditAccId == null || creditAccId == 0)
                    {
                        creditAccId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 AccountID FROM dbo.ChartOfAccounts WHERE AccountNameAr LIKE N'%نقدية%' OR AccountNameAr LIKE N'%خزينة%' OR AccountNameAr LIKE N'%صندوق%' ORDER BY AccountID", null, trans);
                    }
                }

                if (creditAccId.HasValue && creditAccId.Value > 0)
                {
                    int? fyId = dto.FiscalYearID;
                    int? pId = dto.PeriodID;
                    
                    var jId = await connection.ExecuteScalarAsync<int>(
                        @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate)
                          VALUES(@JNum, @JDate, @Desc, @Ref, 'OverheadExpense', @SrcID, @SrcNum, @Amt, @Amt, @FYID, @PID, 1, 1, 1, @UserID, GETDATE());
                          SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new
                        {
                            JNum = journalNumber,
                            JDate = dto.ExpenseDate,
                            Desc = dto.Description, // بيان نظيف بدون كود - الكود في SourceDocNumber
                            Ref = dto.Reference,
                            SrcID = newId,
                            SrcNum = dto.ExpenseNumber,
                            Amt = dto.Amount,
                            FYID = fyId,
                            PID = pId,
                            UserID = userId
                        }, trans);

                    await connection.ExecuteAsync(
                        "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID) VALUES(@JID, 1, @AccID, @Amt, 0, @Desc, NULLIF(@CCID,0))",
                        new { JID = jId, AccID = dto.AccountID, Amt = dto.Amount, Desc = dto.Description, CCID = dto.CostCenterID ?? 0 }, trans);

                    await connection.ExecuteAsync(
                        "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID) VALUES(@JID, 2, @AccID, 0, @Amt, @Desc, NULLIF(@CCID,0))",
                        new { JID = jId, AccID = creditAccId.Value, Amt = dto.Amount, Desc = dto.Description, CCID = dto.CostCenterID ?? 0 }, trans);

                    await connection.ExecuteAsync("UPDATE dbo.OverheadExpenses SET JournalID=@JID, IsPosted=1 WHERE ExpenseID=@ID",
                        new { JID = jId, ID = newId }, trans);

                    // ==================== تحديث الأرصدة الفعلية ====================
                    // لو طريقة الدفع نقدي ولم يحدد خزينة -> خد الافتراضية
                    if ((dto.CashBoxID == null || dto.CashBoxID == 0) && dto.PaymentMethod == 1)
                    {
                        try
                        {
                            var defCB = await connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CashBoxID FROM dbo.CashBoxes WHERE IsDefault=1 ORDER BY CashBoxID", null, trans);
                            if (!defCB.HasValue) defCB = await connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CashBoxID FROM dbo.CashBoxes ORDER BY CashBoxID", null, trans);
                            if (defCB.HasValue) dto.CashBoxID = defCB.Value;
                        }
                        catch { }
                    }
                    // لو بنك ولم يحدد حساب بنكي -> خد أول حساب
                    if ((dto.BankAccountID == null || dto.BankAccountID == 0) && dto.PaymentMethod == 2)
                    {
                        try
                        {
                            var defB = await connection.ExecuteScalarAsync<int?>("SELECT TOP 1 BankAccountID FROM dbo.BankAccounts ORDER BY BankAccountID", null, trans);
                            if (defB.HasValue) dto.BankAccountID = defB.Value;
                        }
                        catch { }
                    }

                    // 1) خصم من رصيد الخزنة
                    if (dto.CashBoxID.HasValue && dto.CashBoxID.Value > 0)
                    {
                        await connection.ExecuteAsync(
                            "UPDATE dbo.CashBoxes SET CurrentBalance = ISNULL(CurrentBalance,0) - @Amt WHERE CashBoxID=@CBID",
                            new { Amt = dto.Amount, CBID = dto.CashBoxID.Value }, trans);
                        // تحديث العمود في جدول المصروفات لو كان null قبل كده
                        try { await connection.ExecuteAsync("UPDATE dbo.OverheadExpenses SET CashBoxID=@CBID WHERE ExpenseID=@EID", new { CBID = dto.CashBoxID.Value, EID = newId }, trans); } catch { }
                    }
                    // 2) خصم من رصيد البنك
                    if (dto.BankAccountID.HasValue && dto.BankAccountID.Value > 0)
                    {
                        await connection.ExecuteAsync(
                            "UPDATE dbo.BankAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) - @Amt WHERE BankAccountID=@BID",
                            new { Amt = dto.Amount, BID = dto.BankAccountID.Value }, trans);
                        try { await connection.ExecuteAsync("UPDATE dbo.OverheadExpenses SET BankAccountID=@BID WHERE ExpenseID=@EID", new { BID = dto.BankAccountID.Value, EID = newId }, trans); } catch { }
                    }
                    // 3) تحديث أرصدة شجرة الحسابات - حساب المصروف مدين + وحساب الخزنة دائن -
                    // تحديث مباشر + تحديث الآباء بالـ CTE
                    await connection.ExecuteAsync(
                        @"UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Amt WHERE AccountID=@AccID",
                        new { Amt = dto.Amount, AccID = dto.AccountID }, trans);
                    await connection.ExecuteAsync(
                        @"UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) - @Amt WHERE AccountID=@AccID",
                        new { Amt = dto.Amount, AccID = creditAccId.Value }, trans);

                    // تحديث أرصدة الآباء (Parent accounts) باستخدام حلقة
                    async Task UpdateParents(int accId, decimal delta)
                    {
                        int? currentId = accId;
                        for (int i = 0; i < 10; i++) // حماية من حلقة لا نهائية - max 10 مستويات
                        {
                            var parentId = await connection.ExecuteScalarAsync<int?>(
                                "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = currentId }, trans);
                            if (!parentId.HasValue || parentId.Value == 0) break;
                            await connection.ExecuteAsync(
                                "UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Delta WHERE AccountID=@PID",
                                new { Delta = delta, PID = parentId.Value }, trans);
                            currentId = parentId.Value;
                        }
                    }
                    await UpdateParents(dto.AccountID, dto.Amount); // المصروف يزيد رصيد الأب
                    await UpdateParents(creditAccId.Value, -dto.Amount); // الخزنة ينقص رصيد الأب
                }

                trans.Commit();
                try { await _audit.WriteAuditLogAsync(userId, 1, "OverheadExpenses", newId.ToString(), moduleName: "SCR_EXPENSES", description: $"إنشاء مصروف: {dto.ExpenseNumber} - {dto.Amount} - خصم خزنة {dto.CashBoxID}"); } catch { }
                return newId;
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task UpdateExpenseAsync(ExpenseDto dto, int userId)
        {
            using var connection = CreateConnection();
            if (dto.Amount <= 0) throw new Exception("المبلغ يجب أن يكون أكبر من صفر");
            
            var sql = @"UPDATE dbo.OverheadExpenses SET 
                        ExpenseDate=@ExpenseDate, PeriodID=@PeriodID, AccountID=@AccountID, 
                        CreditAccountID=NULLIF(@CreditAccountID,0), CostCenterID=NULLIF(@CostCenterID,0),
                        Description=@Description, Amount=@Amount, Reference=@Reference,
                        CashBoxID=NULLIF(@CashBoxID,0), BankAccountID=NULLIF(@BankAccountID,0), PaymentMethod=@PaymentMethod,
                        Notes=@Notes
                        WHERE ExpenseID=@ExpenseID AND ISNULL(ExpenseStatus,1)=1";
            await connection.ExecuteAsync(sql, dto);
            try { await _audit.WriteAuditLogAsync(userId, 2, "OverheadExpenses", dto.ExpenseID.ToString(), moduleName: "SCR_EXPENSES", description: $"تعديل مصروف: {dto.ExpenseNumber}"); } catch { }
        }

        public async Task DeleteExpenseAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var exp = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT ExpenseStatus, JournalID, Amount, AccountID, CreditAccountID, CashBoxID, BankAccountID FROM dbo.OverheadExpenses WHERE ExpenseID=@ID",
                    new { ID = id }, trans);
                if (exp == null) throw new Exception("المصروف غير موجود");
                if ((int)(exp.ExpenseStatus ?? 1) != 1) throw new Exception("لا يمكن حذف مصروف معتمد");

                decimal amount = (decimal)(exp.Amount ?? 0);
                int accountId = (int)(exp.AccountID ?? 0);
                int? creditAccId = (int?)exp.CreditAccountID;
                int? cashBoxId = (int?)exp.CashBoxID;
                int? bankAccId = (int?)exp.BankAccountID;
                int? journalId = (int?)exp.JournalID;

                // عكس الأرصدة
                if (cashBoxId.HasValue && cashBoxId.Value > 0)
                    await connection.ExecuteAsync("UPDATE dbo.CashBoxes SET CurrentBalance = ISNULL(CurrentBalance,0) + @Amt WHERE CashBoxID=@CBID", new { Amt = amount, CBID = cashBoxId.Value }, trans);
                if (bankAccId.HasValue && bankAccId.Value > 0)
                    await connection.ExecuteAsync("UPDATE dbo.BankAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Amt WHERE BankAccountID=@BID", new { Amt = amount, BID = bankAccId.Value }, trans);
                if (accountId > 0)
                    await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) - @Amt WHERE AccountID=@AccID", new { Amt = amount, AccID = accountId }, trans);
                if (creditAccId.HasValue && creditAccId.Value > 0)
                    await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Amt WHERE AccountID=@AccID", new { Amt = amount, AccID = creditAccId.Value }, trans);

                // عكس أرصدة الآباء
                async Task ReverseParents(int accId, decimal delta)
                {
                    int? cur = accId;
                    for (int i = 0; i < 10; i++)
                    {
                        var pid = await connection.ExecuteScalarAsync<int?>("SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = cur }, trans);
                        if (!pid.HasValue || pid.Value == 0) break;
                        await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @D WHERE AccountID=@PID", new { D = delta, PID = pid.Value }, trans);
                        cur = pid.Value;
                    }
                }
                if (accountId > 0) await ReverseParents(accountId, -amount);
                if (creditAccId.HasValue) await ReverseParents(creditAccId.Value, amount);

                if (journalId.HasValue && journalId.Value > 0)
                    await connection.ExecuteAsync("DELETE FROM dbo.JournalEntryDetails WHERE JournalID=@JID; DELETE FROM dbo.JournalEntries WHERE JournalID=@JID",
                        new { JID = journalId.Value }, trans);

                await connection.ExecuteAsync("DELETE FROM dbo.OverheadExpenses WHERE ExpenseID=@ID", new { ID = id }, trans);
                trans.Commit();
                try { await _audit.WriteAuditLogAsync(userId, 3, "OverheadExpenses", id.ToString(), moduleName: "SCR_EXPENSES", description: "حذف مصروف وعكس الأرصدة"); } catch { }
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task ApproveExpenseAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.OverheadExpenses SET ExpenseStatus=2, ApprovedBy=@UserID, ApprovedDate=GETDATE() WHERE ExpenseID=@ID", new { UserID = userId, ID = id });
            try { await _audit.WriteAuditLogAsync(userId, 2, "OverheadExpenses", id.ToString(), moduleName: "SCR_EXPENSES", description: "اعتماد مصروف"); } catch { }
        }

        // ==========================================
        // تقارير محاسبية - vw_TrialBalance + vw_AccountLedger
        // ==========================================
        public async Task<List<TrialBalanceDto>> GetTrialBalanceAsync(DateTime? from = null, DateTime? to = null, int? level = null)
        {
            using var connection = CreateConnection();
            // المحاولة 1: vw_TrialBalance لو موجود وفيه داتا
            try
            {
                var sql = @"SELECT * FROM dbo.vw_TrialBalance ORDER BY AccountCode";
                var result = await connection.QueryAsync<TrialBalanceDto>(sql);
                var list = result.ToList();
                if (list.Any()) return list;
            }
            catch { }

            // المحاولة 2: حساب مباشر من القيود المرحلة + الرصيد الافتتاحي
            try
            {
                var sql2 = @"SELECT coa.AccountID, coa.AccountCode, coa.AccountNameAr, coa.AccountLevel, coa.AccountType,
                                    ISNULL(coa.OpeningBalance,0) AS OpeningBalance,
                                    ISNULL((SELECT SUM(DebitAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND ISNULL(je.JournalStatus,1)!=3 AND (@From IS NULL OR je.JournalDate>=@From) AND (@To IS NULL OR je.JournalDate<=@To)),0) AS TotalDebit,
                                    ISNULL((SELECT SUM(CreditAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND ISNULL(je.JournalStatus,1)!=3 AND (@From IS NULL OR je.JournalDate>=@From) AND (@To IS NULL OR je.JournalDate<=@To)),0) AS TotalCredit,
                                    ISNULL(coa.OpeningBalance,0) + ISNULL((SELECT SUM(DebitAmount - CreditAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND ISNULL(je.JournalStatus,1)!=3 AND (@From IS NULL OR je.JournalDate>=@From) AND (@To IS NULL OR je.JournalDate<=@To)),0) AS CurrentBalance
                             FROM dbo.ChartOfAccounts coa WHERE (@Level IS NULL OR coa.AccountLevel<=@Level) AND coa.IsActive=1 ORDER BY coa.AccountCode";
                var result = await connection.QueryAsync<TrialBalanceDto>(sql2, new { From = from, To = to, Level = level });
                var list = result.ToList();
                if (list.Any()) return list;
            }
            catch { }

            // المحاولة 3: أبسط - كل الحسابات مع افتتاحي وحالي من الجدول مباشرة (حتى لو مفيش قيود)
            try
            {
                var sql3 = @"SELECT AccountID, AccountCode, AccountNameAr, AccountLevel, AccountType,
                                    ISNULL(OpeningBalance,0) AS OpeningBalance,
                                    0 AS TotalDebit, 0 AS TotalCredit,
                                    ISNULL(CurrentBalance, ISNULL(OpeningBalance,0)) AS CurrentBalance
                             FROM dbo.ChartOfAccounts
                             WHERE (@Level IS NULL OR AccountLevel<=@Level)
                             ORDER BY AccountCode";
                var result = await connection.QueryAsync<TrialBalanceDto>(sql3, new { Level = level });
                return result.ToList();
            }
            catch
            {
                return new List<TrialBalanceDto>();
            }
        }

        public async Task<List<AccountLedgerDto>> GetAccountLedgerAsync(int accountId, DateTime? from = null, DateTime? to = null)
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT * FROM dbo.vw_AccountLedger WHERE AccountID=@AccID AND (@From IS NULL OR JournalDate>=@From) AND (@To IS NULL OR JournalDate<=@To) ORDER BY JournalDate, JournalID";
                var result = await connection.QueryAsync<AccountLedgerDto>(sql, new { AccID = accountId, From = from, To = to });
                return result.ToList();
            }
            catch
            {
                var sql2 = @"SELECT jd.JournalDetailID, je.JournalID, je.JournalNumber, je.JournalDate, je.Description, jd.DebitAmount, jd.CreditAmount, coa.AccountCode, coa.AccountNameAr,
                                    SUM(jd.DebitAmount - jd.CreditAmount) OVER (ORDER BY je.JournalDate, je.JournalID ROWS UNBOUNDED PRECEDING) AS RunningBalance
                             FROM dbo.JournalEntryDetails jd
                             JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID
                             JOIN dbo.ChartOfAccounts coa ON jd.AccountID=coa.AccountID
                             WHERE jd.AccountID=@AccID AND je.JournalStatus=2 AND (@From IS NULL OR je.JournalDate>=@From) AND (@To IS NULL OR je.JournalDate<=@To)
                             ORDER BY je.JournalDate, je.JournalID";
                var result = await connection.QueryAsync<AccountLedgerDto>(sql2, new { AccID = accountId, From = from, To = to });
                return result.ToList();
            }
        }
    }

    public class TrialBalanceDto
    {
        public int AccountID { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountNameAr { get; set; }
        public int AccountLevel { get; set; }
        public int AccountType { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal Balance => OpeningBalance + TotalDebit - TotalCredit;
    }

    public class AccountLedgerDto
    {
        public int JournalDetailID { get; set; }
        public int JournalID { get; set; }
        public string? JournalNumber { get; set; }
        public DateTime JournalDate { get; set; }
        public string? Description { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountNameAr { get; set; }
        public decimal RunningBalance { get; set; }
    }
}
