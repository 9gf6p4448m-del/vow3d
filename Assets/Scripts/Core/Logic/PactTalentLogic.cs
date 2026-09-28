namespace Vow.Core.Logic
{
    public enum PactTalent
    {
        None = 0,
        SwiftStep = 1,
        StoneBody = 2,
        FireSurge = 3,
        WindPiercer = 4,
        TidalPull = 5,
        StoneShock = 6,
        GeothermalFrenzy = 7,
        ExtremeOverclock = 8,
        ElementalAnnihilation = 9,
    }

    // 每局雙方各自選擇三階；只由 CaptureMatchLogic 在 Active 時推進與寫入。
    public sealed class PactTalentLogic
    {
        private readonly PactTalent[,] _selected = new PactTalent[2, 3];

        public int UnlockedTier { get; private set; }

        public static int TierOf(PactTalent talent)
        {
            int value = (int)talent;
            return value >= 1 && value <= 9 ? (value - 1) / 3 + 1 : 0;
        }

        public PactTalent Selected(int side, int tier)
        {
            return ValidSide(side) && tier >= 1 && tier <= 3 ? _selected[side, tier - 1] : PactTalent.None;
        }

        public int PendingTier(int side)
        {
            if (!ValidSide(side)) return 0;
            for (int tier = 1; tier <= UnlockedTier; tier++)
            {
                if (_selected[side, tier - 1] == PactTalent.None) return tier;
            }
            return 0;
        }

        internal void Refresh(float elapsed, int blueScore, int redScore)
        {
            int leadingScore = blueScore > redScore ? blueScore : redScore;
            int tier = elapsed >= 540f || leadingScore >= 750 ? 3
                     : elapsed >= 360f || leadingScore >= 500 ? 2
                     : elapsed >= 180f || leadingScore >= 250 ? 1 : 0;
            if (tier > UnlockedTier) UnlockedTier = tier;
        }

        internal bool TryChoose(int side, PactTalent talent)
        {
            int tier = TierOf(talent);
            if (!ValidSide(side) || tier == 0 || tier != PendingTier(side)) return false;
            _selected[side, tier - 1] = talent;
            return true;
        }

        internal void Reset()
        {
            UnlockedTier = 0;
            for (int side = 0; side < 2; side++)
                for (int tier = 0; tier < 3; tier++)
                    _selected[side, tier] = PactTalent.None;
        }

        private static bool ValidSide(int side) => side == CaptureMatchLogic.BlueFactionId || side == CaptureMatchLogic.RedFactionId;
    }
}
