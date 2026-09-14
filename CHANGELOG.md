# Changelog

## [0.1.1] - 2026-09-14

### Fixed
- **退出/失焦时可能丢最后一次存档**：`CSaveAutoSaveHook` 的 `OnApplicationQuit` / `OnApplicationPause(true)`
  此前调用的是**节流版** `SaveDataAuto` —— 既可能因 `AutoSaveMinInterval` 未到而被直接跳过，
  又即便入队也没有 `Flush()`，进程可能在后台写盘完成前就结束。现在这两个时机改走新增的
  `SaveDataAutoImmediate`（忽略节流 + 阻塞到落盘）。移动端失焦后随时可能被系统杀死，同理。
- **`Flush()` 可能等不到最后一次写入**：原本只 `Wait()` 一次捕获到的 `Task`，等待期间新入队的写入
  会由新任务承接而不会被等到。更隐蔽的是 `EnqueueWrite` 用 `_writer.IsCompleted` 判断"要不要起新任务"，
  而**委托返回与 Task 标记完成之间存在窗口** —— 落在窗口里的入队会既不起新任务、又赶上循环退出，
  该次写入被静默丢弃。现在改用与队列同锁维护的 `_writerRunning` 标志，`Flush()` 循环等到队列清空
  且写盘循环停止。
- **`DeleteSlot` 漏删自动档备份**：只删 `{Slot}_auto.sav`，遗留 `{Slot}_auto.sav.bak`。
  槽位删除后若 `LoadData` 走备份回退路径，可能读回本该已删除的旧档。同时补删崩溃残留的 `.tmp`
  （`{Slot}.sav.tmp` / `{Slot}_auto.sav.tmp`）。

### Added
- `CSaveSystem.SaveDataAuto<T>(T data, bool force = false)`：`force` 忽略节流（仍受自动存档总开关约束）。
- `CSaveSystem.SaveDataAutoImmediate<T>(T data)`：忽略节流并阻塞到落盘，供失焦/退出使用。

### Tests
- 新增 5 个 EditMode 测试：force 忽略节流 / 立即版无需手动 Flush 即落盘 / 立即版遵守总开关 /
  `DeleteSlot` 清除自动档备份与 tmp 残留 / `Flush` 排空队列且最后一次写入生效（200 次连续写入）。
- EditMode：**19/19 全绿**。

## [0.1.0] - 2026-08-27

### Added
- **`CSaveSystem` 存档门面**：文件槽位（`persistentDataPath/{Slot}.sav` + 备份 + 自动档）、
  **原子写**（tmp → 旧档转 .bak → tmp 转正）、**损坏自动回退备份**、
  **串行异步写**（后台单任务"最新优先"，修复 BinarySerializ 静态字段竞态）、
  **版本迁移**（文件头 version + `SetMigrator` 钩子 + 写回新版本）、删除槽位
- **序列化后端可插拔 `ISaveSerializer`**：`CMemoryPackSerializer`（默认，高效二进制，依赖 com.cysharp.memorypack）+
  `CJsonSerializer`（JSON 兜底，基于 tools CJson）
- **`CSaveEncrypt`**：AES-256-CBC + 随机 IV（每次写盘不同 IV）+ 可选 XOR 混淆（默认开启）
- **`CSaveAutoSaveHook`**：定时 + 失焦/退出自动存档（对齐 Idle 调度）；`SaveDataAuto` 节流
- **SaveDemo 示例**：保存/读取（MemoryPack + 加密）、自动档、删除存档
- Core 可选集成：Bridge 条件编译（模块标记 + 生命周期）
- EditMode 测试：序列化后端往返（含 Dictionary / VersionTolerant）/ 加密 / 损坏回退 / 版本迁移 / 自动档节流

### Notes
- 依赖 `com.coffeebean.tools`（CJson 兜底 / 日志）+ `com.cysharp.memorypack`（声明依赖，来源消费工程定）
- 替代 Idle 项目的 BinarySerializ（BinaryFormatter，废弃）与裸 MemoryPackSerialize；LitJson JSON 场景归 net/资产层
