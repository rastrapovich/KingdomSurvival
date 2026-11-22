using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.MapLabBridge;
using UnityEngine;

// Стоянка героя прежней фишкой MapLab при наборе карты: рост ВИДИМОЙ фигуры
// фишки подгоняется под рост бегущего героя в том же ракурсе. Видимая часть
// измеряется по непрозрачным пикселям (порог — как у карточки фигуры), поэтому
// разнобой роста между ракурсами фишки тоже исчезает.
public partial class PrototypeUIController
{
    // Альфа, ниже которой карточка фигуры пиксель отбрасывает (_Cutoff 0.35).
    private const byte StandVisibleAlpha = 89;
    private const int StandExtentMaxHeight = 256;

    // (низ, верх) видимой фигуры в долях высоты области; кэш по кадру и по фишке.
    private static readonly Dictionary<Sprite, Vector2> standFrameExtents = new Dictionary<Sprite, Vector2>();
    private static readonly Dictionary<Texture2D, Vector2> standTextureExtents = new Dictionary<Texture2D, Vector2>();

    private CreatureAnimationSetData standReferenceSet;
    private readonly float[] standReferenceFractions = NewStandFractions();
    private readonly bool[] standLogged = new bool[CreatureAnimationLabels.DirectionCount];

    private static float[] NewStandFractions()
    {
        float[] values = new float[CreatureAnimationLabels.DirectionCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = -1f;
        return values;
    }

    // Область (uvScale, uvOffset) текстуры в уменьшенной копии: низ и верх
    // видимой фигуры. Исходная текстура может быть нечитаемой (Blit).
    private static Vector2 MeasureVisibleExtent(Texture2D source, Vector2 uvScale, Vector2 uvOffset, float regionHeight)
    {
        int h = Mathf.Max(8, Mathf.RoundToInt(Mathf.Min(StandExtentMaxHeight, regionHeight)));
        int w = Mathf.Max(8, Mathf.RoundToInt(h * (uvScale.x * source.width) / Mathf.Max(1f, uvScale.y * source.height)));
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        RenderTexture keep = RenderTexture.active;
        Graphics.Blit(source, rt, uvScale, uvOffset);
        RenderTexture.active = rt;
        Texture2D readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        readable.Apply();
        RenderTexture.active = keep;
        RenderTexture.ReleaseTemporary(rt);
        Color32[] pixels = readable.GetPixels32();
        Object.Destroy(readable);

        int bottom = -1, top = -1;
        for (int y = 0; y < h; y++)
        {
            bool any = false;
            for (int x = 0; x < w && !any; x++)
                any = pixels[y * w + x].a > StandVisibleAlpha;
            if (!any)
                continue;
            if (bottom < 0)
                bottom = y;
            top = y;
        }
        return bottom < 0 ? Vector2.zero : new Vector2(bottom / (float)h, (top + 1) / (float)h);
    }

    private static Vector2 FrameVisibleExtent(Sprite sprite)
    {
        if (standFrameExtents.TryGetValue(sprite, out Vector2 cached))
            return cached;
        Texture2D atlas = sprite.texture;
        // Холст кадра — sprite.rect (textureRect обрезан по прозрачным полям).
        Rect r = sprite.rect;
        Vector2 extent = MeasureVisibleExtent(atlas, new Vector2(r.width / atlas.width, r.height / atlas.height), new Vector2(r.x / atlas.width, r.y / atlas.height), r.height);
        standFrameExtents[sprite] = extent;
        return extent;
    }

    private static Vector2 TextureVisibleExtent(Texture2D texture)
    {
        if (standTextureExtents.TryGetValue(texture, out Vector2 cached))
            return cached;
        Vector2 extent = MeasureVisibleExtent(texture, Vector2.one, Vector2.zero, texture.height);
        standTextureExtents[texture] = extent;
        return extent;
    }

    // Наибольшая доля высоты кадра, занятая фигурой, по кадрам Walk ракурса.
    private float StandReferenceFraction(WorldMapAnimationSetData set, CreatureAnimationDirection direction)
    {
        if (!ReferenceEquals(standReferenceSet, set.Animation))
        {
            standReferenceSet = set.Animation;
            for (int i = 0; i < standReferenceFractions.Length; i++)
            {
                standReferenceFractions[i] = -1f;
                standLogged[i] = false;
            }
        }
        int index = (int)direction;
        if (index < 0 || index >= standReferenceFractions.Length)
            return 0f;
        if (standReferenceFractions[index] >= 0f)
            return standReferenceFractions[index];

        float best = 0f;
        CreatureAnimationClip clip = CreatureAnimationResolver.Resolve(set.Animation, CreatureAnimationAction.Walk, direction);
        if (clip != null)
        {
            for (int i = 0; i < clip.Frames.Count; i++)
            {
                Sprite frame = clip.Frames[i];
                if (frame == null || frame.texture == null)
                    continue;
                Vector2 extent = FrameVisibleExtent(frame);
                best = Mathf.Max(best, extent.y - extent.x);
            }
        }
        standReferenceFractions[index] = best;
        return best;
    }

    // Высота карточки фишки на стоянке (мир): видимая фигура фишки равна по
    // росту видимой фигуре бегущего героя в том же ракурсе. Опора и
    // соотношение сторон — собственные. Нет замера — высота прежняя.
    private float StandMarkerCardHeight(MapLabGameLive.World world, WorldMapAnimationDatabaseAsset database, WorldMapAnimationSetData set, int sector, Texture2D texture, float cardHeight, Vector2 pivot)
    {
        sector = ((sector % 6) + 6) % 6;
        CreatureAnimationDirection direction = database.GetDirection((WorldMapFacing)sector);
        float animationFraction = StandReferenceFraction(set, direction);
        Vector2 marker = TextureVisibleExtent(texture);
        float markerFraction = marker.y - marker.x;
        if (animationFraction <= 0.0001f || markerFraction <= 0.0001f)
            return cardHeight;

        WorldMapHexGrid grid = WorldMapNavigation.Grid;
        float hexWorldWidth = (float)(grid.HexWidth / grid.CanvasWidth) * world.Play.width;
        float referenceWorld = animationFraction * set.HeightInHexes * hexWorldWidth;
        float result = referenceWorld / markerFraction;

        int index = (int)direction;
        if (index >= 0 && index < standLogged.Length && !standLogged[index])
        {
            standLogged[index] = true;
            Debug.Log("[WorldMapHero] стоянка: сектор " + sector + " → ракурс " + direction +
                      "; анимация видимая доля max " + animationFraction.ToString("0.000") + " → " + referenceWorld.ToString("0.00") +
                      "; фишка доля " + markerFraction.ToString("0.000") +
                      ", карточка была " + cardHeight.ToString("0.00") + " стала " + result.ToString("0.00") +
                      "; низ фишки " + marker.x.ToString("0.000") + " при опоре " + pivot.y.ToString("0.000"));
        }
        return result;
    }
}
