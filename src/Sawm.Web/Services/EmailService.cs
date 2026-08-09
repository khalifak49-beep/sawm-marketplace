using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Sawm.Web.Services;

// ── إعدادات البريد (تُقرأ من قسم "Email" في التهيئة؛ كلمة المرور من متغيّر بيئة) ──
public class EmailSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "منصة ساوم";
    public bool UseStartTls { get; set; } = true;
    /// <summary>عنوان الموقع العام لبناء روابط مطلقة في الرسائل (اختياري)</summary>
    public string BaseUrl { get; set; } = "";
}

// ── رسالة بريد في الطابور ──
public record EmailMessage(IReadOnlyList<string> To, string Subject, string HtmlBody, IReadOnlyList<string>? Bcc = null);

// ── الطابور: تُدرَج الرسائل هنا ويُرسلها العامل الخلفي دون تعطيل الطلب ──
public class EmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateUnbounded<EmailMessage>(new UnboundedChannelOptions { SingleReader = true });

    public bool Enqueue(EmailMessage message)
    {
        if (message.To.Count == 0 && (message.Bcc is null || message.Bcc.Count == 0)) return false;
        return _channel.Writer.TryWrite(message);
    }

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

// ── مُرسِل SMTP فعلي ──
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _s;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> log)
    {
        _s = options.Value;
        _log = log;
    }

    public bool IsConfigured =>
        _s.Enabled && !string.IsNullOrWhiteSpace(_s.Host) && !string.IsNullOrWhiteSpace(_s.User)
        && !string.IsNullOrWhiteSpace(_s.Password) && !string.IsNullOrWhiteSpace(_s.FromEmail);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("تخطّي إرسال بريد '{Subject}': إعدادات SMTP غير مكتملة (مفعّلة={Enabled}).", message.Subject, _s.Enabled);
            return;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_s.FromEmail, _s.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        foreach (var to in message.To.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
            mail.To.Add(to);
        if (message.Bcc is not null)
            foreach (var bcc in message.Bcc.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
                mail.Bcc.Add(bcc);

        if (mail.To.Count == 0 && mail.Bcc.Count == 0) return;
        // بعض الخوادم ترفض رسالة بلا مستلم ظاهر — نضع المُرسِل كمستلم إن كان الإرسال للنسخ المخفية فقط
        if (mail.To.Count == 0) mail.To.Add(_s.FromEmail);

        using var client = new SmtpClient(_s.Host, _s.Port)
        {
            EnableSsl = _s.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_s.User, _s.Password),
            Timeout = 20000
        };

        await client.SendMailAsync(mail, ct);
        _log.LogInformation("أُرسل بريد '{Subject}' إلى {Count} مستلم.", message.Subject, mail.To.Count + mail.Bcc.Count);
    }
}

// ── العامل الخلفي: يقرأ الطابور ويُرسل مع إعادة محاولة بسيطة ──
public class EmailQueueWorker : BackgroundService
{
    private readonly EmailQueue _queue;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailQueueWorker> _log;

    public EmailQueueWorker(EmailQueue queue, IEmailSender sender, ILogger<EmailQueueWorker> log)
    {
        _queue = queue;
        _sender = sender;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in _queue.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await _sender.SendAsync(msg, stoppingToken);
                    break;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "فشل إرسال بريد '{Subject}' (محاولة {Attempt}/3).", msg.Subject, attempt);
                    if (attempt < 3)
                        try { await Task.Delay(TimeSpan.FromSeconds(3 * attempt), stoppingToken); }
                        catch (OperationCanceledException) { return; }
                }
            }
        }
    }
}

// ── قالب بريد HTML عربي RTL بهويّة ساوم الخضراء ──
public static class EmailTemplate
{
    public static string Wrap(string heading, string bodyHtml, string? actionUrl = null, string? actionText = null)
    {
        var button = (!string.IsNullOrWhiteSpace(actionUrl) && !string.IsNullOrWhiteSpace(actionText))
            ? $@"<tr><td style=""padding:8px 0 4px;"">
                   <a href=""{actionUrl}"" style=""display:inline-block;background:#15803D;color:#ffffff;
                      text-decoration:none;font-weight:bold;padding:12px 28px;border-radius:10px;font-size:15px;"">
                      {actionText}</a></td></tr>"
            : "";

        return $@"<!DOCTYPE html>
<html dir=""rtl"" lang=""ar""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f1f5f4;font-family:'Segoe UI',Tahoma,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f1f5f4;padding:24px 12px;"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""560"" cellpadding=""0"" cellspacing=""0""
             style=""max-width:560px;width:100%;background:#ffffff;border-radius:16px;overflow:hidden;
                    box-shadow:0 6px 24px rgba(21,128,61,.08);"">
        <tr><td style=""background:linear-gradient(135deg,#166534,#15803D);padding:22px 28px;"">
          <span style=""color:#ffffff;font-size:20px;font-weight:bold;"">🌿 منصة ساوم</span>
          <span style=""color:#dcfce7;font-size:13px;""> — سوق المحاصيل الذكي</span>
        </td></tr>
        <tr><td style=""padding:28px;"">
          <h2 style=""margin:0 0 12px;color:#14532d;font-size:19px;"">{heading}</h2>
          <div style=""color:#334155;font-size:15px;line-height:1.9;"">{bodyHtml}</div>
          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"">{button}</table>
        </td></tr>
        <tr><td style=""background:#f8fafc;padding:16px 28px;border-top:1px solid #e2e8f0;
                       color:#94a3b8;font-size:12px;line-height:1.7;"">
          هذه رسالة آلية من منصة ساوم. المنصة تربط وتوثّق وتضمن رقمياً فقط.<br>
          © {DateTime.Now.Year} منصة ساوم
        </td></tr>
      </table>
    </td></tr>
  </table>
</body></html>";
    }
}
