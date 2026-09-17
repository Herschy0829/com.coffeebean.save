# 第三方组件声明（内嵌的 MemoryPack 二进制）

本目录随 `com.coffeebean.save` 一起分发以下**第三方二进制**，目的是让消费工程不必自行提供 NuGet 产物。

| 文件 | 组件 | 版本 | 许可 | 来源 |
|---|---|---|---|---|
| `MemoryPack.Core.1.21.4/lib/netstandard2.1/MemoryPack.Core.dll` | MemoryPack.Core | 1.21.4 | MIT | github.com/Cysharp/MemoryPack（tag `1.21.4`，commit `0f2fe0827907940768293b7bb81b9605a7d8cba8`） |
| `MemoryPack.Generator.1.21.4/analyzers/dotnet/cs/MemoryPack.Generator.dll` | MemoryPack.Generator | 1.21.4 | MIT | 同上 |
| `System.Collections.Immutable.6.0.0/lib/netstandard2.0/System.Collections.Immutable.dll` | System.Collections.Immutable | 6.0.0 | MIT | NuGet（dotnet/runtime） |
| `System.Runtime.CompilerServices.Unsafe.6.0.0/lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll` | System.Runtime.CompilerServices.Unsafe | 6.0.0 | MIT | NuGet（dotnet/runtime） |

版权与许可全文：
- MemoryPack / MemoryPack.Core / MemoryPack.Generator —— © Cysharp, Inc.，MIT，见 `LICENSE-MemoryPack.txt`
- System.Collections.Immutable —— © .NET Foundation and Contributors，MIT，见 `LICENSE-System.Collections.Immutable.txt`
- System.Runtime.CompilerServices.Unsafe —— © .NET Foundation and Contributors，MIT，见 `LICENSE-System.Runtime.CompilerServices.Unsafe.txt`

后两者是 MemoryPack.Core 在 `.NETStandard 2.1` 下的依赖（见其 nuspec 的 dependency 声明）。

## 为什么内嵌

上游 `MemoryPack.Unity` 的 **UPM git 包只提供胶水层**（`Runtime/` 3 个文件 + `package.json`），
**既不含** `MemoryPack.Core.dll`，**也不含** Roslyn 源生成器 `MemoryPack.Generator.dll`；
而 `MemoryPack.Unity.asmdef` 里写着 `precompiledReferences: ["MemoryPack.Core.dll"]`，
且所有 `[MemoryPackable]` 类型都依赖生成器产出格式化器。

上游的官方做法是引入 **NuGetForUnity** 还原 NuGet 包；本模块改为**直接内嵌这些二进制**：
消费工程只需加一条 git 依赖，不需要额外的编辑器工具，也不需要联网还原。
实测：只做 git 引用而不提供这些二进制，会在消费工程产生 87 条 `CS0234/CS0246`。

## ⚠️ 不要重复提供（否则编译报错）

Unity 遇到**同名预编译程序集**会直接报错：`Multiple precompiled assemblies with the same name`。
使用本模块时**不要**再从别处提供这些 DLL，包括：

- NuGetForUnity 还原到 `Assets/Packages/` 的 `MemoryPack.Core.dll` / `MemoryPack.Generator.dll`
- 任何其它方式放进工程的 MemoryPack 二进制

> `com.cysharp.memorypack`（上游**胶水层**）仍然按依赖正常安装 —— 它是 UPM 包，
> 与本目录的二进制不重名，两者互补。

## 升级须知

升级 MemoryPack 时三处必须同步，否则轻则生成器与运行时版本不匹配、重则编译失败：

1. 本目录的二进制（换成新版本 NuGet 还原产物，并保留生成器 `.meta` 的 `RoslynAnalyzer` 标签）
2. `com.coffeebean.save/package.json` 的 `com.cysharp.memorypack` 依赖版本
3. 消费工程引用的 `com.cysharp.memorypack` tag

> 二进制是从 NuGet 还原产物**原样复制**的；`.meta` 使用**重新生成的 GUID**，
> 以免与消费工程里可能存在的同源副本撞 GUID。
