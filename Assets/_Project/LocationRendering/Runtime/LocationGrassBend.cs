using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Реакция растительности на проход — расчёт без объектов Unity: сила
    // по расстоянию, направление «от ног», движение отрезком, сложение
    // источников, сглаживание и вес прогиба по высоте растения. Один и тот
    // же закон для ковра (вершины на CPU) и объектов (шейдер с той же
    // формулой веса — KsBendWeight в LocationSpriteAdjust.shader).
    //
    // Плоскость: точки рисунка места (пиксели, Y вниз) с разжатой вертикалью —
    // поле боя сжато по вертикали (BattlefieldFrame.VerticalScale), поэтому
    // круг на земле виден эллипсом; так же, как гексы.
    public static class LocationGrassBend
    {
        // Вес смещения точки растения: t — высота над опорой в долях высоты
        // растения. Ниже закреплённой части — 0, выше — плавно растёт к верху
        // (квадратично: основание почти неподвижно, верх гнётся сильнее середины).
        public static float Weight(float t, float pinned)
        {
            pinned = Mathf.Clamp(pinned, 0, .95f);
            float s = Mathf.Clamp01((t - pinned) / (1 - pinned));
            return s * s;
        }

        // Спад силы: 1 у ног, 0 на краю радиуса, без излома на краю.
        public static float Falloff(float distance, float radius)
        {
            if (!(radius > 0) || !(distance < radius)) return 0;
            float x = Mathf.Max(0, distance) / radius;
            float k = 1 - x * x;
            return k * k;
        }

        // Пиксели рисунка → плоскость земли и обратно (для смещения на экране).
        public static Vector2 ToGround(Vector2 pixel, float verticalScale) =>
            new Vector2(pixel.x, pixel.y / Mathf.Max(.01f, verticalScale));

        // Прогиб в плоскости земли → направление смещения верха в мире (Y вверх):
        // наклон «к зрителю / от зрителя» укорочен, как вертикаль поля.
        public static Vector2 ToScreen(Vector2 groundBend, float verticalScale) =>
            new Vector2(groundBend.x, -groundBend.y * Mathf.Max(.01f, verticalScale));

        public static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 ab = b - a;
            float length = ab.sqrMagnitude;
            if (length < 1e-8f) return a;
            return a + ab * Mathf.Clamp01(Vector2.Dot(point - a, ab) / length);
        }

        // Толчок одного источника: направление «от ног» × сила (0..strength).
        // Ноги — ближайшая точка отрезка движения from → to за шаг: быстрый
        // шаг не перескакивает узкий пучок. У самых ног направление «от ног»
        // неустойчиво — к нему подмешивается направление движения, а без
        // движения берётся fallback (прежний наклон растения).
        public static Vector2 Push(Vector2 plant, Vector2 from, Vector2 to, float radius, float strength, Vector2 moveDirection, Vector2 fallback)
        {
            Vector2 away = plant - ClosestOnSegment(from, to, plant);
            float distance = away.magnitude;
            float force = Falloff(distance, radius) * Mathf.Max(0, strength);
            if (force <= 0) return Vector2.zero;
            float near = radius * .25f;
            Vector2 direction = away + moveDirection * Mathf.Max(0, near - distance);
            float length = direction.magnitude;
            if (length > radius * 1e-3f) direction /= length;
            else direction = fallback.sqrMagnitude > 1e-8f ? fallback.normalized : Vector2.right;
            return direction * force;
        }

        // Сумма нескольких источников — не больше полного прогиба.
        public static Vector2 Limit(Vector2 bend) => bend.sqrMagnitude > 1 ? bend.normalized : bend;

        // Сила контакта при скорости: стоящий держит мягкий прогиб, идущий
        // гнёт полностью. speed — доля обычного шага (0..1).
        public static float Strength(float speedInfluence, float speed) =>
            1 - Mathf.Clamp01(speedInfluence) * (1 - Mathf.Clamp01(speed));

        // Шаг к цели, не зависящий от частоты кадров: уступает за reactTime,
        // возвращается за recoverTime (≈95 % пути за указанное время).
        // Новое воздействие продолжает от текущего состояния — без скачка.
        public static Vector2 Approach(Vector2 current, Vector2 target, float deltaTime, float reactTime, float recoverTime)
        {
            if (!(deltaTime > 0)) return current;
            float time = target.sqrMagnitude > current.sqrMagnitude ? reactTime : recoverTime;
            float k = 1 - Mathf.Exp(-3 * deltaTime / Mathf.Max(1e-3f, time));
            return current + (target - current) * k;
        }

        // Смещение верха (единицы мира) и опускание вдоль оси растения при
        // прогибе bend (|bend| ≤ 1) растения высотой height над опорой.
        // Опускание: приминание плюс укорочение дуги — верх, уходя вбок,
        // немного опускается, а не растягивает рисунок.
        public static void TopOffset(Vector2 bend, float height, float bendShare, float pressShare, float verticalScale,
            out Vector2 offset, out float drop)
        {
            float amount = Mathf.Min(1, bend.magnitude);
            offset = ToScreen(bend, verticalScale) * (bendShare * height);
            float side = bendShare * amount;
            drop = (pressShare * amount + .5f * side * side) * height;
        }
    }
}
