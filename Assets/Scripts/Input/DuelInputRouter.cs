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

        public DuelInputRouter(IPlayerInputService source, IRuneCastInput runes,
                               ICombatTarget opponent, DuelRoundLogic round)
        {
            _source = source;
            _runes = runes;
            _opponent = opponent;
            _round = round;
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

        private bool IsPaused => _round.State == DuelRoundState.KnockoutPause;

        private void HandleMove(Vector3 point)
        {
            if (!IsPaused) OnMoveDestinationSelected?.Invoke(point);
        }

        private void HandleTarget(ICombatTarget target)
        {
            if (IsPaused || target == null) return;
            if (_round.State == DuelRoundState.Dormant && ReferenceEquals(target, _opponent))
            {
                OnStartRequested?.Invoke();
                return;
            }
            if (_round.State == DuelRoundState.Active && !ReferenceEquals(target, _opponent)
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
