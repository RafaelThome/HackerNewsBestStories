using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Models;

using Polly;
using Polly.RateLimiting;

using HackerNewsBestStoriesApi.Models;
using HackerNewsBestStoriesApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddHttpClient("hackernews"
                                , client =>
{
    client.BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/");
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddResilienceHandler("standard"
                            , static builder =>
{
    builder.AddRateLimiter(new RateLimiterStrategyOptions
    {
        DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
        {
            PermitLimit = 100,
            QueueLimit = 1000,
        },
        OnRejected = args =>
        {
            //ToDo: logging
            Console.WriteLine("Rate limit has been exceeded");
            return default;
        }
    });
    // Retry policy (exponential backoff, respects Retry-After)
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        BackoffType = DelayBackoffType.Exponential,
        MaxRetryAttempts = 3,
        UseJitter = true,
        MaxDelay = TimeSpan.FromSeconds(10),
        ShouldHandle = static args => ValueTask.FromResult(
            args.Outcome switch
            {
                { Exception: HttpRequestException } => true,
                { Result.StatusCode: HttpStatusCode.RequestTimeout } => true,
                { Result.StatusCode: HttpStatusCode.TooManyRequests } => true,
                { Result.StatusCode: >= HttpStatusCode.InternalServerError } => true,
                _ => false
            })
    });
    // Circuit breaker to prevent hammering when HN is down
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(30),
        FailureRatio = 0.2,  // 20% failures in the sample triggers break
        MinimumThroughput = 100,
        BreakDuration = TimeSpan.FromSeconds(60)
    });
});

builder.Services.AddMemoryCache();

builder.Services.Configure<HackerNewsClientOptions>(options =>
{
    options.BestIdsCacheSeconds = 1; // cache beststories IDs for 1s
    options.ItemCacheSeconds = 600; // cache story items for 10 minutes
    options.MaxThreadserCore = 10; // concurrent fetch limit
    options.MaxN = 1000; // maximum n (number of items) allowed
    options.RequestSemaphoreCount = 1000; // maximum number of concurrent requests for HN
});

builder.Services.AddSingleton<IHackerNewsClient, HackerNewsClient>();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v0", new OpenApiInfo
    {
        Title = "Hacker News Best Stories API",
        Version = "v0",
        Description = "A RESTful API that retrieves the first N best stories from Hacker News, sorted by score (descending)."
    });
});

builder.Services.AddRateLimiter(limiterOptions =>
{
    limiterOptions.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                                                            ? (StringValues)((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo)
                                                            : (StringValues)"30";

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        return new ValueTask();
    };

    limiterOptions.AddPolicy("rateLimiterPolicy", context =>
    {
        var username = "anonymous user";
        if (context.User.Identity?.IsAuthenticated is true)
        {
            username = context.User.Identity.Name;
        }

        return RateLimitPartition.GetSlidingWindowLimiter(username,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 100,
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });

    });

    limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, IPAddress>(context =>
    {
        IPAddress? remoteIpAddress = context.Connection.RemoteIpAddress;

        return !IPAddress.IsLoopback(remoteIpAddress!)
                    ? RateLimitPartition.GetTokenBucketLimiter(remoteIpAddress!, _ =>
                        new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 100,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 1000,
                            AutoReplenishment = true
                        })
                    : RateLimitPartition.GetNoLimiter(IPAddress.Loopback);
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v0/swagger.json", "Hacker News Best Stories API v0");
        c.RoutePrefix = "";
    });
}
else
    app.UseHttpsRedirection();

// Minimal API endpoint
app.MapGet("/api/beststories"
            , async (int? n
                                , IHackerNewsClient hnClient
                                , Microsoft.Extensions.Options.IOptions<HackerNewsClientOptions> cfg) =>
{
    int maxN = cfg.Value.MaxN;
    int requestedN = n ?? 10;

    if (requestedN <= 0)
        return Results.BadRequest(new { error = "Parameter 'n' must be a positive integer." });

    if (requestedN > maxN)
        return Results.BadRequest(new { error = $"Parameter 'n' must be <= {maxN}." });

    var items = await hnClient.GetFirstNBestStoriesAsync(requestedN);

    return Results.Ok(items);
})
.WithName("GetBestStories")
.WithDescription("Returns the first N best stories from Hacker News, sorted by score (descending).")
.WithOpenApi(op =>
{
    op.Parameters[0].Description = "Number of stories to retrieve (default: 10, max: 500)";
    op.Summary = "Get top Hacker News best stories";
    op.Description = "Fetches the first N best stories from the Hacker News API and returns them sorted by score in descending order.";
    return op;
})
.Produces<List<BestStoryResponse>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

app.Run();