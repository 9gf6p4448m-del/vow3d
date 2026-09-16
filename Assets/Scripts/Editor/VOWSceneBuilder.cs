#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 《VOW 誓約》Unity 編輯器一鍵自動搭建工具
/// 點擊頂部選單【VOW 誓約 ➔ 一鍵生成 Phase 1 測試場景】即可自動配置全部物件！
/// </summary>
public class VOWSceneBuilder : Editor
{
    [MenuItem("VOW 誓約/一鍵生成 Phase 1 測試場景 (Setup Arena)", false, 1)]
    public static void BuildPhase1TestScene()
    {
        // 1. 建立全新場景
        var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 2. 設置相機俯視角
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.transform.position = new Vector3(0f, 16f, -12f);
            mainCam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        }

        // 3. 建立地面 (Ground)
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground_Arena";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(6f, 1f, 6f); // 60x60 大平原

        // 4. 建立石牆 Prefab 物件
        GameObject wallObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallObj.name = "DynamicWall_Prefab";
        wallObj.transform.localScale = new Vector3(3f, 2.5f, 0.8f);
        var wallComp = wallObj.AddComponent<DynamicOBBWall>();
        var obstacle = wallObj.AddComponent<NavMeshObstacle>();
        obstacle.carving = true;

        // 5. 建立主角英雄 (Hero)
        GameObject hero = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        hero.name = "VOW_Hero";
        hero.transform.position = new Vector3(0f, 1f, -5f);
        hero.tag = "Player";

        var agent = hero.AddComponent<NavMeshAgent>();
        agent.speed = 6.5f;
        agent.acceleration = 30f;
        agent.stoppingDistance = 0.1f;

        var cadence = hero.AddComponent<MicroFlickCadenceController>();
        var caster = hero.AddComponent<RuneVectorCaster>();
        var router = hero.AddComponent<CrossPlatformInputRouter>();
        var netPredictor = hero.AddComponent<NetworkCadencePredictor>();

        // 關聯牆體預製體
        var serializedCaster = new SerializedObject(caster);
        serializedCaster.FindProperty("wallPrefab").objectReferenceValue = wallObj;
        serializedCaster.ApplyModifiedProperties();

        // 6. 建立敵方英雄木樁 (Enemy Hero Dummy - PvP 目標)
        GameObject enemyHero = GameObject.CreatePrimitive(PrimitiveType.Cube);
        enemyHero.name = "Dummy_EnemyHero (PvP)";
        enemyHero.transform.position = new Vector3(0f, 1f, 3f);
        enemyHero.tag = "Hero";
        var enemyTarget = enemyHero.AddComponent<CombatTarget>();
        var serEnemy = new SerializedObject(enemyTarget);
        serEnemy.FindProperty("targetType").enumValueIndex = (int)CombatTarget.TargetType.Hero;
        serEnemy.ApplyModifiedProperties();

        // 7. 建立小兵木樁 (Minion Dummy - PvE 目標)
        GameObject minion = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        minion.name = "Dummy_Minion (PvE)";
        minion.transform.position = new Vector3(4f, 1f, -1f);
        minion.tag = "Untagged";
        var minionTarget = minion.AddComponent<CombatTarget>();
        var serMinion = new SerializedObject(minionTarget);
        serMinion.FindProperty("targetType").enumValueIndex = (int)CombatTarget.TargetType.Minion;
        serMinion.ApplyModifiedProperties();

        // 8. 保存場景
        string sceneDir = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(sceneDir))
        {
            AssetDatabase.CreateFolder("Assets", "Scenes");
        }
        string scenePath = $"{sceneDir}/Phase1_Arena.unity";
        EditorSceneManager.SaveScene(newScene, scenePath);

        Debug.Log($"<color=green>★★★★★【場景生成成功！】已儲存至: {scenePath} ★★★★★</color>");
        Debug.Log("<color=cyan>提示：請在 Window > AI > Navigation 中對 Ground_Arena 進行 Bake 烘焙即可直接按下 Play 測試！</color>");
    }
}
#endif
