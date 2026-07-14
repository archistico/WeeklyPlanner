-- Schema v2 - Revisione monotona della board
-- La revisione viene incrementata da trigger nella stessa transazione della modifica alle card.
-- Non dipende dall'orologio del processo e rileva anche le cancellazioni.

CREATE TABLE IF NOT EXISTS BoardState (
    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    Revision INTEGER NOT NULL CHECK (Revision >= 0)
);

INSERT INTO BoardState (Id, Revision)
SELECT 1, 0
WHERE NOT EXISTS (SELECT 1 FROM BoardState WHERE Id = 1);

CREATE TRIGGER IF NOT EXISTS TR_Cards_BoardRevision_AfterInsert
AFTER INSERT ON Cards
BEGIN
    UPDATE BoardState
    SET Revision = Revision + 1
    WHERE Id = 1;
END;

CREATE TRIGGER IF NOT EXISTS TR_Cards_BoardRevision_AfterUpdate
AFTER UPDATE ON Cards
BEGIN
    UPDATE BoardState
    SET Revision = Revision + 1
    WHERE Id = 1;
END;

CREATE TRIGGER IF NOT EXISTS TR_Cards_BoardRevision_AfterDelete
AFTER DELETE ON Cards
BEGIN
    UPDATE BoardState
    SET Revision = Revision + 1
    WHERE Id = 1;
END;
