# 《VOW 誓約》工程技術架構白皮書 (Technical Architecture Specification)

> **版本**：v1.0.0 (Phase 1 Engineering Contract)  
> **適用目標**：Claude Code、Codex、以及全體一線客戶端/後端工程師  
> **核心準則**：本文件為不可逾越的工程底線。任何代碼變更必須嚴格遵守本規格書定義的技術棧、介面隔離 (Interface Decoupling) 與性能約束。

---

## 壹、 基礎技術棧與環境鎖定 (Target Tech Stack)

| 組件 | 規範版本 | 強制要求與說明 |
| :--- | :--- | :--- |
| **遊戲引擎** | **Unity 2022.3 LTS** (推薦 2022.3.20f1+) | 嚴禁使用 Unity 6 或 2021 舊版，確保套件生態穩定。 |
| **渲染管線** | **Universal Render Pipeline (URP)** | 啟用 Forward+ 渲染路徑，禁用 Built-in 管線。 |
| **輸入系統** | **Unity New Input System** (`com.unity.inputsystem` 1.7+) | **嚴禁使用舊版 `Input.GetMouseButton` / `Input.touch`**。所有輸入必須經由 `EnhancedTouch` 或 Input Actions 抽象。 |
| **物理引擎** | **Unity 3D Physics (PhysX)** | 碰撞器一律採用 Primitive Collider (`BoxCollider`, `CapsuleCollider`)，禁用 MeshCollider 動態更新。 |
| **尋路系統** | **Unity NavMesh (100% 靜態預烘焙)** | **嚴禁使用 `NavMeshObstacle.carving = true`**！石牆僅使用實體 `BoxCollider` 阻擋，尋路走局部向量場/RVO 避障。 |
| **目標平台** | **iOS / Android / PC Steam** | iOS 走 Metal，Android 走 Vulkan，PC 走 DX11/DX12。鎖定 120 FPS 原生運行。 |

---

## 貳、 第一階段（Phase 1）開發邊界：40m x 40m 灰盒驗證

> **🚨 工程熔斷原則**：  
> **在 Phase 1 期間，全面封存 19 塊圍棋演算法、三段誓約天賦 UI、局外養成、大廳與匹配系統！**  
> 第一階段的全部工程精力，100% 聚焦在一個 **40m x 40m 的平坦灰盒競技場**。

```
[Phase 1 聚焦範疇]
  ├── 1 位測試英雄 (Hero_Player)
  ├── 1 個訓練假人 (Dummy_Target)
  ├── 純點擊地圖移動 (Pure Tap Navigation)
  ├── 普攻切後搖 + 3 格充能微衝刺 (1.4m → 0.9m → 0.5m 動能衰減)
  ├── 模式 A (全螢幕微彈) vs 模式 B (左手微輪盤 + 右手點擊) 雙模切換
  ├── 50ms ~ 80ms 網路模擬延遲注入 (Netcode 前置測試)
  ├── CoreHaptics 震覺疲勞管理 (僅微滑步重震，常規平A輕震/靜音)
  └── 一鍵渲染 Hitbox 與 VFX 發光邊界框 (Day 1 可視化調試開關)
```

**驗收標準**：
只有當 10 名測試者在 6.7 吋手機上連續單挑 10 分鐘，確認「指關節零疲勞、無視野遮擋、有如《隻狼》打鐵般心流」，方可解鎖 Phase 2！

---

## 參、 核心介面契約與模組解耦 (Core Interfaces)

為貫徹 SOLID 原則，所有核心邏輯必須面向介面編程，杜絕「單一 MonoBehaviour 巨無霸麵條代碼」：

### 1. 輸入路由服務：`IPlayerInputService`
```csharp
using UnityEngine;
using System;

public enum ControlMode
{
    ModeA_FullScreenFlick,  // 模式 A：純粹全螢幕微彈
    ModeB_DualZonePip       // 模式 B：左手身位引導微輪盤 + 右手目標點擊
}

public interface IPlayerInputService
{
    ControlMode ActiveMode { get; set; }
    
    // 點擊移動與目標選擇事件
    event Action<Vector3> OnMoveDestinationSelected;
    event Action<ICombatTarget> OnCombatTargetSelected;
    
    // 微滑步身位向量輸入 (已考慮物理毫米 PPI 換算與邊緣防誤觸)
    event Action<Vector2> OnCadenceVectorFlicked;
    
    // 符印石牆長按/雙擊事件 (強制阻斷向普攻控制器滲透)
    event Action<Vector2, float> OnRuneVectorDragUpdated; // 旋轉角度與距離
    event Action OnRuneQuickCastTriggered;
    event Action OnRuneCastCancelled;
}
```

### 2. 微操位移執行器：`ICadenceMover`
```csharp
using UnityEngine;
using System;

public interface ICadenceMover
{
    int CurrentCharges { get; }
    int MaxCharges { get; }
    float ChargeRecoveryNormalized { get; } // 0.0 ~ 1.0 (2.5秒一格)
    
    // 執行位移 (內建 1.0s 窗口動能衰減：1.4m → 0.9m → 0.5m)
    bool TryExecuteCadenceDash(Vector3 worldDirection);
    
    event Action<int> OnChargesChanged;
    event Action<float> OnDashExecuted; // 傳出實際位移距離
    event Action OnCadenceExhausted;    // 充能歸零
}
```

### 3. 戰鬥目標抽象：`ICombatTarget`
```csharp
using UnityEngine;

public interface ICombatTarget
{
    Transform TargetTransform { get; }
    bool IsAlive { get; }
    Faction TargetFaction { get; }
    
    void ReceiveDamage(float amount, DamageType type, GameObject instigator);
    bool CanBeTargetedBy(Faction attackerFaction);
}

public enum Faction
{
    BlueTeam,
    RedTeam,
    Neutral,
    DestructibleWall // 石牆為獨立陣營，供近戰破牆護盾校驗
}
```

### 4. 符印掩體介面：`IRuneWall`
```csharp
using UnityEngine;
using System;

public interface IRuneWall : ICombatTarget
{
    float RemainingLifespan { get; }
    int MaxPenetrationCount { get; } // 10 發
    int CurrentPenetrationCount { get; }
    
    // 友軍彈道穿透：前 5 發無衰減，每發扣 10% 耐久與 0.5s 壽命
    bool TryPenetrateBullet(Vector3 bulletVelocity, out float damageMultiplier);
    
    // 坍塌回調 (杜絕全圖 NavMesh 烘焙)
    void CollapseWall(bool crushedByMelee);
}
```

### 5. 核心英雄狀態機規範：`IPlayerStateMachine`
```csharp
using System;

public enum PlayerState
{
    Idle,               // 待機
    Moving,             // 點地導航步行中
    AttackWindup,       // 普攻前搖 (可被新移動指令打斷)
    AttackRelease,      // 普攻傷害判定幀 (結算傷害 + 開啟 220ms 目押切後搖窗口)
    CadenceDashing,     // 微位移中 (0 幀切除後搖，執行 1.4m/0.9m/0.5m 衰減位移)
    AttackRecovery,     // 常規普攻後搖 (0.15s 平滑收招，絕無卡刀硬直)
    CastingRune         // 符印拖曳/施法狀態
}

public interface IPlayerStateMachine
{
    PlayerState CurrentState { get; }
    bool CanMove { get; }
    bool CanAttack { get; }
    bool IsInCadenceWindow { get; } // 220ms 寬容窗口
    
    void ChangeState(PlayerState newState);
    
    event Action<PlayerState, PlayerState> OnStateChanged; // (oldState, newState)
}
```

**狀態流轉約束**：
* `AttackRelease` 狀態下，若收到 `OnCadenceVectorFlicked` 且充能 > 0，**0 幀立即轉換為 `CadenceDashing`**，消耗 1 格充能。
* `AttackRelease` 狀態結束時若無微彈，**平滑切換至 `AttackRecovery` (0.15s)**，期間狂點螢幕平穩排隊，**嚴禁觸發卡刀硬直**。

### 6. 板塊佔領唯讀視圖：`ICaptureMatchView`
```csharp
using Vow.Core.Logic; // CaptureMatchState、CaptureMatchResult

public interface ICaptureMatchView
{
    CaptureMatchState State { get; }          // Off / Lobby / Active / Ended
    int TileCount { get; }                    // 19（中央 1＋中圈 6＋外圈 12；v0.8.0 七塊夾具為 7）
    Faction OwnerOf(int tileIndex);           // Neutral / BlueTeam / RedTeam，不新增 enum

    int BlueScore { get; }
    int RedScore { get; }

    int BlueChannelingTile { get; }           // -1＝沒有在引導
    float BlueChannelProgress { get; }        // 秒，滿 3.5 翻塊
    int RedChannelingTile { get; }
    float RedChannelProgress { get; }

    bool BlueKnockedOut { get; }
    float BlueRespawnRemaining { get; }       // 秒，倒數 5.0
    bool RedKnockedOut { get; }
    float RedRespawnRemaining { get; }

    float BlueRageRemaining { get; }          // 秒，劣勢狂怒剩餘（觸發設為 12.0）；> 0 即生效
    float RedRageRemaining { get; }

    CaptureMatchResult Result { get; }        // 本局結果（Ended 時有值）
    CaptureMatchResult LastResult { get; }    // 上一局結果（回待機後保留顯示）
}
```

**契約約束**（`docs/V080_CAPTURE_PLAN.md` §2.1-2；v0.9.0 擴充見 `docs/V090_ENCIRCLE_PLAN.md` §2.1-2）：
* 唯一的事實來源是 `Vow.Core.Logic.CaptureMatchLogic`（零 UnityEngine 的純邏輯）；本介面**唯讀**，所有寫入（`Tick`、受傷、倒地、開局）只經組裝根 `Phase1Bootstrap`。
* HUD（Vow.UI）、板塊顯示（Vow.Combat 的 `CaptureBoardView`）、輸入路由（Vow.Input 的 `DuelInputRouter`）**只依賴這個介面**，不直接碰 `CaptureMatchLogic`。
* 純邏輯用 int 陣營代碼（Blue=0、Red=1、Neutral=2）；轉成 `Faction` 的轉接在 Bootstrap 層（`Vow.Bootstrap.CaptureMatchView`）——`CaptureMatchLogic` 不得引用定義在含 `using UnityEngine` 檔案裡的 `Faction`。第一次開局之前 `OwnerOf` 一律回 `Neutral`。
* `ICombatTarget`、`ISkillTelegraphService`、`IPlayerInputService` 的簽章不因佔領模式改動。

---

## 肆、 現代網絡架構規範 (Authoritative Netcode)

1. **狀態同步頻率 (Tick Rate)**：
   - 伺服器以 **30 Tick / 60 Tick** 執行權威模擬（Authoritative Simulation）。
   - 客戶端輸入包 (`InputPacket`) 包含：時間戳記、移動目標點、普攻指令、滑動向量角度。
2. **本地樂觀預測 (Client-Side Prediction)**：
   - 客戶端在發送滑步指令的同時，**0 幀立即播放本地動畫與 1.4m 微位移**。
   - 收到伺服器狀態包時，若位置偏差在 0.2m 內不予拉扯；若偏差 > 0.2m，在 **3 幀 (33ms) 內線性插值 (Smooth Interpolation)** 柔和校正。
3. **伺服器防作弊限流 (Anti-Cheat / Anti-Macro)**：
   - 伺服器端檢測輸入時間戳：若連續 5 次點擊間隔標準差小於 2ms（機械化巨集/滑鼠滾輪），伺服器強制判定為普通點擊，拒絕響應切後搖判定。

---

## 伍、 開發紅線與代碼健康度清單 (Non-Negotiables)

* [ ] **絕對禁止 `TriggerCadenceJam()`**：檢測到高頻點擊時，一律平滑轉為常規平 A，嚴禁讓玩家角色原地硬直罰站。
* [ ] **絕對禁止走A 增加攻速**：`AttackPeriod` 必須嚴格保持恆定，走A 僅切除後搖並位移，不縮短整體攻擊週期。
* [ ] **絕對禁止主線程 GC Alloc**：`Update()`、`FixedUpdate()` 內嚴禁 `new` 任何對象（包括閉包、委託、LINQ），全面使用對象池 (Object Pooling)。
* [ ] **絕對禁止 `obstacle.carving = true`**：動態石牆只准開啟 `BoxCollider`。
* [ ] **雙層渲染管線守則**：UI Canvas 與輸入採樣永遠鎖死 120Hz，3D 渲染動態解析度允許在低端機/高溫時降至 85%。

---

## 陸、 技能預警指示器與戰鬥打擊反饋架構 (Skill Telegraph & Combat Feedback Architecture)

為實現頂級競技 MOBA 的技能可讀性與《隻狼》般硬核打擊感，借鑒並吸納 `HandCastAbility` 的指示器與程序化反饋管線，規範 Unity URP 下的技能預警與打擊系統：

### 1. 技能預警指示器介面：`ISkillTelegraphService`
```csharp
using UnityEngine;
using System;

public enum TelegraphShape
{
    LineCast,   // 線性技能投射箭頭 (如長矛穿刺、箭矢彈道)
    ZoneCast    // 範圍技能法陣 (如冰霜牢獄、落雷轟炸)
}

public interface ISkillTelegraphService
{
    TelegraphShape ActiveShape { get; }
    bool IsAiming { get; }
    
    // 開啟預警指示器：設定基礎長度、寬度或半徑
    void ShowLineIndicator(Vector3 origin, Vector3 direction, float length, float width);
    void ShowZoneIndicator(Vector3 center, float radius, float edgeThickness);
    
    // 更新瞄準位置 (滑鼠牽引或左/右微輪盤瞄準)
    void UpdateAimTransform(Vector3 currentAimPosition);
    
    // 隱藏與釋放指示器 (0 GC，回收至對象池)
    void HideIndicator();
    
    // 指示器邊界動畫：外衝吸附 (Snap Animation) 與邊界高光
    void TriggerSnapFeedback();
}
```

### 2. 戰鬥打擊反饋管理器：`ICombatFeedbackService`
```csharp
using UnityEngine;
using System;

public interface ICombatFeedbackService
{
    // 1. 創傷阻尼震屏 (Camera Shake)
    // trauma: 0.0 ~ 1.0，平方衰減，頻率與三軸旋轉分離，支援優先級覆蓋
    void RequestCameraShake(float trauma, float duration = 0.2f);

    // 2. 打擊頓挫幀 (Hitstop / Hit Pause)
    // 普攻/暴擊命中瞬間短暫凍結攻擊者與受擊者動畫幀 (30ms~60ms)，強化刀刀入肉感，完美對齊 220ms 目押窗口
    void TriggerHitstop(float durationMs);

    // 3. 高對比度受擊閃白 (Screen Flash)
    // 暴擊/斬殺時的全螢幕微閃，內建防光敏癲癇 (Photosensitivity Safe) 與溫控自動降級
    void TriggerScreenFlash(Color flashColor, float durationMs = 50f);

    // 4. 地面殘留打擊貼花 (Impact Decals)
    // 技能重擊、地裂、冰霜附著殘留，從對象池取出，壽命結束後平滑溶解回收
    void SpawnGroundDecal(Vector3 worldPosition, DecalType type, float duration = 3.0f);
}

public enum DecalType
{
    ScorchCrater,   // 烈焰焦痕
    FrostCrack,     // 冰霜裂紋
    VoidRupture     // 虛空地裂
}
```

### 3. 高效能渲染與對象池合約 (Instancing & Pooling Protocol)
* **GPU 實例化破碎 (Instanced Shatter Mesh)**：
  冰雕碎裂、岩石崩解等大量碎片，嚴禁使用獨立 GameObject，必須透過 `Graphics.RenderMeshInstanced` 單次 Draw Call 繪製，位置與朝向由 GPU 頂點著色器計算。
* **預熱與零運行時 GC (Zero-Alloc Policy)**：
  所有指示器 Quad/Mesh、貼花 Prefab、粒子系統均在場景載入時完成靜態預熱（Prewarm），戰鬥中嚴禁動態 Instantiation。

### 4. 資產與動畫落地標準 (Asset & Animation Pipeline Specification)
* **Phase 1 灰盒角色與手感驗證**：
  - **強制標準 Humanoid 骨骼**：英雄載體強制採用標準 Unity **Humanoid FBX**（首選 Mixamo Y-Bot / X-Bot 或 Unity Starter Assets 測試人偶），嚴禁以純代碼移動未帶骨骼的膠囊體代替真實動畫。
  - **動畫事件契約 (Animation Event Contract)**：普攻揮砍/射擊動作必須在精確傷害判定幀掛載 **`OnAttackHit()`** 事件，用於喚醒 `ICombatFeedbackService`（30~60ms 頓挫）並開啟 220ms 目押走A窗口。
  - **灰盒動作清單**：僅引入標準切片（`Idle`, `Run`, `Attack`, `Dash`, `Hit`），嚴禁在 Phase 1 導入未經剪輯的複雜混合動作。
* **Phase 2+ 正式資產量產標準**：
  - **英雄與角色管線**：優先採用 **Meshy / Tripo3D** 依 2D 概念圖生成風格化高精白模 ➔ 於 Blender 檢驗手肘與肩胛等關節環線（Joint Loops）以防極限形變塌陷 ➔ 綁定標準 Humanoid 骨架導入 Unity。
  - **場景與障礙物管線**：符印石牆（`IRuneWall`）、防禦塔與地貌道具優先採用 **Sloyd / Tripo3D** 參數化生成，面數嚴格收斂在 3,000~6,000 面，確保手機端 120 FPS 渲染預算。

---

> **簽署生效**：  
> 本架構書與 GDD v3.4.1 共同構成《VOW 誓約》最高技術標準。所有使用 Claude Code、Codex 或人工編寫的程式碼，均需通過本架構書中定義的介面規範與單元測試。
