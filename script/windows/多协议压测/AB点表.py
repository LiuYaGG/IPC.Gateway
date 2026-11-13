"""ab_server 2.7.2 实际支持的类型。按符号和下标为每个逻辑点分配独立存储。"""
TYPES = [
    ("BOOL", "Bool", "B"), ("SINT", "Int8", "b"), ("INT", "Int16", "h"),
    ("DINT", "Int32", "i"), ("LINT", "Int64", "q"), ("REAL", "Float", "f"),
    ("LREAL", "Double", "d"), ("STRING", "String", "88s")
]


def points():
    offsets = {kind: 0 for kind, _, _ in TYPES}
    for i in range(1000):
        kind, gateway_type, fmt = TYPES[i % len(TYPES)]
        count = 4 if (i // len(TYPES)) % 4 == 1 and kind != "STRING" else 1
        start = offsets[kind]
        offsets[kind] += count
        yield f"PT{kind}[{start}]", kind, gateway_type + ("Array" if count > 1 else ""), fmt, count
