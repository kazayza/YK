# Skill: إضافة صفحة (Razor Page) جديدة في YKCoatings

## الهيكل النموذجي
```razor
@page "/widgets"
@using YKCoatings.Services
@using YKCoatings.Models
@inject WidgetService WidgetService
@inject UserSessionService UserSession
@inject PermissionService PermissionService
@inject NavigationManager Navigation
@inject IJSRuntime JS

<PageTitle>الأدوات - واي كي كوتينج</PageTitle>

@if (isLoading)
{
    <div class="d-flex justify-content-center p-5"><div class="spinner-border text-primary"></div></div>
}
else
{
    <div class="card" dir="rtl">
        <div class="card-header d-flex justify-content-between align-items-center">
            <span class="yk-page-title">الأدوات</span>
            @if (PermissionService.Can("SCR_WIDGETS", "CanAdd"))
            {
                <button class="btn btn-primary" @onclick="OpenNew">+ إضافة</button>
            }
        </div>
        <div class="table-responsive">
            <table class="table table-hover align-middle">
                <thead><tr><th>الاسم</th><th>الحالة</th><th></th></tr></thead>
                <tbody>
                @foreach (var w in items)
                {
                    <tr>
                        <td>@w.NameAr</td>
                        <td><span class="badge badge-soft-@(w.IsActive ? "success" : "danger")">@(w.IsActive ? "نشط" : "موقوف")</span></td>
                        <td class="text-nowrap">
                            <button class="btn btn-sm btn-outline-primary" @onclick="@(() => Edit(w))">تعديل</button>
                        </td>
                    </tr>
                }
                </tbody>
            </table>
        </div>
    </div>
}

@code {
    private bool isLoading = true;
    private List<WidgetListDto> items = new();

    protected override async Task OnInitializedAsync()
    {
        if (!UserSession.IsLoggedIn) { Navigation.NavigateTo("/login"); return; }
        try { items = await WidgetService.GetWidgetsAsync(); } finally { isLoading = false; }
    }
    private void OpenNew() => Navigation.NavigateTo("/widget-edit/0");
}
```

## قواعد
- استخدم كلاسات `yk-theme.css` (card/table/btn/badge/alert) بدل كتابة CSS جديدة.
- تحقق من الصلاحية قبل أي زر كتابة.
- `@onclick` بالعربية RTL لا يحتاج dir إضافي داخل `.card[dir=rtl]`.
- للهروب في `<style>` داخل razor: `@@media`, `@@keyframes`, `@@import`.
- أضف المسار في `Helpers/ModuleRouteHelper.cs` + سجل في جدول `SystemModules` (كود `SCR_*`) لظهوره بالقائمة.
