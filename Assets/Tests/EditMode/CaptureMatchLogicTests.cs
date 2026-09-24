using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V080_CAPTURE_PLAN.md §3-A：V-A05～V-A19（模式生命週期、引導/爭奪/翻塊、計分、倒地/復活、結算）。
    // 全部凍結，期望值一律寫死字面值；dt 一律 0.25（必要時 0.125），都是二進位有限小數。
    public sealed class CaptureMatchLogicTests
    {
        private const float Away = 1000f; // 明確不在任何板塊/光圈內的座標

        private static CaptureMatchLogic NewActiveMatch()
        {
            var logic = new CaptureMatchLogic(new CaptureTuning());
            Assert.IsTrue(logic.TryEnterCaptureMode());
            Assert.IsTrue(logic.TryStart());
            return logic;
        }

        [Test]
        public void VA05_ModeLifecycle_AndKickoffOwnership()
        {
            var logic = new CaptureMatchLogic(new CaptureTuning());
            Assert.IsFalse(logic.TryStart());
            Assert.IsTrue(logic.TryEnterCaptureMode());
            Assert.AreEqual(CaptureMatchState.Lobby, logic.State);
            Assert.IsTrue(logic.TryStart());
            Assert.AreEqual(CaptureMatchState.Active, logic.State);
            Assert.AreEqual(1, logic.StartCount);
            Assert.AreEqual(2, logic.OwnerOf(0));
            Assert.AreEqual(1, logic.OwnerOf(1));
            Assert.AreEqual(2, logic.OwnerOf(2));
            Assert.AreEqual(2, logic.OwnerOf(3));
            Assert.AreEqual(0, logic.OwnerOf(4));
            Assert.AreEqual(2, logic.OwnerOf(5));
            Assert.AreEqual(2, logic.OwnerOf(6));
            Assert.AreEqual(0, logic.BlueScore);
            Assert.AreEqual(0, logic.RedScore);
            Assert.IsFalse(logic.TryStart());
            Assert.IsFalse(logic.TryExitCaptureMode());
        }

        [Test]
        public void VA06_CaptureTakesExactlyFourteenQuarterTicks_OrTwentyEightEighthTicks()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 13; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, logic.OwnerOf(0));
            Assert.AreEqual(3.25f, logic.BlueChannelProgress, 1e-5f);
            logic.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(0, logic.OwnerOf(0));

            var logic2 = NewActiveMatch();
            for (int tick = 1; tick <= 27; tick++) logic2.Tick(0.125f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, logic2.OwnerOf(0));
            logic2.Tick(0.125f, 0f, 0f, Away, Away);
            Assert.AreEqual(0, logic2.OwnerOf(0));
        }

        [Test]
        public void VA07_LeavingTheCircle_ZeroesProgress()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 10; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            logic.Tick(0.25f, Away, Away, Away, Away); // 第11個tick：圈外
            for (int tick = 12; tick <= 24; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, logic.OwnerOf(0));
            logic.Tick(0.25f, 0f, 0f, Away, Away); // 第25個tick
            Assert.AreEqual(0, logic.OwnerOf(0));
        }

        [Test]
        public void VA08_DamageInterruptsOwnSideOnly_AndIsSafeOutsideTheCircle()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 10; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            logic.NotifyDamaged(CaptureMatchLogic.BlueFactionId);
            for (int tick = 11; tick <= 23; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, logic.OwnerOf(0));
            logic.Tick(0.25f, 0f, 0f, Away, Away); // 第24個tick
            Assert.AreEqual(0, logic.OwnerOf(0));
            Assert.AreEqual(1, logic.InterruptCount);

            // 對照組：受傷的是紅方，藍方不受影響，照樣在第14個tick翻。
            var control = NewActiveMatch();
            for (int tick = 1; tick <= 10; tick++) control.Tick(0.25f, 0f, 0f, Away, Away);
            control.NotifyDamaged(CaptureMatchLogic.RedFactionId);
            for (int tick = 11; tick <= 13; tick++) control.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, control.OwnerOf(0));
            control.Tick(0.25f, 0f, 0f, Away, Away); // 第14個tick
            Assert.AreEqual(0, control.OwnerOf(0));

            // 在圈外受傷不影響之後的引導、也不丟例外。
            var outside = NewActiveMatch();
            outside.NotifyDamaged(CaptureMatchLogic.BlueFactionId);
            for (int tick = 1; tick <= 13; tick++) outside.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, outside.OwnerOf(0));
            outside.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(0, outside.OwnerOf(0));
        }

        [Test]
        public void VA09_ContestFreezesProgress_IncludingWhenTheOwnerIsDefending()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 8; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2.0f, logic.BlueChannelProgress, 1e-5f);
            for (int tick = 9; tick <= 12; tick++) logic.Tick(0.25f, 0f, 0f, 0f, 0f); // 雙方同在 0 號圈內
            Assert.AreEqual(2.0f, logic.BlueChannelProgress, 1e-5f);
            Assert.AreEqual(0f, logic.RedChannelProgress, 1e-5f);
            for (int tick = 13; tick <= 17; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away); // 對手離開
            Assert.AreEqual(2, logic.OwnerOf(0));
            logic.Tick(0.25f, 0f, 0f, Away, Away); // 第18個tick
            Assert.AreEqual(0, logic.OwnerOf(0));
            Assert.AreEqual(4, logic.ContestTickCount);

            // 守塔對照：擁有者站在自己的塔上也算爭奪。
            var guard = NewActiveMatch();
            float hx = HexBoardLayout.CenterX(HexBoardLayout.BlueBaseTile);
            float hz = HexBoardLayout.CenterZ(HexBoardLayout.BlueBaseTile);
            for (int tick = 1; tick <= 20; tick++) guard.Tick(0.25f, hx, hz, hx, hz);
            Assert.AreEqual(0f, guard.RedChannelProgress, 1e-5f);
            Assert.AreEqual(0, guard.OwnerOf(HexBoardLayout.BlueBaseTile));
            for (int tick = 21; tick <= 33; tick++) guard.Tick(0.25f, Away, Away, hx, hz);
            Assert.AreEqual(0, guard.OwnerOf(HexBoardLayout.BlueBaseTile));
            guard.Tick(0.25f, Away, Away, hx, hz); // 第34個tick
            Assert.AreEqual(1, guard.OwnerOf(HexBoardLayout.BlueBaseTile));
        }

        [Test]
        public void VA10_CapturingTheEnemysTile_FlipsDirectly_NeverThroughNeutral()
        {
            var logic = NewActiveMatch();
            float tx = HexBoardLayout.CenterX(HexBoardLayout.RedBaseTile);
            float tz = HexBoardLayout.CenterZ(HexBoardLayout.RedBaseTile);
            for (int tick = 1; tick <= 13; tick++)
            {
                logic.Tick(0.25f, tx, tz, Away, Away);
                Assert.AreEqual(1, logic.OwnerOf(HexBoardLayout.RedBaseTile), "tick " + tick);
            }
            logic.Tick(0.25f, tx, tz, Away, Away); // 第14個tick
            Assert.AreEqual(0, logic.OwnerOf(HexBoardLayout.RedBaseTile));
        }

        [Test]
        public void VA11_TheKnockedOutSide_DoesNotCountAsPresent()
        {
            var logic = NewActiveMatch();
            logic.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
            float bx = HexBoardLayout.CenterX(HexBoardLayout.BlueBaseTile);
            float bz = HexBoardLayout.CenterZ(HexBoardLayout.BlueBaseTile);
            for (int tick = 1; tick <= 13; tick++) logic.Tick(0.25f, bx, bz, bx, bz);
            Assert.AreEqual(0, logic.OwnerOf(HexBoardLayout.BlueBaseTile));
            logic.Tick(0.25f, bx, bz, bx, bz); // 第14個tick
            Assert.AreEqual(1, logic.OwnerOf(HexBoardLayout.BlueBaseTile));

            var logic2 = NewActiveMatch();
            for (int tick = 1; tick <= 8; tick++) logic2.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2.0f, logic2.BlueChannelProgress, 1e-5f);
            logic2.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
            for (int tick = 9; tick <= 27; tick++) logic2.Tick(0.25f, 0f, 0f, Away, Away);
            logic2.Tick(0.25f, 0f, 0f, Away, Away); // 第28個tick
            Assert.IsTrue(logic2.TryConsumeBlueRespawn(out float rx, out float rz));
            Assert.AreEqual(0f, rx, 1e-5f);
            Assert.AreEqual(-13.625f, rz, 1e-5f);
            for (int tick = 29; tick <= 41; tick++) logic2.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, logic2.OwnerOf(0));
            logic2.Tick(0.25f, 0f, 0f, Away, Away); // 第42個tick
            Assert.AreEqual(0, logic2.OwnerOf(0));
        }

        [Test]
        public void VA12_ScorePulsesOnceEverySecond_NotContinuously()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 3; tick++) logic.Tick(0.25f, Away, Away, Away, Away);
            Assert.AreEqual(0, logic.BlueScore);
            Assert.AreEqual(0, logic.RedScore);
            logic.Tick(0.25f, Away, Away, Away, Away); // 第4個tick
            Assert.AreEqual(2, logic.BlueScore);
            Assert.AreEqual(2, logic.RedScore);
            for (int tick = 5; tick <= 8; tick++) logic.Tick(0.25f, Away, Away, Away, Away);
            Assert.AreEqual(4, logic.BlueScore);
            Assert.AreEqual(4, logic.RedScore);
            Assert.AreEqual(2, logic.ScoreTickCount);
        }

        [Test]
        public void VA13_FlipHappensBeforeScoring_AndDoesNotResetTheScoreClock()
        {
            var group1 = NewActiveMatch();
            for (int tick = 1; tick <= 2; tick++) group1.Tick(0.25f, Away, Away, Away, Away);
            for (int tick = 3; tick <= 15; tick++) group1.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2, group1.OwnerOf(0));
            group1.Tick(0.25f, 0f, 0f, Away, Away); // 第16個tick：同一tick翻塊+計分
            Assert.AreEqual(0, group1.OwnerOf(0));
            Assert.AreEqual(10, group1.BlueScore);

            var group2 = NewActiveMatch();
            for (int tick = 1; tick <= 14; tick++) group2.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(0, group2.OwnerOf(0));
            group2.Tick(0.25f, 0f, 0f, Away, Away); // 第15個tick
            Assert.AreEqual(6, group2.BlueScore);
            group2.Tick(0.25f, 0f, 0f, Away, Away); // 第16個tick
            Assert.AreEqual(10, group2.BlueScore);
        }

        [Test]
        public void VA14_WinIsDetectedOnTheExactPulseTick_NotClampedAtTheThreshold()
        {
            var w1 = NewActiveMatch();
            for (int tick = 1; tick <= 14; tick++) w1.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(0, w1.OwnerOf(0));
            for (int tick = 15; tick <= 1007; tick++) w1.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(CaptureMatchState.Active, w1.State);
            Assert.AreEqual(998, w1.BlueScore);
            Assert.AreEqual(502, w1.RedScore);
            w1.Tick(0.25f, 0f, 0f, Away, Away); // 第1008個tick
            Assert.AreEqual(CaptureMatchState.Ended, w1.State);
            Assert.AreEqual(CaptureMatchResult.BlueWins, w1.Result);
            Assert.AreEqual(1002, w1.BlueScore);
            Assert.AreEqual(504, w1.RedScore);

            var w2 = NewActiveMatch();
            for (int tick = 1; tick <= 1999; tick++) w2.Tick(0.25f, Away, Away, Away, Away);
            Assert.AreEqual(CaptureMatchState.Active, w2.State);
            Assert.AreEqual(998, w2.BlueScore);
            Assert.AreEqual(998, w2.RedScore);
            w2.Tick(0.25f, Away, Away, Away, Away); // 第2000個tick
            Assert.AreEqual(CaptureMatchState.Ended, w2.State);
            Assert.AreEqual(CaptureMatchResult.Draw, w2.Result);
            Assert.AreEqual(1000, w2.BlueScore);
            Assert.AreEqual(1000, w2.RedScore);
            for (int tick = 1; tick <= 12; tick++) w2.Tick(0.25f, Away, Away, Away, Away);
            Assert.AreEqual(1000, w2.BlueScore);
            Assert.AreEqual(1000, w2.RedScore);
        }

        [Test]
        public void VA15_BothCrossingTheLineOnTheSamePulse_HigherScoreWins()
        {
            var logic = NewActiveMatch();
            float tx = HexBoardLayout.CenterX(2), tz = HexBoardLayout.CenterZ(2);
            for (int tick = 1; tick <= 14; tick++) logic.Tick(0.25f, Away, Away, tx, tz);
            Assert.AreEqual(1, logic.OwnerOf(2));
            logic.SeedScoresForTest(998, 998);
            logic.Tick(0.25f, Away, Away, Away, Away); // 第15個tick
            Assert.AreEqual(CaptureMatchState.Active, logic.State);
            logic.Tick(0.25f, Away, Away, Away, Away); // 第16個tick
            Assert.AreEqual(CaptureMatchState.Ended, logic.State);
            Assert.AreEqual(1000, logic.BlueScore);
            Assert.AreEqual(1002, logic.RedScore);
            Assert.AreEqual(CaptureMatchResult.RedWins, logic.Result);
        }

        [Test]
        public void VA16_RespawnFiresOnTheExactTick_AndRepeatedNotifyDoesNotRestartIt()
        {
            var logic = NewActiveMatch();
            logic.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
            for (int tick = 1; tick <= 3; tick++) logic.Tick(0.25f, Away, Away, Away, Away);
            logic.Tick(0.25f, Away, Away, Away, Away); // 第4個tick
            Assert.AreEqual(4.0f, logic.BlueRespawnRemaining, 1e-5f);
            for (int tick = 5; tick <= 9; tick++) logic.Tick(0.25f, Away, Away, Away, Away);
            logic.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId); // 已倒地時再叫一次，不重啟倒數
            for (int tick = 10; tick <= 19; tick++)
            {
                logic.Tick(0.25f, Away, Away, Away, Away);
                Assert.IsFalse(logic.TryConsumeBlueRespawn(out _, out _), "tick " + tick);
            }
            logic.Tick(0.25f, Away, Away, Away, Away); // 第20個tick
            Assert.IsTrue(logic.TryConsumeBlueRespawn(out float x, out float z));
            Assert.AreEqual(0f, x, 1e-5f);
            Assert.AreEqual(-13.625f, z, 1e-5f);
            logic.Tick(0.25f, Away, Away, Away, Away); // 第21個tick
            Assert.IsFalse(logic.TryConsumeBlueRespawn(out _, out _));

            var red = NewActiveMatch();
            red.NotifyKnockedOut(CaptureMatchLogic.RedFactionId);
            for (int tick = 1; tick <= 19; tick++) red.Tick(0.25f, Away, Away, Away, Away);
            red.Tick(0.25f, Away, Away, Away, Away); // 第20個tick
            Assert.IsTrue(red.TryConsumeRedRespawn(out float rx, out float rz));
            Assert.AreEqual(0f, rx, 1e-5f);
            Assert.AreEqual(13.625f, rz, 1e-5f);
        }

        [Test]
        public void VA17_RespawnLocation_HomeUnlessTheBaseWasCaptured()
        {
            float bx = HexBoardLayout.CenterX(HexBoardLayout.BlueBaseTile);
            float bz = HexBoardLayout.CenterZ(HexBoardLayout.BlueBaseTile);

            // (a) 對手第 8~19 個 tick 在基地圈內：respawn 那個 tick 基地還沒被翻，回家；
            //     之後英雄站進自家光圈，紅方進度就此凍結在 3.25。
            var a = NewActiveMatch();
            a.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
            for (int tick = 1; tick <= 7; tick++) a.Tick(0.25f, bx, bz, Away, Away);
            for (int tick = 8; tick <= 19; tick++) a.Tick(0.25f, bx, bz, bx, bz);
            a.Tick(0.25f, bx, bz, bx, bz); // 第20個tick
            Assert.IsTrue(a.TryConsumeBlueRespawn(out float ax, out float az));
            Assert.AreEqual(0f, ax, 1e-5f);
            Assert.AreEqual(-13.625f, az, 1e-5f);
            for (int tick = 21; tick <= 30; tick++) a.Tick(0.25f, ax, az, bx, bz);
            Assert.AreEqual(0, a.OwnerOf(HexBoardLayout.BlueBaseTile));
            Assert.AreEqual(3.25f, a.RedChannelProgress, 1e-5f);

            // (b) 對手第 1~14 個 tick 在基地圈內：早就翻紅，respawn 到場邊。
            var b = NewActiveMatch();
            b.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
            for (int tick = 1; tick <= 13; tick++) b.Tick(0.25f, bx, bz, bx, bz);
            Assert.AreEqual(0, b.OwnerOf(HexBoardLayout.BlueBaseTile));
            b.Tick(0.25f, bx, bz, bx, bz); // 第14個tick
            Assert.AreEqual(1, b.OwnerOf(HexBoardLayout.BlueBaseTile));
            for (int tick = 15; tick <= 19; tick++) b.Tick(0.25f, bx, bz, bx, bz);
            b.Tick(0.25f, bx, bz, bx, bz); // 第20個tick
            Assert.IsTrue(b.TryConsumeBlueRespawn(out float bx2, out float bz2));
            Assert.AreEqual(-17f, bx2, 1e-5f);
            Assert.AreEqual(-17f, bz2, 1e-5f);

            // (c) 對手第 7~19 個 tick 在基地圈內：第20個tick同時翻紅與到期，先翻塊再判定，回場邊。
            var c = NewActiveMatch();
            c.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
            for (int tick = 1; tick <= 6; tick++) c.Tick(0.25f, bx, bz, Away, Away);
            for (int tick = 7; tick <= 19; tick++) c.Tick(0.25f, bx, bz, bx, bz);
            Assert.AreEqual(0, c.OwnerOf(HexBoardLayout.BlueBaseTile));
            c.Tick(0.25f, bx, bz, bx, bz); // 第20個tick：同時翻紅與到期
            Assert.AreEqual(1, c.OwnerOf(HexBoardLayout.BlueBaseTile));
            Assert.IsTrue(c.TryConsumeBlueRespawn(out float cx, out float cz));
            Assert.AreEqual(-17f, cx, 1e-5f);
            Assert.AreEqual(-17f, cz, 1e-5f);

            // (d) 紅方對照：英雄翻下紅方基地，對手 respawn 到場邊。
            var d = NewActiveMatch();
            d.NotifyKnockedOut(CaptureMatchLogic.RedFactionId);
            float rx = HexBoardLayout.CenterX(HexBoardLayout.RedBaseTile);
            float rz = HexBoardLayout.CenterZ(HexBoardLayout.RedBaseTile);
            for (int tick = 1; tick <= 13; tick++) d.Tick(0.25f, rx, rz, rx, rz);
            Assert.AreEqual(1, d.OwnerOf(HexBoardLayout.RedBaseTile));
            d.Tick(0.25f, rx, rz, rx, rz); // 第14個tick
            Assert.AreEqual(0, d.OwnerOf(HexBoardLayout.RedBaseTile));
            for (int tick = 15; tick <= 19; tick++) d.Tick(0.25f, rx, rz, rx, rz);
            d.Tick(0.25f, rx, rz, rx, rz); // 第20個tick
            Assert.IsTrue(d.TryConsumeRedRespawn(out float dx, out float dz));
            Assert.AreEqual(17f, dx, 1e-5f);
            Assert.AreEqual(17f, dz, 1e-5f);
        }

        [Test]
        public void VA18_OwnershipAndScore_SurviveThroughAKnockout()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 40; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away); // 10.0 秒
            Assert.AreEqual(0, logic.OwnerOf(0));
            logic.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
            for (int tick = 41; tick <= 59; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            logic.Tick(0.25f, 0f, 0f, Away, Away); // 第60個tick(15.0秒)：同一tick也取到復活事件
            Assert.AreEqual(54, logic.BlueScore);
            Assert.AreEqual(30, logic.RedScore);
            Assert.AreEqual(0, logic.OwnerOf(0));
            Assert.AreEqual(0, logic.OwnerOf(HexBoardLayout.BlueBaseTile));
            Assert.AreEqual(1, logic.OwnerOf(HexBoardLayout.RedBaseTile));
            for (int i = 0; i < HexBoardLayout.TileCount; i++)
            {
                if (i == 0 || i == HexBoardLayout.BlueBaseTile || i == HexBoardLayout.RedBaseTile) continue;
                Assert.AreEqual(2, logic.OwnerOf(i), "tile " + i + " 應維持中立");
            }
            Assert.IsTrue(logic.TryConsumeBlueRespawn(out _, out _));
        }

        [Test]
        public void VA19_EndPauseFreezesEverything_ThenReturnsToLobbyWithResultsKept()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 1008; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(CaptureMatchState.Ended, logic.State);
            Assert.AreEqual(1002, logic.BlueScore);
            Assert.AreEqual(504, logic.RedScore);

            float fx = HexBoardLayout.CenterX(5), fz = HexBoardLayout.CenterZ(5);
            for (int tick = 1009; tick <= 1019; tick++)
            {
                logic.Tick(0.25f, fx, fz, Away, Away); // 停頓期間英雄傳入 5 號圈內
                Assert.AreEqual(2, logic.OwnerOf(5), "tick " + tick);
            }
            Assert.AreEqual(1002, logic.BlueScore);
            Assert.AreEqual(504, logic.RedScore);
            Assert.AreEqual(CaptureMatchState.Ended, logic.State);
            Assert.IsFalse(logic.TryStart());

            logic.Tick(0.25f, fx, fz, Away, Away); // 第1020個tick
            Assert.AreEqual(CaptureMatchState.Lobby, logic.State);
            Assert.IsTrue(logic.TryConsumeJustReturnedToLobby());
            Assert.AreEqual(2, logic.OwnerOf(5));
            Assert.AreEqual(1002, logic.BlueScore);
            Assert.AreEqual(504, logic.RedScore);
            Assert.AreEqual(CaptureMatchResult.BlueWins, logic.LastResult);

            Assert.IsTrue(logic.TryStart());
            Assert.AreEqual(2, logic.OwnerOf(0));
            Assert.AreEqual(1, logic.OwnerOf(HexBoardLayout.RedBaseTile));
            Assert.AreEqual(0, logic.OwnerOf(HexBoardLayout.BlueBaseTile));
            Assert.AreEqual(0, logic.BlueScore);
            Assert.AreEqual(0, logic.RedScore);
        }

        // 主對話 2026-09-24 審稿裁定 1（加嚴）：己方塊不引導——單獨站在自己已擁有的塔圈內不算引導，
        // 不會每 CaptureSeconds 對自己的塊空轉一次翻塊。對照組證明離開自家塔、改去中立塊仍能正常引導。
        [Test]
        public void ReviewFix1_StandingAloneOnYourOwnBase_DoesNotChannel_ThenCapturingElsewhereStillWorks()
        {
            var logic = NewActiveMatch();
            float bx = HexBoardLayout.CenterX(HexBoardLayout.BlueBaseTile);
            float bz = HexBoardLayout.CenterZ(HexBoardLayout.BlueBaseTile);
            for (int tick = 1; tick <= 20; tick++)
            {
                logic.Tick(0.25f, bx, bz, 17f, 17f);
                Assert.AreEqual(-1, logic.BlueChannelingTile, "tick " + tick);
                Assert.AreEqual(0f, logic.BlueChannelProgress, 1e-5f, "tick " + tick);
            }
            Assert.AreEqual(0, logic.FlipCount);

            for (int tick = 21; tick <= 33; tick++) logic.Tick(0.25f, 0f, 0f, 17f, 17f);
            Assert.AreEqual(2, logic.OwnerOf(0));
            logic.Tick(0.25f, 0f, 0f, 17f, 17f); // 第34個tick
            Assert.AreEqual(0, logic.OwnerOf(0));
            Assert.AreEqual(1, logic.FlipCount);
        }

        // 主對話 2026-09-24 審稿裁定 2（修 bug）：爭奪只凍結「同一個圈」的進度；憑空出現在對方所在圈的
        // 殘留進度（例如傳送）要視同離圈先歸零，不得帶進這次爭奪。若保留殘留進度會在第 15 個 tick 翻
        // （2.0 + 0.25*6）；修好後第 10 個 tick 重新從 0 開始算 14 tick，第 23 個 tick 才翻。
        [Test]
        public void ReviewFix2_TeleportingIntoAContestedCircle_DoesNotCarryOverStaleProgress()
        {
            var logic = NewActiveMatch();
            for (int tick = 1; tick <= 8; tick++) logic.Tick(0.25f, 0f, 0f, Away, Away);
            Assert.AreEqual(2.0f, logic.BlueChannelProgress, 1e-5f);

            float tx = HexBoardLayout.CenterX(2), tz = HexBoardLayout.CenterZ(2);
            logic.Tick(0.25f, tx, tz, tx, tz); // 第9個tick：英雄直接傳到2號塔心，對手也在2號圈內（爭奪）
            Assert.AreEqual(0f, logic.BlueChannelProgress, 1e-5f, "傳送到別的圈時，2號塔上不該帶著0號的殘留進度");

            for (int tick = 10; tick <= 22; tick++) logic.Tick(0.25f, 0f, 0f, 17f, 17f);
            Assert.AreEqual(2, logic.OwnerOf(0));
            logic.Tick(0.25f, 0f, 0f, 17f, 17f); // 第23個tick
            Assert.AreEqual(0, logic.OwnerOf(0));
        }
    }
}
