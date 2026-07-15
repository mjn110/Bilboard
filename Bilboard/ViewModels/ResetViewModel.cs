using System.ComponentModel.DataAnnotations;

namespace Bilboard.ViewModels
{
    public class ResetViewModel
    {
        [EmailAddress]
        public string? Email { get; set; }
        public string? Token { get; set; }
        [Required]
        public string Password { get; set; }
        [Required]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }
}
