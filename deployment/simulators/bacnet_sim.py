import asyncio
import os
import socket
from argparse import Namespace

from bacpypes3.app import Application
from bacpypes3.local.analog import AnalogValueObject
from bacpypes3.local.binary import BinaryValueObject
from bacpypes3.local.multistate import MultiStateValueObject
from bacpypes3.object import (
    CharacterStringValueObject,
    IntegerValueObject,
    LargeAnalogValueObject,
    PositiveIntegerValueObject,
)


DEVICE_INSTANCE = 599


def local_ipv4():
    configured = os.getenv("BACNET_IP", "").strip()
    if configured:
        return configured
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.connect(("8.8.8.8", 80))
        return sock.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        sock.close()


async def main():
    ip = local_ipv4()
    prefix = int(os.getenv("BACNET_PREFIX", "24"))
    address = f"{ip}/{prefix}"
    args = Namespace(
        name="IPC BACnet Simulator",
        instance=DEVICE_INSTANCE,
        address=address,
        network=None,
        foreign=None,
        bbmd=None,
        vendoridentifier=999,
    )
    app = Application.from_args(args)

    objects = []
    float_values = (25.5, -12.75, 100.125, 0.5)
    double_values = (1234.5678, -9876.5432, 0.000125, 1_000_000.25)
    int_values = (-123456, 987654, -42, 2_000_000_000)
    uint_values = (1, 345678, 4_000_000_000, 99)
    for index in range(1, 5):
        objects.extend(
            [
                BinaryValueObject(objectIdentifier=("binary-value", index), objectName=f"Bool{index}", presentValue="active" if index % 2 else "inactive"),
                AnalogValueObject(objectIdentifier=("analog-value", index), objectName=f"Float{index}", presentValue=float_values[index - 1], units="no-units"),
                LargeAnalogValueObject(objectIdentifier=("large-analog-value", index), objectName=f"Double{index}", presentValue=double_values[index - 1]),
                IntegerValueObject(objectIdentifier=("integer-value", index), objectName=f"Int{index}", presentValue=int_values[index - 1]),
                PositiveIntegerValueObject(objectIdentifier=("positive-integer-value", index), objectName=f"UInt{index}", presentValue=uint_values[index - 1]),
                CharacterStringValueObject(objectIdentifier=("characterstring-value", index), objectName=f"String{index}", presentValue=f"IPC-BACNET-{index}"),
                MultiStateValueObject(
                    objectIdentifier=("multi-state-value", index),
                    objectName=f"State{index}",
                    presentValue=index,
                    numberOfStates=4,
                    stateText=["Stopped", "Running", "Paused", "Fault"],
                ),
            ]
        )
    for obj in objects:
        app.add_object(obj)

    print(f"BACnet/IP simulator device={DEVICE_INSTANCE} listening on {address}:47808", flush=True)
    try:
        await asyncio.Future()
    finally:
        app.close()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
