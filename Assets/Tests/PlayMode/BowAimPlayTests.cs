#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓「按住拖曳瞄準」（手勢操作第一批）凍結驗收 B3～B9 的真場景部分。凍結檔：vow-toolchain/acceptance-bowaim-20261003.md。
    // 全部走真路由：ATK 用模擬按住手指（Began→MoveSimulatedHold 拖曳→每幀 Stationary→Ended／Canceled），DASH 用 SendScreenTap。
    // 位移換算用 PlayerInputService.PixelsPerMillimeter（與塑牆同一把尺：GestureMath.MillimetersToPixels(Screen.dpi)）。
    // 只引用基底 ab45498 也有的成員，讓同一份測試在基底編得過、紅在行為斷言（B11）；預覽方向讀實際繪製的 LineRenderer。
    public sealed partial class CameraLabCombatPlayTests
    {
        private const float AimTestDragFor30Degrees = 11.75f;   // (11.75 − 2) / 13 × 40 = 30°
        private const float AimTestDragFull = 15f;               // 拉滿 40°
        private const float AimTestYawTolerance = 1f;

        private void DragAttackMillimetres(float dxMillimetres)
        {
            Vector2 c = Center(_lab.ActionButtonLayout.Attack);
            _input.MoveSimulatedHold(0, c.x + dxMillimetres * _input.PixelsPerMillimeter, c.y);
        }

        // 預覽錐實際畫出的方向：LineRenderer 第 0 點＝頂點、弧中點（32 段的第 16 段＝index 17）＝錐軸。回傳 [0,360)。
        private float PreviewYawDegrees()
        {
            GameObject indicator = ReadProperty(_lab, "AimPreviewIndicator") as GameObject;
            Assert.IsNotNull(indicator, "要有預覽 indicator");
            LineRenderer line = indicator.GetComponent<LineRenderer>();
            Vector3 apex = line.GetPosition(0);
            Vector3 mid = line.GetPosition(line.positionCount / 2);
            return Mathf.Repeat(Mathf.Atan2(mid.x - apex.x, mid.z - apex.z) * Mathf.Rad2Deg, 360f);
        }

        private static float YawDiff(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b));

        // 鏡頭朝 +Z（yaw 0），場景木樁在英雄前方 6m、偏右（+x）30°；選好弓。
        private IEnumerator SetupBowThirtyRight()
        {
            yield return Load();
            _bowDummy = Object.FindObjectOfType<DummyTarget>();
            _bowDummy.Configure(1000f, _bowDummy.TargetFaction);
            float r = 30f * Mathf.Deg2Rad;
            yield return WarpAndSettle(_bowDummy.transform.position - new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r)) * 6f);
            Vector3 d = _bowDummy.transform.position - _hero.transform.position;
            float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Debug.Log("[BOWAIM TEST] setup bearing=" + bearing.ToString("F3") + " dist=" + new Vector2(d.x, d.z).magnitude.ToString("F3"));
            Assert.AreEqual(30f, bearing, 0.5f, "場景前提：木樁在偏右 30°");
            Assert.AreEqual(0f, _lab.YawDegrees, "場景前提：鏡頭朝 +Z");
            SelectWeaponByTaps(2);
            AssertBowSelected();
            yield return null; yield return null;
        }

        // 按下→下一幀拖 dx（mm）→按滿 seconds（觸控時鐘）放開。
        private IEnumerator HoldAttackWithDrag(float dxMillimetres, double seconds)
        {
            double t0 = Time.unscaledTimeAsDouble;
            PressAttack();
            yield return null;
            if (dxMillimetres != 0f) DragAttackMillimetres(dxMillimetres);
            while (Time.unscaledTimeAsDouble - t0 < seconds) yield return null;
            ReleaseAttack();
        }

        [UnityTest] // B3(a)(b)：不拖按住 1.0s→偏右 30° 木樁不受傷；拖到 offset +30° 按住 1.0s→受傷 108
        public IEnumerator B3ab_Bow_DragRightThirty_FullChargeHitsDummyOutsideCrosshairCone()
        {
            yield return SetupBowThirtyRight();
            DummyTarget dummy = _bowDummy;
            float h0 = dummy.Health;
            yield return HoldAttackWithDrag(0f, 1.0);
            yield return WaitSeconds(1.5f);
            Assert.AreEqual(h0, dummy.Health, "B3(a) 不拖：出手朝準星（0°），30° 的木樁不受傷");
            Assert.AreEqual(0f, _lab.YawDegrees, 1e-3f, "B3(a) 不拖：鏡頭不動");

            float h1 = dummy.Health;
            yield return HoldAttackWithDrag(AimTestDragFor30Degrees, 1.0);
            yield return WaitForDrop(dummy, h1, 2f);
            Debug.Log("[BOWAIM TEST] B3b hit=" + (h1 - dummy.Health).ToString("F4") + " yaw=" + _lab.YawDegrees.ToString("F3"));
            Assert.AreEqual(h1 - _hero.AttackDamage * BowChargedDamageMultiplier, dummy.Health, 1e-3f,
                "B3(b) 拖 offset +30° 按住 1.0s：出手朝 aimYaw，木樁受傷 60×1.8");
        }

        [UnityTest] // B3(c)：拖反向（offset −30°）按住 1.0s 放開→偏右 30° 的木樁不受傷
        public IEnumerator B3c_Bow_DragLeftThirty_DummyOnRightNotHit()
        {
            yield return SetupBowThirtyRight();
            DummyTarget dummy = _bowDummy;
            float h0 = dummy.Health;
            yield return HoldAttackWithDrag(-AimTestDragFor30Degrees, 1.0);
            yield return WaitSeconds(1.5f);
            Debug.Log("[BOWAIM TEST] B3c dealt=" + (h0 - dummy.Health).ToString("F3") + " yaw=" + _lab.YawDegrees.ToString("F3"));
            Assert.AreEqual(h0, dummy.Health, "B3(c) 拖反向（−30°）：右邊 30° 的木樁不受傷");
            Assert.AreEqual(330f, _lab.YawDegrees, AimTestYawTolerance, "B3(c) 鏡頭跟到 −30°（＝330°）");
        }

        [UnityTest] // B4：拖到 offset +30° 後 0.15s 內放開（快速射擊）→偏右 30° 的木樁受傷 60
        public IEnumerator B4_Bow_QuickShotAfterDrag_UsesAimYaw()
        {
            yield return SetupBowThirtyRight();
            DummyTarget dummy = _bowDummy;
            float h0 = dummy.Health;
            double t0 = Time.unscaledTimeAsDouble;
            PressAttack();
            yield return null;
            DragAttackMillimetres(AimTestDragFor30Degrees);
            yield return null;
            double held = Time.unscaledTimeAsDouble - t0;
            ReleaseAttack();
            Assert.Less(held, 0.15, "測試前提：0.15s 內放開（快速射擊）");
            yield return WaitForDrop(dummy, h0, 2f);
            Debug.Log("[BOWAIM TEST] B4 held=" + held.ToString("F3") + " hit=" + (h0 - dummy.Health).ToString("F4"));
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "B4 快速射擊也朝 aimYaw：木樁受傷 60（p=0）");
        }

        [UnityTest] // B5：拖到 +40° 按住→鏡頭每幀 ≤ 90°/s×dt、0.6s 內到 +40°（±1°）；放開後 0.5s 不回彈
        public IEnumerator B5_Bow_CameraTracksAimYaw_SpeedLimited_NoSnapBackAfterRelease()
        {
            yield return Load();
            SelectWeaponByTaps(2);
            AssertBowSelected();
            yield return null;
            float pressYaw = _lab.YawDegrees;
            PressAttack();
            yield return null;
            DragAttackMillimetres(AimTestDragFull);
            double t0 = Time.unscaledTimeAsDouble;
            float prevYaw = _lab.YawDegrees;
            float prevDt = Time.unscaledDeltaTime;
            float maxRate = 0f;
            int frames = 0;
            bool reached = false;
            while (Time.unscaledTimeAsDouble - t0 < 0.6)
            {
                yield return null;
                float yaw = _lab.YawDegrees;
                float step = YawDiff(prevYaw, yaw);
                // 這次讀到的變化＝上一幀 LateUpdate 用上一幀的 dt 轉的
                Assert.LessOrEqual(step, 90f * prevDt + 0.01f, "第 " + frames + " 幀：鏡頭轉速不得超過 90°/s（step=" + step + " dt=" + prevDt + "）");
                if (prevDt > 0f) maxRate = Mathf.Max(maxRate, step / prevDt);
                prevYaw = yaw;
                prevDt = Time.unscaledDeltaTime;
                frames++;
                if (YawDiff(yaw, pressYaw + 40f) <= AimTestYawTolerance) { reached = true; break; }
            }
            double reachedAt = Time.unscaledTimeAsDouble - t0;
            Debug.Log("[BOWAIM TEST] B5 reached=" + reached + " at=" + reachedAt.ToString("F3") + " frames=" + frames
                + " maxRate=" + maxRate.ToString("F2") + " yaw=" + _lab.YawDegrees.ToString("F3"));
            Assert.IsTrue(reached, "0.6s 內鏡頭要追到 pressYaw+40°（±1°），實際 " + _lab.YawDegrees);
            Assert.Greater(frames, 2, "不得瞬間跳轉：要分多幀追上");
            while (YawDiff(_lab.YawDegrees, pressYaw + 40f) > 1e-3f && Time.unscaledTimeAsDouble - t0 < 1.0) yield return null;
            ReleaseAttack();
            yield return null;
            float released = _lab.YawDegrees;
            double r0 = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - r0 < 0.5)
            {
                yield return null;
                Assert.AreEqual(released, _lab.YawDegrees, 1e-3f, "放開後鏡頭停在當下，不回彈");
            }
            Assert.AreEqual(pressYaw + 40f, _lab.YawDegrees, AimTestYawTolerance, "放開後仍在 +40°");
        }

        [UnityTest] // B6：拖曳中預覽錐方向＝aimYaw（±1°），鏡頭還沒追上時就已指向 aimYaw
        public IEnumerator B6_Bow_PreviewConePointsAtAimYaw_BeforeCameraCatchesUp()
        {
            yield return Load();
            SelectWeaponByTaps(2);
            AssertBowSelected();
            yield return null;
            float pressYaw = _lab.YawDegrees;
            PressAttack();
            yield return null;
            Assert.AreEqual("Cone", PreviewKind(), "弓按住：Cone 預覽");
            Assert.AreEqual(pressYaw, PreviewYawDegrees(), AimTestYawTolerance, "還沒拖：預覽朝 pressYaw");
            DragAttackMillimetres(AimTestDragFull);
            yield return null;
            float camera = _lab.YawDegrees;
            float preview = PreviewYawDegrees();
            Debug.Log("[BOWAIM TEST] B6 +40 preview=" + preview.ToString("F3") + " camera=" + camera.ToString("F3"));
            Assert.Greater(YawDiff(camera, pressYaw + 40f), AimTestYawTolerance, "測試前提：鏡頭還沒追上");
            Assert.AreEqual(0f, YawDiff(preview, pressYaw + 40f), AimTestYawTolerance, "預覽錐已指向 aimYaw＝pressYaw+40°（跟手指不跟鏡頭）");

            DragAttackMillimetres(-AimTestDragFull);
            yield return null;
            camera = _lab.YawDegrees;
            preview = PreviewYawDegrees();
            Debug.Log("[BOWAIM TEST] B6 -40 preview=" + preview.ToString("F3") + " camera=" + camera.ToString("F3"));
            Assert.Greater(YawDiff(camera, pressYaw - 40f), AimTestYawTolerance, "測試前提：鏡頭還沒追上");
            Assert.AreEqual(0f, YawDiff(preview, pressYaw - 40f), AimTestYawTolerance, "反向拖：預覽立即改指 pressYaw−40°");
            Assert.IsTrue(PreviewIndicatorActive(), "拖曳中 indicator 持續顯示");
            ReleaseAttack();
        }

        [UnityTest] // B7：拖曳＋蓄力中 DASH→不出手、預覽消失、鏡頭停在當下；再按 ATK 的 pressYaw＝新的當下 yaw（offset 歸零）
        public IEnumerator B7_Bow_DragThenDashCancel_NoShot_CameraStays_NextPressStartsFresh()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            int shots0 = BowShots();
            float h0 = dummy.Health;
            PressAttack();
            yield return null;
            DragAttackMillimetres(AimTestDragFull);
            yield return WaitUnscaled(0.3);
            TapDash();
            yield return null;
            float canceledYaw = _lab.YawDegrees;
            Debug.Log("[BOWAIM TEST] B7 canceledYaw=" + canceledYaw.ToString("F3"));
            Assert.Greater(canceledYaw, 5f, "測試前提：取消前鏡頭已在追");
            Assert.Less(canceledYaw, 39f, "測試前提：取消前鏡頭還沒追到 +40°");
            Assert.AreEqual("None", PreviewKind(), "DASH 取消：預覽消失");
            Assert.IsFalse(PreviewIndicatorActive(), "DASH 取消：indicator 收起");
            double c0 = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - c0 < 0.5)
            {
                yield return null;
                if (Time.unscaledTimeAsDouble - c0 > 0.2) DragAttackMillimetres(-AimTestDragFull);   // 作廢後手指還在拖：不得再影響
                Assert.AreEqual(canceledYaw, _lab.YawDegrees, 1e-3f, "取消後鏡頭停在當下、不回彈、不再追");
            }
            ReleaseAttack();
            yield return WaitSeconds(1.0f);
            Assert.AreEqual(shots0, BowShots(), "取消後放開不出手：BowShotCount 不變");
            Assert.AreEqual(h0, dummy.Health, "取消後放開不出手：木樁不受傷");

            float freshYaw = _lab.YawDegrees;
            PressAttack();
            yield return null;
            yield return WaitUnscaled(0.4);
            float preview = PreviewYawDegrees();
            Debug.Log("[BOWAIM TEST] B7 fresh yaw=" + freshYaw.ToString("F3") + " preview=" + preview.ToString("F3") + " camera=" + _lab.YawDegrees.ToString("F3"));
            Assert.AreEqual(0f, YawDiff(preview, freshYaw), AimTestYawTolerance, "再按 ATK：預覽朝新的當下 yaw（offset 歸零、pressYaw 重取）");
            Assert.AreEqual(freshYaw, _lab.YawDegrees, 1e-3f, "再按 ATK 不拖：鏡頭不動（沒有殘留 offset）");
            ReleaseAttack();
        }

        [UnityTest] // B8：Standard／Sword／Hammer／Grapple 在 ATK 上拖 15mm→出手、鏡頭 yaw、預覽方向與不拖時完全相同
        public IEnumerator B8_OtherWeapons_DragOnAttack_SameAsNoDrag()
        {
            int[] weapons = { 0, 1, 3, 4 };
            string[] labels = { "Standard", "Sword", "Hammer", "Grapple" };
            float[] distances = { 3f, 3f, 3f, 9f };
            for (int w = 0; w < weapons.Length; w++)
            {
                float[] hit = new float[2];
                string[] picked = new string[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    string label = labels[w] + (pass == 0 ? " 不拖" : " 拖 15mm");
                    yield return Load();
                    DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
                    dummy.Configure(1000f, dummy.TargetFaction);
                    yield return WarpAndSettle(dummy.transform.position + Vector3.back * distances[w]);
                    SelectWeaponByTaps(weapons[w]);
                    Assert.AreEqual(weapons[w], (int)_lab.CurrentWeapon, label + " 選取");
                    yield return null; yield return null;
                    float h0 = dummy.Health;
                    double t0 = Time.unscaledTimeAsDouble;
                    PressAttack();
                    picked[pass] = _lab.LastAimTarget != null ? ((Component)_lab.LastAimTarget).name : "none";
                    yield return null;
                    if (pass == 1) DragAttackMillimetres(AimTestDragFull);
                    float firstHit = -1f;
                    while (Time.unscaledTimeAsDouble - t0 < 0.6)
                    {
                        yield return null;
                        if (firstHit < 0f && dummy.Health < h0) firstHit = h0 - dummy.Health;
                        Assert.AreEqual(0f, _lab.YawDegrees, 1e-3f, label + "：按住期間鏡頭 yaw 不變");
                        if (weapons[w] != 0) Assert.AreEqual(0f, YawDiff(PreviewYawDegrees(), 0f), AimTestYawTolerance, label + "：預覽方向＝鏡頭前方");
                    }
                    ReleaseAttack();
                    float until = Time.time + 2f;
                    while (firstHit < 0f && Time.time < until)
                    {
                        yield return null;
                        if (dummy.Health < h0) firstHit = h0 - dummy.Health;
                    }
                    hit[pass] = firstHit;   // 只量第一下（避免按住期間打幾下的時序差）
                    yield return WaitSeconds(0.3f);
                    Assert.AreEqual(0f, _lab.YawDegrees, 1e-3f, label + "：放開後鏡頭 yaw 不變");
                }
                Debug.Log("[BOWAIM TEST] B8 " + labels[w] + " noDrag=" + hit[0].ToString("F3") + "/" + picked[0]
                    + " drag=" + hit[1].ToString("F3") + "/" + picked[1]);
                Assert.Greater(hit[0], 0f, labels[w] + "：不拖要真的命中（對照組）");
                Assert.AreEqual(hit[0], hit[1], 1e-3f, labels[w] + "：拖曳與不拖的出手結果相同");
                Assert.AreEqual(picked[0], picked[1], labels[w] + "：拖曳與不拖挑到同一目標");
            }
        }

        // B9 事實查核（不是 B9 通過證據）：TOP（俯視）沒有 ATK 鈕——按在 ATK 位置不會有任何 ATK 事件，也不轉鏡頭。
        [UnityTest]
        public IEnumerator B9_FactCheck_TopModeHasNoAttackButton_DragDoesNotRotateCamera()
        {
            yield return Load();
            SelectWeaponByTaps(2);
            AssertBowSelected();
            _lab.SetThirdPerson(false);
            yield return null; yield return null;
            Assert.IsFalse(_lab.IsThirdPerson);
            Assert.IsFalse(_lab.ActionButtonsActive, "TOP：ATK／DASH／WPN 鈕不啟用");
            Quaternion rig0 = Camera.main.transform.rotation;
            int attacks0 = _lab.AimAttackCount, shots0 = BowShots();
            PressAttack();
            yield return null;
            DragAttackMillimetres(AimTestDragFull);
            yield return WaitUnscaled(0.6);
            Assert.AreEqual("None", PreviewKind(), "TOP：沒有按住預覽");
            ReleaseAttack();
            yield return null;
            Assert.AreEqual(attacks0, _lab.AimAttackCount, "TOP：沒有 ATK 出手");
            Assert.AreEqual(shots0, BowShots(), "TOP：沒有弓出手");
            Assert.Less(Quaternion.Angle(rig0, Camera.main.transform.rotation), 1e-3f, "TOP：鏡頭不轉");
        }
    }
}
#endif
