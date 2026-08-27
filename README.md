# CoffeeBean Save（com.coffeebean.save）

CoffeeBean 框架的存档模块：**MemoryPack 二进制序列化**（可插拔后端）+ **AES 加密** + 原子写 / 损坏回退 / 串行异步写 / 自动存档节流 / 版本迁移。

对齐 Idle 项目的 MemoryPack 方案与自动存档调度，补齐加密 / 原子写 / 备份回退等缺口。

> 设计文档：`docs/design-save.md`（v0.1）

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.save": "https://github.com/Herschy0829/com.coffeebean.save.git#v0.1.0",
    "com.coffeebean.tools": "https://github.com/Herschy0829/com.coffeebean.tools.git#v0.5.0",
    "com.cysharp.memorypack": "https://github.com/Cysharp/MemoryPack.git?path=src/MemoryPack"  // 或本地副本
  }
}
```

> `com.cysharp.memorypack` 仅**声明依赖**，来源由消费工程提供（git 或 file: 本地副本均可）。

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
- **自动存档节流**：`SaveDataAuto` 距上次自动写不足间隔跳过；`CSaveAutoSaveHook` 定时 + 失焦/退出
- **版本迁移**：文件头 version + MemoryPack VersionTolerant + `SetMigrator` 钩子
- **安全边界**：AES key 硬编码在客户端 = 混淆级（防普通读取），真安全需服务器校验

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
└── Bridge/      与 Core 的可选集成
```

## 测试

EditMode 测试：序列化后端（MemoryPack 往返含 Dictionary / VersionTolerant 版本容错 / JSON 往返）、
存档系统（加密往返 / 损坏回退 / 版本迁移 / 自动档节流 / 删除 / 无存档容错）。

## 版本约定

- SemVer + git tag `vX.Y.Z`；每个版本对应 GitHub Release（CHANGELOG 派生说明）
