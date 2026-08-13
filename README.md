# MH 市场监控工具

梦幻西游手游交易行的本地数据分析工具原型。本项目只处理模拟数据、用户主动提供的截图或后续合规采集数据，不包含自动交易、验证码绕过、抓包、内存读取或游戏数据修改。

## 当前节点（节点 1：工程与数据闭环）

已完成：

- .NET 10 解法与 `MH.Core`、`MH.Server`、`MH.Client`、`MH.Collector`、`MH.Tests` 五个项目。
- SQLite 数据模型、首次建库、WAL/外键配置和确定性 DEMO 种子数据。
- 24 个模拟商品、180 天、每日 4 个快照，包含节日、供给变化、昼夜和可标记 OCR 异常。
- 基础 API：目录、商品日线、幂等快照上传、存活/就绪健康检查。
- WPF 客户端与采集器的可编译空壳，为后续节点保留入口。
- 6 项自动化测试，覆盖模拟器、建库、API 参数、幂等性及 UTC 处理。

## 运行与验证

需要 Windows 和 .NET SDK 10.0.101（`global.json` 已锁定）。

```powershell
dotnet build MH.slnx -c Release
dotnet test MH.Tests\MH.Tests.csproj -c Release
dotnet run --project MH.Server\MH.Server.csproj
```

服务默认使用 `%LOCALAPPDATA%\MHMarket\data\market.db`，可通过配置键 `Database:Path` 改写。启动后可访问：

- `GET /api/v1/catalog`
- `GET /api/v1/markets/demo-server-01/demo-item-01/series`
- `POST /api/v1/snapshots`
- `GET /health/live`
- `GET /health/ready`

## 后续计划（当前暂停）

1. 分析层：MAD 异常过滤、短中期指标、建议规则、确定性回测与无未来数据泄漏验证。
2. WPF 行情面板：总览、走势图、建议榜、活动日历、区服指标和个人仓位，并实现离线降级。
3. OCR 与悬浮查价：本地 RapidOcrNet、低置信度显式提示、`Ctrl+Alt+M`、多显示器/DPI。
4. 采集器离线闭环：截图目录/录制帧回放、状态机、断点恢复和人工处理通知。
5. 真实适配预留：截图规范、标注格式、观察模式和窗口捕获说明；没有真实截图前不编造坐标。

Windows 采集器的登录、更新、验证码、掉线和未知页面自动处理尚未实现。下一阶段先按人工处理：检测到这些状态时停止操作并通知用户，绝不尝试绕过验证码。
