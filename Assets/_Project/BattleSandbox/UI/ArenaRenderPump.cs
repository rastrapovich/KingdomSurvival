using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    // Рендер арены вне обновления панелей UI Toolkit: запросы копятся за кадр
    // и выполняются в LateUpdate скрытого объекта (как живая карта мира).
    internal sealed class ArenaRenderPump : MonoBehaviour
    {
        private static ArenaRenderPump instance;
        private static readonly System.Collections.Generic.List<MapLabArenaDriver> requests = new System.Collections.Generic.List<MapLabArenaDriver>();

        public static void Request(MapLabArenaDriver driver)
        {
            if (!requests.Contains(driver))
                requests.Add(driver);
#if UNITY_EDITOR
            // Вне Play Mode (окно «Генератор арен») LateUpdate не идёт: кадр —
            // после обновления окон редактора, тоже вне обновления панелей.
            if (!Application.isPlaying)
            {
                if (!editorQueued)
                {
                    editorQueued = true;
                    UnityEditor.EditorApplication.delayCall += FlushEditor;
                }
                return;
            }
#endif
            if (instance != null)
                return;
            // Объект сцены боя: уходит вместе со сценой и Play Mode, при новом
            // запросе создаётся снова.
            GameObject host = new GameObject("MapLab Arena Render") { hideFlags = HideFlags.HideInHierarchy };
            instance = host.AddComponent<ArenaRenderPump>();
        }

#if UNITY_EDITOR
        private static bool editorQueued;

        private static void FlushEditor()
        {
            editorQueued = false;
            Flush();
        }
#endif

        private void LateUpdate() => Flush();

        private static void Flush()
        {
            if (KingdomSurvival.MapLabBridge.MapLabArena.HasLive)
                KingdomSurvival.MapLabBridge.MapLabArena.StripOtherCameras();
            if (requests.Count == 0)
                return;
            MapLabArenaDriver[] pending = requests.ToArray();
            requests.Clear();
            foreach (MapLabArenaDriver driver in pending)
                driver.RenderRequested();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
