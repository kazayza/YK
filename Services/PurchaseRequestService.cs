using Dapper;
using ClosedXML.Excel;

namespace YKCoatings.Services
{
    public class PurchaseRequestService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notif;

        public PurchaseRequestService(IConfiguration configuration, AuditService audit, NotificationService notif)
            : base(configuration)
        {
            _audit = audit;
            _notif = notif;
        }

        // ==========================================
        // قائمة الطلبات
        // ==========================================
        public async Task<List<PurchaseRequestListDto>> GetRequestsListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            pr.RequestID, pr.RequestNumber, pr.RequestDate, pr.RequiredDate,
                            e.FullNameAr AS RequestedByName, d.DepartmentNameAr AS DepartmentName,
                            pr.RequestStatus, pr.Priority, pr.Notes,
                            (SELECT COUNT(*) FROM dbo.PurchaseRequestDetails rd WHERE rd.RequestID = pr.RequestID) AS ItemCount
                        FROM dbo.PurchaseRequests pr
                        LEFT JOIN dbo.Employees e ON pr.RequestedBy = e.EmployeeID
                        LEFT JOIN dbo.Departments d ON pr.DepartmentID = d.DepartmentID
                        ORDER BY pr.RequestDate DESC, pr.RequestID DESC";
            var result = await connection.QueryAsync<PurchaseRequestListDto>(sql);
            return result.ToList();
        }

        // ==========================================
        // جلب طلب واحد
        // ==========================================
        public async Task<PurchaseRequestHeaderDto?> GetRequestByIdAsync(int requestId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT RequestID, RequestNumber, RequestDate, RequiredDate,
                               RequestedBy, DepartmentID, RequestStatus, Priority,
                               Notes, ApprovedBy, ApprovedDate, RejectionReason
                        FROM dbo.PurchaseRequests WHERE RequestID = @RequestID";
            return await connection.QueryFirstOrDefaultAsync<PurchaseRequestHeaderDto>(sql, new { RequestID = requestId });
        }

        // ==========================================
        // تفاصيل الطلب
        // ==========================================
        public async Task<List<PurchaseRequestDetailDto>> GetRequestDetailsAsync(int requestId)
        {
            using var connection = CreateConnection();
            var sql = @"SELECT 
                            rd.RequestDetailID, rd.RequestID, rd.LineNumber,
                            rd.ItemID, i.ItemCode, i.ItemNameAr,
                            rd.UnitID, u.UnitNameAr AS UnitName,
                            rd.RequestedQty, rd.ApprovedQty, rd.EstimatedPrice,
                            rd.CurrentStock, rd.Purpose, rd.Notes
                        FROM dbo.PurchaseRequestDetails rd
                        INNER JOIN dbo.Items i ON rd.ItemID = i.ItemID
                        INNER JOIN dbo.Units u ON rd.UnitID = u.UnitID
                        WHERE rd.RequestID = @RequestID
                        ORDER BY rd.LineNumber";
            var result = await connection.QueryAsync<PurchaseRequestDetailDto>(sql, new { RequestID = requestId });
            return result.ToList();
        }

        // ==========================================
        // توليد رقم الطلب
        // ==========================================
        public async Task<string> GenerateRequestNumberAsync()
        {
            using var connection = CreateConnection();
            try
            {
                var sql = @"DECLARE @NextNum NVARCHAR(50);
                           EXEC sp_GetNextNumber N'PR', @NextNum OUTPUT;
                           SELECT @NextNum;";
                var result = await connection.QueryFirstOrDefaultAsync<string>(sql);
                return result ?? $"PR-{DateTime.Now:yyMMddHHmmss}";
            }
            catch { return $"PR-{DateTime.Now:yyMMddHHmmss}"; }
        }

        // ==========================================
        // إضافة طلب جديد
        // ==========================================
        public async Task<int> InsertRequestAsync(PurchaseRequestHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"INSERT INTO dbo.PurchaseRequests 
                        (RequestNumber, RequestDate, RequiredDate, RequestedBy, DepartmentID,
                         RequestStatus, Priority, Notes, CreatedBy, CreatedDate)
                        VALUES
                        (@RequestNumber, @RequestDate, @RequiredDate, NULLIF(@RequestedBy, 0),
                         NULLIF(@DepartmentID, 0), @RequestStatus, @Priority, @Notes, @UserID, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                header.RequestNumber, header.RequestDate, header.RequiredDate,
                header.RequestedBy, header.DepartmentID,
                header.RequestStatus, header.Priority, header.Notes,
                UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 1, "PurchaseRequests", newId.ToString(),
                moduleName: "SCR_PR",
                description: $"إنشاء طلب شراء: {header.RequestNumber}");

            return newId;
        }

        // ==========================================
        // تحديث طلب
        // ==========================================
        public async Task UpdateRequestAsync(PurchaseRequestHeaderDto header, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.PurchaseRequests SET
                            RequestDate = @RequestDate, RequiredDate = @RequiredDate,
                            RequestedBy = NULLIF(@RequestedBy, 0),
                            DepartmentID = NULLIF(@DepartmentID, 0),
                            Priority = @Priority, Notes = @Notes,
                            ModifiedBy = @UserID, ModifiedDate = GETDATE()
                        WHERE RequestID = @RequestID AND RequestStatus = 1";

            await connection.ExecuteAsync(sql, new
            {
                header.RequestID, header.RequestDate, header.RequiredDate,
                header.RequestedBy, header.DepartmentID,
                header.Priority, header.Notes, UserID = userId
            });

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseRequests", header.RequestID.ToString(),
                moduleName: "SCR_PR",
                description: $"تعديل طلب شراء: {header.RequestNumber}");
        }

        // ==========================================
        // إضافة سطر تفاصيل
        // ==========================================
        public async Task<int> InsertDetailAsync(PurchaseRequestDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();

            var maxLine = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT ISNULL(MAX(LineNumber), 0) FROM dbo.PurchaseRequestDetails WHERE RequestID = @ID",
                new { ID = detail.RequestID });

            detail.LineNumber = maxLine + 1;

            var sql = @"INSERT INTO dbo.PurchaseRequestDetails 
                        (RequestID, LineNumber, ItemID, UnitID, RequestedQty, ApprovedQty,
                         EstimatedPrice, CurrentStock, Purpose, Notes)
                        VALUES
                        (@RequestID, @LineNumber, @ItemID, @UnitID, @RequestedQty, @ApprovedQty,
                         @EstimatedPrice, @CurrentStock, @Purpose, @Notes);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, detail);

            await _audit.WriteAuditLogAsync(userId, 1, "PurchaseRequestDetails", newId.ToString(),
                moduleName: "SCR_PR",
                description: $"إضافة صنف في طلب شراء رقم {detail.RequestID}");

            return newId;
        }

        // ==========================================
        // تحديث سطر تفاصيل
        // ==========================================
        public async Task UpdateDetailAsync(PurchaseRequestDetailEditDto detail, int userId)
        {
            using var connection = CreateConnection();
            var sql = @"UPDATE dbo.PurchaseRequestDetails SET
                            ItemID = @ItemID, UnitID = @UnitID,
                            RequestedQty = @RequestedQty, ApprovedQty = @ApprovedQty,
                            EstimatedPrice = @EstimatedPrice, CurrentStock = @CurrentStock,
                            Purpose = @Purpose, Notes = @Notes
                        WHERE RequestDetailID = @RequestDetailID";

            await connection.ExecuteAsync(sql, detail);

            await _audit.WriteAuditLogAsync(userId, 2, "PurchaseRequestDetails", detail.RequestDetailID.ToString(),
                moduleName: "SCR_PR",
                description: $"تعديل سطر في طلب شراء رقم {detail.RequestID}");
        }

        // ==========================================
        // حذف سطر تفاصيل
        // ==========================================
        public async Task DeleteDetailAsync(int detailId, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(
                "DELETE FROM dbo.PurchaseRequestDetails WHERE RequestDetailID = @ID",
                new { ID = detailId });

            await _audit.WriteAuditLogAsync(userId, 3, "PurchaseRequestDetails", detailId.ToString(),
                moduleName: "SCR_PR",
                description: $"حذف سطر من طلب شراء");
        }

        // ==========================================
        // تغيير حالة الطلب
        // ==========================================
        public async Task ChangeStatusAsync(
            int requestId,
            int newStatus,
            int userId,
            int? employeeId = null,
            string? rejectionReason = null)
        {
            using var connection = CreateConnection();

            var reqNum = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT RequestNumber FROM dbo.PurchaseRequests WHERE RequestID = @ID",
                new { ID = requestId });

            var createdBy = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT CreatedBy FROM dbo.PurchaseRequests WHERE RequestID = @ID",
                new { ID = requestId });

            if (newStatus == 2) // إرسال للاعتماد
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseRequests SET 
                        RequestStatus = 2,
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE RequestID = @ID",
                    new { ID = requestId, UserID = userId });

                try
                {
                    var approverRoles = await connection.QueryAsync<int>(
                        @"SELECT DISTINCT r.RoleID FROM dbo.UserRoles r
                          INNER JOIN dbo.RolePermissions rp ON r.RoleID = rp.RoleID
                          INNER JOIN dbo.SystemModules sm ON rp.ModuleID = sm.ModuleID
                          WHERE sm.ModuleCode = N'SCR_PR' AND rp.CanApprove = 1");

                    foreach (var roleId in approverRoles)
                    {
                        await _notif.CreateNotificationAsync(
                            notificationType: 3,
                            title: "طلب شراء بانتظار الاعتماد 📋",
                            message: $"طلب الشراء رقم {reqNum} بانتظار الاعتماد",
                            priority: 1,
                            targetUserId: null,
                            targetRoleId: roleId,
                            relatedModule: "SCR_PR",
                            relatedRecordId: requestId,
                            createdBy: userId);
                    }
                }
                catch { }

                await _audit.WriteAuditLogAsync(userId, 2, "PurchaseRequests", requestId.ToString(),
                    moduleName: "SCR_PR",
                    description: $"إرسال طلب شراء {reqNum} للاعتماد");
            }
            else if (newStatus == 3) // اعتماد
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseRequests SET 
                        RequestStatus = 3,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE RequestID = @ID",
                    new { ID = requestId, EmployeeID = employeeId, UserID = userId });

                try
                {
                    if (createdBy.HasValue)
                    {
                        await _notif.CreateNotificationAsync(
                            notificationType: 3,
                            title: "تم اعتماد طلب الشراء ✅",
                            message: $"تم اعتماد طلب الشراء رقم {reqNum}",
                            priority: 2,
                            targetUserId: createdBy.Value,
                            relatedModule: "SCR_PR",
                            relatedRecordId: requestId,
                            createdBy: userId);
                    }

                    await _notif.MarkRelatedAsActionedAsync("SCR_PR", requestId);
                }
                catch { }

                await _audit.WriteAuditLogAsync(userId, 2, "PurchaseRequests", requestId.ToString(),
                    moduleName: "SCR_PR",
                    description: $"اعتماد طلب شراء: {reqNum}");
            }
            else if (newStatus == 4) // رفض
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseRequests SET 
                        RequestStatus = 4,
                        ApprovedBy = @EmployeeID,
                        ApprovedDate = GETDATE(),
                        RejectionReason = @Reason,
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE RequestID = @ID",
                    new { ID = requestId, EmployeeID = employeeId, Reason = rejectionReason, UserID = userId });

                try
                {
                    if (createdBy.HasValue)
                    {
                        await _notif.CreateNotificationAsync(
                            notificationType: 3,
                            title: "تم رفض طلب الشراء ❌",
                            message: $"تم رفض طلب الشراء رقم {reqNum}. السبب: {rejectionReason}",
                            priority: 1,
                            targetUserId: createdBy.Value,
                            relatedModule: "SCR_PR",
                            relatedRecordId: requestId,
                            createdBy: userId);
                    }

                    await _notif.MarkRelatedAsActionedAsync("SCR_PR", requestId);
                }
                catch { }

                await _audit.WriteAuditLogAsync(userId, 2, "PurchaseRequests", requestId.ToString(),
                    moduleName: "SCR_PR",
                    description: $"رفض طلب شراء: {reqNum} — السبب: {rejectionReason}");
            }
            else
            {
                await connection.ExecuteAsync(
                    @"UPDATE dbo.PurchaseRequests SET 
                        RequestStatus = @Status,
                        ModifiedBy = @UserID,
                        ModifiedDate = GETDATE()
                      WHERE RequestID = @ID",
                    new { ID = requestId, Status = newStatus, UserID = userId });

                await _audit.WriteAuditLogAsync(userId, 2, "PurchaseRequests", requestId.ToString(),
                    moduleName: "SCR_PR",
                    description: $"تغيير حالة طلب شراء {reqNum} إلى: {GetStatusName(newStatus)}");
            }
        }

        // ==========================================
        // اعتماد سريع
        // ==========================================
        public async Task QuickApproveAsync(int requestId, int userId, int? employeeId = null)
{
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction();

    try
    {
        // 1) الكمية المعتمدة = المطلوبة
        await connection.ExecuteAsync(
            @"UPDATE dbo.PurchaseRequestDetails 
              SET ApprovedQty = RequestedQty 
              WHERE RequestID = @ID",
            new { ID = requestId }, transaction);

        // 2) تحديث حالة الطلب
        await connection.ExecuteAsync(
            @"UPDATE dbo.PurchaseRequests SET 
                RequestStatus = 3,
                ApprovedBy = @EmployeeID,
                ApprovedDate = GETDATE(),
                ModifiedBy = @UserID,
                ModifiedDate = GETDATE()
              WHERE RequestID = @ID",
            new
            {
                ID = requestId,
                EmployeeID = employeeId,
                UserID = userId
            }, transaction);

        transaction.Commit();
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        throw new Exception($"فشل الاعتماد السريع: {ex.Message}", ex);
    }

    // بعد الحفظ النهائي: إشعار + Audit
    var reqNum = await connection.QueryFirstOrDefaultAsync<string>(
        "SELECT RequestNumber FROM dbo.PurchaseRequests WHERE RequestID = @ID",
        new { ID = requestId });

    var createdBy = await connection.QueryFirstOrDefaultAsync<int?>(
        "SELECT CreatedBy FROM dbo.PurchaseRequests WHERE RequestID = @ID",
        new { ID = requestId });

    try
    {
        if (createdBy.HasValue)
        {
            await _notif.CreateNotificationAsync(
                notificationType: 3,
                title: "تم اعتماد طلب الشراء ✅",
                message: $"تم اعتماد طلب الشراء رقم {reqNum} (اعتماد سريع)",
                priority: 2,
                targetUserId: createdBy.Value,
                relatedModule: "SCR_PR",
                relatedRecordId: requestId,
                createdBy: userId);
        }

        await _notif.MarkRelatedAsActionedAsync("SCR_PR", requestId);
    }
    catch { }

    await _audit.WriteAuditLogAsync(
        userId,
        2,
        "PurchaseRequests",
        requestId.ToString(),
        moduleName: "SCR_PR",
        description: $"اعتماد سريع لطلب شراء: {reqNum}");
}

        // ==========================================
        // اعتماد تفصيلي
        // ==========================================
        public async Task ApproveWithQuantitiesAsync(
    int requestId,
    List<ApprovedQtyDto> approvedItems,
    int userId,
    int? employeeId = null)
{
    using var connection = CreateConnection();
    await connection.OpenAsync();
    using var transaction = connection.BeginTransaction();

    try
    {
        // 1) تحديث الكميات المعتمدة
        foreach (var item in approvedItems)
        {
            await connection.ExecuteAsync(
                @"UPDATE dbo.PurchaseRequestDetails 
                  SET ApprovedQty = @ApprovedQty 
                  WHERE RequestDetailID = @DetailID AND RequestID = @RequestID",
                new
                {
                    DetailID = item.DetailID,
                    ApprovedQty = item.ApprovedQty,
                    RequestID = requestId
                }, transaction);
        }

        // 2) تحديث حالة الطلب
        await connection.ExecuteAsync(
            @"UPDATE dbo.PurchaseRequests SET 
                RequestStatus = 3,
                ApprovedBy = @EmployeeID,
                ApprovedDate = GETDATE(),
                ModifiedBy = @UserID,
                ModifiedDate = GETDATE()
              WHERE RequestID = @ID",
            new
            {
                ID = requestId,
                EmployeeID = employeeId,
                UserID = userId
            }, transaction);

        transaction.Commit();
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        throw new Exception($"فشل الاعتماد التفصيلي: {ex.Message}", ex);
    }

    // بعد الحفظ النهائي: إشعار + Audit
    var reqNum = await connection.QueryFirstOrDefaultAsync<string>(
        "SELECT RequestNumber FROM dbo.PurchaseRequests WHERE RequestID = @ID",
        new { ID = requestId });

    var createdBy = await connection.QueryFirstOrDefaultAsync<int?>(
        "SELECT CreatedBy FROM dbo.PurchaseRequests WHERE RequestID = @ID",
        new { ID = requestId });

    try
    {
        if (createdBy.HasValue)
        {
            await _notif.CreateNotificationAsync(
                notificationType: 3,
                title: "تم اعتماد طلب الشراء ✅",
                message: $"تم اعتماد طلب الشراء رقم {reqNum} (اعتماد تفصيلي)",
                priority: 2,
                targetUserId: createdBy.Value,
                relatedModule: "SCR_PR",
                relatedRecordId: requestId,
                createdBy: userId);
        }

        await _notif.MarkRelatedAsActionedAsync("SCR_PR", requestId);
    }
    catch { }

    await _audit.WriteAuditLogAsync(
        userId,
        2,
        "PurchaseRequestDetails",
        requestId.ToString(),
        moduleName: "SCR_PR",
        description: $"اعتماد تفصيلي ({approvedItems.Count} صنف) لطلب شراء: {reqNum}");
}

        // ==========================================
        // حذف طلب (مسودة فقط)
        // ==========================================
        public async Task<(bool Success, string Message)> DeleteRequestAsync(int requestId, int userId)
        {
            using var connection = CreateConnection();

            var status = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT RequestStatus FROM dbo.PurchaseRequests WHERE RequestID = @ID",
                new { ID = requestId });

            if (status != 1)
                return (false, "لا يمكن حذف طلب غير مسودة");

            var reqNum = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT RequestNumber FROM dbo.PurchaseRequests WHERE RequestID = @ID",
                new { ID = requestId });

            await connection.ExecuteAsync(
                "DELETE FROM dbo.PurchaseRequestDetails WHERE RequestID = @ID",
                new { ID = requestId });

            await connection.ExecuteAsync(
                "DELETE FROM dbo.PurchaseRequests WHERE RequestID = @ID",
                new { ID = requestId });

            await _audit.WriteAuditLogAsync(userId, 3, "PurchaseRequests", requestId.ToString(),
                moduleName: "SCR_PR",
                description: $"حذف طلب شراء: {reqNum}");

            return (true, "تم حذف الطلب بنجاح");
        }

        // ==========================================
        // تصدير Excel
        // ==========================================
        public async Task<byte[]> ExportToExcelAsync(List<PurchaseRequestListDto> requests, int userId)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("طلبات الشراء");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير طلبات الشراء — مصنع واي كي كوتينج";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 8).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int headerRow = 3;
            var headers = new[] { "#", "رقم الطلب", "التاريخ", "مطلوب بتاريخ", "طالب الشراء",
                                  "القسم", "الحالة", "الأولوية" };
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
            foreach (var req in requests)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = req.RequestNumber ?? "";
                ws.Cell(row, 3).Value = req.RequestDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 4).Value = req.RequiredDate?.ToString("dd/MM/yyyy") ?? "—";
                ws.Cell(row, 5).Value = req.RequestedByName ?? "—";
                ws.Cell(row, 6).Value = req.DepartmentName ?? "—";
                ws.Cell(row, 7).Value = GetStatusName(req.RequestStatus);
                ws.Cell(row, 8).Value = GetPriorityName(req.Priority);

                for (int i = 1; i <= 8; i++)
                {
                    ws.Cell(row, i).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, i).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e2e8f0");
                }
                if (num % 2 == 0)
                    ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            await _audit.WriteAuditLogAsync(userId, 5, "PurchaseRequests",
                moduleName: "SCR_PR",
                description: $"تصدير {requests.Count} طلب شراء إلى Excel");

            return stream.ToArray();
        }

        // ==========================================
        // تقرير طباعة
        // ==========================================
        public async Task<string> GenerateReportHtmlAsync(int requestId, int userId)
        {
            var header = await GetRequestByIdAsync(requestId);
            if (header == null) return "<h3>الطلب غير موجود</h3>";

            var details = await GetRequestDetailsAsync(requestId);

            using var connection = CreateConnection();
            var requestedByName = header.RequestedBy > 0
                ? await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
                    new { ID = header.RequestedBy }) ?? "—"
                : "—";

            var deptName = header.DepartmentID > 0
                ? await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT DepartmentNameAr FROM dbo.Departments WHERE DepartmentID = @ID",
                    new { ID = header.DepartmentID }) ?? "—"
                : "—";

            var detailsHtml = "";
            int n = 0;
            foreach (var d in details)
            {
                n++;
                detailsHtml += $@"<tr>
                    <td style='text-align:center'>{n}</td>
                    <td>{d.ItemCode}</td><td>{d.ItemNameAr}</td>
                    <td>{d.UnitName}</td>
                    <td style='text-align:center'>{d.RequestedQty:#,##0.##}</td>
                    <td style='text-align:center'>{d.ApprovedQty?.ToString("#,##0.##") ?? "—"}</td>
                    <td style='text-align:left'>{d.EstimatedPrice:#,##0.00}</td>
                    <td>{d.Purpose ?? "—"}</td></tr>";
            }

            var totalEstimated = details.Sum(d => d.RequestedQty * d.EstimatedPrice);

            var html = $@"<!DOCTYPE html><html dir='rtl' lang='ar'><head><meta charset='utf-8'>
<title>طلب شراء — {header.RequestNumber}</title>
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
    <div class='doc-title'>طلب شراء</div>
    <div class='doc-num'>{header.RequestNumber}</div>
</div>
<div class='info-grid'>
    <div class='info-item'><div class='info-label'>تاريخ الطلب</div><div class='info-value'>{header.RequestDate:dd/MM/yyyy}</div></div>
    <div class='info-item'><div class='info-label'>مطلوب بتاريخ</div><div class='info-value'>{header.RequiredDate?.ToString("dd/MM/yyyy") ?? "—"}</div></div>
    <div class='info-item'><div class='info-label'>طالب الشراء</div><div class='info-value'>{requestedByName}</div></div>
    <div class='info-item'><div class='info-label'>القسم</div><div class='info-value'>{deptName}</div></div>
    <div class='info-item'><div class='info-label'>الأولوية</div><div class='info-value'>{GetPriorityName(header.Priority)}</div></div>
    <div class='info-item'><div class='info-label'>الحالة</div><div class='info-value'>{GetStatusName(header.RequestStatus)}</div></div>
    <div class='info-item'><div class='info-label'>عدد الأصناف</div><div class='info-value'>{details.Count}</div></div>
    <div class='info-item'><div class='info-label'>إجمالي تقديري</div><div class='info-value'>{totalEstimated:#,##0.00} ج.م</div></div>
</div>
<table>
    <thead>
        <tr>
            <th>#</th><th>الكود</th><th>الصنف</th><th>الوحدة</th>
            <th>الكمية المطلوبة</th><th>الكمية المعتمدة</th>
            <th>السعر التقديري</th><th>الغرض</th>
        </tr>
    </thead>
    <tbody>
        {detailsHtml}
        <tr class='total-row'>
            <td colspan='6' style='text-align:left'>الإجمالي التقديري</td>
            <td style='text-align:left'>{totalEstimated:#,##0.00} ج.م</td>
            <td></td>
        </tr>
    </tbody>
</table>
{(string.IsNullOrEmpty(header.Notes) ? "" : $"<div style='margin-bottom:16px'><strong>ملاحظات:</strong> {header.Notes}</div>")}
<div class='footer'>تم الطباعة: {DateTime.Now:dd/MM/yyyy hh:mm tt} — واي كي كوتينج ERP v2.0</div>
</body></html>";

            await _audit.WriteAuditLogAsync(userId, 5, "PurchaseRequests", requestId.ToString(),
                moduleName: "SCR_PR",
                description: $"طباعة طلب شراء: {header.RequestNumber}");

            return html;
        }

        // ==========================================
        // أسماء مساعدة
        // ==========================================
        public static string GetStatusName(int status)
        {
            return status switch
            {
                1 => "مسودة",
                2 => "مقدم للاعتماد",
                3 => "معتمد",
                4 => "مرفوض",
                5 => "تم التنفيذ",
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
                _ => "#6b7280"
            };
        }

        public static string GetPriorityName(int priority)
        {
            return priority switch
            {
                1 => "عاجل",
                2 => "عادي",
                3 => "غير عاجل",
                _ => "عادي"
            };
        }

        public static string GetPriorityColor(int priority)
        {
            return priority switch
            {
                1 => "#ef4444",
                2 => "#3b82f6",
                3 => "#6b7280",
                _ => "#3b82f6"
            };
        }
    }
}