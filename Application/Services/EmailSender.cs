using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        public EmailSender(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["Brevo:ApiKey"];
        }
        public async Task SendEmailAsync(string toEmail)
        {
            Console.WriteLine($"Sending email to: {toEmail}");

            var payload = new
            {
                sender = new { email = "verify@bilboard.online", name = "Bilboard" },
                to = new[] { new { email = toEmail } },
                subject = "Welcome!",
                htmlContent = "<h1>Welcome to our app</h1><p>Thanks for signing up.</p>"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Add("api-key", _apiKey);
            Console.WriteLine($"Sending email to: {toEmail}");
            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Console.WriteLine($"Email sent successfully to: {toEmail}");
        }
    }
}
