namespace IPC.Plc.Communication.MitsubishiMc
{
    internal interface IMcFrameCodec
    {
        int ResponseHeaderLength { get; }
        byte[] BuildRequest(ushort command, ushort subcommand, McAddress address, int points, byte[] data);
        int GetResponseDataLength(byte[] header);
        byte[] ParseResponse(byte[] response, byte[] request);
    }

    internal static class McFrameCodecFactory
    {
        public static IMcFrameCodec Create(McDriverOptions options)
        {
            bool frame4E = string.Equals(options.FrameType, "4E", System.StringComparison.OrdinalIgnoreCase);
            bool ascii = string.Equals(options.DataCode, "ASCII", System.StringComparison.OrdinalIgnoreCase);
            return ascii
                ? new McAsciiFrameCodec(options, frame4E)
                : new McBinaryFrameCodec(options, frame4E);
        }
    }
}
