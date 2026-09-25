using PcSante.Service.Diagnostics;
using PcSante.Service.Data;
using PcSante.Core.Ui;
using Microsoft.Extensions.DependencyInjection;
using PcSante.Core.Audit;
using PcSante.Core.Commands;
using PcSante.Core.Health;
using PcSante.Core.Processes;
using PcSante.Core.Reporting;
using PcSante.Core.Scheduling;
using PcSante.Core.Windows;
using PcSante.Core.Optimization;

namespace PcSante.Service.Tests;

public sealed class ActionTests
{
    [Fact]
    public async Task Pare_feu_active_avec_sauvegarde_et_annulation()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.EnableFirewallProfile, new() { ["profile"] = "Public" });

        result.Status.Should().Be(CommandStatus.Succeeded);
        result.MessageKey.Should().Be("Result_FirewallEnabled");
        svc.Firewall.Profiles[FirewallProfile.Public].Should().BeTrue();

        var undo = await svc.Run(CommandId.UndoAction, new() { ["undoId"] = result.UndoId!.Value.ToString() });
        undo.Status.Should().Be(CommandStatus.Succeeded);
        svc.Firewall.Profiles[FirewallProfile.Public].Should().BeFalse();
        (await svc.AuditAsync()).Should().Contain(a => a.Command == "undo:EnableFirewallProfile");
    }

    [Fact]
    public async Task Pare_feu_deja_actif()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.EnableFirewallProfile, new() { ["profile"] = "Private" })).Status.Should().Be(CommandStatus.AlreadyDone);
    }

    [Fact]
    public async Task Controle_echoue_retour_arriere()
    {
        await using var svc = new ServiceFixture();
        svc.Firewall.IgnoreWrites = true;

        var result = await svc.Run(CommandId.EnableFirewallProfile, new() { ["profile"] = "Public" });

        result.Status.Should().Be(CommandStatus.Failed);
        result.Reason.Should().Be(FailureReason.VerificationFailed);
        (await svc.AuditAsync()).Should().Contain(a => a.Outcome == AuditOutcome.RolledBack);
    }

    [Fact]
    public async Task Desactivation_du_pare_feu_confirmee()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.DisableFirewallProfile, new() { ["profile"] = "Private" })).Reason.Should().Be(FailureReason.ConfirmationRequired);
        (await svc.Run(CommandId.DisableFirewallProfile, new() { ["profile"] = "Private" }, confirmed: true)).Status.Should().Be(CommandStatus.Succeeded);
        svc.Firewall.Profiles[FirewallProfile.Private].Should().BeFalse();
    }

    [Fact]
    public async Task Reinitialisation_du_pare_feu_exporte_d_abord()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.ResetFirewallRules, confirmed: true);

        result.Status.Should().Be(CommandStatus.Succeeded);
        svc.Firewall.Exports.Should().ContainSingle();
        svc.Restore.Points.Should().ContainSingle("point de restauration + sauvegarde propre");
        (await svc.Run(CommandId.UndoAction, new() { ["undoId"] = result.UndoId!.Value.ToString() })).Status.Should().Be(CommandStatus.Succeeded);
    }

    [Fact]
    public async Task Protections_defender()
    {
        await using var svc = new ServiceFixture();
        svc.Defender.Status = svc.Defender.Status with { RealTimeProtectionEnabled = false, CloudProtectionEnabled = false };

        (await svc.Run(CommandId.EnableRealtimeProtection)).MessageKey.Should().Be("Result_RealtimeEnabled");
        (await svc.Run(CommandId.EnableCloudProtection)).MessageKey.Should().Be("Result_CloudEnabled");
        (await svc.Run(CommandId.EnableControlledFolderAccess)).MessageKey.Should().Be("Result_RansomwareEnabled");
        (await svc.Run(CommandId.EnableRealtimeProtection)).Status.Should().Be(CommandStatus.AlreadyDone);
        svc.Defender.Status.ControlledFolderAccessEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Defender_bloque_par_windows_message_clair()
    {
        await using var svc = new ServiceFixture();
        svc.Defender.Status = svc.Defender.Status with { RealTimeProtectionEnabled = false };
        svc.Defender.RefuseChanges = true;

        var result = await svc.Run(CommandId.EnableRealtimeProtection);

        result.Status.Should().Be(CommandStatus.Failed);
        result.Reason.Should().Be(FailureReason.VerificationFailed);
    }

    [Fact]
    public async Task Defender_passif_action_impossible()
    {
        await using var svc = new ServiceFixture();
        svc.Defender.Status = svc.Defender.Status with { IsActiveAntivirus = false };

        var result = await svc.Run(CommandId.StartQuickScan);

        result.Reason.Should().Be(FailureReason.NotSupportedOnThisPc);
        result.MessageKey.Should().Be("Result_DefenderNotActive");
    }

    [Fact]
    public async Task Scan_en_arriere_plan_puis_resultat_journalise()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.StartQuickScan);
        result.Status.Should().Be(CommandStatus.Started);
        (await svc.Run(CommandId.StartFullScan)).MessageKey.Should().Be("Result_ScanAlreadyRunning");

        svc.Defender.ScanGate.SetResult(true);
        await ((BackgroundJobs)svc.Provider.GetService(typeof(BackgroundJobs))!).WaitAsync(BackgroundJobs.ScanJob);

        (await svc.AuditAsync()).Should().Contain(a => a.Command == "StartQuickScan:completed" && a.Outcome == AuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Scan_personnalise_chemin_valide()
    {
        await using var svc = new ServiceFixture();
        svc.Defender.ScanGate.SetResult(false);

        (await svc.Run(CommandId.StartCustomScan, new() { ["path"] = @"\\serveur\partage" })).Reason.Should().Be(FailureReason.InvalidParameters);
    }

    [Fact]
    public async Task Quarantaine_restauration()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.RestoreQuarantinedItem, new() { ["id"] = "Virus:DOS/EICAR" })).Reason.Should().Be(FailureReason.ConfirmationRequired);
        (await svc.Run(CommandId.RestoreQuarantinedItem, new() { ["id"] = "Virus:DOS/EICAR" }, confirmed: true)).MessageKey.Should().Be("Result_QuarantineRestored");
        (await svc.Run(CommandId.RestoreQuarantinedItem, new() { ["id"] = "Absent" }, confirmed: true)).Reason.Should().Be(FailureReason.NotFound);
    }

    [Fact]
    public async Task Signatures_et_mises_a_jour()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.UpdateSignatures)).MessageKey.Should().Be("Result_SignaturesUpdated");
        (await svc.Run(CommandId.SearchUpdates)).MessageArgs.Should().Equal("1");
        var install = await svc.Run(CommandId.InstallUpdates, confirmed: true);
        install.MessageKey.Should().Be("Result_UpdatesInstalledReboot");
        (await svc.Run(CommandId.InstallUpdates, confirmed: true)).Status.Should().Be(CommandStatus.AlreadyDone);
        (await svc.Run(CommandId.SearchUpdates)).MessageKey.Should().Be("Result_NoUpdates");
    }

    [Fact]
    public async Task Reparation_windows_update_renomme_le_cache()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.RepairWindowsUpdate);

        result.MessageKey.Should().Be("Result_UpdateRepaired");
        svc.Updates.Backup.Should().StartWith(@"C:\Windows\SoftwareDistribution.pcsante-");
        result.UndoId.Should().NotBeNull();
    }

    [Theory]
    [InlineData(RepairOutcome.NoProblemFound, "Result_RepairNothingFound", CommandStatus.Succeeded)]
    [InlineData(RepairOutcome.Repaired, "Result_RepairDone", CommandStatus.Succeeded)]
    [InlineData(RepairOutcome.ProblemsRemain, "Result_RepairRemainsTryDism", CommandStatus.Succeeded)]
    [InlineData(RepairOutcome.Failed, "Result_RepairFailed", CommandStatus.Failed)]
    public async Task Sfc_resultats(RepairOutcome outcome, string key, CommandStatus status)
    {
        await using var svc = new ServiceFixture();
        svc.Repair.Outcome = outcome;

        var result = await svc.Run(CommandId.RunSystemFileCheck);

        result.MessageKey.Should().Be(key);
        result.Status.Should().Be(status);
        svc.Restore.Points.Should().ContainSingle();
        (await svc.Run(CommandId.RunDismRepair)).Status.Should().Be(status);
    }

    [Fact]
    public async Task Points_de_restauration()
    {
        await using var svc = new ServiceFixture();
        svc.Restore.Enabled = false;

        (await svc.Run(CommandId.CreateRestorePoint)).MessageKey.Should().Be("Result_RestoreDisabled");
        (await svc.Run(CommandId.CleanTemporaryFiles, confirmed: true)).Reason.Should().Be(FailureReason.SafeguardFailed);
        svc.Cleanup.Cleaned.Should().BeEmpty("jamais de nettoyage sans point de restauration");

        (await svc.Run(CommandId.EnableSystemRestore)).MessageKey.Should().Be("Result_RestoreEnabled");
        (await svc.Run(CommandId.EnableSystemRestore)).Status.Should().Be(CommandStatus.AlreadyDone);
        (await svc.Run(CommandId.CreateRestorePoint)).MessageKey.Should().Be("Result_RestorePointCreated");
    }

    [Fact]
    public async Task Arret_de_processus()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.StopProcess, new() { ["pid"] = "4" }, confirmed: true)).Reason.Should().Be(FailureReason.ProtectedItem);
        svc.System.Owners[100] = "S-1-5-21-999";
        (await svc.Run(CommandId.StopProcess, new() { ["pid"] = "100" }, confirmed: true)).MessageKey.Should().Be("Result_ProcessOtherUser");
        svc.System.Owners.Clear();
        (await svc.Run(CommandId.StopProcess, new() { ["pid"] = "100" }, confirmed: true)).MessageKey.Should().Be("Result_ProcessStopped");
        (await svc.Run(CommandId.StopProcess, new() { ["pid"] = "100" }, confirmed: true)).Status.Should().Be(CommandStatus.AlreadyDone);
        (await svc.Run(CommandId.StopProcess, new() { ["pid"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) }, confirmed: true))
            .Reason.Should().Be(FailureReason.ProtectedItem);
    }

    [Fact]
    public async Task Desactivation_de_service_annulable_et_services_proteges()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.DisableServiceForProcess, new() { ["service"] = "WinDefend" }, confirmed: true)).Reason.Should().Be(FailureReason.ProtectedItem);
        (await svc.Run(CommandId.DisableServiceForProcess, new() { ["service"] = "Inconnu" }, confirmed: true)).Reason.Should().Be(FailureReason.NotFound);

        var result = await svc.Run(CommandId.DisableServiceForProcess, new() { ["service"] = "EvilUpdater" }, confirmed: true);
        result.MessageKey.Should().Be("Result_ServiceDisabled");
        svc.ServicesApi.Services["EvilUpdater"].StartMode.Should().Be(ServiceStartMode.Disabled);

        await svc.Run(CommandId.UndoAction, new() { ["undoId"] = result.UndoId!.Value.ToString() });
        svc.ServicesApi.Services["EvilUpdater"].Should().Match<ServiceInfo>(s => s.StartMode == ServiceStartMode.Automatic && s.IsRunning);
    }

    [Fact]
    public async Task Demarrage_desactive_puis_annule_pour_l_utilisateur_appelant()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.DisableStartupItem, new() { ["id"] = "UserRun|Spotify" });

        result.MessageKey.Should().Be("Result_StartupDisabled");
        svc.Startup.LastSid.Should().Be("S-1-5-21-1");
        svc.Startup.Items.Single(i => i.Name == "Spotify").Enabled.Should().BeFalse();
        (await svc.Run(CommandId.GetUndoableActions)).GetData<List<UndoView>>().Should().ContainSingle(u => u.Command == CommandId.DisableStartupItem);

        await svc.Run(CommandId.UndoAction, new() { ["undoId"] = result.UndoId!.Value.ToString() });
        svc.Startup.Items.Single(i => i.Name == "Spotify").Enabled.Should().BeTrue();
        (await svc.Run(CommandId.EnableStartupItem, new() { ["id"] = "UserRun|Spotify" })).Status.Should().Be(CommandStatus.AlreadyDone);
        (await svc.Run(CommandId.DisableStartupItem, new() { ["id"] = "Inconnu|X" })).Reason.Should().Be(FailureReason.NotFound);
        (await svc.Run(CommandId.UndoAction, new() { ["undoId"] = Guid.NewGuid().ToString() })).Reason.Should().Be(FailureReason.NotFound);
    }

    [Fact]
    public async Task Taches_tierces()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.DisableThirdPartyTask, new() { ["id"] = @"\Contoso\Updater" });
        result.MessageKey.Should().Be("Result_TaskDisabled");
        (await svc.Run(CommandId.EnableThirdPartyTask, new() { ["id"] = @"\Contoso\Updater" })).MessageKey.Should().Be("Result_TaskEnabled");
        (await svc.Run(CommandId.EnableThirdPartyTask, new() { ["id"] = @"\Contoso\Updater" })).Status.Should().Be(CommandStatus.AlreadyDone);
        (await svc.Run(CommandId.EnableThirdPartyTask, new() { ["id"] = @"\Absent" })).Reason.Should().Be(FailureReason.NotFound);
        (await svc.Run(CommandId.GetThirdPartyTasks)).GetData<List<ThirdPartyTask>>().Should().ContainSingle();
    }

    [Theory]
    [InlineData(CommandId.CleanTemporaryFiles, CleanupTarget.TemporaryFiles)]
    [InlineData(CommandId.CleanWindowsUpdateCache, CleanupTarget.WindowsUpdateCache)]
    [InlineData(CommandId.EmptyRecycleBin, CleanupTarget.RecycleBin)]
    [InlineData(CommandId.CleanBrowserCaches, CleanupTarget.BrowserCaches)]
    public async Task Nettoyage(CommandId command, CleanupTarget target)
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(command, confirmed: true);

        result.Status.Should().Be(CommandStatus.Succeeded);
        result.MessageKey.Should().Be("Result_Cleaned");
        svc.Cleanup.Cleaned.Should().Equal(target);
        svc.Restore.Points.Should().ContainSingle();
        (await svc.Run(command, confirmed: true)).MessageKey.Should().Be("Result_NothingToClean");
    }

    [Fact]
    public async Task Plan_d_alimentation_annulable()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.SetPowerPlan, new() { ["plan"] = FakePower.High.ToString() });
        result.MessageKey.Should().Be("Result_PowerPlanSet");
        svc.Power.Active.Should().Be(FakePower.High);

        await svc.Run(CommandId.UndoAction, new() { ["undoId"] = result.UndoId!.Value.ToString() });
        svc.Power.Active.Should().Be(FakePower.Balanced);
        (await svc.Run(CommandId.SetPowerPlan, new() { ["plan"] = Guid.NewGuid().ToString() })).Reason.Should().Be(FailureReason.NotFound);
        (await svc.Run(CommandId.SetPowerPlan, new() { ["plan"] = FakePower.Balanced.ToString() })).Status.Should().Be(CommandStatus.AlreadyDone);
        (await svc.Run(CommandId.GetPowerPlans)).GetData<List<PowerPlan>>().Should().HaveCount(2);
    }

    [Fact]
    public async Task Planification_des_modeles_et_execution()
    {
        await using var svc = new ServiceFixture();
        var p = new Dictionary<string, string>
        {
            ["template"] = "WeeklyCleanup",
            ["day"] = "Sunday",
            ["time"] = "11:00",
            ["onlyWhenIdle"] = "true",
            ["onlyOnAcPower"] = "true",
        };

        (await svc.Run(CommandId.EnableScheduledTemplate, p)).MessageKey.Should().Be("Result_TaskScheduled");
        (await svc.Run(CommandId.EnableScheduledTemplate, p)).Status.Should().Be(CommandStatus.AlreadyDone);
        svc.Scheduler.Registered[ScheduledTemplateId.WeeklyCleanup].Day.Should().Be(DayOfWeek.Sunday);

        (await svc.Run(CommandId.RunScheduledTemplate, new() { ["template"] = "DailyAntivirusScan" })).MessageKey
            .Should().Be("Result_TaskNotScheduled", "un modÃ¨le non programmÃ© ne peut pas Ãªtre dÃ©clenchÃ©");
        var run = await svc.Run(CommandId.RunScheduledTemplate, new() { ["template"] = "WeeklyCleanup" });
        run.MessageKey.Should().Be("Result_TaskRunSucceeded");
        svc.Cleanup.Cleaned.Should().Equal(CleanupTarget.TemporaryFiles, CleanupTarget.RecycleBin);

        var templates = (await svc.Run(CommandId.GetScheduledTemplates)).GetData<List<TemplateView>>()!;
        templates.Should().HaveCount(ScheduledTemplates.All.Count);
        templates.Single(t => t.Id == ScheduledTemplateId.WeeklyCleanup).Should().Match<TemplateView>(t => t.Enabled && t.LastRun!.Succeeded);
        (await svc.Run(CommandId.GetTaskRunLog)).GetData<List<TaskRunEntry>>().Should().ContainSingle();
        (await svc.AuditAsync()).Should().Contain(a => a.Who == "Planificateur" && a.Command == "CleanTemporaryFiles");

        (await svc.Run(CommandId.DisableScheduledTemplate, new() { ["template"] = "WeeklyCleanup" })).MessageKey.Should().Be("Result_TaskUnscheduled");
        (await svc.Run(CommandId.DisableScheduledTemplate, new() { ["template"] = "WeeklyCleanup" })).Status.Should().Be(CommandStatus.AlreadyDone);
    }

    [Fact]
    public async Task Reparation_reseau_dns_sans_risque_et_reinitialisation_protegee()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.FlushDnsCache)).MessageKey.Should().Be("Result_DnsFlushed");
        svc.Restore.Points.Should().BeEmpty("vider le cache DNS ne modifie pas Windows");

        (await svc.Run(CommandId.ResetNetworkStack)).Reason.Should().Be(FailureReason.ConfirmationRequired);
        svc.Network.Calls.Should().Equal("dns");

        (await svc.Run(CommandId.ResetNetworkStack, confirmed: true)).MessageKey.Should().Be("Result_NetworkResetRestart");
        svc.Restore.Points.Should().ContainSingle("point de restauration avant la rÃ©initialisation");
        svc.Network.Calls.Should().Equal("dns", "reset");

        svc.Network.Succeeds = false;
        (await svc.Run(CommandId.FlushDnsCache)).MessageKey.Should().Be("Result_NetworkRepairFailed");
    }

    [Fact]
    public async Task BitLocker_cle_enregistree_avant_tout_chiffrement()
    {
        await using var svc = new ServiceFixture();
        svc.Accounts.Accounts[0] = svc.Accounts.Accounts[0] with { Sid = ServiceFixture.Alice.UserSid! };
        var keySaved = new Dictionary<string, string> { ["keySaved"] = "true" };

        (await svc.Run(CommandId.EnableBitLocker, keySaved, confirmed: true)).MessageKey.Should().Be("Result_BitLockerKeyFirst", "aucune clÃ© de rÃ©cupÃ©ration n'existe encore");
        (await svc.Run(CommandId.EnableBitLocker, keySaved)).Reason.Should().Be(FailureReason.ConfirmationRequired);

        var key = (await svc.Run(CommandId.GetBitLockerRecoveryKey)).GetData<BitLockerRecoveryKey>()!;
        key.Password.Should().HaveLength(55);
        (await svc.Run(CommandId.EnableBitLocker, new() { ["keySaved"] = "false" }, confirmed: true)).MessageKey.Should().Be("Result_BitLockerKeyFirst");
        svc.BitLocker.EncryptionStarts.Should().Be(0);

        var started = await svc.Run(CommandId.EnableBitLocker, keySaved, confirmed: true);
        started.MessageKey.Should().Be("Result_BitLockerStarted");
        svc.BitLocker.EncryptionStarts.Should().Be(1);
        (await svc.Run(CommandId.EnableBitLocker, keySaved, confirmed: true)).Status.Should().Be(CommandStatus.AlreadyDone);
    }

    [Fact]
    public async Task BitLocker_refuse_aux_comptes_standard_sur_famille_et_sans_tpm()
    {
        await using var svc = new ServiceFixture();
        var keySaved = new Dictionary<string, string> { ["keySaved"] = "true" };

        // Appelant non administrateur : ni clÃ©, ni chiffrement.
        (await svc.Run(CommandId.GetBitLockerRecoveryKey)).MessageKey.Should().Be("Result_AdminRequired");
        (await svc.Run(CommandId.EnableBitLocker, keySaved, confirmed: true)).MessageKey.Should().Be("Result_AdminRequired");

        svc.Accounts.Accounts[0] = svc.Accounts.Accounts[0] with { Sid = ServiceFixture.Alice.UserSid! };
        svc.BitLocker.Status = svc.BitLocker.Status with { TpmReady = false, HasRecoveryKey = true };
        (await svc.Run(CommandId.EnableBitLocker, keySaved, confirmed: true)).MessageKey.Should().Be("Result_TpmNotReady");

        svc.BitLocker.Status = BitLockerStatus.NotSupported;
        (await svc.Run(CommandId.GetBitLockerRecoveryKey)).MessageKey.Should().Be("Result_BitLockerNotSupported");
        (await svc.Run(CommandId.EnableBitLocker, keySaved, confirmed: true)).MessageKey.Should().Be("Result_BitLockerNotSupported");
        svc.BitLocker.EncryptionStarts.Should().Be(0);
    }

    [Fact]
    public async Task Sessions_actions_reservees_aux_administrateurs()
    {
        await using var svc = new ServiceFixture();
        var bob = new Dictionary<string, string> { ["sessionId"] = "2" };

        (await svc.Run(CommandId.GetSessions)).GetData<List<UserSession>>().Should().HaveCount(2);
        (await svc.Run(CommandId.LogOffSession, bob, confirmed: true)).MessageKey.Should().Be("Result_AdminRequired",
            "un compte standard ne ferme pas la session d'un autre par le service SYSTEM");
        svc.Sessions.Sessions.Should().HaveCount(2);

        svc.Accounts.Accounts[0] = svc.Accounts.Accounts[0] with { Sid = ServiceFixture.Alice.UserSid! };
        (await svc.Run(CommandId.SendSessionMessage, new() { ["sessionId"] = "2", ["message"] = "Maintenance", ["language"] = "en" }))
            .MessageKey.Should().Be("Result_SendSessionMessageDone");
        svc.Sessions.Messages.Should().ContainSingle().Which.Should().Contain("Maintenance in progress");
        (await svc.RunRaw("SendSessionMessage", new() { ["sessionId"] = "2", ["message"] = "Tapez votre mot de passe" })).Status
            .Should().Be(CommandStatus.Refused, "aucun texte libre : liste fermée de messages");

        (await svc.Run(CommandId.DisconnectSession, bob)).Reason.Should().Be(FailureReason.ConfirmationRequired);
        (await svc.Run(CommandId.DisconnectSession, bob, confirmed: true)).MessageKey.Should().Be("Result_DisconnectSessionDone");
        (await svc.Run(CommandId.DisconnectSession, bob, confirmed: true)).Status.Should().Be(CommandStatus.AlreadyDone);
        (await svc.Run(CommandId.LogOffSession, bob, confirmed: true)).MessageKey.Should().Be("Result_LogOffSessionDone");
        svc.Sessions.Sessions.Should().ContainSingle(s => s.SessionId == 1);
        (await svc.Run(CommandId.LogOffSession, bob, confirmed: true)).MessageKey.Should().Be("Result_SessionNotFound");
    }

    [Fact]
    public async Task Connexion_a_distance_depuis_une_adresse_nouvelle_signalee()
    {
        await using var svc = new ServiceFixture();

        var first = (await svc.Run(CommandId.RunHealthAnalysis)).GetData<HealthReport>()!;
        var issue = first.Issues.Should().ContainSingle(i => i.Code == "NewRemoteConnection").Subject;
        issue.Args.Should().Equal("203.0.113.7");
        issue.Fix!.Screen.Should().Be(ScreenId.Sessions);

        // L'adresse a été vue pour la première fois il y a plus de 24 heures : elle n'est plus inhabituelle.
        var history = svc.Provider.GetRequiredService<HistoryStore>();
        var old = DateTimeOffset.UtcNow - RemoteAccessTracker.NewAddressWindow - TimeSpan.FromMinutes(1);
        await history.SetValueAsync("rdp.addresses", System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, DateTimeOffset> { ["203.0.113.7"] = old }, PcSante.Core.PcSanteJson.Options), default);
        (await svc.Run(CommandId.RunHealthAnalysis)).GetData<HealthReport>()!.Issues
            .Should().NotContain(i => i.Code == "NewRemoteConnection", "adresse déjà connue depuis plus de 24 heures");
    }

    [Fact]
    public async Task Profil_de_services_en_manuel_seulement_et_annulable()
    {
        await using var svc = new ServiceFixture();
        svc.ServicesApi.Services["DiagTrack"] = new ServiceInfo("DiagTrack", "Télémétrie", ServiceStartMode.Automatic, true, null);
        svc.ServicesApi.Services["XblGameSave"] = new ServiceInfo("XblGameSave", "Xbox", ServiceStartMode.AutomaticDelayed, false, null);
        svc.ServicesApi.Services["MapsBroker"] = new ServiceInfo("MapsBroker", "Cartes", ServiceStartMode.Manual, false, null);
        var office = new Dictionary<string, string> { ["profile"] = "Office" };

        var preview = (await svc.Run(CommandId.GetServiceProfileChanges, office)).GetData<List<ServiceChange>>()!;
        preview.Select(c => c.Name).Should().BeEquivalentTo(["DiagTrack", "XblGameSave"], "seuls les services automatiques changent");
        (await svc.Run(CommandId.GetServiceProfileChanges, new() { ["profile"] = "Gaming" })).GetData<List<ServiceChange>>()!
            .Should().ContainSingle(c => c.Name == "DiagTrack", "le profil jeu garde les services Xbox");

        var result = await svc.Run(CommandId.ApplyServiceProfile, office);
        result.MessageKey.Should().Be("Result_ProfileApplied");
        result.MessageArgs.Should().Equal("2");
        svc.Restore.Points.Should().ContainSingle();
        svc.ServicesApi.Services["DiagTrack"].StartMode.Should().Be(ServiceStartMode.Manual);
        svc.ServicesApi.Services.Values.Should().NotContain(s => s.StartMode == ServiceStartMode.Disabled, "aucun service n'est désactivé");
        (await svc.Run(CommandId.ApplyServiceProfile, office)).Status.Should().Be(CommandStatus.AlreadyDone);

        (await svc.Run(CommandId.UndoAction, new() { ["undoId"] = result.UndoId!.Value.ToString() })).Status.Should().Be(CommandStatus.Succeeded);
        svc.ServicesApi.Services["DiagTrack"].StartMode.Should().Be(ServiceStartMode.Automatic);
        svc.ServicesApi.Services["XblGameSave"].StartMode.Should().Be(ServiceStartMode.AutomaticDelayed);
    }

    [Fact]
    public async Task Antivirus_declares_consultables_en_offre_gratuite()
    {
        await using var svc = new ServiceFixture(premium: false);
        svc.SecurityCenter.Products.Add(new AntivirusProduct("Norton 360", true, false, false));

        var products = (await svc.Run(CommandId.GetAntivirusProducts)).GetData<List<AntivirusProduct>>()!;

        products.Should().HaveCount(2).And.Contain(p => p.Name == "Norton 360" && p.Enabled && !p.UpToDate && !p.IsDefender);
    }

    [Fact]
    public async Task Compte_invite_desactive_puis_annule_et_signale_par_l_analyse()
    {
        await using var svc = new ServiceFixture();
        (await svc.Run(CommandId.DisableGuestAccount)).Status.Should().Be(CommandStatus.AlreadyDone, "l'InvitÃ© est dÃ©sactivÃ© par dÃ©faut");

        svc.Accounts.Accounts[1] = svc.Accounts.Accounts[1] with { Enabled = true };
        var accounts = (await svc.Run(CommandId.GetLocalAccounts)).GetData<List<LocalAccount>>()!;
        accounts.Should().Contain(a => a.IsGuest && a.Enabled).And.Contain(a => a.Name == "alice" && a.IsAdministrator);
        (await svc.Run(CommandId.RunHealthAnalysis)).GetData<HealthReport>()!.Issues.Should().Contain(i => i.Code == "GuestEnabled");

        var result = await svc.Run(CommandId.DisableGuestAccount);
        result.MessageKey.Should().Be("Result_GuestDisabled");
        svc.Accounts.Accounts[1].Enabled.Should().BeFalse();

        (await svc.Run(CommandId.UndoAction, new() { ["undoId"] = result.UndoId!.Value.ToString() })).Status.Should().Be(CommandStatus.Succeeded);
        svc.Accounts.Accounts[1].Enabled.Should().BeTrue("Â« Annuler Â» rÃ©active le compte InvitÃ©");
    }

    [Fact]
    public async Task Rapport_mensuel_planifie_dans_la_langue_choisie()
    {
        await using var svc = new ServiceFixture();
        var p = new Dictionary<string, string>
        {
            ["template"] = "MonthlyReport",
            ["day"] = "MonthStart",
            ["time"] = "09:00",
            ["onlyWhenIdle"] = "false",
            ["onlyOnAcPower"] = "false",
            ["language"] = "en",
        };

        (await svc.Run(CommandId.EnableScheduledTemplate, p)).MessageKey.Should().Be("Result_TaskScheduled");
        svc.Scheduler.Registered[ScheduledTemplateId.MonthlyReport].Should().Match<ScheduleSettings>(s => s.Monthly && s.Day == null && s.Language == "en");

        var run = await svc.Run(CommandId.RunScheduledTemplate, new() { ["template"] = "MonthlyReport", ["language"] = "en" });

        run.MessageKey.Should().Be("Result_TaskRunSucceeded");
        var report = Directory.GetFiles(svc.Paths.Reports, "*.pdf").Should().ContainSingle().Subject;
        (await File.ReadAllBytesAsync(report)).Take(4).Should().Equal("%PDF"u8.ToArray());
        (await svc.AuditAsync()).Should().Contain(a => a.Who == "Planificateur" && a.Command == "GenerateMonthlyReport");
    }

    [Fact]
    public async Task Rapport_mensuel_refuse_une_langue_inconnue_et_exige_premium()
    {
        await using var svc = new ServiceFixture();
        (await svc.Run(CommandId.GenerateMonthlyReport, new() { ["language"] = "de" })).Status.Should().Be(CommandStatus.Refused);
        (await svc.Run(CommandId.GenerateMonthlyReport, new() { ["language"] = "ar" })).MessageKey.Should().Be("Result_MonthlyReportSaved");

        await using var free = new ServiceFixture(premium: false);
        (await free.Run(CommandId.GenerateMonthlyReport)).Status.Should().Be(CommandStatus.Refused);
    }

    [Fact]
    public async Task Tache_planifiee_sans_premium_echoue_proprement()
    {
        await using var svc = new ServiceFixture(premium: false);

        svc.Scheduler.Registered[ScheduledTemplateId.DailyAntivirusScan] = ScheduledTemplates.Get(ScheduledTemplateId.DailyAntivirusScan).Default;

        var run = await svc.Run(CommandId.RunScheduledTemplate, new() { ["template"] = "DailyAntivirusScan" });

        run.Status.Should().Be(CommandStatus.Refused, "RunScheduledTemplate est une fonction Premium");
    }

    [Fact]
    public async Task Analyse_de_sante_et_historique()
    {
        await using var svc = new ServiceFixture();

        var result = await svc.Run(CommandId.RunHealthAnalysis);

        var report = result.GetData<HealthReport>()!;
        report.Issues.Should().Contain(i => i.Code == "FirewallOffPublic");
        report.Color.Should().Be(HealthColor.Red);
        (await svc.Run(CommandId.GetLastHealthReport)).GetData<HealthReport>()!.Score.Should().Be(report.Score);
        (await svc.Run(CommandId.GetScoreHistory)).GetData<List<ScorePoint>>().Should().ContainSingle();

        await svc.Run(CommandId.EnableFirewallProfile, new() { ["profile"] = "Public" });
        var after = (await svc.Run(CommandId.RunHealthAnalysis)).GetData<HealthReport>()!;
        after.Score.Should().BeGreaterThan(report.Score);
        after.Duration.Should().BeLessThan(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Requetes_de_lecture()
    {
        await using var svc = new ServiceFixture();

        (await svc.Run(CommandId.GetLiveMetrics)).GetData<LiveMetrics>()!.CpuPercent.Should().Be(12);
        (await svc.Run(CommandId.GetSystemInfo)).GetData<SystemInfo>()!.MachineName.Should().Be("PC-TEST");
        (await svc.Run(CommandId.GetDefenderStatus)).GetData<DefenderStatus>()!.IsAvailable.Should().BeTrue();
        (await svc.Run(CommandId.GetThreatHistory)).GetData<List<ThreatInfo>>().Should().ContainSingle();
        (await svc.Run(CommandId.GetQuarantine)).GetData<List<QuarantineItem>>().Should().ContainSingle();
        (await svc.Run(CommandId.GetFirewallStatus)).GetData<List<FirewallProfileStatus>>().Should().HaveCount(3);
        (await svc.Run(CommandId.GetUpdateStatus)).GetData<UpdateStatus>()!.ServiceRunning.Should().BeTrue();
        (await svc.Run(CommandId.GetStartupItems)).GetData<List<StartupItem>>().Should().HaveCount(2);
        (await svc.Run(CommandId.GetCleanupEstimate)).GetData<CleanupEstimate>()!.TotalBytes.Should().BeGreaterThan(0);
        (await svc.Run(CommandId.GetAuditLog)).IsSuccess.Should().BeTrue();
        (await svc.Run(CommandId.GetLicenseStatus)).GetData<Core.Licensing.LicenseStatus>()!.IsPremium.Should().BeTrue();
    }

    [Fact]
    public async Task Processus_avec_reputation_et_historique()
    {
        await using var svc = new ServiceFixture();
        svc.Signatures.Known[@"C:\Program Files\Google\chrome.exe"] = new SignatureInfo(true, true, "Google LLC", "G");

        var list = (await svc.Run(CommandId.GetProcesses)).GetData<List<ProcessView>>()!;

        list.First().Name.Should().Be("chrome");
        list.Single(p => p.Name == "chrome").Should().Match<ProcessView>(p => p.IsSigned && p.Reputation == Reputation.Useful);
        list.Single(p => p.Name == "updater").Reputation.Should().Be(Reputation.Suspicious);
        list.Single(p => p.Name == "lsass").IsProtected.Should().BeTrue();

        var history = (Data.HistoryStore)svc.Provider.GetService(typeof(Data.HistoryStore))!;
        await history.AddProcessSamplesAsync([("chrome", 30, 900L << 20), ("notepad", 0, 10L << 20)], default);
        await history.AddProcessSamplesAsync([("chrome", 20, 800L << 20)], default);
        var entries = (await svc.Run(CommandId.GetProcessHistory)).GetData<List<ProcessHistoryEntry>>()!;
        entries.First().Name.Should().Be("chrome");
        entries.First().AverageCpuPercent.Should().Be(25);
        entries.First().HighUsageRatio.Should().Be(1);
        await history.PruneAsync(default);
    }

    [Fact]
    public async Task Donnees_du_rapport()
    {
        await using var svc = new ServiceFixture();
        await svc.Run(CommandId.RunHealthAnalysis);
        await svc.Run(CommandId.EnableFirewallProfile, new() { ["profile"] = "Public" });

        var data = (await svc.Run(CommandId.GetReportData, new() { ["period"] = "Month" })).GetData<ReportData>()!;

        data.Period.Should().Be(ReportPeriod.Month);
        data.MachineName.Should().Be("PC-TEST");
        data.CurrentHealth.Should().NotBeNull();
        data.ScoreHistory.Should().NotBeEmpty();
        data.Threats.Should().ContainSingle();
        data.Actions.Should().Contain(a => a.Command == "EnableFirewallProfile");
        (await svc.Run(CommandId.GetReportData, new() { ["period"] = "Year" })).Reason.Should().Be(FailureReason.InvalidParameters);
    }

    [Fact]
    public async Task Operations_de_licence()
    {
        await using var svc = new ServiceFixture(premium: false);
        var key = Core.Licensing.LicenseKeyFormat.Generate();

        var activate = await svc.Run(CommandId.ActivateLicense, new() { ["key"] = key });
        activate.MessageKey.Should().Be("License_Activated");
        activate.GetData<Core.Licensing.LicenseStatus>()!.IsPremium.Should().BeTrue();
        (await svc.Run(CommandId.RevalidateLicense)).MessageKey.Should().Be("License_Revalidated");
        (await svc.Run(CommandId.TransferLicense, new() { ["key"] = key })).MessageKey.Should().Be("License_Transferred");
        (await svc.Run(CommandId.DeactivateLicense)).Reason.Should().Be(FailureReason.ConfirmationRequired);
        var deactivate = await svc.Run(CommandId.DeactivateLicense, confirmed: true);
        deactivate.MessageKey.Should().Be("License_Deactivated");
        (await svc.Run(CommandId.RevalidateLicense)).Status.Should().Be(CommandStatus.Failed);
    }
}
