using Microsoft.EntityFrameworkCore;
using PcSante.Core.Pme;
using PcSante.PmeConsole.Data;

namespace PcSante.PmeConsole.Services;

public sealed record NewOrganization(Guid OrganizationId, string OwnerEmail, string TemporaryPassword, string EnrollmentCode);

public enum LoginOutcome
{
    Success,
    Invalid,
    Locked,
}

/// <summary>Organisations, gérants et lecteurs : création, connexion (verrouillage après 5 échecs), code d'inscription.</summary>
public sealed partial class ManagerService(ConsoleDbContext db, TimeProvider time, ILogger<ManagerService> logger)
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private readonly ILogger<ManagerService> _logger = logger;

    /// <summary>Création d'une organisation et de son gérant (commande d'administration du serveur).</summary>
    public async Task<NewOrganization> CreateOrganizationAsync(string name, string ownerEmail, int seats, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || !IsEmail(ownerEmail) || seats is < 1 or > 1000)
        {
            throw new ArgumentException("Nom, adresse e-mail ou nombre de postes invalide.");
        }

        var code = EnrollmentCodeFormat.Generate();
        var organization = new OrganizationEntity
        {
            Id = Guid.NewGuid(),
            Name = Secrets.Truncate(name.Trim(), 128),
            EnrollmentCodeHash = EnrollmentCodeFormat.Hash(code),
            EnrollmentCodeHint = code[^4..],
            Seats = seats,
            CreatedAt = time.GetUtcNow(),
        };
        var password = Secrets.TemporaryPassword();
        db.Organizations.Add(organization);
        db.Managers.Add(NewManager(organization.Id, ownerEmail, ownerEmail, ManagerRole.Owner, password));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new NewOrganization(organization.Id, NormalizeEmail(ownerEmail), password, code);
    }

    public async Task<(LoginOutcome Outcome, ManagerEntity? Manager)> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email ?? string.Empty);
        var manager = await db.Managers.FirstOrDefaultAsync(m => m.Email == normalized, ct).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (manager is null)
        {
            // Même coût qu'une vérification réelle : on ne révèle pas si le compte existe.
            _ = Secrets.VerifyPassword(password ?? string.Empty, Secrets.HashPassword("leurre"));
            LogLoginRefused("inconnu");
            return (LoginOutcome.Invalid, null);
        }

        if (manager.LockedUntil > now)
        {
            return (LoginOutcome.Locked, null);
        }

        if (!Secrets.VerifyPassword(password ?? string.Empty, manager.PasswordHash))
        {
            manager.FailedLogins++;
            if (manager.FailedLogins >= MaxFailedLogins)
            {
                manager.LockedUntil = now + LockDuration;
                manager.FailedLogins = 0;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogLoginRefused("mot de passe");
            return (manager.LockedUntil > now ? LoginOutcome.Locked : LoginOutcome.Invalid, null);
        }

        manager.FailedLogins = 0;
        manager.LockedUntil = null;
        manager.LastLoginAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (LoginOutcome.Success, manager);
    }

    public async Task<bool> ChangePasswordAsync(Guid managerId, string? current, string? replacement, CancellationToken ct)
    {
        var manager = await db.Managers.FirstOrDefaultAsync(m => m.Id == managerId, ct).ConfigureAwait(false);
        if (manager is null || replacement is null || replacement.Length < Secrets.MinPasswordLength
            || !Secrets.VerifyPassword(current ?? string.Empty, manager.PasswordHash))
        {
            return false;
        }

        manager.PasswordHash = Secrets.HashPassword(replacement);
        manager.MustChangePassword = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Ajoute un gérant ou un lecteur ; renvoie son mot de passe provisoire (affiché une seule fois).</summary>
    public async Task<string?> AddManagerAsync(Guid organizationId, string email, string displayName, ManagerRole role, CancellationToken ct)
    {
        if (!IsEmail(email) || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var normalized = NormalizeEmail(email);
        if (await db.Managers.AnyAsync(m => m.Email == normalized, ct).ConfigureAwait(false))
        {
            return null;
        }

        var password = Secrets.TemporaryPassword();
        db.Managers.Add(NewManager(organizationId, email, displayName, role, password));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return password;
    }

    /// <summary>Supprime un utilisateur de l'organisation (jamais soi-même, jamais le dernier gérant).</summary>
    public async Task<bool> RemoveManagerAsync(Guid organizationId, Guid managerId, Guid callerId, CancellationToken ct)
    {
        var manager = await db.Managers.FirstOrDefaultAsync(m => m.Id == managerId && m.OrganizationId == organizationId, ct).ConfigureAwait(false);
        if (manager is null || manager.Id == callerId)
        {
            return false;
        }

        if (manager.Role == ManagerRole.Owner
            && await db.Managers.CountAsync(m => m.OrganizationId == organizationId && m.Role == ManagerRole.Owner, ct).ConfigureAwait(false) <= 1)
        {
            return false;
        }

        db.Managers.Remove(manager);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Nouveau code d'inscription (l'ancien cesse de fonctionner ; les postes déjà inscrits ne sont pas touchés).</summary>
    public async Task<string?> RotateEnrollmentCodeAsync(Guid organizationId, CancellationToken ct)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, ct).ConfigureAwait(false);
        if (organization is null)
        {
            return null;
        }

        var code = EnrollmentCodeFormat.Generate();
        organization.EnrollmentCodeHash = EnrollmentCodeFormat.Hash(code);
        organization.EnrollmentCodeHint = code[^4..];
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return code;
    }

    public static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    internal static bool IsEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 254 && email.Count(c => c == '@') == 1
        && email.IndexOf('@', StringComparison.Ordinal) > 0 && email.LastIndexOf('.') > email.IndexOf('@', StringComparison.Ordinal) + 1
        && !email.Any(char.IsWhiteSpace);

    private ManagerEntity NewManager(Guid organizationId, string email, string displayName, ManagerRole role, string password) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = NormalizeEmail(email),
        DisplayName = Secrets.Truncate(displayName.Trim(), 128),
        PasswordHash = Secrets.HashPassword(password),
        Role = role,
        MustChangePassword = true,
        CreatedAt = time.GetUtcNow(),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Console PME : connexion refusée ({Reason})")]
    private partial void LogLoginRefused(string reason);
}
