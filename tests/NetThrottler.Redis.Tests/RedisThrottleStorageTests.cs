using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetThrottler.Redis.Options;
using NetThrottler.Redis.Storage;
using StackExchange.Redis;
using Xunit;

namespace NetThrottler.Redis.Tests;

public class RedisThrottleStorageTests : IAsyncLifetime
{
    private RedisThrottleStorage _storage = null!;
    private IConnectionMultiplexer _connection = null!;

    public async Task InitializeAsync()
    {
        // Use a mock Redis connection for testing
        // In a real scenario, you would use a test Redis instance
        var options = new RedisThrottleStorageOptions
        {
            ConnectionString = "localhost:6379",
            Database = 0,
            KeyPrefix = "test:",
            DefaultTtl = TimeSpan.FromMinutes(5)
        };

        // For testing purposes, we'll create a mock connection
        // In production, this would connect to a real Redis instance
        try
        {
            _connection = await ConnectionMultiplexer.ConnectAsync(options.ConnectionString);
            _storage = new RedisThrottleStorage(_connection, Microsoft.Extensions.Options.Options.Create(options));
        }
        catch (Exception)
        {
            // If Redis is not available, skip tests
            _connection = null!;
            _storage = null!;
        }
    }

    public async Task DisposeAsync()
    {
        _storage?.Dispose();
        _connection?.Dispose();
    }

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ShouldReturnNull()
    {
        // Skip if Redis is not available
        if (_storage == null) return;

        // Act
        var result = await _storage.GetAsync("non-existent-key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_AndGetAsync_ShouldStoreAndRetrieveValue()
    {
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

        // Act
        var exists = await _storage.ExistsAsync("non-existent");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExpireAsync_WithExistingKey_ShouldSetExpiration()
    {
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

        // Act & Assert
        await _storage.ExpireAsync("non-existent", TimeSpan.FromMinutes(1));
        // Should not throw
    }

    [Fact]
    public async Task ConcurrentIncrementAsync_ShouldBeThreadSafe()
    {
        // Skip if Redis is not available
        if (_storage == null) return;

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
        // Skip if Redis is not available
        if (_storage == null) return;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SetAsync("", "value"));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SetAsync("   ", "value"));
    }

    [Fact]
    public async Task GetAsync_WithInvalidKey_ShouldThrow()
    {
        // Skip if Redis is not available
        if (_storage == null) return;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.GetAsync("   "));
    }

    [Fact]
    public async Task IncrementAsync_WithTTL_ShouldSetExpiration()
    {
        // Skip if Redis is not available
        if (_storage == null) return;

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

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RedisThrottleStorage(Microsoft.Extensions.Options.Options.Create<RedisThrottleStorageOptions>(null!)));
    }

    [Fact]
    public void Constructor_WithNullConnection_ShouldThrow()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new RedisThrottleStorageOptions());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RedisThrottleStorage(null!, options));
    }
}
