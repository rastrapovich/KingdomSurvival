using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // Одна часть предмета, готовая к показу: рисунок, высота в мире и опора
    // (доля своего рисунка — точка, которая встаёт на опору предмета).
    public sealed class LocationResolvedPart
    {
        public string Name;
        public Sprite Sprite;
        // Рисунка нет — рендерер показывает техническую заглушку.
        public bool Placeholder;
        public float Height;
        public Vector2 Pivot;
        public LocationVisualBand Band;
        public int OrderOffset;
        public bool ProjectsShadow;
        public Sprite ShadowSprite;
        public float ShadowHeight;
        public Vector2 ShadowPivot;
        // ПР-12П: кадры анимации части (первый — Sprite); null — неподвижна.
        public Sprite[] Frames;

        public bool Animated => Frames != null && Frames.Length > 1;
    }

    // Предмет места в выбранном ракурсе: прямой рисунок (прежний формат) или
    // запись Базы ассетов с настройками экземпляра.
    public sealed class LocationResolvedVisual
    {
        public LocationVisualObject Source;
        public ArtAssetDefinition Asset;
        // AssetId задан, а записи в каталоге нет: показан запасной прямой рисунок.
        public bool AssetMissing;
        public ArtAssetView RequestedView;
        public ArtAssetView ShownView;
        // У ассета нет ни одного рисунка.
        public bool NoArt;
        public readonly List<LocationResolvedPart> Parts = new List<LocationResolvedPart>();
        public bool FlipX;
        public bool BlocksMovement;
        // Основание: размер и смещение центра от опоры, единицы мира (Y вверх),
        // с учётом масштаба экземпляра и отражения.
        public Vector2 FootprintSize;
        public Vector2 FootprintOffset;
        public bool OccludesLight;
        public float ShadowLength = 1;
        // ПР-12П: ход кадров у анимированных частей; фаза — своя у экземпляра.
        public float FramesPerSecond;
        public ArtAssetPlayback Playback;
        public float Phase;

        public bool FromAsset => Asset != null;
        public bool ViewFallback => FromAsset && !NoArt && ShownView != RequestedView;
        public LocationResolvedPart Main => Parts.Count > 0 ? Parts[0] : null;
    }

    // ПР-12Н: единое разрешение предмета места для рендерера, проходимости и
    // проверки. Ассет: масштаб всех ракурсов — PixelsPerUnit записи (поля PNG
    // не влияют); опора — своя у каждого ракурса, поэтому смена ракурса не
    // сдвигает точку касания земли; части выравниваются по рисунку основы.
    public static class LocationVisualResolver
    {
        public static LocationResolvedVisual Resolve(LocationVisualObject item, string variantId = null, ArtAssetDatabaseAsset catalog = null)
        {
            LocationResolvedVisual result = new LocationResolvedVisual { Source = item };
            if (item == null) return result;
            result.FlipX = item.FlipX;
            result.RequestedView = item.View;
            ArtAssetDefinition asset = null;
            if (item.UsesAsset)
            {
                asset = catalog != null ? catalog.Find(item.AssetId) : ArtAssetDatabaseAsset.FindCurrent(item.AssetId);
                result.AssetMissing = asset == null;
            }
            if (asset == null) ResolveDirect(item, variantId, result);
            else ResolveAsset(item, asset, variantId, result);
            return result;
        }

        public static LocationVisualBand Band(ArtAssetLayer layer) => (LocationVisualBand)(int)layer;
        public static ArtAssetLayer Layer(LocationVisualBand band) => (ArtAssetLayer)(int)band;

        private static void ResolveDirect(LocationVisualObject item, string variantId, LocationResolvedVisual result)
        {
            Sprite sprite = item.ResolveSprite(variantId);
            result.Parts.Add(new LocationResolvedPart
            {
                Name = item.Name, Sprite = sprite, Placeholder = sprite == null,
                Height = item.Height, Pivot = item.Pivot, Band = item.Band, OrderOffset = item.OrderOffset,
                ProjectsShadow = item.ProjectsShadow && item.Band == LocationVisualBand.World,
                ShadowSprite = item.ShadowSprite, ShadowHeight = item.Height, ShadowPivot = item.Pivot
            });
            result.BlocksMovement = item.BlocksMovement;
            result.FootprintSize = item.Footprint;
            result.FootprintOffset = Vector2.zero;
            result.OccludesLight = item.CastsShadow;
            result.ShadowLength = item.ShadowLength;
        }

        private static void ResolveAsset(LocationVisualObject item, ArtAssetDefinition asset, string variantId, LocationResolvedVisual result)
        {
            result.Asset = asset;
            float scale = item.Scale > 0 ? item.Scale : 1;
            float ppu = asset.SafePixelsPerUnit;
            result.NoArt = !asset.TryResolveView(item.View, out ArtAssetView shown);
            result.ShownView = shown;
            ArtAssetViewSettings settings = asset.Settings(shown);
            Sprite mainSprite = asset.MainSprite(shown);
            Vector2 mainSize = mainSprite != null ? mainSprite.rect.size : Vector2.one * ppu;
            Vector2 pivotPixels = Vector2.Scale(settings.Pivot, mainSize);
            Sprite variant = !string.IsNullOrEmpty(variantId ?? item.DefaultVariantId) ? item.ResolveSprite(variantId) : null;
            if (variant == item.Sprite) variant = null;

            for (int i = 0; i < asset.Parts.Count; i++)
            {
                ArtAssetPart part = asset.Parts[i];
                if (part == null) continue;
                ArtAssetPartView view = part.FindView(shown);
                bool main = i == 0;
                Sprite sprite = main && variant != null ? variant : view?.Sprite;
                if (sprite == null && !main) continue;
                LocationVisualBand band = Band(part.Layer);
                int order = part.OrderOffset;
                if (main && item.IsOverridden(LocationAssetOverride.Layer)) { band = item.Band; order = item.OrderOffset; }
                bool projects = item.IsOverridden(LocationAssetOverride.Shadows) ? item.ProjectsShadow : part.ProjectsShadow;
                LocationResolvedPart resolved = new LocationResolvedPart
                {
                    Name = part.Name, Sprite = sprite, Placeholder = sprite == null, Band = band, OrderOffset = order,
                    ProjectsShadow = projects && band == LocationVisualBand.World
                };
                if (sprite == null)
                {
                    // Ассет без рисунка в ракурсе: заглушка игрового размера на опоре.
                    resolved.Height = Mathf.Max(.2f, asset.Height > 0 ? asset.Height : 1) * scale;
                    resolved.Pivot = settings.Pivot;
                }
                else if (main && variant != null)
                {
                    resolved.Height = sprite.rect.height / ppu * scale;
                    resolved.Pivot = settings.Pivot;
                }
                else
                {
                    Vector2 offsetPixels = (view?.Offset ?? Vector2.zero) * ppu;
                    resolved.Height = sprite.rect.height / ppu * scale;
                    resolved.Pivot = Divide(pivotPixels - offsetPixels, sprite.rect.size);
                }
                Sprite shadow = view?.ShadowSprite;
                if (shadow != null && !(main && variant != null))
                {
                    Vector2 offsetPixels = (view.Offset) * ppu;
                    resolved.ShadowSprite = shadow;
                    resolved.ShadowHeight = shadow.rect.height / ppu * scale;
                    resolved.ShadowPivot = Divide(pivotPixels - offsetPixels, shadow.rect.size);
                }
                // Состояние объекта (вариант) заменяет рисунок основы — без анимации.
                if (view != null && view.IsAnimated && !(main && variant != null)) resolved.Frames = view.FrameSprites();
                result.Parts.Add(resolved);
            }

            bool passability = item.IsOverridden(LocationAssetOverride.Passability);
            result.BlocksMovement = passability ? item.BlocksMovement : asset.BlocksMovement;
            result.FootprintSize = passability ? item.Footprint : settings.FootprintSize * scale;
            Vector2 offset = settings.FootprintOffset * scale;
            if (item.FlipX) offset.x = -offset.x;
            result.FootprintOffset = offset;
            bool shadows = item.IsOverridden(LocationAssetOverride.Shadows);
            result.OccludesLight = shadows ? item.CastsShadow : asset.OccludesLight;
            result.ShadowLength = shadows ? item.ShadowLength : asset.ShadowLength;
            result.FramesPerSecond = asset.FramesPerSecond;
            result.Playback = asset.Playback;
            result.Phase = asset.RandomPhase ? ArtAssetAnimation.StablePhase(item.Id) : 0;
        }

        // Рисунок части в момент seconds (у неподвижной — её единственный).
        public static Sprite FrameAt(LocationResolvedVisual visual, LocationResolvedPart part, double seconds)
        {
            if (part == null) return null;
            if (visual == null || !part.Animated) return part.Sprite;
            return part.Frames[ArtAssetAnimation.FrameIndex(part.Frames.Length, visual.FramesPerSecond, visual.Playback, seconds, visual.Phase)];
        }

        private static Vector2 Divide(Vector2 value, Vector2 size) =>
            new Vector2(value.x / Mathf.Max(1, size.x), value.y / Mathf.Max(1, size.y));
    }
}
