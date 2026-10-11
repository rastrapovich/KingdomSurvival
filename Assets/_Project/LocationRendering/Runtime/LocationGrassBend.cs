using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Реакция растительности на проход — расчёт без объектов Unity: сила
    // по расстоянию, направление «от ног», движение отрезком, сложение
    // источников, сглаживание (или пружина при отпускании) и вес прогиба по
    // высоте растения. Один и тот же закон для ковра (вершины на CPU) и
    // объектов (шейдер с той же формулой веса — KsBendWeight в
    // LocationSpriteAdjust.shader). Необязательные параметры по умолчанию
    // дают исходный закон.
    //
    // Плоскость: точки рисунка места (пиксели, Y вниз) с разжатой вертикалью —
    // поле боя сжато по вертикали (BattlefieldFrame.VerticalScale), поэтому
    // круг на земле виден эллипсом; так же, как гексы.
    public static class LocationGrassBend
    {
        // Форма зоны касания одного слоя.
        public struct Zone
        {
            // Полная сила у самых ног — доля радиуса без спада.
            public float Core;
            // Степень спада: больше — дольше держит силу и резче спадает к краю.
            public float Softness;
            // Вытянутость зоны вглубь: вертикаль расстояния делится на неё.
            public float DepthStretch;
            // Зона сдвинута вперёд по ходу (доля радиуса).
            public float Lead;
            // Доля направления «по ходу» вместо «от ног».
            public float Forward;

            public static Zone Default => new Zone { Softness = 2, DepthStretch = 1 };
        }

        // Вес смещения точки растения: t — высота над опорой в долях высоты
        // растения. Ниже закреплённой части — 0, выше растёт к верху степенью
        // curve: 1 — прямой наклон, 2 — дуга (основание почти неподвижно).
        public static float Weight(float t, float pinned, float curve = 2)
        {
            pinned = Mathf.Clamp(pinned, 0, .95f);
            float s = Mathf.Clamp01((t - pinned) / (1 - pinned));
            return Mathf.Abs(curve - 2) < 1e-4f ? s * s : Mathf.Pow(s, Mathf.Max(.1f, curve));
        }

        // Спад силы: 1 у ног (и внутри ядра core — доли радиуса), 0 на краю
        // радиуса, без излома на краю.
        public static float Falloff(float distance, float radius, float core = 0, float softness = 2)
        {
            if (!(radius > 0) || !(distance < radius)) return 0;
            float x = Mathf.Max(0, distance) / radius;
            core = Mathf.Clamp(core, 0, .95f);
            if (x <= core) return 1;
            float u = (x - core) / (1 - core);
            float k = 1 - (Mathf.Abs(softness - 2) < 1e-4f ? u * u : Mathf.Pow(u, Mathf.Max(.1f, softness)));
            return k * k;
        }

        // Пиксели рисунка → плоскость земли.
        public static Vector2 ToGround(Vector2 pixel, float verticalScale) =>
            new Vector2(pixel.x, pixel.y / Mathf.Max(.01f, verticalScale));

        // Прогиб в плоскости земли → направление смещения верха в мире (Y вверх):
        // наклон «к зрителю / от зрителя» укорочен, как вертикаль поля, и
        // умножен на depthLean.
        public static Vector2 ToScreen(Vector2 groundBend, float verticalScale, float depthLean = 1) =>
            new Vector2(groundBend.x, -groundBend.y * Mathf.Max(.01f, verticalScale) * Mathf.Max(0, depthLean));

        public static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 ab = b - a;
            float length = ab.sqrMagnitude;
            if (length < 1e-8f) return a;
            return a + ab * Mathf.Clamp01(Vector2.Dot(point - a, ab) / length);
        }

        // Толчок одного источника: направление × сила (0..strength).
        // Ноги — ближайшая точка отрезка движения from → to за шаг: быстрый
        // шаг не перескакивает узкий пучок. У самых ног направление «от ног»
        // неустойчиво — к нему подмешивается направление движения, а без
        // движения берётся fallback (прежний наклон растения).
        public static Vector2 Push(Vector2 plant, Vector2 from, Vector2 to, float radius, float strength, Vector2 moveDirection, Vector2 fallback) =>
            Push(plant, from, to, radius, strength, moveDirection, fallback, Zone.Default);

        public static Vector2 Push(Vector2 plant, Vector2 from, Vector2 to, float radius, float strength, Vector2 moveDirection, Vector2 fallback, Zone zone)
        {
            Vector2 shift = moveDirection * (Mathf.Max(0, zone.Lead) * radius);
            Vector2 away = plant - ClosestOnSegment(from + shift, to + shift, plant);
            away.y /= Mathf.Max(.05f, zone.DepthStretch);
            float distance = away.magnitude;
            float force = Falloff(distance, radius, zone.Core, zone.Softness) * Mathf.Max(0, strength);
            if (force <= 0) return Vector2.zero;
            float near = radius * .25f;
            Vector2 direction = away + moveDirection * Mathf.Max(0, near - distance);
            float length = direction.magnitude;
            if (length > radius * 1e-3f) direction /= length;
            else direction = fallback.sqrMagnitude > 1e-8f ? fallback.normalized : Vector2.right;
            if (zone.Forward > 0 && moveDirection.sqrMagnitude > 1e-8f)
            {
                Vector2 mixed = Vector2.Lerp(direction, moveDirection.normalized, Mathf.Clamp01(zone.Forward));
                if (mixed.sqrMagnitude > 1e-6f) direction = mixed.normalized;
            }
            return direction * force;
        }

        // Сумма нескольких источников — не больше предела.
        public static Vector2 Limit(Vector2 bend, float limit = 1) =>
            bend.sqrMagnitude > limit * limit ? bend.normalized * limit : bend;

        // Сила контакта при скорости: стоящий держит мягкий прогиб, идущий
        // гнёт полностью. speed — доля скорости полного касания (0..1).
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

        // Возврат пружиной: затухающее колебание к цели с частотой frequency
        // (колебаний/с); amount 0 — без перехлёста (критическое затухание),
        // 1 — сильное покачивание. Мелкие подшаги — устойчиво при любом dt.
        public static void SpringStep(ref Vector2 current, ref Vector2 velocity, Vector2 target, float deltaTime, float frequency, float amount)
        {
            if (!(deltaTime > 0)) return;
            float omega = 2 * Mathf.PI * Mathf.Max(.05f, frequency);
            float damping = Mathf.Lerp(1, .08f, Mathf.Clamp01(amount));
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime * omega / .25f), 1, 64);
            float h = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                velocity += (-(omega * omega) * (current - target) - 2 * damping * omega * velocity) * h;
                current += velocity * h;
            }
        }

        // Смещение верха (единицы мира) и опускание вдоль оси растения при
        // прогибе bend растения высотой height над опорой. Опускание:
        // приминание плюс укорочение дуги (arcDrop) — верх, уходя вбок,
        // немного опускается, а не растягивает рисунок.
        public static void TopOffset(Vector2 bend, float height, float bendShare, float pressShare, float verticalScale,
            out Vector2 offset, out float drop, float depthLean = 1, float arcDrop = 1)
        {
            float amount = bend.magnitude;
            offset = ToScreen(bend, verticalScale, depthLean) * (bendShare * height);
            float side = bendShare * amount;
            drop = (pressShare * Mathf.Min(1, amount) + .5f * Mathf.Max(0, arcDrop) * side * side) * height;
        }
    }
}
