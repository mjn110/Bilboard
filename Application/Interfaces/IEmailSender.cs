using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interfaces
{
    public interface IEmailSender
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlMessage);
    }
}
