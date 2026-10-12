using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Тень солнца на людях и предметах. Силуэты от солнца пишутся в альфу
    // кадра и затемняют только землю (LocationShadowResolve, до предметов).
    // Человек или предмет, чьё основание стоит в чужой тени, темнеет так же,
    // как земля под ним: к цвету теней, на силу этой тени. Проверка — на CPU,
    // тем же переносом силуэта, что в LocationProjectedShadow, только
    // обратным: точка у ног возвращается в рисунок отбрасывающего тень и
    // проверяется его непрозрачность (контуры альфы). Несколько точек поперёк
    // основания — вход в тень плавный; сила меняется плавно и по времени.
    // Своя тень и тени людей не учитываются; детали земли и предметы, не
    // освещаемые солнцем, не темнеют.
    public sealed partial class LocationWorldRenderer
    {
        // Точки поперёк основания (доли полуширины).
        private static readonly float[] ShadeProbes = { -.8f, -.4f, 0, .4f, .8f };
        // За сколько секунд затемнение полностью меняется.
        public const float ShadeFadeSeconds = .3f;

        private float shadeSeconds = float.NaN;
        private Vector2 shadeSun = new Vector2(float.NaN, float.NaN);
        private float shadeOpacity = -1;
        private int shadeCasterCount = -1;

        private void UpdateSunShade()
        {
            float delta = float.IsNaN(shadeSeconds) ? float.PositiveInfinity : Mathf.Max(0, Seconds - shadeSeconds);
            shadeSeconds = Seconds;
            float step = float.IsInfinity(delta) ? 1 : delta / ShadeFadeSeconds;
            Color tint = Sky.ShadowStyle.OcclusionColor;
            tint.a = 1;
            // Предметы стоят: тени и затемнение предметов — заново, только когда
            // сдвинулось солнце или сменился состав теней.
            bool sunMoved = float.IsNaN(shadeSun.x) || (shadeSun - sunRaw).sqrMagnitude > 1e-4f || Mathf.Abs(shadeOpacity - sunOpacity) > .01f ||
                            shadeCasterCount != casters.Count;
            if (sunMoved) CollectShadeCasters();

            // Люди — каждый кадр.
            foreach (Actor actor in actors.Values)
            {
                if (actor.Image == null || actor.Image.sprite == null) continue;
                Rect body = LocationAlphaContours.OpaqueRect(actor.Image.sprite);
                float half = Mathf.Max(.05f, body.width * Mathf.Abs(actor.Image.transform.lossyScale.x) * .3f);
                float target = ShadeAt(actor.Anchor, actor.Anchor.position, half);
                actor.ShadeLevel = Mathf.MoveTowards(actor.ShadeLevel, target, step);
                actor.Shade = Color.Lerp(Color.white, tint, actor.ShadeLevel);
            }

            foreach (Placed item in placed)
            {
                if (item.Resolved == null || item.Images.Count == 0 || !item.Resolved.LitBySun) continue;
                // Предмет сдвинули (окно Базы локаций) — тоже заново.
                if (sunMoved || !item.ShadeReady || item.ShadeFoot != item.Anchor.position)
                {
                    item.ShadeFoot = item.Anchor.position;
                    item.ShadeTarget = ShadeAt(item.Anchor, item.ShadeFoot, ObjectHalfWidth(item));
                }
                // Первый раз — сразу, дальше — плавно.
                float level = item.ShadeReady ? Mathf.MoveTowards(item.ShadeLevel, item.ShadeTarget, step) : item.ShadeTarget;
                item.ShadeReady = true;
                if (Mathf.Approximately(level, item.ShadeLevel)) continue;
                item.ShadeLevel = level;
                item.Shade = Color.Lerp(Color.white, tint, level);
                for (int i = 0; i < item.Images.Count && i < item.Parts.Count; i++)
                {
                    SpriteRenderer image = item.Images[i];
                    if (image == null || LitAsGround(item.Parts[i].Band)) continue;
                    Color source = i < item.BaseColor.Count ? item.BaseColor[i] : Color.white;
                    image.color = new Color(source.r * item.Shade.r, source.g * item.Shade.g, source.b * item.Shade.b, image.color.a);
                }
            }
            if (sunMoved)
            {
                shadeSun = sunRaw;
                shadeOpacity = sunOpacity;
                shadeCasterCount = casters.Count;
            }
        }

        // Полуширина основания предмета (мир): основание кистью или
        // прямоугольником, иначе — треть ширины рисунка основы.
        private static float ObjectHalfWidth(Placed item)
        {
            List<Rect> footprint = OcclusionFootprint(item.Resolved);
            float half = 0;
            if (footprint != null)
                foreach (Rect cell in footprint) half = Mathf.Max(half, Mathf.Max(Mathf.Abs(cell.xMin), Mathf.Abs(cell.xMax)));
            if (half <= .01f && item.Image != null) half = item.Image.bounds.extents.x * .66f;
            return Mathf.Max(.05f, half);
        }

        // Тени предметов на земле сейчас и их рамки: собираются заново, когда
        // сдвинулось солнце (сотни травинок раскидки не проверяются точно).
        private readonly List<(Caster caster, Rect box)> shadeCasters = new List<(Caster, Rect)>();

        private void CollectShadeCasters()
        {
            shadeCasters.Clear();
            foreach (Caster caster in casters)
            {
                if (caster.People || caster.Anchor == null || !ShadowGround(caster, out Rect box)) continue;
                shadeCasters.Add((caster, box));
            }
        }

        // Рамка тени на земле: углы рисунка тем же переносом, что в шейдере.
        private static bool ShadowGround(Caster caster, out Rect box)
        {
            box = default;
            SpriteRenderer shadow = caster.Shadows[0];
            ShadowState state = caster.States[0];
            if (shadow == null || !shadow.enabled || !state.Shown || shadow.sprite == null || state.Opacity <= .001f) return false;
            Projection(state, out float a, out float b);
            Bounds bounds = shadow.bounds;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
                {
                    float x = corner % 2 == 0 ? bounds.min.x : bounds.max.x, y = corner < 2 ? bounds.min.y : bounds.max.y;
                    float h = Mathf.Max(0, y - state.GroundY);
                    float px = x + h * b, py = state.GroundY + h * a;
                    minX = Mathf.Min(minX, px); maxX = Mathf.Max(maxX, px);
                    minY = Mathf.Min(minY, py); maxY = Mathf.Max(maxY, py);
                }
            box = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        // Перенос шейдера LocationProjectedShadow: точка на высоте h над
        // линией земли ложится в x + h·b, земля + h·a.
        private static void Projection(ShadowState state, out float a, out float b)
        {
            float lift = state.Lift, rise = 1 - lift;
            float depth = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .3f, lift));
            a = Mathf.Lerp(lift, 1, depth) + rise * state.Vector.y;
            b = rise * state.Vector.x;
        }

        // Сила чужой тени солнца у основания: среднее по точкам поперёк него,
        // в каждой — самая тёмная из теней.
        private float ShadeAt(Transform self, Vector3 foot, float half)
        {
            Rect reach = Rect.MinMaxRect(foot.x - half, foot.y - 1e-3f, foot.x + half, foot.y + 1e-3f);
            float sum = 0;
            foreach (float probe in ShadeProbes)
            {
                Vector2 point = new Vector2(foot.x + probe * half, foot.y);
                float darkest = 0;
                foreach ((Caster caster, Rect box) in shadeCasters)
                {
                    if (caster.Anchor == self || !box.Overlaps(reach)) continue;
                    float darkness = ShadowDarknessAt(caster, point);
                    if (darkness > darkest) darkest = darkness;
                }
                sum += darkest;
            }
            return sum / ShadeProbes.Length;
        }

        // Тёмность тени солнца одного предмета в точке земли (0 — нет тени).
        private static float ShadowDarknessAt(Caster caster, Vector2 point)
        {
            SpriteRenderer shadow = caster.Shadows[0];
            ShadowState state = caster.States[0];
            if (shadow == null || !shadow.enabled || !state.Shown || shadow.sprite == null || state.Opacity <= .001f) return 0;
            Projection(state, out float a, out float b);
            if (Mathf.Abs(a) < 1e-4f) return 0;
            float h = (point.y - state.GroundY) / a;
            if (h < 0) return 0;
            // Быстрый отказ: точка дальше рамки рисунка, перенесённой на землю.
            Bounds bounds = shadow.bounds;
            float top = Mathf.Max(0, bounds.max.y - state.GroundY);
            if (h > top + 1e-3f) return 0;
            float x = point.x - h * b;
            if (x < bounds.min.x - 1e-3f || x > bounds.max.x + 1e-3f) return 0;
            Vector3 local = shadow.transform.InverseTransformPoint(new Vector3(x, state.GroundY + h, 0));
            if (shadow.flipX) local.x = -local.x;
            return LocationAlphaContours.IsOpaqueAt(shadow.sprite, local) ? Mathf.Clamp01(state.Opacity) : 0;
        }

        // Затемнение человека или предмета чужой тенью солнца (белый — на свету).
        public Color ActorShade(string actorId) => actorId != null && actors.TryGetValue(actorId, out Actor actor) ? actor.Shade : Color.white;
        public Color ObjectShade(string objectId) => placed.Find(entry => entry.Data.Id == objectId)?.Shade ?? Color.white;
    }
}
