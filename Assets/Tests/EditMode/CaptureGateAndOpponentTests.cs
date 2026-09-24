using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V080_CAPTURE_PLAN.md §3-A：V-A20（MatchGate 真值表）、V-A21（對手選目標）、V-A22（追打與放棄）。凍結。
    public sealed class CaptureGateAndOpponentTests
    {
        private static void AssertDecision(MatchGateDecision d, bool locked, bool blocked,
                                            CaptureTapAction tap, CaptureButtonAction button, string label)
        {
            Assert.AreEqual(locked, d.ElementsLocked, label + "：元素鎖住");
            Assert.AreEqual(blocked, d.HeroInputBlocked, label + "：英雄輸入封鎖");
            Assert.AreEqual(tap, d.TapAction, label + "：點對手動作");
            Assert.AreEqual(button, d.CaptureButton, label + "：CAPTURE鈕動作");
        }

        [Test]
        public void VA20_MatchGate_TruthTable()
        {
            AssertDecision(MatchGate.Evaluate(DuelRoundState.Dormant, CaptureMatchState.Off, true),
                false, false, CaptureTapAction.OpenDuel, CaptureButtonAction.EnterCaptureMode, "單挑待機+Off");

            AssertDecision(MatchGate.Evaluate(DuelRoundState.Active, CaptureMatchState.Off, true),
                true, false, CaptureTapAction.None, CaptureButtonAction.Invalid, "單挑Active+Off");

            AssertDecision(MatchGate.Evaluate(DuelRoundState.KnockoutPause, CaptureMatchState.Off, true),
                true, true, CaptureTapAction.None, CaptureButtonAction.Invalid, "單挑KO+Off");

            AssertDecision(MatchGate.Evaluate(DuelRoundState.Dormant, CaptureMatchState.Lobby, true),
                false, false, CaptureTapAction.OpenCapture, CaptureButtonAction.ExitCaptureMode, "Lobby");

            AssertDecision(MatchGate.Evaluate(DuelRoundState.Dormant, CaptureMatchState.Active, true),
                true, false, CaptureTapAction.None, CaptureButtonAction.Invalid, "佔領Active+英雄活著");

            AssertDecision(MatchGate.Evaluate(DuelRoundState.Dormant, CaptureMatchState.Active, false),
                true, true, CaptureTapAction.None, CaptureButtonAction.Invalid, "佔領Active+英雄倒地");

            AssertDecision(MatchGate.Evaluate(DuelRoundState.Dormant, CaptureMatchState.Ended, true),
                true, true, CaptureTapAction.None, CaptureButtonAction.Invalid, "Ended");
        }

        private static int[] DefaultOwnership()
        {
            var o = new int[HexBoardLayout.TileCount];
            for (int i = 0; i < o.Length; i++) o[i] = CaptureMatchLogic.NeutralFactionId;
            o[HexBoardLayout.RedBaseTile] = CaptureMatchLogic.RedFactionId;
            o[HexBoardLayout.BlueBaseTile] = CaptureMatchLogic.BlueFactionId;
            return o;
        }

        [Test]
        public void VA21_SelectTargetTile_NearestNonRedTile_SmallestIndexOnExactTie()
        {
            var ownership = DefaultOwnership();
            Assert.AreEqual(2, CaptureOpponentPolicy.SelectTargetTile(0f, 13.625f, ownership));

            ownership[2] = CaptureMatchLogic.RedFactionId;
            Assert.AreEqual(6, CaptureOpponentPolicy.SelectTargetTile(0f, 13.625f, ownership));

            var ownership2 = DefaultOwnership();
            ownership2[1] = CaptureMatchLogic.BlueFactionId;
            ownership2[2] = CaptureMatchLogic.RedFactionId;
            Assert.AreEqual(0, CaptureOpponentPolicy.SelectTargetTile(8f, 6.0625f, ownership2));

            var allRed = new int[HexBoardLayout.TileCount];
            for (int i = 0; i < allRed.Length; i++) allRed[i] = CaptureMatchLogic.RedFactionId;
            Assert.AreEqual(-1, CaptureOpponentPolicy.SelectTargetTile(0f, 0f, allRed));
        }

        [Test]
        public void VA22_ChaseStartAndGiveUp_HaveHysteresis_AndNeverChaseAKnockedOutHero()
        {
            var ownership = DefaultOwnership();
            var tuning = new CaptureTuning();

            // 沒在追時：起追門檻 6.0（含邊界）。
            Assert.IsTrue(CaptureOpponentPolicy.Decide(0f, 0f, 6.0f, 0f, false, false, ownership, tuning).ChaseHero);
            Assert.IsFalse(CaptureOpponentPolicy.Decide(0f, 0f, 6.0625f, 0f, false, false, ownership, tuning).ChaseHero);
            Assert.IsFalse(CaptureOpponentPolicy.Decide(0f, 0f, 8f, 0f, false, false, ownership, tuning).ChaseHero);

            // 已經在追時：放棄門檻 10.0（含邊界）。
            Assert.IsTrue(CaptureOpponentPolicy.Decide(0f, 0f, 8f, 0f, false, true, ownership, tuning).ChaseHero);
            Assert.IsTrue(CaptureOpponentPolicy.Decide(0f, 0f, 10.0f, 0f, false, true, ownership, tuning).ChaseHero);
            var gaveUp = CaptureOpponentPolicy.Decide(0f, 0f, 10.0625f, 0f, false, true, ownership, tuning);
            Assert.IsFalse(gaveUp.ChaseHero);
            Assert.GreaterOrEqual(gaveUp.TargetTile, 0);

            // 英雄倒地：不論原本有沒有在追，都不追。
            Assert.IsFalse(CaptureOpponentPolicy.Decide(0f, 0f, 1f, 0f, true, false, ownership, tuning).ChaseHero);
            Assert.IsFalse(CaptureOpponentPolicy.Decide(0f, 0f, 1f, 0f, true, true, ownership, tuning).ChaseHero);
        }
    }
}
