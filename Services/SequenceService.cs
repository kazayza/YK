using System.Data;
using System.Text.Json;
using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class SequenceService : BaseDbService
    {
        private const string SettingsModuleCode = "SCR_SETTINGS";

        // Mirrors dbo.sp_GetNextNumber: annual reset may restart at 1, and the optional
        // year component is part of the generated code. Prefix is NVARCHAR(10) in the SP.
        private const string NextNumberPreviewExpression = @"
CASE
    WHEN ISNULL(ns.IsActive, 0) = 0
      OR ns.Prefix IS NULL OR LEN(LTRIM(RTRIM(ns.Prefix))) = 0
      OR ns.CurrentNumber IS NULL
      OR ns.NumberLength IS NULL
      OR ns.NumberLength < 1
      OR ns.NumberLength > 50 - (DATALENGTH(LEFT(ns.Prefix, 10)) / 2)
                                  - CASE WHEN ISNULL(ns.IncludeYear, 0) = 1 THEN 6 ELSE 1 END
      OR ns.CurrentNumber < 0
      OR (ns.CurrentNumber >= 2147483647
          AND NOT (ISNULL(ns.ResetYearly, 0) = 1
                   AND (ns.LastResetYear IS NULL OR ns.LastResetYear < YEAR(GETDATE()))))
      OR (ns.NumberLength < 10
          AND CASE WHEN ISNULL(ns.ResetYearly, 0) = 1
                         AND (ns.LastResetYear IS NULL OR ns.LastResetYear < YEAR(GETDATE()))
                   THEN CAST(1 AS BIGINT)
                   ELSE CAST(ns.CurrentNumber AS BIGINT) + 1 END
              >= POWER(CAST(10 AS BIGINT), ns.NumberLength))
      OR LEN(ns.SequenceCode) > 20
    THEN CAST(NULL AS NVARCHAR(50))
    ELSE LEFT(ns.Prefix, 10)
         + CASE WHEN ISNULL(ns.IncludeYear, 0) = 1
                THEN N'-' + CONVERT(NVARCHAR(4), YEAR(GETDATE())) + N'-'
                ELSE N'-' END
         + RIGHT(REPLICATE(N'0', ns.NumberLength)
                 + CONVERT(NVARCHAR(10), CASE
                       WHEN ns.ResetYearly = 1
                        AND (ns.LastResetYear IS NULL OR ns.LastResetYear < YEAR(GETDATE()))
                       THEN 1 ELSE CAST(ns.CurrentNumber AS BIGINT) + 1 END),
                 ns.NumberLength)
END";

        private readonly ModuleAuthorizationService _authorization;
        private readonly AuditService _audit;

        public SequenceService(
            IConfiguration configuration,
            ModuleAuthorizationService authorization,
            AuditService audit) : base(configuration)
        {
            _authorization = authorization;
            _audit = audit;
        }

        /// <summary>
        /// Generates the next code through dbo.sp_GetNextNumber. The active state and
        /// uniqueness of SequenceCode are checked under a serializable lock because the
        /// current procedure itself does not filter IsActive and the table has no unique
        /// SequenceCode index in the supplied schema extract.
        /// </summary>
        public async Task<string> GetNextCodeAsync(string sequenceCode)
        {
            if (string.IsNullOrWhiteSpace(sequenceCode))
                throw new ArgumentException("رمز التسلسل مطلوب.", nameof(sequenceCode));

            var normalizedCode = sequenceCode.Trim();
            if (normalizedCode.Length > 20)
                throw new InvalidOperationException("إجراء توليد الأرقام يقبل رمز تسلسل حتى 20 حرفاً فقط.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var matches = (await connection.QueryAsync<SequenceState>(
                    @"SELECT SequenceID, IsActive, Prefix, CurrentNumber, NumberLength,
                             IncludeYear, ResetYearly, LastResetYear,
                             YEAR(GETDATE()) AS DatabaseCurrentYear
                      FROM dbo.NumberSequences WITH (UPDLOCK, HOLDLOCK)
                      WHERE SequenceCode=@Code",
                    new { Code = normalizedCode }, transaction)).ToList();

                if (matches.Count != 1)
                    throw new InvalidOperationException(matches.Count == 0
                        ? $"لا يوجد تسلسل وحيد مُعدّ بالرمز '{normalizedCode}'."
                        : $"رمز التسلسل '{normalizedCode}' مكرر؛ أوقفنا التوليد لمنع تحديث أكثر من تسلسل.");
                var state = matches[0];
                if (state.IsActive != true)
                    throw new InvalidOperationException($"التسلسل '{normalizedCode}' غير مفعل؛ لم يُستهلك رقم.");
                if (string.IsNullOrWhiteSpace(state.Prefix))
                    throw new InvalidOperationException($"بادئة التسلسل '{normalizedCode}' فارغة في قاعدة البيانات؛ لم يُستهلك رقم.");
                if (!state.CurrentNumber.HasValue || state.CurrentNumber.Value < 0)
                    throw new InvalidOperationException($"العداد الحالي للتسلسل '{normalizedCode}' غير صالح؛ لم يُستهلك رقم.");
                if (!state.NumberLength.HasValue || state.NumberLength.Value < 1
                    || state.NumberLength.Value > GetMaxNumberLength(state.Prefix, state.IncludeYear == true))
                    throw new InvalidOperationException($"طول الرقم للتسلسل '{normalizedCode}' يتجاوز سعة الإجراء المخزن؛ لم يُستهلك رقم.");
                var willReset = state.ResetYearly == true &&
                    (!state.LastResetYear.HasValue || state.LastResetYear.Value < state.DatabaseCurrentYear);
                var nextValue = willReset ? 1L : (long)state.CurrentNumber.Value + 1L;
                if (nextValue > int.MaxValue)
                    throw new InvalidOperationException($"عداد التسلسل '{normalizedCode}' بلغ الحد الأعلى؛ لم يُستهلك رقم.");
                if (!FitsNumberLength(nextValue, state.NumberLength.Value))
                    throw new InvalidOperationException($"الرقم التالي للتسلسل '{normalizedCode}' يتجاوز طول الجزء الرقمي؛ لم يُستهلك رقم.");

                var result = await connection.QueryFirstOrDefaultAsync<string>(
                    @"DECLARE @NextNum NVARCHAR(50);
                      EXEC dbo.sp_GetNextNumber @SequenceCode=@SeqCode, @NextNumber=@NextNum OUTPUT;
                      SELECT @NextNum AS NewCode;",
                    new { SeqCode = normalizedCode }, transaction);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً للتسلسل المطلوب.");

                transaction.Commit();
                return result;
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); } catch { }
                throw new InvalidOperationException(
                    $"تعذر توليد رقم للتسلسل '{normalizedCode}'. لم يُنشأ رقم بديل؛ راجع إعداد NumberSequences.", ex);
            }
        }

        /// <summary>
        /// Read-only preview using the same year/reset/prefix rules as dbo.sp_GetNextNumber.
        /// It never updates CurrentNumber or LastResetYear.
        /// </summary>
        public async Task<string> PeekNextCodeAsync(string sequenceCode)
        {
            if (string.IsNullOrWhiteSpace(sequenceCode))
                throw new ArgumentException("رمز التسلسل مطلوب.", nameof(sequenceCode));

            var normalizedCode = sequenceCode.Trim();
            using var connection = CreateConnection();
            try
            {
                var matches = (await connection.QueryAsync<string>(
                    $@"SELECT {NextNumberPreviewExpression} AS NextNumberPreview
                       FROM dbo.NumberSequences ns
                       WHERE ns.SequenceCode=@Code AND ISNULL(ns.IsActive,0)=1",
                    new { Code = normalizedCode })).ToList();

                if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0]))
                    throw new InvalidOperationException(matches.Count > 1
                        ? $"رمز التسلسل '{normalizedCode}' مكرر؛ لا يمكن عرض معاينة موثوقة."
                        : $"لا يوجد تسلسل فعال وصالح للمعاينة بالرمز '{normalizedCode}'.");
                return matches[0];
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"تعذر قراءة الرقم التالي للتسلسل '{normalizedCode}'. تحقق من إعدادات NumberSequences.", ex);
            }
        }

        public async Task<List<NumberSequenceDto>> GetManagedSequencesAsync(int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, SettingsModuleCode, permission => permission.CanView,
                "لا تملك صلاحية عرض إعدادات التسلسلات.");

            using var connection = CreateConnection();
            var sql = $@"SELECT
                            ns.SequenceID, ns.SequenceCode, ns.SequenceName, ns.Prefix,
                            ns.CurrentNumber, ns.NumberLength,
                            ISNULL(ns.IncludeYear, 0) AS IncludeYear,
                            ISNULL(ns.ResetYearly, 0) AS ResetYearly,
                            ns.LastResetYear,
                            ISNULL(ns.IsActive, 0) AS IsActive,
                            ns.CreatedDate,
                            {NextNumberPreviewExpression} AS NextNumberPreview
                         FROM dbo.NumberSequences ns
                         ORDER BY ns.SequenceCode, ns.SequenceID";
            var rows = await connection.QueryAsync<NumberSequenceDto>(sql);
            return rows.ToList();
        }

        public async Task<int> AddManagedSequenceAsync(NumberSequenceDto sequence, int userId)
        {
            ArgumentNullException.ThrowIfNull(sequence);
            await _authorization.RequireUserPermissionAsync(
                userId, SettingsModuleCode, permission => permission.CanAdd,
                "لا تملك صلاحية إضافة تسلسل مستند.");
            ValidateSequenceConfiguration(sequence, isNew: true);

            var code = sequence.SequenceCode.Trim();
            var name = sequence.SequenceName.Trim();
            var prefix = sequence.Prefix?.Trim() ?? string.Empty;
            var currentNumber = sequence.CurrentNumber!.Value;
            var numberLength = sequence.NumberLength!.Value;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            int newId;
            try
            {
                var databaseYear = await connection.ExecuteScalarAsync<int>("SELECT YEAR(GETDATE())", transaction: transaction);
                if (sequence.LastResetYear.HasValue && sequence.LastResetYear.Value > databaseYear)
                    throw new InvalidOperationException("سنة آخر إعادة ضبط لا يمكن أن تتجاوز سنة قاعدة البيانات الحالية.");
                ValidateNextNumberFits(
                    currentNumber, numberLength, sequence.ResetYearly, sequence.LastResetYear, databaseYear);

                var existingCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.NumberSequences WITH (UPDLOCK, HOLDLOCK)
                      WHERE SequenceCode=@Code",
                    new { Code = code }, transaction);
                if (existingCount > 0)
                    throw new InvalidOperationException($"رمز التسلسل '{code}' موجود بالفعل.");

                newId = await connection.QuerySingleAsync<int>(
                    @"INSERT INTO dbo.NumberSequences
                          (SequenceCode, SequenceName, Prefix, CurrentNumber, NumberLength,
                           IncludeYear, ResetYearly, LastResetYear, IsActive)
                      VALUES
                          (@SequenceCode, @SequenceName, @Prefix, @CurrentNumber, @NumberLength,
                           @IncludeYear, @ResetYearly, @LastResetYear, @IsActive);
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        SequenceCode = code,
                        SequenceName = name,
                        Prefix = prefix,
                        CurrentNumber = currentNumber,
                        NumberLength = numberLength,
                        sequence.IncludeYear,
                        sequence.ResetYearly,
                        sequence.LastResetYear,
                        sequence.IsActive
                    }, transaction);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId, 1, "NumberSequences", newId.ToString(),
                newValues: JsonSerializer.Serialize(new
                {
                    SequenceCode = code,
                    SequenceName = name,
                    Prefix = prefix,
                    CurrentNumber = currentNumber,
                    NumberLength = numberLength,
                    sequence.IncludeYear,
                    sequence.ResetYearly,
                    sequence.LastResetYear,
                    sequence.IsActive
                }),
                changedColumns: "SequenceCode,SequenceName,Prefix,CurrentNumber,NumberLength,IncludeYear,ResetYearly,LastResetYear,IsActive",
                moduleName: SettingsModuleCode,
                description: $"إضافة تسلسل مستند {code} — {name}");
            return newId;
        }

        public async Task UpdateManagedSequenceAsync(NumberSequenceDto sequence, int userId)
        {
            ArgumentNullException.ThrowIfNull(sequence);
            await _authorization.RequireUserPermissionAsync(
                userId, SettingsModuleCode, permission => permission.CanEdit,
                "لا تملك صلاحية تعديل إعدادات التسلسلات.");
            if (sequence.SequenceID <= 0) throw new ArgumentOutOfRangeException(nameof(sequence.SequenceID));
            ValidateSequenceConfiguration(sequence, isNew: false);

            var name = sequence.SequenceName.Trim();
            var prefix = sequence.Prefix?.Trim() ?? string.Empty;
            var numberLength = sequence.NumberLength;
            var oldValuesJson = string.Empty;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var current = await connection.QueryFirstOrDefaultAsync<SequenceState>(
                    @"SELECT SequenceID, SequenceCode, SequenceName, Prefix, IsActive,
                             CurrentNumber, NumberLength, IncludeYear, ResetYearly, LastResetYear,
                             YEAR(GETDATE()) AS DatabaseCurrentYear
                      FROM dbo.NumberSequences WITH (UPDLOCK, HOLDLOCK)
                      WHERE SequenceID=@ID",
                    new { ID = sequence.SequenceID }, transaction);
                if (current is null) throw new KeyNotFoundException("التسلسل المطلوب غير موجود.");
                oldValuesJson = JsonSerializer.Serialize(new
                {
                    current.SequenceName,
                    current.Prefix,
                    current.NumberLength,
                    current.IncludeYear,
                    current.ResetYearly,
                    current.IsActive
                });
                if (!string.Equals(current.SequenceCode?.Trim(), sequence.SequenceCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("رمز التسلسل غير قابل للتغيير؛ أعد تحميل الإعداد قبل الحفظ.");

                var codeCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.NumberSequences WITH (UPDLOCK, HOLDLOCK)
                      WHERE SequenceCode=@Code",
                    new { Code = current.SequenceCode }, transaction);
                if (codeCount != 1)
                    throw new InvalidOperationException("رمز التسلسل غير فريد؛ لم يتم تعديل الإعدادات.");

                if (sequence.IsActive)
                {
                    if (!current.CurrentNumber.HasValue)
                        throw new InvalidOperationException("العداد الحالي فارغ؛ لا يمكن تفعيل التسلسل قبل مراجعة قيمته.");
                    if (!numberLength.HasValue)
                        throw new InvalidOperationException("طول الجزء الرقمي مطلوب لتفعيل التسلسل.");
                    ValidateNextNumberFits(
                        current.CurrentNumber.Value, numberLength.Value, sequence.ResetYearly,
                        current.LastResetYear, current.DatabaseCurrentYear);
                }

                var affected = await connection.ExecuteAsync(
                    @"UPDATE dbo.NumberSequences
                      SET SequenceName=@SequenceName,
                          Prefix=@Prefix,
                          NumberLength=@NumberLength,
                          IncludeYear=@IncludeYear,
                          ResetYearly=@ResetYearly,
                          IsActive=@IsActive
                      WHERE SequenceID=@ID",
                    new
                    {
                        ID = sequence.SequenceID,
                        SequenceName = name,
                        Prefix = prefix,
                        NumberLength = numberLength,
                        sequence.IncludeYear,
                        sequence.ResetYearly,
                        sequence.IsActive
                    }, transaction);
                if (affected != 1) throw new InvalidOperationException("لم يتم تحديث التسلسل.");
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId, 2, "NumberSequences", sequence.SequenceID.ToString(),
                oldValues: oldValuesJson,
                newValues: JsonSerializer.Serialize(new
                {
                    SequenceName = name,
                    Prefix = prefix,
                    NumberLength = numberLength,
                    sequence.IncludeYear,
                    sequence.ResetYearly,
                    sequence.IsActive
                }),
                changedColumns: "SequenceName,Prefix,NumberLength,IncludeYear,ResetYearly,IsActive",
                moduleName: SettingsModuleCode,
                description: $"تعديل إعدادات التسلسل {sequence.SequenceCode.Trim()} — {name}");
        }

        private static void ValidateSequenceConfiguration(NumberSequenceDto sequence, bool isNew)
        {
            var code = sequence.SequenceCode?.Trim() ?? string.Empty;
            var name = sequence.SequenceName?.Trim() ?? string.Empty;
            var prefix = sequence.Prefix?.Trim() ?? string.Empty;
            var requiresUsableConfiguration = isNew || sequence.IsActive;
            if (code.Length == 0) throw new InvalidOperationException("رمز التسلسل مطلوب.");
            if (code.Length > 20)
                throw new InvalidOperationException("إجراء sp_GetNextNumber يقبل رمز تسلسل حتى 20 حرفاً فقط.");
            if (name.Length == 0) throw new InvalidOperationException("اسم التسلسل مطلوب لأن SequenceName عمود NOT NULL.");
            if (name.Length > 200) throw new InvalidOperationException("اسم التسلسل يتجاوز 200 حرف.");
            if (prefix.Length > 10)
                throw new InvalidOperationException("إجراء sp_GetNextNumber يستخدم بادئة حتى 10 أحرف فقط.");
            if (requiresUsableConfiguration && prefix.Length == 0)
                throw new InvalidOperationException("البادئة مطلوبة لأن إجراء التوليد يضيف شرطة ورقمًا حتى عند خلوها.");
            if (requiresUsableConfiguration &&
                (!sequence.NumberLength.HasValue || sequence.NumberLength.Value < 1
                 || sequence.NumberLength.Value > GetMaxNumberLength(prefix, sequence.IncludeYear)))
                throw new InvalidOperationException("طول الجزء الرقمي يتجاوز سعة إجراء sp_GetNextNumber.");
            if (requiresUsableConfiguration && sequence.ResetYearly && !sequence.IncludeYear)
                throw new InvalidOperationException("لا يمكن إعادة الرقم سنوياً دون تضمين السنة في الرمز؛ قد يتكرر رقم مستند قديم.");

            if (isNew)
            {
                if (!sequence.CurrentNumber.HasValue)
                    throw new InvalidOperationException("قيمة العداد الابتدائية مطلوبة؛ لا تُنشأ قيمة افتراضية للتسلسل.");
                var currentNumber = sequence.CurrentNumber.Value;
                if (currentNumber < 0) throw new InvalidOperationException("الرقم الحالي لا يمكن أن يكون سالباً.");
                if (sequence.LastResetYear.HasValue && sequence.LastResetYear.Value < 1900)
                    throw new InvalidOperationException("سنة آخر إعادة ضبط يجب أن تكون 1900 أو أحدث.");
                if (sequence.ResetYearly && currentNumber > 0 && !sequence.LastResetYear.HasValue)
                    throw new InvalidOperationException("أدخل LastResetYear إذا كان العداد الابتدائي غير صفر مع إعادة ضبط سنوية.");
            }
        }

        private static int GetMaxNumberLength(string? prefix, bool includeYear)
        {
            var effectivePrefixLength = Math.Min(prefix?.Length ?? 0, 10);
            var fixedPartLength = includeYear ? 6 : 1; // hyphen + year + hyphen, or one hyphen
            return 50 - effectivePrefixLength - fixedPartLength; // @NextNumber is NVARCHAR(50)
        }

        private static bool FitsNumberLength(long value, int numberLength)
        {
            if (value < 0) return false;
            if (numberLength >= 10) return value <= int.MaxValue;
            long exclusiveLimit = 1;
            for (var digit = 0; digit < numberLength; digit++) exclusiveLimit *= 10;
            return value < exclusiveLimit;
        }

        private static void ValidateNextNumberFits(
            int currentNumber, int numberLength, bool resetYearly, int? lastResetYear, int currentYear)
        {
            if (currentNumber < 0) throw new InvalidOperationException("العداد الحالي لا يمكن أن يكون سالباً.");
            var willReset = resetYearly && (!lastResetYear.HasValue || lastResetYear.Value < currentYear);
            var nextNumber = willReset ? 1L : (long)currentNumber + 1L;
            if (nextNumber > int.MaxValue)
                throw new InvalidOperationException("العداد بلغ الحد الأعلى الذي يدعمه الإجراء المخزن.");
            if (!FitsNumberLength(nextNumber, numberLength))
                throw new InvalidOperationException("الرقم التالي يتجاوز طول الجزء الرقمي؛ سيؤدي الإجراء إلى قص أرقام وقد يكرر مستنداً.");
        }

        private sealed class SequenceState
        {
            public int SequenceID { get; set; }
            public string? SequenceCode { get; set; }
            public string? SequenceName { get; set; }
            public bool? IsActive { get; set; }
            public string? Prefix { get; set; }
            public int? CurrentNumber { get; set; }
            public int? NumberLength { get; set; }
            public bool? IncludeYear { get; set; }
            public bool? ResetYearly { get; set; }
            public int? LastResetYear { get; set; }
            public int DatabaseCurrentYear { get; set; }
        }
    }
}
