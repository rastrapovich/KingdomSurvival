using System;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase
{
    // Снаряд набора анимаций (стрела лучника): картинка и настройки полёта.
    // Пустая картинка — без снаряда, урон показывается в момент удара, как раньше.
    [Serializable]
    public sealed class CreatureAnimationProjectileData
    {
        public const float DefaultSpeedHexesPerSecond = 10f;
        public const float DefaultLengthHexes = 0.75f;
        public const float DefaultArcHeight = 0.08f;
        public const float DefaultHitHeightHexes = 0.8f;
        public static readonly Vector2 DefaultLaunchPoint = new Vector2(0.14f, 0.235f);

        // Картинка нарисована остриём вправо.
        [SerializeField] private Sprite sprite;
        [SerializeField] private float speedHexesPerSecond = DefaultSpeedHexesPerSecond;
        [SerializeField] private float lengthHexes = DefaultLengthHexes;
        [SerializeField] private float arcHeight = DefaultArcHeight;
        // Точка вылета: доли холста кадра от точки опоры; X — вперёд, Y — вверх.
        [SerializeField] private Vector2 launchPoint = DefaultLaunchPoint;
        [SerializeField] private float hitHeightHexes = DefaultHitHeightHexes;

        public Sprite Sprite => sprite;
        public bool HasSprite => sprite != null;

        public float SpeedHexesPerSecond =>
            float.IsNaN(speedHexesPerSecond) || speedHexesPerSecond <= 0f
                ? DefaultSpeedHexesPerSecond
                : Mathf.Clamp(speedHexesPerSecond, 1f, 60f);

        public float LengthHexes =>
            float.IsNaN(lengthHexes) || lengthHexes <= 0f
                ? DefaultLengthHexes
                : Mathf.Clamp(lengthHexes, 0.1f, 3f);

        public float ArcHeight =>
            float.IsNaN(arcHeight) ? DefaultArcHeight : Mathf.Clamp(arcHeight, 0f, 0.5f);

        public Vector2 LaunchPoint =>
            float.IsNaN(launchPoint.x) || float.IsNaN(launchPoint.y) ? DefaultLaunchPoint : launchPoint;

        public float HitHeightHexes =>
            float.IsNaN(hitHeightHexes) || hitHeightHexes < 0f
                ? DefaultHitHeightHexes
                : Mathf.Clamp(hitHeightHexes, 0f, 3f);

        public void SetSprite(Sprite value) { sprite = value; }
        public void SetSpeedHexesPerSecond(float value) { speedHexesPerSecond = value <= 0f || float.IsNaN(value) ? DefaultSpeedHexesPerSecond : Mathf.Clamp(value, 1f, 60f); }
        public void SetLengthHexes(float value) { lengthHexes = value <= 0f || float.IsNaN(value) ? DefaultLengthHexes : Mathf.Clamp(value, 0.1f, 3f); }
        public void SetArcHeight(float value) { arcHeight = float.IsNaN(value) ? DefaultArcHeight : Mathf.Clamp(value, 0f, 0.5f); }
        public void SetLaunchPoint(Vector2 value) { launchPoint = float.IsNaN(value.x) || float.IsNaN(value.y) ? DefaultLaunchPoint : value; }
        public void SetHitHeightHexes(float value) { hitHeightHexes = value < 0f || float.IsNaN(value) ? DefaultHitHeightHexes : Mathf.Clamp(value, 0f, 3f); }
    }

    // Геометрия полёта снаряда. Координаты — поля UI Toolkit: Y растёт вниз,
    // «вверх» — это -Y.
    public static class CreatureAnimationProjectileMath
    {
        private const float MinFlightSeconds = 0.05f;

        // Где стрела появляется в момент выпуска. ground — центр гекса стрелка,
        // figureSize — показанный размер холста кадра, targetGround — центр гекса цели.
        public static Vector2 LaunchPosition(Vector2 ground, Vector2 figureSize, Vector2 targetGround,
            CreatureAnimationProjectileData data, float fallbackForwardSign)
        {
            Vector2 launch = data != null ? data.LaunchPoint : CreatureAnimationProjectileData.DefaultLaunchPoint;
            float dx = targetGround.x - ground.x;
            float forward = Mathf.Abs(dx) < 0.001f
                ? (fallbackForwardSign < 0f ? -1f : 1f)
                : Mathf.Sign(dx);
            return new Vector2(
                ground.x + forward * launch.x * figureSize.x,
                ground.y - launch.y * figureSize.y);
        }

        public static Vector2 HitPosition(Vector2 targetGround, float hexSize, CreatureAnimationProjectileData data)
        {
            float height = data != null ? data.HitHeightHexes : CreatureAnimationProjectileData.DefaultHitHeightHexes;
            return new Vector2(targetGround.x, targetGround.y - height * hexSize);
        }

        public static float FlightSeconds(Vector2 from, Vector2 to, float hexStepPx, CreatureAnimationProjectileData data)
        {
            float step = hexStepPx > 0f ? hexStepPx : 1f;
            float speed = data != null ? data.SpeedHexesPerSecond : CreatureAnimationProjectileData.DefaultSpeedHexesPerSecond;
            return Mathf.Max(MinFlightSeconds, (to - from).magnitude / step / speed);
        }

        // Позиция на дуге при t в 0..1. angleDegrees — угол касательной в экранных
        // координатах: 0 — вправо, положительный — по часовой (вниз).
        public static Vector2 Evaluate(Vector2 from, Vector2 to, float arcHeight, float t, out float angleDegrees)
        {
            t = Mathf.Clamp01(t);
            float arc = Mathf.Clamp(float.IsNaN(arcHeight) ? 0f : arcHeight, 0f, 0.5f);
            Vector2 delta = to - from;
            float lift = 4f * arc * delta.magnitude;
            Vector2 position = Vector2.Lerp(from, to, t) + new Vector2(0f, -lift * t * (1f - t));
            // Производная по t: delta + (0, -lift * (1 - 2t)).
            Vector2 tangent = delta + new Vector2(0f, -lift * (1f - 2f * t));
            angleDegrees = tangent.sqrMagnitude < 1e-8f
                ? 0f
                : Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
            return position;
        }
    }
}
