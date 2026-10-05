using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12Н: предметы из Базы ассетов. Экземпляр хранит ссылку (ID ассета),
    // ракурс, положение, масштаб, отражение и явные переопределения; рисунки,
    // части, опора и основание ракурса — в каталоге. Старые предметы с
    // прямым рисунком работают как прежде и переводятся кнопкой.
    public sealed partial class LocationDatabaseWindow
    {
        private const string PaletteKey = "KS.LocationDatabase.AssetPalette";
        private const int PaletteSize = 12;
        private string placingAssetId;

        private void PickAssetToAdd() => ArtAssetPicker.Show("Добавить из Базы ассетов", id => AddAssetInstance(id));

        private void AddAssetInstance(string assetId, Vector2? pixel = null)
        {
            ArtAssetDefinition asset = ArtAssetDatabaseAsset.FindCurrent(assetId);
            if (asset == null || Visual == null) { status.text = "Ассет не найден в Базе ассетов."; return; }
            LocalLocationDefinition location = Location;
            ArtAssetView view = asset.TryResolveView(ArtAssetView.Front, out ArtAssetView shown) ? shown : ArtAssetView.Front;
            Change(() =>
            {
                ArtAssetPart main = asset.MainPart;
                ArtAssetViewSettings settings = asset.Settings(view);
                // Поля прямого рисунка — значения для явных переопределений,
                // заполнены рекомендациями ассета: включение не меняет вид.
                LocationVisualObject item = new LocationVisualObject
                {
                    Name = asset.Name, AssetId = asset.Id, View = view, Scale = 1,
                    Position = LocationVisualGeometry.ToNormalized(location, pixel ?? viewCenter),
                    Height = Mathf.Max(.1f, asset.Height), Pivot = settings.Pivot,
                    Band = LocationVisualResolver.Band(main.Layer), OrderOffset = main.OrderOffset,
                    BlocksMovement = asset.BlocksMovement, Footprint = settings.FootprintSize,
                    ProjectsShadow = main.ProjectsShadow, ShadowLength = asset.ShadowLength, CastsShadow = asset.OccludesLight
                };
                Visual.Objects.Add(item);
                selectedKind = Kind.Art;
                selectedElementId = item.Id;
            }, true);
            RememberPalette(asset.Id);
            status.text = "Добавлен «" + asset.Name + "» (ссылка на Базу ассетов).";
        }

        // ------------------------------------------------------------------
        // Палитра: избранное каталога и недавно поставленные
        // ------------------------------------------------------------------

        private static List<string> RecentAssets() =>
            EditorPrefs.GetString(PaletteKey, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static void RememberPalette(string id)
        {
            List<string> recent = RecentAssets();
            recent.Remove(id);
            recent.Insert(0, id);
            EditorPrefs.SetString(PaletteKey, string.Join("|", recent.Take(PaletteSize)));
        }

        private void BuildAssetPalette()
        {
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            if (catalog == null) return;
            List<ArtAssetDefinition> items = RecentAssets().Select(catalog.Find).Where(item => item != null)
                .Concat(catalog.assets.Where(item => item != null && item.Favorite)).Distinct().Take(PaletteSize).ToList();
            Label title = new Label("Палитра (недавние и избранные) — клик: ставить кликами по месту");
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.color = new Color(.7f, .76f, .7f);
            title.style.marginTop = 6;
            settings.Add(title);
            if (items.Count == 0)
            {
                settings.Add(new HelpBox("Пусто. Добавьте ассет кнопкой «+ Из Базы ассетов» или перетащите его из окна «База ассетов» на картинку места.", HelpBoxMessageType.None));
                return;
            }
            const float size = 62;
            IMGUIContainer palette = new IMGUIContainer();
            palette.onGUIHandler = () =>
            {
                float width = palette.contentRect.width;
                int columns = Mathf.Max(1, Mathf.FloorToInt(width / (size + 4)));
                for (int i = 0; i < items.Count; i++)
                {
                    ArtAssetDefinition asset = items[i];
                    Rect cell = new Rect((i % columns) * (size + 4), (i / columns) * (size + 18), size, size + 14);
                    bool armed = tool == Tool.PlaceAsset && placingAssetId == asset.Id;
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(cell, armed ? new Color(.95f, .75f, .3f, .45f) : new Color(0, 0, 0, .25f));
                        ArtAssetDrawing.DrawFitted(catalog, asset, ArtAssetView.Front, new Rect(cell.x + 3, cell.y + 3, size - 6, size - 6));
                        GUI.Label(new Rect(cell.x, cell.yMax - 14, cell.width, 14), asset.Name, EditorStyles.miniLabel);
                    }
                    if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                    {
                        placingAssetId = armed ? null : asset.Id;
                        SetTool(armed ? Tool.Select : Tool.PlaceAsset);
                        Event.current.Use();
                    }
                }
            };
            int rows = Mathf.CeilToInt(items.Count / 5f);
            palette.style.height = rows * (size + 18) + 4;
            settings.Add(palette);
        }

        // ------------------------------------------------------------------
        // Экземпляр ассета
        // ------------------------------------------------------------------

        private void BuildAssetInstanceSettings(LocationVisualDefinition visual, LocationVisualObject selected)
        {
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            ArtAssetDefinition asset = ArtAssetDatabaseAsset.FindCurrent(selected.AssetId);
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(selected);
            Text("Название", selected.Name, value => selected.Name = value);
            if (asset == null)
                settings.Add(new HelpBox("Ассет «" + selected.AssetId + "» не найден в Базе ассетов. Показан запасной прямой рисунок (если он был).", HelpBoxMessageType.Error));
            else
            {
                Label source = new Label("Ассет: " + asset.Name + " · " + ArtAssetDrawing.Completeness(asset));
                source.style.whiteSpace = WhiteSpace.Normal;
                settings.Add(source);
            }
            VisualElement actions = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            AddButton(actions, "Открыть в Базе ассетов", () => ArtAssetDatabaseWindow.Open(selected.AssetId));
            AddButton(actions, "Заменить ассет…", () => ArtAssetPicker.Show("Заменить ассет", id => ReplaceAsset(selected, id), selected.AssetId));
            settings.Add(actions);

            if (asset != null)
            {
                Label viewTitle = new Label("Ракурс экземпляра (камера и герой ракурс не меняют)");
                viewTitle.style.whiteSpace = WhiteSpace.Normal;
                viewTitle.style.marginTop = 6;
                settings.Add(viewTitle);
                const float size = 52;
                IMGUIContainer views = new IMGUIContainer(() =>
                {
                    for (int i = 0; i < ArtAssetLabels.ViewCount; i++)
                    {
                        ArtAssetView view = ArtAssetLabels.Views[i];
                        Rect cell = new Rect((i % 3) * (size + 52), (i / 3) * (size + 18), size + 48, size + 14);
                        bool active = selected.View == view;
                        bool has = asset.HasView(view);
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(cell, active ? new Color(.95f, .75f, .3f, .45f) : new Color(0, 0, 0, .25f));
                            if (has) ArtAssetDrawing.DrawFitted(catalog, asset, view, new Rect(cell.x + 3, cell.y + 3, cell.width - 6, size - 6));
                            GUI.Label(new Rect(cell.x + 2, cell.yMax - 14, cell.width, 14), ArtAssetLabels.ViewTitle(view) + (has ? "" : " · нет"), EditorStyles.miniLabel);
                        }
                        if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                        {
                            Change(() => selected.View = view, true);
                            Event.current.Use();
                        }
                    }
                }) { style = { height = 2 * (size + 18) + 4 } };
                settings.Add(views);
                if (resolved.ViewFallback)
                    settings.Add(new HelpBox("Ракурса «" + ArtAssetLabels.ViewTitle(selected.View) + "» у ассета нет — показан «" +
                                             ArtAssetLabels.ViewTitle(resolved.ShownView) + "» (ближайший по кругу, без зеркалирования).", HelpBoxMessageType.Warning));
                if (resolved.NoArt)
                    settings.Add(new HelpBox("У ассета нет рисунков — показана розовая заглушка.", HelpBoxMessageType.Warning));
            }

            Number("Масштаб экземпляра", selected.Scale, .1f, 4, value => selected.Scale = Mathf.Max(.05f, value),
                "1 — игровой размер ассета. Опора остаётся на месте.");
            Toggle("Отразить по X", selected.FlipX, value => selected.FlipX = value);
            Toggle("Заблокировать", selected.Locked, value => selected.Locked = value);
            Toggle("Скрыть", selected.Hidden, value => selected.Hidden = value);

            Heading("Настройки ассета или свои");
            Help("По умолчанию экземпляр берёт настройки из Базы ассетов. «Настроить для этого экземпляра» включается для одного параметра; изменения каталога остальное обновят.");
            OverrideGroup(selected, asset, LocationAssetOverride.Layer, "Слой основы",
                () => (asset != null ? ArtAssetLabels.LayerTitle(asset.MainPart.Layer) + ", порядок " + asset.MainPart.OrderOffset : "—"),
                () =>
                {
                    PopupField<string> band = new PopupField<string>("Слой", new List<string> { "Земля", "Детали земли", "Объекты и персонажи", "Кроны / крыши" }, (int)selected.Band);
                    band.RegisterValueChangedCallback(evt => Change(() => selected.Band = (LocationVisualBand)band.index));
                    settings.Add(band);
                    Number("Порядок внутри слоя", selected.OrderOffset, -1000, 1000, value => selected.OrderOffset = Mathf.RoundToInt(value));
                },
                () => { selected.Band = LocationVisualResolver.Band(asset.MainPart.Layer); selected.OrderOffset = asset.MainPart.OrderOffset; });
            OverrideGroup(selected, asset, LocationAssetOverride.Passability, "Проходимость",
                () => (resolved.BlocksMovement ? "блокирует, основание " : "не блокирует, основание ") + resolved.FootprintSize.x.ToString("0.##") + "×" + resolved.FootprintSize.y.ToString("0.##"),
                () =>
                {
                    Toggle("Блокирует проход", selected.BlocksMovement, value => selected.BlocksMovement = value);
                    Vector2Field footprint = new Vector2Field("Основание на земле") { value = selected.Footprint };
                    footprint.RegisterValueChangedCallback(evt => Change(() => selected.Footprint = Vector2.Max(Vector2.zero, evt.newValue)));
                    settings.Add(footprint);
                },
                () => { selected.BlocksMovement = resolved.BlocksMovement; selected.Footprint = resolved.FootprintSize; });
            OverrideGroup(selected, asset, LocationAssetOverride.Shadows, "Тени",
                () => (resolved.Main?.ProjectsShadow == true ? "тень-силуэт" : "без тени-силуэта") + ", длина " + resolved.ShadowLength.ToString("0.##") +
                      (resolved.OccludesLight ? ", перекрывает свет" : ""),
                () =>
                {
                    Toggle("Отбрасывает тень-силуэт", selected.ProjectsShadow, value => selected.ProjectsShadow = value);
                    Number("Длина тени (множитель)", selected.ShadowLength, 0, 3, value => selected.ShadowLength = value);
                    Toggle("Перекрывает свет местных источников (по основанию)", selected.CastsShadow, value => selected.CastsShadow = value);
                },
                () =>
                {
                    selected.ProjectsShadow = asset.MainPart.ProjectsShadow; selected.ShadowLength = resolved.ShadowLength; selected.CastsShadow = resolved.OccludesLight;
                });

            Heading("Свет этого предмета");
            LightEditor(selected);
            if (selected.Variants.Count > 0)
            {
                Heading("Состояния рисунка (прежние)");
                Help("Состояния прямого рисунка сохранены при переводе и меняют рисунок основы.");
                foreach (LocationVisualVariant variant in selected.Variants)
                    AddButton(settings, "Показать: " + variant.Name, () => Change(() => selected.DefaultVariantId = variant.Id));
                AddButton(settings, "Основной рисунок", () => Change(() => selected.DefaultVariantId = ""));
            }
            AddButton(settings, "Дублировать предмет", () => Change(() =>
            {
                LocationVisualObject copy = JsonUtility.FromJson<LocationVisualObject>(JsonUtility.ToJson(selected));
                copy.Id = Guid.NewGuid().ToString("N"); copy.GroupId = ""; copy.Position += new Vector2(.03f, .03f);
                visual.Objects.Add(copy); selectedElementId = copy.Id;
            }, true));
            AddButton(settings, "Удалить предмет", () => Change(() => { visual.Objects.Remove(selected); selectedKind = Kind.None; }, true));
        }

        // Группа параметров: унаследовано от ассета или своё (явно).
        private void OverrideGroup(LocationVisualObject selected, ArtAssetDefinition asset, LocationAssetOverride flag, string title,
            Func<string> inherited, Action ownFields, Action copyInherited)
        {
            bool own = selected.IsOverridden(flag);
            UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle(title + ": настроить для этого экземпляра") { value = own };
            toggle.RegisterValueChangedCallback(evt => Change(() =>
            {
                if (evt.newValue)
                {
                    // Начать со значений ассета: включение ничего не меняет на картинке.
                    if (asset != null) copyInherited();
                    selected.Overrides |= flag;
                }
                else selected.Overrides &= ~flag;
            }, true));
            settings.Add(toggle);
            if (own) ownFields();
            else
            {
                Label label = new Label("Из ассета: " + inherited());
                label.style.color = new Color(.65f, .75f, .85f);
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginLeft = 12;
                settings.Add(label);
            }
        }

        private void ReplaceAsset(LocationVisualObject selected, string newId)
        {
            ArtAssetDefinition from = ArtAssetDatabaseAsset.FindCurrent(selected.AssetId), to = ArtAssetDatabaseAsset.FindCurrent(newId);
            if (to == null || newId == selected.AssetId) return;
            List<string> changes = new List<string> { "рисунки всех ракурсов и части (" + to.Parts.Count + ")" };
            if (from == null || Mathf.Abs(from.Height - to.Height) > .01f) changes.Add("игровой размер: " + (from?.Height ?? 0).ToString("0.##") + " → " + to.Height.ToString("0.##"));
            if (!selected.IsOverridden(LocationAssetOverride.Layer) && (from == null || from.MainPart.Layer != to.MainPart.Layer)) changes.Add("слой: " + ArtAssetLabels.LayerTitle(to.MainPart.Layer));
            if (!selected.IsOverridden(LocationAssetOverride.Passability)) changes.Add("проходимость и основание — из нового ассета");
            if (!selected.IsOverridden(LocationAssetOverride.Shadows)) changes.Add("тени — из нового ассета");
            if (!to.HasView(selected.View)) changes.Add("ракурса «" + ArtAssetLabels.ViewTitle(selected.View) + "» нет — будет показан ближайший");
            if (!EditorUtility.DisplayDialog("Заменить ассет?",
                    "«" + (from?.Name ?? selected.AssetId) + "» → «" + to.Name + "»\n\nСохранятся: ID экземпляра, место опоры, ракурс, масштаб, отражение, свет, свои настройки.\n\nОбновятся из нового ассета:\n• " +
                    string.Join("\n• ", changes), "Заменить", "Отмена"))
                return;
            Change(() => { selected.AssetId = newId; if (selected.Name == from?.Name) selected.Name = to.Name; }, true);
            RememberPalette(newId);
        }

        // ------------------------------------------------------------------
        // Перевод прямых рисунков в Базу ассетов
        // ------------------------------------------------------------------

        private void MigrateLocationObjects()
        {
            if (Visual == null) return;
            MigrateObjects(Visual.Objects.ToList(), true);
        }

        private void MigrateObjects(List<LocationVisualObject> items, bool confirm)
        {
            ArtAssetDatabaseAsset catalog = ArtAssetImporter.LoadOrCreateCatalog();
            ArtAssetMigrationReport preview = ArtAssetMigration.Preview(items, catalog);
            if (preview.Created + preview.Reused == 0)
            {
                status.text = "Переводить нечего: " + string.Join("; ", preview.Entries.Select(entry => entry.Item?.Name + " — " + entry.Reason).Take(6));
                return;
            }
            if (confirm)
            {
                string lines = string.Join("\n", preview.Entries.Take(20).Select(entry =>
                    (entry.Action == ArtAssetMigrationAction.Skip ? "— " : "• ") + entry.Item?.Name + ": " + entry.Reason));
                if (!EditorUtility.DisplayDialog("Перевести предметы в Базу ассетов",
                        preview.Summary + "\n\n" + lines + "\n\nРисунок, опора, размер, слой, основание, свет и тени сохраняются. Повторный перевод дубликатов не создаёт.",
                        "Перевести", "Отмена"))
                    return;
            }
            ArtAssetMigrationReport report = ArtAssetMigration.Migrate(items, catalog, database);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(database);
            status.text = report.Summary + (report.Problems.Count > 0 ? " " + string.Join(" ", report.Problems.Take(3)) : "");
            rebuildRequested = true;
            BuildSettings();
        }
    }
}
