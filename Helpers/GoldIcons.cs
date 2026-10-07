namespace YKCoatings.Helpers
{
    /// <summary>
    /// Gold Edition Professional Icon Library
    /// مزيج من أيقونات التصنيع الاحترافية + Line Gold Thin 1.5px
    /// الأساس الموحد لكل المشروع - بدون إيموجي نهائياً
    /// </summary>
    public static class GoldIcons
    {
        private static string Svg(string d, int size = 24) =>
            $"<svg viewBox='0 0 24 24' width='{size}' height='{size}' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'>{d}</svg>";

        private static string SvgSmall(string d) => Svg(d, 16);
        private static string SvgMedium(string d) => Svg(d, 18);
        private static string SvgLarge(string d) => Svg(d, 20);

        // ===== Manufacturing Module - الأساس الاحترافي =====
        public static class Manufacturing
        {
            // BOM - من BOMList bi-diagram-3-fill -> line gold boxes
            public static string BOM => Svg("<path d='M12 2 2 7l10 5 10-5-10-5Z'/><path d='M2 17l10 5 10-5'/><path d='M2 12l10 5 10-5'/><path d='M12 12v10'/><path d='M8 10h8'/><path d='M8 14h8'/>");
            // Production Order - Factory - من ProductionOrders bi-gear-wide-connected
            public static string ProductionOrder => Svg("<path d='M3 21h18'/><path d='M6 21V9l6-4 6 4v12'/><path d='M9 9h1'/><path d='M13 9h1'/><path d='M9 13h1'/><path d='M13 13h1'/><path d='M9 17h1'/><path d='M13 17h1'/><circle cx='18' cy='11' r='3'/><path d='M18 9.5v1'/><path d='M18 12.5v1'/>");
            // Batch - Layers - من ProductionBatches bi-layers-fill
            public static string Batch => Svg("<path d='M12 2 2 7l10 5 10-5-10-5Z'/><path d='M2 17l10 5 10-5'/><path d='M2 12l10 5 10-5'/>");
            // Material Issue - Package Minus - من MaterialIssues bi-box-arrow-up-right
            public static string MaterialIssue => Svg("<path d='M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8Z'/><path d='M3.3 7 12 12l8.7-5'/><path d='M12 22V12'/><path d='M8 15h8'/>");
            // QC - Shield Check - أفضل أيقونة
            public static string QC => Svg("<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/><path d='m9 12 2 2 4-4'/>");
            // Factory building
            public static string Factory => Svg("<path d='M2 20a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8l-7-5-9 5v12Z'/><path d='M6 8h4v4H6z'/><path d='M14 8h4v4h-4z'/><path d='M6 14h4v4H6z'/><path d='M14 14h4v4h-4z'/>");
            // Gear
            public static string Gear => Svg("<circle cx='12' cy='12' r='3'/><path d='M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 9 15a1.65 1.65 0 0 0-1-1.51V13a2 2 0 0 1 0-4v.49c.39-.2.8-.33 1.23-.4'/>");
        }

        // ===== Status Icons - بدون إيموجي =====
        public static class Status
        {
            public static string Draft => Svg("<circle cx='12' cy='12' r='10'/><path d='M12 8v4'/><path d='M12 16h.01'/>", 16);
            public static string Active => Svg("<path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'/><path d='M22 4 12 14.01l-3-3'/>", 16);
            public static string Paused => Svg("<circle cx='12' cy='12' r='10'/><path d='M10 8v8'/><path d='M14 8v8'/>", 16);
            public static string Cancelled => Svg("<circle cx='12' cy='12' r='10'/><path d='M15 9 9 15'/><path d='M9 9 15 15'/>", 16);
            public static string Approved => Svg("<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/><path d='m9 12 2 2 4-4'/>", 16);
            public static string Urgent => Svg("<path d='M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z'/><path d='M12 9v4'/><path d='M12 17h.01'/>", 16);
            public static string Normal => Svg("<circle cx='12' cy='12' r='10'/>", 16);
            public static string Low => Svg("<circle cx='12' cy='12' r='10'/><path d='M8 12h8'/>", 16);
            // Dots for status chip
            public static string Dot(string color = "#D4AF37") => $"<span class='status-dot' style='background:{color}'></span>";
        }

        // ===== Action Icons =====
        public static class Action
        {
            public static string Edit => Svg("<path d='M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7'/><path d='M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5Z'/>");
            public static string View => Svg("<path d='M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8Z'/><circle cx='12' cy='12' r='3'/>");
            public static string Delete => Svg("<path d='M3 6h18'/><path d='M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/>");
            public static string Print => Svg("<path d='M6 9V2h12v7'/><path d='M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2'/><rect x='6' y='14' width='12' height='8'/>");
            public static string Approve => Svg("<path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'/><path d='M22 4 12 14.01l-3-3'/>");
            public static string Cancel => Svg("<circle cx='12' cy='12' r='10'/><path d='M15 9 9 15'/><path d='M9 9 15 15'/>");
            public static string Plus => Svg("<path d='M12 5v14M5 12h14'/>");
            public static string Search => Svg("<circle cx='11' cy='11' r='8'/><path d='m21 21-4.3-4.3'/>");
            public static string Filter => Svg("<path d='M22 3H2l8 9.46V19l4 2v-8.54L22 3Z'/>");
            public static string Export => Svg("<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/><path d='M7 10l5 5 5-5'/><path d='M12 15V3'/>");
            public static string Back => Svg("<path d='M19 12H5'/><path d='M12 19l-7-7 7-7'/>");
            public static string Save => Svg("<path d='M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2Z'/><path d='M17 21V13H7v8'/><path d='M7 3v5h8'/>");
        }

        // ===== Common UI =====
        public static class UI
        {
            public static string Dashboard => Svg("<rect x='3' y='3' width='7' height='7' rx='1'/><rect x='14' y='3' width='7' height='7' rx='1'/><rect x='14' y='14' width='7' height='7' rx='1'/><rect x='3' y='14' width='7' height='7' rx='1'/>");
            public static string Table => Svg("<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M3 9h18'/><path d='M3 15h18'/><path d='M9 3v18'/><path d='M15 3v18'/>");
            public static string Box => Svg("<path d='M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8Z'/><path d='M3.3 7 12 12l8.7-5'/><path d='M12 22V12'/>");
            public static string Layers => Svg("<path d='M12 2 2 7l10 5 10-5-10-5Z'/><path d='M2 17l10 5 10-5'/><path d='M2 12l10 5 10-5'/>");
            public static string Lock => Svg("<rect x='3' y='11' width='18' height='11' rx='2'/><path d='M7 11V7a5 5 0 0 1 10 0v4'/>");
            public static string Shield => Svg("<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z'/>");
            public static string Users => Svg("<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 21v-2a4 4 0 0 0-3-3.87'/><path d='M16 3.13a4 4 0 0 1 0 7.75'/>");
            public static string Activity => Svg("<path d='M22 12h-4l-3 9L9 3l-3 9H2'/>");
            public static string Inbox => Svg("<path d='M22 12h-6l-2 3h-9L2 12V6a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v6Z'/>");
        }

        // ===== Helper to get by code - unified entry =====
        public static string GetByCode(string? code)
        {
            return NavMenuIcons.Get(code);
        }
    }
}
