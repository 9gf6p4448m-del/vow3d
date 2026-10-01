using System;
using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 符印施法主控（GDD §參-1）：訂閱符印事件 → RuneCastLogic 換算冷卻與落點 → 從預建池取一面 RuneWall 立牆。
    // 虛影（RuneGhostPreview）與按鈕（RuneButtonView）都是純本地回饋，各自獨立訂閱同一組輸入事件，不依賴這個類別。
    public sealed class RuneCaster : MonoBehaviour
    {
        private ITerrainQuery _terrain;
        public void SetTerrain(ITerrainQuery terrain) { _terrain = terrain; }
        private RuneTuning _tuning;
        private RuneCastLogic _castLogic;
        private RuneWallRoster _roster;
        private RuneWall[] _pool;
        private Transform _hero;
        private Transform _cameraTransform;
        private Faction _ownerFaction;

        private IPlayerInputService _input;
        private IRuneCastInput _releaseInput;

        // 給 RuneButtonView 畫冷卻遮罩用。
        public float CooldownRemaining => _castLogic != null ? (float)_castLogic.CooldownRemaining(Time.timeAsDouble) : 0f;

        // 實際拿到手的玩家石牆池。批 3 把敵方牆從 FindObjectsOfType 整批當池的做法裡切出去（§4-6），
        // 驗收（V4-o）必須看得到「組裝端到底交了哪幾面牆給玩家」，不能自己另外湊一份。
        public RuneWall[] Pool => _pool;

        public void ResetForRound()
        {
            if (_pool != null)
                for (int i = 0; i < _pool.Length; i++)
                    if (_pool[i] != null && _pool[i].IsAlive) _pool[i].CollapseWall(false);
            _castLogic?.ResetCooldown();
        }

        // input：拖曳更新／極速施放／取消（極速施放走延遲佇列時，input 應為包住 latency 的那一層）。
        // releaseInput：鬆手成牆，因 ARCHITECTURE 的 IPlayerInputService 沒有這個事件而另立 IRuneCastInput。
        public void Initialize(IPlayerInputService input, IRuneCastInput releaseInput, Transform hero, Camera worldCamera,
            RuneTuning tuning, RuneWall[] pool, Faction ownerFaction)
        {
            Unsubscribe();

            _tuning = tuning;
            _castLogic = new RuneCastLogic(tuning);
            _roster = new RuneWallRoster(tuning.TeamWallCap);
            _pool = pool;
            _hero = hero;
            _cameraTransform = worldCamera != null ? worldCamera.transform : null;
            _ownerFaction = ownerFaction;

            if (_pool != null)
            {
                for (int i = 0; i < _pool.Length; i++)
                    if (_pool[i] != null) _pool[i].Initialize(tuning);
            }

            _input = input;
            _releaseInput = releaseInput;
            Subscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_input != null) _input.OnRuneQuickCastTriggered += HandleQuickCast;
            if (_releaseInput != null) _releaseInput.OnRuneCastReleased += HandleReleased;
        }

        private void Unsubscribe()
        {
            if (_input != null) _input.OnRuneQuickCastTriggered -= HandleQuickCast;
            if (_releaseInput != null) _releaseInput.OnRuneCastReleased -= HandleReleased;
            _input = null;
            _releaseInput = null;
        }

        // 輕點一下就放：冷卻中一律忽略（不論按了多久），不吃指令、不進二次冷卻。
        private void HandleQuickCast()
        {
            if (_hero == null || _castLogic == null || !_castLogic.TryBeginCast(Time.timeAsDouble)) return;

            Vector3 pos = _hero.position;
            Vector3 forward = _hero.forward;
            if (!_castLogic.TryQuickCastPlacement(pos.x, pos.z, forward.x, forward.z, out RuneWallPlacement placement)) return;

            SpawnWall(placement, pos.y);
        }

        private void HandleReleased(Vector2 screenDirection, float distance01)
        {
            if (_hero == null || _castLogic == null || !_castLogic.TryBeginCast(Time.timeAsDouble)) return;

            Vector3 worldDir = ResolveDragWorldDirection(screenDirection, _cameraTransform, _dragDirectionOverride);
            Vector3 pos = _hero.position;
            if (!_castLogic.TryDragPlacement(pos.x, pos.z, worldDir.x, worldDir.z, distance01, out RuneWallPlacement placement)) return;

            SpawnWall(placement, pos.y);
        }

        private void SpawnWall(RuneWallPlacement placement, float heroGroundY)
        {
            int index = FindFreeSlot();
            if (index < 0) return; // 池全滿：理論上不會發生（上限 2 面 + 1 面坍塌緩衝）

            int evicted = _roster.Add(index);
            if (evicted >= 0 && evicted < _pool.Length && _pool[evicted] != null) _pool[evicted].CollapseWall(false);

            float groundY = _terrain != null ? _terrain.HeightAt(placement.CenterX, placement.CenterZ, 0) : heroGroundY;
            float centerY = groundY + _tuning.WallHeight * 0.5f;
            Vector3 position = new Vector3(placement.CenterX, centerY, placement.CenterZ);
            Quaternion rotation = Quaternion.LookRotation(new Vector3(placement.NormalX, 0f, placement.NormalZ), Vector3.up);
            _pool[index].Activate(position, rotation, _ownerFaction, this, index);
        }

        private int FindFreeSlot()
        {
            if (_pool == null) return -1;
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null && !_pool[i].IsAlive) return i;
            return -1;
        }

        // 供 RuneWall 自己死亡（壽命到／被打碎／被友軍彈道打穿）時呼叫，讓名冊釋放名額。
        public void ReleaseSlot(int slot)
        {
            _roster?.Remove(slot);
        }

        // camera-lab 第三人稱（docs/CAMERA_LAB_COMBAT_PLAN.md §1.3）：拖曳只取拉伸量，方向由覆寫來源（鏡頭前方）提供。
        // null＝俯視原路徑：仍是改動前同一個 ScreenToWorldGroundDirection 的結果，經 CameraLabAim.WallDragDirection 原值交回。
        private Func<Vector3> _dragDirectionOverride;
        public void SetDragDirectionOverride(Func<Vector3> source) { _dragDirectionOverride = source; }

        public static Vector3 ResolveDragWorldDirection(Vector2 screenDirection, Transform cameraTransform, Func<Vector3> overrideSource)
        {
            Vector3 screenWorld = ScreenToWorldGroundDirection(screenDirection, cameraTransform);
            Vector3 aim = overrideSource != null ? overrideSource() : Vector3.zero;
            CameraLabAim.WallDragDirection(overrideSource != null, aim.x, aim.z, screenWorld.x, screenWorld.z,
                out float x, out float z);
            return new Vector3(x, screenWorld.y, z);
        }

        // 螢幕方向（單位向量）→ 世界 XZ 方向：以鏡頭水平朝向為基準，與 HeroController.HandleCadenceFlick／
        // CadenceAimPreview.ScreenToWorldDirection 同一套換算數學。那兩個檔案不在本批可改動範圍內（V5），
        // 這裡另立一份公用給 RuneGhostPreview 與測試共用，數學上完全一致，不得再重複第三份。
        public static Vector3 ScreenToWorldGroundDirection(Vector2 screenDirection, Transform cameraTransform)
        {
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (cameraTransform != null)
            {
                forward = cameraTransform.forward;
                right = cameraTransform.right;
                forward.y = 0f;
                right.y = 0f;
                if (forward.sqrMagnitude < 1e-6f) forward = cameraTransform.up; // 純俯視鏡頭
                forward.y = 0f;
                forward.Normalize();
                right.Normalize();
            }
            return right * screenDirection.x + forward * screenDirection.y;
        }
    }
}
