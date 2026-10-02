#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓蓄力 追加 A11（使用者 2026-10-03 裁定 M1 選 A「蓄力時停火」）＋覆審 r2 N1（H1d）。
    // 凍結檔：vow-toolchain/acceptance-bowcharge-20261003.md 末段「追加 A11」。
    public sealed partial class CameraLabCombatPlayTests
    {
        // 弓快速點擊鎖定 6m 木樁，等第一下命中（英雄進入自動普攻）。
        private IEnumerator LockBowOnDummy()
        {
            yield return SetupBow(6f);
            float h0 = _bowDummy.Health;
            TapAttack();
            yield return WaitForDrop(_bowDummy, h0, 1.5f);
            Assert.Less(_bowDummy.Health, h0, "前提：快速點擊鎖定後自動普攻命中");
            Assert.AreSame(_bowDummy, _hero.CurrentTarget, "前提：英雄正在打木樁");
        }

        [UnityTest] // A11a：鎖定木樁中按住 1.0s→按住期間不掉血、AimAttackCount 不增加；放開後恰出一發 108
        public IEnumerator A11a_Bow_HoldingStopsAutoAttack_ReleaseFiresExactlyOneChargedShot()
        {
            yield return LockBowOnDummy();
            DummyTarget dummy = _bowDummy;
            float h0 = dummy.Health;
            int count0 = _lab.AimAttackCount;
            PressAttack();
            double t0 = Time.unscaledTimeAsDouble;
            float g0 = Time.time;
            // 按住同時滿足：觸控時鐘 ≥ 1.0s（滿蓄）且遊戲時間 ≥ 1.7s（＞2 個攻擊週期，舊行為必然再出手）
            while (Time.unscaledTimeAsDouble - t0 < 1.0 || Time.time - g0 < 1.7f)
            {
                yield return null;
                Assert.AreEqual(h0, dummy.Health, "按住期間仍自動普攻：木樁不得掉血");
            }
            Assert.AreEqual(count0, _lab.AimAttackCount, "按住期間 AimAttackCount 不增加");
            ReleaseAttack();
            Assert.AreEqual(count0 + 1, _lab.AimAttackCount, "放開恰出手一次");
            yield return WaitForDrop(dummy, h0, 2f);
            Debug.Log("[BOWCHARGE TEST] A11a release hit=" + (h0 - dummy.Health).ToString("F3"));
            Assert.AreEqual(h0 - _hero.AttackDamage * BowChargedDamageMultiplier, dummy.Health, 1e-3f, "放開後那一發＝滿蓄 108");
        }

        [UnityTest] // A11b：按住中 DASH 取消→自動普攻恢復（木樁之後受傷）
        public IEnumerator A11b_Bow_HoldCanceledByDash_AutoAttackResumes()
        {
            yield return LockBowOnDummy();
            DummyTarget dummy = _bowDummy;
            int shots0 = BowShots();
            PressAttack();
            yield return WaitUnscaled(0.4);
            TapDash();
            yield return null;
            ReleaseAttack();
            float h1 = dummy.Health;
            yield return WaitForDrop(dummy, h1, 3f);
            Debug.Log("[BOWCHARGE TEST] A11b resumed hit=" + (h1 - dummy.Health).ToString("F3"));
            Assert.AreEqual(h1 - _hero.AttackDamage, dummy.Health, 1e-3f, "取消後恢復自動普攻：木樁之後吃一般普攻 60");
            Assert.AreEqual(shots0, BowShots(), "取消不算出手");
        }

        [UnityTest] // A11c：快速點擊（跨幀、< 0.2s）不因停火規則消失——6m 偏 8° 照樣受傷 60（同 A3(c)）
        public IEnumerator A11c_Bow_QuickTapAcrossFrames_SixMetresEightDegrees_StillHitsSixty()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            _lab.RotateThirdPerson(8f);
            yield return null; yield return null;
            float h0 = dummy.Health;
            yield return HoldAttack(0.1);   // 真實手指的快速點擊：按下與放開隔幾幀
            yield return WaitForDrop(dummy, h0, 1.5f);
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "快速點擊：8° 在 12° 錐內，受傷 60");

            // 已在打的目標再快速點擊一次：照樣鎖定、繼續命中（黏性不因按住中的停火而遺失）
            float h1 = dummy.Health;
            yield return HoldAttack(0.1);
            Assert.AreSame(dummy, _hero.CurrentTarget, "再次快速點擊仍鎖定同一木樁");
            yield return WaitForDrop(dummy, h1, 1.5f);
            Assert.AreEqual(h1 - _hero.AttackDamage, dummy.Health, 1e-3f, "再次快速點擊：下一下仍是 60");
        }

        [UnityTest] // A11d：其他武器按住 1.0s 期間照舊自動普攻（放開前就掉血）
        public IEnumerator A11d_OtherWeapons_HoldingKeepsAutoAttacking()
        {
            int[] weapons = { 0, 1, 4 };   // Standard、Sword、Grapple（錘只在按下時橫掃、沒有自動普攻）
            string[] labels = { "Standard", "Sword", "Grapple" };
            float[] distances = { 3f, 3f, 9f };
            for (int w = 0; w < weapons.Length; w++)
            {
                yield return Load();
                DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
                dummy.Configure(1000f, dummy.TargetFaction);
                yield return WarpAndSettle(dummy.transform.position + Vector3.back * distances[w]);
                SelectWeaponByTaps(weapons[w]);
                yield return null;
                float h0 = dummy.Health;
                PressAttack();
                double t0 = Time.unscaledTimeAsDouble;
                float g0 = Time.time;
                int hits = 0;
                float last = h0;
                while (Time.unscaledTimeAsDouble - t0 < 1.0 || Time.time - g0 < 1.7f)
                {
                    yield return null;
                    if (dummy.Health < last) { hits++; last = dummy.Health; }
                }
                ReleaseAttack();
                Debug.Log("[BOWCHARGE TEST] A11d " + labels[w] + " hitsWhileHeld=" + hits);
                Assert.GreaterOrEqual(hits, 2, labels[w] + "：按住期間照舊自動普攻（≥ 2 下）");
            }
        }

        [UnityTest] // 覆審 r2 N1／H1d：滿蓄放開→前搖內按 DASH 打斷→世界點擊同一目標→第一下 60
        public IEnumerator H1d_Bow_ChargedShotInterruptedByDash_LaterWorldTapIsNormalAttack()
        {
            yield return ReleaseFullChargeAtFourteen();
            TapDash();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(1000f, _bowDummy.Health, "前提：被打斷的蓄力箭沒命中");
            Assert.IsNull(_hero.CurrentTarget, "前提：DASH 在前搖中打斷並解除鎖定（ActiveDashLogic 直接對大腦下移動指令）");
            yield return WaitSeconds(0.8f);
            yield return WarpAndSettle(_h1Stand);
            yield return WorldTapDummy(_bowDummy);
            Assert.AreSame(_bowDummy, _hero.CurrentTarget, "前提：世界點擊點到木樁");
            yield return MeasureFirstHit(_bowDummy, _h1Behind, 4f);
            AssertNormalFirstHit("DASH 打斷");
        }
    }
}
#endif
