using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // 12У-H: фигуры в сцене живой карты (сейчас — герой). Карточка фигуры
    // стоит в мире MapLab рядом с деревьями и домами и сортируется с ними по
    // глубине: что ближе к зрителю по опоре, то и закрывает. Закрытая часть
    // фигуры видна светлым контуром (материал с ZTest Greater). Под фигурой —
    // плоская мягкая тень на земле (её закрывают предметы перед ней).
    public static class MapLabLiveFigures
    {
        private sealed class Figure
        {
            public GameObject Root;
            public Transform Card, Shadow;
            public Mesh CardMesh, LineMesh, ShadowMesh;
            public Material Fill, XRayFill, XRayLine, ShadowMaterial;
            public Vector2 Size, Pivot;
            public Vector4 Uv;
            // Тень на земле: чем построена (длина вдоль света, угол) и каким цветом показана.
            public float ShadowHeight = -1f, ShadowLength;
            public Color ShadowColor;
        }

        private static readonly Dictionary<string, Figure> figures = new Dictionary<string, Figure>();
        private static MapLabView owner;
        private static Shader shader;
        private static Texture2D shadowTexture;
        private static readonly Color OutlineColor = new Color(1f, 0.93f, 0.72f, 1f);

        // Есть шейдер фигуры — можно ставить фигуры в сцену (иначе UI-фигура).
        public static bool Available => (shader != null || (shader = Resources.Load<Shader>("MapLabFigureCard")) != null) && shader.isSupported;

        // navPercent — точка опоры в процентах карты пути; worldHeight —
        // рост в единицах мира; pivot — опора на рисунке (0..1); lift —
        // подскок при беге (мир). outline — контур силуэта для закрытой части.
        public static bool Place(MapLabGameLive.World w, string key, Texture2D texture, Texture2D outline, Vector2 navPercent, float worldHeight, float aspect, Vector2 pivot, float lift, bool shadow)
        {
            return Place(w, key, texture, FullUv, outline, navPercent, worldHeight, aspect, pivot, lift, shadow);
        }

        // Целая текстура: UV (0, 0)–(1, 1).
        public static readonly Vector4 FullUv = new Vector4(0f, 0f, 1f, 1f);

        // uv — прямоугольник кадра в текстуре (атлас): (uMin, vMin, uMax, vMax);
        // uMin > uMax — кадр показывается отражённым. Контур (outline) —
        // отдельная текстура целого кадра: он отражается вместе с кадром.
        public static bool Place(MapLabGameLive.World w, string key, Texture2D texture, Vector4 uv, Texture2D outline, Vector2 navPercent, float worldHeight, float aspect, Vector2 pivot, float lift, bool shadow)
        {
            if (w == null || !w.Ready || texture == null || !Available)
                return false;
            if (!ReferenceEquals(owner, w.View))
                ClearAll();
            owner = w.View;
            if (!figures.TryGetValue(key, out Figure f) || f.Root == null)
                figures[key] = f = Create(w, key);
            float x = w.Play.xMin + navPercent.x / 100f * w.Play.width;
            float z = w.Play.yMax - navPercent.y / 100f * w.Play.height;
            float y = MapLabGameLive.GroundHeight(w, x, z);
            f.Root.SetActive(true);
            f.Card.position = new Vector3(x, y + lift, z);
            f.Shadow.position = new Vector3(x, y + 0.04f, z);
            UpdateShadow(w.View, f, worldHeight, shadow);
            Vector2 size = new Vector2(worldHeight * aspect, worldHeight);
            if ((size - f.Size).sqrMagnitude > 1e-6f || (pivot - f.Pivot).sqrMagnitude > 1e-6f)
            {
                f.Size = size;
                f.Pivot = pivot;
                f.Uv = uv;
                BuildCard(f.CardMesh, size, pivot, uv);
                BuildCard(f.LineMesh, size, pivot, LineUv(uv));
            }
            else if ((uv - f.Uv).sqrMagnitude > 1e-12f)
            {
                // Кадр сменился, размер тот же — меняются только UV карточки.
                f.Uv = uv;
                SetCardUv(f.CardMesh, uv);
                SetCardUv(f.LineMesh, LineUv(uv));
            }
            float lean = w.View.Settings != null ? w.View.Settings.Art.CardLean : 0.3f;
            foreach (Material m in new[] { f.Fill, f.XRayFill, f.XRayLine })
                m.SetFloat("_CardLean", lean);
            f.Fill.mainTexture = texture;
            f.XRayFill.mainTexture = texture;
            f.XRayLine.mainTexture = outline != null ? outline : texture;
            f.XRayLine.SetFloat("_Cutoff", outline != null ? 0.35f : 1.1f);
            return true;
        }

        // Час суток карты — тени предметов и фигур идут за солнцем (MapLabView.SetSunHour).
        public static void SetSunHour(float hour)
        {
            MapLabGameLive.World w = MapLabGameLive.Current;
            if (w != null && w.View != null)
                w.View.SetSunHour(hour);
        }

        public static void Hide(string key)
        {
            if (figures.TryGetValue(key, out Figure f) && f.Root != null)
                f.Root.SetActive(false);
        }

        public static void ClearAll()
        {
            foreach (Figure f in figures.Values)
            {
                if (f.Root != null)
                    Kill(f.Root);
                foreach (Object o in new Object[] { f.CardMesh, f.LineMesh, f.ShadowMesh, f.Fill, f.XRayFill, f.XRayLine, f.ShadowMaterial })
                    if (o != null)
                        Kill(o);
            }
            figures.Clear();
            owner = null;
        }

        private static void Kill(Object o)
        {
            if (Application.isPlaying)
                Object.Destroy(o);
            else
                Object.DestroyImmediate(o);
        }

        private static Figure Create(MapLabGameLive.World w, string key)
        {
            Figure f = new Figure();
            f.Root = new GameObject("MapLab Figure " + key) { hideFlags = HideFlags.HideAndDontSave, layer = MapLabGameLive.Layer };
            f.Root.transform.SetParent(w.View.transform, false);
            f.CardMesh = new Mesh { name = "Figure Card", hideFlags = HideFlags.HideAndDontSave };
            f.LineMesh = new Mesh { name = "Figure Outline Card", hideFlags = HideFlags.HideAndDontSave };
            f.ShadowMesh = new Mesh { name = "Figure Shadow", hideFlags = HideFlags.HideAndDontSave };
            // Видимая часть — обычная глубина; закрытая — заливка 15% и контур.
            f.Fill = MakeMaterial(4f, 1f, Color.white, 0.35f, 3000);
            f.XRayFill = MakeMaterial(5f, 0f, new Color(1f, 1f, 1f, 0.15f), 0.35f, 3001);
            f.XRayLine = MakeMaterial(5f, 0f, OutlineColor, 0.35f, 3002);
            f.ShadowMaterial = MakeMaterial(4f, 0f, new Color(0.12f, 0.1f, 0.07f, 0.34f), 0.004f, 2999);
            f.ShadowMaterial.mainTexture = ShadowTexture();
            f.Card = new GameObject("Card") { hideFlags = HideFlags.HideAndDontSave, layer = MapLabGameLive.Layer }.transform;
            f.Card.SetParent(f.Root.transform, false);
            foreach (Material m in new[] { f.Fill, f.XRayFill, f.XRayLine })
            {
                GameObject part = new GameObject(m.name) { hideFlags = HideFlags.HideAndDontSave, layer = MapLabGameLive.Layer };
                part.transform.SetParent(f.Card, false);
                part.AddComponent<MeshFilter>().sharedMesh = ReferenceEquals(m, f.XRayLine) ? f.LineMesh : f.CardMesh;
                MeshRenderer r = part.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            GameObject shadowObject = new GameObject("Shadow") { hideFlags = HideFlags.HideAndDontSave, layer = MapLabGameLive.Layer };
            shadowObject.transform.SetParent(f.Root.transform, false);
            shadowObject.AddComponent<MeshFilter>().sharedMesh = f.ShadowMesh;
            MeshRenderer sr = shadowObject.AddComponent<MeshRenderer>();
            sr.sharedMaterial = f.ShadowMaterial;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            f.Shadow = shadowObject.transform;
            return f;
        }

        private static Material MakeMaterial(float zTest, float zWrite, Color color, float cutoff, int queue)
        {
            Material m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue, name = "Figure " + queue };
            m.SetFloat("_ZTest", zTest);
            m.SetFloat("_ZWrite", zWrite);
            m.SetColor("_Color", color);
            m.SetFloat("_Cutoff", cutoff);
            return m;
        }

        // Карточка: опора в начале координат, углы — смещения по кадру (как у
        // карточек MapLab: x вправо, y вверх от опоры).
        private static void BuildCard(Mesh mesh, Vector2 size, Vector2 pivot, Vector4 uv)
        {
            float left = -pivot.x * size.x, right = (1f - pivot.x) * size.x;
            float bottom = -pivot.y * size.y, top = (1f - pivot.y) * size.y;
            mesh.Clear();
            mesh.vertices = new Vector3[4];
            SetCardUv(mesh, uv);
            mesh.uv2 = new[] { new Vector2(left, bottom), new Vector2(right, bottom), new Vector2(right, top), new Vector2(left, top) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.up * size.y * 0.5f, new Vector3(size.x + size.y, size.y * 2f, size.x + size.y) * 2f);
        }

        private static readonly Vector2[] uvBuffer = new Vector2[4];

        // Порядок вершин карточки: левый низ, правый низ, правый верх, левый верх.
        private static void SetCardUv(Mesh mesh, Vector4 uv)
        {
            uvBuffer[0] = new Vector2(uv.x, uv.y);
            uvBuffer[1] = new Vector2(uv.z, uv.y);
            uvBuffer[2] = new Vector2(uv.z, uv.w);
            uvBuffer[3] = new Vector2(uv.x, uv.w);
            mesh.uv = uvBuffer;
        }

        // Контур — целая текстура кадра, отражается вместе с кадром.
        private static Vector4 LineUv(Vector4 uv) => uv.x > uv.z ? new Vector4(1f, 0f, 0f, 1f) : FullUv;

        // Тень фигуры — тот же цвет, плотность и направление солнца, что у теней
        // карточек (MapLabView.SunGround / SunDensity): овал под ногами, вытянутый
        // по тени; ночью (плотность 0) не рисуется. Мешу нужна пересборка только
        // при смене роста, длины или угла.
        private static void UpdateShadow(MapLabView view, Figure f, float height, bool wanted)
        {
            MapLabStage05Settings s5 = view.Settings.Stage05;
            float density = view.SunDensity * s5.CardShadowOpacity;
            bool on = wanted && density > 0.004f;
            if (f.Shadow.gameObject.activeSelf != on)
                f.Shadow.gameObject.SetActive(on);
            if (!on)
                return;
            Color color = s5.CardShadowColor;
            color.a = density;
            if (color != f.ShadowColor)
            {
                f.ShadowColor = color;
                f.ShadowMaterial.SetColor("_Color", color);
            }
            Vector2 ground = view.SunGround;
            float length = ground.magnitude * height * s5.FigureShadowStretch;
            float yaw = -Mathf.Atan2(ground.y, ground.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(f.ShadowHeight - height) > 1e-5f || Mathf.Abs(f.ShadowLength - length) > 1e-4f)
            {
                f.ShadowHeight = height;
                f.ShadowLength = length;
                BuildShadow(f.ShadowMesh, height, length);
            }
            f.Shadow.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Плоская тень на земле: овал под ногами, вытянутый вдоль +x на length
        // (поворачивается по направлению тени).
        private static void BuildShadow(Mesh mesh, float height, float length)
        {
            float w = height * 0.24f, d = height * 0.1f;
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(-w, 0f, -d), new Vector3(w + length, 0f, -d), new Vector3(w + length, 0f, d), new Vector3(-w, 0f, d) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.uv2 = new Vector2[4];
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
        }

        private static Texture2D ShadowTexture()
        {
            if (shadowTexture != null)
                return shadowTexture;
            const int size = 64;
            shadowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, name = "Figure Shadow" };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.55f, 1f, Mathf.Sqrt(dx * dx + dy * dy)));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            shadowTexture.SetPixels32(pixels);
            shadowTexture.Apply(false, true);
            return shadowTexture;
        }
    }
}
