using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12М: готовые настройки света места. Меняют общий свет, солнце и
    // обработку кадра; источники и предметы не трогают. Это стартовые точки
    // для художника, а не канон облика мест [РАБОЧЕЕ].
    public sealed class LocationLightPreset
    {
        public string Name;
        // visual — «улица / под крышей»; sky — небо (общее или своё).
        public Action<LocationVisualDefinition, LocationSky> Apply;
    }

    public static class LocationLightPresets
    {
        public static readonly LocationLightPreset[] All =
        {
            new LocationLightPreset { Name = "Ясный день", Apply = (visual, sky) =>
            {
                visual.Lighting = LocationLightingMode.Outdoor;
                sky.Daylight = new LocationDaylight();
                sky.Sun = new LocationSunDefinition();
                sky.Post = new LocationPostEffects { Enabled = true, Bloom = false };
            } },
            new LocationLightPreset { Name = "Пасмурно", Apply = (visual, sky) =>
            {
                visual.Lighting = LocationLightingMode.Outdoor;
                sky.Daylight = new LocationDaylight { Intensity = .8f };
                sky.Daylight.Color.SetKeys(new[]
                {
                    new GradientColorKey(new Color(.45f, .52f, .65f), 0),
                    new GradientColorKey(new Color(.80f, .82f, .85f), .5f),
                    new GradientColorKey(new Color(.45f, .52f, .65f), 1)
                }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                sky.Sun = new LocationSunDefinition { Opacity = .12f, Softness = .9f };
                sky.Post = new LocationPostEffects { Enabled = true, Bloom = false, Day = new LocationColorGrade { Saturation = -22, Contrast = -5 } };
            } },
            new LocationLightPreset { Name = "Лунная ночь", Apply = (visual, sky) =>
            {
                visual.Lighting = LocationLightingMode.Outdoor;
                sky.Daylight = new LocationDaylight();
                sky.Sun = new LocationSunDefinition { MoonShadows = true, MoonOpacity = .2f };
                sky.Post = new LocationPostEffects
                {
                    Enabled = true, Vignette = true,
                    Night = new LocationColorGrade { Exposure = -.3f, Saturation = -40, Temperature = -30, Contrast = 10, Filter = new Color(.75f, .85f, 1f) }
                };
            } },
            new LocationLightPreset { Name = "Пещера", Apply = (visual, sky) =>
            {
                visual.Lighting = LocationLightingMode.Indoor;
                visual.IndoorColor = new Color(.50f, .47f, .43f);
                visual.IndoorIntensity = .35f;
                sky.Post = new LocationPostEffects
                {
                    Enabled = true, Vignette = true, VignetteIntensity = .38f, Bloom = true,
                    Day = new LocationColorGrade { Exposure = -.15f, Saturation = -15, Contrast = 8 }
                };
            } },
            new LocationLightPreset { Name = "Изба с лучиной", Apply = (visual, sky) =>
            {
                visual.Lighting = LocationLightingMode.Indoor;
                visual.IndoorColor = new Color(.55f, .45f, .35f);
                visual.IndoorIntensity = .3f;
                sky.Post = new LocationPostEffects
                {
                    Enabled = true, Vignette = true, VignetteIntensity = .32f, Bloom = true, Grain = .15f,
                    Day = new LocationColorGrade { Temperature = 18, Saturation = -10 }
                };
            } }
        };
    }

    // ПР-12М: карта нормалей подключается к спрайту как вторая текстура
    // `_NormalMap` в настройках импорта — так её находит 2D-свет Unity,
    // и для отдельного рисунка, и для страницы атласа.
    public static class LocationNormalMaps
    {
        public const string SecondaryName = "_NormalMap";

        public static bool Assign(Sprite sprite, Texture2D normal, out string message)
        {
            message = string.Empty;
            if (sprite == null)
            {
                message = "Нормали подключаются к рисунку-спрайту; у заглушки их нет.";
                return false;
            }
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                message = "У рисунка нет настроек импорта: " + path;
                return false;
            }
            if (normal != null)
                PrepareNormalTexture(normal);
            List<SecondarySpriteTexture> textures = (importer.secondarySpriteTextures ?? Array.Empty<SecondarySpriteTexture>())
                .Where(item => item.name != SecondaryName).ToList();
            if (normal != null)
                textures.Add(new SecondarySpriteTexture { name = SecondaryName, texture = normal });
            importer.secondarySpriteTextures = textures.ToArray();
            importer.SaveAndReimport();
            return true;
        }

        // Карта нормалей импортируется как «Normal map» (линейная, без sRGB).
        public static void PrepareNormalTexture(Texture2D normal)
        {
            string path = AssetDatabase.GetAssetPath(normal);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.textureType == TextureImporterType.NormalMap)
                return;
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        public static Texture2D Find(Sprite sprite)
        {
            if (sprite == null) return null;
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return null;
            foreach (SecondarySpriteTexture item in importer.secondarySpriteTextures ?? Array.Empty<SecondarySpriteTexture>())
                if (item.name == SecondaryName) return item.texture;
            return null;
        }
    }
}
