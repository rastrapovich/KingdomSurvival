using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    /// <summary>
    /// Axial coordinate for a pointy-top hex grid.
    /// </summary>
    public readonly struct HexCoord : IEquatable<HexCoord>, IComparable<HexCoord>
    {
        private static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1)
        };

        public int Q { get; }
        public int R { get; }
        public int S => -Q - R;

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public IEnumerable<HexCoord> Neighbors()
        {
            foreach (HexCoord direction in Directions)
                yield return this + direction;
        }

        public int DistanceTo(HexCoord other)
        {
            int q = Math.Abs(Q - other.Q);
            int r = Math.Abs(R - other.R);
            int s = Math.Abs(S - other.S);
            return Math.Max(q, Math.Max(r, s));
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
