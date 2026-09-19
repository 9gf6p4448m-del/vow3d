using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
using Vow.UI;

namespace Vow.EditorTools
{
    // 一鍵生成 Phase 1 灰盒場景：40m x 40m 棋盤格平地、靜態預烘焙 NavMesh、1 英雄、1 木樁、2 面測試石牆、鏡頭與全部服務。
    // 可重複執行：每次都從空場景重建並覆寫同一路徑，不會累積殘留物件。
    public static class VOWPhase1SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/VOW_Phase1_Greybox.unity";
        private const string NavMeshDataPath = "Assets/Scenes/VOW_Phase1_NavMesh.asset";
        private const string TuningPath = "Assets/Settings/HeroTuning.asset";

        private const float ArenaSize = 40f;
        private const int IgnoreRaycastLayer = 2;

        [MenuItem("VOW/Phase 1/Build Greybox Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            GreyboxAssetFactory.EnsureFolder("Assets/Scenes");
            GreyboxAssetFactory.EnsureFolder(GreyboxAssetFactory.SettingsFolder);
            GreyboxAssetFactory.EnsureRenderPipeline();
            EnsureNewInputSystemBackend();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Materials materials = CreateMaterials();
            HeroTuningAsset tuning = EnsureTuningAsset();

            CreateLight();
            GameObject ground = CreateGround(materials.Ground);

            // NavMesh 在「只有地板」的時候烘焙：石牆、木樁、英雄都還不存在，所以烘出來的是一整片無洞的靜態網格。
            // 石牆之後被打碎也不需要重烘——它從頭到尾就不在 NavMesh 裡（紅線 5：零 carving、零執行期烘焙）。
            BakeStaticNavMesh(ground);
            GameObject arenaBoundary = CreateArenaBoundary(tuning.BodyRadius); // 必須在烘焙之後：邊界牆不得進入 NavMesh

            HeroController hero = CreateHero(tuning, materials.Hero);
            CreateDummy(new Vector3(0f, 0f, 6f), materials.Dummy, materials.Bar);
            CreateWall("TestWall_A", new Vector3(-7f, 0f, 3f), 0f, materials.Wall, materials.Bar);
            CreateWall("TestWall_B", new Vector3(7f, 0f, 3f), 90f, materials.Wall, materials.Bar);
            CreateRuneWallPool(tuning.Rune, materials.RuneWall);
            RuneGhostPreview runeGhost = CreateRuneGhostPreview(tuning.Rune, materials.RuneGhost);
            NavGridDebugView navGridDebug = CreateNavGridDebugView(materials.NavGrid);

            Camera camera = CreateCameraRig(out FollowCameraRig rig, out Transform shakePivot);
            CreateSystems(hero, camera, rig, shakePivot, materials, tuning, runeGhost, navGridDebug, arenaBoundary.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("[VOW] Phase 1 灰盒場景已生成：" + ScenePath + "。按 Play 即可測試。");
        }

        // ───────────────────────── 資產 ─────────────────────────

        private struct Materials
        {
            public Material Ground, Hero, Dummy, Wall, Bar, Flash, Decal, Telegraph, HitboxLines, RuneWall, RuneGhost, NavGrid;
        }

        private static Materials CreateMaterials()
        {
            Texture2D checker = GreyboxAssetFactory.EnsureCheckerTexture();
            return new Materials
            {
                // 貼圖為 2x2 格；tiling = 邊長 / 2 → 每格恰好 1 公尺
                Ground = GreyboxAssetFactory.EnsureLitMaterial("VOW_Ground", Color.white, checker, ArenaSize * 0.5f),
                Hero = GreyboxAssetFactory.EnsureLitMaterial("VOW_Hero", new Color(0.25f, 0.55f, 0.95f)),
                Dummy = GreyboxAssetFactory.EnsureLitMaterial("VOW_Dummy", new Color(0.72f, 0.52f, 0.32f)),
                Wall = GreyboxAssetFactory.EnsureLitMaterial("VOW_Wall", new Color(0.45f, 0.43f, 0.4f)),
                Bar = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_OverheadBar", Color.white, false, true),
                Flash = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_ScreenFlash", new Color(1f, 1f, 1f, 0f), true, true),
                Decal = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_GroundDecal", new Color(0f, 0f, 0f, 0.8f), true, true),
                Telegraph = GreyboxAssetFactory.EnsureVertexColorMaterial("VOW_Telegraph"),
                HitboxLines = GreyboxAssetFactory.EnsureGlLineMaterial("VOW_HitboxLines"),
                RuneWall = GreyboxAssetFactory.EnsureLitMaterial("VOW_RuneWall", new Color(0.35f, 0.4f, 0.58f)),
                RuneGhost = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_RuneGhost", new Color(0.35f, 0.85f, 1f, 0.35f), true, true),
                NavGrid = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_NavGridDebug", new Color(1f, 0.35f, 0.25f, 0.45f), true, true)
            };
        }

        private static HeroTuningAsset EnsureTuningAsset()
        {
            HeroTuningAsset tuning = AssetDatabase.LoadAssetAtPath<HeroTuningAsset>(TuningPath);
            if (tuning != null) return tuning; // 已存在就沿用：測試者調過的手感數值不可被重建場景洗掉

            tuning = ScriptableObject.CreateInstance<HeroTuningAsset>();
            AssetDatabase.CreateAsset(tuning, TuningPath);
            return tuning;
        }

        // ───────────────────────── 場景物件 ─────────────────────────

        private static void CreateLight()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject CreateGround(Material material)
        {
            // 用 Cube（BoxCollider）而非 Plane（MeshCollider）：ARCHITECTURE §壹 規定碰撞器一律 Primitive
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground_40x40";
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(ArenaSize, 0.2f, ArenaSize);
            ground.isStatic = true;
            ground.GetComponent<Renderer>().sharedMaterial = material;
            return ground;
        }

        // 場地四周的隱形邊界（只有 BoxCollider、沒有 Renderer）。微滑步與步行的位移都經 SphereCast 裁切，
        // 有了實體邊界，英雄就不可能被滑出平台、掉出 NavMesh——不需要依賴 NavMeshAgent 夾回位置的行為。
        // 回傳邊界根物件：Phase1Bootstrap 要拿它把四面牆登記進阻擋格點（§6 R3，格點最外圈必須是 Blocked）。
        private static GameObject CreateArenaBoundary(float bodyRadius)
        {
            const float height = 3f;
            const float thickness = 1f;

            // 牆的內面不能貼齊地板邊緣：NavMesh 烘焙會依 agent 半徑從邊緣往內侵蝕（預設 Humanoid agent = 0.5m，
            // PlayMode 測試實測 NavMesh 邊界在 ±19.5），若牆內面在 ±20，英雄中心會落進「牆內、NavMesh 外」的窄帶。
            // 牆內面由 BodyRadius 推導，使「牆擋住時的英雄中心」恆落在 NavMesh 邊界內側 margin 處——
            // 日後調整 BodyRadius 並重建場景，這個關係仍然成立。
            const float navMeshErosion = 0.5f;
            const float margin = 0.05f;
            float half = ArenaSize * 0.5f - navMeshErosion + bodyRadius - margin;

            GameObject root = new GameObject("ArenaBoundary");
            for (int i = 0; i < 4; i++)
            {
                bool alongX = i < 2;
                float sign = i % 2 == 0 ? 1f : -1f;

                GameObject wall = new GameObject("Boundary_" + i);
                wall.transform.SetParent(root.transform, false);
                wall.layer = IgnoreRaycastLayer; // 不擋點擊射線；HeroLocomotion 的 SphereCast 用 AllLayers，所以仍擋得住身體
                wall.transform.position = alongX
                    ? new Vector3(sign * (half + thickness * 0.5f), height * 0.5f, 0f)
                    : new Vector3(0f, height * 0.5f, sign * (half + thickness * 0.5f));

                BoxCollider box = wall.AddComponent<BoxCollider>();
                box.size = alongX
                    ? new Vector3(thickness, height, ArenaSize + thickness * 2f)
                    : new Vector3(ArenaSize + thickness * 2f, height, thickness);
            }
            return root;
        }

        private static void BakeStaticNavMesh(GameObject ground)
        {
            NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All; // 此刻場景裡有 Collider 的只有地板
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
                throw new System.InvalidOperationException("[VOW] NavMesh 烘焙失敗：navMeshData 為 null");

            AssetDatabase.DeleteAsset(NavMeshDataPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshDataPath);
        }

        private static HeroController CreateHero(HeroTuningAsset tuning, Material placeholderMaterial)
        {
            GameObject hero = new GameObject("Hero_Player");
            hero.transform.position = Vector3.zero;

            CapsuleCollider capsule = hero.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = tuning.BodyRadius;

            NavMeshAgent agent = hero.AddComponent<NavMeshAgent>();
            agent.radius = tuning.BodyRadius;
            agent.height = 1.8f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.enabled = false; // 由 HeroLocomotion.Start 啟用：必須晚於 NavMeshSurface 載入 NavMesh 資料

            hero.AddComponent<HeroLocomotion>();
            hero.AddComponent<MicroCadenceMover>();
            HeroController controller = hero.AddComponent<HeroController>();
            SetReference(controller, "_tuningAsset", tuning);

            HeroRigFactory.HeroRig rig = HeroRigFactory.Build(tuning.Combat.WindupSeconds, placeholderMaterial);
            rig.ModelInstance.transform.SetParent(hero.transform, false);

            if (rig.IsHumanoid)
            {
                // 匯入的 FBX 材質是內建管線的 Standard shader，在 URP 下會渲染成粉紅色；灰盒階段統一換成英雄的 URP 材質
                foreach (Renderer modelRenderer in rig.ModelInstance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] overridden = new Material[modelRenderer.sharedMaterials.Length];
                    for (int i = 0; i < overridden.Length; i++) overridden[i] = placeholderMaterial;
                    modelRenderer.sharedMaterials = overridden;
                }
            }

            // Ignore Raycast：點擊射線穿過自己的英雄，點在英雄身上＝點到他腳下的地板
            SetLayerRecursively(hero, IgnoreRaycastLayer);
            return controller;
        }

        private static void CreateDummy(Vector3 position, Material material, Material barMaterial)
        {
            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule); // 自帶 CapsuleCollider
            dummy.name = "Dummy_Target";
            dummy.transform.position = position + Vector3.up;
            dummy.GetComponent<Renderer>().sharedMaterial = material;

            DummyTarget target = dummy.AddComponent<DummyTarget>();
            SetFloat(target, "_maxHealth", 600f);
            SetEnum(target, "_faction", (int)Faction.RedTeam);

            TargetOverheadDisplay overhead = dummy.AddComponent<TargetOverheadDisplay>();
            SetReference(overhead, "_barMaterial", barMaterial);
            SetFloat(overhead, "_height", 1.6f);
        }

        private static void CreateWall(string name, Vector3 position, float yawDegrees, Material material, Material barMaterial)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); // 自帶 BoxCollider
            wall.name = name;
            wall.transform.position = position + Vector3.up * 1.25f;
            wall.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            wall.transform.localScale = new Vector3(4f, 2.5f, 0.6f);
            wall.GetComponent<Renderer>().sharedMaterial = material;

            TestWallTarget target = wall.AddComponent<TestWallTarget>();
            SetFloat(target, "_maxHealth", 300f);
            SetEnum(target, "_faction", (int)Faction.DestructibleWall);

            TargetOverheadDisplay overhead = wall.AddComponent<TargetOverheadDisplay>();
            SetReference(overhead, "_barMaterial", barMaterial);
            SetFloat(overhead, "_height", 1.9f); // 自石牆中心 (y=1.25) 起算，落在牆頂上方
        }

        // 符印石牆池：上限 2 面 + 1 面坍塌緩衝（計畫書 §4 假設 9）。平時保持 GameObject 啟用、
        // 只關 Collider／Renderer——RuneWall.Awake 會在建立當下立刻把自己關成「待命」狀態。
        // 執行期禁止 CreatePrimitive（IL2CPP 剔除），這裡是 Editor-only 程式碼，不受此限。
        private const int RuneWallPoolSize = 3;

        private static void CreateRuneWallPool(RuneTuning runeTuning, Material material)
        {
            for (int i = 0; i < RuneWallPoolSize; i++)
            {
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); // 自帶 BoxCollider（紅線 5）
                wall.name = "RuneWall_Pool_" + i;
                wall.transform.position = new Vector3(0f, runeTuning.WallHeight * 0.5f, 0f);
                wall.transform.localScale = new Vector3(runeTuning.WallWidth, runeTuning.WallHeight, runeTuning.WallThickness);
                wall.GetComponent<Renderer>().sharedMaterial = material;

                RuneWall runeWall = wall.AddComponent<RuneWall>();
                SetFloat(runeWall, "_maxHealth", runeTuning.WallMaxHealth);
                SetEnum(runeWall, "_faction", (int)Faction.DestructibleWall);

                // r1 對抗審查 H4：PlayerInputService.OnWorldTap 的點擊射線用 Physics.DefaultRaycastLayers，
                // 石牆若留在 Default 層，點自家石牆後方的地板會先打到牆、英雄原地砍自己的牆。放 Ignore Raycast 層
                // 讓點擊射線穿過去；HeroLocomotion.ApplyDisplacement 的身體 SphereCast 用 Physics.AllLayers，
                // 照樣擋得住（邊界牆已經是同一套做法）。批 1 的代價：**所有**符印石牆都無法被點擊鎖定攻擊（全專案唯一的選取路徑就是那條射線）。
                // GDD §參-2「近戰砸碎敵方／中立石牆得護盾」需要牆打得到——批 3 不能只加陣營校驗，得把這個圖層做法換成分陣營圖層，
                // 或讓射線解析到牆之後再做陣營校驗。
                SetLayerRecursively(wall, IgnoreRaycastLayer);
            }
        }

        // 拖曳中的半透明虛影：只要 Renderer，沒有 Collider（不得擋路、不得吃射線）。
        private static RuneGhostPreview CreateRuneGhostPreview(RuneTuning runeTuning, Material material)
        {
            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "RuneWallGhost";
            Object.DestroyImmediate(ghost.GetComponent<BoxCollider>());
            ghost.transform.localScale = new Vector3(runeTuning.WallWidth, runeTuning.WallHeight, runeTuning.WallThickness);

            Renderer renderer = ghost.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false; // 預設隱藏，拖曳時才顯示

            RuneGhostPreview preview = ghost.AddComponent<RuneGhostPreview>();
            SetReference(preview, "_visualRenderer", renderer);
            return preview;
        }

        // GRID 除錯疊圖的載體：空的 MeshFilter／MeshRenderer，Mesh 由 NavGridDebugView 在執行期填內容。
        // 執行期禁止 CreatePrimitive（IL2CPP 剔除），所以物件本體一定要在這裡預建好（計畫書 §4 假設 10）。
        private static NavGridDebugView CreateNavGridDebugView(Material material)
        {
            GameObject view = new GameObject("NavGridDebug");
            view.layer = IgnoreRaycastLayer; // 不吃點擊射線
            view.AddComponent<MeshFilter>();

            MeshRenderer renderer = view.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false; // 預設關
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return view.AddComponent<NavGridDebugView>();
        }

        private static Camera CreateCameraRig(out FollowCameraRig rig, out Transform shakePivot)
        {
            GameObject rigObject = new GameObject("CameraRig");
            rig = rigObject.AddComponent<FollowCameraRig>();

            shakePivot = new GameObject("ShakePivot").transform;
            shakePivot.SetParent(rigObject.transform, false);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(shakePivot, false);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cameraObject.AddComponent<AudioListener>();

            rigObject.transform.rotation = Quaternion.Euler(52f, 0f, 0f);
            rigObject.transform.position = new Vector3(0f, 13.4f, -10.5f);
            return camera;
        }

        private static void CreateSystems(HeroController hero, Camera camera, FollowCameraRig rig, Transform shakePivot,
            Materials materials, HeroTuningAsset tuning, RuneGhostPreview runeGhost, NavGridDebugView navGridDebug,
            Transform arenaBoundary)
        {
            GameObject systems = new GameObject("VOW_Systems");

            PlayerInputService input = systems.AddComponent<PlayerInputService>();
            SetReference(input, "_worldCamera", camera);
            SetFloat(input, "_runeSaturationMillimeters", tuning.Rune.DragSaturationMillimeters);

            NetworkLatencySimulator latency = systems.AddComponent<NetworkLatencySimulator>();
            SetReference(latency, "_inner", input);
            HapticFeedbackService haptics = systems.AddComponent<HapticFeedbackService>();

            CombatFeedbackService feedback = systems.AddComponent<CombatFeedbackService>();
            SetReference(feedback, "_shakePivot", shakePivot);
            SetReference(feedback, "_camera", camera);
            SetReference(feedback, "_flashMaterial", materials.Flash);
            SetReference(feedback, "_decalMaterial", materials.Decal);

            SkillTelegraphService telegraph = systems.AddComponent<SkillTelegraphService>();
            SetReference(telegraph, "_lineMaterial", materials.Telegraph);

            HitboxVisualizer hitboxes = systems.AddComponent<HitboxVisualizer>();
            SetReference(hitboxes, "_lineMaterial", materials.HitboxLines);
            DebugHud hud = systems.AddComponent<DebugHud>();
            CadenceAimPreview aimPreview = systems.AddComponent<CadenceAimPreview>();

            RuneCaster runeCaster = systems.AddComponent<RuneCaster>();
            RuneButtonView runeButton = systems.AddComponent<RuneButtonView>();

            Phase1Bootstrap bootstrap = systems.AddComponent<Phase1Bootstrap>();
            SetReference(bootstrap, "_hero", hero);
            SetReference(bootstrap, "_input", input);
            SetReference(bootstrap, "_latency", latency);
            SetReference(bootstrap, "_haptics", haptics);
            SetReference(bootstrap, "_feedback", feedback);
            SetReference(bootstrap, "_telegraph", telegraph);
            SetReference(bootstrap, "_cameraRig", rig);
            SetReference(bootstrap, "_camera", camera);
            SetReference(bootstrap, "_hud", hud);
            SetReference(bootstrap, "_hitboxes", hitboxes);
            SetReference(bootstrap, "_aimPreview", aimPreview);
            SetReference(bootstrap, "_tuningAsset", tuning);
            SetReference(bootstrap, "_runeCaster", runeCaster);
            SetReference(bootstrap, "_runeGhost", runeGhost);
            SetReference(bootstrap, "_runeButton", runeButton);
            SetReference(bootstrap, "_navGridDebug", navGridDebug);
            SetReference(bootstrap, "_arenaBoundary", arenaBoundary);
        }

        // ───────────────────────── 專案設定 ─────────────────────────

        // Active Input Handling：0 = 舊版、1 = Input System Package (New)、2 = Both。
        // 舊版 (0) 之下 EnhancedTouch 收不到任何事件；改設定後必須重啟 Editor 才生效。
        private static void EnsureNewInputSystemBackend()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;

            SerializedObject settings = new SerializedObject(assets[0]);
            SerializedProperty handler = settings.FindProperty("activeInputHandler");
            if (handler == null || handler.intValue != 0) return;

            handler.intValue = 1;
            settings.ApplyModifiedProperties();
            Debug.LogWarning("[VOW] 已將 Active Input Handling 切換為 Input System Package (New)。請重新啟動 Unity Editor 後再按 Play。");
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
                if (scenes[i].path == scenePath) return;

            EditorBuildSettingsScene[] updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updated;
        }

        // ───────────────────────── 序列化欄位接線 ─────────────────────────
        // 欄位名打錯時 FindProperty 會回 null；這裡一律丟例外，絕不允許「場景看起來建好了、其實引用是空的」。

        private static SerializedProperty RequireProperty(SerializedObject serialized, string propertyName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new System.InvalidOperationException(
                    "[VOW] " + serialized.targetObject.GetType().Name + " 沒有序列化欄位 '" + propertyName + "'");
            return property;
        }

        private static void SetReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            RequireProperty(serialized, propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            RequireProperty(serialized, propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 寫 intValue（列舉的數值）而非 enumValueIndex（宣告順序）：日後有人替列舉指定明碼值時，兩者會不同而靜默寫錯。
        private static void SetEnum(Object target, string propertyName, int enumValue)
        {
            SerializedObject serialized = new SerializedObject(target);
            RequireProperty(serialized, propertyName).intValue = enumValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
