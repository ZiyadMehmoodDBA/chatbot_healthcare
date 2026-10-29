# SQL Reporting Layer --- Query Perspective, Requirements, and Output Location

## 1. Purpose

This document defines a reporting layer between the SQL Server Data
Dictionary and report execution. Each report must document:

1.  **Query perspective** --- the business question the report answers.
2.  **What is needed** --- date range, filters, measures, grouping, and
    business rules.
3.  **Where to find the data** --- source table(s) and verified columns.
4.  **Where to find the output** --- execution destination, export
    format, and configured save location.
5.  **Validation** --- reconciliation checks and assumptions that must
    be confirmed.

The Data Dictionary describes table structures, but it does not by
itself confirm the business meaning, row grain, joins, or financial
calculation rules. Validate these before production use.

## 2. Suggested project structure

``` text
reporting/
├── README.md
├── data-dictionary/
│   └── SQL_Server_Data_Dictionary.md
├── report-specs/
│   ├── insurance-vs-patient-payments.md
│   ├── payments-by-payer.md
│   ├── denial-analysis.md
│   ├── ar-aging.md
│   └── ...
├── sql/
│   ├── insurance-vs-patient-payments.sql
│   ├── payments-by-payer.sql
│   └── ...
└── output/
    ├── csv/
    ├── excel/
    └── pdf/
```

**Output-path note:** This is a proposed project layout, not a claim
that these folders already exist. Choose and configure the actual output
directory for your environment. SQL Server Management Studio (SSMS)
displays query results in its Results grid unless the user exports them
or a reporting job writes them to a configured destination.

## 3. Report specification template

Use this template for every report.

### Report identity

-   **Report name:**
-   **Business owner:**
-   **Purpose / question answered:**
-   **Refresh frequency:**
-   **SQL file:**
-   **Output format:** SSMS grid / CSV / Excel / PDF / dashboard
-   **Output location:** Specify the actual configured folder, share,
    report server, or dashboard.

### Query perspective

-   **Primary measure(s):**
-   **Group by:** (payer, practice, provider, date, claim, etc.)
-   **Date basis:** (posting date, deposit date, date of service,
    submission date, etc.)
-   **Filters:** (date range, practice, payer, provider, status)
-   **Required parameters:**
-   **Inclusion / exclusion rules:**

### Data requirements

-   **Source table(s):**
-   **Required columns:**
-   **Join keys and join type:** Confirm against schema and business
    rules.
-   **Expected row grain:** What one output row represents.
-   **Calculation rules:** Include null handling, signs, rounding, and
    duplicate handling.
-   **Sensitive data:** Exclude patient-identifying fields unless
    explicitly required and authorized.

### Validation and delivery

-   **Reconciliation total(s):**
-   **Sample records checked:**
-   **Known caveats:**
-   **Output filename convention:**
-   **Delivery owner / audience:**
-   **Approval status:** Draft / Validated / Approved for production

------------------------------------------------------------------------

## 4. Initial report catalogue

These are report perspectives to implement. A source table and columns
are listed only where they are present in the available Data Dictionary
context; other mappings must be verified before implementation.

  -----------------------------------------------------------------------------------------------------------------------
  Report           Query perspective    What is needed            Data source / fields to verify        Output
  ---------------- -------------------- ------------------------- ------------------------------------- -----------------
  Insurance        Compare insurance    Start/end date; date      `dbo.FACT_CC_Claims_Payment_Count`:   SSMS grid
  vs. Patient      and patient payment  basis; optional practice, `Posting_Date`, `Payer_Id`,           initially; export
  Payments         amounts and their    payer, provider filters;  `Payer_Name`, `InsurancePayments`,    to CSV/Excel
                   share of combined    payment definitions       `PatientPayments`, `Claim_No`,        after validation
                   payments                                       `Practice_Code`, `Rendering_Provider` 

  Payments by      Rank payers by       Date range; posting       `dbo.FACT_CC_Claims_Payment_Count`:   SSMS grid;
  Payer            payment totals       vs. deposit date; payer   `Payer_Id`, `Payer_Name`,             CSV/Excel
                                        grouping; duplicate/grain `Posting_Date`, `Deposit_Date`,       
                                        rules                     `InsurancePayments`,                  
                                                                  `PatientPayments`, `Total_Payment`    

  Denial Analysis  Identify             Date range;               Confirm the rejection/denial table    SSMS grid;
                   denial/rejection     denial/rejection          and its actual date, status, reason,  CSV/Excel
                   volume and reasons   definition; status/reason payer, practice, and claim columns in 
                   by payer, practice,  filters; distinct-claim   the Data Dictionary                   
                   and time             rule                                                            

  A/R Aging        Show outstanding     Aging/as-of date; bucket  Locate and validate the table/view    SSMS grid;
                   insurance balances   boundaries;               containing open insurance balance,    Excel/PDF
                   by aging bucket      insurance-only rule;      payer, claim, and aging/date fields   
                                        balance definition;                                             
                                        payer/practice/provider                                         
                                        filters                                                         

  Top Payers by    Rank payers by open  As-of date; payer         Verify the source for outstanding     SSMS grid; Excel
  Outstanding A/R  insurance balance    grouping; open-balance    insurance balance and payer           
                                        definition; top-N         attributes                            
                                        parameter                                                       

  Claims           Measure claims       Date range; submission    Verify claims/submission source and   SSMS grid;
  Submission       submitted and        status; resubmission      actual submission date/status columns CSV/Excel
  Performance      submission outcomes  handling; claim-level                                           
                   over time            grain                                                           

  Days to Bill     Measure elapsed time Date range; start/end     Verify the relevant claims or         SSMS grid; Excel
                   between date of      event definitions;        `FactDaysToBill` source and exact     
                   service and          negative/null-date        columns                               
                   billing/submission   handling                                                        

  Claim Volume by  Count claims by      Date range;               Verify source containing claim        SSMS grid;
  Practice and     practice/provider    practice/provider         number, practice, provider, and the   dashboard/Excel
  Provider         and period           filters; distinct-claim   chosen date                           
                                        rule                                                            

  CPT Procedure    Compare procedure    Date range; procedure     Verify source containing procedure    SSMS grid; Excel
  Performance      volume, charges, and code; charge/payment      code, charges, and payment measures   
                   payments             definitions;                                                    
                                        claim/procedure grain                                           

  Claim Interest   Report interest      Date range; definition of Locate and verify the actual interest SSMS grid; Excel
  Report           amounts associated   interest; posting and     source and amount/date/claim columns  
                   with claims          claim relationship                                              

  Facility and     Compare volume or    Date range; facility and  Verify facility/location and sales    SSMS grid;
  Sales            financial measures   representative mapping;   representative sources, join keys,    Excel/dashboard
  Representative   by facility and      selected KPI definitions  and effective dates                   
  Performance      sales representative                                                                 
  -----------------------------------------------------------------------------------------------------------------------

## 5. Detailed specification --- Insurance vs. Patient Payments

### Business question

For the selected posting period, how much was collected from insurance
versus patients, and what percentage did each contribute?

### Required inputs

-   `@StartDate` --- inclusive start date.
-   `@EndDate` --- inclusive end date.
-   Optional filters: `Practice_Code`, `Payer_Id`, and
    `Rendering_Provider`.
-   Confirm whether reporting is by **posting date** or **deposit
    date**.

### Source

`dbo.FACT_CC_Claims_Payment_Count`

Relevant columns: - `Posting_Date`, `Deposit_Date` -
`InsurancePayments`, `PatientPayments` - `Payer_Id`, `Payer_Name` -
`Claim_No`, `Practice_Code` - `Rendering_Provider`

The table also contains `InsuranceAmountPaid` and `PatientAmountPaid`.
Confirm which pair is authoritative before production reporting.

### Output columns

-   Payer ID and payer name, if grouped by payer
-   Unique claim count and source record count
-   Insurance payments
-   Patient payments
-   Combined payments
-   Payment difference (insurance minus patient)
-   Insurance payment percentage
-   Patient payment percentage

### Calculation rules

-   Use a half-open date filter:
    `Posting_Date >= @StartDate AND Posting_Date < DATEADD(DAY, 1, @EndDate)`.
-   Treat null payment values as zero only if approved by the report
    owner.
-   Calculate percentages using `NULLIF(Combined_Payments, 0)` to avoid
    division by zero.
-   Do not assume patient payments belong to the insurance payer on the
    same row. If that relationship is not meaningful, produce an overall
    comparison or group by `Responsible_Party` instead, after validating
    its values.
-   Do not sum both `Total_Payment` and the component payment fields
    into the same total without confirming their definitions.
-   Validate the table grain to avoid duplicated payment totals.

### Output destination

-   **Initial development:** SSMS Results grid.
-   **File export:**
    `output/csv/insurance-vs-patient-payments_YYYYMMDD.csv` or
    `output/excel/insurance-vs-patient-payments_YYYYMMDD.xlsx`.
-   These are suggested names and folders; configure the actual paths
    before implementation.
-   **Production delivery:** Specify a report server, shared folder,
    dashboard, or scheduled export destination in the report
    configuration. Do not assume an output file is created automatically
    by a SELECT query.

### Acceptance checks

1.  Confirm payment totals against a trusted source for a small sample
    period.
2.  Compare `InsurancePayments + PatientPayments` with `Total_Payment`
    and document any difference.
3.  Check for duplicate claim/procedure/payment rows.
4.  Verify null and negative payment handling.
5.  Confirm date basis and payer attribution with the business owner.

## 6. Output destination standards

For every report, record one explicit destination:

-   **Interactive:** SSMS Results grid (manual execution).
-   **File-based:** configured CSV, Excel, or PDF path.
-   **Scheduled:** SQL Server Agent job or an approved orchestration
    service writes the output to a configured location.
-   **Dashboard:** a named reporting dataset/dashboard with a documented
    refresh schedule.

Do not put patient names, account numbers, or other patient-identifying
information into output files unless necessary, authorized, and
protected according to organizational policy.

## 7. Implementation sequence

1.  Confirm the Data Dictionary and identify the exact source
    table/view.
2.  Create a report-spec Markdown file from the template.
3.  Confirm source grain, joins, date basis, and measure definitions.
4.  Write the parameterized SQL file.
5.  Test with a limited date range and known records.
6.  Reconcile totals and document caveats.
7.  Agree on the output destination and file naming.
8.  Mark the report validated before scheduling or distributing it.
