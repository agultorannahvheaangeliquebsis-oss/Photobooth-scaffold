# One-Week Build Plan

Status as of 2026-08-30.

## Done

**Day 1 — Nikon D3500 PTP spike**
- `Photobooth.CameraBridge.Host` (net48/x86) wraps digiCamControl's
  `CameraControl.Devices` and exposes `PING`/`STATUS`/`CAPTURE` over a
  named pipe (`PhotoboothCameraBridge`) — has to be a separate process
  because that library targets net46 and its bundled PTP interop only
  loads under x86.
- `Photobooth.CameraBridge.Client` proved the pipe round-trip end to end
  (tested against a stand-in webcam, since no D3500 was attached).
- Fixed along the way: missing .NET 8 SDK on this machine, `NuGet.config`
  had all package sources cleared (added `nuget.org`).
- **Not yet verified:** real D3500 hardware. See README's "Camera: Nikon
  D3500" section for the full writeup.

**Extra, not in the original plan — cloud upload + QR download**
- `ICloudUploadService` / `MockCloudUploadService` + `QrCodeGenerator`
  (QRCoder), wired into `BoothStateMachine` so upload runs in the
  background alongside Reviewing/Printing and never blocks the print.
- WPF UI shows a QR panel during Printing/Complete once the upload
  finishes. Verified live in the running app (screenshotted).
- Found and fixed a pre-existing crash on the way: `Assets/Logo.png`
  wasn't embedded as a WPF resource.
- **Backend swapped from the original plan:** Firebase Storage now
  requires the paid Blaze plan just to provision a bucket (changed since
  this plan was written), and no card is available. Used Cloudinary
  instead — same seam, free tier needs no card. Added
  `CloudinaryCloudUploadService : ICloudUploadService` (`CloudinaryDotNet`
  NuGet package) wired into `MainWindow`'s composition root in place of
  the mock; reads credentials from `CLOUDINARY_URL` (same
  environment-variable pattern as `PHOTOBOOTH_DB_CONNECTION` in
  `SqlConnectionFactory`), throwing a clear error at startup if unset.
  **Not yet verified: an actual Cloudinary account** — needs a real
  `CLOUDINARY_URL` to test the upload → URL → QR path end to end; not
  fixed now since it needs your account, not just code.

**Extra, not in the original plan — live camera preview during Countdown**
- New `ILiveViewService` (`MockLiveViewService` / `PtpLiveViewService`,
  same interface-plus-mock pattern as camera/printer/cloud), added
  `LIVEVIEW` / `LIVEVIEW_STOP` to the bridge protocol.
- Real path calls `StartLiveView()` / `GetLiveViewImage()` on
  `CameraControl.Devices`, same as digiCamControl uses for tethered
  cameras. **Not yet verified against the D3500** (same caveat as
  Day 1/2's capture work).
- The UVC webcam stand-in doesn't implement that API at all
  (`HaveLiveView` is always false for it in this library) — added a
  fallback in `HandleLiveViewFrame()` that grabs a live frame via a
  plain `CapturePhoto()` cycle instead, reusing the same raw-bytes
  extraction as `CAPTURE`. Measured ~130ms per round trip for the
  webcam, fast enough to poll at ~7fps.
- `MainWindow.xaml`'s Countdown screen is now a `Grid`: a full-bleed
  `Image` behind a `StackPanel` with an explicit transparent background,
  so the feed shows through around the countdown number. A
  `DispatcherTimer` polls `ILiveViewService.GetFrameAsync()` only while
  Countdown is showing, and calls `StopAsync()` the moment any other
  state shows (including right before Capturing, since some PTP cameras
  won't take a full-res shot while live view is still streaming — the
  bridge also defensively stops live view itself in `HandleCapture()` in
  case the client and hardware race).
- Verified live in the running app: real webcam feed rendering behind a
  visible countdown number, screenshotted mid-countdown.

**Day 3 — Persistence layer**
- New `Photobooth.Data` project (net8.0, `Microsoft.Data.SqlClient` against
  LocalDB) added to the solution and referenced from `Photobooth.UI`.
- Repository classes for the six tables the plan called out:
  `LocationRepository`, `BookingRepository`, `PrinterRepository` (plain
  CRUD, used by seeding today, will back Day 6's admin dashboard later),
  plus `SqlSessionRepository` which owns the other three (`Session`,
  `Print`, `Payment`) since those three only ever get written together as
  part of one guest session's lifecycle.
- `ISessionRepository` added to `Photobooth.Core` as the seam (same
  interface-plus-mock pattern as camera/printer/cloud upload) —
  `BoothStateMachine` now takes it as a fourth constructor argument and
  creates a `Session` row at the start of `RunSessionAsync`, a `Print` row
  right after a successful print, a `Payment` row (`'free_event'`, since
  the vendo flow doesn't exist until Day 6), and marks the session
  `completed` or `error` on the way out. `MockSessionRepository` (in
  `Photobooth.Core`) is an in-memory stand-in that also exposes what it
  recorded, for `Photobooth.ConsoleDemo` and future `Photobooth.Tests`
  assertions.
- `DatabaseInitializer.InitializeAsync()` (in `Photobooth.Data`) is
  idempotent: creates the `Photobooth` database on LocalDB if missing,
  applies `schema.sql` if the tables aren't there yet (the Data project
  links straight to the root `schema.sql` and copies it to its own output
  dir, so there's one source of truth), then seeds one `Location`, one
  `Printer`, and two `Booking` rows if the `Location` table is empty —
  otherwise reuses what's already there. Returns the seeded
  `LocationId`/`PrinterId` so `SqlSessionRepository` has FK values to write
  against.
- `MainWindow`'s composition root now calls `DatabaseInitializer
  .InitializeAsync()` synchronously at startup (blocking is fine here —
  it runs once, before the window shows, and every session after it
  depends on the seeded ids anyway) and wires `SqlSessionRepository` into
  `BoothStateMachine` in place of a mock.
- Verified via `Photobooth.ConsoleDemo`: 3 sessions run against
  `MockSessionRepository`, session 2's forced capture failure correctly
  produces no `Print`/`Payment` row (`Sessions recorded: 3 (2 completed, 1
  failed), 2 prints, 2 payments`), matching the state machine's actual
  behavior.
- **Not yet verified: a real LocalDB instance** — not installed on this
  dev machine (no `sqllocaldb` on PATH), same category of gap as the D3500
  and Firebase credentials. Found one real thing worth knowing before this
  ships, though: running the actual WPF exe against a nonexistent
  `(localdb)\MSSQLLocalDB` instance doesn't fail fast — `SqlConnection
  .OpenAsync()` just hangs (confirmed: still running, no output, 25+
  seconds in) rather than throwing quickly. If the booth machine's LocalDB
  service is ever down at boot, today's code would hang the app on a black
  screen instead of showing an error. Worth a connection timeout + a
  friendly message once there's a real instance to test against — not
  fixed now since it'd be guessing at behavior I can't observe here.

**Day 4 — Test project**
- New `Photobooth.Tests` project (xunit, scaffolded via `dotnet new xunit`
  so the package versions matched the installed SDK rather than guessed),
  added to the solution and referencing `Photobooth.Core`. Zero coverage
  existed before this.
- `BoothStateMachineTests` — the two transition paths the plan called out:
  the happy path (asserts the full `Countdown → ... → Idle` state
  sequence, and that a `Session`/`Print`/`Payment` row each get recorded
  exactly once against `MockSessionRepository`) and the forced-failure
  path (`MockCameraService.FailNextCapture`, asserts the session gets
  marked failed with *no* `Print`/`Payment` row, and that
  `FailNextCapture` resets itself after firing).
- `MockServicesTests` — `MockCameraService` (writes a real BMP with a
  `'B' 'M'` header, frame numbers increment across calls, throws once
  then resets), `MockPrinterService` (completes without throwing),
  `MockCloudUploadService` (returned URL contains the file name).
- `MockSessionRepositoryTests` — session ids increment, `RecordPrintAsync`/
  `RecordPaymentAsync`/`CompleteAsync`/`FailAsync` each append to their own
  list independently.
- `QrCodeGeneratorTests` — output starts with the real PNG magic bytes.
- Real repository methods aren't covered here — `SqlSessionRepository`/
  `DatabaseInitializer` need a live LocalDB instance, same gap as Day 3.
  `MockSessionRepository` is what's actually exercised, which is what the
  plan meant by "repository methods" in a mocks-first project like this
  one.
- Verified by actually running the suite, not just building it:
  `dotnet test` → **11 passed, 0 failed** (16s — the two full-session
  tests run the state machine's real countdown/print/error delays rather
  than a faked clock, so they're slow but exercise the real timing path).

## Remaining

**Day 2 — Swap mocks for real backends**
- [x] `PtpCameraService : ICameraService` (pipe client) written in
      `Photobooth.Core` — sends `CAPTURE` over the named pipe, returns the
      path from `OK <path>`, throws on `ERR`/timeout.
- [x] Swapped in at the WPF composition root ([MainWindow.xaml.cs:17](Photobooth.UI/MainWindow.xaml.cs#L17)).
- [x] End-to-end capture verified — but against this machine's webcam as a
      stand-in, not the D3500 (still not attached). `Photobooth.CameraBridge.Host`
      gained an `--allow-webcam` flag (off by default — production runs
      against the real booth camera never pass it) to opt back into webcam
      detection for this kind of dev-machine testing. Hit and fixed a real
      bug along the way: `PhotoCapturedEventArgs.Transfer()` failed for the
      webcam because `.Handle` came back as raw `System.Byte[]` instead of
      a PTP device handle; `HandleCapture()` in
      [Program.cs](Photobooth.CameraBridge.Host/Program.cs) now writes
      those bytes directly when that happens. Confirmed via three live
      sessions through the running app — valid JPEGs (`FFD8FFE0` header,
      150–178KB) landed in `captures/` with no errors.
- [ ] Run an actual capture against the D3500 once it's connected — the one
      thing still not verified against real hardware. Should just work
      (same pipe protocol either way) but the webcam's raw-bytes path is a
      different code branch than a real PTP `Transfer()`, so this isn't
      free to assume.
- [x] `CloudinaryCloudUploadService : ICloudUploadService` written in
      `Photobooth.Core` (`CloudinaryDotNet` package), swapped in at the WPF
      composition root in place of `MockCloudUploadService`. Firebase was
      dropped from the plan — it now requires the paid Blaze plan just to
      provision a Storage bucket, and no card is available.
- [ ] Create a Cloudinary account (free, no card) and set `CLOUDINARY_URL`
      so the swapped-in service actually has something to talk to —
      end-to-end upload → URL → QR still needs this verified.
- Both remaining items are gated on something only you can provide (the
  D3500 being plugged in; a Cloudinary account) — if either isn't ready,
  it slips to the Day 7 buffer.

**Day 5 — Real printer integration**
- `IPrinterService` against the Windows print spooler
  (`System.Drawing.Printing.PrintDocument`).

**Day 6 — Vendo payment flow + admin dashboard**
- Mock QR payment service (`IPaymentService`, same interface pattern),
  inserted before `Printing` in vendo mode; event mode still skips
  straight through as `free_event`.
- Simple admin view: sessions today, revenue by mode, low-inventory
  alerts from `InventoryLog`.

**Day 7 — Buffer**
- Absorbs whatever slipped from Day 2 first (hardware/credentials are
  the least predictable dependency this week).
- Otherwise: polish, README status update, full end-to-end demo pass
  with real camera, real printer, and real cloud upload.

## Future roadmap (post-week-one)

Feature-parity wishlist against commercial booth software (LumaBooth,
dslrBooth). None of this fits the one-week plan above — Days 1–7 are
already fully allocated to getting a single event-mode session working
end to end with real hardware. Listed here so it isn't lost, unprioritized
and unscheduled until there's a week to plan around it.

**Capture & media modes**
- [ ] Alternative capture formats: GIFs, Boomerangs, slow-motion
      sequences, full HD/4K video.
- [ ] 360-degree booth support: rotating multi-angle video loops with
      speed ramping and custom soundtracks.
- [ ] Glam Booth mode: automated skin smoothing, high-contrast B&W filter.
- [ ] Video guestbook: recorded personalized messages/greetings from guests.

**Customization & design**
- [ ] Built-in print template editor: 4x6, 2x6 strip, custom dimensions,
      logos, text, graphics.
- [ ] Screen & UI customization: start screen, buttons, backgrounds,
      themes per event brand.
- [ ] Green screen / chroma key: real-time background replacement with
      custom digital backdrops.
- [ ] Virtual attendant / mirror booth: guided video/audio prompts
      through the session.
- [ ] Digital overlays & stickers: static/animated graphics, props,
      filters layered over media.

**Sharing & connectivity**
- [ ] Instant digital sharing: email, SMS/MMS, WhatsApp, QR code, AirDrop.
- [ ] Offline queueing: store shares locally, auto-upload once back online.
- [ ] Cloud sync: event data, analytics, and media synced across devices.
- [ ] Hashtag printing: pull and print tagged photos from social feeds.

**Workflow & management**
- [ ] Live view & camera control: shutter speed, aperture, ISO, live
      preview alignment from the software UI.
- [ ] Surveys & data collection: feedback, email opt-in, liability
      disclaimer prompts before delivery.
- [ ] Cashless payments: card reader / digital payment integration
      (extends the `IPaymentService` seam from Day 6).
- [ ] Remote booth control: guests/attendants trigger workflows from a
      companion mobile app.
