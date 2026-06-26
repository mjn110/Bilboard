using brevo_csharp.Api;  // <-- This imports TransactionalEmailsApi
using brevo_csharp.Client;
using brevo_csharp.Model;
using Microsoft.Extensions.Options;

namespace Application.Services
{
    public class BrevoSettings
    {
        public string ApiKey { get; set; }
        public string SenderEmail { get; set; }
        public string SenderName { get; set; }
    }

    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent);
    }

    public class EmailService : IEmailService
    {
        private readonly BrevoSettings _settings;
        private readonly TransactionalEmailsApi _emailApi;

        public EmailService(IOptions<BrevoSettings> options)
        {
            _settings = options.Value;
            
            // Configure the API key
            brevo_csharp.Client.Configuration.Default.ApiKey.Add("api-key", _settings.ApiKey);
            
            _emailApi = new TransactionalEmailsApi();
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            try
            {
                var email = new SendSmtpEmail
                {
                    Subject = subject,
                    HtmlContent = htmlContent,
                    Sender = new SendSmtpEmailSender
                    {
                        Email = _settings.SenderEmail,
                        Name = _settings.SenderName
                    },
                    To = new List<SendSmtpEmailTo>
                    {
                        new SendSmtpEmailTo(email: toEmail)
                    }
                };

                var response = await _emailApi.SendTransacEmailAsync(email);
                return response != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
                return false;
            }
        }
    }
}