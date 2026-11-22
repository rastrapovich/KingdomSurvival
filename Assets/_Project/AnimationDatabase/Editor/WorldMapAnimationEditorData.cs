using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Загрузка и общие правила раздела «Глобальная карта» Базы анимаций.
    public static class WorldMapAnimationEditorData
    {
        // Атласы карты — отдельная папка внутри управляемой папки боя.
        // Картинки в git не попадают (.gitignore), их .meta — попадают.
        public const string ArtRoot = CreatureAnimationEditorData.ArtRoot + "/WorldMap";

        // Действия, которые карта умеет показывать. Ожидание необязательно:
        // на стоянке без него игра показывает первый кадр ходьбы.
        public static readonly CreatureAnimationAction[] Actions =
        {
            CreatureAnimationAction.Walk,
            CreatureAnimationAction.Idle
        };

        public static bool IsSupportedAction(CreatureAnimationAction action)
        {
            return action == CreatureAnimationAction.Walk || action == CreatureAnimationAction.Idle;
        }

        public static WorldMapAnimationDatabaseAsset LoadOrCreate()
        {
            WorldMapAnimationDatabaseAsset database =
                AssetDatabase.LoadAssetAtPath<WorldMapAnimationDatabaseAsset>(WorldMapAnimationDatabaseAsset.AssetPath);
            if (database == null)
            {
                string folder = Path.GetDirectoryName(WorldMapAnimationDatabaseAsset.AssetPath)?.Replace('\\', '/');
                CreatureAnimationEditorData.EnsureFolder(folder);
                database = ScriptableObject.CreateInstance<WorldMapAnimationDatabaseAsset>();
                database.MigrateIfNeeded();
                database.AddSet(WorldMapAnimationDatabaseAsset.DefaultHeroSetId, "Герой");
                AssetDatabase.CreateAsset(database, WorldMapAnimationDatabaseAsset.AssetPath);
                AssetDatabase.SaveAssets();
            }
            else if (database.MigrateIfNeeded())
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }
            return database;
        }

        public static string ManagedFolder(string setId)
        {
            return ArtRoot + "/" + CreatureAnimationEditorData.SanitizeFileName(setId);
        }

        public enum SetState
        {
            Empty,
            Partial,
            Ready
        }

        // «Готово» — ходьба во всех шести ракурсах; «Частично» — есть хоть
        // какие-то кадры; иначе пусто.
        public static SetState GetState(WorldMapAnimationSetData set)
        {
            if (set == null || !set.Animation.HasAnyFrames)
                return SetState.Empty;
            return set.HasCompleteWalk ? SetState.Ready : SetState.Partial;
        }

        public static string StateTitle(SetState state)
        {
            switch (state)
            {
                case SetState.Ready: return "Готово";
                case SetState.Partial: return "Частично";
                default: return "Нет кадров";
            }
        }

        // Длительность цикла «Ходьбы» в секундах по ракурсу Front (или
        // ближайшему имеющемуся); 0 — ходьбы нет.
        public static float WalkCycleSeconds(WorldMapAnimationSetData set)
        {
            if (set == null)
                return 0f;
            CreatureAnimationClip clip = CreatureAnimationResolver.Resolve(set.Animation, CreatureAnimationAction.Walk, CreatureAnimationDirection.Front);
            return clip != null && clip.Action == CreatureAnimationAction.Walk ? clip.Duration : 0f;
        }

        // Скорость без скольжения: шаг ходьбы / длительность цикла, клеток/с.
        public static float SlideFreeSpeed(WorldMapAnimationSetData set)
        {
            float duration = WalkCycleSeconds(set);
            return set != null && duration > 0.0001f ? set.StrideHexesPerCycle / duration : 0f;
        }

        // Атласы папки карты, на которые не ссылается ни один набор карты.
        public static List<string> FindUnusedAtlases(WorldMapAnimationDatabaseAsset database)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (database != null)
            {
                foreach (WorldMapAnimationSetData mapSet in database.Sets)
                {
                    if (mapSet == null)
                        continue;
                    foreach (CreatureAnimationClipData clip in mapSet.Animation.Clips)
                    {
                        if (clip == null)
                            continue;
                        foreach (CreatureAnimationFrames cell in clip.Directions)
                        {
                            if (cell == null)
                                continue;
                            foreach (Sprite sprite in cell.Frames)
                            {
                                if (sprite != null)
                                    used.Add(AssetDatabase.GetAssetPath(sprite));
                            }
                        }
                    }
                }
            }

            List<string> unused = new List<string>();
            if (!AssetDatabase.IsValidFolder(ArtRoot))
                return unused;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).Contains("__") && !used.Contains(path))
                    unused.Add(path);
            }
            return unused;
        }

        // Ошибки данных наборов карты (битые ссылки на кадры, FPS, таблица, герой).
        public static List<string> CollectDataErrors(WorldMapAnimationDatabaseAsset database)
        {
            List<string> errors = new List<string>();
            if (database == null)
                return errors;
            if (!database.IsDirectionMapValid())
                errors.Add("Таблица ракурсов карты: каждый ракурс должен смотреть в свой сектор.");

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldMapAnimationSetData mapSet in database.Sets)
            {
                if (mapSet == null || string.IsNullOrWhiteSpace(mapSet.Id))
                {
                    errors.Add("Набор без ID.");
                    continue;
                }
                if (!ids.Add(mapSet.Id))
                    errors.Add("Повторяющийся ID набора: " + mapSet.Id + ".");
                foreach (CreatureAnimationClipData clip in mapSet.Animation.Clips)
                {
                    if (clip == null)
                        continue;
                    string where = mapSet.DisplayName + " · " + CreatureAnimationLabels.ActionTitle(clip.Action);
                    if (clip.RawFramesPerSecond < CreatureAnimationClipData.MinFramesPerSecond ||
                        clip.RawFramesPerSecond > CreatureAnimationClipData.MaxFramesPerSecond)
                    {
                        errors.Add(where + ": недопустимая скорость " + clip.RawFramesPerSecond + " кадров/с (нужно 1–60).");
                    }
                    foreach (CreatureAnimationFrames cell in clip.Directions)
                    {
                        if (cell == null)
                            continue;
                        int broken = 0;
                        for (int i = 0; i < cell.FrameCount; i++)
                        {
                            if (cell.Frames[i] == null)
                                broken++;
                        }
                        if (broken > 0)
                        {
                            errors.Add(where + " → " + CreatureAnimationLabels.DirectionTitle(cell.Direction) + ": " + broken +
                                       " кадр(ов) без картинки (файл атласа удалён или не на этом компьютере).");
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(database.HeroSetId) && database.FindSet(database.HeroSetId) == null)
                errors.Add("Герой на карте: набора «" + database.HeroSetId + "» нет.");
            return errors;
        }
    }
}
