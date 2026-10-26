-- DEV ONLY. Creates a local sample database and a reference implementation of the two reporting views
-- the app reads (contract: docs/Data_Dictionary.md). All rows are synthetic.
-- Production views are written by the data owner over the real source tables; do not run this on a shared server.
--
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -i scripts\dev-reporting-db.sql
--
-- Safe to re-run: objects are created once, views are replaced, sample rows are added only to empty tables.

IF DB_ID(N'AiChatbotReporting') IS NULL CREATE DATABASE AiChatbotReporting;
GO
USE AiChatbotReporting;
GO
IF SCHEMA_ID(N'dev') IS NULL EXEC(N'CREATE SCHEMA dev');
GO
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO

IF OBJECT_ID(N'dev.Facility') IS NULL
    CREATE TABLE dev.Facility (
        FacilityId int           NOT NULL PRIMARY KEY,
        Name       nvarchar(100) NOT NULL UNIQUE
    );
IF OBJECT_ID(N'dev.Admission') IS NULL
    CREATE TABLE dev.Admission (
        AdmissionId   int IDENTITY PRIMARY KEY,
        AccountNumber bigint NOT NULL,
        FacilityId    int    NOT NULL REFERENCES dev.Facility (FacilityId),
        AdmittedOn    date   NOT NULL,
        DischargedOn  date   NULL,
        IsReadmission bit    NOT NULL DEFAULT 0
    );
IF OBJECT_ID(N'dev.Incident') IS NULL
    CREATE TABLE dev.Incident (
        IncidentId    int IDENTITY PRIMARY KEY,
        AccountNumber bigint NOT NULL,
        FacilityId    int    NOT NULL REFERENCES dev.Facility (FacilityId),
        OpenedOn      date   NOT NULL,
        ClosedOn      date   NULL
    );
GO

IF NOT EXISTS (SELECT 1 FROM dev.Facility)
BEGIN
    INSERT dev.Facility (FacilityId, Name) VALUES (1, N'north'), (2, N'south'), (3, N'east'), (4, N'west');

    INSERT dev.Admission (AccountNumber, FacilityId, AdmittedOn, DischargedOn, IsReadmission) VALUES
        (12345, 1, '2026-03-02', '2026-03-06', 0),
        (12345, 1, '2026-05-11', '2026-05-15', 1),
        (12345, 2, '2026-09-30', '2026-10-04', 0),
        (20456, 1, '2026-08-19', '2026-08-21', 0),
        (30001, 2, '2026-07-07', '2026-07-12', 0),
        (30001, 2, '2026-07-20', '2026-07-23', 1),
        (30001, 3, '2026-10-01', NULL,         0),
        (40002, 3, '2026-06-14', '2026-06-20', 0),
        (40002, 4, '2026-09-02', '2026-09-05', 0),
        (50003, 4, '2026-04-25', '2026-04-27', 0);

    INSERT dev.Incident (AccountNumber, FacilityId, OpenedOn, ClosedOn) VALUES
        (12345, 1, '2026-03-04', '2026-03-10'),
        (20456, 1, '2026-08-20', NULL),
        (30001, 2, '2026-07-09', NULL),
        (30001, 3, '2026-10-02', NULL),
        (40002, 3, '2026-06-15', '2026-06-18'),
        (50003, 4, '2026-04-26', '2026-04-30');
END
GO

-- Contract view 1: one row per patient account. Reads only; exposes no names or other identifiers.
CREATE OR ALTER VIEW reporting.vw_PatientSummary AS
SELECT
    a.AccountNumber,
    COUNT(*)                                                                         AS Admissions,
    MAX(a.AdmittedOn)                                                                AS LastVisitDate,
    (SELECT COUNT(*) FROM dev.Incident i
      WHERE i.AccountNumber = a.AccountNumber AND i.ClosedOn IS NULL)                AS OpenIncidents
FROM dev.Admission a
GROUP BY a.AccountNumber;
GO

-- Contract view 2: one row per facility. Aggregates only.
CREATE OR ALTER VIEW reporting.vw_FacilityMetrics AS
SELECT
    f.Name                                                                           AS Facility,
    CAST(100.0 * ISNULL(SUM(CAST(a.IsReadmission AS int)), 0)
         / NULLIF(COUNT(a.AdmissionId), 0) AS decimal(5, 2))                         AS ReadmissionRatePct,
    CAST(ISNULL(AVG(CAST(DATEDIFF(day, a.AdmittedOn, a.DischargedOn) AS decimal(9, 2))), 0)
         AS decimal(6, 2))                                                           AS AvgLengthOfStayDays,
    (SELECT COUNT(*) FROM dev.Incident i WHERE i.FacilityId = f.FacilityId)          AS IncidentCount,
    CAST(GETDATE() AS date)                                                          AS AsOfDate
FROM dev.Facility f
LEFT JOIN dev.Admission a ON a.FacilityId = f.FacilityId
GROUP BY f.FacilityId, f.Name;
GO
