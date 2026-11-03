# 词表数据模型 WIP

**状态：** 本文记录本轮确认的数据身份、个人纪录边界，以及统一 Top 100 公开榜的架构方向，并提出可版本化的 JSON 形状。公开社区 scope 的完整成绩/归档由独立托管后端权威保存；游戏内榜单和 Steam 原生榜仅展示同一份服务端选出的 Top 100。它是设计草案，不代表运行时代码或存档已迁移；服务商、Workshop 票数查询口径、刷新周期和 Steam 后台配置仍待确认。

最新产品决策和 roadmap 汇总在
[`community-v1-discussion-summary.md`](community-v1-discussion-summary.md)。其中“公开后的可玩内容不可原地修改”为最新规则：内容变化产生新的本地词表和新 Workshop Item，不沿用旧 `PublishedFileId`。本文与汇总文档冲突时以后者为准。

**范围：** 词表内容与身份、个人资料库状态、个人纪录、发布/下载绑定、公开排行榜作用域与 Top 100/固定槽绑定。社区 UI、Workshop 上传实现、后端服务商与部署、计分规则和赛季规则不在本文中实现或定案。

## 1. 数据边界与已确认规则

| 数据对象 | 所属边界 | 身份/作用 |
|---|---|---|
| `WordList` | 可编辑的词表内容本体 | 使用完整本地 UUID；保存标题、语言、按序阶段和分段内容；内容指纹只描述可玩语义。 |
| `LibraryEntry` | 本机词表库中的一项 | 记录归属、收藏、最后游玩时间，以及独立的社区下载/发布绑定。一个词表库项只出现一次。 |
| `PersonalRecord` | 本地玩家资料中的个人记录 | 按玩家资料、词表身份、内容指纹、规则和模式隔离；内容改变后产生另一条纪录作用域。 |
| `DownloadBinding` / `PublicationBinding` | 本机库项的远端关系 | Steam 等平台 ID 与本地 UUID 分开；下载源与上传目标都不混入词表身份或内容哈希。 |
| `LeaderboardScope` / `LeaderboardBinding` | 公开榜 scope 与后端/Steam 槽绑定 | 独立托管后端保存所有公开 scope 的成绩与归档；最多 100 个固定 Steam 榜槽只镜像当前服务端选中的 scope。Steam 运行时 handle 只缓存、不作为持久身份。 |

全局导航将 **挑战、词表库、社区、成就** 作为独立入口。词表库分组互斥：`My Lists` = 自有且未发布，`Community Downloads` = 原样下载的社区源，`My Published` = 自有且有发布绑定。收藏是横跨分组的独立视图/筛选，不复制同一条目。搜索、分组折叠、排序、收藏、语言/已游玩筛选及各自状态可本地记忆。星级排序与最佳用时只用于个人纪录展示/排序，不代表社区投票或公开榜单。

编辑他人下载内容产生一份自有词表：新建 UUID，清除下载/发布绑定，不强制保留来源或作者署名。未编辑的下载原版可展示远端作者信息，但下载源保持只读。社区投稿的元数据与本地收藏、路径、账号和个人成绩分离。

## 2. `WordList` 内容本体

### 2.1 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `schemaVersion` | 整数 | 文件形状/字段迁移版本。与哈希算法版本独立。初始草案值为 `1`。 |
| `localListId` | UUID 字符串 | 新建列表使用 128-bit UUID 的 32 位小写无连字符 `N` 格式。它只标识本地列表身份。 |
| `title` | 字符串 | 必填显示标题，不参与内容哈希。 |
| `titleZh` | 可选字符串 | 中文标题，不参与内容哈希。 |
| `description` | 可选字符串 | 简介，不参与内容哈希。 |
| `languageMode` | `English` / `Chinese` / `Mixed` | 显式模式标签；加载后仍需按阶段实际内容验证，不能只信标签。 |
| `phases` | 有序阶段数组 | 阶段顺序和重复阶段均有意义，不能排序、去重或扁平化。每个阶段包含有序 `segments`。 |
| `canonicalizationVersion` | 整数 | 定义 `canonicalGameplayContent` 的规范化/序列化规则；算法改变必须提高此版本。 |
| `contentHash` | 字符串 | `sha256:<64 位小写 hex>`，由规范化后的 gameplay content 计算。 |
| `createdAtUtc` | UTC 时间字符串 | 本地内容首次创建时间。 |
| `contentModifiedAtUtc` | UTC 时间字符串 | 仅当 gameplay content 的 canonical hash 改变时更新；改标题/简介不改此值。 |

阶段形状：

- `Phase.segments` 是有序数组；即使两个阶段或两个段文本相同，也分别保留。
- `Segment.type` 为 `english` 或 `chinese`。
- `english` 段保存原始显示/编辑文本 `text`。其参与输入的字符按游戏解析规则生成 canonical 文本；空格与不参与输入的标点不影响 hash，但分段边界保留。
- `chinese` 段保存有序 `entries`，每项有 `character` 与 `pinyin`。字符用于显示，拼音用于输入；二者及其顺序都属于内容语义。不能只存拼音扁平串。
- `languageMode` 必须与解析后的内容一致；中文/混合禁用时应拒绝内容，而不是加载后删词再保存。

这映射了项目现有模型：`MixedWordEntry.segments` 中英文段用 `text`、中文段用 `entries`; `ChinesePhaseEntry` 保存字符和拼音。`MixedPhaseParser` 生成输入 target 时会去除英文中不参与输入的空格/标点，并为每个中文字保留拼音边界。实现时应复用同一解析规则，避免第二套 parser 漂移。参见 [MixedWordEntry](../Assets/-Scripts/WordList/MixedWordEntry.cs#L4-L16)、[ChineseWordEntry](../Assets/-Scripts/WordList/ChineseWordEntry.cs#L4-L9)、[ChinesePhaseEntry](../Assets/-Scripts/WordList/ChinesePhaseEntry.cs#L3-L8)、[MixedPhaseParser](../Assets/-Scripts/WordList/MixedPhaseParser.cs#L100-L164)。

### 2.2 Canonical gameplay content 与哈希

`schemaVersion` 回答“怎样反序列化”；`canonicalizationVersion` 回答“哪些内容字节/语义参与比较”。二者不能复用一个版本号。草案哈希计算：

1. 按 `languageMode`、`phases[]`、`segments[]` 的现有顺序构造 `canonicalGameplayContent`。
2. 英文每个段独立按当前游戏规则筛出参与输入的字母/数字，再用 invariant lowercase；空格、标点等不参与输入的字符归一。不得跨段/跨阶段拼接后排序或去重。
3. 中文段保留 ordered `character` / `pinyin` pairs；拼音值按解析器接受的语义验证，不在序列化时丢字符或改成 flatten target。
4. 按字段固定顺序，以无 BOM UTF-8 的确定性紧凑 JSON 字节计算 SHA-256，保存为 `sha256:<lowercase-hex>`。
5. 标题、简介、作者、收藏、时间、文件路径、远端发布/下载 ID、votes、records 与“当前入选 Top 100”状态均不属于 `canonicalGameplayContent`，所以这些变化不改 `contentHash`。

如下示例的 canonical JSON bytes 为：

```json
{"languageMode":"Mixed","phases":[{"segments":[{"type":"english","text":"helloworld"}]},{"segments":[{"type":"english","text":"say"},{"type":"chinese","entries":[{"character":"你","pinyin":"ni"},{"character":"好","pinyin":"hao"}]},{"type":"english","text":"today"}]}]}
```

对应 SHA-256：`sha256:ec71b69febdfee474af4c2ae62861114b4e767d1e75664580c0d07c30670cf40`。实现须以实际游戏 parser/validator 定义规范化；若以后改变输入字符筛选、拼音解释、段边界或 JSON canonicalization，提升 `canonicalizationVersion`，不能静默沿用旧 hash。

### 2.3 完整 JSON 示例

纯英文最小列表：

```json
{
  "schemaVersion": 1,
  "localListId": "7db9e7d34ea84b2f9b7ce32171297a10",
  "title": "Everyday English",
  "languageMode": "English",
  "phases": [
    { "segments": [ { "type": "english", "text": "Hello" } ] }
  ],
  "canonicalizationVersion": 1,
  "contentHash": "sha256:79866fcceb34e418aaea97b6b4cab8961c828f234912c29e87ce12984240dd5a",
  "createdAtUtc": "2026-10-31T00:00:00Z",
  "contentModifiedAtUtc": "2026-10-31T00:00:00Z"
}
```

英语与中英混合阶段共存的列表：

```json
{
  "schemaVersion": 1,
  "localListId": "8a3c74e8f1b24370b8ca0be4d508a551",
  "title": "Everyday Mix",
  "titleZh": "日常混合",
  "description": "Short English and Chinese typing phases.",
  "languageMode": "Mixed",
  "phases": [
    { "segments": [ { "type": "english", "text": "Hello, World!" } ] },
    {
      "segments": [
        { "type": "english", "text": "Say" },
        {
          "type": "chinese",
          "entries": [
            { "character": "你", "pinyin": "ni" },
            { "character": "好", "pinyin": "hao" }
          ]
        },
        { "type": "english", "text": "Today" }
      ]
    }
  ],
  "canonicalizationVersion": 1,
  "contentHash": "sha256:ec71b69febdfee474af4c2ae62861114b4e767d1e75664580c0d07c30670cf40",
  "createdAtUtc": "2026-10-31T00:00:00Z",
  "contentModifiedAtUtc": "2026-10-31T00:05:00Z"
}
```

上例 hash 对应 canonical JSON 中 `Hello, World!` → `helloworld`、`Say` → `say`、`Today` → `today`。所有 UUID/Steam ID 示例均为格式示例，不代表真实玩家或发布条目。

## 3. 本地资料库：`LibraryState` / `LibraryEntry`

`LibraryState` 是本机索引，不随社区内容上传。每个 `localListId` 只保留一个 `LibraryEntry`；分组由 `ownership` 和 binding 推导，收藏是跨分组筛选/视图。

| 字段 | 类型 | 规则 |
|---|---|---|
| `LibraryState.schemaVersion` | 整数 | 资料库索引格式版本。 |
| `entries[]` | `LibraryEntry[]` | 一项对应一个本地 `WordList`。 |
| `LibraryEntry.localListId` | UUID 字符串 | 外键到本地列表身份。 |
| `ownership` | `owned` / `communityDownloaded` | `owned` 表示本机创建/另存的可编辑列表；`communityDownloaded` 表示原样下载源，按只读使用。 |
| `addedAtUtc` | UTC 时间字符串 | 加入本机库的时间。 |
| `lastPlayedAtUtc` | UTC 时间或 null | 最近开始游玩的时间，用于个人库筛选/显示；不进入 `WordList` 或社区载荷。 |
| `isFavorite` | 布尔值 | 独立于分组；收藏视图引用同一项，不复制记录。 |
| `downloadBinding` | 可选 `DownloadBinding` | 社区原版下载身份/版本。只有社区下载源使用。 |
| `publicationBinding` | 可选 `PublicationBinding` | 自有列表发布身份/状态。 |
| `LibraryState.legacyIdAliases[]` | 本地迁移映射 | 保留旧 TXT 头部 ID 与新 full UUID 的对应；不作为远端ID。 |
| `LibraryState.viewPreferences` | 本机 UI 状态 | 记住搜索/语言/已游玩筛选、排序、收藏视图和各组折叠状态。 |

互斥分组派生规则：

1. `ownership == communityDownloaded` → `Community Downloads`。
2. `ownership == owned` 且有有效 `publicationBinding` → `My Published`。
3. 其余 `ownership == owned` → `My Lists`。

收藏/搜索/语言/已游玩等筛选只投影同一批库项；不能把一个条目复制成“我的”和“收藏”两条。`bestStars`/`bestTime` 排序读取当前个人资料与当前内容作用域的 `PersonalRecord`，无对应纪录时置后。私人列表没有公开榜单作用域，仍可有本地个人纪录。

`viewPreferences` 可保存 `searchText`, `languageFilter`, `playedFilter`, `sortMode`, `favoritesOnly`, `lastGroup`, `collapsedGroups[]` 等；这些是 UI 偏好，不影响列表内容 hash。具体过滤枚举和空列表体验仍可由 UI 设计迭代。

`LibraryState.legacyIdAliases[]` 的候选映射形状为 `{ legacyLocalListId, localListId, status }`。`status` 可先使用 `verified` / `ambiguous`；若多个文件重用了同一旧 12-hex ID，先标为 `ambiguous`，不自动把一条旧纪录复制给多份新列表。

资料库示例（展示互斥分组；`favorite` 不形成额外条目）：

```json
{
  "schemaVersion": 1,
  "entries": [
    {
      "localListId": "7db9e7d34ea84b2f9b7ce32171297a10",
      "ownership": "owned",
      "addedAtUtc": "2026-10-31T00:00:00Z",
      "lastPlayedAtUtc": null,
      "isFavorite": true,
      "downloadBinding": null,
      "publicationBinding": null
    },
    {
      "localListId": "8a3c74e8f1b24370b8ca0be4d508a551",
      "ownership": "communityDownloaded",
      "addedAtUtc": "2026-10-31T00:10:00Z",
      "lastPlayedAtUtc": "2026-10-31T00:45:00Z",
      "isFavorite": false,
      "downloadBinding": {
        "provider": "steamWorkshop",
        "remoteItemId": "1234567890123456789",
        "downloadedContentHash": "sha256:ec71b69febdfee474af4c2ae62861114b4e767d1e75664580c0d07c30670cf40",
        "remoteUpdatedAtUtc": "2026-10-30T10:00:00Z",
        "remoteAuthorDisplayName": "Workshop Creator",
        "remoteAuthorPlatformId": "76561198000000000"
      },
      "publicationBinding": null
    },
    {
      "localListId": "54ef02d2caae4482b314c8de19e9f019",
      "ownership": "owned",
      "addedAtUtc": "2026-10-31T01:00:00Z",
      "lastPlayedAtUtc": null,
      "isFavorite": false,
      "downloadBinding": null,
      "publicationBinding": {
        "provider": "steamWorkshop",
        "remoteItemId": "9876543210123456789",
        "uploadedContentHash": "sha256:79866fcceb34e418aaea97b6b4cab8961c828f234912c29e87ce12984240dd5a",
        "state": "published",
        "lastPublishedAtUtc": "2026-10-31T01:05:00Z"
      }
    }
  ],
  "legacyIdAliases": [
    {
      "legacyLocalListId": "a12bc34d56ef",
      "localListId": "7db9e7d34ea84b2f9b7ce32171297a10",
      "status": "verified"
    }
  ],
  "viewPreferences": {
    "searchText": "",
    "languageFilter": "all",
    "playedFilter": "all",
    "sortMode": "bestTime",
    "favoritesOnly": false,
    "lastGroup": "communityDownloads",
    "collapsedGroups": ["myLists"]
  }
}
```

## 4. 个人纪录：`PersonalRecord`

个人纪录按复合作用域保存，不写入 `WordList`、Steam Workshop 内容载荷或榜单配置：

```text
(localPlayerProfileId,
 localListId,
 contentHash,
 canonicalizationVersion,
 rulesetId,
 modeId)
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `localPlayerProfileId` | UUID 字符串 | 本机资料身份；不等同 Steam ID。 |
| `localListId` | UUID 字符串 | 本地列表身份。 |
| `contentHash` | SHA-256 字符串 | 可玩内容指纹。 |
| `canonicalizationVersion` | 整数 | 计算该指纹的规范化版本。 |
| `rulesetId` | 字符串 | 时间/错误/星级规则标识。 |
| `modeId` | `solo` / `shared-coop` | 游玩模式作用域。 |
| `lastPlayedAtUtc` | UTC 时间/null | 此内容版本最近游玩时间。 |
| `attemptCount`, `completionCount` | 非负 int32 饱和计数 | 本地摘要计数，不保留无限历史。 |
| `bestTimeRun` | 可选对象 | `runId` UUID、`elapsedMs` int64、`errors`、`phaseCount`、`completedAtUtc`。 |
| `bestStarsRun` | 可选对象 | 单独的真实 `runId` UUID、`stars` 0–3、`errors`、`phaseCount`、`completedAtUtc`。 |

- `localPlayerProfileId` 是本地玩家资料身份，不是 `SteamID64`；只有后端确实需要时才使用平台账号标识。
- `modeId` 至少分开 `solo` 与 `shared-coop`，避免把不同游玩模式的成绩混排。
- `rulesetId` 标识时间/错误/星级规则；规则变化创建新作用域。
- 只保留必要摘要，不保存无限次run、每次按键或输入历史。`attemptCount` / `completionCount` 使用有上限的非负聚合计数；`lastPlayedAtUtc`只保留最近时间。
- `bestTimeRun` 和 `bestStarsRun` 是分别取得最佳时间/最佳星级的运行摘要，必须带各自真实 `runId`；两者可来自不同局，不能拼出不存在的“同一局成绩”。
- `elapsedMs` 是本地 int64 毫秒，不用浮点秒；旧 `TotalTime` float 秒仅作为 legacy 原记录保存/显示，不静默覆盖成新格式。
- 内容 hash 改变即产生新的个人纪录键，旧 hash 的纪录保留；玩家把内容改回原 hash 时可再次看到该版本纪录。只改标题/描述不会清空纪录，也不显示“内容改变”提示。

示例：

```json
{
  "localPlayerProfileId": "9edba113f2d1478da15de1fe13d3e533",
  "localListId": "8a3c74e8f1b24370b8ca0be4d508a551",
  "contentHash": "sha256:ec71b69febdfee474af4c2ae62861114b4e767d1e75664580c0d07c30670cf40",
  "canonicalizationVersion": 1,
  "rulesetId": "typing-stars-v1",
  "modeId": "solo",
  "lastPlayedAtUtc": "2026-10-31T00:45:00Z",
  "attemptCount": 7,
  "completionCount": 2,
  "bestTimeRun": {
    "runId": "b0cb169c32d24bdbb4895687ba809165",
    "elapsedMs": 31250,
    "errors": 7,
    "phaseCount": 2,
    "completedAtUtc": "2026-10-31T00:42:00Z"
  },
  "bestStarsRun": {
    "runId": "12037448e6724ed89c2673ee2cdb4fd1",
    "stars": 3,
    "errors": 0,
    "phaseCount": 2,
    "completedAtUtc": "2026-10-31T00:45:00Z"
  }
}
```

挑战解锁与账号级 lifetime 成就独立于个人词表纪录：编辑列表不会回滚已解锁挑战或已解锁的全局成就。它们不应为了适配词表 schema 被迁移成列表成绩。

## 5. 远端绑定与公开载荷

### 5.1 绑定结构

```json
{
  "downloadBinding": {
    "provider": "steamWorkshop",
    "remoteItemId": "1234567890123456789",
    "downloadedContentHash": "sha256:ec71b69febdfee474af4c2ae62861114b4e767d1e75664580c0d07c30670cf40",
    "remoteUpdatedAtUtc": "2026-10-30T10:00:00Z",
    "remoteAuthorDisplayName": "Workshop Creator",
    "remoteAuthorPlatformId": "76561198000000000"
  },
  "publicationBinding": null
}
```

- `remoteItemId`、`remoteAuthorPlatformId`、未来 `SteamID64` 等平台整数型ID都以 JSON 字符串保存，避免 JavaScript/通用 JSON 工具的 53-bit 精度损失。
- `PublicationBinding` 形状：`{ provider, remoteItemId, uploadedContentHash, state, lastPublishedAtUtc }`。状态可草拟为 `pending | published | needsMetadataUpdate | failed | archived`；上传版本必须与本地 hash 对照。可玩内容 hash 改变时不能沿用此 binding，而应为新本地列表创建新 binding。
- `DownloadBinding` 形状：`{ provider, remoteItemId, downloadedContentHash, remoteUpdatedAtUtc, remoteAuthorDisplayName?, remoteAuthorPlatformId? }`。读取下载完成回调给出的安装路径后，还需重新计算/验证本地内容 hash，不能信任远端 metadata 声称的 hash。
- 订阅原版只读。编辑副本成为 `owned` 新 UUID，并清掉 `downloadBinding` / 旧 `publicationBinding`；副本没有强制 source-author 字段。
- 发布上传只传公开词表字段。禁止打包收藏、个人纪录、账号ID、本地路径、编辑器状态或本机资料库条目。

### 5.2 Workshop 字段和本地身份分离

`PublishedFileId` 是远端社区条目身份；本地 UUID 是本地列表身份；`contentHash` 是某次可玩内容版本身份。三者不可互换，也不能通过文件名、显示标题或SteamID推断。Steam adapter 维护 `(localListId, provider) ↔ remoteItemId` 绑定；远端 id 不参与 canonical gameplay hash。

## 6. 公开排行榜统一数据方向（已确认；待实现）

### 6.1 作用域与权威后端

```json
{
  "publicListKey": "steamWorkshop:1234567890123456789",
  "contentHash": "sha256:ec71b69febdfee474af4c2ae62861114b4e767d1e75664580c0d07c30670cf40",
  "canonicalizationVersion": 1,
  "rulesetId": "typing-stars-v1",
  "modeId": "solo"
}
```

`scopeKey` 由 `publicListKey + contentHash + canonicalizationVersion + rulesetId + modeId` 唯一确定。`publicListKey` 只允许公开 scope：内置关卡用 `builtin:<stableChallengeId>`，Workshop 列表用 `steamWorkshop:<publishedFileId>`。尚未发布的私有词表默认只有本地 `PersonalRecord`，没有公开 scope；本地个人记录仍可覆盖所有本地列表，私人词表和私人记录不会为了公开榜自动上传。只有公开社区 scope 的成绩进入排行榜服务。

独立托管后端是全部公开 scope 的权威记录与归档来源，负责保留词表各内容版本/规则/模式的成绩历史，并按已选定的计分规则生成排名。它不依赖 Steam 榜单槽位保存历史。列表掉出 Top 100 后，后端归档其 scope；相同 `scopeKey` 重新入选时从已保存成绩重填，`contentHash` 或规则/模式不同则是新 scope，不继承旧 scope 的成绩。成绩规则、单分/最佳分与历史粒度仍待定；int32 Steam score 需定义可验证编码，不能把 `elapsedMs` 截断/缩放后假装无损。

客户端只持 Steam auth ticket，不持 Publisher key。客户端提交公开成绩时携带明确 `scopeKey` 与 `assignmentEpoch`；后端验证票据、scope 和当期映射后先写权威记录，再决定是否镜像 Steam。迟到的旧 epoch 结果不得转写到重新分配后的槽；若成绩本身仍被接受，只能保存到它明确指向的旧 scope，不镜像到当前新槽。Steam publisher key 仅留在受控后端。

`LeaderboardBinding` 描述后端 scope 关系，例如 `{ backendProvider, scopeKey, state }`；`state` 可用 `stored | selected | archived | unsupported`。Steam 侧另用固定槽映射，不把 Steam handle 或当前词表名当作 scope 身份：

```json
{
  "slotId": "slot-001",
  "steamLeaderboardId": "1234567",
  "scopeKey": "<canonical scope key or null>",
  "assignmentEpoch": 12,
  "state": "active"
}
```

`assignmentEpoch` 在某个固定槽每次换绑 scope 时单调递增。`steamLeaderboardId`/Steam 内部 leaderboard 名称固定对应这个槽；100 个槽预先建立，轮换只 Reset 并重填，不为每个词表/周期不断创建榜。

### 6.2 服务端 Top 100 选择与镜像

Top 100 由后端定时读取 Steam Workshop 投票指标并统一选出。当前方向是按 `votesUp` 倾向选择最多 100 个公开列表；准确查询字段/API、计分口径/平票规则、观察窗口和刷新周期仍待验证/定案。Workshop 投票决定哪些词表进入展示集合，不是玩家成绩榜的分数或名次。游戏客户端不自行实时查询 UGC 并另选一套 Top 100；游戏内榜单与 Steam 原生榜镜像共同使用同一个 `SelectionSnapshot`：

```json
{
  "selectionId": "selection-2026-11-01T000000Z",
  "effectiveAtUtc": "2026-11-01T00:00:00Z",
  "state": "published",
  "scopes": ["<scope key 1>", "<scope key 2>"],
  "slots": [
    {
      "slotId": "slot-001",
      "scopeKey": "<scope key 1>",
      "assignmentEpoch": 12,
      "steamLeaderboardId": "1234567"
    }
  ]
}
```

候选快照状态为 `building | refreshing | published | failed`。后端保留 `published` 版本，并在新选择重填 Steam 固定槽时记录 `refreshingSelectionId`、开始时间和进度；游戏内页面只读取最后一份完整 `published` 版本，不能显示一半新 scope、一半旧 scope。Steam 的 Reset 和逐玩家 Set 是非原子调用，外部 Steam Community 页面在刷新时仍可能短暂为空或只显示部分分数；后端要显示上次完成时间/refreshing 状态。Steam 镜像不能反向覆盖后端，也不能作为被挤出 scope 的唯一归档。

若选择镜像，100 个槽在 Steamworks 配置为 **Writes = Trusted**。后端为目标 scope 从自己的权威记录集构造完整镜像，调用 `ResetLeaderboard` 后逐 SteamID 使用 `SetLeaderboardScore`（`ForceUpdate`）重填。固定槽重复复用，不新增每周期 leaderboard。Steam 文档限制每个 app 最多 10,000 个 leaderboard、每位玩家每榜一个 entry；该方案用固定 100 个榜对象，不用一个共享榜的 details 冒充每词表独立排名。Steam Community 对外标题需要预先配置 `Community Name`；当前 WebAPI 没有动态更新此标题的接口，因此固定槽外部标题可能为通用名，不能承诺自动展示当前词表名。游戏内可以用同一发布的 scope/词表元数据动态显示准确标题。

服务商尚未选定。Cloudflare Workers + D1 是免维护常驻 VM 的 serverless 候选，需要首次配置/部署并管理密钥和免费额度；目前未部署，免费额度也不视为永久保证。个人最佳星级/时间仍是本地 `PersonalRecord`，不因公共榜架构变更而同步。

## 7. Legacy 兼容与迁移风险

当前实现格式不是此新 schema 的别名，不能无备份强制改写：

- `LevelWordListProvider` 在 `// u:` header 中生成截断的 12 位 hex ID；`EnsureUUID()`目前只赋值内存、没有立即写回。没有 ID 的 JSON 列表还会在 JSON load 分支先返回，之后 `GetListKey()`可能生成每次进程不同的临时 UUID。参见 [LevelWordListProvider](../Assets/-Scripts/WordList/LevelWordListProvider.cs#L56-L81)、[JSON load](../Assets/-Scripts/WordList/LevelWordListProvider.cs#L139-L215)。
- 当前 time key 为 challenge filename 或 custom short UUID，stars key 为 track prefix + challenge index；旧 time/star 数据没有历史 content hash/ruleset。不能把现在磁盘上的内容 hash 回填成“当时得分内容”的证明。
- 重新导入一个有 `// u:` header 的 TXT 会复制原文件并复用旧 ID；Duplicate 只写扁平 `words`，会丢结构化混合内容；TXT Export 也只输出 `GetWords()`。参见 [LevelPanelController import/export/duplicate](../Assets/-Scripts/UI/LevelPanelController.cs#L712-L874)、[Duplicate](../Assets/-Scripts/WordList/LevelWordListProvider.cs#L302-L331)。
- 迁移时先做可恢复备份，保留旧 ID 别名映射；检测重复/冲突 short IDs。不要把同一旧 time entry 静默复制到多个新 UUID，也不要将 legacy unversioned 成绩绑定到当前 `contentHash`。它们可以单独归档/展示为 unversioned，具体 UI 决策待定。
- 新导入或另存必须生成并持久化 full UUID；只有标题、简介、封面等元数据更新可以复用现有 ID。可玩内容改变时创建新本地 ID 和新 remote binding。社区下载在本地重新校验完整内容后才登记 binding。
- 首次迁移无法恢复旧内容时，不会强行清除历史记录；更改 title/description 不影响记录；编辑后新 hash 新作用域，改回旧 hash 可找回旧作用域。

## 8. 现有社区设计衔接与未决项

现有 Steam 社区 WIP 已提出 `PublishedFileId` 与本地 UUID 分离、订阅源只读、另存新 UUID、保留结构化 JSON 并验证下载内容。本文将内容 hash、个人成绩和公开榜 scope 单独定义；它不替代 Steam Workshop UI/API 接入设计。参见 [community-wordlists-wip.md](community-wordlists-wip.md#下载副本与身份) 及其中“必须先补证或修复的项目耦合”。

仍待确认：

1. Workshop `votesUp` 指标的准确 API/查询口径、平票处理、观察窗口和 Top 100 刷新节奏。
2. Steam 固定槽的 App Admin 配置与初始化；`Community Name` 使用通用标题；Reset/逐条 ForceUpdate 失败恢复和展示同步状态。
3. 后端记录保存的成绩粒度/保留周期，score 编码，星级/时间两个个人最佳是否分榜，以及公开榜是否只按最佳时间。
4. 用户发布前已有个人记录是否保持本地私有、未来是否允许主动补交；本轮不自动同步。
5. 下载原作者信息在社区详情中的显示字段；编辑副本不强制保留来源信息。
6. 旧 unversioned 时间和 index stars 的归档展示；遇到重复 12-hex ID 时的人工冲突处理。
7. 最终输入字符验证、文本长度/词条数/文件大小限制及所有 TXT/JSON 导入/导出共用的无损 schema 路径。

本文件仅定义候选数据模型；不修改 provider、存档、Steam、测试/Play 流程，也不表示代码已按此格式运行。
