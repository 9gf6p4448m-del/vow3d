#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓蓄力 追加 A12（使用者 2026-10-03 同意 B：停火從按住滿快速射擊門檻 0.2s 起算；覆審 r3 HIGH-1／MEDIUM-1/2／LOW-1）。
    // 凍結檔：vow-toolchain/acceptance-bowcharge-20261003.md 末段「追加 A12」。
    public sealed partial class CameraLabCombatPlayTests
    {
        private IEnumerator WaitForBrainState(PlayerState state, float seconds)
        {
            float until = Time.time + seconds;
            while (_hero.StateMachine.CurrentState != state && Time.time < until) yield return null;
        }

        // 一下快速點擊：按住 frames 幀（captureDeltaTime 1/60）後放開；回傳觸控時鐘的按住秒數。
        private double _lastTapHeld;
        private IEnumerator QuickTapFrames(int frames)
        {
            PressAttack();
            double t0 = Time.unscaledTimeAsDouble;
            for (int i = 0; i < frames; i++) yield return null;
            _lastTapHeld = Time.unscaledTimeAsDouble - t0;
            ReleaseAttack();
        }

        [UnityTest] // A12a：弓鎖定 6m 木樁，每 0.25s 點一下（每下按 0.08s）連點 3s→木樁持續受傷（≥ 3 下×60）
        public IEnumerator A12a_Bow_RapidTapping_KeepsDealingDamage()
        {
            yield return LockBowOnDummy();
            DummyTarget dummy = _bowDummy;
            float h0 = dummy.Health;
            int taps = 0;
            double maxHeld = 0;
            for (int tick = 0; tick < 12; tick++)   // 12 下 × 15 幀（0.25s 遊戲時間）＝3s
            {
                yield return QuickTapFrames(5);       // 5 幀 ≈ 0.083s 遊戲時間
                taps++;
                if (_lastTapHeld > maxHeld) maxHeld = _lastTapHeld;
                for (int i = 0; i < 10; i++) yield return null;
            }
            float dealt = h0 - dummy.Health;
            Debug.Log("[BOWCHARGE TEST] A12a taps=" + taps + " dealt=" + dealt.ToString("F3") + " maxHeld=" + maxHeld.ToString("F3"));
            Assert.Less(maxHeld, BowChargeLogic.QuickShotSeconds, "前提：每一下都是快速點擊（觸控時鐘 < 0.2s）");
            Assert.GreaterOrEqual(dealt, 3f * _hero.AttackDamage - 1e-3f, "連點 3s：木樁持續受傷（累計 ≥ 3 下×60）");
        }

        [UnityTest] // A12b：放開蓄力箭後 0.25s 內再快速點擊→蓄力箭仍命中 108
        public IEnumerator A12b_Bow_QuickTapRightAfterChargedRelease_ChargedShotStillLands()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            float h0 = dummy.Health;
            yield return HoldAttack(1.0);
            float releasedAt = Time.time;
            Assert.AreSame(dummy, _hero.CurrentTarget, "前提：蓄力箭已鎖定木樁");
            yield return null; yield return null;
            Assert.AreEqual(h0, dummy.Health, "前提：再點之前蓄力箭尚未命中");
            yield return QuickTapFrames(3);
            Assert.Less(Time.time - releasedAt, 0.25f, "前提：0.25s 內再點");
            yield return WaitForDrop(dummy, h0, 2f);
            Debug.Log("[BOWCHARGE TEST] A12b hit=" + (h0 - dummy.Health).ToString("F3"));
            Assert.AreEqual(h0 - _hero.AttackDamage * BowChargedDamageMultiplier, dummy.Health, 1e-3f,
                "放開後立刻再快速點擊：蓄力箭仍命中 108（不得被再按作廢）");
        }

        [UnityTest] // A12c：按住 0.15s（< 0.2s）不停火；按住超過 0.2s 自此停火
        public IEnumerator A12c_Bow_HoldBelowQuickThreshold_NoCeasefire_AboveThreshold_Ceasefire()
        {
            yield return LockBowOnDummy();
            DummyTarget dummy = _bowDummy;
            yield return WaitForBrainState(PlayerState.AttackWindup, 2f);
            Assert.AreEqual(PlayerState.AttackWindup, _hero.StateMachine.CurrentState, "前提：在可被打斷的前搖中按下");
            PressAttack();
            double t0 = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - t0 < 0.15)
            {
                yield return null;
                if (Time.unscaledTimeAsDouble - t0 < 0.15)
                    Assert.AreSame(dummy, _hero.CurrentTarget, "按住未滿 0.2s：不停火、不清目標");
            }
            ReleaseAttack();
            Assert.AreSame(dummy, _hero.CurrentTarget, "0.15s 放開＝快速點擊：仍在打原目標");

            yield return WaitForBrainState(PlayerState.AttackWindup, 2f);
            PressAttack();
            t0 = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - t0 < BowChargeLogic.QuickShotSeconds) yield return null;
            yield return null; yield return null;
            float hAfter = dummy.Health;
            while (Time.unscaledTimeAsDouble - t0 < 0.6) { yield return null; }
            float g0 = Time.time;
            while (Time.time - g0 < 1.7f)
            {
                yield return null;
                Assert.IsNull(_hero.CurrentTarget, "按住超過 0.2s：自此停火（不留普攻目標）");
                Assert.AreEqual(hAfter, dummy.Health, "按住超過 0.2s：木樁不再受傷");
            }
            _input.CancelSimulatedHold(0);
        }

        [UnityTest] // A12d(i)：按住→DASH 取消→滑步中再按住→放開時錐內沒人：仍接回原目標
        public IEnumerator A12d1_Bow_RepressDuringDash_KeepsResumeTarget()
        {
            yield return LockBowOnDummy();
            DummyTarget dummy = _bowDummy;
            PressAttack();
            yield return WaitUnscaled(0.4);
            Assert.IsNull(_hero.CurrentTarget, "前提：按住滿 0.2s 已停火");
            TapDash();
            Assert.IsTrue(_hero.Mover.IsDashing, "前提：DASH 滑步中");
            ReleaseAttack();
            PressAttack();   // 滑步還沒結束就再按住
            yield return WaitUnscaled(0.4);
            _lab.RotateThirdPerson(180f);   // 準星背對木樁：放開時錐內沒人
            yield return null;
            ReleaseAttack();
            float h1 = dummy.Health;
            yield return WaitForDrop(dummy, h1, 3f);
            Debug.Log("[BOWCHARGE TEST] A12d1 resumed=" + (h1 - dummy.Health).ToString("F3"));
            Assert.AreEqual(h1 - _hero.AttackDamage, dummy.Health, 1e-3f, "錐內沒人：接回原目標，木樁之後吃一般普攻 60");
        }

        [UnityTest] // A12d(ii)：按住滿蓄後放開時錐內沒人→接回原目標
        public IEnumerator A12d2_Bow_ReleaseWithEmptyCone_ResumesOriginalTarget()
        {
            yield return LockBowOnDummy();
            DummyTarget dummy = _bowDummy;
            PressAttack();
            yield return WaitUnscaled(0.5);
            Assert.IsNull(_hero.CurrentTarget, "前提：按住滿 0.2s 已停火");
            _lab.RotateThirdPerson(180f);
            yield return null;
            ReleaseAttack();
            float h1 = dummy.Health;
            yield return WaitForDrop(dummy, h1, 3f);
            Assert.AreEqual(h1 - _hero.AttackDamage, dummy.Health, 1e-3f, "錐內沒人：接回原目標，木樁之後吃一般普攻 60");
            Assert.AreSame(dummy, _hero.CurrentTarget);
        }

        [UnityTest] // A12d(iii)：按住弓時倒地→之後切 Standard、無目標按 ATK：不得用弓的舊黏性目標
        public IEnumerator A12d3_Bow_HoldInterruptedByKnockout_OtherWeaponIgnoresStaleSticky()
        {
            yield return SetupBow(6f);
            DummyTarget a = _bowDummy;                      // A：世界 +z、6m（準星 yaw 0 正對 A）
            DummyTarget b = SpawnDummyAt(a, -12f, 5f);      // B：世界角 -12°、5m
            b.Configure(1000f, b.TargetFaction);
            yield return null; yield return null;
            yield return QuickTapFrames(2);                 // 弓快速點擊鎖定 A
            Assert.AreSame(a, _hero.CurrentTarget, "前提：弓鎖定 A");
            PressAttack();
            yield return WaitUnscaled(0.4);
            Assert.IsNull(_hero.CurrentTarget, "前提：按住滿 0.2s 已停火");
            Vector3 stand = _hero.transform.position;
            _hero.TakeDuelDamage(100000f);                  // 倒地：輸入被鎖→按住結束
            yield return null; yield return null;
            Assert.IsFalse(_hero.IsAlive, "前提：英雄倒地");
            ReleaseAttack();
            _hero.ResetForDuel(stand);
            yield return null; yield return null;
            Assert.IsTrue(_hero.IsAlive);
            SelectWeaponByTaps(3);                          // 弓→錘→鉤鎖→Standard
            Assert.AreEqual(0, (int)_lab.CurrentWeapon, "前提：Standard");
            _lab.RotateThirdPerson(-12f);                   // 準星正對 B（B 0°、A 12°）
            yield return null; yield return null;
            Assert.IsNull(_hero.CurrentTarget, "前提：沒有目標");
            TapAttack();
            Assert.AreSame(b, _lab.LastAimTarget, "Standard 無目標按 ATK：挑準星正前方的 B，不得用弓的舊黏性目標 A");
        }
    }
}
#endif
