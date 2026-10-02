#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
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

        [UnityTest] // G5(a)：路徑上有一般實體障礙（不擋視線、擋身體）→鉤得到但停在牆前，不穿牆
        public IEnumerator G5a_Grapple_PathBlockedBySolid_StopsBeforeWall()
        {
            yield return SetupGrapple(GrappleDistance);
            Vector3 h = _hero.transform.position;
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                block.transform.position = new Vector3(h.x, h.y + 1.5f, h.z + 4.5f);
                block.transform.localScale = new Vector3(4f, 3f, 0.4f);
                Physics.SyncTransforms();
                yield return null; yield return null;
                float wallFace = block.transform.position.z - 0.2f;
                float z0 = _hero.transform.position.z;
                yield return TapAttackAndWait(0.5f);
                float z1 = _hero.transform.position.z;
                Debug.Log("[CAMERA LAB TEST] G5a z0=" + z0.ToString("F3") + " z1=" + z1.ToString("F3") + " face=" + wallFace.ToString("F3"));
                Assert.Greater(z1 - z0, 1f, "前提：鉤得到並往前拉（一般障礙不擋視線）");
                Assert.Less(z1, wallFace, "停在牆前，不穿牆");
                yield return WaitSeconds(0.5f);
                Assert.Less(_hero.transform.position.z, wallFace, "之後也沒穿過去");
                AssertGrappleSelected();
            }
            finally { Object.Destroy(block); }
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
