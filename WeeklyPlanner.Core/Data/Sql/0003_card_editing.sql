-- Schema v3 - Editing protetto e concorrenza ottimistica
-- Version protegge da aggiornamenti concorrenti; CardEditLocks implementa lease applicativi
-- brevi, rinnovabili e recuperabili automaticamente dopo crash o chiusure anomale.

ALTER TABLE Cards
ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;

CREATE TABLE CardEditLocks (
    CardId INTEGER NOT NULL PRIMARY KEY REFERENCES Cards(Id) ON DELETE CASCADE,
    SessionId TEXT NOT NULL,
    UserName TEXT NOT NULL,
    MachineName TEXT,
    AcquiredAtUtc TEXT NOT NULL,
    LastHeartbeatUtc TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL
);

CREATE INDEX IX_CardEditLocks_ExpiresAtUtc ON CardEditLocks(ExpiresAtUtc);

-- Acquisizione e rilascio dei lock devono essere osservati dal polling. Il rinnovo periodico
-- non incrementa la revisione: non cambia lo stato visibile e non deve provocare un merge
-- completo della board ogni 10 secondi. La scadenza viene comunque verificata a ogni polling.
CREATE TRIGGER TR_CardEditLocks_BoardRevision_AfterInsert
AFTER INSERT ON CardEditLocks
BEGIN
    UPDATE BoardState
    SET Revision = Revision + 1
    WHERE Id = 1;
END;

CREATE TRIGGER TR_CardEditLocks_BoardRevision_AfterDelete
AFTER DELETE ON CardEditLocks
BEGIN
    UPDATE BoardState
    SET Revision = Revision + 1
    WHERE Id = 1;
END;
