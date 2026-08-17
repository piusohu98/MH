# 当前进度与续接说明

快照时间：2026-08-17

远端仓库：`https://github.com/piusohu98/MH.git`

## 1. 已完成节点

阶段 1 的可运行 MVP，以及阶段 2 的分析指标、建议、回测、质量门禁、只读预览 API、客户端数据层和 WPF 第一屏闭环已完成：

- `MH.slnx` 已包含 Core、Server、Client、Collector、Tests 五个项目。
- SDK 锁定为 .NET 10.0.101，启用 nullable、确定性构建和警告即错误。
- 建立区服、商品、快照、观察、事件、建议、仓位日志等领域模型。
- SQLite 建库时启用外键、WAL 和忙等待；日期按 UTC ISO-8601 字符串持久化。
- 确定性 DEMO 数据包含 1 个区服、24 个商品、180 天、每日 4 次快照，并带节日、供给变化、昼夜和 OCR 异常标记。
- API 已提供目录、日线、幂等快照上传、存活与就绪检查。
- 指标 API 已提供 7/30 日稳健中位数、MAD、样本数、inlier 数、收益率、EWMA、样本波动率、可见供给变化和数据年龄。
- 所有指标均以显式 `asOfUtc` 截断；未来快照和未完成日线不会进入历史结果，等价时区输入会归一化为相同 UTC 结果。
- 价格趋势只使用 MAD inlier；可见供给变化独立使用完整日线 `Volume`，价格异常日仍参与供给序列。
- 指标查询保留 30 天窗口；仅在窗口内没有完整日线时，以同市场窗口前最新一条历史观察作为数据年龄锚点，查询为倒序单行，不加载全部历史。
- `recommendation-rules-v1` 已输出动作、方向分数、置信度、规则版本、结构化理由、失效条件和交易后目标最大仓位；显式拒绝未来指标与陈旧/不足数据。
- 单商品目标最大仓位绝对上限为 25%；候选买入随看多证据增强而上升，候选卖出随看空证据增强而下降，高波动、冲突趋势和数据不足安全降级。
- 滚动回测只使用决策时点以前的完成日线，信号在下一根日线开盘执行；显式记录成本、滑点、权益、收益、最大回撤、换手和逐次决策/成交。
- 回测目标数量以执行开盘权益计算并纳入成本/滑点，已有持仓隔夜跳空后再平衡也不会因目标计算突破 25% 仓位上限；只做多且卖出信号不会开空仓。
- `backtest-quality-gate-v1` 使用至少 3 个互不重叠窗口评估覆盖期、决策/交易数、盈利窗口比例、中位数收益、最坏回撤、单窗尾部亏损和平均换手。
- 门禁输出 `ResearchOnly`、`Disabled` 或 `TrialEligible`；试用回撤线为 20%，灾难回撤线为 35%，单窗 -25% 或整体平均收益非正直接禁用，试用状态仍只允许小额人工监督。
- 建议预览 API 以必填 `asOfUtc` 为历史截点，只查询此前 151 天有界数据，返回当前建议、`isActionable`、三窗口回测门禁和固定研究假设，不持久化结果。
- API 固定使用 100000 初始资金、1% 交易成本和 0.5% 滑点；只有门禁通过且动作是候选买入/卖出时才可执行，研究/禁用状态强制不可执行。
- 新增活动事件研究后端：事件列表按 UTC 半开区间、类型、区服级/商品级范围过滤并稳定排序；单商品影响查询按 3–30 天窗口返回活动前/中/后请求窗口、有效观察窗口、完整性、可用性、原因、价格稳健统计和采集到的在售数量统计，`windowDays` 缺失或空白默认为 7，`asOfUtc` 与数据库查询均隔离未来数据。
- 活动影响价格统计先排除 OCR 异常日，再用 MAD 三倍规则和 MAD=0 零离差规则；在售数量独立使用完整日线 `Volume` 中位数，OCR 异常日仍参与数量统计。活动中/后的价格和在售数量变化分别与各自基线独立计算；任一基线或阶段指标缺失时，仅对应比较字段为 null 并返回对应原因，另一类比较仍可用。
- `MH.Client` 已实现接口化只读 API 客户端，消费目录、走势、指标和建议端点；区服/商品路径段编码，查询时点统一为 UTC。
- 第一屏 ViewModel 已实现 `Idle/Loading/Ready/Offline/Error`、显式区服/商品/历史时点、最后成功时间、请求取消和请求代次；旧请求即使忽略取消并晚返回也不能覆盖新结果。
- 网络失败会保留最后成功快照并标记陈旧；在线数据年龄超过 48 小时也标记陈旧。只有 `Ready`、非陈旧且服务端允许时才显示可执行，否则候选买卖降为观察。
- WPF 启动会加载 DEMO 目录，自动选择首个区服/商品，并用该商品最后一根完整日线的 `EndUtc` 作为默认历史时点，不硬编码目录 ID 或模拟日期。
- 第一屏已展示价格折线、OCR 异常点、7/30 日中位数/收益/波动/可见供给、数据年龄，以及建议动作、门禁、版本、理由、失效条件和研究提示。
- 根据人工反馈，第一屏已从量化面板改为游戏玩家语言：主区展示最近采集价、当日最低/最高、相对近 7 天常见价、近 7 天涨跌、价格稳定性、在售数量变化和囤货参考。
- 方向分数、置信百分比、回测门禁、技术版本及 30 日原始指标均收进“查看分析依据（进阶）”，默认不占据玩家决策视线。
- 价格图已标出最高/最低参考刻度、首尾日期、最新点、近 7 天常见价虚线和可能识别异常点；所有价格明确为模拟/采集样本，不宣称官方实时最低价。
- WPF 活动观察卡与走势图事件标记已接入：客户端只展示节日/供给变化，自动选择一个重点活动并显示活动前/中/后的常见价、在售数量、阶段状态和样本量；活动资料失败时仅复用同一 `server/item` 的上一份活动快照，跨市场或不同历史截点则置空并提示不可用；单日少于 3 个有效日线时显示样本不足。
- 跨事件历史归纳已接入：固定 `event-pattern-summary-v1`，按同一 `server/item/eventType` 聚合已结束活动，分别返回活动中/后的价格和在售数量中位变化、方向计数、一致度和样本门槛；归纳失败独立降级，不影响主行情，不输出买卖结论。
- 区服观察已接入：固定 `server-market-profile-v1`，以最近 7 天目录覆盖、采集日覆盖、价格变化和可见在售数量变化形成活跃度代理，并以动态 P75 高价值商品的在售收缩和价格保持形成高价值需求代理；输出 0–100 无量纲指数、低/中/高、置信度、样本数、数据年龄和不可用原因，超过 48 小时不评分。
- 区服代理端点为 `GET /api/v1/servers/{serverId}/market-profile?asOfUtc=...`；客户端“区服观察”独立降级，仅允许同区同一历史时点复用，固定声明不代表真实在线人数、成交量或高消费玩家人数。
- 窗口提供“重新加载目录”和“刷新行情”两个入口；空目录、无行情、首次断网、非法历史时点和较小窗口滚动均有明确降级。
- 新增 5 类确定性规则验证行情：上涨缩量、下跌放量、短中期冲突、高波动和数据不足；测试通过真实分析器与建议规则验证预期安全行为，并覆盖未来极端行情隔离。
- 新增 `scripts/Validate-Demo.ps1` 和 `docs/VALIDATION.md`，可用独立 SQLite 一键验收真实 HTTP 数据闭环。
- Client 第一屏已接通只读模拟行情；Collector 已增加阶段 3.0 本地截图目录回放入口，只读取 `manifest.json` 和目录内图片，不上传、不调用服务、不自动点击。
- 阶段 3.0 离线回放契约已固定为 `offline-replay-v1`：manifest/frame 元数据、图片路径安全校验、确定性排序、商品候选置信度和人工确认状态均有纯 Core 校验；低置信度、多候选或未确认结果不得静默接受。
- 阶段 3.1 已建立 `IOcrRecognizer` 本地注入边界和确定性 Fake recognizer：Fake 只复用 manifest 中已有文本/候选，不打开图片、不联网、不触发 UI 输入；recognizer 失败只拒绝当前帧，取消按原始 token 透传。
- 阶段 3.1 已接入 `CatalogCandidateMatcher` 和可选 manifest `catalog`：唯一精确名称匹配可接受，包含匹配、多候选、低置信度、无匹配和非法/空目录均显式复核或拒绝；未提供目录的旧 manifest 仍按原候选提示回放。
- 阶段 4 离线闭环首片已增加 `.offline-replay-checkpoint.json`：每帧完成后原子写入，checkpoint 绑定 manifest SHA-256 和连续确定性帧前缀；取消后只恢复剩余帧，manifest 变化或 sidecar 损坏时从头回放，完整成功后清理。
- Collector 已增加 `Reading/Recognizing/ReviewRequired/Completed/Failed` 进度事件和窗口取消入口；回放期间禁用目录切换/重复启动，状态栏显示当前帧，结束时明确需要人工复核的数量且不自动确认。
- `CollectorRunStateMachine` 已固定 `Idle/Observing/Capturing/Recognizing/ReviewRequired`、五类人工停止状态和 `Stopped` 的转移规则。非法/未知事件 fail-closed，`Stopped` 不能直接恢复；这只是状态契约，当前没有真实页面分类器或窗口捕获器。
- Collector 已增加 `.offline-replay-review.json` 人工复核决定 sidecar：只接受当前 `ReviewRequired` 帧已有候选，或明确拒绝；未处理不算成功，sidecar 绑定 manifest SHA-256、原子写入并在损坏/变化/未知帧时整体忽略。WPF 列表已提供候选选择、接受和拒绝动作，但决定仍只用于本地恢复和展示，不上传、不写入行情。
- Collector 已增加 `offline-replay-export-v1` 本地证据导出：绑定 manifest/review sidecar SHA-256，逐帧计算图片 SHA-256，区分自动接受、人工接受、人工拒绝和未处理；导出不猜价格/数量/区服，不调用 Server、不写入行情 SQLite。
- Client 已增加 `Ctrl+Alt+M` 全局热键和单实例缓存行情悬浮窗：只展示当前 `Snapshot.Series` 作用域的区服/商品、参考价、范围、采集截止、数据年龄和安全文案；无快照、离线或陈旧时 fail-closed，不读取屏幕、不截图、不调用 OCR。热键冲突会显示注册失败，退出时注销。

## 2. 已验证结果

2026-08-14 WPF 活动观察节点本地验收：

```text
dotnet restore MH.slnx
结果：成功

dotnet test MH.Tests\MH.Tests.csproj -c Release
结果：169 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

前一节点的 169 项测试覆盖：数据闭环、稳健指标、建议规则、5 类确定性虚拟行情、滚动回测、质量门禁、建议预览 API、活动事件前中后纯 Core 分析和事件 API，以及客户端四端点、事件列表/影响 API、目录初始化/重试、空目录、无日线、中文/空格 URI、UTC 归一化、非法输入、首次失败、离线保留、主动取消、请求竞态、活动失败降级、跨市场活动隔离、活动快照时点一致性、活动重点排序、中文玩家文案和图表事件区间/时间映射。

2026-08-17 跨事件归纳节点独立复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：188 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

新增测试覆盖跨事件中位数/方向计数/一致度、价格与在售数量独立样本门槛、未来活动和未来日线隔离、UTC 等价时点、归纳 API 参数边界、客户端归纳失败隔离、同市场不同历史截点不复用和请求竞态。

2026-08-17 跨区事件标准化节点独立复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：209 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

新增测试覆盖区服级等权中位数、P25/P75、单区安全降级、无符合条件活动的区服不计入样本、价格与在售数量独立缺失、未来活动/日线隔离、UTC 等价、跨区 API 参数边界、未知商品、客户端同作用域保留、不同历史截点隔离和请求竞态。Luna/max 子任务在收尾时遭遇服务限流，主任务接管后修正 UTC 测试夹具、降级期望和空区服样本口径，并独立完成定向测试、全量测试及 Release 构建。

2026-08-17 区服代理指标与阶段 2 收口复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ServerMarketProfile
结果：12 passed，0 failed，0 skipped

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：221 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

新增测试覆盖活跃度/高价值需求分数边界、至少 3 个高价值商品门槛、OCR 异常与在售数量独立处理、样本不足、48 小时陈旧停评分、未来观察隔离、UTC 等价、API 参数边界、未知区服、客户端同区同一时点保留和不同历史时点隔离。独立真实 HTTP 冒烟返回 DEMO 活跃度 98.5、高价值需求 50.5，并带样本、置信度、证据和范围声明。WPF 启动冒烟确认“区服观察”完整显示且不与原行情、活动观察和囤货参考重叠。

2026-08-17 阶段 3.0 离线截图回放首片复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OfflineReplayContractTests
结果：16 passed，0 failed，0 skipped

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：238 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

Collector Release 产物已完成无窗口服务冒烟：不存在的回放目录返回明确错误和 0 帧，不触发网络或数据库调用。另有 1 项独立 Migration 测试在临时空 SQLite 上执行 `MigrateAsync`，确认初始迁移记录及当前 8 张业务表均可用。阶段 3.0 仍未接入真实 OCR 模型，不宣称识别率或 P95 识别性能。

2026-08-17 阶段 3.1 OCR 边界、候选匹配与本地目录接线复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：264 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

本节点提交链为 `752210c`（OCR 契约与 Fake）、`181ffb6`（OCR 边界测试）、`b29cf22`（确定性商品候选匹配）、`0900fef`（Collector 本地目录接线）。定向测试覆盖 recognizer 请求字段、单帧失败隔离、取消透传、精确/包含/歧义/无匹配目录结果和旧 manifest 兼容。当前仍没有真实截图、中文模型、字典或标注集，不宣称真实识别率或 P95。

2026-08-17 阶段 4 checkpoint 首片复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：267 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

提交 `0bfde6b` 覆盖第二帧取消后仅恢复未完成帧、manifest 变化后全部重放、损坏 checkpoint 忽略和成功清理。checkpoint 只写入用户已选择且校验通过的回放目录，不上传、不调用服务、不执行 UI 输入。

2026-08-17 Collector 可取消进度与复核提醒复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：269 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

提交 `1f862e2` 新增读取/逐帧识别/需复核/完成/失败事件顺序测试，以及 Collector 的取消按钮和人工复核数量提示。取消继续通过原 token 透传，并复用 checkpoint 恢复能力。

2026-08-17 Collector 安全停止状态契约复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CollectorRunStateMachineTests
结果：22 passed，0 failed，0 skipped

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：291 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

提交 `f4978aa` 固定正常识别、人工复核、登录/更新/验证码/掉线/未知页面停止、显式停止/重置和非法事件语义；提交 `f025cb7` 阻止旧回放或已取消回放的排队 progress 回调覆盖当前 UI。离线错误仍保持离线错误，未映射成任何真实页面状态。

2026-08-17 Collector 离线人工复核决定节点复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CollectorReplayReviewTests
结果：4 passed，0 failed，0 skipped

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：295 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

新增 `OfflineReplayReviewDecision` 与 `.offline-replay-review.json` sidecar，覆盖接受现有候选、明确拒绝、未处理/非法候选、manifest 变化、损坏 JSON、未知帧和临时文件清理。复核结果不调用 Server API、不上传图片、不自动写入行情；真实中文 OCR、页面检测和端到端导入仍未完成。

2026-08-17 Collector 离线证据导出节点复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CollectorOfflineReplayExportTests
结果：3 passed，0 failed，0 skipped

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：298 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

新增 `OfflineReplayExportDocument`、`OfflineReplayExportService` 和导出夹具，验证固定 `exportedAtUtc` 下字节稳定、帧顺序、image hash、复核决定分类、损坏 sidecar、manifest 变化和原始文件不变。导出只产生本地证据 JSON，不证明真实 OCR、页面检测或行情写入。

2026-08-17 Client 缓存行情悬浮窗节点复验：

```text
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MarketOverlayTests
结果：8 passed，0 failed，0 skipped

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-restore
结果：306 passed，0 failed，0 skipped

dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error
```

新增 `GlobalHotkeyService`、`MarketOverlayProjection` 和单实例 `MarketOverlayWindow`，测试覆盖安全状态解析、Ctrl+Alt+M 修饰键、负坐标工作区位置和无快照文案。未进行多显示器/DPI 人工目视验收，不宣称屏幕识别、OCR P95 或真实游戏兼容性。

客户端节点由 Luna/max 子任务完成初版，主任务独立审查时发现并修复两项安全降级缺口：在线陈旧数据未标记，以及掉线后旧的可执行快照仍可能显示为可执行。

活动事件研究节点同样由 Luna/max 完成初版；主任务审查发现并修复了缺省 `windowDays` 未按约定使用 7、价格与在售数量基线被错误绑定，以及未来观察测试落在事件总窗口之外三个缺口。修复后由主任务独立重跑 Release 构建、全量测试和真实 HTTP 冒烟，结果如上。

WPF 活动观察节点完成客户端事件列表/影响 API、节日/供给变化过滤、重点活动单次影响加载、活动卡中文事实展示、主行情失败隔离、离线活动快照保留、请求竞态保护，以及走势图淡色事件区间标记。该节点已由用户完成界面人工验收；单日少于 3 个有效日线明确显示样本不足。

WPF 第一屏主审查又补充了初始化重试、输入禁用、左右滚动、状态颜色和服务地址根路径约束。无交互启动冒烟已确认 `MH.Client.exe` 可打开标题为“MH 市场监控”的窗口并持续运行；由于本机 Codex 窗口截图组件权限失败，DPI、字体和长文本的最终目视验收仍需人工完成。

玩家导向重排后再次完成无交互启动冒烟，窗口标题和进程正常；本轮只改变展示解释和本地绘图，不改变 Core 指标、Server API 或建议规则。

真实 HTTP 一键复验已通过：1 个 DEMO 区服、24 个商品、180 根日线、历史指标和建议预览均符合预期；本次固定时点输出 `CandidateBuy`、门禁 `ResearchOnly`、`isActionable=false`，证明研究状态不会被误标为可执行。

独立真实 HTTP 复验已确认：

- 新事件端点在临时独立 SQLite 上返回 9 个 DEMO 供给变化事件；省略 `windowDays` 时影响查询使用 7 天，`demo-supply-007` 的活动前/中/后三阶段均为 `Available`，服务进程和临时数据库已在复验后清理。
- 固定数据返回 `visibleSupplyChange7Days=3`、`visibleSupplyChange30Days=7`、`dataAgeHours=12.5`，JSON 为 camelCase 数值字段。
- 窗口外最近完整日线可返回精确 `dataAgeHours=1068.5`，且不会污染 7/30 日价格、样本或供给指标。
- 当前日不完整数据、cutoff 之后的未来快照不会替代历史完整日线或改变历史响应。
- 历史补查 SQL 使用同市场过滤、`ORDER BY ObservedAtUtc DESC LIMIT 1`。

阶段 1 基线提交：`8baf88e feat: establish market data MVP`。

阶段 2 分析指标提交链：

```text
2ef9669 feat(analytics): add robust market indicators
e782880 fix(analytics): make robust median nullable
bdcdd1c fix(server): restore data layer sources
ddd2000 fix(server): configure SQLite connection pragmas
d07e9e0 feat(server): expose robust market indicators
1821ab6 fix(server): bound market indicators observation query
7bd39f2 feat(analytics): add trend and volatility metrics
fdf6d96 feat(analytics): add supply freshness indicators
636d544 fix(api): anchor data age outside indicator window
7200121 feat(analytics): add explainable recommendation rules
eb70e9b feat(backtest): add deterministic rolling simulation
cdde94a feat(backtest): add multi-window quality gate
ee4bef1 feat(api): add recommendation research preview
7dbdee5 test(simulation): add deterministic validation scenarios
bcba8bb feat(client): add safe market data view model
ff6fb41 feat(client): add market dashboard first screen
daf218a feat(client): focus dashboard on game market players
```

## 3. 尚未完成与已知欠账

- 已生成首个正式 EF Core Migration 工件和 Design 依赖，但运行时仍使用 `EnsureCreated` 以兼容没有 `__EFMigrationsHistory` 的既有数据库；在补完 schema 校验和 baseline 策略前，不得直接切换到 `MigrateAsync`。
- 回测当前没有无歧义的完整平仓周期，因此未输出命中率；后续应先定义持仓批次和已实现盈亏口径。
- 可见供给只作为代理指标，尚未进入回测成交容量约束。
- 建议预览已接入只读 Server API 和 WPF 第一屏，但尚无建议持久化；当前仍是研究预览，不代表真实资金建议。
- 跨区事件标准化已完成最小闭环：`cross-server-event-standardization-v1` 先在每个区按活动前稳健中位价计算相对变化，再以区服级中位数等权汇总跨区中位数、P25/P75、方向计数和一致度；价格与在售数量独立处理，少于 2 个可比较区服返回样本不足。当前 DEMO 只有 1 个区服，未伪造第二个区服或真实跨区结论。
- Client 第一屏和缓存行情悬浮窗仍需人工检查 DPI、字体、长文本及多显示器行为；Collector 已完成离线 OCR 边界、目录匹配、人工复核、证据导出和采集状态转移契约，但尚无真实中文 OCR、屏幕识别或真实页面检测。
- 尚无真实截图、OCR 模型、商品别名字典和标注集，不能评估真实识别率或 OCR P95。
- 尚未实现自包含 Windows 发布和 CI。
- 区服人数和高消费玩家只能做代理指数，尚无校准数据，不能输出具体人数。
- 用户已明确取消跨商品建议榜和个人仓位，不再作为未完成项；现有单商品建议和目标最大仓位口径保持不变。

## 4. 回家后继续工作的最短路径

```powershell
git clone git@github.com:piusohu98/MH.git
cd MH
dotnet build MH.slnx -c Release
dotnet test MH.Tests\MH.Tests.csproj -c Release
dotnet run --project MH.Server\MH.Server.csproj
# 另开一个 PowerShell 窗口
dotnet run --project MH.Client\MH.Client.csproj
```

客户端默认连接 `http://localhost:5002/`；若服务使用其他根地址，先设置 `MH_SERVER_BASE_URL`。第一屏人工验收清单：

1. 启动后应自动出现区服、商品、当前参考价、当日高低、折线图、玩家速览和囤货参考，不再是空白窗口。
2. 切换商品并点击“刷新行情”，参考价、价格高低、相对常见价、折线和囤货参考应更新，界面不应冻结。
3. 输入非法历史时点时，输入框下方应出现红色提示且“刷新行情”禁用；改回合法 ISO-8601 UTC 时间后恢复。
4. 暂停服务后再次刷新，旧图表应保留，状态变为离线/陈旧，候选买卖必须降为不可执行；恢复服务后可再次刷新。
5. 展开“查看分析依据（进阶）”，确认技术指标不会默认抢占主界面；缩小窗口后左右两列都应可滚动，重点检查高 DPI、中文字体和长理由是否截断。

阶段 2 已正式关闭，阶段 3.0 离线图片/目录回放、阶段 3.1 的 OCR 边界、确定性 Fake、商品候选匹配、人工复核 sidecar、证据导出和缓存行情悬浮窗已完成。下一步只有在中文模型、字典、标注夹具和页面捕获边界可核验时才接入真实 RapidOcrNet/屏幕识别适配器；不宣称真实识别率或 OCR P95。

交接时应保留以下指标口径：

- `Return7Days/30Days`：MAD inlier 按 `StartUtc` 排序后的末值/首值减一。
- `Ewma7Days/30Days`：跨度分别为 7/30，`alpha = 2 / (span + 1)`，首个 inlier close 为初值。
- `Volatility7Days/30Days`：相邻可用 inlier close 简单收益率的样本标准差，不年化、不填补缺失日期。
- `VisibleSupplyChange7Days/30Days`：完整日线末 `Volume`/首 `Volume` 减一；至少 3 根且首量大于零。
- `DataAgeHours`：cutoff 与最近完整日线结束点之间的 decimal 小时数；不得受 7/30 日窗口影响。
- `Volume` 是按当前快照聚合得到的可见数量代理，受采集频率影响，不得描述成官方供给量或真实成交量。

## 5. 可直接交给后续 Codex 任务的提示词

```text
继续 MH 项目阶段 3.1：阶段 2 行情面板、活动研究、跨区标准化、区服代理指标和阶段 3.0 离线截图回放已经完成；当前已完成本地 OCR 注入边界、确定性 Fake、商品候选匹配和可选 manifest 目录接线。
先阅读 docs/PROJECT_PLAN.md、docs/DEVELOPMENT_PLAN.md、docs/STATUS.md、docs/OFFLINE_REPLAY.md。
下一步仅在 RapidOcrNet 中文模型、字典和标注夹具被明确核验后实现真实适配器；否则继续离线状态机和人工复核通知。checkpoint 恢复已完成首片。截图不得上传；没有真实截图和标注集时不能宣称真实识别率或 P95。
```

## 6. 安全提醒

继续开发时仍须遵守：不自动交易、不绕过验证码、不抓包、不读内存、不修改游戏数据。没有真实截图和账号环境前，只能实现离线回放、观察模式和适配接口，不能宣称真实游戏自动采集已完成。
