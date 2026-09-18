using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests
{
    public sealed class InputRoutingTests
    {
        private const float W = 2796f;
        private const float H = 1290f;

        private static InputRoutingManager NewRouter(out int buttonId)
        {
            InputRoutingManager router = new InputRoutingManager();
            buttonId = router.RegisterUiRegion(new ScreenRegion(40f, 1100f, 300f, 1200f));
            router.SetPipZone(new ScreenRegion(0f, 0f, 760f, 760f));
            return router;
        }

        [Test] // A14：死區優先於一切，包含微輪盤與 UI
        public void EdgeDeadzone_WinsOverEverything()
        {
            InputRoutingManager router = NewRouter(out int _);

            Assert.AreEqual(TouchRoute.Rejected, router.Route(3f, 400f, W, H, ControlMode.ModeB_DualZonePip, out int _), "左緣（落在微輪盤區內）");
            Assert.AreEqual(TouchRoute.Rejected, router.Route(W - 3f, 600f, W, H, ControlMode.ModeA_FullScreenFlick, out int _), "右緣");
            Assert.AreEqual(TouchRoute.Rejected, router.Route(1400f, 5f, W, H, ControlMode.ModeA_FullScreenFlick, out int _), "底緣");
            Assert.AreEqual(TouchRoute.World, router.Route(1400f, 9f, W, H, ControlMode.ModeA_FullScreenFlick, out int _), "8px 之外放行");
        }

        [Test]
        public void UiRegion_BlocksPenetrationToWorld_AndReportsItsId()
        {
            InputRoutingManager router = NewRouter(out int buttonId);

            TouchRoute route = router.Route(100f, 1150f, W, H, ControlMode.ModeA_FullScreenFlick, out int hitId);
            Assert.AreEqual(TouchRoute.UiRegion, route, "點到 UI 的手指不得滲透成點地移動");
            Assert.AreEqual(buttonId, hitId);

            router.SetUiRegionActive(buttonId, false);
            Assert.AreEqual(TouchRoute.World, router.Route(100f, 1150f, W, H, ControlMode.ModeA_FullScreenFlick, out int _), "停用後不再攔截");
        }

        [Test] // 高壓紅線：微輪盤只在模式 B 存在；模式 A 下同一位置是普通的世界觸控
        public void PipZone_OnlyExistsInModeB()
        {
            InputRoutingManager router = NewRouter(out int _);

            Assert.AreEqual(TouchRoute.Pip, router.Route(300f, 300f, W, H, ControlMode.ModeB_DualZonePip, out int _));
            Assert.AreEqual(TouchRoute.World, router.Route(300f, 300f, W, H, ControlMode.ModeA_FullScreenFlick, out int _));
            Assert.AreEqual(TouchRoute.World, router.Route(1800f, 300f, W, H, ControlMode.ModeB_DualZonePip, out int _), "微輪盤區外＝右手點擊區");
        }

        [Test]
        public void RegisteringBeyondCapacity_FailsLoudlyWithMinusOne()
        {
            InputRoutingManager router = new InputRoutingManager();
            for (int i = 0; i < InputRoutingManager.MaxUiRegions; i++)
                Assert.AreEqual(i, router.RegisterUiRegion(new ScreenRegion(0f, 0f, 1f, 1f)));

            Assert.AreEqual(-1, router.RegisterUiRegion(new ScreenRegion(0f, 0f, 1f, 1f)));
        }
    }
}
