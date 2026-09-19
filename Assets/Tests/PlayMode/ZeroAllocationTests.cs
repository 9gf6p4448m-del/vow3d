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
        // 批 3：暖機的近戰破牆會把大腦的目標搶走，之後就不會再打木樁了。由測試開閘，
        // 讓「重新鎖定木樁」這個動作一樣發生在夾區內的 Update()（H2）。
        public bool Retarget;
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

            if (Retarget)
            {
                Retarget = false;
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

    // r2 對抗審查 N3（§6 R11 V11-f）：批 2 新增的兩條路徑——R1a 替代點解析（substituted==true）與
    // SteerMode.GoalBlocked 的當幀重解析——原本都落在量測窗口之外（窗口裡的繞牆目的地是「牆的正後方」＝走得到）。
    // 兩者都只可能由**持有追擊指令**觸發：移動指令在 HeroLocomotion.Step 裡一偵測到格點版本改變就先行重解析，
    // 永遠走不到 GoalBlocked。而戰鬥大腦在「有目標」時每幀 StopMoving 會把注入的追擊指令殺掉，
    // 所以這個 driver 等到「繞牆的移動指令已經把大腦的目標清掉、英雄也停下來」之後才動手（由測試端開閘）。
    public sealed class GoalBlockedDriver : MonoBehaviour
    {
        internal HeroLocomotion Locomotion;
        internal Transform ChaseTarget;
        internal RuneWall Wall;
        internal Faction WallFaction;
        internal float WallHeight;
        public bool Trigger;
        public int Steps;

        private void Update()
        {
            if (!Trigger || Locomotion == null || ChaseTarget == null) return;

            if (Steps == 0)
            {
                Locomotion.Chase(ChaseTarget);
                Steps = 1;
                return;
            }
            if (Steps == 1)
            {
                // 把一面牆蓋在追擊目標身上：上一次解析出來的 goal 格被蓋住，而 0.1s 的重解析窗口還沒到
                // → 下一幀的 Steer 回 GoalBlocked → 當幀拿原始目的地重新解析（那次解析必然 substituted==true）。
                Vector3 p = ChaseTarget.position;
                Wall.Activate(new Vector3(p.x, WallHeight * 0.5f, p.z), Quaternion.identity, WallFaction, null, -1);
                Steps = 2;
            }
        }
    }

    // Phase 2 批 3 V5：砲台開火、子彈穿透己方牆、子彈被牆擋下、護盾取得（含護盾條可見）、
    // 走真實 OnWorldTap 點到己方牆後方的地板——五件事都必須發生在**探針夾區內的 Update()**，
    // 寫在測試協程本體會落在夾區之外（同 H2），對這幾條路徑毫無鑑別力。
    public sealed class Batch3Driver : MonoBehaviour
    {
        internal TestTurret Turret;
        internal IWorldTapInput Tap;
        internal Camera View;
        internal RuneWall FriendlyWall;
        internal RuneWall BlockingWall;
        internal HeroController Hero;
        internal ScriptedInput Input;
        internal IRockShield Shield;
        internal float WallHeight;
        internal float WallMaxHealth;
        internal float MeleeDamage;

        public bool Trigger;
        public bool MeleeGate;   // 由測試在「繞牆那段已經量夠」之後開閘，免得攻擊指令把移動打斷
        public int Stage;
        public int Taps;
        public int DriverGrants;   // r1 覆審 HIGH-2：授予路徑必須在夾區內被行使，不能只靠動畫事件那條

        // 砲台的計數是累計值（窗口外的暖機也算在內），所以窗口內的條件一律看增量。
        internal int ShotsBase;
        internal int PenetrationsBase;
        internal int BlocksBase;

        // 砲台彈道：TestTurret 固定在 (−8, 1, 6) 朝 +X。牆放在走廊靠砲台那一段，遠離英雄的活動範圍。
        private const float TurretX = -8f;
        private const float LaneZ = 6f;

        private void Update()
        {
            if (!Trigger || Turret == null) return;

            switch (Stage)
            {
                case 0:
                    PlaceOnLane(FriendlyWall, 1.5f, Hero.HeroFaction);
                    Stage = 1;
                    return;

                case 1:
                    Vector3 screenPoint = View.WorldToScreenPoint(FriendlyWall.transform.position);
                    Tap.OnWorldTap(screenPoint.x, screenPoint.y); // 點己方牆 → 應該落在牆後的地板上
                    Taps++;
                    Stage = 2;
                    return;

                case 2:
                    PlaceOnLane(BlockingWall, 3f, Faction.RedTeam);
                    Turret.SetFiring(true);
                    Stage = 3;
                    return;

                case 3:
                    // 等砲台的同時每幀再點一次：暖機只掩蓋得了「第一次」的成本，
                    // 每幀都走一次 OnWorldTap，「每次都配置」的實作就藏不住。
                    Vector3 lanePoint = View.WorldToScreenPoint(FriendlyWall.transform.position);
                    Tap.OnWorldTap(lanePoint.x, lanePoint.y);
                    Taps++;
                    // r1 覆審 HIGH-2：真實的授予處理常式掛在**動畫事件**上，那一段落在
                    // AllocationProbeEnd.Update 與 AllocationProbeBegin.LateUpdate 之間，探針夾不到
                    // （實測：在 Grant() 塞配置，測試照樣綠）。所以這裡由 Driver 自己在 Update() 內走
                    // 同一個授予入口，讓 Grant() 與護盾條的顯示更新真的落進量測範圍。
                    Shield.Grant();
                    DriverGrants++;
                    if (Turret.ShotsFired - ShotsBase < 3
                        || Turret.Penetrations - PenetrationsBase < 2
                        || Turret.Blocks - BlocksBase < 2) return;
                    Turret.SetFiring(false);
                    Stage = 4;
                    return;

                case 4:
                    if (!MeleeGate) return;
                    // 把敵方牆搬到英雄面前、削到剩一刀的血，再下攻擊指令：走真實的
                    // 大腦 → ResolveAttackHit → OnAttackHitResolved → 授予護盾這條路。
                    Vector3 forward = Hero.transform.forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
                    forward.Normalize();
                    Vector3 position = Hero.transform.position + forward * (Hero.AttackRange * 0.5f);
                    position.y = WallHeight * 0.5f;
                    BlockingWall.Activate(position, Quaternion.LookRotation(forward, Vector3.up), Faction.RedTeam, null, -1);
                    BlockingWall.ReceiveDamage(WallMaxHealth - MeleeDamage, DamageType.Physical, null);
                    Input.TapTarget(BlockingWall);
                    Stage = 5;
                    return;
            }
        }

        private void PlaceOnLane(RuneWall wall, float distanceFromTurret, Faction owner)
        {
            wall.Activate(new Vector3(TurretX + distanceFromTurret, WallHeight * 0.5f, LaneZ),
                          Quaternion.LookRotation(Vector3.right, Vector3.up), owner, null, -1);
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

        private static RuneWall FirstDeadRuneWall(RuneWall[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (!pool[i].IsAlive) return pool[i];
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
            GoalBlockedDriver goalBlockedDriver = rig.AddComponent<GoalBlockedDriver>();
            Batch3Driver batch3Driver = rig.AddComponent<Batch3Driver>();
            rig.AddComponent<AllocationProbeEnd>();

            HeroLocomotion locomotion = hero.GetComponent<HeroLocomotion>();
            Assert.IsNotNull(locomotion, "英雄身上沒有 HeroLocomotion");
            Phase1Bootstrap bootstrap = UnityEngine.Object.FindObjectOfType<Phase1Bootstrap>();
            Assert.IsNotNull(bootstrap, "場景缺少 Phase1Bootstrap");
            GridNavigator navigator = bootstrap.Navigator;
            Assert.IsNotNull(navigator, "Phase1Bootstrap 沒有建立導航器");

            // V11-f 的追擊目標：一個沒有 Collider 的空物件，位置在下面開閘時才依英雄當下位置決定。
            GameObject chaseProbe = new GameObject("GoalBlockedProbeTarget");
            goalBlockedDriver.Locomotion = locomotion;
            goalBlockedDriver.ChaseTarget = chaseProbe.transform;
            goalBlockedDriver.WallFaction = hero.HeroFaction;
            goalBlockedDriver.WallHeight = new RuneTuning().WallHeight;

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
            // 批 3：改讀「組裝端交給玩家的那幾面」，不是 FindObjectsOfType——後者會把除錯用的敵方牆
            // 一起掃進來，下面所有 AnyRuneWallAlive 的判斷就會被 Batch3Driver 放在彈道上的牆干擾。
            RuneWall[] runeWalls = caster.Pool;
            Assert.GreaterOrEqual(runeWalls.Length, 1, "場景缺少符印石牆池");

            // 批 3 V5 的四件活性：砲台、子彈穿透／擋下、護盾、真實 OnWorldTap。
            TestTurret turret = UnityEngine.Object.FindObjectOfType<TestTurret>();
            Assert.IsNotNull(turret, "場景缺少測試砲台");
            Assert.IsFalse(turret.IsFiring, "砲台必須預設關閉，由夾區內的 Driver 自己打開");
            RockShieldBehaviour shield = UnityEngine.Object.FindObjectOfType<RockShieldBehaviour>();
            Assert.IsNotNull(shield, "英雄身上缺少破牆護盾元件");
            HeroShieldBar shieldBar = UnityEngine.Object.FindObjectOfType<HeroShieldBar>();
            Assert.IsNotNull(shieldBar, "英雄身上缺少護盾條");
            EnemyWallSpawner enemyWalls = UnityEngine.Object.FindObjectOfType<EnemyWallSpawner>();
            Assert.IsNotNull(enemyWalls, "場景缺少敵方石牆池");
            Assert.GreaterOrEqual(enemyWalls.Pool.Length, 2, "敵方石牆池應預建 2 面");

            RuneTuning batch3Tuning = new RuneTuning();
            float dummyHealthBefore = dummy.Health;
            hero.ResolveAttackHit(dummy); // 量一次真實近戰傷害（窗口外），Driver 要靠它算「剩一刀的血」
            float meleeDamage = dummyHealthBefore - dummy.Health;
            Assert.Greater(meleeDamage, 0f, "量不到近戰傷害");

            batch3Driver.Turret = turret;
            batch3Driver.Tap = bootstrap.WorldTapInput;
            batch3Driver.View = Camera.main;
            batch3Driver.FriendlyWall = enemyWalls.Pool[0];
            batch3Driver.BlockingWall = enemyWalls.Pool[1];
            batch3Driver.Hero = hero;
            batch3Driver.Input = input;
            batch3Driver.WallHeight = batch3Tuning.WallHeight;
            batch3Driver.WallMaxHealth = batch3Tuning.WallMaxHealth;
            batch3Driver.MeleeDamage = meleeDamage;
            batch3Driver.Shield = shield;
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

            // 批 3 路徑的暖機（窗口外，與上面符印施放那一段同一個做法）：讓 Batch3Driver 把**整條流程**
            // 先完整跑一遍——Unity 的 Collider／Renderer 受管包裝、每一發子彈第一次離膛、格點改動後第一次
            // 重解析、破牆演出（震屏＋貼花＋震覺）、護盾條第一次顯示，全都是一次性成本。
            // 窗口內同一條流程會再跑一遍，而且點擊每幀一次、穿透與擋下各要 ≥2 次，
            // 所以「每次都配置」的實作照樣會被抓到。
            for (int warm = 0; warm < 2; warm++)
            {
                batch3Driver.MeleeGate = true;
                batch3Driver.Trigger = true;
                int grantsAtWarmStart = shield.GrantCount;
                deadline = Time.time + 25f;
                while (batch3Driver.Stage < 5 || shield.GrantCount == grantsAtWarmStart)
                {
                    if (Time.time > deadline)
                        Assert.Fail("批 3 暖機逾時（第 " + (warm + 1) + " 輪）：stage=" + batch3Driver.Stage
                                    + " shots=" + turret.ShotsFired + " pen=" + turret.Penetrations
                                    + " block=" + turret.Blocks + " grants=" + shield.GrantCount);
                    yield return null;
                }
                batch3Driver.Trigger = false;
                Assert.IsTrue(shieldBar.IsVisible, "暖機時護盾條應該顯示過一次");

                // 子彈池裡每一發都要離膛過（第一次離膛是一次性成本）：補打到整池輪過一圈以上。
                turret.SetFiring(true);
                int enoughShots = turret.ShotsFired + new ProjectileTuning().BulletPoolSize + 1;
                deadline = Time.time + 15f;
                while (turret.ShotsFired < enoughShots)
                {
                    if (Time.time > deadline) Assert.Fail("批 3 暖機補彈逾時：shots=" + turret.ShotsFired);
                    yield return null;
                }
                turret.SetFiring(false);

                enemyWalls.Pool[0].CollapseWall(false);
                enemyWalls.Pool[1].CollapseWall(false);
                yield return new WaitForSeconds(new ProjectileTuning().ShieldDurationSeconds + 0.2f);
                Assert.IsFalse(shieldBar.IsVisible, "暖機的護盾應已到期");
                batch3Driver.Stage = 0;
                batch3Driver.Taps = 0;
                batch3Driver.DriverGrants = 0;
                batch3Driver.MeleeGate = false;
            }

            // 暖機的近戰破牆把大腦的目標搶走了，這裡先重新鎖定木樁（與 CombatDriver 最初那一次鎖定同樣
            // 落在窗口之外：鎖定本身是既有路徑，窗口內要量的是它之後每幀的攻擊週期）。
            driver.Retarget = true;
            yield return null;
            yield return null;
            yield return null;

            int rebuildsBefore = gridDebug.RebuildCount;

            int hitsBefore = hits, dashesBefore = dashes;
            int substitutedBefore = navigator.SubstitutedCount;
            int goalBlockedBefore = locomotion.GoalBlockedResolveCount;
            int shieldGrantsBefore = shield.GrantCount;
            int shotsBefore = turret.ShotsFired;
            int penetrationsBefore = turret.Penetrations;
            int blocksBefore = turret.Blocks;
            batch3Driver.ShotsBase = shotsBefore;
            batch3Driver.PenetrationsBase = penetrationsBefore;
            batch3Driver.BlocksBase = blocksBefore;
            bool sawShieldBarVisible = false;
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
            deadline = Time.time + 20f; // 不動：第 3 段跑完約 10s（牆 1 到期 5s ＋ 牆 2 到期 5s），仍有一倍餘裕
            while (AllocationProbe.Frames < 240 || runeDriver.Casts < 1 || detourDriver.Orders < 1
                   || followFrames < 10 || !sawRuneWallAlive || AnyRuneWallAlive(runeWalls)
                   || goalBlockedDriver.Steps < 2
                   || navigator.SubstitutedCount - substitutedBefore < 1
                   || locomotion.GoalBlockedResolveCount - goalBlockedBefore < 1
                   || batch3Driver.Stage < 5 || batch3Driver.Taps < 2 || batch3Driver.DriverGrants < 2
                   || turret.ShotsFired - shotsBefore < 3
                   || turret.Penetrations - penetrationsBefore < 2
                   || turret.Blocks - blocksBefore < 2
                   || shield.GrantCount - shieldGrantsBefore < 1 || !sawShieldBarVisible)
            {
                // 批 3 的四件活性同樣由夾區內的 Update() 送出；刻意等符印施放那一段過去才開始，
                // 免得兩件事擠在同一幀、出問題時分不出是誰的。
                if (!batch3Driver.Trigger && detourDriver.Orders == 1) batch3Driver.Trigger = true;
                // 近戰砸牆會把移動打斷，所以等「繞牆轉向已經量夠」之後才開閘（動作仍在 Driver 的 Update 裡）。
                if (!batch3Driver.MeleeGate && followFrames >= 10 && detourDriver.Orders == 1)
                    batch3Driver.MeleeGate = true;
                if (shieldBar.IsVisible) sawShieldBarVisible = true;

                // 量測窗口第 3 段（V11-f）：第一面牆到期、英雄也停下來之後，才注入追擊指令並把牆蓋到目標身上。
                // 放在這裡而不是與第 2 段並行，是因為戰鬥／繞牆那兩段還在時大腦會來搶控制權。
                if (goalBlockedDriver.Steps == 0 && !goalBlockedDriver.Trigger
                    && detourDriver.Orders == 1 && sawRuneWallAlive && !AnyRuneWallAlive(runeWalls)
                    && batch3Driver.Stage >= 5 && shield.GrantCount - shieldGrantsBefore >= 1)
                {
                    Vector3 heroNow = hero.transform.position;
                    float probeZ = heroNow.z - 6f;
                    if (probeZ < -18f) probeZ = heroNow.z + 6f;
                    chaseProbe.transform.position = new Vector3(heroNow.x, 0f, probeZ);
                    goalBlockedDriver.Wall = FirstDeadRuneWall(runeWalls);
                    Assert.IsNotNull(goalBlockedDriver.Wall, "石牆池裡沒有空閒的牆可以拿來蓋住追擊目標");
                    goalBlockedDriver.Trigger = true;
                }

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
            UnityEngine.Object.Destroy(chaseProbe);

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
            // §6 R11 V11-f（r2 N3）：批 2 新增的兩條路徑也必須真的在夾區內跑過，否則 0 bytes 只代表「這兩條沒跑」
            Assert.AreEqual(2, goalBlockedDriver.Steps,
                "量測窗口內沒有走完 V11-f 的追擊注入（Steps=" + goalBlockedDriver.Steps + "）");
            Assert.GreaterOrEqual(navigator.SubstitutedCount - substitutedBefore, 1,
                "量測期間從未發生 substituted==true 的解析：R1a 兩階段選點這條路沒有被量到");
            Assert.GreaterOrEqual(locomotion.GoalBlockedResolveCount - goalBlockedBefore, 1,
                "量測期間從未發生 SteerMode.GoalBlocked 的當幀重解析：這條路沒有被量到");
            // 批 3 V5：四件新行為也必須真的在夾區內跑過，否則 0 bytes 只代表「這四條沒跑」
            Assert.AreEqual(5, batch3Driver.Stage,
                "量測窗口內沒有走完批 3 的驅動流程（Stage=" + batch3Driver.Stage + "）");
            Assert.GreaterOrEqual(turret.ShotsFired - shotsBefore, 3,
                "量測期間砲台開火不足（實測 " + (turret.ShotsFired - shotsBefore) + " 發）");
            Assert.GreaterOrEqual(turret.Penetrations - penetrationsBefore, 2,
                "量測期間子彈穿透己方牆不足 2 次：一次性成本可能把這條路徑蓋掉");
            Assert.GreaterOrEqual(turret.Blocks - blocksBefore, 2,
                "量測期間子彈被敵方牆擋下不足 2 次：一次性成本可能把這條路徑蓋掉");
            Assert.GreaterOrEqual(batch3Driver.DriverGrants, 2,
                "量測期間 Driver 沒有在自己的 Update() 內行使護盾授予 ≥2 次：授予路徑落在探針夾區外就量不到"
                + "（實測 " + batch3Driver.DriverGrants + " 次）");
            Assert.GreaterOrEqual(shield.GrantCount - shieldGrantsBefore, batch3Driver.DriverGrants + 1,
                "量測期間除了 Driver 直接授予之外，還必須有一次走真實大腦路徑的授予");
            Assert.IsTrue(sawShieldBarVisible, "量測期間護盾條的 Renderer 從未可見");
            Assert.GreaterOrEqual(batch3Driver.Taps, 2,
                "量測期間送出的真實 OnWorldTap 不足 2 次（RaycastNonAlloc ＋ TapPickLogic 沒有被反覆量到）");
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
