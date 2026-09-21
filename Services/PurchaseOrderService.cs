using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class PurchaseOrderService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public PurchaseOrderService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        // ==========================================
        // قائمة أوامر الشراء
        // ==========================================
        public async Task<List<PurchaseOrderListDto>> GetOrdersListAsync()
        {
            using var connection = CreateConnection();

            var sql = @"SELECT
                            PurchaseOrderID,
                            PONumber,
                            PODate,
                            POStatus,
                            POSource,
                            POSourceName,
                            SupplierID,
                            SupplierCode,
                            SupplierNameAr,
                            RequestID,
                            LinkedPRNumber,
                            TotalAmount,
                            TaxAmount,
                            ExpectedDeliveryDate,
                            ApprovedDate,
                            SentDate,
                            ItemCount,
                            TotalOrderedQty,
                            TotalReceivedQty,
                            TotalRemainingQty,
                            IsOverdue,
                            DaysOverdue,
                            WarehouseNameAr,
                            PaymentTermName,
                            CurrencyCode,
                            RejectionReason,
                            CreatedDate
                        FROM dbo.vw_PurchaseOrderSummary
                        ORDER BY PODate DESC, PurchaseOrderID DESC";

            var result = await connection.QueryAsync<PurchaseOrderListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب أمر شراء واحد
        // ==========================================
        public async Task<PurchaseOrderHeaderDto?> GetOrderByIdAsync(int purchaseOrderId)
        {
            using var connection = CreateConnection();

            var sql = @"SELECT
                            po.PurchaseOrderID,
                            po.PONumber,
                            po.PODate,
                            po.POStatus,
                            po.POSource,

                            po.SupplierID,
                            s.SupplierNameAr,
                            s.SupplierCode,
                            s.Phone1 AS SupplierPhone,
                            s.CurrentBalance AS SupplierCurrentBalance,
                            s.CreditLimit AS SupplierCreditLimit,

                            po.RequestID,
                            pr.RequestNumber AS LinkedPRNumber,

                            po.ExpectedDeliveryDate,
                            po.PaymentTermID,
                            pt.TermNameAr AS PaymentTermName,
                            po.CurrencyID,
                            c.CurrencyCode,
                            c.Symbol AS CurrencySymbol,
                            po.ExchangeRate,
                            po.WarehouseID,
                            w.WarehouseNameAr,

                            po.SubTotal,
                            po.DiscountPercent,
                            po.DiscountAmount,
                            po.TaxAmount,
                            po.ShippingCost,
                            po.OtherCosts,
                            po.TotalAmount,

                            po.SubmittedBy,
                            po.SubmittedDate,
                            po.ApprovedBy,
                            po.ApprovedDate,
                            app.FullNameAr AS ApprovedByName,
                            po.RejectedBy,
                            po.RejectedDate,
                            po.RejectionReason,
                            po.SentBy,
                            po.SentDate,
                            po.SentMethod,

                            po.Notes,
                            po.InternalNotes,

                            po.CreatedBy,
                            po.CreatedDate,
                            po.ModifiedDate
                        FROM dbo.PurchaseOrders po
                        INNER JOIN dbo.Suppliers s ON po.SupplierID = s.SupplierID
                        LEFT JOIN dbo.PurchaseRequests pr ON po.RequestID = pr.RequestID
                        LEFT JOIN dbo.PaymentTerms pt ON po.PaymentTermID = pt.PaymentTermID
                        LEFT JOIN dbo.Currencies c ON po.CurrencyID = c.CurrencyID
                        LEFT JOIN dbo.Warehouses w ON po.WarehouseID = w.WarehouseID
                        LEFT JOIN dbo.Employees app ON po.ApprovedBy = app.EmployeeID
                        WHERE po.PurchaseOrderID = @ID";

            return await connection.QueryFirstOrDefaultAsync<PurchaseOrderHeaderDto>(sql, new { ID = purchaseOrderId });
        }

        // ==========================================
        // تفاصيل أمر الشراء
        // ==========================================
        public async Task<List<PurchaseOrderDetailDto>> GetOrderDetailsAsync(int purchaseOrderId)
        {
            using var connection = CreateConnection();

            var sql = @"SELECT
                            pod.PODetailID,
                            pod.PurchaseOrderID,
                            pod.LineNumber,

                            pod.ItemID,
                            i.ItemCode,
                            i.ItemNameAr,

                            pod.UnitID,
                            u.UnitNameAr AS UnitName,

                            pod.OrderedQty,
                            pod.ReceivedQty,
                            (pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) AS RemainingQty,

                            pod.RequestDetailID,
                            prd.ApprovedQty AS PRApprovedQty,
                            CASE 
                                WHEN pod.RequestDetailID IS NULL THEN NULL
                                ELSE ISNULL(prd.ApprovedQty, prd.RequestedQty) - ISNULL(otherpo.TotalOrdered, 0)
                            END AS PRRemainingToOrder,

                            pod.UnitPrice,
                            pod.DiscountPercent,
                            pod.DiscountAmount,
                            pod.TaxRate,
                            pod.TaxAmount,
                            pod.LineTotal,
                            pod.LineTotalWithTax,

                            pod.ExpectedDate,
                            pod.LineStatus,
                            pod.Notes
                        FROM dbo.PurchaseOrderDetails pod
                        INNER JOIN dbo.Items i ON pod.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON pod.UnitID = u.UnitID
                        LEFT JOIN dbo.PurchaseRequestDetails prd ON pod.RequestDetailID = prd.RequestDetailID
                        LEFT JOIN (
                            SELECT
                                p2.RequestDetailID,
                                SUM(p2.OrderedQty) AS TotalOrdered
                            FROM dbo.PurchaseOrderDetails p2
                            INNER JOIN dbo.PurchaseOrders po2 ON p2.PurchaseOrderID = po2.PurchaseOrderID
                            WHERE po2.POStatus NOT IN (4, 9)
                              AND p2.LineStatus != 4
                              AND p2.RequestDetailID IS NOT NULL
                              AND p2.PurchaseOrderID <> @POID
                            GROUP BY p2.RequestDetailID
                        ) otherpo ON pod.RequestDetailID = otherpo.RequestDetailID
                        WHERE pod.PurchaseOrderID = @POID
                        ORDER BY pod.LineNumber";

            var result = await connection.QueryAsync<PurchaseOrderDetailDto>(sql, new { POID = purchaseOrderId });
            return result.ToList();
        }

        // ==========================================
        // توليد رقم أمر الشراء
        // ==========================================
        public async Task<string> GeneratePONumberAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                            EXEC sp_GetNextNumber N'PO', @NextNum OUTPUT;
                            SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"PO-{DateTime.Now:yyMMddHHmmss}";
            }
            catch
            {
                return $"PO-{DateTime.Now:yyMMddHHmmss}";
            }
        }

        // ==========================================
        // إضافة أمر شراء جديد
        // ==========================================
        public async Task<int> InsertOrderAsync(PurchaseOrderHeaderDto header, int userId)
        {
            using var connection = CreateConnection();

            var sql = @"INSERT INTO dbo.PurchaseOrders
                        (
                            PONumber, PODate, SupplierID, RequestID, ExpectedDeliveryDate,
                            PaymentTermID, CurrencyID, ExchangeRate, WarehouseID,
                            DiscountPercent, ShippingCost, OtherCosts,
                            POStatus, POSource,
                            SupplierBalance, SupplierCreditLimit,
                            Notes, InternalNotes,
                            CreatedBy, CreatedDate
                        )
                        VALUES
                        (
                            @PONumber, @PODate, @SupplierID, NULLIF(@RequestID, 0), @ExpectedDeliveryDate,
                            NULLIF(@PaymentTermID, 0), NULLIF(@CurrencyID, 0), @ExchangeRate, NULLIF(@WarehouseID, 0),
                            @DiscountPercent, @ShippingCost, @OtherCosts,
                            @POStatus, @POSource,
                            ISNULL((SELECT CurrentBalance FROM dbo.Suppliers WHERE SupplierID = @SupplierID), 0),
                            ISNULL((SELECT CreditLimit FROM dbo.Suppliers WHERE SupplierID = @SupplierID), 0),
                            @Notes, @InternalNotes,
                            @UserID, GETDATE()
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                header.PONumber,
                header.PODate,
                header.SupplierID,
                header.RequestID,
                header.ExpectedDeliveryDate,
                header.PaymentTermID,
                header.CurrencyID,
                ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                header.WarehouseID,
                header.DiscountPercent,
                header.ShippingCost,
                header.OtherCosts,
                POStatus = header.POStatus == 0 ? 1 : header.POStatus,
                POSource = header.POSource == 0 ? (header.RequestID > 0 ? 2 : 1) : header.POSource,
                header.Notes,
                header.InternalNotes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(
                userId,
                1,
                "PurchaseOrders",
                newId.ToString(),
                moduleName: "SCR_PO",
                description: $"إنشاء أمر شراء: {header.PONumber}");

            return newId;
        }

        // ==========================================
        // تحديث أمر شراء
        // ==========================================
        public async Task UpdateOrderAsync(PurchaseOrderHeaderDto header, int userId)
        {
            using var connection = CreateConnection();

            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT POStatus FROM dbo.PurchaseOrders WHERE PurchaseOrderID = @ID",
                new { ID = header.PurchaseOrderID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل أمر شراء ليس في حالة مسودة");

            var sql = @"UPDATE dbo.PurchaseOrders SET
                            PODate = @PODate,
                            SupplierID = @SupplierID,
                            RequestID = NULLIF(@RequestID, 0),
                            ExpectedDeliveryDate = @ExpectedDeliveryDate,
                            PaymentTermID = NULLIF(@PaymentTermID, 0),
                            CurrencyID = NULLIF(@CurrencyID, 0),
                            ExchangeRate = @ExchangeRate,
                            WarehouseID = NULLIF(@WarehouseID, 0),
                            DiscountPercent = @DiscountPercent,
                            ShippingCost = @ShippingCost,
                            OtherCosts = @OtherCosts,
                            POSource = @POSource,
                            SupplierBalance = ISNULL((SELECT CurrentBalance FROM dbo.Suppliers WHERE SupplierID = @SupplierID), 0),
                            SupplierCreditLimit = ISNULL((SELECT CreditLimit FROM dbo.Suppliers WHERE SupplierID = @SupplierID), 0),
                            Notes = @Notes,
                            InternalNotes = @InternalNotes,
                            ModifiedBy = @UserID,
                            ModifiedDate = GETDATE()
                        WHERE PurchaseOrderID = @PurchaseOrderID";

            await connection.ExecuteAsync(sql, new
            {
                header.PurchaseOrderID,
                header.PODate,
                header.SupplierID,
                header.RequestID,
                header.ExpectedDeliveryDate,
                header.PaymentTermID,
                header.CurrencyID,
                ExchangeRate = header.ExchangeRate <= 0 ? 1 : header.ExchangeRate,
                header.WarehouseID,
                header.DiscountPercent,
                header.ShippingCost,
                header.OtherCosts,
                POSource = header.POSource == 0 ? (header.RequestID > 0 ? 2 : 1) : header.POSource,
                header.Notes,
                header.InternalNotes,
                UserID = userId
            });

            await RecalculateTotalsAsync(connection, header.PurchaseOrderID);

            await _audit.WriteAuditLogAsync(
                userId,
                2,
                "PurchaseOrders",
                header.PurchaseOrderID.ToString(),
                moduleName: "SCR_PO",
                description: $"تعديل أمر شراء: {header.PONumber}");
        }

        // ==========================================
        // إضافة سطر
        // ==========================================
        public async Task<int> InsertDetailAsync(PurchaseOrderDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT POStatus FROM dbo.PurchaseOrders WHERE PurchaseOrderID = @ID",
                    new { ID = detail.PurchaseOrderID }, transaction);

                if (status != 1)
                    throw new Exception("لا يمكن تعديل تفاصيل أمر شراء ليس في حالة مسودة");

                if (detail.ItemID <= 0)
                    throw new Exception("اختر الصنف");
                if (detail.UnitID <= 0)
                    throw new Exception("اختر الوحدة");
                if (detail.OrderedQty <= 0)
                    throw new Exception("أدخل الكمية المطلوبة");

                if (detail.RequestDetailID.HasValue && detail.RequestDetailID.Value > 0)
                {
                    var exists = await connection.QueryFirstOrDefaultAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.PurchaseOrderDetails
                          WHERE PurchaseOrderID = @POID
                            AND RequestDetailID = @RequestDetailID",
                        new { POID = detail.PurchaseOrderID, detail.RequestDetailID }, transaction);

                    if (exists > 0)
                        throw new Exception("هذا السطر من طلب الشراء مضاف بالفعل في أمر الشراء");

                    await ValidatePODetailQtyAsync(connection, detail.RequestDetailID.Value, detail.OrderedQty, 0, transaction);
                }

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseOrderDetails WHERE PurchaseOrderID = @ID",
                    new { ID = detail.PurchaseOrderID }, transaction);

                detail.LineNumber = maxLine + 1;

                var sql = @"INSERT INTO dbo.PurchaseOrderDetails
                            (
                                PurchaseOrderID, LineNumber, ItemID, UnitID,
                                OrderedQty, UnitPrice, DiscountPercent, TaxRate,
                                ExpectedDate, Notes, LineStatus, RequestDetailID
                            )
                            VALUES
                            (
                                @PurchaseOrderID, @LineNumber, @ItemID, @UnitID,
                                @OrderedQty, @UnitPrice, @DiscountPercent, @TaxRate,
                                @ExpectedDate, @Notes, 1, @RequestDetailID
                            );
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    detail.PurchaseOrderID,
                    detail.LineNumber,
                    detail.ItemID,
                    detail.UnitID,
                    detail.OrderedQty,
                    detail.UnitPrice,
                    detail.DiscountPercent,
                    TaxRate = detail.TaxRate <= 0 ? 14 : detail.TaxRate,
                    detail.ExpectedDate,
                    detail.Notes,
                    detail.RequestDetailID
                }, transaction);

                await RecalculateTotalsAsync(connection, detail.PurchaseOrderID, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId,
                    1,
                    "PurchaseOrderDetails",
                    newId.ToString(),
                    moduleName: "SCR_PO",
                    description: $"إضافة سطر في أمر شراء رقم {detail.PurchaseOrderID}");

                return newId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // تعديل سطر
        // ==========================================
        public async Task UpdateDetailAsync(PurchaseOrderDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT po.POStatus
                      FROM dbo.PurchaseOrders po
                      INNER JOIN dbo.PurchaseOrderDetails pod ON po.PurchaseOrderID = pod.PurchaseOrderID
                      WHERE pod.PODetailID = @ID",
                    new { ID = detail.PODetailID }, transaction);

                if (status != 1)
                    throw new Exception("لا يمكن تعديل تفاصيل أمر شراء ليس في حالة مسودة");

                if (detail.RequestDetailID.HasValue && detail.RequestDetailID.Value > 0)
                    await ValidatePODetailQtyAsync(connection, detail.RequestDetailID.Value, detail.OrderedQty, detail.PurchaseOrderID, transaction);

                var sql = @"UPDATE dbo.PurchaseOrderDetails SET
                                ItemID = @ItemID,
                                UnitID = @UnitID,
                                OrderedQty = @OrderedQty,
                                UnitPrice = @UnitPrice,
                                DiscountPercent = @DiscountPercent,
                                TaxRate = @TaxRate,
                                ExpectedDate = @ExpectedDate,
                                Notes = @Notes
                            WHERE PODetailID = @PODetailID";

                await connection.ExecuteAsync(sql, new
                {
                    detail.PODetailID,
                    detail.ItemID,
                    detail.UnitID,
                    detail.OrderedQty,
                    detail.UnitPrice,
                    detail.DiscountPercent,
                    TaxRate = detail.TaxRate <= 0 ? 14 : detail.TaxRate,
                    detail.ExpectedDate,
                    detail.Notes
                }, transaction);

                await RecalculateTotalsAsync(connection, detail.PurchaseOrderID, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId,
                    2,
                    "PurchaseOrderDetails",
                    detail.PODetailID.ToString(),
                    moduleName: "SCR_PO",
                    description: $"تعديل سطر في أمر شراء رقم {detail.PurchaseOrderID}");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // حذف سطر
        // ==========================================
        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var detailInfo = await connection.QueryFirstOrDefaultAsync<(int PurchaseOrderID, int POStatus)>(
                    @"SELECT pod.PurchaseOrderID, po.POStatus
                      FROM dbo.PurchaseOrderDetails pod
                      INNER JOIN dbo.PurchaseOrders po ON pod.PurchaseOrderID = po.PurchaseOrderID
                      WHERE pod.PODetailID = @ID",
                    new { ID = detailId }, transaction);

                if (detailInfo.PurchaseOrderID == 0)
                    throw new Exception("السطر غير موجود");

                if (detailInfo.POStatus != 1)
                    throw new Exception("لا يمكن حذف سطر من أمر شراء ليس في حالة مسودة");

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.PurchaseOrderDetails WHERE PODetailID = @ID",
                    new { ID = detailId }, transaction);

                await RecalculateTotalsAsync(connection, detailInfo.PurchaseOrderID, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId,
                    3,
                    "PurchaseOrderDetails",
                    detailId.ToString(),
                    moduleName: "SCR_PO",
                    description: "حذف سطر من أمر شراء");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // تحميل سطور من طلب شراء معتمد
        // ==========================================
        public async Task<int> ImportLinesFromPRAsync(
            int purchaseOrderId,
            int requestId,
            List<int> requestDetailIds,
            int supplierId,
            int userId)
        {
            if (requestDetailIds == null || !requestDetailIds.Any())
                return 0;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var poInfo = await connection.QueryFirstOrDefaultAsync<(int POStatus, int? RequestID)>(
                    @"SELECT POStatus, RequestID
                      FROM dbo.PurchaseOrders
                      WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                if (poInfo.POStatus != 1)
                    throw new Exception("لا يمكن تحميل سطور على أمر شراء ليس في حالة مسودة");

                var lines = (await connection.QueryAsync<PRLineForPODto>(
                    @"SELECT
                        RequestDetailID,
                        RequestID,
                        RequestNumber,
                        LineNumber,
                        ItemID,
                        ItemCode,
                        ItemNameAr,
                        UnitID,
                        UnitName,
                        RequestedQty,
                        ApprovedQty,
                        TotalOrderedQty,
                        RemainingToOrder,
                        EstimatedPrice,
                        CurrentStock,
                        Purpose
                      FROM dbo.vw_PRRemainingQty
                      WHERE RequestID = @RequestID
                        AND RequestDetailID IN @IDs
                        AND RemainingToOrder > 0
                      ORDER BY LineNumber",
                    new { RequestID = requestId, IDs = requestDetailIds }, transaction)).ToList();

                if (!lines.Any())
                    throw new Exception("لا توجد سطور متاحة للتحميل من طلب الشراء");

                if (!poInfo.RequestID.HasValue || poInfo.RequestID.Value == 0)
                {
                    await connection.ExecuteAsync(
                        @"UPDATE dbo.PurchaseOrders
                          SET RequestID = @RequestID,
                              POSource = 2
                          WHERE PurchaseOrderID = @POID",
                        new { RequestID = requestId, POID = purchaseOrderId }, transaction);
                }

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseOrderDetails WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                var inserted = 0;

                foreach (var line in lines)
                {
                    var exists = await connection.QueryFirstOrDefaultAsync<int>(
                        @"SELECT COUNT(*)
                          FROM dbo.PurchaseOrderDetails
                          WHERE PurchaseOrderID = @POID
                            AND RequestDetailID = @RequestDetailID",
                        new { POID = purchaseOrderId, line.RequestDetailID }, transaction);

                    if (exists > 0)
                        continue;

                    var priceInfo = await GetSuggestedLineInfoAsync(connection, supplierId, line.ItemID, transaction);

                    maxLine++;

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.PurchaseOrderDetails
                          (
                              PurchaseOrderID, LineNumber, ItemID, UnitID,
                              OrderedQty, UnitPrice, DiscountPercent, TaxRate,
                              ExpectedDate, Notes, LineStatus, RequestDetailID
                          )
                          VALUES
                          (
                              @PurchaseOrderID, @LineNumber, @ItemID, @UnitID,
                              @OrderedQty, @UnitPrice, 0, @TaxRate,
                              NULL, @Notes, 1, @RequestDetailID
                          )",
                        new
                        {
                            PurchaseOrderID = purchaseOrderId,
                            LineNumber = maxLine,
                            ItemID = line.ItemID,
                            UnitID = line.UnitID,
                            OrderedQty = line.RemainingToOrder,
                            UnitPrice = priceInfo?.SuggestedPrice ?? line.EstimatedPrice,
                            TaxRate = priceInfo?.TaxRate ?? 14m,
                            Notes = line.Purpose,
                            RequestDetailID = line.RequestDetailID
                        }, transaction);

                    inserted++;
                }

                await RecalculateTotalsAsync(connection, purchaseOrderId, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId,
                    1,
                    "PurchaseOrderDetails",
                    purchaseOrderId.ToString(),
                    moduleName: "SCR_PO",
                    description: $"تحميل {inserted} سطر من طلب شراء رقم {requestId} إلى أمر الشراء");

                return inserted;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // إرسال للاعتماد
        // ==========================================
        public async Task SubmitForApprovalAsync(int purchaseOrderId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string PONumber, int POStatus, int DetailsCount)>(
                @"SELECT 
                    po.PONumber,
                    po.POStatus,
                    (SELECT COUNT(*) FROM dbo.PurchaseOrderDetails d WHERE d.PurchaseOrderID = po.PurchaseOrderID) AS DetailsCount
                  FROM dbo.PurchaseOrders po
                  WHERE po.PurchaseOrderID = @ID",
                new { ID = purchaseOrderId });

            if (string.IsNullOrWhiteSpace(info.PONumber))
                throw new Exception("أمر الشراء غير موجود");

            if (info.POStatus != 1)
                throw new Exception("لا يمكن إرسال أمر شراء غير مسودة للاعتماد");

            if (info.DetailsCount == 0)
                throw new Exception("لا يمكن إرسال أمر شراء بدون أصناف");

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseOrders SET
                    POStatus = 2,
                    SubmittedBy = @EmployeeID,
                    SubmittedDate = GETDATE(),
                    ModifiedBy = @UserID,
                    ModifiedDate = GETDATE()
                  WHERE PurchaseOrderID = @ID",
                new { ID = purchaseOrderId, EmployeeID = employeeId, UserID = userId });

            try
            {
                var approverRoles = await connection.QueryAsync<int>(
                    @"SELECT DISTINCT r.RoleID
                      FROM dbo.UserRoles r
                      INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
                      INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
                      WHERE sm.ModuleCode = N'SCR_PO' AND rp.CanApprove = 1");

                foreach (var roleId in approverRoles)
                {
                    await _notif.CreateNotificationAsync(
                        notificationType: 3,
                        title: "أمر شراء بانتظار الاعتماد 📄",
                        message: $"أمر الشراء رقم {info.PONumber} بانتظار الاعتماد",
                        priority: 1,
                        targetUserId: null,
                        targetRoleId: roleId,
                        relatedModule: "SCR_PO",
                        relatedRecordId: purchaseOrderId,
                        createdBy: userId);
                }
            }
            catch { }

            await _audit.WriteAuditLogAsync(
                userId,
                2,
                "PurchaseOrders",
                purchaseOrderId.ToString(),
                moduleName: "SCR_PO",
                description: $"إرسال أمر شراء للاعتماد: {info.PONumber}");
        }

        // ==========================================
        // اعتماد أمر الشراء
        // ==========================================
        public async Task ApproveAsync(int purchaseOrderId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string poNumber = "";
            int? requestId = null;
            int? createdBy = null;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string PONumber, int POStatus, int? RequestID, int? CreatedBy)>(
                    @"SELECT PONumber, POStatus, RequestID, CreatedBy
                      FROM dbo.PurchaseOrders
                      WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                if (string.IsNullOrWhiteSpace(info.PONumber))
                    throw new Exception("أمر الشراء غير موجود");

                if (info.POStatus != 2)
                    throw new Exception("لا يمكن اعتماد أمر شراء ليس في انتظار الاعتماد");

                poNumber = info.PONumber;
                requestId = info.RequestID;
                createdBy = info.CreatedBy;

                await RecalculateTotalsAsync(connection, purchaseOrderId, transaction);

                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseOrders SET
                        POStatus = 3,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId, EmployeeID = employeeId, UserID = userId }, transaction);

                if (requestId.HasValue && requestId.Value > 0)
                    await RefreshPRExecutionStatusAsync(connection, requestId.Value, transaction);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            try
            {
                if (createdBy.HasValue)
                {
                    await _notif.CreateNotificationAsync(
                        notificationType: 3,
                        title: "تم اعتماد أمر الشراء ✅",
                        message: $"تم اعتماد أمر الشراء رقم {poNumber}",
                        priority: 2,
                        targetUserId: createdBy.Value,
                        relatedModule: "SCR_PO",
                        relatedRecordId: purchaseOrderId,
                        createdBy: userId);
                }

                await _notif.MarkRelatedAsActionedAsync("SCR_PO", purchaseOrderId);
            }
            catch { }

            await _audit.WriteAuditLogAsync(
                userId,
                2,
                "PurchaseOrders",
                purchaseOrderId.ToString(),
                moduleName: "SCR_PO",
                description: $"اعتماد أمر شراء: {poNumber}");
        }

        // ==========================================
        // رفض أمر الشراء
        // ==========================================
        public async Task RejectAsync(int purchaseOrderId, string rejectionReason, int userId, int? employeeId = null)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
                throw new Exception("أدخل سبب الرفض");

            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string PONumber, int POStatus, int? CreatedBy)>(
                @"SELECT PONumber, POStatus, CreatedBy
                  FROM dbo.PurchaseOrders
                  WHERE PurchaseOrderID = @ID",
                new { ID = purchaseOrderId });

            if (string.IsNullOrWhiteSpace(info.PONumber))
                throw new Exception("أمر الشراء غير موجود");

            if (info.POStatus != 2)
                throw new Exception("لا يمكن رفض أمر شراء ليس في انتظار الاعتماد");

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseOrders SET
                    POStatus = 4,
                    RejectedBy = @EmployeeID,
                    RejectedDate = GETDATE(),
                    RejectionReason = @Reason,
                    ModifiedBy = @UserID,
                    ModifiedDate = GETDATE()
                  WHERE PurchaseOrderID = @ID",
                new
                {
                    ID = purchaseOrderId,
                    EmployeeID = employeeId,
                    Reason = rejectionReason,
                    UserID = userId
                });

            try
            {
                if (info.CreatedBy.HasValue)
                {
                    await _notif.CreateNotificationAsync(
                        notificationType: 3,
                        title: "تم رفض أمر الشراء ❌",
                        message: $"تم رفض أمر الشراء رقم {info.PONumber}. السبب: {rejectionReason}",
                        priority: 1,
                        targetUserId: info.CreatedBy.Value,
                        relatedModule: "SCR_PO",
                        relatedRecordId: purchaseOrderId,
                        createdBy: userId);
                }

                await _notif.MarkRelatedAsActionedAsync("SCR_PO", purchaseOrderId);
            }
            catch { }

            await _audit.WriteAuditLogAsync(
                userId,
                2,
                "PurchaseOrders",
                purchaseOrderId.ToString(),
                moduleName: "SCR_PO",
                description: $"رفض أمر شراء: {info.PONumber} — السبب: {rejectionReason}");
        }

        // ==========================================
        // إرسال للمورد
        // ==========================================
        public async Task MarkAsSentAsync(int purchaseOrderId, string? sentMethod, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string PONumber, int POStatus)>(
                @"SELECT PONumber, POStatus
                  FROM dbo.PurchaseOrders
                  WHERE PurchaseOrderID = @ID",
                new { ID = purchaseOrderId });

            if (string.IsNullOrWhiteSpace(info.PONumber))
                throw new Exception("أمر الشراء غير موجود");

            if (info.POStatus != 3)
                throw new Exception("لا يمكن إرسال أمر شراء غير معتمد للمورد");

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseOrders SET
                    POStatus = 5,
                    SentBy = @EmployeeID,
                    SentDate = GETDATE(),
                    SentMethod = @SentMethod,
                    ModifiedBy = @UserID,
                    ModifiedDate = GETDATE()
                  WHERE PurchaseOrderID = @ID",
                new
                {
                    ID = purchaseOrderId,
                    EmployeeID = employeeId,
                    SentMethod = string.IsNullOrWhiteSpace(sentMethod) ? null : sentMethod,
                    UserID = userId
                });

            await _audit.WriteAuditLogAsync(
                userId,
                2,
                "PurchaseOrders",
                purchaseOrderId.ToString(),
                moduleName: "SCR_PO",
                description: $"إرسال أمر شراء للمورد: {info.PONumber}");
        }

        // ==========================================
        // إغلاق أمر الشراء
        // ==========================================
        public async Task CloseAsync(int purchaseOrderId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string PONumber, int POStatus)>(
                @"SELECT PONumber, POStatus
                  FROM dbo.PurchaseOrders
                  WHERE PurchaseOrderID = @ID",
                new { ID = purchaseOrderId });

            if (string.IsNullOrWhiteSpace(info.PONumber))
                throw new Exception("أمر الشراء غير موجود");

            if (info.POStatus != 7)
                throw new Exception("لا يمكن إغلاق أمر شراء إلا بعد الاستلام الكامل");

            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseOrders SET
                    POStatus = 8,
                    ClosedBy = @EmployeeID,
                    ClosedDate = GETDATE(),
                    ModifiedBy = @UserID,
                    ModifiedDate = GETDATE()
                  WHERE PurchaseOrderID = @ID",
                new { ID = purchaseOrderId, EmployeeID = employeeId, UserID = userId });

            await _audit.WriteAuditLogAsync(
                userId,
                2,
                "PurchaseOrders",
                purchaseOrderId.ToString(),
                moduleName: "SCR_PO",
                description: $"إغلاق أمر شراء: {info.PONumber}");
        }

        // ==========================================
        // إلغاء أمر الشراء
        // ==========================================
        public async Task CancelAsync(int purchaseOrderId, string? cancellationReason, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string poNumber = "";
            int? requestId = null;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string PONumber, int POStatus, int? RequestID)>(
                    @"SELECT PONumber, POStatus, RequestID
                      FROM dbo.PurchaseOrders
                      WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                if (string.IsNullOrWhiteSpace(info.PONumber))
                    throw new Exception("أمر الشراء غير موجود");

                if (info.POStatus == 8)
                    throw new Exception("لا يمكن إلغاء أمر شراء مغلق");

                if (info.POStatus == 9)
                    throw new Exception("أمر الشراء ملغي بالفعل");

                var receivedQty = await connection.QueryFirstOrDefaultAsync<decimal>(
                    @"SELECT ISNULL(SUM(ReceivedQty), 0)
                      FROM dbo.PurchaseOrderDetails
                      WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                if (receivedQty > 0)
                    throw new Exception("لا يمكن إلغاء أمر شراء تم الاستلام عليه");

                poNumber = info.PONumber;
                requestId = info.RequestID;

                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseOrders SET
                        POStatus = 9,
                        CancelledBy = @EmployeeID,
                        CancelledDate = GETDATE(),
                        CancellationReason = @Reason,
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE PurchaseOrderID = @ID",
                    new
                    {
                        ID = purchaseOrderId,
                        EmployeeID = employeeId,
                        Reason = cancellationReason,
                        UserID = userId
                    }, transaction);

                if (requestId.HasValue && requestId.Value > 0)
                    await RefreshPRExecutionStatusAsync(connection, requestId.Value, transaction);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            await _audit.WriteAuditLogAsync(
                userId,
                2,
                "PurchaseOrders",
                purchaseOrderId.ToString(),
                moduleName: "SCR_PO",
                description: $"إلغاء أمر شراء: {poNumber}" +
                             (string.IsNullOrWhiteSpace(cancellationReason) ? "" : $" — السبب: {cancellationReason}"));
        }

        // ==========================================
        // حذف أمر شراء (مسودة فقط)
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteOrderAsync(int purchaseOrderId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string PONumber, int POStatus)>(
                    "SELECT PONumber, POStatus FROM dbo.PurchaseOrders WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                if (string.IsNullOrWhiteSpace(info.PONumber))
                    return (false, "أمر الشراء غير موجود");

                if (info.POStatus != 1)
                    return (false, "لا يمكن حذف أمر شراء غير مسودة");

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.PurchaseOrderDetails WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.PurchaseOrders WHERE PurchaseOrderID = @ID",
                    new { ID = purchaseOrderId }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId,
                    3,
                    "PurchaseOrders",
                    purchaseOrderId.ToString(),
                    moduleName: "SCR_PO",
                    description: $"حذف أمر شراء: {info.PONumber}");

                return (true, "تم حذف أمر الشراء بنجاح");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, ex.Message);
            }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<PurchaseOrderListDto> orders, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("أوامر الشراء");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير أوامر الشراء — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 10).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 10).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[]
            {
                "#", "رقم الأمر", "التاريخ", "المصدر", "المورد",
                "طلب الشراء", "الحالة", "الإجمالي", "التوريد المتوقع", "الأصناف"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = headerRow + 1;
            int num = 0;
            foreach (var po in orders)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = po.PONumber ?? "";
                ws.Cell(row, 3).Value = po.PODate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = GetSourceName(po.POSource);
                ws.Cell(row, 5).Value = po.SupplierNameAr ?? "";
                ws.Cell(row, 6).Value = po.LinkedPRNumber ?? "—";
                ws.Cell(row, 7).Value = GetStatusName(po.POStatus);
                ws.Cell(row, 8).Value = po.TotalAmount;
                ws.Cell(row, 9).Value = po.ExpectedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—";
                ws.Cell(row, 10).Value = po.ItemCount;

                for (int i = 1; i <= 10; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e2e8f0");
                }

                if (num % 2 == 0)
                    ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(
                userId,
                5,
                "PurchaseOrders",
                moduleName: "SCR_PO",
                description: $"تصدير {orders.Count} أمر شراء إلى Excel");

            return stream.ToArray();
        }

        // ==========================================
        // تقرير طباعة
        // ==========================================
        public async Task<string> GenerateReportHtmlAsync(int purchaseOrderId, int userId)
        {
            var header = await GetOrderByIdAsync(purchaseOrderId);
            if (header == null) return "<h3>أمر الشراء غير موجود</h3>";

            var details = await GetOrderDetailsAsync(purchaseOrderId);

            var detailsHtml = "";
            int n = 0;
            foreach (var d in details)
            {
                n++;
                detailsHtml += $@"<tr>
                    <td style='text-align:center'>{n}</td>
                    <td>{d.ItemCode}</td>
                    <td>{d.ItemNameAr}</td>
                    <td>{d.UnitName}</td>
                    <td style='text-align:center'>{d.OrderedQty:#,##0.##}</td>
                    <td style='text-align:left'>{d.UnitPrice:#,##0.00}</td>
                    <td style='text-align:center'>{d.DiscountPercent:#,##0.##}%</td>
                    <td style='text-align:center'>{d.TaxRate:#,##0.##}%</td>
                    <td style='text-align:left'>{d.LineTotalWithTax:#,##0.00}</td>
                </tr>";
            }

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'>
<title>أمر شراء — {header.PONumber}</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Cairo','Segoe UI',sans-serif;padding:30px;color:#0f172a;font-size:13px}}
.header{{text-align:center;border-bottom:3px solid #1e293b;padding-bottom:20px;margin-bottom:24px}}
.company{{font-size:20px;font-weight:800}}
.doc-title{{font-size:15px;color:#475569;margin-top:4px}}
.doc-num{{display:inline-block;background:#f1f5f9;padding:4px 16px;border-radius:8px;font-weight:700;color:#3b82f6;margin-top:8px}}
.info-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:20px}}
.info-item{{background:#f8fafc;padding:10px;border-radius:8px;border:1px solid #e2e8f0}}
.info-label{{font-size:10px;color:#64748b;font-weight:600}}
.info-value{{font-size:13px;font-weight:700;margin-top:2px}}
table{{width:100%;border-collapse:collapse;margin-bottom:16px}}
th{{background:#1e293b;color:white;padding:10px;font-size:12px;text-align:right}}
td{{padding:9px 10px;border-bottom:1px solid #e2e8f0;font-size:12px}}
tr:nth-child(even){{background:#f8fafc}}
.total-row{{font-weight:800;background:#f1f5f9 !important}}
.footer{{text-align:center;margin-top:30px;padding-top:12px;border-top:1px solid #e2e8f0;font-size:10px;color:#94a3b8}}
</style></head><body>
<div class='header'>
    <div class='company'>مصنع واي كي كوتينج لمستحضرات التجميل</div>
    <div class='doc-title'>أمر شراء</div>
    <div class='doc-num'>{header.PONumber}</div>
</div>
<div class='info-grid'>
    <div class='info-item'><div class='info-label'>تاريخ الأمر</div><div class='info-value'>{header.PODate:dd/MM/yyyy}</div></div>
    <div class='info-item'><div class='info-label'>المورد</div><div class='info-value'>{header.SupplierNameAr}</div></div>
    <div class='info-item'><div class='info-label'>طلب الشراء</div><div class='info-value'>{header.LinkedPRNumber ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(header.POStatus)}</div></div>
    <div class='info-item'><div class='info-label'>المخزن</div><div class='info-value'>{header.WarehouseNameAr ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>شروط الدفع</div><div class='info-value'>{header.PaymentTermName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>العملة</div><div class='info-value'>{header.CurrencyCode ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>التوريد المتوقع</div><div class='info-value'>{header.ExpectedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—"}</div></div>
</div>
<table>
    <thead>
        <tr>
            <th>#</th>
            <th>الكود</th>
            <th>الصنف</th>
            <th>الوحدة</th>
            <th>الكمية</th>
            <th>السعر</th>
            <th>خصم</th>
            <th>ضريبة</th>
            <th>الإجمالي</th>
        </tr>
    </thead>
    <tbody>
        {detailsHtml}
        <tr class='total-row'>
            <td colspan='8' style='text-align:left'>الإجمالي الكلي</td>
            <td style='text-align:left'>{header.TotalAmount:#,##0.00} {(header.CurrencySymbol ?? "ج.م")}</td>
        </tr>
    </tbody>
</table>
{(string.IsNullOrWhiteSpace(header.Notes) ? "" : $"<div style='margin-bottom:16px'><strong>ملاحظات:</strong> {header.Notes}</div>")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP v2.0</div>
</body></html>";

            await _audit.WriteAuditLogAsync(
                userId,
                5,
                "PurchaseOrders",
                purchaseOrderId.ToString(),
                moduleName: "SCR_PO",
                description: $"طباعة أمر شراء: {header.PONumber}");

            return html;
        }

        // ==========================================
        // سجل التدقيق - من أضاف ومن عدّل + اعتماد
        // ==========================================
        public async Task<ItemAuditDto?> GetOrderAuditAsync(int purchaseOrderId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT ISNULL(uc.FullName, ISNULL(uc.Username, N'غير محدد')) AS CreatedByName, po.CreatedDate,
                               ISNULL(um.FullName, ISNULL(um.Username, N'')) AS ModifiedByName, po.ModifiedDate
                        FROM dbo.PurchaseOrders po
                        LEFT JOIN dbo.SystemUsers uc ON po.CreatedBy = uc.UserID
                        LEFT JOIN dbo.SystemUsers um ON po.ModifiedBy = um.UserID
                        WHERE po.PurchaseOrderID = @ID";
            return await connection.QueryFirstOrDefaultAsync<ItemAuditDto>(sql, new { ID = purchaseOrderId });
        }

        // ==========================================
        // Helpers: Status / Source
        // ==========================================
        public static string GetStatusName(int status)
        {
            return status switch
            {
                1 => "مسودة",
                2 => "مقدم للاعتماد",
                3 => "معتمد",
                4 => "مرفوض",
                5 => "مرسل للمورد",
                6 => "استلام جزئي",
                7 => "استلام كامل",
                8 => "مغلق",
                9 => "ملغي",
                _ => "غير محدد"
            };
        }

        public static string GetStatusColor(int status)
        {
            return status switch
            {
                1 => "#64748b",
                2 => "#f59e0b",
                3 => "#10b981",
                4 => "#ef4444",
                5 => "#3b82f6",
                6 => "#6366f1",
                7 => "#14b8a6",
                8 => "#0f172a",
                9 => "#dc2626",
                _ => "#6b7280"
            };
        }

        public static string GetSourceName(int source)
        {
            return source switch
            {
                1 => "يدوي",
                2 => "من طلب شراء",
                _ => "يدوي"
            };
        }

        public static string GetSourceColor(int source)
        {
            return source switch
            {
                1 => "#1d4ed8",
                2 => "#0891b2",
                _ => "#1d4ed8"
            };
        }

        // ==========================================
        // Private Helpers
        // ==========================================
        private async Task RecalculateTotalsAsync(SqlConnection connection, int purchaseOrderId, SqlTransaction? transaction = null)
        {
            await connection.ExecuteAsync(
                "EXEC dbo.sp_CalculatePOTotals @PurchaseOrderID",
                new { PurchaseOrderID = purchaseOrderId },
                transaction);
        }

        private async Task ValidatePODetailQtyAsync(
            SqlConnection connection,
            int requestDetailId,
            decimal orderedQty,
            int excludePOId = 0,
            SqlTransaction? transaction = null)
        {
            var p = new DynamicParameters();
            p.Add("@RequestDetailID", requestDetailId);
            p.Add("@OrderedQty", orderedQty);
            p.Add("@ExcludePOID", excludePOId);
            p.Add("@IsValid", dbType: System.Data.DbType.Boolean, direction: System.Data.ParameterDirection.Output);
            p.Add("@Message", dbType: System.Data.DbType.String, size: 200, direction: System.Data.ParameterDirection.Output);

            await connection.ExecuteAsync(
                "dbo.sp_ValidatePOQtyAgainstPR",
                p,
                transaction,
                commandType: System.Data.CommandType.StoredProcedure);

            var isValid = p.Get<bool>("@IsValid");
            var message = p.Get<string>("@Message");

            if (!isValid)
                throw new Exception(message);
        }

        private async Task RefreshPRExecutionStatusAsync(SqlConnection connection, int requestId, SqlTransaction? transaction = null)
        {
            var sql = @"
                ;WITH ReqLines AS
                (
                    SELECT
                        prd.RequestDetailID,
                        ISNULL(prd.ApprovedQty, prd.RequestedQty) AS RequiredQty
                    FROM dbo.PurchaseRequestDetails prd
                    WHERE prd.RequestID = @RequestID
                      AND ISNULL(prd.ApprovedQty, prd.RequestedQty) > 0
                ),
                OrderedLines AS
                (
                    SELECT
                        pod.RequestDetailID,
                        SUM(pod.OrderedQty) AS OrderedQty
                    FROM dbo.PurchaseOrderDetails pod
                    INNER JOIN dbo.PurchaseOrders po ON pod.PurchaseOrderID = po.PurchaseOrderID
                    WHERE po.RequestID = @RequestID
                      AND po.POStatus IN (3,5,6,7,8)
                      AND pod.LineStatus != 4
                      AND pod.RequestDetailID IS NOT NULL
                    GROUP BY pod.RequestDetailID
                )
                SELECT
                    COUNT(*) AS TotalLines,
                    SUM(CASE WHEN ISNULL(o.OrderedQty, 0) >= r.RequiredQty THEN 1 ELSE 0 END) AS CoveredLines
                FROM ReqLines r
                LEFT JOIN OrderedLines o ON r.RequestDetailID = o.RequestDetailID;";

            var result = await connection.QueryFirstOrDefaultAsync<(int TotalLines, int CoveredLines)>(
                sql, new { RequestID = requestId }, transaction);

            if (result.TotalLines > 0 && result.CoveredLines == result.TotalLines)
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseRequests
                      SET RequestStatus = 5,
                          ModifiedDate = GETDATE()
                      WHERE RequestID = @ID
                        AND RequestStatus IN (3,5)",
                    new { ID = requestId }, transaction);
            }
            else
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseRequests
                      SET RequestStatus = 3,
                          ModifiedDate = GETDATE()
                      WHERE RequestID = @ID
                        AND RequestStatus = 5",
                    new { ID = requestId }, transaction);
            }
        }

        private async Task<SuggestedLineInfo?> GetSuggestedLineInfoAsync(
            SqlConnection connection,
            int supplierId,
            int itemId,
            SqlTransaction? transaction = null)
        {
            var sql = @"
                SELECT
                    COALESCE(
                        NULLIF(si.UnitPrice, 0),
                        NULLIF(si.LastPurchasePrice, 0),
                        (
                            SELECT TOP 1 pid.UnitPrice
                            FROM dbo.PurchaseInvoiceDetails pid
                            INNER JOIN dbo.PurchaseInvoices pi ON pid.InvoiceID = pi.InvoiceID
                            WHERE pid.ItemID = i.ItemID
                              AND pi.SupplierID = @SupplierID
                              AND pi.InvoiceStatus NOT IN (6)
                            ORDER BY pi.InvoiceDate DESC
                        ),
                        NULLIF(i.LastPurchasePrice, 0),
                        NULLIF(i.AverageCost, 0),
                        NULLIF(i.StandardCost, 0),
                        0
                    ) AS SuggestedPrice,
                    ISNULL(i.TaxRate, 14) AS TaxRate
                FROM dbo.Items i
                LEFT JOIN dbo.SupplierItems si 
                    ON si.ItemID = i.ItemID 
                   AND si.SupplierID = @SupplierID
                   AND si.IsActive = 1
                WHERE i.ItemID = @ItemID";

            return await connection.QueryFirstOrDefaultAsync<SuggestedLineInfo>(
                sql,
                new { SupplierID = supplierId, ItemID = itemId },
                transaction);
        }

        private class SuggestedLineInfo
        {
            public decimal SuggestedPrice { get; set; }
            public decimal TaxRate { get; set; }
        }
    }
}