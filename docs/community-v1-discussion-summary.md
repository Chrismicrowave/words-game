# Community V1 与排行榜讨论总结

**状态：** 2026-10-31 决策汇总；本轮新增 UI 决策已取代上一版部分分组与内容更新假设。当前 corrected source 已通过 EditMode 33/33，并已刷新、保存派生 mockup scene（clean）；部分运行交互仍待验收。本文把当前讨论收敛为 Community V1 的主索引；与较早 WIP 冲突时，以本文最新结论为准。具体字段和迁移风险仍参考
[`community-wordlists-wip.md`](community-wordlists-wip.md)、
[`wordlist-data-model-wip.md`](wordlist-data-model-wip.md) 和
[`community-architecture-overview-wip.md`](community-architecture-overview-wip.md)。UI mockup 当前文件、隔离边界和验收清单见
[`ui-mockup-handoff.md`](ui-mockup-handoff.md)。

**目标：** Steam 首发版本提供玩家创建、发布、发现、下载和评价社区词表的能力；本地功能、离线游玩和个人成绩不依赖社区服务。公开排行榜和动态 Top 100 作为后续阶段，不阻塞 Community V1。

## 1. 已确认的大框架

### 1.1 产品入口

全局导航保持四个独立入口：

- **Challenges**：官方挑战与挑战进度。
- **Word List**：玩家本机拥有的词表、社区下载、自己发布和收藏。旧讨论和代码中的 `Word Library` 是同一入口的工作名称；mockup 的可见文案目标改为 **Word List**。
- **Community**：寻找、点赞、下载、发布和管理公开词表。
- **Achievements**：游戏成就与 Steam 成就状态。

Word List 当前采用三个独立主分组；社区下载不混入 My Lists：

1. **My Lists**：自己创建、导入的本地词表，以及从社区原版复制后生成的独立本地编辑副本。
2. **Community Downloads**：已缓存的社区原版，单独显示在可折叠分组中；普通 Download 和 Play 自动下载都加入此组，并保持只读、显示作者/来源标记。
3. **My Published**：已经发布到社区的自有词表。

下载完成后，Community Downloads 自动展开，选中新条目并滚动到可见区域。Favorite 是跨分组的收藏视图，不复制词表。Like 是公开质量反馈，不等于 Favorite，也不等于 Subscribe/Download。

普通列表变更应保留当前滚动位置和选择项；下载属于明确的导航动作，按上面的规则展开 Community Downloads 并选中新下载项。

### 1.2 Steam 优先，但不把 Steam 当数据库

- Steam 首发优先使用 Steam Workshop 承载公开内容、作者信息、订阅和下载；投票与游玩数据能否满足产品排序和榜单需求仍需验证。
- 其他商店版本以后实现自己的社区适配层，复用游戏内 UI。
- Steam Workshop 可以作为内容目录和发现信号来源，但不是游戏个人存档、完整历史成绩或 Top 100 选择的通用数据库。
- 对相同 `remoteItemId` 的内容变化，不自动覆盖玩家已有缓存；内容固定发布决策下，移除 `UpdateAvailable` 内容更新 fixture/path，包括旧 `Café 14→16` 假数据。未来是否同步非玩法 metadata 另行讨论。
- Community V1 不依赖自建服务器也能浏览、下载、游玩和保存个人成绩。

### 1.3 内容发现与成绩榜分离

必须使用不同语言和页面层级：

- **Community Browse**：回答“现在有哪些词表值得玩”。
- **Leaderboard**：回答“某个词表上谁的成绩最好”。

Community 的产品排序目标可以包含 Popular now、Most played、Most liked、Newest；这些排序项的数据来源和技术可行性仍需后续验证，不能据此认为 Steam Workshop 已提供全部所需信号，或动态 Top 100 已能直接实现。单个词表详情中才显示该词表的成绩榜。词表详情中的完整 Words 预览使用独立滚动区域，长词表不挤压或裁掉详情操作区。

### 1.4 发布后的可玩内容不可变

V1 采用以下规则：

- 通过 Publish 发布时，条目应公开可见；首次发布后，词语、阶段、语言、拼音和游戏规则内容不可原地修改。
- 标题、简介、标签等不影响玩法身份的元数据可以更新。
- 任何可玩内容变化都产生新的 `contentHash`，并要求 **Publish as New List**。
- 新内容使用新 `localListId`、新 Workshop Item 和独立成绩 scope。
- 旧内容、旧点赞和旧成绩保持在其原条目上，不迁移到新内容。
- 作者可以把旧条目设为私密来停止公开，并发布新的内容版本；已经按规则提供的本地缓存不能被远端操作静默删除。

这样避免订阅者、已下载玩家、成绩榜和 Top 100 快照在同一个 Workshop Item 下发生内容语义变化。

### 1.5 社区内容本地缓存

下载完成后，游戏应把经过验证的词表 JSON 保存到自己的版本化缓存目录，而不是长期直接读取可能被 Steam 更新或清理的安装目录。

```text
CommunityCache/
  <remoteItemId>/
    <contentHash>/
      list.json
      manifest.json
```

Steam 安装目录只用于接收下载结果。游戏可玩数据和之后另存的本地副本都基于自己的缓存副本。

### 1.6 远端状态与本地状态分离

```text
remoteStatus = available | private | deleted | banned | incompatible | unknown
localStatus  = cached | missing | corrupt
```

- 网络失败只能得到 `unknown`，不能当作已下架。
- `private` / `deleted`：播放器可继续使用已有缓存，并看到 `Save as My List` 或 `Remove`；不自动转为自有词表。
- `banned` / `incompatible`：Community 入口停止提供正常游玩，不提供绕过平台管理的保存按钮。
- 社区原版保持只读。`Save as My List` 生成新 UUID 并切断远端绑定。
- 对同一 remote item 的新内容，不自动覆盖本地 cached 内容；`UpdateAvailable` 内容替换流程已从本轮 mock 目标移除。未来 metadata sync 规则另行讨论。

### 1.7 其他作者重新发布相同内容

- 即使 `contentHash` 相同，不同 Workshop Item 也始终是不同社区条目。
- 不自动继承作者、点赞、收藏、下载绑定或排行榜。
- 只有玩家主动选择切换来源时才更新 `downloadBinding`。
- 不同作者重发同一内容最多显示 `Same words as another community list`，不建立所有权或版本继承关系。
- 同一作者管理“系列词表”所需的 `seriesId` 留到后续版本，不进入 V1 必做范围。

### 1.8 审核边界

- Steam 负责 Workshop 平台规则、Steam 页面上的举报审核和平台封禁；游戏审核只决定游戏内是否展示、是否进入精选和是否进入 Top 100。
- 游戏内举报是玩家安全控制：提交举报后，立即只对举报者隐藏该词表，并送入开发者复核；这不会自动改变 Steam Workshop 的全局可见性。
- 游戏内 Like/Steam 投票用于质量反馈和排序；Report 用于安全、违法、冒充、恶意内容和明显刷榜，不以踩票代替举报。
- AI 可以为开发者整理举报、标注风险和给出建议；AI 不自动隐藏全局内容、不自动封禁或下架，最终处置由开发者复核。
- Steam 官方 `ISteamUGC` 文档说明 `SetUserItemVote` 可投赞成或反对，并支持按投票受欢迎程度排序；另有按举报权重排序和举报数统计。文档未公布因票数或举报数自动隐藏/下架的阈值，不能把投票或举报解释为自动下架机制。
- V1 不启用严格的 Curated Workshop 模式。Steam 页面保持普通 Workshop 流程，游戏内维护自己的 `approvalStatus` 和个人隐藏状态。

## 2. 数据身份

| 身份 | 含义 | 规则 |
|---|---|---|
| `localListId` | 玩家本机可编辑词表 | 完整 UUID；另存、复制和新发布会生成新值 |
| `contentHash` | 某份可玩游戏内容 | 由规范化后的阶段、段、词语和拼音计算 |
| `remoteItemId` | Steam Workshop 条目 | 与本地 UUID、内容 hash 分离；JSON 中用字符串保存 |
| `approvalRevision` | 某次游戏内审核结果 | 绑定被审核的 `contentHash`、策略版本和结果 |
| `scopeKey` | 某个公开成绩榜作用域 | 由公开词表身份、内容版本、规则和模式共同决定 |

本地下载原版、发布后的自有词表和玩家另存副本具有不同生命周期。任何 schema 都不能通过文件名、显示标题或 Steam 昵称推断身份。

## 3. Community V1 页面与动作

### 3.1 Browse

- 搜索标题和作者。
- 语言与玩法兼容性筛选。
- Popular now、Most played、Most liked、Newest。
- 卡片显示标题、作者、语言、词数、更新时间、Like、游玩和下载状态。
- 新内容、已下载内容和审核中内容具有明确状态；移除 `UpdateAvailable` 内容替换 fixture/path（包括矛盾的 `Café 14→16` 假数据）。未来 metadata sync 另行讨论。

### 3.2 详情

- 预览标题、作者、简介、语言和阶段数，并在独立滚动区域直接预览完整 Words 列表。
- 动作：Play、Like、Favorite、Download、Report。下载完成后将条目加入 Community Downloads、展开该组、选中新条目并滚动到可见区域；若玩家直接 Play 尚未下载的社区词表，先自动下载，再开始 Play。
- 社区原版另存时使用 `Copy & Edit` / `Save as My List`。
- 当前阶段只显示个人最佳；公共 Leaderboard 作为第二阶段入口。

### 3.3 Community Downloads

- Community Downloads 是独立、可折叠的分组，普通 Download 与 Play 自动下载的社区原版都进入此组，并显示作者和远端来源；下载完成后展开该组、选中新条目并滚动到可见区域。
- 下载内容写入本地缓存并可离线游玩；未缓存内容离线时明确提示。即使玩家个人隐藏或社区条目不再公开，已经缓存的本地副本仍可从此组游玩。
- 社区原版保持只读；Copy & Edit 生成无 remote identity 的 My Lists 独立本地副本。
- 删除已下载词表需要确认；只删除本地副本，不下架远端内容。用户之后仍可重新下载。
- 同一 remote item 内容变化不自动覆盖缓存；未来 metadata sync 另议。不因远端下架静默删除玩家已有缓存。

### 3.4 My Lists 与删除

- My Lists 只包含玩家创建、导入或从社区复制出的本地自有副本，不包含社区下载原版。
- 删除本地原创词表前显示确认。删除带已发布远端身份的列表前，必须先下架，再执行本地删除确认；本地删除动作不等同于删除 Steam 远端条目。
- 列表详情中的完整 Words 预览使用独立滚动区，长词表不挤压或裁掉详情操作区。
- Dropdown template 的每个 Item 行高目标为 20 px，与正式原版 UI 保持一致。

### 3.5 My Published

- 显示 Workshop 状态、审核状态、Like、游玩数和当前内容版本。
- 发布后公开可见；标题、简介和标签等元数据可更新，可玩内容变化只能发布新列表。
- 旧条目可以停止公开，作者可将其设为私密；旧内容身份和成绩不迁移。
- 支持打开 Steam 页面、检查发布结果和停止公开。
- 不把删除远端条目和删除本地自有列表混成一个动作。
- 发布词语固定；Edit/CreateVersion 仅在玩法内容实际改变时可提交。Words 和 Language 未改变时按钮禁用并显示就地原因；改标题等 metadata 不足以构成新内容版本。确认后创建新的独立本地 ID，不带 remote identity，也不修改原 published 条目。

## 4. 审核与 LLM 建议

AI/LLM 只辅助开发者复核，不承担最终封禁或下架权。

### 4.1 建议流程

```text
作者发布
  -> Workshop 条目公开可见，游戏内可发现
玩家举报
  -> 举报者本地立即隐藏该条目
  -> 报告进入开发者复核队列
  -> 确定性检查与 AI 风险建议仅提供参考
  -> 开发者决定是否继续展示、从游戏内列表移除或提交 Steam 平台审核
```

### 4.2 确定性校验

- schema 版本、阶段数、词数、文件大小和字段长度。
- 中文、英文和混合内容 parser 能否真实加载。
- 非法控制字符、路径、脚本或未知字段。
- 标题、简介和词条样本的强制限制。
- 完全相同的内容 hash 与可疑重复内容。
- Steam 返回的 banned、private、incompatible 状态。

### 4.3 LLM 分类

AI 可输出结构化风险建议，例如：

```json
{
  "recommendation": "no-policy-risk-observed",
  "riskLevel": "low",
  "categories": [],
  "confidence": 0.94,
  "reasonCode": "CLEAN_TYPING_CONTENT",
  "summary": "English travel vocabulary with no policy risks observed.",
  "changesVisibility": false
}
```

开发者复核建议：

- AI 标注只用于排序和辅助判断，所有内容隐藏、取消公开或封禁动作都由有权限的开发者/平台审核者决定。
- 模糊、低置信度或严重风险均进入人工复核；AI 不自动通过，不自动隔离或下架。
- Steam 已 `banned` 或 `incompatible` 时，游戏遵守平台状态并停止正常提供内容。
- 玩家举报后，举报者的本地隐藏立即生效；其他玩家仍按公开状态查看，直到开发者或 Steam 审核处理。

### 4.4 安全边界

- 社区内容全部视为不可信输入，不能让其中文字成为模型指令。
- LLM 无 Steam 后台、数据库和封禁工具权限。
- Publisher key 和模型 key 只存在后端或开发者工具，不进入 Unity 客户端。
- 记录策略版本、模型版本、结果和 reason code，不保存思维过程。
- 用户需要隐私说明和基本申诉入口。

## 5. 测试策略

### 5.1 Fake provider

当前阶段先以 fake provider 完成可操作 mock UX。以下是验收目标，不代表当前 mockup 已满足：

- 空数据、加载、刷新和失败状态。
- 下载中、下架、封禁和损坏缓存；不再使用 `UpdateAvailable` 内容替换 fixture。
- Like/Favorite 操作失败与回滚。
- 编辑后另存、新发布和旧成绩归档。
- 普通 Download 与 Play 自动下载进入独立可折叠 Community Downloads 组；下载后展开、选择并滚动到新项；下载缓存支持离线。
- 删除本地原创或已下载词表有确认；删除下载项不下架远端且允许重新下载；删除已发布列表前先下架。
- 详情完整 Words 预览放入独立滚动区；Dropdown Item 行高为 20 px。
- CreateVersion 在 Words/Language 没有实际变化时禁用并显示原因；仅在内容变化后启用。确认后生成无 remote identity 的独立本地 ID，不修改原 published 条目。
- 相同 remoteId 的内容不自动覆盖本地缓存；metadata sync 后续另议。
- 标题/简介/标签可修改，已发布词语不可原地修改；改词发布新条目，旧条目可设为私密。
- 举报后仅举报者立即隐藏；开发者复核队列可操作；AI 建议不触发自动隐藏或下架。
- 筛选使用真正的下拉控件；全局入口文案使用 Word List；Achievements 独立页面包含成就图片。
- 保存场景、inactive 节点和运行时生成的 UI 中每个 `Image` 均为 `Sliced`。
- 超长标题、中文、混合内容和本地化布局。
- 键盘、鼠标和手柄焦点顺序。

### 5.2 Steam 私有测试

Steam Workshop 官方支持私有测试：

- 创建私有 Steam Community Group。
- 把 Workshop Visibility State 设为 `Developers & Testers`。
- 使用两个拥有 App `4966870` 许可的测试账号。
- A 上传，B 查询、订阅、下载和点赞；验证同 remoteId 的新内容不自动覆盖本地缓存。metadata sync 后续另议。
- 测试作者设为私密、删除、Steam 管理 ban、重新提交和网络失败。
- Family Sharing 临时许可不能测试上传。

### 5.3 接入节奏

按以下阶段推进：

1. 完成独立 functional mock，覆盖主要页面、状态和本地交互。
2. 进行用户 UX 验收，定稿导航、文案、分组、表单与错误提示。
3. 定义正式数据结构、旧存档兼容和导入导出路径，并确定成绩与可玩内容版本的绑定规则。当前 mockup 的 Words 字段不代表最终生产 schema。
4. 用 Steam Workshop 双账号完成发布、浏览、下载、点赞、举报、作者 metadata 更新和新内容另发闭环；此阶段暂不建设自有服务器，并验证相同 remoteId 的新内容不自动覆盖本地缓存。
5. 后续再做 leaderboard 与审核自动化；人工审核边界保持明确，AI 不自动隐藏或下架。

## 6. Roadmap

| 阶段 | 交付重点 | 当前状态 / 退出条件 |
|---|---|---|
| **1. 独立 functional mock** | 在隔离场景整合旧 UI 和新社区操作；使用 mock-only Words 与本地状态 | 本轮 corrected source 的 EditMode 为 33 passed / 0 failed；fresh DLL 已刷新并保存派生 scene。已观察到 My Lists/Community Downloads/My Published 分别 6/3/3 项、下载组折叠只隐藏自身 rows，以及 `Café` Remove Download 对话框取消后条目与 selection 保留。确认删除后重下、scroll/selection 导航、Words 独立滚动和 CreateVersion 新行为仍在验收；此前 31 项测试和截图仅属上一轮。 |
| **2. 用户 UX 验收** | 在 Unity 中逐页体验，定稿导航、文案、分组、表单、离线提示与错误状态 | 完成用户验收并记录修改后再进入正式数据设计。 |
| **3. 正式数据结构与兼容** | 定义生产 schema、旧存档迁移/导入导出兼容，以及成绩按可玩内容版本绑定 | 需经正式模型与保存路径评审；原型 `Words` 属性不是最终生产结构。 |
| **4. Steam Workshop 双账号闭环** | 两个测试账号验证发布、浏览、下载、点赞、举报、作者 metadata 更新及新内容另发；验证缓存行为 | 暂不建设自有服务器；闭环需在 Steam 私有测试环境实测通过。相同 remoteId 的内容不自动覆盖缓存，metadata sync 另行定策。 |
| **5. 后续 leaderboard 与审核自动化** | 再评估公开排行榜、人工审核工具与 AI 辅助自动化 | 排行榜和审核服务独立立项；AI 建议不自动改变全局公开状态。 |

## 7. 下一轮讨论顺序

先讨论会改变整体架构的问题，细节使用建议默认值即可。

### Q1. Community 首发是否带公开成绩榜？

**建议默认：不带。** 首发只做发现、下载、点赞、收藏、个人最佳和人工审核流程；公开成绩榜与动态 Top 100 延后到 roadmap 后续阶段。此延期决策不确认排行榜的数据来源、Steam 可行性或是否需要后端服务；这些问题届时再验证。Community V1 的浏览、下载和本地个人成绩不以公开榜为前提。

### Q2. 普通 Workshop，还是 Curated Workshop？

**建议默认：普通 Workshop + 游戏内审核。** 由 Steam 处理平台规则，由游戏决定是否展示。只有以后确实需要让 Steam 页面也阻止未批准内容时，再评估 Curated Workshop 的逐个审批成本。

### Q3. 新词表首发体验是什么？

**已确认：作者执行 Publish 后默认公开可见。** 只有玩家提交举报或平台规则处置等情况才进入相应复核；举报者的个人隐藏即时生效，但不会先把条目全局下架。不做“先评价别人才能发布”的强制任务；用编辑精选解决冷启动。

### Q4. 首发是否启用 AI 审核辅助？

**已确认：AI 只辅助、不会自动下架。** 早期投稿少，先把人工复核状态和 deterministic validator 做对；之后可加 AI 风险摘要，但公开状态仍由有权限的审核者决定。

### Q5. 哪些列表能参加未来公开榜？

**建议默认：已通过审核且内容版本稳定的公开列表。** 私人列表、未审核列表、已知 incompatible 内容不提交成绩。

### Q6. Solo 与 Shared Co-op 如何计分？

**建议默认：两个独立 scope。** 首发若只做 Solo 公开榜，Shared Co-op 继续保留本地个人纪录，不混排。

### Q7. Top 100 的“热门”如何定义？

**暂缓决定：** 近期游玩会话与 Like 可作为后续评估的候选信号；需先验证 Steam 是否能提供项目需要的数据与排序能力，再决定是否采用 Steam 信号、其他服务或不同方案。目前不确认动态 Top 100 的实现方式，也不把排行榜视为已实施功能。

## 8. 官方参考

- [Steam Workshop](https://partner.steamgames.com/doc/features/workshop)
- [Steam Workshop Implementation Guide](https://partner.steamgames.com/doc/features/workshop/implementation)
- [ISteamUGC](https://partner.steamgames.com/doc/api/ISteamUGC?language=english)
- [Steam Community Moderation](https://partner.steamgames.com/doc/marketing/community_moderation)
- [Steam Support：Reporting content within the Steam Community](https://help.steampowered.com/en/faqs/view/4DE7-17AA-0E8B-C1AD)
- [Steam Support：Rules and Guidelines for Steam UGC](https://help.steampowered.com/en/faqs/view/6862-8119-C23E-EA7B)
- [IPublishedFileModerationService](https://partner.steamgames.com/doc/webapi/IPublishedFileModerationService)
- [Steam Leaderboards](https://partner.steamgames.com/doc/features/leaderboards?l=english&language=english)
- [Testing on Steam](https://partner.steamgames.com/doc/store/testing)
