namespace Vow.Core.Logic
{
    // 三技能各自獨立 5s 冷卻＋HUD 標籤索引（PHASE2_BATCH4_PLAN.md §2）。零配置字串表由 Unity 端
    // 接手（步驟 B）：這裡只回傳索引，不做任何字串串接。
    public sealed class ElementCastCooldowns
    {
        private const int SlotCount = 5; // ElementCast 最大值 Rock=4，索引直接用 (int)cast，0 號不用
        private readonly ElementTuning _tuning;
        private readonly float[] _readyAtSeconds = new float[SlotCount];

        public ElementCastCooldowns(ElementTuning tuning)
        {
            _tuning = tuning;
        }

        public void ResetForRound()
        {
            for (int i = 0; i < _readyAtSeconds.Length; i++) _readyAtSeconds[i] = 0f;
        }

        // 冷卻中 → false，不重置冷卻
        public bool TryBeginCast(ElementCast cast, float nowSeconds)
        {
            int slot = (int)cast;
            if (nowSeconds < _readyAtSeconds[slot]) return false;

            _readyAtSeconds[slot] = nowSeconds + _tuning.SkillCooldownSeconds;
            return true;
        }

        public float RemainingSeconds(ElementCast cast, float nowSeconds)
        {
            int slot = (int)cast;
            float remaining = _readyAtSeconds[slot] - nowSeconds;
            return remaining > 0f ? remaining : 0f;
        }

        // 0 ＝ 可用；1..5 ＝ 向上取整的剩餘秒
        public int RemainingLabelIndex(ElementCast cast, float nowSeconds)
        {
            float remaining = RemainingSeconds(cast, nowSeconds);
            if (remaining <= 0f) return 0;

            int ceiled = (int)System.Math.Ceiling((double)remaining);
            return ceiled < 1 ? 1 : ceiled;
        }
    }
}
