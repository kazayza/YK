using System.Globalization;
using System.Net;
using System.Text;
using YKCoatings.Models;

namespace YKCoatings.Helpers
{
    /// <summary>
    /// Builds a self-contained, Arabic RTL print document for a journal entry.
    /// All database/user-provided text is HTML-encoded before insertion.
    /// </summary>
    public static class JournalPrintHtmlBuilder
    {
        public static string Build(JournalEntryDto journal)
        {
            ArgumentNullException.ThrowIfNull(journal);

            var details = journal.Details ?? new List<JournalDetailDto>();
            var html = new StringBuilder(8192);
            var journalNumber = Text(journal.JournalNumber);

            html.Append("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\">")
                .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
                .Append("<title>قيد يومية — ").Append(journalNumber).Append("</title>")
                .Append(@"<style>
@page{size:A4 landscape;margin:11mm 12mm}
*{box-sizing:border-box}
html,body{margin:0;padding:0;background:#fff;color:#172033;font-family:'Tajawal','Segoe UI',Tahoma,Arial,sans-serif;direction:rtl;-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{font-size:11px;line-height:1.55}
.sheet{width:100%;margin:0 auto}
.top-rule{height:5px;border-radius:5px;background:linear-gradient(90deg,#9C7C2E,#D4AF37,#F5E6C8,#D4AF37,#9C7C2E);margin-bottom:14px}
.brand-header{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:4px 0 13px;border-bottom:1px solid #e5dcc2}
.brand{display:flex;align-items:center;gap:12px}
.brand-logo{display:block;width:160px;height:96px;object-fit:contain;background:#070B14;border:1px solid #D4AF37;border-radius:8px}
.brand-caption{font-size:9px;color:#6b7280;font-weight:700}
.document-title{text-align:left}
.document-title strong{display:block;font-size:20px;color:#111827;font-weight:900}
.document-title span{font-size:10px;color:#9C7C2E;font-weight:800}
.meta-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;margin:13px 0}
.meta-item{min-height:48px;border:1px solid #e8e2d0;border-radius:8px;padding:7px 9px;background:#fcfaf6;overflow-wrap:anywhere}
.meta-item span{display:block;font-size:8px;color:#7b8190;font-weight:700;margin-bottom:3px}
.meta-item strong{font-size:10px;color:#111827;font-weight:800}
.description-box{border:1px solid #e8e2d0;border-right:3px solid #D4AF37;border-radius:8px;padding:9px 11px;margin:0 0 12px;background:#fff;overflow-wrap:anywhere}
.description-box .label{display:block;font-size:8px;color:#7b8190;font-weight:700;margin-bottom:3px}
.description-box .value{font-size:10px;font-weight:700;white-space:pre-wrap}
.table-wrap{width:100%;overflow:visible}
table{width:100%;border-collapse:collapse;table-layout:fixed}
thead{display:table-header-group}
th{background:#111827;color:#F5E6C8;font-size:9px;font-weight:800;padding:8px 7px;border:1px solid #111827;text-align:right}
td{padding:7px;border:1px solid #e5e7eb;font-size:9px;vertical-align:top;overflow-wrap:anywhere}
tbody tr:nth-child(even){background:#fcfaf6}
.num{text-align:left;direction:ltr;font-family:Consolas,Menlo,monospace;white-space:nowrap}
.center{text-align:center}
.account-code{direction:ltr;text-align:right;color:#9C7C2E;font-size:8px;font-family:Consolas,Menlo,monospace;font-weight:800}
.account-name{display:block;font-size:9px;font-weight:800;color:#111827;margin-top:2px}
tfoot td{background:#f8f6f0;font-weight:900;border-top:1.5px solid #D4AF37}
.total-strip{display:flex;justify-content:flex-start;gap:10px;margin-top:12px;flex-wrap:wrap}
.total-card{min-width:155px;border:1px solid #e8e2d0;border-radius:8px;padding:8px 11px;background:#fcfaf6}
.total-card span{display:block;font-size:8px;color:#7b8190;font-weight:700}
.total-card strong{display:block;margin-top:3px;font-size:13px;font-family:Consolas,Menlo,monospace;direction:ltr;text-align:right;color:#111827}
.total-card.debit strong{color:#1d4ed8}.total-card.credit strong{color:#9C7C2E}
.signatures{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:24px;margin-top:25px;page-break-inside:avoid}
.signature{min-height:48px;border-top:1px solid #9ca3af;padding-top:5px;text-align:center;font-size:9px;font-weight:700;color:#4b5563}
.footer{display:flex;justify-content:space-between;gap:12px;margin-top:15px;padding-top:7px;border-top:1px solid #e5e7eb;color:#8b95a5;font-size:8px}
tr{page-break-inside:avoid;break-inside:avoid}
@media print{.sheet{width:100%}}
</style></head>")
                .Append("<body><main class=\"sheet\"><div class=\"top-rule\"></div>")
                .Append("<header class=\"brand-header\"><div class=\"brand\"><img class=\"brand-logo\" src=\"/images/yk-logo-gold.png\" alt=\"YK COATINGS\"><span class=\"brand-caption\">نظام الحسابات</span></div>")
                .Append("<div class=\"document-title\"><strong>")
                .Append(Text(JournalTitle(journal)))
                .Append("</strong><span>").Append(Text(JournalOriginLabel(journal)))
                .Append("</span></div></header>")
                .Append("<section class=\"meta-grid\">")
                .Append(Meta("رقم القيد", journal.JournalNumber))
                .Append(Meta("تاريخ القيد", journal.JournalDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)))
                .Append(Meta("الحالة", JournalStatusName(journal)))
                .Append(Meta("النوع", JournalTypeLabel(journal)))
                .Append(Meta("المصدر", SourceName(journal.SourceDocType)))
                .Append(Meta("رقم المستند المصدر", journal.SourceDocNumber))
                .Append(Meta("السنة المالية", journal.FiscalYearName))
                .Append(Meta("الفترة المحاسبية", journal.PeriodName))
                .Append(Meta("المرجع", journal.Reference))
                .Append(Meta("أُعد بواسطة", journal.CreatedByName))
                .Append(Meta("تاريخ الإنشاء", journal.CreatedDate.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)))
                .Append("</section>")
                .Append("<section class=\"description-box\"><span class=\"label\">البيان</span><div class=\"value\">")
                .Append(Text(journal.Description)).Append("</div></section>")
                .Append("<div class=\"table-wrap\"><table><colgroup><col style=\"width:5%\"><col style=\"width:22%\"><col style=\"width:28%\"><col style=\"width:15%\"><col style=\"width:15%\"><col style=\"width:15%\"></colgroup>")
                .Append("<thead><tr><th class=\"center\">#</th><th>الحساب</th><th>البيان</th><th>مركز التكلفة</th><th class=\"num\">مدين</th><th class=\"num\">دائن</th></tr></thead><tbody>");

            if (details.Count == 0)
            {
                html.Append("<tr><td class=\"center\" colspan=\"6\">لا توجد تفاصيل للقيد</td></tr>");
            }
            else
            {
                foreach (var detail in details.OrderBy(x => x.LineNumber))
                {
                    html.Append("<tr><td class=\"center\">")
                        .Append(detail.LineNumber.ToString(CultureInfo.InvariantCulture))
                        .Append("</td><td><div class=\"account-code\">").Append(Text(detail.AccountCode))
                        .Append("</div><span class=\"account-name\">").Append(Text(detail.AccountNameAr))
                        .Append("</span></td><td>").Append(Text(detail.Description))
                        .Append("</td><td>").Append(Text(detail.CostCenterNameAr))
                        .Append("</td><td class=\"num\">").Append(Amount(detail.DebitAmount))
                        .Append("</td><td class=\"num\">").Append(Amount(detail.CreditAmount))
                        .Append("</td></tr>");
                }
            }

            html.Append("</tbody><tfoot><tr><td colspan=\"4\">الإجمالي</td><td class=\"num\">")
                .Append(Amount(journal.TotalDebit)).Append("</td><td class=\"num\">")
                .Append(Amount(journal.TotalCredit)).Append("</td></tr></tfoot></table></div>")
                .Append("<section class=\"total-strip\"><div class=\"total-card debit\"><span>إجمالي المدين</span><strong>")
                .Append(Amount(journal.TotalDebit)).Append("</strong></div><div class=\"total-card credit\"><span>إجمالي الدائن</span><strong>")
                .Append(Amount(journal.TotalCredit)).Append("</strong></div><div class=\"total-card\"><span>فرق التوازن</span><strong>")
                .Append(Amount(Math.Abs(journal.TotalDebit - journal.TotalCredit))).Append("</strong></div></section>")
                .Append("<section class=\"signatures\"><div class=\"signature\">إعداد</div><div class=\"signature\">مراجعة</div><div class=\"signature\">اعتماد</div></section>")
                .Append("<footer class=\"footer\"><span>YK COATINGS — قيد محاسبي</span><span>تاريخ الطباعة: ")
                .Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
                .Append("</span></footer></main></body></html>");

            return html.ToString();
        }

        private static string Meta(string label, string? value)
        {
            return "<div class=\"meta-item\"><span>" + Text(label) + "</span><strong>" + Text(value) + "</strong></div>";
        }

        private static string Text(string? value)
        {
            return WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value.Trim());
        }

        private static string Amount(decimal value)
        {
            return value.ToString("#,##0.00", CultureInfo.InvariantCulture);
        }

        private static string JournalStatusName(JournalEntryDto journal)
        {
            return journal.JournalStatus switch
            {
                1 => "مسودة",
                2 => "مرحل",
                3 => "ملغي",
                _ => journal.StatusName ?? "غير محدد"
            };
        }

        private static string JournalTitle(JournalEntryDto journal)
        {
            if (IsPurchaseInvoiceSource(journal.SourceDocType))
                return "قيد فاتورة مشتريات";
            if (IsSalesInvoiceSource(journal.SourceDocType))
                return "قيد فاتورة مبيعات";

            if (journal.IsAutoGenerated)
            {
                var sourceName = SourceName(journal.SourceDocType);
                return sourceName == "يدوي" || sourceName == "غير محدد"
                    ? "قيد آلي"
                    : "قيد " + sourceName;
            }

            return journal.JournalType switch
            {
                1 => "قيد يومية عامة",
                2 => "قيد افتتاحي",
                3 => "قيد تسوية",
                4 => "قيد إقفال",
                _ => "قيد محاسبي"
            };
        }

        private static string JournalOriginLabel(JournalEntryDto journal)
        {
            return journal.IsAutoGenerated ? "ترحيل آلي" : "قيد يدوي";
        }

        private static string JournalTypeLabel(JournalEntryDto journal)
        {
            if (IsPurchaseInvoiceSource(journal.SourceDocType))
                return "فاتورة مشتريات";
            if (IsSalesInvoiceSource(journal.SourceDocType))
                return "فاتورة مبيعات";

            if (journal.IsAutoGenerated)
            {
                var sourceName = SourceName(journal.SourceDocType);
                return sourceName == "يدوي" ? "قيد آلي" : sourceName;
            }

            return journal.JournalType switch
            {
                1 => "يومية عامة",
                2 => "افتتاحي",
                3 => "تسوية",
                4 => "إقفال",
                _ => "محاسبي"
            };
        }

        private static bool IsPurchaseInvoiceSource(string? sourceType)
        {
            return sourceType is "PI" or "PurchaseInvoice";
        }

        private static bool IsSalesInvoiceSource(string? sourceType)
        {
            return sourceType is "SI" or "SalesInvoice";
        }

        private static string SourceName(string? sourceType)
        {
            return sourceType switch
            {
                "PI" or "PurchaseInvoice" => "فاتورة مشتريات",
                "SI" or "SalesInvoice" => "فاتورة مبيعات",
                "OverheadExpense" => "مصروف",
                "ReceiptVoucher" => "سند قبض",
                "PaymentVoucher" => "سند صرف",
                "CashTransfer" => "تحويل خزينة",
                "BankTransfer" => "تحويل بنكي",
                "OpeningBalance" => "رصيد افتتاحي",
                null or "" => "يدوي",
                _ => sourceType ?? "غير محدد"
            };
        }
    }
}
