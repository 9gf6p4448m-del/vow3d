using UnityEngine;
using UnityEngine.Rendering;
using Vow.Core.Logic;

namespace Vow.Bootstrap
{
    // GRID 除錯疊圖：把阻擋格點裡 Blocked 的格子畫成一張貼地的四邊形 Mesh（計畫書 §4 假設 10）。
    // 使用者用手機試玩、回報「繞得很怪」時，截圖要看得到格點才歸因得出來。
    //
    // 三個硬性條件：
    // ① 預設關閉——零配置量測在 GRID 關閉下進行，關著時這個元件每幀什麼都不做。
    // ② 執行期不得 CreatePrimitive（IL2CPP 剔除）——物件由 VOWPhase1SceneBuilder 預建，這裡只填 Mesh。
    // ③ 重填也不得配置——頂點與索引陣列在 Initialize 一次配好，之後只寫入既有陣列，且只在格點版本變動時重填。
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class NavGridDebugView : MonoBehaviour
    {
        private const float QuadHeight = 0.06f; // 貼在地板上方一點，避免 z-fighting
        private const float CellGap = 0.04f;    // 每格四邊留縫，看得出一格一格

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private BlockGrid _grid;
        private Mesh _mesh;
        private Vector3[] _vertices;
        private int[] _indices;
        private float _cellHalf;
        private int _builtVersion = -1;

        // 目前畫出來的四邊形數；接上格點且已重填時應等於 BlockGrid.BlockedCount。
        public int QuadCount { get; private set; }

        public bool Visible
        {
            get { return _renderer != null && _renderer.enabled; }
            set { if (_renderer != null) _renderer.enabled = value; }
        }

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            _renderer.enabled = false; // 預設關
        }

        public void Initialize(BlockGrid grid)
        {
            _grid = grid;
            if (grid == null) return;

            // BlockGrid 沒有公開格寬（步驟 A 的檔案不在本批可改範圍內），從相鄰兩格的格心差推回來。
            grid.CellCenter(0, 0, out float x0, out _);
            grid.CellCenter(1, 0, out float x1, out _);
            _cellHalf = Mathf.Max(0.01f, (x1 - x0) * 0.5f - CellGap * 0.5f);

            int cellCount = grid.Columns * grid.Rows;
            _vertices = new Vector3[cellCount * 4];
            _indices = new int[cellCount * 6];
            for (int i = 0; i < cellCount; i++)
            {
                int v = i * 4;
                int t = i * 6;
                _indices[t] = v;
                _indices[t + 1] = v + 1;
                _indices[t + 2] = v + 2;
                _indices[t + 3] = v;
                _indices[t + 4] = v + 2;
                _indices[t + 5] = v + 3;
            }

            _mesh = new Mesh { name = "NavGridDebug", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;
            _builtVersion = -1;
            QuadCount = 0;
        }

        // LateUpdate 而非 Update：這一幀所有牆的登記／撤銷都跑完了才重填，不會畫到半套的格點。
        private void LateUpdate()
        {
            if (_grid == null || _mesh == null) return;
            if (!_renderer.enabled) return;                 // 關著＝零工作量（零配置量測的前提）
            if (_builtVersion == _grid.Version) return;     // 沒有格子翻轉就不重填
            Rebuild();
        }

        private void Rebuild()
        {
            _builtVersion = _grid.Version;

            int quads = 0;
            for (int cz = 0; cz < _grid.Rows; cz++)
            {
                for (int cx = 0; cx < _grid.Columns; cx++)
                {
                    if (!_grid.IsBlocked(cx, cz)) continue;
                    _grid.CellCenter(cx, cz, out float x, out float z);
                    int v = quads * 4;
                    _vertices[v] = new Vector3(x - _cellHalf, QuadHeight, z - _cellHalf);
                    _vertices[v + 1] = new Vector3(x - _cellHalf, QuadHeight, z + _cellHalf);
                    _vertices[v + 2] = new Vector3(x + _cellHalf, QuadHeight, z + _cellHalf);
                    _vertices[v + 3] = new Vector3(x + _cellHalf, QuadHeight, z - _cellHalf);
                    quads++;
                }
            }
            QuadCount = quads;

            // 先 Clear 再寫：頂點數變少時，舊的索引會指到已經不存在的頂點。
            _mesh.Clear(false);
            _mesh.SetVertices(_vertices, 0, quads * 4);
            _mesh.SetIndices(_indices, 0, quads * 6, MeshTopology.Triangles, 0, true);
        }
    }
}
