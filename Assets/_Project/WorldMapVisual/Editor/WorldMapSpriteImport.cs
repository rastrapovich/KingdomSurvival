using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // Рисунки для пулов генератора карты из чего угодно: спрайт, картинка
    // (любой тип импорта), папка в Project, файлы и папки с диска. Картинка,
    // импортированная не как Sprite, после подтверждения переводится в
    // Sprite (знаки — точка опоры снизу по центру, иконки — с mip-картами).
    // Файлы с диска копируются в Assets/_Project/Art/WorldMap/<папка>.
    public static class WorldMapSpriteImport
    {
        public const string DiskFolder = "Assets/_Project/Art/WorldMap";
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".gif" };

        // Вопрос «перевести в Sprite?»; в тестах подменяется.
        public static Func<string, bool> ConfirmConversion = question =>
            EditorUtility.DisplayDialog("Рисунки для карты", question, "Перевести", "Пропустить эти");

        public static bool IsImageFile(string path) =>
            !string.IsNullOrEmpty(path) && ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        // Есть ли в перетаскивании что-нибудь пригодное.
        public static bool HasCandidate(IEnumerable<Object> objects, IEnumerable<string> paths)
        {
            foreach (Object item in objects ?? Array.Empty<Object>())
            {
                if (item is Sprite || item is Texture2D || IsProjectFolder(item))
                    return true;
            }
            foreach (string path in paths ?? Array.Empty<string>())
            {
                if (IsImageFile(path) || Directory.Exists(path))
                    return true;
            }
            return false;
        }

        // Спрайты из перетаскивания или выделения. subfolder — куда класть
        // файлы с диска. icons — рисунки для иконок меток (mip-карты).
        public static List<Sprite> Collect(IEnumerable<Object> objects, IEnumerable<string> paths, string subfolder, bool icons)
        {
            List<Sprite> direct = new List<Sprite>();
            List<string> textures = new List<string>();
            List<string> diskFiles = new List<string>();

            foreach (Object item in objects ?? Array.Empty<Object>())
            {
                if (item is Sprite sprite)
                    direct.Add(sprite);
                else if (item is Texture2D)
                    AddUnique(textures, AssetDatabase.GetAssetPath(item));
                else if (IsProjectFolder(item))
                    textures.AddRange(TexturesInProjectFolder(AssetDatabase.GetAssetPath(item)).Where(path => !textures.Contains(path)));
            }

            // Пути из проводника (объектов Unity у них нет).
            if (direct.Count == 0 && textures.Count == 0)
            {
                foreach (string path in paths ?? Array.Empty<string>())
                {
                    string projectPath = ToProjectPath(path);
                    if (projectPath != null && AssetDatabase.IsValidFolder(projectPath))
                        textures.AddRange(TexturesInProjectFolder(projectPath).Where(item => !textures.Contains(item)));
                    else if (projectPath != null && IsImageFile(projectPath))
                        AddUnique(textures, projectPath);
                    else if (Directory.Exists(path))
                        diskFiles.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories).Where(IsImageFile).OrderBy(file => file));
                    else if (IsImageFile(path) && File.Exists(path))
                        diskFiles.Add(path);
                }
            }

            if (diskFiles.Count > 0)
                textures.AddRange(CopyIntoProject(diskFiles, subfolder));

            textures.Sort(StringComparer.OrdinalIgnoreCase);
            List<Sprite> result = new List<Sprite>(direct);
            result.AddRange(SpritesOf(textures, icons));
            return result;
        }

        // Одна текстура (подложка, узор, бумага): из Project — как есть, из
        // спрайта — его текстура, файл с диска копируется в проект. Узорам
        // включается повтор (Wrap Mode — Repeat), чтобы плитка была бесшовной.
        public static Texture2D CollectTexture(IEnumerable<Object> objects, IEnumerable<string> paths, string subfolder)
        {
            Texture2D texture = null;
            foreach (Object item in objects ?? Array.Empty<Object>())
            {
                texture = item as Texture2D ?? (item as Sprite)?.texture;
                if (texture != null)
                    break;
            }
            if (texture == null)
            {
                string file = (paths ?? Array.Empty<string>()).FirstOrDefault(IsImageFile);
                if (file != null)
                {
                    string projectPath = ToProjectPath(file) ?? CopyIntoProject(new List<string> { file }, subfolder).FirstOrDefault();
                    if (projectPath != null)
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(projectPath);
                }
            }
            if (texture != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer &&
                importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            return texture;
        }

        public static List<Sprite> CollectFromFolderPanel(string subfolder, bool icons)
        {
            string folder = EditorUtility.OpenFolderPanel("Папка с рисунками", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder))
                return new List<Sprite>();
            return Collect(null, new[] { folder }, subfolder, icons);
        }

        private static bool IsProjectFolder(Object item) =>
            item != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(item));

        private static IEnumerable<string> TexturesInProjectFolder(string folder) =>
            AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        // Абсолютный путь внутри проекта → "Assets/…"; вне проекта — null.
        private static string ToProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/") || normalized == "Assets")
                return normalized;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            string full = Path.GetFullPath(path).Replace('\\', '/');
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : null;
        }

        private static List<string> CopyIntoProject(List<string> files, string subfolder)
        {
            string folder = DiskFolder + "/" + Sanitize(subfolder);
            Directory.CreateDirectory(folder);
            List<string> imported = new List<string>();
            foreach (string file in files)
            {
                string target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(file));
                File.Copy(file, target);
                imported.Add(target);
            }
            AssetDatabase.Refresh();
            return imported;
        }

        private static string Sanitize(string name)
        {
            string value = string.IsNullOrWhiteSpace(name) ? "Рисунки" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value.Replace('/', '_');
        }

        // Спрайты картинок; картинки не-Sprite переводятся после вопроса.
        private static List<Sprite> SpritesOf(List<string> texturePaths, bool icons)
        {
            List<string> needConversion = texturePaths
                .Where(path => !AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Any() && AssetImporter.GetAtPath(path) is TextureImporter)
                .ToList();

            if (needConversion.Count > 0)
            {
                string question =
                    "Картинок, импортированных не как спрайт (Texture Type не Sprite): " + needConversion.Count + ".\n" +
                    "Перевести их в Sprite (2D and UI)" + (icons ? " с mip-картами" : ", точка опоры — снизу по центру") + "?\n\n" +
                    string.Join("\n", needConversion.Take(6).Select(Path.GetFileName)) + (needConversion.Count > 6 ? "\n…" : "");
                bool convert = ConfirmConversion(question);
                if (convert)
                {
                    foreach (string path in needConversion)
                        ConvertToSprite(path, icons);
                }
            }

            List<Sprite> sprites = new List<Sprite>();
            foreach (string path in texturePaths)
            {
                IEnumerable<Sprite> found = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(sprite => sprite.name, StringComparer.OrdinalIgnoreCase);
                sprites.AddRange(found);
            }
            return sprites;
        }

        public static void ConvertToSprite(string path, bool icons)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return;
            importer.textureType = TextureImporterType.Sprite;
            if (importer.spriteImportMode == SpriteImportMode.None)
                importer.spriteImportMode = SpriteImportMode.Single;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)(icons ? SpriteAlignment.Center : SpriteAlignment.BottomCenter);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = icons;
            importer.SaveAndReimport();
        }

        private static void AddUnique(List<string> list, string path)
        {
            if (!string.IsNullOrEmpty(path) && !list.Contains(path))
                list.Add(path);
        }
    }
}
