using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class TreasuryService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public TreasuryService(IConfiguration configuration, AuditService audit, NotificationService notif) : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        // ==========================================
        // لوحة تحكم الخزينة - على الجداول الحقيقية
        // ==========================================
        public async Task<TreasuryDashboardDto> GetDashboardAsync()
        {
            using var connection = CreateConnection();
            var dto = new TreasuryDashboardDto();
            try
            {
                // استعلام مباشر شامل بدل vw_TreasurySummary المعطوب
                var sqlBoxesDirect = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, cb.CashBoxNameEn, 
                                    ISNULL(cb.CashBoxType,1) AS CashBoxType,
                                    CASE ISNULL(cb.CashBoxType,1) WHEN 1 THEN N'رئيسية' WHEN 2 THEN N'فرعية' WHEN 3 THEN N'بنك' WHEN 4 THEN N'نقطة بيع' ELSE N'أخرى' END AS CashBoxTypeName,
                                    cb.AccountID, coa.AccountCode, coa.AccountNameAr, 
                                    ISNULL(cb.OpeningBalance,0) AS OpeningBalance, 
                                    cb.CurrentBalance, 
                                    cb.CurrentBalance AS CurrentBalanceCalc,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE CashBoxID=cb.CashBoxID AND ReceiptStatus=2),0) AS TotalReceipts,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE CashBoxID=cb.CashBoxID AND PaymentStatus=2),0) 
                                    + ISNULL((SELECT SUM(Amount) FROM dbo.OverheadExpenses WHERE CashBoxID=cb.CashBoxID AND ISNULL(ExpenseStatus,1)!=3),0) AS TotalPayments,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE ToCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalIn,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE FromCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalOut,
                                    cb.CurrencyID, cur.CurrencyCode, 
                                    CAST(1 AS BIT) AS IsActive, 
                                    ISNULL(cb.IsDefault,0) AS IsDefault, cb.CreatedDate
                             FROM dbo.CashBoxes cb
                             LEFT JOIN dbo.ChartOfAccounts coa ON cb.AccountID=coa.AccountID
                             LEFT JOIN dbo.Currencies cur ON cb.CurrencyID=cur.CurrencyID
                             ORDER BY cb.CashBoxNameAr";
                try
                {
                    var boxes = await connection.QueryAsync<CashBoxListDto>(sqlBoxesDirect);
                    dto.CashBoxes = boxes.ToList();
                }
                catch
                {
                    // Fallback للـ View مع تصحيح
                    try
                    {
                        var viewBoxes = await connection.QueryAsync<CashBoxListDto>("SELECT * FROM dbo.vw_TreasurySummary ORDER BY CashBoxNameAr");
                        var vList = viewBoxes.ToList();
                        foreach (var cb in vList)
                        {
                            if (cb.TotalReceipts==0 && cb.TotalPayments==0) cb.CurrentBalanceCalc = cb.CurrentBalance;
                        }
                        dto.CashBoxes = vList;
                    }
                    catch
                    {
                        dto.CashBoxes = new List<CashBoxListDto>();
                    }
                }

                dto.CashBoxesCount = dto.CashBoxes.Count;
                dto.TotalCashBalance = dto.CashBoxes.Sum(b => b.CurrentBalance);

                var today = DateTime.Today;
                try
                {
                    dto.TodayReceipts = await connection.ExecuteScalarAsync<decimal>(
                        "SELECT ISNULL(SUM(Amount),0) FROM dbo.ReceiptVouchers WHERE ReceiptDate=@D AND ReceiptStatus=2", new { D = today });
                    dto.TodayPayments = await connection.ExecuteScalarAsync<decimal>(
                        "SELECT ISNULL(SUM(Amount),0) FROM dbo.PaymentVouchers WHERE PaymentDate=@D AND PaymentStatus=2", new { D = today });
                }
                catch { }
                dto.TodayNet = dto.TodayReceipts - dto.TodayPayments;

                try
                {
                    dto.PendingVouchers = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM (SELECT ReceiptStatus AS S FROM dbo.ReceiptVouchers WHERE ReceiptStatus=1 UNION ALL SELECT PaymentStatus FROM dbo.PaymentVouchers WHERE PaymentStatus=1) t");
                }
                catch { }

                // آخر سندات قبض - على الجدول الحقيقي
                try
                {
                    var recentsR = await connection.QueryAsync<ReceiptVoucherListDto>(
                        @"SELECT TOP 5 rv.ReceiptID, rv.ReceiptNumber, rv.ReceiptDate, rv.ReceivedFrom, rv.EntityType, rv.EntityID,
                                 CASE WHEN rv.EntityType=1 THEN c.CustomerNameAr WHEN rv.EntityType=2 THEN s.SupplierNameAr ELSE rv.ReceivedFrom END AS EntityName,
                                 c.CustomerNameAr, c.CustomerCode, s.SupplierNameAr,
                                 rv.CashBoxID, cb.CashBoxNameAr, cb.CashBoxCode, rv.BankAccountID, ba.BankName AS BankAccountName,
                                 rv.Amount, ISNULL(rv.AmountLocal, rv.Amount) AS AmountLocal, rv.CurrencyID, cur.CurrencyCode,
                                 rv.PaymentMethod, CASE rv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' ELSE N'أخرى' END AS PaymentMethodName,
                                 rv.Description, rv.ReceiptStatus, CASE rv.ReceiptStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' ELSE N'ملغي' END AS StatusName,
                                 ISNULL(rv.IsPosted,0) AS IsPosted, rv.JournalID, je.JournalNumber, rv.CreatedDate, emp.FullNameAr AS CreatedByName
                          FROM dbo.ReceiptVouchers rv
                          LEFT JOIN dbo.CashBoxes cb ON rv.CashBoxID=cb.CashBoxID
                          LEFT JOIN dbo.Customers c ON rv.EntityType=1 AND rv.EntityID=c.CustomerID
                          LEFT JOIN dbo.Suppliers s ON rv.EntityType=2 AND rv.EntityID=s.SupplierID
                          LEFT JOIN dbo.BankAccounts ba ON rv.BankAccountID=ba.BankAccountID
                          LEFT JOIN dbo.Currencies cur ON rv.CurrencyID=cur.CurrencyID
                          LEFT JOIN dbo.JournalEntries je ON rv.JournalID=je.JournalID
                          LEFT JOIN dbo.SystemUsers su ON rv.CreatedBy=su.UserID
                          LEFT JOIN dbo.Employees emp ON su.EmployeeID=emp.EmployeeID
                          ORDER BY rv.ReceiptDate DESC, rv.ReceiptID DESC");
                    dto.RecentReceipts = recentsR.ToList();
                }
                catch { }

                try
                {
                    var recentsP = await connection.QueryAsync<PaymentVoucherListDto>(
                        @"SELECT TOP 5 pv.PaymentID, pv.PaymentNumber, pv.PaymentDate, pv.PaidTo, pv.EntityType, pv.EntityID,
                                 CASE WHEN pv.EntityType=1 THEN s.SupplierNameAr WHEN pv.EntityType=2 THEN c.CustomerNameAr ELSE pv.PaidTo END AS EntityName,
                                 s.SupplierNameAr, c.CustomerNameAr,
                                 pv.CashBoxID, cb.CashBoxNameAr, pv.BankAccountID, pv.Amount, ISNULL(pv.AmountLocal, pv.Amount) AS AmountLocal,
                                 pv.CurrencyID, cur.CurrencyCode, pv.PaymentMethod,
                                 CASE pv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' ELSE N'أخرى' END AS PaymentMethodName,
                                 pv.Description, pv.PaymentStatus, CASE pv.PaymentStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' ELSE N'ملغي' END AS StatusName,
                                 ISNULL(pv.IsPosted,0) AS IsPosted, pv.JournalID, pv.CreatedDate
                          FROM dbo.PaymentVouchers pv
                          LEFT JOIN dbo.CashBoxes cb ON pv.CashBoxID=cb.CashBoxID
                          LEFT JOIN dbo.Suppliers s ON pv.EntityType=1 AND pv.EntityID=s.SupplierID
                          LEFT JOIN dbo.Customers c ON pv.EntityType=2 AND pv.EntityID=c.CustomerID
                          LEFT JOIN dbo.Currencies cur ON pv.CurrencyID=cur.CurrencyID
                          ORDER BY pv.PaymentDate DESC, pv.PaymentID DESC");
                    dto.RecentPayments = recentsP.ToList();
                }
                catch { }
            }
            catch { }
            return dto;
        }

        // ==========================================
        // الخزائن - على الجدول الحقيقي CashBoxes
        // ==========================================
        public async Task<List<CashBoxListDto>> GetCashBoxesAsync()
        {
            using var connection = CreateConnection();
            try
            {
                // استعلام مباشر شامل يحسب الأرصدة من الجدول الفعلي CashBoxes.CurrentBalance (المحدث من المصروفات)
                // ويحسب المقبوضات/المدفوعات شاملة المصروفات
                var sqlDirect = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, cb.CashBoxNameEn, 
                                    ISNULL(cb.CashBoxType,1) AS CashBoxType,
                                    CASE ISNULL(cb.CashBoxType,1) WHEN 1 THEN N'رئيسية' WHEN 2 THEN N'فرعية' WHEN 3 THEN N'بنك' WHEN 4 THEN N'نقطة بيع' ELSE N'أخرى' END AS CashBoxTypeName,
                                    cb.AccountID, coa.AccountCode, coa.AccountNameAr, 
                                    ISNULL(cb.OpeningBalance,0) AS OpeningBalance, 
                                    cb.CurrentBalance, 
                                    cb.CurrentBalance AS CurrentBalanceCalc,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE CashBoxID=cb.CashBoxID AND ReceiptStatus=2),0) AS TotalReceipts,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE CashBoxID=cb.CashBoxID AND PaymentStatus=2),0) 
                                    + ISNULL((SELECT SUM(Amount) FROM dbo.OverheadExpenses WHERE CashBoxID=cb.CashBoxID AND ISNULL(ExpenseStatus,1)!=3),0) AS TotalPayments,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE ToCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalIn,
                                    ISNULL((SELECT SUM(Amount) FROM dbo.CashTransfers WHERE FromCashBoxID=cb.CashBoxID AND Status=2),0) AS TotalOut,
                                    cb.CurrencyID, cur.CurrencyCode, CAST(1 AS BIT) AS IsActive, ISNULL(cb.IsDefault,0) AS IsDefault, cb.CreatedDate
                             FROM dbo.CashBoxes cb
                             LEFT JOIN dbo.ChartOfAccounts coa ON cb.AccountID=coa.AccountID
                             LEFT JOIN dbo.Currencies cur ON cb.CurrencyID=cur.CurrencyID
                             ORDER BY cb.CashBoxNameAr";
                var result = await connection.QueryAsync<CashBoxListDto>(sqlDirect);
                var list = result.ToList();
                if (list.Any()) return list;
            }
            catch { }

            // Fallback: vw_TreasurySummary مع تصحيح CurrentBalanceCalc لو خطأ
            try
            {
                var sqlView = "SELECT * FROM dbo.vw_TreasurySummary ORDER BY CashBoxNameAr";
                var viewResult = await connection.QueryAsync<CashBoxListDto>(sqlView);
                var viewList = viewResult.ToList();
                // تصحيح: لو CurrentBalanceCalc = Opening و CurrentBalance مختلف -> استخدم CurrentBalance
                foreach (var cb in viewList)
                {
                    if (cb.TotalReceipts==0 && cb.TotalPayments==0 && cb.TotalIn==0 && cb.TotalOut==0)
                    {
                        // View لا يحسب الحركات، استخدم CurrentBalance الحقيقي
                        cb.CurrentBalanceCalc = cb.CurrentBalance;
                    }
                }
                return viewList;
            }
            catch
            {
                return new List<CashBoxListDto>();
            }
        }

        public async Task<CashBoxDto?> GetCashBoxByIdAsync(int id)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, cb.CashBoxNameEn, ISNULL(cb.CashBoxType,1) AS CashBoxType,
                               cb.AccountID, coa.AccountNameAr, ISNULL(cb.OpeningBalance,0) AS OpeningBalance, cb.CurrentBalance,
                               cb.CurrencyID, cur.CurrencyCode, cb.CustodianEmployeeID, emp.FullNameAr AS CustodianName,
                               cb.IsActive, ISNULL(cb.IsDefault,0) AS IsDefault, cb.MaxLimit, cb.MinLimit, cb.Notes, cb.CreatedDate
                        FROM dbo.CashBoxes cb
                        LEFT JOIN dbo.ChartOfAccounts coa ON cb.AccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON cb.CurrencyID=cur.CurrencyID
                        LEFT JOIN dbo.Employees emp ON cb.CustodianEmployeeID=emp.EmployeeID
                        WHERE cb.CashBoxID=@ID";
            return await connection.QueryFirstOrDefaultAsync<CashBoxDto>(sql, new { ID = id });
        }

        public async Task<string> GenerateCashBoxCodeAsync()
        {
            using var connection = CreateConnection();
            var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*)+1 FROM dbo.CashBoxes");
            return $"CB-{count:000}";
        }

        public async Task<int> InsertCashBoxAsync(CashBoxDto dto, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(dto.CashBoxCode))
                dto.CashBoxCode = await GenerateCashBoxCodeAsync();

            // التأكد من أن الرصيد الافتتاحي له قيمة افتراضية 0 لو فارغ
            var opening = dto.OpeningBalance;

            var sql = @"INSERT INTO dbo.CashBoxes(CashBoxCode, CashBoxNameAr, CashBoxNameEn, AccountID, CurrencyID, CurrentBalance, OpeningBalance, CashBoxType, IsDefault, MaxLimit, MinLimit, Notes, CustodianEmployeeID, IsActive, CreatedDate)
                        VALUES(@CashBoxCode, @CashBoxNameAr, @CashBoxNameEn, NULLIF(@AccountID,0), NULLIF(@CurrencyID,0), @CurrentBalance, @OpeningBalance, @CashBoxType, @IsDefault, @MaxLimit, @MinLimit, @Notes, NULLIF(@CustodianEmployeeID,0), @IsActive, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.CashBoxCode,
                dto.CashBoxNameAr,
                dto.CashBoxNameEn,
                dto.AccountID,
                dto.CurrencyID,
                CurrentBalance = opening,
                OpeningBalance = opening,
                dto.CashBoxType,
                dto.IsDefault,
                dto.MaxLimit,
                dto.MinLimit,
                dto.Notes,
                dto.CustodianEmployeeID,
                dto.IsActive
            });

            try { await _audit.WriteAuditLogAsync(userId, 1, "CashBoxes", newId.ToString(), moduleName: "SCR_BANKS", description: $"إنشاء خزينة: {dto.CashBoxNameAr} برصيد افتتاحي {opening}"); } catch { }
            return newId;
        }

        public async Task UpdateCashBoxAsync(CashBoxDto dto, int userId)
        {
            using var connection = CreateConnection();
            // كان الخطأ هنا: لا يتم تحديث OpeningBalance نهائياً عند التعديل!
            // تم الإصلاح: إضافة OpeningBalance + تحديث CurrentBalance إذا لم تكن هناك حركات
            var hasTrans = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM (SELECT CashBoxID FROM dbo.ReceiptVouchers WHERE CashBoxID=@ID UNION ALL SELECT CashBoxID FROM dbo.PaymentVouchers WHERE CashBoxID=@ID) t",
                new { ID = dto.CashBoxID });

            var sql = hasTrans == 0
                ? @"UPDATE dbo.CashBoxes SET CashBoxNameAr=@CashBoxNameAr, CashBoxNameEn=@CashBoxNameEn, CashBoxType=@CashBoxType, AccountID=NULLIF(@AccountID,0),
                        CurrencyID=NULLIF(@CurrencyID,0), OpeningBalance=@OpeningBalance, CurrentBalance=@OpeningBalance,
                        IsActive=@IsActive, IsDefault=@IsDefault, MaxLimit=@MaxLimit, MinLimit=@MinLimit, Notes=@Notes,
                        CustodianEmployeeID=NULLIF(@CustodianEmployeeID,0), ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE CashBoxID=@CashBoxID"
                : @"UPDATE dbo.CashBoxes SET CashBoxNameAr=@CashBoxNameAr, CashBoxNameEn=@CashBoxNameEn, CashBoxType=@CashBoxType, AccountID=NULLIF(@AccountID,0),
                        CurrencyID=NULLIF(@CurrencyID,0), OpeningBalance=@OpeningBalance,
                        IsActive=@IsActive, IsDefault=@IsDefault, MaxLimit=@MaxLimit, MinLimit=@MinLimit, Notes=@Notes,
                        CustodianEmployeeID=NULLIF(@CustodianEmployeeID,0), ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE CashBoxID=@CashBoxID";

            await connection.ExecuteAsync(sql, new
            {
                dto.CashBoxID,
                dto.CashBoxNameAr,
                dto.CashBoxNameEn,
                dto.CashBoxType,
                dto.AccountID,
                dto.CurrencyID,
                dto.OpeningBalance,
                dto.IsActive,
                dto.IsDefault,
                dto.MaxLimit,
                dto.MinLimit,
                dto.Notes,
                dto.CustodianEmployeeID,
                UserID = userId
            });
            try { await _audit.WriteAuditLogAsync(userId, 2, "CashBoxes", dto.CashBoxID.ToString(), moduleName: "SCR_BANKS", description: $"تعديل خزينة: {dto.CashBoxNameAr} - رصيد افتتاحي {dto.OpeningBalance}"); } catch { }
        }

        public async Task DeleteCashBoxAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var hasTrans = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM (SELECT CashBoxID FROM dbo.ReceiptVouchers WHERE CashBoxID=@ID UNION ALL SELECT CashBoxID FROM dbo.PaymentVouchers WHERE CashBoxID=@ID) t",
                new { ID = id });
            if (hasTrans > 0) throw new Exception("لا يمكن حذف خزينة بها حركات");

            await connection.ExecuteAsync("DELETE FROM dbo.CashBoxes WHERE CashBoxID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "CashBoxes", id.ToString(), moduleName: "SCR_BANKS", description: "حذف خزينة");
        }

        // ==========================================
        // سندات القبض - على الجدول الحقيقي ReceiptVouchers
        // ==========================================
        public async Task<List<ReceiptVoucherListDto>> GetReceiptVouchersAsync(DateTime? from = null, DateTime? to = null, int? cashBoxId = null)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT rv.ReceiptID, rv.ReceiptNumber, rv.ReceiptDate, rv.ReceivedFrom, rv.EntityType, rv.EntityID,
                               CASE WHEN rv.EntityType=1 THEN c.CustomerNameAr WHEN rv.EntityType=2 THEN s.SupplierNameAr ELSE rv.ReceivedFrom END AS EntityName,
                               c.CustomerNameAr, c.CustomerCode, s.SupplierNameAr,
                               rv.CashBoxID, cb.CashBoxNameAr, cb.CashBoxCode, rv.BankAccountID, ba.BankName AS BankAccountName,
                               rv.Amount, ISNULL(rv.AmountLocal, rv.Amount) AS AmountLocal, rv.CurrencyID, cur.CurrencyCode,
                               rv.PaymentMethod, CASE rv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' WHEN 4 THEN N'فيزا' ELSE N'أخرى' END AS PaymentMethodName,
                               rv.Description, rv.ReceiptStatus, CASE rv.ReceiptStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                               ISNULL(rv.IsPosted,0) AS IsPosted, rv.JournalID, je.JournalNumber, rv.CreatedDate, emp.FullNameAr AS CreatedByName
                        FROM dbo.ReceiptVouchers rv
                        LEFT JOIN dbo.CashBoxes cb ON rv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.Customers c ON rv.EntityType=1 AND rv.EntityID=c.CustomerID
                        LEFT JOIN dbo.Suppliers s ON rv.EntityType=2 AND rv.EntityID=s.SupplierID
                        LEFT JOIN dbo.BankAccounts ba ON rv.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.Currencies cur ON rv.CurrencyID=cur.CurrencyID
                        LEFT JOIN dbo.JournalEntries je ON rv.JournalID=je.JournalID
                        LEFT JOIN dbo.SystemUsers su ON rv.CreatedBy=su.UserID
                        LEFT JOIN dbo.Employees emp ON su.EmployeeID=emp.EmployeeID
                        WHERE 1=1
                        AND (@From IS NULL OR rv.ReceiptDate >= @From)
                        AND (@To IS NULL OR rv.ReceiptDate <= @To)
                        AND (@CashBoxID IS NULL OR rv.CashBoxID=@CashBoxID)
                        ORDER BY rv.ReceiptDate DESC, rv.ReceiptID DESC";

            var result = await connection.QueryAsync<ReceiptVoucherListDto>(sql, new { From = from, To = to, CashBoxID = cashBoxId });
            return result.ToList();
        }

        public async Task<ReceiptVoucherDto?> GetReceiptByIdAsync(int id)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT rv.ReceiptID, rv.ReceiptNumber, rv.ReceiptDate, rv.ReceivedFrom, rv.EntityType, rv.EntityID,
                               CASE WHEN rv.EntityType=1 THEN c.CustomerNameAr WHEN rv.EntityType=2 THEN s.SupplierNameAr ELSE rv.ReceivedFrom END AS EntityName,
                               rv.CashBoxID, cb.CashBoxNameAr, rv.BankAccountID, ba.BankName AS BankAccountName,
                               rv.Amount, rv.CurrencyID, cur.CurrencyCode, rv.ExchangeRate, ISNULL(rv.AmountLocal, rv.Amount) AS AmountLocal,
                               rv.PaymentMethod, rv.ChequeNumber, rv.ChequeDate, rv.ChequeBankName, rv.TransferRef,
                               rv.Description, rv.RevenueAccountID, coa.AccountNameAr AS RevenueAccountNameAr, rv.CostCenterID,
                               rv.ReceiptStatus, rv.JournalID, ISNULL(rv.IsPosted,0) AS IsPosted, rv.Notes, rv.CreatedBy, rv.CreatedDate
                        FROM dbo.ReceiptVouchers rv
                        LEFT JOIN dbo.CashBoxes cb ON rv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.Customers c ON rv.EntityType=1 AND rv.EntityID=c.CustomerID
                        LEFT JOIN dbo.Suppliers s ON rv.EntityType=2 AND rv.EntityID=s.SupplierID
                        LEFT JOIN dbo.BankAccounts ba ON rv.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.ChartOfAccounts coa ON rv.RevenueAccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON rv.CurrencyID=cur.CurrencyID
                        WHERE rv.ReceiptID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ReceiptVoucherDto>(sql, new { ID = id });
        }

        public async Task<string> GenerateReceiptNoAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'RV', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"RV-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"RV-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertReceiptAsync(ReceiptVoucherDto dto, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(dto.ReceiptNumber))
                dto.ReceiptNumber = await GenerateReceiptNoAsync();

            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);
            if (string.IsNullOrWhiteSpace(dto.ReceivedFrom))
            {
                if (dto.EntityType == 1 && dto.EntityID.HasValue)
                    dto.ReceivedFrom = await connection.QueryFirstOrDefaultAsync<string>("SELECT CustomerNameAr FROM dbo.Customers WHERE CustomerID=@ID", new { ID = dto.EntityID });
                else if (dto.EntityType == 2 && dto.EntityID.HasValue)
                    dto.ReceivedFrom = await connection.QueryFirstOrDefaultAsync<string>("SELECT SupplierNameAr FROM dbo.Suppliers WHERE SupplierID=@ID", new { ID = dto.EntityID });
            }

            var sql = @"INSERT INTO dbo.ReceiptVouchers(ReceiptNumber, ReceiptDate, ReceivedFrom, EntityType, EntityID, PaymentMethod, CashBoxID, BankAccountID,
                        Amount, CurrencyID, ExchangeRate, AmountLocal, ChequeNumber, ChequeDate, ChequeBankName, TransferRef, Description, RevenueAccountID, CostCenterID, ReceiptStatus, Notes, CreatedBy, CreatedDate)
                        VALUES(@ReceiptNumber, @ReceiptDate, @ReceivedFrom, @EntityType, NULLIF(@EntityID,0), @PaymentMethod, @CashBoxID, NULLIF(@BankAccountID,0),
                        @Amount, NULLIF(@CurrencyID,0), @ExchangeRate, @AmountLocal, @ChequeNumber, @ChequeDate, @ChequeBankName, @TransferRef, @Description, NULLIF(@RevenueAccountID,0), NULLIF(@CostCenterID,0), 1, @Notes, @CreatedBy, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.ReceiptNumber,
                dto.ReceiptDate,
                dto.ReceivedFrom,
                dto.EntityType,
                dto.EntityID,
                dto.PaymentMethod,
                dto.CashBoxID,
                dto.BankAccountID,
                dto.Amount,
                dto.CurrencyID,
                ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                dto.AmountLocal,
                dto.ChequeNumber,
                dto.ChequeDate,
                dto.ChequeBankName,
                dto.TransferRef,
                dto.Description,
                dto.RevenueAccountID,
                dto.CostCenterID,
                dto.Notes,
                CreatedBy = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "ReceiptVouchers", newId.ToString(), moduleName: "SCR_RECV", description: $"إنشاء سند قبض: {dto.ReceiptNumber} مبلغ {dto.Amount}");
            return newId;
        }

        public async Task UpdateReceiptAsync(ReceiptVoucherDto dto, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>("SELECT ReceiptStatus FROM dbo.ReceiptVouchers WHERE ReceiptID=@ID", new { ID = dto.ReceiptID });
            if (status != 1) throw new Exception("لا يمكن تعديل سند معتمد");

            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);

            var sql = @"UPDATE dbo.ReceiptVouchers SET ReceiptDate=@ReceiptDate, ReceivedFrom=@ReceivedFrom, EntityType=@EntityType, EntityID=NULLIF(@EntityID,0),
                        PaymentMethod=@PaymentMethod, CashBoxID=@CashBoxID, BankAccountID=NULLIF(@BankAccountID,0), Amount=@Amount, CurrencyID=NULLIF(@CurrencyID,0),
                        ExchangeRate=@ExchangeRate, AmountLocal=@AmountLocal, ChequeNumber=@ChequeNumber, ChequeDate=@ChequeDate, ChequeBankName=@ChequeBankName,
                        TransferRef=@TransferRef, Description=@Description, RevenueAccountID=NULLIF(@RevenueAccountID,0), CostCenterID=NULLIF(@CostCenterID,0),
                        Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE ReceiptID=@ReceiptID";
            await connection.ExecuteAsync(sql, new
            {
                dto.ReceiptID,
                dto.ReceiptDate,
                dto.ReceivedFrom,
                dto.EntityType,
                dto.EntityID,
                dto.PaymentMethod,
                dto.CashBoxID,
                dto.BankAccountID,
                dto.Amount,
                dto.CurrencyID,
                ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                dto.AmountLocal,
                dto.ChequeNumber,
                dto.ChequeDate,
                dto.ChequeBankName,
                dto.TransferRef,
                dto.Description,
                dto.RevenueAccountID,
                dto.CostCenterID,
                dto.Notes,
                UserID = userId
            });
            await _audit.WriteAuditLogAsync(userId, 2, "ReceiptVouchers", dto.ReceiptID.ToString(), moduleName: "SCR_RECV", description: $"تعديل سند قبض: {dto.ReceiptNumber}");
        }

        public async Task ApproveReceiptAsync(int id, int userId, int? empId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var rv = await connection.QueryFirstOrDefaultAsync<(string ReceiptNumber, int ReceiptStatus, decimal Amount, int CashBoxID)>(
                    "SELECT ReceiptNumber, ReceiptStatus, Amount, CashBoxID FROM dbo.ReceiptVouchers WHERE ReceiptID=@ID", new { ID = id }, trans);
                if (rv.ReceiptNumber == null) throw new Exception("السند غير موجود");
                if (rv.ReceiptStatus != 1) throw new Exception("السند ليس مسودة");

                await connection.ExecuteAsync("EXEC dbo.sp_PostReceiptVoucher @ReceiptID, @UserID", new { ReceiptID = id, UserID = userId }, trans);

                // تحديث أرصدة شجرة الحسابات من القيد المنشأ
                try
                {
                    var jId = await connection.ExecuteScalarAsync<int?>("SELECT JournalID FROM dbo.ReceiptVouchers WHERE ReceiptID=@ID", new { ID = id }, trans);
                    if (jId.HasValue)
                    {
                        var details = await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit)>(
                            "SELECT AccountID, DebitAmount, CreditAmount FROM dbo.JournalEntryDetails WHERE JournalID=@JID", new { JID = jId.Value }, trans);
                        foreach (var d in details)
                        {
                            var delta = d.Debit - d.Credit;
                            if (delta != 0)
                            {
                                await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Delta WHERE AccountID=@AccID",
                                    new { Delta = delta, AccID = d.AccountID }, trans);
                                int? cur = d.AccountID;
                                for (int i = 0; i < 10; i++)
                                {
                                    var pid = await connection.ExecuteScalarAsync<int?>("SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = cur }, trans);
                                    if (!pid.HasValue || pid.Value == 0) break;
                                    await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Delta WHERE AccountID=@PID",
                                        new { Delta = delta, PID = pid.Value }, trans);
                                    cur = pid.Value;
                                }
                            }
                        }
                    }
                }
                catch { /* تجاهل لو فشل تحديث الأرصدة - الشجرة تحسب من القيود */ }

                await connection.ExecuteAsync(
                    "UPDATE dbo.ReceiptVouchers SET ReceiptStatus=2, ApprovedBy=@EmpID, ApprovedDate=GETDATE() WHERE ReceiptID=@ID",
                    new { EmpID = empId, ID = id }, trans);

                trans.Commit();

                await _audit.WriteAuditLogAsync(userId, 2, "ReceiptVouchers", id.ToString(), moduleName: "SCR_RECV", description: $"اعتماد سند قبض: {rv.ReceiptNumber}");
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task DeleteReceiptAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>("SELECT ReceiptStatus FROM dbo.ReceiptVouchers WHERE ReceiptID=@ID", new { ID = id });
            if (status != 1) throw new Exception("لا يمكن حذف سند معتمد");
            await connection.ExecuteAsync("DELETE FROM dbo.ReceiptVouchers WHERE ReceiptID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "ReceiptVouchers", id.ToString(), moduleName: "SCR_RECV", description: "حذف سند قبض");
        }

        // ==========================================
        // سندات الصرف - على الجدول الحقيقي PaymentVouchers
        // ==========================================
        public async Task<List<PaymentVoucherListDto>> GetPaymentVouchersAsync(DateTime? from = null, DateTime? to = null, int? cashBoxId = null)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT pv.PaymentID, pv.PaymentNumber, pv.PaymentDate, pv.PaidTo, pv.EntityType, pv.EntityID,
                               CASE WHEN pv.EntityType=1 THEN s.SupplierNameAr WHEN pv.EntityType=2 THEN c.CustomerNameAr ELSE pv.PaidTo END AS EntityName,
                               s.SupplierNameAr, c.CustomerNameAr,
                               pv.CashBoxID, cb.CashBoxNameAr, pv.BankAccountID, pv.Amount, ISNULL(pv.AmountLocal, pv.Amount) AS AmountLocal,
                               pv.CurrencyID, cur.CurrencyCode, pv.PaymentMethod,
                               CASE pv.PaymentMethod WHEN 1 THEN N'نقدي' WHEN 2 THEN N'تحويل بنكي' WHEN 3 THEN N'شيك' ELSE N'أخرى' END AS PaymentMethodName,
                               pv.Description, pv.PaymentStatus, CASE pv.PaymentStatus WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' WHEN 3 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                               ISNULL(pv.IsPosted,0) AS IsPosted, pv.JournalID, pv.CreatedDate
                        FROM dbo.PaymentVouchers pv
                        LEFT JOIN dbo.CashBoxes cb ON pv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.Suppliers s ON pv.EntityType=1 AND pv.EntityID=s.SupplierID
                        LEFT JOIN dbo.Customers c ON pv.EntityType=2 AND pv.EntityID=c.CustomerID
                        LEFT JOIN dbo.Currencies cur ON pv.CurrencyID=cur.CurrencyID
                        WHERE 1=1 AND (@From IS NULL OR pv.PaymentDate >= @From) AND (@To IS NULL OR pv.PaymentDate <= @To) AND (@CashBoxID IS NULL OR pv.CashBoxID=@CashBoxID)
                        ORDER BY pv.PaymentDate DESC, pv.PaymentID DESC";
            var result = await connection.QueryAsync<PaymentVoucherListDto>(sql, new { From = from, To = to, CashBoxID = cashBoxId });
            return result.ToList();
        }

        public async Task<PaymentVoucherDto?> GetPaymentByIdAsync(int id)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT pv.PaymentID, pv.PaymentNumber, pv.PaymentDate, pv.PaidTo, pv.EntityType, pv.EntityID,
                               CASE WHEN pv.EntityType=1 THEN s.SupplierNameAr WHEN pv.EntityType=2 THEN c.CustomerNameAr ELSE pv.PaidTo END AS EntityName,
                               pv.CashBoxID, cb.CashBoxNameAr, pv.BankAccountID, ba.BankName AS BankAccountName,
                               pv.Amount, pv.CurrencyID, cur.CurrencyCode, pv.ExchangeRate, ISNULL(pv.AmountLocal, pv.Amount) AS AmountLocal,
                               pv.PaymentMethod, pv.ChequeNumber, pv.ChequeDate, pv.ChequeBankName, pv.TransferRef,
                               pv.Description, pv.ExpenseAccountID, coa.AccountNameAr AS ExpenseAccountNameAr, pv.CostCenterID,
                               pv.PaymentStatus, pv.JournalID, ISNULL(pv.IsPosted,0) AS IsPosted, pv.Notes, pv.CreatedBy, pv.CreatedDate
                        FROM dbo.PaymentVouchers pv
                        LEFT JOIN dbo.CashBoxes cb ON pv.CashBoxID=cb.CashBoxID
                        LEFT JOIN dbo.Suppliers s ON pv.EntityType=1 AND pv.EntityID=s.SupplierID
                        LEFT JOIN dbo.Customers c ON pv.EntityType=2 AND pv.EntityID=c.CustomerID
                        LEFT JOIN dbo.BankAccounts ba ON pv.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.ChartOfAccounts coa ON pv.ExpenseAccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON pv.CurrencyID=cur.CurrencyID
                        WHERE pv.PaymentID=@ID";
            return await connection.QueryFirstOrDefaultAsync<PaymentVoucherDto>(sql, new { ID = id });
        }

        public async Task<string> GeneratePaymentNoAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'PV', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"PV-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"PV-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertPaymentAsync(PaymentVoucherDto dto, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(dto.PaymentNumber))
                dto.PaymentNumber = await GeneratePaymentNoAsync();
            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);
            if (string.IsNullOrWhiteSpace(dto.PaidTo))
            {
                if (dto.EntityType == 1 && dto.EntityID.HasValue)
                    dto.PaidTo = await connection.QueryFirstOrDefaultAsync<string>("SELECT SupplierNameAr FROM dbo.Suppliers WHERE SupplierID=@ID", new { ID = dto.EntityID });
                else if (dto.EntityType == 2 && dto.EntityID.HasValue)
                    dto.PaidTo = await connection.QueryFirstOrDefaultAsync<string>("SELECT CustomerNameAr FROM dbo.Customers WHERE CustomerID=@ID", new { ID = dto.EntityID });
            }

            var sql = @"INSERT INTO dbo.PaymentVouchers(PaymentNumber, PaymentDate, PaidTo, EntityType, EntityID, PaymentMethod, CashBoxID, BankAccountID,
                        Amount, CurrencyID, ExchangeRate, AmountLocal, ChequeNumber, ChequeDate, ChequeBankName, TransferRef, Description, ExpenseAccountID, CostCenterID, PaymentStatus, Notes, CreatedBy, CreatedDate)
                        VALUES(@PaymentNumber, @PaymentDate, @PaidTo, @EntityType, NULLIF(@EntityID,0), @PaymentMethod, @CashBoxID, NULLIF(@BankAccountID,0),
                        @Amount, NULLIF(@CurrencyID,0), @ExchangeRate, @AmountLocal, @ChequeNumber, @ChequeDate, @ChequeBankName, @TransferRef, @Description, NULLIF(@ExpenseAccountID,0), NULLIF(@CostCenterID,0), 1, @Notes, @CreatedBy, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.PaymentNumber,
                dto.PaymentDate,
                dto.PaidTo,
                dto.EntityType,
                dto.EntityID,
                dto.PaymentMethod,
                dto.CashBoxID,
                dto.BankAccountID,
                dto.Amount,
                dto.CurrencyID,
                ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                dto.AmountLocal,
                dto.ChequeNumber,
                dto.ChequeDate,
                dto.ChequeBankName,
                dto.TransferRef,
                dto.Description,
                dto.ExpenseAccountID,
                dto.CostCenterID,
                dto.Notes,
                CreatedBy = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "PaymentVouchers", newId.ToString(), moduleName: "SCR_PAYV", description: $"إنشاء سند صرف: {dto.PaymentNumber}");
            return newId;
        }

        public async Task UpdatePaymentAsync(PaymentVoucherDto dto, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>("SELECT PaymentStatus FROM dbo.PaymentVouchers WHERE PaymentID=@ID", new { ID = dto.PaymentID });
            if (status != 1) throw new Exception("لا يمكن تعديل سند معتمد");
            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);

            var sql = @"UPDATE dbo.PaymentVouchers SET PaymentDate=@PaymentDate, PaidTo=@PaidTo, EntityType=@EntityType, EntityID=NULLIF(@EntityID,0),
                        PaymentMethod=@PaymentMethod, CashBoxID=@CashBoxID, BankAccountID=NULLIF(@BankAccountID,0), Amount=@Amount, CurrencyID=NULLIF(@CurrencyID,0),
                        ExchangeRate=@ExchangeRate, AmountLocal=@AmountLocal, ChequeNumber=@ChequeNumber, ChequeDate=@ChequeDate, ChequeBankName=@ChequeBankName,
                        TransferRef=@TransferRef, Description=@Description, ExpenseAccountID=NULLIF(@ExpenseAccountID,0), CostCenterID=NULLIF(@CostCenterID,0),
                        Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE PaymentID=@PaymentID";
            await connection.ExecuteAsync(sql, new
            {
                dto.PaymentID,
                dto.PaymentDate,
                dto.PaidTo,
                dto.EntityType,
                dto.EntityID,
                dto.PaymentMethod,
                dto.CashBoxID,
                dto.BankAccountID,
                dto.Amount,
                dto.CurrencyID,
                ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                dto.AmountLocal,
                dto.ChequeNumber,
                dto.ChequeDate,
                dto.ChequeBankName,
                dto.TransferRef,
                dto.Description,
                dto.ExpenseAccountID,
                dto.CostCenterID,
                dto.Notes,
                UserID = userId
            });
            await _audit.WriteAuditLogAsync(userId, 2, "PaymentVouchers", dto.PaymentID.ToString(), moduleName: "SCR_PAYV", description: $"تعديل سند صرف: {dto.PaymentNumber}");
        }

        public async Task ApprovePaymentAsync(int id, int userId, int? empId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var pv = await connection.QueryFirstOrDefaultAsync<(string PaymentNumber, int PaymentStatus)>(
                    "SELECT PaymentNumber, PaymentStatus FROM dbo.PaymentVouchers WHERE PaymentID=@ID", new { ID = id }, trans);
                if (pv.PaymentNumber == null) throw new Exception("السند غير موجود");
                if (pv.PaymentStatus != 1) throw new Exception("السند ليس مسودة");

                await connection.ExecuteAsync("EXEC dbo.sp_PostPaymentVoucher @PaymentID, @UserID", new { PaymentID = id, UserID = userId }, trans);

                // تحديث أرصدة شجرة الحسابات
                try
                {
                    var jId = await connection.ExecuteScalarAsync<int?>("SELECT JournalID FROM dbo.PaymentVouchers WHERE PaymentID=@ID", new { ID = id }, trans);
                    if (jId.HasValue)
                    {
                        var details = await connection.QueryAsync<(int AccountID, decimal Debit, decimal Credit)>(
                            "SELECT AccountID, DebitAmount, CreditAmount FROM dbo.JournalEntryDetails WHERE JournalID=@JID", new { JID = jId.Value }, trans);
                        foreach (var d in details)
                        {
                            var delta = d.Debit - d.Credit;
                            if (delta != 0)
                            {
                                await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Delta WHERE AccountID=@AccID",
                                    new { Delta = delta, AccID = d.AccountID }, trans);
                                int? cur = d.AccountID;
                                for (int i = 0; i < 10; i++)
                                {
                                    var pid = await connection.ExecuteScalarAsync<int?>("SELECT ParentAccountID FROM dbo.ChartOfAccounts WHERE AccountID=@ID", new { ID = cur }, trans);
                                    if (!pid.HasValue || pid.Value == 0) break;
                                    await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Delta WHERE AccountID=@PID",
                                        new { Delta = delta, PID = pid.Value }, trans);
                                    cur = pid.Value;
                                }
                            }
                        }
                    }
                }
                catch { }

                await connection.ExecuteAsync("UPDATE dbo.PaymentVouchers SET PaymentStatus=2, ApprovedBy=@UserID, ApprovedDate=GETDATE() WHERE PaymentID=@ID",
                    new { UserID = userId, ID = id }, trans);
                trans.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "PaymentVouchers", id.ToString(), moduleName: "SCR_PAYV", description: $"اعتماد سند صرف: {pv.PaymentNumber}");
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task DeletePaymentAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>("SELECT PaymentStatus FROM dbo.PaymentVouchers WHERE PaymentID=@ID", new { ID = id });
            if (status != 1) throw new Exception("لا يمكن حذف سند معتمد");
            await connection.ExecuteAsync("DELETE FROM dbo.PaymentVouchers WHERE PaymentID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "PaymentVouchers", id.ToString(), moduleName: "SCR_PAYV", description: "حذف سند صرف");
        }

        // ==========================================
        // تحويلات
        // ==========================================
        public async Task<List<CashTransferListDto>> GetTransfersAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ct.TransferID, ct.TransferNo, ct.TransferDate, ct.FromCashBoxID, fcb.CashBoxNameAr AS FromCashBoxNameAr,
                        ct.ToCashBoxID, tcb.CashBoxNameAr AS ToCashBoxNameAr, ct.Amount, ct.Description, ct.Status,
                        CASE ct.Status WHEN 1 THEN N'مسودة' WHEN 2 THEN N'معتمد' ELSE N'ملغي' END AS StatusName, ct.IsPosted, ct.CreatedDate
                        FROM dbo.CashTransfers ct
                        LEFT JOIN dbo.CashBoxes fcb ON ct.FromCashBoxID=fcb.CashBoxID
                        LEFT JOIN dbo.CashBoxes tcb ON ct.ToCashBoxID=tcb.CashBoxID
                        ORDER BY ct.TransferDate DESC, ct.TransferID DESC";
            try
            {
                var result = await connection.QueryAsync<CashTransferListDto>(sql);
                return result.ToList();
            }
            catch { return new List<CashTransferListDto>(); }
        }

        public async Task<string> GenerateTransferNoAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'CT', @Next OUTPUT; SELECT @Next;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"CT-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"CT-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertTransferAsync(CashTransferDto dto, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(dto.TransferNo))
                dto.TransferNo = await GenerateTransferNoAsync();
            if (dto.FromCashBoxID == dto.ToCashBoxID) throw new Exception("لا يمكن التحويل لنفس الخزينة");
            dto.AmountLocal = dto.Amount * (dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate);

            var sql = @"INSERT INTO dbo.CashTransfers(TransferNo, TransferDate, FromCashBoxID, ToCashBoxID, Amount, CurrencyID, ExchangeRate, AmountLocal, Description, Status, CreatedBy, CreatedDate)
                        VALUES(@TransferNo, @TransferDate, @FromCashBoxID, @ToCashBoxID, @Amount, NULLIF(@CurrencyID,0), @ExchangeRate, @AmountLocal, @Description, 1, @CreatedBy, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.TransferNo,
                dto.TransferDate,
                dto.FromCashBoxID,
                dto.ToCashBoxID,
                dto.Amount,
                dto.CurrencyID,
                ExchangeRate = dto.ExchangeRate <= 0 ? 1 : dto.ExchangeRate,
                dto.AmountLocal,
                dto.Description,
                CreatedBy = userId
            });
            await _audit.WriteAuditLogAsync(userId, 1, "CashTransfers", newId.ToString(), moduleName: "SCR_BANKS", description: $"تحويل خزينة: {dto.TransferNo}");
            return newId;
        }

        public async Task ApproveTransferAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var trans = connection.BeginTransaction();
            try
            {
                var tr = await connection.QueryFirstOrDefaultAsync<(string TransferNo, int Status, int FromID, int ToID, decimal AmountLocal)>(
                    "SELECT TransferNo, Status, FromCashBoxID, ToCashBoxID, AmountLocal FROM dbo.CashTransfers WHERE TransferID=@ID", new { ID = id }, trans);
                if (tr.TransferNo == null) throw new Exception("التحويل غير موجود");
                if (tr.Status != 1) throw new Exception("التحويل ليس مسودة");

                await connection.ExecuteAsync("UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)-@Amt WHERE CashBoxID=@ID", new { Amt = tr.AmountLocal, ID = tr.FromID }, trans);
                await connection.ExecuteAsync("UPDATE dbo.CashBoxes SET CurrentBalance=ISNULL(CurrentBalance,0)+@Amt WHERE CashBoxID=@ID", new { Amt = tr.AmountLocal, ID = tr.ToID }, trans);

                var fromAcc = await connection.QueryFirstOrDefaultAsync<int?>("SELECT AccountID FROM dbo.CashBoxes WHERE CashBoxID=@ID", new { ID = tr.FromID }, trans);
                var toAcc = await connection.QueryFirstOrDefaultAsync<int?>("SELECT AccountID FROM dbo.CashBoxes WHERE CashBoxID=@ID", new { ID = tr.ToID }, trans);

                if (fromAcc.HasValue && toAcc.HasValue)
                {
                    string jNo;
                    try
                    {
                        var sql = "DECLARE @Next NVARCHAR(50); EXEC sp_GetNextNumber N'JV', @Next OUTPUT; SELECT @Next;";
                        jNo = await connection.QueryFirstOrDefaultAsync<string>(sql, transaction: trans) ?? $"JV-{DateTime.Now:yyMMddHHmmss}";
                    }
                    catch { jNo = $"JV-{DateTime.Now:yyMMddHHmmss}"; }

                    var jId = await connection.ExecuteScalarAsync<int>(
                        "INSERT INTO dbo.JournalEntries(JournalNumber, JournalDate, Description, SourceDocType, SourceDocID, SourceDocNumber, TotalDebit, TotalCredit, JournalStatus, IsAutoGenerated, CreatedBy, CreatedDate) VALUES(@No, @Date, @Desc, 'CashTransfer', @RefID, @RefNo, @Amt, @Amt, 2, 1, @UserID, GETDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new { No = jNo, Date = DateTime.Today, Desc = $"تحويل خزينة {tr.TransferNo}", RefID = id, RefNo = tr.TransferNo, Amt = tr.AmountLocal, UserID = userId }, trans);

                    await connection.ExecuteAsync("INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description) VALUES(@JID, 1, @Acc, @Amt, 0, @Desc)",
                        new { JID = jId, Acc = toAcc.Value, Amt = tr.AmountLocal, Desc = $"تحويل وارد {tr.TransferNo}" }, trans);
                    await connection.ExecuteAsync("INSERT INTO dbo.JournalEntryDetails(JournalID, LineNumber, AccountID, DebitAmount, CreditAmount, Description) VALUES(@JID, 2, @Acc, 0, @Amt, @Desc)",
                        new { JID = jId, Acc = fromAcc.Value, Amt = tr.AmountLocal, Desc = $"تحويل صادر {tr.TransferNo}" }, trans);

                    // تحديث أرصدة الحسابات
                    await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) + @Amt WHERE AccountID=@Acc", new { Amt = tr.AmountLocal, Acc = toAcc.Value }, trans);
                    await connection.ExecuteAsync("UPDATE dbo.ChartOfAccounts SET CurrentBalance = ISNULL(CurrentBalance,0) - @Amt WHERE AccountID=@Acc", new { Amt = tr.AmountLocal, Acc = fromAcc.Value }, trans);

                    await connection.ExecuteAsync("UPDATE dbo.CashTransfers SET JournalID=@JID, IsPosted=1 WHERE TransferID=@ID", new { JID = jId, ID = id }, trans);
                }

                await connection.ExecuteAsync("UPDATE dbo.CashTransfers SET Status=2, ApprovedBy=@UserID, ApprovedDate=GETDATE() WHERE TransferID=@ID", new { UserID = userId, ID = id }, trans);
                trans.Commit();
                await _audit.WriteAuditLogAsync(userId, 2, "CashTransfers", id.ToString(), moduleName: "SCR_BANKS", description: $"اعتماد تحويل: {tr.TransferNo}");
            }
            catch { trans.Rollback(); throw; }
        }

        public async Task DeleteTransferAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>("SELECT Status FROM dbo.CashTransfers WHERE TransferID=@ID", new { ID = id });
            if (status != 1) throw new Exception("لا يمكن حذف تحويل معتمد");
            await connection.ExecuteAsync("DELETE FROM dbo.CashTransfers WHERE TransferID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "CashTransfers", id.ToString(), moduleName: "SCR_BANKS", description: "حذف تحويل خزينة");
        }

        // ==========================================
        // كشف حساب خزينة - Gold Edition شامل المصروفات
        // ==========================================
        public async Task<List<CashBoxStatementDto>> GetCashBoxStatementAsync(int cashBoxId, DateTime? from, DateTime? to)
        {
            using var connection = CreateConnection();
            try
            {
                // محاولة قراءة من View أولاً لو موجود - لكن نضيف المصروفات يدوياً بعدها
                var sqlView = @"SELECT * FROM dbo.vw_CashBoxStatement WHERE CashBoxID=@CBID
                            AND (@From IS NULL OR TransactionDate >= @From)
                            AND (@To IS NULL OR TransactionDate <= @To)
                            ORDER BY TransactionDate, TransactionNo";
                var viewResult = await connection.QueryAsync<CashBoxStatementDto>(sqlView, new { CBID = cashBoxId, From = from, To = to });
                var viewList = viewResult.ToList();

                // جلب المصروفات المرتبطة بالخزينة (OverheadExpenses) - حتى لو View لا يشملها
                List<CashBoxStatementDto> expenseList = new();
                try
                {
                    var sqlExp = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
                                          oe.ExpenseDate AS TransactionDate, oe.ExpenseNumber AS TransactionNo,
                                          'Expense' AS TransactionType, N'مصروف' AS TransactionTypeAr,
                                          0 AS DebitAmount, oe.Amount AS CreditAmount,
                                          oe.Description, ISNULL(oe.ExpenseStatus,1) AS Status, coa.AccountNameAr AS EntityName, 0 AS Balance
                                   FROM dbo.CashBoxes cb
                                   JOIN dbo.OverheadExpenses oe ON cb.CashBoxID=oe.CashBoxID
                                   LEFT JOIN dbo.ChartOfAccounts coa ON oe.AccountID=coa.AccountID
                                   WHERE cb.CashBoxID=@CBID AND ISNULL(oe.ExpenseStatus,1)!=3
                                   AND (@From IS NULL OR oe.ExpenseDate >= @From)
                                   AND (@To IS NULL OR oe.ExpenseDate <= @To)";
                    var expRes = await connection.QueryAsync<CashBoxStatementDto>(sqlExp, new { CBID = cashBoxId, From = from, To = to });
                    expenseList = expRes.ToList();
                }
                catch { }

                // جلب تحويلات الخزينة أيضاً لو View لا يشملها
                List<CashBoxStatementDto> transferList = new();
                try
                {
                    var sqlTr = @"SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
                                         ct.TransferDate AS TransactionDate, ct.TransferNo AS TransactionNo,
                                         'Transfer' AS TransactionType,
                                         CASE WHEN ct.FromCashBoxID=@CBID THEN N'تحويل صادر' ELSE N'تحويل وارد' END AS TransactionTypeAr,
                                         CASE WHEN ct.ToCashBoxID=@CBID THEN ct.Amount ELSE 0 END AS DebitAmount,
                                         CASE WHEN ct.FromCashBoxID=@CBID THEN ct.Amount ELSE 0 END AS CreditAmount,
                                         ct.Description, ct.Status, 
                                         CASE WHEN ct.FromCashBoxID=@CBID THEN tcb.CashBoxNameAr ELSE fcb.CashBoxNameAr END AS EntityName, 0 AS Balance
                                  FROM dbo.CashBoxes cb
                                  JOIN dbo.CashTransfers ct ON (ct.FromCashBoxID=cb.CashBoxID OR ct.ToCashBoxID=cb.CashBoxID)
                                  LEFT JOIN dbo.CashBoxes fcb ON ct.FromCashBoxID=fcb.CashBoxID
                                  LEFT JOIN dbo.CashBoxes tcb ON ct.ToCashBoxID=tcb.CashBoxID
                                  WHERE cb.CashBoxID=@CBID AND ct.Status=2
                                  AND (@From IS NULL OR ct.TransferDate >= @From)
                                  AND (@To IS NULL OR ct.TransferDate <= @To)";
                    var trRes = await connection.QueryAsync<CashBoxStatementDto>(sqlTr, new { CBID = cashBoxId, From = from, To = to });
                    transferList = trRes.ToList();
                }
                catch { }

                // دمج الكل
                var all = viewList.Concat(expenseList).Concat(transferList)
                          .GroupBy(x => new { x.TransactionDate, x.TransactionNo, x.TransactionType }) // منع التكرار
                          .Select(g => g.First())
                          .OrderBy(x => x.TransactionDate).ThenBy(x => x.TransactionNo).ToList();

                // حساب الرصيد التراكمي
                var opening = await connection.ExecuteScalarAsync<decimal>("SELECT ISNULL(OpeningBalance,0) FROM dbo.CashBoxes WHERE CashBoxID=@ID", new { ID = cashBoxId });
                decimal balance = opening;
                foreach (var item in all.OrderBy(x => x.TransactionDate).ThenBy(x => x.TransactionNo))
                {
                    balance += item.DebitAmount - item.CreditAmount;
                    item.Balance = balance;
                }
                return all.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.TransactionNo).ToList();
            }
            catch
            {
                // Fallback شامل بدون View - يشمل كل الحركات
                var sql2 = @"
                    SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr,
                           rv.ReceiptDate AS TransactionDate, rv.ReceiptNumber AS TransactionNo, 'Receipt' AS TransactionType, N'سند قبض' AS TransactionTypeAr,
                           rv.Amount AS DebitAmount, 0 AS CreditAmount, rv.Description, rv.ReceiptStatus AS Status, rv.ReceivedFrom AS EntityName, 0 AS Balance
                    FROM dbo.CashBoxes cb JOIN dbo.ReceiptVouchers rv ON cb.CashBoxID=rv.CashBoxID AND rv.ReceiptStatus=2 WHERE cb.CashBoxID=@CBID AND (@From IS NULL OR rv.ReceiptDate >= @From) AND (@To IS NULL OR rv.ReceiptDate <= @To)
                    UNION ALL
                    SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, pv.PaymentDate, pv.PaymentNumber, 'Payment', N'سند صرف', 0, pv.Amount, pv.Description, pv.PaymentStatus, pv.PaidTo, 0
                    FROM dbo.CashBoxes cb JOIN dbo.PaymentVouchers pv ON cb.CashBoxID=pv.CashBoxID AND pv.PaymentStatus=2 WHERE cb.CashBoxID=@CBID AND (@From IS NULL OR pv.PaymentDate >= @From) AND (@To IS NULL OR pv.PaymentDate <= @To)
                    UNION ALL
                    SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, oe.ExpenseDate, oe.ExpenseNumber, 'Expense', N'مصروف', 0, oe.Amount, oe.Description, ISNULL(oe.ExpenseStatus,1), coa.AccountNameAr, 0
                    FROM dbo.CashBoxes cb JOIN dbo.OverheadExpenses oe ON cb.CashBoxID=oe.CashBoxID LEFT JOIN dbo.ChartOfAccounts coa ON oe.AccountID=coa.AccountID
                    WHERE cb.CashBoxID=@CBID AND ISNULL(oe.ExpenseStatus,1)!=3 AND (@From IS NULL OR oe.ExpenseDate >= @From) AND (@To IS NULL OR oe.ExpenseDate <= @To)
                    UNION ALL
                    SELECT cb.CashBoxID, cb.CashBoxCode, cb.CashBoxNameAr, ct.TransferDate, ct.TransferNo, 'Transfer',
                           CASE WHEN ct.FromCashBoxID=@CBID THEN N'تحويل صادر' ELSE N'تحويل وارد' END,
                           CASE WHEN ct.ToCashBoxID=@CBID THEN ct.Amount ELSE 0 END,
                           CASE WHEN ct.FromCashBoxID=@CBID THEN ct.Amount ELSE 0 END,
                           ct.Description, ct.Status,
                           CASE WHEN ct.FromCashBoxID=@CBID THEN tcb.CashBoxNameAr ELSE fcb.CashBoxNameAr END, 0
                    FROM dbo.CashBoxes cb
                    JOIN dbo.CashTransfers ct ON (ct.FromCashBoxID=cb.CashBoxID OR ct.ToCashBoxID=cb.CashBoxID)
                    LEFT JOIN dbo.CashBoxes fcb ON ct.FromCashBoxID=fcb.CashBoxID
                    LEFT JOIN dbo.CashBoxes tcb ON ct.ToCashBoxID=tcb.CashBoxID
                    WHERE cb.CashBoxID=@CBID AND ct.Status=2 AND (@From IS NULL OR ct.TransferDate >= @From) AND (@To IS NULL OR ct.TransferDate <= @To)
                ";
                try
                {
                    var result = await connection.QueryAsync<CashBoxStatementDto>(sql2, new { CBID = cashBoxId, From = from, To = to });
                    var list = result.OrderBy(x => x.TransactionDate).ThenBy(x => x.TransactionNo).ToList();
                    var opening = await connection.ExecuteScalarAsync<decimal>("SELECT ISNULL(OpeningBalance,0) FROM dbo.CashBoxes WHERE CashBoxID=@ID", new { ID = cashBoxId });
                    decimal bal = opening;
                    foreach (var item in list)
                    {
                        bal += item.DebitAmount - item.CreditAmount;
                        item.Balance = bal;
                    }
                    return list.OrderByDescending(x => x.TransactionDate).ToList();
                }
                catch
                {
                    return new List<CashBoxStatementDto>();
                }
            }
        }

        public async Task<byte[]> ExportReceiptsToExcelAsync(List<ReceiptVoucherListDto> data, int userId)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("سندات القبض");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Tajawal";
            ws.Cell(1, 1).Value = "تقرير سندات القبض — Gold Edition";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 8).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int hr = 3;
            var headers = new[] { "#", "رقم السند", "التاريخ", "الخزينة", "المستلم من", "المبلغ", "طريقة الدفع", "الحالة" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(hr, i + 1);
                c.Value = headers[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#070B14");
                c.Style.Font.FontColor = XLColor.FromHtml("#D4AF37");
            }
            int r = hr + 1;
            int n = 0;
            foreach (var item in data)
            {
                n++;
                ws.Cell(r, 1).Value = n;
                ws.Cell(r, 2).Value = item.ReceiptNumber ?? "";
                ws.Cell(r, 3).Value = item.ReceiptDate.ToString("dd/MM/yyyy");
                ws.Cell(r, 4).Value = item.CashBoxNameAr ?? "";
                ws.Cell(r, 5).Value = item.EntityName ?? item.ReceivedFrom ?? "";
                ws.Cell(r, 6).Value = item.Amount;
                ws.Cell(r, 7).Value = item.PaymentMethodName ?? "";
                ws.Cell(r, 8).Value = item.StatusName ?? "";
                if (n % 2 == 0) ws.Range(r, 1, r, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#fcfaf6");
                r++;
            }
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            await _audit.WriteAuditLogAsync(userId, 5, "ReceiptVouchers", moduleName: "SCR_RECV", description: $"تصدير {data.Count} سند قبض");
            return ms.ToArray();
        }

        // ==========================================
        // البنوك - على الجدول الحقيقي BankAccounts 15 عمود
        // ==========================================
        public async Task<List<BankAccountListDto>> GetBankAccountsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ba.BankAccountID, ba.BankAccountCode, ba.BankName, ba.BranchName, ba.AccountNumber, ba.IBAN, ba.SwiftCode,
                               ba.AccountType, CASE ba.AccountType WHEN 1 THEN N'جاري' WHEN 2 THEN N'توفير' WHEN 3 THEN N'اعتماد' ELSE N'أخرى' END AS AccountTypeName,
                               ba.CurrencyID, cur.CurrencyCode, ba.AccountID, coa.AccountCode, coa.AccountNameAr, ba.CurrentBalance,
                               ba.ContactPerson, ba.Phone, ba.IsActive, ba.CreatedDate,
                               ISNULL((SELECT SUM(Amount) FROM dbo.ReceiptVouchers WHERE BankAccountID=ba.BankAccountID AND ReceiptStatus=2),0) AS TotalReceipts,
                               ISNULL((SELECT SUM(Amount) FROM dbo.PaymentVouchers WHERE BankAccountID=ba.BankAccountID AND PaymentStatus=2),0) AS TotalPayments
                        FROM dbo.BankAccounts ba
                        LEFT JOIN dbo.ChartOfAccounts coa ON ba.AccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON ba.CurrencyID=cur.CurrencyID
                        ORDER BY ba.BankName";
            try
            {
                var result = await connection.QueryAsync<BankAccountListDto>(sql);
                return result.ToList();
            }
            catch
            {
                var fallback = @"SELECT BankAccountID, BankAccountCode, BankName, BranchName, AccountNumber, IBAN, SwiftCode, AccountType, CurrencyID, AccountID, CurrentBalance, ContactPerson, Phone, IsActive, CreatedDate,
                                        0 AS TotalReceipts, 0 AS TotalPayments FROM dbo.BankAccounts ORDER BY BankName";
                var result = await connection.QueryAsync<BankAccountListDto>(fallback);
                return result.ToList();
            }
        }

        public async Task<BankAccountDto?> GetBankAccountByIdAsync(int id)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ba.BankAccountID, ba.BankAccountCode, ba.BankName, ba.BranchName, ba.AccountNumber, ba.IBAN, ba.SwiftCode,
                               ba.AccountType, ba.CurrencyID, cur.CurrencyCode, ba.AccountID, coa.AccountNameAr, ba.CurrentBalance,
                               ba.ContactPerson, ba.Phone, ba.IsActive, ba.CreatedDate
                        FROM dbo.BankAccounts ba
                        LEFT JOIN dbo.ChartOfAccounts coa ON ba.AccountID=coa.AccountID
                        LEFT JOIN dbo.Currencies cur ON ba.CurrencyID=cur.CurrencyID
                        WHERE ba.BankAccountID=@ID";
            return await connection.QueryFirstOrDefaultAsync<BankAccountDto>(sql, new { ID = id });
        }

        public async Task<string> GenerateBankAccountCodeAsync()
        {
            using var connection = CreateConnection();
            var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*)+1 FROM dbo.BankAccounts");
            return $"BANK-{count:000}";
        }

        public async Task<int> InsertBankAccountAsync(BankAccountDto dto, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(dto.BankAccountCode))
                dto.BankAccountCode = await GenerateBankAccountCodeAsync();

            var sql = @"INSERT INTO dbo.BankAccounts(BankAccountCode, BankName, BranchName, AccountNumber, IBAN, SwiftCode, AccountType, CurrencyID, AccountID, CurrentBalance, ContactPerson, Phone, IsActive, CreatedDate)
                        VALUES(@BankAccountCode, @BankName, @BranchName, @AccountNumber, @IBAN, @SwiftCode, @AccountType, NULLIF(@CurrencyID,0), NULLIF(@AccountID,0), @CurrentBalance, @ContactPerson, @Phone, @IsActive, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.BankAccountCode,
                dto.BankName,
                dto.BranchName,
                dto.AccountNumber,
                dto.IBAN,
                dto.SwiftCode,
                dto.AccountType,
                dto.CurrencyID,
                dto.AccountID,
                dto.CurrentBalance,
                dto.ContactPerson,
                dto.Phone,
                dto.IsActive
            });
            await _audit.WriteAuditLogAsync(userId, 1, "BankAccounts", newId.ToString(), moduleName: "SCR_BANKS", description: $"إنشاء حساب بنكي: {dto.BankName}");
            return newId;
        }

        public async Task UpdateBankAccountAsync(BankAccountDto dto, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.BankAccounts SET BankName=@BankName, BranchName=@BranchName, AccountNumber=@AccountNumber, IBAN=@IBAN, SwiftCode=@SwiftCode,
                        AccountType=@AccountType, CurrencyID=NULLIF(@CurrencyID,0), AccountID=NULLIF(@AccountID,0), ContactPerson=@ContactPerson, Phone=@Phone, IsActive=@IsActive
                        WHERE BankAccountID=@BankAccountID";
            await connection.ExecuteAsync(sql, new
            {
                dto.BankAccountID,
                dto.BankName,
                dto.BranchName,
                dto.AccountNumber,
                dto.IBAN,
                dto.SwiftCode,
                dto.AccountType,
                dto.CurrencyID,
                dto.AccountID,
                dto.ContactPerson,
                dto.Phone,
                dto.IsActive
            });
            await _audit.WriteAuditLogAsync(userId, 2, "BankAccounts", dto.BankAccountID.ToString(), moduleName: "SCR_BANKS", description: $"تعديل حساب بنكي: {dto.BankName}");
        }

        public async Task DeleteBankAccountAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var hasTrans = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM (SELECT BankAccountID FROM dbo.ReceiptVouchers WHERE BankAccountID=@ID UNION ALL SELECT BankAccountID FROM dbo.PaymentVouchers WHERE BankAccountID=@ID) t", new { ID = id });
            if (hasTrans > 0) throw new Exception("لا يمكن حذف حساب بنكي به حركات");
            await connection.ExecuteAsync("DELETE FROM dbo.BankAccounts WHERE BankAccountID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "BankAccounts", id.ToString(), moduleName: "SCR_BANKS", description: "حذف حساب بنكي");
        }

        // ==========================================
        // الشيكات - على الجدول الحقيقي Cheques 25 عمود
        // ==========================================
        public async Task<List<ChequeListDto>> GetChequesAsync(DateTime? from = null, DateTime? to = null, int? type = null, int? status = null)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ch.ChequeID, ch.ChequeNumber, ch.ChequeType, CASE ch.ChequeType WHEN 1 THEN N'وارد' WHEN 2 THEN N'صادر' ELSE N'غير محدد' END AS ChequeTypeName,
                               ch.ChequeDate, ch.DueDate, ch.Amount, ch.CurrencyID, cur.CurrencyCode, ch.BankName, ch.BranchName,
                               ch.BankAccountID, ba.BankName AS BankAccountName, ch.EntityType, ch.EntityID,
                               CASE WHEN ch.EntityType=1 THEN c.CustomerNameAr WHEN ch.EntityType=2 THEN s.SupplierNameAr ELSE ch.EntityName END AS EntityName,
                               ch.ChequeStatus, CASE ch.ChequeStatus WHEN 1 THEN N'بالحافظة' WHEN 2 THEN N'مقدم للبنك' WHEN 3 THEN N'محصل' WHEN 4 THEN N'مرتجع' WHEN 5 THEN N'ملغي' ELSE N'غير محدد' END AS StatusName,
                               ch.CollectionDate, ch.BounceDate, ch.BounceReason, ch.ReceiptID, ch.PaymentID, rv.ReceiptNumber, pv.PaymentNumber, ch.JournalID, ch.CreatedDate
                        FROM dbo.Cheques ch
                        LEFT JOIN dbo.BankAccounts ba ON ch.BankAccountID=ba.BankAccountID
                        LEFT JOIN dbo.Currencies cur ON ch.CurrencyID=cur.CurrencyID
                        LEFT JOIN dbo.Customers c ON ch.EntityType=1 AND ch.EntityID=c.CustomerID
                        LEFT JOIN dbo.Suppliers s ON ch.EntityType=2 AND ch.EntityID=s.SupplierID
                        LEFT JOIN dbo.ReceiptVouchers rv ON ch.ReceiptID=rv.ReceiptID
                        LEFT JOIN dbo.PaymentVouchers pv ON ch.PaymentID=pv.PaymentID
                        WHERE 1=1 AND (@From IS NULL OR ch.DueDate >= @From) AND (@To IS NULL OR ch.DueDate <= @To)
                        AND (@Type IS NULL OR ch.ChequeType=@Type) AND (@Status IS NULL OR ch.ChequeStatus=@Status)
                        ORDER BY ch.DueDate DESC, ch.ChequeID DESC";
            try
            {
                var result = await connection.QueryAsync<ChequeListDto>(sql, new { From = from, To = to, Type = type, Status = status });
                return result.ToList();
            }
            catch { return new List<ChequeListDto>(); }
        }

        public async Task<ChequeDto?> GetChequeByIdAsync(int id)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ChequeID, ChequeNumber, ChequeType, ChequeDate, DueDate, Amount, CurrencyID, BankName, BranchName, BankAccountID, EntityType, EntityID, EntityName, ChequeStatus, CollectionDate, BounceDate, BounceReason, ReceiptID, PaymentID, Notes, CreatedBy, CreatedDate
                        FROM dbo.Cheques WHERE ChequeID=@ID";
            return await connection.QueryFirstOrDefaultAsync<ChequeDto>(sql, new { ID = id });
        }

        public async Task<int> InsertChequeAsync(ChequeDto dto, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.Cheques(ChequeNumber, ChequeType, ChequeDate, DueDate, Amount, CurrencyID, BankName, BranchName, BankAccountID, EntityType, EntityID, EntityName, ChequeStatus, ReceiptID, PaymentID, Notes, CreatedBy, CreatedDate)
                        VALUES(@ChequeNumber, @ChequeType, @ChequeDate, @DueDate, @Amount, NULLIF(@CurrencyID,0), @BankName, @BranchName, NULLIF(@BankAccountID,0), @EntityType, NULLIF(@EntityID,0), @EntityName, @ChequeStatus, NULLIF(@ReceiptID,0), NULLIF(@PaymentID,0), @Notes, @CreatedBy, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.ChequeNumber,
                dto.ChequeType,
                dto.ChequeDate,
                dto.DueDate,
                dto.Amount,
                dto.CurrencyID,
                dto.BankName,
                dto.BranchName,
                dto.BankAccountID,
                dto.EntityType,
                dto.EntityID,
                dto.EntityName,
                dto.ChequeStatus,
                dto.ReceiptID,
                dto.PaymentID,
                dto.Notes,
                CreatedBy = userId
            });
            await _audit.WriteAuditLogAsync(userId, 1, "Cheques", newId.ToString(), moduleName: "SCR_CHEQ", description: $"إنشاء شيك: {dto.ChequeNumber}");
            return newId;
        }

        public async Task UpdateChequeAsync(ChequeDto dto, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.Cheques SET ChequeNumber=@ChequeNumber, ChequeType=@ChequeType, ChequeDate=@ChequeDate, DueDate=@DueDate, Amount=@Amount, CurrencyID=NULLIF(@CurrencyID,0),
                        BankName=@BankName, BranchName=@BranchName, BankAccountID=NULLIF(@BankAccountID,0), EntityType=@EntityType, EntityID=NULLIF(@EntityID,0), EntityName=@EntityName,
                        ChequeStatus=@ChequeStatus, CollectionDate=@CollectionDate, BounceDate=@BounceDate, BounceReason=@BounceReason, Notes=@Notes, ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE ChequeID=@ChequeID";
            await connection.ExecuteAsync(sql, new
            {
                dto.ChequeID,
                dto.ChequeNumber,
                dto.ChequeType,
                dto.ChequeDate,
                dto.DueDate,
                dto.Amount,
                dto.CurrencyID,
                dto.BankName,
                dto.BranchName,
                dto.BankAccountID,
                dto.EntityType,
                dto.EntityID,
                dto.EntityName,
                dto.ChequeStatus,
                dto.CollectionDate,
                dto.BounceDate,
                dto.BounceReason,
                dto.Notes,
                UserID = userId
            });
            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", dto.ChequeID.ToString(), moduleName: "SCR_CHEQ", description: $"تعديل شيك: {dto.ChequeNumber}");
        }

        public async Task CollectChequeAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.Cheques SET ChequeStatus=3, CollectionDate=GETDATE() WHERE ChequeID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: "تحصيل شيك");
        }

        public async Task BounceChequeAsync(int id, string reason, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("UPDATE dbo.Cheques SET ChequeStatus=4, BounceDate=GETDATE(), BounceReason=@Reason WHERE ChequeID=@ID", new { ID = id, Reason = reason });
            await _audit.WriteAuditLogAsync(userId, 2, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: $"ارتجاع شيك: {reason}");
        }

        public async Task DeleteChequeAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>("SELECT ChequeStatus FROM dbo.Cheques WHERE ChequeID=@ID", new { ID = id });
            if (status != 1 && status != 5) throw new Exception("لا يمكن حذف شيك مقدم للبنك أو محصل");
            await connection.ExecuteAsync("DELETE FROM dbo.Cheques WHERE ChequeID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "Cheques", id.ToString(), moduleName: "SCR_CHEQ", description: "حذف شيك");
        }

        // ==========================================
        // مراكز التكلفة - على الجدول الحقيقي CostCenters 9 أعمدة
        // ==========================================
        public async Task<List<CostCenterListDto>> GetCostCentersTreeAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT cc.CostCenterID, cc.CostCenterCode, cc.CostCenterNameAr, cc.CostCenterNameEn, cc.ParentCostCenterID, p.CostCenterNameAr AS ParentNameAr,
                               cc.CenterLevel, cc.DepartmentID, d.DepartmentNameAr, cc.IsActive, cc.CreatedDate,
                               (SELECT COUNT(*) FROM dbo.CostCenters c WHERE c.ParentCostCenterID=cc.CostCenterID) AS ChildrenCount
                        FROM dbo.CostCenters cc
                        LEFT JOIN dbo.CostCenters p ON cc.ParentCostCenterID=p.CostCenterID
                        LEFT JOIN dbo.Departments d ON cc.DepartmentID=d.DepartmentID
                        ORDER BY cc.CostCenterCode";
            var all = (await connection.QueryAsync<CostCenterListDto>(sql)).ToList();
            var lookup = all.ToDictionary(x => x.CostCenterID);
            var roots = new List<CostCenterListDto>();
            foreach (var cc in all)
            {
                if (cc.ParentCostCenterID.HasValue && lookup.ContainsKey(cc.ParentCostCenterID.Value))
                    lookup[cc.ParentCostCenterID.Value].Children.Add(cc);
                else
                    roots.Add(cc);
            }
            return roots;
        }

        public async Task<List<CostCenterListDto>> GetCostCentersFlatAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT CostCenterID, CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, IsActive, CreatedDate, 0 AS ChildrenCount
                        FROM dbo.CostCenters WHERE IsActive=1 ORDER BY CostCenterCode";
                var result = await connection.QueryAsync<CostCenterListDto>(sql);
                return result.ToList();
            }
            catch
            {
                try
                {
                    var sql2 = @"SELECT CostCenterID, CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, 1 AS IsActive, CreatedDate, 0 AS ChildrenCount
                        FROM dbo.CostCenters ORDER BY CostCenterCode";
                    var result = await connection.QueryAsync<CostCenterListDto>(sql2);
                    return result.ToList();
                }
                catch { return new List<CostCenterListDto>(); }
            }
        }

        public async Task<CostCenterDto?> GetCostCenterByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<CostCenterDto>("SELECT CostCenterID, CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, IsActive, CreatedDate FROM dbo.CostCenters WHERE CostCenterID=@ID", new { ID = id });
        }

        public async Task<int> InsertCostCenterAsync(CostCenterDto dto, int userId)
        {
            using var connection = CreateConnection();
            var parentLevel = 0;
            if (dto.ParentCostCenterID.HasValue && dto.ParentCostCenterID > 0)
                parentLevel = await connection.ExecuteScalarAsync<int>("SELECT ISNULL(CenterLevel,1) FROM dbo.CostCenters WHERE CostCenterID=@ID", new { ID = dto.ParentCostCenterID });
            dto.CenterLevel = parentLevel + 1;

            var sql = @"INSERT INTO dbo.CostCenters(CostCenterCode, CostCenterNameAr, CostCenterNameEn, ParentCostCenterID, CenterLevel, DepartmentID, IsActive, CreatedDate)
                        VALUES(@CostCenterCode, @CostCenterNameAr, @CostCenterNameEn, NULLIF(@ParentCostCenterID,0), @CenterLevel, NULLIF(@DepartmentID,0), @IsActive, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.CostCenterCode,
                dto.CostCenterNameAr,
                dto.CostCenterNameEn,
                dto.ParentCostCenterID,
                dto.CenterLevel,
                dto.DepartmentID,
                dto.IsActive
            });
            await _audit.WriteAuditLogAsync(userId, 1, "CostCenters", newId.ToString(), moduleName: "SCR_COST", description: $"إنشاء مركز تكلفة: {dto.CostCenterNameAr}");
            return newId;
        }

        public async Task UpdateCostCenterAsync(CostCenterDto dto, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.CostCenters SET CostCenterNameAr=@CostCenterNameAr, CostCenterNameEn=@CostCenterNameEn, DepartmentID=NULLIF(@DepartmentID,0), IsActive=@IsActive WHERE CostCenterID=@CostCenterID";
            await connection.ExecuteAsync(sql, new { dto.CostCenterID, dto.CostCenterNameAr, dto.CostCenterNameEn, dto.DepartmentID, dto.IsActive });
            await _audit.WriteAuditLogAsync(userId, 2, "CostCenters", dto.CostCenterID.ToString(), moduleName: "SCR_COST", description: $"تعديل مركز تكلفة: {dto.CostCenterNameAr}");
        }

        public async Task DeleteCostCenterAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            var hasChildren = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.CostCenters WHERE ParentCostCenterID=@ID", new { ID = id });
            if (hasChildren > 0) throw new Exception("لا يمكن حذف مركز له مراكز فرعية");
            var hasTrans = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.JournalEntryDetails WHERE CostCenterID=@ID", new { ID = id });
            if (hasTrans > 0) throw new Exception("لا يمكن حذف مركز به حركات محاسبية");
            await connection.ExecuteAsync("DELETE FROM dbo.CostCenters WHERE CostCenterID=@ID", new { ID = id });
            await _audit.WriteAuditLogAsync(userId, 3, "CostCenters", id.ToString(), moduleName: "SCR_COST", description: "حذف مركز تكلفة");
        }
    }
}
