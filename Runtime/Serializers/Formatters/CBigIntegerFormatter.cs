using System;
using System.Numerics;
using System.Runtime.InteropServices;
using MemoryPack;
using UnityEngine.Scripting;

namespace CoffeeBean
{
    /// <summary>
    /// <see cref="BigInteger"/> 的 MemoryPack 格式化器。
    ///
    /// **为什么框架必须自带这一份**（而不是用 MemoryPack.Core 里的内建版）：
    /// MemoryPack.Core 确实有一个 <c>MemoryPack.Formatters.BigIntegerFormatter</c>，但它**不能用**。
    /// 上游源码里那段是：
    /// <code>
    /// #if !UNITY_2021_2_OR_NEWER
    ///     Span&lt;byte&gt; temp = stackalloc byte[255];
    ///     if (value.TryWriteBytes(temp, out var written))
    ///     {
    ///         writer.WriteUnmanagedSpan(temp.Slice(written));   // ← 应为 Slice(0, written)
    ///         return;
    ///     }
    /// #endif
    /// </code>
    /// <c>TryWriteBytes</c> 把数值写在缓冲区**开头**（written = 实际字节数），
    /// 而 <c>Slice(written)</c> 取的是「跳过前 written 个字节之后」的剩余部分 ——
    /// 于是一次 13 字节的数值会被写成「长度 242 + 242 个未初始化栈字节」。
    /// 该分支只在**未定义 `UNITY_2021_2_OR_NEWER`** 时参与编译：
    /// Unity 工程编译时走的是安全的 <c>ToByteArray()</c> 分支，
    /// 而 NuGet 那份 <c>MemoryPack.Core.dll</c> 是按 netstandard2.1 编译的、没有 Unity 宏，
    /// 于是把这条坏路径编进了 DLL。
    ///
    /// 实测（本工程内的 MemoryPack.Core 1.21.4 DLL）：
    /// <code>
    /// 数值 123456789012345678901234567890
    ///   本格式化器  : 17 B  = 4 字节长度(13) + ToByteArray()
    ///   DLL 内建版  : 246 B = 4 字节长度(242) + 242 个 0x00，且读回 = 0（自读不自洽）
    /// </code>
    ///
    /// 字节布局**与本工程历史上手写的那份完全一致**（4 字节长度 + <c>ToByteArray()</c>，
    /// 小端二进制补码、含符号字节），所以换用本类**不会**改变已经写出的存档格式。
    /// </summary>
    [Preserve]
    public sealed class CBigIntegerFormatter : MemoryPackFormatter<BigInteger>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref BigInteger value)
        {
            // 不用 stackalloc 那条快路径：它在 NuGet 包里是有缺陷的（见类型注释），
            // 而且它与历史存档布局不同。ToByteArray() 短小、正确、且与既有数据一致。
            byte[] bytes = value.ToByteArray();
            writer.WriteUnmanagedArray(bytes);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref BigInteger value)
        {
            if (!reader.TryReadCollectionHeader(out int length))
            {
                value = default;
                return;
            }

            ref byte src = ref reader.GetSpanReference(length);
            value = new BigInteger(MemoryMarshal.CreateReadOnlySpan(ref src, length));

            reader.Advance(length);
        }
    }
}
