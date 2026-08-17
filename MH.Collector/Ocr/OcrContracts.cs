namespace MH.Collector.Ocr;

/// <summary>
/// Local OCR boundary used by the collector. Implementations must not upload
/// images, call the market server, or trigger UI input.
/// </summary>
// RapidOcrNet 4.0.2 was verified, but its bundled model is Latin-only. A
// future adapter can map DetectAsync into this boundary after the Chinese
// model files and an accuracy fixture are explicitly verified.
public interface IOcrRecognizer
{
    ValueTask<OcrRecognitionResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OcrRecognitionRequest(
    string FrameId,
    string ImagePath,
    string? RawTextHint,
    IReadOnlyList<OcrRecognitionCandidate> CandidateHints);

public sealed record OcrRecognitionCandidate(
    string ItemId,
    string? DisplayName,
    decimal Confidence,
    bool IsConfirmed);

public sealed record OcrTextLine(string Text, decimal Confidence);

public sealed record OcrRecognitionResult(
    OcrRecognitionStatus Status,
    string? RawText,
    IReadOnlyList<OcrTextLine> Lines,
    IReadOnlyList<OcrRecognitionCandidate> Candidates,
    string? Error = null);

public enum OcrRecognitionStatus
{
    Completed = 0,
    Failed = 1
}
