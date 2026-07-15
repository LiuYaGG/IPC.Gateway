using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Automatak.DNP3.Interface;

namespace IPC.Plc.Communication.Dnp3
{
    internal sealed class Dnp3SoeHandler : ISOEHandler
    {
        private readonly ConcurrentDictionary<string, Dnp3CachedValue> _values = new ConcurrentDictionary<string, Dnp3CachedValue>();

        public void BeginFragment(ResponseInfo info) { }
        public void EndFragment(ResponseInfo info) { }
        public bool TryGet(Dnp3Address address, out Dnp3CachedValue value) => _values.TryGetValue(address.ToString(), out value);
        public void Clear() => _values.Clear();

        public void Process(HeaderInfo info, IEnumerable<IndexedValue<Binary>> values) => Store(info, Dnp3PointType.Binary, values);
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<DoubleBitBinary>> values) => Store(info, Dnp3PointType.DoubleBitBinary, values);
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<Analog>> values) => Store(info, Dnp3PointType.Analog, values);
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<Counter>> values) => Store(info, Dnp3PointType.Counter, values);
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<FrozenCounter>> values) => Store(info, Dnp3PointType.FrozenCounter, values);
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<BinaryOutputStatus>> values) => Store(info, Dnp3PointType.BinaryOutput, values);
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<AnalogOutputStatus>> values) => Store(info, Dnp3PointType.AnalogOutput, values);
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<OctetString>> values) { }
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<TimeAndInterval>> values) { }
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<BinaryCommandEvent>> values) { }
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<AnalogCommandEvent>> values) { }
        public void Process(HeaderInfo info, IEnumerable<IndexedValue<SecurityStat>> values) { }

        private void Store<T>(HeaderInfo info, Dnp3PointType pointType, IEnumerable<IndexedValue<T>> values) where T : MeasurementBase
        {
            foreach (IndexedValue<T> item in values)
            {
                object value = item.Value is DoubleBitBinary doubleBit
                    ? Convert.ToByte(doubleBit.Value)
                    : item.Value.GetType().GetProperty("Value")!.GetValue(item.Value)!;
                bool online = !info.flagsValid || (item.Value.Quality.Value & 0x01) != 0;
                string key = pointType + ":" + item.Index;
                _values[key] = new Dnp3CachedValue(value, online, item.Value.Quality.Value, DateTime.UtcNow);
            }
        }
    }

    internal sealed record Dnp3CachedValue(object Value, bool Online, byte Quality, DateTime TimestampUtc);
}
