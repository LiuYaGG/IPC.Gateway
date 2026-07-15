using System.Reflection;

namespace IPC.Gateway.Tests;

public sealed class Dnp3RuntimeCompatibilityTests
{
    [Fact]
    public void OpenDnp3Manager_LoadsOnCurrentRuntime()
    {
        string pluginDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "IPC.Gateway.LegacyProtocolPlugins", "bin", "Debug", "net10.0"));
        Assembly contract = Assembly.LoadFrom(Path.Combine(pluginDirectory, "DNP3CLRInterface.dll"));
        Assembly adapter = Assembly.LoadFrom(Path.Combine(pluginDirectory, "DNP3CLRAdapter.dll"));
        Type factory = adapter.GetType("Automatak.DNP3.Adapter.DNP3ManagerFactory", throwOnError: true)!;
        Type handlerType = contract.GetType("Automatak.DNP3.Interface.ILogHandler", throwOnError: true)!;
        object handler = DispatchProxy.Create(handlerType, typeof(NullDispatchProxy));
        MethodInfo create = factory.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(item => item.Name == "CreateManager" && item.GetParameters().Length == 2);
        object manager = create.Invoke(null, [1, handler])!;
        manager.GetType().GetMethod("Shutdown")!.Invoke(manager, null);
    }

    private class NullDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
