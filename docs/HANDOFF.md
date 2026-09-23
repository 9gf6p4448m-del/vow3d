# VOW 誓約 — Codex 接手紀錄（2026-09-23）

## 目前狀態

- 專案：Unity 2022.3.62f1、C#、URP。工程約束見 `ARCHITECTURE.md`，詳細驗收見 `docs/PHASE1_ACCEPTANCE_GUIDE.md`。
- Claude Code 交接基線為 `e6e9d30`；Codex 入口與本紀錄於 `d652ce4` 加入。開始工作時以實際 `git status` 與 `git log` 為準；`e6e9d30` 記錄 v0.6.1 驗收與線上試玩結果。
- v0.6.1 新增五種元素反應飄字、英雄受困 `ROOTED` 飄字與 HUD `ROOTED`／`SLOWED`。規格與凍結驗收見 `docs/V061_FEEDBACK_PLAN.md`；結果見驗收指南 §15。
- 驗收指南記錄：`verify.sh` 195 通過、1 略過；Unity EditMode 0 紅、PlayMode 99 全綠；突變 114／114 CAUGHT。這是 2026-09-21 的既有紀錄；2026-09-23 的重新驗證見下節。
- `origin/gh-pages` 為 `fcaf70c`（2026-09-23 20:27:04 +0800），部署 v0.6.2，建置來源 `cdb41d0`；線上首頁顯示 `v0.6.2 · build 2026-09-23 12:27 UTC · cdb41d0`。驗收指南 §16 記有線上互動與畫面結果。

## 未決事項與下一步

1. 原生 Android／iOS 的 120Hz、震動與延遲注入下真人手感仍未驗證；WebGL 試玩不能代替原生裝置驗收。
2. v0.6.2 的血條避讓已驗證「英雄靠近木樁並極速立牆」的線上畫面；其他視角或落點尚未全面驗證。
3. 下一批玩法方向已選定為「灰盒對手攻防」：點紅色對手開局、追擊＋單招、無元素、英雄 100 HP／對手 300 HP／敵擊 20／預警 0.7 秒、任一方倒地後停 2.5 秒，雙方重置再點開局。完整規格與分步驗收見 `docs/V070_GREYBOX_DUEL_PLAN.md`；其中對手移速、射程、攻擊圈半徑、恢復時間仍是待確認的工程提案值，未實作。

## 交接檢查

開始工作時重新執行 `git status --short --branch`、`git log -5 --oneline`、`git log origin/gh-pages -1`；以當下結果為準。若要宣告新版本完成，附改動檔案、實跑指令與輸出，以及部署後的送達證明。

## 2026-09-23 v0.6.2 送達

- 使用者選定本版包含血條避讓與 STEAM 對比；來源提交 `cdb41d0` 將 STEAM 改為深藍灰，並同步更新程式版號與 Unity `bundleVersion`。
- `bash Tools/DotnetCheck/verify.sh`：195 通過／1 略過、Unity 腳本編譯 0 error、8 條紅線掃描全過；Unity batchmode EditMode：192 通過／4 略過／0 失敗；PlayMode：100／100 通過。
- `bash Tools/deploy-webgl.sh`：重新建置 11 MB WebGL，推送 `origin/gh-pages fcaf70c`；首頁版本列讀回 `v0.6.2 · build 2026-09-23 12:27 UTC · cdb41d0`。
- Playwright 線上手機模擬（844×390、DPR 2、觸控）：載入成功、console 0 error；`WATER`→`FIRE` 後 0.3 秒，深藍灰 `STEAM` 在白霧上清楚可讀；英雄靠近木樁並按符印鈕後，牆與木樁血條分開。畫面在 `../vow-toolchain/browser-screenshots/v062-01-steam-0_3s.png`、`v062-03-wall-bars.png`。

## 2026-09-23 Codex 接手進度

- VOW 已登錄為 Codex 本機專案，根目錄為本 Git 倉庫；Codex 專案清單讀回 ID `01a0ce1e-2395-7073-9ebe-c4db69666b26`。
- 符印牆血條的高度由牆心上方 1.4m 調為 1.9m；場景內 6 面池牆與場景建置器同步。PlayMode 新增木樁前 4m 極速立牆的畫面投影測試，修正前 0/1、修正後 1/1；完整 PlayMode 100/100。
- `Tools/DotnetCheck/verify.sh`：純邏輯 195 通過／1 略過、Unity 腳本編譯 0 error、8 條紅線掃描全過。本機 WebGL 建置成功（10.5 MB），本機瀏覽器試玩可見兩條血條分開、console 無 error。這是發布前的本機結果；v0.6.2 送達證明見上節。
- 當時 `STEAM` 飄字對比未改、線上版仍是 `origin/gh-pages 849cc87` 的 v0.6.1；v0.6.2 已處理並送達（見上節）。原生 Android／iOS 手感仍待實機驗收。
