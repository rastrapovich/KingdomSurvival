using System;
using KingdomSurvival.LocationRendering;
using KingdomSurvival.MapLabBridge;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    // Обработка кадра генерируемой арены целиком (21.11.2026): объём URP
    // (цвет, тон, линза) — внутри MapLabArena; здесь — проход «Холста» на
    // камере арены, всплеск линзы, вспышка и тряска при ударе, красные края
    // по здоровью вождя и подкраска фигур доски (раздел «Персонажи»).
    // Обработка — общая (ArenaGeneratorAsset).
    public sealed class ArenaFrameBinding : IDisposable
    {
        // Последняя связка (кнопки проверки удара в окнах редактора).
        public static ArenaFrameBinding Current { get; private set; }

        public ArenaFrameState State { get; }
        private readonly MapLabArena arena;
        private readonly HexBoardElement board;
        private Camera boundCamera;
        private bool disposed;

        public ArenaFrameBinding(MapLabArena arena, HexBoardElement board)
        {
            this.arena = arena;
            this.board = board;
            State = new ArenaFrameState
            {
                PostSource = () => ArenaGeneratorAsset.CurrentFrame(),
                LeaderHealth = () => board != null ? board.LeaderHealthShare : 1f,
            };
            if (arena != null)
            {
                arena.LensPulse = () => State.LensPulse;
                State.CollectFires = arena.CollectFireViewport;
                State.UnitScale = () => arena.AssetUnitScale;
            }
            if (board != null)
            {
                board.ImpactShown += OnImpact;
                board.UnitTint = State.UnitTint;
            }
            Current = this;
        }

        private void OnImpact(SandboxHitRecord record, bool targetOurs) => State.BattleHit(targetOurs, record.TargetDefeated);

        public void Hit(float strength) => State.Hit(strength);

        public bool Animating => !disposed && State.Animating;

        // Перед кадром арены: «Холст» на камере, тряска.
        public void BeforeRender()
        {
            if (disposed || arena == null) return;
            Camera camera = arena.FrameCamera;
            if (camera != null && camera != boundCamera)
            {
                LocationCanvasPass.Bind(camera, State);
                boundCamera = camera;
            }
            arena.ShakeShare = State.ShakeShare;
            LocationFrameQualityLevel quality = LocationWorldRenderer.QualityLevel;
            arena.ParticleDensityScale = quality == LocationFrameQualityLevel.High ? 1f : quality == LocationFrameQualityLevel.Medium ? .7f : .4f;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            LocationCanvasPass.Unbind(State);
            if (board != null)
            {
                board.ImpactShown -= OnImpact;
                board.UnitTint = null;
            }
            if (arena != null) arena.LensPulse = null;
            if (Current == this) Current = null;
        }
    }
}
