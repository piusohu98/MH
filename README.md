# MH 市场监控工具

梦幻西游手游交易行的本地数据分析工具原型。本项目只处理模拟数据、用户主动提供的截图或后续合规采集数据，不包含自动交易、验证码绕过、抓包、内存读取或游戏数据修改。

## 项目文档

- [项目总计划](docs/PROJECT_PLAN.md)：目标、边界、指标定义、五阶段路线和完成标准。
- [开发方案](docs/DEVELOPMENT_PLAN.md)：技术架构、数据流、算法方案、测试门禁和 Git 工作方式。
- [当前进度与续接说明](docs/STATUS.md)：已完成内容、验证结果、已知欠账和下一步操作。
- [虚拟数据与验证指南](docs/VALIDATION.md)：独立 SQLite、自动化测试和真实 HTTP 一键验收。
- [离线截图回放格式](docs/OFFLINE_REPLAY.md)：阶段 3.1 本地回放目录、manifest 字段、商品目录匹配和安全规则。

## 当前节点（阶段 2 已完成，阶段 3 进行中）

已完成：

- .NET 10 解法与 `MH.Core`、`MH.Server`、`MH.Client`、`MH.Collector`、`MH.Tests` 五个项目。
- SQLite 数据模型、首次建库、WAL/外键配置和确定性 DEMO 种子数据。
- 24 个模拟商品、180 天、每日 4 个快照，包含节日、供给变化、昼夜和可标记 OCR 异常。
- 基础 API：目录、商品日线、幂等快照上传、存活/就绪健康检查。
- 指标 API：MAD 过滤、7/30 日稳健中位数、收益率、EWMA、波动率、可见供给变化和数据年龄。
- 纯 Core 可解释建议规则：方向分数、置信度、理由、失效条件、规则版本和不超过 25% 的目标最大仓位。
- 纯 Core 确定性滚动回测：历史截断、下一日开盘执行、成本/滑点、收益、回撤、换手和逐次记录。
- 多窗口回测质量门禁：区分仅研究、禁用和小额人工监督试用，单个赚钱窗口不能直接启用规则。
- 只读建议预览 API：按显式历史时点返回建议、可执行标记、三窗口回测门禁和固定研究假设。
- 活动事件研究后端：查询区服级/商品级活动，并按显式历史时点比较活动前、中、后的常见价格和采集到的在售数量；未发生时段和样本不足会明确返回不可用原因。
- WPF 客户端第一屏已按游戏玩家重排：突出最近采集价、当日高低、相对近期常见价、价格方向、稳定性、在售数量变化和囤货参考；技术指标收进进阶折叠区。采集器已具备阶段 3.1 本地截图目录回放、可注入 OCR 边界、确定性 Fake recognizer、可选本地商品目录匹配、取消后 checkpoint 恢复和逐帧进度/取消入口，并已固定安全停止状态契约；仍未接入真实中文 OCR 或页面检测。
- WPF 活动观察卡与走势图事件标记已接入：只展示节日/供给变化，自动选择重点活动，显示活动前/中/后的事实比较、阶段状态和样本量；单日少于 3 个有效日线时明确显示样本不足。
- 跨事件历史归纳与跨区标准化已接入：跨区使用 `cross-server-event-standardization-v1`，每个区先按活动变化中位数形成区服级结果，再对各区等权汇总价格与在售数量的中位变化、P25/P75 范围、方向计数和一致度；少于 2 个可比较区服时只显示样本不足，不输出买卖结论。DEMO 仍只有 1 个区服，不伪造跨区数据。
- 区服观察已接入 `server-market-profile-v1`：最近 7 天的目录/采集日覆盖、价格变化频率和可见在售数量变化频率形成无量纲活跃度指数；动态高价分位商品的在售数量收缩及价格保持形成高价值需求代理。数据超过 48 小时或样本不足时不评分，结果不代表真实人数或成交量。
- 291 项自动化测试覆盖数据闭环、稳健指标、定向虚拟场景、滚动回测、质量门禁、建议预览 API、活动影响研究、跨事件归纳、跨区标准化、区服代理指标、离线回放契约、OCR 边界、商品候选匹配、Collector 本地回放/checkpoint/进度状态/停止状态转移、初始数据库 Migration，以及客户端初始化、安全降级、事件 API、玩家文案、快照时点隔离和请求竞态。

## 运行与验证

需要 Windows 和 .NET SDK 10.0.101（`global.json` 已锁定）。

```powershell
dotnet build MH.slnx -c Release
dotnet test MH.Tests\MH.Tests.csproj -c Release
dotnet run --project MH.Server\MH.Server.csproj
# 另开一个 PowerShell 窗口
dotnet run --project MH.Client\MH.Client.csproj
```

服务默认使用 `%LOCALAPPDATA%\MHMarket\data\market.db`，可通过配置键 `Database:Path` 改写。启动后可访问：

- `GET /api/v1/catalog`
- `GET /api/v1/markets/demo-server-01/demo-item-01/series`
- `GET /api/v1/markets/demo-server-01/demo-item-01/indicators`
- `GET /api/v1/markets/demo-server-01/demo-item-01/recommendation?asOfUtc=2025-06-30T00:00:00Z`
- `GET /api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2025-01-01T00:00:00Z&toUtc=2025-06-30T00:00:00Z&type=Holiday`
- `GET /api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00Z&windowDays=7`
- `GET /api/v1/items/demo-item-01/events/cross-server-summary?type=Holiday&asOfUtc=2025-06-30T00:00:00Z`
- `GET /api/v1/servers/demo-server-01/market-profile?asOfUtc=2025-06-30T00:00:00Z`
- `POST /api/v1/snapshots`
- `GET /health/live`
- `GET /health/ready`

## 后续计划

1. OCR 与悬浮查价：获得并校验中文模型、字典和标注夹具后接入本地 RapidOcrNet 适配器，再推进 `Ctrl+Alt+M`、透明置顶窗和多显示器/DPI。
2. 采集器离线闭环：checkpoint、逐帧进度、取消、需复核提醒和停止状态契约已完成；下一步在真实页面检测边界可用后产生登录/更新/验证码/掉线/未知页面事件。
3. 分析层：可见供给成交容量约束、完整平仓周期和命中率口径。
4. 真实适配预留：截图规范、标注格式、观察模式和窗口捕获说明；没有真实截图前不编造坐标。

Windows 采集器的登录、更新、验证码、掉线和未知页面自动处理尚未实现。下一阶段先按人工处理：检测到这些状态时停止操作并通知用户，绝不尝试绕过验证码。
