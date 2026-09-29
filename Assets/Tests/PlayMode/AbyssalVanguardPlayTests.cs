using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    public sealed class AbyssalVanguardPlayTests
    {
        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; }

        private IEnumerator StartCapture()
        {
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _hero = Object.FindObjectOfType<HeroController>();
            _opponent = Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(_bootstrap);
            Assert.IsNotNull(_bootstrap.VanguardTarget, "場景必須預建並接上先鋒實體");
            Assert.IsNotNull(_bootstrap.VanguardLogic);
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float x, out float y));
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Vector3 screen = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            yield return null;
        }

        private IEnumerator SpawnVanguard()
        {
            _bootstrap.SeedCaptureMatchElapsedForTest(599.9f);
            int frames = 0;
            while (_bootstrap.VanguardLogic.Phase != AbyssalVanguardPhase.Vanguard && frames++ < 20)
                yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Vanguard, _bootstrap.VanguardLogic.Phase);
        }

        private Faction[] SnapshotOwners()
        {
            var owners = new Faction[_bootstrap.CaptureSpec.TileCount];
            for (int i = 0; i < owners.Length; i++) owners[i] = _bootstrap.CaptureView.OwnerOf(i);
            return owners;
        }

        private void AssertOwnersUnchanged(Faction[] before, string moment)
        {
            for (int i = 0; i < before.Length; i++)
                Assert.AreEqual(before[i], _bootstrap.CaptureView.OwnerOf(i), moment + "：第 " + i + " 塊歸屬不得改變");
        }

        [UnityTest]
        public IEnumerator Vanguard_IsGloballyVisible_AndRedActuallyDamagesIt()
        {
            yield return StartCapture();
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(0f, 0f, -15.15625f));
            yield return SpawnVanguard();
            AbyssalVanguardTarget boss = _bootstrap.VanguardTarget;
            Assert.AreEqual(900f, boss.Health);
            Assert.IsTrue(_hero.CanEngage(boss), "遠處的中立先鋒必須越過迷霧可鎖定");
            _opponent.GetComponent<HeroLocomotion>().WarpTo(new Vector3(3f, 0f, 1.5f));
            int frames = 0;
            while (boss.Health >= 900f && frames++ < 180) yield return null;
            Assert.Less(boss.Health, 900f, "紅方 AI 須透過正式受擊入口實際打掉先鋒生命");

            // 先鋒反擊也打紅方 AI（英雄遠在 15m 外，紅方扣血只可能來自反擊）。
            float redHealth = _opponent.Health;
            frames = 0;
            while (_opponent.Health >= redHealth && frames++ < 240) yield return null;
            Assert.Less(_opponent.Health, redHealth, "5m 內的紅方 AI 須吃到先鋒反擊");
            Assert.AreEqual(redHealth - 8f * _opponent.DamageTakenPercent / 100f, _opponent.Health, 0.001f,
                            "反擊打紅方剛好 8 傷（再乘聖所百分比）");
        }

        [UnityTest]
        public IEnumerator VanguardCounterattack_WarnsThreeSeconds_Then8DamageWithinFiveMeters()
        {
            yield return StartCapture();
            _opponent.StopRound();
            yield return SpawnVanguard();
            AbyssalVanguardTarget boss = _bootstrap.VanguardTarget;
            Vector3 core = boss.transform.position;
            _hero.GetComponent<HeroLocomotion>().WarpTo(core + Vector3.back * 4.9f);
            float health = _hero.Health;

            int frame = 0, warnFrame = -1, hitFrame = -1;
            while (hitFrame < 0 && frame++ < 300)
            {
                yield return null;
                if (warnFrame < 0 && boss.IsWarning) warnFrame = frame;
                if (_hero.Health < health) hitFrame = frame;
            }
            Assert.That(warnFrame, Is.InRange(178, 182), "第一次預警應在先鋒出現 3 秒後");
            Assert.That(hitFrame - warnFrame, Is.InRange(41, 43), "預警 0.7 秒後才出手");
            Assert.AreEqual(health - 8f, _hero.Health, 0.001f, "4.9m（半徑 5m 內側）反擊剛好 8 傷");

            // 5.1m（半徑 5m 外側）：下一輪仍有預警，但圈外不受傷。
            _hero.GetComponent<HeroLocomotion>().WarpTo(core + Vector3.back * 5.1f);
            health = _hero.Health;
            bool warnedAgain = false;
            for (int i = 0; i < 200; i++)
            {
                yield return null;
                warnedAgain |= boss.IsWarning;
            }
            Assert.IsTrue(warnedAgain, "活性：第二輪反擊須發生");
            Assert.IsFalse(boss.IsWarning, "200 幀後第二輪已出手完畢");
            Assert.AreEqual(health, _hero.Health, "5m 外不得受反擊傷害");
        }

        [UnityTest]
        public IEnumerator CoreClaim_SpawnsMovingBehemoth_WithoutFlippingTiles()
        {
            yield return StartCapture();
            yield return SpawnVanguard();
            _opponent.StopRound();
            AbyssalVanguardTarget boss = _bootstrap.VanguardTarget;
            yield return null;
            Assert.AreEqual("VANGUARD 0: 900/900", _bootstrap.VanguardStatusLabel, "HUD 須顯示先鋒血量");
            int flips = _bootstrap.CaptureFlipCount;
            Faction[] owners = SnapshotOwners();
            int blueScore = _bootstrap.CaptureView.BlueScore;
            int redScore = _bootstrap.CaptureView.RedScore;
            boss.ReceiveDamage(900f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(AbyssalVanguardPhase.Core, _bootstrap.VanguardLogic.Phase);
            Assert.AreEqual(flips, _bootstrap.CaptureFlipCount, "擊倒先鋒不能直接翻塊");
            AssertOwnersUnchanged(owners, "擊倒瞬間");
            Assert.AreEqual(blueScore, _bootstrap.CaptureView.BlueScore, "擊倒瞬間不得加藍分");
            Assert.AreEqual(redScore, _bootstrap.CaptureView.RedScore, "擊倒瞬間不得加紅分");
            yield return null;
            AssertOwnersUnchanged(owners, "擊倒後一幀");
            Assert.AreEqual("CORE B 0.0  R 0.0 /3.5", _bootstrap.VanguardStatusLabel, "HUD 須顯示核心雙方引導進度");
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(3f, 0f, 0f));
            for (int i = 0; i < 90; i++) yield return null;
            Assert.Greater(_bootstrap.VanguardLogic.BlueCoreProgress, 1f, "站圈必須真的累積引導");
            _hero.TakeDuelDamage(1f, DamageType.Physical);
            Assert.AreEqual(0f, _bootstrap.VanguardLogic.BlueCoreProgress, "受傷當下即中斷核心引導");
            int frames = 0;
            while (_bootstrap.VanguardLogic.Phase != AbyssalVanguardPhase.Behemoth && frames++ < 300)
                yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, _bootstrap.VanguardLogic.Phase);
            Assert.AreEqual(CaptureMatchLogic.BlueFactionId, _bootstrap.VanguardLogic.BehemothOwner);
            Assert.AreEqual(1500f, boss.Health);
            Assert.AreEqual(_bootstrap.CaptureSpec.MotherTile(CaptureMatchLogic.RedFactionId, 0),
                            boss.DestinationTile, "藍方巨獸須朝紅方母板塊行進");
            Assert.AreEqual(flips, _bootstrap.CaptureFlipCount, "核心歸屬不得代替玩家翻塊");
            AssertOwnersUnchanged(owners, "取得巨獸");
            yield return null;
            StringAssert.StartsWith("BLUE BEAST 1500 / ", _bootstrap.VanguardStatusLabel, "HUD 須顯示歸屬與巨獸血量");
            Vector3 before = boss.transform.position;
            for (int i = 0; i < 60; i++) yield return null;
            Assert.Greater(Vector3.Distance(before, boss.transform.position), 0.5f, "巨獸須沿路向敵母板塊移動");
            Assert.AreEqual(flips, _bootstrap.CaptureFlipCount, "巨獸移動本身不得翻塊");
            AssertOwnersUnchanged(owners, "巨獸行進");
            // 直接跳到第 600 秒會留下待選天賦，面板蓋在畫面中央會吃掉這一下點擊；先選完，量的才是點擊穿透本身。
            for (int i = 0; i < 5 && _bootstrap.TalentPanelVisible; i++)
            {
                _bootstrap.PressTalentButton(0);
                yield return null;
            }
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "點擊前天賦面板須已關閉");
            bool moved = false;
            ICombatTarget selected = null;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moved = true;
            _bootstrap.InputService.OnCombatTargetSelected += t => selected = t;
            Vector3 screen = Camera.main.WorldToScreenPoint(boss.transform.position + Vector3.up);
            Assert.Greater(screen.z, 0f);
            Assert.IsTrue(Physics.Raycast(Camera.main.ScreenPointToRay(screen), out RaycastHit first, 200f,
                                          Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
            Assert.AreSame(boss.gameObject, first.collider.gameObject, "活性：這一下射線最先打到的必須是巨獸本體");
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
            Assert.IsNull(selected, "點己方巨獸不得鎖定它");
            Assert.IsTrue(moved, "己方巨獸應可點穿，點到地面仍能移動");
        }

        [UnityTest]
        public IEnumerator Behemoth_ReachesEnemyMother_AndAttacksHeroAndWall()
        {
            yield return StartCapture();
            yield return SpawnVanguard();
            _opponent.StopRound();
            AbyssalVanguardTarget boss = _bootstrap.VanguardTarget;
            boss.ReceiveDamage(900f, DamageType.Physical, _hero.gameObject);
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(3f, 0f, 0f));
            int frames = 0;
            while (_bootstrap.VanguardLogic.Phase != AbyssalVanguardPhase.Behemoth && frames++ < 300)
                yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, _bootstrap.VanguardLogic.Phase);
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(
                _bootstrap.CaptureSpec.MotherRespawnX(CaptureMatchLogic.BlueFactionId, 0), 0f,
                _bootstrap.CaptureSpec.MotherRespawnZ(CaptureMatchLogic.BlueFactionId, 0)));
            frames = 0;
            while (!boss.HasReachedDestination && frames++ < 900) yield return null;
            Assert.IsTrue(boss.HasReachedDestination, "巨獸應在 15 秒內抵達敵方母板塊");
            Assert.Less(Vector3.Distance(boss.transform.position,
                new Vector3(_bootstrap.CaptureSpec.CenterX(boss.DestinationTile), 0f,
                            _bootstrap.CaptureSpec.CenterZ(boss.DestinationTile))), 1f);

            _opponent.StartRound();
            _opponent.ApplyCaptureStun(10f);
            _opponent.GetComponent<HeroLocomotion>().WarpTo(boss.transform.position + Vector3.right);
            RuneWall wall = _bootstrap.EnemyWalls.SpawnFrom(boss.transform.position - Vector3.forward * 2f,
                                                              Vector3.forward);
            Assert.IsNotNull(wall);
            float heroHealth = _opponent.Health;
            float wallHealth = wall.Health;
            int firstHeroHit = -1, secondHeroHit = -1, firstWallHit = -1;
            for (int i = 1; i <= 200 && secondHeroHit < 0; i++)
            {
                yield return null;
                if (_opponent.Health < heroHealth)
                {
                    // 母板塊是聖所（v0.10 E3～E5）：受傷百分比讀命中當下實際套用的值。
                    Assert.Less(_opponent.DamageTakenPercent, 100, "前提：敵母板塊的聖所減傷生效");
                    Assert.AreEqual(heroHealth - 12f * _opponent.DamageTakenPercent / 100f, _opponent.Health, 0.001f,
                                    "巨獸每次打敵英雄剛好 12 傷（再乘聖所百分比）");
                    heroHealth = _opponent.Health;
                    if (firstHeroHit < 0) firstHeroHit = i; else secondHeroHit = i;
                }
                if (wall.Health < wallHealth)
                {
                    Assert.AreEqual(wallHealth - 60f, wall.Health, 0.001f, "巨獸每次打敵牆剛好 60 傷");
                    wallHealth = wall.Health;
                    if (firstWallHit < 0) firstWallHit = i;
                }
            }
            Assert.Greater(firstHeroHit, 0, "到達母板塊後仍須攻擊敵英雄");
            Assert.Greater(firstWallHit, 0, "到達母板塊後仍須打敵方牆");
            Assert.AreEqual(firstHeroHit, firstWallHit, "同一輪攻擊同時打英雄與牆");
            Assert.That(secondHeroHit - firstHeroHit, Is.InRange(89, 91), "攻擊間隔 1.5 秒");
        }

        [UnityTest]
        public IEnumerator Behemoth_ExpiresAfterNinetySeconds_AndRedBeastHitsBlueHeroFor12()
        {
            yield return StartCapture();
            yield return SpawnVanguard();
            AbyssalVanguardTarget boss = _bootstrap.VanguardTarget;
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(
                _bootstrap.CaptureSpec.MotherRespawnX(CaptureMatchLogic.BlueFactionId, 0), 0f,
                _bootstrap.CaptureSpec.MotherRespawnZ(CaptureMatchLogic.BlueFactionId, 0)));
            boss.ReceiveDamage(900f, DamageType.Physical, _hero.gameObject);
            int frames = 0;
            while (_bootstrap.VanguardLogic.Phase != AbyssalVanguardPhase.Behemoth && frames++ < 720)
                yield return null;
            Assert.AreEqual(CaptureMatchLogic.RedFactionId, _bootstrap.VanguardLogic.BehemothOwner);
            _opponent.StopRound();

            // 紅方巨獸打藍英雄：與藍方巨獸對稱。英雄留在自家母板塊，等巨獸走到。
            float health = _hero.Health;
            int sinceClaim = 0;
            while (_hero.Health >= health && sinceClaim < 1200)
            {
                yield return null;
                sinceClaim++;
            }
            Assert.Less(_hero.Health, health, "紅方巨獸須打到守在母板塊的藍英雄");
            Assert.Less(_hero.DamageTakenPercent, 100, "前提：藍母板塊的聖所減傷生效");
            Assert.AreEqual(health - 12f * _hero.DamageTakenPercent / 100f, _hero.Health, 0.001f,
                            "紅方巨獸打藍英雄剛好 12 傷（再乘聖所百分比）");

            // 90 秒存續：從取得時起算，期滿消失且不再出現。英雄移出巨獸攻擊範圍，避免反覆倒地干擾。
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(0f, 0f, 0f));
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, _bootstrap.VanguardLogic.Phase);
            while (_bootstrap.VanguardLogic.Phase == AbyssalVanguardPhase.Behemoth && sinceClaim < 5700)
            {
                if (_bootstrap.CaptureState != CaptureMatchState.Active) break;
                yield return null;
                sinceClaim++;
            }
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "前提：對局在 90 秒內仍進行中");
            Assert.AreEqual(AbyssalVanguardPhase.Finished, _bootstrap.VanguardLogic.Phase, "90 秒期滿巨獸結束");
            Assert.That(sinceClaim, Is.InRange(5397, 5403), "從取得起 90 秒（5400 幀）才結束");
            yield return null;
            Assert.AreEqual(AbyssalVanguardTarget.ObjectivePhase.Inactive, boss.Phase, "期滿後實體須消失");
            Assert.IsFalse(boss.IsAlive);
        }

        [UnityTest]
        public IEnumerator RedClaimsCore_EnemyBehemothObeysFog_AndSecondMatchResets()
        {
            yield return StartCapture();
            yield return SpawnVanguard();
            AbyssalVanguardTarget boss = _bootstrap.VanguardTarget;
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(
                _bootstrap.CaptureSpec.MotherRespawnX(CaptureMatchLogic.BlueFactionId, 0), 0f,
                _bootstrap.CaptureSpec.MotherRespawnZ(CaptureMatchLogic.BlueFactionId, 0)));
            boss.ReceiveDamage(900f, DamageType.Physical, _hero.gameObject);
            int frames = 0;
            while (_bootstrap.VanguardLogic.Phase != AbyssalVanguardPhase.Behemoth && frames++ < 720)
                yield return null;
            Assert.AreEqual(AbyssalVanguardPhase.Behemoth, _bootstrap.VanguardLogic.Phase,
                "紅方 AI 須走進核心圈並實際完成引導");
            Assert.AreEqual(CaptureMatchLogic.RedFactionId, _bootstrap.VanguardLogic.BehemothOwner);
            Assert.AreEqual(Faction.RedTeam, boss.TargetFaction);
            yield return null;
            StringAssert.StartsWith("RED BEAST ", _bootstrap.VanguardStatusLabel, "HUD 須對稱顯示紅方巨獸");

            _hero.GetComponent<HeroLocomotion>().WarpTo(boss.transform.position + Vector3.back * 7f);
            yield return null;
            yield return null;
            Assert.IsFalse(boss.IsFogVisible, "敵方巨獸在己方視野外須隱藏");
            Assert.IsFalse(_hero.CanEngage(boss), "暗區敵方巨獸不得鎖定");
            _hero.GetComponent<HeroLocomotion>().WarpTo(boss.transform.position + Vector3.back * 5f);
            yield return null;
            yield return null;
            Assert.IsTrue(boss.IsFogVisible, "靠近 6m 應揭露敵方巨獸");
            Assert.IsTrue(_hero.CanEngage(boss));

            _bootstrap.SeedCaptureMatchElapsedForTest(900f);
            yield return null;
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState);
            Assert.AreEqual(AbyssalVanguardTarget.ObjectivePhase.Inactive, boss.Phase);
            frames = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && frames++ < 186) yield return null;
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上回到出生點的英雄（同 Capture19PlayTests）
            Vector3 screen = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(AbyssalVanguardPhase.Dormant, _bootstrap.VanguardLogic.Phase);
            Assert.AreEqual(AbyssalVanguardTarget.ObjectivePhase.Inactive, boss.Phase);
            yield return SpawnVanguard();
            Assert.AreEqual(900f, boss.Health, "第二局須重新生成唯一先鋒並滿血");
        }
    }
}
