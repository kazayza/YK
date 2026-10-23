# جدول تحويل الإيموجي ➜ Bootstrap Icons

**قاعدة ذهبية:** كل أيقونة تُدرَج بصيغة **`<i class=bi bi-NAME></i>`** — صنف واحد بدون مسافة.
السبب: مُحدِّد Bootstrap Icons هو `[class^=bi-]::before`، وهذه الصيغة وحدها تنجو داخل
السلاسل الحرفية في C# (`$@"…"`) دون كسر مُحلِّل Razor.

> 🚫 لا تكتب `class="bi bi-x"` في Razor (يكسر السلاسل الحرفية).
> 🚫 لا تكتب `bi bi-` مكرّراً.
> 🚫 لا تضع الإيموجي داخل نص يُعرض للمستخدم — **البطاقة تحمل الأيقونة** (`.toast-card-luxury::before`).

---

## 1 · المحرف ➜ الأيقونة

| المحرف | Codepoint | `bi` | المعنى | ملاحظات |
|---|---|---|---|---|
| ✅ | U+2705 | `check-circle-fill` | نجاح | **أُزيل من نص الرسالة** — البطاقة تحمل الأيقونة |
| ✔ | U+2714 | `check-lg` | علامة صح | |
| ✓ | U+2713 | `check2` | صح | |
| ❌ | U+274C | `x-circle-fill` | فشل | **أُزيل من نص الرسالة** |
| ⛔ | U+26D4 | `lock-fill` | محظور | في صفحة «لا صلاحية» |
| 🔒 | U+1F512 | `lock-fill` | مقفل | |
| 🔓 | U+1F513 | `unlock` | مفتوح | |
| 👁️ | U+1F441 | `eye-fill` | عرض |Procedures أزرار الصف |
| ✏️ | U+270F | `pencil-fill` | تعديل | |
| 🗑️ | U+1F5D1 | `trash-fill` | حذف | |
| 💰 | U+1F4B0 | `cash-stack` | المال | |
| 💳 | U+1F4B3 | `credit-card` | بطاقة/دفع | |
| 🧾 | U+1F9FE | `receipt` | إيصال | |
| 📄 | U+1F4C4 | `file-earmark-text` | مستند | |
| 📋 | U+1F4CB | `clipboard-fill` | نسخ/لوحة | |
| 📎 | U+1F4CE | `paperclip` | مشبك | زر «تصدير» |
| 📥 | U+1F4E5 | `download` | تنزيل | |
| 📤 | U+1F4E4 | `upload` | رفع | |
| 📊 | U+1F4CA | `bar-chart` | إحصاء/مخطط | |
| 📅 | U+1F4C5 | `calendar3` | تاريخ | |
| 🗓️ | U+1F5D3 | `calendar3` | تقويم | |
| 📦 | U+1F4E6 | `box-seam` | عبوة/صنف | |
| 📥📤 | U+1F4E5/U+1F4E4 | `download`/`upload` | | |
| ⏰ | U+23F0 | `clock` | وقت/موعد | |
| ⏱️ | U+23F1 | `stopwatch` | مؤقّت | |
| ⏳ | U+23F3 | `hourglass-split` | انتظار | |
| 🔍 | U+1F50D | `search` | بحث | |
| 🔄 | U+1F504 | `arrow-repeat` | تحديث | |
| 🔔 | U+1F514 | `bell-fill` | إشعار | |
| 🔕 | U+1F515 | `bell-slash` | لا إشعارات | |
| ⚙️ | U+2699 | `gear-fill` | إعدادات | |
| 🔧 | U+1F527 | `wrench` | صيانة | |
| ⚖️ | U+2696 | `scales` | ميزان/تسوية | |
| ⚡ | U+26A1 | `lightning-charge-fill` | سريع/فوري | |
| ⚠️ | U+26A0 | `exclamation-triangle-fill` | تحذير | |
| 🔶 | U+1F536 | `exclamation-triangle-fill` | انتباه | |
| 🟢 | U+1F7E2 | `check-circle-fill` | نشط | **أُزيل من `<option>`** — الخيارات لا تحمل أيقونات |
| 🟡 | U+1F7E1 | `exclamation-circle-fill` | متوسط | |
| 🔴 | U+1F534 | `circle-fill` | متوقف | |
| ⚪ | U+26AA | `circle` | محايد | |
| ● | U+25CF | `circle-fill` | نقطة | |
| ❔ | U+2754 | `question-circle` | غير معروف | |
| ⏸ | U+23F8 | `pause-circle-fill` | إيقاف | |
| ★ | U+2605 | `star-fill` | تقييم ممتلئ | **دالّة** — عبر `@for` لا `new string` |
| ☆ | U+2606 | `star` | تقييم فارغ | |
| ⭐ | U+2B50 | `star-fill` | مميّز | **أُزيل من النص** |
| 🏆 | U+1F3C6 | `trophy-fill` | إنجاز | |
| 👤 👥 | U+1F464/U+1F465 | `person`/`people-fill` | موظفون | |
| 🏢 🏭 | U+1F3E2/U+1F3ED | `building` | مؤسسة/مصنع | |
| 💼 | U+1F4BC | `briefcase` | مهنة/مندوب | |
| 👔 | U+1F454 | `person-badge` | مسمى وظيفي | |
| 👑 | U+1F451 | `award` | مدير عام | |
| 🖨️ | U+1F5A5 | `printer-fill` | طباعة | |
| 🔒🗝️ | U+1F512 | `lock-fill` | قفل | |
| 🌴 | U+1F334 | `tree` | حضور | |
| 🧮 | U+1F9EE | `calculator-fill` | حساب | |
| 🏖️ | U+1F3D6 | `calendar3` | إجازة | |
| ➕ ➖ | U+2795/U+2796 | `plus-lg`/`dash-lg` | | |
| 🚫 | U+1F6AB | `slash-circle` | ممنوع | |
| 🛡️ | U+1F6E1 | `shield-fill` | حماية | |
| 🔗 | U+1F517 | `link-45deg` | رابط | |
| 🧾💳 | — | `receipt`/`credit-card` | | |
| ▲ ▼ | U+25B2/U+25BC | `chevron-up`/`chevron-down` | ترتيب | **دالّة** — عبر `RenderFragment` |
| ← → | U+2190/U+2192 | `arrow-left`/`arrow-right` | اتجاه | |
| ↑ ↓ | U+2191/U+2193 | `arrow-up`/`arrow-down` | اتجاه | |
| ↳ | U+21B3 | — | إزاحة شجرة | **أُزيل** (Excel ⇒ مسافات) |

**أيقونات جديدة استُخدمت ولم تكن في النظام:** `calculator-fill` · `box-seam` · `clock` · `credit-card` · `lightning-charge-fill` · `person-badge` · `question-circle` · `scales` · `search` · `star` · `star-fill` · `tree` · `wrench` · `arrow-repeat` · `arrow-left` · `arrow-right` · `arrow-up` · `arrow-down` · `chevron-up` · `chevron-down` · `exclamation-circle-fill` · `circle` · `circle-fill` · `eye-fill` · `pencil-fill` · `trash-fill` · `check-circle-fill` · `x-circle-fill` · `lock-fill` · `unlock` · `cash-stack` · `file-earmark-text` · `clipboard-fill` · `paperclip` · `download` · `upload` · `bar-chart` · `calendar3` · `hourglass-split` · `stopwatch` · `bell-fill` · `bell-slash` · `gear-fill` · `exclamation-triangle-fill` · `pause-circle-fill` · `check-lg` · `check2` · `building` · `briefcase` · `people-fill` · `printer-fill` · `award` · `box-seam-fill` · `shield-fill` · `slash-circle` · `link-45deg` · `plus-lg` · `dash-lg`

---

## 2 · مواضع الاستبدال (المنهجية)

| الموضع | قبل | بعد |
|---|---|---|
| **نص رسالة نجاح/خطأ** | `"تم الحفظ ✅"` | `"تم الحفظ"` + الأيقونة في البطاقة عبر CSS |
| **`<option>`** | `<option>📁 كل الأقسام</option>` | `<option>كل الأقسام</option>` (الخيارات لا تحمل أيقونات) |
| **حاوية أيقونة** | `<div class="st-icon">📦</div>` | `<div class="st-icon"><i class=bi bi-box-seam></i></div>` |
| **زر إجراء** | `<button>👁️</button>` | `<button><i class=bi bi-eye-fill></i></button>` |
| **تعبير Razor** | `@(c ? "✅" : "❌")` | `@if(c){<i class=bi bi-check-circle-fill></i>}else{<i class=bi bi-x-circle-fill></i>}` |
| **دالّة ترتيب** | `return sortAsc ? "▲" : "▼";` | `RenderFragment` يُعيد `<i class="bi bi-chevron-up"></i>` |
| **تقييم نجوم** | `new string('★', n)` | `@for(int i=0;i<n;i++){<i class=bi bi-star-fill></i>}` |
| **SQL / وصف تدقيق** | `N'نفد ❌'` | `N'نفد'` (نص نظيف) + الأيقونة في العرض |
| **خلية Excel** | `new string('⭐', r)` | `r + " / 5"` (Excel لا يعرض HTML) |

---

## 3 · بوابة الجودة

`tests/e2e/gate.js` يمنع:
1. **أي إيموجي** في `Components/` · `Services/` · `wwwroot/js/` (عدا الأسهم داخل التعليقات).
2. **أي اعتماد خارجي** (CDN) في `App.razor` أو ملفات CSS.
3. **`<i class="bi bi-`** (بادئة مكرّرة) أو `class="bi bi-` (يكسر السلاسل الحرفية).

شغّلها: `node tests/e2e/gate.js` — يجب أن تطبع `PASS` وأن تفشل عند أي مخالفة.
