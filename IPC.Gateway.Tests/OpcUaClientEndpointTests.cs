using System.Reflection;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.OpcUa;

namespace IPC.Gateway.Tests;

public sealed class OpcUaClientEndpointTests
{
    [Theory]
    [InlineData("opc.tcp://127.0.0.1", 49320, "opc.tcp://127.0.0.1:49320/")]
    [InlineData("opc.tcp://127.0.0.1:49320", 4840, "opc.tcp://127.0.0.1:49320")]
    [InlineData("opc.tcp://localhost/UA", 49320, "opc.tcp://localhost:49320/UA")]
    [InlineData("127.0.0.1", 49320, "opc.tcp://127.0.0.1:49320/")]
    public void BuildEndpoint_UsesConfiguredPortWhenEndpointOmitsPort(string host, int port, string expected)
    {
        using OpcUaClient client = new OpcUaClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OpcUa,
            Host = host,
            Port = port
        });

        Assert.Equal(expected, InvokeBuildEndpoint(client));
    }

    [Fact]
    public void BatchReadExceptionClassifier_SplitsUnavailableErrorsWhenSessionIsConnected()
    {
        Exception exception = new InvalidOperationException("OPC UA node is unavailable.");

        Assert.True(InvokeShouldSplitBatchReadException(exception, true, 8));
        Assert.False(InvokeShouldTreatBatchReadExceptionAsCommunication(exception, true));
        Assert.Equal(PlcReadFailureScope.Batch, InvokeClassifyBatchReadExceptionScope(exception, true, 8));
        Assert.Equal(PlcReadFailureScope.Tag, InvokeClassifyBatchReadExceptionScope(exception, true, 1));
    }

    [Fact]
    public void BatchReadExceptionClassifier_TreatsUnavailableErrorsAsDownstreamWhenSessionStateIsStale()
    {
        Exception exception = new InvalidOperationException("OPC UA node is unavailable.");

        Assert.True(InvokeShouldSplitBatchReadException(exception, false, 8));
        Assert.False(InvokeShouldTreatBatchReadExceptionAsCommunication(exception, false));
        Assert.Equal(PlcReadFailureScope.Batch, InvokeClassifyBatchReadExceptionScope(exception, false, 8));
        Assert.Equal(PlcReadFailureScope.Tag, InvokeClassifyBatchReadExceptionScope(exception, false, 1));
    }

    [Fact]
    public void BatchReadExceptionClassifier_TreatsSessionErrorsAsSessionFailure()
    {
        Exception exception = new InvalidOperationException("OPC UA client is not connected.");

        Assert.False(InvokeShouldSplitBatchReadException(exception, false, 8));
        Assert.True(InvokeShouldTreatBatchReadExceptionAsCommunication(exception, false));
        Assert.Equal(PlcReadFailureScope.Session, InvokeClassifyBatchReadExceptionScope(exception, false, 8));
    }

    private static string InvokeBuildEndpoint(OpcUaClient client)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("BuildEndpoint", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string)method!.Invoke(client, Array.Empty<object>())!;
    }

    private static bool InvokeShouldSplitBatchReadException(Exception exception, bool sessionConnected, int count)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("ShouldSplitBatchReadException", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object[] { exception, sessionConnected, count })!;
    }

    private static bool InvokeShouldTreatBatchReadExceptionAsCommunication(Exception exception, bool sessionConnected)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("ShouldTreatBatchReadExceptionAsCommunication", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object[] { exception, sessionConnected })!;
    }

    private static PlcReadFailureScope InvokeClassifyBatchReadExceptionScope(Exception exception, bool sessionConnected, int count)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("ClassifyBatchReadExceptionScope", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (PlcReadFailureScope)method!.Invoke(null, new object[] { exception, sessionConnected, count })!;
    }
}
