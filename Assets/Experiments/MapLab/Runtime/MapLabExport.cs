using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KingdomSurvival.MapLab
{
    // Чистый рендер камеры MapLab в PNG: без Gizmos и интерфейса, со
    // сглаживанием суперсэмплингом. Камера и RenderTexture восстанавливаются.
    public static class MapLabExport
    {
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
            RenderTexture rt = new RenderTexture(rw, rh, 24, GraphicsFormat.R8G8B8A8_SRGB)
            {
                name = "MapLab Export",
                antiAliasing = 1,
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

        // Пересчитать «вся карта» под соотношение сторон, сохранив фокус, приближение и режим.
        private static void RefitKeepingView(MapLabCameraRig rig)
        {
            rig.RecomputeFitSize();
            rig.Apply();
        }
    }
}
