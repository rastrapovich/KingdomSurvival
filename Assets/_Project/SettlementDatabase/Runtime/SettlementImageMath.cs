using UnityEngine;

namespace KingdomSurvival.SettlementDatabase
{
    // Кадрирование изображения поселения — один расчёт для предпросмотра в
    // «Базе поселений» и для экрана игры, чтобы автор видел ровно то, что
    // увидит игрок. Фокус: x — слева направо, y — сверху вниз (0..1).
    public static class SettlementImageMath
    {
        // Возвращает видимую часть изображения (UV в долях самого изображения,
        // начало — внизу слева, как у текстур) и куда её рисовать внутри кадра
        // (доли кадра, начало — вверху слева).
        public static Rect VisibleUv(float sourceWidth, float sourceHeight, float frameWidth, float frameHeight,
            bool cover, Vector2 focus, float zoom, out Rect drawRectNormalized)
        {
            drawRectNormalized = new Rect(0f, 0f, 1f, 1f);
            if (sourceWidth <= 0f || sourceHeight <= 0f || frameWidth <= 0f || frameHeight <= 0f)
                return new Rect(0f, 0f, 1f, 1f);
            zoom = Mathf.Clamp(zoom, 1f, 2f);
            float fx = Mathf.Clamp01(focus.x);
            float fyBottom = 1f - Mathf.Clamp01(focus.y);

            float scale = (cover
                ? Mathf.Max(frameWidth / sourceWidth, frameHeight / sourceHeight)
                : Mathf.Min(frameWidth / sourceWidth, frameHeight / sourceHeight)) * zoom;
            float drawnWidth = sourceWidth * scale;
            float drawnHeight = sourceHeight * scale;

            float visibleW = Mathf.Min(1f, frameWidth / drawnWidth);
            float visibleH = Mathf.Min(1f, frameHeight / drawnHeight);
            float u = Mathf.Clamp(fx - visibleW * 0.5f, 0f, 1f - visibleW);
            float v = Mathf.Clamp(fyBottom - visibleH * 0.5f, 0f, 1f - visibleH);

            float drawW = Mathf.Min(drawnWidth, frameWidth) / frameWidth;
            float drawH = Mathf.Min(drawnHeight, frameHeight) / frameHeight;
            drawRectNormalized = new Rect((1f - drawW) * 0.5f, (1f - drawH) * 0.5f, drawW, drawH);
            return new Rect(u, v, visibleW, visibleH);
        }

        // UV в координатах всей текстуры (у спрайта из атласа — своя область).
        public static Rect TextureUv(Sprite sprite, Rect spriteUv)
        {
            if (sprite == null || sprite.texture == null)
                return spriteUv;
            Rect tr = sprite.textureRect;
            Texture2D texture = sprite.texture;
            return new Rect((tr.x + spriteUv.x * tr.width) / texture.width, (tr.y + spriteUv.y * tr.height) / texture.height,
                spriteUv.width * tr.width / texture.width, spriteUv.height * tr.height / texture.height);
        }
    }
}
