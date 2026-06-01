using Application.Interfaces;
using Application.Services;
using Bilboard.Application.Interfaces;
using Bilboard.Application.Services;
using Infrastructure.Data;
using Infrastructure.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Presentation.Seeders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddScoped<IBoardService, BoardService>();
builder.Services.AddScoped<IConsoleService, ConsoleService>();

#region Context  
builder.Services.AddDbContext<BilContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
//builder.Services.AddDbContext<EnrolmentContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
#endregion

#region Identity  
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = false)
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<BilContext>()
.AddDefaultTokenProviders();
builder.Services.AddScoped<SignInManager<ApplicationUser>>(); // Ensure SignInManager is registered
builder.Services.AddScoped<UserManager<ApplicationUser>>(); // Ensure UserManager is registered
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
app.UseAuthentication(); // Add this line  
app.UseAuthorization(); // Add this line  
app.MapControllers();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
