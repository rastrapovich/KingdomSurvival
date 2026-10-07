using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12О: камера места следует за командиром — в пикселях рисунка места
    // (Y вниз), той же системой координат, что земля, клики и высота.
    // Ограничивается весь видимый прямоугольник, а не только центр; если
    // место меньше кадра по оси — кадр по этой оси по центру (без скрытого
    // приближения). Сглаживание — SmoothDamp с явным временем кадра.
    // Переход через границу участка ничего не сбрасывает: участки — часть
    // одной карты.
    public sealed class LocationCameraFollow
    {
        public Vector2 Center { get; private set; }
        public bool HasCenter { get; private set; }
        private Vector2 velocity;

        // Новая карта или новая цель: следующий шаг ставит кадр сразу.
        public void Reset()
        {
            HasCenter = false;
            velocity = Vector2.zero;
        }

        // После временной камеры (бой, сцена): продолжать от её кадра.
        public void Continue(Vector2 center)
        {
            Center = center;
            HasCenter = true;
            velocity = Vector2.zero;
        }

        // leader — логическая опора ног командира (пиксели рисунка);
        // view — размер кадра (пиксели рисунка); snap — появление/телепорт.
        public Vector2 Step(Vector2 leader, Vector2 view, Rect bounds, LocationCameraSettings settings, float deltaTime, bool snap = false)
        {
            settings = settings ?? new LocationCameraSettings();
            Vector2 goal = leader + settings.Offset;
            if (!settings.FollowCommander)
            {
                // Без следования: кадр стоит, при первом показе — на командире.
                Center = ClampView(HasCenter && !snap ? Center : goal, view, bounds);
                HasCenter = true;
                velocity = Vector2.zero;
                return Center;
            }
            bool teleport = HasCenter && settings.SnapDistance > 0 && (goal - Center).magnitude > settings.SnapDistance * view.y;
            if (!HasCenter || snap || teleport)
            {
                Center = ClampView(goal, view, bounds);
                HasCenter = true;
                velocity = Vector2.zero;
                return Center;
            }

            Vector2 zone = Vector2.Scale(view / 2, new Vector2(Mathf.Clamp01(settings.DeadZone.x), Mathf.Clamp01(settings.DeadZone.y)));
            Vector2 desired = Center;
            desired.x = goal.x > Center.x + zone.x ? goal.x - zone.x : goal.x < Center.x - zone.x ? goal.x + zone.x : Center.x;
            desired.y = goal.y > Center.y + zone.y ? goal.y - zone.y : goal.y < Center.y - zone.y ? goal.y + zone.y : Center.y;
            // Цель ограничена заранее: у края кадр останавливается, а не
            // упирается со скоростью; сглаживание не выносит кадр наружу.
            desired = ClampView(desired, view, bounds);
            float dt = Mathf.Clamp(deltaTime, 0, .1f);
            if (settings.SmoothTime <= 0.0001f)
            {
                Center = desired;
                velocity = Vector2.zero;
            }
            else if (dt > 0)
            {
                float x = Mathf.SmoothDamp(Center.x, desired.x, ref velocity.x, settings.SmoothTime, Mathf.Infinity, dt);
                float y = Mathf.SmoothDamp(Center.y, desired.y, ref velocity.y, settings.SmoothTime, Mathf.Infinity, dt);
                Center = new Vector2(x, y);
                // Остановка без дрожания: в пределах сотой пикселя — ровно цель.
                if ((Center - desired).sqrMagnitude < 1e-4f && velocity.sqrMagnitude < 1e-2f)
                {
                    Center = desired;
                    velocity = Vector2.zero;
                }
            }
            Center = ClampView(Center, view, bounds);
            return Center;
        }

        // Центр кадра, при котором кадр view не выходит за bounds.
        public static Vector2 ClampView(Vector2 center, Vector2 view, Rect bounds)
        {
            float halfW = view.x / 2, halfH = view.y / 2;
            float x = bounds.width <= view.x ? bounds.center.x : Mathf.Clamp(center.x, bounds.xMin + halfW, bounds.xMax - halfW);
            float y = bounds.height <= view.y ? bounds.center.y : Mathf.Clamp(center.y, bounds.yMin + halfH, bounds.yMax - halfH);
            return new Vector2(x, y);
        }

        // Рамка камеры места (пиксели рисунка, Y вниз).
        public static Rect ResolveBounds(LocationVisualDefinition visual, LocalLocationDefinition location)
        {
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            Rect whole = new Rect(Vector2.zero, canvas);
            LocationCameraSettings settings = visual?.Camera;
            if (settings == null) return whole;
            Rect result = whole;
            if (settings.Bounds == LocationCameraBounds.UsefulArea && visual.Ground != null && visual.Ground.IsTiled &&
                visual.Ground.UsefulPixels.width > 0 && visual.Ground.UsefulPixels.height > 0)
                result = LocationGroundGrid.For(visual.Ground, location).VirtualRectToCanvas(visual.Ground.UsefulPixels);
            else if (settings.Bounds == LocationCameraBounds.Custom && settings.CustomBounds.width > 0 && settings.CustomBounds.height > 0)
                result = new Rect(Vector2.Scale(settings.CustomBounds.position, canvas), Vector2.Scale(settings.CustomBounds.size, canvas));
            // Пересечение с рисунком: за краем места камере смотреть не на что.
            Rect clipped = Rect.MinMaxRect(Mathf.Max(whole.xMin, result.xMin), Mathf.Max(whole.yMin, result.yMin),
                Mathf.Min(whole.xMax, result.xMax), Mathf.Min(whole.yMax, result.yMax));
            return clipped.width > 1 && clipped.height > 1 ? clipped : whole;
        }

        // Видимая высота рисунка: своя у места или прежняя (до 1080 пикселей).
        public static float DefaultViewHeight(LocationVisualDefinition visual, LocalLocationDefinition location)
        {
            float canvas = LocationVisualGeometry.CanvasSize(location).y;
            float own = visual?.Camera?.ViewHeight ?? 0;
            return own > 1 ? own : Mathf.Min(canvas, 1080f);
        }
    }
}
