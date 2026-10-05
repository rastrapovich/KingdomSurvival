using System;
using KingdomSurvival.BattlefieldDatabase;
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
}
