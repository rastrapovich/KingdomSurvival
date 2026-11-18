using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLabBridge;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    // Перо сетки боя = перо дорог MapLab (MAP_STYLE_GUIDE.md §5 «Единое перо»):
    // цвет, толщина, плотность, нажим, разрывы и окончания берутся из профиля
    // рисунка игры (MapLabGameAssets.Profile, Stage05). Вид карты с тушью
    // (сепия) красит и сетку своим цветом туши. Длины пера заданы в ед. мира
    // MapLab, у сетки — в радиусах гекса (радиус клетки арены — 3 ед.).
    public static class BattlefieldInkPenSync
    {
        private const float ArenaHexWorld = 3f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            MapLabMapViews.ActiveChanged -= Apply;
            MapLabMapViews.ActiveChanged += Apply;
            Apply();
        }

        public static void Apply()
        {
            BattlefieldInkPen pen = new BattlefieldInkPen();
            MapLabGameAssets assets = MapLabGameAssets.Load();
            MapLabStage05Settings s5 = assets != null && assets.Profile != null ? assets.Profile.Stage05 : null;
            if (s5 != null)
            {
                pen.Color = s5.InkColor;
                pen.WidthShare = s5.InkWidth / ArenaHexWorld;
                pen.MinPixels = s5.InkMinPixels;
                pen.MaxPixels = Mathf.Max(s5.InkMinPixels + 0.5f, s5.InkMaxPixels);
                pen.Density = s5.InkDensity;
                pen.Drift = s5.InkThicknessDrift;
                pen.DriftLength = s5.InkDriftLength / ArenaHexWorld;
                pen.BreakLength = s5.InkBreakLength / ArenaHexWorld;
                pen.BreakShare = s5.InkBreakShare;
                pen.Grain = s5.InkGrain;
                pen.Endings = s5.InkEndings;
            }
            MapLabMapView view = MapLabMapViews.Active;
            if (view != null && view.Grade != null && !view.Grade.IsNeutral && view.Grade.InkStrength > 0f)
                pen.Color = Color.Lerp(pen.Color, view.Grade.InkColor, Mathf.Clamp01(view.Grade.InkStrength));
            BattlefieldInkPen.Current = pen;
        }
    }
}
