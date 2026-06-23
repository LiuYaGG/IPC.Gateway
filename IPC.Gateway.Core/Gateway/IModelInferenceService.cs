/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：IModelInferenceService
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
namespace IPC.Gateway.Core.Gateway
{
    public interface IModelInferenceService : IDisposable
    {
        ModelInferenceResult Predict(ModelInferenceRequest request);
    }

    public sealed class ModelInferenceRequest
    {
        public ModelInferenceRequest()
        {
            ModelPath = string.Empty;
            ModelPurpose = string.Empty;
            InputName = string.Empty;
            InputNames = string.Empty;
            OutputName = string.Empty;
            Features = new List<float>();
            TimeoutMilliseconds = 1000;
        }

        public string ModelPath { get; set; }
        public string ModelPurpose { get; set; }
        public string InputName { get; set; }
        public string InputNames { get; set; }
        public string OutputName { get; set; }
        public int OutputIndex { get; set; }
        public IList<float> Features { get; set; }
        public int TimeoutMilliseconds { get; set; }
    }

    public sealed class ModelInferenceResult
    {
        public ModelInferenceResult()
        {
            ErrorMessage = string.Empty;
            Outputs = new List<double>();
            Timestamp = DateTime.Now;
        }

        public bool Success { get; set; }
        public double Score { get; set; }
        public IList<double> Outputs { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }

        public static ModelInferenceResult Failed(string message)
        {
            return new ModelInferenceResult
            {
                Success = false,
                ErrorMessage = message ?? string.Empty
            };
        }
    }

    public sealed class NoopModelInferenceService : IModelInferenceService
    {
        public static readonly NoopModelInferenceService Instance = new NoopModelInferenceService();

        private NoopModelInferenceService()
        {
        }

        public ModelInferenceResult Predict(ModelInferenceRequest request)
        {
            return ModelInferenceResult.Failed("ONNX inference service is not registered.");
        }

        public void Dispose()
        {
        }
    }
}
