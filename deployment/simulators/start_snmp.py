import os
import runpy
import sys


if __name__ == "__main__":
    base = os.path.dirname(os.path.abspath(__file__))
    sys.argv = [
        "snmpsim-command-responder",
        "--data-dir=" + os.path.join(base, "snmp"),
        "--agent-udpv4-endpoint=0.0.0.0:1161",
    ]
    runpy.run_module("snmpsim.commands.responder", run_name="__main__")
