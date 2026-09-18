using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class LoginHistoryService : BaseDbService
    {
        public LoginHistoryService(IConfiguration configuration) : base(configuration) { }

        // ══════════════════════════════════════
        // 1) جلب سجل الدخول مع الفلاتر
        // ══════════════════════════════════════
        public async Task<List<LoginHistoryDto>> GetAllAsync(LoginHistoryFilterDto? filter = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT TOP 500
                    lh.LoginID,
                    lh.UserID,
                    lh.Username,
                    u.FullName,
                    r.RoleNameAr,
                    lh.LoginTime,
                    lh.LogoutTime,
                    lh.SessionDuration,
                    lh.LoginStatus,
                    CASE lh.LoginStatus
                        WHEN 1 THEN N'ناجح ✅'
                        WHEN 2 THEN N'فاشل - كلمة مرور ❌'
                        WHEN 3 THEN N'فاشل - محظور 🔒'
                    END AS LoginStatusName,
                    lh.IPAddress,
                    lh.MachineName,
                    lh.FailureReason
                FROM dbo.LoginHistory lh
                LEFT JOIN dbo.SystemUsers u ON lh.UserID = u.UserID
                LEFT JOIN dbo.UserRoles r ON u.RoleID = r.RoleID
                WHERE 1=1";

            var parameters = new DynamicParameters();

            // فلتر التاريخ من
            if (filter?.DateFrom.HasValue == true)
            {
                sql += " AND CAST(lh.LoginTime AS DATE) >= @DateFrom";
                parameters.Add("DateFrom", filter.DateFrom.Value.Date);
            }

            // فلتر التاريخ إلى
            if (filter?.DateTo.HasValue == true)
            {
                sql += " AND CAST(lh.LoginTime AS DATE) <= @DateTo";
                parameters.Add("DateTo", filter.DateTo.Value.Date);
            }

            // فلتر المستخدم
            if (!string.IsNullOrWhiteSpace(filter?.Username))
            {
                sql += " AND (lh.Username LIKE @Username OR u.FullName LIKE @Username)";
                parameters.Add("Username", $"%{filter.Username}%");
            }

            // فلتر الدور
            if (filter?.RoleID.HasValue == true)
            {
                sql += " AND u.RoleID = @RoleID";
                parameters.Add("RoleID", filter.RoleID.Value);
            }

            // فلتر حالة الدخول
            if (filter?.LoginStatus.HasValue == true)
            {
                sql += " AND lh.LoginStatus = @LoginStatus";
                parameters.Add("LoginStatus", filter.LoginStatus.Value);
            }

            // فلتر IP
            if (!string.IsNullOrWhiteSpace(filter?.IPAddress))
            {
                sql += " AND lh.IPAddress LIKE @IPAddress";
                parameters.Add("IPAddress", $"%{filter.IPAddress}%");
            }

            sql += " ORDER BY lh.LoginTime DESC";

            var result = await connection.QueryAsync<LoginHistoryDto>(sql, parameters);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 2) إحصائيات سريعة لسجل الدخول
        // ══════════════════════════════════════
        public async Task<LoginStatsDto> GetStatsAsync(DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            using var connection = CreateConnection();

            var fromDate = dateFrom ?? DateTime.Today;
            var toDate = dateTo ?? DateTime.Today;

            var sql = @"
                SELECT
                    COUNT(*) AS TotalAttempts,
                    SUM(CASE WHEN LoginStatus = 1 THEN 1 ELSE 0 END) AS SuccessCount,
                    SUM(CASE WHEN LoginStatus = 2 THEN 1 ELSE 0 END) AS FailedPasswordCount,
                    SUM(CASE WHEN LoginStatus = 3 THEN 1 ELSE 0 END) AS FailedLockedCount,
                    COUNT(DISTINCT CASE WHEN LoginStatus = 1 THEN UserID END) AS UniqueUsers,
                    COUNT(DISTINCT IPAddress) AS UniqueIPs
                FROM dbo.LoginHistory
                WHERE CAST(LoginTime AS DATE) BETWEEN @DateFrom AND @DateTo";

            var stats = await connection.QueryFirstOrDefaultAsync<LoginStatsDto>(
                sql, new { DateFrom = fromDate.Date, DateTo = toDate.Date });

            return stats ?? new LoginStatsDto();
        }
        // ══════════════════════════════════════
// 3) جلب المستخدمين للـ Dropdown
// ══════════════════════════════════════
public async Task<List<LookupDto>> GetUsersLookupAsync()
{
    using var connection = CreateConnection();

    var sql = @"
        SELECT DISTINCT
            u.UserID AS Id,
            u.FullName + ' (' + u.Username + ')' AS Name,
            u.Username AS Code
        FROM dbo.SystemUsers u
        INNER JOIN dbo.LoginHistory lh ON u.UserID = lh.UserID
        ORDER BY Name";

    var result = await connection.QueryAsync<LookupDto>(sql);
    return result.ToList();
}
// ══════════════════════════════════════
// 4) تسجيل وقت الخروج ومدة الجلسة
// ══════════════════════════════════════
public async Task RecordLogoutAsync(int userId)
{
    using var connection = CreateConnection();

    var sql = @"
    UPDATE dbo.LoginHistory
    SET 
        LogoutTime = GETDATE()
    WHERE LoginID = (
        SELECT TOP 1 LoginID 
        FROM dbo.LoginHistory 
        WHERE UserID = @UserID 
          AND LoginStatus = 1 
          AND LogoutTime IS NULL
        ORDER BY LoginTime DESC
    )";

    await connection.ExecuteAsync(sql, new { UserID = userId });
}
    }

    // DTO للإحصائيات - نضعه هنا مؤقتاً
    // أو يمكن نقله لـ SecurityDtos.cs
    public class LoginStatsDto
    {
        public int TotalAttempts { get; set; }
        public int SuccessCount { get; set; }
        public int FailedPasswordCount { get; set; }
        public int FailedLockedCount { get; set; }
        public int UniqueUsers { get; set; }
        public int UniqueIPs { get; set; }
    }
}