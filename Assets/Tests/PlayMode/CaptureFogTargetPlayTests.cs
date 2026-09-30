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
    public sealed class CaptureFogTargetPlayTests
    {
        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;
        private GameObject _opponentOverhead;
        private GameObject _enemyWallOverhead;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; }

        private IEnumerator StartCapture()
        {
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _bootstrap.UseFlatCaptureSpecForTest();
            _hero = Object.FindObjectOfType<HeroController>();
            _opponent = Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(_bootstrap);
            Assert.IsNotNull(_hero);
            Assert.IsNotNull(_opponent);
            _opponentOverhead = GameObject.Find(_opponent.name + "_Overhead");
            Assert.IsNotNull(_opponentOverhead, "對手獨立血條應在 Awake 建立");
            RuneWall firstWall = _bootstrap.EnemyWalls.Pool[0];
            _enemyWallOverhead = GameObject.Find(firstWall.name + "_Overhead");
            Assert.IsNotNull(_enemyWallOverhead, "敵牆獨立血條應在 Awake 建立");
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float buttonX, out float buttonY));
            _bootstrap.WorldTapInput.SendScreenTap(buttonX, buttonY);
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Tap(_opponent.transform.position + Vector3.up);
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            yield return null;
        }

        private void Tap(Vector3 world)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            Assert.Greater(screen.z, 0f);
            Assert.IsTrue(screen.x >= 8f && screen.x <= Screen.width - 8f
                && screen.y >= 8f && screen.y <= Screen.height - 8f,
                "測試點投影在畫面外：" + screen);
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
        }

        private void SeedAllNeutral()
        {
            int[] owners = new int[19];
            for (int i = 0; i < owners.Length; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
        }

        [UnityTest]
        public IEnumerator DarkEnemy_CannotBeSelected_AndOwnedTileRevealsImmediately()
        {
            yield return StartCapture();
            SeedAllNeutral();
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(0f, 0f, -15.15625f));
            _opponent.GetComponent<HeroLocomotion>().WarpTo(Vector3.zero);
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_opponent));
            Assert.IsFalse(_opponent.GetComponentInChildren<Renderer>(true).enabled,
                "暗區敵方模型應隱藏");
            Assert.IsFalse(_opponentOverhead.activeSelf, "獨立血條不得洩漏暗區敵人位置");
            bool moved = false;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moved = true;
            Tap(_opponent.transform.position + Vector3.up);
            Assert.IsNull(_hero.CurrentTarget, "暗區點擊不應留下目標");
            Assert.IsTrue(moved, "點隱形敵人投影應穿過 Collider 落到地面探索");

            int[] owners = new int[19];
            for (int i = 0; i < owners.Length; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            owners[0] = CaptureMatchLogic.BlueFactionId;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
            yield return null;
            Assert.IsTrue(_hero.CanEngage(_opponent), "己方板塊提供遠端真視野");
            Assert.IsTrue(_opponent.GetComponentInChildren<Renderer>(true).enabled);
            Assert.IsTrue(_opponentOverhead.activeSelf, "真視野應恢復對手血條");
            Tap(_opponent.transform.position + Vector3.up);
            Assert.AreSame(_opponent, _hero.CurrentTarget);

            owners[0] = CaptureMatchLogic.NeutralFactionId;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
            yield return null;
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_opponent));
            Assert.IsNull(_hero.CurrentTarget, "歸屬失去後舊鎖定應取消");
            Assert.IsFalse(_opponentOverhead.activeSelf);
        }

        [UnityTest]
        public IEnumerator EnemyWall_FogHidesRendererButKeepsCollision()
        {
            yield return StartCapture();
            SeedAllNeutral();
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(0f, 0f, -15.15625f));
            RuneWall wall = _bootstrap.EnemyWalls.SpawnFrom(new Vector3(0f, 0f, 15.15625f), Vector3.forward);
            Assert.IsNotNull(wall, "敵牆須成功立起，否則沒有驗收活性");
            yield return null;
            Renderer renderer = wall.GetComponentInChildren<Renderer>(true);
            Collider collider = wall.GetComponent<Collider>();
            Assert.IsFalse(renderer.enabled, "暗區牆不顯示");
            Assert.IsFalse(_enemyWallOverhead.activeSelf, "暗區牆獨立血條也應隱藏");
            Assert.IsTrue(collider.enabled, "迷霧不得移除碰撞");
            Assert.IsFalse(_hero.CanEngage(wall), "暗區牆不能被鎖定");

            _hero.GetComponent<HeroLocomotion>().WarpTo(wall.transform.position + Vector3.back * 5.5f);
            yield return null;
            Assert.IsTrue(renderer.enabled, "6m 內敵牆應揭露");
            Assert.IsTrue(_enemyWallOverhead.activeSelf);
            Assert.IsTrue(collider.enabled);
            Assert.IsTrue(_hero.CanEngage(wall));
        }

        [UnityTest]
        public IEnumerator RedRageAura_DoesNotRevealHiddenOpponent()
        {
            yield return StartCapture();
            int[] owners = new int[19];
            for (int i = 0; i < owners.Length; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            foreach (int tile in new[] { 12, 13, 14, 3, 2 }) owners[tile] = CaptureMatchLogic.BlueFactionId;
            foreach (int tile in new[] { 7, 8, 18, 9, 10 }) owners[tile] = CaptureMatchLogic.RedFactionId;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
            _bootstrap.SeedCaptureScoresForTest(100, 0);
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(13.125f, 0f, 7.578125f));
            int frames = 0;
            while (_bootstrap.CaptureView.OwnerOf(9) != Faction.BlueTeam && frames++ < 240) yield return null;
            Assert.AreEqual(Faction.BlueTeam, _bootstrap.CaptureView.OwnerOf(9), "須真的翻塊觸發狂怒");
            Assert.Greater(_bootstrap.CaptureView.RedRageRemaining, 0f);

            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(0f, 0f, -15.15625f));
            _opponent.GetComponent<HeroLocomotion>().WarpTo(Vector3.zero);
            yield return null;
            Assert.IsFalse(_bootstrap.RageAuras.RedAura.enabled, "暗區狂怒光環不得洩漏敵人位置");
            owners[0] = CaptureMatchLogic.BlueFactionId;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
            yield return null;
            Assert.IsTrue(_bootstrap.RageAuras.RedAura.enabled, "真視野應顯示狂怒光環");
        }

        [UnityTest]
        public IEnumerator OwnedTileTrueVision_RevealsThroughSteam_WithoutChangingLocalSteamRules()
        {
            yield return StartCapture();
            SeedAllNeutral();
            _hero.GetComponent<HeroLocomotion>().WarpTo(Vector3.zero);
            _opponent.GetComponent<HeroLocomotion>().WarpTo(new Vector3(0f, 0f, 6f));
            Vector3 steam = new Vector3(0f, 0f, 9f);
            _bootstrap.ElementField.CastWater(steam, (int)Faction.BlueTeam);
            _bootstrap.ElementField.CastFire(steam, (int)Faction.BlueTeam);
            yield return null;
            Assert.Greater(_bootstrap.ElementField.CountZonesOfKind(ElementZoneKind.Steam), 0,
                "蒸氣須確實成形");
            Assert.IsFalse(_hero.CanEngage(_opponent), "非己方板塊內，6m 局部視野仍受蒸氣遮蔽");

            int[] owners = new int[19];
            for (int i = 0; i < owners.Length; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            owners[1] = CaptureMatchLogic.BlueFactionId;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
            yield return null;
            Assert.IsTrue(_hero.CanEngage(_opponent), "己方板塊真視野應穿透蒸氣");
            owners[1] = CaptureMatchLogic.NeutralFactionId;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_opponent), "失去板塊後蒸氣遮蔽應恢復");
        }

        [UnityTest]
        public IEnumerator RedAi_ChasesVisibleHero_AndReturnsToCaptureWhenVisibilityEnds()
        {
            yield return StartCapture();
            SeedAllNeutral();
            var heroBody = _hero.GetComponent<HeroLocomotion>();
            var redBody = _opponent.GetComponent<HeroLocomotion>();
            heroBody.WarpTo(new Vector3(0f, 0f, -15.15625f));
            redBody.WarpTo(Vector3.zero);
            yield return null;
            yield return null;
            Assert.IsFalse(_opponent.IsChasingHero, "暗區英雄不得觸發紅方追擊");

            heroBody.WarpTo(new Vector3(0f, 0f, -5f));
            yield return null;
            yield return null;
            Assert.IsTrue(_opponent.IsChasingHero, "看見英雄後應進入既有追擊");

            heroBody.WarpTo(new Vector3(0f, 0f, -15.15625f));
            yield return null;
            yield return null;
            Assert.IsFalse(_opponent.IsChasingHero, "英雄離開視野後應停止追擊");
            Assert.GreaterOrEqual(_opponent.CaptureTargetTile, 0, "應回到佔點決策");
        }
    }
}
