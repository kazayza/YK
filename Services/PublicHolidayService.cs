using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class PublicHolidayService : BaseDbService
    {
        private readonly AuditService _audit;

        public PublicHolidayService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        public async Task<List<PublicHolidayListDto>> GetHolidaysListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT HolidayID, HolidayDate, HolidayNameAr, HolidayNameEn,
                               IsRecurring, Notes
                        FROM dbo.PublicHolidays ORDER BY HolidayDate DESC";
            var result = await connection.QueryAsync<PublicHolidayListDto>(sql);
            return result.ToList();
        }

        public async Task<PublicHolidayEditDto?> GetHolidayByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<PublicHolidayEditDto>(
                @"SELECT HolidayID, HolidayDate, HolidayNameAr, HolidayNameEn,
                         FiscalYearID, IsRecurring, Notes
                  FROM dbo.PublicHolidays WHERE HolidayID = @ID", new { ID = id });
        }

        public async Task<int> InsertHolidayAsync(PublicHolidayEditDto item, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(item.HolidayNameAr))
                throw new Exception("اسم العطلة مطلوب");

            var sql = @"
                INSERT INTO dbo.PublicHolidays (HolidayDate, HolidayNameAr, HolidayNameEn,
                    FiscalYearID, IsRecurring, Notes)
                VALUES (@HolidayDate, @HolidayNameAr, @HolidayNameEn,
                    @FiscalYearID, @IsRecurring, @Notes);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, item);

            await _audit.WriteAuditLogAsync(userId, 1, "PublicHolidays", newId.ToString(),
                moduleName: "SCR_ATTEND", description: $"إضافة عطلة: {item.HolidayNameAr}");

            return newId;
        }

        public async Task UpdateHolidayAsync(PublicHolidayEditDto item, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(@"
                UPDATE dbo.PublicHolidays SET
                    HolidayDate = @HolidayDate, HolidayNameAr = @HolidayNameAr,
                    HolidayNameEn = @HolidayNameEn, FiscalYearID = @FiscalYearID,
                    IsRecurring = @IsRecurring, Notes = @Notes
                WHERE HolidayID = @HolidayID", item);

            await _audit.WriteAuditLogAsync(userId, 2, "PublicHolidays", item.HolidayID.ToString(),
                moduleName: "SCR_ATTEND", description: $"تعديل عطلة: {item.HolidayNameAr}");
        }

        public async Task DeleteHolidayAsync(int id, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync("DELETE FROM dbo.PublicHolidays WHERE HolidayID = @ID", new { ID = id });

            await _audit.WriteAuditLogAsync(userId, 3, "PublicHolidays", id.ToString(),
                moduleName: "SCR_ATTEND", description: $"حذف عطلة #{id}");
        }
    }
}