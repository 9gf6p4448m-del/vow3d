namespace Vow.Core.Logic
{
    // v0.7.0 手感試玩值。改動前須同步更新 docs/V070_GREYBOX_DUEL_PLAN.md 的驗收。
    public sealed class DuelTuning
    {
        public float HeroHealth = 100f;
        public float OpponentHealth = 300f;
        public float OpponentDamage = 20f;
        public float OpponentMoveSpeed = 4f;
        public float OpponentStopDistance = 1.8f;
        public float AttackRadius = 1.5f;
        public float WindupSeconds = 0.7f;
        public float RecoverySeconds = 1f;
        public float ResetDelaySeconds = 2.5f;
    }
}
