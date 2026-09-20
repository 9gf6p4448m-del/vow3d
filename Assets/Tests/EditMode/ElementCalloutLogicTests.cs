using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // V061_FEEDBACK_PLAN.md §3 F1：反應飄字索引與 HUD 受困狀態索引的純邏輯判準。
    public sealed class ElementCalloutLogicTests
    {
        // F1-a LabelIndexFor_MapsTheFiveReactions_AndReturnsMinusOneForTheRest
        [Test]
        public void LabelIndexFor_MapsTheFiveReactions_AndReturnsMinusOneForTheRest()
        {
            int quicksand = ElementCalloutLogic.LabelIndexFor(ElementReaction.Quicksand);
            int steam = ElementCalloutLogic.LabelIndexFor(ElementReaction.Steam);
            int firestorm = ElementCalloutLogic.LabelIndexFor(ElementReaction.Firestorm);
            int boil = ElementCalloutLogic.LabelIndexFor(ElementReaction.Boil);
            int rescue = ElementCalloutLogic.LabelIndexFor(ElementReaction.Rescue);

            Assert.AreEqual(0, quicksand);
            Assert.AreEqual(1, steam);
            Assert.AreEqual(2, firestorm);
            Assert.AreEqual(3, boil);
            Assert.AreEqual(4, rescue);

            int[] indices = { quicksand, steam, firestorm, boil, rescue };
            for (int i = 0; i < indices.Length; i++)
                for (int j = i + 1; j < indices.Length; j++)
                    Assert.AreNotEqual(indices[i], indices[j], "五個反應的飄字索引必須互不相同");

            Assert.AreEqual(-1, ElementCalloutLogic.LabelIndexFor(ElementReaction.None), "None 不該跳字");
            Assert.AreEqual(-1, ElementCalloutLogic.LabelIndexFor(ElementReaction.PlainFire), "空地火不該跳字");
            Assert.AreEqual(-1, ElementCalloutLogic.LabelIndexFor(ElementReaction.WaterPool), "WaterPool 不該跳字");
        }

        // F1-b HeroStatusIndex_RootedWins_ThenSlowed_ThenNone
        [Test]
        public void HeroStatusIndex_RootedWins_ThenSlowed_ThenNone()
        {
            Assert.AreEqual(1, ElementCalloutLogic.HeroStatusIndex(true, 0.65f), "縛足優先於減速");
            Assert.AreEqual(1, ElementCalloutLogic.HeroStatusIndex(true, 1f), "縛足優先於滿速");
            Assert.AreEqual(2, ElementCalloutLogic.HeroStatusIndex(false, 0.65f), "未縛足但減速中");
            Assert.AreEqual(0, ElementCalloutLogic.HeroStatusIndex(false, 1f), "未縛足且滿速");
        }
    }
}
