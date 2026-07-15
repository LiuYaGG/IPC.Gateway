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
                        Number("rack", "Unit ID", "1", 1, 247, string.Empty),
                        InGroup(Number("driverOptions.maxBatchGapPoints", "批读允许间隔", "2", 0, 64, "点"), "批读优化")
                    });
                case PlcProtocol.SiemensS7:
                    return Network("127.0.0.1", "102", false, new[]
                    {
                        InGroup(Select("driverOptions.controllerProfile", "CPU系列", "Auto", new[]
                        {
                            "Auto", "S7-200 SMART", "S7-300", "S7-400", "S7-1200", "S7-1500", "LOGO!"
                        }, false), "控制器"),
                        InGroup(Number("rack", "Rack", "0", 0, 7, string.Empty), "控制器"),
                        InGroup(Number("slot", "Slot", "1", 0, 31, string.Empty), "控制器"),
                        InGroup(Select("driverOptions.s7TsapMode", "TSAP模式", "RackSlot", new[] { "RackSlot", "Custom" }, false), "S7连接"),
                        InGroup(Select("driverOptions.s7ConnectionType", "连接类型", "PG", new[] { "PG", "OP", "Basic" }, false), "S7连接"),
                        InGroup(WithHelp(Text("driverOptions.s7LocalTsap", "Local TSAP", "0100", "例如：0100"), "仅自定义TSAP模式使用，填写4位十六进制。"), "自定义TSAP"),
                        InGroup(WithHelp(Text("driverOptions.s7RemoteTsap", "Remote TSAP", "0101", "例如：0102"), "仅自定义TSAP模式使用；S7-200 SMART、LOGO!或网关连接可能需要设置。"), "自定义TSAP"),
                        InGroup(Number("driverOptions.s7MaxItemsPerRequest", "单次最大变量数", "20", 1, 64, string.Empty), "批读优化")
                    }, "Tcp", false);
                case PlcProtocol.EtherNetIp:
                    return Network("127.0.0.1", "44818", false, new[]
                    {
                        InGroup(Select("driverOptions.controllerProfile", "设备类型", "Generic", new[] { "Generic" }, true), "EtherNet/IP 设备"),
                        InGroup(Select("driverOptions.cipRouteMode", "路由模式", "Direct", new[] { "Direct", "Slot", "Custom" }, false), "CIP 路由"),
                        InGroup(Number("slot", "CPU Slot", "0", 0, 255, string.Empty), "CIP 路由"),
                        InGroup(TextArea(
                            "driverOptions.cipRoutePath",
                            "自定义 Route Path",
                            string.Empty,
                            "例如：1,0/2,192.168.1.20",
                            "仅在经过背板或网关访问目标设备时配置。"), "CIP 路由"),
                        InGroup(Number("driverOptions.cipMaxRequestBytes", "单包最大字节", "400", 64, 4000, "byte"), "显式消息"),
                        InGroup(Number("driverOptions.cipMaxServicesPerPacket", "单包最大服务数", "16", 1, 64, string.Empty), "显式消息"),
                        InGroup(Select("driverOptions.eipIoMode", "I/O 模式", "Explicit", new[] { "Explicit", "Implicit" }, false), "周期 I/O（Class 1）"),
                        InGroup(Number("driverOptions.eipOutputAssembly", "输出 Assembly", "100", 1, 255, string.Empty), "周期 I/O（Class 1）"),
                        InGroup(Number("driverOptions.eipInputAssembly", "输入 Assembly", "101", 1, 255, string.Empty), "周期 I/O（Class 1）"),
                        InGroup(Number("driverOptions.eipConfigurationAssembly", "配置 Assembly", "1", 1, 255, string.Empty), "周期 I/O（Class 1）"),
                        InGroup(WithHelp(Number("driverOptions.eipOutputLength", "输出长度", "0", 0, 65535, "byte"), "设为 0 时通过显式消息自动探测。"), "周期 I/O（Class 1）"),
                        InGroup(WithHelp(Number("driverOptions.eipInputLength", "输入长度", "0", 0, 65535, "byte"), "设为 0 时通过显式消息自动探测。"), "周期 I/O（Class 1）"),
                        InGroup(Number("driverOptions.eipRpiMilliseconds", "RPI", "100", 1, 10000, "ms"), "周期 I/O（Class 1）"),
                        InGroup(Select("driverOptions.eipOutputRealTimeFormat", "输出实时格式", "Header32Bit", new[] { "Header32Bit", "Modeless", "Heartbeat", "ZeroLength" }, false), "周期 I/O（Class 1）"),
                        InGroup(Select("driverOptions.eipInputRealTimeFormat", "输入实时格式", "Modeless", new[] { "Modeless", "Header32Bit", "Heartbeat", "ZeroLength" }, false), "周期 I/O（Class 1）"),
                        InGroup(Select("driverOptions.eipInputConnectionType", "输入连接类型", "PointToPoint", new[] { "PointToPoint", "Multicast" }, false), "周期 I/O（Class 1）"),
                        InGroup(WithHelp(Number("driverOptions.eipInputDataOffset", "输入数据起始偏移", "8", 0, 64, "byte"), "EEIP 接收缓冲默认包含 8 字节序列/封装头；按设备数据布局调整。"), "周期 I/O 数据布局"),
                        InGroup(Number("driverOptions.eipOutputDataOffset", "输出数据起始偏移", "0", 0, 64, "byte"), "周期 I/O 数据布局"),
                        InGroup(Number("driverOptions.eipIoStaleTimeoutMilliseconds", "输入失效阈值", "1000", 100, 60000, "ms"), "周期 I/O 数据布局")
                    });
                case PlcProtocol.RockwellCip:
                    return Network("127.0.0.1", "44818", false, new[]
                    {
                          InGroup(Select("driverOptions.controllerProfile", "控制器档案", "Logix", new[] { "Logix", "Micro800", "Generic" }, false), "EtherNet/IP设备"),
                        InGroup(Number("slot", "CPU Slot", "0", 0, 255, string.Empty), "AB控制器"),
                        InGroup(Select("driverOptions.cipRouteMode", "路由模式", "Slot", new[] { "Slot", "Direct", "Custom" }, false), "CIP路由"),
                        InGroup(TextArea(
                            "driverOptions.cipRoutePath",
                            "自定义Route Path",
                            string.Empty,
                            "例如：1,0/2,192.168.1.20/1,3",
                            "每个跳点使用“端口,链路地址”，多个跳点使用“/”分隔。"), "CIP路由"),
                        InGroup(Number("driverOptions.cipMaxRequestBytes", "单包最大字节", "400", 64, 4000, "byte"), "CIP高级设置"),
                        InGroup(Number("driverOptions.cipMaxServicesPerPacket", "单包最大服务数", "16", 1, 64, string.Empty), "CIP高级设置")
                    });
                case PlcProtocol.RockwellPccc:
                    return Network("127.0.0.1", "44818", false, new[]
                    {
                        InGroup(Select("driverOptions.controllerProfile", "控制器系列", "SLC/MicroLogix", new[] { "SLC/MicroLogix", "PLC-5" }, false), "AB控制器"),
                        InGroup(Select("driverOptions.cipRouteMode", "路由模式", "Direct", new[] { "Direct", "Slot", "Custom" }, false), "CIP路由"),
                        InGroup(Number("slot", "CPU Slot", "0", 0, 255, string.Empty), "CIP路由"),
                        InGroup(TextArea(
                            "driverOptions.cipRoutePath",
                            "自定义Route Path",
                            string.Empty,
                            "例如：1,0/2,192.168.1.20",
                            "仅在通过网关或背板访问目标PLC时设置。"), "CIP路由")
                    });
                case PlcProtocol.BeckhoffAds:
                    return Network("127.0.0.1", "48898", false, new[]
                    {
                        InGroup(WithHelp(Text("driverOptions.amsNetId", "AMS NetId", string.Empty, "例如 192.168.1.20.1.1"), "留空时 IPv4 主机自动补 .1.1；本机 ADS Router 中仍需存在到目标的路由。"), "ADS 目标"),
                        InGroup(Number("driverOptions.adsPort", "ADS Port", "851", 1, 65535, string.Empty), "ADS 目标"),
                        InGroup(Number("driverOptions.adsStringLength", "默认字符串长度", "80", 1, 4096, "字符"), "ADS 数据"),
                        InGroup(Number("driverOptions.adsMaxBatchItems", "单批最大标签数", "100", 1, 500, string.Empty), "批读优化")
                    }, "Tcp", false);
                case PlcProtocol.Snmp:
                    return Network("127.0.0.1", "161", true, new[]
                    {
                        InGroup(Select("driverOptions.snmpVersion", "SNMP 版本", "V2c", new[] { "V1", "V2c", "V3" }, false), "SNMP"),
                        InGroup(Password("driverOptions.snmpCommunity", "Community"), "V1 / V2c"),
                        InGroup(Text("driverOptions.snmpUserName", "用户名", string.Empty, string.Empty), "V3 安全"),
                        InGroup(Select("driverOptions.snmpAuthProtocol", "认证算法", "None", new[] { "None", "MD5", "SHA1", "SHA256", "SHA384", "SHA512" }, false), "V3 安全"),
                        InGroup(Password("driverOptions.snmpAuthPassword", "认证密码"), "V3 安全"),
                        InGroup(Select("driverOptions.snmpPrivacyProtocol", "隐私算法", "None", new[] { "None", "DES", "3DES", "AES128", "AES192", "AES256" }, false), "V3 安全"),
                        InGroup(Password("driverOptions.snmpPrivacyPassword", "隐私密码"), "V3 安全"),
                        InGroup(Text("driverOptions.snmpContextName", "Context Name", string.Empty, string.Empty), "V3 安全"),
                        InGroup(Number("driverOptions.snmpMaxOidsPerRequest", "单包最大 OID 数", "40", 1, 100, string.Empty), "批读优化")
                    }, "Udp", false);
                case PlcProtocol.MqttClient:
                    return Network("127.0.0.1", "1883", false, new[]
                    {
                        InGroup(Text("username", "用户名", string.Empty, string.Empty), "Broker 认证"),
                        InGroup(Password("password", "密码"), "Broker 认证"),
                        InGroup(Text("driverOptions.mqttClientId", "Client ID", "IPC-Gateway-Southbound", "IPC-Gateway-Southbound"), "MQTT 订阅"),
                        InGroup(Text("driverOptions.mqttSubscribeFilter", "订阅过滤器", "#", "例如 factory/+/data/#"), "MQTT 订阅"),
                        InGroup(Select("driverOptions.mqttPayloadMode", "Payload 模式", "Text", new[] { "Text", "Json", "SparkplugB" }, false), "Payload"),
                        InGroup(Switch("driverOptions.mqttUseTls", "启用 TLS", "false"), "Broker 安全"),
                        InGroup(Switch("driverOptions.mqttAllowUntrustedCertificates", "允许不受信任证书", "false"), "Broker 安全"),
                        InGroup(Select("driverOptions.mqttQos", "写入 QoS", "0", new[] { "0", "1", "2" }, false), "MQTT 写入"),
                        InGroup(Number("driverOptions.mqttMaxValueAgeSeconds", "值最大有效期", "0", 0, 86400, "秒"), "缓存")
                    }, "Tcp", false);
                case PlcProtocol.Dnp3:
                    return Network("127.0.0.1", "20000", false, new[]
                    {
                        InGroup(Number("driverOptions.dnp3LocalAddress", "主站链路地址", "1", 0, 65535, string.Empty), "DNP3 链路"),
                        InGroup(Number("driverOptions.dnp3RemoteAddress", "从站链路地址", "1024", 0, 65535, string.Empty), "DNP3 链路"),
                        InGroup(Number("driverOptions.dnp3ScanGapLimit", "范围合并最大空洞", "32", 0, 1000, "点"), "批读优化"),
                        InGroup(Switch("driverOptions.dnp3SelectBeforeOperate", "写命令先选择后执行", "true"), "DNP3 命令")
                        , InGroup(Switch("driverOptions.dnp3StartupIntegrity", "启动完整性扫描", "true"), "DNP3 事件与缓存")
                        , InGroup(Switch("driverOptions.dnp3EnableUnsolicited", "启用非请求事件", "true"), "DNP3 事件与缓存")
                        , InGroup(Number("driverOptions.dnp3EventScanIntervalSeconds", "事件轮询周期", "5", 0, 3600, "s"), "DNP3 事件与缓存")
                        , InGroup(Number("driverOptions.dnp3IntegrityScanIntervalSeconds", "完整性扫描周期", "900", 0, 86400, "s"), "DNP3 事件与缓存")
                        , InGroup(WithHelp(Number("driverOptions.dnp3CacheMaxAgeMilliseconds", "按需缓存失效时间", "0", 0, 86400000, "ms"), "0 表示依靠事件和周期完整性扫描维护缓存，不在每次读取时重复扫描。"), "DNP3 事件与缓存")
                        , InGroup(Select("driverOptions.dnp3TimeSyncMode", "对时模式", "None", new[] { "None", "LAN", "NonLAN" }, false), "DNP3 时间同步")
                    }, "Tcp", false);
                case PlcProtocol.MitsubishiMc:
                    return Network("127.0.0.1", "5001", true, new[]
                    {
                        InGroup(Select("driverOptions.controllerProfile", "PLC系列", "Auto", new[] { "Auto", "Q/L", "iQ-R", "iQ-F/FX5" }, false), "三菱控制器"),
                        InGroup(Select("driverOptions.mcFrameType", "MC帧类型", "3E", new[] { "3E", "4E" }, false), "MC/SLMP帧"),
                        InGroup(Select("driverOptions.mcDataCode", "数据编码", "Binary", new[] { "Binary", "ASCII" }, false), "MC/SLMP帧"),
                        InGroup(Number("driverOptions.networkNumber", "网络号", "0", 0, 255, string.Empty), "目标路由"),
                        InGroup(Number("driverOptions.pcNumber", "PC号", "255", 0, 255, string.Empty), "目标路由"),
                        InGroup(Number("driverOptions.moduleIoNumber", "模块I/O号", "1023", 0, 65535, string.Empty), "目标路由"),
                        InGroup(Number("driverOptions.stationNumber", "站号", "0", 0, 255, string.Empty), "目标路由"),
                        InGroup(Number("driverOptions.mcMaxBatchGapPoints", "批读允许间隔", "2", 0, 64, "点"), "批读优化")
                    });
                case PlcProtocol.MitsubishiMc1E:
                    return Network("127.0.0.1", "5001", true, new[]
                    {
                        InGroup(Select("driverOptions.controllerProfile", "PLC系列", "A/FX3", new[] { "A/FX3", "Q兼容1E", "FX5兼容1E" }, false), "三菱控制器"),
                        InGroup(Number("driverOptions.mcMaxBatchGapPoints", "批读允许间隔", "2", 0, 64, "点"), "批读优化")
                    });
                case PlcProtocol.OmronFins:
                    return Network("127.0.0.1", "9600", true, new[]
                    {
                        InGroup(Select("driverOptions.controllerProfile", "控制器系列", "Auto", new[] { "Auto", "CS/CJ/CP", "NJ/NX" }, false), "欧姆龙控制器"),
                        InGroup(WithHelp(Number("driverOptions.sourceNode", "源节点", "0", 0, 254, string.Empty), "0 表示根据本机 IPv4 地址自动推导。"), "FINS 路由"),
                        InGroup(WithHelp(Number("driverOptions.destinationNode", "目标节点", "0", 0, 254, string.Empty), "0 表示根据 PLC IPv4 地址自动推导；跨网段或特殊路由请显式设置。"), "FINS 路由"),
                        InGroup(Number("driverOptions.sourceNetwork", "源网络号", "0", 0, 127, string.Empty), "FINS 路由"),
                        InGroup(Number("driverOptions.network", "目标网络号", "0", 0, 127, string.Empty), "FINS 路由"),
                        InGroup(Number("driverOptions.sourceUnit", "源单元号", "0", 0, 254, string.Empty), "FINS 路由"),
                        InGroup(Number("driverOptions.destinationUnit", "目标单元号", "0", 0, 254, string.Empty), "FINS 路由"),
                        InGroup(Number("driverOptions.maxWordCount", "单次最大字数", "240", 1, 999, string.Empty), "批读优化"),
                        InGroup(Number("driverOptions.maxBitCount", "单次最大位数", "480", 1, 1998, string.Empty), "批读优化"),
                        InGroup(Number("driverOptions.maxGapWords", "允许合并间隔", "4", 0, 64, "word"), "批读优化"),
                        InGroup(WithHelp(Number("driverOptions.maxEmBank", "最大 EM 银行（十进制）", "24", 0, 24, string.Empty), "24 对应 FINS 地址中的十六进制银行 E18。"), "地址能力"),
                        InGroup(Number("driverOptions.udpReadRetries", "UDP 读重试次数", "1", 0, 1, string.Empty), "UDP 可靠性")
                    }, "Udp");
                case PlcProtocol.BacnetIp:
                    return Network("127.0.0.1", "47808", true, new[]
                    {
                        InGroup(Number("driverOptions.localPort", "本地端口", "0", 0, 65535, string.Empty), "BACnet/IP"),
                        InGroup(Number("driverOptions.deviceInstance", "设备实例号", "-1", -1, 4194303, string.Empty), "设备发现"),
                        InGroup(WithHelp(Text("driverOptions.bbmdAddress", "BBMD 地址", string.Empty, "例如 192.168.1.10"), "跨子网发现或访问时填写；同一子网留空。"), "BBMD 外部设备"),
                        InGroup(Number("driverOptions.bbmdPort", "BBMD 端口", "47808", 1, 65535, string.Empty), "BBMD 外部设备"),
                        InGroup(Number("driverOptions.bbmdTtlSeconds", "注册有效期", "600", 30, 65535, "秒"), "BBMD 外部设备"),
                        InGroup(Number("driverOptions.retries", "重试次数", "1", 0, 10, string.Empty), "可靠性"),
                        InGroup(Number("driverOptions.writePriority", "写优先级", "16", 1, 16, string.Empty), "写入"),
                        InGroup(Number("driverOptions.maxBatchObjects", "单批最大对象数", "16", 1, 128, string.Empty), "批读优化")
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
                        Number("driverOptions.maxBatchItems", "单轮最大对象数", "32", 1, 256, string.Empty),
                        Switch("driverOptions.probeNodeOnConnect", "连接时探测节点", "true"),
                        WithHelp(Switch("driverOptions.startNodeOnConnect", "连接时发送 NMT Start", "false"), "仅在需要由网关启动节点时开启，默认不改变设备运行状态。")
                          , Switch("driverOptions.resetCommunicationOnConnect", "连接时复位通信", "false")
                          , Number("driverOptions.heartbeatTimeoutMilliseconds", "Heartbeat 超时", "3000", 100, 60000, "ms")
                          , Number("driverOptions.pdoMaxAgeMilliseconds", "TPDO 失效阈值", "3000", 0, 60000, "ms")
                          , WithHelp(Number("driverOptions.syncIntervalMilliseconds", "SYNC 周期", "0", 0, 60000, "ms"), "0 表示不由网关周期发送 SYNC。")
                      };
                case PlcProtocol.ModbusRtu:
                    return Serial(new[]
                    {
                        Number("rack", "Slave ID", "1", 1, 247, string.Empty),
                        InGroup(Number("driverOptions.maxBatchGapPoints", "批读允许间隔", "2", 0, 64, "点"), "批读优化")
                    });
                case PlcProtocol.ModbusAscii:
                    return Serial(new[]
                    {
                        Number("rack", "Slave ID", "1", 1, 247, string.Empty),
                        InGroup(Number("driverOptions.maxBatchGapPoints", "批读允许间隔", "2", 0, 64, "点"), "批读优化")
                    }, "7", "Even", "One");
                case PlcProtocol.MitsubishiSerial:
                case PlcProtocol.MitsubishiQlSerial:
                    return Serial();
                case PlcProtocol.Dlt6452007:
                case PlcProtocol.Cjt1882004:
                case PlcProtocol.Cjt1882018:
                    return Network(
                        "127.0.0.1",
                        "4001",
                        false,
                        new PlcConnectionParameterDefinition[0],
                        "Tcp",
                        false);
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
                        InGroup(Number("driverOptions.opcDaUpdateRateMilliseconds", "服务端组刷新周期", "1000", 50, 60000, "ms"), "OPC DA 读取"),
                        InGroup(WithHelp(Select("driverOptions.opcDaReadSource", "读取来源", "Cache", new[] { "Cache", "Device" }, false), "Cache 使用 OPC Server 主动组缓存，适合大量点位；Device 每轮强制读取现场设备。"), "OPC DA 读取"),
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

        private static List<PlcConnectionParameterDefinition> Network(
            string host,
            string port,
            bool allowUdp,
            IEnumerable<PlcConnectionParameterDefinition> extra,
            string defaultTransport,
            bool includeWordOrder = true)
        {
            List<PlcConnectionParameterDefinition> items = new List<PlcConnectionParameterDefinition>
            {
                Text("host", "主机 / 地址", host, host),
                Number("port", "端口", port, 0, 65535, string.Empty),
                Select("transport", "传输", defaultTransport, allowUdp ? new[] { "Tcp", "Udp" } : new[] { "Tcp" }, !allowUdp),
                Number("timeoutMilliseconds", "超时", "3000", 100, 60000, "ms")
            };
            items.AddRange(extra);
            if (includeWordOrder)
                items.Add(Select("wordOrder", "字序", "HighWordFirst", new[] { "HighWordFirst", "LowWordFirst" }, false));
            return items;
        }

        private static List<PlcConnectionParameterDefinition> Serial()
        {
            return Serial(new PlcConnectionParameterDefinition[0]);
        }

        private static List<PlcConnectionParameterDefinition> Serial(
            IEnumerable<PlcConnectionParameterDefinition> extra,
            string dataBits = "8",
            string parity = "None",
            string stopBits = "One")
        {
            List<PlcConnectionParameterDefinition> items = new List<PlcConnectionParameterDefinition>
            {
                Text("host", "串口", "COM1", "COM1"),
                Number("port", "波特率", "9600", 1200, 921600, string.Empty),
                Number("dataBits", "数据位", dataBits, 5, 8, string.Empty),
                Select("serialParity", "校验", parity, new[] { "None", "Odd", "Even", "Mark", "Space" }, false),
                Select("serialStopBits", "停止位", stopBits, new[] { "One", "Two", "OnePointFive", "None" }, false),
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

        private static PlcConnectionParameterDefinition TextArea(string key, string label, string defaultValue, string placeholder, string helpText)
        {
            return new PlcConnectionParameterDefinition
            {
                Key = key,
                Label = label,
                ParameterType = "textarea",
                DefaultValue = defaultValue,
                Placeholder = placeholder,
                HelpText = helpText,
                Advanced = true
            };
        }

        private static PlcConnectionParameterDefinition InGroup(PlcConnectionParameterDefinition definition, string group)
        {
            definition.Group = group;
            return definition;
        }

        private static PlcConnectionParameterDefinition WithHelp(PlcConnectionParameterDefinition definition, string helpText)
        {
            definition.HelpText = helpText;
            return definition;
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
