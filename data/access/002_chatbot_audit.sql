-- Durable audit trail for report runs and exports (who, which report, which filters, outcome, row count).
-- No result values and no patient data are stored. Keep rows as long as your compliance policy requires,
-- then purge by TimestampUtc (example at the end).
-- The chatbot needs INSERT on this table only; use a login separate from the read-only report login
-- (ConnectionStrings:RcmAudit).
IF SCHEMA_ID(N'chatbot') IS NULL EXEC(N'CREATE SCHEMA chatbot');
GO
IF OBJECT_ID(N'chatbot.AuditLog') IS NULL
CREATE TABLE chatbot.AuditLog
(
    AuditId        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    TimestampUtc   datetime2(3)  NOT NULL,
    CorrelationId  varchar(100)  NOT NULL,   -- joins to the ChatTrace log line
    Action         varchar(20)   NOT NULL,   -- run | export
    UserId         varchar(100)  NOT NULL,   -- Azure AD object id
    Role           varchar(30)   NOT NULL,
    ReportId       varchar(80)   NOT NULL,
    Filters        varchar(1000) NOT NULL,   -- e.g. practices=10;startDate=...
    Outcome        varchar(30)   NOT NULL,   -- ok | forbidden | invalid | unavailable ...
    ResultRows     int           NOT NULL,   -- not RowCount: ROWCOUNT is a reserved word in T-SQL
    DurationMs     bigint        NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_Time' AND object_id = OBJECT_ID(N'chatbot.AuditLog'))
    CREATE INDEX IX_AuditLog_Time ON chatbot.AuditLog (TimestampUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_User' AND object_id = OBJECT_ID(N'chatbot.AuditLog'))
    CREATE INDEX IX_AuditLog_User ON chatbot.AuditLog (UserId, TimestampUtc);
GO
-- GRANT INSERT ON chatbot.AuditLog TO [<audit login or user>];
-- Reviewers: GRANT SELECT ON chatbot.AuditLog TO [<security reviewer role>];
-- Retention example: DELETE FROM chatbot.AuditLog WHERE TimestampUtc < DATEADD(YEAR, -7, SYSUTCDATETIME());
