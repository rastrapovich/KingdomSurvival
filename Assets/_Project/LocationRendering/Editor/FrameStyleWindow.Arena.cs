using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLabBridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Окно «Обработка кадра» × генерируемая арена боя (21.11.2026): первая
    // строка списка мест — арена. Предпросмотр — настоящий кадр арены MapLab
    // (земля и слой объектов) с её обработкой кадра: общей или своей у вида
    // карты; сравнение «до / после», «Холст», удар (всплеск, вспышка,
    // тряска) и снимок для LUT. Данные — ассет «Генератора арен».
    public sealed partial class FrameStyleWindow
    {
        public const string ArenaId = "__arena";

        private bool ArenaMode => selectedId == ArenaId;

        private MapLabArena arena;
        private ArenaFrameBinding arenaFrame;
        private ArenaGeneratorAsset arenaAsset;
        private int arenaSeed;

        private void RebuildArena()
        {
            ReleaseArena();
            if (EditorApplication.isPlayingOrWillChangePlaymode || fields == null) return;
            arenaAsset = ArenaGeneratorWindow.EnsureAsset();
            arenaAsset.Activate();
            if (arenaSeed == 0) arenaSeed = Random.Range(1, 100000);
            BattlefieldDefinitionData field = fields.GetSandboxBattlefield();
            SandboxArenaTheme[] themes = SandboxArenaThemes.Ordinary;
            SandboxArenaTheme theme = themes[arenaSeed % themes.Length];
            SandboxBattle battle = SandboxRoster.CreateBattle(SandboxRoster.PlayerRoster.Take(4).ToList(), SandboxRoster.EnemyRoster.Take(4),
                arenaSeed, disabledCells: BattlefieldFrame.DisabledCells(field), theme: theme, themeSource: "окно «Обработка кадра»");
            arena = MapLabArena.Begin(MapLabArenaDriver.BuildSpec(field, battle));
            arenaFrame = new ArenaFrameBinding(arena, null);
            if (status != null) status.text = "Собирается арена: " + SandboxArenaThemes.Label(theme) + ", seed " + arenaSeed + "…";
        }

        private void ReleaseArena()
        {
            arenaFrame?.Dispose();
            arenaFrame = null;
            arena?.Dispose();
            arena = null;
        }

        private Vector2 arenaAreaSize;
        private double lastArenaRender;

        private void PumpArena()
        {
            if (arena != null && arena.Ready) RenderArenaFrames();
            if (arena == null || !arena.Building) return;
            Stopwatch watch = Stopwatch.StartNew();
            while (arena.Building && watch.Elapsed.TotalMilliseconds < 30) arena.Advance();
            canvas?.MarkDirtyRepaint();
        }

        private void BuildArenaPanel()
        {
            arenaAsset = ArenaGeneratorWindow.EnsureAsset();
            arenaAsset.Frame.Repair();
            Label title = new Label("Арена боя (генерируемая)") { style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(.88f, .78f, .53f), marginBottom = 4 } };
            panel.Add(title);
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            row.Add(new Button(() => { arenaSeed = Random.Range(1, 100000); RebuildArena(); }) { text = "Другая арена", tooltip = "Новая случайная арена (тема и seed)" });
            row.Add(new Button(ArenaGeneratorWindow.Open) { text = "Генератор арен ↗", tooltip = "Трава, деревья, клетки — окно «Генератор арен»" });
            panel.Add(row);
            FrameEffectsEditor editor = new FrameEffectsEditor
            {
                Database = database,
                DirectPost = () => arenaAsset.Frame.Common,
                ViewFrames = () => arenaAsset.Frame.Views,
                DirectTitle = "Правится: обработка кадра арен боя (все дорожные бои и полигон)",
                Sections = ArenaGeneratorWindow.ArenaFrameSections,
                DirectNote = ArenaGeneratorWindow.ArenaFrameNote,
                Change = (action, rebuild) =>
                {
                    Undo.RecordObject(arenaAsset, "Обработка кадра арены");
                    action();
                    EditorUtility.SetDirty(arenaAsset);
                    ArenaGeneratorAsset.Touch();
                    saveAt = EditorApplication.timeSinceStartup + .5;
                    if (rebuild) BuildPanel();
                    canvas?.MarkDirtyRepaint();
                },
                Rebuild = BuildPanel,
                ViewSelected = () => { arena?.ApplyActiveLook(); canvas?.MarkDirtyRepaint(); },
                Pulse = strength => arenaFrame?.Hit(strength),
                Hit = strength => arenaFrame?.Hit(strength),
                Snapshot = Snapshot,
            };
            panel.Add(editor.Build());
        }

        // Кадр арены (земля + слой объектов) в свою текстуру; before — «до».
        private Texture RenderArena(bool before, ref RenderTexture target, int height)
        {
            LocationFrameStyle style = before && compareSource == CompareSource.Style ? database?.FindFrameStyle(EditorPrefs.GetString("KS.FrameEffects.Style", "")) : null;
            arena.Spec.Frame = before ? style?.Post : ArenaGeneratorAsset.FrameFor(MapLabGameViews.ActiveId);
            arenaFrame?.BeforeRender();
            RenderTexture ground = arena.Render(Mathf.RoundToInt(height * 16f / 9f), height);
            if (ground == null) return null;
            if (target == null || target.width != ground.width || target.height != ground.height)
            {
                Release(ref target);
                target = new RenderTexture(ground.width, ground.height, 0, GraphicsFormat.R8G8B8A8_SRGB) { hideFlags = HideFlags.HideAndDontSave };
                target.Create();
            }
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(ground, target);
            RenderTexture objects = arena.OverlayTexture;
            if (objects != null)
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, target.width, target.height, 0);
                Graphics.DrawTexture(new Rect(0, 0, target.width, target.height), objects);
                GL.PopMatrix();
            }
            RenderTexture.active = active;
            return target;
        }

        // Кадры арены «до» и «после» — в цикле обновления редактора, вне перерисовки окна.
        private void RenderArenaFrames()
        {
            if (EditorApplication.timeSinceStartup - lastArenaRender < 1 / 30.0) return;
            lastArenaRender = EditorApplication.timeSinceStartup;
            if (arenaAreaSize.x < 8f || arenaAreaSize.y < 8f || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Stopwatch watch = Stopwatch.StartNew();
            float aspect = 16f / 9f;
            int height = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(arenaAreaSize.y, arenaAreaSize.x / aspect) * EditorGUIUtility.pixelsPerPoint), 128, 2048);
            if (compare == CompareMode.SideBySide)
            {
                RenderArena(true, ref beforeTexture, height / 2 + 64);
                RenderArena(false, ref afterTexture, height / 2 + 64);
            }
            else
            {
                if (compare == CompareMode.Curtain) RenderArena(true, ref beforeTexture, height);
                RenderArena(false, ref afterTexture, height);
            }
            frameMs = Mathf.Lerp(frameMs, (float)watch.Elapsed.TotalMilliseconds, .2f);
            if (frameLabel != null) frameLabel.text = "кадр " + frameMs.ToString("0.0") + " мс";
        }

        private void DrawArenaPreview(Rect area)
        {
            if (arena == null || !arena.Ready)
            {
                string text = EditorApplication.isPlaying ? "Идёт игра — предпросмотр вернётся после выхода из Play Mode."
                    : arena == null ? "Арена не собрана." : arena.Building ? "Собирается арена… " + Mathf.RoundToInt(arena.Progress * 100) + "%" : arena.Error;
                GUI.Label(new Rect(10, 10, area.width - 20, 40), text);
                return;
            }
            if (Event.current.type == EventType.MouseDrag && compare == CompareMode.Curtain)
            {
                split = Mathf.Clamp01((Event.current.mousePosition.x - area.x) / Mathf.Max(1, area.width));
                Event.current.Use();
            }
            if (Event.current.type != EventType.Repaint) return;
            // Камера арены рисуется не здесь (рендер камеры внутри перерисовки IMGUI с обработкой кадра
            // вешал редактор и давал чёрный кадр), а в RenderArenaFrames из цикла обновления.
            arenaAreaSize = area.size;
            float aspect = 16f / 9f;
            if (compare == CompareMode.SideBySide)
            {
                Rect leftFrame = Fit(new Rect(area.x, area.y, area.width / 2 - 1, area.height), aspect);
                Rect rightFrame = Fit(new Rect(area.x + area.width / 2 + 1, area.y, area.width / 2 - 1, area.height), aspect);
                Texture before = beforeTexture;
                Texture after = afterTexture;
                if (before != null) GUI.DrawTexture(leftFrame, before, ScaleMode.StretchToFill, false);
                if (after != null) GUI.DrawTexture(rightFrame, after, ScaleMode.StretchToFill, false);
                Caption(leftFrame, BeforeName(), true);
                Caption(rightFrame, "после", false);
            }
            else
            {
                Rect frame = Fit(area, aspect);
                Texture before = compare == CompareMode.Curtain ? beforeTexture : null;
                Texture after = afterTexture;
                if (after != null) GUI.DrawTexture(frame, after, ScaleMode.StretchToFill, false);
                if (compare == CompareMode.Curtain && before != null)
                {
                    float x = frame.x + frame.width * split;
                    GUI.DrawTextureWithTexCoords(new Rect(frame.x, frame.y, x - frame.x, frame.height), before, new Rect(0, 0, split, 1), false);
                    EditorGUI.DrawRect(new Rect(x - 1, frame.y, 2, frame.height), new Color(1, .9f, .6f, .9f));
                    Caption(new Rect(frame.x, frame.y, x - frame.x, frame.height), BeforeName(), true);
                    Caption(new Rect(x, frame.y, frame.xMax - x, frame.height), "после", false);
                }
            }
            if (status != null)
            {
                LocationPostEffects post = ArenaGeneratorAsset.FrameFor(MapLabGameViews.ActiveId);
                status.text = (post != null ? "Обработка кадра арены включена" : "Обработка кадра арены ВЫКЛЮЧЕНА") + " · вид «" + MapLabGameViews.ActiveName + "»" +
                              (arenaAsset != null && arenaAsset.Frame.Views.Exists(v => v != null && v.ViewId == MapLabGameViews.ActiveId && v.Own) ? " — своя обработка" : " — общая обработка") +
                              " · seed " + arenaSeed + (compare == CompareMode.Curtain ? " · шторка — перетаскиванием" : "");
            }
            // Вспышка, тряска, плёнка, облака — кадр каждую перерисовку окна.
        }

        private static Rect Fit(Rect area, float aspect)
        {
            float width = Mathf.Min(area.width, area.height * aspect);
            float height = width / aspect;
            return new Rect(area.x + (area.width - width) / 2, area.y + (area.height - height) / 2, width, height);
        }

        // Снимок кадра арены без LUT (для покраски LUT): 1600 по ширине.
        private Texture2D ArenaSnapshot()
        {
            if (arena == null || !arena.Ready) return null;
            LocationPostEffects post = ArenaGeneratorAsset.FrameFor(MapLabGameViews.ActiveId);
            Texture2D lut = post?.Tone?.Lut;
            RenderTexture shot = null;
            try
            {
                if (post != null) post.Tone.Lut = null;
                Texture rendered = RenderArena(false, ref shot, 900);
                if (rendered == null) return null;
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = (RenderTexture)rendered;
                Texture2D result = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBA32, false, false);
                result.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0);
                result.Apply();
                RenderTexture.active = active;
                return result;
            }
            finally
            {
                if (post != null) post.Tone.Lut = lut;
                Release(ref shot);
            }
        }
    }
}
