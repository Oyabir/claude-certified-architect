using PcSante.Core.Commands;

namespace PcSante.Core.Optimization;

/// <summary>Action annulable (bouton « Annuler »).</summary>
public sealed record UndoView(Guid Id, CommandId Command, DateTimeOffset CreatedAt, string Description);
