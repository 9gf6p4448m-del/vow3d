using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Combat;
using Vow.Core;

namespace Vow.Tests.PlayMode
{
    // 載入 SceneBuilder 生成的灰盒場景實際跑起來的冒煙測試：驗的是純邏輯測試碰不到的 Unity 邊界——
    // NavMeshAgent 手動位移、動畫事件 OnAttackHit 真的從 Animator 觸發、佔位骨架真的在動、邊界牆真的擋得住。
    //
    // Unity 測試框架遇到任何非預期的 Debug.LogError 會直接判測試失敗，所以
    // 「前搖逾時未收到動畫事件」「非法狀態轉換」「英雄不在 NavMesh 上」這些紅字一出現，測試就紅。
    public sealed class GreyboxSmokeTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";

        private sealed class ScriptedInput : IPlayerInputService
        {
            public ControlMode ActiveMode { get; set; }
            public event Action<Vector3> OnMoveDestinationSelected;
            public event Action<ICombatTarget> OnCombatTargetSelected;
            public event Action<Vector2> OnCadenceVectorFlicked;
            public event Action<Vector2, float> OnRuneVectorDragUpdated { add { } remove { } }
            public event Action OnRuneQuickCastTriggered { add { } remove { } }
            public event Action OnRuneCastCancelled { add { } remove { } }

            public void TapGround(Vector3 point) { OnMoveDestinationSelected?.Invoke(point); }
            public void TapTarget(ICombatTarget target) { OnCombatTargetSelected?.Invoke(target); }
            public void Flick(Vector2 screenDirection) { OnCadenceVectorFlicked?.Invoke(screenDirection); }
        }

        private HeroController _hero;
        private ScriptedInput _input;

        private IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null; // Awake／OnEnable／Start（含 Phase1Bootstrap 的接線）全部跑完

            _hero = UnityEngine.Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(_hero, "場景裡找不到 HeroController");

            // 以腳本化輸入取代真實觸控：驗的是輸入之後的整條鏈路，觸控辨識本身已有 TouchGestureRouter 的測試
            _input = new ScriptedInput();
            _hero.Initialize(_input, UnityEngine.Object.FindObjectOfType<Vow.Combat.Feedback.CombatFeedbackService>(), Camera.main);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
        {
            float deadline = Time.time + timeoutSeconds;
            while (!condition())
            {
                if (Time.time > deadline) Assert.Fail("逾時（" + timeoutSeconds + "s）：" + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TapToMove_WalksAcrossTheBakedNavMesh_AndStops()
        {
            yield return LoadScene();
            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.isOnNavMesh, "英雄一開始就不在 NavMesh 上");

            Vector3 destination = new Vector3(-6f, 0f, -5f);
            _input.TapGround(destination);
            Assert.AreEqual(PlayerState.Moving, _hero.StateMachine.CurrentState);

            yield return WaitUntil(() => _hero.StateMachine.CurrentState == PlayerState.Idle, 6f, "英雄走到定點並回到 Idle");

            Vector3 offset = _hero.transform.position - destination;
            offset.y = 0f;
            Assert.Less(offset.magnitude, 0.35f, "停下的位置離目的地太遠：" + _hero.transform.position);
            Assert.IsTrue(agent.isOnNavMesh);
        }

        [UnityTest]
        public IEnumerator Attack_IsResolvedByTheAnimationEvent_AtWindupTime_AndTheRigActuallyMoves()
        {
            yield return LoadScene();
            DummyTarget dummy = UnityEngine.Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(dummy);
            float healthBefore = dummy.Health;

            Transform arm = _hero.transform.Find("HeroModel/Hips/Spine/Chest/RightUpperArm");
            Assert.IsNotNull(arm, "找不到佔位骨架的 RightUpperArm");
            Quaternion armRest = arm.localRotation;
            float maxArmDeviation = 0f;

            float windupAt = -1f, hitAt = -1f;
            _hero.OnAttackWindupStarted += () => { if (windupAt < 0f) windupAt = Time.time; };
            _hero.OnAttackHitResolved += target => { if (hitAt < 0f) hitAt = Time.time; };

            _input.TapTarget(dummy);

            float deadline = Time.time + 6f;
            while (hitAt < 0f)
            {
                if (Time.time > deadline) Assert.Fail("6 秒內沒有任何一刀命中");
                if (windupAt >= 0f) maxArmDeviation = Mathf.Max(maxArmDeviation, Quaternion.Angle(armRest, arm.localRotation));
                yield return null;
            }

            Assert.Less(dummy.Health, healthBefore, "木樁沒有掉血");

            // 命中若來自 0.6s 保險機制而非動畫事件：① 會噴 LogError 使本測試失敗；② 這裡的時間差也會是 0.6 而不是 0.25
            float windupDuration = hitAt - windupAt;
            Assert.That(windupDuration, Is.InRange(0.2f, 0.32f), "前搖→命中應約 0.25s（由 OnAttackHit 動畫事件驅動），實測 " + windupDuration);

            Assert.Greater(maxArmDeviation, 30f, "前搖期間右臂幾乎沒動（最大偏轉 " + maxArmDeviation + "°）：佔位動畫曲線沒有生效");
        }

        [UnityTest]
        public IEnumerator FlickInsideTheWindow_DashesOnePointFourMetres_AndSpendsACharge()
        {
            yield return LoadScene();
            DummyTarget dummy = UnityEngine.Object.FindObjectOfType<DummyTarget>();

            _input.TapTarget(dummy);
            yield return WaitUntil(() => _hero.StateMachine.CurrentState == PlayerState.AttackRelease, 6f, "進入目押窗口");

            Vector3 before = _hero.transform.position;
            int chargesBefore = _hero.CadenceMover.CurrentCharges;

            _input.Flick(new Vector2(0f, -1f)); // 往畫面下方＝遠離木樁，沿途沒有障礙物
            Assert.AreEqual(PlayerState.CadenceDashing, _hero.StateMachine.CurrentState, "0 幀切後搖：同一呼叫內就要進入 CadenceDashing");
            Assert.AreEqual(chargesBefore - 1, _hero.CadenceMover.CurrentCharges);

            yield return WaitUntil(() => _hero.StateMachine.CurrentState != PlayerState.CadenceDashing, 2f, "滑步結束");

            Vector3 moved = _hero.transform.position - before;
            moved.y = 0f;
            Assert.AreEqual(1.4f, moved.magnitude, 0.08f, "第一次滑步實際位移");
        }

        [UnityTest]
        public IEnumerator DashingOutwardAtTheArenaEdge_IsStoppedByTheBoundary_AndStaysOnTheNavMesh()
        {
            yield return LoadScene();
            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.Warp(new Vector3(18.9f, 0f, 0f)), "無法把英雄放到場地邊緣");
            _hero.transform.position = agent.nextPosition;
            yield return null;

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(_hero.Mover.TryExecuteCadenceDash(Vector3.right), "第 " + (i + 1) + " 次滑步未能起手");
                yield return WaitUntil(() => !_hero.Mover.IsDashing, 2f, "滑步結束");
            }

            float x = _hero.transform.position.x;
            Assert.LessOrEqual(x, 19.5f, "英雄越過了 NavMesh 邊界：x = " + x);

            // 兩道防線要分開驗：NavMeshAgent 自己會把位置夾回 NavMesh 邊界 (±19.5)，所以上面那條在邊界牆失效時照樣會過。
            // 邊界牆（內面 ±19.8）應該更早擋住身體：中心最遠 19.8 − 半徑 0.35 − 皮膚 0.02 = 19.43。
            Assert.LessOrEqual(x, 19.46f, "邊界牆沒有在 NavMesh 邊界之前擋住英雄：x = " + x);
            Assert.Greater(x, 19.0f, "英雄根本沒往外滑（x = " + x + "），這個測試沒有測到邊界");
            Assert.IsTrue(agent.isOnNavMesh, "英雄掉出 NavMesh");

            // 掉出去之後的典型症狀是再也不能移動——確認還走得回來
            _input.TapGround(new Vector3(10f, 0f, 0f));
            yield return WaitUntil(() => _hero.transform.position.x < 12f, 5f, "從邊緣走回場內");
        }
    }
}
