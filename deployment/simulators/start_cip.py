import runpy
import sys


if __name__ == "__main__":
    sys.argv = [
        "cpppo.server.enip",
        "--address",
        "0.0.0.0:44818",
        "--print",
        "BoolTags=BOOL[4]",
        "SIntTags=SINT[4]",
        "IntTags=INT[4]",
        "DIntTags=DINT[4]",
        "RealTags=REAL[4]",
        "StringTags=STRING[4]",
    ]
    runpy.run_module("cpppo.server.enip", run_name="__main__")
