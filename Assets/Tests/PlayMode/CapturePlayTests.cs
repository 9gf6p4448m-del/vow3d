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
            _bootstrap.UseFlatCaptureSpecForTest();
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

        // ───────────── V-B15：v0.11.0 佔領 Active 開元素，其餘測試設施仍鎖 ─────────────
        [UnityTest]
        public IEnumerator V_B15_ActiveOpensElements_ButKnockoutAndEndedLockThem_AndDebugToolsStayLocked()
        {
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            int windBefore = _bootstrap.SectorTelegraph.ShowCount;
            _bootstrap.PressElementWindButton();
            Assert.AreEqual(windBefore + 1, _bootstrap.SectorTelegraph.ShowCount,
                "佔領 Active 應可施放 WIND（活性）");
            PressDebugButtonsAndAssertLocked("佔領 Active");

            _hero.TakeDuelDamage(200f); // v0.10.0 §2.6 T5②：100 → 200（英雄在 13 號聖所內 100 只受 85、不倒地）
            yield return null;
            Assert.IsTrue(_bootstrap.CaptureView.BlueKnockedOut, "前提：英雄確實倒地");
            PressWaterAndDebugButtonsAndAssertLocked("英雄倒地");

            _bootstrap.SeedCaptureScoresForTest(999, 0); // v0.10.0 §2.6 T5①：(998, 0) → (999, 0)（慢計分）
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && f < 186) // v0.10.0 §2.6 T5①：窗口 66 → 186 幀
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "種子 999 後一次計分應結束");
            PressWaterAndDebugButtonsAndAssertLocked("Ended");

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

        private void PressWaterAndDebugButtonsAndAssertLocked(string when)
        {
            int firesBefore = _bootstrap.ElementField.PlainFireCount;
            _bootstrap.PressElementWaterButton();
            _bootstrap.PressElementFireButton();
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount, when + "：WATER 不得施放");
            Assert.AreEqual(firesBefore, _bootstrap.ElementField.PlainFireCount,
                when + "：FIRE 不得施放");
            PressDebugButtonsAndAssertLocked(when);
        }

        private void PressDebugButtonsAndAssertLocked(string when)
        {
            _bootstrap.HudPanel.PressTurretButton();
            _bootstrap.HudPanel.PressEnemyWallButton();
            Assert.IsFalse(_bootstrap.Turret.IsFiring, when + "：TURRET 不得開火");
            Assert.AreEqual(0, _bootstrap.EnemyWalls.AliveCount(), when + "：ENEMY WALL 不得生牆");
        }

        // B0：施法、ELEM 與除錯鈕都走 HUD 實際矩形的真實觸控，不直接呼叫委派。
        [UnityTest]
        public IEnumerator V0110_B0_CaptureActive_RealHudTouchesCastAllThreeBlueElements_WithoutOpeningDebugToolsOrLeakingWorldInput()
        {
            yield return Setup();
            DebugHudLayout layout = CurrentHudLayout();
            yield return TapHudRect(layout, layout.Elem, "開局前 ELEM");
            Assert.AreEqual(Faction.RedTeam, _bootstrap.ElementCastFaction, "前提：開局前 ELEM 已切紅");

            EnterLobbyAndStart();
            yield return null;
            layout = CurrentHudLayout(true);
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount, "開局應清除舊元素區");

            int moves = 0;
            int picks = 0;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moves++;
            _bootstrap.InputService.OnCombatTargetSelected += _ => picks++;

            yield return TapHudRect(layout, layout.Water, "WATER");
            Assert.AreEqual(1, _bootstrap.ElementField.CountZonesOfKind(ElementZoneKind.Water),
                "Active 觸碰 WATER 應形成水域");
            bool foundBlueWater = false;
            for (int slot = 0; slot < _bootstrap.ElementField.ZoneCapacity; slot++)
            {
                if (!_bootstrap.ElementField.TryGetZoneBySlot(slot, out ElementZone zone)) continue;
                if (zone.Kind != ElementZoneKind.Water) continue;
                Assert.AreEqual((int)Faction.BlueTeam, zone.FactionId,
                    "開局前即使 ELEM 切紅，佔領局也只能施放藍方元素");
                foundBlueWater = true;
            }
            Assert.IsTrue(foundBlueWater);

            _hero.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return null; // 火的落點避開剛生成的水域，否則會變蒸氣而不留燃燒區。
            int firesBefore = _bootstrap.ElementField.PlainFireCount;
            yield return TapHudRect(layout, layout.Fire, "FIRE");
            Assert.AreEqual(firesBefore + 1, _bootstrap.ElementField.PlainFireCount,
                "Active 觸碰 FIRE 應施放空地火");

            int windBefore = _bootstrap.SectorTelegraph.ShowCount;
            yield return TapHudRect(layout, layout.Wind, "WIND");
            Assert.AreEqual(windBefore + 1, _bootstrap.SectorTelegraph.ShowCount,
                "Active 觸碰 WIND 應放出扇形預警");

            Faction factionBefore = _bootstrap.ElementCastFaction;
            yield return TapHudRect(layout, layout.Elem, "Active ELEM");
            Assert.AreEqual(factionBefore, _bootstrap.ElementCastFaction,
                "Active 觸碰 ELEM 不得切換除錯陣營");
            yield return TapHudRect(layout, layout.EnemyWall, "ENEMY WALL");
            yield return TapHudRect(layout, layout.Turret, "TURRET");
            Assert.AreEqual(0, _bootstrap.EnemyWalls.AliveCount(), "Active 觸碰 ENEMY WALL 不得生牆");
            Assert.IsFalse(_bootstrap.Turret.IsFiring, "Active 觸碰 TURRET 不得開火");
            Assert.AreEqual(0, moves, "觸碰元素／除錯鈕不得漏成移動");
            Assert.AreEqual(0, picks, "觸碰元素／除錯鈕不得漏成世界目標選取");

            Assert.Greater(_bootstrap.ElementField.ActiveZoneCount, 0,
                "結算清場的前提：Active 仍有元素區域");
            _bootstrap.SeedCaptureScoresForTest(999, 0);
            int frames = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && frames < 186)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "比分達 1000 後應結算");
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount,
                "結算時應清除 Active 留下的元素區域");
            frames = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && frames < 186)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount,
                "回 Lobby 不得沿用上一局的元素區域");
        }

        [UnityTest]
        public IEnumerator V0110_B0_DuelActive_RealWaterTouchRemainsLocked()
        {
            yield return Setup();
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            DebugHudLayout layout = CurrentHudLayout();
            yield return TapHudRect(layout, layout.Water, "單挑 WATER");
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount,
                "單挑 Active 觸碰 WATER 仍不能新增元素區");
        }

        [Test]
        public void V0110_B0_ElementButtonsRemainReachableAtTheLandscapeTrialViewport()
        {
            DebugHudLayout layout = DebugHudLayout.Compute(844f, 390f, 0f, true, true, true, true, true);
            AssertInsideViewport(layout.Water, layout.Scale, 844f, 390f, "WATER");
            AssertInsideViewport(layout.Fire, layout.Scale, 844f, 390f, "FIRE");
            AssertInsideViewport(layout.Wind, layout.Scale, 844f, 390f, "WIND");
        }

        [UnityTest]
        public IEnumerator V0110_C01_TalentPanelShowsThreeChoicesAtEachScoreTier_ThenHidesInEndedAndLobby()
        {
            yield return Setup();
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "Off 不得顯示天賦盤");
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            yield return null;
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "Lobby 不得顯示天賦盤");
            TapOpponent();
            yield return null;
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "未達門檻不得顯示天賦盤");

            _bootstrap.SeedCaptureScoresForTest(250, 0);
            yield return null;
            AssertTalentChoices(1, "PACT TALENT 1", "SWIFT 1.4/1/.7", "STONE SHIELD 220", "FIRE CD -15%");
            TapTalentOption(0);
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "放開同一鈕當幀應完成 Tier 1 選擇");
            Assert.AreEqual(0, _bootstrap.CaptureView.BluePendingTalentTier);

            _bootstrap.SeedCaptureScoresForTest(750, 0);
            yield return null;
            AssertTalentChoices(2, "PACT TALENT 2", "PIERCE NEXT HIT", "WATER RADIUS +2", "LUNGE STUN 0.5S");
            TapTalentOption(1);
            AssertTalentChoices(3, "PACT TALENT 3", "OWN TILE DMG +15%", "DASH CHARGES 4", "COMBO MAX HP +8%");
            TapTalentOption(1);
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "三階選完應收起選擇盤");
            Assert.AreEqual(4, _hero.Mover.MaxCharges, "極限超頻應在觸控放開當幀套用");

            _bootstrap.SeedCaptureScoresForTest(999, 0);
            int frames = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && frames < 186)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState);
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "Ended 不得顯示天賦盤");
            frames = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && frames < 186)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "回 Lobby 不得殘留天賦盤");
            Assert.AreEqual(3, _hero.Mover.MaxCharges, "結算後極限超頻不得殘留");
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.IsFalse(_bootstrap.TalentPanelVisible);
        }

        [UnityTest]
        public IEnumerator V0110_C04_CrossButtonOrPanelReleaseDoesNotChooseOrLeak_ButSameButtonReleaseChoosesImmediately()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(250, 0);
            yield return null;
            Assert.AreEqual(1, _bootstrap.TalentPendingTier, "前提：Tier 1 有待選項");
            Assert.IsTrue(_bootstrap.TryGetTalentButtonScreenPoint(0, out float firstX, out float firstY));
            Assert.IsTrue(_bootstrap.TryGetTalentButtonScreenPoint(1, out float secondX, out float secondY));

            int moves = 0, picks = 0, flicks = 0, runes = 0;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moves++;
            _bootstrap.InputService.OnCombatTargetSelected += _ => picks++;
            _bootstrap.InputService.OnCadenceVectorFlicked += _ => flicks++;
            _bootstrap.InputService.OnRuneQuickCastTriggered += () => runes++;

            _bootstrap.BeginScreenHold(firstX, firstY);
            yield return null;
            Assert.AreEqual(1, _bootstrap.TalentPendingTier, "按下未放開不能先選");
            _bootstrap.MoveScreenHold(secondX, secondY);
            _bootstrap.EndScreenHold();
            Assert.AreEqual(1, _bootstrap.TalentPendingTier, "第一鈕按下、第二鈕放開不得選");

            DebugHudLayout layout = CurrentHudLayout(true);
            float outsideX = (layout.TalentPanel.XMax + 10f) * layout.Scale;
            _bootstrap.BeginScreenHold(firstX, firstY);
            _bootstrap.MoveScreenHold(outsideX, firstY);
            _bootstrap.EndScreenHold();
            Assert.AreEqual(1, _bootstrap.TalentPendingTier, "從鈕移到盤外放開不得選");

            yield return TapHudRect(layout, layout.TalentTitle, "天賦標題");
            Assert.AreEqual(1, _bootstrap.TalentPendingTier, "點標題不得選天賦");
            yield return TapHudRect(layout, new HudRect(layout.TalentPanel.X, 123f, 176f, 4f), "天賦鈕間隙");
            Assert.AreEqual(1, _bootstrap.TalentPendingTier, "點鈕間隙不得選天賦");

            _bootstrap.BeginScreenHold(firstX, firstY);
            yield return null;
            _bootstrap.EndScreenHold();
            Assert.AreEqual(0, _bootstrap.TalentPendingTier, "同一鈕放開當幀應選入天賦");
            Assert.IsFalse(_bootstrap.TalentPanelVisible);
            _bootstrap.PressTalentButton(1);
            Assert.AreEqual(0, _bootstrap.TalentPendingTier, "選後再按處理器不得重選");
            Assert.AreEqual(0, moves, "天賦盤觸控不得漏成移動");
            Assert.AreEqual(0, picks, "天賦盤觸控不得漏成普攻鎖定");
            Assert.AreEqual(0, flicks, "天賦盤觸控不得漏成微滑步");
            Assert.AreEqual(0, runes, "天賦盤觸控不得漏成符印");
        }

        [UnityTest]
        public IEnumerator V0110_C04_TwoFingersPressedOnTierOne_CannotChooseTierTwoWithTheStaleSecondRelease()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(750, 0); // 三階同時解鎖，才能重現跨階錯選。
            yield return null;
            Assert.AreEqual(1, _bootstrap.TalentPendingTier);
            Assert.IsTrue(_bootstrap.TryGetTalentButtonScreenPoint(0, out float firstX, out float firstY));
            Assert.IsTrue(_bootstrap.TryGetTalentButtonScreenPoint(1, out float secondX, out float secondY));

            int moves = 0, picks = 0, flicks = 0, runes = 0;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moves++;
            _bootstrap.InputService.OnCombatTargetSelected += _ => picks++;
            _bootstrap.InputService.OnCadenceVectorFlicked += _ => flicks++;
            _bootstrap.InputService.OnRuneQuickCastTriggered += () => runes++;

            _bootstrap.BeginScreenHold(0, firstX, firstY);
            _bootstrap.BeginScreenHold(1, secondX, secondY);
            yield return null;
            Assert.AreEqual(1, _bootstrap.TalentPendingTier, "兩指僅按下時仍不得選");
            _bootstrap.EndScreenHold(0);
            Assert.AreEqual(2, _bootstrap.TalentPendingTier,
                "第一指放開應只選 Tier 1，並立即顯示 Tier 2");
            _bootstrap.EndScreenHold(1);
            Assert.AreEqual(2, _bootstrap.TalentPendingTier,
                "舊 Tier 1 上按下的第二指放開不得誤選同位置 Tier 2");
            Assert.IsTrue(_bootstrap.TalentPanelVisible);

            TapTalentOption(1); // 新的一次觸控仍可正常選 Tier 2。
            Assert.AreEqual(3, _bootstrap.TalentPendingTier);
            Assert.AreEqual(0, moves);
            Assert.AreEqual(0, picks);
            Assert.AreEqual(0, flicks);
            Assert.AreEqual(0, runes);
        }

        [UnityTest]
        public IEnumerator V0110_B1_FireSurge_RealTalentChoiceGivesEachElementA425SecondCooldown()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(250, 0);
            yield return null;
            Assert.AreEqual(1, _bootstrap.TalentPendingTier);
            TapTalentOption(2); // 熾火涌；不能直接呼叫冷卻邏輯跳過三選接線。
            Assert.AreEqual(0, _bootstrap.TalentPendingTier);
            Assert.AreEqual(5f, _bootstrap.ElementTuning.SkillCooldownSeconds, 0.001f,
                "天賦不能改寫共享的元素 tuning");

            float castAt = Time.time;
            CastThreeElementButtons();
            Assert.AreEqual(1, _bootstrap.ElementField.CountZonesOfKind(ElementZoneKind.Water));
            Assert.AreEqual(1, _bootstrap.ElementField.PlainFireCount);
            Assert.AreEqual(1, _bootstrap.SectorTelegraph.ShowCount);

            while (Time.time - castAt < 4.23f) yield return null;
            CastThreeElementButtons();
            Assert.AreEqual(1, _bootstrap.ElementField.CountZonesOfKind(ElementZoneKind.Water),
                "4.23 秒前 WATER 仍在冷卻");
            Assert.AreEqual(1, _bootstrap.ElementField.PlainFireCount, "4.23 秒前 FIRE 仍在冷卻");
            Assert.AreEqual(1, _bootstrap.SectorTelegraph.ShowCount, "4.23 秒前 WIND 仍在冷卻");

            while (Time.time - castAt < 4.28f) yield return null;
            CastThreeElementButtons();
            Assert.AreEqual(2, _bootstrap.ElementField.CountZonesOfKind(ElementZoneKind.Water),
                "4.28 秒後 WATER 應完成 4.25 秒冷卻");
            Assert.AreEqual(2, _bootstrap.ElementField.PlainFireCount,
                "4.28 秒後 FIRE 應完成 4.25 秒冷卻");
            Assert.AreEqual(2, _bootstrap.SectorTelegraph.ShowCount,
                "4.28 秒後 WIND 應完成 4.25 秒冷卻");
        }

        [UnityTest]
        public IEnumerator V0110_B1_TidalPull_OnlyNewWaterZonesGrowToFiveMeters_AndNextRoundReturnsToThree()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _hero.transform.rotation = Quaternion.identity;
            float firstCastAt = Time.time;
            _bootstrap.PressElementWaterButton();
            ElementZone original = LatestWaterZone();
            Assert.AreEqual(3f, original.Radius, 0.01f, "未選潮汐引時水域半徑為 3m");

            _bootstrap.SeedCaptureScoresForTest(250, 0);
            yield return null;
            TapTalentOption(2); // 熾火涌：舊水冷卻不得被事後縮短。
            _bootstrap.SeedCaptureScoresForTest(500, 0);
            yield return null;
            TapTalentOption(1); // 潮汐引。

            while (Time.time - firstCastAt < 4.28f) yield return null;
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(1, _bootstrap.ElementField.CountZonesOfKind(ElementZoneKind.Water),
                "選熾火涌後，選前已開始的 5 秒 WATER 冷卻不得回溯成 4.25 秒");
            while (Time.time - firstCastAt < 5.03f) yield return null;
            _hero.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            _bootstrap.PressElementWaterButton();
            ElementZone enlarged = LatestWaterZone();
            Assert.Greater(enlarged.Id, original.Id, "冷卻結束後應新生成水域");
            Assert.AreEqual(5f, enlarged.Radius, 0.01f, "新水域半徑應為 5m");
            Assert.IsTrue(_bootstrap.ElementField.TryGetZoneById(original.Id, out ElementZone stillOriginal),
                "舊水域在 6 秒生命期內應仍存活，才能驗證不回溯改寫");
            Assert.AreEqual(3f, stillOriginal.Radius, 0.01f, "選天賦前已存在的水域仍為 3m");

            yield return EndCaptureAndReturnToLobby();
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(0, _bootstrap.TalentPendingTier, "新局未達門檻不能沿用天賦");
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(3f, LatestWaterZone().Radius, 0.01f,
                "重開佔領局後新水域應回到 3m");
        }

        [UnityTest]
        public IEnumerator V0110_B1_StoneBody_RealMeleeBreaksAnEnemyWallFor220_ButOtherWallDeathsDoNot()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(250, 0);
            yield return null;
            TapTalentOption(1); // 堅磐體；後面的敵牆只由測試擺盤入口生成，不解鎖 Active 的除錯鈕。
            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 0.001f, "選天賦不能直接贈送護盾");
            ScriptedInput meleeInput = new ScriptedInput();
            _hero.Initialize(meleeInput, null, Camera.main); // 擺盤牆在 batchmode 鏡頭外；仍走大腦真實攻擊結算。

            RuneWall enemy = _bootstrap.EnemyWalls.Spawn();
            Assert.IsNotNull(enemy, "測試擺盤應生成紅方石牆");
            Assert.AreEqual(Faction.RedTeam, enemy.OwnerFaction);
            yield return MeleeBreakWall(enemy, meleeInput, "紅方牆");
            Assert.AreEqual(220f, _bootstrap.Shield.Amount, 0.01f,
                "選堅磐體後由英雄近戰擊碎敵牆應取得 220 護盾");
            Assert.AreEqual(220f, _bootstrap.Shield.GrantedAmount, 0.01f,
                "該次護盾滿值應為 220，不得改共用 150 tuning");
            yield return null; // HeroShieldBar.LateUpdate／DebugHud.Update 追上當幀授予。
            HeroShieldBar bar = Object.FindObjectOfType<HeroShieldBar>();
            Assert.IsNotNull(bar, "場景缺少英雄頭頂護盾條");
            Assert.IsTrue(bar.IsVisible, "取得護盾後頭頂條應顯示");
            GameObject barRoot = GameObject.Find(_hero.name + "_ShieldBar");
            Assert.IsNotNull(barRoot);
            Transform fill = barRoot.transform.Find("ShieldBarFill");
            Transform background = barRoot.transform.Find("ShieldBarBackground");
            Assert.IsNotNull(fill);
            Assert.IsNotNull(background);
            Assert.AreEqual(1f, fill.localScale.x / (background.localScale.x - 0.06f), 0.001f,
                "220 護盾應把頭頂條填滿");
            Assert.AreEqual("220", _bootstrap.HudPanel.ShieldValueLabel,
                "HUD 護盾值應顯示 220");

            float grantedAt = Time.time;
            while (Time.time - grantedAt < 2.43f) yield return null;
            Assert.Greater(_bootstrap.Shield.Amount, 0f, "2.43 秒前護盾仍應存活");
            while (Time.time - grantedAt < 2.57f) yield return null;
            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 0.01f,
                "堅磐體不應延長原本 2.5 秒護盾時效");

            RuneWall[] pool = _bootstrap.EnemyWalls.Pool;
            Assert.GreaterOrEqual(pool.Length, 3, "需至少三面預建牆區分中立、彈擊與到期擺盤");
            RuneWall neutral = pool[1];
            Vector3 neutralPoint = _hero.transform.position + _hero.transform.forward * 4f;
            neutralPoint.y = _hero.transform.position.y + 1f;
            neutral.Activate(neutralPoint, _hero.transform.rotation, Faction.Neutral, null, -1);
            yield return MeleeBreakWall(neutral, meleeInput, "中立牆");
            Assert.AreEqual(150f, _bootstrap.Shield.Amount, 0.01f,
                "選堅磐體後近戰擊碎中立牆仍給基本 150 護盾");
            Assert.AreEqual(150f, _bootstrap.Shield.GrantedAmount, 0.01f);

            _bootstrap.Shield.Clear();
            RuneWall bulletWall = pool[2];
            bulletWall.Activate(new Vector3(-6f, 1f, 6f), Quaternion.LookRotation(Vector3.right),
                                Faction.RedTeam, null, -1);
            bulletWall.ReceiveDamage(bulletWall.Health - 1f, DamageType.Physical, null);
            int shotsBefore = _bootstrap.Turret.ShotsFired;
            _bootstrap.Turret.SetFiring(true); // 測試設施直接擺盤，不經 Active 已鎖住的 HUD 鈕。
            int frames = 0;
            while (bulletWall.IsAlive && frames < 240)
            {
                yield return null;
                frames++;
            }
            _bootstrap.Turret.SetFiring(false);
            Assert.Greater(_bootstrap.Turret.ShotsFired, shotsBefore, "前提：砲台真的發射過子彈");
            Assert.IsFalse(bulletWall.IsAlive, "紅方牆應被實際子彈擊碎");
            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 0.01f,
                "子彈擊碎敵牆不能觸發近戰專屬護盾");

            RuneWall timedWall = pool[1];
            timedWall.Activate(new Vector3(10f, 1f, 10f), Quaternion.identity, Faction.RedTeam, null, -1);
            yield return new WaitForSeconds(5.2f);
            Assert.IsFalse(timedWall.IsAlive, "前提：敵牆應自然到期");
            Assert.AreEqual(0f, _bootstrap.Shield.Amount, 0.01f,
                "敵牆自然到期不能觸發近戰專屬護盾");
        }

        [UnityTest]
        public IEnumerator V0110_B1_SwiftAndOverclock_RealChoicesGiveFourChainedDashes_ThenResetForNextRoundAndDuel()
        {
            yield return Setup();
            EnterLobbyAndStart();
            Assert.AreEqual(3, _hero.Mover.MaxCharges);
            Assert.AreEqual(3, _hero.Mover.CurrentCharges);
            _bootstrap.SeedCaptureScoresForTest(750, 0);
            yield return null;
            TapTalentOption(0); // Tier1 迅影步。
            TapTalentOption(1); // Tier2 潮汐引；此測試不使用元素。
            TapTalentOption(1); // Tier3 極限超頻。
            Assert.AreEqual(4, _hero.Mover.MaxCharges, "選超頻當幀上限應變 4");
            Assert.AreEqual(3, _hero.Mover.CurrentCharges, "選超頻不能立即贈送第四格");

            float selectedAt = Time.time;
            while (Time.time - selectedAt < 2.45f) yield return null;
            Assert.AreEqual(3, _hero.Mover.CurrentCharges, "第四格不得早於 2.5 秒回充");
            while (Time.time - selectedAt < 2.55f) yield return null;
            Assert.AreEqual(4, _hero.Mover.CurrentCharges, "第四格應照既有 2.5 秒節奏自然回充");

            float[] distances = new float[4];
            int dashCount = 0;
            _hero.Mover.OnDashExecuted += distance =>
            {
                if (dashCount < distances.Length) distances[dashCount] = distance;
                dashCount++;
            };
            for (int i = 0; i < 4; i++) yield return DashAndWait();
            Assert.AreEqual(4, dashCount);
            Assert.AreEqual(0, _hero.Mover.CurrentCharges, "四段連滑應消耗四格");
            Assert.AreEqual(1.4f, distances[0], 0.01f);
            Assert.AreEqual(1.0f, distances[1], 0.01f);
            Assert.AreEqual(0.7f, distances[2], 0.01f);
            Assert.AreEqual(0.7f, distances[3], 0.01f, "第四段應沿用迅影步最後一段距離");

            yield return EndCaptureAndReturnToLobby();
            Assert.AreEqual(3, _hero.Mover.MaxCharges, "結算後充能上限應回到 3");
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(3, _hero.Mover.MaxCharges, "新局不能沿用極限超頻");
            Assert.AreEqual(3, _hero.Mover.CurrentCharges, "新局應補滿基本三格");
            dashCount = 0;
            yield return DashAndWait();
            yield return DashAndWait();
            Assert.AreEqual(2, dashCount);
            Assert.AreEqual(1.4f, distances[0], 0.01f);
            Assert.AreEqual(0.9f, distances[1], 0.01f,
                "新局第二段應回到未選迅影步的 0.9m");

            yield return EndCaptureAndReturnToLobby();
            TapCaptureButton();
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.AreEqual(3, _hero.Mover.MaxCharges, "回單挑後仍是基本三格");
            Assert.AreEqual(1.4f, _hero.Mover.NextDashDistance, 0.01f,
                "單挑不能沿用上一局的連滑段數");
        }

        [UnityTest]
        public IEnumerator V0110_B2_StoneShock_LandedBasicStunsDuringWindup_ThenResumesRemainingWindup()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(500, 0);
            yield return null;
            TapTalentOption(0); // Tier 1 SwiftStep, allowing Tier 2 selection.
            TapTalentOption(2); // Tier 2 StoneShock.

            ScriptedInput input = new ScriptedInput();
            _hero.Initialize(input, null, Camera.main);
            _opponent.transform.position = _hero.transform.position + Vector3.forward * 1.1f;
            yield return null;
            Assert.IsTrue(_hero.Mover.TryExecuteCadenceDash(Vector3.back), "成功微滑步是增效前提");
            int dashFrames = 0;
            while (_hero.Mover.IsDashing && dashFrames++ < 16) yield return null;
            Assert.IsFalse(_hero.Mover.IsDashing);
            _opponent.transform.position = _hero.transform.position + Vector3.forward * 1.1f;
            input.TapTarget(_opponent);
            int frames = 0;
            while (_opponent.StunRemaining <= 0f && frames++ < 90) yield return null;
            Assert.Greater(_opponent.StunRemaining, 0f, "真正的普攻命中須使紅方眩暈");
            int attacks = _opponent.AttacksResolved;
            Vector3 pausedAt = _opponent.transform.position;
            for (int i = 0; i < 15; i++) yield return null;
            Assert.AreEqual(attacks, _opponent.AttacksResolved, "眩暈期間不可出刀");
            Assert.Less(Vector3.Distance(pausedAt, _opponent.transform.position), 0.01f, "眩暈期間不可移動");
            while (_opponent.StunRemaining > 0f) yield return null;
            Assert.AreEqual(attacks, _opponent.AttacksResolved, "眩暈解除同幀不可補出前搖攻擊");

            _hero.CancelCombatForDuel(); // Stop further basic attacks while observing the AI windup.
            frames = 0;
            while (!_opponent.IsWarning && frames++ < 180) yield return null;
            Assert.IsTrue(_opponent.IsWarning, "前提：AI 已進入攻擊前搖");
            _opponent.ApplyCaptureStun(0.5f);
            attacks = _opponent.AttacksResolved;
            for (int i = 0; i < 29; i++) yield return null;
            Assert.AreEqual(attacks, _opponent.AttacksResolved, "前搖中眩暈不可出刀");
            Assert.IsTrue(_opponent.IsWarning, "前搖剩餘時間應保留");
            while (_opponent.StunRemaining > 0f) yield return null;
            Assert.AreEqual(attacks, _opponent.AttacksResolved, "眩暈解除當幀不可直接出刀");
            Assert.IsTrue(_opponent.IsWarning, "解除後仍須完成剩餘前搖");

            yield return EndCaptureAndReturnToLobby();
            _opponent.ApplyCaptureStun(0.5f);
            Assert.AreEqual(0f, _opponent.StunRemaining, "非 Active 不得套眩暈");
        }

        [UnityTest]
        public IEnumerator V0110_B2_WindPiercer_RealLandedBasicPiercesEnemyWallBehindDirectTarget()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(500, 0);
            yield return null;
            TapTalentOption(0); // Tier 1 SwiftStep.
            TapTalentOption(0); // Tier 2 WindPiercer.
            ScriptedInput input = new ScriptedInput();
            _hero.Initialize(input, null, Camera.main);
            _opponent.SetHitstopFrozen(true); // Keep the target on the ray during this hit.

            Assert.IsTrue(_hero.Mover.TryExecuteCadenceDash(Vector3.back));
            int frames = 0;
            while (_hero.Mover.IsDashing && frames++ < 16) yield return null;
            Assert.IsFalse(_hero.Mover.IsDashing);
            _opponent.transform.position = _hero.transform.position + Vector3.forward * 1.1f;
            RuneWall wall = _bootstrap.EnemyWalls.SpawnFrom(_hero.transform.position, Vector3.forward);
            Assert.IsNotNull(wall);
            Physics.SyncTransforms();
            float beforeTarget = _opponent.Health;
            float beforeWall = wall.Health;
            input.TapTarget(_opponent);
            frames = 0;
            while (_opponent.Health >= beforeTarget && frames++ < 90) yield return null;
            Assert.Less(_opponent.Health, beforeTarget, "大腦須真的命中直接目標");
            Assert.AreEqual(beforeTarget - 60f, _opponent.Health, 0.01f,
                "直接目標只能受一次原普攻傷害");
            Assert.AreEqual(beforeWall - 60f, wall.Health, 0.01f,
                "目標後方 5m 內的敵牆應吃到一次原普攻傷害");
        }

        [UnityTest]
        public IEnumerator V0110_B2_WindPiercer_DoesNotDamageFriendlyWallOnTheSameRay()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(500, 0);
            yield return null;
            TapTalentOption(0); // Tier 1 SwiftStep.
            TapTalentOption(0); // Tier 2 WindPiercer.
            ScriptedInput input = new ScriptedInput();
            _hero.Initialize(input, null, Camera.main);
            _opponent.SetHitstopFrozen(true);

            Assert.IsTrue(_hero.Mover.TryExecuteCadenceDash(Vector3.back));
            int frames = 0;
            while (_hero.Mover.IsDashing && frames++ < 16) yield return null;
            Assert.IsFalse(_hero.Mover.IsDashing);
            _opponent.transform.position = _hero.transform.position + Vector3.forward * 1.1f;
            GameObject pool = GameObject.Find("RuneWallPool");
            Assert.IsNotNull(pool, "場景須有玩家自己的石牆池");
            RuneWall[] blueWalls = pool.GetComponentsInChildren<RuneWall>(true);
            Assert.Greater(blueWalls.Length, 0);
            RuneWall friendly = blueWalls[0];
            Vector3 wallPosition = _hero.transform.position + Vector3.forward * 4f + Vector3.up;
            friendly.Activate(wallPosition, Quaternion.LookRotation(Vector3.forward), Faction.BlueTeam, null, -1);
            Physics.SyncTransforms();
            float beforeTarget = _opponent.Health;
            float beforeFriendly = friendly.Health;
            input.TapTarget(_opponent);
            frames = 0;
            while (_opponent.Health >= beforeTarget && frames++ < 90) yield return null;
            Assert.AreEqual(beforeTarget - 60f, _opponent.Health, 0.01f,
                "直接目標必須真的命中，否則不能鑑別己方牆過濾");
            Assert.AreEqual(beforeFriendly, friendly.Health, 0.01f,
                "同射線己方石牆不可被裂風矢傷害");
        }

        [UnityTest]
        public IEnumerator V0110_AI_OnlyActiveChaseCastsRedWallFourMetresAheadOfOpponent()
        {
            yield return Setup();
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            for (int i = 0; i < 12; i++) yield return null;
            Assert.AreEqual(0, _bootstrap.EnemyWalls.AliveCount(), "Lobby 不得施放紅牆");
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            _opponent.transform.position = _hero.transform.position + Vector3.forward * 5.5f;
            ScriptedInput combatInput = new ScriptedInput();
            _hero.Initialize(combatInput, null, Camera.main);
            combatInput.TapTarget(_opponent);
            Assert.AreSame(_opponent, _hero.CurrentTarget, "紅方放牆測試須進入真實交戰");
            int frames = 0;
            while (_bootstrap.EnemyWalls.AliveCount() == 0 && frames++ < 90) yield return null;
            Assert.Greater(_bootstrap.EnemyWalls.AliveCount(), 0, "Active 追英雄時應由紅方 AI 施放石牆");
            RuneWall wall = null;
            foreach (RuneWall candidate in _bootstrap.EnemyWalls.Pool)
                if (candidate != null && candidate.IsAlive) { wall = candidate; break; }
            Assert.IsNotNull(wall);
            Assert.AreEqual(Faction.RedTeam, wall.OwnerFaction);
            float fromRed = Vector3.Distance(new Vector3(wall.transform.position.x, 0f, wall.transform.position.z),
                                             new Vector3(_opponent.transform.position.x, 0f, _opponent.transform.position.z));
            Assert.AreEqual(4f, fromRed, 0.2f, "牆應從紅方自身往英雄方向 4m 落下");
        }

        [UnityTest]
        public IEnumerator V0110_AI_ActiveRedWallCanBeBrokenForStoneBody_AndAIResumesCapture()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(250, 0);
            yield return null;
            TapTalentOption(1); // Tier 1 StoneBody.
            _opponent.transform.position = _hero.transform.position + Vector3.forward * 5.5f;
            ScriptedInput combatInput = new ScriptedInput();
            _hero.Initialize(combatInput, null, Camera.main);
            combatInput.TapTarget(_opponent);
            Assert.AreSame(_opponent, _hero.CurrentTarget, "堅磐體測試須先讓雙方交戰");
            int frames = 0;
            while (_bootstrap.EnemyWalls.AliveCount() == 0 && frames++ < 90) yield return null;
            Assert.Greater(_bootstrap.EnemyWalls.AliveCount(), 0, "石牆必須由正常 Active AI 自行施放");
            RuneWall wall = null;
            foreach (RuneWall candidate in _bootstrap.EnemyWalls.Pool)
                if (candidate != null && candidate.IsAlive) { wall = candidate; break; }
            Assert.IsNotNull(wall);
            Assert.AreEqual(Faction.RedTeam, wall.OwnerFaction);

            // While its own wall is alive, the red AI must continue pursuing instead of parking against it.
            float initialDistance = Vector3.Distance(_opponent.transform.position, _hero.transform.position);
            float nearest = initialDistance;
            float maxSideStep = 0f;
            float initialX = _opponent.transform.position.x;
            for (int i = 0; i < 120 && wall.IsAlive; i++)
            {
                yield return null;
                nearest = Mathf.Min(nearest, Vector3.Distance(_opponent.transform.position, _hero.transform.position));
                maxSideStep = Mathf.Max(maxSideStep, Mathf.Abs(_opponent.transform.position.x - initialX));
            }
            Assert.IsTrue(_opponent.IsChasingHero, "紅方立牆後仍須追英雄");
            Assert.Greater(maxSideStep, 0.3f, "紅方應側移繞過自己立下的牆");
            Assert.Less(nearest, initialDistance - 0.5f, "紅方應繼續逼近英雄");
            Assert.IsTrue(wall.IsAlive, "繞牆觀察必須發生在 5 秒牆壽命內");

            _opponent.SetHitstopFrozen(true); // Isolate the melee break from incoming AI attacks.
            _hero.CancelCombatForDuel(); // End the opponent lock before directing the next real melee at the wall.
            ScriptedInput input = new ScriptedInput();
            _hero.Initialize(input, null, Camera.main);
            Assert.Greater(wall.RemainingLifespan, 0.5f, "破牆前仍須有時間供近戰命中");
            float breakStartedAt = Time.time;
            yield return MeleeBreakWall(wall, input, "正常 AI 紅牆");
            Assert.Less(Time.time - breakStartedAt, 0.5f, "必須由近戰擊碎，不能等到 5 秒自然坍塌");
            Assert.AreEqual(220f, _bootstrap.Shield.Amount, 0.01f,
                "選堅磐體後 5 秒內近戰擊碎正常 AI 紅牆應得到 220 護盾");

            _opponent.SetHitstopFrozen(false);
            _hero.CancelCombatForDuel();
            _hero.transform.position = _opponent.transform.position + Vector3.forward * 12f;
            Physics.SyncTransforms();
            frames = 0;
            while (_opponent.CaptureTargetTile < 0 && frames++ < 120) yield return null;
            Assert.GreaterOrEqual(_opponent.CaptureTargetTile, 0,
                "英雄離開追擊距離後紅方應恢復尋找佔領板塊");
        }

        private void CastThreeElementButtons()
        {
            _hero.transform.rotation = Quaternion.identity;
            _bootstrap.PressElementWaterButton();
            _hero.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            _bootstrap.PressElementFireButton();
            _hero.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _bootstrap.PressElementWindButton();
        }

        private ElementZone LatestWaterZone()
        {
            ElementZone latest = default;
            for (int slot = 0; slot < _bootstrap.ElementField.ZoneCapacity; slot++)
                if (_bootstrap.ElementField.TryGetZoneBySlot(slot, out ElementZone zone)
                    && zone.Kind == ElementZoneKind.Water && zone.Id > latest.Id)
                    latest = zone;
            Assert.Greater(latest.Id, 0, "場上應有水域");
            return latest;
        }

        private IEnumerator DashAndWait()
        {
            Assert.IsTrue(_hero.Mover.TryExecuteCadenceDash(Vector3.right), "微滑步應成功起手");
            int frames = 0;
            while (_hero.Mover.IsDashing && frames < 16)
            {
                yield return null;
                frames++;
            }
            Assert.IsFalse(_hero.Mover.IsDashing, "16 幀內應完成滑步");
        }

        private IEnumerator MeleeBreakWall(RuneWall wall, ScriptedInput input, string who)
        {
            Assert.IsTrue(wall.IsAlive, who + " 應先存活");
            wall.ReceiveDamage(wall.Health - 1f, DamageType.Physical, null);
            Assert.AreEqual(1f, wall.Health, 0.01f, who + " 的前置血量應留到真實近戰最後一擊");
            input.TapTarget(wall);
            Assert.AreSame(wall, _hero.CurrentTarget, who + " 應由大腦鎖定，而非直接結算傷害");
            int frames = 0;
            while (wall.IsAlive && frames < 180)
            {
                yield return null;
                frames++;
            }
            Assert.IsFalse(wall.IsAlive, who + " 應由英雄實際近戰擊碎，不能等 5 秒自然到期");
            Assert.Less(frames, 180, who + " 不得靠壽命到期假綠");
        }

        private IEnumerator EndCaptureAndReturnToLobby()
        {
            _bootstrap.SeedCaptureScoresForTest(999, 0);
            int frames = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && frames < 186)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "應由 999 分種子結算對局");
            frames = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && frames < 186)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "結算後應回 Lobby");
            for (int i = 0; i < 30; i++) yield return null; // 相機追上回出生點的英雄，再點紅方開下一局。
        }

        private void AssertTalentChoices(int tier, string title, string first, string second, string third)
        {
            Assert.IsTrue(_bootstrap.TalentPanelVisible, "Tier " + tier + " 應顯示三選盤");
            Assert.AreEqual(tier, _bootstrap.TalentPendingTier);
            Assert.AreEqual(title, _bootstrap.TalentTitleLabel);
            Assert.AreEqual(first, _bootstrap.TalentOptionLabel(0));
            Assert.AreEqual(second, _bootstrap.TalentOptionLabel(1));
            Assert.AreEqual(third, _bootstrap.TalentOptionLabel(2));
        }

        private void TapTalentOption(int choice)
        {
            Assert.IsTrue(_bootstrap.TryGetTalentButtonScreenPoint(choice, out float x, out float y),
                "拿不到天賦選項 " + choice + " 的觸控點");
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

        private static DebugHudLayout CurrentHudLayout(bool captureModeActive = false)
        {
            return DebugHudLayout.Compute(Screen.width, Screen.height, Screen.dpi,
                                          true, true, true, captureModeActive, captureModeActive);
        }

        private static void AssertInsideViewport(HudRect rect, float scale, float width, float height, string who)
        {
            Assert.GreaterOrEqual(rect.XMin * scale, 0f, who + " 左緣在畫面外");
            Assert.GreaterOrEqual(rect.YMin * scale, 0f, who + " 上緣在畫面外");
            Assert.LessOrEqual(rect.XMax * scale, width, who + " 右緣在畫面外");
            Assert.LessOrEqual(rect.YMax * scale, height, who + " 下緣在畫面外");
        }

        private IEnumerator TapHudRect(DebugHudLayout layout, HudRect rect, string who)
        {
            float x = (rect.XMin + rect.XMax) * 0.5f * layout.Scale;
            float y = Screen.height - (rect.YMin + rect.YMax) * 0.5f * layout.Scale;
            Assert.IsTrue(x >= EdgeMarginPixels && x <= Screen.width - EdgeMarginPixels
                          && y >= EdgeMarginPixels && y <= Screen.height - EdgeMarginPixels,
                who + " 鈕中心無法觸碰（" + x + ", " + y + "，畫面 " + Screen.width + "x" + Screen.height + "）");
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
            yield return null;
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
            _bootstrap.SeedCaptureScoresForTest(999, 0); // v0.10.0 §2.6 T6：(998, 0) → (999, 0)（慢計分）
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && f < 372) // v0.10.0 §2.6 T6：窗口 300 → 372 幀
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
