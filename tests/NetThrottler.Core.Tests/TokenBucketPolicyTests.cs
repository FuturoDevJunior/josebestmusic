using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Policies;
using NetThrottler.Core.Storage;
using Xunit;

namespace NetThrottler.Core.Tests;

public class TokenBucketPolicyTests : IDisposable
{
    private readonly MemoryThrottleStorage _storage;
    private readonly TokenBucketPolicy _policy;

    public TokenBucketPolicyTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _storage = new MemoryThrottleStorage(memoryCache);
        _policy = new TokenBucketPolicy("test-policy", capacity: 5, refillRatePerSecond: 2.0, _storage);
    }

    [Fact]
    public async Task TryAcquireAsync_WithValidKey_ShouldAllowRequestsUpToCapacity()
    {
        // Arrange
        const string key = "test-key";

        // Act & Assert
        (await _policy.TryAcquireAsync(key, 1)).Should().BeTrue();
        (await _policy.TryAcquireAsync(key, 1)).Should().BeTrue();
        (await _policy.TryAcquireAsync(key, 1)).Should().BeTrue();
        (await _policy.TryAcquireAsync(key, 1)).Should().BeTrue();
        (await _policy.TryAcquireAsync(key, 1)).Should().BeTrue();

        // Should be rate limited after capacity is exceeded
        (await _policy.TryAcquireAsync(key, 1)).Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WithMultiplePermits_ShouldRespectCapacity()
    {
        // Arrange
        const string key = "test-key";

        // Act & Assert
        (await _policy.TryAcquireAsync(key, 3)).Should().BeTrue();
        (await _policy.TryAcquireAsync(key, 2)).Should().BeTrue();

        // Should be rate limited after capacity is exceeded
        (await _policy.TryAcquireAsync(key, 1)).Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WithRefillRate_ShouldAllowRequestsAfterTime()
    {
        // Arrange
        const string key = "test-key";
        var policy = new TokenBucketPolicy("refill-test", capacity: 2, refillRatePerSecond: 10.0, _storage);

        // Act - consume all tokens
        (await policy.TryAcquireAsync(key, 1)).Should().BeTrue();
        (await policy.TryAcquireAsync(key, 1)).Should().BeTrue();
        (await policy.TryAcquireAsync(key, 1)).Should().BeFalse();

        // Wait for refill (100ms should add 1 token)
        await Task.Delay(150);

        // Assert - should allow one more request
        (await policy.TryAcquireAsync(key, 1)).Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WithDifferentKeys_ShouldBeIndependent()
    {
        // Arrange
        const string key1 = "key1";
        const string key2 = "key2";

        // Act - consume all tokens for key1
        (await _policy.TryAcquireAsync(key1, 5)).Should().BeTrue();
        (await _policy.TryAcquireAsync(key1, 1)).Should().BeFalse();

        // Assert - key2 should still have full capacity
        (await _policy.TryAcquireAsync(key2, 5)).Should().BeTrue();
        (await _policy.TryAcquireAsync(key2, 1)).Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WithConcurrentRequests_ShouldMaintainThreadSafety()
    {
        // Arrange
        const string key = "concurrent-key";
        const int concurrentRequests = 20;
        const int expectedAllowed = 5; // capacity

        // Act
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => Task.Run(() => _policy.TryAcquireAsync(key, 1)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        var allowedCount = results.Count(r => r);
        allowedCount.Should().Be(expectedAllowed);
    }

    [Fact]
    public async Task GetStateAsync_WithValidKey_ShouldReturnCurrentState()
    {
        // Arrange
        const string key = "state-key";
        await _policy.TryAcquireAsync(key, 2);

        // Act
        var state = await _policy.GetStateAsync(key);

        // Assert
        state.Should().NotBeNull();
        state!.Key.Should().Be(key);
        state.RemainingPermits.Should().Be(3); // 5 - 2
        state.TotalPermits.Should().Be(5);
        state.ResetTime.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetStateAsync_WithNonExistentKey_ShouldReturnNull()
    {
        // Arrange
        const string key = "non-existent-key";

        // Act
        var state = await _policy.GetStateAsync(key);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithInvalidParameters_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new TokenBucketPolicy("", 5, 2.0, _storage));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucketPolicy("test", 0, 2.0, _storage));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucketPolicy("test", 5, -1.0, _storage));
        Assert.Throws<ArgumentNullException>(() => new TokenBucketPolicy("test", 5, 2.0, null!));
    }

    [Fact]
    public async Task TryAcquireAsync_WithInvalidParameters_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _policy.TryAcquireAsync("", 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _policy.TryAcquireAsync("key", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _policy.TryAcquireAsync("key", -1));
    }

    [Fact]
    public void Policy_ShouldHaveCorrectProperties()
    {
        // Assert
        _policy.Name.Should().Be("test-policy");
        _policy.Algorithm.Should().Be("TokenBucket");
        _policy.MaxRequests.Should().Be(5);
        _policy.Capacity.Should().Be(5);
        _policy.RefillRatePerSecond.Should().Be(2.0);
        _policy.Parameters.Should().ContainKey("Capacity");
        _policy.Parameters.Should().ContainKey("RefillRatePerSecond");
    }

    [Fact]
    public async Task TryAcquireAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        const string key = "cancellation-key";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _policy.TryAcquireAsync(key, 1, cts.Token));
    }

    [Fact]
    public async Task TryAcquireAsync_WithHighRefillRate_ShouldRefillQuickly()
    {
        // Arrange
        var policy = new TokenBucketPolicy("fast-refill", capacity: 1, refillRatePerSecond: 100.0, _storage);
        const string key = "fast-key";

        // Act - consume token
        (await policy.TryAcquireAsync(key, 1)).Should().BeTrue();
        (await policy.TryAcquireAsync(key, 1)).Should().BeFalse();

        // Wait a short time (10ms should be enough for refill)
        await Task.Delay(20);

        // Assert - should allow another request
        (await policy.TryAcquireAsync(key, 1)).Should().BeTrue();
    }

    public void Dispose()
    {
        _storage.Dispose();
    }
}
