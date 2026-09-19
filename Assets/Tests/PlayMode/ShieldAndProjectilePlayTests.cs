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
    // Phase 2 批 3 步驟 B：陣營校驗破牆得護盾＋友軍彈道穿透（PHASE2_BATCH3_PLAN.md §5 V4 a~r）。
    // V4-e（點己方牆穿過去）在 RuneWallPlayTests；V5（零配置）在 ZeroAllocationTests。
    //
    // 數值一律讀 tuning（RuneTuning／ProjectileTuning／實測的近戰傷害），測試裡不寫死寬鬆常數。
    public sealed class ShieldAndProjectilePlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";

        private HeroController _hero;
        private ScriptedInput _input;
        private RuneCaster _caster;
        private Phase1Bootstrap _bootstrap;
        private RuneWall[] _playerPool;
        private DummyTarget _dummy;
        private RuneTuning _rune;
        private readonly ProjectileTuning _projectile = new ProjectileTuning();

        // 砲台彈道：TestTurret 固定在 (−8, 1, 6)、朝木樁 (0,1,6)＝+X（§4-7）。
        private const float TurretX = -8f;
        private const float LaneZ = 6f;

        private IEnumerator Setup(RuneTuning tuning)
        {
            _rune = tuning;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null; // Awake／OnEnable／Start（含 Phase1Bootstrap 的接線）全部跑完

            _hero = Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(_hero, "場景裡找不到 HeroController");
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            Assert.IsNotNull(_bootstrap, "場景缺少 Phase1Bootstrap");
            _dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(_dummy, "場景缺少木樁");

            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.Warp(Vector3.zero), "無法把英雄放回原點");
            _hero.transform.rotation = Quaternion.identity;

            _input = new ScriptedInput();
            _hero.Initialize(_input, Object.FindObjectOfType<CombatFeedbackService>(), Camera.main,
                             Object.FindObjectOfType<HapticFeedbackService>());

            _caster = Object.FindObjectOfType<RuneCaster>();
            Assert.IsNotNull(_caster, "場景缺少 RuneCaster");
            _playerPool = _caster.Pool; // 組裝端實際交給玩家的那幾面（不是自己另外湊一份，V4-o 靠這個）
            Assert.IsNotNull(_playerPool, "RuneCaster 沒有拿到石牆池");
            Assert.GreaterOrEqual(_playerPool.Length, 3, "玩家石牆池應預建 3 面（上限 2 面＋1 面坍塌緩衝）");
            _caster.Initialize(_input, _input, _hero.transform, Camera.main, tuning, _playerPool, _hero.HeroFaction);

            yield return null;
        }

        // ───────────────────────── 小工具 ─────────────────────────

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

        private static RuneWall FirstDead(RuneWall[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (!pool[i].IsAlive) return pool[i];
            return null;
        }

        private static TestWallTarget FindTestWall(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            Assert.IsNotNull(go, "場景缺少 " + objectName);
            TestWallTarget wall = go.GetComponent<TestWallTarget>();
            Assert.IsNotNull(wall, objectName + " 沒有 TestWallTarget");
            return wall;
        }

        // 近戰傷害不寫死：拿英雄真的打一下木樁量出來（＝HeroTuningAsset.AttackDamage）。
        private float MeasureMeleeDamage()
        {
            float before = _dummy.Health;
            _hero.ResolveAttackHit(_dummy);
            float damage = before - _dummy.Health;
            Assert.Greater(damage, 0f, "量不到近戰傷害，後面的期望值全部沒有意義");
            return damage;
        }

        // 在砲台彈道上放一面牆。distanceFromTurret＝牆心離砲台幾公尺；牆面法線朝 +X（子彈穿厚度方向）。
        private RuneWall PlaceWallOnLane(RuneWall wall, float distanceFromTurret, Faction owner)
        {
            Vector3 position = new Vector3(TurretX + distanceFromTurret, _rune.WallHeight * 0.5f, LaneZ);
            wall.Activate(position, Quaternion.LookRotation(Vector3.right, Vector3.up), owner, null, -1);
            return wall;
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds, string message)
        {
            float deadline = Time.time + timeoutSeconds;
            while (!condition())
            {
                if (Time.time > deadline)
                {
                    Assert.Fail(message);
                    yield break;
                }
                yield return null;
            }
        }

        // ───────────────────────── V4-a／V4-b：兩本血量帳合一 ─────────────────────────

        // V4-a：己方符印牆連續受近戰傷害 → RuneWallLogic 的血量與 ICombatTarget 的血量逐值相同，
        // 第 5 下牆死、Collider 關閉、名冊釋放名額。
        [UnityTest]
        public IEnumerator V4a_MeleeDamageOnARuneWall_KeepsBothLedgersIdentical_AndReleasesTheRosterSeat()
        {
            yield return Setup(new RuneTuning { CooldownSeconds = 0f });

            float melee = MeasureMeleeDamage();
            _input.RuneQuickCast();
            yield return null;

            RuneWall wall = FirstAlive(_playerPool);
            Assert.IsNotNull(wall, "石牆未成形");
            BoxCollider collider = wall.GetComponent<BoxCollider>();

            int hitsToKill = Mathf.CeilToInt(_rune.WallMaxHealth / melee);
            Assert.AreEqual(5, hitsToKill, "前提：300 血 ÷ 60 傷 ＝ 5 下。數值改了這條要重看");
            Assert.AreEqual(_rune.WallMaxHealth, wall.Health, 1e-3f);
            Assert.AreEqual(wall.LogicHealth, wall.Health, 1e-3f, "立牆當下兩本帳就該是同一個數字");

            for (int hit = 1; hit <= hitsToKill; hit++)
            {
                wall.ReceiveDamage(melee, DamageType.Physical, _hero.gameObject);

                float expected = Mathf.Max(0f, _rune.WallMaxHealth - melee * hit);
                Assert.AreEqual(expected, wall.Health, 1e-3f, "第 " + hit + " 下之後的血量");
                Assert.AreEqual(wall.LogicHealth, wall.Health, 1e-3f,
                    "第 " + hit + " 下之後 RuneWallLogic 與 ICombatTarget 的血量分岔了（兩本帳沒有合一）");
            }

            Assert.IsFalse(wall.IsAlive, "第 " + hitsToKill + " 下之後石牆必須死亡");
            Assert.IsFalse(collider.enabled, "石牆的 Collider 應已關閉");

            // 名冊釋放名額：還放得下 2 面
            _input.RuneQuickCast();
            yield return null;
            _input.RuneQuickCast();
            yield return null;
            Assert.AreEqual(2, AliveCount(_playerPool), "名冊沒有把被打碎那面的名額還回來");
        }

        // V4-b：穿透與近戰打的是同一本帳——先穿 2 次（各扣 10% 最大生命），近戰就少打一下。
        // 對照：合帳前的碼會在近戰第 5 下才死（穿透只扣 RuneWallLogic、近戰只扣 CombatTargetBehaviour）。
        [UnityTest]
        public IEnumerator V4b_PenetrationAndMeleeShareOneLedger_SoTheWallDiesOneHitEarlier()
        {
            yield return Setup(new RuneTuning { CooldownSeconds = 0f });

            float melee = MeasureMeleeDamage();
            _input.RuneQuickCast();
            yield return null;

            RuneWall wall = FirstAlive(_playerPool);
            Assert.IsNotNull(wall, "石牆未成形");

            float perPenetration = _rune.WallMaxHealth * _rune.PenetrationHealthFraction;
            Vector3 velocity = Vector3.forward * _projectile.BulletSpeed;
            Assert.IsTrue(wall.TryPenetrateBullet(velocity, out float _), "第 1 發穿透必須放行");
            Assert.IsTrue(wall.TryPenetrateBullet(velocity, out float _), "第 2 發穿透必須放行");

            float afterPenetration = _rune.WallMaxHealth - perPenetration * 2f;
            Assert.AreEqual(afterPenetration, wall.Health, 1e-3f, "兩發穿透之後 ICombatTarget 看到的血量沒有跟著少");
            Assert.AreEqual(wall.LogicHealth, wall.Health, 1e-3f);

            int remainingHits = Mathf.CeilToInt(afterPenetration / melee);
            Assert.AreEqual(4, remainingHits, "前提：240 血 ÷ 60 傷 ＝ 4 下（合帳前是 5 下）");

            for (int hit = 1; hit < remainingHits; hit++)
            {
                wall.ReceiveDamage(melee, DamageType.Physical, _hero.gameObject);
                Assert.IsTrue(wall.IsAlive, "第 " + hit + " 下之後石牆不該死");
                Assert.AreEqual(afterPenetration - melee * hit, wall.Health, 1e-3f);
            }

            wall.ReceiveDamage(melee, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(wall.IsAlive, "第 " + remainingHits + " 下就該死（合帳前要到第 5 下）");
        }

        // V4-c：合帳不得污染別的目標——木樁與測試牆的受擊、死亡、重生逐值與 v0.4.1 相同。
        [UnityTest]
        public IEnumerator V4c_TheMergedLedger_DoesNotTouchTheDummyOrTheTestWall()
        {
            yield return Setup(new RuneTuning());

            float max = _dummy.MaxHealth;
            float quarter = max * 0.25f;
            float[] expected = { 0.75f, 0.5f, 0.25f, 0f };
            for (int i = 0; i < expected.Length; i++)
            {
                _dummy.ReceiveDamage(quarter, DamageType.Physical, _hero.gameObject);
                Assert.AreEqual(expected[i], _dummy.HealthNormalized, 1e-4f,
                    "木樁的 HealthNormalized 序列必須與合帳前逐值相同（第 " + (i + 1) + " 下）");
            }
            Assert.IsFalse(_dummy.IsAlive, "木樁應已倒下");

            yield return new WaitForSeconds(3.0f); // 木樁 2.5s 後重生
            Assert.IsTrue(_dummy.IsAlive, "木樁應已重生");
            Assert.AreEqual(1f, _dummy.HealthNormalized, 1e-4f, "重生後應回滿血");

            TestWallTarget wallA = FindTestWall("TestWall_A");
            BoxCollider wallCollider = wallA.GetComponent<BoxCollider>();
            wallA.ReceiveDamage(wallA.MaxHealth, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(wallA.IsAlive, "測試牆應被打碎");
            Assert.IsFalse(wallCollider.enabled, "測試牆的 Collider 應已關閉");

            yield return new WaitForSeconds(6.4f); // 測試牆 6s 後重生
            Assert.IsTrue(wallA.IsAlive, "測試牆應已重生");
            Assert.IsTrue(wallCollider.enabled, "重生後 Collider 應重新開啟");
            Assert.AreEqual(1f, wallA.HealthNormalized, 1e-4f);
        }

        // V4-d：壽命到期仍走完整死亡流程（OnDied 恰 1 次、Collider 關、名額還回去）。
        // 合帳後若 ForceKill 保留 `if (!IsAlive) return`，HandleDeath 不再被呼叫 → 牆永遠擋路、名額永遠不還。
        [UnityTest]
        public IEnumerator V4d_AWallExpiring_StillRunsTheWholeDeathFlow()
        {
            RuneTuning tuning = new RuneTuning { CooldownSeconds = 0f };
            yield return Setup(tuning);

            _input.RuneQuickCast();
            yield return null;
            RuneWall wall = FirstAlive(_playerPool);
            Assert.IsNotNull(wall, "石牆未成形");
            BoxCollider collider = wall.GetComponent<BoxCollider>();

            int deaths = 0;
            wall.OnDied += () => deaths++;

            yield return new WaitForSeconds(tuning.WallLifespanSeconds + 0.4f);

            Assert.IsFalse(wall.IsAlive, "壽命到期後石牆應已消失");
            Assert.IsFalse(collider.enabled, "壽命到期後 Collider 必須關閉（否則牆永遠擋路）");
            Assert.AreEqual(1, deaths, "OnDied 必須恰好送出一次");

            _input.RuneQuickCast();
            yield return null;
            _input.RuneQuickCast();
            yield return null;
            Assert.AreEqual(2, AliveCount(_playerPool), "壽命到期的牆沒有把名冊名額還回來");
        }

        // ───────────────────────── V4-f：敵方／中立牆點得到 ─────────────────────────

        [UnityTest]
        public IEnumerator V4f_EnemyAndNeutralWalls_AreSelectableByTapping()
        {
            yield return Setup(new RuneTuning());

            ICombatTarget picked = null;
            bool moved = false;
            _bootstrap.InputService.OnCombatTargetSelected += target => picked = target;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moved = true;

            RuneWall enemy = _bootstrap.EnemyWalls.Spawn();
            Assert.IsNotNull(enemy, "除錯鈕沒有生出敵方石牆");
            Assert.AreEqual(Faction.RedTeam, enemy.OwnerFaction);
            yield return null;

            TapWorldPoint(enemy.transform.position);
            Assert.AreSame(enemy, picked, "點敵方石牆應該鎖定它");
            Assert.IsFalse(moved, "點敵方石牆不該變成移動指令");

            picked = null;
            moved = false;
            TestWallTarget wallA = FindTestWall("TestWall_A");
            TapWorldPoint(wallA.transform.position);
            Assert.AreSame(wallA, picked, "點中立測試牆應該鎖定它");
            Assert.IsFalse(moved, "點中立測試牆不該變成移動指令");
        }

        private void TapWorldPoint(Vector3 worldPoint)
        {
            Vector3 screenPoint = Camera.main.WorldToScreenPoint(worldPoint);
            _bootstrap.WorldTapInput.OnWorldTap(screenPoint.x, screenPoint.y);
        }

        // ───────────────────────── V4-g～j：護盾 ─────────────────────────

        // V4-g：近戰砸碎中立牆 → 當幀拿到滿額護盾，2.5s 後歸零。
        [UnityTest]
        public IEnumerator V4g_SmashingANeutralWallWithMelee_GrantsAFullShield_ThatExpires()
        {
            yield return Setup(new RuneTuning());

            IRockShield shield = _bootstrap.Shield;
            Assert.IsNotNull(shield, "場景缺少破牆護盾元件");
            Assert.AreEqual(0f, shield.Amount, 1e-3f, "初始不該有護盾");

            TestWallTarget wallA = FindTestWall("TestWall_A");
            _input.TapTarget(wallA);

            yield return WaitUntil(() => !wallA.IsAlive, 20f, "英雄沒有在 20s 內砸碎中立牆");

            Assert.AreEqual(_projectile.ShieldAmount, shield.Amount, 1e-3f, "砸碎中立牆的當幀應拿到滿額護盾");
            Assert.Greater(shield.RemainingSeconds, _projectile.ShieldDurationSeconds - 0.1f, "倒數應該幾乎是滿的");
            Assert.LessOrEqual(shield.RemainingSeconds, _projectile.ShieldDurationSeconds, "倒數不得超過滿值");

            yield return new WaitForSeconds(_projectile.ShieldDurationSeconds + 0.1f);
            Assert.AreEqual(0f, shield.Amount, 1e-3f, "護盾應在 2.5s 後歸零");
        }

        // V4-h：砸自家牆一點護盾都不給。
        // ①照計畫字面直接呼叫 hero.ResolveAttackHit；②另加真實大腦路徑（TapTarget → 大腦 → ResolveAttackHit
        // → OnAttackHitResolved）——只有②抓得到「陣營比較恆真」，因為 ResolveAttackHit 本身不送出那個事件。
        [UnityTest]
        public IEnumerator V4h_SmashingYourOwnWall_GrantsNothing()
        {
            yield return Setup(new RuneTuning { CooldownSeconds = 0f });

            IRockShield shield = _bootstrap.Shield;
            float melee = MeasureMeleeDamage();

            // ① 字面路徑。**這半條恆真**：HeroController.ResolveAttackHit 不送 OnAttackHitResolved
            //    （大腦在 HeroCombatBrain.cs:269-270 才送），所以授予路徑根本走不到，不論陣營比較怎麼寫
            //    都會是 0。鑑別力全部在下面的 ②（r1 對抗審查 LOW-4）。
            _input.RuneQuickCast();
            yield return null;
            RuneWall own = FirstAlive(_playerPool);
            Assert.IsNotNull(own, "石牆未成形");
            Assert.AreEqual(_hero.HeroFaction, own.OwnerFaction, "自己施放的牆擁有者應該是英雄陣營");

            own.ReceiveDamage(_rune.WallMaxHealth - melee, DamageType.Physical, _hero.gameObject);
            _hero.ResolveAttackHit(own);
            Assert.IsFalse(own.IsAlive, "前提：這一下要真的把自家牆打碎");
            Assert.AreEqual(0f, shield.Amount, 1e-3f, "砸自家牆不得給盾（直接呼叫 ResolveAttackHit）");

            // ② 真實大腦路徑
            _input.RuneQuickCast();
            yield return null;
            RuneWall own2 = FirstAlive(_playerPool);
            Assert.IsNotNull(own2, "第二面石牆未成形");
            own2.ReceiveDamage(_rune.WallMaxHealth - melee, DamageType.Physical, _hero.gameObject);

            _input.TapTarget(own2);
            yield return WaitUntil(() => !own2.IsAlive, 10f, "英雄沒有在 10s 內砸碎自家牆");

            Assert.AreEqual(0f, shield.Amount, 1e-3f, "砸自家牆不得給盾（走大腦的真實攻擊結算）");
        }

        // V4-i①：己方牆壽命到期不得給盾。
        [UnityTest]
        public IEnumerator V4i1_AWallExpiringOnItsOwn_GrantsNoShield()
        {
            RuneTuning tuning = new RuneTuning { CooldownSeconds = 0f };
            yield return Setup(tuning);

            _input.RuneQuickCast();
            yield return null;
            RuneWall wall = FirstAlive(_playerPool);
            Assert.IsNotNull(wall, "石牆未成形");

            yield return new WaitForSeconds(tuning.WallLifespanSeconds + 0.4f);

            Assert.IsFalse(wall.IsAlive, "前提：石牆必須已經到期");
            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 1e-3f, "壽命到期不得給盾");
        }

        // V4-i②：己方牆被友軍子彈穿到崩解不得給盾（走真實路徑：放在彈道上，第 7 發穿透崩解）。
        [UnityTest]
        public IEnumerator V4i2_AFriendlyWallCollapsingUnderFriendlyFire_GrantsNoShield()
        {
            yield return Setup(new RuneTuning());

            RuneWall wall = PlaceWallOnLane(FirstDead(_playerPool), 2f, _hero.HeroFaction);
            yield return null;

            TestTurret turret = _bootstrap.Turret;
            turret.SetFiring(true);
            yield return WaitUntil(() => !wall.IsAlive, 10f, "己方牆沒有在 10s 內被友軍火力穿到崩解");
            turret.SetFiring(false);

            Assert.GreaterOrEqual(wall.CurrentPenetrationCount, 1, "前提：牆真的被穿透過");
            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 1e-3f, "被友軍子彈穿到崩解不得給盾");
        }

        // V4-i③：放第 3 面牆擠掉最舊那面，不得給盾。
        [UnityTest]
        public IEnumerator V4i3_TheThirdWallEvictingTheOldest_GrantsNoShield()
        {
            yield return Setup(new RuneTuning { CooldownSeconds = 0f });

            _input.RuneQuickCast();
            yield return null;
            RuneWall first = FirstAlive(_playerPool);
            Assert.IsNotNull(first, "第一面石牆未成形");

            _input.RuneQuickCast();
            yield return null;
            _input.RuneQuickCast();
            yield return null;

            Assert.IsFalse(first.IsAlive, "前提：第三面成形後最早那面應已坍塌");
            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 1e-3f, "被名冊擠掉不得給盾");
        }

        // V4-i④：中立牆被子彈打碎不得給盾（遠程不吃近戰特權，GDD.md:95）。
        [UnityTest]
        public IEnumerator V4i4_ANeutralWallDestroyedByBullets_GrantsNoShield()
        {
            yield return Setup(new RuneTuning());

            // TestWall_B 搬到彈道上（法線朝 +X），讓子彈一路打到它碎。
            TestWallTarget wallB = FindTestWall("TestWall_B");
            wallB.transform.SetPositionAndRotation(new Vector3(TurretX + 4f, 1.25f, LaneZ),
                                                   Quaternion.LookRotation(Vector3.right, Vector3.up));
            Assert.AreEqual(Faction.Neutral, wallB.OwnerFaction, "前提：測試牆是中立牆");
            yield return null;

            TestTurret turret = _bootstrap.Turret;
            turret.SetFiring(true);
            yield return WaitUntil(() => !wallB.IsAlive, 20f, "中立牆沒有在 20s 內被子彈打碎");
            turret.SetFiring(false);

            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 1e-3f, "遠程打碎中立牆不得給盾");
        }

        // V4-j：連砸兩面中立牆，護盾只刷新不疊加。
        [UnityTest]
        public IEnumerator V4j_SmashingTwoNeutralWallsInARow_NeverStacksTheShield()
        {
            yield return Setup(new RuneTuning());

            IRockShield shield = _bootstrap.Shield;
            float peak = 0f;

            TestWallTarget wallA = FindTestWall("TestWall_A");
            _input.TapTarget(wallA);
            float deadline = Time.time + 20f;
            while (wallA.IsAlive)
            {
                if (Time.time > deadline) Assert.Fail("英雄沒有在 20s 內砸碎 TestWall_A");
                if (shield.Amount > peak) peak = shield.Amount;
                yield return null;
            }
            if (shield.Amount > peak) peak = shield.Amount;
            Assert.AreEqual(_projectile.ShieldAmount, shield.Amount, 1e-3f, "第一面砸碎後應有滿額護盾");

            TestWallTarget wallB = FindTestWall("TestWall_B");
            _input.TapTarget(wallB);
            deadline = Time.time + 25f;
            while (wallB.IsAlive)
            {
                if (Time.time > deadline) Assert.Fail("英雄沒有在 25s 內砸碎 TestWall_B");
                if (shield.Amount > peak) peak = shield.Amount;
                yield return null;
            }
            if (shield.Amount > peak) peak = shield.Amount;

            Assert.AreEqual(_projectile.ShieldAmount, shield.Amount, 1e-3f, "第二面砸碎後仍是滿額，不是兩倍");
            Assert.LessOrEqual(peak, _projectile.ShieldAmount + 1e-3f,
                "整段期間護盾值不得超過滿值（實測峰值 " + peak + "）");
        }

        // ───────────────────────── V4-k～n：友軍彈道 ─────────────────────────

        // V4-k：己方牆被友軍子彈逐發穿透——木樁第 6 發起吃到 ×0.85 衰減、牆每發扣 10% 血與 0.5s 壽命、
        // 第 7 發穿透時崩解，整段不得出現第 8 次穿透。
        [UnityTest]
        public IEnumerator V4k_FriendlyBulletsPenetrateTheWall_DecayFromTheSixthShot_AndCollapseItOnTheSeventh()
        {
            yield return Setup(new RuneTuning());

            const int shots = 7;

            // 期望倍率不寫死：拿一顆全新的 RuneWallLogic 跑同一序列取得。
            RuneWallLogic reference = new RuneWallLogic(_rune);
            reference.Activate();
            float[] expectedMultiplier = new float[shots];
            for (int i = 0; i < shots; i++)
                Assert.IsTrue(reference.TryPenetrate(out expectedMultiplier[i]), "參考模型第 " + (i + 1) + " 發應放行");
            Assert.AreEqual(1f, expectedMultiplier[0], 1e-4f, "前提：前 5 發不衰減");
            Assert.AreEqual(_rune.DecayedDamageMultiplier, expectedMultiplier[5], 1e-4f, "前提：第 6 發起 ×0.85");

            // 先放牆、再開砲台（§4-4⑤ 的時序要求）。對照牆放在遠處，只用來讀「同一時刻未被穿透的壽命」。
            RuneWall wall = PlaceWallOnLane(FirstDead(_playerPool), 2f, _hero.HeroFaction);
            RuneWall control = FirstDead(_playerPool);
            control.Activate(new Vector3(15f, _rune.WallHeight * 0.5f, -15f), Quaternion.identity, _hero.HeroFaction, null, -1);
            BoxCollider wallCollider = wall.GetComponent<BoxCollider>();

            int deaths = 0;
            wall.OnDied += () => deaths++;

            float[] dummyDamage = new float[shots + 1];
            int dummyHits = 0;
            _dummy.OnDamaged += amount => { if (dummyHits < dummyDamage.Length) dummyDamage[dummyHits++] = amount; };

            float[] healthAt = new float[shots];
            float[] lifespanGapAt = new float[shots];
            int seen = 0;

            TestTurret turret = _bootstrap.Turret;
            turret.SetFiring(true);

            float deadline = Time.time + 20f;
            while (dummyHits < shots || seen < shots)
            {
                if (Time.time > deadline) break;
                if (turret.ShotsFired >= shots) turret.SetFiring(false); // 第 8 發不得離膛
                if (wall.CurrentPenetrationCount > seen && seen < shots)
                {
                    healthAt[seen] = wall.LogicHealth;
                    lifespanGapAt[seen] = control.RemainingLifespan - wall.RemainingLifespan;
                    seen++;
                }
                yield return null;
            }
            turret.SetFiring(false);

            Assert.AreEqual(shots, seen, "應該恰好觀察到 7 次穿透（實測 " + seen + "）");
            Assert.AreEqual(shots, wall.CurrentPenetrationCount, "整段不得出現第 8 次穿透");
            Assert.AreEqual(shots, dummyHits, "木樁應該恰好挨了 7 發（實測 " + dummyHits + "）");

            float perPenetration = _rune.WallMaxHealth * _rune.PenetrationHealthFraction;
            for (int i = 0; i < shots; i++)
            {
                Assert.AreEqual(_projectile.BulletDamage * expectedMultiplier[i], dummyDamage[i], 1e-2f,
                    "第 " + (i + 1) + " 發打到木樁的傷害");
                if (i < shots - 1) // 第 7 發把牆打塌，血量與壽命歸零，不在逐值比對之列
                {
                    Assert.AreEqual(_rune.WallMaxHealth - perPenetration * (i + 1), healthAt[i], 1e-2f,
                        "第 " + (i + 1) + " 發之後的牆血量");
                    Assert.AreEqual(_rune.PenetrationLifespanCost * (i + 1), lifespanGapAt[i], 0.05f,
                        "第 " + (i + 1) + " 發之後，牆的剩餘壽命應比未被穿透的對照牆少 " +
                        (_rune.PenetrationLifespanCost * (i + 1)) + "s");
                }
            }

            Assert.IsFalse(wall.IsAlive, "第 7 發穿透時牆必須崩解");
            Assert.IsFalse(wallCollider.enabled, "崩解後 Collider 必須關閉");
            Assert.AreEqual(1, deaths, "OnDied 必須恰好送出一次");
        }

        // V4-l：敵方牆擋下子彈——木樁血量不變，牆每發少一發子彈的傷害。
        [UnityTest]
        public IEnumerator V4l_AnEnemyWallOnTheLane_StopsTheBullets_AndTakesTheDamageItself()
        {
            yield return Setup(new RuneTuning());

            // 英雄站到彈道上（順帶驗 V4-p：英雄不是 ICombatTarget，子彈穿過他）並朝 +X，
            // 讓除錯鈕在他正前方 4m 生出敵方牆＝(−1, ?, 6)。
            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.Warp(new Vector3(-5f, 0f, LaneZ)));
            _hero.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            yield return null;

            RuneWall enemy = _bootstrap.EnemyWalls.Spawn();
            Assert.IsNotNull(enemy, "除錯鈕沒有生出敵方石牆");
            Assert.AreEqual(Faction.RedTeam, enemy.OwnerFaction);

            float dummyHealthBefore = _dummy.Health;
            int wallHits = 0;
            bool wrongAmount = false;
            enemy.OnDamaged += amount => { wallHits++; if (Mathf.Abs(amount - _projectile.BulletDamage) > 1e-2f) wrongAmount = true; };

            TestTurret turret = _bootstrap.Turret;
            turret.SetFiring(true);
            yield return new WaitForSeconds(2f);
            turret.SetFiring(false);
            yield return new WaitForSeconds(0.5f);

            Assert.GreaterOrEqual(wallHits, 3, "2s 內敵方牆應該至少挨了 3 發（實測 " + wallHits + "）");
            Assert.IsFalse(wrongAmount, "敵方牆每發應該恰好挨 " + _projectile.BulletDamage + " 點傷害");
            Assert.AreEqual(dummyHealthBefore, _dummy.Health, 1e-3f, "木樁血量不得變動：子彈應該全被敵方牆擋下");
            Assert.AreEqual(0, turret.Penetrations, "敵方牆不得被穿透");
            Assert.GreaterOrEqual(turret.Blocks, 3);
        }

        // V4-m：一發子彈通過一面己方牆後，穿透計數的增量恰為 1（即使子彈在牆內跨了多幀）。
        // **這一條對 Projectile 的 HasPenetrated 守衛零鑑別力**（r1 對抗審查 HIGH-3 實測：把守衛整行刪掉
        // 這條照樣綠）——Unity 的射線不回報「起點落在其內部」的 Collider，單一 BoxCollider 的牆本來就
        // 不可能被同一發子彈回報兩次。真正守住那個守衛的是下面的 V4-m2。
        [UnityTest]
        public IEnumerator V4m_OneBulletThroughOneWall_CountsExactlyOnce_EvenAcrossFrames()
        {
            yield return Setup(new RuneTuning());

            RuneWall wall = PlaceWallOnLane(FirstDead(_playerPool), 3f, _hero.HeroFaction);
            yield return null;

            // 讓每幀位移遠小於牆厚（0.6m）：子彈會在牆體內待上好幾幀。
            Time.captureDeltaTime = 1f / 240f;
            try
            {
                TestTurret turret = _bootstrap.Turret;
                turret.SetFiring(true);
                yield return WaitUntil(() => turret.ShotsFired >= 1, 5f, "砲台沒有射出第一發");
                turret.SetFiring(false);

                yield return WaitUntil(() => turret.TargetHits >= 1, 10f, "那一發沒有飛到木樁");

                Assert.AreEqual(1, turret.ShotsFired, "這條測試只能有一發子彈");
                Assert.AreEqual(1, wall.CurrentPenetrationCount,
                    "一發子彈通過一面牆，穿透計數的增量必須恰為 1（實測 " + wall.CurrentPenetrationCount + "）");
                Assert.AreEqual(1, turret.Penetrations);
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }
        }

        // V4-m2（r1 對抗審查 HIGH-3）：能真的紅的量法。替同一面己方牆加上第二個 BoxCollider（沿彈道錯開），
        // 一發子彈會在**不同幀**各回報一次同一面牆——這正是 HasPenetrated 守衛存在的理由。
        // 先把牆穿到衰減區（第 6 發起 ×0.85），倍率被連乘兩次時木樁受到的傷害也會不一樣。
        [UnityTest]
        public IEnumerator V4m2_TwoCollidersOnTheSameWall_StillCountAsOnePenetration()
        {
            yield return Setup(new RuneTuning());

            RuneWall wall = PlaceWallOnLane(FirstDead(_playerPool), 1.5f, _hero.HeroFaction);
            yield return null;

            // 牆的本地 +Z＝牆面法線＝世界 +X（PlaceWallOnLane 用 LookRotation(right)），
            // 所以 center.z = 2 代表沿彈道往前挪 2 × 0.6 = 1.2m，與第一個 Collider 不重疊。
            BoxCollider extra = wall.gameObject.AddComponent<BoxCollider>();
            extra.center = new Vector3(0f, 0f, 2f);
            extra.size = Vector3.one;

            _bootstrap.TargetRegistry.Unregister(wall);
            wall.RefreshColliderCache();
            _bootstrap.TargetRegistry.Register(wall);
            Assert.AreEqual(2, wall.TargetColliders.Length, "前提：這面牆現在有兩個 Collider");
            yield return null;

            // 先穿到衰減區：第 UndecayedPenetrations+1 發起倍率才不是 1
            Vector3 velocity = Vector3.right * _projectile.BulletSpeed;
            for (int i = 0; i < _rune.UndecayedPenetrations; i++)
                Assert.IsTrue(wall.TryPenetrateBullet(velocity, out float _), "前置穿透第 " + (i + 1) + " 發應放行");

            RuneWallLogic reference = new RuneWallLogic(_rune);
            reference.Activate();
            float expectedMultiplier = 0f;
            for (int i = 0; i <= _rune.UndecayedPenetrations; i++)
                Assert.IsTrue(reference.TryPenetrate(out expectedMultiplier), "參考模型第 " + (i + 1) + " 發應放行");
            Assert.AreEqual(_rune.DecayedDamageMultiplier, expectedMultiplier, 1e-4f, "前提：這一發落在衰減區");

            int penetrationsBefore = wall.CurrentPenetrationCount;
            int dummyHits = 0;
            float dummyDamage = 0f;
            _dummy.OnDamaged += amount => { dummyHits++; dummyDamage = amount; };

            TestTurret turret = _bootstrap.Turret;
            turret.SetFiring(true);
            yield return WaitUntil(() => turret.ShotsFired >= 1, 5f, "砲台沒有射出第一發");
            turret.SetFiring(false);
            yield return WaitUntil(() => dummyHits >= 1, 10f, "那一發沒有飛到木樁");
            yield return null;

            Assert.AreEqual(1, turret.ShotsFired, "這條測試只能有一發子彈");
            Assert.AreEqual(1, wall.CurrentPenetrationCount - penetrationsBefore,
                "一發子彈穿過同一面牆的兩個 Collider，穿透計數只能加 1（實測 +"
                + (wall.CurrentPenetrationCount - penetrationsBefore) + "）");
            Assert.AreEqual(1, dummyHits, "木樁只該挨這一發");
            Assert.AreEqual(_projectile.BulletDamage * expectedMultiplier, dummyDamage, 1e-2f,
                "木樁受到的傷害必須是子彈傷害 × 單次倍率，不是連乘兩次");
        }

        // r1 對抗審查 MEDIUM-4／LOW-1：符印牆與敵方牆現在也有頭頂血條，而且血條要跟著
        // 「友軍彈道穿透」掉（那條扣的是 RuneWallLogic 的血，不經 ReceiveDamage，不送 OnDamaged）。
        [UnityTest]
        public IEnumerator R5m4_TheRuneWallOverheadBar_TracksPenetrationDamage()
        {
            yield return Setup(new RuneTuning());

            RuneWall wall = PlaceWallOnLane(FirstDead(_playerPool), 2f, _hero.HeroFaction);
            yield return null;
            yield return null; // LateUpdate 把血條帶回來

            GameObject overhead = GameObject.Find(wall.name + "_Overhead");
            Assert.IsNotNull(overhead, "符印牆缺少頭頂血條（V8-② 量不到「牆血條下降」）");
            Transform background = overhead.transform.Find("BarBackground");
            Transform fill = overhead.transform.Find("BarFill");
            Assert.IsNotNull(background, "血條缺少背景");
            Assert.IsNotNull(fill, "血條缺少填滿");
            Assert.IsTrue(fill.GetComponent<Renderer>().enabled, "牆立起來之後血條應該顯示");

            float barWidth = background.localScale.x - 0.06f; // 背景比填滿寬 0.06（TargetOverheadDisplay）
            Assert.AreEqual(1f, fill.localScale.x / barWidth, 1e-3f, "滿血時血條應該是滿的");

            Assert.IsTrue(wall.TryPenetrateBullet(Vector3.right * _projectile.BulletSpeed, out float _));
            yield return null;
            yield return null;

            float expected = (_rune.WallMaxHealth - _rune.WallMaxHealth * _rune.PenetrationHealthFraction)
                             / _rune.WallMaxHealth;
            Assert.AreEqual(expected, fill.localScale.x / barWidth, 1e-3f,
                "穿透一次後血條比例應為 270/300（實測 " + (fill.localScale.x / barWidth) + "）");
        }

        // r1 對抗審查 MEDIUM-7：同一面池牆歷經三種死因之後，格點的登記／撤銷必須對稱。
        [UnityTest]
        public IEnumerator R5m7_APooledWallThroughEveryDeathPath_LeavesTheNavGridSymmetric()
        {
            RuneTuning tuning = new RuneTuning { CooldownSeconds = 0f };
            yield return Setup(tuning);

            BlockGrid grid = _bootstrap.NavGrid;
            Assert.IsNotNull(grid, "Phase1Bootstrap 沒有建立阻擋格點");
            int baseline = grid.BlockedCount;
            float melee = MeasureMeleeDamage();

            // 死因 1：被近戰打碎
            _input.RuneQuickCast();
            yield return null;
            RuneWall first = FirstAlive(_playerPool);
            Assert.IsNotNull(first, "第一面石牆未成形");
            for (int i = 0; i < Mathf.CeilToInt(_rune.WallMaxHealth / melee); i++)
                first.ReceiveDamage(melee, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(first.IsAlive, "死因 1：應被近戰打碎");
            yield return null;

            // 死因 2：壽命到期
            _input.RuneQuickCast();
            yield return null;
            Assert.IsNotNull(FirstAlive(_playerPool), "第二面石牆未成形");
            yield return new WaitForSeconds(tuning.WallLifespanSeconds + 0.4f);
            Assert.AreEqual(0, AliveCount(_playerPool), "死因 2：應已壽命到期");

            // 死因 3：被第 3 面擠掉
            _input.RuneQuickCast();
            yield return null;
            RuneWall third = FirstAlive(_playerPool);
            Assert.IsNotNull(third, "第三輪第一面石牆未成形");
            _input.RuneQuickCast();
            yield return null;
            _input.RuneQuickCast();
            yield return null;
            Assert.IsFalse(third.IsAlive, "死因 3：第 3 面成形後最舊那面應已坍塌");

            yield return new WaitForSeconds(tuning.WallLifespanSeconds + 0.4f);
            Assert.AreEqual(0, AliveCount(_playerPool), "收尾：所有石牆都該消失");
            yield return null;

            Assert.AreEqual(0, grid.NegativeStampCount,
                "格點出現過負的引用計數：登記與撤銷不對稱（實測 " + grid.NegativeStampCount + "）");
            Assert.AreEqual(baseline, grid.BlockedCount,
                "所有石牆消失後，阻擋格數必須回到開場基線（基線 " + baseline + "、實測 " + grid.BlockedCount + "）");
        }

        // r1 對抗審查 MEDIUM-2：兩顆新按鈕要走**真實**的輸入分流（InputRoutingManager 的區域判定），
        // 不是直接呼叫按鈕的處理常式。按鈕矩形算錯（例如與 GRID 鈕重疊）在這裡會露出來。
        [UnityTest]
        public IEnumerator R5m2_TheHudButtons_AreReachedThroughTheRealTouchRouting()
        {
            yield return Setup(new RuneTuning());

            bool moved = false;
            ICombatTarget picked = null;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moved = true;
            _bootstrap.InputService.OnCombatTargetSelected += target => picked = target;

            TestTurret turret = _bootstrap.Turret;
            IDebugHudPanel hud = _bootstrap.HudPanel;
            Assert.IsFalse(turret.IsFiring, "前提：砲台預設關閉");

            Assert.IsTrue(hud.TryGetTurretButtonScreenPoint(out float turretX, out float turretY),
                "HUD 沒有 TURRET 鈕");
            _bootstrap.WorldTapInput.SendScreenTap(turretX, turretY);
            yield return null;
            Assert.IsTrue(turret.IsFiring, "對 TURRET 鈕的矩形送真實觸控應該把砲台打開");
            StringAssert.Contains("ON", hud.TurretButtonLabel);
            turret.SetFiring(false);

            int aliveBefore = _bootstrap.EnemyWalls.AliveCount();
            Assert.IsTrue(hud.TryGetEnemyWallButtonScreenPoint(out float wallX, out float wallY),
                "HUD 沒有 ENEMY WALL 鈕");
            _bootstrap.WorldTapInput.SendScreenTap(wallX, wallY);
            yield return null;
            Assert.AreEqual(aliveBefore + 1, _bootstrap.EnemyWalls.AliveCount(),
                "對 ENEMY WALL 鈕的矩形送真實觸控應該生出一面紅隊牆");

            Assert.IsFalse(moved, "點 HUD 按鈕不得滲透成移動指令");
            Assert.IsNull(picked, "點 HUD 按鈕不得滲透成鎖定目標");
        }

        // V4-n：不穿隧。每幀位移 1.0m > 牆厚 0.6m，20 發全部必須被登記。
        // 擋牆刻意放在「樸素點查詢的取樣點之間」（離砲台 5.2～5.8m，取樣點在 5.0 與 6.0），
        // 所以只查當幀端點的實作在同條件下會全部漏掉。
        [UnityTest]
        public IEnumerator V4n_AtTwentyFps_TheSweepStillRegistersEveryShot()
        {
            yield return Setup(new RuneTuning());

            const int shots = 20;

            // 擋牆必須撐得過 20 發，否則「穿透數＋擋下數 == 發射數」在牆死後就對不齊了。
            RuneWall wall = FirstDead(_playerPool);
            wall.Initialize(new RuneTuning { WallMaxHealth = 1e6f, WallLifespanSeconds = 1e6f });
            PlaceWallOnLane(wall, 5.5f, Faction.RedTeam);
            yield return null;

            Time.captureDeltaTime = 1f / 20f;
            try
            {
                TestTurret turret = _bootstrap.Turret;
                turret.SetFiring(true);
                yield return WaitUntil(() => turret.ShotsFired >= shots, 30f, "砲台沒有射滿 20 發");
                turret.SetFiring(false);
                yield return new WaitForSeconds(1f); // 讓最後幾發飛完

                Assert.AreEqual(shots, turret.ShotsFired);
                Assert.AreEqual(shots, turret.Penetrations + turret.Blocks,
                    "每一發都必須被登記（穿透 " + turret.Penetrations + " ＋ 擋下 " + turret.Blocks +
                    " 應等於發射 " + turret.ShotsFired + "）");
                Assert.AreEqual(0, turret.TargetHits, "一發都不該漏穿到木樁");
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }
        }

        // ───────────────────────── V4-o～r：分池、物理、開關、HUD ─────────────────────────

        // V4-o：敵方牆不佔玩家名冊。
        [UnityTest]
        public IEnumerator V4o_EnemyWalls_DoNotEatThePlayersRosterSeats()
        {
            yield return Setup(new RuneTuning());

            // 組裝端交給玩家的池裡不得有敵方牆
            RuneWall[] pool = _caster.Pool;
            Assert.AreEqual(3, pool.Length, "玩家池應恰好 3 面（敵方牆被混進來就會變 5 面）");
            for (int i = 0; i < pool.Length; i++)
                Assert.IsTrue(pool[i].name.StartsWith("RuneWall_Pool_"), "玩家池裡混進了 " + pool[i].name);

            // r1 對抗審查 CRITICAL-1：池滿時 Spawn() 曾經回 null（FIFO 擠掉最舊是死碼），
            // 而舊斷言只看 AliveCount()==2——有沒有擠掉存活數都是 2，對它要守的行為恆真。
            RuneWall firstEnemy = _bootstrap.EnemyWalls.Spawn();
            Assert.IsNotNull(firstEnemy, "第 1 次生成敵方牆失敗");
            yield return null;
            Assert.IsNotNull(_bootstrap.EnemyWalls.Spawn(), "第 2 次生成敵方牆失敗");
            yield return null;

            Vector3 heroPos = _hero.transform.position;
            Vector3 heroForward = _hero.transform.forward;
            RuneWall thirdEnemy = _bootstrap.EnemyWalls.Spawn();
            yield return null;
            Assert.IsNotNull(thirdEnemy, "池滿時第 3 次生成必須擠掉最舊那面並回傳新牆，不得回 null");
            Assert.IsFalse(firstEnemy.IsAlive, "第 3 次生成要擠掉的是最舊那面（第 1 面）");
            Assert.AreEqual(2, _bootstrap.EnemyWalls.AliveCount(), "敵方牆同時存活上限仍是 2");

            Vector3 expectedCenter = heroPos + heroForward * _rune.QuickCastDistance;
            Vector3 offset = thirdEnemy.transform.position - expectedCenter;
            offset.y = 0f;
            Assert.LessOrEqual(offset.magnitude, 0.1f,
                "新生的敵方牆應該在英雄正前方 " + _rune.QuickCastDistance + "m：" + thirdEnemy.transform.position);

            // 連按 6 次，每次都必須生得出來（不能有「按了沒反應」的那一下）
            for (int press = 0; press < 6; press++)
            {
                Assert.IsNotNull(_bootstrap.EnemyWalls.Spawn(), "第 " + (press + 1) + " 次連按沒有生出敵方牆");
                yield return null;
            }
            Assert.AreEqual(2, _bootstrap.EnemyWalls.AliveCount(), "連按之後同時存活數仍是 2");

            _caster.Initialize(_input, _input, _hero.transform, Camera.main,
                               new RuneTuning { CooldownSeconds = 0f }, pool, _hero.HeroFaction);

            _input.RuneQuickCast();
            yield return null;
            RuneWall firstOwn = FirstAlive(pool);
            Assert.IsNotNull(firstOwn, "第一面己方牆未成形");

            _input.RuneQuickCast();
            yield return null;
            Assert.AreEqual(2, AliveCount(pool), "按過除錯鈕之後，玩家仍應放得滿 2 面");

            _input.RuneQuickCast();
            yield return null;
            Assert.IsFalse(firstOwn.IsAlive, "第 3 面己方牆要擠掉的是己方最舊那面");
            Assert.AreEqual(2, AliveCount(pool), "己方存活數仍應是 2");
            Assert.AreEqual(2, _bootstrap.EnemyWalls.AliveCount(), "敵方牆不得被玩家的名冊擠掉");
        }

        // V4-p：砲台與子彈不改變既有物理。
        [UnityTest]
        public IEnumerator V4p_TheTurretAndItsBullets_ChangeNoExistingPhysics()
        {
            yield return Setup(new RuneTuning());

            TestTurret turret = _bootstrap.Turret;
            Assert.AreEqual(0, turret.GetComponentsInChildren<Collider>(true).Length,
                "砲台不得有任何 Collider（不擋路、不吃點擊）");

            Projectile[] bullets = Object.FindObjectsOfType<Projectile>(true);
            Assert.AreEqual(_projectile.BulletPoolSize, bullets.Length, "子彈池應預建 4 發");
            for (int i = 0; i < bullets.Length; i++)
                Assert.AreEqual(0, bullets[i].GetComponentsInChildren<Collider>(true).Length,
                    "子彈不得有 Collider");

            // 英雄站在彈道上：他不是 ICombatTarget，子彈應該穿過去打到木樁。
            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.Warp(new Vector3(-4f, 0f, LaneZ)));
            yield return null;

            int blockedBefore = _bootstrap.NavGrid.BlockedCount;
            float dummyBefore = _dummy.Health;

            turret.SetFiring(true);
            yield return new WaitForSeconds(1.5f);
            turret.SetFiring(false);
            yield return new WaitForSeconds(0.5f);

            Assert.Less(_dummy.Health, dummyBefore, "子彈應該穿過站在彈道上的英雄、打到木樁");
            Assert.AreEqual(blockedBefore, _bootstrap.NavGrid.BlockedCount,
                "子彈不得登記阻擋格點（BlockedCount 在整段量測中只該隨牆變動）");
        }

        // V4-r：砲台預設關閉。
        [UnityTest]
        public IEnumerator V4r_TheTurretIsOffByDefault_AndHonoursTheSwitch()
        {
            yield return Setup(new RuneTuning());

            TestTurret turret = _bootstrap.Turret;
            float dummyMax = _dummy.MaxHealth;

            yield return new WaitForSeconds(3f);
            Assert.IsFalse(turret.IsFiring, "砲台必須預設關閉");
            Assert.AreEqual(0, turret.ShotsFired, "沒按鈕就不該有任何發射");
            Assert.AreEqual(dummyMax, _dummy.Health, 1e-3f, "沒按鈕時木樁血量必須文風不動");

            turret.SetFiring(true);
            yield return new WaitForSeconds(1f);
            int firedInOneSecond = turret.ShotsFired;
            Assert.GreaterOrEqual(firedInOneSecond, 3, "開啟後 1s 內應至少射出 3 發（實測 " + firedInOneSecond + "）");

            turret.SetFiring(false);
            int firedAtStop = turret.ShotsFired;
            float dummyAtStop = _dummy.Health;
            yield return new WaitForSeconds(1f);

            Assert.AreEqual(firedAtStop, turret.ShotsFired, "關閉後不得再發射");
            Assert.Less(_dummy.Health, dummyAtStop, "關閉時已在飛的子彈仍須照常結算");
        }

        // V4-q：除錯 HUD 的兩顆鈕與護盾列。
        [UnityTest]
        public IEnumerator V4q_TheDebugHud_TogglesTheTurret_SpawnsARedWall_AndShowsTheShield()
        {
            yield return Setup(new RuneTuning());

            IDebugHudPanel hud = _bootstrap.HudPanel;
            Assert.IsNotNull(hud, "場景缺少除錯 HUD");
            TestTurret turret = _bootstrap.Turret;

            Assert.IsFalse(turret.IsFiring);
            StringAssert.Contains("OFF", hud.TurretButtonLabel, "預設狀態的鈕應該寫 OFF");

            hud.PressTurretButton();
            yield return null;
            Assert.IsTrue(turret.IsFiring, "按 TURRET 鈕應該把砲台打開");
            StringAssert.Contains("ON", hud.TurretButtonLabel, "打開後鈕上的文字要跟著變");

            hud.PressTurretButton();
            yield return null;
            Assert.IsFalse(turret.IsFiring, "再按一次應該關掉");
            StringAssert.Contains("OFF", hud.TurretButtonLabel);

            int aliveBefore = _bootstrap.EnemyWalls.AliveCount();
            hud.PressEnemyWallButton();
            yield return null;
            Assert.AreEqual(aliveBefore + 1, _bootstrap.EnemyWalls.AliveCount(), "按 ENEMY WALL 鈕應該多一面牆");

            RuneWall spawned = null;
            RuneWall[] all = Object.FindObjectsOfType<RuneWall>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].IsAlive && all[i].OwnerFaction == Faction.RedTeam) spawned = all[i];
            Assert.IsNotNull(spawned, "找不到剛生出來的紅隊牆");

            Color wallColor = spawned.GetComponentInChildren<Renderer>().sharedMaterial.color;
            Assert.Greater(wallColor.r, wallColor.g, "敵方牆必須是紅的（實際 " + wallColor + "）");
            Assert.Greater(wallColor.r, wallColor.b, "敵方牆必須是紅的（實際 " + wallColor + "）");

            _bootstrap.Shield.Grant();
            yield return null;
            Assert.AreEqual(Mathf.RoundToInt(_projectile.ShieldAmount).ToString(), hud.ShieldValueLabel,
                "護盾列應該顯示滿額數值");

            yield return new WaitForSeconds(_projectile.ShieldDurationSeconds + 0.2f);
            Assert.AreEqual("0", hud.ShieldValueLabel, "倒數走完後護盾列應歸零");
        }
    }
}
