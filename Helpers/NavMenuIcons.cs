namespace YKCoatings.Helpers
{
    public static class NavMenuIcons
    {
        private static string Svg(string d) =>
            $"<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'>{d}</svg>";

        public static string Get(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return IBox();
            var c = code.Trim().ToUpperInvariant();

            // كل كود ليه شكل مختلف تماماً - مفيش اتنين شبه بعض
            return c switch
            {
                // ===== البيانات الأساسية =====
                "SCR_ITEMS"       => Svg("<path d='M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8Z'/><path d='M3.3 7 12 12l8.7-5'/><path d='M12 22V12'/>"), // package
                "SCR_CATEGORIES"  => Svg("<rect x='3' y='3' width='7' height='7' rx='1'/><rect x='14' y='3' width='7' height='7' rx='1'/><rect x='14' y='14' width='7' height='7' rx='1'/><rect x='3' y='14' width='7' height='7' rx='1'/>"), // grid 2x2
                "SCR_UNITS"       => Svg("<path d='M12 2 3 7l9 5 9-5-9-5Z'/><path d='M3 7v10l9 5 9-5V7'/><path d='M12 12v10'/>"), // cube
                "SCR_CURRENCIES"  => Svg("<circle cx='12' cy='12' r='8'/><path d='M12 6v12M9 10a3 3 0 0 1 6 0c0 2-3 1-3 3a3 3 0 0 0 6 0'/>"), // dollar
                "SCR_PAYTERMS"    => Svg("<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/>"), // calendar
                "SCR_WAREHOUSES"  => Svg("<path d='M3 9.5 12 3l9 6.5V20a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9.5Z'/><path d='M9 20V12h6v8'/>"), // warehouse
                "SCR_SUPPLIERS"   => Svg("<rect x='1' y='3' width='15' height='13' rx='1'/><path d='M16 8h4l3 6v3h-7V8Z'/><circle cx='5.5' cy='18.5' r='2.5'/><circle cx='18.5' cy='18.5' r='2.5'/>"), // truck
                "SCR_CUSTOMERS"   => Svg("<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 21v-2a4 4 0 0 0-3-3.87'/><path d='M16 3.13a4 4 0 0 1 0 7.75'/>"), // users
                "SCR_PRICELISTS"  => Svg("<path d='M20 12V8H6a2 2 0 0 1-2-2c0-1.1.9-2 2-2h16v12c0 1.1-.9 2-2 2H6a2 2 0 0 1-2-2v-2'/><path d='M12 12a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z'/>"), // tag
                "SCR_SETTINGS"    => Svg("<circle cx='12' cy='12' r='3'/><path d='M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 9 15a1.65 1.65 0 0 0-1-1.51V13a2 2 0 0 1 0-4v.49c.39-.2.8-.33 1.23-.4.43-.08.86-.03 1.27.15.41.18.77.46 1.05.83.28.37.46.8.52 1.26.06.46 0 .93-.18 1.36'/>"), // gear

                // ===== المشتريات =====
                "SCR_PR"          => Svg("<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Z'/><path d='M14 2v6h6'/><path d='M10 13H8'/><path d='M16 17H8'/><path d='M16 9H8'/>"), // file-text PR
                "SCR_PO"          => Svg("<path d='M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z'/><path d='M3 6h18'/><path d='M16 10a4 4 0 0 1-8 0'/>"), // shopping bag PO
                "SCR_GRN"         => Svg("<path d='M16 16 12 12 8 16'/><path d='M12 12v8'/><path d='M20.3 20.3a2.4 2.4 0 0 1-3.4 0L12 15.4l-4.9 4.9a2.4 2.4 0 0 1-3.4-3.4L12 8.6l8.3 8.3a2.4 2.4 0 0 1 0 3.4Z' opacity='.2'/><path d='M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8'/>"), // package incoming
                "SCR_PINV"        => Svg("<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Z'/><path d='M14 2v6h6'/><path d='M8 13h8'/><path d='M8 17h8'/><path d='M8 9h1'/>"), // invoice
                "SCR_PRET"        => Svg("<path d='M3 12a9 9 0 1 0 9-9'/><path d='M3 3v5h5'/><path d='M9 14 11 12 9 10'/>"), // return arrow

                // ===== التصنيع =====
                "SCR_BOM"         => Svg("<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M3 9h18'/><path d='M9 21V9'/><path d='M14 14h4'/><path d='M14 18h4'/>"), // table BOM
                "SCR_PRODORD"     => Svg("<path d='M2 20a8 8 0 0 1 8-8h4a8 8 0 0 1 8 8v2H2v-2Z'/><circle cx='12' cy='8' r='5'/>"), // factory order
                "SCR_PRODORDER"   => Svg("<path d='M2 20a8 8 0 0 1 8-8h4a8 8 0 0 1 8 8v2H2v-2Z'/><circle cx='12' cy='8' r='5'/>"),
                "SCR_MATISS"      => Svg("<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Z'/><path d='M14 2v6h6'/><path d='M12 18v-6'/><path d='M9 15h6'/>"), // issue
                "SCR_MATISSUE"    => Svg("<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Z'/><path d='M14 2v6h6'/><path d='M12 18v-6'/><path d='M9 15h6'/>"),
                "SCR_BATCH"       => Svg("<path d='M12 2 2 7l10 5 10-5-10-5Z'/><path d='M2 17l10 5 10-5'/><path d='M2 12l10 5 10-5'/>"), // layers
                "SCR_PRODBATCH"   => Svg("<path d='M12 2 2 7l10 5 10-5-10-5Z'/><path d='M2 17l10 5 10-5'/><path d='M2 12l10 5 10-5'/>"),
                "SCR_QC"          => Svg("<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/><path d='m9 12 2 2 4-4'/>"), // shield check
                "SCR_CONTRACT"    => Svg("<path d='M11 15h2'/><path d='M12 20a8 8 0 0 0 8-8c0-1.5-.4-3-1.2-4.2'/><path d='M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z'/>"), // pen contract
                "SCR_CONTRACTS"   => Svg("<path d='M11 15h2'/><path d='M12 20a8 8 0 0 0 8-8c0-1.5-.4-3-1.2-4.2'/><path d='M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z'/>"),

                // ===== المخازن =====
                "SCR_STOCK"       => Svg("<path d='M3 3v18h18'/><path d='M18 17V9'/><path d='M13 17V5'/><path d='M8 17v-3'/>"), // bar chart
                "SCR_TRANS"       => Svg("<path d='M7 16V4m0 0L3 8m4-4 4 4'/><path d='M17 8v12m0 0 4-4m-4 4-4-4'/>"), // up down arrows
                "SCR_STOCKTRANS"  => Svg("<path d='M7 16V4m0 0L3 8m4-4 4 4'/><path d='M17 8v12m0 0 4-4m-4 4-4-4'/>"),
                "SCR_TRANSFER"    => Svg("<path d='M17 1 21 5 17 9'/><path d='M3 11V9a4 4 0 0 1 4-4h14'/><path d='M7 23 3 19 7 15'/><path d='M21 13v2a4 4 0 0 1-4 4H3'/>"), // transfer
                "SCR_COUNT"       => Svg("<path d='M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2'/><rect x='9' y='3' width='6' height='4' rx='1'/><path d='M9 12h6'/><path d='M9 16h6'/>"), // clipboard

                // ===== المبيعات =====
                "SCR_QUOT"        => Svg("<path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Z'/><path d='M14 2v6h6'/><path d='M10 13H8'/><path d='M16 13H10'/>"), // quotation
                "SCR_SO"          => Svg("<path d='M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z'/><path d='M3 6h18'/><path d='M16 10a4 4 0 0 1-8 0'/>"), // sales order
                "SCR_SINV"        => Svg("<path d='M4 2v20l3-2.5L10 22l3-2.5L16 22l3-2.5L22 22V2l-3 2.5L16 2l-3 2.5L10 2 7 4.5 4 2Z'/><path d='M8 10h8'/><path d='M8 14h8'/>"), // sales inv
                "SCR_SRET"        => Svg("<path d='M9 14 4 9l5-5'/><path d='M20 20v-7a4 4 0 0 0-4-4H4'/>"), // sales return
                "SCR_RECV"        => Svg("<rect x='2' y='5' width='20' height='14' rx='2'/><path d='M2 10h20'/><circle cx='12' cy='14' r='1'/>"), // receipt voucher

                // ===== الحسابات =====
                "SCR_COA"         => Svg("<path d='M4 19.5A2.5 2.5 0 0 1 6.5 17H20'/><path d='M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z'/>"), // book
                "SCR_JV"          => Svg("<path d='M12 7v14'/><path d='M16 12h2'/><path d='M16 8h2'/><path d='M3 7a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z'/><path d='M21 7a2 2 0 0 0-2-2h-6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2Z'/>"), // journal
                "SCR_PAYV"        => Svg("<rect x='2' y='5' width='20' height='14' rx='2'/><path d='M16 14a2 2 0 0 0 0 4h4v-4h-4Z'/>"), // payment voucher
                "SCR_BANKS"       => Svg("<path d='M3 21h18'/><path d='M3 10h18'/><path d='M5 6 12 2l7 4'/><path d='M7 10v11'/><path d='M12 10v11'/><path d='M17 10v11'/>"), // bank
                "SCR_CHEQUES"     => Svg("<rect x='2' y='6' width='20' height='12' rx='2'/><path d='M2 10h20'/><path d='M7 15h3'/><path d='M12 15h2'/>"), // cheque
                "SCR_COSTCARD"    => Svg("<path d='M12 2 2 7l10 5 10-5-10-5Z'/><path d='M2 17l10 5 10-5'/><path d='M2 12l10 5 10-5'/>"), // cost layers

                // ===== الموارد البشرية =====
                "SCR_DEPARTMENTS" => Svg("<path d='M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z'/><path d='M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2'/><path d='M18 9h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-2'/>"), // departments building
                "SCR_DEPT"        => Svg("<path d='M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z'/><path d='M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2'/>"),
                "SCR_JOBTITLES"   => Svg("<rect x='2' y='7' width='20' height='14' rx='2'/><path d='M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2'/>"), // briefcase
                "SCR_JOBTITLE"    => Svg("<rect x='2' y='7' width='20' height='14' rx='2'/><path d='M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2'/>"),
                "SCR_EMP"         => Svg("<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/>"), // single user
                "SCR_ATTEND"      => Svg("<circle cx='12' cy='12' r='10'/><path d='M12 6v6l4 2'/>"), // clock
                "SCR_LEAVE"       => Svg("<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M16 2v4'/><path d='M8 2v4'/><path d='M3 10h18'/><path d='M12 14l-2 2 2 2 4-4'/>"), // leave calendar
                "SCR_LEAVES"      => Svg("<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M16 2v4'/><path d='M8 2v4'/><path d='M3 10h18'/>"),
                "SCR_PAYROLL"     => Svg("<rect x='2' y='7' width='20' height='14' rx='2'/><path d='M16 7V5a2 2 0 0 1 2-2h4v6h-6Z'/><path d='M2 7v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V7'/>"), // wallet payroll
                "SCR_LOANS"       => Svg("<path d='M12 2v20'/><path d='M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6'/>"), // dollar loans
                "SCR_PENALTY"     => Svg("<path d='M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z'/><path d='M12 9v4'/><path d='M12 17h.01'/>"), // triangle alert
                "SCR_PENALTIES"   => Svg("<path d='M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z'/>"),
                "SCR_HOLIDAYS"    => Svg("<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/><path d='M12 14a1 1 0 1 0 0 2 1 1 0 0 0 0-2Z'/>"), // holiday
                "SCR_PUBLICHOLIDAYS"=> Svg("<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/>"),
                "SCR_LV_BALANCE"  => Svg("<path d='M12 3v19'/><path d='M5 3a2 2 0 0 0-2 2v4a2 2 0 0 0 2 2h4a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2H5Z'/><path d='M19 3a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4Z'/>"), // balance scale
                "SCR_LEAVEBALANCE"=> Svg("<path d='M12 3v19'/><path d='M5 3a2 2 0 0 0-2 2v4a2 2 0 0 0 2 2h4a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2H5Z'/>"),
                "SCR_LV_TYPE"     => Svg("<path d='M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2'/><rect x='9' y='3' width='6' height='4' rx='1'/>"), // type list
                "SCR_LEAVETYPE"   => Svg("<path d='M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2'/><rect x='9' y='3' width='6' height='4' rx='1'/>"),

                // ===== إدارة النظام =====
                "SCR_USERS"       => Svg("<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 11a3 3 0 0 0-3-3v0'/><path d='M16 3.13a4 4 0 0 1 0 7.75'/>"), // users cog
                "SCR_ROLES"       => Svg("<rect x='3' y='11' width='18' height='11' rx='2'/><path d='M7 11V7a5 5 0 0 1 10 0v4'/>"), // lock roles
                "SCR_AUDIT"       => Svg("<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/><path d='M9 12h6'/>"), // audit shield
                "SCR_AUDITLOG"    => Svg("<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/>"),
                "SCR_LOGINLOG"    => Svg("<path d='M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1 2 2h-4'/><path d='M10 17l5-5-5-5'/><path d='M15 12H3'/>"), // login
                "SCR_LOGINHISTORY"=> Svg("<path d='M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4'/><path d='M10 17l5-5-5-5'/>"),

                _ => DistinctFallback(c)
            };
        }

        // أيقونات مختلفة تماماً للاكواد الغير معروفة - كل كود ياخد أيقونة مختلفة بالهاش
        private static string DistinctFallback(string code)
        {
            var pool = new string[]
            {
                Svg("<circle cx='12' cy='12' r='9'/><path d='M12 8v4'/><path d='M12 16h.01'/>"), // circle alert
                Svg("<path d='M12 2 2 7l10 5 10-5-10-5Z'/><path d='M2 17l10 5 10-5'/><path d='M2 12l10 5 10-5'/>"), // layers
                Svg("<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M3 9h18'/>"), // rect
                Svg("<path d='M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2Z'/><path d='M14 2v6h6'/>"), // file
                Svg("<circle cx='12' cy='12' r='10'/><path d='M12 16a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z'/><path d='M12 8v4'/>"), // info
                Svg("<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/>"), // user
                Svg("<path d='M3 9h18v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V9Z'/>"), // box
                Svg("<path d='M3 3v18h18'/><path d='M18 17V9'/>"), // chart
                Svg("<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/>"), // shield
                Svg("<rect x='2' y='7' width='20' height='14' rx='2'/>"), // wallet
            };
            int hash = code.GetHashCode();
            int idx = Math.Abs(hash) % pool.Length;
            return pool[idx];
        }

        private static string IBox() => Svg("<rect x='3' y='3' width='18' height='18' rx='3'/><path d='M9 9h6'/><path d='M9 15h6'/>");
    }
}
