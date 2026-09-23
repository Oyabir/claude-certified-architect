using System.IO.Pipes;

namespace PcSante.Ipc.Security;

public sealed record ClientVerification(bool IsTrusted, ClientInfo? Client, TrustDecision Decision, string? ExecutablePath);

/// <summary>Vérifie le processus à l'autre bout du pipe AVANT de lire la moindre demande.</summary>
public interface IClientVerifier
{
    ClientVerification Verify(NamedPipeServerStream pipe);
}

/// <summary>Fournit les faits sur le processus client (implémentation Windows ou simulée).</summary>
public interface IClientProcessInspector
{
    ClientProcessFacts Inspect(NamedPipeServerStream pipe);
}

/// <summary>Vérificateur standard : inspection du processus + signature + politique.</summary>
public sealed class ClientVerifier(
    IClientProcessInspector inspector,
    Core.Windows.ISignatureVerifier signatures,
    ClientTrustPolicy policy,
    string serviceExecutablePath) : IClientVerifier
{
    private Core.Windows.SignatureInfo? _serviceSignature;

    public ClientVerification Verify(NamedPipeServerStream pipe)
    {
        var facts = inspector.Inspect(pipe);
        var clientSignature = string.IsNullOrEmpty(facts.ExecutablePath)
            ? Core.Windows.SignatureInfo.Unsigned
            : signatures.Verify(facts.ExecutablePath);
        _serviceSignature ??= signatures.Verify(serviceExecutablePath);

        var decision = policy.Evaluate(facts, clientSignature, _serviceSignature);
        return decision == TrustDecision.Trusted
            ? new ClientVerification(true, new ClientInfo(facts.ProcessId, facts.ExecutablePath!, facts.UserName ?? "?", facts.UserSid), decision, facts.ExecutablePath)
            : new ClientVerification(false, null, decision, facts.ExecutablePath);
    }
}
