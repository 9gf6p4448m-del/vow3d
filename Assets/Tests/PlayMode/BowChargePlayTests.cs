#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓蓄力＋五把武器按住預覽 凍結驗收 A3～A8 的真場景部分。凍結檔：vow-toolchain/acceptance-bowcharge-20261003.md。
    // 全部走真路由：ATK 用 PlayerInputService 的模擬按住手指（Began→每幀 Stationary→Ended／Canceled），DASH／WPN 用 SendScreenTap。
    // 刻意只「直接」引用基底 05f97b9 也有的公開成員，讓同一份測試能在基底編得過、紅在行為斷言（A10）。
    // 新成員（BowShotCount、ActivePreview、AimPreviewIndicator）以反射讀：基底沒有這些成員＝弓從不計出手、從不顯示預覽，
    // 讀成 0／None／無 indicator（不是屬性錯誤），斷言照樣落在行為上。
    public sealed partial class CameraLabCombatPlayTests
    {
        private const float BowChargedDamageMultiplier = 1.8f;   // 規格表：滿蓄倍率

        // ── 反射讀新成員（基底沒有＝行為上的「沒有」）──
        private static object ReadProperty(object owner, string name)
        {
            PropertyInfo p = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return p != null ? p.GetValue(owner) : null;
        }

        private int BowShots()
        {
            object v = ReadProperty(_lab, "BowShotCount");
            return v != null ? (int)v : 0;
        }

        private string PreviewKind()
        {
            object preview = ReadProperty(_lab, "ActivePreview");
            if (preview == null) return "None";
            return preview.GetType().GetField("Kind").GetValue(preview).ToString();
        }

        private float PreviewHalfAngle()
        {
            object preview = ReadProperty(_lab, "ActivePreview");
            return preview != null ? (float)preview.GetType().GetField("HalfAngleDegrees").GetValue(preview) : -1f;
        }

        private float PreviewRange()
        {
            object preview = ReadProperty(_lab, "ActivePreview");
            return preview != null ? (float)preview.GetType().GetField("RangeMeters").GetValue(preview) : -1f;
        }

        private bool PreviewIndicatorActive()
        {
            GameObject indicator = ReadProperty(_lab, "AimPreviewIndicator") as GameObject;
            if (indicator == null || !indicator.activeInHierarchy) return false;
            LineRenderer line = indicator.GetComponent<LineRenderer>();
            return line != null && line.enabled && line.positionCount > 2;
        }

        // ── ATK 按住／放開（模擬手指走真實觸控路由）──
        private void PressAttack() { Vector2 c = Center(_lab.ActionButtonLayout.Attack); _input.BeginSimulatedHold(0, c.x, c.y); }
        private void ReleaseAttack() { _input.EndSimulatedHold(0); }

        private static IEnumerator WaitUnscaled(double seconds)
        {
            double start = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - start < seconds) yield return null;
        }

        // 按住 seconds（觸控路由的時鐘＝unscaled）後放開。
        private IEnumerator HoldAttack(double seconds)
        {
            PressAttack();
            yield return WaitUnscaled(seconds);
            ReleaseAttack();
        }

        private static IEnumerator WaitForDrop(DummyTarget dummy, float before, float seconds)
        {
            float until = Time.time + seconds;
            while (dummy.Health >= before && Time.time < until) yield return null;
        }

        // 由預設 Standard 起按 WPN：1 下劍、2 下弓、3 下錘、4 下鉤鎖（整數＝基底也編得過）。
        private void SelectWeaponByTaps(int index) { for (int i = 0; i < index; i++) TapWeapon(); }

        private DummyTarget _bowDummy;

        // 木樁在英雄正前方（+z、準星正對）distance 公尺；血量加厚；選好弓。
        private IEnumerator SetupBow(float distance)
        {
            yield return Load();
            _bowDummy = Object.FindObjectOfType<DummyTarget>();
            _bowDummy.Configure(1000f, _bowDummy.TargetFaction);
            yield return WarpAndSettle(_bowDummy.transform.position + Vector3.back * distance);
            Assert.AreEqual(0f, _lab.YawDegrees);
            SelectWeaponByTaps(2);
            yield return null; yield return null;
        }

        private IEnumerator ResetHeroTarget()
        {
            _hero.ClearCombatTargetInPlace();
            yield return WaitSeconds(0.9f);   // 讓前搖／後搖走完、錘冷卻過
        }

        [UnityTest] // A3(a)(b)：14m 錐內木樁——快速點擊不受傷（超出 12m）；按住 1.0s 放開受傷 60×1.8
        public IEnumerator A3ab_Bow_FourteenMetres_QuickTapMisses_FullChargeHitsForOnePointEight()
        {
            yield return SetupBow(14f);
            DummyTarget dummy = _bowDummy;
            float damage = _hero.AttackDamage;
            Assert.AreEqual(60f, damage, "規格以普攻 60 為準");

            float h0 = dummy.Health;
            TapAttack();
            yield return WaitSeconds(1.5f);
            Assert.AreEqual(h0, dummy.Health, "快速點擊：14m 超出 12m，木樁不受傷");

            float h1 = dummy.Health;
            yield return HoldAttack(1.0);
            yield return WaitForDrop(dummy, h1, 2f);
            Debug.Log("[BOWCHARGE TEST] A3b hit=" + (h1 - dummy.Health).ToString("F4"));
            Assert.AreEqual(h1 - damage * BowChargedDamageMultiplier, dummy.Health, 1e-3f,
                "按住 1.0s 放開：射程延伸到 16m，木樁受傷 60×1.8");
            AssertBowSelected();
        }

        [UnityTest] // A3(c)：6m、偏軸 8°——快速點擊受傷 60；按住 1.0s 放開不受傷（錐收到 4°）
        public IEnumerator A3c_Bow_SixMetresEightDegreesOff_QuickTapHits_FullChargeConeTooNarrow()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            _lab.RotateThirdPerson(8f);   // 準星往 +x 轉 8°：木樁在準星左 8°
            yield return null; yield return null;

            float h0 = dummy.Health;
            TapAttack();
            yield return WaitForDrop(dummy, h0, 1.5f);
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "快速點擊：8° 在 12° 錐內，受傷 60");

            yield return ResetHeroTarget();
            float h1 = dummy.Health;
            yield return HoldAttack(1.0);
            yield return WaitSeconds(1.5f);
            Assert.AreEqual(h1, dummy.Health, "按住 1.0s 放開：錐收到 4°，8° 的木樁不受傷");
            AssertBowSelected();
        }

        [UnityTest] // A4：一前一後兩木樁同線——快速點擊只最近者受傷；滿蓄放開兩者都受傷
        public IEnumerator A4_Bow_FullChargePiercesBothDummies_QuickTapHitsNearestOnly()
        {
            yield return SetupBow(6f);
            DummyTarget near = _bowDummy;
            DummyTarget far = SpawnDummyAt(near, 0f, 10f);
            far.Configure(1000f, far.TargetFaction);
            _bootstrap.TargetRegistry.Register(far);   // 比照場景木樁：碰撞體登記進查表（裂風矢同一份）
            yield return null; yield return null;

            float n0 = near.Health, f0 = far.Health;
            TapAttack();
            yield return WaitForDrop(near, n0, 1.5f);
            Assert.Less(near.Health, n0, "快速點擊：最近者受傷");
            yield return WaitSeconds(0.3f);
            Assert.AreEqual(f0, far.Health, "快速點擊：不穿透，後方木樁不受傷");

            yield return ResetHeroTarget();
            float n1 = near.Health, f1 = far.Health;
            yield return HoldAttack(1.0);
            yield return WaitForDrop(near, n1, 2f);
            yield return null;
            Debug.Log("[BOWCHARGE TEST] A4 near=" + (n1 - near.Health).ToString("F3") + " far=" + (f1 - far.Health).ToString("F3"));
            Assert.Less(near.Health, n1, "滿蓄：前方木樁受傷");
            Assert.Less(far.Health, f1, "滿蓄：穿透，後方木樁也受傷");
            AssertBowSelected();
        }

        [UnityTest] // A5：錐內無目標，按住 0.5s 放開→算一次出手（BowShotCount+1）、無人受傷
        public IEnumerator A5_Bow_NoTargetInCone_HalfCharge_CountsAsShot_NoDamage()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            _lab.RotateThirdPerson(180f);   // 準星背對木樁
            yield return null; yield return null;
            int shots0 = BowShots();
            float h0 = dummy.Health;
            yield return HoldAttack(0.5);
            Assert.AreEqual(shots0 + 1, BowShots(), "錐內無目標：放開照樣朝準星出手一次");
            yield return WaitSeconds(1.0f);
            Assert.AreEqual(h0, dummy.Health, "無人受傷");
            Assert.IsNull(_hero.CurrentTarget, "沒有鎖定任何目標");
            AssertBowSelected();
        }

        [UnityTest] // A6：蓄力中 DASH／切 WPN／切 TOP／觸控 Canceled 後放開→不出手、木樁不受傷、預覽消失
        public IEnumerator A6_Bow_ChargeCanceled_ByDashWeaponTopOrTouchCancel_NoShot()
        {
            string[] labels = { "DASH", "WPN", "TOP", "觸控 Canceled" };
            for (int variant = 0; variant < labels.Length; variant++)
            {
                string label = labels[variant];
                yield return SetupBow(6f);
                DummyTarget dummy = _bowDummy;
                int shots0 = BowShots();
                float h0 = dummy.Health;
                PressAttack();
                yield return WaitUnscaled(0.4);
                if (variant == 0) TapDash();
                else if (variant == 1) TapWeapon();
                else if (variant == 2) _lab.SetThirdPerson(false);
                else _input.CancelSimulatedHold(0);
                yield return null;
                Assert.AreEqual("None", PreviewKind(), label + "：作廢後預覽 Kind=None");
                Assert.IsFalse(PreviewIndicatorActive(), label + "：作廢後 indicator 收起");
                ReleaseAttack();
                yield return WaitSeconds(1.0f);
                Assert.AreEqual(h0, dummy.Health, label + "：作廢後放開不出手，木樁不受傷");
                Assert.AreEqual(shots0, BowShots(), label + "：BowShotCount 不變");
                Assert.AreEqual("None", PreviewKind(), label + "：放開後仍無預覽");
            }
        }

        [UnityTest] // A7：Standard／Sword／Hammer／Grapple 按住 1.0s 的傷害與點擊相同（按下即出手、放開不再出手、沒有蓄力倍率）
        public IEnumerator A7_OtherWeapons_HoldOneSecond_DamageEqualsTap()
        {
            int[] weapons = { 0, 1, 3, 4 };                   // Standard、Sword、Hammer、Grapple（WPN 下數）
            string[] labels = { "Standard", "Sword", "Hammer", "Grapple" };
            float[] distances = { 3f, 3f, 3f, 9f };
            for (int w = 0; w < weapons.Length; w++)
            {
                string label = labels[w];
                yield return Load();
                DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
                dummy.Configure(1000f, dummy.TargetFaction);
                Vector3 stand = dummy.transform.position + Vector3.back * distances[w];
                yield return WarpAndSettle(stand);
                SelectWeaponByTaps(weapons[w]);
                Assert.AreEqual(weapons[w], (int)_lab.CurrentWeapon, label + " 選取");
                yield return null; yield return null;

                float h0 = dummy.Health;
                TapAttack();
                yield return WaitForDrop(dummy, h0, 2f);
                float tapHit = h0 - dummy.Health;
                Assert.Greater(tapHit, 0f, label + "：點擊要真的命中（對照組）");

                _hero.ClearCombatTargetInPlace();
                yield return WaitSeconds(w == 3 ? 4.3f : 0.9f);   // 鉤鎖冷卻 4s
                yield return WarpAndSettle(stand);
                yield return null;

                float h1 = dummy.Health;
                int count1 = _lab.AimAttackCount;
                PressAttack();
                Assert.AreEqual(count1 + 1, _lab.AimAttackCount, label + "：按下當下就出手");
                double t0 = Time.unscaledTimeAsDouble;
                float holdHit = -1f;
                bool released = false;
                double heldAtRelease = 0.0;
                float until = Time.time + 4f;
                while (Time.time < until && (holdHit < 0f || !released))
                {
                    yield return null;
                    if (holdHit < 0f && dummy.Health < h1) holdHit = h1 - dummy.Health;
                    if (!released && Time.unscaledTimeAsDouble - t0 >= 1.0)
                    {
                        int before = _lab.AimAttackCount;
                        heldAtRelease = Time.unscaledTimeAsDouble - t0;
                        ReleaseAttack();
                        released = true;
                        Assert.AreEqual(before, _lab.AimAttackCount, label + "：放開不再出手");
                    }
                }
                Debug.Log("[BOWCHARGE TEST] A7 " + label + " tap=" + tapHit.ToString("F3") + " hold=" + holdHit.ToString("F3"));
                Assert.IsTrue(released, label + "：有按滿 1.0s 再放開");
                if (weapons[w] == 3)
                {
                    // 錘蓄力重擊修訂一（acceptance-hammer-20261006.md，使用者 2026-10-06 同意取代）：按住 1.0s＝p 對應倍率 1+0.6p（p＝t/1.2）。
                    float p = Mathf.Min((float)(heldAtRelease / 1.2), 1f);
                    Assert.AreEqual(tapHit * (1f + 0.6f * p), holdHit, 0.05f, label + "：按住 1.0s 放開＝1+0.6p 倍（p=" + p + "）");
                }
                else Assert.AreEqual(tapHit, holdHit, 1e-3f, label + "：按住 1.0s 的傷害與點擊相同（沒有蓄力效果）");

                // 覆審 r1 M3：放開之後那一發也要量——放開不得把下一發升級（倍率／穿透）。
                float hr = dummy.Health;
                if (weapons[w] == 3)
                {
                    yield return WaitSeconds(1f);
                    Assert.AreEqual(hr, dummy.Health, 1e-3f, label + "：放開後不再出手（錘只在放開時橫掃一次）");
                }
                else
                {
                    yield return WaitForDrop(dummy, hr, 2f);
                    float afterRelease = hr - dummy.Health;
                    Debug.Log("[BOWCHARGE TEST] A7 " + label + " afterRelease=" + afterRelease.ToString("F3"));
                    Assert.AreEqual(tapHit, afterRelease, 1e-3f, label + "：放開後那一發＝一般普攻（與點擊相同，沒有蓄力倍率）");
                }
            }
        }

        [UnityTest] // A8：五把武器各按住 ATK→ActivePreview 與表一致、indicator active；弓半角隨按住時間單調遞減；放開後 None
        public IEnumerator A8_HoldAttack_PreviewPerWeapon_BowNarrowsOverTime_ClearsOnRelease()
        {
            string[] expectedKind = { "None", "Arc", "Cone", "Sector", "Cone" };
            float[] expectedHalf = { 0f, 180f, 12f, 50f, 20f };
            float[] expectedRange = { 0f, 5f, 12f, 3.5f, 10f };
            string[] labels = { "Standard", "Sword", "Bow", "Hammer", "Grapple" };
            for (int w = 0; w < labels.Length; w++)
            {
                string label = labels[w];
                yield return Load();
                SelectWeaponByTaps(w);
                Assert.AreEqual(w, (int)_lab.CurrentWeapon, label + " 選取");
                yield return null;
                PressAttack();   // 按下當下（同一幀、還沒過 0.2s）就要有預覽
                Assert.AreEqual(expectedKind[w], PreviewKind(), label + "：按住時的預覽形狀");
                if (w == 0)
                {
                    Assert.IsFalse(PreviewIndicatorActive(), "Standard：無預覽、不畫");
                }
                else
                {
                    Assert.IsTrue(PreviewIndicatorActive(), label + "：按住時 indicator 實際顯示");
                    if (w == 2)
                    {
                        // 起手（< 0.2s）＝快速射擊的錐；之後半角單調遞減到 4°、射程同步延伸到 16m。
                        float half = PreviewHalfAngle();
                        Assert.AreEqual(12f, half, 1e-3f, "弓：剛按下＝12°");
                        Assert.AreEqual(12f, PreviewRange(), 1e-3f, "弓：剛按下＝12m");
                        double[] marks = { 0.4, 0.7, 1.1 };
                        double pressedAt = Time.unscaledTimeAsDouble;
                        yield return null;
                        Assert.IsTrue(PreviewIndicatorActive(), "弓：下一幀 indicator 仍顯示");
                        for (int m = 0; m < marks.Length; m++)
                        {
                            while (Time.unscaledTimeAsDouble - pressedAt < marks[m]) yield return null;
                            yield return null;
                            float next = PreviewHalfAngle();
                            float range = PreviewRange();
                            Debug.Log("[BOWCHARGE TEST] A8 bow mark=" + marks[m] + " half=" + next.ToString("F3") + " range=" + range.ToString("F3"));
                            Assert.Less(next, half, "弓：半角隨按住時間單調遞減（" + marks[m] + "s）");
                            Assert.AreEqual(12f + (12f - next) * 0.4f, range, 1e-3f, "弓：射程與半角同一個 p（12+4p、12−10p；bowline C8）");
                            Assert.IsTrue(PreviewIndicatorActive(), "弓：蓄力中 indicator 持續顯示");
                            half = next;
                        }
                        Assert.AreEqual(2f, half, 1e-3f, "弓：滿蓄收到 2°（bowline C8：4→2）");
                        Assert.AreEqual(16f, PreviewRange(), 1e-3f, "弓：滿蓄 16m");
                    }
                    else
                    {
                        Assert.AreEqual(expectedHalf[w], PreviewHalfAngle(), 1e-3f, label + "：半角");
                        Assert.AreEqual(expectedRange[w], PreviewRange(), 1e-3f, label + "：半徑／射程");
                        yield return WaitUnscaled(0.3);
                        Assert.AreEqual(expectedKind[w], PreviewKind(), label + "：按住 0.3s 仍是同一個預覽");
                        Assert.IsTrue(PreviewIndicatorActive(), label + "：按住 0.3s indicator 仍顯示");
                    }
                }
                ReleaseAttack();
                Assert.AreEqual("None", PreviewKind(), label + "：放開後立即 Kind=None");
                Assert.IsFalse(PreviewIndicatorActive(), label + "：放開後 indicator 立即收起");
                yield return null;
                Assert.AreEqual("None", PreviewKind(), label + "：下一幀仍無預覽");
            }
        }

        private void AssertBowSelected() { Assert.AreEqual(2, (int)_lab.CurrentWeapon, "WPN 第 2 下應為弓"); }
    }
}
#endif
