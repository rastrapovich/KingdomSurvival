using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Подсветка бойцов контуром силуэта — как у героя на глобальной карте,
    // когда он заходит в лес: светлая линия вокруг фигуры поверх деревьев.
    // Свои — жёлтым, противники — красным. Боец в лесу подсвечен всегда;
    // переключатель HighlightAll включает подсветку для всех на поле.
    // Скрытые (пень Срубника) и павшие не подсвечиваются.
    public sealed partial class HexBoardElement
    {
        // Еле заметный контур (просьба 24.11.2026): тонкая линия, полупрозрачная.
        public static readonly Color PlayerHighlightColor = new Color(1f, 0.86f, 0.38f, 0.42f);
        public static readonly Color EnemyHighlightColor = new Color(0.95f, 0.26f, 0.20f, 0.42f);

        private readonly Dictionary<string, Image> unitOutlines = new Dictionary<string, Image>();
        private readonly HashSet<string> visibleOutlineIds = new HashSet<string>();
        private bool highlightAll;

        public bool HighlightAll
        {
            get => highlightAll;
            set
            {
                if (highlightAll == value)
                    return;
                highlightAll = value;
                SyncUnitImages();
            }
        }

        // Подсвечен ли боец сейчас: живой, видимый и (в лесу или включено для всех).
        public bool IsUnitHighlighted(SandboxUnitState unit)
        {
            if (battle == null || unit == null || unit.IsHidden || unit.IsDefeated || unit.IsOffBoard)
                return false;
            return highlightAll || battle.GetVegetation(unit.Position) == SandboxVegetation.Forest;
        }

        private void SyncUnitOutline(SandboxUnitState unit, Image figure, bool shownAlive)
        {
            if (!shownAlive || figure == null || figure.sprite == null || !IsUnitHighlighted(unit))
                return;
            Texture2D outline = SandboxFigureOutline.For(figure.sprite);
            if (outline == null)
                return;
            if (!unitOutlines.TryGetValue(unit.Id, out Image image))
            {
                image = new Image { pickingMode = PickingMode.Ignore, name = "sandbox-unit-outline-" + unit.Id };
                image.style.position = Position.Absolute;
                unitOutlines.Add(unit.Id, image);
                Add(image);
            }
            image.image = outline;
            image.scaleMode = figure.scaleMode;
            image.tintColor = unit.Team == SandboxTeam.Enemy ? EnemyHighlightColor : PlayerHighlightColor;
            image.style.left = figure.style.left;
            image.style.top = figure.style.top;
            image.style.width = figure.style.width;
            image.style.height = figure.style.height;
            image.style.scale = figure.style.scale;
            image.style.display = DisplayStyle.Flex;
            visibleOutlineIds.Add(unit.Id);
        }

        // После порядка слоёв: контуры поверх деревьев и фигур, под полосами здоровья.
        private void FinishUnitOutlines()
        {
            List<string> removed = null;
            foreach (KeyValuePair<string, Image> pair in unitOutlines)
            {
                if (visibleOutlineIds.Contains(pair.Key))
                {
                    pair.Value.BringToFront();
                    continue;
                }
                pair.Value.RemoveFromHierarchy();
                (removed ??= new List<string>()).Add(pair.Key);
            }
            if (removed != null)
                foreach (string id in removed)
                    unitOutlines.Remove(id);
            visibleOutlineIds.Clear();
        }

        private void ClearUnitOutlines()
        {
            foreach (Image image in unitOutlines.Values)
                image.RemoveFromHierarchy();
            unitOutlines.Clear();
            visibleOutlineIds.Clear();
        }
    }

    // Белый контур вокруг силуэта кадра (снаружи, тонкий — ~0,7% высоты); цвет задаёт
    // tintColor картинки. Строится один раз на кадр (кэш), в уменьшенном
    // разрешении; читаемость исходной текстуры не нужна (Blit).
    internal static class SandboxFigureOutline
    {
        private const int MaxHeight = 256;
        private static readonly Dictionary<Sprite, Texture2D> cache = new Dictionary<Sprite, Texture2D>();

        public static Texture2D For(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
                return null;
            if (cache.TryGetValue(sprite, out Texture2D cached) && cached != null)
                return cached;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return null;
            Texture2D atlas = sprite.texture;
            Rect r = sprite.rect;
            float k = Mathf.Min(1f, MaxHeight / Mathf.Max(1f, r.height));
            int w = Mathf.Max(8, Mathf.RoundToInt(r.width * k)), h = Mathf.Max(8, Mathf.RoundToInt(r.height * k));
            Vector2 scale = new Vector2(r.width / atlas.width, r.height / atlas.height);
            Vector2 offset = new Vector2(r.x / atlas.width, r.y / atlas.height);
            Texture2D outline = Build(atlas, scale, offset, w, h, sprite.name);
            cache[sprite] = outline;
            return outline;
        }

        private static Texture2D Build(Texture2D source, Vector2 uvScale, Vector2 uvOffset, int w, int h, string name)
        {
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
            if (Application.isPlaying) Object.Destroy(readable); else Object.DestroyImmediate(readable);

            bool[] mask = new bool[w * h];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = pixels[i].a > 90;
            int radius = Mathf.Max(1, Mathf.RoundToInt(h * 0.007f));
            // Расширение маски: по строкам, затем по столбцам (квадратное ядро).
            bool[] rows = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                int last = -100000;
                for (int x = 0; x < w; x++)
                    if (mask[y * w + x]) last = x;
                    else if (x - last <= radius) rows[y * w + x] = true;
                last = 100000;
                for (int x = w - 1; x >= 0; x--)
                    if (mask[y * w + x]) last = x;
                    else if (last - x <= radius) rows[y * w + x] = true;
                for (int x = 0; x < w; x++)
                    rows[y * w + x] |= mask[y * w + x];
            }
            bool[] grown = new bool[w * h];
            for (int x = 0; x < w; x++)
            {
                int last = -100000;
                for (int y = 0; y < h; y++)
                    if (rows[y * w + x]) last = y;
                    else if (y - last <= radius) grown[y * w + x] = true;
                last = 100000;
                for (int y = h - 1; y >= 0; y--)
                    if (rows[y * w + x]) last = y;
                    else if (last - y <= radius) grown[y * w + x] = true;
                for (int y = 0; y < h; y++)
                    grown[y * w + x] |= rows[y * w + x];
            }
            Color32 line = new Color32(255, 255, 255, 255);
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32[] result = new Color32[w * h];
            for (int i = 0; i < result.Length; i++)
                result[i] = grown[i] && !mask[i] ? line : clear;
            Texture2D outline = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name + " · контур боя",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            outline.SetPixels32(result);
            outline.Apply(false, true);
            return outline;
        }
    }
}
