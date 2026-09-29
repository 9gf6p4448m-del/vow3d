using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    public sealed class AbyssalVanguardLogicTests
    {
        private const float Away = 1000f;
        private static readonly float CoreX = new AbyssalVanguardTuning().CoreX;

        private static AbyssalVanguardLogic NewCore()
        {
            var logic = new AbyssalVanguardLogic(new AbyssalVanguardTuning());
            logic.Tick(0.25f, CaptureMatchState.Active, 600f, Away, Away, true, Away, Away, true);
            logic.NotifyVanguardDefeated();
            Assert.AreEqual(AbyssalVanguardPhase.Core, logic.Phase);
            return logic;
        }

        private static void TickCore(AbyssalVanguardLogic logic, float blueX, float blueZ,
            bool blueAlive, float redX, float redZ, bool redAlive, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                logic.Tick(0.25f, CaptureMatchState.Active, 610f,
                    blueX, blueZ, blueAlive, redX, redZ, redAlive);
        }

        [Test]
        public void Tuning_UsesFrozenGreyboxValuesAndCaptureCircleRadius()
        {
            var tuning = new AbyssalVanguardTuning();
            Assert.AreEqual(600f, tuning.SpawnSeconds);
            Assert.AreEqual(4.375f, tuning.CoreX);
            Assert.AreEqual(0f, tuning.CoreZ);
            Assert.AreEqual(1.8f, tuning.CoreRadius);
            Assert.AreEqual(3.5f, tuning.CoreChannelSeconds);
            Assert.AreEqual(900, tuning.VanguardMaxHealth);
            Assert.AreEqual(3f, tuning.VanguardCounterattackIntervalSeconds);
            Assert.AreEqual(0.7f, tuning.VanguardTelegraphSeconds);
            Assert.AreEqual(5f, tuning.VanguardCounterattackRadius);
            Assert.AreEqual(8, tuning.VanguardCounterattackDamage);
            Assert.AreEqual(1500, tuning.BehemothMaxHealth);
            Assert.AreEqual(90f, tuning.BehemothLifetimeSeconds);
            Assert.AreEqual(3f, tuning.BehemothMoveSpeed);
            Assert.AreEqual(1.5f, tuning.BehemothAttackIntervalSeconds);
            Assert.AreEqual(12, tuning.BehemothHeroDamage);
            Assert.AreEqual(60, tuning.BehemothWallDamage);

            var logic = new AbyssalVanguardLogic(tuning);
            Assert.AreEqual(tuning.CoreX, logic.CoreX);
            Assert.AreEqual(tuning.CoreZ, logic.CoreZ);
            Assert.AreEqual(tuning.CoreRadius, logic.CoreRadius);
        }

        [Test]
        public void CoreCircle_OverlapsNoCaptureCircle_OnNineteenBoard()
        {
            // v0.13.1 裁定：站同一點不得同時引導核心與佔任何塔（每個塔心距 >= 兩半徑和）。
            var tuning = new AbyssalVanguardTuning();
            CaptureBoardSpec spec = CaptureBoardSpec.V0100Sanctuary;
            float gap = tuning.CoreRadius + new CaptureTuning().CircleRadius;
            for (int i = 0; i < spec.TileCount; i++)
            {
                float dx = tuning.CoreX - spec.CenterX(i);
                float dz = tuning.CoreZ - spec.CenterZ(i);
                Assert.GreaterOrEqual(dx * dx + dz * dz, gap * gap, "核心圈不得與第 " + i + " 塊佔塔圈重疊");
            }
        }

        [Test]
        public void Spawn_OnlyOnActiveTickCrossingMinuteTen_OncePerMatch()
        {
            var logic = new AbyssalVanguardLogic(new AbyssalVanguardTuning());
            logic.Tick(0.25f, CaptureMatchState.Off, 600.125f, Away, Away, true, Away, Away, true);
            logic.Tick(0.25f, CaptureMatchState.Lobby, 600.125f, Away, Away, true, Away, Away, true);
            logic.Tick(0.25f, CaptureMatchState.Active, 599.875f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Dormant, logic.Phase);
            logic.Tick(0.25f, CaptureMatchState.Active, 600.125f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Vanguard, logic.Phase);
            logic.NotifyVanguardDefeated();
            Assert.AreEqual(AbyssalVanguardPhase.Core, logic.Phase);
            logic.Tick(0.25f, CaptureMatchState.Active, 800f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Core, logic.Phase, "同局不再生成第二隻先鋒");

            var ended = new AbyssalVanguardLogic(new AbyssalVanguardTuning());
            ended.Tick(0.25f, CaptureMatchState.Ended, 600.125f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Finished, ended.Phase);
            ended.Tick(0.25f, CaptureMatchState.Active, 700f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Finished, ended.Phase);
        }

        [Test]
        // 板塊歸屬／比分不受擊倒影響，由 PlayMode CoreClaim 的全盤快照守（純邏輯層兩者沒有連線，這裡驗不到）。
        public void VanguardDefeat_OpensNeutralCore_LastHitDoesNotDecideOwner()
        {
            var logic = new AbyssalVanguardLogic(new AbyssalVanguardTuning());
            logic.NotifyVanguardDefeated();
            Assert.AreEqual(AbyssalVanguardPhase.Dormant, logic.Phase);
            logic.Tick(0.25f, CaptureMatchState.Active, 600f, Away, Away, true, Away, Away, true);
            logic.NotifyVanguardDefeated();
            logic.NotifyVanguardDefeated();
            Assert.AreEqual(AbyssalVanguardPhase.Core, logic.Phase);
            Assert.AreEqual(CaptureMatchLogic.NeutralFactionId, logic.BehemothOwner,
                "尾刀不可決定巨獸歸屬");
        }

        [Test]
        public void Core_ContestPausesBothProgress_LeavingOrFallingResetsOnlyThatSide()
        {
            var logic = NewCore();
            TickCore(logic, CoreX, 0f, true, Away, Away, true, 8);
            Assert.AreEqual(2f, logic.BlueCoreProgress);
            TickCore(logic, CoreX, 0f, true, CoreX, 0f, true, 4);
            Assert.AreEqual(2f, logic.BlueCoreProgress);
            Assert.AreEqual(0f, logic.RedCoreProgress);
            TickCore(logic, Away, Away, true, CoreX, 0f, true, 4);
            Assert.AreEqual(0f, logic.BlueCoreProgress);
            Assert.AreEqual(1f, logic.RedCoreProgress);
            TickCore(logic, Away, Away, true, CoreX, 0f, false, 1);
            Assert.AreEqual(0f, logic.RedCoreProgress);
            Assert.AreEqual(AbyssalVanguardPhase.Core, logic.Phase);
        }

        [Test]
        public void Core_DamageClearsProgressAndSkipsThatSidesNextTick()
        {
            var logic = NewCore();
            TickCore(logic, CoreX, 0f, true, Away, Away, true, 10);
            logic.NotifyHeroDamaged(CaptureMatchLogic.BlueFactionId);
            Assert.AreEqual(0f, logic.BlueCoreProgress);
            TickCore(logic, CoreX, 0f, true, Away, Away, true, 1);
            Assert.AreEqual(0f, logic.BlueCoreProgress);
            TickCore(logic, CoreX, 0f, true, Away, Away, true, 13);
            Assert.AreEqual(3.25f, logic.BlueCoreProgress);
            TickCore(logic, CoreX, 0f, true, Away, Away, true, 1);
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, logic.Phase);
            Assert.AreEqual(CaptureMatchLogic.BlueFactionId, logic.BehemothOwner);

            var control = NewCore();
            TickCore(control, CoreX, 0f, true, Away, Away, true, 13);
            control.NotifyHeroDamaged(CaptureMatchLogic.RedFactionId);
            TickCore(control, CoreX, 0f, true, Away, Away, true, 1);
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, control.Phase,
                "紅方受傷不能中斷藍方引導");

            var red = NewCore();
            TickCore(red, Away, Away, true, CoreX, 0f, true, 10);
            red.NotifyHeroDamaged(CaptureMatchLogic.RedFactionId);
            TickCore(red, Away, Away, true, CoreX, 0f, true, 1);
            Assert.AreEqual(0f, red.RedCoreProgress);
            TickCore(red, Away, Away, true, CoreX, 0f, true, 13);
            Assert.AreEqual(AbyssalVanguardPhase.Core, red.Phase);
            TickCore(red, Away, Away, true, CoreX, 0f, true, 1);
            Assert.AreEqual(CaptureMatchLogic.RedFactionId, red.BehemothOwner);
        }

        [Test]
        public void Core_CircleBoundaryAndUnopposedChannelClaimForBothSides()
        {
            var blue = NewCore();
            TickCore(blue, CoreX, 1.801f, true, Away, Away, true, 4);
            Assert.AreEqual(0f, blue.BlueCoreProgress, "半徑外不得引導");
            TickCore(blue, CoreX, 1.8f, true, Away, Away, true, 13);
            Assert.AreEqual(AbyssalVanguardPhase.Core, blue.Phase);
            Assert.AreEqual(3.25f, blue.BlueCoreProgress);
            TickCore(blue, CoreX, 1.8f, true, Away, Away, true, 1);
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, blue.Phase);
            Assert.AreEqual(CaptureMatchLogic.BlueFactionId, blue.BehemothOwner);
            Assert.AreEqual(90f, blue.BehemothRemaining);

            var red = NewCore();
            TickCore(red, Away, Away, true, CoreX, 0f, true, 13);
            Assert.AreEqual(AbyssalVanguardPhase.Core, red.Phase);
            TickCore(red, Away, Away, true, CoreX, 0f, true, 1);
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, red.Phase);
            Assert.AreEqual(CaptureMatchLogic.RedFactionId, red.BehemothOwner);
        }

        [Test]
        public void Behemoth_ExpiresAtNinetySeconds_OrWhenDefeated_NoRespawn()
        {
            var logic = NewCore();
            TickCore(logic, CoreX, 0f, true, Away, Away, true, 14);
            logic.Tick(89.75f, CaptureMatchState.Active, 800f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, logic.Phase);
            Assert.AreEqual(0.25f, logic.BehemothRemaining);
            logic.Tick(0.25f, CaptureMatchState.Active, 800.25f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Finished, logic.Phase);
            Assert.AreEqual(0f, logic.BehemothRemaining);
            logic.Tick(0.25f, CaptureMatchState.Active, 850f, Away, Away, true, Away, Away, true);
            logic.NotifyVanguardDefeated();
            Assert.AreEqual(AbyssalVanguardPhase.Finished, logic.Phase);

            var defeated = NewCore();
            TickCore(defeated, Away, Away, true, CoreX, 0f, true, 14);
            defeated.NotifyBehemothDefeated();
            Assert.AreEqual(AbyssalVanguardPhase.Finished, defeated.Phase);
            Assert.AreEqual(0f, defeated.BehemothRemaining);
        }

        [Test]
        public void EndedMatchCleansUp_AndResetAllowsExactlyOneNewSpawn()
        {
            var logic = NewCore();
            TickCore(logic, Away, Away, true, CoreX, 0f, true, 14);
            Assert.AreEqual(CaptureMatchLogic.RedFactionId, logic.BehemothOwner);
            logic.Tick(0.25f, CaptureMatchState.Ended, 900f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Finished, logic.Phase);
            Assert.AreEqual(0f, logic.BehemothRemaining);

            logic.ResetForMatch();
            Assert.AreEqual(AbyssalVanguardPhase.Dormant, logic.Phase);
            Assert.AreEqual(CaptureMatchLogic.NeutralFactionId, logic.BehemothOwner);
            Assert.AreEqual(0f, logic.BlueCoreProgress);
            Assert.AreEqual(0f, logic.RedCoreProgress);
            logic.Tick(0.25f, CaptureMatchState.Active, 599.875f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Dormant, logic.Phase);
            logic.Tick(0.25f, CaptureMatchState.Active, 600.125f, Away, Away, true, Away, Away, true);
            Assert.AreEqual(AbyssalVanguardPhase.Vanguard, logic.Phase);
        }
    }
}
