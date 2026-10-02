using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    // Перетаскивание картинок: спрайты и текстуры проекта, файлы из
    // проводника. Внешний файл копируется в папку проекта, текстура
    // переводится в спрайт.
    public sealed partial class BattlefieldDatabaseWindow
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".psd", ".tga", ".tif", ".tiff", ".bmp", ".webp" };
        private static readonly Color DropHighlight = new Color(0.86f, 0.70f, 0.38f, 1f);

        private void RegisterImageDrop(VisualElement target, string importFolder, bool multiple, Action<List<Sprite>> onDrop)
        {
            void Highlight(bool on)
            {
                Color color = on ? DropHighlight : Color.clear;
                float width = on ? 2f : 0f;
                target.style.borderTopWidth = width;
                target.style.borderBottomWidth = width;
                target.style.borderRightWidth = width;
                target.style.borderLeftWidth = width;
                target.style.borderTopColor = color;
                target.style.borderBottomColor = color;
                target.style.borderRightColor = color;
                target.style.borderLeftColor = color;
            }

            target.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!HasDraggedImage())
                    return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                Highlight(true);
                evt.StopPropagation();
            });
            target.RegisterCallback<DragLeaveEvent>(_ => Highlight(false));
            target.RegisterCallback<DragExitedEvent>(_ => Highlight(false));
            target.RegisterCallback<DragPerformEvent>(evt =>
            {
                Highlight(false);
                if (!HasDraggedImage())
                    return;
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();
                List<Sprite> sprites = ImportDraggedSprites(importFolder, multiple);
                if (sprites.Count > 0)
                    onDrop(sprites);
                else
                    ShowNotification(new GUIContent("Не удалось получить картинку"));
            });
        }

        private static bool HasDraggedImage()
        {
            foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            {
                if (dragged is Sprite || dragged is Texture2D)
                    return true;
            }
            foreach (string path in DragAndDrop.paths)
            {
                if (IsImagePath(path))
                    return true;
            }
            return false;
        }

        private static bool IsImagePath(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return Array.IndexOf(ImageExtensions, extension) >= 0;
        }

        private static List<Sprite> ImportDraggedSprites(string importFolder, bool multiple)
        {
            List<Sprite> sprites = new List<Sprite>();
            HashSet<string> seen = new HashSet<string>();

            foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            {
                Sprite sprite = dragged as Sprite;
                if (sprite == null && dragged is Texture2D)
                    sprite = EnsureSprite(AssetDatabase.GetAssetPath(dragged));
                if (sprite != null && seen.Add(AssetDatabase.GetAssetPath(sprite)))
                    sprites.Add(sprite);
                if (sprites.Count > 0 && !multiple)
                    return sprites;
            }

            // Файлы из проводника: objectReferences пуст, есть только пути.
            foreach (string path in DragAndDrop.paths)
            {
                if (!IsImagePath(path))
                    continue;
                string assetPath = ToProjectAsset(path, importFolder);
                if (assetPath == null || !seen.Add(assetPath))
                    continue;
                Sprite sprite = EnsureSprite(assetPath);
                if (sprite != null)
                    sprites.Add(sprite);
                if (sprites.Count > 0 && !multiple)
                    break;
            }
            return sprites;
        }

        // Путь внутри проекта; внешний файл копируется в importFolder.
        private static string ToProjectAsset(string path, string importFolder)
        {
            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal))
                return normalized;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
            string full = Path.GetFullPath(path).Replace('\\', '/');
            if (full.StartsWith(projectRoot + "Assets/", StringComparison.OrdinalIgnoreCase))
                return full.Substring(projectRoot.Length);
            if (!File.Exists(full))
                return null;

            Directory.CreateDirectory(Path.Combine(projectRoot, importFolder));
            string target = AssetDatabase.GenerateUniqueAssetPath(importFolder + "/" + Path.GetFileName(full));
            File.Copy(full, Path.Combine(projectRoot, target));
            AssetDatabase.ImportAsset(target);
            return target;
        }

        // Текстура как спрайт без мип-карт; фоны — до 4096 пикселей.
        private static Sprite EnsureSprite(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite ||
                                     importer.spriteImportMode == SpriteImportMode.None))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, 4096);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}
