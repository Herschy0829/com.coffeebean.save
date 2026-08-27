namespace CoffeeBean
{
    /// <summary>
    /// 存档序列化后端（可插拔）：数据对象 ↔ 字节。
    /// 默认 <see cref="CMemoryPackSerializer"/>（高效二进制，对齐项目现状）；
    /// 兜底 <see cref="CJsonSerializer"/>（JSON，调试可读）。
    /// </summary>
    public interface ISaveSerializer
    {
        /// <summary>后端名称（日志 / 调试用）。</summary>
        string Name { get; }

        /// <summary>序列化为字节。</summary>
        byte[] Serialize<T>(T data);

        /// <summary>反序列化；失败抛异常（由调用方容错处理）。</summary>
        T Deserialize<T>(byte[] bytes);
    }
}
