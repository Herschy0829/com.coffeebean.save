using System;
using MemoryPack;
using UniRx;
using UnityEngine.Scripting;

namespace CoffeeBean
{
    /// <summary>
    /// UniRx <see cref="ReactiveProperty{T}"/> 的 MemoryPack 格式化器。
    ///
    /// 为什么框架要提供：<see cref="ReactiveProperty{T}"/> 是外部库类型，既不是 `[MemoryPackable] partial`，
    /// MemoryPack 也没有内建格式化器（实测 MemoryPack.Core 里 ReactiveProperty 相关类型为 0 个），
    /// 所以不注册就无法序列化。
    ///
    /// ⚠️ 字节布局与既有工程里手写的版本保持一致（对象头 1 + 内部值），
    /// 换用本类**不会**改变已写出的存档格式。
    /// </summary>
    /// <typeparam name="TValue">ReactiveProperty 内部值类型。</typeparam>
    [Preserve]
    public class CReactivePropertyFormatter<TValue> : MemoryPackFormatter<ReactiveProperty<TValue>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref ReactiveProperty<TValue> value)
        {
            if (value == null)
            {
                writer.WriteNullObjectHeader();
                return;
            }

            writer.WriteObjectHeader(1);
            writer.WriteValue(value.Value);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref ReactiveProperty<TValue> value)
        {
            // 注意：TryReadObjectHeader 的输出参数是 byte（对象头 1 字节），不是 int
            if (!reader.TryReadObjectHeader(out byte count))
            {
                value = null;
                return;
            }

            if (count != 1)
                MemoryPackSerializationException.ThrowInvalidPropertyCount(1, count);

            TValue inner = reader.ReadValue<TValue>();
            if (value == null) value = new ReactiveProperty<TValue>(inner);
            else value.Value = inner;
        }
    }

    /// <summary>UniRx <see cref="IntReactiveProperty"/> 的格式化器（复用泛型实现，反序列化后保持派生类型）。</summary>
    [Preserve]
    public sealed class CIntReactivePropertyFormatter : MemoryPackFormatter<IntReactiveProperty>
    {
        private static readonly CReactivePropertyFormatter<int> Inner = new CReactivePropertyFormatter<int>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref IntReactiveProperty value)
        {
            ReactiveProperty<int> temp = value;
            Inner.Serialize(ref writer, ref temp);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref IntReactiveProperty value)
        {
            ReactiveProperty<int> temp = value;
            Inner.Deserialize(ref reader, ref temp);
            value = temp as IntReactiveProperty ?? (temp != null ? new IntReactiveProperty(temp.Value) : null);
        }
    }

    /// <summary>UniRx <see cref="LongReactiveProperty"/> 的格式化器。</summary>
    [Preserve]
    public sealed class CLongReactivePropertyFormatter : MemoryPackFormatter<LongReactiveProperty>
    {
        private static readonly CReactivePropertyFormatter<long> Inner = new CReactivePropertyFormatter<long>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref LongReactiveProperty value)
        {
            ReactiveProperty<long> temp = value;
            Inner.Serialize(ref writer, ref temp);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref LongReactiveProperty value)
        {
            ReactiveProperty<long> temp = value;
            Inner.Deserialize(ref reader, ref temp);
            value = temp as LongReactiveProperty ?? (temp != null ? new LongReactiveProperty(temp.Value) : null);
        }
    }

    /// <summary>UniRx <see cref="BoolReactiveProperty"/> 的格式化器。</summary>
    [Preserve]
    public sealed class CBoolReactivePropertyFormatter : MemoryPackFormatter<BoolReactiveProperty>
    {
        private static readonly CReactivePropertyFormatter<bool> Inner = new CReactivePropertyFormatter<bool>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref BoolReactiveProperty value)
        {
            ReactiveProperty<bool> temp = value;
            Inner.Serialize(ref writer, ref temp);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref BoolReactiveProperty value)
        {
            ReactiveProperty<bool> temp = value;
            Inner.Deserialize(ref reader, ref temp);
            value = temp as BoolReactiveProperty ?? (temp != null ? new BoolReactiveProperty(temp.Value) : null);
        }
    }

    /// <summary>UniRx <see cref="FloatReactiveProperty"/> 的格式化器。</summary>
    [Preserve]
    public sealed class CFloatReactivePropertyFormatter : MemoryPackFormatter<FloatReactiveProperty>
    {
        private static readonly CReactivePropertyFormatter<float> Inner = new CReactivePropertyFormatter<float>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref FloatReactiveProperty value)
        {
            ReactiveProperty<float> temp = value;
            Inner.Serialize(ref writer, ref temp);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref FloatReactiveProperty value)
        {
            ReactiveProperty<float> temp = value;
            Inner.Deserialize(ref reader, ref temp);
            value = temp as FloatReactiveProperty ?? (temp != null ? new FloatReactiveProperty(temp.Value) : null);
        }
    }

    /// <summary>UniRx <see cref="DoubleReactiveProperty"/> 的格式化器。</summary>
    [Preserve]
    public sealed class CDoubleReactivePropertyFormatter : MemoryPackFormatter<DoubleReactiveProperty>
    {
        private static readonly CReactivePropertyFormatter<double> Inner = new CReactivePropertyFormatter<double>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref DoubleReactiveProperty value)
        {
            ReactiveProperty<double> temp = value;
            Inner.Serialize(ref writer, ref temp);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref DoubleReactiveProperty value)
        {
            ReactiveProperty<double> temp = value;
            Inner.Deserialize(ref reader, ref temp);
            value = temp as DoubleReactiveProperty ?? (temp != null ? new DoubleReactiveProperty(temp.Value) : null);
        }
    }

    /// <summary>UniRx <see cref="StringReactiveProperty"/> 的格式化器。</summary>
    [Preserve]
    public sealed class CStringReactivePropertyFormatter : MemoryPackFormatter<StringReactiveProperty>
    {
        private static readonly CReactivePropertyFormatter<string> Inner = new CReactivePropertyFormatter<string>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref StringReactiveProperty value)
        {
            ReactiveProperty<string> temp = value;
            Inner.Serialize(ref writer, ref temp);
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref StringReactiveProperty value)
        {
            ReactiveProperty<string> temp = value;
            Inner.Deserialize(ref reader, ref temp);
            value = temp as StringReactiveProperty ?? (temp != null ? new StringReactiveProperty(temp.Value) : null);
        }
    }
}
