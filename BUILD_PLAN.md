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
  **Update — verified against a real Cloudinary account.** You created a
  free-tier account and set `CLOUDINARY_URL`. Confirmed the upload leg
  with a throwaway script (not checked in) that called
  `CloudinaryCloudUploadService.UploadAsync` on a real file: it returned
  `https://res.cloudinary.com/va7jmxhy/image/upload/.../photobooth/test_xvhzxd.png`,
  a real hosted URL, not a fake one. `QrCodeGenerator` turning a URL into
  a PNG is pure local code already covered by `QrCodeGeneratorTests`, so
  the two halves are each verified — what's still open is watching the
  full booth session (capture → upload → QR shown on screen) in the
  actual running app rather than a script.

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
- **Update — now verified against a real LocalDB instance.** Installed
  SQL Server Express LocalDB (`SqlLocalDB.msi` from Microsoft, elevated
  install) and created/started the `MSSQLLocalDB` instance
  `SqlConnectionFactory`'s default connection string already expected.
  Found and fixed a real bug this exposed: `schema.sql`'s `Print` table
  was unquoted, and `PRINT` is a reserved T-SQL keyword — `CREATE INDEX
  IX_Print_Session ON Print(SessionId)` parsed as the `PRINT` statement
  rather than a table reference (`Incorrect syntax near the keyword
  'Print'`), so schema application failed outright the first time it ever
  ran against a real engine. Fixed by bracket-quoting it everywhere:
  `[Print]` in `schema.sql` (table + index) and in
  `SqlSessionRepository.RecordPrintAsync`'s `INSERT INTO [Print]`. This is
  exactly the class of bug mocks can never catch.
  Verified via a throwaway console script (not checked in) exercising the
  real code paths end to end: `DatabaseInitializer.InitializeAsync()`
  created the DB, applied the fixed schema, and seeded Location/Printer/
  Booking/InventoryLog; `SqlSessionRepository` recorded one real event
  session (print + free_event payment) and one real vendo session (paid
  150.00 qr_gcash + print); `AdminDashboardRepository` correctly reported
  2 sessions today, revenue split by mode, and found the seeded 100-sheet
  paper row when queried with a threshold above it. Also launched the
  actual WPF exe (real composition root, not the script) and confirmed
  `DatabaseInitializer.InitializeAsync().GetAwaiter().GetResult()`
  completes without hanging or crashing at startup. Database was then
  dropped back to empty so the next real run starts from a clean seed,
  same as a first-ever launch would.
  The connection-timeout/hang concern noted below is still open — that's
  about a *missing* instance, not today's fix, and wasn't re-tested since
  the instance is installed now.
- **Update — fixed.** `SqlConnectionFactory`'s default connection string
  now sets `Connect Timeout=5`. Verified against both realistic failure
  modes with a throwaway script hitting `SqlConnection.OpenAsync()`
  directly: a nonexistent instance name fails in ~4.8s with a clear
  "LocalDB instance does not exist" error, and the real `MSSQLLocalDB`
  instance stopped (`sqllocaldb stop`) auto-restarts and responds in
  ~1s (automatic instances start on connect, same as the docs describe).
  Also wired `MainWindow`'s constructor to catch a
  `DatabaseInitializer.InitializeAsync()` failure and show a `MessageBox`
  with a plain-English message instead of an unhandled-exception crash or
  a silent hang; confirmed end to end by launching the real exe with
  LocalDB stopped -- it reached an idle, near-zero-CPU state within
  ~8s (consistent with the ~1-5s connect failure above) instead of still
  spinning the way the original 25+s hang did.

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
- [x] Cloudinary account created, `CLOUDINARY_URL` set, upload leg
      verified against the real account (a throwaway script call to
      `UploadAsync` returned a real `res.cloudinary.com` URL). Still open:
      watching the full capture → upload → QR flow in the actual running
      app, not just a script.
- Remaining gap is the D3500 — plugged in and captured through, that's
  the one item still purely gated on hardware you provide; if it isn't
  ready, it slips to the Day 7 buffer.

**Day 5 — Real printer integration**
- `SpoolerPrinterService : IPrinterService` added in `Photobooth.Core`
  (`System.Drawing.Printing.PrintDocument`, via the `System.Drawing.Common`
  package -- Windows-only, marked `[SupportedOSPlatform("windows")]`,
  fine since the whole solution is already Windows-only). Scales the
  captured photo to fit the page margins and sends it through
  `PrintDocument.Print()`. Reads the target printer name from
  `PHOTOBOOTH_PRINTER_NAME` (same env-var pattern as `CLOUDINARY_URL`/
  `PHOTOBOOTH_DB_CONNECTION`), falling back to the Windows default
  printer if unset. Swapped in at the WPF composition root
  ([MainWindow.xaml.cs](Photobooth.UI/MainWindow.xaml.cs)) in place of
  `MockPrinterService`.
- No physical printer attached (same as the D3500), so verified against
  a stand-in the same way Day 2's camera work used a webcam: this dev
  machine has a driver-installed but currently-unplugged "Canon SELPHY
  CP1500" queue (`WorkOffline: True` -- matches the model
  `DatabaseInitializer` seeds), and Windows' print spooler accepts and
  queues a job for an offline printer without needing it physically
  present. Ran `SpoolerPrinterService.PrintAsync()` against it for real
  (via `PHOTOBOOTH_PRINTER_NAME=Canon SELPHY CP1500`) with a real
  captured image, confirmed `Get-PrintJob` showed a genuine spooled job
  (1 page, 31,496 bytes) afterward -- proof the call actually drove
  `StartDoc` -> draw -> `EndDoc` through the real Windows print pipeline,
  not just that the method returned without throwing. Cleaned the test
  job out of the queue afterward.
  Also tried "Microsoft Print to PDF" first as a stand-in (always
  installed, no hardware needed) -- that printer's port
  (`PORTPROMPT:`) opens an interactive Save-As dialog on every job with
  no way to suppress it from `PrintDocument` alone, which just hangs in
  a session with no interactive desktop. Not a bug in the new code; ruled
  it out as a verification method and used the offline Selphy queue
  instead, which doesn't have that problem.
- **Not yet verified: actual physical output** -- spooling successfully
  proves the code path is correct, but nothing has confirmed a real page
  comes out of a real DNP/Selphy/Epson dye-sub printer (color accuracy,
  paper size/margins, driver quirks). Needs the real printer connected.

**Day 6 — Vendo payment flow + admin dashboard**
- `IPaymentService` (`Photobooth.Core`) added with a `MockQrPaymentService`
  implementation — same interface-plus-mock seam as camera/printer/cloud
  upload/session repository. `GenerateQrCode()` builds a PNG synchronously
  (reuses `QrCodeGenerator`, no network call for a mock);
  `WaitForConfirmationAsync()` simulates guest scan-and-confirm time
  (2.5s delay) then reports success as `qr_gcash`. No real gateway this
  week — that's the "Cashless payments" roadmap item.
- `BoothState.Payment` added between `Reviewing` and `Printing`.
  `BoothStateMachine` now takes `IPaymentService` and a `mode` ("event" or
  "vendo", fixed per instance since one booth machine serves one
  location) as constructor arguments. Vendo mode runs the Payment state
  and records the real paid amount/method; event mode is unchanged
  (skips straight through, still recorded as a zero-amount `free_event`
  Payment row).
- `mode` is threaded from `DatabaseInitializer`'s seeded `Location.Type`
  (`SeedIds.LocationType`) through to `MainWindow`'s composition root,
  rather than hardcoded — so a vendo-mode deployment just needs the
  seeded Location's Type changed to `'vendo'`, no code change.
- WPF UI: new `PaymentView` screen shows the QR the guest scans, wired
  into `MainWindow`'s state-driven visibility switch the same way as the
  other screens.
- `AdminDashboardRepository` (`Photobooth.Data`) added — read-only
  queries for sessions today, revenue by mode (`Payment` joined to
  `Session`, `Status = 'paid'`), and low-inventory alerts (latest
  `InventoryLog` row per `PrinterId`+`ItemType` via `ROW_NUMBER()`, since
  a printer logs paper and ribbon independently). New
  `InventoryLogRepository` backs it; `DatabaseInitializer` now seeds one
  `InventoryLog` row (100 sheets of paper) alongside the existing
  Location/Printer/Booking seed so the dashboard shows something on
  first run.
- New `AdminWindow` (WPF) renders those three sections with a Refresh
  button. Reached from `MainWindow` via F12 (only while `Idle`, so it
  can't interrupt a guest session) rather than an on-screen button, so
  guests on a touchscreen kiosk can't stumble into it.
- Verified via `Photobooth.ConsoleDemo`: added a 4th (vendo-mode) session
  after the existing 3 event-mode ones — confirmed `[STATE] Payment`
  fires between `Reviewing` and `Printing` only for that session, and the
  final payment summary shows `session 4, ₱150.00, qr_gcash` alongside
  the two `free_event` rows from the event-mode sessions.
- Verified via `dotnet test`: **12 passed, 0 failed** — the 11 existing
  tests plus a new `RunSessionAsync_VendoMode_RunsPaymentBeforePrintingAndRecordsPaidAmount`
  asserting the state sequence, `PaymentQrPng` is set, and the recorded
  payment is `150m`/`qr_gcash` rather than `free_event`.
- **Update — SQL paths now verified against a real LocalDB instance**
  (see the Day 3 entry above for the LocalDB install and the `[Print]`
  reserved-keyword bug it uncovered). `AdminDashboardRepository`'s three
  queries and `SqlSessionRepository`'s vendo-mode payment recording were
  confirmed against a real database, not just mocks.
  **Still not exercised: `AdminWindow` and `PaymentView` rendering in the
  actual running UI** — the WPF exe was launched and confirmed to start
  cleanly against the real DB, but reaching the Payment screen needs a
  full guest session (camera hardware, not available) and reaching
  `AdminWindow` needs an interactive desktop to send F12 and see the
  result (this dev environment has no interactive desktop session to
  test that in). The underlying data layer both screens read from is
  confirmed correct; the XAML/bindings rendering it are not yet seen
  rendered.
- **Follow-up attempt at a full live session (camera bridge + Cloudinary +
  spooler together), same interactive-desktop wall hit again.** Started
  `Photobooth.CameraBridge.Host.exe --allow-webcam` and confirmed via the
  `STATUS` command that it connects to this machine's webcam stand-in
  (`CONNECTED USB2.0 HD UVC WebCam`) -- the pipe protocol and camera
  detection both work correctly on their own (worth noting: the bridge's
  own startup log prints "no camera detected" for a split second before
  the async `CameraConnected` event finishes registering the device --
  cosmetic race in the log line, not a real failure, since `STATUS`
  moments later correctly reports connected). Launched `Photobooth.UI.exe`
  with `CLOUDINARY_URL` set in its process environment on top of that: the
  process starts and stays responsive, WPF's internal plumbing windows
  appear (`SystemResourceNotifyWindow`, `MediaContextNotificationWindow`,
  the IME window), but `MainWindow` itself never gets a window handle --
  confirmed with a control test that even a plain `notepad.exe` launched
  the same way produces no visible window here either. So this isn't a
  Photobooth bug: whatever runs this session can capture the live desktop
  (screenshotting works, via a raw screen copy) but can't create a window
  that desktop's window station will recognize, so no GUI app can be
  driven end-to-end from here, not just this one's `AdminWindow`. Confirms
  the same wall the persistence-layer work hit, from a different angle.
  Killed both processes afterward rather than leave them orphaned. The
  Cloudinary upload leg itself was already verified separately (see Day
  2's update above) without needing a rendered window, since that's a
  plain library call -- it's specifically the "watch the QR panel render
  on screen" half of that verification that still needs you to run the
  app yourself on a real interactive session.

**Extra, not in the original plan — offline upload queueing**
- With the D3500, physical printer, and interactive-desktop GUI testing
  all blocked on things only you can provide, picked up the "Offline
  queueing" item from the Future roadmap below instead -- fully
  code/test-verifiable without hardware or a rendered window.
- `IPendingUploadQueue` added to `Photobooth.Core` (same interface-plus-
  mock seam as everything else): `FileSystemPendingUploadQueue` persists
  the backlog to a small JSON file so it survives an app restart, not
  just a network blip that resolves before the next retry;
  `MockPendingUploadQueue` is the in-memory stand-in for tests/demo.
- `BoothStateMachine.UploadInBackgroundAsync` now queues the file instead
  of silently losing it when `ICloudUploadService.UploadAsync` throws.
  New `RetryQueuedUploadsAsync()` retries everything still queued,
  removing only the ones that actually succeed. Called two ways: fire-
  and-forget at the start of every `RunSessionAsync` (so a backlog
  flushes as soon as the next guest walks up, no dedicated timer needed)
  and once at `MainWindow` startup (so a backlog from last night doesn't
  sit unflushed all day waiting for a guest). Deliberately doesn't
  re-fire `PhotoUploaded`/update `LastPhotoUrl` on a successful retry --
  that guest is long gone by the time a queued upload finally lands, so
  there's no one to show a QR code to; the point is just not losing the
  hosted copy.
- Added `MockCloudUploadService.FailNextUpload` (same pattern as
  `MockCameraService.FailNextCapture`) so the failure path is
  deterministically testable rather than relying on a real dropped
  connection.
- Verified via `Photobooth.Tests`: 11 new tests (`BoothStateMachineTests`
  covers queue-on-failure and both retry outcomes; `PendingUploadQueueTests`
  covers both the mock and the real file-backed queue, including a
  same-file-path-twice dedupe check and a **queue surviving a fresh
  instance** test standing in for an app restart). `dotnet test` — 23
  passed, 0 failed.
- Verified via `Photobooth.ConsoleDemo`: session 3's upload is forced to
  fail (simulating dropped WiFi) -- no `[UPLOADED]` line for it, and
  "Pending uploads after session 3: 1" confirms it queued instead of
  vanishing. Session 4 (a different `BoothStateMachine` instance, vendo
  mode, sharing the same queue) starts and its opportunistic retry
  drains the backlog in the background -- "Pending uploads after session
  4: 0" confirms the queued file got uploaded without anyone asking it to
  directly.
- Wired into `MainWindow`'s composition root
  (`new FileSystemPendingUploadQueue()`, plus the startup retry call) --
  not yet run against the actual live app for the same interactive-
  desktop reason noted just above, but the full solution builds clean
  with this wiring in place.

**Extra, not in the original plan — liability disclaimer + email opt-in**
- Picked up the "Surveys & data collection" roadmap item next (liability
  disclaimer + email opt-in specifically -- general feedback surveys
  stayed out of scope, still unprioritized on the roadmap below).
- New `Consent` table in `schema.sql` (`DisclaimerAccepted`, `EmailOptIn`,
  `Email`, one row per session) plus `IX_Consent_Session`.
  `DatabaseInitializer.EnsureSchemaAsync` got a small top-up path for
  this: its existing check only looks for the `Location` table, so an
  already-seeded LocalDB (like this dev machine's) would never pick up a
  table added after that check was written. Not a real migration system
  -- just a second, narrower existence check that creates `Consent` on
  its own if it's the only thing missing, so this machine's existing DB
  (and yours, once you pull this) doesn't need a manual `DROP DATABASE`.
- `IConsentService` added to `Photobooth.Core` (same interface-plus-mock
  seam as everything else) -- `CollectAsync()` returns a `ConsentResult`
  (accepted/declined, email opt-in, optional email). `MockConsentService`
  simulates a guest reading and tapping through; `DeclineNext` (same
  reset-after-firing pattern as `MockCameraService.FailNextCapture`)
  exercises the decline path deterministically. No real interactive
  button-driven capture yet -- same "mock only, real integration is
  future work" status `IPaymentService` already has.
- New `BoothState.Consent`, shown first thing every session (before
  Countdown). `BoothStateMachine` takes `IConsentService` as a new
  constructor argument; the outcome is always recorded via the new
  `ISessionRepository.RecordConsentAsync`, whether accepted or declined.
  A decline skips straight to `Idle` -- no countdown, capture, or print
  -- and the session is marked via the new `AbandonAsync` (a new
  `Session.Status = 'abandoned'`, distinct from `FailAsync`'s `'error'`,
  using a status the schema's `CHECK` constraint already allowed but
  nothing had ever actually set until now). `SqlSessionRepository` and
  `MockSessionRepository` both implement the two new interface methods.
- New `ConsentView` in `MainWindow.xaml`, wired into the state-driven
  visibility switch the same way as the other screens -- not yet seen
  rendered, same interactive-desktop gap noted above.
- Verified via `Photobooth.Tests`: 5 new tests (`BoothStateMachineTests`
  covers the decline path end to end -- states sequence is just
  `Consent -> Idle`, `AbandonedSessionIds` gets the session, no
  print/payment recorded, `DeclineNext` resets itself; `MockConsentServiceTests`
  covers the default-accept, decline-then-reset, and email-opt-in-off
  cases; a `MockCloudUploadService.FailNextUpload` reset test rode along
  too). `dotnet test` -- **28 passed, 0 failed**.
- Verified via `Photobooth.ConsoleDemo`: session 4 (event mode, guest
  declines) shows exactly `[STATE] Consent` then `[STATE] Idle` -- no
  Countdown/Capturing/Printing lines at all -- and the final summary
  correctly reports "1 abandoned" alongside "5 consent records" across
  all 5 sessions.
- Wired into `MainWindow`'s composition root (`new MockConsentService()`)
  -- not yet run against the actual live app, same interactive-desktop
  gap as everything else in this section.

**Extra, not in the original plan — second payment gateway (card reader)**
- Picked up the "Cashless payments" roadmap item next: a second
  `IPaymentService` implementation (`MockCardReaderPaymentService`,
  tap/insert/swipe) alongside the existing QR-only `MockQrPaymentService`.
- Real finding along the way: the original interface's
  `GenerateQrCode(amount, reference) -> byte[]` couldn't represent a card
  reader at all -- there's no QR code to generate for a tap-to-pay flow.
  Redesigned it to `Initiate(amount, reference) -> PaymentPrompt`, where
  `PaymentPrompt` carries `Instructions` (gateway-specific guest-facing
  text) and a nullable `QrCodePng` (null for card reader, present for
  QR). This is exactly the kind of thing a single implementation can't
  surface -- the interface only turned out to be QR-shaped once a second,
  genuinely different gateway tried to fit through it.
  `BoothStateMachine` gained a matching `PaymentInstructions` property
  alongside the existing `PaymentQrPng`.
- `MockCardReaderPaymentService.DeclineNext` (same reset-after-firing
  pattern as `MockCameraService.FailNextCapture`) is the first payment
  mock able to simulate a decline at all -- `MockQrPaymentService`
  always succeeded, so `BoothStateMachine`'s
  `if (!result.Success) throw ...` payment-declined branch had never
  actually been exercised by anything in this codebase until now.
- `MainWindow`'s `PaymentView` updated to be gateway-agnostic: the title
  is generic ("Complete your payment"), the subtitle now comes from
  `PaymentInstructions` at runtime instead of a hardcoded "Scan to pay",
  and the QR `Border` collapses when there's nothing to show. Still
  wired to `MockQrPaymentService` in the real composition root -- GCash/
  Maya QR stays the realistic near-term choice for this business, the
  card reader is groundwork for later, not a replacement.
- Verified via `Photobooth.Tests`: 7 new tests (`PaymentServiceTests.cs`
  covers both mocks directly -- QR still returns a real PNG and
  `qr_gcash`, card reader returns no QR and `card`, decline-then-reset;
  `BoothStateMachineTests` adds the card-reader-in-vendo-mode case
  proving `PaymentQrPng` stays null end to end, plus the
  previously-impossible-to-trigger payment-declined case: `Error` state,
  `FailedSessionIds`, no print/payment recorded). `dotnet test` --
  **35 passed, 0 failed**.
- Verified via `Photobooth.ConsoleDemo`: session 6 (card reader) shows
  `Payment prompt shown: "Tap, insert, or swipe your card to pay." (QR
  code: none)` and records a `card` payment; session 7 (card declined)
  shows `[ERROR] Payment was not completed.` -> `[STATE] Error` --
  the dead branch, alive for the first time. Final summary: "7 (4
  completed, 2 failed, 1 abandoned)".

**Extra, not in the original plan — email delivery on opt-in**
- Picked up the email half of "Instant digital sharing" next -- Consent
  already captured `EmailOptIn`/`Email` per session, but nothing ever
  read it back. That's a real, pre-existing gap: data collected and then
  never used for anything.
- `IEmailDeliveryService` added to `Photobooth.Core` (same
  interface-plus-mock seam as everything else) --
  `SendPhotoLinkAsync(toEmail, photoUrl)`. `MockEmailDeliveryService`
  records what it "sent" for tests/demo to assert against. No real SMTP
  delivery yet -- that needs real mail credentials, same "mock only,
  real integration is future work" status `IPaymentService` and
  `IConsentService` already have, so this stays proportionate to how
  those were scoped rather than standing up a whole real mail sender
  nobody asked for yet.
- `BoothStateMachine` sends the email once the guest has earned their
  photo, only if `LastConsent is { EmailOptIn: true, Email: not null }`.
  (Originally wired to fire right after the upload finished -- see the
  gap noted below, fixed in the next entry.) A failed send is swallowed
  (best-effort) -- the guest already has the QR code as a working
  fallback, so an email hiccup shouldn't become a guest-facing error.
- **Known gap, not fixed now:** `RetryQueuedUploadsAsync` (the offline
  upload queue from earlier) only knows a file path, not which guest or
  email it belonged to, so a session whose upload initially failed and
  later succeeds via retry never gets its email sent, even if that guest
  opted in. Fixing this means threading consent info through
  `IPendingUploadQueue`'s shape, which felt like more surface area than
  this feature needed to claim -- documented instead of silently
  papered over, same as `AdminWindow`'s untested rendering above.
- Verified via `Photobooth.Tests`: 2 new tests (`RunSessionAsync_EmailOptInFalse_NoEmailSent`
  confirms no send when the guest didn't opt in; `MockEmailDeliveryServiceTests`
  covers the mock directly), plus the existing happy-path test extended
  to assert an email actually got "sent" to the right address with the
  right URL. `dotnet test` -- **37 passed, 0 failed**.
- Verified via `Photobooth.ConsoleDemo`: every successful session (guest
  opts in by default in the mock) now logs a `[EMAILED] guest@example.com
  -> https://...` line right after `[UPLOADED]`; the final summary counts
  emails sent alongside sessions/prints/payments/consents.
- **Second known gap, found via the demo -- fixed in the entry right
  below:** session 7 (the declined-card-payment session) still got
  emailed -- `[EMAILED]` fired right after `[UPLOADED]`, well before
  `[ERROR] Payment was not completed.` showed up. Upload (and email)
  ran in the background as soon as capture finished, independent of
  whatever the Payment state later decided. Net effect: a guest whose
  vendo payment got declined still received a free digital copy by
  email, undermining the pay-to-get-your-photo model.
- Wired into `MainWindow`'s composition root (`new
  MockEmailDeliveryService()`) -- same "mock in production for now"
  status as `MockQrPaymentService`.

**Extra, not in the original plan — fixed the payment-declined email gap**
- The gap above was real, not theoretical, and directly affected
  revenue integrity for vendo mode, so fixed it immediately rather than
  letting it ride as a known issue.
- Reproduced first: extended `RunSessionAsync_VendoPaymentDeclined_RecordsFailureAndSkipsPrint`
  with `Assert.Empty(email.SentEmails)` and confirmed it actually failed
  before touching any production code (`Collection: [Tuple
  ("guest@example.com", ...mock_0001.bmp)]`) -- proof this was a real
  bug, not a hypothetical one.
- Fix: `UploadInBackgroundAsync` no longer sends email itself -- it only
  uploads and queues on failure, same as before this feature existed.
  `RunSessionAsync` now captures the upload's `Task` instead of firing
  it fully fire-and-forget, and triggers a new `EmailIfOptedInAsync(
  uploadTask, ct)` (also fire-and-forget, so it still can't block
  Printing) right after the vendo payment gate clears -- meaning event
  mode (free by design) and a *successful* vendo payment both reach it,
  but a declined payment's `throw` jumps straight to the `catch` block
  and never does. `EmailIfOptedInAsync` awaits the upload task first
  (which never throws -- `UploadInBackgroundAsync` catches its own
  failures) so it always has the final `LastPhotoUrl`/`LastConsent`
  state before deciding whether to send.
- Deliberately gated on "payment cleared," not "print succeeded" --
  a vendo guest who paid but hits a printer jam afterward should still
  get their digital copy; only a guest who never paid shouldn't. The
  gate sits right after the payment block, before `Printing`.
- Verified via `Photobooth.Tests`: the extended vendo-decline test now
  passes. `dotnet test` -- **37 passed, 0 failed** (same count as
  before -- this was a fix, not new coverage beyond that one assertion).
- Verified via `Photobooth.ConsoleDemo`: session 7 (card declined) no
  longer logs `[EMAILED]` at all now; every other session's email
  behavior is unchanged.

**Extra, not in the original plan — digital branding overlay**
- Picked up "Digital overlays & stickers" next: every commercial booth
  (LumaBooth, dslrBooth) stamps a studio name/date caption onto photos
  before delivery, and this was fully code/test-verifiable without
  hardware or a rendered window.
- `IPhotoBrandingService` added to `Photobooth.Core` -- same
  interface-plus-mock seam as camera/printer, but for a different reason
  than usual: there's no hardware or network to fake here, the seam
  exists purely to keep `Photobooth.Tests`/`Photobooth.ConsoleDemo`
  decoupled from `System.Drawing.Common` (Windows-only), the same
  reasoning `PlaceholderImage`'s own doc comment already gives for
  hand-rolling BMP bytes instead of using it.
  (Side note, found while reading that file: `PlaceholderImage`'s
  comment claims writing raw BMP bytes "keeps this project dependency
  free" -- no longer true since `SpoolerPrinterService` added
  `System.Drawing.Common` to `Photobooth.Core.csproj` back on Day 5.
  Stale comment, not touched, since fixing it wasn't part of this task.)
- `GdiPhotoBrandingService` (real, `[SupportedOSPlatform("windows")]`,
  same pattern as `SpoolerPrinterService`) composites a black caption
  bar onto the bottom of the photo via GDI+ (`Focus & Snap | <date>`),
  saved as a new `_branded.jpg` file -- the original capture is left
  untouched. `MockPhotoBrandingService` just copies the file with a
  `_branded` suffix, no GDI+, for tests/demo.
- Confirmed the interface indirection actually avoids the platform-
  compat warning it's there for: calling `GdiPhotoBrandingService`
  directly from an unmarked method would trip CA1416 (calling
  Windows-only code from code that isn't marked as Windows-only), but
  `BoothStateMachine` only ever holds the unmarked `IPhotoBrandingService`
  interface, same as it does for `IPrinterService` -- `dotnet build`
  stayed at 0 warnings after wiring this in.
  `Photobooth.Tests` needed one exception: `GdiPhotoBrandingServiceTests`
  tests the real GDI+ implementation directly (to prove actual
  compositing works, not just that a mock returns a plausible path), so
  that one test class is marked `[SupportedOSPlatform("windows")]` too --
  honest since the whole solution only ever runs on the Windows booth
  machine anyway.
- `BoothStateMachine` applies branding right after capture, before
  anything else reads `LastCapturedImagePath` -- so Reviewing, the
  print, and the upload all see the same branded photo, not three
  different versions depending on which step happened to run first.
- Verified via `Photobooth.Tests`: 3 new tests. `GdiPhotoBrandingServiceTests`
  runs the real compositing against a real `MockCameraService`-captured
  BMP and confirms the output is a genuine JPEG (`0xFF 0xD8` header) that's
  taller than the original (the caption bar) with the same width;
  `MockPhotoBrandingServiceTests` confirms the mock's path handling; the
  happy-path test extended to assert `LastCapturedImagePath` contains
  `_branded` by the time the session ends. `dotnet test` -- **40 passed,
  0 failed**.
- Verified via `Photobooth.ConsoleDemo`: every session's captured/
  uploaded/printed file path now carries the `_branded` suffix.
- Wired into `MainWindow`'s composition root (`new
  GdiPhotoBrandingService()`) -- the real implementation, not a mock,
  since compositing needs no external credentials or hardware to work
  right now, unlike the payment gateway or email delivery.
- **Worth noting, not acted on:** `BoothStateMachine`'s constructor is
  now 9 positional service parameters plus `mode` -- this is the fourth
  seam added on top of the original camera/printer/cloud-upload/session/
  payment set, and each addition has made the parameter list longer
  without anyone stopping to ask whether it should become a bundled
  options object instead. Flagging it here rather than refactoring it
  unprompted, since every call site (`MainWindow`, `ConsoleDemo`, every
  test) would need touching either way and nothing about the current
  shape is actually broken.

**Extra, not in the original plan — fixed a flaky test-suite race**
- Re-running the full suite right after the branding work landed
  produced an intermittent failure that hadn't shown up before:
  `MockCameraServiceTests.CaptureAsync_IncrementsFrameNumberAcrossCalls`
  failed with `IOException: The process cannot access the file
  '...\captures\mock_0001.bmp' because it is being used by another
  process.` A second run passed clean -- classic race, not a real
  regression in the new code.
- Root cause: `MockCameraService` always starts numbering at
  `mock_0001.bmp` from a fresh instance, and xunit runs test classes in
  parallel by default. Every test class that constructs its own
  `MockCameraService()` (there are many, across `BoothStateMachineTests`,
  `MockServicesTests`, and the two new branding test files) was already
  racing on that exact same relative path -- this bug predates today's
  work, adding two more test classes that call `CaptureAsync()` just
  raised the collision odds enough to actually hit it.
- Fixed by giving each `MockCameraService` instance a random 8-character
  suffix, appended after the frame number so existing
  `Assert.Contains("mock_0001", ...)`-style checks still match (e.g.
  `mock_0001_a1b2c3d4.bmp`). Fixes the actual isolation bug rather than
  disabling test parallelism, which would have papered over it at the
  cost of a much slower suite (many of these tests run real multi-second
  `Task.Delay` sequences that only finish quickly because they overlap
  across parallel test classes).
- Verified by running `dotnet test` twice in a row after the fix --
  **40 passed, 0 failed** both times, where before the fix a rerun had
  already reproduced the race once.

**Extra, not in the original plan — bundled BoothStateMachine's services**
- Acted on the parameter-list concern flagged twice already: the
  constructor had grown to 9 positional service parameters plus `mode`
  as each new seam (offline queue, consent, email, branding) got added
  on top of the original camera/printer/cloud-upload/session/payment
  five. Bundled them into a new `BoothServices` record instead of adding
  a 10th parameter to the next feature.
- Pure refactor, no behavior change: `BoothStateMachine` now takes
  `(BoothServices services, string mode = "event")`, with every `_camera`/
  `_printer`/etc. field access rewritten to `_services.Camera`/
  `_services.Printer`/etc.
- Updated every call site: `MainWindow`'s composition root (named
  arguments, since that's the one place readability at the construction
  site matters most), `Photobooth.ConsoleDemo` (reuses one `services`
  value across the event/vendo/card-reader machines, swapping just the
  gateway via `services with { Payment = cardPayment }` for the card
  demo -- a `with` expression is one of the concrete reasons a record
  was the right shape here, not a class), and every constructor call in
  `Photobooth.Tests`.
- Verified via `Photobooth.Tests` -- ran twice in a row post-refactor,
  **40 passed, 0 failed** both times (same count as before the refactor,
  confirming nothing's behavior actually changed). Couldn't rebuild
  `Photobooth.UI` itself to double-check at the full-solution level: its
  own `Photobooth.UI.exe` was running at the time (PID 21060, presumably
  you testing the live app after the recent `MainWindow.xaml.cs` changes
  -- the file lock on `Photobooth.Core.dll`/`Photobooth.Data.dll` in its
  output folder is exactly what a running instance holds). Built
  `Photobooth.Core`, `Photobooth.Tests`, and `Photobooth.ConsoleDemo`
  individually instead (all clean, 0 warnings) rather than kill a
  process that might be your active session.
- Follow-up once `Photobooth.UI.exe` exited on its own: ran a full
  `dotnet build Photobooth.sln` and it came back clean (0 warnings, 0
  errors, `Photobooth.UI` included) -- confirms the refactor is correct
  end to end, not just in the projects that happened to be free to
  rebuild at the time.

**Extra, not in the original plan — Glam Booth mode (B&W filter half)**
- Picked up "Glam Booth mode" next, scoped to just the high-contrast
  black & white filter -- the roadmap's other half, automated skin
  smoothing, needs face detection and is genuinely separate, unbuilt
  work, not something to fold in here.
- `IPhotoFilterService`/`GdiPhotoFilterService` added to `Photobooth.Core`,
  same pattern as `IPhotoBrandingService` (interface-plus-mock purely to
  keep `System.Drawing.Common` out of `Photobooth.Tests`/
  `Photobooth.ConsoleDemo`, not because there's hardware/network to
  fake). Real implementation runs two GDI+ `ColorMatrix` passes: a
  standard luminance-weighted grayscale conversion, then a contrast
  boost (values pushed away from mid-gray) so it reads as "glam,"
  not just desaturated. Extracted the `LoadIndependentCopy` file-loading
  helper both GDI+ services need into a small shared `GdiImageHelpers`
  class rather than duplicating it a second time.
- This is exactly the case the `BoothServices` refactor from the
  previous entry was for: adding a 10th seam meant one new property on
  `BoothServices` plus updating each `new BoothServices(...)` call site
  -- `BoothStateMachine`'s own constructor call sites (`new
  BoothStateMachine(services, mode: ...)`) didn't need to change at all,
  since the new `applyGlamFilter` parameter has a default.
- `applyGlamFilter` is a per-booth setting like `mode`, not a per-guest
  UI choice (there's no UI to pick it yet) -- defaults to `false`
  everywhere, including `MainWindow`'s real composition root, so
  wiring the real `GdiPhotoFilterService` in doesn't change any existing
  deployment's behavior. Filter runs before branding when enabled, so
  the caption bar's white-on-black styling stays independent of whatever
  the photo's colors are.
- Verified via `Photobooth.Tests`: 4 new tests. `GdiPhotoFilterServiceTests`
  runs the real GDI+ path against a real `MockCameraService`-captured
  BMP, confirms a genuine JPEG comes out matching the original's
  dimensions, and samples pixels across the image to confirm they're
  actually grayscale (R == G == B) -- proof the color matrix math is
  correct, not just that a file got written; `MockPhotoFilterServiceTests`
  covers the mock; `BoothStateMachineTests` adds a Glam-mode-enabled
  case confirming `_glam` appears before `_branded` in the final
  filename (proves ordering, not just that both ran) and extends the
  happy-path test to confirm `_glam` is *absent* when the flag is off
  (the default). `dotnet test` -- **44 passed, 0 failed**, run twice in a
  row clean (still watching for the parallel-test race from the previous
  entry, since this added yet more `MockCameraService` usage).
- Verified via `Photobooth.ConsoleDemo`: a new session 8 (event mode,
  `applyGlamFilter: true`) shows a final photo path containing both
  `_glam` and `_branded` in the right order.

**Extra, not in the original plan — closed the retry-queue email gap**
- Closed the gap flagged twice already: `RetryQueuedUploadsAsync` only
  knew a file path, not which guest/email a queued upload belonged to,
  so a session whose upload initially failed and later succeeded via
  retry never got its email sent, even if that guest opted in.
- `IPendingUploadQueue` now carries a `PendingUpload(FilePath, Email)`
  record instead of a bare `string` -- `EnqueueAsync(filePath, email,
  ct)` takes the email explicitly (required, not defaulted, so a future
  call site has to consciously decide there's none rather than
  forgetting the parameter), `GetPendingAsync()` returns
  `IReadOnlyList<PendingUpload>`. Both `FileSystemPendingUploadQueue`
  (JSON shape changed accordingly) and `MockPendingUploadQueue` updated;
  `RemoveAsync` still keys on file path alone.
- This forced a more careful fix than "just add an email parameter,"
  though: the original code queued a failed upload immediately, from
  inside the fire-and-forget upload task itself, which runs concurrently
  with (and can finish before) the vendo payment step resolves. Naively
  attaching the guest's email at that same enqueue point would have
  reintroduced the exact payment-declined-still-gets-emailed bug fixed
  two entries ago, just via the retry path instead of the direct one.
  Fixed by removing the enqueue-on-failure logic from
  `UploadInBackgroundAsync` entirely and merging it into
  `FinalizeUploadAsync` (renamed from `EmailIfOptedInAsync`) -- the same
  method already gated on "payment cleared" now decides between sending
  the email now (upload succeeded) or queuing the file with that same
  email (upload failed), so a declined payment's `throw` skips both
  outcomes identically, not just the email one.
  `RetryQueuedUploadsAsync` sends the email itself once a retried upload
  actually succeeds.
- Verified via `Photobooth.Tests`: 3 new tests plus 2 renamed/extended
  ones. `RunSessionAsync_UploadFails_QueuesFileWithEmailInsteadOfLosingIt`
  confirms the queued entry carries the guest's email; the new
  `RunSessionAsync_VendoPaymentDeclinedAndUploadFails_QueuesFileWithoutEmail`
  confirms a declined vendo payment with a *simultaneously* failing
  upload queues nothing at all (guards specifically against the
  reintroduction risk above); `RetryQueuedUploadsAsync_UploadNowSucceeds_RemovesFileFromQueueAndEmailsTheGuest`
  is the actual fix verification -- a queued entry with an email now
  gets that email sent once the retry succeeds; a sibling test confirms
  a queued entry with no email sends none. `PendingUploadQueueTests`
  updated for the new shape plus a null-email case.
- Verified via `Photobooth.ConsoleDemo` -- and this is where a second,
  real bug turned up (see next entry).

**Extra, not in the original plan — fixed a duplicate-email race the demo caught**
- Running the demo to verify the fix above showed `mock_0002` emailed
  *twice*: `[EMAILED] guest@example.com -> ...mock_0002...` appeared back
  to back. Not a demo glitch -- a real race in the new retry-with-email
  code, caught by actually running it rather than trusting the tests alone.
- Root cause: `RetryQueuedUploadsAsync` fires fire-and-forget at the
  start of every session and can keep running after a short session
  (e.g. a quick Consent decline) already returned. Session 4's decline
  fired a retry that was still mid-upload when session 5 (a *different*
  `BoothStateMachine` instance -- `Photobooth.ConsoleDemo` runs one
  instance per gateway/mode combination, all sharing one
  `BoothServices.UploadQueue`) fired its own retry moments later. Both
  read the same pending item via `GetPendingAsync` before either had
  called `RemoveAsync`, so both uploaded it and both emailed the guest.
- First attempt, corrected before shipping: a `SemaphoreSlim` on
  `BoothStateMachine` itself serializing overlapping calls on *that
  instance*. Reran the demo expecting it fixed -- it wasn't. Realized
  why before writing this up: session 4 and session 5 are different
  `BoothStateMachine` instances, each with its own semaphore, so nothing
  serialized them against each other. A per-instance lock can't protect
  a resource two instances both hold a reference to.
- Real fix: moved the atomicity to the shared resource. New
  `IPendingUploadQueue.DequeueAllAsync()` atomically returns everything
  queued and empties the queue in one locked step (added a lock to
  `MockPendingUploadQueue`, which had none before -- a plain `List<T>` is
  not safe for concurrent access, and this method makes that a real
  question for the first time; `FileSystemPendingUploadQueue` already had
  one for its file I/O). Whichever caller calls it first gets the whole
  backlog; a second overlapping caller -- same instance or a different
  one -- gets nothing left to process. `RetryQueuedUploadsAsync` now
  claims via this instead of `GetPendingAsync`+`RemoveAsync`, re-enqueuing
  (via the existing `EnqueueAsync`) any claimed item whose retry still
  fails, so a still-offline venue doesn't lose the backlog.
- Verified via `Photobooth.Tests`: 4 new tests. Two regression tests
  reproduce the actual failure mode -- one two overlapping calls on the
  *same* instance, one on *two different* instances sharing a queue
  (the demo's real shape) -- both assert exactly one email got sent, not
  two. Two more cover `DequeueAllAsync` directly (returns and empties
  everything; two concurrent callers split the claim, never double it).
  `dotnet test` -- **52 passed, 0 failed**, run twice in a row clean.
- Verified via `Photobooth.ConsoleDemo`: reran after the real fix and
  `mock_0002` (or whichever session's file was queued) is now emailed
  exactly once.

**Extra, not in the original plan — admin settings screen (countdown + Glam Booth toggle)**
- You asked directly whether there's an admin area to edit the countdown,
  toggle filters/effects, or manage frames -- there wasn't; `AdminWindow`
  was entirely read-only (sessions today, revenue, low-inventory alerts).
  Picked "admin settings screen" as the first piece, scoped to the two
  things that already existed as code-level knobs: countdown duration and
  the Glam Booth filter. Frame management and a guest-facing frame picker
  are separate, larger pieces not started here.
- New `Location.CountdownSeconds`/`Location.GlamFilterEnabled` columns
  (`schema.sql`, defaults 3 and 0) -- one booth machine has one location,
  so booth-wide settings live there rather than a new table, same
  reasoning `Location.Type` (event/vendo) already established.
  `DatabaseInitializer` got another top-up migration
  (`EnsureBoothSettingsColumnsAsync`, same pattern as the `Consent` table's
  before it) so an already-seeded LocalDB -- like this dev machine's --
  picks up the new columns via `ALTER TABLE` without a manual reset.
- `IBoothSettingsProvider`/`BoothSettings` added to `Photobooth.Core` --
  same interface-plus-mock seam as everything else. Real
  `SqlBoothSettingsProvider` (`Photobooth.Data`) reads the Location row
  fresh on every call, deliberately uncached -- `BoothStateMachine` now
  calls `GetSettingsAsync()` at the start of every `RunSessionAsync`
  instead of once at construction, so an admin's save takes effect for
  the *very next guest*, not the app's next restart.
- This retired the `applyGlamFilter` constructor flag added two entries
  ago -- having both a static deploy-time flag and a dynamic per-session
  setting controlling the same behavior would've been confusing, so the
  flag is gone and `BoothServices.Settings` is the only way to control it
  now. `BoothServices` picked up its 11th property for this
  (`Filter`/`Settings` were both additions in consecutive entries) --
  each addition stays cheap specifically because of the record-bundle
  refactor a few entries back: one new property, no `BoothStateMachine`
  constructor signature change.
- `AdminWindow` gained a "Booth settings" section: a countdown-seconds
  text box, a Glam Booth checkbox, and a Save button
  (`LocationRepository.UpdateSettingsAsync`) with basic validation (must
  parse as a whole number > 0). Not yet seen rendered, same
  interactive-desktop gap as `AdminWindow`'s existing dashboard section
  and `ConsentView`/`PaymentView` before it.
- Verified via `Photobooth.Tests`: the Glam-mode test now enables it
  through `MockBoothSettingsProvider` instead of the removed constructor
  flag, plus a new test confirms a custom `CountdownSeconds` (5 instead of
  the default 3) actually changes how many `CountdownTick` events fire --
  proof `BoothStateMachine` reads the value rather than a hardcoded
  constant. `dotnet test` -- 53 passed, 0 failed.
- Verified the real SQL path directly, since `Photobooth.Tests` doesn't
  cover SQL-backed code (same established gap as `SqlSessionRepository`):
  a throwaway script (not checked in) ran `DatabaseInitializer
  .InitializeAsync()` against this machine's real, already-seeded
  LocalDB and confirmed the `ALTER TABLE` migration applied cleanly
  (`CountdownSeconds=3, GlamFilterEnabled=False` read back correctly on a
  database that predates those columns), then wrote `(7, true)` via
  `LocationRepository.UpdateSettingsAsync` and read it straight back
  through `SqlBoothSettingsProvider`, confirming the pair round-trips
  correctly -- then restored the default so the shared dev database
  didn't end up in a surprising state afterward.
- Verified via `Photobooth.ConsoleDemo`: session 8 now flips
  `MockBoothSettingsProvider.Settings` mid-run (simulating an admin
  saving new settings) on the *same* `eventMachine` instance already used
  for sessions 1-4, then re-runs it -- proof the change takes effect on
  the next session without constructing a new `BoothStateMachine` or
  restarting anything, which is the entire point of reading settings
  fresh per-session instead of once at startup.

**Extra, not in the original plan — frame library + guest-facing frame picker**
- Picked this up next: the two pieces explicitly flagged as "not started"
  the last time this project's status was reviewed (admin-managed frame
  overlays and the guest picker screen). Both fully code/test-verifiable
  without hardware; the guest-facing screen is the first genuinely real
  (not mocked) interactive UI seam in the app -- see below.
- New `Frame` table (`Name`, `ImagePath`, `SortOrder`, `IsActive`),
  scoped to a `Location` the same way `Printer`/`Booking` are.
  `DatabaseInitializer` got another top-up migration
  (`EnsureFrameTableAsync`, same pattern as `Consent`'s/the booth-settings
  columns' before it). Starts empty on any database (fresh or
  already-seeded), so `FramePicker` is skipped entirely until an admin
  adds a frame -- zero behavior change for every existing deployment/test.
- Three new `Photobooth.Core` seams, same interface-plus-mock pattern as
  everything else: `IFrameLibraryService` (reads active frames, real impl
  `SqlFrameLibraryService` in `Photobooth.Data`, read fresh every session
  like `IBoothSettingsProvider`), `IFrameOverlayService` (GDI+ compositing
  via `GdiFrameOverlayService`, same Windows-only pattern as
  `IPhotoBrandingService`/`IPhotoFilterService` -- frame PNG stretched to
  the photo's exact dimensions so it lines up regardless of the asset's
  native resolution), and `IFrameSelectionService` (collects the guest's
  pick).
- `IFrameSelectionService` is the interesting one: unlike
  `IConsentService`/`IPaymentService` (still mock-only -- a real
  disclaimer/gateway needs external integration this project hasn't done),
  a frame pick is just a button tap with no hardware or network dependency
  to stand up. Built a real implementation, `UiFrameSelectionService` -- a
  `TaskCompletionSource` bridge that raises `SelectionRequested` (the UI
  shows the offered thumbnails) and completes once `MainWindow` calls
  `SubmitSelection` in response to a tap. `MockFrameSelectionService`
  (defaults to picking the first option; `SkipNext` simulates "no frame")
  is what `Photobooth.Tests`/`Photobooth.ConsoleDemo` use instead.
- `BoothStateMachine` reads the active frame list right after `Reviewing`
  (guest has seen the raw shot first). If any exist, shows `FramePicker`,
  awaits the pick, and applies it (if any) *before* the upload task starts
  and before `Printing` -- moved the upload kickoff later than it used to
  fire (previously right after branding, overlapping with the Reviewing
  delay) specifically so the QR code and the physical print show the same
  final composited photo, same invariant branding/filter ordering already
  established. If nothing's configured, `FramePicker` never shows and
  upload timing is unchanged from before this feature.
- `BoothServices` picked up its 12th-14th properties for this
  (`FrameLibrary`/`FrameSelection`/`FrameOverlay`) -- same "one new
  property per seam, no constructor signature change" cost the record
  refactor was built for, though the parameter list is now long enough
  that a future feature might be the one to finally split it.
- WPF: `MainWindow` gained a real `FramePickerView` -- thumbnails built
  from actual frame image files (not placeholders), each a clickable
  `Button` wired to `UiFrameSelectionService.SubmitSelection`, plus a "No
  frame" button. `AdminWindow` gained a "Frame library" section: an
  `ItemsControl` listing frames with an Active checkbox and Delete button
  per row, plus an "Add Frame" form (name + `OpenFileDialog` image picker)
  that copies the chosen file into a local `Assets/Frames/` folder and
  inserts the row.
- Verified via `Photobooth.Tests`: 12 new tests. Three
  `BoothStateMachineTests` cases cover frame chosen (states show
  `FramePicker` between `Reviewing` and `Printing`, the framed path -- not
  the pre-frame one -- is what gets recorded as the print and appears in
  the uploaded URL), guest skips (state still shows, nothing applied), and
  no active frames configured (state never shows at all, matching every
  pre-existing test's unchanged expectations). Plus mock coverage for
  `MockFrameLibraryService`/`MockFrameSelectionService`/
  `MockFrameOverlayService`, a real round-trip test for
  `UiFrameSelectionService` (doesn't complete until `SubmitSelection`
  fires; a null submission means skipped), and `GdiFrameOverlayServiceTests`
  compositing a real frame PNG (transparent center, opaque red border)
  onto a real captured photo -- confirms a genuine same-dimension JPEG
  comes out, the transparent region still shows the original photo's
  color, and the opaque region shows the frame's. `dotnet test` -- **65
  passed, 0 failed**, run twice in a row clean (still watching for the
  earlier parallel-test race, since this added more `MockCameraService`
  usage).
- Verified via `Photobooth.ConsoleDemo`: session 9 (admin adds two frames)
  shows `[STATE] FramePicker`, picks "Classic Gold Border" by default, and
  both the final photo path and the uploaded URL carry a `_framed` suffix;
  session 10 (guest skips) shows `FramePicker` too but no `_framed` suffix
  anywhere.
- Verified the real SQL path directly, since `Photobooth.Tests` doesn't
  cover SQL-backed code (same gap as `SqlSessionRepository`/
  `SqlBoothSettingsProvider`): a throwaway script (not checked in) ran the
  migration against this machine's real, already-seeded LocalDB --
  confirmed the `Frame` table got created cleanly on a database that
  predates it -- inserted a frame, confirmed `SqlFrameLibraryService`
  returned it as active, deactivated it and confirmed the active list
  emptied while `GetAllByLocationAsync` still showed it (inactive), then
  deleted it and confirmed the table was back to its starting row count.
- **Not yet verified:** `FramePickerView`/`AdminWindow`'s new section
  actually rendering or being tapped through -- same interactive-desktop
  gap as every other WPF screen in this project (`ConsentView`,
  `PaymentView`, the rest of `AdminWindow`).

**Extra, not in the original plan -- print template editor**
- Camera and physical printer are both unavailable for the time being, so
  picked up "Print template editor" from the roadmap next -- fully
  code/test-verifiable without either, and `SpoolerPrinterService`'s own
  doc comment already flagged this exact gap ("booth print layout (strip
  vs. 4x6, borders, branding) is future work").
- New `PrintTemplate` record in `Photobooth.Core`: `Layout` ("Single" or
  "Strip"), `WidthInches`/`HeightInches`, `StripCopies`. Its
  `ComputeCellBounds(Rectangle pageBounds)` is pure geometry (one
  full-bounds rectangle for Single, `StripCopies` equal-height rectangles
  stacked top to bottom for Strip) -- deliberately kept out of
  `SpoolerPrinterService` so it's unit-testable without a real printer or
  the `[SupportedOSPlatform("windows")]` marking the actual GDI+ drawing
  code needs. `IsValid` centralizes the same validation `AdminWindow` and
  a future caller would otherwise duplicate.
- Folded into the existing `BoothSettings`/`IBoothSettingsProvider` seam
  (a third property, alongside `CountdownSeconds`/`GlamFilterEnabled`)
  rather than a new `BoothServices` seam -- a print template is booth-wide
  and admin-editable, exactly the shape that interface already covers, so
  `BoothStateMachine`'s constructor didn't need to change at all.
- `Location.PrintLayout`/`PrintWidthInches`/`PrintHeightInches`/
  `PrintStripCopies` -- four new `schema.sql` columns (defaults `'Single'`,
  4, 6, 1). `DatabaseInitializer` got another top-up migration
  (`EnsurePrintTemplateColumnsAsync`, same pattern as the booth-settings
  columns' before it) so an already-seeded database picks them up via
  `ALTER TABLE` without a manual reset.
- `IPrinterService.PrintAsync` now takes a `PrintTemplate` alongside the
  image path -- a real interface signature change, not just an additive
  one, since every implementation genuinely needs to know the layout to
  print correctly. `SpoolerPrinterService` sets `PrintDocument`'s custom
  `PaperSize` from the template's dimensions (hundredths of an inch, per
  that API) and draws into each cell `ComputeCellBounds` returns.
  `MockPrinterService` gained `PrintedTemplates`, recording every call --
  the first mock able to prove *which* template it was actually driven
  with, not just that printing happened.
- `AdminWindow`'s Settings section gained a "Print template" block:
  Single/Strip radio buttons, width/height text boxes, a strip-copies text
  box, validated via `PrintTemplate.IsValid` before saving through the
  extended `LocationRepository.UpdateSettingsAsync`. Not yet seen
  rendered, same interactive-desktop gap as the rest of `AdminWindow`.
- Verified via `Photobooth.Tests`: 10 new tests. `PrintTemplateTests`
  covers `IsValid` (bad layout name, non-positive dimensions, fewer than 1
  strip copy) and `ComputeCellBounds` directly for both layouts (a 3-copy
  strip produces three equal-height, gap-free, top-to-bottom rectangles --
  proof of the actual geometry, not just that *something* came back);
  `MockPrinterServiceTests` confirms `PrintedTemplates` records calls in
  order; a new `BoothStateMachineTests` case switches the booth to a
  2x6/2-copy strip via `MockBoothSettingsProvider` and confirms
  `MockPrinterService.PrintedTemplates` received that exact template
  rather than `PrintTemplate.Default` -- the actual thing this feature
  needed to prove, not just that the code compiles. `dotnet test` -- **75
  passed, 0 failed**, run twice in a row clean.
- Verified via `Photobooth.ConsoleDemo`: new session 9 switches to a 2x6
  strip mid-run (same "simulate an admin saving a change" pattern session
  8 already established) and prints `Printed with: PrintTemplate { Layout
  = Strip, WidthInches = 2, HeightInches = 6, StripCopies = 2, IsValid =
  True }` -- the printer genuinely received the new template, not the 4x6
  default.
- **Not yet verified:** the real SQL path -- this environment has no
  LocalDB instance installed at all (`sqllocaldb` isn't even on PATH),
  unlike the dev machine the earlier SQL-backed features (booth settings,
  frame library) were verified against, so this is a different, harder
  gap than those left behind: not just "not yet run," but "can't run here
  regardless." Also not verified: a physical sheet actually coming out at
  the configured size/layout -- needs the real printer, which is why this
  feature was picked up instead of Day 7's hardware buffer in the first
  place.

**Extra, not in the original plan -- general feedback surveys**
- Camera and printer are still unavailable, so picked up the other unbuilt
  half of "Surveys & data collection" next -- the liability disclaimer and
  email opt-in half was already done (`IConsentService`); a post-session
  rating/comment prompt was the piece still marked unbuilt on the roadmap.
- New `FeedbackResult` record (`Photobooth.Core`): `Rating` (1-5, nullable)
  and `Comment` (nullable), plus `IsEmpty` for "guest gave neither." New
  `BoothState.Feedback`, shown right after `Complete`'s "thank you" dwell,
  before the machine returns to `Idle`.
- `IFeedbackService` -- same interface-plus-mock seam as everything else.
  Unlike `IConsentService`/`IPaymentService` (still mock-only, since a real
  disclaimer/gateway needs external integration), a star rating and a
  comment box is just button taps and text input with no hardware or
  network dependency -- same reasoning that made `IFrameSelectionService`
  a real implementation rather than a mock, so `UiFeedbackService` is too
  (same `TaskCompletionSource` handoff `UiFrameSelectionService` already
  established).
- Collecting feedback is wrapped in its own `try`/`catch` in
  `RunSessionAsync`, deliberately separate from the rest of the session --
  a guest who walks away without tapping anything (a real risk here,
  more so than at Consent/Payment/FramePicker, since this is the very
  last screen before the booth is free for the next guest) should never
  turn an already-completed session into an `Error` one. Flagging, not
  fixing: no timeout auto-skips a guest who never responds -- consistent
  with every other interactive gate in this project (Consent, Payment,
  FramePicker), none of which have one either, but worth calling out
  since this is the state where a stuck guest costs the *next* guest the
  most.
- New `Feedback` table in `schema.sql` (`Rating` 1-5 nullable, `Comment`
  nullable). A row is only ever inserted when at least one of the two is
  non-null -- a guest who skips entirely leaves no row, not a row full of
  nulls. `DatabaseInitializer` got another top-up migration
  (`EnsureFeedbackTableAsync`, same pattern as `Consent`'s/`Frame`'s
  before it).
- `ISessionRepository.RecordFeedbackAsync` -- both `MockSessionRepository`
  and `SqlSessionRepository` implement it, same shape as
  `RecordConsentAsync`.
- `AdminDashboardRepository` gained `GetFeedbackSummaryAsync` (average
  rating + how many guests actually rated) and `GetRecentCommentsAsync` --
  collecting feedback and never reading it back would repeat the exact
  "data collected and then never used" gap already caught and fixed once
  for email opt-in (see that entry above), so `AdminWindow`'s dashboard
  section shows both alongside sessions/revenue/inventory.
- `MainWindow` gained `FeedbackView`: five star buttons (☆ tap to fill up
  to ★), an optional comment box, and Submit/Skip. The QR panel's eligible
  screens now include `Feedback`, not just `Printing`/`Complete` -- it
  previously disappeared the moment `Complete`'s dwell ended, before this
  state existed, so a guest still has the code to scan while rating.
- Verified via `Photobooth.Tests`: 5 new tests. `MockFeedbackServiceTests`/
  `UiFeedbackServiceTests` cover both implementations directly (default
  5-star/no-comment, skip-then-reset, the real `TaskCompletionSource`
  handoff not completing until `SubmitFeedback` fires); the happy-path
  `BoothStateMachineTests` case extended to assert the recorded feedback
  matches `MockFeedbackService`'s default (5 stars, no comment); a new
  skip case confirms the `Feedback` state still shows but no `Feedback`
  row gets written when the guest gives nothing at all. `dotnet test` --
  **80 passed, 0 failed**, run twice in a row clean.
- Verified via `Photobooth.ConsoleDemo`: session 12 (4-star rating plus a
  comment) prints `Feedback recorded: 4 stars -- "Loved the frames,
  printer was a little slow."`; session 13 (guest skips) prints `Feedback
  recorded this session: False` -- confirming the empty-skip path actually
  leaves no row, not just that the code ran without throwing. Final
  summary: 9 feedback records across 10 completed sessions (session 13's
  skip is the one gap, exactly as expected).
- **Not yet verified:** the real SQL path -- no LocalDB in this
  environment, same gap as the print template editor above -- or
  `FeedbackView`/the dashboard's new section actually rendering, same
  interactive-desktop gap as every other WPF screen in this project.

**Extra, not in the original plan -- screen & UI theming (colors, logo, event name)**
- Picked up "Screen & UI customization" next -- start-screen/button/background
  customization stays out of scope (colors + logo + event name only, per
  explicit direction), but that's still real progress on a `[ ]` roadmap item.
- New `BoothTheme` record (`Photobooth.Core`): `AccentColorHex`/`CanvasColorHex`/
  `InkColorHex` (validated `#RRGGBB`), an optional `LogoImagePath`, and
  `EventName`. Folded into `BoothSettings` as a new `Theme` property -- same
  reasoning `PrintTemplate` itself already established (booth-wide,
  admin-editable, read fresh every session, so no new `BoothServices` seam
  needed).
- **New technique, worth calling out:** `Theme` is an **`init`-only property
  outside `BoothSettings`'s primary constructor**, not a 4th positional
  parameter -- a record's positional parameters can't default to another
  type's static field (`BoothTheme.Default` isn't a compile-time constant),
  but an `init` property can. Every existing `new BoothSettings(...)` call
  site (mocks, `SqlBoothSettingsProvider`, `AdminWindow`, every test,
  `Photobooth.ConsoleDemo`) kept compiling completely unchanged, with `Theme`
  silently defaulting -- zero call-site churn for a feature that touches
  `BoothSettings`.
- `IPhotoBrandingService.ApplyBrandingAsync` gained a `studioName` parameter
  -- a genuine signature change, not just additive, since branding now needs
  to know the current event name. Retired `GdiPhotoBrandingService`'s
  hardcoded `private const string StudioName = "Focus & Snap"` -- the last of
  three hardcoded "Focus & Snap" occurrences in the codebase to fall (the
  other two were UI markup, see below).
- `Location` gained 5 new columns (`AccentColorHex`/`CanvasColorHex`/
  `InkColorHex`/`LogoImagePath`/`EventName`); `DatabaseInitializer` got
  another top-up migration (`EnsureBoothThemeColumnsAsync`, same pattern as
  every prior one). `LocationRepository` gained `UpdateThemeAsync`, kept
  deliberately separate from `UpdateSettingsAsync` so saving a theme change
  doesn't force the countdown/print-template fields to also validate, and
  vice versa.
- `AdminWindow` gained a "Theme" section: 3 hex color text boxes each with a
  live-updating color swatch, an event name box, and a logo `Browse...`
  button reusing the exact same "copy into a local `Assets/<Feature>/`
  folder" pattern the Frame library section already established (here,
  `Assets/Theme/`).
- `MainWindow` applies the theme by mutating the *existing* `SolidColorBrush`
  objects in `App.xaml`'s resource dictionary in place, not by replacing the
  dictionary entries. **Real bug caught before it shipped:** every screen
  binds these via `{StaticResource ...}`, which resolves to a direct
  reference to the brush object at XAML load time -- replacing the
  dictionary entry (`Application.Current.Resources["AccentBrush"] = new
  SolidColorBrush(...)`) wouldn't repaint anything already on screen, since
  existing elements would still be holding a reference to the *old* brush
  object. Mutating the existing unfrozen brush's `.Color` in place does
  repaint, since every `StaticResource` reference shares that same object.
  Applied once at startup and again every time the machine returns to
  `Idle` -- same "next guest, no restart" semantics every other setting
  already has, with the explicit caveat that a theme change saved mid-session
  won't repaint until the booth is back at `Idle`.
- Verified via `Photobooth.Tests`: new `BoothThemeTests.cs` (9 cases --
  `IsValid` for the default, bad hex on each of the 3 color fields, an empty
  event name, and confirming a null logo is still valid). `dotnet test` --
  **89 passed, 0 failed** (up from 80).
- Verified via `Photobooth.ConsoleDemo`: new session flips
  `settings.Settings = settings.Settings with { Theme = new BoothTheme(...) }`
  (renaming the event to "Sunset Social" and changing all 3 colors) and
  prints `branding.LastStudioName` (a new property added to
  `MockPhotoBrandingService` for exactly this) -- confirmed it reads
  `Sunset Social`, proving the new event name actually reached branding, not
  just that the settings object changed.
- **Not yet verified:** `AdminWindow`'s new Theme section or `MainWindow`'s
  brush-mutation repaint actually rendering on a real screen -- same
  interactive-desktop gap every other WPF screen in this project has. The SQL
  path (the new `Location` columns, `UpdateThemeAsync`) is also unverified
  against a real LocalDB -- this environment has none installed at all, same
  gap the print template editor and feedback survey entries above already
  flagged.

**Extra, not in the original plan -- video guestbook**
- Picked up "Video guestbook" next -- full build, mock-verified, same
  treatment `IPaymentService`/`IConsentService` already got: a real seam plus
  a real (if hardware-unverifiable-here) Windows implementation behind it,
  not a skeleton.
- **Scope decision:** a guestbook video is recorded, stored locally, and
  listed in `AdminWindow` for the admin to review/download -- it does *not*
  get a QR code, does *not* go through `ICloudUploadService`, and does *not*
  participate in printing. A video message is addressed to the event hosts
  (a digital equivalent of a paper guestbook), not a takeaway product for the
  guest the way the photo is, and video hosting is real added scope
  (Cloudinary free-tier limits, an entirely separate upload path) with no
  clear guest-facing payoff. Same proportionate-scoping precedent Glam Booth
  mode already set (just the B&W filter, skin smoothing left out).
- Two new `Photobooth.Core` seams, deliberately split: `IVideoGuestbookService`
  (`StartRecordingAsync`/`StopRecordingAsync`, the actual capture -- real impl
  `FfmpegVideoGuestbookService`, mock `MockVideoGuestbookService`) and
  `IGuestbookPromptService` (`AskToRecordAsync`/`WaitForStopAsync`, the UI
  wait -- real impl `UiGuestbookPromptService`, same `TaskCompletionSource`
  handoff `UiFrameSelectionService`/`UiFeedbackService` already established;
  mock `MockGuestbookPromptService`). Split for the same reason
  `IFrameSelectionService` and `IFrameOverlayService` already are separate
  seams: "does the guest want to, and when are they done" is a pure UI
  interaction with no hardware dependency, so it gets a real implementation
  immediately, while the actual capture is the part still gated on hardware
  this environment doesn't have.
- **Deliberately not routed through `ICameraService`/the CameraBridge pipe
  protocol** -- that protocol tethers the Nikon D3500, a photo/PTP-only
  device with no audio path, and a guestbook message needs the guest's actual
  voice. `FfmpegVideoGuestbookService` drives an independent webcam+mic
  capture instead, via `ffmpeg -f dshow -i video=...:audio=...` as a child
  `Process`. **This is the project's first external-process dependency**, a
  different and harder category of gap than "hardware unplugged behind an
  already-installed driver" (the D3500/printer's prior status) -- ffmpeg
  itself has to be separately installed, configured via `PHOTOBOOTH_FFMPEG_PATH`
  (falling back to `ffmpeg` on PATH) plus `PHOTOBOOTH_WEBCAM_DEVICE_NAME`/
  `PHOTOBOOTH_MIC_DEVICE_NAME` (DirectShow device names are machine-specific,
  same env-var-driven config pattern as `PHOTOBOOTH_PRINTER_NAME`/
  `CLOUDINARY_URL`). Stopping writes `"q"` to ffmpeg's stdin (its documented
  graceful-stop signal, needed so the mp4's `moov` atom finalizes -- an
  outright `Kill()` risks a corrupt file) with a 10s timeout/`Kill()` fallback
  if it doesn't exit cleanly.
- New `BoothState.Guestbook`, shown right after `Complete`'s dwell and before
  `Feedback`. Wrapped in its own `try`/`catch` in `RunSessionAsync`, same
  reasoning the `Feedback` block right after it already documents: a guest
  who walks away without tapping anything should never turn an
  already-completed (already printed, already paid for) session into an
  `Error` one. A 60-second safety-net `Task.Delay` races against
  `WaitForStopAsync` so a stuck guest can't leave ffmpeg recording
  indefinitely.
- New `GuestbookVideo` table (`SessionId`, `FilePath`, `DurationSeconds`,
  `RecordedAt`); `DatabaseInitializer` got another top-up migration
  (`EnsureGuestbookVideoTableAsync`). `ISessionRepository.RecordGuestbookVideoAsync`
  implemented by both `MockSessionRepository` and `SqlSessionRepository`, same
  shape as `RecordFeedbackAsync`. `AdminDashboardRepository` gained
  `GetRecentGuestbookVideosAsync`/`DeleteGuestbookVideoAsync` -- collecting
  recordings and never reading them back would repeat the exact "data
  collected, never used" gap already caught and fixed once for email opt-in.
- `AdminWindow` gained a "Guestbook messages" section: session/duration/
  timestamp per row, an "Open" button (launches the OS default video player
  via `Process.Start` with `UseShellExecute = true`) and "Delete" (removes
  the row; leaves the physical file, same "nothing deletes old print files
  either" precedent). `MainWindow` gained a `GuestbookView` with two
  mutually-exclusive sub-panels (Ask: Yes/No; Recording: Stop), same
  nested-visibility idiom `PaymentQrBorder` already established inside
  `PaymentView`.
- `BoothServices` picked up its 16th/17th properties (`GuestbookPrompt`,
  `VideoGuestbook`) -- one new property each, no constructor signature
  change, the exact payoff the record-bundle refactor was built for several
  entries ago.
- Verified via `Photobooth.Tests`: 12 new tests. `VideoGuestbookServiceTests.cs`
  covers `MockVideoGuestbookService` (start/stop round-trip, `FailNextStart`/
  `FailNextStop` reset-after-firing, stopping with nothing in progress
  throws), `MockGuestbookPromptService` (default accept, `SkipNext`
  decline-then-reset), and `UiGuestbookPromptService`'s two independent
  `TaskCompletionSource` waits (neither completes until the matching
  `SubmitRecordDecision`/`SubmitStop` fires). `BoothStateMachineTests` adds
  three cases: a recorded message (states show `Guestbook` between `Complete`
  and `Feedback`, one `RecordedGuestbookVideos` entry with a positive
  duration), a decline (state still shows, no row), and a forced
  `FailNextStart` (session still reaches `Idle` normally, no `Error` state,
  no row -- the try/catch actually works). `dotnet test` -- **101 passed, 0
  failed** (up from 89).
- Verified via `Photobooth.ConsoleDemo`: session 14 (guest records) shows
  `[STATE] Guestbook` between `Complete` and `Feedback` and prints the
  recorded file path/duration; session 15 (guest declines via
  `guestbookPrompt.SkipNext`) shows the state but records nothing. Final
  summary: 12 guestbook videos across 13 completed sessions (session 15's
  decline is the one gap, exactly as expected).
- **Not yet verified:** real webcam+mic capture -- no hardware available
  here, and (unlike prior hardware gaps) no confirmation that ffmpeg itself
  is even installed on a real deployment machine yet either. *Can* confirm
  `FfmpegVideoGuestbookService`'s failure path is honest, though: pointing
  `PHOTOBOOTH_FFMPEG_PATH` at a nonexistent file and calling
  `StartRecordingAsync()` throws the clear "is ffmpeg installed... see the
  README" message rather than an opaque crash. The manual ffmpeg-install
  step (and the two device-name env vars) needs documenting in the README
  alongside `CLOUDINARY_URL`/`PHOTOBOOTH_PRINTER_NAME` before a real
  deployment. Also unverified: `GuestbookView`/`AdminWindow`'s new section
  actually rendering, same interactive-desktop gap as everything else.

**Extra, not in the original plan -- print template visual editor (logos/text drag-and-drop)**
- Picked up the piece of "Built-in print template editor" flagged as still
  unbuilt (`[~]` on the roadmap): a dedicated visual editor for arbitrary
  logo/text placement, on top of the paper-size/layout work already done.
  Built as a real drag-and-drop canvas, per explicit direction, not a
  numeric-fields form.
- New `PrintTemplateElement` record (`Photobooth.Core`): `Kind` (Logo or
  Text), position/size as **fractions (0-1) of the cell's bounds**
  (`XPercent`/`YPercent`/`WidthPercent`/`HeightPercent`), plus `Text`/
  `ImagePath`/`FontFamily`/`FontSizePercent`/`Bold`/`ColorHex`. Percent-based,
  not absolute pixels/inches, specifically so the same element list scales
  correctly if an admin later changes `WidthInches`/`HeightInches` (e.g.
  4x6 to a 2x6 strip). `PrintTemplate.Elements` added the same way
  `BoothSettings.Theme` was two entries ago -- an **`init`-only property
  outside the primary constructor**, so every existing 4-arg
  `new PrintTemplate(...)` call site kept compiling unchanged.
  `PrintTemplate.ComputeElementBounds` translates one element's cell-relative
  percentages into pixel bounds -- pure geometry, same unit-testability as
  the pre-existing `ComputeCellBounds`.
- New `PrintCompositor` (`Photobooth.Core`, Windows-only): the single source
  of truth both the real print (`SpoolerPrinterService.Draw`, now a one-line
  delegation) and the editor's live preview (`RenderPreview`) draw from.
  This is deliberate, not incidental -- it's what makes the editor's preview
  **provably WYSIWYG** rather than a second renderer that happens to agree
  with the real one today and silently drift from it tomorrow.
- New `PrintTemplateElementRepository` (`Photobooth.Data`, plain repository
  like `FrameRepository`, no interface/mock): `GetAllByLocationAsync` and
  `ReplaceAllAsync` (delete-then-reinsert inside one transaction -- the
  editor always saves its whole working list at once, so there's no need for
  per-row update/delete tracking). `SqlBoothSettingsProvider` attaches the
  element list to `PrintTemplate.Elements` after reading the `Location` row
  (on a separate connection, read only once the first reader has finished --
  an early version of this interleaved the two queries on the same open
  reader and would have broken).
- New `Location`-scoped `PrintTemplateElement` table; `DatabaseInitializer`
  got another top-up migration (`EnsurePrintTemplateElementTableAsync`).
- New `PrintTemplateEditorWindow` -- a dedicated modal (not folded into
  `AdminWindow`'s flat settings list, since a `Canvas` drag surface doesn't
  fit that flow), opened via a new "Edit print template..." button in
  `AdminWindow`'s existing Print template block. Mechanics: a
  `PreviewImage`/`ElementsCanvas` pair sized and positioned identically, so a
  drag delta measured on the canvas maps 1:1 onto the rendered preview below
  it with no extra scaling math; plain `MouseLeftButtonDown`/`MouseMove`/
  `MouseLeftButtonUp` + `CaptureMouse()` on each element's `Border` for
  dragging (no WPF `Thumb` control -- this codebase has zero
  templated-control usage anywhere, so plain event handlers stay consistent
  with every other interactive screen) and a small corner `Rectangle` handle
  with its own drag trio for resizing; every drag-end/resize-end/property
  edit re-renders the live preview via `PrintCompositor.RenderPreview`,
  converted to a `BitmapSource` via `GetHbitmap()` +
  `CreateBitmapSourceFromHBitmap` (with an explicit `DeleteObject` P/Invoke
  cleanup to avoid leaking the GDI handle). A side panel shows Text/Logo
  properties for whatever's selected (text content, font size slider, bold,
  hex color; or a Browse button for a logo image, reusing the same
  copy-into-local-`Assets/PrintElements/`-folder pattern the Frame library
  and Theme sections already established). Falls back to a generated
  placeholder photo if `./captures` is empty, so the editor works in this
  camera-less dev environment too.
- **Gotcha found and documented, not just fixed silently:** `PrintTemplate.Elements`
  is `IReadOnlyList<T>`, and C# records only synthesize *reference* equality
  for non-record collection-typed properties -- a whole-record
  `Assert.Equal(expectedTemplate, actualTemplate)` does **not** structurally
  compare `Elements` even though `Layout`/`WidthInches`/etc. compare fine.
  Every new test touching `Elements` asserts on `.Elements.Count` and
  individual element properties directly instead.
- Also fixed along the way: `PrintCompositor`'s original aspect-ratio math
  for `RenderPreview` had width/height backwards (a portrait 4x6 would have
  come out wider than tall). Caught before ever running by re-deriving it
  explicitly (`aspect = Width/Height`; the *longer* side becomes
  `previewWidthPx` -- for a portrait template, aspect < 1, so *height* is the
  longer side, not width) and factoring the corrected logic into a shared
  `ComputePreviewDimensions` helper both `RenderPreview` and the editor
  window's canvas sizing now call, so they can't drift apart from each
  other either.
- Verified via `Photobooth.Tests`: 18 new tests. `PrintTemplateElementTests`
  (new file, 10 cases counting theory data) covers `IsValid` for both kinds
  and out-of-range bounds; the pre-existing `PrintTemplateTests` class (in
  `MockServicesTests.cs`) gained 3 cases -- `Elements` defaulting to empty
  and two `ComputeElementBounds` cases (including one with an offset cell,
  proving the cell's own position is added in, not just its size).
  `PrintCompositorTests.cs` (new, Windows-only, 5 cases) draws a real Text
  element and a real Logo element onto a fabricated `Bitmap` and samples
  pixel regions to confirm each was actually drawn where expected while an
  untouched corner still shows the base photo's color, plus
  `ComputePreviewDimensions`/`RenderPreview` cases for both portrait and
  landscape templates (this is what caught the aspect-ratio bug above,
  before it ever reached the editor window). `dotnet test` -- **119 passed,
  0 failed** (up from 101).
- Verified via `Photobooth.ConsoleDemo`: new session builds a `PrintTemplate`
  with a Text and a Logo element via
  `settings.Settings.PrintTemplate with { Elements = [...] }` (the same
  "simulate an admin saving a change" pattern every settings-driven feature
  in this demo already uses) and prints
  `printer.PrintedTemplates[^1].Elements.Count` -- confirmed as 2, proving
  the elements actually reached `IPrinterService`, not just that the code
  compiles.
- Verified via a full `dotnet build Photobooth.sln`: clean, 0 warnings, 0
  errors, all 7 projects including `Photobooth.UI` and
  `Photobooth.CameraBridge.Host`.
- **Not yet verified:** the real SQL path (no LocalDB in this environment,
  same gap as every other SQL-backed feature above) and, the single largest
  unverified risk of this whole three-feature body of work, the actual
  drag/resize *feel* of `PrintTemplateEditorWindow` -- no interactive desktop
  to click through it. Unlike the percent-math underneath it (fully
  unit-tested, see above), mouse-event wiring itself isn't something a unit
  test can exercise. A physically printed sheet showing the composited
  elements in the right place also needs the real printer, same gap
  `SpoolerPrinterService` already had before this feature existed.

## Remaining

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
- [~] Video guestbook: recorded personalized messages/greetings from guests.
      Full mock-verified build done -- see the Done section above
      (`IVideoGuestbookService`/`FfmpegVideoGuestbookService`,
      `BoothState.Guestbook`, `GuestbookVideo`). Real webcam/mic capture
      against actual hardware is still unverified (no camera/mic hardware
      or confirmed ffmpeg install in this environment).

**Customization & design**
- [x] Built-in print template editor: 4x6, 2x6 strip, custom dimensions,
      logos, text, graphics. Paper size and Single/Strip layout, plus a
      dedicated drag-and-drop visual editor for arbitrary logo/text
      placement, are both done -- see `PrintTemplate`/`PrintTemplateElement`/
      `PrintCompositor`/`PrintTemplateEditorWindow` in the Done section above.
- [~] Screen & UI customization: start screen, buttons, backgrounds,
      themes per event brand. Colors, an event logo, and the event/brand
      name are admin-editable now -- see `BoothTheme` in the Done section
      above. Per-screen backgrounds and start-screen/button customization
      beyond color are still unbuilt.
- [ ] Green screen / chroma key: real-time background replacement with
      custom digital backdrops.
- [ ] Virtual attendant / mirror booth: guided video/audio prompts
      through the session.
- [~] Digital overlays & stickers: static/animated graphics, props,
      filters layered over media. Partly done -- `IPhotoBrandingService`
      stamps a studio name/date caption bar (see the Done section
      above). Animated graphics, props, and guest-chosen stickers are
      still unbuilt.

**Sharing & connectivity**
- [~] Instant digital sharing: email, SMS/MMS, WhatsApp, QR code, AirDrop.
      QR code and email are both done now (`IEmailDeliveryService`, see
      the Done section above; no real SMTP delivery yet). SMS/MMS,
      WhatsApp, and AirDrop are still unbuilt.
- [x] Offline queueing: store shares locally, auto-upload once back online.
      Done — see `IPendingUploadQueue` in the Done section above.
- [ ] Cloud sync: event data, analytics, and media synced across devices.
- [ ] Hashtag printing: pull and print tagged photos from social feeds.

**Workflow & management**
- [ ] Live view & camera control: shutter speed, aperture, ISO, live
      preview alignment from the software UI.
- [x] Surveys & data collection: email opt-in and liability disclaimer
      prompts, plus general feedback (star rating + comment) — done, see
      `IConsentService`/`Consent` and `IFeedbackService`/`Feedback` in the
      Done section above.
- [~] Cashless payments: card reader / digital payment integration
      (extends the `IPaymentService` seam from Day 6). Partly done —
      `MockCardReaderPaymentService` proves the seam generalizes beyond
      QR (see the Done section above), but there's still no real card
      reader hardware or payment gateway behind it.
- [ ] Remote booth control: guests/attendants trigger workflows from a
      companion mobile app.

## dslrBooth feature-parity plan (2026-08-31)

Screenshots of dslrBooth's admin UI (Event Manager, Screen Editor, Print
Layout designer, General/Capture/Camera Settings, Virtual Attendant,
Effects & Stickers, Green Screen, Survey, Disclaimer, Sharing Settings,
Print Setup) were reviewed feature-by-feature and mapped onto this
project's actual architecture (state-machine-driven single session flow +
`AdminWindow` settings dashboard + `PrintTemplateEditorWindow`), not a
screen-editor rebuild. Full feature inventory from the screenshots lives
in chat history; below is the subset actually being tracked, in build
order. Items already covered by the roadmap above are cross-referenced,
not duplicated.

**Phase 1 — Settings foundation (schema + BoothSettings) — done 2026-08-31**
- [x] Extended `Location` (`schema.sql`) with columns grouped by dslrBooth
      section: `CaptureMode`/`AlsoCreateGif`; `BoothIconsEnabled`/
      `ShowLiveView`/`MirrorLiveView`/`LiveViewRotation`; `BeautyFilterEnabled`/
      `FiltersMode`/`WatermarkImagePath`; `GreenScreenEnabled`/
      `GreenScreenBackgroundPath`; `SurveyEnabled`; `DisclaimerHeader`/
      `DisclaimerText`; `PrintAutomatically`/`ShowPrintButton`/
      `PrintLimitPerEvent`/`PrintLimitPerSession`/`PrintSharpening`;
      `EmailEnabled`/`SmsEnabled`/`QrEnabled`.
- [x] Expanded `BoothSettings` (`Photobooth.Core/IBoothSettingsProvider.cs`)
      with 8 new nested init-only records, one per dslrBooth section --
      `CaptureSettings`, `ScreenSettings`, `EffectsSettings`,
      `GreenScreenSettings`, `SurveySettings`, `DisclaimerSettings`,
      `PrintOptions`, `SharingSettings` -- each with a `static Default`
      instance, same pattern `Theme` already used. Init properties (not
      positional constructor params), so every existing
      `new BoothSettings(...)` call site (mocks, tests, ConsoleDemo,
      `SqlBoothSettingsProvider`) keeps compiling unchanged with these
      silently defaulting.
- [x] `SqlBoothSettingsProvider.GetSettingsAsync` now selects all new
      columns in the same query and maps them into the 8 new records.
- [x] `DatabaseInitializer.EnsureDslrBoothParitySettingsColumnsAsync`
      added (same top-up-migration pattern as
      `EnsureBoothSettingsColumnsAsync`/`EnsureBoothThemeColumnsAsync`) so
      an already-seeded LocalDB picks up all 22 new columns via
      `ALTER TABLE` without a manual `DROP DATABASE`.
- Verified: `dotnet build Photobooth.sln` -- clean, 0 warnings, 0 errors,
      all 7 projects. `dotnet test` -- **119 passed, 0 failed** (no
      regressions; this phase added no new test coverage of its own since
      nothing yet reads the new settings -- Phases 3/5 wire them into
      actual behavior).
- **Not yet verified:** the real SQL path against a live LocalDB instance
      (same recurring gap noted throughout this file for SQL-backed
      features -- no LocalDB instance available in this environment) and
      `AdminWindow`/`MainWindow` don't read any of these settings yet
      (that's Phases 3 and 5).

**Phase 2 — Capture mode expansion (BoothStateMachine) — GIF/Boomerang/
Video all done 2026-08-31**

Overlaps the existing unstarted roadmap item "Alternative capture
formats: GIFs, Boomerangs, ... video" above. Unblocked the same day it
was originally flagged as hardware-risky (see the reasoning that was
here before -- `PtpCameraService` already works against this machine's
UVC webcam per Day 2, so GIF/Boomerang need no camera-specific code).

- [x] `CaptureSettings` (Phase 1) extended with `FrameCount`/`FrameDelayMs`
      (defaults 4/500ms, matching dslrBooth's own GIF screen defaults);
      `schema.sql`'s `GifFrameCount`/`GifFrameDelayMs` columns and the
      matching `DatabaseInitializer` top-up migration added alongside.
- [x] `IGifComposerService` (interface + mock) added to `Photobooth.Core`,
      same seam as camera/printer/branding. `MockGifComposerService`
      copies the first frame with a `_gif`/`_boomerang` suffix (same
      "copy with a suffix" pattern `MockPhotoBrandingService` already
      established) and records the frame count/reversed flag it received
      for tests/demo to assert against.
- [x] `GdiGifComposerService` (real implementation) encodes each frame as
      a single-image GIF via GDI+ (the only encoder GDI+ has), then
      splices the frames' Image Descriptor + LZW data blocks together by
      hand into one real animated GIF89a file -- no new NuGet dependency,
      same "keeps this project dependency free" preference already
      established for `PlaceholderImage`/branding/filter. Adds a
      NETSCAPE2.0 Application Extension once (loop forever) and a
      Graphic Control Extension per frame (delay, from `FrameDelayMs`).
      **Documented known limitation:** only the first frame's color table
      is kept as the shared palette; other frames' LZW data still indexes
      whatever palette GDI+ chose for that frame individually. Reads
      correctly for the real use case (frames captured back-to-back of a
      barely-moving guest almost always get near-identical palettes) but
      isn't spec-guaranteed for frames with genuinely different color
      content. A fully correct version would re-quantize every frame to
      one shared palette first -- meaningfully more work, deferred rather
      than built speculatively.
- [x] `BoothStateMachine`'s `Capturing` step branches on
      `settings.Capture.Mode`: `"GIF"`/`"Boomerang"` loop
      `ICameraService.CaptureAsync()` `FrameCount` times (with
      `FrameDelayMs` between captures) and hand the frames to
      `IGifComposerService.ComposeAsync(..., reversed: Mode == "Boomerang")`;
      `"Photo"` (the default, and the only mode that existed before this)
      is unchanged. Deliberately skips the glam filter/branding/
      frame-overlay pipeline entirely for GIF/Boomerang -- those are all
      single-still GDI+ operations that would either only touch the first
      frame or corrupt the animation outright if pointed at a multi-frame
      GIF; a real fix means compositing each effect onto every frame
      before assembly, not attempted here, documented inline in the code.
- [x] `BoothServices` gained a `GifComposer` property; every call site
      updated (`MainWindow`'s composition root -> `GdiGifComposerService`,
      `Photobooth.ConsoleDemo` -> `MockGifComposerService`, all 24
      `Photobooth.Tests` call sites -> `MockGifComposerService`).
- [x] **Video mode.** Needed continuous recording rather than discrete
      stills, so it reuses the `IVideoGuestbookService`/ffmpeg precedent
      (ordinary webcam+mic, independent of the PTP pipe) rather than
      `ICameraService`. New `IBoothVideoService`/`BoothVideoRecording`
      (interface + `MockBoothVideoService`), and
      `FfmpegBoothVideoService` (real, drives ffmpeg to an mp4 the same
      way `FfmpegVideoGuestbookService` does, reusing the same
      `PHOTOBOOTH_FFMPEG_PATH`/`PHOTOBOOTH_WEBCAM_DEVICE_NAME`/
      `PHOTOBOOTH_MIC_DEVICE_NAME` env vars rather than duplicating device
      config under new names -- it's the same physical webcam/mic
      either way). `CaptureSettings.VideoDurationSeconds` (default 10,
      schema column + migration added alongside) drives a fixed-duration
      recording: start, wait out the duration, stop -- no "guest taps
      stop" UI yet, an accepted simplification for the same reason the
      guestbook recording's 60s safety-net timeout already is one.
  - [x] `BoothStateMachine`'s mode branch renamed `isBurstMode` ->
        `isNonPrintableCapture` and extended to include `"Video"`
        alongside `"GIF"`/`"Boomerang"` -- it now also guards the
        `Printing` state itself (not just `FramePicker`), so Video mode
        goes straight from `Reviewing` to `Complete` with no `Printing`
        state, no `IPrinterService.PrintAsync` call, and no `Print` row --
        dslrBooth's own Video mode is share-only too, and there's no
        sane way to hand a video file to `SpoolerPrinterService`/GDI+
        anyway. Not printing doesn't affect the session's outcome
        otherwise -- `Complete`, the free-event `Payment` row, upload,
        and email all still happen exactly as they do for Photo mode.
  - [x] `BoothServices` gained a `BoothVideo` property; every call site
        updated the same way `GifComposer` was (`MainWindow` ->
        `FfmpegBoothVideoService`, `ConsoleDemo`/all 26
        `Photobooth.Tests` call sites -> `MockBoothVideoService`).
- Verified via `Photobooth.Tests`: 1 new test
      (`RunSessionAsync_VideoMode_RecordsAndSkipsPrintingEntirely` --
      confirms the recorded file becomes `LastCapturedImagePath`, and
      explicitly asserts `Printing`/`FramePicker` are both absent from the
      state sequence and no `Print` row or printer call happened, not just
      that the session completed). `dotnet test` -- **124 passed, 0
      failed**, up from 123.
- Verified via `Photobooth.ConsoleDemo`: new session 20 (Video mode, 1s
      recording for a fast demo) shows the real state sequence
      `Reviewing -> Complete` (no `Printing` in between), `Recorded file`
      matches `Final photo path`, `Printed this session: False (false is
      correct...)`, and the `.mp4` still gets uploaded and emailed
      correctly, same as any other session's final artifact.
- (GIF/Boomerang verification details below are from earlier the same
      day, unchanged by adding Video.) Verified via `Photobooth.Tests`: 4
      new tests. `GdiGifComposerServiceTests`
      runs the real GDI+ path against real `MockCameraService`-captured
      frames and confirms a genuine `GIF89a`-signed file comes out with
      the right frame count via `Image.GetFrameCount(FrameDimension.Time)`
      -- 3 frames in for GIF mode, 6 frames out for Boomerang mode with 4
      frames in (4 forward + 2 backward, not repeating the two end
      frames) -- proving the splice math is correct, not just that a file
      got written. `BoothStateMachineTests` adds GIF-mode (confirms
      composer received 3 frames, `reversed: false`, `_gif` in the final
      path, branding/filter/`FramePicker` all skipped) and Boomerang-mode
      (confirms `reversed: true`, `_boomerang` in the path) cases.
      `dotnet test` -- **123 passed, 0 failed**, up from 119.
- Verified via `Photobooth.ConsoleDemo`: two new sessions (18: GIF, 3
      frames; 19: Boomerang, 4 frames) added, simulating an admin flipping
      `CaptureSettings.Mode` the same way every other admin-editable
      setting in this demo already does. Session 19's real output:
      `Frames composed: 4 (reversed: True)`, final path
      `mock_0019_..._boomerang.gif`, uploaded and emailed correctly like
      any other session.
- Verified via `dotnet build Photobooth.sln`: clean, 0 warnings, 0
      errors, all 7 projects.
- **Not yet verified:** real D3500/real webcam hardware (same recurring
      gap as every camera-touching feature in this file -- the mock/GDI+
      path is fully exercised, the live `PtpCameraService` capture loop
      for GIF/Boomerang specifically has not been run against physical
      hardware, only against `MockCameraService`).

**Phase 3 — Admin Dashboard UI (`AdminWindow.xaml`/`.cs`) — done 2026-08-31**
- [x] Extend the existing sectioned layout (`SectionHeader`/`Row` styles)
      with new sections once Phase 1 settings exist: Capture Settings,
      Effects & Stickers (beauty filter/watermark/filters-mode — beauty
      filter overlaps the existing "Glam Booth mode" roadmap item),
      Green Screen, Survey on/off, Disclaimer text editor, Sharing
      Settings (email/SMS/QR toggles — overlaps "Instant digital
      sharing"), Print Setup extensions (auto-print, limits, sharpening).
- Explicitly out of scope (no analog in this architecture, dslrBooth-
      ecosystem-specific): Event Manager grid, fotoShare Cloud, LumaShare,
      Triggers/local HTTP API. (Virtual Attendant, the Survey
      question-builder, and the visual Screen Editor were cut from this
      "out of scope" list on 2026-08-31 -- see Phase 6 below, they're in
      scope now.)
- `LocationRepository` gained `GetAllAsync`/`LocationRecord` coverage for
  all 8 Phase 1 setting groups (same columns/order `SqlBoothSettingsProvider`
  already reads) plus a new `UpdateDslrBoothParitySettingsAsync` write path,
  kept separate from `UpdateSettingsAsync`/`UpdateThemeAsync` for the same
  "don't force an unrelated section to validate" reason those two are
  already split. `ScreenSettings` has no editable UI yet (its guest-facing
  consumption is Phase 5, not this phase) so `AdminWindow` round-trips
  whatever it loaded straight back through on save instead of resetting it.
- `AdminWindow` gained seven new sections (Capture Settings, Effects &
  Stickers, Green Screen, Survey, Disclaimer, Sharing Settings, Print
  Setup extensions), reusing the existing `SectionHeader`/`Row` styles,
  radio-button-group pattern (`CaptureMode`/`FiltersMode`/`PrintSharpening`
  each mirror their schema `CHECK` constraint's valid values), and the
  browse-and-copy-to-`Assets/<Folder>` pattern already established for
  frame images and the theme logo (watermark → `Assets/Watermarks`, green
  screen background → `Assets/GreenScreen`). One new "Save
  Capture/Effects/Sharing Settings" button/status text for the whole
  group, following the one-save-button-per-section precedent, with the
  same defensive numeric validation and Firebrick/MutedBrush status-text
  style as `SaveSettingsButton_Click`.
- Verified via `dotnet build Photobooth.sln`: clean, 0 warnings, 0
  errors, all 7 projects. `dotnet test`: 124 passed, 0 failed (no
  regressions -- unchanged from the pre-existing count; no new tests
  added, this phase is UI plus a repository write path, no new
  `BoothStateMachine`-observable behavior to test).
- **Not yet verified:** the new sections haven't been seen rendered,
  same interactive-desktop gap as `AdminWindow`'s existing dashboard
  section and `ConsentView`/`PaymentView` before it.

**Phase 4 — Print Template Designer (`PrintTemplateEditorWindow`) — done 2026-08-31**
- [x] Extend existing editor (already covers `PrintTemplateElement` =
      dslrBooth's layers) with a layer list panel and alignment tools if
      not already present. Paper size/orientation already covered by
      `PrintLayout`/`PrintWidthInches`/`PrintHeightInches`.
- Confirmed neither piece existed yet by reading the window's full
      `.xaml`/`.xaml.cs` before writing anything: no list control of any
      kind, no alignment buttons -- only free-drag/resize on the canvas
      and one flat "Selected element" property panel.
- Layer list panel: a `ListBox` (`LayerListBox`) added above "Selected
      element", populated from `_elements` ("Text: <text>" / "Logo" per
      row) via a new `RefreshLayerList()`, called after every add/delete
      and reorder. Selecting a row calls the existing `SelectElement(index)`
      (guarded by a new `_suppressLayerListEvents` flag, the same
      round-trip-guard pattern `_suppressPropertyEvents` already uses for
      the property panel); `SelectElement` now also pushes selection back
      into `LayerListBox.SelectedIndex` so canvas clicks and list clicks
      stay in sync both directions.
- Alignment tools: six buttons (Left/Center/Right, Top/Middle/Bottom)
      that set the selected element's `XPercent`/`YPercent` against the
      canvas edges (e.g. right = `1 - WidthPercent`, center = `(1 -
      WidthPercent) / 2`) via the same `with { }` pattern
      `ElementContainer_MouseLeftButtonUp` already uses, then
      `PositionVisual(index)` + `RefreshPreview()`.
- Z-order: read `PrintCompositor.RenderPreview`/its `foreach
      (PrintTemplateElement element in template.Elements)` loop first to
      confirm list order really is paint order (later elements draw over
      earlier ones) before adding "Bring to front"/"Send to back" buttons
      -- confirmed, so both buttons just move the element to the end/start
      of `_elements`.
- Reordering approach: up/down buttons, not drag-to-reorder in the list.
      `_elements`/`_containers`/`_handles` are three parallel lists every
      existing handler indexes into (`_containers[index]`,
      `SelectElement`, the drag/resize handlers); a `ListBox` drag-reorder
      gesture would need its own drag-tracking state independent of the
      canvas's existing `_draggingIndex`/`_resizing` fields, doubling the
      surface area for keeping three lists in sync for a photobooth admin
      screen that will rarely hold more than a handful of layers. One
      `MoveSelectedLayerTo(newIndex)` handles up/down, front, and back
      alike: removes the element/container/handle at the same index from
      all three lists, re-inserts at `newIndex`, then rebuilds
      `ElementsCanvas.Children` in list order (Canvas paints children in
      collection order, which must track `_elements`' order after a
      move).
- Verified via `dotnet build Photobooth.sln`: clean, 0 warnings, 0
      errors, all 7 projects. `dotnet test`: 124 passed, 0 failed -- no
      regressions vs. Phase 3/5's count; `PrintTemplateEditorWindow` has
      no direct unit tests (mouse/UI wiring, per its own doc comment) and
      no pure logic was factored out worth a separate test -- the
      alignment math is a few lines of arithmetic identical in shape to
      what `ElementContainer_MouseLeftButtonUp` already does inline,
      unguarded by a test today either.
- **Not yet verified:** the new layer list and alignment buttons haven't
      been seen rendered or clicked through, same interactive-desktop gap
      every WPF screen in this project has.

**Phase 5 — MainWindow guest-facing screens — done 2026-08-31**
- [x] Apply `ScreenSettings` to existing state-bound views: `Idle` view
      reads `BoothIconsEnabled`/`ShowLiveView`; `Capturing` view reads
      `MirrorLiveView`/`LiveViewRotation`/countdown color.
- [x] Post-`Complete` sharing step: surface `LastPhotoUrl` as QR (reuses
      existing `CloudUpload`/`PhotoUploaded`) gated by
      `SharingSettings` Email/SMS/QR toggles.
- The scope text's screen names don't quite match where these things
      actually live in `MainWindow.xaml`: the live camera feed and the
      countdown number both render in `CountdownView`, not `IdleView` or
      `CapturingView` (`IdleView` is just "Tap to start" text, `CapturingView`
      is just "Say cheese!" text -- neither has ever had a live-view or icon
      element). `ShowLiveView`/`MirrorLiveView`/`LiveViewRotation` are wired
      to the one place a live feed actually renders (`LiveViewImage` inside
      `CountdownView`): `ShowState` now only starts `_liveViewTimer` (and
      shows the `Image`) when `ScreenSettings.ShowLiveView` is true --
      collapsed and the timer left stopped otherwise, so a booth with the
      feed off doesn't keep polling the camera pipe for frames nobody sees.
      A new `ApplyLiveViewTransform()` (called from `ApplyThemeAsync`
      alongside the existing theme re-read) sets `LiveViewImage.LayoutTransform`
      to a `ScaleTransform(-1,1)` for `MirrorLiveView` and a `RotateTransform`
      for `LiveViewRotation` -- only 0/90/180/270 are honored (the schema
      column has no `CHECK` constraint unlike `CaptureMode`/`FiltersMode`, so
      an out-of-range value just falls back to unrotated rather than throwing).
- `BoothIconsEnabled` has no on-screen icon UI to gate: nothing resembling
      dslrBooth's decorative Screen Editor icons exists in `IdleView` today.
      Rather than inventing new icon elements this phase didn't ask for, it's
      read into a `_screenSettings` field (same re-read-at-Idle cadence as
      everything else) for Phase 6's Screen Editor to consume once that UI
      exists.
- "Countdown color" was already satisfied structurally, not new code:
      `CountdownNumber`'s `Foreground` was already bound to
      `{StaticResource AccentBrush}`, which `ApplyThemeAsync` already
      repaints from `BoothTheme.AccentColorHex` on every Idle transition --
      confirmed by reading `MainWindow.xaml`, no separate countdown-color
      setting exists in `ScreenSettings` to add.
- QR gating: `QrPanel.Visibility` (set in both `ShowState` and `LoadQrCode`,
      the two existing call sites) now additionally requires
      `SharingSettings.QrEnabled`, read into a new `_sharingSettings` field
      alongside `_screenSettings`. `EmailEnabled`/`SmsEnabled` end up as
      no-ops in `MainWindow` for now: email delivery (`IEmailDeliveryService`,
      triggered from `BoothStateMachine`'s consent-driven opt-in) isn't
      QR-adjacent and isn't gated by this setting anywhere yet, and there is
      no `ISmsDeliveryService`/SMS feature in `Photobooth.Core` at all --
      confirmed by grep. `QrEnabled` is the only one of the three toggles
      with a real guest-facing surface to gate today.
- Verified via `dotnet build Photobooth.sln`: clean, 0 warnings, 0 errors,
      all 7 projects. `dotnet test`: 124 passed, 0 failed -- unchanged from
      Phase 3's count; no new `BoothStateMachine`-observable behavior was
      added (the changes are `MainWindow` visibility/transform wiring only),
      so no new unit tests were added either.
- **Not yet verified:** the transform/visibility changes haven't been seen
      rendered, same interactive-desktop gap as every other `MainWindow`
      change in this file.

**Phase 6 — Virtual Attendant, Survey question-builder, visual Screen
Editor (added to scope 2026-08-31, was previously the open question below)**

- **Virtual Attendant** — per-stage audio/video cues, not a new
      `BoothState` (the existing states already mark every stage dslrBooth
      cues: Consent, Countdown, Capturing, Reviewing, Printing, Complete).
  - [x] New `VirtualAttendantClip` table (`LocationId`, `Stage`, `FilePath`,
        `SortOrder`) -- a list per stage, not a single settings row, since
        dslrBooth's Randomize toggle needs a pool to pick from.
  - [x] `VirtualAttendantSettings` record on `BoothSettings` (`Enabled`,
        `Style`, per-stage `Randomize` flags).
  - [x] `IVirtualAttendantService` (interface + mock, same seam as
        everything else) -- `BoothStateMachine` calls it once per
        `SetState`, it picks (or randomizes) a clip for that stage and
        raises a new `AttendantCueChanged` event; `MainWindow` plays the
        clip alongside whatever screen is already showing. Purely
        additive to existing state transitions, no new states.
- **Virtual Attendant — done 2026-08-31.** `Randomize` is six fixed bool
      properties on `VirtualAttendantSettings` (`RandomizeConsent` ...
      `RandomizeComplete`), not a `Dictionary<string,bool>` -- the cue-worthy
      stages are a small, fixed set (matches `ScreenSettings`/`EffectsSettings`'s
      fixed-property style, not a bag of flags). `BoothStateMachine.SetState` now
      fires `_ = FireAttendantCueAsync(state)` after `StateChanged?.Invoke` --
      fire-and-forget, wrapped in its own try/catch (same "best-effort, never
      disrupts a session" shape the Feedback/Guestbook blocks already use), so a
      slow or failing cue lookup can never delay or interrupt a transition.
      `MockVirtualAttendantService` defaults to disabled with an empty clip pool
      (matches a fresh table); `SqlVirtualAttendantService` (real, not mocked --
      same reasoning `UiFeedbackService` is real, since picking a clip and
      reading settings needs no external hardware/credentials) reads settings +
      pool fresh on every call, same "next guest, no restart" cadence as
      everything else. `MainWindow` plays the cue via a new zero-size
      `MediaElement` (`LoadedBehavior`/`UnloadedBehavior="Manual"`, driven purely
      by `PlayAttendantCue`) placed outside the state-bound views so it can play
      alongside whatever screen is showing, exactly as scoped. Admin UI for
      managing the clip pool itself (upload/reorder/assign-to-stage) is **not
      built** -- the scope text's checklist doesn't ask for one (unlike Survey's
      explicit "Admin UI" bullet below), so clips must be inserted directly for
      now; noted as an open gap.
- **Survey question-builder** — dslrBooth's "+ Question"/"View Responses"
      screen, currently just the `SurveySettings.Enabled` on/off switch
      from Phase 1.
  - [x] New `SurveyQuestion` (`LocationId`, `Text`, `SortOrder`) and
        `SurveyResponse` (`SessionId`, `SurveyQuestionId`, `Answer`) tables.
  - [x] `ISurveyService` (interface + mock) -- `GetActiveQuestionsAsync`,
        `RecordResponsesAsync`. New `BoothState.Survey`, shown after
        `Feedback` (same "best-effort, wrapped in its own try/catch, never
        turns a completed session into an Error one" pattern the Feedback
        step already uses) and skipped entirely when
        `SurveySettings.Enabled` is off or there are no active questions
        -- same "empty table = feature invisible" reasoning `Frame`/
        `FramePicker` already established.
  - [x] Admin UI: `+ Question` add/remove list and a `View Responses`
        list, in `AdminWindow`'s new Survey section from Phase 3.
- **Survey question-builder — done 2026-08-31.** `ISurveyService` ended up
      with a third method, `CollectAnswersAsync(questions)`, beyond the scope
      text's literal two -- `BoothStateMachine` needs something to await for
      the guest's actual answers before it has anything to hand
      `RecordResponsesAsync`, same "state waits on a service call" shape
      `IFeedbackService.CollectAsync`/`IFrameSelectionService.SelectFrameAsync`
      already use. `SqlSurveyService` (real, in `Photobooth.Data` rather than
      `Photobooth.Core` like `UiFeedbackService`, since this one also needs
      direct SQL access for `GetActiveQuestionsAsync`/`RecordResponsesAsync`)
      bridges the wait to WPF via the same `TaskCompletionSource` handoff
      `UiFeedbackService` established, raising `AnswersRequested` for
      `MainWindow` to show `SurveyView`. `BoothStateMachine.RunSessionAsync`
      runs the Survey block right after the existing Feedback
      try/catch, inside its own try/catch, gated by `settings.Survey.Enabled
      && questions.Count > 0` -- mirrors Feedback's shape exactly, including
      "the state still shows, recording is just conditional" for a guest who
      taps Skip. `MainWindow.SurveyView` renders one `TextBlock`+`TextBox`
      pair per active question via `ShowSurveyQuestions`, and Submit/Skip both
      call `_survey.SubmitAnswers` (empty list on Skip) to complete the
      pending `TaskCompletionSource`. `AdminWindow`'s existing Survey section
      (Phase 3's `SurveyEnabledCheckBox`) now also has a `+ Question`
      add/delete `ItemsControl` (saved immediately via `SurveyRepository`,
      same as the Frame library) and a read-only `View Responses` list
      (`SurveyRepository.GetResponsesByLocationAsync`, joined to question text,
      newest first).
- **Visual Screen Editor** — dslrBooth's drag/resize canvas for Welcome/
      Capture/Sharing screens. Mapped onto the same
      percent-of-canvas element model `PrintTemplateElement` /
      `PrintTemplateEditorWindow` already use for prints, not a new UI
      paradigm.
  - [x] New `ScreenTemplateElement` table (`LocationId`, `Screen` --
        `'Welcome'|'Capture'|'Sharing'`, `Kind` -- `'Text'|'Image'|'Shape'`,
        `X/Y/Width/HeightPercent`, plus the same text/image/font/color
        columns `PrintTemplateElement` already has).
  - [x] New `ScreenTemplateEditorWindow` (WPF), sibling to
        `PrintTemplateEditorWindow`, reusing its drag/resize/percent-math
        approach across three tabs (Welcome/Capture/Sharing) instead of
        one canvas.
  - [x] `MainWindow`'s `Idle`/`Countdown`/sharing views render the saved
        elements as an overlay on top of the existing state-bound
        controls, the same way `PrintCompositor` overlays
        `PrintTemplateElement` rows onto the captured photo at print time.
- **Visual Screen Editor — done 2026-08-31.** `ScreenTemplateElement`
      (`Photobooth.Core`) mirrors `PrintTemplateElement`'s column set plus
      `Screen` (`Welcome`/`Capture`/`Sharing` -- `Capture` maps to
      `CountdownView`, the only screen with a live camera feed, same mapping
      Phase 5 already established for `ScreenSettings`; `Sharing` maps to the
      post-`Complete` step, since there's no literal `BoothState.Sharing`) and
      a third `Kind`, `Shape` (a plain color rectangle, reusing `ColorHex` as
      its fill -- no separate `ShapeColorHex` column, same "one Kind,
      differently-used fields" shape `PrintTemplateElementKind` already
      established for `Text` vs `Logo`). `ScreenTemplateElementRepository`
      (`Photobooth.Data`) mirrors `PrintTemplateElementRepository`'s
      delete-then-reinsert `ReplaceAllAsync`, just across all three screens'
      rows in one transaction instead of one screen's.
      `ScreenTemplateEditorWindow` reuses `PrintTemplateEditorWindow`'s exact
      drag/resize/percent-math handlers (`ElementContainer_MouseLeftButtonDown/
      Move/Up`, `Handle_Mouse*`, `PositionVisual`) verbatim, copied rather than
      factored into a shared base class -- but deliberately does **not**
      triple the canvas: a `TabControl` used purely as a screen selector (no
      per-tab content) swaps which screen's `List<ScreenTemplateElement>` one
      shared `ElementsCanvas` is showing/editing
      (`_elementsByScreen[_activeScreen]`, rebuilt via `LoadActiveScreen` on
      `ScreenTabControl_SelectionChanged`), so there's one drag/resize call
      site, not three. This trims Phase 4's layer-reorder/z-order/alignment
      buttons and `PrintCompositor`-backed rendered preview -- neither is in
      the Phase 6 scope text's checklist (only "drag/resize/percent-math" is),
      and `ElementsCanvas` here is the live view itself (placed WPF elements),
      not a bitmap composited from a captured photo, so there's nothing for a
      `PrintCompositor`-style preview renderer to do. `AdminWindow` gets one
      new "Edit screen layout..." button (Settings section, next to "Edit
      print template...") opening the editor with the location's existing
      elements loaded.
      `MainWindow` renders the live overlay via three `IsHitTestVisible="False"`
      `Canvas` elements (`WelcomeOverlayCanvas`/`CaptureOverlayCanvas`/
      `SharingOverlayCanvas`) layered into the same state-bound `Grid` as every
      other view, each `Visibility`-bound to its corresponding view
      (`{Binding Visibility, ElementName=IdleView}` etc.) so it only shows
      when that screen does; `IsHitTestVisible="False"` so a placed element
      can never steal a guest's tap from the real controls underneath (e.g.
      `Surface_MouseLeftButtonUp`'s tap-to-start on `Idle`). `RenderScreenOverlay`
      positions `TextBlock`/`Image`/`Rectangle` by percent of the canvas's own
      `ActualWidth`/`ActualHeight` directly in code-behind -- the "WPF-live-
      rendering equivalent" of `PrintCompositor`'s percent-of-cell math the
      task brief asked for, not a bitmap composite, since these are live
      interactive screens. Elements are fetched fresh in `ApplyThemeAsync`
      (same "next guest, no restart" cadence as the theme/settings reads
      already there), and each canvas also re-renders on its own `SizeChanged`
      (`ActualWidth`/`Height` are 0 until the first layout pass, so
      `ApplyThemeAsync`'s very first call would otherwise render into an
      empty canvas).
      `ScreenTemplateElementTests.cs` covers `ScreenTemplateElement.IsValid`
      (same shape as `PrintTemplateElementTests`, plus a case for `Shape`
      needing neither `Text` nor `ImagePath`) -- the only pure logic this
      sub-feature has; `ScreenTemplateEditorWindow`'s drag/resize wiring and
      `MainWindow`'s overlay rendering are UI-only, same "no direct unit
      tests, mouse/UI wiring isn't something a unit test can exercise" gap
      `PrintTemplateEditorWindow` already carries -- **not yet seen rendered
      or clicked through**, same interactive-desktop gap as every WPF screen
      in this project.

**Build order (updated 2026-08-31):** Phase 1 (done) → Phase 2 (done) →
Phase 3 (done) → Phase 5 (done) → Phase 4 (done) → Phase 6 (done). All
six phases of this build plan are now complete. Phase 6 sat after Phase 4
because all three of its pieces extend patterns Phases 3-4 establish first
(settings sections, the percent-of-canvas editor) -- confirmed true in
practice: Virtual Attendant reused `BoothSettings`'s nested-record-per-
section shape, Survey extended Phase 3's `AdminWindow` section instead of
duplicating it, and the Visual Screen Editor reused Phase 4's
`PrintTemplateEditorWindow` drag/resize math directly rather than
re-deriving it. Every phase's own entry above still carries its own
"not yet verified" interactive-desktop caveats where they apply (mouse/UI
wiring across `PrintTemplateEditorWindow`, `ScreenTemplateEditorWindow`,
and `MainWindow`'s screen-specific rendering) -- those remain open gaps
for whoever next runs this app on a real touchscreen, not a re-opened
part of this build plan's own checklist.
