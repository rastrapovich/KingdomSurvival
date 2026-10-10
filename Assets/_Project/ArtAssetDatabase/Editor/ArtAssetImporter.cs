using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    public sealed class ArtAssetImportResult
    {
        public readonly List<string> Created = new List<string>();
        public readonly List<string> Updated = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Cancelled;

        public string Summary
        {
            get
            {
                List<string> parts = new List<string>();
                if (Created.Count > 0) parts.Add("создано: " + Created.Count);
                if (Updated.Count > 0) parts.Add("обновлено: " + Updated.Count);
                if (Errors.Count > 0) parts.Add("ошибок: " + Errors.Count);
                if (Cancelled) parts.Add("остановлено пользователем");
                return parts.Count == 0 ? "Ничего не импортировано." : "Импорт: " + string.Join(", ", parts) + ".";
            }
        }
    }

    // ПР-12Н: загрузка рисунков и нормалей в каталог. Внешние файлы копируются
    // в управляемую папку проекта (Assets/_Project/Art/Assets/<ID>/), исходники
    // пользователя не меняются. Обновление того же ракурса перезаписывает файл
    // по тому же пути — .meta и GUID сохраняются, ссылки мест не рвутся.
    // Один объект импортируется согласованно: при ошибке его новые файлы
    // удаляются, перезаписанные восстанавливаются, запись каталога не меняется.
    public static class ArtAssetImporter
    {
        public const string ManagedRoot = "Assets/_Project/Art/Assets";

        public static ArtAssetDatabaseAsset LoadOrCreateCatalog()
        {
            ArtAssetDatabaseAsset catalog = AssetDatabase.LoadAssetAtPath<ArtAssetDatabaseAsset>(ArtAssetDatabaseAsset.AssetPath);
            if (catalog != null) return catalog;
            Directory.CreateDirectory(Path.GetDirectoryName(ArtAssetDatabaseAsset.AssetPath));
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            AssetDatabase.CreateAsset(catalog, ArtAssetDatabaseAsset.AssetPath);
            AssetDatabase.SaveAssets();
            ArtAssetDatabaseAsset.ResetCurrent();
            return catalog;
        }

        // targetFor: существующая запись для обновления или null — создать новую.
        public static ArtAssetImportResult Import(ArtAssetDatabaseAsset catalog, ArtAssetImportPlan plan,
            Func<ArtAssetImportGroup, ArtAssetDefinition> targetFor, bool progress = true)
        {
            ArtAssetImportResult result = new ArtAssetImportResult();
            if (catalog == null || plan == null) return result;
            try
            {
                for (int i = 0; i < plan.Groups.Count; i++)
                {
                    ArtAssetImportGroup group = plan.Groups[i];
                    if (progress && EditorUtility.DisplayCancelableProgressBar("База ассетов · импорт",
                            (i + 1) + " из " + plan.Groups.Count + ": " + group.Name, i / (float)Math.Max(1, plan.Groups.Count)))
                    {
                        result.Cancelled = true;
                        break;
                    }
                    ArtAssetDefinition existing = targetFor?.Invoke(group);
                    try
                    {
                        ArtAssetDefinition imported = ImportGroup(catalog, group, existing, result.Warnings);
                        (existing != null ? result.Updated : result.Created).Add(imported.Id);
                    }
                    catch (Exception exception)
                    {
                        result.Errors.Add(group.Name + ": " + exception.Message);
                        Debug.LogWarning("База ассетов: «" + group.Name + "» не импортирован: " + exception);
                    }
                }
            }
            finally
            {
                if (progress) EditorUtility.ClearProgressBar();
            }
            if (result.Created.Count + result.Updated.Count > 0)
            {
                catalog.MarkChanged();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
            return result;
        }

        private sealed class FileTransaction
        {
            public readonly List<string> Created = new List<string>();
            public readonly List<(string path, string backup)> Overwritten = new List<(string, string)>();

            public void Rollback()
            {
                foreach (string path in Created)
                    AssetDatabase.DeleteAsset(path);
                foreach ((string path, string backup) in Overwritten)
                {
                    if (!File.Exists(backup)) continue;
                    File.Copy(backup, path, true);
                    File.Delete(backup);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }

            public void Commit()
            {
                foreach ((string _, string backup) in Overwritten)
                    if (File.Exists(backup)) File.Delete(backup);
            }
        }

        private static ArtAssetDefinition ImportGroup(ArtAssetDatabaseAsset catalog, ArtAssetImportGroup group, ArtAssetDefinition existing, List<string> warnings)
        {
            ArtAssetDefinition working = existing != null ? Clone(existing) : new ArtAssetDefinition { Name = group.Name };
            working.ImportKey = group.Key;
            // Источник вне проекта запоминается: «Обновить из папки».
            if (!string.IsNullOrEmpty(group.SourceFolder) && !IsProjectPath(group.SourceFolder)) working.SourceFolder = group.SourceFolder;
            FileTransaction transaction = new FileTransaction();
            try
            {
                foreach (ArtAssetImportSlot slot in group.Slots)
                {
                    ArtAssetPart part = FindOrAddPart(working, slot.Part);
                    ArtAssetPartView view = part.View(slot.View);
                    Sprite sprite = slot.ColorPath != null ? ImportColor(working, part, slot.View, slot.ColorPath, view.Sprite, transaction) : view.Sprite;
                    Texture2D normal = slot.NormalPath != null ? ImportNormal(working, part, slot.View, slot.NormalPath, view.NormalMap, transaction) : view.NormalMap;
                    view.Sprite = sprite;
                    view.NormalMap = normal;
                    if (sprite != null && normal != null)
                        AssignNormal(sprite, normal, warnings, working.Name + " · " + ArtAssetLabels.ViewTitle(slot.View));
                    if (slot.Frames.Count > 0) ImportFrames(working, part, slot, view, transaction, warnings);
                }
                if (existing == null)
                    InitializeScale(working);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            Undo.RecordObject(catalog, existing != null ? "Обновить ассет" : "Создать ассет");
            int index = existing != null ? catalog.assets.IndexOf(existing) : -1;
            if (index >= 0) catalog.assets[index] = working;
            else catalog.assets.Add(working);
            EditorUtility.SetDirty(catalog);
            return working;
        }

        // ПР-12П: кадры после первого. Пакет с кадрами заменяет прежний список
        // кадров ракурса целиком; файл кадра k перезаписывается на месте
        // (GUID сохраняется). Пакет без кадров прежние кадры не трогает.
        private static void ImportFrames(ArtAssetDefinition asset, ArtAssetPart part, ArtAssetImportSlot slot, ArtAssetPartView view,
            FileTransaction transaction, List<string> warnings)
        {
            List<ArtAssetFrame> previous = view.Frames ?? new List<ArtAssetFrame>();
            List<ArtAssetFrame> frames = new List<ArtAssetFrame>();
            Vector2Int firstSize = view.Sprite != null ? SpriteNormalMaps.SourceSize(view.Sprite.texture) : Vector2Int.zero;
            for (int i = 0; i < slot.Frames.Count; i++)
            {
                ArtAssetImportFrame source = slot.Frames[i];
                ArtAssetFrame current = i < previous.Count ? previous[i] : null;
                int number = i + 1;
                string where = asset.Name + " · " + ArtAssetLabels.ViewTitle(slot.View) + " · кадр " + (number + 1);
                Sprite sprite = ImportColor(asset, part, slot.View, source.ColorPath, current?.Sprite, transaction, number);
                Texture2D normal = source.NormalPath != null
                    ? ImportNormal(asset, part, slot.View, source.NormalPath, current?.NormalMap, transaction, number)
                    : null;
                if (normal != null) AssignNormal(sprite, normal, warnings, where);
                Vector2Int size = SpriteNormalMaps.SourceSize(sprite.texture);
                if (firstSize != Vector2Int.zero && size != firstSize)
                    warnings.Add(where + ": размер " + size.x + "×" + size.y + " не совпадает с первым кадром " + firstSize.x + "×" + firstSize.y + ".");
                frames.Add(new ArtAssetFrame { Sprite = sprite, NormalMap = normal });
            }
            view.Frames = frames;
        }

        // Новый ассет: эталонный ракурс высотой примерно в человеческий рост,
        // если рисунок не огромный; дальше размер правится в карточке.
        public static void InitializeScale(ArtAssetDefinition asset)
        {
            if (!asset.TryReferenceView(out ArtAssetView view)) return;
            float pixels = asset.MainSprite(view).rect.height;
            asset.PixelsPerUnit = Mathf.Max(ArtAssetDefinition.DefaultPixelsPerUnit, pixels / 6f);
        }

        public static ArtAssetPart FindOrAddPart(ArtAssetDefinition asset, string name)
        {
            if (string.IsNullOrEmpty(name)) return asset.MainPart;
            ArtAssetPart part = asset.Parts.FirstOrDefault(item => item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (part != null) return part;
            part = new ArtAssetPart { Name = PrettyPartName(name), Layer = ArtAssetLayer.World };
            asset.Parts.Add(part);
            return part;
        }

        private static string PrettyPartName(string name)
        {
            string value = name.Replace('_', ' ').Trim();
            return value.Length == 0 ? "Часть" : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        public static ArtAssetDefinition Clone(ArtAssetDefinition source) =>
            JsonUtility.FromJson<ArtAssetDefinition>(JsonUtility.ToJson(source));

        // ------------------------------------------------------------------
        // Файлы
        // ------------------------------------------------------------------

        public static string AssetFolder(ArtAssetDefinition asset) => ManagedRoot + "/" + asset.Id;

        // frame — номер кадра анимации: 0 — первый (прежнее имя), дальше _f001…
        private static string ManagedPath(ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view, bool normal, int frame = 0)
        {
            string name = ArtAssetLabels.ViewFolder(view);
            if (part != asset.MainPart) name += "_part-" + part.Id.Substring(0, Math.Min(8, part.Id.Length));
            if (frame > 0) name += "_f" + frame.ToString("000");
            if (normal) name += "_normal";
            return AssetFolder(asset) + "/" + name + ".png";
        }

        public static bool IsProjectPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            string data = Application.dataPath.Replace('\\', '/');
            return normalized.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static string ToProjectPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            string data = Application.dataPath.Replace('\\', '/');
            return normalized.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase) ? "Assets" + normalized.Substring(data.Length) : normalized;
        }

        // Внешний файл → управляемая копия; файл проекта — как есть.
        private static string Bring(string source, string destination, string current, ArtAssetDefinition asset, FileTransaction transaction)
        {
            if (IsProjectPath(source)) return ToProjectPath(source);
            if (!File.Exists(source)) throw new FileNotFoundException("нет файла " + source);
            // Тот же ракурс уже в управляемой папке — перезаписать на месте (GUID сохраняется).
            string target = current != null && current.StartsWith(AssetFolder(asset) + "/", StringComparison.Ordinal) ? current : destination;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target))
            {
                if (target != current) target = AssetDatabase.GenerateUniqueAssetPath(target);
                else
                {
                    string backup = Path.Combine(Path.GetTempPath(), "ks_art_" + Guid.NewGuid().ToString("N") + ".png");
                    File.Copy(target, backup, true);
                    transaction.Overwritten.Add((target, backup));
                }
            }
            bool created = !File.Exists(target);
            File.Copy(source, target, true);
            if (created) transaction.Created.Add(target);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return target;
        }

        private static Sprite ImportColor(ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view, string source, Sprite current,
            FileTransaction transaction, int frame = 0)
        {
            string currentPath = current != null ? AssetDatabase.GetAssetPath(current) : null;
            bool external = !IsProjectPath(source);
            string path = Bring(source, ManagedPath(asset, part, view, false, frame), currentPath, asset, transaction);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                throw new InvalidOperationException("файл не является изображением: " + path);
            if (external) EnsureColorSettings(importer);
            else if (importer.textureType != TextureImporterType.Sprite)
            {
                // Не перенарезать существующие атласы: меняется только не-спрайт.
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                SpriteNormalMaps.SaveVerified(importer, meta => SpriteNormalMaps.MetaValue(meta, "textureType") == SpriteType);
            }
            if (importer.spriteImportMode == SpriteImportMode.Multiple)
                throw new InvalidOperationException("«" + Path.GetFileName(path) + "» — лист из нескольких спрайтов: перетащите нужный спрайт прямо в ячейку ракурса.");
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("не удалось получить спрайт из " + path);
            return sprite;
        }

        private static Texture2D ImportNormal(ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view, string source, Texture2D current,
            FileTransaction transaction, int frame = 0)
        {
            string currentPath = current != null ? AssetDatabase.GetAssetPath(current) : null;
            bool external = !IsProjectPath(source);
            string path = Bring(source, ManagedPath(asset, part, view, true, frame), currentPath, asset, transaction);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                throw new InvalidOperationException("файл не является изображением: " + path);
            if (external && importer.textureType != TextureImporterType.NormalMap) ConfigureNormal(importer);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("не удалось загрузить нормаль " + path);
            SpriteNormalMaps.PrepareNormalTexture(texture);
            return texture;
        }

        private static readonly string SpriteType = ((int)TextureImporterType.Sprite).ToString();
        private static readonly string NormalType = ((int)TextureImporterType.NormalMap).ToString();

        // Управляемые копии настраивает ArtAssetTexturePostprocessor при первом
        // импорте; здесь — только если настройки всё же не те.
        public static void EnsureColorSettings(TextureImporter importer)
        {
            if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single && importer.sRGBTexture)
                return;
            ConfigureColor(importer);
        }

        public static void ConfigureColor(TextureImporter importer)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = MaxSize(width, height);
            SpriteNormalMaps.SaveVerified(importer, meta => SpriteNormalMaps.MetaValue(meta, "textureType") == SpriteType);
        }

        public static void ConfigureNormal(TextureImporter importer)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            importer.textureType = TextureImporterType.NormalMap;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = MaxSize(width, height);
            SpriteNormalMaps.SaveVerified(importer, meta => SpriteNormalMaps.MetaValue(meta, "textureType") == NormalType);
        }

        private static int MaxSize(int width, int height) =>
            Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(32, Mathf.Max(width, height))), 32, 8192);

        private static void AssignNormal(Sprite sprite, Texture2D normal, List<string> warnings, string where)
        {
            if (SpriteNormalMaps.IsSheet(sprite))
                warnings.Add(where + ": рисунок из листа — нормаль назначена всему листу.");
            Vector2Int color = SpriteNormalMaps.SourceSize(sprite.texture), map = SpriteNormalMaps.SourceSize(normal);
            if (color != map)
                warnings.Add(where + ": размер нормали " + map.x + "×" + map.y + " не совпадает с рисунком " + color.x + "×" + color.y + ".");
            if (!SpriteNormalMaps.Assign(sprite, normal, out string message))
                throw new InvalidOperationException(message);
        }

        // ------------------------------------------------------------------
        // Ячейка ракурса: один файл, очистка
        // ------------------------------------------------------------------

        // Один файл в конкретный слот. Рисунок сохраняет имеющуюся нормаль слота
        // (пара остаётся); нормаль подключается к рисунку слота.
        public static string AssignFile(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view,
            string source, ArtAssetFileKind kind)
        {
            List<string> warnings = new List<string>();
            FileTransaction transaction = new FileTransaction();
            ArtAssetPartView slot = part.View(view);
            Sprite sprite = slot.Sprite;
            Texture2D normal = slot.NormalMap;
            try
            {
                if (kind == ArtAssetFileKind.Color) sprite = ImportColor(asset, part, view, source, slot.Sprite, transaction);
                else normal = ImportNormal(asset, part, view, source, slot.NormalMap, transaction);
                if (sprite != null && normal != null) AssignNormal(sprite, normal, warnings, ArtAssetLabels.ViewTitle(view));
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            bool firstArt = asset.IsEmpty;
            Undo.RecordObject(catalog, kind == ArtAssetFileKind.Color ? "Заменить рисунок ракурса" : "Заменить нормаль ракурса");
            slot.Sprite = sprite;
            slot.NormalMap = normal;
            if (firstArt && part == asset.MainPart) InitializeScale(asset);
            Changed(catalog);
            return warnings.Count > 0 ? string.Join(" ", warnings) : null;
        }

        // Спрайт или текстура из Project — прямо в слот, без копирования.
        public static string AssignProjectObject(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view,
            UnityEngine.Object value, ArtAssetFileKind kind)
        {
            ArtAssetPartView slot = part.View(view);
            List<string> warnings = new List<string>();
            Sprite sprite = slot.Sprite;
            Texture2D normal = slot.NormalMap;
            if (kind == ArtAssetFileKind.Color)
            {
                sprite = value as Sprite;
                if (sprite == null && value is Texture2D texture)
                    sprite = ImportColor(asset, part, view, AssetDatabase.GetAssetPath(texture), slot.Sprite, new FileTransaction());
                if (sprite == null) throw new InvalidOperationException("нужен Sprite или PNG.");
            }
            else
            {
                normal = value as Texture2D ?? (value as Sprite)?.texture;
                if (normal == null) throw new InvalidOperationException("нужна текстура карты нормалей.");
                SpriteNormalMaps.PrepareNormalTexture(normal);
            }
            if (sprite != null && normal != null) AssignNormal(sprite, normal, warnings, ArtAssetLabels.ViewTitle(view));
            bool firstArt = asset.IsEmpty;
            Undo.RecordObject(catalog, "Назначить рисунок ракурса");
            slot.Sprite = sprite;
            slot.NormalMap = normal;
            if (firstArt && part == asset.MainPart) InitializeScale(asset);
            Changed(catalog);
            return warnings.Count > 0 ? string.Join(" ", warnings) : null;
        }

        // Снять рисунок ракурса вместе с кадрами анимации. Файлы не удаляются.
        public static void ClearColor(ArtAssetDatabaseAsset catalog, ArtAssetPart part, ArtAssetView view)
        {
            Undo.RecordObject(catalog, "Очистить рисунок ракурса");
            ArtAssetPartView slot = part.View(view);
            slot.Sprite = null;
            slot.Frames = new List<ArtAssetFrame>();
            Changed(catalog);
        }

        // Снять нормаль: и назначение в записи, и вторую текстуру импорта рисунка
        // (у первого кадра и у всех следующих).
        public static void ClearNormal(ArtAssetDatabaseAsset catalog, ArtAssetPart part, ArtAssetView view)
        {
            ArtAssetPartView slot = part.View(view);
            if (slot.Sprite != null) SpriteNormalMaps.Assign(slot.Sprite, null, out _);
            if (slot.Frames != null)
                foreach (ArtAssetFrame frame in slot.Frames)
                    if (frame?.Sprite != null && frame.NormalMap != null) SpriteNormalMaps.Assign(frame.Sprite, null, out _);
            Undo.RecordObject(catalog, "Снять нормаль ракурса");
            slot.NormalMap = null;
            if (slot.Frames != null)
                foreach (ArtAssetFrame frame in slot.Frames)
                    if (frame != null) frame.NormalMap = null;
            Changed(catalog);
        }

        // Нормали папкой, как в Базе анимаций: папка с теми же ракурсами (и
        // кадрами), что у рисунков; имена любые, суффикс «_normal» не нужен.
        // Ракурс — по папке или имени, кадр — по номеру в конце имени (иначе
        // по порядку). Рисунки ракурсов не меняются. Возвращает отчёт.
        public static string AttachNormalFolder(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, IEnumerable<string> paths, ArtAssetView? defaultView,
            out int attached)
        {
            attached = 0;
            List<string> warnings = new List<string>();
            Dictionary<(ArtAssetView view, string part), List<string>> slots = ArtAssetImportParser.ParseNormals(paths, asset.Name, defaultView, warnings,
                name => asset.Parts.Skip(1).Any(item => item != null && string.Equals(ArtAssetImportParser.NormalizeKey(item.Name), ArtAssetImportParser.NormalizeKey(name), StringComparison.OrdinalIgnoreCase)));
            FileTransaction transaction = new FileTransaction();
            List<(ArtAssetPartView slot, int index, Texture2D normal)> plan = new List<(ArtAssetPartView, int, Texture2D)>();
            try
            {
                foreach (KeyValuePair<(ArtAssetView view, string part), List<string>> pair in slots)
                {
                    ArtAssetPart part = string.IsNullOrEmpty(pair.Key.part) ? asset.MainPart
                        : asset.Parts.FirstOrDefault(item => item != null && string.Equals(ArtAssetImportParser.NormalizeKey(item.Name), ArtAssetImportParser.NormalizeKey(pair.Key.part), StringComparison.OrdinalIgnoreCase));
                    string where = ArtAssetLabels.ViewTitle(pair.Key.view) + (string.IsNullOrEmpty(pair.Key.part) ? "" : " · " + pair.Key.part);
                    ArtAssetPartView slot = part?.FindView(pair.Key.view);
                    if (slot?.Sprite == null) { warnings.Add(where + ": у ассета нет рисунка — нормали пропущены."); continue; }
                    int frames = slot.FrameCount;
                    if (pair.Value.Count != frames)
                        warnings.Add(where + ": нормалей " + pair.Value.Count + ", кадров " + frames + (pair.Value.Count < frames ? " — у остальных кадров нормалей нет." : " — лишние пропущены."));
                    for (int i = 0; i < Math.Min(frames, pair.Value.Count); i++)
                    {
                        Texture2D current = i == 0 ? slot.NormalMap : slot.Frames[i - 1]?.NormalMap;
                        Texture2D normal = ImportNormal(asset, part, pair.Key.view, pair.Value[i], current, transaction, i);
                        AssignNormal(i == 0 ? slot.Sprite : slot.Frames[i - 1].Sprite, normal, warnings, where + (frames > 1 ? " · кадр " + (i + 1) : ""));
                        plan.Add((slot, i, normal));
                    }
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            if (plan.Count > 0)
            {
                Undo.RecordObject(catalog, "Нормали папкой");
                foreach ((ArtAssetPartView slot, int index, Texture2D normal) in plan)
                {
                    if (index == 0) slot.NormalMap = normal;
                    else slot.Frames[index - 1].NormalMap = normal;
                }
                attached = plan.Count;
                Changed(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
            return "Нормалей подключено: " + attached + (slots.Count > 0 ? " (ракурсов " + slots.Count + ")" : "") + "." +
                   (warnings.Count > 0 ? " ⚠ " + string.Join(" ", warnings.Take(6)) : "");
        }

        // ПР-12П: снять кадры анимации ракурса — остаётся первый рисунок. Файлы не удаляются.
        public static void ClearFrames(ArtAssetDatabaseAsset catalog, ArtAssetPart part, ArtAssetView view)
        {
            Undo.RecordObject(catalog, "Снять кадры анимации");
            part.View(view).Frames = new List<ArtAssetFrame>();
            Changed(catalog);
        }

        public static void Changed(ArtAssetDatabaseAsset catalog)
        {
            EditorUtility.SetDirty(catalog);
            catalog.MarkChanged();
        }
    }
}
