# Changelog

## [0.4.0] - 2026-09-17

### Fixed
- **多槽位写入会被静默丢弃（竞态）**。后台写盘原先只有**一个**待写信箱
  （`_pendingSlot` / `_pendingPayload`），第二次 `EnqueueWrite` 直接覆盖第一次 ——
  于是「先写 A 槽、紧接着写 B 槽」时 A 的那次写就没了，而且**丢不丢取决于后台线程的调度时机**，
  属于竞态：同一段代码可能这次正常、下次丢档。
  本模块自带的 `SaveDataAuto` 写的正是另一个槽位（`{Slot}_auto`），
  所以 `SaveData(x)` 紧跟 `SaveDataAuto(x)` 就是这条缺陷的真实触发路径。

  实测复现（修复前）：连续 `SaveData(a, "slotA")` + `SaveData(b, "slotB")` 后 `Flush()`，
  `slotA.sav` 根本没有生成、`LoadData("slotA")` 返回 null，只有 `slotB.sav` 落盘。

  修法：待写队列改为**按槽位**分别暂存（`Dictionary<string, byte[]>` + 脏槽位入队顺序表）。
  - **不同槽位互不覆盖** —— 每个槽位的写都会落盘；
  - **同槽位仍然只保留最新一份**，保留原「最新优先」语义与省 IO 的收益；
  - 写入顺序按首次入队先后（FIFO），便于推理与测试；
  - `Flush()` 的判空条件同步改为「脏槽位为空且写盘循环已停」，语义与原来一致。

  对单槽位使用者（绝大多数）行为不变，无 API 变更、无存档格式变更 —— 升级即修复。

### Tests
- 新增 4 个回归用例：`SaveData_TwoDifferentSlots_NeitherWriteIsDropped`、
  `SaveData_ThenSaveDataAuto_BothSlotsPersisted`、
  `SaveData_ManyInterleavedSlots_AllPersisted`（5 槽 × 4 轮交错入队且中途不 Flush）、
  `SaveData_SameSlotRepeated_KeepsLatestOnly`（确认同槽位「最新优先」语义未被破坏）。
- 已验证这些用例**确实能抓到旧缺陷**：把旧实现临时放回去后，
  `SaveData_ManyInterleavedSlots_AllPersisted` 稳定失败（22/23）。
  单轮两槽那条因竞态有时会侥幸通过，故交错的这条是可靠的回归守卫。

## [0.3.0] - 2026-09-17

### Added
- **内嵌 MemoryPack 二进制**（`Runtime/Plugins/MemoryPack/`）：`MemoryPack.Core.dll`（1.21.4）、
  Roslyn 源生成器 `MemoryPack.Generator.dll`（`.meta` 带 `RoslynAnalyzer` 标签）、
  以及 Core 在 netstandard2.1 下的依赖 `System.Collections.Immutable` / `System.Runtime.CompilerServices.Unsafe`。
  同时附上 `THIRD-PARTY-NOTICES.md` 与各自的 MIT 许可全文（再分发要求）。

  **解决的问题**：上游 `MemoryPack.Unity` 的 git 包**只给胶水层**，不含 Core 与生成器；
  消费工程若只做 git 引用会得到 **87 条 `CS0234/CS0246`**（`MemoryPack.Internal`、`MemoryPackFormatter<>`、
  `MemoryPackWriter<>`、`MemoryPackReader`、`PreserveAttribute` 全部找不到）。
  此前唯一的办法是引 NuGetForUnity 或手动放 DLL（见 0.1.2 的文档说明）。

  现在消费工程只需三条 manifest 依赖即可，**不需要额外编辑器工具、不需要联网还原、不需要手动放 DLL**。

### ⚠️ 升级注意（破坏性使用约束）
- **不要重复提供这些 DLL**。Unity 遇到同名预编译程序集直接报错
  `Multiple precompiled assemblies with the same name`。
  若你此前用 NuGetForUnity 还原或手动放过 `MemoryPack.Core.dll` / `MemoryPack.Generator.dll`，
  **升级到本版本时必须先把它们移除**。
- 上游 `com.cysharp.memorypack`（胶水层）仍要装，与内嵌二进制不重名，互补。
- 内嵌二进制的 `.meta` 使用**重新生成的 GUID**，避免与既有同源副本撞 GUID。

### Verified
- 在 dev 工程把 `com.cysharp.memorypack` 换成**只有胶水层的上游 git 包**、并删除原先 vendored 的
  完整副本（含旧 DLL）后：全量 EditMode **461/461 通过**，源生成器正常工作
  （即 Core 与生成器确实来自本模块内嵌的那份）。
- 过程中先复现了"生成器跑两遍"的失败（`CS0102/CS0111` 重复生成），确认了"不要重复提供"这条约束是真实生效的。

## [0.2.0] - 2026-09-17

### Added
- **UniRx 自定义 MemoryPack 格式化器**（可选程序集 `CoffeeBean.Save.UniRx`）：
  `CReactivePropertyFormatter<T>`、6 个派生类型（`CInt/CLong/CBool/CFloat/CDouble/CStringReactivePropertyFormatter`）、
  `CReactiveCollectionFormatter<T>`、`CReactiveDictionaryFormatter<TKey,TValue>`，
  以及注册入口 `CUniRxSaveFormatters.RegisterAll()`（幂等；运行时 `RuntimeInitializeOnLoadMethod` +
  编辑器 `InitializeOnLoadMethod` 双时机自动注册）。

  **为什么框架要提供**：MemoryPack **没有** UniRx 类型的内建格式化器（实测 MemoryPack.Core 里
  `ReactiveProperty` 相关类型为 **0** 个），而 MemoryPack 依赖 `[ModuleInitializer]` 自动注册格式化器，
  **Unity 不支持 ModuleInitializer** —— 因此外部库类型必须手工注册，否则要到序列化时才抛
  "formatter is not registered"。此前每个工程都得自己写一份（约 350 行），现在由框架提供。

  - **可选、不强制**：该程序集用 `versionDefines` 监听 `com.neuecc.unirx`，**装了 UniRx 才编译**
    （`defineConstraints: ["COFFEEBEAN_UNIRX"]` + `autoReferenced: false`），没装则整体跳过 ——
    与框架既有的 Bridge 模式一致。
  - **字节兼容**：对象头/集合头布局与既有工程手写版本**逐字节一致**，换用不会改变已写出的存档格式
    （只替换实现，不换格式）。
  - **不包含 `BigIntegerFormatter`**：MemoryPack.Core **已内建**
    `MemoryPack.Formatters.BigIntegerFormatter`（本次已核实），再写一份是冗余，
    且会因"该类型已有格式化器"导致注册失败（既有工程那份大概一直在静默失败）。

### Notes
- 需要其他元素类型的格式化器时，用 `CUniRxSaveFormatters.Register<T>(...)` 追加即可，无需改框架。

## [0.1.2] - 2026-09-17

### Fixed（**文档缺陷**：会让接入方直接编译失败）
- **README 的 MemoryPack git 引用路径是错的**：原文写 `?path=src/MemoryPack`，那并不是 Unity 包
  （Unity 包在 `src/MemoryPack.Unity/Assets/MemoryPack.Unity`）。照抄会解析出错误的内容。
- **README 未说明 MemoryPack 还必须单独提供 NuGet 产物**（本次在真实工程接入时暴露）：
  上游 `MemoryPack.Unity` 包内**只有**胶水层（`Runtime/` 3 个文件 + `package.json`），
  既没有 `MemoryPack.Core.dll`，也没有 Roslyn 源生成器 `MemoryPack.Generator.dll`；
  而 `MemoryPack.Unity.asmdef` 要求 `precompiledReferences: ["MemoryPack.Core.dll"]`。

  只做 git 引用会在消费工程产生 **87 条编译错误**（`CS0234` 命名空间 `MemoryPack.Internal` 不存在、
  `CS0246` 找不到 `MemoryPackFormatter<>` / `MemoryPackWriter<>` / `MemoryPackReader` / `PreserveAttribute`），
  导致 `MemoryPack.Unity` 程序集编不出来，进而 `CoffeeBean.Save` 也编不出来。

  现已补上完整的两种供给方式（**NuGetForUnity** 还原 / **手动放置** NuGet 产物到 `Assets/Packages/`，
  生成器 `.meta` 必须带 `RoslynAnalyzer` 标签），并给出「如何确认源生成器真的生效」的判据：
  编译后 `Library/BuildPlayerData/Player/TypeDb-All.json` 里应能搜到 `<类型>+<类型>Formatter`
  （例如 `PlayerData+PlayerDataFormatter`）。
- README 安装示例中 `com.coffeebean.tools` 的 pin 由过期的 `v0.5.0` 修正为 `v0.6.0`。

### Notes
- 本版本**只改文档，无代码变更**。之所以照常发版：这类文档缺陷会让接入方直接踩坑，
  发版后 Hub 才能把新版本推给已接入的工程。
- 同步修正 `docs/design-save.md` 里"来源由消费工程决定"这一过于笼统的表述。

### Verified（真实工程接入实测）
- 在 IdleMedievalLife（Unity 6000.0.71f1）以 **git 依赖**装入 `com.coffeebean.core` / `com.coffeebean.tools` /
  `com.coffeebean.save`：三者均编译成功（`CoffeeBean.Core.dll` / `CoffeeBean.Tools.dll` / `CoffeeBean.Save.dll`
  及 `CoffeeBean.Save.Bridge.dll`），MemoryPack 源生成器确认生效（产出 `PlayerData+PlayerDataFormatter`）。

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
