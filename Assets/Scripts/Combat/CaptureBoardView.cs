using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // v0.8.0 七塊板塊的顯示（V080_CAPTURE_PLAN.md §2.1-4、E30）。只讀 ICaptureMatchView，不碰 CaptureMatchLogic。
    //
    // 全部物件由 VOWPhase1SceneBuilder 預建（執行期禁止 CreatePrimitive）：7 塊地板、7 座塔、7 個光圈、7 個進度盤，
    // 陣列索引＝板塊索引（E3）。換色只換 sharedMaterial（讀 renderer.material 會複製材質＝配置），
    // 而且只在歸屬真的變了才換；進度盤每塔一個（7 個 > 同時引導上限 2），不共用池。
    // 沒有任何 Collider（E23）：不擋路、不吃點擊、不進 BlockGrid。
    public sealed class CaptureBoardView : MonoBehaviour
    {
        private const float DiscHeightScale = 0.01f;

        [SerializeField] private Renderer[] _floors = new Renderer[HexBoardLayout.TileCount];
        [SerializeField] private Renderer[] _towers = new Renderer[HexBoardLayout.TileCount];
        [SerializeField] private Renderer[] _rings = new Renderer[HexBoardLayout.TileCount];
        [SerializeField] private Renderer[] _progressDiscs = new Renderer[HexBoardLayout.TileCount];
        [SerializeField] private Material _neutralMaterial;
        [SerializeField] private Material _blueMaterial;
        [SerializeField] private Material _redMaterial;

        private readonly int[] _shownOwner = { -1, -1, -1, -1, -1, -1, -1 };
        // 進度盤的 Transform 在 Initialize 抓一次：第一次讀某個 Component.transform 時 Unity 才建出它的 managed 包裝
        // （實測每個 40 bytes）——若等到對局中那座塔第一次有人引導才讀，就會在 LateUpdate 裡配置（V-C03 量到）。
        private readonly Transform[] _discTransforms = new Transform[HexBoardLayout.TileCount];
        private ICaptureMatchView _view;
        private CaptureTuning _tuning;

        // 地板／光圈換色的累計次數（零配置量測的活性，V-C03）。
        public int MaterialSwapCount { get; private set; }

        public Renderer Floor(int tile) => _floors[tile];
        public Renderer Tower(int tile) => _towers[tile];
        public Renderer Ring(int tile) => _rings[tile];
        public Renderer ProgressDisc(int tile) => _progressDiscs[tile];
        public int FloorCount => _floors != null ? _floors.Length : 0;
        public int TowerCount => _towers != null ? _towers.Length : 0;
        public int RingCount => _rings != null ? _rings.Length : 0;
        public int ProgressDiscCount => _progressDiscs != null ? _progressDiscs.Length : 0;
        public Material NeutralMaterial => _neutralMaterial;
        public Material BlueMaterial => _blueMaterial;
        public Material RedMaterial => _redMaterial;
        public bool IsShown => gameObject.activeSelf;

        public void Initialize(ICaptureMatchView view, CaptureTuning tuning)
        {
            _view = view;
            _tuning = tuning;
            // 起始狀態＝全部中立（與場景建置器預建的材質相同），在這裡明寫一次；之後只在歸屬真的變了才換色，
            // 進入佔領待機時不必把 21 個物件全部重設一遍。
            for (int i = 0; i < _shownOwner.Length; i++)
            {
                _shownOwner[i] = (int)Faction.Neutral;
                if (_floors[i] != null) _floors[i].sharedMaterial = _neutralMaterial;
                if (_rings[i] != null) _rings[i].sharedMaterial = _neutralMaterial;
                if (_towers[i] != null) _towers[i].sharedMaterial = _neutralMaterial;
            }
            for (int i = 0; i < _discTransforms.Length; i++)
                _discTransforms[i] = _progressDiscs[i] != null ? _progressDiscs[i].transform : null;
        }

        // 整組開關（Off 時整組不啟用，V-B01）。開啟當下立刻對一次顏色，不等到 LateUpdate。
        public void SetShown(bool shown)
        {
            if (gameObject.activeSelf != shown) gameObject.SetActive(shown);
            if (shown) Refresh();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_view == null || _tuning == null) return;

            for (int i = 0; i < HexBoardLayout.TileCount; i++)
            {
                int owner = (int)_view.OwnerOf(i);
                if (owner != _shownOwner[i])
                {
                    _shownOwner[i] = owner;
                    Material material = MaterialFor(owner);
                    if (_floors[i] != null) _floors[i].sharedMaterial = material;
                    if (_rings[i] != null) _rings[i].sharedMaterial = material;
                    if (_towers[i] != null) _towers[i].sharedMaterial = material;
                    MaterialSwapCount++;
                }
                RefreshDisc(i);
            }
        }

        // 進度盤半徑＝光圈半徑 × 進度 ÷ 引導秒數；引導中的一方用自己的顏色（E30）。
        private void RefreshDisc(int tile)
        {
            Renderer disc = _progressDiscs[tile];
            if (disc == null) return;

            float progress = 0f;
            Material material = null;
            if (_view.BlueChannelingTile == tile && _view.BlueChannelProgress > 0f)
            {
                progress = _view.BlueChannelProgress;
                material = _blueMaterial;
            }
            else if (_view.RedChannelingTile == tile && _view.RedChannelProgress > 0f)
            {
                progress = _view.RedChannelProgress;
                material = _redMaterial;
            }

            if (material == null)
            {
                if (disc.enabled) disc.enabled = false;
                return;
            }

            float radius = _tuning.CircleRadius * progress / _tuning.CaptureSeconds;
            if (radius > _tuning.CircleRadius) radius = _tuning.CircleRadius;
            _discTransforms[tile].localScale = new Vector3(radius * 2f, DiscHeightScale, radius * 2f);
            if (disc.sharedMaterial != material) disc.sharedMaterial = material;
            if (!disc.enabled) disc.enabled = true;
        }

        private Material MaterialFor(int owner)
        {
            if (owner == (int)Faction.BlueTeam) return _blueMaterial;
            if (owner == (int)Faction.RedTeam) return _redMaterial;
            return _neutralMaterial;
        }
    }
}
