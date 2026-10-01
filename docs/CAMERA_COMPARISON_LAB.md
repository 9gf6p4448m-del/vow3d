# 鏡頭比較試作

日期：2026-10-01。獨立工作樹 `vow-camera-lab`、分支 `camera-lab-20261001`，原始基線 `12fbaa2`。只在這條試作支線運作；正式主工作樹與線上版本不受此元件影響。

## 目標與邊界

同一灰盒場景、英雄、敵人、戰鬥與觸控規則，比較原本俯視與中距離第三人稱。沒有新武器、自由滑步、全螢幕拖曳或鏡頭手勢；點地移動、點敵普攻、符印與既有模式照舊。

`CameraComparisonLab` 在 `VOW_Phase1_Greybox` 載入後附掛 Bootstrap；Start 不改原 rig、FOV、英雄狀態。第三人稱停用原 FollowCameraRig，使用 pitch 25°、距離 8m、焦點高 1m、FOV 55°，yaw 初始 0°，左右各轉 30°。鏡頭與震屏仍用既有 rig 階層。SphereCast 半徑 0.25m，忽略觸發器與英雄自身 collider，遇牆縮短距離；不改牆、英雄或導航規則。切回／停用時恢復進入前 FOV、rig enabled 狀態與 transform，原 rig 若啟用則 Snap 到目前英雄位置（正式預設 52°／17m／FOV40）。

既有遊戲程式唯二窄幅變更為 `TouchGestureRouter` 在有效 UI tap 判定後保留 release 座標，以及 `PlayerInputService.UiTapScreenPosition` 將這兩個值公開。回呼時可讀到本次手指座標；無新增裝置讀取、無 EventSystem、無 GUI.Button、無改 MaxUiRegions（仍16）。既有HUD使用15區，lab只註冊最後1區，四個子按鈕與文字共用它。停用時取消訂閱、作廢持續觸控並停用區域；重啟重用原 ID，不佔新槽。WebGL模板另有主代理負責的試作標記。

## 操作

- `CAMERA LAB | TOP`：預設原俯視。點 `THIRD` 進第三人稱。
- 第三人稱點 `LEFT`／`RIGHT` 每次轉30°；`RESET` 回 yaw0°；`TOP DOWN` 回俯視。
- 操作沿用「點地移動、點敵普攻」。左右與重置在俯視時無作用。
- 橫向844×390使用單排4鈕；640×480使用2×2，640×360借用未顯示天賦盤的空位。位置避開原左HUD、天賦盤、右上CAPTURE、右下符印與正式局元素鈕。版面尺寸／對局狀態／天賦盤顯示變動時更新同一region並作廢舊觸控。元件Update順序-900早於輸入採樣；初始化延一frame讓原HUD先註冊區域。
- 可用空位不足時自動回俯視，顯示 `use landscape`，鏡頭按鈕暫不顯示，提示區仍消耗UI點擊。此試作優先橫向手機；原生高DPI與直式不是已驗收結論。

## 驗證狀態

新增 `CameraComparisonLabTests`：預設／往返復原、四按鈕真路由與世界事件零滲透、跟隨、防牆與英雄Collider忽略、disable/re-enable生命週期、雙指有效release座標與失效touch不更新、真GameView 640×480→844×390第三人稱→640×360首個Update操作切回→640×480 resize。

2026-10-01 實跑命令：`UNITY_REFS_DIR=C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/refs bash Tools/DotnetCheck/verify.sh`。
實際輸出：純邏輯326過／1既有略／0敗，Unity腳本編譯0錯，8項靜態掃描PASS，`RESULT: ALL PASS`。本指令不編譯／執行PlayMode測試。

同日最終runtime來源再跑相同verify命令：326過／1既有略／0敗、Unity腳本0錯、8項掃描PASS、`RESULT: ALL PASS`（38秒）。

Unity首輪因測試Object型別歧義在編譯階段中止，已明確指定UnityEngine.Object。`camera-lab-play-r2.xml` 原始結果保留為6項／5過／1敗／0略；紅項為牆Destroy後第一個yield仍讀到上一幀LateUpdate的距離4.25m，原期待8m。測試已改成先確認Collider銷毀，再固定跨過移除後一次LateUpdate；距離斷言仍為原8m／0.01m容差，沒有重試或放寬。修後Unity結果待主代理接跑。

修後 `camera-lab-play-r3.xml`：6項／6過／0敗／0略，Unity exit0。既有輸入 EditMode `camera-lab-input-edit.xml`：16項／16過／0敗／0略，Unity exit0。獨立程式覆審 APPROVE，無未解 finding。

WebGL已於2026-10-01部署至獨立 `/camera-lab/`；建置來源fe78256、Pages提交971386c，完整紀錄見 `CAMERA_LAB_DELIVERY.md`。桌面瀏覽器已查看俯視／第三人稱、左右旋轉／重置／切回，以及844×390與640×360 CSS視窗。640窄畫面部分按鈕文字擁擠；除錯面板會遮住角色部分身體，尚非正式HUD。原生手機的辨識度、觸控尺寸、遮擋、地熱飛行與震屏後鏡頭距牆、兩視角下相同操作的舒適度仍需實機試玩。
