using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class LeaveRequestService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly SequenceService _sequence;
        private readonly NotificationService _notif;

        public LeaveRequestService(
            IConfiguration configuration,
            AuditService audit,
            SequenceService sequence,
            NotificationService notif) : base(configuration)
        {
            _audit = audit;
            _sequence = sequence;
            _notif = notif;
        }

        // ========================================
        // جلب قائمة الطلبات مع Paging
        // ========================================
        public async Task<LeaveRequestPagedResult> GetLeaveRequestsPagedAsync(LeaveRequestFilterDto filter)
        {
            using var connection = CreateConnection();
            filter ??= new LeaveRequestFilterDto();

            var where = @"
                WHERE (@LeaveTypeID IS NULL OR @LeaveTypeID = 0 OR lr.LeaveTypeID = @LeaveTypeID)
                  AND (@RequestStatus IS NULL OR @RequestStatus = 0 OR lr.RequestStatus = @RequestStatus)
                  AND (@DateFrom IS NULL OR lr.StartDate >= @DateFrom)
                  AND (@DateTo IS NULL OR lr.EndDate <= @DateTo)
                  AND (@DepartmentID IS NULL OR @DepartmentID = 0 OR e.DepartmentID = @DepartmentID)
                  AND (ISNULL(@SearchText, N'') = N''
                      OR e.FullNameAr LIKE N'%' + @SearchText + N'%'
                      OR e.EmployeeCode LIKE N'%' + @SearchText + N'%'
                      OR lr.RequestNumber LIKE N'%' + @SearchText + N'%')";

            var countSql = @"SELECT COUNT(*) FROM dbo.LeaveRequests lr
                INNER JOIN dbo.Employees e ON lr.EmployeeID = e.EmployeeID " + where;

            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
            {
                filter.SearchText,
                filter.DepartmentID,
                filter.LeaveTypeID,
                filter.RequestStatus,
                filter.DateFrom,
                filter.DateTo
            });

            var dataSql = @"
                SELECT * FROM (
                    SELECT lr.LeaveRequestID, lr.RequestNumber, lr.EmployeeID,
                        e.EmployeeCode, e.FullNameAr AS EmployeeName,
                        d.DepartmentNameAr AS DepartmentName, lr.LeaveTypeID,
                        lt.LeaveTypeNameAr AS LeaveTypeName,
                        lr.StartDate, lr.EndDate, lr.NumberOfDays, lr.Reason, lr.RequestStatus,
                        CASE lr.RequestStatus
                            WHEN 1 THEN N'مقدم'
                            WHEN 2 THEN N'معتمد مدير'
                            WHEN 3 THEN N'معتمد HR'
                            WHEN 4 THEN N'مرفوض'
                            WHEN 5 THEN N'ملغي'
                        END AS StatusName,
                        mgr.FullNameAr AS ApprovedByManagerName, lr.ManagerApprovalDate,
                        hr.FullNameAr AS ApprovedByHRName, lr.HRApprovalDate,
                        lr.RejectionReason,
                        ROW_NUMBER() OVER (ORDER BY lr.CreatedDate DESC) AS RowNum
                    FROM dbo.LeaveRequests lr
                    INNER JOIN dbo.Employees e ON lr.EmployeeID = e.EmployeeID
                    LEFT JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
                    LEFT JOIN dbo.LeaveTypes lt ON lr.LeaveTypeID = lt.LeaveTypeID
                    LEFT JOIN dbo.Employees mgr ON lr.ApprovedByManager = mgr.EmployeeID
                    LEFT JOIN dbo.Employees hr ON lr.ApprovedByHR = hr.EmployeeID
                    " + where + @"
                ) ranked
                WHERE RowNum BETWEEN @StartRow AND @EndRow
                ORDER BY RowNum";

            var items = await connection.QueryAsync<LeaveRequestListDto>(dataSql, new
            {
                filter.SearchText,
                filter.DepartmentID,
                filter.LeaveTypeID,
                filter.RequestStatus,
                filter.DateFrom,
                filter.DateTo,
                StartRow = (filter.PageNumber - 1) * filter.PageSize + 1,
                EndRow = filter.PageNumber * filter.PageSize
            });

            return new LeaveRequestPagedResult
            {
                Items = items.ToList(),
                TotalCount = totalCount,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize
            };
        }

        // ========================================
        // جلب طلب واحد
        // ========================================
        public async Task<LeaveRequestEditDto?> GetLeaveRequestByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<LeaveRequestEditDto>(
                @"SELECT LeaveRequestID, RequestNumber, EmployeeID, LeaveTypeID,
                         StartDate, EndDate, NumberOfDays, Reason, RequestStatus,
                         RejectionReason, SubstituteEmployeeID, Notes
                  FROM dbo.LeaveRequests WHERE LeaveRequestID = @ID",
                new { ID = id });
        }
        public async Task<LeaveRequestApprovalDto?> GetLeaveRequestApprovalAsync(int id)
{
    using var connection = CreateConnection();
    return await connection.QueryFirstOrDefaultAsync<LeaveRequestApprovalDto>(
        @"SELECT
            lr.LeaveRequestID,
            lr.RequestStatus,
            lr.RejectionReason,
            mgr.FullNameAr AS ApprovedByManagerName,
            lr.ManagerApprovalDate,
            hr.FullNameAr AS ApprovedByHRName,
            lr.HRApprovalDate
          FROM dbo.LeaveRequests lr
          LEFT JOIN dbo.Employees mgr ON lr.ApprovedByManager = mgr.EmployeeID
          LEFT JOIN dbo.Employees hr ON lr.ApprovedByHR = hr.EmployeeID
          WHERE lr.LeaveRequestID = @ID",
        new { ID = id });
}

        // ========================================
        // تقديم طلب إجازة
        // ========================================
        public async Task<int> InsertLeaveRequestAsync(LeaveRequestEditDto item, int userId)
        {
            using var connection = CreateConnection();

            if (item.EmployeeID <= 0)
                throw new Exception("يجب اختيار الموظف");
            if (item.LeaveTypeID <= 0)
                throw new Exception("يجب اختيار نوع الإجازة");
            if (item.EndDate < item.StartDate)
                throw new Exception("تاريخ النهاية يجب أن يكون بعد البداية");

            item.NumberOfDays = (item.EndDate - item.StartDate).Days + 1;

            // جلب نوع الإجازة
            var leaveType = await connection.QueryFirstOrDefaultAsync<LeaveTypeInfo>(
                @"SELECT LeaveTypeID, LeaveTypeNameAr, IsPaid, MaxDaysPerYear,
                         DeductFromBalance, AllowNegativeBalance
                  FROM dbo.LeaveTypes WHERE LeaveTypeID = @ID",
                new { ID = item.LeaveTypeID });

            if (leaveType == null)
                throw new Exception("نوع الإجازة غير موجود");

            // التحقق من الرصيد
            if (leaveType.DeductFromBalance)
            {
                var fiscalYearId = await GetCurrentFiscalYearIdAsync(connection, item.StartDate);

                if (fiscalYearId == 0)
                    throw new Exception("لا توجد سنة مالية نشطة لتاريخ الإجازة");

                var balance = await connection.QueryFirstOrDefaultAsync<LeaveBalanceInfo>(
                    @"SELECT BalanceID, TotalEntitled, UsedDays, PendingDays, RemainingDays
                      FROM dbo.EmployeeLeaveBalances
                      WHERE EmployeeID = @EmployeeID
                        AND LeaveTypeID = @LeaveTypeID
                        AND FiscalYearID = @FiscalYearID",
                    new
                    {
                        item.EmployeeID,
                        item.LeaveTypeID,
                        FiscalYearID = fiscalYearId
                    });

                if (balance == null && !leaveType.AllowNegativeBalance)
                    throw new Exception($"لا يوجد رصيد إجازات من نوع ({leaveType.LeaveTypeNameAr}) لهذا الموظف");

                if (balance != null && !leaveType.AllowNegativeBalance)
                {
                    var available = balance.RemainingDays;
                    if (item.NumberOfDays > available)
                        throw new Exception($"رصيد الإجازات غير كافٍ. المتاح: {available} يوم، المطلوب: {item.NumberOfDays} يوم");
                }

                // التحقق من الحد الأقصى السنوي
                if (leaveType.MaxDaysPerYear > 0 && balance != null)
                {
                    var totalUsed = balance.UsedDays + balance.PendingDays + item.NumberOfDays;
                    if (totalUsed > leaveType.MaxDaysPerYear)
                        throw new Exception($"تجاوز الحد الأقصى السنوي ({leaveType.MaxDaysPerYear} يوم) لهذا النوع من الإجازات");
                }

                // تحديث PendingDays
                if (balance != null)
                {
                    await connection.ExecuteAsync(@"
                        UPDATE dbo.EmployeeLeaveBalances
                        SET PendingDays = ISNULL(PendingDays, 0) + @Days,
                            ModifiedDate = GETDATE()
                        WHERE BalanceID = @BalanceID",
                        new { Days = item.NumberOfDays, balance.BalanceID });
                }
            }

            // توليد رقم الطلب
            item.RequestNumber = await _sequence.GetNextCodeAsync("LV");

            var sql = @"
                INSERT INTO dbo.LeaveRequests
                    (RequestNumber, EmployeeID, LeaveTypeID,
                     StartDate, EndDate, NumberOfDays, Reason, RequestStatus,
                     SubstituteEmployeeID, Notes, CreatedBy, CreatedDate)
                VALUES
                    (@RequestNumber, @EmployeeID, @LeaveTypeID,
                     @StartDate, @EndDate, @NumberOfDays, @Reason, 1,
                     @SubstituteEmployeeID, @Notes, @CreatedBy, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                item.RequestNumber,
                item.EmployeeID,
                item.LeaveTypeID,
                item.StartDate,
                item.EndDate,
                item.NumberOfDays,
                item.Reason,
                item.SubstituteEmployeeID,
                item.Notes,
                CreatedBy = userId
            });

            // Audit
            await _audit.WriteAuditLogAsync(userId, 1, "LeaveRequests", newId.ToString(),
                moduleName: "SCR_LEAVE",
                description: $"طلب إجازة {item.RequestNumber}: موظف {item.EmployeeID} - {item.NumberOfDays} يوم");

            // إشعار للمدير العام
            var empName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
                new { ID = item.EmployeeID });

            await _notif.CreateNotificationAsync(
                notificationType: 4,
                title: "طلب إجازة جديد بانتظار الاعتماد",
                message: $"طلب إجازة من {empName} - {leaveType.LeaveTypeNameAr} - {item.NumberOfDays} يوم ({item.StartDate:dd/MM/yyyy} إلى {item.EndDate:dd/MM/yyyy})",
                priority: 2,
                targetRoleId: 2,
                relatedModule: "SCR_LEAVE",
                relatedRecordId: newId,
                createdBy: userId);

            return newId;
        }

        // ========================================
        // اعتماد المدير
        // ========================================
        public async Task ApproveByManagerAsync(int id, int userId)
        {
            using var connection = CreateConnection();

            var request = await connection.QueryFirstOrDefaultAsync<LeaveRequestActionInfo>(
                @"SELECT LeaveRequestID, RequestNumber, EmployeeID, LeaveTypeID,
                         NumberOfDays, RequestStatus, CreatedBy
                  FROM dbo.LeaveRequests WHERE LeaveRequestID = @ID",
                new { ID = id });

            if (request == null)
                throw new Exception("الطلب غير موجود");

            if (request.RequestStatus != 1)
                throw new Exception("لا يمكن اعتماد طلب ليس في حالة (مقدم)");

            var employeeId = await GetEmployeeIdFromUserAsync(connection, userId);

            await connection.ExecuteAsync(@"
                UPDATE dbo.LeaveRequests SET
                    RequestStatus = 2,
                    ApprovedByManager = @ApprovedBy,
                    ManagerApprovalDate = GETDATE(),
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE LeaveRequestID = @ID",
                new
                {
                    ID = id,
                    ApprovedBy = employeeId,
                    ModifiedBy = userId
                });

            await _audit.WriteAuditLogAsync(userId, 2, "LeaveRequests", id.ToString(),
                moduleName: "SCR_LEAVE",
                description: $"اعتماد مدير لطلب إجازة #{request.RequestNumber}");

            // إشعار لـ HR للاعتماد النهائي
            var empName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
                new { ID = request.EmployeeID });

            await _notif.CreateNotificationAsync(
                notificationType: 4,
                title: "طلب إجازة بانتظار اعتماد HR",
                message: $"طلب إجازة {request.RequestNumber} من {empName} - {request.NumberOfDays} يوم - تم اعتماده من المدير وبانتظار اعتماد HR",
                priority: 2,
                targetRoleId: 2,
                relatedModule: "SCR_LEAVE",
                relatedRecordId: id,
                createdBy: userId);
        }

        // ========================================
        // اعتماد HR (الاعتماد النهائي)
        // ========================================
        public async Task ApproveByHRAsync(int id, int userId)
        {
            using var connection = CreateConnection();

            var request = await connection.QueryFirstOrDefaultAsync<LeaveRequestActionInfo>(
                @"SELECT LeaveRequestID, RequestNumber, EmployeeID, LeaveTypeID,
                         NumberOfDays, RequestStatus, CreatedBy
                  FROM dbo.LeaveRequests WHERE LeaveRequestID = @ID",
                new { ID = id });

            if (request == null)
                throw new Exception("الطلب غير موجود");

            if (request.RequestStatus != 2)
                throw new Exception("لا يمكن اعتماد HR لطلب لم يعتمده المدير أولاً");

            var employeeId = await GetEmployeeIdFromUserAsync(connection, userId);

            await connection.ExecuteAsync(@"
                UPDATE dbo.LeaveRequests SET
                    RequestStatus = 3,
                    ApprovedByHR = @ApprovedBy,
                    HRApprovalDate = GETDATE(),
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE LeaveRequestID = @ID",
                new
                {
                    ID = id,
                    ApprovedBy = employeeId,
                    ModifiedBy = userId
                });

            // تحديث رصيد الإجازات: نقل من Pending إلى Used
            var leaveType = await connection.QueryFirstOrDefaultAsync<LeaveTypeInfo>(
                @"SELECT LeaveTypeID, LeaveTypeNameAr, IsPaid, MaxDaysPerYear,
                         DeductFromBalance, AllowNegativeBalance
                  FROM dbo.LeaveTypes WHERE LeaveTypeID = @ID",
                new { ID = request.LeaveTypeID });

            if (leaveType != null && leaveType.DeductFromBalance)
            {
                var startDate = await connection.QueryFirstOrDefaultAsync<DateTime>(
                    "SELECT StartDate FROM dbo.LeaveRequests WHERE LeaveRequestID = @ID",
                    new { ID = id });

                var fiscalYearId = await GetCurrentFiscalYearIdAsync(connection, startDate);

                if (fiscalYearId > 0)
                {
                    await connection.ExecuteAsync(@"
                        UPDATE dbo.EmployeeLeaveBalances
                        SET PendingDays = CASE
                                WHEN ISNULL(PendingDays, 0) - @Days < 0 THEN 0
                                ELSE ISNULL(PendingDays, 0) - @Days
                            END,
                            UsedDays = ISNULL(UsedDays, 0) + @Days,
                            ModifiedDate = GETDATE()
                        WHERE EmployeeID = @EmployeeID
                          AND LeaveTypeID = @LeaveTypeID
                          AND FiscalYearID = @FiscalYearID",
                        new
                        {
                            Days = request.NumberOfDays,
                            EmployeeID = request.EmployeeID,
                            LeaveTypeID = request.LeaveTypeID,
                            FiscalYearID = fiscalYearId
                        });
                }
            }

            await _audit.WriteAuditLogAsync(userId, 2, "LeaveRequests", id.ToString(),
                moduleName: "SCR_LEAVE",
                description: $"اعتماد HR النهائي لطلب إجازة #{request.RequestNumber}");

            // تحديد الإشعارات القديمة كتم تنفيذها
            await _notif.MarkRelatedAsActionedAsync("SCR_LEAVE", id);

            // إشعار رد للموظف اللي قدّم الطلب
            if (request.CreatedBy > 0)
            {
                var empName = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
                    new { ID = request.EmployeeID });

                await _notif.CreateNotificationAsync(
                    notificationType: 3,
                    title: "تم اعتماد طلب الإجازة",
                    message: $"تم اعتماد طلب الإجازة {request.RequestNumber} للموظف {empName} - {request.NumberOfDays} يوم",
                    priority: 2,
                    targetUserId: request.CreatedBy,
                    relatedModule: "SCR_LEAVE",
                    relatedRecordId: id,
                    createdBy: userId);
            }
        }

        // ========================================
        // رفض الطلب
        // ========================================
        public async Task RejectLeaveRequestAsync(int id, int userId, string reason)
        {
            using var connection = CreateConnection();

            var request = await connection.QueryFirstOrDefaultAsync<LeaveRequestActionInfo>(
                @"SELECT LeaveRequestID, RequestNumber, EmployeeID, LeaveTypeID,
                         NumberOfDays, RequestStatus, CreatedBy
                  FROM dbo.LeaveRequests WHERE LeaveRequestID = @ID",
                new { ID = id });

            if (request == null)
                throw new Exception("الطلب غير موجود");

            if (request.RequestStatus != 1 && request.RequestStatus != 2)
                throw new Exception("لا يمكن رفض طلب ليس في حالة (مقدم) أو (معتمد مدير)");

            var employeeId = await GetEmployeeIdFromUserAsync(connection, userId);

            await connection.ExecuteAsync(@"
                UPDATE dbo.LeaveRequests SET
                    RequestStatus = 4,
                    RejectionReason = @Reason,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE LeaveRequestID = @ID",
                new
                {
                    ID = id,
                    Reason = reason,
                    ModifiedBy = userId
                });

            // إرجاع PendingDays
            await RestorePendingDaysAsync(connection, request);

            await _audit.WriteAuditLogAsync(userId, 2, "LeaveRequests", id.ToString(),
                moduleName: "SCR_LEAVE",
                description: $"رفض طلب إجازة #{request.RequestNumber} - السبب: {reason}");

            // تحديد الإشعارات القديمة كتم تنفيذها
            await _notif.MarkRelatedAsActionedAsync("SCR_LEAVE", id);

            // إشعار رد للموظف اللي قدّم الطلب
            if (request.CreatedBy > 0)
            {
                var empName = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT FullNameAr FROM dbo.Employees WHERE EmployeeID = @ID",
                    new { ID = request.EmployeeID });

                await _notif.CreateNotificationAsync(
                    notificationType: 4,
                    title: "تم رفض طلب الإجازة",
                    message: $"تم رفض طلب الإجازة {request.RequestNumber} للموظف {empName} - السبب: {reason}",
                    priority: 2,
                    targetUserId: request.CreatedBy,
                    relatedModule: "SCR_LEAVE",
                    relatedRecordId: id,
                    createdBy: userId);
            }
        }

        // ========================================
        // إلغاء الطلب
        // ========================================
        public async Task CancelLeaveRequestAsync(int id, int userId)
        {
            using var connection = CreateConnection();

            var request = await connection.QueryFirstOrDefaultAsync<LeaveRequestActionInfo>(
                @"SELECT LeaveRequestID, RequestNumber, EmployeeID, LeaveTypeID,
                         NumberOfDays, RequestStatus, CreatedBy
                  FROM dbo.LeaveRequests WHERE LeaveRequestID = @ID",
                new { ID = id });

            if (request == null)
                throw new Exception("الطلب غير موجود");

            if (request.RequestStatus >= 3)
                throw new Exception("لا يمكن إلغاء طلب تم اعتماده نهائيًا أو رفضه");

            await connection.ExecuteAsync(@"
                UPDATE dbo.LeaveRequests SET
                    RequestStatus = 5,
                    ModifiedBy = @ModifiedBy,
                    ModifiedDate = GETDATE()
                WHERE LeaveRequestID = @ID",
                new { ID = id, ModifiedBy = userId });

            // إرجاع PendingDays
            await RestorePendingDaysAsync(connection, request);

            await _audit.WriteAuditLogAsync(userId, 2, "LeaveRequests", id.ToString(),
                moduleName: "SCR_LEAVE",
                description: $"إلغاء طلب إجازة #{request.RequestNumber}");

            await _notif.MarkRelatedAsActionedAsync("SCR_LEAVE", id);
        }

        // ========================================
        // جلب أرصدة إجازات موظف
        // ========================================
        public async Task<List<LeaveBalanceDto>> GetLeaveBalancesAsync(int employeeId)
        {
            using var connection = CreateConnection();

            var sql = @"
                SELECT elb.EmployeeID, elb.LeaveTypeID,
                    lt.LeaveTypeNameAr AS LeaveTypeName,
                    elb.EntitledDays, elb.CarriedForward, elb.AdditionalDays,
                    elb.TotalEntitled, elb.UsedDays, elb.PendingDays, elb.RemainingDays
                FROM dbo.EmployeeLeaveBalances elb
                INNER JOIN dbo.LeaveTypes lt ON elb.LeaveTypeID = lt.LeaveTypeID
                INNER JOIN dbo.FiscalYears fy ON elb.FiscalYearID = fy.FiscalYearID
                WHERE elb.EmployeeID = @EmployeeID
                  AND fy.IsCurrent = 1
                ORDER BY lt.LeaveTypeNameAr";

            var result = await connection.QueryAsync<LeaveBalanceDto>(sql, new { EmployeeID = employeeId });
            return result.ToList();
        }

        // ========================================
        // تصدير Excel
        // ========================================
        public async Task<byte[]> ExportLeaveRequestsToExcelAsync(List<LeaveRequestListDto> items, int userId)
        {
            await Task.CompletedTask;

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("طلبات الإجازات");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "تقرير طلبات الإجازات — واي كي كوتينج";
            ws.Range(1, 1, 1, 8).Merge().Style.Font.Bold = true;

            var headers = new[] { "#", "رقم الطلب", "الموظف", "النوع", "من", "إلى", "الأيام", "الحالة" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1d143f");
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            }

            int row = 4, num = 0;
            foreach (var item in items)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = item.RequestNumber ?? "";
                ws.Cell(row, 3).Value = item.EmployeeName ?? "";
                ws.Cell(row, 4).Value = item.LeaveTypeName ?? "";
                ws.Cell(row, 5).Value = item.StartDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 6).Value = item.EndDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 7).Value = item.NumberOfDays;
                ws.Cell(row, 8).Value = item.StatusName ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ========================================
        // دوال مساعدة داخلية
        // ========================================

        private async Task<int> GetEmployeeIdFromUserAsync(System.Data.IDbConnection connection, int userId)
        {
            var employeeId = await connection.ExecuteScalarAsync<int?>(
                "SELECT EmployeeID FROM dbo.SystemUsers WHERE UserID = @UserID",
                new { UserID = userId });

            if (employeeId == null || employeeId == 0)
                throw new Exception("لم يتم العثور على بيانات الموظف المرتبط بحسابك");

            return employeeId.Value;
        }

        private async Task<int> GetCurrentFiscalYearIdAsync(System.Data.IDbConnection connection, DateTime date)
        {
            // أولاً: البحث بالتاريخ
            var fiscalYearId = await connection.ExecuteScalarAsync<int?>(
                @"SELECT TOP 1 FiscalYearID
                  FROM dbo.FiscalYears
                  WHERE @Date BETWEEN StartDate AND EndDate
                    AND YearStatus = 1
                  ORDER BY StartDate DESC",
                new { Date = date });

            if (fiscalYearId.HasValue)
                return fiscalYearId.Value;

            // ثانياً: السنة الحالية
            fiscalYearId = await connection.ExecuteScalarAsync<int?>(
                @"SELECT TOP 1 FiscalYearID
                  FROM dbo.FiscalYears
                  WHERE IsCurrent = 1
                    AND YearStatus = 1");

            return fiscalYearId ?? 0;
        }

        private async Task RestorePendingDaysAsync(System.Data.IDbConnection connection, LeaveRequestActionInfo request)
        {
            var leaveType = await connection.QueryFirstOrDefaultAsync<LeaveTypeInfo>(
                @"SELECT LeaveTypeID, DeductFromBalance
                  FROM dbo.LeaveTypes WHERE LeaveTypeID = @ID",
                new { ID = request.LeaveTypeID });

            if (leaveType != null && leaveType.DeductFromBalance)
            {
                var startDate = await connection.QueryFirstOrDefaultAsync<DateTime>(
                    "SELECT StartDate FROM dbo.LeaveRequests WHERE LeaveRequestID = @ID",
                    new { ID = request.LeaveRequestID });

                var fiscalYearId = await GetCurrentFiscalYearIdAsync(connection, startDate);

                if (fiscalYearId > 0)
                {
                    await connection.ExecuteAsync(@"
                        UPDATE dbo.EmployeeLeaveBalances
                        SET PendingDays = CASE
                                WHEN ISNULL(PendingDays, 0) - @Days < 0 THEN 0
                                ELSE ISNULL(PendingDays, 0) - @Days
                            END,
                            ModifiedDate = GETDATE()
                        WHERE EmployeeID = @EmployeeID
                          AND LeaveTypeID = @LeaveTypeID
                          AND FiscalYearID = @FiscalYearID",
                        new
                        {
                            Days = request.NumberOfDays,
                            EmployeeID = request.EmployeeID,
                            LeaveTypeID = request.LeaveTypeID,
                            FiscalYearID = fiscalYearId
                        });
                }
            }
        }

        // ========================================
        // Classes داخلية
        // ========================================

        private sealed class LeaveTypeInfo
        {
            public int LeaveTypeID { get; set; }
            public string? LeaveTypeNameAr { get; set; }
            public bool IsPaid { get; set; }
            public int MaxDaysPerYear { get; set; }
            public bool DeductFromBalance { get; set; }
            public bool AllowNegativeBalance { get; set; }
        }

        private sealed class LeaveBalanceInfo
        {
            public int BalanceID { get; set; }
            public decimal TotalEntitled { get; set; }
            public decimal UsedDays { get; set; }
            public decimal PendingDays { get; set; }
            public decimal RemainingDays { get; set; }
        }

        private sealed class LeaveRequestActionInfo
        {
            public int LeaveRequestID { get; set; }
            public string? RequestNumber { get; set; }
            public int EmployeeID { get; set; }
            public int LeaveTypeID { get; set; }
            public int NumberOfDays { get; set; }
            public int RequestStatus { get; set; }
            public int CreatedBy { get; set; }
        }
    }
}