using System;
using System.Collections;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // 紅線 4：Update／LateUpdate 每幀路徑零 GC 配置——用量的，不用看的。
    //
    // 做法：兩個探針元件分別排在執行順序的最前 (-32000) 與最後 (+32000)，夾住「全場所有 MonoBehaviour」的 Update 與 LateUpdate，
    // 以 Profiler「GC Allocated In Frame」計數器在夾區前後的差值，累計這段期間配置的位元組數。
    // 測試框架自己的協程在 Update 之後、LateUpdate 之前執行，落在兩段夾區之外，不會污染量測。
    // 腳本化的輸入由排在中間的 Driver 元件在 Update 內送出，所以「指令 → 狀態轉換 → 滑步起手」這條路徑也在夾區內。
    public static class AllocationProbe
    {
        public static bool Measuring;
        public static long UpdateBytes;
        public static long LateUpdateBytes;
        public static int Frames;
        private static long _mark;
        private static ProfilerRecorder _recorder;

        // 量測來源：Unity Profiler 的「GC Allocated In Frame」計數器——它在一幀之內隨每次配置即時累加，
        // 在夾區前後各讀一次 CurrentValue 相減，就是夾區內的配置量。
        // （System.GC.GetAllocatedBytesForCurrentThread 在 Unity 的 Mono 上恆回 0，正向對照測試實測抓不到任何配置，不能用。）
        public static void Reset()
        {
            Measuring = false;
            UpdateBytes = 0;
            LateUpdateBytes = 0;
            Frames = 0;
            if (!_recorder.Valid) _recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        public static void Dispose()
        {
            if (_recorder.Valid) _recorder.Dispose();
        }

        private static long Now() { return _recorder.Valid ? _recorder.CurrentValue : 0L; }

        public static void Begin() { _mark = Now(); }

        public static void EndUpdate()
        {
            long delta = Now() - _mark;
            if (!Measuring) return;
            UpdateBytes += delta;
            Frames++;
        }

        public static void EndLateUpdate()
        {
            long delta = Now() - _mark;
            if (Measuring) LateUpdateBytes += delta;
        }
    }

    [DefaultExecutionOrder(-32000)]
    public sealed class AllocationProbeBegin : MonoBehaviour
    {
        private void Update() { AllocationProbe.Begin(); }
        private void LateUpdate() { AllocationProbe.Begin(); }
    }

    [DefaultExecutionOrder(32000)]
    public sealed class AllocationProbeEnd : MonoBehaviour
    {
        private void Update() { AllocationProbe.EndUpdate(); }
        private void LateUpdate() { AllocationProbe.EndLateUpdate(); }
    }

    // 正向對照用：每幀故意配置。
    public sealed class DeliberateAllocator : MonoBehaviour
    {
        public byte[] Last;
        private void Update() { Last = new byte[256]; }
    }

    // 在 Update 夾區內送出腳本化輸入：持續攻擊木樁，目押窗口一開就微彈。
    public sealed class CombatDriver : MonoBehaviour
    {
        internal HeroController Hero;
        internal ScriptedInput Input;
        internal ICombatTarget Target;
        public int Hits;
        public int Dashes;
        private bool _started;
        private static readonly Vector2 Away = new Vector2(0f, -1f);

        private void Update()
        {
            if (Hero == null) return;

            if (!_started)
            {
                _started = true;
                Input.TapTarget(Target);
                return;
            }

            if (Hero.StateMachine.CurrentState == PlayerState.AttackRelease && Hero.CadenceMover.CurrentCharges > 0)
                Input.Flick(Away);
        }
    }

    // r1 對抗審查 H2：符印施放必須真的發生在 AllocationProbeBegin／End 的夾區之內（Update()），
    // 不能是測試協程本體直接呼叫——那樣落在夾區外，對這條路徑毫無鑑別力。Trigger 由測試在
    // AllocationProbe.Measuring 打開的同一刻設成 true，下一次這個元件的 Update() 就會送出施放。
    public sealed class RuneCastDriver : MonoBehaviour
    {
        internal ScriptedInput Input;
        public bool Trigger;
        public int Casts;

        private void Update()
        {
            if (!Trigger || Input == null) return;
            Trigger = false;
            Casts++;
            Input.RuneQuickCast();
        }
    }

    // Phase 2 批 2 V4-i：繞牆的移動指令也必須由夾區內的 Update() 送出，才量得到
    // 「視線被擋 → 重建整合場 → Follow 轉向」這條路徑；寫在測試協程本體會落在探針夾區之外（同 H2）。
    public sealed class NavDetourDriver : MonoBehaviour
    {
        internal ScriptedInput Input;
        public Vector3 Destination;
        public bool Trigger;
        public int Orders;

        private void Update()
        {
            if (!Trigger || Input == null) return;
            Trigger = false;
            Orders++;
            Input.TapGround(Destination);
        }
    }

    public sealed class ZeroAllocationTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";

        private static bool AnyRuneWallAlive(RuneWall[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) return true;
            return false;
        }

        private static RuneWall FirstAliveRuneWall(RuneWall[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) return pool[i];
            return null;
        }

        private static IEnumerator WaitUntilNoRuneWallIsAlive(RuneWall[] pool, float timeoutSeconds)
        {
            float deadline = Time.time + timeoutSeconds;
            while (AnyRuneWallAlive(pool))
            {
                if (Time.time > deadline)
                {
                    Assert.Fail("石牆逾時未到期");
                    yield break;
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Probe_DetectsADeliberatePerFrameAllocation()
        {
            // 正向對照：探針必須「抓得到」配置，否則下面那個 0 沒有任何意義。
            AllocationProbe.Reset();
            GameObject rig = new GameObject("ProbeRig");
            rig.AddComponent<AllocationProbeBegin>();
            rig.AddComponent<DeliberateAllocator>();
            rig.AddComponent<AllocationProbeEnd>();
            yield return null;

            AllocationProbe.Measuring = true;
            for (int i = 0; i < 30; i++) yield return null;
            AllocationProbe.Measuring = false;

            UnityEngine.Object.Destroy(rig);
            Assert.GreaterOrEqual(AllocationProbe.Frames, 25);
            Assert.GreaterOrEqual(AllocationProbe.UpdateBytes, 256L * 25L,
                "探針沒有量到每幀 256 bytes 的故意配置（量到 " + AllocationProbe.UpdateBytes + "）：這個量測來源在此環境沒有鑑別力");
        }

        [UnityTest]
        public IEnumerator Combat_UpdateAndLateUpdate_AllocateNothing()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            HeroController hero = UnityEngine.Object.FindObjectOfType<HeroController>();
            DummyTarget dummy = UnityEngine.Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(hero);
            Assert.IsNotNull(dummy);
            dummy.Configure(100000f, Faction.RedTeam); // 量測期間不要打死：死亡／復活另有測試

            ScriptedInput input = new ScriptedInput();
            hero.Initialize(input, UnityEngine.Object.FindObjectOfType<CombatFeedbackService>(), Camera.main,
                UnityEngine.Object.FindObjectOfType<HapticFeedbackService>());

            int hits = 0, dashes = 0;
            hero.OnAttackHitResolved += target => hits++;
            hero.CadenceMover.OnDashExecuted += distance => dashes++;

            AllocationProbe.Reset();
            GameObject rig = new GameObject("ProbeRig");
            rig.AddComponent<AllocationProbeBegin>();
            CombatDriver driver = rig.AddComponent<CombatDriver>();
            driver.Hero = hero;
            driver.Input = input;
            driver.Target = dummy;
            RuneCastDriver runeDriver = rig.AddComponent<RuneCastDriver>();
            runeDriver.Input = input;
            NavDetourDriver detourDriver = rig.AddComponent<NavDetourDriver>();
            detourDriver.Input = input;
            rig.AddComponent<AllocationProbeEnd>();

            HeroLocomotion locomotion = hero.GetComponent<HeroLocomotion>();
            Assert.IsNotNull(locomotion, "英雄身上沒有 HeroLocomotion");

            // 暖機：跑完至少兩刀、一次滑步，讓靜態表、JIT、首次進入各狀態的一次性初始化都發生在量測之前
            float deadline = Time.time + 12f;
            while (hits < 2 || dashes < 1)
            {
                if (Time.time > deadline) Assert.Fail("暖機逾時：hits=" + hits + " dashes=" + dashes);
                yield return null;
            }

            // 符印石牆（Phase 2 批 1 步驟 B V4g）：量測窗口內要包含一次施放與一次到期，證明這條路徑也是 0 GC。
            RuneCaster caster = UnityEngine.Object.FindObjectOfType<RuneCaster>();
            Assert.IsNotNull(caster, "場景缺少 RuneCaster");
            RuneWall[] runeWalls = UnityEngine.Object.FindObjectsOfType<RuneWall>();
            Assert.GreaterOrEqual(runeWalls.Length, 1, "場景缺少符印石牆池");
            caster.Initialize(input, input, hero.transform, Camera.main, new RuneTuning(), runeWalls, hero.HeroFaction);

            // 量測窗口外先跑一次完整的施放到期（協程本體直接呼叫即可，反正不落在夾區內、不受 H2 約束）：
            // 把第一次呼叫可能有的一次性成本排除在量測之外。
            input.RuneQuickCast();
            yield return WaitUntilNoRuneWallIsAlive(runeWalls, 8f);

            // 重新 Initialize：重置冷卻與名冊狀態，確保量測窗口內的施放不會被暖機那一次的冷卻擋下
            caster.Initialize(input, input, hero.transform, Camera.main, new RuneTuning(), runeWalls, hero.HeroFaction);

            // r1 對抗審查 M2：「GRID 疊圖重填也不配置」原本只是讀碼的宣稱。把疊圖打開，讓量測窗口內
            // 至少發生兩次重填（立牆 → Blocked 格變多、牆到期 → 變少）。先在窗口外暖機一次，
            // 把 Mesh 原生緩衝第一次配置的成本排除在外。
            NavGridDebugView gridDebug = UnityEngine.Object.FindObjectOfType<NavGridDebugView>();
            Assert.IsNotNull(gridDebug, "場景缺少 NavGridDebugView");
            gridDebug.Visible = true;
            yield return null;
            yield return null;
            Assert.GreaterOrEqual(gridDebug.RebuildCount, 1, "疊圖打開後應該至少重填過一次（暖機）");
            int rebuildsBefore = gridDebug.RebuildCount;

            int hitsBefore = hits, dashesBefore = dashes;
            AllocationProbe.Measuring = true;

            // 量測窗口第 1 段（仍在夾區內）：先把戰鬥活性跑滿。批 2 的繞牆指令會中斷攻擊，
            // 所以命中與滑步必須在下移動指令之前收集完，否則兩件事互相排擠。
            deadline = Time.time + 20f;
            while (hits - hitsBefore < 2 || dashes - dashesBefore < 1)
            {
                if (Time.time > deadline) break;
                yield return null;
            }

            // 量測窗口第 2 段：符印施放 → 立牆擋住去路 → 對牆後方下移動指令（Build＋Follow 轉向）→ 牆到期撤銷
            runeDriver.Trigger = true; // 下一次 RuneCastDriver.Update()（落在探針夾區內）才真的送出施放
            bool sawRuneWallAlive = false;
            int followFrames = 0;
            deadline = Time.time + 20f;
            while (AllocationProbe.Frames < 240 || runeDriver.Casts < 1 || detourDriver.Orders < 1
                   || followFrames < 10 || !sawRuneWallAlive || AnyRuneWallAlive(runeWalls))
            {
                if (AnyRuneWallAlive(runeWalls))
                {
                    sawRuneWallAlive = true;

                    // 批 2 V4-i：牆一立起來就對牆的另一側下移動指令（由夾區內的 Update 送出），
                    // 逼出「視線被擋 → FlowField.Build → Follow 轉向」；牆到期時的撤銷仍落在同一個量測窗口內。
                    if (detourDriver.Orders == 0 && !detourDriver.Trigger)
                    {
                        RuneWall alive = FirstAliveRuneWall(runeWalls);
                        Vector3 behind = alive.transform.position + alive.transform.forward * 4f;
                        behind.y = 0f;
                        detourDriver.Destination = behind;
                        detourDriver.Trigger = true;
                    }
                }
                if (locomotion.LastSteerMode == SteerMode.Follow
                    && hero.StateMachine.CurrentState == PlayerState.Moving) followFrames++;
                if (Time.time > deadline) break;
                yield return null;
            }
            // 再放一幀：石牆是在 Update 裡到期的，它造成的格點撤銷與 GRID 疊圖重填發生在同一幀的
            // LateUpdate——比測試協程晚。不多等這一幀，「牆到期」那一段就落在量測窗口之外。
            yield return null;
            AllocationProbe.Measuring = false;
            UnityEngine.Object.Destroy(rig);

            // 活性：量測期間受測行為必須真的發生過，否則 0 配置只代表「什麼都沒跑」
            Assert.GreaterOrEqual(AllocationProbe.Frames, 240, "量測幀數不足");
            Assert.GreaterOrEqual(hits - hitsBefore, 2, "量測期間沒有足夠的命中");
            Assert.GreaterOrEqual(dashes - dashesBefore, 1, "量測期間沒有發生滑步");
            Assert.AreEqual(1, runeDriver.Casts, "量測窗口內應該恰好送出一次符印施放（在探針夾區內的 Update() 裡）");
            Assert.IsTrue(sawRuneWallAlive, "量測期間從未觀察到石牆存活：符印施放這條路徑沒有被量到");
            Assert.IsFalse(AnyRuneWallAlive(runeWalls), "量測窗口內石牆應已到期（否則量測時間不夠長，這不構成證據）");
            Assert.AreEqual(1, detourDriver.Orders, "量測窗口內應該恰好送出一次繞牆移動指令（在探針夾區內的 Update() 裡）");
            Assert.GreaterOrEqual(followFrames, 10,
                "量測期間從未進入 Follow 轉向：整合場重建與繞牆轉向這條路徑沒有被量到（實測 " + followFrames + " 幀）");
            Assert.IsTrue(gridDebug.Visible, "量測期間 GRID 疊圖應保持開啟");
            Assert.GreaterOrEqual(gridDebug.RebuildCount - rebuildsBefore, 2,
                "量測期間 GRID 疊圖沒有重填過兩次（立牆＋到期）：M2 那條零配置沒有被量到（實測 "
                + (gridDebug.RebuildCount - rebuildsBefore) + " 次）");

            Assert.AreEqual(0L, AllocationProbe.UpdateBytes,
                "Update 夾區在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.UpdateBytes + " bytes");
            Assert.AreEqual(0L, AllocationProbe.LateUpdateBytes,
                "LateUpdate 夾區在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.LateUpdateBytes + " bytes");
        }
    }
}
