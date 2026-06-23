/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Api
* 项目描述 ：
* 类 名 称 ：ReadTagResponse
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Api
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
using IPC.Runtime.Values;

namespace IPC.Runtime.Api
{
    
    
    
    
    
    
    
    
    
    public sealed class ReadTagResponse
    {
        public ReadTagResponse()
        {
            DeviceName = string.Empty;
            GroupName = string.Empty;
            TagName = string.Empty;
            RawValue = string.Empty;
            RawValueText = string.Empty;
            Value = string.Empty;
            ValueText = string.Empty;
            Unit = string.Empty;
            DataType = string.Empty;
            CleaningAction = string.Empty;
            CleaningMessage = string.Empty;
            Quality = TagQuality.Unknown.ToString();
            Timestamp = DateTime.MinValue;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string DeviceName { get; set; }
        public string GroupName { get; set; }
        public string TagName { get; set; }
        public object RawValue { get; set; }
        public string RawValueText { get; set; }
        public object Value { get; set; }
        public string ValueText { get; set; }
        public string Unit { get; set; }
        public string DataType { get; set; }
        public bool CleaningApplied { get; set; }
        public string CleaningAction { get; set; }
        public string CleaningMessage { get; set; }
        public string Quality { get; set; }
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; }

        public static ReadTagResponse FromSnapshot(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return new ReadTagResponse
                {
                    Success = false,
                    Quality = TagQuality.NotFound.ToString(),
                    ErrorMessage = "Tag was not found."
                };
            }

            return new ReadTagResponse
            {
                Success = snapshot.Quality == TagQuality.Good,
                DeviceName = snapshot.DeviceName,
                GroupName = snapshot.GroupName,
                TagName = snapshot.TagName,
                RawValue = snapshot.RawValue,
                RawValueText = snapshot.RawValueText,
                Value = snapshot.Value,
                ValueText = snapshot.ValueText,
                Unit = snapshot.Unit,
                DataType = snapshot.DataType,
                CleaningApplied = snapshot.CleaningApplied,
                CleaningAction = snapshot.CleaningAction,
                CleaningMessage = snapshot.CleaningMessage,
                Quality = snapshot.Quality.ToString(),
                Timestamp = snapshot.Timestamp,
                ErrorMessage = snapshot.ErrorMessage
            };
        }
    }
}
