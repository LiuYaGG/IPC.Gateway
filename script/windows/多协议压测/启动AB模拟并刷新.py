"""启动专用 ab_server 子进程，同时每 500ms 更新 1000 个逻辑点位。"""
import argparse
import ctypes as C
from pathlib import Path
import socket
import struct
import subprocess
import time
from AB点表 import points
from AB模拟器接口 import gateway_endpoint
from 周期刷新 import run_cycles


def wait_ready(dll, handles, timeout=10):
    pending = set(handles)
    end = time.monotonic() + timeout
    while pending:
        for handle in tuple(pending):
            rc = dll.plc_tag_status(handle)
            if rc < 0:
                raise RuntimeError(f"libplctag handle={handle} status={rc}")
            if rc == 0:
                pending.remove(handle)
        if pending:
            if time.monotonic() > end:
                raise TimeoutError(f"{len(pending)} 个 AB 请求尚未完成")
            time.sleep(0.001)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tools", type=Path, default=Path(r"D:\libplctag_2.7.2_windows_x64_tools"))
    parser.add_argument("--port", type=int, default=44818)
    parser.add_argument("--once", action="store_true")
    args = parser.parse_args()
    tools = args.tools.resolve()
    dll = C.CDLL(str(tools / "plctag.dll"))
    points_list = list(points())
    sizes = {}
    formats = {}
    for name, kind, _, fmt, count in points_list:
        sizes[kind] = sizes.get(kind, 0) + count
        formats[kind] = fmt
    blocks = []
    for kind, size in sizes.items():
        limit = 400 // struct.calcsize("<" + formats[kind])
        for start in range(0, size, limit):
            blocks.append((f"PT{kind}[{start}]", kind, formats[kind], start, min(size - start, limit)))
    # ab_server 不提供绑定单一接口的选项；拒绝复用已存在的服务。
    with socket.socket() as check:
        check.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        check.bind(("0.0.0.0", 0))
        simulator_port = check.getsockname()[1]
    with subprocess.Popen([str(tools / "ab_server.exe"), "--plc=ControlLogix", "--path=1,0",
                           f"--port={simulator_port}", *(f"--tag=PT{k}:{k}[{count}]" for k,count in sizes.items())],
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as server:
        handles = []
        try:
            end = time.monotonic() + 5
            while True:
                if server.poll() is not None:
                    raise RuntimeError(f"ab_server 启动失败，退出码 {server.returncode}")
                try:
                    with socket.create_connection(("127.0.0.1", simulator_port), timeout=.2):
                        break
                except OSError:
                    if time.monotonic() > end:
                        raise
                    time.sleep(.05)
            for n, _, _, _, count in blocks:
                attrs = f"protocol=ab_eip&gateway=127.0.0.1:{simulator_port}&path=1,0&plc=ControlLogix&allow_packing=0&name={n}&elem_count={count}"
                h = dll.plc_tag_create(attrs.encode(), 0)
                if h < 0:
                    raise RuntimeError(f"{n} 初始化失败: {h}")
                handles.append(h)
                if len(handles) % 50 == 0:
                    wait_ready(dll, handles[-50:])
            wait_ready(dll, handles)
            def write_cycle(cycle):
                if server.poll() is not None:
                    raise RuntimeError("AB 模拟器已退出")
                for handle, (name, kind, fmt, start, count) in zip(handles, blocks):
                    base = start + cycle
                    if kind == "STRING":
                        strings = [f"PT{start+j}-{cycle % 1000000}".encode() for j in range(count)]
                        data = b"".join(struct.pack("<I84s", len(text), text) for text in strings)
                    else:
                        values = [(base + j) % 2 if kind == "BOOL" else
                                  (base + j) % 240 - 120 if kind == "SINT" else
                                  (base + j) % 30000 + (0.25 if kind in ("REAL", "LREAL") else 0) for j in range(count)]
                        data = struct.pack("<" + fmt * count, *values)
                    if dll.plc_tag_get_size(handle) != len(data):
                        raise RuntimeError(f"{name} 标签长度不匹配")
                    rc = dll.plc_tag_set_raw_bytes(handle, 0, data, len(data))
                    if rc == 0:
                        rc = dll.plc_tag_write(handle, 0)
                    if rc < 0:
                        raise RuntimeError(f"{name} 写入失败: {rc}")
                wait_ready(dll, handles)
            with gateway_endpoint(args.port, simulator_port):
                run_cycles(write_cycle, args.once)
        finally:
            for h in handles:
                dll.plc_tag_destroy(h)
            server.terminate()


if __name__ == "__main__":
    main()
