using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CoffeeBean
{
    /// <summary>
    /// 存档系统门面（对齐 Idle 的 MemoryPack + 自动存档调度，补齐并优化）：
    ///
    /// - **文件槽位**：`{SaveDirectory}/{Slot}.sav`（自动档 `{Slot}_auto.sav`），备份 `{Slot}.sav.bak`
    /// - **原子写**：tmp → 旧档转 bak → tmp 转正（崩溃任意点不损坏旧档）
    /// - **损坏回退**：主档读/解码失败自动回退备份档
    /// - **串行异步写**：后台单任务按"每槽位最新优先"写盘（修复 BinarySerializ 静态字段互踩）；
    ///   不同槽位各自暂存、互不覆盖，同槽位只保留最新一份
    /// - **加密**：AES + 可选 XOR（<see cref="CSaveEncrypt"/>），文件头带 version
    /// - **版本迁移**：读档 version &lt; 当前时调 <see cref="SetMigrator{T}"/> 钩子并写回新版本
    /// - **自动存档节流**：<see cref="SaveDataAuto"/> 距上次自动写不足间隔跳过；
    ///   配合 <see cref="CSaveAutoSaveHook"/> 定时 + 失焦/退出自动存
    ///
    /// 文件格式：`[4 字节 version][序列化数据（可选 XOR + AES 或明文）]`
    /// </summary>
    public sealed class CSaveSystem
    {
        private const string Tag = "CoffeeBean.Save";
        private const byte XorKey = 0x4C;

        private readonly object _lock = new object();
        private readonly CSaveOptions _options;

        /// <summary>
        /// 待写槽位 → 最新载荷（**按槽位**各自「最新优先」）。
        ///
        /// 早期实现只有一个 `_pendingSlot`/`_pendingPayload` 信箱，第二次入队直接覆盖第一次：
        /// 「先写 A 槽、紧接着写 B 槽」时 A 的写会被丢掉，且是否丢失取决于后台线程的调度时机
        /// （竞态）。而模块自带的 <see cref="SaveDataAuto"/> 写的正是另一个槽位
        /// （<c>{Slot}_auto</c>），所以 `SaveData(x)` 紧跟 `SaveDataAuto(x)` 就可能静默漏写一次自动档。
        /// 改为按槽位分别暂存后：不同槽位互不覆盖，同槽位仍然只保留最新一份（保留原「最新优先」语义）。
        /// </summary>
        private readonly Dictionary<string, byte[]> _pendingPayloads =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>脏槽位的入队顺序：先入队的先写，同槽位重复入队不重复排队。</summary>
        private readonly List<string> _dirtySlots = new List<string>();

        /// <summary>
        /// 写盘循环是否在运行。必须与待写队列在同一把锁内维护：
        /// 早期实现靠 `_writer.IsCompleted` 判断"要不要起新任务"，而委托返回与
        /// Task 标记完成之间存在窗口 —— 落在窗口里的入队会既不起新任务、又赶上循环退出，
        /// 该次写入就被静默丢掉（退出时丢档的元凶之一）。
        /// </summary>
        private bool _writerRunning;

        private Task _writer;
        private Action<int, object> _migrator;
        private long _lastAutoWriteTicks;

        public CSaveSystem(CSaveOptions options)
        {
            _options = options ?? new CSaveOptions();
        }

        /// <summary>存档目录（按选项解析）。</summary>
        public string SaveDirectory => _options.ResolveDirectory();

        /// <summary>主存档文件路径。</summary>
        public string SaveFilePath => Path.Combine(SaveDirectory, _options.Slot + ".sav");

        /// <summary>注册版本迁移钩子：读档版本低于当前版本时调用（参数 = 旧版本号 + 已反序列化的数据对象）。</summary>
        public void SetMigrator<T>(Action<int, T> migrator)
        {
            _migrator = migrator == null ? null : (version, data) => migrator(version, (T)data);
        }

        /// <summary>
        /// 保存数据（主线程调用；序列化+加密在主线程，文件写入后台异步、串行最新优先）。
        /// 默认写主槽位，也可指定槽位。
        /// </summary>
        public void SaveData<T>(T data, string slot = null)
        {
            if (data == null) return;
            byte[] payload = BuildPayload(data);
            EnqueueWrite(slot ?? _options.Slot, payload);
        }

        /// <summary>
        /// 自动档保存（写 `{Slot}_auto.sav`）：带节流——距上次自动写不足
        /// <see cref="CSaveOptions.AutoSaveMinInterval"/> 秒则跳过并返回 false。
        /// </summary>
        /// <param name="force">
        /// true 时忽略节流，强制写入（用于失焦/退出等"必须落盘"的时机）。
        /// 仍受自动存档总开关 <see cref="CSaveOptions.AutoSaveInterval"/> 约束（&lt;=0 视为关闭）。
        /// </param>
        public bool SaveDataAuto<T>(T data, bool force = false)
        {
            if (_options.AutoSaveInterval <= 0f) return false; // 自动存档关闭
            long now = DateTime.UtcNow.Ticks;
            if (!force && _options.AutoSaveMinInterval > 0f
                && now - _lastAutoWriteTicks < (long)(_options.AutoSaveMinInterval * TimeSpan.TicksPerSecond))
                return false; // 节流跳过

            SaveData(data, _options.Slot + "_auto");
            _lastAutoWriteTicks = now;
            return true;
        }

        /// <summary>
        /// 立即写自动档并**阻塞到落盘完成**：忽略节流，且等待后台写盘结束。
        /// 用于失焦（移动端随时可能被系统杀死）与应用退出 —— 只入队不等待的话，
        /// 进程可能在后台线程真正写盘前就结束了，最后一次存档就丢了。
        /// 写盘跑在线程池、不依赖主线程，因此主线程调用可安全等待。
        /// </summary>
        public bool SaveDataAutoImmediate<T>(T data)
        {
            bool saved = SaveDataAuto(data, force: true);
            if (saved) Flush();
            return saved;
        }

        /// <summary>读取存档（主线程；主档失败自动回退备份档；版本低于当前时迁移并写回）。</summary>
        public T LoadData<T>(string slot = null)
        {
            string name = slot ?? _options.Slot;
            byte[] primary = TryReadFile(name + ".sav");
            if (primary != null)
            {
                try { return DecodePayload<T>(primary, name); }
                catch (Exception e)
                {
                    CLog.Warn(Tag, $"主档读取失败，回退备份: {e.Message}");
                }
            }

            byte[] backup = TryReadFile(name + ".sav.bak");
            if (backup != null)
            {
                try { return DecodePayload<T>(backup, name); }
                catch (Exception e)
                {
                    CLog.Error(Tag, $"备份档读取失败: {e.Message}");
                }
            }

            return default;
        }

        /// <summary>删除槽位文件（含备份、自动档与崩溃残留的 .tmp）。</summary>
        public void DeleteSlot(string slot = null)
        {
            string name = slot ?? _options.Slot;
            DeleteIfExists(name + ".sav");
            DeleteIfExists(name + ".sav.bak");
            DeleteIfExists(name + ".sav.tmp");
            DeleteIfExists(name + "_auto.sav");
            // 早期实现漏删自动档的备份：删除槽位后 {Slot}_auto.sav.bak 会残留，
            // 若之后 LoadData 走备份回退路径，可能读回本该已删除的旧档。
            DeleteIfExists(name + "_auto.sav.bak");
            DeleteIfExists(name + "_auto.sav.tmp");
        }

        /// <summary>
        /// 阻塞等待所有已排队的写盘完成（测试断言前 / 退出前调用；主线程可用）。
        /// 写盘在后台线程池执行，不依赖 Unity 主线程，故可安全 Wait。
        /// 循环等待直到队列清空且写盘循环停止 —— 单次 Wait 可能只等到旧任务，
        /// 而等待期间新入队的写入会由新任务承接。
        /// </summary>
        public void Flush()
        {
            while (true)
            {
                Task writer;
                lock (_lock)
                {
                    if (_dirtySlots.Count == 0 && !_writerRunning) return;
                    writer = _writer;
                }

                try
                {
                    writer?.Wait();
                }
                catch (AggregateException e)
                {
                    CLog.Error(Tag, $"Flush 等待写盘异常: {e.InnerException?.Message ?? e.Message}");
                    return;
                }
            }
        }

        // ========== 内部 ==========

        /// <summary>序列化 + 加密 + 文件头 version。</summary>
        private byte[] BuildPayload<T>(T data)
        {
            byte[] payload = _options.Serializer.Serialize(data);
            if (!string.IsNullOrEmpty(_options.EncryptionKey))
            {
                if (_options.UseXorObfuscation) payload = CSaveEncrypt.Xor(payload, XorKey);
                payload = CSaveEncrypt.Encrypt(payload, _options.EncryptionKey);
            }

            var file = new byte[4 + payload.Length];
            WriteInt32(file, 0, _options.Version);
            Buffer.BlockCopy(payload, 0, file, 4, payload.Length);
            return file;
        }

        private T DecodePayload<T>(byte[] file, string slot)
        {
            int version = ReadInt32(file, 0);
            var payload = new byte[file.Length - 4];
            Buffer.BlockCopy(file, 4, payload, 0, payload.Length);

            if (!string.IsNullOrEmpty(_options.EncryptionKey))
            {
                payload = CSaveEncrypt.Decrypt(payload, _options.EncryptionKey);
                if (_options.UseXorObfuscation) payload = CSaveEncrypt.Xor(payload, XorKey);
            }

            T data = _options.Serializer.Deserialize<T>(payload);

            // 版本迁移：旧档 → 迁移钩子 → 写回新版本
            if (version < _options.Version && _migrator != null)
            {
                CLog.Info(Tag, $"存档版本 {version} → {_options.Version}，执行迁移");
                _migrator(version, data);
                EnqueueWrite(slot, BuildPayload(data));
            }

            return data;
        }

        private void EnqueueWrite(string slot, byte[] payload)
        {
            lock (_lock)
            {
                // 同槽位只保留最新一份；不同槽位各占一格，不会被彼此覆盖
                if (!_pendingPayloads.ContainsKey(slot)) _dirtySlots.Add(slot);
                _pendingPayloads[slot] = payload;

                // 用 _writerRunning（循环自己在同一把锁内置/清）而不是 _writer.IsCompleted 判断，
                // 否则委托返回与 Task 完成之间的窗口会丢掉这次写入。
                if (!_writerRunning)
                {
                    _writerRunning = true;
                    _writer = Task.Run(WriteLoop);
                }
            }
        }

        private void WriteLoop()
        {
            while (true)
            {
                string slot;
                byte[] payload;
                lock (_lock)
                {
                    if (_dirtySlots.Count == 0)
                    {
                        // 清标志与"检查队列"必须原子：否则入队方可能看到 running=true
                        // 而循环已经决定退出，写入被丢。
                        _writerRunning = false;
                        return;
                    }
                    slot = _dirtySlots[0];
                    _dirtySlots.RemoveAt(0);
                    payload = _pendingPayloads[slot];
                    _pendingPayloads.Remove(slot);
                }
                AtomicWrite(slot, payload);
            }
        }

        /// <summary>原子写：tmp → 旧档转 bak → tmp 转正（崩溃任意点不损坏旧档，bak 保留上一版供回退）。</summary>
        private void AtomicWrite(string slot, byte[] payload)
        {
            string dir = _options.ResolveDirectory();
            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, slot + ".sav");
                string tmp = path + ".tmp";
                string bak = path + ".bak";

                File.WriteAllBytes(tmp, payload);
                if (File.Exists(path))
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                CLog.Error(Tag, $"写盘失败 {slot}: {e.Message}");
            }
        }

        private byte[] TryReadFile(string fileName)
        {
            try
            {
                string path = Path.Combine(_options.ResolveDirectory(), fileName);
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        private void DeleteIfExists(string fileName)
        {
            try
            {
                string path = Path.Combine(_options.ResolveDirectory(), fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                CLog.Warn(Tag, $"删除失败 {fileName}: {e.Message}");
            }
        }

        private static int ReadInt32(byte[] buffer, int offset)
            => (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
