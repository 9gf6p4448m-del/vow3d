using UnityEngine;
using Vow.Core;
using Vow.Input;

namespace Vow.Bootstrap
{
    // 預警指示器在 Phase 1 的實際用途：
    //   ① 模式 B 推著微輪盤時，在英雄腳下投射「下一次滑步會滑到哪」的指向箭頭（長度＝已套用動能衰減的距離）；
    //   ② 滑步成功的瞬間觸發吸附反饋 (Snap)；
    //   ③ 鎖定目標時以圓形法陣標出普攻射程。
    public sealed class CadenceAimPreview : MonoBehaviour
    {
        private const float ArrowWidth = 0.22f;
        private const float RangeRingThickness = 0.08f;
        private const float ArrowStartOffset = 0.65f; // 從身體外緣出發：箭頭畫在腳下會被模型整個蓋住

        private HeroController _hero;
        private PlayerInputService _input;
        private ISkillTelegraphService _telegraph;
        private Transform _cameraTransform;
        private Transform _heroTransform;
        private float _holdAfterSnap;

        public void Initialize(HeroController hero, PlayerInputService input, ISkillTelegraphService telegraph, Camera viewCamera)
        {
            _hero = hero;
            _input = input;
            _telegraph = telegraph;
            _cameraTransform = viewCamera != null ? viewCamera.transform : null;
            _heroTransform = hero != null ? hero.transform : null;

            if (_hero != null) _hero.CadenceMover.OnDashExecuted += HandleDashExecuted;
        }

        private void OnDestroy()
        {
            if (_hero != null) _hero.CadenceMover.OnDashExecuted -= HandleDashExecuted;
        }

        private void HandleDashExecuted(float distance)
        {
            if (_telegraph == null || !_telegraph.IsAiming) return;
            _telegraph.TriggerSnapFeedback();
            _holdAfterSnap = 0.12f;
        }

        private void LateUpdate()
        {
            if (_hero == null || _input == null || _telegraph == null) return;

            if (_holdAfterSnap > 0f)
            {
                _holdAfterSnap -= Time.deltaTime;
                return; // 讓吸附動畫播完再更新
            }

            Vector3 heroPosition = _heroTransform.position;
            bool pipActive = _input.ActiveMode == ControlMode.ModeB_DualZonePip && _input.HasPipVector;

            if (pipActive && _hero.CadenceMover.CurrentCharges > 0)
            {
                Vector3 direction = ScreenToWorldDirection(_input.PipDirection);
                _telegraph.ShowLineIndicator(heroPosition + direction * ArrowStartOffset, direction,
                    _hero.Mover.NextDashDistance, ArrowWidth);
                return;
            }

            ICombatTarget target = _hero.CurrentTarget;
            if (target != null && target.IsAlive)
            {
                _telegraph.ShowZoneIndicator(heroPosition, _hero.AttackRange, RangeRingThickness);
                return;
            }

            if (_telegraph.IsAiming) _telegraph.HideIndicator();
        }

        private Vector3 ScreenToWorldDirection(Vector2 screenDirection)
        {
            if (_cameraTransform == null) return new Vector3(screenDirection.x, 0f, screenDirection.y);

            Vector3 forward = _cameraTransform.forward;
            Vector3 right = _cameraTransform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            return right * screenDirection.x + forward * screenDirection.y;
        }
    }
}
