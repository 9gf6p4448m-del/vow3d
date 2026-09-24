using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V080_CAPTURE_PLAN.md §3-A：V-A01（CaptureTuning 字面值）、V-A02（板塊幾何與場地）、
    // V-A03（點→板塊）、V-A04（點→光圈）。全部凍結，期望值一律寫死字面值。
    public sealed class CaptureGeometryTests
    {
        [Test]
        public void VA01_CaptureTuning_HasTheFrozenValues()
        {
            var t = new CaptureTuning();
            Assert.AreEqual(2.5f, t.CircleRadius);
            Assert.AreEqual(3.5f, t.CaptureSeconds);
            Assert.AreEqual(2, t.ScorePerTilePerTick);
            Assert.AreEqual(1000, t.WinScore);
            Assert.AreEqual(5.0f, t.RespawnSeconds);
            Assert.AreEqual(3.0f, t.EndPauseSeconds);
            Assert.AreEqual(6.0f, t.ChaseStartDistance);
            Assert.AreEqual(10.0f, t.ChaseGiveUpDistance);
        }

        [Test]
        public void VA02_SevenTileCenters_AndBases_MatchTheFrozenLayout()
        {
            Assert.AreEqual(0f, HexBoardLayout.CenterX(0)); Assert.AreEqual(0f, HexBoardLayout.CenterZ(0));
            Assert.AreEqual(0f, HexBoardLayout.CenterX(1)); Assert.AreEqual(12.125f, HexBoardLayout.CenterZ(1));
            Assert.AreEqual(10.5f, HexBoardLayout.CenterX(2)); Assert.AreEqual(6.0625f, HexBoardLayout.CenterZ(2));
            Assert.AreEqual(10.5f, HexBoardLayout.CenterX(3)); Assert.AreEqual(-6.0625f, HexBoardLayout.CenterZ(3));
            Assert.AreEqual(0f, HexBoardLayout.CenterX(4)); Assert.AreEqual(-12.125f, HexBoardLayout.CenterZ(4));
            Assert.AreEqual(-10.5f, HexBoardLayout.CenterX(5)); Assert.AreEqual(-6.0625f, HexBoardLayout.CenterZ(5));
            Assert.AreEqual(-10.5f, HexBoardLayout.CenterX(6)); Assert.AreEqual(6.0625f, HexBoardLayout.CenterZ(6));

            float maxAbsX = 0f, maxAbsZ = 0f;
            for (int tile = 0; tile < HexBoardLayout.TileCount; tile++)
            {
                for (int vertex = 0; vertex < 6; vertex++)
                {
                    float vx = HexBoardLayout.VertexX(tile, vertex);
                    float vz = HexBoardLayout.VertexZ(tile, vertex);
                    Assert.LessOrEqual(vx < 0f ? -vx : vx, 19.0f, "頂點 x 超出留邊安全值");
                    Assert.LessOrEqual(vz < 0f ? -vz : vz, 19.0f, "頂點 z 超出留邊安全值");
                    if ((vx < 0f ? -vx : vx) > maxAbsX) maxAbsX = vx < 0f ? -vx : vx;
                    if ((vz < 0f ? -vz : vz) > maxAbsZ) maxAbsZ = vz < 0f ? -vz : vz;
                }
            }
            Assert.AreEqual(17.5f, maxAbsX);
            Assert.AreEqual(18.1875f, maxAbsZ);

            Assert.AreEqual(4, HexBoardLayout.BlueBaseTile);
            Assert.AreEqual(1, HexBoardLayout.RedBaseTile);
        }

        [Test]
        public void VA03_TileAt_BoundaryInclusive_SmallestIndexOnOverlap()
        {
            Assert.AreEqual(0, HexBoardLayout.TileAt(0f, 0f));
            Assert.AreEqual(0, HexBoardLayout.TileAt(0f, 6.0625f));       // 0/1 共用邊中點
            Assert.AreEqual(1, HexBoardLayout.TileAt(0f, 6.125f));
            Assert.AreEqual(2, HexBoardLayout.TileAt(10.5f, 0f));         // 2/3 共用邊中點
            Assert.AreEqual(3, HexBoardLayout.TileAt(10.5f, -0.0625f));
            Assert.AreEqual(0, HexBoardLayout.TileAt(7f, 0f));            // 0/2/3 三塊交點
            Assert.AreEqual(0, HexBoardLayout.TileAt(5.25f, 3.03125f));   // 0/2 斜邊上的點
            Assert.AreEqual(2, HexBoardLayout.TileAt(5.3125f, 3.03125f));
            Assert.AreEqual(2, HexBoardLayout.TileAt(17.5f, 6.0625f));    // 外緣頂點
            Assert.AreEqual(-1, HexBoardLayout.TileAt(17.5625f, 6.0625f));
            Assert.AreEqual(-1, HexBoardLayout.TileAt(19f, 19f));
        }

        [Test]
        public void VA04_CircleAt_BoundaryInclusive()
        {
            const float r = 2.5f;
            Assert.AreEqual(0, HexBoardLayout.CircleAt(2.5f, 0f, r));
            Assert.AreEqual(-1, HexBoardLayout.CircleAt(2.5625f, 0f, r));
            Assert.AreEqual(2, HexBoardLayout.CircleAt(13.0f, 6.0625f, r));
            Assert.AreEqual(-1, HexBoardLayout.CircleAt(13.0625f, 6.0625f, r));
            Assert.AreEqual(1, HexBoardLayout.CircleAt(0f, 14.625f, r));
            Assert.AreEqual(-1, HexBoardLayout.CircleAt(0f, 14.6875f, r));
            Assert.AreEqual(5, HexBoardLayout.CircleAt(-10.5f, -3.5625f, r));
            Assert.AreEqual(-1, HexBoardLayout.CircleAt(-10.5f, -3.5f, r));
        }
    }
}
