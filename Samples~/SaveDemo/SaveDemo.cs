using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

namespace CoffeeBean.Save.Demo
{
    /// <summary>演示存档数据（MemoryPackable，VersionTolerant 便于字段演进）。</summary>
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class DemoPlayerData
    {
        [MemoryPackOrder(0)] public int Level;
        [MemoryPackOrder(1)] public string PlayerName;
        [MemoryPackOrder(2)] public long Gold;
        [MemoryPackOrder(3)] public List<int> UnlockedHeroes;
    }

    /// <summary>
    /// 存档模块示例（场景挂载后运行，IMGUI 演示）：
    /// 保存 / 读取（MemoryPack + AES 加密）、自动档（节流）、删除存档。
    /// 存档文件位于 persistentDataPath/main.sav。
    /// </summary>
    public sealed class SaveDemo : MonoBehaviour
    {
        private CSaveSystem _save;
        private DemoPlayerData _player = new DemoPlayerData
        {
            Level = 1,
            PlayerName = "冒险者",
            Gold = 0,
            UnlockedHeroes = new List<int> { 1 },
        };

        private string _status = "";
        private readonly List<string> _log = new List<string>();

        private void Awake()
        {
            _save = new CSaveSystem(new CSaveOptions
            {
                Slot = "main",
                EncryptionKey = "demo-save-key-0123456789abcdef", // 项目应使用固定 key
                Version = 1,
            });
        }

        private void OnDestroy()
        {
            _save.SaveData(_player); // 退出前保存
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 460, 420));

            GUILayout.Label("<b>存档演示（MemoryPack + AES 加密）</b>");
            GUILayout.Label($"存档文件: {_save.SaveFilePath}");

            // 编辑玩家数据
            GUILayout.BeginHorizontal();
            GUILayout.Label("名字:", GUILayout.Width(50));
            _player.PlayerName = GUILayout.TextField(_player.PlayerName, GUILayout.Width(150));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("等级:", GUILayout.Width(50));
            _player.Level = Mathf.Max(1, int.Parse(GUILayout.TextField(_player.Level.ToString(), GUILayout.Width(60))));
            GUILayout.Label("金币:", GUILayout.Width(50));
            _player.Gold = long.Parse(GUILayout.TextField(_player.Gold.ToString(), GUILayout.Width(80)));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存", GUILayout.Width(90), GUILayout.Height(30))) { _save.SaveData(_player); _status = "已保存（原子写 + AES 加密）"; Log("保存"); }
            if (GUILayout.Button("读取", GUILayout.Width(90), GUILayout.Height(30))) { Load(); }
            if (GUILayout.Button("自动档（节流）", GUILayout.Width(130), GUILayout.Height(30))) { AutoSave(); }
            if (GUILayout.Button("删除存档", GUILayout.Width(100), GUILayout.Height(30))) { _save.DeleteSlot(); _player = new DemoPlayerData(); _status = "已删除"; Log("删除存档"); }
            GUILayout.EndHorizontal();

            GUILayout.Label(_status);
            GUILayout.Space(6);
            GUILayout.Label("说明：SaveData 序列化+加密在主线程、文件写入后台异步（串行最新优先）；");
            GUILayout.Label("崩溃时 tmp 残留不损坏旧档，主档损坏自动回退 .bak；版本号可配合 SetMigrator 迁移旧档。");
            GUILayout.Space(6);

            GUILayout.Label("<b>日志</b>");
            GUILayout.BeginVertical(GUILayout.Height(160));
            int start = Mathf.Max(0, _log.Count - 12);
            for (int i = start; i < _log.Count; i++) GUILayout.Label(_log[i]);
            GUILayout.EndVertical();

            GUILayout.EndArea();
        }

        private void Load()
        {
            DemoPlayerData loaded = _save.LoadData<DemoPlayerData>();
            if (loaded == null)
            {
                _status = "无存档（返回默认）";
                Log("读取：无存档");
                return;
            }
            _player = loaded;
            _status = $"已读取：{_player.PlayerName} Lv.{_player.Level} 金币 {_player.Gold}";
            Log("读取成功");
        }

        private void AutoSave()
        {
            bool saved = _save.SaveDataAuto(_player);
            _status = saved ? "自动档已写入（main_auto.sav）" : "自动档被节流跳过（间隔内）";
            Log(saved ? "自动档写入" : "自动档节流跳过");
        }

        private void Log(string message) => _log.Add($"[{Time.time:F1}] {message}");
    }
}
