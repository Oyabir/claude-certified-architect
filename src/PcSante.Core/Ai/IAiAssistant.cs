namespace PcSante.Core.Ai;

/// <summary>Langue d'explication de l'assistant (M10).</summary>
public enum AssistantLanguage
{
    French,
    Darija,
    English,
}

/// <summary>Proposition de l'assistant : jamais exécutée sans validation de l'utilisateur.</summary>
public sealed record AssistantSuggestion(string Explanation, Commands.CommandId? ProposedCommand);

/// <summary>
/// Abstraction de l'assistant IA (M10). Hors MVP (section 10) : aucune implémentation n'est livrée.
/// Contrat : envoi minimal de données (codes d'erreur, métriques), consentement explicite préalable,
/// aucune action automatique décidée par l'IA.
/// </summary>
public interface IAiAssistant
{
    bool IsAvailable { get; }

    Task<AssistantSuggestion> ExplainIssueAsync(string issueCode, AssistantLanguage language, CancellationToken cancellationToken);

    Task<AssistantSuggestion> AnalyzeCrashAsync(string bugCheckCode, AssistantLanguage language, CancellationToken cancellationToken);
}
