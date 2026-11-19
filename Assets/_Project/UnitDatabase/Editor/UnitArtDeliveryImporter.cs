using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Editor
{
    // Загрузка художественной поставки целиком (19.11.2026,
    // ProjectDocs/CREATURE_VIEWS_ART_BRIEF.md): папка с файлами
    // «<ID существа>__portrait|field__<ID вида>.png» в любых подпапках.
    // Рисунок «Обычного» становится основным полем существа, рисунок
    // другого вида — его слотом вида. Каждый файл проходит через
    // UnitArtImporter (копия в управляемую папку, импорт Sprite).
    public static class UnitArtDeliveryImporter
    {
        public const string FolderArgument = "-unitArtDeliveryFolder";

        public readonly struct DeliveryFile
        {
            public DeliveryFile(string path, string unitId, UnitArtKind kind, string viewId)
            {
                Path = path;
                UnitId = unitId;
                Kind = kind;
                ViewId = viewId;
            }

            public string Path { get; }
            public string UnitId { get; }
            public UnitArtKind Kind { get; }
            public string ViewId { get; }
        }

        // Имя файла → существо, вид рисунка и вид карты. Null — имя не по
        // правилу поставки.
        public static DeliveryFile? Parse(string path)
        {
            if (!UnitArtImporter.IsImageFile(path))
                return null;
            string[] parts = System.IO.Path.GetFileNameWithoutExtension(path).Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]))
                return null;
            UnitArtKind kind;
            if (parts[1] == "portrait")
                kind = UnitArtKind.Portrait;
            else if (parts[1] == "field")
                kind = UnitArtKind.Battlefield;
            else
                return null;
            if (!UnitArtViews.Ids.Contains(parts[2]))
                return null;
            return new DeliveryFile(path, parts[0], kind, parts[2]);
        }

        [MenuItem("Kingdom Survival/База существ/Загрузить поставку рисунков по видам…")]
        private static void ImportFromMenu()
        {
            string folder = EditorUtility.OpenFolderPanel("Папка поставки рисунков", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(folder))
                return;
            string report = Import(folder);
            EditorUtility.DisplayDialog("Поставка рисунков", report, "Ок");
        }

        // Для -batchmode -executeMethod: папка в аргументе -unitArtDeliveryFolder.
        public static void ImportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, FolderArgument);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Нужен аргумент " + FolderArgument + " <папка>.");
            Debug.Log("[UnitArtDelivery]\n" + Import(args[index + 1]));
        }

        public static string Import(string folder)
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            if (database == null)
                return "База существ не найдена.";
            if (!Directory.Exists(folder))
                return "Папка не найдена: " + folder;

            StringBuilder report = new StringBuilder();
            List<DeliveryFile> files = new List<DeliveryFile>();
            foreach (string path in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).OrderBy(item => item, StringComparer.Ordinal))
            {
                DeliveryFile? parsed = Parse(path);
                if (parsed == null)
                {
                    if (UnitArtImporter.IsImageFile(path))
                        report.AppendLine("Пропущен (имя не по правилу): " + System.IO.Path.GetFileName(path));
                    continue;
                }
                if (database.FindById(parsed.Value.UnitId) == null)
                {
                    report.AppendLine("Пропущен (нет существа «" + parsed.Value.UnitId + "»): " + System.IO.Path.GetFileName(path));
                    continue;
                }
                files.Add(parsed.Value);
            }

            Undo.RecordObject(database, "Поставка рисунков по видам");
            int classic = 0;
            int views = 0;
            List<(string unitId, string property, Sprite sprite)> classicFields = new List<(string, string, Sprite)>();
            foreach (DeliveryFile file in files)
            {
                Sprite sprite = UnitArtImporter.ImportExternal(file.Path, file.UnitId, file.Kind, file.ViewId, out string error);
                if (sprite == null)
                {
                    report.AppendLine("Ошибка: " + error);
                    continue;
                }
                if (UnitArtViews.IsClassic(file.ViewId))
                {
                    classicFields.Add((file.UnitId, file.Kind == UnitArtKind.Portrait ? "portrait" : "battlefieldSprite", sprite));
                    classic++;
                    continue;
                }
                UnitViewArtData slot = database.FindById(file.UnitId).GetOrAddViewArt(file.ViewId);
                if (file.Kind == UnitArtKind.Portrait)
                    slot.SetPortrait(sprite);
                else
                    slot.SetBattlefieldSprite(sprite);
                views++;
            }
            EditorUtility.SetDirty(database);

            // Поля «Обычного» закрыты — через SerializedObject, как окно Базы.
            SerializedObject serialized = new SerializedObject(database);
            SerializedProperty units = serialized.FindProperty("units");
            foreach ((string unitId, string property, Sprite sprite) in classicFields)
            {
                for (int i = 0; i < units.arraySize; i++)
                {
                    SerializedProperty unit = units.GetArrayElementAtIndex(i);
                    if (unit.FindPropertyRelative("id").stringValue != unitId)
                        continue;
                    unit.FindPropertyRelative(property).objectReferenceValue = sprite;
                    break;
                }
            }
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            report.Insert(0, "Загружено: " + (classic + views) + " (основные рисунки — " + classic + ", слоты видов — " + views + ")." +
                             Environment.NewLine);
            return report.ToString();
        }
    }
}
