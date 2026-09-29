namespace Vow.Core.Logic
{
    // v0.13.0 灰盒數值；玩法與數值見 docs/V0130_ABYSSAL_VANGUARD_PLAN.md。
    public sealed class AbyssalVanguardTuning
    {
        public float SpawnSeconds = 600f;
        public float CoreX = 3f;
        public float CoreZ = 0f;
        public float CoreRadius = new CaptureTuning().CircleRadius;
        public float CoreChannelSeconds = new CaptureTuning().CaptureSeconds;

        public int VanguardMaxHealth = 900;
        public float VanguardCounterattackIntervalSeconds = 3f;
        public float VanguardTelegraphSeconds = 0.7f;
        public float VanguardCounterattackRadius = 5f;
        public int VanguardCounterattackDamage = 8;

        public int BehemothMaxHealth = 1500;
        public float BehemothLifetimeSeconds = 90f;
        public float BehemothMoveSpeed = 3f;
        public float BehemothAttackIntervalSeconds = 1.5f;
        public int BehemothHeroDamage = 12;
        public int BehemothWallDamage = 60;
    }
}
