using System;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase
{
    // Виды рисунков существ (19.11.2026, ProjectDocs/CREATURE_VIEWS_ART_BRIEF.md):
    // те же ID, что у видов карты. «Обычный» — прежние поля существа, у
    // остальных видов свои слоты; пустой слот берёт рисунок «Обычного».
    // База существ не ссылается на сборку видов карты: выбранный вид
    // сообщает ActiveViewProvider (его ставит интерфейс боя).
    [Serializable]
    public sealed class UnitViewArtData
    {
        [SerializeField] private string viewId = string.Empty;
        [SerializeField] private Sprite portrait;
        [SerializeField] private Sprite battlefieldSprite;
        // Свой набор анимаций вида; пусто — набор «Обычного».
        [SerializeField] private string animationSetId = string.Empty;

        public UnitViewArtData()
        {
        }

        public UnitViewArtData(string viewId)
        {
            this.viewId = viewId ?? string.Empty;
        }

        public string ViewId => viewId ?? string.Empty;
        public Sprite Portrait => portrait;
        public Sprite BattlefieldSprite => battlefieldSprite;
        public string AnimationSetId => animationSetId ?? string.Empty;

        public bool IsEmpty => portrait == null && battlefieldSprite == null && string.IsNullOrEmpty(animationSetId);

        public void SetPortrait(Sprite value) => portrait = value;
        public void SetBattlefieldSprite(Sprite value) => battlefieldSprite = value;
        public void SetAnimationSetId(string value) => animationSetId = value ?? string.Empty;
    }

    public static class UnitArtViews
    {
        public const string ClassicId = "classic";
        public const string GloomId = "gloom";
        public const string SepiaId = "sepia";

        // Виды, для которых База существ показывает слоты (по порядку).
        public static readonly string[] Ids = { ClassicId, GloomId, SepiaId };

        // Выбранный вид игры. Не задан или упал — «Обычный».
        public static Func<string> ActiveViewProvider;

        public static string ActiveViewId
        {
            get
            {
                string id = null;
                try
                {
                    id = ActiveViewProvider?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Вид рисунков существ: " + exception.Message);
                }
                return string.IsNullOrEmpty(id) ? ClassicId : id;
            }
        }

        public static bool IsClassic(string viewId)
        {
            return string.IsNullOrEmpty(viewId) || string.Equals(viewId, ClassicId, StringComparison.Ordinal);
        }

        public static string Title(string viewId)
        {
            switch (viewId)
            {
                case GloomId: return "Мрачный";
                case SepiaId: return "Сепия";
                case ClassicId:
                case null:
                case "":
                    return "Обычный";
                default: return viewId;
            }
        }
    }
}
