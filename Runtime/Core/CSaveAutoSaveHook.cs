using System;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 自动存档调度器（对齐 Idle 的定时 + 失焦/退出自动存档）：
    /// 挂载后每 interval 秒调用一次 <see cref="CSaveSystem.SaveDataAuto"/>（内部节流），
    /// 应用失焦（OnApplicationPause）与退出（OnApplicationQuit）时自动存档。
    /// </summary>
    public sealed class CSaveAutoSaveHook : MonoBehaviour
    {
        private CSaveSystem _save;
        private Func<object> _provider;
        private float _timer;
        private float _interval;

        /// <summary>创建并挂载自动存档钩子（DontDestroyOnLoad，返回实例；Interval 秒一次）。</summary>
        public static CSaveAutoSaveHook Attach<T>(CSaveSystem save, Func<T> provider, float intervalSeconds)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            var go = new GameObject("[CoffeeBean] SaveAutoSave");
            DontDestroyOnLoad(go);
            var hook = go.AddComponent<CSaveAutoSaveHook>();
            hook._save = save;
            hook._provider = () => provider();
            hook._interval = Mathf.Max(0f, intervalSeconds);
            return hook;
        }

        private void Update()
        {
            if (_interval <= 0f) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer >= _interval)
            {
                _timer = 0f;
                _save.SaveDataAuto(_provider());
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) _save.SaveDataAuto(_provider());
        }

        private void OnApplicationQuit()
        {
            _save.SaveDataAuto(_provider());
        }
    }
}
