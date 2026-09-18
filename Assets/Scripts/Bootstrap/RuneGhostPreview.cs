using System;
using UnityEngine;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Bootstrap
{
    // 拖曳中的半透明石牆虛影（先例：CadenceAimPreview）。純本地回饋：直接訂閱未經延遲注入的符印事件，
    // 鬆手／取消／極速施放都要立即隱藏，不等網路延遲佇列（計畫書 §4 假設 11：虛影與取消不延遲）。
    //
    // 使用者 2026-09-19 試玩 v0.3.1 後裁定：畫面上不出現任何取消 UI，改成「滑回按下的位置就是取消」——
    // 這裡用虛影變色當唯一提示：isCancelArmed 為真（此刻放手會取消）時整面牆變紅，鬆手或離開該範圍變回原色。
    // 上色走 MaterialPropertyBlock（建一次、重用；先例 DummyTarget.ApplyColor），不碰共用材質資產本身。
    public sealed class RuneGhostPreview : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly Color CancelColorRgb = new Color(0.95f, 0.15f, 0.15f);

        [SerializeField] private Renderer _visualRenderer;

        private IPlayerInputService _input;
        private IRuneCastInput _releaseInput;
        private Transform _hero;
        private Transform _cameraTransform;
        private RuneCastLogic _placement;
        private RuneTuning _tuning;
        private Func<bool> _isCancelArmed;
        private bool _subscribed;

        private MaterialPropertyBlock _block;
        private Color _normalColor = Color.white;
        private Color _cancelColor = Color.red;

        // 供測試斷言：目前是不是套著取消色、以及實際套用的顏色（讀 PropertyBlock 回來的值，不是自己記帳的旗標）。
        public bool ShowingCancelColor { get; private set; }
        public Color CurrentColor { get; private set; }

        // isCancelArmed：「此刻放手會不會取消」的查詢來源，比照 RuneButtonView 拿冷卻的 Func<float> 做法。
        // 可為 null（永遠當作 false，不變色）——RuneCaster／RuneWall 沒有等效物可以問，這是 RuneGhostPreview 專屬的狀態。
        public void Initialize(IPlayerInputService input, IRuneCastInput releaseInput, Transform hero, Camera worldCamera,
            RuneTuning tuning, Func<bool> isCancelArmed = null)
        {
            Unsubscribe();

            _input = input;
            _releaseInput = releaseInput;
            _hero = hero;
            _cameraTransform = worldCamera != null ? worldCamera.transform : null;
            _placement = new RuneCastLogic(tuning);
            _tuning = tuning;
            _isCancelArmed = isCancelArmed;

            EnsureColorSetup();
            Subscribe();
            Hide();
        }

        // 正常色直接讀共用材質目前設定的顏色（不寫死重複一份）；取消色維持同樣的透明度、只換色相。
        private void EnsureColorSetup()
        {
            if (_block == null) _block = new MaterialPropertyBlock();

            Material shared = _visualRenderer != null ? _visualRenderer.sharedMaterial : null;
            if (shared != null && shared.HasProperty(BaseColorId)) _normalColor = shared.GetColor(BaseColorId);
            else if (shared != null && shared.HasProperty(LegacyColorId)) _normalColor = shared.GetColor(LegacyColorId);

            _cancelColor = new Color(CancelColorRgb.r, CancelColorRgb.g, CancelColorRgb.b, _normalColor.a);
            ShowingCancelColor = false;
            ApplyColor(_normalColor);
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (_input != null)
            {
                _input.OnRuneVectorDragUpdated += HandleDragUpdated;
                _input.OnRuneCastCancelled += HandleHide;
                _input.OnRuneQuickCastTriggered += HandleHide;
            }
            if (_releaseInput != null) _releaseInput.OnRuneCastReleased += HandleReleased;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_input != null)
            {
                _input.OnRuneVectorDragUpdated -= HandleDragUpdated;
                _input.OnRuneCastCancelled -= HandleHide;
                _input.OnRuneQuickCastTriggered -= HandleHide;
            }
            if (_releaseInput != null) _releaseInput.OnRuneCastReleased -= HandleReleased;
            _subscribed = false;
            _input = null;
            _releaseInput = null;
        }

        private void HandleDragUpdated(Vector2 screenDirection, float distance01)
        {
            if (_hero == null || _placement == null) { Hide(); return; }

            Vector3 worldDir = RuneCaster.ScreenToWorldGroundDirection(screenDirection, _cameraTransform);
            Vector3 heroPosition = _hero.position;
            if (!_placement.TryDragPlacement(heroPosition.x, heroPosition.z, worldDir.x, worldDir.z, distance01, out RuneWallPlacement placement))
            {
                Hide();
                return;
            }

            // 虛影中心 y 要跟實牆（RuneCaster.SpawnWall）同一個公式，否則虛影半截埋在地板下。
            float centerY = heroPosition.y + (_tuning != null ? _tuning.WallHeight * 0.5f : 0f);
            Vector3 center = new Vector3(placement.CenterX, centerY, placement.CenterZ);
            Quaternion rotation = Quaternion.LookRotation(new Vector3(placement.NormalX, 0f, placement.NormalZ), Vector3.up);
            transform.SetPositionAndRotation(center, rotation);
            Show();
        }

        private void HandleReleased(Vector2 screenDirection, float distance01)
        {
            Hide();
        }

        private void HandleHide()
        {
            Hide();
        }

        // 虛影可見期間每幀查一次「此刻放手會不會取消」，狀態改變時才換色——不必每幀都重設 PropertyBlock。
        private void LateUpdate()
        {
            if (_visualRenderer == null || !_visualRenderer.enabled) return;

            bool armed = _isCancelArmed != null && _isCancelArmed();
            if (armed == ShowingCancelColor) return;

            ApplyColor(armed ? _cancelColor : _normalColor);
            ShowingCancelColor = armed;
        }

        private void ApplyColor(Color color)
        {
            CurrentColor = color;
            if (_visualRenderer == null || _block == null) return;

            _visualRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(LegacyColorId, color);
            _visualRenderer.SetPropertyBlock(_block);
        }

        private void Show()
        {
            if (_visualRenderer != null) _visualRenderer.enabled = true;
        }

        private void Hide()
        {
            if (_visualRenderer != null) _visualRenderer.enabled = false;
            if (ShowingCancelColor)
            {
                ApplyColor(_normalColor);
                ShowingCancelColor = false;
            }
        }
    }
}
