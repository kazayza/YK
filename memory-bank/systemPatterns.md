# 🧩 الأنماط المعمارية (System Patterns) — YKCoatings

## 1. نموذج الخدمة (Service-per-Domain)
كل وحدة لها خدمة ترث `BaseDbService`:
```csharp
namespace YKCoatings.Services
{
    public class XService : BaseDbService
    {
        private readonly AuditService _audit;
        public XService(IConfiguration configuration, AuditService audit) : base(configuration) { _audit = audit; }

        public async Task<List<XListDto>> GetXListAsync() { using var c = CreateConnection(); /* Dapper Query */ }
    }
}
```
- `BaseDbService` يوفر `CreateConnection()` من `DefaultConnection`.
- **التسجيل:** كل خدمة تُسجَّل في `Program.cs` كـ `AddScoped<T>()`.

## 2. نموذج DTO (Models)
- ملف DTO لكل مجال (`Models/ItemDtos.cs`…) بداخل فئات `List/Edit/Audit`Dto.
- قوائم تستخدم ListDto خفيف؛ النماذج تستخدم EditDto كامل؛ تعبئة القيم الفارغة بـ `ISNULL(...,0)` داخل SQL.

## 3. نموذج الصفحات (Razor Pages)
- `@page "/route"` + `<PageTitle>... - واي كي كوتينج</PageTitle>`.
- أنماط الصفحة داخل `<style>` في الملف نفسه (مع `@@` للهروب في Razor) — **مع نظام `yk-theme.css` الموحد يُفضَّل** استخدام كلاساته بدل تكرار CSS.
- دورة نموذجية: `OnInitializedAsync` ← تحميل + صلاحيات ← `isLoading` skeleton ← عرض.
- حقن نموذجي: `UserSessionService`, `PermissionService`, `NavigationManager`, `IJSRuntime`, خدمة المجال.

## 4. الصلاحيات
- `PermissionService` يحمّل صلاحيات المستخدم مرة واحدة (`IsLoaded`) من `dbo.vw_UserEffectivePermissions`.
- كل شاشة تتحقق: `PermissionService.Can("MODULE_CODE", "CanAdd")` (أو ما يشبهها) قبل إظهار أزرار الإضافة/التعديل/الحذف/الاعتماد.
- القوائم الجانبية (`NavMenu`) تُبنى ديناميكياً من `SystemModules` المسموحة فقط.
- رموز الموديولات موحّدة (`SCR_*`) ومساراتها في `Helpers/ModuleRouteHelper.cs`.

## 5. الجلسة والأمان
- `UserSessionService` (Scoped) + `ProtectedSessionStorage` بمفاتيح `s_*` (`s_uid`, `s_login`, …).
- استرجاع الجلسة في `MainLayout.OnAfterRenderAsync(firstRender)`.
- تسجيل الدخول: SHA-256 hash + قفل بعد محاولات فاشلة + سجل `LoginHistory` + انتهاء صلاحية كلمة المرور.
- `AuditService` يكتب كل إضافة/تعديل/حذف في سجل التدقيق.

## 6. التسلسل والترقيم
`SequenceService` يولّد أرقام المستندات (PR/PO/GRN/INV…) بصيغة بادئة + سنة + تسلسل.

## 7. الطباعة والتصدير
- تصدير Excel: ClosedXML في الخدمة → `downloadFile` (JS) بـ base64.
- طباعة: HTML منسق → `printHtml` (JS) في نافذة جديدة برأس يحمل شعار YK.

## 8. الإشعارات
`NotificationService` + استطلاع كل 30 ثانية في MainLayout + صوت عبر `playNotificationSound` (WebAudio) + لوحة إشعارات منسدلة.

## 9. مساعدات
- `ArabicSearchHelper` — بحث عربي متسامح (توشكي/همزات).
- `NavMenuIcons` — أيقونات SVG inline حسب `ModuleCode`.
- `SearchableSelect` — قائمة منسدلة قابلة للبحث (مكوّن مشترك).

## 10. الهوية البصرية
- **المتغيرات:** `--yk-primary` أزرق `#2563eb` · `--yk-accent` برتقالي `#f97316` (في `wwwroot/yk-theme.css`).
- **الشعارات:** `/images/yk-logo.svg` (شريط + دخول) و`/images/yk-icon.svg` (مصغّر/favicon).
- **الاسم المعروض:** واي كي كوتينج · YK Coatings — كودياً `YKCoatings`.
