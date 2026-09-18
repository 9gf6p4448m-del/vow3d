using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Vow.Core;
using Vow.Core.Logic;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

// 命名空間刻意取名 Vow.Input：在 Vow.* 之下寫 `Input.xxx` 會先解析到這個命名空間而編譯失敗，
// 等於替「嚴禁舊版 UnityEngine.Input」這條紅線多上一道編譯期保險。
namespace Vow.Input
{
    // IPlayerInputService 的 New Input System 實作（EnhancedTouch；桌機以 TouchSimulation 讓滑鼠走同一條路徑）。
    // 本類別只做轉接：EnhancedTouch → TouchGestureRouter（純邏輯、有測試）→ 射線判定 → 對外事件。
    // 手勢怎麼判、手指槽位怎麼管，全部在 TouchGestureRouter。
    public sealed class PlayerInputService : MonoBehaviour, IPlayerInputService, ITouchGestureSink
    {
        private const float FallbackDpi = 160f;
        private const float PipZoneMillimeters = 42f;
        private const float RaycastDistance = 500f;
        private const float InputSamplingHz = 120f;

        [SerializeField] private Camera _worldCamera;
        [SerializeField] private ControlMode _initialMode = ControlMode.ModeA_FullScreenFlick;

        private readonly InputRoutingManager _routing = new InputRoutingManager();
        private TouchGestureRouter _router;

        private ICombatTargetResolver _targetResolver;
        private float _minRadiusPx;
        private float _maxRadiusPx;
        private float _pipZonePx;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private bool _enabledTouchSimulation;

        public event Action<Vector3> OnMoveDestinationSelected;
        public event Action<ICombatTarget> OnCombatTargetSelected;
        public event Action<Vector2> OnCadenceVectorFlicked;

        // 符印事件屬 Phase 2（冷庫協議）。契約保留、Phase 1 不發；空的 add/remove 避免編譯器對「從未使用的事件」提出警告。
        public event Action<Vector2, float> OnRuneVectorDragUpdated { add { } remove { } }
        public event Action OnRuneQuickCastTriggered { add { } remove { } }
        public event Action OnRuneCastCancelled { add { } remove { } }

        // 已登記的 UI 區域被點擊（HUD 按鈕走這裡，不另接 EventSystem——全專案只有一條輸入路徑）。
        public event Action<int> OnUiRegionTapped;

        public InputRoutingManager Routing => _routing;
        public float FlickMinRadiusPixels => _minRadiusPx;
        public float FlickMaxRadiusPixels => _maxRadiusPx;
        public float PipZonePixels => _pipZonePx;

        // 微輪盤目前狀態（給 HUD 繪製與預警箭頭預覽）。
        public bool IsPipHeld => Router.IsPipHeld;
        public bool HasPipVector => Router.HasPipVector;
        public Vector2 PipOrigin => new Vector2(Router.PipOriginX, Router.PipOriginY);
        public Vector2 PipDirection => new Vector2(Router.PipDirX, Router.PipDirY);

        public ControlMode ActiveMode
        {
            get => Router.ActiveMode;
            set => Router.ActiveMode = value;
        }

        // 惰性建立：其他元件可能在本元件 Awake 之前就讀 ActiveMode／Routing（同物件上的 Awake 順序 Unity 不保證）。
        private TouchGestureRouter Router
        {
            get
            {
                if (_router == null) _router = new TouchGestureRouter(_routing, this, _initialMode);
                return _router;
            }
        }

        public void Initialize(ICombatTargetResolver targetResolver, Camera worldCamera)
        {
            _targetResolver = targetResolver;
            if (worldCamera != null) _worldCamera = worldCamera;
        }

        private void Awake()
        {
            if (_worldCamera == null) _worldCamera = Camera.main;
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            InputSystem.pollingFrequency = InputSamplingHz; // 紅線 6：輸入採樣鎖定 120Hz

            // 沒有實體觸控螢幕（Editor／PC）時，讓滑鼠模擬成觸控，與手機走完全相同的程式路徑。
            if (Touchscreen.current == null)
            {
                TouchSimulation.Enable();
                _enabledTouchSimulation = true;
            }
            RefreshScreenMetrics();
        }

        private void OnDisable()
        {
            if (_enabledTouchSimulation)
            {
                TouchSimulation.Disable();
                _enabledTouchSimulation = false;
            }
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) RefreshScreenMetrics();

            TouchGestureRouter router = Router;
            double now = Time.unscaledTimeAsDouble;

            router.BeginFrame();
            var touches = Touch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                Touch touch = touches[i];
                if (!TryMapPhase(touch.phase, out TouchPhaseKind phase)) continue;

                Vector2 position = touch.screenPosition;
                router.ProcessTouch(touch.touchId, phase, position.x, position.y, now, touch.startTime);
            }
            router.EndFrame();

            EmitHeldPipVector(router);
        }

        private static bool TryMapPhase(TouchPhase phase, out TouchPhaseKind kind)
        {
            switch (phase)
            {
                case TouchPhase.Began: kind = TouchPhaseKind.Began; return true;
                case TouchPhase.Moved: kind = TouchPhaseKind.Moved; return true;
                case TouchPhase.Stationary: kind = TouchPhaseKind.Stationary; return true;
                case TouchPhase.Ended: kind = TouchPhaseKind.Ended; return true;
                case TouchPhase.Canceled: kind = TouchPhaseKind.Canceled; return true;
                default: kind = TouchPhaseKind.Canceled; return false; // TouchPhase.None
            }
        }

        private void RefreshScreenMetrics()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            float dpi = Screen.dpi;
            _minRadiusPx = GestureMath.MillimetersToPixels(GestureMath.FlickMinMillimeters, dpi, FallbackDpi);
            _maxRadiusPx = GestureMath.MillimetersToPixels(GestureMath.FlickMaxMillimeters, dpi, FallbackDpi);
            _pipZonePx = GestureMath.MillimetersToPixels(PipZoneMillimeters, dpi, FallbackDpi);

            _routing.SetPipZone(new ScreenRegion(0f, 0f, _pipZonePx, _pipZonePx));

            TouchGestureRouter router = Router;
            router.ScreenWidth = _lastScreenWidth;
            router.ScreenHeight = _lastScreenHeight;
            router.MinRadiusPixels = _minRadiusPx;
        }

        // 模式 B：微輪盤推著的期間每幀送出方向。狀態機的 120ms 預輸入緩衝會自然吃到「命中前最後一刻」的那一筆，
        // 於是「先推好方向、命中幀自動滑出」不需要任何特例。桌機測試以 WASD／方向鍵代替左手拇指。
        private void EmitHeldPipVector(TouchGestureRouter router)
        {
            if (router.ActiveMode != ControlMode.ModeB_DualZonePip) return;

            if (router.HasPipVector)
            {
                OnCadenceFlick(router.PipDirX, router.PipDirY);
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            float x = 0f, y = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;

            if (GestureMath.SnapToEightWay(x, y, out float sx, out float sy)) OnCadenceFlick(sx, sy);
        }

        // ───────────────────────── ITouchGestureSink ─────────────────────────

        public void OnCadenceFlick(float screenDirX, float screenDirY)
        {
            OnCadenceVectorFlicked?.Invoke(new Vector2(screenDirX, screenDirY));
        }

        void ITouchGestureSink.OnUiRegionTapped(int regionId)
        {
            OnUiRegionTapped?.Invoke(regionId);
        }

        public void OnWorldTap(float screenX, float screenY)
        {
            if (_worldCamera == null) return;

            Ray ray = _worldCamera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, RaycastDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;

            if (_targetResolver != null && _targetResolver.TryResolve(hit.collider, out ICombatTarget target) && target.IsAlive)
                OnCombatTargetSelected?.Invoke(target);
            else
                OnMoveDestinationSelected?.Invoke(hit.point);
        }
    }
}
