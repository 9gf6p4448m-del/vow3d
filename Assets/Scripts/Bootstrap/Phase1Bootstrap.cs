using UnityEngine;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Input;
using Vow.UI;

namespace Vow.Bootstrap
{
    // Phase 1 灰盒場景的組裝根 (Composition Root)：全場唯一知道「誰是具體類別」的地方。
    // 各模組只認得 Vow.Core 裡的介面；把它們接起來的工作集中在這裡，換實作、接測試替身都只改這一個檔。
    [DefaultExecutionOrder(-1000)]
    public sealed class Phase1Bootstrap : MonoBehaviour
    {
        private const int TargetFrameRate = 120;

        [SerializeField] private HeroController _hero;
        [SerializeField] private PlayerInputService _input;
        [SerializeField] private CombatFeedbackService _feedback;
        [SerializeField] private SkillTelegraphService _telegraph;
        [SerializeField] private FollowCameraRig _cameraRig;
        [SerializeField] private Camera _camera;
        [SerializeField] private DebugHud _hud;
        [SerializeField] private HitboxVisualizer _hitboxes;
        [SerializeField] private CadenceAimPreview _aimPreview;

        private readonly ColliderTargetRegistry _targets = new ColliderTargetRegistry();

        private void Awake()
        {
            // 紅線 6：畫面與輸入鎖定 120Hz（輸入取樣頻率由 PlayerInputService 設定 InputSystem.pollingFrequency）。
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;

            ResolveMissingReferences();
        }

        // 放在 Start：此時所有物件的 Awake 都已跑完（HeroController 的狀態機、各目標的 Collider 快取都已就緒）。
        private void Start()
        {
            if (_hero == null || _input == null)
            {
                Debug.LogError("[VOW] 場景缺少 HeroController 或 PlayerInputService，請執行 VOW/Phase 1/Build Greybox Scene。", this);
                return;
            }

            CombatTargetBehaviour[] targets = FindObjectsOfType<CombatTargetBehaviour>();
            for (int i = 0; i < targets.Length; i++) _targets.Register(targets[i]);

            _input.Initialize(_targets, _camera);
            _hero.Initialize(_input, _feedback, _camera);

            if (_feedback != null)
            {
                MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                    if (behaviours[i] is IHitstopParticipant participant) _feedback.RegisterHitstopParticipant(participant);

                _hero.StateMachine.OnStateChanged += HandleHeroStateChanged;
            }

            if (_cameraRig != null) _cameraRig.SetTarget(_hero.transform);
            if (_hud != null) _hud.Initialize(_hero, _input, _hitboxes);
            if (_aimPreview != null) _aimPreview.Initialize(_hero, _input, _telegraph, _camera);
        }

        private void OnDestroy()
        {
            if (_hero != null && _feedback != null) _hero.StateMachine.OnStateChanged -= HandleHeroStateChanged;
        }

        // 目押成功切後搖 → 立即解除上一刀的頓挫幀，滑步動畫不被卡住。
        private void HandleHeroStateChanged(PlayerState oldState, PlayerState newState)
        {
            if (newState == PlayerState.CadenceDashing) _feedback.CancelHitstop();
        }

        // SceneBuilder 會把引用全部接好；這裡只是手動拼場景時的後備。
        private void ResolveMissingReferences()
        {
            if (_hero == null) _hero = FindObjectOfType<HeroController>();
            if (_input == null) _input = FindObjectOfType<PlayerInputService>();
            if (_feedback == null) _feedback = FindObjectOfType<CombatFeedbackService>();
            if (_telegraph == null) _telegraph = FindObjectOfType<SkillTelegraphService>();
            if (_cameraRig == null) _cameraRig = FindObjectOfType<FollowCameraRig>();
            if (_camera == null) _camera = Camera.main;
            if (_hud == null) _hud = FindObjectOfType<DebugHud>();
            if (_hitboxes == null) _hitboxes = FindObjectOfType<HitboxVisualizer>();
            if (_aimPreview == null) _aimPreview = FindObjectOfType<CadenceAimPreview>();
        }
    }
}
