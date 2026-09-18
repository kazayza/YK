namespace YKCoatings.Helpers
{
    public static class NavMenuIcons
    {
        private static string Svg(string paths) =>
            $"<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' " +
            $"stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{paths}</svg>";

        public static string Home() => Svg(
            "<path d='M3 10.5L12 3l9 7.5V21a1 1 0 01-1 1H4a1 1 0 01-1-1V10.5z'/>" +
            "<path d='M9 21V14h6v7'/>");

       public static string Get(string? code) => (code?.ToUpper()) switch
{
    // ═══ البيانات الأساسية ═══
    "SCR_ITEMS" => Svg(
        "<rect x='2' y='7' width='20' height='14' rx='2'/>" +
        "<path d='M16 7V5a4 4 0 00-8 0v2'/>" +
        "<circle cx='12' cy='15' r='2.5'/>"),

    "SCR_CATEGORIES" => Svg(
        "<rect x='3' y='3' width='7' height='7' rx='1.5'/>" +
        "<rect x='14' y='3' width='7' height='7' rx='1.5'/>" +
        "<rect x='3' y='14' width='7' height='7' rx='1.5'/>" +
        "<rect x='14' y='14' width='7' height='7' rx='1.5'/>"),

    "SCR_UNITS" => Svg(
        "<path d='M12 2L3 7v10l9 5 9-5V7l-9-5z'/>" +
        "<path d='M12 12l9-5'/>" +
        "<path d='M12 12v10'/>" +
        "<path d='M12 12L3 7'/>"),

    "SCR_CURRENCIES" => Svg(
        "<circle cx='12' cy='12' r='9'/>" +
        "<path d='M14.5 8.5c-.5-1-1.5-1.5-2.5-1.5-1.7 0-3 1.3-3 3s1.3 3 3 3c1 0 2 .5 2.5 1.5'/>" +
        "<path d='M12 6v1m0 10v1'/>"),

    "SCR_PAYTERMS" => Svg(
        "<rect x='3' y='4' width='18' height='16' rx='2'/>" +
        "<path d='M3 9h18'/>" +
        "<path d='M8 2v4m8-4v4'/>" +
        "<path d='M8 13h2m4 0h2'/>" +
        "<path d='M8 16h4'/>"),

    "SCR_PRICELISTS" => Svg(
        "<path d='M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2'/>" +
        "<rect x='9' y='3' width='6' height='4' rx='1'/>" +
        "<path d='M9 12h6'/>" +
        "<path d='M9 16h4'/>" +
        "<path d='M14 14l2 2'/>"),

    "SCR_SETTINGS" => Svg(
        "<circle cx='12' cy='12' r='3'/>" +
        "<path d='M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z'/>"),

    // ═══ المخازن ═══
    "SCR_WAREHOUSES" => Svg(
        "<path d='M3 21V8l9-5 9 5v13'/>" +
        "<path d='M9 21v-6h6v6'/>" +
        "<path d='M3 8h18'/>"),

    "SCR_STOCK" => Svg(
        "<path d='M6 20V14'/>" +
        "<path d='M10 20V10'/>" +
        "<path d='M14 20V6'/>" +
        "<path d='M18 20V2'/>"),

    "SCR_TRANS" => Svg(
        "<rect x='2' y='3' width='20' height='18' rx='2'/>" +
        "<path d='M2 9h20'/>" +
        "<path d='M2 15h20'/>" +
        "<path d='M9 3v18'/>"),

    "SCR_TRANSFER" => Svg(
        "<path d='M7 16V4m0 0L3 8m4-4l4 4'/>" +
        "<path d='M17 8v12m0 0l4-4m-4 4l-4-4'/>"),

    "SCR_COUNT" => Svg(
        "<rect x='5' y='2' width='14' height='20' rx='2'/>" +
        "<path d='M9 6h6'/>" +
        "<path d='M9 10h6'/>" +
        "<path d='M9 14h4'/>" +
        "<circle cx='15' cy='18' r='1' fill='currentColor'/>"),

    // ═══ المشتريات ═══
    "SCR_PR" => Svg(
        "<circle cx='9' cy='21' r='1.5'/>" +
        "<circle cx='20' cy='21' r='1.5'/>" +
        "<path d='M1 1h4l2.68 13.39a2 2 0 002 1.61h9.72a2 2 0 001.92-1.44L23 6H6'/>"),

    "SCR_PO" => Svg(
        "<path d='M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8l-6-6z'/>" +
        "<path d='M14 2v6h6'/>" +
        "<path d='M8 13h2v4H8z'/>" +
        "<path d='M14 11h2v6h-2z'/>"),

    "SCR_GRN" => Svg(
        "<rect x='2' y='3' width='20' height='18' rx='2'/>" +
        "<path d='M8 12l3 3 5-5'/>" +
        "<path d='M2 8h20'/>"),

    "SCR_PINV" => Svg(
        "<path d='M4 2h16v20l-3-2-3 2-3-2-3 2V2z'/>" +
        "<path d='M8 7h8'/>" +
        "<path d='M8 11h8'/>" +
        "<path d='M8 15h5'/>"),

    "SCR_PRET" => Svg(
        "<polyline points='1 4 1 10 7 10'/>" +
        "<path d='M3.51 15a9 9 0 102.13-9.36L1 10'/>" +
        "<path d='M12 7v5l3 3'/>"),

    // ═══ التصنيع ═══
    "SCR_BOM" => Svg(
        "<rect x='2' y='3' width='8' height='5' rx='1'/>" +
        "<rect x='14' y='3' width='8' height='5' rx='1'/>" +
        "<rect x='8' y='15' width='8' height='5' rx='1'/>" +
        "<path d='M6 8v4h12V8'/>" +
        "<path d='M12 12v3'/>"),

    "SCR_PRODORD" => Svg(
        "<rect x='2' y='2' width='20' height='20' rx='2'/>" +
        "<path d='M7 12l3 3 5-6'/>" +
        "<path d='M2 8h20'/>"),

    "SCR_MATISS" => Svg(
        "<path d='M5 8h14'/>" +
        "<path d='M5 12h14'/>" +
        "<path d='M5 16h7'/>" +
        "<path d='M19 14l2 2-2 2'/>" +
        "<path d='M21 16h-5'/>"),

    "SCR_BATCH" => Svg(
        "<rect x='3' y='3' width='7' height='7' rx='1'/>" +
        "<rect x='14' y='3' width='7' height='7' rx='1'/>" +
        "<rect x='3' y='14' width='7' height='7' rx='1'/>" +
        "<path d='M14 17h7m-3-3v6'/>"),

    "SCR_QC" => Svg(
        "<path d='M12 2l8 4v6c0 5.25-3.5 10-8 11.5C7.5 22 4 17.25 4 12V6l8-4z'/>" +
        "<path d='M9 12l2 2.5L15 10'/>"),

    // ═══ المبيعات ═══
    "SCR_QUOT" => Svg(
        "<path d='M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8l-6-6z'/>" +
        "<path d='M14 2v6h6'/>" +
        "<path d='M10 13a2 2 0 104 0 2 2 0 00-4 0'/>" +
        "<path d='M13.4 15l1.6 1.6'/>"),

    "SCR_SO" => Svg(
        "<path d='M6 2L3 6v14a2 2 0 002 2h14a2 2 0 002-2V6l-3-4z'/>" +
        "<path d='M3 6h18'/>" +
        "<path d='M16 10a4 4 0 01-8 0'/>"),

    "SCR_SINV" => Svg(
        "<path d='M4 2h16v20l-3-2-3 2-3-2-3 2V2z'/>" +
        "<path d='M8 7h8'/>" +
        "<path d='M8 11h5'/>" +
        "<path d='M14 14l2 2-2 2'/>"),

    "SCR_SRET" => Svg(
        "<path d='M3 12a9 9 0 109 9'/>" +
        "<path d='M3 3v9h9'/>" +
        "<path d='M9 15l3 3-3 3'/>"),

    "SCR_RECV" => Svg(
        "<rect x='2' y='5' width='20' height='14' rx='2'/>" +
        "<path d='M2 10h20'/>" +
        "<path d='M6 15h4'/>" +
        "<path d='M14 15h4'/>"),

    // ═══ الأطراف ═══
    "SCR_SUPPLIERS" => Svg(
        "<rect x='1' y='3' width='15' height='13' rx='2'/>" +
        "<path d='M16 8h4l3 3v5h-7V8z'/>" +
        "<circle cx='6.5' cy='19.5' r='2.5'/>" +
        "<circle cx='19.5' cy='19.5' r='2.5'/>"),

    "SCR_CUSTOMERS" => Svg(
        "<circle cx='9' cy='7' r='4'/>" +
        "<path d='M2 21v-2a5 5 0 015-5h4a5 5 0 015 5v2'/>" +
        "<circle cx='19' cy='8' r='2.5'/>" +
        "<path d='M19 14c2.5 0 4 1.5 4 4v3'/>"),

    // ═══ المحاسبة ═══
    "SCR_COA" => Svg(
        "<rect x='2' y='3' width='20' height='16' rx='2'/>" +
        "<path d='M2 8h20'/>" +
        "<path d='M8 21h8'/>" +
        "<path d='M12 19v2'/>" +
        "<path d='M7 12h3'/>" +
        "<path d='M7 15h6'/>"),

    "SCR_JV" => Svg(
        "<path d='M4 4h16a2 2 0 012 2v12a2 2 0 01-2 2H4a2 2 0 01-2-2V6a2 2 0 012-2z'/>" +
        "<path d='M2 10h20'/>" +
        "<path d='M12 4v16'/>" +
        "<path d='M7 7h2'/>" +
        "<path d='M15 7h2'/>"),

    "SCR_PAYV" => Svg(
        "<rect x='2' y='5' width='20' height='14' rx='2'/>" +
        "<path d='M2 10h20'/>" +
        "<circle cx='12' cy='15' r='2'/>" +
        "<path d='M6 15h2m8 0h2'/>"),

    "SCR_BANKS" => Svg(
        "<path d='M3 21h18'/>" +
        "<path d='M3 10h18'/>" +
        "<path d='M5 6l7-3 7 3'/>" +
        "<path d='M4 10v11m4-11v11m4-11v11m4-11v11m4-11v11'/>"),

    "SCR_CHEQUES" => Svg(
        "<rect x='2' y='6' width='20' height='12' rx='2'/>" +
        "<path d='M2 10h20'/>" +
        "<path d='M6 14h4'/>" +
        "<path d='M14 14h4'/>" +
        "<path d='M6 17h2'/>"),

    "SCR_COSTCARD" => Svg(
        "<path d='M12 2L2 7l10 5 10-5-10-5z'/>" +
        "<path d='M2 17l10 5 10-5'/>" +
        "<path d='M2 12l10 5 10-5'/>"),

    // ═══ الموارد البشرية ═══
    "SCR_DEPARTMENTS" => Svg(
        "<rect x='2' y='2' width='8' height='6' rx='1'/>" +
        "<rect x='14' y='2' width='8' height='6' rx='1'/>" +
        "<rect x='8' y='14' width='8' height='6' rx='1'/>" +
        "<path d='M6 8v3h12V8'/>" +
        "<path d='M12 11v3'/>"),

    "SCR_JOBTITLES" => Svg(
        "<rect x='3' y='7' width='18' height='13' rx='2'/>" +
        "<path d='M8 7V5a2 2 0 012-2h4a2 2 0 012 2v2'/>" +
        "<path d='M12 12v4'/>" +
        "<path d='M8 14h8'/>"),

    "SCR_EMP" => Svg(
        "<circle cx='12' cy='7' r='4'/>" +
        "<path d='M4 21v-2a6 6 0 0112 0v2'/>" +
        "<path d='M20 8v6'/>" +
        "<path d='M17 11h6'/>"),

    "SCR_ATTEND" => Svg(
        "<rect x='3' y='4' width='18' height='16' rx='2'/>" +
        "<path d='M3 9h18'/>" +
        "<path d='M8 2v4m8-4v4'/>" +
        "<path d='M9 14l2 2 4-4'/>"),

    "SCR_LEAVE" => Svg(
        "<rect x='3' y='4' width='18' height='16' rx='2'/>" +
        "<path d='M3 9h18'/>" +
        "<path d='M8 2v4m8-4v4'/>" +
        "<path d='M8 13h8'/>" +
        "<path d='M8 16h5'/>"),

    "SCR_PAYROLL" => Svg(
        "<rect x='2' y='5' width='20' height='14' rx='2'/>" +
        "<path d='M2 10h20'/>" +
        "<path d='M6 14h3'/>" +
        "<path d='M13 14c0-1.1.9-2 2-2h2'/>" +
        "<path d='M13 17h6'/>"),

    "SCR_LOANS" => Svg(
        "<path d='M12 2v20'/>" +
        "<path d='M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6'/>"),

    "SCR_PENALTY" => Svg(
        "<path d='M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z'/>" +
        "<path d='M12 9v4'/>" +
        "<circle cx='12' cy='17' r='1' fill='currentColor'/>"),

    "SCR_HOLIDAYS" => Svg(
        "<rect x='3' y='4' width='18' height='16' rx='2'/>" +
        "<path d='M3 9h18'/>" +
        "<path d='M8 2v4m8-4v4'/>" +
        "<path d='M12 13a1 1 0 100 2 1 1 0 000-2z' fill='currentColor'/>"),

    "SCR_LV_BALANCE" => Svg(
        "<path d='M12 2L2 7l10 5 10-5-10-5z'/>" +
        "<path d='M2 17l10 5 10-5'/>" +
        "<path d='M2 12l10 5 10-5'/>"),

    "SCR_LV_TYPE" => Svg(
        "<path d='M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2'/>" +
        "<rect x='9' y='3' width='6' height='4' rx='1'/>" +
        "<path d='M9 12h6'/>" +
        "<path d='M9 16h4'/>"),

    // ═══ إدارة النظام ═══
    "SCR_USERS" => Svg(
        "<circle cx='9' cy='7' r='4'/>" +
        "<path d='M2 21v-2a5 5 0 015-5h4a5 5 0 015 5v2'/>" +
        "<path d='M19 11l2 2 3-3'/>"),

    "SCR_ROLES" => Svg(
        "<rect x='3' y='11' width='18' height='11' rx='2'/>" +
        "<path d='M7 11V7a5 5 0 0110 0v4'/>" +
        "<circle cx='12' cy='16' r='1.5' fill='currentColor'/>"),

    "SCR_AUDIT" => Svg(
        "<path d='M12 2l8 4v6c0 5.25-3.5 10-8 11.5C7.5 22 4 17.25 4 12V6l8-4z'/>" +
        "<path d='M9 12l2 2.5L15 10'/>"),

    "SCR_LOGINLOG" => Svg(
        "<path d='M15 3h4a2 2 0 012 2v14a2 2 0 01-2 2h-4'/>" +
        "<path d='M10 17l5-5-5-5'/>" +
        "<path d='M15 12H3'/>"),

    // ═══ Default ═══
    _ => Svg(
        "<rect x='3' y='3' width='18' height='18' rx='3'/>" +
        "<path d='M9 9h6'/>" +
        "<path d='M9 12h6'/>" +
        "<path d='M9 15h4'/>")
};

        public static string GetColor(string? code) => (code?.ToUpper()) switch
{
    // البيانات الأساسية - أزرق
    "SCR_ITEMS" or "SCR_CATEGORIES" or "SCR_UNITS" or
    "SCR_CURRENCIES" or "SCR_PAYTERMS" or "SCR_PRICELISTS" or "SCR_SETTINGS"
        => "#3b82f6",

    // المخازن - أخضر
    "SCR_WAREHOUSES" or "SCR_STOCK" or "SCR_TRANS" or
    "SCR_TRANSFER" or "SCR_COUNT"
        => "#10b981",

    // المشتريات - بنفسجي
    "SCR_PR" or "SCR_PO" or "SCR_GRN" or
    "SCR_PINV" or "SCR_PRET"
        => "#3b82f6",

    // المبيعات - سماوي
    "SCR_QUOT" or "SCR_SO" or "SCR_SINV" or
    "SCR_SRET" or "SCR_RECV"
        => "#06b6d4",

    // الموردين - أحمر
    "SCR_SUPPLIERS" => "#ef4444",

    // العملاء - تركواز
    "SCR_CUSTOMERS" => "#14b8a6",

    // المحاسبة - برتقالي
    "SCR_COA" or "SCR_JV" or "SCR_PAYV" or
    "SCR_BANKS" or "SCR_CHEQUES" or "SCR_COSTCARD"
        => "#f97316",

    // التصنيع - أصفر
    "SCR_BOM" or "SCR_PRODORD" or "SCR_MATISS" or
    "SCR_BATCH" or "SCR_QC"
        => "#eab308",

    // الموارد البشرية - وردي
    "SCR_DEPARTMENTS" or "SCR_JOBTITLES" or "SCR_EMP" or
    "SCR_ATTEND" or "SCR_LEAVE" or "SCR_PAYROLL" or
    "SCR_LOANS" or "SCR_PENALTY" or "SCR_HOLIDAYS" or
    "SCR_LV_BALANCE" or "SCR_LV_TYPE"
        => "#ec4899",

    // إدارة النظام - رمادي
    "SCR_USERS" or "SCR_ROLES" or "SCR_AUDIT" or "SCR_LOGINLOG"
        => "#64748b",

    _ => "#6366f1"
};

        public static string GetGroupLabel(string? code) => (code?.ToUpper()) switch
        {
            "SCR_ITEMS" or "SCR_CATEGORIES" or "SCR_UNITS"
                => "INVENTORY",
            "SCR_WAREHOUSES" or "SCR_STOCK" or "SCR_TRANS" or "SCR_TRANSFER" or "SCR_COUNT"
                => "WAREHOUSE",
            "SCR_PR" or "SCR_PO" or "SCR_GRN"
                => "PURCHASING",
            "SCR_PINV" or "SCR_PRET"
                => "INVOICES",
            "SCR_SUPPLIERS"
                => "SUPPLIERS",
            "SCR_CUSTOMERS"
                => "CUSTOMERS",
            "SCR_COA" or "SCR_JV"
                => "FINANCE",
            "SCR_EMP"
                => "HR",
            "SCR_USERS" or "SCR_ROLES" or "SCR_AUDIT"
                => "ADMIN",
            "SCR_REPORTS"
                => "REPORTS",
            _ => "OTHER"
        };
    }
}