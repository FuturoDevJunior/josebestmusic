using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NetThrottler.Core.Storage;
using Xunit;

namespace NetThrottler.Core.Tests;

public class MemoryThrottleStorageTests : IDisposable
{
    private readonly MemoryThrottleStorage _storage;

    public MemoryThrottleStorageTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _storage = new MemoryThrottleStorage(memoryCache);
    }

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ShouldReturnNull()
    {
        // Act
        var result = await _storage.GetAsync("non-existent-key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_AndGetAsync_ShouldStoreAndRetrieveValue()
    {
        // Arrange
        const string key = "test-key";
        const string value = "test-value";

        // Act
        await _storage.SetAsync(key, value);
        var result = await _storage.GetAsync(key);

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public async Task SetAsync_WithTTL_ShouldExpireAfterTime()
    {
        // Arrange
        const string key = "ttl-key";
        const string value = "ttl-value";
        var ttl = TimeSpan.FromMilliseconds(100);

        // Act
        await _storage.SetAsync(key, value, ttl);

        // Verify it exists initially
        (await _storage.GetAsync(key)).Should().Be(value);

        // Wait for expiration
        await Task.Delay(150);

        // Assert
        (await _storage.GetAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task IncrementAsync_WithNewKey_ShouldStartAtIncrementValue()
    {
        // Arrange
        const string key = "increment-key";
        const long increment = 5;

        // Act
        var result = await _storage.IncrementAsync(key, increment);

        // Assert
        result.Should().Be(increment);
        (await _storage.GetAsync(key)).Should().Be(increment.ToString());
    }

    [Fact]
    public async Task IncrementAsync_WithExistingKey_ShouldAddToExistingValue()
    {
        // Arrange
        const string key = "increment-existing";
        await _storage.SetAsync(key, "10");

        // Act
        var result = await _storage.IncrementAsync(key, 3);

        // Assert
        result.Should().Be(13);
        (await _storage.GetAsync(key)).Should().Be("13");
    }

    [Fact]
    public async Task DecrementAsync_WithNewKey_ShouldReturnZero()
    {
        // Arrange
        const string key = "decrement-new";

        // Act
        var result = await _storage.DecrementAsync(key, 5);

        // Assert
        result.Should().Be(0);
        (await _storage.GetAsync(key)).Should().Be("0");
    }

    [Fact]
    public async Task DecrementAsync_WithExistingKey_ShouldSubtractFromExistingValue()
    {
        // Arrange
        const string key = "decrement-existing";
        await _storage.SetAsync(key, "10");

        // Act
        var result = await _storage.DecrementAsync(key, 3);

        // Assert
        result.Should().Be(7);
        (await _storage.GetAsync(key)).Should().Be("7");
    }

    [Fact]
    public async Task DecrementAsync_WithValueBelowZero_ShouldReturnZero()
    {
        // Arrange
        const string key = "decrement-below-zero";
        await _storage.SetAsync(key, "2");

        // Act
        var result = await _storage.DecrementAsync(key, 5);

        // Assert
        result.Should().Be(0);
        (await _storage.GetAsync(key)).Should().Be("0");
    }

    [Fact]
    public async Task RemoveAsync_WithExistingKey_ShouldRemoveKey()
    {
        // Arrange
        const string key = "remove-key";
        await _storage.SetAsync(key, "value");

        // Act
        await _storage.RemoveAsync(key);

        // Assert
        (await _storage.GetAsync(key)).Should().BeNull();
        (await _storage.ExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ShouldReturnTrue()
    {
        // Arrange
        const string key = "exists-key";
        await _storage.SetAsync(key, "value");

        // Act
        var exists = await _storage.ExistsAsync(key);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentKey_ShouldReturnFalse()
    {
        // Act
        var exists = await _storage.ExistsAsync("non-existent");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExpireAsync_WithExistingKey_ShouldSetExpiration()
    {
        // Arrange
        const string key = "expire-key";
        await _storage.SetAsync(key, "value");

        // Act
        await _storage.ExpireAsync(key, TimeSpan.FromMilliseconds(100));

        // Verify it exists initially
        (await _storage.ExistsAsync(key)).Should().BeTrue();

        // Wait for expiration
        await Task.Delay(150);

        // Assert
        (await _storage.ExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task ExpireAsync_WithNonExistentKey_ShouldNotThrow()
    {
        // Act & Assert
        await _storage.ExpireAsync("non-existent", TimeSpan.FromMinutes(1));
        // Should not throw
    }

    [Fact]
    public async Task ConcurrentIncrementAsync_ShouldBeThreadSafe()
    {
        // Arrange
        const string key = "concurrent-increment";
        const int concurrentOperations = 100;

        // Act
        var tasks = Enumerable.Range(0, concurrentOperations)
            .Select(_ => Task.Run(() => _storage.IncrementAsync(key, 1)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        var finalValue = await _storage.GetAsync(key);
        finalValue.Should().Be(concurrentOperations.ToString());
    }

    [Fact]
    public async Task SetAsync_WithInvalidKey_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SetAsync("", "value"));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SetAsync("   ", "value"));
    }

    [Fact]
    public async Task GetAsync_WithInvalidKey_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.GetAsync("   "));
    }

    [Fact]
    public async Task IncrementAsync_WithTTL_ShouldSetExpiration()
    {
        // Arrange
        const string key = "increment-ttl";
        var ttl = TimeSpan.FromMilliseconds(100);

        // Act
        await _storage.IncrementAsync(key, 1, ttl);

        // Verify it exists initially
        (await _storage.ExistsAsync(key)).Should().BeTrue();

        // Wait for expiration
        await Task.Delay(150);

        // Assert
        (await _storage.ExistsAsync(key)).Should().BeFalse();
    }

    public void Dispose()
    {
        _storage.Dispose();
    }
}
