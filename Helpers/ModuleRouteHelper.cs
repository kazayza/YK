namespace YKCoatings.Helpers
{
    public static class ModuleRouteHelper
    {
        public static string GetRoute(string moduleCode)
        {
            return moduleCode?.ToUpper() switch
            {
                // البيانات الأساسية
                "SCR_ITEMS"      => "/items",
                "SCR_CATEGORIES" => "/categories",
                "SCR_UNITS"      => "/units",
                "SCR_CURRENCIES"=> "/currencies",
                "SCR_PAYTERMS"=> "/payment-terms",
                "SCR_WAREHOUSES" => "/warehouses",
                "SCR_SUPPLIERS"  => "/suppliers",
                "SCR_CUSTOMERS"  => "/customers",
                "SCR_PRICELISTS" => "/pricelists",
                "SCR_SETTINGS"   => "/settings",

                // المشتريات
                "SCR_PR"   => "/purchase-requests",
                "SCR_PO"   => "/purchase-orders",
                "SCR_GRN"  => "/goods-receipts",
                "SCR_PINV" => "/purchase-invoices",
                "SCR_PRET" => "/purchase-returns",

                // التصنيع
                "SCR_BOM"     => "/boms",
                "SCR_PRODORD" => "/production-orders",
                "SCR_MATISS"  => "/material-issues",
                "SCR_BATCH"   => "/production-batches",
                "SCR_QC"      => "/quality-control",
                "SCR_CONTRACT" => "/contract-orders",

                // المخازن
                "SCR_STOCK"    => "/stock-balances",
                "SCR_TRANS"    => "/stock-movements",
                "SCR_TRANSFER" => "/stock-transfers",
                "SCR_COUNT"    => "/stock-counts",

                // المبيعات
                "SCR_QUOT" => "/quotations",
                "SCR_SO"   => "/sales-orders",
                "SCR_SINV" => "/sales-invoices",
                "SCR_SRET" => "/sales-returns",
                "SCR_RECV" => "/receipt-vouchers",

                // الحسابات
                "SCR_COA"      => "/chart-of-accounts",
                "SCR_JV"       => "/journal-entries",
                "SCR_PAYV"     => "/payment-vouchers",
                "SCR_BANKS"    => "/banks",
                "SCR_CHEQUES"  => "/cheques",
                "SCR_COSTCARD" => "/cost-card",

                // الموارد البشرية
                "SCR_DEPARTMENTS"    => "/departments",
                "SCR_JOBTITLES"=> "/jobtitles",
                "SCR_EMP"     => "/employees",
                "SCR_ATTEND"  => "/attendance",
                "SCR_LEAVE"   => "/leaves",
                "SCR_PAYROLL" => "/payroll",
                "SCR_LOANS"   => "/loans",
                "SCR_PENALTY" => "/penalties",
                "SCR_HOLIDAYS"=> "/holidays",
                "SCR_LV_BALANCE"=> "/leave-balances",
                "SCR_LV_TYPE"=> "/leavetypes",

                // إدارة النظام
                "SCR_USERS"    => "/users",
                "SCR_ROLES"    => "/roles",
                "SCR_AUDIT"    => "/audit-log",
                "SCR_LOGINLOG" => "/login-history",
                //"SCR_BACKUP"   => "/backup",
                //"SCR_SYSSET"   => "/system-settings",

                _ => $"/module/{moduleCode}"
            };
        }
    }
}