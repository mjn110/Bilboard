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

        public AccountController(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
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
    }
}
