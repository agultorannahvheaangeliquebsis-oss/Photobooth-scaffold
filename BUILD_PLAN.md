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
- [ ] Video guestbook: recorded personalized messages/greetings from guests.

**Customization & design**
- [~] Built-in print template editor: 4x6, 2x6 strip, custom dimensions,
      logos, text, graphics. Paper size and Single/Strip layout are done
      -- see `PrintTemplate` in the Done section above. Logos/text/graphics
      overlap with `IPhotoBrandingService`'s caption bar (already done, see
      "digital branding overlay" above) and `IFrameOverlayService`'s frame
      art (also done); a dedicated visual editor for arbitrary
      logo/text/graphic placement is still unbuilt.
- [ ] Screen & UI customization: start screen, buttons, backgrounds,
      themes per event brand.
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
