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
    public IEnumerator TiledTextureLayer_RepeatsThePattern()
    {
        RenderTexture target = new RenderTexture(64, 32, 24, RenderTextureFormat.ARGB32);
        PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.targetTexture = target;
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.clearColor = true;
        panel.colorClearValue = Color.black;
        GameObject host = new GameObject("WorldMapTextureLayerTest");
        Texture2D pattern = new Texture2D(32, 32, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        try
        {
            Color32[] pixels = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                    pixels[y * 32 + x] = x < 16 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
            }
            pattern.SetPixels32(pixels);
            pattern.Apply(true);

            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = panel;
            yield return null;
            VisualElement root = document.rootVisualElement;
            root.style.width = 64;
            root.style.height = 32;
            // Плитка — половина ширины: узор должен повториться дважды.
            root.Add(new WorldMapTextureLayerView(new WorldMapTextureLayer { Texture = pattern, Tiled = true, TileWidthPercent = 50f, TileHeightPercent = 100f }));
            for (int i = 0; i < 6; i++)
                yield return null;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D read = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, 64, 32), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color[] samples = { read.GetPixel(8, 16), read.GetPixel(24, 16), read.GetPixel(40, 16), read.GetPixel(56, 16) };
            Object.Destroy(read);
            Assert.That(samples[0].r, Is.GreaterThan(0.6f), "1-я плитка, левая половина — красная. " + samples[0]);
            Assert.That(samples[1].b, Is.GreaterThan(0.6f), "1-я плитка, правая половина — синяя. " + samples[1]);
            Assert.That(samples[2].r, Is.GreaterThan(0.6f), "2-я плитка повторяет узор — красная. " + samples[2]);
            Assert.That(samples[3].b, Is.GreaterThan(0.6f), "2-я плитка — синяя. " + samples[3]);
        }
        finally
        {
            Object.Destroy(host);
            Object.Destroy(panel);
            Object.Destroy(pattern);
            target.Release();
            Object.Destroy(target);
        }
    }

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
