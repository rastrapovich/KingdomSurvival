using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены»: программное обозначение
    // оснований, растительности и объектов местности до появления рисунков.
    // Заливки соседних клеток слегка перекрываются (без ярких «наклеек»),
    // следы и детали — стабильные по координатам, без шума каждый кадр.
    // Это временная читаемая графика, не окончательный художественный ассет.
    public sealed partial class HexBoardElement
    {
        private void DrawTerrainSurfaces(Painter2D painter, HexLayout layout)
        {
            if (battle == null)
                return;
            BeginTerrainFrame();
            float radius = layout.Size * (1f - hexStyle.Gap);
            // Основания: слитые пятна по маске клеток (одна заливка на
            // связный участок, без «наклеек» и швов), затем редкие детали.
            foreach (IGrouping<SandboxGround, KeyValuePair<HexCoord, SandboxGround>> group in battle.GroundCells
                         .Where(pair => IsBoardCell(pair.Key)).GroupBy(pair => pair.Value).OrderBy(group => (int)group.Key))
            {
                (Color fill, Color edge) = GroundFill(group.Key);
                if (fill.a > 0f)
                    PaintRegion(painter, layout, group.Select(pair => pair.Key).ToList(), fill, edge);
            }
            foreach (KeyValuePair<HexCoord, SandboxGround> pair in battle.GroundCells)
            {
                if (!IsBoardCell(pair.Key))
                    continue;
                Vector2 c = layout.GetCenter(pair.Key);
                float s = layout.Size;
                switch (pair.Value)
                {
                    case SandboxGround.Mud:
                        Track(painter, layout, pair.Key, c, Tc(0.26f, 0.18f, 0.11f, 0.6f));
                        break;
                    case SandboxGround.Bog:
                        for (int i = 0; i < 3; i++)
                            PaintCircle(painter, c + Jitter(pair.Key, i, s * 0.7f, layout), s * 0.09f, Tc(0.42f, 0.47f, 0.26f, 0.85f), Color.clear, 0f);
                        break;
                    case SandboxGround.Rubble:
                        for (int i = 0; i < 4; i++)
                            PaintCircle(painter, c + Jitter(pair.Key, 10 + i, s * 0.75f, layout), s * 0.07f, Tc(0.42f, 0.40f, 0.37f, 0.85f), Color.clear, 0f);
                        break;
                    case SandboxGround.Ice:
                        if (StableNoise(pair.Key, 30) < 0.5f)
                            Track(painter, layout, pair.Key, c, Tc(0.95f, 0.98f, 1f, 0.7f));
                        break;
                    case SandboxGround.ShallowWater:
                        if (StableNoise(pair.Key, 31) < 0.45f)
                            Track(painter, layout, pair.Key, c, Tc(0.70f, 0.82f, 0.88f, 0.55f));
                        break;
                    case SandboxGround.Cliff:
                        PaintHex(painter, c, radius * 0.7f, Color.clear, Tc(0.10f, 0.09f, 0.08f, 0.9f), 1.6f, false, layout.VerticalScale);
                        break;
                }
            }
            foreach (KeyValuePair<HexCoord, SandboxVegetation> pair in battle.VegetationCells)
            {
                if (!IsBoardCell(pair.Key))
                    continue;
                Vector2 c = layout.GetCenter(pair.Key);
                float s = layout.Size;
                if (pair.Value == SandboxVegetation.Forest)
                {
                    // Группа крон у края клетки — центр и ноги бойца видны.
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 at = c + Jitter(pair.Key, 40 + i, s * 0.9f, layout) + new Vector2(0f, -s * 0.15f);
                        PaintCircle(painter, at, s * (0.20f + 0.06f * StableNoise(pair.Key, 50 + i)), Tc(0.25f, 0.38f, 0.22f, 0.55f), Tc(0.15f, 0.24f, 0.13f, 0.6f), 1f);
                    }
                }
                else if (pair.Value == SandboxVegetation.Brush)
                {
                    for (int i = 0; i < 2; i++)
                        PaintCircle(painter, c + Jitter(pair.Key, 60 + i, s * 0.8f, layout), s * 0.13f, Tc(0.40f, 0.48f, 0.27f, 0.55f), Color.clear, 0f);
                }
            }
            DrawEffectsWithFade(painter, layout);
            foreach (SandboxBattleObject item in battle.TerrainObjects)
            {
                foreach (HexCoord cell in item.Cells.Where(IsBoardCell))
                {
                    // Объект под рисунком MapLab-арены (валун, слот Базы ассетов) — уже нарисован артом.
                    if ((Relief != null && ArtBackedObjects.Contains(cell)) || HasBoardArt(item, cell))
                        continue;
                    DrawTerrainObject(painter, layout, item, cell, radius);
                }
            }
            effectFade = 1f;
            DrawTerrainDebug(painter, layout, radius);
        }

        // Отладка полигона: логические клетки (закрыто — красный, опасность —
        // оранжевый, особая местность — жёлтый) и линия огня.
        public bool DebugShowLogicalCells { get; set; }
        public IReadOnlyList<HexCoord> DebugLineCells { get; set; }
        public bool DebugLineClear { get; set; }

        public void RefreshTerrainDebug() => MarkDirtyRepaint();

        private void DrawTerrainDebug(Painter2D painter, HexLayout layout, float radius)
        {
            if (DebugShowLogicalCells)
            {
                foreach (HexCoord cell in battle.DebugBoardCells().Where(IsBoardCell))
                {
                    string category = battle.DebugCellCategory(cell);
                    Color color = category == "closed" ? new Color(0.85f, 0.20f, 0.18f, 0.9f)
                        : category == "hazard" ? new Color(0.95f, 0.55f, 0.15f, 0.9f)
                        : category == "special" ? new Color(0.90f, 0.82f, 0.30f, 0.75f)
                        : Color.clear;
                    if (color.a > 0f)
                        PaintHex(painter, layout.GetCenter(cell), radius * 0.82f, Color.clear, color, 2f, false, layout.VerticalScale);
                }
            }
            if (DebugLineCells != null && DebugLineCells.Count > 1)
            {
                Color line = DebugLineClear ? new Color(0.45f, 0.85f, 0.95f, 0.9f) : new Color(0.95f, 0.25f, 0.25f, 0.9f);
                foreach (HexCoord cell in DebugLineCells.Where(IsBoardCell))
                    PaintCircle(painter, layout.GetCenter(cell), layout.Size * 0.12f, line, Color.clear, 0f);
            }
        }

        private void DrawTerrainObject(Painter2D painter, HexLayout layout, SandboxBattleObject item, HexCoord cell, float radius)
        {
            Vector2 c = layout.GetCenter(cell);
            float s = layout.Size;
            switch (item.Kind)
            {
                case SandboxObjectKind.Boulder:
                    PaintCircle(painter, c, s * 0.48f, Tc(0.52f, 0.50f, 0.47f, 0.95f), Tc(0.20f, 0.18f, 0.16f, 0.95f), 2f);
                    break;
                case SandboxObjectKind.Trunk:
                    PaintCircle(painter, c, s * 0.34f, Tc(0.38f, 0.27f, 0.17f, 0.95f), Tc(0.18f, 0.12f, 0.07f, 0.95f), 2f);
                    PaintCircle(painter, c, s * 0.16f, Tc(0.55f, 0.42f, 0.27f, 0.9f), Color.clear, 0f);
                    break;
                case SandboxObjectKind.FallenLog:
                    PaintSegment(painter, c + new Vector2(-s * 0.55f, s * 0.1f), c + new Vector2(s * 0.55f, -s * 0.1f), Tc(0.42f, 0.30f, 0.18f, 0.9f), s * 0.18f);
                    break;
                case SandboxObjectKind.Deadfall:
                    PaintSegment(painter, c + new Vector2(-s * 0.6f, s * 0.25f), c + new Vector2(s * 0.6f, -s * 0.2f), Tc(0.36f, 0.25f, 0.15f, 0.95f), s * 0.2f);
                    PaintSegment(painter, c + new Vector2(-s * 0.5f, -s * 0.25f), c + new Vector2(s * 0.55f, s * 0.25f), Tc(0.32f, 0.22f, 0.13f, 0.95f), s * 0.17f);
                    break;
                case SandboxObjectKind.Wattle:
                    for (int i = -2; i <= 2; i++)
                        PaintSegment(painter, c + new Vector2(i * s * 0.2f, s * 0.25f), c + new Vector2(i * s * 0.2f, -s * 0.3f), Tc(0.45f, 0.33f, 0.20f, 0.95f), 2.2f);
                    PaintSegment(painter, c + new Vector2(-s * 0.5f, -s * 0.05f), c + new Vector2(s * 0.5f, -s * 0.05f), Tc(0.56f, 0.42f, 0.26f, 0.95f), 3f);
                    break;
                case SandboxObjectKind.StoneWall:
                    PaintSegment(painter, c + new Vector2(-s * 0.6f, 0f), c + new Vector2(s * 0.6f, 0f), Tc(0.55f, 0.53f, 0.50f, 0.95f), s * 0.26f);
                    break;
                case SandboxObjectKind.Bridge:
                    PaintHex(painter, c, radius * 0.94f, Tc(0.60f, 0.47f, 0.30f, 0.85f), Tc(0.32f, 0.23f, 0.13f, 0.9f), 1.4f, true, layout.VerticalScale);
                    for (int i = -1; i <= 1; i++)
                        PaintSegment(painter, c + new Vector2(-s * 0.7f, i * s * 0.25f * layout.VerticalScale), c + new Vector2(s * 0.7f, i * s * 0.25f * layout.VerticalScale), Tc(0.38f, 0.28f, 0.16f, 0.6f), 1.2f);
                    break;
                case SandboxObjectKind.RootNest:
                    for (int i = 0; i < 4; i++)
                        PaintSegment(painter, c, c + Jitter(cell, 70 + i, s * 1.3f, layout), Tc(0.30f, 0.22f, 0.12f, 0.95f), 3f);
                    PaintCircle(painter, c, s * 0.2f, Tc(0.24f, 0.17f, 0.09f, 0.95f), Color.clear, 0f);
                    break;
                case SandboxObjectKind.RitualStone:
                    PaintCircle(painter, c + new Vector2(0f, -s * 0.15f), s * 0.3f, Tc(0.45f, 0.45f, 0.48f, 0.95f), Tc(0.18f, 0.18f, 0.2f, 0.95f), 2f);
                    PaintCircle(painter, c + new Vector2(0f, s * 0.15f), s * 0.3f, Tc(0.45f, 0.45f, 0.48f, 0.95f), Color.clear, 0f);
                    break;
            }
        }

        // Временные поверхности: немного крупных форм, бойцов не закрывают.
        private void DrawCellEffect(Painter2D painter, HexLayout layout, HexCoord cell, SandboxCellEffect effect)
        {
            Vector2 c = layout.GetCenter(cell);
            float s = layout.Size;
            switch (effect.Kind)
            {
                case SandboxEffectKind.Fire:
                    if (!effectFillDone) PaintHex(painter, c, s, EffectFill(effect.Kind), Color.clear, 0f, true, layout.VerticalScale);
                    for (int i = 0; i < 2; i++)
                        PaintCircle(painter, c + Jitter(cell, 80 + i, s * 0.7f, layout), s * 0.14f, Tc(0.98f, 0.62f, 0.18f, 0.75f), Color.clear, 0f);
                    break;
                case SandboxEffectKind.Tar:
                    if (!effectFillDone) PaintHex(painter, c, s, EffectFill(effect.Kind), Color.clear, 0f, true, layout.VerticalScale);
                    break;
                case SandboxEffectKind.Web:
                    for (int i = 0; i < 3; i++)
                        PaintSegment(painter, c + Jitter(cell, 90 + i, s * 1.2f, layout), c + Jitter(cell, 95 + i, s * 1.2f, layout), Tc(0.86f, 0.86f, 0.82f, 0.7f), 1.2f);
                    break;
                case SandboxEffectKind.Roots:
                    for (int i = 0; i < 3; i++)
                        PaintSegment(painter, c, c + Jitter(cell, 110 + i, s * 1.2f, layout), Tc(0.34f, 0.24f, 0.12f, 0.85f), 2.4f);
                    break;
                case SandboxEffectKind.Thorns:
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 at = c + Jitter(cell, 120 + i, s * 0.9f, layout);
                        PaintSegment(painter, at, at + new Vector2(0f, -s * 0.18f), Tc(0.30f, 0.40f, 0.18f, 0.9f), 1.6f);
                    }
                    break;
                case SandboxEffectKind.Poison:
                    if (!effectFillDone) PaintHex(painter, c, s, EffectFill(effect.Kind), Color.clear, 0f, true, layout.VerticalScale);
                    break;
                case SandboxEffectKind.Smoke:
                    PaintCircle(painter, c + new Vector2(0f, -s * 0.2f), s * 0.6f, Tc(0.55f, 0.55f, 0.55f, 0.32f), Color.clear, 0f);
                    break;
                case SandboxEffectKind.Ice:
                    if (!effectFillDone) PaintHex(painter, c, s, EffectFill(effect.Kind), Color.clear, 0f, true, layout.VerticalScale);
                    Track(painter, layout, cell, c, Tc(1f, 1f, 1f, 0.7f));
                    break;
                case SandboxEffectKind.Electric:
                    if (StableNoise(cell, 130) < 0.6f)
                    {
                        Vector2 a = c + Jitter(cell, 131, s * 0.6f, layout);
                        Vector2 b = a + new Vector2(s * 0.18f, -s * 0.12f);
                        PaintSegment(painter, a, b, Tc(1f, 0.92f, 0.45f, 0.9f), 1.6f);
                        PaintSegment(painter, b, b + new Vector2(-s * 0.08f, -s * 0.14f), Tc(1f, 0.92f, 0.45f, 0.9f), 1.6f);
                    }
                    break;
            }
        }

        private void Track(Painter2D painter, HexLayout layout, HexCoord cell, Vector2 center, Color color)
        {
            float angle = -0.4f + StableNoise(cell, 5) * 0.8f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * layout.VerticalScale) * layout.Size * 0.42f;
            Vector2 shift = new Vector2(StableNoise(cell, 6) - 0.5f, (StableNoise(cell, 7) - 0.5f) * layout.VerticalScale) * layout.Size * 0.35f;
            PaintSegment(painter, center + shift - dir, center + shift + dir, color, 1.5f);
        }

        private Vector2 Jitter(HexCoord cell, int salt, float span, HexLayout layout) =>
            new Vector2(StableNoise(cell, salt) - 0.5f, (StableNoise(cell, salt + 100) - 0.5f) * layout.VerticalScale) * span;
    }
}
