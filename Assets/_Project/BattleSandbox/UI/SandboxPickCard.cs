using System;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Карточка выбора в полигоне: крупный портрет и имя, без характеристик.
    // ЛКМ — добавить, ПКМ — карточка бойца, «−» — убрать. Состояние (число,
    // доступность) меняется на месте, без перестройки колонки — прокрутка
    // не сбрасывается.
    internal sealed class SandboxPickCard
    {
        public const float Width = 150f;
        public const float PortraitHeight = 200f;
        private const float NameHeight = 34f;

        private static readonly Color CardBackground = new Color(0.085f, 0.095f, 0.11f, 1f);
        private static readonly Color IdleBorder = new Color(0.25f, 0.26f, 0.27f, 1f);

        private readonly Color accent;
        private readonly Label badge;
        private readonly Button minus;
        private readonly Label name;
        private bool hovered;
        private int count;

        public Button Root { get; }
        public string TypeId { get; }

        public SandboxPickCard(
            SandboxUnitDefinition definition,
            SandboxUnitVisual visual,
            Color accent,
            Action onAdd,
            Action onRemove,
            Action onDetails)
        {
            this.accent = accent;
            TypeId = definition.Id;
            Root = new Button(onAdd) { name = "sandbox-pick-card-" + definition.Id };
            Root.style.width = Width;
            Root.style.height = PortraitHeight + NameHeight;
            Root.style.marginLeft = 0f;
            Root.style.marginTop = 0f;
            Root.style.marginRight = 10f;
            Root.style.marginBottom = 10f;
            Root.style.paddingLeft = 0f;
            Root.style.paddingRight = 0f;
            Root.style.paddingTop = 0f;
            Root.style.paddingBottom = 0f;
            Root.style.flexShrink = 0f;
            Root.style.overflow = Overflow.Hidden;
            Root.style.backgroundColor = CardBackground;
            SetRadius(Root, 6f);
            Root.tooltip = definition.RoleLabel + "\nЛКМ — добавить · ПКМ — карточка · «−» — убрать";

            VisualElement portrait = new VisualElement { pickingMode = PickingMode.Ignore };
            portrait.style.height = PortraitHeight;
            portrait.style.overflow = Overflow.Hidden;
            portrait.style.backgroundColor = new Color(0.12f, 0.13f, 0.15f, 1f);
            portrait.style.alignItems = Align.Center;
            portrait.style.justifyContent = Justify.Center;
            FillPortrait(portrait, definition, visual);
            Root.Add(portrait);

            name = new Label(definition.RoleLabel.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            name.style.height = NameHeight;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.fontSize = 12f;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.paddingLeft = 4f;
            name.style.paddingRight = 4f;
            Root.Add(name);

            badge = new Label { pickingMode = PickingMode.Ignore };
            badge.style.position = Position.Absolute;
            badge.style.top = 6f;
            badge.style.right = 6f;
            badge.style.minWidth = 30f;
            badge.style.height = 30f;
            badge.style.paddingLeft = 6f;
            badge.style.paddingRight = 6f;
            badge.style.fontSize = 15f;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.style.color = new Color(0.07f, 0.06f, 0.05f, 1f);
            badge.style.backgroundColor = accent;
            SetRadius(badge, 15f);
            Root.Add(badge);

            minus = new Button(() => onRemove?.Invoke()) { text = "−", tooltip = "Убрать одного" };
            minus.style.position = Position.Absolute;
            minus.style.top = 6f;
            minus.style.left = 6f;
            minus.style.width = 30f;
            minus.style.height = 30f;
            minus.style.marginLeft = 0f;
            minus.style.marginTop = 0f;
            minus.style.fontSize = 18f;
            minus.style.unityFontStyleAndWeight = FontStyle.Bold;
            minus.style.color = new Color(0.95f, 0.90f, 0.82f, 1f);
            minus.style.backgroundColor = new Color(0.06f, 0.06f, 0.07f, 0.85f);
            SetBorder(minus, new Color(0.45f, 0.44f, 0.42f, 1f), 1f);
            SetRadius(minus, 15f);
            // «−» не должен заодно срабатывать как щелчок по карточке.
            minus.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            minus.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            minus.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            Root.Add(minus);

            Root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1)
                    return;
                onDetails?.Invoke();
                evt.StopPropagation();
            });
            Root.RegisterCallback<PointerEnterEvent>(_ => { hovered = true; ApplyLook(); });
            Root.RegisterCallback<PointerLeaveEvent>(_ => { hovered = false; ApplyLook(); });
            SetState(0, true);
        }

        // Число выбранных и можно ли добавить ещё (сторона не заполнена).
        public void SetState(int value, bool canAdd)
        {
            count = Mathf.Max(0, value);
            badge.text = count > 0 ? "×" + count : string.Empty;
            badge.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            minus.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Root.SetEnabled(true);
            Root.style.opacity = canAdd || count > 0 ? 1f : 0.45f;
            ApplyLook();
        }

        private void ApplyLook()
        {
            bool picked = count > 0;
            Color border = picked ? accent : hovered ? Color.Lerp(IdleBorder, accent, 0.55f) : IdleBorder;
            SetBorder(Root, border, picked ? 2f : 1f);
            name.style.color = picked ? accent : new Color(0.82f, 0.80f, 0.74f, 1f);
            name.style.backgroundColor = picked
                ? new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, 1f)
                : new Color(0.07f, 0.075f, 0.085f, 1f);
        }

        // Портрет с кадрированием Базы существ; без портрета — миниатюра поля
        // или первый кадр анимации; без них — буквы жетона.
        private static void FillPortrait(VisualElement box, SandboxUnitDefinition definition, SandboxUnitVisual visual)
        {
            Sprite portrait = SandboxFighterCardFactory.ResolvePortrait(definition.Id, visual.Portrait);
            if (portrait != null)
            {
                UnitPortraitElement image = new UnitPortraitElement { pickingMode = PickingMode.Ignore };
                image.style.position = Position.Absolute;
                image.style.left = 0f;
                image.style.right = 0f;
                image.style.top = 0f;
                image.style.bottom = 0f;
                SandboxFighterCardFactory.ApplyPortraitFraming(image, definition.Id, portrait);
                box.Add(image);
                return;
            }

            Sprite figure = visual.BattlefieldSprite != null
                ? visual.BattlefieldSprite
                : visual.AnimationSet != null ? visual.AnimationSet.FindFirstFrame() : null;
            if (figure != null)
            {
                Image image = new Image { sprite = figure, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.style.position = Position.Absolute;
                image.style.left = 6f;
                image.style.right = 6f;
                image.style.top = 6f;
                image.style.bottom = 6f;
                box.Add(image);
                return;
            }

            string letters = !string.IsNullOrEmpty(visual.TokenText)
                ? visual.TokenText
                : SandboxUnitVisual.MakeTokenText(definition.RoleLabel);
            Label token = new Label(letters) { pickingMode = PickingMode.Ignore };
            token.style.fontSize = 46f;
            token.style.unityFontStyleAndWeight = FontStyle.Bold;
            token.style.color = new Color(0.55f, 0.52f, 0.46f, 1f);
            box.Add(token);
            Label waiting = new Label("ждёт рисунка") { pickingMode = PickingMode.Ignore };
            waiting.style.fontSize = 10f;
            waiting.style.color = new Color(0.45f, 0.45f, 0.43f, 1f);
            box.Add(waiting);
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
