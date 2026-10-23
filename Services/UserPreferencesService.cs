using Dapper;
using System.Data;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public sealed class UserPreferencesService : BaseDbService
    {
        private static readonly HashSet<int> SupportedPageSizes = new() { 10, 20, 50, 100 };
        private readonly UserSessionService _userSession;

        public event Action? PreferencesChanged;

        public UserPreferencesService(IConfiguration configuration, UserSessionService userSession)
            : base(configuration)
        {
            _userSession = userSession;
        }

        public async Task<UserPreferencesDto> GetCurrentAsync(int userId)
        {
            EnsureCurrentUser(userId);
            var values = await ReadCurrentValuesAsync(userId);

            var pageSize = 20;
            if (values.TryGetValue("PageSize", out var pageSizeValue)
                && int.TryParse(pageSizeValue, out var parsedPageSize)
                && SupportedPageSizes.Contains(parsedPageSize))
            {
                pageSize = parsedPageSize;
            }

            var showNotifications = true;
            if (values.TryGetValue("ShowNotifications", out var notificationsValue)
                && TryParseBoolean(notificationsValue, out var parsedShowNotifications))
            {
                showNotifications = parsedShowNotifications;
            }

            bool? playSound = null;
            if (values.TryGetValue("PlaySound", out var playSoundValue)
                && TryParseBoolean(playSoundValue, out var parsedPlaySound))
            {
                playSound = parsedPlaySound;
            }

            return new UserPreferencesDto
            {
                PageSize = pageSize,
                ShowNotifications = showNotifications,
                PlaySound = playSound
            };
        }

        public async Task<int> GetPageSizeAsync(int userId)
        {
            EnsureCurrentUser(userId);
            try
            {
                var values = await ReadCurrentValuesAsync(userId);
                return values.TryGetValue("PageSize", out var value)
                    && int.TryParse(value, out var pageSize)
                    && SupportedPageSizes.Contains(pageSize)
                        ? pageSize
                        : 20;
            }
            catch
            {
                // The list screens retain their existing default if a preference cannot be read.
                return 20;
            }
        }

        public async Task<bool> GetEffectivePlaySoundAsync(int userId)
        {
            var preferences = await GetCurrentAsync(userId);
            if (preferences.PlaySound.HasValue)
                return preferences.PlaySound.Value;

            using var connection = CreateConnection();
            var globalValue = await connection.QueryFirstOrDefaultAsync<string>(@"
SELECT SettingValue
FROM dbo.SystemSettings
WHERE SettingKey=N'NotificationSound'");
            if (string.IsNullOrWhiteSpace(globalValue))
                return true;
            return TryParseBoolean(globalValue, out var globalEnabled) && globalEnabled;
        }

        public async Task SaveAsync(UserPreferencesDto preferences, int userId)
        {
            EnsureCurrentUser(userId);
            ArgumentNullException.ThrowIfNull(preferences);
            if (!SupportedPageSizes.Contains(preferences.PageSize))
                throw new InvalidOperationException("اختر عدد سجلات مدعوماً: 10 أو 20 أو 50 أو 100.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                await SaveKeyAsync(connection, transaction, userId, "PageSize", preferences.PageSize.ToString());
                await SaveKeyAsync(connection, transaction, userId, "ShowNotifications", preferences.ShowNotifications ? "1" : "0");
                await SaveKeyAsync(connection, transaction, userId, "PlaySound", preferences.PlaySound.HasValue
                    ? (preferences.PlaySound.Value ? "1" : "0")
                    : null);

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            PreferencesChanged?.Invoke();
        }

        private async Task<Dictionary<string, string?>> ReadCurrentValuesAsync(int userId)
        {
            using var connection = CreateConnection();
            var rows = await connection.QueryAsync<UserPreferenceValueDto>(@"
SELECT PreferenceKey,PreferenceValue
FROM dbo.UserPreferences
WHERE UserID=@UserID
ORDER BY PreferenceID DESC", new { UserID = userId });

            // لو وُجدت بيانات قديمة مكررة، نعرض أحدث قيمة فقط؛ الحفظ سيرفض التكرار
            // بدلاً من تعديل سجل غير محدد بصمت.
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.PreferenceKey))
                    values.TryAdd(row.PreferenceKey.Trim(), row.PreferenceValue);
            }
            return values;
        }

        private static async Task SaveKeyAsync(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int userId,
            string key,
            string? value)
        {
            var count = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.UserPreferences WITH (UPDLOCK,HOLDLOCK)
WHERE UserID=@UserID AND PreferenceKey=@PreferenceKey",
                new { UserID = userId, PreferenceKey = key }, transaction);

            if (count > 1)
                throw new InvalidOperationException($"يوجد أكثر من سجل للتفضيل {key} لهذا المستخدم؛ صحّح التكرار قبل الحفظ.");

            if (value is null)
            {
                if (count == 1)
                    await connection.ExecuteAsync(@"
DELETE FROM dbo.UserPreferences
WHERE UserID=@UserID AND PreferenceKey=@PreferenceKey",
                        new { UserID = userId, PreferenceKey = key }, transaction);
                return;
            }

            if (count == 0)
            {
                await connection.ExecuteAsync(@"
INSERT INTO dbo.UserPreferences (UserID,PreferenceKey,PreferenceValue)
VALUES (@UserID,@PreferenceKey,@PreferenceValue)",
                    new { UserID = userId, PreferenceKey = key, PreferenceValue = value }, transaction);
                return;
            }

            var affected = await connection.ExecuteAsync(@"
UPDATE dbo.UserPreferences
SET PreferenceValue=@PreferenceValue
WHERE UserID=@UserID AND PreferenceKey=@PreferenceKey",
                new { UserID = userId, PreferenceKey = key, PreferenceValue = value }, transaction);
            if (affected != 1)
                throw new InvalidOperationException($"تعذر حفظ التفضيل {key}؛ أعد المحاولة.");
        }

        private void EnsureCurrentUser(int userId)
        {
            if (userId <= 0 || !_userSession.IsLoggedIn || _userSession.UserID != userId)
                throw new UnauthorizedAccessException("يمكن عرض أو تعديل تفضيلات المستخدم الحالي فقط.");
        }

        private static bool TryParseBoolean(string? value, out bool result)
        {
            var normalized = value?.Trim();
            if (normalized == "1" || bool.TryParse(normalized, out var parsedTrue) && parsedTrue)
            {
                result = true;
                return true;
            }
            if (normalized == "0" || bool.TryParse(normalized, out var parsedFalse) && !parsedFalse)
            {
                result = false;
                return true;
            }
            result = false;
            return false;
        }

        private sealed class UserPreferenceValueDto
        {
            public string? PreferenceKey { get; set; }
            public string? PreferenceValue { get; set; }
        }
    }
}
