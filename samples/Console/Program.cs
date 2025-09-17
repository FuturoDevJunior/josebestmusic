using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Policies;
using NetThrottler.Core.Storage;

// Create a simple console application to demonstrate NetThrottler
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var memoryCache = new MemoryCache(new MemoryCacheOptions());
var storage = new MemoryThrottleStorage(memoryCache, loggerFactory.CreateLogger<MemoryThrottleStorage>());

// Create a Token Bucket policy
var policy = new TokenBucketPolicy(
    "demo-policy",
    capacity: 5,
    refillRatePerSecond: 1.0,
    storage,
    loggerFactory.CreateLogger<TokenBucketPolicy>());

Console.WriteLine("NetThrottler Demo - Token Bucket Policy");
Console.WriteLine("Capacity: 5 tokens, Refill Rate: 1 token/second");
Console.WriteLine("Press any key to make a request, 'q' to quit");
Console.WriteLine();

var key = "demo-user";
var requestCount = 0;

while (true)
{
    var input = Console.ReadKey(true);

    if (input.KeyChar == 'q' || input.KeyChar == 'Q')
    {
        break;
    }

    requestCount++;
    var isAllowed = await policy.TryAcquireAsync(key, 1);
    var state = await policy.GetStateAsync(key);

    Console.WriteLine($"Request #{requestCount}: {(isAllowed ? "ALLOWED" : "RATE LIMITED")} " +
                     $"- Remaining: {state?.RemainingPermits ?? 0}, " +
                     $"Reset: {state?.ResetTime:HH:mm:ss}");

    if (!isAllowed)
    {
        Console.WriteLine("  ⚠️  Rate limit exceeded! Wait for tokens to refill.");
    }
}

Console.WriteLine("\nDemo completed. Press any key to exit...");
Console.ReadKey();

// Cleanup
policy.Dispose();
storage.Dispose();
memoryCache.Dispose();
loggerFactory.Dispose();
