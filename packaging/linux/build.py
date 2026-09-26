"""Baut das Linux-Release (tar.gz) - läuft auch unter Windows.

    python packaging/linux/build.py 1.0.0

Ergebnis: <Repo>/../build-linux/fulltxt-linux-x64-<version>.tar.gz
Ein unter Windows erzeugtes Archiv hat sonst keine Ausführungsrechte; sie werden hier gezielt gesetzt.
"""
import io
import subprocess
import sys
import tarfile
from pathlib import Path

version = sys.argv[1] if len(sys.argv) > 1 else "1.0.0"
repo = Path(__file__).resolve().parents[2]
out_dir = repo.parent / "build-linux"
publish = out_dir / "fulltxt"
name = f"fulltxt-linux-x64-{version}"

subprocess.run(
    ["dotnet", "publish", str(repo / "src" / "Fulltxt.Linux"), "-c", "Release", "-r", "linux-x64",
     "--self-contained", "true", "-p:DebugType=none", "-o", str(publish)],
    check=True,
)

EXECUTABLE = {"fulltxt", "createdump"}
archive = out_dir / f"{name}.tar.gz"


def add_bytes(tar: tarfile.TarFile, arcname: str, data: bytes, mode: int) -> None:
    info = tarfile.TarInfo(arcname)
    info.size = len(data)
    info.mode = mode
    tar.addfile(info, io.BytesIO(data))


with tarfile.open(archive, "w:gz", compresslevel=9) as tar:
    for path in sorted(publish.rglob("*")):
        if path.is_file():
            rel = path.relative_to(publish).as_posix()
            info = tar.gettarinfo(str(path), f"{name}/app/{rel}")
            info.mode = 0o755 if path.name in EXECUTABLE else 0o644
            info.uname = info.gname = ""
            info.uid = info.gid = 0
            with path.open("rb") as handle:
                tar.addfile(info, handle)

    add_bytes(tar, f"{name}/app/icon.png",
              (repo / "src" / "Fulltxt.Linux" / "Assets" / "icon.png").read_bytes(), 0o644)
    # Zeilenenden erzwingen: ein CRLF-Skript würde unter Linux nicht starten.
    script = (repo / "packaging" / "linux" / "install.sh").read_bytes().replace(b"\r\n", b"\n")
    add_bytes(tar, f"{name}/install.sh", script, 0o755)
    add_bytes(tar, f"{name}/INSTALL.md",
              (repo / "INSTALL.md").read_bytes().replace(b"\r\n", b"\n"), 0o644)

print(f"{archive} ({archive.stat().st_size / 1024 / 1024:.1f} MB)")
