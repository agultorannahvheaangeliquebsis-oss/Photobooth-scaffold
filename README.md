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
  `Idle → Consent → Countdown → Capturing → Reviewing → FramePicker → Printing → Complete → Idle`
  (`FramePicker` only shows when at least one admin-configured frame is
  active — a fresh booth skips straight from `Reviewing` to `Printing`),
  with every failure path recovering back to `Idle` instead of hanging.
  Takes its dependencies as one `BoothServices` record rather than
  thirteen separate constructor parameters — that seam grew one interface
  at a time as features landed, and a bundle stayed readable (and cheap
  to extend) where a positional parameter list stopped being.
- **`Photobooth.ConsoleDemo`** — proves the state machine end to end,
  including a forced failure, before any UI or real hardware exists.
- **`schema.sql`** — relational schema covering locations, bookings,
  sessions, prints, payments, consent records, and printer inventory. See
  the ERD below.
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
  runs, plus `LocationRepository`/`BookingRepository`/`PrinterRepository`/
  `InventoryLogRepository` for the other tables,
  `AdminDashboardRepository` for the read-only dashboard queries, and
  `DatabaseInitializer` to create the DB, apply `schema.sql`, and seed a
  `Location`/`Printer`/two `Booking`/one `InventoryLog` row on first run.
- **`SpoolerPrinterService : IPrinterService`** — sends the captured photo
  through the Windows print spooler (`System.Drawing.Printing.PrintDocument`),
  scaled to fit the page margins. See "Printer: Windows print spooler"
  below.
- **`IPaymentService`** — same seam as camera/printer/cloud upload, for
  vendo-mode payment before printing. Two implementations:
  `MockQrPaymentService` (GCash/Maya-style scan-to-pay) and
  `MockCardReaderPaymentService` (tap/insert/swipe, no QR). See "Vendo
  payment flow" below.
- **`IPendingUploadQueue`** — same seam again, backs a durable retry queue
  for uploads that fail (dropped venue WiFi). See "Offline upload
  queueing" below.
- **`IConsentService`** — same seam again, collects the liability
  disclaimer acceptance and email opt-in before a session runs. See
  "Liability disclaimer & email opt-in" below.
- **`IEmailDeliveryService`** — same seam again, actually follows through
  on that opt-in once the photo's uploaded. See "Email delivery on
  opt-in" below.
- **`IPhotoBrandingService`** — same seam again, though for a different
  reason: there's no hardware or network to fake here, it just keeps
  `System.Drawing.Common` (Windows-only) out of `Photobooth.Tests`/
  `Photobooth.ConsoleDemo`. See "Digital branding overlay" below.
- **`IPhotoFilterService`** — same seam and same reasoning, for the
  Glam Booth high-contrast B&W filter. See "Glam Booth mode" below.
- **`IBoothSettingsProvider`** — same seam again, backs the admin-editable
  countdown duration and Glam Booth toggle. See "Admin settings screen"
  below.
- **`IFrameLibraryService`**/**`IFrameOverlayService`** — the data and
  compositing halves of admin-managed frame overlays: the first reads
  which frames are currently active, the second composites the guest's
  pick onto the photo. Same interface-plus-mock seams as everything else
  above them. See "Frame library & guest frame picker" below.
- **`IFrameSelectionService`** — collects the guest's frame pick during
  `FramePicker`. Unlike `IConsentService`/`IPaymentService` (both still
  mock-only — a real disclaimer/gateway needs external integration work
  this project hasn't done yet), this one has a real implementation,
  `UiFrameSelectionService`: a frame pick is just a button tap with no
  hardware or network dependency to stand up. See "Frame library & guest
  frame picker" below.
- **`Photobooth.Tests`** — xunit coverage for `Photobooth.Core`: state
  machine transitions (happy path, forced-failure path, vendo payment
  via both gateways, payment decline, offline upload queueing,
  disclaimer decline, email delivery, Glam Booth mode, custom countdown
  settings, and the frame picker's three cases — frame chosen, frame
  skipped, no frames configured) and the mock/real camera, printer,
  cloud upload, session repository, both payment gateways,
  pending-upload-queue, consent, email, photo-branding, photo-filter,
  settings, and frame-library/frame-selection/frame-overlay
  implementations. `dotnet test` — 65 passed.

```
Location ──< Session >── Print
   │            │
   │            ├──< Payment
   │            └──< Consent
   ├──< Booking
   ├──< Frame
   └──< Printer ──< InventoryLog
```

## Status

- [x] Core state machine + mocks, tested via console demo
- [x] Database schema
- [x] Persistence layer wired in and **verified against a real LocalDB
      instance** (`Photobooth.Data`) — see "Persistence layer" below
- [x] WPF UI bound to the state machine
- [x] Test coverage (`Photobooth.Tests`, xunit) — state machine
      transitions (happy path, forced-failure, vendo payment, offline
      upload queueing, disclaimer decline), mock camera/printer/cloud/
      session/payment/upload-queue/consent paths. `dotnet test` — 28
      passed.
- [ ] Real camera integration (Nikon D3500 via PTP — no official Nikon SDK
      support for this body, so this goes through digiCamControl's
      CameraControl library or gPhoto2 instead of a vendor SDK). Pipe
      client written and verified against a webcam stand-in; the real
      D3500 itself still isn't attached to a dev machine.
- [x] Real printer integration via Windows print spooler
      (`SpoolerPrinterService`) — verified spooling a real job against a
      driver-installed stand-in printer; actual physical dye-sub output
      still isn't verified (no printer attached). See "Printer: Windows
      print spooler" below.
- [x] Vendo payment flow (`IPaymentService`; mock QR gateway plus a
      second mock card-reader gateway) — see "Vendo payment flow" below.
- [x] Admin dashboard (sessions today, revenue by mode, low-inventory
      alerts) — see "Admin dashboard" below.
- [x] Cloud upload + QR download for guests (`CloudinaryCloudUploadService`
      wired in and **verified against a real Cloudinary account** — see
      "Cloud upload & QR download" below)
- [x] Offline upload queueing (`IPendingUploadQueue`) — a failed upload is
      queued and retried instead of the photo just being lost. See
      "Offline upload queueing" below.
- [x] Liability disclaimer + email opt-in (`IConsentService`) — every
      session records accept/decline and opt-in before it proceeds. See
      "Liability disclaimer & email opt-in" below.
- [x] Email delivery on opt-in (`IEmailDeliveryService`) — a guest who
      opted in actually gets emailed once their photo uploads. See
      "Email delivery on opt-in" below.
- [x] Digital branding overlay (`IPhotoBrandingService`) — every photo
      gets a studio name/date caption bar before print/upload. See
      "Digital branding overlay" below.
- [x] Glam Booth mode, B&W filter half (`IPhotoFilterService`) — a
      per-booth setting, off by default. See "Glam Booth mode" below.
- [x] Admin settings screen (`IBoothSettingsProvider`) — countdown
      duration and Glam Booth mode, editable and taking effect without a
      restart. See "Admin settings screen" below.
- [x] Frame library & guest frame picker (`IFrameLibraryService`,
      `IFrameSelectionService`, `IFrameOverlayService`) — admin-managed
      frame overlays, plus a real (not mocked) guest-facing picker
      screen. See "Frame library & guest frame picker" below.
- [x] Print template editor (`PrintTemplate`) — admin-configurable paper
      size and Single/Strip layout, fed into `SpoolerPrinterService`. See
      "Print template editor" below.
- [x] General feedback surveys (`IFeedbackService`) — a post-session star
      rating and optional comment, plus an admin dashboard summary. See
      "General feedback surveys" below.

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
dependency), and camera detection runs in two passes so the bridge never
mistakes a laptop webcam for the booth camera when a D3500 is actually
attached: it first scans with `Manager.DetectWebcams` off (DSLR/tethered
only); only if that finds nothing does it turn `DetectWebcams` on and scan
again, so a webcam is picked up as a fallback rather than a first choice.
This means `Photobooth.UI` doesn't need a D3500 attached to start a session
during dev — it auto-detects whatever camera the machine actually has.
Pass `--require-dslr` to `Photobooth.CameraBridge.Host` (or set
`PHOTOBOOTH_REQUIRE_DSLR=1` before launching `Photobooth.UI`, which forwards
it) to disable the webcam fallback on real booth hardware.

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
  clear startup error if it's unset. **Verified against a real Cloudinary
  account:** a throwaway script (not checked in) called `UploadAsync` on a
  real file and got back a live `res.cloudinary.com` URL. Still open:
  clicking through a full booth session in the actual running app to see
  the QR panel render with a real upload behind it, rather than just the
  API call in isolation.
- **`QrCodeGenerator`** (via the `QRCoder` NuGet package) — pure local PNG
  generation from the upload URL, no network call, so this half works
  regardless of which cloud backend ends up behind `ICloudUploadService`.
- **`BoothStateMachine`** kicks off the upload right after a successful
  capture and runs it in the background alongside Reviewing/Printing —
  it never blocks the print, which is the part that actually matters to a
  guest standing at the booth. `LastPhotoUrl` and the `PhotoUploaded` event
  expose the result once it lands; a failed or slow upload just means no QR
  shows that session, not a stuck booth (the photo itself isn't lost though
  — see "Offline upload queueing" below).
- **UI**: a QR panel appears bottom-right during the Printing and Complete
  screens (the two with enough natural dwell time for a guest to actually
  scan something) once the upload finishes.

Verified in the running WPF app (screenshotted during Printing and Complete,
QR panel rendering correctly) and in the console demo, where the mock upload
fires and completes mid-Reviewing well before Printing starts.

## Offline upload queueing

A guest's photo shouldn't just vanish because the venue's WiFi hiccuped —
`IPendingUploadQueue` is a small durable backlog for uploads that fail, same
interface-plus-mock seam as everything else:

- **`PendingUpload(FilePath, Email)`** — what gets queued: the file, and
  who (if anyone opted in during Consent) to email once it finally
  uploads. **`FileSystemPendingUploadQueue`** — the real implementation.
  Persists the queue to a small JSON file, so it survives an app
  restart, not just a network blip that resolves before the next retry.
  **`MockPendingUploadQueue`** is the in-memory stand-in for tests and
  the console demo.
- **`BoothStateMachine.FinalizeUploadAsync`** decides, once the upload
  settles and the guest has earned their photo (payment cleared, or
  event mode which is free by design): send the email now if the upload
  succeeded, or queue the file — carrying the same email along — if it
  didn't. **`RetryQueuedUploadsAsync()`** calls **`DequeueAllAsync()`**
  to atomically claim the whole backlog, retries each one, emails
  whoever opted in once a retry lands, and re-queues (via
  `EnqueueAsync`) anything that's still failing. Called opportunistically
  at the start of every session (so a backlog flushes as soon as the
  next guest walks up, no dedicated retry timer needed) and once at
  `MainWindow` startup (so a backlog from last night doesn't sit
  unflushed all day waiting for a guest). A retry deliberately doesn't
  re-fire `PhotoUploaded` or update `LastPhotoUrl` — that guest is long
  gone by the time a queued upload finally lands, so there's nothing on
  screen to update; sending their email (if they asked for one) is the
  one thing that still matters.
- **`MockCloudUploadService.FailNextUpload`** (same pattern as
  `MockCameraService.FailNextCapture`) makes the failure path deterministically
  testable instead of relying on an actual dropped connection.
- **Real bug the demo caught, not the tests:** `RetryQueuedUploadsAsync`
  fires fire-and-forget at the start of every session and can outlive a
  short one (a quick Consent decline, say). Running the demo after the
  email-on-retry fix above showed one photo emailed *twice* — a
  still-in-flight retry from one session and a new retry fired by the
  next both read the same pending item before either had removed it.
  First fix attempt (a lock on `BoothStateMachine` itself) didn't hold up
  on rerun: `Photobooth.ConsoleDemo` runs a separate `BoothStateMachine`
  instance per gateway/mode combination, all sharing one queue, so a
  per-instance lock can't stop two *different* instances from racing on
  it. Real fix: `DequeueAllAsync()` atomically claims the entire backlog
  in one locked step, so whichever caller gets there first — regardless
  of which instance it's calling from — leaves nothing for a second,
  overlapping caller to double-process.

Verified via `Photobooth.Tests` (queue-on-failure carrying the right
email, both retry outcomes including email-on-success and no-email-when-
none-was-given, a declined vendo payment with a simultaneously failing
upload queuing nothing at all — guarding against reintroducing the
payment-declined-still-gets-emailed bug via this path instead of the
direct one it was originally fixed on — two regression tests
reproducing the duplicate-email race both same-instance and
cross-instance, `DequeueAllAsync` tested directly, plus the mock and real
file-backed queue including a same-app-restart persistence check) and
via `Photobooth.ConsoleDemo`, where session 3's upload is forced to
fail, "Pending uploads after session 3: 1" confirms it queued instead of
vanishing, and a later session's opportunistic retry drains it back to 0
and emails the guest who was waiting. Wired into `MainWindow`'s
composition root; not yet run against the live app for the same
interactive-desktop reason noted in "Admin dashboard" below, but the
solution builds clean with it in place.

**Incidental fixes made while wiring this up**, unrelated to the feature
itself but found along the way:
- `Photobooth.UI.csproj` was missing a `<Resource Include="Assets\Logo.png" />`
  item, so `Window.Icon="Assets/Logo.png"` crashed the app on startup with
  `IOException: Cannot locate resource 'assets/logo.png'` — the file existed
  on disk but wasn't embedded as a WPF pack resource. Also needed a clean
  rebuild (`bin`/`obj` deleted) — the incremental build didn't pick up the
  csproj change on its own.

## Liability disclaimer & email opt-in

Every session records whether the guest accepted the liability disclaimer
and whether they opted in for an emailed copy, before anything else runs —
same interface-plus-mock seam as the rest of the app:

- **`IConsentService`** — `CollectAsync() -> ConsentResult` (accepted,
  email opt-in, optional email). `MockConsentService` simulates a guest
  reading and tapping through; set `DeclineNext = true` to exercise the
  decline path (same reset-after-firing pattern as
  `MockCameraService.FailNextCapture`). No real interactive button-driven
  capture yet — same "mock only, real integration is future work" status
  `IPaymentService` has today.
- **`BoothState.Consent`** shows first, before `Countdown` — declining
  means no countdown, no capture, no print at all. The outcome is always
  written via the new `ISessionRepository.RecordConsentAsync`, whether
  accepted or declined, and a decline marks the session via the new
  `AbandonAsync` (`Session.Status = 'abandoned'` — a status the schema
  always allowed but nothing had ever actually set before this).
- **`Consent`** table in `schema.sql` (`DisclaimerAccepted`, `EmailOptIn`,
  `Email`, one row per session). `DatabaseInitializer` picks this table up
  automatically on a fresh database; for one that already existed before
  this feature (like this dev machine's), it has a small top-up check
  that creates just the `Consent` table on its own rather than needing a
  full reset.
- **UI**: a `ConsentView` screen shows the disclaimer text — wired into
  `MainWindow`'s state-driven visibility switch the same way as every
  other screen, not yet seen rendered for the same interactive-desktop
  reason noted in "Admin dashboard" below.

Verified via `Photobooth.Tests` (the decline path end to end — states are
exactly `Consent -> Idle`, the session lands in `AbandonedSessionIds`, no
print or payment gets recorded — plus `MockConsentService`'s default-accept,
decline, and opt-out-of-email cases) and via `Photobooth.ConsoleDemo`,
where a forced decline shows exactly `[STATE] Consent` then `[STATE] Idle`
with no capture/print lines, and the final summary counts it as abandoned
rather than completed or failed.

## Email delivery on opt-in

Consent captures `EmailOptIn`/`Email` per session, but until this,
nothing ever read it back — collected and never used. `IEmailDeliveryService`
closes that loop, same interface-plus-mock seam as everything else:

- **`SendPhotoLinkAsync(toEmail, photoUrl)`** — `MockEmailDeliveryService`
  records what it "sent" for tests/the console demo to assert against.
  No real SMTP delivery yet — that needs real mail credentials, same
  "mock only, real integration is future work" status `IPaymentService`
  and `IConsentService` already have.
- **`BoothStateMachine`** sends the email once the guest has definitely
  earned their photo, only if the session's `LastConsent` opted in with
  an email. The upload starts as soon as capture finishes (fire-and-
  forget, same as always — never blocks the print), but the email
  trigger waits until right after the vendo payment gate clears (or
  immediately in event mode, which is free by design). A declined vendo
  payment `throw`s before reaching that point, so it never fires. A
  failed send is swallowed — the guest already has the QR code as a
  working fallback, so an email hiccup shouldn't become a guest-facing
  error.

  This wasn't the original design — email first shipped wired to fire
  right after the upload finished, same timing as the QR code. The
  console demo caught the bug immediately: a vendo session with a
  *declined* card payment still got emailed a free digital copy,
  because upload/email ran independent of what Payment decided
  afterward, unlike the on-screen QR (`QrPanel` only shows during
  `Printing`/`Complete`, so it correctly never appeared). Reproduced
  with a test first (`Assert.Empty(email.SentEmails)` failed with a real
  recorded send), then fixed by capturing the upload's `Task` and
  triggering email from the payment-gate call site instead of from
  inside the upload itself. Gated on "payment cleared," not "print
  succeeded" — a vendo guest who paid but hits a printer jam afterward
  still gets their digital copy; only a guest who never paid doesn't.
- `RetryQueuedUploadsAsync` (see "Offline upload queueing" below) also
  emails whoever opted in once a previously-failed upload's retry
  succeeds — the queue carries the email along, so this isn't limited to
  the same-session path above.

Verified via `Photobooth.Tests` (no email when `EmailOptIn` is false; the
happy-path test extended to confirm the right address and URL; the
vendo-decline test extended to confirm no email) and via
`Photobooth.ConsoleDemo`, where every successful session logs
`[EMAILED] guest@example.com -> https://...` right after `[UPLOADED]`,
and the declined-payment session logs no `[EMAILED]` line at all — the
final summary went from 4 emails sent (bug) to 3 (fixed) across the same
7 demo sessions.

## Digital branding overlay

Every commercial booth (LumaBooth, dslrBooth) stamps a studio name/date
caption onto photos before delivery. `IPhotoBrandingService` does that
here — same interface-plus-mock seam as the rest of the app, but for a
different reason than usual: there's no hardware or network to fake, the
seam exists purely to keep `System.Drawing.Common` (Windows-only) out of
`Photobooth.Tests`/`Photobooth.ConsoleDemo`, same reasoning `PlaceholderImage`
already gives for hand-rolling BMP bytes instead of using it.

- **`GdiPhotoBrandingService`** — the real implementation
  (`[SupportedOSPlatform("windows")]`, same pattern as
  `SpoolerPrinterService`). Composites a black caption bar onto the
  bottom of the photo via GDI+ (`Focus & Snap | <date>`), saved as a new
  `_branded.jpg` file — the original capture is left untouched.
  **`MockPhotoBrandingService`** just copies the file with a `_branded`
  suffix, no GDI+, for tests/the console demo.
- Confirmed the interface indirection does what it's there for: calling
  `GdiPhotoBrandingService` directly from unmarked code would trip
  CA1416 (Windows-only API called from code not marked as such), but
  `BoothStateMachine` only ever holds the unmarked `IPhotoBrandingService`
  interface — `dotnet build` stayed at 0 warnings after wiring this in.
  One test class breaks that pattern on purpose: `GdiPhotoBrandingServiceTests`
  exercises the real GDI+ path directly (to prove actual compositing
  works, not just that a mock returns a plausible path), so it's marked
  `[SupportedOSPlatform("windows")]` too — honest, since the whole
  solution only ever runs on the Windows booth machine anyway.
- `BoothStateMachine` applies branding right after capture, before
  anything else reads `LastCapturedImagePath` — so Reviewing, the
  print, and the upload all see the same branded photo.

Verified via `Photobooth.Tests` (the real compositing path confirms a
genuine JPEG comes out, taller than the input by a caption bar's worth,
same width) and via `Photobooth.ConsoleDemo`, where every session's
captured/uploaded/printed file path now carries the `_branded` suffix
(e.g. `mock_0001_b86a2e03_branded.bmp`). Wired into `MainWindow`'s
composition root as the real implementation, not a mock — compositing
needs no external credentials or hardware, unlike the payment gateway or
email delivery.

## Glam Booth mode

The high-contrast black & white filter half of "Glam Booth mode" (skin
smoothing is separate, unbuilt work — it needs face detection).
`IPhotoFilterService` is the same interface-plus-mock seam as
`IPhotoBrandingService`, for the same reason: no hardware or network to
fake, just keeping `System.Drawing.Common` out of the tests/demo.

- **`GdiPhotoFilterService`** — the real implementation. Two GDI+
  `ColorMatrix` passes: a standard luminance-weighted grayscale
  conversion, then a contrast boost (values pushed away from mid-gray)
  so it reads as "glam," not just desaturated. Saved as a new
  `_glam.jpg` file. **`MockPhotoFilterService`** copies the file with a
  `_glam` suffix, no GDI+, for tests/the demo. Both GDI+ services share
  a small `GdiImageHelpers` class for the file-loading logic they'd
  otherwise duplicate.
- Whether it's on is read from `IBoothSettingsProvider` fresh at the
  start of every session (originally a constructor flag — see "Admin
  settings screen" below for why and when that changed) — a per-booth
  setting, not a per-guest UI choice. Runs before branding when enabled,
  so the caption bar's white-on-black styling stays independent of the
  photo's colors.
- Adding this was the first real test of the `BoothServices` refactor:
  one new property on the record, and (at the time) one new optional
  constructor parameter with no `BoothStateMachine` call site needing to
  change at all.

Verified via `Photobooth.Tests` (the real GDI+ path confirms a genuine
JPEG comes out matching the original's dimensions, and samples pixels
across the image to confirm they're actually grayscale — R == G == B —
proof the color matrix math is correct, not just that a file got
written) and via `Photobooth.ConsoleDemo`, where a Glam-mode session's
final photo path contains both `_glam` and `_branded`, in that order.

## Persistence layer

`BoothStateMachine` now writes every session to LocalDB instead of just
holding state in memory, via the same interface-plus-mock seam as the
camera/printer/cloud upload:

- **`ISessionRepository`** (in `Photobooth.Core`) — `CreateAsync`,
  `CompleteAsync`, `FailAsync`, `RecordPrintAsync`, `RecordPaymentAsync`.
  `MockSessionRepository` is an in-memory stand-in used by
  `Photobooth.ConsoleDemo`; `SqlSessionRepository` (in `Photobooth.Data`)
  is the real LocalDB-backed implementation used by the WPF app.
- **`Photobooth.Data`** — `Microsoft.Data.SqlClient` against
  `(localdb)\MSSQLLocalDB`. `LocationRepository`/`BookingRepository`/
  `PrinterRepository`/`InventoryLogRepository` round out the tables the
  schema needs a repository for; `DatabaseInitializer.InitializeAsync()`
  creates the database if missing, applies the root `schema.sql` if the
  tables aren't there yet, and seeds one `Location`, one `Printer`, two
  `Booking` rows, and one `InventoryLog` row (100 sheets of paper) on
  first run so the FK chain isn't empty and the admin dashboard has
  something to show.
- **`BoothStateMachine`** creates a `Session` row at the start of every
  run, a `Print` row right after a successful print, marks the session
  `completed` or `error` on the way out, and records a `Payment` row —
  `'free_event'` in event mode, or the real paid amount/method in vendo
  mode (see "Vendo payment flow" below). A session that fails mid-capture
  correctly gets no `Print`/`Payment` row.

**Verified against a real LocalDB instance**, not just
`MockSessionRepository`: installed SQL Server Express LocalDB, created
the `MSSQLLocalDB` instance `SqlConnectionFactory` expects, and ran
`DatabaseInitializer` plus `SqlSessionRepository` and
`AdminDashboardRepository` against it directly. This immediately
surfaced a real bug mocks could never catch: `schema.sql`'s `Print` table
was unquoted, and `PRINT` is a reserved T-SQL keyword —
`CREATE INDEX IX_Print_Session ON Print(SessionId)` parsed as the `PRINT`
statement rather than a table reference, so schema creation failed
outright the first time it ever ran against a real engine. Fixed by
bracket-quoting `[Print]` everywhere (`schema.sql` and
`SqlSessionRepository`'s insert). After the fix: schema applies, seed
data inserts, an event session and a vendo session both record correctly,
and the dashboard queries return correct numbers.

Also fixed the hang this file used to flag as a known risk:
`SqlConnection.OpenAsync()` against a missing or stopped LocalDB instance
used to hang indefinitely instead of failing fast. `SqlConnectionFactory`
now sets `Connect Timeout=5` on its default connection string, and
`MainWindow` catches a `DatabaseInitializer` failure and shows a
plain-English `MessageBox` instead of crashing or hanging. Verified: a
nonexistent instance name now fails in ~5s, a stopped-but-registered
instance auto-restarts in ~1s (LocalDB's normal "automatic instance"
behavior), and the running WPF app reaches an idle, near-zero-CPU state
within ~8s when LocalDB is stopped at launch.

## Printer: Windows print spooler

`SpoolerPrinterService : IPrinterService` (in `Photobooth.Core`) sends
the captured photo through `System.Drawing.Printing.PrintDocument`,
scaled to fit the page margins, via the `System.Drawing.Common` package.
No vendor SDK needed here, unlike the camera — any driver-backed printer
(DNP, Selphy, Epson) shows up as a normal Windows printer once its driver
is installed. Reads the target printer name from
`PHOTOBOOTH_PRINTER_NAME` (same environment-variable pattern as
`CLOUDINARY_URL`/`PHOTOBOOTH_DB_CONNECTION`), falling back to the Windows
default printer if unset.

**No physical printer attached**, so verified against a stand-in the same
way the camera work used a webcam: this dev machine has a
driver-installed but currently-unplugged "Canon SELPHY CP1500" queue
(matches the model `DatabaseInitializer` seeds), and Windows' print
spooler accepts a job for an offline printer without it being physically
present. Confirmed via `Get-PrintJob` that a real job (1 page, 31,496
bytes) landed in the queue after calling `PrintAsync()` — proof the call
drives `StartDoc` → draw → `EndDoc` through the real Windows print
pipeline, not just that the method returns without throwing.
"Microsoft Print to PDF" was tried first as a hardware-free stand-in, but
its port (`PORTPROMPT:`) opens an interactive Save-As dialog on every job
with no way to suppress it — ruled out as a verification method, not a
bug in the new code. **Not yet verified: actual physical output** (color,
paper size/margins, driver quirks) — needs the real printer connected.

## Vendo payment flow

`IPaymentService` (in `Photobooth.Core`) is the seam for collecting
payment in vendo mode, same interface-plus-mock pattern as the other
services:

- **`Initiate(amount, reference) -> PaymentPrompt`** — starts a payment
  attempt and returns what to show the guest: `Instructions` (gateway-
  specific text) plus a nullable `QrCodePng`. It wasn't always shaped
  this way — it used to be `GenerateQrCode(amount, reference) -> byte[]`,
  which is fine for a QR gateway but has no sensible answer for a card
  reader (there's nothing to scan). Redesigned once a second gateway
  actually needed to fit through the interface; see below.
- **`WaitForConfirmationAsync(reference, amount)`** — simulates guest
  confirmation time then reports success or decline. No real gateway or
  card-reader hardware yet — that's still future work.
- **`MockQrPaymentService`** — GCash/Maya-style scan-to-pay. Builds a QR
  PNG synchronously (reuses `QrCodeGenerator`; no network call for a
  mock), then reports success after a simulated 2.5s scan-and-confirm
  wait. This is what `MainWindow`'s real composition root uses — GCash/
  Maya QR is the realistic near-term payment method for this business.
- **`MockCardReaderPaymentService`** — tap/insert/swipe, no QR at all
  (`Initiate` returns a null `QrCodePng`). Reports success (as `card`)
  after a simulated 1.2s authorization, faster than the QR gateway since
  there's no phone/app round trip. `DeclineNext` (same reset-after-firing
  pattern as `MockCameraService.FailNextCapture`) simulates a declined
  card — the first payment mock able to do that at all, which means
  `BoothStateMachine`'s payment-declined branch (`throw` → `Error` state
  → `FailAsync`) had never actually been exercised by anything in this
  codebase until this mock existed to trigger it.

`BoothState.Payment` sits between `Reviewing` and `Printing`.
`BoothStateMachine` takes a `mode` ("event" or "vendo", fixed per
instance since one booth machine serves one location) — vendo mode runs
the Payment state and records the real paid amount/method; event mode is
unchanged (`free_event`). `mode` is threaded from
`DatabaseInitializer`'s seeded `Location.Type` through to `MainWindow`'s
composition root, so switching a deployment to vendo is a data change,
not a code change. The WPF UI's `PaymentView` screen is gateway-agnostic:
its subtitle text comes from `PaymentInstructions` at runtime instead of
a hardcoded "Scan to pay", and its QR `Border` collapses when there's
nothing to show.

Verified via `Photobooth.ConsoleDemo` (a QR vendo session confirming
`Payment` fires in the right place and records `₱150.00 qr_gcash`; a card
-reader vendo session confirming no QR code and a `₱150.00 card` record;
and a declined card session confirming the payment-declined path
actually works — `[ERROR] Payment was not completed.` → `Error` state,
no print or payment recorded), `dotnet test`, and directly against a
real LocalDB instance (see "Persistence layer" above). **Not yet
verified:** the `PaymentView` screen actually rendering in the running
UI — that needs a full guest session, which needs the real camera.

## Admin dashboard

`AdminDashboardRepository` (in `Photobooth.Data`) is three read-only
queries: sessions today, revenue by mode (`Payment` joined to `Session`,
`Status = 'paid'`), and low-inventory alerts (the latest `InventoryLog`
row per `PrinterId`+`ItemType`, via `ROW_NUMBER()`, since a printer logs
paper and ribbon independently). `AdminWindow` (WPF) renders those three
sections with a Refresh button, reached from `MainWindow` via F12 —
only while `Idle`, so it can't interrupt a guest session, and off the
touchscreen surface so guests can't stumble into it.

Verified directly against a real LocalDB instance: seeded one event and
one vendo session, confirmed the dashboard correctly reported 2 sessions
today, revenue split by mode, and found the seeded inventory row when
queried with a threshold above it. **Not yet verified:** `AdminWindow`
actually rendering — this dev environment has no interactive desktop to
press F12 and look.

## Admin settings screen

`AdminWindow` also has an editable "Booth settings" section — countdown
duration and Glam Booth mode — the first non-read-only part of the admin
area. Backed by `IBoothSettingsProvider`, same interface-plus-mock seam
as everything else:

- **`Location.CountdownSeconds`/`Location.GlamFilterEnabled`** — new
  columns in `schema.sql` (defaults 3 and 0). One booth machine has one
  location, so booth-wide settings live there rather than a new table —
  same reasoning `Location.Type` (event/vendo) already established.
  `DatabaseInitializer` picks these up automatically on a fresh database;
  an already-seeded one (like this dev machine's) gets them via another
  `ALTER TABLE` top-up, same pattern as the `Consent` table before it.
- **`SqlBoothSettingsProvider`** reads the Location row fresh on *every*
  call, deliberately uncached. `BoothStateMachine` calls
  `GetSettingsAsync()` at the start of every session instead of once at
  construction — that's what makes an admin's save take effect for the
  very next guest, not the app's next restart. **`MockBoothSettingsProvider`**
  is a plain settable holder for tests/the demo.
- This retired the `applyGlamFilter` constructor flag from the previous
  entry — having both a static deploy-time flag and a dynamic per-session
  setting controlling the same behavior would've been confusing, so
  `BoothServices.Settings` is now the only way to control it.
- `AdminWindow`'s new section: a countdown text box, a Glam Booth
  checkbox, and a Save button (`LocationRepository.UpdateSettingsAsync`)
  with basic validation (must parse as a whole number greater than 0).

Verified via `Photobooth.Tests` (a custom `CountdownSeconds` of 5 actually
changes how many `CountdownTick` events fire, proving `BoothStateMachine`
reads the value rather than a hardcoded constant; Glam mode now toggles
through settings instead of the removed constructor flag). Verified the
real SQL path directly too, since `Photobooth.Tests` doesn't cover
SQL-backed code (same gap as `SqlSessionRepository`): a throwaway script
ran the migration against this machine's real, already-seeded LocalDB,
confirmed the defaults read back correctly, wrote `(7, true)`, read it
straight back through `SqlBoothSettingsProvider`, then restored the
default. Verified via `Photobooth.ConsoleDemo`: session 8 flips
`MockBoothSettingsProvider.Settings` mid-run — simulating an admin
saving new settings — on the *same* `BoothStateMachine` instance already
used for earlier sessions, and the next session picks up both the 5-second
countdown and the Glam filter without recreating anything. **Not yet
verified:** the settings screen actually rendering or being clicked
through — same interactive-desktop gap as the rest of `AdminWindow`.

## Print template editor

Admin-configurable print layout — paper size, and whether the photo prints
once ("Single", e.g. a 4x6) or repeated down a narrow strip ("Strip", e.g.
a 2x6 photo-booth strip). Picked up next since the camera and physical
printer were both unavailable to verify against for the time being, and
`SpoolerPrinterService`'s own doc comment already flagged this exact gap
("booth print layout (strip vs. 4x6, borders, branding) is future work").

- **`PrintTemplate`** (`Photobooth.Core`) — `record PrintTemplate(string
  Layout, double WidthInches, double HeightInches, int StripCopies)`, plus
  `IsValid` and `ComputeCellBounds(Rectangle pageBounds)`. The latter is
  pure geometry (one full-bounds rectangle for "Single", `StripCopies`
  equal-height rectangles stacked top to bottom for "Strip") — deliberately
  kept out of `SpoolerPrinterService` so it's unit-testable without a real
  printer or a `[SupportedOSPlatform("windows")]` marking, unlike the
  GDI+-drawing code that actually uses it.
- Folded into the existing `BoothSettings`/`IBoothSettingsProvider` seam
  rather than a new one — a print template is booth-wide and
  admin-editable, exactly like `CountdownSeconds`/`GlamFilterEnabled`
  already are, so it's a third property there instead of a fifteenth
  `BoothServices` seam.
- **`Location.PrintLayout`/`PrintWidthInches`/`PrintHeightInches`/
  `PrintStripCopies`** — four new columns in `schema.sql` (defaults
  `'Single'`, 4, 6, 1 — a plain 4x6). `DatabaseInitializer` picks these up
  automatically on a fresh database; an already-seeded one gets them via
  another `ALTER TABLE` top-up, same pattern as the booth-settings columns
  before it.
- **`IPrinterService.PrintAsync`** now takes a `PrintTemplate` alongside
  the image path. `SpoolerPrinterService` sets `PrintDocument`'s custom
  `PaperSize` from the template's width/height (in hundredths of an inch,
  per that API) and draws the photo — scaled to fit and centered — into
  each cell `PrintTemplate.ComputeCellBounds` hands back.
  `MockPrinterService` records every template it was asked to print with
  (`PrintedTemplates`), so tests/the demo can confirm
  `BoothStateMachine` actually passed the *current* admin setting through,
  not a hardcoded default.
- `AdminWindow`'s Settings section gained a "Print template" block: a
  Single/Strip radio choice, width/height text boxes, and a strip-copies
  text box, validated the same way as the countdown field before saving
  (`PrintTemplate.IsValid`).

Verified via `Photobooth.Tests` — 10 new tests: `PrintTemplateTests`
covers `IsValid`'s rules (bad layout name, non-positive dimensions, fewer
than 1 strip copy) and `ComputeCellBounds` for both layouts directly (a
3-copy strip produces three equal-height, gap-free, top-to-bottom
rectangles); `MockPrinterServiceTests` confirms `PrintedTemplates` records
each call in order; a new `BoothStateMachineTests` case switches the
booth to a 2x6/2-copy strip via `MockBoothSettingsProvider` and confirms
`MockPrinterService.PrintedTemplates` received that exact template, not
`PrintTemplate.Default` — the actual thing this feature needed to prove.
`dotnet test` — **75 passed, 0 failed**, run twice in a row clean.
Verified via `Photobooth.ConsoleDemo`: a new session 9 switches to a 2x6
strip mid-run (same "simulate an admin saving a change" pattern as session
8) and prints `Printed with: PrintTemplate { Layout = Strip, WidthInches =
2, HeightInches = 6, StripCopies = 2, IsValid = True }` — the printer
genuinely received the new template, not the 4x6 default. **Not yet
verified:** the real SQL path (this environment has no LocalDB instance
installed, unlike the dev machine the earlier SQL-backed features were
verified against — same class of gap, different cause) or a physical
sheet actually coming out at the configured size — that needs the real
printer, which is why this was picked up instead of finishing the Day 7
hardware buffer.

## General feedback surveys

A post-session rating/comment prompt — the other unbuilt half of "Surveys
& data collection" (the liability disclaimer and email opt-in half was
already done, see "Liability disclaimer & email opt-in" below). Picked up
next since the camera and printer were both still unavailable, and it's
fully code/test-verifiable without either.

- **`FeedbackResult`** (`Photobooth.Core`) — `record FeedbackResult(int?
  Rating, string? Comment)`, plus `IsEmpty` (true when the guest gave
  neither). `BoothState.Feedback` is a new state shown right after
  `Complete`'s "thank you" dwell, before the machine returns to `Idle`.
- **`IFeedbackService`** — same interface-plus-mock seam as everything
  else. Unlike `IConsentService`/`IPaymentService` (both still mock-only,
  since a real disclaimer/gateway needs external integration), a star
  rating and a comment box is just button taps and text input with no
  hardware or network dependency — same reasoning that made
  `IFrameSelectionService` a real, WPF-backed implementation instead of
  just a mock, so `UiFeedbackService` is too (same `TaskCompletionSource`
  handoff pattern as `UiFrameSelectionService`).
- Collecting feedback is wrapped in its own `try`/`catch` in
  `BoothStateMachine`, separate from the rest of the session: a guest who
  walks away without tapping anything, or any other failure here, should
  never turn an already-completed session (photo captured, paid for if
  vendo, printed) into an `Error` one.
- **`Feedback`** table in `schema.sql` (`Rating` 1-5 nullable, `Comment`
  nullable). A row is only ever inserted when at least one of the two is
  non-null — a guest who skips entirely leaves no row at all, not a row
  full of nulls. `DatabaseInitializer` picks this up automatically on a
  fresh database; an already-seeded one gets it via another
  `CREATE TABLE` top-up, same pattern as `Consent`/`Frame` before it.
- `AdminDashboardRepository` gained `GetFeedbackSummaryAsync` (average
  rating + how many guests actually left one) and `GetRecentCommentsAsync`
  — collecting feedback and never reading it back would repeat the exact
  "data collected and then never used" gap already caught and fixed once
  for email opt-in, so `AdminWindow`'s dashboard section shows both
  alongside sessions/revenue/inventory.
- `MainWindow`'s new `FeedbackView`: five star buttons (☆/★, tap to set
  the rating), an optional comment box, and Submit/Skip — the QR panel
  now also shows during `Feedback` (it previously stopped after
  `Complete`), so a guest still has the code to scan while rating their
  experience.

Verified via `Photobooth.Tests` — 5 new tests: `MockFeedbackServiceTests`/
`UiFeedbackServiceTests` cover both implementations directly (default
5-star/no-comment, skip-then-reset, the real `TaskCompletionSource`
handoff not completing until `SubmitFeedback` is called); a
`BoothStateMachineTests` case confirms the happy path's recorded feedback
matches what `MockFeedbackService` returned (rating 5, no comment), and a
new skip case confirms the `Feedback` state still shows but no `Feedback`
row gets written when the guest gives nothing. `dotnet test` — **80
passed, 0 failed**, run twice in a row clean. Verified via
`Photobooth.ConsoleDemo`: session 12 (a guest leaves a 4-star rating and a
comment) prints `Feedback recorded: 4 stars -- "Loved the frames, printer
was a little slow."`; session 13 (guest skips) prints `Feedback recorded
this session: False` — confirming the empty-skip case leaves no row, not
just that the code ran. Final summary: 9 feedback records across 10
completed sessions (session 13's skip is the one gap). **Not yet
verified:** the real SQL path (no LocalDB in this environment, same gap
as the print template editor above) or `FeedbackView`/the dashboard's new
section actually rendering — same interactive-desktop gap as the rest of
this project's WPF screens.

## Frame library & guest frame picker

Admin-managed frame overlays a guest can pick during a session, plus the
picker screen itself — the two pieces flagged as "not started" the last
time this project's status was reviewed.

- **`Frame`** table in `schema.sql` (`Name`, `ImagePath`, `SortOrder`,
  `IsActive`). One row per overlay, scoped to a `Location` the same way
  `Printer`/`Booking` are. `IsActive` lets an admin retire a frame
  without losing its history; a fresh database (or an already-seeded one,
  via another `ALTER`-free `CREATE TABLE` top-up, same pattern as
  `Consent`/the booth-settings columns before it) starts with zero
  frames, so `FramePicker` is skipped entirely until an admin adds one —
  existing deployments and tests see no behavior change.
- **`FrameRepository`** (`Photobooth.Data`) — plain CRUD, same shape as
  `LocationRepository`/`InventoryLogRepository` (no interface/mock; only
  `AdminWindow` and `SqlFrameLibraryService` talk to it directly).
- **`IFrameLibraryService`** reads the active frames for a location, same
  interface-plus-mock seam (and same "read fresh every session, no
  caching" reasoning) as `IBoothSettingsProvider` — an admin's newly
  added or retired frame takes effect for the very next guest, not the
  app's next restart.
- **`IFrameOverlayService`** composites the chosen frame's PNG onto the
  photo via GDI+ (`GdiFrameOverlayService`), stretched to the photo's
  exact dimensions so it lines up regardless of the frame asset's native
  resolution — same Windows-only, interface-plus-mock pattern as
  `IPhotoBrandingService`/`IPhotoFilterService`.
- **`IFrameSelectionService`** collects the guest's pick. This is the one
  seam in this list that's genuinely real rather than "mock for now, real
  integration is future work" (the status `IConsentService`/
  `IPaymentService` still carry): a frame pick has no external
  hardware/gateway to integrate, it's just a button tap. Real
  implementation is `UiFrameSelectionService` — a `TaskCompletionSource`
  bridge that raises `SelectionRequested` (the UI shows the offered
  thumbnails) and completes once `MainWindow` calls `SubmitSelection` in
  response to a tap. `MockFrameSelectionService` (picks the first option,
  or none if `SkipNext` is set) is what `Photobooth.Tests`/
  `Photobooth.ConsoleDemo` use instead.
- **`BoothStateMachine`** reads the active frame list right after the
  guest's seen their photo on `Reviewing`. If any exist, it shows
  `FramePicker`, waits for a pick, and — if the guest chose one — applies
  it before the upload starts and before printing, so the QR code and the
  physical print both show the same final composited photo (same
  invariant branding/filter ordering already established). If nothing's
  configured, `FramePicker` never shows and the session behaves exactly
  as it did before this feature existed.
- **UI**: `MainWindow` gained a real `FramePickerView` — a WPF screen
  built from actual frame thumbnails (not a placeholder), each a clickable
  `Button` wired to `UiFrameSelectionService.SubmitSelection`, plus a "No
  frame" button. `AdminWindow` gained a "Frame library" section: an
  `ItemsControl` listing existing frames with an Active checkbox and
  Delete button per row, and an "Add Frame" form (name + `OpenFileDialog`
  image picker) that copies the chosen image into a local
  `Assets/Frames/` folder and inserts the row.

Verified via `Photobooth.Tests`: three new `BoothStateMachineTests` cases
(frame chosen — `FramePicker` shows between `Reviewing` and `Printing`,
the framed path is what gets printed and uploaded, not the pre-frame one;
guest skips the frame — `FramePicker` still shows but nothing's applied;
no active frames — `FramePicker` never shows at all) plus unit coverage
for each new mock (`MockFrameLibraryService`, `MockFrameSelectionService`,
`MockFrameOverlayService`), a real round-trip test for
`UiFrameSelectionService` (`SelectFrameAsync` doesn't complete until
`SubmitSelection` is called, and a null submission means "skipped"), and a
`GdiFrameOverlayServiceTests` that composites a real frame PNG with a
transparent center and an opaque red border onto a real captured photo,
confirming the output is a genuine same-dimension JPEG, the transparent
region still shows the original photo's color, and the opaque region
shows the frame's. `dotnet test` — 65 passed. Verified via
`Photobooth.ConsoleDemo`: session 9 (an admin-simulated two-frame library)
shows `[STATE] FramePicker`, picks "Classic Gold Border", and the final
photo path/uploaded URL both carry a `_framed` suffix; session 10 (guest
skips) shows `FramePicker` too but the final path has no `_framed`
suffix. Verified the real SQL path directly, same reasoning as
`SqlSessionRepository`/`SqlBoothSettingsProvider` (`Photobooth.Tests`
doesn't cover SQL-backed code): a throwaway script ran the migration
against this machine's real, already-seeded LocalDB, inserted a frame,
confirmed `SqlFrameLibraryService` returned it as active, deactivated it
and confirmed the active list emptied while the all-frames list still
showed it, then deleted it and confirmed the table was back to its
starting count. **Not yet verified:** `FramePickerView`/`AdminWindow`'s
new section actually rendering or being tapped through — same
interactive-desktop gap as the rest of the WPF UI.
