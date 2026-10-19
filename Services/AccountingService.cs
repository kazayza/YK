using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class AccountingService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly ModuleAuthorizationService _authorization;

        public AccountingService(
            IConfiguration configuration,
            AuditService audit,
            ModuleAuthorizationService authorization) : base(configuration)
        {
            _audit = audit;
            _authorization = authorization;
        }

        private async Task RequireFiscalLookupPermissionAsync(string deniedMessage)
        {
            try
            {
                await RequireCurrentFiscalPermissionAsync(p => p.CanView, deniedMessage);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // Expense screens need fiscal-year/period labels as lookup data, but not admin access.
            }

            await RequireCurrentExpensePermissionAsync(p => p.CanView, deniedMessage);
        }

        private Task RequireCurrentFiscalPermissionAsync(
            Func<ModulePermissionDto, bool> predicate,
            string deniedMessage) =>
            _authorization.RequireAnyCurrentUserPermissionAsync(
                deniedMessage,
                ("SCR_FY", predicate),
                ("SCR_FISCALYEARS", predicate),
                ("SCR_FISCAL_YEARS", predicate));

        private Task RequireFiscalPermissionAsync(
            int userId,
            Func<ModulePermissionDto, bool> predicate,
            string deniedMessage) =>
            _authorization.RequireAnyUserPermissionAsync(
                userId,
                deniedMessage,
                ("SCR_FY", predicate),
                ("SCR_FISCALYEARS", predicate),
                ("SCR_FISCAL_YEARS", predicate));

        private Task RequireCurrentExpensePermissionAsync(
            Func<ModulePermissionDto, bool> predicate,
            string deniedMessage) =>
            _authorization.RequireAnyCurrentUserPermissionAsync(
                deniedMessage,
                ("SCR_EXPENSES", predicate),
                ("SCR_EXPENSE", predicate),
                ("SCR_OVERHEAD", predicate),
                ("SCR_OVERHEAD_EXPENSES", predicate));

        private Task RequireExpensePermissionAsync(
            int userId,
            Func<ModulePermissionDto, bool> predicate,
            string deniedMessage) =>
            _authorization.RequireAnyUserPermissionAsync(
                userId,
                deniedMessage,
                ("SCR_EXPENSES", predicate),
                ("SCR_EXPENSE", predicate),
                ("SCR_OVERHEAD", predicate),
                ("SCR_OVERHEAD_EXPENSES", predicate));

        // ==========================================
        // شجرة الحسابات - على الجدول الحقيقي ChartOfAccounts
        // ==========================================
        // حساب الأرصدة الفعلية من القيود - الرصيد الحقيقي = افتتاحي + مدين - دائن
        public async Task<List<ChartOfAccountDto>> GetChartOfAccountsAsync()
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_COA", p => p.CanView, "ليس لديك صلاحية عرض شجرة الحسابات.");

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
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_COA", p => p.CanView, "ليس لديك صلاحية عرض شجرة الحسابات.");

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

        public async Task<List<ChartOfAccountDto>> GetExpenseAccountLookupsAsync()
        {
            await RequireCurrentExpensePermissionAsync(
                p => p.CanView || p.CanAdd || p.CanEdit,
                "ليس لديك صلاحية استخدام قوائم حسابات المصروفات.");

            return await GetAccountSelectionLookupsCoreAsync();
        }

        public async Task<List<ChartOfAccountDto>> GetCashBoxAccountLookupsAsync()
        {
            await _authorization.RequireAnyCurrentUserPermissionAsync(
                "ليس لديك صلاحية استخدام قوائم حسابات الخزائن.",
                ("SCR_CASHBOX", p => p.CanView || p.CanAdd || p.CanEdit),
                ("SCR_CASHBOXES", p => p.CanView || p.CanAdd || p.CanEdit),
                ("SCR_BANKS", p => p.CanView || p.CanAdd || p.CanEdit));

            return await GetAccountSelectionLookupsCoreAsync();
        }

        private async Task<List<ChartOfAccountDto>> GetAccountSelectionLookupsCoreAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT AccountID, AccountCode, AccountNameAr, AccountType,
                                  CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية'
                                                   WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                                  IsDetailAccount, IsActive
                            FROM dbo.ChartOfAccounts WHERE IsActive=1 ORDER BY AccountCode";
                var result = await connection.QueryAsync<ChartOfAccountDto>(sql);
                return result.ToList();
            }
            catch
            {
                var sql = @"SELECT AccountID, AccountCode, AccountNameAr, AccountType,
                                  CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية'
                                                   WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                                  IsDetailAccount, 1 AS IsActive
                            FROM dbo.ChartOfAccounts ORDER BY AccountCode";
                var result = await connection.QueryAsync<ChartOfAccountDto>(sql);
                return result.ToList();
            }
        }

        public async Task<ChartOfAccountDto?> GetAccountByIdAsync(int id)
        {
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_COA", p => p.CanView, "ليس لديك صلاحية عرض تفاصيل الحساب.");

            using var connection = CreateConnection();
            var sql = @"SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType, AccountNature, IsDetailAccount,
                               CurrencyID, CostCenterRequired, OpeningBalance, CurrentBalance, LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount,
                               SortOrder, Description, IsActive, 0 AS ChildrenCount
                        FROM dbo.ChartOfAccounts WHERE AccountID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ChartOfAccountDto>(sql, new { ID = id });
        }

        public async Task<int> InsertAccountAsync(ChartOfAccountDto dto, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_COA", p => p.CanAdd, "ليس لديك صلاحية إضافة حساب.");

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
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_COA", p => p.CanEdit, "ليس لديك صلاحية تعديل الحسابات.");

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
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_COA", p => p.CanDelete, "ليس لديك صلاحية حذف الحسابات.");

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
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_JV", p => p.CanView, "ليس لديك صلاحية عرض القيود المحاسبية.");

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
            await _authorization.RequireCurrentUserPermissionAsync(
                "SCR_JV", p => p.CanView, "ليس لديك صلاحية عرض تفاصيل القيد المحاسبي.");

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

        public async Task<string> GenerateJournalNoAsync(int userId)
        {
            await _authorization.RequireAnyUserPermissionAsync(
                userId,
                "ليس لديك صلاحية إنشاء رقم قيد يومية.",
                ("SCR_JV", p => p.CanAdd),
                ("SCR_JV_NEW", p => p.CanAdd));

            return await GenerateJournalNoCoreAsync();
        }

        private async Task<string> GenerateJournalNoCoreAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لقيد اليومية.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم قيد اليومية. لم يتم إنشاء رقم بديل؛ راجع إعداد تسلسل JV والإجراء المخزن.", ex);
            }
        }

        public async Task<int> InsertJournalAsync(JournalEntryDto dto, int userId)
        {
            await _authorization.RequireAnyUserPermissionAsync(
                userId,
                "ليس لديك صلاحية إنشاء قيد يومية.",
                ("SCR_JV", p => p.CanAdd),
                ("SCR_JV_NEW", p => p.CanAdd));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                if (string.IsNullOrWhiteSpace(dto.JournalNumber))
                    dto.JournalNumber = await GenerateJournalNoCoreAsync();

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
                        IsAutoGenerated = false,
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

        public async Task UpdateJournalAsync(JournalEntryDto dto, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_JV", p => p.CanEdit, "ليس لديك صلاحية تعديل القيود المحاسبية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(int JournalStatus, bool IsAutoGenerated)>(
                    "SELECT JournalStatus, IsAutoGenerated FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK) WHERE JournalID=@ID",
                    new { ID = dto.JournalID }, trans);
                if (current.JournalStatus == 0)
                    throw new Exception("القيد المطلوب غير موجود.");
                if (current.JournalStatus != 1)
                    throw new Exception("لا يمكن تعديل قيد مرحل.");
                if (current.IsAutoGenerated)
                    throw new Exception("هذا قيد أوتوماتيك؛ عدل المستند الأصلي بدلاً من القيد.");

                var validDetails = dto.Details
                    .Where(d => d.AccountID > 0 && (d.DebitAmount > 0 || d.CreditAmount > 0))
                    .ToList();
                if (validDetails.Count < 2)
                    throw new Exception("يجب إدخال حسابين على الأقل بمبالغ.");
                if (validDetails.Any(d => d.DebitAmount > 0 && d.CreditAmount > 0))
                    throw new Exception("لا يمكن أن يكون السطر مديناً ودائناً معاً.");

                dto.TotalDebit = validDetails.Sum(d => d.DebitAmount);
                dto.TotalCredit = validDetails.Sum(d => d.CreditAmount);
                if (Math.Abs(dto.TotalDebit - dto.TotalCredit) > 0.01m)
                    throw new Exception($"القيد غير متوازن - مدين: {dto.TotalDebit:#,##0.00} ≠ دائن: {dto.TotalCredit:#,##0.00}");
                if (dto.TotalDebit <= 0)
                    throw new Exception("لا يمكن حفظ قيد صفري.");

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
                catch { /* حافظ على سلوك المخطط الحالي عند غياب الجداول الاختيارية. */ }

                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.JournalEntries
                      SET JournalDate=@JournalDate, Description=@Description, Reference=@Reference,
                          TotalDebit=@TotalDebit, TotalCredit=@TotalCredit, FiscalYearID=@FiscalYearID,
                          PeriodID=@PeriodID, JournalType=@JournalType
                      WHERE JournalID=@JournalID AND JournalStatus=1 AND IsAutoGenerated=0",
                    new
                    {
                        dto.JournalID,
                        dto.JournalDate,
                        dto.Description,
                        dto.Reference,
                        dto.TotalDebit,
                        dto.TotalCredit,
                        FiscalYearID = fiscalYearId,
                        PeriodID = periodId,
                        dto.JournalType
                    }, trans);
                if (affected != 1)
                    throw new Exception("تعذر تحديث القيد؛ ربما تغيرت حالته أثناء التعديل.");

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.JournalEntryDetails WHERE JournalID=@JournalID",
                    new { dto.JournalID }, trans);

                int line = 1;
                foreach (var detail in validDetails)
                {
                    await connection.ExecuteAsync(
                        "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID) VALUES(@JID, @Line, @AccID, @Debit, @Credit, @Desc, NULLIF(@CCID,0))",
                        new
                        {
                            JID = dto.JournalID,
                            Line = line++,
                            AccID = detail.AccountID,
                            Debit = detail.DebitAmount,
                            Credit = detail.CreditAmount,
                            Desc = detail.Description,
                            CCID = detail.CostCenterID ?? 0
                        }, trans);
                }

                trans.Commit();
                try { await _audit.WriteAuditLogAsync(userId, 2, "JournalEntries", dto.JournalID.ToString(), moduleName: "SCR_JV", description: $"تعديل قيد: {dto.JournalNumber}"); } catch { }
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task PostJournalAsync(int id, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_JV", p => p.CanPost, "ليس لديك صلاحية ترحيل القيود المحاسبية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var journal = await connection.QueryFirstOrDefaultAsync<(
                    int JournalStatus,
                    bool IsAutoGenerated,
                    int? FiscalYearID,
                    int? PeriodID,
                    DateTime JournalDate)>(
                    "SELECT JournalStatus, ISNULL(IsAutoGenerated,1) AS IsAutoGenerated, FiscalYearID, PeriodID, JournalDate FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK) WHERE JournalID=@ID",
                    new { ID = id }, trans);
                if (journal.JournalStatus == 0) throw new Exception("القيد المطلوب غير موجود.");
                if (journal.JournalStatus != 1) throw new Exception("القيد ليس مسودة؛ لا يمكن ترحيله مرة أخرى.");
                if (journal.IsAutoGenerated)
                    throw new Exception("لا يمكن ترحيل قيد أوتوماتيك يدوياً؛ استخدم اعتماد/ترحيل المستند المصدر.");
                if (!journal.FiscalYearID.HasValue || journal.FiscalYearID.Value <= 0
                    || !journal.PeriodID.HasValue || journal.PeriodID.Value <= 0)
                    throw new Exception("لا يمكن ترحيل القيد قبل ربطه بسنة وفترة مالية موجودتين ومفتوحتين.");

                var fiscalYear = await connection.QueryFirstOrDefaultAsync<(int YearStatus, DateTime StartDate, DateTime EndDate)>(
                    "SELECT YearStatus, StartDate, EndDate FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = journal.FiscalYearID.Value }, trans);
                if (fiscalYear.YearStatus != 1)
                    throw new Exception("السنة المالية المرتبطة بالقيد غير موجودة أو مغلقة.");
                if (journal.JournalDate.Date < fiscalYear.StartDate.Date || journal.JournalDate.Date > fiscalYear.EndDate.Date)
                    throw new Exception("تاريخ القيد خارج نطاق السنة المالية المرتبطة به.");

                var period = await connection.QueryFirstOrDefaultAsync<(int FiscalYearID, int PeriodStatus, DateTime StartDate, DateTime EndDate)>(
                    "SELECT FiscalYearID, PeriodStatus, StartDate, EndDate FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK) WHERE PeriodID=@ID",
                    new { ID = journal.PeriodID.Value }, trans);
                if (period.FiscalYearID != journal.FiscalYearID.Value || period.PeriodStatus != 1)
                    throw new Exception("الفترة المرتبطة بالقيد غير موجودة أو مغلقة أو لا تتبع السنة المحددة.");
                if (journal.JournalDate.Date < period.StartDate.Date || journal.JournalDate.Date > period.EndDate.Date)
                    throw new Exception("تاريخ القيد خارج نطاق الفترة المالية المرتبطة به.");

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

                await connection.ExecuteAsync(
                    "UPDATE dbo.JournalEntries SET JournalStatus=2, PostedBy=@UserID, PostedDate=GETDATE() WHERE JournalID=@ID AND JournalStatus=1",
                    new { UserID = userId, ID = id }, trans);

                trans.Commit();
                try { await _audit.WriteAuditLogAsync(userId, 2, "JournalEntries", id.ToString(), moduleName: "SCR_JV", description: "ترحيل قيد محاسبي وتحديث الأرصدة"); } catch { }
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task DeleteJournalAsync(int id, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_JV", p => p.CanDelete, "ليس لديك صلاحية حذف القيود المحاسبية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(int JournalStatus, bool IsAutoGenerated)>(
                    "SELECT JournalStatus, IsAutoGenerated FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK) WHERE JournalID=@ID",
                    new { ID = id }, trans);
                if (current.JournalStatus == 0)
                    throw new Exception("القيد المطلوب غير موجود.");
                if (current.JournalStatus != 1)
                    throw new Exception("لا يمكن حذف قيد مرحل.");
                if (current.IsAutoGenerated)
                    throw new Exception("لا يمكن حذف قيد أوتوماتيك من هذه الخدمة؛ احذف أو اعكس المستند المصدر وفق إجراءه.");

                await connection.ExecuteAsync("DELETE FROM dbo.JournalEntryDetails WHERE JournalID=@ID", new { ID = id }, trans);
                await connection.ExecuteAsync("DELETE FROM dbo.JournalEntries WHERE JournalID=@ID AND JournalStatus=1 AND IsAutoGenerated=0", new { ID = id }, trans);
                trans.Commit();
                try { await _audit.WriteAuditLogAsync(userId, 3, "JournalEntries", id.ToString(), moduleName: "SCR_JV", description: "حذف قيد محاسبي"); } catch { }
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // ==========================================
        // السنوات المالية - على الجدول الحقيقي FiscalYears 11 عمود - بدون IsActive
        // ==========================================
        public async Task<List<FiscalYearListDto>> GetFiscalYearsAsync()
        {
            await RequireFiscalLookupPermissionAsync("ليس لديك صلاحية عرض السنوات المالية.");

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
            await RequireCurrentFiscalPermissionAsync(
                p => p.CanView, "ليس لديك صلاحية عرض تفاصيل السنة المالية.");

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
            await RequireFiscalPermissionAsync(
                userId, p => p.CanAdd, "ليس لديك صلاحية إنشاء سنة مالية.");

            if (dto.EndDate <= dto.StartDate) throw new Exception("تاريخ النهاية يجب أن يكون بعد البداية");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            int newId;
            try
            {
                var sql = @"INSERT INTO dbo.FiscalYears(YearCode, YearName, StartDate, EndDate, YearStatus, IsCurrent, Notes, CreatedDate)
                            VALUES(@YearCode, @YearName, @StartDate, @EndDate, 1, @IsCurrent, @Notes, GETDATE());
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";
                newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    dto.YearCode,
                    dto.YearName,
                    dto.StartDate,
                    dto.EndDate,
                    dto.IsCurrent,
                    dto.Notes
                }, trans);

                if (dto.IsCurrent)
                    await connection.ExecuteAsync("UPDATE dbo.FiscalYears SET IsCurrent=0 WHERE FiscalYearID<>@ID", new { ID = newId }, trans);

                // إنشاء الفترات الشهرية في معاملة السنة نفسها حتى لا يبقى إعداد مالي جزئي عند الفشل.
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
                        new { FYID = newId, Num = m, Name = periodName, S = pStart, E = pEnd }, trans);
                }

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            try { await _audit.WriteAuditLogAsync(userId, 1, "FiscalYears", newId.ToString(), moduleName: "SCR_FY", description: $"إنشاء سنة مالية: {dto.YearName}"); } catch { }
            return newId;
        }

        public async Task UpdateFiscalYearAsync(FiscalYearDto dto, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanEdit, "ليس لديك صلاحية تعديل السنوات المالية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                // الحالة لا تُعدّل هنا؛ الإغلاق محصور في CloseFiscalYearAsync بصلاحية CanApprove.
                var sql = @"UPDATE dbo.FiscalYears SET YearCode=@YearCode, YearName=@YearName, StartDate=@StartDate, EndDate=@EndDate, IsCurrent=@IsCurrent, Notes=@Notes WHERE FiscalYearID=@FiscalYearID";
                await connection.ExecuteAsync(sql, new
                {
                    dto.YearCode,
                    dto.YearName,
                    dto.StartDate,
                    dto.EndDate,
                    dto.IsCurrent,
                    dto.Notes,
                    dto.FiscalYearID
                }, trans);
                if (dto.IsCurrent)
                    await connection.ExecuteAsync("UPDATE dbo.FiscalYears SET IsCurrent=0 WHERE FiscalYearID<>@ID", new { ID = dto.FiscalYearID }, trans);

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            try { await _audit.WriteAuditLogAsync(userId, 2, "FiscalYears", dto.FiscalYearID.ToString(), moduleName: "SCR_FY", description: $"تعديل سنة مالية: {dto.YearName}"); } catch { }
        }

        public async Task CloseFiscalYearAsync(int id, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanApprove, "ليس لديك صلاحية إغلاق السنة المالية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var yearStatus = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT YearStatus FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = id }, trans);
                if (!yearStatus.HasValue) throw new Exception("السنة المالية غير موجودة.");

                var hasOpenPeriods = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID AND PeriodStatus=1",
                    new { ID = id }, trans);
                if (hasOpenPeriods > 0) throw new Exception("يجب إغلاق جميع الفترات أولاً");

                await connection.ExecuteAsync(
                    "UPDATE dbo.FiscalYears SET YearStatus=2, ClosedBy=@UserID, ClosedDate=GETDATE() WHERE FiscalYearID=@ID",
                    new { UserID = userId, ID = id }, trans);
                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            try { await _audit.WriteAuditLogAsync(userId, 2, "FiscalYears", id.ToString(), moduleName: "SCR_FY", description: "إغلاق سنة مالية"); } catch { }
        }

        // ==========================================
        // الفترات المحاسبية - على الجدول الحقيقي AccountingPeriods 9 أعمدة - بدون IsActive
        // ==========================================
        public async Task<List<AccountingPeriodListDto>> GetAccountingPeriodsAsync(int? fiscalYearId = null)
        {
            await RequireFiscalLookupPermissionAsync("ليس لديك صلاحية عرض الفترات المحاسبية.");

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
            await RequireFiscalPermissionAsync(
                userId, p => p.CanApprove, "ليس لديك صلاحية إغلاق الفترة المحاسبية.");

            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.AccountingPeriods SET PeriodStatus=2, ClosedBy=@UserID, ClosedDate=GETDATE() WHERE PeriodID=@ID", new { UserID = userId, ID = id });
            try { await _audit.WriteAuditLogAsync(userId, 2, "AccountingPeriods", id.ToString(), moduleName: "SCR_FY", description: "إغلاق فترة محاسبية"); } catch { }
        }

        public async Task OpenPeriodAsync(int id, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanApprove, "ليس لديك صلاحية إعادة فتح الفترة المحاسبية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                // اقرأ مرجع السنة أولاً، ثم اقفل السنة والفترة بهذا الترتيب نفسه المستخدم عند إغلاق السنة.
                var fiscalYearId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT FiscalYearID FROM dbo.AccountingPeriods WHERE PeriodID=@ID",
                    new { ID = id }, trans);
                if (!fiscalYearId.HasValue || fiscalYearId.Value <= 0)
                    throw new Exception("الفترة المحاسبية غير موجودة أو غير مرتبطة بسنة مالية.");

                var yearStatus = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT YearStatus FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = fiscalYearId.Value }, trans);
                if (yearStatus != 1)
                    throw new Exception("لا يمكن فتح فترة داخل سنة مالية مغلقة أو غير موجودة.");

                var period = await connection.QueryFirstOrDefaultAsync<(int FiscalYearID, int PeriodStatus)>(
                    "SELECT FiscalYearID, PeriodStatus FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK) WHERE PeriodID=@ID",
                    new { ID = id }, trans);
                if (period.FiscalYearID != fiscalYearId.Value)
                    throw new Exception("تغير ارتباط الفترة بالسنة المالية أثناء العملية؛ أعد المحاولة.");

                await connection.ExecuteAsync(
                    "UPDATE dbo.AccountingPeriods SET PeriodStatus=1, ClosedBy=NULL, ClosedDate=NULL WHERE PeriodID=@ID AND FiscalYearID=@FYID",
                    new { ID = id, FYID = fiscalYearId.Value }, trans);
                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            try { await _audit.WriteAuditLogAsync(userId, 2, "AccountingPeriods", id.ToString(), moduleName: "SCR_FY", description: "فتح فترة محاسبية"); } catch { }
        }

        // ==========================================
        // المصروفات - OverheadExpenses Gold Edition Pro
        // مع ربط بمركز تكلفة اختياري + فترة محاسبية + قيد أوتوماتيك
        // ==========================================
        public async Task<List<ExpenseListDto>> GetExpensesAsync(DateTime? from = null, DateTime? to = null, int? costCenterId = null, int? periodId = null, int? expenseId = null)
        {
            await RequireCurrentExpensePermissionAsync(
                p => p.CanView, "ليس لديك صلاحية عرض المصروفات.");

            using var connection = CreateConnection();
            // جدول المصروف هو المصدر المعتمد للمبلغ؛ لا نعرض قيمة مشتقة من View غير متاح تعريفه محلياً.
            var expenseIdFilter = expenseId.HasValue ? "AND oe.ExpenseID=@ExpenseID" : string.Empty;
            var sql = $@"SELECT oe.ExpenseID, ISNULL(oe.ExpenseNumber,'EXP-'+CAST(oe.ExpenseID AS NVARCHAR)) AS ExpenseNumber,
                               oe.ExpenseDate, oe.PeriodID, ap.PeriodName, ap.PeriodNumber, ap.FiscalYearID AS FiscalYearID, fy.YearName AS FiscalYearName,
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
                        LEFT JOIN dbo.FiscalYears fy ON ap.FiscalYearID=fy.FiscalYearID
                        LEFT JOIN dbo.ChartOfAccounts coa ON oe.AccountID=coa.AccountID
                        LEFT JOIN dbo.ChartOfAccounts coa2 ON oe.CreditAccountID=coa2.AccountID
                        LEFT JOIN dbo.CostCenters cc ON oe.CostCenterID=cc.CostCenterID
                        LEFT JOIN dbo.JournalEntries je ON oe.JournalID=je.JournalID
                        LEFT JOIN dbo.CashBoxes cb ON oe.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.BankAccounts ba ON oe.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.SystemUsers su ON oe.CreatedBy=su.UserID
                        WHERE (@From IS NULL OR oe.ExpenseDate >= @From)
                          AND (@To IS NULL OR oe.ExpenseDate <= @To)
                          AND (@CCID IS NULL OR oe.CostCenterID = @CCID)
                          AND (@PID IS NULL OR oe.PeriodID = @PID)
                          {expenseIdFilter}
                        ORDER BY oe.ExpenseDate DESC, oe.ExpenseID DESC";
            var result = await connection.QueryAsync<ExpenseListDto>(sql,
                new { From = from, To = to, CCID = costCenterId, PID = periodId, ExpenseID = expenseId });
            return result.ToList();
        }

        public async Task<ExpenseDto?> GetExpenseByIdAsync(int id)
        {
            await RequireCurrentExpensePermissionAsync(
                p => p.CanView, "ليس لديك صلاحية عرض المصروفات.");

            using var connection = CreateConnection();
            var sql = @"SELECT oe.ExpenseID, oe.ExpenseNumber, oe.ExpenseDate, oe.PeriodID,
                               ap.FiscalYearID AS FiscalYearID, oe.AccountID, oe.CreditAccountID, oe.CostCenterID,
                               oe.Description, oe.Amount, oe.Reference, oe.ReferenceType, oe.ExpenseStatus,
                               oe.IsPosted, oe.JournalID, oe.PaymentMethod, oe.CashBoxID, oe.BankAccountID,
                               oe.Notes, oe.CreatedBy, oe.CreatedDate
                        FROM dbo.OverheadExpenses oe
                        LEFT JOIN dbo.AccountingPeriods ap ON oe.PeriodID=ap.PeriodID
                        WHERE oe.ExpenseID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ExpenseDto>(sql, new { ID = id });
        }

        public async Task<string> GenerateExpenseNoAsync()
        {
            await RequireCurrentExpensePermissionAsync(
                p => p.CanAdd, "ليس لديك صلاحية إنشاء مصروف.");

            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'EXP', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً للمصروف.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم المصروف. لم يتم إنشاء رقم بديل؛ راجع إعداد تسلسل EXP والإجراء المخزن.", ex);
            }
        }

        public async Task<int> InsertExpenseAsync(ExpenseDto dto, int userId)
        {
            await RequireExpensePermissionAsync(
                userId, p => p.CanAdd, "ليس لديك صلاحية إنشاء مصروف.");

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

            var journalNumber = await GenerateJournalNoCoreAsync();

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                // استنتاج الفترة والسنة من البيانات الموجودة فقط؛ لا تُنشأ إعدادات مالية من مسار المصروفات.
                int? periodFiscalYearId = null;
                if (dto.PeriodID == 0)
                {
                    var period = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT TOP 1 PeriodID, FiscalYearID FROM dbo.AccountingPeriods WHERE @EDate BETWEEN StartDate AND EndDate ORDER BY PeriodNumber",
                        new { EDate = dto.ExpenseDate }, trans);
                    if (period != null)
                    {
                        dto.PeriodID = (int)period.PeriodID;
                        periodFiscalYearId = (int?)period.FiscalYearID;
                    }
                }

                if (dto.PeriodID == 0)
                    throw new Exception($"لا توجد فترة محاسبية تغطي تاريخ {dto.ExpenseDate:yyyy-MM-dd}. أنشئ السنة والفترات من شاشة إدارة السنوات المالية أولاً.");

                if (!periodFiscalYearId.HasValue)
                {
                    periodFiscalYearId = await connection.QueryFirstOrDefaultAsync<int?>(
                        "SELECT FiscalYearID FROM dbo.AccountingPeriods WHERE PeriodID=@PeriodID",
                        new { PeriodID = dto.PeriodID }, trans);
                }

                if (!periodFiscalYearId.HasValue || periodFiscalYearId.Value <= 0)
                    throw new Exception("الفترة المحاسبية المحددة غير موجودة أو غير مرتبطة بسنة مالية.");

                // اقفل السنة أولاً ثم الفترة، بنفس ترتيب إغلاق السنة/إعادة فتح الفترة.
                var fiscalYear = await connection.QueryFirstOrDefaultAsync<(int YearStatus, DateTime StartDate, DateTime EndDate)>(
                    "SELECT YearStatus, StartDate, EndDate FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = periodFiscalYearId.Value }, trans);
                if (fiscalYear.YearStatus != 1)
                    throw new Exception("لا يمكن إنشاء مصروف داخل سنة مالية مغلقة أو غير موجودة.");

                var periodState = await connection.QueryFirstOrDefaultAsync<(int FiscalYearID, int PeriodStatus, DateTime StartDate, DateTime EndDate)>(
                    "SELECT FiscalYearID, PeriodStatus, StartDate, EndDate FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK) WHERE PeriodID=@ID",
                    new { ID = dto.PeriodID }, trans);
                if (periodState.FiscalYearID != periodFiscalYearId.Value || periodState.PeriodStatus != 1)
                    throw new Exception("لا يمكن إنشاء مصروف داخل فترة مغلقة أو غير مرتبطة بالسنة المحددة.");
                if (dto.ExpenseDate.Date < fiscalYear.StartDate.Date || dto.ExpenseDate.Date > fiscalYear.EndDate.Date
                    || dto.ExpenseDate.Date < periodState.StartDate.Date || dto.ExpenseDate.Date > periodState.EndDate.Date)
                    throw new Exception("تاريخ المصروف خارج نطاق السنة أو الفترة المحاسبية المفتوحة المحددة.");

                if (dto.FiscalYearID.HasValue && dto.FiscalYearID.Value > 0 && dto.FiscalYearID.Value != periodFiscalYearId.Value)
                    throw new Exception("السنة المالية المحددة لا تطابق الفترة المحاسبية المختارة.");

                dto.FiscalYearID = periodFiscalYearId.Value;

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
                    if ((creditAccId == null || creditAccId == 0) &&
                        dto.BankAccountID is int bankAccountId && bankAccountId > 0)
                    {
                        creditAccId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 AccountID FROM dbo.BankAccounts WHERE BankAccountID=@ID", new { ID = bankAccountId }, trans);
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
            await RequireExpensePermissionAsync(
                userId, p => p.CanEdit, "ليس لديك صلاحية تعديل المصروفات.");

            if (dto.ExpenseID <= 0) throw new Exception("المصروف المحدد غير صالح.");
            if (dto.Amount <= 0) throw new Exception("المبلغ يجب أن يكون أكبر من صفر.");
            if (dto.AccountID <= 0) throw new Exception("يجب اختيار حساب المصروف.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(
                    int? ExpenseStatus,
                    bool IsPosted,
                    int? JournalID,
                    decimal Amount,
                    int? PeriodID,
                    DateTime? ExpenseDate,
                    int? CashBoxID,
                    int? BankAccountID,
                    int? CreditAccountID)>(
                    @"SELECT ISNULL(ExpenseStatus,1) AS ExpenseStatus, ISNULL(IsPosted,0) AS IsPosted,
                             JournalID, Amount, PeriodID, ExpenseDate, CashBoxID, BankAccountID, CreditAccountID
                      FROM dbo.OverheadExpenses WITH (UPDLOCK, HOLDLOCK)
                      WHERE ExpenseID=@ID",
                    new { ID = dto.ExpenseID }, trans);

                if (!current.ExpenseStatus.HasValue) throw new Exception("المصروف غير موجود.");
                if (current.ExpenseStatus.Value != 1) throw new Exception("لا يمكن تعديل مصروف معتمد أو ملغي.");

                var hasOldJournal = current.JournalID.HasValue && current.JournalID.Value > 0;
                if (current.IsPosted && !hasOldJournal)
                    throw new Exception("المصروف معلّم كمرحّل دون رقم قيد؛ تعذر إثبات الأثر القديم بأمان، ولم يتم تعديله.");
                if (hasOldJournal && !current.IsPosted)
                    throw new Exception("المصروف مرتبط بقيد لكن حالة أثره غير مؤكدة؛ أُوقف التعديل لتجنب عكس أو إعادة تطبيق أرصدة غير مثبتة.");

                var hasFinancialEffect = current.IsPosted || hasOldJournal;
                if (hasFinancialEffect)
                {
                    await RequireExpensePermissionAsync(
                        userId, p => p.CanApprove,
                        "تعديل مصروف له أثر مالي يتطلب صلاحية التعديل والاعتماد معاً.");
                }

                // إذا لم يحدد المستدعي فترة، استنتجها من تاريخ المصروف دون إنشاء سنة/فترة.
                if (dto.PeriodID <= 0)
                {
                    var inferredPeriodId = await connection.QueryFirstOrDefaultAsync<int?>(
                        @"SELECT TOP 1 PeriodID FROM dbo.AccountingPeriods
                          WHERE @ExpenseDate BETWEEN StartDate AND EndDate
                          ORDER BY PeriodNumber",
                        new { dto.ExpenseDate }, trans);
                    if (!inferredPeriodId.HasValue || inferredPeriodId.Value <= 0)
                        throw new Exception($"لا توجد فترة محاسبية تغطي تاريخ {dto.ExpenseDate:yyyy-MM-dd}.");
                    dto.PeriodID = inferredPeriodId.Value;
                }

                var newFiscalYearId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT FiscalYearID FROM dbo.AccountingPeriods WHERE PeriodID=@ID",
                    new { ID = dto.PeriodID }, trans);
                if (!newFiscalYearId.HasValue || newFiscalYearId.Value <= 0)
                    throw new Exception("الفترة المحاسبية المحددة غير موجودة أو غير مرتبطة بسنة مالية.");
                if (dto.FiscalYearID.HasValue && dto.FiscalYearID.Value > 0
                    && dto.FiscalYearID.Value != newFiscalYearId.Value)
                    throw new Exception("السنة المالية المحددة لا تطابق الفترة المحاسبية المختارة.");
                dto.FiscalYearID = newFiscalYearId.Value;

                int? oldFiscalYearId = null;
                int? oldPeriodId = null;
                DateTime? oldJournalDate = null;
                decimal oldFinancialAmount = 0m;
                var oldJournalLines = new List<(int AccountID, decimal Debit, decimal Credit)>();
                int oldJournalId = hasOldJournal ? current.JournalID!.Value : 0;

                if (hasOldJournal)
                {
                    var oldJournal = await connection.QueryFirstOrDefaultAsync<(
                        int? JournalStatus,
                        bool IsAutoGenerated,
                        string? SourceDocType,
                        int? SourceDocID,
                        int? FiscalYearID,
                        int? PeriodID,
                        DateTime? JournalDate)>(
                        @"SELECT JournalStatus, ISNULL(IsAutoGenerated,0) AS IsAutoGenerated,
                                 SourceDocType, SourceDocID, FiscalYearID, PeriodID, JournalDate
                          FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                          WHERE JournalID=@ID",
                        new { ID = oldJournalId }, trans);

                    if (!oldJournal.JournalStatus.HasValue)
                        throw new Exception("قيد المصروف غير موجود؛ تعذر عكس الأثر القديم، ولم يتم تعديل المصروف.");
                    if (oldJournal.JournalStatus.Value != 1 || !oldJournal.IsAutoGenerated)
                        throw new Exception("قيد المصروف ليس قيداً أوتوماتيكياً مسودة؛ يُرفض التعديل حفاظاً على القيد المالي.");
                    if (!string.Equals(oldJournal.SourceDocType, "OverheadExpense", StringComparison.OrdinalIgnoreCase)
                        || oldJournal.SourceDocID != dto.ExpenseID)
                        throw new Exception("القيد غير مرتبط بهذا المصروف على نحو يمكن إثباته؛ لم يتم تعديل أي بيانات.");
                    if (!oldJournal.FiscalYearID.HasValue || oldJournal.FiscalYearID.Value <= 0
                        || !oldJournal.PeriodID.HasValue || oldJournal.PeriodID.Value <= 0
                        || !oldJournal.JournalDate.HasValue)
                        throw new Exception("القيد القديم يفتقد بيانات السنة أو الفترة أو التاريخ؛ تعذر عكسه بأمان.");

                    oldFiscalYearId = oldJournal.FiscalYearID.Value;
                    oldPeriodId = oldJournal.PeriodID.Value;
                    oldJournalDate = oldJournal.JournalDate.Value;
                    oldJournalLines = (await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit)>(
                        @"SELECT AccountID, DebitAmount AS Debit, CreditAmount AS Credit
                          FROM dbo.JournalEntryDetails WITH (UPDLOCK, HOLDLOCK)
                          WHERE JournalID=@ID ORDER BY LineNumber",
                        new { ID = oldJournalId }, trans)).ToList();

                    // المصروفات التي ينشئها المسار الحالي لها سطر مدين وسطر دائن.
                    // استخدم التفاصيل نفسها للعكس، ولا تعتمد على CreditAccountID في رأس المصروف.
                    var debitLines = oldJournalLines.Where(x => x.Debit > 0m && x.Credit == 0m).ToList();
                    var creditLines = oldJournalLines.Where(x => x.Credit > 0m && x.Debit == 0m).ToList();
                    if (oldJournalLines.Count != 2 || debitLines.Count != 1 || creditLines.Count != 1
                        || oldJournalLines.Any(x => x.AccountID <= 0 || x.Debit < 0m || x.Credit < 0m))
                        throw new Exception("تفاصيل القيد القديم لا تطابق قيد مصروف قابل للعكس؛ لم يتم تغيير المصروف أو الأرصدة.");
                    if (debitLines[0].Debit != creditLines[0].Credit)
                        throw new Exception("القيد القديم غير متوازن؛ تعذر إثبات أثره المالي، ولم يتم تعديل المصروف.");

                    // إذا كان حساب الدائن أو حساب الخزينة/البنك المرتبط محفوظاً، يجب أن يطابق القيد القديم.
                    int? linkedOldCreditAccountId = current.CreditAccountID.GetValueOrDefault() > 0
                        ? current.CreditAccountID
                        : null;
                    if (!linkedOldCreditAccountId.HasValue && current.CashBoxID.GetValueOrDefault() > 0)
                        linkedOldCreditAccountId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT AccountID FROM dbo.CashBoxes WHERE CashBoxID=@ID",
                            new { ID = current.CashBoxID!.Value }, trans);
                    if (!linkedOldCreditAccountId.HasValue && current.BankAccountID.GetValueOrDefault() > 0)
                        linkedOldCreditAccountId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT AccountID FROM dbo.BankAccounts WHERE BankAccountID=@ID",
                            new { ID = current.BankAccountID!.Value }, trans);
                    if (linkedOldCreditAccountId.HasValue && linkedOldCreditAccountId.Value > 0
                        && linkedOldCreditAccountId.Value != creditLines[0].AccountID)
                        throw new Exception("ربط حساب الدائن الحالي لا يطابق تفاصيل القيد القديم؛ تعذر عكس رصيد الخزينة/البنك بأمان.");

                    // هذا هو مبلغ الأثر القديم (مثلاً 200)، حتى لو غُيّر مبلغ رأس المصروف سابقاً إلى 150.
                    oldFinancialAmount = debitLines[0].Debit;
                    if (oldFinancialAmount <= 0m)
                        throw new Exception("مبلغ القيد القديم غير صالح للعكس.");
                }

                // اقفل السنوات بالترتيب التصاعدي ثم الفترات بالترتيب نفسه لتجنب عكس ترتيب أقفال الإغلاق.
                var yearIds = new SortedSet<int> { newFiscalYearId.Value };
                if (oldFiscalYearId.HasValue) yearIds.Add(oldFiscalYearId.Value);
                var fiscalYears = new Dictionary<int, (int YearStatus, DateTime StartDate, DateTime EndDate)>();
                foreach (var yearId in yearIds)
                {
                    var yearState = await connection.QueryFirstOrDefaultAsync<(
                        int? YearStatus, DateTime? StartDate, DateTime? EndDate)>(
                        @"SELECT YearStatus, StartDate, EndDate
                          FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK)
                          WHERE FiscalYearID=@ID",
                        new { ID = yearId }, trans);
                    if (!yearState.YearStatus.HasValue || !yearState.StartDate.HasValue || !yearState.EndDate.HasValue)
                        throw new Exception("السنة المالية المطلوبة غير موجودة.");
                    fiscalYears[yearId] = (yearState.YearStatus.Value, yearState.StartDate.Value, yearState.EndDate.Value);
                }

                var periodIds = new SortedSet<int> { dto.PeriodID };
                if (oldPeriodId.HasValue) periodIds.Add(oldPeriodId.Value);
                var periodsById = new Dictionary<int, (int FiscalYearID, int PeriodStatus, DateTime StartDate, DateTime EndDate)>();
                foreach (var periodId in periodIds)
                {
                    var periodState = await connection.QueryFirstOrDefaultAsync<(
                        int? FiscalYearID, int? PeriodStatus, DateTime? StartDate, DateTime? EndDate)>(
                        @"SELECT FiscalYearID, PeriodStatus, StartDate, EndDate
                          FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK)
                          WHERE PeriodID=@ID",
                        new { ID = periodId }, trans);
                    if (!periodState.FiscalYearID.HasValue || !periodState.PeriodStatus.HasValue
                        || !periodState.StartDate.HasValue || !periodState.EndDate.HasValue)
                        throw new Exception("الفترة المحاسبية المطلوبة غير موجودة.");
                    periodsById[periodId] = (periodState.FiscalYearID.Value, periodState.PeriodStatus.Value,
                        periodState.StartDate.Value, periodState.EndDate.Value);
                }

                var newYear = fiscalYears[newFiscalYearId.Value];
                var newPeriod = periodsById[dto.PeriodID];
                if (newYear.YearStatus != 1 || newPeriod.PeriodStatus != 1
                    || newPeriod.FiscalYearID != newFiscalYearId.Value)
                    throw new Exception("لا يمكن تعديل مصروف داخل سنة أو فترة مغلقة أو غير متطابقة.");
                if (dto.ExpenseDate.Date < newYear.StartDate.Date || dto.ExpenseDate.Date > newYear.EndDate.Date
                    || dto.ExpenseDate.Date < newPeriod.StartDate.Date || dto.ExpenseDate.Date > newPeriod.EndDate.Date)
                    throw new Exception("تاريخ المصروف خارج نطاق السنة أو الفترة المحاسبية المفتوحة.");

                if (hasOldJournal)
                {
                    var oldYear = fiscalYears[oldFiscalYearId!.Value];
                    var oldPeriod = periodsById[oldPeriodId!.Value];
                    if (oldYear.YearStatus != 1 || oldPeriod.PeriodStatus != 1
                        || oldPeriod.FiscalYearID != oldFiscalYearId.Value)
                        throw new Exception("الفترة الأصلية للقيد مغلقة؛ لا يمكن عكسه أو تعديل تاريخها.");
                    if (oldJournalDate!.Value.Date < oldYear.StartDate.Date || oldJournalDate.Value.Date > oldYear.EndDate.Date
                        || oldJournalDate.Value.Date < oldPeriod.StartDate.Date || oldJournalDate.Value.Date > oldPeriod.EndDate.Date)
                        throw new Exception("تاريخ القيد القديم لا يطابق نطاق سنته وفترته؛ تعذر عكسه بأمان.");
                }

                // تحديد حساب الطرف الدائن وفق نفس قاعدة الإنشاء، مع الاعتماد على تفاصيل القيد القديم للعكس.
                int? newCreditAccountId = null;
                if (hasOldJournal)
                {
                    newCreditAccountId = dto.CreditAccountID;
                    if (!newCreditAccountId.HasValue || newCreditAccountId.Value <= 0)
                    {
                        if (dto.CashBoxID.HasValue && dto.CashBoxID.Value > 0)
                            newCreditAccountId = await connection.ExecuteScalarAsync<int?>(
                                "SELECT TOP 1 AccountID FROM dbo.CashBoxes WHERE CashBoxID=@ID",
                                new { ID = dto.CashBoxID.Value }, trans);
                        if ((!newCreditAccountId.HasValue || newCreditAccountId.Value <= 0)
                            && dto.BankAccountID.HasValue && dto.BankAccountID.Value > 0)
                            newCreditAccountId = await connection.ExecuteScalarAsync<int?>(
                                "SELECT TOP 1 AccountID FROM dbo.BankAccounts WHERE BankAccountID=@ID",
                                new { ID = dto.BankAccountID.Value }, trans);
                        if (!newCreditAccountId.HasValue || newCreditAccountId.Value <= 0)
                            newCreditAccountId = await connection.ExecuteScalarAsync<int?>(
                                @"SELECT TOP 1 AccountID FROM dbo.ChartOfAccounts
                                  WHERE AccountNameAr LIKE N'%نقدية%' OR AccountNameAr LIKE N'%خزينة%' OR AccountNameAr LIKE N'%صندوق%'
                                  ORDER BY AccountID", null, trans);
                    }

                    // احتفظ بسلوك الإنشاء للحسابات الافتراضية، لكن لا تعرض/تستخدم قيماً افتراضية كرصيد.
                    if ((!dto.CashBoxID.HasValue || dto.CashBoxID.Value <= 0) && dto.PaymentMethod == 1)
                    {
                        dto.CashBoxID = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 CashBoxID FROM dbo.CashBoxes WHERE IsDefault=1 ORDER BY CashBoxID", null, trans)
                            ?? await connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CashBoxID FROM dbo.CashBoxes ORDER BY CashBoxID", null, trans);
                    }
                    if ((!dto.BankAccountID.HasValue || dto.BankAccountID.Value <= 0) && dto.PaymentMethod == 2)
                        dto.BankAccountID = await connection.ExecuteScalarAsync<int?>(
                            "SELECT TOP 1 BankAccountID FROM dbo.BankAccounts ORDER BY BankAccountID", null, trans);
                }

                // عكس أرصدة الخزينة/البنك القديمة باستخدام قيمة القيد القديم، لا قيمة الرأس التي ربما تغيرت.
                if (hasOldJournal && current.CashBoxID.HasValue && current.CashBoxID.Value > 0)
                {
                    var reversed = await connection.ExecuteAsync(
                        "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amount WHERE CashBoxID=@ID",
                        new { Amount = oldFinancialAmount, ID = current.CashBoxID.Value }, trans);
                    if (reversed != 1) throw new Exception("تعذر تحديد الخزينة القديمة لعكس أثر المصروف؛ لم يتم حفظ التعديل.");
                }
                if (hasOldJournal && current.BankAccountID.HasValue && current.BankAccountID.Value > 0)
                {
                    var reversed = await connection.ExecuteAsync(
                        "UPDATE dbo.BankAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amount WHERE BankAccountID=@ID",
                        new { Amount = oldFinancialAmount, ID = current.BankAccountID.Value }, trans);
                    if (reversed != 1) throw new Exception("تعذر تحديد الحساب البنكي القديم لعكس أثر المصروف؛ لم يتم حفظ التعديل.");
                }

                async Task ApplyAccountDeltaAsync(int accountId, decimal delta)
                {
                    if (delta == 0m) return;
                    var affected = await connection.ExecuteAsync(
                        "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                        new { Delta = delta, ID = accountId }, trans);
                    if (affected != 1) throw new Exception("تعذر تحديث رصيد أحد الحسابات؛ أُلغيت عملية التعديل كاملة.");

                    int? currentAccountId = accountId;
                    for (var depth = 0; depth < 10; depth++)
                    {
                        var parentId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID",
                            new { ID = currentAccountId }, trans);
                        if (!parentId.HasValue || parentId.Value <= 0) break;
                        var parentAffected = await connection.ExecuteAsync(
                            "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                            new { Delta = delta, ID = parentId.Value }, trans);
                        if (parentAffected != 1) throw new Exception("تعذر تحديث رصيد الحساب الأب؛ أُلغيت عملية التعديل كاملة.");
                        currentAccountId = parentId.Value;
                    }
                }

                if (hasOldJournal)
                {
                    foreach (var group in oldJournalLines.GroupBy(x => x.AccountID).OrderBy(x => x.Key))
                    {
                        var oldDelta = group.Sum(x => x.Debit - x.Credit);
                        await ApplyAccountDeltaAsync(group.Key, -oldDelta);
                    }
                }

                var sourceJournalId = (int?)null;
                var sourceIsPosted = false;
                if (hasOldJournal && newCreditAccountId.HasValue && newCreditAccountId.Value > 0)
                {
                    var headerAffected = await connection.ExecuteAsync(
                        @"UPDATE dbo.JournalEntries
                          SET JournalDate=@JournalDate, Description=@Description, Reference=@Reference,
                              TotalDebit=@Amount, TotalCredit=@Amount, FiscalYearID=@FiscalYearID, PeriodID=@PeriodID
                          WHERE JournalID=@JournalID AND JournalStatus=1 AND IsAutoGenerated=1",
                        new
                        {
                            JournalDate = dto.ExpenseDate,
                            dto.Description,
                            dto.Reference,
                            dto.Amount,
                            dto.FiscalYearID,
                            dto.PeriodID,
                            JournalID = oldJournalId
                        }, trans);
                    if (headerAffected != 1)
                        throw new Exception("تغيرت حالة القيد أثناء التعديل؛ أُلغيت العملية.");

                    await connection.ExecuteAsync("DELETE FROM dbo.JournalEntryDetails WHERE JournalID=@ID", new { ID = oldJournalId }, trans);
                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID)
                          VALUES(@JournalID,1,@AccountID,@Amount,0,@Description,NULLIF(@CostCenterID,0))",
                        new { JournalID = oldJournalId, dto.AccountID, dto.Amount, dto.Description, CostCenterID = dto.CostCenterID ?? 0 }, trans);
                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID)
                          VALUES(@JournalID,2,@AccountID,0,@Amount,@Description,NULLIF(@CostCenterID,0))",
                        new { JournalID = oldJournalId, AccountID = newCreditAccountId.Value, dto.Amount, dto.Description, CostCenterID = dto.CostCenterID ?? 0 }, trans);

                    sourceJournalId = oldJournalId;
                    sourceIsPosted = true;
                }

                var hasFiscalYearColumn = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='FiscalYearID'",
                    transaction: trans) > 0;
                var fiscalYearAssignment = hasFiscalYearColumn ? "FiscalYearID=@FiscalYearID, " : string.Empty;
                var updateExpenseSql = $@"UPDATE dbo.OverheadExpenses SET
                            ExpenseDate=@ExpenseDate, PeriodID=@PeriodID, {fiscalYearAssignment}AccountID=@AccountID,
                            CreditAccountID=NULLIF(@CreditAccountID,0), CostCenterID=NULLIF(@CostCenterID,0),
                            Description=@Description, Amount=@Amount, Reference=@Reference,
                            CashBoxID=NULLIF(@CashBoxID,0), BankAccountID=NULLIF(@BankAccountID,0), PaymentMethod=@PaymentMethod,
                            Notes=@Notes, JournalID=@JournalID, IsPosted=@IsPosted
                          WHERE ExpenseID=@ExpenseID AND ISNULL(ExpenseStatus,1)=1";
                var updatedExpense = await connection.ExecuteAsync(updateExpenseSql, new
                {
                    dto.ExpenseDate,
                    dto.PeriodID,
                    dto.FiscalYearID,
                    dto.AccountID,
                    dto.CreditAccountID,
                    dto.CostCenterID,
                    dto.Description,
                    dto.Amount,
                    dto.Reference,
                    dto.CashBoxID,
                    dto.BankAccountID,
                    dto.PaymentMethod,
                    dto.Notes,
                    JournalID = sourceJournalId,
                    IsPosted = sourceIsPosted,
                    dto.ExpenseID
                }, trans);
                if (updatedExpense != 1) throw new Exception("لم يتم تحديث المصروف؛ ربما تغيرت حالته أثناء العملية.");

                if (sourceIsPosted && newCreditAccountId.HasValue)
                {
                    if (dto.CashBoxID.HasValue && dto.CashBoxID.Value > 0)
                    {
                        var affected = await connection.ExecuteAsync(
                            "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amount WHERE CashBoxID=@ID",
                            new { Amount = dto.Amount, ID = dto.CashBoxID.Value }, trans);
                        if (affected != 1) throw new Exception("تعذر تطبيق الأثر الجديد على الخزينة؛ أُلغيت العملية.");
                    }
                    if (dto.BankAccountID.HasValue && dto.BankAccountID.Value > 0)
                    {
                        var affected = await connection.ExecuteAsync(
                            "UPDATE dbo.BankAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amount WHERE BankAccountID=@ID",
                            new { Amount = dto.Amount, ID = dto.BankAccountID.Value }, trans);
                        if (affected != 1) throw new Exception("تعذر تطبيق الأثر الجديد على البنك؛ أُلغيت العملية.");
                    }

                    var newDeltas = new Dictionary<int, decimal>();
                    void AddNewDelta(int accountId, decimal delta)
                    {
                        if (newDeltas.ContainsKey(accountId)) newDeltas[accountId] += delta;
                        else newDeltas[accountId] = delta;
                    }
                    AddNewDelta(dto.AccountID, dto.Amount);
                    AddNewDelta(newCreditAccountId.Value, -dto.Amount);
                    foreach (var item in newDeltas.OrderBy(x => x.Key))
                        await ApplyAccountDeltaAsync(item.Key, item.Value);
                }

                // عند إزالة الحساب الدائن، يُعكس القيد القديم ويُزال القيد المسودة؛ لا يُنشأ أثر مالي بديل.
                if (hasOldJournal && !sourceIsPosted)
                {
                    await connection.ExecuteAsync("DELETE FROM dbo.JournalEntryDetails WHERE JournalID=@ID", new { ID = oldJournalId }, trans);
                    var deletedJournal = await connection.ExecuteAsync(
                        "DELETE FROM dbo.JournalEntries WHERE JournalID=@ID AND JournalStatus=1 AND IsAutoGenerated=1",
                        new { ID = oldJournalId }, trans);
                    if (deletedJournal != 1) throw new Exception("تعذر إزالة القيد الأوتوماتيكي المسودة بعد عكسه؛ أُلغيت العملية.");
                }

                trans.Commit();
                dto.JournalID = sourceJournalId;
                dto.IsPosted = sourceIsPosted;
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 2, "OverheadExpenses", dto.ExpenseID.ToString(),
                        moduleName: "SCR_EXPENSES", description: $"تعديل مصروف وعكس/إعادة تطبيق القيد: {dto.ExpenseNumber}");
                }
                catch { }
            }
            catch
            {
                try { trans.Rollback(); } catch { }
                throw;
            }
        }

        public async Task DeleteExpenseAsync(int id, int userId)
        {
            await RequireExpensePermissionAsync(
                userId, p => p.CanDelete, "ليس لديك صلاحية حذف المصروفات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var exp = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT ExpenseStatus, JournalID, Amount, AccountID, CreditAccountID, CashBoxID, BankAccountID FROM dbo.OverheadExpenses WITH (UPDLOCK, HOLDLOCK) WHERE ExpenseID=@ID",
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
            await RequireExpensePermissionAsync(
                userId, p => p.CanApprove, "ليس لديك صلاحية اعتماد المصروفات.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var expense = await connection.QueryFirstOrDefaultAsync<(
                    int? ExpenseStatus,
                    int? PeriodID,
                    DateTime? ExpenseDate,
                    int? JournalID,
                    bool IsPosted,
                    decimal Amount,
                    int AccountID,
                    int? CreditAccountID,
                    int? CashBoxID,
                    int? BankAccountID)>(
                    @"SELECT ISNULL(ExpenseStatus,1) AS ExpenseStatus, PeriodID, ExpenseDate,
                             JournalID, ISNULL(IsPosted,0) AS IsPosted, Amount,
                             AccountID, CreditAccountID, CashBoxID, BankAccountID
                      FROM dbo.OverheadExpenses WITH (UPDLOCK, HOLDLOCK)
                      WHERE ExpenseID=@ID",
                    new { ID = id }, trans);
                if (!expense.ExpenseStatus.HasValue) throw new Exception("المصروف غير موجود.");
                if (expense.ExpenseStatus.Value != 1) throw new Exception("لا يمكن اعتماد مصروف ليس مسودة.");
                if (!expense.PeriodID.HasValue || !expense.ExpenseDate.HasValue)
                    throw new Exception("لا يمكن اعتماد المصروف دون تاريخ وفترة مالية محددين.");

                var fiscalYearId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT FiscalYearID FROM dbo.AccountingPeriods WHERE PeriodID=@ID",
                    new { ID = expense.PeriodID.Value }, trans);
                if (!fiscalYearId.HasValue || fiscalYearId.Value <= 0)
                    throw new Exception("الفترة المحاسبية للمصروف غير موجودة أو غير مرتبطة بسنة.");

                var fiscalYear = await connection.QueryFirstOrDefaultAsync<(int YearStatus, DateTime StartDate, DateTime EndDate)>(
                    "SELECT YearStatus, StartDate, EndDate FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = fiscalYearId.Value }, trans);
                if (fiscalYear.YearStatus != 1)
                    throw new Exception("لا يمكن اعتماد مصروف داخل سنة مالية مغلقة أو غير موجودة.");

                var periodState = await connection.QueryFirstOrDefaultAsync<(int FiscalYearID, int PeriodStatus, DateTime StartDate, DateTime EndDate)>(
                    "SELECT FiscalYearID, PeriodStatus, StartDate, EndDate FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK) WHERE PeriodID=@ID",
                    new { ID = expense.PeriodID.Value }, trans);
                if (periodState.FiscalYearID != fiscalYearId.Value || periodState.PeriodStatus != 1)
                    throw new Exception("لا يمكن اعتماد مصروف داخل فترة مغلقة أو غير مرتبطة بسنتها.");
                if (expense.ExpenseDate.Value.Date < fiscalYear.StartDate.Date || expense.ExpenseDate.Value.Date > fiscalYear.EndDate.Date
                    || expense.ExpenseDate.Value.Date < periodState.StartDate.Date || expense.ExpenseDate.Value.Date > periodState.EndDate.Date)
                    throw new Exception("تاريخ المصروف خارج نطاق السنة أو الفترة المحاسبية المفتوحة.");

                if (expense.IsPosted && (!expense.JournalID.HasValue || expense.JournalID.Value <= 0))
                    throw new Exception("المصروف معلّم كمرحّل دون قيد مرتبط؛ لا يمكن اعتماده بأمان.");

                if (expense.JournalID.HasValue && expense.JournalID.Value > 0)
                {
                    if (!expense.IsPosted)
                        throw new Exception("المصروف مرتبط بقيد لكن حالة أثره غير مؤكدة؛ أُوقف الاعتماد لتجنب تكرار الأرصدة.");

                    var journal = await connection.QueryFirstOrDefaultAsync<(
                        int? JournalStatus,
                        bool IsAutoGenerated,
                        string? SourceDocType,
                        int? SourceDocID,
                        int? FiscalYearID,
                        int? PeriodID,
                        DateTime? JournalDate,
                        decimal? TotalDebit,
                        decimal? TotalCredit)>(
                        @"SELECT JournalStatus, ISNULL(IsAutoGenerated,0) AS IsAutoGenerated,
                                 SourceDocType, SourceDocID, FiscalYearID, PeriodID, JournalDate, TotalDebit, TotalCredit
                          FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                          WHERE JournalID=@ID",
                        new { ID = expense.JournalID.Value }, trans);
                    if (!journal.JournalStatus.HasValue
                        || journal.JournalStatus.Value != 1
                        || !journal.IsAutoGenerated
                        || !string.Equals(journal.SourceDocType, "OverheadExpense", StringComparison.OrdinalIgnoreCase)
                        || journal.SourceDocID != id
                        || journal.FiscalYearID != fiscalYearId.Value
                        || journal.PeriodID != expense.PeriodID.Value
                        || !journal.JournalDate.HasValue
                        || journal.JournalDate.Value.Date != expense.ExpenseDate.Value.Date
                        || !journal.TotalDebit.HasValue || journal.TotalDebit.Value != expense.Amount
                        || !journal.TotalCredit.HasValue || journal.TotalCredit.Value != expense.Amount)
                        throw new Exception("القيد الأوتوماتيكي لا يطابق المصروف أو حالته؛ أُوقف الاعتماد دون تغيير الأرصدة.");

                    var details = (await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit)>(
                        @"SELECT AccountID, DebitAmount AS Debit, CreditAmount AS Credit
                          FROM dbo.JournalEntryDetails WITH (UPDLOCK, HOLDLOCK)
                          WHERE JournalID=@ID ORDER BY LineNumber",
                        new { ID = expense.JournalID.Value }, trans)).ToList();
                    var debitLines = details.Where(x => x.Debit > 0m && x.Credit == 0m).ToList();
                    var creditLines = details.Where(x => x.Credit > 0m && x.Debit == 0m).ToList();
                    if (details.Count != 2 || debitLines.Count != 1 || creditLines.Count != 1
                        || details.Any(x => x.AccountID <= 0 || x.Debit < 0m || x.Credit < 0m)
                        || expense.AccountID <= 0 || debitLines[0].AccountID != expense.AccountID
                        || debitLines[0].Debit != expense.Amount
                        || creditLines[0].Credit != expense.Amount)
                        throw new Exception("تفاصيل القيد لا تطابق حساب ومبلغ المصروف؛ أُوقف الاعتماد دون تغيير الحالة أو الأرصدة.");

                    int? expectedCreditAccountId = expense.CreditAccountID.GetValueOrDefault() > 0
                        ? expense.CreditAccountID
                        : null;
                    if (!expectedCreditAccountId.HasValue && expense.CashBoxID.GetValueOrDefault() > 0)
                        expectedCreditAccountId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT AccountID FROM dbo.CashBoxes WITH (UPDLOCK, HOLDLOCK) WHERE CashBoxID=@ID",
                            new { ID = expense.CashBoxID!.Value }, trans);
                    if (!expectedCreditAccountId.HasValue && expense.BankAccountID.GetValueOrDefault() > 0)
                        expectedCreditAccountId = await connection.ExecuteScalarAsync<int?>(
                            "SELECT AccountID FROM dbo.BankAccounts WITH (UPDLOCK, HOLDLOCK) WHERE BankAccountID=@ID",
                            new { ID = expense.BankAccountID!.Value }, trans);
                    if (!expectedCreditAccountId.HasValue || expectedCreditAccountId.Value <= 0
                        || creditLines[0].AccountID != expectedCreditAccountId.Value)
                        throw new Exception("تعذر إثبات حساب الدائن من المصروف أو الخزينة/البنك المرتبط؛ أُوقف الاعتماد بأمان.");

                    // الأرصدة حُدّثت عند إنشاء المصروف أو عكسه/إعادة تطبيقه؛ لا تُحدّث ثانيةً عند الاعتماد.
                    var postedJournal = await connection.ExecuteAsync(
                        @"UPDATE dbo.JournalEntries
                          SET JournalStatus=2, PostedBy=@UserID, PostedDate=GETDATE()
                          WHERE JournalID=@ID AND JournalStatus=1 AND IsAutoGenerated=1",
                        new { ID = expense.JournalID.Value, UserID = userId }, trans);
                    if (postedJournal != 1)
                        throw new Exception("تعذر ترحيل القيد الأوتوماتيكي؛ أُلغيت عملية اعتماد المصروف.");
                }

                var approved = await connection.ExecuteAsync(
                    @"UPDATE dbo.OverheadExpenses
                      SET ExpenseStatus=2, ApprovedBy=@UserID, ApprovedDate=GETDATE()
                      WHERE ExpenseID=@ID AND ISNULL(ExpenseStatus,1)=1",
                    new { UserID = userId, ID = id }, trans);
                if (approved != 1) throw new Exception("تغيرت حالة المصروف أثناء الاعتماد؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); } catch { }
                throw;
            }

            try { await _audit.WriteAuditLogAsync(userId, 2, "OverheadExpenses", id.ToString(), moduleName: "SCR_EXPENSES", description: "اعتماد مصروف وترحيل قيده الأوتوماتيكي"); } catch { }
        }

        // ==========================================
        // تقارير محاسبية - vw_TrialBalance + vw_AccountLedger
        // ==========================================
        public async Task<List<TrialBalanceDto>> GetTrialBalanceAsync(DateTime? from = null, DateTime? to = null, int? level = null)
        {
            await _authorization.RequireAnyCurrentUserPermissionAsync(
                "ليس لديك صلاحية عرض ميزان المراجعة.",
                ("SCR_TRIAL", p => p.CanView),
                ("SCR_TRIALBAL", p => p.CanView),
                ("SCR_TB", p => p.CanView));

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
                                    ISNULL((SELECT SUM(DebitAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND je.JournalStatus=2 AND (@From IS NULL OR je.JournalDate>=@From) AND (@To IS NULL OR je.JournalDate<=@To)),0) AS TotalDebit,
                                    ISNULL((SELECT SUM(CreditAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND je.JournalStatus=2 AND (@From IS NULL OR je.JournalDate>=@From) AND (@To IS NULL OR je.JournalDate<=@To)),0) AS TotalCredit,
                                    ISNULL(coa.OpeningBalance,0) + ISNULL((SELECT SUM(DebitAmount - CreditAmount) FROM dbo.JournalEntryDetails jd JOIN dbo.JournalEntries je ON jd.JournalID=je.JournalID WHERE jd.AccountID=coa.AccountID AND je.JournalStatus=2 AND (@From IS NULL OR je.JournalDate>=@From) AND (@To IS NULL OR je.JournalDate<=@To)),0) AS CurrentBalance
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
            await _authorization.RequireAnyCurrentUserPermissionAsync(
                "ليس لديك صلاحية عرض دفتر الأستاذ.",
                ("SCR_LEDGER", p => p.CanView),
                ("SCR_ACCLEDGER", p => p.CanView),
                ("SCR_ACCOUNT_LEDGER", p => p.CanView));

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
