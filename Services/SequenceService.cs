using Dapper;

namespace YKCoatings.Services
{
    public class SequenceService : BaseDbService
    {
        public SequenceService(IConfiguration configuration) : base(configuration) { }

        /// <summary>
        /// يولد الكود التالي باستخدام sp_GetNextNumber
        /// مثال: HR-EMP000024
        /// </summary>
        public async Task<string> GetNextCodeAsync(string sequenceCode)
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                           EXEC sp_GetNextNumber @SeqCode, @NextNum OUTPUT;
                           SELECT @NextNum AS NewCode;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql, new { SeqCode = sequenceCode });
                return result ?? $"{sequenceCode}-{DateTime.Now:yyMMddHHmmss}";
            }
            catch
            {
                return $"{sequenceCode}-{DateTime.Now:yyMMddHHmmss}";
            }
        }

        /// <summary>
        /// يجيب الكود التالي المتوقع بدون ما يزود الرقم
        /// </summary>
        public async Task<string> PeekNextCodeAsync(string sequenceCode)
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"SELECT Prefix + RIGHT(REPLICATE('0', NumberLength) + CAST(CurrentNumber + 1 AS NVARCHAR(20)), NumberLength)
                            FROM dbo.NumberSequences WHERE SequenceCode = @Code AND IsActive = -1";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql, new { Code = sequenceCode });
                return result ?? $"{sequenceCode}-???";
            }
            catch
            {
                return $"{sequenceCode}-???";
            }
        }
    }
}