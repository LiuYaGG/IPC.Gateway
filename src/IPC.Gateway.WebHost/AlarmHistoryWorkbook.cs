using System.Globalization;
using IPC.EdgeGateway;

namespace IPC.Gateway.WebHost;

internal static class AlarmHistoryWorkbook
{
    internal static void Write(Stream destination, IReadOnlyList<GatewayAlarmRecord> rows, bool english, CancellationToken ct, TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        string[] headers = english
            ? [$"Timestamp ({timeZone.Id})", "Channel", "Device", "Group", "Tag", "Alarm source", "State transition", "Sample value", "Threshold", "Message", "Delivery status"]
            : [$"时间 ({timeZone.Id})", "通道", "设备", "分组", "标签", "报警来源", "状态变化", "触发值", "阈值", "消息", "发送状态"];
        HistoryPageWorkbook.WriteTable(destination, headers, [31, 24, 24, 24, 28, 58, 30, 18, 18, 70, 22],
            rows.Select(row => Values(row, english, timeZone)), english ? "Alarm history" : "报警记录", english, ct);
    }

    private static string?[] Values(GatewayAlarmRecord row, bool english, TimeZoneInfo timeZone)
    {
        string path = string.Join('.', new[] { row.ChannelName, row.DeviceName, row.GroupName,
            string.IsNullOrEmpty(row.TagName) ? row.TagId : row.TagName }.Where(value => !string.IsNullOrEmpty(value)));
        string source = string.Join(" · ", new[] { row.RuleName, path }.Where(value => !string.IsNullOrEmpty(value)));
        string transition = !string.IsNullOrEmpty(row.PreviousState) && !string.IsNullOrEmpty(row.State)
            ? Label(row.PreviousState, english) + " → " + Label(row.State, english)
            : Label(string.IsNullOrEmpty(row.State) ? row.EventType : row.State, english);
        return [WorkbookTimeZone.Format(row.Timestamp.ToUniversalTime(), timeZone),
            row.ChannelName, row.DeviceName, row.GroupName, row.TagName, source, transition,
            row.Value?.ToString("R", CultureInfo.InvariantCulture), row.Threshold?.ToString("R", CultureInfo.InvariantCulture),
            row.Message, Label(row.DeliveryStatus, english)];
    }

    internal static string Label(string? value, bool english)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        bool quality = value.StartsWith("Quality:", StringComparison.OrdinalIgnoreCase);
        string code = quality ? value[8..] : value;
        return code.ToLowerInvariant() switch
        {
            "normal" or "good" => english ? "Normal" : "正常",
            "highalarm" => english ? "High alarm" : "高限报警",
            "lowalarm" => english ? "Low alarm" : "低限报警",
            "highwarning" => english ? "High warning" : "高限预警",
            "lowwarning" => english ? "Low warning" : "低限预警",
            "pending" => english ? "Pending" : "待发送",
            "sent" => english ? "Sent" : "已发送",
            "local" => english ? "Recorded locally" : "仅本地记录",
            "alarm" => english ? "Alarm" : "报警",
            "warning" => english ? "Warning" : "预警",
            "recovery" or "clear" => english ? "Recovery" : "恢复",
            "active" => english ? "Triggered" : "触发",
            "high" => english ? "High limit" : "高限",
            "low" => english ? "Low limit" : "低限",
            "deadband" => english ? "Deadband" : "死区",
            "rateofchange" => english ? "Rate of change" : "变化率",
            "qualitygate" => english ? "Quality gate" : "质量门控",
            "expression" => english ? "Expression" : "表达式",
            "sequence" => english ? "Sequence / timing" : "顺序/时序",
            "nochangetimeout" => english ? "Unchanged value timeout" : "连续无变化超时",
            "unknown" => english ? "No data" : "无数据",
            "bad" => english ? "Bad" : "异常",
            "readerror" => english ? "Read error" : "读取失败",
            "notconnected" => english ? "Not connected" : "未连接",
            "disabled" => english ? "Disabled" : "停用",
            "outofrange" => english ? "Out of range" : "越界",
            "filtered" => english ? "Filtered" : "已过滤",
            "spike" => english ? "Spike" : "毛刺",
            "uncertain" => english ? "Uncertain" : "不确定",
            "notfound" => english ? "Not found" : "未找到",
            "accessdenied" => english ? "Access denied" : "拒绝访问",
            _ => code
        };
    }
}
