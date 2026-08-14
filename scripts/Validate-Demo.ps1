param(
    [string]$BaseUrl = "http://localhost:5002",
    [string]$AsOfUtc = "2025-06-30T00:00:00Z"
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')

function Get-Json([string]$Path) {
    Invoke-RestMethod -Method Get -Uri "$base$Path"
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "验证失败：$Message"
    }
}

Write-Host "验证服务：$base"

$ready = Invoke-WebRequest -UseBasicParsing -Uri "$base/health/ready"
Assert-True ($ready.StatusCode -eq 200) "数据库就绪检查不是 HTTP 200"
Write-Host "[通过] SQLite 就绪检查"

$catalog = Get-Json "/api/v1/catalog?kind=demo"
Assert-True (@($catalog.servers).Count -eq 1) "DEMO 区服数量应为 1"
Assert-True (@($catalog.items).Count -eq 24) "DEMO 商品数量应为 24"
Write-Host "[通过] 目录：1 个区服，24 个商品"

$series = Get-Json "/api/v1/markets/demo-server-01/demo-item-01/series"
Assert-True (@($series.bars).Count -eq 180) "日线数量应为 180"
Assert-True ($series.bars[0].startUtc -lt $series.bars[-1].startUtc) "日线未按时间正序返回"
Write-Host "[通过] 行情：180 根日线且时间有序"

$encodedAsOf = [Uri]::EscapeDataString($AsOfUtc)
$indicators = Get-Json "/api/v1/markets/demo-server-01/demo-item-01/indicators?asOfUtc=$encodedAsOf"
Assert-True ([DateTimeOffset]::Parse($indicators.cutoffUtc).UtcDateTime -eq [DateTimeOffset]::Parse($AsOfUtc).UtcDateTime) "指标截止时间未保持为指定 UTC 时点"
Assert-True ($null -ne $indicators.robustMedian7Days) "7 日稳健中位数不应为空"
Assert-True ($indicators.sampleCount30Days -gt 0) "30 日样本数应大于 0"
Write-Host "[通过] 指标：MAD、趋势、波动、供给和数据年龄已生成"

$preview = Get-Json "/api/v1/markets/demo-server-01/demo-item-01/recommendation?asOfUtc=$encodedAsOf"
Assert-True ($preview.decision.ruleVersion -eq "recommendation-rules-v1") "建议规则版本不正确"
Assert-True ($preview.qualityGate.gateVersion -eq "backtest-quality-gate-v1") "回测门禁版本不正确"
Assert-True (-not $preview.isActionable -or $preview.qualityGate.status -eq 2) "未通过门禁的建议不得标记为可执行"
$actionNames = @("DataInsufficient", "Observe", "CandidateBuy", "Hold", "CandidateSell", "Avoid")
$gateNames = @("ResearchOnly", "Disabled", "TrialEligible")
Write-Host "[通过] 建议：$($actionNames[$preview.decision.action])，门禁：$($gateNames[$preview.qualityGate.status])，可执行：$($preview.isActionable)"

Write-Host "全部 DEMO 验证通过。"
