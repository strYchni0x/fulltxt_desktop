#!/usr/bin/env bash
# Installiert FullTXT für den aktuellen Benutzer (kein root nötig) oder entfernt es wieder.
#   ./install.sh              installieren
#   ./install.sh --uninstall  Programm entfernen (der Index in ~/.local/share/Fulltxt bleibt erhalten)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
prefix="$HOME/.local/opt/fulltxt"
launcher="$HOME/.local/bin/fulltxt"
desktop_file="$data_home/applications/fulltxt.desktop"

if [[ "${1:-}" == "--uninstall" ]]; then
    rm -rf "$prefix"
    rm -f "$launcher" "$desktop_file"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data_home/applications" || true
    echo "FullTXT wurde entfernt. Der verschlüsselte Index liegt weiter in $data_home/Fulltxt (bei Bedarf selbst löschen)."
    exit 0
fi

if [[ ! -x "$here/app/fulltxt" && ! -f "$here/app/fulltxt" ]]; then
    echo "Fehler: Ordner 'app' neben install.sh nicht gefunden." >&2
    exit 1
fi

rm -rf "$prefix"
mkdir -p "$prefix" "$(dirname "$launcher")" "$(dirname "$desktop_file")"
cp -r "$here/app/." "$prefix/"
chmod +x "$prefix/fulltxt"
ln -sf "$prefix/fulltxt" "$launcher"

cat > "$desktop_file" <<EOF
[Desktop Entry]
Type=Application
Name=FullTXT
Comment=Verschlüsselte Volltextsuche für lokale Ordner und Cloud-Konten
Exec=$prefix/fulltxt
Icon=$prefix/icon.png
Terminal=false
Categories=Utility;Office;
StartupWMClass=fulltxt
EOF

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data_home/applications" || true

echo "FullTXT ist installiert: $prefix"
echo "Start über das Anwendungsmenü oder mit: fulltxt   (falls nötig ~/.local/bin in den PATH aufnehmen)"

# FullTXT legt den Index-Schlüssel im Schlüsselbund ab und öffnet Dateien mit xdg-open.
missing=()
command -v secret-tool >/dev/null 2>&1 || missing+=("libsecret-tools")
command -v xdg-open >/dev/null 2>&1 || missing+=("xdg-utils")
if (( ${#missing[@]} )); then
    echo
    echo "Hinweis: Es fehlen Pakete: ${missing[*]}"
    echo "  Debian/Ubuntu/Mint: sudo apt install ${missing[*]} gnome-keyring"
    echo "  Fedora:             sudo dnf install libsecret xdg-utils gnome-keyring"
    echo "  Arch:               sudo pacman -S libsecret xdg-utils gnome-keyring"
fi
