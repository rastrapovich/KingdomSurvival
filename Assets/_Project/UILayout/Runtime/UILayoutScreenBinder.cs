using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UILayout
{
    /// <summary>
    /// Универсальное применение экрана из `UILayoutDatabaseAsset` к дереву
    /// UI Toolkit по именам элементов UXML.
    ///
    /// Экран диалога имеет собственный код применения в `PrototypeUIController`
    /// и через этот байндер не проходит: у него `autoApply = false`.
    ///
    /// Ключевое правило безопасности: обычный элемент меняется только по
    /// явно включённым `overrideRect` / `overrideBackground` / `overrideText`.
    /// Тип `Portrait` сам является явным выбором preset-геометрии и изображения,
    /// поэтому применяет их без двух legacy-флагов.
    /// </summary>
    public static class UILayoutScreenBinder
    {
        /// <summary>
        /// Применяет все экраны базы, у которых включён `autoApply`.
        /// </summary>
        public static void ApplyAutoScreens(
            VisualElement interfaceRoot,
            Vector2 actualResolution)
        {
            UILayoutDatabaseAsset database = UILayoutRuntimeApplier.LoadDefaultDatabase();
            if (database == null || interfaceRoot == null)
                return;

            IReadOnlyList<UILayoutScreenDefinition> screens = database.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                UILayoutScreenDefinition screen = screens[i];
                if (screen == null || !screen.AutoApply)
                    continue;
                ApplyScreen(database, screen, interfaceRoot, actualResolution);
            }
        }

        /// <summary>
        /// Применяет один экран по его идентификатору.
        /// </summary>
        public static bool ApplyScreen(
            string screenId,
            VisualElement interfaceRoot,
            Vector2 actualResolution)
        {
            UILayoutDatabaseAsset database = UILayoutRuntimeApplier.LoadDefaultDatabase();
            if (database == null || interfaceRoot == null)
                return false;

            UILayoutScreenDefinition screen = database.FindScreen(screenId);
            if (screen == null)
                return false;

            ApplyScreen(database, screen, interfaceRoot, actualResolution);
            return true;
        }

        private static void ApplyScreen(
            UILayoutDatabaseAsset database,
            UILayoutScreenDefinition screen,
            VisualElement interfaceRoot,
            Vector2 actualResolution)
        {
            VisualElement scope = ResolveScope(interfaceRoot, screen);
            if (scope == null)
                return;

            Vector2 reference = database.ReferenceResolution;
            if (actualResolution.x <= 0f || actualResolution.y <= 0f)
                actualResolution = reference;

            IReadOnlyList<UILayoutElementDefinition> elements = screen.Elements;
            for (int i = 0; i < elements.Count; i++)
            {
                UILayoutElementDefinition element = elements[i];
                if (element == null)
                    continue;

                bool wantsRect = ShouldApplyRect(element);
                bool wantsBackground = ShouldApplyBackground(element);
                bool wantsAnything = wantsRect ||
                                     wantsBackground ||
                                     element.OverrideText;
                if (!wantsAnything)
                    continue;

                VisualElement target = ResolveTarget(scope, element);
                if (target == null)
                    continue;

                if (wantsRect)
                    UILayoutRuntimeApplier.ApplyRect(target, element, screen, reference, actualResolution);
                if (wantsBackground)
                    UILayoutRuntimeApplier.ApplyBackground(target, element, reference, actualResolution);
                if (element.OverrideText && element.IsTextual)
                    UILayoutRuntimeApplier.ApplyTextStyle(target, element, reference, actualResolution);
            }
        }

        /// <summary>
        /// Выбор типа Portrait сам является явным включением канонической
        /// геометрии и изображения. У такого элемента нет отдельного режима
        /// свободного Rect, который мог бы вернуть произвольный размер.
        /// </summary>
        public static bool ShouldApplyRect(UILayoutElementDefinition element)
        {
            return element != null && (element.IsPortrait || element.OverrideRect);
        }

        public static bool ShouldApplyBackground(UILayoutElementDefinition element)
        {
            return element != null && (element.IsPortrait || element.OverrideBackground);
        }

        private static VisualElement ResolveScope(
            VisualElement interfaceRoot,
            UILayoutScreenDefinition screen)
        {
            if (string.IsNullOrWhiteSpace(screen.RootName))
                return interfaceRoot;

            VisualElement scope = interfaceRoot.Q<VisualElement>(screen.RootName);
            return scope ?? interfaceRoot;
        }

        private static VisualElement ResolveTarget(
            VisualElement scope,
            UILayoutElementDefinition element)
        {
            string name = element.TargetName;
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (string.Equals(scope.name, name, System.StringComparison.Ordinal))
                return scope;
            return scope.Q<VisualElement>(name);
        }

        /// <summary>
        /// Диагностика для конструктора: какие элементы экрана не находятся
        /// в текущем дереве интерфейса.
        /// </summary>
        public static void CollectMissingTargets(
            UILayoutScreenDefinition screen,
            VisualElement interfaceRoot,
            List<string> missing)
        {
            if (screen == null || interfaceRoot == null || missing == null)
                return;

            VisualElement scope = ResolveScope(interfaceRoot, screen);
            if (scope == null)
                return;

            IReadOnlyList<UILayoutElementDefinition> elements = screen.Elements;
            for (int i = 0; i < elements.Count; i++)
            {
                UILayoutElementDefinition element = elements[i];
                if (element == null)
                    continue;
                if (ResolveTarget(scope, element) == null)
                    missing.Add(element.Id + " → " + element.TargetName);
            }
        }
    }
}
