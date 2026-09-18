using System;
using System.Collections;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;

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

    public sealed class ZeroAllocationTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";

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
            rig.AddComponent<AllocationProbeEnd>();

            // 暖機：跑完至少兩刀、一次滑步，讓靜態表、JIT、首次進入各狀態的一次性初始化都發生在量測之前
            float deadline = Time.time + 12f;
            while (hits < 2 || dashes < 1)
            {
                if (Time.time > deadline) Assert.Fail("暖機逾時：hits=" + hits + " dashes=" + dashes);
                yield return null;
            }

            int hitsBefore = hits, dashesBefore = dashes;
            AllocationProbe.Measuring = true;
            deadline = Time.time + 20f;
            while (AllocationProbe.Frames < 240 || hits - hitsBefore < 2 || dashes - dashesBefore < 1)
            {
                if (Time.time > deadline) break;
                yield return null;
            }
            AllocationProbe.Measuring = false;
            UnityEngine.Object.Destroy(rig);

            // 活性：量測期間受測行為必須真的發生過，否則 0 配置只代表「什麼都沒跑」
            Assert.GreaterOrEqual(AllocationProbe.Frames, 240, "量測幀數不足");
            Assert.GreaterOrEqual(hits - hitsBefore, 2, "量測期間沒有足夠的命中");
            Assert.GreaterOrEqual(dashes - dashesBefore, 1, "量測期間沒有發生滑步");

            Assert.AreEqual(0L, AllocationProbe.UpdateBytes,
                "Update 夾區在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.UpdateBytes + " bytes");
            Assert.AreEqual(0L, AllocationProbe.LateUpdateBytes,
                "LateUpdate 夾區在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.LateUpdateBytes + " bytes");
        }
    }
}
