using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Уровень качества обработки кадра: высокий — всё; средний — живописность
    // меньшим радиусом, без тилт-шифта и марева; низкий — ещё без
    // живописности и лучей, частицы реже, сглаживание не дороже FXAA.
    public enum LocationFrameQualityLevel { High, Medium, Low }

    // Рабочие значения обработки кадра в этот кадр — для прохода «Холста».
    public struct LocationFrameRuntime
    {
        public float Seconds, DayFactor;
        public bool Outdoor;
        // Направление теней солнца на экране и ветер рисунков (мир).
        public Vector2 Sun, Wind;
        // Огни (свет с мерцанием) в мире и доля их силы (днём слабее).
        public List<Vector2> Fires;
        public float FireShare;
        // 0..1: красные края (уже по порогу), оглушение, вспышка, затемнение, кинополосы (доля высоты).
        public float LowHealth, Stun, Flash, Fade, Letterbox;
    }

    // ПР-12М: состояния героя на экране и события кадра, которые задаёт
    // игра: здоровье (красные пульсирующие края ниже порога), оглушение
    // (темнее, бледнее, размыто), затемнение в цвет, кинополосы сцен, а
    // также вспышка и тряска камеры при ударе. Вид — раздел «Игра» и блок
    // «Удар в бою»; здесь — сила в данный момент.
    public sealed partial class LocationWorldRenderer
    {
        // Общий для всех мест уровень качества (настройки игры; окно — для проверки).
        public static LocationFrameQualityLevel QualityLevel = LocationFrameQualityLevel.High;

        private float heroHealth = 1;
        private float stunValue, stunTarget, stunSpeed = 4;
        private float fadeFrom, fadeGoal, fadeStart, fadeDuration;
        private float letterboxFrom, letterboxGoal, letterboxStart, letterboxDuration;
        private float flashAt = float.NegativeInfinity, flashStrength;
        private float shakeAt = float.NegativeInfinity, shakeStrength;
        private float lastStateSeconds = float.NaN;
        private readonly List<Vector2> fireBuffer = new List<Vector2>();
        private bool shaking;

        // Доля здоровья героя 0..1 (1 — здоров): ниже порога — красные края.
        public void SetHeroHealth(float share) => heroHealth = Mathf.Clamp01(share);
        public float HeroHealth => heroHealth;

        // Оглушение 0..1, плавно за seconds.
        public void SetStun(float amount, float seconds = .25f)
        {
            stunTarget = Mathf.Clamp01(amount);
            stunSpeed = seconds <= 0 ? 1000 : 1 / seconds;
        }

        // Затемнение в цвет раздела «Игра» (0 — нет, 1 — полностью) за seconds.
        public void FadeTo(float amount, float seconds)
        {
            fadeFrom = FadeAmount;
            fadeGoal = Mathf.Clamp01(amount);
            fadeStart = Seconds;
            fadeDuration = Mathf.Max(0, seconds);
        }

        // Кинополосы сцены (доля высоты сверху и снизу) за seconds; поверх постоянных.
        public void SetLetterbox(float amount, float seconds)
        {
            letterboxFrom = LetterboxRuntime;
            letterboxGoal = Mathf.Clamp(amount, 0, .45f);
            letterboxStart = Seconds;
            letterboxDuration = Mathf.Max(0, seconds);
        }

        private static float Ease(float from, float to, float start, float duration, float now)
        {
            float t = duration <= 0 ? 1 : Mathf.Clamp01((now - start) / duration);
            return Mathf.Lerp(from, to, t * t * (3 - 2 * t));
        }

        public float FadeAmount => Ease(fadeFrom, fadeGoal, fadeStart, fadeDuration, Seconds);
        private float LetterboxRuntime => Ease(letterboxFrom, letterboxGoal, letterboxStart, letterboxDuration, Seconds);
        public float StunAmount => stunValue;

        // Вспышка и тряска при ударе (BattleHit): сила — та же, что у всплеска линзы.
        private void HitFlashAndShake(float strength)
        {
            LocationLensEffects lens = Post.Lens;
            if (lens.PulseFlash) { flashAt = Seconds; flashStrength = strength; }
            if (lens.PulseShake) { shakeAt = Seconds; shakeStrength = strength; }
        }

        private float FlashNow()
        {
            LocationLensEffects lens = Post.Lens;
            float t = (Seconds - flashAt) / Mathf.Max(.02f, lens.PulseDuration * .6f);
            if (t < 0 || t >= 1) return 0;
            return Mathf.Clamp01(lens.PulseFlashStrength * flashStrength * (1 - t) * (1 - t));
        }

        // Сдвиг камеры от тряски сейчас (мир): окна предпросмотра добавляют его к своей камере.
        public Vector2 ShakeOffset
        {
            get
            {
                if (!PostActive) return Vector2.zero;
                LocationLensEffects lens = Post.Lens;
                float duration = Mathf.Max(.02f, lens.PulseDuration);
                float t = (Seconds - shakeAt) / duration;
                if (t < 0 || t >= 1) return Vector2.zero;
                float amplitude = lens.PulseShakeAmount * shakeStrength * (1 - t) * (1 - t) * Camera.orthographicSize * 2;
                float s = Seconds * 47;
                return new Vector2(Mathf.Sin(s) + Mathf.Sin(s * 1.7f) * .5f, Mathf.Cos(s * 1.3f) + Mathf.Sin(s * 2.3f) * .4f) * amplitude * .6f;
            }
        }

        // Оглушение идёт к цели по часам рендерера; камера трясётся поверх вида.
        private void UpdateStates()
        {
            float step = float.IsNaN(lastStateSeconds) ? 0 : Mathf.Clamp(Seconds - lastStateSeconds, 0, .5f);
            lastStateSeconds = Seconds;
            stunValue = Mathf.MoveTowards(stunValue, stunTarget, step * stunSpeed);
            Vector2 shake = ShakeOffset;
            if (shake == Vector2.zero && !shaking) return;
            shaking = shake != Vector2.zero;
            Vector2 world = LocationVisualGeometry.PixelToWorld(Location, ViewCenter) + shake;
            Camera.transform.localPosition = new Vector3(world.x, world.y, -10);
        }

        public LocationFrameRuntime FrameRuntime()
        {
            LocationPostEffects post = Post;
            LocationGameEffects game = post.Game;
            fireBuffer.Clear();
            foreach (Vector2 fire in FireSources()) fireBuffer.Add(fire);
            float threshold = Mathf.Max(.01f, game.LowHealthThreshold);
            LocationSpriteEffects sprites = post.Sprites;
            return new LocationFrameRuntime
            {
                Seconds = Seconds,
                DayFactor = DayFactor,
                Outdoor = !Indoor,
                Sun = SunShadowVector,
                Wind = sprites.Wind ? new Vector2(Mathf.Clamp(sprites.WindDirection, -1, 1), 0) : Vector2.zero,
                Fires = fireBuffer,
                FireShare = Indoor ? 1 : Mathf.Lerp(1, .5f, DayFactor),
                LowHealth = Mathf.Clamp01((threshold - heroHealth) / threshold),
                Stun = stunValue,
                Flash = FlashNow(),
                Fade = FadeAmount,
                Letterbox = Mathf.Max(Mathf.Clamp(game.Letterbox, 0, .45f), LetterboxRuntime)
            };
        }
    }
}
