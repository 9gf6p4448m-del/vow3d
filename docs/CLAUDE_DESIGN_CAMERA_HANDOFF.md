# Claude Code：VOW 設計與鏡頭試作交接

日期：2026-10-01。目的：接續本次設計討論，保留正式開發進度，讓鏡頭選擇有試玩依據。
本文件是交接指引；不是整版驗收，也不是第三人稱定案或立即合併的授權。

## 先做什麼

1. 在你目前的 VOW 工作樹確認分支、HEAD、未提交檔案及正在進行的工作；不要把下列快照當成即時狀態。
2. 讀本文件、`docs/DESIGN_BLUEPRINT_20261001.md`、`docs/CAMERA_LAB_TOUCH_PLAN.md`、`docs/CAMERA_LAB_TOUCH_RESULT.md`。
3. 對照你目前的 `GDD.md`、`docs/PLAYER_EXPERIENCE_BLUEPRINT.md` 與當批計畫，整理「一致／衝突／尚未實作／待使用者決定」。保留現有未提交內容，不用試作分支整份覆蓋。
4. 先提出藍圖整合差異與建議順序；使用者未選定鏡頭前，正式版繼續沿原定方案，不啟動全系統第三人稱改造。

## 已選定的產品方向

權威整理：`docs/DESIGN_BLUEPRINT_20261001.md`。它描述設計方向，不宣稱已實作。

- 手機優先；PC 配對應操作，跨端同池尚未承諾。
- 戰術動作競技：身位、攻防、塑牆與元素改變交戰，再影響領地。
- 可主動滑步並保留命中節奏連動；充能、距離與限制未定。
- 普攻依武器區分；近戰方向範圍、投射預判、地面施法是候選方式，具體武器未定。每位英雄先固定招牌武器。
- 人人有基礎塑牆，角色專精造牆、利用牆或破牆。各自管理，新牆只替換自己的舊牆；上限、存續與尺寸未定。
- 個人攻防完整，合作拓展元素組合；不要求每位英雄都有多種元素。
- 延續地脈爭地、晶塔與先鋒焦點；19 板塊先保留並驗證節奏。
- 保留 3v3／5v5，建議先試作 3v3；5v5 的地圖與資訊密度另設計。
- 越到後期死亡越需謹慎；復活秒數與回場距離未定。
- 誓約天賦偏玩法變化，需有取捨且對手可辨識；分支、觸發時機未定。
- 首玩採短情境戰鬥，可跳過與重玩；故事未定。

## 鏡頭方案仍待試玩裁定

- 俯視是目前主方案，第三人稱是比較方案；第一人稱未選定。
- 目前第三人稱：左下浮動搖桿連續移動，右側空白拖曳轉頭，移動依鏡頭朝向；俯仰 10–50 度，初始 25 度。
- UI／Rune 優先；可雙指移動加轉頭。右側短點敵人沿原攻擊，點地面不導航；TOP 切回俯視。
- 未把自由滑步、武器瞄準、個人牆的新規則混入本次鏡頭比較。
- 下一步讓使用者用同一遭遇比較：能否看懂受擊原因、牆與目標位置、移動轉頭是否順手、是否想再玩。
- WebGL 與瀏覽器模擬觸控不能代替原生手機手感或效能驗收，也不能據此宣稱第三人稱更有趣。

## 分支與整合邊界

- 試作工作樹：`C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab`；分支 `camera-lab-20261001`，遠端 `vow3d`。
- 藍圖／初版比較提交 `fe78256`；觸控邏輯 `d60d656`；顯示修補 `0b82bac`；先前紀錄 `b2e0670`。挑選前先看各提交 diff，初版提交同時含文件與程式。
- 正式工作樹快照：`C:/Users/shung/.gemini/antigravity/scratch/vow`，分支 `v0140-abyssal-canyon`，HEAD `1c91bfc9a172d49f2b99564fc85796b5c08f65bf`。
- 該工作樹當時已有 `M GDD.md`、未追蹤 `docs/PLAYER_EXPERIENCE_BLUEPRINT.md` 與 `Tools/DotnetCheck/__pycache__/`；本次交接沒有修改它們。使用者口中的 main 不應直接當成已核對的分支名稱。
- 選定鏡頭後才建立整合分支，對照正式版最新輸入路由、HUD、移動、MatchGate 與場景變動，挑選必要改動、解衝突、重新驗證。舊版測試結果不能代替合併後驗收。
- 本次設計決議不自動改寫 v0.14 凍結驗收；需要變更的條件另依專案程序處理。
- 正式部署由一方負責。不要讓鏡頭試作與正式版同時覆寫 `vow3d` 的 gh-pages；本次預覽使用獨立儲存庫。

## 試玩送達與驗證證據

- 公開網址：https://9gf6p4448m-del.github.io/vow-camera-preview/?v=0b82bac 。可跨 Wi-Fi／行動網路存取；手機橫向，按 THIRD，左搖桿移動、右空白拖曳轉頭。
- 預覽儲存庫 `9gf6p4448m-del/vow-camera-preview`，只含生成的 WebGL 檔案。Pages main 提交 `0b40e22d2465ca10c525fc3ec9fc1f60a05bb05f`，發布時間 `2026-10-01 17:10:26 +08:00`。
- 實跑 `gh api repos/9gf6p4448m-del/vow-camera-preview/pages/builds/latest --jq '{status:.status,commit:.commit,updated_at:.updated_at,error:.error.message}'`：`status=built`，上述 commit，`updated_at=2026-10-01T09:10:26Z`，`error=null`。
- 已讀回頁面版本 `build 2026-10-01 08:55 UTC · 0b82bac`；Unity instance 存在，loader/framework/wasm/data HTTP 200。844×390、DPR 2 模擬手機已執行 THIRD 與雙指 move/look；真機尚未驗證。
- 公開頁面有 favicon 404、WebGL INVALID_ENUM 警告與合成觸控下 AudioContext 自動播放警告；不宣稱 console 全無警告。
- 若看到舊版，關閉分頁重開或強制重新整理，再核對版本列。預覽版 sw.js 會解除自己的 service worker。
- 原測試：DotnetCheck 333 過／1 既有略過／0 敗，ALL PASS；定向 Unity EditMode 23/23、PlayMode 15/15。完整指令、fixture 修正與歷史失敗見 `docs/CAMERA_LAB_TOUCH_RESULT.md`，不等於正式版全套驗收。
- 該舊紀錄的「未公開送達」描述的是當時階段；本文件補記後續獨立 Pages 送達。正式 gh-pages 現況應另核對，不能沿用舊 SHA。

## 請 Claude 的第一輪回報

回報目前分支／未提交內容、設計差異表、會受影響的模組、建議實作順序與待裁定項目。保留第三人稱未定案與真機未驗證的標記；先讓使用者看懂差異，再安排工程整合。
