using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // PHASE2_BATCH4_PLAN.md §5 V2-ae：45ms 陷阱。CombatFeedbackService.TriggerHitstop 會
    // Clamp(30,60)；把 45 打成 4.5 或 450 時，所有「有頓挫」的斷言照樣綠，只有這一條會紅。
    public sealed class ElementTuningTests
    {
        // V2-ae ComboHitstop_IsStrictlyInsideTheClampWindow_SoTheValueIsNotSilentlyRewritten
        [Test]
        public void ComboHitstop_IsStrictlyInsideTheClampWindow_SoTheValueIsNotSilentlyRewritten()
        {
            ElementTuning tuning = new ElementTuning();

            Assert.Greater(tuning.ComboHitstopMs, 30f,
                "45 必須嚴格大於 Clamp(30,60) 的下界，否則 CombatFeedbackService 會靜默改成 30");
            Assert.Less(tuning.ComboHitstopMs, 60f,
                "45 必須嚴格小於 Clamp(30,60) 的上界，否則 CombatFeedbackService 會靜默改成 60");
        }
    }
}
