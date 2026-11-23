-- ============================================================
-- تسجيل شاشتي قائمة الدخل والميزانية العمومية في وحدات النظام (حزمة الإقفال والقوائم)
-- ============================================================
-- القائمة الجانبية مبنية من جدول SystemModules وصلاحياته؛ هذا الملف يضيف
-- الشاشتين الجديدتين تحت الوحدة الأم نفسها التي تتبعها شاشة ميزان المراجعة
-- (SCR_TRIAL) وبأيقونتها، دون أي منح صلاحيات تلقائي.
--
-- خطواتك:
--   1) شغّل هذا الملف مرة واحدة على قاعدة البيانات.
--   2) من شاشة إدارة الصلاحيات امنح الأدوار/المستخدمين المطلوبين صلاحية
--      «عرض» على الشاشتين الجديدتين (قائمة الدخل SCR_INCOME والميزانية
--      العمومية SCR_BS). وبدلًا من ذلك تفتح الشاشتان أيضًا لمن يملك صلاحية
--      عرض ميزان المراجعة أو السنوات المالية، وتصلان من أزرار أعلى شاشة
--      ميزان المراجعة مباشرة.
-- آمن لإعادة التشغيل: لا يكرر صفًا موجودًا.
-- ============================================================

DECLARE @ParentID INT, @TrialSort INT, @TrialIcon NVARCHAR(100);

SELECT @ParentID = ParentModuleID, @TrialSort = SortOrder, @TrialIcon = IconName
FROM dbo.SystemModules
WHERE ModuleCode = 'SCR_TRIAL';

IF @ParentID IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SystemModules WHERE ModuleCode = 'SCR_INCOME')
    BEGIN
        INSERT INTO dbo.SystemModules (ParentModuleID, ModuleCode, ModuleNameAr, IconName, ModuleType, SortOrder, IsActive)
        VALUES (@ParentID, 'SCR_INCOME', N'قائمة الدخل', @TrialIcon, 2, ISNULL(@TrialSort, 0) + 1, 1);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemModules WHERE ModuleCode = 'SCR_BS')
    BEGIN
        INSERT INTO dbo.SystemModules (ParentModuleID, ModuleCode, ModuleNameAr, IconName, ModuleType, SortOrder, IsActive)
        VALUES (@ParentID, 'SCR_BS', N'الميزانية العمومية', @TrialIcon, 2, ISNULL(@TrialSort, 0) + 2, 1);
    END
END
GO
