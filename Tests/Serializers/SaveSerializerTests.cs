using System.Collections.Generic;
using MemoryPack;
using NUnit.Framework;

namespace CoffeeBean.Save.Tests
{
    /// <summary>可序列化测试数据（MemoryPackable + VersionTolerant 版本演进）。</summary>
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class SaveTestData
    {
        [MemoryPackOrder(0)] public int Level;
        [MemoryPackOrder(1)] public string Name;
        [MemoryPackOrder(2)] public List<int> Items;
        [MemoryPackOrder(3)] public Dictionary<string, int> Stats;
    }

    /// <summary>新版结构（多了 Gold 字段，模拟版本升级）。</summary>
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class SaveTestDataV2
    {
        [MemoryPackOrder(0)] public int Level;
        [MemoryPackOrder(1)] public string Name;
        [MemoryPackOrder(2)] public List<int> Items;
        [MemoryPackOrder(3)] public Dictionary<string, int> Stats;
        [MemoryPackOrder(4)] public long Gold;
    }

    /// <summary>序列化后端测试：MemoryPack 往返 / JSON 往返 / 版本容错。</summary>
    public class SaveSerializerTests
    {
        private static SaveTestData CreateData()
            => new SaveTestData
            {
                Level = 5,
                Name = "测试存档",
                Items = new List<int> { 1, 2, 3 },
                Stats = new Dictionary<string, int> { ["金币"] = 100, ["钻石"] = 20 },
            };

        [Test]
        public void MemoryPack_RoundTrip_WithDictionary()
        {
            var serializer = CMemoryPackSerializer.Instance;
            SaveTestData data = CreateData();

            byte[] bytes = serializer.Serialize(data);
            SaveTestData restored = serializer.Deserialize<SaveTestData>(bytes);

            Assert.AreEqual(5, restored.Level);
            Assert.AreEqual("测试存档", restored.Name);
            CollectionAssert.AreEqual(data.Items, restored.Items);
            Assert.AreEqual(100, restored.Stats["金币"]);
            Assert.AreEqual(20, restored.Stats["钻石"]);
        }

        [Test]
        public void MemoryPack_VersionTolerant_NewFieldsDefault()
        {
            // 旧版数据（SaveTestData）→ 新版类（SaveTestDataV2）反序列化：新字段 Gold 取默认值
            byte[] oldBytes = CMemoryPackSerializer.Instance.Serialize(CreateData());
            SaveTestDataV2 restored = CMemoryPackSerializer.Instance.Deserialize<SaveTestDataV2>(oldBytes);

            Assert.AreEqual(5, restored.Level);
            Assert.AreEqual("测试存档", restored.Name);
            Assert.AreEqual(0, restored.Gold, "VersionTolerant 缺字段应取默认值");
        }

        [Test]
        public void Json_RoundTrip()
        {
            var serializer = CJsonSerializer.Instance;
            SaveTestData data = CreateData();

            byte[] bytes = serializer.Serialize(data);
            SaveTestData restored = serializer.Deserialize<SaveTestData>(bytes);

            Assert.AreEqual(5, restored.Level);
            Assert.AreEqual("测试存档", restored.Name);
        }

        [Test]
        public void MemoryPack_EmptyData_RoundTrip()
        {
            var data = new SaveTestData();
            byte[] bytes = CMemoryPackSerializer.Instance.Serialize(data);
            SaveTestData restored = CMemoryPackSerializer.Instance.Deserialize<SaveTestData>(bytes);
            Assert.IsNotNull(restored);
        }
    }
}
