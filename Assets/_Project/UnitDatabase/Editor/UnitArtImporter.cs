using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Editor
{
    public enum UnitArtKind
    {
        Portrait,
        Battlefield
    }

    // Загрузка портрета и полевой миниатюры существа, как в Базе анимаций:
    // внешний PNG/JPG копируется в управляемую папку проекта и импортируется
    // как Sprite. Картинки в git не попадают (.gitignore), их .meta — попадают.
    // Каждая загрузка пишет новый файл, чтобы Undo возвращал прежнюю картинку.
    public static class UnitArtImporter
    {
        public const string PortraitFolder = "Assets/_Project/Art/units/Portraits";
        public const string BattlefieldFolder = "Assets/_Project/Art/units/Battlefield";
        private const string ManagedMarker = "__";

        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        public static bool IsImageFile(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            return Extensions.Any(known => string.Equals(known, extension, StringComparison.OrdinalIgnoreCase));
        }

        public static string FolderFor(UnitArtKind kind)
        {
            return kind == UnitArtKind.Portrait ? PortraitFolder : BattlefieldFolder;
        }

        public static string KindTitle(UnitArtKind kind)
        {
            return kind == UnitArtKind.Portrait ? "портрет" : "миниатюру на поле";
        }

        // Внешний файл → Sprite в проекте. Null и текст ошибки — файл не принят,
        // проект не изменён.
        public static Sprite ImportExternal(string sourcePath, string unitId, UnitArtKind kind, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                error = "Файл не найден: " + sourcePath;
                return null;
            }
            if (!IsImageFile(sourcePath))
            {
                error = "Нужен PNG или JPG: " + Path.GetFileName(sourcePath);
                return null;
            }

            byte[] bytes;
            Vector2Int size;
            Texture2D probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                bytes = File.ReadAllBytes(sourcePath);
                if (!probe.LoadImage(bytes, false))
                {
                    error = "Повреждённый или неподдерживаемый файл: " + Path.GetFileName(sourcePath);
                    return null;
                }
                size = new Vector2Int(probe.width, probe.height);
            }
            catch (Exception exception)
            {
                error = "Не удалось прочитать " + Path.GetFileName(sourcePath) + ": " + exception.Message;
                return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            string folder = FolderFor(kind);
            EnsureFolder(folder);
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string assetPath = folder + "/" + Sanitize(unitId) + ManagedMarker + (kind == UnitArtKind.Portrait ? "portrait" : "field") +
                               ManagedMarker + stamp + extension;
            File.WriteAllBytes(ToAbsolute(assetPath), bytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                error = "Unity не импортировала " + assetPath + ".";
                return null;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = extension == ".png";
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = 100f;
            importer.maxTextureSize = NextPowerOfTwo(Math.Max(size.x, size.y));
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)(kind == UnitArtKind.Portrait ? SpriteAlignment.Center : SpriteAlignment.BottomCenter);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().FirstOrDefault();
            if (sprite == null)
                error = "Unity не создала Sprite из " + assetPath + ".";
            return sprite;
        }

        // Sprite или Texture2D из окна Project — уже в проекте, без копии.
        public static Sprite FromProjectObject(UnityEngine.Object item)
        {
            if (item is Sprite sprite)
                return sprite;
            if (item is Texture2D texture)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                Sprite existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (existing != null)
                    return existing;
                // Текстура импортирована не как Sprite — переключить.
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                    return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                }
            }
            return null;
        }

        // Загруженные окном картинки, на которые больше не ссылается ни одно существо.
        public static List<string> FindUnused(UnitDatabaseAsset database)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (database != null)
            {
                foreach (UnitDefinitionData unit in database.Units)
                {
                    if (unit == null)
                        continue;
                    if (unit.Portrait != null)
                        used.Add(AssetDatabase.GetAssetPath(unit.Portrait));
                    if (unit.BattlefieldSprite != null)
                        used.Add(AssetDatabase.GetAssetPath(unit.BattlefieldSprite));
                }
            }

            List<string> unused = new List<string>();
            foreach (string folder in new[] { PortraitFolder, BattlefieldFolder })
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    continue;
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = Path.GetFileName(path);
                    // Только файлы, загруженные этим окном («существо__вид__время»).
                    bool managed = name.Contains(ManagedMarker + "portrait" + ManagedMarker) ||
                                   name.Contains(ManagedMarker + "field" + ManagedMarker);
                    if (managed && !used.Contains(path))
                        unused.Add(path);
                }
            }
            return unused;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unit";
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char symbol in value.Trim())
            {
                bool allowed = (symbol >= 'a' && symbol <= 'z') || (symbol >= 'A' && symbol <= 'Z') ||
                               (symbol >= '0' && symbol <= '9') || symbol == '_' || symbol == '-';
                builder.Append(allowed ? symbol : '_');
            }
            return builder.ToString();
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
                return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 32;
            while (result < value && result < 8192)
                result *= 2;
            return result;
        }

        public static string ToAbsolute(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot ?? string.Empty, assetPath).Replace('\\', '/');
        }
    }
}
