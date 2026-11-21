using System;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // Вид гекса на поле боя: картинки, подложка, линия и цвета состояний.
    // Один стиль — общий для базы; поле может завести свой.
    [Serializable]
    public sealed class BattlefieldHexStyle
    {
        [Header("Картинки")]
        [SerializeField] private Sprite hexImage;
        [SerializeField] private Color hexImageTint = Color.white;
        [SerializeField] private Sprite frameImage;
        [SerializeField] private Color frameImageTint = Color.white;
        [SerializeField, Range(0.5f, 1.5f)] private float imageScale = 1f;

        [Header("Гекс")]
        [SerializeField] private Color fillColor = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private Color lineColor = new Color(0.36f, 0.38f, 0.38f, 0.80f);
        [SerializeField, Range(0f, 6f)] private float lineWidth = 1.2f;
        [SerializeField, Range(0f, 0.3f)] private float gap = 0.02f;
        [SerializeField, Range(0f, 1f)] private float opacity = 1f;

        [Header("Рисованная линия (перо дорог MapLab)")]
        [Tooltip("Линии сетки — тем же пером, что дороги и берега карты: нажим, дрожь руки, редкие разрывы и свободные окончания. Выключено — ровная линия цвета «Линия».")]
        [SerializeField] private bool inkLines = true;
        [Tooltip("Плотность туши сетки относительно пера дорог: сетка спокойнее фигур и отметок.")]
        [SerializeField, Range(0f, 1f)] private float inkOpacity = 0.55f;

        [Header("Состояния")]
        [SerializeField] private Color difficultColor = new Color(0.29f, 0.25f, 0.17f, 0.80f);
        [SerializeField] private Color impassableColor = new Color(0.07f, 0.08f, 0.09f, 0.80f);
        [SerializeField] private Color reachableColor = new Color(0.28f, 0.75f, 0.90f, 0.40f);
        [SerializeField, Range(0.5f, 6f)] private float reachableLineWidth = 2.2f;
        [SerializeField] private Color targetColor = new Color(0.58f, 0.20f, 0.17f, 0.80f);
        [SerializeField] private Color attackHoverFill = new Color(0.82f, 0.64f, 0.18f, 0.16f);
        [SerializeField] private Color attackHoverLine = new Color(0.98f, 0.80f, 0.32f, 0.95f);
        [Tooltip("Траектория хода при наведении — тем же пером, что контуры клеток.")]
        [SerializeField] private Color routeColor = new Color(0.62f, 0.86f, 0.92f, 0.95f);
        [SerializeField, Range(0.5f, 8f)] private float routeLineWidth = 2.6f;

        // Картинка гекса растягивается на прямоугольник гекса (ширина : высота ≈ 1,15 : 1).
        public Sprite HexImage => hexImage;
        public Color HexImageTint => hexImageTint;
        public Sprite FrameImage => frameImage;
        public Color FrameImageTint => frameImageTint;
        public float ImageScale => Mathf.Clamp(imageScale, 0.5f, 1.5f);

        // Подложка — заливка под картинками гекса.
        public Color FillColor => fillColor;
        public Color LineColor => lineColor;
        public bool InkLines => inkLines;
        public float InkOpacity => Mathf.Clamp01(inkOpacity);
        public float LineWidth => Mathf.Max(0f, lineWidth);
        // Зазор между гексами — доля радиуса.
        public float Gap => Mathf.Clamp(gap, 0f, 0.3f);
        // Непрозрачность основного вида сетки: подложка, картинки, линия.
        // Состояния (трудный, доступный ход, цель) — своими цветами.
        public float Opacity => Mathf.Clamp01(opacity);

        public Color DifficultColor => difficultColor;
        public Color ImpassableColor => impassableColor;
        public Color ReachableColor => reachableColor;
        public float ReachableLineWidth => Mathf.Max(0.5f, reachableLineWidth);
        public Color TargetColor => targetColor;
        public Color AttackHoverFill => attackHoverFill;
        public Color AttackHoverLine => attackHoverLine;
        public Color RouteColor => routeColor;
        public float RouteLineWidth => Mathf.Max(0.5f, routeLineWidth);

        // Цвет с учётом общей непрозрачности сетки.
        public Color Fade(Color color)
        {
            color.a *= Opacity;
            return color;
        }
    }
}
