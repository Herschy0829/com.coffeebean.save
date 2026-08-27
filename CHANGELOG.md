# Changelog

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
