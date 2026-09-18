using System;
using UnityEngine;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.UI
{
    // 右下角地脈符印鈕（GDD §參-1）。IMGUI 自畫，不用 GUI.Button、不引入 EventSystem——
    // 全專案只有一條輸入路徑：整顆按鈕的觸控歸 InputRoutingManager 的 Rune 路由所有（見 RecalculateLayout 對
    // input.Routing.SetRuneZone 的呼叫）；手勢辨識與事件全部在 TouchGestureRouter／RuneGestureTracker／RuneCaster，
    // 這裡只負責畫面與登記區域。按鈕幾何一律向 RuneButtonLayout 要，不在這裡另算一份。
    //
    // 使用者 2026-09-19 試玩 v0.3.1 後裁定：畫面上不出現任何取消 UI（原本按鈕上方那根紅色取消柱很怪），
    // 拖曳中滑回按下的位置放手＝取消，用虛影變色提示（見 RuneGhostPreview）。這裡另外把拇指壓著的按鈕本體
    // 也同步變紅——玩家操作時眼睛多半盯著拇指底下，不是螢幕中央的虛影。
    public sealed class RuneButtonView : MonoBehaviour
    {
        private static readonly Color ReadyColor = new Color(0.25f, 0.55f, 0.85f, 0.9f);
        private static readonly Color CooldownBaseColor = new Color(0.15f, 0.18f, 0.22f, 0.85f);
        private static readonly Color CooldownMaskColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color CancelArmedColor = new Color(0.85f, 0.25f, 0.25f, 0.9f);

        private PlayerInputService _input;
        private RuneTuning _tuning;
        private Func<float> _cooldownRemainingSecondsProvider;

        private ScreenRegion _buttonRegion;
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

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            // PlayerInputService.PixelsPerMillimeter 一律回傳正值（dpi<=0 時退回 FallbackDpi 換算），
            // 不像舊版還要等 RuneSaturationPixels 就緒；沒有取消區之後版面計算跟拖曳飽和行程完全無關。
            RuneButtonLayout layout = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter);
            _buttonRegion = layout.Button;
            _layoutValid = true;

            _input.Routing.SetRuneZone(_buttonRegion);
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

            // 拖曳中、此刻放手會取消：按鈕整顆變紅，蓋過冷卻遮罩（正在拖曳表示上一面牆的冷卻早就不是玩家此刻關心的事）。
            bool cancelArmed = _input.IsRuneDragging && _input.IsRuneCancelArmed;
            if (cancelArmed)
            {
                Fill(buttonRect, CancelArmedColor);
                return;
            }

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
