using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace CoffeeBean.Save.Tests
{
    /// <summary>CSaveSystem 测试：存取往返 / 加密 / 损坏回退 / 原子写 / 版本迁移 / 自动存档节流。</summary>
    public class CSaveSystemTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "coffeebean_save_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private CSaveSystem CreateSave(string encryptionKey = "test-key-32-bytes-0123456789abcdef", int version = 1)
            => new CSaveSystem(new CSaveOptions
            {
                SaveDirectory = _dir,
                Slot = "main",
                EncryptionKey = encryptionKey,
                Version = version,
                AutoSaveMinInterval = 0f, // 测试关闭节流干扰（节流单独测）
            });

        /// <summary>等待异步写盘完成（后台任务队列排空）。</summary>
        private static void WaitForWrite(CSaveSystem save) => save.Flush();

        [Test]
        public void SaveLoad_RoundTrip_WithEncryption()
        {
            var save = CreateSave();
            var data = new SaveTestData { Level = 7, Name = "加密存档", Items = new System.Collections.Generic.List<int> { 1 }, Stats = new System.Collections.Generic.Dictionary<string, int> { ["金币"] = 50 } };

            save.SaveData(data);
            WaitForWrite(save);

            // 密文文件不应含明文
            byte[] fileBytes = File.ReadAllBytes(save.SaveFilePath);
            Assert.IsFalse(ContainsBytes(fileBytes, System.Text.Encoding.UTF8.GetBytes("加密存档")), "加密存档不应含明文");

            SaveTestData restored = save.LoadData<SaveTestData>();
            Assert.IsNotNull(restored);
            Assert.AreEqual(7, restored.Level);
            Assert.AreEqual("加密存档", restored.Name);
            Assert.AreEqual(50, restored.Stats["金币"]);
        }

        [Test]
        public void SaveLoad_NoEncryption_RoundTrip()
        {
            var save = CreateSave(encryptionKey: null);
            save.SaveData(new SaveTestData { Level = 3 });
            WaitForWrite(save);

            SaveTestData restored = save.LoadData<SaveTestData>();
            Assert.IsNotNull(restored);
            Assert.AreEqual(3, restored.Level);
        }

        [Test]
        public void Load_WrongKey_ReturnsDefault()
        {
            var save = CreateSave("key-A");
            save.SaveData(new SaveTestData { Level = 9 });
            WaitForWrite(save);

            var wrongKey = CreateSave("key-B");
            SaveTestData restored = wrongKey.LoadData<SaveTestData>();
            Assert.IsNull(restored, "key 不匹配应解码失败返回默认");
        }

        [Test]
        public void CorruptPrimary_FallsBackToBackup()
        {
            var save = CreateSave();
            save.SaveData(new SaveTestData { Level = 5 });
            WaitForWrite(save);

            // 写第二份（写入时旧档转 bak = 第一份 Level5，主档 = Level6），然后损坏主档
            save.SaveData(new SaveTestData { Level = 6 });
            WaitForWrite(save); // 等 bak 生成完毕再损坏主档

            File.WriteAllBytes(save.SaveFilePath, new byte[] { 1, 2, 3, 4, 5 }); // 损坏主档

            SaveTestData restored = save.LoadData<SaveTestData>();
            Assert.IsNotNull(restored, "主档损坏应回退备份档");
            Assert.AreEqual(5, restored.Level, "备份档应为上一次成功写入（第一份 Level5）");
        }

        [Test]
        public void MissingFile_ReturnsDefault()
        {
            var save = CreateSave();
            SaveTestData restored = save.LoadData<SaveTestData>();
            Assert.IsNull(restored, "无存档应返回默认");
        }

        [Test]
        public void DeleteSlot_RemovesAllFiles()
        {
            var save = CreateSave();
            save.SaveData(new SaveTestData { Level = 1 });
            WaitForWrite(save);

            save.DeleteSlot();

            Assert.IsFalse(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0, "删除后目录应无文件");
        }

        [Test]
        public void VersionMigration_MigratesAndRewrites()
        {
            var saveV1 = CreateSave(version: 1);
            saveV1.SaveData(new SaveTestData { Level = 2, Name = "旧档" });
            WaitForWrite(saveV1);

            // 新版本 + 迁移钩子：补 Gold
            var saveV2 = CreateSave(version: 2);
            bool migrated = false;
            saveV2.SetMigrator<SaveTestDataV2>((oldVersion, data) =>
            {
                migrated = true;
                Assert.AreEqual(1, oldVersion);
                data.Gold = 999; // 迁移补默认值
            });

            SaveTestDataV2 restored = saveV2.LoadData<SaveTestDataV2>();
            Assert.IsNotNull(restored);
            Assert.IsTrue(migrated, "旧版本应触发迁移钩子");
            Assert.AreEqual(999, restored.Gold);
            Assert.AreEqual("旧档", restored.Name);
            saveV2.Flush(); // 迁移写回是异步的，等它完成再进入 TearDown 清理
        }

        [Test]
        public void AutoSave_ThrottlesWithinMinInterval()
        {
            var save = new CSaveSystem(new CSaveOptions
            {
                SaveDirectory = _dir,
                Slot = "main",
                EncryptionKey = null,
                AutoSaveInterval = 120f,
                AutoSaveMinInterval = 60f, // 60 秒内第二次跳过
            });

            Assert.IsTrue(save.SaveDataAuto(new SaveTestData { Level = 1 }), "首次自动档应写入");
            WaitForWrite(save);

            Assert.IsFalse(save.SaveDataAuto(new SaveTestData { Level = 2 }), "间隔内第二次自动档应被节流跳过");
        }

        [Test]
        public void AutoSave_Disabled_WhenIntervalZero()
        {
            var save = new CSaveSystem(new CSaveOptions
            {
                SaveDirectory = _dir,
                AutoSaveInterval = 0f, // 关闭
                EncryptionKey = null,
            });

            Assert.IsFalse(save.SaveDataAuto(new SaveTestData()), "AutoSaveInterval=0 时自动存档应关闭");
        }

        [Test]
        public void AutoSave_Slot_IsAutoSuffixed()
        {
            var save = CreateSave();
            save.SaveDataAuto(new SaveTestData { Level = 4 });
            WaitForWrite(save);

            string autoPath = Path.Combine(_dir, "main_auto.sav");
            Assert.IsTrue(File.Exists(autoPath), "自动档应写入 {Slot}_auto.sav");

            SaveTestData restored = save.LoadData<SaveTestData>("main_auto");
            Assert.IsNotNull(restored);
            Assert.AreEqual(4, restored.Level);
        }

        [Test]
        public void SaveDataAuto_Force_IgnoresThrottle()
        {
            var save = new CSaveSystem(new CSaveOptions
            {
                SaveDirectory = _dir,
                Slot = "main",
                EncryptionKey = null,
                AutoSaveInterval = 120f,
                AutoSaveMinInterval = 60f,
            });

            Assert.IsTrue(save.SaveDataAuto(new SaveTestData { Level = 1 }));
            save.Flush();
            Assert.IsFalse(save.SaveDataAuto(new SaveTestData { Level = 2 }), "节流窗口内应跳过");
            Assert.IsTrue(save.SaveDataAuto(new SaveTestData { Level = 2 }, force: true), "force 应忽略节流");
            save.Flush();

            Assert.AreEqual(2, save.LoadData<SaveTestData>("main_auto").Level);
        }

        [Test]
        public void SaveDataAutoImmediate_PersistsWithoutManualFlush()
        {
            var save = new CSaveSystem(new CSaveOptions
            {
                SaveDirectory = _dir,
                Slot = "main",
                EncryptionKey = null,
                AutoSaveInterval = 120f,
                AutoSaveMinInterval = 60f,
            });

            Assert.IsTrue(save.SaveDataAuto(new SaveTestData { Level = 1 }));
            save.Flush();
            Assert.IsFalse(save.SaveDataAuto(new SaveTestData { Level = 2 }), "节流窗口内应跳过");

            // 回归点：失焦/退出路径此前用的是节流版 SaveDataAuto 且不 Flush ——
            // 既可能被节流直接跳过，也可能只入队而在后台写盘完成前进程就结束了。
            Assert.IsTrue(save.SaveDataAutoImmediate(new SaveTestData { Level = 3 }));

            // 刻意不再调用 Flush：立即版必须已经阻塞到落盘
            SaveTestData restored = save.LoadData<SaveTestData>("main_auto");
            Assert.IsNotNull(restored);
            Assert.AreEqual(3, restored.Level, "立即版应在返回前把最新数据落盘");
        }

        [Test]
        public void SaveDataAutoImmediate_RespectsMasterSwitch()
        {
            var save = new CSaveSystem(new CSaveOptions
            {
                SaveDirectory = _dir,
                Slot = "main",
                AutoSaveInterval = 0f, // 自动存档总开关关闭
                EncryptionKey = null,
            });

            Assert.IsFalse(save.SaveDataAutoImmediate(new SaveTestData { Level = 1 }),
                "总开关关闭时立即版也不应写入（业务应自行 SaveData + Flush）");
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "main_auto.sav")));
        }

        [Test]
        public void DeleteSlot_RemovesAutoBackupAndTmpLeftovers()
        {
            var save = CreateSave();
            save.SaveData(new SaveTestData { Level = 1 });
            WaitForWrite(save);

            // 手工造出自动档备份与崩溃残留 tmp
            File.WriteAllBytes(Path.Combine(_dir, "main_auto.sav"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(_dir, "main_auto.sav.bak"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(_dir, "main.sav.tmp"), new byte[] { 1 });

            save.DeleteSlot();

            Assert.IsFalse(File.Exists(Path.Combine(_dir, "main_auto.sav.bak")),
                "自动档备份应被删除（早期实现漏删，回退读取可能读回本该删除的旧档）");
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "main.sav.tmp")), "崩溃残留 tmp 应被删除");
            Assert.IsFalse(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0, "删除后目录应无文件");
        }

        [Test]
        public void Flush_DrainsAllQueuedWrites_LastWriteWins()
        {
            var save = CreateSave();
            for (int i = 1; i <= 200; i++) save.SaveData(new SaveTestData { Level = i });

            // 回归点：早期 Flush 只 Wait 一次捕获到的 Task；若等待期间有新写入换了任务，
            // 或入队恰好落在"委托已返回但 Task 未标记完成"的窗口里，这次写入会被丢掉。
            save.Flush();

            SaveTestData restored = save.LoadData<SaveTestData>();
            Assert.IsNotNull(restored);
            Assert.AreEqual(200, restored.Level, "Flush 返回后最后一次写入必须已落盘");
        }

        private static bool ContainsBytes(byte[] data, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= data.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }
    }
}
