using YKCoatings.Components;
using YKCoatings.Services;

var builder = WebApplication.CreateBuilder(args);
// Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// === Services ===
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<UnitService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<PriceListService>();
builder.Services.AddScoped<SettingService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PurchaseRequestService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<GoodsReceiptService>();
builder.Services.AddScoped<PurchaseInvoiceService>();
builder.Services.AddScoped<PurchaseReturnService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<LoginHistoryService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<JobTitleService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<LeaveTypeService>();
builder.Services.AddScoped<PublicHolidayService>();
builder.Services.AddScoped<LeaveBalanceService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<LeaveRequestService>();
builder.Services.AddScoped<LoanService>();
builder.Services.AddScoped<PenaltyService>();
builder.Services.AddScoped<PayrollService>();
builder.Services.AddScoped<SequenceService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<PaymentTermService>();
builder.Services.AddScoped<BOMService>();
builder.Services.AddScoped<ProductionOrderService>();
builder.Services.AddScoped<MaterialIssueService>();
builder.Services.AddScoped<ProductionBatchService>();
builder.Services.AddScoped<ContractManufacturingService>();
builder.Services.AddScoped<SalesInvoiceService>();
builder.Services.AddScoped<SalesOrderService>();
builder.Services.AddScoped<TreasuryService>();
builder.Services.AddScoped<AccountingService>();

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    context.Items["ClientIP"] = ip;
    await next();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();