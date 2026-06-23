/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Inference
* 项目描述 ：
* 类 名 称 ：OnnxModelInferenceService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Inference
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
using System.Collections.Concurrent;
using IPC.Gateway.Core.Gateway;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IPC.Gateway.Inference;

public sealed class OnnxModelInferenceService : IModelInferenceService
{
    private readonly ConcurrentDictionary<string, Lazy<InferenceSession>> _sessions;
    private bool _disposed;

    public OnnxModelInferenceService()
    {
        _sessions = new ConcurrentDictionary<string, Lazy<InferenceSession>>(StringComparer.OrdinalIgnoreCase);
    }

    public ModelInferenceResult Predict(ModelInferenceRequest request)
    {
        if (request == null)
            return ModelInferenceResult.Failed("Inference request is empty.");
        if (string.IsNullOrWhiteSpace(request.ModelPath))
            return ModelInferenceResult.Failed("Model path is empty.");
        if (request.Features == null || request.Features.Count == 0)
            return ModelInferenceResult.Failed("Model features are empty.");

        int timeoutMilliseconds = NormalizeTimeout(request.TimeoutMilliseconds);
        try
        {
            Task<ModelInferenceResult> task = Task.Factory.StartNew(
                () => RunCore(request),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);

            if (!task.Wait(timeoutMilliseconds))
                return ModelInferenceResult.Failed("ONNX inference timed out after " + timeoutMilliseconds + " ms.");
            return task.GetAwaiter().GetResult();
        }
        catch (AggregateException ex)
        {
            Exception inner = ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex;
            return ModelInferenceResult.Failed(inner.Message);
        }
        catch (Exception ex)
        {
            return ModelInferenceResult.Failed(ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (Lazy<InferenceSession> lazy in _sessions.Values)
        {
            if (lazy.IsValueCreated)
                lazy.Value.Dispose();
        }

        _sessions.Clear();
    }

    private ModelInferenceResult RunCore(ModelInferenceRequest request)
    {
        string modelPath = ResolveModelPath(request.ModelPath);
        if (!File.Exists(modelPath))
            return ModelInferenceResult.Failed("ONNX model file was not found: " + modelPath);

        InferenceSession session = GetSession(modelPath);
        List<NamedOnnxValue> inputs = BuildInputs(session, request);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
        DisposableNamedOnnxValue? selected = SelectOutput(results, request.OutputName);
        if (selected == null)
            return ModelInferenceResult.Failed("ONNX output was not found.");

        List<double> outputs = ToDoubles(selected.Value);
        if (outputs.Count == 0)
            return ModelInferenceResult.Failed("ONNX output is empty or unsupported.");

        int outputIndex = Math.Max(0, Math.Min(request.OutputIndex, outputs.Count - 1));
        return new ModelInferenceResult
        {
            Success = true,
            Score = outputs[outputIndex],
            Outputs = outputs,
            Timestamp = DateTime.Now
        };
    }

    private InferenceSession GetSession(string modelPath)
    {
        Lazy<InferenceSession> lazy = _sessions.GetOrAdd(
            modelPath,
            path => new Lazy<InferenceSession>(() => new InferenceSession(path), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private static List<NamedOnnxValue> BuildInputs(InferenceSession session, ModelInferenceRequest request)
    {
        List<NamedOnnxValue> inputs = new List<NamedOnnxValue>();
        List<string> configuredInputNames = SplitNames(request.InputNames);
        IReadOnlyList<float> features = request.Features.ToList();

        if (configuredInputNames.Count > 1)
        {
            if (features.Count < configuredInputNames.Count)
                throw new InvalidOperationException("Feature count is less than configured ONNX input count.");

            for (int i = 0; i < configuredInputNames.Count; i++)
            {
                string inputName = configuredInputNames[i];
                session.InputMetadata.TryGetValue(inputName, out NodeMetadata? metadata);
                DenseTensor<float> tensor = CreateTensor(new[] { features[i] }, metadata, preferSingleRow: false);
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, tensor));
            }

            return inputs;
        }

        string singleInputName = !string.IsNullOrWhiteSpace(request.InputName)
            ? request.InputName.Trim()
            : configuredInputNames.Count == 1
                ? configuredInputNames[0]
                : session.InputMetadata.Keys.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(singleInputName))
            throw new InvalidOperationException("ONNX input name was not found.");

        session.InputMetadata.TryGetValue(singleInputName, out NodeMetadata? singleMetadata);
        inputs.Add(NamedOnnxValue.CreateFromTensor(singleInputName, CreateTensor(features, singleMetadata, preferSingleRow: true)));
        return inputs;
    }

    private static DisposableNamedOnnxValue? SelectOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string outputName)
    {
        if (!string.IsNullOrWhiteSpace(outputName))
        {
            foreach (DisposableNamedOnnxValue result in results)
            {
                if (string.Equals(result.Name, outputName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return result;
            }
        }

        return results.FirstOrDefault();
    }

    private static DenseTensor<float> CreateTensor(IReadOnlyList<float> values, NodeMetadata? metadata, bool preferSingleRow)
    {
        int[] dimensions = BuildDimensions(values.Count, metadata, preferSingleRow);
        return new DenseTensor<float>(values.ToArray(), dimensions);
    }

    private static int[] BuildDimensions(int valueCount, NodeMetadata? metadata, bool preferSingleRow)
    {
        if (metadata != null && metadata.Dimensions != null && metadata.Dimensions.Length > 0)
        {
            int[] normalized = metadata.Dimensions.Select(item => item <= 0 ? 1 : item).ToArray();
            long elementCount = 1;
            for (int i = 0; i < normalized.Length; i++)
                elementCount *= normalized[i];
            if (elementCount == valueCount)
                return normalized;
        }

        if (preferSingleRow)
            return new[] { 1, Math.Max(1, valueCount) };
        return new[] { Math.Max(1, valueCount) };
    }

    private static List<double> ToDoubles(object value)
    {
        List<double> values = new List<double>();
        if (value == null)
            return values;

        if (value is Tensor<float> floatTensor)
        {
            foreach (float item in floatTensor)
                values.Add(item);
            return values;
        }

        if (value is Tensor<double> doubleTensor)
        {
            foreach (double item in doubleTensor)
                values.Add(item);
            return values;
        }

        if (value is Tensor<int> intTensor)
        {
            foreach (int item in intTensor)
                values.Add(item);
            return values;
        }

        if (value is Tensor<long> longTensor)
        {
            foreach (long item in longTensor)
                values.Add(item);
            return values;
        }

        if (value is IEnumerable<float> floats)
        {
            values.AddRange(floats.Select(item => (double)item));
            return values;
        }

        if (value is IEnumerable<double> doubles)
        {
            values.AddRange(doubles);
            return values;
        }

        if (value is IEnumerable<int> ints)
        {
            values.AddRange(ints.Select(item => (double)item));
            return values;
        }

        if (value is IEnumerable<long> longs)
        {
            values.AddRange(longs.Select(item => (double)item));
            return values;
        }

        if (value is float singleFloat)
            values.Add(singleFloat);
        else if (value is double singleDouble)
            values.Add(singleDouble);
        else if (value is int singleInt)
            values.Add(singleInt);
        else if (value is long singleLong)
            values.Add(singleLong);

        return values;
    }

    private static List<string> SplitNames(string names)
    {
        if (string.IsNullOrWhiteSpace(names))
            return new List<string>();

        return names
            .Split(new[] { ',', ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string ResolveModelPath(string modelPath)
    {
        string trimmed = modelPath.Trim();
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        string basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed));
        if (File.Exists(basePath))
            return basePath;

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, trimmed));
    }

    private static int NormalizeTimeout(int timeoutMilliseconds)
    {
        if (timeoutMilliseconds <= 0)
            return 1000;
        return Math.Min(30000, timeoutMilliseconds);
    }
}
