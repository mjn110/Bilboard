using Application;
using Application.DTO.Account;
using Application.Interfaces;
using Application.Services;
using Bilboard.Application.Interfaces;
using Bilboard.Application.Services;
using Domain.Entities;
using Infrastructure;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Presentation.Seeders;
using Presentation.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<IConsoleService, ConsoleService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<IJwtService, JwtService>();

#region Context  
//builder.Services.AddDbContext<BilContext>(options => options.UseSqlServer(Environment.GetEnvironmentVariable("BILBOARD_DB_CONNECTION")));
builder.Services.AddDbContext<BilContext>(options => options.UseNpgsql(Environment.GetEnvironmentVariable("BILBOARD_DB_CONNECTION")));
#endregion

#region Identity  
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = false)
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<BilContext>()
.AddDefaultTokenProviders();
builder.Services.AddScoped<SignInManager<ApplicationUser>>(); // Ensure SignInManager is registered
builder.Services.AddScoped<UserManager<ApplicationUser>>(); // Ensure UserManager is registered
#endregion

#region Authentication
// Validates the JWTs issued by JwtTokenService (same secret, issuer and audience) so the board
// endpoints know which ApplicationUser is calling. Identity's cookie scheme stays the default;
// the board endpoints select this scheme explicitly.
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        var jwtSettings = builder.Configuration.GetSection("JwtSettings");
        var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrEmpty(secretKey))
        {
            throw new InvalidOperationException("JWT settings are not configured properly");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };
    });
#endregion

#region CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});
#endregion

var app = builder.Build();

// Seed BEFORE middleware, but AFTER app is built
using (var scope = app.Services.CreateScope())
{
    try
    {
        await IdentitySeeder.SeedAdminUserAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error during identity seeding");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowBlazorClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
