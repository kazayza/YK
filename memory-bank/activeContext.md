# 🔄 السياق النشط (Active Context) — YKCoatings

> آخر تحديث: 2026-10-07 (الجلسة 7 — إعادة تصميم شاشة قوائم الأسعار بالنمط الموحد + طريقة عرض ثانية · ثم الجلسة 6 سابقًا)

## ✅ إنجازات الجلسة 7 — شاشة قوائم الأسعار بالتصميم الموحد (2026-10-07 — غير مُرسلة بعد)
1. **إعادة كتابة `Components/Pages/PriceLists.razor` بالكامل** (510 → ~790 سطر) بنمط **Gold Luxury** الموحد: `luxury-page` + ترويسة `luxury-header-hybrid` (شعار ذهبي + خط ذهبي) + إحصائيات `stats-luxury` (4 كروت) + بحث `filter-luxury` مع فلترة بالنوع + حالات `loading/empty/no-access/toast-luxury` + 3 مودالات `modal-overlay-luxury` + مبدّل `view-toggle-luxury` في الترويسة.
2. **طريقة عرض جديدة في نفس الشاشة:** `viewMode` = **0 بطاقات** (الطريقة الحالية بعد التطوير — كروت `plc-card` مع بادج افتراضي وشريط ذهبي عند Hover/التحديد) أو **1 جدول** (جديد — `table-luxury` بأعمدة: كود/اسم/نوع/عملة/خصم/أصناف/حالة/إجراءات مع أيقونات عرض-تعديل-حذف). لوحة أسعار الأصناف تظهر أسفل **كلا العرضين**.
3. **صفر إيموجي نهائياً:** كل الأيقونات SVG inline (في الترويسة/الإحصائيات/البطاقات/الجدول/الحوارات/التوست/رسائل النجاح) — حتى رموز `✅` في رسائل `resultMessage` أُبدلت بنص عربي + أيقونة SVG (فحص برمجي: 0 إيموجي).
4. **منطق محفوظ + تحسينات:** كل صلاحيات `SCR_PRICELISTS` (عرض/إضافة/تعديل/حذف) و `LookupService`/`PriceListService` كما هي · إضافة `filterType` + `viewMode` · `SelectPriceList` صار toggle (ضغط مرة ثانية يغلق) + زر `CloseDetails` · حذف `console.log` debug و `useFixedItemSelect` · مبدّل `IsActive`/`IsDefault` في نموذج القائمة (كان `IsDefault` مفقوداً).
5. **أخطاء إصلاحية أثناء البناء:** الخاصية الحقيقية `ItemNameAr`/`ItemCode` (كانت `ItemName`) · `ArabicSearchHelper` يوفّر `Contains` فقط (لا `Filter`) → بحث عبر `Where(...Contains...)`.
6. **البناء:** نجح — **0 Error · 0 Warning** (المخرجات المؤقتة؛ تحذيرات المشروع القديمة موجودة أصلاً في صفحات أخرى). ملاحظة: تطبيق يعمل حالياً (PID 18204) يقفل `bin` → **يجب إعادة تشغيل التطبيق لرؤية الشاشة الجديدة**.
7. **الملفات المتبقية من المهمة:** إرسال التغييرات (git) لم يُطلب بعد — الشاشة + بنك الذاكره غير مُرسلة.


## 📍 أين نحن الآن (2026-10-07)
- **Git:** `master` = `origin/master` @ **`d3565a5`** («Fix : Home» — 07/10 الساعة 21:50) — شجرة عمل نظيفة، لا تغييرات غير مُرسلة.
- **الحجم:** 105 صفحة في `Components/Pages` (+ مكوّن `Components/SupplierEdit.razor`) · 51 ملف خدمة · 26 ملف DTO.
- **الوحدات المنفذة:** أساسية · مشتريات · مخازن · تصنيع · موارد بشرية · **مبيعات (فواتير + أوامر)** · **خزينة (Gold Edition)** · **حسابات (Gold Edition Pro)**.
- **المتبقي:** عروض أسعار · مرتجعات بيع · إقفال يومي · بطاقة تكلفة · QC · إضافة وحدات المبيعات/الخزينة/الحسابات للبحث الشامل · اختبار حي للشاشات الجديدة · توثيق سكيما قاعدة البيانات لوحدات جديدة.


## ✅ إنجازات الجلسة 6 — المبيعات + الخزينة + الحسابات (commit `d3565a5` «Fix : Home» — 2026-10-07)
1. **اكتمال وحدة المبيعات:** `SalesOrders.razor` + `SalesOrderEdit.razor` (`/sales-orders` + new/edit/view) + `SalesOrderService.cs` (810 سطر) + `Models/SalesOrderDtos.cs` (241) · وتطوير `SalesInvoiceService.cs` (+91). مبدأ الصفحة: «الفاتورة هي الأساس — أوامر البيع اختيارية».
2. **وحدة الخزينة — Gold Edition:** `TreasuryService.cs` (1358 سطر) + `Models/TreasuryDtos.cs` (633 سطر) + 9 شاشات: خزائن وبنوك `/cash-boxes`+`/banks` · كشف خزينة `/cash-box-statement` · تحويلات نقدية `/cash-transfers` · حسابات بنكية `/bank-accounts` · سندات قبض `/receipt-vouchers` (+new/edit/view) · سندات صرف `/payment-vouchers` (+new/edit/view) · مصروفات `/expenses` (+new/edit) · شيكات `/cheques` · لوحة الخزينة `/treasury-dashboard` — مع «رصيد لحظي + قيد أوتوماتيك».
3. **وحدة الحسابات — Gold Edition Pro:** `AccountingService.cs` (1132 سطر) + شاشات: شجرة حسابات `/chart-of-accounts` (عرضان: شجري/تفصيلي) · قيود يومية `/journal-entries` (+new/edit/view) · ميزان مراجعة `/trial-balance` · دفتر أستاذ `/account-ledger` · سنوات مالية `/fiscal-years` · مراكز تكلفة `/cost-centers`.
4. **`Program.cs`:** تسجيل 4 خدمات Scoped جديدة: `SalesInvoiceService` · `SalesOrderService` · `TreasuryService` · `AccountingService`.
5. **`Helpers/GoldIcons.cs`** (88 سطر): مكتبة أيقونات SVG stroke 1.5px بفئات `Manufacturing/Status/Action/UI` — مبدأ «بدون إيموجي إطلاقاً» + `GetByCode()` يحوّل لـ `NavMenuIcons`.
6. **NavMenu.razor:** أيقونة `nm-sub-icon` ملوّنة (`--ic` من `GetModuleColor`) لكل عنصر ابن داخل المجموعة · +~25 كود في خريطة الألوان (حسابات/خزينة سماوي مزرق `#06b6d4`) · استبدال إيموجي 🔍 في نتيجة البحث بـ SVG.
7. **`ModuleRouteHelper.cs`:** مسارات الخزينة/الحسابات مع أكواد بديلة لكل شاشة (`SCR_TB`/`SCR_TRIALBAL` · `SCR_LEDGER`/`SCR_ACCLEDGER`/`SCR_ACCOUNT_LEDGER` · `SCR_DAILYCLOSE => /daily-closing` …).
8. **إعادة تصميم شاشات الإدارة** (تقليص كبير للأسطر + نمط Luxury): `Users` · `UserEdit` · `Roles` · `RoleEdit` · `Settings` · `AuditLog` · `LoginHistory` + لمسات في `Payroll`/`PayrollDetails`.
9. **التحقق:** بناء ناجح وقت الالتزام (DLL ‏8.5MB محدّث في المستودع) · `master` = `origin/master` · شجرة نظيفة.

## ✅ إنجازات الجلسة 5 — ثيم Gold + بداية المبيعات (commit `3b12f93` «Fix : Some Fixes» — 2026-09-21)
1. **ثيم Gold & Black Premium:** ملف جديد `wwwroot/yk-theme-gold.css` (147 سطر) — الذهب `#D4AF37` صار `--yk-primary` بدل الأزرق + أسود فاخر (`--yk-black…`) + خط **Tajawal** (استيراد Google Fonts). يُحمّل في `App.razor` بعد `yk-theme.css` فيغلب عليه.
2. **شعارات PNG جديدة:** `yk-logo-gold.png` · `yk-icon-gold.png` · `yk-logo-white.png` — favicon في `App.razor` = `images/yk-icon-gold.png` + تحميل **ApexCharts** (CDN).
3. **بداية نظام المبيعات:** `SalesInvoices.razor` + `SalesInvoiceEdit.razor` (`/sales-invoices` + new/edit/view) + `SalesInvoiceService.cs` (884 سطر) + `Models/SalesInvoiceDtos.cs` (272 سطر).
4. **`wwwroot/js/app.js` (+89 سطر):** `dashboardCharts` (ApexCharts ذهبية: شهرية/أرصدة/تنبيه/sparklines) + `getClientInfo` + `navigateTo`.
5. **مكتبات أيقونات:** `Helpers/LucideIcons.cs` (86 سطر) + إعادة كتابة `NavMenuIcons.cs` (506 سطر).
6. **تنظيف شامل:** حذف كل ملفات العمل المؤقتة من الجذر (`_bl*` · `_chk` · `_s1/_s2` · `_run*` …) ومخرجات `obj/Release` القديمة باسم البناء السابق.
7. **إعادة هيكلة ~35 صفحة** إلى النمط الجديد (منها `Items` · `Customers` · `CustomerForm` · `PurchaseOrders(+Edit)` · `Dashboard` · `MainLayout` · `NavMenu`) + تطويرات في `DashboardService` (+137) و`DbService` (+36) وخدمات العملاء/الموردين/أوامر الشراء.

## ✅ إنجازات الجلسة 4
1. **البحث الشامل أصبح بحث بيانات حقيقي** (كان يبحث في أسماء الشاشات فقط) — في `MainLayout.razor` + `DashboardService.GlobalSearchAsync`:
   - 8 أنواع مُتحقَّق من أسمائها الفعلية بالسكيما: أصناف (`Items`/`ItemCode`,`ItemNameAr`) · عملاء (`Customers`/`CustomerCode`,`CustomerName`) · موردين (`Suppliers`/`SupplierCode`,`SupplierName`) · موظفين (`Employees`/`EmployeeCode`,`FullName`) · مستخدمين (`SystemUsers`/`Username`) · أوامر شراء (`PurchaseOrders`) · طلبات شراء (`PurchaseRequests`) · فواتير شراء (`PurchaseInvoices`).
   - **كل نوع محمي بصلاحيته** (`PermissionService.HasPermission("SCR_*","View")`) — لا نتائج بلا صلاحية.
   - بحث بالكود أو الاسم · debounce 300ms · `TOP 5` لكل نوع · كل جمل SQL **parameterized**.
   - نتيجة موحدة عبر `GlobalSearchResultDto` (Type · TypeLabel · Icon · Title · SubTitle · Route).
   - تصميم منسدل بمجموعتين (الشاشات + بيانات النظام) مع أيقونة وشادة نوع ملوّنة لكل نوع.
2. **Top Header:** لوحة `tb-page-info` (اسم الشاشة + مسار التنقل) أصبحت رابطًا `<a href="/dashboard">` — الضغط يرجّع للرئيسية من أي شاشة. وضيفت هوية `tb-brand` للأيقونة + اسم المشروع + سطر فرعي `YK COATINGS ERP`.
3. **الداشبورد — حذف أجزاء لا لزوم لها:** أُزيل قسم "الوصول السريع" (لأن كروت الإحصائيات تفتح الشاشات بالفعل) وقسم "مشتريات الشهر الحالي". كما أُزيل كارت "YK Coatings Gold" الذي كان يحوي «شاشة متاحة لك / صنف في النظام / اليوم»، وبقيت بطاقة "أصناف حرجة" فقط (بعدد 4 أصناف).
4. **كروت الإحصائيات أصبحت داكنة فاخرة** (كانت فاتحة شبه شفافة): تدرج `#141D33 → #0A1020` + هالة لونية (`--ac-soft`) + حدود (`--ac-edge`) لكل كارت:
   - أصناف ذهبي `#D4AF37` · موردين سماوي `#0ea5e9` · عملاء أخضر `#10b981` · موظفين بنفسجي `#8b5cf6` · مخازن برتقالي `#f97316` · عمليات اليوم سماوي مزرق `#06b6d4`.
5. **Discovery Filter:** `ModulePermissionDto` صار له `GroupName` (يُحسب من `ModuleCode`) — عمود "الوحدة" في جدول شاشاتك.
6. **إصلاح `GetModuleColor` الناقصة في NavMenu.razor** — الكود كان يستدعيها للسطر `var modColor = GetModuleColor(mod, kids)` بدون تعريف (خطأ بناء) → أُضيفت خريطة ألوان دلالية كاملة (9 عائلات وظيفية) + لوحة ألوان احتياطية ثابتة حسب `ModuleID`.
7. **إعادة تصميم القائمة الجانبية:**
   - تلوين أيقونات الموديولات حسب العائلة الوظيفية (`--ic` من `modColor`) بدل الذهبي الموحّد.
   - خط جانبي متوهج `nm-rail` على يمين المجموعة النشطة بلون الموديول.
   - عناصر أطول قليلًا (`9px 11px`) · زوايا `13-14px` · شريط نشط 3px بتوهج · تدرج أفقي عند الهوفر (`::before`) · `translateX(-3px)`.
   - `nm-section-label` بنقطة ذهبية متوهجة + خط متلاشٍ.
8. **البناء:** نجح — 0 Error (تحذير CS0414 قديم في MainLayout غير مرتبط).

## ⚠️ ملاحظات الجلسة 4
- ~~**لا توجد خدمات مبيعات/حسابات في الكود بعد**~~ **عُدّل في الجلستين 5–6:** صار هناك فواتير وأوامر بيع + خزينة + حسابات فعلياً → **المطلوب:** إضافتها للبحث الشامل في `MainLayout`/`DashboardService.GlobalSearchAsync` بعد التأكد من أسماء جداولها. ما زال مسارات بلا صفحات: `SCR_QUOT` (عروض أسعار) · `SCR_SRET` (مرتجعات بيع) · `SCR_DAILYCLOSE` (إقفال يومي) · `SCR_COSTCARD` (بطاقة تكلفة) · `SCR_QC`.
- البحث الشامل يظهر نتائج البيانات فقط عند تطابق الاسم/الكود (لا يظهر مجموعة فارغة).
- `ScrollToPending` وباقي تحسينات الجلسة 3 سليمة ولم تُتأثر.

---

## ✅ إنجازات الجلسة 3 — ترقية Dashboard
1. **إصلاح ScrollToPending** — كانت دالة فارغة: حبة "طلبات بانتظار الاعتماد" في الـ Hero تمرّر الآن بسلاسة إلى قسم `#pending` (scrollIntoView) وأصبحت `<button>` حقيقية (a11y).
2. **تحية ديناميكية** — "صباح الخير / نهار سعيد / مساء الخير" حسب وقت اليوم (`GetGreeting()`).
3. **Skeleton Shimmer** للتحميل بدل الـ spinner (متسق مع NavMenu).
4. **دلتا حقيقية في كروت الإحصائيات** — `DashboardDeltasDto` (في DbService.cs) + `DashboardService.GetStatsDeltasAsync()` تقارن الشهر الحالي بالسابق (أصناف/موردين/عملاء/موظفين) مع سهم ▲/▼ وألوان (أخضر/أحمر).
5. **Sparklines** داخل كروت الإحصائيات (`dashboardCharts.renderSpark` بوضع sparkline في ApexCharts).
6. **مبدّل فترة الرسم** — 6/12 شهرًا: `GetMonthlyPurchaseChartAsync(int months)` بمعامل parameterized (`WHERE m.N < @Months`) + واجهة `chart-period-switch`.
7. **تحديث تلقائي اختياري** — مفتاح في الـ Hero كل 5 دقائق (System.Timers.Timer + تنظيف في `Dispose()` — الصفحة أصبحت `@implements IDisposable`).
8. **شريط خطأ مع إعادة المحاولة** — بدل `catch{}` الصامت: `_loadError` + شريط أحمر بزر retry.
9. **Empty State** لرسم المشتريات عند عدم وجود فواتير + CTA لفواتير الشراء.
10. **توحيد الأرقام في المخططات** — لاتينية `en-US` (كانت مختلطة مع `ar-EG`).
11. **ألوان الأرصدة منطقية** — دونات + شرائط: موردين سماوي #0ea5e9، عملاء أخضر #10b981، مخزون ذهبي (بدل الأحمر المضلل للموردين).
12. **البناء:** نجح — 0 Error (تحذير CS0414 قديم في MainLayout غير مرتبط بهذه الجلسة).

## ✅ إنجازات الجلسة 2 — التحقق والتنظيف النهائي
> آخر تحديث: 2026-09-17 (الجلسة 2)

## ✅ إنجازات الجلسة 2 — التحقق والتنظيف النهائي
1. **إثبات البناء:** `dotnet build YKCoatings.csproj` → **نجح · 0 Warning · 0 Error** → أخرج `bin\Debug\net8.0\YKCoatings.dll`.
2. **سبب تعطّل البناء سابقاً (مُشخَّص ومحلول):** كان مجلد `obj/` يحوي مخلفات استعادة باسم `DizurdeWeb.csproj` من أبريل → كانت `dotnet restore` تفشل صامتة.
   **الحل:** حذف `obj/` وإعادة `dotnet restore YKCoatings.csproj` ثم البناء.
3. **النشر (Publish):** `dotnet publish -c Release -o publish` → نجح وأنتج `YKCoatings.dll/.exe/pdb/.deps.json/.runtimeconfig.json` + `wwwroot/YKCoatings.styles.css`.
4. **إعادة التلوين الموحّدة:** استبدال 447 موضعاً للأزرق القديم `#3b82f6` / `rgba(59,130,246,…)` بالأزرق الرسمي `#2563eb` / `rgba(37,99,235,…)` عبر **71 ملف** (razor/css) — الهوية الآن موحّدة فعلياً.
5. **تنظيف الأصول القديمة (كانت لا تزال باسم Dizurde!):** حُذف `dizurde-logo.svg` · `dizurde-icon.svg` · `logo.png` (2.1MB) · `logo-icon.png` — بعد التحقق من **صفر مراجع** لها. مجلد `images/` الآن يحتوي فقط `yk-logo.svg` + `yk-icon.svg`.
6. **إصلاح ترويسة الشريط الجانبي:** كان الشعار يظهر مرتين (أيقونة + نص) ومُجبراً على اللون الأبيض بـ `filter: brightness(0) invert(1)`، و`alt="D"` متبقٍ من الاسم القديم.
   - حُذف التكرار والـ`alt` القديم، وأُزيل الـ filter (الشعار الآن بألوان الهوية الأصلية)، وضُبط الارتفاع إلى 40px.
7. **توحيد متغيرات الألوان** في `MainLayout.razor`: `--sb-accent` من `#3b82f6` إلى `#2563eb`.
8. **`README.md` احترافي** في الجذر (بدء سريع · بنية · فهارس التوثيق · قواعد إلزامية).
9. **تنظيف كل ملفات العمل المؤقتة** (`_bo`, `_be`, `_rebrand*`, `_scan*`, `_build*`, `_recolor*`, `project-structure.txt`) — الجذر نظيف.
10. **تنظيف نهائي لمخلفات البناء:** كانت `bin/Release/net8.0/` تحتوي 6 ملفات باسم `DizurdeWeb.*` (من بناء قديم) → حُذفت وأُعيد البناء.
    **النتيجة النهائية: `FILE_NAME_MATCHES = 0` على مستوى المستودع كله** (مصدر + bin + obj + publish).
11. **التحقق الحي للتطبيق:** شُغّل التطبيق على `http://localhost:5021` وفُحص بمتصفح — العنوان «تسجيل الدخول - واي كي كوتينج» · `dir=rtl` · `lang=ar` · الشعار `yk-logo.svg` · favicon `yk-icon.svg` · زر الدخول بالتدرج `rgb(29,78,216)→rgb(37,99,235)` · **صفر أثر للاسم القديم في الصفحة**.
    - سجل الخادم نظيف تماماً بلا أي exception. (رسالة «خطأ في الاتصال بالخادم» في صفحة الدخول سببها أن قاعدة SQL السحابية غير قابلة للوصول من بيئة التنفيذ الحالية — سلوك بيئي لا خطأ كود.)

## 🔴 تنبيه مهم للجلسات القادمة
> إذا فشل البناء بصمت → **احذف `obj/`** ثم أعد `dotnet restore` والبناء. مخلفات الاستعادة القديمة أخطر عدو لهذا المستودع.

## الخطوة التالية المقترحة
- **اختبار حي** للشاشات الجديدة (خزينة/حسابات/أوامر بيع) على `http://localhost:5021` والتحقق من الصلاحيات (`SCR_COA/SCR_JV/SCR_TREASURY/SCR_SO/SCR_SINV`…).
- **إضافة المبيعات/الخزينة/الحسابات للبحث الشامل** (الأنواع الجديدة في `GlobalSearchAsync` مع `PermissionService`).
- **توثيق سكيما** جداول الوحدات الجديدة في `database/` (لا Migrations — خطر فقدان التعريف).
- **توحيد فئات `luxury-*`**: `.luxury-page` معرّفة داخل `<style>` في **44 صفحة** — نقلها إلى `yk-theme-gold.css`.
- المراجعة الأمنية: نقل كلمة مرور قاعدة البيانات من `appsettings.json` إلى user-secrets + تدويرها.
- ترحيل الهاش من SHA-256 إلى PBKDF2 هجين.
- Pagination SQL للقوائم الكبيرة (Items, StockMovements, AuditLog, LoginHistory).

## نقاط مفتوحة (اسأل المالك)
- هل تريد الحفاظ على جداول قاعدة البيانات كما هي (لا تتغير أسماؤها)؟ — **نعم افتراضياً**.
- أي بيانات دراسية في `Counter.razor`/`Weather.razor` — مرشحة للحذف.
