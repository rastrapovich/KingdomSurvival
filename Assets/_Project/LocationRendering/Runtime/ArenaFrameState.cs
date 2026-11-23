using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Обработка кадра генерируемой арены боя (21.11.2026): то, что у мира
    // места считает LocationWorldRenderer, — часы эффектов, удар (всплеск
    // линзы, вспышка, тряска), здоровье вождя (красные края), полосы и
    // затемнение. Источник прохода «Холста» для камеры арены. Арена — день и
    // улица; огни — клетки боя с огнём (марево над ними). Ветер рисунков качает деревья в шейдере карточек
    // (MapLabArena), здесь он ведёт тени облаков и требует перерисовки кадра.
    public sealed class ArenaFrameState : ILocationCanvasSource
    {
        private readonly float started = Time.realtimeSinceStartup;
        private readonly List<Vector2> fires = new List<Vector2>();
        private float pulseAt = float.NegativeInfinity, pulseStrength;
        private float flashAt = float.NegativeInfinity, flashStrength;
        private float shakeAt = float.NegativeInfinity, shakeStrength;

        // Обработка кадра арены сейчас (по виду карты); null — нет.
        public Func<LocationPostEffects> PostSource;
        // Доля здоровья вождя 0..1 (1 — здоров).
        public Func<float> LeaderHealth;
        // Огни боя (клетки с огнём) точками кадра 0..1, Y вверх — для марева; null — огней нет.
        public Action<List<Vector2>> CollectFires;
        // Единиц мира арены на единицу мира мест (размер марева).
        public Func<float> UnitScale;
        public float Fade, Letterbox;
        // Оглушение 0..1 (размытие, тусклее, темнее — раздел «Игра»); задаёт игра, как у места.
        public float Stun;

        public float Seconds => Time.realtimeSinceStartup - started + 1f;

        public LocationPostEffects Post
        {
            get
            {
                LocationPostEffects post = PostSource?.Invoke();
                return post != null && post.Enabled ? post.Repair() : null;
            }
        }

        public LocationPostEffects CanvasPost => Post;

        // Удар в бою: сила по блоку «Удар в бою» (0 — не было).
        public float BattleHit(bool targetOurs, bool defeated)
        {
            LocationPostEffects post = Post;
            if (post == null) return 0f;
            float strength = post.Lens.HitPulse(targetOurs, defeated);
            if (strength > 0f) Hit(strength);
            return strength;
        }

        // Всплеск линзы, вспышка и тряска (по галочкам блока «Удар в бою»).
        public void Hit(float strength)
        {
            LocationPostEffects post = Post;
            float now = Seconds;
            pulseAt = now;
            pulseStrength = Mathf.Max(0f, strength);
            if (post == null) return;
            if (post.Lens.PulseFlash) { flashAt = now; flashStrength = strength; }
            if (post.Lens.PulseShake) { shakeAt = now; shakeStrength = strength; }
        }

        // Сила всплеска линзы сейчас (0..), для аберрации и искажения.
        public float LensPulse
        {
            get
            {
                LocationPostEffects post = Post;
                if (post == null) return 0f;
                float t = (Seconds - pulseAt) / Mathf.Max(.02f, post.Lens.PulseDuration);
                return t < 0f || t >= 1f ? 0f : pulseStrength * (1f - t) * (1f - t);
            }
        }

        private float FlashNow(LocationPostEffects post)
        {
            float t = (Seconds - flashAt) / Mathf.Max(.02f, post.Lens.PulseDuration * .6f);
            return t < 0f || t >= 1f ? 0f : Mathf.Clamp01(post.Lens.PulseFlashStrength * flashStrength * (1f - t) * (1f - t));
        }

        // Тряска в долях высоты кадра.
        public Vector2 ShakeShare
        {
            get
            {
                LocationPostEffects post = Post;
                if (post == null) return Vector2.zero;
                float t = (Seconds - shakeAt) / Mathf.Max(.02f, post.Lens.PulseDuration);
                if (t < 0f || t >= 1f) return Vector2.zero;
                float amplitude = post.Lens.PulseShakeAmount * shakeStrength * (1f - t) * (1f - t) * 2f * .6f;
                float s = Seconds * 47f;
                return new Vector2(Mathf.Sin(s) + Mathf.Sin(s * 1.7f) * .5f, Mathf.Cos(s * 1.3f) + Mathf.Sin(s * 2.3f) * .4f) * amplitude;
            }
        }

        public LocationFrameRuntime FrameRuntime()
        {
            LocationPostEffects post = Post ?? new LocationPostEffects();
            LocationGameEffects game = post.Game;
            LocationSpriteEffects sprites = post.Sprites;
            float threshold = Mathf.Max(.01f, game.LowHealthThreshold);
            float health = Mathf.Clamp01(LeaderHealth?.Invoke() ?? 1f);
            return new LocationFrameRuntime
            {
                Seconds = Seconds,
                DayFactor = 1f,
                Outdoor = true,
                Sun = new Vector2(.6f, -.35f),
                // Ветер раздела «Рисунки» ведёт тени облаков (как в мире места).
                Wind = sprites != null && sprites.Wind ? new Vector2(Mathf.Clamp(sprites.WindDirection, -1, 1), 0) : Vector2.zero,
                Fires = CollectedFires(),
                FiresInViewport = true,
                UnitScale = UnitScale?.Invoke() ?? 1f,
                FireShare = .5f,
                LowHealth = Mathf.Clamp01((threshold - health) / threshold),
                Stun = Mathf.Clamp01(Stun),
                Flash = FlashNow(post),
                Fade = Mathf.Clamp01(Fade),
                Letterbox = Mathf.Max(Mathf.Clamp(game.Letterbox, 0f, .45f), Mathf.Clamp(Letterbox, 0f, .45f)),
            };
        }

        private List<Vector2> CollectedFires()
        {
            fires.Clear();
            CollectFires?.Invoke(fires);
            return fires;
        }

        // Кадр меняется со временем: перерисовывать каждый кадр, а не по запросу.
        public bool Animating
        {
            get
            {
                LocationPostEffects post = Post;
                if (post == null) return false;
                LocationFrameRuntime state = FrameRuntime();
                LocationCanvasEffects canvas = post.Canvas;
                return state.Flash > .001f || state.LowHealth > .001f || LensPulse > .001f || ShakeShare != Vector2.zero || post.Grain > .001f ||
                       canvas.Film || canvas.Fog && canvas.FogSpeed > 0f || post.Air.Clouds || post.Air.Rays ||
                       post.Sprites != null && post.Sprites.Wind && post.Sprites.WindStrength > 0f ||
                       post.Air.HeatHaze && CollectedFires().Count > 0;
            }
        }

        // Подкраска фигуры доски по разделу «Персонажи»: подкраска × яркость.
        public Color UnitTint(string unitTypeId)
        {
            LocationPostEffects post = Post;
            LocationCreatureLook look = post?.Characters?.Resolve(unitTypeId);
            if (look == null) return Color.white;
            Color tint = look.Tint * Mathf.Max(0f, look.Brightness);
            tint.a = 1f;
            return tint;
        }
    }
}
