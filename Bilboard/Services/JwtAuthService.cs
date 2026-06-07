using Microsoft.JSInterop;

namespace Bilboard.Application.Services
{
    public interface IJwtAuthService
    {
        Task SaveTokenAsync(string token);
        Task<string> GetTokenAsync();
        Task RemoveTokenAsync();
        Task<bool> IsAuthenticatedAsync();
        Task SignOutAsync(HttpClient httpClient);
    }

    public class JwtAuthService : IJwtAuthService
    {
        private const string TokenKey = "jwtToken";
        private readonly IJSRuntime _js;

        public JwtAuthService(IJSRuntime jsRuntime)
        {
            _js = jsRuntime;
        }

        public async Task SaveTokenAsync(string token)
        {
            await _js.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
        }

        public async Task<string> GetTokenAsync()
        {
            var token = await _js.InvokeAsync<string>("localStorage.getItem", TokenKey);
            return token ?? string.Empty;
        }

        public async Task RemoveTokenAsync()
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            var token = await GetTokenAsync();
            return !string.IsNullOrEmpty(token);
        }

        public async Task SignOutAsync(HttpClient httpClient)
        {
            try
            {
                // Call the API SignOut endpoint
                var response = await httpClient.PostAsync("/api/Account/SignOut", null);

                // Remove token from localStorage regardless of API response
                await RemoveTokenAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignOut error: {ex.Message}");
                // Still remove token even if API call fails
                await RemoveTokenAsync();
            }
        }
    }
}
