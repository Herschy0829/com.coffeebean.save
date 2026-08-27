using System;
using System.Text;
using MemoryPack;

namespace CoffeeBean
{
    /// <summary>
    /// MemoryPack 序列化后端（默认）：高效二进制。
    /// 数据类需标记 <c>[MemoryPackable]</c>（建议 <c>GenerateType.VersionTolerant</c> 以便字段演进时兼容旧档）。
    /// 注意：VersionTolerant 模式要求每个成员显式标注 <c>[MemoryPackOrder(n)]</c>（否则生成器报 MEMPACK025）。
    /// 依赖 com.cysharp.memorypack（来源由消费工程提供：git 或本地副本）。
    /// </summary>
    public sealed class CMemoryPackSerializer : ISaveSerializer
    {
        public static CMemoryPackSerializer Instance { get; } = new CMemoryPackSerializer();

        public string Name => "memorypack";

        public byte[] Serialize<T>(T data)
            => MemoryPackSerializer.Serialize(data);

        public T Deserialize<T>(byte[] bytes)
            => MemoryPackSerializer.Deserialize<T>(bytes);
    }

    /// <summary>
    /// JSON 序列化后端（兜底）：基于 tools 的 CJson，调试可读、无需 MemoryPack 标记。
    /// 注意：JsonUtility 不支持 Dictionary / 多态 / 根数组（复杂结构请用 MemoryPack 后端）。
    /// </summary>
    public sealed class CJsonSerializer : ISaveSerializer
    {
        public static CJsonSerializer Instance { get; } = new CJsonSerializer();

        public string Name => "json";

        public byte[] Serialize<T>(T data)
            => Encoding.UTF8.GetBytes(CJson.ToJson(data));

        public T Deserialize<T>(byte[] bytes)
            => CJson.FromJson<T>(Encoding.UTF8.GetString(bytes));
    }
}
