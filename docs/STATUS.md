# 当前进度与续接说明

快照时间：2026-08-14

远端仓库：`https://github.com/piusohu98/MH.git`

## 1. 已完成节点

阶段 1 的可运行 MVP，以及阶段 2 的分析指标、建议、回测、质量门禁、只读预览 API 和客户端数据层闭环已完成：

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
- `MH.Client` 已实现接口化只读 API 客户端，消费目录、走势、指标和建议端点；区服/商品路径段编码，查询时点统一为 UTC。
- 第一屏 ViewModel 已实现 `Idle/Loading/Ready/Offline/Error`、显式区服/商品/历史时点、最后成功时间、请求取消和请求代次；旧请求即使忽略取消并晚返回也不能覆盖新结果。
- 网络失败会保留最后成功快照并标记陈旧；在线数据年龄超过 48 小时也标记陈旧。只有 `Ready`、非陈旧且服务端允许时才显示可执行，否则候选买卖降为观察。
- 新增 5 类确定性规则验证行情：上涨缩量、下跌放量、短中期冲突、高波动和数据不足；测试通过真实分析器与建议规则验证预期安全行为，并覆盖未来极端行情隔离。
- 新增 `scripts/Validate-Demo.ps1` 和 `docs/VALIDATION.md`，可用独立 SQLite 一键验收真实 HTTP 数据闭环。
- Client 的窗口仍是 WPF 空壳，但只读客户端和第一屏状态逻辑已就绪；Collector 仍是可编译空壳。

## 2. 已验证结果

2026-08-14 客户端数据层节点本地验收：

```text
dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-build
结果：81 passed，0 failed，0 skipped
```

81 项测试覆盖：数据闭环、稳健指标、建议规则、5 类确定性虚拟行情、滚动回测、质量门禁、建议预览 API，以及客户端四端点、中文/空格 URI、UTC 归一化、非法选择、首次失败、离线保留、主动取消、请求竞态、48 小时陈旧边界和不可执行展示语义。

本节点由 Luna/max 子任务完成初版，主任务独立审查时发现并修复两项安全降级缺口：在线陈旧数据未标记，以及掉线后旧的可执行快照仍可能显示为可执行。收敛后由主任务独立重跑 Release 构建和全量测试，结果如上。

真实 HTTP 一键复验已通过：1 个 DEMO 区服、24 个商品、180 根日线、历史指标和建议预览均符合预期；本次固定时点输出 `CandidateBuy`、门禁 `ResearchOnly`、`isActionable=false`，证明研究状态不会被误标为可执行。

独立真实 HTTP 复验已确认：

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
```

## 3. 尚未完成与已知欠账

- 当前使用 `EnsureCreated` 首次建库，尚未生成正式 EF Core Migration；升级数据库前必须补上。
- 回测当前没有无歧义的完整平仓周期，因此未输出命中率；后续应先定义持仓批次和已实现盈亏口径。
- 可见供给只作为代理指标，尚未进入回测成交容量约束。
- 建议预览已接入只读 Server API 和客户端状态层，但尚无建议持久化或 WPF 可视化；当前仍是研究预览，不代表真实资金建议。
- 事件前/中/后比较与跨区标准化尚未实现。
- Client 尚未把现有数据层接入窗口，Collector 仍只有工程壳；没有行情可视化、OCR、热键或采集状态机。
- 尚无真实截图、OCR 模型、商品别名字典和标注集，不能评估真实识别率。
- 尚未实现自包含 Windows 发布和 CI。
- 区服人数和高消费玩家只能做代理指数，尚无校准数据，不能输出具体人数。

## 4. 回家后继续工作的最短路径

```powershell
git clone git@github.com:piusohu98/MH.git
cd MH
dotnet build MH.slnx -c Release
dotnet test MH.Tests\MH.Tests.csproj -c Release
dotnet run --project MH.Server\MH.Server.csproj
```

访问 API 前以服务启动日志显示的实际端口为准。

推荐从阶段 2 的下一个最小闭环继续：

1. 为 ViewModel 增加独立目录初始化入口，让窗口启动后先加载 DEMO 区服/商品，不依赖硬编码 ID；服务默认地址使用开发端口 `http://localhost:5002`，并允许环境变量覆盖。
2. 把现有只读客户端和 ViewModel 接入 `MainWindow`，实现区服/商品选择、显式历史时点和刷新；保持业务逻辑不进入 code-behind。
3. 第一屏只做总览、单商品轻量走势图和建议卡，必须展示门禁状态、规则/门禁版本、理由、数据年龄、离线/陈旧状态和“研究预览”提示。
4. 增加目录加载失败、空目录、选择联动和 UI 所需派生状态测试；不先实现 OCR/悬浮窗，也不引入重型 UI 框架。
5. 第一屏验收后再扩展活动日历、区服指标和个人仓位。

交接时应保留以下指标口径：

- `Return7Days/30Days`：MAD inlier 按 `StartUtc` 排序后的末值/首值减一。
- `Ewma7Days/30Days`：跨度分别为 7/30，`alpha = 2 / (span + 1)`，首个 inlier close 为初值。
- `Volatility7Days/30Days`：相邻可用 inlier close 简单收益率的样本标准差，不年化、不填补缺失日期。
- `VisibleSupplyChange7Days/30Days`：完整日线末 `Volume`/首 `Volume` 减一；至少 3 根且首量大于零。
- `DataAgeHours`：cutoff 与最近完整日线结束点之间的 decimal 小时数；不得受 7/30 日窗口影响。
- `Volume` 是按当前快照聚合得到的可见数量代理，受采集频率影响，不得描述成官方供给量或真实成交量。

## 5. 可直接交给后续 Codex 任务的提示词

```text
继续 MH 项目阶段 2 的下一小节：把已完成的只读 API 客户端和 FirstScreenViewModel 接入 WPF 第一屏。
先阅读 docs/PROJECT_PLAN.md、docs/DEVELOPMENT_PLAN.md、docs/STATUS.md。
只改 MH.Client 和 MH.Tests；不得修改 Server/Collector/OCR，不新增第三方 MVVM 或重型图表依赖。
先补目录初始化与空目录/失败测试，再将 MainWindow 绑定到 ViewModel；服务默认地址为 http://localhost:5002，并允许 MH_SERVER_BASE_URL 覆盖。第一屏实现区服/商品选择、显式历史时点、轻量走势图和建议卡，清晰展示 Loading/Offline/Error、门禁、版本、理由、数据年龄与研究提示。
成功标准：不硬编码目录 ID，离线或陈旧时不显示可执行买卖；Release 全解法 0 warning/0 error，全部测试通过。完成后停止，不进入 OCR/悬浮窗。
```

## 6. 安全提醒

继续开发时仍须遵守：不自动交易、不绕过验证码、不抓包、不读内存、不修改游戏数据。没有真实截图和账号环境前，只能实现离线回放、观察模式和适配接口，不能宣称真实游戏自动采集已完成。
