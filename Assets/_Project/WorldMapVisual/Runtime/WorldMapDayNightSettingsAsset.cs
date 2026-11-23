using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Смена дня и ночи на глобальной карте (бриф 19.11.2026): границы фаз и
    // свет местности. Время берётся из существующих часов мира; ассет задаёт
    // только показ. Все значения — рабочие настройки, не канон.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapDayNight",
        menuName = "Kingdom Survival/Карта/Day-Night Settings")]
    public sealed class WorldMapDayNightSettingsAsset : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapDayNight";

        [Header("Освещение карты")]
        [Tooltip("Менять свет карты по времени суток. Выключено — карта всегда дневная (круг суток остаётся).")]
        [SerializeField] private bool lightingEnabled = true;
        [Tooltip("Общая сила тонирования: 0 — без изменений, 1 — полные цвета ниже.")]
        [SerializeField, Range(0f, 1f)] private float strength = 1f;

        [Header("Границы фаз (игровые часы)")]
        [SerializeField, Range(0f, 12f)] private float dawnStartHour = 5f;
        [SerializeField, Range(0f, 12f)] private float dayStartHour = 7f;
        [SerializeField, Range(12f, 24f)] private float duskStartHour = 17f;
        [SerializeField, Range(12f, 24f)] private float nightStartHour = 19f;
        [Tooltip("Ширина плавного перехода света вокруг середины рассвета и заката (часы).")]
        [SerializeField, Range(0.5f, 8f)] private float transitionHours = 3f;

        [Header("Цвета (множитель рисунка местности)")]
        [Tooltip("Рассвет: слабая тёплая подсветка.")]
        [SerializeField] private Color dawnTint = new Color(1.00f, 0.93f, 0.82f);
        [Tooltip("Закат: охристые приглушённые цвета.")]
        [SerializeField] private Color duskTint = new Color(0.97f, 0.84f, 0.66f);
        [Tooltip("Ночь: холодный сине-серый и умеренно темнее. Не делать почти чёрным — дороги, реки и здания должны читаться.")]
        [SerializeField] private Color nightTint = new Color(0.30f, 0.36f, 0.58f);

        private static WorldMapDayNightSettingsAsset cached;
        private static bool loaded;

        public DayNightSettings ToSettings()
        {
            return new DayNightSettings
            {
                Enabled = lightingEnabled,
                Strength = strength,
                DawnStartHour = dawnStartHour,
                DayStartHour = Mathf.Max(dawnStartHour, dayStartHour),
                DuskStartHour = duskStartHour,
                NightStartHour = Mathf.Max(duskStartHour, nightStartHour),
                TransitionHours = transitionHours,
                DawnTint = ToLight(dawnTint),
                DuskTint = ToLight(duskTint),
                NightTint = ToLight(nightTint)
            };
        }

        // Ассет из Resources в ядро; без ассета — значения по умолчанию.
        public static void ApplyToCore()
        {
            if (!loaded)
            {
                cached = Resources.Load<WorldMapDayNightSettingsAsset>(ResourcesPath);
                loaded = true;
            }
            DayNightCycle.Current = cached != null ? cached.ToSettings() : DayNightSettings.Default;
        }

        private void OnValidate()
        {
            // Правка в инспекторе во время Play Mode подхватывается сразу.
            if (ReferenceEquals(this, cached))
                DayNightCycle.Current = ToSettings();
        }

        private static DayNightLight ToLight(Color c) => new DayNightLight { R = c.r, G = c.g, B = c.b };
    }
}
