using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using PcSante.Core.Commands;
using PcSante.Ipc.Security;

namespace PcSante.Ipc.Tests;

internal sealed class FixedVerifier(bool trusted) : IClientVerifier
{
    public ClientVerification Verify(NamedPipeServerStream pipe) => trusted
        ? new ClientVerification(true, new ClientInfo(1, "/opt/PcSante/PcSante.exe", "alice", null), TrustDecision.Trusted, null)
        : new ClientVerification(false, null, TrustDecision.Unsigned, "/tmp/evil.exe");
}

internal sealed class EchoHandler : IRequestHandler
{
    public List<IpcRequest> Received { get; } = [];

    public Task<CommandResult> HandleAsync(IpcRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        Received.Add(request);
        return Task.FromResult(CommandResult.Success("Result_Echo", request.Command, client.UserName));
    }
}

internal sealed class RejectServer : IServerVerifier
{
    public bool IsGenuineService(NamedPipeClientStream pipe) => false;
}

public sealed class PipeEndToEndTests : IAsyncDisposable
{
    private readonly string _pipeName = "pcsante-test-" + Guid.NewGuid().ToString("N")[..12];
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    private EchoHandler Start(bool trusted)
    {
        var handler = new EchoHandler();
        var server = new PipeServer(_pipeName, new UnsecuredPipeStreamFactory(), new FixedVerifier(trusted), handler, NullLogger<PipeServer>.Instance);
        _serverTask = server.RunAsync(_cts.Token);
        return handler;
    }

    [Fact]
    public async Task Client_de_confiance_dialogue_avec_le_service()
    {
        var handler = Start(trusted: true);
        await using var client = new PipeClient(_pipeName, new AcceptAnyServer());

        var r1 = await client.SendAsync(CommandId.RunHealthAnalysis);
        var r2 = await client.SendAsync(CommandId.StopProcess, new Dictionary<string, string> { ["pid"] = "5" }, confirmed: true);

        r1.MessageKey.Should().Be("Result_Echo");
        r1.MessageArgs.Should().Equal("RunHealthAnalysis", "alice");
        r2.MessageArgs[0].Should().Be("StopProcess");
        handler.Received.Should().HaveCount(2);
        handler.Received[1].Confirmed.Should().BeTrue();
        handler.Received[1].Parameters!["pid"].Should().Be("5");
    }

    [Fact]
    public async Task Client_non_signe_refuse_par_le_named_pipe()
    {
        var handler = Start(trusted: false);
        await using var client = new PipeClient(_pipeName, new AcceptAnyServer());

        var result = await client.SendAsync(CommandId.RunHealthAnalysis);

        result.Status.Should().Be(CommandStatus.Refused);
        result.Reason.Should().Be(FailureReason.ClientNotTrusted);
        handler.Received.Should().BeEmpty("aucune demande d'un client non vérifié n'est traitée");
    }

    [Fact]
    public async Task Client_refuse_deconnecte_sans_bloquer_le_service()
    {
        var handler = Start(trusted: false);

        await using (var raw = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await raw.ConnectAsync(3000);
            await raw.WriteAsync(BitConverter.GetBytes(int.MaxValue));
            await raw.FlushAsync();
            (await raw.ReadAsync(new byte[4])).Should().Be(0, "une demande trop volumineuse d'un client refusé ferme la connexion");
        }

        // Plusieurs refus successifs n'épuisent pas les connexions du service.
        for (var i = 0; i < 6; i++)
        {
            await using var client = new PipeClient(_pipeName, new AcceptAnyServer());
            (await client.SendAsync(CommandId.RunHealthAnalysis)).Reason.Should().Be(FailureReason.ClientNotTrusted);
        }

        handler.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task Version_de_protocole_inconnue_refusee()
    {
        var handler = Start(trusted: true);
        await using var client = new PipeClient(_pipeName, new AcceptAnyServer());

        var result = await client.SendRawAsync(new IpcRequest { RequestId = "1", Command = "RunHealthAnalysis", Version = 99 });

        result.MessageKey.Should().Be("Result_InvalidRequest");
        handler.Received.Should().BeEmpty();
        (await client.SendRawAsync(new IpcRequest { RequestId = new string('x', 100), Command = "RunHealthAnalysis" }))
            .MessageKey.Should().Be("Result_InvalidRequest");
    }

    [Fact]
    public async Task Message_geant_ou_mal_forme_ferme_la_connexion()
    {
        var handler = Start(trusted: true);

        await using (var raw = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await raw.ConnectAsync(3000);
            await raw.WriteAsync(BitConverter.GetBytes(int.MaxValue));
            await raw.FlushAsync();
            var buffer = new byte[4];
            (await raw.ReadAsync(buffer)).Should().Be(0, "le service coupe la connexion");
        }

        await using (var raw = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await raw.ConnectAsync(3000);
            var junk = "{ pas du json"u8.ToArray();
            await raw.WriteAsync(BitConverter.GetBytes(junk.Length));
            await raw.WriteAsync(junk);
            await raw.FlushAsync();
            (await raw.ReadAsync(new byte[4])).Should().Be(0);
        }

        handler.Received.Should().BeEmpty();

        // Le service reste disponible pour les clients légitimes.
        await using var client = new PipeClient(_pipeName, new AcceptAnyServer());
        (await client.SendAsync(CommandId.GetLiveMetrics)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Service_absent_resultat_clair()
    {
        await using var client = new PipeClient("pcsante-absent-" + Guid.NewGuid().ToString("N")[..8], new AcceptAnyServer(), TimeSpan.FromMilliseconds(200));

        var result = await client.SendAsync(CommandId.RunHealthAnalysis);

        result.Reason.Should().Be(FailureReason.ServiceUnavailable);
        result.MessageKey.Should().Be("Result_ServiceUnavailable");
    }

    [Fact]
    public async Task Faux_service_refuse_par_le_client()
    {
        Start(trusted: true);
        await using var client = new PipeClient(_pipeName, new RejectServer());

        (await client.SendAsync(CommandId.RunHealthAnalysis)).Reason.Should().Be(FailureReason.ServiceUnavailable);
    }

    [Fact]
    public async Task Reconnexion_apres_redemarrage_du_service()
    {
        Start(trusted: true);
        await using var client = new PipeClient(_pipeName, new AcceptAnyServer());
        (await client.SendAsync(CommandId.RunHealthAnalysis)).IsSuccess.Should().BeTrue();

        await _cts.CancelAsync();
        await _serverTask!;
        using var cts2 = new CancellationTokenSource();
        var server = new PipeServer(_pipeName, new UnsecuredPipeStreamFactory(), new FixedVerifier(true), new EchoHandler(), NullLogger<PipeServer>.Instance);
        var task = server.RunAsync(cts2.Token);

        (await client.SendAsync(CommandId.RunHealthAnalysis)).IsSuccess.Should().BeTrue();

        await cts2.CancelAsync();
        await task;
    }

    [Fact]
    public async Task Encadrement_borne()
    {
        using var stream = new MemoryStream();
        var big = new IpcRequest { RequestId = "1", Command = new string('a', MessageFraming.MaxRequestBytes) };

        var act = () => MessageFraming.WriteAsync(stream, big, MessageFraming.MaxRequestBytes, default);

        await act.Should().ThrowAsync<InvalidDataException>();
        (await MessageFraming.ReadAsync<IpcRequest>(new MemoryStream(), 100, default)).Should().BeNull();
        var truncated = new MemoryStream([10, 0, 0, 0, 1, 2]);
        var read = () => MessageFraming.ReadAsync<IpcRequest>(truncated, 100, default);
        await read.Should().ThrowAsync<EndOfStreamException>();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_serverTask is not null)
        {
            await _serverTask;
        }

        _cts.Dispose();
    }
}
