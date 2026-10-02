using System;
using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests
{
    // v0.16.0 camera-lab 武器灰盒（劍／弓／錘＋WPN 切換鈕）凍結驗收 W1／W2／W4（純邏輯部分）／W5（版面與路由）。
    // 凍結檔：vow-toolchain/acceptance-weapons-20261002.md。dotnet 與 Unity EditMode 共用。
    public sealed class WeaponLabTests
    {
        // ───────────────────────── W1 四筆數值 ─────────────────────────

        [Test]
        public void W1_Standard_EqualsCurrentCameraLabAim()
        {
            WeaponSpec s = WeaponSpec.Standard;
            Assert.AreEqual(WeaponId.Standard, s.Id);
            Assert.AreEqual(CameraLabAim.ConeHalfAngleDegrees, s.ConeHalfAngleDegrees);
            Assert.AreEqual(CameraLabAim.MaxAimDistance, s.AimRangeMeters);
            Assert.AreEqual(30f, s.ConeHalfAngleDegrees);
            Assert.AreEqual(8f, s.AimRangeMeters);
            Assert.AreEqual(5f, s.AttackRangeMeters, "紀錄 HeroTuningAsset 預設 5m");
            Assert.IsFalse(s.OverridesAttackRange, "Standard 不覆寫攻擊射程");
            Assert.IsTrue(s.FallbackToNearest);
            Assert.IsFalse(s.IsSweep);
        }

        [Test]
        public void W1_SwordBowHammer_ValuesLocked()
        {
            WeaponSpec sw = WeaponSpec.Sword;
            Assert.AreEqual(WeaponId.Sword, sw.Id);
            Assert.AreEqual(0f, sw.ConeHalfAngleDegrees, "劍：錐 0°＝無錐內優先");
            Assert.AreEqual(5f, sw.AimRangeMeters);
            Assert.AreEqual(5f, sw.AttackRangeMeters);
            Assert.IsFalse(sw.OverridesAttackRange);
            Assert.IsTrue(sw.FallbackToNearest);
            Assert.IsFalse(sw.IsSweep);

            WeaponSpec b = WeaponSpec.Bow;
            Assert.AreEqual(WeaponId.Bow, b.Id);
            Assert.AreEqual(12f, b.ConeHalfAngleDegrees);
            Assert.AreEqual(12f, b.AimRangeMeters);
            Assert.AreEqual(12f, b.AttackRangeMeters);
            Assert.IsTrue(b.OverridesAttackRange, "弓：攻擊射程覆寫 12m");
            Assert.IsFalse(b.FallbackToNearest, "弓：錐外不挑");
            Assert.IsFalse(b.IsSweep);

            WeaponSpec h = WeaponSpec.Hammer;
            Assert.AreEqual(WeaponId.Hammer, h.Id);
            Assert.IsTrue(h.IsSweep);
            Assert.AreEqual(100f, h.SweepFullAngleDegrees, "全角 100°");
            Assert.AreEqual(3.5f, h.SweepRangeMeters);
            Assert.AreEqual(0.8f, h.SweepCooldownSeconds, "冷卻＝普攻週期");
            Assert.AreEqual(0.25f, h.SweepWindupSeconds, "前搖");
            Assert.IsFalse(h.OverridesAttackRange);

            Assert.AreEqual(WeaponSpec.Sword.Id, WeaponSpec.Get(WeaponId.Sword).Id);
            Assert.AreEqual(WeaponSpec.Bow.Id, WeaponSpec.Get(WeaponId.Bow).Id);
            Assert.AreEqual(WeaponSpec.Hammer.Id, WeaponSpec.Get(WeaponId.Hammer).Id);
            Assert.AreEqual(WeaponSpec.Standard.Id, WeaponSpec.Get(WeaponId.Standard).Id);
        }

        [Test]
        public void W1_Selection_DefaultsStandard_CyclesFourWeapons()
        {
            WeaponSelection sel = default;
            Assert.AreEqual(WeaponId.Standard, sel.CurrentId, "預設 Standard");
            Assert.AreEqual(WeaponId.Sword, sel.Next());
            Assert.AreEqual(WeaponId.Bow, sel.Next());
            Assert.AreEqual(WeaponId.Hammer, sel.Next());
            Assert.AreEqual(WeaponId.Grapple, sel.Next());
            Assert.AreEqual(WeaponId.Standard, sel.Next(), "循環回 Standard");
            Assert.AreEqual(WeaponId.Standard, sel.Current.Id);
            sel.Next();
            sel.Reset();
            Assert.AreEqual(WeaponId.Standard, sel.CurrentId);
        }

        // ───────────────────────── W2 挑目標 ─────────────────────────

        private static float[] Polar(float degreesFromZ, float distance)
        {
            double a = degreesFromZ * Math.PI / 180.0;
            return new[] { (float)(Math.Sin(a) * distance), (float)(Math.Cos(a) * distance) };
        }

        private static int ResolveWith(WeaponSpec weapon, int preferred, params float[][] targets)
        {
            AimTargetPicker picker = default;
            picker.Begin(0f, 0f, 0f, 1f, weapon);
            for (int i = 0; i < targets.Length; i++) picker.Consider(i, targets[i][0], targets[i][1], i == preferred);
            return picker.ResolvedIndex;
        }

        [Test]
        public void W2_Sword_PicksNearestWithinFiveMetres_IgnoringAim()
        {
            WeaponSpec sw = WeaponSpec.Sword;
            Assert.AreEqual(1, ResolveWith(sw, -1, Polar(0f, 4f), Polar(90f, 2f)), "正前 4m vs 側面 2m：取最近（不管準星）");
            Assert.AreEqual(1, ResolveWith(sw, -1, Polar(5f, 3f), Polar(180f, 2.5f)), "背後較近也取");
            Assert.AreEqual(0, ResolveWith(sw, -1, Polar(180f, 4.99f)), "背後 4.99m 在射程內");
            Assert.AreEqual(-1, ResolveWith(sw, -1, Polar(0f, 5.01f)), "正前 5.01m 超出 5m 不挑");
            Assert.AreEqual(1, ResolveWith(sw, -1, Polar(0f, 6f), Polar(120f, 4.5f)), "5m 外正前方不搶 5m 內的");
            Assert.AreEqual(-1, ResolveWith(sw, -1), "無候選");
        }

        [Test]
        public void W2_Bow_TwelveDegreeCone_NoFallback_TwelveMetres()
        {
            WeaponSpec b = WeaponSpec.Bow;
            Assert.AreEqual(0, ResolveWith(b, -1, Polar(8f, 10f)), "12° 錐內 10m 挑得到");
            Assert.AreEqual(0, ResolveWith(b, -1, Polar(0f, 10f)), "正前 10m");
            Assert.AreEqual(-1, ResolveWith(b, -1, Polar(20f, 6f)), "錐外 20° 處 6m 不挑（Fallback=false）");
            Assert.AreEqual(-1, ResolveWith(b, -1, Polar(180f, 2f)), "背後 2m 不挑");
            Assert.AreEqual(-1, ResolveWith(b, -1, Polar(0f, 12.01f)), "12m 外不挑");
            Assert.AreEqual(0, ResolveWith(b, -1, Polar(0f, 11.99f)), "11.99m 挑得到");
            Assert.AreEqual(1, ResolveWith(b, -1, Polar(20f, 3f), Polar(10f, 11f)), "錐內遠的勝過錐外近的");
            Assert.AreEqual(-1, ResolveWith(b, 0, Polar(20f, 6f)), "正在打的目標出錐也不留（錐外一律不挑）");
        }

        // 覆審 r1 M3（主對話裁定＝照凍結 W2 字面）：劍永遠取射程內最近，不吃「正在打的目標」黏性。
        [Test]
        public void W2_Sword_IgnoresPreferred_AlwaysNearestWithinFive()
        {
            WeaponSpec sw = WeaponSpec.Sword;
            Assert.AreEqual(1, ResolveWith(sw, 0, Polar(0f, 4.5f), Polar(90f, 1f)), "正在打 4.5m 的 A，B 走到 1m→選 B");
            Assert.AreEqual(1, ResolveWith(sw, 0, Polar(0f, 6f), Polar(180f, 3f)), "正在打的 A 已在 5m 外→選 5m 內的 B");
            Assert.AreEqual(0, ResolveWith(sw, 0, Polar(0f, 2f), Polar(90f, 3f)), "A 本來就最近→A");
            Assert.AreEqual(-1, ResolveWith(sw, 0, Polar(0f, 5.5f)), "只剩 5m 外的 A→不挑");
        }

        // 有錐武器的黏性不變：弓錐內正在打的目標，他人只靠準星不到 15° 就不換。
        [Test]
        public void W2_Bow_KeepsStickyPreferredInCone()
        {
            WeaponSpec b = WeaponSpec.Bow;
            Assert.AreEqual(0, ResolveWith(b, 0, Polar(10f, 8f), Polar(0f, 6f)), "錐內 preferred 10° vs 他人 0°（差 10°<15°）→留");
            Assert.AreEqual(1, ResolveWith(b, -1, Polar(10f, 8f), Polar(0f, 6f)), "沒有 preferred→夾角小者");
        }

        // Standard 走武器多載必須與既有 6 參數 Begin 逐案相同（含黏性與 fallbackEligible）。
        [Test]
        public void W2_Standard_WeaponOverload_MatchesLegacyBegin_OnRandomCases()
        {
            uint seed = 20261002u;
            float Next01() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 16777216f; }
            int inCone = 0, fallback = 0, stickyKept = 0;
            for (int c = 0; c < 2000; c++)
            {
                float aimDeg = Next01() * 360f;
                float ax = (float)Math.Sin(aimDeg * Math.PI / 180.0), az = (float)Math.Cos(aimDeg * Math.PI / 180.0);
                float ox = Next01() * 10f - 5f, oz = Next01() * 10f - 5f;
                int n = (int)(Next01() * 6f);
                int preferred = (int)(Next01() * (n + 1)) - 1;
                AimTargetPicker legacy = default, weapon = default;
                legacy.Begin(ox, oz, ax, az, CameraLabAim.ConeHalfAngleDegrees, CameraLabAim.MaxAimDistance);
                weapon.Begin(ox, oz, ax, az, WeaponSpec.Standard);
                for (int i = 0; i < n; i++)
                {
                    float tx = ox + Next01() * 20f - 10f, tz = oz + Next01() * 20f - 10f;
                    bool eligible = Next01() > 0.3f;
                    bool a = legacy.Consider(i, tx, tz, i == preferred, eligible);
                    bool b = weapon.Consider(i, tx, tz, i == preferred, eligible);
                    Assert.AreEqual(a, b, "case " + c + " Consider " + i);
                }
                Assert.AreEqual(legacy.BestIndex, weapon.BestIndex, "case " + c + " Best");
                Assert.AreEqual(legacy.NearestIndex, weapon.NearestIndex, "case " + c + " Nearest");
                Assert.AreEqual(legacy.PreferredIndex, weapon.PreferredIndex, "case " + c + " Preferred");
                Assert.AreEqual(legacy.ResolvedIndex, weapon.ResolvedIndex, "case " + c + " Resolved");
                if (legacy.BestIndex >= 0) inCone++;
                else if (legacy.ResolvedIndex >= 0) fallback++;
                if (preferred >= 0 && legacy.ResolvedIndex == preferred && legacy.BestIndex != preferred) stickyKept++;
            }
            // 活性：三條分支都真的被行使過（共同卡死也會逐值相同）。
            Assert.Greater(inCone, 50, "錐內分支");
            Assert.Greater(fallback, 50, "錐外退回分支");
            Assert.Greater(stickyKept, 20, "黏性分支");
        }

        // ───────────────────────── W5 版面與路由 ─────────────────────────

        private static bool Overlaps(ScreenRegion a, ScreenRegion b)
        {
            return a.XMin < b.XMax && b.XMin < a.XMax && a.YMin < b.YMax && b.YMin < a.YMax;
        }

        [Test]
        public void W5_WeaponButton_DoesNotOverlapAtkDashRuneOrStick_AcrossScreens()
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
                ScreenRegion wpn = layout.Weapon;
                Assert.Greater(wpn.XMax - wpn.XMin, 0f, label);
                Assert.Greater(wpn.YMax - wpn.YMin, 0f, label);
                Assert.IsFalse(Overlaps(wpn, layout.Attack), label + " 壓到 ATK");
                Assert.IsFalse(Overlaps(wpn, layout.Dash), label + " 壓到 DASH");
                Assert.IsFalse(Overlaps(wpn, rune), label + " 壓到符印鈕");
                Assert.IsFalse(Overlaps(wpn, new ScreenRegion(0f, 0f, w * 0.42f, h * 0.55f)), label + " 壓到搖桿區");
                Assert.GreaterOrEqual(wpn.XMin, w * 0.5f, label + " 右側拇指區");
                Assert.AreEqual(LabActionButton.Weapon, layout.Hit((wpn.XMin + wpn.XMax) * .5f, (wpn.YMin + wpn.YMax) * .5f), label);
            }
        }

        // 覆審 r1 M2：佔領模式三選一天賦盤（DebugHudLayout.TalentPanel，IMGUI 座標×Scale）可見時，WPN 區須為空、或不與它重疊；
        // 天賦盤不可見時 WPN 照原位。6 組既有解析度（dpi＝ppmm×25.4）＋審查列 844x390@dpi96、2532x1170@dpi288。
        [Test]
        public void W5_TalentPanelVisible_WeaponYieldsOrAvoidsPanel_RestoresWhenHidden()
        {
            float[,] cases =
            {
                { 844f, 390f, 6.3f * 25.4f }, { 640f, 360f, 6.3f * 25.4f }, { 1280f, 720f, 7.56f * 25.4f },
                { 1688f, 780f, 7.56f * 25.4f }, { 1688f, 780f, 12.6f * 25.4f }, { 2532f, 1170f, 18.9f * 25.4f },
                { 844f, 390f, 96f }, { 2532f, 1170f, 288f }
            };
            int overlappedBefore = 0;
            for (int c = 0; c < cases.GetLength(0); c++)
            {
                float w = cases[c, 0], h = cases[c, 1], dpi = cases[c, 2];
                float ppmm = GestureMath.MillimetersToPixels(1f, dpi, 160f);
                string label = w + "x" + h + "@dpi" + dpi;
                DebugHudLayout hud = DebugHudLayout.Compute(w, h, dpi, true, true, true, true, true);
                float s = hud.Scale;
                HudRect t = hud.TalentPanel;
                ScreenRegion talent = new ScreenRegion(t.XMin * s, h - t.YMax * s, t.XMax * s, h - t.YMin * s);

                LabActionButtonLayout hidden = LabActionButtonLayout.Compute(w, h, ppmm, false);
                Assert.IsTrue(hidden.WeaponVisible, label + " 天賦盤沒顯示：WPN 在");
                if (Overlaps(hidden.Weapon, talent)) overlappedBefore++;

                LabActionButtonLayout shown = LabActionButtonLayout.Compute(w, h, ppmm, true);
                Assert.IsTrue(!shown.WeaponVisible || !Overlaps(shown.Weapon, talent), label + " 天賦盤顯示時 WPN 壓到天賦盤");
                float cxw = (hidden.Weapon.XMin + hidden.Weapon.XMax) * .5f, cyw = (hidden.Weapon.YMin + hidden.Weapon.YMax) * .5f;
                if (!shown.WeaponVisible) Assert.AreNotEqual(LabActionButton.Weapon, shown.Hit(cxw, cyw), label + " 讓開時不收路由");
                Assert.AreEqual(LabActionButton.Attack, shown.Hit((shown.Attack.XMin + shown.Attack.XMax) * .5f, (shown.Attack.YMin + shown.Attack.YMax) * .5f), label + " ATK 不受影響");
            }
            Assert.Greater(overlappedBefore, 0, "原位置至少一組與天賦盤重疊，否則本測試沒有鑑別力");
        }

        private sealed class WeaponSink : ITouchGestureSink, IActionButtonSink
        {
            public int Taps, Flicks, Attack, DashPresses, Weapon, Other;
            public void OnWorldTap(float x, float y) { Taps++; }
            public void OnCadenceFlick(float x, float y) { Flicks++; }
            public void OnUiRegionTapped(int id) { Other++; }
            public void OnRuneDragUpdated(float x, float y, float amount) { Other++; }
            public void OnRuneQuickCast() { Other++; }
            public void OnRuneReleased(float x, float y, float amount) { Other++; }
            public void OnRuneCancelled() { Other++; }
            public void OnActionButtonPressed(LabActionButton button)
            {
                if (button == LabActionButton.Attack) Attack++;
                else if (button == LabActionButton.Dash) DashPresses++;
                else if (button == LabActionButton.Weapon) Weapon++;
            }
            public void OnActionButtonReleased(LabActionButton button, float heldSeconds) { }
            public void OnActionButtonCanceled(LabActionButton button) { }
        }

        [Test]
        public void W5_Router_WeaponButton_FiresOnce_NoWorldTapNoAttack_OnlyInThirdPerson()
        {
            const float W = 844f, H = 390f, Ppmm = 6.3f;
            InputRoutingManager routing = new InputRoutingManager();
            routing.SetRuneZone(RuneButtonLayout.Compute(W, H, Ppmm).Button);
            WeaponSink sink = new WeaponSink();
            LabActionButtonLayout layout = LabActionButtonLayout.Compute(W, H, Ppmm);
            TouchGestureRouter r = new TouchGestureRouter(routing, sink, ControlMode.ModeA_FullScreenFlick)
            {
                ScreenWidth = W, ScreenHeight = H, MinRadiusPixels = 10f, JoystickRadiusPixels = 50f,
                MoveZone = new ScreenRegion(0f, 0f, W * 0.42f, H * 0.55f), RuneSaturationPixels = 80f, RuneTapSlopPixels = 20f,
                ActionButtons = layout, ActionButtonsEnabled = true
            };
            r.SetThirdPersonEnabled(true);
            float x = (layout.Weapon.XMin + layout.Weapon.XMax) * .5f, y = (layout.Weapon.YMin + layout.Weapon.YMax) * .5f;
            r.ProcessTouch(1, TouchPhaseKind.Began, x, y, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Stationary, x, y, 0.05, 0);
            r.ProcessTouch(1, TouchPhaseKind.Ended, x, y, 0.1, 0);
            Assert.AreEqual(1, sink.Weapon, "按下當下送出一次");
            Assert.AreEqual(0, sink.Attack + sink.DashPresses + sink.Taps + sink.Flicks, "不觸發攻擊／世界點擊");
            Assert.AreEqual(0f, r.LookDeltaX, "不轉頭");

            r.SetThirdPersonEnabled(false);
            r.ProcessTouch(2, TouchPhaseKind.Began, x, y, 1.0, 1.0);
            r.ProcessTouch(2, TouchPhaseKind.Ended, x, y, 1.05, 1.0);
            Assert.AreEqual(1, sink.Weapon, "俯視不觸發");
        }

        // ───────────────────────── W4 錘（純邏輯部分）─────────────────────────

        private static bool InHammer(float degreesFromAim, float distance)
        {
            float[] p = Polar(degreesFromAim, distance);
            return WeaponSweep.Contains(WeaponSpec.Hammer, 0f, 0f, 0f, 1f, p[0], p[1]);
        }

        [Test]
        public void W4_HammerSweep_FullAngleHundredDegrees_RadiusThreePointFive()
        {
            Assert.IsTrue(InHammer(0f, 3f), "正前 3m");
            Assert.IsTrue(InHammer(-30f, 2.5f), "左 30° 2.5m");
            Assert.IsTrue(InHammer(49f, 3f), "49° 在全角 100° 內");
            Assert.IsFalse(InHammer(51f, 3f), "51° 在外（全角不是半角）");
            Assert.IsFalse(InHammer(70f, 3f), "70° 在外");
            Assert.IsFalse(InHammer(180f, 3f), "正後 3m");
            Assert.IsTrue(InHammer(0f, 3.49f), "3.49m 在半徑內");
            Assert.IsFalse(InHammer(0f, 3.51f), "3.51m 超出半徑");
            Assert.IsFalse(InHammer(0f, 5f), "5m 超出半徑");
            float[] q = Polar(90f, 3f);
            Assert.IsTrue(WeaponSweep.Contains(WeaponSpec.Hammer, 0f, 0f, 1f, 0f, q[0], q[1]), "方向跟著準星（+x）");
        }

        [Test]
        public void W4_SweepTimer_WindupThenResolveOnce_CooldownBlocksRestart()
        {
            WeaponSpec h = WeaponSpec.Hammer;
            WeaponSweepTimer t = default;
            Assert.IsFalse(t.TryConsumeResolve(1f), "沒起手不結算");
            Assert.IsTrue(t.TryStart(1f, h.SweepCooldownSeconds, h.SweepWindupSeconds), "首次起手");
            Assert.IsTrue(t.Pending);
            Assert.IsFalse(t.TryConsumeResolve(1.2f), "前搖 0.25s 未到");
            Assert.IsTrue(t.TryConsumeResolve(1.25f), "前搖到點結算");
            Assert.IsFalse(t.TryConsumeResolve(1.3f), "只結算一次");
            Assert.IsFalse(t.TryStart(1.5f, h.SweepCooldownSeconds, h.SweepWindupSeconds), "0.8s 冷卻內不起手");
            Assert.IsFalse(t.TryConsumeResolve(1.79f), "冷卻內那一下不會結算");
            Assert.IsTrue(t.TryStart(1.8f, h.SweepCooldownSeconds, h.SweepWindupSeconds), "冷卻後可再起手");
            Assert.IsTrue(t.TryConsumeResolve(2.05f));
            t.Reset();
            Assert.IsFalse(t.Pending);
        }
    }
}
