# DeskBox 四项功能改造方案

## Context（为什么做）

用户当前在用的 DeskBox（WinUI 3 / WinAppSDK 桌面整理软件）需要四个改动：

1. **组合标题显示**：多个格子组合后，当前只能在有限空间里显示一部分标题、靠滚动选择。要改成「一排显示全部成员标题、宽度按可用空间与数量自适应、超长截断、同一组合内所有标题等宽」。用户已确认**两种导航样式（Tabs / Stack）都统一为新效果**。
2. **按使用次数排序**：格子内项目可按被打开的次数排序（当前只有 名称/大小/类型/修改时间/手动）。
3. **去掉升级检测与提示**：用户已确认**彻底移除**更新检查服务、后台检查、托盘/弹窗提示、设置页更新项。
4. **搜索入口**：现有搜索弹窗只能靠全局热键触发。需要在**主窗口标题栏加搜索按钮** + **托盘右键菜单加「搜索」项**，复用现有 `App.OpenSearchPopup()`。

目标：四项改动各自独立、可编译、可测试通过，且不破坏现有契约测试基线。

## 关键既有事实（复用点）

- 组合标题控件：`src/DeskBox/Controls/WidgetGroupTitleSwitcher.xaml(.cs)`、`.Tabs.cs`、`.Interaction.cs`；宿主 `src/DeskBox/Controls/WidgetShell.xaml`（L128-138）+ `WidgetShell.xaml.cs` `SetGroupPresentation`(L907-932)、`ApplyChromeMode`(L4532-4592)。
  - `UsesTabs`（`.Tabs.cs` L39-40）同时是**交互能力开关**（tab 拖拽/悬停/拖放冻结），被 `.Tabs.cs`、`.xaml.cs`、`.Interaction.cs` 多处引用。
  - 原生 `TabView`（x:Name=`TabsView`）当前 `TabWidthMode=SizeToContent`，资源键 `TabViewItemMinWidth=32` / `TabViewItemMaxWidth=180`（`.xaml` L281-283）。
- 排序：`src/DeskBox/Models/WidgetConfig.cs`（`WidgetSortMode` 枚举 L217-233、`FileAddedAtByPath` L169-174）、`src/DeskBox/ViewModels/WidgetViewModel.SortingAndWatchers.cs`（`SetSortMode` L15-55、`CompareItems` L1007-1037、`SortModeLabel` L254）、`src/DeskBox/ViewModels/WidgetViewModel.AddedAt.cs`（逐路径字典 + 批处理持久化范式）、`src/DeskBox/ViewModels/WidgetViewModel.Operations.cs` `OpenItemAsync`(L453-468)、菜单 `src/DeskBox/Controls/WidgetContents/FileSurfaceContent.SelectionAndMenus.cs`（`AddSortItem` L900-920、排序菜单 L808-827）。
- 更新功能：`Services/AppUpdateService.cs`、`Services/IAppUpdateService.cs`、`Services/AppUpdateServiceFactory.cs`、`Services/StoreAppUpdateService.cs`、`Services/ServiceRegistry.cs`、`Models/AppUpdateManifest.cs`（保留，ReleaseNotes 仍用）、`App.xaml.cs`、`App.Tray.cs`、`ViewModels/SettingsViewModel.AboutAndUpdates.cs`、`Views/SettingsWindow.StorageAndUpdates.cs`、`Views/SettingsWindow.xaml`、`Models/CoreSettingsSlice.cs`、`Contracts/IInteractionSettings.cs`、`Contracts/IMaintenanceSettings.cs`、`Services/InteractionSettingsCoordinator.cs`、`Services/MaintenanceSettingsCoordinator.cs`、`Services/SettingsSearchCatalog.cs`。
- 搜索：`App.OpenSearchPopup()` / `OpenSearchPopupWithQuery()`（`App.xaml.cs` L5785-5856，已含 UI 线程 marshal 与单例复用）、`Views/DesktopOrganizationWindow.xaml(.cs)`、`App.Tray.cs`（`CreateTrayIcon` L26-135，`RunTrayMenuActionAsync`/`RunTraySettingsActionAsync` 助手）。
- 本地化：`src/DeskBox/Strings/*.json` **12 个语言文件键集必须完全一致**（`tests/DeskBox.Tests/LocalizationResourceContractTests.cs` L25-62）；新增键需 12 个文件同补；删除键同样需 12 个文件同删。

---

## 功能 1：组合标题全部显示、等宽自适应

### 1a. 等宽铺满（`.Tabs.cs`）
- 新增 `UpdateUniformTabWidth()`：可用宽 `usable = TabsView.ActualWidth - TabsView.Margin.Left - TabsView.Margin.Right - TabStripChromeReserve(≈16)`；`uniform = Clamp(usable / N, MinUniform≈32, MaxUniform=180)`。
- 对每个 `GroupTab.Tab` 设**本地** `MinWidth = MaxWidth = uniform`（本地值优先于样式 setter），使总宽 ≤ 可用宽 → 原生 TabView 不出现溢出滚动按钮。保持 `TabWidthMode=SizeToContent`。
- 同时把 `row.Title.MaxWidth = uniform - 图标宽 - 间距`，让标题在等宽内以 `CharacterEllipsis` 截断（`TextTrimming` 已具备）。
- 触发点：`TabsView.SizeChanged`、`SynchronizeTabs()`（L107-194，成员数变化后）、`ApplyTitleBarMetrics`、`ApplyNavigationStyle`。首帧 `ActualWidth<=0` 直接 return，靠 `SizeChanged` 补算。
- 回退策略：若仍溢出，从 `TabsView` 视觉树取内部 `ScrollViewer`，用 `ViewportWidth / N` 重算；再不行按 `ScrollableWidth` 差值收缩。

### 1b. 统一两种样式（`.xaml.cs`）
- `ApplyNavigationStyle`(L442-473)：不再按样式分支——分组存在即显示 `TabsView`；`SelectorButton`/`CapsuleSurface`/`TitleInteractionChrome` 恒 `Collapsed`。
- `UsesTabs` 改为恒 `true`（保留属性名，避免改动十余处调用点语义），使 tab 拖拽/复排/悬停/拖放冻结在两种样式下都可用。
- `WidgetShell.ApplyChromeMode`(L4538)：`usesGroupTabs` 条件放宽为「存在分组且非 overlay 且非改标题」，否则 Stack 版式仍走 L4559-4579 的分隔线/padding，出现错位。
- 保留但不再展示：胶囊身份动画、滚轮方向反馈仍是成员切换（滚轮/Ctrl+Tab/Alt+Down）的有效交互，仅「单标题胶囊」不再显示。

### 1c. 风险
- 宽度启发式在极多成员时会缩到很小（MinUniform 兜底，必要时再按可用宽二次收缩）。
- 长按分离拖拽仅在 Stack 的 `SelectorButton` 路径；统一后分离手势改为 tab 拖拽（`.Tabs.cs` L423-458 原生支持），属手势变化，需在验证时确认。
- a11y：`UpdateAccessibility`(L1014-1036) 对隐藏的 SelectorButton 失效，需把名称继续设到控件本体，并保留 tab 上的 `AutomationProperties`（`SynchronizeTabs` L179-182）。

---

## 功能 2：格子内按使用次数排序

- `Models/WidgetConfig.cs`：`WidgetSortMode` **末尾追加** `Frequency`（枚举按整数持久化，不可插队）；`WidgetConfig` 增 `Dictionary<string,int> UsageCountByPath = []`（仿 `FileAddedAtByPath`）。
- `WidgetViewModel` 新增分文件/追加方法，复刻 `AddedAt.cs` 范式：`EnsureUsageDictionaryComparer`（OrdinalIgnoreCase 归一）、`ApplyPersistedUsageCounts`（失效路径清理）、`RecordItemUsage(path)`、`TransferItemUsage`、`RemoveItemUsage`、`PersistUsageTrackingIfNeeded`（复用 `_itemMutationBatchDepth` 批处理，最终 `UpdateWidget(Config, notifySubscribers:false)`）。
- 计数接缝：`WidgetViewModel.Operations.cs` `OpenItemAsync`(L453-468) 中 `result == FileService.OpenItemResult.OpenedOrHandled` 时 `RecordItemUsage(item.Path)`；若当前 `SortMode==Frequency` 则重排并 `OnPropertyChanged(nameof(SortModeLabel))`。
- `CompareItems` L1014-1024 增加 `Frequency => Usage(left).CompareTo(Usage(right))`（缺失记 0），名称/路径兜底已存在。
- `SortModeLabel` L254 增加 case。
- 菜单：`FileSurfaceContent.SelectionAndMenus.cs` `AddSortItem`(L808-827) 在 Name 后追加 `AddSortItem(viewAndSort, "Widget.Sort.Frequency", WidgetSortMode.Frequency)`。
- 本地化：`Widget.Sort.Frequency` 补入 **12 个** `Strings/*.json`（键风格对齐现有 `Widget.Sort.*`）。
- 测试：补 WidgetViewModel 计数与排序单测（缺失→0、降序、名称兜底）。

---

## 功能 3：彻底移除升级检测与提示

删除顺序（每步保持可编译）：
1. **入口/宿主**：`App.xaml.cs` 删 `ScheduleBackgroundUpdateCheck`(L2128-2163) 及其调度(L1501-1505)、更新通知 UI(L2916-3009)、字段 `_hasUpdateAvailable`(L175)/`_availableUpdateVersion`(L178)、`AppUpdateService` 属性与 DI/订阅(L453-458)；`App.Tray.cs` 删 `updateItem`(L85-95)、菜单插入(L121-122)、`_trayUpdateItem`(L130)、刷新逻辑(L994-1029)，`App.xaml.cs` L121 字段同删。
2. **设置页 UI**：`ViewModels/SettingsViewModel.AboutAndUpdates.cs`、`Views/SettingsWindow.StorageAndUpdates.cs`、`SettingsWindow.xaml`(更新卡片 L3314-3322 及 One-Click 更新按钮)、`SettingsWindow.SectionElements.cs` 相关引用——移除更新检查/下载/安装卡片。**保留「关于」里的版本号显示**（`AppVersion`、渠道、反馈等无更新依赖）。
3. **服务层**：删除 `AppUpdateService.cs`、`IAppUpdateService.cs`、`AppUpdateServiceFactory.cs`、`StoreAppUpdateService.cs`，以及 `ServiceRegistry.cs` 中注册；`SettingsViewModel.cs` 注入点。**保留** `Models/AppUpdateManifest.cs`（`ReleaseNotesWindow`/`ReleaseNotesService` 仍引用）。
4. **死设置键（彻底清理）**：`Models/CoreSettingsSlice.cs` L55-58（`AutoCheckForUpdates`/`LastUpdateCheckAt`）、`Models/AppSettings.cs` facade L145-149、`Contracts/IInteractionSettings.cs` L42/L110、`Services/InteractionSettingsCoordinator.cs`、`Features/Interaction/InteractionSettingsViewModel.cs`、`Contracts/IMaintenanceSettings.cs` L16、`Services/MaintenanceSettingsCoordinator.cs`、`Features/Maintenance/MaintenanceSettingsViewModel.cs`、`ViewModels/SettingsViewModel.cs`(L153/L227)、`SettingsViewModel.AotBindableProperties.cs`(L6)、`SettingsViewModel.PreferenceCallbacks.cs`(L95)、`SettingsViewModel.SettingsSync.cs`(L73)、`Services/SettingsSearchCatalog.cs`、`Services/SettingsMigrationService.cs` 相关项。
5. **本地化（可选但推荐）**：从 12 个 `Strings/*.json` 删除更新相关键（`Tray.UpdateAvailable`、设置页更新文案等），保持 12 文件键集一致。
6. **测试同步**：删除 `tests/DeskBox.Tests/AppUpdateServiceTests.cs`；更新 `JsonSerializationBaselineContractTests.cs`(L32/L100/L168-169 期望)、`SettingsSliceContractBaselineTests.cs`(L38)、`ModuleBoundaryContractTests.cs`(预算条目，只允许收缩)、`SettingsServiceTests.cs`(L1416-1417)、`InteractionSettingsCoordinatorTests.cs`、`MaintenanceSettingsCoordinatorTests.cs`、`FeatureSettingsBoundaryContractTests.cs`(L86/L125-126 正则，删除后不匹配即通过)。
- `installer/*.iss`、publish 脚本：**不动**（安装包与更新清单产出属另一渠道，超出本次「应用内检测/提示」范围）。

---

## 功能 4：搜索按钮（主窗口标题栏 + 托盘）

- **主窗口**：`Views/DesktopOrganizationWindow.xaml` 的 `AppTitleBar`(L18-32) 左侧标题后加一个按钮（复用 `WidgetTitleActionButtonStyle` 或 `SubtleButtonStyle`，`FontIcon \uE721`），天然避开右侧 140 的标题按钮区；`.xaml.cs` 构造函数(L36-73) `click += (_,_) => App.Current.OpenSearchPopup();`（该文件已在用 `App.Current`，无需注入）。
- **托盘**：`App.Tray.cs` 在 `openManagedStorageItem`(L76-85) 之后、`settingsItem` 之前插入 `searchItem`（`T("Tray.Search")`），用 `AttachTrayMenuItemInteraction(contextMenu, () => RunTraySettingsActionAsync(contextMenu, OpenSearchFromTray))`；新增 `OpenSearchFromTray`（仿 `OpenSettingsFromTray`），并纳入托盘菜单本地化刷新逻辑。
- **本地化**：新增 `Tray.Search`、`DesktopOrganization.Search`，补入 12 个 `Strings/*.json`。

---

## 推荐实施顺序

1. 功能 1a（等宽）→ 1b（统一表现）——风险最高，先做且单独验证。
2. 功能 3（删更新）——波及测试基线，需一次性改到位。
3. 功能 2（使用次数排序）。
4. 功能 4（搜索入口）。

每项独立构建/测试，全部完成后再整体回归。因本地化键集一致性要求，凡涉及新增/删除键的改动，一次补齐 12 个语言文件。

## 验证

按仓库 `AGENTS.md`：
1. 先停掉仓库内运行的 `DeskBox.exe`（含 `C:\Program Files\DeskBox` 安装版单实例锁）。
2. 构建 Debug：`dotnet build .\src\DeskBox\DeskBox.csproj`（以 `DeskBox.dll` 写入时间晚于构建开始为准）。
3. 测试：`dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64`，仅当 **passed == total** 才算绿。
4. 从 `src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.exe` 启动，报告 exe 路径。
5. 人工视觉回归（WinUI 桌面应用，无法用浏览器工具）：组合成员数为 1/3/6/10 时标题是否一排铺满、等宽、截断、无滚动按钮；Stack 与 Tabs 两种设置下表现一致；tab 拖拽复排、Ctrl+Tab、拖文件悬停切换、右键「移动/移除/解散」仍可用；格子右键菜单出现「使用次数」排序并能生效；设置页与托盘不再出现任何更新检查/提示；标题栏搜索按钮与托盘「搜索」均能打开搜索弹窗。

## 最高风险点

- **功能 1b** 与 `WidgetShell.ApplyChromeMode` 的版式联动（分隔线/padding/边距），以及宽度启发式——建议先提「1a 等宽」，确认无滚动按钮后再提「1b 统一表现」。
- **功能 3** 的契约测试基线（JSON/切片/模块预算），删文件必须同步改期望，否则测试红；且 `ModuleBoundaryContractTests` 预算只允许收缩。
- **功能 2** 持久化频率：必须走批处理，否则每次打开都触发一次保存，放大 IO。
