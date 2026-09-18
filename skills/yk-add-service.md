# Skill: إضافة خدمة (Service) جديدة في YKCoatings

## متى تستخدمها
عند الحاجة لوحدة بيانات جديدة (مثال: المخازن الضريبية، شهادات الجودة).

## الخطوات
1. **DTOs** — أضف في `Models/<Domain>Dtos.cs`:
```csharp
public class WidgetListDto { public int WidgetID { get; set; } public string? NameAr { get; set; } public bool IsActive { get; set; } }
public class WidgetEditDto { public int WidgetID { get; set; } public string? NameAr { get; set; } public string? Notes { get; set; } public bool IsActive { get; set; } }
```
2. **Service** — أنشئ `Services/WidgetService.cs`:
```csharp
using Dapper;
namespace YKCoatings.Services
{
    public class WidgetService : BaseDbService
    {
        private readonly AuditService _audit;
        public WidgetService(IConfiguration cfg, AuditService audit) : base(cfg) { _audit = audit; }

        public async Task<List<WidgetListDto>> GetWidgetsAsync()
        {
            using var c = CreateConnection();
            return (await c.QueryAsync<WidgetListDto>(
                "SELECT WidgetID, NameAr, IsActive FROM dbo.Widgets WHERE IsActive = 1 ORDER BY NameAr")).ToList();
        }

        public async Task<int> InsertAsync(WidgetEditDto x, int userId)
        {
            using var c = CreateConnection();
            var id = await c.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.Widgets (NameAr, Notes, IsActive, CreatedBy, CreatedDate)
                  VALUES (@NameAr, @Notes, 1, @Uid, GETDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { x.NameAr, x.Notes, Uid = userId });
            await _audit.WriteAuditLogAsync("Widgets", id, "إضافة", userId);
            return id;
        }
    }
}
```
3. **التسجيل** في `Program.cs`: `builder.Services.AddScoped<WidgetService>();`
4. **الصفحة**: انظر skill الصفحات. أضف الكود+المسار في `Helpers/ModuleRouteHelper.cs` وجدول `SystemModules` على قاعدة البيانات.

## تحذيرات
- `ISNULL(col,0)` في كل SELECT لتفادي Null على القيم الرقمية.
- أسماء الجداول دائماً `dbo.<PluralName>` — تحقق من السكيما الفعلية أولاً.
