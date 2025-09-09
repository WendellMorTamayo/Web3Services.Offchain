using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Web3Services.Data.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFastEndpoints();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddFastEndpoints(o => o.IncludeAbstractValidators = true);
builder.Services.AddDbContextFactory<Web3ServicesDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("CardanoContext"),
        x => x.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            builder.Configuration.GetConnectionString("CardanoContextSchema")
        )
    );
});
WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Levvy API Documentation";
        options.Theme = ScalarTheme.Default;
        options.ShowSidebar = true;
    });
}

app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = "api";
    c.Versioning.Prefix = "v";
    c.Versioning.DefaultVersion = 1;
    c.Versioning.PrependToRoute = true;
});

app.Run();
