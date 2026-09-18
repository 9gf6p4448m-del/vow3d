namespace Vow.Core.Logic
{
    public enum HapticCue
    {
        BasicAttackHit, // 常規平 A 命中
        CadenceDash,    // 目押成功、微滑步
        WallBreak       // 石牆破碎
    }

    public enum HapticStrength
    {
        None,
        Light,
        Heavy
    }

    public enum BasicAttackHaptics
    {
        Light,  // 輕震（受疲勞管理）
        Silent  // 靜音
    }

    // 震覺疲勞管理（ARCHITECTURE §貳：僅微滑步重震，常規平 A 輕震／靜音）。
    //
    // 重震是「決策成功」的訊號，必須每次都到位，所以不受任何節流。
    // 普攻的輕震若每 0.8 秒震一次、連震十分鐘，手會麻、訊號也會被習慣掉；
    // 所以連續出刀時只有前幾刀有輕震，之後自動靜音，停手一段時間才重置。
    public struct HapticFatiguePolicy
    {
        public BasicAttackHaptics BasicAttackMode;
        public int LightPulsesBeforeMute;   // 連續幾刀之後靜音
        public double ResetAfterIdleSeconds;

        private int _consecutiveBasicHits;
        private double _lastBasicHitTime;
        private bool _hasHitBefore;

        public static HapticFatiguePolicy CreateDefault()
        {
            return new HapticFatiguePolicy
            {
                BasicAttackMode = BasicAttackHaptics.Light,
                LightPulsesBeforeMute = 3,
                ResetAfterIdleSeconds = 2.0
            };
        }

        public HapticStrength Decide(HapticCue cue, double now)
        {
            if (cue == HapticCue.CadenceDash || cue == HapticCue.WallBreak) return HapticStrength.Heavy;

            if (_hasHitBefore && now - _lastBasicHitTime >= ResetAfterIdleSeconds) _consecutiveBasicHits = 0;
            _hasHitBefore = true;
            _lastBasicHitTime = now;
            _consecutiveBasicHits++;

            if (BasicAttackMode == BasicAttackHaptics.Silent) return HapticStrength.None;
            return _consecutiveBasicHits <= LightPulsesBeforeMute ? HapticStrength.Light : HapticStrength.None;
        }
    }
}
