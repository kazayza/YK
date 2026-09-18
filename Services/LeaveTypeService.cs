using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class LeaveTypeService : BaseDbService
    {
        private readonly AuditService _audit;

        public LeaveTypeService(IConfiguration configuration, AuditService audit) : base(configuration)
        {
            _audit = audit;
        }

        public async Task<List<LeaveTypeListDto>> GetLeaveTypesListAsync()
        {
            using var connection = CreateConnection();
            var sql = @"SELECT LeaveTypeID, LeaveTypeCode, LeaveTypeNameAr, LeaveTypeNameEn,
                               IsPaid, MaxDaysPerYear, RequiresApproval, DeductFromBalance,
                               AllowNegativeBalance, Color, IsActive
                        FROM dbo.LeaveTypes ORDER BY LeaveTypeNameAr";
            var result = await connection.QueryAsync<LeaveTypeListDto>(sql);
            return result.ToList();
        }

        public async Task<LeaveTypeEditDto?> GetLeaveTypeByIdAsync(int id)
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<LeaveTypeEditDto>(
                @"SELECT LeaveTypeID, LeaveTypeCode, LeaveTypeNameAr, LeaveTypeNameEn,
                         IsPaid, MaxDaysPerYear, RequiresApproval, DeductFromBalance,
                         AllowNegativeBalance, Color, IsActive
                  FROM dbo.LeaveTypes WHERE LeaveTypeID = @ID", new { ID = id });
        }

        public async Task<int> InsertLeaveTypeAsync(LeaveTypeEditDto item, int userId)
        {
            using var connection = CreateConnection();
            if (string.IsNullOrWhiteSpace(item.LeaveTypeNameAr))
                throw new Exception("اسم الإجازة مطلوب");

            var sql = @"
                INSERT INTO dbo.LeaveTypes (LeaveTypeCode, LeaveTypeNameAr, LeaveTypeNameEn,
                    IsPaid, MaxDaysPerYear, RequiresApproval, DeductFromBalance,
                    AllowNegativeBalance, Color, IsActive)
                VALUES (@LeaveTypeCode, @LeaveTypeNameAr, @LeaveTypeNameEn,
                    @IsPaid, @MaxDaysPerYear, @RequiresApproval, @DeductFromBalance,
                    @AllowNegativeBalance, @Color, @IsActive);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var newId = await connection.ExecuteScalarAsync<int>(sql, item);

            await _audit.WriteAuditLogAsync(userId, 1, "LeaveTypes", newId.ToString(),
                moduleName: "SCR_LEAVE", description: $"إضافة نوع إجازة: {item.LeaveTypeNameAr}");

            return newId;
        }

        public async Task UpdateLeaveTypeAsync(LeaveTypeEditDto item, int userId)
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(@"
                UPDATE dbo.LeaveTypes SET
                    LeaveTypeCode = @LeaveTypeCode,
                    LeaveTypeNameAr = @LeaveTypeNameAr,
                    LeaveTypeNameEn = @LeaveTypeNameEn,
                    IsPaid = @IsPaid, MaxDaysPerYear = @MaxDaysPerYear,
                    RequiresApproval = @RequiresApproval,
                    DeductFromBalance = @DeductFromBalance,
                    AllowNegativeBalance = @AllowNegativeBalance,
                    Color = @Color, IsActive = @IsActive
                WHERE LeaveTypeID = @LeaveTypeID", item);

            await _audit.WriteAuditLogAsync(userId, 2, "LeaveTypes", item.LeaveTypeID.ToString(),
                moduleName: "SCR_LEAVE", description: $"تعديل نوع إجازة: {item.LeaveTypeNameAr}");
        }

        public async Task<byte[]> ExportLeaveTypesToExcelAsync(List<LeaveTypeListDto> items, int userId)
        {
            await Task.CompletedTask;
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("أنواع الإجازات");
            ws.RightToLeft = true;
            ws.Style.Font.FontName = "Cairo";
            ws.Style.Font.FontSize = 11;

            ws.Cell(1, 1).Value = "أنواع الإجازات — واي كي كوتينج";
            ws.Range(1, 1, 1, 6).Merge().Style.Font.Bold = true;

            var headers = new[] { "#", "الكود", "الاسم", "مدفوعة", "الحد الأقصى", "تخصم من الرصيد" };
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
                ws.Cell(row, 2).Value = item.LeaveTypeCode ?? "";
                ws.Cell(row, 3).Value = item.LeaveTypeNameAr ?? "";
                ws.Cell(row, 4).Value = item.IsPaid ? "نعم" : "لا";
                ws.Cell(row, 5).Value = item.MaxDaysPerYear;
                ws.Cell(row, 6).Value = item.DeductFromBalance ? "نعم" : "لا";
                row++;
            }
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}