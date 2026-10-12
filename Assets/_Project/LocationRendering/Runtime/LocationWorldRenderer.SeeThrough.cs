using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Просвечивание высоких предметов (галочка ассета «Просвечивает, когда
    // за ним человек»): пока фигура стоит за предметом (рисуется раньше него)
    // и непрозрачная часть его рисунка закрывает фигуру, все рисунки предмета
    // плавно уходят к заданной непрозрачности; фигура вышла — плотнеют
    // обратно. Тень, свет и окклюзия не меняются.
    public sealed partial class LocationWorldRenderer
    {
        // За сколько секунд предмет полностью проявляется или растворяется.
        public const float SeeThroughFadeSeconds = .25f;

        // Точки фигуры, по которым проверяется закрытие: середина, грудь,
        // голова и бока (доли рамки рисунка фигуры, от низа).
        private static readonly Vector2[] ActorProbes =
        {
            new Vector2(.5f, .5f), new Vector2(.5f, .7f), new Vector2(.5f, .88f), new Vector2(.5f, .3f),
            new Vector2(.3f, .55f), new Vector2(.7f, .55f)
        };

        private float seeThroughSeconds = float.NaN;

        private void UpdateSeeThrough()
        {
            float delta = float.IsNaN(seeThroughSeconds) ? float.PositiveInfinity : Mathf.Max(0, Seconds - seeThroughSeconds);
            seeThroughSeconds = Seconds;
            float step = delta / SeeThroughFadeSeconds;
            foreach (Placed item in placed)
            {
                if (item.Resolved == null || !item.Resolved.SeeThrough || item.Images.Count == 0) continue;
                float target = HidesActor(item) ? Mathf.Clamp01(item.Resolved.SeeThroughAlpha) : 1;
                if (Mathf.Approximately(item.SeeThroughLevel, target)) continue;
                item.SeeThroughLevel = Mathf.MoveTowards(item.SeeThroughLevel, target, float.IsInfinity(step) ? 1 : step);
                for (int i = 0; i < item.Images.Count; i++)
                {
                    SpriteRenderer image = item.Images[i];
                    if (image == null) continue;
                    Color color = image.color;
                    color.a = (i < item.BaseAlpha.Count ? item.BaseAlpha[i] : 1) * item.SeeThroughLevel;
                    image.color = color;
                }
            }
        }

        // Непрозрачность рисунков предмета сейчас (1 — не просвечивает).
        public float ObjectSeeThroughLevel(string objectId) => placed.Find(entry => entry.Data.Id == objectId)?.SeeThroughLevel ?? 1;

        private bool HidesActor(Placed item)
        {
            foreach (Actor actor in actors.Values)
            {
                SpriteRenderer figure = actor.Image;
                if (figure == null || !figure.enabled || figure.sprite == null) continue;
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
