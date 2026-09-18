using Dapper;
using YKCoatings.Models;

namespace YKCoatings.Services
{
    public class ContractManufacturingService : BaseDbService
    {
        private readonly AuditService _audit;
        private readonly NotificationService _notification;

        public ContractManufacturingService(
            IConfiguration configuration,
            AuditService audit,
            NotificationService notification) : base(configuration)
        {
            _audit = audit;
            _notification = notification;
        }

        // ==========================================
        // إنشاء رقم عقد جديد
        // ==========================================
        public async Task<string> GenerateContractNumberAsync()
        {
            using var connection = CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("@Prefix", "CO");
            parameters.Add("@NextNum", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

            await connection.ExecuteAsync(
                "EXEC dbo.sp_GetNextNumber @Prefix, @NextNum OUTPUT",
                parameters,
                commandTimeout: 30);

            var nextNum = parameters.Get<int>("@NextNum");
            return $"CO-{nextNum:D6}";
        }

        // ==========================================
        // قائمة عقود التصنيع
        // ==========================================
        public async Task<List<ContractOrderListDto>> GetAllAsync(
            string? search = null,
            int? statusFilter = null,
            int? customerIdFilter = null)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    co.ContractOrderID,
    co.ContractNumber,
    co.ContractDate,
    c.CustomerNameAr AS CustomerName,
    co.ContractStatus,
    CASE co.ContractStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'جاري التنفيذ'
        WHEN 4 THEN N'مكتمل'
        WHEN 5 THEN N'ملغي'
    END AS ContractStatusName,
    co.MaterialSource,
    CASE co.MaterialSource
        WHEN 1 THEN N'المصنع يوفر الخامات'
        WHEN 2 THEN N'العميل يوفر الخامات'
        WHEN 3 THEN N'مشترك'
    END AS MaterialSourceName,
    co.DeliveryDate,
    co.TotalContractValue,
    (SELECT COUNT(*) FROM ContractOrderDetails cod WHERE cod.ContractOrderID = co.ContractOrderID) AS TotalLines,
    (SELECT COUNT(*) FROM ContractOrderDetails cod WHERE cod.ContractOrderID = co.ContractOrderID AND cod.LineStatus = 3) AS CompletedLines,
    ISNULL(uc.Username, N'') AS CreatedByName
FROM dbo.ContractOrders co
INNER JOIN dbo.Customers c ON co.CustomerID = c.CustomerID
LEFT JOIN dbo.SystemUsers uc ON co.CreatedBy = uc.UserID
WHERE 1=1";

            if (!string.IsNullOrWhiteSpace(search))
                sql += " AND (co.ContractNumber LIKE @Search OR c.CustomerNameAr LIKE @Search OR co.CustomerPONumber LIKE @Search)";

            if (statusFilter.HasValue)
                sql += " AND co.ContractStatus = @StatusFilter";

            if (customerIdFilter.HasValue)
                sql += " AND co.CustomerID = @CustomerIdFilter";

            sql += " ORDER BY co.ContractDate DESC, co.ContractOrderID DESC";

            return (await connection.QueryAsync<ContractOrderListDto>(sql, new
            {
                Search = $"%{search}%",
                StatusFilter = statusFilter,
                CustomerIdFilter = customerIdFilter
            })).ToList();
        }

        // ==========================================
        // جلب عقد بالكامل للتعديل
        // ==========================================
        public async Task<ContractOrderEditDto?> GetByIdAsync(int contractOrderId)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    co.ContractOrderID,
    co.ContractNumber,
    co.ContractDate,
    co.CustomerID,
    c.CustomerNameAr AS CustomerName,
    co.MaterialSource,
    co.ContractStatus,
    CASE co.ContractStatus
        WHEN 1 THEN N'مسودة'
        WHEN 2 THEN N'معتمد'
        WHEN 3 THEN N'جاري التنفيذ'
        WHEN 4 THEN N'مكتمل'
        WHEN 5 THEN N'ملغي'
    END AS ContractStatusName,
    co.DeliveryDate,
    ISNULL(co.CustomerPONumber, N'') AS CustomerPONumber,
    co.TotalManufacturingFee,
    co.TotalMaterialCost,
    co.TotalContractValue,
    ISNULL(co.Notes, N'') AS Notes,
    co.ApprovedBy,
    ISNULL(ea.FullNameAr, N'') AS ApprovedByName,
    co.ApprovedDate
FROM dbo.ContractOrders co
INNER JOIN dbo.Customers c ON co.CustomerID = c.CustomerID
LEFT JOIN dbo.Employees ea ON co.ApprovedBy = ea.EmployeeID
WHERE co.ContractOrderID = @ContractOrderID";

            var result = await connection.QueryFirstOrDefaultAsync<ContractOrderEditDto>(sql, new
            {
                ContractOrderID = contractOrderId
            });

            if (result is null) return null;

            // جلب التفاصيل
            var detailsSql = @"
SELECT
    cod.ContractDetailID,
    cod.ContractOrderID,
    cod.LineNumber,
    cod.ProductItemID,
    ISNULL(i.ItemCode, N'') AS ProductCode,
    ISNULL(i.ItemNameAr, N'') AS ProductName,
    cod.BOMID,
    ISNULL(b.BOMName, N'') AS BOMName,
    cod.OrderedQty,
    cod.UnitID,
    ISNULL(u.UnitNameAr, N'') AS UnitName,
    ISNULL(cod.ProducedQty, 0) AS ProducedQty,
    ISNULL(cod.DeliveredQty, 0) AS DeliveredQty,
    ISNULL(cod.ManufacturingFeePerUnit, 0) AS ManufacturingFeePerUnit,
    ISNULL(cod.TotalManufacturingFee, 0) AS TotalManufacturingFee,
    ISNULL(cod.MaterialCostPerUnit, 0) AS MaterialCostPerUnit,
    ISNULL(cod.TotalMaterialCost, 0) AS TotalMaterialCost,
    ISNULL(cod.LineTotal, 0) AS LineTotal,
    cod.ProductionOrderID,
    ISNULL(po.OrderNumber, N'') AS ProductionOrderNumber,
    cod.LineStatus,
    CASE cod.LineStatus
        WHEN 1 THEN N'في الانتظار'
        WHEN 2 THEN N'جاري التصنيع'
        WHEN 3 THEN N'مكتمل'
        WHEN 4 THEN N'ملغي'
    END AS LineStatusName,
    ISNULL(cod.Notes, N'') AS Notes
FROM dbo.ContractOrderDetails cod
INNER JOIN dbo.Items i ON cod.ProductItemID = i.ItemID
INNER JOIN dbo.Units u ON cod.UnitID = u.UnitID
LEFT JOIN dbo.BillOfMaterials b ON cod.BOMID = b.BOMID
LEFT JOIN dbo.ProductionOrders po ON cod.ProductionOrderID = po.ProductionOrderID
WHERE cod.ContractOrderID = @ContractOrderID
ORDER BY cod.LineNumber";

            result.Details = (await connection.QueryAsync<ContractOrderDetailDto>(detailsSql, new
            {
                ContractOrderID = contractOrderId
            })).ToList();

            return result;
        }

        // ==========================================
        // إدراج عقد جديد
        // ==========================================
        public async Task<int> InsertAsync(ContractOrderEditDto contract, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var contractNumber = await GenerateContractNumberAsync();

                var sql = @"
INSERT INTO dbo.ContractOrders
(
    ContractNumber, ContractDate, CustomerID, MaterialSource,
    ContractStatus, DeliveryDate, CustomerPONumber,
    TotalManufacturingFee, TotalMaterialCost, TotalContractValue,
    Notes, CreatedBy, CreatedDate
)
VALUES
(
    @ContractNumber, @ContractDate, @CustomerID, @MaterialSource,
    1, @DeliveryDate, @CustomerPONumber,
    @TotalManufacturingFee, @TotalMaterialCost, @TotalContractValue,
    @Notes, @UserID, GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                var newId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    ContractNumber = contractNumber,
                    contract.ContractDate,
                    contract.CustomerID,
                    contract.MaterialSource,
                    contract.DeliveryDate,
                    contract.CustomerPONumber,
                    contract.TotalManufacturingFee,
                    contract.TotalMaterialCost,
                    contract.TotalContractValue,
                    contract.Notes,
                    UserID = userId
                }, transaction);

                // إدراج التفاصيل
                if (contract.Details.Any())
                {
                    var lineNum = 0;
                    foreach (var line in contract.Details)
                    {
                        lineNum++;
                        var detailSql = @"
INSERT INTO dbo.ContractOrderDetails
(
    ContractOrderID, LineNumber, ProductItemID, BOMID,
    OrderedQty, UnitID,
    ManufacturingFeePerUnit, TotalManufacturingFee,
    MaterialCostPerUnit, TotalMaterialCost,
    LineTotal, LineStatus, Notes
)
VALUES
(
    @ContractOrderID, @LineNumber, @ProductItemID, @BOMID,
    @OrderedQty, @UnitID,
    @ManufacturingFeePerUnit, @TotalManufacturingFee,
    @MaterialCostPerUnit, @TotalMaterialCost,
    @LineTotal, 1, @Notes
);";

                        await connection.ExecuteAsync(detailSql, new
                        {
                            ContractOrderID = newId,
                            LineNumber = lineNum,
                            line.ProductItemID,
                            line.BOMID,
                            line.OrderedQty,
                            line.UnitID,
                            line.ManufacturingFeePerUnit,
                            line.TotalManufacturingFee,
                            line.MaterialCostPerUnit,
                            line.TotalMaterialCost,
                            line.LineTotal,
                            line.Notes
                        }, transaction);
                    }
                }

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 1,
                    tableName: "ContractOrders",
                    recordId: newId.ToString(),
                    moduleName: "SCR_CONTRACT",
                    description: $"إنشاء عقد تصنيع جديد: {contractNumber}"
                );

                return newId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // تحديث عقد موجود
        // ==========================================
        public async Task UpdateAsync(ContractOrderEditDto contract, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var sql = @"
UPDATE dbo.ContractOrders
SET
    CustomerID = @CustomerID,
    MaterialSource = @MaterialSource,
    DeliveryDate = @DeliveryDate,
    CustomerPONumber = @CustomerPONumber,
    TotalManufacturingFee = @TotalManufacturingFee,
    TotalMaterialCost = @TotalMaterialCost,
    TotalContractValue = @TotalContractValue,
    Notes = @Notes,
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ContractOrderID = @ContractOrderID
  AND ContractStatus = 1;";

                var affected = await connection.ExecuteAsync(sql, new
                {
                    contract.CustomerID,
                    contract.MaterialSource,
                    contract.DeliveryDate,
                    contract.CustomerPONumber,
                    contract.TotalManufacturingFee,
                    contract.TotalMaterialCost,
                    contract.TotalContractValue,
                    contract.Notes,
                    UserID = userId,
                    contract.ContractOrderID
                }, transaction);

                if (affected == 0)
                    throw new Exception("لا يمكن تعديل العقد في الحالة الحالية");

                // حذف التفاصيل القديمة وإدراج الجديدة
                await connection.ExecuteAsync(
                    "DELETE FROM dbo.ContractOrderDetails WHERE ContractOrderID = @ContractOrderID",
                    new { contract.ContractOrderID }, transaction);

                if (contract.Details.Any())
                {
                    var lineNum = 0;
                    foreach (var line in contract.Details)
                    {
                        lineNum++;
                        var detailSql = @"
INSERT INTO dbo.ContractOrderDetails
(
    ContractOrderID, LineNumber, ProductItemID, BOMID,
    OrderedQty, UnitID,
    ManufacturingFeePerUnit, TotalManufacturingFee,
    MaterialCostPerUnit, TotalMaterialCost,
    LineTotal, LineStatus, Notes
)
VALUES
(
    @ContractOrderID, @LineNumber, @ProductItemID, @BOMID,
    @OrderedQty, @UnitID,
    @ManufacturingFeePerUnit, @TotalManufacturingFee,
    @MaterialCostPerUnit, @TotalMaterialCost,
    @LineTotal, 1, @Notes
);";

                        await connection.ExecuteAsync(detailSql, new
                        {
                            contract.ContractOrderID,
                            LineNumber = lineNum,
                            line.ProductItemID,
                            line.BOMID,
                            line.OrderedQty,
                            line.UnitID,
                            line.ManufacturingFeePerUnit,
                            line.TotalManufacturingFee,
                            line.MaterialCostPerUnit,
                            line.TotalMaterialCost,
                            line.LineTotal,
                            line.Notes
                        }, transaction);
                    }
                }

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "ContractOrders",
                    recordId: contract.ContractOrderID.ToString(),
                    moduleName: "SCR_CONTRACT",
                    description: $"تعديل عقد تصنيع: {contract.ContractNumber}"
                );
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // اعتماد العقد
        // ==========================================
        public async Task<(bool Success, string Message)> ApproveAsync(int contractOrderId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var contract = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT ContractOrderID, ContractNumber, ContractStatus
FROM dbo.ContractOrders
WHERE ContractOrderID = @ContractOrderID",
                    new { ContractOrderID = contractOrderId }, transaction);

                if (contract is null)
                    return (false, "العقد غير موجود");

                if ((int)contract.ContractStatus != 1)
                    return (false, "لا يمكن اعتماد العقد إلا من حالة مسودة");

                await connection.ExecuteAsync(@"
UPDATE dbo.ContractOrders
SET ContractStatus = 2,
    ApprovedBy = @UserID,
    ApprovedDate = GETDATE(),
    ModifiedBy = @UserID,
    ModifiedDate = GETDATE()
WHERE ContractOrderID = @ContractOrderID",
                    new { ContractOrderID = contractOrderId, UserID = userId }, transaction);

                transaction.Commit();

                await _audit.WriteAuditLogAsync(
                    userId: userId,
                    actionType: 2,
                    tableName: "ContractOrders",
                    recordId: contractOrderId.ToString(),
                    changedColumns: "ContractStatus,ApprovedBy,ApprovedDate",
                    moduleName: "SCR_CONTRACT",
                    description: $"اعتماد عقد تصنيع: {(string)contract.ContractNumber}"
                );

                return (true, "تم اعتماد العقد بنجاح");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return (false, ex.Message);
            }
        }
                // ==========================================
        // إضافة سطر منتج
        // ==========================================
        public async Task AddProductLineAsync(int contractOrderId, ContractOrderDetailDto line, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var lineNum = await connection.ExecuteScalarAsync<int>(@"
SELECT ISNULL(MAX(LineNumber), 0) + 1
FROM dbo.ContractOrderDetails
WHERE ContractOrderID = @ContractOrderID",
                    new { ContractOrderID = contractOrderId }, transaction);

                line.TotalManufacturingFee = line.OrderedQty * line.ManufacturingFeePerUnit;
                line.TotalMaterialCost = line.OrderedQty * line.MaterialCostPerUnit;
                line.LineTotal = line.TotalManufacturingFee + line.TotalMaterialCost;

                await connection.ExecuteAsync(@"
INSERT INTO dbo.ContractOrderDetails
(ContractOrderID, LineNumber, ProductItemID, BOMID,
 OrderedQty, UnitID,
 ManufacturingFeePerUnit, TotalManufacturingFee,
 MaterialCostPerUnit, TotalMaterialCost,
 LineTotal, LineStatus)
VALUES
(@ContractOrderID, @LineNumber, @ProductItemID, @BOMID,
 @OrderedQty, @UnitID,
 @ManufacturingFeePerUnit, @TotalManufacturingFee,
 @MaterialCostPerUnit, @TotalMaterialCost,
 @LineTotal, 1)",
                    new
                    {
                        ContractOrderID = contractOrderId,
                        LineNumber = lineNum,
                        line.ProductItemID,
                        BOMID = (int?)line.BOMID,
                        line.OrderedQty,
                        line.UnitID,
                        line.ManufacturingFeePerUnit,
                        line.TotalManufacturingFee,
                        line.MaterialCostPerUnit,
                        line.TotalMaterialCost,
                        line.LineTotal
                    }, transaction);

                await UpdateContractTotals(connection, transaction, contractOrderId);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // حذف سطر منتج
        // ==========================================
        public async Task DeleteProductLineAsync(int contractDetailId, int contractOrderId, int userId)
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var hasProdOrder = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM dbo.ContractOrderDetails
WHERE ContractDetailID = @ContractDetailID
  AND ProductionOrderID IS NOT NULL",
                    new { ContractDetailID = contractDetailId }, transaction);

                if (hasProdOrder > 0)
                    throw new Exception("لا يمكن حذف منتج مرتبط بأمر تصنيع");

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.ContractOrderDetails WHERE ContractDetailID = @ContractDetailID",
                    new { ContractDetailID = contractDetailId }, transaction);

                await UpdateContractTotals(connection, transaction, contractOrderId);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ==========================================
        // تحديث إجماليات العقد
        // ==========================================
                private async Task UpdateContractTotals(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int contractOrderId)
        {
            await connection.ExecuteAsync(@"
UPDATE co
SET co.TotalManufacturingFee = ISNULL(ag.TotalFee, 0),
    co.TotalMaterialCost = ISNULL(ag.TotalMaterial, 0),
    co.TotalContractValue = ISNULL(ag.TotalValue, 0)
FROM dbo.ContractOrders co
INNER JOIN (
    SELECT ContractOrderID,
           SUM(TotalManufacturingFee) AS TotalFee,
           SUM(TotalMaterialCost) AS TotalMaterial,
           SUM(LineTotal) AS TotalValue
    FROM dbo.ContractOrderDetails
    WHERE ContractOrderID = @ContractOrderID
    GROUP BY ContractOrderID
) ag ON co.ContractOrderID = ag.ContractOrderID
WHERE co.ContractOrderID = @ContractOrderID",
                new { ContractOrderID = contractOrderId }, transaction);
        }

        // ==========================================
        // جلب تكلفة خامات الوصفة
        // ==========================================
        public async Task<decimal> GetBOMMaterialCostAsync(int bomId)
        {
            using var connection = CreateConnection();

            var sql = @"
SELECT
    ISNULL(SUM(bd.Quantity * (1 + ISNULL(bd.WastePercent, 0) / 100) * ISNULL(bd.UnitCost, 0)), 0) / NULLIF(b.OutputQty, 0)
FROM dbo.BillOfMaterials b
LEFT JOIN dbo.BOMDetails bd ON b.BOMID = bd.BOMID
WHERE b.BOMID = @BOMID
GROUP BY b.OutputQty";

            var result = await connection.ExecuteScalarAsync<decimal?>(sql, new { BOMID = bomId });
            return result ?? 0;
        }

        
        
    }
}