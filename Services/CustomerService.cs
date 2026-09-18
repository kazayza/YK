using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
namespace YKCoatings.Services
{
    public class CustomerService : BaseDbService
    {
        private readonly AuditService _audit;

        public CustomerService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }
                // ==========================================
        // قائمة العملاء مع Paging وفلاتر
        // ==========================================
        public async Task<CustomerPagedResult> GetCustomersPagedAsync(CustomerFilterDto filter)
        {
            using var connection = CreateConnection();

            filter ??= new CustomerFilterDto();

            var pageNumber = filter.PageNumber <= 0 ? 1 : filter.PageNumber;
            var pageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;
            if (pageSize > 100) pageSize = 100;

            var offset = (pageNumber - 1) * pageSize;

            var sql = @"
                ;WITH CTE AS
                (
                    SELECT
                        c.CustomerID,
                        c.CustomerCode,
                        c.CustomerNameAr,
                        c.CustomerNameEn,
                        c.CustomerType,
                        cg.GroupNameAr AS CustomerGroupName,
                        c.Phone1,
                        c.Mobile,
                        c.Email,
                        c.City,
                        pt.TermNameAr AS PaymentTermName,
                        pl.PriceListNameAr AS PriceListName,
                        e.FullNameAr AS SalesRepName,
                        cur.CurrencyNameAr AS CurrencyName,
                        ISNULL(c.CreditLimit, 0) AS CreditLimit,
                        ISNULL(c.CurrentBalance, 0) AS CurrentBalance,
                        ISNULL(c.DiscountPercent, 0) AS DiscountPercent,
                        ISNULL(c.Rating, 3) AS Rating,
                        c.IsActive,
                        COUNT(*) OVER() AS TotalCount
                    FROM dbo.Customers c
                    LEFT JOIN dbo.CustomerGroups cg ON c.CustomerGroupID = cg.GroupID
                    LEFT JOIN dbo.PaymentTerms pt ON c.PaymentTermID = pt.PaymentTermID
                    LEFT JOIN dbo.PriceLists pl ON c.PriceListID = pl.PriceListID
                    LEFT JOIN dbo.SalesRepresentatives sr ON c.SalesRepID = sr.SalesRepID
                    LEFT JOIN dbo.Employees e ON sr.EmployeeID = e.EmployeeID
                    LEFT JOIN dbo.Currencies cur ON c.CurrencyID = cur.CurrencyID
                    WHERE
                        (@IsActive IS NULL OR c.IsActive = @IsActive)
                        AND (@CustomerType = 0 OR c.CustomerType = @CustomerType)
                        AND (@CustomerGroupID = 0 OR c.CustomerGroupID = @CustomerGroupID)
                        AND (@SalesRepID = 0 OR c.SalesRepID = @SalesRepID)
                        AND (
                            ISNULL(@SearchText, N'') = N''
                            OR c.CustomerNameAr LIKE N'%' + @SearchText + N'%'
                            OR c.CustomerNameEn LIKE N'%' + @SearchText + N'%'
                            OR c.CustomerCode LIKE N'%' + @SearchText + N'%'
                            OR c.Phone1 LIKE N'%' + @SearchText + N'%'
                            OR c.Phone2 LIKE N'%' + @SearchText + N'%'
                            OR c.Mobile LIKE N'%' + @SearchText + N'%'
                            OR c.City LIKE N'%' + @SearchText + N'%'
                        )
                )
                SELECT
                    CustomerID,
                    CustomerCode,
                    CustomerNameAr,
                    CustomerNameEn,
                    CustomerType,
                    CustomerGroupName,
                    Phone1,
                    Mobile,
                    Email,
                    City,
                    PaymentTermName,
                    PriceListName,
                    SalesRepName,
                    CurrencyName,
                    CreditLimit,
                    CurrentBalance,
                    DiscountPercent,
                    Rating,
                    IsActive,
                    TotalCount
                FROM CTE
                ORDER BY CustomerNameAr, CustomerID
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

            var items = await connection.QueryAsync<CustomerListDto>(sql, new
            {
                SearchText = filter.SearchText,
                CustomerType = filter.CustomerType,
                CustomerGroupID = filter.CustomerGroupID,
                SalesRepID = filter.SalesRepID,
                IsActive = filter.IsActive,
                Offset = offset,
                PageSize = pageSize
            });

            var list = items.ToList();
            var totalCount = list.FirstOrDefault()?.TotalCount ?? 0;

            return new CustomerPagedResult
            {
                Items = list,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
                // ==========================================
        // قائمة العملاء للتقرير / الطباعة / التصدير
        // ==========================================
        public async Task<List<CustomerListDto>> GetCustomersReportAsync(CustomerFilterDto filter)
        {
            using var connection = CreateConnection();

            filter ??= new CustomerFilterDto();

            var sql = @"
                SELECT
                    c.CustomerID,
                    c.CustomerCode,
                    c.CustomerNameAr,
                    c.CustomerNameEn,
                    c.CustomerType,
                    cg.GroupNameAr AS CustomerGroupName,
                    c.Phone1,
                    c.Mobile,
                    c.Email,
                    c.City,
                    pt.TermNameAr AS PaymentTermName,
                    pl.PriceListNameAr AS PriceListName,
                    e.FullNameAr AS SalesRepName,
                    cur.CurrencyNameAr AS CurrencyName,
                    ISNULL(c.CreditLimit, 0) AS CreditLimit,
                    ISNULL(c.CurrentBalance, 0) AS CurrentBalance,
                    ISNULL(c.DiscountPercent, 0) AS DiscountPercent,
                    ISNULL(c.Rating, 3) AS Rating,
                    c.IsActive
                FROM dbo.Customers c
                LEFT JOIN dbo.CustomerGroups cg ON c.CustomerGroupID = cg.GroupID
                LEFT JOIN dbo.PaymentTerms pt ON c.PaymentTermID = pt.PaymentTermID
                LEFT JOIN dbo.PriceLists pl ON c.PriceListID = pl.PriceListID
                LEFT JOIN dbo.SalesRepresentatives sr ON c.SalesRepID = sr.SalesRepID
                LEFT JOIN dbo.Employees e ON sr.EmployeeID = e.EmployeeID
                LEFT JOIN dbo.Currencies cur ON c.CurrencyID = cur.CurrencyID
                WHERE
                    (@IsActive IS NULL OR c.IsActive = @IsActive)
                    AND (@CustomerType = 0 OR c.CustomerType = @CustomerType)
                    AND (@CustomerGroupID = 0 OR c.CustomerGroupID = @CustomerGroupID)
                    AND (@SalesRepID = 0 OR c.SalesRepID = @SalesRepID)
                    AND (
                        ISNULL(@SearchText, N'') = N''
                        OR c.CustomerNameAr LIKE N'%' + @SearchText + N'%'
                        OR c.CustomerNameEn LIKE N'%' + @SearchText + N'%'
                        OR c.CustomerCode LIKE N'%' + @SearchText + N'%'
                        OR c.Phone1 LIKE N'%' + @SearchText + N'%'
                        OR c.Phone2 LIKE N'%' + @SearchText + N'%'
                        OR c.Mobile LIKE N'%' + @SearchText + N'%'
                        OR c.City LIKE N'%' + @SearchText + N'%'
                    )
                ORDER BY c.CustomerNameAr, c.CustomerID;";

            var result = await connection.QueryAsync<CustomerListDto>(sql, new
            {
                SearchText = filter.SearchText,
                CustomerType = filter.CustomerType,
                CustomerGroupID = filter.CustomerGroupID,
                SalesRepID = filter.SalesRepID,
                IsActive = filter.IsActive
            });

            return result.ToList();
        }

        // ==========================================
        // إحصائيات العملاء
        // ==========================================
        public async Task<CustomerStatsDto> GetCustomerStatsAsync()
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS TotalActive,
                    SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) AS TotalInactive,
                    SUM(CASE WHEN CustomerType = 1 AND IsActive = 1 THEN 1 ELSE 0 END) AS TotalWholesale,
                    SUM(CASE WHEN CustomerType IN (2,3) AND IsActive = 1 THEN 1 ELSE 0 END) AS TotalRetail,
                    SUM(CASE WHEN CustomerType = 4 AND IsActive = 1 THEN 1 ELSE 0 END) AS TotalOnline,
                    SUM(CASE WHEN CustomerType = 5 AND IsActive = 1 THEN 1 ELSE 0 END) AS TotalInstitutional,
                    ISNULL(SUM(CASE WHEN IsActive = 1 THEN CurrentBalance ELSE 0 END), 0) AS TotalBalance
                FROM dbo.Customers";

            var result = await connection.QueryFirstOrDefaultAsync<CustomerStatsDto>(sql);
            return result ?? new CustomerStatsDto();
        }

        // ==========================================
        // تعطيل / إعادة تفعيل العميل
        // ==========================================
        public async Task<(bool Success, string Message)> SetCustomerActiveStatusAsync(
            int customerId, bool isActive, int userId)
        {
            using var connection = CreateConnection();

            var existing = await connection.QueryFirstOrDefaultAsync<CustomerEditDto>(
                @"SELECT CustomerID, CustomerNameAr, CurrentBalance, IsActive
                  FROM dbo.Customers
                  WHERE CustomerID = @ID",
                new { ID = customerId });

            if (existing == null)
                return (false, "العميل غير موجود");

            if (!isActive && existing.CurrentBalance != 0)
                return (false, $"لا يمكن تعطيل العميل لوجود رصيد ({existing.CurrentBalance:#,##0.00})");

            await connection.ExecuteAsync(@"
                UPDATE dbo.Customers
                SET IsActive = @IsActive,
                    ModifiedBy = @UserID,
                    ModifiedDate = GETDATE()
                WHERE CustomerID = @ID",
                new { ID = customerId, IsActive = isActive, UserID = userId });

            await _audit.WriteAuditLogAsync(
                userId,
                isActive ? (byte)2 : (byte)3,
                "Customers",
                customerId.ToString(),
                moduleName: "SCR_CUSTOMERS",
                description: isActive
                    ? $"إعادة تفعيل عميل: {existing.CustomerNameAr}"
                    : $"تعطيل عميل: {existing.CustomerNameAr}");

            return isActive
                ? (true, "تمت إعادة تفعيل العميل بنجاح")
                : (true, "تم تعطيل العميل بنجاح");
        }

        // ==========================================
        // توافق مع الكود القديم: الحذف = تعطيل
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteCustomerAsync(int customerId, int userId)
        {
            return await SetCustomerActiveStatusAsync(customerId, false, userId);
        }

        // ==========================================
        // جهات الاتصال
        // ==========================================
        public async Task<List<CustomerContactDto>> GetCustomerContactsAsync(int customerId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT
                    ContactID,
                    CustomerID,
                    ContactName,
                    JobTitle,
                    Phone,
                    Mobile,
                    Email,
                    IsPrimary,
                    Notes,
                    IsActive,
                    CreatedDate
                FROM dbo.CustomerContacts
                WHERE CustomerID = @CustomerID
                  AND IsActive = 1
                ORDER BY IsPrimary DESC, ContactName";

            var result = await connection.QueryAsync<CustomerContactDto>(sql, new { CustomerID = customerId });
            return result.ToList();
        }

        public async Task<int> SaveCustomerContactAsync(CustomerContactDto dto, int userId)
        {
            using var connection = CreateConnection();

            if (dto.CustomerID <= 0)
                throw new Exception("رقم العميل غير صحيح");

            if (string.IsNullOrWhiteSpace(dto.ContactName))
                throw new Exception("اسم جهة الاتصال مطلوب");

            if (dto.IsPrimary)
            {
                await connection.ExecuteAsync(@"
                    UPDATE dbo.CustomerContacts
                    SET IsPrimary = 0
                    WHERE CustomerID = @CustomerID
                      AND IsActive = 1
                      AND ContactID <> @ContactID",
                    new
                    {
                        dto.CustomerID,
                        dto.ContactID
                    });
            }

            if (dto.ContactID == 0)
            {
                var sql = @"
                    INSERT INTO dbo.CustomerContacts
                    (
                        CustomerID, ContactName, JobTitle, Phone,
                        Mobile, Email, IsPrimary, Notes, IsActive, CreatedDate
                    )
                    VALUES
                    (
                        @CustomerID, @ContactName, @JobTitle, @Phone,
                        @Mobile, @Email, @IsPrimary, @Notes, 1, GETDATE()
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, dto);

                await _audit.WriteAuditLogAsync(
                    userId,
                    1,
                    "CustomerContacts",
                    newId.ToString(),
                    newValues: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        dto.CustomerID,
                        dto.ContactName,
                        dto.Mobile,
                        dto.Email
                    }),
                    moduleName: "SCR_CUSTOMERS",
                    description: $"إضافة جهة اتصال للعميل رقم {dto.CustomerID}: {dto.ContactName}");

                return newId;
            }
            else
            {
                var oldContact = await connection.QueryFirstOrDefaultAsync<CustomerContactDto>(@"
                    SELECT ContactID, CustomerID, ContactName, JobTitle, Phone, Mobile, Email, IsPrimary, Notes
                    FROM dbo.CustomerContacts
                    WHERE ContactID = @ContactID",
                    new { dto.ContactID });

                var sql = @"
                    UPDATE dbo.CustomerContacts
                    SET ContactName = @ContactName,
                        JobTitle = @JobTitle,
                        Phone = @Phone,
                        Mobile = @Mobile,
                        Email = @Email,
                        IsPrimary = @IsPrimary,
                        Notes = @Notes
                    WHERE ContactID = @ContactID
                      AND CustomerID = @CustomerID";

                await connection.ExecuteAsync(sql, dto);

                await _audit.WriteAuditLogAsync(
                    userId,
                    2,
                    "CustomerContacts",
                    dto.ContactID.ToString(),
                    oldValues: oldContact != null
                        ? System.Text.Json.JsonSerializer.Serialize(new
                        {
                            oldContact.ContactName,
                            oldContact.Mobile,
                            oldContact.Email
                        })
                        : null,
                    newValues: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        dto.ContactName,
                        dto.Mobile,
                        dto.Email
                    }),
                    moduleName: "SCR_CUSTOMERS",
                    description: $"تعديل جهة اتصال للعميل رقم {dto.CustomerID}: {dto.ContactName}");

                return dto.ContactID;
            }
        }

        public async Task<(bool Success, string Message)> DeleteCustomerContactAsync(int contactId, int userId)
        {
            using var connection = CreateConnection();

            var oldContact = await connection.QueryFirstOrDefaultAsync<CustomerContactDto>(@"
                SELECT ContactID, CustomerID, ContactName
                FROM dbo.CustomerContacts
                WHERE ContactID = @ID",
                new { ID = contactId });

            if (oldContact == null)
                return (false, "جهة الاتصال غير موجودة");

            await connection.ExecuteAsync(@"
                UPDATE dbo.CustomerContacts
                SET IsActive = 0
                WHERE ContactID = @ID",
                new { ID = contactId });

            await _audit.WriteAuditLogAsync(
                userId,
                3,
                "CustomerContacts",
                contactId.ToString(),
                moduleName: "SCR_CUSTOMERS",
                description: $"حذف جهة اتصال: {oldContact.ContactName}");

            return (true, "تم حذف جهة الاتصال بنجاح");
        }

        public async Task<List<CustomerListDto>> GetCustomersListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            c.CustomerID, c.CustomerCode, c.CustomerNameAr, c.CustomerNameEn,
                            c.CustomerType, cg.GroupNameAr AS CustomerGroupName,
                            c.Phone1, c.Mobile, c.Email, c.City,
                            pt.TermNameAr AS PaymentTermName,
                            pl.PriceListNameAr AS PriceListName,
                            e.FullNameAr AS SalesRepName,
                            ISNULL(c.CreditLimit, 0) AS CreditLimit,
                            ISNULL(c.CurrentBalance, 0) AS CurrentBalance,
                            ISNULL(c.DiscountPercent, 0) AS DiscountPercent,
                            ISNULL(c.Rating, 3) AS Rating,
                            c.IsActive
                        FROM dbo.Customers c
                        LEFT JOIN dbo.CustomerGroups cg ON c.CustomerGroupID = cg.GroupID
                        LEFT JOIN dbo.PaymentTerms pt ON c.PaymentTermID = pt.PaymentTermID
                        LEFT JOIN dbo.PriceLists pl ON c.PriceListID = pl.PriceListID
                        LEFT JOIN dbo.SalesRepresentatives sr ON c.SalesRepID = sr.SalesRepID
                        LEFT JOIN dbo.Employees e ON sr.EmployeeID = e.EmployeeID
                        WHERE c.IsActive = 1
                        ORDER BY c.CustomerNameAr";
            var result = await connection.QueryAsync<CustomerListDto>(sql);
            return result.ToList();
        }

        public async Task<CustomerEditDto?> GetCustomerByIdAsync(int customerId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT CustomerID, CustomerCode, CustomerNameAr, CustomerNameEn,
                   CustomerType, CustomerGroupID, TaxNumber, CommercialRegister,
                   Address, City, Region, Country,
                   Phone1, Phone2, Mobile, Fax, Email, Website,
                   PaymentTermID, CurrencyID,
                   ISNULL(CreditLimit, 0) AS CreditLimit,
                   ISNULL(OpeningBalance, 0) AS OpeningBalance,
                   ISNULL(CurrentBalance, 0) AS CurrentBalance,
                   PriceListID, ISNULL(DiscountPercent, 0) AS DiscountPercent,
                   SalesRepID, BankName, BankAccountNumber,
                   ISNULL(Rating, 3) AS Rating, Notes, IsActive
            FROM dbo.Customers
            WHERE CustomerID = @CustomerID";
            return await connection.QueryFirstOrDefaultAsync<CustomerEditDto>(sql, new { CustomerID = customerId });
        }

        public async Task<int> InsertCustomerAsync(CustomerEditDto cust, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.Customers 
                        (CustomerCode, CustomerNameAr, CustomerNameEn, CustomerType, CustomerGroupID,
                         TaxNumber, CommercialRegister, Address, City, Region, Country,
                         Phone1, Phone2, Mobile, Fax, Email, Website,
                         PaymentTermID, CurrencyID, CreditLimit, OpeningBalance, CurrentBalance,
                         PriceListID, DiscountPercent, SalesRepID,
                         BankName, BankAccountNumber, Rating, Notes,
                         IsActive, CreatedBy, CreatedDate)
                        VALUES
                        (@CustomerCode, @CustomerNameAr, @CustomerNameEn, @CustomerType, NULLIF(@CustomerGroupID, 0),
                         @TaxNumber, @CommercialRegister, @Address, @City, @Region, @Country,
                         @Phone1, @Phone2, @Mobile, @Fax, @Email, @Website,
                         NULLIF(@PaymentTermID, 0), NULLIF(@CurrencyID, 0),
                         @CreditLimit, @OpeningBalance, @OpeningBalance,
                         NULLIF(@PriceListID, 0), @DiscountPercent, NULLIF(@SalesRepID, 0),
                         @BankName, @BankAccountNumber, @Rating, @Notes,
                         1, @UserID, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                cust.CustomerCode, cust.CustomerNameAr, cust.CustomerNameEn, cust.CustomerType,
                cust.CustomerGroupID, cust.TaxNumber, cust.CommercialRegister,
                cust.Address, cust.City, cust.Region, cust.Country,
                cust.Phone1, cust.Phone2, cust.Mobile, cust.Fax, cust.Email, cust.Website,
                cust.PaymentTermID, cust.CurrencyID, cust.CreditLimit, cust.OpeningBalance,
                cust.PriceListID, cust.DiscountPercent, cust.SalesRepID,
                cust.BankName, cust.BankAccountNumber, cust.Rating, cust.Notes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "Customers", newId.ToString(),
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { cust.CustomerCode, cust.CustomerNameAr, cust.CustomerType }),
                moduleName: "SCR_CUSTOMERS",
                description: $"إضافة عميل جديد: {cust.CustomerNameAr} ({cust.CustomerCode})");

            return newId;
        }

        public async Task UpdateCustomerAsync(CustomerEditDto cust, int userId)
        {
            using var connection = CreateConnection();

            var oldCust = await connection.QueryFirstOrDefaultAsync<CustomerEditDto>(
    @"SELECT CustomerCode, CustomerNameAr, CustomerType, Phone1, Fax, Email,
             CreditLimit, DiscountPercent, Rating
      FROM dbo.Customers
      WHERE CustomerID = @CustomerID",
    new { cust.CustomerID });

            var sql = @"UPDATE dbo.Customers SET
                            CustomerCode=@CustomerCode, CustomerNameAr=@CustomerNameAr,
                            CustomerNameEn=@CustomerNameEn, CustomerType=@CustomerType,
                            CustomerGroupID=NULLIF(@CustomerGroupID, 0),
                            TaxNumber=@TaxNumber, CommercialRegister=@CommercialRegister,
                            Address=@Address, City=@City, Region=@Region, Country=@Country,
                            Phone1=@Phone1, Phone2=@Phone2, Mobile=@Mobile, Fax=@Fax, Email=@Email, Website=@Website,
                            PaymentTermID=NULLIF(@PaymentTermID, 0), CurrencyID=NULLIF(@CurrencyID, 0),
                            CreditLimit=@CreditLimit,
                            PriceListID=NULLIF(@PriceListID, 0), DiscountPercent=@DiscountPercent,
                            SalesRepID=NULLIF(@SalesRepID, 0),
                            BankName=@BankName, BankAccountNumber=@BankAccountNumber,
                            Rating=@Rating, Notes=@Notes,
                            ModifiedBy=@UserID, ModifiedDate=GETDATE()
                        WHERE CustomerID=@CustomerID";

            await connection.ExecuteAsync(sql, new
            {
                cust.CustomerID, cust.CustomerCode, cust.CustomerNameAr, cust.CustomerNameEn,
                cust.CustomerType, cust.CustomerGroupID, cust.TaxNumber, cust.CommercialRegister,
                cust.Address, cust.City, cust.Region, cust.Country,
                cust.Phone1, cust.Phone2, cust.Mobile, cust.Fax, cust.Email, cust.Website,
                cust.PaymentTermID, cust.CurrencyID, cust.CreditLimit,
                cust.PriceListID, cust.DiscountPercent, cust.SalesRepID,
                cust.BankName, cust.BankAccountNumber, cust.Rating, cust.Notes,
                UserID = userId
            });

            var changes = new List<string>();
            if (oldCust != null)
            {
                if (oldCust.CustomerNameAr != cust.CustomerNameAr) changes.Add("CustomerNameAr");
                if (oldCust.CustomerType != cust.CustomerType) changes.Add("CustomerType");
                if (oldCust.Phone1 != cust.Phone1) changes.Add("Phone1");
                if (oldCust.Fax != cust.Fax) changes.Add("Fax");
                if (oldCust.Email != cust.Email) changes.Add("Email");
                if (oldCust.CreditLimit != cust.CreditLimit) changes.Add("CreditLimit");
                if (oldCust.DiscountPercent != cust.DiscountPercent) changes.Add("DiscountPercent");
                if (oldCust.Rating != cust.Rating) changes.Add("Rating");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "Customers", cust.CustomerID.ToString(),
                oldValues: oldCust != null ? System.Text.Json.JsonSerializer.Serialize(new
                { oldCust.CustomerNameAr, oldCust.CustomerType, oldCust.CreditLimit }) : null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new
                { cust.CustomerNameAr, cust.CustomerType, cust.CreditLimit }),
                changedColumns: changes.Any() ? string.Join(",", changes) : null,
                moduleName: "SCR_CUSTOMERS",
                description: $"تعديل عميل: {cust.CustomerNameAr} — تغيير {changes.Count} حقل");
        }

        

                public async Task<byte[]> ExportCustomersToExcelAsync(List<CustomerListDto> customers, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("العملاء");

            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير العملاء — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 13).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 13).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"تاريخ التصدير: {DateTime.Now:dd/MM/yyyy hh:mm tt}";
            ws.Range(2, 1, 2, 13).Merge().Style.Font.FontSize = 9;
            ws.Range(2, 1, 2, 13).Style.Font.FontColor = XLColor.Gray;
            ws.Range(2, 1, 2, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 4;
            var headers = new[]
            {
                "#",
                "الكود",
                "اسم العميل",
                "النوع",
                "المجموعة",
                "الهاتف",
                "المدينة",
                "العملة",
                "المندوب",
                "حد الائتمان",
                "الرصيد",
                "الحالة",
                "التقييم"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d143f");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = headerRow + 1;
            int num = 0;

            foreach (var cust in customers)
            {
                num++;

                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = cust.CustomerCode ?? "";
                ws.Cell(row, 3).Value = cust.CustomerNameAr ?? "";
                ws.Cell(row, 4).Value = GetCustomerTypeName(cust.CustomerType);
                ws.Cell(row, 5).Value = cust.CustomerGroupName ?? "—";
                ws.Cell(row, 6).Value = cust.Phone1 ?? cust.Mobile ?? "—";
                ws.Cell(row, 7).Value = cust.City ?? "—";
                ws.Cell(row, 8).Value = cust.CurrencyName ?? "—";
                ws.Cell(row, 9).Value = cust.SalesRepName ?? "—";
                ws.Cell(row, 10).Value = cust.CreditLimit;
                ws.Cell(row, 11).Value = cust.CurrentBalance;
                ws.Cell(row, 12).Value = cust.IsActive ? "نشط" : "غير نشط";
                ws.Cell(row, 13).Value = cust.Rating;

                ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";

                for (int i = 1; i <= 13; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e5e7eb");
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f7ff");

                row++;
            }

            if (customers.Any())
            {
                ws.Cell(row + 1, 1).Value = "إجمالي العملاء";
                ws.Cell(row + 1, 2).Value = customers.Count;

                ws.Cell(row + 2, 1).Value = "إجمالي الأرصدة";
                ws.Cell(row + 2, 2).Value = customers.Sum(x => x.CurrentBalance);
                ws.Cell(row + 2, 2).Style.NumberFormat.Format = "#,##0.00";
            }

            ws.Columns().AdjustToContents();
            ws.Column(3).Width = 30;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId,
                5,
                "Customers",
                moduleName: "SCR_CUSTOMERS",
                description: $"تصدير تقرير العملاء إلى Excel ({customers.Count} سجل)");

            return stream.ToArray();
        }

        public static string GetCustomerTypeName(int customerType)
        {
            return customerType switch
            {
                1 => "جملة",
                2 => "نص جملة",
                3 => "تجزئة",
                4 => "أونلاين",
                5 => "مؤسسي",
                _ => "غير محدد"
            };
        }

                public async Task<string> GenerateCustomerCodeAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                           EXEC sp_GetNextNumber N'CUST', @NextNum OUTPUT;
                           SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"CUST-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"CUST-{DateTime.Now:yyMMddHHmmss}"; }
        }
    }
}