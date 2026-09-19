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
    // IPlayerInputService 的 New Input System 實作（EnhancedTouch 讀觸控；滑鼠左鍵被當成一根手指餵進同一個路由）。
    // 本類別只做轉接：EnhancedTouch → TouchGestureRouter（純邏輯、有測試）→ 射線判定 → 對外事件。
    // 手勢怎麼判、手指槽位怎麼管，全部在 TouchGestureRouter。
    public sealed class PlayerInputService : MonoBehaviour, IPlayerInputService, IRuneCastInput, IWorldTapInput, ITouchGestureSink
    {
        private const float FallbackDpi = 160f;
        private const float PipZoneMillimeters = 42f;
        private const float RaycastDistance = 500f;
        private const float InputSamplingHz = 120f;
        private const int MouseTouchId = -1000;              // 不會與真實 touchId 相撞的固定編號
        private const double MouseSuppressSeconds = 0.5;      // 真實觸控之後這段時間內忽略滑鼠

        [SerializeField] private Camera _worldCamera;
        [SerializeField] private ControlMode _initialMode = ControlMode.ModeA_FullScreenFlick;
        [SerializeField] private float _runeSaturationMillimeters = 14f; // 與 RuneTuning.DragSaturationMillimeters 同值，由場景建置器寫入

        private readonly InputRoutingManager _routing = new InputRoutingManager();
        private TouchGestureRouter _router;

        // Phase 2 批 3：符印石牆回到 Default 層之後，點擊改成「射線照常打到牆，再做陣營校驗、己方牆往後找」
        // （§4-1；否決分陣營圖層的理由見同節）。全部緩衝預配置，執行期零配置。
        private readonly ProjectileTuning _projectileTuning = new ProjectileTuning();
        private RaycastHit[] _tapHits;
        private float[] _tapDistances;
        private bool[] _tapOwnWall;
        private Faction _localFaction = Faction.BlueTeam;

        private ICombatTargetResolver _targetResolver;
        private float _minRadiusPx;
        private float _maxRadiusPx;
        private float _pipZonePx;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private bool _mouseHeld;
        private double _mouseStartTime;
        private double _lastRealTouchTime = -10.0;

        public event Action<Vector3> OnMoveDestinationSelected;
        public event Action<ICombatTarget> OnCombatTargetSelected;
        public event Action<Vector2> OnCadenceVectorFlicked;

        // 地脈符印（GDD §參-1）。手勢怎麼判在 RuneGestureTracker；符印區由 RuneButtonView 登記到 Routing。
        public event Action<Vector2, float> OnRuneVectorDragUpdated;
        public event Action OnRuneQuickCastTriggered;
        public event Action OnRuneCastCancelled;
        public event Action<Vector2, float> OnRuneCastReleased;

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

        public bool IsRuneDragging => Router.IsRuneDragging;
        public bool IsRuneCancelArmed => Router.IsRuneCancelArmed;
        public float RuneSaturationPixels => Router.RuneSaturationPixels;
        public float PixelsPerMillimeter => GestureMath.MillimetersToPixels(1f, Screen.dpi, FallbackDpi);

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

        // 本地玩家的陣營：決定哪些石牆算「自家牆」（點下去要穿過去點到牆後的地板）。
        public Faction LocalFaction => _localFaction;

        public void SetLocalFaction(Faction faction)
        {
            _localFaction = faction;
        }

        private void Awake()
        {
            if (_worldCamera == null) _worldCamera = Camera.main;
            EnsureTapBuffers();
        }

        private void EnsureTapBuffers()
        {
            if (_tapHits != null) return;
            int size = _projectileTuning.TapHitBufferSize;
            _tapHits = new RaycastHit[size];
            _tapDistances = new float[size];
            _tapOwnWall = new bool[size];
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            InputSystem.pollingFrequency = InputSamplingHz; // 紅線 6：輸入採樣鎖定 120Hz

            RefreshScreenMetrics();
        }

        private void OnDisable()
        {
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
            if (touches.Count > 0) _lastRealTouchTime = now;
            FeedMouse(router, now);
            router.EndFrame();

            EmitHeldPipVector(router);
        }

        // 滑鼠左鍵 = 一根手指。原本靠 TouchSimulation 把滑鼠轉成觸控，但 WebGL 平台不論有沒有觸控螢幕都會註冊 Touchscreen 裝置，
        // 「沒有觸控螢幕才開模擬」的判斷在瀏覽器上永遠不成立，桌機滑鼠因此完全沒反應（實機驗收發現）。直接讀滑鼠沒有這個平台差異。
        // 真實觸控之後的一小段時間忽略滑鼠：手機瀏覽器會替觸控補發相容用的滑鼠事件，不擋掉的話一次點擊會變兩次（HUD 開關會被切兩下）。
        private void FeedMouse(TouchGestureRouter router, double now)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (now - _lastRealTouchTime < MouseSuppressSeconds)
            {
                _mouseHeld = false; // 槽位由 router.EndFrame 的「本幀沒出現」回收
                return;
            }

            Vector2 position = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            bool released = mouse.leftButton.wasReleasedThisFrame;

            if (pressed)
            {
                _mouseHeld = true;
                _mouseStartTime = now;
                router.ProcessTouch(MouseTouchId, TouchPhaseKind.Began, position.x, position.y, now, _mouseStartTime);
            }

            if (!_mouseHeld) return;

            if (released || !mouse.leftButton.isPressed)
            {
                _mouseHeld = false;
                router.ProcessTouch(MouseTouchId, TouchPhaseKind.Ended, position.x, position.y, now, _mouseStartTime);
            }
            else if (!pressed)
            {
                router.ProcessTouch(MouseTouchId, TouchPhaseKind.Moved, position.x, position.y, now, _mouseStartTime);
            }
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
            router.RuneSaturationPixels = GestureMath.MillimetersToPixels(_runeSaturationMillimeters, dpi, FallbackDpi);
            router.RuneTapSlopPixels = _maxRadiusPx; // 7.5mm：與微彈的飽和半徑同一把尺
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

        void ITouchGestureSink.OnRuneDragUpdated(float screenDirX, float screenDirY, float distance01)
        {
            OnRuneVectorDragUpdated?.Invoke(new Vector2(screenDirX, screenDirY), distance01);
        }

        void ITouchGestureSink.OnRuneQuickCast()
        {
            OnRuneQuickCastTriggered?.Invoke();
        }

        void ITouchGestureSink.OnRuneReleased(float screenDirX, float screenDirY, float distance01)
        {
            OnRuneCastReleased?.Invoke(new Vector2(screenDirX, screenDirY), distance01);
        }

        void ITouchGestureSink.OnRuneCancelled()
        {
            OnRuneCastCancelled?.Invoke();
        }

        // 使用者裁定 1：點己方牆＝穿過去點到牆後的地板（維持批 1 的手感）；敵方／中立牆點得到、會鎖定去砸。
        // 做法是拿到射線上的**全部**命中，把「己方石牆」標記起來，再取剩下的最近者——
        // 沒有它就回不到 v0.4.1 的手感，因為符印牆已經從 Ignore Raycast 層回到 Default 層（§4-1）。
        public void OnWorldTap(float screenX, float screenY)
        {
            if (_worldCamera == null) return;
            EnsureTapBuffers();

            Ray ray = _worldCamera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
            int count = Physics.RaycastNonAlloc(ray, _tapHits, RaycastDistance,
                                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;
            if (count >= _tapHits.Length)
            {
                count = _tapHits.Length;
                // 緩衝溢位會讓最近的合法命中被丟掉（RaycastNonAlloc 不保證由近到遠）。常數字串，不配置。
                Debug.LogWarning("[VOW] 點擊射線的命中數已達緩衝上限，最近的合法命中可能被丟掉。");
            }

            for (int i = 0; i < count; i++)
            {
                _tapDistances[i] = _tapHits[i].distance;
                _tapOwnWall[i] = IsOwnWall(_tapHits[i].collider);
            }

            int pick = TapPickLogic.SelectNearestAcceptable(_tapDistances, _tapOwnWall, count);
            if (pick < 0) return; // 整條射線上只有自家牆：這一下什麼都不做

            Collider picked = _tapHits[pick].collider;
            if (_targetResolver != null && _targetResolver.TryResolve(picked, out ICombatTarget target) && target.IsAlive)
                OnCombatTargetSelected?.Invoke(target);
            else
                OnMoveDestinationSelected?.Invoke(_tapHits[pick].point);
        }

        private bool IsOwnWall(Collider collider)
        {
            if (_targetResolver == null) return false;
            if (!_targetResolver.TryResolve(collider, out ICombatTarget target) || target == null) return false;
            if (target.TargetFaction != Faction.DestructibleWall) return false;

            IFactionOwned owned = target as IFactionOwned;
            return owned != null && owned.OwnerFaction == _localFaction;
        }
    }
}
