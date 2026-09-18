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
    // Phase 2 批 1 步驟 B：符印石牆的 Unity 端行為，對應計畫書 §5 V4 a~f。
    // V4 g（零配置量測窗口內的施放與到期）在 ZeroAllocationTests.cs。
    //
    // 每個測試都用一顆全新的 ScriptedInput 重新 Initialize 場景既有的 RuneCaster／RuneGhostPreview，
    // 繞過真實觸控辨識（TouchGestureRouter 另有 EditMode 測試），只驗這一層「收到符印事件之後做了什麼」。
    public sealed class RuneWallPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";

        private HeroController _hero;
        private ScriptedInput _input;
        private RuneCaster _caster;
        private RuneGhostPreview _ghost;
        private RuneWall[] _pool;

        private IEnumerator Setup(RuneTuning tuning)
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null; // Awake／OnEnable／Start（含 Phase1Bootstrap 的接線）全部跑完

            _hero = Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(_hero, "場景裡找不到 HeroController");

            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.Warp(Vector3.zero), "無法把英雄放回原點");
            _hero.transform.rotation = Quaternion.identity; // forward = (0,0,1)，讓每個測試的方位可預期

            _input = new ScriptedInput();
            CombatFeedbackService feedback = Object.FindObjectOfType<CombatFeedbackService>();
            _hero.Initialize(_input, feedback, Camera.main);

            _caster = Object.FindObjectOfType<RuneCaster>();
            Assert.IsNotNull(_caster, "場景缺少 RuneCaster");
            _pool = Object.FindObjectsOfType<RuneWall>();
            Assert.GreaterOrEqual(_pool.Length, 3, "石牆池應預建 3 面（上限 2 面＋1 面坍塌緩衝）");
            _caster.Initialize(_input, _input, _hero.transform, Camera.main, tuning, _pool, _hero.HeroFaction);

            _ghost = Object.FindObjectOfType<RuneGhostPreview>();
            Assert.IsNotNull(_ghost, "場景缺少 RuneGhostPreview");
            _ghost.Initialize(_input, _input, _hero.transform, Camera.main, tuning);

            yield return null;
        }

        private static int AliveCount(RuneWall[] pool)
        {
            int count = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) count++;
            return count;
        }

        private static RuneWall FirstAlive(RuneWall[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) return pool[i];
            return null;
        }

        // V4 a：極速施放後 0.2s 內，場上恰有 1 面存活石牆，中心在施放當下位置前方 4m（±0.1m）、
        // 牆面法線與英雄朝向夾角 <1°、BoxCollider.enabled。
        [UnityTest]
        public IEnumerator QuickCast_SpawnsOneWall_FourMetresAhead_FacingTheHero()
        {
            yield return Setup(new RuneTuning());

            Vector3 heroPos = _hero.transform.position;
            Vector3 heroForward = _hero.transform.forward;

            _input.RuneQuickCast();

            float deadline = Time.time + 0.2f;
            RuneWall wall = null;
            while (Time.time <= deadline)
            {
                wall = FirstAlive(_pool);
                if (wall != null) break;
                yield return null;
            }

            Assert.IsNotNull(wall, "0.2s 內沒有任何存活石牆");
            Assert.AreEqual(1, AliveCount(_pool), "應恰好 1 面存活");

            Vector3 expectedCenter = heroPos + heroForward * 4f;
            Vector3 offset = wall.transform.position - expectedCenter;
            offset.y = 0f;
            Assert.LessOrEqual(offset.magnitude, 0.1f, "石牆中心偏離預期落點：" + wall.transform.position);

            float angle = Vector3.Angle(wall.transform.forward, heroForward);
            Assert.Less(angle, 1f, "牆面法線與英雄朝向夾角過大：" + angle);

            BoxCollider collider = wall.GetComponent<BoxCollider>();
            Assert.IsTrue(collider.enabled, "石牆的 Collider 應已開啟");
        }

        // V4 b 正向對照：沒有石牆時，沿同一條路線 3 秒內應越過 z=4 平面。
        [UnityTest]
        public IEnumerator NoWall_HeroCrossesThePlane_WithinThreeSeconds()
        {
            yield return Setup(new RuneTuning());

            _input.TapGround(new Vector3(0f, 0f, 20f));

            float deadline = Time.time + 3f;
            bool crossed = false;
            while (Time.time <= deadline)
            {
                if (_hero.transform.position.z > 4f) { crossed = true; break; }
                yield return null;
            }

            Assert.IsTrue(crossed, "正向對照失敗：沒有石牆時 3 秒內應越過 z=4 平面，實際位置 " + _hero.transform.position);
        }

        // V4 b：同一條路線，極速施放的石牆立在 z=4（法線朝 +Z）之後，3 秒內不得越過該平面。
        [UnityTest]
        public IEnumerator Wall_BlocksTheHero_AlongTheSamePath()
        {
            yield return Setup(new RuneTuning());

            _input.RuneQuickCast();
            yield return null;
            Assert.IsNotNull(FirstAlive(_pool), "石牆未成形，阻擋測試沒有意義");

            _input.TapGround(new Vector3(0f, 0f, 20f));

            float deadline = Time.time + 3f;
            while (Time.time <= deadline)
            {
                Assert.LessOrEqual(_hero.transform.position.z, 4f, "英雄越過了石牆所在的 z=4 平面：" + _hero.transform.position);
                yield return null;
            }
        }

        // V4 c：施放後 5.0s（+0.3s 容差）石牆不再存活、Collider 已關。
        [UnityTest]
        public IEnumerator Wall_ExpiresAfterItsLifespan()
        {
            yield return Setup(new RuneTuning());

            _input.RuneQuickCast();
            yield return null;

            RuneWall wall = FirstAlive(_pool);
            Assert.IsNotNull(wall, "石牆未成形");
            BoxCollider collider = wall.GetComponent<BoxCollider>();

            yield return new WaitForSeconds(5.3f);

            Assert.IsFalse(wall.IsAlive, "5.3s 後石牆應已消失");
            Assert.IsFalse(collider.enabled, "石牆的 Collider 應已關閉");
        }

        // V4 d：連放三面，第三面成形後第一面不再存活，存活數＝2。
        // 冷卻歸零只作用在這個測試自己的 RuneTuning 實例上，不動 RuneTuning.cs 的正式預設值（8s）。
        [UnityTest]
        public IEnumerator ThirdWall_EvictsTheOldestWhenTeamCapIsExceeded()
        {
            RuneTuning tuning = new RuneTuning { CooldownSeconds = 0f };
            yield return Setup(tuning);

            _input.RuneQuickCast();
            yield return null;
            RuneWall first = FirstAlive(_pool);
            Assert.IsNotNull(first, "第一面石牆未成形");

            _input.RuneQuickCast();
            yield return null;
            _input.RuneQuickCast();
            yield return null;

            Assert.IsFalse(first.IsAlive, "第三面成形後，最早的那面應已坍塌");
            Assert.AreEqual(2, AliveCount(_pool), "存活數應為 2");
        }

        // V4 f：冷卻中再次極速施放，存活石牆數不變。
        [UnityTest]
        public IEnumerator QuickCastDuringCooldown_DoesNotSpawnAnotherWall()
        {
            yield return Setup(new RuneTuning()); // 預設冷卻 8s

            _input.RuneQuickCast();
            yield return null;
            Assert.AreEqual(1, AliveCount(_pool), "第一次施放應成牆");

            _input.RuneQuickCast(); // 冷卻中：應被忽略
            yield return null;

            Assert.AreEqual(1, AliveCount(_pool), "冷卻中再次施放不應增加存活石牆數");
        }

        // V4 e：拖曳更新→虛影可見且位置＝預期落點；取消→虛影隱藏、無石牆、冷卻未開始；
        // 鬆手→石牆成形於該落點、虛影隱藏。
        [UnityTest]
        public IEnumerator Drag_ShowsGhost_CancelHidesWithoutWall_ReleaseSpawnsWallAndHidesGhost()
        {
            yield return Setup(new RuneTuning());

            Renderer ghostRenderer = _ghost.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(ghostRenderer, "虛影缺少 Renderer");
            Assert.IsFalse(ghostRenderer.enabled, "初始狀態虛影應隱藏");

            Vector2 screenDir = new Vector2(0f, 1f); // 螢幕正上方 → 攝影機水平前方（見 RuneCaster.ScreenToWorldGroundDirection）
            const float distance01 = 0.5f;

            _input.RuneDrag(screenDir, distance01);
            yield return null;

            Assert.IsTrue(ghostRenderer.enabled, "拖曳中虛影應可見");

            Vector3 heroPos = _hero.transform.position;
            Vector3 worldDir = RuneCaster.ScreenToWorldGroundDirection(screenDir, Camera.main.transform);
            RuneCastLogic placementCalc = new RuneCastLogic(new RuneTuning());
            Assert.IsTrue(placementCalc.TryDragPlacement(heroPos.x, heroPos.z, worldDir.x, worldDir.z, distance01, out RuneWallPlacement expected));
            Vector3 expectedPos = new Vector3(expected.CenterX, _ghost.transform.position.y, expected.CenterZ);
            Vector3 ghostOffset = _ghost.transform.position - expectedPos;
            ghostOffset.y = 0f;
            Assert.LessOrEqual(ghostOffset.magnitude, 0.05f, "虛影位置與預期落點不符：" + _ghost.transform.position);

            // 取消：不必真的滑回原點（那是 RuneGestureTracker 的職責，EditMode 已測），這裡只驗 RuneCaster／
            // RuneGhostPreview 對 Cancelled 事件的反應——無石牆、虛影隱藏、冷卻未開始。
            _input.RuneCancel();
            yield return null;
            Assert.IsFalse(ghostRenderer.enabled, "取消後虛影應隱藏");
            Assert.AreEqual(0, AliveCount(_pool), "取消不應產生石牆");
            Assert.AreEqual(0f, _caster.CooldownRemaining, 0.001f, "取消不應進入冷卻");

            // 鬆手成牆
            _input.RuneDrag(screenDir, distance01);
            yield return null;
            _input.RuneRelease(screenDir, distance01);
            yield return null;

            Assert.IsFalse(ghostRenderer.enabled, "鬆手後虛影應隱藏");
            RuneWall spawned = FirstAlive(_pool);
            Assert.IsNotNull(spawned, "鬆手應該成牆");
            Vector3 spawnedOffset = spawned.transform.position - expectedPos;
            spawnedOffset.y = 0f;
            Assert.LessOrEqual(spawnedOffset.magnitude, 0.1f, "成牆位置與拖曳預期落點不符：" + spawned.transform.position);
        }
    }
}
