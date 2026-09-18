using Dapper;
using ClosedXML.Excel;

namespace YKCoatings.Services
{
    public class PriceListService : BaseDbService
    {
        private readonly AuditService _audit;

        public PriceListService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        public async Task<List<PriceListHeaderDto>> GetPriceListsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            pl.PriceListID, pl.PriceListCode, pl.PriceListNameAr, pl.PriceListNameEn,
                            pl.PriceListType, c.CurrencyNameAr AS CurrencyName,
                            ISNULL(pl.DiscountPercent, 0) AS DiscountPercent,
                            pl.IsDefault, pl.IsActive,
                            (SELECT COUNT(*) FROM dbo.PriceListDetails d WHERE d.PriceListID = pl.PriceListID AND d.IsActive = 1) AS ItemCount
                        FROM dbo.PriceLists pl
                        LEFT JOIN dbo.Currencies c ON pl.CurrencyID = c.CurrencyID
                        WHERE pl.IsActive = 1
                        ORDER BY pl.PriceListType, pl.PriceListNameAr";
            var result = await connection.QueryAsync<PriceListHeaderDto>(sql);
            return result.ToList();
        }

        public async Task<PriceListEditDto?> GetPriceListByIdAsync(int priceListId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT PriceListID, PriceListCode, PriceListNameAr, PriceListNameEn,
                               PriceListType, CurrencyID, ISNULL(DiscountPercent, 0) AS DiscountPercent,
                               IsDefault, IsActive, Notes
                        FROM dbo.PriceLists WHERE PriceListID = @PriceListID";
            return await connection.QueryFirstOrDefaultAsync<PriceListEditDto>(sql, new { PriceListID = priceListId });
        }

        public async Task<int> InsertPriceListAsync(PriceListEditDto pl, int userId)
        {
            using var connection = CreateConnection();

            if (pl.IsDefault)
                await connection.ExecuteAsync("UPDATE dbo.PriceLists SET IsDefault = 0 WHERE IsDefault = 1");

            var sql = @"INSERT INTO dbo.PriceLists 
                        (PriceListCode, PriceListNameAr, PriceListNameEn, PriceListType,
                         CurrencyID, DiscountPercent, IsDefault, IsActive, Notes, CreatedBy, CreatedDate)
                        VALUES
                        (@PriceListCode, @PriceListNameAr, @PriceListNameEn, @PriceListType,
                         NULLIF(@CurrencyID, 0), @DiscountPercent, @IsDefault, 1, @Notes, @UserID, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                pl.PriceListCode, pl.PriceListNameAr, pl.PriceListNameEn, pl.PriceListType,
                pl.CurrencyID, pl.DiscountPercent, pl.IsDefault, pl.Notes, UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "PriceLists", newId.ToString(),
                moduleName: "SCR_PRICELISTS",
                description: $"إضافة قائمة أسعار: {pl.PriceListNameAr} ({pl.PriceListCode})");

            return newId;
        }

        public async Task UpdatePriceListAsync(PriceListEditDto pl, int userId)
        {
            using var connection = CreateConnection();

            if (pl.IsDefault)
                await connection.ExecuteAsync(
                    "UPDATE dbo.PriceLists SET IsDefault = 0 WHERE IsDefault = 1 AND PriceListID != @ID",
                    new { ID = pl.PriceListID });

            var sql = @"UPDATE dbo.PriceLists SET
                            PriceListCode=@PriceListCode, PriceListNameAr=@PriceListNameAr,
                            PriceListNameEn=@PriceListNameEn, PriceListType=@PriceListType,
                            CurrencyID=NULLIF(@CurrencyID, 0), DiscountPercent=@DiscountPercent,
                            IsDefault=@IsDefault, Notes=@Notes,
                            ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE PriceListID=@PriceListID";

            await connection.ExecuteAsync(sql, new
            {
                pl.PriceListID, pl.PriceListCode, pl.PriceListNameAr, pl.PriceListNameEn,
                pl.PriceListType, pl.CurrencyID, pl.DiscountPercent, pl.IsDefault, pl.Notes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 2, "PriceLists", pl.PriceListID.ToString(),
                moduleName: "SCR_PRICELISTS",
                description: $"تعديل قائمة أسعار: {pl.PriceListNameAr}");
        }

        public async Task<(bool Success, string Message)> DeletePriceListAsync(int priceListId, int userId)
        {
            using var connection = CreateConnection();

            var custCount = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM dbo.Customers WHERE PriceListID=@ID AND IsActive=1",
                new { ID = priceListId });
            if (custCount > 0)
                return (false, $"لا يمكن حذف القائمة لوجود {custCount} عميل مرتبط بها");

            var isDefault = await connection.QueryFirstOrDefaultAsync<bool>(
                "SELECT IsDefault FROM dbo.PriceLists WHERE PriceListID=@ID",
                new { ID = priceListId });
            if (isDefault)
                return (false, "لا يمكن حذف القائمة الافتراضية");

            var plName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT PriceListNameAr FROM dbo.PriceLists WHERE PriceListID=@ID",
                new { ID = priceListId });

            await connection.ExecuteAsync(
                "UPDATE dbo.PriceLists SET IsActive=0, ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE PriceListID=@ID",
                new { ID = priceListId, UserID = userId });

            await _audit.WriteAuditLogAsync(userId, 3, "PriceLists", priceListId.ToString(),
                moduleName: "SCR_PRICELISTS", description: $"حذف قائمة أسعار: {plName}");

            return (true, "تم حذف القائمة بنجاح");
        }

        // ==========================================
        // تفاصيل القائمة (الأصناف والأسعار)
        // ==========================================
        public async Task<List<PriceListDetailDto>> GetPriceListDetailsAsync(int priceListId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            d.PriceDetailID, d.PriceListID, d.ItemID,
                            i.ItemCode, i.ItemNameAr, d.UnitID, u.UnitNameAr AS UnitName,
                            d.UnitPrice, ISNULL(d.MinQty, 1) AS MinQty,
                            ISNULL(d.DiscountPercent, 0) AS DiscountPercent,
                            ISNULL(d.MinSellingPrice, 0) AS MinSellingPrice,
                            d.IsActive
                        FROM dbo.PriceListDetails d
                        INNER JOIN dbo.Items i ON d.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON d.UnitID = u.UnitID
                        WHERE d.PriceListID = @PriceListID AND d.IsActive = 1
                        ORDER BY i.ItemNameAr";
            var result = await connection.QueryAsync<PriceListDetailDto>(sql, new { PriceListID = priceListId });
            return result.ToList();
        }

        public async Task<int> InsertPriceDetailAsync(PriceListDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.PriceListDetails 
                        (PriceListID, ItemID, UnitID, UnitPrice, MinQty, DiscountPercent, MinSellingPrice, IsActive, CreatedDate)
                        VALUES (@PriceListID, @ItemID, @UnitID, @UnitPrice, @MinQty, @DiscountPercent, @MinSellingPrice, 1, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, detail);

            await _audit.WriteAuditLogAsync(userId, 1, "PriceListDetails", newId.ToString(),
                moduleName: "SCR_PRICELISTS",
                description: $"إضافة سعر صنف في قائمة {detail.PriceListID}");

            return newId;
        }

        public async Task UpdatePriceDetailAsync(PriceListDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.PriceListDetails SET
                            UnitPrice=@UnitPrice, MinQty=@MinQty,
                            DiscountPercent=@DiscountPercent, MinSellingPrice=@MinSellingPrice
                        WHERE PriceDetailID=@PriceDetailID";

            await connection.ExecuteAsync(sql, detail);

            await _audit.WriteAuditLogAsync(userId, 2, "PriceListDetails", detail.PriceDetailID.ToString(),
                moduleName: "SCR_PRICELISTS",
                description: $"تعديل سعر صنف في قائمة {detail.PriceListID}");
        }

        public async Task DeletePriceDetailAsync(int priceDetailId, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE dbo.PriceListDetails SET IsActive=0 WHERE PriceDetailID=@ID",
                new { ID = priceDetailId });

            await _audit.WriteAuditLogAsync(userId, 3, "PriceListDetails", priceDetailId.ToString(),
                moduleName: "SCR_PRICELISTS", description: $"حذف سعر صنف رقم: {priceDetailId}");
        }

        // ==========================================
        // جلب الأصناف التامة للقائمة المنسدلة
        // ==========================================
        public async Task<List<LookupDto>> GetFinishedItemsAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ItemID AS Id, ItemNameAr + ' (' + ItemCode + ')' AS Name
                        FROM dbo.Items WHERE IsActive = 1 AND ItemType = 4
                        ORDER BY ItemNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        public static string GetPriceListTypeName(int type)
        {
            return type switch
            {
                1 => "جملة",
                2 => "نص جملة",
                3 => "تجزئة",
                4 => "خاص",
                _ => "غير محدد"
            };
        }
    }
}