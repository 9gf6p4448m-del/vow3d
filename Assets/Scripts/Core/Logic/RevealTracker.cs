namespace Vow.Core.Logic
{
    // v0.14.0 開火顯形（docs/V0140_CANYON_PLAN.md §4.7「介面」，2026-09-30 凍結）。零 UnityEngine、零配置、固定 4 格。
    public enum RevealUnit { BlueHero = 0, RedOpponent = 1, BlueBehemoth = 2, RedBehemoth = 3 }   // 固定 4 格

    public sealed class RevealTracker
    {
        private const int UnitCount = 4;

        private readonly float _revealSeconds;
        private readonly float[] _remaining = new float[UnitCount];
        private readonly int[] _revealedTo = new int[UnitCount];   // 顯形對象（觀看方陣營）

        public RevealTracker(float revealSeconds)
        {
            _revealSeconds = revealSeconds;
            Reset();
        }

        // 顯形「attackerSide 的英雄／對手」（藍→BlueHero、紅→RedOpponent），顯形對象＝victimSide。
        // 再次命中重設為 revealSeconds（不累加）。
        public void NotifyHit(int attackerSide, int victimSide)
        {
            if (attackerSide == CaptureMatchLogic.BlueFactionId) Reveal(RevealUnit.BlueHero, victimSide);
            else if (attackerSide == CaptureMatchLogic.RedFactionId) Reveal(RevealUnit.RedOpponent, victimSide);
        }

        // 巨獸打人：顯形的是巨獸本身。
        public void NotifyUnitHit(RevealUnit unit, int victimSide)
        {
            Reveal(unit, victimSide);
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < UnitCount; i++)
            {
                float next = _remaining[i] - dt;
                _remaining[i] = next > 0f ? next : 0f;
            }
        }

        public bool IsRevealedTo(RevealUnit unit, int viewerSide)
        {
            int i = (int)unit;
            return _remaining[i] > 0f && viewerSide == _revealedTo[i];
        }

        public float Remaining(RevealUnit unit) => _remaining[(int)unit];

        public void Reset()
        {
            for (int i = 0; i < UnitCount; i++)
            {
                _remaining[i] = 0f;
                _revealedTo[i] = -1;
            }
        }

        private void Reveal(RevealUnit unit, int viewerSide)
        {
            int i = (int)unit;
            _remaining[i] = _revealSeconds;
            _revealedTo[i] = viewerSide;
        }
    }
}
