using Bilboard.Application.Services;
using Microsoft.AspNetCore.Components;

namespace Bilboard.Components.Services
{
    public partial class AuthService
    {
        private readonly IJwtAuthService _jwtAuthService;
        private readonly NavigationManager _navigationManager;

        public AuthService(IJwtAuthService jwtAuthService, NavigationManager navigationManager)
        {
            _jwtAuthService = jwtAuthService;
            _navigationManager = navigationManager;
        }

        public async Task LogoutAsync()
        {
            // Remove JWT token from localStorage
            await _jwtAuthService.RemoveTokenAsync();

            // Navigate to home page
            _navigationManager.NavigateTo("/");
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            return await _jwtAuthService.IsAuthenticatedAsync();
        }

        public async Task<string> GetTokenAsync()
        {
            return await _jwtAuthService.GetTokenAsync();
        }
    }
}
