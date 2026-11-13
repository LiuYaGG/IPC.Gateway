"""沿用本机 PyModbus 配置，在 UDP 模拟器内部每 500ms 更新保持寄存器。"""
import argparse
import asyncio
import os
from pathlib import Path
import socket
from pymodbus.server.simulator.http_server import ModbusSimulatorServer


async def run(config):
    simulator = ModbusSimulatorServer("udp", "gateway_test", "127.0.0.1", 8082, json_file=str(config))
    address = simulator.modbus_server.comm_params.source_address
    if tuple(address) != ("127.0.0.1", 5021):
        raise ValueError(f"配置中的 UDP 地址与本机压测配置不匹配: {address}")
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as check:
        check.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        check.bind(address)
    stop = os.environ.get("IPC_PRESSURE_STOP_FILE")
    loop = asyncio.get_running_loop()
    deadline = loop.time()
    cycle = 0
    await simulator.run_forever(only_start=True)
    try:
        while not stop or not Path(stop).exists():
            cycle += 1
            await simulator.modbus_server.async_setValues(1, 16, 0, [(cycle+i) % 32768 for i in range(1000)])
            deadline += .5
            if cycle == 1 or cycle % 120 == 0:
                print(f"已更新全部 1000 个寄存器，共 {cycle} 轮", flush=True)
            if deadline < loop.time():
                deadline += (int((loop.time()-deadline)/.5)+1)*.5
            await asyncio.sleep(max(0, deadline-loop.time()))
    finally:
        await simulator.stop()

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=Path(r"D:\虚拟modbus\模拟器配置.json"))
    asyncio.run(run(parser.parse_args().config))
