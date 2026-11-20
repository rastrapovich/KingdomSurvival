using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattlefieldDatabase
{
    // Обработка кадра по видам карты («Обычный / Мрачный / Сепия»), 21.11.2026,
    // просьба пользователя: одно правило для мест и генерируемых арен —
    // общая обработка действует во всех видах, у вида может быть своя.
    [Serializable]
    public sealed class LocationViewFrame
    {
        public string ViewId = string.Empty;
        // Своя обработка у вида (иначе — общая).
        public bool Own;
        public LocationPostEffects Post = new LocationPostEffects();
    }

    public static class LocationFrameViews
    {
        public const string ClassicId = "classic", GloomId = "gloom", SepiaId = "sepia";
        public static readonly string[] Known = { ClassicId, GloomId, SepiaId };

        // Текущий вид карты (задаёт модуль видов: MapLabBridge). Без него — «Обычный».
        public static Func<string> ActiveViewProvider;

        public static string ActiveViewId
        {
            get
            {
                string id = ActiveViewProvider?.Invoke();
                return string.IsNullOrEmpty(id) ? ClassicId : id;
            }
        }

        // Своя у вида — если включена; иначе общая.
        public static LocationPostEffects Pick(List<LocationViewFrame> views, LocationPostEffects common, string viewId)
        {
            if (views == null || string.IsNullOrEmpty(viewId)) return common;
            LocationViewFrame view = views.Find(item => item != null && item.ViewId == viewId);
            return view != null && view.Own && view.Post != null ? view.Post : common;
        }

        // Запись вида (создаётся по требованию).
        public static LocationViewFrame Get(List<LocationViewFrame> views, string viewId)
        {
            if (views == null || string.IsNullOrEmpty(viewId)) return null;
            LocationViewFrame found = views.Find(item => item != null && item.ViewId == viewId);
            if (found == null)
            {
                found = new LocationViewFrame { ViewId = viewId };
                views.Add(found);
            }
            found.Post ??= new LocationPostEffects();
            return found;
        }
    }
}
