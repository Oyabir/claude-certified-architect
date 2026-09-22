namespace PcSante.Core.Updates;

/// <summary>Résultat de la recherche d'une nouvelle version de PC Santé.</summary>
public sealed record AppUpdateInfo(bool Available, string CurrentVersion, string? NewVersion);
