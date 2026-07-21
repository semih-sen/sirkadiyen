using System.Security.Cryptography;
using System.Text.Json;
using Sirkadiyen.Contracts.Serialization;
using Sirkadiyen.Contracts.Spreadsheets;

namespace Sirkadiyen.Infrastructure.ScheduleIngestion;

public static class SnapshotContentHasher
{
    public const string Algorithm = "SHA-256";

    public static string Compute(
        IReadOnlyList<NormalizedWorksheet> worksheets,
        IReadOnlyList<AcquisitionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(worksheets);
        ArgumentNullException.ThrowIfNull(diagnostics);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new SnapshotHashPayload(SpreadsheetContractVersions.V1, worksheets, diagnostics),
            ContractJson.CreateOptions());
        byte[] digest = SHA256.HashData(payload);

        return $"sha256:{Convert.ToHexStringLower(digest)}";
    }

    private sealed record SnapshotHashPayload(
        string ContractVersion,
        IReadOnlyList<NormalizedWorksheet> Worksheets,
        IReadOnlyList<AcquisitionDiagnostic> Diagnostics);
}
