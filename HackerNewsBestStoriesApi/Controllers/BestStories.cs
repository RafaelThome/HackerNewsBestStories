using HackerNewsBestStoriesApi.Models;
using HackerNewsBestStoriesApi.Services;

using Microsoft.AspNetCore.RateLimiting;

namespace HackerNewsBestStoriesApi.Controllers
{
    public static class BestStories
    {
        public static void AddRoutes(IEndpointRouteBuilder app)
        {
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
                    .RequireRateLimiting("concurrencyByIp")
                    .WithName("GetBestStories")
                    .WithDescription("Returns the first N best stories from Hacker News, sorted by score (descending).")
                    .WithOpenApi(op =>
                    {
                        op.Parameters[0].Description = "Number of stories to retrieve (default: 10, max: 500)";
                        op.Summary = "Get top Hacker News best stories";
                        op.Description = "Fetches the first N best stories from the Hacker News API and returns them sorted by score in descending order.";
                        return op;
                    })
                    .Produces<List<BestStoriesResponseItem>>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status400BadRequest);

        }
    }
}
