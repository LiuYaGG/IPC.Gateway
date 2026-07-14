using System.Collections.Generic;

namespace IPC.Plc.Communication.Core
{
    public static class PlcConnectionParameterCatalog
    {
        public static IList<PlcConnectionParameterDefinition> ForProtocol(PlcProtocol protocol)
        {
            switch (protocol)
            {
                case PlcProtocol.VirtualPlc:
                    return new List<PlcConnectionParameterDefinition>
                    {
                        Text("host", "模拟源", "default", "default"),
                        Number("timeoutMilliseconds", "超时", "3000", 100, 60000, "ms")
                    };
                case PlcProtocol.ModbusTcp:
                    return Network("127.0.0.1", "502", false, new[]
                    {
                        Number("rack", "Unit ID", "1", 1, 247, string.Empty)
                    });
                case PlcProtocol.SiemensS7:
                    return Network("127.0.0.1", "502", false, new[]
                    {
                        Number("rack", "Rack", "0", 0, 16, string.Empty),
                        Number("slot", "Slot", "1", 0, 16, string.Empty)
                    });
                case PlcProtocol.RockwellCip:
                    return Network("127.0.0.1", "44818", false);
                case PlcProtocol.MitsubishiMc:
                case PlcProtocol.MitsubishiMc1E:
                    return Network("127.0.0.1", "5001", true);
                case PlcProtocol.OmronFins:
                    return Network("127.0.0.1", "9600", true, new[]
                    {
                        Number("driverOptions.sourceNode", "源节点", "0", 0, 254, string.Empty),
                        Number("driverOptions.destinationNode", "目标节点", "0", 0, 254, string.Empty),
                        Number("driverOptions.network", "网络号", "0", 0, 127, string.Empty)
                    }, "Udp");
                case PlcProtocol.BacnetIp:
                    return Network("127.0.0.1", "47808", true, new[]
                    {
                        Number("driverOptions.localPort", "Local Port", "0", 0, 65535, string.Empty),
                        Number("driverOptions.retries", "Retries", "1", 0, 10, string.Empty),
                        Number("driverOptions.writePriority", "Write Priority", "16", 1, 16, string.Empty),
                        Number("driverOptions.maxBatchObjects", "Max Batch Objects", "16", 1, 128, string.Empty)
                    }, "Udp");
                case PlcProtocol.CanOpen:
                    return new List<PlcConnectionParameterDefinition>
                    {
                        Text("host", "CAN Adapter Port", "COM1", "COM1"),
                        Number("port", "Adapter Baud Rate", "115200", 1200, 3000000, string.Empty),
                        Number("dataBits", "Data Bits", "8", 5, 8, string.Empty),
                        Select("serialParity", "Parity", "None", new[] { "None", "Odd", "Even", "Mark", "Space" }, false),
                        Select("serialStopBits", "Stop Bits", "One", new[] { "One", "Two", "OnePointFive", "None" }, false),
                        Number("timeoutMilliseconds", "Timeout", "3000", 100, 60000, "ms"),
                        Select("driverOptions.adapter", "CAN Adapter", "SLCAN", new[] { "SLCAN" }, true),
                        Number("driverOptions.canBitRate", "CAN Bit Rate", "500000", 10000, 1000000, "bit/s"),
                        Number("driverOptions.defaultNodeId", "Default Node ID", "1", 1, 127, string.Empty),
                        Number("driverOptions.maxBatchItems", "Max Batch Items", "32", 1, 256, string.Empty)
                    };
                case PlcProtocol.ModbusRtu:
                    return Serial(new[]
                    {
                        Number("rack", "Slave ID", "1", 1, 247, string.Empty)
                    });
                case PlcProtocol.MitsubishiSerial:
                case PlcProtocol.MitsubishiQlSerial:
                case PlcProtocol.Dlt6452007:
                case PlcProtocol.Cjt1882004:
                    return Serial();
                case PlcProtocol.OpcUa:
                    return new List<PlcConnectionParameterDefinition>
                    {
                        Text("host", "Endpoint", "opc.tcp://127.0.0.1", "opc.tcp://127.0.0.1"),
                        Number("port", "端口", "49320", 0, 65535, string.Empty),
                        Select("transport", "传输", "Tcp", new[] { "Tcp" }, true),
                        Number("timeoutMilliseconds", "超时", "3000", 100, 60000, "ms"),
                        Text("username", "用户名", string.Empty, string.Empty),
                        Password("password", "密码"),
                        Select("opcUaSecurityPolicy", "安全策略", "None", new[]
                        {
                            "None",
                            "Basic128Rsa15",
                            "Basic256",
                            "Basic256Sha256",
                            "Aes128_Sha256_RsaOaep",
                            "Aes256_Sha256_RsaPss"
                        }, false),
                        Select("opcUaMessageSecurityMode", "消息安全模式", "None", new[]
                        {
                            "None",
                            "Sign",
                            "SignAndEncrypt"
                        }, false),
                        Switch("opcUaAutoTrustServerCertificate", "自动信任证书", "false")
                    };
                case PlcProtocol.OpcDa:
                    return new List<PlcConnectionParameterDefinition>
                    {
                        Text("host", "主机", "localhost", "localhost"),
                        Text("opcDaServerProgId", "Server ProgID", "Kepware.KEPServerEX.V6", string.Empty),
                        Text("opcDaGroupName", "Group", "IPC", string.Empty),
                        Number("timeoutMilliseconds", "超时", "3000", 100, 60000, "ms")
                    };
                case PlcProtocol.Plugin:
                    return Network(string.Empty, "0", true);
                default:
                    return new List<PlcConnectionParameterDefinition>();
            }
        }

        private static List<PlcConnectionParameterDefinition> Network(string host, string port, bool allowUdp)
        {
            return Network(host, port, allowUdp, new PlcConnectionParameterDefinition[0], "Tcp");
        }

        private static List<PlcConnectionParameterDefinition> Network(string host, string port, bool allowUdp, IEnumerable<PlcConnectionParameterDefinition> extra)
        {
            return Network(host, port, allowUdp, extra, "Tcp");
        }

        private static List<PlcConnectionParameterDefinition> Network(string host, string port, bool allowUdp, IEnumerable<PlcConnectionParameterDefinition> extra, string defaultTransport)
        {
            List<PlcConnectionParameterDefinition> items = new List<PlcConnectionParameterDefinition>
            {
                Text("host", "主机 / 地址", host, host),
                Number("port", "端口", port, 0, 65535, string.Empty),
                Select("transport", "传输", defaultTransport, allowUdp ? new[] { "Tcp", "Udp" } : new[] { "Tcp" }, !allowUdp),
                Number("timeoutMilliseconds", "超时", "3000", 100, 60000, "ms")
            };
            items.AddRange(extra);
            items.Add(Select("wordOrder", "字序", "HighWordFirst", new[] { "HighWordFirst", "LowWordFirst" }, false));
            return items;
        }

        private static List<PlcConnectionParameterDefinition> Serial()
        {
            return Serial(new PlcConnectionParameterDefinition[0]);
        }

        private static List<PlcConnectionParameterDefinition> Serial(IEnumerable<PlcConnectionParameterDefinition> extra)
        {
            List<PlcConnectionParameterDefinition> items = new List<PlcConnectionParameterDefinition>
            {
                Text("host", "串口", "COM1", "COM1"),
                Number("port", "波特率", "9600", 1200, 921600, string.Empty),
                Number("dataBits", "数据位", "8", 5, 8, string.Empty),
                Select("serialParity", "校验", "None", new[] { "None", "Odd", "Even", "Mark", "Space" }, false),
                Select("serialStopBits", "停止位", "One", new[] { "One", "Two", "OnePointFive", "None" }, false),
                Number("timeoutMilliseconds", "超时", "3000", 100, 60000, "ms"),
                Select("wordOrder", "字序", "HighWordFirst", new[] { "HighWordFirst", "LowWordFirst" }, false)
            };
            items.AddRange(extra);
            return items;
        }

        private static PlcConnectionParameterDefinition Text(string key, string label, string defaultValue, string placeholder)
        {
            return new PlcConnectionParameterDefinition
            {
                Key = key,
                Label = label,
                ParameterType = "text",
                DefaultValue = defaultValue,
                Placeholder = placeholder
            };
        }

        private static PlcConnectionParameterDefinition Password(string key, string label)
        {
            return new PlcConnectionParameterDefinition
            {
                Key = key,
                Label = label,
                ParameterType = "password",
                Secret = true
            };
        }

        private static PlcConnectionParameterDefinition Number(string key, string label, string defaultValue, double min, double max, string unit)
        {
            return new PlcConnectionParameterDefinition
            {
                Key = key,
                Label = label,
                ParameterType = "number",
                DefaultValue = defaultValue,
                Min = min,
                Max = max,
                Unit = unit
            };
        }

        private static PlcConnectionParameterDefinition Select(string key, string label, string defaultValue, IEnumerable<string> options, bool readOnly)
        {
            return new PlcConnectionParameterDefinition
            {
                Key = key,
                Label = label,
                ParameterType = "select",
                DefaultValue = defaultValue,
                Options = new List<string>(options),
                ReadOnly = readOnly
            };
        }

        private static PlcConnectionParameterDefinition Switch(string key, string label, string defaultValue)
        {
            return new PlcConnectionParameterDefinition
            {
                Key = key,
                Label = label,
                ParameterType = "switch",
                DefaultValue = defaultValue
            };
        }
    }
}
