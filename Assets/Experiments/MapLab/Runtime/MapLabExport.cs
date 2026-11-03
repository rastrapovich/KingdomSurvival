using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KingdomSurvival.MapLab
{
    // Чистый рендер камеры MapLab в PNG: без Gizmos и интерфейса, со
    // сглаживанием суперсэмплингом. Камера и RenderTexture восстанавливаются.
    public static class MapLabExport
    {
        // Этап 05: сглаживание экспорта. Суперсэмплинг усредняется в линейном
        // пространстве (прежний вариант — среднее sRGB-байтов: тонкие тёмные
        // линии от него темнеют и «рвутся»), плюс MSAA своей RenderTexture,
        // если устройство его поддерживает (для AlphaToMask карточек). Рендерер
        // проекта не меняется — это только целевая текстура экспорта MapLab.
        public static bool LinearDownsample;
        public static int Msaa = 1;
        public static string LastProfile { get; private set; } = "";
        public static int LastMsaa { get; private set; } = 1;

        private static float[] srgbToLinear;

        public static byte[] RenderPng(MapLabCameraRig rig, int width, int height, int supersample = 2)
        {
            Texture2D image = Render(rig, width, height, supersample);
            try
            {
                return image.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        public static Texture2D Render(MapLabCameraRig rig, int width, int height, int supersample = 2)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            width = Mathf.Clamp(width, 64, 8192);
            height = Mathf.Clamp(height, 64, 8192);
            supersample = Mathf.Clamp(supersample, 1, 3);
            while (supersample > 1 && (width * supersample > 8192 || height * supersample > 8192))
                supersample--;
            Camera cam = rig.Camera;
            RenderTexture previousTarget = cam.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            float previousAspect = cam.aspect;
            int rw = width * supersample, rh = height * supersample;
            int msaa = SupportedMsaa(rw, rh, Mathf.Clamp(Msaa, 1, 8));
            LastMsaa = msaa;
            LastProfile = $"SSAA {supersample}x{supersample} " + (supersample > 1 ? (LinearDownsample ? "linear" : "sRGB-average") : "none") + (msaa > 1 ? $" + MSAA {msaa}x (AlphaToMask)" : " + no MSAA");
            RenderTexture rt = new RenderTexture(rw, rh, 24, GraphicsFormat.R8G8B8A8_SRGB)
            {
                name = "MapLab Export",
                antiAliasing = msaa,
                hideFlags = HideFlags.DontSave,
            };
            Texture2D full = null;
            try
            {
                rt.Create();
                cam.targetTexture = rt;
                cam.aspect = width / (float)height;
                RefitKeepingView(rig);
                // Пределы толщины пера в пикселях — в пикселях итогового кадра.
                Shader.SetGlobalFloat("_MapLabPixelScale", supersample);
                cam.Render();
                Shader.SetGlobalFloat("_MapLabPixelScale", 1f);
                RenderTexture.active = rt;
                full = new Texture2D(rw, rh, TextureFormat.RGBA32, false, false);
                full.ReadPixels(new Rect(0, 0, rw, rh), 0, 0, false);
                full.Apply(false, false);
                if (supersample == 1)
                {
                    Texture2D result = full;
                    full = null;
                    return result;
                }
                Color32[] source = full.GetPixels32();
                Color32[] target = new Color32[width * height];
                int s2 = supersample * supersample;
                if (LinearDownsample)
                {
                    DownsampleLinear(source, target, width, height, supersample);
                    Texture2D linear = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                    linear.SetPixels32(target);
                    linear.Apply(false, false);
                    return linear;
                }
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int r = 0, g = 0, b = 0;
                        for (int dy = 0; dy < supersample; dy++)
                        for (int dx = 0; dx < supersample; dx++)
                        {
                            Color32 c = source[(y * supersample + dy) * rw + x * supersample + dx];
                            r += c.r; g += c.g; b += c.b;
                        }
                        target[y * width + x] = new Color32((byte)(r / s2), (byte)(g / s2), (byte)(b / s2), 255);
                    }
                }
                Texture2D downsampled = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                downsampled.SetPixels32(target);
                downsampled.Apply(false, false);
                return downsampled;
            }
            finally
            {
                cam.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                cam.aspect = previousAspect;
                cam.ResetAspect();
                RefitKeepingView(rig);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                if (full != null)
                    UnityEngine.Object.DestroyImmediate(full);
            }
        }

        private static int SupportedMsaa(int w, int h, int wanted)
        {
            if (wanted <= 1)
                return 1;
            RenderTextureDescriptor d = new RenderTextureDescriptor(w, h, GraphicsFormat.R8G8B8A8_SRGB, 24) { msaaSamples = wanted };
            return Mathf.Max(1, Mathf.Min(wanted, SystemInfo.GetRenderTextureSupportedMSAASampleCount(d)));
        }

        // Среднее sRGB-пикселей в линейном свете и обратно в sRGB.
        public static void DownsampleLinear(Color32[] source, Color32[] target, int width, int height, int supersample)
        {
            if (srgbToLinear == null)
            {
                float[] table = new float[256];
                for (int i = 0; i < 256; i++)
                    table[i] = Mathf.GammaToLinearSpace(i / 255f);
                srgbToLinear = table;
            }
            int rw = width * supersample;
            float inv = 1f / (supersample * supersample);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (int dy = 0; dy < supersample; dy++)
                    for (int dx = 0; dx < supersample; dx++)
                    {
                        Color32 c = source[(y * supersample + dy) * rw + x * supersample + dx];
                        r += srgbToLinear[c.r]; g += srgbToLinear[c.g]; b += srgbToLinear[c.b];
                    }
                    target[y * width + x] = new Color32(ToSrgb(r * inv), ToSrgb(g * inv), ToSrgb(b * inv), 255);
                }
            }
        }

        private static byte[] linearToSrgb;

        // Обратный перевод — по таблице 4096 ступеней (без pow на пиксель).
        private static byte ToSrgb(float linear)
        {
            if (linearToSrgb == null)
            {
                byte[] table = new byte[4096];
                for (int i = 0; i < table.Length; i++)
                    table[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(i / 4095f) * 255f), 0, 255);
                linearToSrgb = table;
            }
            return linearToSrgb[Mathf.Clamp((int)(linear * 4095f + 0.5f), 0, 4095)];
        }

        // Пересчитать «вся карта» под соотношение сторон, сохранив фокус, приближение и режим.
        private static void RefitKeepingView(MapLabCameraRig rig)
        {
            rig.RecomputeFitSize();
            rig.Apply();
        }
    }
}
