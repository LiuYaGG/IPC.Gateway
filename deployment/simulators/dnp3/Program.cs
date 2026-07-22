using Automatak.DNP3.Adapter;
using Automatak.DNP3.Interface;

const ushort Port = 20000;
const ushort MasterAddress = 1;
const ushort OutstationAddress = 10;
const ushort PointCount = 4;

IDNP3Manager manager = DNP3ManagerFactory.CreateManager(1, new PrintingLogAdapter());
IChannel channel = manager.AddTCPServer(
    "ipc-simulator-dnp3-server",
    LogLevels.NORMAL,
    ServerAcceptMode.CloseExisting,
    new IPEndpoint("0.0.0.0", Port),
    ChannelListener.None());

OutstationStackConfig config = new();
for (ushort index = 0; index < PointCount; index++)
{
    config.databaseTemplate.binary.Add(index, new BinaryConfig());
    config.databaseTemplate.doubleBinary.Add(index, new DoubleBinaryConfig());
    config.databaseTemplate.analog.Add(index, new AnalogConfig
    {
        staticVariation = StaticAnalogVariation.Group30Var6,
        eventVariation = EventAnalogVariation.Group32Var6
    });
    config.databaseTemplate.counter.Add(index, new CounterConfig());
    config.databaseTemplate.frozenCounter.Add(index, new FrozenCounterConfig());
    config.databaseTemplate.binaryOutputStatus.Add(index, new BinaryOutputStatusConfig());
    config.databaseTemplate.analogOutputStatus.Add(index, new AnalogOutputStatusConfig
    {
        staticVariation = StaticAnalogOutputStatusVariation.Group40Var4,
        eventVariation = EventAnalogOutputStatusVariation.Group42Var4
    });
}

config.outstation.config.allowUnsolicited = true;
config.outstation.buffer = new EventBufferConfig(32);
config.link.localAddr = OutstationAddress;
config.link.remoteAddr = MasterAddress;

ICommandHandler commands = new WritableCommandHandler();
IOutstation outstation = channel.AddOutstation(
    "ipc-simulator-dnp3-outstation",
    commands,
    DefaultOutstationApplication.Instance,
    config);

outstation.Enable();
LoadInitialValues(outstation);

Console.WriteLine($"DNP3 simulator listening on 0.0.0.0:{Port}; master={MasterAddress}; outstation={OutstationAddress}");
Console.WriteLine("Points 0..3: Binary, DoubleBitBinary, Analog, Counter, FrozenCounter, BinaryOutput, AnalogOutput");

using ManualResetEventSlim stop = new(false);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Set();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Set();
stop.Wait();

outstation.Shutdown();
channel.Shutdown();
manager.Shutdown();

static void LoadInitialValues(IOutstation outstation)
{
    Flags binaryFlags = new();
    binaryFlags.Set(BinaryQuality.ONLINE);
    Flags doubleBinaryFlags = new();
    doubleBinaryFlags.Set(DoubleBitBinaryQuality.ONLINE);
    Flags analogFlags = new();
    analogFlags.Set(AnalogQuality.ONLINE);
    Flags counterFlags = new();
    counterFlags.Set(CounterQuality.ONLINE);
    Flags binaryOutputFlags = new();
    binaryOutputFlags.Set(BinaryOutputStatusQuality.ONLINE);
    Flags analogOutputFlags = new();
    analogOutputFlags.Set(AnalogOutputStatusQuality.ONLINE);

    ChangeSet changes = new();
    double[] analogValues = [12, -7, 100, 1];
    uint[] counterValues = [100, 200, 300, 400];
    for (ushort index = 0; index < PointCount; index++)
    {
        changes.Update(new Binary(index % 2 == 0, binaryFlags), index);
        changes.Update(new DoubleBitBinary(index % 2 == 0 ? DoubleBit.DETERMINED_ON : DoubleBit.DETERMINED_OFF, doubleBinaryFlags), index);
        changes.Update(new Analog(analogValues[index], analogFlags), index);
        changes.Update(new Counter(counterValues[index], counterFlags), index);
        changes.Update(new BinaryOutputStatus(index % 2 != 0, binaryOutputFlags), index);
        changes.Update(new AnalogOutputStatus(analogValues[index] * 2, analogOutputFlags), index);
    }
    outstation.Load(changes);

    ChangeSet freeze = new();
    for (ushort index = 0; index < PointCount; index++)
        freeze.FreezeCounter(index, false, EventMode.Detect);
    outstation.Load(freeze);
}

sealed class WritableCommandHandler : ICommandHandler
{
    private const ushort WritablePointCount = 4;

    public void Begin() { }
    public void End() { }

    public CommandStatus Select(ControlRelayOutputBlock command, ushort index) => ValidateIndex(index);
    public CommandStatus Select(AnalogOutputInt32 command, ushort index) => ValidateIndex(index);
    public CommandStatus Select(AnalogOutputInt16 command, ushort index) => ValidateIndex(index);
    public CommandStatus Select(AnalogOutputFloat32 command, ushort index) => ValidateIndex(index);
    public CommandStatus Select(AnalogOutputDouble64 command, ushort index) => ValidateIndex(index);

    public CommandStatus Operate(ControlRelayOutputBlock command, ushort index, IDatabase database, OperateType operateType)
    {
        CommandStatus status = ValidateIndex(index);
        if (status != CommandStatus.SUCCESS) return status;
        bool value = command.opType switch
        {
            OperationType.LATCH_ON or OperationType.PULSE_ON => true,
            OperationType.LATCH_OFF or OperationType.PULSE_OFF => false,
            _ => false
        };
        Flags flags = new();
        flags.Set(BinaryOutputStatusQuality.ONLINE);
        database.Update(new BinaryOutputStatus(value, flags), index, EventMode.Detect);
        Console.WriteLine($"BinaryOutput:{index} <- {value} ({operateType})");
        return CommandStatus.SUCCESS;
    }

    public CommandStatus Operate(AnalogOutputInt32 command, ushort index, IDatabase database, OperateType operateType)
        => UpdateAnalog(command.value, index, database, operateType);

    public CommandStatus Operate(AnalogOutputInt16 command, ushort index, IDatabase database, OperateType operateType)
        => UpdateAnalog(command.value, index, database, operateType);

    public CommandStatus Operate(AnalogOutputFloat32 command, ushort index, IDatabase database, OperateType operateType)
        => UpdateAnalog(command.value, index, database, operateType);

    public CommandStatus Operate(AnalogOutputDouble64 command, ushort index, IDatabase database, OperateType operateType)
        => UpdateAnalog(command.value, index, database, operateType);

    private static CommandStatus UpdateAnalog(double value, ushort index, IDatabase database, OperateType operateType)
    {
        CommandStatus status = ValidateIndex(index);
        if (status != CommandStatus.SUCCESS) return status;
        Flags flags = new();
        flags.Set(AnalogOutputStatusQuality.ONLINE);
        database.Update(new AnalogOutputStatus(value, flags), index, EventMode.Detect);
        Console.WriteLine($"AnalogOutput:{index} <- {value} ({operateType})");
        return CommandStatus.SUCCESS;
    }

    private static CommandStatus ValidateIndex(ushort index)
        => index < WritablePointCount ? CommandStatus.SUCCESS : CommandStatus.OUT_OF_RANGE;
}
