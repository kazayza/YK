# 🎨 YK Coatings ERP — نظام تخطيط موارد مصنع واي كي كوتينج

> نظام ERP ويب عربي متكامل (RTL) لمصنع طلاءات ودهانات — يغطي المشتريات، المخازن، التصنيع، الموارد البشرية، والصلاحيات الأمنية.

| البند | القيمة |
|---|---|
| الاسم الكودي | `YKCoatings` |
| الاسم المعروض | **واي كي كوتينج** · YK Coatings |
| التقنية | ASP.NET Core 8 · Blazor Server (Interactive Server) |
| الوصول للبيانات | Dapper + Microsoft.Data.SqlClient (SQL parameterized) |
| قاعدة البيانات | SQL Server (سحابية) |
| Excel | ClosedXML |
| الواجهة | عربية RTL · خط Cairo · Bootstrap 5 + Bootstrap Icons |
| الشعارات | `wwwroot/images/yk-logo.svg` · `yk-icon.svg` |

---

## 🚀 البدء السريع

```powershell
# استعادة الحزم
dotnet restore YKCoatings.csproj

# البناء
dotnet build YKCoatings.csproj

# التشغيل (http://localhost:5021)
dotnet run --project YKCoatings.csproj

# النشر للإنتاج
dotnet publish YKCoatings.csproj -c Release -o publish
```

**متطلبات:** .NET SDK 8.0+ · اتصال بقاعدة بيانات SQL Server (سلسلة الاتصال في `appsettings.json`).

---

##  بنية المشروع

```
YkWeb/
├── YKCoatings.sln / YKCoatings.csproj / Program.cs
├── appsettings.json · appsettings.Development.json · appsettings.Production.json
├── Components/
│   ├── App.razor · Routes.razor · _Imports.razor
│   ├── Layout/   MainLayout · EmptyLayout · NavMenu (+css)
│   └── Pages/    ~80 صفحة (قوائم + نماذج إدخال + تقارير)
├── Services/     47 خدمة — كلها ترث BaseDbService
├── Models/       23 ملف DTO (List / Edit / Audit)
├── Helpers/      ArabicSearchHelper · ModuleRouteHelper · NavMenuIcons
├── wwwroot/
│   ├── yk-theme.css   ← نظام التصميم الموحد (أزرق #2563eb / برتقالي #f97316)
│   ├── app.css · js/app.js · images/ · sounds/
├── memory-bank/  ← بنك الذاكرة (اقرأه أولاً)
├── skills/       ← مهارات جاهزة للمهام المتكررة
└── docs/         ← المراجعة الاحترافية الشاملة
```

---

##  التوثيق الذكي (اقرأ قبل أي تعديل)

### بنك الذاكرة — `memory-bank/`
| الملف | متى تقرأه |
|---|---|
| [`projectbrief.md`](memory-bank/projectbrief.md) | دائماً — أول ملف |
| [`productContext.md`](memory-bank/productContext.md) | عند العمل على ميزات/صفحات |
| [`techContext.md`](memory-bank/techContext.md) | عند كتابة أي كود |
| [`systemPatterns.md`](memory-bank/systemPatterns.md) | قبل إنشاء أي ملف جديد |
| [`activeContext.md`](memory-bank/activeContext.md) | دائماً — آخر حالة |
| [`progress.md`](memory-bank/progress.md) | لمعرفة المنجز والمتبقي |

### ملفات Skills — `skills/`
| الملف | الاستخدام |
|---|---|
| [`yk-project-overview.md`](skills/yk-project-overview.md) | بداية أي جلسة عمل |
| [`yk-add-service.md`](skills/yk-add-service.md) | إضافة خدمة/وحدة بيانات جديدة |
| [`yk-add-page.md`](skills/yk-add-page.md) | إضافة صفحة Razor جديدة |
| [`yk-database.md`](skills/yk-database.md) | SQL / Dapper / السكيما |
| [`yk-design-system.md`](skills/yk-design-system.md) | الهوية البصرية والألوان |
| [`yk-security-session.md`](skills/yk-security-session.md) | الصلاحيات والجلسة والأمان |
| [`yk-print-export.md`](skills/yk-print-export.md) | الطباعة وتصدير Excel |

### المراجعة الاحترافية
[`docs/PROJECT_REVIEW.md`](docs/PROJECT_REVIEW.md) — مراجعة معمارية وأمنية شاملة مع خطة معالجة مرتبة حسب الأولوية.

---

##  الوحدات الوظيفية

- **البيانات الأساسية:** أصناف، تصنيفات، وحدات، مخازن، موردون، عملاء، عملات، شروط دفع، قوائم أسعار.
- **المشتريات:** طلبات شراء ← أوامر شراء ← أذون استلام ← فواتير شراء ← مرتجعات.
- **المخازن:** أرصدة، حركات، تحويلات، جرد.
- **التصنيع:** وصفات (BOM)، أوامر إنتاج، صرف خامات، تشغيلات، تصنيع بالقطع.
- **الموارد البشرية:** موظفون، أقسام، مسميات، حضور، إجازات، سلف، جزاءات، مسير مرتبات.
- **إدارة النظام:** مستخدمون، أدوار وصلاحيات (9 أنواع عمليات)، سجل تدقيق، سجل دخول، إشعارات.

---

## ⚠️ قواعد إلزامية

1. **الهوية:** `YKCoatings` كودياً / واي كي كوتينج معروضاً — لا يُستخدم الاسم القديم.
2. **اللغة:** الواجهة والتوثيق بالعربية RTL، أسماء الكود بالإنجليزية.
3. **البيانات:** لا SQL بدون Parameterized queries — لا دمج نصوص إطلاقاً.
4. **الخدمات:** كل خدمة ترث `BaseDbService` وتُسجَّل `Scoped` في `Program.cs`.
5. **الصلاحيات:** افحص `PermissionService.Can(...)` قبل عرض أي زر كتابة.
6. **التصميم:** استخدم كلاسات `yk-theme.css` قبل كتابة CSS جديد.
7. **لا تعديل يدوي** في `bin/` · `obj/` · `publish/`.
8. **بعد كل مهمة:** حدّث `memory-bank/activeContext.md` و`progress.md`.

> التفاصيل الكاملة في [`.clinerules`](.clinerules).

---

## 📌 حالة المشروع

- ✅ المرحلة الحالية: نظام ERP مكتمل للوحدات الأساسية (مشتريات · مخازن · تصنيع · HR · أمان).
- 🚧 قيد التطوير: وحدة المبيعات · الحسابات (دليل الحسابات والقيود) · ضبط الجودة.
- 📄 آخر تحديث: 2026-09-17.