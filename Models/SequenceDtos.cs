namespace YKCoatings.Models
{
    /// <summary>
    /// Projection of dbo.NumberSequences for the settings-gated administration screen.
    /// CurrentNumber and LastResetYear are shown but are not editable after creation.
    /// </summary>
    public sealed class NumberSequenceDto
    {
        public int SequenceID { get; set; }
        public string SequenceCode { get; set; } = string.Empty;
        public string SequenceName { get; set; } = string.Empty;
        public string? Prefix { get; set; }
        public int? CurrentNumber { get; set; }
        public int? NumberLength { get; set; }
        public bool IncludeYear { get; set; }
        public bool ResetYearly { get; set; }
        public int? LastResetYear { get; set; }
        public bool IsActive { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? NextNumberPreview { get; set; }
    }
}
