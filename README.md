# CoffeeBean Save（com.coffeebean.save）

CoffeeBean 框架的存档模块：**MemoryPack 二进制序列化**（可插拔后端）+ **AES 加密** + 原子写 / 损坏回退 / 串行异步写 / 自动存档节流 / 版本迁移。

对齐 Idle 项目的 MemoryPack 方案与自动存档调度，补齐加密 / 原子写 / 备份回退等缺口。

> 设计文档：`docs/design-save.md`（v0.1）

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.save": "https://github.com/Herschy0829/com.coffeebean.save.git#v0.2.0",
    "com.coffeebean.tools": "https://github.com/Herschy0829/com.coffeebean.tools.git#v0.6.0",
    "com.cysharp.memorypack": "https://github.com/Cysharp/MemoryPack.git?path=src/MemoryPack.Unity/Assets/MemoryPack.Unity#1.21.4"
  }
}
```

### ⚠️ MemoryPack 还需要单独提供 NuGet 产物（必读）

**上面那条 git 引用只提供"胶水层"** —— 上游 `MemoryPack.Unity` 包内**只有** `Runtime/`（3 个文件）与 `package.json`，
**不含** `MemoryPack.Core.dll`，也**不含** Roslyn 源生成器 `MemoryPack.Generator.dll`。
而 `MemoryPack.Unity.asmdef` 里写着 `precompiledReferences: ["MemoryPack.Core.dll"]`，所以**必须**另外提供 NuGet 产物，二选一：

| 方式 | 做法 |
|---|---|
| **A. NuGetForUnity（上游推荐）** | 引入 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)（本身也是 UPM 包），搜索 `MemoryPack` 安装 → 它会还原到 `Assets/Packages/` |
| **B. 手动放置 NuGet 产物** | 把 `MemoryPack.Core.dll`（`lib/netstandard2.1/`）、`MemoryPack.Generator.dll`（`analyzers/dotnet/cs/`，其 `.meta` **必须**带 `RoslynAnalyzer` 标签）、以及依赖的 `System.Collections.Immutable.dll` / `System.Runtime.CompilerServices.Unsafe.dll` 放进工程（如 `Assets/Packages/`），并保留各自 `.meta` |

**只做 git 引用会直接编译失败**（实测，一次 87 条错误）：

```
error CS0234: 命名空间 "MemoryPack" 中不存在类型或命名空间名 "Internal"
error CS0246: 找不到类型或命名空间名 "MemoryPackFormatter<>" / "MemoryPackWriter<>" / "MemoryPackReader" / "PreserveAttribute"
```

即 `MemoryPack.Unity` 程序集编不出来 → 引用它的 `CoffeeBean.Save` 也编不出来。

**如何确认生成器真的生效**：编译后 `Library/BuildPlayerData/Player/TypeDb-All.json` 里应能搜到
`<你的类型>+<类型>Formatter`（例如 `PlayerData+PlayerDataFormatter`）—— 那是源生成器产出的嵌套类型，进入了程序集才说明它跑了。

> 为什么不能把 DLL 直接塞进本模块：MemoryPack 的 Core 与生成器是 **NuGet 二进制 + 版本锁**，
> 且生成器要作为 Roslyn analyzer 参与消费工程的编译；内嵌到 UPM 包里既会锁定版本，也拿不到正确的 analyzer 装配。故由消费工程提供。

## 快速使用

```csharp
using CoffeeBean;
using MemoryPack;

// 数据类标记 MemoryPackable（建议 VersionTolerant 以便字段演进兼容旧档；
// 注意 VersionTolerant 模式要求每个成员显式标注 [MemoryPackOrder(n)]，否则生成器报 MEMPACK025）
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class PlayerData
{
    [MemoryPackOrder(0)] public int Level;
    [MemoryPackOrder(1)] public string Name;
    [MemoryPackOrder(2)] public System.Collections.Generic.Dictionary<string, int> Stats;
}

var save = new CSaveSystem(new CSaveOptions
{
    Slot = "main",
    EncryptionKey = "project-fixed-key-32bytes",  // 空 = 不加密
    Version = 1,
});

save.SaveData(playerData);                            // 保存（原子写 + AES + 后台异步）
PlayerData loaded = save.LoadData<PlayerData>();      // 读取（损坏自动回退 .bak）
save.SaveDataAuto(playerData);                        // 自动档（main_auto.sav，带节流）
CSaveAutoSaveHook.Attach(save, () => playerData, 120f); // 定时 + 失焦/退出自动存档

// 版本升级：旧档迁移
save.SetMigrator<PlayerData>((oldVersion, data) =>
{
    if (oldVersion < 2) data.NewField = DefaultValue; // 补新字段
});
```

## 特性

- **序列化后端可插拔** `ISaveSerializer`：`CMemoryPackSerializer`（默认，高效二进制）/ `CJsonSerializer`（JSON 兜底，调试可读）
- **AES-256-CBC + 随机 IV**（每次写盘不同 IV）+ 可选 XOR 混淆；key 项目配置，默认开启
- **原子写**：tmp → 旧档转 .bak → tmp 转正（崩溃不损坏旧档）
- **损坏回退**：主档读/解码失败自动回退备份档
- **串行异步写**：后台单任务"最新优先"，修复静态字段竞态
- **自动存档节流**：`SaveDataAuto` 距上次自动写不足间隔跳过；`CSaveAutoSaveHook` 定时 + 失焦/退出；
  失焦/退出走 `SaveDataAutoImmediate`（忽略节流并阻塞到落盘），避免最后一次存档丢失
- **`Flush()`**：阻塞到队列真正排空，退出前/测试断言前可调用
- **版本迁移**：文件头 version + MemoryPack VersionTolerant + `SetMigrator` 钩子
- **安全边界**：AES key 硬编码在客户端 = 混淆级（防普通读取），真安全需服务器校验
- **UniRx 格式化器内置**：装了 `com.neuecc.unirx` 即自动编译并注册（见下节），无需每个工程自己写

## UniRx 自定义格式化器（可选）

MemoryPack **没有** UniRx 类型的内建格式化器，且它靠 `[ModuleInitializer]` 自动注册、
**Unity 不支持 ModuleInitializer** —— 外部库类型只能手工注册，否则要等到序列化时才抛
"formatter is not registered"。本模块已把这份实现收进框架：

| 类型 | 覆盖 |
|---|---|
| `CReactivePropertyFormatter<T>` | 泛型 `ReactiveProperty<T>` |
| `CInt/CLong/CBool/CFloat/CDouble/CStringReactivePropertyFormatter` | 6 个常用派生类型 |
| `CReactiveCollectionFormatter<T>` | `ReactiveCollection<T>` |
| `CReactiveDictionaryFormatter<TKey,TValue>` | `ReactiveDictionary<K,V>` |
| `CUniRxSaveFormatters` | 注册入口（幂等，自动 + 可手动） |

- **可选**：位于独立程序集 `CoffeeBean.Save.UniRx`，用 `versionDefines` 监听 `com.neuecc.unirx`；
  **装了 UniRx 才编译**（`defineConstraints: ["COFFEEBEAN_UNIRX"]`），没装则整体跳过
- **自动注册**：运行时 `RuntimeInitializeOnLoadMethod`（场景加载前）+ 编辑器 `InitializeOnLoadMethod`
  （编辑模式不跑 RuntimeInitialize，而失焦存档在编辑器里也会触发）。需要显式确保时可调
  `CUniRxSaveFormatters.RegisterAll()`
- **字节兼容**：布局与手写版本一致（对象头 1 + 值 / 集合头 + 元素），换用不改变已有存档格式
- **扩展**：需要别的元素类型时 `CUniRxSaveFormatters.Register(new CReactivePropertyFormatter<MyStruct>())`，
  无需改框架
- **不含 `BigIntegerFormatter`**：MemoryPack.Core 已内建 `MemoryPack.Formatters.BigIntegerFormatter`

## 文件格式

```
{SaveDirectory}/{Slot}.sav
  [4 字节 int version][序列化数据（可选 XOR + AES 或明文）]
备份：{Slot}.sav.bak（最近一次写入的旧版）；自动档：{Slot}_auto.sav
```

## 目录结构

```
Runtime/
├── Core/        CSaveSystem / CSaveOptions / CSaveEncrypt / CSaveAutoSaveHook / ISaveSerializer
├── Serializers/ CMemoryPackSerializer / CJsonSerializer
│   └── Formatters/UniRx/  可选程序集 CoffeeBean.Save.UniRx（装了 UniRx 才编译）
└── Bridge/      与 Core 的可选集成
```

## 测试

EditMode 测试：序列化后端（MemoryPack 往返含 Dictionary / VersionTolerant 版本容错 / JSON 往返）、
存档系统（加密往返 / 损坏回退 / 版本迁移 / 自动档节流 / 删除 / 无存档容错）。

## 版本约定

- SemVer + git tag `vX.Y.Z`；每个版本对应 GitHub Release（CHANGELOG 派生说明）
