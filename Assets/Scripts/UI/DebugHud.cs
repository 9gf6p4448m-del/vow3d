using System;
using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.UI
{
    // Day 1 可視化調試 HUD（左上角）：FPS、PlayerState、充能點、操作模式切換、220ms 目押窗口倒數條、Hitbox 開關。
    //
    // 為什麼用 IMGUI：這是調試工具，不是正式 UI。關掉 GUILayout、固定 Rect、字串全部預先快取之後，
    // 每幀不產生 GC，不會污染 Profiler 對「遊戲本體 0 GC」的量測。
    // 按鈕不用 GUI.Button：點擊一律走 InputRoutingManager 的 UI 區域，全專案只有一條輸入路徑，
    // 同時保證「點到 HUD 的手指不會滲透成點地移動」。
    public sealed class DebugHud : MonoBehaviour
    {
        private const float ReferenceDpi = 160f;
        private const float FpsSampleSeconds = 0.25f;
        private const float Pad = 8f;
        private const float Row = 22f;
        private const float PanelWidth = 250f;
        private const float InfoRows = 8f;

        private static readonly string[] StateNames = Enum.GetNames(typeof(PlayerState));

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color PipOn = new Color(0.2f, 0.95f, 1f);
        private static readonly Color PipOff = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color WindowColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.45f, 0.9f, 0.85f);
        private static readonly Color ButtonOnColor = new Color(0.2f, 0.75f, 0.35f, 0.9f);

        private HeroController _hero;
        private PlayerInputService _input;
        private HitboxVisualizer _hitboxes;

        private GUIStyle _label;
        private GUIStyle _buttonLabel;
        private float _scale = 1f;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private int _modeRegion = -1;
        private int _hitboxRegion = -1;
        private Rect _modeRect;
        private Rect _hitboxRect;

        private float _fpsTimer;
        private int _fpsFrames;
        private int _fps;
        private string _stateName = StateNames[0];
        private string _rigLabel = "?";
        private Color _rigColor = Color.white;
        private int _refreshHz;

        public void Initialize(HeroController hero, PlayerInputService input, HitboxVisualizer hitboxes)
        {
            _hero = hero;
            _input = input;
            _hitboxes = hitboxes;

            if (_hero != null)
            {
                _hero.StateMachine.OnStateChanged += HandleStateChanged;

                // 常駐標示載體種類：盲測時沒有人會記得自己測的是佔位骨架還是正式的 Humanoid（紅線 3）
                Animator animator = _hero.GetComponentInChildren<Animator>();
                bool humanoid = animator != null && animator.isHuman;
                _rigLabel = humanoid ? "HUMANOID" : "PLACEHOLDER";
                _rigColor = humanoid ? Color.white : new Color(1f, 0.45f, 0.3f);
            }
            if (_input != null)
            {
                _input.OnUiRegionTapped += HandleRegionTapped;
                _modeRegion = _input.Routing.RegisterUiRegion(default);
                _hitboxRegion = _input.Routing.RegisterUiRegion(default);
            }
            RecalculateLayout();
        }

        private void Awake()
        {
            useGUILayout = false; // 關掉 Layout 階段：少跑一輪 OnGUI，也是 IMGUI 零配置的前提
        }

        private void OnDestroy()
        {
            if (_hero != null) _hero.StateMachine.OnStateChanged -= HandleStateChanged;
            if (_input != null) _input.OnUiRegionTapped -= HandleRegionTapped;
        }

        private void Update()
        {
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= FpsSampleSeconds)
            {
                _fps = Mathf.RoundToInt(_fpsFrames / _fpsTimer);
                // 面板回報的刷新率（不是實際送幀率，也不是觸控採樣率；Editor 下讀到的是桌面顯示器）。用途只有一個：
                // 讓測試者一眼看出這台裝置的面板上限是不是 120——面板只有 60 時，FPS 顯示再高也沒有意義。
                _refreshHz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
                _fpsFrames = 0;
                _fpsTimer = 0f;
            }

            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) RecalculateLayout();
        }

        private void HandleStateChanged(PlayerState oldState, PlayerState newState)
        {
            _stateName = StateNames[(int)newState]; // Enum.ToString() 會配置，改查預先建好的表
        }

        private void HandleRegionTapped(int regionId)
        {
            if (regionId == _modeRegion && _input != null)
            {
                _input.ActiveMode = _input.ActiveMode == ControlMode.ModeA_FullScreenFlick
                    ? ControlMode.ModeB_DualZonePip
                    : ControlMode.ModeA_FullScreenFlick;
            }
            else if (regionId == _hitboxRegion && _hitboxes != null)
            {
                _hitboxes.Visible = !_hitboxes.Visible;
            }
        }

        private void RecalculateLayout()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            float dpi = Screen.dpi;
            _scale = dpi > 0f ? Mathf.Max(1f, dpi / ReferenceDpi) : 1f;

            float y = Pad + Row * InfoRows + Pad;
            float buttonWidth = (PanelWidth - Pad * 3f) * 0.5f;
            _modeRect = new Rect(Pad * 2f, y, buttonWidth, Row * 1.6f);
            _hitboxRect = new Rect(Pad * 3f + buttonWidth, y, buttonWidth, Row * 1.6f);

            if (_input == null) return;
            _input.Routing.UpdateUiRegion(_modeRegion, ToScreenRegion(_modeRect));
            _input.Routing.UpdateUiRegion(_hitboxRegion, ToScreenRegion(_hitboxRect));
        }

        // IMGUI 座標（原點左上、已縮放）→ 螢幕座標（原點左下、像素）
        private ScreenRegion ToScreenRegion(Rect guiRect)
        {
            float xMin = guiRect.xMin * _scale;
            float xMax = guiRect.xMax * _scale;
            float yMax = Screen.height - guiRect.yMin * _scale;
            float yMin = Screen.height - guiRect.yMax * _scale;
            return new ScreenRegion(xMin, yMin, xMax, yMax);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

            float panelHeight = Pad + Row * InfoRows + Pad + Row * 1.6f + Pad;
            Fill(new Rect(Pad, Pad, PanelWidth, panelHeight), PanelColor);

            float x = Pad * 2f;
            float valueX = x + 84f;
            float y = Pad * 1.5f;

            GUI.Label(new Rect(x, y, PanelWidth - Pad * 2f, Row), VowVersion.Label, _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "FPS", _label);
            GUI.Label(new Rect(valueX, y, 160f, Row), IntStringCache.Get(_fps), _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "PANEL HZ", _label);
            GUI.Label(new Rect(valueX, y, 160f, Row), IntStringCache.Get(_refreshHz), _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "STATE", _label);
            GUI.Label(new Rect(valueX, y, 170f, Row), _stateName, _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "PIPS", _label);
            DrawPips(valueX, y + 4f);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "WINDOW", _label);
            DrawWindowBar(valueX, y + 6f);
            y += Row;

            bool modeB = _input != null && _input.ActiveMode == ControlMode.ModeB_DualZonePip;
            GUI.Label(new Rect(x, y, 84f, Row), "MODE", _label);
            GUI.Label(new Rect(valueX, y, 170f, Row), modeB ? "B  Pip + Tap" : "A  Full-Screen Flick", _label);

            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "RIG", _label);
            Color previousContent = GUI.contentColor;
            GUI.contentColor = _rigColor;
            GUI.Label(new Rect(valueX, y, 170f, Row), _rigLabel, _label);
            GUI.contentColor = previousContent;

            Fill(_modeRect, ButtonColor);
            GUI.Label(_modeRect, modeB ? "Switch to A" : "Switch to B", _buttonLabel);

            bool hitboxOn = _hitboxes != null && _hitboxes.Visible;
            Fill(_hitboxRect, hitboxOn ? ButtonOnColor : ButtonColor);
            GUI.Label(_hitboxRect, hitboxOn ? "Hitbox ON" : "Hitbox OFF", _buttonLabel);

            GUI.matrix = previous;

            if (modeB) DrawPipWidget();
        }

        private void DrawPips(float x, float y)
        {
            if (_hero == null) return;
            ICadenceMover mover = _hero.CadenceMover;
            int max = mover.MaxCharges;
            int current = mover.CurrentCharges;

            for (int i = 0; i < max; i++)
            {
                Rect pip = new Rect(x + i * 26f, y, 20f, 14f);
                Fill(pip, i < current ? PipOn : PipOff);

                // 正在回充的那一格：由左往右填滿
                if (i == current)
                {
                    pip.width *= mover.ChargeRecoveryNormalized;
                    Fill(pip, new Color(PipOn.r, PipOn.g, PipOn.b, 0.45f));
                }
            }
        }

        // 220ms 目押窗口倒數：命中瞬間滿格，隨時間縮短；條還在＝現在微彈就會切後搖。
        private void DrawWindowBar(float x, float y)
        {
            const float width = 150f;
            Fill(new Rect(x, y, width, 10f), PipOff);
            if (_hero == null) return;

            float remaining = _hero.CadenceWindowRemainingNormalized;
            if (remaining > 0f) Fill(new Rect(x, y, width * remaining, 10f), WindowColor);
        }

        // 模式 B 微輪盤：畫出左下判定區，以及按住時的浮動原點與 3.5mm／7.5mm 兩圈（以螢幕像素繪製，不套 HUD 縮放）。
        private void DrawPipWidget()
        {
            float zone = _input.PipZonePixels;
            Fill(new Rect(0f, Screen.height - zone, zone, zone), new Color(1f, 1f, 1f, 0.06f));

            if (!_input.IsPipHeld) return;

            Vector2 origin = _input.PipOrigin;
            float guiY = Screen.height - origin.y;
            float min = _input.FlickMinRadiusPixels;
            float max = _input.FlickMaxRadiusPixels;

            Fill(new Rect(origin.x - max, guiY - max, max * 2f, max * 2f), new Color(1f, 1f, 1f, 0.10f));
            Fill(new Rect(origin.x - min, guiY - min, min * 2f, min * 2f), new Color(0f, 0f, 0f, 0.35f));

            if (!_input.HasPipVector) return;
            Vector2 dir = _input.PipDirection;
            float knob = min * 0.8f;
            float kx = origin.x + dir.x * max;
            float ky = guiY - dir.y * max;
            Fill(new Rect(kx - knob * 0.5f, ky - knob * 0.5f, knob, knob), PipOn);
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

            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            _label.normal.textColor = Color.white;

            _buttonLabel = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
    }
}
