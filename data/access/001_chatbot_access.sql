-- Who may use the chatbot, with which role and which practices.
-- Users sign in with Azure AD; they are matched by tenant id + object id (stable, not an email).
-- The chatbot's login only needs SELECT on this schema. Administrators change rows with SQL
-- (see docs/Azure_AD_Setup.md). A user missing from this table, or with IsActive = 0, gets no access.
IF SCHEMA_ID(N'chatbot') IS NULL EXEC(N'CREATE SCHEMA chatbot');
GO
IF OBJECT_ID(N'chatbot.UserAccess') IS NULL
CREATE TABLE chatbot.UserAccess
(
    UserId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserAccess PRIMARY KEY,
    TenantId     uniqueidentifier  NOT NULL,
    ObjectId     uniqueidentifier  NOT NULL,   -- Azure AD "oid" claim
    DisplayName  nvarchar(200)     NULL,       -- for administrators only; never sent to the model
    Role         varchar(30)       NOT NULL,
    IsActive     bit               NOT NULL CONSTRAINT DF_UserAccess_IsActive DEFAULT (1),
    CreatedUtc   datetime2(0)      NOT NULL CONSTRAINT DF_UserAccess_Created DEFAULT (SYSUTCDATETIME()),
    UpdatedUtc   datetime2(0)      NOT NULL CONSTRAINT DF_UserAccess_Updated DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_UserAccess_Identity UNIQUE (TenantId, ObjectId),
    CONSTRAINT CK_UserAccess_Role CHECK (Role IN ('BillingStaff', 'RcmManager', 'Analyst'))
);
GO
IF OBJECT_ID(N'chatbot.UserPractice') IS NULL
CREATE TABLE chatbot.UserPractice
(
    UserId        int    NOT NULL CONSTRAINT FK_UserPractice_User REFERENCES chatbot.UserAccess (UserId) ON DELETE CASCADE,
    PracticeCode  bigint NOT NULL,                 -- same values as Practice_Code in the reporting views
    CONSTRAINT PK_UserPractice PRIMARY KEY (UserId, PracticeCode)
);
GO
-- Example (replace the ids; do not commit real ones):
-- INSERT chatbot.UserAccess (TenantId, ObjectId, DisplayName, Role)
--   VALUES ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000000', N'Jane Doe', 'RcmManager');
-- INSERT chatbot.UserPractice (UserId, PracticeCode) VALUES (SCOPE_IDENTITY(), 21109);
--
-- Revoke at once:  UPDATE chatbot.UserAccess SET IsActive = 0, UpdatedUtc = SYSUTCDATETIME() WHERE ObjectId = '...';
-- (the chatbot re-reads access every Auth:AccessCacheSeconds, default 60)
--
-- GRANT SELECT ON SCHEMA::chatbot TO [<chatbot read-only login or user>];
