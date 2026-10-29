# TreasureKeeperGlobal｜國際服寶物庫整理助手

國際服 Dalamud 插件，保留繁中操作介面，物品名稱與解讀提示依遊戲語言顯示。

## 遊戲內安裝

1. 在遊戲輸入 `/xlsettings`，開啟「實驗性／Experimental」。
2. 在「自訂插件庫／Custom Plugin Repositories」貼上以下網址，新增、啟用並儲存：

```text
https://raw.githubusercontent.com/wenwen-ff14/TreasureKeeperGlobal/main/pluginmaster.json
```

3. 輸入 `/xlplugins`，重新整理後搜尋 **TreasureKeeperGlobal**，按「安裝」。
4. 安裝完成後輸入 `/tkeepglobal` 開啟整理助手，或 `/tmapglobal` 開啟寶圖工具。

插件安裝器會自動下載並安裝套件，後續更新也使用同一網址。玩家不需要下載原始碼或自行編譯。

## 功能

- 按寶物庫瀏覽掉落，支援分類、搜尋、批次勾選與手動新增。
- 全域自動丟棄已勾選物品；深層地圖固定保留。
- 自動補錄庫內新取得物品，每庫可設定新物品預設保留或丟棄。
- 寶圖快捷解讀與本次解讀自動確認。

自動丟棄需手動啟動，會移除一般背包中已勾選物品的整疊數量，包含原本持有的物品。輸入 `/tkeepglobal stop` 停止。

國際服版使用獨立設定，不匯入繁中版丟棄選擇。國際服版使用專屬指令，避免與其他插件衝突。

內建掉落清單不代表國際服完整掉落表，可透過自動補錄或手動新增補充。目前已通過離線驗證，尚待國際服遊戲內實測。

原作者：TreasureKeeper contributors。授權 AGPL-3.0-or-later；第三方授權見 third-party。
