using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class GoodsReceiptService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public GoodsReceiptService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notif)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        // ==========================================
        // قائمة أذونات الاستلام
        // ==========================================
        public async Task<List<GoodsReceiptListDto>> GetReceiptsListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            GRNID, GRNNumber, GRNDate, GRNStatus, GRNSource, GRNSourceName,
                            SupplierID, SupplierCode, SupplierNameAr,
                            PurchaseOrderID, LinkedPONumber,
                            WarehouseNameAr,
                            InspectionStatus, InspectionStatusName,
                            SupplierInvoiceNo, ReceivedByName,
                            ItemCount, TotalReceivedQty, TotalAcceptedQty, TotalRejectedQty, TotalCost,
                            CreatedDate
                        FROM dbo.vw_GoodsReceiptSummary
                        ORDER BY GRNDate DESC, GRNID DESC";
            var result = await connection.QueryAsync<GoodsReceiptListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب إذن استلام واحد
        // ==========================================
        public async Task<GoodsReceiptHeaderDto?> GetReceiptByIdAsync(int grnId)
        {
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
                            grn.InspectedBy,
                            insp.FullNameAr AS InspectedByName,
                            grn.InspectionStatus,
                            grn.InspectionNotes,

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
                        LEFT JOIN dbo.Employees insp ON grn.InspectedBy = insp.EmployeeID
                        LEFT JOIN dbo.Employees appr ON grn.ApprovedBy = appr.EmployeeID
                        WHERE grn.GRNID = @ID";
            return await connection.QueryFirstOrDefaultAsync<GoodsReceiptHeaderDto>(sql, new { ID = grnId });
        }

        // ==========================================
        // تفاصيل إذن الاستلام
        // ==========================================
        public async Task<List<GoodsReceiptDetailDto>> GetReceiptDetailsAsync(int grnId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT
                            grd.GRNDetailID, grd.GRNID, grd.LineNumber,
                            grd.ItemID, i.ItemCode, i.ItemNameAr,
                            grd.UnitID, u.UnitNameAr AS UnitName,
                            grd.OrderedQty, grd.ReceivedQty, grd.AcceptedQty, grd.RejectedQty,
                            grd.BatchNumber, grd.ExpiryDate, grd.ManufactureDate,
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
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                            EXEC sp_GetNextNumber N'GRN', @NextNum OUTPUT;
                            SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"GRN-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"GRN-{DateTime.Now:yyMMddHHmmss}"; }
        }

        // ==========================================
        // إضافة إذن استلام جديد
        // ==========================================
        public async Task<int> InsertReceiptAsync(GoodsReceiptHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.GoodsReceiptNotes
                        (
                            GRNNumber, GRNDate, PurchaseOrderID, SupplierID, WarehouseID,
                            SupplierInvoiceNo, SupplierInvoiceDate, DeliveryNoteNo,
                            GRNStatus, GRNSource,
                            ReceivedBy, InspectedBy, InspectionStatus, InspectionNotes,
                            Notes, CreatedBy, CreatedDate
                        )
                        VALUES
                        (
                            @GRNNumber, @GRNDate, NULLIF(@PurchaseOrderID, 0), @SupplierID, @WarehouseID,
                            @SupplierInvoiceNo, @SupplierInvoiceDate, @DeliveryNoteNo,
                            @GRNStatus, @GRNSource,
                            NULLIF(@ReceivedBy, 0), NULLIF(@InspectedBy, 0), @InspectionStatus, @InspectionNotes,
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
                GRNStatus = header.GRNStatus == 0 ? 1 : header.GRNStatus,
                GRNSource = header.GRNSource == 0 ? (header.PurchaseOrderID > 0 ? 1 : 2) : header.GRNSource,
                header.ReceivedBy,
                header.InspectedBy,
                InspectionStatus = header.InspectionStatus == 0 ? 1 : header.InspectionStatus,
                header.InspectionNotes,
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
            using var connection = CreateConnection();

            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT GRNStatus FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                new { ID = header.GRNID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل إذن استلام ليس في حالة مسودة");

            var sql = @"UPDATE dbo.GoodsReceiptNotes SET
                            GRNDate = @GRNDate,
                            SupplierID = @SupplierID,
                            WarehouseID = @WarehouseID,
                            SupplierInvoiceNo = @SupplierInvoiceNo,
                            SupplierInvoiceDate = @SupplierInvoiceDate,
                            DeliveryNoteNo = @DeliveryNoteNo,
                            ReceivedBy = NULLIF(@ReceivedBy, 0),
                            InspectedBy = NULLIF(@InspectedBy, 0),
                            InspectionStatus = @InspectionStatus,
                            InspectionNotes = @InspectionNotes,
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
                header.InspectedBy,
                header.InspectionStatus,
                header.InspectionNotes,
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
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT GRNStatus FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = detail.GRNID }, transaction);

                if (status != 1)
                    throw new Exception("لا يمكن تعديل إذن استلام ليس في حالة مسودة");

                if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
                if (detail.UnitID <= 0) throw new Exception("اختر الوحدة");
                if (detail.ReceivedQty <= 0) throw new Exception("أدخل الكمية المستلمة");

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

                if (!detail.AcceptedQty.HasValue)
                    detail.AcceptedQty = detail.ReceivedQty - detail.RejectedQty;

                var sql = @"INSERT INTO dbo.GoodsReceiptDetails
                            (
                                GRNID, LineNumber, PODetailID, ItemID, UnitID,
                                OrderedQty, ReceivedQty, AcceptedQty, RejectedQty,
                                BatchNumber, ExpiryDate, ManufactureDate,
                                UnitCost, LineTotalCost,
                                StorageLocation, QualityStatus, QualityNotes, Notes
                            )
                            VALUES
                            (
                                @GRNID, @LineNumber, @PODetailID, @ItemID, @UnitID,
                                @OrderedQty, @ReceivedQty, @AcceptedQty, @RejectedQty,
                                @BatchNumber, @ExpiryDate, @ManufactureDate,
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
                    detail.OrderedQty,
                    detail.ReceivedQty,
                    detail.AcceptedQty,
                    detail.RejectedQty,
                    detail.BatchNumber,
                    detail.ExpiryDate,
                    detail.ManufactureDate,
                    detail.UnitCost,
                    LineTotalCost = detail.LineTotalCost,
                    detail.StorageLocation,
                    QualityStatus = detail.QualityStatus == 0 ? 1 : detail.QualityStatus,
                    detail.QualityNotes,
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
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // تعديل سطر استلام
        // ==========================================
        public async Task UpdateDetailAsync(GoodsReceiptDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();

            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                @"SELECT grn.GRNStatus
                  FROM dbo.GoodsReceiptNotes grn
                  INNER JOIN dbo.GoodsReceiptDetails grd ON grn.GRNID = grd.GRNID
                  WHERE grd.GRNDetailID = @ID",
                new { ID = detail.GRNDetailID });

            if (status != 1)
                throw new Exception("لا يمكن تعديل سطر في إذن استلام ليس في حالة مسودة");

            if (!detail.AcceptedQty.HasValue)
                detail.AcceptedQty = detail.ReceivedQty - detail.RejectedQty;

            var sql = @"UPDATE dbo.GoodsReceiptDetails SET
                            ItemID = @ItemID, UnitID = @UnitID,
                            OrderedQty = @OrderedQty,
                            ReceivedQty = @ReceivedQty,
                            AcceptedQty = @AcceptedQty,
                            RejectedQty = @RejectedQty,
                            BatchNumber = @BatchNumber,
                            ExpiryDate = @ExpiryDate,
                            ManufactureDate = @ManufactureDate,
                            UnitCost = @UnitCost,
                            LineTotalCost = @LineTotalCost,
                            StorageLocation = @StorageLocation,
                            QualityStatus = @QualityStatus,
                            QualityNotes = @QualityNotes,
                            Notes = @Notes
                        WHERE GRNDetailID = @GRNDetailID";

            await connection.ExecuteAsync(sql, new
            {
                detail.GRNDetailID,
                detail.ItemID,
                detail.UnitID,
                detail.OrderedQty,
                detail.ReceivedQty,
                detail.AcceptedQty,
                detail.RejectedQty,
                detail.BatchNumber,
                detail.ExpiryDate,
                detail.ManufactureDate,
                detail.UnitCost,
                LineTotalCost = detail.LineTotalCost,
                detail.StorageLocation,
                detail.QualityStatus,
                detail.QualityNotes,
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
            using var connection = CreateConnection();

            var info = await connection.QueryFirstOrDefaultAsync<(int GRNID, int GRNStatus)>(
                @"SELECT grd.GRNID, grn.GRNStatus
                  FROM dbo.GoodsReceiptDetails grd
                  INNER JOIN dbo.GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
                  WHERE grd.GRNDetailID = @ID",
                new { ID = detailId });

            if (info.GRNID == 0) throw new Exception("السطر غير موجود");
            if (info.GRNStatus != 1) throw new Exception("لا يمكن حذف سطر من إذن استلام ليس في حالة مسودة");

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
            if (lines == null || !lines.Any(l => l.IsSelected))
                return 0;

            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var status = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT GRNStatus FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = grnId }, transaction);

                if (status != 1)
                    throw new Exception("لا يمكن تحميل سطور على إذن استلام ليس في حالة مسودة");

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

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.GoodsReceiptDetails
                          (
                              GRNID, LineNumber, PODetailID, ItemID, UnitID,
                              OrderedQty, ReceivedQty, AcceptedQty, RejectedQty,
                              BatchNumber, ExpiryDate,
                              UnitCost, LineTotalCost,
                              QualityStatus, Notes
                          )
                          VALUES
                          (
                              @GRNID, @LineNumber, @PODetailID, @ItemID, @UnitID,
                              @OrderedQty, @ReceivedQty, @ReceivedQty, 0,
                              @BatchNumber, @ExpiryDate,
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
                            OrderedQty = line.RemainingToReceive,
                            ReceivedQty = line.QtyToReceive,
                            line.BatchNumber,
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
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // اعتماد إذن الاستلام
        // ==========================================
        public async Task ApproveAsync(int grnId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            string grnNumber = "";
            int? purchaseOrderId = null;

            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string GRNNumber, int GRNStatus, int? PurchaseOrderID, int DetailCount)>(
                    @"SELECT
                        grn.GRNNumber, grn.GRNStatus, grn.PurchaseOrderID,
                        (SELECT COUNT(*) FROM dbo.GoodsReceiptDetails d WHERE d.GRNID = grn.GRNID) AS DetailCount
                      FROM dbo.GoodsReceiptNotes grn
                      WHERE grn.GRNID = @ID",
                    new { ID = grnId }, transaction);

                if (string.IsNullOrWhiteSpace(info.GRNNumber))
                    throw new Exception("إذن الاستلام غير موجود");

                if (info.GRNStatus != 1)
                    throw new Exception("لا يمكن اعتماد إذن استلام ليس في حالة مسودة");

                if (info.DetailCount == 0)
                    throw new Exception("لا يمكن اعتماد إذن استلام بدون أصناف");

                grnNumber = info.GRNNumber;
                purchaseOrderId = info.PurchaseOrderID;

                // 1) تحديث حالة GRN
                await connection.ExecuteAsync(
                    @"UPDATE dbo.GoodsReceiptNotes SET
                        GRNStatus = 2,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE GRNID = @ID",
                    new { ID = grnId, EmployeeID = employeeId, UserID = userId }, transaction);

                // 2) تحديث أمر الشراء (الكميات المستلمة + الحالة)
                if (purchaseOrderId.HasValue && purchaseOrderId.Value > 0)
                {
                    await connection.ExecuteAsync(
                        "EXEC dbo.sp_UpdatePOAfterGRN @PurchaseOrderID",
                        new { PurchaseOrderID = purchaseOrderId.Value }, transaction);
                }

                // 3) تحديث تكلفة الأصناف
                var details = await connection.QueryAsync<(int ItemID, decimal AcceptedQty, decimal UnitCost, int WarehouseID)>(
                    @"SELECT
                        grd.ItemID,
                        ISNULL(grd.AcceptedQty, grd.ReceivedQty) AS AcceptedQty,
                        grd.UnitCost,
                        grn.WarehouseID
                      FROM dbo.GoodsReceiptDetails grd
                      INNER JOIN dbo.GoodsReceiptNotes grn ON grd.GRNID = grn.GRNID
                      WHERE grd.GRNID = @ID
                        AND ISNULL(grd.AcceptedQty, grd.ReceivedQty) > 0",
                    new { ID = grnId }, transaction);

                foreach (var d in details)
                {
                    if (d.UnitCost > 0)
                    {
                        await connection.ExecuteAsync(
                            "EXEC dbo.sp_UpdateItemCostAfterGRN @ItemID, @ReceivedQty, @UnitCost, @WarehouseID",
                            new
                            {
                                d.ItemID,
                                ReceivedQty = d.AcceptedQty,
                                d.UnitCost,
                                d.WarehouseID
                            }, transaction);
                    }
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            // إشعار
            try
            {
                var createdBy = await connection.QueryFirstOrDefaultAsync<int?>(
                    "SELECT CreatedBy FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
                    new { ID = grnId });

                if (createdBy.HasValue)
                {
                    await _notif.CreateNotificationAsync(
                        notificationType: 1,
                        title: "تم اعتماد إذن الاستلام ✅",
                        message: $"تم اعتماد إذن الاستلام رقم {grnNumber}",
                        priority: 2,
                        targetUserId: createdBy.Value,
                        relatedModule: "SCR_GRN",
                        relatedRecordId: grnId,
                        createdBy: userId);
                }
            }
            catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
                moduleName: "SCR_GRN",
                description: $"اعتماد إذن استلام: {grnNumber}");
        }

        // ==========================================
        // إلغاء إذن الاستلام
        // ==========================================
        public async Task CancelAsync(int grnId, int userId)
        {
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

            await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
                moduleName: "SCR_GRN",
                description: $"إلغاء إذن استلام: {info.GRNNumber}");
        }

        // ==========================================
        // حذف إذن استلام (مسودة فقط)
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteReceiptAsync(int grnId, int userId)
        {
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
                transaction.Rollback();
                return (false, ex.Message);
            }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<GoodsReceiptListDto> receipts, int userId)
        {
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
    <div class='company'>مصنع واي كي كوتينج لمستحضرات التجميل</div>
    <div class='doc-title'>إذن استلام بضاعة</div>
    <div class='doc-num'>{header.GRNNumber}</div>
</div>
<div class='info-grid'>
    <div class='info-item'><div class='info-label'>التاريخ</div><div class='info-value'>{header.GRNDate:dd/MM/yyyy}</div></div>
    <div class='info-item'><div class='info-label'>المورد</div><div class='info-value'>{header.SupplierNameAr}</div></div>
    <div class='info-item'><div class='info-label'>أمر الشراء</div><div class='info-value'>{header.LinkedPONumber ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>المخزن</div><div class='info-value'>{header.WarehouseNameAr ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>المستلم</div><div class='info-value'>{header.ReceivedByName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>الفاحص</div><div class='info-value'>{header.InspectedByName ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>فاتورة المورد</div><div class='info-value'>{header.SupplierInvoiceNo ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(header.GRNStatus)}</div></div>
</div>
<table>
    <thead><tr>
        <th>#</th><th>الكود</th><th>الصنف</th><th>الوحدة</th>
        <th>مستلم</th><th>مقبول</th><th>مرفوض</th>
        <th>الباتش</th><th>الصلاحية</th><th>الإجمالي</th>
    </tr></thead>
    <tbody>
        {detailsHtml}
        <tr class='total-row'>
            <td colspan='9' style='text-align:left'>الإجمالي</td>
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
// إرسال إشعار فحص الجودة
// ==========================================
public async Task RequestInspectionAsync(int grnId, int userId)
{
    using var connection = CreateConnection();

    var info = await connection.QueryFirstOrDefaultAsync<(string GRNNumber, int GRNStatus)>(
        "SELECT GRNNumber, GRNStatus FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
        new { ID = grnId });

    if (string.IsNullOrWhiteSpace(info.GRNNumber)) return;

    try
    {
        // إشعار لمسؤولي الجودة
        var qcRoles = await connection.QueryAsync<int>(
            @"SELECT DISTINCT r.RoleID
              FROM dbo.UserRoles r
              INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
              INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
              WHERE sm.ModuleCode = N'SCR_QC' AND rp.CanView = 1");

        foreach (var roleId in qcRoles)
        {
            await _notif.CreateNotificationAsync(
                notificationType: 1,
                title: "إذن استلام بانتظار الفحص 🔍",
                message: $"إذن الاستلام رقم {info.GRNNumber} بانتظار فحص الجودة",
                priority: 1,
                targetUserId: null,
                targetRoleId: roleId,
                relatedModule: "SCR_GRN",
                relatedRecordId: grnId,
                createdBy: userId);
        }

        // لو مافيش QC Role، نبعت لأمناء المخازن
        if (!qcRoles.Any())
        {
            var whRoles = await connection.QueryAsync<int>(
                @"SELECT DISTINCT r.RoleID
                  FROM dbo.UserRoles r
                  INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
                  INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
                  WHERE sm.ModuleCode = N'SCR_GRN' AND rp.CanApprove = 1");

            foreach (var roleId in whRoles)
            {
                await _notif.CreateNotificationAsync(
                    notificationType: 1,
                    title: "إذن استلام بانتظار الفحص 🔍",
                    message: $"إذن الاستلام رقم {info.GRNNumber} بانتظار الفحص والاعتماد",
                    priority: 1,
                    targetUserId: null,
                    targetRoleId: roleId,
                    relatedModule: "SCR_GRN",
                    relatedRecordId: grnId,
                    createdBy: userId);
            }
        }
    }
    catch { }

    await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
        moduleName: "SCR_GRN",
        description: $"طلب فحص جودة لإذن استلام: {info.GRNNumber}");
}

// ==========================================
// إرسال إشعار طلب اعتماد بعد الفحص
// ==========================================
public async Task RequestApprovalAsync(int grnId, int userId)
{
    using var connection = CreateConnection();

    var info = await connection.QueryFirstOrDefaultAsync<(string GRNNumber, int GRNStatus, int InspectionStatus)>(
        "SELECT GRNNumber, GRNStatus, InspectionStatus FROM dbo.GoodsReceiptNotes WHERE GRNID = @ID",
        new { ID = grnId });

    if (string.IsNullOrWhiteSpace(info.GRNNumber)) return;
    if (info.GRNStatus != 1) return;

    try
    {
        var approverRoles = await connection.QueryAsync<int>(
            @"SELECT DISTINCT r.RoleID
              FROM dbo.UserRoles r
              INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
              INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
              WHERE sm.ModuleCode = N'SCR_GRN' AND rp.CanApprove = 1");

        var inspectionText = info.InspectionStatus switch
        {
            2 => "✅ الفحص: مقبول",
            3 => "❌ الفحص: مرفوض",
            _ => "⏳ الفحص: لم يتم"
        };

        foreach (var roleId in approverRoles)
        {
            await _notif.CreateNotificationAsync(
                notificationType: 3,
                title: "إذن استلام بانتظار الاعتماد 📦",
                message: $"إذن الاستلام رقم {info.GRNNumber} بانتظار الاعتماد\n{inspectionText}",
                priority: 1,
                targetUserId: null,
                targetRoleId: roleId,
                relatedModule: "SCR_GRN",
                relatedRecordId: grnId,
                createdBy: userId);
        }
    }
    catch { }

    await _audit.WriteAuditLogAsync(userId, 2, "GoodsReceiptNotes", grnId.ToString(),
        moduleName: "SCR_GRN",
        description: $"طلب اعتماد إذن استلام: {info.GRNNumber}");
}
    }
}