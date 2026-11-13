"""每个刷新器独立进程，日志与停止标志在本机临时目录 IPC-Gateway-Pressure。"""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import uuid

SCRIPTS = ["刷新三菱D区", "刷新ModbusTCP", "刷新ModbusUDP", "启动AB模拟并刷新"]
STATE = Path(tempfile.gettempdir()) / "IPC-Gateway-Pressure"


def start():
    STATE.mkdir(exist_ok=True)
    active = STATE / "active"
    generation = uuid.uuid4().hex
    # Exclusive creation prevents a second set of writers.
    try:
        with active.open("x", encoding="ascii") as f:
            f.write(generation)
    except FileExistsError:
        raise SystemExit("已有一轮刷新记录。需要重新启动时，请先运行“停止全部刷新”。") from None
    run = STATE / generation
    run.mkdir()
    stop = run / "stop"
    env = dict(os.environ, IPC_PRESSURE_STOP_FILE=str(stop), PYTHONUTF8="1")
    try:
        processes = []
        for name in SCRIPTS:
            with (run / (name+".log")).open("wb") as log:
                proc = subprocess.Popen([sys.executable,"-B","-u",str(Path(__file__).with_name(name+".py"))],
                            stdout=log,stderr=subprocess.STDOUT,env=env,creationflags=subprocess.CREATE_NO_WINDOW)
            print(f"{name} PID={proc.pid}",flush=True)
            processes.append((name, proc))
        for _ in range(100):
            for name, proc in processes:
                if proc.poll() is not None:
                    raise RuntimeError(f"{name} 已退出，请查看 {run / (name+'.log')}")
            if all("已完成 1 轮" in (run/(name+".log")).read_text("utf-8", errors="replace")
                   or "共 1 轮" in (run/(name+".log")).read_text("utf-8", errors="replace")
                   for name, _ in processes):
                break
            time.sleep(.15)
        else:
            raise RuntimeError(f"部分刷新器在 15 秒内未完成首轮，请检查 {run}")
        print(run,flush=True)
    except BaseException:
        stop.touch()
        active.unlink(missing_ok=True)
        raise


def stop():
    active=STATE / "active"
    if active.exists():
        run=STATE / uuid.UUID(hex=active.read_text(encoding="ascii")).hex
        (run/"stop").touch()
        active.unlink()
        print("已通知本轮刷新器退出，专用 AB 和 UDP 模拟器也将关闭。")


if __name__=="__main__":
    start()
