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
    // 弓「蓄滿一條線」覆審 r1 的重現與修正驗收（凍結檔 acceptance-bowline-20261003.md「修訂 R1」：H1、H2、D3～D8）。
    // 同 BowLinePlayTests：全部走真路由；新成員一律反射讀（基底 f9b4133 沒有＝讀成 0／null），所以基底也編得過、紅在行為上。
    public sealed partial class CameraLabCombatPlayTests
    {
        private static int ActiveArrowCount()
        {
            int n = 0;
            foreach (LineRenderer l in Object.FindObjectsOfType<LineRenderer>())
                if (l.name == "BowArrow" && l.gameObject.activeInHierarchy) n++;
            return n;
        }

        private static float DrawnWidth(LineRenderer line)
        {
            float curveMax = 0f;
            foreach (Keyframe k in line.widthCurve.keys) curveMax = Mathf.Max(curveMax, k.value);
            return line.widthMultiplier * (line.widthCurve.length > 0 ? curveMax : 1f);
        }

        // M3／D6：「看得見」的最低條件——物件在、Renderer 開著、材質不是 null／洋紅錯誤 shader、主鏡頭有畫這一層、尺寸與不透明度 > 0。
        private static void AssertRendered(GameObject go, string label)
        {
            Assert.IsNotNull(go, label + "：物件存在");
            Assert.IsTrue(go.activeInHierarchy, label + "：active");
            Renderer r = go.GetComponent<Renderer>();
            Assert.IsNotNull(r, label + "：有 Renderer");
            Assert.IsTrue(r.enabled, label + "：Renderer enabled");
            Assert.IsNotNull(r.sharedMaterial, label + "：材質非 null");
            Assert.IsNotNull(r.sharedMaterial.shader, label + "：shader 非 null");
            Assert.AreNotEqual("Hidden/InternalErrorShader", r.sharedMaterial.shader.name, label + "：不是洋紅錯誤 shader");
            Assert.IsTrue(r.sharedMaterial.shader.isSupported, label + "：shader 可用");
            Assert.AreNotEqual(0, Camera.main.cullingMask & (1 << go.layer), label + "：主鏡頭有畫這一層");
            if (r is LineRenderer line)
            {
                Assert.Greater(DrawnWidth(line), 0f, label + "：線寬 > 0");
                Assert.Greater(Mathf.Max(line.startColor.a, line.endColor.a), 0f, label + "：不透明度 > 0");
                Vector3 a = line.GetPosition(0), b = line.GetPosition(line.positionCount - 1);
                float extent = 0f;
                for (int i = 0; i < line.positionCount; i++) extent = Mathf.Max(extent, Vector3.Distance(a, line.GetPosition(i)));
                Assert.Greater(extent + Vector3.Distance(a, b), 0f, label + "：尺寸 > 0");
            }
        }

        private static Vector3 Centroid(GameObject cue)
        {
            LineRenderer line = cue.GetComponent<LineRenderer>();
            int n = line.positionCount > 1 ? line.positionCount - 1 : line.positionCount;   // 四芒星最後一點＝第一點（收口）
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < n; i++) sum += line.GetPosition(i);
            return sum / Mathf.Max(1, n);
        }

        // ── H1：箭只在真的結算時生成（點 12 次、傷害 N 次 → 箭 N 支）──
        [UnityTest] // R1-H1(a)：6m 木樁 4Hz 連點 12 下、量 4s：每一刻箭數 ≤ 傷害次數；結束時 箭＝傷害＝出手提示、命中提示＝傷害
        public IEnumerator R1_H1_RapidTwelveTaps_ArrowsEqualDamageEvents()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            int s0 = LastArrow().Serial, r0 = ReleaseCues(), c0 = HitCues();
            int drops = 0, taps = 0;
            float last = dummy.Health;
            float start = Time.time, nextTap = Time.time;
            while (Time.time - start < 4f)
            {
                if (taps < 12 && Time.time >= nextTap - 1e-4f) { TapAttack(); taps++; nextTap += 0.25f; }
                yield return null;
                if (dummy.Health < last) drops++;
                last = dummy.Health;
                Assert.LessOrEqual(LastArrow().Serial - s0, drops,
                    "H1：任何時刻箭數不得多於傷害次數（全部瞄準木樁、無空射；t=" + (Time.time - start).ToString("F2") + " taps=" + taps + "）");
            }
            _hero.ClearCombatTargetInPlace();   // 停手，讓最後一支飛到
            float settle = Time.time + 0.9f;
            while (Time.time < settle)
            {
                yield return null;
                if (dummy.Health < last) drops++;
                last = dummy.Health;
            }
            int arrows = LastArrow().Serial - s0;
            Debug.Log("[BOWLINE R1] H1a taps=" + taps + " damage=" + drops + " arrows=" + arrows
                + " releaseCues=" + (ReleaseCues() - r0) + " hitCues=" + (HitCues() - c0));
            Assert.AreEqual(12, taps, "測試前提：點了 12 下");
            Assert.Greater(drops, 0, "測試前提：真的有傷害");
            Assert.AreEqual(drops, arrows, "H1：點 12 次，箭數＝傷害次數");
            Assert.AreEqual(arrows, ReleaseCues() - r0, "H1：出手提示＝箭數（以結算為單位）");
            Assert.AreEqual(drops, HitCues() - c0, "H1：命中提示＝傷害次數（每支箭到各一次）");
        }

        [UnityTest] // R1-H1(b)：6m 只點一下、放 4s：自動普攻每次傷害各一支箭（原本 1 支箭對 5 次掉血）
        public IEnumerator R1_H1_SingleTapThenAutoAttacks_EveryDamageHasAnArrow()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            int s0 = LastArrow().Serial, c0 = HitCues();
            int drops = 0;
            float last = dummy.Health;
            TapAttack();
            float until = Time.time + 4f;
            while (Time.time < until)
            {
                yield return null;
                if (dummy.Health < last) drops++;
                last = dummy.Health;
            }
            _hero.ClearCombatTargetInPlace();
            float settle = Time.time + 0.9f;
            while (Time.time < settle)
            {
                yield return null;
                if (dummy.Health < last) drops++;
                last = dummy.Health;
            }
            int arrows = LastArrow().Serial - s0;
            Debug.Log("[BOWLINE R1] H1b taps=1 damage=" + drops + " arrows=" + arrows + " hitCues=" + (HitCues() - c0));
            Assert.GreaterOrEqual(drops, 4, "測試前提：4s 內自動普攻持續掉血");
            Assert.AreEqual(drops, arrows, "H1：自動普攻的弓射擊造成傷害也各生一支箭");
            Assert.AreEqual(drops, HitCues() - c0, "H1：每次傷害各一次命中提示");
        }

        [UnityTest] // R1-H1(c)：錐內無人的快速點擊＝空射：放開當下一支、出手提示 +1；1.5s 內無傷害、命中提示不增；箭 ≤ 傷害＋空射
        public IEnumerator R1_H1_EmptyQuickTap_IsAnAirShot()
        {
            yield return SetupBow(6f);
            DummyTarget dummy = _bowDummy;
            _lab.RotateThirdPerson(180f);
            yield return null; yield return null;
            int s0 = LastArrow().Serial, r0 = ReleaseCues(), c0 = HitCues();
            float h0 = dummy.Health;
            TapAttack();
            Assert.AreEqual(s0 + 1, LastArrow().Serial, "空射：放開當下一支箭");
            Assert.IsFalse(LastArrow().Hit, "空射：不是命中");
            Assert.AreEqual(r0 + 1, ReleaseCues(), "空射：出手提示 +1");
            yield return WaitSeconds(1.5f);
            Assert.AreEqual(h0, dummy.Health, "測試前提：無傷害");
            Assert.AreEqual(c0, HitCues(), "空射：命中提示不增");
            Assert.LessOrEqual(LastArrow().Serial - s0, 0 + 1, "箭數 ≤ 傷害次數 0 ＋ 空射次數 1");
        }

        // ── H2：命中半角＝錐半角＋atan(半徑/距離)（近距離、偏離中心但在身體內）──
        [UnityTest] // R1-H2：木樁（半徑 0.5）3m／6m，瞄在離中心 0.30m 處蓄滿須中（受傷 108）；離中心 0.80m（身體外）蓄滿不中
        public IEnumerator R1_H2_NearRange_LineThroughBodyHits_OutsideBodyMisses()
        {
            float[] distances = { 3f, 6f };
            for (int i = 0; i < distances.Length; i++)
            {
                float d = distances[i];
                yield return SetupBow(d);
                DummyTarget dummy = _bowDummy;
                float inside = Mathf.Atan(0.30f / d) * Mathf.Rad2Deg;
                _lab.RotateThirdPerson(inside);
                yield return null; yield return null;
                float h0 = dummy.Health;
                yield return HoldAttack(1.0);
                yield return WaitForDrop(dummy, h0, 2f);
                Debug.Log("[BOWLINE R1] H2 d=" + d + " offset=0.30 (" + inside.ToString("F2") + "deg) hit=" + (h0 - dummy.Health).ToString("F4"));
                Assert.AreEqual(h0 - _hero.AttackDamage * BowChargedDamageMultiplier, dummy.Health, 1e-3f,
                    d + "m 離中心 0.30m（身體內）：蓄滿須中、受傷 108");

                yield return ResetHeroTarget();
                float outside = Mathf.Atan(0.80f / d) * Mathf.Rad2Deg;
                _lab.RotateThirdPerson(outside - inside);
                yield return null; yield return null;
                float h1 = dummy.Health;
                yield return HoldAttack(1.0);
                yield return WaitSeconds(1.5f);
                Debug.Log("[BOWLINE R1] H2 d=" + d + " offset=0.80 (" + outside.ToString("F2") + "deg) hit=" + (h1 - dummy.Health).ToString("F4"));
                Assert.AreEqual(h1, dummy.Health, d + "m 離中心 0.80m（身體外）：蓄滿不中");
            }
        }

        // ── D3（H3）：放開後同幀 DASH／換武器取消了這一發 → 不得有箭、不得有命中提示 ──
        [UnityTest] // R1-D3：8m 蓄滿放開、同幀 DASH 或 WPN：總傷 0、ArrowHitCueCount 0、箭視覺體全程不在
        public IEnumerator R1_D3_ReleaseThenSameFrameDashOrWeapon_NoArrowNoHitCue()
        {
            string[] labels = { "DASH", "WPN" };
            for (int v = 0; v < labels.Length; v++)
            {
                yield return SetupBow(8f);
                DummyTarget dummy = _bowDummy;
                float h0 = dummy.Health;
                int s0 = LastArrow().Serial, c0 = HitCues();
                PressAttack();
                yield return WaitUnscaled(1.0);
                ReleaseAttack();
                if (v == 0) TapDash(); else TapWeapon();   // 同一幀
                float until = Time.time + 0.6f;
                while (Time.time < until)
                {
                    yield return null;
                    Assert.AreEqual(0, ActiveArrowCount(), labels[v] + "：被取消的這一發不得有箭在飛（假命中）");
                    Assert.AreEqual(c0, HitCues(), labels[v] + "：命中提示不增");
                }
                Debug.Log("[BOWLINE R1] D3 " + labels[v] + " damage=" + (h0 - dummy.Health).ToString("F2") + " arrows=" + (LastArrow().Serial - s0) + " hitCues=" + (HitCues() - c0));
                Assert.AreEqual(h0, dummy.Health, labels[v] + "：測試前提＝這一發真的被取消（總傷 0）");
                Assert.AreEqual(s0, LastArrow().Serial, labels[v] + "：沒有結算＝不生箭");
            }
        }

        // ── D4（H4）：其他武器的傷害永不碰弓的命中提示；換武器／切 TOP 清掉空中的箭與還沒出現的提示 ──
        [UnityTest] // R1-D4(a)：3m 弓點一下→同幀切兩次到鉤鎖→鉤鎖出手打到：ArrowHitCueCount 0、沒有弓箭
        public IEnumerator R1_D4_BowTapThenSwitchToGrapple_GrappleDamageNeverCuesBow()
        {
            yield return SetupBow(3f);
            DummyTarget dummy = _bowDummy;
            TapAttack();
            TapWeapon(); TapWeapon();   // 同一幀：弓→錘→鉤鎖
            yield return null; yield return null;
            Assert.AreEqual(WeaponId.Grapple, _lab.CurrentWeapon, "測試前提：已切到鉤鎖");
            float h0 = dummy.Health;
            TapAttack();
            yield return WaitSeconds(1.5f);
            Debug.Log("[BOWLINE R1] D4a grappleDamage=" + (h0 - dummy.Health).ToString("F2") + " hitCues=" + HitCues() + " arrows=" + LastArrow().Serial);
            Assert.Less(dummy.Health, h0, "測試前提：鉤鎖真的打到");
            Assert.AreEqual(0, HitCues(), "其他武器的傷害不得觸發弓的命中提示");
            Assert.AreEqual(0, LastArrow().Serial, "這一路沒有任何弓的結算：不生箭");
        }

        [UnityTest] // R1-D4(b)：14m 蓄滿命中後、箭還在飛時換武器／切 TOP：箭立刻收、命中提示不出現
        public IEnumerator R1_D4_SwitchWeaponOrTopWhileArrowFlies_ClearsArrowAndPendingCue()
        {
            string[] labels = { "WPN", "TOP" };
            for (int v = 0; v < labels.Length; v++)
            {
                yield return SetupBow(14f);
                DummyTarget dummy = _bowDummy;
                float h0 = dummy.Health;
                int c0 = HitCues();
                yield return HoldAttack(1.0);
                yield return WaitForDrop(dummy, h0, 2f);
                yield return null;
                Assert.Less(dummy.Health, h0, labels[v] + "：測試前提＝蓄滿真的命中");
                Assert.AreEqual(1, ActiveArrowCount(), labels[v] + "：測試前提＝傷害當下箭還在飛（14m 約 0.35s）");
                Assert.AreEqual(c0, HitCues(), labels[v] + "：測試前提＝箭還沒到、提示未出");
                if (v == 0) TapWeapon(); else _lab.SetThirdPerson(false);
                yield return null;
                float until = Time.time + 0.6f;
                while (Time.time < until)
                {
                    Assert.AreEqual(0, ActiveArrowCount(), labels[v] + "：換武器／切 TOP 後箭立刻收");
                    Assert.AreEqual(c0, HitCues(), labels[v] + "：等待中的弓命中提示一併清掉");
                    yield return null;
                }
            }
        }

        // ── D5（M2）：箭終點＝目標身體中心（碰撞體中心），對 pivot 在腳底的 TrainingOpponent 不射進地面 ──
        [UnityTest] // R1-D5：峽谷對局，同高度地面上 3m 的 TrainingOpponent（Hold）：快速射擊命中箭終點高出 pivot 0.5～1.5m、水平在 0.5m 內
        public IEnumerator R1_D5_ArrowEndsAtBodyCentre_NotFootPivot()
        {
            yield return StartCanyonMatchThirdPerson();
            SelectWeaponByTaps(2);
            yield return null; yield return null;
            Assert.AreEqual(WeaponId.Bow, _lab.CurrentWeapon, "測試前提：拿弓");
            CanyonTerrainSpec t = CanyonTerrainSpec.V0140;
            Vector3 hp = TileCenter(7);
            Vector3 toward = TileCenter(8) - hp;
            toward.y = 0f;
            Vector3 rp = hp + toward.normalized * 3f;
            rp.y = hp.y;
            Assert.AreEqual(t.HeightAt(hp.x, hp.z, 0), t.HeightAt(rp.x, rp.z, 0), 0.01f, "測試前提：英雄與對手同高度");
            yield return PlaceCanyon(hp, rp, true);
            int s0 = LastArrow().Serial;
            float health = _red.HealthNormalized;
            TapAttack();
            float until = Time.time + 2f;
            while (LastArrow().Serial == s0 && Time.time < until) yield return null;
            ArrowInfo a = LastArrow();
            Vector3 pivot = _red.transform.position;
            Debug.Log("[BOWLINE R1] D5 end=" + a.End.ToString("F3") + " pivot=" + pivot.ToString("F3") + " heightAbovePivot=" + (a.End.y - pivot.y).ToString("F3"));
            Assert.Greater(a.Serial, s0, "有一支箭");
            Assert.IsTrue(a.Hit, "命中箭");
            Assert.That(a.End.y - pivot.y, Is.InRange(0.5f, 1.5f), "終點在身體中心高度（不是腳底 pivot）");
            Assert.LessOrEqual(ArrowFlatDistance(a.End, pivot), 0.5f, "終點水平在目標 0.5m 內");
            float deadline = Time.time + 1.5f;
            while (_red.HealthNormalized >= health && Time.time < deadline) yield return null;
            Assert.Less(_red.HealthNormalized, health, "測試前提：真的打到");
        }

        // ── D6（M3）：渲染檢查——箭、出手提示、命中提示、蓄滿細線 ──
        [UnityTest] // R1-D6：箭／出手提示／命中提示各自 active、材質非 null／非洋紅、主鏡頭有畫、尺寸 > 0；提示位置對；細線寬 0～0.15
        public IEnumerator R1_D6_ArrowCuesAndLine_AreActuallyRendered()
        {
            yield return SetupBow(10f);
            DummyTarget dummy = _bowDummy;
            int c0 = HitCues();
            TapAttack();
            yield return WaitForArrow(1, 1.5f);
            ArrowInfo a = LastArrow();
            AssertRendered(ArrowObject(), "箭");
            GameObject release = ReadProperty(_lab, "LastReleaseCueObject") as GameObject;
            AssertRendered(release, "出手提示");
            Assert.LessOrEqual(Vector3.Distance(Centroid(release), a.Start), 0.6f, "出手提示在箭的起點");
            float until = Time.time + 1.5f;
            while (HitCues() == c0 && Time.time < until) yield return null;
            Assert.AreEqual(c0 + 1, HitCues(), "測試前提：命中提示出現");
            GameObject hit = ReadProperty(_lab, "LastHitCueObject") as GameObject;
            AssertRendered(hit, "命中提示");
            Assert.LessOrEqual(Vector3.Distance(Centroid(hit), dummy.transform.position), 0.6f, "命中提示在木樁身上");

            yield return ResetHeroTarget();
            double t0 = Time.unscaledTimeAsDouble;
            PressAttack();
            while (Time.unscaledTimeAsDouble - t0 < 0.97) yield return null;
            yield return null;
            Assert.AreEqual("Line", PreviewKind(), "測試前提：蓄滿細線");
            GameObject indicator = ReadProperty(_lab, "AimPreviewIndicator") as GameObject;
            AssertRendered(indicator, "蓄滿細線");
            float width = DrawnWidth(indicator.GetComponent<LineRenderer>());
            Assert.That(width, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.15f), "細線寬 0～0.15m");
            ReleaseAttack();
        }

        // ── D7（M4）：箭身 ≥1.5m、寬 ≥0.12m；近距離最短可見飛行 ≥0.12s ──
        [UnityTest] // R1-D7：3m 命中箭飛行 ≥0.12s（40m/s 只要 0.08s）；箭寬 ≥0.12m；長射程飛行中畫出的箭身 ≥1.5m
        public IEnumerator R1_D7_ArrowIsLongThickAndVisibleAtShortRange()
        {
            yield return SetupBow(3f);
            TapAttack();
            yield return WaitForArrow(1, 1.5f);
            ArrowInfo near = LastArrow();
            GameObject obj = ArrowObject();
            Assert.IsNotNull(obj, "有箭");
            float width = DrawnWidth(obj.GetComponent<LineRenderer>());
            yield return MeasureFlight(near, obj);
            float dist = Vector3.Distance(near.Start, near.End);
            Debug.Log("[BOWLINE R1] D7 near dist=" + dist.ToString("F3") + " flight=" + _flightSeconds.ToString("F4") + " at40=" + (dist / ArrowSpeedSpec).ToString("F4")
                + " width=" + width.ToString("F3"));
            Assert.Less(dist / ArrowSpeedSpec, 0.12f, "測試前提：這個距離用 40m/s 會短於 0.12s");
            Assert.GreaterOrEqual(_flightSeconds, 0.12f, "近距離最短可見飛行 ≥0.12s");
            Assert.GreaterOrEqual(width, 0.12f, "箭寬 ≥0.12m");

            yield return ResetHeroTarget();
            _lab.RotateThirdPerson(180f);
            yield return null; yield return null;
            yield return HoldAttack(0.5);
            GameObject far = ArrowObject();
            Assert.IsNotNull(far, "空射有箭");
            float maxLen = 0f;
            float until = Time.time + 1f;
            while (Time.time < until && far.activeInHierarchy)
            {
                LineRenderer l = far.GetComponent<LineRenderer>();
                maxLen = Mathf.Max(maxLen, Vector3.Distance(l.GetPosition(0), l.GetPosition(l.positionCount - 1)));
                yield return null;
            }
            Debug.Log("[BOWLINE R1] D7 far drawnLength=" + maxLen.ToString("F3"));
            Assert.GreaterOrEqual(maxLen, 1.5f, "箭身 ≥1.5m");
        }

        // ── D8（L1）：目標在箭到前死亡——命中提示照常在箭終點（結算當下的身體中心）出現一次 ──
        [UnityTest] // R1-D8：14m 木樁 100 血、蓄滿 108 一擊殺：箭到時命中提示恰 +1、位置＝結算當下目標身體中心（±0.05m；滿蓄穿透時箭終點在射程盡頭，不是目標），沒有例外
        public IEnumerator R1_D8_TargetDiesBeforeArrival_CueAtArrowEndOnce()
        {
            yield return SetupBow(14f);
            DummyTarget dummy = _bowDummy;
            dummy.Configure(100f, dummy.TargetFaction);
            yield return null;
            int s0 = LastArrow().Serial, c0 = HitCues();
            Vector3 body = dummy.transform.position;   // 木樁碰撞體中心＝pivot（場景建置器 CreateDummy）；它不會移動
            yield return HoldAttack(1.0);
            float until = Time.time + 2f;
            while (LastArrow().Serial == s0 && Time.time < until) yield return null;
            ArrowInfo a = LastArrow();
            until = Time.time + 1f;
            while (HitCues() == c0 && Time.time < until) yield return null;
            bool aliveAtCue = dummy.IsAlive;
            object pos = ReadProperty(_lab, "LastHitCuePosition");
            yield return WaitSeconds(0.5f);
            Debug.Log("[BOWLINE R1] D8 aliveAtCue=" + aliveAtCue + " cues=" + (HitCues() - c0) + " end=" + a.End.ToString("F3") + " body=" + body.ToString("F3") + " cue=" + (pos != null ? ((Vector3)pos).ToString("F3") : "null"));
            Assert.IsFalse(aliveAtCue, "測試前提：箭到之前木樁已被這一發打死");
            Assert.AreEqual(c0 + 1, HitCues(), "死亡目標：命中提示照常恰一次");
            Assert.IsNotNull(pos, "命中提示位置可讀");
            Assert.IsTrue(a.Hit, "命中箭");
            Assert.LessOrEqual(Vector3.Distance((Vector3)pos, body), 0.05f, "命中提示在結算當下的目標身體中心（屍體已隱藏也照常）");
        }
    }
}
#endif
