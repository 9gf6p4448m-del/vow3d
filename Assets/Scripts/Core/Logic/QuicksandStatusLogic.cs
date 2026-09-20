namespace Vow.Core.Logic
{
    // 單一單位的縛足／減速狀態機（PHASE2_BATCH4_PLAN.md §2、§4-4，GDD 圍欄九）。
    // 陣營判定不在這裡：insideZoneId 由呼叫端先做完「敵對流沙」篩選（見 ElementReactionLogic.IsHostile）。
    public sealed class QuicksandStatusLogic
    {
        private readonly ElementTuning _tuning;
        private int _currentZoneId = -1;
        private float _rootRemainingSeconds;
        private int _rootedByZoneId = -1;

        public QuicksandStatusLogic(ElementTuning tuning)
        {
            _tuning = tuning;
        }

        public bool IsRooted => _rootRemainingSeconds > 0f;
        public float RootRemainingSeconds => _rootRemainingSeconds > 0f ? _rootRemainingSeconds : 0f;

        // 在敵對流沙內 ＝ 0.65；否則 1.0。與 IsRooted 是兩個獨立旗標：縛足期間也是 0.65（外層的
        // 「完全禁止位移」由 IsRooted／IsMovementLocked 另外把關，不靠這個倍率）。
        public float SpeedMultiplier => _currentZoneId != -1 ? _tuning.QuicksandSlowMultiplier : 1f;

        // 已被哪個流沙縛足過；-1 ＝ 未曾
        public int RootedByZoneId => _rootedByZoneId;

        // insideZoneId：此刻所在的「敵對流沙」id；不在任何敵對流沙內 ＝ -1
        public void Tick(float deltaSeconds, int insideZoneId)
        {
            if (insideZoneId == -1)
            {
                _currentZoneId = -1;
                _rootRemainingSeconds = 0f;
                return;
            }

            if (insideZoneId != _currentZoneId)
            {
                _currentZoneId = insideZoneId;
                if (insideZoneId != _rootedByZoneId)
                {
                    _rootedByZoneId = insideZoneId;
                    _rootRemainingSeconds = _tuning.RootDurationSeconds;
                }
            }

            if (_rootRemainingSeconds > 0f)
            {
                _rootRemainingSeconds -= deltaSeconds;
                if (_rootRemainingSeconds < 0f) _rootRemainingSeconds = 0f;
            }
        }

        public void Reset()
        {
            _currentZoneId = -1;
            _rootRemainingSeconds = 0f;
            _rootedByZoneId = -1;
        }
    }
}
