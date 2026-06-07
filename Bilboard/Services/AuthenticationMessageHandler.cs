namespace Bilboard.Services
{
    public class AuthenticationMessageHandler : DelegatingHandler
    {
        private readonly IServiceProvider _serviceProvider;

        public AuthenticationMessageHandler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var jwtAuthService = _serviceProvider.GetRequiredService<IAsyncJwtAuthService>();
            var token = await jwtAuthService.GetTokenAsync();

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    public interface IAsyncJwtAuthService
    {
        Task SaveTokenAsync(string token);
        Task<string> GetTokenAsync();
        Task RemoveTokenAsync();
        Task<bool> IsAuthenticatedAsync();
    }
}
