namespace YKCoatings.Helpers
{
    public static class ArabicSearchHelper
    {
        /// <summary>
        /// تطبيع النص العربي للبحث
        /// يتجاهل: الهمزات - التاء المربوطة - الألف المقصورة - التشكيل
        /// </summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var result = text.Trim().ToLower();

            // إزالة التشكيل
            result = RemoveDiacritics(result);

            // توحيد الهمزات
            result = result
                .Replace("أ", "ا")
                .Replace("إ", "ا")
                .Replace("آ", "ا")
                .Replace("ء", "ا")
                .Replace("ؤ", "و")
                .Replace("ئ", "ي");

            // التاء المربوطة
            result = result.Replace("ة", "ه");

            // الألف المقصورة
            result = result.Replace("ى", "ي");

            // إزالة المسافات الزائدة
            result = System.Text.RegularExpressions.Regex
                .Replace(result, @"\s+", " ");

            return result;
        }

        /// <summary>
        /// هل النص يحتوي على كلمة البحث (مع التطبيع)
        /// </summary>
        public static bool Contains(string? text, string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Normalize(text).Contains(Normalize(search));
        }

        /// <summary>
        /// إزالة التشكيل العربي
        /// </summary>
        private static string RemoveDiacritics(string text)
        {
            // نطاق التشكيل العربي: 0x064B إلى 0x065F
            var sb = new System.Text.StringBuilder(text.Length);

            foreach (var c in text)
            {
                if (c < '\u064B' || c > '\u065F')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}