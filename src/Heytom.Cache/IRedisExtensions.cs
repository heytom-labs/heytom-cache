namespace Heytom.Cache;

/// <summary>
/// Redis 扩展功能接口，提供 Redis 特有的数据结构操作
/// </summary>
public interface IRedisExtensions
{
    // Hash operations
    Task<bool> HashSetAsync(string key, string field, byte[] value);
    Task<byte[]?> HashGetAsync(string key, string field);
    Task<Dictionary<string, byte[]>> HashGetAllAsync(string key);
    Task<bool> HashDeleteAsync(string key, string field);
    
    // List operations
    Task<long> ListPushAsync(string key, byte[] value);
    Task<byte[]?> ListPopAsync(string key);
    Task<long> ListLengthAsync(string key);
    
    // Set operations
    Task<bool> SetAddAsync(string key, byte[] value);
    Task<bool> SetRemoveAsync(string key, byte[] value);
    Task<byte[][]> SetMembersAsync(string key);
    
    // Sorted Set operations
    Task<bool> SortedSetAddAsync(string key, byte[] value, double score);
    Task<byte[][]> SortedSetRangeByScoreAsync(string key, double min, double max);
    
    // Pub/Sub
    Task PublishAsync(string channel, byte[] message);
    Task SubscribeAsync(string channel, Action<byte[]> handler);
}
