namespace Heytom.Cache;

/// <summary>
/// 序列化器接口
/// </summary>
public interface ISerializer
{
    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    byte[] Serialize<T>(T value);
    
    /// <summary>
    /// 反序列化字节数组为对象
    /// </summary>
    T? Deserialize<T>(byte[] data);
}
