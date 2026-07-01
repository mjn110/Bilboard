using Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Services
{
    public class EmailSender : IEmailSender
    {
        public async Task SendEmailAsync(string toEmail)
        {
            Console.WriteLine($"Sending email to: {toEmail}");
        }
    }
}
