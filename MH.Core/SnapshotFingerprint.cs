using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MH.Core.Contracts;

namespace MH.Core;

public static class SnapshotFingerprint
{
    public static string Compute(SnapshotUploadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        builder.Append(request.ServerId?.Trim()).Append('\n');
        builder.Append(request.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(request.Source?.Trim()).Append('\n');

        foreach (var observation in (request.Observations ?? []).OrderBy(x => x.ItemId, StringComparer.Ordinal))
        {
            builder.Append(observation.ItemId?.Trim()).Append('|');
            builder.Append(observation.Price.ToString(CultureInfo.InvariantCulture)).Append('|');
            builder.Append(observation.Quantity.ToString(CultureInfo.InvariantCulture)).Append('|');
            builder.Append(observation.ObservedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|');
            builder.Append(observation.IsOcrAnomaly ? '1' : '0').Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
