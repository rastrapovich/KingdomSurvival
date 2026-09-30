using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Загрузка баз и связь «существо → набор» для обоих окон.
    public static class CreatureAnimationEditorData
    {
        public const string UnitDatabasePath = "Assets/_Project/UnitDatabase/Resources/UnitDatabase/KingdomSurvivalUnits.asset";
        // Управляемая папка атласов. Картинки в git не попадают (.gitignore),
        // их .meta — попадают.
        public const string ArtRoot = "Assets/_Project/Art/Animations";
        public const string AnimationWindowMenu = "Kingdom Survival/База анимаций";
        public const string UnitWindowMenu = "Kingdom Survival/База существ";

        public static event Action DataChanged;

        // База анимаций существует всегда: бой читает её из Resources.
        [InitializeOnLoadMethod]
        private static void EnsureDatabaseOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    LoadOrCreate();
            };
        }

        public static void NotifyChanged()
        {
            DataChanged?.Invoke();
        }

        public static CreatureAnimationDatabaseAsset LoadOrCreate()
        {
            CreatureAnimationDatabaseAsset database =
                AssetDatabase.LoadAssetAtPath<CreatureAnimationDatabaseAsset>(CreatureAnimationDatabaseAsset.AssetPath);
            if (database == null)
            {
                string folder = Path.GetDirectoryName(CreatureAnimationDatabaseAsset.AssetPath)?.Replace('\\', '/');
                EnsureFolder(folder);
                database = ScriptableObject.CreateInstance<CreatureAnimationDatabaseAsset>();
                database.MigrateIfNeeded();
                AssetDatabase.CreateAsset(database, CreatureAnimationDatabaseAsset.AssetPath);
                AssetDatabase.SaveAssets();
            }
            else if (database.MigrateIfNeeded())
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }
            return database;
        }

        public static UnitDatabaseAsset LoadUnits()
        {
            return AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(UnitDatabasePath);
        }

        public static List<UnitDefinitionData> FindUsers(UnitDatabaseAsset units, string setId)
        {
            List<UnitDefinitionData> users = new List<UnitDefinitionData>();
            if (units == null || string.IsNullOrWhiteSpace(setId))
                return users;
            foreach (UnitDefinitionData unit in units.Units)
            {
                if (unit != null && string.Equals(unit.AnimationSetId, setId, StringComparison.Ordinal))
                    users.Add(unit);
            }
            return users;
        }

        public static string DescribeUsers(UnitDatabaseAsset units, string setId)
        {
            List<UnitDefinitionData> users = FindUsers(units, setId);
            return users.Count == 0
                ? "никто"
                : string.Join(", ", users.Select(unit => string.IsNullOrWhiteSpace(unit.DisplayLabel) ? unit.Id : unit.DisplayLabel));
        }

        // Назначение набора существу через SerializedObject: Undo и сохранение
        // работают так же, как у полей Базы существ.
        public static bool AssignSet(UnitDatabaseAsset units, string unitId, string setId)
        {
            if (units == null || string.IsNullOrWhiteSpace(unitId))
                return false;
            SerializedObject serialized = new SerializedObject(units);
            SerializedProperty list = serialized.FindProperty("units");
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty unit = list.GetArrayElementAtIndex(i);
                if (unit.FindPropertyRelative("id").stringValue != unitId)
                    continue;
                SerializedProperty link = unit.FindPropertyRelative("animationSetId");
                if (link.stringValue == (setId ?? string.Empty))
                    return false;
                link.stringValue = setId ?? string.Empty;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(units);
                NotifyChanged();
                return true;
            }
            return false;
        }

        public static CreatureAnimationSetData FindSetOf(CreatureAnimationDatabaseAsset database, UnitDefinitionData unit)
        {
            return database != null && unit != null ? database.FindSet(unit.AnimationSetId) : null;
        }

        // Новый набор для существа: ID по ID существа, масштаб — от старой миниатюры,
        // чтобы размер на поле не прыгнул и не применился дважды.
        public static CreatureAnimationSetData CreateSetFor(
            CreatureAnimationDatabaseAsset database,
            UnitDatabaseAsset units,
            UnitDefinitionData unit)
        {
            if (database == null || unit == null)
                return null;
            Undo.RecordObject(database, "Создать набор анимаций");
            CreatureAnimationSetData set = database.AddSet(database.MakeUniqueSetId(unit.Id), unit.DisplayLabel);
            set.SetFieldScale(unit.BattlefieldScale);
            EditorUtility.SetDirty(database);
            Undo.RecordObject(units, "Назначить набор анимаций");
            AssignSet(units, unit.Id, set.Id);
            NotifyChanged();
            return set;
        }

        public static string ManagedFolder(string setId)
        {
            return ArtRoot + "/" + SanitizeFileName(setId);
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "set";
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char symbol in value.Trim())
            {
                bool allowed = (symbol >= 'a' && symbol <= 'z') || (symbol >= 'A' && symbol <= 'Z') ||
                               (symbol >= '0' && symbol <= '9') || symbol == '_' || symbol == '-';
                builder.Append(allowed ? symbol : '_');
            }
            return builder.ToString();
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
                return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        // Диагностика полноты набора: работать бой будет, но художнику видно,
        // чего не хватает.
        public static List<string> CollectContentWarnings(CreatureAnimationSetData set, UnitDefinitionData unit)
        {
            List<string> warnings = new List<string>();
            if (set == null)
                return warnings;
            string who = unit != null && !string.IsNullOrWhiteSpace(unit.DisplayLabel) ? unit.DisplayLabel : set.DisplayName;
            bool ranged = unit != null && unit.AttackRange > 1;

            List<CreatureAnimationAction> required = new List<CreatureAnimationAction>
            {
                CreatureAnimationAction.Idle,
                CreatureAnimationAction.Walk,
                CreatureAnimationAction.Death,
                ranged ? CreatureAnimationAction.Shoot : CreatureAnimationAction.Attack
            };
            foreach (CreatureAnimationAction action in required)
            {
                CreatureAnimationClipData clip = set.FindClip(action);
                if (clip == null || !clip.HasAnyFrames)
                {
                    warnings.Add(who + ": нет действия «" + CreatureAnimationLabels.ActionTitle(action) + "» — " + FallbackNote(action) + ".");
                    continue;
                }
                AddMissingDirections(warnings, who, clip);
            }

            CreatureAnimationClipData hit = set.FindClip(CreatureAnimationAction.Hit);
            if (hit == null || !hit.HasAnyFrames)
                warnings.Add(who + ": нет «Получения удара» (желательно) — в бою реакция покажется без него.");
            else
                AddMissingDirections(warnings, who, hit);

            foreach (CreatureAnimationClipData clip in set.Clips)
            {
                if (clip == null || required.Contains(clip.Action) || clip.Action == CreatureAnimationAction.Hit || !clip.HasAnyFrames)
                    continue;
                AddMissingDirections(warnings, who, clip);
            }
            return warnings;
        }

        private static void AddMissingDirections(List<string> warnings, string who, CreatureAnimationClipData clip)
        {
            List<string> missing = new List<string>();
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
            {
                CreatureAnimationFrames cell = clip.FindDirection(direction);
                if (cell == null || !cell.HasFrames)
                    missing.Add(CreatureAnimationLabels.DirectionTitle(direction));
            }
            if (missing.Count > 0)
            {
                warnings.Add(who + ": «" + CreatureAnimationLabels.ActionTitle(clip.Action) + "» — нет ракурсов: " +
                             string.Join(", ", missing) + ". До загрузки показывается ближайший имеющийся.");
            }
        }

        public static string FallbackNote(CreatureAnimationAction action)
        {
            switch (action)
            {
                case CreatureAnimationAction.Idle: return "стоит первый доступный кадр или старая миниатюра";
                case CreatureAnimationAction.Walk: return "при перемещении будет ожидание";
                case CreatureAnimationAction.Death: return "павший остаётся последним кадром с затемнением";
                case CreatureAnimationAction.Shoot: return "выстрел покажется атакой, если она есть";
                case CreatureAnimationAction.Attack: return "удар покажется выстрелом, если он есть, иначе коротким выпадом";
                default: return "будет ожидание";
            }
        }

        // Ошибки данных, из-за которых набор нельзя применить: битые ссылки,
        // недопустимый FPS, маркер вне клипа, повтор ID.
        public static List<string> CollectDataErrors(CreatureAnimationDatabaseAsset database, UnitDatabaseAsset units)
        {
            List<string> errors = new List<string>();
            if (database == null)
                return errors;
            if (!database.IsDirectionMapValid())
                errors.Add("Таблица ракурсов: каждый ракурс должен смотреть в своё направление гекса.");

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CreatureAnimationSetData set in database.Sets)
            {
                if (set == null || string.IsNullOrWhiteSpace(set.Id))
                {
                    errors.Add("Набор без ID.");
                    continue;
                }
                if (!ids.Add(set.Id))
                    errors.Add("Повторяющийся ID набора: " + set.Id + ".");
                foreach (CreatureAnimationClipData clip in set.Clips)
                {
                    if (clip == null)
                        continue;
                    string where = set.DisplayName + " · " + CreatureAnimationLabels.ActionTitle(clip.Action) +
                                   (string.IsNullOrEmpty(clip.ClipKey) ? string.Empty : " [" + clip.ClipKey + "]");
                    if (clip.RawFramesPerSecond < CreatureAnimationClipData.MinFramesPerSecond ||
                        clip.RawFramesPerSecond > CreatureAnimationClipData.MaxFramesPerSecond)
                    {
                        errors.Add(where + ": недопустимая скорость " + clip.RawFramesPerSecond + " кадров/с (нужно 1–60).");
                    }
                    if (clip.RawImpactTime < 0f || clip.RawImpactTime > 1f)
                        errors.Add(where + ": маркер удара за пределами клипа.");
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
                        if (clip.UseSourceTiming && cell.FrameCount > 0 && !cell.HasUsableSourceNumbers)
                        {
                            errors.Add(where + " → " + CreatureAnimationLabels.DirectionTitle(cell.Direction) +
                                       ": включён исходный темп, но у кадров нет номеров Blender.");
                        }
                    }
                }
            }

            if (units != null)
            {
                foreach (UnitDefinitionData unit in units.Units)
                {
                    if (unit != null && !string.IsNullOrWhiteSpace(unit.AnimationSetId) && database.FindSet(unit.AnimationSetId) == null)
                        errors.Add(unit.Id + ": ссылка на несуществующий набор анимаций «" + unit.AnimationSetId + "».");
                }
            }
            return errors;
        }
    }
}
