using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Галерея вариаций «Генератора арен»: арены собираются по одной (два мира
    // MapLab на одном слое одновременно нельзя), кадр каждой — рисунок земли
    // и слой стоящих объектов без доски. Щелчок по карточке — эта тема и seed
    // в большом предпросмотре.
    public sealed partial class ArenaGeneratorWindow
    {
        private const int ThumbWidth = 480, ThumbHeight = 270;
        private const double GalleryStepMs = 30;

        private sealed class Thumb
        {
            public SandboxArenaTheme Theme;
            public int Seed;
            public SandboxBattle Battle;
            public MapLabArena Arena;
            public RenderTexture Ground, Objects;
            public Image GroundImage, ObjectsImage;
            public Label Caption;
        }

        private readonly List<Thumb> thumbs = new List<Thumb>();
        private int thumbIndex = -1;

        private void StartGallery(bool allThemes)
        {
            galleryAllThemes = allThemes;
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                status.text = "Во время игры галерея выключена.";
                return;
            }
            ReleasePreview();
            CancelGallery();
            rebuildAt = -1;
            previewHost.style.display = DisplayStyle.None;
            galleryHost.style.display = DisplayStyle.Flex;
            galleryHost.Clear();
            List<(SandboxArenaTheme Theme, int Seed)> plan = allThemes
                ? SandboxArenaThemes.All.Select(theme => (theme, Seed)).ToList()
                : Enumerable.Range(0, 9).Select(i => (ThemeFor(Seed + i), Seed + i)).ToList();
            foreach ((SandboxArenaTheme theme, int seed) in plan)
            {
                Thumb thumb = new Thumb { Theme = theme, Seed = seed };
                VisualElement card = new VisualElement();
                card.style.width = new Length(32, LengthUnit.Percent);
                card.style.marginLeft = card.style.marginRight = 3;
                card.style.marginBottom = 6;
                VisualElement frame = new VisualElement();
                frame.style.width = new Length(100, LengthUnit.Percent);
                frame.style.height = 150;
                frame.style.backgroundColor = new Color(.08f, .09f, .1f);
                thumb.GroundImage = new Image { scaleMode = ScaleMode.ScaleToFit };
                thumb.ObjectsImage = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                foreach (Image image in new[] { thumb.GroundImage, thumb.ObjectsImage })
                {
                    image.style.position = Position.Absolute;
                    image.style.left = image.style.right = image.style.top = image.style.bottom = 0;
                    frame.Add(image);
                }
                card.Add(frame);
                thumb.Caption = new Label(SandboxArenaThemes.Label(theme) + " · seed " + seed + " — ждёт");
                thumb.Caption.style.whiteSpace = WhiteSpace.Normal;
                thumb.Caption.style.color = new Color(.78f, .76f, .7f);
                card.Add(thumb.Caption);
                Thumb captured = thumb;
                card.RegisterCallback<ClickEvent>(_ => OpenThumb(captured));
                card.tooltip = "Открыть в большом предпросмотре";
                galleryHost.Add(card);
                thumbs.Add(thumb);
            }
            thumbIndex = 0;
            status.text = "Галерея: собирается " + thumbs.Count + " арен по одной…";
            report.text = string.Empty;
        }

        private void OpenThumb(Thumb thumb)
        {
            List<SandboxArenaTheme> all = SandboxArenaThemes.All.ToList();
            ThemeIndex = all.IndexOf(thumb.Theme);
            Seed = thumb.Seed;
            CreateGUI();
        }

        // Шаг галереи; true — галерея идёт (предпросмотр ждёт).
        private bool PumpGallery()
        {
            if (thumbIndex < 0) return false;
            if (thumbIndex >= thumbs.Count)
            {
                thumbIndex = -1;
                status.text = "Галерея готова: " + thumbs.Count + " арен. Щелчок по карточке — в большой предпросмотр.";
                return false;
            }
            Thumb thumb = thumbs[thumbIndex];
            if (thumb.Arena == null)
            {
                try
                {
                    thumb.Battle = CreateBattle(thumb.Theme, thumb.Seed);
                    thumb.Arena = MapLabArena.Begin(MapLabArenaDriver.BuildSpec(Field, thumb.Battle));
                    thumb.Caption.text = SandboxArenaThemes.Label(thumb.Theme) + " · seed " + thumb.Seed + " — собирается…";
                }
                catch (System.Exception exception)
                {
                    thumb.Caption.text = SandboxArenaThemes.Label(thumb.Theme) + " · seed " + thumb.Seed + " — ошибка: " + exception.Message;
                    thumbIndex++;
                }
                return true;
            }
            Stopwatch watch = Stopwatch.StartNew();
            while (thumb.Arena.Building && watch.Elapsed.TotalMilliseconds < GalleryStepMs)
                thumb.Arena.Advance();
            if (thumb.Arena.Building) return true;
            if (thumb.Arena.Ready)
            {
                RenderTexture ground = thumb.Arena.Render(ThumbWidth, ThumbHeight);
                thumb.Ground = Copy(ground);
                thumb.Objects = Copy(thumb.Arena.OverlayTexture);
                thumb.GroundImage.image = thumb.Ground;
                thumb.ObjectsImage.image = thumb.Objects;
                SandboxArenaReport arenaReport = thumb.Battle.ArenaReport;
                thumb.Caption.text = SandboxArenaThemes.Label(thumb.Theme) + " · seed " + thumb.Seed + " · " + (thumb.Arena.BuildMs / 1000.0).ToString("0.0") + " с" +
                                     (arenaReport != null && (arenaReport.Simplified || arenaReport.BareFallback) ? " · " + (arenaReport.BareFallback ? "без особой местности" : "упрощённый вариант") : "");
                thumb.Caption.tooltip = arenaReport?.Summary();
            }
            else
                thumb.Caption.text = SandboxArenaThemes.Label(thumb.Theme) + " · seed " + thumb.Seed + " — " + thumb.Arena.Error;
            thumb.Arena.Dispose();
            thumb.Arena = null;
            thumbIndex++;
            status.text = "Галерея: " + thumbIndex + " из " + thumbs.Count + "…";
            return true;
        }

        private static RenderTexture Copy(RenderTexture source)
        {
            if (source == null) return null;
            RenderTexture copy = new RenderTexture(source.width, source.height, 0, GraphicsFormat.R8G8B8A8_SRGB) { hideFlags = HideFlags.HideAndDontSave, name = "Arena Gallery" };
            copy.Create();
            Graphics.Blit(source, copy);
            return copy;
        }

        private void CancelGallery()
        {
            foreach (Thumb thumb in thumbs)
            {
                thumb.Arena?.Dispose();
                foreach (RenderTexture texture in new[] { thumb.Ground, thumb.Objects })
                {
                    if (texture == null) continue;
                    texture.Release();
                    Object.DestroyImmediate(texture);
                }
            }
            thumbs.Clear();
            thumbIndex = -1;
            galleryHost?.Clear();
        }
    }
}
