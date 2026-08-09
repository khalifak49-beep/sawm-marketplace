using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Sawm.Web.Services;

// ── إعدادات البريد (تُقرأ من قسم "Email" في التهيئة؛ كلمة المرور من متغيّر بيئة) ──
public class EmailSettings
{
    public bool Enabled { get; set; } = false;
    /// <summary>مزوّد الإرسال: "smtp" (محلياً) أو "brevo" (HTTPS — للسحابة التي تحجب SMTP مثل Render)</summary>
    public string Provider { get; set; } = "smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 465;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>مفتاح واجهة Brevo (يُضبط عبر متغيّر بيئة، لا يُحفظ في المستودع)</summary>
    public string ApiKey { get; set; } = "";
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

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_s.FromName, _s.FromEmail));
        foreach (var to in message.To.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
            mime.To.Add(MailboxAddress.Parse(to));
        if (message.Bcc is not null)
            foreach (var bcc in message.Bcc.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
                mime.Bcc.Add(MailboxAddress.Parse(bcc));

        if (mime.To.Count == 0 && mime.Bcc.Count == 0) return;
        // بعض الخوادم ترفض رسالة بلا مستلم ظاهر — نضع المُرسِل كمستلم إن كان الإرسال للنسخ المخفية فقط
        if (mime.To.Count == 0) mime.To.Add(MailboxAddress.Parse(_s.FromEmail));

        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        // 465 → SSL ضمني عند الاتصال، 587 → STARTTLS، غير ذلك → تفاوض تلقائي
        var security = _s.Port == 465 ? SecureSocketOptions.SslOnConnect
                     : _s.Port == 587 ? SecureSocketOptions.StartTls
                     : SecureSocketOptions.Auto;

        using var client = new SmtpClient { Timeout = 20000 };
        await client.ConnectAsync(_s.Host, _s.Port, security, ct);
        await client.AuthenticateAsync(_s.User, _s.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);

        _log.LogInformation("أُرسل بريد '{Subject}' إلى {Count} مستلم عبر منفذ {Port}.",
            message.Subject, mime.To.Count + mime.Bcc.Count, _s.Port);
    }
}

// ── مُرسِل عبر Brevo HTTP API (منفذ 443/HTTPS) — يعمل حيث تُحجب منافذ SMTP ──
public class BrevoEmailSender : IEmailSender
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly EmailSettings _s;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<BrevoEmailSender> _log;

    public BrevoEmailSender(IOptions<EmailSettings> options, IHttpClientFactory http, ILogger<BrevoEmailSender> log)
    {
        _s = options.Value;
        _http = http;
        _log = log;
    }

    public bool IsConfigured =>
        _s.Enabled && !string.IsNullOrWhiteSpace(_s.ApiKey) && !string.IsNullOrWhiteSpace(_s.FromEmail);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("تخطّي إرسال بريد '{Subject}': مفتاح Brevo غير مضبوط.", message.Subject);
            return;
        }

        var to = message.To.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct()
            .Select(a => new BrevoAddr(a)).ToList();
        var bcc = message.Bcc?.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct()
            .Select(a => new BrevoAddr(a)).ToList();

        if (to.Count == 0 && (bcc is null || bcc.Count == 0)) return;
        if (to.Count == 0) to.Add(new BrevoAddr(_s.FromEmail));

        var payload = new
        {
            sender = new { name = _s.FromName, email = _s.FromEmail },
            to,
            bcc = (bcc is { Count: > 0 }) ? bcc : null,
            subject = message.Subject,
            htmlContent = message.HtmlBody
        };

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(25);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.TryAddWithoutValidation("api-key", _s.ApiKey);
        req.Headers.TryAddWithoutValidation("accept", "application/json");
        req.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(payload, JsonOpts),
            System.Text.Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Brevo API {(int)resp.StatusCode}: {body}");
        }
        _log.LogInformation("أُرسل بريد '{Subject}' عبر Brevo إلى {Count} مستلم.",
            message.Subject, to.Count + (bcc?.Count ?? 0));
    }

    private sealed record BrevoAddr(string email);
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
