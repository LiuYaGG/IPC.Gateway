import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parent
PYTHON = ROOT / "Python" / "python.exe"
RUNTIME = ROOT / "runtime"
LOGS = ROOT / "logs"
STATE = ROOT / "state.json"
STOP_FILE = ROOT / "stop.requested"

PROGRAMS = {
    "modbus": [str(PYTHON), str(RUNTIME / "modbus_sim.py")],
    "s7": [str(PYTHON), str(RUNTIME / "s7_sim.py")],
    "bacnet": [str(PYTHON), str(RUNTIME / "bacnet_sim.py")],
    "cip": [str(PYTHON), str(RUNTIME / "start_cip.py")],
    "snmp": [str(PYTHON), str(RUNTIME / "start_snmp.py")],
    "dnp3": [str(RUNTIME / "dnp3" / "Dnp3Simulator.exe")],
}


def save_state(children):
    data = {
        "supervisorPid": os.getpid(),
        "updatedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "children": {name: process.pid for name, process in children.items()},
    }
    STATE.write_text(json.dumps(data, indent=2), encoding="utf-8")


def start(name, command):
    LOGS.mkdir(parents=True, exist_ok=True)
    output = open(LOGS / f"{name}.log", "a", encoding="utf-8", buffering=1)
    output.write(f"\n[{time.strftime('%Y-%m-%d %H:%M:%S')}] starting: {' '.join(command)}\n")
    env = os.environ.copy()
    env["PYTHONUNBUFFERED"] = "1"
    return subprocess.Popen(
        command,
        cwd=str(ROOT),
        stdout=output,
        stderr=subprocess.STDOUT,
        env=env,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )


def stop_all(children):
    for process in children.values():
        if process.poll() is None:
            process.terminate()
    deadline = time.time() + 5
    for process in children.values():
        if process.poll() is None:
            try:
                process.wait(max(0.1, deadline - time.time()))
            except subprocess.TimeoutExpired:
                process.kill()
    STATE.unlink(missing_ok=True)
    STOP_FILE.unlink(missing_ok=True)


def main():
    if not PYTHON.exists():
        raise FileNotFoundError(f"Python runtime not found: {PYTHON}")
    STOP_FILE.unlink(missing_ok=True)
    stopping = False

    def request_stop(*_):
        nonlocal stopping
        stopping = True

    signal.signal(signal.SIGINT, request_stop)
    signal.signal(signal.SIGTERM, request_stop)
    children = {name: start(name, command) for name, command in PROGRAMS.items()}
    save_state(children)
    try:
        while not stopping and not STOP_FILE.exists():
            for name, process in list(children.items()):
                if process.poll() is not None:
                    time.sleep(1)
                    children[name] = start(name, PROGRAMS[name])
                    save_state(children)
            time.sleep(1)
    finally:
        stop_all(children)


if __name__ == "__main__":
    main()
