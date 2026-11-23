-- ============================================================
-- إعدادات حسابات التصنيع المحاسبية (حزمة قيود التصنيع + تصحيح 04b)
-- ============================================================
-- حسابات المخزون عائلة بحسب نوع البضاعة، لذا فُصل حساب المخزون إلى اثنين:
--   اعتماد إذن صرف مواد:  مدين «تشغيل تحت التصنيع» / دائن «مخزون المواد»
--                          بقيمة FIFO الفعلية لأسطر الإذن.
--   ترحيل دفعة منتج تام:   مدين «مخزون المنتجات التامة» / دائن «تشغيل تحت التصنيع»
--                          بتكلفة الدفعة (يدوية أو محسوبة من الفعلي).
-- قبل التهيئة (الإعدادات فارغة) يعمل المسار المخزني تمامًا كما هو اليوم
-- دون أي قيد — فالتفعيل قرارك وتوقيته لك. ملء أحد إعدادي أي زوج دون الآخر
-- يوقف ذلك المسار برسالة صريحة حتى لا تنشأ قيود ناقصة.
--
-- ملاحظة لمن شغّل النسخة الأولى من هذا الملف: الإعداد القديم
-- MfgInventoryAccountCode لم يعد مقروءًا من الكود؛ صفّه إن وُجد خامل بلا أثر.
--
-- خطواتك:
--   1) شغّل هذا الملف مرة واحدة على قاعدة البيانات (آمن لإعادة التشغيل).
--   2) من شاشة الإعدادات (مجموعة «التصنيع») املأ القيم بأكواد حسابات
--      أصول تفصيلية (النوع 1) من دليل حساباتك: التشغيل تحت التصنيع،
--      ومخزون المواد، ومخزون المنتجات التامة.
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'MfgWipAccountCode')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, SettingDescription, SettingGroup, DataType)
    VALUES ('MfgWipAccountCode', '', N'كود حساب التشغيل تحت التصنيع (WIP) المستخدم في قيود صرف المواد وإتمام دفعات الإنتاج', 'Manufacturing', 'Text');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'MfgMaterialsInventoryAccountCode')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, SettingDescription, SettingGroup, DataType)
    VALUES ('MfgMaterialsInventoryAccountCode', '', N'كود حساب مخزون المواد الذي يُدائن عند اعتماد أذون صرف مواد التصنيع', 'Manufacturing', 'Text');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'MfgFinishedInventoryAccountCode')
BEGIN
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, SettingDescription, SettingGroup, DataType)
    VALUES ('MfgFinishedInventoryAccountCode', '', N'كود حساب مخزون المنتجات التامة الذي يُدين عند ترحيل دفعات الإنتاج إلى المخزون', 'Manufacturing', 'Text');
END
GO
