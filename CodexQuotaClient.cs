using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Transport only: PowerShell interprets the account quota windows.
public sealed class CodexQuotaClient : IDisposable {
    readonly object gate = new object();
    Process active;
    bool disposed;
    public Task<string> ReadAsync(string executable) {
        return Task.Run(() => Read(executable));
    }
    string Read(string executable) {
        var p = new Process();
        p.StartInfo = new ProcessStartInfo(executable, "app-server --listen stdio://") {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        lock (gate) {
            if (disposed) throw new ObjectDisposedException("CodexQuotaClient");
            if (active != null) throw new InvalidOperationException("额度查询正在进行");
            p.Start(); active = p;
        }
        // Drain stderr to prevent a full pipe from blocking the server. Never expose tokens/logs.
        p.StandardError.ReadToEndAsync();
        var clock = Stopwatch.StartNew();
        var json = new JavaScriptSerializer();
        try {
            p.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_reset\",\"version\":\"1.1.0\"}}}");
            p.StandardInput.Flush();
            bool initialized = false;
            while (clock.ElapsedMilliseconds < 25000) {
                var lineTask = p.StandardOutput.ReadLineAsync();
                int wait = Math.Max(1, 25000 - (int)clock.ElapsedMilliseconds);
                if (!lineTask.Wait(wait)) throw new TimeoutException("额度查询超时");
                var line = lineTask.Result;
                if (line == null) throw new InvalidOperationException("Codex 查询进程已关闭");
                var message = json.Deserialize<Dictionary<string, object>>(line);
                object id;
                if (!message.TryGetValue("id", out id) || id == null) continue;
                if (Convert.ToString(id) == "1" && !initialized) {
                    if (message.ContainsKey("error")) throw new InvalidOperationException("Codex 初始化失败，请检查版本");
                    initialized = true;
                    p.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                    p.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                    p.StandardInput.Flush();
                } else if (Convert.ToString(id) == "2" && initialized) {
                    return line;
                }
            }
            throw new TimeoutException("额度查询超时");
        } finally {
            lock (gate) {
                try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
                p.Dispose(); if (active == p) active = null;
            }
        }
    }
    public void Dispose() {
        lock (gate) {
            disposed = true;
            try { if (active != null && !active.HasExited) active.Kill(); } catch (InvalidOperationException) { }
        }
    }
}
