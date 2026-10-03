#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Vow.Core.Logic;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓「蓄滿一條線」覆審 r2 的重現與修正驗收（凍結檔 acceptance-bowline-20261003.md「修訂 R2」：N1、N2、N3、穿透同式；
    // 測試幾何在動手前寫死於 vow-toolchain/acceptance-bowline-r2-interp.md）。只用 6bd257f 已有的成員，修前也編得過、紅在行為上。
    public sealed partial class CameraLabCombatPlayTests
    {
        // ── N1：近處正在打的 A 不得因身體半徑搶走精準瞄 B 的蓄滿箭（覆審探針 Q9 前三組）──
        [UnityTest] // R2-N1（穿透卷修訂 P2 改寫）：A 3m/10°、3m/6°、6m/6°＋6m/8°（A 在線外），B 10m 準星正對，蓄滿 1.0s →
                    // 直接目標＝B、B 受 108；A 的身體在穿透線（英雄＋1.0m、沿準星水平、半徑 0.15）內＝A 也受 108（穿透是預期），線外＝A 不受傷
        public IEnumerator R2_N1_FullChargeAtFarB_NearStickyADoesNotSteal()
        {
            float[] nearD = { 3f, 3f, 6f, 6f };
            float[] sepDeg = { 10f, 6f, 6f, 8f };
            bool sawInside = false, sawOutside = false;
            for (int i = 0; i < nearD.Length; i++)
            {
                yield return SetupBow(nearD[i]);
                DummyTarget a = _bowDummy;
                DummyTarget b = SpawnDummyAt(a, sepDeg[i], 10f);
                b.Configure(1000f, b.TargetFaction);
                yield return null;
                float ha = a.Health;
                TapAttack();
                yield return WaitForDrop(a, ha, 2f);
                yield return WaitSeconds(0.1f);
                Assert.Less(a.Health, ha, "測試前提：英雄正在打 A");
                Assert.AreSame(a, _hero.CurrentTarget, "測試前提：A 是當前目標（黏性偏好）");
                _lab.RotateThirdPerson(sepDeg[i]);
                yield return null; yield return null;
                float clearance = N1BodyClearanceFromPierceLine(a, sepDeg[i]);
                float a0 = a.Health, b0 = b.Health;
                yield return HoldAttack(1.0);
                object picked = _lab.LastAimTarget;
                yield return WaitForDrop(b, b0, 2f);
                yield return WaitSeconds(0.3f);
                string who = picked == null ? "none" : ReferenceEquals(picked, a) ? "A" : ReferenceEquals(picked, b) ? "B" : "other";
                Debug.Log("[BOWLINE R2] N1 nearA=" + nearD[i] + "m sep=" + sepDeg[i] + "deg picked=" + who
                    + " dmgA=" + (a0 - a.Health).ToString("F4") + " dmgB=" + (b0 - b.Health).ToString("F4")
                    + " bodyClearance=" + clearance.ToString("F3") + " axisDist=" + (clearance + 0.5f).ToString("F3"));
                Assert.AreSame(b, picked, "A " + nearD[i] + "m/" + sepDeg[i] + "°：蓄滿精準瞄 B 應挑 B（黏性不得用加寬半角）");
                float full = _hero.AttackDamage * BowChargedDamageMultiplier;
                Assert.AreEqual(b0 - full, b.Health, 1e-3f, "B 受蓄滿 108");
                // 修訂 P2：A 身體離穿透線軸 ≤0.15−0.03（線內）＝穿透也中 108；≥0.15＋0.03（軸距 ≥0.68）＝不受傷；中間帶不出題。
                Assert.IsTrue(clearance <= 0.12f || clearance >= 0.18f, "測試前提：A 不在線半徑邊界帶（clearance=" + clearance.ToString("F3") + "）");
                if (clearance <= 0.12f)
                {
                    sawInside = true;
                    Assert.AreEqual(a0 - full, a.Health, 1e-3f, "A 身體在穿透線內：同一發穿透也受 108（直接目標仍是 B）");
                }
                else
                {
                    sawOutside = true;
                    Assert.AreEqual(a0, a.Health, "A 身體離穿透線 ≥0.65m：A 不因這一發受傷");
                }
                Object.Destroy(b.gameObject);
            }
            Assert.IsTrue(sawInside && sawOutside, "測試前提：線內、線外兩種情境都有（inside=" + sawInside + " outside=" + sawOutside + "）");
        }

        // A 的碰撞體表面到穿透線軸的最短距離（線＝英雄 pivot＋1.0m、沿 bearingDeg 的水平方向、長 16m；凍結檔 acceptance-pierce-20261003.md 規格值）。
        private float N1BodyClearanceFromPierceLine(DummyTarget a, float bearingDeg)
        {
            Collider col = a.GetComponent<Collider>();
            float r = bearingDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            Vector3 origin = _hero.transform.position + Vector3.up * 1.0f;
            float best = float.PositiveInfinity;
            for (float t = 0f; t <= 16f; t += 0.01f)
            {
                Vector3 p = origin + dir * t;
                float d = Vector3.Distance(p, col.ClosestPoint(p));
                if (d < best) best = d;
            }
            return best;
        }

        // ── N2：箭／提示的「是不是弓」以起手時的武器為準 ──
        [UnityTest] // R2-N2：劍打 2m 木樁、前搖開始那一幀換弓：劍那一下（60）結算時不生箭、出手／命中提示都不增
        public IEnumerator R2_N2_SwordWindupThenSwitchToBow_SwordHitIsNotAnArrow()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            dummy.Configure(1000f, dummy.TargetFaction);
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 2f);
            SelectWeaponByTaps(1);
            yield return null; yield return null;
            Assert.AreEqual(WeaponId.Sword, _lab.CurrentWeapon, "測試前提：拿劍");
            bool windup = false;
            System.Action onWindup = () => windup = true;
            _hero.OnAttackWindupStarted += onWindup;
            try
            {
                float h0 = dummy.Health;
                int s0 = LastArrow().Serial, r0 = ReleaseCues(), c0 = HitCues();
                TapAttack();
                float until = Time.time + 1f;
                while (!windup && Time.time < until) yield return null;
                Assert.IsTrue(windup, "測試前提：劍的前搖開始");
                Assert.AreEqual(h0, dummy.Health, "測試前提：換武器時劍還沒結算");
                TapWeapon();
                Assert.AreEqual(WeaponId.Bow, _lab.CurrentWeapon, "測試前提：前搖中已換成弓");
                until = Time.time + 1f;
                while (dummy.Health >= h0 && Time.time < until) yield return null;
                float firstHit = h0 - dummy.Health;
                int arrowsAtHit = LastArrow().Serial - s0, releaseAtHit = ReleaseCues() - r0;
                Assert.AreEqual(60f, firstHit, 1e-3f, "測試前提：劍那一下結算 60");
                // 劍那一下之後、下一次（弓的）傷害之前：命中提示不得出現
                float h1 = dummy.Health;
                until = Time.time + 0.6f;
                while (Time.time < until && dummy.Health >= h1) yield return null;
                int hitCuesBeforeNext = HitCues() - c0;
                Debug.Log("[BOWLINE R2] N2 swordHit=" + firstHit.ToString("F1") + " arrowsAtHit=" + arrowsAtHit + " releaseAtHit=" + releaseAtHit
                    + " hitCuesBeforeNextDamage=" + hitCuesBeforeNext);
                Assert.AreEqual(0, arrowsAtHit, "劍的傷害不得生弓箭（以起手武器為準）");
                Assert.AreEqual(0, releaseAtHit, "劍的傷害不得增加 BowReleaseCueCount");
                Assert.AreEqual(0, hitCuesBeforeNext, "劍的傷害不得增加 ArrowHitCueCount");
            }
            finally { _hero.OnAttackWindupStarted -= onWindup; }
        }

        // ── N3：身體半徑＝碰撞盒垂直於視線的投影半寬（上限 1.0m）──
        private TestWallTarget FindTestWallA()
        {
            foreach (TestWallTarget w in Object.FindObjectsOfType<TestWallTarget>()) if (w.name == "TestWall_A") return w;
            return null;
        }

        // 英雄站在牆心 6m 處（endOn＝長軸指向英雄；否則寬面朝英雄），準星到牆心的垂直距離 lateral，往順時針側偏。
        private IEnumerator SetupWallShot(bool endOn, float lateral)
        {
            yield return Load();
            TestWallTarget wall = FindTestWallA();
            Assert.IsNotNull(wall, "測試前提：TestWall_A 存在");
            Vector3 c = wall.transform.position;
            Vector3 toHero = endOn ? -wall.transform.right : -wall.transform.forward;
            toHero.y = 0f; toHero.Normalize();
            Vector3 hp = c + toHero * 6f;
            hp.y = _hero.transform.position.y;
            yield return WarpAndSettle(hp);
            SelectWeaponByTaps(2);
            yield return null; yield return null;
            Assert.AreEqual(WeaponId.Bow, _lab.CurrentWeapon, "測試前提：拿弓");
            Vector3 toWall = c - _hero.transform.position; toWall.y = 0f;
            Assert.AreEqual(6f, toWall.magnitude, 0.05f, "測試前提：牆心 6m");
            float yaw = Mathf.Atan2(toWall.x, toWall.z) * Mathf.Rad2Deg + Mathf.Asin(lateral / toWall.magnitude) * Mathf.Rad2Deg;
            _lab.RotateThirdPerson(Mathf.DeltaAngle(_lab.YawDegrees, yaw));
            yield return null; yield return null;
        }

        [UnityTest] // R2-N3(a)：石牆端面朝英雄 6m、準星在牆外 1.3m（離牆心 1.6m）：快速射擊與蓄滿都不中（空射、牆不掉血）
        public IEnumerator R2_N3_WallEndOn_AimOutsideByOnePointThree_QuickAndFullMiss()
        {
            yield return SetupWallShot(true, 1.6f);
            TestWallTarget wall = FindTestWallA();
            float w0 = wall.Health;
            TapAttack();
            object quickPick = _lab.LastAimTarget;
            yield return WaitSeconds(1.5f);
            float quickDamage = w0 - wall.Health;
            float w1 = wall.Health;
            yield return HoldAttack(1.0);
            object fullPick = _lab.LastAimTarget;
            yield return WaitSeconds(1.5f);
            float fullDamage = w1 - wall.Health;
            Debug.Log("[BOWLINE R2] N3 endOn lateral=1.6 quickPicked=" + (quickPick != null) + " quickDamage=" + quickDamage.ToString("F1")
                + " fullPicked=" + (fullPick != null) + " fullDamage=" + fullDamage.ToString("F1"));
            Assert.IsNull(quickPick, "端面朝向、準星在牆外 1.3m：快速射擊不得挑到牆");
            Assert.AreEqual(0f, quickDamage, "快速射擊：牆不掉血");
            Assert.IsNull(fullPick, "端面朝向、準星在牆外 1.3m：蓄滿不得挑到牆");
            Assert.AreEqual(0f, fullDamage, "蓄滿：牆不掉血");
        }

        [UnityTest] // R2-N3(b)：石牆寬面朝英雄 6m、準星在牆內 1.0m（離牆心 1.0m）：蓄滿中
        public IEnumerator R2_N3_WallSideOn_AimInsideByOne_FullChargeHits()
        {
            yield return SetupWallShot(false, 1.0f);
            TestWallTarget wall = FindTestWallA();
            float w0 = wall.Health;
            yield return HoldAttack(1.0);
            object picked = _lab.LastAimTarget;
            float until = Time.time + 2f;
            while (wall.Health >= w0 && Time.time < until) yield return null;
            Debug.Log("[BOWLINE R2] N3 sideOn lateral=1.0 picked=" + (picked != null) + " damage=" + (w0 - wall.Health).ToString("F1"));
            Assert.AreSame(wall, picked, "寬面朝向、準星在牆內 1.0m：蓄滿挑到牆");
            Assert.Less(wall.Health, w0, "蓄滿真的打到牆");
        }
    }
}
#endif
