-- Photobooth business management system
-- Schema covers both event-mode (attended, flat fee) and vendo-mode
-- (unattended, pay-per-print) sessions on the same underlying tables.

CREATE TABLE Location (
    LocationId      INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(100)   NOT NULL,
    Type            NVARCHAR(20)    NOT NULL CHECK (Type IN ('event', 'vendo')),
    Address         NVARCHAR(255)   NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Printer (
    PrinterId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    Model           NVARCHAR(100)   NOT NULL,     -- e.g. 'Canon Selphy CP1500', 'DNP DS620A'
    SerialNumber    NVARCHAR(100)   NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'active'
                        CHECK (Status IN ('active', 'offline', 'maintenance')),
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Booking (
    BookingId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    ClientName      NVARCHAR(150)   NOT NULL,
    EventDate       DATE            NOT NULL,
    PackageType     NVARCHAR(50)    NOT NULL,     -- 'Starter', 'Standard', 'Premium'
    Price           DECIMAL(10,2)   NOT NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'confirmed'
                        CHECK (Status IN ('inquiry', 'confirmed', 'completed', 'cancelled')),
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Session (
    SessionId       INT IDENTITY(1,1) PRIMARY KEY,
    LocationId      INT             NOT NULL REFERENCES Location(LocationId),
    BookingId       INT             NULL REFERENCES Booking(BookingId),  -- NULL for vendo sessions
    Mode            NVARCHAR(20)    NOT NULL CHECK (Mode IN ('event', 'vendo')),
    StartedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    EndedAt         DATETIME2       NULL,
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'in_progress'
                        CHECK (Status IN ('in_progress', 'completed', 'abandoned', 'error'))
);

CREATE TABLE Print (
    PrintId         INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    PrinterId       INT             NOT NULL REFERENCES Printer(PrinterId),
    PrintedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    TemplateUsed    NVARCHAR(100)   NULL,
    FilePath        NVARCHAR(500)   NULL          -- local path to the composited image
);

CREATE TABLE Payment (
    PaymentId       INT IDENTITY(1,1) PRIMARY KEY,
    SessionId       INT             NOT NULL REFERENCES Session(SessionId),
    Amount          DECIMAL(10,2)   NOT NULL,
    Method          NVARCHAR(30)    NOT NULL,     -- 'qr_gcash', 'qr_maya', 'card', 'free_event'
    Status          NVARCHAR(20)    NOT NULL DEFAULT 'pending'
                        CHECK (Status IN ('pending', 'paid', 'refunded', 'failed')),
    TransactionRef  NVARCHAR(100)   NULL,
    PaidAt          DATETIME2       NULL
);

CREATE TABLE InventoryLog (
    InventoryId     INT IDENTITY(1,1) PRIMARY KEY,
    PrinterId       INT             NOT NULL REFERENCES Printer(PrinterId),
    ItemType        NVARCHAR(20)    NOT NULL CHECK (ItemType IN ('paper', 'ink', 'ribbon')),
    QuantityRemaining INT           NOT NULL,
    LoggedAt        DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Helpful indexes for the dashboard queries you'll write later
CREATE INDEX IX_Session_Location_Mode ON Session(LocationId, Mode);
CREATE INDEX IX_Print_Session ON Print(SessionId);
CREATE INDEX IX_Payment_Session ON Payment(SessionId);
CREATE INDEX IX_InventoryLog_Printer_LoggedAt ON InventoryLog(PrinterId, LoggedAt DESC);
