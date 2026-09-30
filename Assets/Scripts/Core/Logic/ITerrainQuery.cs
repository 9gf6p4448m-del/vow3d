namespace Vow.Core.Logic
{
    // v0.14.0 雙層接口（docs/V0140_CANYON_PLAN.md §1.3，凍結）。本批恆為單層；第 2 批岩橋只新增實作、不改呼叫端。
    public enum TerrainClass { Plain = 0, Canyon = 1, Cliff = 2, Ramp = 3 }   // Plain＝中層與棋盤外

    public interface ITerrainQuery
    {
        int LayerCountAt(float x, float z);                    // 本批恆為 1
        float HeightAt(float x, float z, int layer);           // layer 0＝地面；第 2 批 layer 1＝橋面
        TerrainClass ClassAt(float x, float z, int layer);
        int ResolveLayer(float x, float z, float currentY);    // 由目前高度決定站哪一層；本批恆回 0
        bool IsSameFloor(float x0, float z0, int layer0, float x1, float z1, int layer1); // §5「同一樓地板」判定
    }
}
