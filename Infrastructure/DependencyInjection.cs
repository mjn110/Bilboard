using Application.Common.Interface.Persistence;
using Microsoft.AspNetCore.Identity;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, ConfigurationManager configuration)
        {
            services.AddScoped<IBoardRepository, BoardRepository>();


            //// Add DbContext
            //services.AddDbContext<BilContext>(options =>
            //    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            //// FIX: Ensure AddIdentity extension method is available by referencing the correct NuGet package and using directive
            //services.AddIdentityCore<ApplicationUser>()
            //    .AddRoles<IdentityRole>()
            //    //.AddSignInManager()
            //    .AddEntityFrameworkStores<BilContext>();

            return services;
        }
    }
}
