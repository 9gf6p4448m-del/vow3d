#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 穿透卷（凍結檔 vow-toolchain/acceptance-pierce-20261003.md）E1、E3、E4、E5、E6、E8、E9 的真場景部分（E7 在 CapturePlayTests）。
    // 英雄一律站在 Load() 後的真實出生高度（E6 例外：站在腳底 y=2 的平台上），不放 y=1。
    // 只直接引用基底 67691e0 也有的成員，讓同一份測試在基底編得過、紅在行為斷言（E11）。
    public sealed partial class CameraLabCombatPlayTests
    {
        private float _pierceSpawnY;

        private static void PierceLog(string s) { Debug.Log("[PIERCE TEST] " + s); }

        // 木樁 1 在英雄正前方（+z、準星正對）distance 公尺；英雄腳底 heroFeetY（NaN＝真實出生高度）；木樁 1 pivot 改到 stakePivotY（NaN＝不動）。選好弓。
        private IEnumerator SetupBowGround(float distance, float heroFeetY = float.NaN, float stakePivotY = float.NaN)
        {
            yield return Load();
            _pierceSpawnY = _hero.transform.position.y;
            _bowDummy = Object.FindObjectOfType<DummyTarget>();
            _bowDummy.Configure(1000f, _bowDummy.TargetFaction);
            if (!float.IsNaN(stakePivotY))
            {
                Vector3 s = _bowDummy.transform.position;
                _bowDummy.transform.position = new Vector3(s.x, stakePivotY, s.z);
            }
            Vector3 p = _bowDummy.transform.position + Vector3.back * distance;
            p.y = float.IsNaN(heroFeetY) ? _pierceSpawnY : heroFeetY;
            yield return WarpAndSettle(p);
            Assert.AreEqual(0f, _lab.YawDegrees);
            SelectWeaponByTaps(2);
            yield return null; yield return null;
            Physics.SyncTransforms();
        }

        // 第二木樁：英雄正前 forward 公尺、橫偏 lateral，pivot 高 pivotY（NaN＝同木樁 1）。登記進穿透查表（比照場景木樁）。
        private DummyTarget SpawnSecondStake(DummyTarget template, float lateral, float forward, float pivotY = float.NaN)
        {
            Vector3 h = _hero.transform.position;
            float y = float.IsNaN(pivotY) ? template.transform.position.y : pivotY;
            DummyTarget clone = Object.Instantiate(template, new Vector3(h.x + lateral, y, h.z + forward), template.transform.rotation);
            _bootstrap.RegisterElementTarget(clone);
            _bootstrap.TargetRegistry.Register(clone);
            clone.Configure(1000f, clone.TargetFaction);
            Physics.SyncTransforms();
            return clone;
        }

        private static string BoundsY(Component c)
        {
            Collider col = c.GetComponent<Collider>();
            if (col == null) return "none";
            return "[" + col.bounds.min.y.ToString("F2") + "," + col.bounds.max.y.ToString("F2") + "]";
        }

        // 滿蓄放開（按住 1.0s），等木樁 1 受傷，停火；回傳兩者受傷量（以木樁 1 結算那一幀＋之後 0.5s 計）。
        private float _pierceD1, _pierceD2;
        private IEnumerator FullChargeAndMeasure(DummyTarget first, CombatTargetBehaviour second)
        {
            float h1 = first.Health, h2 = second.Health;
            yield return HoldAttack(1.0);
            Assert.AreSame(first, _lab.LastAimTarget, "測試前提：準星正對木樁 1＝直接目標");
            yield return WaitForDrop(first, h1, 2f);
            _pierceD1 = h1 - first.Health;
            _hero.ClearCombatTargetInPlace();   // 停手：只量這一發
            yield return WaitSeconds(0.5f);
            _pierceD2 = h2 - second.Health;
        }

        [UnityTest] // E1：英雄在真實出生高度（站地面），6m／10m 兩木樁同線（pivot y=1、半徑 0.5），滿蓄放開→兩者各受傷 108
        public IEnumerator Pierce_E1_GroundHero_FullCharge_HitsBothStakesInLine()
        {
            yield return SetupBowGround(6f);
            DummyTarget first = _bowDummy;
            DummyTarget second = SpawnSecondStake(first, 0f, 10f);
            yield return null;
            float heroY = _hero.transform.position.y;
            CapsuleCollider cap = second.GetComponent<CapsuleCollider>();
            PierceLog("E1 spawnY=" + _pierceSpawnY.ToString("F3") + " heroY=" + heroY.ToString("F3")
                + " stakePivotY=" + first.transform.position.y.ToString("F3") + " secondBoundsY=" + BoundsY(second)
                + " radius=" + (cap != null ? (cap.radius * Mathf.Max(second.transform.lossyScale.x, second.transform.lossyScale.z)).ToString("F2") : "?"));
            Assert.AreEqual(_pierceSpawnY, heroY, 1e-3f, "前提：英雄在真實出生高度（沒有被放到 y=1）");
            Assert.Less(heroY, first.transform.position.y - 0.5f, "前提：英雄站地面（腳底明顯低於木樁 pivot）");
            yield return FullChargeAndMeasure(first, second);
            PierceLog("E1 first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4"));
            float full = _hero.AttackDamage * BowChargedDamageMultiplier;
            Assert.AreEqual(full, _pierceD1, 1e-3f, "木樁 1 受蓄滿 108");
            Assert.AreEqual(full, _pierceD2, 1e-3f, "英雄站地面：同線第二木樁也受 108（穿透）");
        }

        [UnityTest] // E3：E1 配置，第二木樁離直線 0.30m（身體內）→兩者中；0.80m（身體外，0.8>0.5+0.15）→只中第一個
        public IEnumerator Pierce_E3_GroundHero_LateralThirtyHits_EightyMisses()
        {
            float[] laterals = { 0.30f, 0.80f };
            for (int i = 0; i < laterals.Length; i++)
            {
                yield return SetupBowGround(6f);
                DummyTarget first = _bowDummy;
                DummyTarget second = SpawnSecondStake(first, laterals[i], 10f);
                yield return null;
                Assert.AreEqual(_pierceSpawnY, _hero.transform.position.y, 1e-3f, "前提：英雄在真實出生高度");
                yield return FullChargeAndMeasure(first, second);
                PierceLog("E3 lateral=" + laterals[i] + " heroY=" + _hero.transform.position.y.ToString("F3")
                    + " first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4"));
                float full = _hero.AttackDamage * BowChargedDamageMultiplier;
                Assert.AreEqual(full, _pierceD1, 1e-3f, "木樁 1 受蓄滿 108");
                if (i == 0) Assert.AreEqual(full, _pierceD2, 1e-3f, "離直線 0.30m（身體內）：同一發也中 108");
                else Assert.AreEqual(0f, _pierceD2, "離直線 0.80m（身體外）：不中");
            }
        }

        [UnityTest] // E4：第二目標＝TrainingOpponent（pivot 在腳底、直立膠囊半徑 0.35），英雄站地面→受傷 108
        public IEnumerator Pierce_E4_GroundHero_TrainingOpponentBehindStake_IsPierced()
        {
            yield return SetupBowGround(6f);
            DummyTarget first = _bowDummy;
            Vector3 h = _hero.transform.position;
            TrainingOpponent red = Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(red, "場景要有 TrainingOpponent");
            red.HoldForTest(true);
            red.RespawnAt(new Vector3(h.x, h.y, h.z + 10f));
            // 同 CameraLabChaseMemoryPlayTests.SpawnBystander：Hold 中不會自己動，手動設成「啟用」才算活的可打目標。
            typeof(TrainingOpponent).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(red, true);
            Physics.SyncTransforms();
            yield return null; yield return null;
            CapsuleCollider cap = red.GetComponent<CapsuleCollider>();
            PierceLog("E4 heroY=" + _hero.transform.position.y.ToString("F3") + " redPivot=" + red.transform.position.ToString("F2")
                + " redBoundsY=" + BoundsY(red) + " redRadius=" + (cap != null ? cap.radius.ToString("F2") : "?"));
            Assert.AreEqual(_pierceSpawnY, _hero.transform.position.y, 1e-3f, "前提：英雄在真實出生高度");
            Assert.AreEqual(h.y, red.transform.position.y, 0.05f, "前提：對手 pivot 在腳底、與英雄同地面");
            Assert.IsTrue(red.IsAlive, "前提：對手是活的可打目標");
            yield return FullChargeAndMeasure(first, red);
            PierceLog("E4 first=" + _pierceD1.ToString("F4") + " red=" + _pierceD2.ToString("F4"));
            float full = _hero.AttackDamage * BowChargedDamageMultiplier;
            Assert.AreEqual(full, _pierceD1, 1e-3f, "木樁 1 受蓄滿 108");
            Assert.AreEqual(full, _pierceD2, 1e-3f, "pivot 在腳底的對手在線上：受 108");
        }

        [UnityTest] // E5：高處目標——碰撞盒最低點高於線＋0.15m（底部 y=1.3）→不被穿透；碰撞盒涵蓋線高度（底部 0.8）→被穿透
        public IEnumerator Pierce_E5_HighTargetAboveLineMissed_CoveringTargetPierced()
        {
            // (a) 木樁 1 也在高處（pivot 2.3，碰撞 1.3～3.3）6m，第二木樁 pivot 2.3 在 10m：直接目標照中，第二個不中。
            yield return SetupBowGround(6f, float.NaN, 2.3f);
            DummyTarget first = _bowDummy;
            DummyTarget second = SpawnSecondStake(first, 0f, 10f, 2.3f);
            yield return null;
            Collider c2 = second.GetComponent<Collider>();
            PierceLog("E5a heroY=" + _hero.transform.position.y.ToString("F3") + " firstBoundsY=" + BoundsY(first) + " secondBoundsY=" + BoundsY(second));
            Assert.AreEqual(_pierceSpawnY, _hero.transform.position.y, 1e-3f, "前提：英雄在真實出生高度");
            Assert.GreaterOrEqual(c2.bounds.min.y - _hero.transform.position.y, 1.3f - 1e-3f, "前提：第二目標碰撞盒底部 ≥ 英雄腳底＋1.3m");
            yield return FullChargeAndMeasure(first, second);
            PierceLog("E5a first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4"));
            float full = _hero.AttackDamage * BowChargedDamageMultiplier;
            Assert.AreEqual(full, _pierceD1, 1e-3f, "(a) 高處木樁 1 是直接目標：受 108");
            Assert.AreEqual(0f, _pierceD2, "(a) 第二目標碰撞盒最低點高於線＋0.15m：不被穿透");

            // (b) 木樁 1 正常（pivot 1）6m，第二木樁 pivot 1.8（碰撞 0.8～2.8，涵蓋線高度）10m：被穿透。
            yield return SetupBowGround(6f);
            first = _bowDummy;
            second = SpawnSecondStake(first, 0f, 10f, 1.8f);
            yield return null;
            PierceLog("E5b heroY=" + _hero.transform.position.y.ToString("F3") + " secondBoundsY=" + BoundsY(second));
            yield return FullChargeAndMeasure(first, second);
            PierceLog("E5b first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4"));
            Assert.AreEqual(full, _pierceD1, 1e-3f, "(b) 木樁 1 受 108");
            Assert.AreEqual(full, _pierceD2, 1e-3f, "(b) 碰撞盒涵蓋線高度：被穿透 108");
        }

        [UnityTest] // E6：英雄站在腳底 y=2 的平台上，水平線跟著英雄（y=3）；同平台上 6m／10m 兩木樁（pivot y=3）都中
        public IEnumerator Pierce_E6_HeroOnPlatform_LineFollowsHero_PiercesSamePlatformStake()
        {
            yield return Load();
            DummyTarget probe = Object.FindObjectOfType<DummyTarget>();
            Vector3 s = probe.transform.position;
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "PierceE6_Platform";
            platform.transform.position = new Vector3(s.x, 1f, s.z + 0f);
            platform.transform.localScale = new Vector3(6f, 2f, 20f);   // 頂面 y=2，涵蓋英雄（-6m）到第二木樁（+4m）
            yield return SetupBowGroundKeepScene(6f, 2f, 3f);
            DummyTarget first = _bowDummy;
            DummyTarget second = SpawnSecondStake(first, 0f, 10f);
            yield return null;
            float heroY = _hero.transform.position.y;
            PierceLog("E6 heroY=" + heroY.ToString("F3") + " platformTop=" + platform.GetComponent<Collider>().bounds.max.y.ToString("F2")
                + " firstBoundsY=" + BoundsY(first) + " secondBoundsY=" + BoundsY(second));
            Assert.AreEqual(2f, heroY, 0.05f, "前提：英雄腳底在平台頂 y=2");
            yield return FullChargeAndMeasure(first, second);
            PierceLog("E6 first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4"));
            float full = _hero.AttackDamage * BowChargedDamageMultiplier;
            Assert.AreEqual(full, _pierceD1, 1e-3f, "平台上木樁 1 受 108");
            Assert.AreEqual(full, _pierceD2, 1e-3f, "線高度跟著英雄：同平台第二木樁也受 108");
            Object.Destroy(platform);
        }

        // E6 用：不重載場景（平台已放好），其餘同 SetupBowGround。
        private IEnumerator SetupBowGroundKeepScene(float distance, float heroFeetY, float stakePivotY)
        {
            _pierceSpawnY = _hero.transform.position.y;
            _bowDummy = Object.FindObjectOfType<DummyTarget>();
            _bowDummy.Configure(1000f, _bowDummy.TargetFaction);
            Vector3 st = _bowDummy.transform.position;
            _bowDummy.transform.position = new Vector3(st.x, stakePivotY, st.z);
            Vector3 p = _bowDummy.transform.position + Vector3.back * distance;
            p.y = heroFeetY;
            yield return WarpAndSettle(p);
            Assert.AreEqual(0f, _lab.YawDegrees);
            SelectWeaponByTaps(2);
            yield return null; yield return null;
            Physics.SyncTransforms();
        }

        [UnityTest] // E8：英雄站地面，3m／7m 兩木樁同線——快速射擊、p=0.5 蓄力、劍、錘、鉤鎖都不穿透（後方 0 傷）；第一下傷害記錄（與 67691e0 比對）
        public IEnumerator Pierce_E8_GroundHero_NonFullAndOtherWeapons_DoNotPierce()
        {
            string[] modes = { "bowQuick", "bowHalf", "sword", "hammer", "grapple" };
            int[] taps = { 2, 2, 1, 3, 4 };
            for (int m = 0; m < modes.Length; m++)
            {
                yield return Load();
                float spawnY = _hero.transform.position.y;
                DummyTarget first = Object.FindObjectOfType<DummyTarget>();
                first.Configure(1000f, first.TargetFaction);
                Vector3 p = first.transform.position + Vector3.back * 3f;
                p.y = spawnY;
                yield return WarpAndSettle(p);
                SelectWeaponByTaps(taps[m]);
                yield return null; yield return null;
                DummyTarget second = SpawnSecondStake(first, 0f, 7f);
                yield return null;
                float h1 = first.Health, h2 = second.Health;
                if (modes[m] == "bowHalf") yield return HoldAttack(0.5);
                else TapAttack();
                yield return WaitForDrop(first, h1, 2f);
                float firstHit = h1 - first.Health;
                float secondAtFirstHit = h2 - second.Health;
                yield return WaitSeconds(1.5f);
                float secondLater = h2 - second.Health;
                _hero.ClearCombatTargetInPlace();
                PierceLog("E8 mode=" + modes[m] + " heroY=" + _hero.transform.position.y.ToString("F3") + " firstHit=" + firstHit.ToString("F4")
                    + " secondAtFirstHit=" + secondAtFirstHit.ToString("F4") + " secondAfter1.5s=" + secondLater.ToString("F4"));
                Assert.AreEqual(0f, secondAtFirstHit, modes[m] + "：後方木樁不得在第一下同時受傷（不穿透）");
                Assert.AreEqual(0f, secondLater, modes[m] + "：後方木樁 1.5s 內都不受傷");
                if (modes[m] == "bowQuick") Assert.AreEqual(_hero.AttackDamage, firstHit, 1e-3f, "快速射擊：60");
                Object.Destroy(second.gameObject);
            }
        }

        [UnityTest] // E9：英雄站地面穿透兩木樁——命中提示共 2、出手提示 1、箭 1 支；兩個傷害結算都屬於這一支箭
        public IEnumerator Pierce_E9_GroundHero_PierceTwo_TwoHitCues_OneReleaseCue_OneArrow()
        {
            yield return SetupBowGround(6f);
            DummyTarget first = _bowDummy;
            DummyTarget second = SpawnSecondStake(first, 0f, 10f);
            yield return null;
            int r0 = ReleaseCues(), c0 = HitCues(), s0 = LastArrow().Serial;
            yield return FullChargeAndMeasure(first, second);
            yield return WaitSeconds(0.7f);   // 16m／40m/s＝0.4s 飛完，提示都已出現
            int releases = ReleaseCues() - r0, cues = HitCues() - c0, arrows = LastArrow().Serial - s0;
            PierceLog("E9 heroY=" + _hero.transform.position.y.ToString("F3") + " first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4")
                + " releaseCues=" + releases + " hitCues=" + cues + " arrows=" + arrows);
            float full = _hero.AttackDamage * BowChargedDamageMultiplier;
            Assert.AreEqual(full, _pierceD1, 1e-3f, "木樁 1 受 108");
            Assert.AreEqual(full, _pierceD2, 1e-3f, "木樁 2 受 108（兩個傷害結算）");
            Assert.AreEqual(1, arrows, "穿透一發只算一支箭");
            Assert.AreEqual(1, releases, "出手提示 1");
            Assert.AreEqual(2, cues, "命中提示：被傷的兩個目標各一次共 2");
        }

        // ── 修訂 P3（覆審 review-pierce-r1）──

        // 碰撞體表面到穿透線軸（英雄 pivot＋1.0m、沿 dir 水平、長 16m；凍結檔規格值，不讀產品常數）的最短距離。
        private float PierceClearance(Component target, Vector3 dir)
        {
            Collider col = target.GetComponent<Collider>();
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

        [UnityTest] // E12（H1）：英雄站地面、準星 0°，A 在 3m／方位 9°（另一組 6°）由身體判定挑為直接目標；C 在英雄→A 延長線 10m（箭視覺正中穿過 C）→C 受 108
        public IEnumerator Pierce_E12_BodyPickedOffAxis_LineFollowsDirectTarget_CPierced()
        {
            float[] bearings = { 9f, 6f };
            for (int k = 0; k < bearings.Length; k++)
            {
                yield return SetupBowGround(3f);
                DummyTarget a = _bowDummy;
                Vector3 h = _hero.transform.position;
                float r = bearings[k] * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
                a.transform.position = new Vector3(h.x + dir.x * 3f, a.transform.position.y, h.z + dir.z * 3f);
                Physics.SyncTransforms();
                DummyTarget c = SpawnSecondStake(a, dir.x * 10f, dir.z * 10f);
                yield return null;
                Assert.AreEqual(_pierceSpawnY, _hero.transform.position.y, 1e-3f, "前提：英雄在真實出生高度");
                Assert.AreEqual(0f, _lab.YawDegrees, 1e-3f, "前提：準星朝 0°");
                float a0 = a.Health, c0 = c.Health;
                yield return HoldAttack(1.0);
                object picked = _lab.LastAimTarget;
                yield return WaitForDrop(a, a0, 2f);
                float dA = a0 - a.Health;
                _hero.ClearCombatTargetInPlace();
                yield return WaitSeconds(0.6f);
                float dC = c0 - c.Health;
                ArrowInfo arrow = LastArrow();
                Vector3 ad = Flat(arrow.End - arrow.Start);
                float arrowBearing = Mathf.Atan2(ad.x, ad.z) * Mathf.Rad2Deg;
                Vector3 cf = Flat(c.transform.position - arrow.Start);
                float cToArrow = ad.sqrMagnitude > 1e-6f ? Vector3.Cross(ad.normalized, cf).magnitude : -1f;
                PierceLog("E12 bearing=" + bearings[k] + " heroY=" + h.y.ToString("F3") + " picked=" + (ReferenceEquals(picked, a) ? "A" : picked == null ? "none" : "other")
                    + " dmgA=" + dA.ToString("F2") + " dmgC=" + dC.ToString("F2") + " arrowBearing=" + arrowBearing.ToString("F2")
                    + " C_toArrowLine=" + cToArrow.ToString("F3") + " C_toAimLine=" + (dir.x * 10f).ToString("F3"));
                float full = _hero.AttackDamage * BowChargedDamageMultiplier;
                Assert.AreSame(a, picked, "前提：A 由身體判定挑為直接目標（偏離準星 " + bearings[k] + "°）");
                Assert.AreEqual(full, dA, 1e-3f, "A 受 108");
                Assert.AreEqual(bearings[k], arrowBearing, 0.5f, "前提：箭視覺沿英雄→A");
                Assert.AreEqual(full, dC, 1e-3f, "箭視覺正中穿過 C：判定線也要沿英雄→直接目標，C 受 108");
                Object.Destroy(c.gameObject);
            }
        }

        [UnityTest] // E13（M2 鎖線半徑）：英雄站地面，第二木樁 10m、碰撞體表面離線軸 0.14m→中；0.16m→不中
        public IEnumerator Pierce_E13_LineRadius_Clearance014Hit_016Miss()
        {
            float[] clearances = { 0.14f, 0.16f };
            for (int i = 0; i < clearances.Length; i++)
            {
                yield return SetupBowGround(6f);
                DummyTarget first = _bowDummy;
                CapsuleCollider cap = first.GetComponent<CapsuleCollider>();
                float bodyR = cap.radius * Mathf.Max(first.transform.lossyScale.x, first.transform.lossyScale.z);
                DummyTarget second = SpawnSecondStake(first, bodyR + clearances[i], 10f);
                yield return null;
                float measured = PierceClearance(second, Vector3.forward);
                PierceLog("E13 target=" + clearances[i] + " measured=" + measured.ToString("F4") + " heroY=" + _hero.transform.position.y.ToString("F3") + " secondBoundsY=" + BoundsY(second));
                Assert.AreEqual(clearances[i], measured, 0.005f, "前提：第二木樁表面離線軸 " + clearances[i] + "m");
                yield return FullChargeAndMeasure(first, second);
                PierceLog("E13 clearance=" + clearances[i] + " first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4"));
                float full = _hero.AttackDamage * BowChargedDamageMultiplier;
                Assert.AreEqual(full, _pierceD1, 1e-3f, "木樁 1 受 108");
                if (i == 0) Assert.AreEqual(full, _pierceD2, 1e-3f, "clearance 0.14 < 半徑 0.15：中 108");
                else Assert.AreEqual(0f, _pierceD2, "clearance 0.16 > 半徑 0.15：不中");
            }
        }

        [UnityTest] // E14（M2 鎖線高度）：英雄站地面，第二目標正對線上、碰撞盒底部高 1.10m→中；1.20m→不中（線高 1.0＋半徑 0.15＝1.15）
        public IEnumerator Pierce_E14_LineHeight_Bottom110Hit_120Miss()
        {
            float[] bottoms = { 1.10f, 1.20f };
            for (int i = 0; i < bottoms.Length; i++)
            {
                yield return SetupBowGround(6f);
                DummyTarget first = _bowDummy;
                float halfH = first.GetComponent<Collider>().bounds.extents.y;
                DummyTarget second = SpawnSecondStake(first, 0f, 10f, _hero.transform.position.y + bottoms[i] + halfH);
                yield return null;
                float bottomRel = second.GetComponent<Collider>().bounds.min.y - _hero.transform.position.y;
                PierceLog("E14 target=" + bottoms[i] + " bottomRel=" + bottomRel.ToString("F4") + " heroY=" + _hero.transform.position.y.ToString("F3") + " secondBoundsY=" + BoundsY(second));
                Assert.AreEqual(bottoms[i], bottomRel, 0.005f, "前提：第二目標碰撞盒底部高 " + bottoms[i] + "m");
                yield return FullChargeAndMeasure(first, second);
                PierceLog("E14 bottom=" + bottoms[i] + " first=" + _pierceD1.ToString("F4") + " second=" + _pierceD2.ToString("F4"));
                float full = _hero.AttackDamage * BowChargedDamageMultiplier;
                Assert.AreEqual(full, _pierceD1, 1e-3f, "木樁 1 受 108");
                if (i == 0) Assert.AreEqual(full, _pierceD2, 1e-3f, "底部 1.10 < 1.15：中 108");
                else Assert.AreEqual(0f, _pierceD2, "底部 1.20 > 1.15：不中");
            }
        }
    }
}
#endif
