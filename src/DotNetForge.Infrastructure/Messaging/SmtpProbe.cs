using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using DotNetForge.Shared.Configuration;

namespace DotNetForge.Infrastructure.Messaging;

/// <summary>One step of a connection test: what was checked, whether it worked, and how long it took.</summary>
public sealed record SmtpProbeStep(string Name, bool Ok, string Detail, TimeSpan Elapsed);

/// <summary>The outcome of <see cref="SmtpProbe.RunAsync"/>.</summary>
public sealed record SmtpProbeResult(IReadOnlyList<SmtpProbeStep> Steps, IReadOnlyList<string> Extensions)
{
    public bool Succeeded => Steps.Count > 0 && Steps.All(s => s.Ok);
}

/// <summary>
/// Tests an SMTP connection without sending anything (.docs/pages/email-configuration.md#test-connection): connect,
/// greeting, EHLO, STARTTLS (when TLS is on; the certificate must be valid), sign-in (when a username is set; AUTH PLAIN
/// or LOGIN), QUIT. The details are the reply codes and this class's own words - never the server's text (it can echo
/// addresses) and never the password.
/// </summary>
public static class SmtpProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<SmtpProbeResult> RunAsync(EmailSettings settings, CancellationToken cancellationToken = default)
    {
        var steps = new List<SmtpProbeStep>();
        var extensions = new List<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var ct = timeout.Token;
        var clock = Stopwatch.StartNew();

        bool Step(string name, bool ok, string detail)
        {
            steps.Add(new SmtpProbeStep(name, ok, detail, clock.Elapsed));
            clock.Restart();
            return ok;
        }

        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(settings.Host!, settings.Port, ct);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Step("Connect", false, ex is OperationCanceledException
                ? $"{settings.Host}:{settings.Port} did not answer within {Timeout.TotalSeconds:0} seconds."
                : $"Could not connect to {settings.Host}:{settings.Port} ({((SocketException)ex).SocketErrorCode}).");
            return new SmtpProbeResult(steps, extensions);
        }

        Step("Connect", true, $"Connected to {settings.Host}:{settings.Port}.");
        Stream stream = tcp.GetStream();
        var session = new Session(stream);
        try
        {
            var greeting = await session.ReadAsync(ct);
            if (!Step("Greeting", greeting.Code == 220, greeting.Code == 220
                    ? "The server is ready (220)."
                    : $"Unexpected greeting ({greeting.Code}) - is this an SMTP port? Port 465 (implicit TLS) is not supported."))
            {
                return new SmtpProbeResult(steps, extensions);
            }

            var ehlo = await session.CommandAsync("EHLO dotnetforge", ct);
            if (!Step("EHLO", ehlo.Code == 250, ehlo.Code == 250 ? "The server introduced itself (250)." : $"EHLO was refused ({ehlo.Code})."))
            {
                return new SmtpProbeResult(steps, extensions);
            }

            extensions.AddRange(ehlo.Extensions);
            if (settings.UseTls)
            {
                if (!Has(extensions, "STARTTLS"))
                {
                    Step("STARTTLS", false, "TLS is on, but the server offers no STARTTLS. Turn TLS off (SMTP_TLS=false) only for a local test server.");
                    return new SmtpProbeResult(steps, extensions);
                }

                var start = await session.CommandAsync("STARTTLS", ct);
                if (start.Code != 220)
                {
                    Step("STARTTLS", false, $"STARTTLS was refused ({start.Code}).");
                    return new SmtpProbeResult(steps, extensions);
                }

                var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                try
                {
                    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = settings.Host }, ct);
                }
                catch (Exception ex) when (ex is System.Security.Authentication.AuthenticationException or IOException)
                {
                    Step("STARTTLS", false, "The TLS handshake failed - the server's certificate is not valid for this host, or TLS is broken.");
                    return new SmtpProbeResult(steps, extensions);
                }

                Step("STARTTLS", true, $"Encrypted ({tls.SslProtocol}).");
                session = new Session(tls);
                var again = await session.CommandAsync("EHLO dotnetforge", ct);
                extensions.Clear();
                extensions.AddRange(again.Extensions);
            }

            if (!string.IsNullOrEmpty(settings.Username))
            {
                var auth = extensions.FirstOrDefault(e => e.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
                var mechanisms = auth.Split(' ', '=').Skip(1).Select(m => m.ToUpperInvariant()).ToHashSet();
                SmtpReply reply;
                if (mechanisms.Contains("PLAIN"))
                {
                    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{settings.Username}\0{settings.Password}"));
                    reply = await session.CommandAsync("AUTH PLAIN " + token, ct);
                }
                else if (mechanisms.Contains("LOGIN"))
                {
                    reply = await session.CommandAsync("AUTH LOGIN", ct);
                    if (reply.Code == 334)
                    {
                        reply = await session.CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Username)), ct);
                    }

                    if (reply.Code == 334)
                    {
                        reply = await session.CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Password ?? string.Empty)), ct);
                    }
                }
                else
                {
                    Step("Sign-in", false, mechanisms.Count == 0
                        ? "A username is set, but the server offers no sign-in (AUTH) on this connection."
                        : $"The server offers no sign-in method this app supports ({string.Join(", ", mechanisms)}; needs PLAIN or LOGIN).");
                    return new SmtpProbeResult(steps, extensions);
                }

                if (!Step("Sign-in", reply.Code == 235, reply.Code == 235
                        ? $"Signed in as {settings.Username} (235)."
                        : $"The server refused the sign-in ({reply.Code}). Check SMTP_USERNAME and SMTP_PASSWORD."))
                {
                    return new SmtpProbeResult(steps, extensions);
                }
            }

            var quit = await session.CommandAsync("QUIT", ct);
            Step("QUIT", quit.Code == 221, quit.Code == 221 ? "Closed - nothing was sent." : $"QUIT answered {quit.Code} - nothing was sent.");
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Step("Connection", false, ex is OperationCanceledException
                ? $"The server stopped answering (no reply within {Timeout.TotalSeconds:0} seconds)."
                : "The server closed the connection.");
        }

        return new SmtpProbeResult(steps, extensions);
    }

    private static bool Has(IEnumerable<string> extensions, string name) =>
        extensions.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase) || e.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));

    private sealed record SmtpReply(int Code, IReadOnlyList<string> Extensions);

    /// <summary>Line-based SMTP over a stream; replies may span lines ("250-..." until "250 ...").</summary>
    private sealed class Session(Stream stream)
    {
        private readonly StreamReader _reader = new(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        public async Task<SmtpReply> CommandAsync(string command, CancellationToken ct)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\r\n"), ct);
            await stream.FlushAsync(ct);
            return await ReadAsync(ct);
        }

        public async Task<SmtpReply> ReadAsync(CancellationToken ct)
        {
            var lines = new List<string>();
            while (true)
            {
                var line = await _reader.ReadLineAsync(ct) ?? throw new IOException("The server closed the connection.");
                lines.Add(line);
                if (line.Length < 4 || line[3] != '-')
                {
                    break;
                }
            }

            var code = int.TryParse(lines[^1].AsSpan(0, Math.Min(3, lines[^1].Length)), out var parsed) ? parsed : 0;
            // EHLO: the first line greets, the rest are the extensions ("STARTTLS", "AUTH PLAIN LOGIN", "SIZE 0", ...).
            var extensions = lines.Skip(1).Select(l => l.Length > 4 ? l[4..].Trim() : string.Empty).Where(e => e.Length > 0).ToList();
            return new SmtpReply(code, extensions);
        }
    }
}
