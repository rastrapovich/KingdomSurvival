using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge
{
    // 12У-G, задача подсветки до клика: маска альфы рисунка (точная цель
    // клика — прозрачный угол крыши не ловит курсор) и рисунок подсветки —
    // мягкое осветление силуэта и тонкий тёплый льняной контур по внешнему
    // краю. Строится один раз на рисунок через GPU (текстуры не открыты для
    // чтения), без чтения пикселей в кадре.
    public sealed class MapLabSpriteMask
    {
        public const int Width = 192;
        public const float Cutoff = 0.5f;

        public int W, H;
        public bool[] Solid;
        public Texture2D Glow;

        // u, v — доли рисунка от левого верхнего угла.
        public bool Hit(float u, float v)
        {
            if (Solid == null || u < 0f || v < 0f || u >= 1f || v >= 1f)
                return false;
            int x = Mathf.Clamp((int)(u * W), 0, W - 1), y = Mathf.Clamp((int)(v * H), 0, H - 1);
            return Solid[y * W + x];
        }

        private static readonly Dictionary<Texture2D, MapLabSpriteMask> Cache = new Dictionary<Texture2D, MapLabSpriteMask>();

        public static MapLabSpriteMask For(Texture2D texture)
        {
            if (texture == null)
                return null;
            if (Cache.TryGetValue(texture, out MapLabSpriteMask cached) && cached.Glow != null)
                return cached;
            int w = Width, h = Mathf.Max(8, Mathf.RoundToInt(Width * texture.height / (float)Mathf.Max(1, texture.width)));
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D read = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                read.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
            Color32[] px = read.GetPixels32();
            Object.Destroy(read);
            MapLabSpriteMask mask = new MapLabSpriteMask { W = w, H = h, Solid = new bool[w * h] };
            byte cut = (byte)(Cutoff * 255f);
            // GetPixels32 — снизу вверх; маска — сверху вниз.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mask.Solid[y * w + x] = px[(h - 1 - y) * w + x].a >= cut;
            Color32 fill = new Color32(255, 248, 225, 46);
            Color32 edge = new Color32(255, 232, 178, 235);
            Color32 none = new Color32(255, 240, 210, 0);
            Color32[] glow = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color32 c = none;
                    if (mask.Solid[y * w + x])
                        c = fill;
                    else
                    {
                        bool near = false;
                        for (int dy = -2; dy <= 2 && !near; dy++)
                            for (int dx = -2; dx <= 2 && !near; dx++)
                            {
                                if (dx * dx + dy * dy > 5)
                                    continue;
                                int xx = x + dx, yy = y + dy;
                                near = xx >= 0 && yy >= 0 && xx < w && yy < h && mask.Solid[yy * w + xx];
                            }
                        if (near)
                            c = edge;
                    }
                    glow[(h - 1 - y) * w + x] = c;
                }
            }
            mask.Glow = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "MapLab Hover Glow", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            mask.Glow.SetPixels32(glow);
            mask.Glow.Apply(false, true);
            Cache[texture] = mask;
            return mask;
        }
    }
}
