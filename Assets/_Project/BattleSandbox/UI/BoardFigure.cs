using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-12М: одна фигура поля боя для внешнего рендерера (бой на месте рисует
    // фигуры рендерер места под своим светом). Координаты — поля (HexBoardElement).
    public struct BoardFigure
    {
        public string UnitId;
        public Sprite Sprite;
        // Прямоугольник картинки на поле.
        public Rect Rect;
        public bool Mirrored;
        // Миниатюра без набора анимаций: вписана в прямоугольник с сохранением пропорций.
        public bool FitInside;
        public Color Tint;
        // Точка земли (центр гекса с учётом шага и удара).
        public Vector2 Ground;
        public bool Corpse;
    }
}
