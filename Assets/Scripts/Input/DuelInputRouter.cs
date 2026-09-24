using System;
using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Input
{
    // 英雄與符印施放器唯一訂閱的輸入出口；開局點擊在此被消費，不依賴多播事件順序。
    public sealed class DuelInputRouter : IPlayerInputService, IRuneCastInput, IDisposable
    {
        private readonly IPlayerInputService _source;
        private readonly IRuneCastInput _runes;
        private readonly ICombatTarget _opponent;
        private readonly DuelRoundLogic _round;
        private readonly ICaptureMatchView _capture;

        public DuelInputRouter(IPlayerInputService source, IRuneCastInput runes,
                               ICombatTarget opponent, DuelRoundLogic round)
            : this(source, runes, opponent, round, null)
        {
        }

        // v0.8.0（V080_CAPTURE_PLAN.md §2.2「路由」）：另注入佔領唯讀視圖。只有佔領模式（Lobby／Active／Ended）時
        // 行為才改變；capture 為 null 或狀態為 Off 時，MatchGate 的 Off 列與 v0.7.0 逐列相同（V-A20）。
        public DuelInputRouter(IPlayerInputService source, IRuneCastInput runes,
                               ICombatTarget opponent, DuelRoundLogic round, ICaptureMatchView capture)
        {
            _source = source;
            _runes = runes;
            _opponent = opponent;
            _round = round;
            _capture = capture;
            source.OnMoveDestinationSelected += HandleMove;
            source.OnCombatTargetSelected += HandleTarget;
            source.OnCadenceVectorFlicked += HandleFlick;
            source.OnRuneVectorDragUpdated += HandleRuneDrag;
            source.OnRuneQuickCastTriggered += HandleQuickCast;
            source.OnRuneCastCancelled += HandleRuneCancelled;
            runes.OnRuneCastReleased += HandleRuneReleased;
        }

        public ControlMode ActiveMode { get => _source.ActiveMode; set => _source.ActiveMode = value; }
        public event Action<Vector3> OnMoveDestinationSelected;
        public event Action<ICombatTarget> OnCombatTargetSelected;
        public event Action<Vector2> OnCadenceVectorFlicked;
        public event Action<Vector2, float> OnRuneVectorDragUpdated;
        public event Action OnRuneQuickCastTriggered;
        public event Action OnRuneCastCancelled;
        public event Action<Vector2, float> OnRuneCastReleased;
        public event Action OnStartRequested;
        public event Action OnCaptureStartRequested;

        private CaptureMatchState CaptureState => _capture != null ? _capture.State : CaptureMatchState.Off;

        // 佔領對局中英雄是否活著：看唯讀視圖的倒地旗標（Bootstrap 在英雄倒地與復活的同一個呼叫裡更新它）。
        private MatchGateDecision Gate =>
            MatchGate.Evaluate(_round.State, CaptureState, _capture == null || !_capture.BlueKnockedOut);

        // Off：只有單挑 KO 停頓封鎖（v0.7.0 原樣）；佔領：英雄倒地與結算停頓也封鎖（R2）。
        private bool IsPaused => Gate.HeroInputBlocked;

        private void HandleMove(Vector3 point)
        {
            if (!IsPaused) OnMoveDestinationSelected?.Invoke(point);
        }

        private void HandleTarget(ICombatTarget target)
        {
            if (IsPaused || target == null) return;
            MatchGateDecision gate = Gate;
            if (ReferenceEquals(target, _opponent))
            {
                // 單挑待機點對手＝開單挑；佔領待機點對手＝開佔領局。兩者都只開局、不轉發當次普攻（R1）。
                if (gate.TapAction == CaptureTapAction.OpenDuel)
                {
                    OnStartRequested?.Invoke();
                    return;
                }
                if (gate.TapAction == CaptureTapAction.OpenCapture)
                {
                    OnCaptureStartRequested?.Invoke();
                    return;
                }
            }
            // 對局中（單挑 Active 或佔領 Active）只能鎖定對手與石牆（E25）。
            bool inMatch = _round.State == DuelRoundState.Active || CaptureState == CaptureMatchState.Active;
            if (inMatch && !ReferenceEquals(target, _opponent)
                && target.TargetFaction != Faction.DestructibleWall) return;
            OnCombatTargetSelected?.Invoke(target);
        }

        private void HandleFlick(Vector2 direction)
        {
            if (!IsPaused) OnCadenceVectorFlicked?.Invoke(direction);
        }

        private void HandleRuneDrag(Vector2 direction, float distance)
        {
            if (!IsPaused) OnRuneVectorDragUpdated?.Invoke(direction, distance);
        }

        private void HandleQuickCast()
        {
            if (!IsPaused) OnRuneQuickCastTriggered?.Invoke();
        }

        private void HandleRuneCancelled()
        {
            if (!IsPaused) OnRuneCastCancelled?.Invoke();
        }

        private void HandleRuneReleased(Vector2 direction, float distance)
        {
            if (!IsPaused) OnRuneCastReleased?.Invoke(direction, distance);
        }

        public void Dispose()
        {
            _source.OnMoveDestinationSelected -= HandleMove;
            _source.OnCombatTargetSelected -= HandleTarget;
            _source.OnCadenceVectorFlicked -= HandleFlick;
            _source.OnRuneVectorDragUpdated -= HandleRuneDrag;
            _source.OnRuneQuickCastTriggered -= HandleQuickCast;
            _source.OnRuneCastCancelled -= HandleRuneCancelled;
            _runes.OnRuneCastReleased -= HandleRuneReleased;
        }
    }
}
