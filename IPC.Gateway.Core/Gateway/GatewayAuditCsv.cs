/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayAuditCsv
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using System.Text;

namespace IPC.Gateway.Core.Gateway;

public static class GatewayAuditCsv
{
    public const string Header = "timestamp,source,outcome,target,user,role,ip,method,path,traceId,error";

    public static string Build(IEnumerable<GatewayAuditLogEntry> entries)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(Header);
        foreach (GatewayAuditLogEntry entry in entries ?? Array.Empty<GatewayAuditLogEntry>())
        {
            builder.Append(Escape(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")));
            builder.Append(',');
            builder.Append(Escape(entry.Source));
            builder.Append(',');
            builder.Append(Escape(entry.Outcome));
            builder.Append(',');
            builder.Append(Escape(entry.Target));
            builder.Append(',');
            builder.Append(Escape(entry.UserName));
            builder.Append(',');
            builder.Append(Escape(entry.Role));
            builder.Append(',');
            builder.Append(Escape(entry.RemoteIpAddress));
            builder.Append(',');
            builder.Append(Escape(entry.Method));
            builder.Append(',');
            builder.Append(Escape(entry.Path));
            builder.Append(',');
            builder.Append(Escape(entry.TraceId));
            builder.Append(',');
            builder.Append(Escape(entry.ErrorMessage));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static byte[] BuildUtf8WithBom(IEnumerable<GatewayAuditLogEntry> entries)
    {
        byte[] bom = Encoding.UTF8.GetPreamble();
        byte[] body = Encoding.UTF8.GetBytes(Build(entries));
        byte[] payload = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, payload, 0, bom.Length);
        Buffer.BlockCopy(body, 0, payload, bom.Length, body.Length);
        return payload;
    }

    private static string Escape(string value)
    {
        string text = value ?? string.Empty;
        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
