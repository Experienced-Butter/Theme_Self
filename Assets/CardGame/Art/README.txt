卡牌战斗 · 美术资源放置说明

目录结构（每关一套，互不影响）：
  Level1\     关一
  Level2\     关二
  SideQuest\  支线关

每关下面三个子目录：
  Background\  背景图   建议 1920×1080（16:9），会铺满全屏
  Player\      玩家形象  建议 512×512 正方形
  Monsters\    怪物形象  建议 512×512 正方形

放好图以后要做的事：
  1. 选中 Assets\CardGame\Resources\CardGameThemes 下对应关卡的 Theme_*.asset
  2. 把图片从 Project 窗口直接拖进 Inspector 里对应的槽位
  3. 保存（Ctrl+S）→ 提交时务必连 .meta 文件一起提交

重要：
  · 图片的 Import 设置请用 Sprite (2D and UI)。Texture Type 不对就不能拖进 Sprite 槽位。
  · 不要移动／重命名已经提交过的资源，那会改变 GUID，导致别人那边的引用全部丢失。
  · 槽位留空是允许的：留空会自动用程序生成的占位外观（纯色背景 + 圆形头像）。
