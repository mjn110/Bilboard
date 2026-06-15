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
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.DisplayName, _settings.From));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            message.Body = new BodyBuilder
            {
                HtmlBody = $"<p>Hi,</p><p>Thank you for creating an account with us!</p><p>{htmlMessage}</p><p>Best regards, </p><p>Bilboard Team</p>"
            }.ToMessageBody();

            using var client = new SmtpClient();
            var username = Environment.GetEnvironmentVariable("EMAIL_USERNAME");
            var password = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");
            await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(username, password);
            var send = await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
