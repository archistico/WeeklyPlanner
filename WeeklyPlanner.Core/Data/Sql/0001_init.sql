-- Schema v1 - WeeklyPlanner
-- Applicato una sola volta alla creazione del file DB locale.
-- Vedi §8.3-8.4 del documento di progetto per le note di design.

CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Columns (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    SortOrder INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Cards (
    Id INTEGER PRIMARY KEY,
    ColumnId INTEGER NOT NULL REFERENCES Columns(Id),
    Title TEXT NOT NULL,
    Notes TEXT,
    SortOrder INTEGER NOT NULL,
    CreatedBy TEXT,
    UpdatedBy TEXT,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Cards_ColumnId ON Cards(ColumnId);
CREATE INDEX IF NOT EXISTS IX_Cards_UpdatedAtUtc ON Cards(UpdatedAtUtc);

-- Colonne di default: Backlog + Lun..Dom
INSERT INTO Columns (Id, Name, SortOrder)
SELECT 0, 'Backlog', 0
WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 0);

INSERT INTO Columns (Id, Name, SortOrder)
SELECT 1, 'Lunedì', 1 WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 1);
INSERT INTO Columns (Id, Name, SortOrder)
SELECT 2, 'Martedì', 2 WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 2);
INSERT INTO Columns (Id, Name, SortOrder)
SELECT 3, 'Mercoledì', 3 WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 3);
INSERT INTO Columns (Id, Name, SortOrder)
SELECT 4, 'Giovedì', 4 WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 4);
INSERT INTO Columns (Id, Name, SortOrder)
SELECT 5, 'Venerdì', 5 WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 5);
INSERT INTO Columns (Id, Name, SortOrder)
SELECT 6, 'Sabato', 6 WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 6);
INSERT INTO Columns (Id, Name, SortOrder)
SELECT 7, 'Domenica', 7 WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 7);

