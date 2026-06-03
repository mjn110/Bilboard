using Application.DTO.Account;
using Bilboard.ViewModels;
using Infrastructure.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {

        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        // POST api/<AccountController>
        [HttpPost("SignIn")]
        public async Task<IActionResult> SignIn([FromBody] SignInViewModel model)
        {
            Console.WriteLine("Entered SignIn api");
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, isPersistent: false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    return Ok();
                }
                else
                {
                    Console.WriteLine("SignIn failed: " + result.Succeeded);
                    return Unauthorized();
                }
            }
            return BadRequest(ModelState);
        }

        [HttpPost("SignUp")]
        public async Task<IActionResult> SignUp([FromBody] SignUpViewModel model)
        {
            Console.WriteLine("Entered SignUp api");
            var user = new ApplicationUser
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                UserName = model.Email,
                Email = model.Email,
            };
            await _userManager.CreateAsync(user,model.Password);
            return Ok();
        }
    }
}
