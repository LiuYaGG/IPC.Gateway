using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.WebHost;

internal sealed class ProtocolDiagnosticReport
{
    public string Operation { get; init; } = "";
    public long ElapsedMs { get; set; }
    public string Mode { get; set; } = "Connect";
    public string Outcome { get; set; } = "Failed";
    public string Stage { get; set; } = "Connect";
    public string Error { get; set; } = "";
    public ProtocolDiagnosticItemResult[] Items { get; init; } = [];
    public List<ProtocolDiagnosticFrame> Frames { get; } = [];
    public bool FramesTruncated { get; set; }
}

internal sealed class ProtocolDiagnosticItemResult
{
    public int Index { get; init; }
    public string Address { get; init; } = "";
    public string DataType { get; init; } = "";
    public int Batch { get; set; }
    public string Status { get; set; } = "NotAttempted";
    public string Value { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Error { get; set; } = "";
    public string ReadBackValue { get; set; } = "";
    public string ReadBackStatus { get; set; } = "NotRequested";
}

internal sealed record ProtocolDiagnosticFrame(string Direction, string Hex, string Stage, int Batch, int ByteCount, bool Truncated);

internal static class ProtocolDiagnosticRunner
{
    public static bool NeedsProbe(PlcConnectionOptions c) =>
        c.Transport == NetworkTransport.Udp || c.Protocol is PlcProtocol.ModbusUdp or PlcProtocol.BacnetIp or PlcProtocol.Snmp;

    public static async Task<ProtocolDiagnosticReport> ExecuteAsync(ProtocolDiagnosticRequest request, Func<string?> checkBusAvailability,
        CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        var c = request.Connection!;
        var report = new ProtocolDiagnosticReport { Operation = request.Operation,
            Items = request.Items.Select((i, index) => new ProtocolDiagnosticItemResult
                { Index = index, Address = i.Address, DataType = i.DataType.ToString() }).ToArray() };
        int batch = 0;
        var secrets = CollectSecrets(c);
        string Safe(string? message)
        {
            string text = message ?? "";
            foreach (string secret in secrets) text = text.Replace(secret, "***", StringComparison.Ordinal);
            return text.Length > 2048 ? text[..2048] : text;
        }
        void Capture(string direction, ReadOnlyMemory<byte> bytes)
        {
            lock (report.Frames)
            {
                if (report.Frames.Count >= 64) { report.FramesTruncated = true; return; }
                int length = Math.Min(512, bytes.Length);
                report.Frames.Add(new(direction, Convert.ToHexString(bytes.Span[..length]), report.Stage, batch, bytes.Length, length < bytes.Length));
            }
        }
        CancellationTokenSource OperationDeadline()
        {
            cancellation.ThrowIfCancellationRequested();
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(c.TimeoutMilliseconds);
            return deadline;
        }

        using var trace = !request.SampleOnly && c.Protocol is PlcProtocol.ModbusTcp or PlcProtocol.ModbusUdp or PlcProtocol.SiemensS7 or PlcProtocol.MitsubishiMc
            or PlcProtocol.RockwellCip or PlcProtocol.OmronFins
            ? PlcDiagnosticTrace.Begin(Capture) : null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var client = PlcClientFactory.Create(c);
            var capabilities = PlcClientInvoker.GetCapabilities(client);
            var asyncClient = client as IAsyncPlcClient;
            bool probe = request.Operation == "connect" && NeedsProbe(c);
            if ((probe || request.Operation is "read" or "batch" || request.ReadBack && request.Operation == "write") && !capabilities.SupportsRead)
                throw new NotSupportedException("This driver does not support reads.");
            if (request.Operation == "write" && !capabilities.SupportsWrite)
                throw new NotSupportedException("This driver does not support writes.");
            string? denied = checkBusAvailability();
            if (denied != null) throw new InvalidOperationException(denied);
            using (var deadline = OperationDeadline())
            {
                if (asyncClient != null) await asyncClient.ConnectAsync(deadline.Token);
                else client.Connect();
            }
            if (!client.IsConnected) throw new IOException("The driver did not establish a connection.");

            async ValueTask<PlcReadResult> Read(ProtocolDiagnosticItem item)
            {
                using var deadline = OperationDeadline();
                return asyncClient != null
                    ? await asyncClient.ReadAsync(item.Address, item.DataType, item.ElementCount, item.ElementOffset, deadline.Token)
                    : client.Read(item.Address, item.DataType, item.ElementCount, item.ElementOffset);
            }
            void Fail(ProtocolDiagnosticItemResult row, Exception e, PlcReadFailureScope fallback)
            {
                row.Error = Safe(Message(e, c.TimeoutMilliseconds));
                row.Scope = PlcFailureClassifier.Classify(e, fallback).ToString();
                row.Status = "Failed";
            }

            if (request.Operation == "write")
            {
                report.Mode = "SingleWrite";
                report.Stage = "Write";
                batch = 1;
                var item = request.Items[0];
                var row = report.Items[0];
                row.Batch = batch;
                try
                {
                    using var deadline = OperationDeadline();
                    string? writeError = checkBusAvailability();
                    if (writeError != null) throw new InvalidOperationException(writeError);
                    if (item.DataType == PlcDataType.String && client is IPlcStringWriter stringWriter)
                        await stringWriter.WriteStringAsync(item.Address, item.ValueText!, item.ElementCount, item.ElementOffset, deadline.Token);
                    else if (asyncClient != null)
                        await asyncClient.WriteAsync(item.Address, item.DataType, item.ValueText!, item.ElementOffset, deadline.Token);
                    else client.Write(item.Address, item.DataType, item.ValueText!, item.ElementOffset);
                    row.Value = item.ValueText!;
                    row.Status = "Success";
                }
                catch (Exception e)
                {
                    Fail(row, e, PlcReadFailureScope.Tag);
                    if (e is OperationCanceledException or TimeoutException or IOException || row.Scope == "Transport")
                        row.Error += " The write outcome may be unknown. Read the address before retrying.";
                }
                if (row.Status == "Success" && request.ReadBack)
                {
                    report.Stage = "ReadBack";
                    try
                    {
                        var actual = await Read(item);
                        row.ReadBackValue = PlcValueFormatter.Format(actual.Value);
                        bool match = !actual.IsUncertain && MatchesWrite(item.ValueText!, actual.Value);
                        row.ReadBackStatus = match ? "Matched" : "Mismatch";
                        if (!match) row.Error = "The write was acknowledged, but the read-back did not confirm the requested value.";
                    }
                    catch (Exception e)
                    {
                        row.ReadBackStatus = "Failed";
                        row.Error = Safe(Message(e, c.TimeoutMilliseconds));
                    }
                }
            }
            else if (request.Operation == "batch" && capabilities.SupportsBatchRead)
            {
                report.Stage = "Read";
                report.Mode = "DriverBatch";
                int chunkSize = Math.Clamp(capabilities.MaxBatchItems, 1, 32);
                for (int start = 0; start < report.Items.Length; start += chunkSize)
                {
                    batch++;
                    var chunk = request.Items.Skip(start).Take(chunkSize).Select(i =>
                        new PlcBatchReadRequest(i.Address, i.DataType, i.ElementCount, i.ElementOffset)).ToList();
                    bool transportFailure = false;
                    using var deadline = OperationDeadline();
                    for (int j = 0; j < chunk.Count; j++) report.Items[start + j].Batch = batch;
                    try
                    {
                        var results = client is IAsyncPlcBatchReadClient asyncBatch
                            ? await asyncBatch.ReadManyAsync(chunk, deadline.Token)
                            : ((IPlcBatchReadClient)client).ReadMany(chunk);
                        for (int j = 0; j < chunk.Count; j++)
                        {
                            var row = report.Items[start + j];
                            var answer = j < results.Count ? results[j] : null;
                            if (answer?.Success == true) SetValue(row, answer.Result!);
                            else
                            {
                                row.Status = answer?.WasAttempted == true ? "Failed" : "NotAttempted";
                                row.Error = Safe(answer?.ErrorMessage ?? "The driver returned no result for this address.");
                                row.Scope = (answer?.FailureScope ?? PlcReadFailureScope.Batch).ToString();
                                transportFailure |= answer?.IsCommunicationError == true;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        for (int j = 0; j < chunk.Count; j++) Fail(report.Items[start + j], e, PlcReadFailureScope.Batch);
                        transportFailure = true;
                    }
                    if (transportFailure) break;
                }
            }
            else if (request.Operation != "connect" || probe)
            {
                report.Stage = "Read";
                report.Mode = probe ? "ProbeRead" : request.Operation == "batch" ? "Sequential" : "SingleRead";
                for (int i = 0; i < report.Items.Length; i++)
                {
                    var row = report.Items[i];
                    row.Batch = ++batch;
                    try { SetValue(row, await Read(request.Items[i])); }
                    catch (Exception e)
                    {
                        Fail(row, e, PlcReadFailureScope.Tag);
                        if (PlcBatchReadResult.IsConnectionFailureScope(PlcFailureClassifier.Classify(e, PlcReadFailureScope.Tag))) break;
                    }
                }
            }
            else if (IsTransportOnly(c.Protocol)) report.Mode = "TransportOnly";

            var problems = report.Items.Where(i => i.Status != "Success" || i.Error.Length != 0).ToList();
            if (request.Operation == "connect" && !probe) problems.Clear();
            report.Outcome = problems.Count == 0 ? "Success" : report.Items.Any(i => i.Status == "Success") ? "Partial" : "Failed";
            report.Error = problems.Select(i => i.Error).FirstOrDefault(e => e.Length != 0) ?? "";
            if (report.Outcome == "Success") report.Stage = "Complete";
        }
        catch (Exception e) { report.Error = Safe(Message(e, c.TimeoutMilliseconds)); }
        report.ElapsedMs = watch.ElapsedMilliseconds;
        return report;
    }

    private static bool IsTransportOnly(PlcProtocol protocol) => protocol is PlcProtocol.ModbusTcp or
        PlcProtocol.MitsubishiMc or PlcProtocol.MitsubishiMc1E or PlcProtocol.ModbusRtu or PlcProtocol.ModbusAscii or
        PlcProtocol.MitsubishiSerial or PlcProtocol.MitsubishiQlSerial or PlcProtocol.Dlt6452007 or PlcProtocol.Cjt1882004 or PlcProtocol.Cjt1882018;

    private static void SetValue(ProtocolDiagnosticItemResult row, PlcReadResult result)
    {
        row.Value = PlcValueFormatter.Format(result.Value);
        row.Status = result.IsUncertain ? "Failed" : "Success";
        if (result.IsUncertain) { row.Error = "The driver reported uncertain quality."; row.Scope = "Tag"; }
    }

    private static string Message(Exception error, int timeoutMs) => error is OperationCanceledException
        ? $"Operation timed out or was cancelled (configured timeout {timeoutMs} ms)."
        : string.Join(" → ", GetMessages(error).Distinct());

    private static IEnumerable<string> GetMessages(Exception? error)
    {
        for (; error != null; error = error.InnerException)
            yield return error is SocketException socket
                ? $"{socket.Message} ({socket.SocketErrorCode}, {socket.NativeErrorCode})"
                : error.Message;
    }

    private static List<string> CollectSecrets(PlcConnectionOptions c)
    {
        var secrets = new List<string>();
        if (!string.IsNullOrEmpty(c.Password)) secrets.Add(c.Password);
        if (!string.IsNullOrWhiteSpace(c.DriverOptionsJson))
        {
            using var options = JsonDocument.Parse(c.DriverOptionsJson);
            var driver = PlcDriverPluginRegistry.GetRegisteredDrivers().FirstOrDefault(d => d.Protocol == c.Protocol.ToString() &&
                (string.IsNullOrEmpty(c.DriverId) || d.DriverId.Equals(c.DriverId, StringComparison.OrdinalIgnoreCase)));
            var secretKeys = (driver?.ConnectionParameters ?? []).Where(p => p.Secret).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var p in options.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && (secretKeys.Contains(p.Name) ||
                    new[] { "password", "secret", "community", "token", "key" }.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase))) &&
                    p.Value.GetString() is { Length: > 0 } secret)
                    secrets.Add(secret);
        }
        return secrets.OrderByDescending(s => s.Length).ToList();
    }

    private static bool MatchesWrite(string expected, object? actual)
    {
        if (actual is Array array)
        {
            var tokens = expected.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length <= array.Length && tokens.Select((token, index) => MatchesWrite(token, array.GetValue(index))).All(same => same);
        }
        if (actual is string s) return expected == s;
        if (actual == null) return false;
        if (actual is bool b) return expected.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "on" or "yes" => b,
            "false" or "0" or "off" or "no" => !b,
            _ => false
        };
        try { return actual.Equals(Convert.ChangeType(expected.Trim(), actual.GetType(), CultureInfo.InvariantCulture)); }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException) { return false; }
    }
}
