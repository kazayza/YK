using Dapper;
using ClosedXML.Excel;
using YKCoatings.Models;
using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;

namespace YKCoatings.Services
{
    public class InventoryService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public InventoryService(IConfiguration configuration, AuditService audit, NotificationService notif)
            : base(configuration) { _audit = audit; _notif = notif; }

        // ==========================================
        // إحصائيات المخزون
        // ==========================================
        public async Task<InventoryDashboardDto> GetDashboardAsync()
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<InventoryDashboardDto>(
                "SELECT * FROM dbo.vw_InventoryDashboard") ?? new InventoryDashboardDto();
        }

        // ==========================================
        // أرصدة المخزون
        // ==========================================
        public async Task<List<StockBalanceDto>> GetStockBalancesAsync(int? warehouseId = null, int? itemType = null, int? alertLevel = null)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT * FROM dbo.vw_StockBalances
                        WHERE (@WarehouseID IS NULL OR WarehouseID = @WarehouseID)
                          AND (@ItemType IS NULL OR ItemType = @ItemType)
                          AND (@AlertLevel IS NULL OR StockAlert >= @AlertLevel)
                        ORDER BY StockAlert DESC, ItemNameAr";
            return (await connection.QueryAsync<StockBalanceDto>(sql,
                new { WarehouseID = warehouseId, ItemType = itemType, AlertLevel = alertLevel })).ToList();
        }

        // ==========================================
        // أرصدة الباتشات
        // ==========================================
        public async Task<List<BatchBalanceDto>> GetBatchBalancesAsync(int? itemId = null, int? warehouseId = null, int? expiryAlert = null)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT * FROM dbo.vw_BatchBalances
                        WHERE (@ItemID IS NULL OR ItemID = @ItemID)
                          AND (@WarehouseID IS NULL OR WarehouseID = @WarehouseID)
                          AND (@ExpiryAlert IS NULL OR ExpiryAlert >= @ExpiryAlert)
                        ORDER BY ExpiryAlert DESC, ExpiryDate ASC";
            return (await connection.QueryAsync<BatchBalanceDto>(sql,
                new { ItemID = itemId, WarehouseID = warehouseId, ExpiryAlert = expiryAlert })).ToList();
        }

        // ==========================================
        // حركات المخزون
        // ==========================================
        public async Task<List<StockMovementDto>> GetMovementsAsync(
            int? itemId = null, int? warehouseId = null, int? transType = null,
            DateTime? fromDate = null, DateTime? toDate = null)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT * FROM dbo.vw_StockMovements
                        WHERE (@ItemID IS NULL OR ItemID = @ItemID)
                          AND (@WarehouseID IS NULL OR WarehouseID = @WarehouseID)
                          AND (@TransType IS NULL OR TransactionType = @TransType)
                          AND (@FromDate IS NULL OR CAST(TransactionDate AS DATE) >= @FromDate)
                          AND (@ToDate IS NULL OR CAST(TransactionDate AS DATE) <= @ToDate)
                        ORDER BY TransactionDate DESC, TransactionID DESC";
            return (await connection.QueryAsync<StockMovementDto>(sql,
                new { ItemID = itemId, WarehouseID = warehouseId, TransType = transType, FromDate = fromDate, ToDate = toDate })).ToList();
        }

        // ==========================================
        // كارت صنف
        // ==========================================
        public async Task<List<StockMovementDto>> GetItemCardAsync(int itemId, int? warehouseId = null)
        {
            return await GetMovementsAsync(itemId: itemId, warehouseId: warehouseId);
        }

        // ==========================================
        // تحويلات المخزون - القائمة
        // ==========================================
        public async Task<List<StockTransferListDto>> GetTransfersListAsync()
        {
            using var connection = CreateConnection();
            return (await connection.QueryAsync<StockTransferListDto>(
                "SELECT * FROM dbo.vw_StockTransferSummary ORDER BY TransferDate DESC, TransferID DESC")).ToList();
        }

        public async Task<StockTransferHeaderDto?> GetTransferByIdAsync(int transferId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT st.TransferID, st.TransferNumber, st.TransferDate, st.TransferStatus,
                            st.FromWarehouseID, fw.WarehouseNameAr AS FromWarehouseName,
                            st.ToWarehouseID, tw.WarehouseNameAr AS ToWarehouseName,
                            st.TransferReason, st.RequestedBy, req.FullNameAr AS RequestedByName,
                            st.ApprovedBy, st.ApprovedDate, st.Notes, st.CreatedBy, st.CreatedDate
                        FROM dbo.StockTransfers st
                        INNER JOIN dbo.Warehouses fw ON st.FromWarehouseID = fw.WarehouseID
                        INNER JOIN dbo.Warehouses tw ON st.ToWarehouseID = tw.WarehouseID
                        LEFT JOIN dbo.Employees req ON st.RequestedBy = req.EmployeeID
                        WHERE st.TransferID = @ID";
            return await connection.QueryFirstOrDefaultAsync<StockTransferHeaderDto>(sql, new { ID = transferId });
        }

        public async Task<List<StockTransferDetailDto>> GetTransferDetailsAsync(int transferId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT std.TransferDetailID, std.TransferID, std.LineNumber,
                            std.ItemID, i.ItemCode, i.ItemNameAr,
                            std.UnitID, u.UnitNameAr AS UnitName,
                            std.Quantity, std.UnitCost, std.BatchNumber, std.ExpiryDate, std.Notes,
                            ISNULL(ib.AvailableQty, 0) AS AvailableQty
                        FROM dbo.StockTransferDetails std
                        INNER JOIN dbo.Items i ON std.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON std.UnitID = u.UnitID
                        LEFT JOIN dbo.InventoryBalance ib ON std.ItemID = ib.ItemID
                            AND ib.WarehouseID = (SELECT FromWarehouseID FROM dbo.StockTransfers WHERE TransferID = std.TransferID)
                        WHERE std.TransferID = @ID ORDER BY std.LineNumber";
            return (await connection.QueryAsync<StockTransferDetailDto>(sql, new { ID = transferId })).ToList();
        }

        public async Task<string> GenerateTransferNumberAsync()
        {
            using var connection = CreateConnection();
            try { return await connection.QueryFirstOrDefaultAsync<string>(
                "DECLARE @N NVARCHAR(50); EXEC sp_GetNextNumber N'ST', @N OUTPUT; SELECT @N;") ?? $"ST-{DateTime.Now:yyMMddHHmmss}"; }
            catch { return $"ST-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertTransferAsync(StockTransferHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var newId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.StockTransfers (TransferNumber, TransferDate, FromWarehouseID, ToWarehouseID,
                    TransferReason, TransferStatus, RequestedBy, Notes, CreatedBy, CreatedDate)
                  VALUES (@TransferNumber, @TransferDate, @FromWarehouseID, @ToWarehouseID,
                    @TransferReason, 1, NULLIF(@RequestedBy, 0), @Notes, @UserID, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { header.TransferNumber, header.TransferDate, header.FromWarehouseID, header.ToWarehouseID,
                    header.TransferReason, header.RequestedBy, header.Notes, UserID = userId });
            await _audit.WriteAuditLogAsync(userId, 1, "StockTransfers", newId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"إنشاء تحويل مخزني: {header.TransferNumber}");
            return newId;
        }

        public async Task UpdateTransferAsync(StockTransferHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var status = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT TransferStatus FROM dbo.StockTransfers WHERE TransferID = @ID", new { ID = header.TransferID });
            if (status != 1) throw new Exception("لا يمكن تعديل تحويل ليس في حالة مسودة");
            await connection.ExecuteAsync(
                @"UPDATE dbo.StockTransfers SET TransferDate=@TransferDate, FromWarehouseID=@FromWarehouseID,
                    ToWarehouseID=@ToWarehouseID, TransferReason=@TransferReason, Notes=@Notes,
                    ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE TransferID=@TransferID",
                new { header.TransferID, header.TransferDate, header.FromWarehouseID, header.ToWarehouseID,
                    header.TransferReason, header.Notes, UserID = userId });
            await _audit.WriteAuditLogAsync(userId, 2, "StockTransfers", header.TransferID.ToString(),
                moduleName: "SCR_TRANSFER", description: $"تعديل تحويل: {header.TransferNumber}");
        }

        public async Task<int> InsertTransferDetailAsync(StockTransferDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            if (detail.ItemID <= 0) throw new Exception("اختر الصنف");
            if (detail.Quantity <= 0) throw new Exception("أدخل الكمية");

            var available = await connection.QueryFirstOrDefaultAsync<decimal>(
                @"SELECT ISNULL(ib.AvailableQty, 0) FROM dbo.InventoryBalance ib
                  INNER JOIN dbo.StockTransfers st ON ib.WarehouseID = st.FromWarehouseID
                  WHERE ib.ItemID = @ItemID AND st.TransferID = @TransferID",
                new { detail.ItemID, detail.TransferID });
            if (detail.Quantity > available)
                throw new Exception($"الكمية المطلوبة ({detail.Quantity:#,##0.##}) أكبر من المتاح ({available:#,##0.##})");

            var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT ISNULL(MAX(LineNumber),0) FROM dbo.StockTransferDetails WHERE TransferID=@ID", new { ID = detail.TransferID });
            detail.LineNumber = maxLine + 1;

            var avgCost = await connection.QueryFirstOrDefaultAsync<decimal>(
                "SELECT ISNULL(AverageCost, 0) FROM dbo.Items WHERE ItemID = @ID", new { ID = detail.ItemID });

            var newId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.StockTransferDetails (TransferID, LineNumber, ItemID, UnitID, Quantity, UnitCost, BatchNumber, ExpiryDate, Notes)
                  VALUES (@TransferID, @LineNumber, @ItemID, @UnitID, @Quantity, @UnitCost, @BatchNumber, @ExpiryDate, @Notes);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { detail.TransferID, detail.LineNumber, detail.ItemID, detail.UnitID, detail.Quantity,
                    UnitCost = detail.UnitCost > 0 ? detail.UnitCost : avgCost,
                    detail.BatchNumber, detail.ExpiryDate, detail.Notes });
            await _audit.WriteAuditLogAsync(userId, 1, "StockTransferDetails", newId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"إضافة سطر في تحويل رقم {detail.TransferID}");
            return newId;
        }

        public async Task DeleteTransferDetailAsync(int detailId, int userId)
        {
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(int TransferID, int Status)>(
                @"SELECT std.TransferID, st.TransferStatus FROM dbo.StockTransferDetails std
                  INNER JOIN dbo.StockTransfers st ON std.TransferID=st.TransferID WHERE std.TransferDetailID=@ID",
                new { ID = detailId });
            if (info.Status != 1) throw new Exception("لا يمكن حذف سطر من تحويل ليس مسودة");
            await connection.ExecuteAsync("DELETE FROM dbo.StockTransferDetails WHERE TransferDetailID=@ID", new { ID = detailId });
            await _audit.WriteAuditLogAsync(userId, 3, "StockTransferDetails", detailId.ToString(),
                moduleName: "SCR_TRANSFER", description: "حذف سطر من تحويل مخزني");
        }

        public async Task ApproveAndExecuteTransferAsync(int transferId, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(string TransferNumber, int Status, int DetailCount)>(
                @"SELECT TransferNumber, TransferStatus,
                    (SELECT COUNT(*) FROM dbo.StockTransferDetails WHERE TransferID=st.TransferID)
                  FROM dbo.StockTransfers st WHERE TransferID=@ID", new { ID = transferId });
            if (string.IsNullOrWhiteSpace(info.TransferNumber)) throw new Exception("التحويل غير موجود");
            if (info.Status != 1) throw new Exception("لا يمكن اعتماد تحويل ليس مسودة");
            if (info.DetailCount == 0) throw new Exception("لا يمكن اعتماد تحويل بدون أصناف");

            await connection.ExecuteAsync(
                @"UPDATE dbo.StockTransfers SET TransferStatus=2, ApprovedBy=@EmpID, ApprovedDate=GETDATE(),
                    ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE TransferID=@ID",
                new { ID = transferId, EmpID = employeeId, UserID = userId });

            await connection.ExecuteAsync("EXEC dbo.sp_ExecuteStockTransfer @TransferID, @UserID",
                new { TransferID = transferId, UserID = userId });

            try
            {
                var roles = await connection.QueryAsync<int>(
                    @"SELECT DISTINCT r.RoleID FROM dbo.UserRoles r
                      INNER JOIN dbo.RolePermissions rp ON r.RoleID=rp.RoleID
                      INNER JOIN dbo.SystemModules sm ON rp.ModuleID=sm.ModuleID
                      WHERE sm.ModuleCode=N'SCR_TRANSFER' AND rp.CanView=1");
                foreach (var roleId in roles)
                    await _notif.CreateNotificationAsync(1, "تم تنفيذ تحويل مخزني ✅",
                        $"تم تنفيذ التحويل المخزني رقم {info.TransferNumber}", 2, null, roleId,
                        "SCR_TRANSFER", transferId, userId);
            } catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "StockTransfers", transferId.ToString(),
                moduleName: "SCR_TRANSFER", description: $"اعتماد وتنفيذ تحويل: {info.TransferNumber}");
        }

        public async Task<(bool Success, string Message)> DeleteTransferAsync(int transferId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string TransferNumber, int Status)>(
                    "SELECT TransferNumber, TransferStatus FROM dbo.StockTransfers WHERE TransferID=@ID",
                    new { ID = transferId }, transaction);
                if (string.IsNullOrWhiteSpace(info.TransferNumber)) return (false, "التحويل غير موجود");
                if (info.Status != 1) return (false, "لا يمكن حذف تحويل غير مسودة");
                await connection.ExecuteAsync("DELETE FROM dbo.StockTransferDetails WHERE TransferID=@ID", new { ID = transferId }, transaction);
                await connection.ExecuteAsync("DELETE FROM dbo.StockTransfers WHERE TransferID=@ID", new { ID = transferId }, transaction);
                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 3, "StockTransfers", transferId.ToString(),
                    moduleName: "SCR_TRANSFER", description: $"حذف تحويل: {info.TransferNumber}");
                return (true, "تم الحذف بنجاح");
            }
            catch (Exception ex) { transaction.Rollback(); return (false, ex.Message); }
        }

        // ==========================================
        // الجرد
        // ==========================================
        public async Task<List<StockCountListDto>> GetCountsListAsync()
        {
            using var connection = CreateConnection();
            return (await connection.QueryAsync<StockCountListDto>(
                "SELECT * FROM dbo.vw_StockCountSummary ORDER BY CountDate DESC, CountID DESC")).ToList();
        }

        public async Task<StockCountHeaderDto?> GetCountByIdAsync(int countId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT sc.CountID, sc.CountNumber, sc.CountDate, sc.CountStatus, sc.CountType,
                            sc.WarehouseID, w.WarehouseNameAr, sc.CategoryID, cat.CategoryNameAr,
                            sc.CountedBy, sc.SupervisorID, sc.ApprovedBy, sc.ApprovedDate,
                            sc.Notes, sc.CreatedBy, sc.CreatedDate
                        FROM dbo.StockCounts sc
                        INNER JOIN dbo.Warehouses w ON sc.WarehouseID=w.WarehouseID
                        LEFT JOIN dbo.ItemCategories cat ON sc.CategoryID=cat.CategoryID
                        WHERE sc.CountID=@ID";
            return await connection.QueryFirstOrDefaultAsync<StockCountHeaderDto>(sql, new { ID = countId });
        }

        public async Task<List<StockCountDetailDto>> GetCountDetailsAsync(int countId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT scd.CountDetailID, scd.CountID, scd.LineNumber,
                            scd.ItemID, i.ItemCode, i.ItemNameAr,
                            scd.UnitID, u.UnitNameAr AS UnitName,
                            scd.SystemQty, scd.CountedQty,
                            ISNULL(scd.CountedQty, 0) - ISNULL(scd.SystemQty, 0) AS Variance,
                            (ISNULL(scd.CountedQty, 0) - ISNULL(scd.SystemQty, 0)) * ISNULL(scd.UnitCost, 0) AS VarianceValue,
                            scd.UnitCost, scd.BatchNumber, scd.AdjustmentApproved, scd.Notes
                        FROM dbo.StockCountDetails scd
                        INNER JOIN dbo.Items i ON scd.ItemID=i.ItemID
                        INNER JOIN dbo.Units u ON scd.UnitID=u.UnitID
                        WHERE scd.CountID=@ID ORDER BY scd.LineNumber";
            return (await connection.QueryAsync<StockCountDetailDto>(sql, new { ID = countId })).ToList();
        }

        public async Task<string> GenerateCountNumberAsync()
        {
            using var connection = CreateConnection();
            try { return await connection.QueryFirstOrDefaultAsync<string>(
                "DECLARE @N NVARCHAR(50); EXEC sp_GetNextNumber N'SC', @N OUTPUT; SELECT @N;") ?? $"SC-{DateTime.Now:yyMMddHHmmss}"; }
            catch { return $"SC-{DateTime.Now:yyMMddHHmmss}"; }
        }

        public async Task<int> InsertCountAsync(StockCountHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var newId = await connection.ExecuteScalarAsync<int>(
                @"INSERT INTO dbo.StockCounts (CountNumber, CountDate, WarehouseID, CountType, CategoryID,
                    CountStatus, CountedBy, SupervisorID, Notes, CreatedBy, CreatedDate)
                  VALUES (@CountNumber, @CountDate, @WarehouseID, @CountType, NULLIF(@CategoryID, 0),
                    1, NULLIF(@CountedBy, 0), NULLIF(@SupervisorID, 0), @Notes, @UserID, GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { header.CountNumber, header.CountDate, header.WarehouseID,
                    CountType = header.CountType == 0 ? 1 : header.CountType,
                    header.CategoryID, header.CountedBy, header.SupervisorID, header.Notes, UserID = userId });

            await connection.ExecuteAsync("EXEC dbo.sp_LoadItemsForStockCount @CountID, @WarehouseID, @CategoryID",
                new { CountID = newId, header.WarehouseID, header.CategoryID });

            await _audit.WriteAuditLogAsync(userId, 1, "StockCounts", newId.ToString(),
                moduleName: "SCR_COUNT", description: $"إنشاء أمر جرد: {header.CountNumber}");
            return newId;
        }

        public async Task UpdateCountedQtyAsync(int countDetailId, decimal? countedQty, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE dbo.StockCountDetails SET CountedQty=@Qty WHERE CountDetailID=@ID",
                new { ID = countDetailId, Qty = countedQty });
        }

        public async Task UpdateCountedQtiesBulkAsync(int countId, List<(int DetailID, decimal? CountedQty)> items, int userId)
        {
            using var connection = CreateConnection();
            foreach (var item in items)
                await connection.ExecuteAsync(
                    "UPDATE dbo.StockCountDetails SET CountedQty=@Qty WHERE CountDetailID=@ID AND CountID=@CountID",
                    new { ID = item.DetailID, Qty = item.CountedQty, CountID = countId });

            await connection.ExecuteAsync(
                "UPDATE dbo.StockCounts SET CountStatus=3, ModifiedBy=@UserID, ModifiedDate=GETDATE() WHERE CountID=@ID AND CountStatus IN (1,2)",
                new { ID = countId, UserID = userId });

            await _audit.WriteAuditLogAsync(userId, 2, "StockCounts", countId.ToString(),
                moduleName: "SCR_COUNT", description: "تحديث كميات الجرد الفعلية");
        }

        public async Task ApproveAdjustmentsAsync(int countId, List<int> approvedDetailIds, int userId, int? employeeId = null)
        {
            using var connection = CreateConnection();
            var info = await connection.QueryFirstOrDefaultAsync<(string CountNumber, int Status)>(
                "SELECT CountNumber, CountStatus FROM dbo.StockCounts WHERE CountID=@ID", new { ID = countId });
            if (string.IsNullOrWhiteSpace(info.CountNumber)) throw new Exception("أمر الجرد غير موجود");
            if (info.Status != 3) throw new Exception("لا يمكن اعتماد جرد غير مكتمل");

            foreach (var detailId in approvedDetailIds)
                await connection.ExecuteAsync(
                    "UPDATE dbo.StockCountDetails SET AdjustmentApproved=1 WHERE CountDetailID=@ID",
                    new { ID = detailId });

            await connection.ExecuteAsync("EXEC dbo.sp_ExecuteStockCountAdjustments @CountID, @UserID",
                new { CountID = countId, UserID = userId });

            try
            {
                var roles = await connection.QueryAsync<int>(
                    @"SELECT DISTINCT r.RoleID FROM dbo.UserRoles r
                      INNER JOIN dbo.RolePermissions rp ON r.RoleID=rp.RoleID
                      INNER JOIN dbo.SystemModules sm ON rp.ModuleID=sm.ModuleID
                      WHERE sm.ModuleCode=N'SCR_COUNT' AND rp.CanView=1");
                foreach (var roleId in roles)
                    await _notif.CreateNotificationAsync(1, "تم اعتماد الجرد ✅",
                        $"تم اعتماد أمر الجرد رقم {info.CountNumber} وتطبيق التسويات",
                        2, null, roleId, "SCR_COUNT", countId, userId);
            } catch { }

            await _audit.WriteAuditLogAsync(userId, 2, "StockCounts", countId.ToString(),
                moduleName: "SCR_COUNT", description: $"اعتماد جرد وتطبيق تسويات: {info.CountNumber}");
        }

        public async Task<(bool Success, string Message)> DeleteCountAsync(int countId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                var info = await connection.QueryFirstOrDefaultAsync<(string CountNumber, int Status)>(
                    "SELECT CountNumber, CountStatus FROM dbo.StockCounts WHERE CountID=@ID", new { ID = countId }, transaction);
                if (string.IsNullOrWhiteSpace(info.CountNumber)) return (false, "أمر الجرد غير موجود");
                if (info.Status >= 4) return (false, "لا يمكن حذف جرد معتمد");
                await connection.ExecuteAsync("DELETE FROM dbo.StockCountDetails WHERE CountID=@ID", new { ID = countId }, transaction);
                await connection.ExecuteAsync("DELETE FROM dbo.StockCounts WHERE CountID=@ID", new { ID = countId }, transaction);
                transaction.Commit();
                await _audit.WriteAuditLogAsync(userId, 3, "StockCounts", countId.ToString(),
                    moduleName: "SCR_COUNT", description: $"حذف جرد: {info.CountNumber}");
                return (true, "تم الحذف بنجاح");
            }
            catch (Exception ex) { transaction.Rollback(); return (false, ex.Message); }
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportBalancesToExcelAsync(List<StockBalanceDto> balances, int userId)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("أرصدة المخزون");
            ws.RightToLeft = true; ws.Style.Font.FontName = "Cairo";
            ws.Cell(1, 1).Value = "تقرير أرصدة المخزون — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 9).Merge().Style.Font.Bold = true;
            var headers = new[] { "#", "الكود", "الصنف", "المخزن", "الرصيد", "المتاح", "التكلفة", "القيمة", "الحالة" };
            for (int i = 0; i < headers.Length; i++)
            { var c = ws.Cell(3, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
              c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b"); c.Style.Font.FontColor = XLColor.White; }
            int row = 4, n = 0;
            foreach (var b in balances)
            { n++; ws.Cell(row, 1).Value = n; ws.Cell(row, 2).Value = b.ItemCode ?? "";
              ws.Cell(row, 3).Value = b.ItemNameAr ?? ""; ws.Cell(row, 4).Value = b.WarehouseNameAr ?? "";
              ws.Cell(row, 5).Value = b.CurrentQty; ws.Cell(row, 6).Value = b.AvailableQty;
              ws.Cell(row, 7).Value = b.AverageCost; ws.Cell(row, 8).Value = b.TotalValue;
              ws.Cell(row, 9).Value = b.StockAlertName ?? ""; row++; }
            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream(); wb.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, "InventoryBalance", moduleName: "SCR_STOCK",
                description: $"تصدير {balances.Count} رصيد مخزون");
            return stream.ToArray();
        }

        public async Task<byte[]> ExportMovementsToExcelAsync(List<StockMovementDto> movements, int userId)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("حركات المخزون");
            ws.RightToLeft = true; ws.Style.Font.FontName = "Cairo";
            var headers = new[] { "#", "التاريخ", "النوع", "الاتجاه", "الكود", "الصنف", "المخزن", "الكمية", "التكلفة", "المستند" };
            for (int i = 0; i < headers.Length; i++)
            { var c = ws.Cell(1, i + 1); c.Value = headers[i]; c.Style.Font.Bold = true;
              c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b"); c.Style.Font.FontColor = XLColor.White; }
            int row = 2, n = 0;
            foreach (var m in movements)
            { n++; ws.Cell(row, 1).Value = n; ws.Cell(row, 2).Value = m.TransactionDate.ToString("dd/MM/yyyy HH:mm");
              ws.Cell(row, 3).Value = m.TransactionTypeName ?? ""; ws.Cell(row, 4).Value = m.Direction ?? "";
              ws.Cell(row, 5).Value = m.ItemCode ?? ""; ws.Cell(row, 6).Value = m.ItemNameAr ?? "";
              ws.Cell(row, 7).Value = m.WarehouseNameAr ?? ""; ws.Cell(row, 8).Value = m.Quantity;
              ws.Cell(row, 9).Value = m.TotalCost; ws.Cell(row, 10).Value = m.SourceDocNumber ?? ""; row++; }
            ws.Columns().AdjustToContents();
            using var stream = new MemoryStream(); wb.SaveAs(stream);
            await _audit.WriteAuditLogAsync(userId, 5, "InventoryTransactions", moduleName: "SCR_TRANS",
                description: $"تصدير {movements.Count} حركة مخزون");
            return stream.ToArray();
        }
        // ==========================================
// فحص وإرسال تنبيهات المخزون
// ==========================================
public async Task<int> CheckAndSendStockAlertsAsync(int userId)
{
    using var connection = CreateConnection();
    int alertCount = 0;

    // جلب الأدوار المسؤولة عن المخزون
    var stockRoles = (await connection.QueryAsync<int>(
        @"SELECT DISTINCT r.RoleID FROM dbo.UserRoles r
          INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
          INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
          WHERE sm.ModuleCode IN (N'SCR_STOCK', N'SCR_PR') AND rp.CanView = 1")).ToList();

    int? targetRole = stockRoles.Any() ? stockRoles.First() : (int?)null;
    // حذف الإشعارات القديمة غير المقروءة من نفس النوع (لمنع التكرار)
await connection.ExecuteAsync(
    @"DELETE FROM dbo.Notifications 
      WHERE RelatedModule = N'SCR_STOCK' 
        AND IsActioned = 0 
        AND NotificationType IN (1, 4)
        AND CreatedDate < GETDATE()");

    try
    {
        // 1) أصناف نفدت
        var outOfStock = (await connection.QueryAsync<(int ItemID, string ItemCode, string ItemNameAr, string WarehouseNameAr)>(
            @"SELECT ib.ItemID, i.ItemCode, i.ItemNameAr, w.WarehouseNameAr
              FROM dbo.InventoryBalance ib
              INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON ib.WarehouseID = w.WarehouseID
              WHERE ib.CurrentQty <= 0 AND i.IsActive = 1 AND i.MinStockLevel > 0")).ToList();

        foreach (var item in outOfStock)
        {
            try
            {
                await _notif.CreateNotificationAsync(
                    notificationType: 1,
                    title: $"نفد المخزون ❌ — {item.ItemNameAr}",
                    message: $"الصنف {item.ItemCode} - {item.ItemNameAr} نفد في مخزن {item.WarehouseNameAr}",
                    priority: 1,
                    targetUserId: userId,
                    targetRoleId: targetRole,
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: item.ItemID,
                    createdBy: userId);
                alertCount++;
            }
            catch { }
        }

        // 2) أصناف تحت الحد الأدنى
        var belowMin = (await connection.QueryAsync<(int ItemID, string ItemCode, string ItemNameAr, string WarehouseNameAr, decimal CurrentQty, decimal MinStockLevel)>(
            @"SELECT ib.ItemID, i.ItemCode, i.ItemNameAr, w.WarehouseNameAr, ib.CurrentQty, i.MinStockLevel
              FROM dbo.InventoryBalance ib
              INNER JOIN dbo.Items i ON ib.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON ib.WarehouseID = w.WarehouseID
              WHERE ib.CurrentQty > 0 AND ib.CurrentQty <= i.MinStockLevel
                AND i.MinStockLevel > 0 AND i.IsActive = 1")).ToList();

        foreach (var item in belowMin)
        {
            try
            {
                await _notif.CreateNotificationAsync(
                    notificationType: 1,
                    title: $"مخزون منخفض ⚠️ — {item.ItemNameAr}",
                    message: $"الصنف {item.ItemCode} - {item.ItemNameAr} في {item.WarehouseNameAr}\nالرصيد: {item.CurrentQty:#,##0.##} | الحد الأدنى: {item.MinStockLevel:#,##0.##}",
                    priority: 2,
                    targetUserId: userId,
                    targetRoleId: targetRole,
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: item.ItemID,
                    createdBy: userId);
                alertCount++;
            }
            catch { }
        }

        // 3) باتشات قرب انتهاء الصلاحية (30 يوم)
        var nearExpiry = (await connection.QueryAsync<(string ItemNameAr, string ItemCode, string BatchNumber, DateTime ExpiryDate, string WarehouseNameAr, int DaysToExpiry)>(
            @"SELECT i.ItemNameAr, i.ItemCode, bb.BatchNumber, bb.ExpiryDate, w.WarehouseNameAr,
                     DATEDIFF(DAY, GETDATE(), bb.ExpiryDate) AS DaysToExpiry
              FROM dbo.InventoryBatchBalance bb
              INNER JOIN dbo.Items i ON bb.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON bb.WarehouseID = w.WarehouseID
              WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
                AND bb.ExpiryDate > CAST(GETDATE() AS DATE)
                AND bb.ExpiryDate <= DATEADD(DAY, 30, GETDATE())")).ToList();

        foreach (var b in nearExpiry)
        {
            try
            {
                await _notif.CreateNotificationAsync(
                    notificationType: 4,
                    title: $"قرب انتهاء الصلاحية ⏰ — {b.ItemNameAr}",
                    message: $"الصنف {b.ItemCode} باتش {b.BatchNumber} في {b.WarehouseNameAr}\nينتهي بعد {b.DaysToExpiry} يوم ({b.ExpiryDate:dd/MM/yyyy})",
                    priority: 1,
                    targetUserId: userId,
                    targetRoleId: targetRole,
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: 0,
                    createdBy: userId);
                alertCount++;
            }
            catch { }
        }

        // 4) باتشات منتهية الصلاحية
        var expired = (await connection.QueryAsync<(string ItemNameAr, string ItemCode, string BatchNumber, DateTime ExpiryDate, string WarehouseNameAr)>(
            @"SELECT i.ItemNameAr, i.ItemCode, bb.BatchNumber, bb.ExpiryDate, w.WarehouseNameAr
              FROM dbo.InventoryBatchBalance bb
              INNER JOIN dbo.Items i ON bb.ItemID = i.ItemID
              INNER JOIN dbo.Warehouses w ON bb.WarehouseID = w.WarehouseID
              WHERE bb.CurrentQty > 0 AND bb.ExpiryDate IS NOT NULL
                AND bb.ExpiryDate <= CAST(GETDATE() AS DATE)")).ToList();

        foreach (var b in expired)
        {
            try
            {
                await _notif.CreateNotificationAsync(
                    notificationType: 4,
                    title: $"منتهي الصلاحية ❌ — {b.ItemNameAr}",
                    message: $"الصنف {b.ItemCode} باتش {b.BatchNumber} في {b.WarehouseNameAr}\nانتهت صلاحيته في {b.ExpiryDate:dd/MM/yyyy}",
                    priority: 1,
                    targetUserId: userId,
                    targetRoleId: targetRole,
                    relatedModule: "SCR_STOCK",
                    relatedRecordId: 0,
                    createdBy: userId);
                alertCount++;
            }
            catch { }
        }
    }
    catch { }

    return alertCount;
}
    // ==========================================
// إضافة رصيد افتتاحي لصنف في مخزن
// ==========================================
public async Task SetOpeningBalanceAsync(int itemId, int warehouseId, decimal qty, decimal unitCost, int userId)
{
    using var connection = CreateConnection();

    if (await ExistsBalanceAsync(connection, itemId, warehouseId))
    {
        throw new Exception("يوجد رصيد بالفعل لهذا الصنف في هذا المخزن. استخدم تسوية الجرد لتعديل الرصيد.");
    }

    await connection.ExecuteAsync(
        @"INSERT INTO dbo.InventoryBalance (ItemID, WarehouseID, CurrentQty, AverageCost, LastMovementDate, ModifiedDate)
          VALUES (@ItemID, @WarehouseID, @Qty, @UnitCost, GETDATE(), GETDATE())",
        new { ItemID = itemId, WarehouseID = warehouseId, Qty = qty, UnitCost = unitCost });

    // تحديث تكلفة الصنف
    await connection.ExecuteAsync(
        "UPDATE dbo.Items SET AverageCost = @Cost, ModifiedDate = GETDATE() WHERE ItemID = @ID AND ISNULL(AverageCost, 0) = 0",
        new { ID = itemId, Cost = unitCost });

    // حركة مخزون
    await connection.ExecuteAsync(
        @"INSERT INTO dbo.InventoryTransactions
          (TransactionDate, TransactionType, ItemID, WarehouseID, Quantity, UnitID, UnitCost,
           BalanceBefore, BalanceAfter, SourceDocType, Notes, CreatedBy, CreatedDate)
          VALUES (GETDATE(), 9, @ItemID, @WarehouseID, @Qty,
           (SELECT PrimaryUnitID FROM dbo.Items WHERE ItemID = @ItemID),
           @UnitCost, 0, @Qty, N'OB', N'رصيد افتتاحي', @UserID, GETDATE())",
        new { ItemID = itemId, WarehouseID = warehouseId, Qty = qty, UnitCost = unitCost, UserID = userId });

    await _audit.WriteAuditLogAsync(userId, 1, "InventoryBalance", itemId.ToString(),
        moduleName: "SCR_STOCK",
        description: $"رصيد افتتاحي: صنف {itemId} في مخزن {warehouseId} — كمية {qty:#,##0.##}");
}

private async Task<bool> ExistsBalanceAsync(SqlConnection conn, int itemId, int warehouseId)
{
    var count = await conn.QueryFirstOrDefaultAsync<int>(
        "SELECT COUNT(*) FROM dbo.InventoryBalance WHERE ItemID = @ItemID AND WarehouseID = @WarehouseID",
        new { ItemID = itemId, WarehouseID = warehouseId });
    return count > 0;
}

// ==========================================
// إضافة أرصدة افتتاحية دفعة واحدة
// ==========================================
public async Task SetOpeningBalancesBulkAsync(List<OpeningBalanceDto> balances, int userId)
{
    foreach (var b in balances)
    {
        try
        {
            await SetOpeningBalanceAsync(b.ItemID, b.WarehouseID, b.Quantity, b.UnitCost, userId);
        }
        catch { /* تخطي الأصناف الموجودة */ }
    }
}

        // ==========================================
        // Helpers
        // ==========================================
        public static string GetTransferStatusName(int s) => s switch { 1 => "مسودة", 2 => "معتمد", 3 => "تم التحويل", 4 => "ملغي", _ => "غير محدد" };
        public static string GetTransferStatusColor(int s) => s switch { 1 => "#64748b", 2 => "#f59e0b", 3 => "#10b981", 4 => "#ef4444", _ => "#6b7280" };
        public static string GetCountStatusName(int s) => s switch { 1 => "مسودة", 2 => "قيد الجرد", 3 => "مكتمل", 4 => "معتمد", 5 => "ملغي", _ => "غير محدد" };
        public static string GetCountStatusColor(int s) => s switch { 1 => "#64748b", 2 => "#3b82f6", 3 => "#f59e0b", 4 => "#10b981", 5 => "#ef4444", _ => "#6b7280" };
        public static string GetAlertColor(int a) => a switch { 0 => "#10b981", 1 => "#f59e0b", 2 => "#f97316", 3 => "#ef4444", _ => "#6b7280" };
        public static string GetExpiryColor(int a) => a switch { 0 => "#10b981", 1 => "#f59e0b", 2 => "#f97316", 3 => "#ef4444", _ => "#6b7280" };
    }
}