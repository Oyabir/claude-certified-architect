using PcSante.Core.Commands;

namespace PcSante.Core.Tests;

public class CommandDefinitionsTests
{
    [Fact]
    public void Chaque_identifiant_a_une_definition()
    {
        foreach (var id in Enum.GetValues<CommandId>())
        {
            CommandDefinitions.TryGet(id, out var d).Should().BeTrue($"{id} doit être décrit");
            d.Id.Should().Be(id);
        }

        CommandDefinitions.All.Should().HaveCount(Enum.GetValues<CommandId>().Length);
    }

    [Fact]
    public void Commande_inconnue_leve_une_exception()
    {
        var act = () => CommandDefinitions.Get((CommandId)99999);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Suppressions_de_fichiers_confirmees_et_protegees()
    {
        var deletions = CommandDefinitions.All.Values.Where(d => d.Confirmation == ConfirmationKind.DeletesFiles).ToList();

        deletions.Should().NotBeEmpty();
        deletions.Should().OnlyContain(d => d.Safeguard == SafeguardKind.RestorePoint || d.Safeguard == SafeguardKind.RestorePointAndOwnBackup);
    }

    [Fact]
    public void Lectures_sans_confirmation_ni_sauvegarde()
    {
        CommandDefinitions.All.Values.Where(d => d.Kind == CommandKind.Query)
            .Should().OnlyContain(d => d.Safeguard == SafeguardKind.None && !d.RequiresConfirmation);
    }

    [Fact]
    public void Diagnostic_et_licence_gratuits_actions_premium()
    {
        CommandDefinitions.Get(CommandId.RunHealthAnalysis).Tier.Should().Be(RequiredTier.Free);
        CommandDefinitions.Get(CommandId.GetLiveMetrics).Tier.Should().Be(RequiredTier.Free);
        CommandDefinitions.Get(CommandId.ActivateLicense).Tier.Should().Be(RequiredTier.Free);
        CommandDefinitions.Get(CommandId.EnableRealtimeProtection).Tier.Should().Be(RequiredTier.Premium);
        CommandDefinitions.Get(CommandId.CleanTemporaryFiles).Tier.Should().Be(RequiredTier.Premium);
    }

    [Fact]
    public void Optimisations_reversibles_annulables()
    {
        CommandDefinitions.Get(CommandId.DisableStartupItem).IsUndoable.Should().BeTrue();
        CommandDefinitions.Get(CommandId.SetPowerPlan).IsUndoable.Should().BeTrue();
        CommandDefinitions.Get(CommandId.StartQuickScan).IsUndoable.Should().BeFalse();
    }

    [Fact]
    public void Reduire_la_protection_demande_confirmation()
    {
        CommandDefinitions.Get(CommandId.DisableFirewallProfile).Confirmation.Should().Be(ConfirmationKind.ReducesProtection);
        CommandDefinitions.Get(CommandId.ResetFirewallRules).RequiresConfirmation.Should().BeTrue();
    }
}
