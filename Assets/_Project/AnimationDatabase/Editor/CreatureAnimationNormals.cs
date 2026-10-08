using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // ПР-12М/12Н: карты нормалей уже загруженных анимаций — без повторной
    // загрузки кадров. Нормаль кадра кладётся на страницу нормалей той же
    // раскладки, что страница атласа (по прямоугольнику спрайта), и страница
    // подключается к атласу второй текстурой `_NormalMap`. База анимаций не
    // меняется: нормали живут в настройках импорта страниц.
    public static class CreatureAnimationNormals
    {
        public const string SecondaryName = CreatureAnimationAtlasBuilder.NormalMapName;
        private static readonly Color32 FlatNormal = new Color32(128, 128, 255, 255);

        public static bool HasNormal(Sprite sprite) => sprite != null && FindPageNormal(sprite.texture) != null;

        public static Texture2D FindPageNormal(Texture texture)
        {
            if (texture == null || !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer)) return null;
            foreach (SecondarySpriteTexture item in importer.secondarySpriteTextures ?? Array.Empty<SecondarySpriteTexture>())
                if (item.name == SecondaryName) return item.texture;
            return null;
        }

        // Сколько кадров ячейки освещаются по нормалям.
        public static int CountWithNormals(CreatureAnimationFrames cell)
        {
            if (cell == null) return 0;
            Dictionary<Texture, bool> pages = new Dictionary<Texture, bool>();
            int count = 0;
            foreach (Sprite sprite in cell.Frames)
            {
                if (sprite == null) continue;
                if (!pages.TryGetValue(sprite.texture, out bool has)) pages[sprite.texture] = has = FindPageNormal(sprite.texture) != null;
                if (has) count++;
            }
            return count;
        }

        private static readonly System.Text.RegularExpressions.Regex NormalFolderSuffix =
            new System.Text.RegularExpressions.Regex(@"[_\- ](normals?|нормали|нормаль)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Папки нормалей названы как папки кадров с суффиксом: «Idle_normal/Front» →
        // «Idle/Front». Имя файла (последняя часть) не трогается.
        public static string StripNormalFolders(string path)
        {
            string[] parts = (path ?? string.Empty).Split('/');
            int folders = Path.HasExtension(path) ? parts.Length - 1 : parts.Length;
            for (int i = 0; i < folders; i++)
                parts[i] = NormalFolderSuffix.Replace(parts[i], string.Empty);
            return string.Join("/", parts);
        }

        // Имя файла нормали без «_n» / «_normal»: «walk_se_007_n» → «walk_se_007».
        public static string BaseName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            foreach (string suffix in new[] { "_normals", "_normal", "_n" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - suffix.Length);
            return name;
        }

        // Файлы нормалей → по кадру ячейки: по номерам кадров (как у загруженных
        // кадров), иначе по порядку имён при равном числе.
        public static bool Match(CreatureAnimationFrames cell, IReadOnlyList<string> normalFiles, out List<string> ordered, out string problem) =>
            Match(cell, normalFiles, out ordered, out problem, out _);

        // note — пояснение к удачному сопоставлению (например, «по порядку»).
        public static bool Match(CreatureAnimationFrames cell, IReadOnlyList<string> normalFiles, out List<string> ordered, out string problem, out string note)
        {
            ordered = null;
            problem = null;
            note = null;
            int frames = cell?.FrameCount ?? 0;
            if (frames == 0) { problem = "в ячейке нет кадров"; return false; }
            List<(string path, int number, bool numbered)> files = normalFiles
                .Select(path => (path, number: CreatureAnimationImportParser.TryParseFrameNumber(BaseName(path), out int number) ? number : 0,
                    numbered: CreatureAnimationImportParser.TryParseFrameNumber(BaseName(path), out _)))
                .ToList();
            if (cell.HasUsableSourceNumbers && files.Count > 0 && files.All(file => file.numbered))
            {
                Dictionary<int, string> byNumber = new Dictionary<int, string>();
                foreach ((string path, int number, bool _) in files)
                {
                    if (byNumber.ContainsKey(number)) { problem = "два файла нормали с номером " + number; return false; }
                    byNumber[number] = path;
                }
                List<int> missing = cell.SourceFrameNumbers.Where(number => !byNumber.ContainsKey(number)).ToList();
                // Номера другие (рендер нормалей с иного кадра), но файлов ровно
                // по кадру — по порядку номеров.
                if (missing.Count > 0 && files.Count == frames)
                {
                    ordered = files.OrderBy(file => file.number).Select(file => file.path).ToList();
                    note = "номера файлов не совпали с кадрами — по порядку";
                    return true;
                }
                if (missing.Count > 0)
                {
                    problem = "нет нормалей для кадров " + string.Join(", ", missing.Take(8)) + (missing.Count > 8 ? "…" : "") +
                              " (нормалей " + files.Count + ", кадров " + frames + ")";
                    return false;
                }
                ordered = cell.SourceFrameNumbers.Select(number => byNumber[number]).ToList();
                return true;
            }
            if (files.Count != frames)
            {
                problem = "нормалей " + files.Count + ", а кадров " + frames + " — нужно по одной на кадр";
                return false;
            }
            ordered = files.OrderBy(file => file.numbered ? 0 : 1).ThenBy(file => file.number)
                .ThenBy(file => file.path, StringComparer.OrdinalIgnoreCase).Select(file => file.path).ToList();
            return true;
        }

        // Подключить нормали к кадрам (порядок файлов = порядок кадров).
        public static bool Attach(IReadOnlyList<Sprite> frames, IReadOnlyList<string> normalFiles, out string message)
        {
            message = string.Empty;
            if (frames == null || normalFiles == null || frames.Count != normalFiles.Count || frames.Count == 0)
            {
                message = "Число нормалей не совпадает с числом кадров.";
                return false;
            }
            List<string> errors = new List<string>();
            int resized = 0;
            foreach (IGrouping<string, int> page in Enumerable.Range(0, frames.Count)
                         .Where(index => frames[index] != null)
                         .GroupBy(index => AssetDatabase.GetAssetPath(frames[index].texture)))
            {
                if (string.IsNullOrEmpty(page.Key) || !(AssetImporter.GetAtPath(page.Key) is TextureImporter importer))
                {
                    errors.Add("кадр без страницы атласа в проекте");
                    continue;
                }
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                string normalPath = Path.ChangeExtension(page.Key, null) + "_n.png";
                Texture2D normalPage = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                try
                {
                    // Уже есть страница нормалей — дополняется (другие кадры страницы сохраняются).
                    bool loaded = File.Exists(normalPath) && normalPage.LoadImage(File.ReadAllBytes(normalPath), false) &&
                                  normalPage.width == width && normalPage.height == height;
                    if (!loaded)
                    {
                        normalPage.Reinitialize(width, height);
                        Color32[] fill = new Color32[width * height];
                        for (int i = 0; i < fill.Length; i++) fill[i] = FlatNormal;
                        normalPage.SetPixels32(fill);
                    }
                    foreach (int index in page)
                    {
                        Rect rect = frames[index].rect;
                        RectInt cell = new RectInt(Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
                        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                        try
                        {
                            if (!File.Exists(normalFiles[index]) || !source.LoadImage(File.ReadAllBytes(normalFiles[index]), false))
                                throw new InvalidOperationException("не читается " + Path.GetFileName(normalFiles[index]));
                            Color32[] pixels = source.GetPixels32();
                            if (source.width != cell.width || source.height != cell.height)
                            {
                                // Те же пропорции, другое разрешение рендера — подгоняется под кадр.
                                float sourceAspect = source.width / (float)source.height, cellAspect = cell.width / (float)cell.height;
                                if (Mathf.Abs(sourceAspect - cellAspect) > cellAspect * .02f)
                                    throw new InvalidOperationException(Path.GetFileName(normalFiles[index]) + ": размер " + source.width + "×" + source.height +
                                                                        ", а кадр " + cell.width + "×" + cell.height + " — пропорции разные, нужен рендер той же камерой");
                                pixels = Resample(pixels, source.width, source.height, cell.width, cell.height);
                                resized++;
                            }
                            normalPage.SetPixels32(cell.x, cell.y, cell.width, cell.height, pixels);
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(source);
                        }
                    }
                    normalPage.Apply(false);
                    File.WriteAllBytes(CreatureAnimationAtlasBuilder.ToAbsolute(normalPath), normalPage.EncodeToPNG());
                }
                catch (Exception exception)
                {
                    errors.Add(exception.Message);
                    continue;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(normalPage);
                }
                Texture2D imported = ImportNormalPage(normalPath, Math.Max(width, height));
                if (imported == null || !SetPageNormal(importer, imported))
                    errors.Add("не удалось подключить " + Path.GetFileName(normalPath));
            }
            message = errors.Count == 0
                ? "Нормали подключены: " + frames.Count + " кадр(ов)." + (resized > 0 ? " Размер подогнан под кадр у " + resized + "." : string.Empty)
                : "Ошибки нормалей: " + string.Join("; ", errors.Distinct().Take(4));
            return errors.Count == 0;
        }

        // Билинейное изменение размера карты нормалей; направление
        // переводится обратно в единичное.
        public static Color32[] Resample(Color32[] source, int width, int height, int targetWidth, int targetHeight)
        {
            Color32[] result = new Color32[targetWidth * targetHeight];
            for (int y = 0; y < targetHeight; y++)
            {
                float sy = Mathf.Clamp((y + .5f) * height / targetHeight - .5f, 0, height - 1);
                int y0 = (int)sy, y1 = Mathf.Min(y0 + 1, height - 1);
                float ty = sy - y0;
                for (int x = 0; x < targetWidth; x++)
                {
                    float sx = Mathf.Clamp((x + .5f) * width / targetWidth - .5f, 0, width - 1);
                    int x0 = (int)sx, x1 = Mathf.Min(x0 + 1, width - 1);
                    float tx = sx - x0;
                    Color a = Color.Lerp(source[y0 * width + x0], source[y0 * width + x1], tx);
                    Color b = Color.Lerp(source[y1 * width + x0], source[y1 * width + x1], tx);
                    Color c = Color.Lerp(a, b, ty);
                    Vector3 n = new Vector3(c.r * 2 - 1, c.g * 2 - 1, c.b * 2 - 1);
                    if (n.sqrMagnitude > 1e-4f) n.Normalize();
                    result[y * targetWidth + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, c.a);
                }
            }
            return result;
        }

        // Похоже ли изображение на карту нормалей: почти все непрозрачные
        // пиксели — единичные векторы, смотрящие к зрителю. Защита от загрузки
        // нормалей вместо цветных кадров.
        public static bool LooksLikeNormalMap(string path)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || !texture.LoadImage(File.ReadAllBytes(path), false)) return false;
                Color32[] pixels = texture.GetPixels32();
                int step = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(pixels.Length / 4096f)));
                int opaque = 0, normal = 0;
                // Непрозрачный фон рендера (цвет угла) не считается.
                Color32 corner = pixels[0];
                bool Background(Color32 pixel) => corner.a >= 128 &&
                                                  Mathf.Abs(pixel.r - corner.r) <= 6 && Mathf.Abs(pixel.g - corner.g) <= 6 && Mathf.Abs(pixel.b - corner.b) <= 6;
                for (int y = 0; y < texture.height; y += step)
                {
                    for (int x = 0; x < texture.width; x += step)
                    {
                        Color32 pixel = pixels[y * texture.width + x];
                        if (pixel.a < 128 || Background(pixel)) continue;
                        opaque++;
                        Vector3 n = new Vector3(pixel.r / 127.5f - 1, pixel.g / 127.5f - 1, pixel.b / 127.5f - 1);
                        float length = n.magnitude;
                        if (n.z >= -.02f && length > .8f && length < 1.2f) normal++;
                    }
                }
                return opaque >= 16 && normal >= opaque * .9f;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        public static bool LookLikeNormalMaps(IEnumerable<string> files)
        {
            List<string> sample = (files ?? Enumerable.Empty<string>()).Where(CreatureAnimationImportParser.IsImageFile).Take(3).ToList();
            return sample.Count > 0 && sample.All(LooksLikeNormalMap);
        }

        // Снять нормали со страниц кадров ячейки (файлы страниц нормалей остаются).
        public static int Remove(IReadOnlyList<Sprite> frames)
        {
            int count = 0;
            foreach (string path in frames.Where(sprite => sprite != null).Select(sprite => AssetDatabase.GetAssetPath(sprite.texture)).Distinct())
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && FindPageNormal(AssetDatabase.LoadAssetAtPath<Texture2D>(path)) != null &&
                    SetPageNormal(importer, null))
                    count++;
            }
            return count;
        }

        private static Texture2D ImportNormalPage(string path, int size)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return null;
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                int max = 32;
                while (max < size && max < 16384) max *= 2;
                importer.maxTextureSize = max;
                SaveVerified(importer, meta => MetaValue(meta, "textureType") == ((int)TextureImporterType.NormalMap).ToString());
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool SetPageNormal(TextureImporter importer, Texture2D normal)
        {
            List<SecondarySpriteTexture> textures = (importer.secondarySpriteTextures ?? Array.Empty<SecondarySpriteTexture>())
                .Where(item => item.name != SecondaryName).ToList();
            if (normal != null) textures.Add(new SecondarySpriteTexture { name = SecondaryName, texture = normal });
            importer.secondarySpriteTextures = textures.ToArray();
            string guid = normal != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(normal)) : null;
            return SaveVerified(importer, meta => guid != null ? meta.Contains(guid) : !meta.Contains("name: " + SecondaryName));
        }

        // Запись настроек импорта с проверкой по .meta (на Windows свежий .meta
        // бывает кратко занят, и запись молча не проходит).
        private static bool SaveVerified(TextureImporter importer, Func<string, bool> metaOk)
        {
            string meta = importer.assetPath + ".meta";
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (attempt > 0)
                {
                    System.Threading.Thread.Sleep(120 * attempt);
                    EditorUtility.SetDirty(importer);
                }
                importer.SaveAndReimport();
                try
                {
                    if (File.Exists(meta) && metaOk(File.ReadAllText(meta))) return true;
                }
                catch (IOException)
                {
                }
            }
            Debug.LogWarning("База анимаций: настройки импорта не записаны в " + meta);
            return false;
        }

        private static string MetaValue(string meta, string key)
        {
            foreach (string line in (meta ?? string.Empty).Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(key + ":", StringComparison.Ordinal)) return trimmed.Substring(key.Length + 1).Trim();
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Папка нормалей всего набора
        // ------------------------------------------------------------------

        // Папка нормалей той же структуры, что и экспорт кадров (действие /
        // ракурс / номера). Все PNG в ней считаются нормалями; окончания
        // «_n» / «_normal» не обязательны. Ячейки без кадров пропускаются.
        public static string AttachFolder(CreatureAnimationSetData set, string root, IEnumerable<string> files, out int cells, out int failures)
        {
            cells = failures = 0;
            if (set == null) return "Не выбран набор анимаций.";
            Dictionary<string, string> real = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> images = (files ?? Enumerable.Empty<string>()).Select(CreatureAnimationImportParser.NormalizePath)
                .Where(CreatureAnimationImportParser.IsImageFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Кадры и нормали «_normal» вперемешку: берутся только нормали.
            if (images.Any(CreatureAnimationImportParser.IsNormalFile))
                images = images.Where(CreatureAnimationImportParser.IsNormalFile).ToList();
            foreach (string file in images)
            {
                string pseudo = StripNormalFolders(CreatureAnimationImportParser.NormalizePath(Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, BaseName(file) + Path.GetExtension(file))));
                if (!real.ContainsKey(pseudo)) real[pseudo] = file;
            }
            if (real.Count == 0) return "В папке нет PNG нормалей.";
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(StripNormalFolders(CreatureAnimationImportParser.NormalizePath(root ?? string.Empty)), real.Keys);
            List<string> lines = new List<string>();
            foreach (CreatureAnimationImportGroup group in package.Groups)
            {
                if (!group.ChosenAction.HasValue)
                {
                    lines.Add("«" + group.RawName + "»: действие не распознано — пропущено");
                    failures++;
                    continue;
                }
                CreatureAnimationAction action = group.ChosenAction.Value;
                foreach (CreatureAnimationImportCell source in group.Cells.Values)
                {
                    string where = CreatureAnimationLabels.ActionTitle(action) + " → " + CreatureAnimationLabels.DirectionTitle(source.Direction);
                    CreatureAnimationFrames target = set.FindFrames(action, source.Direction, group.ClipKey);
                    if (target == null || target.FrameCount == 0)
                    {
                        lines.Add(where + ": кадров нет — сначала загрузите кадры");
                        failures++;
                        continue;
                    }
                    List<string> normals = source.Frames.Select(frame => real.TryGetValue(frame.Path, out string path) ? path : frame.Path).ToList();
                    if (!Match(target, normals, out List<string> ordered, out string problem, out string note))
                    {
                        lines.Add(where + ": " + problem);
                        failures++;
                        continue;
                    }
                    if (Attach(target.Frames, ordered, out string message)) cells++;
                    else failures++;
                    lines.Add(where + ": " + message + (note != null ? " (" + note + ")" : string.Empty));
                }
            }
            return "Нормали: ячеек подключено " + cells + (failures > 0 ? ", с замечаниями " + failures : "") + ".\n" + string.Join("\n", lines.Take(30));
        }
    }
}
