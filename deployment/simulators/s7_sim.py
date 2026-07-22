import logging
import signal
import struct
import time
from ctypes import c_uint8

import snap7
from snap7.type import SrvArea


PORT = 1102
DB_NUMBER = 1
BUFFER_SIZE = 1024


def put(buffer: bytearray, offset: int, fmt: str, value):
    raw = struct.pack(">" + fmt, value)
    buffer[offset : offset + len(raw)] = raw


def build_db():
    data = bytearray(BUFFER_SIZE)
    data[0] = 0b00001101  # DB1.DBX0.0 .. DBX0.7
    put(data, 2, "b", -12)  # DB1.DBB2 Int8
    put(data, 3, "B", 250)  # DB1.DBB3 UInt8
    put(data, 4, "h", -1234)  # DB1.DBW4 Int16
    put(data, 6, "H", 54321)  # DB1.DBW6 UInt16
    put(data, 8, "i", -12345678)  # DB1.DBD8 Int32
    put(data, 12, "I", 3456789012)  # DB1.DBD12 UInt32
    put(data, 16, "q", -1234567890123)  # DB1.DBB16 Int64
    put(data, 24, "Q", 12345678901234)  # DB1.DBB24 UInt64
    put(data, 32, "f", 25.5)  # DB1.DBD32 Float
    put(data, 36, "d", 1234.5678)  # DB1.DBB36 Double

    text = b"IPC-S7-TEST"
    data[48] = 30  # S7 string maximum length
    data[49] = len(text)
    data[50 : 50 + len(text)] = text

    for i, value in enumerate((-10, 20, -30, 40)):
        put(data, 100 + i * 2, "h", value)
    for i, value in enumerate((1.1, 2.2, 3.3, 4.4)):
        put(data, 120 + i * 4, "f", value)

    data[160] = 0b01011010  # Bool[8]
    for i, value in enumerate((-10, 20, -30, 40)):
        put(data, 168 + i, "b", value)
    for i, value in enumerate((10, 20, 30, 240)):
        put(data, 172 + i, "B", value)
    for i, value in enumerate((100, 2000, 30000, 60000)):
        put(data, 180 + i * 2, "H", value)
    for i, value in enumerate((-100000, 200000, -300000, 400000)):
        put(data, 188 + i * 4, "i", value)
    for i, value in enumerate((100000, 200000, 300000, 4000000000)):
        put(data, 204 + i * 4, "I", value)
    for i, value in enumerate((-10**10, 2 * 10**10, -3 * 10**10, 4 * 10**10)):
        put(data, 220 + i * 8, "q", value)
    for i, value in enumerate((10**10, 2 * 10**10, 3 * 10**10, 4 * 10**10)):
        put(data, 252 + i * 8, "Q", value)
    for i, value in enumerate((1.125, -2.25, 3.5, 4.875)):
        put(data, 284 + i * 8, "d", value)
    for i, value in enumerate((b"S7-STRING-1", b"S7-STRING-2", b"S7-STRING-3", b"S7-STRING-4")):
        start = 320 + i * 32
        data[start] = 30
        data[start + 1] = len(value)
        data[start + 2 : start + 2 + len(value)] = value
    return data


def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    raw_db = build_db()
    raw_mk = bytearray(256)
    db_buffer = (c_uint8 * len(raw_db)).from_buffer(raw_db)
    mk_buffer = (c_uint8 * len(raw_mk)).from_buffer(raw_mk)

    server = snap7.Server(log=False)
    server.register_area(SrvArea.DB, DB_NUMBER, db_buffer)
    server.register_area(SrvArea.MK, 0, mk_buffer)
    server.start(tcp_port=PORT)
    stopping = False

    def stop(*_):
        nonlocal stopping
        stopping = True

    signal.signal(signal.SIGINT, stop)
    signal.signal(signal.SIGTERM, stop)
    logging.info("S7 simulator listening on 0.0.0.0:%s, rack=0 slot=1, DB=%s", PORT, DB_NUMBER)
    try:
        while not stopping:
            time.sleep(1)
    finally:
        server.stop()
        server.destroy()


if __name__ == "__main__":
    main()
