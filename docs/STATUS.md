# 当前进度与续接说明

快照时间：2026-08-13

远端仓库：`git@github.com:piusohu98/MH.git`

## 1. 已完成节点

阶段 1 的可运行 MVP 已完成：

- `MH.slnx` 已包含 Core、Server、Client、Collector、Tests 五个项目。
- SDK 锁定为 .NET 10.0.101，启用 nullable、确定性构建和警告即错误。
- 建立区服、商品、快照、观察、事件、建议、仓位日志等领域模型。
- SQLite 建库时启用外键、WAL 和忙等待；日期按 UTC ISO-8601 字符串持久化。
- 确定性 DEMO 数据包含 1 个区服、24 个商品、180 天、每日 4 次快照，并带节日、供给变化、昼夜和 OCR 异常标记。
- API 已提供目录、日线、幂等快照上传、存活与就绪检查。
- Client 与 Collector 当前是可编译的 WPF 空壳。

## 2. 已验证结果

2026-08-13 本地验收：

```text
dotnet build MH.slnx -c Release --no-restore
结果：5/5 项目成功，0 warning，0 error

dotnet test MH.Tests\MH.Tests.csproj -c Release
结果：6 passed，0 failed，0 skipped
```

6 项测试覆盖：模拟数据确定性、首次建库与种子目录、合法目录/走势参数、非法参数 Problem Details、快照幂等上传、非 UTC 输入归一化。

节点 1 基线提交：`8baf88e feat: establish market data MVP`。

## 3. 尚未完成与已知欠账

- 当前使用 `EnsureCreated` 首次建库，尚未生成正式 EF Core Migration；升级数据库前必须补上。
- 日线聚合已存在，但 MAD、短中期指标、建议评分、回测和启用门槛尚未实现。
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

推荐从阶段 2 的最小闭环继续：

1. 为 MAD 和 7/30 日指标先写固定输入输出测试。
2. 在 `MH.Core` 实现分析器，明确时间截断参数，保证不读取未来数据。
3. 增加 SQLite 分析查询和 API；不先扩展 WPF。
4. 用模拟数据完成确定性滚动回测及数据不足门槛。
5. 分析 API 通过后再实现 WPF 总览和单商品走势图。

## 5. 可直接交给后续 Codex 任务的提示词

```text
继续 MH 项目阶段 2 的第一小节：只实现 MAD 异常过滤、7/30 日稳健指标和对应确定性测试。
先阅读 docs/PROJECT_PLAN.md、docs/DEVELOPMENT_PLAN.md、docs/STATUS.md。
不得修改 Collector/OCR/WPF，不得引入机器学习框架。
成功标准：无未来数据读取；MAD=0、样本不足、异常值和 UTC 边界测试通过；Release 全解法 0 warning/0 error。
```

## 6. 安全提醒

继续开发时仍须遵守：不自动交易、不绕过验证码、不抓包、不读内存、不修改游戏数据。没有真实截图和账号环境前，只能实现离线回放、观察模式和适配接口，不能宣称真实游戏自动采集已完成。
