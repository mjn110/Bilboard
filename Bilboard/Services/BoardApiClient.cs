using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTO.Boards;
using Bilboard.Application.Services;

namespace Bilboard.Services
{
    public interface IBoardApiClient
    {
        Task<List<GetBoardDto>> GetBoardsAsync();
        Task<GetBoardDto?> GetBoardAsync(string boardId);
        Task<BoardResponseDto> CreateBoardAsync(CreateBoardDto board);
        Task<BoardResponseDto?> UpdateBoardAsync(string boardId, UpdateBoardDto board);
    }

    /// <summary>
    /// Calls the board endpoints of the API with the signed-in user's JWT, which is how the API
    /// knows whose boards to read and write. Throws <see cref="UnauthorizedAccessException"/>
    /// when the token is missing or has expired, so pages can send the user to sign in.
    /// </summary>
    public class BoardApiClient : IBoardApiClient
    {
        private readonly HttpClient _http;
        private readonly IJwtAuthService _jwtAuthService;

        public BoardApiClient(HttpClient http, IJwtAuthService jwtAuthService)
        {
            _http = http;
            _jwtAuthService = jwtAuthService;
        }

        public async Task<List<GetBoardDto>> GetBoardsAsync()
        {
            using var response = await SendAsync(HttpMethod.Get, "/api/Board/GetBoards");
            await EnsureSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<List<GetBoardDto>>() ?? new List<GetBoardDto>();
        }

        // Null when the board does not exist or belongs to another user.
        public async Task<GetBoardDto?> GetBoardAsync(string boardId)
        {
            using var response = await SendAsync(HttpMethod.Get, $"/api/Board/{Uri.EscapeDataString(boardId)}");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<GetBoardDto>();
        }

        public async Task<BoardResponseDto> CreateBoardAsync(CreateBoardDto board)
        {
            using var response = await SendAsync(HttpMethod.Post, "/api/Board", board);
            await EnsureSuccessAsync(response);
            return (await response.Content.ReadFromJsonAsync<BoardResponseDto>())!;
        }

        // Null when the board does not exist or belongs to another user.
        public async Task<BoardResponseDto?> UpdateBoardAsync(string boardId, UpdateBoardDto board)
        {
            using var response = await SendAsync(HttpMethod.Put, $"/api/Board/{Uri.EscapeDataString(boardId)}", board);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<BoardResponseDto>();
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, object? body = null)
        {
            using var request = new HttpRequestMessage(method, uri);

            var token = await _jwtAuthService.GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            if (body != null)
            {
                request.Content = JsonContent.Create(body);
            }

            var response = await _http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new UnauthorizedAccessException("The session has expired. Please sign in again.");
            }

            return response;
        }

        // Carries the status code and what the API said into the message, so a page can show why
        // a board could not be saved instead of just that it could not.
        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string body = (await response.Content.ReadAsStringAsync()).Trim();
            if (body.Length > 200)
            {
                body = body[..200] + "...";
            }

            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim());
        }
    }
}
