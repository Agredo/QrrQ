# QrrQ – QR codes with a mirror secret

[![NuGet QrrQ](https://img.shields.io/nuget/v/QrrQ.svg?label=NuGet%20QrrQ)](https://www.nuget.org/packages/QrrQ)
[![NuGet QrrQ.Cli](https://img.shields.io/nuget/v/QrrQ.Cli.svg?label=NuGet%20QrrQ.Cli)](https://www.nuget.org/packages/QrrQ.Cli)
[![CI](https://github.com/Agredo/QrrQ/actions/workflows/ci.yml/badge.svg)](https://github.com/Agredo/QrrQ/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**English** · [Deutsch](README.de.md)

**QrrQ** generates QR codes with two payloads: scanned normally they return text A, scanned in a mirror (or from the back of a transparency) they return text B. In both reading directions the data are error-free Reed-Solomon codewords (with `MaxCorrectedCodewords = 0`), and common scanners read both directions normally. Strictly speaking, the codes are nevertheless not compliant with ISO/IEC 18004: the format information deviates on purpose (usually 1 bit per direction), and the pad bytes do not follow the standard `0xEC/0x11` pattern. No special app is needed — any ordinary scanner will do.

- .NET 11 / C# 15, no runtime dependencies (own QR encoder, Reed-Solomon, PNG and SVG output)
- API: two strings in, one code out as `byte[]` PNG, SVG or module grid
- Built-in decoder verifies every solution in both directions
- Thoroughly tested (37 unit tests, self-test) and cross-checked against **OpenCV** and **zxing-cpp**
- NuGet packages: [`QrrQ`](https://www.nuget.org/packages/QrrQ) (library) and [`QrrQ.Cli`](https://www.nuget.org/packages/QrrQ.Cli) (dotnet tool)
- Native AOT: prebuilt, trimmed single files for win-x64, win-arm64, linux-x64 and osx-arm64 (no .NET installation required, ~1.7 MB)

## Installation

```bash
# library
dotnet add package QrrQ

# command line tool (command: qrrq)
dotnet tool install -g QrrQ.Cli
```

## Ready-made binaries (Native AOT)

For machines without a .NET runtime there are fully precompiled single files. They are built as **Native AOT** with `TrimMode=full`, invariant globalization and without reflection metadata — the result is one executable with no runtime dependencies.

| Platform | RID | File | Size (unpacked) |
|---|---|---|---|
| Windows x64 | `win-x64` | `qrrq-<version>-win-x64.zip` | 1.7 MB |
| Windows ARM64 | `win-arm64` | `qrrq-<version>-win-arm64.zip` | 1.7 MB |
| Linux x64 | `linux-x64` | `qrrq-<version>-linux-x64.tar.gz` | 1.8 MB |
| macOS Apple Silicon | `osx-arm64` | `qrrq-<version>-osx-arm64.tar.gz` | 1.7 MB |

The archives themselves are about 0.8 MB.

Download the assets of the latest [GitHub release](https://github.com/Agredo/QrrQ/releases), unpack, run `qrrq` (or `qrrq.exe`), done:

```bash
qrrq "Wrong trail" "Code: 4711" -o puzzle --svg
```

The archives contain `LICENSE` and `NOTICE`. They are produced by `.github/workflows/native.yml`: four runners, one `dotnet publish -p:PublishAot=true` each, followed by a self-test of the produced file before packaging. On a `v*` tag the workflow attaches the archives to the release automatically.

## Quick start

```csharp
using QrrQ;

var code = QrrQGenerator.Create("Wrong trail", "Code: 4711");

byte[] png = code.ToPng();                            // print artwork
var (normal, mirrored) = code.ToPngPair();            // artwork + mirror image (for on-screen testing)
string svg = code.ToSvg(moduleSize: 8);
bool[,] modules = code.Matrix.ToArray();              // raw data [row, column]
byte[] packed = code.Matrix.ToPackedRows();           // 1 bit per module

Console.WriteLine(code);   // QrrQ V6 (41×41), M/M: "Wrong trail" ⇄ "Code: 4711"
```

Without exceptions, using a union type (C# 15):

```csharp
var message = QrrQGenerator.TryCreate(a, b, new QrrQOptions { MaxVersion = 10 }) switch
{
    QrrQCode code => $"Version {code.Version}, error-free: {code.IsErrorFree}",
    QrrQError error => error.Message,   // PayloadTooLong | IncompatibleEccLevels | NoSolutionFound
};
```

### Command line

```bash
qrrq "Wrong trail" "Code: 4711" -o puzzle --svg
# → puzzle.png, puzzle.mirrored.png, puzzle.svg, puzzle.mirrored.svg + console preview

qrrq --selftest 200        # random round trips including a damage test

# straight from source:
dotnet run --project src/QrrQ.Cli -- "Wrong trail" "Code: 4711" -o puzzle --svg
```

## Options

| Option | Default | Meaning |
|---|---|---|
| `NormalEcc` / `MirroredEcc` | `null` (automatic M, then L) | error correction per direction |
| `MinVersion` / `MaxVersion` | 1 / 40 | allowed size range |
| `MaxCorrectedCodewords` | 0 | codewords that may be pre-corrected per direction (see below) |
| `Seed` | fixed | same input + same seed = same code |
| `AttemptsPerCandidate` | 2 | random fill attempts per mask pair; the best-looking one wins |

## Mathematical background

### 1. Grid, mirror and transposition

A QR symbol of version $v$ is a square grid

$$M \in \mathbb{F}_2^{n \times n}, \qquad n = 17 + 4v,$$

where $M_{ij} = 1$ means a dark module. The mirrored view is the horizontal flip

$$F(M)_{ij} = M_{i,\,n-1-j}.$$

For the transpose $T(M)_{ij} = M_{ji}$ we get: mirroring is transposing plus a rigid 90° rotation,

$$F(M) = R_{90}\bigl(T(M)\bigr).$$

Scanners compensate rotations by themselves. So we are looking for a grid $M$ such that $M$ yields text A and $T(M)$ yields text B. That is the whole idea in one line:

$$R(M) = A, \qquad R\bigl(T(M)\bigr) = B,$$

where $R$ denotes the complete read path of a decoder (normalize position, read format, unmask, Reed-Solomon correct, parse segments).

### 2. Symmetric function patterns

Finder, timing and alignment patterns as well as the reserved areas for format and version information are symmetric about the main diagonal. Hence under $T$ the set of function modules maps onto itself; the **data modules are merely permuted**. This permutation $\sigma$ is an involution,

$$\sigma^2 = \mathrm{id},$$

because transposing twice is the identity. It is the only place in the procedure where the geometry of the QR code enters.

### 3. A linear system over GF(2)

Let $x_k$ be the $k$-th data module in read order (starting at the bottom right, zigzag upwards). Direction A reads stream bit $k$ as

$$a_k = x_k \oplus m_A(k),$$

while the mirrored direction B reads the same cell at the position $\sigma(k)$:

$$b_k = x_{\sigma(k)} \oplus m_B(k).$$

Here $m_A, m_B \in \mathbb{F}_2$ are the values of the eight mask formulas (row $i$, column $j$), for example

$$m_0(i,j) = (i+j) \bmod 2, \qquad m_4(i,j) = \left(\left\lfloor i/2 \right\rfloor + \left\lfloor j/3 \right\rfloor\right) \bmod 2.$$

The unknowns are the $x_k$. We require:

1. **The message bits** of both texts sit at the start of their data stream. Each message bit is a fixed linear equation $x_k \oplus m_A(k) = a_k$ or $x_{\sigma(k)} \oplus m_B(k) = b_k$.
2. **All Reed-Solomon syndromes** of both directions are zero.

Together this is a linear system over $\mathbb{F}_2$. The degrees of freedom live in the padding bytes behind the terminator — bits no scanner evaluates. That is why both directions are **completely error-free Reed-Solomon codewords** and the full error correction remains available.

### 4. Reed-Solomon as eight bit equations

A codeword $c(x) = \sum_i c_i x^i$ with $c_i \in \mathrm{GF}(2^8)$ is valid exactly when all syndromes vanish:

$$S_j = c(\alpha^j) = \sum_i c_i\,\alpha^{ij} = 0, \qquad j = 0,\dots,e-1,$$

where $e$ is the number of check codewords per block. QrrQ computes in $\mathrm{GF}(2^8)$ using the QR polynomial

$$x^8 + x^4 + x^3 + x^2 + 1 \quad (\texttt{0x11D}), \qquad \alpha = 2.$$

Multiplication by the constant $\alpha^{ij}$ is an $\mathbb{F}_2$-linear map. Each syndrome $S_j \in \mathrm{GF}(2^8)$ therefore splits into **eight bit equations**. Together with the message bits this yields a linear system whose right-hand side is fixed for both reading directions.

### 5. Capacity condition

Let $D_A, D_B$ be the data codeword capacity of the two directions and $C$ the total number of codewords in the symbol. The construction needs room for two payloads and two sets of check bits. A necessary condition is

$$D_A + D_B > C.$$

Hence L/L, M/L and M/M work, as do Q/L and H/L; Q/M only barely. Q/Q and H with M, Q or H have no room and are reported as `IncompatibleEccLevels`. Satisfying the condition does not yet mean the system is solvable — that is why QrrQ additionally searches over versions and mask pairs.

### 6. The shared corner

Both data streams start at the bottom right, and that corner is a **fixed point of the transposition**. Modules that determine *both* messages therefore have to agree. This is handled by the choice of masks and the choice of encoding: a text may live in one segment or in two, and scanners reassemble them into the same text. The number of such conflict modules grows approximately with

$$\frac{|A| \cdot |B|}{n^2}.$$

Long texts therefore need larger versions. Alternatively `MaxCorrectedCodewords > 0` allows sacrificing individual conflict codewords of one side, which the scanner then repairs via error correction. That yields noticeably smaller codes but costs some robustness.

### 7. Format information

Format information is a BCH(15,5) code with Hamming distance 7; it corrects up to 3 bit errors. None of the 32 valid format words is valid again when bitwise inverted, since the Hamming distance is always ≥ 3. Each direction therefore gets its own copy. The respective foreign copy is placed so that it is at least 4 bits away from *all* format words. Decoders then discard it instead of being lured into the wrong direction. These rules were measured against real decoders:

- OpenCV fails as soon as any copy is correctable into a wrong format word.
- ZBar combines the halves of both copies by majority vote.
- zxing-cpp also evaluates the mirrored reading.

Mostly each direction therefore reads its format word with **1 bit of deviation**. Format information tolerates up to 3 bit errors, so every scanner corrects that. `NormalFormatBitErrors` and `MirroredFormatBitErrors` expose the value.

### 8. Solving with Gauss-Jordan over GF(2)

The coefficient matrix is identical for all eight masks of direction A and all eight masks of direction B — only the right-hand side depends on the mask pair. QrrQ packs the 64 right-hand sides into the 64 bits of a `ulong` and performs **a single** Gauss-Jordan elimination over bitsets. Afterwards it is known for each of the $8 \times 8 = 64$ mask pairs whether a solution exists; the best-looking one wins (penalty score per ISO/IEC 18004). The mask evaluation is transposition-invariant and thus valid for both reading directions.

### 9. Umlauts and emojis

Non-ASCII text is encoded as UTF-8 with an ECI indicator. Without the indicator ZBar shows mojibake. OpenCV handles ECI codes through its own path, which has two quirks: it reads past the terminator up to the byte boundary, and after an ECI switch it only evaluates the last segment. QrrQ therefore always sets the bits up to the byte boundary to 0 and always encodes UTF-8 texts in a single segment.

## Tests and verification

**Unit tests** (`dotnet test`): 37 tests cover round trips in both directions, all rotations, UTF-8/emoji, determinism via the seed, version limits, ECC combinations, pre-correction, error cases, PNG/SVG/text output and the grid transformations.

**Self-test** (`qrrq --selftest 200`): generates random pairs, reads both directions with the built-in decoder, checks a rotated view and additionally flips data modules to stress the error correction.

**Independent decoders** (latest measurement, 8 representative codes, versions 4–14, ASCII, umlauts, emoji, Wi-Fi string, pre-correction):

| View | OpenCV 5.1 | zxing-cpp |
|---|---|---|
| normal | 8/8 | 8/8 |
| mirrored | 8/8 | 8/8 |
| mirrored + 90°/180°/270° | 8/8 | 8/8 |
| mirrored + 15° | 7/8 | 8/8 |

OpenCV loses the detector on one code at 15° and recovers a few degrees further on — the same behaviour OpenCV shows for ordinary QR codes. An earlier series with 108 codes (versions 1–16, ASCII and umlauts, with and without pre-correction, 5 views each) gave 100 % in all views for zxing-cpp and ZBar, and 100 % without rotation for OpenCV.

## Limits and caveats

- **Size:** short texts (codes, hints) fit into versions 1–7. Two URLs of ~20 characters each need about version 10 (57 × 57) when error-free. With `MaxCorrectedCodewords = 4`, versions 5–7 often suffice.
- **Camera apps:** the iOS and Android scanners are proprietary and cannot be tested here. Please check the finished codes with the devices that will actually scan them.
- **Not a security feature:** anyone can mirror a code. QrrQ hides information from casual glances, not from attackers.
- **Printing:** on a transparency or glass the back side is automatically the mirror image, so no mirror is needed at all.
- **Print size:** keep at least a 4-module quiet zone (standard) and do not print the code below roughly 2 cm edge length.

## Ideas for use

- **Escape rooms and scavenger hunts:** the "wrong hint" scans normally, the real solution only with the mirror found in the room.
- **Transparencies and glass:** a sticker on a glass door shows "opening hours" outside and "Wi-Fi for the team" inside.
- **Selfie effect:** a T-shirt or poster code delivers bonus content through the mirrored front camera.
- **Geocaching (mystery caches), advent calendars, game packaging:** a second layer for the curious.
- **Art and installations:** reflections in water, shop windows, rear projection.
- **Teaching:** a hands-on example of linear algebra over GF(2) and Reed-Solomon codes.

## Project structure

```
QrrQ.slnx
src/QrrQ/                 library (NuGet package "QrrQ")
  QrrQGenerator.cs        public API (Create / TryCreate)
  QrrQCode.cs             result: PNG, SVG, text, matrix, Verify()
  QrrQResult.cs           union QrrQResult(QrrQCode, QrrQError)       – C# 15
  QrrQError.cs            closed record QrrQError + cases              – C# 15
  QrMatrix.cs             module grid, Transpose/Flip/Rotate, packing
  Codec/MirrorSolver.cs   core: GF(2) system, mask traces, corner conflicts
  Codec/FormatPlanner.cs  scanner-proof placement of the format copies
  Algebra/                GF(256), GF(2) elimination, Reed-Solomon (BM/Chien/Forney)
  Reading/                built-in decoder (for verification)
  Rendering/              PNG (no dependencies), SVG, console
src/QrrQ.Cli/             command line "qrrq" (dotnet tool)
tests/QrrQ.Tests/         xUnit tests (37)
.github/workflows/ci.yml  build + tests on push and pull request
.github/workflows/nuget.yml  pack and publish the NuGet packages
.github/workflows/native.yml Native AOT binaries for four platforms
```

Language features used: union types, closed hierarchies, `with(...)` arguments in collection expressions, labelled `break` (C# 15), extension members and the `field` keyword (C# 14), plus primary constructors and collection expressions.

## Development

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/QrrQ.Cli -c Release -- --selftest 200

dotnet pack src/QrrQ -c Release -o artifacts
dotnet pack src/QrrQ.Cli -c Release -o artifacts

# native single file, e.g. for this machine
dotnet publish src/QrrQ.Cli -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/native
```

### Publishing

The package appears on nuget.org under the publisher **Agredo**. The metadata (`Authors`, `Company`, `RepositoryUrl`, `PackageProjectUrl`, `PackageIcon`, `PackageTags`, `Description`) lives centrally in `Directory.Build.props` and in the two project files.

The workflow `.github/workflows/nuget.yml` builds, tests and packs both packages and uploads them as an artifact. It is triggered by a `v*` tag or manually via "Run workflow":

```bash
git tag v1.0.0
git push origin v1.0.0
```

To publish, the repository needs the secret `NUGET_API_KEY` (nuget.org → API keys). Without the secret it only packs and provides the artifact. On a tag it additionally creates a GitHub release with the package files.

## Origin and attribution

QrrQ — implementation by **Christian Müller (Agredo)**, 2026. The basic idea of double-sided QR codes was first described by Alexey Tikhonov (2019), see "Related work".

The MIT license permits any use, modification and redistribution, including commercially and without consideration. It only requires that the copyright and license notice are retained when redistributing. So that the origin stays visible, two more files ship with the project:

- [`NOTICE`](NOTICE) — provenance notice, bilingual
- [`CITATION.cff`](CITATION.cff) — citation metadata (GitHub shows a "Cite this repository" button)

If you redistribute QrrQ, fork it or embed it in a product, please carry the reference to the project and its author along. The idea behind "one QR code, two readings" is deliberately documented in full in [Mathematical background](#mathematical-background) and is free to build upon — just please do not obscure where it came from.

## Related work

QrrQ is not the first approach to double-sided QR codes:

- **Alexey Tikhonov: "On Double-Sided QR-Codes"** (arXiv:1902.05722, SIGBOVIK 2019) – the first documented description of the idea, with a brute-force and an analytic method. https://arxiv.org/abs/1902.05722
- **Joris van Rantwijk: "Double-sided QR codes"** (2025-06-30) – a very similar approach: one joint GF(2) system over the syndrome bits of both messages, palindromic format information, and intentional errors at the overlaps that error correction repairs. Error correction level L only, versions 2–5. Online generator available. https://jorisvr.nl/article/double-sided-qrcodes · https://jorisvr.nl/article/make-mirror-qrcode

Other ways to carry two messages in one code (not mirror-based):

- Angle-dependent: "Dueling QR Codes" (arXiv:2503.13458) and the example on Hackaday (https://hackaday.com/2025/01/23/this-qr-code-leads-to-two-websites-but-how)
- Distance-dependent: dual-message QR code, Sensors 2024, 24(10), 3055 (https://www.mdpi.com/1424-8220/24/10/3055)
- Related math: Russ Cox's QArt Codes (2012) also control modules via linear algebra over GF(2) (https://research.swtch.com/qart)

**What QrrQ adds:**

- Error-free data in both directions by default; sacrificing error correction is opt-in (`MaxCorrectedCodewords`).
- All workable error-correction combinations (L/L, M/L, M/M, Q/L, H/L) and versions 1–40.
- Format information planned per mask pair, matched to measured decoder rules of OpenCV, ZBar and zxing-cpp; different masks and levels per direction are possible.
- All 64 mask pairs in a single elimination, plus equivalent segment encodings to resolve the overlap in the shared corner.
- An open-source .NET library with NuGet package and CLI (MIT license).

## License

MIT — see [LICENSE](LICENSE) and [NOTICE](NOTICE).
