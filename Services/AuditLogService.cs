using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class AuditLogService : BaseDbService
    {
        public AuditLogService(IConfiguration configuration) : base(configuration) { }

        // ══════════════════════════════════════
        // 1) جلب سجل العمليات مع الفلاتر
        // ══════════════════════════════════════
        public async Task<List<AuditLogDto>> GetAllAsync(AuditLogFilterDto? filter = null)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT TOP 500
                    a.AuditID,
                    a.AuditDate,
                    a.UserID,
                    a.Username,
                    a.ActionType,
                    CASE a.ActionType
                        WHEN 1 THEN N'إضافة'
                        WHEN 2 THEN N'تعديل'
                        WHEN 3 THEN N'حذف'
                        WHEN 4 THEN N'عرض'
                        WHEN 5 THEN N'طباعة'
                    END AS ActionTypeName,
                    a.TableName,
                    a.RecordID,
                    a.ModuleName,
                    a.FormName,
                    a.Description,
                    a.IPAddress,
                    a.MachineName,
                    a.OldValues,
                    a.NewValues,
                    a.ChangedColumns
                FROM dbo.AuditLog a
                WHERE 1=1";

            var parameters = new DynamicParameters();

            // فلتر التاريخ من
            if (filter?.DateFrom.HasValue == true)
            {
                sql += " AND CAST(a.AuditDate AS DATE) >= @DateFrom";
                parameters.Add("DateFrom", filter.DateFrom.Value.Date);
            }

            // فلتر التاريخ إلى
            if (filter?.DateTo.HasValue == true)
            {
                sql += " AND CAST(a.AuditDate AS DATE) <= @DateTo";
                parameters.Add("DateTo", filter.DateTo.Value.Date);
            }

            // فلتر المستخدم
            if (!string.IsNullOrWhiteSpace(filter?.Username))
            {
                sql += " AND a.Username LIKE @Username";
                parameters.Add("Username", $"%{filter.Username}%");
            }

            // فلتر نوع العملية
            if (filter?.ActionType.HasValue == true)
            {
                sql += " AND a.ActionType = @ActionType";
                parameters.Add("ActionType", filter.ActionType.Value);
            }

            // فلتر اسم الجدول
            if (!string.IsNullOrWhiteSpace(filter?.TableName))
            {
                sql += " AND a.TableName LIKE @TableName";
                parameters.Add("TableName", $"%{filter.TableName}%");
            }

            // فلتر اسم الموديول
            if (!string.IsNullOrWhiteSpace(filter?.ModuleName))
            {
                sql += " AND a.ModuleName LIKE @ModuleName";
                parameters.Add("ModuleName", $"%{filter.ModuleName}%");
            }

            // فلتر معرّف السجل
            if (!string.IsNullOrWhiteSpace(filter?.RecordID))
            {
                sql += " AND a.RecordID = @RecordID";
                parameters.Add("RecordID", filter.RecordID);
            }

            sql += " ORDER BY a.AuditDate DESC";

            var result = await connection.QueryAsync<AuditLogDto>(sql, parameters);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 2) جلب تفاصيل سجل واحد
        // ══════════════════════════════════════
        public async Task<AuditLogDto?> GetByIdAsync(long auditId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT 
                    a.AuditID,
                    a.AuditDate,
                    a.UserID,
                    a.Username,
                    a.ActionType,
                    CASE a.ActionType
                        WHEN 1 THEN N'إضافة'
                        WHEN 2 THEN N'تعديل'
                        WHEN 3 THEN N'حذف'
                        WHEN 4 THEN N'عرض'
                        WHEN 5 THEN N'طباعة'
                    END AS ActionTypeName,
                    a.TableName,
                    a.RecordID,
                    a.ModuleName,
                    a.FormName,
                    a.Description,
                    a.IPAddress,
                    a.MachineName,
                    a.OldValues,
                    a.NewValues,
                    a.ChangedColumns
                FROM dbo.AuditLog a
                WHERE a.AuditID = @AuditID";

            return await connection.QueryFirstOrDefaultAsync<AuditLogDto>(sql, new { AuditID = auditId });
        }

        // ══════════════════════════════════════
        // 3) جلب أسماء الجداول المميزة (للفلتر)
        // ══════════════════════════════════════
        public async Task<List<string>> GetDistinctTablesAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT DISTINCT TableName
                FROM dbo.AuditLog
                WHERE TableName IS NOT NULL AND TableName != ''
                ORDER BY TableName";

            var result = await connection.QueryAsync<string>(sql);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 4) جلب أسماء الموديولات المميزة (للفلتر)
        // ══════════════════════════════════════
        public async Task<List<string>> GetDistinctModulesAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT DISTINCT ModuleName
                FROM dbo.AuditLog
                WHERE ModuleName IS NOT NULL AND ModuleName != ''
                ORDER BY ModuleName";

            var result = await connection.QueryAsync<string>(sql);
            return result.ToList();
        }

        // ══════════════════════════════════════
        // 5) إحصائيات سريعة
        // ══════════════════════════════════════
        public async Task<AuditStatsDto> GetStatsAsync(DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            using var connection = CreateConnection();

            var fromDate = dateFrom ?? DateTime.Today;
            var toDate = dateTo ?? DateTime.Today;

            var sql = @"
                SELECT
                    COUNT(*) AS TotalActions,
                    SUM(CASE WHEN ActionType = 1 THEN 1 ELSE 0 END) AS AddCount,
                    SUM(CASE WHEN ActionType = 2 THEN 1 ELSE 0 END) AS EditCount,
                    SUM(CASE WHEN ActionType = 3 THEN 1 ELSE 0 END) AS DeleteCount,
                    SUM(CASE WHEN ActionType = 4 THEN 1 ELSE 0 END) AS ViewCount,
                    SUM(CASE WHEN ActionType = 5 THEN 1 ELSE 0 END) AS PrintCount,
                    COUNT(DISTINCT Username) AS UniqueUsers
                FROM dbo.AuditLog
                WHERE CAST(AuditDate AS DATE) BETWEEN @DateFrom AND @DateTo";

            var stats = await connection.QueryFirstOrDefaultAsync<AuditStatsDto>(
                sql, new { DateFrom = fromDate.Date, DateTo = toDate.Date });

            return stats ?? new AuditStatsDto();
        }
    }

    // DTO للإحصائيات
    public class AuditStatsDto
    {
        public int TotalActions { get; set; }
        public int AddCount { get; set; }
        public int EditCount { get; set; }
        public int DeleteCount { get; set; }
        public int ViewCount { get; set; }
        public int PrintCount { get; set; }
        public int UniqueUsers { get; set; }
    }
}