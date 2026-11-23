-- ============================================================
-- الحسابان الوسيطان لدورة الشيكات المحاسبية (حزمة الشيكات)
-- ============================================================
-- 1109 «شيكات تحت التحصيل» — أصول (النوع 1)، تحت «11 الأصول المتداولة»:
--   يُدين عند ربط شيك وارد بسند قبض معتمد، ويُدائن عند التحصيل أو الارتجاع.
-- 2108 «شيكات صادرة تحت التسوية» — التزامات (النوع 2)، تحت «21 الخصوم المتداولة»:
--   يُدائن عند ربط شيك صادر بسند صرف معتمد، ويُدين عند التسوية من البنك أو الارتجاع.
-- الكود يبحث عن الحسابين بالكود نفسه ويرفض العمل إن لم يكونا حسابين
-- تفصيليين نشطين بالنوع الصحيح؛ هذا الملف ينشئهما إن لم يكونا موجودين.
-- آمن لإعادة التشغيل: إن وُجد الكود قائمًا لا يغيّر شيئًا.
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts WHERE AccountCode = '1109')
BEGIN
    INSERT INTO dbo.ChartOfAccounts
        (AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel,
         AccountType, AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired,
         OpeningBalance, CurrentBalance, LinkedEntityType, LinkedEntityID,
         IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, CreatedBy, CreatedDate)
    VALUES
        ('1109', N'شيكات تحت التحصيل', 'Cheques Under Collection',
         (SELECT AccountID FROM dbo.ChartOfAccounts WHERE AccountCode = '11'), 3,
         1, 1, 1, NULL, 0,
         0, 0, NULL, NULL,
         1, 0, 0, N'حساب وسيط لدورة الشيكات الواردة', 1, NULL, GETDATE());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ChartOfAccounts WHERE AccountCode = '2108')
BEGIN
    INSERT INTO dbo.ChartOfAccounts
        (AccountCode, AccountNameAr, AccountNameEn, ParentAccountID, AccountLevel,
         AccountType, AccountNature, IsDetailAccount, CurrencyID, CostCenterRequired,
         OpeningBalance, CurrentBalance, LinkedEntityType, LinkedEntityID,
         IsSystemAccount, IsCashFlowAccount, SortOrder, Description, IsActive, CreatedBy, CreatedDate)
    VALUES
        ('2108', N'شيكات صادرة تحت التسوية', 'Outgoing Cheques Under Settlement',
         (SELECT AccountID FROM dbo.ChartOfAccounts WHERE AccountCode = '21'), 3,
         2, 2, 1, NULL, 0,
         0, 0, NULL, NULL,
         1, 0, 0, N'حساب وسيط لدورة الشيكات الصادرة', 1, NULL, GETDATE());
END
GO
