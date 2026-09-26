# FullTXT für Windows – Installationsanleitung

FullTXT durchsucht lokale Ordner und Cloud-Konten per Volltextsuche. Alles bleibt auf deinem PC:
der Index ist verschlüsselt (SQLCipher, Schlüssel per Windows-Konto/DPAPI geschützt), es werden keine Daten an Dritte gesendet.

## Systemvoraussetzungen

- Windows 10 (1809) oder Windows 11, 64 Bit
- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) – nur nötig, wenn du die Variante „framework-abhängig" nutzt
- Für die Anmeldung bei OneDrive/Dropbox: ein Standardbrowser und freier lokaler Port `53682`

## Variante A: Aus dem Quellcode bauen

Zusätzlich wird das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) benötigt
(`winget install Microsoft.DotNet.SDK.8`).

```powershell
git clone https://github.com/strYchni0x/fulltxt_win.git
cd fulltxt_win
dotnet test
```

### Kleine Installation (benötigt installierte .NET 8 Runtime)

```powershell
dotnet publish src/Fulltxt.App -c Release -r win-x64 --self-contained false -o publish
```

### Eigenständig (ohne .NET-Installation, ca. 150 MB)

```powershell
dotnet publish src/Fulltxt.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Der Ordner `publish` enthält danach `Fulltxt.App.exe`.

## Installieren

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
5. Design (System/Hell/Dunkel) unter Einstellungen wählen.

Cloud-Dateien werden nur einmal zum Indexieren gelesen und nicht gespeichert – das spart Plattenplatz.
Spätere Aktualisierungen laden nur geänderte Dateien.

## Unterstützte Dateitypen

Text/Code (txt, md, csv, json, xml, html, Quellcode u. a.), Word (docx), Excel (xlsx), PowerPoint (pptx), PDF mit Textebene.
Gescannte PDFs ohne Textebene werden noch nicht erkannt (kein OCR).

## Wo liegen die Daten?

| Datei | Inhalt |
|---|---|
| `%LOCALAPPDATA%\Fulltxt\index.db` | verschlüsselter Volltextindex |
| `%LOCALAPPDATA%\Fulltxt\index.key` | Index-Schlüssel, per Windows-DPAPI an dein Benutzerkonto gebunden |
| `%LOCALAPPDATA%\Fulltxt\settings.json` | Einstellungen (Theme) |

Wichtig: Der Index ist nur unter demselben Windows-Benutzerkonto lesbar. Auf einem anderen PC oder nach einer
Neuinstallation von Windows muss neu indexiert werden. Das ist beabsichtigt.

## Problemlösung

| Problem | Lösung |
|---|---|
| Start bricht ab, „.NET 8 Desktop Runtime" fehlt | Runtime installieren oder Variante „Eigenständig" bauen |
| SmartScreen-Warnung beim ersten Start | Die App ist nicht signiert: „Weitere Informationen" → „Trotzdem ausführen" |
| OneDrive/Dropbox: „redirect_uri" Fehler | Umleitungs-URI in der Konsole fehlt oder ist falsch (siehe Anleitung Azure/Dropbox) |
| Anmeldung wartet endlos | Port 53682 belegt oder vom Browser blockiert; andere Programme schließen, erneut versuchen |
| Nextcloud: Anmeldung schlägt fehl | App-Passwort statt Kontopasswort verwenden; bei 2FA ist das Pflicht |
| Konto zeigt „Neu anmelden" | Konto in den Einstellungen entfernen und neu hinzufügen |
| Nach Fehlermeldung neu indexieren | Einstellungen → Quelle → Neu indexieren |
