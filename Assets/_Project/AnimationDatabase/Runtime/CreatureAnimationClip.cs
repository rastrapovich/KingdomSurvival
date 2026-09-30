using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase
{
    // Расчёт времени кадров. Один и тот же для предпросмотра и боя.
    public static class CreatureAnimationTiming
    {
        // Длительность каждого кадра в секундах.
        // Обычный режим: 1 / FPS. «Исходный темп»: разница соседних номеров
        // Blender / исходный FPS; последний кадр держится обычный шаг экспорта
        // (самый частый интервал), даже если последний интервал короче.
        public static float[] BuildFrameDurations(
            int frameCount,
            float framesPerSecond,
            bool useSourceTiming,
            IReadOnlyList<int> sourceNumbers,
            float sourceFramesPerSecond)
        {
            if (frameCount <= 0)
                return Array.Empty<float>();

            float[] durations = new float[frameCount];
            if (useSourceTiming && sourceFramesPerSecond > 0f && AreUsable(sourceNumbers, frameCount))
            {
                int step = MostCommonInterval(sourceNumbers);
                for (int i = 0; i < frameCount - 1; i++)
                    durations[i] = (sourceNumbers[i + 1] - sourceNumbers[i]) / sourceFramesPerSecond;
                durations[frameCount - 1] = step / sourceFramesPerSecond;
                return durations;
            }

            float safeFps = Mathf.Clamp(framesPerSecond, CreatureAnimationClipData.MinFramesPerSecond, CreatureAnimationClipData.MaxFramesPerSecond);
            float frameDuration = 1f / safeFps;
            for (int i = 0; i < frameCount; i++)
                durations[i] = frameDuration;
            return durations;
        }

        public static bool AreUsable(IReadOnlyList<int> sourceNumbers, int frameCount)
        {
            if (sourceNumbers == null || sourceNumbers.Count != frameCount || frameCount == 0)
                return false;
            for (int i = 1; i < sourceNumbers.Count; i++)
            {
                if (sourceNumbers[i] <= sourceNumbers[i - 1])
                    return false;
            }
            return true;
        }

        // Шаг экспорта: 1, 4, 7, …, 19, 20 → 3. При равенстве — меньший.
        public static int MostCommonInterval(IReadOnlyList<int> sourceNumbers)
        {
            if (sourceNumbers == null || sourceNumbers.Count < 2)
                return 1;
            Dictionary<int, int> counts = new Dictionary<int, int>();
            for (int i = 1; i < sourceNumbers.Count; i++)
            {
                int interval = sourceNumbers[i] - sourceNumbers[i - 1];
                counts.TryGetValue(interval, out int count);
                counts[interval] = count + 1;
            }
            int best = 1;
            int bestCount = -1;
            foreach (KeyValuePair<int, int> pair in counts)
            {
                if (pair.Value > bestCount || (pair.Value == bestCount && pair.Key < best))
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }
            return Mathf.Max(1, best);
        }

        public static float Sum(float[] durations)
        {
            float total = 0f;
            if (durations != null)
            {
                for (int i = 0; i < durations.Length; i++)
                    total += durations[i];
            }
            return total;
        }

        // Номер кадра в момент time с начала клипа.
        public static int GetFrameIndex(float[] durations, float time, CreatureAnimationPlayback playback)
        {
            if (durations == null || durations.Length == 0)
                return 0;
            float total = Sum(durations);
            if (total <= 0f)
                return 0;

            float t = Mathf.Max(0f, time);
            if (playback == CreatureAnimationPlayback.Loop)
                t %= total;
            else if (t >= total)
                return durations.Length - 1;

            float accumulated = 0f;
            for (int i = 0; i < durations.Length; i++)
            {
                accumulated += durations[i];
                if (t < accumulated)
                    return i;
            }
            return durations.Length - 1;
        }

        // Момент начала кадра, секунды.
        public static float GetFrameStart(float[] durations, int frameIndex)
        {
            float start = 0f;
            if (durations == null)
                return 0f;
            int count = Mathf.Clamp(frameIndex, 0, durations.Length);
            for (int i = 0; i < count; i++)
                start += durations[i];
            return start;
        }
    }

    // Клип, выбранный для показа: с учётом подстановок и без пустых кадров.
    public sealed class CreatureAnimationClip
    {
        private readonly List<Sprite> frames;
        private readonly float[] durations;

        public CreatureAnimationAction RequestedAction { get; }
        public CreatureAnimationAction Action { get; }
        public CreatureAnimationDirection RequestedDirection { get; }
        public CreatureAnimationDirection Direction { get; }
        public CreatureAnimationClipData Data { get; }
        public CreatureAnimationFrames Cell { get; }
        public IReadOnlyList<Sprite> Frames => frames;
        public int FrameCount => frames.Count;
        public float Duration { get; }
        public CreatureAnimationPlayback Playback => Data.Playback;
        public Vector2 Offset => Cell.Offset;

        public bool IsActionSubstitute => Action != RequestedAction;
        public bool IsDirectionSubstitute => Direction != RequestedDirection;

        // Момент удара в секундах от начала клипа.
        public float ImpactSeconds => Duration * Data.ImpactTime;

        internal CreatureAnimationClip(
            CreatureAnimationAction requestedAction,
            CreatureAnimationDirection requestedDirection,
            CreatureAnimationClipData data,
            CreatureAnimationFrames cell,
            List<Sprite> frames,
            List<int> sourceNumbers)
        {
            RequestedAction = requestedAction;
            RequestedDirection = requestedDirection;
            Data = data;
            Cell = cell;
            Action = data.Action;
            Direction = cell.Direction;
            this.frames = frames;
            durations = CreatureAnimationTiming.BuildFrameDurations(
                frames.Count,
                data.FramesPerSecond,
                data.UseSourceTiming,
                sourceNumbers,
                data.SourceFramesPerSecond);
            Duration = CreatureAnimationTiming.Sum(durations);
        }

        public int GetFrameIndex(float time)
        {
            return CreatureAnimationTiming.GetFrameIndex(durations, time, Playback);
        }

        public Sprite GetFrame(float time)
        {
            return frames.Count > 0 ? frames[GetFrameIndex(time)] : null;
        }

        public float GetFrameStart(int frameIndex)
        {
            return CreatureAnimationTiming.GetFrameStart(durations, frameIndex);
        }

        public float GetFrameDuration(int frameIndex)
        {
            return frameIndex >= 0 && frameIndex < durations.Length ? durations[frameIndex] : 0f;
        }

        public bool IsFinished(float time)
        {
            return Playback != CreatureAnimationPlayback.Loop && time >= Duration;
        }
    }

    // Общие предсказуемые подстановки неполного набора (бриф 12З, §12).
    public static class CreatureAnimationResolver
    {
        private static readonly CreatureAnimationAction[] IdleOnly = { CreatureAnimationAction.Idle };

        public static IReadOnlyList<CreatureAnimationAction> GetFallbackChain(CreatureAnimationAction action)
        {
            switch (action)
            {
                case CreatureAnimationAction.Idle:
                    return IdleOnly;
                case CreatureAnimationAction.Walk:
                case CreatureAnimationAction.Hit:
                case CreatureAnimationAction.Block:
                case CreatureAnimationAction.Victory:
                case CreatureAnimationAction.Taunt:
                    return new[] { action, CreatureAnimationAction.Idle };
                case CreatureAnimationAction.Attack:
                    return new[] { CreatureAnimationAction.Attack, CreatureAnimationAction.Shoot };
                case CreatureAnimationAction.Shoot:
                    return new[] { CreatureAnimationAction.Shoot, CreatureAnimationAction.Attack };
                case CreatureAnimationAction.SpecialAttack:
                    return new[] { CreatureAnimationAction.SpecialAttack, CreatureAnimationAction.Attack, CreatureAnimationAction.Shoot };
                case CreatureAnimationAction.Death:
                    return new[] { CreatureAnimationAction.Death };
                default:
                    return new[] { action };
            }
        }

        // Null — показать нечего: вызывающий берёт статичную картинку.
        public static CreatureAnimationClip Resolve(
            CreatureAnimationSetData set,
            CreatureAnimationAction action,
            CreatureAnimationDirection direction,
            string clipKey = null)
        {
            if (set == null)
                return null;

            IReadOnlyList<CreatureAnimationAction> chain = GetFallbackChain(action);
            for (int i = 0; i < chain.Count; i++)
            {
                CreatureAnimationAction candidate = chain[i];
                CreatureAnimationClipData data = null;
                if (candidate == CreatureAnimationAction.SpecialAttack && !string.IsNullOrEmpty(clipKey))
                    data = WithFrames(set.FindClip(candidate, clipKey));
                if (data == null)
                    data = WithFrames(set.FindClip(candidate));
                if (data == null)
                    continue;

                CreatureAnimationClip clip = ResolveDirection(action, direction, data);
                if (clip != null)
                    return clip;
            }
            return null;
        }

        // Ближайший имеющийся ракурс этого действия: сначала точный, затем
        // соседние по кругу (по часовой, против часовой), затем дальше.
        public static CreatureAnimationClip ResolveDirection(
            CreatureAnimationAction requestedAction,
            CreatureAnimationDirection direction,
            CreatureAnimationClipData data)
        {
            int count = CreatureAnimationLabels.DirectionCount;
            int start = (int)direction;
            for (int distance = 0; distance <= count / 2; distance++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    if (distance == 0 && sign < 0)
                        continue;
                    int index = ((start + sign * distance) % count + count) % count;
                    CreatureAnimationFrames cell = data.FindDirection((CreatureAnimationDirection)index);
                    CreatureAnimationClip clip = Build(requestedAction, direction, data, cell);
                    if (clip != null)
                        return clip;
                }
            }
            return null;
        }

        private static CreatureAnimationClipData WithFrames(CreatureAnimationClipData data)
        {
            return data != null && data.HasAnyFrames ? data : null;
        }

        private static CreatureAnimationClip Build(
            CreatureAnimationAction requestedAction,
            CreatureAnimationDirection requestedDirection,
            CreatureAnimationClipData data,
            CreatureAnimationFrames cell)
        {
            if (cell == null || cell.FrameCount == 0)
                return null;

            // Пропавшие картинки (удалён файл) не показываются и не ломают темп.
            List<Sprite> frames = new List<Sprite>(cell.FrameCount);
            List<int> numbers = cell.HasUsableSourceNumbers ? new List<int>(cell.FrameCount) : null;
            for (int i = 0; i < cell.FrameCount; i++)
            {
                if (cell.Frames[i] == null)
                    continue;
                frames.Add(cell.Frames[i]);
                numbers?.Add(cell.SourceFrameNumbers[i]);
            }
            if (frames.Count == 0)
                return null;
            return new CreatureAnimationClip(requestedAction, requestedDirection, data, cell, frames, numbers);
        }
    }

    // Состояние воспроизведения одного бойца или одного окна предпросмотра.
    // Кадры общие для всех, часы и состояние — свои у каждого экземпляра.
    public sealed class CreatureAnimationPlayer
    {
        private CreatureAnimationClip clip;
        private float startedAt;
        private bool clipResolved;

        public CreatureAnimationSetData Set { get; }
        public CreatureAnimationAction Action { get; private set; } = CreatureAnimationAction.Idle;
        public CreatureAnimationDirection Direction { get; private set; }
        public string ClipKey { get; private set; } = string.Empty;
        public bool IsDead { get; private set; }
        // Сдвиг фазы циклов, чтобы одинаковые существа не шагали в ногу.
        public float LoopPhase { get; }

        public CreatureAnimationPlayer(CreatureAnimationSetData set, CreatureAnimationDirection direction, float loopPhase = 0f)
        {
            Set = set;
            Direction = direction;
            LoopPhase = Mathf.Max(0f, loopPhase);
        }

        public CreatureAnimationClip Clip
        {
            get
            {
                if (!clipResolved)
                {
                    clip = CreatureAnimationResolver.Resolve(Set, Action, Direction, ClipKey);
                    clipResolved = true;
                }
                return clip;
            }
        }

        public float StartedAt => startedAt;

        // Смерть имеет приоритет: павший не возвращается к живым действиям.
        public bool Play(CreatureAnimationAction action, float time, string clipKey = null)
        {
            if (IsDead && action != CreatureAnimationAction.Death)
                return false;
            Action = action;
            ClipKey = clipKey ?? string.Empty;
            IsDead = action == CreatureAnimationAction.Death;
            startedAt = time;
            clipResolved = false;
            return true;
        }

        // Смена ракурса не перезапускает клип: ходьба продолжает шаг.
        public void SetDirection(CreatureAnimationDirection direction)
        {
            if (Direction == direction)
                return;
            Direction = direction;
            clipResolved = false;
        }

        public float GetElapsed(float time)
        {
            float elapsed = Mathf.Max(0f, time - startedAt);
            CreatureAnimationClip current = Clip;
            if (current != null && current.Playback == CreatureAnimationPlayback.Loop)
                elapsed += LoopPhase;
            return elapsed;
        }

        // Однократное действие закончилось: пора к следующему шагу очереди.
        public bool IsFinished(float time)
        {
            CreatureAnimationClip current = Clip;
            return current == null || current.IsFinished(GetElapsed(time));
        }

        // Длительность текущего действия; 0 — клипа нет, ждать нечего.
        public float Duration
        {
            get
            {
                CreatureAnimationClip current = Clip;
                return current != null ? current.Duration : 0f;
            }
        }

        // Секунды от начала действия до маркера удара; 0 — клипа нет.
        public float ImpactSeconds
        {
            get
            {
                CreatureAnimationClip current = Clip;
                return current != null ? current.ImpactSeconds : 0f;
            }
        }

        // Кадр в момент time. Однократное живое действие по окончании само
        // возвращается к ожиданию; смерть держит последний кадр.
        public Sprite Evaluate(float time)
        {
            CreatureAnimationClip current = Clip;
            if (current != null && !IsDead && current.Playback == CreatureAnimationPlayback.Once &&
                Action != CreatureAnimationAction.Idle && current.IsFinished(GetElapsed(time)))
            {
                Play(CreatureAnimationAction.Idle, time);
                current = Clip;
            }
            if (current == null)
                return null;
            float elapsed = GetElapsed(time);
            if (IsDead && current.Playback == CreatureAnimationPlayback.Once && elapsed >= current.Duration)
                return current.Frames[current.FrameCount - 1];
            return current.GetFrame(elapsed);
        }
    }
}
