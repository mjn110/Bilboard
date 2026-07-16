using Application.DTO.Account;
using Application.Interfaces;
using Application.Services;
using Bilboard.ViewModels;
using Infrastructure.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using Microsoft.ML.Tokenizers;
using Presentation.Services;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IConfiguration _configuration;
        private readonly IEmailSender _emailSender;


        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IJwtTokenService jwtTokenService, IConfiguration configuration, IEmailSender emailSender)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _jwtTokenService = jwtTokenService;
            _configuration = configuration;
            _emailSender = emailSender;
        }

        [HttpPost("SignIn")]
        public async Task<IActionResult> SignIn([FromBody] SignInViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                Console.WriteLine("User not found: " + model.Email);
                return Unauthorized(new { message = "Invalid email or password" });
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                Console.WriteLine("SignIn failed for user: " + model.Email);
                return Unauthorized(new { message = "Invalid email or password" });
            }

            // Generate JWT token
            var token = await _jwtTokenService.GenerateTokenAsync(user);
            var roles = await _userManager.GetRolesAsync(user);

            var response = new SignInResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = roles.ToList()
            };

            Console.WriteLine("Sign in successful for user: " + model.Email);
            return Ok(response);
        }

        [HttpPost("SignUp")]
        public async Task<IActionResult> SignUp([FromBody] SignUpViewModel model)
        {
            Console.WriteLine("Entered SignUp api");
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Check if user already exists
            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                return BadRequest(new { message = "User with this email already exists" });
            }

            var user = new ApplicationUser
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                UserName = model.Email,
                Email = model.Email,
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(e => e.Description).ToList();
                return BadRequest(new { message = "Failed to create user", errors });
            }
            Console.WriteLine("User created successfully: " + model.Email);
            // Generate JWT token for new user
            var token = await _jwtTokenService.GenerateTokenAsync(user);
            var roles = await _userManager.GetRolesAsync(user);

            var confirmationToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmationToken));
            var clientBaseUrl = _configuration["ClientSettings:BaseUrl"] ?? "http://localhost:3000";
            var confirmationLink = $"{clientBaseUrl}/confirm?userId={user.Id}&token={encodedToken}";

            var emailBody = $"<p>Welcome! Please confirm your email by clicking " +
                             $"<a href='{confirmationLink}'>this link</a>.</p>";

            await _emailSender.SendEmailAsync(user.Email, "Welcome to Bilboard!", emailBody);

            var response = new SignInResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = roles.ToList()
            };

            Console.WriteLine("User created successfully: " + model.Email);
            return Ok();
        }

        [Authorize]
        [HttpPost("SignOut")]
        public async Task<IActionResult> SignOut()
        {
            Console.WriteLine("Entered SignOut api");
            try
            {
                // Get the current user
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Unauthorized(new { message = "User not found" });
                }

                // Sign out the user (clear Identity cookie if any)
                await _signInManager.SignOutAsync();

                Console.WriteLine($"User signed out successfully: {user.Email}");
                return Ok(new { message = "Signed out successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignOut error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred during sign out" });
            }
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            byte[] decodedBytes;

            if (user == null)
                return BadRequest(new { message = "Invalid confirmation request. User not found." });

            try
            {
                decodedBytes = WebEncoders.Base64UrlDecode(token);
            }
            catch (FormatException)
            {
                return BadRequest(new { message = "Invalid confirmation token format." });
            }

            var decodedToken = Encoding.UTF8.GetString(decodedBytes);
            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);

            if (!result.Succeeded)
                return BadRequest(new { message = "Email confirmation failed. The link may be invalid or expired." });

            Console.WriteLine($"Email confirmed successfully for user: {user.Email}");
            return Ok(new { message = "Email confirmed successfully. You can now log in." });
        }

        [HttpPost("GetAuthenticationState")]
        public async Task<IActionResult> GetAuthenticationStateAsync([FromBody] string? token) // Removed async as it is not needed
        {
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            // 2. Parse claims from the JWT
            var claims = ParseClaimsFromJwt(token);
            var identity = new ClaimsIdentity(claims, "jwt");
            var user = new ClaimsPrincipal(identity);

            return Ok(user.Identity.IsAuthenticated);
        }

        [HttpPost("ResetPasswordRequest")]
        public async Task<IActionResult> ResetPasswordRequest([FromBody] string email)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return BadRequest(new { message = "User not found" });
            }
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var clientBaseUrl = _configuration["ClientSettings:BaseUrl"] ?? "http://localhost:3000";
            var resetLink = $"{clientBaseUrl}/reset?email={email}&token={encodedToken}";
            var emailBody = $"<p>You requested a password reset. Click " +
                             $"<a href='{resetLink}'>this link</a> to reset your password.</p>";
            await _emailSender.SendEmailAsync(user.Email, "Bilboard Password Reset Request", emailBody);
            return Ok("Password reset link sent to your email.");
        }

        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                Console.WriteLine($"ModelState invalid: {string.Join(", ", errors)}");
                return BadRequest(new { message = "Invalid input", errors });
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                return BadRequest(new { message = "User not found" });
            }

            try
            {
                var decodedTokenBytes = WebEncoders.Base64UrlDecode(model.Token);
                var decodedToken = Encoding.UTF8.GetString(decodedTokenBytes);
                var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.Password);

                if (!result.Succeeded)
                {
                    var errors = result.Errors.Select(e => e.Description).ToList();
                    Console.WriteLine($"Failed to reset password. Errors: {string.Join(", ", errors)}");
                    return BadRequest(new { message = "Failed to reset password", errors });
                }

                return Ok(new { message = "Password reset successful" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during password reset: {ex.Message}");
                return BadRequest(new { message = "Error processing password reset", error = ex.Message });
            }
        }

        // Helper method to decode JWT payload (handles base64 URL encoding)
        private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var payload = jwt.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

            return keyValuePairs.Select(kvp => new Claim(kvp.Key, kvp.Value.ToString()));
        }

        private byte[] ParseBase64WithoutPadding(string base64)
        {
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}
