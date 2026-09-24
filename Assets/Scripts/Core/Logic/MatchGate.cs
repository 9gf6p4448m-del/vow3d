namespace Vow.Core.Logic
{
    public enum CaptureTapAction { None, OpenDuel, OpenCapture }

    public enum CaptureButtonAction { Invalid, EnterCaptureMode, ExitCaptureMode }

    public struct MatchGateDecision
    {
        public bool ElementsLocked;
        public bool HeroInputBlocked;
        public CaptureTapAction TapAction;
        public CaptureButtonAction CaptureButton;

        public MatchGateDecision(bool elementsLocked, bool heroInputBlocked,
                                  CaptureTapAction tapAction, CaptureButtonAction captureButton)
        {
            ElementsLocked = elementsLocked;
            HeroInputBlocked = heroInputBlocked;
            TapAction = tapAction;
            CaptureButton = captureButton;
        }
    }

    // 把單挑狀態、佔領狀態與英雄是否倒地合成四個判斷（V080_CAPTURE_PLAN.md §2.1-1、V-A20，2026-09-24 凍結）：
    // 元素/砲台/敵牆是否鎖住、英雄輸入是否封鎖、點對手時要開哪一種局、CAPTURE 鈕該做什麼。
    public static class MatchGate
    {
        public static MatchGateDecision Evaluate(DuelRoundState duelState, CaptureMatchState captureState, bool heroAlive)
        {
            if (captureState == CaptureMatchState.Lobby)
            {
                return new MatchGateDecision(false, false, CaptureTapAction.OpenCapture, CaptureButtonAction.ExitCaptureMode);
            }
            if (captureState == CaptureMatchState.Active)
            {
                return new MatchGateDecision(true, !heroAlive, CaptureTapAction.None, CaptureButtonAction.Invalid);
            }
            if (captureState == CaptureMatchState.Ended)
            {
                return new MatchGateDecision(true, true, CaptureTapAction.None, CaptureButtonAction.Invalid);
            }

            // captureState == Off：沿用 v0.7.0 單挑狀態的真值表（R1～R3 原樣）。
            switch (duelState)
            {
                case DuelRoundState.Dormant:
                    return new MatchGateDecision(false, false, CaptureTapAction.OpenDuel, CaptureButtonAction.EnterCaptureMode);
                case DuelRoundState.Active:
                    return new MatchGateDecision(true, false, CaptureTapAction.None, CaptureButtonAction.Invalid);
                default: // KnockoutPause
                    return new MatchGateDecision(true, true, CaptureTapAction.None, CaptureButtonAction.Invalid);
            }
        }
    }
}
