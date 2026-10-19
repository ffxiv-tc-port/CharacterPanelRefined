# Character Panel Refined

[Character Panel Refined](https://github.com/Kouzukii/ffxiv-characterstatus-refined) 的台服移植版。精簡角色面板顯示的資訊，並加入實用數值（暴擊、直擊機率提升，以及預估輸出），方便比較裝備。

## 功能

- 角色面板加入暴擊傷害提升、直擊傷害提升百分比
- 顯示套裝的平均傷害／平均治療預估值，方便比較不同裝備
- 顯示不含消耗品（藥劑／食物）的製作、採集屬性
- 顯示裝備等級資訊
- 顯示「裝備屬性合計」：目前裝備（含高品質加成與魔晶石）總共給了多少屬性，滑鼠移上去可看每一項屬性的明細（裝備本體／魔晶石分開列）；製作職／採集職也可顯示「裝備」區塊
- 物品提示中顯示等級同步後的屬性
- 可用更詳細的提示取代角色面板內建的提示
- 可設定跟隨遊戲語言自動切換插件語言

## 指令

- `/cprconfig`：開啟／關閉設定視窗

## 台服版的修改

- 新增「裝備屬性合計」與製作職／採集職的「裝備」區塊。
- 修正開啟角色面板時可能讓遊戲崩潰的提示文字查表方式，以及台服的提示框特徵碼。
- 介面繁體中文化（用台服官方用語，例如「暴擊」）。

## 安裝

在 Dalamud 設定的「自訂插件庫」加入
`https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json`
並啟用，再從插件列表安裝。

原作者：[Kouzukii](https://github.com/Kouzukii/ffxiv-characterstatus-refined)
