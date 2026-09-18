# ⚙️ السياق التقني (Tech Context) — YKCoatings

## حزمة التقنيات
| المكوّن | التقنية | ملاحظات |
|---|---|---|
| المنصة | .NET 8 (ASP.NET Core) | `YKCoatings.csproj` |
| الواجهة | Blazor Server (Interactive Server) | بدون prerender |
| الوصول للبيانات | Dapper + `Microsoft.Data.SqlClient` | SQL مكتوبة يدوياً (إجراءات غير مخزنة غالباً) |
| قاعدة البيانات | SQL Server سحابية (public.databaseasp.net) | الاتصال في `appsettings.json` |
| Excel | ClosedXML 0.105 | تصدير القوائم والتقارير |
| الخطوط/الأيقونات | Cairo + Bootstrap Icons 1.11.3 + Bootstrap 5 | عبر CDN |

## بنية المجلدات
```
YkWeb/
├── YKCoatings.sln / YKCoatings.csproj / Program.cs
├── appsettings.{json,Development,Production}.json
├── Components/
│   ├── App.razor / Routes.razor / _Imports.razor
│   ├── SupplierEdit.razor          (مكون مشترك)
│   ├── Layout/  MainLayout.razor · EmptyLayout.razor · NavMenu.razor(+.css)
│   └── Pages/   ~80 صفحة razor
├── Services/    47 خدمة (كلها ترث BaseDbService)
├── Models/      23 ملف DTO
├── Helpers/     ArabicSearchHelper · ModuleRouteHelper · NavMenuIcons
├── wwwroot/
│   ├── app.css (أساسي) · yk-theme.css (نظام التصميم الموحد) ← مُحمّل في App.razor
│   ├── images/ yk-logo.svg · yk-icon.svg (الشعارات الرسمية)
│   ├── js/ app.js (طباعة/تحميل/صوت/مهلة الجلسة) · auth.js (غير محمّل)
│   └── sounds/ notification.mp3
└── memory-bank/ · skills/ · docs/  (التوثيق)
```

## تدفق التشغيل
1. `Program.cs` يسجل 45+ خدمة كـ Scoped + `AddHttpContextAccessor`.
2. `App.razor` يحمّل الأنماط ويشغّل `Routes` بوضع InteractiveServer بدون prerender.
3. `MainLayout` يستعيد الجلسة من `ProtectedSessionStorage` عند أول رسم، يحمّل الصلاحيات والإشعارات، ويدير مهلة الخمول عبر JS interop.
4. الطلبات تمر عبر middleware يلتقط IP العميل في `context.Items["ClientIP"]`.

## الأوامر
```powershell
dotnet build YKCoatings.sln            # البناء
dotnet run --project YKCoatings.csproj # التشغيل (http://localhost:5021)
dotnet publish -c Release -o publish   # النشر
```

## قواعد صارمة
- **لا تعدل** مجلدات `bin/` و`obj/` و`publish/` (مخرجات بناء قديمة).
- لا تُدخل أي أسرار جديدة في appsettings — كلمة مرور قاعدة البيانات موجودة بالفعل في `appsettings.json` (ملاحظة أمنية معروفة — انظر المراجعة).
- أي SQL جديدة: دائماً بمعاملات Parameterized — ممنوع دمج نصوص المستخدم في SQL.
- الترميز: احفظ الملفات UTF-8 مع BOM (الملفات الموجودة كذلك).
