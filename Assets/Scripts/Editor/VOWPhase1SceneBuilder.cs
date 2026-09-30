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

            // v0.14.0 沿用原平地靜態 NavMesh；地形高度與崖壁由 runtime 地形查詢／格點處理。
            AttachStaticNavMesh(ground);
            Transform canyonTerrain = CreateCanyonTerrain(materials);
            Transform cliffBarriers = CreateCliffBarriers();
            GameObject arenaBoundary = CreateArenaBoundary(tuning.BodyRadius); // 必須在烘焙之後：邊界牆不得進入 NavMesh

            HeroController hero = CreateHero(tuning, materials.Hero, materials.Bar);
            CreateDummy(new Vector3(0f, 0f, 6f), materials.Dummy, materials.Bar);
            TrainingOpponent opponent = CreateOpponent(new Vector3(-4f, 0f, 8f), tuning, materials.Opponent, materials.Bar);
            CreateWall("TestWall_A", new Vector3(-7f, 0f, 3f), 0f, materials.Wall, materials.Bar);
            CreateWall("TestWall_B", new Vector3(7f, 0f, 3f), 90f, materials.Wall, materials.Bar);
            Transform runeWallPool = CreateRuneWallPool(tuning.Rune, materials.RuneWall, materials.Bar);
            Transform enemyWallPool = CreateEnemyWallPool(tuning.Rune, materials.EnemyWall, materials.Bar);
            TestTurret turret = CreateTurretAndBullets(materials.Turret, materials.Bullet);
            RuneGhostPreview runeGhost = CreateRuneGhostPreview(tuning.Rune, materials.RuneGhost);
            NavGridDebugView navGridDebug = CreateNavGridDebugView(materials.NavGrid);
            Transform elementZonePool = CreateElementZonePool(materials);
            CaptureBoardView captureBoard = CreateCaptureBoard(materials);
            AbyssalVanguardTarget vanguardTarget = CreateAbyssalVanguard(materials);
            RageAuraView rageAuras = CreateRageAuras(materials.Rage);

            Camera camera = CreateCameraRig(out FollowCameraRig rig, out Transform shakePivot);
            CreateSystems(hero, opponent, camera, rig, shakePivot, materials, tuning, runeGhost, navGridDebug, arenaBoundary.transform,
                          runeWallPool, enemyWallPool, turret, elementZonePool, captureBoard, rageAuras, vanguardTarget,
                          canyonTerrain, cliffBarriers, ground.GetComponent<Collider>());

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
            public Material EnemyWall, Turret, Bullet, Opponent;
            public Material ZoneWater, ZoneBurning, ZoneQuicksand, ZoneSteam;
            public Material CaptureNeutral, CaptureBlue, CaptureRed;
            public Material Rage;
            public Material Vanguard, BehemothBlue, BehemothRed, VanguardCore, VanguardWarning;
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
                NavGrid = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_NavGridDebug", new Color(1f, 0.35f, 0.25f, 0.45f), true, true),
                // 批 3：敵方牆是紅的（一眼分得出哪面砸得到）、砲台與子彈用高對比色
                EnemyWall = GreyboxAssetFactory.EnsureLitMaterial("VOW_EnemyWall", new Color(0.72f, 0.18f, 0.16f)),
                Turret = GreyboxAssetFactory.EnsureLitMaterial("VOW_Turret", new Color(0.3f, 0.65f, 0.9f)),
                Bullet = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_Bullet", new Color(1f, 0.92f, 0.45f), false, false),
                Opponent = GreyboxAssetFactory.EnsureLitMaterial("VOW_Opponent", new Color(0.82f, 0.16f, 0.18f)),
                // 批 4：四種元素區域的灰盒色（冷庫協議：不做粒子／著色器，只有半透明扁圓柱）
                ZoneWater = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_ZoneWater", new Color(0.24f, 0.55f, 0.95f, 0.38f), true, true),
                ZoneBurning = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_ZoneBurning", new Color(1f, 0.45f, 0.12f, 0.45f), true, true),
                ZoneQuicksand = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_ZoneQuicksand", new Color(0.72f, 0.56f, 0.24f, 0.5f), true, true),
                ZoneSteam = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_ZoneSteam", new Color(0.95f, 0.95f, 0.98f, 0.5f), true, true),
                // v0.8.0 佔領板塊（E30）：中立灰／藍／紅三份預建材質，執行期只切 sharedMaterial
                CaptureNeutral = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_CaptureNeutral", new Color(0.62f, 0.62f, 0.66f, 0.32f), true, true),
                CaptureBlue = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_CaptureBlue", new Color(0.2f, 0.48f, 1f, 0.45f), true, true),
                CaptureRed = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_CaptureRed", new Color(1f, 0.22f, 0.18f, 0.45f), true, true),
                // v0.9.0 E18：狂怒光環＝亮橘、不受光、不透明（與中灰半透明的棋盤、白色地板都拉得開）
                Rage = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_Rage", new Color(1f, 0.5f, 0.05f, 1f), false, true),
                Vanguard = GreyboxAssetFactory.EnsureLitMaterial("VOW_Vanguard", new Color(0.42f, 0.31f, 0.66f)),
                BehemothBlue = GreyboxAssetFactory.EnsureLitMaterial("VOW_BehemothBlue", new Color(0.12f, 0.53f, 0.98f)),
                BehemothRed = GreyboxAssetFactory.EnsureLitMaterial("VOW_BehemothRed", new Color(0.92f, 0.2f, 0.14f)),
                VanguardCore = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_VanguardCore", new Color(0.98f, 0.78f, 0.22f, 0.66f), true, true),
                VanguardWarning = GreyboxAssetFactory.EnsureUnlitMaterial("VOW_VanguardWarning", new Color(1f, 0.16f, 0.08f, 0.32f), true, true)
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

        private static void AttachStaticNavMesh(GameObject ground)
        {
            NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshDataPath);
            if (data == null)
                throw new System.InvalidOperationException("[VOW] 缺少既有平地 NavMesh 資產：" + NavMeshDataPath);
            NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.navMeshData = data;
            surface.AddData();
        }

        private static Transform CreateCanyonTerrain(Materials materials)
        {
            CanyonTerrainSpec terrain = CanyonTerrainSpec.V0140;
            CaptureBoardSpec board = terrain.Board;
            Transform root = new GameObject("CanyonTerrain").transform;
            Mesh surfaceMesh = EnsureCanyonHexMesh(board);
            const float bottom = -2f;
            // 長邊 2h、短邊 R，90°／30°／150° 三矩形的聯集恰為平頂正六角。
            for (int tile = 0; tile < board.TileCount; tile++)
            {
                float top = terrain.TileHeight(tile);
                for (int part = 0; part < 3; part++)
                {
                    float yaw = part == 0 ? 90f : (part == 1 ? 30f : 150f);
                    CreateTerrainBox(root, "Tile_" + tile + "_" + part,
                        new Vector3(board.CenterX(tile), (top + bottom) * 0.5f, board.CenterZ(tile)),
                        Quaternion.Euler(0f, yaw, 0f),
                        new Vector3(board.InRadius * 2f, top - bottom, board.CircumRadius), materials.Ground, false);
                }
                // 完整六角表面只畫一次；所有 primitive 盒仍僅負責原凍結碰撞幾何。
                GameObject surface = new GameObject("TileSurface_" + tile);
                surface.transform.SetParent(root, false);
                const float visualOffset = 0.005f;
                surface.transform.localPosition = new Vector3(board.CenterX(tile), top + visualOffset, board.CenterZ(tile));
                surface.transform.localScale = new Vector3(1f, top - bottom + visualOffset, 1f);
                surface.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
                surface.AddComponent<MeshRenderer>().sharedMaterial = materials.Ground;
            }
            for (int ramp = 0; ramp < terrain.RampCount; ramp++)
            {
                float low = terrain.TileHeight(terrain.RampLowTile(ramp));
                float high = terrain.TileHeight(terrain.RampHighTile(ramp));
                Vector3 forward = new Vector3(terrain.RampAxisX(ramp), (high - low) / terrain.RampLength,
                    terrain.RampAxisZ(ramp)).normalized;
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
                const float thickness = 0.2f;
                Vector3 surfaceCenter = new Vector3(terrain.RampCenterX(ramp), (low + high) * 0.5f,
                    terrain.RampCenterZ(ramp));
                CreateTerrainBox(root, "Ramp_" + ramp, surfaceCenter - rotation * Vector3.up * (thickness * 0.5f),
                    rotation, new Vector3(terrain.RampWidth, thickness,
                        Mathf.Sqrt(terrain.RampLength * terrain.RampLength + (high - low) * (high - low))), materials.Ground);
            }
            // 四條外側平原從非中層頂點包圍盒延伸到場邊；只和中層地形重疊。
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int tile = 0; tile < board.TileCount; tile++)
            {
                if (terrain.TileHeight(tile) == 0f) continue;
                for (int vertex = 0; vertex < 6; vertex++)
                {
                    minX = Mathf.Min(minX, board.VertexX(tile, vertex));
                    maxX = Mathf.Max(maxX, board.VertexX(tile, vertex));
                    minZ = Mathf.Min(minZ, board.VertexZ(tile, vertex));
                    maxZ = Mathf.Max(maxZ, board.VertexZ(tile, vertex));
                }
            }
            float half = ArenaSize * 0.5f;
            CreateTerrainBox(root, "Outside_West", new Vector3((-half + minX) * 0.5f, -1f, 0f), Quaternion.identity,
                new Vector3(minX + half, 2f, ArenaSize), materials.Ground);
            CreateTerrainBox(root, "Outside_East", new Vector3((half + maxX) * 0.5f, -1f, 0f), Quaternion.identity,
                new Vector3(half - maxX, 2f, ArenaSize), materials.Ground);
            CreateTerrainBox(root, "Outside_South", new Vector3((minX + maxX) * 0.5f, -1f, (-half + minZ) * 0.5f), Quaternion.identity,
                new Vector3(maxX - minX, 2f, minZ + half), materials.Ground);
            CreateTerrainBox(root, "Outside_North", new Vector3((minX + maxX) * 0.5f, -1f, (half + maxZ) * 0.5f), Quaternion.identity,
                new Vector3(maxX - minX, 2f, half - maxZ), materials.Ground);

            float diameter = new CanyonTuning().VentPadRadius * 2f;
            for (int vent = 0; vent < CanyonTerrainSpec.VentCount; vent++)
            {
                GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = "GeothermalPad_" + vent;
                pad.transform.SetParent(root, false);
                float x = terrain.VentPadX(vent), z = terrain.VentPadZ(vent);
                pad.transform.localPosition = new Vector3(x, terrain.HeightAt(x, z, 0) + 0.025f, z);
                pad.transform.localScale = new Vector3(diameter, 0.015f, diameter);
                Object.DestroyImmediate(pad.GetComponent<Collider>());
                pad.GetComponent<Renderer>().sharedMaterial = materials.VanguardCore;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        private static void CreateTerrainBox(Transform root, string name, Vector3 position, Quaternion rotation,
            Vector3 size, Material material, bool visible = true)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = position;
            box.transform.localRotation = rotation;
            box.transform.localScale = size;
            if (visible) box.GetComponent<Renderer>().sharedMaterial = material;
            else
            {
                Object.DestroyImmediate(box.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(box.GetComponent<MeshFilter>());
            }
        }

        private static Mesh EnsureCanyonHexMesh(CaptureBoardSpec board)
        {
            const string path = "Assets/Settings/VOW_CanyonHex.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "VOW_CanyonHex";
            Vector3[] vertices = new Vector3[31];
            Vector3[] normals = new Vector3[31];
            Vector2[] uv = new Vector2[31];
            int[] triangles = new int[54];
            normals[0] = Vector3.up;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int v = 0; v < 6; v++)
            {
                Vector3 p = new Vector3(board.VertexX(0, v) - board.CenterX(0), 0f,
                    board.VertexZ(0, v) - board.CenterZ(0));
                vertices[v + 1] = p;
                normals[v + 1] = Vector3.up;
                uv[v + 1] = new Vector2(p.x / ArenaSize + 0.5f, p.z / ArenaSize + 0.5f);
                triangles[v * 3] = 0;
                triangles[v * 3 + 1] = (v + 1) % 6 + 1;
                triangles[v * 3 + 2] = v + 1;
            }
            for (int v = 0; v < 6; v++)
            {
                Vector3 a = vertices[v + 1], b = vertices[(v + 1) % 6 + 1];
                int start = 7 + v * 4;
                vertices[start] = a; vertices[start + 1] = b;
                vertices[start + 2] = a + Vector3.down; vertices[start + 3] = b + Vector3.down;
                Vector3 normal = Vector3.Cross(b - a, Vector3.down).normalized;
                for (int n = 0; n < 4; n++) normals[start + n] = normal;
                uv[start] = Vector2.zero; uv[start + 1] = Vector2.right;
                uv[start + 2] = Vector2.up; uv[start + 3] = Vector2.one;
                int index = 18 + v * 6;
                triangles[index] = start; triangles[index + 1] = start + 1; triangles[index + 2] = start + 2;
                triangles[index + 3] = start + 1; triangles[index + 4] = start + 3; triangles[index + 5] = start + 2;
            }
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Transform CreateCliffBarriers()
        {
            CanyonTerrainSpec terrain = CanyonTerrainSpec.V0140;
            Transform root = new GameObject("CliffBarriers").transform;
            float thickness = new CanyonTuning().CliffBarrierThickness;
            for (int i = 0; i < terrain.CliffSegmentCount; i++)
            {
                terrain.GetCliffSegment(i, out float x0, out float z0, out float x1, out float z1);
                GameObject wall = new GameObject("CliffBarrier_" + i);
                wall.transform.SetParent(root, false);
                wall.layer = IgnoreRaycastLayer;
                wall.transform.localPosition = new Vector3((x0 + x1) * 0.5f, 1f, (z0 + z1) * 0.5f);
                Vector3 tangent = new Vector3(x1 - x0, 0f, z1 - z0);
                wall.transform.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                wall.AddComponent<BoxCollider>().size = new Vector3(thickness, 6f, tangent.magnitude);
            }
            root.gameObject.SetActive(false);
            return root;
        }

        private static HeroController CreateHero(HeroTuningAsset tuning, Material placeholderMaterial, Material barMaterial)
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

            // 批 3：破牆護盾與頭上護盾條。IL2CPP／WebGL 會剔除沒被場景引用的類別，所以一定要真的掛上去。
            hero.AddComponent<RockShieldBehaviour>();
            HeroShieldBar shieldBar = hero.AddComponent<HeroShieldBar>();
            SetReference(shieldBar, "_barMaterial", barMaterial);

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

        private static TrainingOpponent CreateOpponent(Vector3 position, HeroTuningAsset tuning,
                                                       Material material, Material barMaterial)
        {
            GameObject root = new GameObject("TrainingOpponent_Red");
            root.transform.position = position;
            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = tuning.BodyRadius;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = tuning.BodyRadius;
            agent.height = 1.8f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.enabled = false;
            root.AddComponent<HeroLocomotion>();
            TrainingOpponent opponent = root.AddComponent<TrainingOpponent>();
            SetFloat(opponent, "_maxHealth", 300f);
            SetEnum(opponent, "_faction", (int)Faction.RedTeam);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "RedGreyboxBody";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            visual.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            TargetOverheadDisplay overhead = root.AddComponent<TargetOverheadDisplay>();
            SetReference(overhead, "_barMaterial", barMaterial);
            SetFloat(overhead, "_height", 2f);
            return opponent;
        }

        // 根物件一直啟用，供 Bootstrap 的 FindObjectsOfType 與 collider registry 登記。
        // 身體、核心圈、反擊預警都在 Editor 預建；核心圈沒有 Collider，點地射線可穿過。
        private static AbyssalVanguardTarget CreateAbyssalVanguard(Materials materials)
        {
            GameObject root = new GameObject("AbyssalVanguard_Objective");
            var vanguardTuning = new AbyssalVanguardTuning();
            root.transform.position = new Vector3(vanguardTuning.CoreX, 0f, vanguardTuning.CoreZ);
            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1.1f, 0f);
            capsule.height = 2.2f;
            capsule.radius = 0.8f;
            capsule.enabled = false;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Vanguard_Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            body.transform.localScale = new Vector3(1.6f, 1.1f, 1.6f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            bodyRenderer.sharedMaterial = materials.Vanguard;
            bodyRenderer.enabled = false;

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "Vanguard_CoreCircle";
            core.transform.SetParent(root.transform, false);
            core.transform.localPosition = new Vector3(0f, 0.045f, 0f);
            float coreDiameter = vanguardTuning.CoreRadius * 2f;   // 與判定半徑同源
            core.transform.localScale = new Vector3(coreDiameter, 0.015f, coreDiameter);
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.layer = IgnoreRaycastLayer;
            Renderer coreRenderer = core.GetComponent<Renderer>();
            coreRenderer.sharedMaterial = materials.VanguardCore;
            coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
            coreRenderer.enabled = false;

            GameObject warning = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            warning.name = "Vanguard_CounterattackWarning";
            warning.transform.SetParent(root.transform, false);
            warning.transform.localPosition = new Vector3(0f, 0.065f, 0f);
            warning.transform.localScale = new Vector3(10f, 0.012f, 10f);
            Object.DestroyImmediate(warning.GetComponent<Collider>());
            warning.layer = IgnoreRaycastLayer;
            Renderer warningRenderer = warning.GetComponent<Renderer>();
            warningRenderer.sharedMaterial = materials.VanguardWarning;
            warningRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            warningRenderer.receiveShadows = false;
            warningRenderer.enabled = false;

            AbyssalVanguardTarget target = root.AddComponent<AbyssalVanguardTarget>();
            SetReference(target, "_bodyRenderer", bodyRenderer);
            SetReference(target, "_coreRenderer", coreRenderer);
            SetReference(target, "_warningRenderer", warningRenderer);
            SetReference(target, "_neutralMaterial", materials.Vanguard);
            SetReference(target, "_blueMaterial", materials.BehemothBlue);
            SetReference(target, "_redMaterial", materials.BehemothRed);
            SetFloat(target, "_maxHealth", 0f);
            SetEnum(target, "_faction", (int)Faction.Neutral);

            TargetOverheadDisplay overhead = root.AddComponent<TargetOverheadDisplay>();
            SetReference(overhead, "_barMaterial", materials.Bar);
            SetFloat(overhead, "_height", 2.5f);
            return target;
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
            // 批 3 §4-2：兩面灰色測試牆視為**中立**牆——砸碎給盾、點得到、子彈擋得下。
            // 明寫而不是靠欄位初始值：序列化的預設值若哪天變成 0（BlueTeam）會讓它靜默變成「自家牆」。
            SetEnum(target, "_ownerFaction", (int)Faction.Neutral);

            TargetOverheadDisplay overhead = wall.AddComponent<TargetOverheadDisplay>();
            SetReference(overhead, "_barMaterial", barMaterial);
            SetFloat(overhead, "_height", 1.9f); // 自石牆中心 (y=1.25) 起算，落在牆頂上方
        }

        // 符印石牆池：上限 2 面 + 1 面坍塌緩衝（計畫書 §4 假設 9）。平時保持 GameObject 啟用、
        // 只關 Collider／Renderer——RuneWall.Awake 會在建立當下立刻把自己關成「待命」狀態。
        // 執行期禁止 CreatePrimitive（IL2CPP 剔除），這裡是 Editor-only 程式碼，不受此限。
        private const int RuneWallPoolSize = 3;
        // 3 面＝同時存活上限 2 ＋ 1 面坍塌緩衝（與玩家池同結構）。少了緩衝格，池滿時 FIFO 擠掉最舊
        // 那面永遠走不到，除錯鈕會變成沒反應（r1 對抗審查 CRITICAL-1）。
        private const int EnemyWallPoolSize = 3;

        // 批 3 §4-1：石牆**回到 Default 層**。批 1 靠 Ignore Raycast 層讓點擊射線穿過自家牆，代價是
        // 所有符印石牆都無法被點擊鎖定（全專案唯一的選取路徑就是那條射線），而 GDD §參-2 的
        // 「近戰砸碎敵方／中立石牆得護盾」需要牆打得到。現在改成「射線照常打到牆，再做陣營校驗、
        // 己方牆沿射線往後找」（PlayerInputService.OnWorldTap ＋ TapPickLogic）。
        // 分陣營圖層的做法被否決（要寫 TagManager.asset，且層 2 目前同時住著英雄本體、邊界牆、GRID 疊圖、
        // 血條四邊形與技能預警，把層 2 加進點擊 mask 會讓這五類全部變成可點）。
        // 身體阻擋不受影響：HeroLocomotion 的 SphereCast 用 Physics.AllLayers，兩種層都擋得住。
        private static Transform CreateRuneWallPool(RuneTuning runeTuning, Material material, Material barMaterial)
        {
            GameObject root = new GameObject("RuneWallPool");
            for (int i = 0; i < RuneWallPoolSize; i++)
                CreatePooledRuneWall(root.transform, "RuneWall_Pool_" + i, runeTuning, material, barMaterial, Faction.Neutral);
            return root.transform;
        }

        // 除錯鈕用的敵方（紅隊）石牆池。獨立於玩家名冊（§4-6）：敵方牆不得佔用玩家的 2 面上限，
        // 所以兩個池各有一個父物件，Phase1Bootstrap 依父物件分割，不再用 FindObjectsOfType 整批當池。
        private static Transform CreateEnemyWallPool(RuneTuning runeTuning, Material material, Material barMaterial)
        {
            GameObject root = new GameObject("EnemyWallPool");
            for (int i = 0; i < EnemyWallPoolSize; i++)
                CreatePooledRuneWall(root.transform, "EnemyWall_Pool_" + i, runeTuning, material, barMaterial, Faction.RedTeam);
            return root.transform;
        }

        private static void CreatePooledRuneWall(Transform parent, string objectName, RuneTuning runeTuning,
                                                 Material material, Material barMaterial, Faction ownerFaction)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); // 自帶 BoxCollider（紅線 5）
            wall.name = objectName;
            wall.transform.SetParent(parent, false);
            wall.transform.position = new Vector3(0f, runeTuning.WallHeight * 0.5f, 0f);
            wall.transform.localScale = new Vector3(runeTuning.WallWidth, runeTuning.WallHeight, runeTuning.WallThickness);
            wall.GetComponent<Renderer>().sharedMaterial = material;

            RuneWall runeWall = wall.AddComponent<RuneWall>();
            SetFloat(runeWall, "_maxHealth", runeTuning.WallMaxHealth);
            SetEnum(runeWall, "_faction", (int)Faction.DestructibleWall);
            SetEnum(runeWall, "_ownerFaction", (int)ownerFaction); // RuneWall.Activate 施放時會覆寫成實際擁有者

            // 頭頂血條：砸牆／穿透的進度要看得見（r1 對抗審查 MEDIUM-4／LOW-1；V8-② 靠它量）。
            // 與木樁、測試牆同一個既有元件，全部物件在 Awake 預熱，戰鬥中零配置。
            TargetOverheadDisplay overhead = wall.AddComponent<TargetOverheadDisplay>();
            SetReference(overhead, "_barMaterial", barMaterial);
            SetFloat(overhead, "_height", runeTuning.WallHeight * 0.5f + 0.9f); // 與同深度木樁血條留出畫面間距
        }

        // 友軍測試砲台（§4-7）：固定在 (−8, 1.0, 6)，開火方向於 Initialize 時朝木樁 (0,1,6) 算出＝+X、距離 8m。
        // 這條 z=6 的橫向走廊與英雄出生點 (0,0,0)、TestWall_A（z∈[2.7,3.3]）、TestWall_B（x∈[6.7,7.3]）皆不相交，
        // 所以不改變任何既有測試的幾何。**沒有 Collider**：不擋路、不吃點擊。子彈池預建 4 發（執行期禁止 CreatePrimitive）。
        private static TestTurret CreateTurretAndBullets(Material turretMaterial, Material bulletMaterial)
        {
            ProjectileTuning projectileTuning = new ProjectileTuning();

            GameObject turretObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            turretObject.name = "TestTurret";
            Object.DestroyImmediate(turretObject.GetComponent<BoxCollider>()); // §3：砲台不給 Collider
            turretObject.transform.position = new Vector3(-8f, 1f, 6f);
            turretObject.transform.localScale = new Vector3(0.6f, 0.6f, 1.2f);
            turretObject.GetComponent<Renderer>().sharedMaterial = turretMaterial;
            turretObject.layer = IgnoreRaycastLayer; // 沒有 Collider，這裡只是把意圖寫死

            GameObject bulletRoot = new GameObject("BulletPool");
            Projectile[] pool = new Projectile[projectileTuning.BulletPoolSize];
            for (int i = 0; i < pool.Length; i++)
            {
                GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bullet.name = "Bullet_" + i;
                Object.DestroyImmediate(bullet.GetComponent<BoxCollider>()); // 子彈不擋路、不進 NavGrid
                bullet.transform.SetParent(bulletRoot.transform, false);
                bullet.transform.localScale = new Vector3(0.22f, 0.22f, 0.5f);
                bullet.layer = IgnoreRaycastLayer;

                Renderer bulletRenderer = bullet.GetComponent<Renderer>();
                bulletRenderer.sharedMaterial = bulletMaterial;
                bulletRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bulletRenderer.receiveShadows = false;
                bulletRenderer.enabled = false;

                Projectile projectile = bullet.AddComponent<Projectile>();
                SetReference(projectile, "_visual", bulletRenderer);
                pool[i] = projectile;
            }

            TestTurret turret = turretObject.AddComponent<TestTurret>();
            SetObjectArray(turret, "_pool", pool);
            return turret;
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

        // 批 4：元素區域視覺池。**池 8 個 > 同時存活上限 6**（`ElementTuning.MaxLiveZones`）——
        // 池等於上限時「擠掉剩餘時間最短那個」永遠走不到，第 7 次施放在玩家眼裡就是按鈕沒反應
        //（批 3 r1 CRITICAL-1 同型；ElementField.Initialize 另有 LogError 守它）。
        // 扁圓柱**沒有 Collider**：不擋路、不吃點擊、不進 NavGrid（V4-q）。執行期禁 CreatePrimitive，故預建於此。
        private const int ElementZonePoolSize = 8;

        private static Transform CreateElementZonePool(Materials materials)
        {
            // 索引對齊 ElementZoneKind：0=None（不用）、1=Water、2=Burning、3=Quicksand、4=Steam
            Material[] kindMaterials =
            {
                null, materials.ZoneWater, materials.ZoneBurning, materials.ZoneQuicksand, materials.ZoneSteam
            };

            GameObject root = new GameObject("ElementZonePool");
            for (int i = 0; i < ElementZonePoolSize; i++)
            {
                GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                zone.name = "ElementZone_" + i;
                Object.DestroyImmediate(zone.GetComponent<Collider>()); // 區域不擋路、不吃射線
                zone.transform.SetParent(root.transform, false);
                zone.transform.localScale = new Vector3(1f, 0.02f, 1f);
                zone.layer = IgnoreRaycastLayer;

                Renderer zoneRenderer = zone.GetComponent<Renderer>();
                zoneRenderer.sharedMaterial = materials.ZoneWater;
                zoneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                zoneRenderer.receiveShadows = false;
                zoneRenderer.enabled = false;

                ElementZoneView view = zone.AddComponent<ElementZoneView>();
                SetReference(view, "_visual", zoneRenderer);
                SetObjectArray(view, "_kindMaterials", kindMaterials);
            }
            return root.transform;
        }

        // ───────────────────────── 板塊佔領（V080_CAPTURE_PLAN.md E23、E30；v0.9.0 19 塊：V090_ENCIRCLE_PLAN.md E1～E3、E26）─────────────────────────
        // 19 塊地板、19 座塔、19 個光圈、19 個進度盤全部在這裡預建（執行期禁止 CreatePrimitive）；陣列索引＝板塊索引。
        // 幾何的單一事實來源是 CaptureBoardSpec.V090Nineteen（與 Phase1Bootstrap 用的是同一份）。
        // **一個 Collider 都沒有**（E23）：不擋路、不吃點擊射線、不登記進 BlockGrid，格點凍結常數與繞牆測試不受影響。
        // 根物件預設關閉：不按 CAPTURE 時整組不啟用（§2.2、V-B01）。
        // E26：新的資產路徑。建置器遇到已存在的資產會直接載入，沿用 v0.8.0 的 VOW_HexTile.asset 會拿到 7m 的網格（R5）。
        private const string HexTileMeshPath = "Assets/Settings/VOW_HexTile19.asset";
        private const float HexVisualInset = 0.96f;   // 只縮地板網格讓相鄰兩塊之間露出縫；判定幾何一律以 CaptureBoardSpec 為準
        private const float TowerRadius = 0.35f;
        private const float TowerHeight = 2.4f;

        private static CaptureBoardView CreateCaptureBoard(Materials materials)
        {
            CaptureBoardSpec spec = CaptureBoardSpec.V090Nineteen;
            Mesh hexMesh = EnsureHexTileMesh(spec);
            float ringDiameter = new CaptureTuning().CircleRadius * 2f;   // 光圈半徑的單一事實來源

            GameObject root = new GameObject("CaptureBoard");
            root.layer = IgnoreRaycastLayer;

            int count = spec.TileCount;
            Renderer[] floors = new Renderer[count];
            Renderer[] towers = new Renderer[count];
            Renderer[] rings = new Renderer[count];
            Renderer[] discs = new Renderer[count];
            for (int i = 0; i < count; i++)
            {
                GameObject tile = new GameObject("CaptureTile_" + i);
                tile.layer = IgnoreRaycastLayer;
                tile.transform.SetParent(root.transform, false);
                tile.transform.localPosition = new Vector3(spec.CenterX(i), 0f, spec.CenterZ(i));

                GameObject floor = new GameObject("Floor_" + i);
                floor.layer = IgnoreRaycastLayer;
                floor.transform.SetParent(tile.transform, false);
                floor.transform.localPosition = new Vector3(0f, 0.01f, 0f);
                floor.AddComponent<MeshFilter>().sharedMesh = hexMesh;
                floors[i] = ConfigureBoardRenderer(floor.AddComponent<MeshRenderer>(), materials.CaptureNeutral, 0);

                GameObject tower = CreateColliderlessCylinder("Tower_" + i, tile.transform,
                    new Vector3(0f, TowerHeight * 0.5f, 0f), new Vector3(TowerRadius * 2f, TowerHeight * 0.5f, TowerRadius * 2f));
                towers[i] = ConfigureBoardRenderer(tower.GetComponent<Renderer>(), materials.CaptureNeutral, 3);

                GameObject ring = CreateColliderlessCylinder("Ring_" + i, tile.transform,
                    new Vector3(0f, 0.02f, 0f), new Vector3(ringDiameter, 0.005f, ringDiameter));
                rings[i] = ConfigureBoardRenderer(ring.GetComponent<Renderer>(), materials.CaptureNeutral, 1);

                GameObject disc = CreateColliderlessCylinder("Progress_" + i, tile.transform,
                    new Vector3(0f, 0.035f, 0f), new Vector3(0.01f, 0.01f, 0.01f));
                discs[i] = ConfigureBoardRenderer(disc.GetComponent<Renderer>(), materials.CaptureBlue, 2);
                discs[i].enabled = false; // 引導中才顯示
            }

            CaptureBoardView view = root.AddComponent<CaptureBoardView>();
            SetObjectArray(view, "_floors", floors);
            SetObjectArray(view, "_towers", towers);
            SetObjectArray(view, "_rings", rings);
            SetObjectArray(view, "_progressDiscs", discs);
            SetReference(view, "_neutralMaterial", materials.CaptureNeutral);
            SetReference(view, "_blueMaterial", materials.CaptureBlue);
            SetReference(view, "_redMaterial", materials.CaptureRed);

            root.SetActive(false);
            return view;
        }

        private static GameObject CreateColliderlessCylinder(string objectName, Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = objectName;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // E23：不擋路、不吃點擊、不進 BlockGrid
            go.layer = IgnoreRaycastLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            return go;
        }

        // 半透明疊在棋盤地板上；sortingOrder 固定疊放順序（地板 < 光圈 < 進度盤 < 塔），避免同高度的透明物件互相搶排序。
        private static Renderer ConfigureBoardRenderer(Renderer renderer, Material material, int sortingOrder)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        // 平頂六角形地板網格（v0.9.0 E1／E2：頂點 (±4.375,0)、(±2.1875,±3.7890625)，取自 spec 本身），乘上 HexVisualInset 露縫。
        // 已存在就就地覆寫內容，資產 GUID 不變（場景引用與 .meta 不會每次重建都換）。
        private static Mesh EnsureHexTileMesh(CaptureBoardSpec spec)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(HexTileMeshPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "VOW_HexTile19";

            Vector3[] vertices = new Vector3[7];
            Vector3[] normals = new Vector3[7];
            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            for (int v = 0; v < 6; v++)
            {
                vertices[v + 1] = new Vector3((spec.VertexX(0, v) - spec.CenterX(0)) * HexVisualInset, 0f,
                                              (spec.VertexZ(0, v) - spec.CenterZ(0)) * HexVisualInset);
                normals[v + 1] = Vector3.up;
            }

            // spec 的頂點由上往下看是逆時針；Unity 正面是順時針，所以三角形寫成 (中心, v+1, v)。
            int[] triangles = new int[18];
            for (int v = 0; v < 6; v++)
            {
                triangles[v * 3] = 0;
                triangles[v * 3 + 1] = (v + 1) % 6 + 1;
                triangles[v * 3 + 2] = v + 1;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            if (created) AssetDatabase.CreateAsset(mesh, HexTileMeshPath);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // v0.9.0 E18：兩個狂怒光環（半徑 1.2m、y＝0.05 的扁圓盤、VOW_Rage 不受光材質）。**獨立的根物件**，不掛在英雄／對手底下
        //（R4：既有 AssertBody 會斷言英雄／對手底下全部 Renderer 的開關）；由 RageAuraView 在 LateUpdate 跟隨身體 xz。
        // 沒有 Collider：不擋路、不吃點擊、不進 BlockGrid。預設不顯示（狂怒中才開）。
        private const float RageAuraRadius = 1.2f;
        private const float RageAuraHeight = 0.05f;

        private static RageAuraView CreateRageAuras(Material rageMaterial)
        {
            Renderer blue = CreateRageAuraDisc("RageAura_Blue", rageMaterial);
            Renderer red = CreateRageAuraDisc("RageAura_Red", rageMaterial);

            GameObject viewObject = new GameObject("RageAuraView");
            RageAuraView view = viewObject.AddComponent<RageAuraView>();
            SetReference(view, "_blueAura", blue);
            SetReference(view, "_redAura", red);
            return view;
        }

        private static Renderer CreateRageAuraDisc(string objectName, Material material)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = objectName;
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.layer = IgnoreRaycastLayer;
            disc.transform.position = new Vector3(0f, RageAuraHeight, 0f);
            disc.transform.localScale = new Vector3(RageAuraRadius * 2f, 0.005f, RageAuraRadius * 2f);
            Renderer renderer = disc.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        // 批 4：擴散火浪的扇形預警載體（§4-6，不經 ISkillTelegraphService）。自有一條 LineRenderer。
        private static SectorTelegraph CreateSectorTelegraph(Material lineMaterial)
        {
            GameObject go = new GameObject("SectorTelegraph");
            go.layer = IgnoreRaycastLayer;

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.alignment = LineAlignment.TransformZ;
            line.numCapVertices = 0;
            line.widthMultiplier = 0.18f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (lineMaterial != null) line.sharedMaterial = lineMaterial;
            line.startColor = new Color(1f, 0.6f, 0.15f, 0.95f);
            line.endColor = new Color(1f, 0.6f, 0.15f, 0.95f);
            line.enabled = false;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 線寬攤平在地面上（同 SkillTelegraphService）

            SectorTelegraph telegraph = go.AddComponent<SectorTelegraph>();
            SetReference(telegraph, "_line", line);
            return telegraph;
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

        private static void CreateSystems(HeroController hero, TrainingOpponent opponent, Camera camera, FollowCameraRig rig, Transform shakePivot,
            Materials materials, HeroTuningAsset tuning, RuneGhostPreview runeGhost, NavGridDebugView navGridDebug,
            Transform arenaBoundary, Transform runeWallPool, Transform enemyWallPool, TestTurret turret,
            Transform elementZonePool, CaptureBoardView captureBoard, RageAuraView rageAuras,
            AbyssalVanguardTarget vanguardTarget, Transform canyonTerrain, Transform cliffBarriers, Collider flatGround)
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
            SkillTelegraphService opponentTelegraph = systems.AddComponent<SkillTelegraphService>();
            SetReference(opponentTelegraph, "_lineMaterial", materials.Telegraph);
            SetColor(opponentTelegraph, "_baseColor", new Color(1f, 0.12f, 0.1f, 1f));

            HitboxVisualizer hitboxes = systems.AddComponent<HitboxVisualizer>();
            SetReference(hitboxes, "_lineMaterial", materials.HitboxLines);
            DebugHud hud = systems.AddComponent<DebugHud>();
            CadenceAimPreview aimPreview = systems.AddComponent<CadenceAimPreview>();

            RuneCaster runeCaster = systems.AddComponent<RuneCaster>();
            RuneButtonView runeButton = systems.AddComponent<RuneButtonView>();
            EnemyWallSpawner enemyWalls = systems.AddComponent<EnemyWallSpawner>();
            ElementField elementField = systems.AddComponent<ElementField>();
            SectorTelegraph sectorTelegraph = CreateSectorTelegraph(materials.Telegraph);

            Phase1Bootstrap bootstrap = systems.AddComponent<Phase1Bootstrap>();
            SetReference(bootstrap, "_hero", hero);
            SetReference(bootstrap, "_opponent", opponent);
            SetReference(bootstrap, "_opponentTelegraph", opponentTelegraph);
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
            SetReference(bootstrap, "_runeWallPool", runeWallPool);
            SetReference(bootstrap, "_enemyWallPool", enemyWallPool);
            SetReference(bootstrap, "_turret", turret);
            SetReference(bootstrap, "_enemyWalls", enemyWalls);
            SetReference(bootstrap, "_shield", hero.GetComponent<RockShieldBehaviour>());
            SetReference(bootstrap, "_shieldBar", hero.GetComponent<HeroShieldBar>());
            SetReference(bootstrap, "_elementField", elementField);
            SetReference(bootstrap, "_sectorTelegraph", sectorTelegraph);
            SetReference(bootstrap, "_elementZonePool", elementZonePool);
            SetReference(bootstrap, "_captureBoard", captureBoard);
            SetReference(bootstrap, "_rageAuras", rageAuras);
            SetReference(bootstrap, "_vanguardTarget", vanguardTarget);
            SetReference(bootstrap, "_canyonTerrain", canyonTerrain);
            SetReference(bootstrap, "_cliffBarriers", cliffBarriers);
            SetReference(bootstrap, "_flatGround", flatGround);
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

        private static void SetColor(Object target, string propertyName, Color value)
        {
            SerializedObject serialized = new SerializedObject(target);
            RequireProperty(serialized, propertyName).colorValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 寫 intValue（列舉的數值）而非 enumValueIndex（宣告順序）：日後有人替列舉指定明碼值時，兩者會不同而靜默寫錯。
        private static void SetEnum(Object target, string propertyName, int enumValue)
        {
            SerializedObject serialized = new SerializedObject(target);
            RequireProperty(serialized, propertyName).intValue = enumValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 物件陣列欄位（砲台的子彈池）。陣列長度與每一格都寫進去，欄位名打錯一樣會丟例外。
        private static void SetObjectArray(Object target, string propertyName, Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = RequireProperty(serialized, propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
