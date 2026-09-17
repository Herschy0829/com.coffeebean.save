using System;
using System.Collections.Generic;
using System.Numerics;
using MemoryPack;
using NUnit.Framework;

namespace CoffeeBean.Save.Tests
{
    /// <summary>
    /// BigInteger 格式化器测试。
    ///
    /// 背景（为什么这些用例值得存在）：MemoryPack.Core **确实**带一个
    /// <c>MemoryPack.Formatters.BigIntegerFormatter</c>，但 NuGet 那份 DLL 把它上游一条
    /// 有缺陷的分支编了进去（<c>temp.Slice(written)</c>，应为 <c>Slice(0, written)</c>），
    /// 于是 13 字节的数值会被写成「长度 242 + 242 个栈垃圾字节」，且**自读不自洽**（读回 0）。
    /// 实测：同一个数值，内建版 246 B 且读回 0，本模块版 17 B 且读回正确。
    /// 所以这里的用例既验证功能，也**锁死字节布局**，防止哪天又退回内建版。
    /// </summary>
    public class BigIntegerFormatterTests
    {
        private static readonly string[] Values =
        {
            "0",
            "1",
            "-1",
            "127",
            "128",
            "-128",
            "123456789012345678901234567890",
            "-123456789012345678901234567890",
        };

        [Test]
        public void BigInteger_RoundTrip_AllValues()
        {
            CSaveFormatters.RegisterAll(); // 幂等

            foreach (string text in Values)
            {
                var value = BigInteger.Parse(text);
                byte[] bytes = MemoryPackSerializer.Serialize<BigInteger>(value);
                BigInteger restored = MemoryPackSerializer.Deserialize<BigInteger>(bytes);
                Assert.AreEqual(value, restored, $"BigInteger {text} 往返失败");
            }
        }

        /// <summary>
        /// 线格式回归守卫：必须恰好是「4 字节小端长度 + ToByteArray()」。
        /// 一旦有人换用 MemoryPack 内建版或改回 stackalloc 快路径，长度会立刻对不上（17 → 246）。
        /// </summary>
        [Test]
        public void BigInteger_WireFormat_IsLengthPrefixedToByteArray()
        {
            CSaveFormatters.RegisterAll();

            var value = BigInteger.Parse("123456789012345678901234567890");
            byte[] raw = value.ToByteArray();

            byte[] encoded = MemoryPackSerializer.Serialize<BigInteger>(value);

            Assert.AreEqual(4 + raw.Length, encoded.Length,
                "布局应为「4 字节长度 + ToByteArray()」；长度不符通常是又用了 MemoryPack 内建版");

            int length = encoded[0] | (encoded[1] << 8) | (encoded[2] << 16) | (encoded[3] << 24);
            Assert.AreEqual(raw.Length, length, "长度前缀应为实际字节数（小端）");

            for (int i = 0; i < raw.Length; i++)
            {
                Assert.AreEqual(raw[i], encoded[4 + i], $"第 {i} 个数据字节不一致");
            }
        }

        /// <summary>生效的必须是本模块的实现，而不是 MemoryPack 内建版。</summary>
        [Test]
        public void BigInteger_LiveFormatter_IsOurImplementation()
        {
            CSaveFormatters.RegisterAll();

            // GetFormatter<T> 是 internal，用序列化行为间接判定：内建版会写出远大于 4+ToByteArray 的长度
            var value = BigInteger.Parse("123456789012345678901234567890");
            byte[] encoded = MemoryPackSerializer.Serialize<BigInteger>(value);

            Assert.Less(encoded.Length, 64,
                "生效的似乎是 MemoryPack.Core 的内建版（它会写出 246 B 的垃圾数据）");
        }

        [Test]
        public void BigInteger_NegativeAndZero_KeepSign()
        {
            CSaveFormatters.RegisterAll();

            var zero = BigInteger.Zero;
            Assert.AreEqual(BigInteger.Zero, RoundTrip(ref zero));

            var minusOne = BigInteger.MinusOne;
            Assert.AreEqual(BigInteger.MinusOne, RoundTrip(ref minusOne));

            var big = BigInteger.Parse("-9876543210987654321098765432109876543210");
            Assert.AreEqual(big, RoundTrip(ref big));
        }

        [Test]
        public void BigInteger_InDictionary_RoundTrip()
        {
            CSaveFormatters.RegisterAll();

            var dict = new Dictionary<int, BigInteger>
            {
                [1] = BigInteger.Parse("99999999999999999999999999"),
                [2] = -5,
                [3] = BigInteger.Zero,
            };

            byte[] bytes = MemoryPackSerializer.Serialize(dict);
            Dictionary<int, BigInteger> restored =
                MemoryPackSerializer.Deserialize<Dictionary<int, BigInteger>>(bytes);

            Assert.AreEqual(3, restored.Count);
            Assert.AreEqual(dict[1], restored[1]);
            Assert.AreEqual(dict[2], restored[2]);
            Assert.AreEqual(dict[3], restored[3]);
        }

        [Test]
        public void BigInteger_InsideMemoryPackableClass_RoundTrip()
        {
            CSaveFormatters.RegisterAll();

            var data = new BigIntHolder
            {
                Gold = BigInteger.Parse("123456789012345678901234567890"),
                Debt = BigInteger.Parse("-42"),
                Resources = new Dictionary<int, BigInteger> { [7] = BigInteger.Parse("1000000000000000000000") },
            };

            byte[] bytes = MemoryPackSerializer.Serialize(data);
            BigIntHolder restored = MemoryPackSerializer.Deserialize<BigIntHolder>(bytes);

            Assert.AreEqual(data.Gold, restored.Gold);
            Assert.AreEqual(data.Debt, restored.Debt);
            Assert.AreEqual(data.Resources[7], restored.Resources[7]);
        }

        [Test]
        public void RegisterAll_IsIdempotent()
        {
            CSaveFormatters.RegisterAll();
            CSaveFormatters.RegisterAll();
            Assert.IsTrue(CSaveFormatters.IsRegistered);
        }

        private static BigInteger RoundTrip(ref BigInteger value)
        {
            byte[] bytes = MemoryPackSerializer.Serialize<BigInteger>(value);
            return MemoryPackSerializer.Deserialize<BigInteger>(bytes);
        }
    }

    /// <summary>模拟业务里「[MemoryPackable] 类持有 BigInteger 字段」的形态。</summary>
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class BigIntHolder
    {
        [MemoryPackOrder(0)] public BigInteger Gold;
        [MemoryPackOrder(1)] public BigInteger Debt;
        [MemoryPackOrder(2)] public Dictionary<int, BigInteger> Resources;
    }
}
