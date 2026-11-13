"""通过本机网关 API 增量创建专用压测通道，不替换整个项目。"""
import http.cookiejar
import json
import sys
import time
import tkinter as tk
import uuid
from tkinter import simpledialog
from urllib import error, request
from AB点表 import points

BASE = "http://127.0.0.1:5184/api"
SPECS = [
    ("MitsubishiMc", "三菱D区", 5000, "legacy.mitsubishi-mc", 2000),
    ("ModbusTcp", "ModbusTCP", 5020, "builtin.modbus-tcp", 1000),
    ("ModbusUdp", "ModbusUDP", 5021, "builtin.modbus-udp", 1000),
    ("RockwellCip", "AB-Logix", 44818, "legacy.rockwell-cip", 1000),
]


def ident(*parts):
    return uuid.uuid5(uuid.NAMESPACE_URL, "ipc.local/pressure-v1/" + "/".join(map(str, parts))).hex


def make_device(spec, driver):
    protocol, title, port, _, size = spec
    channel_id = ident(protocol, "channel")
    device_id = ident(protocol, "device")
    opts = {}
    if protocol == "RockwellCip":
        opts.update(cipRouteMode="Slot", cipBoolArrayMode="NativeBool", cipStringFormat="Logix", cipMaxServicesPerPacket=16)
    channel = dict(id=channel_id, name="压测_"+title, enabled=True, protocol=protocol, driverId=driver)
    device = dict(id=device_id, name="压测_"+title+"_PLC", channelId=channel_id, protocol=protocol, enabled=True,
                  defaultScanRateMs=500, tags=[], groups=[], connection=dict(protocol=protocol, host="127.0.0.1",
                  port=port, slot=0, transport="Udp" if protocol=="ModbusUdp" else "Tcp", driverId=driver,
                  driverOptionsJson=json.dumps(opts)))
    ab = list(points()) if protocol=="RockwellCip" else []
    width = size // 5
    for part in range(5):
        group_id = ident(protocol, "group", part) if part else ""
        tags = []
        if part:
            device["groups"].append(dict(id=group_id, deviceId=device_id, name=f"压测组_{part}", scanRateMs=500, enabled=True, tags=tags))
        else:
            device["tags"] = tags

        def add(name, address, kind="UInt16", count=1):
            tags.append(dict(id=ident(protocol, name), name=name, deviceId=device_id, groupId=group_id,
                             protocol=protocol, address=address, dataType=kind, elementCount=count, enabled=True,
                             accessMode="ReadOnly", mqttPublishEnabled=False))
        for i in range(part*width, (part+1)*width):
            if i in (part*width, part*width+width//2):
                k=2*part+int(i!=part*width)
                address=("S"+str(k) if protocol=="MitsubishiMc" else "PT_MISSING_"+str(k) if ab else "HR"+str(1100+k))
                add(f"坏点_{k:02}", address, "Bool" if protocol=="MitsubishiMc" else "Int16")
            if ab:
                addr, _, kind, _, count = ab[i]
                add(f"PT_{i:04}_{kind}", addr, kind, count)
            elif protocol=="MitsubishiMc":
                add(f"D{i:04}", f"D{i}")
            else:
                add(f"HR_{40000+i}", f"HR{i}")
    return channel, device


def all_tags(device):
    return device.get("tags", []) + [t for g in device.get("groups", []) for t in g["tags"]]


def main():
    opener = request.build_opener(request.ProxyHandler({}), request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    def api(path, body=None):
        req = request.Request(BASE+path, data=None if body is None else json.dumps(body).encode(),
                              headers={"Content-Type":"application/json"})
        try:
            with opener.open(req, timeout=150) as response:
                result=json.load(response)
        except error.HTTPError as e:
            detail=json.loads(e.read() or b"{}").get("errorMessage","")
            raise RuntimeError(f"HTTP {e.code} {path} {detail}") from None
        if result.get("success") is False:
            raise RuntimeError(result.get("errorMessage") or "请求失败")
        return result.get("data",result)
    root=tk.Tk()
    root.withdraw()
    root.attributes("-topmost",True)
    username=simpledialog.askstring("网关压测配置", "请输入本机网关账号（需要设备管理权限）", parent=root)
    if not username: raise SystemExit("已取消")
    password=simpledialog.askstring("网关压测配置", f"{username} 的网关密码（仅在本机登录）",show="*",parent=root)
    root.destroy()
    if not password: raise SystemExit("已取消")
    api("/auth/login",dict(username=username,password=password))
    del password
    protocols=api("/config/protocols")
    channels={c["id"]:c for c in api("/config/channels")}
    devices={d["id"]:d for d in api("/config/devices")}
    for spec in SPECS:
        proto=spec[0]
        candidates=[p["driverId"] for p in protocols if p["protocol"].lower()==proto.lower()]
        if not candidates:
            print(f"未安装驱动 {proto}，跳过",flush=True)
            continue
        driver=spec[3] if spec[3] in candidates else candidates[0]
        channel,device=make_device(spec,driver)
        if channel["id"] not in channels:
            if any(c["name"]==channel["name"] for c in channels.values()): raise ValueError("通道名称冲突 "+channel["name"])
            api("/config/channels",channel)
        if device["id"] not in devices:
            if any(d["name"]==device["name"] for d in devices.values()): raise ValueError("设备名称冲突 "+device["name"])
            api("/config/devices",device)
        stored=next(d for d in api("/config/devices") if d["id"]==device["id"])
        expected={(t["id"],t["address"],t["dataType"],t["groupId"] or "") for t in all_tags(device)}
        actual={(t["id"],t["address"],t["dataType"],t["groupId"] or "") for t in all_tags(stored)}
        if expected!=actual: raise ValueError("已存点表与计划不一致 "+proto)
        print(f"已核对 {proto}: {len(actual)} 点，4 个分组",flush=True)
    for delay in (2,10):
        time.sleep(delay)
        rows=api("/config/status")["tags"]
        for spec in SPECS:
            selected=[t for t in rows if t.get("deviceId")==ident(spec[0],"device")]
            from collections import Counter
            states=Counter((t["tagName"].startswith("坏点"),t["quality"]) for t in selected)
            errors=list(dict.fromkeys((t.get("errorMessage") or t.get("lastReadError") or "")[:120] for t in selected if t["quality"]!="Good"))
            print(spec[0],dict(states),errors[:3],flush=True)
    print("导入核对结束",flush=True)


if __name__=="__main__":
    main()
