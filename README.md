# FullTXT Desktop (Windows und Linux)

Verschlüsselte Volltextsuche für lokale Ordner und Cloud-Konten. Das Gegenstück zur Android-App
[FullTXT](https://fulltxt.me) für Windows und Linux.

- **Alles lokal:** Es gibt keine Server von uns, keine Telemetrie, keine Daten nach außen.
- **Verschlüsselter Index:** SQLite mit SQLCipher (AES-256). Der Schlüssel ist unter Windows per DPAPI an das
  Benutzerkonto gebunden, unter Linux liegt er im Schlüsselbund (Secret Service).
- **Cloud ohne Download-Ballast:** Cloud-Dateien werden nur einmal zum Indexieren gelesen und nicht gespeichert.
  Später wird nur die gerade benötigte Datei geladen oder per Link im Browser geöffnet. Gut für kleine Festplatten.
- **Anbieter:** Nextcloud, ownCloud, MagentaCloud, Strato HiDrive, Yandex Disk (WebDAV), OneDrive, Dropbox.
- **Dateitypen:** Text und Code, Word (docx), Excel (xlsx), PowerPoint (pptx), PDF mit Textebene.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `src/Fulltxt.Core` | Index, Suche, Extraktoren, Cloud-Anbieter, OAuth, Verschlüsselung (plattformunabhängig, .NET 8) |
| `src/Fulltxt.App` | Windows-Oberfläche (WPF) |
| `src/Fulltxt.Linux` | Linux-Oberfläche (Avalonia, läuft technisch auch unter Windows) |
| `tests/Fulltxt.Core.Tests` | xUnit-Tests für den Kern |
| `packaging/linux` | Build-Skript für das Linux-Release und `install.sh` |

Beide Oberflächen nutzen denselben Kern, deshalb liegen sie in einem Repository.

## Installation

Siehe [INSTALL.md](INSTALL.md) für Windows (ZIP) und Linux (tar.gz), Voraussetzungen und Fehlerbehebung.

## Bauen

Voraussetzung ist das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet test                                   # Tests des Kerns
dotnet run --project src/Fulltxt.App          # Windows-App (nur unter Windows)
dotnet run --project src/Fulltxt.Linux        # Linux-App
python3 packaging/linux/build.py 1.0.0        # Linux-Release als tar.gz
```

Das Windows-Release entsteht mit
`dotnet publish src/Fulltxt.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`.

## Hinweise zur Cloud-Anmeldung

- OneDrive und Dropbox melden sich per OAuth (PKCE) im Standardbrowser an. Die Rückleitung erfolgt an `http://localhost`
  bzw. `http://localhost:53682/`; diese Adressen müssen in der jeweiligen Entwicklerkonsole als Umleitungs-URI eingetragen sein.
- Bei den WebDAV-Anbietern wird ein App-Passwort empfohlen. Unverschlüsseltes `http://` akzeptiert die App nur im lokalen Netz.

## Stand

Die Windows-App ist Ende zu Ende gegen simulierte Server getestet. Die Linux-App wurde bisher nur unter Windows
ausgeführt; der Linux-Schlüsselbund ist nur mit einer Testimplementierung geprüft. OneDrive und Dropbox sind noch
nicht mit echten Konten getestet. Nicht enthalten: OCR für gescannte PDFs, Hintergrund-Synchronisierung, Code-Signierung.
