using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class LookupService : BaseDbService
    {
        private readonly ModuleAuthorizationService _authorization;

        public LookupService(IConfiguration configuration, ModuleAuthorizationService authorization) : base(configuration)
        {
            _authorization = authorization;
        }

        private static (string ModuleCode, Func<ModulePermissionDto, bool> Predicate) ViewOnly(string moduleCode) =>
            (moduleCode, permission => permission.CanView);

        private static (string ModuleCode, Func<ModulePermissionDto, bool> Predicate) ViewOrAdd(string moduleCode) =>
            (moduleCode, permission => permission.CanView || permission.CanAdd);

        private static (string ModuleCode, Func<ModulePermissionDto, bool> Predicate) AnyOperation(string moduleCode) =>
            (moduleCode, permission => permission.CanView || permission.CanAdd || permission.CanEdit);

        private static (string ModuleCode, Func<ModulePermissionDto, bool> Predicate) AddOrEdit(string moduleCode) =>
            (moduleCode, permission => permission.CanAdd || permission.CanEdit);

        private static (string ModuleCode, Func<ModulePermissionDto, bool> Predicate) AddOnly(string moduleCode) =>
            (moduleCode, permission => permission.CanAdd);

        private Task RequireLookupPermissionAsync(
            string deniedMessage,
            params (string ModuleCode, Func<ModulePermissionDto, bool> Predicate)[] requirements) =>
            _authorization.RequireAnyCurrentUserPermissionAsync(deniedMessage, requirements);

        public async Task<List<LookupDto>> GetCategoriesAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_COUNT"),
                AddOrEdit("SCR_ITEMS"));

            using var connection = CreateConnection();
            var sql = @"SELECT c.CategoryID AS Id, c.CategoryNameAr AS Name,
                               c.CategoryCode AS Code, c.ParentCategoryID AS ParentId,
                               p.CategoryCode AS ParentCode
                        FROM dbo.ItemCategories c
                        LEFT JOIN dbo.ItemCategories p ON c.ParentCategoryID = p.CategoryID
                        WHERE c.IsActive = 1
                        ORDER BY c.CategoryLevel, c.SortOrder, c.CategoryNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetUnitsAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_PR"),
                ViewOnly("SCR_SO"),
                ViewOnly("SCR_PRICELISTS"),
                ViewOnly("SCR_QUOT"),
                ViewOnly("SCR_SINV"),
                ViewOnly("SCR_PRET"),
                ViewOnly("SCR_PO"),
                ViewOnly("SCR_PINV"),
                ViewOnly("SCR_TRANSFER"),
                ViewOnly("SCR_GRN"),
                AddOrEdit("SCR_ITEMS"));

            using var connection = CreateConnection();
            var sql = @"SELECT UnitID AS Id, UnitNameAr AS Name
                        FROM dbo.Units WHERE IsActive = 1
                        ORDER BY UnitNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetParentCategoriesAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_CATEGORIES"));

            using var connection = CreateConnection();
            var sql = @"SELECT CategoryID AS Id, CategoryNameAr AS Name, CategoryCode AS Code
                        FROM dbo.ItemCategories
                        WHERE ParentCategoryID IS NULL AND IsActive = 1
                        ORDER BY SortOrder, CategoryNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }
        
            public async Task<List<LookupDto>> GetEmployeesAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_PR"),
                ViewOnly("SCR_COUNT"),
                AnyOperation("SCR_ATTEND"),
                AnyOperation("SCR_LOANS"),
                AnyOperation("SCR_PENALTY"),
                ViewOrAdd("SCR_LEAVE"),
                ViewOnly("SCR_WAREHOUSES"),
                ViewOnly("SCR_TRANSFER"),
                ViewOnly("SCR_GRN"));

            using var connection = CreateConnection();
            var sql = @"SELECT EmployeeID AS Id, FullNameAr AS Name
                        FROM dbo.Employees
                        WHERE IsActive = 1 AND EmployeeStatus = 1
                        ORDER BY FullNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }
                public async Task<List<LookupDto>> GetPaymentTermsAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_SO"),
                ViewOnly("SCR_SUPPLIERS"),
                ViewOnly("SCR_SINV"),
                ViewOnly("SCR_PO"),
                ViewOnly("SCR_PINV"),
                AnyOperation("SCR_CUSTOMERS"));

            using var connection = CreateConnection();
            var sql = @"SELECT PaymentTermID AS Id, TermNameAr AS Name
                        FROM dbo.PaymentTerms WHERE IsActive = 1
                        ORDER BY DueDays";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetCurrenciesAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_BANKACC"),
                ViewOnly("SCR_BANK_ACCOUNTS"),
                ViewOnly("SCR_SO"),
                ViewOrAdd("SCR_RECV"),
                ViewOrAdd("SCR_PAYV"),
                ViewOnly("SCR_PRICELISTS"),
                AnyOperation("SCR_CUSTOMERS"),
                ViewOnly("SCR_SUPPLIERS"),
                ViewOnly("SCR_CHEQUES"),
                ViewOnly("SCR_CHEQUE"),
                AddOnly("SCR_CASHTRANS"),
                ViewOnly("SCR_SINV"),
                ViewOnly("SCR_BANKS"),
                ViewOnly("SCR_CASHBOX"),
                ViewOnly("SCR_CASHBOXES"),
                ViewOnly("SCR_PO"),
                ViewOnly("SCR_PINV"));

            using var connection = CreateConnection();
            var sql = @"SELECT CurrencyID AS Id, CurrencyNameAr + ' (' + CurrencyCode + ')' AS Name
                        FROM dbo.Currencies WHERE IsActive = 1
                        ORDER BY IsDefault DESC, CurrencyNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

                public async Task<List<LookupDto>> GetCustomerGroupsAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                AnyOperation("SCR_CUSTOMERS"));

            using var connection = CreateConnection();
            var sql = @"SELECT GroupID AS Id, GroupNameAr AS Name
                        FROM dbo.CustomerGroups WHERE IsActive = 1
                        ORDER BY GroupNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetPriceListsAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_SO"),
                AnyOperation("SCR_CUSTOMERS"));

            using var connection = CreateConnection();
            var sql = @"SELECT PriceListID AS Id, PriceListNameAr AS Name
                        FROM dbo.PriceLists WHERE IsActive = 1
                        ORDER BY PriceListNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetSalesRepsAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                AnyOperation("SCR_CUSTOMERS"),
                ViewOnly("SCR_SO"),
                ViewOnly("SCR_QUOT"));

            using var connection = CreateConnection();
            var sql = @"SELECT sr.SalesRepID AS Id, e.FullNameAr AS Name
                        FROM dbo.SalesRepresentatives sr
                        INNER JOIN dbo.Employees e ON sr.EmployeeID = e.EmployeeID
                        WHERE sr.IsActive = 1
                        ORDER BY e.FullNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }
                public async Task<List<LookupDto>> GetDepartmentsAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_PR"),
                ViewOnly("SCR_COST"),
                ViewOnly("SCR_COSTCENTERS"),
                ViewOnly("SCR_COST_CENTERS"));

            using var connection = CreateConnection();
            var sql = @"SELECT DepartmentID AS Id, DepartmentNameAr AS Name
                        FROM dbo.Departments WHERE IsActive = 1
                        ORDER BY SortOrder, DepartmentNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetItemsForPurchaseAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_PR"),
                ViewOnly("SCR_PRET"),
                ViewOnly("SCR_PO"),
                ViewOnly("SCR_PINV"),
                ViewOnly("SCR_GRN"),
                ViewOnly("SCR_SUPPLIERS"));

            using var connection = CreateConnection();
            var sql = @"SELECT ItemID AS Id, ItemNameAr + ' (' + ItemCode + ')' AS Name
                        FROM dbo.Items 
                        WHERE IsActive = 1
                        ORDER BY ItemNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }
                public async Task<List<SearchableItem>> GetItemsSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_STOCK"),
                ViewOnly("SCR_TRANSFER"));

            using var connection = CreateConnection();
            var sql = @"SELECT ItemID AS Id, 
                               ItemNameAr + N' (' + ItemCode + N')' AS Name, 
                               ItemCode AS Code,
                               CASE ItemType WHEN 1 THEN N'خام' WHEN 2 THEN N'تعبئة' WHEN 3 THEN N'نصف مصنع' WHEN 4 THEN N'منتج تام' ELSE N'أخرى' END AS Extra
                        FROM dbo.Items 
                        WHERE IsActive = 1
                        ORDER BY ItemNameAr";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetItemsForPurchaseSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_PR"),
                ViewOnly("SCR_PRET"),
                ViewOnly("SCR_PO"),
                ViewOnly("SCR_PINV"),
                ViewOnly("SCR_GRN"),
                ViewOnly("SCR_SUPPLIERS"));

            using var connection = CreateConnection();
            // تم التوسيع ليشمل كل الأصناف النشطة مع إظهار النوع والسعر - لأن المستخدم قد لا يجد أصنافه بسبب فلتر ItemType
            var sql = @"SELECT ItemID AS Id, 
                               ItemNameAr + N' (' + ItemCode + N')' + 
                               CASE ItemType WHEN 1 THEN N' - خام' WHEN 2 THEN N' - تعبئة' WHEN 3 THEN N' - نصف مصنع' WHEN 4 THEN N' - تام' ELSE N'' END AS Name, 
                               ItemCode AS Code,
                               CASE WHEN IsActive=1 THEN N'نشط' ELSE N'غير نشط' END AS Extra
                        FROM dbo.Items 
                        WHERE IsActive = 1
                        ORDER BY ItemType, ItemNameAr";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetSuppliersSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOrAdd("SCR_RECV"),
                ViewOrAdd("SCR_PAYV"),
                ViewOnly("SCR_PRET"),
                ViewOnly("SCR_PO"),
                ViewOnly("SCR_PINV"),
                ViewOnly("SCR_GRN"));

            using var connection = CreateConnection();
            var sql = @"SELECT SupplierID AS Id, 
                               SupplierNameAr AS Name, 
                               SupplierCode AS Code
                        FROM dbo.Suppliers 
                        WHERE IsActive = 1
                        ORDER BY SupplierNameAr";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetCustomersSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_SO"),
                ViewOrAdd("SCR_RECV"),
                ViewOrAdd("SCR_PAYV"),
                ViewOnly("SCR_QUOT"),
                ViewOnly("SCR_SINV"));

            using var connection = CreateConnection();
            var sql = @"SELECT CustomerID AS Id, 
                               CustomerNameAr AS Name, 
                               CustomerCode AS Code
                        FROM dbo.Customers 
                        WHERE IsActive = 1
                        ORDER BY CustomerNameAr";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }
                public async Task<int> GetEmployeeDepartmentAsync(int employeeId)
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_PR"));

            using var connection = CreateConnection();
            var result = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT DepartmentID FROM dbo.Employees WHERE EmployeeID = @ID",
                new { ID = employeeId });
            return result ?? 0;
        }

        public async Task<ItemQuickInfoDto?> GetItemQuickInfoAsync(int itemId)
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_PR"),
                ViewOnly("SCR_SO"),
                ViewOnly("SCR_QUOT"),
                ViewOnly("SCR_SINV"),
                ViewOnly("SCR_PRET"),
                ViewOnly("SCR_PO"),
                ViewOnly("SCR_PINV"),
                ViewOnly("SCR_TRANSFER"),
                ViewOnly("SCR_GRN"));

            using var connection = CreateConnection();
            var sql = @"SELECT 
                            i.PrimaryUnitID AS UnitID,
                            CASE 
                                WHEN ISNULL(i.LastPurchasePrice, 0) > 0 THEN i.LastPurchasePrice
                                WHEN ISNULL(i.StandardCost, 0) > 0 THEN i.StandardCost
                                ELSE ISNULL(i.AverageCost, 0)
                            END AS EstimatedPrice,
                            ISNULL(i.DefaultSellingPrice,0) AS SalesPrice
                        FROM dbo.Items i
                        WHERE i.ItemID = @ID";
            return await connection.QueryFirstOrDefaultAsync<ItemQuickInfoDto>(sql, new { ID = itemId });
        }

        public async Task<List<SearchableItem>> GetItemsForSaleSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_SO"),
                ViewOnly("SCR_QUOT"),
                ViewOnly("SCR_SINV"));

            using var connection = CreateConnection();
            // إصلاح جذري: كان يستثني المنتج التام (نوع 4) - الآن يشمل كل الأصناف النشطة مع سعر البيع
            var sql = @"SELECT ItemID AS Id, 
                               ItemNameAr + N' (' + ItemCode + N')' + 
                               CASE WHEN ISNULL(DefaultSellingPrice,0)>0 THEN N' - ' + CAST(CAST(DefaultSellingPrice AS DECIMAL(18,2)) AS NVARCHAR) + N' ج.م' ELSE N'' END +
                               CASE ItemType WHEN 1 THEN N' - خام' WHEN 2 THEN N' - تعبئة' WHEN 3 THEN N' - نصف مصنع' WHEN 4 THEN N' - تام' ELSE N'' END AS Name, 
                               ItemCode AS Code,
                               CAST(ISNULL(DefaultSellingPrice,0) AS NVARCHAR) AS Extra
                        FROM dbo.Items 
                        WHERE IsActive = 1
                        ORDER BY 
                            CASE ItemType WHEN 4 THEN 0 WHEN 3 THEN 1 ELSE 2 END,
                            ItemNameAr";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<CustomerQuickInfoDto?> GetCustomerQuickInfoAsync(int customerId)
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_SO"),
                ViewOnly("SCR_SINV"));

            using var connection = CreateConnection();
            var sql = @"SELECT c.CustomerID, c.CustomerNameAr, c.CustomerCode,
                               c.PaymentTermID, c.CurrencyID,
                               ISNULL(cur.ExchangeRate,1) AS ExchangeRate,
                               c.CreditLimit, c.CurrentBalance
                        FROM dbo.Customers c
                        LEFT JOIN dbo.Currencies cur ON c.CurrencyID=cur.CurrencyID
                        WHERE c.CustomerID=@ID";
            return await connection.QueryFirstOrDefaultAsync<CustomerQuickInfoDto>(sql, new { ID = customerId });
        }

        public async Task<List<LookupDto>> GetCashBoxesLookupAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_BANKS"),
                ViewOnly("SCR_CASHBOX"),
                ViewOnly("SCR_CASHBOXES"));

            using var connection = CreateConnection();
            var sql = @"SELECT CashBoxID AS Id, CashBoxNameAr AS Name, CashBoxCode AS Code FROM dbo.CashBoxes WHERE IsActive=1 ORDER BY CashBoxNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetCashBoxesSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_BANKS"),
                ViewOnly("SCR_CASHBOX"),
                ViewOnly("SCR_CASHBOXES"));

            using var connection = CreateConnection();
            var sql = @"SELECT CashBoxID AS Id, CashBoxNameAr AS Name, CashBoxCode AS Code FROM dbo.CashBoxes WHERE IsActive=1 ORDER BY CashBoxNameAr";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetChartOfAccountsLookupAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_COA"));

            using var connection = CreateConnection();
            var sql = @"SELECT AccountID AS Id, AccountNameAr + ' (' + AccountCode + ')' AS Name, AccountCode AS Code FROM dbo.ChartOfAccounts WHERE IsActive=1 AND IsGroup=0 ORDER BY AccountCode";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetChartOfAccountsSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_BANKACC"),
                ViewOnly("SCR_BANK_ACCOUNTS"),
                ViewOnly("SCR_LEDGER"),
                ViewOnly("SCR_ACCLEDGER"),
                ViewOnly("SCR_ACCOUNT_LEDGER"));

            using var connection = CreateConnection();
            var sql = @"SELECT AccountID AS Id, AccountNameAr AS Name, AccountCode AS Code FROM dbo.ChartOfAccounts WHERE IsActive=1 ORDER BY AccountCode";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetJournalPostingAccountsSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOrAdd("SCR_JV"),
                ViewOrAdd("SCR_JV_NEW"));

            using var connection = CreateConnection();
            var sql = @"SELECT AccountID AS Id, AccountNameAr AS Name, AccountCode AS Code
                        FROM dbo.ChartOfAccounts
                        WHERE IsActive=1 AND IsDetailAccount=1
                        ORDER BY AccountCode";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        // Alias for backward compatibility - fixes CS1061 errors
        public async Task<List<SearchableItem>> GetAccountsSearchableAsync()
        {
            return await GetChartOfAccountsSearchableAsync();
        }

        public async Task<List<SearchableItem>> GetRevenueAccountsSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOrAdd("SCR_RECV"));

            using var connection = CreateConnection();
            var sql = @"SELECT AccountID AS Id, AccountNameAr AS Name, AccountCode AS Code FROM dbo.ChartOfAccounts WHERE IsActive=1 AND IsDetailAccount=1 AND AccountType=4 ORDER BY AccountCode";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetExpenseAccountsSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOrAdd("SCR_PAYV"));

            using var connection = CreateConnection();
            var sql = @"SELECT AccountID AS Id, AccountNameAr AS Name, AccountCode AS Code FROM dbo.ChartOfAccounts WHERE IsActive=1 AND IsDetailAccount=1 AND AccountType=6 ORDER BY AccountCode";
            var result = await connection.QueryAsync<SearchableItem>(sql);
            return result.ToList();
        }

        public async Task<List<SearchableItem>> GetBankAccountsSearchableAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_BANKACC"),
                ViewOnly("SCR_BANK_ACCOUNTS"));

            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT BankAccountID AS Id, BankName + ISNULL(' - ' + AccountNumber,'') AS Name, BankAccountCode AS Code FROM dbo.BankAccounts WHERE IsActive=1 ORDER BY BankName";
                var result = await connection.QueryAsync<SearchableItem>(sql);
                return result.ToList();
            }
            catch
            {
                return new List<SearchableItem>();
            }
        }

        public Task<List<LookupDto>> GetCostCentersAsync() =>
            GetCostCentersCoreAsync(rethrowReadFailure: false);

        public Task<List<LookupDto>> GetCostCentersForJournalEntryAsync() =>
            GetCostCentersCoreAsync(rethrowReadFailure: true);

        private async Task<List<LookupDto>> GetCostCentersCoreAsync(bool rethrowReadFailure)
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOrAdd("SCR_JV"),
                ViewOrAdd("SCR_JV_NEW"));

            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT CostCenterID AS Id, CostCenterNameAr AS Name, CostCenterCode AS Code FROM dbo.CostCenters WHERE IsActive=1 ORDER BY CostCenterNameAr";
                var result = await connection.QueryAsync<LookupDto>(sql);
                return result.ToList();
            }
            catch
            {
                if (rethrowReadFailure)
                    throw;

                return new List<LookupDto>();
            }
        }
        // ==========================================
        // الموردين (LookupDto)
        // ==========================================
        public async Task<List<LookupDto>> GetSuppliersAsync()
        {
        await RequireLookupPermissionAsync(
            "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
            ViewOnly("SCR_SUPPLIERS"));

        using var connection = CreateConnection();
        var sql = @"SELECT SupplierID AS Id, 
                       SupplierNameAr AS Name,
                       SupplierCode AS Code
                FROM dbo.Suppliers 
                WHERE IsActive = 1
                ORDER BY SupplierNameAr";
        var result = await connection.QueryAsync<LookupDto>(sql);
        return result.ToList();
      }

// ==========================================
// المخازن (LookupDto)
// ==========================================
public async Task<List<LookupDto>> GetWarehousesAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_SO"),
        ViewOnly("SCR_COUNT"),
        ViewOnly("SCR_SRET"),
        ViewOnly("SCR_TRANS"),
        ViewOnly("SCR_SINV"),
        ViewOnly("SCR_STOCK"),
        ViewOnly("SCR_TRANSFER"));

    using var connection = CreateConnection();
    var sql = @"SELECT WarehouseID AS Id, 
                       WarehouseNameAr AS Name,
                       WarehouseCode AS Code
                FROM dbo.Warehouses 
                WHERE IsActive = 1
                ORDER BY WarehouseNameAr";
    var result = await connection.QueryAsync<LookupDto>(sql);
    return result.ToList();
}

    public async Task<SupplierQuickInfoDto?> GetSupplierQuickInfoAsync(int supplierId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PRET"),
        ViewOnly("SCR_PO"),
        ViewOnly("SCR_PINV"));

    using var connection = CreateConnection();
    var sql = @"SELECT 
                    s.SupplierID,
                    s.SupplierNameAr,
                    s.SupplierCode,
                    s.Phone1,
                    s.Mobile,
                    s.Email,
                    s.City,
                    s.PaymentTermID,
                    pt.TermNameAr AS PaymentTermName,
                    s.CurrencyID,
                    c.CurrencyCode,
                    ISNULL(c.ExchangeRate, 1) AS ExchangeRate,
                    s.CreditLimit,
                    s.CurrentBalance,
                    s.Rating,
                    s.Notes
                FROM dbo.Suppliers s
                LEFT JOIN dbo.PaymentTerms pt ON s.PaymentTermID = pt.PaymentTermID
                LEFT JOIN dbo.Currencies c ON s.CurrencyID = c.CurrencyID
                WHERE s.SupplierID = @ID";
    return await connection.QueryFirstOrDefaultAsync<SupplierQuickInfoDto>(sql, new { ID = supplierId });
}

// ==========================================
// سعر الصنف من المورد
// ==========================================
public async Task<SupplierItemPriceDto?> GetSupplierItemPriceAsync(int supplierId, int itemId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PO"));

    using var connection = CreateConnection();
    var sql = @"
        DECLARE @SupplierPrice DECIMAL(18,4) = 0;
        DECLARE @LastPurchPrice DECIMAL(18,4) = 0;
        DECLARE @AvgCost DECIMAL(18,4) = 0;
        DECLARE @StdCost DECIMAL(18,4) = 0;
        DECLARE @MinOrderQty DECIMAL(18,4) = 0;
        DECLARE @LeadTimeDays INT = 0;
        DECLARE @LastPurchDate DATE;
        DECLARE @CurrCode NVARCHAR(3);

        -- 1) سعر المورد من SupplierItems
        SELECT 
            @SupplierPrice = ISNULL(si.UnitPrice, 0),
            @MinOrderQty = ISNULL(si.MinOrderQty, 0),
            @LeadTimeDays = ISNULL(si.LeadTimeDays, 0),
            @LastPurchDate = si.LastPurchaseDate,
            @LastPurchPrice = ISNULL(si.LastPurchasePrice, 0),
            @CurrCode = c.CurrencyCode
        FROM dbo.SupplierItems si
        LEFT JOIN dbo.Currencies c ON si.CurrencyID = c.CurrencyID
        WHERE si.SupplierID = @SupplierID AND si.ItemID = @ItemID AND si.IsActive = 1;

        -- 2) بيانات الصنف
        SELECT 
            @AvgCost = ISNULL(i.AverageCost, 0),
            @StdCost = ISNULL(i.StandardCost, 0)
        FROM dbo.Items i 
        WHERE i.ItemID = @ItemID;

        -- 3) آخر سعر شراء من الفواتير (لو مش موجود في SupplierItems)
        IF @LastPurchPrice = 0
        BEGIN
            SELECT TOP 1 
                @LastPurchPrice = pid.UnitPrice,
                @LastPurchDate = pi.InvoiceDate
            FROM dbo.PurchaseInvoiceDetails pid
            INNER JOIN dbo.PurchaseInvoices pi ON pid.InvoiceID = pi.InvoiceID
            WHERE pid.ItemID = @ItemID 
              AND pi.SupplierID = @SupplierID
              AND pi.InvoiceStatus NOT IN (6)
            ORDER BY pi.InvoiceDate DESC;
        END

        -- 4) السعر المقترح (الأفضل المتاح)
        DECLARE @SuggestedPrice DECIMAL(18,4);
        DECLARE @PriceSource NVARCHAR(50);

        IF @SupplierPrice > 0
        BEGIN
            SET @SuggestedPrice = @SupplierPrice;
            SET @PriceSource = N'SupplierPrice';
        END
        ELSE IF @LastPurchPrice > 0
        BEGIN
            SET @SuggestedPrice = @LastPurchPrice;
            SET @PriceSource = N'LastPurchase';
        END
        ELSE IF @AvgCost > 0
        BEGIN
            SET @SuggestedPrice = @AvgCost;
            SET @PriceSource = N'AverageCost';
        END
        ELSE IF @StdCost > 0
        BEGIN
            SET @SuggestedPrice = @StdCost;
            SET @PriceSource = N'StandardCost';
        END
        ELSE
        BEGIN
            SET @SuggestedPrice = 0;
            SET @PriceSource = N'NoPrice';
        END

        SELECT 
            @ItemID AS ItemID,
            @SupplierID AS SupplierID,
            @SupplierPrice AS UnitPrice,
            @CurrCode AS CurrencyCode,
            @MinOrderQty AS MinOrderQty,
            @LeadTimeDays AS LeadTimeDays,
            @LastPurchDate AS LastPurchaseDate,
            @LastPurchPrice AS LastPurchasePrice,
            @SuggestedPrice AS SuggestedPrice,
            @PriceSource AS PriceSource;";

    return await connection.QueryFirstOrDefaultAsync<SupplierItemPriceDto>(
        sql, new { SupplierID = supplierId, ItemID = itemId });
}

// ==========================================
// أسعار كل الأصناف من المورد دفعة واحدة
// (للتحميل بعد اختيار PR + Supplier)
// ==========================================
public async Task<List<SupplierItemPriceDto>> GetSupplierItemPricesBulkAsync(int supplierId, List<int> itemIds)
{
    var result = new List<SupplierItemPriceDto>();
    foreach (var itemId in itemIds)
    {
        var price = await GetSupplierItemPriceAsync(supplierId, itemId);
        if (price != null)
            result.Add(price);
    }
    return result;
}

// ==========================================
// طلبات الشراء المعتمدة المتاحة لأمر شراء
// ==========================================
public async Task<List<ApprovedPRForPODto>> GetApprovedPRsForPOAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PO"));

    using var connection = CreateConnection();
    var sql = @"SELECT 
                    pr.RequestID,
                    pr.RequestNumber,
                    pr.RequestDate,
                    pr.RequiredDate,
                    e.FullNameAr AS RequestedByName,
                    d.DepartmentNameAr AS DepartmentName,
                    COUNT(v.RequestDetailID) AS TotalItems,
                    SUM(CASE WHEN v.RemainingToOrder > 0 THEN 1 ELSE 0 END) AS RemainingItems,
                    pr.Notes
                FROM dbo.PurchaseRequests pr
                LEFT JOIN dbo.Employees e ON pr.RequestedBy = e.EmployeeID
                LEFT JOIN dbo.Departments d ON pr.DepartmentID = d.DepartmentID
                INNER JOIN dbo.vw_PRRemainingQty v ON pr.RequestID = v.RequestID
                WHERE pr.RequestStatus = 3  -- معتمد
                GROUP BY pr.RequestID, pr.RequestNumber, pr.RequestDate, 
                         pr.RequiredDate, e.FullNameAr, d.DepartmentNameAr, pr.Notes
                HAVING SUM(CASE WHEN v.RemainingToOrder > 0 THEN 1 ELSE 0 END) > 0
                ORDER BY pr.RequestDate DESC";
    var result = await connection.QueryAsync<ApprovedPRForPODto>(sql);
    return result.ToList();
}

// ==========================================
// سطور PR المتبقية لأمر شراء
// ==========================================
public async Task<List<PRLineForPODto>> GetPRLinesForPOAsync(int requestId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PO"));

    using var connection = CreateConnection();
    var sql = @"SELECT 
                    RequestDetailID,
                    RequestID,
                    RequestNumber,
                    LineNumber,
                    ItemID,
                    ItemCode,
                    ItemNameAr,
                    UnitID,
                    UnitName,
                    RequestedQty,
                    ApprovedQty,
                    TotalOrderedQty,
                    RemainingToOrder,
                    EstimatedPrice,
                    CurrentStock,
                    Purpose
                FROM dbo.vw_PRRemainingQty
                WHERE RequestID = @RequestID
                  AND RemainingToOrder > 0
                ORDER BY LineNumber";
    var result = await connection.QueryAsync<PRLineForPODto>(sql, new { RequestID = requestId });
    return result.ToList();
}

// ==========================================
// بحث طلبات الشراء المعتمدة (SearchableSelect)
// ==========================================
public async Task<List<SearchableItem>> GetApprovedPRsSearchableAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PO"));

    using var connection = CreateConnection();
    var sql = @"SELECT DISTINCT
                    pr.RequestID AS Id,
                    pr.RequestNumber + N' — ' + ISNULL(e.FullNameAr, N'') AS Name,
                    pr.RequestNumber AS Code
                FROM dbo.PurchaseRequests pr
                LEFT JOIN dbo.Employees e ON pr.RequestedBy = e.EmployeeID
                INNER JOIN dbo.vw_PRRemainingQty v ON pr.RequestID = v.RequestID
                WHERE pr.RequestStatus = 3
                  AND v.RemainingToOrder > 0
                ORDER BY Name";
    var result = await connection.QueryAsync<SearchableItem>(sql);
    return result.ToList();
}

// ==========================================
// مخازن الاستلام (حسب نوع الصنف)
// ==========================================
public async Task<List<LookupDto>> GetReceivingWarehousesAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PRET"),
        ViewOnly("SCR_PO"),
        ViewOnly("SCR_GRN"));

    using var connection = CreateConnection();
    var sql = @"SELECT WarehouseID AS Id, 
                       WarehouseNameAr AS Name,
                       WarehouseCode AS Code
                FROM dbo.Warehouses 
                WHERE IsActive = 1 
                  AND WarehouseType IN (1, 2, 6) -- خام / تعبئة / عام
                ORDER BY WarehouseNameAr";
    var result = await connection.QueryAsync<LookupDto>(sql);
    return result.ToList();
}

// ==========================================
// سعر صرف العملة
// ==========================================
public async Task<decimal> GetExchangeRateAsync(int currencyId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_SO"),
        ViewOnly("SCR_PO"));

    using var connection = CreateConnection();
    var result = await connection.QueryFirstOrDefaultAsync<decimal?>(
        "SELECT ExchangeRate FROM dbo.Currencies WHERE CurrencyID = @ID",
        new { ID = currencyId });
    return result ?? 1;
}


    // ==========================================
// أوامر الشراء المتاحة للاستلام
// ==========================================
public async Task<List<POForGRNDto>> GetPOsForGRNAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_GRN"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    po.PurchaseOrderID,
                    po.PONumber,
                    po.PODate,
                    s.SupplierNameAr,
                    po.SupplierID,
                    po.WarehouseID,
                    w.WarehouseNameAr,
                    COUNT(pod.PODetailID) AS TotalItems,
                    SUM(CASE WHEN (pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) > 0 THEN 1 ELSE 0 END) AS RemainingItems
                FROM dbo.PurchaseOrders po
                INNER JOIN dbo.Suppliers s ON po.SupplierID = s.SupplierID
                LEFT JOIN dbo.Warehouses w ON po.WarehouseID = w.WarehouseID
                INNER JOIN dbo.PurchaseOrderDetails pod ON po.PurchaseOrderID = pod.PurchaseOrderID
                WHERE po.POStatus IN (3,5, 6)
                  AND pod.LineStatus IN (1, 2)
                GROUP BY po.PurchaseOrderID, po.PONumber, po.PODate,
                         s.SupplierNameAr, po.SupplierID,
                         po.WarehouseID, w.WarehouseNameAr
                HAVING SUM(CASE WHEN (pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) > 0 THEN 1 ELSE 0 END) > 0
                ORDER BY po.PODate DESC";
    var result = await connection.QueryAsync<POForGRNDto>(sql);
    return result.ToList();
}

// ==========================================
// سطور PO المتبقية للاستلام
// ==========================================
public async Task<List<POLineForGRNDto>> GetPOLinesForGRNAsync(int purchaseOrderId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_GRN"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    PODetailID,
                    PurchaseOrderID,
                    PONumber,
                    LineNumber,
                    ItemID,
                    ItemCode,
                    ItemNameAr,
                    UnitID,
                    UnitName,
                    OrderedQty,
                    ReceivedQty,
                    RemainingToReceive,
                    UnitPrice,
                    TaxRate,
                    ShelfLifeDays
                FROM dbo.vw_POLinesForGRN
                WHERE PurchaseOrderID = @POID
                ORDER BY LineNumber";
    var result = await connection.QueryAsync<POLineForGRNDto>(sql, new { POID = purchaseOrderId });
    return result.ToList();
}

// ==========================================
// بحث أوامر الشراء للاستلام (SearchableSelect)
// ==========================================
public async Task<List<SearchableItem>> GetPOsForGRNSearchableAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_GRN"));

    using var connection = CreateConnection();
    var sql = @"SELECT DISTINCT
                    po.PurchaseOrderID AS Id,
                    po.PONumber + N' — ' + s.SupplierNameAr AS Name,
                    po.PONumber AS Code
                FROM dbo.PurchaseOrders po
                INNER JOIN dbo.Suppliers s ON po.SupplierID = s.SupplierID
                INNER JOIN dbo.PurchaseOrderDetails pod ON po.PurchaseOrderID = pod.PurchaseOrderID
                WHERE po.POStatus IN (3, 5, 6)
                  AND pod.LineStatus IN (1, 2)
                  AND (pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) > 0
                ORDER BY Name";
    var result = await connection.QueryAsync<SearchableItem>(sql);
    return result.ToList();
}

    // ==========================================
// أذونات الاستلام المعتمدة المتاحة لفاتورة
// ==========================================
public async Task<List<GRNForInvoiceDto>> GetGRNsForInvoiceAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PINV"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    GRNID, GRNNumber, GRNDate,
                    SupplierNameAr, SupplierID,
                    PurchaseOrderID, PONumber,
                    WarehouseID, WarehouseNameAr,
                    TotalItems, TotalCost
                FROM dbo.vw_GRNsForInvoice
                WHERE IsLinkedToInvoice = 0
                ORDER BY GRNDate DESC";
    var result = await connection.QueryAsync<GRNForInvoiceDto>(sql);
    return result.ToList();
}

// ==========================================
// بحث GRN للفاتورة (SearchableSelect)
// ==========================================
public async Task<List<SearchableItem>> GetGRNsForInvoiceSearchableAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PINV"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    GRNID AS Id,
                    GRNNumber + N' — ' + SupplierNameAr AS Name,
                    GRNNumber AS Code
                FROM dbo.vw_GRNsForInvoice
                WHERE IsLinkedToInvoice = 0
                ORDER BY GRNDate DESC";
    var result = await connection.QueryAsync<SearchableItem>(sql);
    return result.ToList();
}

// ==========================================
// سطور GRN لتحميلها في الفاتورة
// ==========================================
public async Task<List<GRNLineForInvoiceDto>> GetGRNLinesForInvoiceAsync(int grnId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PINV"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    grd.GRNDetailID,
                    grd.GRNID,
                    grn.GRNNumber,
                    grd.ItemID,
                    i.ItemCode,
                    i.ItemNameAr,
                    grd.UnitID,
                    u.UnitNameAr AS UnitName,
                    grn.WarehouseID,
                    ISNULL(grd.AcceptedQty, grd.ReceivedQty) AS ReceivedQty,
                    grd.AcceptedQty,
                    grd.UnitCost,
                    grd.LineTotalCost,
                    grd.BatchNumber,
                    grd.ExpiryDate,
                    grd.PODetailID
                FROM dbo.GoodsReceiptDetails grd
                INNER JOIN dbo.GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
                INNER JOIN dbo.Items i ON grd.ItemID = i.ItemID
                INNER JOIN dbo.Units u ON grd.UnitID = u.UnitID
                WHERE grd.GRNID = @ID
                  AND ISNULL(grd.AcceptedQty, grd.ReceivedQty) > 0
                ORDER BY grd.LineNumber";
    var result = await connection.QueryAsync<GRNLineForInvoiceDto>(sql, new { ID = grnId });
    return result.ToList();
}

    // ==========================================
// بيانات GRN سريعة (عند الاختيار)
// ==========================================
public async Task<GRNForInvoiceDto?> GetGRNQuickInfoAsync(int grnId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PINV"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    grn.GRNID, grn.GRNNumber, grn.GRNDate,
                    s.SupplierNameAr, grn.SupplierID,
                    grn.PurchaseOrderID,
                    po.PONumber,
                    grn.WarehouseID,
                    w.WarehouseNameAr
                FROM dbo.GoodsReceiptNotes grn
                INNER JOIN dbo.Suppliers s ON grn.SupplierID = s.SupplierID
                LEFT JOIN dbo.PurchaseOrders po ON grn.PurchaseOrderID = po.PurchaseOrderID
                LEFT JOIN dbo.Warehouses w ON grn.WarehouseID = w.WarehouseID
                WHERE grn.GRNID = @ID";
    return await connection.QueryFirstOrDefaultAsync<GRNForInvoiceDto>(sql, new { ID = grnId });
}
    // ==========================================
// فواتير المشتريات المتاحة للمرتجع
// ==========================================
public async Task<List<SearchableItem>> GetInvoicesForReturnSearchableAsync()
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PRET"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    InvoiceID AS Id,
                    InvoiceNumber + N' — ' + SupplierNameAr AS Name,
                    InvoiceNumber AS Code
                FROM dbo.vw_InvoicesForReturn
                ORDER BY InvoiceDate DESC";
    var result = await connection.QueryAsync<SearchableItem>(sql);
    return result.ToList();
}

// ==========================================
// بيانات فاتورة سريعة للمرتجع
// ==========================================
public async Task<InvoiceForReturnDto?> GetInvoiceQuickInfoForReturnAsync(int invoiceId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PRET"));

    using var connection = CreateConnection();
    var sql = @"SELECT InvoiceID, InvoiceNumber, InvoiceDate,
                       SupplierNameAr, SupplierID, WarehouseID,
                       TotalAmount, TotalItems
                FROM dbo.vw_InvoicesForReturn
                WHERE InvoiceID = @ID";
    return await connection.QueryFirstOrDefaultAsync<InvoiceForReturnDto>(sql, new { ID = invoiceId });
}

// ==========================================
// سطور الفاتورة المتاحة للمرتجع
// ==========================================
public async Task<List<InvoiceLineForReturnDto>> GetInvoiceLinesForReturnAsync(int invoiceId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_PRET"));

    using var connection = CreateConnection();
    var sql = @"SELECT
                    pid.InvoiceDetailID,
                    pid.InvoiceID,
                    pi.InvoiceNumber,
                    pid.ItemID,
                    i.ItemCode,
                    i.ItemNameAr,
                    pid.UnitID,
                    u.UnitNameAr AS UnitName,
                    pid.Quantity,
                    pid.UnitPrice,
                    pid.DiscountPercent,
                    pid.TaxRate,
                    pid.BatchNumber
                FROM dbo.PurchaseInvoiceDetails pid
                INNER JOIN dbo.PurchaseInvoices pi ON pid.InvoiceID = pi.InvoiceID
                INNER JOIN dbo.Items i ON pid.ItemID = i.ItemID
                INNER JOIN dbo.Units u ON pid.UnitID = u.UnitID
                WHERE pid.InvoiceID = @ID
                ORDER BY pid.LineNumber";
    var result = await connection.QueryAsync<InvoiceLineForReturnDto>(sql, new { ID = invoiceId });
    return result.ToList();
}
    // ==========================================
// أصناف مخزن معين (للتحويل)
// ==========================================
public async Task<List<SearchableItem>> GetWarehouseItemsSearchableAsync(int warehouseId)
{
    await RequireLookupPermissionAsync(
        "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
        ViewOnly("SCR_TRANSFER"));

    using var connection = CreateConnection();
    var sql = @"SELECT 
                    i.ItemID AS Id,
                    i.ItemNameAr AS Name,
                    i.ItemCode AS Code,
                    CAST(ib.AvailableQty AS NVARCHAR(20)) + N' ' + u.UnitNameAr AS Extra
                FROM dbo.InventoryBalance ib
                INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
                INNER JOIN dbo.Units u ON i.PrimaryUnitID = u.UnitID
                WHERE ib.WarehouseID = @WarehouseID
                  AND ib.CurrentQty > 0
                  AND i.IsActive = 1
                ORDER BY i.ItemNameAr";
    var result = await connection.QueryAsync<SearchableItem>(sql, new { WarehouseID = warehouseId });
    return result.ToList();
}
            public async Task<List<LookupDto>> GetDepartmentsLookupAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_LV_BALANCE"),
                ViewOnly("SCR_LOANS"),
                ViewOnly("SCR_LEAVE"),
                ViewOnly("SCR_PENALTY"),
                ViewOnly("SCR_ATTEND"),
                AnyOperation("SCR_EMP"),
                AnyOperation("SCR_DEPARTMENTS"));

            using var connection = CreateConnection();
            var sql = @"SELECT DepartmentID AS Id, DepartmentNameAr AS Name
                        FROM dbo.Departments
                        WHERE IsActive = 1
                        ORDER BY DepartmentNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetJobTitlesLookupAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                AnyOperation("SCR_EMP"));

            using var connection = CreateConnection();
            var sql = @"SELECT JobTitleID AS Id, JobTitleNameAr AS Name
                        FROM dbo.JobTitles
                        WHERE IsActive = 1
                        ORDER BY JobTitleNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public async Task<List<LookupDto>> GetManagersLookupAsync()
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                AnyOperation("SCR_EMP"),
                AnyOperation("SCR_DEPARTMENTS"));

            using var connection = CreateConnection();
            var sql = @"SELECT EmployeeID AS Id, FullNameAr AS Name
                        FROM dbo.Employees
                        WHERE IsActive = 1 AND EmployeeStatus = 1
                        ORDER BY FullNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }
                public async Task<int> GetEmployeeIdByCodeAsync(string code)
        {
            await RequireLookupPermissionAsync(
                "ليس لديك صلاحية استخدام بيانات الاختيار المطلوبة لهذه العملية.",
                ViewOnly("SCR_ATTEND"));

            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT EmployeeID FROM dbo.Employees WHERE EmployeeCode = @Code AND IsActive = 1",
                new { Code = code });
        }

    }
             
}
