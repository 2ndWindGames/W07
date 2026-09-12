"""Verify JNI entry points in the final APK/AAB DEX, after R8 shrinking.

Usage: python Tools/Verify-AndroidBridge.py artifact.aab --report report.json
Checks actual defined public static methods, not strings or references alone.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zipfile

CLASS = "Lcom/secondwindgames/runbeat/RunBeatService;"
EXPECTED = {
    "initialize": "(Landroid/content/Context;)V",
    "snapshot": "(Landroid/content/Context;)Ljava/lang/String;",
    "command": "(Landroid/content/Context;Ljava/lang/String;Ljava/lang/String;)V",
    "openMusic": "(Landroid/app/Activity;Ljava/lang/String;)Z",
    "notificationSettings": "(Landroid/app/Activity;)V",
}


def defined_bridge_methods(data):
    if not data.startswith(b"dex\n") or data[7] != 0:
        raise ValueError("Not a standard DEX file")

    def u32(offset):
        return struct.unpack_from("<I", data, offset)[0]

    def u16(offset):
        return struct.unpack_from("<H", data, offset)[0]

    def uleb(offset):
        value = 0
        for shift in range(0, 35, 7):
            byte = data[offset]
            offset += 1
            value |= (byte & 127) << shift
            if byte < 128:
                return value, offset
        raise ValueError("Invalid ULEB128")

    strings = []
    for index in range(u32(56)):
        offset = u32(u32(60) + 4 * index)
        _, offset = uleb(offset)
        end = data.index(b"\0", offset)
        # All descriptors and method names relevant to JNI here are ASCII.
        strings.append(data[offset:end].decode("utf-8", errors="replace"))
    types = [strings[u32(u32(68) + 4 * i)] for i in range(u32(64))]
    methods = []
    for index in range(u32(88)):
        offset = u32(92) + 8 * index
        proto = u32(76) + 12 * u16(offset + 2)
        parameters = u32(proto + 8)
        args = "" if not parameters else "".join(
            types[u16(parameters + 4 + 2 * p)] for p in range(u32(parameters))
        )
        methods.append((types[u16(offset)], strings[u32(offset + 4)],
                        "(" + args + ")" + types[u32(proto + 4)]))
    found = []
    for index in range(u32(96)):
        offset = u32(100) + 32 * index
        if types[u32(offset)] != CLASS:
            continue
        cursor = u32(offset + 24)
        if not cursor:
            continue
        counts = []
        for _ in range(4):
            value, cursor = uleb(cursor)
            counts.append(value)
        for _ in range(counts[0] + counts[1]):
            _, cursor = uleb(cursor)
            _, cursor = uleb(cursor)
        for count in counts[2:]:
            method_index = 0
            for _ in range(count):
                delta, cursor = uleb(cursor)
                access, cursor = uleb(cursor)
                code_offset, cursor = uleb(cursor)
                method_index += delta
                owner, name, descriptor = methods[method_index]
                if owner == CLASS:
                    found.append({"name": name, "descriptor": descriptor,
                                  "access": access, "codeOffset": code_offset})
    return found


def verify(path):
    with zipfile.ZipFile(path) as archive:
        methods = []
        for entry in archive.namelist():
            if entry.endswith(".dex"):
                methods.extend(defined_bridge_methods(archive.read(entry)))
    checks = []
    for name, descriptor in EXPECTED.items():
        matches = [m for m in methods if m["name"] == name and m["descriptor"] == descriptor]
        passed = len(matches) == 1 and matches[0]["access"] & 9 == 9 and matches[0]["codeOffset"] > 0
        checks.append({"method": name, "descriptor": descriptor, "passed": passed})
    return {"artifact": str(path.resolve()), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "passed": all(c["passed"] for c in checks), "checks": checks}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifact", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    result = verify(args.artifact)
    output = json.dumps(result, indent=2)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(output + "\n", encoding="utf-8")
    print(output)
    raise SystemExit(0 if result["passed"] else 1)
