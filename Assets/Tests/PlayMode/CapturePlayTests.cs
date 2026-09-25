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
    // v0.8.0 步驟 B 的驗收（V080_CAPTURE_PLAN.md §3-B，V-B01～V-B17，2026-09-24 凍結）。
    // captureDeltaTime＝1/60；「幀」＝一次 yield return null。期望值一律寫字面值，不讀 CaptureTuning／HexBoardLayout。
    // v0.9.0（V090_ENCIRCLE_PLAN.md §2.6，使用者 2026-09-25 同意 §5 Q8）：依賴 7 塊座標的 15 條已退役，
    // 改寫在 Capture19PlayTests.cs（對照表在該檔開頭）；本檔只留逐字保留的 V_B02、V_B03、V_B09、V_B15、V_B16、V_C04。
    public sealed class CapturePlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float PosTolerance = 0.05f;
        private const float EdgeMarginPixels = 8f; // GestureMath.EdgeDeadzonePixels：比這更靠邊的點擊會被路由丟掉

        private static bool _screenLogged;

        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;
        private HeroLocomotion _heroLocomotion;
        private HeroLocomotion _opponentLocomotion;
        private CaptureBoardView _board;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; AllocationProbe.Measuring = false; }

        private IEnumerator Setup()
        {
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _hero = Object.FindObjectOfType<HeroController>();
            _opponent = Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(_bootstrap);
            Assert.IsNotNull(_hero);
            Assert.IsNotNull(_opponent);
            _heroLocomotion = _hero.GetComponent<HeroLocomotion>();
            _opponentLocomotion = _opponent.GetComponent<HeroLocomotion>();
            _board = _bootstrap.CaptureBoard;
            Assert.IsNotNull(_board, "場景缺少 CaptureBoard");
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            if (!_screenLogged)
            {
                _screenLogged = true;
                Debug.Log("[CAPTURE-TEST] Screen " + Screen.width + "x" + Screen.height + " dpi " + Screen.dpi);
            }
        }

        // ───────────── 共用操作（全部走真實觸控路由）─────────────

        private void TapCaptureButton()
        {
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float x, out float y), "拿不到 CAPTURE 鈕的螢幕點");
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

        private void TapOpponent()
        {
            TapWorld(_opponent.transform.position + Vector3.up, "對手");
        }

        private void TapWorld(Vector3 world, string who)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            Assert.Greater(screen.z, 0f, who + " 在鏡頭後方");
            Assert.IsTrue(screen.x >= EdgeMarginPixels && screen.x <= Screen.width - EdgeMarginPixels
                          && screen.y >= EdgeMarginPixels && screen.y <= Screen.height - EdgeMarginPixels,
                who + " 的螢幕投影 (" + screen.x + ", " + screen.y + ") 不在可點範圍內（畫面 "
                + Screen.width + "x" + Screen.height + "）");
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
        }

        // Off → 真實點 CAPTURE → Lobby → 真實點對手 → Active。
        private void EnterLobbyAndStart()
        {
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "在 Lobby 點對手應開佔領局");
        }

        private Faction Owner(int tile) { return _bootstrap.CaptureView.OwnerOf(tile); }

        // ───────────── V-B02 不按 CAPTURE＝v0.7.0 ─────────────
        [UnityTest]
        public IEnumerator V_B02_WithoutCapture_TapOpponentIsTheV070Duel_AndKnockoutResetsInTwoPointFiveSeconds()
        {
            yield return Setup();
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.AreEqual(1, _bootstrap.DuelStartCount);
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.IsFalse(_board.gameObject.activeInHierarchy);

            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            int frames = 0;
            while (_bootstrap.DuelState != DuelRoundState.Dormant && frames < 156)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState, "156 幀內應回單挑待機");
            Assert.GreaterOrEqual(frames, 150, "回單挑待機太早：" + frames + " 幀");
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            Assert.IsFalse(_opponent.IsBodyHidden, "單挑 KO 不得走佔領的倒地隱藏");
            Assert.AreEqual(0, _bootstrap.CaptureRespawnCount);
        }

        // ───────────── V-B03 CAPTURE 鈕走真實觸控 ─────────────
        [UnityTest]
        public IEnumerator V_B03_CaptureButton_TogglesThroughRealTouch_WithoutLeaking_AndIsInertDuringADuel()
        {
            yield return Setup();
            int moves = 0;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moves++;

            Vector3 before = _hero.transform.position;
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Assert.IsTrue(_board.gameObject.activeInHierarchy);
            Assert.AreEqual("CAPTURE: ON", _bootstrap.CaptureButtonLabel);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.Less(Vector3.Distance(before, _hero.transform.position), 0.01f, "點 CAPTURE 漏成移動（第 " + (i + 1) + " 幀）");
                Assert.AreEqual(PlayerState.Idle, _hero.StateMachine.CurrentState);
            }

            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.IsFalse(_board.gameObject.activeInHierarchy);
            Assert.AreEqual("CAPTURE", _bootstrap.CaptureButtonLabel);

            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            before = _hero.transform.position;
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState, "單挑中按 CAPTURE 不得切換模式");
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.IsFalse(_board.gameObject.activeInHierarchy);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.Less(Vector3.Distance(before, _hero.transform.position), 0.01f, "單挑中點 CAPTURE 漏成移動");
            Assert.IsNull(_hero.CurrentTarget);
            Assert.AreEqual(0, moves, "點 CAPTURE 不得滲透成移動指令");
        }

        // ───────────── V-B09 爭奪接線 ─────────────
        [UnityTest]
        public IEnumerator V_B09_BothInsideTowerZero_NobodyFlipsIt()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(0f, 0f, 0f));
            _opponentLocomotion.WarpTo(new Vector3(2f, 0f, 0f));
            int contestBefore = _bootstrap.CaptureContestTickCount;
            for (int f = 1; f <= 240; f++)
            {
                yield return null;
                Assert.AreEqual(Faction.Neutral, Owner(0), "雙方都在 0 號光圈內，第 " + f + " 幀有人翻塊");
            }
            Assert.GreaterOrEqual(_bootstrap.CaptureContestTickCount - contestBefore, 120, "爭奪 tick 數（活性）");
            Assert.IsTrue(_opponent.IsAlive);
        }

        // ───────────── V-B15 元素與測試設施在佔領對局中停用 ─────────────
        [UnityTest]
        public IEnumerator V_B15_ElementsTurretAndEnemyWall_AreLockedInActiveKnockoutAndEnded_ThenUsableInLobby()
        {
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            PressAllTestButtonsAndAssertLocked("佔領 Active");

            _hero.TakeDuelDamage(100f);
            yield return null;
            PressAllTestButtonsAndAssertLocked("英雄倒地");

            _bootstrap.SeedCaptureScoresForTest(998, 0);
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && f < 66)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "種子 998 後一次計分應結束");
            PressAllTestButtonsAndAssertLocked("Ended");

            f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && f < 186)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(1, _bootstrap.ElementField.ActiveZoneCount, "回 Lobby 後 WATER 應可用（活性）");
        }

        private void PressAllTestButtonsAndAssertLocked(string when)
        {
            _bootstrap.PressElementWaterButton();
            _bootstrap.HudPanel.PressTurretButton();
            _bootstrap.HudPanel.PressEnemyWallButton();
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount, when + "：WATER 不得施放");
            Assert.IsFalse(_bootstrap.Turret.IsFiring, when + "：TURRET 不得開火");
            Assert.AreEqual(0, _bootstrap.EnemyWalls.AliveCount(), when + "：ENEMY WALL 不得生牆");
        }

        // ───────────── V-B16 佔領後回單挑不殘留 ─────────────
        [UnityTest]
        public IEnumerator V_B16_BackToDuelAfterCapture_TheOpponentChasesNotTowers_AndKnockoutIsTheV070Reset()
        {
            yield return Setup();
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);

            float startDistance = Vector3.Distance(_hero.transform.position, _opponent.transform.position);
            bool warned = false;
            for (int f = 1; f <= 120; f++)
            {
                yield return null;
                Assert.AreEqual(-1, _opponent.CaptureTargetTile, "單挑中對手不得找塔（第 " + f + " 幀）");
                if (_opponent.IsWarning) warned = true;
            }
            Assert.Less(Vector3.Distance(_hero.transform.position, _opponent.transform.position), startDistance,
                "單挑中對手應朝英雄逼近");
            int g = 0;
            while (_hero.Health >= 100f && g < 120)
            {
                yield return null;
                g++;
                if (_opponent.IsWarning) warned = true;
            }
            Assert.IsTrue(warned, "應出現預警");
            Assert.AreEqual(80f, _hero.Health, 0.01f, "應命中一次");
            Assert.AreEqual(1, _opponent.AttacksResolved);

            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            int k = 0;
            while (_bootstrap.DuelState != DuelRoundState.Dormant && k < 156)
            {
                yield return null;
                k++;
            }
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState, "應走 v0.7.0 的 2.5 秒重置");
            Assert.GreaterOrEqual(k, 150);
            Assert.AreEqual(0, _bootstrap.CaptureRespawnCount, "不得走 5 秒復活");
            Assert.IsFalse(_opponent.IsBodyHidden);
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
        }

        // ═════════════════════════ 步驟 C：組裝與完整一局（V-C04）═════════════════════════

        // ───────────── V-C04 延遲模式開局只開一次 ─────────────
        [UnityTest]
        public IEnumerator V_C04_UnderSimulatedLatency_ATapOnTheOpponentStartsExactlyOneCaptureMatch()
        {
            yield return Setup();
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);

            _bootstrap.SetDuelLatencyPreset(80);
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "80 ms 模擬下開局指令不該立即生效");
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(1, _bootstrap.CaptureStartCount);
            Assert.AreEqual(0, _bootstrap.DuelStartCount, "開局點擊不得同時開單挑");
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);

            // 下一局：先把這局結束並回 Lobby
            _bootstrap.SeedCaptureScoresForTest(998, 0);
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && f < 300)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "第一局應結束並回 Lobby");
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上回到出生點的英雄

            _bootstrap.SetDuelLatencyPreset(50);
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "50 ms 模擬下開局指令不該立即生效");
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(2, _bootstrap.CaptureStartCount, "50 ms 模式下一次點擊只應開一局");
            Assert.AreEqual(0, _bootstrap.DuelStartCount, "開局點擊不得同時開單挑");
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
        }
    }

    // V-C03：在 Update 夾區內（探針架上）於窗口第 KnockoutAtFrame 幀擊倒英雄，讓倒地／復活路徑落在量測範圍裡。
    public sealed class CaptureKnockoutDriver : MonoBehaviour
    {
        internal HeroController Hero;
        public int KnockoutAtFrame = 160;
        public int MeasuredFrames;
        public bool Fired;

        private void Update()
        {
            if (!AllocationProbe.Measuring || Hero == null) return;
            MeasuredFrames++;
            if (Fired || MeasuredFrames != KnockoutAtFrame) return;
            Fired = true;
            Hero.TakeDuelDamage(100f);
        }
    }
}
