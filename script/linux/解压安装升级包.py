#!/usr/bin/env python3
"""Extract an IPC Gateway ZIP, including legacy Windows backslash entry paths.

Original ZIP contents and file bytes are preserved; use the original ZIP for
package verification and driver trust inheritance during installation.
"""
import argparse
from pathlib import Path, PurePosixPath
import shutil
import stat
import tempfile
import zipfile


def extract_package(source, destination=None):
    source = Path(source).expanduser().resolve(strict=True)
    if destination is None:
        target_root = Path(tempfile.mkdtemp(prefix="ipc-gateway-fixed-", dir="/var/tmp")).resolve()
    else:
        target_root = Path(destination).expanduser().resolve()
        if target_root.exists() and (not target_root.is_dir() or any(target_root.iterdir())):
            raise ValueError("目标目录必须是新的空目录。")
        target_root.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(source) as archive:
        destinations = set()
        for entry in archive.infolist():
            name = entry.filename.replace("\\", "/")
            relative = PurePosixPath(name)
            if relative.is_absolute() or ".." in relative.parts or not relative.parts or any(":" in part for part in relative.parts):
                raise ValueError("安装包包含不安全的路径：" + name)
            mode = entry.external_attr >> 16
            if stat.S_ISLNK(mode):
                raise ValueError("安装包不能包含符号链接：" + name)
            target = target_root.joinpath(*relative.parts)
            if target in destinations:
                raise ValueError("安装包包含重复的目标路径：" + name)
            destinations.add(target)
            if entry.is_dir() or name.endswith("/"):
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as src, target.open("xb") as dst:
                    shutil.copyfileobj(src, dst)
    if not (target_root / "tools" / "安装热备服务.sh").is_file():
        raise ValueError("解压后的安装包缺少 tools/安装热备服务.sh，请核对是否为完整安装包。")
    return target_root


def main():
    parser = argparse.ArgumentParser(description="修复旧 ZIP 路径分隔符并解压到新的空目录，不修改原 ZIP。")
    parser.add_argument("zip", help="原始安装或升级 ZIP 路径")
    parser.add_argument("directory", nargs="?", help="新的空目录，默认 /var/tmp/ipc-gateway-fixed-* ")
    args = parser.parse_args()
    try:
        result = extract_package(args.zip, args.directory)
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        parser.exit(1, "解压失败：" + str(error) + "\n")
    print("解压完成：" + str(result))
    print("安装入口：" + str(result / "tools" / "安装热备服务.sh"))


if __name__ == "__main__":
    main()
