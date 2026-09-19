using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // Phase 2 批 2 步驟 B：0.5m 阻擋格點接上 Unity 之後的繞牆行為，對應計畫書 §5 V4 a~h 與 j。
    // V4 i（零配置量測窗口內含 Build＋Follow 與牆到期撤銷）在 ZeroAllocationTests.cs。
    //
    // 時間上限一律照 §5 的公式 T = 1.5 × 幾何最短繞行長度 ÷ 英雄移動速度：
    //   ．「幾何最短繞行長度」＝繞過**外擴後**（＋BodyRadius）牆角的真正最短折線，逐段算式寫在各測試的註解裡；
    //   ．速度讀 NavMeshAgent.speed——它由 HeroLocomotion.Configure 從 HeroTuningAsset.MoveSpeed 餵進來，
    //     寫死一個常數的話日後改手感速度會讓這些上限無聲變鬆。
    // 每個測試都先用 AssertWallGeometry 把推導所依賴的尺寸釘住（牆心／半寬／半厚／體半徑），
    // 任何一項漂掉就當場紅，避免註解裡的算式與場景悄悄脫節。
    public sealed class WallDetourPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";

        // §5 凍結的位置容差，實作中不得放寬。
        private const float ArriveTolerance = 0.3f;
        private const float WallPenetrationTolerance = 0.03f;
        private const float StraightLineLateralTolerance = 0.05f;

        private HeroController _hero;
        private HeroLocomotion _locomotion;
        private NavMeshAgent _agent;
        private ScriptedInput _input;
        private RuneCaster _caster;
        private RuneWall[] _pool;
        private Phase1Bootstrap _bootstrap;
        private BlockGrid _grid;

        private float MoveSpeed => _agent.speed;
        private float BodyRadius => _agent.radius;

        private IEnumerator Setup(Vector3 heroStart, Quaternion heroRotation, RuneTuning tuning)
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null; // Awake／OnEnable／Start（含 Phase1Bootstrap 的格點組裝）全部跑完

            _hero = Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(_hero, "場景裡找不到 HeroController");
            _locomotion = _hero.GetComponent<HeroLocomotion>();
            Assert.IsNotNull(_locomotion, "英雄身上沒有 HeroLocomotion");
            _agent = _hero.GetComponent<NavMeshAgent>();

            Assert.IsTrue(_agent.Warp(heroStart), "無法把英雄放到 " + heroStart);
            _hero.transform.position = _agent.nextPosition; // 不同步的話 SyncAgent 下一幀就把 Warp 拉回去
            _hero.transform.rotation = heroRotation;

            _input = new ScriptedInput();
            _hero.Initialize(_input, Object.FindObjectOfType<CombatFeedbackService>(), Camera.main);

            _caster = Object.FindObjectOfType<RuneCaster>();
            Assert.IsNotNull(_caster, "場景缺少 RuneCaster");
            _pool = Object.FindObjectsOfType<RuneWall>();
            Assert.GreaterOrEqual(_pool.Length, 3, "石牆池應預建 3 面");
            _caster.Initialize(_input, _input, _hero.transform, Camera.main, tuning, _pool, _hero.HeroFaction);

            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            Assert.IsNotNull(_bootstrap, "場景缺少 Phase1Bootstrap");
            _grid = _bootstrap.NavGrid;
            Assert.IsNotNull(_grid, "Phase1Bootstrap 沒有建立阻擋格點");

            yield return null;
        }

        private static RuneWall FirstAlive(RuneWall[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) return pool[i];
            return null;
        }

        // ───────────────────── 測試自己算的幾何（不呼叫實作那一份）─────────────────────

        private struct Obb
        {
            public Vector2 Center;
            public Vector2 Normal;   // 厚度軸（＝立方體的 +Z）
            public float HalfWidth;  // 沿牆方向
            public float HalfThickness;
        }

        private static Obb ReadObb(Component wall)
        {
            BoxCollider box = wall.GetComponent<BoxCollider>();
            Transform t = box.transform;
            Vector3 center = t.TransformPoint(box.center);
            Vector3 scale = t.lossyScale;
            Vector3 forward = t.forward;
            return new Obb
            {
                Center = new Vector2(center.x, center.z),
                Normal = new Vector2(forward.x, forward.z).normalized,
                HalfWidth = Mathf.Abs(box.size.x * scale.x) * 0.5f,
                HalfThickness = Mathf.Abs(box.size.z * scale.z) * 0.5f
            };
        }

        // 點到 OBB 的最短距離（點落在盒內回 0）。
        private static float DistanceToObb(Vector3 point, Obb obb)
        {
            Vector2 tangent = new Vector2(-obb.Normal.y, obb.Normal.x);
            Vector2 d = new Vector2(point.x - obb.Center.x, point.z - obb.Center.y);
            float outT = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, tangent)) - obb.HalfWidth);
            float outN = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, obb.Normal)) - obb.HalfThickness);
            return Mathf.Sqrt(outT * outT + outN * outN);
        }

        // 釘住推導所依賴的尺寸：任何一項漂掉，註解裡的 T 算式就不再成立，當場紅。
        private void AssertWallGeometry(Obb obb, Vector2 expectedCenter, Vector2 expectedNormal)
        {
            Assert.AreEqual(expectedCenter.x, obb.Center.x, 0.02f, "牆心 x 與推導不符");
            Assert.AreEqual(expectedCenter.y, obb.Center.y, 0.02f, "牆心 z 與推導不符");
            Assert.Less(Vector2.Angle(obb.Normal, expectedNormal), 1f, "牆面法線與推導不符：" + obb.Normal);
            Assert.AreEqual(2f, obb.HalfWidth, 0.01f, "牆半寬與推導不符");
            Assert.AreEqual(0.3f, obb.HalfThickness, 0.01f, "牆半厚與推導不符");
            Assert.AreEqual(0.35f, BodyRadius, 0.001f, "體半徑（＝格點外擴量）與推導不符");
            Assert.AreEqual(5.5f, MoveSpeed, 0.001f, "移動速度與推導不符");
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // 英雄原地不動了嗎：再跑 12 幀，總位移不得超過 2cm。
        private IEnumerator AssertHeroHasStopped()
        {
            Vector3 before = _hero.transform.position;
            for (int i = 0; i < 12; i++) yield return null;
            Assert.LessOrEqual(PlanarDistance(_hero.transform.position, before), 0.02f,
                "英雄停下之後仍在移動：" + before + " → " + _hero.transform.position);
        }

        // 在英雄正前方 4m 立一面符印牆（極速施放），回傳它的 OBB。
        private IEnumerator QuickCastWall(Vector2 expectedCenter, Vector2 expectedNormal)
        {
            _input.RuneQuickCast();
            yield return null;
            RuneWall wall = FirstAlive(_pool);
            Assert.IsNotNull(wall, "石牆未成形，繞牆測試沒有意義");
            _castWall = wall;
            _castObb = ReadObb(wall);
            AssertWallGeometry(_castObb, expectedCenter, expectedNormal);
            Assert.Greater(_grid.BlockedCount, 0, "石牆沒有登記進阻擋格點");
        }

        private RuneWall _castWall;
        private Obb _castObb;

        // ───────────────────────────── V4 a ─────────────────────────────

        // V4-a 繞牆＋雙向對照。
        // 幾何：英雄 (0,0)、牆心 (0,4) 法線 +Z、半寬 2／半厚 0.3，外擴 0.35
        //   → 英雄圓心不可進入的矩形 x∈[−2.35, 2.35]、z∈[3.35, 4.65]。
        // 真正最短繞行折線（左右對稱，取右邊）：(0,0) →(2.35,3.35) →(2.35,4.65) →(0,8)
        //   ① √(2.35² + 3.35²) = √16.745 = 4.09207
        //   ② 4.65 − 3.35                = 1.30000
        //   ③ √(2.35² + 3.35²)           = 4.09207   （(2.35,4.65)→(0,8)：Δx 2.35、Δz 3.35）
        //   合計 L = 9.48414 m  →  T = 1.5 × 9.48414 ÷ 5.5 = 2.58658 s
        // 對照組用同一場景、同一 T、同一目的地，只把導航器拔掉（＝v0.3.2 的頂牆行為）。
        [UnityTest]
        public IEnumerator V4a_HeroDetoursAroundTheWall_WhileTheNavigatorOffControlDoesNot()
        {
            const float shortestDetour = 9.48414f;
            Vector3 destination = new Vector3(0f, 0f, 8f);

            // ── 實驗組：格點接上 ──
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));

            float limit = 1.5f * shortestDetour / MoveSpeed;
            _input.TapGround(destination);

            float deadline = Time.time + limit;
            bool arrived = false;
            while (Time.time <= deadline)
            {
                if (PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance) { arrived = true; break; }
                yield return null;
            }
            Assert.IsTrue(arrived, "格點接上時，" + limit + "s 內應繞過石牆抵達目的地，實際停在 " + _hero.transform.position);

            // ── 對照組：同一場景、同一時間上限，只拔掉導航器 ──
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            _locomotion.SetNavigator(null, 0f);
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));

            _input.TapGround(destination);
            deadline = Time.time + limit;
            bool arrivedWithoutNavigator = false;
            while (Time.time <= deadline)
            {
                if (PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance)
                {
                    arrivedWithoutNavigator = true;
                    break;
                }
                yield return null;
            }
            Assert.IsFalse(arrivedWithoutNavigator,
                "對照失敗：沒有格點也到得了目的地，代表這條路本來就繞得過去，實驗組那條綠燈沒有鑑別力");
            Assert.Greater(_hero.transform.position.z, 1f,
                "對照組的英雄根本沒起步（" + _hero.transform.position + "），沒有測到頂牆行為");
        }

        // ───────────────────────────── V4 b ─────────────────────────────

        // V4-b 不穿牆：V4-a 的全程每一幀，英雄圓心到牆 OBB 的距離 ≥ BodyRadius − 0.03。
        [UnityTest]
        public IEnumerator V4b_HeroNeverPenetratesTheWall_WhileDetouring()
        {
            const float shortestDetour = 9.48414f; // 與 V4-a 同一推導
            Vector3 destination = new Vector3(0f, 0f, 8f);

            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));

            float limit = 1.5f * shortestDetour / MoveSpeed;
            _input.TapGround(destination);

            float deadline = Time.time + limit;
            bool arrived = false;
            float closest = float.MaxValue;
            while (Time.time <= deadline)
            {
                float distance = DistanceToObb(_hero.transform.position, _castObb);
                if (distance < closest) closest = distance;
                Assert.GreaterOrEqual(distance, BodyRadius - WallPenetrationTolerance,
                    "英雄圓心離牆面只剩 " + distance + "m（下限 " + (BodyRadius - WallPenetrationTolerance) + "）：" + _hero.transform.position);
                if (PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance) { arrived = true; break; }
                yield return null;
            }

            Assert.IsTrue(arrived, "沒有走完全程，這條不穿牆的斷言涵蓋不到繞牆段");
            Assert.Less(closest, 1.5f, "英雄全程離牆超過 1.5m，根本沒有貼近牆走過，這條斷言沒有鑑別力（最近 " + closest + "m）");
        }

        // ───────────────────────────── V4 c ─────────────────────────────

        // V4-c 點牆腳：目的地＝牆心正下方的地板（牆在 Ignore Raycast 層，射線打到牆底下的地板，§4 假設 7）。
        // 目的地格 (x=0,z=4) 是 Blocked → 退到「最近可達點」。
        //   §6 R8／R1a 定義（審查 H2：舊定義只看「離目的地最近」，會把英雄送到牆的另一側）：
        //   dMin＝連通區內格心到目的地的最小歐氏距離（南北兩側都是 1.27475）；候選帶 B＝距離 ≤ dMin＋一格對角線
        //   （1.27475＋0.70711 = 1.98186）的格；W＝B 內路徑成本最低者（決定哪一側）；
        //   最後在 B 內成本 ≤ cost(W)＋28 的格中取**離目的地最近**者。
        //   英雄在 (0,0)（格 (40,40)）：W＝格 (40,44)（4 步直走、成本 40）＝南側；
        //   南側離目的地最近的一排是 z=2.75（dMin），其中 (40,45)（成本 50 ≤ 40+28）比 (39,45)（成本 54）低
        //   → 停點＝格心 (0.25, 2.75)。北側那排要繞過整面牆，成本遠超過 68，不在寬限內。
        // 幾何最短繞行長度＝直線 (0,0)→(0.25,2.75) ＝ √(0.0625 + 7.5625) = 2.76134 m
        //   → T = 1.5 × 2.76134 ÷ 5.5 = 0.75309 s
        // 停點容差＝§5 原文：牆半厚 0.3 ＋ 外擴 0.35 ＋ 一格對角線 0.5√2 = 1.35711 m（實際 1.27475 m）
        [UnityTest]
        public IEnumerator V4c_TappingTheWallFoot_StopsAtTheNearestReachablePoint()
        {
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));

            Vector3 heroStart = _hero.transform.position;
            Vector3 destination = new Vector3(_castObb.Center.x, 0f, _castObb.Center.y); // 牆腳
            float limit = 1.5f * 2.76134f / MoveSpeed;

            _input.TapGround(destination);
            yield return null; // L5：剛下指令的那一幀 _hasOrder 還是 false，HasArrived 會假性回 true
            yield return null;

            float deadline = Time.time + limit;
            bool sawMoving = false;
            while (Time.time <= deadline)
            {
                if (_hero.StateMachine.CurrentState == PlayerState.Moving) sawMoving = true;
                if (_hero.StateMachine.CurrentState == PlayerState.Idle && _locomotion.HasArrived) break;
                yield return null;
            }

            Assert.IsTrue(sawMoving, "英雄從頭到尾沒有進入 Moving：這個測試沒有測到任何移動");
            Assert.IsTrue(_locomotion.HasArrived, "T=" + limit + "s 內 HasArrived 仍為 false：英雄還在頂牆或發呆");
            Assert.AreEqual(PlayerState.Idle, _hero.StateMachine.CurrentState, "狀態機沒有回到 Idle");
            yield return AssertHeroHasStopped();

            Vector3 stop = _hero.transform.position;
            float stopTolerance = 0.3f + 0.35f + 0.5f * Mathf.Sqrt(2f);
            Assert.LessOrEqual(PlanarDistance(stop, destination), stopTolerance,
                "停點離牆腳太遠：" + stop + "（上限 " + stopTolerance + "m）");

            // §6 R1 新增：停點必須與英雄起點在牆的同一側（禁區 z∈[3.35,4.65]）
            Assert.Less(stop.z, 3.35f, "停點跑到牆的另一側去了：" + stop);
            Assert.AreEqual(0.25f, stop.x, ArriveTolerance, "停點不是 R1a 定義的那一格格心 x：" + stop);
            Assert.AreEqual(2.75f, stop.z, ArriveTolerance, "停點不是 R1a 定義的那一格格心 z：" + stop);
            Assert.Less(heroStart.z, 3.35f, "前置條件：英雄起點本來就在牆的南側");
        }

        // ───────────────────────────── V4 d ─────────────────────────────

        // V4-d 角落圍死：45° 牆封住場地 +X/+Z 角，目的地在三角形內、英雄在外。
        // 牆心 (18,18)、法線 (0.7071,0.7071)：英雄站在 (18,18) − 4×(0.7071,0.7071) = (15.1716,15.1716) 極速施放即得。
        //   外擴後的 OBB 兩個外角落在 (20.121,16.798) 與 (16.798,20.121)——都在格點 (±20) 之外，
        //   所以這面牆連同「界外一律 Blocked」把角落封死；實體上牆端到邊界牆內面只剩 0.174m，體半徑 0.35 也鑽不過去。
        // 解析後的替代點（§6 R8／R1a）：
        //   dMin＝2.47487（格心 (17.25,17.25)，x+z=34.5 是還沒被外擴蓋到的最後一排）；
        //   候選帶 B＝距離 ≤ 2.47487＋0.70711 = 3.18198 的格；英雄格 (70,70)＝格心 (15.25,15.25)。
        //   W＝(16.75,16.75)（格 (73,73)，3 個斜步＝成本 42，B 內最低）——決定了「停在牆的外側」。
        //   最後在 B 內成本 ≤ 42＋28 = 70 的格中取離目的地最近者：B 內成本分別是 42／52／56／58／62／68，
        //   全部在寬限內，其中離 (19,19) 最近的是 (17.25,17.25)（2.47487）→ 替代點回到 (17.25,17.25)。
        // 幾何最短繞行長度＝直線 (15.1716,15.1716)→(17.25,17.25) ＝ √2 × 2.07843 = 2.93935 m
        //   → T = 1.5 × 2.93935 ÷ 5.5 = 0.80164 s
        // 「三角形」＝被牆切下來的角落區，以牆的中心平面 x+z = 36 為界。
        [UnityTest]
        public IEnumerator V4d_WallSealingTheCorner_StopsOutside_AndNeverEntersTheTriangle()
        {
            Vector3 heroStart = new Vector3(18f - 2.828427f, 0f, 18f - 2.828427f);
            yield return Setup(heroStart, Quaternion.Euler(0f, 45f, 0f), new RuneTuning());
            yield return QuickCastWall(new Vector2(18f, 18f), new Vector2(0.70710678f, 0.70710678f));

            Vector3 destination = new Vector3(19f, 0f, 19f);
            float limit = 1.5f * 2.93935f / MoveSpeed;

            _input.TapGround(destination);
            yield return null; // L5：剛下指令那一幀 _hasOrder 仍為 false，HasArrived 會假性回 true
            yield return null;

            float deadline = Time.time + limit;
            bool sawMoving = false;
            while (Time.time <= deadline)
            {
                Vector3 p = _hero.transform.position;
                Assert.Less(p.x + p.z, 36f, "英雄進入了被封死的三角形：" + p);
                Assert.GreaterOrEqual(DistanceToObb(p, _castObb), BodyRadius - WallPenetrationTolerance,
                    "英雄穿進了 45° 牆：" + p);
                if (_hero.StateMachine.CurrentState == PlayerState.Moving) sawMoving = true;
                if (_hero.StateMachine.CurrentState == PlayerState.Idle && _locomotion.HasArrived) break;
                yield return null;
            }

            Assert.IsTrue(sawMoving, "英雄從頭到尾沒有進入 Moving：這個測試沒有測到任何移動");
            Assert.IsTrue(_locomotion.HasArrived, "T=" + limit + "s 內 HasArrived 仍為 false");
            Assert.AreEqual(PlayerState.Idle, _hero.StateMachine.CurrentState, "狀態機沒有回到 Idle");
            yield return AssertHeroHasStopped();

            Vector3 end = _hero.transform.position;
            Assert.Less(end.x + end.z, 36f, "英雄最後停在三角形裡：" + end);
        }

        // ───────────────────────────── V4 e ─────────────────────────────

        // V4-e 牆消失後重新解析：英雄正走向替代點（V4-c 的 (0.25,2.75)）時把牆收掉 → 最終抵達**原始**目的地 (0,4)。
        // 最長的合法路線＝先走完整段替代路徑、再從替代點走到原目的地：
        //   ① (0,0)→(0.25,2.75)   = √(0.0625 + 7.5625) = 2.76134
        //   ② (0.25,2.75)→(0,4)   = √(0.0625 + 1.5625) = 1.27475
        //   合計 L = 4.03609 m  →  T = 1.5 × 4.03609 ÷ 5.5 = 1.10075 s（從下指令那一刻起算）
        //
        // 鑑別力：光看「最後有沒有走到 (0,4)」是不夠的——牆早早被收掉的話，連格點都沒接上的英雄也會直直走到。
        // 所以前後各釘一次 NavMeshAgent 的實際目的地：收牆前必須是替代點、收牆後必須換回原始目的地。
        // 前者在「格點沒接上」時紅，後者在「重新解析那一段被拿掉」時紅（英雄會停在替代點，離原目的地 1.27m）。
        [UnityTest]
        public IEnumerator V4e_CollapsingTheWallMidRoute_ResumesToTheOriginalDestination()
        {
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            int blockedBeforeWall = _grid.BlockedCount; // 只有兩面測試牆時的基準
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));
            Assert.AreEqual(blockedBeforeWall + 40, _grid.BlockedCount, "一面符印牆應登記 40 格");

            Vector3 destination = new Vector3(_castObb.Center.x, 0f, _castObb.Center.y);
            float limit = 1.5f * 4.03609f / MoveSpeed;

            _input.TapGround(destination);
            float deadline = Time.time + limit;

            // 前置：這一刻英雄真的被導去替代點，不是直接朝原目的地走（否則後面那條綠燈毫無意義）
            Vector3 substituted = _agent.destination;
            Assert.AreEqual(0.25f, substituted.x, 0.1f, "目的地沒有被換成替代點（實際 " + substituted + "）");
            Assert.AreEqual(2.75f, substituted.z, 0.1f, "目的地沒有被換成替代點（實際 " + substituted + "）");

            bool collapsed = false;
            bool arrived = false;
            while (Time.time <= deadline)
            {
                // 「正走向替代點」：等他真的離開起點之後才收牆，才算測到「持有指令中重新解析」這條路徑
                if (!collapsed && _hero.transform.position.z > 0.5f)
                {
                    collapsed = true;
                    _castWall.CollapseWall(false);
                    Assert.IsFalse(_castWall.IsAlive, "CollapseWall 之後石牆仍存活");
                }
                if (collapsed && PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance)
                {
                    arrived = true;
                    break;
                }
                yield return null;
            }

            Assert.IsTrue(collapsed, "英雄沒有起步，牆根本沒被收掉，這個測試沒有測到重新解析");
            Assert.IsTrue(arrived, "牆消失後 T=" + limit + "s 內沒有走到原始目的地，停在 " + _hero.transform.position);
            Vector3 resolved = _agent.destination;
            Assert.AreEqual(destination.x, resolved.x, 0.1f, "牆消失後目的地沒有換回原始值（實際 " + resolved + "）");
            Assert.AreEqual(destination.z, resolved.z, 0.1f, "牆消失後目的地沒有換回原始值（實際 " + resolved + "）");
            Assert.AreEqual(blockedBeforeWall, _grid.BlockedCount,
                "石牆消失後 BlockedCount 應該回到放牆前的值（每一條離場路徑都要撤銷登記）");
        }

        // ───────────────────────────── V4 f ─────────────────────────────

        // V4-f 推出：在英雄腳下啟用一面牆。
        //   牆心 (0,0)、法線 +Z、半寬 2／半厚 0.3、外擴 0.35 → 格點上 x 格 35~44（x∈[−2.5,2.5]）、z 格 38~41（z∈[−1,1]）。
        //   最近的空格格心＝(−0.25, −1.25)（同距離取索引較小者：cz=37 先於 cz=42、cx=39 先於 cx=40）。
        // 推出後下令走到牆另一側的 (−3,3)：
        //   外擴後的牆佔 x∈[−2.35,2.35]、z∈[−0.65,0.65]，最短折線從左邊繞
        //   ① (−0.25,−1.25)→(−2.35,−0.65) = √(2.1² + 0.6²) = 2.18403
        //   ② (−2.35,−0.65)→(−3,3)         = √(0.65² + 3.65²) = 3.70742   （第 2 個角不必繞：x 一路 ≤ −2.35）
        //   合計 L = 5.89145 m  →  T = 1.5 × 5.89145 ÷ 5.5 = 1.60676 s
        [UnityTest]
        public IEnumerator V4f_WallSpawnedOnTheHero_EjectsHim_AndHeStillWalksAround()
        {
            RuneTuning tuning = new RuneTuning();
            yield return Setup(Vector3.zero, Quaternion.identity, tuning);

            Vector3 before = _hero.transform.position;
            RuneWall wall = _pool[0];
            wall.Activate(new Vector3(before.x, tuning.WallHeight * 0.5f, before.z), Quaternion.identity,
                          _hero.HeroFaction, null, -1);
            yield return null;

            Obb obb = ReadObb(wall);
            AssertWallGeometry(obb, new Vector2(0f, 0f), new Vector2(0f, 1f));

            // 對照：不推出的話，同一幀的英雄位置就是重疊的
            Assert.IsTrue(BlockGrid.CircleOverlapsBox(before.x, before.z, BodyRadius,
                              obb.Center.x, obb.Center.y, obb.Normal.x, obb.Normal.y, obb.HalfWidth, obb.HalfThickness),
                "對照失敗：牆根本沒有壓到英雄原本站的位置，推出這條斷言沒有鑑別力");

            Vector3 after = _hero.transform.position;
            Assert.IsFalse(BlockGrid.CircleOverlapsBox(after.x, after.z, BodyRadius,
                               obb.Center.x, obb.Center.y, obb.Normal.x, obb.Normal.y, obb.HalfWidth, obb.HalfThickness),
                "牆立起來之後英雄仍與牆重疊：" + after);
            Assert.IsTrue(_agent.isOnNavMesh, "推出之後英雄掉出 NavMesh");
            Assert.AreEqual(-0.25f, after.x, 0.01f, "推出的落點不是最近的空格格心 x");
            Assert.AreEqual(-1.25f, after.z, 0.01f, "推出的落點不是最近的空格格心 z");

            Vector3 destination = new Vector3(-3f, 0f, 3f);
            float limit = 1.5f * 5.89145f / MoveSpeed;
            _input.TapGround(destination);

            float deadline = Time.time + limit;
            bool arrived = false;
            while (Time.time <= deadline)
            {
                Assert.GreaterOrEqual(DistanceToObb(_hero.transform.position, obb), BodyRadius - WallPenetrationTolerance,
                    "繞行途中穿進了牆：" + _hero.transform.position);
                if (PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance) { arrived = true; break; }
                yield return null;
            }
            Assert.IsTrue(arrived, "推出之後 T=" + limit + "s 內沒有繞到牆的另一側，停在 " + _hero.transform.position);
        }

        // ───────────────────────────── V4 g ─────────────────────────────

        // V4-g 追擊繞牆＋對照：木樁挪到 (0,12)、牆立在 (0,4)，下攻擊指令。
        //   攻擊距離 5m，所以英雄只要走進以 (0,12) 為心、半徑 5 的圓內就開得了刀。
        //   最短折線：(0,0) →(2.35,3.35) →(2.35,4.65) →圓周
        //   ① √(2.35² + 3.35²)                         = 4.09207
        //   ② 1.30000
        //   ③ |(2.35,4.65)−(0,12)| − 5 = √59.545 − 5   = 2.71654
        //   合計 L = 8.10861 m  →  T = 1.5 × 8.10861 ÷ 5.5 = 2.21144 s，再加一次攻擊週期 0.8s
        // 對照組：同一場景、同一時間窗，只拔掉導航器——英雄頂在牆上，離木樁 8.65m，永遠開不了刀。
        [UnityTest]
        public IEnumerator V4g_ChasingAroundTheWall_DamagesTheDummy_WhileTheNavigatorOffControlDoesNot()
        {
            float attackCycle = new CombatTuning().AttackPeriodSeconds;

            // ── 實驗組 ──
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(dummy, "場景缺少木樁");
            dummy.transform.position = new Vector3(0f, 1f, 12f);
            Assert.AreEqual(5f, _hero.AttackRange, 0.001f, "攻擊距離與推導不符");
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));

            float window = 1.5f * 8.10861f / MoveSpeed + attackCycle;
            float healthBefore = dummy.Health;
            _input.TapTarget(dummy);

            float deadline = Time.time + window;
            bool damaged = false;
            while (Time.time <= deadline)
            {
                if (dummy.Health < healthBefore) { damaged = true; break; }
                yield return null;
            }
            Assert.IsTrue(damaged, "格點接上時，" + window + "s 內應繞過石牆打到木樁，英雄停在 " + _hero.transform.position);

            // ── 對照組：同一場景、同一時間窗，只拔掉導航器 ──
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            _locomotion.SetNavigator(null, 0f);
            dummy = Object.FindObjectOfType<DummyTarget>();
            dummy.transform.position = new Vector3(0f, 1f, 12f);
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));

            healthBefore = dummy.Health;
            _input.TapTarget(dummy);
            deadline = Time.time + window;
            bool damagedWithoutNavigator = false;
            while (Time.time <= deadline)
            {
                if (dummy.Health < healthBefore) { damagedWithoutNavigator = true; break; }
                yield return null;
            }
            Assert.IsFalse(damagedWithoutNavigator,
                "對照失敗：沒有格點也打得到木樁，代表這面牆本來就沒擋住追擊路線");
            Assert.Greater(_hero.transform.position.z, 1f,
                "對照組的英雄根本沒起步（" + _hero.transform.position + "），沒有測到頂牆行為");
        }

        // ───────────────────────────── V4 h ─────────────────────────────

        // V4-h 無牆擋路時走直線：(0,0)→(6,8)，全程有視線。
        // r1 對抗審查 L2（§6 R7）更正距離：最近的是 TestWall_B（x∈[6.7,7.3]、z∈[1,5]）外擴 0.35 後的角落
        // (6.35,5.35)，它到這條線的垂直距離是 |6.35×0.8 − 5.35×0.6| = 1.87m（未外擴的角落 (6.7,5) 是 2.36m）——
        // 原註解寫「3m 以上」不實。1.87m 仍遠大於體半徑 0.35，這條路線確實全程有視線，測試本身有效。
        // 沒有繞行，所以「幾何最短繞行長度」就是直線 10m → T = 1.5 × 10 ÷ 5.5 = 2.72727 s。
        // 側向偏移＝點到「起點→目的地」那條直線的垂直距離，門檻 0.05m（走格點就會變成 45° 鋸齒，一定超標）。
        [UnityTest]
        public IEnumerator V4h_WithNothingInTheWay_TheHeroWalksAStraightLine()
        {
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());

            Vector3 start = _hero.transform.position;
            Vector3 destination = new Vector3(6f, 0f, 8f);
            Vector2 direction = new Vector2(destination.x - start.x, destination.z - start.z).normalized;

            float limit = 1.5f * 10f / MoveSpeed;
            _input.TapGround(destination);

            float deadline = Time.time + limit;
            bool arrived = false;
            float maxLateral = 0f;
            float travelled = 0f;
            while (Time.time <= deadline)
            {
                Vector3 p = _hero.transform.position;
                float lateral = Mathf.Abs((p.x - start.x) * direction.y - (p.z - start.z) * direction.x);
                if (lateral > maxLateral) maxLateral = lateral;
                travelled = PlanarDistance(p, start);
                if (PlanarDistance(p, destination) <= ArriveTolerance) { arrived = true; break; }
                yield return null;
            }

            Assert.IsTrue(arrived, "沒有牆擋路卻走不到目的地，停在 " + _hero.transform.position);
            Assert.Greater(travelled, 9f, "英雄幾乎沒走（" + travelled + "m），側向偏移的斷言沒有鑑別力");
            Assert.Less(maxLateral, StraightLineLateralTolerance,
                "有視線的路線上出現了 " + maxLateral + "m 的側向偏移：移動被改走格點了");
        }

        // ───────────────────────────── V4 j ─────────────────────────────

        // V4-j GRID 疊圖：預設 Renderer 關；切到 ON → 開，且 Mesh 的四邊形數 == BlockedCount；放一面牆後數字跟著變。
        // 四邊形數直接讀 Mesh 的頂點數（每格 4 個頂點），不信元件自己回報的 QuadCount。
        [UnityTest]
        public IEnumerator V4j_GridDebugOverlay_IsOffByDefault_AndTracksTheBlockedCells()
        {
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());

            NavGridDebugView view = Object.FindObjectOfType<NavGridDebugView>();
            Assert.IsNotNull(view, "場景缺少 NavGridDebugView（SceneBuilder 應預建）");
            MeshRenderer renderer = view.GetComponent<MeshRenderer>();
            Assert.IsFalse(renderer.enabled, "GRID 疊圖預設應為關閉");
            Assert.IsFalse(view.Visible, "GRID 疊圖預設應為關閉");

            Assert.Greater(_grid.BlockedCount, 0, "場上兩面測試牆應已登記進格點，否則這條斷言沒有鑑別力");

            view.Visible = true;
            yield return null;
            yield return null; // LateUpdate 重填

            Assert.IsTrue(renderer.enabled, "切到 ON 之後 Renderer 應開啟");
            Mesh mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh, "疊圖沒有 Mesh");
            int blocked = _grid.BlockedCount;
            Assert.AreEqual(blocked, view.QuadCount, "疊圖的四邊形數與 BlockedCount 不符");
            Assert.AreEqual(blocked * 4, mesh.vertexCount, "Mesh 的頂點數應為 BlockedCount × 4");

            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));
            yield return null;
            yield return null;

            Assert.AreEqual(blocked + 40, _grid.BlockedCount, "多一面符印牆應多 40 格");
            Assert.AreEqual(_grid.BlockedCount, view.QuadCount, "放牆後疊圖的四邊形數沒有跟著變");
            Assert.AreEqual(_grid.BlockedCount * 4, mesh.vertexCount, "放牆後 Mesh 的頂點數沒有跟著變");
        }

        // ───────────────────────────── V4 k（§6 R3）─────────────────────────────

        // V4-k 貼邊的牆：外擴後的牆端蓋到場地邊界時，格點最外一圈是「自由但英雄到不了」的地方，
        // 向量場會把英雄導進去頂死（審查 H1，Probe2 實跑：英雄停在 (19.44,−1.09) 永不抵達）。
        // §6 R3：最外一圈必須是 Blocked——不變量是「每一個非 Blocked 格的格心都落在『邊界牆內面 − BodyRadius』以內」，
        // 內面由場景裡實際的邊界 BoxCollider 量出來，不寫死 19.45。
        //
        // 幾何：英雄 (17.15,−4)、牆心 (17.15,0) 法線 +Z、半寬 2／半厚 0.3，外擴 0.35
        //   → 禁區 x∈[14.80, 19.50]、z∈[−0.65, 0.65]。東端 19.50 已經超過英雄中心的物理上限（內面 19.80 − 0.35 = 19.45），
        //     東側沒有任何縫可鑽，唯一的路是繞西端。
        // 真正最短繞行折線：(17.15,−4) →(14.8,−0.65) →(14.8,0.65) →(17.15,4)
        //   ① √(2.35² + 3.35²) = 4.09207
        //   ② 0.65 − (−0.65)   = 1.30000
        //   ③ √(2.35² + 3.35²) = 4.09207
        //   合計 L = 9.48414 m  →  T = 1.5 × 9.48414 ÷ 5.5 = 2.58658 s
        // 對照：同一場景、同一 T，換成一張「最外圈沒有登記」的裸格點（只有這面牆進去）——英雄應該卡在邊界夾角到不了。
        [UnityTest]
        public IEnumerator V4k_WallReachingTheArenaEdge_DoesNotWedgeTheHeroAgainstTheBoundary()
        {
            const float shortestDetour = 9.48414f;
            Vector3 heroStart = new Vector3(17.15f, 0f, -4f);
            Vector3 destination = new Vector3(17.15f, 0f, 4f);

            // ── 實驗組：正式格點（最外圈已登記）──
            yield return Setup(heroStart, Quaternion.identity, new RuneTuning());
            yield return QuickCastWall(new Vector2(17.15f, 0f), new Vector2(0f, 1f));

            AssertEveryFreeCellIsReachableByTheBody();

            float limit = 1.5f * shortestDetour / MoveSpeed;
            _input.TapGround(destination);

            float deadline = Time.time + limit;
            bool arrived = false;
            while (Time.time <= deadline)
            {
                if (PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance) { arrived = true; break; }
                yield return null;
            }
            Assert.IsTrue(arrived, "最外圈登記之後，" + limit + "s 內應繞過牆的西端抵達目的地，實際停在 " + _hero.transform.position);

            // ── 對照組：最外圈不登記（＝修復前的格點）──
            yield return Setup(heroStart, Quaternion.identity, new RuneTuning());
            yield return QuickCastWall(new Vector2(17.15f, 0f), new Vector2(0f, 1f));

            NavGridTuning bareTuning = new NavGridTuning();
            BlockGrid bare = new BlockGrid(bareTuning.OriginX, bareTuning.OriginZ, bareTuning.CellSize,
                                           bareTuning.Columns, bareTuning.Rows);
            _castWall.SetNavGrid(bare, bareTuning.BodyRadius); // 只有這面牆進裸格點，最外圈刻意留空
            _locomotion.SetNavigator(new GridNavigator(bare, bareTuning), bareTuning.BodyRadius);

            _input.TapGround(destination);
            deadline = Time.time + limit;
            bool arrivedWithoutBoundary = false;
            while (Time.time <= deadline)
            {
                if (PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance)
                {
                    arrivedWithoutBoundary = true;
                    break;
                }
                yield return null;
            }
            Assert.IsFalse(arrivedWithoutBoundary,
                "對照失敗：最外圈不登記也走得到，代表這條路本來就不會被邊界夾住，實驗組那條綠燈沒有鑑別力");
        }

        // §6 R3 的不變量：每一個非 Blocked 格的格心，都落在「邊界牆內面 − BodyRadius」以內。
        // 內面由場景裡實際的四面邊界 BoxCollider 量出來（不寫死 19.45），日後改 BodyRadius 重建場景仍然成立。
        private void AssertEveryFreeCellIsReachableByTheBody()
        {
            GameObject boundary = GameObject.Find("ArenaBoundary");
            Assert.IsNotNull(boundary, "場景缺少 ArenaBoundary（SceneBuilder 應預建）");

            float innerX = float.MaxValue;
            float innerZ = float.MaxValue;
            BoxCollider[] boxes = boundary.GetComponentsInChildren<BoxCollider>();
            Assert.AreEqual(4, boxes.Length, "場地邊界應為四面牆");
            for (int i = 0; i < boxes.Length; i++)
            {
                Bounds b = boxes[i].bounds;
                if (b.size.x < b.size.z) innerX = Mathf.Min(innerX, Mathf.Abs(b.center.x) - b.extents.x);
                else innerZ = Mathf.Min(innerZ, Mathf.Abs(b.center.z) - b.extents.z);
            }
            float limitX = innerX - BodyRadius;
            float limitZ = innerZ - BodyRadius;
            Assert.Less(limitX, 20f, "邊界內面量測失敗");

            int outside = 0;
            for (int cz = 0; cz < _grid.Rows; cz++)
            {
                for (int cx = 0; cx < _grid.Columns; cx++)
                {
                    _grid.CellCenter(cx, cz, out float x, out float z);
                    bool reachable = Mathf.Abs(x) <= limitX && Mathf.Abs(z) <= limitZ;
                    if (!reachable)
                    {
                        outside++;
                        Assert.IsTrue(_grid.IsBlocked(cx, cz),
                            "格 (" + cx + "," + cz + ") 的格心 (" + x + "," + z + ") 在英雄身體到得了的範圍之外，卻不是 Blocked");
                    }
                }
            }

            // §6 R8／R3a：現行幾何下「格心到不了」的恰好是最外一圈 4×80−4 = 316 格。
            // 擋多了（例如用方塊相交擋兩圈）會讓「沒有牆擋路時」點場地邊緣被替代到更裡面，違反 Phase 1 手感不變。
            Assert.AreEqual(316, outside, "格心落在可達範圍外的格數與 §6 R3a 的推導不符");
        }

        // ───────────────────────────── V4 l（§6 R4）─────────────────────────────

        // V4-l 測試牆重生壓住英雄（審查 C1，Probe3 實跑：重生後英雄留在牆體內，接著直接穿牆走出去）。
        // §6 R4：推出要按「效果」寫——任何阻擋物蓋上格點都要觸發，而不是只掛 RuneWall.OnActivated 一個入口。
        //
        // TestWall_A：牆心 (−7,3)、法線 +Z、半寬 2／半厚 0.3（scale 4×2.5×0.6），外擴 0.35
        //   → 禁區 x∈[−9.35,−4.65]、z∈[2.35,3.65]；格點上 x 格 21~30（x∈[−9.5,−5.0]）、z 格 44~47（z∈[2.0,4.0]）。
        //   推出落點＝離牆心 (−7,3) 最近的空格格心＝(−7.25, 1.75)（同距離取索引較小者：cz=43 先於 cz=48、cx=25 先於 cx=26）。
        // 推出後下令走到牆另一側的 (−7,6)，繞西端最短：
        //   ① (−7.25,1.75)→(−9.35,2.35) = √(2.1² + 0.6²)   = 2.18403
        //   ② (−9.35,2.35)→(−9.35,3.65) = 1.30000
        //   ③ (−9.35,3.65)→(−7,6)       = √(2.35² + 2.35²) = 3.32340
        //   合計 L = 6.80743 m  →  T = 1.5 × 6.80743 ÷ 5.5 = 1.85657 s
        //   （繞東端是 2.66833＋1.30＋3.32340 = 7.29173 m，較長，所以最短是西端這條。）
        [UnityTest]
        public IEnumerator V4l_RespawningTestWallEjectsTheHero_AndHeCannotWalkThroughIt()
        {
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());

            GameObject wallObject = GameObject.Find("TestWall_A");
            Assert.IsNotNull(wallObject, "場景缺少 TestWall_A");
            TestWallTarget wall = wallObject.GetComponent<TestWallTarget>();
            Assert.IsNotNull(wall, "TestWall_A 上沒有 TestWallTarget");
            Obb obb = ReadObb(wall);
            AssertWallGeometry(obb, new Vector2(-7f, 3f), new Vector2(0f, 1f));

            // 打爆它：重生前牆心那塊地是空的，英雄站得進去
            wall.ReceiveDamage(1000f, DamageType.True, null);
            yield return null;
            Assert.IsFalse(wall.IsAlive, "TestWall_A 沒有被打爆");

            Vector3 wallCentre = new Vector3(obb.Center.x, 0f, obb.Center.y);
            Assert.IsTrue(_agent.Warp(wallCentre), "無法把英雄放到牆心");
            _hero.transform.position = _agent.nextPosition;
            yield return null;

            // 對照：不推出的話，重生那一幀英雄站的位置就是重疊的
            Vector3 before = _hero.transform.position;
            Assert.IsTrue(BlockGrid.CircleOverlapsBox(before.x, before.z, BodyRadius,
                              obb.Center.x, obb.Center.y, obb.Normal.x, obb.Normal.y, obb.HalfWidth, obb.HalfThickness),
                "對照失敗：英雄根本沒站進牆體，推出這條斷言沒有鑑別力（" + before + "）");

            float respawnDeadline = Time.time + 10f;
            while (!wall.IsAlive)
            {
                if (Time.time > respawnDeadline) Assert.Fail("TestWall_A 逾時未重生");
                yield return null;
            }
            yield return null; // 重生後的下一幀

            Vector3 after = _hero.transform.position;
            Assert.IsFalse(BlockGrid.CircleOverlapsBox(after.x, after.z, BodyRadius,
                               obb.Center.x, obb.Center.y, obb.Normal.x, obb.Normal.y, obb.HalfWidth, obb.HalfThickness),
                "測試牆重生之後英雄仍留在牆體內：" + after);
            Assert.IsTrue(_agent.isOnNavMesh, "推出之後英雄掉出 NavMesh");
            Assert.AreEqual(-7.25f, after.x, 0.01f, "推出的落點不是最近的空格格心 x");
            Assert.AreEqual(1.75f, after.z, 0.01f, "推出的落點不是最近的空格格心 z");

            Vector3 destination = new Vector3(-7f, 0f, 6f);
            float limit = 1.5f * 6.80743f / MoveSpeed;
            _input.TapGround(destination);

            float deadline = Time.time + limit;
            bool arrived = false;
            while (Time.time <= deadline)
            {
                Assert.GreaterOrEqual(DistanceToObb(_hero.transform.position, obb), BodyRadius - WallPenetrationTolerance,
                    "英雄穿過了重生的測試牆：" + _hero.transform.position);
                if (PlanarDistance(_hero.transform.position, destination) <= ArriveTolerance) { arrived = true; break; }
                yield return null;
            }
            Assert.IsTrue(arrived, "推出之後 T=" + limit + "s 內沒有繞到牆的另一側，停在 " + _hero.transform.position);
        }

        // ───────────────────────────── V4 m（§6 R8／R3a）─────────────────────────────

        // V4-m 無牆時的 Phase 1 行為回歸：R3 第一輪用「方塊相交」登記邊界，連第 78 圈（格心 19.25）都擋掉，
        // 於是點場地邊緣 19.0～19.45m 會被替代到 18.75——沒有牆擋路卻改變了 Phase 1 的手感，違反最高優先序。
        // R3a 只擋「格心落在可達範圍之外」的那一圈，(19.3,6) 落在第 78 圈、格心 19.25 ≤ 19.45，照舊走得到。
        // 幾何：沒有任何東西擋路（場上兩面測試牆在 x≈±7、z∈[1,5]，離這條線很遠），
        //   所以「最短繞行長度」就是直線 (10,0)→(19.3,6)＝√(9.3² + 6²) = √122.49 = 11.06752 m
        //   → T = 1.5 × 11.06752 ÷ 5.5 = 3.01841 s
        [UnityTest]
        public IEnumerator V4m_TappingNearTheArenaEdge_WithNoWallInTheWay_IsStillPhase1()
        {
            yield return Setup(new Vector3(10f, 0f, 0f), Quaternion.identity, new RuneTuning());

            // 剛開場時格點上只有「格心到不了」的那一圈與兩面測試牆：
            //   邊界圈 4×80−4 = 316；TestWall_A（牆心 (−7,3)、法線 +Z）外擴後 x 格 21~30 × z 格 44~47 = 40；
            //   TestWall_B（牆心 (7,3)、轉 90°）外擴後 x 格 52~55 × z 格 41~50 = 40。合計 396。
            // 這條同時守住「不得擋太多」——用方塊相交擋兩圈會變成 624＋80 = 704。
            Assert.AreEqual(316 + 40 + 40, _grid.BlockedCount,
                "開場的 Blocked 格數與 §6 R3a 的推導不符（邊界圈 316 ＋ 兩面測試牆各 40）");

            Vector3 start = _hero.transform.position;
            Vector3 destination = new Vector3(19.3f, 0f, 6f);
            Vector2 direction = new Vector2(destination.x - start.x, destination.z - start.z).normalized;

            int buildsBefore = _bootstrap.Navigator.BuildCount;
            float limit = 1.5f * 11.06752f / MoveSpeed;
            _input.TapGround(destination);

            float deadline = Time.time + limit;
            bool arrived = false;
            float maxLateral = 0f;
            while (Time.time <= deadline)
            {
                Vector3 p = _hero.transform.position;
                float lateral = Mathf.Abs((p.x - start.x) * direction.y - (p.z - start.z) * direction.x);
                if (lateral > maxLateral) maxLateral = lateral;
                if (PlanarDistance(p, destination) <= ArriveTolerance) { arrived = true; break; }
                yield return null;
            }

            Assert.IsTrue(arrived,
                "場地邊緣 19.3m 沒有任何東西擋路，卻走不到：" + _hero.transform.position + "（T=" + limit + "s）");
            Assert.Less(maxLateral, StraightLineLateralTolerance,
                "沒有牆擋路的路線上出現了 " + maxLateral + "m 的側向偏移");
            Assert.AreEqual(buildsBefore, _bootstrap.Navigator.BuildCount,
                "沒有牆擋路卻重建了整合場：這一趟不該碰到格點的任何 Dijkstra");
        }

        // ───────────────────────────── §6 R2 ─────────────────────────────

        // R2：目的地落在格點涵蓋範圍外（地板恰好 ±20、格點也是 ±20，點在最外那條線上就會發生）時，
        // 舊實作整趟退回 Phase 1 行為——審查 H3 實跑：z=19 繞得過去、z=20 頂在牆上。
        // 新標準：夾進格點再照常解析，繞牆行為不因為多按了半公尺而消失。
        // 幾何：只需要越過牆面（牆心 (0,4)、外擴後 z∈[3.35,4.65]），最短折線 (0,0)→(2.35,3.35)→(2.35,4.65)
        //   ① √(2.35² + 3.35²) = 4.09207   ② 1.30000
        //   合計 L = 5.39207 m  →  T = 1.5 × 5.39207 ÷ 5.5 = 1.47057 s
        [UnityTest]
        public IEnumerator R2_DestinationOutsideTheGrid_IsClampedAndTheHeroStillDetours()
        {
            yield return Setup(Vector3.zero, Quaternion.identity, new RuneTuning());
            yield return QuickCastWall(new Vector2(0f, 4f), new Vector2(0f, 1f));

            float limit = 1.5f * 5.39207f / MoveSpeed;
            _input.TapGround(new Vector3(0f, 0f, 20f)); // 恰好在格點之外

            float deadline = Time.time + limit;
            bool crossed = false;
            while (Time.time <= deadline)
            {
                Assert.GreaterOrEqual(DistanceToObb(_hero.transform.position, _castObb), BodyRadius - WallPenetrationTolerance,
                    "英雄穿進了牆：" + _hero.transform.position);
                if (_hero.transform.position.z > 4.65f) { crossed = true; break; }
                yield return null;
            }
            Assert.IsTrue(crossed,
                "界外目的地被整趟退回 Phase 1 了：T=" + limit + "s 內沒有繞到牆後，停在 " + _hero.transform.position);
        }
    }
}
