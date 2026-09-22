using System.IO.Pipes;

namespace PcSante.Ipc.Security;

/// <summary>Crée les instances serveur du named pipe (avec ACL sous Windows).</summary>
public interface IPipeStreamFactory
{
    NamedPipeServerStream Create(string pipeName, int maxInstances, bool firstInstance);
}

/// <summary>Pipe sans ACL spécifique : uniquement pour les tests et les plateformes non Windows.</summary>
public sealed class UnsecuredPipeStreamFactory : IPipeStreamFactory
{
    public NamedPipeServerStream Create(string pipeName, int maxInstances, bool firstInstance) =>
        new(pipeName, PipeDirection.InOut, maxInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None));
}
