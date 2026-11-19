using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Виды карты в окне «Генератор карты» (17.11.2026): переключатель «Вид»
    // в верхней строке — тот же выбор, что кнопка «Вид: …» в игре (общая
    // настройка, видна и в Play Mode). Цветокоррекция вида — сразу в
    // предпросмотре; своя палитра или свои рисунки вида — пересборка карты
    // тем же seed. Раздел «Виды карты» на вкладке «Карта» правит выбранный
    // вид, создаёт новые (заложено сколько угодно видов) и собирает свои
    // рисунки вида из папки Assets/_Project/Art/MapViews/<ID вида>/.
    public sealed partial class MapLabGeneratorWindow
    {
        private bool viewsOpen = true;
        private string lookBuiltKey;
        private string lookShownId;

        // Ассет видов в Resources; нет — создаётся из встроенных видов.
        private static MapLabMapViews ViewsAsset(bool create)
        {
            MapLabMapViews asset = AssetDatabase.LoadAssetAtPath<MapLabMapViews>(MapLabMapViews.AssetPath);
            if (asset != null || !create)
                return asset;
            string folder = Path.GetDirectoryName(MapLabMapViews.AssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                Directory.CreateDirectory(folder);
            asset = CreateInstance<MapLabMapViews>();
            asset.EnsureBuiltIn();
            AssetDatabase.CreateAsset(asset, MapLabMapViews.AssetPath);
            AssetDatabase.SaveAssets();
            MapLabMapViews.ResetCache();
            return asset;
        }

        // Пакетное создание ассета видов (Unity -batchmode -executeMethod).
        public static void CreateViewsAssetBatch()
        {
            MapLabMapViews asset = ViewsAsset(true);
            if (asset.EnsureBuiltIn())
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            Debug.Log("Виды карты: " + AssetDatabase.GetAssetPath(asset) + " — " + asset.Views.Count + " вид(а).");
        }

        // Вид в предпросмотре: вид окна совпадает с выбранным; если у вида
        // другая палитра или другие рисунки, чем у собранной карты, — пересборка.
        private void SyncLook()
        {
            if (view == null)
                return;
            MapLabMapView active = MapLabMapViews.Active;
            string id = active != null ? active.Id : string.Empty;
            if (id == lookShownId && ReferenceEquals(view.Look, active))
                return;
            lookShownId = id;
            previewDirty = true;
            // Поле сравнения на вкладке «Ассеты» — тот же вид.
            if (fieldView != null && fieldView.Data != null && !fieldView.SwapLook(active))
            {
                fieldView.SetLook(active);
                BuildField();
            }
            string key = MapLabMapViews.BuildKey(active);
            // Та же палитра — свои рисунки подменяются в готовой карте сразу.
            if (view.Data != null && view.SwapLook(active))
                lookBuiltKey = key;
            else if (view.Data == null)
                view.SetLook(active);
            if (view.Data != null && lookBuiltKey != key)
            {
                status = "Вид «" + active?.Name + "»: своя палитра — карта пересобирается тем же seed.";
                EditorApplication.delayCall += () =>
                {
                    if (this != null && view != null)
                        Generate();
                };
            }
            else
                status = "Вид карты: " + active?.Name + ".";
            Repaint();
        }

        // Рисунок записи в выбранном виде (своя версия вида или основной) —
        // для миниатюр каталога и свойств ассета.
        private static Texture2D Shown(MapLabArtEntry e)
        {
            if (e == null)
                return null;
            MapLabMapView look = MapLabMapViews.Active;
            Texture2D own = look != null ? look.FindTexture(e, out _) : null;
            return own != null ? own : e.Texture;
        }

        // Перед генерацией: собрать в текущем виде.
        private void PrepareLookForGenerate()
        {
            MapLabMapView active = MapLabMapViews.Active;
            view.SetLook(active);
            lookShownId = active != null ? active.Id : string.Empty;
            lookBuiltKey = MapLabMapViews.BuildKey(active);
        }

        // Верхняя строка: выпадающий выбор вида.
        private void DrawViewPopup()
        {
            List<MapLabMapView> views = MapLabMapViews.Load().EnabledViews();
            if (views.Count == 0)
                return;
            MapLabMapView active = MapLabMapViews.Active;
            string[] names = new string[views.Count];
            int index = 0;
            for (int i = 0; i < views.Count; i++)
            {
                names[i] = views[i].Name;
                if (views[i] == active)
                    index = i;
            }
            GUILayout.Label(new GUIContent("Вид", "Облик карты: тот же мир, другая гамма и рисунки. Тот же выбор, что кнопка «Вид: …» в игре."), EditorStyles.miniLabel, GUILayout.Width(26f));
            int next = EditorGUILayout.Popup(index, names, EditorStyles.toolbarPopup, GUILayout.Width(120f));
            if (next != index)
                MapLabMapViews.ActiveId = views[next].Id;
            SyncLook();
        }

        // Вкладка «Карта»: правка выбранного вида.
        private void DrawViewsGroup()
        {
            MapLabMapView shown = MapLabMapViews.Active;
            viewsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(viewsOpen, "Только для вида «" + (shown != null ? shown.Name : "—") + "» · свои настройки");
            if (viewsOpen)
            {
                MapLabMapViews asset = ViewsAsset(false);
                if (asset == null)
                {
                    EditorGUILayout.HelpBox("Виды работают встроенными (Обычный, Мрачный, Сепия). Чтобы править их и добавлять свои — создайте ассет видов.", MessageType.Info);
                    if (GUILayout.Button("Создать ассет видов"))
                        Guard(() =>
                        {
                            ViewsAsset(true);
                            lookShownId = null;
                        });
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    return;
                }
                if (asset.EnsureBuiltIn())
                    EditorUtility.SetDirty(asset);
                EditorGUILayout.HelpBox("Всё ниже относится только к выбранному виду. Общие для всех видов настройки — в разделе «Общие для всех видов» выше.", MessageType.None);
                MapLabMapView v = asset.Find(MapLabMapViews.ActiveId) ?? MapLabMapViews.Active;
                if (v == null)
                {
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    return;
                }
                bool grade = false;
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Название", v.Name);
                bool enabled = EditorGUILayout.Toggle(new GUIContent("Доступен", "Вид есть в переключателе игры и окна."), v.Enabled);
                string note = EditorGUILayout.TextField("Описание", v.Note);
                EditorGUILayout.LabelField("ID: " + v.Id + " · папка рисунков: " + v.ArtFolder, EditorStyles.miniLabel);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Виды карты: вид");
                    v.Name = name;
                    v.Enabled = enabled || v.Id == MapLabMapViews.ClassicId;
                    v.Note = note;
                    EditorUtility.SetDirty(asset);
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Цветокоррекция (сразу)", EditorStyles.boldLabel);
                MapLabViewGrade g = v.Grade ??= MapLabViewGrade.Neutral();
                EditorGUI.BeginChangeCheck();
                float strength = EditorGUILayout.Slider(new GUIContent("Сила вида", "0 — исходный рисунок."), g.Strength, 0f, 1f);
                float brightness = EditorGUILayout.Slider("Яркость", g.Brightness, 0.2f, 2f);
                float contrast = EditorGUILayout.Slider("Контраст", g.Contrast, 0.3f, 2f);
                float saturation = EditorGUILayout.Slider("Насыщенность", g.Saturation, 0f, 2f);
                float gradient = EditorGUILayout.Slider(new GUIContent("Тонирование", "Доля градиентной карты: светлота → тень / середина / свет (1 — чистая сепия)."), g.GradientMix, 0f, 1f);
                Color shadows = EditorGUILayout.ColorField("Тень", g.Shadows);
                Color mids = EditorGUILayout.ColorField("Середина", g.Midtones);
                Color lights = EditorGUILayout.ColorField("Свет", g.Highlights);
                Color tint = EditorGUILayout.ColorField(new GUIContent("Подкраска", "Умножение на цвет."), g.Tint);
                float tintStrength = EditorGUILayout.Slider("Сила подкраски", g.TintStrength, 0f, 1f);
                float warm = EditorGUILayout.Slider(new GUIContent("Тёплые огни", "Окна, фонари, огонь сохраняют свой цвет."), g.KeepWarmLights, 0f, 1f);
                float lift = EditorGUILayout.Slider(new GUIContent("Подъём тени", "Самые тёмные тона не проваливаются в черноту."), g.ShadowLift, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Виды карты: цветокоррекция");
                    g.Strength = strength;
                    g.Brightness = brightness;
                    g.Contrast = contrast;
                    g.Saturation = saturation;
                    g.GradientMix = gradient;
                    g.Shadows = shadows;
                    g.Midtones = mids;
                    g.Highlights = lights;
                    g.Tint = tint;
                    g.TintStrength = tintStrength;
                    g.KeepWarmLights = warm;
                    g.ShadowLift = lift;
                    EditorUtility.SetDirty(asset);
                    grade = true;
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Тёплый свет на земле (сразу)", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                float glow = EditorGUILayout.Slider(new GUIContent("Сила", "Тёплые блики на земле — как подсветка крон. 0 — нет."), g.GroundGlow, 0f, 1f);
                Color glowColor = EditorGUILayout.ColorField("Цвет света", g.GroundGlowColor);
                float glowRelief = EditorGUILayout.Slider(new GUIContent("Склоны к свету", "Блики на склонах, повёрнутых к свету."), g.GroundGlowRelief, 0f, 2f);
                float glowStrokes = EditorGUILayout.Slider(new GUIContent("Светлые мазки", "Блики по светлым мазкам покраски земли."), g.GroundGlowStrokes, 0f, 2f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Виды карты: свет на земле");
                    g.GroundGlow = glow;
                    g.GroundGlowColor = glowColor;
                    g.GroundGlowRelief = glowRelief;
                    g.GroundGlowStrokes = glowStrokes;
                    EditorUtility.SetDirty(asset);
                    grade = true;
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Тушь контуров (сразу)", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                float inkStrength = EditorGUILayout.Slider(new GUIContent("Цвет туши вида", "Насколько контуры дорог, берегов и меж берут цвет туши вида. 0 — как есть."), g.InkStrength, 0f, 1f);
                Color inkColor = EditorGUILayout.ColorField("Цвет туши", g.InkColor);
                float inkDensity = EditorGUILayout.Slider(new GUIContent("Плотность", "1 — как есть; больше — контуры плотнее и темнее."), g.InkDensity, 1f, 3f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Виды карты: тушь");
                    g.InkStrength = inkStrength;
                    g.InkColor = inkColor;
                    g.InkDensity = inkDensity;
                    EditorUtility.SetDirty(asset);
                    grade = true;
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Игра: туман, фон, картинки интерфейса", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                bool fog = EditorGUILayout.Toggle("Свой цвет тумана", v.OverrideFog);
                Color fogColor = v.FogColor;
                using (new EditorGUI.DisabledScope(!fog))
                    fogColor = EditorGUILayout.ColorField("Туман", v.FogColor);
                Color background = EditorGUILayout.ColorField(new GUIContent("Фон под картой", "Пока карта рисуется и за краем полотна."), v.Background);
                Color uiTint = EditorGUILayout.ColorField(new GUIContent("Подкраска значков", "Картинки скрытых мест, которые игра рисует интерфейсом поверх карты."), v.UiTint);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Виды карты: игра");
                    v.OverrideFog = fog;
                    v.FogColor = fogColor;
                    v.Background = background;
                    v.UiTint = uiTint;
                    EditorUtility.SetDirty(asset);
                }

                DrawViewFogClouds(asset, v);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Пересборка: палитра и свои рисунки", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                MapLabPalette palette = (MapLabPalette)EditorGUILayout.ObjectField(new GUIContent("Своя палитра", "Пусто — палитра профиля."), v.Palette, typeof(MapLabPalette), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Виды карты: палитра");
                    v.Palette = palette;
                    EditorUtility.SetDirty(asset);
                    lookShownId = null;
                }
                EditorGUI.BeginChangeCheck();
                bool ungraded = EditorGUILayout.ToggleLeft(new GUIContent("Свои рисунки — без цветокоррекции", "Свои PNG вида нарисованы уже в его гамме: коррекция их не трогает (земля, мазки, тушь — под коррекцией)."), v.OwnArtUngraded);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Виды карты: свои рисунки");
                    v.OwnArtUngraded = ungraded;
                    EditorUtility.SetDirty(asset);
                    lookShownId = null;
                    lookBuiltKey = string.Empty;
                }
                EditorGUILayout.LabelField("Свои рисунки вида: " + v.ArtCount + " из " + CountKnownArt() + " ассетов карты", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Собрать из папки", "PNG в " + v.ArtFolder + " с именем = ID ассета карты (House_Thatch_A_P35.png…). Импорт — как у основного рисунка.")))
                        Guard(() =>
                        {
                            status = CollectViewArt(asset, v, KnownArt());
                            lookShownId = null;
                        });
                    if (GUILayout.Button("Открыть папку"))
                        Guard(() =>
                        {
                            EnsureFolder(v.ArtFolder);
                            EditorUtility.RevealInFinder(v.ArtFolder);
                        });
                    if (GUILayout.Button(new GUIContent("Список ID", "Записать в папку вида список ID всех ассетов карты с размерами холста (для художника).")))
                        Guard(() => status = WriteArtList(v));
                }

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Новый вид", "Копия выбранного вида с новым ID — дальше правится отдельно.")))
                        Guard(() =>
                        {
                            Undo.RecordObject(asset, "Виды карты: новый вид");
                            MapLabMapView copy = v.Clone();
                            copy.Id = "view" + (asset.Views.Count + 1);
                            while (asset.Find(copy.Id) != null)
                                copy.Id += "b";
                            copy.Name = v.Name + " (копия)";
                            copy.Art.Clear();
                            copy.Enabled = true;
                            asset.Views.Add(copy);
                            EditorUtility.SetDirty(asset);
                            MapLabMapViews.ActiveId = copy.Id;
                        });
                    using (new EditorGUI.DisabledScope(v.Id == MapLabMapViews.ClassicId))
                    {
                        if (GUILayout.Button(new GUIContent("Удалить вид", "Прежний облик (Обычный) не удаляется. Папка рисунков остаётся.")) &&
                            EditorUtility.DisplayDialog("Виды карты", "Удалить вид «" + v.Name + "»? Папка его рисунков останется.", "Удалить", "Отмена"))
                            Guard(() =>
                            {
                                Undo.RecordObject(asset, "Виды карты: удалить вид");
                                asset.Views.Remove(v);
                                EditorUtility.SetDirty(asset);
                                MapLabMapViews.ActiveId = MapLabMapViews.ClassicId;
                            });
                    }
                    if (GUILayout.Button("Сохранить виды"))
                        Guard(() =>
                        {
                            EditorUtility.SetDirty(asset);
                            AssetDatabase.SaveAssetIfDirty(asset);
                            status = "Виды карты сохранены.";
                        });
                }
                if (grade)
                    previewDirty = true;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // Все записи наборов карты по ID (для сборки своих рисунков).
        private Dictionary<string, MapLabArtEntry> KnownArt()
        {
            Dictionary<string, MapLabArtEntry> known = new Dictionary<string, MapLabArtEntry>();
            if (view != null)
                AddKnown(known, ActiveSets());
            AddKnown(known, AllArtSets());
            AddExtraKnown(known);
            return known;
        }

        // Все наборы арта MapLab проекта (сцена генератора может держать не все).
        private static IEnumerable<MapLabArtSet> AllArtSets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:MapLabArtSet"))
            {
                MapLabArtSet set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (set != null)
                    yield return set;
            }
        }

        // Записи вне наборов MapLab (ассеты Базы ассетов для арены и леса карты) —
        // подключает мост к игре (KingdomSurvival.MapLabBridge.Editor).
        public static System.Func<IEnumerable<MapLabArtEntry>> ExtraKnownArt;

        private static void AddExtraKnown(Dictionary<string, MapLabArtEntry> known)
        {
            if (ExtraKnownArt == null)
                return;
            foreach (MapLabArtEntry e in ExtraKnownArt())
                if (e != null && !string.IsNullOrEmpty(e.Id) && e.Id.IndexOf('#') < 0 && !known.ContainsKey(e.Id))
                    known[e.Id] = e;
        }

        private static void AddKnown(Dictionary<string, MapLabArtEntry> known, IEnumerable<MapLabArtSet> sets)
        {
            foreach (MapLabArtSet set in sets)
                foreach (MapLabArtEntry e in set.Entries)
                    if (e != null && e.HasTexture && !string.IsNullOrEmpty(e.Id) && !known.ContainsKey(e.Id))
                        known[e.Id] = e;
        }

        // Пакетно: «Собрать из папки» для всех видов, у которых есть папка рисунков.
        //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabGeneratorWindow.CollectViewArtBatch
        public static void CollectViewArtBatch()
        {
            MapLabMapViews asset = ViewsAsset(true);
            Dictionary<string, MapLabArtEntry> known = new Dictionary<string, MapLabArtEntry>();
            AddKnown(known, AllArtSets());
            AddExtraKnown(known);
            foreach (MapLabMapView v in asset.Views)
                if (v != null && AssetDatabase.IsValidFolder(v.ArtFolder))
                    Debug.Log("MAP_VIEW_ART: " + CollectViewArt(asset, v, known));
            MapLabMapViews.ResetCache();
        }

        // ---------- облака тумана войны вида ----------

        // Пункт «Облака тумана войны» выбранного вида: рисунки можно заменить
        // по одному (перетащить PNG в поле), добавить, убрать или собрать из
        // папки вида. Игра подхватывает замену сразу (и в Play Mode); числа
        // облаков — ассет настроек движения, раздел «Облака тумана войны».
        private void DrawViewFogClouds(MapLabMapViews asset, MapLabMapView v)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Облака тумана войны (игра, сразу)", EditorStyles.boldLabel);
            v.FogClouds ??= new List<Texture2D>();
            int remove = -1;
            for (int i = 0; i < v.FogClouds.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    Texture2D t = (Texture2D)EditorGUILayout.ObjectField("Облако " + (i + 1), v.FogClouds[i], typeof(Texture2D), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(asset, "Виды карты: облако тумана");
                        v.FogClouds[i] = t;
                        EditorUtility.SetDirty(asset);
                    }
                    if (GUILayout.Button(new GUIContent("×", "Убрать это облако из вида (файл остаётся)."), GUILayout.Width(22)))
                        remove = i;
                }
            }
            if (remove >= 0)
            {
                Undo.RecordObject(asset, "Виды карты: убрать облако тумана");
                v.FogClouds.RemoveAt(remove);
                EditorUtility.SetDirty(asset);
            }
            // Цвет облаков вида: как у ассетов карты — яркость, тон,
            // насыщенность и уровни (PNG не меняется; игра пересчитывает копии).
            EditorGUI.BeginChangeCheck();
            float cloudBrightness = EditorGUILayout.Slider(new GUIContent("Яркость облаков", "1 — без изменений"), v.FogCloudBrightness, 0.3f, 2f);
            float cloudHue = EditorGUILayout.Slider(new GUIContent("Цветовой тон облаков, °", "0 — без изменений"), v.FogCloudHue, -180f, 180f);
            float cloudSaturation = EditorGUILayout.Slider(new GUIContent("Насыщенность облаков", "1 — без изменений"), v.FogCloudSaturation, 0f, 2f);
            MapLabLevels cloudLevels = MapLabLevelsGUI.Draw(v.FogCloudLevels, "Уровни облаков");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Виды карты: цвет облаков тумана");
                v.FogCloudBrightness = cloudBrightness;
                v.FogCloudHue = cloudHue;
                v.FogCloudSaturation = cloudSaturation;
                (v.FogCloudLevels ??= new MapLabLevels()).CopyFrom(cloudLevels);
                EditorUtility.SetDirty(asset);
            }
            if (!v.FogCloudAdjustNeutral && GUILayout.Button(new GUIContent("Сбросить цвет облаков", "Яркость, тон, насыщенность и уровни облаков — к нейтральным.")))
            {
                Undo.RecordObject(asset, "Виды карты: сброс цвета облаков");
                v.FogCloudBrightness = v.FogCloudSaturation = 1f;
                v.FogCloudHue = 0f;
                (v.FogCloudLevels ??= new MapLabLevels()).Reset();
                EditorUtility.SetDirty(asset);
            }
            if (v.FogCloudCount == 0)
                EditorGUILayout.HelpBox(v.Id == MapLabMapViews.ClassicId
                    ? "Нет облаков: в игре останется только маска тумана."
                    : "Нет своих облаков: игра возьмёт облака вида «Обычный».", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("+ Облако", "Пустое поле — перетащите в него PNG облака.")))
                    Guard(() =>
                    {
                        Undo.RecordObject(asset, "Виды карты: добавить облако тумана");
                        v.FogClouds.Add(null);
                        EditorUtility.SetDirty(asset);
                    });
                if (GUILayout.Button(new GUIContent("Собрать из папки", "Все PNG из " + v.FogCloudsFolder + " — облака вида (импорт: прозрачность, мип-карты, до 2048 px, высокое качество).")))
                    Guard(() => status = CollectFogClouds(asset, v));
                if (GUILayout.Button("Открыть папку"))
                    Guard(() =>
                    {
                        EnsureFolder(v.FogCloudsFolder);
                        EditorUtility.RevealInFinder(v.FogCloudsFolder);
                    });
            }
        }

        // PNG папки облаков вида → список облаков вида (по имени файла).
        private static string CollectFogClouds(MapLabMapViews asset, MapLabMapView v)
        {
            EnsureFolder(v.FogCloudsFolder);
            List<Texture2D> found = new List<Texture2D>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { v.FogCloudsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) ||
                    Path.GetDirectoryName(path)?.Replace('\\', '/') != v.FogCloudsFolder)
                    continue;
                ConfigureFogCloudImporter(path);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                    found.Add(texture);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            Undo.RecordObject(asset, "Виды карты: облака тумана из папки");
            v.FogClouds = found;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return "Облака тумана «" + v.Name + "»: " + found.Count + " из " + v.FogCloudsFolder + ".";
        }

        // Импорт облака: прозрачность, мип-карты (карта отдаляется), без
        // повторения на краях, полный размер до 2048 px, сжатие высокого качества.
        private static void ConfigureFogCloudImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return;
            bool changed = importer.textureType != TextureImporterType.Default ||
                           !importer.alphaIsTransparency || !importer.mipmapEnabled ||
                           importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 2048 ||
                           importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                           importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 4;
            if (!changed)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            // Чёткость: облако на экране бывает шире 1000 px — полный размер
            // рисунка (2048), сжатие высокого качества (BC7, без «грязи» на
            // туши), трилинейная выборка между мип-уровнями при отдалении.
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        // Пакетная сборка облаков всех видов (Unity -batchmode -executeMethod).
        public static void CollectFogCloudsBatch()
        {
            MapLabMapViews asset = ViewsAsset(true);
            asset.EnsureBuiltIn();
            foreach (MapLabMapView v in asset.Views)
                if (v != null && AssetDatabase.IsValidFolder(v.FogCloudsFolder))
                    Debug.Log("MAP_FOG_CLOUDS: " + CollectFogClouds(asset, v));
            AssetDatabase.SaveAssets();
            MapLabMapViews.ResetCache();
        }

        private int knownArtCount = -1;

        private int CountKnownArt()
        {
            if (knownArtCount < 0)
                knownArtCount = KnownArt().Count;
            return knownArtCount;
        }

        private static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
        }

        // PNG папки вида → свои рисунки вида; импорт — как у основного рисунка
        // (прозрачность, читаемость для пятен, размер, сжатие).
        private static string CollectViewArt(MapLabMapViews asset, MapLabMapView v, Dictionary<string, MapLabArtEntry> known)
        {
            EnsureFolder(v.ArtFolder);
            Dictionary<string, bool> wasEnabled = new Dictionary<string, bool>();
            foreach (MapLabViewArtOverride o in v.Art)
                if (o != null && !string.IsNullOrEmpty(o.ArtId))
                    wasEnabled[o.ArtId] = o.Enabled;
            List<MapLabViewArtOverride> next = new List<MapLabViewArtOverride>();
            List<string> unknown = new List<string>();
            List<string> skewed = new List<string>();
            int reimported = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { v.ArtFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string id = MapLabMapView.IdOfFile(Path.GetFileNameWithoutExtension(path));
                if (!known.TryGetValue(id, out MapLabArtEntry entry))
                {
                    unknown.Add(id);
                    continue;
                }
                if (MatchImport(path, entry))
                    reimported++;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                    continue;
                // Пропорции холста — как у исходника (опора и рост берутся из него).
                if (entry.Width > 0 && entry.Height > 0 && Mathf.Abs(texture.width / (float)texture.height - entry.Width / (float)entry.Height) > 0.02f * entry.Width / entry.Height)
                    skewed.Add($"{id} ({texture.width}×{texture.height}, нужно {entry.Width}×{entry.Height})");
                next.Add(new MapLabViewArtOverride { ArtId = id, Texture = texture, Enabled = !wasEnabled.TryGetValue(id, out bool on) || on });
            }
            next.Sort((a, b) => string.CompareOrdinal(a.ArtId, b.ArtId));
            Undo.RecordObject(asset, "Виды карты: свои рисунки");
            v.Art = next;
            v.InvalidateIndex();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            string message = $"Вид «{v.Name}»: своих рисунков {next.Count}" + (reimported > 0 ? $", импорт выровнен у {reimported}" : string.Empty) + ".";
            if (skewed.Count > 0)
                message += " Пропорции холста не совпадают с исходником (рисунок встанет искажённым): " + string.Join(", ", skewed) + ".";
            if (unknown.Count > 0)
            {
                message += " Не найдены ID (имя файла не совпало с ассетом карты): " + string.Join(", ", unknown) + ".";
                Debug.LogWarning(message);
            }
            return message;
        }

        // Настройки импорта своего рисунка — как у основного; true — переимпортирован.
        private static bool MatchImport(string path, MapLabArtEntry entry)
        {
            TextureImporter dst = AssetImporter.GetAtPath(path) as TextureImporter;
            TextureImporter src = entry.Texture != null ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(entry.Texture)) as TextureImporter : null;
            if (dst == null)
                return false;
            TextureImporterSettings want = new TextureImporterSettings();
            if (src != null)
                src.ReadTextureSettings(want);
            else
            {
                dst.ReadTextureSettings(want);
                want.alphaIsTransparency = true;
                want.mipmapEnabled = true;
            }
            TextureImporterSettings have = new TextureImporterSettings();
            dst.ReadTextureSettings(have);
            bool same = EditorJsonUtility.ToJson(want) == EditorJsonUtility.ToJson(have)
                        && (src == null || (dst.maxTextureSize == src.maxTextureSize && dst.textureCompression == src.textureCompression));
            if (same)
                return false;
            dst.SetTextureSettings(want);
            if (src != null)
            {
                dst.maxTextureSize = src.maxTextureSize;
                dst.textureCompression = src.textureCompression;
            }
            dst.SaveAndReimport();
            return true;
        }

        // Список ID ассетов карты с размерами холста — художнику вида.
        private string WriteArtList(MapLabMapView v)
        {
            EnsureFolder(v.ArtFolder);
            List<string> lines = new List<string>
            {
                "# Ассеты карты для вида «" + v.Name + "» (" + v.Id + ")",
                "# Своя версия рисунка: PNG с прозрачностью, ТОТ ЖЕ размер холста и та же опора (основание) — имя файла = ID.",
                "# Имя файла без .png (= ID; у ассетов Базы ассетов — art_…)\tкатегория\tроль\tхолст, px\tесть своя",
            };
            List<MapLabArtEntry> entries = new List<MapLabArtEntry>(KnownArt().Values);
            entries.Sort((a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : string.CompareOrdinal(a.Id, b.Id));
            foreach (MapLabArtEntry e in entries)
                lines.Add($"{MapLabMapView.FileNameOf(e.Id)}\t{e.Category}\t{e.Role}\t{e.Width}x{e.Height}\t{(v.FindTexture(e.Id) != null ? "да" : "—")}");
            string file = Path.Combine(v.ArtFolder, "_ASSET_LIST.txt");
            File.WriteAllLines(file, lines);
            return "Список ID (" + entries.Count + ") записан: " + file;
        }
    }
}
