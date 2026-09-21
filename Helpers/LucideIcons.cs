namespace YKCoatings.Helpers
{
    public static class LucideIcons
    {
        // Mapping SCR codes to Lucide icon names - Gold Luxury Line Icons
        // https://lucide.dev/icons
        public static string GetIconName(string? code) => (code?.ToUpper()) switch
        {
            // Dashboard
            "SCR_DASHBOARD" or "DASHBOARD" => "layout-dashboard",

            // البيانات الأساسية
            "SCR_ITEMS" => "package",
            "SCR_CATEGORIES" => "layout-grid",
            "SCR_UNITS" => "ruler",
            "SCR_CURRENCIES" => "coins",
            "SCR_PAYTERMS" => "calendar-clock",
            "SCR_WAREHOUSES" => "warehouse",
            "SCR_SUPPLIERS" => "truck",
            "SCR_CUSTOMERS" => "users-round",
            "SCR_PRICELISTS" => "tags",
            "SCR_SETTINGS" => "settings-2",

            // المشتريات
            "SCR_PR" => "clipboard-list",
            "SCR_PO" => "shopping-cart",
            "SCR_GRN" => "package-check",
            "SCR_PINV" => "receipt-text",
            "SCR_PRET" => "undo-2",

            // التصنيع - مميز جداً
            "SCR_BOM" => "boxes",
            "SCR_PRODORD" or "SCR_PROD" => "factory",
            "SCR_MATISS" or "SCR_MATISSUE" => "package-minus",
            "SCR_BATCH" or "SCR_PRODBATCH" => "layers",
            "SCR_QC" => "shield-check",
            "SCR_CONTRACT" => "handshake",

            // المخازن
            "SCR_STOCK" => "bar-chart-3",
            "SCR_TRANS" => "arrow-left-right",
            "SCR_TRANSFER" => "move",
            "SCR_COUNT" => "clipboard-check",

            // المبيعات
            "SCR_QUOT" => "file-text",
            "SCR_SO" => "shopping-bag",
            "SCR_SINV" => "file-check",
            "SCR_SRET" => "rotate-ccw",
            "SCR_RECV" => "banknote",

            // الحسابات
            "SCR_COA" => "book-open",
            "SCR_JV" => "notebook-pen",
            "SCR_PAYV" => "credit-card",
            "SCR_BANKS" => "landmark",
            "SCR_CHEQUES" => "ticket",
            "SCR_COSTCARD" => "calculator",

            // الموارد البشرية
            "SCR_DEPARTMENTS" => "building-2",
            "SCR_JOBTITLES" => "briefcase-business",
            "SCR_EMP" => "users",
            "SCR_ATTEND" => "clock-4",
            "SCR_LEAVE" or "SCR_LEAVES" => "calendar-days",
            "SCR_PAYROLL" => "wallet",
            "SCR_LOANS" => "hand-coins",
            "SCR_PENALTY" => "triangle-alert",
            "SCR_HOLIDAYS" => "calendar-off",
            "SCR_LV_BALANCE" => "scale",
            "SCR_LV_TYPE" => "list-checks",

            // النظام
            "SCR_USERS" => "user-cog",
            "SCR_ROLES" => "shield-user",
            "SCR_AUDIT" => "shield-check",
            "SCR_LOGINLOG" => "log-in",
            "SCR_REPORTS" => "chart-pie",

            _ => "box"
        };

        // Gold color - always gold luxury
        public static string GetColor(string? code) => "#D4AF37";
    }
}
