using System;
using System.Collections.Generic;
using KingdomSurvival.LocationRendering;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    // Отметки поля (HexBoardElement.Marks) → отметки рендерера места: точки
    // поля переводятся в пиксели рисунка места, радиусы и толщины — по
    // масштабу перевода у каждой отметки.
    public static class BoardMarksInWorld
    {
        public static void Convert(IReadOnlyList<BoardMark> marks, Func<Vector2, Vector2> toCanvas, List<LocationWorldRenderer.BattleMark> into)
        {
            into.Clear();
            if (marks == null) return;
            foreach (BoardMark mark in marks)
            {
                Vector2 a = toCanvas(mark.A);
                float scale = Mathf.Abs(toCanvas(mark.A + Vector2.right).x - a.x);
                into.Add(new LocationWorldRenderer.BattleMark
                {
                    Kind = mark.Kind == BoardMarkKind.Hex ? LocationWorldRenderer.BattleMarkKind.Hex
                        : mark.Kind == BoardMarkKind.Circle ? LocationWorldRenderer.BattleMarkKind.Circle
                        : LocationWorldRenderer.BattleMarkKind.Segment,
                    A = a,
                    B = mark.Kind == BoardMarkKind.Segment ? toCanvas(mark.B) : a,
                    Radius = mark.Radius * scale,
                    VerticalScale = mark.VerticalScale,
                    LineWidth = mark.LineWidth * scale,
                    Fill = mark.Fill,
                    Line = mark.Line
                });
            }
        }
    }
}
