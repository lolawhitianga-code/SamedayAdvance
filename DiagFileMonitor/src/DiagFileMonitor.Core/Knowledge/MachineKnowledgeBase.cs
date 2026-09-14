namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// The machine models we have written knowledge for. Add a model by writing a builder like
/// <see cref="RakedWallExtruderKnowledge"/> and listing it in <see cref="All"/>.
/// </summary>
public static class MachineKnowledgeBase
{
    private static readonly Lazy<IReadOnlyList<MachineKnowledge>> Entries = new(() => new[]
    {
        RakedWallExtruderKnowledge.Build()
    });

    public static IReadOnlyList<MachineKnowledge> All => Entries.Value;

    /// <summary>The knowledge for a model, or null where we have none written down yet.</summary>
    public static MachineKnowledge? Find(string? machineModel) =>
        string.IsNullOrWhiteSpace(machineModel)
            ? null
            : All.FirstOrDefault(k => k.Matches(machineModel));
}
