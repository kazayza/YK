namespace YKCoatings.Models
{
    /// <summary>
    /// Whitelisted, read-only company identity fields used by printable documents.
    /// </summary>
    public sealed class PrintCompanyProfileDto
    {
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyNameEn { get; set; } = string.Empty;
        public string CompanyAddress { get; set; } = string.Empty;
        public string CompanyPhone { get; set; } = string.Empty;
        public string CompanyEmail { get; set; } = string.Empty;
        public string TaxNumber { get; set; } = string.Empty;
        public string CommercialRegister { get; set; } = string.Empty;
        public string CompanyLogo { get; set; } = "/images/yk-logo-gold.png";
    }
}
