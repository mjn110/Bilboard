using Application.Common.Interface.Persistence;
using Application.Interfaces;
using Application.Services;
using Bilboard.Application.Interfaces;
using Bilboard.Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddScoped<IBoardService, BoardService>();
            services.AddScoped<IConsoleService, ConsoleService>();
            services.AddScoped<IEmailSender, EmailSender>();
            services.AddScoped<IJwtService, JwtService>();

            return services;
        }
    }
}
