using System;
using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Input
{
    public enum LatencyPreset
    {
        Off,
        Ms50,
        Ms80
    }

    // 網路延遲注入（ARCHITECTURE §貳：50~80ms 網路模擬延遲，防單機自嗨偏差）。
    //
    // 包在真正的輸入服務外面的裝飾器：英雄訂閱的是它，它把移動／鎖定／微彈事件延遲一段時間後才轉送。
    // 模擬的是最壞情況——完全沒有客戶端預測、每個指令都要等一趟往返才生效——用來回答 GDD Premortem 漏洞一：
    // 扣掉網路延遲之後，220ms 目押窗口＋120ms 預輸入緩衝還按不按得出來。
    // 之後接上真正的客戶端預測（ARCHITECTURE §肆）時，本地手感只會比這個模式好，不會更差。
    //
    // 鐵律沿用 DelayedEventQueue：保序、不丟。佇列滿時最舊的事件立即送出，絕不吃指令。
    public sealed class NetworkLatencySimulator : MonoBehaviour, IPlayerInputService, IRuneCastInput
    {
        private const int QueueCapacity = 256;
        private const double JitterSeconds = 0.005; // 每筆 ±5ms

        // 極速施放／鬆手成牆屬於「送出指令」，忠於「沒有客戶端預測的最壞情況」一併走延遲佇列；
        // 拖曳更新／取消是本機 UI 回饋（虛影、取消手勢），維持直通，見下方的 pass-through 事件。
        private enum Kind { Move, Target, Flick, RuneQuickCast, RuneReleased }

        private struct Pending
        {
            public Kind Kind;
            public Vector3 Vector; // Flick：xy 方向；RuneReleased：x=方向X y=方向Y z=distance01
            public ICombatTarget Target;
        }

        [SerializeField] private PlayerInputService _inner;
        [SerializeField] private LatencyPreset _preset = LatencyPreset.Off;

        private readonly DelayedEventQueue<Pending> _queue = new DelayedEventQueue<Pending>(QueueCapacity);
        private LatencyJitter _jitter = new LatencyJitter(0xC0FFEEu);
        private bool _subscribed;

        public event Action<Vector3> OnMoveDestinationSelected;
        public event Action<ICombatTarget> OnCombatTargetSelected;
        public event Action<Vector2> OnCadenceVectorFlicked;

        // 符印事件屬 Phase 2，直接掛回內層，不經延遲佇列。
        public event Action<Vector2, float> OnRuneVectorDragUpdated
        {
            add { if (_inner != null) _inner.OnRuneVectorDragUpdated += value; }
            remove { if (_inner != null) _inner.OnRuneVectorDragUpdated -= value; }
        }

        // 極速施放：走延遲佇列，見 Subscribe/HandleRuneQuickCast/Dispatch。
        public event Action OnRuneQuickCastTriggered;

        public event Action OnRuneCastCancelled
        {
            add { if (_inner != null) _inner.OnRuneCastCancelled += value; }
            remove { if (_inner != null) _inner.OnRuneCastCancelled -= value; }
        }

        // 鬆手成牆：走延遲佇列，見 Subscribe/HandleRuneReleased/Dispatch。
        public event Action<Vector2, float> OnRuneCastReleased;

        public ControlMode ActiveMode
        {
            get => _inner != null ? _inner.ActiveMode : ControlMode.ModeA_FullScreenFlick;
            set { if (_inner != null) _inner.ActiveMode = value; }
        }

        public LatencyPreset Preset
        {
            get => _preset;
            set
            {
                if (_preset == value) return;
                _preset = value;
                if (_preset == LatencyPreset.Off) FlushAll(); // 關掉時依序清空，已經在路上的指令不得消失、也不得被後來的直通事件超車
            }
        }

        public int PendingCount => _queue.Count;

        public void Initialize(PlayerInputService inner)
        {
            Unsubscribe();
            _inner = inner;
            Subscribe();
        }

        public void CyclePreset()
        {
            Preset = _preset == LatencyPreset.Off ? LatencyPreset.Ms50
                   : _preset == LatencyPreset.Ms50 ? LatencyPreset.Ms80
                   : LatencyPreset.Off;
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            FlushAll();
        }

        private void Subscribe()
        {
            if (_subscribed || _inner == null || !isActiveAndEnabled) return;
            _inner.OnMoveDestinationSelected += HandleMove;
            _inner.OnCombatTargetSelected += HandleTarget;
            _inner.OnCadenceVectorFlicked += HandleFlick;
            _inner.OnRuneQuickCastTriggered += HandleRuneQuickCast;
            _inner.OnRuneCastReleased += HandleRuneReleased;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _inner == null) return;
            _inner.OnMoveDestinationSelected -= HandleMove;
            _inner.OnCombatTargetSelected -= HandleTarget;
            _inner.OnCadenceVectorFlicked -= HandleFlick;
            _inner.OnRuneQuickCastTriggered -= HandleRuneQuickCast;
            _inner.OnRuneCastReleased -= HandleRuneReleased;
            _subscribed = false;
        }

        private void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            while (_queue.TryDequeueDue(now, out Pending pending)) Dispatch(pending);
        }

        private void HandleMove(Vector3 destination)
        {
            Submit(new Pending { Kind = Kind.Move, Vector = destination });
        }

        private void HandleTarget(ICombatTarget target)
        {
            Submit(new Pending { Kind = Kind.Target, Target = target });
        }

        private void HandleFlick(Vector2 direction)
        {
            Submit(new Pending { Kind = Kind.Flick, Vector = new Vector3(direction.x, direction.y, 0f) });
        }

        private void HandleRuneQuickCast()
        {
            Submit(new Pending { Kind = Kind.RuneQuickCast });
        }

        private void HandleRuneReleased(Vector2 direction, float distance01)
        {
            Submit(new Pending { Kind = Kind.RuneReleased, Vector = new Vector3(direction.x, direction.y, distance01) });
        }

        private void Submit(Pending pending)
        {
            double center = CenterSeconds(_preset);
            if (center <= 0.0)
            {
                Dispatch(pending); // OFF：同一呼叫內直通，不多等一幀
                return;
            }

            double delay = _jitter.Next(center - JitterSeconds, center + JitterSeconds);
            if (_queue.Enqueue(pending, Time.unscaledTimeAsDouble, delay, out Pending evicted)) Dispatch(evicted);
        }

        private void FlushAll()
        {
            while (_queue.TryDequeueAny(out Pending pending)) Dispatch(pending);
        }

        private void Dispatch(Pending pending)
        {
            switch (pending.Kind)
            {
                case Kind.Move:
                    OnMoveDestinationSelected?.Invoke(pending.Vector);
                    break;
                case Kind.Target:
                    OnCombatTargetSelected?.Invoke(pending.Target);
                    break;
                case Kind.Flick:
                    OnCadenceVectorFlicked?.Invoke(new Vector2(pending.Vector.x, pending.Vector.y));
                    break;
                case Kind.RuneQuickCast:
                    OnRuneQuickCastTriggered?.Invoke();
                    break;
                case Kind.RuneReleased:
                    OnRuneCastReleased?.Invoke(new Vector2(pending.Vector.x, pending.Vector.y), pending.Vector.z);
                    break;
            }
        }

        public static double CenterSeconds(LatencyPreset preset)
        {
            switch (preset)
            {
                case LatencyPreset.Ms50: return 0.050;
                case LatencyPreset.Ms80: return 0.080;
                default: return 0.0;
            }
        }
    }
}
