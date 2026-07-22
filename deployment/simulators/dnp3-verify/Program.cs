using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Dnp3;

PlcConnectionOptions connection = new()
{
    Protocol = PlcProtocol.Dnp3,
    Host = "127.0.0.1",
    Port = 20000,
    TimeoutMilliseconds = 5000,
    DriverOptionsJson = """
    {
      "dnp3LocalAddress": 1,
      "dnp3RemoteAddress": 10,
      "dnp3SelectBeforeOperate": true,
      "dnp3StartupIntegrity": false,
      "dnp3EnableUnsolicited": false,
      "dnp3EventScanIntervalSeconds": 0,
      "dnp3IntegrityScanIntervalSeconds": 0,
      "dnp3CacheMaxAgeMilliseconds": 0
    }
    """
};

using Dnp3Client client = new(connection);
client.Connect();

PlcReadResult binary = client.Read("Binary:0", PlcDataType.Bool, 1, 0);
PlcReadResult analog = client.Read("Analog:1", PlcDataType.Double, 1, 0);
PlcReadResult counter = client.Read("Counter:2", PlcDataType.UInt32, 1, 0);

client.Write("BinaryOutput:0", PlcDataType.Bool, "true", 0);
client.Write("AnalogOutput:1", PlcDataType.Int32, "321", 0);
Thread.Sleep(300);

PlcReadResult binaryOutput = client.Read("BinaryOutput:0", PlcDataType.Bool, 1, 0);
PlcReadResult analogOutput = client.Read("AnalogOutput:1", PlcDataType.Double, 1, 0);

Console.WriteLine($"DNP3 output values after write: BinaryOutput:0={binaryOutput.Value}, AnalogOutput:1={analogOutput.Value}");

if (!Convert.ToBoolean(binaryOutput.Value))
    throw new InvalidOperationException("DNP3 binary output write did not persist");
if (Convert.ToInt32(analogOutput.Value) != 321)
    throw new InvalidOperationException("DNP3 analog output write did not persist");

Console.WriteLine($"PASS DNP3 read: Binary:0={binary.Value}, Analog:1={analog.Value}, Counter:2={counter.Value}");
Console.WriteLine($"PASS DNP3 write: BinaryOutput:0={binaryOutput.Value}, AnalogOutput:1={analogOutput.Value}");
