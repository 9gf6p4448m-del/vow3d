#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 錘蓄力重擊 凍結驗收 A1～A8 的真場景部分。凍結檔：vow-toolchain/acceptance-hammer-20261006.md（含修訂一）。
    // 全部走真路由：ATK 用模擬按住手指（Began→每幀 Stationary→Ended），DASH／WPN 用 SendScreenTap，搖桿用第二根模擬手指。
    // 只直接引用基底 26d0b66 已有的公開成員（SweepStartCount／SweepResolveCount／LastSweepHits／木樁血量／英雄位置／
    // ActivePreview 經反射），讓同一份測試在基底編得過、紅在行為斷言。
    // 時鐘：按住秒數＝觸控路由的 unscaled 時鐘；冷卻／前搖＝遊戲時間（Time.captureDeltaTime 固定 1/60）。
    public sealed partial class CameraLabCombatPlayTests
    {
        private const double HmFullHold = 1.25;   // 按住 ≥1.2s＝蓄滿（留 0.05s 餘裕）

        private static float HmProgress(double held)
        {
            if (!(held >= 0.2)) return 0f;
            double p = held / 1.2;
            return p >= 1.0 ? 1f : (float)p;
        }

        // 同時滿足 unscaled 與遊戲時間兩個門檻：基底「按下即起手」的前搖（遊戲時間 0.25s）一定已經結算，紅燈才落在行為上。
        private static IEnumerator HmWaitBoth(double unscaledSeconds, float gameSeconds)
        {
            double u0 = Time.unscaledTimeAsDouble;
            float g0 = Time.time;
            while (Time.unscaledTimeAsDouble - u0 < unscaledSeconds || Time.time - g0 < gameSeconds) yield return null;
        }

        private static IEnumerator HmWaitGameSince(float since, float seconds)
        {
            while (Time.time - since < seconds) yield return null;
        }

        private DummyTarget _hmFront;

        // 木樁在英雄正前方（+z、準星正對）distance 公尺；血量 1000；選好錘。
        private IEnumerator HmSetup(float distance = 3f)
        {
            yield return Load();
            _hmFront = Object.FindObjectOfType<DummyTarget>();
            _hmFront.Configure(1000f, _hmFront.TargetFaction);
            yield return WarpAndSettle(_hmFront.transform.position + Vector3.back * distance);
            SelectWeapon(WeaponId.Hammer);
            yield return null;
        }

        private IEnumerator HmWaitResolve(int count)
        {
            for (int i = 0; i < 120 && _lab.SweepResolveCount < count; i++) yield return null;
            Assert.AreEqual(count, _lab.SweepResolveCount, "前搖後要結算第 " + count + " 掃");
        }

        [UnityTest] // A1：未滿 0.2s 放開＝現行橫掃（100°／3.5m／前搖 0.25s／1 倍／冷卻 0.8s），放開才出手
        public IEnumerator HA1_Hammer_QuickRelease_IsCurrentSweep_StartsOnRelease()
        {
            yield return HmSetup();
            DummyTarget far = SpawnDummyAt(_hmFront, 0f, 4.0f);    // 3.5m 外、4.5m 內：快速橫掃不得掃到
            DummyTarget side = SpawnDummyAt(_hmFront, 55f, 3f);    // 半角 50° 外、65° 內：快速橫掃不得掃到
            far.Configure(1000f, _hmFront.TargetFaction);
            side.Configure(1000f, _hmFront.TargetFaction);
            yield return null;
            float f0 = _hmFront.Health, r0 = far.Health, s0 = side.Health;
            float damage = _hero.AttackDamage;

            PressAttack();
            double t0 = Time.unscaledTimeAsDouble;
            Assert.AreEqual(0, _lab.SweepStartCount, "按住未放開不起手（蓄力重擊：放開才出手）");
            yield return null;
            double held = Time.unscaledTimeAsDouble - t0;
            Assert.Less(held, 0.2, "fixture：一幀內放開＝未滿快速門檻");
            ReleaseAttack();
            float releasedAt = Time.time;
            Assert.AreEqual(1, _lab.SweepStartCount, "快速放開即起手");
            for (int i = 0; i < 60 && _lab.SweepResolveCount == 0; i++)
            {
                Assert.AreEqual(f0, _hmFront.Health, "前搖未結算前不扣血");
                yield return null;
            }
            Assert.AreEqual(1, _lab.SweepResolveCount);
            float windup = Time.time - releasedAt;
            Assert.GreaterOrEqual(windup, 0.25f - 1e-3f, "前搖 0.25s（自放開起算）未到不結算");
            Assert.LessOrEqual(windup, 0.25f + 1f / 60f + 1e-3f, "前搖到點那一幀結算");
            yield return WaitSeconds(0.1f);
            Assert.AreEqual(f0 - damage, _hmFront.Health, 1e-3f, "正前 3m 受 1 倍普攻");
            Assert.AreEqual(r0, far.Health, "快速橫掃半徑 3.5m：4.0m 0 傷");
            Assert.AreEqual(s0, side.Health, "快速橫掃全角 100°：55° 0 傷");
            Assert.AreEqual(1, _lab.LastSweepHits);

            yield return HmWaitGameSince(releasedAt, 0.7f);
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "快速橫掃冷卻 0.8s 內不起手");
            yield return HmWaitGameSince(releasedAt, 0.82f);
            TapAttack();
            Assert.AreEqual(2, _lab.SweepStartCount, "冷卻 0.8s 後可再起手");
        }

        [UnityTest] // A2：蓄滿放開＝130°／4.5m 扇形、每個命中目標 1.6 倍；錐內沒人也出手；己方石牆與同陣營不傷
        public IEnumerator HA2_Hammer_FullCharge_WiderLonger_OnePointSix_EmptyConeAndFactions()
        {
            yield return HmSetup();
            float f0 = _hmFront.Health;
            // ① 準星轉向背面：蓄滿放開照樣起手、結算、零命中。
            _lab.RotateThirdPerson(180f);
            PressAttack();
            yield return WaitUnscaled(HmFullHold);
            ReleaseAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "錐內沒人也出手（蓄滿）");
            yield return HmWaitResolve(1);
            Assert.AreEqual(0, _lab.LastSweepHits, "背面零命中");
            Assert.AreEqual(f0, _hmFront.Health, "背後的木樁不受傷");
            _lab.RotateThirdPerson(180f);
            yield return WaitSeconds(1.3f);   // 等蓄滿冷卻

            DummyTarget far42 = SpawnDummyAt(_hmFront, 0f, 4.2f);    // 4.5m 內（快速 3.5m 外）
            DummyTarget side60 = SpawnDummyAt(_hmFront, 60f, 3f);    // 半角 65° 內（快速 50° 外）
            DummyTarget side70 = SpawnDummyAt(_hmFront, 70f, 3f);    // 65° 外
            DummyTarget far48 = SpawnDummyAt(_hmFront, 0f, 4.8f);    // 4.5m 外
            DummyTarget behind = SpawnDummyAt(_hmFront, 180f, 3f);
            DummyTarget ally = SpawnDummyAt(_hmFront, 25f, 2f);
            ally.Configure(1000f, _hero.HeroFaction);
            DummyTarget[] enemies = { far42, side60, side70, far48, behind };
            foreach (DummyTarget d in enemies) d.Configure(1000f, _hmFront.TargetFaction);
            Vector3 h = _hero.transform.position;
            TestWallTarget wall = PlaceOwnTestWall(new Vector3(h.x - 1.0f, h.y, h.z + 1.6f));   // 己方石牆，牆心在扇形內
            yield return null;
            float a0 = _hmFront.Health, b0 = far42.Health, c0 = side60.Health, d0 = side70.Health, e0 = far48.Health;
            float g0 = behind.Health, al0 = ally.Health, w0 = wall.Health;
            float charged = _hero.AttackDamage * 1.6f;

            PressAttack();
            yield return WaitUnscaled(HmFullHold);
            Assert.AreEqual(a0, _hmFront.Health, "蓄力中不出手");
            ReleaseAttack();
            Assert.AreEqual(2, _lab.SweepStartCount, "蓄滿放開起手");
            yield return HmWaitResolve(2);
            yield return WaitSeconds(0.1f);
            Debug.Log("[HAMMER TEST] A2 front=" + _hmFront.Health + " far42=" + far42.Health + " side60=" + side60.Health
                + " side70=" + side70.Health + " far48=" + far48.Health + " hits=" + _lab.LastSweepHits);
            Assert.AreEqual(a0 - charged, _hmFront.Health, 1e-3f, "正前 3m 受 1.6 倍");
            Assert.AreEqual(b0 - charged, far42.Health, 1e-3f, "正前 4.2m（蓄滿半徑 4.5m 內）受 1.6 倍");
            Assert.AreEqual(c0 - charged, side60.Health, 1e-3f, "60°（蓄滿全角 130° 內）受 1.6 倍");
            Assert.AreEqual(d0, side70.Health, "70°（130° 外）0 傷");
            Assert.AreEqual(e0, far48.Health, "4.8m（4.5m 外）0 傷");
            Assert.AreEqual(g0, behind.Health, "正後 0 傷");
            Assert.AreEqual(al0, ally.Health, "同陣營 0 傷");
            Assert.AreEqual(w0, wall.Health, "己方石牆 0 傷");
            Assert.AreEqual(3, _lab.LastSweepHits, "恰好 3 個命中");
        }

        [UnityTest] // A3：傷害隨蓄力單調不降，中間值＝1+0.6p 倍（p＝t/1.2）
        public IEnumerator HA3_Hammer_DamageGrowsWithCharge_QuickMidFull()
        {
            yield return HmSetup();
            double[] holds = { 0.0, 0.6, HmFullHold };
            float[] dealt = new float[holds.Length];
            for (int k = 0; k < holds.Length; k++)
            {
                float h0 = _hmFront.Health;
                float p;
                if (holds[k] <= 0.0) { TapAttack(); p = 0f; }
                else
                {
                    PressAttack();
                    double t0 = Time.unscaledTimeAsDouble;
                    yield return WaitUnscaled(holds[k]);
                    double held = Time.unscaledTimeAsDouble - t0;
                    ReleaseAttack();
                    p = HmProgress(held);
                }
                yield return HmWaitResolve(k + 1);
                yield return WaitSeconds(0.05f);
                dealt[k] = h0 - _hmFront.Health;
                float expected = _hero.AttackDamage * (1f + 0.6f * p);
                Debug.Log("[HAMMER TEST] A3 hold=" + holds[k] + " p=" + p.ToString("F4") + " dealt=" + dealt[k].ToString("F3") + " expected=" + expected.ToString("F3"));
                Assert.AreEqual(expected, dealt[k], 0.05f, "按住 " + holds[k] + "s：傷害＝1+0.6p 倍（p=" + p + "）");
                yield return WaitSeconds(1.3f);
            }
            Assert.Less(dealt[0], dealt[1], "中間蓄力傷害大於快速橫掃");
            Assert.Less(dealt[1], dealt[2], "蓄滿傷害大於中間蓄力");
            Assert.AreEqual(_hero.AttackDamage * 1.6f, dealt[2], 1e-3f, "蓄滿 1.6 倍");
        }

        // 一幀位移（沿搖桿方向）＝期望倍率×正常步速×dt，誤差 ≤5%。
        private IEnumerator HmStepFrames(int frames, float scale, string label)
        {
            for (int i = 0; i < frames; i++)
            {
                Vector2 stick = (Time.frameCount / 3) % 2 == 0 ? Vector2.right : Vector2.left;
                Vector3 dir = new Vector3(stick.x, 0f, 0f);
                ChangeBowMove(stick);
                AssertBowPathClear(dir);
                Vector3 before = _hero.transform.position;
                yield return null;
                float moved = Vector3.Dot(Flat(_hero.transform.position - before), dir);
                float expected = BowMoveNormalSpeed * scale * Time.deltaTime;
                Assert.AreEqual(expected, moved, expected * 0.05f, label + "：一幀位移＝" + scale + "×正常步速（frame " + i + "）");
            }
        }

        [UnityTest] // A4：蓄力中實際步速＝正常 ×0.6；放開後前搖 0.25s＋收招 0.15s 位移＝0；之後恢復正常步速
        public IEnumerator HA4_Hammer_ChargeWalksAtSixtyPercent_ReleaseLocksWindupAndRecovery()
        {
            yield return HmSetup(8f);   // 木樁在前方 8m，搖桿只左右折返
            BeginBowMove(Vector2.right);
            yield return null; yield return null;
            yield return HmStepFrames(12, 1f, "對照：未按 ATK");

            PressAttack();
            double t0 = Time.unscaledTimeAsDouble;
            yield return HmStepFrames(24, 0.6f, "蓄力中");
            int guard = 0;
            while (Time.unscaledTimeAsDouble - t0 < HmFullHold && guard++ < 2000) yield return HmStepFrames(1, 0.6f, "蓄力中（續）");
            ReleaseAttack();
            float releasedAt = Time.time;
            Assert.AreEqual(1, _lab.SweepStartCount, "放開起手");
            int lockedFrames = 0;
            while (true)
            {
                Vector2 stick = (Time.frameCount / 3) % 2 == 0 ? Vector2.right : Vector2.left;
                ChangeBowMove(stick);
                Vector3 before = _hero.transform.position;
                yield return null;
                if (Time.time - releasedAt >= 0.39f) break;
                float moved = Flat(_hero.transform.position - before).magnitude;
                Assert.LessOrEqual(moved, 1e-4f, "放開後前搖＋收招原地鎖步（放開後 " + (Time.time - releasedAt).ToString("F3") + "s）");
                lockedFrames++;
            }
            Assert.GreaterOrEqual(lockedFrames, 22, "鎖步窗口涵蓋前搖 0.25s＋收招 0.15s");
            yield return null; yield return null;
            yield return HmStepFrames(12, 1f, "收招結束後恢復正常步速");
            _input.EndSimulatedHold(1);
        }

        [UnityTest] // A5：蓄力中 DASH／切武器／切鏡頭＝作廢：不出手、不傷人、不進冷卻
        public IEnumerator HA5_Hammer_ChargeCanceledByDashWeaponOrView_NoSweepNoDamageNoCooldown()
        {
            string[] labels = { "DASH", "WPN", "TOP↔THIRD" };
            for (int kind = 0; kind < labels.Length; kind++)
            {
                string label = labels[kind];
                yield return HmSetup();
                float h0 = _hmFront.Health;
                PressAttack();
                yield return HmWaitBoth(0.5, 0.4f);
                if (kind == 0) TapDash();
                else if (kind == 1) TapWeapon();
                else { _lab.SetThirdPerson(false); yield return null; _lab.SetThirdPerson(true); }
                yield return null;
                ReleaseAttack();
                Assert.AreEqual(0, _lab.SweepStartCount, label + "：作廢後放開不出手");
                if (kind == 1) SelectWeapon(WeaponId.Hammer);
                yield return WaitSeconds(0.4f);
                Assert.AreEqual(h0, _hmFront.Health, label + "：作廢不傷人");
                Assert.AreEqual(0, _lab.SweepResolveCount, label + "：作廢不結算");
                TapAttack();
                Assert.AreEqual(1, _lab.SweepStartCount, label + "：作廢不進冷卻，立刻可起手");
                yield return HmWaitResolve(1);
                yield return WaitSeconds(0.05f);
                Assert.AreEqual(h0 - _hero.AttackDamage, _hmFront.Health, 1e-3f, label + "：之後正常一掃照樣扣血（對照）");
            }
        }

        [UnityTest] // A6：輸入被鎖（對局暫停／倒地）時蓄力與前搖取消，不結算傷害
        public IEnumerator HA6_Hammer_InputLockOrDeath_CancelsChargeAndWindup_NoDamage()
        {
            // ① 蓄力中輸入被鎖：解鎖後再放開也不出手；之後不在冷卻。
            yield return HmSetup();
            float h0 = _hmFront.Health;
            PressAttack();
            yield return HmWaitBoth(0.5, 0.4f);
            SetHeroInputLockedForTest(true);
            Assert.IsTrue(_bootstrap.HeroInputBlockedForLab);
            yield return WaitFrames(3);
            SetHeroInputLockedForTest(false);
            yield return null;
            ReleaseAttack();
            Assert.AreEqual(0, _lab.SweepStartCount, "蓄力中被鎖：取消，放開不出手");
            yield return WaitSeconds(0.4f);
            Assert.AreEqual(h0, _hmFront.Health, "蓄力中被鎖：不傷人");
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "取消不進冷卻（對照）");
            yield return HmWaitResolve(1);
            yield return WaitSeconds(0.05f);
            Assert.AreEqual(h0 - _hero.AttackDamage, _hmFront.Health, 1e-3f, "對照：正常一掃扣血");

            // ② 蓄滿放開後、前搖中被鎖 3 幀即解鎖：這一掃取消，不結算。
            yield return HmSetup();
            h0 = _hmFront.Health;
            PressAttack();
            yield return WaitUnscaled(HmFullHold);
            ReleaseAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "蓄滿放開起手");
            SetHeroInputLockedForTest(true);
            yield return WaitFrames(3);
            SetHeroInputLockedForTest(false);
            yield return WaitSeconds(0.5f);
            Assert.AreEqual(h0, _hmFront.Health, "前搖中被鎖：取消，不傷人");
            Assert.AreEqual(0, _lab.SweepResolveCount, "前搖中被鎖：不結算");

            // ③ 蓄力中倒地：放開不出手、不傷人。
            yield return HmSetup();
            h0 = _hmFront.Health;
            PressAttack();
            yield return HmWaitBoth(0.5, 0.4f);
            _hero.TakeDuelDamage(10000f, DamageType.True);
            Assert.IsFalse(_hero.IsAlive, "fixture：英雄倒地");
            yield return WaitFrames(2);
            ReleaseAttack();
            Assert.AreEqual(0, _lab.SweepStartCount, "倒地：蓄力取消，放開不出手");
            yield return WaitSeconds(0.5f);
            Assert.AreEqual(h0, _hmFront.Health, "倒地：不傷人");
        }

        [UnityTest] // A7（修訂一）：出手後冷卻＝0.8＋0.4p 秒；蓄滿 1.2s 內再按不起手
        public IEnumerator HA7_Hammer_CooldownScalesWithCharge_FullBlocksWithinOnePointTwo()
        {
            yield return HmSetup();
            PressAttack();
            yield return WaitUnscaled(HmFullHold);
            ReleaseAttack();
            float t = Time.time;
            Assert.AreEqual(1, _lab.SweepStartCount, "蓄滿放開起手");
            yield return HmWaitGameSince(t, 0.9f);
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "蓄滿後 0.9s（>0.8、<1.2）再按不起手");
            yield return HmWaitGameSince(t, 1.15f);
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "蓄滿後 1.15s 再按不起手");
            yield return HmWaitGameSince(t, 1.22f);
            TapAttack();
            Assert.AreEqual(2, _lab.SweepStartCount, "蓄滿冷卻 1.2s 後可起手");

            yield return WaitSeconds(0.9f);   // 快速橫掃冷卻 0.8s
            PressAttack();
            double t0 = Time.unscaledTimeAsDouble;
            yield return WaitUnscaled(0.6);
            double held = Time.unscaledTimeAsDouble - t0;
            ReleaseAttack();
            float t2 = Time.time;
            Assert.AreEqual(3, _lab.SweepStartCount, "中間蓄力放開起手");
            float cooldown = 0.8f + 0.4f * HmProgress(held);
            Debug.Log("[HAMMER TEST] A7 mid held=" + held.ToString("F4") + " cooldown=" + cooldown.ToString("F4"));
            yield return HmWaitGameSince(t2, cooldown - 0.04f);
            TapAttack();
            Assert.AreEqual(3, _lab.SweepStartCount, "中間蓄力：冷卻 0.8+0.4p 內（-0.04s）不起手");
            yield return HmWaitGameSince(t2, cooldown + 0.04f);
            TapAttack();
            Assert.AreEqual(4, _lab.SweepStartCount, "中間蓄力：冷卻 0.8+0.4p 後（+0.04s）可起手");
        }

        [UnityTest] // A8：按住時預覽扇形（ActivePreview＋實際畫線）隨蓄力放大到 130°／4.5m
        public IEnumerator HA8_Hammer_HoldPreview_GrowsToFullChargeAndLineMatches()
        {
            yield return HmSetup();
            PressAttack();
            double t0 = Time.unscaledTimeAsDouble;
            Assert.AreEqual("Sector", PreviewKind(), "按下當下：扇形");
            Assert.AreEqual(50f, PreviewHalfAngle(), 1e-3f, "按下當下：半角 50°");
            Assert.AreEqual(3.5f, PreviewRange(), 1e-3f, "按下當下：3.5m");
            float prevHalf = 50f, prevRange = 3.5f;
            int grew = 0;
            double sampled = 0.0;
            while (sampled < 1.25)
            {
                // 協程在 Update 之後、LateUpdate 之前恢復：讀到的預覽是上一幀 LateUpdate 依「上一幀 unscaled 時間」算的。
                sampled = Time.unscaledTimeAsDouble - t0;
                yield return null;
                float p = HmProgress(sampled);
                float half = PreviewHalfAngle(), range = PreviewRange();
                Assert.AreEqual((100f + 30f * p) * 0.5f, half, 1e-2f, "預覽半角＝(100+30p)/2，p=" + p);
                Assert.AreEqual(3.5f + p, range, 1e-3f, "預覽半徑＝3.5+p，p=" + p);
                Assert.GreaterOrEqual(half, prevHalf - 1e-4f, "預覽半角單調不降");
                Assert.GreaterOrEqual(range, prevRange - 1e-4f, "預覽半徑單調不降");
                Assert.IsTrue(PreviewIndicatorActive(), "蓄力中 indicator 持續顯示");
                if (half > prevHalf + 1e-4f) grew++;
                prevHalf = half;
                prevRange = range;
            }
            Assert.AreEqual(65f, prevHalf, 1e-3f, "蓄滿：半角 65°（全角 130°）");
            Assert.AreEqual(4.5f, prevRange, 1e-3f, "蓄滿：4.5m");
            Assert.Greater(grew, 0, "預覽確實隨蓄力放大");
            LineRenderer line = (ReadProperty(_lab, "AimPreviewIndicator") as GameObject).GetComponent<LineRenderer>();
            Vector3[] pts = new Vector3[line.positionCount];
            line.GetPositions(pts);
            float arc = Flat(pts[1] - pts[0]).magnitude;
            float mid = Flat(pts[pts.Length / 2] - pts[0]).magnitude;
            Assert.AreEqual(4.5f, arc, 1e-2f, "實際畫出的扇形邊＝4.5m");
            Assert.AreEqual(4.5f, mid, 1e-2f, "實際畫出的扇形弧中點＝4.5m");
            Vector3 left = Flat(pts[1] - pts[0]), right = Flat(pts[pts.Length - 2] - pts[0]);
            Assert.AreEqual(130f, Vector3.Angle(left, right), 0.5f, "實際畫出的扇形全角＝130°");
            ReleaseAttack();
            Assert.AreEqual("None", PreviewKind(), "放開後預覽收起");
        }
    }
}
#endif
