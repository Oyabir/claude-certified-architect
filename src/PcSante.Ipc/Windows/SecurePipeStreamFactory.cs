using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using PcSante.Ipc.Security;

namespace PcSante.Ipc.Windows;

/// <summary>
/// Named pipe protégé par ACL (section 5) :
/// SYSTEM contrôle total ; utilisateurs authentifiés locaux lecture/écriture (pas de création d'instance) ;
/// accès réseau refusé ; première instance obligatoire pour empêcher l'usurpation du nom du pipe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecurePipeStreamFactory : IPipeStreamFactory
{
    public NamedPipeServerStream Create(string pipeName, int maxInstances, bool firstInstance)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AnonymousSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        var options = PipeOptions.Asynchronous | PipeOptions.WriteThrough;
        if (firstInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.InOut, maxInstances, PipeTransmissionMode.Byte, options,
            inBufferSize: 0, outBufferSize: 0, security);
    }
}
