using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.CanOpen
{
    internal sealed class SlcanAdapter : IDisposable
    {
        private readonly PlcConnectionOptions _options;
        private readonly int _canBitRate;
        private SerialPort _port;

        public SlcanAdapter(PlcConnectionOptions options, int canBitRate)
        {
            _options = options ?? throw new ArgumentNullException("options");
            _canBitRate = canBitRate;
        }

        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }

        public void Open()
        {
            if (IsOpen)
                return;

            int baudRate = _options.Port > 0 ? _options.Port : 115200;
            int timeout = _options.TimeoutMilliseconds > 0 ? _options.TimeoutMilliseconds : 3000;
            _port = new SerialPort(_options.Host, baudRate, SerialPortOptionMapper.MapParity(_options.SerialParity), _options.DataBits, SerialPortOptionMapper.MapStopBits(_options.SerialStopBits))
            {
                ReadTimeout = timeout,
                WriteTimeout = timeout,
                NewLine = "\r",
                Encoding = Encoding.ASCII
            };

            _port.Open();
            SendCommand("C");
            SendCommand("S" + MapBitRate(_canBitRate));
            SendCommand("O");
            DrainPendingFrames();
        }

        public void Close()
        {
            SerialPort port = _port;
            _port = null;
            if (port == null)
                return;

            try
            {
                if (port.IsOpen)
                    port.Write("C\r");
            }
            catch
            {
            }

            port.Dispose();
        }

        public void SendFrame(CanFrame frame)
        {
            if (!IsOpen)
                Open();
            if (frame.Identifier < 0 || frame.Identifier > 0x7FF)
                throw new ArgumentOutOfRangeException("frame");
            if (frame.Data.Length > 8)
                throw new ArgumentException("CAN frame data cannot exceed 8 bytes.", "frame");

            StringBuilder builder = new StringBuilder();
            builder.Append('t');
            builder.Append(frame.Identifier.ToString("X3", CultureInfo.InvariantCulture));
            builder.Append(frame.Data.Length.ToString("X1", CultureInfo.InvariantCulture));
            for (int i = 0; i < frame.Data.Length; i++)
                builder.Append(frame.Data[i].ToString("X2", CultureInfo.InvariantCulture));
            builder.Append('\r');
            _port.Write(builder.ToString());
        }

        public CanFrame ReceiveFrame(int expectedIdentifier)
        {
            if (!IsOpen)
                Open();

            while (true)
            {
                string line = ReadFrameLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                CanFrame frame;
                if (!TryParseFrame(line, out frame))
                    continue;
                if (expectedIdentifier < 0 || frame.Identifier == expectedIdentifier)
                    return frame;
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void SendCommand(string command)
        {
            _port.Write(command + "\r");
        }

        private string ReadFrameLine()
        {
            try
            {
                return _port.ReadLine();
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException("SLCAN receive timed out.", ex);
            }
        }

        private void DrainPendingFrames()
        {
            int originalTimeout = _port.ReadTimeout;
            try
            {
                _port.ReadTimeout = 10;
                while (true)
                    _port.ReadLine();
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                _port.ReadTimeout = originalTimeout;
            }
        }

        private static bool TryParseFrame(string line, out CanFrame frame)
        {
            frame = default;
            string text = (line ?? string.Empty).Trim();
            if (text.Length < 5 || text[0] != 't')
                return false;

            int identifier;
            int dataLength;
            if (!int.TryParse(text.Substring(1, 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out identifier))
                return false;
            if (!int.TryParse(text.Substring(4, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dataLength))
                return false;
            if (dataLength < 0 || dataLength > 8 || text.Length < 5 + dataLength * 2)
                return false;

            byte[] data = new byte[dataLength];
            for (int i = 0; i < dataLength; i++)
                data[i] = byte.Parse(text.Substring(5 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            frame = new CanFrame(identifier, data);
            return true;
        }

        private static string MapBitRate(int bitRate)
        {
            switch (bitRate)
            {
                case 10000:
                    return "0";
                case 20000:
                    return "1";
                case 50000:
                    return "2";
                case 100000:
                    return "3";
                case 125000:
                    return "4";
                case 250000:
                    return "5";
                case 500000:
                    return "6";
                case 800000:
                    return "7";
                case 1000000:
                    return "8";
                default:
                    throw new InvalidOperationException("SLCAN only supports standard CAN bit rates: 10000, 20000, 50000, 100000, 125000, 250000, 500000, 800000, 1000000.");
            }
        }
    }
}
