using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLab;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 5: облик программных
    // поверхностей доски.
    //   • Слитые пятна: одна заливка на связный участок по маске гексов
    //     (контур — граница участка), без перекрытия соседних заливок и швов.
    //   • Три вида карты: classic — природные спокойные заливки; gloom —
    //     приглушённые серо-бурые массы с сизо-лиловыми тенями; sepia —
    //     кремовые поверхности и тёмно-коричневая тушь без цветного остатка.
    //     Вид меняет только палитру — клетки, логика и попадание мышью те же.
    //   • Временная область появляется и исчезает за 0,4 с; логика и урон не
    //     ждут анимации.
    // Это временная читаемая графика до художественных ассетов, не арт.
    public sealed partial class HexBoardElement
    {
        private const float EffectFadeSeconds = 0.4f;

        private string terrainViewId = MapLabMapViews.ClassicId;
        private float effectFade = 1f;
        private bool effectFillDone;
        private readonly Dictionary<(HexCoord, SandboxEffectKind), float> effectShownAt = new Dictionary<(HexCoord, SandboxEffectKind), float>();
        private readonly Dictionary<(HexCoord, SandboxEffectKind), (SandboxCellEffect Effect, float RemovedAt)> effectGhosts =
            new Dictionary<(HexCoord, SandboxEffectKind), (SandboxCellEffect, float)>();
        private bool fadeRepaintQueued;
        private readonly Dictionary<(HexCoord, SandboxEffectKind), SandboxCellEffect> effectLast = new Dictionary<(HexCoord, SandboxEffectKind), SandboxCellEffect>();

        // Клетки, где MapLab-арена уже нарисовала объект местности артом.
        public readonly HashSet<HexCoord> ArtBackedObjects = new HashSet<HexCoord>();

        private void InitTerrainLook()
        {
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                MapLabMapViews.ActiveChanged -= OnMapViewChanged;
                MapLabMapViews.ActiveChanged += OnMapViewChanged;
            });
            RegisterCallback<DetachFromPanelEvent>(_ => MapLabMapViews.ActiveChanged -= OnMapViewChanged);
        }

        private void OnMapViewChanged()
        {
            MarkDirtyRepaint();
            SyncUnitImages();
        }

        private void BeginTerrainFrame()
        {
            string id = MapLabMapViews.Active?.Id;
            terrainViewId = string.IsNullOrEmpty(id) ? MapLabMapViews.ClassicId : id;
            effectFade = 1f;
            effectFillDone = false;
        }

        // Цвет поверхности в текущем виде с учётом появления области.
        private Color Tc(float r, float g, float b, float a)
        {
            Color c = Tone(new Color(r, g, b, a));
            c.a *= effectFade;
            return c;
        }

        private Color Tone(Color c)
        {
            if (terrainViewId == MapLabMapViews.SepiaId)
            {
                // Монохром: кремовая поверхность ↔ тёмно-коричневая тушь.
                float lum = c.r * 0.30f + c.g * 0.59f + c.b * 0.11f;
                Color ink = new Color(0.30f, 0.20f, 0.12f), cream = new Color(0.93f, 0.86f, 0.72f);
                Color mono = Color.Lerp(ink, cream, Mathf.Clamp01(lum * 1.15f));
                return new Color(mono.r, mono.g, mono.b, Mathf.Min(1f, c.a * 1.1f));
            }
            if (terrainViewId == MapLabMapViews.GloomId)
            {
                // Матовые серо-бурые массы, тени уходят в сизо-лиловый.
                Color.RGBToHSV(c, out float h, out float s, out float v);
                Color muted = Color.HSVToRGB(h, s * 0.45f, v * 0.78f);
                Color shadow = new Color(0.36f, 0.33f, 0.44f);
                Color mixed = Color.Lerp(muted, shadow, Mathf.Clamp01((0.55f - v) * 0.6f));
                return new Color(mixed.r, mixed.g, mixed.b, c.a);
            }
            return c;
        }

        private (Color Fill, Color Edge) GroundFill(SandboxGround ground)
        {
            switch (ground)
            {
                case SandboxGround.Mud: return (Tc(0.40f, 0.29f, 0.18f, 0.42f), Tc(0.30f, 0.21f, 0.13f, 0.45f));
                case SandboxGround.Bog: return (Tc(0.20f, 0.24f, 0.17f, 0.62f), Tc(0.14f, 0.17f, 0.11f, 0.55f));
                case SandboxGround.Rubble: return (Tc(0.50f, 0.48f, 0.44f, 0.30f), Color.clear);
                case SandboxGround.DeepSnow: return (Tc(0.92f, 0.93f, 0.95f, 0.55f), Tc(0.70f, 0.74f, 0.82f, 0.5f));
                case SandboxGround.Ice: return (Tc(0.72f, 0.83f, 0.88f, 0.55f), Tc(0.55f, 0.68f, 0.76f, 0.55f));
                case SandboxGround.DeepWater: return (Tc(0.13f, 0.22f, 0.30f, 0.78f), Tc(0.30f, 0.26f, 0.18f, 0.7f));
                case SandboxGround.Cliff: return (Tc(0.22f, 0.21f, 0.20f, 0.85f), Tc(0.10f, 0.09f, 0.08f, 0.9f));
                case SandboxGround.Road: return (Tc(0.70f, 0.60f, 0.43f, 0.30f), Color.clear);
                case SandboxGround.Sand: return (Tc(0.82f, 0.74f, 0.52f, 0.38f), Color.clear);
                case SandboxGround.WetGround: return (Tc(0.33f, 0.27f, 0.20f, 0.30f), Color.clear);
                // Мелководье рисует прежнее водное состояние клетки.
                default: return (Color.clear, Color.clear);
            }
        }

        private Color EffectFill(SandboxEffectKind kind)
        {
            switch (kind)
            {
                case SandboxEffectKind.Fire: return Tc(0.85f, 0.38f, 0.12f, 0.30f);
                case SandboxEffectKind.Tar: return Tc(0.07f, 0.06f, 0.05f, 0.62f);
                case SandboxEffectKind.Poison: return Tc(0.45f, 0.58f, 0.18f, 0.40f);
                case SandboxEffectKind.Ice: return Tc(0.80f, 0.90f, 0.95f, 0.60f);
                default: return Color.clear;
            }
        }

        // Связный участок одной заливкой: граница — рёбра гексов, которые не
        // делит соседняя клетка участка. В режиме отметок под фигурами —
        // гексами встык (без перекрытия).
        private void PaintRegion(Painter2D painter, HexLayout layout, List<HexCoord> cells, Color fill, Color edge)
        {
            if (cells.Count == 0)
                return;
            float size = layout.Size;
            float vs = layout.VerticalScale;
            if (marksDrawnBelow)
            {
                foreach (HexCoord cell in cells)
                    PaintHex(painter, layout.GetCenter(cell), size, fill, Color.clear, 0f, true, vs);
                return;
            }
            IBattlefieldRelief reliefNow = paintLayout.Relief;
            Rect frame = paintLayout.Frame;
            Vector2Int Key(Vector2 p) => new Vector2Int(Mathf.RoundToInt(p.x * 4f), Mathf.RoundToInt(p.y * 4f));

            Dictionary<(Vector2Int, Vector2Int), int> count = new Dictionary<(Vector2Int, Vector2Int), int>();
            List<(Vector2 A, Vector2 B, Vector2Int KA, Vector2Int KB)> edges = new List<(Vector2, Vector2, Vector2Int, Vector2Int)>();
            foreach (HexCoord cell in cells)
            {
                Vector2 center = layout.GetCenter(cell);
                Vector2 flat = reliefNow != null ? BattlefieldReliefMath.Lower(reliefNow, frame, center) : center;
                Vector2[] corners = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float angle = Mathf.Deg2Rad * (60f * i - 30f);
                    corners[i] = flat + new Vector2(Mathf.Cos(angle) * size, Mathf.Sin(angle) * size * vs);
                }
                for (int i = 0; i < 6; i++)
                {
                    Vector2 a = corners[i], b = corners[(i + 1) % 6];
                    Vector2Int ka = Key(a), kb = Key(b);
                    (Vector2Int, Vector2Int) key = ka.x < kb.x || (ka.x == kb.x && ka.y < kb.y) ? (ka, kb) : (kb, ka);
                    count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
                    edges.Add((a, b, ka, kb));
                }
            }
            // Граничные рёбра сохраняют обход своей клетки — петли собираются
            // по вершинам.
            Dictionary<Vector2Int, (Vector2 A, Vector2 B, Vector2Int KB)> next = new Dictionary<Vector2Int, (Vector2, Vector2, Vector2Int)>();
            foreach ((Vector2 a, Vector2 b, Vector2Int ka, Vector2Int kb) in edges)
            {
                (Vector2Int, Vector2Int) key = ka.x < kb.x || (ka.x == kb.x && ka.y < kb.y) ? (ka, kb) : (kb, ka);
                if (count[key] == 1)
                    next[ka] = (a, b, kb);
            }
            Vector2 Show(Vector2 p) => reliefNow != null ? BattlefieldReliefMath.Raise(reliefNow, frame, p) : p;

            painter.BeginPath();
            HashSet<Vector2Int> used = new HashSet<Vector2Int>();
            foreach (Vector2Int start in next.Keys.ToList())
            {
                if (used.Contains(start))
                    continue;
                Vector2Int cursor = start;
                bool first = true;
                int guard = 0;
                while (!used.Contains(cursor) && next.TryGetValue(cursor, out (Vector2 A, Vector2 B, Vector2Int KB) step) && guard++ < 4096)
                {
                    used.Add(cursor);
                    if (first)
                    {
                        painter.MoveTo(Show(step.A));
                        first = false;
                    }
                    painter.LineTo(Show(step.B));
                    cursor = step.KB;
                }
                if (!first)
                    painter.ClosePath();
            }
            painter.fillColor = fill;
            painter.Fill(FillRule.OddEven);
            if (edge.a > 0.001f)
            {
                painter.strokeColor = edge;
                painter.lineWidth = 1.3f;
                painter.Stroke();
            }
        }

        // Временные области: полные — слитыми пятнами, появляющиеся и
        // исчезающие — по клетке с прозрачностью.
        private void DrawEffectsWithFade(Painter2D painter, HexLayout layout)
        {
            float now = Time.realtimeSinceStartup;
            HashSet<(HexCoord, SandboxEffectKind)> present = new HashSet<(HexCoord, SandboxEffectKind)>();
            List<(HexCoord Cell, SandboxCellEffect Effect, float Fade)> draw = new List<(HexCoord, SandboxCellEffect, float)>();
            foreach (KeyValuePair<HexCoord, List<SandboxCellEffect>> pair in battle.CellEffects)
            {
                if (!IsBoardCell(pair.Key))
                    continue;
                foreach (SandboxCellEffect effect in pair.Value)
                {
                    (HexCoord, SandboxEffectKind) key = (pair.Key, effect.Kind);
                    if (!present.Add(key))
                        continue;
                    if (!effectShownAt.TryGetValue(key, out float shown))
                        effectShownAt[key] = shown = now;
                    effectGhosts.Remove(key);
                    effectLast[key] = effect;
                    draw.Add((pair.Key, effect, Mathf.Clamp01((now - shown) / EffectFadeSeconds)));
                }
            }
            foreach ((HexCoord, SandboxEffectKind) key in effectShownAt.Keys.Where(k => !present.Contains(k)).ToList())
            {
                effectShownAt.Remove(key);
                if (effectLast.TryGetValue(key, out SandboxCellEffect last))
                    effectGhosts[key] = (last, now);
                effectLast.Remove(key);
            }
            foreach (KeyValuePair<(HexCoord, SandboxEffectKind), (SandboxCellEffect Effect, float RemovedAt)> ghost in effectGhosts.ToList())
            {
                float fade = 1f - (now - ghost.Value.RemovedAt) / EffectFadeSeconds;
                if (fade <= 0f)
                    effectGhosts.Remove(ghost.Key);
                else
                    draw.Add((ghost.Key.Item1, ghost.Value.Effect, fade));
            }

            // Полностью проявленные заливки — одним участком на вид.
            effectFade = 1f;
            foreach (IGrouping<SandboxEffectKind, (HexCoord Cell, SandboxCellEffect Effect, float Fade)> group in draw
                         .Where(item => item.Fade >= 1f).GroupBy(item => item.Effect.Kind))
            {
                Color fill = EffectFill(group.Key);
                if (fill.a > 0f)
                    PaintRegion(painter, layout, group.Select(item => item.Cell).Distinct().ToList(), fill, Color.clear);
            }
            bool fading = false;
            foreach ((HexCoord cell, SandboxCellEffect effect, float fade) in draw)
            {
                effectFade = fade;
                effectFillDone = fade >= 1f;
                fading |= fade < 1f;
                DrawCellEffect(painter, layout, cell, effect);
            }
            effectFade = 1f;
            effectFillDone = false;
            if (fading && !fadeRepaintQueued)
            {
                fadeRepaintQueued = true;
                schedule.Execute(() =>
                {
                    fadeRepaintQueued = false;
                    MarkDirtyRepaint();
                }).StartingIn(16);
            }
        }
    }
}
