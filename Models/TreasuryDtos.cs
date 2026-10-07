namespace YKCoatings.Models
{
    // ==========================================
    // الخزائن - على الجداول الحقيقية
    // CashBoxes: CashBoxID, CashBoxCode, CashBoxNameAr/En, AccountID, CurrencyID, CurrentBalance, CustodianEmployeeID, IsActive, CreatedDate + Patch: OpeningBalance, CashBoxType, IsDefault, MaxLimit, MinLimit, Notes
    // ==========================================
    public class CashBoxListDto
    {
        public int CashBoxID { get; set; }
        public string? CashBoxCode { get; set; }
        public string? CashBoxNameAr { get; set; }
        public string? CashBoxNameEn { get; set; }
        public int CashBoxType { get; set; } = 1;
        public string? CashBoxTypeName { get; set; }
        public int? AccountID { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountNameAr { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal CurrentBalanceCalc { get; set; }
        public decimal TotalReceipts { get; set; }
        public decimal TotalPayments { get; set; }
        public decimal TotalIn { get; set; }
        public decimal TotalOut { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public bool IsActive { get; set; }
        public bool IsDefault { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class CashBoxDto
    {
        public int CashBoxID { get; set; }
        public string? CashBoxCode { get; set; }
        public string? CashBoxNameAr { get; set; }
        public string? CashBoxNameEn { get; set; }
        public int CashBoxType { get; set; } = 1;
        public int? AccountID { get; set; }
        public string? AccountNameAr { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public int? CustodianEmployeeID { get; set; }
        public string? CustodianName { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDefault { get; set; }
        public decimal? MaxLimit { get; set; }
        public decimal? MinLimit { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    // ==========================================
    // سندات القبض - على الجدول الحقيقي ReceiptVouchers
    // ReceiptID, ReceiptNumber, ReceiptDate, ReceivedFrom, EntityType, EntityID, PaymentMethod, CashBoxID, BankAccountID, Amount, CurrencyID, ExchangeRate, ChequeNumber, ChequeDate, ChequeBankName, TransferRef, Description, ReceiptStatus, JournalID, Notes
    // ==========================================
    public class ReceiptVoucherListDto
    {
        public int ReceiptID { get; set; }
        public string? ReceiptNumber { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string? ReceivedFrom { get; set; }
        public int? EntityType { get; set; }
        public int? EntityID { get; set; }
        public string? EntityName { get; set; } // اسم العميل/المورد من Join
        public string? CustomerNameAr { get; set; }
        public string? CustomerCode { get; set; }
        public string? SupplierNameAr { get; set; }
        public int CashBoxID { get; set; }
        public string? CashBoxNameAr { get; set; }
        public string? CashBoxCode { get; set; }
        public int? BankAccountID { get; set; }
        public string? BankAccountName { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountLocal { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public int PaymentMethod { get; set; }
        public string? PaymentMethodName { get; set; }
        public string? Description { get; set; }
        public string? ReferenceNo { get; set; }
        public int ReceiptStatus { get; set; }
        public string? StatusName { get; set; }
        public bool IsPosted { get; set; }
        public int? JournalID { get; set; }
        public string? JournalNumber { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? CreatedByName { get; set; }
    }

    public class ReceiptVoucherDto
    {
        public int ReceiptID { get; set; }
        public string? ReceiptNumber { get; set; }
        public DateTime ReceiptDate { get; set; } = DateTime.Today;
        public string? ReceivedFrom { get; set; }
        public int? EntityType { get; set; } = 1; // 1=Customer 2=Supplier 3=Other
        public int? EntityID { get; set; }
        public string? EntityName { get; set; }
        public int CashBoxID { get; set; }
        public string? CashBoxNameAr { get; set; }
        public int? BankAccountID { get; set; }
        public string? BankAccountName { get; set; }
        public decimal Amount { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public decimal AmountLocal { get; set; }
        public int PaymentMethod { get; set; } = 1; // 1=نقدي 2=تحويل 3=شيك
        public string? ChequeNumber { get; set; }
        public DateTime? ChequeDate { get; set; }
        public string? ChequeBankName { get; set; }
        public string? TransferRef { get; set; }
        public string? Description { get; set; }
        public int? RevenueAccountID { get; set; }
        public string? RevenueAccountNameAr { get; set; }
        public int? CostCenterID { get; set; }
        public int ReceiptStatus { get; set; } = 1; // 1=مسودة 2=معتمد 3=ملغي
        public string StatusName => ReceiptStatus switch { 1 => "مسودة", 2 => "معتمد", 3 => "ملغي", _ => "غير محدد" };
        public int? JournalID { get; set; }
        public bool IsPosted { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    // ==========================================
    // سندات الصرف - على الجدول الحقيقي PaymentVouchers
    // ==========================================
    public class PaymentVoucherListDto
    {
        public int PaymentID { get; set; }
        public string? PaymentNumber { get; set; }
        public DateTime PaymentDate { get; set; }
        public string? PaidTo { get; set; }
        public int? EntityType { get; set; }
        public int? EntityID { get; set; }
        public string? EntityName { get; set; }
        public string? SupplierNameAr { get; set; }
        public string? CustomerNameAr { get; set; }
        public int CashBoxID { get; set; }
        public string? CashBoxNameAr { get; set; }
        public int? BankAccountID { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountLocal { get; set; }
        public string? CurrencyCode { get; set; }
        public int PaymentMethod { get; set; }
        public string? PaymentMethodName { get; set; }
        public string? Description { get; set; }
        public int PaymentStatus { get; set; }
        public string? StatusName { get; set; }
        public bool IsPosted { get; set; }
        public int? JournalID { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class PaymentVoucherDto
    {
        public int PaymentID { get; set; }
        public string? PaymentNumber { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.Today;
        public string? PaidTo { get; set; }
        public int? EntityType { get; set; } = 1; // 1=Supplier 2=Customer 3=Other
        public int? EntityID { get; set; }
        public string? EntityName { get; set; }
        public int CashBoxID { get; set; }
        public string? CashBoxNameAr { get; set; }
        public int? BankAccountID { get; set; }
        public string? BankAccountName { get; set; }
        public decimal Amount { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public decimal AmountLocal { get; set; }
        public int PaymentMethod { get; set; } = 1;
        public string? ChequeNumber { get; set; }
        public DateTime? ChequeDate { get; set; }
        public string? ChequeBankName { get; set; }
        public string? TransferRef { get; set; }
        public string? Description { get; set; }
        public int? ExpenseAccountID { get; set; }
        public string? ExpenseAccountNameAr { get; set; }
        public int? CostCenterID { get; set; }
        public int PaymentStatus { get; set; } = 1;
        public string StatusName => PaymentStatus switch { 1 => "مسودة", 2 => "معتمد", 3 => "ملغي", _ => "غير محدد" };
        public int? JournalID { get; set; }
        public bool IsPosted { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    // ==========================================
    // تحويل بين الخزائن
    // ==========================================
    public class CashTransferListDto
    {
        public int TransferID { get; set; }
        public string? TransferNo { get; set; }
        public DateTime TransferDate { get; set; }
        public int FromCashBoxID { get; set; }
        public string? FromCashBoxNameAr { get; set; }
        public int ToCashBoxID { get; set; }
        public string? ToCashBoxNameAr { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public int Status { get; set; }
        public string? StatusName { get; set; }
        public bool IsPosted { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class CashTransferDto
    {
        public int TransferID { get; set; }
        public string? TransferNo { get; set; }
        public DateTime TransferDate { get; set; } = DateTime.Today;
        public int FromCashBoxID { get; set; }
        public int ToCashBoxID { get; set; }
        public decimal Amount { get; set; }
        public int? CurrencyID { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public decimal AmountLocal { get; set; }
        public string? Description { get; set; }
        public int Status { get; set; } = 1;
        public int? JournalID { get; set; }
        public bool IsPosted { get; set; }
        public int? CreatedBy { get; set; }
    }

    // ==========================================
    // شجرة الحسابات - على الجدول الحقيقي ChartOfAccounts
    // ==========================================
    public class ChartOfAccountDto
    {
        public int AccountID { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountNameAr { get; set; }
        public string? AccountNameEn { get; set; }
        public int? ParentAccountID { get; set; }
        public string? ParentAccountNameAr { get; set; }
        public int AccountLevel { get; set; }
        public int AccountType { get; set; }
        public string? AccountTypeName { get; set; }
        public int AccountNature { get; set; }
        public bool IsDetailAccount { get; set; }
        public bool IsGroup => !IsDetailAccount;
        public int? CurrencyID { get; set; }
        public bool CostCenterRequired { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public string? LinkedEntityType { get; set; }
        public int? LinkedEntityID { get; set; }
        public bool IsSystemAccount { get; set; }
        public bool IsCashFlowAccount { get; set; }
        public int SortOrder { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int ChildrenCount { get; set; }
        public List<ChartOfAccountDto> Children { get; set; } = new();
    }

    // ==========================================
    // القيود - على الجداول الحقيقية JournalEntries + JournalEntryDetails
    // ==========================================
    public class JournalEntryListDto
    {
        public int JournalID { get; set; }
        public string? JournalNumber { get; set; }
        public DateTime JournalDate { get; set; }
        public string? Description { get; set; }
        public string? Reference { get; set; }
        public string? SourceDocType { get; set; }
        public int? SourceDocID { get; set; }
        public string? SourceDocNumber { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public int JournalStatus { get; set; }
        public string? StatusName { get; set; }
        public bool IsAutoGenerated { get; set; }
        public int JournalType { get; set; } = 1;
        public DateTime CreatedDate { get; set; }
        public string? CreatedByName { get; set; }
        public int DetailsCount { get; set; }
    }

    public class JournalEntryDto
    {
        public int JournalID { get; set; }
        public string? JournalNumber { get; set; }
        public DateTime JournalDate { get; set; } = DateTime.Today;
        public string? Description { get; set; }
        public string? Reference { get; set; }
        public string? SourceDocType { get; set; }
        public string? SourceDocID { get; set; }
        public string? SourceDocNumber { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public int? FiscalYearID { get; set; }
        public string? FiscalYearName { get; set; }
        public int? PeriodID { get; set; }
        public string? PeriodName { get; set; }
        public int JournalStatus { get; set; } = 1;
        public string? StatusName { get; set; }
        public int JournalType { get; set; } = 1; // 1=يومية عامة 2=افتتاحي 3=تسوية 4=إقفال - من الجدول الفعلي JournalType
        public bool IsAutoGenerated { get; set; }
        public List<JournalDetailDto> Details { get; set; } = new();
        public int? CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    public class JournalDetailDto
    {
        public int JournalDetailID { get; set; }
        public int JournalID { get; set; }
        public int AccountID { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountNameAr { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public string? Description { get; set; }
        public int LineNumber { get; set; }
        public int? CostCenterID { get; set; }
        public string? CostCenterNameAr { get; set; }
    }

    // ==========================================
    // كشف حساب
    // ==========================================
    public class CashBoxStatementDto
    {
        public int CashBoxID { get; set; }
        public string? CashBoxCode { get; set; }
        public string? CashBoxNameAr { get; set; }
        public DateTime TransactionDate { get; set; }
        public string? TransactionNo { get; set; }
        public string? TransactionType { get; set; }
        public string? TransactionTypeAr { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal Balance { get; set; }
        public string? Description { get; set; }
        public int Status { get; set; }
        public string? EntityName { get; set; }
    }

    public class TreasuryDashboardDto
    {
        public decimal TotalCashBalance { get; set; }
        public decimal TodayReceipts { get; set; }
        public decimal TodayPayments { get; set; }
        public decimal TodayNet { get; set; }
        public int CashBoxesCount { get; set; }
        public int PendingVouchers { get; set; }
        public List<CashBoxListDto> CashBoxes { get; set; } = new();
        public List<ReceiptVoucherListDto> RecentReceipts { get; set; } = new();
        public List<PaymentVoucherListDto> RecentPayments { get; set; } = new();
    }

    // البنوك - على الجدول الحقيقي BankAccounts 15 عمود
    public class BankAccountListDto
    {
        public int BankAccountID { get; set; }
        public string? BankAccountCode { get; set; }
        public string? BankName { get; set; }
        public string? BranchName { get; set; }
        public string? AccountNumber { get; set; }
        public string? IBAN { get; set; }
        public string? SwiftCode { get; set; }
        public int? AccountType { get; set; }
        public string? AccountTypeName { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public int? AccountID { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountNameAr { get; set; }
        public decimal CurrentBalance { get; set; }
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public decimal TotalReceipts { get; set; }
        public decimal TotalPayments { get; set; }
    }

    public class BankAccountDto
    {
        public int BankAccountID { get; set; }
        public string? BankAccountCode { get; set; }
        public string? BankName { get; set; }
        public string? BranchName { get; set; }
        public string? AccountNumber { get; set; }
        public string? IBAN { get; set; }
        public string? SwiftCode { get; set; }
        public int? AccountType { get; set; } = 1; // 1=جاري 2=توفير 3=اعتماد 4=أخرى
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public int? AccountID { get; set; }
        public string? AccountNameAr { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal OpeningBalance { get; set; }
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string? Notes { get; set; }
    }

    // الشيكات - على الجدول الحقيقي Cheques 25 عمود
    public class ChequeListDto
    {
        public int ChequeID { get; set; }
        public string? ChequeNumber { get; set; }
        public int ChequeType { get; set; } // 1=وارد 2=صادر
        public string? ChequeTypeName { get; set; }
        public DateTime ChequeDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public string? BankName { get; set; }
        public string? BranchName { get; set; }
        public int? BankAccountID { get; set; }
        public string? BankAccountName { get; set; }
        public int? EntityType { get; set; }
        public int? EntityID { get; set; }
        public string? EntityName { get; set; }
        public int ChequeStatus { get; set; }
        public string? StatusName { get; set; }
        public DateTime? CollectionDate { get; set; }
        public DateTime? BounceDate { get; set; }
        public string? BounceReason { get; set; }
        public int? ReceiptID { get; set; }
        public int? PaymentID { get; set; }
        public string? ReceiptNumber { get; set; }
        public string? PaymentNumber { get; set; }
        public int? JournalID { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class ChequeDto
    {
        public int ChequeID { get; set; }
        public string? ChequeNumber { get; set; }
        public int ChequeType { get; set; } = 1;
        public DateTime ChequeDate { get; set; } = DateTime.Today;
        public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);
        public decimal Amount { get; set; }
        public int? CurrencyID { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal ExchangeRate { get; set; } = 1;
        public string? BankName { get; set; }
        public string? BranchName { get; set; }
        public int? BankAccountID { get; set; }
        public int? EntityType { get; set; } = 1;
        public int? EntityID { get; set; }
        public string? EntityName { get; set; }
        public int ChequeStatus { get; set; } = 1; // 1=بالحافظة 2=مقدم للبنك 3=محصل 4=مرتجع 5=ملغي
        public DateTime? CollectionDate { get; set; }
        public DateTime? BounceDate { get; set; }
        public string? BounceReason { get; set; }
        public int? ReceiptID { get; set; }
        public int? PaymentID { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    // مراكز التكلفة - على الجدول الحقيقي CostCenters 9 أعمدة
    public class CostCenterListDto
    {
        public int CostCenterID { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterNameAr { get; set; }
        public string? CostCenterNameEn { get; set; }
        public int? ParentCostCenterID { get; set; }
        public string? ParentNameAr { get; set; }
        public int? CenterLevel { get; set; }
        public int? DepartmentID { get; set; }
        public string? DepartmentNameAr { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public int ChildrenCount { get; set; }
        public List<CostCenterListDto> Children { get; set; } = new();
    }

    public class CostCenterDto
    {
        public int CostCenterID { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterNameAr { get; set; }
        public string? CostCenterNameEn { get; set; }
        public int? ParentCostCenterID { get; set; }
        public int? CenterLevel { get; set; } = 1;
        public int? DepartmentID { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string? Description { get; set; }
    }

    // السنوات المالية - على الجدول الحقيقي FiscalYears 11 عمود
    public class FiscalYearListDto
    {
        public int FiscalYearID { get; set; }
        public string? YearCode { get; set; }
        public string? YearName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int YearStatus { get; set; }
        public string? StatusName { get; set; }
        public bool IsCurrent { get; set; }
        public int? ClosedBy { get; set; }
        public DateTime? ClosedDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public int PeriodsCount { get; set; }
        public int JournalsCount { get; set; }
    }

    public class FiscalYearDto
    {
        public int FiscalYearID { get; set; }
        public string? YearCode { get; set; }
        public string? YearName { get; set; }
        public DateTime StartDate { get; set; } = new DateTime(DateTime.Now.Year, 1, 1);
        public DateTime EndDate { get; set; } = new DateTime(DateTime.Now.Year, 12, 31);
        public int YearStatus { get; set; } = 1; // 1=مفتوحة 2=مغلقة
        public bool IsCurrent { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    // الفترات المحاسبية - على الجدول الحقيقي AccountingPeriods 9 أعمدة
    public class AccountingPeriodListDto
    {
        public int PeriodID { get; set; }
        public int FiscalYearID { get; set; }
        public string? FiscalYearName { get; set; }
        public int PeriodNumber { get; set; }
        public string? PeriodName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int PeriodStatus { get; set; }
        public string? StatusName { get; set; }
        public int? ClosedBy { get; set; }
        public DateTime? ClosedDate { get; set; }
    }

    public class AccountingPeriodDto
    {
        public int PeriodID { get; set; }
        public int FiscalYearID { get; set; }
        public int PeriodNumber { get; set; }
        public string? PeriodName { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today.AddMonths(1).AddDays(-1);
        public int PeriodStatus { get; set; } = 1;
    }

    // ==========================================
    // المصروفات - OverheadExpenses Gold Edition Pro
    // ==========================================
    public class ExpenseListDto
    {
        public int ExpenseID { get; set; }
        public string? ExpenseNumber { get; set; }
        public DateTime ExpenseDate { get; set; }
        public int PeriodID { get; set; }
        public string? PeriodName { get; set; }
        public int PeriodNumber { get; set; }
        public int? FiscalYearID { get; set; }
        public string? FiscalYearName { get; set; }
        public int AccountID { get; set; }
        public string? ExpenseAccountCode { get; set; }
        public string? ExpenseAccountNameAr { get; set; }
        public int? CreditAccountID { get; set; }
        public string? CreditAccountCode { get; set; }
        public string? CreditAccountNameAr { get; set; }
        public int? CostCenterID { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterNameAr { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
        public int ExpenseStatus { get; set; } = 1;
        public string? StatusName { get; set; }
        public bool IsPosted { get; set; }
        public int? JournalID { get; set; }
        public string? JournalNumber { get; set; }
        public int? PaymentMethod { get; set; }
        public int? CashBoxID { get; set; }
        public string? CashBoxNameAr { get; set; }
        public int? BankAccountID { get; set; }
        public string? BankName { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedDate { get; set; }
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedDate { get; set; }
    }

    public class ExpenseDto
    {
        public int ExpenseID { get; set; }
        public string? ExpenseNumber { get; set; }
        public DateTime ExpenseDate { get; set; } = DateTime.Today;
        public int PeriodID { get; set; }
        public int? FiscalYearID { get; set; }
        public int AccountID { get; set; }
        public int? CreditAccountID { get; set; }
        public int? CostCenterID { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
        public string? ReferenceType { get; set; }
        public int ExpenseStatus { get; set; } = 1;
        public bool IsPosted { get; set; }
        public int? JournalID { get; set; }
        public int? PaymentMethod { get; set; } = 1; // 1=نقدي 2=بنك 3=آجل
        public int? CashBoxID { get; set; }
        public int? BankAccountID { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
