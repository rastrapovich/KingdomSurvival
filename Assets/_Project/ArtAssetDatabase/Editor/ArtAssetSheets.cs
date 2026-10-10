using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.AnimationDatabase.Editor;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Листы кадров анимированных ассетов — как атласы Базы анимаций: кадры
    // ракурса части складываются на страницы (до 4096×4096, прозрачная рамка
    // между кадрами), нормали — страницами той же раскладки, второй текстурой
    // _NormalMap. Отдельные кадры остаются источником правки (карточка,
    // импорт, нормали папкой); лист собирается из них заново, когда они
    // меняются (отпечаток SheetKey). Места показывают анимацию из листа.
    public static class ArtAssetSheets
    {
        public const string FolderName = "Sheets";

        // Собрать недостающие и устаревшие листы ассета (force — все заново).
        // Возвращает число собранных листов; причины, почему лист не собран, — в warnings.
        public static int Refresh(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, List<string> warnings = null, bool force = false)
        {
            if (asset?.Parts == null) return 0;
            int built = 0;
            bool changed = false;
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string folder = ArtAssetImporter.AssetFolder(asset) + "/" + FolderName;
            foreach (ArtAssetPart part in asset.Parts.Where(item => item != null))
            {
                foreach (ArtAssetView view in ArtAssetLabels.Views)
                {
                    ArtAssetPartView slot = part.FindView(view);
                    if (slot == null) continue;
                    string baseName = BaseName(asset, part, view);
                    string where = asset.Name + " · " + ArtAssetLabels.ViewTitle(view) + (part == asset.MainPart ? "" : " · " + part.Name);
                    if (!slot.IsAnimated || !TrySources(slot, where, warnings, out List<string> colors, out List<string> normals, out Vector2Int size))
                    {
                        if (slot.SheetFrames != null && slot.SheetFrames.Count > 0) { Clear(slot); changed = true; }
                        continue;
                    }
                    string key = Key(colors, normals);
                    if (!force && slot.HasSheet && slot.SheetKey == key)
                    {
                        foreach (Sprite sprite in slot.SheetFrames) keep.Add(AssetDatabase.GetAssetPath(sprite));
                        foreach (Texture2D normal in slot.SheetNormals ?? new List<Texture2D>()) if (normal != null) keep.Add(AssetDatabase.GetAssetPath(normal));
                        continue;
                    }
                    List<Sprite> sprites;
                    try
                    {
                        sprites = CreatureAnimationAtlasBuilder.BuildAtlas(folder, baseName, baseName, colors, size, normals);
                    }
                    catch (Exception exception)
                    {
                        // Лист не собрался — анимация идёт отдельными (свежими) кадрами.
                        warnings?.Add(where + ": лист кадров не собран — " + exception.Message);
                        Clear(slot);
                        changed = true;
                        Debug.LogWarning("База ассетов: " + where + ": лист кадров не собран — " + exception);
                        continue;
                    }
                    Undo.RecordObject(catalog, "Лист кадров");
                    slot.SheetFrames = sprites;
                    slot.SheetNormals = sprites.Select(SpriteNormalMaps.Find).ToList();
                    slot.SheetKey = key;
                    foreach (Sprite sprite in sprites) keep.Add(AssetDatabase.GetAssetPath(sprite));
                    foreach (Texture2D normal in slot.SheetNormals) if (normal != null) keep.Add(AssetDatabase.GetAssetPath(normal));
                    built++;
                    changed = true;
                }
            }
            // Страницы, которые больше не нужны (кадров стало меньше, кадры сняты).
            if (AssetDatabase.IsValidFolder(folder))
                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!AssetDatabase.IsValidFolder(path) && !keep.Contains(path)) AssetDatabase.DeleteAsset(path);
                }
            if (changed && catalog != null)
            {
                ArtAssetImporter.Changed(catalog);
                if (AssetDatabase.Contains(catalog)) AssetDatabase.SaveAssetIfDirty(catalog);
            }
            return built;
        }

        public static void Clear(ArtAssetPartView slot)
        {
            slot.SheetFrames = new List<Sprite>();
            slot.SheetNormals = new List<Texture2D>();
            slot.SheetKey = string.Empty;
        }

        // Лист устарел или не собран (для проверки и окна).
        public static bool IsStale(ArtAssetPartView slot)
        {
            if (slot == null || !slot.IsAnimated) return false;
            if (!slot.HasSheet) return true;
            return TrySources(slot, null, null, out List<string> colors, out List<string> normals, out _) && Key(colors, normals) != slot.SheetKey;
        }

        // Кадры, из которых собирается лист: отдельные файлы одного размера;
        // нормали — у всех кадров или ни у одного.
        private static bool TrySources(ArtAssetPartView slot, string where, List<string> warnings, out List<string> colors, out List<string> normals, out Vector2Int size)
        {
            colors = new List<string>();
            normals = null;
            size = Vector2Int.zero;
            Sprite[] frames = slot.FrameSprites();
            List<string> normalPaths = new List<string>();
            foreach (Sprite frame in frames)
            {
                if (SpriteNormalMaps.IsSheet(frame))
                {
                    warnings?.Add(where + ": кадры уже из листа Unity — лист не собирается.");
                    return false;
                }
                Vector2Int frameSize = SpriteNormalMaps.SourceSize(frame.texture);
                if (frameSize != new Vector2Int(Mathf.RoundToInt(frame.rect.width), Mathf.RoundToInt(frame.rect.height)))
                {
                    warnings?.Add(where + ": рисунок кадра — часть текстуры, лист не собирается.");
                    return false;
                }
                if (size == Vector2Int.zero) size = frameSize;
                else if (size != frameSize)
                {
                    warnings?.Add(where + ": кадры разного размера — лист не собирается, анимация идёт отдельными рисунками.");
                    return false;
                }
                colors.Add(AssetDatabase.GetAssetPath(frame.texture));
                Texture2D normal = slot.NormalOf(frame);
                normalPaths.Add(normal != null ? AssetDatabase.GetAssetPath(normal) : null);
            }
            int withNormals = normalPaths.Count(path => !string.IsNullOrEmpty(path));
            if (withNormals == normalPaths.Count) normals = normalPaths;
            else if (withNormals > 0) warnings?.Add(where + ": нормали есть не у всех кадров (" + withNormals + " из " + normalPaths.Count + ") — лист без нормалей.");
            return colors.Count > 1;
        }

        // Отпечаток источников: файл, его размер и время изменения.
        private static string Key(List<string> colors, List<string> normals)
        {
            string Part(string path)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "-";
                FileInfo info = new FileInfo(path);
                return AssetDatabase.AssetPathToGUID(path) + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }
            return string.Join("|", colors.Select(Part)) + "#" + (normals != null ? string.Join("|", normals.Select(Part)) : "без нормалей");
        }

        private static string BaseName(ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view) =>
            ArtAssetLabels.ViewFolder(view) + (part == asset.MainPart ? "" : "_part-" + part.Id.Substring(0, Math.Min(8, part.Id.Length))) + "_sheet";
    }
}
