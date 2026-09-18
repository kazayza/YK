# Skill: قاعدة البيانات في YKCoatings (Dapper/SQL Server)

## القواعد
1. **اتصال فقط عبر `BaseDbService.CreateConnection()`** — لا `new SqlConnection` مباشر.
2. **معاملات دائماً:** `WHERE Id = @Id` + كائن `new { Id = x }`. ممنوع `$"…{x}…"`.
3. **قراءة:** `QueryAsync<T>` / `QueryFirstOrDefaultAsync<T>` مع DTO مطابق لأسماء الأعمدة (Column Aliasing عند الاختلاف).
4. **كتابة:** `ExecuteAsync` / `ExecuteScalarAsync<int>` مع `SCOPE_IDENTITY()`.
5. **تواريخ:** `GETDATE()` على الخادم، لا `DateTime.Now` داخل SQL.
6. **نصوص عربية:** داخل SQL الحرفية استخدم `N'...'` للقيم الحرفية العربية.
7. **Audit:** كل كتابة تتبعها `await _audit.WriteAuditLogAsync(...)`.

## مثال UPDATE آمن
```csharp
await connection.ExecuteAsync(@"
    UPDATE dbo.Suppliers
       SET NameAr = @NameAr, Phone = @Phone,
           ModifiedBy = @Uid, ModifiedDate = GETDATE()
     WHERE SupplierID = @SupplierID",
    new { x.NameAr, x.Phone, Uid = userId, x.SupplierID });
```

## ملاحظات السكيما
- الجداول الرئيسية: `SystemUsers, UserRoles, SystemModules, LoginHistory, Items, Units, Categories, Warehouses, Suppliers, Customers, PurchaseRequests/Orders/Invoices/Returns, GoodsReceipts, BOM*, Production*, Stock*, Employees, Departments, Attendance*, Leave*, Loans, Penalties, Payroll*, Notifications, AuditLog`.
- Views: `vw_UserEffectivePermissions`.
- لا توجد Migrations — أي تعديل سكيما يتم يدوياً على الخادم (تنسيق مطلوب).

## فحص سريع للاتصال
```csharp
var ok = await connection.ExecuteScalarAsync<int>("SELECT 1");
```
