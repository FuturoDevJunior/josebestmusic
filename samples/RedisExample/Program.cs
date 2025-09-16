using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Policies;
using NetThrottler.Redis.Options;
using NetThrottler.Redis.Storage;
using StackExchange.Redis;

// Redis connection string - change this to your Redis instance
const string redisConnectionString = "localhost:6379";

Console.WriteLine("NetThrottler Redis Demo - Distributed Rate Limiting");
Console.WriteLine("==================================================");
Console.WriteLine();

try
{
    // Create logger
    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var logger = loggerFactory.CreateLogger<RedisThrottleStorage>();

    // Create Redis connection
    var connection = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
    Console.WriteLine($"✅ Connected to Redis at {redisConnectionString}");

    // Configure Redis storage options
    var redisOptions = new RedisThrottleStorageOptions
    {
        ConnectionString = redisConnectionString,
        Database = 0,
        KeyPrefix = "netthrottler:demo:",
        DefaultTtl = TimeSpan.FromMinutes(5)
    };

    // Create Redis storage
    var storage = new RedisThrottleStorage(connection, Options.Create(redisOptions), logger);

    // Create Token Bucket policy with Redis storage
    var policy = new TokenBucketPolicy(
        "redis-demo-policy",
        capacity: 10,
        refillRatePerSecond: 2.0,
        storage,
        loggerFactory.CreateLogger<TokenBucketPolicy>());

    Console.WriteLine("Policy: Token Bucket (Capacity: 10, Refill: 2/sec)");
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

        Console.WriteLine($"Request #{requestCount}: {(isAllowed ? "✅ ALLOWED" : "❌ RATE LIMITED")} " +
                         $"- Remaining: {state?.RemainingPermits ?? 0}, " +
                         $"Reset: {state?.ResetTime:HH:mm:ss}");

        if (!isAllowed)
        {
            Console.WriteLine("  ⚠️  Rate limit exceeded! Wait for tokens to refill.");
        }
    }

    Console.WriteLine("\nDemo completed. Cleaning up...");

    // Cleanup
    storage.Dispose();
    connection.Dispose();
    loggerFactory.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    Console.WriteLine("Make sure Redis is running on localhost:6379");
    Console.WriteLine("You can start Redis with: docker run -p 6379:6379 redis:alpine");
}
