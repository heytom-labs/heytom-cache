namespace Heytom.Cache;

/// <summary>
/// Redis 扩展功能接口，提供 Redis 特有的数据结构操作
/// </summary>
public interface IRedisExtensions
{
    // Hash operations

    /// <summary>
    /// 在 Redis Hash 中设置字段值
    /// </summary>
    /// <param name="key">Hash 键</param>
    /// <param name="field">字段名</param>
    /// <param name="value">字段值</param>
    /// <returns>如果字段是新创建的返回 true，如果字段已存在并被更新返回 false</returns>
    Task<bool> HashSetAsync(string key, string field, byte[] value);

    /// <summary>
    /// 从 Redis Hash 中获取字段值
    /// </summary>
    /// <param name="key">Hash 键</param>
    /// <param name="field">字段名</param>
    /// <returns>字段值，如果字段不存在则返回 null</returns>
    Task<byte[]?> HashGetAsync(string key, string field);

    /// <summary>
    /// 获取 Redis Hash 中的所有字段和值
    /// </summary>
    /// <param name="key">Hash 键</param>
    /// <returns>包含所有字段和值的字典</returns>
    Task<Dictionary<string, byte[]>> HashGetAllAsync(string key);

    /// <summary>
    /// 从 Redis Hash 中删除字段
    /// </summary>
    /// <param name="key">Hash 键</param>
    /// <param name="field">字段名</param>
    /// <returns>如果字段被删除返回 true，如果字段不存在返回 false</returns>
    Task<bool> HashDeleteAsync(string key, string field);

    // List operations

    /// <summary>
    /// 将值推入 Redis List 的头部
    /// </summary>
    /// <param name="key">List 键</param>
    /// <param name="value">要推入的值</param>
    /// <returns>推入后 List 的长度</returns>
    Task<long> ListPushAsync(string key, byte[] value);

    /// <summary>
    /// 从 Redis List 的头部弹出值
    /// </summary>
    /// <param name="key">List 键</param>
    /// <returns>弹出的值，如果 List 为空则返回 null</returns>
    Task<byte[]?> ListPopAsync(string key);

    /// <summary>
    /// 获取 Redis List 的长度
    /// </summary>
    /// <param name="key">List 键</param>
    /// <returns>List 的长度</returns>
    Task<long> ListLengthAsync(string key);

    // Set operations

    /// <summary>
    /// 向 Redis Set 中添加成员
    /// </summary>
    /// <param name="key">Set 键</param>
    /// <param name="value">要添加的成员</param>
    /// <returns>如果成员是新添加的返回 true，如果成员已存在返回 false</returns>
    Task<bool> SetAddAsync(string key, byte[] value);

    /// <summary>
    /// 从 Redis Set 中移除成员
    /// </summary>
    /// <param name="key">Set 键</param>
    /// <param name="value">要移除的成员</param>
    /// <returns>如果成员被移除返回 true，如果成员不存在返回 false</returns>
    Task<bool> SetRemoveAsync(string key, byte[] value);

    /// <summary>
    /// 获取 Redis Set 中的所有成员
    /// </summary>
    /// <param name="key">Set 键</param>
    /// <returns>包含所有成员的数组</returns>
    Task<byte[][]> SetMembersAsync(string key);

    // Sorted Set operations

    /// <summary>
    /// 向 Redis Sorted Set 中添加成员及其分数
    /// </summary>
    /// <param name="key">Sorted Set 键</param>
    /// <param name="value">要添加的成员</param>
    /// <param name="score">成员的分数</param>
    /// <returns>如果成员是新添加的返回 true，如果成员已存在并更新了分数返回 false</returns>
    Task<bool> SortedSetAddAsync(string key, byte[] value, double score);

    /// <summary>
    /// 根据分数范围获取 Redis Sorted Set 中的成员
    /// </summary>
    /// <param name="key">Sorted Set 键</param>
    /// <param name="min">最小分数（包含）</param>
    /// <param name="max">最大分数（包含）</param>
    /// <returns>包含指定分数范围内所有成员的数组</returns>
    Task<byte[][]> SortedSetRangeByScoreAsync(string key, double min, double max);

    // Pub/Sub

    /// <summary>
    /// 向 Redis 频道发布消息
    /// </summary>
    /// <param name="channel">频道名称</param>
    /// <param name="message">要发布的消息</param>
    /// <returns>表示异步操作的任务</returns>
    Task PublishAsync(string channel, byte[] message);

    /// <summary>
    /// 订阅 Redis 频道并处理接收到的消息
    /// </summary>
    /// <param name="channel">频道名称</param>
    /// <param name="handler">消息处理器</param>
    /// <returns>表示异步操作的任务</returns>
    Task SubscribeAsync(string channel, Action<byte[]> handler);
}
