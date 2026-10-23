# 🔄 السياق النشط (Active Context) — YKCoatings

> آخر تحديث: **2026-10-21 11:20 (الجلسات 13–15 — ثلاث جولات اختبار E2E حي)**
> ⏸️ **الجلسة معلَّقة بانتظار المستخدم** — لا تكمل الاختبار حتى يطلب.

---

## ⏸️ حالة الاستئناف (اقرأ هذا أولاً)

### ما اكتمل
| # | ما تم | الحالة |
|---|---|---|
| 1 | **حزمة اختبار E2E في المستودع** | `tests/e2e/` — `helpers.js` · `README.md` · `SCENARIOS.md` (55 سيناريو) · `run-accounting.js` |
| 2 | **مجلد `database/`** | `seed/01-number-sequences.sql` (37 تسلسلاً) · `cleanup-e2e-testdata.sql` (DRY RUN افتراضياً) · `README.md` |
| 3 | **الجولة 1** — التصنيع + المخازن | ✅ 65 مسار · **11 عيب** · FIFO مُثبَت |
| 4 | **الجولة 2** — الحسابات (المحرك المالي) | ✅ أول اختبار حي له · **4 عيوب** |
| 5 | **الجولة 3** — المشتريات + تشريح القيود | ✅ **3 عيوب** |
| 6 | **خادم Playwright MCP** | `C:\Users\Admin\.config\kilo\kilo.jsonc` + أمر `/erp-visual-check` (✅ مُصحَّح بالدروس المستفادة) |

### 🔴 العيوب المؤكَّدة (21 عيباً — 7 حرجة)
| # | العيب | الملف | الحالة |
|---|---|---|---|
| 1 | **تسلسل `BOM` مفقود** ⇒ الوصفات مستحيلة + موت الـ circuit | `BOMService.cs:1383` | ✅ **أُصلح** (أُنشئ التسلسل) |
| 2 | **الجرد لا يحدّث `InventoryBatchBalance`** ⇒ كسر دائم لمطابقة FIFO | `sp_ExecuteStockCountAdjustments` | ❌ مفتوح |
| 3 | **تسوية الجرد بالتكلفة `0.00`** (المصدر `Items.AverageCost` لا تحدّثه دفعة الإنتاج) | `sql.txt:5123` | ❌ مفتوح |
| 4 | **سند القبض بلا `RevenueAccountID`** ⇒ سطران بـ `AccountID=0` والمال لا يصل للدفتر | `TreasuryService.cs:664-691` | ❌ مفتوح |
| 5 | **قيد فاتورة الشراء «مسودة» بمجاميع صفرية** — ونفس النقص في 4 إجراءات | `sql.txt:4369` · `:4510` · `:4266` · `:4650` | ❌ مفتوح |
| 6 | ميزان المراجعة يعرض `L@t.AccountLevel` خاماً | `TrialBalance.razor:61` | ❌ مفتوح |
| 7 | صفحة تعديل القيد المرحّل تعرض سطرين فارغين | `JournalEntryEdit.razor` | ❌ مفتوح |
| 8 | رصيد الصندوق لا يُحدَّث بالقيد اليدوي | `TreasuryService` / `AccountingService` | ❌ مفتوح |
| 9 | قيد المصروف التلقائي قد يبقى مسودة (بيانات تشغيلية قائمة) | `AccountingService.cs:1908-1919` | ❌ مفتوح |
| 10 | `SQ` ببادئة `QT-` ⇒ `QT--2026-000003` · `ITM` ناقص | `NumberSequences` | ✅ **أُصلح** |
| 11 | المعاينة تعرض المتوسط لا FIFO (انحراف 12%) | `MaterialIssueService` | ❌ مفتوح |
| 12 | كود الصنف يُولَّد تلقائياً والمكتوب يُهمَل · البحث لا يطابق الكود · «حالة المخزون» محاكاة | `ItemEdit.razor` · `Items.razor` | ❌ مفتوح |
| 13 | `app.UseExceptionHandler` لا يلتقط أخطاء الـcircuit · لا `#blazor-error-ui` · لا `UseHttpsRedirection` · لا `AddAuthentication` | `Program.cs` · `App.razor` | ❌ مفتوح |
| 14 | `appsettings.Production.json` نص عربي ⇒ انقطاع 100% · `AllowedHosts:*` · `TrustServerCertificate` | `appsettings*.json` | ❌ مفتوح |
| 15 | **رصيد الخزينة لا يصل لدفتر الأستاذ** — خزينة برصيد 1,000 مقابل حسابها 0.00 | `TreasuryService` (CreateCashBox) | ❌ مفتوح |
| 16 | **خزينة بلا حساب محاسبي تُقبل** — 500 نقداً خارج الدفتر | `TreasuryService.cs:353` | ❌ مفتوح |
| 17 | **تحصيل شيك لا يُنتج قيداً ولا يحرّك رصيداً** (500 EGP «محصل») | `TreasuryService` (collect) | ❌ مفتوح |
| 18 | **مدير النظام ممنوع من لوحة الخزينة** — تتطلب `SCR_TREASURY` وحدها | `TreasuryDashboard.razor:152` | ❌ مفتوح |
| 19 | **دور «مستخدم عرض فقط» يملك 9 أعلام على 6 وحدات محاسبية** | `RolePermissions` (الدور VIEWER) | ❌ مفتوح |
| 20 | **سباق يمسح اختيار العملة + لا حقل سعر صرف** ⇒ لا فاتورة أجنبية إطلاقاً | `SalesInvoiceEdit.razor:583` | ❌ مفتوح |
| 21 | **ميزان المراجعة يقبل أرصدة سالبة** (‎−11.14 · ‎−3,192.00) | `AccountingService` (trial balance) | ❌ مفتوح |

**التقرير الكامل:** `docs/E2E-TEST-REPORT.md` (584 سطر · 3 جولات) · الأدلة `docs/e2e-screenshots/` (22 صورة).

### ⏭️ الخطوة التالية بالترتيب
> **تحديث 2026-10-22 08:00 — انتهى عمل التصميم (الجلسة 18). المتبّي هو الاختبار الوظيفي.**

**✅ المنجز في تصميم الواجهة (الجلسة 18):** إزالة كاملة للإيموجي (457 سطر · 89 ملف) · Bootstrap Icons وApexCharts وخط Tajawal **محلياً (صفر CDN)** · حذف `app.css` الميت · توحيد هوية صفحات الموارد البشرية · تسمية صفحات 90 مساراً · بوابة جودة `tests/e2e/gate.js` (PASS) · المسح الصارم 89/94 بلا انحدار.

**⬜ المتبقي (بالترتيب):**
1. **جولة E2E الوظيفية الكاملة** — 30 من 84 شاشة كتابة مُختبَرة فقط (36%): المحاسبة ✔ · المشتريات (فاتورة الشراء) ✔ · التصنيع والمخازن ✔ · **الخزينة ✘** · **المبيعات ✘** · **المرتبات ✘** · **مصفوفة الصلاحيات ✘** · **الإقفال المالي ✘** · **تحويل المخزون (G2) ✘**
2. **إصلاح العيوب 14 المؤكَّدة** (`docs/PRODUCTION-READINESS-REPORT.md`) — 7 حرجة
3. **توحيد HTML صفحات HR** (١٢ قائمة + ٩ نماذج) — تمّ التوحيد **اللوني** فقط؛ البنية الهيكلية (رأس كحلي + بطاقات + شريط تصفية كما في «لوكسري») ما زالت مختلفة ⇒ تحتاج إعادة بناء
4. **المرحلة 3 (الجاهزية)**: CI · اختبارات xUnit · `net10.0` · سكربت تحقّق النشر · `database/schema.sql` من `sql.txt`

### 🔧 حالة البيئة الحيّة الآن
| العنصر | القيمة |
|---|---|
| **التطبيق** | يعمل على `http://localhost:5021` (عملية `dotnet run` خلفية — **قد تكون متوقفة بعد إغلاق الجلسة**) |
| **الجلسة المحفوظة** | `%LOCALAPPDATA%\Temp\kilo\e2e\session.json` (Blazor session مشفَّر + كوكيز) ⇒ **لا حاجة لتسجيل دخول جديد** |
| **بروفايل المتصفح** | `%LOCALAPPDATA%\Temp\kilo\e2e\profile` |
| **النتائج** | `%LOCALAPPDATA%\Temp\kilo\e2e\findings.jsonl` (كل نتيجة بـ `id` + `kind` + `ts`) |
| **الصور** | `%LOCALAPPDATA%\Temp\kilo\e2e\shots\` |
| **Playwright** | `@playwright/mcp` 0.0.83 مثبَّت عالمياً · `playwright` مضمَّن داخله (يُستدعى من `tests/e2e/helpers.js`) |

**⚠️ بيانات اختبار في القاعدة الحية (`db68995`) — تُنظَّف بـ `database/cleanup-e2e-testdata.sql`:**
```
أصناف   : TEST خام أ (RM-2026-000002) · TEST خام ب · TEST تعبئة أ · TEST منتج تام 1 (FG-2026-000001)
مخازن   : TEST-WH-RAW · TEST-WH-FG        مورد : TEST مورد · TEST مورد (مكرر)
مستند   : أوامر شراء PO-2026-000013..17 · إذونات استلام GRN-2026-000001..008
          إذونات صرف MI-2026-000007 · دفعة BT-2026-000002 · جرد SC-2026-000006/8/11
          سندات قبض RV-2026-000005..008 · سندات صرف PV-2026-000009/10 · مصروفات EXP-2026-000013/14
          فاتورة شراء PI-2026-000001 · أوامر تصنيع MO-2026-000007 · وصفات TEST وصفة 1
قيود    : JV-2026-000026..037 (منها 3 برأس تالف — 2 منها أخطاء مُسجَّلة في التقرير)
```
**الدفتر مُسترجَع** بعد كل جولة: `1101=99,600` · `41=0` · `5301=200` · `1104=0` · والصندوق `CB-001=99,600` = حسابه 1101.
**⚠️ ملاحظة:** `PI-2026-000001` **معتمدة** وحركت الأرصدة بشكل صحيح (110501+1,400 · 1108+196 · 2101−1,596) — تُركت كما هي لأنها معاملة صحيحة؛ تنظيفها يتطلب إعادة مزامنة `ChartOfAccounts.CurrentBalance` (لا توجد واجهة لذلك).

### 📌 كيف تُشغَّل الجولة التالية
```powershell
# 1) تأكّد أن التطبيق يعمل
dotnet run --project "D:\YK Coatings\YkWeb\YKCoatings.csproj"
# 2) شغّل سكربت الجولة (sessions.json موجود ⇒ لا تسجيل دخول)
& "C:\Program Files\nodejs\node.exe" "D:\YK Coatings\YkWeb\tests\e2e\run-<module>.js"
# 3) لو لم يكن الـsession صالحاً ⇒ node tests/e2e/login.js  (نافذة مرئية، المستخدم يكتب)
```

---

## ✅ إنجازات الجلسة 12 — أعمدة المحسوبة Computed Columns (2026-10-20)

### المشكلة
الكود كان يكتب مباشرة في **أعمدة محسوبة (computed columns)** في SQL Server، وهو ما يفشل وقت التشغيل بخطأ:
`The column "X" cannot be modified because it is either a computed column...`

### الطريقة
1. **مصدر الحقيقة = قاعدة البيانات الحية** (Azure SQL) — استُخرجت **37 عموداً محسوباً** عبر سكربت استعلام على `sys.computed_columns`.
2. **ماسح (scanner)** بلغة PowerShell يمسح كل `@"..."` SQL في `*.cs`/`*.razor` (خارج bin/obj/publish) ويطابق جداول/أعمدة الكتابة مع قائمة المحسوبات → كشف **5 انتهاكات**.
3. **إثبات حي قبل الإصلاح:** كل انتهاك أُعيد إنتاجه داخل معاملة (Transaction) على القاعدة → نفس الخطأ بالضبط؛ والصيغة المقترحة بعد الإصلاح نجحت (فشل فقط بـ FK لبيانات اختبار وهمية).

### الانتهاكات الخمسة والإصلاح
| # | الملف | الإصلاح |
|---|---|---|
| 1 | `Services\MaterialIssueInventoryPosting.cs` (~267) UPDATE `MaterialIssueDetails.LineCost` | حُذف `LineCost` من SET — يُشتق = `IssuedQty × UnitCost` |
| 2 | `Services\MaterialIssueInventoryPosting.cs` (~291) INSERT `LineCost` | حُذف العمود وزوج `@LineCost` |
| 3 | `Services\MaterialIssueService.cs` (~878) INSERT `LineCost` | حُذف العمود وزوج `@LineCost` (كان يحسبها C# يدوياً) |
| 4 | `Services\ProductionBatchService.cs` (~479) INSERT `ProductionBatches.UnitCost` | حُذف `UnitCost` وزوج `NULL` (بقي 29 عموداً مقابل 29 قيمة) |
| 5 | `Services\ProductionBatchService.cs` (~700) UPDATE `SET UnitCost=` | **الأهم:** صار `SET TotalCost = unitCost × GoodQty` لأن `UnitCost` مشتق = `TotalCost/GoodQty` |

### إصلاح #5 بالتفصيل (قرار تصميمي)
- الجدول **لا يملك** عمود تكلفة يدوي — فقط `TotalCost` (قابل للكتابة) و`UnitCost` (مشتق).
- لجعل `UnitCost` يساوي قيمة المستخدم اليدوية: نحفظ `TotalCost = unitCost × GoodQty` (مقرب لمنزلتين).
- أُضيف `GoodQty` إلى `BatchHeadDbDto` + قراءته في SELECT + حارس `GoodQty <= 0` برسالة واضحة (وإلا لكان `UnitCost` = 0 وفشل الترحيل لاحقاً برسالة غامضة).
- `changedColumns` في سجل التدقيق صار `TotalCost` (اسم العمود الحقيقي المكتوب).

### التحقق النهائي
- الماسح بعد الإصلاح: **`HITS=0`**.
- `dotnet build` → **نجاح · 0 Warning · 0 Error** (exit 0).
- **التطبيق شغّال** على `http://127.0.0.1:5021` (profile `http`) وصفحة الدخول تظهر صحيحة RTL بالعربية.

## 🧪 بيانات الاختبار الحي الجاهزة (من القاعدة)
- **الصلاحيات:** `admin` (role 1) له `CanView/CanAdd/CanEdit/CanPost/CanApprove` على `SCR_BATCH` و`SCR_MATISS` و`SCR_PRODORD`.
- **مسار إذن الصرف:** أمر التصنيع **25** (`MO-2026-000001`, status=2) له 4 مواد `POMaterialID 164–167` (أصناف 70·71·72·73) **ومخزونها يطابق المطلوب تماماً** في مخزن 1 — والأصناف: 5/40/20/35 مقابل مطلوب 5/40/20/35.
- **مسار الدفعات:** جدول `ProductionBatches` **فارغ** → تُنشأ دفعة من أمر 25 ثم QC ثم تكلفة يدوية ثم ترحيل. مخرجات: مخزن **4** (`مخزن المنتجات التامة`, type=4, aktif).
- جداول محسوبة **فارغة حالياً** = بيئة اختبار نظيفة (0 صف في `MaterialIssueNotes`/`MaterialIssueDetails`/`ProductionBatches`).
- اسم جدول رأس إذن الصرف هو **`MaterialIssueNotes`** (ليس `MaterialIssues`).

## ⚠️ ملاحظات تشغيلية للجلسة
- **مربع أخطاء البناء السابق كان قفلاً لا كوداً:** عملية `dotnet run` قديمة (بدأت 00:36) كانت تحجز `YKCoatings.dll` فتعليق البناء بلا رسالة خطأ. **الحل:** `Stop-Process` على `YKCoatings.exe` و`dotnet run` القديمين ثم إعادة البناء.
- **`localhost` يُرفض من متصفح Playwright** (IPv6) → استخدم **`http://127.0.0.1:5021`**. ⚠️ **تصحيح 2026-10-21:** العكس تماماً في الاختبار الأخير — `127.0.0.1:5021` أعطى `ECONNREFUSED` و`localhost:5021` عمل. التطبيق يستمع على IPv6 محلياً؛ استعمل `localhost`.
- **Blazor Server + `prerender: false` ⇒ الصفحة تبدأ فارغة** ويصل المحتوى عبر SignalR: أي `browser_navigate` + لقطة فوريَين ينتج **لقطة فارغة**. **لازم** تنتظر ر rendered text (ليس `childElementCount` — الوسوم تُحسب) قبل اللقطة.
- **حقول `@bind` في Blazor لا تلتقط `page.fill()`** — استخدم `HTMLInputElement.value` native setter + إطلاق `input` ثم `change` صراحةً، وإلا بقيت القيمة القديمة (كذلك `@bind:event="oninput"`).
- **`<label>` قد يلفّ حقله** (`<label class="sequence-field"><span>…</span><input></label>`) ⇒ البحث عن الحقل يجب أن يبدأ **من الـ label نفسه** لا من والده.
- **أزرار تحمل نفس النص في مكانين** (مثل `إضافة تسلسل` في الشريط وفي المودال) ⇒ يجب حصر النقر داخل المودال.
- **`>` في PowerShell يخزّن مخرجات `dotnet build` حتى انتهاء العملية** → استخدم `Start-Process -RedirectStandardOutput` للحصول على سجل حي.
- الماسح يتعامل فقط مع السلاسل المُداخلة `@"..."` — أي SQL بـ `"` عادي يحتاج مراجعة يدوية.

## 🔴 مطلوب في هذه الجلسة
1. **إصلاح العيوب الحرجة الثلاثة** من `docs/E2E-TEST-REPORT.md`: (أ) تضمين تسلسل `BOM` في بيانات التهيئة + `try/catch` في `BOMEdit.SaveHeaderAsync` · (ب) تضمين `InventoryBatchBalance` في `sp_ExecuteStockCountAdjustments` · (ج) تسوية الجرد تسحب تكلفة FIFO الفعلية لا `Items.AverageCost`.
2. 🔴 **العيب 9 (الجولة الثانية):** `RevenueAccountID` إلزامي في سند القبض + حذف `catch { }` من `TreasuryService.cs:691` (أو نقل تحديث الأرصدة داخل الـSP).
3. **العيب 10:** `TrialBalance.razor:61` ⇒ `L@(t.AccountLevel)`.
4. **`#blazor-error-ui` + صفحة إعادة اتصال** في `App.razor` (عيب: أي استثناء = شاشة بيضاء).
5. **`try/catch` في `NavMenu.OnInitializedAsync` / `PermissionService.LoadPermissionsAsync`** — انقطاع SQL Server يقتل الـ circuit.
6. **إكمال الجولة الثانية:** الخزينة · المبيعات · **فاتورة الشراء (الأخطر — `try/catch` مثبت في `PurchaseInvoiceService.cs:569`)** · المرتبات · مصفوفة الصلاحيات · Excel.
7. **تنظيف البيانات التجريبية** من القاعدة الحية — `database/cleanup-e2e-testdata.sql`
   (DRY RUN أولاً). المستندات التي أنشأتها الجولة الثانية:
   `PV-2026-000009/10` · `RV-2026-000005/6/7/8` · `EXP-2026-000013/14` ·
   قيود `JV-2026-000026/27/28/29/30/31/32/33/34/35` + قيود التصحيح.
8. **حذف كل ملفات `_tmp_*`** (تحوي كلمة مرور قاعدة البيانات) + **تدوير كلمة المرور** في `appsettings.json`.

## 🧹 ملفات `_tmp_*` المؤقتة النشطة هذه الجلسة (يجب حذفها كلها)
`_tmp_scan_final.ps1/.txt` · `_tmp_q_def.ps1/.txt` · `_tmp_verify.ps1/.txt` · `_tmp_pb_schema.ps1/.txt` ·
`_tmp_users.ps1/.txt` · `_tmp_testdata*.ps1/.txt` · `_tmp_probe.txt` · `_tmp_status*.txt` · `_tmp_st.txt` ·
`_tmp_build.log` · `_tmp_build_*.txt` · `_tmp_bout.txt` · `_tmp_berr.txt` · `_tmp_exit.txt` · `_tmp_run.txt` ·
`_tmp_runerr.txt` · `_tmp_runpid.txt` + كل `_tmp_*` السابق من الجلسات الماضية.

---
## ✅ إنجازات الجلسة 11 — تنظيف Settings (2026-10-09)
1. `SettingService.cs`: الأيقونات من إيموجي إلى SVG inline + الألوان بلوحة ذهبية (حذف `#3b82f6` الممنوع) + ترجمة `System`.
2. `Settings.razor`: عرض SVG عبر `(MarkupString)` + حالة التبديل بنقطة ملونة + تنظيف الرسالة والعنوان + إخفاء شارة الصفر + `sr-key` ذهبي + تمييز القراءة فقط.
3. المنطق محفوظ 100% — التطبيق توقف أثناء المعاينة (Connection Refused + WebSocket 1006) فيحتاج Restart؛ المعروض بالمتصفح كان النسخة القديمة المخزنة.

## ✅ إنجازات الجلسة 10 — توحيد PaymentTerms (2026-10-09)
1. تحويل الملف من UTF-16 إلى UTF-8 (كان يمنع القراءة والبحث) + حذف النسخة الاحتياطية.
2. توحيد كامل مع نمط `Currencies.razor`: ترويسة `luxury-header-hybrid` بشعار ذهبي + إحصائيات `stats-luxury` بأيقونات SVG + بطاقات `rates-luxury` + فلتر `filter-luxury` + جدول `table-luxury` + مودالات `modal-overlay-luxury` + توست `toast-luxury`.
3. صفر إيموجي: حذف 9 إيموجي من الماركب + تنظيف 4 رسائل توست · إصلاح `HtmlNoVal` غير المعرّف.
4. المنطق والصلاحيات `SCR_PAYTERMS` محفوظة 100% — التطبيق توقف أثناء المعاينة (Connection Refused) فتحتاج Restart لرؤية النتيجة.

## ✅ إنجازات الجلسة 9 — تبسيط Login (2026-10-09)
1. حذف `extra-info` (شارتا `اتصال مشفر وآمن` + `نظام ERP متكامل`) و CSS الخاص بهما نهائياً.
2. حذف `v2.0   ` من الفوتر (`footer-version`) والـ `PageTitle`، وبادج الهوية صار `نظام ERP متكامل`.
3. تكبير الشعار ليملأ عرض اللوحة: `.brand-logo-box` بعرض 100% (حتى 420px) والصورة بعرض مرن (حتى 320px).
4. تجاوب شامل: `1100px` تابلت عريض · `960px` عمود واحد · `520px` جوال مع `100dvh` وإخفاء الهالات · `360px` هواتف صغيرة · `1400px+` شاشات كبيرة.
5. المنطق كما هو 100% — التطبيق الشغّال يعرض النسخة القديمة حتى إعادة التشغيل.
6. **إلغاء السكرول (تحديث لاحق نفس الجلسة):** `height:100dvh` بدل `min-height` + `overflow:hidden` + ضغط شامل للمحتوى (كارد 1020px · حشوات 36px · شعار 140px · فوتر مضغوط) — الديسكتوب بلا سكرول نهائياً، والموبايل سكرول داخلي فقط عند الحاجة (`overflow-y:auto` تحت 960px).

## ✅ إنجازات الجلسة 8 — شاشة الدخول Split Gold & Black Premium (2026-10-08)
1. **إعادة كتابة `Components/Pages/Login.razor` بالكامل** (898 → 512 سطر) من **كارت أزرق مركزي** إلى **Split Screen ذهبي احترافي**: لوحة الهوية (شعار + اسم + بادج    + 3 ميزات بأيقونات SVG) + لوحة الدخول، داخل حاوية زجاجية `.split` بحد ذهبي وخط ذهبي علوي للصفحة.
2. **إصلاح تعارض الهوية الحرجم:** كان الشاشة **زرقاء كلياً** (خلفية `#060b16→#123a6b` · زر `#1d4ed8→#2563eb` · شبكة/هالات زرقاء · خط Cairo · شعار `yk-logo.svg` · `v1.0.0`) بينما النظام ذهبي — صار: خلفية `#070B14→#0A0F1E→#121A2E` (نفس `app-boot`) · هالات `rgba(212,175,55,.16)` · زر `#D4AF37→#9C7C2E` بنص `#0A0F1E` · خط **Tajawal** · شعار `yk-logo-white.png` · **`v2.0   `**.
3. **توافق مع أي شاشة:** استخدام متغيرات `--yk-gold/--yk-gold-light/--yk-gold-dark` بدل الألوان الصلبة حيث أمكن · نفس `radii/shadows/transitions` · تحققات: **OLDBlue=0** (لا أثر للأزرق القديم/`yk-logo.svg`).
4. **تفاصيل احترافية:** شبكة ذهبية رفيعة بـ `mask` · هالتان عامتان `orb` (blur 70) · دخول بـ `splitIn` · نبضة حول الشعار `pulseRing` · لمعة زر عند Hover `::after` · أنيميشن تنبيهات · `prefers-reduced-motion`.
5. **التجاوب:** `≤960px` يتحول لعمود واحد (لوحة الهوية تُخفي الميزات) · `≤520px` تكبير الخطوط ومنع zoom على الموبايل (`font-size:16px`).
6. **المنطق محفوظ 100%:** كل `@code` كما هو — `DoLogin` · `HandleKeyDown` · `TogglePassword` · `OnAfterRenderAsync` (استعادة الجلسة + توجيه `RequiresPasswordChange→/change-password`) · `ClientInfo` · جمع IP/MachineName · `@layout EmptyLayout`.
7. **التحقق:** فحص برمجي **MORE=0 · Emoji=0 · Gold=10 · OldBlue=0** · بناء **CompileErrors=0** (أخطاء `MSB3027/MSB3021` فقط = فشل نسخ لقفل التطبيق الشغّال PID 16700 — ليس خطأ كود). 512 سطر.


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
- **الوحدات المنفذة:** أساسية · مشتريات · مخازن · تصنيع · موارد بشرية · **مبيعات (فواتير + أوامر)** · **خزينة (  )** · **حسابات (   Pro)**.
- **المتبقي:** عروض أسعار · مرتجعات بيع · إقفال يومي · بطاقة تكلفة · QC · إضافة وحدات المبيعات/الخزينة/الحسابات للبحث الشامل · اختبار حي للشاشات الجديدة · توثيق سكيما قاعدة البيانات لوحدات جديدة.


## ✅ إنجازات الجلسة 6 — المبيعات + الخزينة + الحسابات (commit `d3565a5` «Fix : Home» — 2026-10-07)
1. **اكتمال وحدة المبيعات:** `SalesOrders.razor` + `SalesOrderEdit.razor` (`/sales-orders` + new/edit/view) + `SalesOrderService.cs` (810 سطر) + `Models/SalesOrderDtos.cs` (241) · وتطوير `SalesInvoiceService.cs` (+91). مبدأ الصفحة: «الفاتورة هي الأساس — أوامر البيع اختيارية».
2. **وحدة الخزينة —   :** `TreasuryService.cs` (1358 سطر) + `Models/TreasuryDtos.cs` (633 سطر) + 9 شاشات: خزائن وبنوك `/cash-boxes`+`/banks` · كشف خزينة `/cash-box-statement` · تحويلات نقدية `/cash-transfers` · حسابات بنكية `/bank-accounts` · سندات قبض `/receipt-vouchers` (+new/edit/view) · سندات صرف `/payment-vouchers` (+new/edit/view) · مصروفات `/expenses` (+new/edit) · شيكات `/cheques` · لوحة الخزينة `/treasury-dashboard` — مع «رصيد لحظي + قيد أوتوماتيك».
3. **وحدة الحسابات —    Pro:** `AccountingService.cs` (1132 سطر) + شاشات: شجرة حسابات `/chart-of-accounts` (عرضان: شجري/تفصيلي) · قيود يومية `/journal-entries` (+new/edit/view) · ميزان مراجعة `/trial-balance` · دفتر أستاذ `/account-ledger` · سنوات مالية `/fiscal-years` · مراكز تكلفة `/cost-centers`.
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
2. **تحية ديناميكية** — "صباح الخير / يوم سعيد / مساء الخير" حسب وقت اليوم (`GetGreeting()`).
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
