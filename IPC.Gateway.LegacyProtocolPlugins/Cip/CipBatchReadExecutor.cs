using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    internal static class CipBatchReadExecutor
    {
        public static IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests, CipBatchReadContext context)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null ||
                context.BuildReadRequest == null ||
                context.SendConnectedMessage == null ||
                context.DecodeReadResponse == null ||
                context.ReadTag == null)
                throw new ArgumentNullException("context");

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<CipBatchReadItem> items = BuildItems(requests, context, results);
            List<CipBatchReadOperation> operations = FlattenOperations(items);
            ExecuteOperations(operations, context);

            for (int i = 0; i < items.Count; i++)
                results[items[i].Index] = items[i].BuildResult();

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                output.Add(results[i] ?? PlcBatchReadResult.FromFailure(request, "Batch read did not produce a result.", true));
            }

            return output;
        }

        public static async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CipAsyncBatchReadContext context,
            CancellationToken cancellationToken)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null ||
                context.BuildReadRequest == null ||
                context.SendConnectedMessageAsync == null ||
                context.DecodeReadResponse == null)
                throw new ArgumentNullException("context");

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<CipBatchReadItem> items = BuildItems(requests, context, results);
            List<CipBatchReadOperation> operations = FlattenOperations(items);
            await ExecuteOperationsAsync(operations, context, cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < items.Count; i++)
                results[items[i].Index] = items[i].BuildResult();

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                output.Add(results[i] ?? PlcBatchReadResult.FromFailure(request, "Batch read did not produce a result.", true));
            }

            return output;
        }

        private static List<CipBatchReadItem> BuildItems(
            IList<PlcBatchReadRequest> requests,
            CipBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            List<CipBatchReadItem> items = new List<CipBatchReadItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    EnsureSupportedType(request.DataType);
                    items.Add(CipBatchReadItem.Create(i, request, context));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }
            return items;
        }

        private static List<CipBatchReadItem> BuildItems(
            IList<PlcBatchReadRequest> requests,
            CipAsyncBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            List<CipBatchReadItem> items = new List<CipBatchReadItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    EnsureSupportedType(request.DataType);
                    items.Add(CipBatchReadItem.Create(i, request, context));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }
            return items;
        }

        private static List<CipBatchReadOperation> FlattenOperations(List<CipBatchReadItem> items)
        {
            List<CipBatchReadOperation> operations = new List<CipBatchReadOperation>();
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = 0; j < items[i].Operations.Count; j++)
                    operations.Add(items[i].Operations[j]);
            }
            return operations;
        }

        private static void ExecuteOperations(List<CipBatchReadOperation> operations, CipBatchReadContext context)
        {
            int index = 0;
            while (index < operations.Count)
            {
                if (!CanUseMultipleService(operations[index], context))
                {
                    ExecuteSingleOperation(operations[index], context);
                    index++;
                    continue;
                }

                List<CipBatchReadOperation> batch = new List<CipBatchReadOperation>();
                while (index < operations.Count && CanAddToBatch(batch, operations[index], context))
                {
                    batch.Add(operations[index]);
                    index++;
                }

                ExecuteBatch(batch, context);
            }
        }

        private static async ValueTask ExecuteOperationsAsync(
            List<CipBatchReadOperation> operations,
            CipAsyncBatchReadContext context,
            CancellationToken cancellationToken)
        {
            int index = 0;
            while (index < operations.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanUseMultipleService(operations[index], context))
                {
                    await ExecuteSingleOperationAsync(operations[index], context, cancellationToken).ConfigureAwait(false);
                    index++;
                    continue;
                }

                List<CipBatchReadOperation> batch = new List<CipBatchReadOperation>();
                while (index < operations.Count && CanAddToBatch(batch, operations[index], context))
                {
                    batch.Add(operations[index]);
                    index++;
                }

                await ExecuteBatchAsync(batch, context, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void ExecuteBatch(List<CipBatchReadOperation> batch, CipBatchReadContext context)
        {
            if (batch.Count == 1)
            {
                ExecuteSingleOperation(batch[0], context);
                return;
            }

            try
            {
                byte[] request = BuildMultipleServiceRequest(batch);
                byte[] response = context.SendConnectedMessage(request);
                byte[][] replies = ParseMultipleServiceResponse(response, batch.Count);
                for (int i = 0; i < batch.Count; i++)
                    DecodeOperation(batch[i], replies[i], context);
            }
            catch (Exception ex)
            {
                PlcReadFailureScope scope = PlcFailureClassifier.Classify(
                    ex,
                    IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
                if (scope != PlcReadFailureScope.Tag)
                    throw;

                for (int i = 0; i < batch.Count; i++)
                    ExecuteSingleOperation(batch[i], context);
            }
        }

        private static void ExecuteSingleOperation(CipBatchReadOperation operation, CipBatchReadContext context)
        {
            try
            {
                byte[] response = context.SendConnectedMessage(operation.RequestBytes);
                DecodeOperation(operation, response, context);
            }
            catch (Exception ex)
            {
                PlcReadFailureScope scope = PlcFailureClassifier.Classify(
                    ex,
                    IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
                if (scope != PlcReadFailureScope.Tag)
                    throw;
                operation.SetFailure(ex.Message, scope);
            }
        }

        private static async ValueTask ExecuteBatchAsync(
            List<CipBatchReadOperation> batch,
            CipAsyncBatchReadContext context,
            CancellationToken cancellationToken)
        {
            if (batch.Count == 1)
            {
                await ExecuteSingleOperationAsync(batch[0], context, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                byte[] request = BuildMultipleServiceRequest(batch);
                byte[] response = await context.SendConnectedMessageAsync(request, cancellationToken).ConfigureAwait(false);
                byte[][] replies = ParseMultipleServiceResponse(response, batch.Count);
                for (int i = 0; i < batch.Count; i++)
                    DecodeOperation(batch[i], replies[i], context);
            }
            catch (Exception ex)
            {
                PlcReadFailureScope scope = PlcFailureClassifier.Classify(
                    ex,
                    IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
                if (scope != PlcReadFailureScope.Tag)
                    throw;

                for (int i = 0; i < batch.Count; i++)
                    await ExecuteSingleOperationAsync(batch[i], context, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async ValueTask ExecuteSingleOperationAsync(
            CipBatchReadOperation operation,
            CipAsyncBatchReadContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] response = await context.SendConnectedMessageAsync(operation.RequestBytes, cancellationToken).ConfigureAwait(false);
                DecodeOperation(operation, response, context);
            }
            catch (Exception ex)
            {
                PlcReadFailureScope scope = PlcFailureClassifier.Classify(
                    ex,
                    IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
                if (scope != PlcReadFailureScope.Tag)
                    throw;
                operation.SetFailure(ex.Message, scope);
            }
        }

        private static void DecodeOperation(CipBatchReadOperation operation, byte[] response, CipBatchReadContext context)
        {
            try
            {
                operation.SetSuccess(context.DecodeReadResponse(
                    response,
                    operation.TagName,
                    operation.DataType,
                    operation.ElementCount,
                    operation.Owner.Request.ElementOffset));
            }
            catch (Exception ex)
            {
                operation.SetFailure(ex.Message, PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag));
            }
        }

        private static void DecodeOperation(CipBatchReadOperation operation, byte[] response, CipAsyncBatchReadContext context)
        {
            try
            {
                operation.SetSuccess(context.DecodeReadResponse(
                    response,
                    operation.TagName,
                    operation.DataType,
                    operation.ElementCount,
                    operation.Owner.Request.ElementOffset));
            }
            catch (Exception ex)
            {
                operation.SetFailure(ex.Message, PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag));
            }
        }

        private static bool CanAddToBatch(List<CipBatchReadOperation> batch, CipBatchReadOperation operation, CipBatchReadContext context)
        {
            if (!CanUseMultipleService(operation, context))
                return false;

            int maxServices = context.MaxServicesPerPacket > 0 ? context.MaxServicesPerPacket : 16;
            if (batch.Count >= maxServices)
                return false;

            int candidateLength = EstimateMultipleServiceLength(batch, operation);
            int maxBytes = context.MaxRequestBytes > 0 ? context.MaxRequestBytes : int.MaxValue;
            return candidateLength <= maxBytes;
        }

        private static bool CanAddToBatch(List<CipBatchReadOperation> batch, CipBatchReadOperation operation, CipAsyncBatchReadContext context)
        {
            if (!CanUseMultipleService(operation, context))
                return false;

            int maxServices = context.MaxServicesPerPacket > 0 ? context.MaxServicesPerPacket : 16;
            if (batch.Count >= maxServices)
                return false;

            int candidateLength = EstimateMultipleServiceLength(batch, operation);
            int maxBytes = context.MaxRequestBytes > 0 ? context.MaxRequestBytes : int.MaxValue;
            return candidateLength <= maxBytes;
        }

        private static bool CanUseMultipleService(CipBatchReadOperation operation, CipBatchReadContext context)
        {
            int maxBytes = context.MaxRequestBytes > 0 ? context.MaxRequestBytes : int.MaxValue;
            return EstimateMultipleServiceLength(null, operation) <= maxBytes;
        }

        private static bool CanUseMultipleService(CipBatchReadOperation operation, CipAsyncBatchReadContext context)
        {
            int maxBytes = context.MaxRequestBytes > 0 ? context.MaxRequestBytes : int.MaxValue;
            return EstimateMultipleServiceLength(null, operation) <= maxBytes;
        }

        private static int EstimateMultipleServiceLength(List<CipBatchReadOperation> batch, CipBatchReadOperation next)
        {
            int count = (batch == null ? 0 : batch.Count) + 1;
            int length = 1 + 1 + 4 + 2 + count * 2;
            if (batch != null)
            {
                for (int i = 0; i < batch.Count; i++)
                    length += batch[i].RequestBytes.Length;
            }
            return length + next.RequestBytes.Length;
        }

        private static byte[] BuildMultipleServiceRequest(List<CipBatchReadOperation> batch)
        {
            MemoryStream stream = new MemoryStream();
            stream.WriteByte(0x0A);
            stream.WriteByte(0x02);
            stream.WriteByte(0x20);
            stream.WriteByte(0x02);
            stream.WriteByte(0x24);
            stream.WriteByte(0x01);

            long dataStart = stream.Position;
            WriteUInt16(stream, (ushort)batch.Count);
            long offsetsStart = stream.Position;
            for (int i = 0; i < batch.Count; i++)
                WriteUInt16(stream, 0);

            ushort[] offsets = new ushort[batch.Count];
            for (int i = 0; i < batch.Count; i++)
            {
                offsets[i] = (ushort)(stream.Position - dataStart);
                byte[] request = batch[i].RequestBytes;
                stream.Write(request, 0, request.Length);
            }

            byte[] result = stream.ToArray();
            for (int i = 0; i < offsets.Length; i++)
                PutUInt16(result, (int)offsetsStart + i * 2, offsets[i]);
            return result;
        }

        private static byte[][] ParseMultipleServiceResponse(byte[] response, int expectedCount)
        {
            int dataOffset = ParseCipReply(response, 0x8A);
            if (response.Length < dataOffset + 2)
                throw new InvalidOperationException("CIP Multiple Service response is missing the item count.");

            ushort count = ReadUInt16(response, dataOffset);
            if (count != expectedCount)
                throw new InvalidOperationException("CIP Multiple Service response item count mismatch.");
            if (response.Length < dataOffset + 2 + count * 2)
                throw new InvalidOperationException("CIP Multiple Service response offset table is truncated.");

            byte[][] replies = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                int start = dataOffset + ReadUInt16(response, dataOffset + 2 + i * 2);
                int end = i + 1 < count
                    ? dataOffset + ReadUInt16(response, dataOffset + 2 + (i + 1) * 2)
                    : response.Length;

                if (start < dataOffset || end < start || end > response.Length)
                    throw new InvalidOperationException("CIP Multiple Service response item offset is invalid.");

                replies[i] = Slice(response, start, end - start);
            }
            return replies;
        }

        private static int ParseCipReply(byte[] response, byte expectedService)
        {
            if (response == null || response.Length < 4)
                throw new InvalidOperationException("CIP response is too short.");
            if (response[0] != expectedService)
                throw new InvalidOperationException("CIP response service mismatch.");

            byte generalStatus = response[2];
            byte additionalWords = response[3];
            int offset = 4 + additionalWords * 2;
            if (offset > response.Length)
                throw new InvalidOperationException("CIP additional status length is invalid.");
            // 0x1E 表示一个或多个内嵌服务失败；仍应解析每个响应，避免坏点拖垮整个批次。
            bool hasEmbeddedServiceError = expectedService == 0x8A && generalStatus == 0x1E;
            if (generalStatus != 0 && !hasEmbeddedServiceError)
                throw new PlcProtocolException(
                    CipStatusClassifier.Classify(generalStatus, true),
                    "CIP Multiple Service错误: general status 0x" + generalStatus.ToString("X2"),
                    "0x" + generalStatus.ToString("X2"));

            return offset;
        }

        private static bool IsCommunicationException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is PlcCommunicationException ||
                    current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException)
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("socket", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unreachable", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
        }

        private static void EnsureSupportedType(PlcDataType dataType)
        {
            if (dataType == PlcDataType.Coil ||
                dataType == PlcDataType.CoilArray ||
                dataType == PlcDataType.DiscreteInput ||
                dataType == PlcDataType.DiscreteInputArray)
                throw new NotSupportedException("Rockwell CIP does not support Modbus Coil/Discrete Input data types. Use BOOL or BOOL[] instead.");
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private static void PutUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
