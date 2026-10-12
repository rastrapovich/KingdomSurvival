using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Тень солнца на людях и предметах. Силуэты от солнца пишутся в альфу
    // кадра и затемняют только землю (LocationShadowResolve, до предметов).
    // Чужая тень ложится и на тех, кто стоит в ней, — к цвету теней, на силу
    // той же тени, что у земли. Проверка — на CPU, тем же переносом силуэта,
    // что в LocationProjectedShadow, только обратным: точка земли
    // возвращается в рисунок отбрасывающего тень и проверяется его
    // непрозрачность (контуры альфы).
    //   Люди — целиком, по точкам поперёк ступней (фигура узкая), плавно.
    //   Предметы — тенью на самом рисунке: сетка точек по его рамке (до
    //   16 × 8), в каждой — лежит ли тень на земле там, где этот кусок
    //   рисунка; шейдер LocationSpriteAdjust затемняет плавно между точками.
    //   У высокого предмета (опора внизу) кусок рисунка стоит над своим
    //   столбцом у основания; у низкого широкого (камень, опора поднята —
    //   то же правило, что у его тени) рисунок — верх, уходящий вглубь, и
    //   кусок лежит на земле там же, где нарисован.
    // Тень даёт только предмет заметно выше того, на кого падает (дерево —
    // да, трава — нет), силуэт — по одному кадру (ветер не дёргает
    // затемнение). Своя тень и тени людей не учитываются; детали земли (их
    // темнит земля) и предметы без «Освещается солнцем» не темнеют. Предметы
    // пересчитываются, только когда сдвинулось солнце или сам предмет.
    public sealed partial class LocationWorldRenderer
    {
        // Точки поперёк ступней человека (доли полуширины).
        private static readonly float[] ShadeProbes = { -.8f, -.4f, 0, .4f, .8f };
        // За сколько секунд затемнение человека полностью меняется.
        public const float ShadeFadeSeconds = .3f;
        // Тень даёт только предмет хотя бы во столько раз выше того, на кого она падает.
        public const float ShadeCasterTaller = 1.5f;
        // Сетка тени на рисунке: шаг (мир) и наибольшее число точек.
        public const float ShadeCell = .35f;
        public const int ShadeColumns = 16, ShadeRows = 8;

        private static readonly int ShadeMapId = Shader.PropertyToID("_KsShadeMap");
        private static readonly int ShadeRectId = Shader.PropertyToID("_KsShadeRect");
        private static readonly int ShadeGridId = Shader.PropertyToID("_KsShadeGrid");
        private static readonly int ShadeTintId = Shader.PropertyToID("_KsShadeTint");

        // Сетка тени одного рисунка предмета.
        private sealed class ShadeMap
        {
            // Материал рисунка без тени (в тени — материал с поправкой).
            public Material Material;
            public readonly Vector4[] Values = new Vector4[ShadeColumns * ShadeRows / 4];
            public Rect Rect;
            public int Columns, Rows;
            // Хоть одна точка в тени.
            public bool Any;
            public bool Written;
        }

        private struct ShadeCaster
        {
            public Caster Caster;
            public Rect Box;
            // Силуэт для проверки — один кадр (первый увиденный).
            public Sprite Sprite;
            // Видимая высота предмета (мир).
            public float Height;
        }

        private float shadeSeconds = float.NaN;
        private Vector2 shadeSun = new Vector2(float.NaN, float.NaN);
        private float shadeOpacity = -1;
        private int shadeCasterCount = -1;
        private Color shadeTint = Color.clear;
        private readonly List<ShadeCaster> shadeCasters = new List<ShadeCaster>();
        private readonly MaterialPropertyBlock shadeBlock = new MaterialPropertyBlock();

        private void UpdateSunShade()
        {
            float delta = float.IsNaN(shadeSeconds) ? float.PositiveInfinity : Mathf.Max(0, Seconds - shadeSeconds);
            shadeSeconds = Seconds;
            float step = float.IsInfinity(delta) ? 1 : delta / ShadeFadeSeconds;
            Color tint = Sky.ShadowStyle.OcclusionColor;
            tint.a = 1;
            bool sunMoved = float.IsNaN(shadeSun.x) || (shadeSun - sunRaw).sqrMagnitude > 1e-4f || Mathf.Abs(shadeOpacity - sunOpacity) > .01f ||
                            shadeCasterCount != casters.Count || shadeTint != tint;
            if (sunMoved)
            {
                CollectShadeCasters();
                shadeSun = sunRaw;
                shadeOpacity = sunOpacity;
                shadeCasterCount = casters.Count;
                shadeTint = tint;
            }

            // Люди — каждый кадр, целиком.
            foreach (Actor actor in actors.Values)
            {
                if (actor.Image == null || actor.Image.sprite == null) continue;
                Rect body = LocationAlphaContours.OpaqueRect(actor.Image.sprite);
                Vector3 scale = actor.Image.transform.lossyScale;
                float half = Mathf.Max(.05f, body.width * Mathf.Abs(scale.x) * .3f);
                actor.BodyHeight = Mathf.Max(actor.BodyHeight, body.height * Mathf.Abs(scale.y));
                float sum = 0;
                foreach (float probe in ShadeProbes)
                    sum += DarknessAt(actor.Anchor, new Vector2(actor.Anchor.position.x + probe * half, actor.Anchor.position.y), actor.BodyHeight);
                actor.ShadeLevel = Mathf.MoveTowards(actor.ShadeLevel, sum / ShadeProbes.Length, step);
                actor.Shade = Color.Lerp(Color.white, tint, actor.ShadeLevel);
            }

            // Предметы — тенью на рисунке, заново при сдвиге солнца или предмета.
            foreach (Placed item in placed)
            {
                if (item.Resolved == null || item.Images.Count == 0 || !item.Resolved.LitBySun) continue;
                if (!sunMoved && item.ShadeReady && item.ShadeFoot == item.Anchor.position) continue;
                item.ShadeReady = true;
                item.ShadeFoot = item.Anchor.position;
                for (int i = 0; i < item.Images.Count && i < item.Parts.Count && i < item.ShadeMaps.Count; i++)
                {
                    SpriteRenderer image = item.Images[i];
                    if (image == null || image.sprite == null || LitAsGround(item.Parts[i].Band)) continue;
                    ShadeMap map = item.ShadeMaps[i];
                    FillShadeMap(item, image, map);
                    WriteShadeMap(image, map, tint);
                }
            }
        }

        // Сетка тени рисунка: точки по рамке непрозрачного, у каждой — тёмность
        // чужой тени на её точке земли.
        private void FillShadeMap(Placed item, SpriteRenderer image, ShadeMap map)
        {
            Rect opaque = LocationAlphaContours.OpaqueRect(image.sprite);
            Transform from = image.transform;
            Vector2 a = from.TransformPoint(opaque.min), b = from.TransformPoint(opaque.max);
            Rect rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            map.Rect = rect;
            // Низкий широкий предмет — рисунок лежит на земле (как у его тени).
            Caster own = casters.Find(caster => caster.Source == image);
            float lift = own != null && ShadowGroundDrop(own) > 0 ? own.GroundLift : 0;
            float depth = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .3f, lift));
            float baseY = item.Anchor.position.y;
            map.Columns = Mathf.Clamp(Mathf.CeilToInt(rect.width / ShadeCell), 1, ShadeColumns);
            map.Rows = depth > .001f ? Mathf.Clamp(Mathf.CeilToInt(rect.height / ShadeCell), 1, ShadeRows) : 1;
            map.Any = false;
            float height = rect.height;
            // Рамка всего рисунка на земле — для отбора теней.
            Rect reach = Rect.MinMaxRect(rect.xMin, Mathf.Min(baseY, rect.yMin), rect.xMax, Mathf.Max(baseY, Mathf.Lerp(baseY, rect.yMax, depth)) + 1e-3f);
            bool near = false;
            foreach (ShadeCaster entry in shadeCasters)
                if (entry.Caster.Anchor != item.Anchor && entry.Height >= height * ShadeCasterTaller && entry.Box.Overlaps(reach)) { near = true; break; }
            for (int row = 0; row < map.Rows; row++)
                for (int column = 0; column < map.Columns; column++)
                {
                    float value = 0;
                    if (near)
                    {
                        float x = rect.xMin + rect.width * (column + .5f) / map.Columns;
                        float y = map.Rows == 1 ? baseY : rect.yMin + rect.height * (row + .5f) / map.Rows;
                        value = DarknessAt(item.Anchor, new Vector2(x, Mathf.Lerp(baseY, y, depth)), height);
                    }
                    int index = row * map.Columns + column;
                    map.Values[index / 4][index % 4] = value;
                    map.Any |= value > .001f;
                }
        }

        // Сетка — в блок свойств рисунка; в тени рисунок идёт материалом с
        // поправкой, без тени — снова своим.
        private void WriteShadeMap(SpriteRenderer image, ShadeMap map, Color tint)
        {
            if (adjustLit == null) return;
            if (!map.Any && !map.Written) return;
            image.GetPropertyBlock(shadeBlock);
            if (map.Any)
            {
                if (image.sharedMaterial != adjustLit)
                {
                    image.sharedMaterial = adjustLit;
                    // Свой материал без поправки: цвет как есть, свет — как у общего.
                    shadeBlock.SetVector(HsvId, new Vector4(0, 1, 1, 0));
                    shadeBlock.SetVector(LightShareId, new Vector4(1, 1, 0, 0));
                }
                shadeBlock.SetVectorArray(ShadeMapId, map.Values);
                shadeBlock.SetVector(ShadeRectId, new Vector4(map.Rect.xMin, map.Rect.yMin, map.Rect.width, map.Rect.height));
                shadeBlock.SetVector(ShadeGridId, new Vector4(map.Columns, map.Rows, 1, 0));
                shadeBlock.SetVector(ShadeTintId, tint);
                map.Written = true;
            }
            else
            {
                shadeBlock.SetVector(ShadeGridId, Vector4.zero);
                map.Written = false;
                if (map.Material != null && image.sharedMaterial != map.Material) image.sharedMaterial = map.Material;
            }
            image.SetPropertyBlock(shadeBlock);
        }

        // Тени предметов на земле сейчас и их рамки: собираются заново, когда
        // сдвинулось солнце (сотни травинок раскидки не проверяются точно).
        private void CollectShadeCasters()
        {
            shadeCasters.Clear();
            foreach (Caster caster in casters)
            {
                if (caster.People || caster.Anchor == null || !ShadowGround(caster, out Rect box)) continue;
                SpriteRenderer shadow = caster.Shadows[0];
                if (caster.ShadeSprite == null) caster.ShadeSprite = shadow.sprite;
                float height = LocationAlphaContours.OpaqueRect(caster.ShadeSprite).height * Mathf.Abs(shadow.transform.lossyScale.y);
                shadeCasters.Add(new ShadeCaster { Caster = caster, Box = box, Sprite = caster.ShadeSprite, Height = height });
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

        // Самая тёмная чужая тень в точке земли — от предметов выше height.
        private float DarknessAt(Transform self, Vector2 point, float height)
        {
            float darkest = 0;
            foreach (ShadeCaster entry in shadeCasters)
            {
                if (entry.Caster.Anchor == self || entry.Height < height * ShadeCasterTaller || !entry.Box.Contains(point)) continue;
                float darkness = ShadowDarknessAt(entry.Caster, entry.Sprite, point);
                if (darkness > darkest) darkest = darkness;
            }
            return darkest;
        }

        // Тёмность тени солнца одного предмета в точке земли (0 — нет тени).
        private static float ShadowDarknessAt(Caster caster, Sprite sprite, Vector2 point)
        {
            SpriteRenderer shadow = caster.Shadows[0];
            ShadowState state = caster.States[0];
            if (shadow == null || !shadow.enabled || !state.Shown || sprite == null || state.Opacity <= .001f) return 0;
            Projection(state, out float a, out float b);
            if (Mathf.Abs(a) < 1e-4f) return 0;
            float h = (point.y - state.GroundY) / a;
            if (h < 0) return 0;
            float x = point.x - h * b;
            Vector3 local = shadow.transform.InverseTransformPoint(new Vector3(x, state.GroundY + h, 0));
            if (shadow.flipX) local.x = -local.x;
            return LocationAlphaContours.IsOpaqueAt(sprite, local) ? Mathf.Clamp01(state.Opacity) : 0;
        }

        // Затемнение человека чужой тенью солнца (белый — на свету).
        public Color ActorShade(string actorId) => actorId != null && actors.TryGetValue(actorId, out Actor actor) ? actor.Shade : Color.white;

        // Тёмность чужой тени на рисунке предмета (часть part) в точке мира: 0 — на свету.
        public float ObjectShadeAt(string objectId, Vector2 world, int part = 0)
        {
            Placed item = placed.Find(entry => entry.Data.Id == objectId);
            if (item == null || part < 0 || part >= item.ShadeMaps.Count) return 0;
            ShadeMap map = item.ShadeMaps[part];
            if (!map.Any) return 0;
            Vector2 t = new Vector2(Mathf.Clamp01((world.x - map.Rect.xMin) / Mathf.Max(1e-4f, map.Rect.width)),
                Mathf.Clamp01((world.y - map.Rect.yMin) / Mathf.Max(1e-4f, map.Rect.height)));
            int column = Mathf.Min(map.Columns - 1, (int)(t.x * map.Columns)), row = Mathf.Min(map.Rows - 1, (int)(t.y * map.Rows));
            int index = row * map.Columns + column;
            return map.Values[index / 4][index % 4];
        }

        // Рамка рисунка предмета (мир) — для проверок.
        public Rect ObjectShadeRect(string objectId, int part = 0)
        {
            Placed item = placed.Find(entry => entry.Data.Id == objectId);
            return item != null && part >= 0 && part < item.ShadeMaps.Count ? item.ShadeMaps[part].Rect : Rect.zero;
        }
    }
}
