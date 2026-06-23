/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Core
* 项目描述 ：
* 类 名 称 ：Parity
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Core
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
namespace IPC.Plc.Communication.Core
{
    public enum Parity
    {
        None,
        Odd,
        Even,
        Mark,
        Space
    }

    public enum StopBits
    {
        None,
        One,
        Two,
        OnePointFive
    }

    
    
    
    
    
    
    
    
    
    public sealed class PlcConnectionOptions
    {
        public PlcConnectionOptions()
        {
            Protocol = PlcProtocol.RockwellCip;
            Host = string.Empty;
            Port = 44818;
            TimeoutMilliseconds = 3000;
            WordOrder = PlcWordOrder.HighWordFirst;
            Transport = NetworkTransport.Tcp;
            DataBits = 8;
            SerialParity = Parity.None;
            SerialStopBits = StopBits.One;
            Username = string.Empty;
            Password = string.Empty;
            CertificatePath = string.Empty;
            CertificatePassword = string.Empty;
            CertificateThumbprint = string.Empty;
            TrustStorePath = string.Empty;
            ValidateServerCertificate = true;
            OpcDaServerProgId = string.Empty;
            OpcDaGroupName = "IPC";
            DriverId = string.Empty;
            DriverOptionsJson = string.Empty;
        }

        public PlcProtocol Protocol { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public int Rack { get; set; }
        public int Slot { get; set; }
        public int TimeoutMilliseconds { get; set; }
        public PlcWordOrder WordOrder { get; set; }
        public NetworkTransport Transport { get; set; }
        public int DataBits { get; set; }
        public Parity SerialParity { get; set; }
        public StopBits SerialStopBits { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string CertificatePath { get; set; }
        public string CertificatePassword { get; set; }
        public string CertificateThumbprint { get; set; }
        public string TrustStorePath { get; set; }
        public bool ValidateServerCertificate { get; set; }
        public string OpcDaServerProgId { get; set; }
        public string OpcDaGroupName { get; set; }
        public string DriverId { get; set; }
        public string DriverOptionsJson { get; set; }
    }
}
