using Argus.Sync.Data.Models;
using Argus.Sync.Extensions;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCardanoIndexer<Web3ServicesDbContext>(builder.Configuration);
builder.Services.AddReducers<Web3ServicesDbContext, IReducerModel>(builder.Configuration);

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();