using System.Collections;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Слой своих рисунков поверх карты рисуется видеокартой ровно как
// загружен: верх картинки — вверху экрана, рисунок не перевёрнут.
public sealed class WorldMapSignLayerPlayModeTests
{
    [UnityTest]
    public IEnumerator SignLayer_DrawsTheLoadedPictureUpright()
    {
        RenderTexture target = new RenderTexture(64, 128, 24, RenderTextureFormat.ARGB32);
        PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.targetTexture = target;
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.clearColor = true;
        panel.colorClearValue = Color.black;
        GameObject host = new GameObject("WorldMapSignLayerTest");
        Texture2D atlas = new Texture2D(32, 64, TextureFormat.RGBA32, true);
        try
        {
            Color32[] pixels = new Color32[32 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 32; x++)
                    pixels[y * 32 + x] = y >= 32 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
            }
            atlas.SetPixels32(pixels);
            atlas.Apply(true);

            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = panel;
            yield return null;

            VisualElement root = document.rootVisualElement;
            root.style.width = 64;
            root.style.height = 128;
            WorldMapSigns signs = new WorldMapSigns { Atlas = atlas };
            signs.Items.Add(new WorldMapSign
            {
                XPercent = 50f,
                YPercent = 100f,
                HeightPercent = 100f,
                Aspect = 0.5f,
                Uv = new Rect(0f, 0f, 1f, 1f),
                Tint = new Color32(255, 255, 255, 255)
            });
            root.Add(new WorldMapSignLayer { Signs = signs });

            for (int i = 0; i < 6; i++)
                yield return null;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D read = new Texture2D(64, 128, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, 64, 128), 0, 0);
            read.Apply();
            RenderTexture.active = previous;

            // ReadPixels: строка 0 — низ кадра.
            Color top = read.GetPixel(32, 112);
            Color bottom = read.GetPixel(32, 16);
            Object.Destroy(read);
            Assert.That(top.r, Is.GreaterThan(0.6f), "Вверху экрана — верх рисунка (красный). Получено " + top);
            Assert.That(bottom.b, Is.GreaterThan(0.6f), "Внизу экрана — низ рисунка (синий). Получено " + bottom);
        }
        finally
        {
            Object.Destroy(host);
            Object.Destroy(panel);
            Object.Destroy(atlas);
            target.Release();
            Object.Destroy(target);
        }
    }
}
