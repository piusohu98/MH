param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [string]$CsvPath,

    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

function Get-NormalizedRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -ne $Path.Trim()) {
        throw "图片路径为空或包含首尾空白：$Path"
    }

    if ([System.IO.Path]::IsPathRooted($Path) -or $Path.Contains(':')) {
        throw "图片路径必须是相对路径：$Path"
    }

    $normalized = $Path.Replace('\', '/')
    $segments = $normalized.Split('/')
    if ($segments | Where-Object { $_.Length -eq 0 -or $_ -eq '.' -or $_ -eq '..' }) {
        throw "图片路径包含非法段：$Path"
    }

    return $normalized
}

function Get-LabelKind([string]$CandidateUse) {
    if ($CandidateUse -eq '正样本') {
        return 'positive'
    }

    if ($CandidateUse -eq '负样本') {
        return 'negative'
    }

    return 'auxiliary'
}

function Get-LabelRank([string]$LabelKind) {
    switch ($LabelKind) {
        'positive' { return 3 }
        'auxiliary' { return 2 }
        'negative' { return 1 }
        default { return 0 }
    }
}

function Get-LabelCompleteness([object]$Record) {
    $score = 0
    if (-not [string]::IsNullOrWhiteSpace($Record.VisibleItemText)) {
        $score++
    }

    if (-not [string]::IsNullOrWhiteSpace($Record.VisiblePriceText)) {
        $score++
    }

    if (-not [string]::IsNullOrWhiteSpace($Record.Notes)) {
        $score++
    }

    return $score
}

function Get-RequiredColumn([object]$Row, [string]$Name) {
    $property = $Row.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "审核 CSV 缺少列：$Name"
    }

    return [string]$property.Value
}

$rootPath = [System.IO.Path]::GetFullPath($Root)
if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
    throw "输入目录不存在：$rootPath"
}

if ([string]::IsNullOrWhiteSpace($CsvPath)) {
    $CsvPath = Join-Path $rootPath 'review-confirmed.csv'
}

$csvFullPath = [System.IO.Path]::GetFullPath($CsvPath)
if (-not (Test-Path -LiteralPath $csvFullPath -PathType Leaf)) {
    throw "找不到审核 CSV：$csvFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $rootPath 'ocr-dataset-v1.json'
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$rootPrefix = $rootPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $outputFullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "输出文件必须位于输入目录内：$outputFullPath"
}

$rows = @(Import-Csv -LiteralPath $csvFullPath -Encoding UTF8)
if ($rows.Count -eq 0) {
    throw "审核 CSV 没有数据。"
}

$requiredColumns = @('保留', '图片文件', '页面类型', '可见商品', '可见价格', '候选用途', '行情入库建议', '来源', '备注', 'SHA256')
foreach ($row in $rows) {
    foreach ($column in $requiredColumns) {
        [void](Get-RequiredColumn $row $column)
    }
}

$records = foreach ($row in $rows) {
    if ((Get-RequiredColumn $row '保留') -ne '是') {
        continue
    }

    $relativePath = Get-NormalizedRelativePath (Get-RequiredColumn $row '图片文件')
    $fullImagePath = [System.IO.Path]::GetFullPath((Join-Path $rootPath $relativePath))
    if (-not $fullImagePath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "图片路径越出输入目录：$relativePath"
    }

    if (-not (Test-Path -LiteralPath $fullImagePath -PathType Leaf)) {
        throw "图片文件不存在：$relativePath"
    }

    $extension = [System.IO.Path]::GetExtension($fullImagePath)
    if ($extension -notin @('.bmp', '.jpeg', '.jpg', '.png')) {
        throw "不支持的图片格式：$relativePath"
    }

    $actualHash = (Get-FileHash -LiteralPath $fullImagePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $declaredHash = (Get-RequiredColumn $row 'SHA256').Trim().ToUpperInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "图片 SHA256 不匹配：$relativePath"
    }

    $labelKind = Get-LabelKind (Get-RequiredColumn $row '候选用途')
    $recommended = (Get-RequiredColumn $row '行情入库建议') -eq '是'
    $pageType = Get-RequiredColumn $row '页面类型'
    $itemText = Get-RequiredColumn $row '可见商品'
    $priceText = Get-RequiredColumn $row '可见价格'
    if ($labelKind -eq 'positive' -and ($pageType -ne 'MarketList' -or -not $recommended -or [string]::IsNullOrWhiteSpace($itemText) -or [string]::IsNullOrWhiteSpace($priceText))) {
        throw "正样本必须是带商品名和价格的 MarketList：$relativePath"
    }

    [PSCustomObject]@{
        RelativeImagePath = $relativePath
        Sha256 = $actualHash
        PageType = $pageType
        LabelKind = $labelKind
        RecommendedForOcr = $recommended
        VisibleItemText = if ([string]::IsNullOrWhiteSpace($itemText)) { $null } else { $itemText }
        VisiblePriceText = if ([string]::IsNullOrWhiteSpace($priceText)) { $null } else { $priceText }
        Source = Get-RequiredColumn $row '来源'
        Notes = Get-RequiredColumn $row '备注'
    }
}

$duplicateGroups = @($records | Group-Object Sha256)
$samples = foreach ($group in ($duplicateGroups | Sort-Object Name)) {
    $groupRecords = @($group.Group | Sort-Object RelativeImagePath)
    $bestRank = ($groupRecords | ForEach-Object { Get-LabelRank $_.LabelKind } | Measure-Object -Maximum).Maximum
    $bestRecords = @($groupRecords | Where-Object { (Get-LabelRank $_.LabelKind) -eq $bestRank })
    $first = @($bestRecords | Sort-Object @{ Expression = { Get-LabelCompleteness $_ }; Descending = $true }, RelativeImagePath)[0]
    if ($first.LabelKind -eq 'positive') {
        foreach ($other in $bestRecords | Where-Object { $_.RelativeImagePath -ne $first.RelativeImagePath }) {
            $keys = @('PageType', 'LabelKind', 'RecommendedForOcr', 'VisibleItemText', 'VisiblePriceText')
            foreach ($key in $keys) {
                if ([string]$other.$key -ne [string]$first.$key) {
                    throw "相同 SHA256 的正样本标签发生冲突：$($first.RelativeImagePath) 与 $($other.RelativeImagePath)"
                }
            }
        }
    }

    [PSCustomObject]@{
        SampleId = "sample-$($first.Sha256.Substring(0, 16).ToLowerInvariant())"
        RelativeImagePath = $first.RelativeImagePath
        Sha256 = $first.Sha256
        PageType = $first.PageType
        LabelKind = $first.LabelKind
        RecommendedForOcr = $first.RecommendedForOcr
        VisibleItemText = $first.VisibleItemText
        VisiblePriceText = $first.VisiblePriceText
        Source = $first.Source
        Notes = $first.Notes
        DuplicateGroupId = $first.Sha256
    }
}

$manifest = [PSCustomObject]@{
    version = 'ocr-dataset-v1'
    datasetId = 'step1-web-candidates'
    sourceKind = 'public-web-debug-only'
    samples = @($samples | Sort-Object RelativeImagePath | ForEach-Object {
        [PSCustomObject]@{
            sampleId = $_.SampleId
            relativeImagePath = $_.RelativeImagePath
            sha256 = $_.Sha256
            pageType = $_.PageType
            labelKind = $_.LabelKind
            recommendedForOcr = $_.RecommendedForOcr
            visibleItemText = $_.VisibleItemText
            visiblePriceText = $_.VisiblePriceText
            source = $_.Source
            notes = $_.Notes
            duplicateGroupId = $_.DuplicateGroupId
        }
    })
}

$outputDirectory = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$json = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($outputFullPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

$positiveCount = @($samples | Where-Object LabelKind -eq 'positive').Count
$auxiliaryCount = @($samples | Where-Object LabelKind -eq 'auxiliary').Count
$negativeCount = @($samples | Where-Object LabelKind -eq 'negative').Count
Write-Host "已生成：$outputFullPath"
Write-Host "样本：$($samples.Count)（正样本 $positiveCount，辅助 $auxiliaryCount，负样本 $negativeCount）"
Write-Host "原始记录：$($rows.Count)，按 SHA256 去重后：$($samples.Count)"
