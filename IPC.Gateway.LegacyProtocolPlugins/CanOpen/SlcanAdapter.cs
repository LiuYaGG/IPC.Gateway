using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.CanOpen
{
    internal sealed class SlcanAdapter : IDisposable
    {
        private readonly PlcConnectionOptions _options;
        private readonly int _canBitRate;
        private readonly object _writeSync = new object();
        private readonly ConcurrentDictionary<int, ConcurrentQueue<CanFrame>> _receivedFrames = new ConcurrentDictionary<int, ConcurrentQueue<CanFrame>>();
        private readonly AutoResetEvent _frameReceived = new AutoResetEvent(false);
        private SerialPort _port;
        private Thread _readerThread;
        private volatile bool _readerRunning;
        private Exception _readerFault;

        public event Action<CanFrame> FrameReceived;

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
            StartReader(_port);
        }

        public void Close()
        {
            SerialPort port = _port;
            _port = null;
            if (port == null)
                return;

            _readerRunning = false;
            try
            {
                if (port.IsOpen)
                    port.Write("C\r");
            }
            catch
            {
            }

            try { port.Close(); } catch { }
            _frameReceived.Set();
            Thread reader = _readerThread;
            _readerThread = null;
            if (reader != null && reader.IsAlive && !ReferenceEquals(reader, Thread.CurrentThread))
                reader.Join(1000);
            port.Dispose();
            _receivedFrames.Clear();
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
            lock (_writeSync)
                _port.Write(builder.ToString());
        }

        public CanFrame ReceiveFrame(int expectedIdentifier)
        {
            if (!IsOpen)
                Open();

            int timeout = _options.TimeoutMilliseconds > 0 ? _options.TimeoutMilliseconds : 3000;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (TryDequeue(expectedIdentifier, out CanFrame frame))
                    return frame;
                if (_readerFault != null)
                    throw new IOException("SLCAN receive loop stopped.", _readerFault);
                int remaining = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                _frameReceived.WaitOne(remaining);
            }
            throw new TimeoutException("SLCAN receive timed out.");
        }

        public void DiscardFrames(int identifier)
        {
            if (!_receivedFrames.TryGetValue(identifier, out ConcurrentQueue<CanFrame> queue))
                return;
            while (queue.TryDequeue(out _))
            {
            }
        }

        public void Dispose()
        {
            Close();
            _frameReceived.Dispose();
        }

        private void StartReader(SerialPort port)
        {
            _readerFault = null;
            _readerRunning = true;
            _readerThread = new Thread(() => ReaderLoop(port))
            {
                IsBackground = true,
                Name = "CANopen-SLCAN-Receive"
            };
            _readerThread.Start();
        }

        private void ReaderLoop(SerialPort port)
        {
            while (_readerRunning)
            {
                try
                {
                    string line = port.ReadLine();
                    if (!TryParseFrame(line, out CanFrame frame))
                        continue;

                    try { FrameReceived?.Invoke(frame); } catch { }
                    if (frame.Identifier >= 0x580 && frame.Identifier <= 0x5FF)
                    {
                        ConcurrentQueue<CanFrame> queue = _receivedFrames.GetOrAdd(
                            frame.Identifier,
                            _ => new ConcurrentQueue<CanFrame>());
                        queue.Enqueue(frame);
                    }
                    _frameReceived.Set();
                }
                catch (TimeoutException)
                {
                }
                catch (Exception ex)
                {
                    if (_readerRunning)
                        _readerFault = ex;
                    break;
                }
            }
            _readerRunning = false;
            _frameReceived.Set();
        }

        private bool TryDequeue(int expectedIdentifier, out CanFrame frame)
        {
            if (expectedIdentifier >= 0)
            {
                if (_receivedFrames.TryGetValue(expectedIdentifier, out ConcurrentQueue<CanFrame> queue) &&
                    queue.TryDequeue(out frame))
                    return true;
                frame = default;
                return false;
            }

            foreach (ConcurrentQueue<CanFrame> queue in _receivedFrames.Values)
            {
                if (queue.TryDequeue(out frame))
                    return true;
            }
            frame = default;
            return false;
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
