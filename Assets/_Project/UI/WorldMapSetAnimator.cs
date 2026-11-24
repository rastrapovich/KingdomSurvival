using KingdomSurvival.AnimationDatabase;
using UnityEngine;

// Анимация фигуры глобальной карты из набора раздела «Глобальная карта»
// Базы анимаций: выбор кадра, темп под скорость фигуры и чистая математика
// (UV кадра в атласе, шаг по земле, опора с поправкой ракурса). Состояние —
// у каждой фигуры своё; кадры и клипы общие.
public sealed class WorldMapSetAnimator
{
    // Сглаживание измеренной скорости (1/с): шаги по клеткам неровные.
    public const float SpeedSharpness = 10f;
    // Скачок дальше этой скорости (клеток/с) — не шаг, а перенос: не в счёт.
    public const float MaxSampleHexesPerSecond = 20f;

    private CreatureAnimationSetData animation;
    private CreatureAnimationPlayer player;
    private float clock;
    private float speedHexesPerSecond;
    private bool wasRunning;
    private bool fresh = true;

    // Клип и отражение последнего кадра (после Tick).
    public CreatureAnimationClip Clip => player != null ? player.Clip : null;
    public bool IsMirrored => player != null && player.IsMirrored;
    public float SpeedHexesPerSecond => speedHexesPerSecond;

    // Кадр на текущий момент. dt — шаг тех же часов, в которых измерен
    // stepHexes (клетки карты, пройденные за этот шаг по земле). На стоянке —
    // Idle набора, а если его нет, первый кадр ходьбы текущего ракурса.
    public Sprite Tick(WorldMapAnimationSetData set, CreatureAnimationDirection direction, bool running, float dt, float stepHexes, bool syncCadence)
    {
        if (set == null)
            return null;
        if (dt > 0f)
            clock += dt;
        if (player == null || !ReferenceEquals(animation, set.Animation))
        {
            animation = set.Animation;
            player = new CreatureAnimationPlayer(animation, direction);
            wasRunning = false;
            fresh = true;
            speedHexesPerSecond = 0f;
        }

        if (!running)
            speedHexesPerSecond = 0f;
        else if (dt > 0f)
        {
            float sample = Mathf.Min(Mathf.Max(0f, stepHexes) / dt, MaxSampleHexesPerSecond);
            speedHexesPerSecond = wasRunning
                ? Mathf.Lerp(speedHexesPerSecond, sample, 1f - Mathf.Exp(-SpeedSharpness * dt))
                : sample;
        }

        CreatureAnimationAction want = running || !HasIdle(animation)
            ? CreatureAnimationAction.Walk
            : CreatureAnimationAction.Idle;
        // Начало бега — с первого кадра ходьбы, как и стоит фигура на стоянке.
        if (fresh || want != player.Action || (running && !wasRunning))
            player.Play(want, clock);
        player.SetDirection(direction);

        CreatureAnimationClip clip = player.Clip;
        wasRunning = running;
        fresh = false;
        if (clip == null)
            return null;

        if (running && syncCadence && want == CreatureAnimationAction.Walk)
            player.SetRate(set.GetWalkRate(clip.Duration, speedHexesPerSecond), clock);
        else
            player.SetRate(1f, clock);

        bool frozen = !running && want == CreatureAnimationAction.Walk;
        return frozen ? clip.GetFrame(0f) : player.Evaluate(clock);
    }

    private static bool HasIdle(CreatureAnimationSetData set)
    {
        CreatureAnimationClipData idle = set.FindClip(CreatureAnimationAction.Idle);
        return idle != null && idle.HasAnyFrames;
    }

    // Клетки карты, пройденные по земле. motionPercent — смещение в процентах
    // полотна; canvasHeightOverWidth — отношение сторон полотна; heightScale —
    // сжатие карты наклоном камеры по высоте (1 — без сжатия); hexPercent —
    // ширина клетки в процентах ширины полотна.
    public static float GroundHexes(Vector2 motionPercent, float canvasHeightOverWidth, float heightScale, float hexPercent)
    {
        if (hexPercent <= 0.0001f)
            return 0f;
        float groundY = motionPercent.y * canvasHeightOverWidth / Mathf.Max(0.2f, heightScale);
        return Mathf.Sqrt(motionPercent.x * motionPercent.x + groundY * groundY) / hexPercent;
    }

    // Холст кадра: из набора, а если не задан — из размера кадра.
    public static Vector2 CanvasOf(CreatureAnimationSetData set, Sprite sprite)
    {
        return set.CanvasSize.x > 0 && set.CanvasSize.y > 0 ? (Vector2)set.CanvasSize : sprite.rect.size;
    }

    // Опора на кадре: опора набора с поправкой ракурса; у отражённого кадра
    // зеркалятся опора X и поправка (ноги остаются в точке фигуры).
    public static Vector2 CardPivot(CreatureAnimationSetData set, CreatureAnimationClip clip, bool mirrored)
    {
        Vector2 pivot = set.Pivot;
        Vector2 offset = clip != null ? clip.Offset : Vector2.zero;
        if (mirrored)
        {
            pivot.x = 1f - pivot.x;
            offset.x = -offset.x;
        }
        return pivot - offset;
    }

    // Прямоугольник кадра в текстуре-атласе: (uMin, vMin, uMax, vMax), с
    // отступом в полпикселя от соседних кадров. Отражённый — uMin > uMax.
    public static Vector4 FrameUv(Sprite sprite, bool mirrored)
    {
        Texture texture = sprite.texture;
        // Берём sprite.rect, а не textureRect: Unity обрезает в textureRect
        // прозрачные поля, а карточка и опора рассчитаны на полный холст кадра.
        Rect r = sprite.rect;
        float w = Mathf.Max(1f, texture.width), h = Mathf.Max(1f, texture.height);
        float u0 = (r.xMin + 0.5f) / w, u1 = (r.xMax - 0.5f) / w;
        float v0 = (r.yMin + 0.5f) / h, v1 = (r.yMax - 0.5f) / h;
        return mirrored ? new Vector4(u1, v0, u0, v1) : new Vector4(u0, v0, u1, v1);
    }
}
