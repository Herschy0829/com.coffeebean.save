using System;
using MemoryPack;
using UnityEngine;
using UnityEngine.Scripting;

namespace CoffeeBean
{
    /// <summary>
    /// 存档模块自带格式化器的注册入口（幂等，可重复调用）。
    ///
    /// **为什么必须手动注册**：MemoryPack 靠 <c>[ModuleInitializer]</c> 自动注册格式化器，
    /// 而 **Unity 不支持 ModuleInitializer**；外部类型（如 <c>System.Numerics.BigInteger</c>）
    /// 的格式化器只能手工 <see cref="MemoryPackFormatterProvider.Register{T}"/>。
    /// 不在序列化前注册完，运行期会抛 "formatter is not registered"。
    ///
    /// 注意 <see cref="BigInteger"/> 的情况特殊：MemoryPack.Core 里的内建版**不可用**
    /// （NuGet 那份 DLL 把上游一条有缺陷的分支编了进去，详见 <see cref="CBigIntegerFormatter"/> 注释），
    /// 所以这里注册的是本模块自带的实现，布局与工程历史存档一致。
    ///
    /// 注册时机（运行时 + 编辑器双保险，与 UniRx 那套一致）：
    /// · 运行时：<see cref="RuntimeInitializeOnLoadMethodAttribute"/>（场景加载前）
    /// · 编辑器：<c>[InitializeOnLoadMethod]</c> —— 编辑模式下 RuntimeInitializeOnLoadMethod 不生效，
    ///   而编辑器里也会存档（比如失焦自动存）
    /// </summary>
    [Preserve]
    public static class CSaveFormatters
    {
        private const string Tag = "CoffeeBean.Save";

        private static volatile bool _registered;
        private static readonly object _Lock = new object();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        [Preserve]
        private static void AutoRegisterRuntime() => RegisterAll();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        [Preserve]
        private static void AutoRegisterEditor() => RegisterAll();
#endif

        /// <summary>是否已完成注册。</summary>
        public static bool IsRegistered => _registered;

        /// <summary>注册存档模块自带的全部格式化器（幂等）。</summary>
        [Preserve]
        public static void RegisterAll()
        {
            if (_registered) return;
            lock (_Lock)
            {
                if (_registered) return;

                Register(new CBigIntegerFormatter());

                _registered = true;
                CLog.Info(Tag, "存档模块自带格式化器注册完成");
            }
        }

        /// <summary>注册单个格式化器：重复注册或不受支持时**只告警不抛出**，避免一个失败拖垮其余。</summary>
        [Preserve]
        public static void Register<T>(MemoryPackFormatter<T> formatter)
        {
            if (formatter == null) return;
            try
            {
                MemoryPackFormatterProvider.Register(formatter);
            }
            catch (Exception e)
            {
                CLog.Warn(Tag, $"注册 {typeof(T).Name} 格式化器失败（已跳过）: {e.Message}");
            }
        }
    }
}
