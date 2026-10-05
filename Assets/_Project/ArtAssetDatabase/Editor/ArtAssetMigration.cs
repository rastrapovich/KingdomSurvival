using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    public enum ArtAssetMigrationAction { Create, Reuse, Skip }

    public sealed class ArtAssetMigrationEntry
    {
        public LocationVisualObject Item;
        public ArtAssetMigrationAction Action;
        public ArtAssetDefinition Reuse;
        public string Reason;
    }

    public sealed class ArtAssetMigrationReport
    {
        public readonly List<ArtAssetMigrationEntry> Entries = new List<ArtAssetMigrationEntry>();
        public readonly List<string> Problems = new List<string>();
        public int Created, Reused, Skipped, Failed;

        public string Summary => "Создано записей: " + Created + ", переиспользовано: " + Reused + ", пропущено: " + Skipped +
                                 (Failed > 0 ? ", не прошли проверку (оставлены как были): " + Failed : "") + ".";
    }

    // ПР-12Н: перевод предмета с прямым рисунком на ссылку Базы ассетов.
    // Рисунок, опора, размер, слой, основание, свет и тени сохраняются;
    // состояния рисунка (Variants) остаются у экземпляра и работают как прежде.
    // Повторный запуск не создаёт дубликатов: переведённые пропускаются, тот же
    // рисунок с той же опорой переиспользует запись. После перевода показ
    // сверяется с прежним; расхождение — откат этого предмета.
    public static class ArtAssetMigration
    {
        public static ArtAssetMigrationEntry Plan(LocationVisualObject item, ArtAssetDatabaseAsset catalog)
        {
            ArtAssetMigrationEntry entry = new ArtAssetMigrationEntry { Item = item };
            if (item == null) { entry.Action = ArtAssetMigrationAction.Skip; entry.Reason = "пустой предмет"; return entry; }
            if (item.UsesAsset) { entry.Action = ArtAssetMigrationAction.Skip; entry.Reason = "уже из Базы ассетов"; return entry; }
            if (item.LightOnly) { entry.Action = ArtAssetMigrationAction.Skip; entry.Reason = "только источник света"; return entry; }
            if (item.Sprite == null) { entry.Action = ArtAssetMigrationAction.Skip; entry.Reason = "техническая заглушка без рисунка"; return entry; }
            entry.Reuse = FindReusable(item, catalog);
            entry.Action = entry.Reuse != null ? ArtAssetMigrationAction.Reuse : ArtAssetMigrationAction.Create;
            entry.Reason = entry.Reuse != null ? "та же картинка и опора: «" + entry.Reuse.Name + "»" : "новая запись";
            return entry;
        }

        public static ArtAssetMigrationReport Preview(IEnumerable<LocationVisualObject> items, ArtAssetDatabaseAsset catalog)
        {
            ArtAssetMigrationReport report = new ArtAssetMigrationReport();
            // Одинаковые рисунки внутри пакета: второй переиспользует первый.
            List<LocationVisualObject> pending = new List<LocationVisualObject>();
            foreach (LocationVisualObject item in items)
            {
                ArtAssetMigrationEntry entry = Plan(item, catalog);
                if (entry.Action == ArtAssetMigrationAction.Create && pending.Any(other => SameArt(other, item)))
                {
                    entry.Action = ArtAssetMigrationAction.Reuse;
                    entry.Reason = "та же картинка, что у «" + pending.First(other => SameArt(other, item)).Name + "»";
                }
                if (entry.Action == ArtAssetMigrationAction.Create) pending.Add(item);
                report.Entries.Add(entry);
                if (entry.Action == ArtAssetMigrationAction.Create) report.Created++;
                else if (entry.Action == ArtAssetMigrationAction.Reuse) report.Reused++;
                else report.Skipped++;
            }
            return report;
        }

        // Выполнить перевод. undoTarget — база мест (для Undo), catalog — каталог.
        public static ArtAssetMigrationReport Migrate(IEnumerable<LocationVisualObject> items, ArtAssetDatabaseAsset catalog, Object undoTarget)
        {
            List<LocationVisualObject> list = items.ToList();
            ArtAssetMigrationReport report = Preview(list, catalog);
            report.Created = report.Reused = 0;
            Undo.RecordObject(catalog, "Перевести предметы в Базу ассетов");
            if (undoTarget != null) Undo.RecordObject(undoTarget, "Перевести предметы в Базу ассетов");
            foreach (ArtAssetMigrationEntry entry in report.Entries)
            {
                if (entry.Action == ArtAssetMigrationAction.Skip) continue;
                LocationResolvedVisual before = LocationVisualResolver.Resolve(entry.Item, null, catalog);
                LocationVisualObject backup = JsonUtility.FromJson<LocationVisualObject>(JsonUtility.ToJson(entry.Item));
                ArtAssetDefinition asset = FindReusable(entry.Item, catalog);
                bool created = asset == null;
                if (created)
                {
                    asset = CreateFrom(entry.Item, report.Problems);
                    catalog.assets.Add(asset);
                    catalog.MarkChanged();
                }
                Link(entry.Item, asset);
                LocationResolvedVisual after = LocationVisualResolver.Resolve(entry.Item, null, catalog);
                string difference = Difference(before, after);
                if (difference != null)
                {
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(backup), entry.Item);
                    if (created) { catalog.assets.Remove(asset); catalog.MarkChanged(); }
                    report.Failed++;
                    report.Problems.Add(entry.Item.Name + ": " + difference + " — оставлен прямой рисунок.");
                    continue;
                }
                if (created) report.Created++; else report.Reused++;
            }
            EditorUtility.SetDirty(catalog);
            if (undoTarget != null) EditorUtility.SetDirty(undoTarget);
            catalog.MarkChanged();
            return report;
        }

        private static bool SameArt(LocationVisualObject a, LocationVisualObject b) =>
            a.Sprite == b.Sprite && Vector2.Distance(a.Pivot, b.Pivot) < .005f;

        public static ArtAssetDefinition FindReusable(LocationVisualObject item, ArtAssetDatabaseAsset catalog)
        {
            if (catalog == null || item?.Sprite == null) return null;
            return catalog.assets.FirstOrDefault(asset => asset != null && asset.Parts.Count == 1 &&
                                                          asset.MainSprite(ArtAssetView.Front) == item.Sprite &&
                                                          Vector2.Distance(asset.Settings(ArtAssetView.Front).Pivot, item.Pivot) < .005f);
        }

        // Черновая запись из одного рисунка: ракурс «Спереди».
        public static ArtAssetDefinition CreateFrom(LocationVisualObject item, List<string> problems = null)
        {
            Sprite sprite = item.Sprite;
            ArtAssetDefinition asset = new ArtAssetDefinition
            {
                Name = string.IsNullOrWhiteSpace(item.Name) ? sprite.name : item.Name,
                ImportKey = ArtAssetImportParser.NormalizeKey(sprite.name),
                PixelsPerUnit = sprite.rect.height / Mathf.Max(.01f, item.Height),
                BlocksMovement = item.BlocksMovement,
                OccludesLight = item.CastsShadow,
                ShadowLength = item.ShadowLength
            };
            ArtAssetViewSettings settings = asset.Settings(ArtAssetView.Front);
            settings.Pivot = item.Pivot;
            settings.FootprintSize = item.Footprint;
            settings.FootprintOffset = Vector2.zero;
            ArtAssetPart main = asset.MainPart;
            main.Layer = LocationVisualResolver.Layer(item.Band);
            main.OrderOffset = item.OrderOffset;
            main.ProjectsShadow = item.ProjectsShadow;
            ArtAssetPartView view = main.View(ArtAssetView.Front);
            view.Sprite = sprite;
            view.NormalMap = item.NormalMap != null ? item.NormalMap : SpriteNormalMaps.Find(sprite);
            if (item.ShadowSprite != null)
            {
                if (item.ShadowSprite.rect.height > 0 && Mathf.Abs(item.ShadowSprite.rect.height - sprite.rect.height) < .5f) view.ShadowSprite = item.ShadowSprite;
                else problems?.Add(item.Name + ": свой силуэт тени другого размера — перенесите его в карточку вручную.");
            }
            return asset;
        }

        // Привязать экземпляр, сохранив его вид: масштаб — из высоты, отличия
        // от рекомендаций ассета — явные переопределения.
        public static void Link(LocationVisualObject item, ArtAssetDefinition asset)
        {
            item.AssetId = asset.Id;
            item.View = ArtAssetView.Front;
            float height = asset.HeightIn(ArtAssetView.Front);
            item.Scale = height > 0 ? item.Height / height : 1;
            LocationAssetOverride overrides = LocationAssetOverride.None;
            ArtAssetPart main = asset.MainPart;
            if (LocationVisualResolver.Band(main.Layer) != item.Band || main.OrderOffset != item.OrderOffset) overrides |= LocationAssetOverride.Layer;
            ArtAssetViewSettings settings = asset.Settings(ArtAssetView.Front);
            if (asset.BlocksMovement != item.BlocksMovement || Vector2.Distance(settings.FootprintSize * item.Scale, item.Footprint) > .001f ||
                settings.FootprintOffset.sqrMagnitude > 1e-6f)
                overrides |= LocationAssetOverride.Passability;
            if (main.ProjectsShadow != item.ProjectsShadow || !Mathf.Approximately(asset.ShadowLength, item.ShadowLength) ||
                asset.OccludesLight != item.CastsShadow)
                overrides |= LocationAssetOverride.Shadows;
            item.Overrides = overrides;
        }

        // Показ до и после совпадает? null — да, иначе — что разошлось.
        public static string Difference(LocationResolvedVisual before, LocationResolvedVisual after)
        {
            LocationResolvedPart a = before.Main, b = after.Main;
            if (a == null || b == null) return "нет рисунка";
            if (a.Sprite != b.Sprite) return "другой рисунок";
            if (Mathf.Abs(a.Height - b.Height) > .001f) return "другая высота";
            if (Vector2.Distance(a.Pivot, b.Pivot) > .001f) return "другая опора";
            if (a.Band != b.Band || a.OrderOffset != b.OrderOffset) return "другой слой";
            if (a.ProjectsShadow != b.ProjectsShadow) return "другая тень";
            if (before.BlocksMovement != after.BlocksMovement || Vector2.Distance(before.FootprintSize, after.FootprintSize) > .001f ||
                Vector2.Distance(before.FootprintOffset, after.FootprintOffset) > .001f) return "другое основание";
            if (before.OccludesLight != after.OccludesLight || Mathf.Abs(before.ShadowLength - after.ShadowLength) > .001f) return "другие тени";
            return null;
        }
    }
}
