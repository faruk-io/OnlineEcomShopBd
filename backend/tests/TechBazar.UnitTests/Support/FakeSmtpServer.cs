using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TechBazar.UnitTests.Support;

/// <summary>Minimal in-process SMTP server (no TLS, no AUTH) that records every accepted message. TEST ONLY.</summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<byte[]> _messages = [];
    private readonly Task _loop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    /// <summary>When set, RCPT TO is refused with an error that echoes the address (like many real servers).</summary>
    public bool RejectRecipients { get; set; }
    public IReadOnlyList<byte[]> Messages { get { lock (_messages) return [.. _messages]; } }

    public FakeSmtpServer()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => Serve(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task Serve(TcpClient client)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        async Task Say(string line) => await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"));
        try
        {
            await Say("220 fake.local ESMTP ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                var cmd = line.ToUpperInvariant();
                if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO")) { await Say("250-fake.local"); await Say("250 8BITMIME"); }
                else if (cmd.StartsWith("MAIL FROM")) await Say("250 2.1.0 OK");
                else if (cmd.StartsWith("RCPT TO")) await Say(RejectRecipients ? $"550 5.1.1 {line[8..].Trim()} user unknown" : "250 2.1.5 OK");
                else if (cmd == "DATA")
                {
                    await Say("354 End data with <CR><LF>.<CR><LF>");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } l && l != ".") data.Append(l.StartsWith("..") ? l[1..] : l).Append("\r\n");
                    lock (_messages) _messages.Add(Encoding.UTF8.GetBytes(data.ToString()));
                    await Say("250 2.0.0 queued");
                }
                else if (cmd == "QUIT") { await Say("221 bye"); break; }
                else await Say("250 OK");
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try { _loop.Wait(1000); } catch { /* shutting down */ }
        _stop.Dispose();
    }
}
