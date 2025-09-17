# NetThrottler - Advanced Rate Limiting Library

🚀 A high-performance, enterprise-grade .NET rate limiting and throttling library with support for multiple algorithms and distributed scenarios.

## 🔧 Features

- **Multiple Algorithms**: Token Bucket, Leaky Bucket, Fixed Window, Sliding Window
- **Thread-Safe**: Optimized for high-concurrency scenarios  
- **Distributed Support**: Redis and SQL Server backends
- **ASP.NET Core Integration**: Easy-to-use middleware
- **HttpClient Support**: Built-in client-side rate limiting
- **Monitoring**: Built-in metrics and health checks
- **Professional Quality**: Zero compilation errors/warnings

## 📦 Installation

```bash
dotnet add package NetThrottler.Core
dotnet add package NetThrottler.AspNetCore
dotnet add package NetThrottler.HttpClient
dotnet add package NetThrottler.Redis
```

## 🚀 Quick Start

```csharp
var storage = new MemoryThrottleStorage();
var policy = new TokenBucketPolicy("api", capacity: 100, refillRatePerSecond: 10, storage);

var isAllowed = await policy.TryAcquireAsync("user123", permits: 1);
if (isAllowed)
{
    Console.WriteLine("✅ Request allowed");
}
else
{
    Console.WriteLine("❌ Rate limit exceeded");
}
```

## 🏗️ Repository Structure

- **src/**: Core libraries and integrations
- **tests/**: Unit, integration, and performance tests  
- **samples/**: Working examples
- **.github/workflows/**: CI/CD pipelines

## 📊 Quality Standards

- ✅ Zero compilation errors
- ✅ Zero compilation warnings  
- ✅ Clean Architecture (SOLID, DRY, KISS)
- ✅ Enterprise-grade security
- ✅ Comprehensive testing
- ✅ Professional documentation

Built with passion for high-quality software engineering.