import argparse
import asyncio
import socket
import struct
import subprocess
import sys


def verify_modbus():
    from pymodbus.client import ModbusTcpClient

    client = ModbusTcpClient("127.0.0.1", port=1502, timeout=3)
    assert client.connect(), "Modbus connect failed"
    try:
        before = client.read_holding_registers(0, count=1, device_id=1)
        assert not before.isError(), before
        coil_before = client.read_coils(0, count=1, device_id=1)
        assert not coil_before.isError(), coil_before
        result = client.write_register(0, 4321, device_id=1)
        assert not result.isError(), result
        after = client.read_holding_registers(0, count=1, device_id=1)
        assert after.registers[0] == 4321, after.registers
        coil = client.write_coil(0, True, device_id=1)
        assert not coil.isError(), coil
        coil_read = client.read_coils(0, count=1, device_id=1)
        assert coil_read.bits[0] is True
        print("PASS Modbus TCP read/write")
    finally:
        if "before" in locals() and not before.isError():
            client.write_register(0, before.registers[0], device_id=1)
        if "coil_before" in locals() and not coil_before.isError():
            client.write_coil(0, coil_before.bits[0], device_id=1)
        client.close()


def verify_s7():
    import snap7

    client = snap7.Client()
    client.connect("127.0.0.1", 0, 1, tcp_port=1102)
    try:
        assert client.get_connected(), "S7 connect failed"
        before = client.db_read(1, 4, 2)
        float_before = client.db_read(1, 32, 4)
        client.db_write(1, 4, bytearray(struct.pack(">h", -2222)))
        after = client.db_read(1, 4, 2)
        assert struct.unpack(">h", after)[0] == -2222
        client.db_write(1, 32, bytearray(struct.pack(">f", 31.25)))
        value = struct.unpack(">f", client.db_read(1, 32, 4))[0]
        assert abs(value - 31.25) < 0.001
        print("PASS Siemens S7 read/write")
    finally:
        if "before" in locals():
            client.db_write(1, 4, before)
        if "float_before" in locals():
            client.db_write(1, 32, float_before)
        client.disconnect()
        client.destroy()


async def verify_snmp():
    from pysnmp.hlapi.asyncio import (
        CommunityData,
        ContextData,
        ObjectIdentity,
        ObjectType,
        SnmpEngine,
        UdpTransportTarget,
        getCmd,
        setCmd,
    )
    from pysnmp.proto.rfc1902 import Integer32

    engine = SnmpEngine()
    target = UdpTransportTarget(("127.0.0.1", 1161), timeout=2, retries=1)
    oid = "1.3.6.1.4.1.53864.1.1.0"
    response = await getCmd(engine, CommunityData("public", mpModel=1), target, ContextData(), ObjectType(ObjectIdentity(oid)))
    assert response[0] is None and int(response[1]) == 0, response[:2]
    original = int(response[3][0][1])
    response = await setCmd(
        engine,
        CommunityData("public", mpModel=1),
        target,
        ContextData(),
        ObjectType(ObjectIdentity(oid), Integer32(-2468)),
    )
    assert response[0] is None and int(response[1]) == 0, response[:2]
    response = await getCmd(engine, CommunityData("public", mpModel=1), target, ContextData(), ObjectType(ObjectIdentity(oid)))
    assert int(response[3][0][1]) == -2468, response[3]
    response = await setCmd(
        engine,
        CommunityData("public", mpModel=1),
        target,
        ContextData(),
        ObjectType(ObjectIdentity(oid), Integer32(original)),
    )
    assert response[0] is None and int(response[1]) == 0, response[:2]
    print("PASS SNMP v2c GET/SET")


def local_ipv4():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("8.8.8.8", 80))
        return sock.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        sock.close()


async def verify_bacnet():
    from argparse import Namespace
    from bacpypes3.app import Application
    from bacpypes3.basetypes import BinaryPV
    from bacpypes3.pdu import Address
    from bacpypes3.primitivedata import Real

    ip = local_ipv4()
    args = Namespace(
        name="IPC BACnet Verifier",
        instance=600,
        address=f"{ip}/24:47809",
        network=None,
        foreign=None,
        bbmd=None,
        vendoridentifier=999,
    )
    app = Application.from_args(args)
    target = Address(f"{ip}:47808")
    try:
        before = await app.read_property(target, "analog-value,1", "present-value")
        binary_before = await app.read_property(target, "binary-value,1", "present-value")
        await app.write_property(target, "analog-value,1", "present-value", Real(31.25), priority=8)
        after = await app.read_property(target, "analog-value,1", "present-value")
        assert abs(float(after) - 31.25) < 0.001, after
        await app.write_property(target, "binary-value,1", "present-value", BinaryPV("inactive"), priority=8)
        binary_after = await app.read_property(target, "binary-value,1", "present-value")
        assert str(binary_after) == "inactive", binary_after
        await app.write_property(target, "analog-value,1", "present-value", Real(float(before)), priority=8)
        await app.write_property(target, "binary-value,1", "present-value", BinaryPV(binary_before), priority=8)
        print("PASS BACnet/IP read/write")
    finally:
        app.close()


def verify_cip():
    exe = r"D:\IPC-Simulators\Python\Scripts\enip_client.exe"
    read = subprocess.run(
        [exe, "-a", "127.0.0.1:44818", "-p", "DIntTags[0-3]", "RealTags[0-3]"],
        text=True,
        capture_output=True,
        timeout=15,
    )
    if read.returncode != 0:
        raise RuntimeError(read.stdout + read.stderr)
    write = subprocess.run(
        [exe, "-a", "127.0.0.1:44818", "-p", "DIntTags[0]=(DINT)12345", "RealTags[0]=(REAL)31.25"],
        text=True,
        capture_output=True,
        timeout=15,
    )
    if write.returncode != 0:
        raise RuntimeError(write.stdout + write.stderr)
    print("PASS EtherNet/IP CIP read/write")


async def main(selected):
    if "modbus" in selected:
        verify_modbus()
    if "s7" in selected:
        verify_s7()
    if "snmp" in selected:
        await verify_snmp()
    if "bacnet" in selected:
        await verify_bacnet()
    if "cip" in selected:
        verify_cip()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("protocols", nargs="*", default=["modbus", "s7", "snmp", "bacnet", "cip"])
    args = parser.parse_args()
    asyncio.run(main(set(args.protocols)))
