using System.Data;
using Dapper;

namespace YKCoatings.Services
{
    internal static class LocalCurrencyGuard
    {
        public static async Task EnsureLocalCurrencyAsync(
            IDbConnection connection,
            IDbTransaction? transaction,
            int? currencyId,
            decimal exchangeRate)
        {
            if (Math.Abs(exchangeRate - 1m) > 0.000001m)
                throw new InvalidOperationException(
                    "لا يدعم هذا المسار العملات الأجنبية أو فروق الصرف؛ استخدم العملة الافتراضية بسعر صرف 1.");

            var resolvedCurrencyId = currencyId.GetValueOrDefault();
            if (resolvedCurrencyId < 0)
                throw new InvalidOperationException("معرّف العملة غير صالح.");
            if (resolvedCurrencyId == 0)
                return;

            var isDefaultCurrency = await connection.ExecuteScalarAsync<int?>(
                @"SELECT CONVERT(int,ISNULL(IsDefault,0))
                  FROM dbo.Currencies WITH (UPDLOCK,HOLDLOCK)
                  WHERE CurrencyID=@CurrencyID",
                new { CurrencyID = resolvedCurrencyId }, transaction);
            if (isDefaultCurrency != 1)
                throw new InvalidOperationException(
                    "لا يدعم هذا المسار العملات الأجنبية أو فروق الصرف؛ اختر العملة الافتراضية بسعر صرف 1.");
        }
    }
}
