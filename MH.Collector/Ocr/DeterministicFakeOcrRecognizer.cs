namespace MH.Collector.Ocr;

/// <summary>
/// Replays OCR-shaped values already present in an offline manifest.
/// It performs no image I/O, so its output is a pipeline canary, not an
/// OCR accuracy measurement.
/// </summary>
public sealed class DeterministicFakeOcrRecognizer : IOcrRecognizer
{
    public ValueTask<OcrRecognitionResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FrameId)
            || string.IsNullOrWhiteSpace(request.ImagePath))
        {
            return ValueTask.FromResult(new OcrRecognitionResult(
                OcrRecognitionStatus.Failed,
                null,
                [],
                [],
                "OCR 输入缺少帧标识或图片路径。"));
        }

        var candidates = (request.CandidateHints ?? [])
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.ItemId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.DisplayName ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
        var lines = string.IsNullOrWhiteSpace(request.RawTextHint)
            ? Array.Empty<OcrTextLine>()
            : [new OcrTextLine(request.RawTextHint, candidates.Length == 0 ? 0m : candidates.Average(candidate => candidate.Confidence))];

        return ValueTask.FromResult(new OcrRecognitionResult(
            OcrRecognitionStatus.Completed,
            request.RawTextHint,
            lines,
            candidates));
    }
}
