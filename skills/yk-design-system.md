# Skill: الهوية البصرية والتصميم (YK Coatings Design System)

> **مصدر الحقيقة: `wwwroot/yk-theme-gold.css`** — كل ما يلي مستمدّ منه.
> أي قيمة هنا لا تطابقreality ⇒ خطأ في هذه الوثيقة أو في الملف.

## 1 · الألوان الرسمية

| الاستخدام | المتغير | القيمة |
|---|---|---|
| **أساسي (أزرار، روابط، ترويسات، حدود مميّزة)** | `--yk-primary` / `--yk-gold` | `#D4AF37` |
| أساسي فاتح / غامق | `--yk-gold-light` / `--yk-gold-dark` | `#E8C86A` / `#9C7C2E` |
| تدرّجات شفافة | `--yk-gold-10/15/20` | `rgba(212,175,55,.10/.15/.20)` |
| **الأسطح (كحلي)** | `--yk-black` / `--yk-black2` / `--yk-black3` / `--yk-black4` | `#070B14` / `#0A0F1E` / `#121A2E` / `#1A2540` |
| خلفية / سطح فاتح | `--yk-bg` / `--yk-surface` | `#f8f7f4` / `#ffffff` |
| نص أساسي / ثانوي / خفيف | `--yk-text` `--yk-text-2` `--yk-text-3` | `#0A0F1E` / `#475569` / `#94a3b8` |
| حدود | `--yk-border` / `--yk-border-gold` | `#e8e2d0` / `rgba(212,175,55,.18)` |
| نجاح / خطر / تحذير / معلومة | `--yk-success` `--yk-danger` `--yk-warning` `--yk-info` | `#059669` / `#dc2626` / `#d97706` / `#0284c7` |
| نصف قطر / ظل / انتقال | `--yk-radius` `--yk-shadow` `--yk-shadow-lg` `--yk-transition` | `12px` · `0 4px 16px rgba(0,0,0,.06)` · `0 12px 32px rgba(0,0,0,.12)` · `.25s cubic-bezier(.4,0,.2,1)` |

**الأسماء القديمة** `--gold` / `--black` / `--border-gold` / `--body-bg` **أسماء مستعارة** لـ`--yk-*`
(معرَّفة في `yk-theme-gold.css`) — استعمل `--yk-*` دائماً.

## 2 · الخطوط

| الخط | الاستخدام | المصدر |
|---|---|---|
| **Tajawal** | كل شيء (200 · 400 · 500 · 700 · 800) | **محلي**: `wwwroot/fonts/tajawal-*.woff2` + `wwwroot/fonts/tajawal.css` |

> 🚫 **ممنوع أي خط من CDN** — لا Google Fonts ولا غيره.
> 🚫 لا تستخدم Cairo ولا أي خط آخر (الوثائق القديمة ذكرته بالخطأ).

## 3 · الأيقونات

**التقنية المعتمدة: Bootstrap Icons 1.11.3 — مستضافة محلياً.**
- الملف: `wwwroot/bootstrap-icons/bootstrap-icons.min.css` + `wwwroot/bootstrap-icons/fonts/*`
- الترخيص: `wwwroot/bootstrap-icons/LICENSE` (MIT)
- **صيغة الإدراج:** `<i class=bi bi-NAME></i>`
  - **صنف واحد بدون مسافة** — وهي الصيغة الوحيدة التي تنجو داخل السلاسل الحرفية في C#.
  - `bi` وحده يكفي لأن مُحدِّد Bootstrap Icons هو `[class^=bi-]::before`.
  - **لا تكتب** `class="bi bi-x"` داخل Razor (يكسر السلاسل الحرفية) ولا `bi bi-` مكرّراً.
- الأحجام تُضبط في `wwwroot/yk-icons.css` (سلّم موحّد: 9 / 12 / 14 / 18 / 30 / 44px).
- **SVG inline** مسموح للأيقونات الفريدة (`NavMenuIcons.cs` · `LucideIcons.cs` · `GoldIcons`).
- 🚫 **ممنوع الإيموجي نهائياً** — انظر `docs/ICON-MAPPING.md`.
- 🚫 **ممنوع الاعتماد على CDN** (أيقونات · خطوط · مخططات).

## 4 · المخططات
`ApexCharts 8.4.1` — **محلي**: `wwwroot/js/apexcharts.min.js` (بلا ملف CSS — لم يكن محمَّلاً أصلاً).
> 🚫 لا ترجع إلى `cdn.jsdelivr.net` — النسخة المثبّتة pinned 8.4.1 (ما قبلها يُعطي أخطاء `NaN` في SVG).

## 5 · البنية

| الملف | الدور |
|---|---|
| `wwwroot/yk-theme-gold.css` | **مصدر الحقيقة** — الرموز + الطبقة الأساسية (يُحمَّل أولاً) |
| `wwwroot/yk-theme.css` | طبقة قديمة (أزرق) — **مهملة**، تُحمَّل قبل الذهبي فيُغلب عليها |
| `wwwroot/yk-gold-pages.css` | صفحات التصنيع (عائلة `yk-gold-page`) |
| `wwwroot/yk-hr-overrides.css` | **طبقة توحيد الهوية لصفحات الموارد البشرية** (فوق الـ1,390 لوناً المكتوب يدوياً) |
| `wwwroot/yk-icons.css` | أحجام الأيقونات + أيقونات بطاقات الرسائل |
| `wwwroot/fonts/tajawal.css` | `@font-face` محلي |

**ترتيب التحميل في `App.razor` (مهم):**
```
bootstrap.min.css → bootstrap-icons → yk-theme.css → yk-theme-gold.css
→ YKCoatings.styles.css → yk-gold-pages.css → yk-hr-overrides.css → yk-icons.css
```

## 6 · القواعد

1. **ابدأ دائماً بمتغير `--yk-*`** — لا تكتب لوناً صريحاً في CSS جديد.
2. **صفحة جديدة**: خذ بنية من نفس العائلة (لوكسري / yk-gold-page / items-page) — لا تخترع بنية رابعة.
3. **RTL دائماً**: `dir="rtl"`، و`border-inline-start` (لا `border-left`).
4. **الشعارات**: `wwwroot/images/yk-logo-gold.png` (كحلي) · `yk-logo-white.png` (فاتح) · `yk-icon-gold.png` (favicon). **لا توجد ملفات SVG** — الوثائق القديمة أخطأت.
5. **الصفحة لا تعيد تعريف `:root`** — الرموز موروثة من `yk-theme-gold.css`.
6. **الأيقونة داخل `<button>`**: `class="btn-luxury"` + `<i class=bi bi-x></i>`.
7. **رسالة نجاح/خطأ**: **لا تضع الإيموجي في النص** — البطاقة تحمل الأيقونة (`.toast-card-luxury::before`).
8. **لا تستخدم `filter: brightness(0) invert(1)`** على الشعار.
9. **الشعار يظهر مرة واحدة** في الترويسة.

## 7 · أمثلة

```razor
@* زر أساسي *@
<button class="btn-luxury"><i class=bi bi-check-lg></i> حفظ</button>

@* شارة حالة *@
<span class="status-badge-luxury">نشط</span>

@* بطاقة إحصائية (العائلة الذهبية) *@
<div class="stat-card"><div class="st-icon"><i class=bi bi-box-seam></i></div><div class="st-value">136</div></div>

@* رسالة نجاح — بلا إيموجي في النص *@
<div class="toast-luxury"><div class="toast-card-luxury success"><span>تم الحفظ بنجاح</span></div></div>
```

```css
/* CSS جديد — دائماً بالرموز */
.my-card { background: var(--yk-surface); border: 1px solid var(--yk-border-gold); border-radius: var(--yk-radius); }
.my-card__title { color: var(--yk-gold-dark); }
```

## 8 · 🚫 قائمة المنع (تُراجَع في المراجعة)

| الممنوع | البديل |
|---|---|
| `#2563eb` · `#1d4ed8` · `#eff6ff` (أزرق) | `--yk-primary` · `--yk-gold-10` |
| `#eef2f7` · `#f1f5f9` (رمادي بارد) | `--yk-bg` · `--yk-surface` |
| أي إيموجي | `<i class=bi bi-NAME></i>` |
| أي `<link>`/`<script>` خارجي | استضافة محلية |
| `class="bi bi-x"` داخل Razor | `class=bi bi-x` |
| `new string('★', n)` | `@for(...){<i class=bi bi-star-fill></i>}` |
