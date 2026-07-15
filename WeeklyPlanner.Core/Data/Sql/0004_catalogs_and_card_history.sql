-- Schema v4 - Cataloghi configurabili e storico funzionale delle card
-- Introduce priorità, tipologie, regole di scadenza, metadati stabili delle card
-- e un audit trail applicativo persistente e indipendente dai log tecnici.

CREATE TABLE Priorities (
    Id INTEGER NOT NULL PRIMARY KEY,
    Code TEXT NOT NULL COLLATE NOCASE,
    Name TEXT NOT NULL,
    Description TEXT,
    DefaultDueHours INTEGER NOT NULL CHECK (DefaultDueHours > 0),
    SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    IsDefault INTEGER NOT NULL DEFAULT 0 CHECK (IsDefault IN (0, 1)),
    Version INTEGER NOT NULL DEFAULT 1 CHECK (Version > 0)
);

CREATE UNIQUE INDEX UX_Priorities_Code ON Priorities(Code);
CREATE UNIQUE INDEX UX_Priorities_Name ON Priorities(Name COLLATE NOCASE);
CREATE UNIQUE INDEX UX_Priorities_Default
ON Priorities(IsDefault)
WHERE IsDefault = 1;

CREATE TABLE CardTypes (
    Id INTEGER NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL COLLATE NOCASE,
    ColorHex TEXT NOT NULL CHECK (
        length(ColorHex) = 7
        AND substr(ColorHex, 1, 1) = '#'
        AND substr(ColorHex, 2) NOT GLOB '*[^0-9A-Fa-f]*'
    ),
    SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    IsDefault INTEGER NOT NULL DEFAULT 0 CHECK (IsDefault IN (0, 1)),
    Version INTEGER NOT NULL DEFAULT 1 CHECK (Version > 0)
);

CREATE UNIQUE INDEX UX_CardTypes_Name ON CardTypes(Name);
CREATE UNIQUE INDEX UX_CardTypes_Default
ON CardTypes(IsDefault)
WHERE IsDefault = 1;

CREATE TABLE PriorityTypeDeadlines (
    PriorityId INTEGER NOT NULL REFERENCES Priorities(Id) ON DELETE CASCADE,
    CardTypeId INTEGER NOT NULL REFERENCES CardTypes(Id) ON DELETE CASCADE,
    DueHours INTEGER NOT NULL CHECK (DueHours > 0),
    Version INTEGER NOT NULL DEFAULT 1 CHECK (Version > 0),
    PRIMARY KEY (PriorityId, CardTypeId)
);

INSERT INTO Priorities
    (Id, Code, Name, Description, DefaultDueHours, SortOrder, IsActive, IsDefault, Version)
VALUES
    (1, 'U', 'Urgente', 'Da eseguire nel più breve tempo possibile e comunque entro 72 ore.', 72, 0, 1, 0, 1),
    (2, 'B', 'Breve', 'Da garantire entro 10 giorni.', 240, 1, 1, 0, 1),
    (3, 'D', 'Differibile', 'Entro 30 giorni, salvo regole specifiche della tipologia.', 720, 2, 1, 0, 1),
    (4, 'P', 'Programmabile', 'Da erogare entro 120 giorni.', 2880, 3, 1, 0, 1);

INSERT INTO CardTypes
    (Id, Name, ColorHex, SortOrder, IsActive, IsDefault, Version)
VALUES
    (1, 'WinCliente', '#2563EB', 0, 1, 0, 1),
    (2, 'Report', '#7C3AED', 1, 1, 0, 1),
    (3, 'SQL', '#059669', 2, 1, 0, 1),
    (4, 'Visita', '#D97706', 3, 1, 0, 1),
    (5, 'Esame strumentale', '#DC2626', 4, 1, 0, 1);

INSERT INTO PriorityTypeDeadlines (PriorityId, CardTypeId, DueHours, Version)
SELECT priority.Id, cardType.Id, 1440, 1
FROM Priorities priority
JOIN CardTypes cardType
  ON cardType.Name = 'Esame strumentale'
WHERE priority.Code = 'D';

ALTER TABLE Cards ADD COLUMN StableId TEXT;
ALTER TABLE Cards ADD COLUMN CreatedAtUtc TEXT;
ALTER TABLE Cards ADD COLUMN CreatedAtIsEstimated INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Cards ADD COLUMN PriorityId INTEGER REFERENCES Priorities(Id);
ALTER TABLE Cards ADD COLUMN CardTypeId INTEGER REFERENCES CardTypes(Id);
ALTER TABLE Cards ADD COLUMN PriorityAssignedAtUtc TEXT;
ALTER TABLE Cards ADD COLUMN DueAtUtc TEXT;

UPDATE Cards
SET StableId = lower(hex(randomblob(16))),
    CreatedAtUtc = UpdatedAtUtc,
    CreatedAtIsEstimated = 1
WHERE StableId IS NULL OR length(trim(StableId)) = 0;

CREATE UNIQUE INDEX UX_Cards_StableId ON Cards(StableId);
CREATE INDEX IX_Cards_PriorityId ON Cards(PriorityId);
CREATE INDEX IX_Cards_CardTypeId ON Cards(CardTypeId);
CREATE INDEX IX_Cards_DueAtUtc ON Cards(DueAtUtc);

CREATE TRIGGER TR_Cards_StableId_Required_BeforeInsert
BEFORE INSERT ON Cards
WHEN NEW.StableId IS NULL OR length(trim(NEW.StableId)) = 0
BEGIN
    SELECT RAISE(ABORT, 'Cards.StableId is required');
END;

CREATE TRIGGER TR_Cards_StableId_Immutable_BeforeUpdate
BEFORE UPDATE OF StableId ON Cards
WHEN NEW.StableId IS NULL
  OR length(trim(NEW.StableId)) = 0
  OR NEW.StableId <> OLD.StableId
BEGIN
    SELECT RAISE(ABORT, 'Cards.StableId is required and immutable');
END;

CREATE TABLE CardEvents (
    Id INTEGER NOT NULL PRIMARY KEY,
    CardStableId TEXT NOT NULL,
    CardId INTEGER REFERENCES Cards(Id) ON DELETE SET NULL,
    EventType TEXT NOT NULL,
    OccurredAtUtc TEXT NOT NULL,
    UserName TEXT NOT NULL,
    SessionId TEXT,
    MachineName TEXT,
    Summary TEXT NOT NULL,
    DataJson TEXT NOT NULL,
    FormatVersion INTEGER NOT NULL DEFAULT 1 CHECK (FormatVersion > 0)
);

CREATE INDEX IX_CardEvents_CardStableId_Id
ON CardEvents(CardStableId, Id DESC);
CREATE INDEX IX_CardEvents_CardId_Id
ON CardEvents(CardId, Id DESC);
CREATE INDEX IX_CardEvents_OccurredAtUtc
ON CardEvents(OccurredAtUtc DESC);

INSERT INTO CardEvents
    (CardStableId, CardId, EventType, OccurredAtUtc, UserName, SessionId, MachineName, Summary, DataJson, FormatVersion)
SELECT
    StableId,
    Id,
    'Imported',
    COALESCE(CreatedAtUtc, UpdatedAtUtc),
    COALESCE(NULLIF(trim(CreatedBy), ''), NULLIF(trim(UpdatedBy), ''), 'Sconosciuto'),
    NULL,
    NULL,
    'Card esistente acquisita durante la migrazione allo schema v4.',
    '{"createdAtIsEstimated":true}',
    1
FROM Cards;

-- Le variazioni dei cataloghi e delle colonne sono parte dello snapshot della board.
-- I dati iniziali sono inseriti prima dei trigger, così un database nuovo parte da Revision = 0.
CREATE TRIGGER TR_Priorities_BoardRevision_AfterInsert
AFTER INSERT ON Priorities
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_Priorities_BoardRevision_AfterUpdate
AFTER UPDATE ON Priorities
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_Priorities_BoardRevision_AfterDelete
AFTER DELETE ON Priorities
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;

CREATE TRIGGER TR_CardTypes_BoardRevision_AfterInsert
AFTER INSERT ON CardTypes
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_CardTypes_BoardRevision_AfterUpdate
AFTER UPDATE ON CardTypes
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_CardTypes_BoardRevision_AfterDelete
AFTER DELETE ON CardTypes
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;

CREATE TRIGGER TR_PriorityTypeDeadlines_BoardRevision_AfterInsert
AFTER INSERT ON PriorityTypeDeadlines
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_PriorityTypeDeadlines_BoardRevision_AfterUpdate
AFTER UPDATE ON PriorityTypeDeadlines
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_PriorityTypeDeadlines_BoardRevision_AfterDelete
AFTER DELETE ON PriorityTypeDeadlines
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;

CREATE TRIGGER TR_Columns_BoardRevision_AfterInsert
AFTER INSERT ON Columns
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_Columns_BoardRevision_AfterUpdate
AFTER UPDATE ON Columns
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
CREATE TRIGGER TR_Columns_BoardRevision_AfterDelete
AFTER DELETE ON Columns
BEGIN
    UPDATE BoardState SET Revision = Revision + 1 WHERE Id = 1;
END;
