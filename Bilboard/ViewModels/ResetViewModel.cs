using System.ComponentModel.DataAnnotations;

namespace Bilboard.ViewModels
{
    public class ResetViewModel
    {
        [Required]
        public string Password { get; set; }
        [Required]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }
}
