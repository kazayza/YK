using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services;

public sealed class CustomerGroupService : BaseDbService
{
    private const string ModuleCode = "SCR_CUSTOMERS";
    private readonly AuditService _audit;
    private readonly ModuleAuthorizationService _authorization;

    public CustomerGroupService(
        IConfiguration configuration,
        AuditService audit,
        ModuleAuthorizationService authorization) : base(configuration)
    {
        _audit = audit;
        _authorization = authorization;
    }

    private Task RequireCurrentPermissionAsync(
        Func<ModulePermissionDto, bool> predicate,
        string deniedMessage) =>
        _authorization.RequireCurrentUserPermissionAsync(ModuleCode, predicate, deniedMessage);

    private Task RequireUserPermissionAsync(
        int userId,
        Func<ModulePermissionDto, bool> predicate,
        string deniedMessage) =>
        _authorization.RequireUserPermissionAsync(userId, ModuleCode, predicate, deniedMessage);

    public async Task<List<CustomerGroupDto>> GetAllAsync(bool includeInactive = true)
    {
        await RequireCurrentPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض مجموعات العملاء.");

        using var connection = CreateConnection();
        const string sql = @"
            SELECT g.GroupID, g.GroupCode, g.GroupNameAr, g.GroupNameEn,
                   ISNULL(g.DiscountPercent, 0) AS DiscountPercent,
                   g.PriceListID, pl.PriceListNameAr AS PriceListName,
                   g.Description, CAST(CASE WHEN ISNULL(g.IsActive, 0) <> 0 THEN 1 ELSE 0 END AS bit) AS IsActive,
                   g.CreatedDate,
                   (SELECT COUNT(*) FROM dbo.Customers c WHERE c.CustomerGroupID = g.GroupID) AS CustomerCount
            FROM dbo.CustomerGroups g
            LEFT JOIN dbo.PriceLists pl ON pl.PriceListID = g.PriceListID
            WHERE @IncludeInactive = 1 OR ISNULL(g.IsActive, 0) <> 0
            ORDER BY g.GroupNameAr, g.GroupCode";

        var result = await connection.QueryAsync<CustomerGroupDto>(sql, new { IncludeInactive = includeInactive });
        return result.ToList();
    }

    public async Task<int> InsertAsync(CustomerGroupDto group, int userId)
    {
        await RequireUserPermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إضافة مجموعات العملاء.");
        NormalizeAndValidate(group);

        using var connection = CreateConnection();
        await ValidateUniqueCodeAsync(connection, group.GroupCode, 0);
        await ValidatePriceListAsync(connection, group.PriceListID, null);

        // عند عدم اختيار قائمة، لا نرسل قيمة مصطنعة؛ يظل NULL أو default قاعدة البيانات هو المصدر.
        var sql = group.PriceListID is > 0
            ? @"INSERT INTO dbo.CustomerGroups
                    (GroupCode, GroupNameAr, GroupNameEn, DiscountPercent, PriceListID, Description, IsActive, CreatedDate)
                VALUES
                    (@GroupCode, @GroupNameAr, @GroupNameEn, @DiscountPercent, @PriceListID, @Description, 1, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);"
            : @"INSERT INTO dbo.CustomerGroups
                    (GroupCode, GroupNameAr, GroupNameEn, DiscountPercent, Description, IsActive, CreatedDate)
                VALUES
                    (@GroupCode, @GroupNameAr, @GroupNameEn, @DiscountPercent, @Description, 1, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

        var newId = await connection.ExecuteScalarAsync<int>(sql, new
        {
            group.GroupCode,
            group.GroupNameAr,
            group.GroupNameEn,
            group.DiscountPercent,
            group.PriceListID,
            group.Description
        });

        await _audit.WriteAuditLogAsync(userId, 1, "CustomerGroups", newId.ToString(),
            moduleName: ModuleCode,
            description: $"إضافة مجموعة عملاء: {group.GroupNameAr} ({group.GroupCode})");

        return newId;
    }

    public async Task UpdateAsync(CustomerGroupDto group, int userId)
    {
        await RequireUserPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل مجموعات العملاء.");
        if (group.GroupID <= 0)
            throw new ArgumentException("معرّف مجموعة العملاء غير صالح.", nameof(group));

        NormalizeAndValidate(group);
        using var connection = CreateConnection();

        var existing = await connection.QueryFirstOrDefaultAsync<CustomerGroupDto>(
            @"SELECT GroupID, GroupCode, PriceListID
              FROM dbo.CustomerGroups WHERE GroupID = @GroupID",
            new { group.GroupID });
        if (existing is null)
            throw new InvalidOperationException("مجموعة العملاء غير موجودة.");

        await ValidateUniqueCodeAsync(connection, group.GroupCode, group.GroupID);
        await ValidatePriceListAsync(connection, group.PriceListID, existing.PriceListID);

        const string sql = @"
            UPDATE dbo.CustomerGroups
            SET GroupCode = @GroupCode,
                GroupNameAr = @GroupNameAr,
                GroupNameEn = @GroupNameEn,
                DiscountPercent = @DiscountPercent,
                PriceListID = @PriceListID,
                Description = @Description
            WHERE GroupID = @GroupID";

        var affected = await connection.ExecuteAsync(sql, new
        {
            group.GroupID,
            group.GroupCode,
            group.GroupNameAr,
            group.GroupNameEn,
            group.DiscountPercent,
            group.PriceListID,
            group.Description
        });
        if (affected != 1)
            throw new InvalidOperationException("لم يتم تعديل مجموعة العملاء؛ أعد تحميل البيانات وحاول مرة أخرى.");

        await _audit.WriteAuditLogAsync(userId, 2, "CustomerGroups", group.GroupID.ToString(),
            moduleName: ModuleCode,
            description: $"تعديل مجموعة عملاء: {group.GroupNameAr} ({group.GroupCode})");
    }

    public async Task<(bool Success, string Message)> SetActiveAsync(int groupId, bool isActive, int userId)
    {
        if (groupId <= 0)
            throw new ArgumentException("معرّف مجموعة العملاء غير صالح.", nameof(groupId));

        Func<ModulePermissionDto, bool> statusPermission = isActive
            ? permission => permission.CanEdit
            : permission => permission.CanDelete;
        await RequireUserPermissionAsync(
            userId,
            statusPermission,
            isActive ? "ليس لديك صلاحية إعادة تفعيل مجموعات العملاء." : "ليس لديك صلاحية تعطيل مجموعات العملاء.");

        using var connection = CreateConnection();
        var group = await connection.QueryFirstOrDefaultAsync<CustomerGroupDto>(
            "SELECT GroupID, GroupCode, GroupNameAr, IsActive FROM dbo.CustomerGroups WHERE GroupID = @GroupID",
            new { GroupID = groupId });
        if (group is null)
            return (false, "مجموعة العملاء غير موجودة.");

        if (!isActive)
        {
            var usedCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Customers WHERE CustomerGroupID = @GroupID",
                new { GroupID = groupId });
            if (usedCount > 0)
                return (false, $"لا يمكن تعطيل المجموعة؛ ما زالت مرتبطة بـ {usedCount} عميل. انقل العملاء إلى مجموعة أخرى أولاً.");
        }

        var affected = await connection.ExecuteAsync(
            "UPDATE dbo.CustomerGroups SET IsActive = @IsActive WHERE GroupID = @GroupID",
            new { IsActive = isActive, GroupID = groupId });
        if (affected != 1)
            return (false, "لم تتغير حالة المجموعة؛ أعد تحميل البيانات وحاول مرة أخرى.");

        await _audit.WriteAuditLogAsync(userId, (byte)(isActive ? 2 : 3), "CustomerGroups", groupId.ToString(),
            moduleName: ModuleCode,
            description: $"{(isActive ? "تفعيل" : "تعطيل")} مجموعة العملاء: {group.GroupNameAr} ({group.GroupCode})");

        return (true, isActive ? "تم تفعيل المجموعة." : "تم تعطيل المجموعة.");
    }

    private static void NormalizeAndValidate(CustomerGroupDto group)
    {
        group.GroupCode = (group.GroupCode ?? string.Empty).Trim().ToUpperInvariant();
        group.GroupNameAr = (group.GroupNameAr ?? string.Empty).Trim();
        group.GroupNameEn = string.IsNullOrWhiteSpace(group.GroupNameEn) ? null : group.GroupNameEn.Trim();
        group.Description = string.IsNullOrWhiteSpace(group.Description) ? null : group.Description.Trim();
        if (group.PriceListID == 0)
            group.PriceListID = null;

        if (string.IsNullOrWhiteSpace(group.GroupCode))
            throw new ArgumentException("كود المجموعة مطلوب.");
        if (group.GroupCode.Length > 20)
            throw new ArgumentException("كود المجموعة يجب ألا يتجاوز 20 حرفًا.");
        if (string.IsNullOrWhiteSpace(group.GroupNameAr))
            throw new ArgumentException("اسم المجموعة بالعربية مطلوب.");
        if (group.GroupNameAr.Length > 100)
            throw new ArgumentException("اسم المجموعة بالعربية يجب ألا يتجاوز 100 حرف.");
        if (group.GroupNameEn?.Length > 100)
            throw new ArgumentException("اسم المجموعة بالإنجليزية يجب ألا يتجاوز 100 حرف.");
        if (group.Description?.Length > 500)
            throw new ArgumentException("الوصف يجب ألا يتجاوز 500 حرف.");
        if (group.DiscountPercent < 0 || group.DiscountPercent > 100)
            throw new ArgumentException("نسبة الخصم يجب أن تكون بين 0 و100.");
    }

    private static async Task ValidateUniqueCodeAsync(System.Data.IDbConnection connection, string groupCode, int excludeGroupId)
    {
        var exists = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM dbo.CustomerGroups
              WHERE UPPER(LTRIM(RTRIM(GroupCode))) = @GroupCode AND GroupID <> @GroupID",
            new { GroupCode = groupCode, GroupID = excludeGroupId });
        if (exists > 0)
            throw new ArgumentException("كود المجموعة مستخدم بالفعل؛ اختر كودًا آخر.");
    }

    private static async Task ValidatePriceListAsync(
        System.Data.IDbConnection connection,
        int? priceListId,
        int? existingPriceListId)
    {
        if (!priceListId.HasValue || priceListId.Value == 0)
            return;

        if (priceListId.Value < 0)
        {
            if (existingPriceListId != priceListId)
                throw new ArgumentException("معرّف قائمة الأسعار غير صالح.");
            return;
        }

        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PriceLists WHERE PriceListID = @PriceListID",
            new { PriceListID = priceListId.Value });
        if (exists == 0)
            throw new ArgumentException("قائمة الأسعار المحددة غير موجودة.");
    }
}
