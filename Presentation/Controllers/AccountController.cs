using Application.DTO.Account;
using Bilboard.ViewModels;
using Infrastructure.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Presentation.Services;

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IJwtTokenService _jwtTokenService;

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IJwtTokenService jwtTokenService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _jwtTokenService = jwtTokenService;
        }

        [HttpPost("SignIn")]
        public async Task<IActionResult> SignIn([FromBody] SignInViewModel model)
        {
            Console.WriteLine("Entered SignIn api");
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

            // Generate JWT token for new user
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

            Console.WriteLine("User created successfully: " + model.Email);
            return Ok(response);
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
    }
}
