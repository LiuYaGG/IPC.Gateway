/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：MqttPublishResult
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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
namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class MqttPublishResult
    {
        public MqttPublishResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public ushort PacketId { get; set; }
        public string ErrorMessage { get; set; }

        public static MqttPublishResult Ok(ushort packetId)
        {
            return new MqttPublishResult { Success = true, PacketId = packetId, ErrorMessage = string.Empty };
        }

        public static MqttPublishResult Fail(string message)
        {
            return new MqttPublishResult { Success = false, PacketId = 0, ErrorMessage = message ?? string.Empty };
        }
    }
}
