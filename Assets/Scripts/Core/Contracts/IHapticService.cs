using Vow.Core.Logic;

namespace Vow.Core
{
    // 震覺反饋的抽象（ARCHITECTURE §貳：CoreHaptics 震覺疲勞管理）。
    // 呼叫端只回報「發生了什麼事」，要不要震、震多重由實作端的疲勞策略決定。
    public interface IHapticService
    {
        void Notify(HapticCue cue);
    }
}
