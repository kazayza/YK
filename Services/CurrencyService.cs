using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class CurrencyService : BaseDbService
    {
        private readonly AuditService _audit;

        public CurrencyService(IConfiguration configuration, AuditService audit)
            : base(configuration) { _audit = audit; }

        public async Task<List<CurrencyListDto>> GetAllAsync(bool includeInactive = false)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT CurrencyID, CurrencyCode, CurrencyNameAr, CurrencyNameEn,
                               Symbol, ExchangeRate, IsDefault, IsActive
                        FROM dbo.Currencies
                        WHERE (@All = 1 OR IsActive = 1)
                        ORDER BY IsDefault DESC, CurrencyNameAr";
            return (await connection.QueryAsync<CurrencyListDto>(sql, new { All = includeInactive })).ToList();
        }

        public async Task<CurrencyEditDto?> GetByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<CurrencyEditDto>(
                @"SELECT CurrencyID, CurrencyCode, CurrencyNameAr, CurrencyNameEn,
                         Symbol, ExchangeRate, IsDefault, IsActive
                  FROM dbo.Currencies WHERE CurrencyID = @ID", new { ID = id });
        }

        public async Task<int> InsertAsync(CurrencyEditDto dto, int userId)
        {
            using var connection = CreateConnection();

            // لو هي الافتراضية، شيل الافتراضية من الباقي
            if (dto.IsDefault)
                await connection.ExecuteAsync("UPDATE dbo.Currencies SET IsDefault = 0 WHERE IsDefault = 1");

            var sql = @"INSERT INTO dbo.Currencies
                        (CurrencyCode, CurrencyNameAr, CurrencyNameEn, Symbol, ExchangeRate, IsDefault, IsActive,CreatedBy,  CreatedDate)
                        VALUES (@CurrencyCode, @CurrencyNameAr, @CurrencyNameEn, @Symbol, @ExchangeRate, @IsDefault, 1,@UserID,  GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                dto.CurrencyCode, dto.CurrencyNameAr, dto.CurrencyNameEn,
                dto.Symbol, dto.ExchangeRate, dto.IsDefault, UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "Currencies", newId.ToString(),
                moduleName: "SCR_CURRENCIES",
                description: $"إضافة عملة: {dto.CurrencyNameAr} ({dto.CurrencyCode})");

            return newId;
        }

        public async Task UpdateAsync(CurrencyEditDto dto, int userId)
        {
            using var connection = CreateConnection();

            if (dto.IsDefault)
                await connection.ExecuteAsync("UPDATE dbo.Currencies SET IsDefault = 0 WHERE IsDefault = 1");

            await connection.ExecuteAsync(@"
                UPDATE dbo.Currencies SET
                    CurrencyCode = @CurrencyCode, CurrencyNameAr = @CurrencyNameAr,
                    CurrencyNameEn = @CurrencyNameEn, Symbol = @Symbol,
                    ExchangeRate = @ExchangeRate, IsDefault = @IsDefault,
                    ModifiedBy = @UserID, ModifiedDate = GETDATE()
                WHERE CurrencyID = @CurrencyID",
                new
                {
                    dto.CurrencyID, dto.CurrencyCode, dto.CurrencyNameAr,
                    dto.CurrencyNameEn, dto.Symbol, dto.ExchangeRate,
                    dto.IsDefault, UserID = userId
                });

            await _audit.WriteAuditLogAsync(userId, 2, "Currencies", dto.CurrencyID.ToString(),
                moduleName: "SCR_CURRENCIES",
                description: $"تعديل عملة: {dto.CurrencyNameAr}");
        }

        public async Task<(bool Success, string Message)> DeleteAsync(int id, int userId)
{
    using var connection = CreateConnection();

    var isDefault = await connection.QueryFirstOrDefaultAsync<bool>(
        "SELECT ISNULL(IsDefault, 0) FROM dbo.Currencies WHERE CurrencyID = @ID", new { ID = id });
    if (isDefault) return (false, "لا يمكن حذف العملة الافتراضية");

    // عدّ الاستخدام بأمان
    int usedCount = 0;
    try
    {
        usedCount = await connection.QueryFirstOrDefaultAsync<int>(@"
            SELECT ISNULL((SELECT COUNT(*) FROM dbo.Suppliers WHERE CurrencyID = @ID AND IsActive = 1), 0)
                 + ISNULL((SELECT COUNT(*) FROM dbo.Customers WHERE CurrencyID = @ID AND IsActive = 1), 0)
                 + ISNULL((SELECT COUNT(*) FROM dbo.PriceLists WHERE CurrencyID = @ID AND IsActive = 1), 0)",
            new { ID = id });
    }
    catch { usedCount = 0; }

    if (usedCount > 0) return (false, $"لا يمكن الحذف — مستخدمة في {usedCount} مكان");

    var name = await connection.QueryFirstOrDefaultAsync<string>(
        "SELECT CurrencyNameAr FROM dbo.Currencies WHERE CurrencyID = @ID", new { ID = id });

    await connection.ExecuteAsync(
        "UPDATE dbo.Currencies SET IsActive = 0, ModifiedBy = @UserID, ModifiedDate = GETDATE() WHERE CurrencyID = @ID",
        new { ID = id, UserID = userId });

    await _audit.WriteAuditLogAsync(userId, 3, "Currencies", id.ToString(),
        moduleName: "SCR_CURRENCIES",
        description: $"حذف عملة: {name}");

    return (true, "تم الحذف بنجاح");
}
    }
}