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
    // Phase 2 批 4 步驟 B：三大元素反應的 Unity 端接線（PHASE2_BATCH4_PLAN.md §5 V4-a～V4-r）。
    // V4-o／V4-p（HUD 版面）在 EditMode 的 DebugHudLayoutTests；V5（零配置）在 ZeroAllocationTests。
    //
    // 數值一律讀 ElementTuning／HeroTuningAsset，測試裡不寫死寬鬆常數。
    // 全域容差（§5 開頭）：位置 0.10m、時間 0.034s、傷害 0.01、DoT 總量 0.5。
    // PlayMode 一律 Time.captureDeltaTime = 1/60f（決定性；TearDown 還原成 0）。
    public sealed class ElementReactionPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float Fps = 60f;
        private const float PositionTolerance = 0.10f;
        private const float TimeTolerance = 0.034f;
        private const float DamageTolerance = 0.01f;
        private const float DotTolerance = 0.5f;
        private const float BoundaryClearance = 0.5f;

        private HeroController _hero;
        private HeroLocomotion _locomotion;
        private Phase1Bootstrap _bootstrap;
        private ElementField _field;
        private ElementTuning _tuning;
        private CombatFeedbackService _feedback;
        private DummyTarget _dummy;
        private RuneCaster _caster;
        private EnemyWallSpawner _enemyWalls;
        private ScriptedInput _input;
        private readonly RuneTuning _rune = new RuneTuning();

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
        }

        private IEnumerator Setup()
        {
            Time.captureDeltaTime = 1f / Fps;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null; // Awake／OnEnable／Start（含 Phase1Bootstrap 的接線）全部跑完

            _hero = Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(_hero, "場景裡找不到 HeroController");
            _locomotion = _hero.GetComponent<HeroLocomotion>();
            Assert.IsNotNull(_locomotion, "英雄身上沒有 HeroLocomotion");
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            Assert.IsNotNull(_bootstrap, "場景缺少 Phase1Bootstrap");
            _field = _bootstrap.ElementField;
            Assert.IsNotNull(_field, "場景缺少 ElementField（SceneBuilder 沒有預建？）");
            _tuning = _bootstrap.ElementTuning;
            _feedback = Object.FindObjectOfType<CombatFeedbackService>();
            _dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(_dummy, "場景缺少木樁");
            _caster = Object.FindObjectOfType<RuneCaster>();
            Assert.IsNotNull(_caster, "場景缺少 RuneCaster");
            _enemyWalls = Object.FindObjectOfType<EnemyWallSpawner>();
            Assert.IsNotNull(_enemyWalls, "場景缺少敵方石牆池");

            _input = new ScriptedInput();
            _hero.Initialize(_input, _feedback, Camera.main, Object.FindObjectOfType<HapticFeedbackService>());
            PlaceHero(Vector3.zero, 0f);
            yield return null;
        }

        // ───────────────────────── 小工具 ─────────────────────────

        private void PlaceHero(Vector3 position, float yawDegrees)
        {
            _hero.StopMoving();
            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.Warp(position), "無法把英雄放到 " + position);
            _hero.transform.position = position;
            _hero.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        // captureDeltaTime 固定 1/60 ⇒ 幀數即秒數，等待長度是決定性的（不是掛鐘時間）。
        private static IEnumerator Seconds(float seconds)
        {
            int frames = Mathf.RoundToInt(seconds * Fps);
            for (int i = 0; i < frames; i++) yield return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(Flat(a), Flat(b));
        }

        // V7 點名 ⑨：受試者不得恰好站在區域邊界上（距圓心＝半徑時，浮點數決定紅綠）。
        // 刻意的邊界反例自己不呼叫這個。
        private void AssertClearOfZoneBoundaries(Vector3 point, string who)
        {
            for (int slot = 0; slot < _field.ZoneCapacity; slot++)
            {
                if (!_field.TryGetZoneBySlot(slot, out ElementZone zone)) continue;
                float distance = Mathf.Sqrt((point.x - zone.X) * (point.x - zone.X)
                                            + (point.z - zone.Z) * (point.z - zone.Z));
                Assert.GreaterOrEqual(Mathf.Abs(distance - zone.Radius), BoundaryClearance,
                    who + " 離 " + zone.Kind + " 區域邊界只有 " + Mathf.Abs(distance - zone.Radius)
                    + "m（要求 ≥ " + BoundaryClearance + "m）：浮點數會決定這條測試的紅綠");
            }
        }

        private RuneWall ActivateWall(RuneWall wall, Vector3 groundPoint, Vector3 facing, Faction owner)
        {
            Vector3 position = new Vector3(groundPoint.x, _rune.WallHeight * 0.5f, groundPoint.z);
            wall.Activate(position, Quaternion.LookRotation(Flat(facing).normalized, Vector3.up), owner, null, -1);
            return wall;
        }

        // 走「英雄前方 4m」的真實鈕路徑（Phase1Bootstrap → DebugHud → 委派 → 冷卻閘 → ElementField）。
        private void PressWater() { _bootstrap.PressElementWaterButton(); }
        private void PressFire() { _bootstrap.PressElementFireButton(); }
        private void PressWind() { _bootstrap.PressElementWindButton(); }
        private void PressElemFaction() { _bootstrap.PressElementFactionButton(); }

        // 紅流沙（敵對藍英雄）：水域中心在 (0,10)，流沙半徑 4；牆立在水域內、離英雄行進路線 2m 外。
        // 回傳流沙的 id。
        private IEnumerator BuildHostileQuicksand()
        {
            PlaceHero(new Vector3(0f, 0f, 6f), 0f);   // 面向 +Z，前方 4m ＝ (0,0,10)
            PressElemFaction();                        // BLUE → RED
            Assert.AreEqual(Faction.RedTeam, _bootstrap.ElementCastFaction, "ELEM 鈕沒有切到 RED");
            PressWater();
            yield return null;

            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Water), "WATER 鈕沒有生出水域");

            // 「先往前走約 2m 再立牆」：原地連按會讓英雄恰好站在流沙邊界上（§5 V4-a 的盤面約束）。
            PlaceHero(new Vector3(-3f, 0f, 10f), 90f); // 站進水域西側，離流沙邊界 1m；面向 +X
            yield return null;

            ActivateWall(_enemyWalls.Pool[0], new Vector3(0f, 0f, 12f), Vector3.forward, Faction.RedTeam);
            yield return null;

            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Water), "水域沒有被岩消耗");
            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Quicksand), "流沙沒有成形");
            int zoneId = _field.FindZoneIdContaining(_hero.transform.position, ElementZoneKind.Quicksand);
            Assert.GreaterOrEqual(zoneId, 0, "英雄不在流沙內");
            AssertClearOfZoneBoundaries(_hero.transform.position, "英雄");
        }

        // ───────────────────── V4-a：流沙縛足（正例，真實路徑）─────────────────────

        [UnityTest]
        public IEnumerator V4a_HostileQuicksand_RootsTheHero_ThenSlowsHimToSixtyFivePercent()
        {
            yield return Setup();

            // ① 無流沙下的基準：同一條路徑、同一個窗口（起步 0.3s 後開始量的 0.5s）
            PlaceHero(new Vector3(-3f, 0f, 10f), 90f);
            yield return null;
            _input.TapGround(new Vector3(3f, 0f, 10f));
            yield return Seconds(0.3f);
            Vector3 baseFrom = _hero.transform.position;
            yield return Seconds(0.5f);
            float baseline = PlanarDistance(baseFrom, _hero.transform.position);
            Assert.Greater(baseline, 1f, "基準窗口內英雄根本沒走（" + baseline + "m）：後面的比值沒有意義");

            // ② 建流沙（紅水＋紅牆），英雄（藍）在區內
            yield return BuildHostileQuicksand();
            Vector3 rootedFrom = _hero.transform.position;

            Assert.IsTrue(_locomotion.IsMovementLocked, "流沙成形當幀英雄就該被縛足");
            Assert.AreEqual(_tuning.QuicksandSlowMultiplier, _locomotion.SpeedMultiplier, 1e-4f,
                "縛足期間的移速倍率也該是 0.65");

            // ③ 縛足期間：移動指令無效、滑步無效且不消耗充能
            int chargesBefore = _hero.CadenceMover.CurrentCharges;
            _input.TapGround(new Vector3(3f, 0f, 10f));
            yield return Seconds(0.5f);
            Assert.Less(PlanarDistance(rootedFrom, _hero.transform.position), PositionTolerance,
                "縛足期間英雄仍然走動了");
            Assert.IsFalse(_hero.TryBeginCadenceDash(1f, 0f), "縛足期間不得滑步");
            Assert.AreEqual(chargesBefore, _hero.CadenceMover.CurrentCharges, "滑步被擋下時不得消耗充能");
            Assert.IsTrue(_locomotion.IsMovementLocked);

            // ④ 1.2s 之後：解除縛足，穩態速度比 ＝ 0.65 ± 0.035
            yield return Seconds(_tuning.RootDurationSeconds - 0.5f + 0.05f);
            Assert.IsFalse(_locomotion.IsMovementLocked, "縛足時長過了仍然鎖著");
            Assert.AreEqual(_tuning.QuicksandSlowMultiplier, _locomotion.SpeedMultiplier, 1e-4f);

            _input.TapGround(new Vector3(3f, 0f, 10f));
            yield return Seconds(0.3f);
            Vector3 slowFrom = _hero.transform.position;

            int windowFrames = Mathf.RoundToInt(0.5f * Fps);
            int quicksandZoneId = _field.FindZoneIdContaining(_hero.transform.position, ElementZoneKind.Quicksand);
            for (int i = 0; i < windowFrames; i++)
            {
                yield return null;
                // 量測窗口內每一幀都要確認量到的是「在流沙裡走」，不是繞牆或已經走出去
                Assert.AreEqual(quicksandZoneId,
                    _field.FindZoneIdContaining(_hero.transform.position, ElementZoneKind.Quicksand),
                    "量測窗口內英雄離開了流沙（第 " + i + " 幀）");
                Assert.AreEqual(SteerMode.Direct, _locomotion.LastSteerMode,
                    "量測窗口內英雄在繞牆（第 " + i + " 幀）：量到的不是減速");
            }
            float slowed = PlanarDistance(slowFrom, _hero.transform.position);

            float ratio = slowed / baseline;
            Assert.AreEqual(_tuning.QuicksandSlowMultiplier, ratio, 0.035f,
                "穩態速度比 ＝ " + ratio + "（基準 " + baseline + "m、流沙內 " + slowed + "m）");
        }

        // ───────────────────── V4-b：流沙（最接近的反例）─────────────────────

        [UnityTest]
        public IEnumerator V4b_AWallLandingZeroPointOneMetreOutsideTheWater_MakesNoQuicksand()
        {
            yield return Setup();

            PlaceHero(new Vector3(0f, 0f, 6f), 0f);
            PressElemFaction();
            PressWater();
            yield return null;
            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Water));

            // 牆心距水域圓心 WaterRadius + 0.10m ＝ 剛好落在圓外
            float justOutside = _tuning.WaterRadius + 0.10f;
            ActivateWall(_enemyWalls.Pool[0], new Vector3(0f, 0f, 10f + justOutside), Vector3.forward, Faction.RedTeam);
            yield return null;

            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Quicksand), "圓外 0.10m 不該生流沙");
            Assert.AreEqual(1, _field.ActiveZoneCount, "場上應該只剩水域那一個區域");
            Assert.IsFalse(_locomotion.IsMovementLocked);
            Assert.AreEqual(1f, _locomotion.SpeedMultiplier, 1e-4f);

            // 英雄照常全速走到 6m 外
            PlaceHero(new Vector3(-3f, 0f, 10f), 90f);
            yield return null;
            Vector3 from = _hero.transform.position;
            _input.TapGround(new Vector3(3f, 0f, 10f));
            yield return Seconds(0.3f);
            Vector3 windowFrom = _hero.transform.position;
            yield return Seconds(0.5f);
            float travelled = PlanarDistance(windowFrom, _hero.transform.position);
            Assert.Greater(travelled, 2.4f, "沒有流沙時 0.5s 應該走超過 2.4m（實測 " + travelled + "m）");
            Assert.Greater(PlanarDistance(from, _hero.transform.position), 2.5f);
        }

        // ───────────────────── V4-c：流沙陣營方向（不生效）─────────────────────

        [UnityTest]
        public IEnumerator V4c_FriendlyQuicksand_DoesNotRootOrSlowTheHero()
        {
            yield return Setup();

            // 先量無流沙基準
            PlaceHero(new Vector3(-3f, 0f, 10f), 90f);
            yield return null;
            _input.TapGround(new Vector3(3f, 0f, 10f));
            yield return Seconds(0.3f);
            Vector3 baseFrom = _hero.transform.position;
            yield return Seconds(0.5f);
            float baseline = PlanarDistance(baseFrom, _hero.transform.position);

            // 藍水＋藍牆（ELEM 預設 BLUE，英雄也是 BlueTeam）
            PlaceHero(new Vector3(0f, 0f, 6f), 0f);
            Assert.AreEqual(Faction.BlueTeam, _bootstrap.ElementCastFaction, "ELEM 預設應為 BLUE");
            PressWater();
            yield return null;
            PlaceHero(new Vector3(-3f, 0f, 10f), 90f);
            yield return null;
            ActivateWall(_caster.Pool[0], new Vector3(0f, 0f, 12f), Vector3.forward, Faction.BlueTeam);
            yield return null;

            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Quicksand), "藍流沙沒有成形");
            Assert.GreaterOrEqual(_field.FindZoneIdContaining(_hero.transform.position, ElementZoneKind.Quicksand), 0,
                "英雄不在那個流沙裡：這條測試就沒有鑑別力了");
            AssertClearOfZoneBoundaries(_hero.transform.position, "英雄");

            Assert.IsFalse(_locomotion.IsMovementLocked, "自家流沙不得困住自己");
            Assert.AreEqual(1f, _locomotion.SpeedMultiplier, 1e-4f, "自家流沙不得減速");
            Assert.AreEqual(-1, _hero.HostileQuicksandZoneId);

            _input.TapGround(new Vector3(3f, 0f, 10f));
            yield return Seconds(0.3f);
            Vector3 windowFrom = _hero.transform.position;
            yield return Seconds(0.5f);
            float travelled = PlanarDistance(windowFrom, _hero.transform.position);
            Assert.AreEqual(baseline, travelled, PositionTolerance,
                "自家流沙內的移動距離與無流沙時不一致（基準 " + baseline + "m、實測 " + travelled + "m）");
        }

        // ───────────────────── V4-d／V4-e：爆沸與救援 ─────────────────────

        // 盤面：藍水＋藍牆罩住木樁（RedTeam）。回傳流沙 id。
        private IEnumerator BuildQuicksandOverTheDummy(Faction wallOwner)
        {
            _dummy.transform.position = new Vector3(0f, 1f, 6f);
            PlaceHero(new Vector3(0f, 0f, 2f), 0f);       // 前方 4m ＝ 木樁腳下 (0,0,6)
            PressWater();
            yield return null;
            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Water));

            ActivateWall(_caster.Pool[0], new Vector3(0f, 0f, 8f), Vector3.forward, wallOwner);
            yield return null;
            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Quicksand), "流沙沒有罩住木樁");
            AssertClearOfZoneBoundaries(_dummy.transform.position, "木樁");

            PlaceHero(Vector3.zero, 0f);                   // 退到流沙外，前方 4m ＝ (0,0,4) 仍在流沙內
            yield return null;
        }

        [UnityTest]
        public IEnumerator V4d_FireFromTheQuicksandOwnFaction_Boils_AndDealsEightyToTheHostileDummy()
        {
            yield return Setup();
            yield return BuildQuicksandOverTheDummy(Faction.BlueTeam);

            int quicksandId = _field.FindZoneIdContaining(new Vector3(0f, 0f, 6f), ElementZoneKind.Quicksand);
            Assert.GreaterOrEqual(quicksandId, 0);
            RuneWall friendlyWall = _caster.Pool[0];
            float wallHealthBefore = friendlyWall.Health;
            float dummyHealthBefore = _dummy.Health;
            int boilsBefore = _field.BoilCount;

            Assert.AreEqual(Faction.BlueTeam, _bootstrap.ElementCastFaction);
            PressFire();   // 藍火打藍流沙 ＝ 爆沸

            Assert.AreEqual(boilsBefore + 1, _field.BoilCount, "沒有觸發爆沸");
            Assert.AreEqual(_tuning.BoilDamage, dummyHealthBefore - _dummy.Health, DamageTolerance,
                "爆沸對敵對木樁的傷害不是 80");
            Assert.IsFalse(_field.TryGetZoneById(quicksandId, out ElementZone _), "爆沸沒有終止流沙");
            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Burning), "爆沸不得留下燃燒區");
            // §4-7／V7 ⑪：自家石牆不吃自家元素傷害
            Assert.AreEqual(wallHealthBefore, friendlyWall.Health, DamageTolerance,
                "藍爆沸打到了藍方自己的石牆");
            yield return null;
        }

        [UnityTest]
        public IEnumerator V4e_FireFromTheOtherFaction_Rescues_WithoutDamage_AndFreesTheHeroTheSameFrame()
        {
            yield return Setup();

            // ① 木樁版：藍流沙罩紅木樁，紅火＝救援（木樁不掉血、流沙終止）
            yield return BuildQuicksandOverTheDummy(Faction.BlueTeam);
            int quicksandId = _field.FindZoneIdContaining(new Vector3(0f, 0f, 6f), ElementZoneKind.Quicksand);
            float dummyHealthBefore = _dummy.Health;
            int rescuesBefore = _field.RescueCount;

            PressElemFaction();   // BLUE → RED
            Assert.AreEqual(Faction.RedTeam, _bootstrap.ElementCastFaction);
            PressFire();

            Assert.AreEqual(rescuesBefore + 1, _field.RescueCount, "沒有觸發救援（寫反成爆沸？）");
            Assert.AreEqual(dummyHealthBefore, _dummy.Health, DamageTolerance, "救援不得造成任何傷害");
            Assert.IsFalse(_field.TryGetZoneById(quicksandId, out ElementZone _), "救援沒有終止流沙");
            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Burning));

            // ② 英雄版：紅流沙困住藍英雄，縛足中被藍火救援 → 當幀解除
            yield return Setup();
            yield return BuildHostileQuicksand();
            Assert.IsTrue(_locomotion.IsMovementLocked, "英雄應該正在被縛足");

            PressElemFaction();   // RED → BLUE（BuildHostileQuicksand 已經按過一次）
            Assert.AreEqual(Faction.BlueTeam, _bootstrap.ElementCastFaction);
            PressFire();          // 英雄面向 +X，火落 (1,0,10)，在流沙內
            yield return null;    // ElementField(-900) → HeroController 各跑一次 Update

            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Quicksand), "救援沒有終止流沙");
            Assert.IsFalse(_locomotion.IsMovementLocked, "救援沒有當幀解除縛足");
            Assert.AreEqual(1f, _locomotion.SpeedMultiplier, 1e-4f, "救援沒有當幀解除減速");

            Vector3 from = _hero.transform.position;
            _input.TapGround(new Vector3(3f, 0f, 10f));
            yield return Seconds(0.3f);
            Assert.Greater(PlanarDistance(from, _hero.transform.position), 1f,
                "救援之後下一個移動指令沒有立刻生效");
        }

        // ───────────────────── V4-f：蒸氣遮蔽（點擊當下＋鎖定後＋受擊顯影）─────────────────────

        // 霧心刻意不放在木樁身上：要讓「木樁在霧內、攻擊距離內的英雄在霧外」同時成立。
        private static readonly Vector3 SteamCentre = new Vector3(0f, 0f, 9f);

        private void CastSteamOverTheDummy()
        {
            int steamsBefore = _field.CountZonesOfKind(ElementZoneKind.Steam);
            _field.CastWater(SteamCentre, (int)Faction.BlueTeam);
            _field.CastFire(SteamCentre, (int)Faction.BlueTeam);
            Assert.AreEqual(steamsBefore + 1, _field.CountZonesOfKind(ElementZoneKind.Steam), "蒸氣沒有成形");
            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Water), "水域沒有被火消耗");
            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Burning), "蒸氣反應不得留下燃燒區");
        }

        [UnityTest]
        public IEnumerator V4f1_ATargetInsideSteam_CannotBeSelected_ButCanBeOnceTheHeroStepsIntoTheSameFog()
        {
            yield return Setup();
            _dummy.transform.position = new Vector3(0f, 1f, 6f);

            PlaceHero(Vector3.zero, 0f);
            CastSteamOverTheDummy();
            yield return null;

            AssertClearOfZoneBoundaries(_dummy.transform.position, "木樁");
            AssertClearOfZoneBoundaries(_hero.transform.position, "英雄");
            Assert.IsFalse(_hero.CanEngage(_dummy), "霧外的英雄不該鎖得到霧內的木樁");

            _input.TapTarget(_dummy);   // 與真實點擊送出的是同一個 OnCombatTargetSelected 事件
            yield return null;
            Assert.IsNull(_hero.CurrentTarget, "點擊當下沒有被擋下來");

            // 反例：英雄走進同一團霧
            PlaceHero(new Vector3(0f, 0f, 8f), 0f);
            yield return null;
            AssertClearOfZoneBoundaries(_hero.transform.position, "英雄");
            Assert.IsTrue(_hero.CanEngage(_dummy), "同一團霧裡的攻擊者照樣打得到");
            _input.TapTarget(_dummy);
            yield return null;
            Assert.AreSame(_dummy, _hero.CurrentTarget, "霧內的英雄應該鎖得到同霧的木樁");
        }

        [UnityTest]
        public IEnumerator V4f2_FogRisingAfterTheLockButBeforeTheFirstHit_StopsTheAttack()
        {
            yield return Setup();
            _dummy.transform.position = new Vector3(0f, 1f, 6f);

            PlaceHero(new Vector3(0f, 0f, -4f), 0f);   // 距木樁 10m ＞ 攻擊距離 5m
            yield return null;
            _input.TapTarget(_dummy);
            yield return null;
            Assert.AreSame(_dummy, _hero.CurrentTarget, "起霧前應該鎖得到");

            float healthBefore = _dummy.Health;
            CastSteamOverTheDummy();          // 第一刀命中之前起霧
            yield return null;                // 1 幀內生效

            Assert.IsFalse(_hero.CanEngage(_dummy), "起霧後 1 幀內 CanEngage 就該是 false");
            Assert.IsFalse(_hero.IsTargetValid(_dummy), "持續驗證沒有走同一個判準");

            yield return Seconds(1.5f);
            Assert.AreEqual(healthBefore, _dummy.Health, DamageTolerance,
                "起霧之後木樁仍然被打到了：鎖定後的持續驗證沒有把攻擊停下來");
        }

        [UnityTest]
        public IEnumerator V4f3_EveryHitRefreshesTheReveal_SoTheAttackNeverBreaks()
        {
            yield return Setup();
            _dummy.transform.position = new Vector3(0f, 1f, 6f);

            PlaceHero(new Vector3(0f, 0f, 4f), 0f);    // 距木樁 2m（攻擊距離內）、距霧心 5m（霧外）
            yield return null;

            int hits = 0;
            float lastHitTime = 0f;
            _hero.OnAttackHitResolved += target => { hits++; lastHitTime = Time.time; };

            // 先鎖定並砍中一刀（此時還沒有霧），再起霧——③ 要驗的是「打中過就看得到」。
            _input.TapTarget(_dummy);
            int guard = Mathf.RoundToInt(3f * Fps);
            while (hits < 1 && guard-- > 0) yield return null;
            Assert.GreaterOrEqual(hits, 1, "起霧前英雄根本沒打中木樁");

            CastSteamOverTheDummy();
            AssertClearOfZoneBoundaries(_hero.transform.position, "英雄");
            AssertClearOfZoneBoundaries(_dummy.transform.position, "木樁");

            int hitsAtFog = hits;
            int refreshAtFog = _dummy.RevealRefreshCount;
            guard = Mathf.RoundToInt(2.5f * Fps);
            while (hits - hitsAtFog < 2 && guard-- > 0)
            {
                Assert.IsTrue(_hero.CanEngage(_dummy), "打中過之後不得再被霧遮住");
                yield return null;
            }
            Assert.GreaterOrEqual(hits - hitsAtFog, 2, "起霧之後攻擊中斷了（受擊顯影沒接上？）");
            Assert.Greater(_dummy.RevealRefreshCount - refreshAtFog, 1, "每一刀都該刷新顯影");

            // 停手：顯影過期之後就再也鎖不到。重新起霧只是讓白霧活得比 1.5s 久（蒸氣本身只有 3s）。
            _input.TapGround(_hero.transform.position);
            CastSteamOverTheDummy();
            yield return null;

            guard = Mathf.RoundToInt(4f * Fps);
            while (Time.time - lastHitTime < _tuning.RevealDurationSeconds + 0.02f && guard-- > 0) yield return null;
            Assert.Greater(guard, 0, "停手指令沒有讓英雄停止攻擊（顯影一直被刷新）");
            Assert.IsFalse(_hero.CanEngage(_dummy), "距最後一刀超過 1.5s 之後仍然鎖得到：顯影沒有過期");
        }

        // ───────────────────── V4-g：受擊顯影（1.49／1.51 兩側）─────────────────────

        [UnityTest]
        public IEnumerator V4g_TheRevealExpiresBetweenOnePointFourNine_AndOnePointFiveOneSeconds()
        {
            yield return Setup();
            _dummy.transform.position = new Vector3(0f, 1f, 6f);

            PlaceHero(Vector3.zero, 0f);
            CastSteamOverTheDummy();
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_dummy), "前提：起霧後本來就鎖不到");

            // 走真實的 CombatTargetBehaviour.ReceiveDamage → NotifyDamaged（不用砲台：0.25s 一發會一直刷新）
            _dummy.ReceiveDamage(1f, DamageType.Elemental, null);
            Assert.IsTrue(_hero.CanEngage(_dummy), "受擊當下就該顯影");

            // 1/60 固定步長：第 89 幀 ＝ 1.4833s（落在 1.49 ± 0.034 內）；第 91 幀 ＝ 1.5167s（1.51 ± 0.034 內）。
            // 第 90 幀正好是 1.5s 的浮點邊界，刻意跳過——兩側各留一幀，不靠捨入決定紅綠。
            yield return Frames(89);
            Assert.IsTrue(_hero.CanEngage(_dummy), "1.4833s 時顯影就已經過期了（時長被改？）");
            Assert.IsTrue(_dummy.IsRevealed);

            yield return Frames(2);
            Assert.IsFalse(_hero.CanEngage(_dummy), "1.5167s 之後顯影仍未過期（顯影永不過期？）");
            Assert.IsFalse(_dummy.IsRevealed);
        }

        // ───────────────────── V4-h／V4-i：擴散火浪 ─────────────────────

        [UnityTest]
        public IEnumerator V4h_WindSweepingABurningZone_DealsSixtyAndConsumesTheZone()
        {
            yield return Setup();
            _dummy.transform.position = new Vector3(0f, 1f, 6f);

            PlaceHero(new Vector3(0f, 0f, 3f), 0f);   // 前方 4m ＝ (0,0,7)，木樁落在 3m 燃燒圈內
            yield return null;
            PressFire();
            yield return null;

            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Burning), "空地火沒有留下燃燒區");
            int burningId = _field.FindZoneIdContaining(new Vector3(0f, 0f, 7f), ElementZoneKind.Burning);
            Assert.GreaterOrEqual(burningId, 0);
            AssertClearOfZoneBoundaries(_dummy.transform.position, "木樁");

            int hitstopBefore = _feedback.HitstopCount;
            float healthBefore = _dummy.Health;
            PressWind();   // 同一幀內結算，記錄與結算之間 0 個 DoT tick

            float dealt = healthBefore - _dummy.Health;
            Assert.GreaterOrEqual(dealt, 59.99f, "火浪傷害低於 60（倍率沒套到最終傷害？實測 " + dealt + "）");
            Assert.LessOrEqual(dealt, 60.68f, "火浪傷害高於上界（實測 " + dealt + "）");
            Assert.IsFalse(_field.TryGetZoneById(burningId, out ElementZone _), "風沒有消耗燃燒區");
            Assert.GreaterOrEqual(_feedback.HitstopCount - hitstopBefore, 1, "火浪沒有觸發 Combo 頓挫");
            Assert.IsTrue(_bootstrap.SectorTelegraph.ShowCount > 0, "扇形預警沒有顯示過");
            yield return null;
        }

        [UnityTest]
        public IEnumerator V4i_ABurningZoneOneDegreeOutsideTheSector_TakesNoFirestorm()
        {
            yield return Setup();

            // 取向 37°，避開軸對齊與 45°（批 2 R9 教訓）
            const float heading = 37f;
            PlaceHero(Vector3.zero, heading);
            yield return null;

            Vector3 forward = _hero.transform.forward;
            Vector3 burnPoint = _hero.transform.position + Flat(forward).normalized * _tuning.CastDistanceMeters;
            _dummy.transform.position = new Vector3(burnPoint.x, 1f, burnPoint.z);
            yield return null;

            // ① 活性：同一個扇形真的量得到 60
            _field.CastFire(burnPoint, (int)Faction.BlueTeam);
            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Burning));
            int hitstopBefore = _feedback.HitstopCount;
            float healthBefore = _dummy.Health;
            _field.CastWind(_hero.transform.position, _hero.transform.forward, (int)Faction.BlueTeam);
            float dealt = healthBefore - _dummy.Health;
            Assert.GreaterOrEqual(dealt, 59.99f, "活性段沒有量到火浪（實測 " + dealt + "）");
            Assert.LessOrEqual(dealt, 60.68f);
            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Burning), "活性段的燃燒區沒有被消耗");
            Assert.GreaterOrEqual(_feedback.HitstopCount - hitstopBefore, 1);

            // ② 反例：同一個燃燒區、把英雄轉到扇形角外 1°（半角 30° → 31°）
            _field.CastFire(burnPoint, (int)Faction.BlueTeam);
            int burningId = _field.FindZoneIdContaining(burnPoint, ElementZoneKind.Burning);
            Assert.GreaterOrEqual(burningId, 0);
            _hero.transform.rotation = Quaternion.Euler(0f, heading + _tuning.FirestormAngleDegrees * 0.5f + 1f, 0f);
            yield return null;

            int firestormsBefore = _field.FirestormCount;
            hitstopBefore = _feedback.HitstopCount;
            healthBefore = _dummy.Health;
            _field.CastWind(_hero.transform.position, _hero.transform.forward, (int)Faction.BlueTeam);

            Assert.AreEqual(firestormsBefore, _field.FirestormCount, "角外 1° 仍然觸發了火浪");
            Assert.AreEqual(healthBefore, _dummy.Health, DamageTolerance, "角外 1° 仍然造成了傷害");
            Assert.IsTrue(_field.TryGetZoneById(burningId, out ElementZone _), "沒掃到卻消耗了燃燒區");
            Assert.AreEqual(hitstopBefore, _feedback.HitstopCount, "沒掃到卻觸發了 Combo 頓挫");

            // ③ 陣營方向 B：木樁改藍隊 → 同一發扇形不扣血
            _hero.transform.rotation = Quaternion.Euler(0f, heading, 0f);
            _dummy.Configure(_dummy.MaxHealth, Faction.BlueTeam);
            yield return null;
            healthBefore = _dummy.Health;
            _field.CastWind(_hero.transform.position, _hero.transform.forward, (int)Faction.BlueTeam);
            Assert.AreEqual(healthBefore, _dummy.Health, DamageTolerance, "藍火浪打到了藍隊木樁");
            Assert.AreEqual(firestormsBefore + 1, _field.FirestormCount, "這一發應該真的掃到了燃燒區");
        }

        // ───────────────────── V4-j：空地火直傷與燃燒區 DoT ─────────────────────

        [UnityTest]
        public IEnumerator V4j_OpenGroundFire_DealsFortyOnTheSpot_AndEightyMoreOverFourSeconds()
        {
            yield return Setup();
            _dummy.transform.position = new Vector3(0f, 1f, 6f);

            PlaceHero(new Vector3(0f, 0f, 2f), 0f);   // 前方 4m ＝ 木樁腳下
            yield return null;
            AssertClearOfZoneBoundaries(_dummy.transform.position, "木樁");

            float healthBefore = _dummy.Health;
            PressFire();
            Assert.AreEqual(_tuning.FireDirectDamage, healthBefore - _dummy.Health, DamageTolerance,
                "空地火的直傷不是 40");
            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Burning));

            float expectedTotal = _tuning.FireDirectDamage
                                  + _tuning.BurnDamagePerSecond * _tuning.BurnDurationSeconds;
            yield return Seconds(_tuning.BurnDurationSeconds + 0.1f);
            Assert.AreEqual(expectedTotal, healthBefore - _dummy.Health, DotTolerance,
                "4.1s 之後的總傷害不是 120");
            Assert.AreEqual(0, _field.CountZonesOfKind(ElementZoneKind.Burning), "燃燒區沒有到期");

            float afterBurn = _dummy.Health;
            yield return Seconds(0.5f);
            Assert.AreEqual(afterBurn, _dummy.Health, DamageTolerance, "燃燒區到期之後還在燒");
            Assert.AreEqual(expectedTotal, healthBefore - _dummy.Health, DotTolerance,
                "4.6s 時的總減少量不是 120");

            // ② 陣營方向 B：木樁改藍隊 → 同一發空地火不扣血、DoT 也不扣
            _dummy.Configure(_dummy.MaxHealth, Faction.BlueTeam);
            yield return null;
            float blueBefore = _dummy.Health;
            PlaceHero(new Vector3(0f, 0f, 2f), 0f);
            yield return Seconds(_tuning.SkillCooldownSeconds + 0.1f);   // FIRE 冷卻
            PressFire();
            Assert.AreEqual(blueBefore, _dummy.Health, DamageTolerance, "藍火打到了藍隊木樁");
            yield return Seconds(1f);
            Assert.AreEqual(blueBefore, _dummy.Health, DamageTolerance, "藍燃燒區的 DoT 燒到了藍隊木樁");
        }

        // ───────────────────── V4-k：Combo 反饋只掛三個反應 ─────────────────────

        [UnityTest]
        public IEnumerator V4k_ComboFeedbackFiresOnceForQuicksandSteamAndFirestorm_ButNotForRescueOrBoil()
        {
            yield return Setup();
            _dummy.transform.position = new Vector3(20f, 1f, -20f);   // 搬遠，免得元素傷害干擾計數

            // ① 流沙成形
            int before = _feedback.HitstopCount;
            _field.CastWater(new Vector3(0f, 0f, 10f), (int)Faction.RedTeam);
            _field.NotifyWallActivated(new Vector3(0f, 0f, 11f), Faction.RedTeam);
            Assert.AreEqual(before + 1, _feedback.HitstopCount, "流沙成形的 Combo 頓挫不是恰好一次");
            Assert.IsTrue(_feedback.IsHitstopActive);

            // ② 救援／爆沸不得另外觸發
            before = _feedback.HitstopCount;
            _field.CastFire(new Vector3(0f, 0f, 10f), (int)Faction.BlueTeam);   // 藍火打紅流沙 ＝ 救援
            Assert.AreEqual(before, _feedback.HitstopCount, "救援不該觸發 Combo");

            _field.CastWater(new Vector3(0f, 0f, 10f), (int)Faction.RedTeam);
            _field.NotifyWallActivated(new Vector3(0f, 0f, 11f), Faction.RedTeam);
            before = _feedback.HitstopCount;
            _field.CastFire(new Vector3(0f, 0f, 10f), (int)Faction.RedTeam);    // 紅火打紅流沙 ＝ 爆沸
            Assert.AreEqual(before, _feedback.HitstopCount, "爆沸不該觸發 Combo");

            // ③ 蒸氣成形
            before = _feedback.HitstopCount;
            _field.CastWater(new Vector3(-10f, 0f, 0f), (int)Faction.BlueTeam);
            _field.CastFire(new Vector3(-10f, 0f, 0f), (int)Faction.BlueTeam);
            Assert.AreEqual(before + 1, _feedback.HitstopCount, "蒸氣成形的 Combo 頓挫不是恰好一次");

            // ④ 火浪
            PlaceHero(new Vector3(0f, 0f, -14f), 0f);
            yield return null;
            _field.CastFire(new Vector3(0f, 0f, -11f), (int)Faction.BlueTeam);
            before = _feedback.HitstopCount;
            _field.CastWind(_hero.transform.position, Vector3.forward, (int)Faction.BlueTeam);
            Assert.AreEqual(before + 1, _feedback.HitstopCount, "火浪的 Combo 頓挫不是恰好一次");
            Assert.AreEqual(1, _field.FirestormCount);
            yield return null;
        }

        // ───────────────────── V4-l：沒有元素區域時逐值不變 ─────────────────────

        [UnityTest]
        public IEnumerator V4l_WithNoElementZones_TheHeroBehavesExactlyAsBefore()
        {
            yield return Setup();

            Assert.AreEqual(0, _field.ActiveZoneCount, "前提：場上沒有任何元素區域");
            Assert.AreEqual(1f, _locomotion.SpeedMultiplier, 1e-6f);
            Assert.IsFalse(_locomotion.IsMovementLocked);

            // ② 到達時間：接著元素場 vs 把元素場拔掉，逐值相同
            float withField = 0f;
            yield return MeasureArrivalTime(value => withField = value);

            _hero.SetElementField(null, null);
            float withoutField = 0f;
            yield return MeasureArrivalTime(value => withoutField = value);
            Assert.AreEqual(withoutField, withField, TimeTolerance,
                "接上元素場之後到達時間變了（有場 " + withField + "s、無場 " + withoutField + "s）");

            // ③ CanEngage 與 CanBeTargetedBy 對三個目標逐一相同（無霧時恆真；鑑別力在 V4-f 的反例）
            _hero.SetElementField(_field, _tuning);
            RuneWall enemyWall = _enemyWalls.Spawn();
            Assert.IsNotNull(enemyWall, "敵方牆沒有生出來");
            GameObject testWallA = GameObject.Find("TestWall_A");
            Assert.IsNotNull(testWallA, "場景缺少 TestWall_A");
            TestWallTarget testWall = testWallA.GetComponent<TestWallTarget>();
            yield return null;

            Assert.AreEqual(0, _field.ActiveZoneCount, "③ 的前提：仍然沒有任何元素區域");
            AssertEngageMatchesFactionRule(_dummy, "木樁");
            AssertEngageMatchesFactionRule(testWall, "TestWall_A");
            AssertEngageMatchesFactionRule(enemyWall, "敵方牆");
        }

        private void AssertEngageMatchesFactionRule(ICombatTarget target, string who)
        {
            Assert.AreEqual(target.CanBeTargetedBy(_hero.HeroFaction), _hero.CanEngage(target),
                "沒有元素區域時，" + who + " 的 CanEngage 與 CanBeTargetedBy 不一致（fail-closed？）");
        }

        private IEnumerator MeasureArrivalTime(System.Action<float> report)
        {
            PlaceHero(Vector3.zero, 0f);
            yield return null;
            float start = Time.time;
            _input.TapGround(new Vector3(0f, 0f, -6f));   // 往 −Z 走：+Z 那一側站著木樁，會擋住到達判定
            yield return null;

            int guard = Mathf.RoundToInt(6f * Fps);
            while (!_hero.HasArrivedAtDestination && guard-- > 0) yield return null;
            Assert.Greater(guard, 0, "英雄沒有在 6 秒內走完 6m");
            report(Time.time - start);
        }

        // ───────────────────── V4-m：池滿（實機）─────────────────────

        [UnityTest]
        public IEnumerator V4m_TheSeventhZone_EvictsTheOneWithTheLeastTimeLeft_AndStillShowsUp()
        {
            yield return Setup();

            Assert.Greater(_bootstrap.ElementZoneViewPoolSize, _tuning.MaxLiveZones,
                "區域視覺池必須比同時存活上限多（池 " + _bootstrap.ElementZoneViewPoolSize
                + "、上限 " + _tuning.MaxLiveZones + "）");
            int blockedBefore = _bootstrap.NavGrid.BlockedCount;

            // 每次施放隔一幀 ⇒ 剩餘時間依序遞減，「最短」那一格是第一個生的
            int[] ids = new int[_tuning.MaxLiveZones];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = _field.CastWater(new Vector3(-18f + i * 6f, 0f, -18f), (int)Faction.BlueTeam);
                yield return null;
            }
            Assert.AreEqual(_tuning.MaxLiveZones, _field.ActiveZoneCount, "沒有湊滿 6 個同時存活的區域");
            yield return null;
            Assert.AreEqual(_tuning.MaxLiveZones, _field.VisibleViewCount, "6 個區域沒有各借到一個視覺");

            int seventh = _field.CastWater(new Vector3(18f, 0f, 18f), (int)Faction.BlueTeam);
            yield return null;

            Assert.GreaterOrEqual(seventh, 0, "第 7 次施放回傳了無效 id（池滿＝按鈕沒反應）");
            Assert.AreEqual(_tuning.MaxLiveZones, _field.ActiveZoneCount);
            Assert.IsFalse(_field.TryGetZoneById(ids[0], out ElementZone _), "被擠掉的不是剩餘時間最短那一個");
            Assert.IsTrue(_field.TryGetZoneById(seventh, out ElementZone _), "第 7 個區域沒有進場");

            Assert.IsTrue(_field.IsZoneVisible(seventh), "第 7 個區域在場上看不見");
            Assert.IsFalse(_field.IsZoneVisible(ids[0]), "被擠掉的區域仍然看得見");
            Assert.AreEqual(_tuning.MaxLiveZones, _field.VisibleViewCount, "場上可見的區域視覺不是 6 個");
            Assert.AreEqual(blockedBefore, _bootstrap.NavGrid.BlockedCount,
                "元素區域動到了阻擋格點（區域視覺不得有 Collider、不得進 NavGrid）");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ElementZoneViewPool_WhenNotLargerThanTheLiveCap_LogsAnError()
        {
            yield return Setup();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("區域視覺池只有"));

            GameObject host = new GameObject("DegenerateElementField");
            ElementField degenerate = host.AddComponent<ElementField>();
            ElementTuning tuning = new ElementTuning();
            degenerate.Initialize(tuning, new CombatTargetRoster(tuning.TargetRosterCapacity), null, null,
                                  new ElementZoneView[tuning.MaxLiveZones]);   // 池 ＝ 上限，缺緩衝格
            yield return null;
            Object.Destroy(host);
        }

        // ───────────────────── V4-n：冷卻（實機）─────────────────────

        [UnityTest]
        public IEnumerator V4n_EachSkillHasItsOwnFiveSecondCooldown_AndTheLabelCountsDown()
        {
            yield return Setup();
            PlaceHero(new Vector3(0f, 0f, -10f), 0f);
            yield return null;

            int recomputesBefore = _bootstrap.ElementLabelRecomputeCount;
            float t0 = Time.time;
            PressWater();
            PressWater();   // 冷卻中再按一次：不得有任何事發生
            yield return null;
            Assert.AreEqual(1, _field.CountZonesOfKind(ElementZoneKind.Water), "冷卻沒有擋下第二次施放");
            Assert.AreEqual("WATER 5", _bootstrap.ElementWaterButtonLabel);

            // 三顆鈕各自獨立：WATER 冷卻中，FIRE 仍可用。
            // 先轉身再放火：原地放的話落點與剛剛那個水域重疊，會變成蒸氣反應而不是空地火。
            _hero.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            int plainFiresBefore = _field.PlainFireCount;
            PressFire();
            Assert.AreEqual(plainFiresBefore + 1, _field.PlainFireCount, "FIRE 被 WATER 的冷卻擋住了");

            while (Time.time - t0 < 4.02f) yield return null;
            yield return null;   // 標籤在 DebugHud.Update 裡重算
            Assert.AreEqual("WATER 1", _bootstrap.ElementWaterButtonLabel);

            while (Time.time - t0 < _tuning.SkillCooldownSeconds + 0.02f) yield return null;
            yield return null;
            Assert.AreEqual("WATER", _bootstrap.ElementWaterButtonLabel);

            int watersBefore = _field.CountZonesOfKind(ElementZoneKind.Water);
            PressWater();
            Assert.Greater(_field.CountZonesOfKind(ElementZoneKind.Water), watersBefore,
                "冷卻結束後仍然放不出來");
            Assert.GreaterOrEqual(_bootstrap.ElementLabelRecomputeCount - recomputesBefore, 5,
                "冷卻期間標籤沒有被重算 5 次以上（零配置字串表沒有走到？實測 "
                + (_bootstrap.ElementLabelRecomputeCount - recomputesBefore) + "）");
        }

        // ───────────────────── V4-q：不污染既有物理與導航 ─────────────────────

        [UnityTest]
        public IEnumerator V4q_ElementZonesHaveNoColliders_AndDoNotTouchTheNavGrid()
        {
            yield return Setup();

            GameObject pool = GameObject.Find("ElementZonePool");
            Assert.IsNotNull(pool, "場景缺少 ElementZonePool");
            ElementZoneView[] views = pool.GetComponentsInChildren<ElementZoneView>(true);
            Assert.AreEqual(_bootstrap.ElementZoneViewPoolSize, views.Length);
            for (int i = 0; i < views.Length; i++)
                Assert.IsNull(views[i].GetComponent<Collider>(),
                    views[i].name + " 帶著 Collider：英雄會繞路、點擊會被擋");

            int blockedBefore = _bootstrap.NavGrid.BlockedCount;
            PlaceHero(new Vector3(0f, 0f, -6f), 0f);
            yield return null;

            PressWater();
            PressFire();                                  // 火落在水域上 → 蒸氣，圓心 (0,0,-2)、半徑 4
            yield return Seconds(0.5f);
            Assert.Greater(_field.ActiveZoneCount, 0, "前提：場上要有區域");
            Assert.AreEqual(blockedBefore, _bootstrap.NavGrid.BlockedCount,
                "元素區域改動了阻擋格點");

            // 英雄站在區域上不會被推開（站進圈內 1m 處，不站邊界）
            PlaceHero(new Vector3(0f, 0f, -3f), 0f);
            yield return null;
            AssertClearOfZoneBoundaries(_hero.transform.position, "英雄");
            Vector3 standing = _hero.transform.position;
            yield return Seconds(0.5f);
            Assert.AreEqual(0f, PlanarDistance(standing, _hero.transform.position), 1e-3f,
                "英雄站在元素區域上被推開了");
        }

        // ───────────────────── V4-r：縛足不擋推出 ─────────────────────

        [UnityTest]
        public IEnumerator V4r_ARootedHero_IsStillEjectedFromAWallThatLandsOnHim()
        {
            yield return Setup();
            yield return BuildHostileQuicksand();
            Assert.IsTrue(_locomotion.IsMovementLocked, "前提：英雄正在被縛足");

            Vector3 before = _hero.transform.position;
            // EjectFromBox 在 Activate → Stamp(+1) → HandleNavBlockerStamped 裡**同步**發生，
            // 所以這裡不 yield：量的就是那一次推出，不摻任何一幀的走路。
            ActivateWall(_enemyWalls.Pool[1], new Vector3(before.x, 0f, before.z), Vector3.forward, Faction.RedTeam);

            Assert.Greater(PlanarDistance(before, _hero.transform.position), PositionTolerance,
                "被牆壓住的縛足英雄沒有被推出去：防線寫得太上游，他會永久卡在牆體內");
            yield return null;
        }

        // ═════════════════ r1 對抗審查後的修訂（計畫 §9 本輪修）═════════════════

        // R1（HIGH-2）：四顆新鈕必須走**真實**的觸控分流（InputRoutingManager 的區域判定），
        // 不是直接呼叫 Phase1Bootstrap.PressElementXxxButton()。比照批 3 的
        // ShieldAndProjectilePlayTests.R5m2_TheHudButtons_AreReachedThroughTheRealTouchRouting。
        //
        // 這一條會紅的四種壞實作：區域沒登記（RegisterUiRegion 漏掉）、region id 對錯
        //（HandleRegionTapped 分派到別顆鈕）、ToScreenRegion 換算錯、矩形跑到畫面外
        //——四種都會讓玩家按了沒反應，而 V4-o 的幾何不相交測試照樣全綠。
        [UnityTest]
        public IEnumerator R1_TheFourElementButtons_AreReachedThroughTheRealTouchRouting()
        {
            yield return Setup();

            bool moved = false;
            ICombatTarget picked = null;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moved = true;
            _bootstrap.InputService.OnCombatTargetSelected += target => picked = target;

            // 版面用的是 DebugHud 在同一組 Screen.* 下算出來的那一份（DebugHudLayout 是兩邊唯一的來源）。
            // 三個 bool ＝ 灰盒場景實際的組態（延遲模擬、GRID 疊圖、敵方牆／砲台那一列都在）。
            DebugHudLayout layout = DebugHudLayout.Compute(Screen.width, Screen.height, Screen.dpi, true, true, true);

            // ① ELEM：陣營標籤翻面
            string factionLabelBefore = _bootstrap.ElementFactionButtonLabel;
            Faction factionBefore = _bootstrap.ElementCastFaction;
            yield return TapHudRect(layout, layout.Elem, "ELEM");
            Assert.AreNotEqual(factionBefore, _bootstrap.ElementCastFaction,
                "對 ELEM 鈕的矩形送真實觸控沒有切換陣營");
            Assert.AreNotEqual(factionLabelBefore, _bootstrap.ElementFactionButtonLabel,
                "ELEM 的標籤沒有跟著翻面");

            // ② WATER：場上多一個區域
            int zonesBefore = _field.ActiveZoneCount;
            yield return TapHudRect(layout, layout.Water, "WATER");
            Assert.AreEqual(zonesBefore + 1, _field.ActiveZoneCount,
                "對 WATER 鈕的矩形送真實觸控沒有生出水域");

            // ③ FIRE：先轉身，讓落點是空地而不是剛剛那個水域（落進水域會變成蒸氣＝區域數不變，量不到）
            _hero.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return null;
            zonesBefore = _field.ActiveZoneCount;
            int plainFiresBefore = _field.PlainFireCount;
            yield return TapHudRect(layout, layout.Fire, "FIRE");
            Assert.AreEqual(plainFiresBefore + 1, _field.PlainFireCount,
                "對 FIRE 鈕的矩形送真實觸控沒有施放");
            Assert.AreEqual(zonesBefore + 1, _field.ActiveZoneCount, "空地火沒有留下燃燒區");

            // ④ WIND：扇形預警顯示過一次
            int telegraphsBefore = _bootstrap.SectorTelegraph.ShowCount;
            yield return TapHudRect(layout, layout.Wind, "WIND");
            Assert.AreEqual(telegraphsBefore + 1, _bootstrap.SectorTelegraph.ShowCount,
                "對 WIND 鈕的矩形送真實觸控沒有放出扇形預警");

            // 四次都不得滲透成世界輸入
            Assert.IsFalse(moved, "點 HUD 元素鈕不得滲透成移動指令");
            Assert.IsNull(picked, "點 HUD 元素鈕不得滲透成鎖定目標");
        }

        // GUI 座標（原點左上、未乘 scale）→ 螢幕座標（原點左下、像素），用的是 DebugHud.ToScreenRegion
        // 的同一個換算；再對矩形中心送一次真實觸控。
        private IEnumerator TapHudRect(DebugHudLayout layout, HudRect guiRect, string who)
        {
            float scale = layout.Scale;
            float screenXMin = guiRect.XMin * scale;
            float screenXMax = guiRect.XMax * scale;
            float screenYMin = Screen.height - guiRect.YMax * scale;
            float screenYMax = Screen.height - guiRect.YMin * scale;

            float centreX = (screenXMin + screenXMax) * 0.5f;
            float centreY = (screenYMin + screenYMax) * 0.5f;
            Assert.IsTrue(centreX >= 0f && centreX <= Screen.width && centreY >= 0f && centreY <= Screen.height,
                who + " 鈕的中心點落在畫面外（" + centreX + ", " + centreY + "），實機上按不到");

            _bootstrap.WorldTapInput.SendScreenTap(centreX, centreY);
            yield return null;
        }

        // R2（M5）：多團霧重疊時，攻擊者只要與目標共享**任何一團**霧就不遮蔽。
        // 舊實作只取「離目標最近的那一團」再問攻擊者在不在那一團裡 → 目標同時在 A（較近）與 B 內、
        // 攻擊者只在 B 內時會被誤判成遮蔽，違反 GDD 規則②「同一團霧裡的攻擊者打得到」。
        //
        // 取點刻意避開軸對齊，受試三點離**每一團**霧的邊界都 ≥0.5m（AssertClearOfZoneBoundaries 逐圈驗）。
        [UnityTest]
        public IEnumerator R2_AttackerSharingAnySteamWithTheTarget_IsNotConcealed()
        {
            yield return Setup();

            Vector3 steamA = new Vector3(-1.0f, 0f, 2.0f);
            Vector3 steamB = new Vector3(4.0f, 0f, 5.0f);   // 圓心距 5.831m < 2×4 ⇒ 兩圈重疊

            _field.CastWater(steamA, (int)Faction.BlueTeam);
            _field.CastFire(steamA, (int)Faction.BlueTeam);
            _field.CastWater(steamB, (int)Faction.BlueTeam);
            _field.CastFire(steamB, (int)Faction.BlueTeam);
            Assert.AreEqual(2, _field.CountZonesOfKind(ElementZoneKind.Steam), "兩團霧沒有同時存在");

            // 目標在 A∩B，且離 A 圓心較近（2.594m vs 3.245m）——舊實作會挑中 A
            Vector3 target = new Vector3(1.3f, 0f, 3.2f);
            // 攻擊者只在 B 內（離 B 2.5m、離 A 8.32m）
            Vector3 attackerInB = new Vector3(6.0f, 0f, 6.5f);
            // 兩團都不在（活性對照）
            Vector3 attackerOutside = new Vector3(-8.0f, 0f, -6.0f);

            AssertClearOfZoneBoundaries(target, "目標");
            AssertClearOfZoneBoundaries(attackerInB, "只在 B 內的攻擊者");
            AssertClearOfZoneBoundaries(attackerOutside, "兩團都不在的攻擊者");

            float distanceToA = PlanarDistance(target, steamA);
            float distanceToB = PlanarDistance(target, steamB);
            Assert.Less(distanceToA, distanceToB,
                "前提：目標必須離 A 圓心較近，否則舊實作剛好挑中 B、這條測試就沒有鑑別力了");

            Assert.IsFalse(_field.IsConcealedFrom(target, false, attackerInB),
                "攻擊者與目標共享霧 B，卻被判成遮蔽（只看了離目標最近的那一團霧 A）");

            // 活性：同一組盤面下，真的不共享任何一團霧時必須是遮蔽的
            Assert.IsTrue(_field.IsConcealedFrom(target, false, attackerOutside),
                "兩團霧都不在的攻擊者竟然打得到：遮蔽整個失效了");

            // 顯影旗標仍然優先（三個 bool 的真值表不變）
            Assert.IsFalse(_field.IsConcealedFrom(target, true, attackerOutside),
                "目標已顯影時不得再遮蔽");
        }

        // R3（M4）：名冊塞爆時不得靜默丟掉目標——被丟掉的那個再也吃不到任何元素 AOE／DoT，
        // 而全套測試照樣綠。同一個檔案對「池 ≤ 上限」「場景引用掉了」都有 LogError，這裡也要有。
        [UnityTest]
        public IEnumerator R3_RosterOverflow_LogsAnError()
        {
            yield return Setup();

            CombatTargetRoster roster = _bootstrap.ElementRoster;
            Assert.IsNotNull(roster, "Phase1Bootstrap 沒有建立元素名冊");
            Assert.Less(roster.Count, roster.Capacity, "前提：開場時名冊還有空位");

            // 去重那一條不得誤報：已經在名冊裡的目標再登記一次，Add 同樣回 false，但不是「丟掉」。
            _bootstrap.RegisterElementTarget(_dummy);

            // 填到容量上限（停用的空殼：Awake 不跑、IsAlive 為 false，不會干擾任何結算）
            int fillerCount = roster.Capacity - roster.Count;
            GameObject[] fillers = new GameObject[fillerCount + 1];
            for (int i = 0; i < fillers.Length; i++)
            {
                GameObject filler = new GameObject("RosterFiller_" + i);
                filler.SetActive(false);
                filler.transform.position = new Vector3(100f, 0f, 100f);
                fillers[i] = filler;
                TestWallTarget target = filler.AddComponent<TestWallTarget>();
                if (i < fillerCount) _bootstrap.RegisterElementTarget(target);
            }
            Assert.AreEqual(roster.Capacity, roster.Count, "名冊沒有被填滿，後面那一次 Add 不會溢位");

            // 第 33 個：名冊已滿 → 必須留下 LogError
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("元素目標名冊已滿"));
            _bootstrap.RegisterElementTarget(fillers[fillerCount].GetComponent<TestWallTarget>());
            Assert.AreEqual(roster.Capacity, roster.Count, "溢位之後名冊長度不得改變");

            for (int i = 0; i < fillers.Length; i++) Object.Destroy(fillers[i]);
            yield return null;
        }
    }
}
