using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 4: инструмент
    // полигона — пресет и seed арены, кисть оснований/объектов/поверхностей,
    // воздействия огнём/холодом/водой/током, очистка временного, показ
    // логических клеток и линии огня. Отладочный режим, не способность героя;
    // в бою кампании панели нет.
    public sealed partial class BattleSandboxController
    {
        // 25.11.2026, просьба пользователя: правила «Генератора арен» — во всех
        // боях на арене; прежний генератор без темы из полигона убран, по
        // умолчанию — случайная обычная тема (по seed, если он задан).
        private const string RandomThemeLabel = "Случайная тема";
        private string sandboxThemeLabel = RandomThemeLabel;
        private string sandboxSeedText = string.Empty;

        private bool terrainBrushEnabled;
        private string terrainBrush = "Огонь";
        private int terrainBrushDuration = 2;
        private Label terrainDebugInfo;
        private HexCoord? terrainDebugCell;

        private static readonly string[] BrushNames =
        {
            "Огонь", "Смола", "Паутина", "Корни", "Колючки", "Яд", "Дым", "Лёд (поверхность)",
            "Воздействие: огонь", "Воздействие: холод", "Воздействие: вода", "Воздействие: ток",
            "Основание: земля", "Основание: грязь", "Основание: топь", "Основание: мелководье", "Основание: глубокая вода",
            "Основание: щебень", "Основание: глубокий снег", "Основание: лёд", "Основание: обрыв",
            "Холм", "Убрать холм", "Лес", "Заросли", "Убрать растительность",
            "Валун", "Толстый ствол", "Поваленное дерево", "Завал", "Плетень", "Каменная стенка", "Мост",
            "Корневое гнездо", "Обрядовый камень", "Убрать объект",
            "Линия огня от текущего бойца"
        };

        private void AddArenaThemeControls(VisualElement center)
        {
            List<string> options = new List<string> { RandomThemeLabel };
            options.AddRange(SandboxArenaThemes.All.Select(SandboxArenaThemes.Label));
            DropdownField theme = new DropdownField("Арена", options, Math.Max(0, options.IndexOf(sandboxThemeLabel))) { name = "sandbox-arena-theme" };
            theme.style.marginTop = 8f;
            theme.style.color = new Color(0.70f, 0.68f, 0.62f, 1f);
            theme.RegisterValueChangedCallback(evt => sandboxThemeLabel = evt.newValue);
            center.Add(theme);

            TextField seed = new TextField("Seed (пусто — случайный)") { value = sandboxSeedText, name = "sandbox-arena-seed" };
            seed.style.color = new Color(0.70f, 0.68f, 0.62f, 1f);
            seed.RegisterValueChangedCallback(evt => sandboxSeedText = evt.newValue);
            center.Add(seed);
        }

        private SandboxArenaTheme? SandboxThemeChoice()
        {
            foreach (SandboxArenaTheme theme in SandboxArenaThemes.All)
            {
                if (SandboxArenaThemes.Label(theme) == sandboxThemeLabel)
                    return theme;
            }
            // Случайная обычная тема: при заданном seed — одна и та же.
            int? seed = SandboxSeedChoice();
            System.Random random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            return SandboxArenaThemes.Ordinary[random.Next(SandboxArenaThemes.Ordinary.Length)];
        }

        private int? SandboxSeedChoice() =>
            int.TryParse((sandboxSeedText ?? string.Empty).Trim(), out int value) ? value : (int?)null;

        // Панель поверх экрана боя (не в боковой колонке: оформление боя
        // опирается на порядок её детей).
        private void AddTerrainDebugPanel()
        {
            if (campaignBattle != null || root == null)
                return;
            VisualElement panel = new VisualElement { name = "terrain-debug-panel" };
            panel.style.position = Position.Absolute;
            panel.style.left = 16f;
            panel.style.bottom = 16f;
            panel.style.width = 300f;
            panel.style.paddingLeft = 8f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 6f;
            panel.style.paddingBottom = 6f;
            panel.style.backgroundColor = new Color(0.06f, 0.07f, 0.08f, 0.92f);
            SetBorder(panel, new Color(0.30f, 0.30f, 0.28f, 1f));
            SetRadius(panel, 4f);

            VisualElement body = new VisualElement();
            body.style.display = DisplayStyle.None;
            Button toggle = new Button { text = "ОТЛАДКА МЕСТНОСТИ ▸" };
            StyleSecondaryButton(toggle);
            toggle.clicked += () =>
            {
                bool open = body.style.display == DisplayStyle.None;
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                toggle.text = open ? "ОТЛАДКА МЕСТНОСТИ ▾" : "ОТЛАДКА МЕСТНОСТИ ▸";
                if (!open)
                    terrainBrushEnabled = false;
            };
            panel.Add(toggle);
            panel.Add(body);

            Label summary = CreateLabel(battle?.ArenaReport != null ? battle.ArenaReport.Summary() : "Арена: без отчёта генератора.",
                10, new Color(0.62f, 0.64f, 0.60f, 1f));
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.marginTop = 4f;
            body.Add(summary);

            Toggle brushOn = new Toggle("Кисть: клик по клетке применяет") { value = terrainBrushEnabled };
            brushOn.RegisterValueChangedCallback(evt => terrainBrushEnabled = evt.newValue);
            body.Add(brushOn);

            DropdownField brush = new DropdownField("Кисть", BrushNames.ToList(), Math.Max(0, Array.IndexOf(BrushNames, terrainBrush)));
            brush.RegisterValueChangedCallback(evt => terrainBrush = evt.newValue);
            body.Add(brush);

            List<string> durations = new List<string> { "постоянно", "1", "2", "3", "5" };
            DropdownField duration = new DropdownField("Срок, раундов", durations,
                Math.Max(0, durations.IndexOf(terrainBrushDuration == 0 ? "постоянно" : terrainBrushDuration.ToString())));
            duration.RegisterValueChangedCallback(evt => terrainBrushDuration = int.TryParse(evt.newValue, out int d) ? d : 0);
            body.Add(duration);

            Toggle logic = new Toggle("Показать логические клетки") { value = board != null && board.DebugShowLogicalCells };
            logic.RegisterValueChangedCallback(evt =>
            {
                if (board == null)
                    return;
                board.DebugShowLogicalCells = evt.newValue;
                board.RefreshTerrainDebug();
            });
            body.Add(logic);

            Button clear = new Button(() =>
            {
                int removed = battle != null ? battle.DebugClearEffects() : 0;
                TerrainDebugLog("Снято временных областей: " + removed + ".");
            }) { text = "ОЧИСТИТЬ ВРЕМЕННОЕ" };
            StyleSecondaryButton(clear);
            clear.style.marginTop = 4f;
            body.Add(clear);

            terrainDebugInfo = CreateLabel("Красный — закрыто, оранжевый — опасность, жёлтый — особая местность.", 10, new Color(0.70f, 0.72f, 0.68f, 1f));
            terrainDebugInfo.style.whiteSpace = WhiteSpace.Normal;
            terrainDebugInfo.style.marginTop = 4f;
            body.Add(terrainDebugInfo);

            root.Add(panel);
        }

        // true — клик забрала кисть отладки.
        private bool TryHandleTerrainDebugClick(HexCoord cell)
        {
            if (!terrainBrushEnabled || campaignBattle != null || battle == null)
                return false;
            terrainDebugCell = cell;
            string note = ApplyTerrainBrush(cell);
            TerrainDebugLog(note + " " + battle.DescribeCell(battle.CurrentUnit, cell));
            return true;
        }

        private string ApplyTerrainBrush(HexCoord cell)
        {
            int duration = terrainBrushDuration;
            switch (terrainBrush)
            {
                case "Огонь": return Effect(cell, SandboxEffectKind.Fire, duration);
                case "Смола": return Effect(cell, SandboxEffectKind.Tar, duration);
                case "Паутина": return Effect(cell, SandboxEffectKind.Web, duration);
                case "Корни": return Effect(cell, SandboxEffectKind.Roots, duration);
                case "Колючки": return Effect(cell, SandboxEffectKind.Thorns, duration);
                case "Яд": return Effect(cell, SandboxEffectKind.Poison, duration);
                case "Дым": return Effect(cell, SandboxEffectKind.Smoke, duration);
                case "Лёд (поверхность)": return Effect(cell, SandboxEffectKind.Ice, duration);
                case "Воздействие: огонь": return Impulse(cell, SandboxImpulse.Fire, duration);
                case "Воздействие: холод": return Impulse(cell, SandboxImpulse.Cold, duration);
                case "Воздействие: вода": return Impulse(cell, SandboxImpulse.Water, duration);
                case "Воздействие: ток": return Impulse(cell, SandboxImpulse.Electric, duration);
                case "Основание: земля": return Ground(cell, SandboxGround.Default);
                case "Основание: грязь": return Ground(cell, SandboxGround.Mud);
                case "Основание: топь": return Ground(cell, SandboxGround.Bog);
                case "Основание: мелководье": return Ground(cell, SandboxGround.ShallowWater);
                case "Основание: глубокая вода": return Ground(cell, SandboxGround.DeepWater);
                case "Основание: щебень": return Ground(cell, SandboxGround.Rubble);
                case "Основание: глубокий снег": return Ground(cell, SandboxGround.DeepSnow);
                case "Основание: лёд": return Ground(cell, SandboxGround.Ice);
                case "Основание: обрыв": return Ground(cell, SandboxGround.Cliff);
                case "Холм": battle.DebugSetHill(cell, true); return "Холм поставлен.";
                case "Убрать холм": battle.DebugSetHill(cell, false); return "Холм снят.";
                case "Лес": battle.SetVegetation(cell, SandboxVegetation.Forest); return "Лес.";
                case "Заросли": battle.SetVegetation(cell, SandboxVegetation.Brush); return "Заросли.";
                case "Убрать растительность": battle.SetVegetation(cell, SandboxVegetation.None); return "Растительность снята.";
                case "Валун": return Place(cell, SandboxObjectKind.Boulder);
                case "Толстый ствол": return Place(cell, SandboxObjectKind.Trunk);
                case "Поваленное дерево": return Place(cell, SandboxObjectKind.FallenLog);
                case "Завал": return Place(cell, SandboxObjectKind.Deadfall);
                case "Плетень": return Place(cell, SandboxObjectKind.Wattle);
                case "Каменная стенка": return Place(cell, SandboxObjectKind.StoneWall);
                case "Мост": return Place(cell, SandboxObjectKind.Bridge);
                case "Корневое гнездо": return Place(cell, SandboxObjectKind.RootNest);
                case "Обрядовый камень": return Place(cell, SandboxObjectKind.RitualStone);
                case "Убрать объект": return battle.DebugRemoveObject(cell) ? "Объект убран." : "На клетке нет объекта.";
                case "Линия огня от текущего бойца": return LineOfFire(cell);
                default: return string.Empty;
            }
        }

        private string Effect(HexCoord cell, SandboxEffectKind kind, int duration) =>
            battle.AddEffect(cell, kind, duration) != null ? SandboxHazardRules.Label(kind) + " поставлен(а)." : "Сюда нельзя: " + SandboxHazardRules.Label(kind) + ".";

        private string Impulse(HexCoord cell, SandboxImpulse impulse, int duration)
        {
            List<string> notes = battle.ApplyImpulse(impulse, new[] { cell }, duration);
            return notes.Count > 0 ? string.Join(" ", notes) : "Без изменений.";
        }

        private string Ground(HexCoord cell, SandboxGround ground)
        {
            if ((ground == SandboxGround.DeepWater || ground == SandboxGround.Cliff) && battle.GetUnitAt(cell) != null)
                return "На клетке боец — закрытое основание не ставится.";
            battle.DebugSetGround(cell, ground);
            return "Основание: " + SandboxSurfaceLabels.Ground(ground) + ".";
        }

        private string Place(HexCoord cell, SandboxObjectKind kind) =>
            battle.DebugPlaceObject(cell, kind) ? SandboxTerrainObjects.Get(kind).Label + " поставлен." : "На клетке боец.";

        private string LineOfFire(HexCoord cell)
        {
            SandboxUnitState current = battle.CurrentUnit;
            if (current == null || board == null)
                return "Нет текущего бойца.";
            SandboxLineOfFire line = battle.GetLineOfFire(current.Position, cell);
            board.DebugLineCells = SandboxBattle.SymmetricLine(current.Position, cell);
            board.DebugLineClear = line.Clear;
            return "Линия огня " + current.DisplayLabel + " → " + cell + ": " +
                   (line.Clear ? "свободна" + (line.LowCover ? ", укрытие цели низким объектом" : string.Empty) : "закрыта — " + line.Reason) + ".";
        }

        private void TerrainDebugLog(string text)
        {
            if (terrainDebugInfo != null)
                terrainDebugInfo.text = text;
            board?.RefreshTerrainDebug();
            RefreshBattleScreen();
        }
    }
}
