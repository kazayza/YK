using Dapper;
using Microsoft.Data.SqlClient;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class AccountingService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly ModuleAuthorizationService _authorization;
        private readonly ILogger<AccountingService> _logger;

        public AccountingService(
            IConfiguration configuration,
            AuditService audit,
            ModuleAuthorizationService authorization,
            ILogger<AccountingService> logger) : base(configuration)
        {
            _audit = audit;
            _authorization = authorization;
            _logger = logger;
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

        private static void AggregateAccountBalances(IReadOnlyCollection<ChartOfAccountDto> accounts)
        {
            var byId = accounts.ToDictionary(account => account.AccountID);
            var directBalances = accounts.ToDictionary(account => account.AccountID, account => account.CurrentBalance);

            foreach (var account in accounts)
            {
                if (!account.IsActive) continue;
                var balance = directBalances[account.AccountID];
                var parentId = account.ParentAccountID;
                var visited = new HashSet<int> { account.AccountID };

                while (parentId is int id && id > 0)
                {
                    if (!visited.Add(id))
                        throw new InvalidOperationException("توجد دورة في شجرة الحسابات؛ تعذر تجميع الأرصدة بأمان.");
                    if (!byId.TryGetValue(id, out var parent) || !parent.IsActive) break;

                    parent.CurrentBalance += balance;
                    parentId = parent.ParentAccountID;
                }
            }
        }

        // ==========================================
        // شجرة الحسابات - على الجدول الحقيقي ChartOfAccounts
        // ==========================================
        // حساب الأرصدة الفعلية من القيود - الرصيد المعروض يتبع الطبيعة: مدين للحساب المدين ودائن للحساب الدائن
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
                    WHERE je.JournalStatus = 2
                    GROUP BY jd.AccountID
                )
                SELECT coa.AccountID, coa.AccountCode, coa.AccountNameAr, coa.AccountNameEn, coa.ParentAccountID, p.AccountNameAr AS ParentAccountNameAr,
                       coa.AccountLevel, coa.AccountType,
                       CASE coa.AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                       coa.AccountNature, coa.IsDetailAccount, coa.CurrencyID, coa.CostCenterRequired, 
                       ISNULL(coa.OpeningBalance,0) AS OpeningBalance,
                       CASE WHEN ISNULL(coa.AccountNature,1)=2
                            THEN ISNULL(coa.OpeningBalance,0) + ISNULL(js.TotalCredit,0) - ISNULL(js.TotalDebit,0)
                            ELSE ISNULL(coa.OpeningBalance,0) + ISNULL(js.TotalDebit,0) - ISNULL(js.TotalCredit,0)
                       END AS CurrentBalance,
                       coa.LinkedEntityType, coa.LinkedEntityID, coa.IsSystemAccount, coa.IsCashFlowAccount, coa.SortOrder, coa.Description, coa.IsActive,
                       (SELECT COUNT(*) FROM dbo.ChartOfAccounts c WHERE c.ParentAccountID=coa.AccountID) AS ChildrenCount
                FROM dbo.ChartOfAccounts coa
                LEFT JOIN dbo.ChartOfAccounts p ON coa.ParentAccountID=p.AccountID
                LEFT JOIN JournalSums js ON js.AccountID = coa.AccountID
                ORDER BY coa.AccountCode";
                var result = await connection.QueryAsync<ChartOfAccountDto>(sql);
                var all = result.ToList();

                AggregateAccountBalances(all);
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
            catch
            {
                // Fallback بسيط بدون CTE لو فشل
                var sql2 = @"SELECT coa.AccountID, coa.AccountCode, coa.AccountNameAr, coa.AccountNameEn, coa.ParentAccountID, p.AccountNameAr AS ParentAccountNameAr,
                               coa.AccountLevel, coa.AccountType,
                               CASE coa.AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                               coa.AccountNature, coa.IsDetailAccount, coa.CurrencyID, coa.CostCenterRequired, coa.OpeningBalance,
                               CASE WHEN ISNULL(coa.AccountNature,1)=2
                                    THEN (2*ISNULL(coa.OpeningBalance,0))-ISNULL(coa.CurrentBalance,ISNULL(coa.OpeningBalance,0))
                                    ELSE ISNULL(coa.CurrentBalance,ISNULL(coa.OpeningBalance,0))
                               END AS CurrentBalance,
                               coa.LinkedEntityType, coa.LinkedEntityID, coa.IsSystemAccount, coa.IsCashFlowAccount, coa.SortOrder, coa.Description, coa.IsActive,
                               (SELECT COUNT(*) FROM dbo.ChartOfAccounts c WHERE c.ParentAccountID=coa.AccountID) AS ChildrenCount
                        FROM dbo.ChartOfAccounts coa
                        LEFT JOIN dbo.ChartOfAccounts p ON coa.ParentAccountID=p.AccountID
                        ORDER BY coa.AccountCode";
                var result = await connection.QueryAsync<ChartOfAccountDto>(sql2);
                var all = result.ToList();
                AggregateAccountBalances(all);
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
                    WHERE je.JournalStatus = 2
                    GROUP BY jd.AccountID
                )
                SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType,
                       CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                       AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, 
                       ISNULL(OpeningBalance,0) AS OpeningBalance,
                       CASE WHEN ISNULL(coa.AccountNature,1)=2
                            THEN ISNULL(coa.OpeningBalance,0) + ISNULL(js.TotalCredit,0) - ISNULL(js.TotalDebit,0)
                            ELSE ISNULL(coa.OpeningBalance,0) + ISNULL(js.TotalDebit,0) - ISNULL(js.TotalCredit,0)
                       END AS CurrentBalance,
                       LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, 0 AS ChildrenCount
                FROM dbo.ChartOfAccounts coa
                LEFT JOIN JournalSums js ON js.AccountID = coa.AccountID
                WHERE IsActive=1 ORDER BY AccountCode";
                var result = (await connection.QueryAsync<ChartOfAccountDto>(sql)).ToList();
                AggregateAccountBalances(result);
                return result;
            }
            catch
            {
                // Fallback بدون CTE وبدون IsActive لو العمود مش موجود
                try
                {
                    var sql2 = @"SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType,
                               CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                               AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, OpeningBalance,
                               CASE WHEN ISNULL(AccountNature,1)=2
                                    THEN (2*ISNULL(OpeningBalance,0))-ISNULL(CurrentBalance,ISNULL(OpeningBalance,0))
                                    ELSE ISNULL(CurrentBalance,ISNULL(OpeningBalance,0))
                               END AS CurrentBalance,
                               LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, 0 AS ChildrenCount
                        FROM dbo.ChartOfAccounts WHERE IsActive=1 ORDER BY AccountCode";
                    var result = (await connection.QueryAsync<ChartOfAccountDto>(sql2)).ToList();
                    AggregateAccountBalances(result);
                    return result;
                }
                catch
                {
                    var sql3 = @"SELECT AccountID, AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType,
                               CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية' WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                               AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, OpeningBalance,
                               CASE WHEN ISNULL(AccountNature,1)=2
                                    THEN (2*ISNULL(OpeningBalance,0))-ISNULL(CurrentBalance,ISNULL(OpeningBalance,0))
                                    ELSE ISNULL(CurrentBalance,ISNULL(OpeningBalance,0))
                               END AS CurrentBalance,
                               LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, 1 AS IsActive, 0 AS ChildrenCount
                        FROM dbo.ChartOfAccounts ORDER BY AccountCode";
                    var result = (await connection.QueryAsync<ChartOfAccountDto>(sql3)).ToList();
                    AggregateAccountBalances(result);
                    return result;
                }
            }
        }

        public async Task<List<ChartOfAccountDto>> GetExpenseAccountLookupsAsync()
        {
            await RequireCurrentExpensePermissionAsync(
                p => p.CanView || p.CanAdd || p.CanEdit,
                "ليس لديك صلاحية استخدام قوائم حسابات المصروفات.");

            return await GetAccountSelectionLookupsCoreAsync(logPrimaryReadFailure: true);
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

        private async Task<List<ChartOfAccountDto>> GetAccountSelectionLookupsCoreAsync(bool logPrimaryReadFailure = false)
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
            catch (Exception primaryException)
            {
                if (logPrimaryReadFailure)
                    _logger.LogWarning(primaryException,
                        "The active account selection lookup failed; attempting the compatibility fallback.");

                try
                {
                    var sql = @"SELECT AccountID, AccountCode, AccountNameAr, AccountType,
                                  CASE AccountType WHEN 1 THEN N'أصول' WHEN 2 THEN N'التزامات' WHEN 3 THEN N'حقوق ملكية'
                                                   WHEN 4 THEN N'إيرادات' WHEN 5 THEN N'تكلفة' WHEN 6 THEN N'مصروفات' ELSE N'غير محدد' END AS AccountTypeName,
                                  IsDetailAccount, 1 AS IsActive
                            FROM dbo.ChartOfAccounts ORDER BY AccountCode";
                    var result = await connection.QueryAsync<ChartOfAccountDto>(sql);
                    return result.ToList();
                }
                catch (Exception fallbackException)
                {
                    if (logPrimaryReadFailure)
                        _logger.LogError(fallbackException,
                            "The compatibility fallback for account selection lookups failed.");
                    throw;
                }
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
            if (string.IsNullOrWhiteSpace(dto.AccountNameAr))
                throw new InvalidOperationException("اسم الحساب العربي مطلوب.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            int newId;
            try
            {
                var parentAccountId = dto.ParentAccountID.GetValueOrDefault();
                if (parentAccountId <= 0) dto.ParentAccountID = null;

                var parentLevel = 0;
                if (parentAccountId > 0)
                {
                    var parentLevelValue = await connection.ExecuteScalarAsync<int?>(
                        "SELECT ISNULL(AccountLevel,1) FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountID=@ID",
                        new { ID = parentAccountId }, trans);
                    if (!parentLevelValue.HasValue)
                        throw new InvalidOperationException("الحساب الأب غير موجود.");
                    parentLevel = parentLevelValue.Value;
                }

                dto.AccountLevel = parentLevel + 1;
                var generateCode = string.IsNullOrWhiteSpace(dto.AccountCode);
                if (generateCode)
                {
                    var prefix = parentAccountId > 0
                        ? await connection.QueryFirstOrDefaultAsync<string>(
                            "SELECT AccountCode FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountID=@ID",
                            new { ID = parentAccountId }, trans)
                        : "1";
                    if (string.IsNullOrWhiteSpace(prefix))
                        throw new InvalidOperationException("تعذر توليد كود الحساب لأن الحساب الأب غير موجود أو بلا كود.");

                    var nextSuffix = parentAccountId > 0
                        ? await connection.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*)+1 FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE ParentAccountID=@PID",
                            new { PID = parentAccountId }, trans)
                        : await connection.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*)+1 FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE ParentAccountID IS NULL",
                            transaction: trans);

                    while (true)
                    {
                        var candidate = $"{prefix}{nextSuffix:00}";
                        var codeExists = await connection.ExecuteScalarAsync<int>(
                            "SELECT COUNT(1) FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountCode=@Code",
                            new { Code = candidate }, trans);
                        if (codeExists == 0)
                        {
                            dto.AccountCode = candidate;
                            break;
                        }
                        if (nextSuffix == int.MaxValue)
                            throw new InvalidOperationException("تعذر إيجاد كود حساب متاح.");
                        nextSuffix++;
                    }
                }
                else
                {
                    dto.AccountCode = dto.AccountCode!.Trim();
                }

                var duplicateCode = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountCode=@Code",
                    new { Code = dto.AccountCode }, trans);
                if (duplicateCode > 0)
                    throw new InvalidOperationException("كود الحساب مستخدم بالفعل.");

                var sql = @"INSERT INTO dbo.ChartOfAccounts(AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel, AccountType, AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired, OpeningBalance, CurrentBalance, LinkedEntityType, LinkedEntityID, IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, CreatedBy, CreatedDate)
                            VALUES(@AccountCode, @AccountNameAr, @AccountNameEn, NULLIF(@ParentAccountID,0), @AccountLevel, @AccountType, @AccountNature, @IsDetailAccount, NULLIF(@CurrencyID,0), @CostCenterRequired, @OpeningBalance, @OpeningBalance, @LinkedEntityType, @LinkedEntityID, @IsSystemAccount, @IsCashFlowAccount, @SortOrder, @Description, @IsActive, @UserID, GETDATE());
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                newId = await connection.ExecuteScalarAsync<int>(sql, new
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
                }, trans);
                if (newId <= 0) throw new InvalidOperationException("لم يتم إنشاء الحساب.");
                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after creating chart of accounts account {AccountCode} for user {UserID}.",
                        dto.AccountCode,
                        userId);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 1, "ChartOfAccounts", newId.ToString(), moduleName: "SCR_COA", description: $"إنشاء حساب: {dto.AccountCode} - {dto.AccountNameAr}");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after creating chart-of-accounts entry {AccountID}.",
                    newId);
            }
            return newId;
        }

        public async Task UpdateAccountAsync(ChartOfAccountDto dto, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_COA", p => p.CanEdit, "ليس لديك صلاحية تعديل الحسابات.");
            if (dto.AccountID <= 0) throw new ArgumentOutOfRangeException(nameof(dto.AccountID));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<(
                    int? AccountID, string? AccountCode, int? ParentAccountID, decimal? OpeningBalance, bool IsSystemAccount)>(
                    @"SELECT AccountID, AccountCode, ParentAccountID, OpeningBalance,
                             ISNULL(IsSystemAccount,0) AS IsSystemAccount
                      FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountID=@ID",
                    new { ID = dto.AccountID }, trans);
                if (!current.AccountID.HasValue)
                    throw new InvalidOperationException("الحساب غير موجود أو تم حذفه.");

                var requestedParentId = dto.ParentAccountID.GetValueOrDefault();
                var currentParentId = current.ParentAccountID.GetValueOrDefault();
                var accountCodeChanged = !string.Equals(
                    dto.AccountCode?.Trim(), current.AccountCode?.Trim(), StringComparison.OrdinalIgnoreCase);
                if (accountCodeChanged || requestedParentId != currentParentId
                    || dto.OpeningBalance != current.OpeningBalance.GetValueOrDefault()
                    || dto.IsSystemAccount != current.IsSystemAccount)
                    throw new InvalidOperationException(
                        "كود الحساب والأب والرصيد الافتتاحي وحالة الحساب النظامي لا تُعدّل من نافذة التعديل العامة؛ لم تُحفظ هذه التغييرات.");

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.ChartOfAccounts SET
                        AccountNameAr=@AccountNameAr, AccountNameEn=@AccountNameEn,
                        AccountType=@AccountType, AccountNature=@AccountNature,
                        IsDetailAccount=@IsDetailAccount, CurrencyID=NULLIF(@CurrencyID,0),
                        CostCenterRequired=@CostCenterRequired, LinkedEntityType=@LinkedEntityType,
                        LinkedEntityID=@LinkedEntityID, SortOrder=@SortOrder, Description=@Description,
                        IsActive=@IsActive, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                      WHERE AccountID=@AccountID",
                    new
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
                        dto.SortOrder,
                        dto.Description,
                        dto.IsActive,
                        UserID = userId
                    }, trans);
                if (updated != 1)
                    throw new InvalidOperationException("لم يتم تحديث الحساب؛ أُلغيت العملية.");

                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after updating chart of accounts account {AccountID} failed.",
                        dto.AccountID);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 2, "ChartOfAccounts", dto.AccountID.ToString(), moduleName: "SCR_COA", description: $"تعديل حساب: {dto.AccountCode}");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after updating chart-of-accounts entry {AccountID}.",
                    dto.AccountID);
            }
        }

        public async Task DeleteAccountAsync(int id, int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_COA", p => p.CanDelete, "ليس لديك صلاحية حذف الحسابات.");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var account = await connection.QueryFirstOrDefaultAsync<(int? AccountID, bool IsSystemAccount)>(
                    @"SELECT AccountID, ISNULL(IsSystemAccount,0) AS IsSystemAccount
                      FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountID=@ID",
                    new { ID = id }, trans);
                if (!account.AccountID.HasValue) throw new InvalidOperationException("الحساب غير موجود.");
                if (account.IsSystemAccount)
                    throw new InvalidOperationException("لا يمكن حذف حساب نظامي.");

                var hasChildren = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM dbo.ChartOfAccounts WITH (UPDLOCK,HOLDLOCK) WHERE ParentAccountID=@ID",
                    new { ID = id }, trans);
                if (hasChildren > 0) throw new Exception("لا يمكن حذف حساب له حسابات فرعية");

                var hasTrans = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM dbo.JournalEntryDetails WITH (UPDLOCK,HOLDLOCK) WHERE AccountID=@ID",
                    new { ID = id }, trans);
                if (hasTrans > 0) throw new Exception("لا يمكن حذف حساب به حركات محاسبية");

                var deleted = await connection.ExecuteAsync(
                    "DELETE FROM dbo.ChartOfAccounts WHERE AccountID=@ID AND ISNULL(IsSystemAccount,0)=0",
                    new { ID = id }, trans);
                if (deleted != 1) throw new InvalidOperationException("تغيرت بيانات الحساب أثناء الحذف؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after deleting account {AccountID} failed.",
                        id);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 3, "ChartOfAccounts", id.ToString(), moduleName: "SCR_COA", description: "حذف حساب");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after deleting chart-of-accounts entry {AccountID}.",
                    id);
            }
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

        private async Task<string> GenerateJournalNoCoreAsync(SqlConnection connection, SqlTransaction transaction)
        {
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql, transaction: transaction);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لقيد اليومية.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم قيد اليومية داخل معاملة الحفظ؛ أُلغيت المعاملة.", ex);
            }
        }

        private static List<JournalDetailDto> GetEffectiveJournalDetails(IEnumerable<JournalDetailDto>? details)
        {
            var source = (details ?? Enumerable.Empty<JournalDetailDto>()).ToList();
            if (source.Any(d => d.DebitAmount < 0 || d.CreditAmount < 0))
                throw new InvalidOperationException("لا يمكن أن تكون مبالغ القيد سالبة.");
            if (source.Any(d => (d.DebitAmount > 0 || d.CreditAmount > 0) && d.AccountID <= 0))
                throw new InvalidOperationException("اختر حسابًا لكل سطر يحتوي على مبلغ.");

            var validDetails = source
                .Where(d => d.AccountID > 0 && (d.DebitAmount > 0 || d.CreditAmount > 0))
                .ToList();
            if (validDetails.Count < 2)
                throw new InvalidOperationException("يجب إدخال حسابين على الأقل بمبالغ.");
            if (validDetails.Any(d => d.DebitAmount > 0 && d.CreditAmount > 0))
                throw new InvalidOperationException("لا يمكن أن يكون السطر مدينًا ودائنًا معًا.");
            if (validDetails.Any(d => d.CostCenterID.GetValueOrDefault() < 0))
                throw new InvalidOperationException("مركز التكلفة المحدد غير صالح.");

            return validDetails;
        }

        private async Task ValidateJournalAccountLinesAsync(
            SqlConnection connection, SqlTransaction transaction, IReadOnlyCollection<JournalDetailDto> details)
        {
            var accountIds = details.Select(d => d.AccountID).Distinct().ToArray();
            var accounts = await connection.QueryAsync<ChartOfAccountDto>(
                @"SELECT AccountID, CONVERT(bit,ISNULL(IsActive,1)) AS IsActive,
                         CONVERT(bit,ISNULL(IsDetailAccount,0)) AS IsDetailAccount,
                         CONVERT(bit,ISNULL(CostCenterRequired,0)) AS CostCenterRequired
                  FROM dbo.ChartOfAccounts WITH (UPDLOCK, HOLDLOCK)
                  WHERE AccountID IN @AccountIDs",
                new { AccountIDs = accountIds }, transaction);
            var accountById = accounts.ToDictionary(a => a.AccountID);

            foreach (var detail in details)
            {
                if (!accountById.TryGetValue(detail.AccountID, out var account))
                    throw new InvalidOperationException($"الحساب {detail.AccountID} غير موجود.");
                if (!account.IsActive)
                    throw new InvalidOperationException($"الحساب {detail.AccountID} غير نشط.");
                if (!account.IsDetailAccount)
                    throw new InvalidOperationException($"الحساب {detail.AccountID} تجميعي؛ اختر حسابًا تفصيليًا.");
                if (account.CostCenterRequired && detail.CostCenterID.GetValueOrDefault() <= 0)
                    throw new InvalidOperationException($"الحساب {detail.AccountID} يتطلب اختيار مركز تكلفة.");
            }

            var costCenterIds = details
                .Select(d => d.CostCenterID.GetValueOrDefault())
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
            if (costCenterIds.Length == 0) return;

            var existingCostCenterIds = await connection.QueryAsync<int>(
                @"SELECT CostCenterID
                  FROM dbo.CostCenters WITH (UPDLOCK, HOLDLOCK)
                  WHERE CostCenterID IN @CostCenterIDs
                    AND ISNULL(IsActive,1)=1",
                new { CostCenterIDs = costCenterIds }, transaction);
            var existingCostCenterSet = existingCostCenterIds.ToHashSet();
            foreach (var costCenterId in costCenterIds)
                if (!existingCostCenterSet.Contains(costCenterId))
                    throw new InvalidOperationException($"مركز التكلفة {costCenterId} غير موجود أو غير نشط.");
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
                // تحقّق من السطور والحسابات ومراكز التكلفة قبل استهلاك رقم القيد.
                var validDetails = GetEffectiveJournalDetails(dto.Details);
                dto.TotalDebit = validDetails.Sum(d => d.DebitAmount);
                dto.TotalCredit = validDetails.Sum(d => d.CreditAmount);
                if (Math.Abs(dto.TotalDebit - dto.TotalCredit) > 0.01m)
                    throw new InvalidOperationException($"القيد غير متوازن - مدين: {dto.TotalDebit:#,##0.00} ≠ دائن: {dto.TotalCredit:#,##0.00}");
                if (dto.TotalDebit == 0)
                    throw new InvalidOperationException("لا يمكن حفظ قيد صفري.");
                await ValidateJournalAccountLinesAsync(connection, trans, validDetails);

                // لا تحفظ قيداً بلا سنة وفترة مفتوحتين تغطيان تاريخه؛ فذلك يجعل ترحيله مستحيلاً لاحقاً.
                var accountingPeriod = await connection.QueryFirstOrDefaultAsync<(
                    int? FiscalYearID, int? PeriodID, int? YearStatus, int? PeriodStatus,
                    DateTime? YearStart, DateTime? YearEnd, DateTime? PeriodStart, DateTime? PeriodEnd)>(
                    @"SELECT TOP 1 fy.FiscalYearID, ap.PeriodID, fy.YearStatus, ap.PeriodStatus,
                             fy.StartDate AS YearStart, fy.EndDate AS YearEnd,
                             ap.StartDate AS PeriodStart, ap.EndDate AS PeriodEnd
                      FROM dbo.FiscalYears fy WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK, HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
                      WHERE fy.YearStatus=1 AND ap.PeriodStatus=1
                        AND @JDate BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
                        AND @JDate BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
                      ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
                    new { JDate = dto.JournalDate.Date }, trans);
                if (!accountingPeriod.FiscalYearID.HasValue || !accountingPeriod.PeriodID.HasValue
                    || !accountingPeriod.YearStatus.HasValue || accountingPeriod.YearStatus.Value != 1
                    || !accountingPeriod.PeriodStatus.HasValue || accountingPeriod.PeriodStatus.Value != 1
                    || !accountingPeriod.YearStart.HasValue || !accountingPeriod.YearEnd.HasValue
                    || !accountingPeriod.PeriodStart.HasValue || !accountingPeriod.PeriodEnd.HasValue)
                    throw new InvalidOperationException("لا يمكن حفظ القيد دون سنة وفترة محاسبية مفتوحتين تغطيان تاريخ القيد.");
                var fiscalYearId = accountingPeriod.FiscalYearID;
                var periodId = accountingPeriod.PeriodID;

                int? srcIdInt = null;
                if (!string.IsNullOrWhiteSpace(dto.SourceDocID) && int.TryParse(dto.SourceDocID, out var parsedSrc)) srcIdInt = parsedSrc;

                if (string.IsNullOrWhiteSpace(dto.JournalNumber))
                    dto.JournalNumber = await GenerateJournalNoCoreAsync(connection, trans);

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
                    await connection.ExecuteAsync(
                        "INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID) VALUES(@JID, @Line, @AccID, @Debit, @Credit, @Desc, NULLIF(@CCID,0))",
                        new { JID = jId, Line = line++, AccID = d.AccountID, Debit = d.DebitAmount, Credit = d.CreditAmount, Desc = d.Description, CCID = d.CostCenterID ?? 0 }, trans);
                }

                trans.Commit();
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 1, "JournalEntries", jId.ToString(), moduleName: "SCR_JV", description: $"إنشاء قيد: {dto.JournalNumber}");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after creating journal entry for record {RecordID}.",
                        jId);
                }
                return jId;
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after creating manual journal {JournalNumber} for user {UserID}.",
                        dto.JournalNumber,
                        userId);
                }
                throw;
            }
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

                var validDetails = GetEffectiveJournalDetails(dto.Details);
                dto.TotalDebit = validDetails.Sum(d => d.DebitAmount);
                dto.TotalCredit = validDetails.Sum(d => d.CreditAmount);
                if (Math.Abs(dto.TotalDebit - dto.TotalCredit) > 0.01m)
                    throw new Exception($"القيد غير متوازن - مدين: {dto.TotalDebit:#,##0.00} ≠ دائن: {dto.TotalCredit:#,##0.00}");
                if (dto.TotalDebit <= 0)
                    throw new Exception("لا يمكن حفظ قيد صفري.");
                await ValidateJournalAccountLinesAsync(connection, trans, validDetails);

                var accountingPeriod = await connection.QueryFirstOrDefaultAsync<(
                    int? FiscalYearID, int? PeriodID, int? YearStatus, int? PeriodStatus,
                    DateTime? YearStart, DateTime? YearEnd, DateTime? PeriodStart, DateTime? PeriodEnd)>(
                    @"SELECT TOP 1 fy.FiscalYearID, ap.PeriodID, fy.YearStatus, ap.PeriodStatus,
                             fy.StartDate AS YearStart, fy.EndDate AS YearEnd,
                             ap.StartDate AS PeriodStart, ap.EndDate AS PeriodEnd
                      FROM dbo.FiscalYears fy WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.AccountingPeriods ap WITH (UPDLOCK, HOLDLOCK) ON ap.FiscalYearID=fy.FiscalYearID
                      WHERE fy.YearStatus=1 AND ap.PeriodStatus=1
                        AND @JDate BETWEEN CAST(fy.StartDate AS date) AND CAST(fy.EndDate AS date)
                        AND @JDate BETWEEN CAST(ap.StartDate AS date) AND CAST(ap.EndDate AS date)
                      ORDER BY fy.FiscalYearID DESC, ap.PeriodNumber",
                    new { JDate = dto.JournalDate.Date }, trans);
                if (!accountingPeriod.FiscalYearID.HasValue || !accountingPeriod.PeriodID.HasValue
                    || !accountingPeriod.YearStatus.HasValue || accountingPeriod.YearStatus.Value != 1
                    || !accountingPeriod.PeriodStatus.HasValue || accountingPeriod.PeriodStatus.Value != 1
                    || !accountingPeriod.YearStart.HasValue || !accountingPeriod.YearEnd.HasValue
                    || !accountingPeriod.PeriodStart.HasValue || !accountingPeriod.PeriodEnd.HasValue)
                    throw new InvalidOperationException("لا يمكن حفظ القيد دون سنة وفترة محاسبية مفتوحتين تغطيان تاريخ القيد.");
                var fiscalYearId = accountingPeriod.FiscalYearID;
                var periodId = accountingPeriod.PeriodID;

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
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 2, "JournalEntries", dto.JournalID.ToString(), moduleName: "SCR_JV", description: $"تعديل قيد: {dto.JournalNumber}");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after updating journal entry for record {RecordID}.",
                        dto.JournalID);
                }
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after updating manual journal {JournalID} failed.",
                        dto.JournalID);
                }
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
                    DateTime JournalDate,
                    decimal TotalDebit,
                    decimal TotalCredit)>(
                    "SELECT JournalStatus, ISNULL(IsAutoGenerated,1) AS IsAutoGenerated, FiscalYearID, PeriodID, JournalDate, TotalDebit, TotalCredit FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK) WHERE JournalID=@ID",
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

                // أعد فحص التفاصيل والحسابات قبل تحديث الأرصدة وترحيل القيد.
                var storedDetails = await connection.QueryAsync<JournalDetailDto>(
                    "SELECT AccountID, DebitAmount, CreditAmount, CostCenterID FROM dbo.JournalEntryDetails WHERE JournalID=@ID",
                    new { ID = id }, trans);
                var details = GetEffectiveJournalDetails(storedDetails);
                var detailDebit = details.Sum(d => d.DebitAmount);
                var detailCredit = details.Sum(d => d.CreditAmount);
                if (Math.Abs(detailDebit - detailCredit) > 0.01m || detailDebit <= 0
                    || Math.Abs(detailDebit - journal.TotalDebit) > 0.01m
                    || Math.Abs(detailCredit - journal.TotalCredit) > 0.01m)
                    throw new InvalidOperationException("إجمالي رأس القيد لا يطابق تفاصيله أو أن القيد غير متوازن.");
                await ValidateJournalAccountLinesAsync(connection, trans, details);

                foreach (var d in details)
                {
                    var delta = d.DebitAmount - d.CreditAmount; // مدين + ، دائن -
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

                var affected = await connection.ExecuteAsync(
                    "UPDATE dbo.JournalEntries SET JournalStatus=2, PostedBy=@UserID, PostedDate=GETDATE() WHERE JournalID=@ID AND JournalStatus=1",
                    new { UserID = userId, ID = id }, trans);
                if (affected != 1)
                    throw new InvalidOperationException("تعذر ترحيل القيد؛ ربما تغيرت حالته أثناء العملية.");

                trans.Commit();
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 2, "JournalEntries", id.ToString(), moduleName: "SCR_JV", description: "ترحيل قيد محاسبي وتحديث الأرصدة");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after posting journal entry for record {RecordID}.",
                        id);
                }
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after posting journal {JournalID} failed.",
                        id);
                }
                throw;
            }
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
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 3, "JournalEntries", id.ToString(), moduleName: "SCR_JV", description: "حذف قيد محاسبي");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after deleting journal entry for record {RecordID}.",
                        id);
                }
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after deleting manual journal {JournalID} failed.",
                        id);
                }
                throw;
            }
        }

        // ==========================================
        // السنوات المالية - على الجدول الحقيقي FiscalYears 11 عمود - بدون IsActive
        // ==========================================
        public async Task<List<FiscalYearListDto>> GetFiscalYearsAsync()
        {
            return await GetFiscalYearsCoreAsync(rethrowFinalReadFailure: false);
        }

        public async Task<List<FiscalYearListDto>> GetFiscalYearsForManagementPageAsync()
        {
            return await GetFiscalYearsCoreAsync(rethrowFinalReadFailure: true);
        }

        private async Task<List<FiscalYearListDto>> GetFiscalYearsCoreAsync(bool rethrowFinalReadFailure)
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
            catch (Exception primaryException)
            {
                if (rethrowFinalReadFailure)
                    _logger.LogWarning(primaryException,
                        "The full fiscal-year lookup failed; attempting the compatibility fallback.");

                try
                {
                    var sql2 = @"SELECT fy.FiscalYearID, fy.YearCode, fy.YearName, fy.StartDate, fy.EndDate, fy.YearStatus,
                               CASE fy.YearStatus WHEN 1 THEN N'مفتوحة' WHEN 2 THEN N'مغلقة' ELSE N'غير محدد' END AS StatusName,
                               fy.IsCurrent, fy.ClosedBy, fy.ClosedDate, fy.CreatedDate,
                               (SELECT COUNT(*) FROM dbo.AccountingPeriods ap WHERE ap.FiscalYearID=fy.FiscalYearID) AS PeriodsCount,
                               0 AS JournalsCount
                        FROM dbo.FiscalYears fy ORDER BY fy.StartDate DESC";
                    var result = await connection.QueryAsync<FiscalYearListDto>(sql2);
                    return result.ToList();
                }
                catch
                {
                    if (rethrowFinalReadFailure)
                        throw;

                    return new List<FiscalYearListDto>();
                }
            }
        }

        public async Task<List<FiscalYearListDto>> GetFiscalYearsForExpenseEditAsync()
        {
            return await GetFiscalYearsCoreAsync(rethrowFinalReadFailure: true);
        }

        public async Task<FiscalYearDto?> GetFiscalYearByIdAsync(int id)
        {
            return await GetFiscalYearByIdCoreAsync(id, logPrimaryReadFailure: false);
        }

        public async Task<FiscalYearDto?> GetFiscalYearByIdForManagementPageAsync(int id)
        {
            return await GetFiscalYearByIdCoreAsync(id, logPrimaryReadFailure: true);
        }

        private async Task<FiscalYearDto?> GetFiscalYearByIdCoreAsync(int id, bool logPrimaryReadFailure)
        {
            await RequireCurrentFiscalPermissionAsync(
                p => p.CanView, "ليس لديك صلاحية عرض تفاصيل السنة المالية.");

            using var connection = CreateConnection();
            try
            {
                return await connection.QueryFirstOrDefaultAsync<FiscalYearDto>(
                    "SELECT FiscalYearID, YearCode, YearName, StartDate, EndDate, YearStatus, IsCurrent, Notes, CreatedDate FROM dbo.FiscalYears WHERE FiscalYearID=@ID",
                    new { ID = id });
            }
            catch (Exception primaryException)
            {
                if (logPrimaryReadFailure)
                    _logger.LogWarning(primaryException,
                        "The full fiscal-year detail lookup failed; attempting the compatibility fallback for {FiscalYearID}.",
                        id);

                return await connection.QueryFirstOrDefaultAsync<FiscalYearDto>(
                    "SELECT FiscalYearID, YearCode, YearName, StartDate, EndDate, 1 AS YearStatus, IsCurrent, '' AS Notes, GETDATE() AS CreatedDate FROM dbo.FiscalYears WHERE FiscalYearID=@ID",
                    new { ID = id });
            }
        }

        private static async Task EnsureFiscalYearCodeAvailableAsync(
            SqlConnection connection, SqlTransaction transaction, string? yearCode, int? excludeFiscalYearId = null)
        {
            if (string.IsNullOrWhiteSpace(yearCode)) return;

            var duplicateCount = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(1)
                  FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK)
                  WHERE YearCode=@YearCode
                    AND (@ExcludeFiscalYearID IS NULL OR FiscalYearID<>@ExcludeFiscalYearID)",
                new { YearCode = yearCode, ExcludeFiscalYearID = excludeFiscalYearId }, transaction);
            if (duplicateCount > 0)
                throw new InvalidOperationException($"كود السنة المالية '{yearCode}' مستخدم بالفعل.");
        }

        public async Task<int> InsertFiscalYearAsync(FiscalYearDto dto, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanAdd, "ليس لديك صلاحية إنشاء سنة مالية.");

            if (string.IsNullOrWhiteSpace(dto.YearName))
                throw new InvalidOperationException("أدخل اسم السنة.");
            if (dto.EndDate <= dto.StartDate)
                throw new InvalidOperationException("تاريخ النهاية يجب أن يكون بعد البداية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            int newId;
            try
            {
                await EnsureFiscalYearCodeAvailableAsync(connection, trans, dto.YearCode);
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

                // إنشاء فترات شهرية متتابعة تغطي مدى السنة كاملًا، حتى عند عبور سنة ميلادية أو بدء/انتهاء المدى في منتصف الشهر.
                var arabicMonths = new string[] { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
                var fiscalStart = dto.StartDate.Date;
                var fiscalEnd = dto.EndDate.Date;
                var periodStart = fiscalStart;
                var periodNumber = 1;
                while (periodStart <= fiscalEnd)
                {
                    var monthEnd = new DateTime(
                        periodStart.Year, periodStart.Month,
                        DateTime.DaysInMonth(periodStart.Year, periodStart.Month));
                    var periodEnd = monthEnd < fiscalEnd ? monthEnd : fiscalEnd;
                    var periodName = $"{arabicMonths[periodStart.Month]} {periodStart.Year}";
                    await connection.ExecuteAsync(@"INSERT INTO dbo.AccountingPeriods(FiscalYearID, PeriodNumber, PeriodName, StartDate, EndDate, PeriodStatus)
                                                    VALUES(@FYID, @Num, @Name, @S, @E, 1)",
                        new { FYID = newId, Num = periodNumber, Name = periodName, S = periodStart, E = periodEnd }, trans);

                    if (periodEnd == fiscalEnd) break;
                    periodStart = periodEnd.AddDays(1);
                    periodNumber++;
                }

                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after creating fiscal year {YearCode} for user {UserID}.",
                        dto.YearCode,
                        userId);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 1, "FiscalYears", newId.ToString(), moduleName: "SCR_FY", description: $"إنشاء سنة مالية: {dto.YearName}");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after creating fiscal year for record {RecordID}.",
                    newId);
            }
            return newId;
        }

        public async Task UpdateFiscalYearAsync(FiscalYearDto dto, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanEdit, "ليس لديك صلاحية تعديل السنوات المالية.");
            if (string.IsNullOrWhiteSpace(dto.YearName))
                throw new InvalidOperationException("أدخل اسم السنة.");
            if (dto.FiscalYearID <= 0)
                throw new ArgumentOutOfRangeException(nameof(dto.FiscalYearID), "يجب اختيار سنة مالية صالحة للتعديل.");
            if (dto.EndDate <= dto.StartDate)
                throw new InvalidOperationException("تاريخ النهاية يجب أن يكون بعد البداية.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var currentFiscalYear = await connection.QueryFirstOrDefaultAsync<(DateTime? StartDate, DateTime? EndDate)>(
                    @"SELECT StartDate, EndDate
                      FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK)
                      WHERE FiscalYearID=@FiscalYearID",
                    new { FiscalYearID = dto.FiscalYearID }, trans);
                if (!currentFiscalYear.StartDate.HasValue || !currentFiscalYear.EndDate.HasValue)
                    throw new InvalidOperationException("السنة المالية غير موجودة.");

                var dateRangeChanged = currentFiscalYear.StartDate.Value.Date != dto.StartDate.Date
                    || currentFiscalYear.EndDate.Value.Date != dto.EndDate.Date;
                if (dateRangeChanged)
                {
                    var linkedPeriods = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(1)
                          FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK)
                          WHERE FiscalYearID=@FiscalYearID",
                        new { FiscalYearID = dto.FiscalYearID }, trans);
                    if (linkedPeriods > 0)
                        throw new InvalidOperationException("لا يمكن تغيير نطاق تواريخ السنة المالية بعد إنشاء فتراتها.");
                }

                await EnsureFiscalYearCodeAvailableAsync(connection, trans, dto.YearCode, dto.FiscalYearID);
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
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after updating fiscal year {FiscalYearID} ({YearCode}) failed.",
                        dto.FiscalYearID,
                        dto.YearCode);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 2, "FiscalYears", dto.FiscalYearID.ToString(), moduleName: "SCR_FY", description: $"تعديل سنة مالية: {dto.YearName}");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after updating fiscal year for record {RecordID}.",
                    dto.FiscalYearID);
            }
        }

        public async Task CloseFiscalYearAsync(int id, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanApprove, "ليس لديك صلاحية إغلاق السنة المالية.");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var yearStatus = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT YearStatus FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = id }, trans);
                if (!yearStatus.HasValue) throw new InvalidOperationException("السنة المالية غير موجودة.");
                if (yearStatus.Value != 1) throw new InvalidOperationException("لا يمكن إغلاق سنة مالية غير مفتوحة.");

                var periodSummary = await connection.QueryFirstOrDefaultAsync<(int PeriodCount, int NotClosedPeriodCount)>(
                    @"SELECT COUNT(1) AS PeriodCount,
                             ISNULL(SUM(CASE WHEN ISNULL(PeriodStatus,0)<>2 THEN 1 ELSE 0 END),0) AS NotClosedPeriodCount
                      FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK)
                      WHERE FiscalYearID=@ID",
                    new { ID = id }, trans);
                if (periodSummary.PeriodCount == 0)
                    throw new InvalidOperationException("لا يمكن إغلاق السنة المالية قبل إنشاء فتراتها المحاسبية.");
                if (periodSummary.NotClosedPeriodCount > 0)
                    throw new InvalidOperationException("يجب إغلاق جميع فترات السنة أولاً.");

                var pendingJournals = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE FiscalYearID=@ID AND ISNULL(JournalStatus,0) NOT IN (2,3)",
                    new { ID = id }, trans);
                if (pendingJournals > 0)
                    throw new InvalidOperationException("لا يمكن إغلاق السنة؛ توجد قيود مسودة أو غير مرحلة.");

                var closed = await connection.ExecuteAsync(
                    @"UPDATE dbo.FiscalYears SET YearStatus=2, ClosedBy=@UserID, ClosedDate=GETDATE()
                      WHERE FiscalYearID=@ID AND YearStatus=1",
                    new { UserID = userId, ID = id }, trans);
                if (closed != 1)
                    throw new InvalidOperationException("تغيرت حالة السنة المالية أثناء الإغلاق؛ أُلغيت العملية.");

                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after closing fiscal year {FiscalYearID} failed.",
                        id);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 2, "FiscalYears", id.ToString(), moduleName: "SCR_FY", description: "إغلاق سنة مالية");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after closing fiscal year for record {RecordID}.",
                    id);
            }
        }

        // ==========================================
        // الفترات المحاسبية - على الجدول الحقيقي AccountingPeriods 9 أعمدة - بدون IsActive
        // ==========================================
        public async Task<List<AccountingPeriodListDto>> GetAccountingPeriodsAsync(int? fiscalYearId = null)
        {
            return await GetAccountingPeriodsCoreAsync(fiscalYearId, rethrowFinalReadFailure: false);
        }

        public async Task<List<AccountingPeriodListDto>> GetAccountingPeriodsForExpenseFilterAsync()
        {
            return await GetAccountingPeriodsCoreAsync(fiscalYearId: null, rethrowFinalReadFailure: true);
        }

        public async Task<List<AccountingPeriodListDto>> GetAccountingPeriodsForFiscalYearViewAsync(int fiscalYearId)
        {
            return await GetAccountingPeriodsCoreAsync(fiscalYearId, rethrowFinalReadFailure: true);
        }

        private async Task<List<AccountingPeriodListDto>> GetAccountingPeriodsCoreAsync(
            int? fiscalYearId, bool rethrowFinalReadFailure)
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
            catch (Exception primaryException)
            {
                if (rethrowFinalReadFailure)
                    _logger.LogWarning(primaryException,
                        "The full accounting-period lookup failed for fiscal year {FiscalYearID}; attempting the compatibility fallback.",
                        fiscalYearId);

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
                    if (rethrowFinalReadFailure)
                        throw;

                    return new List<AccountingPeriodListDto>();
                }
            }
        }

        public async Task<List<AccountingPeriodListDto>> GetAccountingPeriodsForExpenseEditAsync()
        {
            return await GetAccountingPeriodsCoreAsync(fiscalYearId: null, rethrowFinalReadFailure: true);
        }

        public async Task ClosePeriodAsync(int id, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanApprove, "ليس لديك صلاحية إغلاق الفترة المحاسبية.");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                // اقفل السنة أولاً ثم الفترة، بنفس ترتيب فتح الفترة وإغلاق السنة.
                var fiscalYearId = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT FiscalYearID FROM dbo.AccountingPeriods WHERE PeriodID=@ID",
                    new { ID = id }, trans);
                if (!fiscalYearId.HasValue || fiscalYearId.Value <= 0)
                    throw new InvalidOperationException("الفترة المحاسبية غير موجودة أو غير مرتبطة بسنة مالية.");

                var yearStatus = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT YearStatus FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = fiscalYearId.Value }, trans);
                if (yearStatus != 1)
                    throw new InvalidOperationException("لا يمكن إغلاق فترة داخل سنة مالية مغلقة أو غير موجودة.");

                var period = await connection.QueryFirstOrDefaultAsync<(int? FiscalYearID, int? PeriodStatus)>(
                    "SELECT FiscalYearID, PeriodStatus FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK) WHERE PeriodID=@ID",
                    new { ID = id }, trans);
                if (period.FiscalYearID != fiscalYearId.Value || period.PeriodStatus != 1)
                    throw new InvalidOperationException("الفترة غير مفتوحة أو تغير ارتباطها بالسنة؛ أُوقف الإغلاق.");

                var pendingJournals = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK)
                      WHERE PeriodID=@ID AND ISNULL(JournalStatus,0) NOT IN (2,3)",
                    new { ID = id }, trans);
                if (pendingJournals > 0)
                    throw new InvalidOperationException("لا يمكن إغلاق الفترة؛ توجد قيود مسودة أو غير مرحلة.");

                var closed = await connection.ExecuteAsync(
                    @"UPDATE dbo.AccountingPeriods SET PeriodStatus=2, ClosedBy=@UserID, ClosedDate=GETDATE()
                      WHERE PeriodID=@ID AND FiscalYearID=@FYID AND PeriodStatus=1",
                    new { UserID = userId, ID = id, FYID = fiscalYearId.Value }, trans);
                if (closed != 1)
                    throw new InvalidOperationException("تغيرت حالة الفترة أثناء الإغلاق؛ أُلغيت العملية.");

                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after closing accounting period {PeriodID} failed.",
                        id);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 2, "AccountingPeriods", id.ToString(), moduleName: "SCR_FY", description: "إغلاق فترة محاسبية");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after closing accounting period for record {RecordID}.",
                    id);
            }
        }

        public async Task OpenPeriodAsync(int id, int userId)
        {
            await RequireFiscalPermissionAsync(
                userId, p => p.CanApprove, "ليس لديك صلاحية إعادة فتح الفترة المحاسبية.");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

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
                    throw new InvalidOperationException("الفترة المحاسبية غير موجودة أو غير مرتبطة بسنة مالية.");

                var yearStatus = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT YearStatus FROM dbo.FiscalYears WITH (UPDLOCK, HOLDLOCK) WHERE FiscalYearID=@ID",
                    new { ID = fiscalYearId.Value }, trans);
                if (yearStatus != 1)
                    throw new InvalidOperationException("لا يمكن فتح فترة داخل سنة مالية مغلقة أو غير موجودة.");

                var period = await connection.QueryFirstOrDefaultAsync<(int? FiscalYearID, int? PeriodStatus)>(
                    "SELECT FiscalYearID, PeriodStatus FROM dbo.AccountingPeriods WITH (UPDLOCK, HOLDLOCK) WHERE PeriodID=@ID",
                    new { ID = id }, trans);
                if (!period.FiscalYearID.HasValue || period.FiscalYearID.Value != fiscalYearId.Value)
                    throw new InvalidOperationException("تغير ارتباط الفترة بالسنة المالية أثناء العملية؛ أُوقف إعادة الفتح.");
                if (period.PeriodStatus != 2)
                    throw new InvalidOperationException("لا يمكن إعادة فتح الفترة إلا إذا كانت مغلقة بالحالة المعتمدة.");

                var opened = await connection.ExecuteAsync(
                    @"UPDATE dbo.AccountingPeriods
                      SET PeriodStatus=1, ClosedBy=NULL, ClosedDate=NULL
                      WHERE PeriodID=@ID AND FiscalYearID=@FYID AND PeriodStatus=2",
                    new { ID = id, FYID = fiscalYearId.Value }, trans);
                if (opened != 1)
                    throw new InvalidOperationException("تغيرت حالة الفترة أثناء إعادة الفتح؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after reopening accounting period {PeriodID} failed.",
                        id);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 2, "AccountingPeriods", id.ToString(), moduleName: "SCR_FY", description: "فتح فترة محاسبية");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after opening accounting period for record {RecordID}.",
                    id);
            }
        }

        // ==========================================
        // المصروفات - OverheadExpenses    Pro
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

        private async Task<string> GenerateExpenseNoCoreAsync(SqlConnection connection, SqlTransaction transaction)
        {
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'EXP', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql, transaction: transaction);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً للمصروف.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("تعذر توليد رقم المصروف داخل معاملة الحفظ؛ أُلغيت المعاملة.", ex);
            }
        }

        private async Task<int> ResolveExpenseCreditAccountIdAsync(
            SqlConnection connection, SqlTransaction transaction, ExpenseDto dto)
        {
            if (!dto.PaymentMethod.HasValue || dto.PaymentMethod.Value < 1 || dto.PaymentMethod.Value > 3)
                throw new InvalidOperationException("اختر طريقة دفع صحيحة للمصروف.");
            var paymentMethod = dto.PaymentMethod.Value;

            int? accountId = null;
            if (paymentMethod == 1)
            {
                if (dto.CashBoxID is not int cashBoxId || cashBoxId <= 0 || dto.BankAccountID.GetValueOrDefault() > 0)
                    throw new InvalidOperationException("طريقة الدفع النقدي تتطلب اختيار خزينة فقط.");

                accountId = await connection.ExecuteScalarAsync<int?>(
                    @"SELECT TOP 1 coa.AccountID FROM dbo.CashBoxes cb WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK, HOLDLOCK) ON coa.AccountID=cb.AccountID
                      WHERE cb.CashBoxID=@ID AND ISNULL(cb.IsActive,1)=1 AND coa.IsActive=1
                        AND coa.IsDetailAccount=1 AND coa.AccountType=1",
                    new { ID = cashBoxId }, transaction);
            }
            else if (paymentMethod == 2)
            {
                if (dto.BankAccountID is not int bankAccountId || bankAccountId <= 0 || dto.CashBoxID.GetValueOrDefault() > 0)
                    throw new InvalidOperationException("طريقة الدفع البنكي تتطلب اختيار حساب بنكي فقط.");

                accountId = await connection.ExecuteScalarAsync<int?>(
                    @"SELECT TOP 1 coa.AccountID FROM dbo.BankAccounts ba WITH (UPDLOCK, HOLDLOCK)
                      INNER JOIN dbo.ChartOfAccounts coa WITH (UPDLOCK, HOLDLOCK) ON coa.AccountID=ba.AccountID
                      WHERE ba.BankAccountID=@ID AND ISNULL(ba.IsActive,1)=1 AND coa.IsActive=1
                        AND coa.IsDetailAccount=1 AND coa.AccountType=1",
                    new { ID = bankAccountId }, transaction);
            }
            else if (paymentMethod == 3)
            {
                if (dto.CreditAccountID is not int liabilityAccountId || liabilityAccountId <= 0
                    || dto.CashBoxID.GetValueOrDefault() > 0 || dto.BankAccountID.GetValueOrDefault() > 0)
                    throw new InvalidOperationException("طريقة الدفع الآجل تتطلب اختيار حساب التزام فقط.");

                accountId = await connection.ExecuteScalarAsync<int?>(
                    @"SELECT TOP 1 AccountID FROM dbo.ChartOfAccounts WITH (UPDLOCK, HOLDLOCK)
                      WHERE AccountID=@ID AND IsActive=1 AND IsDetailAccount=1 AND AccountType=2",
                    new { ID = liabilityAccountId }, transaction);
            }

            var resolvedAccountId = accountId.GetValueOrDefault();
            if (resolvedAccountId <= 0)
            {
                var message = paymentMethod switch
                {
                    1 => "الخزينة المحددة غير نشطة أو غير مرتبطة بحساب أصل تفصيلي صالح.",
                    2 => "الحساب البنكي المحدد غير نشط أو غير مرتبط بحساب أصل تفصيلي صالح.",
                    _ => "حساب الالتزام المحدد غير نشط أو غير تفصيلي."
                };
                throw new InvalidOperationException(message);
            }

            if ((paymentMethod == 1 || paymentMethod == 2)
                && dto.CreditAccountID is int savedCreditAccountId
                && savedCreditAccountId > 0
                && savedCreditAccountId != resolvedAccountId)
                throw new InvalidOperationException("حساب الدائن المحفوظ لا يطابق الحساب المرتبط بمصدر الدفع.");

            return resolvedAccountId;
        }

        private async Task ValidateExpenseDebitAccountAsync(
            SqlConnection connection, SqlTransaction transaction, ExpenseDto dto)
        {
            if (dto.AccountID <= 0)
                throw new InvalidOperationException("يجب اختيار حساب المصروف.");

            var account = await connection.QueryFirstOrDefaultAsync<ChartOfAccountDto>(
                @"SELECT AccountID, AccountType, IsDetailAccount, IsActive, CostCenterRequired
                  FROM dbo.ChartOfAccounts WITH (UPDLOCK, HOLDLOCK) WHERE AccountID=@ID",
                new { ID = dto.AccountID }, transaction);
            if (account == null || !account.IsActive || !account.IsDetailAccount || (account.AccountType != 5 && account.AccountType != 6))
                throw new InvalidOperationException("حساب المصروف غير نشط أو غير تفصيلي أو ليس من حسابات التكلفة/المصروفات.");
            if (account.CostCenterRequired && dto.CostCenterID.GetValueOrDefault() <= 0)
                throw new InvalidOperationException("هذا الحساب يتطلب اختيار مركز تكلفة.");

            if (dto.CostCenterID is int costCenterId && costCenterId > 0)
            {
                var centerIsActive = await connection.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 CASE WHEN ISNULL(IsActive,1)=1 THEN 1 ELSE 0 END FROM dbo.CostCenters WITH (UPDLOCK, HOLDLOCK) WHERE CostCenterID=@ID",
                    new { ID = costCenterId }, transaction);
                if (centerIsActive != 1)
                    throw new InvalidOperationException("مركز التكلفة المحدد غير موجود أو غير نشط.");
            }
        }

        public async Task<int> InsertExpenseAsync(ExpenseDto dto, int userId)
        {
            await RequireExpensePermissionAsync(
                userId, p => p.CanAdd, "ليس لديك صلاحية إنشاء مصروف.");

            // افحص مخطط الأعمدة قبل المعاملة، لكن لا تستهلك أي تسلسل قبل نجاح التحقق.
            bool hasCreditCol, hasRefCol, hasNumCol, hasStatusCol, hasPostedCol, hasFiscalCol;
            bool hasCashCol, hasBankCol, hasPaymentMethodCol, hasNotesCol;
            using (var checkConn = CreateConnection())
            {
                hasCreditCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='CreditAccountID'") > 0;
                hasRefCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='Reference'") > 0;
                hasNumCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='ExpenseNumber'") > 0;
                hasStatusCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='ExpenseStatus'") > 0;
                hasPostedCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='IsPosted'") > 0;
                hasFiscalCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='FiscalYearID'") > 0;
                hasCashCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='CashBoxID'") > 0;
                hasBankCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='BankAccountID'") > 0;
                hasPaymentMethodCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='PaymentMethod'") > 0;
                hasNotesCol = await checkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.OverheadExpenses') AND name='Notes'") > 0;
            }

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

                if (dto.Amount <= 0) throw new InvalidOperationException("المبلغ يجب أن يكون أكبر من صفر.");
                if (!hasNumCol || !hasStatusCol || !hasPostedCol || !hasPaymentMethodCol || !hasCreditCol)
                    throw new InvalidOperationException("مخطط جدول المصروفات لا يحتوي الأعمدة اللازمة لحفظ الرقم وحالة الترحيل ومصدر الدفع بأمان.");

                if ((dto.PaymentMethod == 1 && !hasCashCol) || (dto.PaymentMethod == 2 && !hasBankCol))
                    throw new InvalidOperationException("مخطط جدول المصروفات لا يدعم حفظ مصدر الدفع المختار بأمان.");

                await ValidateExpenseDebitAccountAsync(connection, trans, dto);
                var creditAccId = await ResolveExpenseCreditAccountIdAsync(connection, trans, dto);
                dto.CreditAccountID = creditAccId;

                // يُستهلك التسلسلان فقط بعد التحقق من الفترة والحسابات ومصدر الدفع، وداخل معاملة الإدراج.
                dto.ExpenseNumber = await GenerateExpenseNoCoreAsync(connection, trans);
                var journalNumber = await GenerateJournalNoCoreAsync(connection, trans);

                // إدراج المصروف كمسودة غير مرحّلة؛ أثر الأرصدة لا يثبت إلا عند الاعتماد.
                var cols = "PeriodID, ExpenseDate, AccountID, CostCenterID, Description, Amount, CreatedBy, CreatedDate";
                var vals = "@PeriodID, @ExpenseDate, @AccountID, NULLIF(@CostCenterID,0), @Description, @Amount, @UserID, GETDATE()";
                if (hasCreditCol) { cols += ", CreditAccountID"; vals += ", NULLIF(@CreditAccountID,0)"; }
                if (hasRefCol) { cols += ", Reference"; vals += ", @Reference"; }
                if (hasNumCol) { cols += ", ExpenseNumber"; vals += ", @ExpenseNumber"; }
                if (hasStatusCol) { cols += ", ExpenseStatus"; vals += ", 1"; }
                if (hasPostedCol) { cols += ", IsPosted"; vals += ", 0"; }
                if (hasFiscalCol) { cols += ", FiscalYearID"; vals += ", @FiscalYearID"; }
                if (hasCashCol) { cols += ", CashBoxID"; vals += ", NULLIF(@CashBoxID,0)"; }
                if (hasBankCol) { cols += ", BankAccountID"; vals += ", NULLIF(@BankAccountID,0)"; }
                if (hasPaymentMethodCol) { cols += ", PaymentMethod"; vals += ", @PaymentMethod"; }
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

                // إنشاء قيد مسودة متوازن: مدين حساب المصروف - دائن الحساب المرتبط بمصدر الدفع/الالتزام.
                if (creditAccId <= 0) throw new InvalidOperationException("تعذر تحديد حساب دائن صالح للمصروف.");
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
                    new { JID = jId, AccID = creditAccId, Amt = dto.Amount, Desc = dto.Description, CCID = dto.CostCenterID ?? 0 }, trans);

                await connection.ExecuteAsync("UPDATE dbo.OverheadExpenses SET JournalID=@JID, IsPosted=0 WHERE ExpenseID=@ID",
                    new { JID = jId, ID = newId }, trans);

                trans.Commit();
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 1, "OverheadExpenses", newId.ToString(), moduleName: "SCR_EXPENSES", description: $"إنشاء مصروف: {dto.ExpenseNumber} - {dto.Amount} - طريقة الدفع {dto.PaymentMethod}");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after creating expense for record {RecordID}.",
                        newId);
                }
                return newId;
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after creating expense {ExpenseNumber} for user {UserID}.",
                        dto.ExpenseNumber,
                        userId);
                }
                throw;
            }
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

                // وجود قيد مسودة لا يعني وجود أثر أرصدة؛ يُعكس الأثر القديم فقط عند IsPosted=true.
                var hasFinancialEffect = current.IsPosted;
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

                // اعتمد على مصدر الدفع المحدد صراحةً؛ لا تُخمن حساباً بديلاً ولا تختَر خزينة/بنك عشوائياً.
                await ValidateExpenseDebitAccountAsync(connection, trans, dto);
                var newCreditAccountId = await ResolveExpenseCreditAccountIdAsync(connection, trans, dto);
                dto.CreditAccountID = newCreditAccountId;

                // عكس أرصدة الخزينة/البنك القديمة باستخدام قيمة القيد القديم، لا قيمة الرأس التي ربما تغيرت.
                if (current.IsPosted && current.CashBoxID.HasValue && current.CashBoxID.Value > 0)
                {
                    var reversed = await connection.ExecuteAsync(
                        "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amount WHERE CashBoxID=@ID",
                        new { Amount = oldFinancialAmount, ID = current.CashBoxID.Value }, trans);
                    if (reversed != 1) throw new Exception("تعذر تحديد الخزينة القديمة لعكس أثر المصروف؛ لم يتم حفظ التعديل.");
                }
                if (current.IsPosted && current.BankAccountID.HasValue && current.BankAccountID.Value > 0)
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

                if (current.IsPosted)
                {
                    foreach (var group in oldJournalLines.GroupBy(x => x.AccountID).OrderBy(x => x.Key))
                    {
                        var oldDelta = group.Sum(x => x.Debit - x.Credit);
                        await ApplyAccountDeltaAsync(group.Key, -oldDelta);
                    }
                }

                var sourceJournalId = (int?)null;
                var sourceIsPosted = current.IsPosted;
                if (hasOldJournal)
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
                        new { JournalID = oldJournalId, AccountID = newCreditAccountId, dto.Amount, dto.Description, CostCenterID = dto.CostCenterID ?? 0 }, trans);

                    sourceJournalId = oldJournalId;
                    sourceIsPosted = current.IsPosted;
                }
                else
                {
                    // أصلح المصروفات القديمة التي لا تملك قيداً بإنشاء قيد مسودة؛ لا يُنشأ أي أثر رصيد هنا.
                    var journalNumber = await GenerateJournalNoCoreAsync(connection, trans);
                    var newJournalId = await connection.ExecuteScalarAsync<int>(
                        @"INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, Reference, SourceDocType, SourceDocID, SourceDocNumber,
                                TotalDebit, TotalCredit, FiscalYearID, PeriodID, JournalType, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate)
                          VALUES(@JournalNumber, @JournalDate, @Description, @Reference, 'OverheadExpense', @ExpenseID, @ExpenseNumber,
                                @Amount, @Amount, @FiscalYearID, @PeriodID, 1, 1, 1, @UserID, GETDATE());
                          SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new
                        {
                            JournalNumber = journalNumber,
                            JournalDate = dto.ExpenseDate,
                            dto.Description,
                            dto.Reference,
                            dto.ExpenseID,
                            dto.ExpenseNumber,
                            dto.Amount,
                            dto.FiscalYearID,
                            dto.PeriodID,
                            UserID = userId
                        }, trans);
                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID)
                          VALUES(@JournalID,1,@AccountID,@Amount,0,@Description,NULLIF(@CostCenterID,0))",
                        new { JournalID = newJournalId, dto.AccountID, dto.Amount, dto.Description, CostCenterID = dto.CostCenterID ?? 0 }, trans);
                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description, CostCenterID)
                          VALUES(@JournalID,2,@AccountID,0,@Amount,@Description,NULLIF(@CostCenterID,0))",
                        new { JournalID = newJournalId, AccountID = newCreditAccountId, dto.Amount, dto.Description, CostCenterID = dto.CostCenterID ?? 0 }, trans);
                    sourceJournalId = newJournalId;
                    sourceIsPosted = false;
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

                if (sourceIsPosted)
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
                    AddNewDelta(newCreditAccountId, -dto.Amount);
                    foreach (var item in newDeltas.OrderBy(x => x.Key))
                        await ApplyAccountDeltaAsync(item.Key, item.Value);
                }

                trans.Commit();
                dto.JournalID = sourceJournalId;
                dto.IsPosted = sourceIsPosted;
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 2, "OverheadExpenses", dto.ExpenseID.ToString(),
                        moduleName: "SCR_EXPENSES", description: $"تعديل مصروف وعكس/إعادة تطبيق القيد: {dto.ExpenseNumber}");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after updating expense for record {RecordID}.",
                        dto.ExpenseID);
                }
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after updating expense {ExpenseID} failed.",
                        dto.ExpenseID);
                }
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
                var exp = await connection.QueryFirstOrDefaultAsync<(
                    int? ExpenseStatus, bool IsPosted, int? JournalID, decimal Amount, int AccountID,
                    int? CreditAccountID, int? CashBoxID, int? BankAccountID)>(
                    @"SELECT ISNULL(ExpenseStatus,1) AS ExpenseStatus, ISNULL(IsPosted,0) AS IsPosted,
                             JournalID, Amount, AccountID, CreditAccountID, CashBoxID, BankAccountID
                      FROM dbo.OverheadExpenses WITH (UPDLOCK, HOLDLOCK) WHERE ExpenseID=@ID",
                    new { ID = id }, trans);
                if (!exp.ExpenseStatus.HasValue) throw new Exception("المصروف غير موجود");
                if (exp.ExpenseStatus.Value != 1) throw new Exception("لا يمكن حذف مصروف معتمد");

                var hasJournal = exp.JournalID.GetValueOrDefault() > 0;
                if (exp.IsPosted && !hasJournal)
                    throw new InvalidOperationException("المصروف له أثر مالي لكنه بلا قيد مرتبط؛ لا يمكن حذفه بأمان.");
                if (exp.IsPosted)
                    await RequireExpensePermissionAsync(userId, p => p.CanApprove,
                        "حذف مصروف له أثر مالي يتطلب صلاحية الحذف والاعتماد معاً.");

                var journalLines = new List<(int AccountID, decimal Debit, decimal Credit)>();
                decimal financialAmount = 0m;
                if (hasJournal)
                {
                    var journal = await connection.QueryFirstOrDefaultAsync<(
                        int? JournalStatus, bool IsAutoGenerated, string? SourceDocType, int? SourceDocID)>(
                        @"SELECT JournalStatus, ISNULL(IsAutoGenerated,0) AS IsAutoGenerated, SourceDocType, SourceDocID
                          FROM dbo.JournalEntries WITH (UPDLOCK, HOLDLOCK) WHERE JournalID=@ID",
                        new { ID = exp.JournalID!.Value }, trans);
                    if (!journal.JournalStatus.HasValue || journal.JournalStatus.Value != 1 || !journal.IsAutoGenerated
                        || !string.Equals(journal.SourceDocType, "OverheadExpense", StringComparison.OrdinalIgnoreCase)
                        || journal.SourceDocID != id)
                        throw new InvalidOperationException("القيد المرتبط ليس قيد مصروف أوتوماتيكياً مسودة؛ لم يتم حذف المصروف.");

                    journalLines = (await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit)>(
                        @"SELECT AccountID, DebitAmount AS Debit, CreditAmount AS Credit
                          FROM dbo.JournalEntryDetails WITH (UPDLOCK, HOLDLOCK) WHERE JournalID=@ID ORDER BY LineNumber",
                        new { ID = exp.JournalID.Value }, trans)).ToList();
                    var debitLines = journalLines.Where(x => x.Debit > 0m && x.Credit == 0m).ToList();
                    var creditLines = journalLines.Where(x => x.Credit > 0m && x.Debit == 0m).ToList();
                    if (journalLines.Count != 2 || debitLines.Count != 1 || creditLines.Count != 1
                        || journalLines.Any(x => x.AccountID <= 0 || x.Debit < 0m || x.Credit < 0m)
                        || debitLines[0].Debit != creditLines[0].Credit)
                        throw new InvalidOperationException("تفاصيل القيد المرتبط غير متوازنة أو لا تطابق قيد مصروف قابل للحذف.");
                    financialAmount = debitLines[0].Debit;
                }

                if (exp.IsPosted)
                {
                    if (exp.CashBoxID.GetValueOrDefault() > 0)
                    {
                        var affected = await connection.ExecuteAsync(
                            "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amount WHERE CashBoxID=@ID",
                            new { Amount = financialAmount, ID = exp.CashBoxID!.Value }, trans);
                        if (affected != 1) throw new InvalidOperationException("تعذر عكس رصيد الخزينة؛ لم يتم حذف المصروف.");
                    }
                    if (exp.BankAccountID.GetValueOrDefault() > 0)
                    {
                        var affected = await connection.ExecuteAsync(
                            "UPDATE dbo.BankAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amount WHERE BankAccountID=@ID",
                            new { Amount = financialAmount, ID = exp.BankAccountID!.Value }, trans);
                        if (affected != 1) throw new InvalidOperationException("تعذر عكس رصيد البنك؛ لم يتم حذف المصروف.");
                    }

                    foreach (var group in journalLines.GroupBy(x => x.AccountID).OrderBy(x => x.Key))
                    {
                        var reverseDelta = -group.Sum(x => x.Debit - x.Credit);
                        if (reverseDelta == 0m) continue;
                        var affected = await connection.ExecuteAsync(
                            "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                            new { Delta = reverseDelta, ID = group.Key }, trans);
                        if (affected != 1) throw new InvalidOperationException("تعذر عكس رصيد حساب القيد؛ لم يتم حذف المصروف.");

                        int? currentAccountId = group.Key;
                        for (var depth = 0; depth < 10; depth++)
                        {
                            var parentId = await connection.ExecuteScalarAsync<int?>(
                                "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID",
                                new { ID = currentAccountId }, trans);
                            if (!parentId.HasValue || parentId.Value <= 0) break;
                            var parentAffected = await connection.ExecuteAsync(
                                "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                                new { Delta = reverseDelta, ID = parentId.Value }, trans);
                            if (parentAffected != 1) throw new InvalidOperationException("تعذر عكس رصيد الحساب الأب؛ لم يتم حذف المصروف.");
                            currentAccountId = parentId.Value;
                        }
                    }
                }

                if (hasJournal)
                {
                    await connection.ExecuteAsync("DELETE FROM dbo.JournalEntryDetails WHERE JournalID=@JID",
                        new { JID = exp.JournalID!.Value }, trans);
                    var deletedJournal = await connection.ExecuteAsync(
                        "DELETE FROM dbo.JournalEntries WHERE JournalID=@JID AND JournalStatus=1 AND IsAutoGenerated=1",
                        new { JID = exp.JournalID.Value }, trans);
                    if (deletedJournal != 1) throw new InvalidOperationException("تعذر حذف القيد المسودة؛ أُلغيت عملية حذف المصروف.");
                }

                var deletedExpense = await connection.ExecuteAsync(
                    "DELETE FROM dbo.OverheadExpenses WHERE ExpenseID=@ID AND ISNULL(ExpenseStatus,1)=1",
                    new { ID = id }, trans);
                if (deletedExpense != 1) throw new InvalidOperationException("تغيرت حالة المصروف أثناء الحذف؛ أُلغيت العملية.");
                trans.Commit();
                try
                {
                    await _audit.WriteAuditLogAsync(userId, 3, "OverheadExpenses", id.ToString(), moduleName: "SCR_EXPENSES", description: "حذف مصروف وعكس الأرصدة");
                }
                catch (Exception auditException)
                {
                    _logger.LogError(auditException,
                        "Audit logging failed after deleting expense for record {RecordID}.",
                        id);
                }
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after deleting expense {ExpenseID} failed.",
                        id);
                }
                throw;
            }
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
                    int? BankAccountID,
                    int? CostCenterID,
                    int? PaymentMethod)>(
                    @"SELECT ISNULL(ExpenseStatus,1) AS ExpenseStatus, PeriodID, ExpenseDate,
                             JournalID, ISNULL(IsPosted,0) AS IsPosted, Amount,
                             AccountID, CreditAccountID, CashBoxID, BankAccountID, CostCenterID, PaymentMethod
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

                if (!expense.JournalID.HasValue || expense.JournalID.Value <= 0)
                    throw new InvalidOperationException("لا يمكن اعتماد المصروف دون قيد مرتبط صالح؛ احفظ المصروف مجدداً لإصلاح القيد ثم أعد الاعتماد.");

                var expenseForPosting = new ExpenseDto
                {
                    ExpenseID = id,
                    ExpenseDate = expense.ExpenseDate.Value,
                    AccountID = expense.AccountID,
                    CostCenterID = expense.CostCenterID,
                    CreditAccountID = expense.CreditAccountID,
                    CashBoxID = expense.CashBoxID,
                    BankAccountID = expense.BankAccountID,
                    PaymentMethod = expense.PaymentMethod,
                    Amount = expense.Amount
                };
                await ValidateExpenseDebitAccountAsync(connection, trans, expenseForPosting);
                var expectedCreditAccountId = await ResolveExpenseCreditAccountIdAsync(connection, trans, expenseForPosting);

                {
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

                    if (creditLines[0].Credit != expense.Amount
                        || creditLines[0].AccountID != expectedCreditAccountId)
                        throw new InvalidOperationException("حساب الدائن في القيد لا يطابق مصدر الدفع/الالتزام المحفوظ؛ أُوقف الاعتماد بأمان.");

                    // المسودات الجديدة لا تُحدّث الأرصدة عند الحفظ؛ طبّق الأثر مرة واحدة هنا.
                    // السجلات القديمة التي سبق تطبيق أثرها (IsPosted=true) لا تُحدّث ثانيةً.
                    if (!expense.IsPosted)
                    {
                        if (expenseForPosting.PaymentMethod == 1)
                        {
                            var cashAffected = await connection.ExecuteAsync(
                                "UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amount WHERE CashBoxID=@ID",
                                new { Amount = expense.Amount, ID = expenseForPosting.CashBoxID!.Value }, trans);
                            if (cashAffected != 1) throw new InvalidOperationException("تعذر تحديث رصيد الخزينة؛ أُلغيت عملية الاعتماد.");
                        }
                        else if (expenseForPosting.PaymentMethod == 2)
                        {
                            var bankAffected = await connection.ExecuteAsync(
                                "UPDATE dbo.BankAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amount WHERE BankAccountID=@ID",
                                new { Amount = expense.Amount, ID = expenseForPosting.BankAccountID!.Value }, trans);
                            if (bankAffected != 1) throw new InvalidOperationException("تعذر تحديث رصيد البنك؛ أُلغيت عملية الاعتماد.");
                        }

                        async Task ApplyApprovedAccountDeltaAsync(int accountId, decimal delta)
                        {
                            if (delta == 0m) return;
                            var affected = await connection.ExecuteAsync(
                                "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                                new { Delta = delta, ID = accountId }, trans);
                            if (affected != 1) throw new InvalidOperationException("تعذر تحديث رصيد حساب المصروف؛ أُلغيت عملية الاعتماد.");

                            int? parentCandidate = accountId;
                            for (var depth = 0; depth < 10; depth++)
                            {
                                var parentId = await connection.ExecuteScalarAsync<int?>(
                                    "SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID",
                                    new { ID = parentCandidate }, trans);
                                if (!parentId.HasValue || parentId.Value <= 0) break;
                                var parentAffected = await connection.ExecuteAsync(
                                    "UPDATE dbo.ChartOfAccounts SET CurrentBalance=ISNULL(CurrentBalance,0)+@Delta WHERE AccountID=@ID",
                                    new { Delta = delta, ID = parentId.Value }, trans);
                                if (parentAffected != 1) throw new InvalidOperationException("تعذر تحديث رصيد الحساب الأب؛ أُلغيت عملية الاعتماد.");
                                parentCandidate = parentId.Value;
                            }
                        }

                        var accountDeltas = new Dictionary<int, decimal>();
                        accountDeltas[expense.AccountID] = expense.Amount;
                        if (accountDeltas.ContainsKey(expectedCreditAccountId))
                            accountDeltas[expectedCreditAccountId] -= expense.Amount;
                        else
                            accountDeltas[expectedCreditAccountId] = -expense.Amount;
                        foreach (var delta in accountDeltas.OrderBy(x => x.Key))
                            await ApplyApprovedAccountDeltaAsync(delta.Key, delta.Value);
                    }

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
                      SET ExpenseStatus=2, IsPosted=1, ApprovedBy=@UserID, ApprovedDate=GETDATE()
                      WHERE ExpenseID=@ID AND ISNULL(ExpenseStatus,1)=1",
                    new { UserID = userId, ID = id }, trans);
                if (approved != 1) throw new Exception("تغيرت حالة المصروف أثناء الاعتماد؛ أُلغيت العملية.");
                trans.Commit();
            }
            catch
            {
                try { trans.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after approving overhead expense {ExpenseID} failed.",
                        id);
                }
                throw;
            }

            try
            {
                await _audit.WriteAuditLogAsync(userId, 2, "OverheadExpenses", id.ToString(), moduleName: "SCR_EXPENSES", description: "اعتماد مصروف وترحيل قيده الأوتوماتيكي");
            }
            catch (Exception auditException)
            {
                _logger.LogError(auditException,
                    "Audit logging failed after approving expense for record {RecordID}.",
                    id);
            }
        }

        // ==========================================
        // تقارير محاسبية - حساب مباشر من القيود المعتمدة لتفادي اختلاف أعمدة الـ Views
        // ==========================================
        public async Task<List<TrialBalanceDto>> GetTrialBalanceAsync(DateTime? from = null, DateTime? to = null, int? level = null)
        {
            await _authorization.RequireAnyCurrentUserPermissionAsync(
                "ليس لديك صلاحية عرض ميزان المراجعة.",
                ("SCR_TRIAL", p => p.CanView),
                ("SCR_TRIALBAL", p => p.CanView),
                ("SCR_TB", p => p.CanView));

            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                throw new ArgumentException("تاريخ بداية ميزان المراجعة يجب ألا يتجاوز تاريخ النهاية.");

            using var connection = CreateConnection();
            var sql = @";WITH AccountMovements AS
                        (
                            SELECT jd.AccountID,
                                   SUM(CASE WHEN @From IS NOT NULL AND je.JournalDate < @From THEN jd.DebitAmount ELSE 0 END) AS PriorDebit,
                                   SUM(CASE WHEN @From IS NOT NULL AND je.JournalDate < @From THEN jd.CreditAmount ELSE 0 END) AS PriorCredit,
                                   SUM(CASE WHEN (@From IS NULL OR je.JournalDate >= @From)
                                             AND (@To IS NULL OR je.JournalDate < DATEADD(DAY,1,CONVERT(date,@To)))
                                            THEN jd.DebitAmount ELSE 0 END) AS PeriodDebit,
                                   SUM(CASE WHEN (@From IS NULL OR je.JournalDate >= @From)
                                             AND (@To IS NULL OR je.JournalDate < DATEADD(DAY,1,CONVERT(date,@To)))
                                            THEN jd.CreditAmount ELSE 0 END) AS PeriodCredit
                            FROM dbo.JournalEntryDetails jd
                            INNER JOIN dbo.JournalEntries je ON je.JournalID=jd.JournalID
                            WHERE je.JournalStatus=2
                              AND (@To IS NULL OR je.JournalDate < DATEADD(DAY,1,CONVERT(date,@To)))
                            GROUP BY jd.AccountID
                        )
                        SELECT coa.AccountID, coa.AccountCode, coa.AccountNameAr, coa.AccountLevel, coa.AccountType,
                               ISNULL(coa.OpeningBalance,0)+ISNULL(m.PriorDebit,0)-ISNULL(m.PriorCredit,0) AS OpeningBalance,
                               ISNULL(m.PeriodDebit,0) AS TotalDebit,
                               ISNULL(m.PeriodCredit,0) AS TotalCredit,
                               ISNULL(coa.OpeningBalance,0)+ISNULL(m.PriorDebit,0)-ISNULL(m.PriorCredit,0)
                                   +ISNULL(m.PeriodDebit,0)-ISNULL(m.PeriodCredit,0) AS CurrentBalance
                        FROM dbo.ChartOfAccounts coa
                        LEFT JOIN AccountMovements m ON m.AccountID=coa.AccountID
                        WHERE coa.IsActive=1 AND (@Level IS NULL OR coa.AccountLevel<=@Level)
                        ORDER BY coa.AccountCode";
            var result = await connection.QueryAsync<TrialBalanceDto>(sql, new { From = from, To = to, Level = level });
            return result.ToList();
        }

        public async Task<List<AccountLedgerDto>> GetAccountLedgerAsync(int accountId, DateTime? from = null, DateTime? to = null)
        {
            await _authorization.RequireAnyCurrentUserPermissionAsync(
                "ليس لديك صلاحية عرض دفتر الأستاذ.",
                ("SCR_LEDGER", p => p.CanView),
                ("SCR_ACCLEDGER", p => p.CanView),
                ("SCR_ACCOUNT_LEDGER", p => p.CanView));

            if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId), "يجب اختيار حساب صالح.");
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                throw new ArgumentException("تاريخ بداية دفتر الأستاذ يجب ألا يتجاوز تاريخ النهاية.");

            using var connection = CreateConnection();
            var sql = @";WITH OpeningBalance AS
                        (
                            SELECT ISNULL(coa.OpeningBalance,0) + ISNULL(
                                (
                                    SELECT SUM(jd0.DebitAmount-jd0.CreditAmount)
                                    FROM dbo.JournalEntryDetails jd0
                                    INNER JOIN dbo.JournalEntries je0 ON je0.JournalID=jd0.JournalID
                                    WHERE jd0.AccountID=coa.AccountID AND je0.JournalStatus=2
                                      AND @From IS NOT NULL AND je0.JournalDate < @From
                                ),0) AS Balance
                            FROM dbo.ChartOfAccounts coa
                            WHERE coa.AccountID=@AccID
                        )
                        SELECT jd.JournalDetailID, je.JournalID, je.JournalNumber, je.JournalDate, je.Description,
                               jd.DebitAmount, jd.CreditAmount, coa.AccountCode, coa.AccountNameAr,
                               ob.Balance + SUM(jd.DebitAmount-jd.CreditAmount) OVER
                                   (ORDER BY je.JournalDate, je.JournalID, jd.LineNumber, jd.JournalDetailID
                                    ROWS UNBOUNDED PRECEDING) AS RunningBalance
                        FROM dbo.JournalEntryDetails jd
                        INNER JOIN dbo.JournalEntries je ON je.JournalID=jd.JournalID
                        INNER JOIN dbo.ChartOfAccounts coa ON coa.AccountID=jd.AccountID
                        CROSS JOIN OpeningBalance ob
                        WHERE jd.AccountID=@AccID AND je.JournalStatus=2
                          AND (@From IS NULL OR je.JournalDate>=@From)
                          AND (@To IS NULL OR je.JournalDate < DATEADD(DAY,1,CONVERT(date,@To)))
                        ORDER BY je.JournalDate, je.JournalID, jd.LineNumber, jd.JournalDetailID";
            var result = await connection.QueryAsync<AccountLedgerDto>(sql, new { AccID = accountId, From = from, To = to });
            return result.ToList();
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
