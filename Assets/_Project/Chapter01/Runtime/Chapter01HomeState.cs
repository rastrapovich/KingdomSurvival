using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    // Визуальная рифма Дома N01 → N16 (P04-T04). Пять мотивов исходной
    // нормы: вода, мельница, скот, настил, звук.
    public enum Chapter01HomeWaterState
    {
        Normal,
        FloodDisturbed,
        Wrong,
        RestoredOldPattern,
        AlteredByNewRepair
    }

    public enum Chapter01HomeMillState
    {
        RunningNormally,
        DamagedOrStopped,
        RestoredOldWay,
        RestoredNewWay
    }

    public enum Chapter01HomeLivestockState
    {
        Calm,
        AvoidingWater,
        Lost
    }

    public enum Chapter01HomeWalkwayState
    {
        OldIntact,
        Destroyed,
        RestoredOldWay,
        RebuiltNewWay
    }

    public enum Chapter01HomeSoundState
    {
        FamiliarMorning,
        MillSilent,
        UnevenWaterAndMill,
        RestoredButChanged
    }

    // Пять мотивов на один момент времени. Не сохраняется отдельно — либо
    // это Baseline (константа), либо результат ResolveCurrent на запрос.
    public readonly struct Chapter01HomeSnapshot
    {
        public readonly Chapter01HomeWaterState Water;
        public readonly Chapter01HomeMillState Mill;
        public readonly Chapter01HomeLivestockState Livestock;
        public readonly Chapter01HomeWalkwayState Walkway;
        public readonly Chapter01HomeSoundState Sound;

        public Chapter01HomeSnapshot(
            Chapter01HomeWaterState water,
            Chapter01HomeMillState mill,
            Chapter01HomeLivestockState livestock,
            Chapter01HomeWalkwayState walkway,
            Chapter01HomeSoundState sound)
        {
            Water = water;
            Mill = mill;
            Livestock = livestock;
            Walkway = walkway;
            Sound = sound;
        }
    }

    // Типизированный resolver визуальной рифмы Дома (P04-T04, раздел 2
    // инструкции): не хранит второй независимый источник истины, а
    // интерпретирует уже существующие Chapter01Ids.Flags. Ничего не мутирует,
    // ничего не обновляет покадрово — только вычисляет Snapshot по запросу.
    // Причинность в обратную сторону (какие флаги ставить) остаётся за
    // Chapter01OutcomeApplier и production-диалогами последующих узлов.
    public static class Chapter01HomeState
    {
        public static readonly Chapter01HomeSnapshot Baseline = new Chapter01HomeSnapshot(
            Chapter01HomeWaterState.Normal,
            Chapter01HomeMillState.RunningNormally,
            Chapter01HomeLivestockState.Calm,
            Chapter01HomeWalkwayState.OldIntact,
            Chapter01HomeSoundState.FamiliarMorning);

        public static Chapter01HomeSnapshot ResolveCurrent(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Chapter01HomeWaterState water = ResolveWater(state);
            Chapter01HomeMillState mill = ResolveMill(state);
            Chapter01HomeLivestockState livestock = ResolveLivestock(state);
            Chapter01HomeWalkwayState walkway = ResolveWalkway(state);
            Chapter01HomeSoundState sound = ResolveSound(mill, water);

            return new Chapter01HomeSnapshot(water, mill, livestock, walkway, sound);
        }

        // Wrong имеет высший приоритет: даже завершённый ремонт не обязан
        // означать, что проблема воды исчезла (раздел 4 инструкции).
        private static Chapter01HomeWaterState ResolveWater(NarrativeStateData state)
        {
            if (state.HasFlag(Chapter01Ids.Flags.WaterWrongActive))
                return Chapter01HomeWaterState.Wrong;

            bool repairCompleted = state.HasFlag(Chapter01Ids.Flags.RepairCompleted);
            if (repairCompleted && state.HasFlag(Chapter01Ids.Flags.RepairNew))
                return Chapter01HomeWaterState.AlteredByNewRepair;
            if (repairCompleted && state.HasFlag(Chapter01Ids.Flags.RepairOld))
                return Chapter01HomeWaterState.RestoredOldPattern;

            if (state.HasFlag(Chapter01Ids.Flags.FloodHappened))
                return Chapter01HomeWaterState.FloodDisturbed;

            return Chapter01HomeWaterState.Normal;
        }

        // Старый ремонт намеренно не возвращает буквально RunningNormally —
        // мельница снова работает, но след ремонта должен быть виден в N16
        // (раздел 5 инструкции).
        private static Chapter01HomeMillState ResolveMill(NarrativeStateData state)
        {
            bool repairCompleted = state.HasFlag(Chapter01Ids.Flags.RepairCompleted);

            if (state.HasFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed) && !repairCompleted)
                return Chapter01HomeMillState.DamagedOrStopped;

            if (repairCompleted && state.HasFlag(Chapter01Ids.Flags.RepairNew))
                return Chapter01HomeMillState.RestoredNewWay;
            if (repairCompleted && state.HasFlag(Chapter01Ids.Flags.RepairOld))
                return Chapter01HomeMillState.RestoredOldWay;

            return Chapter01HomeMillState.RunningNormally;
        }

        // Lost имеет приоритет над AvoidingWater (раздел 6 инструкции).
        private static Chapter01HomeLivestockState ResolveLivestock(NarrativeStateData state)
        {
            if (state.HasFlag(Chapter01Ids.Flags.FloodLivestockLost))
                return Chapter01HomeLivestockState.Lost;

            if (state.HasFlag(Chapter01Ids.Flags.WaterWrongActive))
                return Chapter01HomeLivestockState.AvoidingWater;

            return Chapter01HomeLivestockState.Calm;
        }

        private static Chapter01HomeWalkwayState ResolveWalkway(NarrativeStateData state)
        {
            bool repairCompleted = state.HasFlag(Chapter01Ids.Flags.RepairCompleted);

            if (state.HasFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed) && !repairCompleted)
                return Chapter01HomeWalkwayState.Destroyed;

            if (repairCompleted && state.HasFlag(Chapter01Ids.Flags.RepairNew))
                return Chapter01HomeWalkwayState.RebuiltNewWay;
            if (repairCompleted && state.HasFlag(Chapter01Ids.Flags.RepairOld))
                return Chapter01HomeWalkwayState.RestoredOldWay;

            return Chapter01HomeWalkwayState.OldIntact;
        }

        // Звук не хранится как отдельный persistent state (раздел 8
        // инструкции) — выводится из уже вычисленных воды и мельницы.
        private static Chapter01HomeSoundState ResolveSound(
            Chapter01HomeMillState mill,
            Chapter01HomeWaterState water)
        {
            if (mill == Chapter01HomeMillState.DamagedOrStopped)
                return Chapter01HomeSoundState.MillSilent;

            if (water == Chapter01HomeWaterState.Wrong)
                return Chapter01HomeSoundState.UnevenWaterAndMill;

            if (mill == Chapter01HomeMillState.RestoredOldWay || mill == Chapter01HomeMillState.RestoredNewWay)
                return Chapter01HomeSoundState.RestoredButChanged;

            return Chapter01HomeSoundState.FamiliarMorning;
        }

        public static int CountChangedMotifs(Chapter01HomeSnapshot snapshot)
        {
            return GetChangedMotifs(snapshot).Count;
        }

        // Пять мотивов раздела 1 инструкции, сравненных с Baseline. N16
        // сможет использовать список категорий, чтобы гарантировать
        // "минимум три изменившихся мотива", а не текстовый список причин.
        public static IReadOnlyList<string> GetChangedMotifs(Chapter01HomeSnapshot snapshot)
        {
            List<string> changed = new List<string>();

            if (snapshot.Water != Baseline.Water)
                changed.Add("Water");
            if (snapshot.Mill != Baseline.Mill)
                changed.Add("Mill");
            if (snapshot.Livestock != Baseline.Livestock)
                changed.Add("Livestock");
            if (snapshot.Walkway != Baseline.Walkway)
                changed.Add("Walkway");
            if (snapshot.Sound != Baseline.Sound)
                changed.Add("Sound");

            return changed;
        }
    }
}
