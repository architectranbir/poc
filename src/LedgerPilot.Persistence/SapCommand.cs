namespace LedgerPilot.Persistence;
public sealed record SapCommand(string Operation, string CommandKey, string ProjectId, decimal Net, string Currency, string StateHash, string? DocumentId = null)
{
    public void Validate()
    {
        if (Operation is not ("Create" or "Post") || string.IsNullOrWhiteSpace(CommandKey) || CommandKey.Length > 128 ||
            string.IsNullOrWhiteSpace(ProjectId) || Net < 0 || Currency is null || Currency.Length != 3 ||
            string.IsNullOrWhiteSpace(StateHash) || (Operation == "Post" && string.IsNullOrWhiteSpace(DocumentId)))
            throw new InvalidDataException("Invalid SAP command.");
    }
}
