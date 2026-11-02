using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Деревянная фишка отряда на карте MapLab: один статичный кадр из шести
    // направлений (сектор 60° относительно камеры), опора — общая точка между
    // ступнями, высота основания — SampleHeight. Короткий preview — перемещение
    // статичной миниатюры по сухому отрезку дороги, без walk-анимации и без
    // игрового времени. В игру не подключена.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MapLabMarker : MonoBehaviour
    {
        public const string RootName = "MarkerRoot";

        public MapLabView View;
        [Tooltip("Ставить на дорогу у демонстрационного участка при каждой генерации.")]
        public bool AutoPlace = true;
        [Tooltip("Положение (X, Z мира).")]
        public Vector2 Position = new Vector2(128f, 128f);
        [Tooltip("Куда смотрит фигурка, градусы от +Z мира. При азимуте камеры 0: 180 — лицом к камере.")]
        public float FacingDegrees = 180f;
        [Tooltip("Preview: фишка ходит туда-обратно по сухому отрезку дороги.")]
        public bool PreviewMove;
        [Min(0.2f)] public float PreviewSpeed = 3f;
        [Min(4f)] public float PreviewLength = 22f;
        [Tooltip("Тестовая стрелка направления на земле.")]
        public bool ShowArrow;

        [SerializeField, HideInInspector] private GameObject root;

        private Mesh cardMesh, contactMesh, arrowMesh;
        private Material cardMaterial, ghostMaterial, contactMaterial, arrowMaterial;
        private MeshRenderer cardRenderer, ghostRenderer, arrowRenderer;
        private readonly List<Vector2> path = new List<Vector2>();
        private float pathLength;
        private float previewStart;
        private int pitchSet = -1;

        // Этап 04: выбранный набор наклона героя (−1 — прежний набор).
        public int PitchSet => pitchSet;
        public float PitchSetPitch { get; private set; }

        public MapLabArtEntry CurrentEntry { get; private set; }
        public MapLabMarkerDirection CurrentDirection { get; private set; }
        public bool Approximate { get; private set; }
        public string Status { get; private set; } = "";
        public IReadOnlyList<Vector2> PreviewPath => path;
        public Vector3 BasePoint { get; private set; }

        private void OnDisable()
        {
            Clear();
        }

        public void Clear()
        {
            if (root != null)
                DestroySafe(root);
            root = null;
            foreach (Object o in new Object[] { cardMesh, contactMesh, arrowMesh, cardMaterial, ghostMaterial, contactMaterial, arrowMaterial })
                DestroySafe(o);
            cardMesh = contactMesh = arrowMesh = null;
            cardMaterial = ghostMaterial = contactMaterial = arrowMaterial = null;
            cardRenderer = ghostRenderer = arrowRenderer = null;
            CurrentEntry = null;
        }

        // После генерации карты: опора на дороге и путь preview.
        public void OnMapBuilt(MapLabView view)
        {
            View = view;
            if (view == null || view.Data == null)
                return;
            BuildPath(view.Data, view.DemoFocus);
            if (AutoPlace && path.Count > 1)
            {
                Position = PointAt(pathLength * 0.5f, out Vector2 dir);
                FacingDegrees = 180f;
            }
            previewStart = Now();
            Refresh();
        }

        // Выбрать кадр под камеру и обновить опору, тень и (если включено) контур.
        public void Refresh()
        {
            MapLabView view = View;
            MapLabMarkerSet markers = view != null ? view.ActiveMarkerSet : null;
            if (view == null || view.Data == null || view.Settings == null || !view.Settings.Art.ShowMarker || markers == null)
            {
                SetVisible(false);
                Status = view != null && markers == null ? "Фишка: набор не подключён." : "";
                return;
            }
            MapLabData data = view.Data;
            float azimuth = view.CameraRig != null ? view.CameraRig.Azimuth : view.Settings.Camera.AzimuthDegrees;
            float pitch = view.CameraRig != null ? view.CameraRig.Pitch : view.Settings.Camera.PitchFor(1f);
            CurrentDirection = MapLabMarkerSet.Sector(FacingDegrees, azimuth);
            MapLabArtEntry entry;
            bool approximate;
            if (markers.HasPitchSets)
            {
                // Смена набора наклона не меняет направление, место и скорость.
                MapLabStage04Settings s4 = view.Settings.Stage04;
                pitchSet = markers.ChooseSet(pitch, pitchSet, s4.HeroViewSwitch, s4.HeroViewHysteresis);
                PitchSetPitch = markers.PitchSets[pitchSet].Pitch;
                entry = markers.ResolveSet(CurrentDirection, pitch, pitchSet, out approximate);
            }
            else
            {
                pitchSet = -1;
                PitchSetPitch = markers.FullSetPitch;
                entry = markers.Resolve(CurrentDirection, pitch, out approximate);
            }
            Approximate = approximate;
            if (entry == null || !entry.HasTexture)
            {
                SetVisible(false);
                Status = $"Фишка: нет кадра {MapLabMarkerSet.DirectionNames[(int)CurrentDirection]} — импортируйте пакет этапа 02.";
                return;
            }
            EnsureObjects(view);
            CurrentEntry = entry;
            float h = data.SampleHeight(Position.x, Position.y);
            BasePoint = new Vector3(Position.x, h, Position.y);
            MapLabArtInstance instance = new MapLabArtInstance
            {
                Base = BasePoint,
                Height = entry.CardHeight,
                Width = entry.CardHeight * entry.Aspect,
                Pivot = entry.Pivot,
                Brightness = 1f,
                Tint = 0f,
                GroundShade = 0.6f,
            };
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();
            MapLabArtBuilder.AppendCard(instance, new Color(0.8f, 0f, 0.6f, 1f), positions, uv0, uv1, colors, indices);
            cardMesh.Clear();
            cardMesh.SetVertices(positions);
            cardMesh.SetUVs(0, uv0);
            cardMesh.SetUVs(1, uv1);
            cardMesh.SetColors(colors);
            cardMesh.SetTriangles(indices, 0, false);
            float reach = instance.Height * 3f + 4f;
            cardMesh.bounds = new Bounds(BasePoint, Vector3.one * reach * 2f);
            view.ConfigureArtMaterial(cardMaterial, entry);
            view.ConfigureArtMaterial(ghostMaterial, entry);
            ghostMaterial.SetFloat("_Ghost", 1f);
            ghostMaterial.SetFloat("_ZTest", (float)CompareFunction.Greater);
            ghostMaterial.SetFloat("_ZWrite", 0f);
            ghostMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            ghostMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            ghostMaterial.renderQueue = (int)RenderQueue.Transparent + 10;
            float radius = Mathf.Max(0.4f, instance.Width * entry.SolidRect.width * 0.42f);
            if (view.Settings.IsStage04)
                BuildCastShadow(view, entry, instance);
            else
                BuildContact(data, BasePoint, radius);
            BuildArrow(data);
            SetVisible(true);
            ghostRenderer.enabled = view.Settings.Art.MarkerGhost;
            arrowRenderer.enabled = ShowArrow;
            Status = $"Фишка: {MapLabMarkerSet.DirectionNames[(int)CurrentDirection]} ({entry.Id})" + (approximate ? $" · приближённый ракурс: кадры нарисованы под {PitchSetPitch:0}°, камера {pitch:0}°" : "");
        }

        private void EnsureObjects(MapLabView view)
        {
            if (root != null && cardRenderer != null)
                return;
            Clear();
            root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            cardMesh = new Mesh { name = "MapLab Marker Card", hideFlags = HideFlags.DontSave };
            contactMesh = new Mesh { name = "MapLab Marker Contact", hideFlags = HideFlags.DontSave };
            arrowMesh = new Mesh { name = "MapLab Marker Arrow", hideFlags = HideFlags.DontSave };
            cardMaterial = new Material(view.ArtCardShader) { name = "MapLab Marker", hideFlags = HideFlags.DontSave };
            ghostMaterial = new Material(view.ArtCardShader) { name = "MapLab Marker Ghost", hideFlags = HideFlags.DontSave };
            contactMaterial = new Material(view.ContactShader) { name = "MapLab Marker Contact", hideFlags = HideFlags.DontSave };
            arrowMaterial = new Material(view.LinesShader) { name = "MapLab Marker Arrow", hideFlags = HideFlags.DontSave };
            arrowMaterial.SetColor("_Color", new Color(0.85f, 0.12f, 0.08f, 1f));
            contactMaterial.SetColor("_Color", view.Palette.ShadowTint);
            cardRenderer = Spawn("Marker", cardMesh, cardMaterial);
            ghostRenderer = Spawn("Marker Ghost", cardMesh, ghostMaterial);
            Spawn("Marker Contact", contactMesh, contactMaterial);
            arrowRenderer = Spawn("Marker Arrow", arrowMesh, arrowMaterial);
        }

        private MeshRenderer Spawn(string name, Mesh mesh, Material material)
        {
            GameObject go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        private void SetVisible(bool visible)
        {
            if (root != null && root.activeSelf != visible)
                root.SetActive(visible);
        }

        // Этап 04: та же логика, что у мировых теней — собранная падающая
        // форма с чётким краем и градиент плотности от опоры; без чёрного круга.
        // Форму считает шейдер Contact по мировым точкам (опора, пятно), сетка
        // лежит на рельефе.
        private void BuildCastShadow(MapLabView view, MapLabArtEntry entry, MapLabArtInstance instance)
        {
            MapLabData data = view.Data;
            MapLabSettings s = view.Settings;
            Vector3 L = s.LightDirection;
            float w = instance.Width * entry.SolidRect.width, h = instance.Height * entry.SolidRect.height;
            float radius = Mathf.Max(0.25f, w * 0.3f);
            Vector2 foot = new Vector2(BasePoint.x, BasePoint.z);
            Vector2 hit = MapLabObjectShadows.Hit(data, BasePoint + Vector3.up * h * 0.55f, L);
            Vector2 center = (foot + hit) * 0.5f;
            float extent = Vector2.Distance(foot, hit) * 0.5f + radius * 1.8f + 0.3f;
            const int n = 13;
            Vector3[] v = new Vector3[n * n];
            Vector2[] uv = new Vector2[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float px = center.x + (x / (n - 1f) * 2f - 1f) * extent;
                float pz = center.y + (z / (n - 1f) * 2f - 1f) * extent;
                v[z * n + x] = new Vector3(px, data.SampleHeight(px, pz) + 0.04f, pz);
                uv[z * n + x] = new Vector2(px, pz);
            }
            int[] t = new int[(n - 1) * (n - 1) * 6];
            int k = 0;
            for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int i = z * n + x;
                t[k++] = i; t[k++] = i + n; t[k++] = i + n + 1;
                t[k++] = i; t[k++] = i + n + 1; t[k++] = i + 1;
            }
            contactMesh.Clear();
            contactMesh.vertices = v;
            contactMesh.uv = uv;
            contactMesh.triangles = t;
            contactMesh.RecalculateBounds();
            Vector2 lightDir = new Vector2(L.x, L.z).normalized;
            contactMaterial.SetFloat("_Mode", 1f);
            contactMaterial.SetVector("_Foot", new Vector4(foot.x, foot.y, hit.x, hit.y));
            contactMaterial.SetVector("_Shape", new Vector4(radius, 1f / Mathf.Max(0.35f, L.y), lightDir.x, lightDir.y));
            contactMaterial.SetVector("_Tone", new Vector4(s.Stage03.ShadowStrength, s.Stage04.ContactDensity, Mathf.Max(radius, w * 0.3f * s.Stage04.ContactReach), 0f));
            contactMaterial.SetColor("_Color", s.Stage03.ShadowColor);
        }

        // Мягкое пятно, повторяющее рельеф под опорой.
        private void BuildContact(MapLabData data, Vector3 center, float radius)
        {
            const int n = 9;
            Vector3[] v = new Vector3[n * n];
            Vector2[] uv = new Vector2[n * n];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (n - 1f) * 2f - 1f, w = z / (n - 1f) * 2f - 1f;
                    // Пятно чуть сдвинуто от света (свет — слева сверху).
                    float px = center.x + u * radius * 1.15f + radius * 0.18f;
                    float pz = center.z + w * radius * 0.85f + radius * 0.06f;
                    v[z * n + x] = new Vector3(px, data.SampleHeight(px, pz) + 0.05f, pz);
                    uv[z * n + x] = new Vector2(u, w);
                }
            }
            int[] t = new int[(n - 1) * (n - 1) * 6];
            int k = 0;
            for (int z = 0; z < n - 1; z++)
            {
                for (int x = 0; x < n - 1; x++)
                {
                    int i = z * n + x;
                    t[k++] = i; t[k++] = i + n; t[k++] = i + n + 1;
                    t[k++] = i; t[k++] = i + n + 1; t[k++] = i + 1;
                }
            }
            contactMesh.Clear();
            contactMesh.vertices = v;
            contactMesh.uv = uv;
            contactMesh.triangles = t;
            contactMesh.RecalculateBounds();
            contactMaterial.SetFloat("_Mode", 0f);
        }

        // Тестовая стрелка: куда смотрит фигурка (мир), лежит на рельефе.
        private void BuildArrow(MapLabData data)
        {
            float a = FacingDegrees * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            Vector2 side = new Vector2(-d.y, d.x);
            Vector2 p0 = Position + d * 0.9f;
            Vector2 p1 = Position + d * 3.6f;
            Vector2 tip = Position + d * 4.6f;
            Vector2[] flat =
            {
                p0 - side * 0.14f, p0 + side * 0.14f, p1 + side * 0.14f, p1 - side * 0.14f,
                p1 - side * 0.55f, p1 + side * 0.55f, tip,
            };
            Vector3[] v = new Vector3[flat.Length];
            for (int i = 0; i < flat.Length; i++)
                v[i] = new Vector3(flat[i].x, data.SampleHeight(flat[i].x, flat[i].y) + 0.12f, flat[i].y);
            arrowMesh.Clear();
            arrowMesh.vertices = v;
            arrowMesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 0, 2, 1, 0, 3, 2, 4, 6, 5 };
            arrowMesh.RecalculateBounds();
        }

        // ---------- Путь preview ----------

        // Самый длинный сухой и пологий участок дороги рядом с фокусом, без мостов.
        private void BuildPath(MapLabData data, Vector2 focus)
        {
            path.Clear();
            pathLength = 0f;
            List<Vector2> best = null;
            float bestScore = float.MaxValue;
            foreach (MapLabPolyline road in data.Roads)
            {
                List<Vector2> samples = MapLabGeometry.Resample(road.Points, 1f);
                List<Vector2> run = new List<Vector2>();
                for (int i = 0; i <= samples.Count; i++)
                {
                    bool ok = i < samples.Count && Valid(data, samples[i]);
                    if (ok)
                    {
                        run.Add(samples[i]);
                        if (run.Count < PreviewLength + 1)
                            continue;
                    }
                    if (run.Count >= Mathf.Min(PreviewLength, 10f))
                    {
                        Vector2 mid = run[run.Count / 2];
                        float score = Vector2.Distance(mid, focus) - (road.Kind == MapLabRoadKind.Road ? 6f : 0f);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = new List<Vector2>(run);
                        }
                    }
                    if (ok)
                        run.RemoveAt(0);
                    else
                        run.Clear();
                }
            }
            if (best == null)
                return;
            path.AddRange(best);
            for (int i = 1; i < path.Count; i++)
                pathLength += Vector2.Distance(path[i - 1], path[i]);
        }

        private static bool Valid(MapLabData data, Vector2 p)
        {
            if (!data.Contains(p.x, p.y) || data.IsUnderWater(p.x, p.y) || data.SampleSlope(p.x, p.y) > 12f)
                return false;
            if (data.SampleMask(data.Shore, p.x, p.y) > 0.5f)
                return false;
            foreach (MapLabBridge b in data.Bridges)
            {
                Vector2 c = (b.EndA + b.EndB) * 0.5f;
                if (Vector2.Distance(p, c) < Vector2.Distance(b.EndA, b.EndB) * 0.5f + 5f)
                    return false;
            }
            return true;
        }

        private Vector2 PointAt(float s, out Vector2 direction)
        {
            direction = Vector2.up;
            if (path.Count == 0)
                return Position;
            for (int i = 1; i < path.Count; i++)
            {
                float d = Vector2.Distance(path[i - 1], path[i]);
                if (s <= d || i == path.Count - 1)
                {
                    direction = (path[i] - path[i - 1]).normalized;
                    return Vector2.Lerp(path[i - 1], path[i], d > 0f ? Mathf.Clamp01(s / d) : 0f);
                }
                s -= d;
            }
            return path[path.Count - 1];
        }

        // Положение preview на момент t (секунды от начала): туда-обратно.
        public void SetPreviewTime(float t)
        {
            if (path.Count < 2 || pathLength <= 0f)
                return;
            float travel = t * PreviewSpeed;
            float cycle = pathLength * 2f;
            float s = Mathf.Repeat(travel, cycle);
            bool back = s > pathLength;
            if (back)
                s = cycle - s;
            Position = PointAt(s, out Vector2 dir);
            if (back)
                dir = -dir;
            FacingDegrees = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            Refresh();
        }

        private static float Now()
        {
#if UNITY_EDITOR
            return (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartup;
#endif
        }

        private void Update()
        {
            Tick();
        }

        public void Tick()
        {
            if (PreviewMove && View != null && View.Data != null)
                SetPreviewTime(Now() - previewStart);
        }

        private static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        // Стрелки направлений в Scene View: сектор и знак поворота проверяются глазами.
        private void OnDrawGizmosSelected()
        {
            if (View == null || View.Data == null)
                return;
            Vector3 c = new Vector3(Position.x, View.Data.SampleHeight(Position.x, Position.y) + 0.3f, Position.y);
            for (int i = 0; i < 6; i++)
            {
                float azimuth = View.CameraRig != null ? View.CameraRig.Azimuth : 0f;
                float facing = azimuth + 180f - i * 60f;
                Vector3 d = Quaternion.Euler(0f, facing, 0f) * Vector3.forward;
                Gizmos.color = i == (int)CurrentDirection ? Color.red : new Color(1f, 0.8f, 0.2f);
                Gizmos.DrawLine(c, c + d * 3f);
            }
            Gizmos.color = Color.cyan;
            for (int i = 1; i < path.Count; i++)
                Gizmos.DrawLine(new Vector3(path[i - 1].x, View.Data.SampleHeight(path[i - 1].x, path[i - 1].y) + 0.2f, path[i - 1].y),
                    new Vector3(path[i].x, View.Data.SampleHeight(path[i].x, path[i].y) + 0.2f, path[i].y));
        }
    }
}
