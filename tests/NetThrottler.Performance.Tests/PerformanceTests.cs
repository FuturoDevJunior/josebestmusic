using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Logging;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Policies;
using NetThrottler.Core.Storage;
using Xunit;

namespace NetThrottler.Performance.Tests;

/// <summary>
/// Performance tests for NetThrottler components.
/// </summary>
public class PerformanceTests
{
    [Fact]
    public void RunTokenBucketBenchmark()
    {
        var summary = BenchmarkRunner.Run<TokenBucketBenchmark>();
        Assert.NotNull(summary);
    }

    [Fact]
    public void RunMemoryStorageBenchmark()
    {
        var summary = BenchmarkRunner.Run<MemoryStorageBenchmark>();
        Assert.NotNull(summary);
    }

    [Fact]
    public void RunConcurrencyBenchmark()
    {
        var summary = BenchmarkRunner.Run<ConcurrencyBenchmark>();
        Assert.NotNull(summary);
    }
}

/// <summary>
/// Benchmark for TokenBucketPolicy performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class TokenBucketBenchmark
{
    private IThrottleStorage _storage = null!;
    private TokenBucketPolicy _policy = null!;
    private readonly string _testKey = "benchmark-key";

    [GlobalSetup]
    public void Setup()
    {
        _storage = new MemoryThrottleStorage();
        _policy = new TokenBucketPolicy(
            "benchmark-policy",
            capacity: 1000,
            refillRatePerSecond: 100.0,
            _storage,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenBucketPolicy>.Instance);
    }

    [Benchmark]
    public async Task<bool> TryAcquireAsync()
    {
        return await _policy.TryAcquireAsync(_testKey, 1);
    }

    [Benchmark]
    public async Task<object?> GetStateAsync()
    {
        return await _policy.GetStateAsync(_testKey);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _policy?.Dispose();
    }
}

/// <summary>
/// Benchmark for MemoryThrottleStorage performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class MemoryStorageBenchmark
{
    private IThrottleStorage _storage = null!;
    private readonly string _testKey = "benchmark-storage-key";
    private readonly string _testValue = "benchmark-storage-value";

    [GlobalSetup]
    public void Setup()
    {
        _storage = new MemoryThrottleStorage();
    }

    [Benchmark]
    public async Task SetAsync()
    {
        await _storage.SetAsync(_testKey, _testValue, TimeSpan.FromMinutes(1));
    }

    [Benchmark]
    public async Task<string?> GetAsync()
    {
        return await _storage.GetAsync(_testKey);
    }

    [Benchmark]
    public async Task<long> IncrementAsync()
    {
        return await _storage.IncrementAsync(_testKey, 1, TimeSpan.FromMinutes(1));
    }

    [Benchmark]
    public async Task<bool> ExistsAsync()
    {
        return await _storage.ExistsAsync(_testKey);
    }
}

/// <summary>
/// Benchmark for concurrent operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class ConcurrencyBenchmark
{
    private IThrottleStorage _storage = null!;
    private TokenBucketPolicy _policy = null!;
    private readonly string _testKey = "concurrency-key";

    [GlobalSetup]
    public void Setup()
    {
        _storage = new MemoryThrottleStorage();
        _policy = new TokenBucketPolicy(
            "concurrency-policy",
            capacity: 10000,
            refillRatePerSecond: 1000.0,
            _storage,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenBucketPolicy>.Instance);
    }

    [Benchmark]
    public async Task<bool> ConcurrentTryAcquireAsync()
    {
        var tasks = new List<Task<bool>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(_policy.TryAcquireAsync(_testKey, 1));
        }
        var results = await Task.WhenAll(tasks);
        return results.All(r => r);
    }

    [Benchmark]
    public async Task ConcurrentIncrementAsync()
    {
        var tasks = new List<Task<long>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(_storage.IncrementAsync(_testKey, 1, TimeSpan.FromMinutes(1)));
        }
        await Task.WhenAll(tasks);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _policy?.Dispose();
    }
}

/// <summary>
/// Load tests for high-throughput scenarios.
/// </summary>
public class LoadTests
{
    [Fact]
    public async Task HighThroughputTokenBucketTest()
    {
        // Arrange
        var storage = new MemoryThrottleStorage();
        var policy = new TokenBucketPolicy(
            "load-test-policy",
            capacity: 10000,
            refillRatePerSecond: 1000.0,
            storage,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenBucketPolicy>.Instance);

        var testKey = "load-test-key";
        var requestCount = 10000;
        var successCount = 0;

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tasks = new List<Task<bool>>();

        for (int i = 0; i < requestCount; i++)
        {
            tasks.Add(policy.TryAcquireAsync(testKey, 1));
        }

        var results = await Task.WhenAll(tasks);
        successCount = results.Count(r => r);
        stopwatch.Stop();

        // Assert
        var throughput = requestCount / stopwatch.Elapsed.TotalSeconds;
        Assert.True(throughput > 1000, $"Throughput {throughput:F2} requests/sec is below expected 1000 req/sec");
        Assert.True(successCount > 0, "No requests were successful");

        policy.Dispose();
    }

    [Fact]
    public async Task MemoryStorageLoadTest()
    {
        // Arrange
        var storage = new MemoryThrottleStorage();
        var operationCount = 10000;
        var successCount = 0;

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tasks = new List<Task>();

        for (int i = 0; i < operationCount; i++)
        {
            var key = $"load-test-key-{i}";
            var value = $"load-test-value-{i}";
            tasks.Add(storage.SetAsync(key, value, TimeSpan.FromMinutes(1)));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Verify operations
        for (int i = 0; i < Math.Min(1000, operationCount); i++)
        {
            var key = $"load-test-key-{i}";
            var value = await storage.GetAsync(key);
            if (value == $"load-test-value-{i}")
            {
                successCount++;
            }
        }

        // Assert
        var throughput = operationCount / stopwatch.Elapsed.TotalSeconds;
        Assert.True(throughput > 5000, $"Throughput {throughput:F2} operations/sec is below expected 5000 ops/sec");
        Assert.True(successCount > 0, "No operations were successful");
    }
}
