#if UNITY_EDITOR
using System.Collections;
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
    // v0.17.0 鉤鎖（拉自己到目標）凍結驗收 G2／G3／G4／G5／G7／G8 的真場景部分。
    // 凍結檔：vow-toolchain/acceptance-grapple-20261002.md。全部走真路由（WPN／ATK 鈕的 SendScreenTap）。
    // 刻意只用舊版（v0.16.0）也有的公開成員：鉤鎖以「WPN 第 4 下」選、行為以位置與血量量測，
    // 讓同一份測試能在舊版跑出「紅在行為斷言」（G9）。
    public sealed partial class CameraLabCombatPlayTests
    {
        private const int GrappleIndex = 4;   // WeaponId.Grapple；用整數是為了舊版也編得過
        private const float GrappleDistance = 9f;

        // 由預設 Standard 起按 WPN 4 下＝鉤鎖（Standard→Sword→Bow→Hammer→Grapple）。身分在各測試行為斷言之後才驗。
        private void SelectGrappleByTaps() { for (int i = 0; i < GrappleIndex; i++) TapWeapon(); }

        private void AssertGrappleSelected() { Assert.AreEqual(GrappleIndex, (int)_lab.CurrentWeapon, "WPN 第 4 下應為鉤鎖"); }

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // 木樁在英雄正前方（+z、準星正對）distance 公尺；木樁血量加厚，選好鉤鎖。
        private DummyTarget _grappleDummy;
        private IEnumerator SetupGrapple(float distance)
        {
            yield return Load();
            _grappleDummy = Object.FindObjectOfType<DummyTarget>();
            _grappleDummy.Configure(1000f, _grappleDummy.TargetFaction);
            yield return WarpAndSettle(_grappleDummy.transform.position + Vector3.back * distance);
            Assert.AreEqual(0f, _lab.YawDegrees);
            SelectGrappleByTaps();
            yield return null; yield return null;
        }

        // 按 ATK 後等 seconds（遊戲時間）。
        private IEnumerator TapAttackAndWait(float seconds)
        {
            TapAttack();
            yield return WaitSeconds(seconds);
        }

        [UnityTest] // G2：錐內 9m 木樁→0.5s 內被拉到距木樁 2m(±0.3)；鉤本身不扣血；抵達後自動接普攻；鉤鎖不被當錘清目標（G7）
        public IEnumerator G2_Grapple_PullsSelfToTwoMetres_WithinHalfSecond_NoHookDamage_ThenAutoAttacks()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            float h0 = dummy.Health;
            Vector3 start = Flat(_hero.transform.position);
            TapAttack();
            float pressedAt = Time.time;
            Assert.AreEqual(h0, dummy.Health, "鉤到瞬間目標血量不變");
            float arrivedAt = -1f;
            for (int i = 0; i < 45; i++)
            {
                yield return null;
                if (FlatDistance(_hero.transform.position, dummy.transform.position) <= 2.3f) { arrivedAt = Time.time; break; }
                Assert.AreEqual(h0, dummy.Health, "拉的途中不扣血");
            }
            Debug.Log("[CAMERA LAB TEST] G2 arrive=" + (arrivedAt - pressedAt).ToString("F3")
                + " dist=" + FlatDistance(_hero.transform.position, dummy.transform.position).ToString("F3"));
            Assert.Greater(arrivedAt, 0f, "按 ATK 後英雄沒被拉到木樁 2.3m 內");
            Assert.LessOrEqual(arrivedAt - pressedAt, 0.5f + 1e-3f, "0.5s 內抵達");
            Assert.AreEqual(h0, dummy.Health, "抵達當下仍未扣血（鉤本身不傷害）");
            for (int i = 0; i < 4; i++) yield return null;
            float settled = FlatDistance(_hero.transform.position, dummy.transform.position);
            Debug.Log("[CAMERA LAB TEST] G2 settled=" + settled.ToString("F3"));
            Assert.AreEqual(2f, settled, 0.3f, "停在距目標 2m 處");
            Assert.Greater((Flat(_hero.transform.position) - start).magnitude, 6f, "真的被拉過去（約 7m）");

            float deadline = Time.time + 1.5f;
            while (dummy.Health >= h0 && Time.time < deadline) yield return null;
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "抵達後自動接普攻（1.5s 內扣一次 AttackDamage）");
            Assert.AreSame(dummy, _hero.CurrentTarget, "鉤鎖不被當錘每幀清目標");
            AssertGrappleSelected();
        }

        [UnityTest] // G3(a)：準星偏 25°（錐外）→不鉤、不動；轉回正對（正向對照）→同位置鉤得到
        public IEnumerator G3a_Grapple_OutsideTwentyDegreeCone_NoHook()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            _lab.RotateThirdPerson(25f);
            yield return null; yield return null;
            Vector3 p0 = Flat(_hero.transform.position);
            float h0 = dummy.Health;
            yield return TapAttackAndWait(0.5f);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "錐外（25°）不鉤、英雄不動");
            Assert.AreEqual(h0, dummy.Health);

            _lab.RotateThirdPerson(-25f);
            yield return null; yield return null;
            yield return TapAttackAndWait(0.5f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "正向對照：正對時鉤得到");
            AssertGrappleSelected();
        }

        [UnityTest] // G3(b)：正前 10.6m（>10m）→不鉤、不動；走近到 9.5m（正向對照）→鉤得到
        public IEnumerator G3b_Grapple_BeyondTenMetres_NoHook()
        {
            yield return SetupGrapple(10.6f);
            DummyTarget dummy = _grappleDummy;
            Vector3 p0 = Flat(_hero.transform.position);
            yield return TapAttackAndWait(0.5f);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, ">10m 不鉤、英雄不動");

            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 9.5f);
            yield return TapAttackAndWait(0.5f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "正向對照：9.5m 鉤得到");
            AssertGrappleSelected();
        }

        [UnityTest] // G3(c)：石牆擋在中間（錐內）→不鉤、不動；牆移開（正向對照）→鉤得到
        public IEnumerator G3c_Grapple_StoneWallBlocksSight_NoHook()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            TestWallTarget wall = PlaceOwnTestWall(dummy.transform.position + Vector3.back * (GrappleDistance * 0.5f));
            yield return null; yield return null;
            Vector3 p0 = Flat(_hero.transform.position);
            yield return TapAttackAndWait(0.5f);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "牆後不鉤、英雄不動");

            wall.transform.position += Vector3.right * 30f;
            Physics.SyncTransforms();
            yield return null; yield return null;
            yield return TapAttackAndWait(0.5f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "正向對照：牆移開後鉤得到");
            AssertGrappleSelected();
        }

        [UnityTest] // G3(d)：崖台→谷底（準星對準、錐內、7.58m≤10m，但跨崖走不到）→不鉤、不動
        public IEnumerator G3d_Grapple_PlateauToValleyAcrossCliff_NoHook()
        {
            yield return StartCanyonMatchThirdPerson();
            SelectGrappleByTaps();
            yield return PlaceCanyon(TileCenter(5), TileCenter(4), true);
            Assert.AreEqual(1f, _hero.transform.position.y, 0.05f, "英雄在崖台");
            Assert.AreEqual(-1f, _red.transform.position.y, 0.05f, "對手在谷底");
            Assert.IsTrue(_hero.CanEngage(_red), "前提：視野規則允許交戰");
            Assert.LessOrEqual(FlatDistance(_hero.transform.position, _red.transform.position), 10f, "前提：在鉤距內");
            Vector3 p0 = Flat(_hero.transform.position);
            float r0 = _red.HealthNormalized;
            yield return TapAttackAndWait(0.5f);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "跨崖不鉤、英雄不動");
            Assert.AreEqual(r0, _red.HealthNormalized);
            AssertGrappleSelected();
        }

        [UnityTest] // G4：鉤一次後冷卻 4s——0.6s 與 3.8s 再按都不鉤（不動），4s 後可再鉤
        public IEnumerator G4_Grapple_CooldownFourSeconds()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            Vector3 home = dummy.transform.position + Vector3.back * GrappleDistance;
            TapAttack();
            float t0 = Time.time;
            yield return WaitSeconds(0.4f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "前提：第一鉤抵達");

            yield return WarpAndSettle(home);
            _hero.ClearCombatTargetInPlace();
            yield return null; yield return null;
            Vector3 p0 = Flat(_hero.transform.position);
            yield return TapAttackAndWait(0.5f);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "冷卻內（約 0.5s）再按不鉤、不動");

            while (Time.time < t0 + 3.8f) yield return null;
            p0 = Flat(_hero.transform.position);
            yield return TapAttackAndWait(0.15f);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "冷卻內（3.8s）再按不鉤、不動");

            while (Time.time < t0 + 4.02f) yield return null;
            yield return TapAttackAndWait(0.4f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "4s 後可再鉤");
            AssertGrappleSelected();
        }

        // 正前方 obstacleAhead 公尺處放一面一般實體方塊（4×3×0.4m，不擋視線、擋身體），按 ATK；
        // pressDeltaTime>0＝按下後 pressFrames 幀改用這個幀時間（模擬卡頓／低幀率），之後恢復 1/60。
        // 斷言：鉤得到並往前拉、停在牆前不穿牆；之後 2s 逐幀不穿牆、不接普攻（CurrentTarget 恆為 null）。
        private IEnumerator AssertBlockedPullStopsWithoutAttack(string tag, float obstacleAhead, float pressDeltaTime, int pressFrames)
        {
            yield return SetupGrapple(GrappleDistance);
            Vector3 h = _hero.transform.position;
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                block.transform.position = new Vector3(h.x, h.y + 1.5f, h.z + obstacleAhead);
                block.transform.localScale = new Vector3(4f, 3f, 0.4f);
                Physics.SyncTransforms();
                yield return null; yield return null;
                float wallFace = block.transform.position.z - 0.2f;
                float z0 = _hero.transform.position.z;
                TapAttack();
                if (pressDeltaTime > 0f)
                {
                    Time.captureDeltaTime = pressDeltaTime;
                    for (int i = 0; i < pressFrames; i++) yield return null;
                    Time.captureDeltaTime = 1f / 60f;
                }
                yield return WaitSeconds(0.5f);
                float z1 = _hero.transform.position.z;
                Debug.Log("[CAMERA LAB TEST] " + tag + " z0=" + z0.ToString("F3") + " z1=" + z1.ToString("F3") + " face=" + wallFace.ToString("F3"));
                Assert.Greater(z1 - z0, 1f, "前提：鉤得到並往前拉（一般障礙不擋視線）");
                Assert.Less(z1, wallFace, "停在牆前，不穿牆");
                float until = Time.time + 2f;
                while (Time.time < until)
                {
                    Assert.Less(_hero.transform.position.z, wallFace, "之後也沒穿過去");
                    Assert.IsNull(_hero.CurrentTarget, "被牆擋住：不接普攻（不得導航繞牆去追）");
                    yield return null;
                }
                AssertGrappleSelected();
            }
            finally { Time.captureDeltaTime = 1f / 60f; Object.Destroy(block); }
        }

        [UnityTest] // G5(a)：路徑上有一般實體障礙（不擋視線、擋身體）→鉤得到但停在牆前，不穿牆、不接普攻
        public IEnumerator G5a_Grapple_PathBlockedBySolid_StopsBeforeWall()
        {
            yield return AssertBlockedPullStopsWithoutAttack("G5a", 4.5f, 0f, 0);
        }

        [UnityTest] // 覆審 r1 M1：卡頓——按下後單幀 0.25s（>0.2s，整段拉動一幀走完），障礙在 4.5m→仍停在牆前、不接普攻
        public IEnumerator G5c_Grapple_SingleLongFrame_BlockedPull_StillNoAttack()
        {
            yield return AssertBlockedPullStopsWithoutAttack("G5c", 4.5f, 0.25f, 1);
        }

        [UnityTest] // 覆審 r1 M1：約 10fps——拉動分兩幀走完，障礙在後半段（6.3m）→仍停在牆前、不接普攻
        public IEnumerator G5d_Grapple_TenFps_ObstacleInSecondHalf_StillNoAttack()
        {
            yield return AssertBlockedPullStopsWithoutAttack("G5d", 6.3f, 0.1f, 2);
        }

        [UnityTest] // G5(b)：縛足（位移鎖）期間按 ATK→不位移
        public IEnumerator G5b_Grapple_MovementLocked_DoesNotMove()
        {
            yield return SetupGrapple(GrappleDistance);
            HeroLocomotion locomotion = _hero.GetComponent<HeroLocomotion>();
            Vector3 p0 = Flat(_hero.transform.position);
            locomotion.SetMovementLocked(true);
            TapAttack();
            // 英雄每幀依流沙狀態重設鎖；測試每幀補上，讓接下來的 Update（含鉤鎖逐幀位移）都在鎖內。
            for (int i = 0; i < 30; i++) { locomotion.SetMovementLocked(true); yield return null; }
            locomotion.SetMovementLocked(false);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "縛足時不位移");
            AssertGrappleSelected();
        }

        [UnityTest] // 覆審 r1 L1：真流沙縛足中按 ATK→不位移、也不起鉤（不吃冷卻）：縛足一結束立刻鉤得到
        public IEnumerator G5e_Grapple_RealQuicksandRoot_NoHookAndNoCooldownSpent()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            Vector3 start = _hero.transform.position;
            Assert.GreaterOrEqual(_bootstrap.ElementField.CastWater(start, (int)Faction.RedTeam), 0);
            _bootstrap.ElementField.NotifyWallActivated(start, Faction.RedTeam);
            yield return null;
            Assert.IsTrue(_hero.IsRooted, "前提：真流沙縛足");
            Vector3 p0 = Flat(_hero.transform.position);
            yield return TapAttackAndWait(0.5f);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "縛足時不位移");
            float deadline = Time.time + 2f;
            while (_hero.IsRooted && Time.time < deadline) yield return null;
            Assert.IsFalse(_hero.IsRooted, "前提：縛足 1.2s 後結束");
            yield return TapAttackAndWait(0.5f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f,
                "縛足中那一按不得吃冷卻：縛足結束後立刻鉤得到");
            AssertGrappleSelected();
        }

        // 起鉤後走一幀，再做 interrupt；0.5s 後英雄應停在半路（離木樁仍 >7.5m）、不接普攻。
        private IEnumerator AssertInterruptedPullStops(System.Action interrupt, string what)
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            AssertGrappleSelected();
            TapAttack();
            yield return null;
            interrupt();
            yield return WaitSeconds(0.5f);
            float d = FlatDistance(_hero.transform.position, dummy.transform.position);
            Debug.Log("[CAMERA LAB TEST] interrupt " + what + " dist=" + d.ToString("F3"));
            Assert.Greater(d, 7.5f, what + "：拉動應取消，英雄停在半路");
            Assert.IsNull(_hero.CurrentTarget, what + "：不接普攻");
        }

        [UnityTest] // 覆審 r1 L3：拉途中切武器→取消
        public IEnumerator G5f_Grapple_SwitchWeaponMidPull_Cancels()
        {
            yield return AssertInterruptedPullStops(TapWeapon, "拉途中切武器");
            Assert.AreEqual(WeaponId.Standard, _lab.CurrentWeapon);
        }

        [UnityTest] // 覆審 r1 L3：拉途中切回俯視→取消
        public IEnumerator G5g_Grapple_SwitchToTopMidPull_Cancels()
        {
            yield return AssertInterruptedPullStops(() => _lab.SetThirdPerson(false), "拉途中切回俯視");
        }

        [UnityTest] // 覆審 r1 L3：拉途中英雄倒地→取消
        public IEnumerator G5h_Grapple_HeroKnockedOutMidPull_Cancels()
        {
            yield return AssertInterruptedPullStops(() => _hero.TakeDuelDamage(100000f, DamageType.True), "拉途中英雄倒地");
            Assert.IsFalse(_hero.IsAlive, "前提：英雄已倒地");
        }

        // ───── 使用者 2026-10-02 補充裁定（覆審 r1 M2／M3）：射程內直接普攻；冷卻中退回普攻（射程內才打、射程外不動）─────

        // 普攻中清目標：後搖期間原地移動指令會排隊，等到目標清掉且可移動（最多 1.5s）再按，才是「冷卻有沒有被吃」的乾淨量測點。
        private IEnumerator WaitIdleAfterClear()
        {
            float until = Time.time + 1.5f;
            do yield return null; while ((_hero.CurrentTarget != null || !_hero.StateMachine.CanMove) && Time.time < until);
            Assert.IsNull(_hero.CurrentTarget, "前提：普攻目標已清掉");
            Assert.IsTrue(_hero.StateMachine.CanMove, "前提：可移動（不在後搖）");
        }

        [UnityTest] // M3：目標已在普攻射程內（3.5m）→直接普攻、不起鉤（英雄不動）、連按不打斷；不吃冷卻（隨後 9m 立刻鉤得到）
        public IEnumerator G2b_Grapple_TargetInAttackRange_DirectAttack_NoHookNoCooldown()
        {
            yield return SetupGrapple(3.5f);
            DummyTarget dummy = _grappleDummy;
            Assert.IsTrue(_hero.IsTargetInAttackRange(dummy), "前提：3.5m 在普攻射程內");
            Vector3 p0 = Flat(_hero.transform.position);
            float h0 = dummy.Health;
            TapAttack();
            Assert.AreSame(dummy, _hero.CurrentTarget, "射程內按下當下就走原普攻鎖定");
            float deadline = Time.time + 1.5f;
            bool tappedAgain = false;
            while (dummy.Health >= h0 && Time.time < deadline)
            {
                yield return null;
                if (!tappedAgain) { TapAttack(); tappedAgain = true; }   // 前搖中再按：不得起鉤、不得打斷
                Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "射程內不起鉤、英雄不動");
            }
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "射程內直接普攻命中");
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "射程內不起鉤、英雄不動");

            yield return WarpAndSettle(dummy.transform.position + Vector3.back * GrappleDistance);
            _hero.ClearCombatTargetInPlace();
            yield return WaitIdleAfterClear();
            yield return TapAttackAndWait(0.5f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f,
                "射程內那幾按不吃冷卻：9m 立刻鉤得到");
            AssertGrappleSelected();
        }

        [UnityTest] // M2：鉤到位後冷卻中，目標在射程內→按 ATK 退回普攻（打得到、不動）
        public IEnumerator G4b_Grapple_CooldownTargetInRange_FallsBackToBasicAttack()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            TapAttack();
            float t0 = Time.time;
            yield return WaitSeconds(0.4f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "前提：第一鉤抵達");
            yield return WaitSeconds(0.6f);
            _hero.ClearCombatTargetInPlace();
            yield return WaitSeconds(1.0f);
            Assert.IsNull(_hero.CurrentTarget, "前提：普攻目標已清掉");
            Assert.Less(Time.time, t0 + 3f, "前提：仍在 4s 冷卻內");
            Vector3 p0 = Flat(_hero.transform.position);
            float h0 = dummy.Health;
            TapAttack();
            float deadline = Time.time + 1.5f;
            while (dummy.Health >= h0 && Time.time < deadline) yield return null;
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "冷卻中、射程內：退回普攻命中");
            Assert.AreSame(dummy, _hero.CurrentTarget);
            Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "冷卻中不鉤、英雄不動");
            AssertGrappleSelected();
        }

        [UnityTest] // M2：冷卻中，目標在射程外（6.5m，錐內 10m 內）→原地不動、不追、不鎖定
        public IEnumerator G4c_Grapple_CooldownTargetOutOfRange_StaysStill()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            TapAttack();
            float t0 = Time.time;
            yield return WaitSeconds(0.4f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "前提：第一鉤抵達");
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 6.5f);
            _hero.ClearCombatTargetInPlace();
            yield return null; yield return null;
            Assert.IsFalse(_hero.IsTargetInAttackRange(dummy), "前提：6.5m 在普攻射程外");
            Vector3 p0 = Flat(_hero.transform.position);
            TapAttack();
            float until = Time.time + 1f;
            while (Time.time < until)
            {
                yield return null;
                Assert.LessOrEqual((Flat(_hero.transform.position) - p0).magnitude, 0.05f, "冷卻中、射程外：原地不動、不追");
                Assert.IsNull(_hero.CurrentTarget, "冷卻中、射程外：不鎖定");
            }
            Assert.Less(Time.time, t0 + 4f, "前提：全程在冷卻內");
            AssertGrappleSelected();
        }

        [UnityTest] // M3：剛鉤到位（約 2m）後冷卻結束再按→直接普攻，不得「拉 0 公尺」燒掉冷卻（隨後 9m 立刻鉤得到）
        public IEnumerator G4d_Grapple_JustArrived_AfterCooldown_NoZeroPullCooldownBurn()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            TapAttack();
            float t0 = Time.time;
            yield return WaitSeconds(0.4f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f, "前提：第一鉤抵達");
            while (Time.time < t0 + 4.1f) yield return null;
            Debug.Log("[CAMERA LAB TEST] G4d dist=" + FlatDistance(_hero.transform.position, dummy.transform.position).ToString("F6"));
            TapAttack();
            yield return WaitSeconds(0.3f);
            Assert.AreSame(dummy, _hero.CurrentTarget, "到位後再按：照打同一目標");
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * GrappleDistance);
            _hero.ClearCombatTargetInPlace();
            yield return WaitIdleAfterClear();
            yield return TapAttackAndWait(0.5f);
            Assert.AreEqual(2f, FlatDistance(_hero.transform.position, dummy.transform.position), 0.3f,
                "到位後那一按不吃冷卻：9m 立刻鉤得到");
            AssertGrappleSelected();
        }

        [UnityTest] // G8：鉤鎖一整輪（起鉤→拉→抵達→普攻）Update／LateUpdate 零配置（附正向對照）
        public IEnumerator G8_GrappleHookCycle_AllocatesNothing_PositiveControlCatchesAllocation()
        {
            yield return SetupGrapple(GrappleDistance);
            DummyTarget dummy = _grappleDummy;
            dummy.Configure(100000f, dummy.TargetFaction);
            Vector3 home = dummy.transform.position + Vector3.back * GrappleDistance;
            // 暖機：完整鉤一次並打到。
            TapAttack();
            float t0 = Time.time;
            yield return WaitSeconds(1.0f);
            yield return WarpAndSettle(home);
            _hero.ClearCombatTargetInPlace();
            while (Time.time < t0 + 4.1f) yield return null;

            Vector3 p0 = Flat(_hero.transform.position);
            float h0 = dummy.Health;
            GameObject driverObject = new GameObject("GrappleButtonDriver");
            GrappleButtonDriver driver = driverObject.AddComponent<GrappleButtonDriver>();
            driver.Lab = _lab;
            driver.Input = _input;
            AllocationProbe.Reset();
            GameObject rig = new GameObject("GrappleProbeRig");
            rig.AddComponent<AllocationProbeBegin>();
            rig.AddComponent<AllocationProbeEnd>();
            yield return null;
            AllocationProbe.Measuring = true;
            driver.Armed = true;
            for (int i = 0; i < 90; i++) yield return null;
            AllocationProbe.Measuring = false;
            long updateBytes = AllocationProbe.UpdateBytes, lateBytes = AllocationProbe.LateUpdateBytes;
            int frames = AllocationProbe.Frames;
            float moved = (Flat(_hero.transform.position) - p0).magnitude;
            float h1 = dummy.Health;

            GameObject allocator = new GameObject("DeliberateAllocator");
            allocator.AddComponent<DeliberateAllocator>();
            AllocationProbe.Reset();
            yield return null;
            AllocationProbe.Measuring = true;
            for (int i = 0; i < 30; i++) yield return null;
            AllocationProbe.Measuring = false;
            long controlBytes = AllocationProbe.UpdateBytes;
            Object.Destroy(allocator);
            Object.Destroy(rig);
            Object.Destroy(driverObject);

            Assert.GreaterOrEqual(frames, 90);
            Assert.AreEqual(1, driver.Taps, "窗口內按了一次 ATK");
            Assert.Greater(moved, 6f, "窗口內沒有鉤鎖位移，0 byte 沒有鑑別力");
            Assert.Less(h1, h0, "窗口內沒有抵達後的普攻命中，0 byte 沒有鑑別力");
            Assert.Greater(controlBytes, 0L, "正向對照量不到配置：探針失效");
            Assert.AreEqual(0L, updateBytes, "鉤鎖路徑 Update 在 " + frames + " 幀內配置了 " + updateBytes + " bytes");
            Assert.AreEqual(0L, lateBytes, "鉤鎖路徑 LateUpdate 在 " + frames + " 幀內配置了 " + lateBytes + " bytes");
            AssertGrappleSelected();
        }
    }

    // G8：在 Update 夾區內按一次 ATK（鉤鎖起鉤路徑也在夾區內）。
    public sealed class GrappleButtonDriver : MonoBehaviour
    {
        internal PlayerInputService Input;
        internal CameraComparisonLab Lab;
        public bool Armed;
        public int Taps;

        private void Update()
        {
            if (Input == null || !Armed || Taps > 0) return;
            ScreenRegion a = Lab.ActionButtonLayout.Attack;
            Input.SendScreenTap((a.XMin + a.XMax) * .5f, (a.YMin + a.YMax) * .5f);
            Taps++;
        }
    }
}
#endif
