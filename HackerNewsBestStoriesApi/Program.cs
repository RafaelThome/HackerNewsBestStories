using HackerNewsBestStoriesApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDefaultServices()
                .AddSwaggerSetup()
                .AddNamedHttpClient()
                .AddNamedRateLimiter()
                .Setup();

var app = builder.Build();

app.Setup()
    .Run();
