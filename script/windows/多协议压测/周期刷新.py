"""本机模拟器的固定节拍。--once 可用于单轮验证。"""
import os
from pathlib import Path
import argparse
import time
from contextlib import closing



def run_cycles(write_cycle, once=False, retry_errors=()):
    deadline = time.monotonic()
    cycle = 0
    stop = os.environ.get("IPC_PRESSURE_STOP_FILE")
    while not stop or not Path(stop).exists():
        try:
            write_cycle(cycle)
        except retry_errors as exc:
            if once:
                raise
            print(f"第 {cycle + 1} 轮中断，500 ms 后重试: {exc}", flush=True)
            time.sleep(0.5)
            deadline = time.monotonic()
            continue
        cycle += 1
        deadline += 0.5
        remaining = deadline - time.monotonic()
        if once or cycle == 1 or cycle % 120 == 0:
            print(f"已完成 {cycle} 轮，本轮剩余 {remaining * 1000:.0f} ms", flush=True)
        if once:
            return
        if remaining < 0:
            print(f"本轮超时 {-remaining * 1000:.0f} ms，跳过错过的节拍", flush=True)
            deadline += (int(-remaining / 0.5) + 1) * 0.5
        time.sleep(max(0, deadline - time.monotonic()))


def run_modbus_tcp():
    from pymodbus.client import ModbusTcpClient
    from pymodbus.exceptions import ModbusException
    parser = argparse.ArgumentParser()
    parser.add_argument("--once", action="store_true")
    args = parser.parse_args()
    port = 5020
    with closing(ModbusTcpClient("127.0.0.1", port=port, timeout=1, retries=0)) as client:
        def write_cycle(cycle):
            if not client.connect():
                raise ConnectionError(f"本机模拟器未启动，端口 {port}")
            for start in range(0, 1000, 120):
                values = [(cycle + i + 1) % 32768 for i in range(start, min(start + 120, 1000))]
                result = client.write_registers(start, values, device_id=1)
                if result.isError() or result.address != start or result.count != len(values):
                    raise RuntimeError(f"保持寄存器 {start} 写入失败: {result}")

        run_cycles(write_cycle, args.once, (OSError, ModbusException))
