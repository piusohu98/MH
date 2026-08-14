# 当前进度与续接说明

快照时间：2026-08-14

远端仓库：`https://github.com/piusohu98/MH.git`

## 1. 已完成节点

阶段 1 的可运行 MVP，以及阶段 2 的分析指标、纯 Core 建议规则、确定性滚动回测和质量门禁闭环已完成：

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
- Client 与 Collector 当前是可编译的 WPF 空壳。

## 2. 已验证结果

2026-08-14 回测质量门禁节点本地验收：

```text
dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error

dotnet test MH.Tests\MH.Tests.csproj -c Release --no-build
结果：59 passed，0 failed，0 skipped
```

59 项测试覆盖：模拟数据与建库、SQLite 连接 pragma、目录/走势/上传 API、UTC 归一化、稳健指标、建议规则、滚动回测边界，以及质量门禁的空/不足、版本冲突、窗口冲突、整体亏损、回撤分级、单窗尾部亏损、高换手、确定性和完全通过场景。

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
```

## 3. 尚未完成与已知欠账

- 当前使用 `EnsureCreated` 首次建库，尚未生成正式 EF Core Migration；升级数据库前必须补上。
- 回测当前没有无歧义的完整平仓周期，因此未输出命中率；后续应先定义持仓批次和已实现盈亏口径。
- 可见供给只作为代理指标，尚未进入回测成交容量约束。
- 建议、回测和门禁尚未接入 Server API、数据库或 WPF；当前仅是可测试的纯 Core v1 基线。
- 事件前/中/后比较与跨区标准化尚未实现。
- WPF 两个程序只有工程壳，没有行情 UI、OCR、热键或采集状态机。
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

1. 增加只读建议预览 API，复用现有指标、建议、回测和质量门禁，不先持久化结果。
2. API 必须显式接收并归一化 `asOfUtc`，只查询该时点以前的数据；返回建议、规则版本、门禁状态、门禁理由和回测摘要。
3. `ResearchOnly/Disabled` 时必须返回 `isActionable=false`，不能把候选动作描述成已启用资金建议。
4. 为模拟行情固定交易成本/滑点与多窗口划分，增加非法参数、数据不足、未来隔离和确定性集成测试。
5. 建议 API 通过后实现 WPF 总览和单商品走势图。

交接时应保留以下指标口径：

- `Return7Days/30Days`：MAD inlier 按 `StartUtc` 排序后的末值/首值减一。
- `Ewma7Days/30Days`：跨度分别为 7/30，`alpha = 2 / (span + 1)`，首个 inlier close 为初值。
- `Volatility7Days/30Days`：相邻可用 inlier close 简单收益率的样本标准差，不年化、不填补缺失日期。
- `VisibleSupplyChange7Days/30Days`：完整日线末 `Volume`/首 `Volume` 减一；至少 3 根且首量大于零。
- `DataAgeHours`：cutoff 与最近完整日线结束点之间的 decimal 小时数；不得受 7/30 日窗口影响。
- `Volume` 是按当前快照聚合得到的可见数量代理，受采集频率影响，不得描述成官方供给量或真实成交量。

## 5. 可直接交给后续 Codex 任务的提示词

```text
继续 MH 项目阶段 2 的下一小节：实现只读建议预览 API 与集成测试。
先阅读 docs/PROJECT_PLAN.md、docs/DEVELOPMENT_PLAN.md、docs/STATUS.md。
复用现有 RobustMarketAnalyzer、RecommendationRule、RollingBacktest 和 BacktestQualityGate；不得修改 Collector/OCR/WPF，不得引入机器学习框架，不先持久化建议结果。
建议使用 `GET /api/v1/markets/{serverId}/{itemId}/recommendation?asOfUtc=...`；响应包含原始建议、`isActionable`、门禁状态/版本/理由和回测摘要。
成功标准：只读取 asOfUtc 以前数据；ResearchOnly/Disabled 时 isActionable=false；非法参数、数据不足、未来隔离、等价 UTC 和相同输入确定性测试通过；Release 全解法 0 warning/0 error，全部测试通过。
```

## 6. 安全提醒

继续开发时仍须遵守：不自动交易、不绕过验证码、不抓包、不读内存、不修改游戏数据。没有真实截图和账号环境前，只能实现离线回放、观察模式和适配接口，不能宣称真实游戏自动采集已完成。
