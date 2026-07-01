using Application.DTO.Account;
using Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Text;


namespace Application.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        //private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public SmtpEmailSender(/*HttpClient httpClient,*/ IConfiguration config)
        {
            //_httpClient = httpClient;
            _apiKey = config["Brevo:ApiKey"]; // Or Brevo__ApiKey from env
        }

        public async Task SendEmailAsync(string toEmail)
        {
            Console.WriteLine($"Sending email to: {toEmail}");

            //var payload = new
            //{
            //    sender = new { email = "verify@bilboard.online", name = "Bilboard" },
            //    to = new[] { new { email = toEmail } },
            //    subject = "Welcome!",
            //    htmlContent = "<h1>Welcome to our app</h1><p>Thanks for signing up.</p>"
            //};

            //var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
            //{
            //    Content = new StringContent(
            //        System.Text.Json.JsonSerializer.Serialize(payload),
            //        System.Text.Encoding.UTF8,
            //        "application/json")
            //};

            //request.Headers.Add("api-key", _apiKey);
            //Console.WriteLine($"Sending email to: {toEmail}");
            //var response = await _httpClient.SendAsync(request);
            //response.EnsureSuccessStatusCode();
            //Console.WriteLine($"Email sent successfully to: {toEmail}");
        }



        //private readonly EmailSettings _settings;

        //public SmtpEmailSender(IOptions<EmailSettings> settings)
        //{
        //    _settings = settings.Value;
        //}

        //public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        //{
        //    Console.WriteLine($"Sending email to: {toEmail}, Subject: {subject}, Message: {htmlMessage}");
        //    var message = new MimeMessage();
        //    message.From.Add(new MailboxAddress("Bilboard Verification Email", "verify@bilboard.online"));
        //    message.To.Add(MailboxAddress.Parse(toEmail));
        //    message.Subject = subject;

        //    message.Body = new BodyBuilder
        //    {
        //        HtmlBody = $"<p>Hi,</p><p>Thank you for creating an account with us!</p><p>{htmlMessage}</p><p>Best regards, </p><p>Bilboard Team</p>"
        //    }.ToMessageBody();

        //    using var client = new SmtpClient();
        //    var username = "b00f17001@smtp-brevo.com";
        //    var password = "xsmtpsib-5a0209396e0b33b482954ccde1f8f29e725f49a7e1aa669406c5aae5677e2b27-wWud8GyhgcPMzl73";
        //    Console.WriteLine($"Started to connect to SMTP server: smtp-relay.brevo.com");
        //    await client.ConnectAsync("smtp-relay.brevo.com", 587, SecureSocketOptions.StartTls);
        //    Console.WriteLine($"Connected to SMTP server: smtp-relay.brevo.com");
        //    await client.AuthenticateAsync(username, password);
        //    var send = await client.SendAsync(message);
        //    await client.DisconnectAsync(true);
        //    Console.WriteLine($"Email sent successfully to: {toEmail}");
        //}
    }
}
