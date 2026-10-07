using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.ArtAssets
{
    // Следующий кадр покадровой анимации: рисунок и его карта нормалей.
    [Serializable]
    public sealed class ArtAssetFrame
    {
        public Sprite Sprite;
        public Texture2D NormalMap;
    }

    // Одна часть объекта в одном ракурсе: рисунок, его карта нормалей,
    // смещение и (необязательно) свой силуэт тени.
    [Serializable]
    public sealed class ArtAssetPartView
    {
        public Sprite Sprite;
        // Карта нормалей этого рисунка. В рендере 2D-свет берёт её из
        // настроек импорта рисунка (вторая текстура _NormalMap); поле хранит
        // назначение для проверки, замены и снятия.
        public Texture2D NormalMap;
        // Смещение левого нижнего угла рисунка части от левого нижнего угла
        // рисунка основы, единицы мира (Y вверх). Слои одного кадра — ноль.
        public Vector2 Offset;
        // Свой силуэт тени (крона: тень не совпадает с рисунком).
        public Sprite ShadowSprite;
        // ПР-12П: кадры анимации после первого (первый — Sprite выше). Пусто —
        // неподвижный рисунок. Кадры одного размера с первым; нормаль — своя
        // у каждого кадра (подключается к его рисунку при импорте).
        public List<ArtAssetFrame> Frames = new List<ArtAssetFrame>();

        public bool HasSprite => Sprite != null;
        public bool HasNormal => Sprite != null && NormalMap != null;

        // Кадров всего, считая первый; кадры без рисунка пропускаются.
        public int FrameCount
        {
            get
            {
                if (Sprite == null) return 0;
                int count = 1;
                if (Frames != null)
                    foreach (ArtAssetFrame frame in Frames) if (frame?.Sprite != null) count++;
                return count;
            }
        }

        public bool IsAnimated => FrameCount > 1;

        // Рисунки кадров по порядку, первый — Sprite.
        public Sprite[] FrameSprites()
        {
            if (Sprite == null) return Array.Empty<Sprite>();
            List<Sprite> result = new List<Sprite> { Sprite };
            if (Frames != null)
                foreach (ArtAssetFrame frame in Frames) if (frame?.Sprite != null) result.Add(frame.Sprite);
            return result.ToArray();
        }

        // Нормаль кадра с этим рисунком (первого или следующего).
        public Texture2D NormalOf(Sprite sprite)
        {
            if (sprite == null) return null;
            if (sprite == Sprite) return NormalMap;
            if (Frames != null)
                foreach (ArtAssetFrame frame in Frames) if (frame != null && frame.Sprite == sprite) return frame.NormalMap;
            return null;
        }
    }

    // Логическая часть: «Основа», «Крыша», «Крона», «Ствол» или своё имя.
    // Первая часть — основа: по ней считаются опора, размер и полнота ракурсов.
    [Serializable]
    public sealed class ArtAssetPart
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Основа";
        public ArtAssetLayer Layer = ArtAssetLayer.World;
        public int OrderOffset;
        public bool ProjectsShadow = true;
        public List<ArtAssetPartView> Views = new List<ArtAssetPartView>();

        public ArtAssetPartView View(ArtAssetView view)
        {
            if (Views == null) Views = new List<ArtAssetPartView>();
            while (Views.Count < ArtAssetLabels.ViewCount) Views.Add(new ArtAssetPartView());
            if (Views[(int)view] == null) Views[(int)view] = new ArtAssetPartView();
            return Views[(int)view];
        }

        public ArtAssetPartView FindView(ArtAssetView view) =>
            Views != null && (int)view < Views.Count ? Views[(int)view] : null;
    }

    // Настройки одного ракурса: опора и основание на земле. У фронтального
    // и заднего вида они могут отличаться.
    [Serializable]
    public sealed class ArtAssetViewSettings
    {
        // Точка касания земли: доля рисунка основы (0..1, Y вверх, как у Sprite).
        public Vector2 Pivot = new Vector2(.5f, .1f);
        // Занятая область земли: размер и смещение центра от опоры (единицы мира, Y вверх).
        public Vector2 FootprintSize = new Vector2(1.2f, .55f);
        public Vector2 FootprintOffset;
    }

    // ПР-12Н: запись Базы ассетов — один логический объект с шестью ракурсами.
    // Ссылки из мест — только по Id: не по названию, порядку или пути файла.
    [Serializable]
    public sealed class ArtAssetDefinition
    {
        public const float DefaultPixelsPerUnit = 108f;

        public string Id = NewId();
        public string Name = "Новый ассет";
        public ArtAssetCategory Category;
        public List<string> Tags = new List<string>();
        public bool Favorite;
        public string Credit = "";
        public string SourceUrl = "";
        // Имя источника последнего импорта (папка или общая часть имён файлов) —
        // подсказка для поиска дубликатов, не ключ.
        public string ImportKey = "";

        // Масштаб всех ракурсов и частей: сколько пикселей рисунка в единице
        // мира. Прозрачные поля PNG не меняют размер и не сдвигают объект.
        public float PixelsPerUnit = DefaultPixelsPerUnit;

        // Рекомендуемые настройки места; экземпляр может переопределить явно.
        public bool BlocksMovement;
        public bool OccludesLight;
        public float ShadowLength = 1;

        // ПР-12П: покадровая анимация (трава, флаг, вода). Скорость и порядок
        // общие для всех частей и ракурсов; у каждой части — свои кадры.
        // Случайная фаза — у каждого экземпляра свой сдвиг по ID, чтобы
        // заросль из одинаковых кустов не качалась в такт.
        public float FramesPerSecond = 8;
        public ArtAssetPlayback Playback = ArtAssetPlayback.Loop;
        public bool RandomPhase = true;

        public List<ArtAssetViewSettings> ViewSettings = new List<ArtAssetViewSettings>();
        public List<ArtAssetPart> Parts = new List<ArtAssetPart> { new ArtAssetPart() };

        public static string NewId() => "asset_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        public ArtAssetPart MainPart
        {
            get
            {
                if (Parts == null) Parts = new List<ArtAssetPart>();
                if (Parts.Count == 0) Parts.Add(new ArtAssetPart());
                return Parts[0];
            }
        }

        public ArtAssetViewSettings Settings(ArtAssetView view)
        {
            if (ViewSettings == null) ViewSettings = new List<ArtAssetViewSettings>();
            while (ViewSettings.Count < ArtAssetLabels.ViewCount) ViewSettings.Add(new ArtAssetViewSettings());
            if (ViewSettings[(int)view] == null) ViewSettings[(int)view] = new ArtAssetViewSettings();
            return ViewSettings[(int)view];
        }

        public Sprite MainSprite(ArtAssetView view) => Parts != null && Parts.Count > 0 ? Parts[0].FindView(view)?.Sprite : null;

        public bool HasView(ArtAssetView view) => MainSprite(view) != null;

        public bool HasNormal(ArtAssetView view)
        {
            ArtAssetPartView main = Parts != null && Parts.Count > 0 ? Parts[0].FindView(view) : null;
            return main != null && main.HasNormal;
        }

        public int ViewCount
        {
            get
            {
                int count = 0;
                foreach (ArtAssetView view in ArtAssetLabels.Views) if (HasView(view)) count++;
                return count;
            }
        }

        // Нормали считаются по ракурсам, где есть рисунок основы.
        public int NormalCount
        {
            get
            {
                int count = 0;
                foreach (ArtAssetView view in ArtAssetLabels.Views) if (HasNormal(view)) count++;
                return count;
            }
        }

        // Есть ли кадры анимации хотя бы у одной части в каком-нибудь ракурсе.
        public bool IsAnimated
        {
            get
            {
                if (Parts == null) return false;
                foreach (ArtAssetPart part in Parts)
                {
                    if (part?.Views == null) continue;
                    foreach (ArtAssetPartView view in part.Views)
                        if (view != null && view.IsAnimated) return true;
                }
                return false;
            }
        }

        // Наибольшее число кадров среди частей в ракурсе (1 — неподвижный).
        public int FrameCountIn(ArtAssetView view)
        {
            int count = 0;
            if (Parts == null) return count;
            foreach (ArtAssetPart part in Parts)
            {
                ArtAssetPartView slot = part?.FindView(view);
                if (slot != null) count = Math.Max(count, slot.FrameCount);
            }
            return count;
        }

        public bool IsEmpty
        {
            get
            {
                if (Parts == null) return true;
                foreach (ArtAssetPart part in Parts)
                {
                    if (part?.Views == null) continue;
                    foreach (ArtAssetPartView view in part.Views)
                        if (view != null && view.Sprite != null) return false;
                }
                return true;
            }
        }

        // Ракурс, который будет показан вместо запрошенного; false — рисунка нет совсем.
        public bool TryResolveView(ArtAssetView requested, out ArtAssetView shown)
        {
            foreach (ArtAssetView view in ArtAssetLabels.FallbackOrder(requested))
            {
                if (HasView(view)) { shown = view; return true; }
            }
            shown = requested;
            return false;
        }

        // Эталонный ракурс размера: «Спереди», при его отсутствии — первый имеющийся.
        public bool TryReferenceView(out ArtAssetView view) => TryResolveView(ArtAssetView.Front, out view);

        public float SafePixelsPerUnit => PixelsPerUnit > .01f ? PixelsPerUnit : DefaultPixelsPerUnit;

        // Высота рисунка основы в ракурсе (единицы мира, без масштаба экземпляра).
        public float HeightIn(ArtAssetView view)
        {
            Sprite sprite = MainSprite(view);
            return sprite != null ? sprite.rect.height / SafePixelsPerUnit : 0;
        }

        // Игровой размер: высота эталонного ракурса. Запись меняет масштаб всех ракурсов.
        public float Height
        {
            get => TryReferenceView(out ArtAssetView view) ? HeightIn(view) : 0;
            set
            {
                if (value <= 0 || !TryReferenceView(out ArtAssetView view)) return;
                PixelsPerUnit = MainSprite(view).rect.height / value;
            }
        }

        public bool MatchesQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            query = query.Trim();
            if (Contains(Name, query) || Contains(Id, query)) return true;
            if (Tags != null)
                foreach (string tag in Tags) if (Contains(tag, query)) return true;
            return false;
        }

        private static bool Contains(string value, string query) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        public string TagsText
        {
            get => Tags == null ? string.Empty : string.Join(", ", Tags);
            set
            {
                Tags = new List<string>();
                if (string.IsNullOrWhiteSpace(value)) return;
                foreach (string tag in value.Split(','))
                {
                    string trimmed = tag.Trim();
                    if (trimmed.Length > 0 && !Tags.Contains(trimmed)) Tags.Add(trimmed);
                }
            }
        }
    }
}
