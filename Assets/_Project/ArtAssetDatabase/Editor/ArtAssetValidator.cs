using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    public enum ArtAssetIssueLevel { Info, Warning, Error }

    public sealed class ArtAssetIssue
    {
        public ArtAssetIssueLevel Level;
        public string Text;
        public bool HasView;
        public ArtAssetView View;

        public override string ToString() => (Level == ArtAssetIssueLevel.Error ? "Ошибка: " : Level == ArtAssetIssueLevel.Warning ? "⚠ " : "") + Text;
    }

    // ПР-12Н: проверка записи. Неполная запись допустима: нехватка ракурсов
    // и нормалей — сведения, а не ошибки; размеры пары, потерянные файлы и
    // неверный импорт нормали — предупреждения.
    public static class ArtAssetValidator
    {
        public static List<ArtAssetIssue> Validate(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset)
        {
            List<ArtAssetIssue> issues = new List<ArtAssetIssue>();
            if (asset == null) return issues;
            void Add(ArtAssetIssueLevel level, string text, ArtAssetView? view = null) =>
                issues.Add(new ArtAssetIssue { Level = level, Text = text, HasView = view.HasValue, View = view ?? ArtAssetView.Front });

            if (string.IsNullOrWhiteSpace(asset.Id)) Add(ArtAssetIssueLevel.Error, "пустой ID.");
            else if (catalog != null && catalog.assets.Count(item => item != null && item.Id == asset.Id) > 1) Add(ArtAssetIssueLevel.Error, "повторный ID " + asset.Id + ".");
            if (string.IsNullOrWhiteSpace(asset.Name)) Add(ArtAssetIssueLevel.Warning, "нет названия.");
            if (asset.PixelsPerUnit <= .01f) Add(ArtAssetIssueLevel.Error, "неверный масштаб (пикселей на единицу мира).");
            if (asset.IsEmpty) Add(ArtAssetIssueLevel.Warning, "нет ни одного рисунка — в месте будет заглушка.");
            else
            {
                List<string> missing = ArtAssetLabels.Views.Where(view => !asset.HasView(view)).Select(ArtAssetLabels.ViewTitle).ToList();
                if (missing.Count > 0) Add(ArtAssetIssueLevel.Info, "не хватает ракурсов: " + string.Join(", ", missing) + ".");
                int normals = asset.NormalCount, views = asset.ViewCount;
                if (normals < views) Add(ArtAssetIssueLevel.Info, "нормали: " + normals + "/" + views + " (не обязательны, улучшают свет).");
            }

            Dictionary<Texture2D, Texture2D> sheetNormals = new Dictionary<Texture2D, Texture2D>();
            foreach (ArtAssetPart part in asset.Parts.Where(item => item != null))
            {
                string partName = asset.Parts.Count > 1 ? "«" + part.Name + "» · " : string.Empty;
                foreach (ArtAssetView view in ArtAssetLabels.Views)
                {
                    ArtAssetPartView slot = part.FindView(view);
                    if (slot == null) continue;
                    string where = partName + ArtAssetLabels.ViewTitle(view) + ": ";
                    if (slot.Sprite != null && part != asset.MainPart && !asset.HasView(view))
                        Add(ArtAssetIssueLevel.Info, where + "есть часть, но нет рисунка основы — ракурс не показывается.", view);
                    if (slot.NormalMap == null)
                    {
                        if (slot.Sprite != null && SpriteNormalMaps.Find(slot.Sprite) != null)
                            Add(ArtAssetIssueLevel.Warning, where + "нормаль подключена к рисунку, но не записана в карточке — нажмите «Исправить подключения».", view);
                        continue;
                    }
                    if (slot.Sprite == null)
                    {
                        Add(ArtAssetIssueLevel.Warning, where + "нормаль без рисунка.", view);
                        continue;
                    }
                    Vector2Int color = SpriteNormalMaps.SourceSize(slot.Sprite.texture), normal = SpriteNormalMaps.SourceSize(slot.NormalMap);
                    bool sheet = SpriteNormalMaps.IsSheet(slot.Sprite);
                    if (color != normal)
                        Add(ArtAssetIssueLevel.Warning, where + "размер нормали " + normal.x + "×" + normal.y + " не совпадает с рисунком " + color.x + "×" + color.y +
                                                        (sheet ? " (для листа нормаль — на весь лист)." : "."), view);
                    if (!SpriteNormalMaps.IsNormalMapImport(slot.NormalMap))
                        Add(ArtAssetIssueLevel.Warning, where + "нормаль импортирована как цветная картинка (sRGB) — нажмите «Исправить подключения».", view);
                    if (SpriteNormalMaps.Find(slot.Sprite) != slot.NormalMap)
                        Add(ArtAssetIssueLevel.Warning, where + "нормаль не подключена к рисунку (_NormalMap) — нажмите «Исправить подключения».", view);
                    if (sheet) Add(ArtAssetIssueLevel.Info, where + "рисунок из листа: нормаль назначается всему листу.", view);
                    float length = ArtAssetDrawing.NormalLength(slot.NormalMap);
                    if (length > GammaLength)
                        Add(ArtAssetIssueLevel.Warning, where + "нормаль, похоже, записана с гамма-коррекцией sRGB (длина векторов " + length.ToString("0.00") +
                                                        " вместо 1,00): свет ложится криво. В Blender — View Transform = Raw; для готового файла — «Исправить гамму нормалей».", view);
                    Texture2D texture = slot.Sprite.texture;
                    if (sheetNormals.TryGetValue(texture, out Texture2D other) && other != slot.NormalMap)
                        Add(ArtAssetIssueLevel.Warning, where + "один лист рисунков — разные нормали: у листа может быть только одна.", view);
                    sheetNormals[texture] = slot.NormalMap;
                }
            }
            issues.AddRange(LostReferences(catalog, asset));
            return issues;
        }

        // Ссылка есть, а файла на этом компьютере нет (рисунки хранятся локально).
        public static IEnumerable<ArtAssetIssue> LostReferences(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset)
        {
            if (catalog == null) yield break;
            int index = catalog.assets.IndexOf(asset);
            if (index < 0) yield break;
            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty parts = serialized.FindProperty("assets").GetArrayElementAtIndex(index).FindPropertyRelative("Parts");
            for (int p = 0; p < parts.arraySize; p++)
            {
                SerializedProperty views = parts.GetArrayElementAtIndex(p).FindPropertyRelative("Views");
                string partName = asset.Parts.Count > 1 && p < asset.Parts.Count ? "«" + asset.Parts[p].Name + "» · " : string.Empty;
                for (int v = 0; v < views.arraySize && v < ArtAssetLabels.ViewCount; v++)
                {
                    SerializedProperty slot = views.GetArrayElementAtIndex(v);
                    foreach (string field in new[] { "Sprite", "NormalMap", "ShadowSprite" })
                    {
                        SerializedProperty reference = slot.FindPropertyRelative(field);
                        if (reference != null && reference.objectReferenceValue == null && reference.objectReferenceEntityIdValue.IsValid())
                            yield return new ArtAssetIssue
                            {
                                Level = ArtAssetIssueLevel.Warning, HasView = true, View = (ArtAssetView)v,
                                Text = partName + ArtAssetLabels.ViewTitle((ArtAssetView)v) + ": потерянная текстура (" +
                                       (field == "Sprite" ? "рисунок" : field == "NormalMap" ? "нормаль" : "силуэт тени") + ") — файла нет на этом компьютере."
                            };
                    }
                }
            }
        }

        // Длина вектора, начиная с которой нормаль считается записанной с гаммой.
        public const float GammaLength = 1.06f;

        // Подключить нормали заново по записи (вторая текстура, импорт как
        // Normal map); нормаль, подключённую к рисунку, но не записанную в
        // карточке (например, после Undo импорта), — записать.
        public static int RepairNormals(ArtAssetDefinition asset)
        {
            int fixedCount = 0;
            foreach (ArtAssetPart part in asset.Parts.Where(item => item != null))
                foreach (ArtAssetView view in ArtAssetLabels.Views)
                {
                    ArtAssetPartView slot = part.FindView(view);
                    if (slot?.Sprite != null && slot.NormalMap == null && SpriteNormalMaps.Find(slot.Sprite) != null)
                    {
                        slot.NormalMap = SpriteNormalMaps.Find(slot.Sprite);
                        fixedCount++;
                        continue;
                    }
                    if (slot?.Sprite == null || slot.NormalMap == null) continue;
                    if (SpriteNormalMaps.Find(slot.Sprite) == slot.NormalMap && SpriteNormalMaps.IsNormalMapImport(slot.NormalMap)) continue;
                    if (SpriteNormalMaps.Assign(slot.Sprite, slot.NormalMap, out _)) fixedCount++;
                }
            return fixedCount;
        }

        // Снять гамма-коррекцию с карт нормалей ассета (sRGB → линейные числа).
        // Переписывается файл в проекте (GUID сохраняется); исходники
        // пользователя вне проекта не меняются.
        public static int FixNormalGamma(ArtAssetDefinition asset)
        {
            int count = 0;
            foreach (Texture2D normal in asset.Parts.Where(item => item != null).SelectMany(part => part.Views ?? new List<ArtAssetPartView>())
                         .Where(slot => slot?.NormalMap != null).Select(slot => slot.NormalMap).Distinct())
            {
                if (ArtAssetDrawing.NormalLength(normal) <= GammaLength) continue;
                string path = AssetDatabase.GetAssetPath(normal);
                Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    if (!source.LoadImage(System.IO.File.ReadAllBytes(path), false)) continue;
                    Color32[] pixels = source.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color32 c = pixels[i];
                        pixels[i] = new Color32(Linear(c.r), Linear(c.g), Linear(c.b), c.a);
                    }
                    source.SetPixels32(pixels);
                    source.Apply(false);
                    System.IO.File.WriteAllBytes(path, source.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(source);
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                count++;
            }
            return count;
        }

        private static byte Linear(byte value) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.GammaToLinearSpace(value / 255f) * 255f), 0, 255);
    }
}
