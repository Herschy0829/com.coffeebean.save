# CoffeeBean Save（com.coffeebean.save）

CoffeeBean 框架的存档模块：**MemoryPack 二进制序列化**（可插拔后端）+ **AES 加密** + 原子写 / 损坏回退 / 串行异步写 / 自动存档节流 / 版本迁移。

对齐 Idle 项目的 MemoryPack 方案与自动存档调度，补齐加密 / 原子写 / 备份回退等缺口。

> 设计文档：`docs/design-save.md`（v0.1）

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.save": "https://github.com/Herschy0829/com.coffeebean.save.git#v0.3.0",
    "com.coffeebean.tools": "https://github.com/Herschy0829/com.coffeebean.tools.git#v0.6.0",
    "com.cysharp.memorypack": "https://github.com/Cysharp/MemoryPack.git?path=src/MemoryPack.Unity/Assets/MemoryPack.Unity#1.21.4"
  }
}
```

就这三条，**不需要 NuGetForUnity、不需要手动放任何 DLL、不需要联网还原**。

### MemoryPack 的二进制已内嵌在本模块里

上游 `MemoryPack.Unity` 的 git 包**只提供胶水层**（`Runtime/` 3 个文件 + `package.json`），
不含 `MemoryPack.Core.dll`，也不含 Roslyn 源生成器 `MemoryPack.Generator.dll`
（而 `MemoryPack.Unity.asmdef` 要求 `precompiledReferences: ["MemoryPack.Core.dll"]`，
所有 `[MemoryPackable]` 类型也都依赖生成器产出格式化器）。

**本模块已把这些二进制随包分发**，位于：

```
Runtime/Plugins/MemoryPack/
├── MemoryPack.Core.1.21.4/lib/netstandard2.1/MemoryPack.Core.dll
├── MemoryPack.Generator.1.21.4/analyzers/dotnet/cs/MemoryPack.Generator.dll   ← .meta 带 RoslynAnalyzer 标签
├── System.Collections.Immutable.6.0.0/...                                     ← Core 在 netstandard2.1 下的依赖
├── System.Runtime.CompilerServices.Unsafe.6.0.0/...
├── LICENSE-*.txt / THIRD-PARTY-NOTICES.md
```

所以消费工程只要按上面的 manifest 加依赖即可，MemoryPack 的运行时与源生成器都由框架提供。

### ⚠️ 不要重复提供这些 DLL

Unity 遇到**同名预编译程序集**会直接报错：`Multiple precompiled assemblies with the same name`。

- **不要**再用 NuGetForUnity 还原 MemoryPack 到 `Assets/Packages/`
- **不要**把 `MemoryPack.Core.dll` / `MemoryPack.Generator.dll` 从别处放进工程

> 上游 `com.cysharp.memorypack`（胶水层）**仍然要装** —— 它是 UPM 包，与内嵌二进制不重名，互补关系。

### 生成器是否生效的判据

编译后工程内应能搜到 `<你的类型>+<类型>Formatter`（例如 `PlayerData+PlayerDataFormatter`）——
那是源生成器产出的嵌套类型，进入程序集才说明它跑了。若 `[MemoryPackable]` 类型在运行期抛
"formatter is not registered"，几乎都是生成器没生效（多为重复/缺失 DLL 导致）。

> 升级 MemoryPack 时需同步三处：本模块内嵌的二进制、`package.json` 的依赖版本、消费工程引用的 tag。

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
├── Plugins/MemoryPack/    内嵌的 MemoryPack 二进制（Core / Generator / 依赖 + 许可声明）
└── Bridge/      与 Core 的可选集成
```

## 测试

EditMode 测试：序列化后端（MemoryPack 往返含 Dictionary / VersionTolerant 版本容错 / JSON 往返）、
存档系统（加密往返 / 损坏回退 / 版本迁移 / 自动档节流 / 删除 / 无存档容错）。

## 版本约定

- SemVer + git tag `vX.Y.Z`；每个版本对应 GitHub Release（CHANGELOG 派生说明）
