using System.ComponentModel.DataAnnotations;

namespace OperatorConsole.Models;

public class CreateLinkViewModel
{
    [Required]
    [Url(ErrorMessage = "Enter a full URL starting with http:// or https://")]
    [StringLength(2048)]
    [Display(Name = "Destination URL")]
    public string Url { get; set; } = "";

    [StringLength(16, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Letters, digits, '-' and '_' only.")]
    [Display(Name = "Custom code (optional)")]
    public string? CustomCode { get; set; }
}
