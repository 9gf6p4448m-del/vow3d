#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Vow.Core.Logic;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓「蓄滿一條線」＋箭矢可見＋出手／命中回饋 凍結驗收 C2～C7 的真場景部分。凍結檔：vow-toolchain/acceptance-bowline-20261003.md。
    // 全部走真路由（模擬按住手指／SendScreenTap）。只直接引用基底 223b876 也有的成員；新成員（LastArrowVisual、LastArrowObject、
    // BowReleaseCueCount、ArrowHitCueCount）以反射讀：基底沒有＝「從沒射出箭、從沒提示」，讀成 0／null，斷言照樣落在行為上（C10）。
    // 遊戲時間由 Load() 的 captureDeltaTime＝1/60 固定，時序以 Time.time／Time.frameCount 量。
    public sealed partial class CameraLabCombatPlayTests
    {
        private const float ArrowSpeedSpec = 40f;

        private int LabInt(string name)
        {
            object v = ReadProperty(_lab, name);
            return v != null ? (int)v : 0;
        }

        private int ReleaseCues() => LabInt("BowReleaseCueCount");
        private int HitCues() => LabInt("ArrowHitCueCount");

        private struct ArrowInfo
        {
            public int Serial;
            public Vector3 Start, End;
            public float Speed, SpawnTime;
            public bool Hit;
        }

        private ArrowInfo LastArrow()
        {
            ArrowInfo a = default;
            object v = ReadProperty(_lab, "LastArrowVisual");
            if (v == null) return a;
            System.Type t = v.GetType();
            a.Serial = (int)t.GetField("Serial").GetValue(v);
            a.Start = (Vector3)t.GetField("Start").GetValue(v);
            a.End = (Vector3)t.GetField("End").GetValue(v);
            a.Speed = (float)t.GetField("Speed").GetValue(v);
            a.SpawnTime = (float)t.GetField("SpawnTime").GetValue(v);
            a.Hit = (bool)t.GetField("Hit").GetValue(v);
            return a;
        }

        private GameObject ArrowObject() => ReadProperty(_lab, "LastArrowObject") as GameObject;

        // 箭頭目前位置＝實際繪製的 LineRenderer 最後一點。
        private static Vector3 ArrowHead(GameObject arrow)
        {
            LineRenderer line = arrow.GetComponent<LineRenderer>();
            return line.GetPosition(line.positionCount - 1);
        }

        private static float ArrowFlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        private static float FlatYaw(Vector3 from, Vector3 to) => Mathf.Repeat(Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg, 360f);

        [UnityTest] // C2：14m 偏 1.5°——快速點擊不中（超 12m）、蓄滿受傷 108；偏 3°——蓄滿不受傷（原 4° 會中）
        public IEnumerator C2_Bow_FourteenMetres_OnePointFiveDegreesFullChargeHits_ThreeDegreesMisses()
        {
            yield return SetupBow(14f);
            DummyTarget dummy = _bowDummy;
            _lab.RotateThirdPerson(1.5f);   // 木樁在準星左 1.5°（橫向約 0.37m）
            yield return null; yield return null;

            float h0 = dummy.Health;
            TapAttack();
            yield return WaitSeconds(1.5f);
            Assert.AreEqual(h0, dummy.Health, "快速點擊：14m 超出 12m，不中");

            yield return ResetHeroTarget();
            float h1 = dummy.Health;
            yield return HoldAttack(1.0);
            yield return WaitForDrop(dummy, h1, 2f);
            Debug.Log("[BOWLINE TEST] C2 1.5deg hit=" + (h1 - dummy.Health).ToString("F4"));
            Assert.AreEqual(h1 - _hero.AttackDamage * BowChargedDamageMultiplier, dummy.Health, 1e-3f, "偏 1.5°：蓄滿 2° 錐內，受傷 108");

            yield return ResetHeroTarget();
            _lab.RotateThirdPerson(1.5f);   // 共偏 3°（橫向約 0.73m）
            yield return null; yield return null;
            float h2 = dummy.Health;
            yield return HoldAttack(1.0);
            yield return WaitSeconds(1.5f);
            Debug.Log("[BOWLINE TEST] C2 3deg hit=" + (h2 - dummy.Health).ToString("F4"));
            Assert.AreEqual(h2, dummy.Health, "偏 3°：蓄滿錐只剩 2°，不受傷（原 4° 規格會中）");
            AssertBowSelected();
        }

        [UnityTest] // C3：按住弓 p<0.9＝Cone；p≥0.9＝Line、寬 ≤0.15m、長＝當下射程、方向＝aimYaw；放開 None
        public IEnumerator C3_Bow_PreviewBecomesThinLineAtNinetyPercent_FollowsAimYaw()
        {
            yield return SetupBow(6f);
            float pressYaw = _lab.YawDegrees;
            double t0 = Time.unscaledTimeAsDouble;
            PressAttack();
            int coneFrames = 0;
            while (Time.unscaledTimeAsDouble - t0 < 0.8)
            {
                yield return null;
                Assert.AreEqual("Cone", PreviewKind(), "p<0.9：仍是錐（held=" + (Time.unscaledTimeAsDouble - t0).ToString("F3") + "）");
                coneFrames++;
            }
            Assert.Greater(coneFrames, 3, "測試前提：p<0.9 期間確實量過多幀");
            while (Time.unscaledTimeAsDouble - t0 < 0.97) yield return null;
            yield return null;
            Assert.AreEqual("Line", PreviewKind(), "p≥0.9：Kind＝Line");
            Assert.IsTrue(PreviewIndicatorActive(), "細線實際顯示");

            // 拖 +40°：鏡頭還沒追上時，線已指向 aimYaw（跟手指不跟鏡頭）
            DragAttackMillimetres(AimTestDragFull);
            yield return null;
            GameObject indicator = ReadProperty(_lab, "AimPreviewIndicator") as GameObject;
            LineRenderer line = indicator.GetComponent<LineRenderer>();
            float width = line.widthMultiplier;
            float curveMax = 0f;
            foreach (Keyframe k in line.widthCurve.keys) curveMax = Mathf.Max(curveMax, k.value);
            float drawnWidth = width * (line.widthCurve.length > 0 ? curveMax : 1f);
            Vector3 first = line.GetPosition(0), last = line.GetPosition(line.positionCount - 1);
            Vector3 axis = Flat(last - first).normalized;
            float spread = 0f;
            for (int i = 0; i < line.positionCount; i++)
            {
                Vector3 d = Flat(line.GetPosition(i) - first);
                spread = Mathf.Max(spread, (d - axis * Vector3.Dot(d, axis)).magnitude);
            }
            float length = ArrowFlatDistance(first, last);
            float lineYaw = FlatYaw(first, last);
            float camera = _lab.YawDegrees;
            Debug.Log("[BOWLINE TEST] C3 kind=" + PreviewKind() + " width=" + drawnWidth.ToString("F3") + " spread=" + spread.ToString("F4")
                + " length=" + length.ToString("F3") + " range=" + PreviewRange().ToString("F3") + " lineYaw=" + lineYaw.ToString("F3")
                + " camera=" + camera.ToString("F3"));
            Assert.AreEqual("Line", PreviewKind(), "拖曳中仍是 Line");
            Assert.LessOrEqual(drawnWidth + 2f * spread, 0.15f, "細線寬 ≤ 0.15m（線寬＋點的橫向散布）");
            Assert.AreEqual(PreviewRange(), length, 0.1f, "線長＝當下射程");
            Assert.AreEqual(12f + 4f * 0.97f, PreviewRange(), 0.2f, "當下射程≈12+4p（p≈0.97～1）");
            Assert.Greater(YawDiff(camera, pressYaw + 40f), AimTestYawTolerance, "測試前提：鏡頭還沒追上");
            Assert.AreEqual(0f, YawDiff(lineYaw, pressYaw + 40f), AimTestYawTolerance, "線方向＝aimYaw＝pressYaw+40°");

            ReleaseAttack();
            Assert.AreEqual("None", PreviewKind(), "放開後 None");
            Assert.IsFalse(PreviewIndicatorActive(), "放開後細線收起");
        }

        [UnityTest] // C4：快速／蓄力／無目標出手各更新一次 LastArrowVisual；取消不更新；其他四種武器不更新
        public IEnumerator C4_Bow_ArrowSpawnsOncePerShot_NotOnCancel_NotForOtherWeapons()
        {
            yield return SetupBow(6f);
            int s0 = LastArrow().Serial;

            TapAttack();
            Assert.AreEqual(s0 + 1, LastArrow().Serial, "快速射擊：出手當下就有一支箭");
            yield return WaitSeconds(1.2f);   // 之後的自動普攻不是出手
            Assert.AreEqual(s0 + 1, LastArrow().Serial, "快速射擊：只一支（自動普攻不生箭）");

            yield return ResetHeroTarget();
            yield return HoldAttack(0.5);
            Assert.AreEqual(s0 + 2, LastArrow().Serial, "蓄力出手：一支");
            Assert.IsTrue(LastArrow().Hit, "蓄力出手挑到木樁");

            yield return ResetHeroTarget();
            _lab.RotateThirdPerson(180f);
            yield return null; yield return null;
            yield return HoldAttack(0.5);
            Assert.AreEqual(s0 + 3, LastArrow().Serial, "無目標出手：照樣一支");
            Assert.IsFalse(LastArrow().Hit, "無目標：不是命中");

            PressAttack();
            yield return WaitUnscaled(0.4);
            TapDash();
            yield return null;
            ReleaseAttack();
            yield return WaitSeconds(0.5f);
            Assert.AreEqual(s0 + 3, LastArrow().Serial, "取消（DASH）後放開：不生箭");

            int[] weapons = { 0, 1, 3, 4 };
            string[] labels = { "Standard", "Sword", "Hammer", "Grapple" };
            for (int w = 0; w < weapons.Length; w++)
            {
                yield return Load();
                DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
                dummy.Configure(1000f, dummy.TargetFaction);
                yield return WarpAndSettle(dummy.transform.position + Vector3.back * (weapons[w] == 4 ? 9f : 3f));
                SelectWeaponByTaps(weapons[w]);
                yield return null; yield return null;
                float h0 = dummy.Health;
                TapAttack();
                yield return WaitSeconds(0.6f);
                yield return HoldAttack(0.5);
                yield return WaitSeconds(1.0f);
                Assert.Less(dummy.Health, h0, labels[w] + "：對照組，真的有出手打到");
                Assert.AreEqual(0, LastArrow().Serial, labels[w] + "：不生箭");
                Assert.AreEqual(0, ReleaseCues(), labels[w] + "：沒有出手提示");
                Assert.AreEqual(0, HitCues(), labels[w] + "：沒有箭命中提示");
            }
        }

        [UnityTest] // C5：命中終點在目標 0.5m 內；打空終點離英雄＝當下射程、方向＝aimYaw；起點離英雄 ≤1.5m、高 1.0～1.6m
        public IEnumerator C5_Bow_ArrowGeometry_HitEndsAtTarget_MissEndsAtRangeAlongAimYaw()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            Vector3 hero = _hero.transform.position;
            TapAttack();
            ArrowInfo hit = LastArrow();
            Debug.Log("[BOWLINE TEST] C5 hit start=" + hit.Start.ToString("F3") + " end=" + hit.End.ToString("F3") + " dummy=" + dummy.transform.position.ToString("F3"));
            Assert.AreEqual(1, hit.Serial, "快速射擊生成一支箭");
            Assert.IsTrue(hit.Hit, "挑到木樁");
            Assert.LessOrEqual(Vector3.Distance(hit.End, dummy.transform.position), 0.5f, "命中：終點在目標 0.5m 內");
            Assert.LessOrEqual(Vector3.Distance(hit.Start, hero), 1.5f, "起點離英雄 ≤1.5m");
            Assert.That(hit.Start.y - hero.y, Is.InRange(1.0f, 1.6f), "起點在胸口高度");

            yield return ResetHeroTarget();
            _lab.RotateThirdPerson(180f);   // 背對木樁
            yield return null; yield return null;
            hero = _hero.transform.position;
            float pressYaw = _lab.YawDegrees;
            double t0 = Time.unscaledTimeAsDouble;
            PressAttack();
            while (Time.unscaledTimeAsDouble - t0 < 0.45) yield return null;
            DragAttackMillimetres(AimTestDragFor30Degrees);   // 放開前一刻才拖：鏡頭還沒追上 aimYaw
            yield return null;
            float cameraAtRelease = _lab.YawDegrees;
            double held = Time.unscaledTimeAsDouble - t0;
            ReleaseAttack();
            ArrowInfo miss = LastArrow();
            float range = BowChargeLogic.Resolve(held).RangeMeters;
            float missYaw = FlatYaw(hero, miss.End);
            Debug.Log("[BOWLINE TEST] C5 miss held=" + held.ToString("F3") + " end=" + miss.End.ToString("F3") + " dist=" + ArrowFlatDistance(hero, miss.End).ToString("F3")
                + " range=" + range.ToString("F3") + " yaw=" + missYaw.ToString("F3") + " camera=" + cameraAtRelease.ToString("F3"));
            Assert.AreEqual(2, miss.Serial, "無目標出手生成一支箭");
            Assert.IsFalse(miss.Hit, "打空");
            Assert.AreEqual(range, ArrowFlatDistance(hero, miss.End), 0.5f, "打空：終點離英雄＝當下射程");
            Assert.Greater(YawDiff(cameraAtRelease, pressYaw + 30f), AimTestYawTolerance, "測試前提：鏡頭還沒追上 aimYaw");
            Assert.AreEqual(0f, YawDiff(missYaw, pressYaw + 30f), AimTestYawTolerance, "打空：方向＝aimYaw（不是鏡頭）");
            Assert.LessOrEqual(Vector3.Distance(miss.Start, hero), 1.5f, "起點離英雄 ≤1.5m");
            Assert.That(miss.Start.y - hero.y, Is.InRange(1.0f, 1.6f), "起點在胸口高度");
        }

        // 量一支箭：每幀讀箭頭，回傳到達耗時（遊戲時間）與到達後幾秒消失。
        private float _flightSeconds, _vanishSeconds, _firstStep;

        private IEnumerator MeasureFlight(ArrowInfo arrow, GameObject obj)
        {
            _flightSeconds = _vanishSeconds = -1f;
            _firstStep = -1f;
            float arrivedAt = -1f;
            float until = Time.time + 3f;
            while (Time.time < until)
            {
                yield return null;
                bool active = obj != null && obj.activeInHierarchy;
                if (arrivedAt < 0f)
                {
                    Assert.IsTrue(active, "到達前箭要一直看得到");
                    Vector3 head = ArrowHead(obj);
                    if (_firstStep < 0f) _firstStep = Vector3.Distance(arrow.Start, head);
                    if (Vector3.Distance(head, arrow.End) < 1e-3f) { arrivedAt = Time.time; _flightSeconds = Time.time - arrow.SpawnTime; }
                }
                else if (!active) { _vanishSeconds = Time.time - arrivedAt; yield break; }
            }
        }

        [UnityTest] // C6：飛行時間＝距離/40（±20%，遊戲時間）；到達後 ≤0.3s 消失。6m 與 14m 各量一次（隨距離）
        public IEnumerator C6_Bow_ArrowFlightTimeScalesWithDistance_VanishesAfterArrival()
        {
            yield return SetupBow(6f);
            TapAttack();
            ArrowInfo near = LastArrow();
            Assert.AreEqual(1, near.Serial, "有一支箭");
            yield return MeasureFlight(near, ArrowObject());
            float nearDist = Vector3.Distance(near.Start, near.End);
            Debug.Log("[BOWLINE TEST] C6 near dist=" + nearDist.ToString("F3") + " flight=" + _flightSeconds.ToString("F4") + " expected=" + (nearDist / ArrowSpeedSpec).ToString("F4")
                + " firstStep=" + _firstStep.ToString("F3") + " vanish=" + _vanishSeconds.ToString("F4"));
            Assert.AreEqual(nearDist / ArrowSpeedSpec, _flightSeconds, nearDist / ArrowSpeedSpec * 0.2f, "6m：飛行時間＝距離/40（±20%）");
            Assert.That(_vanishSeconds, Is.InRange(0f, 0.3f), "6m：到達後 ≤0.3s 消失");

            yield return ResetHeroTarget();
            _lab.RotateThirdPerson(180f);
            yield return null; yield return null;
            yield return HoldAttack(0.5);
            ArrowInfo far = LastArrow();
            Assert.AreEqual(2, far.Serial, "有第二支箭");
            yield return MeasureFlight(far, ArrowObject());
            float farDist = Vector3.Distance(far.Start, far.End);
            Debug.Log("[BOWLINE TEST] C6 far dist=" + farDist.ToString("F3") + " flight=" + _flightSeconds.ToString("F4") + " expected=" + (farDist / ArrowSpeedSpec).ToString("F4")
                + " firstStep=" + _firstStep.ToString("F3") + " vanish=" + _vanishSeconds.ToString("F4"));
            Assert.Greater(farDist, 13f, "測試前提：第二支飛得比較遠");
            Assert.AreEqual(farDist / ArrowSpeedSpec, _flightSeconds, farDist / ArrowSpeedSpec * 0.2f, "14m：飛行時間＝距離/40（±20%）");
            Assert.That(_vanishSeconds, Is.InRange(0f, 0.3f), "14m：到達後 ≤0.3s 消失");
        }

        // 量命中提示的時序：每幀記箭頭進度、木樁掉血、提示數；回傳每個提示發生的幀與那一幀箭飛了多遠。
        private int _cueFrameA, _arrivalFrame, _damageFrameA;

        [UnityTest] // C7：出手提示同幀 +1；命中提示到達時才 +1（出手瞬間 0、到達且真的受傷後 1）；穿透兩目標共 2；打空／取消不增
        public IEnumerator C7_Bow_ReleaseCueSameFrame_HitCueOnArrivalOnly_PierceTwice_MissAndCancelNone()
        {
            // (a) 快速射擊 6m 命中
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            int r0 = ReleaseCues(), c0 = HitCues();
            float h0 = dummy.Health;
            TapAttack();
            Assert.AreEqual(r0 + 1, ReleaseCues(), "(a) 快速射擊：出手提示同幀 +1");
            Assert.AreEqual(c0, HitCues(), "(a) 出手瞬間命中提示為 0");
            yield return TraceHitCue(LastArrow(), ArrowObject(), dummy, h0, c0, "(a)");
            yield return WaitSeconds(1.2f);
            Assert.AreEqual(c0 + 1, HitCues(), "(a) 一支箭只提示一次（之後的自動普攻不算）");

            // (e) 蓄滿 14m 命中：傷害（≈0.25s）早於箭到（≈0.35s）→提示仍要等箭到
            yield return SetupBow(14f);
            dummy = _bowDummy;
            r0 = ReleaseCues(); c0 = HitCues();
            h0 = dummy.Health;
            yield return HoldAttack(1.0);
            Assert.AreEqual(r0 + 1, ReleaseCues(), "(e) 蓄力出手：出手提示同幀 +1");
            Assert.AreEqual(c0, HitCues(), "(e) 出手瞬間命中提示為 0");
            yield return TraceHitCue(LastArrow(), ArrowObject(), dummy, h0, c0, "(e)");
            Assert.Less(_damageFrameA, _arrivalFrame, "(e) 測試前提：傷害先於箭到（量得到「等箭到」）");
            Assert.AreEqual(_arrivalFrame, _cueFrameA, "(e) 提示在箭到的那一幀（不是傷害那一幀）");

            // (b) 蓄滿穿透兩木樁（6m／10m 同線）
            yield return SetupBow(6f);
            DummyTarget near = _bowDummy;
            DummyTarget far = SpawnDummyAt(near, 0f, 10f);
            far.Configure(1000f, far.TargetFaction);
            _bootstrap.TargetRegistry.Register(far);
            yield return null; yield return null;
            r0 = ReleaseCues(); c0 = HitCues();
            float n0 = near.Health, f0 = far.Health;
            yield return HoldAttack(1.0);
            Assert.AreEqual(r0 + 1, ReleaseCues(), "(b) 出手提示 +1");
            Assert.AreEqual(c0, HitCues(), "(b) 出手瞬間命中提示為 0");
            ArrowInfo pierce = LastArrow();
            GameObject pierceObj = ArrowObject();
            Assert.IsNotNull(pierceObj, "(b) 要有箭");
            float nearAt = Vector3.Dot(near.transform.position - pierce.Start, (pierce.End - pierce.Start).normalized);
            float farAt = Vector3.Dot(far.transform.position - pierce.Start, (pierce.End - pierce.Start).normalized);
            float traveled = 0f;
            float until = Time.time + 2f;
            while (Time.time < until)
            {
                yield return null;
                if (pierceObj.activeInHierarchy) traveled = Vector3.Distance(pierce.Start, ArrowHead(pierceObj));
                else traveled = Vector3.Distance(pierce.Start, pierce.End);
                int reached = (traveled + 1e-3f >= nearAt ? 1 : 0) + (traveled + 1e-3f >= farAt ? 1 : 0);
                int damaged = (near.Health < n0 ? 1 : 0) + (far.Health < f0 ? 1 : 0);
                Assert.LessOrEqual(HitCues() - c0, reached, "(b) 提示數不得超過箭已飛到的目標數（traveled=" + traveled.ToString("F2") + "）");
                Assert.LessOrEqual(HitCues() - c0, damaged, "(b) 提示數不得超過真的受傷的目標數");
                if (HitCues() - c0 >= 2 && traveled >= Vector3.Distance(pierce.Start, pierce.End) - 1e-3f) break;
            }
            Debug.Log("[BOWLINE TEST] C7b near=" + (n0 - near.Health).ToString("F2") + " far=" + (f0 - far.Health).ToString("F2")
                + " cues=" + (HitCues() - c0) + " nearAt=" + nearAt.ToString("F2") + " farAt=" + farAt.ToString("F2") + " end=" + Vector3.Distance(pierce.Start, pierce.End).ToString("F2"));
            Assert.Less(far.Health, f0, "(b) 測試前提：穿透真的傷到後方木樁");
            Assert.AreEqual(c0 + 2, HitCues(), "(b) 穿透兩目標：各一次共 2");
            Assert.AreEqual(16f, ArrowFlatDistance(_hero.transform.position, pierce.End), 0.5f, "(b) 穿透箭飛到射程盡頭（16m）");

            // (c) 打空（背對、蓄 0.5s）與 (d) 取消：全程命中提示不增
            yield return ResetHeroTarget();
            _lab.RotateThirdPerson(180f);
            yield return null; yield return null;
            r0 = ReleaseCues(); c0 = HitCues();
            yield return HoldAttack(0.5);
            Assert.AreEqual(r0 + 1, ReleaseCues(), "(c) 打空也有出手提示");
            yield return WaitSeconds(1.6f);
            Assert.AreEqual(c0, HitCues(), "(c) 打空：命中提示不增");

            PressAttack();
            yield return WaitUnscaled(0.4);
            TapDash();
            yield return null;
            ReleaseAttack();
            Assert.AreEqual(r0 + 1, ReleaseCues(), "(d) 取消：沒有出手提示");
            yield return WaitSeconds(1.2f);
            Assert.AreEqual(c0, HitCues(), "(d) 取消：命中提示不增");
        }

        // 單一目標：每幀記到達幀、掉血幀、提示幀；斷言提示在 max(到達, 掉血) 那一幀、之前一直是 0。
        private IEnumerator TraceHitCue(ArrowInfo arrow, GameObject obj, DummyTarget dummy, float h0, int c0, string label)
        {
            Assert.IsNotNull(obj, label + " 要有箭");
            Assert.IsTrue(arrow.Hit, label + " 挑到木樁");
            _cueFrameA = _arrivalFrame = _damageFrameA = -1;
            float until = Time.time + 2f;
            while (Time.time < until && (_cueFrameA < 0 || _arrivalFrame < 0 || _damageFrameA < 0))
            {
                yield return null;
                int f = Time.frameCount;
                if (_arrivalFrame < 0 && (!obj.activeInHierarchy || Vector3.Distance(ArrowHead(obj), arrow.End) < 1e-3f)) _arrivalFrame = f;
                if (_damageFrameA < 0 && dummy.Health < h0) _damageFrameA = f;
                int cues = HitCues() - c0;
                if (_arrivalFrame < 0) Assert.AreEqual(0, cues, label + " 箭還沒到：命中提示仍為 0");
                if (_damageFrameA < 0) Assert.AreEqual(0, cues, label + " 還沒受傷：命中提示仍為 0");
                if (_cueFrameA < 0 && cues > 0) { _cueFrameA = f; Assert.AreEqual(1, cues, label + " 一次只 +1"); }
            }
            Debug.Log("[BOWLINE TEST] C7" + label + " arrival=" + _arrivalFrame + " damage=" + _damageFrameA + " cue=" + _cueFrameA);
            Assert.Greater(_arrivalFrame, 0, label + " 箭有到");
            Assert.Greater(_damageFrameA, 0, label + " 木樁有受傷");
            Assert.AreEqual(Mathf.Max(_arrivalFrame, _damageFrameA), _cueFrameA, label + " 命中提示在「箭到且受傷」那一幀");
        }
    }
}
#endif
