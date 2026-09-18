using UnityEngine;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Bootstrap
{
    // 拖曳中的半透明石牆虛影（先例：CadenceAimPreview）。純本地回饋：直接訂閱未經延遲注入的符印事件，
    // 鬆手／取消／極速施放都要立即隱藏，不等網路延遲佇列（計畫書 §4 假設 11：虛影與取消不延遲）。
    public sealed class RuneGhostPreview : MonoBehaviour
    {
        [SerializeField] private Renderer _visualRenderer;

        private IPlayerInputService _input;
        private IRuneCastInput _releaseInput;
        private Transform _hero;
        private Transform _cameraTransform;
        private RuneCastLogic _placement;
        private RuneTuning _tuning;
        private bool _subscribed;

        public void Initialize(IPlayerInputService input, IRuneCastInput releaseInput, Transform hero, Camera worldCamera, RuneTuning tuning)
        {
            Unsubscribe();

            _input = input;
            _releaseInput = releaseInput;
            _hero = hero;
            _cameraTransform = worldCamera != null ? worldCamera.transform : null;
            _placement = new RuneCastLogic(tuning);
            _tuning = tuning;

            Subscribe();
            Hide();
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

            // r1 對抗審查 H1：虛影中心 y 要跟實牆（RuneCaster.SpawnWall）同一個公式，否則虛影半截埋在地板下。
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

        private void Show()
        {
            if (_visualRenderer != null) _visualRenderer.enabled = true;
        }

        private void Hide()
        {
            if (_visualRenderer != null) _visualRenderer.enabled = false;
        }
    }
}
