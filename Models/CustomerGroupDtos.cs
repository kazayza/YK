namespace YKCoatings.Models;

public sealed class CustomerGroupDto
{
    public int GroupID { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string GroupNameAr { get; set; } = string.Empty;
    public string? GroupNameEn { get; set; }
    public decimal DiscountPercent { get; set; }
    public int? PriceListID { get; set; }
    public string? PriceListName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedDate { get; set; }
    public int CustomerCount { get; set; }
}
