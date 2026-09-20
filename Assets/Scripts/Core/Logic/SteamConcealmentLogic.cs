namespace Vow.Core.Logic
{
    // 蒸氣迷霧：受擊顯影計時、遮蔽判定、晶塔引導係數（PHASE2_BATCH4_PLAN.md §2，GDD 圍欄九）。
    public sealed class SteamConcealmentLogic
    {
        private readonly ElementTuning _tuning;
        private float _revealRemainingSeconds;

        public SteamConcealmentLogic(ElementTuning tuning)
        {
            _tuning = tuning;
        }

        public bool IsRevealed => _revealRemainingSeconds > 0f;
        public float RevealRemainingSeconds => _revealRemainingSeconds > 0f ? _revealRemainingSeconds : 0f;

        // 顯影 1.5s；重複受傷 ＝ 刷新回 1.5s（不疊加）。
        public void NotifyDamaged()
        {
            _revealRemainingSeconds = _tuning.RevealDurationSeconds;
        }

        public void Tick(float deltaSeconds)
        {
            if (_revealRemainingSeconds <= 0f) return;
            _revealRemainingSeconds -= deltaSeconds;
            if (_revealRemainingSeconds < 0f) _revealRemainingSeconds = 0f;
        }

        // 三個 bool 的真值表（八種組合全非恆定）：在霧內、顯影未過期、攻擊者不在同一團霧內，三者
        // （目標在霧內、攻擊者不同霧、沒顯影）同時成立才遮蔽。
        public static bool IsConcealed(bool targetInsideSteam, bool attackerInsideSameSteam, bool targetRevealed)
        {
            return targetInsideSteam && !attackerInsideSameSteam && !targetRevealed;
        }

        public static float ChannelSpeedMultiplier(bool channellerInsideSteam, ElementTuning tuning)
        {
            return channellerInsideSteam ? tuning.SteamChannelSpeedMultiplier : 1f;
        }
    }
}
