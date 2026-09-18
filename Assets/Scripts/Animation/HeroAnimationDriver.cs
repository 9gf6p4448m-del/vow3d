using UnityEngine;
using Vow.Core;

namespace Vow.Animation
{
    // 掛在「帶 Animator 的模型物件」上（動畫事件只會送到 Animator 所在的 GameObject）。
    // 職責兩件：① 把狀態機的狀態變化翻成動畫播放；② 接住 OnAttackHit() 動畫事件並轉送給狀態機。
    // 傷害判定幀由動畫驅動而非計時器——換了揮刀動畫，手感時序自動跟著動畫走（紅線 3）。
    [RequireComponent(typeof(Animator))]
    public sealed class HeroAnimationDriver : MonoBehaviour, IHitstopParticipant
    {
        private const float LocomotionBlendSeconds = 0.08f;
        private const float DashBlendSeconds = 0.02f; // 切後搖要「0 幀」的體感，混合時間壓到最短

        private Animator _animator;
        private HeroController _hero;
        private IAttackHitReceiver _hitReceiver;
        private bool _subscribed;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _hero = GetComponentInParent<HeroController>();
            _hitReceiver = _hero;

            if (_hero == null)
                Debug.LogError("[VOW] HeroAnimationDriver 的父層找不到 HeroController。", this);

            if (_animator.runtimeAnimatorController == null)
                Debug.LogError("[VOW] Animator 沒有指派 Controller，請執行 VOW/Phase 1/Build Greybox Scene。", this);
            else if (!_animator.isHuman)
                Debug.LogWarning("[VOW] 目前使用的是佔位骨架（非 Humanoid Avatar）。" +
                                 "請將 Mixamo Y-Bot／X-Bot 的 Humanoid FBX 放進 Assets/Art/Characters 後重新建置場景。", this);
        }

        private void Start()
        {
            if (_hero == null) return;
            _hero.StateMachine.OnStateChanged += HandleStateChanged;
            _hero.OnAttackWindupStarted += HandleWindupStarted;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (!_subscribed || _hero == null) return;
            _hero.StateMachine.OnStateChanged -= HandleStateChanged;
            _hero.OnAttackWindupStarted -= HandleWindupStarted;
        }

        // ── 動畫事件入口：Attack 切片的傷害判定幀 ──
        // 函式名必須等於 HeroAnimatorContract.AttackHitEvent。
        public void OnAttackHit()
        {
            if (_hitReceiver != null) _hitReceiver.NotifyAttackHit();
        }

        public void SetHitstopFrozen(bool frozen)
        {
            _animator.speed = frozen ? 0f : 1f;
        }

        public void PlayHitReaction()
        {
            _animator.CrossFadeInFixedTime(HeroAnimatorContract.HitHash, LocomotionBlendSeconds);
        }

        // 每一刀都從第 0 幀重播（連續攻擊同一個狀態時 CrossFade 不會重新起播，所以用 Play）。
        private void HandleWindupStarted()
        {
            _animator.Play(HeroAnimatorContract.AttackHash, 0, 0f);
        }

        private void HandleStateChanged(PlayerState oldState, PlayerState newState)
        {
            switch (newState)
            {
                case PlayerState.Idle:
                    _animator.CrossFadeInFixedTime(HeroAnimatorContract.IdleHash, LocomotionBlendSeconds);
                    break;

                case PlayerState.Moving:
                    _animator.CrossFadeInFixedTime(HeroAnimatorContract.RunHash, LocomotionBlendSeconds);
                    break;

                case PlayerState.CadenceDashing:
                    _animator.CrossFadeInFixedTime(HeroAnimatorContract.DashHash, DashBlendSeconds);
                    break;

                // AttackWindup：由 HandleWindupStarted 處理
                // AttackRelease／AttackRecovery：Attack 切片自己播完收刀的部分，不介入
            }
        }
    }
}
