# Word List UI mockup handoff

**当前状态：** 2026-11-01。Scene-authored layout 迁移已保存并通过关闭/重载核验，Missing Script 为 0。独立对比确认原 Library 的 939 个对象 ID 与布局/样式 baseline 保持，迁移新增的 61 个 Image 均为 Sliced，`Action_Delete` binding 已持久化。此前 39 项 EditMode 测试全部通过，Unity compile 为 0 errors / 0 warnings。Play smoke 已验证四页导航、筛选、下载并选中、创建/编辑/删除及成就筛选；另两次 Play 验证 metadata-only Copy & Edit。最新 `MockupWordListStoreTests` 35/35、compile 0 errors / 0 warnings 及实际 Play 验证：row→Unpublish→metadata 编辑→Publish 后，原 local ID 与成绩保留，条目回到 My Lists 且选中，remote ID 从 `mock-published-punctuation` 变为 `mock-published-01`。旧 Unpublish→Republish 同 ID 的测试属于已取代规则。此结果只验证 mock owned-list 生命周期，不表示删除或发布了真实 Community/Steam 条目。发布期间字段锁定、Copy & Edit 及成绩归档规则保持；旧下载 ID 的来源检测下架标记尚未接通或验证，除该标记外下载侧 `UpdateAvailable`/缓存语义不变。Edit Details 与 Update Publication UI 状态及处理方法已移除。操作后退出 Play，场景保持 clean，Console 无错误。当前仍是使用 fixture/store 的 UI mock，没有连接真实后端或 Steam；Words 模型也不是最终生产 schema 或存档迁移方案。
**当前规则的 Mock 限制：** `MockupWordListStore` 作者 Publish 只给 owned list 记录 `mock-published-*` 绑定，不创建 `MockupDiscoveryItem`；Community 的 `mock-ugc-*` 样例相互独立、尚未联通。因此上述测试只覆盖 owned-list 状态转换，不会删除或发布真实 Steam 条目。旧下载 ID 的来源检测和下架标记仍待接通真实来源后验证；目前没有验证该联动。

**Scene 测试安全：** 如果目标 Scene 在任何测试或 Play 开始前已 dirty，先停止；保全可恢复副本并核对保存状态/基线后再安排测试。结束时 Scene 显示 clean 本身不能证明原布局得到保留，必须与测试前副本或明确基线核对后再报告。

本文与 [`community-v1-discussion-summary.md`](community-v1-discussion-summary.md) 配合使用；分组和阶段决策以该汇总文档为准。

## 目的与边界

先在独立场景里组合现有 UI 与 Word List、Community、Achievements 流程，完成导航、文案、状态和交互的用户验收；确认 UX 后，再定义正式数据结构、存档兼容与成绩内容绑定，最后迁移正式场景并接入 Steam Workshop。mockup 仅用于设计验证，不证明游戏已上线社区、审核或新成就功能。

旧 Editor builder 曾从 `Assets/Scenes/mainGame.unity` 派生预览 UI；该入口现已安全退役，调用会抛出说明，不再创建或覆盖场景。新的显式迁移入口是 [`WordListHubSceneMigrationTool.cs`](../Assets/Editor/Mockups/WordListHubSceneMigrationTool.cs)，菜单为 `Tools/Mockups/Migrate Word List Hub Scene Layout`，静态入口为 `Words.UiMockups.Editor.WordListHubSceneMigrationTool.MigratePreviewScene`。迁移目标始终是现有 `Assets/Scenes/UI-Mockups/WordListHubMockup.unity`，不会读取或改写 `mainGame`。

controller 使用内存 store 和 fixture，不访问正式 gameplay、Steam、玩家偏好或持久存档。关闭并重开场景后，mock 状态不应被当作保存数据。正式词表 schema、旧存档兼容迁移、成绩与内容版本的绑定规则仍待后续阶段定义。当前 `MockupWordList.Words` 等字段只是为原型交互准备，不承诺正式 JSON 或 C# 数据形状。

## 相关文件与备份

| 路径 | 用途 |
|---|---|
| `Assets/Editor/Mockups/WordListHubMockupBuilder.cs` | 已退役的兼容菜单；调用时安全抛错，不生成或覆盖 Scene |
| `Assets/Editor/Mockups/WordListHubSceneMigrationTool.cs` | 显式、幂等的场景迁移；菜单项 `Tools/Mockups/Migrate Word List Hub Scene Layout` |
| `Assets/Editor/Mockups/WordListHubLayoutPersistence.cs` | 为保留旧 GUID 而留下的 deprecated 空类；不含保存钩子 |
| `Assets/Scenes/UI-Mockups/WordListHubMockup.unity` | 当前独立 mockup scene |
| `Assets/UI-Mockups/WordListHub/Runtime/WordListHubMockupController.cs` | 导航、页面、动态 UI、表单校验与 mock 交互 |
| `Assets/UI-Mockups/WordListHub/Runtime/WordListHubSceneBindings.cs` | Scene-authored 页面、控件和动态模板引用；与 controller 位于同一 GameObject |
| `Assets/UI-Mockups/WordListHub/Runtime/MockupWordListStore.cs` | 内存 fixture、分组、Play/Download、编辑、发布、点赞和举报状态 |
| `Assets/UI-Mockups/WordListHub/Runtime/MockupWordListModel.cs` | 原型专用数据模型；不代表生产 schema |
| `Assets/UI-Mockups/WordListHub/Tests/MockupWordListStoreTests.cs` | EditMode 测试源码；37 passed / 0 failed |
| `Assets/UI-Mockups/WordListHub/Art/Placeholders/` | 六张成就锁定/解锁占位图 |
| `Assets/UI-Mockups/WordListHub/Fonts/` | mockup 字体资源 |
| `D:/Files/Desktop/Unity/Projects/GDS4 Game-a-Week/02-words/_scene_backups/WordListHubMockup-20261031-171903` | 本轮预览场景的稳定备份路径；文件当前存在 |

`_scene_backups` 下列路径是迁移前的历史备份。旧 builder 的 Temp 重建流程已退役；新的迁移工具执行时会在项目 `Library/WordListHubSceneMigrationBackups` 下保存目标 Scene 与 `.meta`。

## Scene-authored UI 与迁移约定

- 固定页面、导航、footer、表单和弹窗是 Scene 内容。`WordListHubSceneBindings` 与 `WordListHubMockupController` 必须挂在同一个 GameObject；controller 读取显式引用，切换页面时对绑定的页面根执行 `SetActive`。
- Library 是现有排版的权威来源。迁移只在其外侧追加 Community、Achievements、Challenges、Modal 和隐藏的 `SceneTemplates`；不重建 Library。动态 Library/Community 行和 Achievement 卡片从 inactive Scene 模板克隆，`WordListHubRuntimeInstance` 只标记运行时克隆。
- **以后修改固定 UI：** 在 `Assets/Scenes/UI-Mockups/WordListHubMockup.unity` 直接编辑页面、导航、footer、表单、弹窗和按钮。固定 UI 由 controller 通过 bindings 操作，不会在运行时销毁重建。
- **以后修改动态外观：** 在 `HubGenerated/SceneTemplates` 编辑 inactive 的 Library 行、Community 行和 Achievement 卡片模板；修改会影响之后生成的克隆。滚动内容与行/卡片排布由 Scene 中对应的 `LayoutGroup`、RectTransform 和 LayoutElement 控制，也在 Scene Inspector 中编辑。
- **以后修改下拉菜单外观：** 在对应 Scene `TMP_Dropdown` 的 `Template` 子树中调整 `Item`、Toggle 和勾选标记；选项文字与筛选行为由绑定和 controller 管理。
- 迁移入口只针对现有目标 Scene，目标缺失或 dirty 时中止；执行前把 Scene 与 `.meta` 备份到项目 `Library/WordListHubSceneMigrationBackups`。它会比较 Library 迁移前后的层级、组件顺序、RectTransform、Graphic/TMP 和 Layout 属性，只有明确新增的 `Action_Delete` 分支可从父树对比中排除，并会单独校验该分支。任何其它 Library 改动都会拒绝保存。绑定控件上的持久 UnityEvent 只移除 target 为该 controller 的回调；其它对象的回调保留。
- 迁移已保存并通过关闭/重载核验。它只向既有 Scene 追加缺失页面、模板和弹窗并绑定引用，不重建 Library；目标 Scene dirty 时拒绝运行。迁移失败且磁盘 Scene/.meta hash 未变时，工具从磁盘恢复目标 Scene；磁盘状态改变时不会自动覆盖文件，并会报告失败。旧 `WordListHubMockupBuilder` 菜单只抛出退役说明，不能重建场景。
- `authoredLayoutOverrides` 与旧保存钩子已移除；`WordListHubLayoutPersistence.cs` 仅为空的 deprecated 类以保留 GUID。Play Mode 不再销毁并重建固定页面；运行时数据和动态行/卡片仍是临时 mock 状态。
- Unity 的 `Scene(s) Have Been Modified` 等模态窗口会阻塞主线程，Pipeline 的编译、测试和 `eval` 请求会表现为长时间 running/timeout，但 `unity status` 仍可能显示 Editor ready。此时必须由唯一 Unity 操作者确认实际 Editor 状态；不能把 Pipeline 的 running 状态当作测试正在正常执行。

## 上一轮源码与运行证据（历史）

以下是上一轮的源码与运行观察，仅作背景，不表示本轮实现状态或通过结果：

- 顶部导航为 **Challenges、Word List、Community、Achievements**；Achievements 是独立页面，带锁定/解锁占位图和状态筛选。Builder 将六张图绑定到 controller 字段。
- Word List 的语言、已玩状态、排序和排序方向使用 `TMP_Dropdown`。每个下拉模板的 `Item` 上只有一个 `Toggle`；勾选标记是子级 `Image`。本轮 Dropdown Item 行高为 20 px，与正式原版一致，已完成运行验收。
- **上一轮已被本轮决策取代：** 社区下载原版曾加入 My Lists，且没有独立 Community Downloads 分组。本轮目标改为 Community Downloads 独立可折叠；My Lists 只放本地原创、导入项和社区编辑副本。
- 新建和编辑表单有多行 Words 输入，按行拆分、Trim 并跳过空行，至少需要一个词。逗号不作为分隔符，以保留词语自身标点。Store 内的 `Words`/`WordCount` 用于 mock 行为和 UX 试验，尚未确定生产存档结构。
- 上一轮 Community 页面有 Like、Download、Play、Report & hide；当时 Play 自动下载会将社区项加入 My Lists。本轮需改为加入 Community Downloads，下载后展开、选中并滚动到该项。旧行为不是本轮实现证据。
- 举报后立即对当前 mock 用户隐藏发现项；本地 store 有复核状态转换 API，但 controller 中没有可操作的开发者审核队列。没有接真实 Steam 举报、AI 审核或全局下架服务。
- 历史 mock 曾限制 Copy & Edit 必须改变 Words/Language、只改标题不能创建副本；该门控已被 2026-11-01 作者规则取代。当前可原样 Copy & Edit 保存，或只改 metadata 后保存；不要求改变 Words/Language。新副本无 remote identity，原 source/records 不变。39 项 EditMode、干净编译和两次 Play 已验收。
- `ApplySlicedImages` 会遍历 controller UI 子树（包括 inactive 后代），给无 Sprite 的 Image 配置回退图，并设为 `Image.Type.Sliced`。上一轮 31 项 EditMode 测试包含保存场景 Image 检查；本轮另有 33/33 EditMode 通过，但这不代表本轮每项 UI 交互均已验收。
- 截图发现后，上一轮源码将 `SampleDataWatermark` 横向铺到 generatedRoot 底部，避开顶部导航；详情操作按钮也已重排到 detail pane 可见区域内。上一轮唯一截图确认 Library 与 footer 完整可见，footer mesh 有 33 个字符、64 个 vertices。该截图不代表本轮 revision。

### 两项 UI 根因记录

- **Mask viewport alpha 从 0 调为 1：** `Mask` 所在 viewport 的 Image 是 stencil writer。alpha 为 0 会让遮罩写入路径失效；现在 Image 保持不透明白色以写 stencil，`Mask.showMaskGraphic = false` 负责让 viewport 图像本身不可见。见 controller 的 `CreateScrollContent` 与 dropdown viewport 创建代码。
- **Dropdown template 每项只保留一个 Toggle：** 选项根 `Item` 上创建唯一 Toggle，checkmark 只是 Image 子对象。选择状态交由这个 Toggle 控制，不在 checkmark 或文本对象上重复添加 Toggle。

### 上一轮 pipeline 与运行验证记录（历史）

后台 pipeline 曾与另一项目占用地址冲突：另一项目绑定 `127.0.0.1`，导致本项目请求冲突。通过每次请求使用 fresh token 并将本项目绑定地址改为 `127.0.0.2:7800` 后恢复；没有升级依赖或修改项目 settings。上一轮 pipeline 编译 `failed=false`、0 errors，Play Mode 已停止，Unity compiling=false；这些状态不能代替本轮验收。

上一轮最终截图：`Temp/pipeline-screenshots/WordListHub-Final-Library-footer-eae03fbfac9548a696c02b886a74732f.png`。截图对应 Library 页，Word List 与 footer 完整可见；footer mesh 生成 33 个字符、64 个 vertices。场景状态为 saved=true、dirty=false。`mainGame` 与 Build Settings hashes 此前已核对未变。

上一轮 smoke 检查观察到：Dropdown 有 4 个选项、1 个 listener，English 筛选显示 5 行；Achievements 显示 3 张卡片并绑定 6 张 sprite，Image 为 Sliced；EventSystem 与 GraphicRaycaster 各有 1 个。上一轮还验证了中英混合词表、3 星/85 秒模拟成绩、元数据编辑保留成绩、改词取消确认不变、确认后 revision 2 清当前成绩并归档旧成绩。以上均是旧 mock iteration 的观察，不是本轮通过证据。

上一轮社区操作 smoke：通过运行时触发 `Action_Download.onClick.Invoke`，`WeekendGarden` 当时被加入 My Lists 并缓存；断网 Play 和举报后再次 Play 分别记录 `playCount = 1`、`2`，均为 3 星、85 秒。本轮分组决策已取代“下载进 My Lists”，这些旧结果不能代表本轮目标。

旧版发布操作 smoke：原创词表通过 Button 事件执行 Publish、Unpublish、Publish again，状态为 `Published → Unpublished → Published`，沿用 mock remote ID `mock-published-01`；断网首次 Publish 被阻止并显示联网提示。沿用旧 ID 的结果已被当前 Unpublish 清绑定、再次 Publish 创建新 mock ID 的规则取代；新 owned-list 生命周期验收见文首。这些记录不表示 Steam Workshop 请求已完成。

上一轮 Footer 诊断记录：问题不是横向左侧裁切，也不是 `Mask`。在问题状态下调用 `ForceMeshUpdate` 后，TMP 报告 character count 为 0、mesh bounds 为 0；`cull=false` 且 alpha 为 1。单独把 y 从 -20 改到 2 没有解决。保持 y=-20，并组合 32 高度、`Overflow` 和 `NoWrap` 后，TMP 生成 33 个字符、64 个 vertices，截图实际显示文字。单项根因没有分离，证据只支持“组合配置有效”，不能归因于高度或某个单一属性。上一轮 controller 落盘了该组合和 `autoSize` 10..16；此证据不代表本轮有代码变更后的构建/截图状态。

上一轮事件监听检查说明：Dropdown smoke 中报告的 1 个 listener 是运行时监听数；部分 Button 的 `persistentEventCount = 0` 不代表未接线，因为 controller 在运行时通过 `button.onClick.AddListener(onClick.Invoke)` 绑定回调。上一轮 EditMode 测试记录为 **31 passed / 0 failed**；本轮对源码有改动，这个结果不能作为本轮通过证据。

## 迁移前实现证据与验收状态（历史；不代表当前 Scene-authored 迁移）

### 历史观察

- 更新后的 fresh DLL 晚于 corrected source；EditMode 33 passed / 0 failed。通过该 DLL 运行 `BuildPreviewInEditor` 后，派生 mockup scene 已刷新并保存，scene 为 clean。
- Play 场景中 My Lists 有 6 项、Community Downloads 有 3 项、My Published 有 3 项。折叠 Community Downloads 只隐藏该组自身的 rows。
- `Café` 的 Remove Download 确认对话框文案说明了本地删除语义；按 Cancel 后本地条目仍在，selection 保留。
- 随后确认删除 `Café` 下载缓存：运行结果为 `localgone`、`remoteexists`、`downloadedfalse`，列表总数为 11。删除本地缓存没有删除远端社区条目。
- 重开 `words` 后进行后台、无 CUA 的 30 fps 临时 cap 验证：新下载 `Weekend` 进入独立 Community Downloads 组，该组自动展开，新条目被选中且 row 在 viewport 内。列表 content height 为 1234、viewport 为 445；scroll offset 设为 150 后触发真实 Favorite 按钮，等待 coroutine 完成后 offset 仍为 150，selection 未变。
- Inline Words 预览使用 72 个词；TMP 生成 914 个字符，content height 为 1680.1，超过 100 的 viewport。滚动 offset 实测从 0 到 1580.1 再回到 0；运行时检查中预览与操作区没有重叠。未生成截图，因此没有截图视觉验收结论。
- 历史 CreateVersion gate 实测中，内容不变或只改标题时按钮 disabled，改词后才允许创建无 remote identity 的副本；这是已被取代的旧 UI 行为。当前 Copy & Edit 可原样保存或只改 metadata，最新验收见文首。
- 在同一个 Café 删除确认场景中，确认 Remove 后观察到 `localgone`、`remoteexists`、`IsDownloaded=false`；随后从 Community 实际重新下载，生成新 ID `created02`。该项出现在只读的 Community Downloads 中，保留相同 remote 来源；分组展开，新条目已选中且完整位于 viewport 内。
- 最终运行后已 Stop Play，fps 恢复为 `-1`；`words` PID 61048 就绪且停止。mock scene 为 clean，未保存 runtime 交互状态；正式 `mainGame` scene 与 Build Settings hash 保持不变。

### 崩溃与恢复记录

- 旧 `words` Unity 进程 PID 17668 在成功删除 `Café` 本地下载后、导航到 Community 时连接断开。专属 crash log 记录 `D3D12 Device failed (887a0006)` / `Unrecoverable GPU device error`，栈为原生 `gfxworker`。触发原因尚未确定；当前证据不能断言该 UI 操作与崩溃有关或无关。
- 诊断文件位于项目外：`D:/Files/Desktop/Unity/Projects/GDS4 Game-a-Week/02-words/_unity_crash_diagnostics/WordListHub-UnityCrash-20261031-083313Z/d3d12-native-stack.txt`；没有上传 dump。
- 用户重新打开的 `words` PID 61048 已核对项目路径；另一个 `HorrorConv` PID 2716 未操作。正式 scene 与 Build Settings hash 未变。该轮 CUA 操作已停止。
- 已完成一次后台小步 30 fps 临时 cap 验证，未修改项目图形配置；Play Mode 停止后 fps 恢复为 `-1`。这次验证没有确定 D3D12 崩溃的触发原因。
- 本轮最终验证未观察到新的 GPU error 或 API interruption；这不改变旧 crash log 记录，也不能确定旧崩溃原因或声称已修复。

### 迁移前验收结果

- 33 项 EditMode tests、下载与 Play 导航、scroll/selection 保持、Dropdown 20 px 行高、Inline Words 滚动、旧版 CreateVersion 内容门控与复制，以及 Café 同项删除后重新下载均曾验收通过。旧 CreateVersion 门控已被作者已发布条目不可变规则取代；新规则另由文首 39 项测试、编译和两次 Play 验收。
- Inline Words 的 72 词几何/mesh/scroll 检查及 preview/actions 不重叠的运行时检查已通过；旧版 CreateVersion 与列表操作有真实交互证据。未生成本轮最终像素截图，因此不能称截图视觉验收通过。
- 最终 Play 已停止并恢复 fps `-1`；runtime 状态未保存到 mock scene。正式 `mainGame` 与 Build Settings hash 未变。

| 项目 | 已确认目标 | 本轮状态 |
|---|---|---|
| 分组 | My Lists 放本地原创、导入与独立编辑副本；Community Downloads 是独立可折叠组；My Published 独立显示 | 已观察到三组分别为 6/3/3 项；折叠只隐藏 Community Downloads 自身 rows。上一轮把下载混入 My Lists 的行为已被取代 |
| 分组导航状态 | 普通列表变更保留当前 scroll/selection；普通 Download 和 Play 自动下载进入 Community Downloads，下载后展开该组、选中新条目并 scroll into view | `Weekend` 和 Café 同项重下均进入独立只读组、自动展开、选中新条目且 row 完整在 viewport 内；offset 150 时 Favorite coroutine 完成后 offset 与 selection 保持。scroll/navigation 交互已验收；无本轮最终像素截图 |
| 删除 | 删除本地原创或下载项需确认；删除下载项仅删本地副本、不下架远端且可重下；删除已发布列表前先下架 | `Café` 取消确认保留本地条目和 selection；确认后 `localgone`、`remoteexists`、`IsDownloaded=false`，同项重下生成 `created02` 并回到 Community Downloads。此删除/重下闭环已验收 |
| Words 预览与 Dropdown | 详情直接预览完整 Words，使用独立滚动区；Dropdown Item 行高 20 px，与正式原版一致 | 72 词预览 TMP 914 字符、content 1680.1、viewport 100，offset 从 0 到 1580.1 再回到 0；几何/mesh/交互检查及与操作区无重叠已验收。Item 高度 20 px 已验收；未生成最终像素截图 |
| 旧 CreateVersion 编辑门控（历史） | 曾要求 Words/Language 有变化才创建副本；该要求已被新作者规则取代 | 旧行为是历史证据，不是当前产品规则。新规则已由 39 项 EditMode、0 errors / 0 warnings 编译及两次 Play 验收；Copy & Edit 可原样保存或仅改 metadata，原 source/records 不变 |
| 固定内容与缓存 | 移除旧 `UpdateAvailable` 内容更新 fixture/path；相同 remoteId 内容不得自动覆盖缓存；未来 metadata sync 另议 | 当前 mockup Runtime/Tests 中未发现旧 `UpdateAvailable` 路径或 fixture；Café 删除后重下生成新 local ID，远端来源保持不变。metadata sync 另议 |
| 迁移前验证 | 当时的 EditMode、fresh DLL 与派生 scene | 33 passed / 0 failed；fresh DLL 在当时 corrected source 之后生成；旧 `BuildPreviewInEditor` 曾刷新并保存派生 scene。该结果不验证当前 Scene-authored 迁移；旧 D3D12 崩溃原因仍未知 |
| 历史验证 | 上轮 EditMode 31 passed / 0 failed，及旧截图、功能 smoke | 仅历史记录；不得当作本轮验证 |

## Steam 投票、举报和下架的界限

- Steam 的 [`ISteamUGC`](https://partner.steamgames.com/doc/api/ISteamUGC?language=english) 文档将 `SetUserItemVote` 描述为对 Workshop item 投赞成或反对；查询支持按 vote popularity 排序，也有票数与评分字段。这是评分/排序信号，不等于玩家投票即可下架。
- 同一 API 文档列出按举报权重排序和举报数统计。Steam 的[内容举报说明](https://help.steampowered.com/en/faqs/view/4DE7-17AA-0E8B-C1AD)描述在 Steam 社区页面举报 UGC；[Steamworks 社区管理文档](https://partner.steamgames.com/doc/marketing/community_moderation?l=en)说明有权限的社区管理员/版主可对 Workshop 内容执行兼容性隐藏、不当内容隐藏、封禁或清理举报等处理。
- Steam 社区[规则 FAQ](https://help.steampowered.com/en/faqs/view/6862-8119-C23E-EA7B)说明 Steam 版主会审核被举报内容。Steam 的全局政策审核与本项目开发者对游戏内展示/精选的审核是不同流程。
- 已查官方文档没有公布“达到多少赞踩或举报就自动隐藏/下架”的阈值；自动阈值未证实，不在产品规则中假设存在。游戏内举报者即时隐藏是本项目自己的本地体验。

## 上一轮验收记录（历史，不代表本轮通过）

上一轮 pipeline 截图为 `Temp/pipeline-screenshots/WordListHub-Final-Library-footer-eae03fbfac9548a696c02b886a74732f.png`。当时 Library 页可见、Play Mode 已停止、saved=true、dirty=false、compiling=false；`mainGame` 与 Build Settings hashes 未变，编译 `failed=false`、0 errors，EditMode 31 passed / 0 failed。这些仍是上一轮记录；本轮当前验证状态见上方证据区。真实 Steam Workshop 未接入。

不要调用旧 builder 重建 preview；它现在只会报错退出。布局迁移入口为 `Words.UiMockups.Editor.WordListHubSceneMigrationTool.MigratePreviewScene`，只在明确执行时保存既有目标 Scene；dirty 场景会被拒绝，执行前会备份，失败时按未变的磁盘副本恢复目标 Scene。已经迁移的布局以后直接在 Scene 与模板中编辑，不需要再运行迁移工具。以下路径是迁移前的稳定工作区备份：

```text
D:/Files/Desktop/Unity/Projects/GDS4 Game-a-Week/02-words/_scene_backups/WordListHubMockup-20261031-171903
```

## 后续阶段

1. **完成 mockup 验收：** 当前 39 项 EditMode 测试、0 errors / 0 warnings 编译及作者不可变规则的 Play smoke 已通过。后续若继续迭代交互，需重新运行相关验证；视觉截图验收仍需单独留证。
2. **正式数据结构与兼容：** 定义生产 schema、旧存档迁移和导入导出兼容；确定成绩按可玩内容版本绑定。当前 mock Words 不作为最终 schema。
3. **Steam Workshop 双账号闭环：** 使用两个测试账号验证发布、浏览、下载、点赞、举报、Copy & Edit 后另发新条目及原条目可见性切换；当前阶段不建设自有服务器。
4. **后续 leaderboard 与审核自动化：** 再评估公开排行榜、人工复核工具和 AI 辅助；AI 不自动隐藏或下架。
