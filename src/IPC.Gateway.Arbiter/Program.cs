using IPC.Gateway.Arbiter;

await ArbiterHost.RunAsync(args, Environment.GetEnvironmentVariable("IPC_ARBITER_CONFIG"),
    Environment.GetEnvironmentVariable("IPC_ARBITER_EXPECTED_NODE"), embedded: false, CancellationToken.None);
