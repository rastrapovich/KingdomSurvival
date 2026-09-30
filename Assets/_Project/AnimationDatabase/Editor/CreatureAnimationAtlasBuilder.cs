using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Страница атласа: равная сетка кадров общего холста. Опора и UV кадра
    // не меняются: каждый кадр занимает ячейку размером ровно с холст.
    public sealed class CreatureAnimationAtlasPage
    {
        public int Width { get; }
        public int Height { get; }
        public List<RectInt> Cells { get; } = new List<RectInt>();

        public CreatureAnimationAtlasPage(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    public static class CreatureAnimationAtlasBuilder
    {
        public const int MaxAtlasSize = 4096;
        // Прозрачная рамка вокруг кадра: соседние кадры не просвечивают при
        // билинейной фильтрации.
        public const int Padding = 2;

        // Раскладка кадров по страницам. Прямоугольники — в координатах
        // текстуры Unity (начало внизу слева).
        public static List<CreatureAnimationAtlasPage> ComputeLayout(int frameCount, Vector2Int canvas, int maxSize = MaxAtlasSize, int padding = Padding)
        {
            List<CreatureAnimationAtlasPage> pages = new List<CreatureAnimationAtlasPage>();
            if (frameCount <= 0 || canvas.x <= 0 || canvas.y <= 0)
                return pages;

            int strideX = canvas.x + padding * 2;
            int strideY = canvas.y + padding * 2;
            if (strideX > maxSize || strideY > maxSize)
                throw new ArgumentException("Кадр " + canvas.x + "×" + canvas.y + " не помещается в атлас " + maxSize + "×" + maxSize + ".");

            int maxColumns = Math.Max(1, maxSize / strideX);
            int maxRows = Math.Max(1, maxSize / strideY);
            int perPage = maxColumns * maxRows;
            int remaining = frameCount;
            while (remaining > 0)
            {
                int count = Math.Min(perPage, remaining);
                int columns = Math.Min(maxColumns, count);
                int rows = (count + columns - 1) / columns;
                int width = RoundUpToFour(columns * strideX);
                int height = RoundUpToFour(rows * strideY);
                CreatureAnimationAtlasPage page = new CreatureAnimationAtlasPage(width, height);
                for (int i = 0; i < count; i++)
                {
                    int column = i % columns;
                    int row = i / columns;
                    int x = column * strideX + padding;
                    int yFromTop = row * strideY + padding;
                    page.Cells.Add(new RectInt(x, height - yFromTop - canvas.y, canvas.x, canvas.y));
                }
                pages.Add(page);
                remaining -= count;
            }
            return pages;
        }

        private static int RoundUpToFour(int value)
        {
            return (value + 3) / 4 * 4;
        }

        // Проверка файла без изменения проекта: читается ли и какого размера.
        public static bool TryReadImageSize(string path, out Vector2Int size, out string error)
        {
            size = Vector2Int.zero;
            error = null;
            Texture2D texture = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes, false))
                {
                    error = "повреждённый или не PNG файл";
                    return false;
                }
                size = new Vector2Int(texture.width, texture.height);
                return true;
            }
            catch (Exception exception)
            {
                error = "не удалось прочитать файл (" + exception.Message + ")";
                return false;
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // Пишет страницы атласа в проект и нарезает их на кадры.
        // Возвращает спрайты в порядке кадров.
        public static List<Sprite> BuildAtlas(
            string folder,
            string baseName,
            string spritePrefix,
            IReadOnlyList<string> framePaths,
            Vector2Int canvas)
        {
            CreatureAnimationEditorData.EnsureFolder(folder);
            List<CreatureAnimationAtlasPage> pages = ComputeLayout(framePaths.Count, canvas);
            List<string> pagePaths = new List<string>();
            List<List<string>> pageSpriteNames = new List<List<string>>();

            int frameIndex = 0;
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                CreatureAnimationAtlasPage page = pages[pageIndex];
                string pagePath = folder + "/" + baseName + (pages.Count > 1 ? "_p" + (pageIndex + 1) : string.Empty) + ".png";
                Texture2D atlas = new Texture2D(page.Width, page.Height, TextureFormat.RGBA32, false);
                List<string> names = new List<string>();
                try
                {
                    atlas.SetPixels32(new Color32[page.Width * page.Height]);
                    foreach (RectInt cell in page.Cells)
                    {
                        Texture2D frame = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        try
                        {
                            if (!frame.LoadImage(File.ReadAllBytes(framePaths[frameIndex]), false))
                                throw new InvalidOperationException("Повреждённый файл: " + framePaths[frameIndex]);
                            if (frame.width != canvas.x || frame.height != canvas.y)
                                throw new InvalidOperationException("Размер кадра изменился после проверки: " + framePaths[frameIndex]);
                            atlas.SetPixels32(cell.x, cell.y, cell.width, cell.height, frame.GetPixels32());
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(frame);
                        }
                        names.Add(spritePrefix + "_" + frameIndex.ToString("000"));
                        frameIndex++;
                    }
                    atlas.Apply(false);
                    File.WriteAllBytes(ToAbsolute(pagePath), atlas.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(atlas);
                }
                pagePaths.Add(pagePath);
                pageSpriteNames.Add(names);
            }

            List<Sprite> sprites = new List<Sprite>();
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                ImportAsSlicedSprite(pagePaths[pageIndex], pages[pageIndex], pageSpriteNames[pageIndex]);
                Dictionary<string, Sprite> byName = AssetDatabase.LoadAllAssetsAtPath(pagePaths[pageIndex])
                    .OfType<Sprite>()
                    .ToDictionary(sprite => sprite.name, StringComparer.Ordinal);
                foreach (string name in pageSpriteNames[pageIndex])
                {
                    if (!byName.TryGetValue(name, out Sprite sprite))
                        throw new InvalidOperationException("Unity не создала кадр " + name + " в " + pagePaths[pageIndex] + ".");
                    sprites.Add(sprite);
                }
            }
            return sprites;
        }

        // Импорт: Sprite (2D and UI), альфа, без мип-карт, билинейная
        // фильтрация, сжатие высокого качества без уменьшения разрешения.
        private static void ImportAsSlicedSprite(string assetPath, CreatureAnimationAtlasPage page, IReadOnlyList<string> spriteNames)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                throw new InvalidOperationException("Не удалось импортировать атлас " + assetPath + ".");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = 100f;
            importer.maxTextureSize = NextPowerOfTwo(Math.Max(page.Width, page.Height));
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            List<SpriteRect> rects = new List<SpriteRect>();
            for (int i = 0; i < page.Cells.Count; i++)
            {
                RectInt cell = page.Cells[i];
                rects.Add(new SpriteRect
                {
                    name = spriteNames[i],
                    rect = new Rect(cell.x, cell.y, cell.width, cell.height),
                    alignment = SpriteAlignment.BottomCenter,
                    pivot = new Vector2(0.5f, 0f),
                    spriteID = GUID.Generate()
                });
            }
            provider.SetSpriteRects(rects.ToArray());
            ISpriteNameFileIdDataProvider names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            names?.SetNameFileIdPairs(rects.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID)).ToList());
            provider.Apply();
            importer.SaveAndReimport();
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 32;
            while (result < value && result < 16384)
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
