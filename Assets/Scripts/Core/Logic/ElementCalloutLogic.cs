namespace Vow.Core.Logic
{
    // 反應成立時該跳哪個飄字、HUD 的 STATE 列此刻該顯示什麼（V061_FEEDBACK_PLAN.md §1、§3 F1）。
    // 零 UnityEngine：純函式，ReactionCalloutDisplay／ElementField／DebugHud／Phase1Bootstrap 共用同一份判準。
    public static class ElementCalloutLogic
    {
        // 飄字標籤表的索引（ReactionCalloutDisplay 的標籤表照這個順序建：0..4 是五個反應，5 是 ROOTED）。
        public const int QuicksandLabelIndex = 0;
        public const int SteamLabelIndex = 1;
        public const int FirestormLabelIndex = 2;
        public const int BoilLabelIndex = 3;
        public const int RescueLabelIndex = 4;
        public const int RootedLabelIndex = 5;
        public const int NoCalloutIndex = -1;

        // 五個反應各對應一個飄字索引；None／PlainFire／WaterPool 不跳字。
        public static int LabelIndexFor(ElementReaction reaction)
        {
            switch (reaction)
            {
                case ElementReaction.Quicksand: return QuicksandLabelIndex;
                case ElementReaction.Steam: return SteamLabelIndex;
                case ElementReaction.Firestorm: return FirestormLabelIndex;
                case ElementReaction.Boil: return BoilLabelIndex;
                case ElementReaction.Rescue: return RescueLabelIndex;
                case ElementReaction.PlainFire: return NoCalloutIndex;
                default: return NoCalloutIndex;
            }
        }

        // DebugHud 的 STATE 列：縛足優先於減速；兩者皆無時回 0（照舊顯示狀態機名稱）。
        public const int NormalStatus = 0;
        public const int RootedStatus = 1;
        public const int SlowedStatus = 2;

        public static int HeroStatusIndex(bool isRooted, float speedMultiplier)
        {
            if (isRooted) return RootedStatus;
            if (speedMultiplier < 1f) return SlowedStatus;
            return NormalStatus;
        }
    }
}
