using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class PaymentTermService : BaseDbService
    {
        private readonly AuditService _audit;

        public PaymentTermService(IConfiguration configuration, AuditService audit)
            : base(configuration) { _audit = audit; }

        public async Task<List<PaymentTermListDto>> GetAllAsync(bool includeInactive = false)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT pt.PaymentTermID, pt.TermCode, pt.TermNameAr, pt.TermNameEn,
                               pt.DueDays, pt.DiscountPercent, pt.DiscountDays, pt.IsActive,
                               (SELECT COUNT(*) FROM dbo.Suppliers WHERE PaymentTermID = pt.PaymentTermID AND IsActive = 1)
                               + (SELECT COUNT(*) FROM dbo.Customers WHERE PaymentTermID = pt.PaymentTermID AND IsActive = 1) AS UsedCount
                        FROM dbo.PaymentTerms pt
                        WHERE (@All = 1 OR pt.IsActive = 1)
                        ORDER BY pt.DueDays";
            return (await connection.QueryAsync<PaymentTermListDto>(sql, new { All = includeInactive })).ToList();
        }

        public async Task<PaymentTermEditDto?> GetByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<PaymentTermEditDto>(
                @"SELECT PaymentTermID, TermCode, TermNameAr, TermNameEn,
                         DueDays, DiscountPercent, DiscountDays, IsActive
                  FROM dbo.PaymentTerms WHERE PaymentTermID = @ID", new { ID = id });
        }

        public async Task<int> InsertAsync(PaymentTermEditDto dto, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.PaymentTerms
                        (TermCode, TermNameAr, TermNameEn, DueDays, DiscountPercent, DiscountDays, IsActive,CreatedBy,  CreatedDate)
                        VALUES (@TermCode, @TermNameAr, @TermNameEn, @DueDays, @DiscountPercent, @DiscountDays, 1,@UserID,  GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.TermCode, dto.TermNameAr, dto.TermNameEn,
                dto.DueDays, dto.DiscountPercent, dto.DiscountDays, UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "PaymentTerms", newId.ToString(),
                moduleName: "SCR_PAYTERMS",
                description: $"إضافة شرط دفع: {dto.TermNameAr} ({dto.TermCode})");

            return newId;
        }

        public async Task UpdateAsync(PaymentTermEditDto dto, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(@"
                UPDATE dbo.PaymentTerms SET
                    TermCode = @TermCode, TermNameAr = @TermNameAr, TermNameEn = @TermNameEn,
                    DueDays = @DueDays, DiscountPercent = @DiscountPercent, DiscountDays = @DiscountDays,
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                WHERE PaymentTermID = @PaymentTermID",
                new
                {
                    dto.PaymentTermID, dto.TermCode, dto.TermNameAr, dto.TermNameEn,
                    dto.DueDays, dto.DiscountPercent, dto.DiscountDays, UserID = userId
                });

            await _audit.WriteAuditLogAsync(userId, 2, "PaymentTerms", dto.PaymentTermID.ToString(),
                moduleName: "SCR_PAYTERMS",
                description: $"تعديل شرط دفع: {dto.TermNameAr}");
        }

        public async Task<(bool Success, string Message)> DeleteAsync(int id, int userId)
{
    using var connection = CreateConnection();

    int usedCount = 0;
    try
    {
        usedCount = await connection.QueryFirstOrDefaultAsync<int>(@"
            SELECT ISNULL((SELECT COUNT(*) FROM dbo.Suppliers WHERE PaymentTermID = @ID AND IsActive = 1), 0)
                 + ISNULL((SELECT COUNT(*) FROM dbo.Customers WHERE PaymentTermID = @ID AND IsActive = 1), 0)
                 + ISNULL((SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE PaymentTermID = @ID AND POStatus NOT IN (9)), 0)",
            new { ID = id });
    }
    catch { usedCount = 0; }

    if (usedCount > 0) return (false, $"لا يمكن الحذف — مستخدم في {usedCount} مكان");

    var name = await connection.QueryFirstOrDefaultAsync<string>(
        "SELECT TermNameAr FROM dbo.PaymentTerms WHERE PaymentTermID = @ID", new { ID = id });

    await connection.ExecuteAsync(
        "UPDATE dbo.PaymentTerms SET IsActive = 0, ModifiedBy = @UserID, ModifiedDate = GETDATE() WHERE PaymentTermID = @ID",
        new { ID = id, UserID = userId });

    await _audit.WriteAuditLogAsync(userId, 3, "PaymentTerms", id.ToString(),
        moduleName: "SCR_PAYTERMS",
        description: $"حذف شرط دفع: {name}");

    return (true, "تم الحذف بنجاح");
}
    }
}