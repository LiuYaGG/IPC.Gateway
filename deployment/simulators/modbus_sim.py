import asyncio
import logging
import struct

from pymodbus.datastore import (
    ModbusDeviceContext,
    ModbusSequentialDataBlock,
    ModbusServerContext,
)
from pymodbus.server import StartAsyncTcpServer


HOST = "0.0.0.0"
PORT = 1502
DEVICE_ID = 1


def words(fmt: str, value):
    raw = struct.pack(">" + fmt, value)
    return [int.from_bytes(raw[i : i + 2], "big") for i in range(0, len(raw), 2)]


def ascii_words(value: str, register_count: int):
    raw = value.encode("ascii")[: register_count * 2].ljust(register_count * 2, b"\x00")
    return [int.from_bytes(raw[i : i + 2], "big") for i in range(0, len(raw), 2)]


def build_context():
    coils = [False] * 128
    coils[0:8] = [False, True, False, True, True, False, True, False]

    discrete_inputs = [False] * 128
    discrete_inputs[0:8] = [True, False, True, False, False, True, False, True]

    holding = [0] * 256
    holding[0] = 0x04D2  # Int16 = 1234
    holding[1] = 54321  # UInt16
    holding[2:4] = words("i", -123456)  # Int32
    holding[4:6] = words("I", 3456789012)  # UInt32
    holding[6:10] = words("q", -1234567890123)  # Int64
    holding[10:14] = words("Q", 12345678901234)  # UInt64
    holding[14:16] = words("f", 25.5)  # Float32
    holding[16:20] = words("d", 1234.5678)  # Float64
    holding[20:28] = ascii_words("IPC-MODBUS-TEST", 8)  # 16-byte string
    holding[40:44] = [1, 2, 3, 4]  # UInt16 array
    holding[50:58] = words("i", -1) + words("i", 2) + words("i", -3) + words("i", 4)
    holding[64:68] = [0xFFF6, 20, 0xFFE2, 40]  # Int16[4]
    holding[68:72] = [10, 200, 3000, 40000]  # UInt16[4]
    holding[72:80] = sum((words("i", value) for value in (-10_000, 20_000, -30_000, 40_000)), [])
    holding[80:88] = sum((words("I", value) for value in (10_000, 20_000, 30_000, 4_000_000_000)), [])
    holding[88:104] = sum((words("q", value) for value in (-10**10, 2 * 10**10, -3 * 10**10, 4 * 10**10)), [])
    holding[104:120] = sum((words("Q", value) for value in (10**10, 2 * 10**10, 3 * 10**10, 4 * 10**10)), [])
    holding[120:128] = sum((words("f", value) for value in (1.25, -2.5, 3.75, 4.5)), [])
    holding[128:144] = sum((words("d", value) for value in (1.125, -2.25, 3.5, 4.875)), [])
    for index, value in enumerate(("MB-STRING-1", "MB-STRING-2", "MB-STRING-3", "MB-STRING-4")):
        start = 144 + index * 8
        holding[start : start + 8] = ascii_words(value, 8)

    input_registers = [0] * 256
    input_registers[0] = 100
    input_registers[1] = 200
    input_registers[2:4] = words("f", 66.6)
    input_registers[4:8] = words("d", 9876.54321)

    device = ModbusDeviceContext(
        # PyModbus uses protocol address 0 for a block declared at address 1.
        di=ModbusSequentialDataBlock(1, discrete_inputs),
        co=ModbusSequentialDataBlock(1, coils),
        ir=ModbusSequentialDataBlock(1, input_registers),
        hr=ModbusSequentialDataBlock(1, holding),
    )
    return ModbusServerContext(devices={DEVICE_ID: device}, single=False)


async def main():
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )
    logging.info("Modbus TCP simulator listening on %s:%s, device id %s", HOST, PORT, DEVICE_ID)
    await StartAsyncTcpServer(build_context(), address=(HOST, PORT))


if __name__ == "__main__":
    asyncio.run(main())
