# Skill: الهوية البصرية والتصميم (YK Coatings Design System)

## الألوان الرسمية
| الاستخدام | المتغير | القيمة |
|---|---|---|
| أساسي (أزرار، روابط، ترويسات) | `--yk-primary` | `#2563eb` |
| أساسي داكن | `--yk-primary-dark` | `#1d4ed8` |
| لون التمييز (دهانات/تنبيهات لطيفة) | `--yk-accent` | `#f97316` |
| نجاح / خطر / تحذير / معلومة | `--yk-success` `--yk-danger` `--yk-warning` `--yk-info` | `#16a34a` `#dc2626` `#f59e0b` `#0ea5e9` |
| الخلفية / السطح | `--yk-bg` / `--yk-surface` | `#eef2f7` / `#ffffff` |
| نص أساسي/ثانوي/خفيف | `--yk-text` `--yk-text-2` `--yk-text-3` | `#0f172a` `#475569` `#94a3b8` |
| حدود / شادو | `--yk-border` `--yk-shadow*` | `#e2e8f0` |

## الشعارات
- `/images/yk-logo.svg` — الشعار الكامل (شريط جانبي موسّع + صفحة الدخول).
- `/images/yk-icon.svg` — الأيقونة المختصرة (شريط مطوي + favicon).
- **لا تعدل** ألوان الشعار يدوياً — عدّل في ملف SVG (قيمة `#2563eb`/`#0ea5e9`/`#f97316`).

## القواعد
1. أي عنصر واجهة جديد: ابدأ بكلاسات `yk-theme.css` (`btn-primary`, `badge-soft-*`, `card`, `table`, `alert-*`, `yk-chip`) قبل كتابة CSS خاص.
2. CSS خاص داخل الصفحة فقط إذا كان تصميماً فريداً — واستخدم `@@` للهروب في razor.
3. الخط: Cairo — لا تستخدم خطوطاً أخرى.
4. RTL دائماً: `dir="rtl"` على الحاويات، و`border-right` (لا left) للحواف التمييزية.
5. الأيقونات: Bootstrap Icons أو SVG inline (كما في `NavMenuIcons.cs`).
6. 🚫 **ممنوع استخدام `#3b82f6`** (أزرق ما قبل الهوية) — تم توحيد كل المشروع إلى `#2563eb` (447 موضعاً في 71 ملف). أي لون جديد من متغيرات `yk-theme.css`.
7. 🚫 **ممنوع `filter: brightness(0) invert(1)` على الشعار** — الشعار مُصمَّم بألوان الهوية ويظهر كما هو.
8. الشعار يظهر **مرة واحدة فقط** في الترويسة: `yk-logo.svg` عند التوسيع، `yk-icon.svg` عند الطي (لا نص مكرر بجانبه).
9. حجم الشعار: `height: 40px` (توسيع) / `36×36px` (طي) — لا تُكبّر أكثر من `max-width: 172px`.

## أمثلة جاهزة
- زر أساسي: `<button class="btn btn-primary">حفظ</button>`
- شارة حالة: `<span class="badge badge-soft-success">نشط</span>`
- كرت: `<div class="card"><div class="card-header">عنوان</div><div class="card-body">…</div></div>`
- تنبيه: `<div class="alert alert-success">تم الحفظ بنجاح</div>`
