using Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly HttpClient _http;
        public EmailSender(HttpClient http)
        {
            _http = http;
        }
        public async Task SendEmailAsync(string toEmail)
        {
            Console.WriteLine($"Sending email to: {toEmail}");
        }
    }
}
