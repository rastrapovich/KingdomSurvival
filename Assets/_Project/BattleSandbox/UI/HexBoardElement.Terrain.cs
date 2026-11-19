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
            float radius = layout.Size * (1f - hexStyle.Gap);
            foreach (KeyValuePair<HexCoord, SandboxGround> pair in battle.GroundCells)
            {
                if (!IsBoardCell(pair.Key))
                    continue;
                Vector2 c = layout.GetCenter(pair.Key);
                float s = layout.Size;
                switch (pair.Value)
                {
                    case SandboxGround.Mud:
                        PaintHex(painter, c, s * 1.03f, new Color(0.40f, 0.29f, 0.18f, 0.42f), Color.clear, 0f, true, layout.VerticalScale);
                        Track(painter, layout, pair.Key, c, new Color(0.26f, 0.18f, 0.11f, 0.6f));
                        break;
                    case SandboxGround.Bog:
                        PaintHex(painter, c, s * 1.03f, new Color(0.20f, 0.24f, 0.17f, 0.62f), Color.clear, 0f, true, layout.VerticalScale);
                        for (int i = 0; i < 3; i++)
                            PaintCircle(painter, c + Jitter(pair.Key, i, s * 0.7f, layout), s * 0.09f, new Color(0.42f, 0.47f, 0.26f, 0.85f), Color.clear, 0f);
                        break;
                    case SandboxGround.Rubble:
                        PaintHex(painter, c, s * 1.02f, new Color(0.50f, 0.48f, 0.44f, 0.30f), Color.clear, 0f, true, layout.VerticalScale);
                        for (int i = 0; i < 4; i++)
                            PaintCircle(painter, c + Jitter(pair.Key, 10 + i, s * 0.75f, layout), s * 0.07f, new Color(0.42f, 0.40f, 0.37f, 0.85f), Color.clear, 0f);
                        break;
                    case SandboxGround.DeepSnow:
                        PaintHex(painter, c, s * 1.03f, new Color(0.92f, 0.93f, 0.95f, 0.55f), Color.clear, 0f, true, layout.VerticalScale);
                        break;
                    case SandboxGround.Ice:
                        PaintHex(painter, c, s * 1.03f, new Color(0.72f, 0.83f, 0.88f, 0.55f), Color.clear, 0f, true, layout.VerticalScale);
                        if (StableNoise(pair.Key, 30) < 0.5f)
                            Track(painter, layout, pair.Key, c, new Color(0.95f, 0.98f, 1f, 0.7f));
                        break;
                    case SandboxGround.DeepWater:
                        PaintHex(painter, c, s * 1.04f, new Color(0.13f, 0.22f, 0.30f, 0.78f), Color.clear, 0f, true, layout.VerticalScale);
                        break;
                    case SandboxGround.Cliff:
                        PaintHex(painter, c, radius * 0.96f, new Color(0.22f, 0.21f, 0.20f, 0.85f), new Color(0.10f, 0.09f, 0.08f, 0.9f), 1.6f, true, layout.VerticalScale);
                        break;
                    case SandboxGround.Road:
                        PaintHex(painter, c, s * 1.02f, new Color(0.70f, 0.60f, 0.43f, 0.30f), Color.clear, 0f, true, layout.VerticalScale);
                        break;
                    case SandboxGround.Sand:
                        PaintHex(painter, c, s * 1.02f, new Color(0.82f, 0.74f, 0.52f, 0.38f), Color.clear, 0f, true, layout.VerticalScale);
                        break;
                    case SandboxGround.WetGround:
                        PaintHex(painter, c, s * 1.02f, new Color(0.33f, 0.27f, 0.20f, 0.30f), Color.clear, 0f, true, layout.VerticalScale);
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
                        PaintCircle(painter, at, s * (0.20f + 0.06f * StableNoise(pair.Key, 50 + i)), new Color(0.25f, 0.38f, 0.22f, 0.55f), new Color(0.15f, 0.24f, 0.13f, 0.6f), 1f);
                    }
                }
                else if (pair.Value == SandboxVegetation.Brush)
                {
                    for (int i = 0; i < 2; i++)
                        PaintCircle(painter, c + Jitter(pair.Key, 60 + i, s * 0.8f, layout), s * 0.13f, new Color(0.40f, 0.48f, 0.27f, 0.55f), Color.clear, 0f);
                }
            }
            foreach (SandboxBattleObject item in battle.TerrainObjects)
            {
                foreach (HexCoord cell in item.Cells.Where(IsBoardCell))
                    DrawTerrainObject(painter, layout, item, cell, radius);
            }
        }

        private void DrawTerrainObject(Painter2D painter, HexLayout layout, SandboxBattleObject item, HexCoord cell, float radius)
        {
            Vector2 c = layout.GetCenter(cell);
            float s = layout.Size;
            switch (item.Kind)
            {
                case SandboxObjectKind.Boulder:
                    PaintCircle(painter, c, s * 0.48f, new Color(0.52f, 0.50f, 0.47f, 0.95f), new Color(0.20f, 0.18f, 0.16f, 0.95f), 2f);
                    break;
                case SandboxObjectKind.Trunk:
                    PaintCircle(painter, c, s * 0.34f, new Color(0.38f, 0.27f, 0.17f, 0.95f), new Color(0.18f, 0.12f, 0.07f, 0.95f), 2f);
                    PaintCircle(painter, c, s * 0.16f, new Color(0.55f, 0.42f, 0.27f, 0.9f), Color.clear, 0f);
                    break;
                case SandboxObjectKind.FallenLog:
                    PaintSegment(painter, c + new Vector2(-s * 0.55f, s * 0.1f), c + new Vector2(s * 0.55f, -s * 0.1f), new Color(0.42f, 0.30f, 0.18f, 0.9f), s * 0.18f);
                    break;
                case SandboxObjectKind.Deadfall:
                    PaintSegment(painter, c + new Vector2(-s * 0.6f, s * 0.25f), c + new Vector2(s * 0.6f, -s * 0.2f), new Color(0.36f, 0.25f, 0.15f, 0.95f), s * 0.2f);
                    PaintSegment(painter, c + new Vector2(-s * 0.5f, -s * 0.25f), c + new Vector2(s * 0.55f, s * 0.25f), new Color(0.32f, 0.22f, 0.13f, 0.95f), s * 0.17f);
                    break;
                case SandboxObjectKind.Wattle:
                    for (int i = -2; i <= 2; i++)
                        PaintSegment(painter, c + new Vector2(i * s * 0.2f, s * 0.25f), c + new Vector2(i * s * 0.2f, -s * 0.3f), new Color(0.45f, 0.33f, 0.20f, 0.95f), 2.2f);
                    PaintSegment(painter, c + new Vector2(-s * 0.5f, -s * 0.05f), c + new Vector2(s * 0.5f, -s * 0.05f), new Color(0.56f, 0.42f, 0.26f, 0.95f), 3f);
                    break;
                case SandboxObjectKind.StoneWall:
                    PaintSegment(painter, c + new Vector2(-s * 0.6f, 0f), c + new Vector2(s * 0.6f, 0f), new Color(0.55f, 0.53f, 0.50f, 0.95f), s * 0.26f);
                    break;
                case SandboxObjectKind.Bridge:
                    PaintHex(painter, c, radius * 0.94f, new Color(0.60f, 0.47f, 0.30f, 0.85f), new Color(0.32f, 0.23f, 0.13f, 0.9f), 1.4f, true, layout.VerticalScale);
                    for (int i = -1; i <= 1; i++)
                        PaintSegment(painter, c + new Vector2(-s * 0.7f, i * s * 0.25f * layout.VerticalScale), c + new Vector2(s * 0.7f, i * s * 0.25f * layout.VerticalScale), new Color(0.38f, 0.28f, 0.16f, 0.6f), 1.2f);
                    break;
                case SandboxObjectKind.RootNest:
                    for (int i = 0; i < 4; i++)
                        PaintSegment(painter, c, c + Jitter(cell, 70 + i, s * 1.3f, layout), new Color(0.30f, 0.22f, 0.12f, 0.95f), 3f);
                    PaintCircle(painter, c, s * 0.2f, new Color(0.24f, 0.17f, 0.09f, 0.95f), Color.clear, 0f);
                    break;
                case SandboxObjectKind.RitualStone:
                    PaintCircle(painter, c + new Vector2(0f, -s * 0.15f), s * 0.3f, new Color(0.45f, 0.45f, 0.48f, 0.95f), new Color(0.18f, 0.18f, 0.2f, 0.95f), 2f);
                    PaintCircle(painter, c + new Vector2(0f, s * 0.15f), s * 0.3f, new Color(0.45f, 0.45f, 0.48f, 0.95f), Color.clear, 0f);
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
