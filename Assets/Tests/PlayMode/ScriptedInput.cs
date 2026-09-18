using System;
using UnityEngine;
using Vow.Core;

namespace Vow.Tests.PlayMode
{
    // 以腳本化輸入取代真實觸控：驗的是輸入之後的整條鏈路，觸控辨識本身另有 TouchGestureRouter 的測試與瀏覽器實機驗收。
    internal sealed class ScriptedInput : IPlayerInputService
    {
        public ControlMode ActiveMode { get; set; }
        public event Action<Vector3> OnMoveDestinationSelected;
        public event Action<ICombatTarget> OnCombatTargetSelected;
        public event Action<Vector2> OnCadenceVectorFlicked;
        public event Action<Vector2, float> OnRuneVectorDragUpdated { add { } remove { } }
        public event Action OnRuneQuickCastTriggered { add { } remove { } }
        public event Action OnRuneCastCancelled { add { } remove { } }

        public void TapGround(Vector3 point) { OnMoveDestinationSelected?.Invoke(point); }
        public void TapTarget(ICombatTarget target) { OnCombatTargetSelected?.Invoke(target); }
        public void Flick(Vector2 screenDirection) { OnCadenceVectorFlicked?.Invoke(screenDirection); }
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
