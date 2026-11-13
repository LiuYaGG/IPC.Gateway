"""为本机 ab_server 2.7.2 提供网关使用的 ListIdentity 和会话注册接口。"""
from contextlib import contextmanager
import socket
import socketserver
import struct
import threading

HEADER = struct.Struct("<HHII8sI")
LOCAL = "127.0.0.1"


def read_exact(sock, count):
    data = bytearray()
    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk:
            raise EOFError
        data.extend(chunk)
    return bytes(data)


def frame(sock):
    header = HEADER.unpack(read_exact(sock, HEADER.size))
    return header, read_exact(sock, header[1])


def simulator_identity(port):
    name = b"Local ab_server Simulator"
    address = struct.pack(">HH4s8x", socket.AF_INET, port, socket.inet_aton(LOCAL))
    item = struct.pack("<H", 1) + address + struct.pack("<HHHBBHIB", 0, 14, 0, 2, 7, 0, 1, len(name)) + name + b"\x03"
    return struct.pack("<HHH", 1, 0x0C, len(item)) + item


@contextmanager
def gateway_endpoint(port, simulator_port):
    class Handler(socketserver.BaseRequestHandler):
        def handle(self):
            try:
                with socket.create_connection((LOCAL, simulator_port), timeout=5) as simulator:
                    simulator.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                    self.request.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                    while True:
                        (command, length, session, status, context, options), body = frame(self.request)
                        if command == 0x63:
                            identity = simulator_identity(port)
                            self.request.sendall(HEADER.pack(command, len(identity), 0, 0, context, 0) + identity)
                            continue
                        # ab_server 仅接受上下文为零的 RegisterSession。
                        inner_context = bytes(8) if command == 0x65 else context
                        simulator.sendall(HEADER.pack(command, length, session, status, inner_context, options) + body)
                        if command == 0x66:
                            return
                        response, payload = frame(simulator)
                        reply_context = context if command == 0x65 else response[4]
                        self.request.sendall(HEADER.pack(*response[:4], reply_context, response[5]) + payload)
            except (EOFError, OSError):
                return

    with socketserver.ThreadingTCPServer((LOCAL, port), Handler, bind_and_activate=False) as listener:
        listener.daemon_threads = True
        listener.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        listener.server_bind()
        listener.server_activate()
        worker = threading.Thread(target=listener.serve_forever, daemon=True)
        worker.start()
        try:
            yield
        finally:
            listener.shutdown()
            worker.join()
