using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    // Слой правки над BattlefieldView: образцы состояний, клетки, которых
    // в бою нет, и наведение. Один и тот же вид в «Базе полей боя» и в
    // «Базе локаций».
    public static class BattlefieldHexPainter
    {
        public static readonly Color DisabledFill = new Color(0.45f, 0.06f, 0.06f, 0.45f);
        public static readonly Color DisabledLine = new Color(0.95f, 0.36f, 0.30f, 0.90f);
        public static readonly Color HoverLine = new Color(1f, 1f, 1f, 0.85f);

        // Образцы состояний для предпросмотра — как в бою.
        private static readonly Vector2Int[] SampleDifficult = { new Vector2Int(3, 2), new Vector2Int(4, 2) };
        private static readonly Vector2Int[] SampleImpassable = { new Vector2Int(6, 4) };
        private static readonly Vector2Int[] SampleReachable = { new Vector2Int(2, 3), new Vector2Int(3, 3), new Vector2Int(2, 4) };
        private static readonly Vector2Int SampleTarget = new Vector2Int(7, 3);
        private static readonly Vector2Int SampleHover = new Vector2Int(6, 2);

        public static float Radius(BattlefieldGridLayout layout, BattlefieldHexStyle style) => layout.Size * (1f - style.Gap);

        // hidden — клетки, которых в бою нет: на них образцы не рисуются.
        public static void DrawStateSamples(
            Painter2D painter,
            BattlefieldGridLayout layout,
            BattlefieldHexStyle style,
            Func<int, int, bool> hidden)
        {
            float radius = Radius(layout, style);
            foreach (Vector2Int cell in SampleDifficult)
                FillSample(painter, layout, style, cell, style.DifficultColor, hidden);
            foreach (Vector2Int cell in SampleImpassable)
                FillSample(painter, layout, style, cell, style.ImpassableColor, hidden);
            FillSample(painter, layout, style, SampleTarget, style.TargetColor, hidden);
            foreach (Vector2Int cell in SampleReachable)
            {
                if (hidden(cell.x, cell.y))
                    continue;
                painter.strokeColor = style.ReachableColor;
                painter.lineWidth = style.ReachableLineWidth;
                BattlefieldFrame.FillHexPath(painter, layout.GetCenter(cell.x, cell.y),
                    Mathf.Max(1f, radius - style.ReachableLineWidth * 0.5f), layout.VerticalScale);
                painter.Stroke();
            }
            if (!hidden(SampleHover.x, SampleHover.y))
            {
                painter.fillColor = style.AttackHoverFill;
                painter.strokeColor = style.AttackHoverLine;
                painter.lineWidth = 3f;
                BattlefieldFrame.FillHexPath(painter, layout.GetCenter(SampleHover.x, SampleHover.y),
                    Mathf.Max(1f, radius - 2.5f), layout.VerticalScale);
                painter.Fill();
                painter.Stroke();
            }
        }

        // Клетка с цветом состояния и линией сетки — как в бою.
        public static void FillState(Painter2D painter, BattlefieldGridLayout layout, BattlefieldHexStyle style, int q, int r, Color fill)
        {
            painter.fillColor = fill;
            BattlefieldFrame.FillHexPath(painter, layout.GetCenter(q, r), Radius(layout, style), layout.VerticalScale);
            painter.Fill();
            Color line = style.Fade(style.LineColor);
            if (style.LineWidth > 0.01f && line.a > 0.001f)
            {
                painter.strokeColor = line;
                painter.lineWidth = style.LineWidth;
                painter.Stroke();
            }
        }

        // Клетка, которой в бою нет: красная заливка с крестом.
        public static void DrawDisabled(Painter2D painter, BattlefieldGridLayout layout, BattlefieldHexStyle style, int q, int r)
        {
            float radius = Radius(layout, style);
            Vector2 center = layout.GetCenter(q, r);
            painter.fillColor = DisabledFill;
            painter.strokeColor = DisabledLine;
            painter.lineWidth = 1.5f;
            BattlefieldFrame.FillHexPath(painter, center, radius, layout.VerticalScale);
            painter.Fill();
            painter.Stroke();
            float arm = radius * 0.32f;
            painter.BeginPath();
            painter.MoveTo(center + new Vector2(-arm, -arm * layout.VerticalScale));
            painter.LineTo(center + new Vector2(arm, arm * layout.VerticalScale));
            painter.MoveTo(center + new Vector2(arm, -arm * layout.VerticalScale));
            painter.LineTo(center + new Vector2(-arm, arm * layout.VerticalScale));
            painter.Stroke();
        }

        public static void DrawHover(Painter2D painter, BattlefieldGridLayout layout, BattlefieldHexStyle style, int q, int r)
        {
            painter.strokeColor = HoverLine;
            painter.lineWidth = 2f;
            BattlefieldFrame.FillHexPath(painter, layout.GetCenter(q, r), Radius(layout, style), layout.VerticalScale);
            painter.Stroke();
        }

        private static void FillSample(
            Painter2D painter,
            BattlefieldGridLayout layout,
            BattlefieldHexStyle style,
            Vector2Int cell,
            Color fill,
            Func<int, int, bool> hidden)
        {
            if (!hidden(cell.x, cell.y))
                FillState(painter, layout, style, cell.x, cell.y, fill);
        }
    }
}
