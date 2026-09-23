# VOW 誓約 — Codex 接手紀錄（2026-09-23）

## 目前狀態

- 專案：Unity 2022.3.62f1、C#、URP。工程約束見 `ARCHITECTURE.md`，詳細驗收見 `docs/PHASE1_ACCEPTANCE_GUIDE.md`。
- Claude Code 交接基線為 `e6e9d30`；Codex 入口與本紀錄於 `d652ce4` 加入。開始工作時以實際 `git status` 與 `git log` 為準；`e6e9d30` 記錄 v0.6.1 驗收與線上試玩結果。
- v0.6.1 新增五種元素反應飄字、英雄受困 `ROOTED` 飄字與 HUD `ROOTED`／`SLOWED`。規格與凍結驗收見 `docs/V061_FEEDBACK_PLAN.md`；結果見驗收指南 §15。
- 驗收指南記錄：`verify.sh` 195 通過、1 略過；Unity EditMode 0 紅、PlayMode 99 全綠；突變 114／114 CAUGHT。這是 2026-09-21 的既有紀錄；2026-09-23 的重新驗證見下節。
- `origin/gh-pages` 為 `849cc87`（2026-09-21 02:25:26 +0800），部署 v0.6.1，建置來源 `2936174`。驗收指南 §15 記有線上 Playwright 手機模擬的畫面與 console 結果。

## 未決事項與下一步

1. `STEAM` 近白色飄字疊在白霧圈上對比偏低。先以實際畫面與玩家回饋確認可讀性，再決定是否改色；不要未經裁定擴大成整套視覺重做。
2. 原生 Android／iOS 的 120Hz、震動與延遲注入下真人手感仍未驗證；WebGL 試玩不能代替原生裝置驗收。
3. 符印牆血條蓋住木樁血條是 v0.6.1 既有問題；2026-09-23 已針對「英雄在木樁前 4m 極速立牆」做本機修正與回歸驗證，線上版仍待更新。其他視角或落點的血條避讓尚未全面驗證。
4. 下一批玩法尚無本紀錄可證實的凍結需求。先取得使用者方向，再依 `GDD.md` 與當批計畫推進。

## 交接檢查

開始工作時重新執行 `git status --short --branch`、`git log -5 --oneline`、`git log origin/gh-pages -1`；以當下結果為準。若要宣告新版本完成，附改動檔案、實跑指令與輸出，以及部署後的送達證明。

## 2026-09-23 Codex 接手進度

- VOW 已登錄為 Codex 本機專案，根目錄為本 Git 倉庫；Codex 專案清單讀回 ID `01a0ce1e-2395-7073-9ebe-c4db69666b26`。
- 符印牆血條的高度由牆心上方 1.4m 調為 1.9m；場景內 6 面池牆與場景建置器同步。PlayMode 新增木樁前 4m 極速立牆的畫面投影測試，修正前 0/1、修正後 1/1；完整 PlayMode 100/100。
- `Tools/DotnetCheck/verify.sh`：純邏輯 195 通過／1 略過、Unity 腳本編譯 0 error、8 條紅線掃描全過。本機 WebGL 建置成功（10.5 MB），本機瀏覽器試玩可見兩條血條分開、console 無 error。這些是本機結果，尚非 gh-pages 送達證明。
- `STEAM` 飄字對比、原生 Android／iOS 手感仍待使用者判斷或實機驗收；線上版仍是 `origin/gh-pages 849cc87` 的 v0.6.1。
