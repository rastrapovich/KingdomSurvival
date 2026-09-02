using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    /// <summary>
    /// Odd-row offset coordinate for a pointy-top hex grid.
    /// Horizontal rows alternate by half a hex, matching the compact
    /// rectangular battlefields used by King's Bounty-style arenas.
    /// </summary>
    public readonly struct HexCoord : IEquatable<HexCoord>, IComparable<HexCoord>
    {
        private static readonly HexCoord[] EvenRowDirections =
        {
            new HexCoord(1, 0),
            new HexCoord(0, -1),
            new HexCoord(-1, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1)
        };

        private static readonly HexCoord[] OddRowDirections =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(0, 1),
            new HexCoord(1, 1)
        };

        /// <summary>
        /// Zero-based column in the rectangular odd-row grid.
        /// Kept as Q for compatibility with the existing sandbox API.
        /// </summary>
        public int Q { get; }

        /// <summary>
        /// Zero-based row in the rectangular odd-row grid.
        /// </summary>
        public int R { get; }

        /// <summary>
        /// Third cube coordinate used for distance calculations.
        /// </summary>
        public int S
        {
            get
            {
                int cubeX = ToCubeX(Q, R);
                int cubeZ = R;
                return -cubeX - cubeZ;
            }
        }

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public IEnumerable<HexCoord> Neighbors()
        {
            HexCoord[] directions = (R & 1) == 0
                ? EvenRowDirections
                : OddRowDirections;
            foreach (HexCoord direction in directions)
                yield return this + direction;
        }

        public int DistanceTo(HexCoord other)
        {
            int thisX = ToCubeX(Q, R);
            int thisZ = R;
            int thisY = -thisX - thisZ;

            int otherX = ToCubeX(other.Q, other.R);
            int otherZ = other.R;
            int otherY = -otherX - otherZ;

            int x = Math.Abs(thisX - otherX);
            int y = Math.Abs(thisY - otherY);
            int z = Math.Abs(thisZ - otherZ);
            return Math.Max(x, Math.Max(y, z));
        }

        private static int ToCubeX(int column, int row)
        {
            return column - (row - (row & 1)) / 2;
        }

        public int CompareTo(HexCoord other)
        {
            int rowComparison = R.CompareTo(other.R);
            return rowComparison != 0 ? rowComparison : Q.CompareTo(other.Q);
        }

        public bool Equals(HexCoord other)
        {
            return Q == other.Q && R == other.R;
        }

        public override bool Equals(object obj)
        {
            return obj is HexCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Q * 397) ^ R;
            }
        }

        public override string ToString()
        {
            return "(" + Q + ", " + R + ")";
        }

        public static HexCoord operator +(HexCoord left, HexCoord right)
        {
            return new HexCoord(left.Q + right.Q, left.R + right.R);
        }

        public static bool operator ==(HexCoord left, HexCoord right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(HexCoord left, HexCoord right)
        {
            return !left.Equals(right);
        }
    }
}
