using System.ComponentModel.DataAnnotations;

namespace OperatorConsole.Models;

public class LoginViewModel
{
    [Required]
    [StringLength(64)]
    public string Username { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [StringLength(128)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
