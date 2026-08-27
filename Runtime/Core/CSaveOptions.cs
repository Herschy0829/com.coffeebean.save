using UnityEngine;

namespace CoffeeBean
{
    /// <summary>存档配置。</summary>
    public sealed class CSaveOptions
    {
        /// <summary>主存档槽位名（文件名 = {Slot}.sav；自动档 = {Slot}_auto.sav）。</summary>
        public string Slot = "main";

        /// <summary>存档目录（默认 Application.persistentDataPath）。</summary>
        public string SaveDirectory;

        /// <summary>加密 key（空 = 不加密）。建议项目固定一个 key。</summary>
        public string EncryptionKey;

        /// <summary>加密时是否叠加 XOR 混淆层（默认 true）。</summary>
        public bool UseXorObfuscation = true;

        /// <summary>序列化后端（默认 MemoryPack）。</summary>
        public ISaveSerializer Serializer;

        /// <summary>当前存档版本（文件头 version；升级时配合迁移钩子）。</summary>
        public int Version = 1;

        /// <summary>自动存档间隔（秒；0 = 关闭自动存档）。</summary>
        public float AutoSaveInterval = 120f;

        /// <summary>自动存档节流：距上次自动写不足该秒数则跳过。</summary>
        public float AutoSaveMinInterval = 10f;

        public CSaveOptions()
        {
            SaveDirectory = Application.persistentDataPath;
            Serializer = CMemoryPackSerializer.Instance;
        }

        internal string ResolveDirectory()
            => string.IsNullOrEmpty(SaveDirectory) ? Application.persistentDataPath : SaveDirectory;
    }
}
