using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 破牆護盾（GDD §參-2、PHASE2_BATCH3_PLAN.md §5 V2 a~c）。
    // 陣營一律用 int 代碼：Core/Logic 不得引用 Contracts 的 Faction（它住在含 using UnityEngine 的檔案裡，
    // 收不進純邏輯專案）。這裡的三個常數只需要「互不相同」，授予規則從不比對特定數值。
    public sealed class RockShieldLogicTests
    {
        private const int BlueTeam = 0;
        private const int RedTeam = 1;
        private const int NeutralFaction = 2;

        private static RockShieldLogic NewShield(out ProjectileTuning tuning)
        {
            tuning = new ProjectileTuning();
            return new RockShieldLogic(tuning);
        }

        // V2-a：授予規則真值表。近戰＋擊殺＋是牆＋擁有者已知且不是自己人——四個條件缺一不給。
        [Test]
        public void GrantRule_OnlyAMeleeKillOnAForeignWall_GrantsTheShield()
        {
            Assert.IsTrue(RockShieldLogic.ShouldGrantOnMeleeKill(true, true, true, RedTeam, BlueTeam),
                "砸碎敵方石牆必須給盾");
            Assert.IsTrue(RockShieldLogic.ShouldGrantOnMeleeKill(true, true, true, NeutralFaction, BlueTeam),
                "砸碎中立石牆必須給盾");

            Assert.IsFalse(RockShieldLogic.ShouldGrantOnMeleeKill(true, true, true, BlueTeam, BlueTeam),
                "砸碎自家石牆不得給盾（陣營比較恆真時這條會紅）");
            Assert.IsFalse(RockShieldLogic.ShouldGrantOnMeleeKill(false, true, true, RedTeam, BlueTeam),
                "沒打死就不給盾");
            Assert.IsFalse(RockShieldLogic.ShouldGrantOnMeleeKill(true, false, true, RedTeam, BlueTeam),
                "打死的不是石牆就不給盾（木樁不算）");
            Assert.IsFalse(RockShieldLogic.ShouldGrantOnMeleeKill(true, true, false, RedTeam, BlueTeam),
                "擁有者不明一律不給（fail-closed）");
        }

        // V2-b 前半：Grant 給滿值＋滿倒數，倒數走完護盾歸零。
        [Test]
        public void Grant_FillsTheShieldAndTheTimer_AndItExpiresWhenTheTimerRunsOut()
        {
            RockShieldLogic shield = NewShield(out ProjectileTuning tuning);
            Assert.IsFalse(shield.IsActive, "初始狀態不該有護盾");

            shield.Grant();
            Assert.AreEqual(tuning.ShieldAmount, shield.Amount, 1e-4f);
            Assert.AreEqual(tuning.ShieldDurationSeconds, shield.RemainingSeconds, 1e-4f);
            Assert.IsTrue(shield.IsActive);

            shield.Tick(tuning.ShieldDurationSeconds - 0.01f);
            Assert.IsTrue(shield.IsActive, "倒數只走了 2.49s，護盾必須還在");
            Assert.AreEqual(tuning.ShieldAmount, shield.Amount, 1e-4f, "倒數期間護盾值不該自己減少");

            shield.Tick(0.01f);
            Assert.IsFalse(shield.IsActive, "倒數滿 2.5s 護盾必須消失");
            Assert.AreEqual(0f, shield.Amount, 1e-4f);
        }

        // V2-b 後半：重複取得＝刷新回滿值，不疊加（使用者裁定 3）。
        [Test]
        public void RepeatedGrant_RefreshesBackToFull_AndNeverStacks()
        {
            RockShieldLogic shield = NewShield(out ProjectileTuning tuning);

            shield.Grant();
            shield.Tick(tuning.ShieldDurationSeconds - 0.3f); // 剩 0.3s
            Assert.AreEqual(0.3f, shield.RemainingSeconds, 1e-4f);

            shield.Grant();
            Assert.AreEqual(tuning.ShieldAmount, shield.Amount, 1e-4f,
                "重複取得只刷新回 150，不得疊加成 300");
            Assert.AreEqual(tuning.ShieldDurationSeconds, shield.RemainingSeconds, 1e-4f,
                "重複取得必須把倒數刷回滿值");
        }

        // V2-c：吸收扣護盾，超額傷害穿過去，不得被吞掉。
        [Test]
        public void Absorb_EatsDamageUntilTheShieldRunsOut_ThenLetsTheRestThrough()
        {
            RockShieldLogic shield = NewShield(out ProjectileTuning tuning);
            shield.Grant();

            float leftover = 50f;
            float firstHit = tuning.ShieldAmount - leftover;  // 100：護盾吃得下
            Assert.AreEqual(0f, shield.Absorb(firstHit), 1e-4f, "護盾吃得下時不得有殘餘傷害");
            Assert.AreEqual(leftover, shield.Amount, 1e-4f, "護盾必須真的被扣掉");

            float secondHit = leftover + 30f;                 // 80：超過剩下的 50
            Assert.AreEqual(30f, shield.Absorb(secondHit), 1e-4f, "超出護盾的部分必須穿過去，不得被吞掉");
            Assert.AreEqual(0f, shield.Amount, 1e-4f);
        }

        // V2-c：護盾值歸零（或根本沒取得）時，傷害必須原封不動穿過去。
        [Test]
        public void AnInactiveShield_AbsorbsNothing()
        {
            RockShieldLogic shield = NewShield(out ProjectileTuning _);

            Assert.IsFalse(shield.IsActive);
            Assert.AreEqual(50f, shield.Absorb(50f), 1e-4f, "沒有護盾時傷害必須全額穿過去");
            Assert.AreEqual(0f, shield.Amount, 1e-4f);
        }
    }
}
