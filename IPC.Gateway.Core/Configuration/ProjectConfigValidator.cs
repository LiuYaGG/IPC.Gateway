/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：ProjectConfigValidator
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Configuration
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
using System.Collections.Generic;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Engine;

namespace IPC.Runtime.Configuration
{
    
    
    
    
    
    
    
    
    
    public static class ProjectConfigValidator
    {
        public static ProjectConfigValidationResult Validate(ProjectConfig config)
        {
            ProjectConfigValidationResult result = new ProjectConfigValidationResult();
            if (config == null)
            {
                result.AddError("项目配置不能为空。");
                return result;
            }

            if (string.IsNullOrWhiteSpace(config.ProjectId))
                result.AddWarning("项目ID为空，运行时会自动补齐。");
            if (string.IsNullOrWhiteSpace(config.Name))
                result.AddWarning("项目名称为空。");

            ProjectChannelValidator.Validate(config, result);

            if (config.Devices == null || config.Devices.Count == 0)
            {
                result.AddWarning("当前项目没有设备。");
                return result;
            }

            HashSet<string> deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < config.Devices.Count; i++)
                ValidateDevice(config.Devices[i], i, deviceNames, result);

            return result;
        }

        private static void ValidateDevice(DeviceConfig device, int index, HashSet<string> deviceNames, ProjectConfigValidationResult result)
        {
            string prefix = "设备[" + (index + 1) + "]";
            if (device == null)
            {
                result.AddError(prefix + "不能为空。");
                return;
            }

            if (string.IsNullOrWhiteSpace(device.Name))
                result.AddError(prefix + "名称不能为空。");
            else if (!deviceNames.Add(device.Name.Trim()))
                result.AddError(prefix + "名称重复：" + device.Name);

            if (device.Connection == null)
                result.AddError(prefix + "连接参数不能为空。");
            else
                ValidateConnection(prefix, device, result);

            if (device.DefaultScanRateMs <= 0)
                result.AddWarning(prefix + "默认采集周期未设置，将按1000毫秒运行。");
            if (device.FailureRetryDelayMs <= 0)
                result.AddWarning(prefix + "失败重试间隔未设置，将按1000毫秒运行。");
            if (device.MaxFailureRetryDelayMs > 0 && device.MaxFailureRetryDelayMs < device.FailureRetryDelayMs)
                result.AddWarning(prefix + "最大失败重试间隔小于失败重试间隔，将自动按失败重试间隔修正。");

            HashSet<string> tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (device.Tags != null)
            {
                for (int t = 0; t < device.Tags.Count; t++)
                    ValidateTag(device, null, device.Tags[t], prefix + ".标签[" + (t + 1) + "]", tagNames, result);
            }

            if (device.Groups != null)
            {
                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    string groupPrefix = prefix + ".分组[" + (g + 1) + "]";
                    if (group == null)
                    {
                        result.AddError(groupPrefix + "不能为空。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(group.Name))
                        result.AddError(groupPrefix + "名称不能为空。");
                    else if (!groupNames.Add(group.Name.Trim()))
                        result.AddError(groupPrefix + "名称重复：" + group.Name);

                    HashSet<string> groupTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (group.Tags != null)
                    {
                        for (int t = 0; t < group.Tags.Count; t++)
                            ValidateTag(device, group, group.Tags[t], groupPrefix + ".标签[" + (t + 1) + "]", groupTagNames, result);
                    }
                }
            }
        }

        private static void ValidateConnection(string prefix, DeviceConfig device, ProjectConfigValidationResult result)
        {
            PlcConnectionOptions connection = device.Connection;
            if (device.Protocol == PlcProtocol.VirtualPlc)
                return;

            if (device.Protocol == PlcProtocol.ModbusTcp ||
                device.Protocol == PlcProtocol.BacnetIp ||
                device.Protocol == PlcProtocol.Dlt6452007 ||
                device.Protocol == PlcProtocol.Cjt1882004)
            {
                if (string.IsNullOrWhiteSpace(connection.Host))
                    result.AddError(prefix + "主机地址不能为空。");
                if (connection.Port <= 0 || connection.Port > 65535)
                    result.AddError(prefix + "端口必须在1到65535之间。");
            }

            if (device.Protocol == PlcProtocol.CanOpen)
            {
                if (string.IsNullOrWhiteSpace(connection.Host))
                    result.AddError(prefix + "CANopen 串口不能为空。");
                if (connection.Port <= 0)
                    result.AddError(prefix + "CANopen 适配器波特率必须大于0。");
            }

            if (connection.TimeoutMilliseconds <= 0)
                result.AddWarning(prefix + "超时时间未设置，将使用驱动默认值。");
        }

        private static void ValidateTag(DeviceConfig device, GroupConfig? group, TagConfig tag, string prefix, HashSet<string> tagNames, ProjectConfigValidationResult result)
        {
            if (tag == null)
            {
                result.AddError(prefix + "不能为空。");
                return;
            }

            if (string.IsNullOrWhiteSpace(tag.Name))
                result.AddError(prefix + "名称不能为空。");
            else if (!tagNames.Add(tag.Name.Trim()))
                result.AddError(prefix + "名称重复：" + tag.Name);

            if (tag.ElementCount <= 0)
                result.AddWarning(prefix + "元素数量小于等于0，将按1处理。");
            if (tag.ScanRateMs < 0)
                result.AddWarning(prefix + "采集周期小于0，将继承分组或设备周期。");
            if (tag.FailureRetryDelayMs < 0)
                result.AddWarning(prefix + "失败重试间隔小于0，将继承设备重试间隔。");

            if (!tag.Enabled)
                return;

            if (device.Protocol == PlcProtocol.Dlt6452007 || device.Protocol == PlcProtocol.Cjt1882004)
            {
                if (string.IsNullOrWhiteSpace(tag.Address))
                {
                    if (string.IsNullOrWhiteSpace(tag.MeterAddress))
                        result.AddError(prefix + "表地址不能为空。");
                    if (string.IsNullOrWhiteSpace(tag.MeterDataIdentifier))
                        result.AddError(prefix + "数据标识不能为空。");
                }
            }
            else if (string.IsNullOrWhiteSpace(tag.Address))
            {
                result.AddError(prefix + "地址不能为空。");
            }

            string staticValidationError = CompiledDeviceReadPlan.ValidateTagDefinition(device, tag);
            if (!string.IsNullOrWhiteSpace(staticValidationError))
                result.AddError(prefix + "静态地址校验失败：" + staticValidationError);
        }
    }
}
