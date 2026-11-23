using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    // Журнал боя идёт вместе с показом: модель считает удар сразу, но строка
    // о нём появляется, когда удар виден на поле (стрела долетела, клинок
    // дошёл). Строка действия — при первом попадании действующего, строка
    // ответного удара — при попадании ответа; остальное — в конце показа.
    public sealed partial class BattleSandboxController
    {
        private readonly List<string> heldActionLog = new List<string>();
        private readonly List<string> heldRetaliationLog = new List<string>();
        private string heldLogActorId;
        private bool battleLogHeld;
        private bool heldActionShown;

        // Включается до расчёта действия: всё, что журнал получит дальше,
        // ждёт своего попадания.
        private void HoldBattleLog(string actorId)
        {
            ReleaseHeldBattleLog();
            battleLogHeld = true;
            heldActionShown = false;
            heldLogActorId = actorId;
        }

        private void AddRetaliationLog(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
                return;
            if (battleLogHeld)
                heldRetaliationLog.Add(entry);
            else
                AddBattleLog(entry);
        }

        private void OnBoardImpactForLog(SandboxHitRecord record, bool targetOurs)
        {
            if (!battleLogHeld || record == null)
                return;
            bool byActor = string.Equals(record.StrikerId, heldLogActorId, System.StringComparison.Ordinal);
            if (!heldActionShown && byActor)
            {
                heldActionShown = true;
                FlushHeld(heldActionLog);
            }
            else if (heldActionShown && !byActor)
            {
                FlushHeld(heldRetaliationLog);
            }
        }

        // Конец показа (или показа не было): всё придержанное — в журнал.
        private void ReleaseHeldBattleLog()
        {
            if (!battleLogHeld)
                return;
            battleLogHeld = false;
            FlushHeld(heldActionLog);
            FlushHeld(heldRetaliationLog);
            heldLogActorId = null;
        }

        // Новый бой: придержанное прежним боем не переносится.
        private void DropHeldBattleLog()
        {
            battleLogHeld = false;
            heldActionLog.Clear();
            heldRetaliationLog.Clear();
            heldLogActorId = null;
        }

        private void FlushHeld(List<string> entries)
        {
            if (entries.Count == 0)
                return;
            bool wasHeld = battleLogHeld;
            battleLogHeld = false;
            foreach (string entry in entries)
                AddBattleLog(entry);
            battleLogHeld = wasHeld;
            entries.Clear();
            ShowBattleLog();
        }
    }
}
