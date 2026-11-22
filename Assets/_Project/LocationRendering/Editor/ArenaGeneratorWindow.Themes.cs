using System.Linq;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLabBridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Настройки по темам: «Стандартная тема» — общие (asset.Tactics/Look); у выбранной
    // темы можно завести свои (профиль в asset.ThemeProfiles), они сохраняются в ассете
    // и не сбрасываются при переключении тем.
    public sealed partial class ArenaGeneratorWindow
    {
        // Id выбранной темы; null — «Стандартная» или «Тема по seed» (правка идёт в общие).
        private string SelectedThemeId()
        {
            if (ThemeIndex < 0) return null;
            System.Collections.Generic.List<SandboxArenaTheme> all = SandboxArenaThemes.All.ToList();
            return ThemeIndex < all.Count ? SandboxArenaThemes.Id(all[ThemeIndex]) : null;
        }

        private int ActiveProfileIndex()
        {
            string id = SelectedThemeId();
            return id == null || asset == null ? -1 : asset.ProfileIndex(id);
        }

        // Путь поля в SerializedObject: у темы со своими настройками — в её профиле.
        private string ProfilePath(string path)
        {
            int index = ActiveProfileIndex();
            return index < 0 ? path : "ThemeProfiles.Array.data[" + index + "]." + path;
        }

        private SandboxArenaGenerationSettings ActiveTactics
        {
            get { int i = ActiveProfileIndex(); return i < 0 ? asset.Tactics : asset.ThemeProfiles[i].Tactics; }
            set { int i = ActiveProfileIndex(); if (i < 0) asset.Tactics = value; else asset.ThemeProfiles[i].Tactics = value; asset.Activate(); }
        }

        private MapLabArenaLook ActiveLook
        {
            get { int i = ActiveProfileIndex(); return i < 0 ? asset.Look : asset.ThemeProfiles[i].Look; }
            set { int i = ActiveProfileIndex(); if (i < 0) asset.Look = value; else asset.ThemeProfiles[i].Look = value; }
        }

        private void BuildThemeBanner()
        {
            string id = SelectedThemeId();
            if (id == null)
            {
                string who = ThemeIndex == -2
                    ? "«Стандартная тема»: общие настройки. Их берут все темы, у которых нет своих. Предпросмотр справа — обычная тема по seed с этими настройками."
                    : "«Тема по seed»: правки идут в стандартные настройки. Чтобы настроить конкретную тему, выберите её в списке «Тема».";
                left.Add(new HelpBox(who, HelpBoxMessageType.Info));
                return;
            }
            SandboxArenaTheme theme = SandboxArenaThemes.All.First(t => SandboxArenaThemes.Id(t) == id);
            int index = asset.ProfileIndex(id);
            VisualElement row = Row();
            if (index < 0)
            {
                left.Add(new HelpBox("У темы «" + SandboxArenaThemes.Label(theme) + "» нет своих настроек — она использует стандартные (ниже показаны они, править нельзя). " +
                                     "Заведите свои: это копия стандартных, дальше они живут отдельно и сохраняются.", HelpBoxMessageType.Info));
                AddButton(row, "Завести свои настройки темы", () =>
                {
                    Undo.RecordObject(asset, "Генератор арен: свои настройки темы");
                    asset.ThemeProfiles.Add(new ArenaThemeProfile { ThemeId = id, Tactics = asset.Tactics.Clone(), Look = asset.Look.Clone() });
                    EditorUtility.SetDirty(asset);
                    asset.Activate();
                    ArenaGeneratorAsset.Touch();
                    AssetDatabase.SaveAssetIfDirty(asset);
                    serialized = new SerializedObject(asset);
                    BuildLeft();
                    RequestRebuild(0);
                }, "Копия стандартных настроек, дальше правится отдельно");
            }
            else
            {
                left.Add(new HelpBox("Свои настройки темы «" + SandboxArenaThemes.Label(theme) + "» — сохраняются в ассете и не сбрасываются при переключении тем.", HelpBoxMessageType.Info));
                AddButton(row, "Скопировать из стандартных", () =>
                {
                    if (!EditorUtility.DisplayDialog("Заменить настройки темы?", "Свои настройки темы будут заменены копией стандартных. Ctrl+Z — вернуть.", "Заменить", "Отмена")) return;
                    Undo.RecordObject(asset, "Генератор арен: из стандартных");
                    asset.ThemeProfiles[index].Tactics = asset.Tactics.Clone();
                    asset.ThemeProfiles[index].Look = asset.Look.Clone();
                    ignoreUntil = 0;
                    MarkChanged(true);
                    serialized.Update();
                    BuildLeft();
                }, "Начать заново от стандартных");
                AddButton(row, "Убрать свои (вернуть к стандартным)", () =>
                {
                    if (!EditorUtility.DisplayDialog("Убрать свои настройки темы?", "Тема снова будет использовать стандартные настройки. Ctrl+Z — вернуть.", "Убрать", "Отмена")) return;
                    Undo.RecordObject(asset, "Генератор арен: убрать настройки темы");
                    asset.ThemeProfiles.RemoveAt(index);
                    asset.Activate();
                    ignoreUntil = 0;
                    MarkChanged(true);
                    serialized = new SerializedObject(asset);
                    BuildLeft();
                }, "Тема вернётся к общим настройкам");
            }
            left.Add(row);
        }

        // У темы без своих настроек поля показывают стандартные и не правятся.
        private void LockIfNoProfile()
        {
            if (SelectedThemeId() == null || ActiveProfileIndex() >= 0) return;
            for (int i = 1; i < left.childCount; i++)
            {
                // Баннер (HelpBox + кнопка заведения) остаётся доступным.
                VisualElement child = left[i];
                if (i <= 2 && (child is HelpBox || child.childCount > 0 && child[0] is Button && ((Button)child[0]).text.StartsWith("Завести")))
                    continue;
                child.SetEnabled(false);
            }
        }
    }
}
