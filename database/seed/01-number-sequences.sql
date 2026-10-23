-- =============================================================================
--  YK Coatings ERP — بيانات التهيئة: تسلسلات الأرقام (NumberSequences)
-- =============================================================================
--  المصدر     : db68995 (قاعدة الإنتاج) — لقطة 2026-10-21
--  الملف المرجعي: D:\YK Coatings\sql.txt (مخطط db68995)
--  الترتيب    : 01  ← يجب أن يسبق أي سكربت بيانات تهيئة أخرى
--
--  لماذا هذا الملف موجود:
--    لا توجد أي آلية بذر (seed/migration) في المستودع. بدون هذا الملف، أي قاعدة
--    بيانات جديدة تكون بلا تسلسلات ⇒ **كل** نوع مستند يفشل عند الحفظ، لأن
--    الخدمات تستدعي dbo.sp_GetNextNumber مباشرةً (بلا فحص IsActive)، والاستثناء
--    غير المعالج في الصفحات يُنهي Blazor circuit ⇒ شاشة بيضاء + فقدان جلسة.
--    عيب مثبت: تسلسل BOM كان مفقوداً ⇒ إنشاء الوصفة مستحيل كلياً.
--
-- Corrections applied vs. the live snapshot (both verified in the E2E run):
--    1) SQ  : كان Prefix = 'QT-' ⇒ ينتج 'QT--2026-000003' (شرطة مزدوجة).
--              تم تصحيحه إلى 'SQ' ⇒ 'SQ-2026-000003'.
--              (نفس صنف عيب تسلسل BOM الذي أُصلح — البادئة يجب ألا تنتهي بشرطة
--               لأن sp_GetNextNumber يضيف بنفسه.)
--    2) ITM : كان مفقوداً. Services/ItemService.cs:225 يستخدمه كاحتياطي لأنواع
--              الأصناف خارج 1..4 ⇒ بدونه يفشل إنشاء الصنف بغير معالجة.
--
--  رقم العدّاد (CurrentNumber) وآخر إعادة ضبط (LastResetYear) مأخوذان من الإنتاج
--  حتى يستأنف السكربت العدّاد من نفس النقطة. لقاعدة جديدة تماماً: اضبطهما 0.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- حارس: لا تُدرج تسلسلاً موجوداً مسبقاً (السكربت قابل لإعادة التشغيل).
IF NOT EXISTS (SELECT 1 FROM dbo.NumberSequences WHERE SequenceCode = N'BOM')
    INSERT INTO dbo.NumberSequences
        (SequenceCode, SequenceName, Prefix, CurrentNumber, NumberLength, IncludeYear, ResetYearly, LastResetYear, IsActive)
    VALUES
        (N'BOM',      N'أرقام الوصفات (تصنيع)',      N'BOM',     3, 6, 1, 1, 2026, 1),
        (N'BT',       N'رقم الباتش',                   N'BT',      2, 6, 1, 1, 2026, 1),
        (N'CD',       N'تسليم المنتجات للعميل',        N'CD',      4, 6, 1, 1, 2026, 1),
        (N'CMR',      N'استلام خامات من العميل',       N'CMR',     0, 6, 1, 1, 2026, 1),
        (N'CO',       N'عقود تصنيع للغير',            N'CO',      0, 6, 1, 1, 2026, 1),
        (N'CRT',      N'ارجاع خامات للعميل',           N'CRT',     0, 6, 1, 1, 2026, 1),
        (N'CT',       N'تحويل خزينة',                  N'CT',      0, 6, 1, 1, NULL, 1),
        (N'CUST',     N'تكويد العملاء',                N'CUST',    9, 6, 1, 1, 2026, 1),
        (N'EXP',      N'مصروف',                        N'EXP',     9, 6, 1, 1, 2026, 1),
        (N'FG',       N'كود منتج تام',                 N'FG',      1, 6, 1, 1, 2026, 1),
        (N'GRN',      N'إذن استلام',                   N'GRN',     7, 6, 1, 1, 2026, 1),
        (N'HR-EMP',   N'تكويد الموظفين',               N'HR-EMP', 25, 6, 0, 0, 2026, 1),
        (N'ITM',      N'كود صنف (احتياطي)',           N'ITM',     0, 6, 1, 1, NULL,  1),
        (N'JV',       N'قيد يومية',                    N'JV',     13, 6, 1, 1, 2026, 1),
        (N'LN',       N'طلب سلفة',                     N'LN',      3, 6, 1, 1, 2026, 1),
        (N'LV',       N'طلب إجازة',                    N'LV',      3, 6, 1, 1, 2026, 1),
        (N'MI',       N'إذن صرف خامات',                N'MI',      7, 6, 1, 1, 2026, 1),
        (N'MO',       N'أمر تصنيع',                    N'MO',      7, 6, 1, 1, 2026, 1),
        (N'PAY',      N'مسير مرتبات',                  N'PAY',     0, 6, 1, 1, NULL,  1),
        (N'PI',       N'فاتورة مشتريات',               N'PI',      0, 6, 1, 1, 2026, 1),
        (N'PM',       N'كود مادة تعبئة',               N'PM',      0, 6, 1, 1, NULL,  1),
        (N'PO',       N'أمر شراء',                     N'PO',     17, 6, 1, 1, 2026, 1),
        (N'PR',       N'طلب شراء',                     N'PR',      0, 6, 1, 1, 2026, 1),
        (N'PRET',     N'مرتجع مشتريات',               N'PRET',    0, 6, 1, 1, 2026, 1),
        (N'PV',       N'سند صرف',                      N'PV',      5, 6, 1, 1, 2026, 1),
        (N'QT',       N'عرض سعر (تسلسل قديم)',         N'QT',      0, 6, 1, 1, NULL,  1),
        (N'RM',       N'كود مادة خام',                 N'RM',      5, 6, 1, 1, 2026, 1),
        (N'RV',       N'سند قبض',                      N'RV',      2, 6, 1, 1, 2026, 1),
        (N'SAL-REP',  N'تكويد مندوب مبيعات',           N'SAL-REP', 0, 6, 0, 0, NULL,  1),
        (N'SC',       N'أمر جرد',                      N'SC',     11, 6, 1, 1, 2026, 1),
        (N'SF',       N'كود نصف مصنع',                 N'SF',      0, 6, 1, 1, NULL,  1),
        (N'SI',       N'فاتورة بيع',                   N'SI',      6, 6, 1, 1, 2026, 1),
        (N'SO',       N'أمر بيع',                      N'SO',      1, 6, 1, 1, 2026, 1),
        (N'SQ',       N'عروض الأسعار',                 N'SQ',      2, 6, 1, 1, 2026, 1),
        (N'SRET',     N'مرتجع مبيعات',                 N'SRET',    0, 6, 1, 1, NULL,  1),
        (N'ST',       N'تحويل مخزني',                  N'ST',      2, 6, 1, 1, 2026, 1),
        (N'SUP',      N'تكويد الموردين',               N'SUP',    29, 6, 1, 1, 2026, 1);

-- تصحيح بادئة SQ على قواعد تحتوي التسلسل بالفعل (BOM مثال: أُنشئ يدوياً قبل هذا الملف).
UPDATE dbo.NumberSequences
   SET Prefix = N'SQ'
 WHERE SequenceCode = N'SQ' AND Prefix IN (N'QT-', N'QT');

-- تصحيح أي بادئة منتهية بشرطة (مصدر خطأ 'BOM--' و 'QT--').
UPDATE dbo.NumberSequences
   SET Prefix = LEFT(Prefix, LEN(Prefix) - 1)
 WHERE Prefix LIKE N'%-'
   AND SequenceCode NOT IN (N'HR-EMP', N'SAL-REP')
   AND Prefix <> N'-';

-- تفعيل كل التسلسلات (sp_GetNextNumber لا يفحص IsActive، لكن الواجهة تفعل).
UPDATE dbo.NumberSequences SET IsActive = 1 WHERE IsActive IS NULL OR IsActive = 0;

COMMIT TRANSACTION;

-- ------------------------------ تقرير ------------------------------
SELECT SequenceCode, SequenceName, Prefix, CurrentNumber, NumberLength,
       IncludeYear, ResetYearly, LastResetYear, IsActive
  FROM dbo.NumberSequences
 ORDER BY SequenceCode;

-- تحقق: يجب ألا يبقى أي تسلسل بصيغة بادئة منتهية بشرطة.
SELECT SequenceCode, Prefix
  FROM dbo.NumberSequences
 WHERE Prefix LIKE N'%-'
   AND SequenceCode NOT IN (N'HR-EMP', N'SAL-REP');
-- ↑ يجب أن يعود 0 صفوف.
