using System;
using MemoryPack;
using UnityEngine;
using UnityEngine.Scripting;

namespace CoffeeBean
{
    /// <summary>
    /// UniRx 相关 MemoryPack 格式化器的注册入口（幂等，可重复调用）。
    ///
    /// **为什么必须手动注册**：MemoryPack 的生成器通过 `[ModuleInitializer]` 自动注册格式化器，
    /// 而 **Unity 不支持 ModuleInitializer** —— 外部库类型（如 UniRx 的 <c>ReactiveProperty&lt;T&gt;</c>）
    /// 的格式化器只能手工 <see cref="MemoryPackFormatterProvider.Register{T}(MemoryPackFormatter{T})"/>。
    /// 不在序列化前完成注册，运行期会抛 "formatter is not registered"。
    ///
    /// 注册时机（与既有工程同样的策略，双保险）：
    /// · 运行时：<see cref="RuntimeInitializeOnLoadMethodAttribute"/>（场景加载前）
    /// · 编辑器：<c>[InitializeOnLoadMethod]</c> —— 编辑模式下 RuntimeInitializeOnLoadMethod 不生效，
    ///   而失焦存档（OnApplicationFocus）在编辑器里也会触发
    ///
    /// 通常**不需要**手动调用：装了 UniRx（`com.neuecc.unirx`）时本程序集会自动参与编译并自动注册。
    /// 若你在自己的启动流程里想显式确保注册，可调用 <see cref="RegisterAll"/>。
    /// </summary>
    [Preserve]
    public static class CUniRxSaveFormatters
    {
        private const string Tag = "CoffeeBean.Save.UniRx";

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

        /// <summary>注册全部 UniRx 格式化器（幂等）。</summary>
        [Preserve]
        public static void RegisterAll()
        {
            if (_registered) return;
            lock (_Lock)
            {
                if (_registered) return;

                RegisterCore();
                _registered = true;
                CLog.Info(Tag, "UniRx 自定义格式化器注册完成");
            }
        }

        /// <summary>
        /// 注册具体的格式化器集合。
        /// 覆盖类型与既有工程手写版本一致：泛型 <c>ReactiveProperty&lt;T&gt;</c> + 6 个常用派生类型、
        /// <c>ReactiveCollection&lt;T&gt;</c>、<c>ReactiveDictionary&lt;K,V&gt;</c>。
        ///
        /// 需要其他元素类型（如自定义 struct）时，用 <see cref="Register{T}"/> 追加即可，无需改框架。
        /// </summary>
        private static void RegisterCore()
        {
            // 泛型 ReactiveProperty<T>
            Register(new CReactivePropertyFormatter<int>());
            Register(new CReactivePropertyFormatter<long>());
            Register(new CReactivePropertyFormatter<bool>());
            Register(new CReactivePropertyFormatter<float>());
            Register(new CReactivePropertyFormatter<double>());
            Register(new CReactivePropertyFormatter<string>());
            Register(new CReactivePropertyFormatter<DateTime>());

            // 常用派生类型（Int/Long/Bool/Float/Double/String）
            Register(new CIntReactivePropertyFormatter());
            Register(new CLongReactivePropertyFormatter());
            Register(new CBoolReactivePropertyFormatter());
            Register(new CFloatReactivePropertyFormatter());
            Register(new CDoubleReactivePropertyFormatter());
            Register(new CStringReactivePropertyFormatter());

            // ReactiveCollection<T>
            Register(new CReactiveCollectionFormatter<int>());
            Register(new CReactiveCollectionFormatter<long>());
            Register(new CReactiveCollectionFormatter<bool>());
            Register(new CReactiveCollectionFormatter<float>());
            Register(new CReactiveCollectionFormatter<double>());
            Register(new CReactiveCollectionFormatter<string>());

            // ReactiveDictionary<K,V>
            Register(new CReactiveDictionaryFormatter<string, int>());
            Register(new CReactiveDictionaryFormatter<int, string>());
            Register(new CReactiveDictionaryFormatter<int, float>());
        }

        /// <summary>
        /// 注册单个格式化器：重复注册或不受支持时**只告警不抛出**，
        /// 避免一个类型失败导致其余格式化器都没注册上（那样会在序列化时才发现）。
        /// </summary>
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
