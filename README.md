# Focus & Snap

Booth management software built to run Focus & Snap Studio's photobooth
rental business — capture, print, and revenue tracking across two operating
modes: **event** (attended, flat fee) and **vendo** (unattended,
pay-per-print).

## Why this exists

Off-the-shelf photobooth software (LumaBooth, dslrBooth) runs the actual
business today. This project is a from-scratch reimplementation: same
real-world requirements (camera tethering, print pipeline, error recovery,
event vs. vendo revenue models), built to understand and demonstrate the
full system, not just the UI layer.

## Architecture

- **`Photobooth.Core`** — camera/printer abstracted behind interfaces
  (`ICameraService`, `IPrinterService`), so the state machine is testable
  without hardware and swappable between mock and real implementations
  without touching business logic.
- **`BoothStateMachine`** — drives a guest session through
  `Idle → Countdown → Capturing → Reviewing → Printing → Complete → Idle`,
  with every failure path recovering back to `Idle` instead of hanging.
- **`Photobooth.ConsoleDemo`** — proves the state machine end to end,
  including a forced failure, before any UI or real hardware exists.
- **`schema.sql`** — relational schema covering locations, bookings,
  sessions, prints, payments, and printer inventory. See the ERD below.
- **`Photobooth.CameraBridge.Host`** — net48/x86 process that drives the
  Nikon D3500 via digiCamControl's `CameraControl.Devices` library and
  exposes capture over a named pipe. See "Camera: Nikon D3500" below for why
  this is a separate process instead of a direct reference from
  `Photobooth.Core`.
- **`Photobooth.CameraBridge.Client`** — spike harness that exercises the
  bridge's pipe protocol (`PING`/`STATUS`/`CAPTURE`) from net8.0. Not the
  real `ICameraService` implementation — that comes next.
- **`ICloudUploadService`** — same seam as the camera/printer: uploads the
  captured photo somewhere a guest's phone can reach it and returns a URL,
  so `QrCodeGenerator` can turn that URL into a QR code shown on screen.
- **`Photobooth.Data`** — LocalDB-backed persistence: `SqlSessionRepository`
  (implements `ISessionRepository`, the same interface-plus-mock seam as
  everything else) writes `Session`/`Print`/`Payment` rows as a session
  runs, plus `LocationRepository`/`BookingRepository`/`PrinterRepository`
  for the other three tables and `DatabaseInitializer` to create the DB,
  apply `schema.sql`, and seed a `Location`/`Printer`/two `Booking` rows on
  first run.
- **`Photobooth.Tests`** — xunit coverage for `Photobooth.Core`: state
  machine transitions (happy path and the forced-failure path) and the
  mock camera/printer/cloud-upload/session-repository implementations.
  `dotnet test` — 11 passed.

```
Location ──< Session >── Print
   │            │
   │            └──< Payment
   ├──< Booking
   └──< Printer ──< InventoryLog
```

## Status

- [x] Core state machine + mocks, tested via console demo
- [x] Database schema
- [x] Persistence layer wired in (LocalDB via `Photobooth.Data`; not yet
      verified against a real LocalDB instance, see below)
- [x] WPF UI bound to the state machine
- [x] Test coverage (`Photobooth.Tests`, xunit) — state machine
      transitions, mock camera/printer/cloud/session paths
- [ ] Real camera integration (Nikon D3500 via PTP — no official Nikon SDK
      support for this body, so this goes through digiCamControl's
      CameraControl library or gPhoto2 instead of a vendor SDK)
- [ ] Real printer integration via Windows print spooler
- [ ] Admin dashboard (sales, inventory alerts)
- [x] Cloud upload + QR download for guests (`CloudinaryCloudUploadService`
      wired in; pending a real `CLOUDINARY_URL` to verify end to end)

## Running it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone <this-repo>
cd Photobooth-scaffold
dotnet run --project Photobooth.ConsoleDemo
```

Expected output: three simulated sessions, with session 2 deliberately
forced to fail at capture to demonstrate error recovery.

## Why the camera and printer are interfaces, not concrete classes

`BoothStateMachine` never talks to hardware directly. `ICameraService` and
`IPrinterService` are the seam: `MockCameraService`/`MockPrinterService`
let the whole app — UI, state transitions, error handling — get built and
demoed before the real camera and printer integrations exist. Swapping in
the real camera later is a one-line change at the composition root, not a
rewrite.

## Camera: Nikon D3500

The booth camera is a Nikon D3500 — an entry-level body that Nikon's own
SDK doesn't support for tethered capture (their SDK targets higher-end
bodies). The real `ICameraService` implementation drives it via
digiCamControl's `CameraControl.Devices` library, confirmed to support the
D3500 by other users of that project.

**Why a bridge process, not a direct reference:** `CameraControl.Devices`
targets .NET Framework 4.6 (last published 2018) and its bundled PTP/COM
interop only loads under `x86` — referencing it directly from the net8.0
projects in this solution isn't viable. `Photobooth.CameraBridge.Host` is a
separate net48/x86 console process that owns the `CameraDeviceManager` and
exposes capture over a named pipe (`PhotoboothCameraBridge`); a future
`ICameraService` implementation in `Photobooth.Core` will be a thin pipe
client. `Photobooth.CameraBridge.Client` is the throwaway spike harness
that proved the pipe round-trip works (`PING`/`STATUS`/`CAPTURE`).

**Confirmed working (no D3500 attached yet, tested against a laptop
webcam as a stand-in PTP-ish device):** the bridge process starts, the
camera manager initializes, a named-pipe client on net8.0 can drive it,
and a full capture → event → file-transfer round trip completes. **Not
yet verified:** actual behavior against the real D3500 — the webcam
stand-in exercises a different code path (`PhotoCapturedEventArgs.Handle`
came back as raw bytes rather than a transferable device object), so this
still needs a real run once the camera is connected.

This required two changes: `NuGet.config` now allows `nuget.org` (previously
locked to no package sources — this is the project's first external
dependency), and `Manager.DetectWebcams` is forced off so the bridge never
mistakes a laptop webcam for the booth camera.

## Cloud upload & QR download

Guests can scan a QR code to get their photo on their own phone instead of
(or alongside) the printed copy. Same interface-plus-mock pattern as the
camera and printer:

- **`ICloudUploadService`** — `UploadAsync(path) -> Uri`. `MockCloudUploadService`
  simulates upload latency and returns a fake URL, for dev/tests.
  Real backend: **Cloudinary** (`CloudinaryCloudUploadService`), wired into
  the WPF composition root. Firebase Storage was the original plan but now
  requires the paid Blaze plan just to provision a bucket, so this project
  uses Cloudinary's free tier instead — same seam, no card needed. Reads
  credentials from the `CLOUDINARY_URL` environment variable (copy it from
  the Cloudinary dashboard's Settings → API Keys page); the app throws a
  clear startup error if it's unset. **Not yet verified against a real
  account** — needs a live `CLOUDINARY_URL` to confirm the upload → URL →
  QR path end to end.
- **`QrCodeGenerator`** (via the `QRCoder` NuGet package) — pure local PNG
  generation from the upload URL, no network call, so this half works
  regardless of which cloud backend ends up behind `ICloudUploadService`.
- **`BoothStateMachine`** kicks off the upload right after a successful
  capture and runs it in the background alongside Reviewing/Printing —
  it never blocks the print, which is the part that actually matters to a
  guest standing at the booth. `LastPhotoUrl` and the `PhotoUploaded` event
  expose the result once it lands; a failed or slow upload just means no QR
  shows that session, not a stuck booth.
- **UI**: a QR panel appears bottom-right during the Printing and Complete
  screens (the two with enough natural dwell time for a guest to actually
  scan something) once the upload finishes.

Verified in the running WPF app (screenshotted during Printing and Complete,
QR panel rendering correctly) and in the console demo, where the mock upload
fires and completes mid-Reviewing well before Printing starts.

**Incidental fixes made while wiring this up**, unrelated to the feature
itself but found along the way:
- `Photobooth.UI.csproj` was missing a `<Resource Include="Assets\Logo.png" />`
  item, so `Window.Icon="Assets/Logo.png"` crashed the app on startup with
  `IOException: Cannot locate resource 'assets/logo.png'` — the file existed
  on disk but wasn't embedded as a WPF pack resource. Also needed a clean
  rebuild (`bin`/`obj` deleted) — the incremental build didn't pick up the
  csproj change on its own.

## Persistence layer

`BoothStateMachine` now writes every session to LocalDB instead of just
holding state in memory, via the same interface-plus-mock seam as the
camera/printer/cloud upload:

- **`ISessionRepository`** (in `Photobooth.Core`) — `CreateAsync`,
  `CompleteAsync`, `FailAsync`, `RecordPrintAsync`, `RecordPaymentAsync`.
  `MockSessionRepository` is an in-memory stand-in used by
  `Photobooth.ConsoleDemo`; `SqlSessionRepository` (in `Photobooth.Data`)
  is the real LocalDB-backed implementation used by the WPF app.
- **`Photobooth.Data`** — new project, `Microsoft.Data.SqlClient` against
  `(localdb)\MSSQLLocalDB`. `LocationRepository`/`BookingRepository`/
  `PrinterRepository` round out the six tables the schema needs a
  repository for; `DatabaseInitializer.InitializeAsync()` creates the
  database if missing, applies the root `schema.sql` if the tables aren't
  there yet, and seeds one `Location`, one `Printer`, and two `Booking`
  rows on first run so the FK chain isn't empty.
- **`BoothStateMachine`** creates a `Session` row at the start of every
  run, a `Print` row right after a successful print, a `Payment` row
  (`'free_event'`, since the vendo payment flow doesn't exist until Day 6),
  and marks the session `completed` or `error` on the way out — a session
  that fails mid-capture correctly gets no `Print`/`Payment` row.

Verified via `Photobooth.ConsoleDemo` against `MockSessionRepository`:
3 simulated sessions produce `2 completed, 1 failed, 2 prints, 2 payments`,
matching the forced-failure session correctly skipping both. **Not yet
verified: a real LocalDB instance** — not installed on this dev machine.
Worth knowing before this ships: running the WPF app against a
`(localdb)\MSSQLLocalDB` instance that doesn't exist doesn't fail fast —
`SqlConnection.OpenAsync()` just hangs rather than throwing — so the booth
would show a black screen instead of an error if LocalDB is ever down at
boot. Worth a connection timeout once there's a real instance to test
against.
