namespace Vow.Core.Logic
{
    public enum DuelRoundState { Dormant, Active, KnockoutPause }

    // 回合相位只有一份。生命值與 Unity 物件由外層維護。
    public sealed class DuelRoundLogic
    {
        private readonly DuelTuning _tuning;
        private float _remaining;

        public DuelRoundLogic(DuelTuning tuning) { _tuning = tuning; }

        public DuelRoundState State { get; private set; }
        public float RemainingSeconds => _remaining;
        public int StartCount { get; private set; }

        public bool TryStart()
        {
            if (State != DuelRoundState.Dormant) return false;
            State = DuelRoundState.Active;
            StartCount++;
            return true;
        }

        public bool Knockout()
        {
            if (State != DuelRoundState.Active) return false;
            State = DuelRoundState.KnockoutPause;
            _remaining = _tuning.ResetDelaySeconds;
            return true;
        }

        // true 僅在停頓結束的那次 Tick 回傳，讓組裝根只重置一次。
        public bool Tick(float deltaSeconds)
        {
            if (State != DuelRoundState.KnockoutPause) return false;
            if (deltaSeconds > 0f) _remaining -= deltaSeconds;
            if (_remaining > 0f) return false;
            _remaining = 0f;
            State = DuelRoundState.Dormant;
            return true;
        }
    }
}
