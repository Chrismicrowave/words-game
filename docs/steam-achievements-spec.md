# Steam 成就技术规格

**状态：** 首批三条成就的本地进度、游戏成功分支与 Steam 适配代码已实现；真实 Unity/SDK 引用编译及本地自动测试已通过。Steamworks 后台配置、实际账号解锁与重启回读仍待验收，不能据编译结果宣称联通完成。
**范围：** 本阶段按 Steam Achievement 解锁展示设计；Steam Inventory 物品/经济奖励暂不纳入，若需要另立需求。

## 已确认方向

- 以游戏内进度条件解锁 Steam 成就。
- 把条件判断与平台 API 分开：游戏逻辑只报告进度事件，统一成就服务负责进度持久化和解锁；Steam adapter 单独封装 `ISteamUserStats`。未来其他商店可接入独立 adapter，不要求现在实现。
- 不把 Steam API 调用散落在 `GameCoordinator`、UI 或每个玩法条件中。
- 首个纵向技术验证使用 `COMPLETE_FIRST_WORD`；随后验证另外两条已确认规则。
- 成就解锁不能依赖将来的 Workshop 发布，也不应阻塞首条成就的纵向验证。

## 当前代码挂点与缺口

| 现有代码 | 当前职责 | 成就接入意义 |
|---|---|---|
| [`GameStateManager.cs`](../Assets/-Scripts/Core/GameStateManager.cs) | 状态进入 `PhaseComplete` 时触发 `OnPhaseCompleted`；进入 `AllComplete` 时触发 `OnAllPhasesCompleted`。 | 阶段完成、整局完成的领域事件入口。 |
| [`GameCoordinator.cs`](../Assets/-Scripts/GameCoordinator.cs) 的 `HandleAllPhasesCompleted` | 取 `PhaseManager`、`TimerSystem` 数据；对挑战模式调用星级、解锁逻辑并显示完成面板。 | 可从完成流程派发领域进度，但不应在此直接调用 Steam API。 |
| [`PhaseManager.cs`](../Assets/-Scripts/Core/PhaseManager.cs) | 当前局的 `TotalErrors`、总阶段数和活动词表。错误数在失败时递增，加载新词表时清零。 | 零错误、完成阶段数等条件的数据源；重开/重玩是否计入累计需明确规则。 |
| [`TimerSystem.cs`](../Assets/-Scripts/Core/TimerSystem.cs) | 管理当前局 `TotalElapsedTime`；运行时累计，重置时清零。 | 当前局时长可作单局条件；跨局累计需要新增持久进度。 |
| [`ChallengeProgression.cs`](../Assets/-Scripts/Core/ChallengeProgression.cs) | PlayerPrefs 持久化挑战解锁数和每关最佳星级；星级按错误数计算。 | 现有挑战进度可读，但没有通用成就事件日志或全局累计计数。 |
| [`ListTimeManager.cs`](../Assets/-Scripts/Core/ListTimeManager.cs) | 本地 JSON 保存每个自定义/挑战列表的最佳用时、阶段数和错误数。 | 是本地个人纪录，不是 Steam 成就同步服务，也不提供跨局计数。 |
| [`ILeaderboardService.cs`](../Assets/-Scripts/Leaderboard/ILeaderboardService.cs) | 现有 leaderboard 接口和 no-op 实现。 | 不是成就接口；不要把 leaderboard 直接改造成成就服务。 |

现已加入 [`WordAchievementProgress`](../Assets/-Scripts/Core/WordAchievementProgress.cs)、[`IWordAchievementPlatform`](../Assets/-Scripts/Core/IWordAchievementPlatform.cs) 和 [`SteamAchievementPlatform`](../Assets/-Scripts/Steam/SteamAchievementPlatform.cs)。成就只从 `GameCoordinator` 的实际 `StepResult.PhaseComplete` 成功分支记录，不同时订阅 `OnAllPhasesCompleted`，避免最后一个词双计。

## 首批已确认成就

以**成功完成的 word phase 次数**累计，跨游戏会话持久化。相同目标词每次真实完成都计数，不按唯一词文本去重。失败、仅按键、编辑列表和手动跳到某个 phase 都不算完成。

| Steam API Name | 解锁条件 | 实现判定 |
|---|---|---|
| `COMPLETE_FIRST_WORD` | 累计成功完成至少 1 个 word phase。 | 成功完成计数达到 1。 |
| `COMPLETE_TWO_WORDS` | 累计成功完成至少 2 个 word phase。 | 成功完成计数达到 2；重复完成同一词也算一次。 |
| `COMPLETE_WORD_WITH_D` | 至少成功完成一个实际输入目标包含 ASCII `d` 或 `D` 的 word phase。 | 只在 phase 成功完成时检查实际打字目标；检查目标字符而非按键事件。 |

`GameCoordinator.HandleKeyAction` 成功分支中的 `wordEngine.TargetText` 是这项检查的建议唯一输入来源；中文/混合 phase 的 `TargetText` 可能是解析后的拼音输入串。**是否把中文/混合拼音目标也纳入 `COMPLETE_WORD_WITH_D`，以及是否所有列表类型都参加累计，仍待确认。** 若未另行限定，实现应按成功 phase 事实计数，不因列表 UI/来源路径漏记。

### 后续候选（不属于首批范围）

零错误完成、星级里程碑、挑战推进、跨局用时/阶段里程碑、社区词表首次发布等可之后另行讨论；本阶段不实现，也不将 Workshop 发布作为首批成就的前置条件。

## 建议架构

```text
Gameplay events / progress facts
        ↓
AchievementService  ← 条件注册表、去重、进度保存、解锁状态
        ├── PersistentProgressStore  ← 游戏侧待同步进度（仅 Steam API 不可用时需要）
        └── IAchievementPlatformAdapter
                ├── SteamAchievementAdapter
                └── NullAchievementAdapter / future platform adapter
```

- 用稳定的内部成就 ID 对接 Steam API Name，集中维护映射和状态回读。
- 玩法层提交语义明确的事实，例如“挑战完成”“累计完成阶段”；条件表统一检测阈值，避免在 UI 回调中增加计数。
- 累计值使用单调、可重放安全的状态；若使用完成事件日志，每个事件要有稳定 ID，重复处理不得重复计数。
- 区分游戏域进度与 Steam 同步状态。Steam 解锁是幂等的；计数型 stat 应从已持久化绝对值设置，不能在重试时重复增加。

### 离线与重试

- Steamworks API 已初始化、Steam 客户端处于离线模式时，使用 Steam 自己的本地 stats/achievement 缓存和联网后提交机制。Steam 官方建议游戏不要再另存一份 Steam stats 缓存，以免产生冲突。
- 即使 Steam API 未初始化，游戏仍可游玩；游戏侧继续保存本地成就进度，等 Steam API 可用后按当前账号档案尝试同步。
- 不对同一进度同时做“Steam 本地缓存累计”和“游戏待同步累计”后再相加。恢复同步时以游戏域绝对进度/已完成成就状态为准，重复送达不能重复累计。
- Steam 设置为草稿时，`SetAchievement` / stat 调用不会正常工作；同步失败不得静默标记完成。

### 成就档案与 Steam 账号隔离

首批只需要持久化两个单调状态：`completedWordCount`（饱和到 2）和 `hasCompletedWordWithD`。成功词数到 1/2 时及首次完成含 D 目标时解锁对应成就；每次真实成功都可推进计数，但保存 2 后无需再增长。

- 已识别 Steam 账号的本地进度保存在按 SteamID 分开的档案中；同时保存最后一次已验证的 active-profile marker。
- Steam API 不可用时，沿最后已验证的档案继续记录；如果本机从未识别过 Steam 账号，则使用 `unbound` 档案。
- 首次识别 SteamID 时只尝试一次认领 `unbound`：目标账号档案不存在时，通过同一目录内的原子文件重命名整体转为该账号档案；目标已存在时不合并、不覆盖，保留 `unbound` 并记录明确警告/认领冲突状态。
- 档案绑定后切换 Steam 账号只加载该账号自己的进度，不能把另一账号的进度灌入当前账号。Steam API 不可用时没有可靠方法识别此刻的本地操作者；按最后已验证档案写入是当前选择，其共享机器边界需要纳入验收。
- 本地离线档案不是 Steam Cloud 同步；Steam 成就一旦成功同步，Steam 负责账号间展示/漫游。跨设备、从未登录 Steam 的 unbound 进度仅在其后发生的一次本地认领流程内处理。
- 同一次加载 phase 的成功处理需要 attempt token/guard 防止重复回调重复计数；显式重开并再次完成同一目标会获得新的 attempt 并计数。

## Steamworks 开发与验收计划

1. **确认访问条件：** 自有 AppID 处于可测试状态；开发账号拥有 Dev Comp package，且 package 包含要测试的 depots。核对 Steamworks 后台编辑权限。
2. **发布技术配置：** 为三条成就创建与本文 API Name 完全匹配的 Steamworks Achievement 定义，在 Steamworks Publish 页面发布配置。这个 Publish 是 Steamworks 配置发布，不是商店游戏发行。
3. **接通纵向切片：** 通过 Steam 客户端启动拥有 AppID 4966870 许可的开发构建；只在直接从 exe/debugger 开发时使用旁置 `steam_appid.txt`，发布 depot 前移除。成功完成一个目标，写入 `COMPLETE_FIRST_WORD` 并调用保存；检查回调和 API 回读，重启后状态仍一致。
4. **覆盖离线和重试：** 在 Steam 客户端离线但 Steam API 初始化成功时解锁，联网后确认 Steam 同步；另外模拟 Steam API 不可用时完成条件，确认本地档案状态在恢复后幂等解锁。测试 StoreStats 失败、重试、进程重启。
5. **覆盖边界：** 重复收到完成事件、阶段失败/重开、不同列表类型、无 Steam 客户端运行；无 Steam 时游戏仍可玩，不因成就服务不可用卡住主流程。
6. **测试数据隔离：** 只用专门测试账号通过 Steam 控制台清成就/重置 stats；不得对正式账号做重置。发行前不以普通 Community/游戏库公开显示作为验收或对玩家承诺，使用开发者预览与 API 回读验证。

### 完成标准

- 一条开发者专用成就从实际游戏条件贯通到 Steam API，并在回读/重启后保持一致。
- 重复事件、保存失败和重试不造成重复累计或丢失待同步进度。
- Steam 未初始化时游戏可继续运行，已有本地进度不会被误认为 Steam 已同步。
- 成就条件读取现有游戏数据，不改变挑战星级、用时记录、存档键或游戏流程。

## 前置与未知项

- Steamworks AppID 4966870 和后台编辑权限已由用户确认；App 状态及 Dev Comp package 是否含所需 depots 尚未验证。
- 玩家可见名称、说明、图标和多语言资源仍需在 Steamworks 后台配置；游戏内规则与 API Name 已按上表确认。
- 是否统计自定义/每日/中文/混合列表，以及拼音含 ASCII `d`/`D` 是否算含 D 的词，仍待确认。
- 通过 `unbound` 档案认领到第一个已识别 SteamID 时，如果该账号档案已存在，当前策略是不合并并保留原 unbound 状态；是否需要未来提供人工迁移工具另行决定。
- 实现已具备，但真实 Steam 后台及账号测试尚未完成；累计口径当前按上述默认规则执行，进一步产品限制仍可调整。

## Unity wrapper 选择

已通过本项目 Editor 的 Unity Package Manager Client API 安装 Steamworks.NET，并固定版本：

```text
https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2025.164.1
```

该 release 对应 Steamworks SDK 1.64；项目当前是 Unity 6 `6000.3.11f1`。安装记录已核实为本项目 PID 45224；未操作另一个项目。独立 Steam 程序集通过包版本约束与桌面平台约束启用，缺包或非支持平台走本地逻辑。

## 本阶段联通操作

1. 在 App `4966870` 的 Stats & Achievements 中建立上表三项，API Name 必须完全一致；`Set By = Client`，`Progress Stat` 留空。可用 Developer-only 成就组测试（需要开发者许可）。
2. 填显示名称、描述和图标后保存，并在 Publish 页发布 Steamworks **配置**，这不等于发行游戏。
3. 开发用 `steam_appid.txt` 已放在项目根目录，内容为 `4966870`。直接运行测试 exe 时需在 exe 旁放同样文件；正式 Steam depot 不应携带该文件。项目根文件不放入 `Assets` 或 `StreamingAssets`。
4. 登录有开发许可的 Steam 账号，在 `words` 项目测试。使用新测试档案完成 `hello` 后应达成首词；再完成 `world` 应达成两词和含 D。现有档案若已达成，连接后会补同步，不应为了复测重置正式存档或正式账号。
5. 以开发者预览、StoreStats 结果及 Steam API 回读验收，并重启复查。当前未执行 Play Mode、真实 Steam 初始化或账号解锁测试。

本地进度目录为 `Application.persistentDataPath/WordAchievementProfiles`。前三项只保存饱和到 2 的成功次数及是否完成含 D 目标；它不是通用无限累计统计。

## 官方资料

- [Steam 成就逐步指南：配置、发布、开发者专用组、开发者预览和测试](https://partner.steamgames.com/doc/features/achievements/ach_guide?l=english)
- [Stats and Achievements：离线缓存与未发行时可见性](https://partner.steamgames.com/doc/features/achievements)
- [ISteamUserStats API：SetAchievement、StoreStats 和回调](https://partner.steamgames.com/doc/api/ISteamUserStats)
- [Steam 上的测试：Dev Comp package、测试 key 与 beta](https://partner.steamgames.com/doc/store/testing)
- [Steamworks API 初始化：AppID、Steam 客户端、steam_appid.txt 开发用途](https://partner.steamgames.com/doc/sdk/api?l=english)
- [Steamworks.NET UPM 安装说明](https://steamworks.github.io/installation/)
- [Steamworks.NET releases：2025.164.1](https://github.com/rlabrecque/Steamworks.NET/releases)
