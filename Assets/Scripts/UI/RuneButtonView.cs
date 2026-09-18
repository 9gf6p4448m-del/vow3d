using System;
using UnityEngine;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.UI
{
    // 右下角地脈符印鈕（GDD §參-1）。IMGUI 自畫，不用 GUI.Button、不引入 EventSystem——
    // 全專案只有一條輸入路徑：整顆按鈕的觸控歸 InputRoutingManager 的 Rune 路由所有（見 RecalculateLayout 對
    // input.Routing.SetRuneZone／SetRuneCancelZone 的呼叫）；手勢辨識與事件全部在 TouchGestureRouter／
    // RuneGestureTracker／RuneCaster，這裡只負責畫面與登記區域。
    public sealed class RuneButtonView : MonoBehaviour
    {
        private const float FallbackDpi = 160f;
        private const float ButtonDiameterMillimeters = 16f;
        private const float EdgeSafetyMarginPixels = 8f; // 疊在 GestureMath.EdgeDeadzonePixels 之上的額外安全距離
        private const float CancelZoneHeightMillimeters = 20f;
        private const float CancelZoneGapPixels = 6f;

        private static readonly Color ReadyColor = new Color(0.25f, 0.55f, 0.85f, 0.9f);
        private static readonly Color CooldownBaseColor = new Color(0.15f, 0.18f, 0.22f, 0.85f);
        private static readonly Color CooldownMaskColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color CancelZoneColor = new Color(0.85f, 0.25f, 0.25f, 0.35f);

        private PlayerInputService _input;
        private RuneTuning _tuning;
        private Func<float> _cooldownRemainingSecondsProvider;

        private Rect _buttonRect;
        private Rect _cancelRect;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private GUIStyle _label;

        // cooldownRemainingSecondsProvider 用 Func 而非直接持有 RuneCaster：Vow.UI 不依賴 Vow.Combat，
        // 接線交給 Phase1Bootstrap（它同時看得到兩邊），避免只為了讀一個數字就替組件邊界開一條新的相依。
        public void Initialize(PlayerInputService input, RuneTuning tuning, Func<float> cooldownRemainingSecondsProvider)
        {
            _input = input;
            _tuning = tuning;
            _cooldownRemainingSecondsProvider = cooldownRemainingSecondsProvider;
            RecalculateLayout();
        }

        private void Awake()
        {
            useGUILayout = false; // IMGUI 零配置的前提，同 DebugHud
        }

        private void Update()
        {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) RecalculateLayout();
        }

        private void RecalculateLayout()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            if (_input == null) return;

            float dpi = Screen.dpi;
            float diameter = GestureMath.MillimetersToPixels(ButtonDiameterMillimeters, dpi, FallbackDpi);
            float margin = GestureMath.EdgeDeadzonePixels + EdgeSafetyMarginPixels;

            float buttonRight = Screen.width - margin;
            float buttonBottom = Screen.height - margin;
            _buttonRect = new Rect(buttonRight - diameter, buttonBottom - diameter, diameter, diameter);

            float cancelHeight = GestureMath.MillimetersToPixels(CancelZoneHeightMillimeters, dpi, FallbackDpi);
            _cancelRect = new Rect(_buttonRect.xMin, _buttonRect.yMin - cancelHeight - CancelZoneGapPixels,
                _buttonRect.width, cancelHeight);

            _input.Routing.SetRuneZone(ToScreenRegion(_buttonRect));
            _input.Routing.SetRuneCancelZone(ToScreenRegion(_cancelRect));
        }

        // IMGUI 座標（原點左上）→ 螢幕座標（原點左下）。RuneButtonView 不像 DebugHud 套 GUI.matrix 縮放，
        // 按鈕的物理尺寸已經是用 Screen.dpi 換算出來的實際像素，兩者座標系直接 1:1 對應。
        private static ScreenRegion ToScreenRegion(Rect guiRect)
        {
            float yMax = Screen.height - guiRect.yMin;
            float yMin = Screen.height - guiRect.yMax;
            return new ScreenRegion(guiRect.xMin, yMin, guiRect.xMax, yMax);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_input == null) return;
            EnsureStyles();

            if (_input.IsRuneDragging) Fill(_cancelRect, CancelZoneColor);

            float remaining = _cooldownRemainingSecondsProvider != null ? Mathf.Max(0f, _cooldownRemainingSecondsProvider()) : 0f;
            bool cooling = remaining > 0f;

            Fill(_buttonRect, cooling ? CooldownBaseColor : ReadyColor);

            if (cooling)
            {
                float cooldownSeconds = _tuning != null ? _tuning.CooldownSeconds : 0f;
                float t = cooldownSeconds > 0f ? Mathf.Clamp01(remaining / cooldownSeconds) : 0f;
                float maskHeight = _buttonRect.height * t; // 由下往上遮罩，隨冷卻剩餘時間縮短
                Fill(new Rect(_buttonRect.x, _buttonRect.yMax - maskHeight, _buttonRect.width, maskHeight), CooldownMaskColor);
                GUI.Label(_buttonRect, IntStringCache.Get(Mathf.CeilToInt(remaining)), _label);
            }
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _label.normal.textColor = Color.white;
        }
    }
}
