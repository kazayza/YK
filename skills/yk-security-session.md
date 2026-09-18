# Skill: الجلسة والصلاحيات والأمان (YKCoatings)

## الجلسة (UserSessionService)
- بياناتها: `UserID, Username, FullName, RoleID, RoleName, RoleCode, EmployeeID, MustChangePassword, PasswordExpired, IsLoggedIn`.
- التخزين: `ProtectedSessionStorage` بمفاتيح `s_uid, s_uname, s_fname, s_rid, s_rname, s_eid, s_mcp, s_pex, s_rcode, s_login`.
- **لا تضف مفتاحاً جديداً** إلا بتحديث `LoginAsync` + `TryRestoreSessionAsync` + `LogoutAsync` معاً (3 أماكن).
- استخدم `CurrentEmployeeID` للحقول الوظيفية و`CurrentUserID` للحقول التدقيقية.

## الصلاحيات (PermissionService)
- يُحمَّل مرة/دائرة: `await PermissionService.LoadPermissionsAsync(UserSession.UserID)`.
- الفحص قبل أي زر كتابة، مثال:
```csharp
@if (PermissionService.Can("SCR_ITEMS", "CanEdit")) { <button>تعديل</button> }
```
- رموز الوحدات في `Helpers/ModuleRouteHelper.cs` + جدول `SystemModules`.

## قواعد أمان صارمة
1. SQL parameterized فقط.
2. كلمات المرور: SHA-256 (النمط الحالي في `AuthService.HashPassword`) — أي ميزة جديدة **تستخدم نفس الدالة** للحفاظ على التوافق.
3. لا تُعرض أسرار (connection strings) في الكود المصدري الجديد — استخدم user-secrets للبيئة المحلية:
   ```powershell
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."
   ```
4. حماية كل صفحة: أول سطر في `OnInitializedAsync` يجب أن يكون فحص `UserSession.IsLoggedIn`.
5. التحقق من إجبار تغيير كلمة المرور (`RequiresPasswordChange`) يحدث تلقائياً في MainLayout — لا تلغِه.
6. سجل كل كتابة في `AuditService` — ممنوع تجاوزه للمستندات المالية.

## مهلة الجلسة
- JS: `sessionTimeoutManager.init(dotnetRef, timeoutMins, warningMins)` — ينبّه `ShowSessionWarning` ثم `ForceLogoutFromIdle`.
- الافتراضي: 30 دقيقة، تحذير قبلها بدقيقتين. التعديل من `MainLayout`.
