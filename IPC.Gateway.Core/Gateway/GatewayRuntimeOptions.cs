/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeOptions
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
using IPC.Runtime.Engine;
using IPC.Gateway.Core.Resilience;

namespace IPC.Gateway.Core.Gateway
{
    
    
    
    
    
    
    
    
    
    public sealed class GatewayRuntimeOptions
    {
        public GatewayRuntimeOptions()
        {
            ProjectPath = string.Empty;
            MqttOptionsPath = string.Empty;
            Database = new GatewayDatabaseOptions();
            Scheduler = new RuntimeSchedulerOptions();
            SecretProtection = new GatewaySecretProtectionOptions();
            Resilience = new GatewayResilienceOptions();
            AutoCreateDefaultProject = true;
        }

        public string ProjectPath { get; set; }
        public string MqttOptionsPath { get; set; }
        public GatewayDatabaseOptions Database { get; set; }
        public RuntimeSchedulerOptions Scheduler { get; set; }
        public GatewaySecretProtectionOptions SecretProtection { get; set; }
        public GatewayResilienceOptions Resilience { get; set; }
        public bool AutoCreateDefaultProject { get; set; }
    }

    public sealed class GatewayResilienceOptions
    {
        public GatewayResilienceOptions()
        {
            RuleEngine = new CircuitBreakerOptions
            {
                FailureThreshold = 10,
                SuccessThreshold = 2,
                BreakDurationSeconds = 30,
                DegradedMode = "SkipEvaluation"
            };
            Mqtt = new CircuitBreakerOptions
            {
                FailureThreshold = 5,
                SuccessThreshold = 1,
                BreakDurationSeconds = 30,
                DegradedMode = "OutboxOnly"
            };
            History = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                SuccessThreshold = 1,
                BreakDurationSeconds = 60,
                DegradedMode = "DropWrites"
            };
            ProtocolDriver = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                SuccessThreshold = 1,
                BreakDurationSeconds = 30,
                DegradedMode = "SkipDevicePoll"
            };
        }

        public CircuitBreakerOptions RuleEngine { get; set; }
        public CircuitBreakerOptions Mqtt { get; set; }
        public CircuitBreakerOptions History { get; set; }
        public CircuitBreakerOptions ProtocolDriver { get; set; }
    }

    
    
    
    
    
    
    
    
    
    public sealed class GatewayDatabaseOptions
    {
        public GatewayDatabaseOptions()
        {
            Provider = "PostgreSQL";
            ConnectionString = string.Empty;
            Host = "localhost";
            Port = 5432;
            Database = "ipc_gateway";
            Username = "postgres";
            Password = string.Empty;
            AutoCreateDatabase = true;
        }

        public string Provider { get; set; }
        public string ConnectionString { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool AutoCreateDatabase { get; set; }
    }
}
