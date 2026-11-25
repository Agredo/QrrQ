# QrrQ – QR-Codes mit Spiegel-Geheimnis

[![NuGet QrrQ](https://img.shields.io/nuget/v/QrrQ.svg?label=NuGet%20QrrQ)](https://www.nuget.org/packages/QrrQ)
[![NuGet QrrQ.Cli](https://img.shields.io/nuget/v/QrrQ.Cli.svg?label=NuGet%20QrrQ.Cli)](https://www.nuget.org/packages/QrrQ.Cli)
[![CI](https://github.com/Agredo/QrrQ/actions/workflows/ci.yml/badge.svg)](https://github.com/Agredo/QrrQ/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Deutsch** · [English](README.md)

**QrrQ** erzeugt QR-Codes mit zwei Inhalten: Normal gescannt liefern sie Text A, im Spiegel (oder von der Rückseite einer Folie) liefern sie Text B. Die Daten sind in beiden Leserichtungen fehlerfreie Reed-Solomon-Codewörter (bei `MaxCorrectedCodewords = 0`), und gängige Scanner lesen beide Richtungen regulär. Streng normkonform nach ISO/IEC 18004 sind die Codes trotzdem nicht: Die Formatinformation weicht absichtlich ab (meist 1 Bit pro Richtung), und die Füllbytes folgen nicht dem Standardmuster `0xEC/0x11`. Es braucht keine spezielle App, jeder normale Scanner reicht.

- .NET 11 / C# 15, keine Laufzeitabhängigkeiten (eigener QR-Encoder, Reed-Solomon, PNG- und SVG-Ausgabe)
- API: zwei Strings rein, ein Code mit `byte[]`-PNG, SVG oder Modulraster raus
- Eingebauter Decoder prüft jede Lösung in beiden Richtungen
- Durchgängig getestet (37 Unit-Tests, Selbsttest) und mit **OpenCV** und **zxing-cpp** gegengeprüft
- NuGet-Pakete: [`QrrQ`](https://www.nuget.org/packages/QrrQ) (Bibliothek) und [`QrrQ.Cli`](https://www.nuget.org/packages/QrrQ.Cli) (dotnet-Tool)
- Native AOT: vorkompilierte, getrimmte Einzeldateien für win-x64, win-arm64, linux-x64 und osx-arm64 (ohne .NET-Installation, 0,5–0,7 MB unter Windows/Linux, 1,7 MB unter macOS)

## Im Browser ausprobieren

Der [QrrQ-Playground](https://agredo.github.io/QrrQ/) führt die Bibliothek als WebAssembly aus: zwei Texte eingeben, Code erzeugen, PNG/SVG herunterladen und beide Richtungen mit dem eingebauten Decoder zurücklesen. Ohne Installation, und nichts verlässt den Browser. Der Quellcode liegt in `samples/QrrQ.Playground`, ausgeliefert von `.github/workflows/pages.yml`.

## Installation

```bash
# Bibliothek
dotnet add package QrrQ

# Kommandozeilen-Tool (Befehl: qrrq)
dotnet tool install -g QrrQ.Cli
```

## Fertige Binaries (Native AOT)

Für Rechner ohne .NET-Laufzeit gibt es vollständig vorkompilierte Einzeldateien. Sie werden als **Native AOT** mit `TrimMode=full`, invarianter Globalisierung und ohne Reflection-Metadaten gebaut – das Ergebnis ist eine einzelne ausführbare Datei ohne Laufzeitabhängigkeiten.

| Plattform | RID | Datei | Größe (entpackt) |
|---|---|---|---|
| Windows x64 | `win-x64` | `qrrq-<version>-win-x64.zip` | 0,6 MB |
| Windows ARM64 | `win-arm64` | `qrrq-<version>-win-arm64.zip` | 0,5 MB |
| Linux x64 | `linux-x64` | `qrrq-<version>-linux-x64.tar.gz` | 0,7 MB |
| macOS Apple Silicon | `osx-arm64` | `qrrq-<version>-osx-arm64.tar.gz` | 1,7 MB |

Die Windows- und Linux-Binärdateien werden zusätzlich mit [UPX](https://upx.github.io/) gepackt (`--best --lzma`), was sie um etwa zwei Drittel verkleinert. Die macOS-Binärdatei bleibt ungepackt: macOS führt nur Code mit gültiger Signatur aus, und mit UPX gepackte Mach-O-arm64-Dateien laufen auch nach erneutem Signieren nicht mehr, deshalb lässt der Workflow sie unkomprimiert. Der Selbsttest läuft immer auf der endgültigen Datei.

Die Archive selbst sind rund 0,6 MB (Windows), 0,7 MB (Linux) und 0,8 MB (macOS) groß.

Bezug: die Assets des jeweils neuesten [GitHub-Release](https://github.com/Agredo/QrrQ/releases). Entpacken, `qrrq` (bzw. `qrrq.exe`) aufrufen, fertig:

```bash
qrrq "Falsche Fährte" "Code: 4711" -o raetsel --svg
```

Die Binaries enthalten `LICENSE` und `NOTICE`. Gebaut werden sie von `.github/workflows/native.yml`: vier Runner, je ein `dotnet publish -p:PublishAot=true`, danach ein Selbsttest der erzeugten Datei, bevor das Archiv entsteht. Bei einem Tag `v*` hängt der Workflow die Archive automatisch an das Release.

## Schnellstart

```csharp
using QrrQ;

var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");

byte[] png = code.ToPng();                            // Druckmotiv
var (normal, mirrored) = code.ToPngPair();            // Motiv + Spiegelbild (zum Testen am Bildschirm)
string svg = code.ToSvg(moduleSize: 8);
bool[,] modules = code.Matrix.ToArray();              // Rohdaten [Zeile, Spalte]
byte[] packed = code.Matrix.ToPackedRows();           // 1 Bit pro Modul

Console.WriteLine(code);   // QrrQ V6 (41×41), M/M: "Falsche Fährte" ⇄ "Code: 4711"
```

Ohne Exceptions, mit Union-Typ (C# 15):

```csharp
var message = QrrQGenerator.TryCreate(a, b, new QrrQOptions { MaxVersion = 10 }) switch
{
    QrrQCode code => $"Version {code.Version}, fehlerfrei: {code.IsErrorFree}",
    QrrQError error => error.Message,   // PayloadTooLong | IncompatibleEccLevels | NoSolutionFound
};
```

### Kommandozeile

```bash
qrrq "Falsche Fährte" "Code: 4711" -o raetsel --svg
# → raetsel.png, raetsel.mirrored.png, raetsel.svg, raetsel.mirrored.svg + Konsolenvorschau

qrrq --selftest 200        # Zufalls-Rundreisen inkl. Beschädigungstest

# aus dem Quellcode:
dotnet run --project src/QrrQ.Cli -- "Falsche Fährte" "Code: 4711" -o raetsel --svg
```

## Optionen

| Option | Standard | Bedeutung |
|---|---|---|
| `NormalEcc` / `MirroredEcc` | `null` (automatisch M, dann L) | Fehlerkorrektur je Richtung |
| `MinVersion` / `MaxVersion` | 1 / 40 | erlaubter Größenbereich |
| `MaxCorrectedCodewords` | 0 | Codewörter, die pro Richtung schon ab Werk korrigiert werden dürfen (siehe unten) |
| `Seed` | fest | gleiche Eingabe + gleicher Seed = gleicher Code |
| `AttemptsPerCandidate` | 2 | Zufallsbelegungen pro Maskenpaar; die optisch beste gewinnt |

## Mathematischer Hintergrund

### 1. Raster, Spiegel und Transposition

Ein QR-Symbol der Version $v$ ist ein quadratisches Raster

$$M \in \mathbb{F}_2^{n \times n}, \qquad n = 17 + 4v,$$

wobei $M_{ij} = 1$ ein dunkles Modul bedeutet. Die gespiegelte Ansicht ist die horizontale Spiegelung

$$F(M)_{ij} = M_{i,\,n-1-j}.$$

Für die Transposition $T(M)_{ij} = M_{ji}$ gilt: Spiegeln ist Transponieren plus eine starre Drehung um 90°,

$$F(M) = R_{90}\bigl(T(M)\bigr).$$

Scanner gleichen Drehungen von selbst aus. Gesucht ist deshalb ein Raster $M$, bei dem $M$ den Text A und $T(M)$ den Text B ergibt. Das ist die ganze Idee in einer Zeile:

$$R(M) = A, \qquad R\bigl(T(M)\bigr) = B,$$

wobei $R$ den vollständigen Lesepfad eines Decoders bezeichnet (Lage normalisieren, Format lesen, demaskieren, Reed-Solomon korrigieren, Segmente parsen).

### 2. Symmetrische Funktionsmuster

Finder-, Timing- und Ausrichtungsmuster sowie die reservierten Bereiche für Format- und Versionsinformation liegen symmetrisch zur Hauptdiagonalen. Unter $T$ bildet die Menge der Funktionsmodule also auf sich selbst ab; die **Datenmodule werden nur permutiert**. Diese Permutation $\sigma$ ist eine Involution,

$$\sigma^2 = \mathrm{id},$$

denn zweimaliges Transponieren ist die Identität. Sie ist der einzige Ort im Verfahren, an dem die Geometrie des QR-Codes eingeht.

### 3. Ein lineares Gleichungssystem über GF(2)

Sei $x_k$ das $k$-te Datenmodul in Lesereihenfolge (unten rechts beginnend, im Zickzack nach oben). Die Richtung A liest den Strombit $k$ als

$$a_k = x_k \oplus m_A(k),$$

die gespiegelte Richtung B liest dieselbe Zelle an der Position $\sigma(k)$:

$$b_k = x_{\sigma(k)} \oplus m_B(k).$$

Dabei sind $m_A, m_B \in \mathbb{F}_2$ die Werte der acht Maskenformeln (Zeile $i$, Spalte $j$), zum Beispiel

$$m_0(i,j) = (i+j) \bmod 2, \qquad m_4(i,j) = \left(\left\lfloor i/2 \right\rfloor + \left\lfloor j/3 \right\rfloor\right) \bmod 2.$$

Die Unbekannten sind die $x_k$. Gefordert ist:

1. **Die Nachrichtenbits** beider Texte stehen am Anfang ihres Datenstroms. Jedes Nachrichtenbit ist eine feste lineare Gleichung $x_k \oplus m_A(k) = a_k$ bzw. $x_{\sigma(k)} \oplus m_B(k) = b_k$.
2. **Alle Reed-Solomon-Syndrome** beider Richtungen sind null.

Beides zusammen ist ein lineares Gleichungssystem über $\mathbb{F}_2$. Die Freiheitsgrade stecken in den Füllbytes hinter dem Terminator – Bits, die kein Scanner auswertet. Deshalb sind beide Richtungen **vollständig fehlerfreie Reed-Solomon-Codewörter**, und die volle Fehlerkorrektur bleibt erhalten.

### 4. Reed-Solomon als acht Bitgleichungen

Ein Codewort $c(x) = \sum_i c_i x^i$ mit $c_i \in \mathrm{GF}(2^8)$ ist genau dann gültig, wenn alle Syndrome verschwinden:

$$S_j = c(\alpha^j) = \sum_i c_i\,\alpha^{ij} = 0, \qquad j = 0,\dots,e-1,$$

wobei $e$ die Zahl der Prüfcodewörter pro Block ist. QrrQ rechnet in $\mathrm{GF}(2^8)$ mit dem QR-Polynom

$$x^8 + x^4 + x^3 + x^2 + 1 \quad (\texttt{0x11D}), \qquad \alpha = 2.$$

Multiplikation mit der Konstanten $\alpha^{ij}$ ist eine $\mathbb{F}_2$-lineare Abbildung. Jedes Syndrom $S_j \in \mathrm{GF}(2^8)$ zerfällt daher in **acht Bitgleichungen**. Zusammen mit den Nachrichtenbits ergibt das ein Gleichungssystem, dessen rechte Seite für beide Leserichtungen festliegt.

### 5. Kapazitätsbedingung

Sei $D_A, D_B$ die Datencodewörter-Kapazität der beiden Richtungen und $C$ die Gesamtzahl der Codewörter des Symbols. Die Konstruktion braucht Platz für zwei Nutzlasten und zwei Sätze Prüfbits. Notwendig ist

$$D_A + D_B > C.$$

Damit funktionieren L/L, M/L und M/M, ebenso Q/L und H/L; Q/M geht nur knapp. Q/Q und H mit M, Q oder H haben keinen Platz und werden als `IncompatibleEccLevels` gemeldet. Ist die Bedingung erfüllt, heißt das noch nicht, dass das Gleichungssystem lösbar ist – deshalb sucht QrrQ zusätzlich über Versionen und Maskenpaare.

### 6. Die gemeinsame Ecke

Beide Datenströme beginnen unten rechts, und diese Ecke ist ein **Fixpunkt der Transposition**. Module, die *beide* Nachrichten festlegen, müssen daher übereinstimmen. Das regeln die Wahl der Masken und die Wahl der Kodierung: Ein Text kann in einem oder in zwei Segmenten stehen, Scanner setzen das wieder zum selben Text zusammen. Die Zahl solcher Konfliktmodule wächst ungefähr mit

$$\frac{|A| \cdot |B|}{n^2}.$$

Lange Texte brauchen deshalb größere Versionen. Alternativ darf man mit `MaxCorrectedCodewords > 0` einzelne Konflikt-Codewörter einer Seite opfern, die der Scanner dann per Fehlerkorrektur repariert. Das ergibt deutlich kleinere Codes, kostet aber etwas Robustheit.

### 7. Formatinformation

Die Formatinformation ist ein BCH(15,5)-Code mit Hamming-Abstand 7; er korrigiert bis zu 3 Bitfehler. Keines der 32 gültigen Formatwörter ist bitweise umgekehrt wieder gültig, denn der Hamming-Abstand ist immer ≥ 3. Jede Richtung bekommt daher eine eigene Kopie. Die jeweils fremde Kopie wird so gesetzt, dass sie von *allen* Formatwörtern mindestens 4 Bit entfernt ist. Dann verwerfen Decoder sie, statt sich in die falsche Richtung locken zu lassen. Die Regeln dafür wurden an echten Decodern gemessen:

- OpenCV scheitert, sobald irgendeine Kopie zu einem falschen Formatwort korrigierbar ist.
- ZBar kombiniert die Hälften beider Kopien per Mehrheitsentscheid.
- zxing-cpp bewertet auch die gespiegelte Lesart.

Meist liest jede Richtung ihr Formatwort deshalb mit **1 Bit Abweichung**. Die Formatinformation verträgt bis zu 3 Bitfehler, das korrigiert also jeder Scanner. `NormalFormatBitErrors` und `MirroredFormatBitErrors` zeigen den Wert an.

### 8. Lösen mit Gauß-Jordan über GF(2)

Die Koeffizientenmatrix ist für alle acht Masken der Richtung A und alle acht Masken der Richtung B identisch – nur die rechte Seite hängt vom Maskenpaar ab. QrrQ packt die 64 rechten Seiten in die 64 Bits eines `ulong` und führt **eine einzige** Gauß-Jordan-Elimination über Bitsets durch. Danach steht für jedes der $8 \times 8 = 64$ Maskenpaare fest, ob es eine Lösung gibt; die optisch beste gewinnt (Strafpunkte nach ISO/IEC 18004). Die Maskenbewertung ist transpositionsinvariant und gilt damit für beide Leserichtungen.

### 9. Umlaute und Emojis

Nicht-ASCII-Text wird als UTF-8 mit ECI-Kennung kodiert. Ohne Kennung zeigt ZBar Zeichensalat. OpenCV verarbeitet ECI-Codes über einen eigenen Pfad, der zwei Eigenheiten hat: Er liest über den Terminator hinaus bis zur Bytegrenze, und nach einem ECI-Wechsel wertet er nur das letzte Segment aus. QrrQ setzt deshalb die Bits bis zur Bytegrenze immer auf 0 und kodiert UTF-8-Texte stets in einem einzigen Segment.

## Tests und Verifikation

**Unit-Tests** (`dotnet test`): 37 Tests decken Rundläufe in beide Richtungen, alle Drehungen, UTF-8/Emoji, Determinismus über den Seed, Versionsgrenzen, ECC-Kombinationen, Vorkorrektur, Fehlerfälle, PNG/SVG/Text-Ausgabe und die Rastertransformationen ab.

**Selbsttest** (`qrrq --selftest 200`): erzeugt zufällige Paare, liest beide Richtungen mit dem eingebauten Decoder, prüft eine gedrehte Ansicht und kippt zusätzlich Datenmodule, um die Fehlerkorrektur zu belasten.

**Unabhängige Decoder** (aktuelle Messung, 8 repräsentative Codes, Version 4–14, ASCII, Umlaute, Emoji, WLAN-String, Vorkorrektur):

| Ansicht | OpenCV 5.1 | zxing-cpp |
|---|---|---|
| normal | 8/8 | 8/8 |
| gespiegelt | 8/8 | 8/8 |
| gespiegelt + 90°/180°/270° | 8/8 | 8/8 |
| gespiegelt + 15° | 7/8 | 8/8 |

OpenCV verliert bei einem Code und 15° den Detektor, ein paar Grad weiter klappt es wieder – dasselbe Verhalten zeigt OpenCV auch bei gewöhnlichen QR-Codes. Eine frühere Messreihe mit 108 Codes (Versionen 1–16, ASCII und Umlaute, mit und ohne Vorkorrektur, je 5 Ansichten) ergab für zxing-cpp und ZBar 100 % in allen Ansichten und für OpenCV 100 % ohne Drehung.

## Grenzen und Hinweise

- **Größe:** Kurze Texte (Codes, Hinweise) passen in Version 1–7. Zwei URLs mit je ~20 Zeichen brauchen fehlerfrei etwa Version 10 (57 × 57). Mit `MaxCorrectedCodewords = 4` reicht oft Version 5–7.
- **Kamera-Apps:** Die Scanner von iOS und Android sind proprietär und hier nicht testbar. Bitte die fertigen Codes mit den Geräten prüfen, die später wirklich scannen sollen.
- **Kein Sicherheitsmerkmal:** Jeder kann einen Code spiegeln. QrrQ versteckt Informationen vor flüchtigen Blicken, nicht vor Angreifern.
- **Druck:** Auf Folie oder Glas ist die Rückseite automatisch das Spiegelbild, ein Spiegel ist dann gar nicht nötig.
- **Druckgröße:** Mindestens 4 Module Ruhezone einhalten (Standard) und den Code nicht unter ca. 2 cm Kantenlänge drucken.

## Ideen für den Einsatz

- **Escape Rooms und Schnitzeljagden:** Der „Fehlhinweis“ lässt sich normal scannen, die Lösung erst mit dem Spiegel im Raum.
- **Folien und Glas:** Ein Sticker an der Glastür zeigt außen „Öffnungszeiten“ und innen „WLAN fürs Team“.
- **Selfie-Effekt:** Ein T-Shirt- oder Poster-Code liefert über die gespiegelte Frontkamera einen Bonusinhalt.
- **Geocaching (Mystery-Caches), Adventskalender, Spieleverpackungen:** eine zweite Ebene für Neugierige.
- **Kunst und Installationen:** Spiegelungen in Wasser, Schaufenstern, Rückprojektion.
- **Unterricht:** ein greifbares Beispiel für lineare Algebra über GF(2) und Reed-Solomon-Codes.

## Projektstruktur

```
QrrQ.slnx
src/QrrQ/                 Bibliothek (NuGet-Paket „QrrQ“)
  QrrQGenerator.cs        öffentliche API (Create / TryCreate)
  QrrQCode.cs             Ergebnis: PNG, SVG, Text, Matrix, Verify()
  QrrQResult.cs           union QrrQResult(QrrQCode, QrrQError)       – C# 15
  QrrQError.cs            closed record QrrQError + Fälle              – C# 15
  QrMatrix.cs             Modulraster, Transpose/Flip/Rotate, Packen
  Codec/MirrorSolver.cs   Kern: GF(2)-System, Masken-Spuren, Ecken-Konflikte
  Codec/FormatPlanner.cs  scannerfeste Belegung der Formatkopien
  Algebra/                GF(256), GF(2)-Elimination, Reed-Solomon (BM/Chien/Forney)
  Reading/                eingebauter Decoder (zur Verifikation)
  Rendering/              PNG (ohne Abhängigkeiten), SVG, Konsole
src/QrrQ.Cli/             Kommandozeile „qrrq“ (dotnet-Tool)
tests/QrrQ.Tests/         xUnit-Tests (37)
samples/QrrQ.Playground/  Blazor-WebAssembly-Playground (GitHub Pages)
.github/workflows/ci.yml  Build + Tests bei Push und Pull Request
.github/workflows/nuget.yml  Packen und Veröffentlichen der NuGet-Pakete
.github/workflows/native.yml Native-AOT-Binaries für vier Plattformen
.github/workflows/pages.yml  Playground auf GitHub Pages ausliefern
```

Verwendete Sprachfeatures: Union-Typen, geschlossene Hierarchien, `with(...)`-Argumente in Collection Expressions, benanntes `break` (C# 15), Extension-Member und das `field`-Schlüsselwort (C# 14) sowie Primärkonstruktoren und Collection Expressions.

## Entwicklung

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/QrrQ.Cli -c Release -- --selftest 200

dotnet pack src/QrrQ -c Release -o artifacts
dotnet pack src/QrrQ.Cli -c Release -o artifacts

# natives Einzelbinary, z. B. für diese Maschine
dotnet publish src/QrrQ.Cli -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/native
```

### Veröffentlichung

Das Paket erscheint auf nuget.org unter dem Publisher **Agredo**. Die Metadaten (`Authors`, `Company`, `RepositoryUrl`, `PackageProjectUrl`, `PackageIcon`, `PackageTags`, `Description`) stehen zentral in `Directory.Build.props` bzw. in den beiden Projektdateien.

Der Workflow `.github/workflows/nuget.yml` baut, testet und packt beide Pakete und lädt sie als Artefakt hoch. Ausgelöst wird er durch einen Tag `v*` oder manuell über „Run workflow“:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Zum Veröffentlichen muss im Repository das Secret `NUGET_API_KEY` hinterlegt sein (nuget.org → API Keys). Ohne Secret wird nur gepackt und das Artefakt bereitgestellt. Bei einem Tag wird zusätzlich ein GitHub-Release mit den Paketdateien angelegt.

## Herkunft und Namensnennung

QrrQ – Umsetzung: **Christian Müller (Agredo)**, 2026. Die Grundidee doppelseitiger QR-Codes wurde zuerst von Alexey Tikhonov (2019) beschrieben, siehe „Verwandte Arbeiten“.

Die MIT-Lizenz erlaubt jede Nutzung, Veränderung und Weitergabe, auch kommerziell und ohne Gegenleistung. Sie verlangt nur, dass Copyright- und Lizenzhinweis bei einer Weitergabe erhalten bleiben. Damit die Herkunft sichtbar bleibt, liegen zusätzlich bei:

- [`NOTICE`](NOTICE) – Herkunftshinweis, zweisprachig
- [`CITATION.cff`](CITATION.cff) – Zitiermetadaten (GitHub zeigt damit einen „Cite this repository“-Knopf)

Wer QrrQ weitergibt, forkt oder in ein Produkt einbaut, möge den Hinweis auf Urheber und Herkunft mitführen. Die Idee hinter „ein QR-Code, zwei Lesarten“ ist im Abschnitt [Mathematischer Hintergrund](#mathematischer-hintergrund) bewusst vollständig offengelegt und darf weiterentwickelt werden – nur bitte nicht ohne den Ursprung zu nennen.

## Verwandte Arbeiten

QrrQ ist nicht der erste Ansatz für doppelseitige QR-Codes:

- **Alexey Tikhonov: „On Double-Sided QR-Codes“** (arXiv:1902.05722, SIGBOVIK 2019) – die erste dokumentierte Beschreibung der Idee, mit einem Brute-Force- und einem analytischen Verfahren. https://arxiv.org/abs/1902.05722
- **Joris van Rantwijk: „Double-sided QR codes“** (30.06.2025) – ein sehr ähnlicher Ansatz: ein gemeinsames GF(2)-Gleichungssystem über die Syndrom-Bits beider Nachrichten, eine palindromische Formatinformation und absichtliche, per Fehlerkorrektur reparierte Fehler an den Überlappungen. Nur Fehlerkorrekturstufe L, Versionen 2–5. Online-Generator verfügbar. https://jorisvr.nl/article/double-sided-qrcodes · https://jorisvr.nl/article/make-mirror-qrcode

Andere Prinzipien mit zwei Inhalten in einem Code (nicht spiegelbasiert):

- Blickwinkelabhängig: „Dueling QR Codes“ (arXiv:2503.13458) und das Beispiel auf Hackaday (https://hackaday.com/2025/01/23/this-qr-code-leads-to-two-websites-but-how)
- Entfernungsabhängig: Dual-Message-QR-Code, Sensors 2024, 24(10), 3055 (https://www.mdpi.com/1424-8220/24/10/3055)
- Verwandte Mathematik: QArt Codes von Russ Cox (2012) steuern Module ebenfalls über lineare Algebra über GF(2) (https://research.swtch.com/qart)

**Was QrrQ beiträgt:**

- Fehlerfreie Daten in beiden Richtungen als Standard; Fehlerkorrektur „opfern“ ist nur eine Option (`MaxCorrectedCodewords`).
- Alle sinnvollen Kombinationen der Fehlerkorrektur (L/L, M/L, M/M, Q/L, H/L) und die Versionen 1–40.
- Formatbelegung pro Maskenpaar, abgestimmt auf gemessene Decoder-Regeln von OpenCV, ZBar und zxing-cpp; verschiedene Masken und Stufen je Richtung möglich.
- Alle 64 Maskenpaare in einer einzigen Elimination sowie gleichwertige Segment-Kodierungen, um die Überlappung in der gemeinsamen Ecke aufzulösen.
- Eine Open-Source-.NET-Bibliothek mit NuGet-Paket und CLI (MIT-Lizenz).

## Lizenz

MIT – siehe [LICENSE](LICENSE) und [NOTICE](NOTICE).