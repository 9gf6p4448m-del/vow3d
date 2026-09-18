using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Input
{
    public enum TouchPhaseKind
    {
        Began,
        Moved,
        Stationary,
        Ended,
        Canceled
    }

    // 手勢辨識的輸出端。PlayerInputService 實作它：點擊 → 射線 → 移動／攻擊事件；微彈 → OnCadenceVectorFlicked。
    public interface ITouchGestureSink
    {
        void OnWorldTap(float screenX, float screenY);
        void OnCadenceFlick(float screenDirX, float screenDirY);
        void OnUiRegionTapped(int regionId);

        // 地脈符印（GDD §參-1）。方向為螢幕座標的單位向量，拉伸量 0~1。
        void OnRuneDragUpdated(float screenDirX, float screenDirY, float distance01);
        void OnRuneQuickCast();
        void OnRuneReleased(float screenDirX, float screenDirY, float distance01);
        void OnRuneCancelled();
    }

    // 多指觸控的槽位管理與相位狀態機。不依賴 UnityEngine／InputSystem，可在 dotnet 下直接測試——
    // 這一層曾出過「切模式時憑空發移動指令」「多指同幀放開被補一次點擊」兩個缺陷，必須有測試守著。
    //
    // 模式 A：位移越過最小半徑的瞬間判為微彈；未越過而離手判為點擊。
    // 模式 B：世界觸控在「按下瞬間」即送出點擊；微輪盤只輸出方向，沒有任何路徑通往 OnWorldTap（高壓紅線）。
    public sealed class TouchGestureRouter
    {
        public const int MaxTouches = 10;
        private const int EndedHistorySize = MaxTouches; // 最壞情況：10 根手指同一幀全部放開

        private readonly InputRoutingManager _routing;
        private readonly ITouchGestureSink _sink;

        private readonly bool[] _slotUsed = new bool[MaxTouches];
        private readonly bool[] _slotSeen = new bool[MaxTouches];
        private readonly int[] _slotTouchId = new int[MaxTouches];
        private readonly TouchRoute[] _slotRoute = new TouchRoute[MaxTouches];
        private readonly int[] _slotUiRegion = new int[MaxTouches];
        private readonly FlickGestureTracker[] _trackers = new FlickGestureTracker[MaxTouches];

        // 最近結束的幾根手指（環形緩衝）：多指同一幀放開時每一根都要記得，不能只記最後一根
        private readonly int[] _endedTouchIds = new int[EndedHistorySize];
        private readonly double[] _endedStartTimes = new double[EndedHistorySize];
        private int _endedCursor;

        private MicroVectorPip _pip;
        private RuneGestureTracker _rune;
        private ControlMode _activeMode;

        public float ScreenWidth;
        public float ScreenHeight;
        public float MinRadiusPixels;
        public float MaxFlickSeconds = 0.25f;
        public float RuneSaturationPixels;

        public TouchGestureRouter(InputRoutingManager routing, ITouchGestureSink sink, ControlMode initialMode)
        {
            _routing = routing;
            _sink = sink;
            _activeMode = initialMode;
            for (int i = 0; i < EndedHistorySize; i++) _endedTouchIds[i] = -1;
        }

        public bool IsPipHeld => _pip.Held;
        public bool HasPipVector => _pip.HasVector;
        public float PipOriginX => _pip.OriginX;
        public float PipOriginY => _pip.OriginY;
        public float PipDirX => _pip.DirX;
        public float PipDirY => _pip.DirY;

        public bool IsRuneHeld => _rune.Held;
        public bool IsRuneDragging => _rune.Held && _rune.Dragging;

        public ControlMode ActiveMode
        {
            get => _activeMode;
            set
            {
                if (_activeMode == value) return;
                _activeMode = value;
                _pip.End();
                EmitRune(_rune.Cancel());

                // 切換模式時，還按在螢幕上的手指一律「作廢」而非「釋放」：槽位留著、路由改成 Rejected，直到它真的離開螢幕。
                // 若直接釋放槽位，下一幀這根手指會被當成新按下；在模式 B 下世界觸控是按下即送出，英雄會憑空朝那根手指走過去。
                for (int i = 0; i < MaxTouches; i++)
                {
                    if (!_slotUsed[i]) continue;
                    _slotRoute[i] = TouchRoute.Rejected;
                    _trackers[i].Cancel();
                }
            }
        }

        // 每幀：BeginFrame → 對每根在場手指呼叫 ProcessTouch → EndFrame
        public void BeginFrame()
        {
            for (int i = 0; i < MaxTouches; i++) _slotSeen[i] = false;
        }

        public void ProcessTouch(int touchId, TouchPhaseKind phase, float x, float y, double now, double touchStartTime)
        {
            int slot = FindSlot(touchId);
            if (slot < 0)
            {
                if (phase == TouchPhaseKind.Canceled) return;

                // 已結束的觸控若在列表中多留一幀，不得被當成新的一次點擊
                if (phase == TouchPhaseKind.Ended && WasRecentlyEnded(touchId, touchStartTime)) return;

                // 沒見過的手指：不論回報的相位為何，先走一次「按下」（同一幀內按下又放開的快速點擊也不會漏）
                slot = BeginTouch(touchId, x, y, now);
                if (slot < 0) return;
                _slotSeen[slot] = true;
                if (phase == TouchPhaseKind.Began) return;
            }
            _slotSeen[slot] = true;

            switch (phase)
            {
                case TouchPhaseKind.Moved:
                case TouchPhaseKind.Stationary:
                    MoveTouch(slot, x, y, now);
                    break;

                case TouchPhaseKind.Ended:
                    _endedTouchIds[_endedCursor] = touchId;
                    _endedStartTimes[_endedCursor] = touchStartTime;
                    _endedCursor = (_endedCursor + 1) % EndedHistorySize;
                    EndTouch(slot, x, y, now);
                    break;

                case TouchPhaseKind.Canceled:
                    CancelTouch(slot);
                    break;
            }
        }

        // 沒收到 Ended 就消失的手指（失焦、裝置移除）：回收槽位，否則 10 個槽位遲早漏光
        public void EndFrame()
        {
            for (int i = 0; i < MaxTouches; i++)
                if (_slotUsed[i] && !_slotSeen[i]) CancelTouch(i);
        }

        public int ActiveSlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MaxTouches; i++) if (_slotUsed[i]) count++;
                return count;
            }
        }

        // ───────────────────────── 相位處理 ─────────────────────────

        private int BeginTouch(int touchId, float x, float y, double now)
        {
            int slot = AllocateSlot(touchId);
            if (slot < 0) return -1;

            TouchRoute route = _routing.Route(x, y, ScreenWidth, ScreenHeight, _activeMode, out int regionId);
            _slotRoute[slot] = route;
            _slotUiRegion[slot] = regionId;

            switch (route)
            {
                case TouchRoute.Pip:
                    if (_pip.Held) _slotRoute[slot] = TouchRoute.Rejected; // 微輪盤一次只認一根手指
                    else _pip.Begin(touchId, x, y);
                    break;

                case TouchRoute.Rune:
                    if (_rune.Held) _slotRoute[slot] = TouchRoute.Rejected; // 符印一次只認一根手指
                    else _rune.Begin(touchId, x, y);
                    break;

                case TouchRoute.World:
                    if (_activeMode == ControlMode.ModeB_DualZonePip)
                    {
                        _sink.OnWorldTap(x, y);
                        _slotRoute[slot] = TouchRoute.Rejected; // 已處理完畢，後續相位不再有任何作用
                    }
                    else
                    {
                        _trackers[slot].Begin(touchId, x, y, now);
                    }
                    break;
            }
            return slot;
        }

        private void MoveTouch(int slot, float x, float y, double now)
        {
            switch (_slotRoute[slot])
            {
                case TouchRoute.Pip:
                    _pip.Move(x, y, MinRadiusPixels);
                    break;

                case TouchRoute.Rune:
                    EmitRune(_rune.Move(x, y, MinRadiusPixels, RuneSaturationPixels));
                    break;

                case TouchRoute.World:
                    if (_trackers[slot].Move(x, y, now, MinRadiusPixels, MaxFlickSeconds) == GestureOutcome.Flick)
                        _sink.OnCadenceFlick(_trackers[slot].FlickDirX, _trackers[slot].FlickDirY);
                    break;
            }
        }

        private void EndTouch(int slot, float x, float y, double now)
        {
            switch (_slotRoute[slot])
            {
                case TouchRoute.Rune:
                    EmitRune(_rune.End(x, y, MinRadiusPixels, RuneSaturationPixels, _routing.IsInRuneCancelZone(x, y)));
                    break;

                case TouchRoute.Pip:
                    _pip.End();
                    break;

                case TouchRoute.UiRegion:
                    // 按下與放開都在同一區域內才算點擊
                    if (_routing.Route(x, y, ScreenWidth, ScreenHeight, _activeMode, out int regionId) == TouchRoute.UiRegion
                        && regionId == _slotUiRegion[slot])
                        _sink.OnUiRegionTapped(regionId);
                    break;

                case TouchRoute.World:
                    GestureOutcome outcome = _trackers[slot].End(x, y, now, MinRadiusPixels, MaxFlickSeconds);
                    if (outcome == GestureOutcome.Flick) _sink.OnCadenceFlick(_trackers[slot].FlickDirX, _trackers[slot].FlickDirY);
                    else if (outcome == GestureOutcome.Tap) _sink.OnWorldTap(x, y);
                    break;
            }
            _slotUsed[slot] = false;
        }

        private void CancelTouch(int slot)
        {
            if (_slotRoute[slot] == TouchRoute.Pip) _pip.End();
            if (_slotRoute[slot] == TouchRoute.Rune) EmitRune(_rune.Cancel());
            _trackers[slot].Cancel();
            _slotUsed[slot] = false;
        }

        private void EmitRune(RuneGestureOutcome outcome)
        {
            switch (outcome)
            {
                case RuneGestureOutcome.DragUpdated: _sink.OnRuneDragUpdated(_rune.DirX, _rune.DirY, _rune.Distance01); break;
                case RuneGestureOutcome.QuickCast: _sink.OnRuneQuickCast(); break;
                case RuneGestureOutcome.Released: _sink.OnRuneReleased(_rune.DirX, _rune.DirY, _rune.Distance01); break;
                case RuneGestureOutcome.Cancelled: _sink.OnRuneCancelled(); break;
            }
        }

        // ───────────────────────── 槽位 ─────────────────────────

        private bool WasRecentlyEnded(int touchId, double startTime)
        {
            for (int i = 0; i < EndedHistorySize; i++)
                if (_endedTouchIds[i] == touchId && _endedStartTimes[i] == startTime) return true;
            return false;
        }

        private int FindSlot(int touchId)
        {
            for (int i = 0; i < MaxTouches; i++)
                if (_slotUsed[i] && _slotTouchId[i] == touchId) return i;
            return -1;
        }

        private int AllocateSlot(int touchId)
        {
            for (int i = 0; i < MaxTouches; i++)
            {
                if (_slotUsed[i]) continue;
                _slotUsed[i] = true;
                _slotTouchId[i] = touchId;
                _slotRoute[i] = TouchRoute.Rejected;
                _slotUiRegion[i] = -1;
                _trackers[i] = default;
                return i;
            }
            return -1;
        }
    }
}
