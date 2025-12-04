using Microsoft.Extensions.Caching.Distributed;

namespace Heytom.Cache;

/// <summary>
/// 混合分布式缓存接口
/// 整合了 IDistributedCache、IRedisExtensions 和泛型类型操作
/// </summary>
public interface IHybridDistributedCache : IDistributedCache, IRedisExtensions, IDisposable
{
    #region Generic Type Operations

    /// <summary>
    /// 获取强类型缓存值（同步）
    /// </summary>
    /// <typeparam name="T">缓存值的类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <returns>缓存值，如果不存在则返回 default(T)</returns>
    T? Get<T>(string key);

    /// <summary>
    /// 异步获取强类型缓存值
    /// </summary>
    /// <typeparam name="T">缓存值的类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="token">取消令牌</param>
    /// <returns>缓存值，如果不存在则返回 default(T)</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken token = default);

    /// <summary>
    /// 设置强类型缓存值（同步）
    /// </summary>
    /// <typeparam name="T">缓存值的类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="options">缓存选项（可选）</param>
    void Set<T>(string key, T value, DistributedCacheEntryOptions? options = null);

    /// <summary>
    /// 异步设置强类型缓存值
    /// </summary>
    /// <typeparam name="T">缓存值的类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="options">缓存选项（可选）</param>
    /// <param name="token">取消令牌</param>
    Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default);

    /// <summary>
    /// 获取或设置缓存值（同步）
    /// 如果缓存不存在，则执行工厂方法并缓存结果
    /// </summary>
    /// <typeparam name="T">缓存值的类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="factory">当缓存不存在时用于生成值的工厂方法</param>
    /// <param name="options">缓存选项（可选）</param>
    /// <returns>缓存值</returns>
    T GetOrSet<T>(string key, Func<T> factory, DistributedCacheEntryOptions? options = null);

    /// <summary>
    /// 异步获取或设置缓存值
    /// 如果缓存不存在，则执行工厂方法并缓存结果
    /// </summary>
    /// <typeparam name="T">缓存值的类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="factory">当缓存不存在时用于生成值的异步工厂方法</param>
    /// <param name="options">缓存选项（可选）</param>
    /// <param name="token">取消令牌</param>
    /// <returns>缓存值</returns>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, DistributedCacheEntryOptions? options = null, CancellationToken token = default);

    #endregion

    #region Metrics

    /// <summary>
    /// 获取指标收集器实例（如果启用了指标收集）
    /// </summary>
    MetricsCollector? MetricsCollector { get; }

    /// <summary>
    /// 获取缓存性能指标
    /// </summary>
    /// <returns>缓存指标，如果未启用指标收集则返回空指标</returns>
    CacheMetrics GetMetrics();

    /// <summary>
    /// 重置所有性能指标
    /// </summary>
    void ResetMetrics();

    /// <summary>
    /// 检查本地缓存是否启用
    /// </summary>
    /// <returns>如果本地缓存启用则返回 true，否则返回 false</returns>
    bool IsLocalCacheEnabled();

    #endregion
}
