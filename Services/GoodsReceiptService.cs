using Dapper;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class GoodsReceiptService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;
        private readonly ModuleAuthorizationService _authorization;
        private readonly ILogger<GoodsReceiptService> _logger;
        private const string ModuleCode = "SCR_GRN";

        private sealed class GoodsReceiptInventoryPostingLine
        {
            public int GRNDetailID { get; set; }
            public int GRNID { get; set; }
            public string GRNNumber { get; set; } = "";
            public DateTime GRNDate { get; set; }
            public int ItemID { get; set; }
            public int UnitID { get; set; }
            public decimal AcceptedQty { get; set; }
            public decimal UnitCost { get; set; }
            public int WarehouseID { get; set; }
            public string? BatchNumber { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public DateTime? ManufactureDate { get; set; }
        }

        public GoodsReceiptService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif,
            ModuleAuthorizationService authorization,
            ILogger<GoodsReceiptService> logger)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
            _authorization = authorization;
            _logger = logger;
        }

        private Task RequireGoodsReceiptPermissionAsync(
            int userId, Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireUserPermissionAsync(userId, ModuleCode, predicate, deniedMessage);

        private Task RequireCurrentGoodsReceiptPermissionAsync(
            Func<ModulePermissionDto, bool> predicate, string deniedMessage) =>
            _authorization.RequireCurrentUserPermissionAsync(ModuleCode, predicate, deniedMessage);

        // ==========================================
        // قائمة أذونات الاستلام
        // ==========================================
        public async Task<List<GoodsReceiptListDto>> GetReceiptsListAsync()
        {
            await RequireCurrentGoodsReceiptPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض أذونات الاستلام.");
            using var connection = CreateConnection();
            var sql = @"SELECT
                            GRNID, GRNNumber, GRNDate, GRNStatus, GRNSource, GRNSourceName,
                            SupplierID, SupplierCode, SupplierNameAr,
                            PurchaseOrderID, LinkedPONumber,
                            CASE
                                WHEN (
                                    SELECT COUNT(DISTINCT COALESCE(NULLIF(gd.WarehouseID, 0), NULLIF(gn.WarehouseID, 0)))
                                    FROM dbo.GoodsReceiptDetails gd
                                    INNER JOIN dbo.GoodsReceiptNotes gn ON gn.GRNID = gd.GRNID
                                    WHERE gd.GRNID = summary.GRNID
                                ) > 1 THEN N'متعدد المخازن'
                                ELSE COALESCE((
                                    SELECT TOP (1) w.WarehouseNameAr
                                    FROM dbo.GoodsReceiptDetails gd
                                    INNER JOIN dbo.GoodsReceiptNotes gn ON gn.GRNID = gd.GRNID
                                    LEFT JOIN dbo.Warehouses w ON w.WarehouseID = COALESCE(NULLIF(gd.WarehouseID, 0), NULLIF(gn.WarehouseID, 0))
                                    WHERE gd.GRNID = summary.GRNID
                                    ORDER BY gd.LineNumber
                                ), summary.WarehouseNameAr)
                            END AS WarehouseNameAr,
                            InspectionStatus, InspectionStatusName,
                            SupplierInvoiceNo, ReceivedByName,
                            ItemCount, TotalReceivedQty, TotalAcceptedQty, TotalRejectedQty, TotalCost,
                            CreatedDate
                        FROM dbo.vw_GoodsReceiptSummary summary
                        ORDER BY GRNDate DESC, GRNID DESC";
            var result = await connection.QueryAsync<GoodsReceiptListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب إذن استلام واحد
        // ==========================================
        public async Task<GoodsReceiptHeaderDto?> GetReceiptByIdAsync(int grnId)
        {
            await RequireCurrentGoodsReceiptPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض أذونات الاستلام.");
            using var connection = CreateConnection();
            var sql = @"SELECT
                            grn.GRNID, grn.GRNNumber, grn.GRNDate, grn.GRNStatus,
                            ISNULL(grn.GRNSource, 1) AS GRNSource,

                            grn.PurchaseOrderID,
                            po.PONumber AS LinkedPONumber,

                            grn.SupplierID,
                            s.SupplierNameAr,
                            s.SupplierCode,

                            grn.WarehouseID,
                            w.WarehouseNameAr,

                            grn.SupplierInvoiceNo,
                            grn.SupplierInvoiceDate,
                            grn.DeliveryNoteNo,

                            grn.ReceivedBy,
                            rcv.FullNameAr AS ReceivedByName,
                            grn.AssignedInspectorID,
                            assignedInspector.FullNameAr AS AssignedInspectorName,
                            grn.InspectedBy,
                            insp.FullNameAr AS InspectedByName,
                            grn.InspectionStatus,
                            grn.InspectionNotes,
                            grn.InspectionRequestedDate,
                            grn.InspectionCompletedDate,
                            grn.ApprovalRequestedDate,

                            grn.ApprovedBy,
                            appr.FullNameAr AS ApprovedByName,
                            grn.ApprovedDate,

                            grn.Notes,
                            grn.CreatedBy,
                            grn.CreatedDate,
                            grn.ModifiedDate
                        FROM dbo.GoodsReceiptNotes grn
                        INNER JOIN dbo.Suppliers s ON grn.SupplierID = s.SupplierID
                        LEFT JOIN dbo.PurchaseOrders po ON grn.PurchaseOrderID = po.PurchaseOrderID
                        LEFT JOIN dbo.Warehouses w ON grn.WarehouseID = w.WarehouseID
                        LEFT JOIN dbo.Employees rcv ON grn.ReceivedBy = rcv.EmployeeID
                        LEFT JOIN dbo.Employees assignedInspector ON grn.AssignedInspectorID = assignedInspector.EmployeeID
                        LEFT JOIN dbo.Employees insp ON grn.InspectedBy = insp.EmployeeID
                        LEFT JOIN dbo.Employees appr ON grn.ApprovedBy = appr.EmployeeID
                        WHERE grn.GRNID = @ID";
            return await connection.QueryFirstOrDefaultAsync<GoodsReceiptHeaderDto>(sql, new { ID = grnId });
        }

        public async Task<List<LookupDto>> GetQualityInspectorsAsync()
        {
            await RequireCurrentGoodsReceiptPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض أذونات الاستلام.");
            using var connection = CreateConnection();
            var sql = @"SELECT DISTINCT e.EmployeeID AS Id, e.FullNameAr AS Name
                        FROM dbo.Employees e
                        INNER JOIN dbo.SystemUsers u ON u.EmployeeID = e.EmployeeID AND u.IsActive = 1
                        INNER JOIN dbo.vw_UserEffectivePermissions qc
                            ON qc.UserID = u.UserID AND qc.ModuleCode = N'SCR_QC'
                           AND qc.CanView = 1 AND qc.CanApprove = 1
                        INNER JOIN dbo.vw_UserEffectivePermissions grn
                            ON grn.UserID = u.UserID AND grn.ModuleCode = N'SCR_GRN'
                           AND grn.CanView = 1
                        WHERE e.IsActive = 1 AND e.EmployeeStatus = 1
                        ORDER BY e.FullNameAr";
            var result = await connection.QueryAsync<LookupDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // تفاصيل إذن الاستلام
        // ==========================================
        public async Task<List<GoodsReceiptDetailDto>> GetReceiptDetailsAsync(int grnId)
        {
            await RequireCurrentGoodsReceiptPermissionAsync(p => p.CanView, "ليس لديك صلاحية عرض تفاصيل أذونات الاستلام.");
            using var connection = CreateConnection();
            var sql = @"SELECT
                            grd.GRNDetailID, grd.GRNID, grd.LineNumber,
                            grd.ItemID, i.ItemCode, i.ItemNameAr,
                            grd.UnitID, u.UnitNameAr AS UnitName,
                            COALESCE(NULLIF(grd.WarehouseID, 0), NULLIF(grn.WarehouseID, 0)) AS WarehouseID,
                            COALESCE(grdWarehouse.WarehouseNameAr, headerWarehouse.WarehouseNameAr) AS WarehouseNameAr,
                            grd.OrderedQty, grd.ReceivedQty, grd.AcceptedQty, grd.RejectedQty,
                            grd.BatchNumber, grd.SupplierBatchNumber, grd.ExpiryDate, grd.ManufactureDate,
                            grd.UnitCost, grd.LineTotalCost,
                            grd.QualityStatus, grd.QualityNotes, grd.StorageLocation,
                            grd.PODetailID,
                            pod.OrderedQty AS POOrderedQty,
                            pod.ReceivedQty AS POReceivedQty,
                            (pod.OrderedQty - ISNULL(pod.ReceivedQty, 0)) AS PORemainingQty,
                            grd.Notes
                        FROM dbo.GoodsReceiptDetails grd
                        INNER JOIN dbo.Items i ON grd.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON grd.UnitID = u.UnitID
                        INNER JOIN dbo.GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
                        LEFT JOIN dbo.Warehouses grdWarehouse ON grd.WarehouseID = grdWarehouse.WarehouseID
                        LEFT JOIN dbo.Warehouses headerWarehouse ON grn.WarehouseID = headerWarehouse.WarehouseID
                        LEFT JOIN dbo.PurchaseOrderDetails pod ON grd.PODetailID = pod.PODetailID
                        WHERE grd.GRNID = @ID
                        ORDER BY grd.LineNumber";
            var result = await connection.QueryAsync<GoodsReceiptDetailDto>(sql, new { ID = grnId });
            return result.ToList();
        }

        // ==========================================
        // توليد رقم إذن الاستلام
        // ==========================================
        public async Task<string> GenerateGRNNumberAsync()
        {
            await RequireCurrentGoodsReceiptPermissionAsync(p => p.CanAdd, "ليس لديك صلاحية إنشاء أذونات الاستلام.");
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                            EXEC sp_GetNextNumber N'GRN', @NextNum OUTPUT;
                            SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                if (string.IsNullOrWhiteSpace(result))
                    throw new InvalidOperationException("لم يُرجع إجراء sp_GetNextNumber رقماً لإذن الاستلام.");

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر توليد رقم إذن الاستلام (GRN). لم يُنشأ رقم بديل؛ راجع إعداد التسلسل GRN والإجراء المخزن.", ex);
            }
        }

        // ==========================================
        // إضافة إذن استلام جديد
        // ==========================================
        public async Task<int> InsertReceiptAsync(GoodsReceiptHeaderDto header, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إنشاء أذونات الاستلام.");
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.GoodsReceiptNotes
                        (
                            GRNNumber, GRNDate, PurchaseOrderID, SupplierID, WarehouseID,
                            SupplierInvoiceNo, SupplierInvoiceDate, DeliveryNoteNo,
                            GRNStatus, GRNSource,
                            ReceivedBy, AssignedInspectorID, InspectedBy, InspectionStatus, InspectionNotes,
                            Notes, CreatedBy, CreatedDate
                        )
                        VALUES
                        (
                            @GRNNumber, @GRNDate, NULLIF(@PurchaseOrderID, 0), @SupplierID, @WarehouseID,
                            @SupplierInvoiceNo, @SupplierInvoiceDate, @DeliveryNoteNo,
                            @GRNStatus, @GRNSource,
                            NULLIF(@ReceivedBy, 0), NULLIF(@AssignedInspectorID, 0), NULL, 1, NULL,
                            @Notes, @UserID, GETDATE()
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                header.GRNNumber,
                header.GRNDate,
                header.PurchaseOrderID,
                header.SupplierID,
                header.WarehouseID,
                header.SupplierInvoiceNo,
                header.SupplierInvoiceDate,
                header.DeliveryNoteNo,
                GRNStatus = 1,
                GRNSource = header.GRNSource == 0 ? (header.PurchaseOrderID > 0 ? 1 : 2) : header.GRNSource,
                header.ReceivedBy,
                header.AssignedInspectorID,
                header.Notes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "GoodsReceiptNotes", newId.ToString(),
                moduleName: "SCR_GRN",
                description: $"إنشاء إذن استلام: {header.GRNNumber}");

            return newId;
        }

        // ==========================================
        // تحديث إذن استلام
        // ==========================================
        public async Task UpdateReceiptAsync(GoodsReceiptHeaderDto header, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل أذونات الاستلام.");
            using var connection = CreateConnection();

            var current = await connection.QueryFirstOrDefaultAsync<(int? GRNStatus, int InspectionStatus, DateTime? InspectionRequestedDate)>(
                @"SELECT GRNStatus, InspectionStatus, InspectionRequestedDate
                  FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                new { ID = header.GRNID });

            if (current.GRNStatus != 1)
                throw new Exception("لا يمكن تعديل إذن استلام ليس في حالة مسودة");
            if (current.InspectionStatus == 2 || (current.InspectionStatus == 1 && current.InspectionRequestedDate.HasValue))
                throw new InvalidOperationException("تم إرسال الإذن للفحص أو قبوله؛ لا يمكن تعديل بيانات الاستلام أثناء هذه المرحلة.");

            var sql = @"UPDATE dbo.GoodsReceiptNotes SET
                            GRNDate = @GRNDate,
                            SupplierID = @SupplierID,
                            WarehouseID = @WarehouseID,
                            SupplierInvoiceNo = @SupplierInvoiceNo,
                            SupplierInvoiceDate = @SupplierInvoiceDate,
                            DeliveryNoteNo = @DeliveryNoteNo,
                            ReceivedBy = NULLIF(@ReceivedBy, 0),
                            AssignedInspectorID = NULLIF(@AssignedInspectorID, 0),
                            Notes = @Notes,
                            ModifiedBy = @UserID,
                            ModifiedDate = GETDATE()
                        WHERE GRNID = @GRNID";

            await connection.ExecuteAsync(sql, new
            {
                header.GRNID,
                header.GRNDate,
                header.SupplierID,
                header.WarehouseID,
                header.SupplierInvoiceNo,
                header.SupplierInvoiceDate,
                header.DeliveryNoteNo,
                header.ReceivedBy,
                header.AssignedInspectorID,
                header.Notes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", header.GRNID.ToString(),
                moduleName: "SCR_GRN",
                description: $"تعديل إذن استلام: {header.GRNNumber}");
        }

        // ==========================================
        // إضافة سطر استلام
        // ==========================================
        public async Task<int> InsertDetailAsync(GoodsReceiptDetailEditDto detail, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية إضافة أصناف إلى أذونات الاستلام.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var receiptState = await connection.QueryFirstOrDefaultAsync<(int? GRNStatus, int InspectionStatus, DateTime? InspectionRequestedDate)>(
                    @"SELECT GRNStatus, InspectionStatus, InspectionRequestedDate
                      FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = detail.GRNID }, transaction);

                if (receiptState.GRNStatus != 1)
                    throw new Exception("لا يمكن تعديل إذن استلام ليس في حالة مسودة");
                if (receiptState.InspectionStatus == 2 || (receiptState.InspectionStatus == 1 && receiptState.InspectionRequestedDate.HasValue))
                    throw new InvalidOperationException("تم إرسال الإذن للفحص أو قبوله؛ لا يمكن تعديل سطور الاستلام أثناء هذه المرحلة.");

                if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
                if (detail.UnitID <= 0) throw new Exception("اختر الوحدة");
                if (detail.WarehouseID <= 0) throw new Exception("اختر مخزنًا لهذا الصنف");
                var supplierBatchNumber = string.IsNullOrWhiteSpace(detail.SupplierBatchNumber)
                    ? null
                    : detail.SupplierBatchNumber.Trim();
                if (detail.ReceivedQty <= 0) throw new Exception("أدخل الكمية المستلمة");
                var warehouseIsActive = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.Warehouses WHERE WarehouseID=@ID AND ISNULL(IsActive,0)=1",
                    new { ID = detail.WarehouseID }, transaction);
                if (warehouseIsActive != 1) throw new InvalidOperationException("المخزن المختار غير موجود أو غير نشط.");

                if (detail.PODetailID.HasValue && detail.PODetailID.Value > 0)
                {
                    var remaining = await connection.QueryFirstOrDefaultAsync<decimal>(
                        @"SELECT ISNULL(RemainingToReceive, 0)
                          FROM dbo.vw_POLinesForGRN
                          WHERE PODetailID = @ID",
                        new { ID = detail.PODetailID.Value }, transaction);

                    if (detail.ReceivedQty > remaining && remaining > 0)
                        throw new Exception($"الكمية المستلمة ({detail.ReceivedQty:#,##0.##}) تتجاوز المتبقي من أمر الشراء ({remaining:#,##0.##})");
                }

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.GoodsReceiptDetails WHERE GRNID = @ID",
                    new { ID = detail.GRNID }, transaction);

                detail.LineNumber = maxLine + 1;

                // Receipt entry records what arrived; only the assigned inspector records accepted/rejected quantities.
                detail.AcceptedQty = null;
                detail.RejectedQty = 0;
                detail.QualityStatus = 1;
                detail.QualityNotes = null;

                var sql = @"INSERT INTO dbo.GoodsReceiptDetails
                            (
                                GRNID, LineNumber, PODetailID, ItemID, UnitID, WarehouseID,
                                OrderedQty, ReceivedQty, AcceptedQty, RejectedQty,
                                BatchNumber, SupplierBatchNumber, ExpiryDate, ManufactureDate,
                                UnitCost, LineTotalCost,
                                StorageLocation, QualityStatus, QualityNotes, Notes
                            )
                            VALUES
                            (
                                @GRNID, @LineNumber, @PODetailID, @ItemID, @UnitID, @WarehouseID,
                                @OrderedQty, @ReceivedQty, @AcceptedQty, @RejectedQty,
                                @BatchNumber, @SupplierBatchNumber, @ExpiryDate, @ManufactureDate,
                                @UnitCost, @LineTotalCost,
                                @StorageLocation, @QualityStatus, @QualityNotes, @Notes
                            );
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    detail.GRNID,
                    detail.LineNumber,
                    detail.PODetailID,
                    detail.ItemID,
                    detail.UnitID,
                    detail.WarehouseID,
                    detail.OrderedQty,
                    detail.ReceivedQty,
                    AcceptedQty = (decimal?)null,
                    RejectedQty = 0m,
                    BatchNumber = supplierBatchNumber,
                    SupplierBatchNumber = supplierBatchNumber,
                    detail.ExpiryDate,
                    detail.ManufactureDate,
                    detail.UnitCost,
                    LineTotalCost = detail.LineTotalCost,
                    detail.StorageLocation,
                    QualityStatus = 1,
                    QualityNotes = (string?)null,
                    detail.Notes
                }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(userId, 1, "GoodsReceiptDetails", newId.ToString(),
                    moduleName: "SCR_GRN",
                    description: $"إضافة سطر في إذن استلام رقم {detail.GRNID}");

                return newId;
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after inserting a goods receipt detail for goods receipt {GoodsReceiptID}.",
                        detail.GRNID);
                }
                throw;
            }
        }

        // ==========================================
        // تعديل سطر استلام
        // ==========================================
        public async Task UpdateDetailAsync(GoodsReceiptDetailEditDto detail, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية تعديل أصناف أذونات الاستلام.");
            using var connection = CreateConnection();

            var receiptState = await connection.QueryFirstOrDefaultAsync<(int? GRNStatus, int InspectionStatus, DateTime? InspectionRequestedDate)>(
                @"SELECT grn.GRNStatus, grn.InspectionStatus, grn.InspectionRequestedDate
                  FROM dbo.GoodsReceiptNotes grn
                  INNER JOIN dbo.GoodsReceiptDetails grd ON grn.GRNID = grd.GRNID
                  WHERE grd.GRNDetailID = @ID",
                new { ID = detail.GRNDetailID });

            if (receiptState.GRNStatus != 1)
                throw new Exception("لا يمكن تعديل سطر في إذن استلام ليس في حالة مسودة");
            if (receiptState.InspectionStatus == 2 || (receiptState.InspectionStatus == 1 && receiptState.InspectionRequestedDate.HasValue))
                throw new InvalidOperationException("تم إرسال الإذن للفحص أو قبوله؛ لا يمكن تعديل سطور الاستلام أثناء هذه المرحلة.");
            if (detail.WarehouseID <= 0)
                throw new Exception("اختر مخزنًا لهذا الصنف");
            var supplierBatchNumber = string.IsNullOrWhiteSpace(detail.SupplierBatchNumber)
                ? null
                : detail.SupplierBatchNumber.Trim();
            var warehouseIsActive = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.Warehouses WHERE WarehouseID=@ID AND ISNULL(IsActive,0)=1",
                new { ID = detail.WarehouseID });
            if (warehouseIsActive != 1)
                throw new InvalidOperationException("المخزن المختار غير موجود أو غير نشط.");

            var sql = @"UPDATE dbo.GoodsReceiptDetails SET
                            ItemID = @ItemID, UnitID = @UnitID, WarehouseID = @WarehouseID,
                            OrderedQty = @OrderedQty,
                            ReceivedQty = @ReceivedQty,
                            AcceptedQty = NULL,
                            RejectedQty = 0,
                            BatchNumber = @BatchNumber,
                            SupplierBatchNumber = @SupplierBatchNumber,
                            ExpiryDate = @ExpiryDate,
                            ManufactureDate = @ManufactureDate,
                            UnitCost = @UnitCost,
                            LineTotalCost = @LineTotalCost,
                            StorageLocation = @StorageLocation,
                            QualityStatus = 1,
                            QualityNotes = NULL,
                            Notes = @Notes
                        WHERE GRNDetailID = @GRNDetailID";

            await connection.ExecuteAsync(sql, new
            {
                detail.GRNDetailID,
                detail.ItemID,
                detail.UnitID,
                detail.WarehouseID,
                detail.OrderedQty,
                detail.ReceivedQty,
                BatchNumber = supplierBatchNumber,
                SupplierBatchNumber = supplierBatchNumber,
                detail.ExpiryDate,
                detail.ManufactureDate,
                detail.UnitCost,
                LineTotalCost = detail.LineTotalCost,
                detail.StorageLocation,
                detail.Notes
            });

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptDetails", detail.GRNDetailID.ToString(),
                moduleName: "SCR_GRN",
                description: $"تعديل سطر في إذن استلام رقم {detail.GRNID}");
        }

        // ==========================================
        // حذف سطر استلام
        // ==========================================
        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanDelete, "ليس لديك صلاحية حذف أصناف أذونات الاستلام.");
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(int GRNID, int GRNStatus, int InspectionStatus, DateTime? InspectionRequestedDate)>(
                @"SELECT grd.GRNID, grn.GRNStatus, grn.InspectionStatus, grn.InspectionRequestedDate
                  FROM dbo.GoodsReceiptDetails grd
                  INNER JOIN dbo.GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
                  WHERE grd.GRNDetailID = @ID",
                new { ID = detailId });

            if (info.GRNID == 0) throw new Exception("السطر غير موجود");
            if (info.GRNStatus != 1) throw new Exception("لا يمكن حذف سطر من إذن استلام ليس في حالة مسودة");
            if (info.InspectionStatus == 2 || (info.InspectionStatus == 1 && info.InspectionRequestedDate.HasValue))
                throw new InvalidOperationException("تم إرسال الإذن للفحص أو قبوله؛ لا يمكن حذف سطور أثناء هذه المرحلة.");

            await connection.ExecuteAsync(
                "DELETE FROM dbo.GoodsReceiptDetails WHERE GRNDetailID = @ID",
                new { ID = detailId });

            await _audit.WriteAuditLogAsync(userId, 3, "GoodsReceiptDetails", detailId.ToString(),
                moduleName: "SCR_GRN", description: "حذف سطر من إذن استلام");
        }

        // ==========================================
        // تحميل سطور من أمر شراء
        // ==========================================
        public async Task<int> ImportLinesFromPOAsync(int grnId, int purchaseOrderId, List<POLineForGRNDto> lines, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanAdd, "ليس لديك صلاحية تحميل أصناف إلى أذونات الاستلام.");
            if (lines == null || !lines.Any(l => l.IsSelected))
                return 0;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var receiptInfo = await connection.QueryFirstOrDefaultAsync<(int? GRNStatus, int? WarehouseID, int InspectionStatus, DateTime? InspectionRequestedDate)>(
                    @"SELECT GRNStatus, WarehouseID, InspectionStatus, InspectionRequestedDate
                      FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                if (receiptInfo.GRNStatus != 1)
                    throw new Exception("لا يمكن تحميل سطور على إذن استلام ليس في حالة مسودة");
                if (receiptInfo.InspectionStatus == 2 || (receiptInfo.InspectionStatus == 1 && receiptInfo.InspectionRequestedDate.HasValue))
                    throw new InvalidOperationException("تم إرسال الإذن للفحص أو قبوله؛ لا يمكن تحميل سطور جديدة أثناء هذه المرحلة.");
                var defaultWarehouseId = receiptInfo.WarehouseID.GetValueOrDefault();
                if (defaultWarehouseId <= 0)
                    throw new InvalidOperationException("اختر مخزنًا افتراضيًا قبل تحميل سطور أمر الشراء؛ يمكن تعديل مخزن كل سطر بعد التحميل.");

                var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.GoodsReceiptDetails WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                var inserted = 0;

                foreach (var line in lines.Where(l => l.IsSelected && l.QtyToReceive > 0))
                {
                    var exists = await connection.QueryFirstOrDefaultAsync<int>(
                        @"SELECT COUNT(*) FROM dbo.GoodsReceiptDetails
                          WHERE GRNID = @GRNID AND PODetailID = @PODetailID",
                        new { GRNID = grnId, line.PODetailID }, transaction);

                    if (exists > 0) continue;

                    maxLine++;
                    var supplierBatchNumber = string.IsNullOrWhiteSpace(line.BatchNumber)
                        ? null
                        : line.BatchNumber.Trim();

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.GoodsReceiptDetails
                          (
                              GRNID, LineNumber, PODetailID, ItemID, UnitID, WarehouseID,
                              OrderedQty, ReceivedQty, AcceptedQty, RejectedQty,
                              BatchNumber, SupplierBatchNumber, ExpiryDate,
                              UnitCost, LineTotalCost,
                              QualityStatus, Notes
                          )
                          VALUES
                          (
                              @GRNID, @LineNumber, @PODetailID, @ItemID, @UnitID, @WarehouseID,
                              @OrderedQty, @ReceivedQty, NULL, 0,
                              @BatchNumber, @SupplierBatchNumber, @ExpiryDate,
                              @UnitCost, @LineTotalCost,
                              1, NULL
                          )",
                        new
                        {
                            GRNID = grnId,
                            LineNumber = maxLine,
                            line.PODetailID,
                            line.ItemID,
                            line.UnitID,
                            WarehouseID = defaultWarehouseId,
                            OrderedQty = line.RemainingToReceive,
                            ReceivedQty = line.QtyToReceive,
                            BatchNumber = supplierBatchNumber,
                            SupplierBatchNumber = supplierBatchNumber,
                            line.ExpiryDate,
                            UnitCost = line.UnitPrice,
                            LineTotalCost = line.QtyToReceive * line.UnitPrice
                        }, transaction);

                    inserted++;
                }

                transaction.Commit();

                await _audit.WriteAuditLogAsync(userId, 1, "GoodsReceiptDetails", grnId.ToString(),
                    moduleName: "SCR_GRN",
                    description: $"تحميل {inserted} سطر من أمر شراء إلى إذن الاستلام");

                return inserted;
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after importing purchase order {PurchaseOrderID} lines into goods receipt {GoodsReceiptID}.",
                        purchaseOrderId,
                        grnId);
                }
                throw;
            }
        }

        // ==========================================
        // اعتماد إذن الاستلام
        // ==========================================
        public async Task ApproveAsync(int grnId, int userId, int? employeeId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanApprove, "ليس لديك صلاحية اعتماد أذونات الاستلام.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string grnNumber = "";
            int? purchaseOrderId = null;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(
                    string GRNNumber,
                    int GRNStatus,
                    int? PurchaseOrderID,
                    int DetailCount,
                    int InspectionStatus,
                    int? AssignedInspectorID,
                    int? InspectedBy,
                    DateTime? InspectionRequestedDate,
                    DateTime? InspectionCompletedDate)>(
                    @"SELECT
                        grn.GRNNumber, grn.GRNStatus, grn.PurchaseOrderID,
                        (SELECT COUNT(*) FROM dbo.GoodsReceiptDetails d WHERE d.GRNID = grn.GRNID) AS DetailCount,
                        grn.InspectionStatus, grn.AssignedInspectorID, grn.InspectedBy,
                        grn.InspectionRequestedDate, grn.InspectionCompletedDate
                      FROM dbo.GoodsReceiptNotes grn WITH (UPDLOCK, HOLDLOCK)
                      WHERE grn.GRNID = @ID",
                    new { ID = grnId }, transaction);

                if (string.IsNullOrWhiteSpace(info.GRNNumber))
                    throw new Exception("إذن الاستلام غير موجود");

                if (info.GRNStatus != 1)
                    throw new Exception("لا يمكن اعتماد إذن استلام ليس في حالة مسودة");

                if (info.DetailCount == 0)
                    throw new Exception("لا يمكن اعتماد إذن استلام بدون أصناف");
                if (info.InspectionStatus != 2 || !info.InspectionRequestedDate.HasValue ||
                    !info.InspectionCompletedDate.HasValue || !info.AssignedInspectorID.HasValue ||
                    info.AssignedInspectorID != info.InspectedBy)
                    throw new InvalidOperationException("لا يمكن اعتماد الإذن قبل تسجيل نتيجة مقبولة من مسئول الفحص المعيّن.");

                grnNumber = info.GRNNumber;
                purchaseOrderId = info.PurchaseOrderID;

                // QC is completed by the assigned inspector before approval; approval only finalizes and posts.
                var statusUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.GoodsReceiptNotes SET
                        GRNStatus = 2,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE GRNID = @ID AND GRNStatus = 1
                        AND InspectionStatus = 2
                        AND InspectionRequestedDate IS NOT NULL
                        AND InspectionCompletedDate IS NOT NULL
                        AND AssignedInspectorID = InspectedBy",
                    new
                    {
                        ID = grnId,
                        EmployeeID = employeeId,
                        UserID = userId
                    }, transaction);
                if (statusUpdated != 1)
                    throw new InvalidOperationException("تغيرت حالة إذن الاستلام أثناء الاعتماد؛ أُلغيت العملية.");

                // 2) تحديث أمر الشراء (الكميات المستلمة + الحالة)
                if (purchaseOrderId.HasValue && purchaseOrderId.Value > 0)
                {
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_UpdatePOAfterGRN @PurchaseOrderID",
                        new { PurchaseOrderID = purchaseOrderId.Value }, transaction);
                }

                // 3) ترحيل الكمية والتكلفة وبيانات طبقة الباتش لكل سطر مقبول
                var details = await connection.QueryAsync<GoodsReceiptInventoryPostingLine>(
                    @"SELECT
                        grd.GRNDetailID,
                        grn.GRNID,
                        grn.GRNNumber,
                        grn.GRNDate,
                        grd.ItemID,
                        grd.UnitID,
                        grd.AcceptedQty AS AcceptedQty,
                        grd.UnitCost,
                        COALESCE(NULLIF(grd.WarehouseID, 0), NULLIF(grn.WarehouseID, 0)) AS WarehouseID,
                        NULLIF(LTRIM(RTRIM(grd.BatchNumber)), N'') AS BatchNumber,
                        grd.ExpiryDate,
                        grd.ManufactureDate
                      FROM dbo.GoodsReceiptDetails grd
                      INNER JOIN dbo.GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
                      WHERE grd.GRNID = @ID
                        AND grd.AcceptedQty > 0
                      ORDER BY grd.GRNDetailID",
                    new { ID = grnId }, transaction);

                if (details.Any(d => d.WarehouseID <= 0))
                    throw new InvalidOperationException("يوجد سطر استلام بلا مخزن صالح؛ لم يُرحّل المخزون.");

                foreach (var d in details)
                {
                    var postingBatchNumber = d.BatchNumber;
                    if (d.UnitCost > 0 && string.IsNullOrWhiteSpace(postingBatchNumber))
                    {
                        var requiresInternalBatch = await connection.QueryFirstOrDefaultAsync<int>(
                            @"SELECT CASE WHEN
                                  EXISTS
                                  (
                                      SELECT 1 FROM dbo.InventoryBatchBalance
                                      WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID
                                  )
                                  OR EXISTS
                                  (
                                      SELECT 1 FROM dbo.GoodsReceiptDetails
                                      WHERE GRNID = @GRNID AND ItemID = @ItemID AND WarehouseID = @WarehouseID
                                        AND NULLIF(LTRIM(RTRIM(BatchNumber)), N'') IS NOT NULL
                                  )
                                  THEN 1 ELSE 0 END",
                            new { d.ItemID, d.WarehouseID, d.GRNID }, transaction);

                        if (requiresInternalBatch == 1 || d.ExpiryDate.HasValue || d.ManufactureDate.HasValue)
                        {
                            postingBatchNumber = $"INT-GRN-{d.GRNID}-D{d.GRNDetailID}";
                            var batchSaved = await connection.ExecuteAsync(
                                @"UPDATE dbo.GoodsReceiptDetails
                                  SET BatchNumber = @BatchNumber
                                  WHERE GRNID = @GRNID AND GRNDetailID = @GRNDetailID
                                    AND NULLIF(LTRIM(RTRIM(BatchNumber)), N'') IS NULL",
                                new { d.GRNID, d.GRNDetailID, BatchNumber = postingBatchNumber }, transaction);
                            if (batchSaved != 1)
                                throw new InvalidOperationException("تعذر حفظ رقم الباتش الداخلي لهذا السطر.");
                        }
                    }

                    if (d.UnitCost > 0)
                    {
                        await connection.ExecuteAsync(
                            @"EXEC dbo.sp_UpdateItemCostAfterGRN
                                  @ItemID = @ItemID,
                                  @ReceivedQty = @ReceivedQty,
                                  @UnitCost = @UnitCost,
                                  @WarehouseID = @WarehouseID,
                                  @GRNID = @GRNID,
                                  @GRNDetailID = @GRNDetailID,
                                  @GRNNumber = @GRNNumber,
                                  @GRNDate = @GRNDate,
                                  @UnitID = @UnitID,
                                  @BatchNumber = @BatchNumber,
                                  @ExpiryDate = @ExpiryDate,
                                  @ManufactureDate = @ManufactureDate,
                                  @UserID = @UserID",
                            new
                            {
                                d.ItemID,
                                ReceivedQty = d.AcceptedQty,
                                d.UnitCost,
                                d.WarehouseID,
                                d.GRNID,
                                d.GRNDetailID,
                                d.GRNNumber,
                                GRNDate = d.GRNDate.Date,
                                d.UnitID,
                                BatchNumber = postingBatchNumber,
                                ExpiryDate = d.ExpiryDate?.Date,
                                ManufactureDate = d.ManufactureDate?.Date,
                                UserID = userId
                            }, transaction);
                    }
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after goods-receipt approval failed for GRN {GRNID}.",
                        grnId);
                }
                throw;
            }

            // Complete pending QC/approval notifications before creating the unread result message.
            try
            {
                await _notif.MarkRelatedAsActionedAsync("SCR_GRN", grnId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after approving a goods receipt.");
            }

            try
            {
                var createdBy = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT CreatedBy FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = grnId });

                if (createdBy.HasValue)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        notificationType: 1,
                        title: "تم اعتماد إذن الاستلام",
                        message: $"تم اعتماد إذن الاستلام رقم {grnNumber}",
                        priority: 2,
                        targetUserId: createdBy.Value,
                        permissionModuleCode: "SCR_GRN",
                        relatedModule: "SCR_GRN",
                        relatedRecordId: grnId,
                        createdBy: userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not notify the creator after approving a goods receipt.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
                moduleName: "SCR_GRN",
                description: $"اعتماد إذن استلام: {grnNumber}");
        }

        // ==========================================
        // إلغاء إذن الاستلام
        // ==========================================
        public async Task CancelAsync(int grnId, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanApprove, "ليس لديك صلاحية إلغاء إذن الاستلام.");
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(string GRNNumber, int GRNStatus)>(
                "SELECT GRNNumber, GRNStatus FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                new { ID = grnId });

            if (string.IsNullOrWhiteSpace(info.GRNNumber))
                throw new Exception("إذن الاستلام غير موجود");

            if (info.GRNStatus == 3)
                throw new Exception("لا يمكن إلغاء إذن استلام مرتبط بفاتورة");

            if (info.GRNStatus == 4)
                throw new Exception("إذن الاستلام ملغي بالفعل");

            await connection.ExecuteAsync(
                @"UPDATE dbo.GoodsReceiptNotes SET
                    GRNStatus = 4,
                    ModifiedBy = @UserID,
                    ModifiedDate = GETDATE()
                  WHERE GRNID = @ID",
                new { ID = grnId, UserID = userId });

            try
            {
                await _notif.MarkRelatedAsActionedAsync("SCR_GRN", grnId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not complete pending notifications after cancelling a goods receipt.");
            }

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
                moduleName: "SCR_GRN",
                description: $"إلغاء إذن استلام: {info.GRNNumber}");
        }

        // ==========================================
        // حذف إذن استلام (مسودة فقط)
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteReceiptAsync(int grnId, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanDelete, "ليس لديك صلاحية حذف أذونات الاستلام.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string GRNNumber, int GRNStatus)>(
                    "SELECT GRNNumber, GRNStatus FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                if (string.IsNullOrWhiteSpace(info.GRNNumber))
                    return (false, "إذن الاستلام غير موجود");

                if (info.GRNStatus != 1)
                    return (false, "لا يمكن حذف إذن استلام غير مسودة");

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.GoodsReceiptDetails WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(userId, 3, "GoodsReceiptNotes", grnId.ToString(),
                    moduleName: "SCR_GRN",
                    description: $"حذف إذن استلام: {info.GRNNumber}");

                return (true, "تم حذف إذن الاستلام بنجاح");
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after goods-receipt deletion failed for GRN {GRNID}.",
                        grnId);
                }
                return (false, ex.Message);
            }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<GoodsReceiptListDto> receipts, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanExport, "ليس لديك صلاحية تصدير أذونات الاستلام.");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("أذونات الاستلام");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير أذونات الاستلام — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 9).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 9).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[] { "#", "رقم الإذن", "التاريخ", "المورد", "أمر الشراء", "المخزن", "الحالة", "أصناف", "الإجمالي" };
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
            foreach (var grn in receipts)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = grn.GRNNumber ?? "";
                ws.Cell(row, 3).Value = grn.GRNDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = grn.SupplierNameAr ?? "";
                ws.Cell(row, 5).Value = grn.LinkedPONumber ?? "—";
                ws.Cell(row, 6).Value = grn.WarehouseNameAr ?? "—";
                ws.Cell(row, 7).Value = GetStatusName(grn.GRNStatus);
                ws.Cell(row, 8).Value = grn.ItemCount;
                ws.Cell(row, 9).Value = grn.TotalCost;

                for (int i = 1; i <= 9; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e2e8f0");
                }
                if (num % 2 == 0)
                    ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
                row++;
            }

            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "GoodsReceiptNotes",
                moduleName: "SCR_GRN",
                description: $"تصدير {receipts.Count} إذن استلام إلى Excel");

            return stream.ToArray();
        }

        // ==========================================
        // تقرير طباعة
        // ==========================================
        public async Task<string> GenerateReportHtmlAsync(int grnId, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanPrint, "ليس لديك صلاحية طباعة أذونات الاستلام.");
            var header = await GetReceiptByIdAsync(grnId);
            if (header == null) return "<h3>إذن الاستلام غير موجود</h3>";

            var details = await GetReceiptDetailsAsync(grnId);

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
                    <td>{d.WarehouseNameAr ?? "—"}</td>
                    <td style='text-align:center'>{d.ReceivedQty:#,##0.##}</td>
                    <td style='text-align:center'>{d.AcceptedQty?.ToString("#,##0.##") ?? "—"}</td>
                    <td style='text-align:center'>{d.RejectedQty:#,##0.##}</td>
                    <td>{d.BatchNumber ?? "—"}</td>
                    <td>{d.ExpiryDate?.ToString("dd/MM/yyyy") ?? "—"}</td>
                    <td style='text-align:left'>{d.LineTotalCost:#,##0.00}</td>
                </tr>";
            }

            var totalCost = details.Sum(d => d.LineTotalCost);

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'>
<title>إذن استلام — {header.GRNNumber}</title>
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
    <div class='company'>مصنع واي كي كوتينج    </div>
    <div class='doc-title'>إذن استلام بضاعة</div>
    <div class='doc-num'>{header.GRNNumber}</div>
</div>
<div class='info-grid'>
    <div class='info-item'><div class='info-label'>التاريخ</div><div class='info-value'>{header.GRNDate:dd/MM/yyyy}</div></div>
    <div class='info-item'><div class='info-label'>المورد</div><div class='info-value'>{header.SupplierNameAr}</div></div>
    <div class='info-item'><div class='info-label'>أمر الشراء</div><div class='info-value'>{header.LinkedPONumber ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>المخزن المبدئي</div><div class='info-value'>{header.WarehouseNameAr ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>المستلم</div><div class='info-value'>{header.ReceivedByName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>الفاحص</div><div class='info-value'>{header.InspectedByName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>فاتورة المورد</div><div class='info-value'>{header.SupplierInvoiceNo ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(header.GRNStatus)}</div></div>
</div>
<table>
    <thead><tr>
        <th>#</th><th>الكود</th><th>الصنف</th><th>الوحدة</th><th>المخزن</th>
        <th>مستلم</th><th>مقبول</th><th>مرفوض</th>
        <th>الباتش</th><th>الصلاحية</th><th>الإجمالي</th>
    </tr></thead>
    <tbody>
        {detailsHtml}
        <tr class='total-row'>
            <td colspan='10' style='text-align:left'>الإجمالي</td>
            <td style='text-align:left'>{totalCost:#,##0.00} ج.م</td>
        </tr>
    </tbody>
</table>
{(string.IsNullOrWhiteSpace(header.Notes) ? "" : $"<div style='margin-bottom:16px'><strong>ملاحظات:</strong> {header.Notes}</div>")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP v2.0</div>
</body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "GoodsReceiptNotes", grnId.ToString(),
                moduleName: "SCR_GRN",
                description: $"طباعة إذن استلام: {header.GRNNumber}");

            return html;
        }

        // ==========================================
        // Helpers
        // ==========================================
        public static string GetStatusName(int status)
        {
            return status switch
            {
                1 => "مسودة",
                2 => "معتمد",
                3 => "مرتبط بفاتورة",
                4 => "ملغي",
                _ => "غير محدد"
            };
        }

        public static string GetStatusColor(int status)
        {
            return status switch
            {
                1 => "#64748b",
                2 => "#10b981",
                3 => "#3b82f6",
                4 => "#ef4444",
                _ => "#6b7280"
            };
        }

        public static string GetInspectionStatusName(int status)
        {
            return status switch
            {
                1 => "لم يتم الفحص",
                2 => "مقبول",
                3 => "مرفوض",
                _ => "غير محدد"
            };
        }

        public static string GetInspectionColor(int status)
        {
            return status switch
            {
                1 => "#f59e0b",
                2 => "#10b981",
                3 => "#ef4444",
                _ => "#6b7280"
            };
        }

        public static string GetQualityStatusName(int status)
        {
            return status switch
            {
                1 => "انتظار فحص",
                2 => "مقبول",
                3 => "مرفوض",
                _ => "غير محدد"
            };
        }

        public static string GetSourceName(int source)
        {
            return source switch
            {
                1 => "من أمر شراء",
                2 => "استلام مباشر",
                _ => "من أمر شراء"
            };
        }

        public static string GetSourceColor(int source)
        {
            return source switch
            {
                1 => "#0891b2",
                2 => "#1d4ed8",
                _ => "#0891b2"
            };
        }
        // ==========================================
        // إرسال الإذن للفاحص المحدد بعد حفظه
        // ==========================================
        public async Task RequestInspectionAsync(int grnId, int userId)
        {
            await RequireGoodsReceiptPermissionAsync(userId, p => p.CanEdit, "ليس لديك صلاحية إرسال إذن الاستلام للفحص.");
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string grnNumber = "";
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(
                    string? GRNNumber,
                    int? GRNStatus,
                    int InspectionStatus,
                    int? AssignedInspectorID,
                    DateTime? InspectionRequestedDate)>(
                    @"SELECT GRNNumber, GRNStatus, InspectionStatus, AssignedInspectorID, InspectionRequestedDate
                      FROM dbo.GoodsReceiptNotes WITH (UPDLOCK, HOLDLOCK)
                      WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                if (string.IsNullOrWhiteSpace(info.GRNNumber))
                    throw new InvalidOperationException("إذن الاستلام غير موجود.");
                if (info.GRNStatus != 1)
                    throw new InvalidOperationException("لا يمكن إرسال إذن استلام ليس في حالة مسودة للفحص.");
                if (info.InspectionStatus == 2)
                    throw new InvalidOperationException("تم قبول فحص هذا الإذن بالفعل.");
                if (info.InspectionStatus == 1 && info.InspectionRequestedDate.HasValue)
                    throw new InvalidOperationException("الإذن مرسل بالفعل للفحص وينتظر نتيجة الفاحص.");
                if (!info.AssignedInspectorID.HasValue || info.AssignedInspectorID.Value <= 0)
                    throw new InvalidOperationException("حدد مسئول فحص قبل إرسال الإذن.");

                var detailCount = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM dbo.GoodsReceiptDetails WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);
                if (detailCount == 0)
                    throw new InvalidOperationException("أضف أصناف الإذن واحفظها قبل إرساله للفحص.");

                var inspectorUserIds = (await connection.QueryAsync<int>(
                    @"SELECT DISTINCT u.UserID
                      FROM dbo.SystemUsers u
                      INNER JOIN dbo.Employees e ON e.EmployeeID = u.EmployeeID
                      INNER JOIN dbo.vw_UserEffectivePermissions qc
                          ON qc.UserID = u.UserID AND qc.ModuleCode = N'SCR_QC'
                         AND qc.CanView = 1 AND qc.CanApprove = 1
                      INNER JOIN dbo.vw_UserEffectivePermissions grn
                          ON grn.UserID = u.UserID AND grn.ModuleCode = N'SCR_GRN'
                         AND grn.CanView = 1
                      WHERE u.IsActive = 1
                        AND e.IsActive = 1 AND e.EmployeeStatus = 1
                        AND e.EmployeeID = @EmployeeID
                      ORDER BY u.UserID",
                    new { EmployeeID = info.AssignedInspectorID.Value }, transaction)).ToList();

                // إصلاح 19: كان الشرط «حساب واحد بالضبط» فيعطّل الفاحص بصمت متى امتلك
                // حسابًا ثانيًا مؤهلًا؛ الصحيح اشتراط وجود حساب مؤهل واحد على الأقل،
                // ويُنبّه أولها ترتيبًا، وتنفيذ الفحص نفسه يتحقق من الموظف لا من الحساب.
                if (inspectorUserIds.Count == 0)
                    throw new InvalidOperationException(
                        "مسئول الفحص المحدد يجب أن يكون مرتبطًا بحساب مستخدم نشط لديه صلاحية فحص الجودة وعرض إذن الاستلام.");

                if (info.InspectionStatus == 3)
                {
                    await connection.ExecuteAsync(
                        @"UPDATE dbo.GoodsReceiptDetails
                          SET AcceptedQty = NULL, RejectedQty = 0,
                              QualityStatus = 1, QualityNotes = NULL
                          WHERE GRNID = @ID",
                        new { ID = grnId }, transaction);
                }

                var updated = await connection.ExecuteAsync(
                    @"UPDATE dbo.GoodsReceiptNotes
                      SET InspectionStatus = 1,
                          InspectedBy = NULL,
                          InspectionNotes = NULL,
                          InspectionRequestedDate = GETDATE(),
                          InspectionCompletedDate = NULL,
                          ApprovalRequestedDate = NULL,
                          ModifiedBy = @UserID,
                          ModifiedDate = GETDATE()
                      WHERE GRNID = @ID AND GRNStatus = 1
                        AND (InspectionStatus = 1 OR InspectionStatus = 3)",
                    new { ID = grnId, UserID = userId }, transaction);
                if (updated != 1)
                    throw new InvalidOperationException("تغيرت حالة الإذن؛ أُلغيت عملية الإرسال للفحص.");

                await _notif.MarkRelatedAsActionedAsync(connection, transaction, "SCR_GRN", grnId);
                var notificationCreated = await _notif.CreateForAuthorizedUserAsync(
                    connection,
                    transaction,
                    notificationType: 1,
                    title: "إذن استلام بانتظار الفحص",
                    message: $"إذن الاستلام رقم {info.GRNNumber} بانتظار فحصك",
                    priority: 1,
                    targetUserId: inspectorUserIds[0],
                    permissionModuleCode: "SCR_QC",
                    relatedModule: "SCR_GRN",
                    relatedRecordId: grnId,
                    createdBy: userId);
                if (!notificationCreated)
                    throw new InvalidOperationException("تعذر إرسال الإشعار لمسئول الفحص المحدد؛ راجع صلاحياته.");

                transaction.Commit();
                grnNumber = info.GRNNumber;
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after routing goods receipt {GoodsReceiptID} to inspection.", grnId);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
                moduleName: "SCR_GRN",
                description: $"إرسال إذن الاستلام للفاحص المحدد: {grnNumber}");
        }

        // ==========================================
        // تسجيل نتيجة الفحص بواسطة الفاحص المعيّن
        // ==========================================
        public async Task<int> CompleteInspectionAsync(
            int grnId,
            List<GoodsReceiptInspectionLineEditDto> inspectionLines,
            string? inspectionNotes,
            int userId)
        {
            await _authorization.RequireUserPermissionAsync(
                userId, "SCR_QC", p => p.CanApprove, "ليس لديك صلاحية تسجيل نتيجة فحص الجودة.");
            await RequireGoodsReceiptPermissionAsync(
                userId, p => p.CanView, "ليس لديك صلاحية عرض إذن الاستلام.");

            if (inspectionLines == null || inspectionLines.Count == 0)
                throw new InvalidOperationException("لا توجد أصناف لفحصها.");

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            int inspectionStatus;
            string grnNumber;
            int? createdBy;
            int employeeId;
            try
            {
                var currentEmployeeId = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT EmployeeID FROM dbo.SystemUsers
                      WHERE UserID = @UserID AND IsActive = 1",
                    new { UserID = userId }, transaction);
                if (!currentEmployeeId.HasValue || currentEmployeeId.Value <= 0)
                    throw new InvalidOperationException("حسابك غير مرتبط بموظف؛ لا يمكن تسجيل الفحص.");
                employeeId = currentEmployeeId.Value;

                var info = await connection.QueryFirstOrDefaultAsync<(
                    string? GRNNumber,
                    int? GRNStatus,
                    int InspectionStatus,
                    int? AssignedInspectorID,
                    DateTime? InspectionRequestedDate,
                    int? CreatedBy)>(
                    @"SELECT GRNNumber, GRNStatus, InspectionStatus, AssignedInspectorID,
                             InspectionRequestedDate, CreatedBy
                      FROM dbo.GoodsReceiptNotes WITH (UPDLOCK, HOLDLOCK)
                      WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                if (string.IsNullOrWhiteSpace(info.GRNNumber))
                    throw new InvalidOperationException("إذن الاستلام غير موجود.");
                if (info.GRNStatus != 1 || info.InspectionStatus != 1 || !info.InspectionRequestedDate.HasValue)
                    throw new InvalidOperationException("الإذن ليس في مرحلة انتظار الفحص.");
                if (info.AssignedInspectorID != employeeId)
                    throw new UnauthorizedAccessException("هذا الإذن مُسند إلى مسئول فحص آخر.");

                grnNumber = info.GRNNumber;
                createdBy = info.CreatedBy;

                var receiptLines = (await connection.QueryAsync<(int GRNDetailID, decimal ReceivedQty)>(
                    @"SELECT GRNDetailID, ReceivedQty
                      FROM dbo.GoodsReceiptDetails WITH (UPDLOCK, HOLDLOCK)
                      WHERE GRNID = @ID
                      ORDER BY LineNumber",
                    new { ID = grnId }, transaction)).ToList();
                if (receiptLines.Count == 0 || inspectionLines.Count != receiptLines.Count)
                    throw new InvalidOperationException("يجب تسجيل نتيجة الفحص لكل صنف في الإذن.");

                var submittedById = inspectionLines.Select(x => x.GRNDetailID).ToList();
                if (submittedById.Distinct().Count() != submittedById.Count ||
                    !submittedById.OrderBy(x => x).SequenceEqual(receiptLines.Select(x => x.GRNDetailID).OrderBy(x => x)))
                    throw new InvalidOperationException("بيانات الفحص لا تطابق أصناف الإذن الحالية.");

                var submittedLines = inspectionLines.ToDictionary(x => x.GRNDetailID);
                decimal totalAcceptedQty = 0;
                foreach (var receiptLine in receiptLines)
                {
                    var submitted = submittedLines[receiptLine.GRNDetailID];
                    if (!submitted.AcceptedQty.HasValue || submitted.AcceptedQty.Value < 0 ||
                        submitted.AcceptedQty.Value > receiptLine.ReceivedQty)
                        throw new InvalidOperationException("الكمية المقبولة لكل صنف يجب أن تكون بين صفر والكمية المستلمة.");

                    var acceptedQty = submitted.AcceptedQty.Value;
                    var rejectedQty = receiptLine.ReceivedQty - acceptedQty;
                    totalAcceptedQty += acceptedQty;

                    await connection.ExecuteAsync(
                        @"UPDATE dbo.GoodsReceiptDetails
                          SET AcceptedQty = @AcceptedQty,
                              RejectedQty = @RejectedQty,
                              QualityStatus = CASE WHEN @AcceptedQty > 0 THEN 2 ELSE 3 END,
                              QualityNotes = NULLIF(@QualityNotes, N'')
                          WHERE GRNDetailID = @GRNDetailID AND GRNID = @GRNID",
                        new
                        {
                            GRNDetailID = receiptLine.GRNDetailID,
                            GRNID = grnId,
                            AcceptedQty = acceptedQty,
                            RejectedQty = rejectedQty,
                            QualityNotes = submitted.QualityNotes?.Trim()
                        }, transaction);
                }

                inspectionStatus = totalAcceptedQty > 0 ? 2 : 3;
                var headerUpdated = await connection.ExecuteAsync(
                    @"UPDATE dbo.GoodsReceiptNotes
                      SET InspectionStatus = @InspectionStatus,
                          InspectedBy = @EmployeeID,
                          InspectionNotes = NULLIF(@InspectionNotes, N''),
                          InspectionCompletedDate = GETDATE(),
                          ApprovalRequestedDate = CASE WHEN @InspectionStatus = 2 THEN GETDATE() ELSE NULL END,
                          ModifiedBy = @UserID,
                          ModifiedDate = GETDATE()
                      WHERE GRNID = @GRNID AND GRNStatus = 1
                        AND InspectionStatus = 1
                        AND InspectionRequestedDate IS NOT NULL
                        AND AssignedInspectorID = @EmployeeID",
                    new
                    {
                        GRNID = grnId,
                        InspectionStatus = inspectionStatus,
                        EmployeeID = employeeId,
                        InspectionNotes = inspectionNotes?.Trim(),
                        UserID = userId
                    }, transaction);
                if (headerUpdated != 1)
                    throw new InvalidOperationException("تغيرت حالة الإذن أثناء تسجيل الفحص؛ أُلغيت العملية.");

                await _notif.MarkRelatedAsActionedAsync(connection, transaction, "SCR_GRN", grnId);
                if (inspectionStatus == 2)
                {
                    var approvalRecipients = await _notif.CreateForPermissionHoldersAsync(
                        connection,
                        transaction,
                        notificationType: 3,
                        title: "إذن استلام بانتظار الاعتماد",
                        message: $"تم قبول فحص إذن الاستلام رقم {grnNumber}؛ يرجى مراجعته واعتماده وترحيله",
                        priority: 1,
                        permissionModuleCode: "SCR_GRN",
                        requiredPermission: ModulePermissionRequirement.Approve,
                        relatedModule: "SCR_GRN",
                        relatedRecordId: grnId,
                        createdBy: userId);
                    if (approvalRecipients == 0)
                        throw new InvalidOperationException("تم الفحص، لكن لا يوجد مستخدم لديه صلاحية اعتماد أذونات الاستلام لاستلام الإشعار.");
                }
                else if (createdBy.HasValue && createdBy.Value > 0)
                {
                    await _notif.CreateForAuthorizedUserAsync(
                        connection,
                        transaction,
                        notificationType: 1,
                        title: "إذن الاستلام مرفوض بالفحص",
                        message: $"تم رفض أصناف إذن الاستلام رقم {grnNumber} في الفحص؛ راجع الملاحظات وأعد الإرسال بعد التصحيح.",
                        priority: 1,
                        targetUserId: createdBy.Value,
                        permissionModuleCode: "SCR_GRN",
                        relatedModule: "SCR_GRN",
                        relatedRecordId: grnId,
                        createdBy: userId);
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException,
                        "Rollback failed after inspection completion for goods receipt {GoodsReceiptID}.", grnId);
                }
                throw;
            }

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
                moduleName: "SCR_GRN",
                description: inspectionStatus == 2
                    ? $"قبول فحص إذن الاستلام: {grnNumber}"
                    : $"رفض فحص إذن الاستلام: {grnNumber}");

            return inspectionStatus;
        }

    }
}