using PcSante.Core.Actions;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Windows;

namespace PcSante.Core.Tests;

internal sealed class FakeAuditLog : IAuditLog
{
    public List<AuditEntry> Entries { get; } = [];

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ReadAsync(DateTimeOffset since, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(Entries.Where(e => e.Timestamp >= since).Take(max).ToList());
}

internal sealed class FakeUndoStore : IUndoStore
{
    public Dictionary<Guid, UndoRecord> Records { get; } = [];

    public Task<Guid> SaveAsync(CommandId command, BackupData backup, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        Records[id] = new UndoRecord(id, command, DateTimeOffset.UnixEpoch, backup, false);
        return Task.FromResult(id);
    }

    public Task<UndoRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Records.GetValueOrDefault(id));

    public Task<IReadOnlyList<UndoRecord>> ListAsync(bool includeUndone, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UndoRecord>>(Records.Values.Where(r => includeUndone || !r.Undone).ToList());

    public Task MarkUndoneAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Records[id] = Records[id] with { Undone = true };
        return Task.CompletedTask;
    }
}

internal sealed class FakeRestorePointApi : IRestorePointApi
{
    public bool Enabled { get; set; } = true;

    public bool CreateSucceeds { get; set; } = true;

    public bool Throws { get; set; }

    public List<RestorePointInfo> Points { get; } = [];

    public int CreateCalls { get; private set; }

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        Throws ? throw new InvalidOperationException("WMI indisponible") : Task.FromResult(Enabled);

    public Task<bool> EnableAsync(CancellationToken cancellationToken)
    {
        Enabled = true;
        return Task.FromResult(true);
    }

    public Task<bool> CreateAsync(string description, CancellationToken cancellationToken)
    {
        CreateCalls++;
        if (CreateSucceeds)
        {
            Points.Add(new RestorePointInfo(Points.Count + 1, description, Clock()));
        }

        return Task.FromResult(CreateSucceeds);
    }

    public Task<IReadOnlyList<RestorePointInfo>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RestorePointInfo>>(Points.ToList());
}

/// <summary>Action simulée entièrement paramétrable pour tester le cycle.</summary>
internal sealed class FakeAction(CommandId command) : SystemAction
{
    public override CommandId Command { get; } = command;

    public CheckResult Check { get; set; } = CheckResult.Proceed;

    public ExecutionResult Execution { get; set; } = ExecutionResult.Ok("Result_Done");

    public bool VerifyResult { get; set; } = true;

    public bool ThrowOnExecute { get; set; }

    public bool ThrowOnCheck { get; set; }

    public bool ThrowOnVerify { get; set; }

    public bool ThrowOnBackup { get; set; }

    public bool BackupReturnsNull { get; set; }

    public bool UndoResult { get; set; } = true;

    public bool ThrowOnUndo { get; set; }

    public List<string> Steps { get; } = [];

    public override Task<CheckResult> CheckAsync(ActionContext context, CancellationToken cancellationToken)
    {
        Steps.Add("check");
        return ThrowOnCheck ? throw new InvalidOperationException("check") : Task.FromResult(Check);
    }

    public override Task<BackupData?> BackupAsync(ActionContext context, CancellationToken cancellationToken)
    {
        Steps.Add("backup");
        if (ThrowOnBackup)
        {
            throw new IOException("backup");
        }

        return Task.FromResult(BackupReturnsNull ? null : new BackupData("état initial", "{\"v\":1}"));
    }

    public override Task<ExecutionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        Steps.Add("execute");
        return ThrowOnExecute ? throw new InvalidOperationException("boom") : Task.FromResult(Execution);
    }

    public override Task<bool> VerifyAsync(ActionContext context, CancellationToken cancellationToken)
    {
        Steps.Add("verify");
        return ThrowOnVerify ? throw new InvalidOperationException("verify") : Task.FromResult(VerifyResult);
    }

    public override Task<bool> UndoAsync(BackupData backup, CancellationToken cancellationToken)
    {
        Steps.Add("undo");
        return ThrowOnUndo ? throw new InvalidOperationException("undo") : Task.FromResult(UndoResult);
    }
}
