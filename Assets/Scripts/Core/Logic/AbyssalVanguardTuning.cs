namespace Vow.Core.Logic
{
    // v0.13.0 灰盒數值；玩法與數值見 docs/V0130_ABYSSAL_VANGUARD_PLAN.md。
    public sealed class AbyssalVanguardTuning
    {
        public float SpawnSeconds = 600f;
        // v0.13.1 使用者裁定：核心放在 0／2／3 號塊交會頂點，半徑 1.8m，與三個 2.5m 佔塔圈各留 0.075m 不重疊。
        public float CoreX = 4.375f;
        public float CoreZ = 0f;
        public float CoreRadius = 1.8f;
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
