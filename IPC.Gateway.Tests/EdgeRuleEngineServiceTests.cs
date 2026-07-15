/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：EdgeRuleEngineServiceTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using IPC.EdgeGateway;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IPC.Gateway.Tests;

public sealed class EdgeRuleEngineServiceTests
{
    [Fact]
    public void ThresholdRule_TriggersAndClears()
    {
        EdgeRuleConfig rule = ThresholdRule();
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 11D);

        EdgeRuleEngineStatus active = harness.Engine.GetStatus();
        Assert.Equal(1, active.TriggeredCount);
        Assert.Equal(1, active.ActiveRuleCount);
        Assert.True(active.Rules.Single().IsActive);
        Assert.Equal("High", active.Rules.Single().ActiveState);

        harness.Raise("Pressure", 5D);

        EdgeRuleEngineStatus cleared = harness.Engine.GetStatus();
        Assert.Equal(1, cleared.ClearedCount);
        Assert.Equal(0, cleared.ActiveRuleCount);
        Assert.False(cleared.Rules.Single().IsActive);
        Assert.Equal(2, harness.Published.Count);
    }

    [Fact]
    public void DurationRule_RequiresConditionToRemainActive()
    {
        EdgeRuleConfig rule = ThresholdRule();
        rule.DurationSeconds = 1;
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 11D);
        Assert.Equal(0, harness.Engine.GetStatus().TriggeredCount);

        Assert.True(SpinWait.SpinUntil(
            () => harness.Engine.GetStatus().TriggeredCount == 1,
            TimeSpan.FromSeconds(2)));

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);
        Assert.True(status.Rules.Single().IsActive);
    }

    [Fact]
    public void DeadbandRule_TriggersWhenDeltaReachesDeadband()
    {
        EdgeRuleConfig rule = SourceRule(EdgeRuleConditionType.Deadband);
        rule.Deadband = 5D;
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 10D);
        harness.Raise("Pressure", 13D);
        Assert.Equal(0, harness.Engine.GetStatus().TriggeredCount);

        harness.Raise("Pressure", 16D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);
        Assert.Equal("Deadband", status.RecentEvents.Single().State);
    }

    [Fact]
    public void RateOfChangeRule_TriggersAndClearsByRate()
    {
        EdgeRuleConfig rule = SourceRule(EdgeRuleConditionType.RateOfChange);
        rule.RateLimitPerSecond = 10D;
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);
        DateTime start = DateTime.UtcNow;

        harness.Raise("Pressure", 10D, start);
        harness.Raise("Pressure", 25D, start.AddSeconds(1));

        EdgeRuleEngineStatus active = harness.Engine.GetStatus();
        Assert.Equal(1, active.TriggeredCount);
        Assert.True(active.Rules.Single().IsActive);

        harness.Raise("Pressure", 30D, start.AddSeconds(2));

        EdgeRuleEngineStatus cleared = harness.Engine.GetStatus();
        Assert.Equal(1, cleared.ClearedCount);
        Assert.False(cleared.Rules.Single().IsActive);
    }

    [Fact]
    public void CombinationRule_AndRequiresAllConditions()
    {
        EdgeRuleConfig rule = CombinationRule(EdgeRuleLogicalOperator.And);
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 20D);
        Assert.Equal(0, harness.Engine.GetStatus().TriggeredCount);

        harness.Raise("Temperature", 4D);

        EdgeRuleEngineStatus active = harness.Engine.GetStatus();
        Assert.Equal(1, active.TriggeredCount);
        Assert.True(active.Rules.Single().IsActive);

        harness.Raise("Temperature", 8D);

        EdgeRuleEngineStatus cleared = harness.Engine.GetStatus();
        Assert.Equal(1, cleared.ClearedCount);
        Assert.False(cleared.Rules.Single().IsActive);
    }

    [Fact]
    public void CombinationRule_OrTriggersWhenAnyConditionMatches()
    {
        EdgeRuleConfig rule = CombinationRule(EdgeRuleLogicalOperator.Or);
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 1D);
        Assert.Equal(0, harness.Engine.GetStatus().TriggeredCount);

        harness.Raise("Temperature", 4D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);
        Assert.True(status.Rules.Single().IsActive);
    }

    [Fact]
    public void AnomalyZScore_DetectsDeviationFromZeroVarianceBaseline()
    {
        EdgeRuleConfig rule = SourceRule(EdgeRuleConditionType.AnomalyDetection);
        rule.AnomalyMode = "ZScore";
        rule.AnomalyThreshold = 3D;
        rule.AnomalyBaselineWindowSeconds = 60;
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 10D);
        harness.Raise("Pressure", 10D);
        harness.Raise("Pressure", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("AnomalyZScore", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void MultiLevelAlarm_UsesHighestSeverityAndCustomMessage()
    {
        EdgeRuleConfig rule = SourceRule(EdgeRuleConditionType.MultiLevelAlarm);
        rule.AlarmLevels.Add(new EdgeRuleAlarmLevelConfig
        {
            Name = "CriticalHigh",
            Severity = "Critical",
            Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual,
            CompareValue = 20D,
            Message = "Critical pressure"
        });
        rule.AlarmLevels.Add(new EdgeRuleAlarmLevelConfig
        {
            Name = "WarningHigh",
            Severity = "Warning",
            Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual,
            CompareValue = 10D,
            Message = "Warning pressure"
        });
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 25D);

        EdgeRuleRuntimeEvent ruleEvent = harness.Engine.GetStatus().RecentEvents.Single();
        Assert.Equal("Critical", ruleEvent.Severity);
        Assert.Equal("Critical pressure", ruleEvent.Message);
        Assert.Equal("CriticalHigh", ruleEvent.State);
    }

    [Fact]
    public void BrokenExpressionRule_DoesNotBlockHealthyRule()
    {
        EdgeRuleConfig broken = SourceRule(EdgeRuleConditionType.Expression);
        broken.Name = "Broken expression";
        broken.Expression = "{missing-tag} > 0";
        EdgeRuleConfig healthy = ThresholdRule();
        healthy.Name = "Healthy threshold";
        using RuleEngineHarness harness = RuleEngineHarness.Start(broken, healthy);

        harness.Raise("Pressure", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.FailedEvaluationCount);
        Assert.Equal(1, status.TriggeredCount);
        Assert.True(status.Rules.Single(item => item.RuleName == "Healthy threshold").IsActive);
    }

    [Fact]
    public void MqttPublishSwitches_AreRespected()
    {
        EdgeRuleConfig noPublishRule = ThresholdRule();
        noPublishRule.PublishToMqtt = false;
        using RuleEngineHarness noPublishHarness = RuleEngineHarness.Start(noPublishRule);

        noPublishHarness.Raise("Pressure", 11D);

        Assert.Equal(1, noPublishHarness.Engine.GetStatus().TriggeredCount);
        Assert.Empty(noPublishHarness.Published);

        EdgeRuleConfig noClearPublishRule = ThresholdRule();
        noClearPublishRule.PublishToMqtt = true;
        noClearPublishRule.PublishOnClear = false;
        using RuleEngineHarness noClearPublishHarness = RuleEngineHarness.Start(noClearPublishRule);

        noClearPublishHarness.Raise("Pressure", 11D);
        noClearPublishHarness.Raise("Pressure", 5D);

        Assert.Equal(1, noClearPublishHarness.Engine.GetStatus().ClearedCount);
        Assert.Single(noClearPublishHarness.Published);
    }

    [Fact]
    public void MqttQos2_IsPreserved()
    {
        EdgeRuleConfig rule = ThresholdRule();
        rule.PublishQos = 2;
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 11D);

        Assert.Single(harness.Published);
        Assert.Equal(2, harness.Published[0].Qos);
    }

    [Fact]
    public void CombinationRule_DoesNotUseStaleRelatedSnapshot()
    {
        EdgeRuleConfig rule = CombinationRule(EdgeRuleLogicalOperator.And);
        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);
        DateTime now = DateTime.Now;

        harness.Raise("Pressure", 20D, now.AddMinutes(-10));
        harness.Raise("Temperature", 4D, now);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(0, status.TriggeredCount);
        Assert.False(status.Rules.Single().IsActive);
    }

    [Fact]
    public void NullRulesAndActions_AreIgnoredInRuntimeStatusAndDispatch()
    {
        ProjectConfig project = new ProjectConfig();
        project.Rules.Add(null!);
        EdgeRuleConfig rule = ThresholdRule();
        rule.PublishToMqtt = false;
        rule.Actions.Add(null!);
        project.Rules.Add(rule);
        using RuleEngineHarness harness = RuleEngineHarness.Start(project);

        EdgeRuleEngineStatus initial = harness.Engine.GetStatus();
        harness.Raise("Pressure", 11D);
        EdgeRuleEngineStatus active = harness.Engine.GetStatus();

        Assert.Equal(1, initial.RuleCount);
        Assert.Single(initial.Rules);
        Assert.Equal(1, active.TriggeredCount);
        Assert.Equal(0, active.FailedEvaluationCount);
    }

    [Fact]
    public async Task WebhookAction_SendsTemplatedRequestWithHeaders()
    {
        using WebhookTestServer server = WebhookTestServer.Start();
        EdgeRuleConfig rule = ThresholdRule();
        rule.Name = "HighPressure";
        rule.ActiveMessage = "Pressure \"critical\"\nline";
        rule.PublishToMqtt = false;
        rule.Actions.Add(new EdgeRuleActionConfig
        {
            ActionType = FlowRuleNodeTypes.WebhookCall,
            ExecuteOnClear = false,
            WebhookUrl = server.Url + "?state={state}",
            WebhookMethod = "PUT",
            WebhookHeaders = "X-Rule: {ruleName}\nAccept: application/json\nContent-Type: application/vnd.ipc.rule+json",
            WebhookBodyTemplate = "{\"rule\":\"{ruleName}\",\"state\":\"{state}\",\"message\":\"{message}\",\"value\":{value},\"point\":\"{pointCode}\"}",
            WebhookContentType = "application/json",
            WebhookTimeoutSeconds = 3
        });

        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 11D);
        WebhookRequest request = await server.RequestTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("PUT", request.Method);
        Assert.Equal("/webhook?state=High", request.Path);
        Assert.Equal("HighPressure", request.Headers["X-Rule"]);
        Assert.Equal("application/json", request.Headers["Accept"]);
        Assert.Equal("application/vnd.ipc.rule+json", request.Headers["Content-Type"]);
        Assert.True(request.Headers.ContainsKey("X-IPC-Rule-Event-Id"));
        Assert.Contains("\"value\":11", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"point\":\"Boiler.Main.Pressure\"", request.Body, StringComparison.Ordinal);
        using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(request.Body);
        Assert.Equal("Pressure \"critical\"\nline", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WebhookAction_RecordsHttpFailureStatus()
    {
        using WebhookTestServer server = WebhookTestServer.Start(500);
        EdgeRuleConfig rule = ThresholdRule();
        rule.PublishToMqtt = false;
        rule.Actions.Add(new EdgeRuleActionConfig
        {
            ActionType = FlowRuleNodeTypes.WebhookCall,
            ExecuteOnClear = false,
            WebhookUrl = server.Url,
            WebhookTimeoutSeconds = 3
        });

        using RuleEngineHarness harness = RuleEngineHarness.Start(rule);

        harness.Raise("Pressure", 11D);
        await server.RequestTask.WaitAsync(TimeSpan.FromSeconds(5));
        EdgeRuleEngineStatus status = await WaitForStatusAsync(
            harness.Engine,
            current => current.ActionFailureCount == 1);

        Assert.Equal(0, status.FailedEvaluationCount);
        Assert.Equal(1, status.ActionFailureCount);
        Assert.Contains("Webhook returned HTTP 500", status.LastError, StringComparison.Ordinal);
    }

    private static EdgeRuleConfig ThresholdRule()
    {
        EdgeRuleConfig rule = SourceRule(EdgeRuleConditionType.Threshold);
        rule.LowLimit = 0D;
        rule.HighLimit = 10D;
        return rule;
    }

    private static EdgeRuleConfig SourceRule(EdgeRuleConditionType conditionType)
    {
        return new EdgeRuleConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = conditionType + " rule",
            ConditionType = conditionType,
            SourceChannelId = "channel:test",
            SourceChannelName = "Test Channel",
            SourceDeviceId = "device:boiler",
            SourceGroupId = "group:main",
            SourceTagId = "tag:boiler/main/pressure",
            SourceDeviceName = "Boiler",
            SourceGroupName = "Main",
            SourceTagName = "Pressure",
            SourceDataType = "Double",
            PublishToMqtt = true,
            PublishOnClear = true,
            PublishQos = 1
        };
    }

    private static EdgeRuleConfig CombinationRule(EdgeRuleLogicalOperator logicalOperator)
    {
        return new EdgeRuleConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = logicalOperator + " combination",
            ConditionType = EdgeRuleConditionType.Combination,
            LogicalOperator = logicalOperator,
            PublishToMqtt = true,
            PublishOnClear = true,
            Conditions =
            {
                new EdgeRuleConditionConfig
                {
                    SourceChannelId = "channel:test",
                    SourceDeviceId = "device:boiler",
                    SourceGroupId = "group:main",
                    SourceTagId = "tag:boiler/main/pressure",
                    SourceDeviceName = "Boiler",
                    SourceGroupName = "Main",
                    SourceTagName = "Pressure",
                    SourceDataType = "Double",
                    Operator = EdgeRuleComparisonOperator.GreaterThan,
                    CompareValue = 10D
                },
                new EdgeRuleConditionConfig
                {
                    SourceChannelId = "channel:test",
                    SourceDeviceId = "device:boiler",
                    SourceGroupId = "group:main",
                    SourceTagId = "tag:boiler/main/temperature",
                    SourceDeviceName = "Boiler",
                    SourceGroupName = "Main",
                    SourceTagName = "Temperature",
                    SourceDataType = "Double",
                    Operator = EdgeRuleComparisonOperator.LessThan,
                    CompareValue = 5D
                }
            }
        };
    }

    private static async Task<EdgeRuleEngineStatus> WaitForStatusAsync(
        EdgeRuleEngineService engine,
        Func<EdgeRuleEngineStatus, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        EdgeRuleEngineStatus status;
        do
        {
            status = engine.GetStatus();
            if (predicate(status))
                return status;

            await Task.Delay(25);
        }
        while (DateTime.UtcNow < deadline);

        return engine.GetStatus();
    }

    private sealed class RuleEngineHarness : IDisposable
    {
        private readonly FakeRuntimeService _runtime;

        private RuleEngineHarness(FakeRuntimeService runtime, EdgeRuleEngineService engine, List<PublishedMessage> published)
        {
            _runtime = runtime;
            Engine = engine;
            Published = published;
        }

        public EdgeRuleEngineService Engine { get; }
        public List<PublishedMessage> Published { get; }

        public static RuleEngineHarness Start(params EdgeRuleConfig[] rules)
        {
            ProjectConfig project = new ProjectConfig();
            project.Rules.AddRange(rules);
            return Start(project);
        }

        public static RuleEngineHarness Start(ProjectConfig project)
        {
            FakeRuntimeService runtime = new FakeRuntimeService();
            List<PublishedMessage> published = new List<PublishedMessage>();
            EdgeRuleEngineService engine = new EdgeRuleEngineService(
                runtime,
                project ?? new ProjectConfig(),
                (topic, payload, qos) =>
                {
                    published.Add(new PublishedMessage(topic, payload, qos));
                    return true;
                },
                new MqttGatewayOptions
                {
                    GatewayId = "test-gateway",
                    GatewayName = "Test Gateway",
                    CloudProtocolVersion = "test.v1"
                });

            engine.Start();
            return new RuleEngineHarness(runtime, engine, published);
        }

        public void Raise(string tagName, double value)
        {
            Raise(tagName, value, DateTime.Now);
        }

        public void Raise(string tagName, double value, DateTime timestamp)
        {
            _runtime.Raise(new TagValueSnapshot
            {
                ChannelId = "channel:test",
                ChannelName = "Test Channel",
                DeviceId = "device:boiler",
                GroupId = "group:main",
                TagId = "tag:boiler/main/" + tagName.ToLowerInvariant(),
                DeviceName = "Boiler",
                GroupName = "Main",
                TagName = tagName,
                PointCode = "Boiler.Main." + tagName,
                DataType = "Double",
                RawValue = value,
                RawValueText = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                Value = value,
                ValueText = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                Quality = TagQuality.Good,
                Timestamp = timestamp
            });
        }

        public void Dispose()
        {
            Engine.Dispose();
        }
    }

    private sealed record PublishedMessage(string Topic, string Payload, int Qos);

    private sealed record WebhookRequest(
        string Method,
        string Path,
        Dictionary<string, string> Headers,
        string Body);

    private sealed class WebhookTestServer : IDisposable
    {
        private readonly TcpListener _listener;
        private bool _disposed;

        private WebhookTestServer(int statusCode)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/webhook";
            RequestTask = Task.Run(() => AcceptOnce(statusCode));
        }

        public string Url { get; }
        public Task<WebhookRequest> RequestTask { get; }

        public static WebhookTestServer Start(int statusCode = 204)
        {
            return new WebhookTestServer(statusCode);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _listener.Stop();
            try
            {
                RequestTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
                
            }
        }

        private WebhookRequest AcceptOnce(int statusCode)
        {
            using TcpClient client = _listener.AcceptTcpClient();
            using NetworkStream stream = client.GetStream();
            WebhookRequest request = ReadRequest(stream);
            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " +
                statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " " +
                GetReasonPhrase(statusCode) +
                "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            stream.Write(response, 0, response.Length);
            return request;
        }

        private static WebhookRequest ReadRequest(NetworkStream stream)
        {
            List<byte> received = new List<byte>();
            byte[] buffer = new byte[1024];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;

                for (int i = 0; i < read; i++)
                    received.Add(buffer[i]);
                headerEnd = FindHeaderEnd(received);
            }

            if (headerEnd < 0)
                throw new InvalidOperationException("HTTP request headers were not complete.");

            string headerText = Encoding.ASCII.GetString(received.Take(headerEnd).ToArray());
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int separator = lines[i].IndexOf(':');
                if (separator <= 0)
                    continue;

                headers[lines[i].Substring(0, separator).Trim()] = lines[i].Substring(separator + 1).Trim();
            }

            int contentLength = 0;
            if (headers.TryGetValue("Content-Length", out string? contentLengthText))
                int.TryParse(contentLengthText, out contentLength);

            using MemoryStream body = new MemoryStream();
            int bodyStart = headerEnd + 4;
            int bufferedBodyLength = Math.Min(received.Count - bodyStart, contentLength);
            if (bufferedBodyLength > 0)
                body.Write(received.Skip(bodyStart).Take(bufferedBodyLength).ToArray());

            while (body.Length < contentLength)
            {
                int read = stream.Read(buffer, 0, Math.Min(buffer.Length, contentLength - (int)body.Length));
                if (read == 0)
                    break;
                body.Write(buffer, 0, read);
            }

            return new WebhookRequest(
                requestLine.Length > 0 ? requestLine[0] : string.Empty,
                requestLine.Length > 1 ? requestLine[1] : string.Empty,
                headers,
                Encoding.UTF8.GetString(body.ToArray()));
        }

        private static int FindHeaderEnd(List<byte> bytes)
        {
            for (int i = 0; i <= bytes.Count - 4; i++)
            {
                if (bytes[i] == '\r' &&
                    bytes[i + 1] == '\n' &&
                    bytes[i + 2] == '\r' &&
                    bytes[i + 3] == '\n')
                    return i;
            }

            return -1;
        }

        private static string GetReasonPhrase(int statusCode)
        {
            return statusCode >= 400 ? "Error" : "OK";
        }
    }

    private sealed class FakeRuntimeService : IRuntimeService
    {
        public event EventHandler<TagValueChangedEventArgs>? TagValueChanged;

        public bool IsRunning { get; private set; }
        public int MaxConcurrentDevicePolls => 1;

        public void Start(ProjectConfig config)
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Raise(TagValueSnapshot snapshot)
        {
            TagValueChanged?.Invoke(this, new TagValueChangedEventArgs(snapshot));
        }

        public IList<TagValueSnapshot> GetSnapshots() => new List<TagValueSnapshot>();
        public void RestoreSnapshots(IList<TagValueSnapshot> snapshots) { }
        public IList<DeviceRuntimeStatus> GetDeviceStatuses() => new List<DeviceRuntimeStatus>();
        public RuntimeSchedulerStatus GetSchedulerStatus() => new RuntimeSchedulerStatus();
        public IList<RuntimeErrorDetail> GetRecentErrors(int maxCount) => new List<RuntimeErrorDetail>();
        public ReadTagResponse ReadCached(ReadTagRequest request) => new ReadTagResponse();
        public ReadTagsResponse ReadCached(ReadTagsRequest request) => new ReadTagsResponse();
        public ReadTagsResponse QueryCached(ReadTagRequest request) => new ReadTagsResponse();
        public ReadTagsResponse ReadTagByDeviceCached(string channelId, string deviceId, string tagId) => new ReadTagsResponse();
        public ReadTagsResponse ReadGroupCached(string channelId, string deviceId, string groupId) => new ReadTagsResponse();
        public WriteTagResponse WriteTag(WriteTagRequest request) => new WriteTagResponse();
        public void Dispose() { }
    }
}
