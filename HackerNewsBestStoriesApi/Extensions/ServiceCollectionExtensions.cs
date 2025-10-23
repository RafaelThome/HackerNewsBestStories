using System.Threading.RateLimiting;
using System.Net;

using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi.Models;

using Polly;
using Polly.RateLimiting;

using HackerNewsBestStoriesApi.Services;

namespace HackerNewsBestStoriesApi.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection Setup(this IServiceCollection services)
        {
            services.Configure<HackerNewsClientOptions>(options =>
            {
                options.BestIdsCacheSeconds = 1;            // cache beststories IDs for 1s
                options.ItemCacheSeconds = 600;             // cache story items for 10 minutes
                options.MaxThreadserCore = 10;              // concurrent fetch limit
                options.MaxN = 1000;                        // maximum n (number of items) allowed
                options.RequestSemaphoreCount = 1000;       // maximum number of concurrent requests for HN
            });
            return services;
        }

        public static IServiceCollection AddDefaultServices(this IServiceCollection services)
        {
            services.AddOpenApi();
            services.AddMemoryCache();
            services.AddSingleton<IHackerNewsClient, HackerNewsClient>();
            services.AddEndpointsApiExplorer();
            return services;
        }

        public static IServiceCollection AddSwaggerSetup(this IServiceCollection services)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v0", new OpenApiInfo
                {
                    Title = "Hacker News Best Stories API",
                    Version = "v0",
                    Description = "A RESTful API that retrieves the first N best stories from Hacker News, sorted by score (descending)."
                });
            });
            return services;
        }

        public static IServiceCollection AddNamedRateLimiter(this IServiceCollection services)
        {
            services.AddRateLimiter(limiterOptions =>
            {
                limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiterOptions.AddPolicy("concurrencyByIp", context =>
                {
                    var remoteIp = context.Connection.RemoteIpAddress;
                    return remoteIp == null //|| !IPAddress.IsLoopback(remoteIp)
                            ? RateLimitPartition.GetConcurrencyLimiter(remoteIp, _
                                => new()
                                {
                                    PermitLimit = 10,
                                    QueueLimit = 100,
                                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                })
                            : RateLimitPartition.GetNoLimiter<IPAddress?>(IPAddress.Loopback);
                });
            });
            return services;
        }

        public static IServiceCollection AddNamedHttpClient(this IServiceCollection services)
        {
            services.AddHttpClient("hackernews"
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
            return services;
        }
    }
}
