using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Просвечивание высоких предметов (галочка ассета «Просвечивает, когда
    // за ним человек»): пока фигура стоит за предметом (рисуется раньше него)
    // и непрозрачная часть его рисунка закрывает фигуру, в рисунке плавно
    // открывается мягкий полупрозрачный круг вокруг неё (шейдер
    // LocationSpriteAdjust, _KsHoles); остальной предмет не меняется. Фигура
    // вышла — круг закрывается. Тень, свет и окклюзия не меняются. Без
    // шейдера — запасной путь: полупрозрачен весь предмет.
    public sealed partial class LocationWorldRenderer
    {
        // За сколько секунд круг полностью открывается или закрывается.
        public const float SeeThroughFadeSeconds = .25f;
        // Кругов на предмет (отряд — до пяти человек, с запасом).
        public const int SeeThroughHoles = 6;
        // Ширина человека — доля роста его фигуры.
        public const float PersonWidthShare = .5f;
        // Мягкий край круга — доля радиуса.
        public const float SeeThroughSoftEdge = .35f;

        private static readonly int HolesId = Shader.PropertyToID("_KsHoles");
        private static readonly int HoleShapeId = Shader.PropertyToID("_KsHoleShape");

        // Точки фигуры, по которым проверяется закрытие: середина, грудь,
        // голова, ноги и бока (доли рамки самой фигуры, от низа).
        private static readonly Vector2[] ActorProbes =
        {
            new Vector2(.5f, .5f), new Vector2(.5f, .7f), new Vector2(.5f, .88f), new Vector2(.5f, .3f),
            new Vector2(.3f, .55f), new Vector2(.7f, .55f)
        };

        private float seeThroughSeconds = float.NaN;
        private readonly List<Actor> holeActors = new List<Actor>();

        private void UpdateSeeThrough()
        {
            float delta = float.IsNaN(seeThroughSeconds) ? float.PositiveInfinity : Mathf.Max(0, Seconds - seeThroughSeconds);
            seeThroughSeconds = Seconds;
            float step = float.IsInfinity(delta) ? 1 : delta / SeeThroughFadeSeconds;
            foreach (Placed item in placed)
            {
                if (item.Resolved == null || !item.Resolved.SeeThrough || item.Images.Count == 0) continue;
                // Сила круга у каждого человека: закрыт — растёт, нет — гаснет.
                holeActors.Clear();
                holeActors.AddRange(item.Holes.Keys);
                foreach (Actor actor in actors.Values)
                    if (!item.Holes.ContainsKey(actor)) holeActors.Add(actor);
                float strongest = 0;
                foreach (Actor actor in holeActors)
                {
                    item.Holes.TryGetValue(actor, out float level);
                    bool present = actor.Anchor != null && actors.TryGetValue(actor.Id, out Actor current) && current == actor;
                    float target = present && Hides(item, actor) ? 1 : 0;
                    level = Mathf.MoveTowards(level, target, step);
                    if (level <= 0 && target <= 0) item.Holes.Remove(actor);
                    else item.Holes[actor] = level;
                    strongest = Mathf.Max(strongest, level);
                }
                float alpha = Mathf.Clamp01(item.Resolved.SeeThroughAlpha);
                item.SeeThroughLevel = Mathf.Lerp(1, alpha, strongest);
                if (adjustLit != null && item.Images[0] != null && item.Images[0].sharedMaterial == adjustLit) WriteHoles(item, alpha);
                else FadeWhole(item);
            }
        }

        // Круги в блок свойств рисунков — только если что-то изменилось.
        private void WriteHoles(Placed item, float alpha)
        {
            Vector4[] data = holeScratch;
            int count = 0;
            foreach (KeyValuePair<Actor, float> hole in item.Holes)
            {
                if (count >= SeeThroughHoles || hole.Value <= 0) continue;
                if (!BodyCircle(hole.Key, item.Resolved.SeeThroughSize, out Vector2 center, out float radius)) continue;
                data[count++] = new Vector4(center.x, center.y, radius, hole.Value);
            }
            for (int i = count; i < SeeThroughHoles; i++) data[i] = Vector4.zero;
            bool same = item.HolesWritten;
            for (int i = 0; i < SeeThroughHoles && same; i++) same = (item.HoleData[i] - data[i]).sqrMagnitude < 1e-10f;
            if (same) return;
            item.HolesWritten = true;
            System.Array.Copy(data, item.HoleData, SeeThroughHoles);
            foreach (SpriteRenderer image in item.Images)
            {
                if (image == null) continue;
                image.GetPropertyBlock(adjustBlock);
                adjustBlock.SetVectorArray(HolesId, item.HoleData);
                adjustBlock.SetVector(HoleShapeId, new Vector4(alpha, SeeThroughSoftEdge, 0, 0));
                image.SetPropertyBlock(adjustBlock);
            }
        }

        private readonly Vector4[] holeScratch = new Vector4[SeeThroughHoles];

        // Запасной путь (нет шейдера с кругом): весь предмет полупрозрачен.
        private static void FadeWhole(Placed item)
        {
            for (int i = 0; i < item.Images.Count; i++)
            {
                SpriteRenderer image = item.Images[i];
                if (image == null) continue;
                Color color = image.color;
                float value = (i < item.BaseAlpha.Count ? item.BaseAlpha[i] : 1) * item.SeeThroughLevel;
                if (Mathf.Approximately(color.a, value)) continue;
                color.a = value;
                image.color = color;
            }
        }

        // Круг вокруг фигуры: центр — середина самой фигуры, поперечник — size
        // человек шириной в половину роста фигуры (рост — наибольший из кадров,
        // чтобы круг не дышал с анимацией).
        private static bool BodyCircle(Actor actor, float size, out Vector2 center, out float radius)
        {
            center = Vector2.zero;
            radius = 0;
            SpriteRenderer figure = actor.Image;
            if (figure == null || figure.sprite == null) return false;
            Rect body = LocationAlphaContours.OpaqueRect(figure.sprite);
            Vector2 middle = body.center;
            if (figure.flipX) middle.x = -middle.x;
            center = figure.transform.TransformPoint(middle);
            actor.BodyHeight = Mathf.Max(actor.BodyHeight, body.height * Mathf.Abs(figure.transform.lossyScale.y));
            radius = size * PersonWidthShare * actor.BodyHeight / 2;
            return radius > 1e-4f;
        }

        // Непрозрачность предмета там, где он сильнее всего просвечивает (1 — не просвечивает).
        public float ObjectSeeThroughLevel(string objectId) => placed.Find(entry => entry.Data.Id == objectId)?.SeeThroughLevel ?? 1;

        // Круги просвечивания предмета сейчас: xy — центр (мир), z — радиус, w — сила.
        public IReadOnlyList<Vector4> ObjectSeeThroughHoles(string objectId)
        {
            Placed item = placed.Find(entry => entry.Data.Id == objectId);
            List<Vector4> result = new List<Vector4>();
            if (item != null && item.HolesWritten)
                foreach (Vector4 hole in item.HoleData)
                    if (hole.z > 0) result.Add(hole);
            return result;
        }

        private static bool Hides(Placed item, Actor actor)
        {
            SpriteRenderer figure = actor.Image;
            if (figure == null || !figure.enabled || figure.sprite == null) return false;
            Bounds bounds = figure.bounds;
            // Сама фигура в кадре (у кадров анимации широкие прозрачные поля).
            Rect body = LocationAlphaContours.OpaqueRect(figure.sprite);
            foreach (SpriteRenderer image in item.Images)
            {
                // Фигура перед предметом — он её не закрывает.
                if (image == null || image.sprite == null || image.sortingOrder <= figure.sortingOrder) continue;
                if (!image.bounds.Intersects(bounds)) continue;
                foreach (Vector2 probe in ActorProbes)
                {
                    Vector2 local = new Vector2(body.xMin + body.width * probe.x, body.yMin + body.height * probe.y);
                    if (figure.flipX) local.x = -local.x;
                    if (Covers(image, figure.transform.TransformPoint(local))) return true;
                }
            }
            return false;
        }

        // Непрозрачен ли рисунок в точке мира (альфа нечитаема — по рамке).
        private static bool Covers(SpriteRenderer image, Vector3 world)
        {
            Vector3 local = image.transform.InverseTransformPoint(world);
            if (image.flipX) local.x = -local.x;
            return LocationAlphaContours.IsOpaqueAt(image.sprite, local);
        }
    }
}
