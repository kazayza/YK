-- YKCoatings — مستخرج آليًا من القاعدة الحية بتاريخ 2026-11-24 02:19
-- المصدر: الخادم db68995.public.databaseasp.net / القاعدة db68995
-- أعد توليده بتشغيل database/extract-schema.ps1 بعد أي تغيير في القاعدة.

CREATE TABLE [dbo].[AccountingPeriods] (
    [PeriodID] [int] IDENTITY(1,1) NOT NULL,
    [FiscalYearID] [int] NOT NULL,
    [PeriodNumber] [int] NOT NULL,
    [PeriodName] [nvarchar](50) NOT NULL,
    [StartDate] [date] NOT NULL,
    [EndDate] [date] NOT NULL,
    [PeriodStatus] [tinyint] NOT NULL CONSTRAINT [DF__Accountin__Perio__640DD89F] DEFAULT ((1)),
    [ClosedBy] [int] NULL,
    [ClosedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[AttendanceRecords] (
    [AttendanceID] [int] IDENTITY(1,1) NOT NULL,
    [EmployeeID] [int] NOT NULL,
    [AttendanceDate] [date] NOT NULL,
    [ScheduledIn] [time] NULL,
    [ScheduledOut] [time] NULL,
    [ActualIn] [time] NULL,
    [ActualOut] [time] NULL,
    [WorkedHours] AS (case when [ActualIn] IS NOT NULL AND [ActualOut] IS NOT NULL then CONVERT([decimal](5,2),datediff(minute,[ActualIn],[ActualOut])/(60.0)) else (0) end),
    [OvertimeHours] [decimal](5,2) NULL CONSTRAINT [DF__Attendanc__Overt__6501FCD8] DEFAULT ((0)),
    [LateMinutes] AS (case when [ActualIn] IS NOT NULL AND [ScheduledIn] IS NOT NULL AND [ActualIn]>[ScheduledIn] then datediff(minute,[ScheduledIn],[ActualIn]) else (0) end),
    [EarlyLeaveMinutes] AS (case when [ActualOut] IS NOT NULL AND [ScheduledOut] IS NOT NULL AND [ActualOut]<[ScheduledOut] then datediff(minute,[ActualOut],[ScheduledOut]) else (0) end),
    [AttendanceStatus] [tinyint] NOT NULL CONSTRAINT [DF__Attendanc__Atten__65F62111] DEFAULT ((1)),
    [LeaveRequestID] [int] NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Attendanc__Creat__66EA454A] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[AuditLog] (
    [AuditID] [bigint] IDENTITY(1,1) NOT NULL,
    [AuditDate] [datetime2](7) NOT NULL CONSTRAINT [DF__AuditLog__AuditD__67DE6983] DEFAULT (getdate()),
    [UserID] [int] NULL,
    [Username] [nvarchar](50) NULL,
    [ActionType] [tinyint] NOT NULL,
    [TableName] [nvarchar](100) NOT NULL,
    [RecordID] [nvarchar](50) NULL,
    [OldValues] [nvarchar](MAX) NULL,
    [NewValues] [nvarchar](MAX) NULL,
    [ChangedColumns] [nvarchar](MAX) NULL,
    [ModuleName] [nvarchar](100) NULL,
    [FormName] [nvarchar](100) NULL,
    [IPAddress] [nvarchar](50) NULL,
    [MachineName] [nvarchar](100) NULL,
    [Description] [nvarchar](500) NULL);
GO

CREATE TABLE [dbo].[BankAccounts] (
    [BankAccountID] [int] IDENTITY(1,1) NOT NULL,
    [BankAccountCode] [nvarchar](20) NOT NULL,
    [BankName] [nvarchar](100) NOT NULL,
    [BranchName] [nvarchar](100) NULL,
    [AccountNumber] [nvarchar](50) NOT NULL,
    [IBAN] [nvarchar](50) NULL,
    [SwiftCode] [nvarchar](20) NULL,
    [AccountType] [tinyint] NULL CONSTRAINT [DF__BankAccou__Accou__68D28DBC] DEFAULT ((1)),
    [CurrencyID] [int] NULL,
    [AccountID] [int] NULL,
    [CurrentBalance] [decimal](18,2) NULL CONSTRAINT [DF__BankAccou__Curre__69C6B1F5] DEFAULT ((0)),
    [ContactPerson] [nvarchar](100) NULL,
    [Phone] [nvarchar](20) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__BankAccou__IsAct__6ABAD62E] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__BankAccou__Creat__6BAEFA67] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[BillOfMaterials] (
    [BOMID] [int] IDENTITY(1,1) NOT NULL,
    [BOMCode] [nvarchar](50) NOT NULL,
    [BOMName] [nvarchar](200) NOT NULL,
    [ProductItemID] [int] NOT NULL,
    [BOMVersion] [int] NOT NULL CONSTRAINT [DF__BillOfMat__BOMVe__6CA31EA0] DEFAULT ((1)),
    [BatchSize] [decimal](18,4) NOT NULL,
    [BatchUnitID] [int] NOT NULL,
    [OutputQty] [decimal](18,4) NOT NULL,
    [OutputUnitID] [int] NOT NULL,
    [WastePercent] [decimal](5,2) NULL CONSTRAINT [DF__BillOfMat__Waste__6D9742D9] DEFAULT ((3.00)),
    [EstimatedTime] [decimal](10,2) NULL,
    [LaborCostPerBatch] [decimal](18,2) NULL CONSTRAINT [DF__BillOfMat__Labor__6E8B6712] DEFAULT ((0)),
    [OverheadCostPerBatch] [decimal](18,2) NULL CONSTRAINT [DF__BillOfMat__Overh__6F7F8B4B] DEFAULT ((0)),
    [Instructions] [nvarchar](MAX) NULL,
    [SafetyNotes] [nvarchar](MAX) NULL,
    [QualityNotes] [nvarchar](MAX) NULL,
    [BOMStatus] [tinyint] NOT NULL CONSTRAINT [DF__BillOfMat__BOMSt__7073AF84] DEFAULT ((1)),
    [EffectiveFrom] [date] NULL,
    [EffectiveTo] [date] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__BillOfMat__Creat__7167D3BD] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__BillOfMat__IsAct__725BF7F6] DEFAULT ((1)));
GO

CREATE TABLE [dbo].[BOMDetails] (
    [BOMDetailID] [int] IDENTITY(1,1) NOT NULL,
    [BOMID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [MaterialType] [tinyint] NOT NULL,
    [UnitID] [int] NOT NULL,
    [Quantity] [decimal](18,6) NOT NULL,
    [WastePercent] [decimal](5,2) NULL CONSTRAINT [DF__BOMDetail__Waste__73501C2F] DEFAULT ((0)),
    [NetQuantity] AS ([Quantity]*((1)+isnull([WastePercent],(0))/(100))),
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__BOMDetail__UnitC__74444068] DEFAULT ((0)),
    [LineCost] AS (([Quantity]*((1)+isnull([WastePercent],(0))/(100)))*isnull([UnitCost],(0))),
    [IsOptional] [bit] NULL CONSTRAINT [DF__BOMDetail__IsOpt__753864A1] DEFAULT ((0)),
    [SubstituteItemID] [int] NULL,
    [Sequence] [int] NULL CONSTRAINT [DF__BOMDetail__Seque__762C88DA] DEFAULT ((0)),
    [StageNumber] [int] NULL CONSTRAINT [DF__BOMDetail__Stage__7720AD13] DEFAULT ((1)),
    [Notes] [nvarchar](200) NULL,
    [InputPercentage] [decimal](18,10) NULL);
GO

CREATE TABLE [dbo].[BOMStages] (
    [BOMStageID] [int] IDENTITY(1,1) NOT NULL,
    [BOMID] [int] NOT NULL,
    [StageID] [int] NOT NULL,
    [StageOrder] [int] NOT NULL,
    [Duration] [decimal](10,2) NULL,
    [Temperature] [nvarchar](50) NULL,
    [Speed] [nvarchar](50) NULL,
    [Instructions] [nvarchar](MAX) NULL,
    [QualityCheckRequired] [bit] NULL CONSTRAINT [DF__BOMStages__Quali__7814D14C] DEFAULT ((0)),
    [QualityCheckNotes] [nvarchar](500) NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[CashBoxes] (
    [CashBoxID] [int] IDENTITY(1,1) NOT NULL,
    [CashBoxCode] [nvarchar](20) NOT NULL,
    [CashBoxNameAr] [nvarchar](100) NOT NULL,
    [CashBoxNameEn] [nvarchar](100) NULL,
    [AccountID] [int] NULL,
    [CurrencyID] [int] NULL,
    [CurrentBalance] [decimal](18,2) NULL CONSTRAINT [DF__CashBoxes__Curre__7908F585] DEFAULT ((0)),
    [CustodianEmployeeID] [int] NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__CashBoxes__IsAct__79FD19BE] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__CashBoxes__Creat__7AF13DF7] DEFAULT (getdate()),
    [OpeningBalance] [decimal](18,2) NOT NULL CONSTRAINT [DF__CashBoxes__Openi__05AEC38C] DEFAULT ((0)),
    [CashBoxType] [int] NOT NULL CONSTRAINT [DF__CashBoxes__CashB__06A2E7C5] DEFAULT ((1)),
    [IsDefault] [bit] NOT NULL CONSTRAINT [DF__CashBoxes__IsDef__07970BFE] DEFAULT ((0)),
    [MaxLimit] [decimal](18,2) NULL,
    [MinLimit] [decimal](18,2) NULL,
    [Notes] [nvarchar](500) NULL,
    [BranchID] [int] NULL,
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime] NULL);
GO

CREATE TABLE [dbo].[CashTransfers] (
    [TransferID] [int] IDENTITY(1,1) NOT NULL,
    [TransferNo] [nvarchar](30) NOT NULL,
    [TransferDate] [date] NOT NULL CONSTRAINT [DF__CashTrans__Trans__11207638] DEFAULT (CONVERT([date],getdate())),
    [FromCashBoxID] [int] NOT NULL,
    [ToCashBoxID] [int] NOT NULL,
    [Amount] [decimal](18,2) NOT NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NOT NULL CONSTRAINT [DF__CashTrans__Excha__13FCE2E3] DEFAULT ((1)),
    [AmountLocal] [decimal](18,2) NOT NULL CONSTRAINT [DF__CashTrans__Amoun__14F1071C] DEFAULT ((0)),
    [Description] [nvarchar](500) NULL,
    [Status] [int] NOT NULL CONSTRAINT [DF__CashTrans__Statu__15E52B55] DEFAULT ((1)),
    [JournalID] [int] NULL,
    [IsPosted] [bit] NOT NULL CONSTRAINT [DF__CashTrans__IsPos__16D94F8E] DEFAULT ((0)),
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime] NOT NULL CONSTRAINT [DF__CashTrans__Creat__17CD73C7] DEFAULT (getdate()),
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime] NULL);
GO

CREATE TABLE [dbo].[ChartOfAccounts] (
    [AccountID] [int] IDENTITY(1,1) NOT NULL,
    [AccountCode] [nvarchar](20) NOT NULL,
    [AccountNameAr] [nvarchar](200) NOT NULL,
    [AccountNameEn] [nvarchar](200) NULL,
    [ParentAccountID] [int] NULL,
    [AccountLevel] [tinyint] NOT NULL CONSTRAINT [DF__ChartOfAc__Accou__7BE56230] DEFAULT ((1)),
    [AccountType] [tinyint] NOT NULL,
    [AccountNature] [tinyint] NOT NULL,
    [IsDetailAccount] [bit] NOT NULL CONSTRAINT [DF__ChartOfAc__IsDet__7CD98669] DEFAULT ((1)),
    [CurrencyID] [int] NULL,
    [CostCenterRequired] [bit] NULL CONSTRAINT [DF__ChartOfAc__CostC__7DCDAAA2] DEFAULT ((0)),
    [OpeningBalance] [decimal](18,2) NULL CONSTRAINT [DF__ChartOfAc__Openi__7EC1CEDB] DEFAULT ((0)),
    [CurrentBalance] [decimal](18,2) NULL CONSTRAINT [DF__ChartOfAc__Curre__7FB5F314] DEFAULT ((0)),
    [LinkedEntityType] [nvarchar](20) NULL,
    [LinkedEntityID] [int] NULL,
    [IsSystemAccount] [bit] NULL CONSTRAINT [DF__ChartOfAc__IsSys__00AA174D] DEFAULT ((0)),
    [IsCashFlowAccount] [bit] NULL CONSTRAINT [DF__ChartOfAc__IsCas__019E3B86] DEFAULT ((0)),
    [SortOrder] [int] NULL CONSTRAINT [DF__ChartOfAc__SortO__02925FBF] DEFAULT ((0)),
    [Description] [nvarchar](200) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__ChartOfAc__IsAct__038683F8] DEFAULT ((1)),
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__ChartOfAc__Creat__047AA831] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[Cheques] (
    [ChequeID] [int] IDENTITY(1,1) NOT NULL,
    [ChequeNumber] [nvarchar](50) NOT NULL,
    [ChequeType] [tinyint] NOT NULL,
    [ChequeDate] [date] NOT NULL,
    [DueDate] [date] NOT NULL,
    [Amount] [decimal](18,2) NOT NULL,
    [CurrencyID] [int] NULL,
    [BankName] [nvarchar](100) NULL,
    [BranchName] [nvarchar](100) NULL,
    [BankAccountID] [int] NULL,
    [EntityType] [tinyint] NULL,
    [EntityID] [int] NULL,
    [EntityName] [nvarchar](200) NULL,
    [ChequeStatus] [tinyint] NOT NULL CONSTRAINT [DF__Cheques__ChequeS__056ECC6A] DEFAULT ((1)),
    [CollectionDate] [date] NULL,
    [BounceDate] [date] NULL,
    [BounceReason] [nvarchar](200) NULL,
    [ReceiptID] [int] NULL,
    [PaymentID] [int] NULL,
    [JournalID] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Cheques__Created__0662F0A3] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[ContractDeliveries] (
    [DeliveryID] [int] IDENTITY(1,1) NOT NULL,
    [DeliveryNumber] [nvarchar](50) NOT NULL,
    [DeliveryDate] [date] NOT NULL CONSTRAINT [DF__ContractD__Deliv__07CC1628] DEFAULT (CONVERT([date],getdate())),
    [ContractOrderID] [int] NOT NULL,
    [CustomerID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [DeliveryStatus] [tinyint] NOT NULL CONSTRAINT [DF__ContractD__Deliv__0B9CA70C] DEFAULT ((1)),
    [DeliveredBy] [int] NULL,
    [ReceivedByCustomer] [nvarchar](100) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__ContractD__Creat__0D84EF7E] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[ContractDeliveryDetails] (
    [DeliveryDetailID] [int] IDENTITY(1,1) NOT NULL,
    [DeliveryID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ContractDetailID] [int] NULL,
    [ProductItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [DeliveredQty] [decimal](18,4) NOT NULL,
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[ContractMaterialSources] (
    [SourceID] [int] IDENTITY(1,1) NOT NULL,
    [ContractOrderID] [int] NOT NULL,
    [ContractDetailID] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [TotalRequiredQty] [decimal](18,4) NOT NULL CONSTRAINT [DF__ContractM__Total__3B4BBA2E] DEFAULT ((0)),
    [CustomerQty] [decimal](18,4) NOT NULL CONSTRAINT [DF__ContractM__Custo__3C3FDE67] DEFAULT ((0)),
    [FactoryQty] [decimal](18,4) NOT NULL CONSTRAINT [DF__ContractM__Facto__3D3402A0] DEFAULT ((0)),
    [MaterialSource] [tinyint] NOT NULL CONSTRAINT [DF__ContractM__Mater__3E2826D9] DEFAULT ((1)),
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[ContractOrderDetails] (
    [ContractDetailID] [int] IDENTITY(1,1) NOT NULL,
    [ContractOrderID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ProductItemID] [int] NOT NULL,
    [BOMID] [int] NULL,
    [OrderedQty] [decimal](18,4) NOT NULL,
    [UnitID] [int] NOT NULL,
    [ProducedQty] [decimal](18,4) NULL CONSTRAINT [DF__ContractO__Produ__6D181FEC] DEFAULT ((0)),
    [DeliveredQty] [decimal](18,4) NULL CONSTRAINT [DF__ContractO__Deliv__6E0C4425] DEFAULT ((0)),
    [ManufacturingFeePerUnit] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__Manuf__6F00685E] DEFAULT ((0)),
    [TotalManufacturingFee] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__Total__6FF48C97] DEFAULT ((0)),
    [MaterialCostPerUnit] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__Mater__70E8B0D0] DEFAULT ((0)),
    [TotalMaterialCost] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__Total__71DCD509] DEFAULT ((0)),
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__LineT__72D0F942] DEFAULT ((0)),
    [ProductionOrderID] [int] NULL,
    [LineStatus] [tinyint] NULL CONSTRAINT [DF__ContractO__LineS__74B941B4] DEFAULT ((1)),
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[ContractOrders] (
    [ContractOrderID] [int] IDENTITY(1,1) NOT NULL,
    [ContractNumber] [nvarchar](50) NOT NULL,
    [ContractDate] [date] NOT NULL CONSTRAINT [DF__ContractO__Contr__5DD5DC5C] DEFAULT (CONVERT([date],getdate())),
    [CustomerID] [int] NOT NULL,
    [MaterialSource] [tinyint] NOT NULL CONSTRAINT [DF__ContractO__Mater__5FBE24CE] DEFAULT ((1)),
    [ContractStatus] [tinyint] NOT NULL CONSTRAINT [DF__ContractO__Contr__60B24907] DEFAULT ((1)),
    [DeliveryDate] [date] NULL,
    [CustomerPONumber] [nvarchar](50) NULL,
    [TotalManufacturingFee] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__Total__61A66D40] DEFAULT ((0)),
    [TotalMaterialCost] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__Total__629A9179] DEFAULT ((0)),
    [TotalContractValue] [decimal](18,2) NULL CONSTRAINT [DF__ContractO__Total__638EB5B2] DEFAULT ((0)),
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__ContractO__Creat__6576FE24] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [SourceWarehouseID] [int] NULL,
    [TargetWarehouseID] [int] NULL);
GO

CREATE TABLE [dbo].[CostCenters] (
    [CostCenterID] [int] IDENTITY(1,1) NOT NULL,
    [CostCenterCode] [nvarchar](20) NOT NULL,
    [CostCenterNameAr] [nvarchar](100) NOT NULL,
    [CostCenterNameEn] [nvarchar](100) NULL,
    [ParentCostCenterID] [int] NULL,
    [CenterLevel] [tinyint] NULL CONSTRAINT [DF__CostCente__Cente__075714DC] DEFAULT ((1)),
    [DepartmentID] [int] NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__CostCente__IsAct__084B3915] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__CostCente__Creat__093F5D4E] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[Currencies] (
    [CurrencyID] [int] IDENTITY(1,1) NOT NULL,
    [CurrencyCode] [nvarchar](3) NOT NULL,
    [CurrencyNameAr] [nvarchar](50) NOT NULL,
    [CurrencyNameEn] [nvarchar](50) NULL,
    [Symbol] [nvarchar](5) NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__Currencie__Excha__0A338187] DEFAULT ((1.000000)),
    [IsDefault] [bit] NULL CONSTRAINT [DF__Currencie__IsDef__0B27A5C0] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__Currencie__IsAct__0C1BC9F9] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Currencie__Creat__0D0FEE32] DEFAULT (getdate()),
    [CreatedBy] [int] NULL,
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime] NULL);
GO

CREATE TABLE [dbo].[CustomerContacts] (
    [ContactID] [int] IDENTITY(1,1) NOT NULL,
    [CustomerID] [int] NOT NULL,
    [ContactName] [nvarchar](100) NOT NULL,
    [JobTitle] [nvarchar](100) NULL,
    [Phone] [nvarchar](20) NULL,
    [Mobile] [nvarchar](20) NULL,
    [Email] [nvarchar](100) NULL,
    [IsPrimary] [bit] NULL CONSTRAINT [DF__CustomerC__IsPri__0E04126B] DEFAULT ((0)),
    [Notes] [nvarchar](200) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__CustomerC__IsAct__0EF836A4] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__CustomerC__Creat__0FEC5ADD] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[CustomerGroups] (
    [GroupID] [int] IDENTITY(1,1) NOT NULL,
    [GroupCode] [nvarchar](20) NOT NULL,
    [GroupNameAr] [nvarchar](100) NOT NULL,
    [GroupNameEn] [nvarchar](100) NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__CustomerG__Disco__10E07F16] DEFAULT ((0)),
    [PriceListID] [int] NULL,
    [Description] [nvarchar](200) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__CustomerG__IsAct__11D4A34F] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__CustomerG__Creat__12C8C788] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[CustomerMaterialReceiptDetails] (
    [ReceiptDetailID] [int] IDENTITY(1,1) NOT NULL,
    [ReceiptID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [ReceivedQty] [decimal](18,4) NOT NULL,
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[CustomerMaterialReceipts] (
    [ReceiptID] [int] IDENTITY(1,1) NOT NULL,
    [ReceiptNumber] [nvarchar](50) NOT NULL,
    [ReceiptDate] [date] NOT NULL CONSTRAINT [DF__CustomerM__Recei__7889D298] DEFAULT (CONVERT([date],getdate())),
    [ContractOrderID] [int] NOT NULL,
    [CustomerID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [ReceiptStatus] [tinyint] NOT NULL CONSTRAINT [DF__CustomerM__Recei__7C5A637C] DEFAULT ((1)),
    [ReceivedBy] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__CustomerM__Creat__7E42ABEE] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[CustomerMaterialReturnDetails] (
    [ReturnDetailID] [int] IDENTITY(1,1) NOT NULL,
    [ReturnID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [ReturnedQty] [decimal](18,4) NOT NULL,
    [BatchNumber] [nvarchar](50) NULL,
    [Notes] [nvarchar](200) NULL,
    [ReceivedQty] [decimal](18,4) NULL CONSTRAINT [DF__CustomerM__Recei__41049384] DEFAULT ((0)),
    [ConsumedQty] [decimal](18,4) NULL CONSTRAINT [DF__CustomerM__Consu__41F8B7BD] DEFAULT ((0)),
    [SurplusQty] [decimal](18,4) NULL CONSTRAINT [DF__CustomerM__Surpl__42ECDBF6] DEFAULT ((0)));
GO

CREATE TABLE [dbo].[CustomerMaterialReturns] (
    [ReturnID] [int] IDENTITY(1,1) NOT NULL,
    [ReturnNumber] [nvarchar](50) NOT NULL,
    [ReturnDate] [date] NOT NULL CONSTRAINT [DF__CustomerM__Retur__18027DF1] DEFAULT (CONVERT([date],getdate())),
    [ContractOrderID] [int] NOT NULL,
    [CustomerID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [ReturnStatus] [tinyint] NOT NULL CONSTRAINT [DF__CustomerM__Retur__1BD30ED5] DEFAULT ((1)),
    [ReturnedBy] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__CustomerM__Creat__1DBB5747] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[Customers] (
    [CustomerID] [int] IDENTITY(1,1) NOT NULL,
    [CustomerCode] [nvarchar](20) NOT NULL,
    [CustomerNameAr] [nvarchar](200) NOT NULL,
    [CustomerNameEn] [nvarchar](200) NULL,
    [CustomerType] [tinyint] NOT NULL CONSTRAINT [DF__Customers__Custo__13BCEBC1] DEFAULT ((1)),
    [CustomerGroupID] [int] NULL,
    [TaxNumber] [nvarchar](50) NULL,
    [CommercialRegister] [nvarchar](50) NULL,
    [Address] [nvarchar](300) NULL,
    [City] [nvarchar](100) NULL,
    [Region] [nvarchar](100) NULL,
    [Country] [nvarchar](100) NULL CONSTRAINT [DF__Customers__Count__14B10FFA] DEFAULT (N'مصر'),
    [Phone1] [nvarchar](20) NULL,
    [Phone2] [nvarchar](20) NULL,
    [Mobile] [nvarchar](20) NULL,
    [Fax] [nvarchar](20) NULL,
    [Email] [nvarchar](100) NULL,
    [Website] [nvarchar](200) NULL,
    [PaymentTermID] [int] NULL,
    [CurrencyID] [int] NULL,
    [CreditLimit] [decimal](18,2) NULL CONSTRAINT [DF__Customers__Credi__15A53433] DEFAULT ((0)),
    [OpeningBalance] [decimal](18,2) NULL CONSTRAINT [DF__Customers__Openi__1699586C] DEFAULT ((0)),
    [CurrentBalance] [decimal](18,2) NULL CONSTRAINT [DF__Customers__Curre__178D7CA5] DEFAULT ((0)),
    [PriceListID] [int] NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__Customers__Disco__1881A0DE] DEFAULT ((0)),
    [SalesRepID] [int] NULL,
    [BankName] [nvarchar](100) NULL,
    [BankAccountNumber] [nvarchar](50) NULL,
    [Rating] [tinyint] NULL CONSTRAINT [DF__Customers__Ratin__1975C517] DEFAULT ((3)),
    [Notes] [nvarchar](MAX) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__Customers__IsAct__1A69E950] DEFAULT ((1)),
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Customers__Creat__1B5E0D89] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[Departments] (
    [DepartmentID] [int] IDENTITY(1,1) NOT NULL,
    [DepartmentCode] [nvarchar](20) NOT NULL,
    [DepartmentNameAr] [nvarchar](100) NOT NULL,
    [DepartmentNameEn] [nvarchar](100) NULL,
    [ParentDepartmentID] [int] NULL,
    [ManagerEmployeeID] [int] NULL,
    [Phone] [nvarchar](20) NULL,
    [CostCenterCode] [nvarchar](20) NULL,
    [SortOrder] [int] NULL CONSTRAINT [DF__Departmen__SortO__1C5231C2] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__Departmen__IsAct__1D4655FB] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Departmen__Creat__1E3A7A34] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[EmployeeLeaveBalances] (
    [BalanceID] [int] IDENTITY(1,1) NOT NULL,
    [EmployeeID] [int] NOT NULL,
    [LeaveTypeID] [int] NOT NULL,
    [FiscalYearID] [int] NOT NULL,
    [EntitledDays] [decimal](5,2) NULL CONSTRAINT [DF__EmployeeL__Entit__1F2E9E6D] DEFAULT ((0)),
    [CarriedForward] [decimal](5,2) NULL CONSTRAINT [DF__EmployeeL__Carri__2022C2A6] DEFAULT ((0)),
    [AdditionalDays] [decimal](5,2) NULL CONSTRAINT [DF__EmployeeL__Addit__2116E6DF] DEFAULT ((0)),
    [TotalEntitled] AS ((isnull([EntitledDays],(0))+isnull([CarriedForward],(0)))+isnull([AdditionalDays],(0))),
    [UsedDays] [decimal](5,2) NULL CONSTRAINT [DF__EmployeeL__UsedD__220B0B18] DEFAULT ((0)),
    [PendingDays] [decimal](5,2) NULL CONSTRAINT [DF__EmployeeL__Pendi__22FF2F51] DEFAULT ((0)),
    [RemainingDays] AS ((((isnull([EntitledDays],(0))+isnull([CarriedForward],(0)))+isnull([AdditionalDays],(0)))-isnull([UsedDays],(0)))-isnull([PendingDays],(0))),
    [ModifiedDate] [datetime2](7) NULL CONSTRAINT [DF__EmployeeL__Modif__23F3538A] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[EmployeeLoans] (
    [LoanID] [int] IDENTITY(1,1) NOT NULL,
    [LoanNumber] [nvarchar](50) NOT NULL,
    [EmployeeID] [int] NOT NULL,
    [LoanDate] [date] NOT NULL CONSTRAINT [DF__EmployeeL__LoanD__24E777C3] DEFAULT (CONVERT([date],getdate())),
    [LoanType] [tinyint] NULL CONSTRAINT [DF__EmployeeL__LoanT__25DB9BFC] DEFAULT ((1)),
    [LoanAmount] [decimal](18,2) NOT NULL,
    [NumberOfInstallments] [int] NOT NULL CONSTRAINT [DF__EmployeeL__Numbe__26CFC035] DEFAULT ((1)),
    [InstallmentAmount] [decimal](18,2) NULL,
    [StartDeductionDate] [date] NULL,
    [PaidAmount] [decimal](18,2) NULL CONSTRAINT [DF__EmployeeL__PaidA__27C3E46E] DEFAULT ((0)),
    [RemainingAmount] AS (isnull([LoanAmount],(0))-isnull([PaidAmount],(0))),
    [LoanReason] [nvarchar](500) NULL,
    [LoanStatus] [tinyint] NOT NULL CONSTRAINT [DF__EmployeeL__LoanS__28B808A7] DEFAULT ((1)),
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [PaymentVoucherID] [int] NULL,
    [JournalID] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__EmployeeL__Creat__29AC2CE0] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[EmployeePenaltiesRewards] (
    [RecordID] [int] IDENTITY(1,1) NOT NULL,
    [EmployeeID] [int] NOT NULL,
    [RecordDate] [date] NOT NULL CONSTRAINT [DF__EmployeeP__Recor__2AA05119] DEFAULT (CONVERT([date],getdate())),
    [RecordType] [tinyint] NOT NULL,
    [Category] [nvarchar](50) NULL,
    [Description] [nvarchar](500) NOT NULL,
    [DeductionType] [tinyint] NULL,
    [Amount] [decimal](18,2) NULL CONSTRAINT [DF__EmployeeP__Amoun__2B947552] DEFAULT ((0)),
    [Percentage] [decimal](5,2) NULL CONSTRAINT [DF__EmployeeP__Perce__2C88998B] DEFAULT ((0)),
    [Days] [decimal](5,2) NULL CONSTRAINT [DF__EmployeePe__Days__2D7CBDC4] DEFAULT ((0)),
    [EffectiveMonth] [date] NULL,
    [RecordStatus] [tinyint] NULL CONSTRAINT [DF__EmployeeP__Recor__2E70E1FD] DEFAULT ((1)),
    [IssuedBy] [int] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [PayrollID] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__EmployeeP__Creat__2F650636] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[Employees] (
    [EmployeeID] [int] IDENTITY(1,1) NOT NULL,
    [EmployeeCode] [nvarchar](20) NOT NULL,
    [FirstNameAr] [nvarchar](50) NOT NULL,
    [SecondNameAr] [nvarchar](50) NULL,
    [ThirdNameAr] [nvarchar](50) NULL,
    [LastNameAr] [nvarchar](50) NOT NULL,
    [FullNameAr] AS (((([FirstNameAr]+N' ')+isnull([SecondNameAr]+N' ',N''))+isnull([ThirdNameAr]+N' ',N''))+[LastNameAr]),
    [FirstNameEn] [nvarchar](50) NULL,
    [LastNameEn] [nvarchar](50) NULL,
    [FullNameEn] AS (isnull([FirstNameEn]+N' ',N'')+isnull([LastNameEn],N'')),
    [NationalID] [nvarchar](20) NULL,
    [BirthDate] [date] NULL,
    [Gender] [nchar](1) NULL,
    [MaritalStatus] [tinyint] NULL CONSTRAINT [DF__Employees__Marit__30592A6F] DEFAULT ((1)),
    [NumberOfChildren] [int] NULL CONSTRAINT [DF__Employees__Numbe__314D4EA8] DEFAULT ((0)),
    [Nationality] [nvarchar](50) NULL CONSTRAINT [DF__Employees__Natio__324172E1] DEFAULT (N'مصري'),
    [Religion] [nvarchar](20) NULL,
    [Address] [nvarchar](300) NULL,
    [City] [nvarchar](100) NULL,
    [Phone] [nvarchar](20) NULL,
    [Mobile] [nvarchar](20) NULL,
    [PersonalEmail] [nvarchar](100) NULL,
    [WorkEmail] [nvarchar](100) NULL,
    [EmergencyContactName] [nvarchar](100) NULL,
    [EmergencyContactPhone] [nvarchar](20) NULL,
    [EmergencyContactRelation] [nvarchar](50) NULL,
    [DepartmentID] [int] NULL,
    [JobTitleID] [int] NULL,
    [ManagerID] [int] NULL,
    [HireDate] [date] NOT NULL,
    [ContractType] [tinyint] NOT NULL CONSTRAINT [DF__Employees__Contr__3335971A] DEFAULT ((1)),
    [ContractStartDate] [date] NULL,
    [ContractEndDate] [date] NULL,
    [ProbationEndDate] [date] NULL,
    [EmployeeStatus] [tinyint] NOT NULL CONSTRAINT [DF__Employees__Emplo__3429BB53] DEFAULT ((1)),
    [TerminationDate] [date] NULL,
    [TerminationReason] [nvarchar](200) NULL,
    [BasicSalary] [decimal](18,2) NULL CONSTRAINT [DF__Employees__Basic__351DDF8C] DEFAULT ((0)),
    [TransportAllowance] [decimal](18,2) NULL CONSTRAINT [DF__Employees__Trans__361203C5] DEFAULT ((0)),
    [HousingAllowance] [decimal](18,2) NULL CONSTRAINT [DF__Employees__Housi__370627FE] DEFAULT ((0)),
    [PhoneAllowance] [decimal](18,2) NULL CONSTRAINT [DF__Employees__Phone__37FA4C37] DEFAULT ((0)),
    [FoodAllowance] [decimal](18,2) NULL CONSTRAINT [DF__Employees__FoodA__38EE7070] DEFAULT ((0)),
    [OtherAllowances] [decimal](18,2) NULL CONSTRAINT [DF__Employees__Other__39E294A9] DEFAULT ((0)),
    [TotalSalary] AS ((((([BasicSalary]+[TransportAllowance])+[HousingAllowance])+[PhoneAllowance])+[FoodAllowance])+[OtherAllowances]),
    [InsuranceNumber] [nvarchar](20) NULL,
    [InsuranceSalary] [decimal](18,2) NULL CONSTRAINT [DF__Employees__Insur__3AD6B8E2] DEFAULT ((0)),
    [InsurancePercEmployee] [decimal](5,2) NULL CONSTRAINT [DF__Employees__Insur__3BCADD1B] DEFAULT ((11.00)),
    [InsurancePercCompany] [decimal](5,2) NULL CONSTRAINT [DF__Employees__Insur__3CBF0154] DEFAULT ((18.75)),
    [IsInsured] [bit] NULL CONSTRAINT [DF__Employees__IsIns__3DB3258D] DEFAULT ((1)),
    [InsuranceStartDate] [date] NULL,
    [BankName] [nvarchar](100) NULL,
    [BankAccountNumber] [nvarchar](50) NULL,
    [IBAN] [nvarchar](50) NULL,
    [PaymentMethod] [tinyint] NULL CONSTRAINT [DF__Employees__Payme__3EA749C6] DEFAULT ((1)),
    [WorkingHoursPerDay] [decimal](4,2) NULL CONSTRAINT [DF__Employees__Worki__3F9B6DFF] DEFAULT ((8.00)),
    [WeeklyDaysOff] [tinyint] NULL CONSTRAINT [DF__Employees__Weekl__408F9238] DEFAULT ((1)),
    [AnnualLeaveBalance] [decimal](5,2) NULL CONSTRAINT [DF__Employees__Annua__4183B671] DEFAULT ((21.00)),
    [ImagePath] [nvarchar](500) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__Employees__IsAct__4277DAAA] DEFAULT ((1)),
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Employees__Creat__436BFEE3] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [NationalIDGovernorate] [nvarchar](100) NULL);
GO

CREATE TABLE [dbo].[FiscalYears] (
    [FiscalYearID] [int] IDENTITY(1,1) NOT NULL,
    [YearCode] [nvarchar](10) NOT NULL,
    [YearName] [nvarchar](50) NOT NULL,
    [StartDate] [date] NOT NULL,
    [EndDate] [date] NOT NULL,
    [YearStatus] [tinyint] NOT NULL CONSTRAINT [DF__FiscalYea__YearS__4460231C] DEFAULT ((1)),
    [IsCurrent] [bit] NULL CONSTRAINT [DF__FiscalYea__IsCur__45544755] DEFAULT ((0)),
    [ClosedBy] [int] NULL,
    [ClosedDate] [datetime2](7) NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__FiscalYea__Creat__46486B8E] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[GoodsReceiptDetails] (
    [GRNDetailID] [int] IDENTITY(1,1) NOT NULL,
    [GRNID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [PODetailID] [int] NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [OrderedQty] [decimal](18,4) NULL CONSTRAINT [DF__GoodsRece__Order__473C8FC7] DEFAULT ((0)),
    [ReceivedQty] [decimal](18,4) NOT NULL,
    [AcceptedQty] [decimal](18,4) NULL,
    [RejectedQty] [decimal](18,4) NULL CONSTRAINT [DF__GoodsRece__Rejec__4830B400] DEFAULT ((0)),
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [ManufactureDate] [date] NULL,
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__GoodsRece__UnitC__4924D839] DEFAULT ((0)),
    [LineTotalCost] [decimal](18,2) NULL CONSTRAINT [DF__GoodsRece__LineT__4A18FC72] DEFAULT ((0)),
    [StorageLocation] [nvarchar](50) NULL,
    [QualityStatus] [tinyint] NULL CONSTRAINT [DF__GoodsRece__Quali__4B0D20AB] DEFAULT ((1)),
    [QualityNotes] [nvarchar](200) NULL,
    [Notes] [nvarchar](200) NULL,
    [WarehouseID] [int] NOT NULL,
    [SupplierBatchNumber] [nvarchar](100) NULL);
GO

CREATE TABLE [dbo].[GoodsReceiptNotes] (
    [GRNID] [int] IDENTITY(1,1) NOT NULL,
    [GRNNumber] [nvarchar](50) NOT NULL,
    [GRNDate] [date] NOT NULL CONSTRAINT [DF__GoodsRece__GRNDa__4C0144E4] DEFAULT (CONVERT([date],getdate())),
    [PurchaseOrderID] [int] NULL,
    [SupplierID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [SupplierInvoiceNo] [nvarchar](50) NULL,
    [SupplierInvoiceDate] [date] NULL,
    [DeliveryNoteNo] [nvarchar](50) NULL,
    [GRNStatus] [tinyint] NOT NULL CONSTRAINT [DF__GoodsRece__GRNSt__4CF5691D] DEFAULT ((1)),
    [ReceivedBy] [int] NULL,
    [InspectedBy] [int] NULL,
    [InspectionStatus] [tinyint] NULL CONSTRAINT [DF__GoodsRece__Inspe__4DE98D56] DEFAULT ((1)),
    [InspectionNotes] [nvarchar](500) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__GoodsRece__Creat__4EDDB18F] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [GRNSource] [tinyint] NULL CONSTRAINT [DF__GoodsRece__GRNSo__522F1F86] DEFAULT ((1)),
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [LinkedInvoiceID] [int] NULL,
    [AssignedInspectorID] [int] NULL,
    [InspectionRequestedDate] [datetime2](0) NULL,
    [InspectionCompletedDate] [datetime2](0) NULL,
    [ApprovalRequestedDate] [datetime2](0) NULL);
GO

CREATE TABLE [dbo].[InventoryBalance] (
    [BalanceID] [int] IDENTITY(1,1) NOT NULL,
    [ItemID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [CurrentQty] [decimal](18,4) NOT NULL CONSTRAINT [DF__Inventory__Curre__4FD1D5C8] DEFAULT ((0)),
    [ReservedQty] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__Reser__50C5FA01] DEFAULT ((0)),
    [AvailableQty] AS ([CurrentQty]-isnull([ReservedQty],(0))),
    [AverageCost] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__Avera__51BA1E3A] DEFAULT ((0)),
    [TotalValue] AS ([CurrentQty]*isnull([AverageCost],(0))),
    [LastMovementDate] [datetime2](7) NULL,
    [LastCountDate] [date] NULL,
    [ModifiedDate] [datetime2](7) NULL CONSTRAINT [DF__Inventory__Modif__52AE4273] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[InventoryBatchBalance] (
    [BatchBalanceID] [int] IDENTITY(1,1) NOT NULL,
    [ItemID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [BatchNumber] [nvarchar](50) NOT NULL,
    [ExpiryDate] [date] NULL,
    [ManufactureDate] [date] NULL,
    [CurrentQty] [decimal](18,4) NOT NULL CONSTRAINT [DF__Inventory__Curre__53A266AC] DEFAULT ((0)),
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__UnitC__54968AE5] DEFAULT ((0)),
    [ReceiptDate] [date] NULL,
    [SourceType] [tinyint] NULL,
    [SourceDocNumber] [nvarchar](50) NULL,
    [ModifiedDate] [datetime2](7) NULL CONSTRAINT [DF__Inventory__Modif__558AAF1E] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[InventoryTransactions] (
    [TransactionID] [int] IDENTITY(1,1) NOT NULL,
    [TransactionDate] [datetime2](7) NOT NULL CONSTRAINT [DF__Inventory__Trans__567ED357] DEFAULT (getdate()),
    [TransactionType] [tinyint] NOT NULL,
    [ItemID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [Quantity] [decimal](18,4) NOT NULL,
    [UnitID] [int] NOT NULL,
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__UnitC__5772F790] DEFAULT ((0)),
    [TotalCost] AS (abs([Quantity])*isnull([UnitCost],(0))),
    [BalanceBefore] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__Balan__58671BC9] DEFAULT ((0)),
    [BalanceAfter] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__Balan__595B4002] DEFAULT ((0)),
    [CostBefore] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__CostB__5A4F643B] DEFAULT ((0)),
    [CostAfter] [decimal](18,4) NULL CONSTRAINT [DF__Inventory__CostA__5B438874] DEFAULT ((0)),
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [SourceDocType] [nvarchar](20) NULL,
    [SourceDocID] [int] NULL,
    [SourceDocNumber] [nvarchar](50) NULL,
    [SourceDocLineID] [int] NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Inventory__Creat__5C37ACAD] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[ItemCategories] (
    [CategoryID] [int] IDENTITY(1,1) NOT NULL,
    [CategoryCode] [nvarchar](20) NOT NULL,
    [CategoryNameAr] [nvarchar](100) NOT NULL,
    [CategoryNameEn] [nvarchar](100) NULL,
    [ParentCategoryID] [int] NULL,
    [CategoryLevel] [tinyint] NULL CONSTRAINT [DF__ItemCateg__Categ__5D2BD0E6] DEFAULT ((1)),
    [SortOrder] [int] NULL CONSTRAINT [DF__ItemCateg__SortO__5E1FF51F] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__ItemCateg__IsAct__5F141958] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__ItemCateg__Creat__60083D91] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[Items] (
    [ItemID] [int] IDENTITY(1,1) NOT NULL,
    [ItemCode] [nvarchar](50) NOT NULL,
    [Barcode] [nvarchar](50) NULL,
    [ItemNameAr] [nvarchar](200) NOT NULL,
    [ItemNameEn] [nvarchar](200) NULL,
    [CategoryID] [int] NOT NULL,
    [ItemType] [tinyint] NOT NULL,
    [PrimaryUnitID] [int] NOT NULL,
    [SecondaryUnitID] [int] NULL,
    [ConversionFactor] [decimal](18,6) NULL,
    [MinStockLevel] [decimal](18,4) NULL CONSTRAINT [DF__Items__MinStockL__60FC61CA] DEFAULT ((0)),
    [MaxStockLevel] [decimal](18,4) NULL CONSTRAINT [DF__Items__MaxStockL__61F08603] DEFAULT ((0)),
    [ReorderLevel] [decimal](18,4) NULL CONSTRAINT [DF__Items__ReorderLe__62E4AA3C] DEFAULT ((0)),
    [ShelfLifeDays] [int] NULL,
    [StandardCost] [decimal](18,4) NULL CONSTRAINT [DF__Items__StandardC__63D8CE75] DEFAULT ((0)),
    [LastPurchasePrice] [decimal](18,4) NULL CONSTRAINT [DF__Items__LastPurch__64CCF2AE] DEFAULT ((0)),
    [AverageCost] [decimal](18,4) NULL CONSTRAINT [DF__Items__AverageCo__65C116E7] DEFAULT ((0)),
    [DefaultSellingPrice] [decimal](18,4) NULL CONSTRAINT [DF__Items__DefaultSe__66B53B20] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__Items__TaxRate__67A95F59] DEFAULT ((14.00)),
    [IsTaxable] [bit] NULL CONSTRAINT [DF__Items__IsTaxable__689D8392] DEFAULT ((1)),
    [Weight] [decimal](18,4) NULL,
    [Volume] [decimal](18,4) NULL,
    [Description] [nvarchar](500) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [ImagePath] [nvarchar](500) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__Items__IsActive__6991A7CB] DEFAULT ((1)),
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Items__CreatedDa__6A85CC04] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[JobTitles] (
    [JobTitleID] [int] IDENTITY(1,1) NOT NULL,
    [JobTitleCode] [nvarchar](20) NOT NULL,
    [JobTitleNameAr] [nvarchar](100) NOT NULL,
    [JobTitleNameEn] [nvarchar](100) NULL,
    [JobLevel] [tinyint] NULL CONSTRAINT [DF__JobTitles__JobLe__6B79F03D] DEFAULT ((1)),
    [MinSalary] [decimal](18,2) NULL CONSTRAINT [DF__JobTitles__MinSa__6C6E1476] DEFAULT ((0)),
    [MaxSalary] [decimal](18,2) NULL CONSTRAINT [DF__JobTitles__MaxSa__6D6238AF] DEFAULT ((0)),
    [Description] [nvarchar](200) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__JobTitles__IsAct__6E565CE8] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__JobTitles__Creat__6F4A8121] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[JournalEntries] (
    [JournalID] [int] IDENTITY(1,1) NOT NULL,
    [JournalNumber] [nvarchar](50) NOT NULL,
    [JournalDate] [date] NOT NULL CONSTRAINT [DF__JournalEn__Journ__703EA55A] DEFAULT (CONVERT([date],getdate())),
    [FiscalYearID] [int] NULL,
    [PeriodID] [int] NULL,
    [JournalType] [tinyint] NOT NULL CONSTRAINT [DF__JournalEn__Journ__7132C993] DEFAULT ((1)),
    [Description] [nvarchar](500) NOT NULL,
    [Reference] [nvarchar](100) NULL,
    [TotalDebit] [decimal](18,2) NULL CONSTRAINT [DF__JournalEn__Total__7226EDCC] DEFAULT ((0)),
    [TotalCredit] [decimal](18,2) NULL CONSTRAINT [DF__JournalEn__Total__731B1205] DEFAULT ((0)),
    [SourceDocType] [nvarchar](20) NULL,
    [SourceDocID] [int] NULL,
    [SourceDocNumber] [nvarchar](50) NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__JournalEn__Excha__740F363E] DEFAULT ((1)),
    [JournalStatus] [tinyint] NOT NULL CONSTRAINT [DF__JournalEn__Journ__75035A77] DEFAULT ((1)),
    [IsAutoGenerated] [bit] NULL CONSTRAINT [DF__JournalEn__IsAut__75F77EB0] DEFAULT ((0)),
    [IsReversed] [bit] NULL CONSTRAINT [DF__JournalEn__IsRev__76EBA2E9] DEFAULT ((0)),
    [ReversedJournalID] [int] NULL,
    [PostedBy] [int] NULL,
    [PostedDate] [datetime2](7) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__JournalEn__Creat__77DFC722] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[JournalEntryDetails] (
    [JournalDetailID] [int] IDENTITY(1,1) NOT NULL,
    [JournalID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [AccountID] [int] NOT NULL,
    [DebitAmount] [decimal](18,2) NULL CONSTRAINT [DF__JournalEn__Debit__78D3EB5B] DEFAULT ((0)),
    [CreditAmount] [decimal](18,2) NULL CONSTRAINT [DF__JournalEn__Credi__79C80F94] DEFAULT ((0)),
    [CostCenterID] [int] NULL,
    [Description] [nvarchar](200) NULL,
    [Reference] [nvarchar](100) NULL,
    [CurrencyID] [int] NULL,
    [ForeignAmount] [decimal](18,2) NULL CONSTRAINT [DF__JournalEn__Forei__7ABC33CD] DEFAULT ((0)),
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__JournalEn__Excha__7BB05806] DEFAULT ((1)),
    [EntityType] [nvarchar](20) NULL,
    [EntityID] [int] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[LeaveRequests] (
    [LeaveRequestID] [int] IDENTITY(1,1) NOT NULL,
    [RequestNumber] [nvarchar](50) NOT NULL,
    [EmployeeID] [int] NOT NULL,
    [LeaveTypeID] [int] NOT NULL,
    [StartDate] [date] NOT NULL,
    [EndDate] [date] NOT NULL,
    [NumberOfDays] [decimal](5,2) NOT NULL,
    [Reason] [nvarchar](500) NULL,
    [RequestStatus] [tinyint] NOT NULL CONSTRAINT [DF__LeaveRequ__Reque__7CA47C3F] DEFAULT ((1)),
    [ApprovedByManager] [int] NULL,
    [ManagerApprovalDate] [datetime2](7) NULL,
    [ApprovedByHR] [int] NULL,
    [HRApprovalDate] [datetime2](7) NULL,
    [RejectionReason] [nvarchar](200) NULL,
    [SubstituteEmployeeID] [int] NULL,
    [Attachment] [nvarchar](500) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__LeaveRequ__Creat__7D98A078] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[LeaveTypes] (
    [LeaveTypeID] [int] IDENTITY(1,1) NOT NULL,
    [LeaveTypeCode] [nvarchar](20) NOT NULL,
    [LeaveTypeNameAr] [nvarchar](100) NOT NULL,
    [LeaveTypeNameEn] [nvarchar](100) NULL,
    [IsPaid] [bit] NULL CONSTRAINT [DF__LeaveType__IsPai__7E8CC4B1] DEFAULT ((1)),
    [MaxDaysPerYear] [int] NULL CONSTRAINT [DF__LeaveType__MaxDa__7F80E8EA] DEFAULT ((0)),
    [RequiresApproval] [bit] NULL CONSTRAINT [DF__LeaveType__Requi__00750D23] DEFAULT ((1)),
    [DeductFromBalance] [bit] NULL CONSTRAINT [DF__LeaveType__Deduc__0169315C] DEFAULT ((1)),
    [AllowNegativeBalance] [bit] NULL CONSTRAINT [DF__LeaveType__Allow__025D5595] DEFAULT ((0)),
    [Color] [nvarchar](7) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__LeaveType__IsAct__035179CE] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__LeaveType__Creat__04459E07] DEFAULT (getdate()),
    [ApplicableGender] [nvarchar](1) NULL CONSTRAINT [DF__LeaveType__Appli__71A7CADF] DEFAULT ('A'));
GO

CREATE TABLE [dbo].[LoanInstallments] (
    [InstallmentID] [int] IDENTITY(1,1) NOT NULL,
    [LoanID] [int] NOT NULL,
    [InstallmentNumber] [int] NOT NULL,
    [DueDate] [date] NOT NULL,
    [Amount] [decimal](18,2) NOT NULL,
    [PaidAmount] [decimal](18,2) NULL CONSTRAINT [DF__LoanInsta__PaidA__0539C240] DEFAULT ((0)),
    [PaidDate] [date] NULL,
    [InstallmentStatus] [tinyint] NULL CONSTRAINT [DF__LoanInsta__Insta__062DE679] DEFAULT ((1)),
    [PayrollID] [int] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[LoginHistory] (
    [LoginID] [int] IDENTITY(1,1) NOT NULL,
    [UserID] [int] NULL,
    [Username] [nvarchar](50) NOT NULL,
    [LoginTime] [datetime2](7) NOT NULL CONSTRAINT [DF__LoginHist__Login__07220AB2] DEFAULT (getdate()),
    [LogoutTime] [datetime2](7) NULL,
    [LoginStatus] [tinyint] NOT NULL,
    [IPAddress] [nvarchar](50) NULL,
    [MachineName] [nvarchar](100) NULL,
    [OSVersion] [nvarchar](100) NULL,
    [AppVersion] [nvarchar](20) NULL,
    [FailureReason] [nvarchar](200) NULL,
    [SessionDuration] AS (datediff(minute,[LoginTime],[LogoutTime])));
GO

CREATE TABLE [dbo].[MaterialIssueDetails] (
    [IssueDetailID] [int] IDENTITY(1,1) NOT NULL,
    [IssueID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [POMaterialID] [int] NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [IssuedQty] [decimal](18,4) NOT NULL,
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__MaterialI__UnitC__08162EEB] DEFAULT ((0)),
    [LineCost] AS (isnull([IssuedQty],(0))*isnull([UnitCost],(0))),
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[MaterialIssueNotes] (
    [IssueID] [int] IDENTITY(1,1) NOT NULL,
    [IssueNumber] [nvarchar](50) NOT NULL,
    [IssueDate] [date] NOT NULL CONSTRAINT [DF__MaterialI__Issue__090A5324] DEFAULT (CONVERT([date],getdate())),
    [ProductionOrderID] [int] NOT NULL,
    [WarehouseID] [int] NOT NULL,
    [IssueType] [tinyint] NULL CONSTRAINT [DF__MaterialI__Issue__09FE775D] DEFAULT ((1)),
    [IssueStatus] [tinyint] NOT NULL CONSTRAINT [DF__MaterialI__Issue__0AF29B96] DEFAULT ((1)),
    [IssuedBy] [int] NULL,
    [ReceivedBy] [int] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__MaterialI__Creat__0BE6BFCF] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[Notifications] (
    [NotificationID] [int] IDENTITY(1,1) NOT NULL,
    [NotificationDate] [datetime2](7) NOT NULL CONSTRAINT [DF__Notificat__Notif__0CDAE408] DEFAULT (getdate()),
    [NotificationType] [tinyint] NOT NULL,
    [Title] [nvarchar](200) NOT NULL,
    [Message] [nvarchar](MAX) NOT NULL,
    [Priority] [tinyint] NULL CONSTRAINT [DF__Notificat__Prior__0DCF0841] DEFAULT ((2)),
    [TargetUserID] [int] NULL,
    [TargetRoleID] [int] NULL,
    [RelatedModule] [nvarchar](50) NULL,
    [RelatedRecordID] [int] NULL,
    [IsRead] [bit] NULL CONSTRAINT [DF__Notificat__IsRea__0EC32C7A] DEFAULT ((0)),
    [ReadDate] [datetime2](7) NULL,
    [IsActioned] [bit] NULL CONSTRAINT [DF__Notificat__IsAct__0FB750B3] DEFAULT ((0)),
    [ActionDate] [datetime2](7) NULL,
    [ExpiryDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Notificat__Creat__10AB74EC] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[NumberSequences] (
    [SequenceID] [int] IDENTITY(1,1) NOT NULL,
    [SequenceCode] [nvarchar](20) NOT NULL,
    [SequenceName] [nvarchar](100) NOT NULL,
    [Prefix] [nvarchar](10) NULL CONSTRAINT [DF__NumberSeq__Prefi__119F9925] DEFAULT (''),
    [CurrentNumber] [int] NULL CONSTRAINT [DF__NumberSeq__Curre__1293BD5E] DEFAULT ((0)),
    [NumberLength] [int] NULL CONSTRAINT [DF__NumberSeq__Numbe__1387E197] DEFAULT ((6)),
    [IncludeYear] [bit] NULL CONSTRAINT [DF__NumberSeq__Inclu__147C05D0] DEFAULT ((1)),
    [ResetYearly] [bit] NULL CONSTRAINT [DF__NumberSeq__Reset__15702A09] DEFAULT ((1)),
    [LastResetYear] [int] NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__NumberSeq__IsAct__16644E42] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__NumberSeq__Creat__1758727B] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[OverheadExpenses] (
    [ExpenseID] [int] IDENTITY(1,1) NOT NULL,
    [PeriodID] [int] NOT NULL,
    [ExpenseDate] [date] NOT NULL,
    [AccountID] [int] NOT NULL,
    [CostCenterID] [int] NULL,
    [Description] [nvarchar](200) NOT NULL,
    [Amount] [decimal](18,2) NOT NULL,
    [AllocationMethod] [tinyint] NULL CONSTRAINT [DF__OverheadE__Alloc__184C96B4] DEFAULT ((1)),
    [AllocationPercent] [decimal](5,2) NULL,
    [IsAllocated] [bit] NULL CONSTRAINT [DF__OverheadE__IsAll__1940BAED] DEFAULT ((0)),
    [JournalID] [int] NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__OverheadE__Creat__1A34DF26] DEFAULT (getdate()),
    [CreditAccountID] [int] NULL,
    [Reference] [nvarchar](100) NULL,
    [ReferenceType] [nvarchar](50) NULL,
    [ExpenseNumber] [nvarchar](30) NULL,
    [ExpenseStatus] [tinyint] NOT NULL CONSTRAINT [DF__OverheadE__Expen__224B023A] DEFAULT ((1)),
    [IsPosted] [bit] NOT NULL CONSTRAINT [DF__OverheadE__IsPos__233F2673] DEFAULT ((0)),
    [FiscalYearID] [int] NULL,
    [PaymentMethod] [tinyint] NULL CONSTRAINT [DF__OverheadE__Payme__24334AAC] DEFAULT ((1)),
    [CashBoxID] [int] NULL,
    [BankAccountID] [int] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime] NULL);
GO

CREATE TABLE [dbo].[PaymentAllocations] (
    [AllocationID] [int] IDENTITY(1,1) NOT NULL,
    [PaymentID] [int] NOT NULL,
    [InvoiceID] [int] NOT NULL,
    [AllocatedAmount] [decimal](18,2) NOT NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PaymentAl__Creat__1B29035F] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[PaymentTerms] (
    [PaymentTermID] [int] IDENTITY(1,1) NOT NULL,
    [TermCode] [nvarchar](20) NOT NULL,
    [TermNameAr] [nvarchar](100) NOT NULL,
    [TermNameEn] [nvarchar](100) NULL,
    [DueDays] [int] NOT NULL CONSTRAINT [DF__PaymentTe__DueDa__1C1D2798] DEFAULT ((0)),
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__PaymentTe__Disco__1D114BD1] DEFAULT ((0)),
    [DiscountDays] [int] NULL CONSTRAINT [DF__PaymentTe__Disco__1E05700A] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__PaymentTe__IsAct__1EF99443] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PaymentTe__Creat__1FEDB87C] DEFAULT (getdate()),
    [CreatedBy] [int] NULL,
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime] NULL);
GO

CREATE TABLE [dbo].[PaymentVouchers] (
    [PaymentID] [int] IDENTITY(1,1) NOT NULL,
    [PaymentNumber] [nvarchar](50) NOT NULL,
    [PaymentDate] [date] NOT NULL CONSTRAINT [DF__PaymentVo__Payme__20E1DCB5] DEFAULT (CONVERT([date],getdate())),
    [PaidTo] [nvarchar](200) NOT NULL,
    [EntityType] [tinyint] NULL,
    [EntityID] [int] NULL,
    [PaymentMethod] [tinyint] NOT NULL,
    [CashBoxID] [int] NULL,
    [BankAccountID] [int] NULL,
    [Amount] [decimal](18,2) NOT NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__PaymentVo__Excha__21D600EE] DEFAULT ((1)),
    [ChequeNumber] [nvarchar](50) NULL,
    [ChequeDate] [date] NULL,
    [ChequeBankName] [nvarchar](100) NULL,
    [TransferRef] [nvarchar](50) NULL,
    [Description] [nvarchar](500) NULL,
    [ExpenseAccountID] [int] NULL,
    [CostCenterID] [int] NULL,
    [PaymentStatus] [tinyint] NOT NULL CONSTRAINT [DF__PaymentVo__Payme__22CA2527] DEFAULT ((1)),
    [JournalID] [int] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PaymentVo__Creat__23BE4960] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [AmountLocal] [decimal](18,2) NULL,
    [ReferenceNo] [nvarchar](100) NULL,
    [ReferenceType] [nvarchar](50) NULL,
    [ReferenceID] [int] NULL,
    [IsPosted] [bit] NOT NULL CONSTRAINT [DF__PaymentVo__IsPos__0C5BC11B] DEFAULT ((0)),
    [SourceType] [int] NULL CONSTRAINT [DF__PaymentVo__Sourc__0D4FE554] DEFAULT ((1)));
GO

CREATE TABLE [dbo].[Payroll] (
    [PayrollID] [int] IDENTITY(1,1) NOT NULL,
    [PayrollNumber] [nvarchar](50) NOT NULL,
    [PayrollMonth] [date] NOT NULL,
    [PeriodID] [int] NULL,
    [PayrollTitle] [nvarchar](100) NULL,
    [TotalBasicSalary] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalBa__24B26D99] DEFAULT ((0)),
    [TotalAllowances] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalAl__25A691D2] DEFAULT ((0)),
    [TotalOvertime] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalOv__269AB60B] DEFAULT ((0)),
    [TotalIncentives] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalIn__278EDA44] DEFAULT ((0)),
    [TotalCommissions] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalCo__2882FE7D] DEFAULT ((0)),
    [TotalGrossEarnings] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalGr__297722B6] DEFAULT ((0)),
    [TotalInsuranceEmp] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalIn__2A6B46EF] DEFAULT ((0)),
    [TotalInsuranceComp] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalIn__2B5F6B28] DEFAULT ((0)),
    [TotalTax] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalTa__2C538F61] DEFAULT ((0)),
    [TotalLoanDeductions] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalLo__2D47B39A] DEFAULT ((0)),
    [TotalPenalties] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalPe__2E3BD7D3] DEFAULT ((0)),
    [TotalAbsenceDeductions] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalAb__2F2FFC0C] DEFAULT ((0)),
    [TotalOtherDeductions] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalOt__30242045] DEFAULT ((0)),
    [TotalDeductions] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalDe__3118447E] DEFAULT ((0)),
    [TotalNetSalary] [decimal](18,2) NULL CONSTRAINT [DF__Payroll__TotalNe__320C68B7] DEFAULT ((0)),
    [EmployeeCount] [int] NULL CONSTRAINT [DF__Payroll__Employe__33008CF0] DEFAULT ((0)),
    [PayrollStatus] [tinyint] NOT NULL CONSTRAINT [DF__Payroll__Payroll__33F4B129] DEFAULT ((1)),
    [CalculatedBy] [int] NULL,
    [CalculatedDate] [datetime2](7) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [PaidBy] [int] NULL,
    [PaidDate] [datetime2](7) NULL,
    [JournalID] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Payroll__Created__34E8D562] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[PayrollDetails] (
    [PayrollDetailID] [int] IDENTITY(1,1) NOT NULL,
    [PayrollID] [int] NOT NULL,
    [EmployeeID] [int] NOT NULL,
    [WorkingDays] [decimal](5,2) NULL CONSTRAINT [DF__PayrollDe__Worki__35DCF99B] DEFAULT ((30)),
    [PresentDays] [decimal](5,2) NULL CONSTRAINT [DF__PayrollDe__Prese__36D11DD4] DEFAULT ((0)),
    [AbsentDays] [decimal](5,2) NULL CONSTRAINT [DF__PayrollDe__Absen__37C5420D] DEFAULT ((0)),
    [LeaveDays] [decimal](5,2) NULL CONSTRAINT [DF__PayrollDe__Leave__38B96646] DEFAULT ((0)),
    [OvertimeHours] [decimal](5,2) NULL CONSTRAINT [DF__PayrollDe__Overt__39AD8A7F] DEFAULT ((0)),
    [LateDays] [decimal](5,2) NULL CONSTRAINT [DF__PayrollDe__LateD__3AA1AEB8] DEFAULT ((0)),
    [BasicSalary] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Basic__3B95D2F1] DEFAULT ((0)),
    [TransportAllowance] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Trans__3C89F72A] DEFAULT ((0)),
    [HousingAllowance] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Housi__3D7E1B63] DEFAULT ((0)),
    [PhoneAllowance] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Phone__3E723F9C] DEFAULT ((0)),
    [FoodAllowance] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__FoodA__3F6663D5] DEFAULT ((0)),
    [OtherAllowances] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Other__405A880E] DEFAULT ((0)),
    [OvertimeAmount] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Overt__414EAC47] DEFAULT ((0)),
    [Incentives] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Incen__4242D080] DEFAULT ((0)),
    [Commissions] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Commi__4336F4B9] DEFAULT ((0)),
    [Rewards] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Rewar__442B18F2] DEFAULT ((0)),
    [OtherEarnings] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Other__451F3D2B] DEFAULT ((0)),
    [GrossEarnings] AS ((((((((((isnull([BasicSalary],(0))+isnull([TransportAllowance],(0)))+isnull([HousingAllowance],(0)))+isnull([PhoneAllowance],(0)))+isnull([FoodAllowance],(0)))+isnull([OtherAllowances],(0)))+isnull([OvertimeAmount],(0)))+isnull([Incentives],(0)))+isnull([Commissions],(0)))+isnull([Rewards],(0)))+isnull([OtherEarnings],(0))),
    [InsuranceEmployee] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Insur__46136164] DEFAULT ((0)),
    [InsuranceCompany] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Insur__4707859D] DEFAULT ((0)),
    [IncomeTax] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Incom__47FBA9D6] DEFAULT ((0)),
    [LoanDeduction] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__LoanD__48EFCE0F] DEFAULT ((0)),
    [PenaltyDeduction] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Penal__49E3F248] DEFAULT ((0)),
    [AbsenceDeduction] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Absen__4AD81681] DEFAULT ((0)),
    [LateDeduction] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__LateD__4BCC3ABA] DEFAULT ((0)),
    [OtherDeductions] [decimal](18,2) NULL CONSTRAINT [DF__PayrollDe__Other__4CC05EF3] DEFAULT ((0)),
    [TotalDeductions] AS ((((((isnull([InsuranceEmployee],(0))+isnull([IncomeTax],(0)))+isnull([LoanDeduction],(0)))+isnull([PenaltyDeduction],(0)))+isnull([AbsenceDeduction],(0)))+isnull([LateDeduction],(0)))+isnull([OtherDeductions],(0))),
    [NetSalary] AS (((((((((((((((((isnull([BasicSalary],(0))+isnull([TransportAllowance],(0)))+isnull([HousingAllowance],(0)))+isnull([PhoneAllowance],(0)))+isnull([FoodAllowance],(0)))+isnull([OtherAllowances],(0)))+isnull([OvertimeAmount],(0)))+isnull([Incentives],(0)))+isnull([Commissions],(0)))+isnull([Rewards],(0)))+isnull([OtherEarnings],(0)))-isnull([InsuranceEmployee],(0)))-isnull([IncomeTax],(0)))-isnull([LoanDeduction],(0)))-isnull([PenaltyDeduction],(0)))-isnull([AbsenceDeduction],(0)))-isnull([LateDeduction],(0)))-isnull([OtherDeductions],(0))),
    [PaymentMethod] [tinyint] NULL CONSTRAINT [DF__PayrollDe__Payme__4DB4832C] DEFAULT ((1)),
    [IsPaid] [bit] NULL CONSTRAINT [DF__PayrollDe__IsPai__4EA8A765] DEFAULT ((0)),
    [PaidDate] [date] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[PriceListDetails] (
    [PriceDetailID] [int] IDENTITY(1,1) NOT NULL,
    [PriceListID] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [UnitPrice] [decimal](18,4) NOT NULL,
    [MinQty] [decimal](18,4) NULL CONSTRAINT [DF__PriceList__MinQt__4F9CCB9E] DEFAULT ((1)),
    [MaxQty] [decimal](18,4) NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__PriceList__Disco__5090EFD7] DEFAULT ((0)),
    [StartDate] [date] NULL,
    [EndDate] [date] NULL,
    [MinSellingPrice] [decimal](18,4) NULL CONSTRAINT [DF__PriceList__MinSe__51851410] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__PriceList__IsAct__52793849] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PriceList__Creat__536D5C82] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[PriceLists] (
    [PriceListID] [int] IDENTITY(1,1) NOT NULL,
    [PriceListCode] [nvarchar](20) NOT NULL,
    [PriceListNameAr] [nvarchar](100) NOT NULL,
    [PriceListNameEn] [nvarchar](100) NULL,
    [PriceListType] [tinyint] NOT NULL CONSTRAINT [DF__PriceList__Price__546180BB] DEFAULT ((1)),
    [CurrencyID] [int] NULL,
    [StartDate] [date] NULL,
    [EndDate] [date] NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__PriceList__Disco__5555A4F4] DEFAULT ((0)),
    [IsDefault] [bit] NULL CONSTRAINT [DF__PriceList__IsDef__5649C92D] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__PriceList__IsAct__573DED66] DEFAULT ((1)),
    [Notes] [nvarchar](200) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PriceList__Creat__5832119F] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[ProductCostCards] (
    [CostCardID] [int] IDENTITY(1,1) NOT NULL,
    [ItemID] [int] NOT NULL,
    [PeriodID] [int] NULL,
    [CalculationDate] [date] NOT NULL CONSTRAINT [DF__ProductCo__Calcu__592635D8] DEFAULT (CONVERT([date],getdate())),
    [RawMaterialCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__RawMa__5A1A5A11] DEFAULT ((0)),
    [PackagingCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Packa__5B0E7E4A] DEFAULT ((0)),
    [DirectLaborCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Direc__5C02A283] DEFAULT ((0)),
    [IndirectMaterialCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Indir__5CF6C6BC] DEFAULT ((0)),
    [IndirectLaborCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Indir__5DEAEAF5] DEFAULT ((0)),
    [UtilityCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Utili__5EDF0F2E] DEFAULT ((0)),
    [DepreciationCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Depre__5FD33367] DEFAULT ((0)),
    [MaintenanceCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Maint__60C757A0] DEFAULT ((0)),
    [OtherOverheadCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Other__61BB7BD9] DEFAULT ((0)),
    [TotalDirectCost] AS ((isnull([RawMaterialCost],(0))+isnull([PackagingCost],(0)))+isnull([DirectLaborCost],(0))),
    [TotalIndirectCost] AS (((((isnull([IndirectMaterialCost],(0))+isnull([IndirectLaborCost],(0)))+isnull([UtilityCost],(0)))+isnull([DepreciationCost],(0)))+isnull([MaintenanceCost],(0)))+isnull([OtherOverheadCost],(0))),
    [TotalCost] AS ((((((((isnull([RawMaterialCost],(0))+isnull([PackagingCost],(0)))+isnull([DirectLaborCost],(0)))+isnull([IndirectMaterialCost],(0)))+isnull([IndirectLaborCost],(0)))+isnull([UtilityCost],(0)))+isnull([DepreciationCost],(0)))+isnull([MaintenanceCost],(0)))+isnull([OtherOverheadCost],(0))),
    [WastePercent] [decimal](5,2) NULL CONSTRAINT [DF__ProductCo__Waste__62AFA012] DEFAULT ((0)),
    [WasteCost] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Waste__63A3C44B] DEFAULT ((0)),
    [CostPerUnit] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__CostP__6497E884] DEFAULT ((0)),
    [TargetProfitPercent] [decimal](5,2) NULL CONSTRAINT [DF__ProductCo__Targe__658C0CBD] DEFAULT ((0)),
    [SuggestedPrice] [decimal](18,4) NULL CONSTRAINT [DF__ProductCo__Sugge__668030F6] DEFAULT ((0)),
    [CostType] [tinyint] NULL CONSTRAINT [DF__ProductCo__CostT__6774552F] DEFAULT ((1)),
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__ProductCo__Creat__68687968] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[ProductionBatches] (
    [BatchID] [int] IDENTITY(1,1) NOT NULL,
    [BatchNumber] [nvarchar](50) NOT NULL,
    [ProductionOrderID] [int] NOT NULL,
    [ProductItemID] [int] NOT NULL,
    [BatchDate] [date] NOT NULL CONSTRAINT [DF__Productio__Batch__695C9DA1] DEFAULT (CONVERT([date],getdate())),
    [ExpiryDate] [date] NULL,
    [PlannedQty] [decimal](18,4) NOT NULL,
    [ProducedQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Produ__6A50C1DA] DEFAULT ((0)),
    [GoodQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__GoodQ__6B44E613] DEFAULT ((0)),
    [RejectedQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Rejec__6C390A4C] DEFAULT ((0)),
    [WasteQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Waste__6D2D2E85] DEFAULT ((0)),
    [SampleQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Sampl__6E2152BE] DEFAULT ((0)),
    [UnitID] [int] NOT NULL,
    [TargetWarehouseID] [int] NULL,
    [MaterialCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Mater__6F1576F7] DEFAULT ((0)),
    [LaborCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Labor__70099B30] DEFAULT ((0)),
    [OverheadCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Overh__70FDBF69] DEFAULT ((0)),
    [TotalCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Total__71F1E3A2] DEFAULT ((0)),
    [UnitCost] AS (case when isnull([GoodQty],(0))>(0) then isnull([TotalCost],(0))/[GoodQty] else (0) end),
    [BatchStatus] [tinyint] NOT NULL CONSTRAINT [DF__Productio__Batch__72E607DB] DEFAULT ((1)),
    [QCInspectedBy] [int] NULL,
    [QCInspectionDate] [datetime2](7) NULL,
    [QCResult] [tinyint] NULL,
    [QCNotes] [nvarchar](MAX) NULL,
    [StartTime] [datetime2](7) NULL,
    [EndTime] [datetime2](7) NULL,
    [ProducedBy] [int] NULL,
    [SupervisorID] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Productio__Creat__73DA2C14] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[ProductionBatchStages] (
    [BatchStageID] [int] IDENTITY(1,1) NOT NULL,
    [BatchID] [int] NOT NULL,
    [StageID] [int] NOT NULL,
    [StageOrder] [int] NOT NULL,
    [StartTime] [datetime2](7) NULL,
    [EndTime] [datetime2](7) NULL,
    [Duration] AS (datediff(minute,[StartTime],[EndTime])),
    [Temperature] [nvarchar](50) NULL,
    [Speed] [nvarchar](50) NULL,
    [Humidity] [nvarchar](50) NULL,
    [OperatorID] [int] NULL,
    [StageStatus] [tinyint] NULL CONSTRAINT [DF__Productio__Stage__74CE504D] DEFAULT ((1)),
    [QualityCheck] [bit] NULL CONSTRAINT [DF__Productio__Quali__75C27486] DEFAULT ((0)),
    [QualityResult] [nvarchar](200) NULL,
    [Notes] [nvarchar](MAX) NULL);
GO

CREATE TABLE [dbo].[ProductionOrderMaterials] (
    [POMaterialID] [int] IDENTITY(1,1) NOT NULL,
    [ProductionOrderID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [MaterialType] [tinyint] NOT NULL,
    [RequiredQty] [decimal](18,4) NOT NULL,
    [IssuedQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Issue__76B698BF] DEFAULT ((0)),
    [ReturnedQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Retur__77AABCF8] DEFAULT ((0)),
    [ConsumedQty] AS (isnull([IssuedQty],(0))-isnull([ReturnedQty],(0))),
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__Productio__UnitC__789EE131] DEFAULT ((0)),
    [LineCost] AS ((isnull([IssuedQty],(0))-isnull([ReturnedQty],(0)))*isnull([UnitCost],(0))),
    [AvailableStock] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Avail__7993056A] DEFAULT ((0)),
    [LineStatus] [tinyint] NULL CONSTRAINT [DF__Productio__LineS__7A8729A3] DEFAULT ((1)),
    [BOMDetailID] [int] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[ProductionOrders] (
    [ProductionOrderID] [int] IDENTITY(1,1) NOT NULL,
    [OrderNumber] [nvarchar](50) NOT NULL,
    [OrderDate] [date] NOT NULL CONSTRAINT [DF__Productio__Order__7B7B4DDC] DEFAULT (CONVERT([date],getdate())),
    [BOMID] [int] NOT NULL,
    [ProductItemID] [int] NOT NULL,
    [PlannedQty] [decimal](18,4) NOT NULL,
    [PlannedUnitID] [int] NOT NULL,
    [ActualQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Actua__7C6F7215] DEFAULT ((0)),
    [WasteQty] [decimal](18,4) NULL CONSTRAINT [DF__Productio__Waste__7D63964E] DEFAULT ((0)),
    [NumberOfBatches] [int] NULL CONSTRAINT [DF__Productio__Numbe__7E57BA87] DEFAULT ((1)),
    [PlannedStartDate] [date] NULL,
    [PlannedEndDate] [date] NULL,
    [ActualStartDate] [datetime2](7) NULL,
    [ActualEndDate] [datetime2](7) NULL,
    [SourceWarehouseID] [int] NULL,
    [TargetWarehouseID] [int] NULL,
    [OrderStatus] [tinyint] NOT NULL CONSTRAINT [DF__Productio__Order__7F4BDEC0] DEFAULT ((1)),
    [OrderPriority] [tinyint] NULL CONSTRAINT [DF__Productio__Order__004002F9] DEFAULT ((2)),
    [EstimatedMaterialCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Estim__01342732] DEFAULT ((0)),
    [EstimatedLaborCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Estim__02284B6B] DEFAULT ((0)),
    [EstimatedOverheadCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Estim__031C6FA4] DEFAULT ((0)),
    [EstimatedTotalCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Estim__041093DD] DEFAULT ((0)),
    [ActualMaterialCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Actua__0504B816] DEFAULT ((0)),
    [ActualLaborCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Actua__05F8DC4F] DEFAULT ((0)),
    [ActualOverheadCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Actua__06ED0088] DEFAULT ((0)),
    [ActualTotalCost] [decimal](18,2) NULL CONSTRAINT [DF__Productio__Actua__07E124C1] DEFAULT ((0)),
    [AssignedTo] [int] NULL,
    [SupervisorID] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Productio__Creat__08D548FA] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [ContractOrderID] [int] NULL);
GO

CREATE TABLE [dbo].[ProductionStages] (
    [StageID] [int] IDENTITY(1,1) NOT NULL,
    [StageCode] [nvarchar](20) NOT NULL,
    [StageNameAr] [nvarchar](100) NOT NULL,
    [StageNameEn] [nvarchar](100) NULL,
    [StageOrder] [int] NOT NULL CONSTRAINT [DF__Productio__Stage__09C96D33] DEFAULT ((0)),
    [DefaultDuration] [decimal](10,2) NULL,
    [Description] [nvarchar](200) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__Productio__IsAct__0ABD916C] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Productio__Creat__0BB1B5A5] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[PublicHolidays] (
    [HolidayID] [int] IDENTITY(1,1) NOT NULL,
    [HolidayDate] [date] NOT NULL,
    [HolidayNameAr] [nvarchar](100) NOT NULL,
    [HolidayNameEn] [nvarchar](100) NULL,
    [FiscalYearID] [int] NULL,
    [IsRecurring] [bit] NULL CONSTRAINT [DF__PublicHol__IsRec__0CA5D9DE] DEFAULT ((0)),
    [Notes] [nvarchar](200) NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PublicHol__Creat__0D99FE17] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[PurchaseInvoiceDetails] (
    [InvoiceDetailID] [int] IDENTITY(1,1) NOT NULL,
    [InvoiceID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [WarehouseID] [int] NULL,
    [Quantity] [decimal](18,4) NOT NULL,
    [UnitPrice] [decimal](18,4) NOT NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__PurchaseI__Disco__0E8E2250] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__Disco__0F824689] DEFAULT ((0)),
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__LineT__10766AC2] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__PurchaseI__TaxRa__116A8EFB] DEFAULT ((14.00)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__TaxAm__125EB334] DEFAULT ((0)),
    [LineTotalWithTax] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__LineT__1352D76D] DEFAULT ((0)),
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [GRNDetailID] [int] NULL,
    [PODetailID] [int] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[PurchaseInvoices] (
    [InvoiceID] [int] IDENTITY(1,1) NOT NULL,
    [InvoiceNumber] [nvarchar](50) NOT NULL,
    [InvoiceDate] [date] NOT NULL CONSTRAINT [DF__PurchaseI__Invoi__1446FBA6] DEFAULT (CONVERT([date],getdate())),
    [SupplierID] [int] NOT NULL,
    [PurchaseOrderID] [int] NULL,
    [GRNID] [int] NULL,
    [SupplierInvoiceNo] [nvarchar](50) NULL,
    [SupplierInvoiceDate] [date] NULL,
    [PaymentTermID] [int] NULL,
    [DueDate] [date] NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__PurchaseI__Excha__153B1FDF] DEFAULT ((1)),
    [SubTotal] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__SubTo__162F4418] DEFAULT ((0)),
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__PurchaseI__Disco__17236851] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__Disco__18178C8A] DEFAULT ((0)),
    [TaxableAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__Taxab__190BB0C3] DEFAULT ((0)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__TaxAm__19FFD4FC] DEFAULT ((0)),
    [ShippingCost] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__Shipp__1AF3F935] DEFAULT ((0)),
    [OtherCosts] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__Other__1BE81D6E] DEFAULT ((0)),
    [TotalAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__Total__1CDC41A7] DEFAULT ((0)),
    [PaidAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseI__PaidA__1DD065E0] DEFAULT ((0)),
    [RemainingAmount] AS ([TotalAmount]-isnull([PaidAmount],(0))),
    [InvoiceStatus] [tinyint] NOT NULL CONSTRAINT [DF__PurchaseI__Invoi__1EC48A19] DEFAULT ((1)),
    [PaymentStatus] [tinyint] NULL CONSTRAINT [DF__PurchaseI__Payme__1FB8AE52] DEFAULT ((1)),
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PurchaseI__Creat__20ACD28B] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [InvoiceSource] [tinyint] NULL CONSTRAINT [DF__PurchaseI__Invoi__57E7F8DC] DEFAULT ((1)),
    [SubmittedBy] [int] NULL,
    [SubmittedDate] [datetime2](7) NULL,
    [RejectedBy] [int] NULL,
    [RejectedDate] [datetime2](7) NULL,
    [RejectionReason] [nvarchar](500) NULL);
GO

CREATE TABLE [dbo].[PurchaseOrderDetails] (
    [PODetailID] [int] IDENTITY(1,1) NOT NULL,
    [PurchaseOrderID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [OrderedQty] [decimal](18,4) NOT NULL,
    [ReceivedQty] [decimal](18,4) NULL CONSTRAINT [DF__PurchaseO__Recei__21A0F6C4] DEFAULT ((0)),
    [RemainingQty] AS ([OrderedQty]-isnull([ReceivedQty],(0))),
    [UnitPrice] [decimal](18,4) NOT NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__PurchaseO__Disco__22951AFD] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__Disco__23893F36] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__PurchaseO__TaxRa__247D636F] DEFAULT ((14.00)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__TaxAm__257187A8] DEFAULT ((0)),
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__LineT__2665ABE1] DEFAULT ((0)),
    [LineTotalWithTax] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__LineT__2759D01A] DEFAULT ((0)),
    [ExpectedDate] [date] NULL,
    [Notes] [nvarchar](200) NULL,
    [LineStatus] [tinyint] NULL CONSTRAINT [DF__PurchaseO__LineS__284DF453] DEFAULT ((1)),
    [RequestDetailID] [int] NULL);
GO

CREATE TABLE [dbo].[PurchaseOrders] (
    [PurchaseOrderID] [int] IDENTITY(1,1) NOT NULL,
    [PONumber] [nvarchar](50) NOT NULL,
    [PODate] [date] NOT NULL CONSTRAINT [DF__PurchaseO__PODat__2942188C] DEFAULT (CONVERT([date],getdate())),
    [SupplierID] [int] NOT NULL,
    [RequestID] [int] NULL,
    [ExpectedDeliveryDate] [date] NULL,
    [PaymentTermID] [int] NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__PurchaseO__Excha__2A363CC5] DEFAULT ((1)),
    [WarehouseID] [int] NULL,
    [SubTotal] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__SubTo__2B2A60FE] DEFAULT ((0)),
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__PurchaseO__Disco__2C1E8537] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__Disco__2D12A970] DEFAULT ((0)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__TaxAm__2E06CDA9] DEFAULT ((0)),
    [ShippingCost] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__Shipp__2EFAF1E2] DEFAULT ((0)),
    [OtherCosts] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__Other__2FEF161B] DEFAULT ((0)),
    [TotalAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__Total__30E33A54] DEFAULT ((0)),
    [POStatus] [tinyint] NOT NULL CONSTRAINT [DF__PurchaseO__POSta__31D75E8D] DEFAULT ((1)),
    [ShippingAddress] [nvarchar](300) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [InternalNotes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [ClosedBy] [int] NULL,
    [ClosedDate] [datetime2](7) NULL,
    [CancelledBy] [int] NULL,
    [CancelledDate] [datetime2](7) NULL,
    [CancellationReason] [nvarchar](500) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PurchaseO__Creat__32CB82C6] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [SubmittedBy] [int] NULL,
    [SubmittedDate] [datetime2](7) NULL,
    [RejectedBy] [int] NULL,
    [RejectedDate] [datetime2](7) NULL,
    [RejectionReason] [nvarchar](500) NULL,
    [SentBy] [int] NULL,
    [SentDate] [datetime2](7) NULL,
    [SentMethod] [nvarchar](50) NULL,
    [POSource] [tinyint] NULL CONSTRAINT [DF__PurchaseO__POSou__47B19113] DEFAULT ((1)),
    [SupplierBalance] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__Suppl__48A5B54C] DEFAULT ((0)),
    [SupplierCreditLimit] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseO__Suppl__4999D985] DEFAULT ((0)));
GO

CREATE TABLE [dbo].[PurchaseRequestDetails] (
    [RequestDetailID] [int] IDENTITY(1,1) NOT NULL,
    [RequestID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [RequestedQty] [decimal](18,4) NOT NULL,
    [ApprovedQty] [decimal](18,4) NULL,
    [EstimatedPrice] [decimal](18,4) NULL CONSTRAINT [DF__PurchaseR__Estim__33BFA6FF] DEFAULT ((0)),
    [CurrentStock] [decimal](18,4) NULL CONSTRAINT [DF__PurchaseR__Curre__34B3CB38] DEFAULT ((0)),
    [Purpose] [nvarchar](200) NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[PurchaseRequests] (
    [RequestID] [int] IDENTITY(1,1) NOT NULL,
    [RequestNumber] [nvarchar](50) NOT NULL,
    [RequestDate] [date] NOT NULL CONSTRAINT [DF__PurchaseR__Reque__35A7EF71] DEFAULT (CONVERT([date],getdate())),
    [RequiredDate] [date] NULL,
    [RequestedBy] [int] NULL,
    [DepartmentID] [int] NULL,
    [RequestStatus] [tinyint] NOT NULL CONSTRAINT [DF__PurchaseR__Reque__369C13AA] DEFAULT ((1)),
    [Priority] [tinyint] NULL CONSTRAINT [DF__PurchaseR__Prior__379037E3] DEFAULT ((2)),
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [RejectionReason] [nvarchar](500) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PurchaseR__Creat__38845C1C] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[PurchaseReturnDetails] (
    [ReturnDetailID] [int] IDENTITY(1,1) NOT NULL,
    [ReturnID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [Quantity] [decimal](18,4) NOT NULL,
    [UnitPrice] [decimal](18,4) NOT NULL,
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseR__LineT__39788055] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__PurchaseR__TaxRa__3A6CA48E] DEFAULT ((14.00)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseR__TaxAm__3B60C8C7] DEFAULT ((0)),
    [LineTotalWithTax] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseR__LineT__3C54ED00] DEFAULT ((0)),
    [BatchNumber] [nvarchar](50) NULL,
    [ReturnReason] [nvarchar](200) NULL,
    [ItemCondition] [tinyint] NULL CONSTRAINT [DF__PurchaseR__ItemC__3D491139] DEFAULT ((1)),
    [InvoiceDetailID] [int] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[PurchaseReturns] (
    [ReturnID] [int] IDENTITY(1,1) NOT NULL,
    [ReturnNumber] [nvarchar](50) NOT NULL,
    [ReturnDate] [date] NOT NULL CONSTRAINT [DF__PurchaseR__Retur__3E3D3572] DEFAULT (CONVERT([date],getdate())),
    [SupplierID] [int] NOT NULL,
    [InvoiceID] [int] NULL,
    [WarehouseID] [int] NOT NULL,
    [ReturnReason] [nvarchar](500) NULL,
    [ReturnType] [tinyint] NULL CONSTRAINT [DF__PurchaseR__Retur__3F3159AB] DEFAULT ((1)),
    [SubTotal] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseR__SubTo__40257DE4] DEFAULT ((0)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseR__TaxAm__4119A21D] DEFAULT ((0)),
    [TotalAmount] [decimal](18,2) NULL CONSTRAINT [DF__PurchaseR__Total__420DC656] DEFAULT ((0)),
    [ReturnStatus] [tinyint] NOT NULL CONSTRAINT [DF__PurchaseR__Retur__4301EA8F] DEFAULT ((1)),
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__PurchaseR__Creat__43F60EC8] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [ReturnSource] [tinyint] NULL CONSTRAINT [DF__PurchaseR__Retur__607D3EDD] DEFAULT ((1)),
    [SubmittedBy] [int] NULL,
    [SubmittedDate] [datetime2](7) NULL,
    [RejectedBy] [int] NULL,
    [RejectedDate] [datetime2](7) NULL,
    [RejectionReason] [nvarchar](500) NULL);
GO

CREATE TABLE [dbo].[ReceiptAllocations] (
    [AllocationID] [int] IDENTITY(1,1) NOT NULL,
    [ReceiptID] [int] NOT NULL,
    [InvoiceID] [int] NOT NULL,
    [AllocatedAmount] [decimal](18,2) NOT NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__ReceiptAl__Creat__44EA3301] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[ReceiptVouchers] (
    [ReceiptID] [int] IDENTITY(1,1) NOT NULL,
    [ReceiptNumber] [nvarchar](50) NOT NULL,
    [ReceiptDate] [date] NOT NULL CONSTRAINT [DF__ReceiptVo__Recei__45DE573A] DEFAULT (CONVERT([date],getdate())),
    [ReceivedFrom] [nvarchar](200) NOT NULL,
    [EntityType] [tinyint] NULL,
    [EntityID] [int] NULL,
    [PaymentMethod] [tinyint] NOT NULL,
    [CashBoxID] [int] NULL,
    [BankAccountID] [int] NULL,
    [Amount] [decimal](18,2) NOT NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__ReceiptVo__Excha__46D27B73] DEFAULT ((1)),
    [ChequeNumber] [nvarchar](50) NULL,
    [ChequeDate] [date] NULL,
    [ChequeBankName] [nvarchar](100) NULL,
    [TransferRef] [nvarchar](50) NULL,
    [Description] [nvarchar](500) NULL,
    [ReceiptStatus] [tinyint] NOT NULL CONSTRAINT [DF__ReceiptVo__Recei__47C69FAC] DEFAULT ((1)),
    [JournalID] [int] NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__ReceiptVo__Creat__48BAC3E5] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [RevenueAccountID] [int] NULL,
    [CostCenterID] [int] NULL,
    [AmountLocal] [decimal](18,2) NULL,
    [SourceType] [int] NULL CONSTRAINT [DF__ReceiptVo__Sourc__0A7378A9] DEFAULT ((1)),
    [ReferenceNo] [nvarchar](100) NULL,
    [ReferenceType] [nvarchar](50) NULL,
    [ReferenceID] [int] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime] NULL,
    [IsPosted] [bit] NOT NULL CONSTRAINT [DF__ReceiptVo__IsPos__0B679CE2] DEFAULT ((0)));
GO

CREATE TABLE [dbo].[RolePermissions] (
    [PermissionID] [int] IDENTITY(1,1) NOT NULL,
    [RoleID] [int] NOT NULL,
    [ModuleID] [int] NOT NULL,
    [CanView] [bit] NULL CONSTRAINT [DF__RolePermi__CanVi__49AEE81E] DEFAULT ((0)),
    [CanAdd] [bit] NULL CONSTRAINT [DF__RolePermi__CanAd__4AA30C57] DEFAULT ((0)),
    [CanEdit] [bit] NULL CONSTRAINT [DF__RolePermi__CanEd__4B973090] DEFAULT ((0)),
    [CanDelete] [bit] NULL CONSTRAINT [DF__RolePermi__CanDe__4C8B54C9] DEFAULT ((0)),
    [CanPrint] [bit] NULL CONSTRAINT [DF__RolePermi__CanPr__4D7F7902] DEFAULT ((0)),
    [CanExport] [bit] NULL CONSTRAINT [DF__RolePermi__CanEx__4E739D3B] DEFAULT ((0)),
    [CanApprove] [bit] NULL CONSTRAINT [DF__RolePermi__CanAp__4F67C174] DEFAULT ((0)),
    [CanPost] [bit] NULL CONSTRAINT [DF__RolePermi__CanPo__505BE5AD] DEFAULT ((0)),
    [CanCancel] [bit] NULL CONSTRAINT [DF__RolePermi__CanCa__515009E6] DEFAULT ((0)),
    [Notes] [nvarchar](200) NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__RolePermi__Creat__52442E1F] DEFAULT (getdate()),
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[SalesInvoiceDetails] (
    [InvoiceDetailID] [int] IDENTITY(1,1) NOT NULL,
    [InvoiceID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [WarehouseID] [int] NULL,
    [Quantity] [decimal](18,4) NOT NULL,
    [UnitPrice] [decimal](18,4) NOT NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesInvo__Disco__53385258] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__Disco__542C7691] DEFAULT ((0)),
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__LineT__55209ACA] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__SalesInvo__TaxRa__5614BF03] DEFAULT ((14.00)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__TaxAm__5708E33C] DEFAULT ((0)),
    [LineTotalWithTax] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__LineT__57FD0775] DEFAULT ((0)),
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__SalesInvo__UnitC__58F12BAE] DEFAULT ((0)),
    [LineCost] AS (isnull([Quantity],(0))*isnull([UnitCost],(0))),
    [LineProfit] AS (isnull([LineTotal],(0))-isnull([Quantity],(0))*isnull([UnitCost],(0))),
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [SODetailID] [int] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[SalesInvoices] (
    [InvoiceID] [int] IDENTITY(1,1) NOT NULL,
    [InvoiceNumber] [nvarchar](50) NOT NULL,
    [InvoiceDate] [date] NOT NULL CONSTRAINT [DF__SalesInvo__Invoi__59E54FE7] DEFAULT (CONVERT([date],getdate())),
    [CustomerID] [int] NOT NULL,
    [SalesOrderID] [int] NULL,
    [SalesRepID] [int] NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__SalesInvo__Excha__5AD97420] DEFAULT ((1)),
    [PaymentTermID] [int] NULL,
    [DueDate] [date] NULL,
    [WarehouseID] [int] NULL,
    [SubTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__SubTo__5BCD9859] DEFAULT ((0)),
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesInvo__Disco__5CC1BC92] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__Disco__5DB5E0CB] DEFAULT ((0)),
    [TaxableAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__Taxab__5EAA0504] DEFAULT ((0)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__TaxAm__5F9E293D] DEFAULT ((0)),
    [ShippingCost] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__Shipp__60924D76] DEFAULT ((0)),
    [TotalAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__Total__618671AF] DEFAULT ((0)),
    [TotalCost] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__Total__627A95E8] DEFAULT ((0)),
    [GrossProfit] AS ((isnull([SubTotal],(0))-isnull([DiscountAmount],(0)))-isnull([TotalCost],(0))),
    [GrossProfitPercent] AS (case when isnull([TotalCost],(0))>(0) then (((isnull([SubTotal],(0))-isnull([DiscountAmount],(0)))-isnull([TotalCost],(0)))/isnull([TotalCost],(0)))*(100) else (0) end),
    [PaidAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesInvo__PaidA__636EBA21] DEFAULT ((0)),
    [RemainingAmount] AS (isnull([TotalAmount],(0))-isnull([PaidAmount],(0))),
    [InvoiceStatus] [tinyint] NOT NULL CONSTRAINT [DF__SalesInvo__Invoi__6462DE5A] DEFAULT ((1)),
    [PaymentStatus] [tinyint] NULL CONSTRAINT [DF__SalesInvo__Payme__65570293] DEFAULT ((1)),
    [InvoiceType] [tinyint] NULL CONSTRAINT [DF__SalesInvo__Invoi__664B26CC] DEFAULT ((1)),
    [DeliveryDate] [date] NULL,
    [DeliveryAddress] [nvarchar](300) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SalesInvo__Creat__673F4B05] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [IsTaxable] [bit] NOT NULL CONSTRAINT [DF__SalesInvo__IsTax__7F01C5FD] DEFAULT ((1)),
    [TaxRate] [decimal](5,2) NOT NULL CONSTRAINT [DF__SalesInvo__TaxRa__7FF5EA36] DEFAULT ((14)),
    [InvoiceSource] [int] NOT NULL CONSTRAINT [DF__SalesInvo__Invoi__00EA0E6F] DEFAULT ((3)),
    [DeliveryID] [int] NULL,
    [OtherCosts] [decimal](18,2) NOT NULL CONSTRAINT [DF__SalesInvo__Other__01DE32A8] DEFAULT ((0)));
GO

CREATE TABLE [dbo].[SalesOrderDetails] (
    [SODetailID] [int] IDENTITY(1,1) NOT NULL,
    [SalesOrderID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [OrderedQty] [decimal](18,4) NOT NULL,
    [DeliveredQty] [decimal](18,4) NULL CONSTRAINT [DF__SalesOrde__Deliv__68336F3E] DEFAULT ((0)),
    [RemainingQty] AS ([OrderedQty]-isnull([DeliveredQty],(0))),
    [UnitPrice] [decimal](18,4) NOT NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesOrde__Disco__69279377] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__Disco__6A1BB7B0] DEFAULT ((0)),
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__LineT__6B0FDBE9] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__SalesOrde__TaxRa__6C040022] DEFAULT ((14.00)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__TaxAm__6CF8245B] DEFAULT ((0)),
    [LineTotalWithTax] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__LineT__6DEC4894] DEFAULT ((0)),
    [LineStatus] [tinyint] NULL CONSTRAINT [DF__SalesOrde__LineS__6EE06CCD] DEFAULT ((1)),
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[SalesOrders] (
    [SalesOrderID] [int] IDENTITY(1,1) NOT NULL,
    [SONumber] [nvarchar](50) NOT NULL,
    [SODate] [date] NOT NULL CONSTRAINT [DF__SalesOrde__SODat__6FD49106] DEFAULT (CONVERT([date],getdate())),
    [CustomerID] [int] NOT NULL,
    [QuotationID] [int] NULL,
    [SalesRepID] [int] NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__SalesOrde__Excha__70C8B53F] DEFAULT ((1)),
    [PriceListID] [int] NULL,
    [PaymentTermID] [int] NULL,
    [WarehouseID] [int] NULL,
    [SubTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__SubTo__71BCD978] DEFAULT ((0)),
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesOrde__Disco__72B0FDB1] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__Disco__73A521EA] DEFAULT ((0)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__TaxAm__74994623] DEFAULT ((0)),
    [ShippingCost] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__Shipp__758D6A5C] DEFAULT ((0)),
    [TotalAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesOrde__Total__76818E95] DEFAULT ((0)),
    [SOStatus] [tinyint] NOT NULL CONSTRAINT [DF__SalesOrde__SOSta__7775B2CE] DEFAULT ((1)),
    [RequiredDate] [date] NULL,
    [DeliveryDate] [date] NULL,
    [DeliveryAddress] [nvarchar](300) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SalesOrde__Creat__7869D707] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[SalesQuotationDetails] (
    [QuotationDetailID] [int] IDENTITY(1,1) NOT NULL,
    [QuotationID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [Quantity] [decimal](18,4) NOT NULL,
    [UnitPrice] [decimal](18,4) NOT NULL,
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesQuot__Disco__795DFB40] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__Disco__7A521F79] DEFAULT ((0)),
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__LineT__7B4643B2] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__SalesQuot__TaxRa__7C3A67EB] DEFAULT ((14.00)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__TaxAm__7D2E8C24] DEFAULT ((0)),
    [LineTotalWithTax] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__LineT__7E22B05D] DEFAULT ((0)),
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__SalesQuot__UnitC__7F16D496] DEFAULT ((0)),
    [ProfitMargin] AS (case when isnull([UnitCost],(0))>(0) then (([UnitPrice]*((1)-isnull([DiscountPercent],(0))/(100))-[UnitCost])/[UnitCost])*(100) else (0) end),
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[SalesQuotations] (
    [QuotationID] [int] IDENTITY(1,1) NOT NULL,
    [QuotationNumber] [nvarchar](50) NOT NULL,
    [QuotationDate] [date] NOT NULL CONSTRAINT [DF__SalesQuot__Quota__000AF8CF] DEFAULT (CONVERT([date],getdate())),
    [ValidUntil] [date] NULL,
    [CustomerID] [int] NOT NULL,
    [SalesRepID] [int] NULL,
    [CurrencyID] [int] NULL,
    [ExchangeRate] [decimal](18,6) NULL CONSTRAINT [DF__SalesQuot__Excha__00FF1D08] DEFAULT ((1)),
    [PriceListID] [int] NULL,
    [PaymentTermID] [int] NULL,
    [SubTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__SubTo__01F34141] DEFAULT ((0)),
    [DiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesQuot__Disco__02E7657A] DEFAULT ((0)),
    [DiscountAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__Disco__03DB89B3] DEFAULT ((0)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__TaxAm__04CFADEC] DEFAULT ((0)),
    [ShippingCost] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__Shipp__05C3D225] DEFAULT ((0)),
    [TotalAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesQuot__Total__06B7F65E] DEFAULT ((0)),
    [QuotationStatus] [tinyint] NOT NULL CONSTRAINT [DF__SalesQuot__Quota__07AC1A97] DEFAULT ((1)),
    [DeliveryDate] [date] NULL,
    [DeliveryAddress] [nvarchar](300) NULL,
    [CustomerNotes] [nvarchar](MAX) NULL,
    [InternalNotes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SalesQuot__Creat__08A03ED0] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[SalesRepresentatives] (
    [SalesRepID] [int] IDENTITY(1,1) NOT NULL,
    [EmployeeID] [int] NOT NULL,
    [RepCode] [nvarchar](20) NOT NULL,
    [CommissionPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesRepr__Commi__09946309] DEFAULT ((0)),
    [MonthlyTarget] [decimal](18,2) NULL CONSTRAINT [DF__SalesRepr__Month__0A888742] DEFAULT ((0)),
    [QuarterlyTarget] [decimal](18,2) NULL CONSTRAINT [DF__SalesRepr__Quart__0B7CAB7B] DEFAULT ((0)),
    [AnnualTarget] [decimal](18,2) NULL CONSTRAINT [DF__SalesRepr__Annua__0C70CFB4] DEFAULT ((0)),
    [Region] [nvarchar](100) NULL,
    [MaxDiscountPercent] [decimal](5,2) NULL CONSTRAINT [DF__SalesRepr__MaxDi__0D64F3ED] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__SalesRepr__IsAct__0E591826] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SalesRepr__Creat__0F4D3C5F] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[SalesReturnDetails] (
    [ReturnDetailID] [int] IDENTITY(1,1) NOT NULL,
    [ReturnID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [Quantity] [decimal](18,4) NOT NULL,
    [UnitPrice] [decimal](18,4) NOT NULL,
    [LineTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesRetu__LineT__10416098] DEFAULT ((0)),
    [TaxRate] [decimal](5,2) NULL CONSTRAINT [DF__SalesRetu__TaxRa__113584D1] DEFAULT ((14.00)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesRetu__TaxAm__1229A90A] DEFAULT ((0)),
    [LineTotalWithTax] [decimal](18,2) NULL CONSTRAINT [DF__SalesRetu__LineT__131DCD43] DEFAULT ((0)),
    [BatchNumber] [nvarchar](50) NULL,
    [ItemCondition] [tinyint] NULL CONSTRAINT [DF__SalesRetu__ItemC__1411F17C] DEFAULT ((1)),
    [ReturnReason] [nvarchar](200) NULL,
    [InvoiceDetailID] [int] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[SalesReturns] (
    [ReturnID] [int] IDENTITY(1,1) NOT NULL,
    [ReturnNumber] [nvarchar](50) NOT NULL,
    [ReturnDate] [date] NOT NULL CONSTRAINT [DF__SalesRetu__Retur__150615B5] DEFAULT (CONVERT([date],getdate())),
    [CustomerID] [int] NOT NULL,
    [InvoiceID] [int] NULL,
    [SalesRepID] [int] NULL,
    [WarehouseID] [int] NOT NULL,
    [ReturnReason] [nvarchar](500) NULL,
    [ReturnType] [tinyint] NULL CONSTRAINT [DF__SalesRetu__Retur__15FA39EE] DEFAULT ((1)),
    [SubTotal] [decimal](18,2) NULL CONSTRAINT [DF__SalesRetu__SubTo__16EE5E27] DEFAULT ((0)),
    [TaxAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesRetu__TaxAm__17E28260] DEFAULT ((0)),
    [TotalAmount] [decimal](18,2) NULL CONSTRAINT [DF__SalesRetu__Total__18D6A699] DEFAULT ((0)),
    [ReturnStatus] [tinyint] NOT NULL CONSTRAINT [DF__SalesRetu__Retur__19CACAD2] DEFAULT ((1)),
    [Notes] [nvarchar](MAX) NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SalesRetu__Creat__1ABEEF0B] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[StockCountDetails] (
    [CountDetailID] [int] IDENTITY(1,1) NOT NULL,
    [CountID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [SystemQty] [decimal](18,4) NULL CONSTRAINT [DF__StockCoun__Syste__1BB31344] DEFAULT ((0)),
    [CountedQty] [decimal](18,4) NULL,
    [Variance] AS (isnull([CountedQty],(0))-isnull([SystemQty],(0))),
    [VarianceValue] AS ((isnull([CountedQty],(0))-isnull([SystemQty],(0)))*isnull([UnitCost],(0))),
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__StockCoun__UnitC__1CA7377D] DEFAULT ((0)),
    [BatchNumber] [nvarchar](50) NULL,
    [AdjustmentApproved] [bit] NULL CONSTRAINT [DF__StockCoun__Adjus__1D9B5BB6] DEFAULT ((0)),
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[StockCounts] (
    [CountID] [int] IDENTITY(1,1) NOT NULL,
    [CountNumber] [nvarchar](50) NOT NULL,
    [CountDate] [date] NOT NULL CONSTRAINT [DF__StockCoun__Count__1E8F7FEF] DEFAULT (CONVERT([date],getdate())),
    [WarehouseID] [int] NOT NULL,
    [CountType] [tinyint] NULL CONSTRAINT [DF__StockCoun__Count__1F83A428] DEFAULT ((1)),
    [CategoryID] [int] NULL,
    [CountStatus] [tinyint] NOT NULL CONSTRAINT [DF__StockCoun__Count__2077C861] DEFAULT ((1)),
    [CountedBy] [int] NULL,
    [SupervisorID] [int] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__StockCoun__Creat__216BEC9A] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[StockTransferDetails] (
    [TransferDetailID] [int] IDENTITY(1,1) NOT NULL,
    [TransferID] [int] NOT NULL,
    [LineNumber] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [UnitID] [int] NOT NULL,
    [Quantity] [decimal](18,4) NOT NULL,
    [UnitCost] [decimal](18,4) NULL CONSTRAINT [DF__StockTran__UnitC__226010D3] DEFAULT ((0)),
    [BatchNumber] [nvarchar](50) NULL,
    [ExpiryDate] [date] NULL,
    [Notes] [nvarchar](200) NULL);
GO

CREATE TABLE [dbo].[StockTransfers] (
    [TransferID] [int] IDENTITY(1,1) NOT NULL,
    [TransferNumber] [nvarchar](50) NOT NULL,
    [TransferDate] [date] NOT NULL CONSTRAINT [DF__StockTran__Trans__2354350C] DEFAULT (CONVERT([date],getdate())),
    [FromWarehouseID] [int] NOT NULL,
    [ToWarehouseID] [int] NOT NULL,
    [TransferReason] [nvarchar](200) NULL,
    [TransferStatus] [tinyint] NOT NULL CONSTRAINT [DF__StockTran__Trans__24485945] DEFAULT ((1)),
    [RequestedBy] [int] NULL,
    [ApprovedBy] [int] NULL,
    [ApprovedDate] [datetime2](7) NULL,
    [Notes] [nvarchar](MAX) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__StockTran__Creat__253C7D7E] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[SupplierContacts] (
    [ContactID] [int] IDENTITY(1,1) NOT NULL,
    [SupplierID] [int] NOT NULL,
    [ContactName] [nvarchar](100) NOT NULL,
    [JobTitle] [nvarchar](100) NULL,
    [Phone] [nvarchar](20) NULL,
    [Mobile] [nvarchar](20) NULL,
    [Email] [nvarchar](100) NULL,
    [IsPrimary] [bit] NULL CONSTRAINT [DF__SupplierC__IsPri__2630A1B7] DEFAULT ((0)),
    [Notes] [nvarchar](200) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__SupplierC__IsAct__2724C5F0] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SupplierC__Creat__2818EA29] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[SupplierItems] (
    [SupplierItemID] [int] IDENTITY(1,1) NOT NULL,
    [SupplierID] [int] NOT NULL,
    [ItemID] [int] NOT NULL,
    [SupplierItemCode] [nvarchar](50) NULL,
    [UnitPrice] [decimal](18,4) NULL CONSTRAINT [DF__SupplierI__UnitP__290D0E62] DEFAULT ((0)),
    [CurrencyID] [int] NULL,
    [MinOrderQty] [decimal](18,4) NULL CONSTRAINT [DF__SupplierI__MinOr__2A01329B] DEFAULT ((0)),
    [LeadTimeDays] [int] NULL CONSTRAINT [DF__SupplierI__LeadT__2AF556D4] DEFAULT ((0)),
    [LastPurchaseDate] [date] NULL,
    [LastPurchasePrice] [decimal](18,4) NULL,
    [IsPrimary] [bit] NULL CONSTRAINT [DF__SupplierI__IsPri__2BE97B0D] DEFAULT ((0)),
    [Notes] [nvarchar](200) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__SupplierI__IsAct__2CDD9F46] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SupplierI__Creat__2DD1C37F] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[Suppliers] (
    [SupplierID] [int] IDENTITY(1,1) NOT NULL,
    [SupplierCode] [nvarchar](20) NOT NULL,
    [SupplierNameAr] [nvarchar](200) NOT NULL,
    [SupplierNameEn] [nvarchar](200) NULL,
    [SupplierType] [tinyint] NOT NULL CONSTRAINT [DF__Suppliers__Suppl__2EC5E7B8] DEFAULT ((1)),
    [TaxNumber] [nvarchar](50) NULL,
    [CommercialRegister] [nvarchar](50) NULL,
    [Address] [nvarchar](300) NULL,
    [City] [nvarchar](100) NULL,
    [Country] [nvarchar](100) NULL CONSTRAINT [DF__Suppliers__Count__2FBA0BF1] DEFAULT (N'مصر'),
    [Phone1] [nvarchar](20) NULL,
    [Phone2] [nvarchar](20) NULL,
    [Mobile] [nvarchar](20) NULL,
    [Fax] [nvarchar](20) NULL,
    [Email] [nvarchar](100) NULL,
    [Website] [nvarchar](200) NULL,
    [PaymentTermID] [int] NULL,
    [CurrencyID] [int] NULL,
    [CreditLimit] [decimal](18,2) NULL CONSTRAINT [DF__Suppliers__Credi__30AE302A] DEFAULT ((0)),
    [OpeningBalance] [decimal](18,2) NULL CONSTRAINT [DF__Suppliers__Openi__31A25463] DEFAULT ((0)),
    [CurrentBalance] [decimal](18,2) NULL CONSTRAINT [DF__Suppliers__Curre__3296789C] DEFAULT ((0)),
    [BankName] [nvarchar](100) NULL,
    [BankAccountNumber] [nvarchar](50) NULL,
    [IBAN] [nvarchar](50) NULL,
    [Rating] [tinyint] NULL CONSTRAINT [DF__Suppliers__Ratin__338A9CD5] DEFAULT ((3)),
    [Notes] [nvarchar](MAX) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__Suppliers__IsAct__347EC10E] DEFAULT ((1)),
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Suppliers__Creat__3572E547] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[SystemModules] (
    [ModuleID] [int] IDENTITY(1,1) NOT NULL,
    [ModuleCode] [nvarchar](50) NOT NULL,
    [ModuleNameAr] [nvarchar](100) NOT NULL,
    [ModuleNameEn] [nvarchar](100) NULL,
    [ParentModuleID] [int] NULL,
    [ModuleType] [tinyint] NULL CONSTRAINT [DF__SystemMod__Modul__36670980] DEFAULT ((1)),
    [FormName] [nvarchar](100) NULL,
    [ReportName] [nvarchar](100) NULL,
    [IconName] [nvarchar](50) NULL,
    [SortOrder] [int] NULL CONSTRAINT [DF__SystemMod__SortO__375B2DB9] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__SystemMod__IsAct__384F51F2] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SystemMod__Creat__3943762B] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[SystemSettings] (
    [SettingID] [int] IDENTITY(1,1) NOT NULL,
    [SettingKey] [nvarchar](100) NOT NULL,
    [SettingValue] [nvarchar](MAX) NULL,
    [SettingDescription] [nvarchar](200) NULL,
    [SettingGroup] [nvarchar](50) NULL,
    [DataType] [nvarchar](20) NULL CONSTRAINT [DF__SystemSet__DataT__3A379A64] DEFAULT ('String'),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SystemSet__Creat__3B2BBE9D] DEFAULT (getdate()),
    [ModifiedDate] [datetime2](7) NULL);
GO

CREATE TABLE [dbo].[SystemUsers] (
    [UserID] [int] IDENTITY(1,1) NOT NULL,
    [Username] [nvarchar](50) NOT NULL,
    [PasswordHash] [nvarchar](256) NOT NULL,
    [PasswordSalt] [nvarchar](100) NULL,
    [EmployeeID] [int] NULL,
    [FullName] [nvarchar](100) NOT NULL,
    [Email] [nvarchar](100) NULL,
    [Phone] [nvarchar](20) NULL,
    [RoleID] [int] NOT NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__SystemUse__IsAct__3C1FE2D6] DEFAULT ((1)),
    [IsLocked] [bit] NULL CONSTRAINT [DF__SystemUse__IsLoc__3D14070F] DEFAULT ((0)),
    [LockoutEnd] [datetime2](7) NULL,
    [FailedLoginAttempts] [int] NULL CONSTRAINT [DF__SystemUse__Faile__3E082B48] DEFAULT ((0)),
    [MaxFailedAttempts] [int] NULL CONSTRAINT [DF__SystemUse__MaxFa__3EFC4F81] DEFAULT ((5)),
    [MustChangePassword] [bit] NULL CONSTRAINT [DF__SystemUse__MustC__3FF073BA] DEFAULT ((1)),
    [PasswordChangedDate] [datetime2](7) NULL,
    [PasswordExpiryDays] [int] NULL CONSTRAINT [DF__SystemUse__Passw__40E497F3] DEFAULT ((90)),
    [LastLoginDate] [datetime2](7) NULL,
    [LastLoginIP] [nvarchar](50) NULL,
    [LastLoginMachine] [nvarchar](100) NULL,
    [DefaultLanguage] [nvarchar](5) NULL CONSTRAINT [DF__SystemUse__Defau__41D8BC2C] DEFAULT (N'ar'),
    [DefaultWarehouseID] [int] NULL,
    [DefaultCashBoxID] [int] NULL,
    [ProfileImage] [nvarchar](500) NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedBy] [int] NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__SystemUse__Creat__42CCE065] DEFAULT (getdate()),
    [ModifiedBy] [int] NULL,
    [ModifiedDate] [datetime2](7) NULL,
    [DefaultCurrencyID] [int] NULL);
GO

CREATE TABLE [dbo].[UnitConversions] (
    [ConversionID] [int] IDENTITY(1,1) NOT NULL,
    [FromUnitID] [int] NOT NULL,
    [ToUnitID] [int] NOT NULL,
    [ConversionFactor] [decimal](18,6) NOT NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__UnitConve__IsAct__43C1049E] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__UnitConve__Creat__44B528D7] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[Units] (
    [UnitID] [int] IDENTITY(1,1) NOT NULL,
    [UnitCode] [nvarchar](10) NOT NULL,
    [UnitNameAr] [nvarchar](50) NOT NULL,
    [UnitNameEn] [nvarchar](50) NULL,
    [UnitType] [nvarchar](20) NULL,
    [IsActive] [bit] NULL CONSTRAINT [DF__Units__IsActive__45A94D10] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Units__CreatedDa__469D7149] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[UserPermissions] (
    [UserPermissionID] [int] IDENTITY(1,1) NOT NULL,
    [UserID] [int] NOT NULL,
    [ModuleID] [int] NOT NULL,
    [CanView] [bit] NULL,
    [CanAdd] [bit] NULL,
    [CanEdit] [bit] NULL,
    [CanDelete] [bit] NULL,
    [CanPrint] [bit] NULL,
    [CanExport] [bit] NULL,
    [CanApprove] [bit] NULL,
    [CanPost] [bit] NULL,
    [CanCancel] [bit] NULL,
    [Notes] [nvarchar](200) NULL,
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__UserPermi__Creat__47919582] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[UserPreferences] (
    [PreferenceID] [int] IDENTITY(1,1) NOT NULL,
    [UserID] [int] NOT NULL,
    [PreferenceKey] [nvarchar](100) NOT NULL,
    [PreferenceValue] [nvarchar](MAX) NULL);
GO

CREATE TABLE [dbo].[UserRoles] (
    [RoleID] [int] IDENTITY(1,1) NOT NULL,
    [RoleCode] [nvarchar](20) NOT NULL,
    [RoleNameAr] [nvarchar](100) NOT NULL,
    [RoleNameEn] [nvarchar](100) NULL,
    [Description] [nvarchar](200) NULL,
    [IsSystemRole] [bit] NULL CONSTRAINT [DF__UserRoles__IsSys__4885B9BB] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__UserRoles__IsAct__4979DDF4] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__UserRoles__Creat__4A6E022D] DEFAULT (getdate()));
GO

CREATE TABLE [dbo].[Warehouses] (
    [WarehouseID] [int] IDENTITY(1,1) NOT NULL,
    [WarehouseCode] [nvarchar](20) NOT NULL,
    [WarehouseNameAr] [nvarchar](100) NOT NULL,
    [WarehouseNameEn] [nvarchar](100) NULL,
    [WarehouseType] [tinyint] NOT NULL,
    [Address] [nvarchar](200) NULL,
    [Phone] [nvarchar](20) NULL,
    [IsDefault] [bit] NULL CONSTRAINT [DF__Warehouse__IsDef__4B622666] DEFAULT ((0)),
    [IsActive] [bit] NULL CONSTRAINT [DF__Warehouse__IsAct__4C564A9F] DEFAULT ((1)),
    [CreatedDate] [datetime2](7) NULL CONSTRAINT [DF__Warehouse__Creat__4D4A6ED8] DEFAULT (getdate()),
    [ManagerID] [int] NULL
);
GO

ALTER TABLE [dbo].[AccountingPeriods] ADD CONSTRAINT [PK__Accounti__E521BB36DC18380E] PRIMARY KEY (PeriodID);
ALTER TABLE [dbo].[AttendanceRecords] ADD CONSTRAINT [PK__Attendan__8B69263C24F0E11D] PRIMARY KEY (AttendanceID);
ALTER TABLE [dbo].[AuditLog] ADD CONSTRAINT [PK__AuditLog__A17F23B8FE2A36CE] PRIMARY KEY (AuditID);
ALTER TABLE [dbo].[BankAccounts] ADD CONSTRAINT [PK__BankAcco__4FC8E7413AE59D0D] PRIMARY KEY (BankAccountID);
ALTER TABLE [dbo].[BillOfMaterials] ADD CONSTRAINT [PK__BillOfMa__CA12FCBB6A32892E] PRIMARY KEY (BOMID);
ALTER TABLE [dbo].[BOMDetails] ADD CONSTRAINT [PK__BOMDetai__796CB380BFB676B7] PRIMARY KEY (BOMDetailID);
ALTER TABLE [dbo].[BOMStages] ADD CONSTRAINT [PK__BOMStage__CE067B215756470E] PRIMARY KEY (BOMStageID);
ALTER TABLE [dbo].[CashBoxes] ADD CONSTRAINT [PK__CashBoxe__3E790512390E2CC5] PRIMARY KEY (CashBoxID);
ALTER TABLE [dbo].[CashTransfers] ADD CONSTRAINT [PK__CashTran__95490171EDF62024] PRIMARY KEY (TransferID);
ALTER TABLE [dbo].[ChartOfAccounts] ADD CONSTRAINT [PK__ChartOfA__349DA5865789E9DC] PRIMARY KEY (AccountID);
ALTER TABLE [dbo].[Cheques] ADD CONSTRAINT [PK__Cheques__B816D9D05D77885C] PRIMARY KEY (ChequeID);
ALTER TABLE [dbo].[ContractMaterialSources] ADD CONSTRAINT [PK__Contract__16E019F9189B4E1C] PRIMARY KEY (SourceID);
ALTER TABLE [dbo].[ContractOrders] ADD CONSTRAINT [PK__Contract__392293D3A50C08CB] PRIMARY KEY (ContractOrderID);
ALTER TABLE [dbo].[ContractDeliveries] ADD CONSTRAINT [PK__Contract__626D8FEEC8CB4DF9] PRIMARY KEY (DeliveryID);
ALTER TABLE [dbo].[ContractOrderDetails] ADD CONSTRAINT [PK__Contract__CCA7AF025C80A421] PRIMARY KEY (ContractDetailID);
ALTER TABLE [dbo].[ContractDeliveryDetails] ADD CONSTRAINT [PK__Contract__EFD2C287EBB69AB9] PRIMARY KEY (DeliveryDetailID);
ALTER TABLE [dbo].[CostCenters] ADD CONSTRAINT [PK__CostCent__89D876D1B0729D43] PRIMARY KEY (CostCenterID);
ALTER TABLE [dbo].[Currencies] ADD CONSTRAINT [PK__Currenci__14470B10D0BB2F3C] PRIMARY KEY (CurrencyID);
ALTER TABLE [dbo].[CustomerGroups] ADD CONSTRAINT [PK__Customer__149AF30A1EDEEA65] PRIMARY KEY (GroupID);
ALTER TABLE [dbo].[CustomerContacts] ADD CONSTRAINT [PK__Customer__5C6625BB1CC6A2C3] PRIMARY KEY (ContactID);
ALTER TABLE [dbo].[CustomerMaterialReceiptDetails] ADD CONSTRAINT [PK__Customer__82FADEDB246D1600] PRIMARY KEY (ReceiptDetailID);
ALTER TABLE [dbo].[CustomerMaterialReturnDetails] ADD CONSTRAINT [PK__Customer__8B89C9EA2041BF28] PRIMARY KEY (ReturnDetailID);
ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [PK__Customer__A4AE64B8B9268A58] PRIMARY KEY (CustomerID);
ALTER TABLE [dbo].[CustomerMaterialReceipts] ADD CONSTRAINT [PK__Customer__CC08C40090920599] PRIMARY KEY (ReceiptID);
ALTER TABLE [dbo].[CustomerMaterialReturns] ADD CONSTRAINT [PK__Customer__F445E98837412FBE] PRIMARY KEY (ReturnID);
ALTER TABLE [dbo].[Departments] ADD CONSTRAINT [PK__Departme__B2079BCD6CF4871C] PRIMARY KEY (DepartmentID);
ALTER TABLE [dbo].[EmployeeLoans] ADD CONSTRAINT [PK__Employee__4F5AD4371FE28353] PRIMARY KEY (LoanID);
ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [PK__Employee__7AD04FF1D4A27D27] PRIMARY KEY (EmployeeID);
ALTER TABLE [dbo].[EmployeeLeaveBalances] ADD CONSTRAINT [PK__Employee__A760D59E6B4E464F] PRIMARY KEY (BalanceID);
ALTER TABLE [dbo].[EmployeePenaltiesRewards] ADD CONSTRAINT [PK__Employee__FBDF78C9A2428C9F] PRIMARY KEY (RecordID);
ALTER TABLE [dbo].[FiscalYears] ADD CONSTRAINT [PK__FiscalYe__A4E4808F3BB38B30] PRIMARY KEY (FiscalYearID);
ALTER TABLE [dbo].[GoodsReceiptDetails] ADD CONSTRAINT [PK__GoodsRec__B7FEA6A131F4BDF7] PRIMARY KEY (GRNDetailID);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [PK__GoodsRec__BC0E8C62EFABFD42] PRIMARY KEY (GRNID);
ALTER TABLE [dbo].[InventoryTransactions] ADD CONSTRAINT [PK__Inventor__55433A4BA09AB5F2] PRIMARY KEY (TransactionID);
ALTER TABLE [dbo].[InventoryBalance] ADD CONSTRAINT [PK__Inventor__A760D59E26FD931F] PRIMARY KEY (BalanceID);
ALTER TABLE [dbo].[InventoryBatchBalance] ADD CONSTRAINT [PK__Inventor__B451432667F66E98] PRIMARY KEY (BatchBalanceID);
ALTER TABLE [dbo].[ItemCategories] ADD CONSTRAINT [PK__ItemCate__19093A2BC96CD290] PRIMARY KEY (CategoryID);
ALTER TABLE [dbo].[Items] ADD CONSTRAINT [PK__Items__727E83EB0891D47C] PRIMARY KEY (ItemID);
ALTER TABLE [dbo].[JobTitles] ADD CONSTRAINT [PK__JobTitle__35382FC9530FDD84] PRIMARY KEY (JobTitleID);
ALTER TABLE [dbo].[JournalEntries] ADD CONSTRAINT [PK__JournalE__25010386E3C8FDC9] PRIMARY KEY (JournalID);
ALTER TABLE [dbo].[JournalEntryDetails] ADD CONSTRAINT [PK__JournalE__2D3EFA6D146A6200] PRIMARY KEY (JournalDetailID);
ALTER TABLE [dbo].[LeaveRequests] ADD CONSTRAINT [PK__LeaveReq__6094218E1D4FD160] PRIMARY KEY (LeaveRequestID);
ALTER TABLE [dbo].[LeaveTypes] ADD CONSTRAINT [PK__LeaveTyp__43BE8FF4EA1CB515] PRIMARY KEY (LeaveTypeID);
ALTER TABLE [dbo].[LoanInstallments] ADD CONSTRAINT [PK__LoanInst__42B42E6209F34075] PRIMARY KEY (InstallmentID);
ALTER TABLE [dbo].[LoginHistory] ADD CONSTRAINT [PK__LoginHis__4DDA2838164B0045] PRIMARY KEY (LoginID);
ALTER TABLE [dbo].[MaterialIssueDetails] ADD CONSTRAINT [PK__Material__68ADB57EAB784390] PRIMARY KEY (IssueDetailID);
ALTER TABLE [dbo].[MaterialIssueNotes] ADD CONSTRAINT [PK__Material__6C86162491D70571] PRIMARY KEY (IssueID);
ALTER TABLE [dbo].[Notifications] ADD CONSTRAINT [PK__Notifica__20CF2E3253092025] PRIMARY KEY (NotificationID);
ALTER TABLE [dbo].[NumberSequences] ADD CONSTRAINT [PK__NumberSe__BAD614F10022F154] PRIMARY KEY (SequenceID);
ALTER TABLE [dbo].[OverheadExpenses] ADD CONSTRAINT [PK__Overhead__1445CFF337284C5C] PRIMARY KEY (ExpenseID);
ALTER TABLE [dbo].[PaymentAllocations] ADD CONSTRAINT [PK__PaymentA__B3C6D6AB87AF3AE1] PRIMARY KEY (AllocationID);
ALTER TABLE [dbo].[PaymentTerms] ADD CONSTRAINT [PK__PaymentT__80A6A8C70945388D] PRIMARY KEY (PaymentTermID);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [PK__PaymentV__9B556A58462AC751] PRIMARY KEY (PaymentID);
ALTER TABLE [dbo].[Payroll] ADD CONSTRAINT [PK__Payroll__99DFC692E79266BD] PRIMARY KEY (PayrollID);
ALTER TABLE [dbo].[PayrollDetails] ADD CONSTRAINT [PK__PayrollD__010127A96B185104] PRIMARY KEY (PayrollDetailID);
ALTER TABLE [dbo].[PriceLists] ADD CONSTRAINT [PK__PriceLis__1E30F34C895A8893] PRIMARY KEY (PriceListID);
ALTER TABLE [dbo].[PriceListDetails] ADD CONSTRAINT [PK__PriceLis__F6F0A0EF6C276BFF] PRIMARY KEY (PriceDetailID);
ALTER TABLE [dbo].[ProductCostCards] ADD CONSTRAINT [PK__ProductC__591514EA92B20CED] PRIMARY KEY (CostCardID);
ALTER TABLE [dbo].[ProductionStages] ADD CONSTRAINT [PK__Producti__03EB7AF83DBF68A3] PRIMARY KEY (StageID);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [PK__Producti__5D55CE3897B1BCBE] PRIMARY KEY (BatchID);
ALTER TABLE [dbo].[ProductionOrderMaterials] ADD CONSTRAINT [PK__Producti__869F20F52EEEC0D2] PRIMARY KEY (POMaterialID);
ALTER TABLE [dbo].[ProductionBatchStages] ADD CONSTRAINT [PK__Producti__8F788F162FFEA50A] PRIMARY KEY (BatchStageID);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [PK__Producti__E86155F0B7E324D4] PRIMARY KEY (ProductionOrderID);
ALTER TABLE [dbo].[PublicHolidays] ADD CONSTRAINT [PK__PublicHo__2D35D59A3D4DAB39] PRIMARY KEY (HolidayID);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [PK__Purchase__036BAC443B2CDD78] PRIMARY KEY (PurchaseOrderID);
ALTER TABLE [dbo].[PurchaseInvoiceDetails] ADD CONSTRAINT [PK__Purchase__1F1578F13A1B6DB7] PRIMARY KEY (InvoiceDetailID);
ALTER TABLE [dbo].[PurchaseRequests] ADD CONSTRAINT [PK__Purchase__33A8519ABF2C07F6] PRIMARY KEY (RequestID);
ALTER TABLE [dbo].[PurchaseOrderDetails] ADD CONSTRAINT [PK__Purchase__4EB47B5E7329F716] PRIMARY KEY (PODetailID);
ALTER TABLE [dbo].[PurchaseReturnDetails] ADD CONSTRAINT [PK__Purchase__8B89C9EAB51BAD41] PRIMARY KEY (ReturnDetailID);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [PK__Purchase__D796AAD569B9278A] PRIMARY KEY (InvoiceID);
ALTER TABLE [dbo].[PurchaseRequestDetails] ADD CONSTRAINT [PK__Purchase__DC528B70C413A68A] PRIMARY KEY (RequestDetailID);
ALTER TABLE [dbo].[PurchaseReturns] ADD CONSTRAINT [PK__Purchase__F445E9889F4D4290] PRIMARY KEY (ReturnID);
ALTER TABLE [dbo].[ReceiptAllocations] ADD CONSTRAINT [PK__ReceiptA__B3C6D6AB19FE7B62] PRIMARY KEY (AllocationID);
ALTER TABLE [dbo].[ReceiptVouchers] ADD CONSTRAINT [PK__ReceiptV__CC08C400969B6F37] PRIMARY KEY (ReceiptID);
ALTER TABLE [dbo].[RolePermissions] ADD CONSTRAINT [PK__RolePerm__EFA6FB0FCCB87E30] PRIMARY KEY (PermissionID);
ALTER TABLE [dbo].[SalesInvoiceDetails] ADD CONSTRAINT [PK__SalesInv__1F1578F163EDA1B4] PRIMARY KEY (InvoiceDetailID);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [PK__SalesInv__D796AAD53CFFA455] PRIMARY KEY (InvoiceID);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [PK__SalesOrd__B14003C2301CBC40] PRIMARY KEY (SalesOrderID);
ALTER TABLE [dbo].[SalesOrderDetails] ADD CONSTRAINT [PK__SalesOrd__E3042991935CA663] PRIMARY KEY (SODetailID);
ALTER TABLE [dbo].[SalesQuotationDetails] ADD CONSTRAINT [PK__SalesQuo__0CEE6A82B81F41D4] PRIMARY KEY (QuotationDetailID);
ALTER TABLE [dbo].[SalesQuotations] ADD CONSTRAINT [PK__SalesQuo__E19752B3F4C10AFD] PRIMARY KEY (QuotationID);
ALTER TABLE [dbo].[SalesRepresentatives] ADD CONSTRAINT [PK__SalesRep__736D06D1C95C036E] PRIMARY KEY (SalesRepID);
ALTER TABLE [dbo].[SalesReturnDetails] ADD CONSTRAINT [PK__SalesRet__8B89C9EAA3A359B3] PRIMARY KEY (ReturnDetailID);
ALTER TABLE [dbo].[SalesReturns] ADD CONSTRAINT [PK__SalesRet__F445E988BF8A011E] PRIMARY KEY (ReturnID);
ALTER TABLE [dbo].[StockCounts] ADD CONSTRAINT [PK__StockCou__06678C9C31A2F750] PRIMARY KEY (CountID);
ALTER TABLE [dbo].[StockCountDetails] ADD CONSTRAINT [PK__StockCou__8C9890BB46B7E94B] PRIMARY KEY (CountDetailID);
ALTER TABLE [dbo].[StockTransfers] ADD CONSTRAINT [PK__StockTra__95490171371B337B] PRIMARY KEY (TransferID);
ALTER TABLE [dbo].[StockTransferDetails] ADD CONSTRAINT [PK__StockTra__F9BF690F4DFA8B3B] PRIMARY KEY (TransferDetailID);
ALTER TABLE [dbo].[SupplierItems] ADD CONSTRAINT [PK__Supplier__3189E5FC0B5258E9] PRIMARY KEY (SupplierItemID);
ALTER TABLE [dbo].[Suppliers] ADD CONSTRAINT [PK__Supplier__4BE666949802C053] PRIMARY KEY (SupplierID);
ALTER TABLE [dbo].[SupplierContacts] ADD CONSTRAINT [PK__Supplier__5C6625BB8933D559] PRIMARY KEY (ContactID);
ALTER TABLE [dbo].[SystemModules] ADD CONSTRAINT [PK__SystemMo__2B7477873450F094] PRIMARY KEY (ModuleID);
ALTER TABLE [dbo].[SystemSettings] ADD CONSTRAINT [PK__SystemSe__54372AFD1F2C173B] PRIMARY KEY (SettingID);
ALTER TABLE [dbo].[SystemUsers] ADD CONSTRAINT [PK__SystemUs__1788CCAC46A7A064] PRIMARY KEY (UserID);
ALTER TABLE [dbo].[UnitConversions] ADD CONSTRAINT [PK__UnitConv__A7A07F938C55CAAB] PRIMARY KEY (ConversionID);
ALTER TABLE [dbo].[Units] ADD CONSTRAINT [PK__Units__44F5EC9538525DBB] PRIMARY KEY (UnitID);
ALTER TABLE [dbo].[UserPermissions] ADD CONSTRAINT [PK__UserPerm__A90F88D2D8D9856B] PRIMARY KEY (UserPermissionID);
ALTER TABLE [dbo].[UserPreferences] ADD CONSTRAINT [PK__UserPref__E228490F96D18D57] PRIMARY KEY (PreferenceID);
ALTER TABLE [dbo].[UserRoles] ADD CONSTRAINT [PK__UserRole__8AFACE3A6A532157] PRIMARY KEY (RoleID);
ALTER TABLE [dbo].[Warehouses] ADD CONSTRAINT [PK__Warehous__2608AFD9CA20B6C6] PRIMARY KEY (WarehouseID);
GO

ALTER TABLE [dbo].[AccountingPeriods] ADD CONSTRAINT [FK__Accountin__Close__4E3E9311] FOREIGN KEY ([ClosedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[AccountingPeriods] ADD CONSTRAINT [FK__Accountin__Fisca__4F32B74A] FOREIGN KEY ([FiscalYearID]) REFERENCES [dbo].[FiscalYears] ([FiscalYearID]);
ALTER TABLE [dbo].[AttendanceRecords] ADD CONSTRAINT [FK__Attendanc__Emplo__5026DB83] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[AttendanceRecords] ADD CONSTRAINT [FK__Attendanc__Leave__511AFFBC] FOREIGN KEY ([LeaveRequestID]) REFERENCES [dbo].[LeaveRequests] ([LeaveRequestID]);
ALTER TABLE [dbo].[AuditLog] ADD CONSTRAINT [FK__AuditLog__UserID__520F23F5] FOREIGN KEY ([UserID]) REFERENCES [dbo].[SystemUsers] ([UserID]);
ALTER TABLE [dbo].[BankAccounts] ADD CONSTRAINT [FK__BankAccou__Accou__5303482E] FOREIGN KEY ([AccountID]) REFERENCES [dbo].[ChartOfAccounts] ([AccountID]);
ALTER TABLE [dbo].[BankAccounts] ADD CONSTRAINT [FK__BankAccou__Curre__53F76C67] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[BillOfMaterials] ADD CONSTRAINT [FK__BillOfMat__Appro__54EB90A0] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[BillOfMaterials] ADD CONSTRAINT [FK__BillOfMat__Batch__55DFB4D9] FOREIGN KEY ([BatchUnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[BillOfMaterials] ADD CONSTRAINT [FK__BillOfMat__Outpu__56D3D912] FOREIGN KEY ([OutputUnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[BillOfMaterials] ADD CONSTRAINT [FK__BillOfMat__Produ__57C7FD4B] FOREIGN KEY ([ProductItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[BOMDetails] ADD CONSTRAINT [FK__BOMDetail__BOMID__58BC2184] FOREIGN KEY ([BOMID]) REFERENCES [dbo].[BillOfMaterials] ([BOMID]);
ALTER TABLE [dbo].[BOMDetails] ADD CONSTRAINT [FK__BOMDetail__ItemI__59B045BD] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[BOMDetails] ADD CONSTRAINT [FK__BOMDetail__Subst__5AA469F6] FOREIGN KEY ([SubstituteItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[BOMDetails] ADD CONSTRAINT [FK__BOMDetail__UnitI__5B988E2F] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[BOMStages] ADD CONSTRAINT [FK__BOMStages__BOMID__5C8CB268] FOREIGN KEY ([BOMID]) REFERENCES [dbo].[BillOfMaterials] ([BOMID]);
ALTER TABLE [dbo].[BOMStages] ADD CONSTRAINT [FK__BOMStages__Stage__5D80D6A1] FOREIGN KEY ([StageID]) REFERENCES [dbo].[ProductionStages] ([StageID]);
ALTER TABLE [dbo].[CashBoxes] ADD CONSTRAINT [FK__CashBoxes__Accou__5E74FADA] FOREIGN KEY ([AccountID]) REFERENCES [dbo].[ChartOfAccounts] ([AccountID]);
ALTER TABLE [dbo].[CashBoxes] ADD CONSTRAINT [FK__CashBoxes__Curre__5F691F13] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[CashBoxes] ADD CONSTRAINT [FK__CashBoxes__Custo__605D434C] FOREIGN KEY ([CustodianEmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[CashTransfers] ADD CONSTRAINT [FK__CashTrans__FromC__12149A71] FOREIGN KEY ([FromCashBoxID]) REFERENCES [dbo].[CashBoxes] ([CashBoxID]);
ALTER TABLE [dbo].[CashTransfers] ADD CONSTRAINT [FK__CashTrans__ToCas__1308BEAA] FOREIGN KEY ([ToCashBoxID]) REFERENCES [dbo].[CashBoxes] ([CashBoxID]);
ALTER TABLE [dbo].[ChartOfAccounts] ADD CONSTRAINT [FK__ChartOfAc__Curre__61516785] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[ChartOfAccounts] ADD CONSTRAINT [FK__ChartOfAc__Paren__62458BBE] FOREIGN KEY ([ParentAccountID]) REFERENCES [dbo].[ChartOfAccounts] ([AccountID]);
ALTER TABLE [dbo].[Cheques] ADD CONSTRAINT [FK__Cheques__BankAcc__6339AFF7] FOREIGN KEY ([BankAccountID]) REFERENCES [dbo].[BankAccounts] ([BankAccountID]);
ALTER TABLE [dbo].[Cheques] ADD CONSTRAINT [FK__Cheques__Currenc__642DD430] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[Cheques] ADD CONSTRAINT [FK__Cheques__Journal__6521F869] FOREIGN KEY ([JournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[Cheques] ADD CONSTRAINT [FK__Cheques__Payment__66161CA2] FOREIGN KEY ([PaymentID]) REFERENCES [dbo].[PaymentVouchers] ([PaymentID]);
ALTER TABLE [dbo].[Cheques] ADD CONSTRAINT [FK__Cheques__Receipt__670A40DB] FOREIGN KEY ([ReceiptID]) REFERENCES [dbo].[ReceiptVouchers] ([ReceiptID]);
ALTER TABLE [dbo].[ContractDeliveries] ADD CONSTRAINT [FK__ContractD__Contr__08C03A61] FOREIGN KEY ([ContractOrderID]) REFERENCES [dbo].[ContractOrders] ([ContractOrderID]);
ALTER TABLE [dbo].[ContractDeliveryDetails] ADD CONSTRAINT [FK__ContractD__Contr__1249A49B] FOREIGN KEY ([ContractDetailID]) REFERENCES [dbo].[ContractOrderDetails] ([ContractDetailID]);
ALTER TABLE [dbo].[ContractDeliveries] ADD CONSTRAINT [FK__ContractD__Custo__09B45E9A] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[ContractDeliveries] ADD CONSTRAINT [FK__ContractD__Deliv__0C90CB45] FOREIGN KEY ([DeliveredBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ContractDeliveryDetails] ADD CONSTRAINT [FK__ContractD__Deliv__11558062] FOREIGN KEY ([DeliveryID]) REFERENCES [dbo].[ContractDeliveries] ([DeliveryID]);
ALTER TABLE [dbo].[ContractDeliveryDetails] ADD CONSTRAINT [FK__ContractD__Produ__133DC8D4] FOREIGN KEY ([ProductItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[ContractDeliveryDetails] ADD CONSTRAINT [FK__ContractD__UnitI__1431ED0D] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[ContractDeliveries] ADD CONSTRAINT [FK__ContractD__Wareh__0AA882D3] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ContractMaterialSources] ADD CONSTRAINT [FK__ContractM__Contr__377B294A] FOREIGN KEY ([ContractOrderID]) REFERENCES [dbo].[ContractOrders] ([ContractOrderID]);
ALTER TABLE [dbo].[ContractMaterialSources] ADD CONSTRAINT [FK__ContractM__Contr__386F4D83] FOREIGN KEY ([ContractDetailID]) REFERENCES [dbo].[ContractOrderDetails] ([ContractDetailID]);
ALTER TABLE [dbo].[ContractMaterialSources] ADD CONSTRAINT [FK__ContractM__ItemI__396371BC] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[ContractMaterialSources] ADD CONSTRAINT [FK__ContractM__UnitI__3A5795F5] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[ContractOrders] ADD CONSTRAINT [FK__ContractO__Appro__6482D9EB] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ContractOrderDetails] ADD CONSTRAINT [FK__ContractO__BOMID__6B2FD77A] FOREIGN KEY ([BOMID]) REFERENCES [dbo].[BillOfMaterials] ([BOMID]);
ALTER TABLE [dbo].[ContractOrderDetails] ADD CONSTRAINT [FK__ContractO__Contr__69478F08] FOREIGN KEY ([ContractOrderID]) REFERENCES [dbo].[ContractOrders] ([ContractOrderID]);
ALTER TABLE [dbo].[ContractOrders] ADD CONSTRAINT [FK__ContractO__Custo__5ECA0095] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[ContractOrderDetails] ADD CONSTRAINT [FK__ContractO__Produ__6A3BB341] FOREIGN KEY ([ProductItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[ContractOrderDetails] ADD CONSTRAINT [FK__ContractO__Produ__73C51D7B] FOREIGN KEY ([ProductionOrderID]) REFERENCES [dbo].[ProductionOrders] ([ProductionOrderID]);
ALTER TABLE [dbo].[ContractOrders] ADD CONSTRAINT [FK__ContractO__Sourc__3F1C4B12] FOREIGN KEY ([SourceWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ContractOrders] ADD CONSTRAINT [FK__ContractO__Targe__40106F4B] FOREIGN KEY ([TargetWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ContractOrderDetails] ADD CONSTRAINT [FK__ContractO__UnitI__6C23FBB3] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[CostCenters] ADD CONSTRAINT [FK__CostCente__Depar__67FE6514] FOREIGN KEY ([DepartmentID]) REFERENCES [dbo].[Departments] ([DepartmentID]);
ALTER TABLE [dbo].[CostCenters] ADD CONSTRAINT [FK__CostCente__Paren__68F2894D] FOREIGN KEY ([ParentCostCenterID]) REFERENCES [dbo].[CostCenters] ([CostCenterID]);
ALTER TABLE [dbo].[CustomerContacts] ADD CONSTRAINT [FK__CustomerC__Custo__69E6AD86] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[CustomerGroups] ADD CONSTRAINT [FK__CustomerG__Price__6ADAD1BF] FOREIGN KEY ([PriceListID]) REFERENCES [dbo].[PriceLists] ([PriceListID]);
ALTER TABLE [dbo].[CustomerMaterialReturns] ADD CONSTRAINT [FK__CustomerM__Contr__18F6A22A] FOREIGN KEY ([ContractOrderID]) REFERENCES [dbo].[ContractOrders] ([ContractOrderID]);
ALTER TABLE [dbo].[CustomerMaterialReceipts] ADD CONSTRAINT [FK__CustomerM__Contr__797DF6D1] FOREIGN KEY ([ContractOrderID]) REFERENCES [dbo].[ContractOrders] ([ContractOrderID]);
ALTER TABLE [dbo].[CustomerMaterialReturns] ADD CONSTRAINT [FK__CustomerM__Custo__19EAC663] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[CustomerMaterialReceipts] ADD CONSTRAINT [FK__CustomerM__Custo__7A721B0A] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[CustomerMaterialReceiptDetails] ADD CONSTRAINT [FK__CustomerM__ItemI__0307610B] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[CustomerMaterialReturnDetails] ADD CONSTRAINT [FK__CustomerM__ItemI__22800C64] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[CustomerMaterialReceiptDetails] ADD CONSTRAINT [FK__CustomerM__Recei__02133CD2] FOREIGN KEY ([ReceiptID]) REFERENCES [dbo].[CustomerMaterialReceipts] ([ReceiptID]);
ALTER TABLE [dbo].[CustomerMaterialReceipts] ADD CONSTRAINT [FK__CustomerM__Recei__7D4E87B5] FOREIGN KEY ([ReceivedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[CustomerMaterialReturns] ADD CONSTRAINT [FK__CustomerM__Retur__1CC7330E] FOREIGN KEY ([ReturnedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[CustomerMaterialReturnDetails] ADD CONSTRAINT [FK__CustomerM__Retur__218BE82B] FOREIGN KEY ([ReturnID]) REFERENCES [dbo].[CustomerMaterialReturns] ([ReturnID]);
ALTER TABLE [dbo].[CustomerMaterialReceiptDetails] ADD CONSTRAINT [FK__CustomerM__UnitI__03FB8544] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[CustomerMaterialReturnDetails] ADD CONSTRAINT [FK__CustomerM__UnitI__2374309D] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[CustomerMaterialReturns] ADD CONSTRAINT [FK__CustomerM__Wareh__1ADEEA9C] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[CustomerMaterialReceipts] ADD CONSTRAINT [FK__CustomerM__Wareh__7B663F43] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [FK__Customers__Curre__6BCEF5F8] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [FK__Customers__Custo__6CC31A31] FOREIGN KEY ([CustomerGroupID]) REFERENCES [dbo].[CustomerGroups] ([GroupID]);
ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [FK__Customers__Payme__6DB73E6A] FOREIGN KEY ([PaymentTermID]) REFERENCES [dbo].[PaymentTerms] ([PaymentTermID]);
ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [FK__Customers__Price__6EAB62A3] FOREIGN KEY ([PriceListID]) REFERENCES [dbo].[PriceLists] ([PriceListID]);
ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [FK__Customers__Sales__6F9F86DC] FOREIGN KEY ([SalesRepID]) REFERENCES [dbo].[SalesRepresentatives] ([SalesRepID]);
ALTER TABLE [dbo].[Departments] ADD CONSTRAINT [FK__Departmen__Paren__7093AB15] FOREIGN KEY ([ParentDepartmentID]) REFERENCES [dbo].[Departments] ([DepartmentID]);
ALTER TABLE [dbo].[EmployeeLoans] ADD CONSTRAINT [FK__EmployeeL__Appro__74643BF9] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[EmployeeLeaveBalances] ADD CONSTRAINT [FK__EmployeeL__Emplo__7187CF4E] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[EmployeeLoans] ADD CONSTRAINT [FK__EmployeeL__Emplo__75586032] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[EmployeeLeaveBalances] ADD CONSTRAINT [FK__EmployeeL__Fisca__727BF387] FOREIGN KEY ([FiscalYearID]) REFERENCES [dbo].[FiscalYears] ([FiscalYearID]);
ALTER TABLE [dbo].[EmployeeLoans] ADD CONSTRAINT [FK__EmployeeL__Journ__764C846B] FOREIGN KEY ([JournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[EmployeeLeaveBalances] ADD CONSTRAINT [FK__EmployeeL__Leave__737017C0] FOREIGN KEY ([LeaveTypeID]) REFERENCES [dbo].[LeaveTypes] ([LeaveTypeID]);
ALTER TABLE [dbo].[EmployeeLoans] ADD CONSTRAINT [FK__EmployeeL__Payme__7740A8A4] FOREIGN KEY ([PaymentVoucherID]) REFERENCES [dbo].[PaymentVouchers] ([PaymentID]);
ALTER TABLE [dbo].[EmployeePenaltiesRewards] ADD CONSTRAINT [FK__EmployeeP__Appro__7834CCDD] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[EmployeePenaltiesRewards] ADD CONSTRAINT [FK__EmployeeP__Emplo__7928F116] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[EmployeePenaltiesRewards] ADD CONSTRAINT [FK__EmployeeP__Issue__7A1D154F] FOREIGN KEY ([IssuedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [FK__Employees__Depar__7B113988] FOREIGN KEY ([DepartmentID]) REFERENCES [dbo].[Departments] ([DepartmentID]);
ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [FK__Employees__JobTi__7C055DC1] FOREIGN KEY ([JobTitleID]) REFERENCES [dbo].[JobTitles] ([JobTitleID]);
ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [FK__Employees__Manag__7CF981FA] FOREIGN KEY ([ManagerID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[FiscalYears] ADD CONSTRAINT [FK__FiscalYea__Close__7DEDA633] FOREIGN KEY ([ClosedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK__GoodsRece__Appro__532343BF] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[GoodsReceiptDetails] ADD CONSTRAINT [FK__GoodsRece__GRNID__7EE1CA6C] FOREIGN KEY ([GRNID]) REFERENCES [dbo].[GoodsReceiptNotes] ([GRNID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK__GoodsRece__Inspe__02B25B50] FOREIGN KEY ([InspectedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[GoodsReceiptDetails] ADD CONSTRAINT [FK__GoodsRece__ItemI__7FD5EEA5] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK__GoodsRece__Linke__541767F8] FOREIGN KEY ([LinkedInvoiceID]) REFERENCES [dbo].[PurchaseInvoices] ([InvoiceID]);
ALTER TABLE [dbo].[GoodsReceiptDetails] ADD CONSTRAINT [FK__GoodsRece__PODet__00CA12DE] FOREIGN KEY ([PODetailID]) REFERENCES [dbo].[PurchaseOrderDetails] ([PODetailID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK__GoodsRece__Purch__03A67F89] FOREIGN KEY ([PurchaseOrderID]) REFERENCES [dbo].[PurchaseOrders] ([PurchaseOrderID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK__GoodsRece__Recei__049AA3C2] FOREIGN KEY ([ReceivedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK__GoodsRece__Suppl__058EC7FB] FOREIGN KEY ([SupplierID]) REFERENCES [dbo].[Suppliers] ([SupplierID]);
ALTER TABLE [dbo].[GoodsReceiptDetails] ADD CONSTRAINT [FK__GoodsRece__UnitI__01BE3717] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK__GoodsRece__Wareh__0682EC34] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[InventoryBalance] ADD CONSTRAINT [FK__Inventory__ItemI__0777106D] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[InventoryBatchBalance] ADD CONSTRAINT [FK__Inventory__ItemI__095F58DF] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[InventoryTransactions] ADD CONSTRAINT [FK__Inventory__ItemI__0B47A151] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[InventoryTransactions] ADD CONSTRAINT [FK__Inventory__UnitI__0C3BC58A] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[InventoryBalance] ADD CONSTRAINT [FK__Inventory__Wareh__086B34A6] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[InventoryBatchBalance] ADD CONSTRAINT [FK__Inventory__Wareh__0A537D18] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[InventoryTransactions] ADD CONSTRAINT [FK__Inventory__Wareh__0D2FE9C3] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ItemCategories] ADD CONSTRAINT [FK__ItemCateg__Paren__0E240DFC] FOREIGN KEY ([ParentCategoryID]) REFERENCES [dbo].[ItemCategories] ([CategoryID]);
ALTER TABLE [dbo].[Items] ADD CONSTRAINT [FK__Items__CategoryI__0F183235] FOREIGN KEY ([CategoryID]) REFERENCES [dbo].[ItemCategories] ([CategoryID]);
ALTER TABLE [dbo].[Items] ADD CONSTRAINT [FK__Items__PrimaryUn__100C566E] FOREIGN KEY ([PrimaryUnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[Items] ADD CONSTRAINT [FK__Items__Secondary__11007AA7] FOREIGN KEY ([SecondaryUnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[JournalEntryDetails] ADD CONSTRAINT [FK__JournalEn__Accou__17AD7836] FOREIGN KEY ([AccountID]) REFERENCES [dbo].[ChartOfAccounts] ([AccountID]);
ALTER TABLE [dbo].[JournalEntries] ADD CONSTRAINT [FK__JournalEn__Appro__11F49EE0] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[JournalEntryDetails] ADD CONSTRAINT [FK__JournalEn__CostC__18A19C6F] FOREIGN KEY ([CostCenterID]) REFERENCES [dbo].[CostCenters] ([CostCenterID]);
ALTER TABLE [dbo].[JournalEntries] ADD CONSTRAINT [FK__JournalEn__Curre__12E8C319] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[JournalEntryDetails] ADD CONSTRAINT [FK__JournalEn__Curre__1995C0A8] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[JournalEntries] ADD CONSTRAINT [FK__JournalEn__Fisca__13DCE752] FOREIGN KEY ([FiscalYearID]) REFERENCES [dbo].[FiscalYears] ([FiscalYearID]);
ALTER TABLE [dbo].[JournalEntryDetails] ADD CONSTRAINT [FK__JournalEn__Journ__1A89E4E1] FOREIGN KEY ([JournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[JournalEntries] ADD CONSTRAINT [FK__JournalEn__Perio__14D10B8B] FOREIGN KEY ([PeriodID]) REFERENCES [dbo].[AccountingPeriods] ([PeriodID]);
ALTER TABLE [dbo].[JournalEntries] ADD CONSTRAINT [FK__JournalEn__Poste__15C52FC4] FOREIGN KEY ([PostedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[JournalEntries] ADD CONSTRAINT [FK__JournalEn__Rever__16B953FD] FOREIGN KEY ([ReversedJournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[LeaveRequests] ADD CONSTRAINT [FK__LeaveRequ__Appro__1B7E091A] FOREIGN KEY ([ApprovedByManager]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[LeaveRequests] ADD CONSTRAINT [FK__LeaveRequ__Appro__1C722D53] FOREIGN KEY ([ApprovedByHR]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[LeaveRequests] ADD CONSTRAINT [FK__LeaveRequ__Emplo__1D66518C] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[LeaveRequests] ADD CONSTRAINT [FK__LeaveRequ__Leave__1E5A75C5] FOREIGN KEY ([LeaveTypeID]) REFERENCES [dbo].[LeaveTypes] ([LeaveTypeID]);
ALTER TABLE [dbo].[LeaveRequests] ADD CONSTRAINT [FK__LeaveRequ__Subst__1F4E99FE] FOREIGN KEY ([SubstituteEmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[LoanInstallments] ADD CONSTRAINT [FK__LoanInsta__LoanI__2042BE37] FOREIGN KEY ([LoanID]) REFERENCES [dbo].[EmployeeLoans] ([LoanID]);
ALTER TABLE [dbo].[LoginHistory] ADD CONSTRAINT [FK__LoginHist__UserI__2136E270] FOREIGN KEY ([UserID]) REFERENCES [dbo].[SystemUsers] ([UserID]);
ALTER TABLE [dbo].[MaterialIssueNotes] ADD CONSTRAINT [FK__MaterialI__Appro__25FB978D] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[MaterialIssueDetails] ADD CONSTRAINT [FK__MaterialI__Issue__222B06A9] FOREIGN KEY ([IssueID]) REFERENCES [dbo].[MaterialIssueNotes] ([IssueID]);
ALTER TABLE [dbo].[MaterialIssueNotes] ADD CONSTRAINT [FK__MaterialI__Issue__26EFBBC6] FOREIGN KEY ([IssuedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[MaterialIssueDetails] ADD CONSTRAINT [FK__MaterialI__ItemI__231F2AE2] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[MaterialIssueDetails] ADD CONSTRAINT [FK__MaterialI__POMat__24134F1B] FOREIGN KEY ([POMaterialID]) REFERENCES [dbo].[ProductionOrderMaterials] ([POMaterialID]);
ALTER TABLE [dbo].[MaterialIssueNotes] ADD CONSTRAINT [FK__MaterialI__Produ__27E3DFFF] FOREIGN KEY ([ProductionOrderID]) REFERENCES [dbo].[ProductionOrders] ([ProductionOrderID]);
ALTER TABLE [dbo].[MaterialIssueNotes] ADD CONSTRAINT [FK__MaterialI__Recei__28D80438] FOREIGN KEY ([ReceivedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[MaterialIssueDetails] ADD CONSTRAINT [FK__MaterialI__UnitI__25077354] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[MaterialIssueNotes] ADD CONSTRAINT [FK__MaterialI__Wareh__29CC2871] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[Notifications] ADD CONSTRAINT [FK__Notificat__Targe__2AC04CAA] FOREIGN KEY ([TargetUserID]) REFERENCES [dbo].[SystemUsers] ([UserID]);
ALTER TABLE [dbo].[Notifications] ADD CONSTRAINT [FK__Notificat__Targe__2BB470E3] FOREIGN KEY ([TargetRoleID]) REFERENCES [dbo].[UserRoles] ([RoleID]);
ALTER TABLE [dbo].[OverheadExpenses] ADD CONSTRAINT [FK__OverheadE__Accou__2CA8951C] FOREIGN KEY ([AccountID]) REFERENCES [dbo].[ChartOfAccounts] ([AccountID]);
ALTER TABLE [dbo].[OverheadExpenses] ADD CONSTRAINT [FK__OverheadE__CostC__2D9CB955] FOREIGN KEY ([CostCenterID]) REFERENCES [dbo].[CostCenters] ([CostCenterID]);
ALTER TABLE [dbo].[OverheadExpenses] ADD CONSTRAINT [FK__OverheadE__Journ__2E90DD8E] FOREIGN KEY ([JournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[OverheadExpenses] ADD CONSTRAINT [FK__OverheadE__Perio__2F8501C7] FOREIGN KEY ([PeriodID]) REFERENCES [dbo].[AccountingPeriods] ([PeriodID]);
ALTER TABLE [dbo].[PaymentAllocations] ADD CONSTRAINT [FK__PaymentAl__Invoi__30792600] FOREIGN KEY ([InvoiceID]) REFERENCES [dbo].[PurchaseInvoices] ([InvoiceID]);
ALTER TABLE [dbo].[PaymentAllocations] ADD CONSTRAINT [FK__PaymentAl__Payme__316D4A39] FOREIGN KEY ([PaymentID]) REFERENCES [dbo].[PaymentVouchers] ([PaymentID]);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [FK__PaymentVo__Appro__32616E72] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [FK__PaymentVo__BankA__335592AB] FOREIGN KEY ([BankAccountID]) REFERENCES [dbo].[BankAccounts] ([BankAccountID]);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [FK__PaymentVo__CashB__3449B6E4] FOREIGN KEY ([CashBoxID]) REFERENCES [dbo].[CashBoxes] ([CashBoxID]);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [FK__PaymentVo__CostC__353DDB1D] FOREIGN KEY ([CostCenterID]) REFERENCES [dbo].[CostCenters] ([CostCenterID]);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [FK__PaymentVo__Curre__3631FF56] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [FK__PaymentVo__Expen__3726238F] FOREIGN KEY ([ExpenseAccountID]) REFERENCES [dbo].[ChartOfAccounts] ([AccountID]);
ALTER TABLE [dbo].[PaymentVouchers] ADD CONSTRAINT [FK__PaymentVo__Journ__381A47C8] FOREIGN KEY ([JournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[Payroll] ADD CONSTRAINT [FK__Payroll__Approve__390E6C01] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[Payroll] ADD CONSTRAINT [FK__Payroll__Calcula__3A02903A] FOREIGN KEY ([CalculatedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[Payroll] ADD CONSTRAINT [FK__Payroll__Journal__3AF6B473] FOREIGN KEY ([JournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[Payroll] ADD CONSTRAINT [FK__Payroll__PaidBy__3BEAD8AC] FOREIGN KEY ([PaidBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[Payroll] ADD CONSTRAINT [FK__Payroll__PeriodI__3CDEFCE5] FOREIGN KEY ([PeriodID]) REFERENCES [dbo].[AccountingPeriods] ([PeriodID]);
ALTER TABLE [dbo].[PayrollDetails] ADD CONSTRAINT [FK__PayrollDe__Emplo__3DD3211E] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PayrollDetails] ADD CONSTRAINT [FK__PayrollDe__Payro__3EC74557] FOREIGN KEY ([PayrollID]) REFERENCES [dbo].[Payroll] ([PayrollID]);
ALTER TABLE [dbo].[PriceLists] ADD CONSTRAINT [FK__PriceList__Curre__4297D63B] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[PriceListDetails] ADD CONSTRAINT [FK__PriceList__ItemI__3FBB6990] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[PriceListDetails] ADD CONSTRAINT [FK__PriceList__Price__40AF8DC9] FOREIGN KEY ([PriceListID]) REFERENCES [dbo].[PriceLists] ([PriceListID]);
ALTER TABLE [dbo].[PriceListDetails] ADD CONSTRAINT [FK__PriceList__UnitI__41A3B202] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[ProductCostCards] ADD CONSTRAINT [FK__ProductCo__ItemI__438BFA74] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[ProductCostCards] ADD CONSTRAINT [FK__ProductCo__Perio__44801EAD] FOREIGN KEY ([PeriodID]) REFERENCES [dbo].[AccountingPeriods] ([PeriodID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Appro__52CE3E04] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Assig__53C2623D] FOREIGN KEY ([AssignedTo]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ProductionBatchStages] ADD CONSTRAINT [FK__Productio__Batch__4C214075] FOREIGN KEY ([BatchID]) REFERENCES [dbo].[ProductionBatches] ([BatchID]);
ALTER TABLE [dbo].[ProductionOrderMaterials] ADD CONSTRAINT [FK__Productio__BOMDe__4EFDAD20] FOREIGN KEY ([BOMDetailID]) REFERENCES [dbo].[BOMDetails] ([BOMDetailID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__BOMID__54B68676] FOREIGN KEY ([BOMID]) REFERENCES [dbo].[BillOfMaterials] ([BOMID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Contr__32B6742D] FOREIGN KEY ([ContractOrderID]) REFERENCES [dbo].[ContractOrders] ([ContractOrderID]);
ALTER TABLE [dbo].[ProductionOrderMaterials] ADD CONSTRAINT [FK__Productio__ItemI__4FF1D159] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[ProductionBatchStages] ADD CONSTRAINT [FK__Productio__Opera__4D1564AE] FOREIGN KEY ([OperatorID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Plann__55AAAAAF] FOREIGN KEY ([PlannedUnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [FK__Productio__Produ__457442E6] FOREIGN KEY ([ProductionOrderID]) REFERENCES [dbo].[ProductionOrders] ([ProductionOrderID]);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [FK__Productio__Produ__4668671F] FOREIGN KEY ([ProductItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [FK__Productio__Produ__475C8B58] FOREIGN KEY ([ProducedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ProductionOrderMaterials] ADD CONSTRAINT [FK__Productio__Produ__50E5F592] FOREIGN KEY ([ProductionOrderID]) REFERENCES [dbo].[ProductionOrders] ([ProductionOrderID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Produ__569ECEE8] FOREIGN KEY ([ProductItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [FK__Productio__QCIns__4850AF91] FOREIGN KEY ([QCInspectedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Sourc__5792F321] FOREIGN KEY ([SourceWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ProductionBatchStages] ADD CONSTRAINT [FK__Productio__Stage__4E0988E7] FOREIGN KEY ([StageID]) REFERENCES [dbo].[ProductionStages] ([StageID]);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [FK__Productio__Super__4944D3CA] FOREIGN KEY ([SupervisorID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Super__5887175A] FOREIGN KEY ([SupervisorID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [FK__Productio__Targe__4A38F803] FOREIGN KEY ([TargetWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ProductionOrders] ADD CONSTRAINT [FK__Productio__Targe__597B3B93] FOREIGN KEY ([TargetWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ProductionBatches] ADD CONSTRAINT [FK__Productio__UnitI__4B2D1C3C] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[ProductionOrderMaterials] ADD CONSTRAINT [FK__Productio__UnitI__51DA19CB] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[PublicHolidays] ADD CONSTRAINT [FK__PublicHol__Fisca__5A6F5FCC] FOREIGN KEY ([FiscalYearID]) REFERENCES [dbo].[FiscalYears] ([FiscalYearID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__Appro__611C5D5B] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__Curre__62108194] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[PurchaseInvoiceDetails] ADD CONSTRAINT [FK__PurchaseI__GRNDe__5B638405] FOREIGN KEY ([GRNDetailID]) REFERENCES [dbo].[GoodsReceiptDetails] ([GRNDetailID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__GRNID__6304A5CD] FOREIGN KEY ([GRNID]) REFERENCES [dbo].[GoodsReceiptNotes] ([GRNID]);
ALTER TABLE [dbo].[PurchaseInvoiceDetails] ADD CONSTRAINT [FK__PurchaseI__Invoi__5C57A83E] FOREIGN KEY ([InvoiceID]) REFERENCES [dbo].[PurchaseInvoices] ([InvoiceID]);
ALTER TABLE [dbo].[PurchaseInvoiceDetails] ADD CONSTRAINT [FK__PurchaseI__ItemI__5D4BCC77] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__Payme__63F8CA06] FOREIGN KEY ([PaymentTermID]) REFERENCES [dbo].[PaymentTerms] ([PaymentTermID]);
ALTER TABLE [dbo].[PurchaseInvoiceDetails] ADD CONSTRAINT [FK__PurchaseI__PODet__5E3FF0B0] FOREIGN KEY ([PODetailID]) REFERENCES [dbo].[PurchaseOrderDetails] ([PODetailID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__Purch__64ECEE3F] FOREIGN KEY ([PurchaseOrderID]) REFERENCES [dbo].[PurchaseOrders] ([PurchaseOrderID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__Rejec__59D0414E] FOREIGN KEY ([RejectedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__Submi__58DC1D15] FOREIGN KEY ([SubmittedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseInvoices] ADD CONSTRAINT [FK__PurchaseI__Suppl__65E11278] FOREIGN KEY ([SupplierID]) REFERENCES [dbo].[Suppliers] ([SupplierID]);
ALTER TABLE [dbo].[PurchaseInvoiceDetails] ADD CONSTRAINT [FK__PurchaseI__UnitI__5F3414E9] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[PurchaseInvoiceDetails] ADD CONSTRAINT [FK__PurchaseI__Wareh__60283922] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Appro__69B1A35C] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Cance__6AA5C795] FOREIGN KEY ([CancelledBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Close__6B99EBCE] FOREIGN KEY ([ClosedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Curre__6C8E1007] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[PurchaseOrderDetails] ADD CONSTRAINT [FK__PurchaseO__ItemI__66D536B1] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Payme__6D823440] FOREIGN KEY ([PaymentTermID]) REFERENCES [dbo].[PaymentTerms] ([PaymentTermID]);
ALTER TABLE [dbo].[PurchaseOrderDetails] ADD CONSTRAINT [FK__PurchaseO__Purch__67C95AEA] FOREIGN KEY ([PurchaseOrderID]) REFERENCES [dbo].[PurchaseOrders] ([PurchaseOrderID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Rejec__4B8221F7] FOREIGN KEY ([RejectedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseOrderDetails] ADD CONSTRAINT [FK__PurchaseO__Reque__46BD6CDA] FOREIGN KEY ([RequestDetailID]) REFERENCES [dbo].[PurchaseRequestDetails] ([RequestDetailID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Reque__6E765879] FOREIGN KEY ([RequestID]) REFERENCES [dbo].[PurchaseRequests] ([RequestID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__SentB__4C764630] FOREIGN KEY ([SentBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Submi__4A8DFDBE] FOREIGN KEY ([SubmittedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Suppl__6F6A7CB2] FOREIGN KEY ([SupplierID]) REFERENCES [dbo].[Suppliers] ([SupplierID]);
ALTER TABLE [dbo].[PurchaseOrderDetails] ADD CONSTRAINT [FK__PurchaseO__UnitI__68BD7F23] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[PurchaseOrders] ADD CONSTRAINT [FK__PurchaseO__Wareh__705EA0EB] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[PurchaseRequests] ADD CONSTRAINT [FK__PurchaseR__Appro__742F31CF] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseReturns] ADD CONSTRAINT [FK__PurchaseR__Appro__7ADC2F5E] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseRequests] ADD CONSTRAINT [FK__PurchaseR__Depar__75235608] FOREIGN KEY ([DepartmentID]) REFERENCES [dbo].[Departments] ([DepartmentID]);
ALTER TABLE [dbo].[PurchaseReturnDetails] ADD CONSTRAINT [FK__PurchaseR__Invoi__770B9E7A] FOREIGN KEY ([InvoiceDetailID]) REFERENCES [dbo].[PurchaseInvoiceDetails] ([InvoiceDetailID]);
ALTER TABLE [dbo].[PurchaseReturns] ADD CONSTRAINT [FK__PurchaseR__Invoi__7BD05397] FOREIGN KEY ([InvoiceID]) REFERENCES [dbo].[PurchaseInvoices] ([InvoiceID]);
ALTER TABLE [dbo].[PurchaseRequestDetails] ADD CONSTRAINT [FK__PurchaseR__ItemI__7152C524] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[PurchaseReturnDetails] ADD CONSTRAINT [FK__PurchaseR__ItemI__77FFC2B3] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[PurchaseReturns] ADD CONSTRAINT [FK__PurchaseR__Rejec__6265874F] FOREIGN KEY ([RejectedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseRequestDetails] ADD CONSTRAINT [FK__PurchaseR__Reque__7246E95D] FOREIGN KEY ([RequestID]) REFERENCES [dbo].[PurchaseRequests] ([RequestID]);
ALTER TABLE [dbo].[PurchaseRequests] ADD CONSTRAINT [FK__PurchaseR__Reque__76177A41] FOREIGN KEY ([RequestedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseReturnDetails] ADD CONSTRAINT [FK__PurchaseR__Retur__78F3E6EC] FOREIGN KEY ([ReturnID]) REFERENCES [dbo].[PurchaseReturns] ([ReturnID]);
ALTER TABLE [dbo].[PurchaseReturns] ADD CONSTRAINT [FK__PurchaseR__Submi__61716316] FOREIGN KEY ([SubmittedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[PurchaseReturns] ADD CONSTRAINT [FK__PurchaseR__Suppl__7CC477D0] FOREIGN KEY ([SupplierID]) REFERENCES [dbo].[Suppliers] ([SupplierID]);
ALTER TABLE [dbo].[PurchaseRequestDetails] ADD CONSTRAINT [FK__PurchaseR__UnitI__733B0D96] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[PurchaseReturnDetails] ADD CONSTRAINT [FK__PurchaseR__UnitI__79E80B25] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[PurchaseReturns] ADD CONSTRAINT [FK__PurchaseR__Wareh__7DB89C09] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[ReceiptAllocations] ADD CONSTRAINT [FK__ReceiptAl__Invoi__7EACC042] FOREIGN KEY ([InvoiceID]) REFERENCES [dbo].[SalesInvoices] ([InvoiceID]);
ALTER TABLE [dbo].[ReceiptAllocations] ADD CONSTRAINT [FK__ReceiptAl__Recei__7FA0E47B] FOREIGN KEY ([ReceiptID]) REFERENCES [dbo].[ReceiptVouchers] ([ReceiptID]);
ALTER TABLE [dbo].[ReceiptVouchers] ADD CONSTRAINT [FK__ReceiptVo__BankA__009508B4] FOREIGN KEY ([BankAccountID]) REFERENCES [dbo].[BankAccounts] ([BankAccountID]);
ALTER TABLE [dbo].[ReceiptVouchers] ADD CONSTRAINT [FK__ReceiptVo__CashB__01892CED] FOREIGN KEY ([CashBoxID]) REFERENCES [dbo].[CashBoxes] ([CashBoxID]);
ALTER TABLE [dbo].[ReceiptVouchers] ADD CONSTRAINT [FK__ReceiptVo__CostC__097F5470] FOREIGN KEY ([CostCenterID]) REFERENCES [dbo].[CostCenters] ([CostCenterID]);
ALTER TABLE [dbo].[ReceiptVouchers] ADD CONSTRAINT [FK__ReceiptVo__Curre__027D5126] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[ReceiptVouchers] ADD CONSTRAINT [FK__ReceiptVo__Journ__0371755F] FOREIGN KEY ([JournalID]) REFERENCES [dbo].[JournalEntries] ([JournalID]);
ALTER TABLE [dbo].[ReceiptVouchers] ADD CONSTRAINT [FK__ReceiptVo__Reven__088B3037] FOREIGN KEY ([RevenueAccountID]) REFERENCES [dbo].[ChartOfAccounts] ([AccountID]);
ALTER TABLE [dbo].[RolePermissions] ADD CONSTRAINT [FK__RolePermi__Modul__04659998] FOREIGN KEY ([ModuleID]) REFERENCES [dbo].[SystemModules] ([ModuleID]);
ALTER TABLE [dbo].[RolePermissions] ADD CONSTRAINT [FK__RolePermi__RoleI__0559BDD1] FOREIGN KEY ([RoleID]) REFERENCES [dbo].[UserRoles] ([RoleID]);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [FK__SalesInvo__Appro__0B129727] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [FK__SalesInvo__Curre__0C06BB60] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [FK__SalesInvo__Custo__0CFADF99] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[SalesInvoiceDetails] ADD CONSTRAINT [FK__SalesInvo__Invoi__064DE20A] FOREIGN KEY ([InvoiceID]) REFERENCES [dbo].[SalesInvoices] ([InvoiceID]);
ALTER TABLE [dbo].[SalesInvoiceDetails] ADD CONSTRAINT [FK__SalesInvo__ItemI__07420643] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [FK__SalesInvo__Payme__0DEF03D2] FOREIGN KEY ([PaymentTermID]) REFERENCES [dbo].[PaymentTerms] ([PaymentTermID]);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [FK__SalesInvo__Sales__0EE3280B] FOREIGN KEY ([SalesOrderID]) REFERENCES [dbo].[SalesOrders] ([SalesOrderID]);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [FK__SalesInvo__Sales__0FD74C44] FOREIGN KEY ([SalesRepID]) REFERENCES [dbo].[SalesRepresentatives] ([SalesRepID]);
ALTER TABLE [dbo].[SalesInvoiceDetails] ADD CONSTRAINT [FK__SalesInvo__SODet__08362A7C] FOREIGN KEY ([SODetailID]) REFERENCES [dbo].[SalesOrderDetails] ([SODetailID]);
ALTER TABLE [dbo].[SalesInvoiceDetails] ADD CONSTRAINT [FK__SalesInvo__UnitI__092A4EB5] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[SalesInvoiceDetails] ADD CONSTRAINT [FK__SalesInvo__Wareh__0A1E72EE] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[SalesInvoices] ADD CONSTRAINT [FK__SalesInvo__Wareh__10CB707D] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Appro__149C0161] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Curre__1590259A] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Custo__168449D3] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[SalesOrderDetails] ADD CONSTRAINT [FK__SalesOrde__ItemI__11BF94B6] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Payme__17786E0C] FOREIGN KEY ([PaymentTermID]) REFERENCES [dbo].[PaymentTerms] ([PaymentTermID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Price__186C9245] FOREIGN KEY ([PriceListID]) REFERENCES [dbo].[PriceLists] ([PriceListID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Quota__1960B67E] FOREIGN KEY ([QuotationID]) REFERENCES [dbo].[SalesQuotations] ([QuotationID]);
ALTER TABLE [dbo].[SalesOrderDetails] ADD CONSTRAINT [FK__SalesOrde__Sales__12B3B8EF] FOREIGN KEY ([SalesOrderID]) REFERENCES [dbo].[SalesOrders] ([SalesOrderID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Sales__1A54DAB7] FOREIGN KEY ([SalesRepID]) REFERENCES [dbo].[SalesRepresentatives] ([SalesRepID]);
ALTER TABLE [dbo].[SalesOrderDetails] ADD CONSTRAINT [FK__SalesOrde__UnitI__13A7DD28] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[SalesOrders] ADD CONSTRAINT [FK__SalesOrde__Wareh__1B48FEF0] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[SalesQuotations] ADD CONSTRAINT [FK__SalesQuot__Curre__1F198FD4] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[SalesQuotations] ADD CONSTRAINT [FK__SalesQuot__Custo__200DB40D] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[SalesQuotationDetails] ADD CONSTRAINT [FK__SalesQuot__ItemI__1C3D2329] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[SalesQuotations] ADD CONSTRAINT [FK__SalesQuot__Payme__2101D846] FOREIGN KEY ([PaymentTermID]) REFERENCES [dbo].[PaymentTerms] ([PaymentTermID]);
ALTER TABLE [dbo].[SalesQuotations] ADD CONSTRAINT [FK__SalesQuot__Price__21F5FC7F] FOREIGN KEY ([PriceListID]) REFERENCES [dbo].[PriceLists] ([PriceListID]);
ALTER TABLE [dbo].[SalesQuotationDetails] ADD CONSTRAINT [FK__SalesQuot__Quota__1D314762] FOREIGN KEY ([QuotationID]) REFERENCES [dbo].[SalesQuotations] ([QuotationID]);
ALTER TABLE [dbo].[SalesQuotations] ADD CONSTRAINT [FK__SalesQuot__Sales__22EA20B8] FOREIGN KEY ([SalesRepID]) REFERENCES [dbo].[SalesRepresentatives] ([SalesRepID]);
ALTER TABLE [dbo].[SalesQuotationDetails] ADD CONSTRAINT [FK__SalesQuot__UnitI__1E256B9B] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[SalesRepresentatives] ADD CONSTRAINT [FK__SalesRepr__Emplo__23DE44F1] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[SalesReturns] ADD CONSTRAINT [FK__SalesRetu__Appro__28A2FA0E] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[SalesReturns] ADD CONSTRAINT [FK__SalesRetu__Custo__29971E47] FOREIGN KEY ([CustomerID]) REFERENCES [dbo].[Customers] ([CustomerID]);
ALTER TABLE [dbo].[SalesReturnDetails] ADD CONSTRAINT [FK__SalesRetu__Invoi__24D2692A] FOREIGN KEY ([InvoiceDetailID]) REFERENCES [dbo].[SalesInvoiceDetails] ([InvoiceDetailID]);
ALTER TABLE [dbo].[SalesReturns] ADD CONSTRAINT [FK__SalesRetu__Invoi__2A8B4280] FOREIGN KEY ([InvoiceID]) REFERENCES [dbo].[SalesInvoices] ([InvoiceID]);
ALTER TABLE [dbo].[SalesReturnDetails] ADD CONSTRAINT [FK__SalesRetu__ItemI__25C68D63] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[SalesReturnDetails] ADD CONSTRAINT [FK__SalesRetu__Retur__26BAB19C] FOREIGN KEY ([ReturnID]) REFERENCES [dbo].[SalesReturns] ([ReturnID]);
ALTER TABLE [dbo].[SalesReturns] ADD CONSTRAINT [FK__SalesRetu__Sales__2B7F66B9] FOREIGN KEY ([SalesRepID]) REFERENCES [dbo].[SalesRepresentatives] ([SalesRepID]);
ALTER TABLE [dbo].[SalesReturnDetails] ADD CONSTRAINT [FK__SalesRetu__UnitI__27AED5D5] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[SalesReturns] ADD CONSTRAINT [FK__SalesRetu__Wareh__2C738AF2] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[StockCounts] ADD CONSTRAINT [FK__StockCoun__Appro__30441BD6] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[StockCounts] ADD CONSTRAINT [FK__StockCoun__Categ__3138400F] FOREIGN KEY ([CategoryID]) REFERENCES [dbo].[ItemCategories] ([CategoryID]);
ALTER TABLE [dbo].[StockCountDetails] ADD CONSTRAINT [FK__StockCoun__Count__2D67AF2B] FOREIGN KEY ([CountID]) REFERENCES [dbo].[StockCounts] ([CountID]);
ALTER TABLE [dbo].[StockCounts] ADD CONSTRAINT [FK__StockCoun__Count__322C6448] FOREIGN KEY ([CountedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[StockCountDetails] ADD CONSTRAINT [FK__StockCoun__ItemI__2E5BD364] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[StockCounts] ADD CONSTRAINT [FK__StockCoun__Super__33208881] FOREIGN KEY ([SupervisorID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[StockCountDetails] ADD CONSTRAINT [FK__StockCoun__UnitI__2F4FF79D] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[StockCounts] ADD CONSTRAINT [FK__StockCoun__Wareh__3414ACBA] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[StockTransfers] ADD CONSTRAINT [FK__StockTran__Appro__37E53D9E] FOREIGN KEY ([ApprovedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[StockTransfers] ADD CONSTRAINT [FK__StockTran__FromW__38D961D7] FOREIGN KEY ([FromWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[StockTransferDetails] ADD CONSTRAINT [FK__StockTran__ItemI__3508D0F3] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[StockTransfers] ADD CONSTRAINT [FK__StockTran__Reque__39CD8610] FOREIGN KEY ([RequestedBy]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[StockTransfers] ADD CONSTRAINT [FK__StockTran__ToWar__3AC1AA49] FOREIGN KEY ([ToWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[StockTransferDetails] ADD CONSTRAINT [FK__StockTran__Trans__35FCF52C] FOREIGN KEY ([TransferID]) REFERENCES [dbo].[StockTransfers] ([TransferID]);
ALTER TABLE [dbo].[StockTransferDetails] ADD CONSTRAINT [FK__StockTran__UnitI__36F11965] FOREIGN KEY ([UnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[SupplierContacts] ADD CONSTRAINT [FK__SupplierC__Suppl__3BB5CE82] FOREIGN KEY ([SupplierID]) REFERENCES [dbo].[Suppliers] ([SupplierID]);
ALTER TABLE [dbo].[SupplierItems] ADD CONSTRAINT [FK__SupplierI__Curre__3CA9F2BB] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[SupplierItems] ADD CONSTRAINT [FK__SupplierI__ItemI__3D9E16F4] FOREIGN KEY ([ItemID]) REFERENCES [dbo].[Items] ([ItemID]);
ALTER TABLE [dbo].[SupplierItems] ADD CONSTRAINT [FK__SupplierI__Suppl__3E923B2D] FOREIGN KEY ([SupplierID]) REFERENCES [dbo].[Suppliers] ([SupplierID]);
ALTER TABLE [dbo].[Suppliers] ADD CONSTRAINT [FK__Suppliers__Curre__3F865F66] FOREIGN KEY ([CurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
ALTER TABLE [dbo].[Suppliers] ADD CONSTRAINT [FK__Suppliers__Payme__407A839F] FOREIGN KEY ([PaymentTermID]) REFERENCES [dbo].[PaymentTerms] ([PaymentTermID]);
ALTER TABLE [dbo].[SystemModules] ADD CONSTRAINT [FK__SystemMod__Paren__416EA7D8] FOREIGN KEY ([ParentModuleID]) REFERENCES [dbo].[SystemModules] ([ModuleID]);
ALTER TABLE [dbo].[SystemUsers] ADD CONSTRAINT [FK__SystemUse__Defau__4262CC11] FOREIGN KEY ([DefaultWarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[SystemUsers] ADD CONSTRAINT [FK__SystemUse__Defau__4356F04A] FOREIGN KEY ([DefaultCashBoxID]) REFERENCES [dbo].[CashBoxes] ([CashBoxID]);
ALTER TABLE [dbo].[SystemUsers] ADD CONSTRAINT [FK__SystemUse__Emplo__444B1483] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[SystemUsers] ADD CONSTRAINT [FK__SystemUse__RoleI__453F38BC] FOREIGN KEY ([RoleID]) REFERENCES [dbo].[UserRoles] ([RoleID]);
ALTER TABLE [dbo].[UnitConversions] ADD CONSTRAINT [FK__UnitConve__FromU__46335CF5] FOREIGN KEY ([FromUnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[UnitConversions] ADD CONSTRAINT [FK__UnitConve__ToUni__4727812E] FOREIGN KEY ([ToUnitID]) REFERENCES [dbo].[Units] ([UnitID]);
ALTER TABLE [dbo].[UserPermissions] ADD CONSTRAINT [FK__UserPermi__Modul__481BA567] FOREIGN KEY ([ModuleID]) REFERENCES [dbo].[SystemModules] ([ModuleID]);
ALTER TABLE [dbo].[UserPermissions] ADD CONSTRAINT [FK__UserPermi__UserI__490FC9A0] FOREIGN KEY ([UserID]) REFERENCES [dbo].[SystemUsers] ([UserID]);
ALTER TABLE [dbo].[UserPreferences] ADD CONSTRAINT [FK__UserPrefe__UserI__4A03EDD9] FOREIGN KEY ([UserID]) REFERENCES [dbo].[SystemUsers] ([UserID]);
ALTER TABLE [dbo].[Warehouses] ADD CONSTRAINT [FK__Warehouse__Manag__526429B0] FOREIGN KEY ([ManagerID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[GoodsReceiptDetails] ADD CONSTRAINT [FK_GoodsReceiptDetails_Warehouses] FOREIGN KEY ([WarehouseID]) REFERENCES [dbo].[Warehouses] ([WarehouseID]);
ALTER TABLE [dbo].[GoodsReceiptNotes] ADD CONSTRAINT [FK_GoodsReceiptNotes_AssignedInspector] FOREIGN KEY ([AssignedInspectorID]) REFERENCES [dbo].[Employees] ([EmployeeID]);
ALTER TABLE [dbo].[SystemUsers] ADD CONSTRAINT [FK_SystemUsers_DefaultCurrencyID_Currencies] FOREIGN KEY ([DefaultCurrencyID]) REFERENCES [dbo].[Currencies] ([CurrencyID]);
GO

CREATE INDEX [IX_AccPeriod_Dates] ON [dbo].[AccountingPeriods] (StartDate, EndDate);
CREATE INDEX [IX_AccPeriod_FiscalYear] ON [dbo].[AccountingPeriods] (FiscalYearID);
CREATE UNIQUE INDEX [UQ_AccPeriod] ON [dbo].[AccountingPeriods] (FiscalYearID, PeriodNumber);
CREATE INDEX [IX_Attend_Date] ON [dbo].[AttendanceRecords] (AttendanceDate);
CREATE INDEX [IX_Attend_EmpID] ON [dbo].[AttendanceRecords] (EmployeeID);
CREATE INDEX [IX_Attend_Status] ON [dbo].[AttendanceRecords] (AttendanceStatus);
CREATE UNIQUE INDEX [UQ_Attendance] ON [dbo].[AttendanceRecords] (EmployeeID, AttendanceDate);
CREATE INDEX [IX_Audit_Action] ON [dbo].[AuditLog] (ActionType);
CREATE INDEX [IX_Audit_Date] ON [dbo].[AuditLog] (AuditDate);
CREATE INDEX [IX_Audit_RecordID] ON [dbo].[AuditLog] (TableName, RecordID);
CREATE INDEX [IX_Audit_Table] ON [dbo].[AuditLog] (TableName);
CREATE INDEX [IX_Audit_UserID] ON [dbo].[AuditLog] (UserID);
CREATE UNIQUE INDEX [UQ__BankAcco__06D1A5C0AE0BB155] ON [dbo].[BankAccounts] (BankAccountCode);
CREATE INDEX [IX_BOM_ProductID] ON [dbo].[BillOfMaterials] (ProductItemID);
CREATE INDEX [IX_BOM_Status] ON [dbo].[BillOfMaterials] (BOMStatus);
CREATE UNIQUE INDEX [UQ__BillOfMa__45EA9B8F3119194D] ON [dbo].[BillOfMaterials] (BOMCode);
CREATE UNIQUE INDEX [UQ_BOM_Product_Version] ON [dbo].[BillOfMaterials] (ProductItemID, BOMVersion);
CREATE INDEX [IX_BOMDet_BOMID] ON [dbo].[BOMDetails] (BOMID);
CREATE INDEX [IX_BOMDet_ItemID] ON [dbo].[BOMDetails] (ItemID);
CREATE INDEX [IX_BOMDet_MaterialType] ON [dbo].[BOMDetails] (MaterialType);
CREATE UNIQUE INDEX [UQ_BOMDetails] ON [dbo].[BOMDetails] (BOMID, LineNumber);
CREATE INDEX [IX_BOMStages_BOMID] ON [dbo].[BOMStages] (BOMID);
CREATE UNIQUE INDEX [UQ_BOMStages] ON [dbo].[BOMStages] (BOMID, StageOrder);
CREATE UNIQUE INDEX [UQ__CashBoxe__A411E6DAAEDC0DDC] ON [dbo].[CashBoxes] (CashBoxCode);
CREATE UNIQUE INDEX [UQ__CashTran__9548BE624C8472A3] ON [dbo].[CashTransfers] (TransferNo);
CREATE INDEX [IX_COA_IsDetail] ON [dbo].[ChartOfAccounts] (IsDetailAccount);
CREATE INDEX [IX_COA_Level] ON [dbo].[ChartOfAccounts] (AccountLevel);
CREATE INDEX [IX_COA_LinkedEntity] ON [dbo].[ChartOfAccounts] (LinkedEntityType, LinkedEntityID);
CREATE INDEX [IX_COA_ParentID] ON [dbo].[ChartOfAccounts] (ParentAccountID);
CREATE INDEX [IX_COA_Type] ON [dbo].[ChartOfAccounts] (AccountType);
CREATE UNIQUE INDEX [UQ__ChartOfA__38D0C56AB980079B] ON [dbo].[ChartOfAccounts] (AccountCode);
CREATE INDEX [IX_Cheque_DueDate] ON [dbo].[Cheques] (DueDate);
CREATE INDEX [IX_Cheque_Entity] ON [dbo].[Cheques] (EntityType, EntityID);
CREATE INDEX [IX_Cheque_Status] ON [dbo].[Cheques] (ChequeStatus);
CREATE INDEX [IX_Cheque_Type] ON [dbo].[Cheques] (ChequeType);
CREATE INDEX [IX_ContractDel_Contract] ON [dbo].[ContractDeliveries] (ContractOrderID);
CREATE INDEX [IX_ContractDel_Customer] ON [dbo].[ContractDeliveries] (CustomerID);
CREATE UNIQUE INDEX [UQ__Contract__CB28B43758E41618] ON [dbo].[ContractDeliveries] (DeliveryNumber);
CREATE INDEX [IX_ContractDelDet_Delivery] ON [dbo].[ContractDeliveryDetails] (DeliveryID);
CREATE UNIQUE INDEX [UQ_ContractDelDet] ON [dbo].[ContractDeliveryDetails] (DeliveryID, LineNumber);
CREATE INDEX [IX_ContractMatSrc_Contract] ON [dbo].[ContractMaterialSources] (ContractOrderID);
CREATE INDEX [IX_ContractMatSrc_Detail] ON [dbo].[ContractMaterialSources] (ContractDetailID);
CREATE INDEX [IX_ContractMatSrc_Item] ON [dbo].[ContractMaterialSources] (ItemID);
CREATE UNIQUE INDEX [UQ_ContractMatSource] ON [dbo].[ContractMaterialSources] (ContractOrderID, ContractDetailID, ItemID);
CREATE INDEX [IX_ContractDet_OrderID] ON [dbo].[ContractOrderDetails] (ContractOrderID);
CREATE INDEX [IX_ContractDet_Product] ON [dbo].[ContractOrderDetails] (ProductItemID);
CREATE UNIQUE INDEX [UQ_ContractDetails] ON [dbo].[ContractOrderDetails] (ContractOrderID, LineNumber);
CREATE INDEX [IX_ContractOrd_Customer] ON [dbo].[ContractOrders] (CustomerID);
CREATE INDEX [IX_ContractOrd_Date] ON [dbo].[ContractOrders] (ContractDate);
CREATE INDEX [IX_ContractOrd_Status] ON [dbo].[ContractOrders] (ContractStatus);
CREATE UNIQUE INDEX [UQ__Contract__C51D43DA415C94EB] ON [dbo].[ContractOrders] (ContractNumber);
CREATE UNIQUE INDEX [UQ__CostCent__5662EF4711B88FCE] ON [dbo].[CostCenters] (CostCenterCode);
CREATE UNIQUE INDEX [UQ__Currenci__408426BFE3F46083] ON [dbo].[Currencies] (CurrencyCode);
CREATE INDEX [IX_CustomerContacts_CustomerID] ON [dbo].[CustomerContacts] (CustomerID);
CREATE UNIQUE INDEX [UQ__Customer__3B974380A82C158E] ON [dbo].[CustomerGroups] (GroupCode);
CREATE INDEX [IX_CustMatRecDet_Item] ON [dbo].[CustomerMaterialReceiptDetails] (ItemID);
CREATE INDEX [IX_CustMatRecDet_Receipt] ON [dbo].[CustomerMaterialReceiptDetails] (ReceiptID);
CREATE UNIQUE INDEX [UQ_CustMatRecDet] ON [dbo].[CustomerMaterialReceiptDetails] (ReceiptID, LineNumber);
CREATE INDEX [IX_CustMatRec_Contract] ON [dbo].[CustomerMaterialReceipts] (ContractOrderID);
CREATE INDEX [IX_CustMatRec_Customer] ON [dbo].[CustomerMaterialReceipts] (CustomerID);
CREATE UNIQUE INDEX [UQ__Customer__C08AFDAB19735711] ON [dbo].[CustomerMaterialReceipts] (ReceiptNumber);
CREATE INDEX [IX_CustMatRetDet_Return] ON [dbo].[CustomerMaterialReturnDetails] (ReturnID);
CREATE UNIQUE INDEX [UQ_CustMatRetDet] ON [dbo].[CustomerMaterialReturnDetails] (ReturnID, LineNumber);
CREATE INDEX [IX_CustMatRet_Contract] ON [dbo].[CustomerMaterialReturns] (ContractOrderID);
CREATE UNIQUE INDEX [UQ__Customer__2739D7BB4FF75902] ON [dbo].[CustomerMaterialReturns] (ReturnNumber);
CREATE INDEX [IX_Customers_GroupID] ON [dbo].[Customers] (CustomerGroupID);
CREATE INDEX [IX_Customers_IsActive] ON [dbo].[Customers] (IsActive);
CREATE INDEX [IX_Customers_SalesRepID] ON [dbo].[Customers] (SalesRepID);
CREATE INDEX [IX_Customers_Type] ON [dbo].[Customers] (CustomerType);
CREATE UNIQUE INDEX [UQ__Customer__066785217A714B7D] ON [dbo].[Customers] (CustomerCode);
CREATE UNIQUE INDEX [UQ__Departme__6EA8896D78FD820E] ON [dbo].[Departments] (DepartmentCode);
CREATE INDEX [IX_EmpLeaveBal_EmpID] ON [dbo].[EmployeeLeaveBalances] (EmployeeID);
CREATE UNIQUE INDEX [UQ_EmpLeaveBalance] ON [dbo].[EmployeeLeaveBalances] (EmployeeID, LeaveTypeID, FiscalYearID);
CREATE INDEX [IX_EmpLoan_EmpID] ON [dbo].[EmployeeLoans] (EmployeeID);
CREATE INDEX [IX_EmpLoan_Status] ON [dbo].[EmployeeLoans] (LoanStatus);
CREATE UNIQUE INDEX [UQ__Employee__EEC26628D43E1BE5] ON [dbo].[EmployeeLoans] (LoanNumber);
CREATE INDEX [IX_EmpPR_Date] ON [dbo].[EmployeePenaltiesRewards] (RecordDate);
CREATE INDEX [IX_EmpPR_EmpID] ON [dbo].[EmployeePenaltiesRewards] (EmployeeID);
CREATE INDEX [IX_EmpPR_Type] ON [dbo].[EmployeePenaltiesRewards] (RecordType);
CREATE INDEX [IX_Employees_DepartmentID] ON [dbo].[Employees] (DepartmentID);
CREATE INDEX [IX_Employees_IsActive] ON [dbo].[Employees] (IsActive);
CREATE INDEX [IX_Employees_JobTitleID] ON [dbo].[Employees] (JobTitleID);
CREATE INDEX [IX_Employees_ManagerID] ON [dbo].[Employees] (ManagerID);
CREATE INDEX [IX_Employees_Status] ON [dbo].[Employees] (EmployeeStatus);
CREATE UNIQUE INDEX [UQ__Employee__1F64254855FD03B1] ON [dbo].[Employees] (EmployeeCode);
CREATE UNIQUE INDEX [UQ__FiscalYe__C44646A791C3882A] ON [dbo].[FiscalYears] (YearCode);
CREATE INDEX [IX_GoodsReceiptDetails_WarehouseID] ON [dbo].[GoodsReceiptDetails] (WarehouseID);
CREATE INDEX [IX_GRNDet_BatchNumber] ON [dbo].[GoodsReceiptDetails] (BatchNumber);
CREATE INDEX [IX_GRNDet_GRNID] ON [dbo].[GoodsReceiptDetails] (GRNID);
CREATE INDEX [IX_GRNDet_ItemID] ON [dbo].[GoodsReceiptDetails] (ItemID);
CREATE INDEX [IX_GRNDet_PODetailID] ON [dbo].[GoodsReceiptDetails] (PODetailID);
CREATE INDEX [IX_GRNDetails_PODetailID] ON [dbo].[GoodsReceiptDetails] (PODetailID);
CREATE UNIQUE INDEX [UQ_GRNDetails] ON [dbo].[GoodsReceiptDetails] (GRNID, LineNumber);
CREATE INDEX [IX_GRN_Date] ON [dbo].[GoodsReceiptNotes] (GRNDate);
CREATE INDEX [IX_GRN_GRNSource] ON [dbo].[GoodsReceiptNotes] (GRNSource);
CREATE INDEX [IX_GRN_POID] ON [dbo].[GoodsReceiptNotes] (PurchaseOrderID);
CREATE INDEX [IX_GRN_Status] ON [dbo].[GoodsReceiptNotes] (GRNStatus);
CREATE INDEX [IX_GRN_SupplierID] ON [dbo].[GoodsReceiptNotes] (SupplierID);
CREATE UNIQUE INDEX [UQ__GoodsRec__8BA9D385DAD30318] ON [dbo].[GoodsReceiptNotes] (GRNNumber);
CREATE INDEX [IX_InvBal_ItemID] ON [dbo].[InventoryBalance] (ItemID);
CREATE INDEX [IX_InvBal_WarehouseID] ON [dbo].[InventoryBalance] (WarehouseID);
CREATE UNIQUE INDEX [UQ_InvBalance] ON [dbo].[InventoryBalance] (ItemID, WarehouseID);
CREATE INDEX [IX_InvBatchBal_Batch] ON [dbo].[InventoryBatchBalance] (BatchNumber);
CREATE INDEX [IX_InvBatchBal_Expiry] ON [dbo].[InventoryBatchBalance] (ExpiryDate);
CREATE INDEX [IX_InvBatchBal_ItemID] ON [dbo].[InventoryBatchBalance] (ItemID);
CREATE INDEX [IX_InvBatchBal_Warehouse] ON [dbo].[InventoryBatchBalance] (WarehouseID);
CREATE UNIQUE INDEX [UQ_InvBatchBal] ON [dbo].[InventoryBatchBalance] (ItemID, WarehouseID, BatchNumber);
CREATE INDEX [IX_InvTrans_BatchNumber] ON [dbo].[InventoryTransactions] (BatchNumber);
CREATE INDEX [IX_InvTrans_Date] ON [dbo].[InventoryTransactions] (TransactionDate);
CREATE INDEX [IX_InvTrans_ItemID] ON [dbo].[InventoryTransactions] (ItemID);
CREATE INDEX [IX_InvTrans_SourceDoc] ON [dbo].[InventoryTransactions] (SourceDocType, SourceDocID);
CREATE INDEX [IX_InvTrans_Type] ON [dbo].[InventoryTransactions] (TransactionType);
CREATE INDEX [IX_InvTrans_WarehouseID] ON [dbo].[InventoryTransactions] (WarehouseID);
CREATE UNIQUE INDEX [UQ__ItemCate__371BA9551DEE65F9] ON [dbo].[ItemCategories] (CategoryCode);
CREATE INDEX [IX_Items_CategoryID] ON [dbo].[Items] (CategoryID);
CREATE INDEX [IX_Items_IsActive] ON [dbo].[Items] (IsActive);
CREATE INDEX [IX_Items_ItemType] ON [dbo].[Items] (ItemType);
CREATE UNIQUE INDEX [UQ__Items__3ECC0FEAD234BE2E] ON [dbo].[Items] (ItemCode);
CREATE UNIQUE INDEX [UQ__JobTitle__CA812E8561FE7041] ON [dbo].[JobTitles] (JobTitleCode);
CREATE INDEX [IX_JE_Date] ON [dbo].[JournalEntries] (JournalDate);
CREATE INDEX [IX_JE_FiscalYear] ON [dbo].[JournalEntries] (FiscalYearID);
CREATE INDEX [IX_JE_Period] ON [dbo].[JournalEntries] (PeriodID);
CREATE INDEX [IX_JE_SourceDoc] ON [dbo].[JournalEntries] (SourceDocType, SourceDocID);
CREATE INDEX [IX_JE_Status] ON [dbo].[JournalEntries] (JournalStatus);
CREATE INDEX [IX_JE_Type] ON [dbo].[JournalEntries] (JournalType);
CREATE UNIQUE INDEX [UQ__JournalE__B6972335574DA06E] ON [dbo].[JournalEntries] (JournalNumber);
CREATE INDEX [IX_JEDet_AccountID] ON [dbo].[JournalEntryDetails] (AccountID);
CREATE INDEX [IX_JEDet_CostCenter] ON [dbo].[JournalEntryDetails] (CostCenterID);
CREATE INDEX [IX_JEDet_Entity] ON [dbo].[JournalEntryDetails] (EntityType, EntityID);
CREATE INDEX [IX_JEDet_JournalID] ON [dbo].[JournalEntryDetails] (JournalID);
CREATE UNIQUE INDEX [UQ_JEDetails] ON [dbo].[JournalEntryDetails] (JournalID, LineNumber);
CREATE INDEX [IX_LeaveReq_Dates] ON [dbo].[LeaveRequests] (StartDate, EndDate);
CREATE INDEX [IX_LeaveReq_EmpID] ON [dbo].[LeaveRequests] (EmployeeID);
CREATE INDEX [IX_LeaveReq_Status] ON [dbo].[LeaveRequests] (RequestStatus);
CREATE UNIQUE INDEX [UQ__LeaveReq__9ADA6BE086E1EC2C] ON [dbo].[LeaveRequests] (RequestNumber);
CREATE UNIQUE INDEX [UQ__LeaveTyp__A264FAEE626EB183] ON [dbo].[LeaveTypes] (LeaveTypeCode);
CREATE INDEX [IX_LoanInst_DueDate] ON [dbo].[LoanInstallments] (DueDate);
CREATE INDEX [IX_LoanInst_LoanID] ON [dbo].[LoanInstallments] (LoanID);
CREATE UNIQUE INDEX [UQ_LoanInstallment] ON [dbo].[LoanInstallments] (LoanID, InstallmentNumber);
CREATE INDEX [IX_LoginHist_Status] ON [dbo].[LoginHistory] (LoginStatus);
CREATE INDEX [IX_LoginHist_Time] ON [dbo].[LoginHistory] (LoginTime);
CREATE INDEX [IX_LoginHist_UserID] ON [dbo].[LoginHistory] (UserID);
CREATE INDEX [IX_MIDet_IssueID] ON [dbo].[MaterialIssueDetails] (IssueID);
CREATE INDEX [IX_MIDet_ItemID] ON [dbo].[MaterialIssueDetails] (ItemID);
CREATE UNIQUE INDEX [UQ_MIDetails] ON [dbo].[MaterialIssueDetails] (IssueID, LineNumber);
CREATE INDEX [IX_MINote_Date] ON [dbo].[MaterialIssueNotes] (IssueDate);
CREATE INDEX [IX_MINote_ProdOrdID] ON [dbo].[MaterialIssueNotes] (ProductionOrderID);
CREATE INDEX [IX_MINote_Status] ON [dbo].[MaterialIssueNotes] (IssueStatus);
CREATE UNIQUE INDEX [UQ__Material__5703F26CAB2B24A1] ON [dbo].[MaterialIssueNotes] (IssueNumber);
CREATE INDEX [IX_Notif_Date] ON [dbo].[Notifications] (NotificationDate);
CREATE INDEX [IX_Notif_TargetRole] ON [dbo].[Notifications] (TargetRoleID, IsRead);
CREATE INDEX [IX_Notif_TargetUser] ON [dbo].[Notifications] (TargetUserID, IsRead);
CREATE INDEX [IX_Notif_Type] ON [dbo].[Notifications] (NotificationType);
CREATE UNIQUE INDEX [UQ__NumberSe__83FAC6E0BAD88711] ON [dbo].[NumberSequences] (SequenceCode);
CREATE INDEX [IX_OvExp_AccountID] ON [dbo].[OverheadExpenses] (AccountID);
CREATE INDEX [IX_OvExp_PeriodID] ON [dbo].[OverheadExpenses] (PeriodID);
CREATE INDEX [IX_PVAlloc_InvoiceID] ON [dbo].[PaymentAllocations] (InvoiceID);
CREATE INDEX [IX_PVAlloc_PaymentID] ON [dbo].[PaymentAllocations] (PaymentID);
CREATE UNIQUE INDEX [UQ__PaymentT__675CC10D6B48A340] ON [dbo].[PaymentTerms] (TermCode);
CREATE INDEX [IX_PV_Date] ON [dbo].[PaymentVouchers] (PaymentDate);
CREATE INDEX [IX_PV_Entity] ON [dbo].[PaymentVouchers] (EntityType, EntityID);
CREATE INDEX [IX_PV_Status] ON [dbo].[PaymentVouchers] (PaymentStatus);
CREATE UNIQUE INDEX [UQ__PaymentV__E2C1723B974BADA9] ON [dbo].[PaymentVouchers] (PaymentNumber);
CREATE INDEX [IX_Payroll_Month] ON [dbo].[Payroll] (PayrollMonth);
CREATE INDEX [IX_Payroll_Status] ON [dbo].[Payroll] (PayrollStatus);
CREATE UNIQUE INDEX [UQ__Payroll__D1C5B2C1F49F668F] ON [dbo].[Payroll] (PayrollNumber);
CREATE INDEX [IX_PayDet_EmpID] ON [dbo].[PayrollDetails] (EmployeeID);
CREATE INDEX [IX_PayDet_PayrollID] ON [dbo].[PayrollDetails] (PayrollID);
CREATE UNIQUE INDEX [UQ_PayrollDetail] ON [dbo].[PayrollDetails] (PayrollID, EmployeeID);
CREATE INDEX [IX_PriceListDet_ItemID] ON [dbo].[PriceListDetails] (ItemID);
CREATE INDEX [IX_PriceListDet_PriceListID] ON [dbo].[PriceListDetails] (PriceListID);
CREATE UNIQUE INDEX [UQ__PriceLis__8C90CEBE1DABCF5E] ON [dbo].[PriceLists] (PriceListCode);
CREATE INDEX [IX_PCC_ItemID] ON [dbo].[ProductCostCards] (ItemID);
CREATE INDEX [IX_PCC_PeriodID] ON [dbo].[ProductCostCards] (PeriodID);
CREATE INDEX [IX_PBatch_Date] ON [dbo].[ProductionBatches] (BatchDate);
CREATE INDEX [IX_PBatch_ExpiryDate] ON [dbo].[ProductionBatches] (ExpiryDate);
CREATE INDEX [IX_PBatch_ProdOrdID] ON [dbo].[ProductionBatches] (ProductionOrderID);
CREATE INDEX [IX_PBatch_ProductID] ON [dbo].[ProductionBatches] (ProductItemID);
CREATE INDEX [IX_PBatch_Status] ON [dbo].[ProductionBatches] (BatchStatus);
CREATE UNIQUE INDEX [UQ__Producti__F869ED6DF7823BA8] ON [dbo].[ProductionBatches] (BatchNumber);
CREATE INDEX [IX_PBStage_BatchID] ON [dbo].[ProductionBatchStages] (BatchID);
CREATE UNIQUE INDEX [UQ_BatchStages] ON [dbo].[ProductionBatchStages] (BatchID, StageOrder);
CREATE INDEX [IX_POMatl_ItemID] ON [dbo].[ProductionOrderMaterials] (ItemID);
CREATE INDEX [IX_POMatl_ProdOrdID] ON [dbo].[ProductionOrderMaterials] (ProductionOrderID);
CREATE UNIQUE INDEX [UQ_POMaterials] ON [dbo].[ProductionOrderMaterials] (ProductionOrderID, LineNumber);
CREATE INDEX [IX_ProdOrd_BOMID] ON [dbo].[ProductionOrders] (BOMID);
CREATE INDEX [IX_ProdOrd_Date] ON [dbo].[ProductionOrders] (OrderDate);
CREATE INDEX [IX_ProdOrd_Priority] ON [dbo].[ProductionOrders] (OrderPriority);
CREATE INDEX [IX_ProdOrd_ProductID] ON [dbo].[ProductionOrders] (ProductItemID);
CREATE INDEX [IX_ProdOrd_Status] ON [dbo].[ProductionOrders] (OrderStatus);
CREATE UNIQUE INDEX [UQ__Producti__CAC5E743A0DC4A43] ON [dbo].[ProductionOrders] (OrderNumber);
CREATE UNIQUE INDEX [UQ__Producti__7BFA4BE3E39CAC47] ON [dbo].[ProductionStages] (StageCode);
CREATE INDEX [IX_Holiday_Date] ON [dbo].[PublicHolidays] (HolidayDate);
CREATE INDEX [IX_PInvDet_InvoiceID] ON [dbo].[PurchaseInvoiceDetails] (InvoiceID);
CREATE INDEX [IX_PInvDet_ItemID] ON [dbo].[PurchaseInvoiceDetails] (ItemID);
CREATE UNIQUE INDEX [UQ_PInvDetails] ON [dbo].[PurchaseInvoiceDetails] (InvoiceID, LineNumber);
CREATE INDEX [IX_PInv_Date] ON [dbo].[PurchaseInvoices] (InvoiceDate);
CREATE INDEX [IX_PInv_DueDate] ON [dbo].[PurchaseInvoices] (DueDate);
CREATE INDEX [IX_PInv_PayStatus] ON [dbo].[PurchaseInvoices] (PaymentStatus);
CREATE INDEX [IX_PInv_POID] ON [dbo].[PurchaseInvoices] (PurchaseOrderID);
CREATE INDEX [IX_PInv_Status] ON [dbo].[PurchaseInvoices] (InvoiceStatus);
CREATE INDEX [IX_PInv_SupplierID] ON [dbo].[PurchaseInvoices] (SupplierID);
CREATE UNIQUE INDEX [UQ__Purchase__D776E9811889F7C8] ON [dbo].[PurchaseInvoices] (InvoiceNumber);
CREATE INDEX [IX_PODet_ItemID] ON [dbo].[PurchaseOrderDetails] (ItemID);
CREATE INDEX [IX_PODet_POID] ON [dbo].[PurchaseOrderDetails] (PurchaseOrderID);
CREATE INDEX [IX_PODetails_RequestDetailID] ON [dbo].[PurchaseOrderDetails] (RequestDetailID);
CREATE UNIQUE INDEX [UQ_PODetails] ON [dbo].[PurchaseOrderDetails] (PurchaseOrderID, LineNumber);
CREATE INDEX [IX_PO_Date] ON [dbo].[PurchaseOrders] (PODate);
CREATE INDEX [IX_PO_POSource] ON [dbo].[PurchaseOrders] (POSource);
CREATE INDEX [IX_PO_POStatus_Date] ON [dbo].[PurchaseOrders] (POStatus, PODate);
CREATE INDEX [IX_PO_RequestID] ON [dbo].[PurchaseOrders] (RequestID);
CREATE INDEX [IX_PO_Status] ON [dbo].[PurchaseOrders] (POStatus);
CREATE INDEX [IX_PO_SupplierID] ON [dbo].[PurchaseOrders] (SupplierID);
CREATE UNIQUE INDEX [UQ__Purchase__69B9A8414DDAE0CD] ON [dbo].[PurchaseOrders] (PONumber);
CREATE INDEX [IX_PRDet_ItemID] ON [dbo].[PurchaseRequestDetails] (ItemID);
CREATE INDEX [IX_PRDet_RequestID] ON [dbo].[PurchaseRequestDetails] (RequestID);
CREATE UNIQUE INDEX [UQ_PRDetails] ON [dbo].[PurchaseRequestDetails] (RequestID, LineNumber);
CREATE INDEX [IX_PR_Date] ON [dbo].[PurchaseRequests] (RequestDate);
CREATE INDEX [IX_PR_RequestedBy] ON [dbo].[PurchaseRequests] (RequestedBy);
CREATE INDEX [IX_PR_Status] ON [dbo].[PurchaseRequests] (RequestStatus);
CREATE UNIQUE INDEX [UQ__Purchase__9ADA6BE04D9BF9C5] ON [dbo].[PurchaseRequests] (RequestNumber);
CREATE INDEX [IX_PRetDet_ItemID] ON [dbo].[PurchaseReturnDetails] (ItemID);
CREATE INDEX [IX_PRetDet_ReturnID] ON [dbo].[PurchaseReturnDetails] (ReturnID);
CREATE UNIQUE INDEX [UQ_PRetDetails] ON [dbo].[PurchaseReturnDetails] (ReturnID, LineNumber);
CREATE INDEX [IX_PRet_Date] ON [dbo].[PurchaseReturns] (ReturnDate);
CREATE INDEX [IX_PRet_InvoiceID] ON [dbo].[PurchaseReturns] (InvoiceID);
CREATE INDEX [IX_PRet_Status] ON [dbo].[PurchaseReturns] (ReturnStatus);
CREATE INDEX [IX_PRet_SupplierID] ON [dbo].[PurchaseReturns] (SupplierID);
CREATE UNIQUE INDEX [UQ__Purchase__2739D7BB74BBF5E3] ON [dbo].[PurchaseReturns] (ReturnNumber);
CREATE INDEX [IX_RVAlloc_InvoiceID] ON [dbo].[ReceiptAllocations] (InvoiceID);
CREATE INDEX [IX_RVAlloc_ReceiptID] ON [dbo].[ReceiptAllocations] (ReceiptID);
CREATE INDEX [IX_RV_Date] ON [dbo].[ReceiptVouchers] (ReceiptDate);
CREATE INDEX [IX_RV_Entity] ON [dbo].[ReceiptVouchers] (EntityType, EntityID);
CREATE INDEX [IX_RV_Status] ON [dbo].[ReceiptVouchers] (ReceiptStatus);
CREATE UNIQUE INDEX [UQ__ReceiptV__C08AFDABE426AF05] ON [dbo].[ReceiptVouchers] (ReceiptNumber);
CREATE INDEX [IX_RolePerm_ModuleID] ON [dbo].[RolePermissions] (ModuleID);
CREATE INDEX [IX_RolePerm_RoleID] ON [dbo].[RolePermissions] (RoleID);
CREATE UNIQUE INDEX [UQ_RolePermission] ON [dbo].[RolePermissions] (RoleID, ModuleID);
CREATE INDEX [IX_SInvDet_InvoiceID] ON [dbo].[SalesInvoiceDetails] (InvoiceID);
CREATE INDEX [IX_SInvDet_ItemID] ON [dbo].[SalesInvoiceDetails] (ItemID);
CREATE UNIQUE INDEX [UQ_SInvDetails] ON [dbo].[SalesInvoiceDetails] (InvoiceID, LineNumber);
CREATE INDEX [IX_SInv_CustomerID] ON [dbo].[SalesInvoices] (CustomerID);
CREATE INDEX [IX_SInv_Date] ON [dbo].[SalesInvoices] (InvoiceDate);
CREATE INDEX [IX_SInv_DueDate] ON [dbo].[SalesInvoices] (DueDate);
CREATE INDEX [IX_SInv_PayStatus] ON [dbo].[SalesInvoices] (PaymentStatus);
CREATE INDEX [IX_SInv_SalesRepID] ON [dbo].[SalesInvoices] (SalesRepID);
CREATE INDEX [IX_SInv_SOID] ON [dbo].[SalesInvoices] (SalesOrderID);
CREATE INDEX [IX_SInv_Status] ON [dbo].[SalesInvoices] (InvoiceStatus);
CREATE UNIQUE INDEX [UQ__SalesInv__D776E9819824AECE] ON [dbo].[SalesInvoices] (InvoiceNumber);
CREATE INDEX [IX_SODet_ItemID] ON [dbo].[SalesOrderDetails] (ItemID);
CREATE INDEX [IX_SODet_SOID] ON [dbo].[SalesOrderDetails] (SalesOrderID);
CREATE UNIQUE INDEX [UQ_SODetails] ON [dbo].[SalesOrderDetails] (SalesOrderID, LineNumber);
CREATE INDEX [IX_SO_CustomerID] ON [dbo].[SalesOrders] (CustomerID);
CREATE INDEX [IX_SO_Date] ON [dbo].[SalesOrders] (SODate);
CREATE INDEX [IX_SO_SalesRepID] ON [dbo].[SalesOrders] (SalesRepID);
CREATE INDEX [IX_SO_Status] ON [dbo].[SalesOrders] (SOStatus);
CREATE UNIQUE INDEX [UQ__SalesOrd__E55218C09756D08A] ON [dbo].[SalesOrders] (SONumber);
CREATE INDEX [IX_SQuotDet_ItemID] ON [dbo].[SalesQuotationDetails] (ItemID);
CREATE INDEX [IX_SQuotDet_QuotationID] ON [dbo].[SalesQuotationDetails] (QuotationID);
CREATE UNIQUE INDEX [UQ_SQuotDetails] ON [dbo].[SalesQuotationDetails] (QuotationID, LineNumber);
CREATE INDEX [IX_SQuot_CustomerID] ON [dbo].[SalesQuotations] (CustomerID);
CREATE INDEX [IX_SQuot_Date] ON [dbo].[SalesQuotations] (QuotationDate);
CREATE INDEX [IX_SQuot_SalesRepID] ON [dbo].[SalesQuotations] (SalesRepID);
CREATE INDEX [IX_SQuot_Status] ON [dbo].[SalesQuotations] (QuotationStatus);
CREATE UNIQUE INDEX [UQ__SalesQuo__F3A63C4A2244E25B] ON [dbo].[SalesQuotations] (QuotationNumber);
CREATE UNIQUE INDEX [UQ__SalesRep__2E3224D2CC24B7AF] ON [dbo].[SalesRepresentatives] (RepCode);
CREATE UNIQUE INDEX [UQ__SalesRep__7AD04FF01EAFD8E8] ON [dbo].[SalesRepresentatives] (EmployeeID);
CREATE INDEX [IX_SRetDet_ItemID] ON [dbo].[SalesReturnDetails] (ItemID);
CREATE INDEX [IX_SRetDet_ReturnID] ON [dbo].[SalesReturnDetails] (ReturnID);
CREATE UNIQUE INDEX [UQ_SRetDetails] ON [dbo].[SalesReturnDetails] (ReturnID, LineNumber);
CREATE INDEX [IX_SRet_CustomerID] ON [dbo].[SalesReturns] (CustomerID);
CREATE INDEX [IX_SRet_Date] ON [dbo].[SalesReturns] (ReturnDate);
CREATE INDEX [IX_SRet_InvoiceID] ON [dbo].[SalesReturns] (InvoiceID);
CREATE INDEX [IX_SRet_Status] ON [dbo].[SalesReturns] (ReturnStatus);
CREATE UNIQUE INDEX [UQ__SalesRet__2739D7BB6B2A0F45] ON [dbo].[SalesReturns] (ReturnNumber);
CREATE INDEX [IX_StockCountDet_CountID] ON [dbo].[StockCountDetails] (CountID);
CREATE INDEX [IX_StockCountDet_ItemID] ON [dbo].[StockCountDetails] (ItemID);
CREATE UNIQUE INDEX [UQ_StockCountDet] ON [dbo].[StockCountDetails] (CountID, LineNumber);
CREATE INDEX [IX_StockCount_Date] ON [dbo].[StockCounts] (CountDate);
CREATE INDEX [IX_StockCount_Status] ON [dbo].[StockCounts] (CountStatus);
CREATE INDEX [IX_StockCount_Warehouse] ON [dbo].[StockCounts] (WarehouseID);
CREATE UNIQUE INDEX [UQ__StockCou__BC624C36E8D8D132] ON [dbo].[StockCounts] (CountNumber);
CREATE INDEX [IX_StockTransDet_ItemID] ON [dbo].[StockTransferDetails] (ItemID);
CREATE INDEX [IX_StockTransDet_TransferID] ON [dbo].[StockTransferDetails] (TransferID);
CREATE UNIQUE INDEX [UQ_StockTransDet] ON [dbo].[StockTransferDetails] (TransferID, LineNumber);
CREATE INDEX [IX_StockTrans_Date] ON [dbo].[StockTransfers] (TransferDate);
CREATE INDEX [IX_StockTrans_Status] ON [dbo].[StockTransfers] (TransferStatus);
CREATE UNIQUE INDEX [UQ__StockTra__02A4FA0DC7A13FDC] ON [dbo].[StockTransfers] (TransferNumber);
CREATE INDEX [IX_SupplierContacts_SupplierID] ON [dbo].[SupplierContacts] (SupplierID);
CREATE INDEX [IX_SupplierItems_ItemID] ON [dbo].[SupplierItems] (ItemID);
CREATE UNIQUE INDEX [UQ_SupplierItems] ON [dbo].[SupplierItems] (SupplierID, ItemID);
CREATE INDEX [IX_Suppliers_IsActive] ON [dbo].[Suppliers] (IsActive);
CREATE INDEX [IX_Suppliers_Type] ON [dbo].[Suppliers] (SupplierType);
CREATE UNIQUE INDEX [UQ__Supplier__44BE981B8AC28A2E] ON [dbo].[Suppliers] (SupplierCode);
CREATE UNIQUE INDEX [UQ__SystemMo__EB27D433B7916CDE] ON [dbo].[SystemModules] (ModuleCode);
CREATE UNIQUE INDEX [UQ__SystemSe__01E719ADFC0CF9BE] ON [dbo].[SystemSettings] (SettingKey);
CREATE INDEX [IX_Users_EmployeeID] ON [dbo].[SystemUsers] (EmployeeID);
CREATE INDEX [IX_Users_IsActive] ON [dbo].[SystemUsers] (IsActive);
CREATE INDEX [IX_Users_RoleID] ON [dbo].[SystemUsers] (RoleID);
CREATE UNIQUE INDEX [UQ__SystemUs__536C85E4144FB859] ON [dbo].[SystemUsers] (Username);
CREATE UNIQUE INDEX [UQ_UnitConversions] ON [dbo].[UnitConversions] (FromUnitID, ToUnitID);
CREATE UNIQUE INDEX [UQ__Units__0665E6D97507CFC5] ON [dbo].[Units] (UnitCode);
CREATE INDEX [IX_UserPerm_UserID] ON [dbo].[UserPermissions] (UserID);
CREATE UNIQUE INDEX [UQ_UserPermission] ON [dbo].[UserPermissions] (UserID, ModuleID);
CREATE UNIQUE INDEX [UQ_UserPref] ON [dbo].[UserPreferences] (UserID, PreferenceKey);
CREATE UNIQUE INDEX [UQ__UserRole__D62CB59C86F8A128] ON [dbo].[UserRoles] (RoleCode);
CREATE UNIQUE INDEX [UQ__Warehous__1686A05602274F54] ON [dbo].[Warehouses] (WarehouseCode);
GO
