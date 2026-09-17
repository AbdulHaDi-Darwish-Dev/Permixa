using PermixaApp.Api.DependencyInjection;
using PermixaApp.Api.Endpoints;
using PermixaApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:Default. Run scripts/init-dev-secrets and/or set User Secrets / environment.");

builder.Services
    .AddAppInfrastructure(connectionString)
    .AddPermixaHost(builder.Configuration, builder.Environment, connectionString)
    .AddApiServices();

var app = builder.Build();

await app.InitializeDevelopmentAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapApiEndpoints();

app.Run();

public partial class Program;
