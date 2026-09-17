using MemoryPack;
using UniRx;
using UnityEngine.Scripting;

namespace CoffeeBean
{
    /// <summary>
    /// UniRx <see cref="ReactiveCollection{T}"/> 的 MemoryPack 格式化器。
    ///
    /// 字节布局与既有工程手写版本一致：对象头 1 + 集合头（元素数）+ 逐个元素。
    /// </summary>
    [Preserve]
    public sealed class CReactiveCollectionFormatter<T> : MemoryPackFormatter<ReactiveCollection<T>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref ReactiveCollection<T> value)
        {
            if (value == null)
            {
                writer.WriteNullObjectHeader();
                return;
            }

            writer.WriteObjectHeader(1);
            writer.WriteCollectionHeader(value.Count);
            foreach (T item in value)
            {
                writer.WriteValue(item);
            }
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref ReactiveCollection<T> value)
        {
            if (!reader.TryReadObjectHeader(out _))
            {
                value = null;
                return;
            }

            if (!reader.TryReadCollectionHeader(out int count))
            {
                value = new ReactiveCollection<T>();
                return;
            }

            value ??= new ReactiveCollection<T>();
            value.Clear();
            for (int i = 0; i < count; i++)
            {
                value.Add(reader.ReadValue<T>());
            }
        }
    }

    /// <summary>
    /// UniRx <see cref="ReactiveDictionary{TKey,TValue}"/> 的 MemoryPack 格式化器。
    ///
    /// 字节布局与既有工程手写版本一致：对象头 1 + 集合头（元素数）+ 逐个 键/值。
    /// </summary>
    [Preserve]
    public sealed class CReactiveDictionaryFormatter<TKey, TValue> : MemoryPackFormatter<ReactiveDictionary<TKey, TValue>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref ReactiveDictionary<TKey, TValue> value)
        {
            if (value == null)
            {
                writer.WriteNullObjectHeader();
                return;
            }

            writer.WriteObjectHeader(1);
            writer.WriteCollectionHeader(value.Count);
            foreach (var pair in value)
            {
                writer.WriteValue(pair.Key);
                writer.WriteValue(pair.Value);
            }
        }

        [Preserve]
        public override void Deserialize(ref MemoryPackReader reader, ref ReactiveDictionary<TKey, TValue> value)
        {
            if (!reader.TryReadObjectHeader(out _))
            {
                value = null;
                return;
            }

            if (!reader.TryReadCollectionHeader(out int count))
            {
                value = new ReactiveDictionary<TKey, TValue>();
                return;
            }

            value ??= new ReactiveDictionary<TKey, TValue>();
            value.Clear();
            for (int i = 0; i < count; i++)
            {
                TKey key = reader.ReadValue<TKey>();
                TValue val = reader.ReadValue<TValue>();
                value[key] = val;
            }
        }
    }
}
