using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NetThrottler.Core.Interfaces;
using NetThrottler.Core.Policies;
using NetThrottler.Core.Storage;
using Xunit;

namespace NetThrottler.Core.Tests;

public class PolicyFactoryTests : IDisposable
{
    private readonly MemoryThrottleStorage _storage;
    private readonly PolicyFactory _factory;

    public PolicyFactoryTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _storage = new MemoryThrottleStorage(memoryCache);
        _factory = new PolicyFactory(_storage);
    }

    [Fact]
    public void CreatePolicy_WithTokenBucketConfig_ShouldReturnTokenBucketPolicy()
    {
        // Arrange
        var config = new PolicyConfiguration(
            "test-policy",
            "TokenBucket",
            10,
            TimeSpan.FromMinutes(1),
            new Dictionary<string, object>
            {
                ["Capacity"] = 10.0,
                ["RefillRatePerSecond"] = 2.0
            });

        // Act
        var policy = _factory.CreatePolicy(config);

        // Assert
        policy.Should().BeOfType<TokenBucketPolicy>();
        policy.Name.Should().Be("test-policy");
        policy.Algorithm.Should().Be("TokenBucket");
        policy.MaxRequests.Should().Be(10);
    }

    [Fact]
    public void CreatePolicy_WithBasicParameters_ShouldCreateTokenBucketPolicy()
    {
        // Act
        var policy = _factory.CreatePolicy("test", "TokenBucket", 5, TimeSpan.FromSeconds(30));

        // Assert
        policy.Should().BeOfType<TokenBucketPolicy>();
        policy.Name.Should().Be("test");
        policy.Algorithm.Should().Be("TokenBucket");
        policy.MaxRequests.Should().Be(5);
    }

    [Fact]
    public void CreateTokenBucketPolicy_WithParameters_ShouldCreateCorrectPolicy()
    {
        // Act
        var policy = _factory.CreateTokenBucketPolicy("tb-policy", 15.0, 3.0);

        // Assert
        policy.Should().BeOfType<TokenBucketPolicy>();
        policy.Name.Should().Be("tb-policy");
        policy.Capacity.Should().Be(15.0);
        policy.RefillRatePerSecond.Should().Be(3.0);
    }

    [Fact]
    public void CreatePolicyFromConfig_WithValidConfig_ShouldCreatePolicy()
    {
        // Arrange
        var config = new Dictionary<string, object>
        {
            ["Algorithm"] = "TokenBucket",
            ["MaxRequests"] = 20,
            ["WindowSeconds"] = 60,
            ["Capacity"] = 20.0,
            ["RefillRatePerSecond"] = 1.0
        };

        // Act
        var policy = _factory.CreatePolicyFromConfig("config-policy", config);

        // Assert
        policy.Should().BeOfType<TokenBucketPolicy>();
        policy.Name.Should().Be("config-policy");
        policy.Algorithm.Should().Be("TokenBucket");
        policy.MaxRequests.Should().Be(20);
    }

    [Fact]
    public void CreatePolicy_WithUnsupportedAlgorithm_ShouldThrow()
    {
        // Arrange
        var config = new PolicyConfiguration(
            "test",
            "UnsupportedAlgorithm",
            10,
            TimeSpan.FromMinutes(1));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _factory.CreatePolicy(config));
    }

    [Fact]
    public void CreatePolicy_WithNullConfig_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _factory.CreatePolicy(null!));
    }

    [Fact]
    public void CreatePolicyFromConfig_WithMissingRequiredKey_ShouldThrow()
    {
        // Arrange
        var config = new Dictionary<string, object>
        {
            ["MaxRequests"] = 10,
            ["WindowSeconds"] = 60
            // Missing Algorithm
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _factory.CreatePolicyFromConfig("test", config));
    }

    [Fact]
    public void CreatePolicyFromConfig_WithInvalidValue_ShouldThrow()
    {
        // Arrange
        var config = new Dictionary<string, object>
        {
            ["Algorithm"] = "TokenBucket",
            ["MaxRequests"] = "invalid-number", // Should be int
            ["WindowSeconds"] = 60
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _factory.CreatePolicyFromConfig("test", config));
    }

    [Fact]
    public void CreatePolicyFromConfig_WithNullName_ShouldThrow()
    {
        // Arrange
        var config = new Dictionary<string, object>
        {
            ["Algorithm"] = "TokenBucket",
            ["MaxRequests"] = 10,
            ["WindowSeconds"] = 60
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _factory.CreatePolicyFromConfig("", config));
        Assert.Throws<ArgumentException>(() => _factory.CreatePolicyFromConfig("   ", config));
    }

    [Fact]
    public void CreatePolicyFromConfig_WithNullConfig_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _factory.CreatePolicyFromConfig("test", null!));
    }

    [Fact]
    public void CreatePolicy_WithLoggerFactory_ShouldCreateLogger()
    {
        // Arrange
        var loggerFactory = new Mock<ILoggerFactory>();
        var logger = new Mock<ILogger<TokenBucketPolicy>>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(logger.Object);

        var factory = new PolicyFactory(_storage, loggerFactory.Object);

        // Act
        var policy = factory.CreateTokenBucketPolicy("logged-policy", 5.0, 1.0);

        // Assert
        policy.Should().BeOfType<TokenBucketPolicy>();
        loggerFactory.Verify(x => x.CreateLogger("NetThrottler.Core.Policies.TokenBucketPolicy"), Times.Once);
    }

    [Fact]
    public void CreatePolicy_WithNullStorage_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PolicyFactory(null!));
    }

    public void Dispose()
    {
        _storage.Dispose();
    }
}
