using Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        public EmailSender(HttpClient http, string apiKey)
        {
            _http = http;
            _apiKey = apiKey;
        }
        public async Task SendEmailAsync(string toEmail)
        {
            Console.WriteLine($"Sending email to: {toEmail}");

            var payload = new
            {
                sender = new { email = "info@yourdomain.com", name = "Your App" },
                to = new[] { new { email = toEmail } },
                subject = "Bilboard verification email",
                htmlContent = "Hi this is a verification email for your Bilboard account."
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            req.Headers.Add("api-key", _apiKey);

            var res = await _http.SendAsync(req);
            res.EnsureSuccessStatusCode();
        }
    }
}
