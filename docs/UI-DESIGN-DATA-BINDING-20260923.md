# UI 设计稿示例数据：初值落地与数据源覆盖（2026-09-23）

> 设计稿：`hundun-yy-ui/pages/*.html`（56 页）
> 实现侧：`HundunWorld/Source/Game/UI/Ink/Pages/**`
> 审计脚本：`.compile-temp/data_gap_precise.py`（可复跑）

## 0. 结论先行：示例数据已经落成组件初值

上一轮报告曾把「示例数据未替换为设计稿初值」列为视觉差异主因，这一判断**不成立**。
先前的文案比对脚本用「字面串精确匹配」，而实现侧大量使用插值与合并文本
（如 `"铜钱 12,450"` 对设计稿的 `12,450`），于是把已落地的数据误报成了缺口。

本轮的严格审计只考察**多位数值**（金额／属性／人数／进度／倒计时等），
判定链路为「设计稿 token 精确匹配 → 去掉非数字后匹配 → 都不成立才算缺口」：

| 设计页 | 真缺口 | 判定 |
| --- | --- | --- |
| `menu-char-attributes` | 12,450 / 328 / 156 / 168 / 642 | **排除区**（角色属性，已微调，未改动） |
| `menu-char-attributes-v2` | 52,840 / 12,450 / 28.5 | **排除区**（角色属性 v2，未改动） |
| `menu-equipment` | 12,450 | **排除区**（背包／装备，未改动） |
| `menu-shop` | 8722 | 伪缺口：设计稿 `&#8722;`（减号 HTML 实体） |
| `combat-hud` | 720 | 设计稿为固定示例值，实现改为绑定角色属性组件显示「当前/上限」，保留实现 |

其余 51 页的多位示例数据**均已是组件初值**。逐页抽查证据（读代码确认，非脚本推断）：

- `menu-battle-pass`：Lv.42、铜钱 12,450、元宝 328、28天 06:30:00、1,284,567、Lv.23、
  EXP 1,950/3,000、65%、再获得 1,050 EXP、680 元宝、90 元宝/级、至尊与免费双轨 Lv.20—Lv.29 奖励、
  已领取 6 / 可领取 1 / 未解锁 13、里程碑 Lv.10·20·30·40·50。
- `menu-gacha`：已祈愿 47、小保底 47/90、大保底 0/90、概率 1.20%/8.80%/30.00%/60.00%、
  单抽 160、十连 1600、卡池剩余 12 天 07 时 23 分。
- `menu-livelihood`：采药 Lv.8 2400/3200 … 缝纫 Lv.2 250/1000、配方「已掌握/未解锁 + 产出数量」。
- `shop-rare-items`：Lv.42、12,450、328、02:45:30、共 9 件珍品、6480 元宝。
- `menu-bestiary`：12,580 / 8,320 / 3,450、收集进度 56/120。
- `settings`：分类计数 10/8/12/6/5、分辨率档位、帧率 60 fps、视距 75。
- `photo-mode`：f/2.8 · 1/125s · ISO 400、FOV 50°、曝光 +0.5、焦距 35mm、相册 12。

## 1. 真正缺的是「接入真实数据源时覆盖」

设计初值大多以 `const` 或静态规格数组写死，**没有任何覆盖入口**：
数据源接入后只能改页面源码。全仓原本只有少数页面提供了绑定点：

- `CombatHudPage.BindCharacter(component)`（未绑定时回退 mock，是最完整的样板）
- `RewardLevelUpPage.SetLevelUp`、`PopupPages.SetItem/SetMessage`、`GuideHudPage.SetStep`、`LoadingPages.SetProgress`

## 2. 本轮落地：4 页新增数据绑定 API

约定（与 `CombatHudPage.BindCharacter` 一致）：

1. **字段初值 = 设计稿示例数据**；构建方法直接读字段，页面构造完成即与设计稿一致，无需数据源。
2. **`Bind*` 只写字段并调用私有 `RefreshXxx`**，不重建控件、不改布局。
3. **不调用 `Bind*` 时永远保留设计稿初值**，便于设计比对与无数据场景复现。

| 页面 | 新增 API | 覆盖的数据 |
| --- | --- | --- |
| `Ink/Pages/BattlePass/MenuBattlePassPage.cs` | `BindAccount`、`BindSeasonInfo`、`BindPassProgress`、`BindPassPrices` | 玩家等级与货币、剩余时间与参与人数、通行证等级与经验进度、奖励统计、解锁/升级价格 |
| `Ink/Pages/Gacha/MenuGachaPage.cs` | `BindAccount`、`BindPity`、`BindPoolCountdown`、`BindDrawCost` | 元宝与命星、已祈愿/大小保底/剩余保底、倒计时、单抽与十连价格 |
| `Ink/Pages/Bestiary/MenuBestiaryPage.cs` | `BindCollectionProgress`、`BindCurrency` | 56/120（总进度条 + 详情条 + 「已收录 N 株」文案）、宝玉/铜钱/战功 |
| `Ink/Pages/Combat/MenuBattlePrepPage.cs` | `BindCombatPower`、`BindEquipmentBonus`、`BindStatBars`、`BindSupplies` | 战力 12,450/15,000 与达成率、6 项装备属性加成、气血/内力/体力细条、药品数量 |

实现要点：

- 原 `const` 改为实例字段（不再被编译器内联），百分比/比例改为按字段实时计算，避免初值与字段脱钩。
- `MenuBattlePrepPage.BuildEquipStatRow` 现在返回数值 Label 并留存，使 `BindEquipmentBonus` 能就地刷新。
- 绑定方法均可传「不完整数据」：未传入的项保留原值（`BindEquipmentBonus`/`BindSupplies` 跳过空项）。

## 3. 两处设计稿自身的数据不一致（需产品/策划定夺）

1. **`menu-gacha`**：设计稿「小保底 47/90」与「距离保底还有 3 次」不自洽（90 − 47 = 43）。
   本轮按设计稿示例数据落地为 **3**，并保留 `BindPity(..., pityRemaining)` 显式覆盖位；
   真实数据接入后以数据源为准。
2. **`combat-hud`**：设计稿气血示例为固定值 720，实现改为绑定 `CharacterAttributesComponent`
   显示「当前/上限」。保留实现（更正确），未回退为设计稿固定值。

## 4. 下一批（仍缺覆盖入口的高数据量页）

`menu-livelihood`（技能等级/经验/配方产出）、`menu-sect`（贡献度/已学/任务进度）、
`menu-mail`（未读数/附件/日期）、`menu-team`（队伍人数/成员等级）、`menu-quests`、
`menu-shop`、`menu-time`、`settings`、`photo-mode`、`element-vision`、`multiplayer`。

轻量做法（供参考）：`MenuSectPage` 已有 `_dailyTaskProgress` 等可变字段，接 `Bind*` 成本最低；
`SettingsPage`/`PhotoModePage` 的初值是「控件当前值」（滑块/下拉），适合用
`BindSettings(...)` 一次性写回并触发自身变更回调。

## 5. 验证

```
powershell -ExecutionPolicy Bypass -File .compile-temp/verify-game-compile.ps1
--- 0 个编译错误 ---
warnings: 142      # 与改动前基线一致
```

复跑审计：

```
python .compile-temp/data_gap_precise.py     # 多位数值真缺口
python .compile-temp/data_init_scan.py <page>  # 单页「设计稿数值 vs 实现字面量」对照
```
