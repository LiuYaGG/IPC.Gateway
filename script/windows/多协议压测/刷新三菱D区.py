"""PortDock MC 3E 二进制模式，仅操作本机 D0..D1999。"""
import argparse
import socket
import struct
from 周期刷新 import run_cycles


def receive(sock, length):
    data = bytearray()
    while len(data) < length:
        part = sock.recv(length - len(data))
        if not part:
            raise ConnectionError("PortDock 关闭连接")
        data.extend(part)
    return bytes(data)


def request(sock, command, address, count, values=b""):
    payload = struct.pack("<HHH", 16, command, 0) + address.to_bytes(3, "little") + bytes([0xA8]) + struct.pack("<H", count) + values
    sock.sendall(bytes.fromhex("500000ffff0300") + struct.pack("<H", len(payload)) + payload)
    header = receive(sock, 9)
    if header[:2] != bytes.fromhex("d000"):
        raise ValueError("不是 MC 3E 二进制响应")
    reply = receive(sock, int.from_bytes(header[7:9], "little"))
    if len(reply) < 2 or reply[:2] != bytes(2):
        raise ValueError(f"D{address} MC 错误 {reply[:2][::-1].hex()}")
    return reply[2:]


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--once", action="store_true")
    args = parser.parse_args()
    sock = None

    def write_cycle(cycle):
        global sock
        try:
            if sock is None:
                sock = socket.create_connection(("127.0.0.1", 5000), timeout=2)
            for start in range(0, 2000, 400):
                values = [(i + cycle + 1) % 32768 for i in range(start, start + 400)]
                request(sock, 0x1401, start, 400, struct.pack("<400H", *values))
        except OSError:
            if sock is not None:
                sock.close()
                sock = None
            raise

    try:
        run_cycles(write_cycle, args.once, (OSError,))
    finally:
        if sock is not None:
            sock.close()
