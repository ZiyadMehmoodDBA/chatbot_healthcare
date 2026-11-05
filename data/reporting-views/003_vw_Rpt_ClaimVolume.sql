-- Reporting view for: claim-volume.
-- Source: dbo.FactClaimCount (one row per claim line, grain unconfirmed; no declared key; reloaded from the source on each sync).
-- Exposes no patient name, account, address or zip. Charge_Amount is the line charge.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_ClaimVolume AS
SELECT
    Claim_No,
    Practice_Code,
    Rendering_Provider_ID,
    Rendering_Provider,
    DOS,
    Charge_Amount
FROM dbo.FactClaimCount;
GO
