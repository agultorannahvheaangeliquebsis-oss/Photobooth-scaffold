# Photobooth Business Management System

A system built to run an actual photobooth rental business — capture, print,
and revenue tracking across two operating modes: **event** (attended, flat
fee) and **vendo** (unattended, pay-per-print).

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
- [ ] WPF UI bound to the state machine
- [ ] Real camera integration (Canon EDSDK — pending Developer Program approval)
- [ ] Real printer integration via Windows print spooler
- [ ] Admin dashboard (sales, inventory alerts)

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
demoed before Canon Developer Program approval (needed for EDSDK) even
comes through. Swapping in the real camera later is a one-line change at
the composition root, not a rewrite.
