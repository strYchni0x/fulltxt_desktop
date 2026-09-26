# FullTXT für Windows und Linux – Installationsanleitung

FullTXT durchsucht lokale Ordner und Cloud-Konten per Volltextsuche. Alles bleibt auf deinem PC:
der Index ist verschlüsselt (SQLCipher). Der Schlüssel ist unter Windows per DPAPI an das Benutzerkonto gebunden, unter Linux liegt er im Schlüsselbund des Benutzers. Es werden keine Daten an Dritte gesendet.

## Windows

### Systemvoraussetzungen

- Windows 10 (1809) oder Windows 11, 64 Bit
- Die ZIP-Version braucht keine Runtime. Nur die kleine, selbst gebaute Variante benötigt die [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)
- Für die Anmeldung bei OneDrive/Dropbox: ein Standardbrowser und freier lokaler Port `53682`

### Variante A: Fertige ZIP-Datei (empfohlen)

1. `FullTXT-Windows-1.1.0-win-x64.zip` herunterladen und in einen beliebigen Ordner entpacken,
   z. B. `C:\Users\<Name>\AppData\Local\Programs\FullTXT`.
2. `Fulltxt.App.exe` starten. Es ist keine Installation und keine .NET-Runtime nötig.
3. Die App ist nicht signiert. Beim ersten Start zeigt Windows SmartScreen eine Warnung:
   **„Weitere Informationen" → „Trotzdem ausführen"**.
   Zur Kontrolle kann die Prüfsumme der ZIP-Datei verglichen werden:
   ```powershell
   Get-FileHash .\FullTXT-Windows-1.1.0-win-x64.zip -Algorithm SHA256
   ```
4. Optional: Rechtsklick auf `Fulltxt.App.exe` → „Verknüpfung erstellen" und die Verknüpfung in den Startmenü- oder Autostart-Ordner legen.

Nur für 64-Bit-Windows (x64), nicht für ARM.

### Variante B: Aus dem Quellcode bauen

Zusätzlich wird das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) benötigt
(`winget install Microsoft.DotNet.SDK.8`).

```powershell
git clone https://github.com/strYchni0x/fulltxt_win.git
cd fulltxt_win
dotnet test
```

#### Kleine Installation (benötigt installierte .NET 8 Runtime)

```powershell
dotnet publish src/Fulltxt.App -c Release -r win-x64 --self-contained false -o publish
```

#### Eigenständig (ohne .NET-Installation, ca. 150 MB)

```powershell
dotnet publish src/Fulltxt.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Der Ordner `publish` enthält danach `Fulltxt.App.exe`.

### Selbst gebaute Version installieren

1. Ordner `publish` nach z. B. `C:\Users\<Name>\AppData\Local\Programs\FullTXT` kopieren.
2. Optional Startmenü-Verknüpfung anlegen:
   ```powershell
   $exe = "$env:LOCALAPPDATA\Programs\FullTXT\Fulltxt.App.exe"
   $lnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\FullTXT.lnk"
   $s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
   $s.TargetPath = $exe; $s.WorkingDirectory = Split-Path $exe; $s.Save()
   ```
3. `Fulltxt.App.exe` starten.

Deinstallieren: Programmordner und Verknüpfung löschen. Wer auch die Daten entfernen will, löscht
zusätzlich `%LOCALAPPDATA%\Fulltxt` (Index, Schlüssel, Einstellungen).

## Linux

Getestet wurde die Oberfläche unter Windows. Die Linux-Version selbst konnte bisher nicht auf einem Linux-System ausgeführt werden.
Rückmeldungen zu Distributionen und Desktop-Umgebungen sind willkommen.

### Systemvoraussetzungen

- Linux x86_64 mit grafischer Oberfläche (X11 oder Wayland/XWayland), z. B. Ubuntu 22.04+, Debian 12+, Fedora 39+, Linux Mint 21+
- Ein **Schlüsselbund mit Secret-Service** (GNOME Keyring, KDE Wallet oder KeePassXC) und das Programm `secret-tool`.
  Er speichert den Schlüssel des Index; FullTXT fragt kein eigenes Passwort ab.
- `xdg-utils` (für „Öffnen", „Im Ordner zeigen" und „Im Browser öffnen")
- `libfontconfig1` und die üblichen X11-Bibliotheken (auf Desktop-Installationen vorhanden)
- Eine .NET-Installation ist nicht nötig

```bash
# Debian / Ubuntu / Mint
sudo apt install libsecret-tools xdg-utils gnome-keyring libfontconfig1
# Fedora
sudo dnf install libsecret xdg-utils gnome-keyring fontconfig
# Arch
sudo pacman -S libsecret xdg-utils gnome-keyring fontconfig
```

### Installieren

```bash
tar xzf fulltxt-linux-x64-1.1.0.tar.gz
cd fulltxt-linux-x64-1.1.0
./install.sh
```

Das Skript kopiert das Programm nach `~/.local/opt/fulltxt`, legt den Startbefehl `~/.local/bin/fulltxt` und einen
Eintrag im Anwendungsmenü an. Es braucht kein `sudo`. Alternativ lässt sich `app/fulltxt` direkt aus dem entpackten Ordner starten.

Entfernen: `./install.sh --uninstall` (der Index in `~/.local/share/Fulltxt` bleibt erhalten und kann von Hand gelöscht werden).

### Aus dem Quellcode bauen (Linux oder Windows)

```bash
python3 packaging/linux/build.py 1.1.0
```

Erzeugt `../build-linux/fulltxt-linux-x64-1.1.0.tar.gz` (benötigt das .NET 8 SDK).
Zum direkten Ausprobieren: `dotnet run --project src/Fulltxt.Linux`.

### Besonderheiten unter Linux

- Der Schlüsselbund wird beim ersten Start ggf. entsperrt (Passwortabfrage deines Desktops). Ohne laufenden Schlüsselbund zeigt FullTXT eine Fehlermeldung mit dem passenden Installationsbefehl.
- „Im Ordner zeigen" öffnet den Ordner im Dateimanager; die Datei selbst wird nicht markiert.
- Statt der Windows-Dateisymbole zeigen die Treffer die Dateiendung (PDF, DOCX, TXT …).
- Die Anmeldung bei OneDrive/Dropbox öffnet den Standardbrowser (`xdg-open`); Port `53682` muss frei sein.

## Erste Schritte

1. **Zahnrad „Einstellungen"** unten links öffnen.
2. **+ Ordner hinzufügen**: lokalen Ordner wählen, die Indexierung startet automatisch.
3. **+ Cloud-Konto hinzufügen**: Anbieter wählen.
   - **Nextcloud / ownCloud / MagentaCloud / Strato HiDrive / Yandex**: Server, Benutzername und **App-Passwort** eintragen
     (in Nextcloud: Einstellungen → Sicherheit → „Neues App-Passwort"). Optional einen Startordner angeben.
     Unverschlüsseltes `http://` wird nur für lokale Adressen akzeptiert.
   - **OneDrive / Dropbox**: „Im Browser anmelden" – der Browser öffnet sich, nach der Freigabe kehrt FullTXT automatisch zurück.
     (Voraussetzung: Umleitungs-URIs in den Entwicklerkonsolen, siehe unten.)
4. Suchbegriff oben eingeben. Treffer zeigen Textauszug und Dateisymbol.
   - Lokal: **Öffnen** / **Im Ordner zeigen**
   - Cloud: **Im Browser öffnen** (Web-Link) oder **Herunterladen** (nur diese eine Datei, in den Ordner „Downloads")
5. Unter Einstellungen lassen sich das Design (System/Hell/Dunkel) und die maximale Trefferzahl (50 bis 1000, Standard 100) wählen.
6. Eine laufende Indexierung bricht das **X** in der Quellenkachel ab. Bereits gelesene Dateien bleiben erhalten, der nächste Lauf macht dort weiter.

**Was bedeutet „nicht durchsuchbar"?** Die Kachel zeigt pro Quelle, wie viele Dateien durchsuchbar sind. Nicht durchsuchbar sind Dateien, aus denen kein Text ausgelesen werden konnte: Dateitypen ohne Textextraktion (z. B. Bilder, Archive), Dateien ohne lesbaren Text (z. B. gescannte PDFs ohne Textebene, leere oder passwortgeschützte Dateien) und Dateien über dem Größenlimit von 50 MB. Sie erscheinen nicht in den Suchergebnissen, auch nicht über den Dateinamen.

Cloud-Dateien werden nur einmal zum Indexieren gelesen und nicht gespeichert – das spart Plattenplatz.
Spätere Aktualisierungen laden nur geänderte Dateien.

## Unterstützte Dateitypen

Text/Code (txt, md, csv, json, xml, html, Quellcode u. a.), Word (docx), Excel (xlsx), PowerPoint (pptx), PDF mit Textebene.
Gescannte PDFs ohne Textebene werden noch nicht erkannt (kein OCR).

## Wo liegen die Daten?

| Datei | Inhalt |
|---|---|
| `%LOCALAPPDATA%\Fulltxt\index.db` (Linux: `~/.local/share/Fulltxt/index.db`) | verschlüsselter Volltextindex |
| `%LOCALAPPDATA%\Fulltxt\index.key` | Index-Schlüssel, per Windows-DPAPI an dein Benutzerkonto gebunden |
| `%LOCALAPPDATA%\Fulltxt\settings.json` | Einstellungen (Darstellung, maximale Trefferzahl) |

Mit der Umgebungsvariable `FULLTXT_DATA_DIR` lässt sich der Datenordner verlegen (z. B. für einen zweiten, getrennten Index).

Wichtig: Der Index ist nur im selben Benutzerkonto lesbar (Linux: nur mit demselben Schlüsselbund). Auf einem anderen PC oder nach einer
Neuinstallation von Windows muss neu indexiert werden. Das ist beabsichtigt.

## Problemlösung

| Problem | Lösung |
|---|---|
| Windows: Start bricht ab, „.NET 8 Desktop Runtime" fehlt | Betrifft nur die kleine selbst gebaute Variante: Runtime installieren oder die ZIP-Version nutzen |
| Windows: SmartScreen-Warnung beim ersten Start | Die App ist nicht signiert: „Weitere Informationen" → „Trotzdem ausführen" |
| OneDrive/Dropbox: „redirect_uri" Fehler | Umleitungs-URI in der Konsole fehlt oder ist falsch (siehe Anleitung Azure/Dropbox) |
| Anmeldung wartet endlos | Port 53682 belegt oder vom Browser blockiert; andere Programme schließen, erneut versuchen |
| Nextcloud: Anmeldung schlägt fehl | App-Passwort statt Kontopasswort verwenden; bei 2FA ist das Pflicht |
| Konto zeigt „Neu anmelden" | Konto in den Einstellungen entfernen und neu hinzufügen |
| Nach Fehlermeldung neu indexieren | Einstellungen → Quelle → Neu indexieren |
| Linux: „Der Schlüsselbund des Systems ist nicht erreichbar" | `libsecret-tools` und `gnome-keyring` (bzw. KDE Wallet/KeePassXC mit aktivierter Secret-Service-Schnittstelle) installieren, neu anmelden |
| Linux: Fenster startet nicht | Prüfen, ob `libfontconfig1` und eine X11/Wayland-Sitzung vorhanden sind; Start im Terminal zeigt die Fehlermeldung |
