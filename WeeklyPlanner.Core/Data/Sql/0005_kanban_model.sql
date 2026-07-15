-- Schema v5 - Modello kanban a swimlane
-- Trasforma il planner settimanale in un workflow fisso con cinque stati e introduce
-- la tipologia di sistema "Generica", obbligatoria per ogni card.

ALTER TABLE Columns ADD COLUMN SystemKey TEXT;
ALTER TABLE Columns ADD COLUMN IsSystem INTEGER NOT NULL DEFAULT 0 CHECK (IsSystem IN (0, 1));

CREATE UNIQUE INDEX UX_Columns_SystemKey
ON Columns(SystemKey)
WHERE SystemKey IS NOT NULL;

-- Memorizzare il vecchio posizionamento prima di trasformare le colonne.
CREATE TEMP TABLE KanbanCardMigration AS
SELECT
    card.Id AS CardId,
    card.StableId AS CardStableId,
    card.ColumnId AS PreviousColumnId,
    columnDefinition.Name AS PreviousColumnName,
    CASE WHEN card.ColumnId = 0 THEN 0 ELSE 1 END AS NewColumnId,
    ROW_NUMBER() OVER
    (
        PARTITION BY CASE WHEN card.ColumnId = 0 THEN 0 ELSE 1 END
        ORDER BY columnDefinition.SortOrder, card.SortOrder, card.Id
    ) - 1 AS NewSortOrder,
    card.UpdatedAtUtc AS OccurredAtUtc,
    COALESCE(NULLIF(trim(card.UpdatedBy), ''), NULLIF(trim(card.CreatedBy), ''), 'Sconosciuto') AS UserName
FROM Cards card
JOIN Columns columnDefinition ON columnDefinition.Id = card.ColumnId;

-- Evitare revisioni multiple dovute alla sola ricostruzione del catalogo colonne.
DROP TRIGGER IF EXISTS TR_Columns_BoardRevision_AfterInsert;
DROP TRIGGER IF EXISTS TR_Columns_BoardRevision_AfterUpdate;
DROP TRIGGER IF EXISTS TR_Columns_BoardRevision_AfterDelete;

INSERT INTO Columns (Id, Name, SortOrder, SystemKey, IsSystem)
SELECT 0, 'BACKLOG', 0, 'backlog', 1
WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 0);

INSERT INTO Columns (Id, Name, SortOrder, SystemKey, IsSystem)
SELECT 1, 'TODO', 1, 'todo', 1
WHERE NOT EXISTS (SELECT 1 FROM Columns WHERE Id = 1);

UPDATE Columns
SET Name = 'BACKLOG',
    SortOrder = 0,
    SystemKey = 'backlog',
    IsSystem = 1
WHERE Id = 0;

UPDATE Columns
SET Name = 'TODO',
    SortOrder = 1,
    SystemKey = 'todo',
    IsSystem = 1
WHERE Id = 1;

UPDATE Cards
SET ColumnId =
    (
        SELECT migration.NewColumnId
        FROM KanbanCardMigration migration
        WHERE migration.CardId = Cards.Id
    ),
    SortOrder =
    (
        SELECT migration.NewSortOrder
        FROM KanbanCardMigration migration
        WHERE migration.CardId = Cards.Id
    )
WHERE EXISTS
(
    SELECT 1
    FROM KanbanCardMigration migration
    WHERE migration.CardId = Cards.Id
);

INSERT INTO CardEvents
    (CardStableId, CardId, EventType, OccurredAtUtc, UserName,
     SessionId, MachineName, Summary, DataJson, FormatVersion)
SELECT
    migration.CardStableId,
    migration.CardId,
    'WorkflowMigrated',
    migration.OccurredAtUtc,
    migration.UserName,
    NULL,
    NULL,
    'Workflow migrato da ' || migration.PreviousColumnName || ' a TODO.',
    json_object(
        'previousColumnId', migration.PreviousColumnId,
        'previousColumnName', migration.PreviousColumnName,
        'columnId', 1,
        'columnKey', 'todo',
        'index', migration.NewSortOrder),
    1
FROM KanbanCardMigration migration
WHERE migration.PreviousColumnId <> 0;

DELETE FROM Columns WHERE Id NOT IN (0, 1);

INSERT INTO Columns (Id, Name, SortOrder, SystemKey, IsSystem)
VALUES
    (2, 'IN PROGRESS', 2, 'in_progress', 1),
    (3, 'TESTING', 3, 'testing', 1),
    (4, 'DONE', 4, 'done', 1);

DROP TABLE KanbanCardMigration;

ALTER TABLE CardTypes ADD COLUMN SystemKey TEXT;
ALTER TABLE CardTypes ADD COLUMN IsSystem INTEGER NOT NULL DEFAULT 0 CHECK (IsSystem IN (0, 1));

-- La riclassificazione iniziale non è una modifica utente osservabile dal polling.
DROP TRIGGER IF EXISTS TR_CardTypes_BoardRevision_AfterInsert;
DROP TRIGGER IF EXISTS TR_CardTypes_BoardRevision_AfterUpdate;
DROP TRIGGER IF EXISTS TR_CardTypes_BoardRevision_AfterDelete;

CREATE UNIQUE INDEX UX_CardTypes_SystemKey
ON CardTypes(SystemKey)
WHERE SystemKey IS NOT NULL;

UPDATE CardTypes
SET SortOrder = SortOrder + 1,
    IsDefault = 0;

-- Se l'utente aveva già creato una tipologia "Generica", promuoverla a fascia
-- di sistema invece di crearne un duplicato e conservare così le associazioni.
UPDATE CardTypes
SET Name = 'Generica',
    SortOrder = 0,
    IsActive = 1,
    IsDefault = 1,
    SystemKey = 'generic',
    IsSystem = 1,
    Version = Version + 1
WHERE Id =
(
    SELECT Id
    FROM CardTypes
    WHERE Name = 'Generica' COLLATE NOCASE
    ORDER BY Id
    LIMIT 1
);

INSERT INTO CardTypes
    (Name, ColorHex, SortOrder, IsActive, IsDefault, Version, SystemKey, IsSystem)
SELECT
    'Generica', '#64748B', 0, 1, 1, 1, 'generic', 1
WHERE NOT EXISTS
(
    SELECT 1
    FROM CardTypes
    WHERE SystemKey = 'generic'
);

INSERT INTO CardEvents
    (CardStableId, CardId, EventType, OccurredAtUtc, UserName,
     SessionId, MachineName, Summary, DataJson, FormatVersion)
SELECT
    card.StableId,
    card.Id,
    'TypeMigrated',
    card.UpdatedAtUtc,
    COALESCE(NULLIF(trim(card.UpdatedBy), ''), NULLIF(trim(card.CreatedBy), ''), 'Sconosciuto'),
    NULL,
    NULL,
    'Tipologia di sistema Generica assegnata durante la migrazione al kanban.',
    json_object(
        'cardTypeId', genericType.Id,
        'cardTypeKey', 'generic'),
    1
FROM Cards card
CROSS JOIN CardTypes genericType
WHERE genericType.SystemKey = 'generic'
  AND card.CardTypeId IS NULL;

UPDATE Cards
SET CardTypeId =
    (
        SELECT Id
        FROM CardTypes
        WHERE SystemKey = 'generic'
    )
WHERE CardTypeId IS NULL;

CREATE TRIGGER TR_Cards_CardType_Required_BeforeInsert
BEFORE INSERT ON Cards
WHEN NEW.CardTypeId IS NULL
BEGIN
    SELECT RAISE(ABORT, 'Cards.CardTypeId is required');
END;

CREATE TRIGGER TR_Cards_CardType_Required_BeforeUpdate
BEFORE UPDATE OF CardTypeId ON Cards
WHEN NEW.CardTypeId IS NULL
BEGIN
    SELECT RAISE(ABORT, 'Cards.CardTypeId is required');
END;

CREATE TRIGGER TR_Columns_System_Delete_Blocked
BEFORE DELETE ON Columns
WHEN OLD.IsSystem = 1
BEGIN
    SELECT RAISE(ABORT, 'System workflow columns cannot be deleted');
END;

CREATE TRIGGER TR_Columns_System_Update_Blocked
BEFORE UPDATE OF Name, SortOrder, SystemKey, IsSystem ON Columns
WHEN OLD.IsSystem = 1
  AND
  (
      NEW.Name IS NOT OLD.Name
      OR NEW.SortOrder IS NOT OLD.SortOrder
      OR NEW.SystemKey IS NOT OLD.SystemKey
      OR NEW.IsSystem IS NOT OLD.IsSystem
  )
BEGIN
    SELECT RAISE(ABORT, 'System workflow columns cannot be modified');
END;

CREATE TRIGGER TR_CardTypes_Generic_Delete_Blocked
BEFORE DELETE ON CardTypes
WHEN OLD.SystemKey = 'generic'
BEGIN
    SELECT RAISE(ABORT, 'The generic card type cannot be deleted');
END;

CREATE TRIGGER TR_CardTypes_Generic_Update_Blocked
BEFORE UPDATE OF Name, SortOrder, IsActive, IsDefault, SystemKey, IsSystem ON CardTypes
WHEN OLD.SystemKey = 'generic'
  AND
  (
      NEW.Name IS NOT OLD.Name
      OR NEW.SortOrder IS NOT OLD.SortOrder
      OR NEW.IsActive IS NOT OLD.IsActive
      OR NEW.IsDefault IS NOT OLD.IsDefault
      OR NEW.SystemKey IS NOT OLD.SystemKey
      OR NEW.IsSystem IS NOT OLD.IsSystem
  )
BEGIN
    SELECT RAISE(ABORT, 'The generic card type cannot be modified');
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
