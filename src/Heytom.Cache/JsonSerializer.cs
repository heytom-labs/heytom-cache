using System.Text.Json;

namespace Heytom.Cache;

/// <summary>
/// 基于 System.Text.Json 的默认序列化器
/// </summary>
public class JsonSerializer : ISerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 初始化 JsonSerializer 实例
    /// </summary>
    public JsonSerializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };
    }

    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <returns>序列化后的字节数组</returns>
    public byte[] Serialize<T>(T value)
    {
        if (value == null)
        {
            return [];
        }
        
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, _options);
    }

    /// <summary>
    /// 反序列化字节数组为对象
    /// </summary>
    /// <typeparam name="T">目标对象类型</typeparam>
    /// <param name="data">要反序列化的字节数组</param>
    /// <returns>反序列化后的对象</returns>
    public T? Deserialize<T>(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return default;
        }
        
        return System.Text.Json.JsonSerializer.Deserialize<T>(data, _options);
    }
}
