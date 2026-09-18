using System;
using UnityEngine;
using Vow.Core;

namespace Vow.Tests.PlayMode
{
    // 以腳本化輸入取代真實觸控：驗的是輸入之後的整條鏈路，觸控辨識本身另有 TouchGestureRouter 的測試與瀏覽器實機驗收。
    // 實作 IRuneCastInput：讓 RuneCaster／RuneGhostPreview 在 PlayMode 測試裡可以繞過真實觸控，
    // 直接呼叫 RuneQuickCast／RuneDrag／RuneRelease／RuneCancel 驅動符印事件。
    internal sealed class ScriptedInput : IPlayerInputService, IRuneCastInput
    {
        public ControlMode ActiveMode { get; set; }
        public event Action<Vector3> OnMoveDestinationSelected;
        public event Action<ICombatTarget> OnCombatTargetSelected;
        public event Action<Vector2> OnCadenceVectorFlicked;
        public event Action<Vector2, float> OnRuneVectorDragUpdated;
        public event Action OnRuneQuickCastTriggered;
        public event Action OnRuneCastCancelled;
        public event Action<Vector2, float> OnRuneCastReleased;

        public void TapGround(Vector3 point) { OnMoveDestinationSelected?.Invoke(point); }
        public void TapTarget(ICombatTarget target) { OnCombatTargetSelected?.Invoke(target); }
        public void Flick(Vector2 screenDirection) { OnCadenceVectorFlicked?.Invoke(screenDirection); }

        public void RuneQuickCast() { OnRuneQuickCastTriggered?.Invoke(); }
        public void RuneDrag(Vector2 screenDirection, float distance01) { OnRuneVectorDragUpdated?.Invoke(screenDirection, distance01); }
        public void RuneRelease(Vector2 screenDirection, float distance01) { OnRuneCastReleased?.Invoke(screenDirection, distance01); }
        public void RuneCancel() { OnRuneCastCancelled?.Invoke(); }
    }

    // 測試用震覺替身：只記帳。
    internal sealed class RecordingHaptics : IHapticService
    {
        public int BasicHits;
        public int Dashes;
        public int WallBreaks;

        public void Notify(Vow.Core.Logic.HapticCue cue)
        {
            if (cue == Vow.Core.Logic.HapticCue.BasicAttackHit) BasicHits++;
            else if (cue == Vow.Core.Logic.HapticCue.CadenceDash) Dashes++;
            else WallBreaks++;
        }
    }
}
