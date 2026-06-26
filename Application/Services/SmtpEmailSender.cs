using Application.DTO.Account;
using Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Text;


namespace Application.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly EmailSettings _settings;

        public SmtpEmailSender(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        {
            Console.WriteLine($"Sending email to: {toEmail}, Subject: {subject}, Message: {htmlMessage}");
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Bilboard Verification Email", "verify@bilboard.online"));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            message.Body = new BodyBuilder
            {
                HtmlBody = $"<p>Hi,</p><p>Thank you for creating an account with us!</p><p>{htmlMessage}</p><p>Best regards, </p><p>Bilboard Team</p>"
            }.ToMessageBody();

            using var client = new SmtpClient();
            var username = "verify@bilboard.online";
            var password = "BIL@board123";
            Console.WriteLine($"Started to connect to SMTP server: smtp.ionos.co.uk");
            await client.ConnectAsync("smtp.ionos.co.uk", 587, SecureSocketOptions.StartTlsWhenAvailable);
            Console.WriteLine($"Connected to SMTP server: smtp.ionos.co.uk");
            await client.AuthenticateAsync(username, password);
            var send = await client.SendAsync(message);
            await client.DisconnectAsync(true);
            Console.WriteLine($"Email sent successfully to: {toEmail}");
        }
    }
}
