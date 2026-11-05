-- Durable session memory: the turn log, last report and last drillable table of each conversation, as JSON.
-- Holds aggregate report digests and the user's own questions (tokenized). Patient results are cleared before saving;
-- the pending question and the patient in focus are never saved. No patient data is stored.
-- The key is a hash of user id + conversation id, so one user cannot reach another's row.
-- The chatbot needs SELECT, INSERT, UPDATE and DELETE on this table only; use its own login
-- (ConnectionStrings:RcmSession), separate from the read-only report login and the INSERT-only audit login.
IF SCHEMA_ID(N'chatbot') IS NULL EXEC(N'CREATE SCHEMA chatbot');
GO
IF OBJECT_ID(N'chatbot.SessionState') IS NULL
CREATE TABLE chatbot.SessionState
(
    SessionKey  uniqueidentifier NOT NULL CONSTRAINT PK_SessionState PRIMARY KEY,
    UpdatedUtc  datetime2(3)     NOT NULL,
    StateJson   nvarchar(max)    NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SessionState_Updated' AND object_id = OBJECT_ID(N'chatbot.SessionState'))
    CREATE INDEX IX_SessionState_Updated ON chatbot.SessionState (UpdatedUtc);
GO
-- GRANT SELECT, INSERT, UPDATE, DELETE ON chatbot.SessionState TO [<session login or user>];
-- The service deletes rows idle longer than Session:RetentionDays (default 30) every six hours.
