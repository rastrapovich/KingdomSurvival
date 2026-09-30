using System;

namespace KingdomSurvival.AnimationDatabase
{
    // Английские ID совпадают с именами папок экспорта KS Sprite Renderer.
    // Порядок значений сериализуется — новые действия добавлять только в конец.
    public enum CreatureAnimationAction
    {
        Idle,
        Walk,
        Attack,
        Hit,
        Death,
        Block,
        Shoot,
        SpecialAttack,
        Victory,
        Taunt
    }

    // Ракурсы рендера (папки Front, Front_Right, …). Порядок — по кругу,
    // соседние значения отличаются на 60°.
    public enum CreatureAnimationDirection
    {
        Front,
        FrontRight,
        BackRight,
        Back,
        BackLeft,
        FrontLeft
    }

    // Направление на экране к соседнему гексу. Порядок совпадает с
    // HexCoord.Neighbors(): вправо, вправо-вверх, влево-вверх, влево,
    // влево-вниз, вправо-вниз.
    public enum HexFacing
    {
        East,
        NorthEast,
        NorthWest,
        West,
        SouthWest,
        SouthEast
    }

    public enum CreatureAnimationPlayback
    {
        // Зациклено: ожидание, ходьба.
        Loop,
        // Один раз, затем ожидание.
        Once,
        // Один раз с удержанием последнего кадра: смерть.
        HoldLastFrame
    }

    public enum CreatureAnimationSetStatus
    {
        Empty,
        Partial,
        Ready
    }

    public static class CreatureAnimationLabels
    {
        public const int ActionCount = 10;
        public const int DirectionCount = 6;

        public static readonly CreatureAnimationAction[] Actions =
        {
            CreatureAnimationAction.Idle,
            CreatureAnimationAction.Walk,
            CreatureAnimationAction.Attack,
            CreatureAnimationAction.Hit,
            CreatureAnimationAction.Death,
            CreatureAnimationAction.Block,
            CreatureAnimationAction.Shoot,
            CreatureAnimationAction.SpecialAttack,
            CreatureAnimationAction.Victory,
            CreatureAnimationAction.Taunt
        };

        public static readonly CreatureAnimationDirection[] Directions =
        {
            CreatureAnimationDirection.Front,
            CreatureAnimationDirection.FrontRight,
            CreatureAnimationDirection.BackRight,
            CreatureAnimationDirection.Back,
            CreatureAnimationDirection.BackLeft,
            CreatureAnimationDirection.FrontLeft
        };

        public static string ActionTitle(CreatureAnimationAction action)
        {
            switch (action)
            {
                case CreatureAnimationAction.Idle: return "Ожидание";
                case CreatureAnimationAction.Walk: return "Ходьба";
                case CreatureAnimationAction.Attack: return "Атака";
                case CreatureAnimationAction.Hit: return "Получение удара";
                case CreatureAnimationAction.Death: return "Смерть";
                case CreatureAnimationAction.Block: return "Защита";
                case CreatureAnimationAction.Shoot: return "Выстрел";
                case CreatureAnimationAction.SpecialAttack: return "Особая атака";
                case CreatureAnimationAction.Victory: return "Победа";
                case CreatureAnimationAction.Taunt: return "Провокация";
                default: return action.ToString();
            }
        }

        // Имя папки экспорта KS Sprite Renderer.
        public static string DirectionFolder(CreatureAnimationDirection direction)
        {
            switch (direction)
            {
                case CreatureAnimationDirection.Front: return "Front";
                case CreatureAnimationDirection.FrontRight: return "Front_Right";
                case CreatureAnimationDirection.BackRight: return "Back_Right";
                case CreatureAnimationDirection.Back: return "Back";
                case CreatureAnimationDirection.BackLeft: return "Back_Left";
                case CreatureAnimationDirection.FrontLeft: return "Front_Left";
                default: return direction.ToString();
            }
        }

        public static string DirectionTitle(CreatureAnimationDirection direction)
        {
            switch (direction)
            {
                case CreatureAnimationDirection.Front: return "Спереди";
                case CreatureAnimationDirection.FrontRight: return "Спереди справа";
                case CreatureAnimationDirection.BackRight: return "Сзади справа";
                case CreatureAnimationDirection.Back: return "Сзади";
                case CreatureAnimationDirection.BackLeft: return "Сзади слева";
                case CreatureAnimationDirection.FrontLeft: return "Спереди слева";
                default: return direction.ToString();
            }
        }

        public static string FacingTitle(HexFacing facing)
        {
            switch (facing)
            {
                case HexFacing.East: return "вправо";
                case HexFacing.NorthEast: return "вправо-вверх";
                case HexFacing.NorthWest: return "влево-вверх";
                case HexFacing.West: return "влево";
                case HexFacing.SouthWest: return "влево-вниз";
                case HexFacing.SouthEast: return "вправо-вниз";
                default: return facing.ToString();
            }
        }

        public static string FacingArrow(HexFacing facing)
        {
            switch (facing)
            {
                case HexFacing.East: return "→";
                case HexFacing.NorthEast: return "↗";
                case HexFacing.NorthWest: return "↖";
                case HexFacing.West: return "←";
                case HexFacing.SouthWest: return "↙";
                case HexFacing.SouthEast: return "↘";
                default: return "?";
            }
        }

        public static string PlaybackTitle(CreatureAnimationPlayback playback)
        {
            switch (playback)
            {
                case CreatureAnimationPlayback.Loop: return "Цикл";
                case CreatureAnimationPlayback.Once: return "Один раз";
                case CreatureAnimationPlayback.HoldLastFrame: return "Один раз, держать последний кадр";
                default: return playback.ToString();
            }
        }

        public static string StatusTitle(CreatureAnimationSetStatus status)
        {
            switch (status)
            {
                case CreatureAnimationSetStatus.Ready: return "Готово";
                case CreatureAnimationSetStatus.Partial: return "Частично";
                default: return "Без анимаций";
            }
        }

        public static CreatureAnimationPlayback DefaultPlayback(CreatureAnimationAction action)
        {
            switch (action)
            {
                case CreatureAnimationAction.Idle:
                case CreatureAnimationAction.Walk:
                    return CreatureAnimationPlayback.Loop;
                case CreatureAnimationAction.Death:
                    return CreatureAnimationPlayback.HoldLastFrame;
                default:
                    return CreatureAnimationPlayback.Once;
            }
        }

        // У каких действий есть маркер «момент удара / выпуска снаряда».
        public static bool HasImpactMarker(CreatureAnimationAction action)
        {
            return action == CreatureAnimationAction.Attack ||
                   action == CreatureAnimationAction.Shoot ||
                   action == CreatureAnimationAction.SpecialAttack;
        }

        // Расстояние по кругу ракурсов: 0…3.
        public static int RingDistance(int first, int second, int count)
        {
            int difference = Math.Abs(first - second) % count;
            return Math.Min(difference, count - difference);
        }
    }
}
