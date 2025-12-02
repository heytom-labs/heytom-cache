using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using StackExchange.Redis;

namespace Heytom.Cache;

/// <summary>
/// Redis 弹性策略配置
/// 提供重试策略和断路器模式
/// </summary>
internal static class ResiliencePolicies
{
    // 策略配置常量
    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMs = 100;
    private const double CircuitBreakerFailureRatio = 0.5;
    private const int CircuitBreakerSamplingDurationSeconds = 10;
    private const int CircuitBreakerMinimumThroughput = 5;
    private const int CircuitBreakerBreakDurationSeconds = 30;

    /// <summary>
    /// 创建异步重试策略
    /// 使用指数退避，最多重试 3 次
    /// </summary>
    public static ResiliencePipeline<T> CreateAsyncRetryPolicy<T>()
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(CreateRetryOptions<T>())
            .Build();
    }

    /// <summary>
    /// 创建同步重试策略
    /// 使用指数退避，最多重试 3 次
    /// </summary>
    public static ResiliencePipeline CreateSyncRetryPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(CreateRetryOptions())
            .Build();
    }

    /// <summary>
    /// 创建异步断路器策略
    /// 连续失败 5 次后打开断路器，持续 30 秒
    /// </summary>
    public static ResiliencePipeline<T> CreateAsyncCircuitBreakerPolicy<T>()
    {
        return new ResiliencePipelineBuilder<T>()
            .AddCircuitBreaker(CreateCircuitBreakerOptions<T>())
            .Build();
    }

    /// <summary>
    /// 创建同步断路器策略
    /// 连续失败 5 次后打开断路器，持续 30 秒
    /// </summary>
    public static ResiliencePipeline CreateSyncCircuitBreakerPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(CreateCircuitBreakerOptions())
            .Build();
    }

    /// <summary>
    /// 创建组合策略（重试 + 断路器）
    /// </summary>
    public static ResiliencePipeline<T> CreateAsyncCombinedPolicy<T>()
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(CreateRetryOptions<T>())
            .AddCircuitBreaker(CreateCircuitBreakerOptions<T>())
            .Build();
    }

    /// <summary>
    /// 创建组合策略（重试 + 断路器）- 同步版本
    /// </summary>
    public static ResiliencePipeline CreateSyncCombinedPolicy()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(CreateRetryOptions())
            .AddCircuitBreaker(CreateCircuitBreakerOptions())
            .Build();
    }

    /// <summary>
    /// 创建重试选项（泛型版本）
    /// </summary>
    private static RetryStrategyOptions<T> CreateRetryOptions<T>()
    {
        var predicateBuilder = new PredicateBuilder<T>();
        ConfigureExceptionHandling(predicateBuilder);
        
        return new RetryStrategyOptions<T>
        {
            MaxRetryAttempts = MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(RetryDelayMs),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = predicateBuilder
        };
    }

    /// <summary>
    /// 创建重试选项（非泛型版本）
    /// </summary>
    private static RetryStrategyOptions CreateRetryOptions()
    {
        var predicateBuilder = new PredicateBuilder();
        ConfigureExceptionHandling(predicateBuilder);
        
        return new RetryStrategyOptions
        {
            MaxRetryAttempts = MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(RetryDelayMs),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = predicateBuilder
        };
    }

    /// <summary>
    /// 创建断路器选项（泛型版本）
    /// </summary>
    private static CircuitBreakerStrategyOptions<T> CreateCircuitBreakerOptions<T>()
    {
        var predicateBuilder = new PredicateBuilder<T>();
        ConfigureExceptionHandling(predicateBuilder);
        
        return new CircuitBreakerStrategyOptions<T>
        {
            FailureRatio = CircuitBreakerFailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(CircuitBreakerSamplingDurationSeconds),
            MinimumThroughput = CircuitBreakerMinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(CircuitBreakerBreakDurationSeconds),
            ShouldHandle = predicateBuilder
        };
    }

    /// <summary>
    /// 创建断路器选项（非泛型版本）
    /// </summary>
    private static CircuitBreakerStrategyOptions CreateCircuitBreakerOptions()
    {
        var predicateBuilder = new PredicateBuilder();
        ConfigureExceptionHandling(predicateBuilder);
        
        return new CircuitBreakerStrategyOptions
        {
            FailureRatio = CircuitBreakerFailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(CircuitBreakerSamplingDurationSeconds),
            MinimumThroughput = CircuitBreakerMinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(CircuitBreakerBreakDurationSeconds),
            ShouldHandle = predicateBuilder
        };
    }

    /// <summary>
    /// 配置异常处理谓词（泛型版本）
    /// </summary>
    private static void ConfigureExceptionHandling<T>(PredicateBuilder<T> builder)
    {
        builder.Handle<RedisConnectionException>()
               .Handle<RedisTimeoutException>()
               .Handle<TimeoutException>();
    }

    /// <summary>
    /// 配置异常处理谓词（非泛型版本）
    /// </summary>
    private static void ConfigureExceptionHandling(PredicateBuilder builder)
    {
        builder.Handle<RedisConnectionException>()
               .Handle<RedisTimeoutException>()
               .Handle<TimeoutException>();
    }
}
