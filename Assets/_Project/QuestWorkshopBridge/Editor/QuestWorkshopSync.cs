using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Где правили запись с прошлой синхронизации. Unity хранит «общую точку» —
    // хеш того, чем запись была, когда мастерская и база совпадали, — в
    // QuestWorkshop/unity-sync.json. Сравнение с ней показывает направление:
    // изменилась только мастерская, только Unity или обе стороны.
    public enum WorkshopSyncDirection
    {
        Same,
        WorkshopNewer,
        UnityNewer,
        Conflict,
        Unknown
    }

    public sealed class QuestWorkshopSyncState
    {
        private readonly Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool Dirty { get; private set; }

        public string Get(string key)
        {
            return hashes.TryGetValue(key, out string value) ? value : string.Empty;
        }

        public void Set(string key, string hash)
        {
            if (Get(key) == hash)
                return;
            hashes[key] = hash;
            Dirty = true;
        }

        public WorkshopSyncDirection Direction(string key, string workshopHash, string unityHash)
        {
            if (workshopHash == unityHash)
                return WorkshopSyncDirection.Same;
            string common = Get(key);
            if (common.Length == 0)
                return WorkshopSyncDirection.Unknown;
            if (common == unityHash)
                return WorkshopSyncDirection.WorkshopNewer;
            if (common == workshopHash)
                return WorkshopSyncDirection.UnityNewer;
            return WorkshopSyncDirection.Conflict;
        }

        public static QuestWorkshopSyncState Load(string path)
        {
            QuestWorkshopSyncState state = new QuestWorkshopSyncState();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return state;
            try
            {
                JsonNode root = new JsonNode(QuestWorkshopJson.Parse(File.ReadAllText(path)));
                if (root["hashes"].Raw is Dictionary<string, object> map)
                    foreach (KeyValuePair<string, object> pair in map)
                        if (pair.Value is string hash)
                            state.hashes[pair.Key] = hash;
            }
            catch (Exception)
            {
                // Повреждённый файл — просто начинаем без общих точек.
            }
            return state;
        }

        public void Save(string path)
        {
            if (!Dirty || string.IsNullOrEmpty(path) || !Directory.Exists(Path.GetDirectoryName(path)))
                return;
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
            List<string> keys = new List<string>(hashes.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
                map[key] = hashes[key];
            Dictionary<string, object> root = new Dictionary<string, object>
            {
                ["format"] = "ks-unity-sync",
                ["version"] = 1,
                ["note"] = "Общие точки синхронизации Unity ↔ Мастерская квестов. Не править вручную.",
                ["hashes"] = map
            };
            File.WriteAllText(path, QuestWorkshopJson.Serialize(root), new UTF8Encoding(false));
            Dirty = false;
        }

        // FNV-1a 64 + длина: устойчиво между запусками, без зависимостей.
        public static string Hash(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char ch in text ?? string.Empty)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            return (text ?? string.Empty).Length.ToString("x") + ":" + hash.ToString("x16");
        }
    }
}
