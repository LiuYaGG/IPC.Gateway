/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Indexing
* 项目描述 ：
* 类 名 称 ：TagRuntimeIndex
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Indexing
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
using System.Collections.Generic;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Indexing
{
    
    
    
    
    
    
    
    
    
    public sealed class TagRuntimeIndex
    {
        private readonly Dictionary<string, TagConfig> _tagsByPath;

        public TagRuntimeIndex(ProjectConfig config)
        {
            _tagsByPath = new Dictionary<string, TagConfig>();
            Rebuild(config);
        }

        public void Rebuild(ProjectConfig config)
        {
            _tagsByPath.Clear();
            if (config == null || config.Devices == null)
                return;

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig device = config.Devices[d];
                if (device == null)
                    continue;

                if (device.Tags != null)
                {
                    for (int t = 0; t < device.Tags.Count; t++)
                    {
                        TagConfig tag = device.Tags[t];
                        if (tag == null)
                            continue;

                        tag.DeviceId = device.Id;
                        tag.GroupId = string.Empty;
                        string key = TagPath.Build(device.Name, string.Empty, tag.Name);
                        _tagsByPath[key] = tag;
                    }
                }

                if (device.Groups == null)
                    continue;

                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null || group.Tags == null)
                        continue;

                    for (int t = 0; t < group.Tags.Count; t++)
                    {
                        TagConfig tag = group.Tags[t];
                        if (tag == null)
                            continue;

                        tag.DeviceId = device.Id;
                        tag.GroupId = group.Id;
                        string key = TagPath.Build(device.Name, group.Name, tag.Name);
                        _tagsByPath[key] = tag;
                    }
                }
            }
        }

        public bool TryGetTag(string deviceName, string groupName, string tagName, out TagConfig? tag)
        {
            return _tagsByPath.TryGetValue(TagPath.Build(deviceName, groupName, tagName), out tag);
        }
    }
}
