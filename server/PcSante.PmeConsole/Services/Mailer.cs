using System.Net;
using System.Net.Mail;

namespace PcSante.PmeConsole.Services;

/// <summary>
/// Réglages d'envoi (section « Email » de la configuration). Le mot de passe SMTP ne vient QUE de la variable
/// d'environnement PCSANTE_SMTP_PASSWORD. Sans serveur SMTP, les e-mails sont écrits en .eml dans PickupDirectory.
/// </summary>
public sealed class EmailOptions
{
    public const string PasswordVariable = "PCSANTE_SMTP_PASSWORD";

    public string From { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public string UserName { get; set; } = string.Empty;

    public string PickupDirectory { get; set; } = "mails";
}

public sealed record OutgoingMail(IReadOnlyList<string> To, string Subject, string HtmlBody, string? AttachmentName = null, byte[]? Attachment = null);

public interface IMailSender
{
    Task<bool> SendAsync(OutgoingMail mail, CancellationToken cancellationToken);
}

/// <summary>Envoi par SMTP (TLS) si configuré, sinon dépôt de fichiers .eml (aucun service payant nécessaire).</summary>
public sealed partial class SmtpMailSender(EmailOptions options, ILogger<SmtpMailSender> logger) : IMailSender
{
    private readonly ILogger<SmtpMailSender> _logger = logger;

    public async Task<bool> SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (mail.To.Count == 0)
        {
            return false;
        }

        var from = string.IsNullOrWhiteSpace(options.From) ? "console@pcsante.invalid" : options.From;
        using var message = new MailMessage { From = new MailAddress(from, "PC Santé"), Subject = mail.Subject, Body = mail.HtmlBody, IsBodyHtml = true };
        foreach (var to in mail.To)
        {
            message.To.Add(to);
        }

        if (mail.Attachment is { } bytes)
        {
            message.Attachments.Add(new Attachment(new MemoryStream(bytes), mail.AttachmentName ?? "piece-jointe", "text/csv"));
        }

        using var client = CreateClient();
        if (client is null)
        {
            LogNotConfigured();
            return false;
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SmtpException ex)
        {
            LogFailed(ex);
            return false;
        }
    }

    private SmtpClient? CreateClient()
    {
        if (!string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            var client = new SmtpClient(options.SmtpHost, options.SmtpPort) { EnableSsl = true, DeliveryMethod = SmtpDeliveryMethod.Network };
            var password = Environment.GetEnvironmentVariable(EmailOptions.PasswordVariable);
            if (!string.IsNullOrEmpty(options.UserName) && !string.IsNullOrEmpty(password))
            {
                client.Credentials = new NetworkCredential(options.UserName, password);
            }

            return client;
        }

        if (string.IsNullOrWhiteSpace(options.PickupDirectory))
        {
            return null;
        }

        var folder = Path.GetFullPath(options.PickupDirectory);
        Directory.CreateDirectory(folder);
        return new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = folder };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Console PME : aucun envoi d'e-mail configuré")]
    private partial void LogNotConfigured();

    [LoggerMessage(Level = LogLevel.Error, Message = "Console PME : envoi d'e-mail impossible")]
    private partial void LogFailed(Exception ex);
}
