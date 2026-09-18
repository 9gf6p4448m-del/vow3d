using UnityEngine;

namespace Vow.Animation
{
    // Animator Controller 與程式之間的唯一契約。Editor 端的建置器與 Runtime 端的驅動器都引用這裡，
    // 改名只需要改一處——狀態名、事件名對不上是「動畫事件沒觸發」最常見的原因。
    public static class HeroAnimatorContract
    {
        // 灰盒 5 大動作切片（ARCHITECTURE §陸-4）
        public const string IdleState = "Idle";
        public const string RunState = "Run";
        public const string AttackState = "Attack";
        public const string DashState = "Dash";
        public const string HitState = "Hit";

        // 普攻傷害判定幀的動畫事件函式名：必須與 HeroAnimationDriver.OnAttackHit() 完全一致
        public const string AttackHitEvent = "OnAttackHit";

        public static readonly int IdleHash = Animator.StringToHash(IdleState);
        public static readonly int RunHash = Animator.StringToHash(RunState);
        public static readonly int AttackHash = Animator.StringToHash(AttackState);
        public static readonly int DashHash = Animator.StringToHash(DashState);
        public static readonly int HitHash = Animator.StringToHash(HitState);
    }
}
