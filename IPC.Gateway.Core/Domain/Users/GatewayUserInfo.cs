/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Users
* 项目描述 ：
* 类 名 称 ：GatewayUserInfo
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Domain.Users
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
using System;

namespace IPC.Gateway.Core.Domain.Users
{
    
    
    
    
    
    
    
    
    
    public sealed class GatewayUserInfo
    {
        public GatewayUserInfo()
        {
            Id = string.Empty;
            Username = string.Empty;
            DisplayName = string.Empty;
            Role = "Viewer";
            PasswordHash = string.Empty;
            PasswordSalt = string.Empty;
            CreatedTime = DateTime.MinValue;
            PasswordChangedTime = DateTime.MinValue;
            LastLoginTime = DateTime.MinValue;
            LastFailedLoginTime = DateTime.MinValue;
            LockoutEndTime = DateTime.MinValue;
        }

        public string Id { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool Enabled { get; set; }
        public string PasswordHash { get; set; }
        public string PasswordSalt { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime PasswordChangedTime { get; set; }
        public DateTime LastLoginTime { get; set; }
        public DateTime LastFailedLoginTime { get; set; }
        public int FailedLoginCount { get; set; }
        public DateTime LockoutEndTime { get; set; }
    }
}
