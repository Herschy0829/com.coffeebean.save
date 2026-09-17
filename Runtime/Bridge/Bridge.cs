#if COFFEEBEAN_CORE
// CoffeeBean 模块标识 + Core 生命周期集成。
// 本文件所在的 Bridge 程序集仅在安装 Core 时编译（asmdef defineConstraints），
// 因此存档核心功能不依赖 Core 也能独立工作。
using CoffeeBean;

[assembly: CoffeeBeanModule(
    "com.coffeebean.save",
    "0.2.0",
    DisplayName = "Save",
    Description = "Save system: MemoryPack serialization, AES encryption, atomic write, auto-save, version migration.",
    Dependencies = new[] { "com.coffeebean.core", "com.coffeebean.tools" }
)]

namespace CoffeeBean
{
    /// <summary>
    /// Core 集成：CSaveSystem 需业务配置（槽位 / 加密 key / 序列化后端），无法预注册默认实例；
    /// 本模块标记使 save 可被 Core 发现、启停与检查版本兼容。
    /// </summary>
    public sealed class SaveModule : ICoffeeBeanModule
    {
        public void OnLoad(CoffeeBeanContext context)
        {
            context.Log("CoffeeBean.Save integrated (create CSaveSystem on demand).");
        }

        public void OnStart()
        {
        }

        public void OnShutdown()
        {
        }
    }
}
#endif
