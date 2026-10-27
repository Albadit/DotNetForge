using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNetForge.Infrastructure.Messaging;
using DotNetForge.Shared.Configuration;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// The connection test of Email → Configuration (<see cref="SmtpProbe"/>) against a scripted SMTP server on a local port
/// (no TLS): every step is reported, a refused sign-in names the step, and the password is never part of the result.
/// </summary>
public sealed class SmtpProbeTests
{
    [Fact]
    public async Task A_working_server_passes_every_step_and_nothing_is_sent()
    {
        await using var server = new FakeSmtpServer(acceptPassword: "s3cret");
        var result = await SmtpProbe.RunAsync(Settings(server.Port, "s3cret"));

        Assert.True(result.Succeeded, string.Join(" | ", result.Steps.Select(s => $"{s.Name}: {s.Detail}")));
        Assert.Equal(new[] { "Connect", "Greeting", "EHLO", "Sign-in", "QUIT" }, result.Steps.Select(s => s.Name));
        Assert.Contains("AUTH PLAIN LOGIN", result.Extensions);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase) || c.StartsWith("DATA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_refused_sign_in_fails_that_step_without_echoing_the_password()
    {
        await using var server = new FakeSmtpServer(acceptPassword: "right");
        var result = await SmtpProbe.RunAsync(Settings(server.Port, "wrong-s3cret"));

        Assert.False(result.Succeeded);
        var failed = Assert.Single(result.Steps, s => !s.Ok);
        Assert.Equal("Sign-in", failed.Name);
        Assert.Contains("535", failed.Detail);
        Assert.All(result.Steps, s => Assert.DoesNotContain("wrong-s3cret", s.Detail));
    }

    [Fact]
    public async Task Tls_without_starttls_on_the_server_is_reported()
    {
        await using var server = new FakeSmtpServer(acceptPassword: "x");
        var settings = Settings(server.Port, "x");
        settings.UseTls = true;
        var result = await SmtpProbe.RunAsync(settings);

        var failed = Assert.Single(result.Steps, s => !s.Ok);
        Assert.Equal("STARTTLS", failed.Name);
        Assert.Contains("offers no STARTTLS", failed.Detail);
    }

    [Fact]
    public async Task A_closed_port_fails_the_connect_step()
    {
        var result = await SmtpProbe.RunAsync(new EmailSettings { Host = "127.0.0.1", Port = 1, UseTls = false, FromAddress = "a@example.com" });

        var step = Assert.Single(result.Steps);
        Assert.Equal("Connect", step.Name);
        Assert.False(step.Ok);
    }

    private static EmailSettings Settings(int port, string password) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        UseTls = false,
        Username = "mailer",
        Password = password,
        FromAddress = "cms@example.com",
    };

    /// <summary>Greets, answers EHLO with AUTH PLAIN LOGIN, accepts AUTH PLAIN for one password, and QUIT.</summary>
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _loop;
        private readonly string _acceptPassword;

        public FakeSmtpServer(string acceptPassword)
        {
            _acceptPassword = acceptPassword;
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<string> Commands { get; } = new();

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                async Task Send(string text) => await stream.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"));

                await Send("220 fake.local ESMTP ready");
                while (await reader.ReadLineAsync() is { } line)
                {
                    Commands.Add(line);
                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                    {
                        await Send("250-fake.local hello\r\n250-SIZE 10240000\r\n250 AUTH PLAIN LOGIN");
                    }
                    else if (line.StartsWith("AUTH PLAIN ", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(line[11..])).Split('\0');
                        await Send(parts.Length == 3 && parts[2] == _acceptPassword ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication credentials invalid");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await Send("221 bye");
                        return;
                    }
                    else
                    {
                        await Send("502 not implemented");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // The probe closed the connection.
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _loop;
        }
    }
}
