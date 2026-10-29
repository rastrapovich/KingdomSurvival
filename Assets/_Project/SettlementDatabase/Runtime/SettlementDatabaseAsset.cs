using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.SettlementDatabase
{
    public enum SettlementImageFit
    {
        // Заполнить кадр (лишнее обрезается по фокусу).
        Cover,
        // Вписать целиком (поля — нейтральный фон).
        Contain
    }

    // Изображение экрана и его кадрирование: фокус — какая точка картинки
    // остаётся в кадре при обрезке, масштаб — ограниченное приближение,
    // затемнение — под левой текстовой панелью.
    [Serializable]
    public sealed class SettlementImageFraming
    {
        public Sprite Image;
        public SettlementImageFit Fit = SettlementImageFit.Cover;
        public Vector2 Focus = new Vector2(0.5f, 0.5f);
        [Range(1f, 2f)] public float Zoom = 1f;
        [Range(0f, 0.85f)] public float PanelDim = 0.55f;
    }

    [Serializable]
    public sealed class SettlementVariantArt
    {
        public string VariantId = string.Empty;
        public Sprite Image;
    }

    [Serializable]
    public sealed class SettlementArt
    {
        public SettlementImageFraming Screen = new SettlementImageFraming();
        public SettlementImageFraming Tavern = new SettlementImageFraming();
        public SettlementImageFraming Board = new SettlementImageFraming();
        // Акцент рамок и заголовков в пределах общей темы.
        public Color Accent = new Color(0.87f, 0.71f, 0.39f, 1f);
        public List<SettlementVariantArt> Variants = new List<SettlementVariantArt>();
    }

    [Serializable]
    public sealed class SettlementProfileEntry
    {
        public SettlementProfileDefinition Data = new SettlementProfileDefinition();
        public SettlementArt Art = new SettlementArt();
        [TextArea(2, 6)] public string AuthorNotes = string.Empty;
    }

    // «База поселений» (Kingdom Survival → База поселений): профили посещаемых
    // поселений — изображение, люди, действия, торговцы, корчма, доска — и
    // общие поручения. Во время игры не меняется: кошельки, остатки,
    // прочитанное и принятое живут в сохранении партии.
    public sealed class SettlementDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "SettlementDatabase/KingdomSurvivalSettlements";
        public const string AssetPath = "Assets/_Project/SettlementDatabase/Resources/SettlementDatabase/KingdomSurvivalSettlements.asset";

        public List<SettlementProfileEntry> profiles = new List<SettlementProfileEntry>();
        public List<ErrandDefinition> errands = new List<ErrandDefinition>();
        // Рабочие сцены поселений, уже засеянные в Базу диалогов (удалённую
        // автором сцену засев второй раз не вернёт).
        public List<string> seededDialogueIds = new List<string>();

        public SettlementProfileEntry FindEntry(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || profiles == null)
                return null;
            return profiles.Find(p => p?.Data != null && p.Data.Id == profileId);
        }

        // Каталог ядра. Данные отдаются как есть: ядро их не меняет.
        public SettlementCatalogData ToCatalog()
        {
            List<SettlementProfileDefinition> list = new List<SettlementProfileDefinition>();
            if (profiles != null)
            {
                foreach (SettlementProfileEntry entry in profiles)
                {
                    if (entry?.Data != null)
                        list.Add(entry.Data);
                }
            }
            return new SettlementCatalogData(list, errands);
        }

        // Изображение экрана с учётом условного варианта.
        public Sprite ScreenImage(string profileId, string variantId)
        {
            SettlementProfileEntry entry = FindEntry(profileId);
            if (entry?.Art == null)
                return null;
            if (!string.IsNullOrEmpty(variantId) && entry.Art.Variants != null)
            {
                SettlementVariantArt art = entry.Art.Variants.Find(v => v != null && v.VariantId == variantId && v.Image != null);
                if (art != null)
                    return art.Image;
            }
            return entry.Art.Screen?.Image;
        }
    }
}
