using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Затемнение краёв и углов места (LocationVisualDefinition.EdgeShade):
    // прямоугольник во весь рисунок места поверх всего — земли, предметов,
    // людей, — шейдер LocationEdgeShade. Привязан к месту, а не к экрану (в
    // отличие от «Затемнения краёв» обработки кадра): камера едет — края
    // поляны остаются тёмными. Настройки читаются каждый кадр — правка в Базе
    // локаций видна сразу.
    public sealed partial class LocationWorldRenderer
    {
        public const string EdgeShadeShaderPath = "LocationRendering/LocationEdgeShade";
        // Поверх всего на рисунке места.
        public const int EdgeShadeSortingOrder = 32700;

        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int EdgeParamsId = Shader.PropertyToID("_EdgeParams");
        private static readonly int EdgeShapeId = Shader.PropertyToID("_EdgeShape");
        private static readonly int EdgeSidesId = Shader.PropertyToID("_EdgeSides");

        private SpriteRenderer edgeShade;
        private Material edgeShadeMaterial;

        // Слой затемнения краёв (для проверок); null — выключено или нет шейдера.
        public SpriteRenderer EdgeShadeRenderer => edgeShade != null && edgeShade.enabled ? edgeShade : null;

        private void UpdateEdgeShade()
        {
            LocationEdgeShade data = Definition.EdgeShade;
            bool on = data != null && data.Enabled && data.Intensity > .001f;
            if (!on)
            {
                if (edgeShade != null && edgeShade.enabled) edgeShade.enabled = false;
                return;
            }
            if (edgeShade == null)
            {
                Shader shader = Resources.Load<Shader>(EdgeShadeShaderPath);
                if (shader == null) return;
                edgeShadeMaterial = Own(new Material(shader) { name = "Затемнение краёв места" });
                GameObject target = Child("Затемнение краёв места");
                edgeShade = target.AddComponent<SpriteRenderer>();
                edgeShade.sprite = MakeSprite(4, 4, (x, y) => Color.white);
                edgeShade.sharedMaterial = edgeShadeMaterial;
                edgeShade.sortingOrder = EdgeShadeSortingOrder;
            }
            if (!edgeShade.enabled) edgeShade.enabled = true;
            // Во весь рисунок места.
            Vector2 a = LocationVisualGeometry.PixelToWorld(Location, Vector2.zero);
            Vector2 b = LocationVisualGeometry.PixelToWorld(Location, CanvasSize);
            Vector2 size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            Bounds local = edgeShade.sprite.bounds;
            Vector2 scale = new Vector2(size.x / Mathf.Max(1e-6f, local.size.x), size.y / Mathf.Max(1e-6f, local.size.y));
            edgeShade.transform.localScale = new Vector3(scale.x, scale.y, 1);
            edgeShade.transform.localPosition = (a + b) / 2 - Vector2.Scale(local.center, scale);
            float smaller = Mathf.Min(size.x, size.y);
            float width = Mathf.Clamp(data.Width, .01f, 1) * smaller / 2;
            float radius = Mathf.Clamp01(data.Roundness) * width * 2;
            edgeShadeMaterial.SetColor(EdgeColorId, data.Color);
            edgeShadeMaterial.SetVector(EdgeParamsId, new Vector4(Mathf.Clamp01(data.Intensity), Mathf.Lerp(1, 4, Mathf.Clamp01(data.Softness)), 0, 0));
            edgeShadeMaterial.SetVector(EdgeShapeId, new Vector4(width, radius, size.x / 2, size.y / 2));
            edgeShadeMaterial.SetVector(EdgeSidesId, new Vector4(Mathf.Clamp01(data.Left), Mathf.Clamp01(data.Right), Mathf.Clamp01(data.Bottom), Mathf.Clamp01(data.Top)));
        }
    }
}
