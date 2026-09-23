using Microsoft.Extensions.Time.Testing;
using PcSante.Core.Actions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;

namespace PcSante.Core.Tests;

public class SystemActionPipelineTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeRestorePointApi _restore = new();
    private readonly FakeUndoStore _undo = new();
    private readonly FakeAuditLog _audit = new();
    private readonly SystemActionPipeline _pipeline;

    public SystemActionPipelineTests()
    {
        _restore.Clock = _time.GetUtcNow;
        _pipeline = new SystemActionPipeline(new RestorePointGuard(_restore, _time), _undo, _audit, _time);
    }

    private static ActionContext Ctx(CommandId id) =>
        new(CommandDefinitions.Get(id), CommandParameters.Empty, new CallerIdentity("alice", "S-1-5-21-1", 42));

    [Fact]
    public async Task Cycle_complet_verifier_sauvegarder_executer_controler_journaliser()
    {
        var action = new FakeAction(CommandId.SetPowerPlan);

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.SetPowerPlan), default);

        result.Status.Should().Be(CommandStatus.Succeeded);
        action.Steps.Should().Equal("check", "backup", "execute", "verify");
        _restore.Points.Should().ContainSingle("SetPowerPlan exige un point de restauration");
        result.UndoId.Should().NotBeNull();
        _undo.Records.Should().ContainKey(result.UndoId!.Value);
        var entry = _audit.Entries.Should().ContainSingle().Subject;
        entry.Who.Should().Be("alice");
        entry.ClientProcessId.Should().Be(42);
        entry.Command.Should().Be("SetPowerPlan");
        entry.Outcome.Should().Be(AuditOutcome.Succeeded);
        entry.Timestamp.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task Deja_fait_rien_n_est_execute()
    {
        var action = new FakeAction(CommandId.EnableRealtimeProtection) { Check = CheckResult.AlreadyDone("Result_AlreadyOn") };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.EnableRealtimeProtection), default);

        result.Status.Should().Be(CommandStatus.AlreadyDone);
        result.MessageKey.Should().Be("Result_AlreadyOn");
        action.Steps.Should().Equal("check");
        _audit.Entries.Single().Outcome.Should().Be(AuditOutcome.AlreadyDone);
    }

    [Fact]
    public async Task Bloque_par_windows_refuse_sans_executer()
    {
        var action = new FakeAction(CommandId.EnableRealtimeProtection)
        {
            Check = CheckResult.Blocked(FailureReason.BlockedByWindows, "Result_TamperProtection"),
        };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.EnableRealtimeProtection), default);

        result.Status.Should().Be(CommandStatus.Refused);
        result.Reason.Should().Be(FailureReason.BlockedByWindows);
        action.Steps.Should().Equal("check");
    }

    [Fact]
    public async Task Erreur_de_verification_initiale_echoue_proprement()
    {
        var action = new FakeAction(CommandId.EnableRealtimeProtection) { ThrowOnCheck = true };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.EnableRealtimeProtection), default);

        result.Reason.Should().Be(FailureReason.PreconditionFailed);
        action.Steps.Should().Equal("check");
    }

    [Fact]
    public async Task Sans_point_de_restauration_rien_n_est_execute()
    {
        _restore.Enabled = false;
        var action = new FakeAction(CommandId.CleanTemporaryFiles);

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.CleanTemporaryFiles), default);

        result.Status.Should().Be(CommandStatus.Failed);
        result.Reason.Should().Be(FailureReason.SafeguardFailed);
        result.MessageKey.Should().Be("Result_RestorePointFailed");
        action.Steps.Should().Equal("check");
        _audit.Entries.Single().Outcome.Should().Be(AuditOutcome.Refused);
    }

    [Fact]
    public async Task Point_de_restauration_en_erreur_bloque_l_action()
    {
        _restore.Throws = true;
        var action = new FakeAction(CommandId.CleanTemporaryFiles);

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.CleanTemporaryFiles), default);

        result.Reason.Should().Be(FailureReason.SafeguardFailed);
        action.Steps.Should().NotContain("execute");
    }

    [Fact]
    public async Task Sauvegarde_impossible_rien_n_est_execute()
    {
        var action = new FakeAction(CommandId.EnableCloudProtection) { ThrowOnBackup = true };
        var result = await _pipeline.RunAsync(action, Ctx(CommandId.EnableCloudProtection), default);
        result.MessageKey.Should().Be("Result_BackupFailed");
        action.Steps.Should().NotContain("execute");

        var action2 = new FakeAction(CommandId.EnableCloudProtection) { BackupReturnsNull = true };
        var result2 = await _pipeline.RunAsync(action2, Ctx(CommandId.EnableCloudProtection), default);
        result2.Reason.Should().Be(FailureReason.SafeguardFailed);
        action2.Steps.Should().NotContain("execute");
    }

    [Fact]
    public async Task Controle_echoue_retour_arriere_automatique()
    {
        var action = new FakeAction(CommandId.EnableFirewallProfile) { VerifyResult = false };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.EnableFirewallProfile), default);

        result.Status.Should().Be(CommandStatus.Failed);
        result.MessageKey.Should().Be("Result_VerificationFailedRolledBack");
        action.Steps.Should().Equal("check", "backup", "execute", "verify", "undo");
        _undo.Records.Values.Single().Undone.Should().BeTrue();
        _audit.Entries.Single().Outcome.Should().Be(AuditOutcome.RolledBack);
    }

    [Fact]
    public async Task Controle_en_exception_sans_sauvegarde_echoue()
    {
        var action = new FakeAction(CommandId.RunSystemFileCheck) { ThrowOnVerify = true };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.RunSystemFileCheck), default);

        result.MessageKey.Should().Be("Result_VerificationFailed");
        _audit.Entries.Single().Outcome.Should().Be(AuditOutcome.Failed);
    }

    [Fact]
    public async Task Execution_en_erreur_retour_arriere()
    {
        var action = new FakeAction(CommandId.EnableFirewallProfile) { ThrowOnExecute = true };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.EnableFirewallProfile), default);

        result.Reason.Should().Be(FailureReason.ExecutionFailed);
        result.TechnicalDetails.Should().Be("boom");
        action.Steps.Should().Contain("undo");
    }

    [Fact]
    public async Task Execution_echouee_et_retour_arriere_echoue()
    {
        var action = new FakeAction(CommandId.EnableFirewallProfile) { Execution = ExecutionResult.Fail("Result_X"), ThrowOnUndo = true };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.EnableFirewallProfile), default);

        result.MessageKey.Should().Be("Result_X");
        _audit.Entries.Single().Outcome.Should().Be(AuditOutcome.Failed);
    }

    [Fact]
    public async Task Action_longue_lancee_en_arriere_plan()
    {
        var action = new FakeAction(CommandId.StartFullScan) { Execution = ExecutionResult.Background("Result_ScanStarted") };

        var result = await _pipeline.RunAsync(action, Ctx(CommandId.StartFullScan), default);

        result.Status.Should().Be(CommandStatus.Started);
        result.IsSuccess.Should().BeTrue();
        action.Steps.Should().NotContain("verify");
    }

    [Fact]
    public async Task Annuler_une_action()
    {
        var action = new FakeAction(CommandId.DisableStartupItem);
        var result = await _pipeline.RunAsync(action, Ctx(CommandId.DisableStartupItem), default);

        var undo = await _pipeline.UndoAsync(result.UndoId!.Value, _ => action, CallerIdentity.Scheduler, default);

        undo.Status.Should().Be(CommandStatus.Succeeded);
        undo.MessageKey.Should().Be("Result_Undone");
        _undo.Records[result.UndoId.Value].Undone.Should().BeTrue();
        _audit.Entries.Last().Command.Should().Be("undo:DisableStartupItem");

        var again = await _pipeline.UndoAsync(result.UndoId.Value, _ => action, CallerIdentity.Scheduler, default);
        again.Reason.Should().Be(FailureReason.NotFound);
    }

    [Fact]
    public async Task Annulation_inconnue_ou_en_echec()
    {
        (await _pipeline.UndoAsync(Guid.NewGuid(), _ => null, CallerIdentity.Scheduler, default)).Reason.Should().Be(FailureReason.NotFound);

        var action = new FakeAction(CommandId.DisableStartupItem);
        var result = await _pipeline.RunAsync(action, Ctx(CommandId.DisableStartupItem), default);
        (await _pipeline.UndoAsync(result.UndoId!.Value, _ => null, CallerIdentity.Scheduler, default)).Reason.Should().Be(FailureReason.NotFound);

        action.ThrowOnUndo = true;
        var failed = await _pipeline.UndoAsync(result.UndoId!.Value, _ => action, CallerIdentity.Scheduler, default);
        failed.Status.Should().Be(CommandStatus.Failed);
        failed.MessageKey.Should().Be("Result_UndoFailed");
    }

    [Fact]
    public async Task Action_et_descripteur_incoherents_refuses()
    {
        var act = () => _pipeline.RunAsync(new FakeAction(CommandId.StopProcess), Ctx(CommandId.SetPowerPlan), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
