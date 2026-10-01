using System;
using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests
{
    // docs/CAMERA_LAB_COMBAT_PLAN.md §3 凍結驗收 A／B／C／E 的純邏輯部分。dotnet 與 Unity EditMode 共用。
    public sealed class CameraLabCombatTests
    {
        private const float Tick = BrainTestHarness.TickSeconds;

        // ───────────────────────── A 普攻鈕 ─────────────────────────

        [Test] // A1
        public void A1_AimForward_MatchesEulerPitchYaw()
        {
            float[] yaws = { 0f, 30f, 90f, 180f, 270f };
            float[] pitches = { 10f, 25f, 50f };
            foreach (float yaw in yaws)
            {
                CameraLabAim.GroundForward(yaw, out float gx, out float gz);
                double y = yaw * Math.PI / 180.0;
                Assert.AreEqual(Math.Sin(y), gx, 1e-5, "ground x yaw " + yaw);
                Assert.AreEqual(Math.Cos(y), gz, 1e-5, "ground z yaw " + yaw);
                Assert.AreEqual(1.0, Math.Sqrt(gx * gx + gz * gz), 1e-5);
                foreach (float pitch in pitches)
                {
                    CameraLabAim.ViewForward(yaw, pitch, out float vx, out float vy, out float vz);
                    double p = pitch * Math.PI / 180.0;
                    Assert.AreEqual(Math.Sin(y) * Math.Cos(p), vx, 1e-5);
                    Assert.AreEqual(-Math.Sin(p), vy, 1e-5);
                    Assert.AreEqual(Math.Cos(y) * Math.Cos(p), vz, 1e-5);
                }
            }
            CameraLabAim.GroundForward(90f, out float rx, out float rz);
            Assert.AreEqual(1f, rx, 1e-5f, "yaw 90 → +x");
            Assert.AreEqual(0f, rz, 1e-5f);
        }

        private static int Pick(float aimX, float aimZ, params float[] xz)
        {
            AimTargetPicker picker = default;
            picker.Begin(0f, 0f, aimX, aimZ, CameraLabAim.ConeHalfAngleDegrees, CameraLabAim.MaxAimDistance);
            for (int i = 0; i < xz.Length / 2; i++) picker.Consider(i, xz[i * 2], xz[i * 2 + 1]);
            return picker.BestIndex;
        }

        private static float[] Polar(float degreesFromZ, float distance)
        {
            double a = degreesFromZ * Math.PI / 180.0;
            return new[] { (float)(Math.Sin(a) * distance), (float)(Math.Cos(a) * distance) };
        }

        [Test] // A2
        public void A2_AimCone_SelectsBySmallestAngle_WithinThirtyDegreesAndEightMetres()
        {
            Assert.AreEqual(30f, CameraLabAim.ConeHalfAngleDegrees);
            Assert.AreEqual(8f, CameraLabAim.MaxAimDistance);
            Assert.AreEqual(0, Pick(0f, 1f, 0f, 3f), "正前方 3m");
            Assert.AreEqual(0, Pick(0f, 1f, Polar(29f, 4f)), "29° 在錐內");
            Assert.AreEqual(-1, Pick(0f, 1f, Polar(31f, 4f)), "31° 在錐外");
            Assert.AreEqual(0, Pick(0f, 1f, 0f, 7.99f), "7.99m 在範圍內");
            Assert.AreEqual(-1, Pick(0f, 1f, 0f, 8.01f), "8.01m 超出範圍");
            Assert.AreEqual(-1, Pick(0f, 1f, 0f, -3f), "正後方不選");
            Assert.AreEqual(-1, Pick(0f, 1f), "無候選");

            float[] near = Polar(20f, 2f), far = Polar(5f, 7f);
            Assert.AreEqual(1, Pick(0f, 1f, near[0], near[1], far[0], far[1]), "夾角小者勝，即使較遠");

            float[] a = Polar(10f, 6f), b = Polar(10f, 3f);
            Assert.AreEqual(1, Pick(0f, 1f, a[0], a[1], b[0], b[1]), "夾角相同取較近");
            Assert.AreEqual(0, Pick(0f, 1f, b[0], b[1], a[0], a[1]), "順序不影響（較近者為 index 0）");

            float[] side = Polar(90f, 3f);
            Assert.AreEqual(0, Pick(1f, 0f, side[0], side[1], 0f, 3f), "aim 改 +x 時改選 +x 方向目標");
        }

        private static int Resolve(float aimX, float aimZ, params float[] xz)
        {
            AimTargetPicker picker = default;
            picker.Begin(0f, 0f, aimX, aimZ, CameraLabAim.ConeHalfAngleDegrees, CameraLabAim.MaxAimDistance);
            for (int i = 0; i < xz.Length / 2; i++) picker.Consider(i, xz[i * 2], xz[i * 2 + 1]);
            return picker.ResolvedIndex;
        }

        [Test] // F1：錐內無目標時退回 8m 內最近者
        public void A2F_AimFallback_NearestWithinEightMetres_WhenConeEmpty()
        {
            Assert.AreEqual(0, Resolve(0f, 1f, 0f, -3f), "正後方 3m：錐內無目標→退回選它");
            Assert.AreEqual(0, Resolve(0f, 1f, Polar(31f, 4f)), "31° 錐外→退回選它");
            Assert.AreEqual(0, Resolve(0f, 1f, 0f, -7.99f), "背後 7.99m 仍在範圍內");
            Assert.AreEqual(-1, Resolve(0f, 1f, 0f, -8.01f), "背後 8.01m 超出範圍不選");
            Assert.AreEqual(-1, Resolve(0f, 1f), "無候選");

            float[] backFar = Polar(180f, 6f), sideNear = Polar(90f, 2f);
            Assert.AreEqual(1, Resolve(0f, 1f, backFar[0], backFar[1], sideNear[0], sideNear[1]), "錐外取最近");
            Assert.AreEqual(0, Resolve(0f, 1f, sideNear[0], sideNear[1], backFar[0], backFar[1]), "順序不影響");

            float[] coneFar = Polar(10f, 7f), backNear = Polar(180f, 1f);
            Assert.AreEqual(0, Resolve(0f, 1f, coneFar[0], coneFar[1], backNear[0], backNear[1]), "錐內有目標時錐內優先，即使錐外較近");

            float[] l = Polar(90f, 3f), r = Polar(-90f, 3f);
            Assert.AreEqual(0, Resolve(0f, 1f, l[0], l[1], r[0], r[1]), "距離相同取先 Consider 者");
        }

        // preferred 是 xz 陣列裡的第幾個目標（-1＝沒有）。
        private static int ResolveSticky(float aimX, float aimZ, int preferred, params float[] xz)
        {
            AimTargetPicker picker = default;
            picker.Begin(0f, 0f, aimX, aimZ, CameraLabAim.ConeHalfAngleDegrees, CameraLabAim.MaxAimDistance);
            for (int i = 0; i < xz.Length / 2; i++) picker.Consider(i, xz[i * 2], xz[i * 2 + 1], i == preferred);
            return picker.ResolvedIndex;
        }

        [Test] // G1：錐內黏性邊際 15°
        public void G1_Sticky_KeepsPreferredUnlessOtherIsMoreThanFifteenDegreesCloserToAim()
        {
            Assert.AreEqual(15f, AimTargetPicker.StickyMarginDegrees);
            float[] a = Polar(20f, 4f), b14 = Polar(6f, 4f), b16 = Polar(4f, 4f);
            Assert.AreEqual(0, ResolveSticky(0f, 1f, 0, a[0], a[1], b14[0], b14[1]), "他人只靠準星 14°→維持原目標");
            Assert.AreEqual(1, ResolveSticky(0f, 1f, 0, a[0], a[1], b16[0], b16[1]), "他人靠準星 16°→換目標");
            Assert.AreEqual(1, ResolveSticky(0f, 1f, 1, a[0], a[1], b14[0], b14[1]), "preferred 本來就是夾角最小者→不變");
        }

        [Test] // G2：preferred 在錐外、錐內另有目標→瞄準覆寫黏性
        public void G2_Sticky_PreferredOutsideCone_ConeCandidateWins()
        {
            float[] outside = Polar(60f, 3f), inside = Polar(25f, 6f);
            Assert.AreEqual(1, ResolveSticky(0f, 1f, 0, outside[0], outside[1], inside[0], inside[1]));
        }

        [Test] // G3：錐內無目標時 preferred 在 8m 內就留著；超出 8m 回到最近者
        public void G3_Sticky_ConeEmpty_KeepsPreferredWithinEightMetres_ElseNearest()
        {
            float[] far = Polar(150f, 7f), near = Polar(120f, 1.5f), tooFar = Polar(150f, 8.5f);
            Assert.AreEqual(0, ResolveSticky(0f, 1f, 0, far[0], far[1], near[0], near[1]), "preferred 7m、他人 1.5m→不改打較近者");
            Assert.AreEqual(1, ResolveSticky(0f, 1f, 0, tooFar[0], tooFar[1], near[0], near[1]), "preferred 8.5m 已超出→改打最近者");
            Assert.AreEqual(-1, ResolveSticky(0f, 1f, 0, tooFar[0], tooFar[1]), "只有超出的 preferred→不出手");
        }

        [Test] // G4：不傳 preferred（或沒有 preferred）→與 A2／A2F 同結果
        public void G4_NoPreferred_BehavesExactlyLikeNearestFallbackPicker()
        {
            float[] a = Polar(10f, 6f), b = Polar(10f, 3f), c = Polar(150f, 2f);
            Assert.AreEqual(Resolve(0f, 1f, a[0], a[1], b[0], b[1]), ResolveSticky(0f, 1f, -1, a[0], a[1], b[0], b[1]));
            Assert.AreEqual(Resolve(0f, 1f, c[0], c[1]), ResolveSticky(0f, 1f, -1, c[0], c[1]));
            Assert.AreEqual(-1, ResolveSticky(0f, 1f, -1));
        }

        // ───────────────────────── B 主動滑步 ─────────────────────────

        [Test] // B1
        public void B1_TuningLiterals_MatchGdd()
        {
            CombatTuning t = new CombatTuning();
            Assert.AreEqual(3, t.MaxCharges);
            Assert.AreEqual(2.5f, t.ChargeRecoverySeconds);
            Assert.AreEqual(1.0f, t.ChainWindowSeconds);
            Assert.AreEqual(new[] { 1.4f, 0.9f, 0.5f }, t.DashDistances);
            Assert.AreEqual(0.22f, t.CadenceWindowSeconds);
        }

        private static ActiveDashOutcome Dash(BrainTestHarness h, float x = 0f, float z = 1f)
        {
            return ActiveDashLogic.Execute(h.Brain, h.Body, h.Body.Sim.Charges, x, z, new GroundPoint(0f, 0f, 0f));
        }

        private static void FinishDash(BrainTestHarness h)
        {
            int guard = 0;
            while (h.Body.Sim.DashActive && guard++ < 1000) h.Step();
            Assert.IsFalse(h.Body.Sim.DashActive);
        }

        [Test] // B2
        public void B2_ActiveDashChain_Decays_1_4_0_9_0_5_WithinOneSecond()
        {
            BrainTestHarness h = new BrainTestHarness();
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(ActiveDashOutcome.FreeDash, Dash(h));
                Assert.AreEqual(PlayerState.Idle, h.State, "自由滑步不改大腦狀態");
                h.Advance(0.2);
                Assert.IsFalse(h.Body.Sim.DashActive);
            }
            Assert.AreEqual(3, h.Body.DashDistances.Count);
            Assert.AreEqual(1.4f, h.Body.DashDistances[0], 1e-4f);
            Assert.AreEqual(0.9f, h.Body.DashDistances[1], 1e-4f);
            Assert.AreEqual(0.5f, h.Body.DashDistances[2], 1e-4f);
            Assert.AreEqual(0, h.Body.Sim.Charges);
        }

        [Test] // B2：1.0s 窗口邊界
        public void B2_ChainWindow_IsOneSecondFromLastDash()
        {
            BrainTestHarness inside = new BrainTestHarness();
            Dash(inside);
            inside.Advance(0.95);
            Dash(inside);
            Assert.AreEqual(0.9f, inside.Body.DashDistances[1], 1e-4f, "0.95s 內：衰減");

            BrainTestHarness outside = new BrainTestHarness();
            Dash(outside);
            outside.Advance(1.05);
            Dash(outside);
            Assert.AreEqual(1.4f, outside.Body.DashDistances[1], 1e-4f, "超過 1.0s：回到 1.4");
        }

        private static void AssertSimEqual(CadenceSimState expected, CadenceSimState actual, string label)
        {
            Assert.AreEqual(expected.Charges, actual.Charges, label);
            Assert.AreEqual(expected.RecoveryTimer, actual.RecoveryTimer, label);
            Assert.AreEqual(expected.ChainCount, actual.ChainCount, label);
            Assert.AreEqual(expected.SinceLastDash, actual.SinceLastDash, label);
            Assert.AreEqual(expected.DashActive, actual.DashActive, label);
        }

        private static BrainTestHarness ZeroCharge()
        {
            BrainTestHarness h = new BrainTestHarness();
            h.Tuning.ChargeRecoverySeconds = 1000f;
            h.Body.Sim.Charges = 0;
            return h;
        }

        [Test] // B3
        public void B3_ZeroCharges_IsNoCharge_AndTouchesNothing_InEveryAcceptingState()
        {
            PlayerState[] states = { PlayerState.Idle, PlayerState.Moving, PlayerState.AttackWindup, PlayerState.AttackRelease };
            foreach (PlayerState state in states)
            {
                BrainTestHarness h = ZeroCharge();
                FakeTarget target = new FakeTarget();
                if (state == PlayerState.Moving) h.Brain.CommandMove(new GroundPoint(5f, 0f, 5f));
                if (state == PlayerState.AttackWindup || state == PlayerState.AttackRelease) h.Brain.CommandAttack(target);
                if (state == PlayerState.AttackRelease) Assert.IsTrue(h.AdvanceUntil(PlayerState.AttackRelease, 0.5));
                Assert.AreEqual(state, h.State, "前置");

                CadenceSimState before = h.Body.Sim;
                object targetBefore = h.Brain.CurrentTarget;
                int moves = h.Body.MoveToCount;
                Assert.AreEqual(ActiveDashOutcome.NoCharge, Dash(h), state.ToString());
                Assert.AreEqual(state, h.State, "狀態不得改變：" + state);
                Assert.AreSame(targetBefore, h.Brain.CurrentTarget, "目標不得改變：" + state);
                Assert.AreEqual(moves, h.Body.MoveToCount, "不得送移動指令（前搖不得被打斷）：" + state);
                Assert.AreEqual(0, h.Body.DashDistances.Count);
                AssertSimEqual(before, h.Body.Sim, state.ToString());
            }
        }

        [Test] // B4
        public void B4_ActiveAndHitLinkedDash_ShareChargesAndChain_OnePressOneCharge()
        {
            BrainTestHarness h = new BrainTestHarness();
            FakeTarget target = new FakeTarget();
            h.Brain.CommandAttack(target);
            Assert.IsTrue(h.AdvanceUntil(PlayerState.AttackRelease, 0.5));

            Assert.AreEqual(ActiveDashOutcome.CadenceFlick, Dash(h), "目押窗口內＝命中連動");
            Assert.AreEqual(PlayerState.CadenceDashing, h.State);
            Assert.AreEqual(2, h.Body.Sim.Charges, "一次按壓只扣一格");
            Assert.AreEqual(1, h.Body.DashDistances.Count);
            Assert.AreSame(target, h.Brain.CurrentTarget, "命中連動保留目標");

            FinishDash(h);
            h.Step();
            Assert.AreNotEqual(PlayerState.CadenceDashing, h.State);
            Assert.AreEqual(PlayerState.Idle, h.State, "滑步結束後等攻擊週期＝Idle（窗口外）");
            Assert.AreEqual(ActiveDashOutcome.FreeDash, Dash(h), "窗口外自由滑步");
            Assert.AreEqual(1, h.Body.Sim.Charges);
            Assert.AreEqual(0.9f, h.Body.DashDistances[1], 1e-4f, "與命中連動共用連段");

            Assert.IsTrue(h.AdvanceUntil(PlayerState.AttackRelease, 1.0));
            h.Brain.CommandFlick(0f, -1f);
            Assert.AreEqual(PlayerState.CadenceDashing, h.State);
            Assert.AreEqual(3, h.Body.DashDistances.Count);
            Assert.AreEqual(0.5f, h.Body.DashDistances[2], 1e-4f, "命中連動接續主動滑步的連段");
            Assert.AreEqual(0, h.Body.Sim.Charges);
        }

        [Test] // B5
        public void B5_Direction_StickFirstRotatedByYaw_ElseCameraForward()
        {
            AssertDir(1f, 0f, 0f, 1f, 0f);
            AssertDir(0f, 1f, 90f, 1f, 0f);
            AssertDir(0.5f, 0f, 180f, -1f, 0f);
            AssertDir(0.19f, 0f, 0f, 0f, 1f);       // 死區內＝鏡頭前方
            AssertDir(0f, 0f, 90f, 1f, 0f);
            AssertDir(0f, 0f, 180f, 0f, -1f);
            Assert.AreEqual(0.2f, ActiveDashLogic.StickDeadzone);
        }

        private static void AssertDir(float sx, float sy, float yaw, float ex, float ez)
        {
            ActiveDashLogic.ResolveDirection(sx, sy, yaw, out float x, out float z);
            Assert.AreEqual(ex, x, 1e-5f, "x stick(" + sx + "," + sy + ") yaw " + yaw);
            Assert.AreEqual(ez, z, 1e-5f, "z stick(" + sx + "," + sy + ") yaw " + yaw);
        }

        [Test] // B6
        public void B6_RouteTable_AndWindupIsInterruptedWithoutHit()
        {
            Assert.AreEqual(ActiveDashOutcome.FreeDash, ActiveDashLogic.Route(PlayerState.Idle));
            Assert.AreEqual(ActiveDashOutcome.FreeDash, ActiveDashLogic.Route(PlayerState.Moving));
            Assert.AreEqual(ActiveDashOutcome.FreeDash, ActiveDashLogic.Route(PlayerState.AttackWindup));
            Assert.AreEqual(ActiveDashOutcome.CadenceFlick, ActiveDashLogic.Route(PlayerState.AttackRelease));
            Assert.AreEqual(ActiveDashOutcome.Rejected, ActiveDashLogic.Route(PlayerState.AttackRecovery));
            Assert.AreEqual(ActiveDashOutcome.Rejected, ActiveDashLogic.Route(PlayerState.CadenceDashing));
            Assert.AreEqual(ActiveDashOutcome.Rejected, ActiveDashLogic.Route(PlayerState.CastingRune));

            BrainTestHarness moving = new BrainTestHarness();
            moving.Brain.CommandMove(new GroundPoint(5f, 0f, 5f));
            Assert.AreEqual(ActiveDashOutcome.FreeDash, Dash(moving));
            Assert.AreEqual(PlayerState.Moving, moving.State);

            BrainTestHarness windup = new BrainTestHarness();
            FakeTarget target = new FakeTarget();
            windup.Brain.CommandAttack(target);
            Assert.AreEqual(PlayerState.AttackWindup, windup.State);
            Assert.AreEqual(ActiveDashOutcome.FreeDash, Dash(windup));
            Assert.AreEqual(PlayerState.Moving, windup.State, "前搖被原地移動指令打斷");
            Assert.IsNull(windup.Brain.CurrentTarget);
            Assert.AreEqual(2, windup.Body.Sim.Charges);
            windup.Advance(0.6);
            Assert.AreEqual(0, target.HitsTaken, "被打斷的前搖不得命中");

            BrainTestHarness recovery = new BrainTestHarness();
            recovery.Brain.CommandAttack(new FakeTarget());
            Assert.IsTrue(recovery.AdvanceUntil(PlayerState.AttackRecovery, 1.0));
            Assert.AreEqual(ActiveDashOutcome.Rejected, Dash(recovery));
            Assert.AreEqual(3, recovery.Body.Sim.Charges);
            Assert.AreEqual(PlayerState.AttackRecovery, recovery.State);

            BrainTestHarness dashing = new BrainTestHarness();
            dashing.Brain.CommandAttack(new FakeTarget());
            Assert.IsTrue(dashing.AdvanceUntil(PlayerState.AttackRelease, 0.5));
            dashing.Brain.CommandFlick(1f, 0f);
            Assert.AreEqual(PlayerState.CadenceDashing, dashing.State);
            Assert.AreEqual(ActiveDashOutcome.Rejected, Dash(dashing));
            Assert.AreEqual(2, dashing.Body.Sim.Charges);
            Assert.AreEqual(1, dashing.Body.DashDistances.Count);

            BrainTestHarness zeroDir = new BrainTestHarness();
            Assert.AreEqual(ActiveDashOutcome.Rejected, Dash(zeroDir, 0f, 0f));
            Assert.AreEqual(3, zeroDir.Body.Sim.Charges);
        }

        // ───────────────────────── C 塑牆 ─────────────────────────

        private static RuneWallPlacement ThirdPlacement(float yaw, float sx, float sz, float distance01)
        {
            RuneCastLogic logic = new RuneCastLogic(new RuneTuning());
            CameraLabAim.GroundForward(yaw, out float ax, out float az);
            CameraLabAim.WallDragDirection(true, ax, az, sx, sz, out float x, out float z);
            Assert.IsTrue(logic.TryDragPlacement(2f, -1f, x, z, distance01, out RuneWallPlacement p));
            return p;
        }

        [Test] // C1
        public void C1_ThirdPerson_WallUsesDragAmountOnly_FacingCameraForward()
        {
            RuneWallPlacement full = ThirdPlacement(90f, 0f, 1f, 1f);
            Assert.AreEqual(10f, full.CenterX, 1e-4f);
            Assert.AreEqual(-1f, full.CenterZ, 1e-4f);
            Assert.AreEqual(1f, full.NormalX, 1e-4f);
            Assert.AreEqual(0f, full.NormalZ, 1e-4f);

            RuneWallPlacement near = ThirdPlacement(90f, 0f, 1f, 0.2f);
            Assert.AreEqual(3.2f, near.CenterX, 1e-4f, "貼身帶＝1.2m");
            RuneWallPlacement mid = ThirdPlacement(90f, 0f, 1f, 2f / 3f);
            Assert.AreEqual(6.6f, mid.CenterX, 1e-4f, "2/3＝4.6m");

            RuneWallPlacement other = ThirdPlacement(90f, -0.6f, -0.8f, 1f);
            Assert.AreEqual(full.CenterX, other.CenterX);
            Assert.AreEqual(full.CenterZ, other.CenterZ);
            Assert.AreEqual(full.NormalX, other.NormalX);
            Assert.AreEqual(full.NormalZ, other.NormalZ);
        }

        [Test] // C2
        public void C2_TopDown_WallDirectionIsBitIdentical_AndPlacementUnchanged()
        {
            RuneCastLogic logic = new RuneCastLogic(new RuneTuning());
            float[] dirs = { 1f, 0f, -0.6f, 0.8f, 0.3f, -0.2f, 0f, -1f, 0.7071068f, 0.7071068f, 2.5f, -3.5f };
            float[] yaws = { 0f, 45f, 90f, 200f };
            foreach (float yaw in yaws)
            {
                for (int i = 0; i < dirs.Length; i += 2)
                {
                    CameraLabAim.GroundForward(yaw, out float ax, out float az);
                    CameraLabAim.WallDragDirection(false, ax, az, dirs[i], dirs[i + 1], out float x, out float z);
                    Assert.AreEqual(BitConverter.SingleToInt32Bits(dirs[i]), BitConverter.SingleToInt32Bits(x));
                    Assert.AreEqual(BitConverter.SingleToInt32Bits(dirs[i + 1]), BitConverter.SingleToInt32Bits(z));
                    foreach (float d in new[] { 0f, 0.2f, 0.5f, 1f })
                    {
                        logic.TryDragPlacement(1f, 2f, dirs[i], dirs[i + 1], d, out RuneWallPlacement direct);
                        logic.TryDragPlacement(1f, 2f, x, z, d, out RuneWallPlacement routed);
                        Assert.AreEqual(direct.CenterX, routed.CenterX);
                        Assert.AreEqual(direct.CenterZ, routed.CenterZ);
                        Assert.AreEqual(direct.NormalX, routed.NormalX);
                        Assert.AreEqual(direct.NormalZ, routed.NormalZ);
                    }
                }
            }
        }

        // ───────────────────────── 路由替身 ─────────────────────────

        private sealed class Sink : ITouchGestureSink, IActionButtonSink
        {
            public int Taps, Flicks, Ui, Drags, Quick, Released, Cancelled, Attack, DashPresses;
            public void OnWorldTap(float x, float y) { Taps++; }
            public void OnCadenceFlick(float x, float y) { Flicks++; }
            public void OnUiRegionTapped(int id) { Ui++; }
            public void OnRuneDragUpdated(float x, float y, float amount) { Drags++; }
            public void OnRuneQuickCast() { Quick++; }
            public void OnRuneReleased(float x, float y, float amount) { Released++; }
            public void OnRuneCancelled() { Cancelled++; }
            public void OnActionButtonPressed(LabActionButton button)
            {
                if (button == LabActionButton.Attack) Attack++;
                if (button == LabActionButton.Dash) DashPresses++;
            }
        }

        private const float W = 844f, H = 390f, Ppmm = 6.3f;

        private static TouchGestureRouter Make(out Sink sink, out LabActionButtonLayout layout, out ScreenRegion rune)
        {
            InputRoutingManager routing = new InputRoutingManager();
            rune = RuneButtonLayout.Compute(W, H, Ppmm).Button;
            routing.SetRuneZone(rune);
            sink = new Sink();
            layout = LabActionButtonLayout.Compute(W, H, Ppmm);
            TouchGestureRouter router = new TouchGestureRouter(routing, sink, ControlMode.ModeA_FullScreenFlick)
            {
                ScreenWidth = W, ScreenHeight = H, MinRadiusPixels = 10f, JoystickRadiusPixels = 50f,
                MoveZone = new ScreenRegion(0f, 0f, W * 0.42f, H * 0.55f), RuneSaturationPixels = 80f, RuneTapSlopPixels = 20f,
                ActionButtons = layout, ActionButtonsEnabled = true
            };
            router.SetThirdPersonEnabled(true);
            return router;
        }

        private static float Cx(ScreenRegion r) => (r.XMin + r.XMax) * 0.5f;
        private static float Cy(ScreenRegion r) => (r.YMin + r.YMax) * 0.5f;

        [Test] // C3
        public void C3_ThirdPerson_RuneCancelAndQuickCastRulesUnchanged()
        {
            TouchGestureRouter r = Make(out Sink sink, out _, out ScreenRegion rune);
            float x = Cx(rune), y = Cy(rune);
            r.ProcessTouch(1, TouchPhaseKind.Began, x, y, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Moved, x, y + 40f, 0.1, 0);
            r.ProcessTouch(1, TouchPhaseKind.Moved, x, y + 3f, 0.3, 0);
            r.ProcessTouch(1, TouchPhaseKind.Ended, x, y + 3f, 0.5, 0);
            Assert.AreEqual(1, sink.Cancelled, "拖出再滑回原點放手＝取消");
            Assert.AreEqual(0, sink.Released);

            r.ProcessTouch(2, TouchPhaseKind.Began, x, y, 1.0, 1.0);
            r.ProcessTouch(2, TouchPhaseKind.Ended, x, y, 1.05, 1.0);
            Assert.AreEqual(1, sink.Quick, "短促點按＝極速石牆");

            r.ProcessTouch(3, TouchPhaseKind.Began, x, y, 2.0, 2.0);
            r.ProcessTouch(3, TouchPhaseKind.Moved, x, y + 60f, 2.1, 2.0);
            r.ProcessTouch(3, TouchPhaseKind.Ended, x, y + 60f, 2.2, 2.0);
            Assert.AreEqual(1, sink.Released);
            Assert.AreEqual(0, sink.Attack + sink.DashPresses + sink.Taps + sink.Flicks);
        }

        // ───────────────────────── E 觸控路由與版面 ─────────────────────────

        [Test] // E1
        public void E1_StickHeld_PlusAttackPress_DoNotStealEachOther()
        {
            TouchGestureRouter r = Make(out Sink sink, out LabActionButtonLayout layout, out _);
            r.ProcessTouch(1, TouchPhaseKind.Began, 100f, 100f, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Moved, 130f, 100f, 0.05, 0);
            Assert.AreEqual(0.6f, r.MoveX, 1e-5f);

            float ax = Cx(layout.Attack), ay = Cy(layout.Attack);
            r.ProcessTouch(2, TouchPhaseKind.Began, ax, ay, 0.1, 0.1);
            Assert.AreEqual(1, sink.Attack, "按下當下送出");
            Assert.IsTrue(r.MoveHeld);
            Assert.AreEqual(0.6f, r.MoveX, 1e-5f);

            r.ProcessTouch(2, TouchPhaseKind.Moved, ax - 60f, ay + 40f, 0.15, 0.1);
            r.ProcessTouch(1, TouchPhaseKind.Stationary, 130f, 100f, 0.15, 0);
            r.ProcessTouch(2, TouchPhaseKind.Ended, ax - 60f, ay + 40f, 0.2, 0.1);
            Assert.AreEqual(1, sink.Attack, "移動／放開不重複觸發");
            Assert.AreEqual(0.6f, r.MoveX, 1e-5f);
            Assert.AreEqual(0f, r.LookDeltaX); Assert.AreEqual(0f, r.LookDeltaY);
            Assert.AreEqual(0, sink.Taps); Assert.AreEqual(0, sink.Flicks); Assert.AreEqual(0, sink.DashPresses);
        }

        [Test] // E2
        public void E2_LookHeld_PlusDashPress_DoNotStealEachOther()
        {
            TouchGestureRouter r = Make(out Sink sink, out LabActionButtonLayout layout, out _);
            r.ProcessTouch(1, TouchPhaseKind.Began, 470f, 340f, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Moved, 490f, 345f, 0.05, 0);
            Assert.AreEqual(20f, r.LookDeltaX);

            float dx = Cx(layout.Dash), dy = Cy(layout.Dash);
            r.ProcessTouch(2, TouchPhaseKind.Began, dx, dy, 0.1, 0.1);
            Assert.AreEqual(1, sink.DashPresses);
            r.ProcessTouch(2, TouchPhaseKind.Moved, dx + 30f, dy + 30f, 0.15, 0.1);
            Assert.AreEqual(20f, r.LookDeltaX, "DASH 手指不得累加轉頭");
            r.ProcessTouch(1, TouchPhaseKind.Moved, 500f, 345f, 0.2, 0);
            Assert.AreEqual(30f, r.LookDeltaX, "轉頭手指持續有效");
            r.ProcessTouch(2, TouchPhaseKind.Ended, dx + 30f, dy + 30f, 0.25, 0.1);
            r.ProcessTouch(1, TouchPhaseKind.Ended, 500f, 345f, 0.3, 0);
            Assert.AreEqual(1, sink.DashPresses);
            Assert.AreEqual(0, sink.Taps); Assert.AreEqual(0, sink.Flicks); Assert.AreEqual(0, sink.Attack);
        }

        [Test] // E3
        public void E3_ButtonsOnlyInThirdPerson_FireOnceOnBegan_RuneKeepsPriority()
        {
            TouchGestureRouter r = Make(out Sink sink, out LabActionButtonLayout layout, out ScreenRegion rune);
            float ax = Cx(layout.Attack), ay = Cy(layout.Attack);
            r.ProcessTouch(1, TouchPhaseKind.Began, ax, ay, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Stationary, ax, ay, 0.05, 0);
            r.ProcessTouch(1, TouchPhaseKind.Ended, ax, ay, 0.1, 0);
            Assert.AreEqual(1, sink.Attack);

            r.ProcessTouch(2, TouchPhaseKind.Began, Cx(rune), Cy(rune), 0.5, 0.5);
            r.ProcessTouch(2, TouchPhaseKind.Ended, Cx(rune), Cy(rune), 0.55, 0.5);
            Assert.AreEqual(1, sink.Quick, "符印區仍歸符印");
            Assert.AreEqual(1, sink.Attack, "符印區不觸發按鈕"); Assert.AreEqual(0, sink.DashPresses);

            r.SetThirdPersonEnabled(false);
            r.ProcessTouch(3, TouchPhaseKind.Began, ax, ay, 1.0, 1.0);
            r.ProcessTouch(3, TouchPhaseKind.Ended, ax, ay, 1.05, 1.0);
            float dx = Cx(layout.Dash), dy = Cy(layout.Dash);
            r.ProcessTouch(4, TouchPhaseKind.Began, dx, dy, 1.2, 1.2);
            r.ProcessTouch(4, TouchPhaseKind.Ended, dx, dy, 1.25, 1.2);
            Assert.AreEqual(1, sink.Attack, "俯視不觸發按鈕");
            Assert.AreEqual(0, sink.DashPresses);
            Assert.AreEqual(2, sink.Taps, "俯視落在按鈕位置的手指照舊是世界點擊");
        }

        [Test] // E4
        public void E4_LayoutInvariants_AcrossScreens()
        {
            float[,] cases =
            {
                { 844f, 390f, 6.3f }, { 640f, 360f, 6.3f }, { 1280f, 720f, 7.56f },
                { 1688f, 780f, 7.56f }, { 1688f, 780f, 12.6f }, { 2532f, 1170f, 18.9f }
            };
            for (int c = 0; c < cases.GetLength(0); c++)
            {
                float w = cases[c, 0], h = cases[c, 1], ppmm = cases[c, 2];
                string label = w + "x" + h + "@" + ppmm;
                LabActionButtonLayout layout = LabActionButtonLayout.Compute(w, h, ppmm);
                ScreenRegion rune = RuneButtonLayout.Compute(w, h, ppmm).Button;
                float margin = Math.Max(RuneButtonLayout.EdgeMarginMillimeters * ppmm, GestureMath.EdgeDeadzonePixels + 8f);
                ScreenRegion move = new ScreenRegion(0f, 0f, w * 0.42f, h * 0.55f);
                ScreenRegion[] buttons = { layout.Attack, layout.Dash };
                foreach (ScreenRegion b in buttons)
                {
                    Assert.Greater(b.XMax - b.XMin, 0f, label);
                    Assert.GreaterOrEqual(b.XMin, GestureMath.EdgeDeadzonePixels, label);
                    Assert.GreaterOrEqual(b.YMin, GestureMath.EdgeDeadzonePixels, label);
                    Assert.LessOrEqual(b.XMax, w - GestureMath.EdgeDeadzonePixels, label);
                    Assert.LessOrEqual(b.YMax, h - GestureMath.EdgeDeadzonePixels, label + " 上緣");
                    Assert.GreaterOrEqual(w - b.XMax, margin - 1e-3f, label + " 右邊距");
                    Assert.GreaterOrEqual(b.YMin, margin - 1e-3f, label + " 下邊距");
                    Assert.IsFalse(Overlaps(b, rune), label + " 壓到符印鈕");
                    Assert.IsFalse(Overlaps(b, move), label + " 壓到搖桿區");
                    Assert.GreaterOrEqual(b.XMin, w * 0.5f, label + " 右側拇指區");
                    Assert.IsFalse(GestureMath.IsInEdgeDeadzone(b.XMin, b.YMin, w, h, GestureMath.EdgeDeadzonePixels), label);
                    Assert.IsFalse(GestureMath.IsInEdgeDeadzone(b.XMax, b.YMax, w, h, GestureMath.EdgeDeadzonePixels), label);
                }
                Assert.IsFalse(Overlaps(layout.Attack, layout.Dash), label + " 兩鈕重疊");
                Assert.AreEqual(rune.XMax - rune.XMin, layout.Attack.XMax - layout.Attack.XMin, 1e-3f, label + " 直徑同符印鈕");
            }
        }

        private static bool Overlaps(ScreenRegion a, ScreenRegion b)
        {
            return a.XMin < b.XMax && b.XMin < a.XMax && a.YMin < b.YMax && b.YMin < a.YMax;
        }
    }
}
