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
- **Not yet wired:** real Firebase Storage backend — needs a Firebase
  project + service account credentials.

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
- [ ] Set up a Firebase project + service account, wire a real
      `FirebaseCloudUploadService`.
- Both remaining items are gated on something only you can provide (the
  D3500 being plugged in; Firebase credentials) — if either isn't ready,
  it slips to the Day 7 buffer.

**Day 3 — Persistence layer**
- New `Photobooth.Data` project, LocalDB (schema.sql is already T-SQL).
- Repository methods for the six tables; `BoothStateMachine` inserts/
  updates a `Session` row per run, a `Print` row on successful print, a
  `Payment` row for vendo mode.
- Seed a `Location` and a couple `Booking` rows so the FK chain isn't
  empty.

**Day 4 — Test project**
- `Photobooth.Tests` (xunit): state machine transitions (including the
  forced-failure path), repository methods, mock camera/printer/cloud
  paths. Zero coverage exists today.

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
