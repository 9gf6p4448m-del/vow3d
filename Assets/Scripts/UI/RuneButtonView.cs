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
    //
    // 幾何本身（按鈕與取消區的位置／大小）一律向 RuneButtonLayout 要，不在這裡另算一份——
    // r1 對抗審查 C1：取消區緊貼按鈕、拖曳飽和行程可以拉進取消區，兩邊各算一次遲早會對不上。
    public sealed class RuneButtonView : MonoBehaviour
    {
        private static readonly Color ReadyColor = new Color(0.25f, 0.55f, 0.85f, 0.9f);
        private static readonly Color CooldownBaseColor = new Color(0.15f, 0.18f, 0.22f, 0.85f);
        private static readonly Color CooldownMaskColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color CancelZoneColor = new Color(0.85f, 0.25f, 0.25f, 0.35f);

        private PlayerInputService _input;
        private RuneTuning _tuning;
        private Func<float> _cooldownRemainingSecondsProvider;

        private ScreenRegion _buttonRegion;
        private ScreenRegion _cancelRegion;
        private bool _layoutValid;
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
            _layoutValid = false;
            RecalculateLayout();
        }

        private void Awake()
        {
            useGUILayout = false; // IMGUI 零配置的前提，同 DebugHud
        }

        private void Update()
        {
            if (!_layoutValid || Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) RecalculateLayout();
        }

        private void RecalculateLayout()
        {
            if (_input == null) return;

            // RuneSaturationPixels 由 PlayerInputService.RefreshScreenMetrics 算出（OnEnable／螢幕尺寸變動時）；
            // 在那之前是 0——用 0 算出來的取消區會貼著按鈕（正是 C1 的洞），寧可這一幀先不登記，下一幀 Update() 會重試。
            float saturationPixels = _input.RuneSaturationPixels;
            if (saturationPixels <= 0f) return;

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            RuneButtonLayout layout = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter, saturationPixels);
            _buttonRegion = layout.Button;
            _cancelRegion = layout.CancelZone;
            _layoutValid = true;

            _input.Routing.SetRuneZone(_buttonRegion);
            _input.Routing.SetRuneCancelZone(_cancelRegion);
        }

        // 螢幕座標（原點左下）→ IMGUI 座標（原點左上）：只是 y 翻轉，兩邊用的是同一份 RuneButtonLayout 結果。
        private static Rect ToGuiRect(ScreenRegion region)
        {
            float yMin = Screen.height - region.YMax;
            return new Rect(region.XMin, yMin, region.XMax - region.XMin, region.YMax - region.YMin);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_input == null || !_layoutValid) return;
            EnsureStyles();

            Rect buttonRect = ToGuiRect(_buttonRegion);

            if (_input.IsRuneDragging) Fill(ToGuiRect(_cancelRegion), CancelZoneColor);

            float remaining = _cooldownRemainingSecondsProvider != null ? Mathf.Max(0f, _cooldownRemainingSecondsProvider()) : 0f;
            bool cooling = remaining > 0f;

            Fill(buttonRect, cooling ? CooldownBaseColor : ReadyColor);

            if (cooling)
            {
                float cooldownSeconds = _tuning != null ? _tuning.CooldownSeconds : 0f;
                float t = cooldownSeconds > 0f ? Mathf.Clamp01(remaining / cooldownSeconds) : 0f;
                float maskHeight = buttonRect.height * t; // 由下往上遮罩，隨冷卻剩餘時間縮短
                Fill(new Rect(buttonRect.x, buttonRect.yMax - maskHeight, buttonRect.width, maskHeight), CooldownMaskColor);
                GUI.Label(buttonRect, IntStringCache.Get(Mathf.CeilToInt(remaining)), _label);
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
