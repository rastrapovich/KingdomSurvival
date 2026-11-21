using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Предпросмотр «Генератора арен» (21.11.2026): вместо круглых заглушек на
    // поле стоят настоящие бойцы и случайные существа из Базы существ — с их
    // рисунками и анимациями, как в бою. Выбор по seed устойчив.
    public static class ArenaPreviewContent
    {
        private static SandboxUnitContent content;

        private static SandboxUnitContent Content => content ??= SandboxUnitDatabaseAdapter.Load();

        // Бойцы — люди отряда; враги — случайные существа из базы (без повторов,
        // пока хватает видов).
        public static void Pick(int seed, int fighters, int enemies,
            out List<SandboxUnitDefinition> ours, out List<SandboxUnitDefinition> theirs)
        {
            SandboxUnitContent c = Content;
            IReadOnlyList<SandboxUnitDefinition> players = c.PlayerRoster.Count > 0 ? c.PlayerRoster : SandboxRoster.PlayerRoster;
            ours = Enumerable.Range(0, fighters).Select(i => players[i % players.Count]).ToList();

            List<SandboxUnitDefinition> pool = c.CreaturesById
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList();
            if (pool.Count == 0)
                pool = SandboxRoster.EnemyRoster.ToList();
            Random rng = new Random(seed * 7919 + 17);
            // Тасование Фишера — Йетса: порядок зависит только от seed.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            theirs = Enumerable.Range(0, enemies).Select(i => pool[i % pool.Count]).ToList();
        }

        // Рисунки и анимации существ — на доску предпросмотра.
        public static void Apply(HexBoardElement board)
        {
            if (board == null)
                return;
            board.SetUnitVisuals(Content.Visuals, Content.AnimationDatabase);
        }
    }
}
