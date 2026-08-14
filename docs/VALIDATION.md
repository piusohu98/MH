# 虚拟数据与验证指南

本指南验证的是当前已完成的数据闭环：确定性虚拟行情、SQLite、HTTP API、稳健指标、建议规则和回测门禁。WPF 目前仍是工程壳，因此本节点不包含界面验收。

## 虚拟数据口径

项目包含两类完全可复现的数据：

- `DemoGenerator`：固定种子生成 1 个虚拟区服、24 个商品、180 天行情、每天 4 次快照，同时包含节日、供给变化、昼夜与 OCR 异常标记。服务连接空数据库时会自动写入。
- `RecommendationValidationScenarios`：为规则验证生成上涨缩量、下跌放量、趋势冲突、高波动和数据不足等定向场景。它只用于自动化测试，不会写入用户数据库。

这些数据只验证技术与规则行为，不代表真实《梦幻西游》区服、商品名称、人数或交易结论。

## 1. 自动化验证

在仓库根目录执行：

```powershell
dotnet build MH.slnx -c Release
dotnet test MH.Tests\MH.Tests.csproj -c Release --no-build
```

预期结果：5 个项目构建成功、0 warning、0 error，全部测试通过。虚拟场景测试还应证明：

- 上涨且可见供给收缩时产生候选买入信号；
- 下跌且可见供给扩张时产生候选卖出信号；
- 趋势冲突、高波动和数据不足时安全降级；
- 在历史截止时间之后追加极端价格，不改变当时的指标和建议；
- DEMO 数据相同种子每次生成完全一致。

## 2. 使用独立 SQLite 启动 API

打开第一个 PowerShell 窗口，在仓库根目录执行：

```powershell
$env:Database__Path = "$PWD\data\validation-market.db"
dotnet run --project MH.Server\MH.Server.csproj --urls http://localhost:5002
```

指定独立文件是为了不影响日后采集数据。首次启动会自动创建数据库并写入确定性 DEMO 行情；再次启动不会重复写入。

若希望从全新的数据库验证，可换一个新的文件名，例如 `validation-market-2.db`，无需删除现有文件。

## 3. 一键验证真实 HTTP

保持服务运行，打开第二个 PowerShell 窗口，在仓库根目录执行：

```powershell
.\scripts\Validate-Demo.ps1 -BaseUrl http://localhost:5002
```

脚本只执行只读请求，并检查：

1. SQLite 就绪；
2. 目录含 1 个虚拟区服和 24 个商品；
3. 指定商品返回 180 根有序日线；
4. `2025-06-30T00:00:00Z` 截止时点可计算稳健指标；
5. 建议规则和回测门禁版本正确，未通过门禁时绝不会标记为可执行。

成功时最后一行是：

```text
全部 DEMO 验证通过。
```

## 4. 手工查看 API

服务运行时可在浏览器打开：

```text
http://localhost:5002/api/v1/catalog?kind=demo
http://localhost:5002/api/v1/markets/demo-server-01/demo-item-01/series
http://localhost:5002/api/v1/markets/demo-server-01/demo-item-01/indicators?asOfUtc=2025-06-30T00:00:00Z
http://localhost:5002/api/v1/markets/demo-server-01/demo-item-01/recommendation?asOfUtc=2025-06-30T00:00:00Z
```

重点查看建议响应中的 `decision.action`、`decision.reasons`、`qualityGate.status`、`qualityGate.reasons` 和 `isActionable`。当前所有输出都是“只读研究预览”，不能作为真实资金承诺或自动交易指令。

## 常见问题

- 连接失败：确认第一个窗口仍在运行，且脚本端口与 `--urls` 一致。
- 端口占用：把两个命令中的 `5002` 同时改成其他空闲端口。
- 数据不是 180 天：确认使用的是新的独立数据库，或改用另一个验证文件名。
- PowerShell 禁止脚本：可仅对本次进程执行 `Set-ExecutionPolicy -Scope Process Bypass`，然后重新运行验证脚本。
