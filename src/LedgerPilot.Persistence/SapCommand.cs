namespace LedgerPilot.Persistence;
public sealed record SapCommand(string Operation, string CommandKey, string ProjectId, decimal Net, string Currency, string StateHash, string? DocumentId = null)
{
    // Decimal scale does not change a command's financial meaning (10.0 == 10.00).
    public string CanonicalPayload() => System.Text.Json.JsonSerializer.Serialize(this with {
        Net = decimal.Parse(Net.ToString("G29", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture)
    });
    public void Validate()
    {
        if (Operation is not ("Create" or "Post" or "CreateAndPost") || string.IsNullOrWhiteSpace(CommandKey) || CommandKey.Length > 128 ||
            string.IsNullOrWhiteSpace(ProjectId) || Net < 0 || Currency is null || Currency.Length != 3 ||
            string.IsNullOrWhiteSpace(StateHash) || (Operation == "Post" && string.IsNullOrWhiteSpace(DocumentId)))
            throw new InvalidDataException("Invalid SAP command.");
    }
}
