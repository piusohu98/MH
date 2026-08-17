# 离线截图回放格式

阶段 3.1 的 Collector 只读取用户选择目录中的 `manifest.json` 和本地图片。它不上传截图、不调用市场服务，也不执行游戏操作。manifest 可以携带一个仅用于离线匹配的本地 `catalog`；目录缺省时保持阶段 3.0 的候选提示回放行为。

## 目录结构

```text
my-replay/
  manifest.json
  frames/
    frame-001.png
    frame-002.jpg
```

## manifest.json

```json
{
  "version": "offline-replay-v1",
  "replayId": "demo-replay-001",
  "createdAtUtc": "2026-08-17T12:00:00Z",
  "catalog": [
    {
      "id": "demo-item-01",
      "name": "示例商品"
    }
  ],
  "frames": [
    {
      "frameId": "frame-001",
      "relativeImagePath": "frames/frame-001.png",
      "capturedAtUtc": "2026-08-17T12:00:01Z",
      "rawText": "示例商品",
      "candidates": [
        {
          "itemId": "demo-item-01",
          "displayName": "示例商品",
          "confidence": 0.95,
          "isConfirmed": true
        }
      ]
    }
  ]
}
```

固定规则：

- `version` 必须是 `offline-replay-v1`。
- `replayId`、`frameId` 必须非空且不能包含路径分隔符；ID 按大小写不敏感判重。
- `createdAtUtc`、`capturedAtUtc` 必须使用非空 UTC 时间，即以 `Z` 表示的时间。
- `relativeImagePath` 必须是目录内的规范相对路径，拒绝绝对路径、盘符、UNC 路径、`.`、`..` 和空路径段。
- 当前只接受 `.png`、`.jpg`、`.jpeg` 和 `.bmp`。
- `catalog` 是可选的本地商品名称目录；每项至少提供 `id` 和 `name`。显式空目录或非法目录项会 fail-closed，不会静默使用 frame 的候选提示。
- 提供 `catalog` 时，`rawText` 先经过 Unicode 归一化后匹配商品名称：唯一精确匹配使用 `1.00` 置信度并可自动接受；包含匹配使用 `0.80` 置信度，默认低于 `0.85` 进入人工复核；多候选和无匹配不会静默选择。
- 单一候选只有在 `isConfirmed=true` 且 `confidence >= 0.85` 时才标记为成功。
- 低置信度、未确认或多候选进入“需人工复核”；无候选或非法元数据进入“拒绝”。

未提供 `catalog` 时，`rawText` 和 `candidates` 用于离线调试与契约验证；提供 `catalog` 时，recognizer 的文本输出由目录匹配器产生候选。当前默认 recognizer 是确定性 Fake：只复用 manifest 中已有的文本和候选，不打开图片、不联网、不代表真实图片识别。RapidOcrNet 中文模型、真实截图、标注集和 P95 性能夹具尚未接入，因此当前节点没有真实识别率或 P95 性能结论。

## 运行

```powershell
dotnet run --project MH.Collector\MH.Collector.csproj
```

启动后选择包含 `manifest.json` 的目录。列表会按捕获时间、图片路径和帧 ID 确定性排序，并显示成功、需人工复核和拒绝数量。

回放每完成一帧，会在同一目录原子更新 `.offline-replay-checkpoint.json`。checkpoint 固定绑定当前 `manifest.json` 的 SHA-256，并只接受与确定性排序结果一致的连续已完成帧；取消或进程中断后重新选择同一目录时只处理剩余帧。manifest 内容变化、checkpoint 损坏、版本不匹配或帧元数据不一致时会忽略旧 checkpoint 并从头回放。完整成功后 sidecar 会被删除。
