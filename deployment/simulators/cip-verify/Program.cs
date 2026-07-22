using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;

PlcConnectionOptions connection = new()
{
    Protocol = PlcProtocol.RockwellCip,
    Host = "127.0.0.1",
    Port = 44818,
    Slot = 0,
    TimeoutMilliseconds = 5000,
    DriverOptionsJson = """
    {
      "controllerProfile": "Generic",
      "cipRouteMode": "Direct",
      "cipBoolArrayMode": "NativeBool",
      "cipStringFormat": "CipString"
    }
    """
};

using IPlcClient client = new RockwellCipProtocolDriver().CreateClient(connection);
client.Connect();

bool originalBool = Convert.ToBoolean(client.Read("BoolTags[0]", PlcDataType.Bool, 1, 0).Value);
string originalString = Convert.ToString(client.Read("StringTags[0]", PlcDataType.String, 1, 0).Value) ?? string.Empty;
bool testBool = !originalBool;
const string testString = "IPC-CIP";

try
{
    client.Write("BoolTags[0]", PlcDataType.Bool, testBool ? "true" : "false", 0);
    client.Write("StringTags[0]", PlcDataType.String, testString, 0);

    bool actualBool = Convert.ToBoolean(client.Read("BoolTags[0]", PlcDataType.Bool, 1, 0).Value);
    string actualString = Convert.ToString(client.Read("StringTags[0]", PlcDataType.String, 1, 0).Value) ?? string.Empty;

    if (actualBool != testBool)
        throw new InvalidOperationException($"Native BOOL write/read mismatch: expected {testBool}, actual {actualBool}");
    if (!string.Equals(actualString, testString, StringComparison.Ordinal))
        throw new InvalidOperationException($"CIP STRING write/read mismatch: expected '{testString}', actual '{actualString}'");

    Console.WriteLine($"PASS CIP NativeBool: BoolTags[0]={actualBool}");
    Console.WriteLine($"PASS CIP 0xD0 STRING: StringTags[0]={actualString}");
}
finally
{
    client.Write("BoolTags[0]", PlcDataType.Bool, originalBool ? "true" : "false", 0);
    client.Write("StringTags[0]", PlcDataType.String, originalString, 0);
}
