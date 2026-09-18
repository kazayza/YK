# Skill: الطباعة والتصدير (YKCoatings)

## تصدير Excel (ClosedXML)
في الخدمة:
```csharp
public (byte[] file, string name) ExportItemsExcel(List<ItemListDto> items)
{
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("الأصناف");
    ws.RightToLeft = true;
    ws.Cell(1,1).Value = "الكود"; ws.Cell(1,2).Value = "الاسم";
    int r = 2;
    foreach (var i in items) { ws.Cell(r,1).Value = i.ItemCode; ws.Cell(r,2).Value = i.ItemNameAr; r++; }
    ws.Columns().AdjustToContents();
    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    return (ms.ToArray(), $"Items_{DateTime.Now:yyyyMMdd}.xlsx");
}
```
في الصفحة:
```csharp
var (file, name) = Service.ExportItemsExcel(items);
await JS.InvokeVoidAsync("downloadFile", name, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(file));
```

## الطباعة (printHtml)
- ابنِ HTML عربي كامل (`dir="rtl"`, خط Cairo) برأس يحمل الشعار `images/yk-logo.svg` وسطر `واي كي كوتينج — نظام تخطيط موارد المؤسسات`.
- ثم: `await JS.InvokeVoidAsync("printHtml", html);`
- أضف `@media print` مخفياً لعناصر الشاشة إن لزم — نظام `yk-theme.css` يخفي الشريط الجانبي/العلوي تلقائياً عند الطباعة.

## قواعد
- اسم الملف يبدأ باسم الوحدة + تاريخ `yyyyMMdd`.
- لا تضع صور ثقيلة (logo.png 2MB) في التقارير المطبوعة — استخدم SVG أو PNG المصغّر.
- أرقام العملة: `N2` format مع رمز العملة من الإعدادات.
