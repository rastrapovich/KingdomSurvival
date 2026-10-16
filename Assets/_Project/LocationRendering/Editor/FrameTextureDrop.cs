using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12М: картинки «Обработки кадра» перетаскиванием — в поле или на
    // предпросмотр окна. Годится текстура или спрайт из окна Project и
    // файл с диска (PNG, JPG, TGA, PSD — из проводника): файл копируется в
    // проект (FrameTextures/<назначение>) и импортируется под назначение.
    // Для LUT покрашенный снимок сразу превращается в полоску LUT.
    public enum FrameTextureKind { Lut, Palette, Paper, Particle }

    public static class FrameTextureDrop
    {
        public const string Folder = "Assets/_Project/LocationRendering/FrameTextures";
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd" };

        public static string Title(FrameTextureKind kind)
        {
            switch (kind)
            {
                case FrameTextureKind.Lut: return "LUT";
                case FrameTextureKind.Palette: return "Палитра";
                case FrameTextureKind.Paper: return "Бумага";
                default: return "Частицы";
            }
        }

        public static bool IsImageFile(string path) =>
            !string.IsNullOrEmpty(path) && ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        // Что-то годное сейчас тащат (текстура, спрайт или файл картинки).
        public static bool HasCandidate() =>
            DragAndDrop.objectReferences.Any(item => item is Texture2D || item is Sprite) ||
            (DragAndDrop.paths ?? Array.Empty<string>()).Any(IsImageFile);

        // Картинка из текущего перетаскивания под назначение; null — нет (error — почему).
        public static Texture2D FromDrag(FrameTextureKind kind, out string error)
        {
            error = null;
            Object dropped = DragAndDrop.objectReferences.FirstOrDefault(item => item is Texture2D || item is Sprite);
            if (dropped != null) return FromObject(dropped, kind, out error);
            string path = (DragAndDrop.paths ?? Array.Empty<string>()).FirstOrDefault(IsImageFile);
            if (path != null) return FromFile(path, kind, out error);
            error = "Перетащите картинку: текстуру, спрайт или файл PNG / JPG.";
            return null;
        }

        public static Texture2D FromObject(Object dropped, FrameTextureKind kind, out string error)
        {
            error = null;
            Texture2D texture = dropped as Texture2D ?? (dropped as Sprite)?.texture;
            if (texture == null) { error = "Это не картинка."; return null; }
            if (kind != FrameTextureKind.Lut) return texture;
            // LUT: годная полоска — как есть; иначе — снимок или полоска с неверным импортом.
            if (LutTools.Problem(texture) == null) return texture;
            string asset = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(asset)) { error = "У текстуры нет файла в проекте."; return null; }
            return LutTools.FromImageFile(Path.GetFullPath(asset), out error);
        }

        // Файл: внутри проекта — ассет; снаружи — копия в проект.
        public static Texture2D FromFile(string path, FrameTextureKind kind, out string error)
        {
            error = null;
            if (kind == FrameTextureKind.Lut) return LutTools.FromImageFile(path, out error);
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string project = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (full.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase))
            {
                Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets" + full.Substring(project.Length));
                if (existing != null) return existing;
            }
            if (!File.Exists(full)) { error = "Файл не найден: " + full; return null; }
            string folder = Folder + "/" + kind;
            Directory.CreateDirectory(folder);
            string asset = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(full));
            File.Copy(full, asset);
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(asset) is TextureImporter importer)
            {
                Configure(importer, kind);
                importer.SaveAndReimport();
            }
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            if (texture == null) error = "Unity не смогла загрузить картинку: " + asset;
            return texture;
        }

        // Импорт под назначение.
        public static void Configure(TextureImporter importer, FrameTextureKind kind)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            switch (kind)
            {
                case FrameTextureKind.Palette:
                    // Каждый пиксель — цвет: без сглаживания, сжатия и мип-карт.
                    importer.mipmapEnabled = false;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    break;
                case FrameTextureKind.Paper:
                    // Фактура повторяется по кадру.
                    importer.mipmapEnabled = true;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    break;
                default:
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = true;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    break;
            }
        }

        // Поле принимает перетаскивание: подсветка при наведении, по отпусканию — set.
        public static void MakeDropTarget(VisualElement target, FrameTextureKind kind, Action<Texture2D> set, Action<string> report)
        {
            Color highlight = new Color(.95f, .82f, .5f, .18f);
            StyleColor normal = target.style.backgroundColor;
            target.RegisterCallback<DragEnterEvent>(evt => { if (HasCandidate()) target.style.backgroundColor = highlight; }, TrickleDown.TrickleDown);
            target.RegisterCallback<DragLeaveEvent>(evt => target.style.backgroundColor = normal, TrickleDown.TrickleDown);
            target.RegisterCallback<DragExitedEvent>(evt => target.style.backgroundColor = normal, TrickleDown.TrickleDown);
            target.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!HasCandidate()) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            target.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (!HasCandidate()) return;
                DragAndDrop.AcceptDrag();
                evt.StopImmediatePropagation();
                target.style.backgroundColor = normal;
                Texture2D texture = FromDrag(kind, out string error);
                if (texture != null) set(texture);
                else report?.Invoke(error);
            }, TrickleDown.TrickleDown);
        }
    }
}
