using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Калибровка нарисованных элементов: точка опоры прямо на PNG (клик),
    // порог альфы с предпросмотром результата AlphaClip, рост в мире и вид
    // «на горизонтальной земле» рядом с фигуркой-эталоном. Для фишки — наложение
    // шести кадров на общей опоре: видно, прыгают ли ноги и пульсирует ли рост.
    public sealed class MapLabArtCalibrationWindow : EditorWindow
    {
        private MapLabArtSet set;
        private int selected;
        private bool onion;
        private bool showClip = true;
        private Texture2D clipPreview;
        private string clipKey;
        private Vector2 scroll;

        [MenuItem("Kingdom Survival/MapLab (эксперимент)/Калибровка арта", priority = 412)]
        public static void Open()
        {
            MapLabArtCalibrationWindow window = GetWindow<MapLabArtCalibrationWindow>("MapLab · арт");
            window.minSize = new Vector2(560f, 520f);
            window.Show();
        }

        private void OnDisable()
        {
            if (clipPreview != null)
                DestroyImmediate(clipPreview);
        }

        private void OnGUI()
        {
            if (set == null)
                set = MapLabArtImport.LoadArtSet();
            set = (MapLabArtSet)EditorGUILayout.ObjectField("Набор арта", set, typeof(MapLabArtSet), false);
            if (set == null || set.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Набор не подключён. Kingdom Survival → MapLab (эксперимент) → Подключить пакет этапа 02…", MessageType.Info);
                return;
            }
            string[] names = new string[set.Entries.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = $"{set.Entries[i].Category}/{set.Entries[i].Id}" + (set.Entries[i].HasTexture ? "" : " (нет PNG)");
            selected = Mathf.Clamp(EditorGUILayout.Popup("Элемент", selected, names), 0, names.Length - 1);
            MapLabArtEntry e = set.Entries[selected];
            bool figure = e.Category == MapLabArtCategory.Figure || e.Category == MapLabArtCategory.FigureCalibration;

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(set, "MapLab: калибровка арта");
            e.Cutoff = EditorGUILayout.Slider(new GUIContent("Порог AlphaClip", "Рекомендация пакета: " + e.Cutoff.ToString("0.00")), e.Cutoff, 0.05f, 0.95f);
            e.WorldHeight = EditorGUILayout.FloatField(new GUIContent("Рост силуэта в мире", "Высота части с alpha ≥ 160."), e.WorldHeight);
            e.HeightCorrection = EditorGUILayout.Slider("Поправка роста кадра", e.HeightCorrection, 0.8f, 1.25f);
            e.Weight = EditorGUILayout.FloatField("Вероятность выбора", e.Weight);
            e.ScaleRange = EditorGUILayout.Vector2Field("Диапазон масштаба", e.ScaleRange);
            e.MaxSlope = EditorGUILayout.Slider("Наибольший уклон", e.MaxSlope, 0f, 60f);
            Vector2 pivot = EditorGUILayout.Vector2Field("Опора (0..1 от левого нижнего)", e.Pivot);
            if (pivot != e.Pivot)
            {
                e.Pivot = pivot;
                e.PivotEdited = true;
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Опора по подсказке пакета"))
            {
                e.Pivot = e.PivotHint;
                e.PivotEdited = true;
            }
            if (GUILayout.Button("Опора по силуэту (низ, центр основания)"))
            {
                e.Pivot = new Vector2(e.FootX, e.SolidRect.yMin + e.SolidRect.height * 0.035f);
                e.PivotEdited = true;
            }
            EditorGUILayout.EndHorizontal();
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.LabelField($"Холст {e.Width}×{e.Height} · силуэт α≥160: x {e.SolidRect.xMin:0.000}–{e.SolidRect.xMax:0.000}, y {e.SolidRect.yMin:0.000}–{e.SolidRect.yMax:0.000} · карточка {e.CardHeight:0.00}×{e.CardHeight * e.Aspect:0.00} ед.", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(e.Notes))
                EditorGUILayout.LabelField(e.Notes, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            showClip = GUILayout.Toggle(showClip, "Показать результат AlphaClip (красное — отсечено)", EditorStyles.miniButtonLeft);
            using (new EditorGUI.DisabledScope(!figure))
                onion = GUILayout.Toggle(onion, "Наложение 6 кадров фишки", EditorStyles.miniButtonRight);
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            Rect area = GUILayoutUtility.GetRect(position.width - 24f, 420f);
            if (e.HasTexture)
            {
                float side = Mathf.Min(area.height, area.width * 0.6f);
                Rect image = new Rect(area.x, area.y, side * e.Aspect > area.width * 0.6f ? area.width * 0.6f : side * e.Aspect, 0f);
                image.height = image.width / e.Aspect;
                EditorGUI.DrawRect(image, new Color(0.22f, 0.22f, 0.22f));
                Texture shown = showClip ? ClipPreview(e) : e.Texture;
                if (onion && figure)
                    DrawOnion(image, e);
                else if (shown != null)
                    GUI.DrawTexture(image, shown, ScaleMode.StretchToFill, true);
                DrawRectOutline(Sub(image, e.SolidRect), new Color(0.2f, 0.9f, 1f, 0.8f));
                Vector2 p = new Vector2(image.x + e.Pivot.x * image.width, image.yMax - e.Pivot.y * image.height);
                EditorGUI.DrawRect(new Rect(p.x - 9, p.y, 18, 1), Color.red);
                EditorGUI.DrawRect(new Rect(p.x, p.y - 9, 1, 18), Color.red);
                Event ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && image.Contains(ev.mousePosition))
                {
                    Undo.RecordObject(set, "MapLab: опора");
                    e.Pivot = new Vector2((ev.mousePosition.x - image.x) / image.width, (image.yMax - ev.mousePosition.y) / image.height);
                    e.PivotEdited = true;
                    changed = true;
                    ev.Use();
                }
                DrawGroundPreview(new Rect(image.xMax + 12f, area.y, area.width - image.width - 12f, area.height), e);
            }
            else
            {
                EditorGUI.HelpBox(area, "PNG не найден локально — переподключите пакет этапа 02. Карта использует процедурный вариант.", MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("Клик по рисунку — опора. Голубая рамка — силуэт α ≥ 160; красный крест — опора.", EditorStyles.miniLabel);
            if (changed)
            {
                EditorUtility.SetDirty(set);
                clipKey = null;
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Сохранить и пересобрать карту"))
            {
                AssetDatabase.SaveAssets();
                MapLabView view = MapLabAssets.FindView();
                if (view != null)
                    view.RebuildArt();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }

        private static Rect Sub(Rect image, Rect uv)
        {
            return new Rect(image.x + uv.x * image.width, image.yMax - uv.yMax * image.height, uv.width * image.width, uv.height * image.height);
        }

        private static void DrawRectOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax, r.y, 1, r.height), c);
        }

        // Все кадры фишки, совмещённые по опоре и масштабу (с поправкой роста).
        private void DrawOnion(Rect image, MapLabArtEntry current)
        {
            MapLabMarkerSet markers = MapLabArtImport.LoadMarkerSet();
            if (markers == null)
                return;
            Vector2 anchor = new Vector2(image.x + current.Pivot.x * image.width, image.yMax - current.Pivot.y * image.height);
            float pixelsPerUnit = image.height / Mathf.Max(0.01f, current.CardHeight);
            Color previous = GUI.color;
            for (int i = 0; i < 6; i++)
            {
                MapLabArtEntry e = markers.View((MapLabMarkerDirection)i);
                if (e == null || !e.HasTexture)
                    continue;
                float h = e.CardHeight * pixelsPerUnit;
                float w = h * e.Aspect;
                Rect r = new Rect(anchor.x - e.Pivot.x * w, anchor.y - (1f - e.Pivot.y) * h, w, h);
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                GUI.DrawTexture(r, e.Texture, ScaleMode.StretchToFill, true);
            }
            GUI.color = previous;
            EditorGUI.DrawRect(new Rect(image.x, anchor.y, image.width, 1), new Color(1f, 0.4f, 0.2f, 0.9f));
        }

        // Вид на ровной земле: рисунок в мировом масштабе рядом с фигуркой-эталоном (2.8 ед.).
        private void DrawGroundPreview(Rect rect, MapLabArtEntry e)
        {
            EditorGUI.DrawRect(rect, new Color(0.6f, 0.58f, 0.36f));
            float ground = rect.y + rect.height * 0.82f;
            EditorGUI.DrawRect(new Rect(rect.x, ground, rect.width, rect.yMax - ground), new Color(0.5f, 0.5f, 0.3f));
            EditorGUI.DrawRect(new Rect(rect.x, ground, rect.width, 1), new Color(0.2f, 0.2f, 0.15f));
            MapLabMarkerSet markers = MapLabArtImport.LoadMarkerSet();
            MapLabArtEntry reference = markers != null ? markers.View(MapLabMarkerDirection.Front) : null;
            float tallest = Mathf.Max(e.CardHeight, reference != null ? reference.CardHeight : 0f);
            float ppu = rect.height * 0.75f / Mathf.Max(0.5f, tallest);
            float x = rect.x + rect.width * 0.4f;
            Draw(e, x, ground, ppu);
            if (reference != null && reference != e && reference.HasTexture)
                Draw(reference, rect.x + rect.width * 0.82f, ground, ppu);
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width, 18), "На ровной земле, мировой масштаб; справа — фишка Front", EditorStyles.miniLabel);
        }

        private static void Draw(MapLabArtEntry e, float x, float ground, float ppu)
        {
            float h = e.CardHeight * ppu;
            float w = h * e.Aspect;
            GUI.DrawTexture(new Rect(x - e.Pivot.x * w, ground - (1f - e.Pivot.y) * h, w, h), e.Texture, ScaleMode.StretchToFill, true);
        }

        // Предпросмотр отсечения из исходного PNG (512 px): красным — отсечённые
        // полупрозрачные пиксели (ореол, кайма), остальное — как будет в сцене.
        private Texture ClipPreview(MapLabArtEntry e)
        {
            string key = e.Id + e.Cutoff.ToString("0.000");
            if (clipPreview != null && clipKey == key)
                return clipPreview;
            string path = AssetDatabase.GetAssetPath(e.Texture);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return e.Texture;
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            source.LoadImage(File.ReadAllBytes(path));
            int w = Mathf.Min(512, source.width), h = Mathf.Max(1, Mathf.RoundToInt(w / (float)source.width * source.height));
            Color32[] src = source.GetPixels32();
            Color32[] dst = new Color32[w * h];
            byte cut = (byte)Mathf.RoundToInt(e.Cutoff * 255f);
            for (int y = 0; y < h; y++)
            {
                int sy = Mathf.Min(source.height - 1, y * source.height / h);
                for (int x = 0; x < w; x++)
                {
                    Color32 c = src[sy * source.width + Mathf.Min(source.width - 1, x * source.width / w)];
                    if (c.a >= cut)
                        dst[y * w + x] = new Color32(c.r, c.g, c.b, 255);
                    else if (c.a > 4)
                        dst[y * w + x] = new Color32(220, 30, 30, 255);
                    else
                        dst[y * w + x] = new Color32(0, 0, 0, 0);
                }
            }
            DestroyImmediate(source);
            if (clipPreview == null || clipPreview.width != w || clipPreview.height != h)
            {
                if (clipPreview != null)
                    DestroyImmediate(clipPreview);
                clipPreview = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            }
            clipPreview.SetPixels32(dst);
            clipPreview.Apply(false, false);
            clipKey = key;
            return clipPreview;
        }
    }
}
