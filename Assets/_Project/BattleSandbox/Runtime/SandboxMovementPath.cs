using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    public static class SandboxMovementPath
    {
        public static bool TryBuild(
            SandboxBattle battle,
            string unitId,
            HexCoord destination,
            out IReadOnlyList<HexCoord> path,
            out int movementCost)
        {
            path = Array.Empty<HexCoord>();
            movementCost = 0;
            if (battle == null)
                return false;

            SandboxUnitState unit = battle.GetUnit(unitId);
            if (unit == null || unit.IsDefeated)
                return false;

            if (destination == unit.Position)
            {
                path = new[] { unit.Position };
                return true;
            }

            IReadOnlyDictionary<HexCoord, int> reachable = battle.GetReachable(unitId);
            if (!reachable.TryGetValue(destination, out movementCost))
                return false;

            List<HexCoord> reversed = new List<HexCoord> { destination };
            HexCoord current = destination;
            int currentCost = movementCost;

            while (current != unit.Position)
            {
                int stepCost = battle.GetTerrain(current) == SandboxTerrain.Difficult ? 2 : 1;
                bool found = false;
                HexCoord bestPredecessor = default;
                int bestPredecessorCost = 0;

                foreach (HexCoord neighbor in current.Neighbors())
                {
                    int neighborCost;
                    if (neighbor == unit.Position)
                    {
                        neighborCost = 0;
                    }
                    else if (!reachable.TryGetValue(neighbor, out neighborCost))
                    {
                        continue;
                    }

                    if (neighborCost + stepCost != currentCost)
                        continue;

                    if (!found || neighbor.CompareTo(bestPredecessor) < 0)
                    {
                        found = true;
                        bestPredecessor = neighbor;
                        bestPredecessorCost = neighborCost;
                    }
                }

                if (!found)
                {
                    path = Array.Empty<HexCoord>();
                    movementCost = 0;
                    return false;
                }

                current = bestPredecessor;
                currentCost = bestPredecessorCost;
                reversed.Add(current);
            }

            reversed.Reverse();
            path = reversed;
            return true;
        }
    }
}
