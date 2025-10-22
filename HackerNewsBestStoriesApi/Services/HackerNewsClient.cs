using HackerNewsBestStoriesApi.Models;

using Microsoft.Extensions.Caching.Memory;

using System.Net;
using System.Text.Json;

namespace HackerNewsBestStoriesApi.Services
{
    public class HackerNewsClientOptions
    {
        public int BestIdsCacheSeconds { get; set; } = 1;
        public int ItemCacheSeconds { get; set; } = 600;
        public int MaxThreadserCore { get; set; } = 10;
        public int MaxN { get; set; } = 1000;
        public int RequestSemaphoreCount { get; set; } = 1000;
    }

    public class HackerNewsClient : IHackerNewsClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly HackerNewsClientOptions _options;
        private readonly SemaphoreSlim _semaphore;
        private readonly JsonSerializerOptions jsonSerOpt = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string BestIdsCacheKey = "hn_best_ids";

        private static string UnixTimeToIso8601(long unixTime)
        {
            // HN times are seconds since epoch (UTC)
            var dt = DateTimeOffset.FromUnixTimeSeconds(unixTime).ToUniversalTime();
            // Format with offset +00:00
            return dt.ToString("yyyy-MM-ddTHH:mm:ssK");
        }

        public HackerNewsClient(IHttpClientFactory httpClientFactory, IMemoryCache cache, Microsoft.Extensions.Options.IOptions<HackerNewsClientOptions> options)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _options = options.Value;
            _semaphore = new SemaphoreSlim(_options.RequestSemaphoreCount);
        }

        public async Task<List<BestStoryResponse>> GetFirstNBestStoriesAsync(int n)
        {
            var bestIds = await GetBestStoryIdsAsync() ?? throw new ApplicationException("Unexpected errror fetching top scored IDs");
            var idsToFetch = bestIds.Length > n 
                                    ? [.. bestIds.Take(n)]
                                    : bestIds;

            var results = new List<BestStoryResponse>();
            var client = _httpClientFactory.CreateClient("hackernews");

            var bag = new System.Collections.Concurrent.ConcurrentBag<BestStoryResponse>();
            await Parallel.ForEachAsync(idsToFetch
                                            , new ParallelOptions 
                                            { 
                                                MaxDegreeOfParallelism = Environment.ProcessorCount *  _options.MaxThreadserCore 
                                            }
                                            , async (id, ct) =>
            {
                var item = await GetItemCachedAsync(id, client, ct);
                if (item != null)
                {
                    var resp = new BestStoryResponse
                    {
                        Title = item.Title ?? "",
                        Uri = item.Url,
                        PostedBy = item.By ?? "",
                        Time = UnixTimeToIso8601(item.Time),
                        Score = item.Score,
                        CommentCount = item.Kids?.Length ?? 0
                    };
                    bag.Add(resp);
                }
            });

            return [.. bag.OrderByDescending(r => r.Score)];
        }

        private async Task<int[]?> GetBestStoryIdsAsync()
        {
            if (_cache.TryGetValue<int[]>(BestIdsCacheKey, out var cached) && cached != null)
                return cached;

            // Avoid multiple threads fetching simultaneously by using GetOrCreateAsync
            var newIds = await _cache.GetOrCreateAsync(BestIdsCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.BestIdsCacheSeconds);
                var client = _httpClientFactory.CreateClient("hackernews");

                // Use a semaphore to avoid many outbound requests at once
                await _semaphore.WaitAsync();
                try
                {
                    var resp = await client.GetAsync("beststories.json");
                    if (!resp.IsSuccessStatusCode) 
                        return [];
                    
                    var content = await resp.Content.ReadAsStringAsync();
                    var ids = JsonSerializer.Deserialize<int[]>(content);
                    
                    return ids ?? [];
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            return newIds;
        }

        private async Task<HackerNewsItemDto?> GetItemCachedAsync(int id, HttpClient client, CancellationToken ct)
        {
            string cacheKey = $"hn_item_{id}";

            // Use GetOrCreateAsync and store Task result to avoid duplicate fetches (thundering herd)
            var result = await _cache.GetOrCreateAsync<Task<HackerNewsItemDto?>>(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.ItemCacheSeconds);

                // Acquire semaphore before making external call
                await _semaphore.WaitAsync(ct);
                try
                {
                    var resp = await client.GetAsync($"item/{id}.json", ct);
                    if (resp.StatusCode == HttpStatusCode.NotFound) 
                        return Task.FromResult<HackerNewsItemDto?>(null);
                    resp.EnsureSuccessStatusCode();
                    
                    var content = await resp.Content.ReadAsStringAsync(ct);
                    var item = JsonSerializer.Deserialize<HackerNewsItemDto>(content, jsonSerOpt);

                    return Task.FromResult(item);
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            // unwrap task result
            return await result!;
        }
    }
}
