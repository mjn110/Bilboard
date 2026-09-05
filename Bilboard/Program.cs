using Bilboard.Application.Interfaces;
using Bilboard.Application.Services;
using Bilboard.Components;
using Bilboard.Evaluation;
using Bilboard.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IConsoleService, ConsoleService>();
builder.Services.AddScoped<IJwtAuthService, JwtAuthService>();

// Dashboard generation pipeline + BIL execution, shared by the Boards page
// and the VisEval evaluation endpoints.
builder.Services.AddEvaluation();

builder.Services.AddHttpClient();

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["ApiSettings:BaseAddress"] ?? "https://178.105.30.34/api")
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// POST /api/eval/generate and POST /api/eval/execute for the Python VisEval harness.
// Only mapped when Evaluation:Enabled is true (default: Development only).
app.MapEvaluationEndpoints();

app.Run();
