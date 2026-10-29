# SQL Server Data Dictionary

## Scope and interpretation

This data dictionary was generated from the supplied SQL Server DDL script. It documents table names, columns, SQL data types, nullability, identity properties, and primary keys where detectable.

**Important:** Column descriptions below are based on names only. The DDL does not define all business rules, table grain, foreign-key relationships, units, or calculation logic. Validate those details against ETL jobs, views, stored procedures, and application code before using this as a formal business definition.

**Tables found:** 43

## Table index

| Table | Column count | Primary key columns detected |
|---|---:|---|
| `DimBillingStatuses` | 5 | `BillingStatusID` |
| `DimClaimInsurance` | 11 | `DimClaimInsuranceID` |
| `DimCPT` | 10 | `CPTCode` |
| `DimDates` | 13 | Not detected in parsed DDL |
| `DimDiagnosis` | 9 | `DiagnosisCode` |
| `DimFacilities` | 14 | `FacilityCode` |
| `DimFacilitySalesRep` | 19 | `Practice_Facilities_Sale_Reps_Id` |
| `DimFinancialClass` | 8 | `FinancialClassID` |
| `DimInsFinancialClass` | 8 | `DimInsFinancialClass` |
| `DimInsuranceAddresses` | 13 | `InsAddressID` |
| `DimInsurances` | 6 | `InsuranceID` |
| `DimLocations` | 14 | `LocationCode` |
| `DimModifiers` | 10 | `ModifierID` |
| `DimPatients` | 46 | `PatientAccount` |
| `DimPracticeBillingProviders` | 4 | `ID` |
| `DimPracticePayers` | 19 | `PracticePayerID` |
| `DimPracticePayers_Filter` | 4 | Not detected in parsed DDL |
| `DimPractices` | 15 | `PracticeCode` |
| `DimProviders` | 14 | `ProviderCode` |
| `DimReportClaimInsurance` | 21 | `Claim_Insurance_Id` |
| `DimReportClaimPhysicians` | 9 | `Physician_Id` |
| `DimSalesRep` | 16 | `Practice_sales_Rep_Id` |
| `FACT_CC_Claims_Payment_Count` | 33 | Not detected in parsed DDL |
| `Fact_DaysAR` | 8 | Not detected in parsed DDL |
| `Fact_Interg` | 26 | Not detected in parsed DDL |
| `Fact_Interg_Data` | 26 | Not detected in parsed DDL |
| `FactAccountReceivable` | 13 | Not detected in parsed DDL |
| `FactClaimCount` | 32 | Not detected in parsed DDL |
| `FactClaimCptReport` | 48 | Not detected in parsed DDL |
| `FactClaimInterestReport` | 22 | Not detected in parsed DDL |
| `FactClaimResubmission` | 14 | Not detected in parsed DDL |
| `FactClaimsSubmissionAccountReceivable` | 5 | Not detected in parsed DDL |
| `FactCPT_KPI` | 99 | `FactCPT_KPI_ID` |
| `FactDaysARbyPayer` | 14 | Not detected in parsed DDL |
| `FactDaysToBill` | 14 | Not detected in parsed DDL |
| `FactFacilitySalesRep` | 19 | Not detected in parsed DDL |
| `FactFinancialClass` | 16 | `FactFinancialClassID` |
| `FactGlobalKPIClaimData` | 24 | Not detected in parsed DDL |
| `FactGlobalKPIClaimSubmissionsHistory` | 9 | Not detected in parsed DDL |
| `FactGlobalKPICPTClaimData` | 29 | Not detected in parsed DDL |
| `FactPayerRejectionReason` | 19 | Not detected in parsed DDL |
| `FactReportClaimPaymentDetails` | 82 | `Claim_Payment_Id` |
| `MAX_GlobalKPIClaimPaymentDenial` | 11 | Not detected in parsed DDL |

## Table definitions

### `DimBillingStatuses`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `BillingStatusID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `BillingStatusID` | `int` | NOT NULL | Yes | Billing Status ID |
| `BillingStatus` | `nvarchar(50)` | NOT NULL | No | Billing Status |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimClaimInsurance`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `DimClaimInsuranceID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `DimClaimInsuranceID` | `bigint` | NOT NULL | Yes | Dim Claim Insurance ID; claim identifier or claim-related attribute, depending on the name |
| `Claim_No` | `bigint` | NOT NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Practice_Code` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Ins_Address_Id` | `bigint` | NOT NULL | No | Ins Address Id |
| `Ins_Payer_Id` | `bigint` | NOT NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Ins_Payer_Name` | `varchar(100)` | NOT NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |
| `Insurance_Type` | `varchar(15)` | NULL | No | Insurance Type |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndtDate` | `datetime` | NULL | No | Endt Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimCPT`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `CPTCode`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `CPTID` | `int` | NOT NULL | Yes | CPTID |
| `CPTCode` | `varchar(50)` | NOT NULL | No | CPTCode |
| `ShortDescription` | `varchar(2000)` | NULL | No | Short Description |
| `LongDescription` | `varchar(2000)` | NULL | No | Long Description |
| `CPTType` | `varchar(100)` | NULL | No | CPTType |
| `ActiveFrom` | `datetime` | NULL | No | Active From; likely start of the effective period |
| `ActiveTo` | `datetime` | NULL | No | Active To; likely end of the effective period |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimDates`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `RID` | `bigint` | NULL | No | RID |
| `Date` | `datetime` | NULL | No | Date; date/time attribute |
| `Quarter` | `int` | NULL | No | Quarter |
| `DayofYear` | `int` | NULL | No | Dayof Year |
| `WeekofYear` | `int` | NULL | No | Weekof Year |
| `Year` | `int` | NULL | No | Year |
| `Month` | `int` | NULL | No | Month |
| `Day` | `int` | NULL | No | Day |
| `Weekday` | `int` | NULL | No | Weekday |
| `MonthName` | `nvarchar(30)` | NULL | No | Month Name |
| `WeekdayName` | `nvarchar(30)` | NULL | No | Weekday Name |
| `IsBusinessDay` | `int` | NOT NULL | No | Is Business Day |
| `Vintage` | `varchar(6)` | NULL | No | Vintage |

### `DimDiagnosis`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `DiagnosisCode`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `DiagnosisID` | `int` | NOT NULL | Yes | Diagnosis ID |
| `DiagnosisCode` | `varchar(50)` | NOT NULL | No | Diagnosis Code |
| `Description` | `varchar(300)` | NULL | No | Description |
| `DiagnosisType` | `varchar(10)` | NULL | No | Diagnosis Type |
| `ActiveFrom` | `datetime` | NULL | No | Active From; likely start of the effective period |
| `ActiveTo` | `datetime` | NULL | No | Active To; likely end of the effective period |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimFacilities`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `FacilityCode`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `FacilityID` | `int` | NOT NULL | Yes | Facility ID |
| `PracticeCode` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `FacilityCode` | `bigint` | NOT NULL | No | Facility Code |
| `FacilityName` | `varchar(max)` | NULL | No | Facility Name |
| `FacilityType` | `varchar(max)` | NULL | No | Facility Type |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(50)` | NULL | No | State |
| `ZIP` | `varchar(15)` | NULL | No | ZIP |
| `Phone` | `varchar(15)` | NULL | No | Phone |
| `Email` | `varchar(50)` | NULL | No | Email |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimFacilitySalesRep`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `Practice_Facilities_Sale_Reps_Id`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `DimFacilitySalesRepID` | `bigint` | NOT NULL | Yes | Dim Facility Sales Rep ID |
| `Practice_Facilities_Sale_Reps_Id` | `bigint` | NOT NULL | No | Practice Facilities Sale Reps Id |
| `Practice_Facility_Id` | `bigint` | NULL | No | Practice Facility Id |
| `Sale_Reps_Id` | `bigint` | NOT NULL | No | Sale Reps Id |
| `Effective_From` | `datetime` | NULL | No | Effective From; likely start of the effective period |
| `Effective_To` | `datetime` | NULL | No | Effective To; likely end of the effective period |
| `First_Name` | `varchar(50)` | NOT NULL | No | First Name |
| `Last_Name` | `varchar(50)` | NOT NULL | No | Last Name |
| `Middle_Initial` | `varchar(1)` | NULL | No | Middle Initial |
| `Date_of_Birth` | `datetime` | NOT NULL | No | Date of Birth; date/time attribute |
| `Email` | `varchar(100)` | NULL | No | Email |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(2)` | NULL | No | State |
| `Zip` | `varchar(10)` | NULL | No | Zip |
| `Country` | `varchar(100)` | NULL | No | Country |
| `StartDate` | `datetime` | NOT NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NOT NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(1)` | NOT NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimFinancialClass`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `FinancialClassID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `FinancialClassID` | `bigint` | NOT NULL | No | Financial Class ID |
| `Description` | `varchar(500)` | NULL | No | Description |
| `HCFA_Class` | `varchar(30)` | NULL | No | HCFA Class |
| `Electronic_Sub_Class` | `varchar(2)` | NULL | No | Electronic Sub Class |
| `Electronic_Sub_Class_Description` | `varchar(100)` | NULL | No | Electronic Sub Class Description |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimInsFinancialClass`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `DimInsFinancialClass`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `DimInsFinancialClass` | `bigint` | NOT NULL | Yes | Dim Ins Financial Class |
| `Practice_Code` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Ins_Address_Id` | `bigint` | NOT NULL | No | Ins Address Id |
| `Ins_Payer_Id` | `bigint` | NOT NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndtDate` | `datetime` | NULL | No | Endt Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimInsuranceAddresses`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `InsAddressID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `InsAddressID` | `int` | NOT NULL | Yes | Ins Address ID |
| `InsurancePayerID` | `bigint` | NULL | No | Insurance Payer ID; insurance payer identifier or name, depending on the data type |
| `InsuranceAddressID` | `bigint` | NULL | No | Insurance Address ID |
| `InsuranceName` | `varchar(100)` | NULL | No | Insurance Name |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(50)` | NULL | No | State |
| `ZIP` | `varchar(15)` | NULL | No | ZIP |
| `Phone` | `varchar(15)` | NULL | No | Phone |
| `Email` | `varchar(50)` | NULL | No | Email |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimInsurances`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `InsuranceID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `InsuranceID` | `int` | NOT NULL | Yes | Insurance ID |
| `InsurancePayerID` | `bigint` | NULL | No | Insurance Payer ID; insurance payer identifier or name, depending on the data type |
| `InsuranceName` | `varchar(100)` | NULL | No | Insurance Name |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimLocations`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `LocationCode`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `LocationID` | `int` | NOT NULL | Yes | Location ID |
| `PracticeCode` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `LocationCode` | `bigint` | NOT NULL | No | Location Code |
| `LocationName` | `varchar(500)` | NULL | No | Location Name |
| `LocationType` | `varchar(50)` | NULL | No | Location Type |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(50)` | NULL | No | State |
| `ZIP` | `varchar(10)` | NULL | No | ZIP |
| `Phone` | `varchar(15)` | NULL | No | Phone |
| `Email` | `varchar(50)` | NULL | No | Email |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimModifiers`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `ModifierID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `ModifierID` | `bigint` | NOT NULL | Yes | Modifier ID |
| `Modifier` | `varchar(10)` | NOT NULL | No | Modifier |
| `Modifier_Details` | `varchar(2000)` | NULL | No | Modifier Details |
| `Modifier_Category` | `varchar(85)` | NULL | No | Modifier Category |
| `Modifier_SubCategory` | `varchar(75)` | NULL | No | Modifier Sub Category |
| `Is_Pricing` | `bit` | NULL | No | Is Pricing |
| `Is_Active` | `bit` | NULL | No | Is Active |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimPatients`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `PatientAccount`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `PatientID` | `bigint` | NOT NULL | Yes | Patient ID; patient-related attribute; handle according to applicable privacy controls |
| `PatientAccount` | `bigint` | NOT NULL | No | Patient Account; patient-related attribute; handle according to applicable privacy controls |
| `PracticeCode` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Patient_First_Name` | `varchar(150)` | NULL | No | Patient First Name; patient-related attribute; handle according to applicable privacy controls |
| `Patient_Last_Name` | `varchar(150)` | NULL | No | Patient Last Name; patient-related attribute; handle according to applicable privacy controls |
| `Middle_Initial` | `varchar(1)` | NULL | No | Middle Initial |
| `Date_of_Birth` | `datetime` | NULL | No | Date of Birth; date/time attribute |
| `Gender` | `varchar(20)` | NULL | No | Gender |
| `SSN` | `varchar(20)` | NULL | No | SSN |
| `Marital_Status` | `varchar(50)` | NULL | No | Marital Status |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(50)` | NULL | No | State |
| `ZIP` | `varchar(10)` | NULL | No | ZIP |
| `Home_Phone` | `varchar(15)` | NULL | No | Home Phone |
| `Cell_Phone` | `varchar(15)` | NULL | No | Cell Phone |
| `Work_phone` | `varchar(15)` | NULL | No | Work phone |
| `Work_Phone_EXT` | `varchar(5)` | NULL | No | Work Phone EXT |
| `Email` | `varchar(50)` | NULL | No | Email |
| `Primary_Financial_Class_ID` | `bigint` | NULL | No | Primary Financial Class ID |
| `Primary_Insurance_ID` | `bigint` | NULL | No | Primary Insurance ID |
| `Primary_Insurance_Name` | `varchar(500)` | NULL | No | Primary Insurance Name |
| `Primary_Plan_StartDate` | `datetime` | NULL | No | Primary Plan Start Date; date/time attribute |
| `Primary_Plan_EndDate` | `datetime` | NULL | No | Primary Plan End Date; date/time attribute |
| `Primary_Policy_Number` | `varchar(150)` | NULL | No | Primary Policy Number |
| `Primary_Insurance_Type` | `varchar(50)` | NULL | No | Primary Insurance Type |
| `Primary_Insurance_Type_Named` | `varchar(50)` | NULL | No | Primary Insurance Type Named |
| `Secondary_Financial_Class_ID` | `bigint` | NULL | No | Secondary Financial Class ID |
| `Secondary_Insurance_ID` | `bigint` | NULL | No | Secondary Insurance ID |
| `Secondary_Insurance_Name` | `varchar(500)` | NULL | No | Secondary Insurance Name |
| `Secondary_Plan_StartDate` | `datetime` | NULL | No | Secondary Plan Start Date; date/time attribute |
| `Secondary_Plan_EndDate` | `datetime` | NULL | No | Secondary Plan End Date; date/time attribute |
| `Secondary_Policy_Number` | `varchar(150)` | NULL | No | Secondary Policy Number |
| `Secondary_Insurance_Type` | `varchar(50)` | NULL | No | Secondary Insurance Type |
| `Secondary_Insurance_Type_Named` | `varchar(50)` | NULL | No | Secondary Insurance Type Named |
| `Tertiary_Financial_Class_ID` | `bigint` | NULL | No | Tertiary Financial Class ID |
| `Tertiary_Insurance_ID` | `bigint` | NULL | No | Tertiary Insurance ID |
| `Tertiary_Insurance_Name` | `varchar(500)` | NULL | No | Tertiary Insurance Name |
| `Tertiary_Plan_StartDate` | `datetime` | NULL | No | Tertiary Plan Start Date; date/time attribute |
| `Tertiary_Plan_EndDate` | `datetime` | NULL | No | Tertiary Plan End Date; date/time attribute |
| `Tertiary_Policy_Number` | `varchar(150)` | NULL | No | Tertiary Policy Number |
| `Tertiary_Insurance_Type` | `varchar(50)` | NULL | No | Tertiary Insurance Type |
| `Tertiary_Insurance_Type_Named` | `varchar(50)` | NULL | No | Tertiary Insurance Type Named |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimPracticeBillingProviders`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `ID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `ID` | `int` | NOT NULL | Yes | ID |
| `Provider_Code` | `bigint` | NULL | No | Provider Code |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Billing_Provider` | `varchar(500)` | NULL | No | Billing Provider |

### `DimPracticePayers`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `PracticePayerID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `PracticePayerID` | `int` | NOT NULL | Yes | Practice Payer ID; insurance payer identifier or name, depending on the data type |
| `Practice_Code` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `InsurancePayerID` | `bigint` | NULL | No | Insurance Payer ID; insurance payer identifier or name, depending on the data type |
| `InsuranceAddressID` | `bigint` | NULL | No | Insurance Address ID |
| `InsuranceName` | `varchar(100)` | NULL | No | Insurance Name |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Financial_Class_Description` | `varchar(255)` | NULL | No | Financial Class Description |
| `Address1` | `varchar(500)` | NULL | No | Address1 |
| `Address2` | `varchar(500)` | NULL | No | Address2 |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(50)` | NULL | No | State |
| `ZIP` | `varchar(15)` | NULL | No | ZIP |
| `Phone` | `varchar(15)` | NULL | No | Phone |
| `ActiveInsurancePayer` | `bit` | NULL | No | Active Insurance Payer; insurance payer identifier or name, depending on the data type |
| `Submission_Type` | `varchar(10)` | NULL | No | Submission Type |
| `ActiveInsuranceAddress` | `bit` | NULL | No | Active Insurance Address |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimPracticePayers_Filter`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `ID` | `int` | NOT NULL | Yes | ID |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Ins_Payer_ID` | `bigint` | NULL | No | Ins Payer ID; insurance payer identifier or name, depending on the data type |
| `Ins_Payer_Name` | `varchar(500)` | NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |

### `DimPractices`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `PracticeCode`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `PracticeID` | `int` | NOT NULL | Yes | Practice ID |
| `PracticeCode` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `PracticeName` | `varchar(500)` | NULL | No | Practice Name |
| `Alias` | `varchar(50)` | NULL | No | Alias |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(50)` | NULL | No | State |
| `ZIP` | `varchar(10)` | NULL | No | ZIP |
| `Phone` | `varchar(15)` | NULL | No | Phone |
| `Email` | `varchar(50)` | NULL | No | Email |
| `Is_Active` | `bit` | NULL | No | Is Active |
| `Specialty_Id` | `varchar(10)` | NULL | No | Specialty Id |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimProviders`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `ProviderCode`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `ProviderID` | `int` | NOT NULL | Yes | Provider ID |
| `PracticeCode` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `ProviderCode` | `bigint` | NOT NULL | No | Provider Code |
| `ProviderName` | `varchar(500)` | NULL | No | Provider Name |
| `Specialty` | `varchar(100)` | NULL | No | Specialty |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(50)` | NULL | No | State |
| `ZIP` | `varchar(10)` | NULL | No | ZIP |
| `Phone` | `varchar(15)` | NULL | No | Phone |
| `Email` | `varchar(150)` | NULL | No | Email |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(15)` | NULL | No | Status; status value, allowed values are not specified in the DDL |

### `DimReportClaimInsurance`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `Claim_Insurance_Id`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Claim_Insurance_Id` | `bigint` | NOT NULL | No | Claim Insurance Id; claim identifier or claim-related attribute, depending on the name |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Patient_Account` | `bigint` | NULL | No | Patient Account; patient-related attribute; handle according to applicable privacy controls |
| `Claim_Type` | `varchar(50)` | NULL | No | Claim Type; claim identifier or claim-related attribute, depending on the name |
| `Billing_Status` | `varchar(30)` | NULL | No | Billing Status |
| `Policy_Number` | `varchar(80)` | NULL | No | Policy Number |
| `Insurance_Type` | `varchar(15)` | NULL | No | Insurance Type |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Ins_Address_Id` | `bigint` | NULL | No | Ins Address Id |
| `Subscriber_Relationship` | `varchar(20)` | NULL | No | Subscriber Relationship |
| `Description` | `varchar(55)` | NULL | No | Description |
| `Ins_Payer_Name` | `varchar(100)` | NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |
| `Ins_Assigned_Payer_Id` | `varchar(50)` | NULL | No | Ins Assigned Payer Id; insurance payer identifier or name, depending on the data type |
| `Payment_Due_Days` | `int` | NULL | No | Payment Due Days |
| `Claim_Modified_Date` | `datetime` | NULL | No | Claim Modified Date; claim identifier or claim-related attribute, depending on the name |
| `Modified_Date` | `datetime` | NULL | No | Modified Date; date/time attribute |
| `Claim_Deleted` | `bit` | NULL | No | Claim Deleted; claim identifier or claim-related attribute, depending on the name |
| `Deleted` | `bit` | NULL | No | Deleted |
| `Row_Version` | `varbinary(8000)` | NULL | No | Row Version |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |

### `DimReportClaimPhysicians`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `Physician_Id`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Physician_Id` | `bigint` | NOT NULL | No | Physician Id |
| `NPI` | `bigint` | NOT NULL | No | NPI |
| `Phy_Name` | `varchar(75)` | NULL | No | Phy Name |
| `Last_Name` | `varchar(60)` | NULL | No | Last Name |
| `First_Name` | `varchar(50)` | NULL | No | First Name |
| `Middle_Initial` | `varchar(50)` | NULL | No | Middle Initial |
| `Deleted` | `bit` | NULL | No | Deleted |
| `Last_Sync_Date` | `datetime` | NULL | No | Last Sync Date; date/time attribute |
| `row_version` | `varbinary(8000)` | NULL | No | row version |

### `DimSalesRep`

Dimension/reference table. Its exact business meaning is inferred from the table and column names.

Primary key detected: `Practice_sales_Rep_Id`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `DimSalesRepID` | `bigint` | NOT NULL | Yes | Dim Sales Rep ID |
| `Practice_sales_Rep_Id` | `bigint` | NOT NULL | No | Practice sales Rep Id |
| `Practice_code` | `bigint` | NULL | No | Practice code; practice identifier |
| `First_Name` | `varchar(50)` | NOT NULL | No | First Name |
| `Last_Name` | `varchar(50)` | NOT NULL | No | Last Name |
| `Middle_Initial` | `varchar(1)` | NULL | No | Middle Initial |
| `Date_of_Birth` | `datetime` | NOT NULL | No | Date of Birth; date/time attribute |
| `Email` | `varchar(100)` | NULL | No | Email |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(2)` | NULL | No | State |
| `Zip` | `varchar(10)` | NULL | No | Zip |
| `Country` | `varchar(100)` | NULL | No | Country |
| `StartDate` | `datetime` | NOT NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NOT NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(1)` | NOT NULL | No | Status; status value, allowed values are not specified in the DDL |

### `FACT_CC_Claims_Payment_Count`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `LocationCode` | `bigint` | NULL | No | Location Code |
| `Location_Name` | `varchar(100)` | NULL | No | Location Name |
| `POS_Name` | `varchar(83)` | NULL | No | POS Name |
| `Billing_Provider` | `varchar(183)` | NULL | No | Billing Provider |
| `Payer_Id` | `bigint` | NULL | No | Payer Id; insurance payer identifier or name, depending on the data type |
| `Payer_Name` | `varchar(100)` | NULL | No | Payer Name; insurance payer identifier or name, depending on the data type |
| `Financial_Class` | `varchar(55)` | NULL | No | Financial Class |
| `Procedure_Code` | `varchar(5)` | NULL | No | Procedure Code |
| `Sequence_Number` | `bigint` | NULL | No | Sequence Number |
| `Date_of_Service` | `date` | NULL | No | Date of Service; date/time attribute |
| `Posting_Date` | `date` | NULL | No | Posting Date; date/time attribute |
| `Deposit_Date` | `date` | NULL | No | Deposit Date; date/time attribute |
| `Check_Date` | `date` | NULL | No | Check Date; date/time attribute |
| `Responsible_Party` | `varchar(30)` | NULL | No | Responsible Party |
| `Amount_Paid` | `money` | NULL | No | Amount Paid; financial amount or balance; currency and sign conventions are not specified here |
| `Amount_Adjusted` | `money` | NULL | No | Amount Adjusted; financial amount or balance; currency and sign conventions are not specified here |
| `Withheld_Amount` | `money` | NULL | No | Withheld Amount; financial amount or balance; currency and sign conventions are not specified here |
| `InsurancePayments` | `money` | NULL | No | Insurance Payments |
| `PatientPayments` | `money` | NULL | No | Patient Payments; patient-related attribute; handle according to applicable privacy controls |
| `Total_Payment` | `money` | NULL | No | Total Payment |
| `Rendering_Provider_ID` | `bigint` | NULL | No | Rendering Provider ID |
| `Rendering_Provider` | `varchar(183)` | NULL | No | Rendering Provider |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `DOE` | `date` | NULL | No | DOE; date/time attribute |
| `Billing_Status` | `varchar(30)` | NULL | No | Billing Status |
| `Claim_Category_Name` | `varchar(300)` | NULL | No | Claim Category Name; claim identifier or claim-related attribute, depending on the name |
| `InsuranceAmountPaid` | `money` | NULL | No | Insurance Amount Paid; financial amount or balance; currency and sign conventions are not specified here |
| `PatientAmountPaid` | `money` | NULL | No | Patient Amount Paid; patient-related attribute; handle according to applicable privacy controls |
| `Id` | `bigint` | NOT NULL | Yes | Id |
| `Outstanding_Balance` | `money` | NULL | No | Outstanding Balance; financial amount or balance; currency and sign conventions are not specified here |
| `POS` | `varchar(55)` | NULL | No | POS |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |

### `Fact_DaysAR`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Due_Amount` | `money` | NULL | No | Due Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Billed_Date` | `datetime` | NULL | No | Billed Date; date/time attribute |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Location_ID` | `bigint` | NULL | No | Location ID |
| `Provider_ID` | `bigint` | NULL | No | Provider ID |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |

### `Fact_Interg`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Fact_Interg_Id` | `bigint` | NOT NULL | Yes | Fact Interg Id |
| `Type` | `varchar(100)` | NULL | No | Type |
| `Patient Name` | `varchar(200)` | NULL | No | Patient Name; patient-related attribute; handle according to applicable privacy controls |
| `Acct Nbr` | `bigint` | NULL | No | Acct Nbr |
| `Payer Name` | `varchar(200)` | NULL | No | Payer Name; insurance payer identifier or name, depending on the data type |
| `Billed Claim ID` | `bigint` | NULL | No | Billed Claim ID; claim identifier or claim-related attribute, depending on the name |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `Status` | `varchar(50)` | NULL | No | Status; status value, allowed values are not specified in the DDL |
| `Charge Responsibility` | `varchar(100)` | NULL | No | Charge Responsibility; financial amount or balance; currency and sign conventions are not specified here |
| `Billing Provider` | `varchar(200)` | NULL | No | Billing Provider |
| `Appt Svc Cntr Name` | `varchar(200)` | NULL | No | Appt Svc Cntr Name |
| `Denial` | `varchar(100)` | NULL | No | Denial |
| `Reason` | `varchar(200)` | NULL | No | Reason |
| `Number of Days between DOS and Date Billed` | `int` | NULL | No | Number of Days between DOS and Date Billed; date/time attribute |
| `Provider Name` | `varchar(200)` | NULL | No | Provider Name |
| `Length of Stay` | `int` | NULL | No | Length of Stay |
| `Appt Svc Cntr POS Code` | `varchar(150)` | NULL | No | Appt Svc Cntr POS Code |
| `First Billed Date` | `date` | NULL | No | First Billed Date; date/time attribute |
| `Last Billed Date` | `date` | NULL | No | Last Billed Date; date/time attribute |
| `Charge Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Payment Received` | `money` | NULL | No | Payment Received |
| `Adjusted Amount` | `money` | NULL | No | Adjusted Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Balance` | `money` | NULL | No | Balance; financial amount or balance; currency and sign conventions are not specified here |
| `Expected Recoverable` | `money` | NULL | No | Expected Recoverable |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |

### `Fact_Interg_Data`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Fact_Interg_Id` | `bigint` | NOT NULL | Yes | Fact Interg Id |
| `Type` | `varchar(100)` | NULL | No | Type |
| `Patient_Name` | `varchar(200)` | NULL | No | Patient Name; patient-related attribute; handle according to applicable privacy controls |
| `Patient_Account` | `bigint` | NULL | No | Patient Account; patient-related attribute; handle according to applicable privacy controls |
| `Payer_Name` | `varchar(200)` | NULL | No | Payer Name; insurance payer identifier or name, depending on the data type |
| `Billed_Claim_Id` | `bigint` | NULL | No | Billed Claim Id; claim identifier or claim-related attribute, depending on the name |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `Status` | `varchar(50)` | NULL | No | Status; status value, allowed values are not specified in the DDL |
| `Charge_Responsibility` | `varchar(100)` | NULL | No | Charge Responsibility; financial amount or balance; currency and sign conventions are not specified here |
| `Billing_Provider` | `varchar(200)` | NULL | No | Billing Provider |
| `Appt_Svc_Cntr_Name` | `varchar(200)` | NULL | No | Appt Svc Cntr Name |
| `Denial` | `varchar(100)` | NULL | No | Denial |
| `Reason` | `varchar(200)` | NULL | No | Reason |
| `Number_of_Days_between_DOS_and_DateBilled` | `int` | NULL | No | Number of Days between DOS and Date Billed; date/time attribute |
| `Provider_Name` | `varchar(200)` | NULL | No | Provider Name |
| `Length_of_Stay` | `int` | NULL | No | Length of Stay |
| `Appt_Svc_Cntr_POS_Code` | `varchar(150)` | NULL | No | Appt Svc Cntr POS Code |
| `First_Billed_Date` | `date` | NULL | No | First Billed Date; date/time attribute |
| `Last_Billed_Date` | `date` | NULL | No | Last Billed Date; date/time attribute |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Payment_Received` | `money` | NULL | No | Payment Received |
| `Adjusted_Amount` | `money` | NULL | No | Adjusted Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Balance` | `money` | NULL | No | Balance; financial amount or balance; currency and sign conventions are not specified here |
| `Expected_Recoverable` | `money` | NULL | No | Expected Recoverable |
| `StartDate` | `datetime` | NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NULL | No | End Date; likely end of the effective period |

### `FactAccountReceivable`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Balance` | `varchar(15)` | NULL | No | Balance; financial amount or balance; currency and sign conventions are not specified here |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Charge_Reponsibility` | `varchar(500)` | NULL | No | Charge Reponsibility; financial amount or balance; currency and sign conventions are not specified here |
| `Client_Bill` | `bit` | NULL | No | Client Bill |
| `Is_Draft` | `bit` | NULL | No | Is Draft |
| `Billing_Status` | `varchar(500)` | NULL | No | Billing Status |
| `Date_of_Service` | `datetime` | NULL | No | Date of Service; date/time attribute |
| `Procedure_Code` | `varchar(5)` | NULL | No | Procedure Code |
| `Sequence_Number` | `bigint` | NULL | No | Sequence Number |
| `Sales_Rep` | `bigint` | NULL | No | Sales Rep |
| `Claim_Type` | `varchar(150)` | NULL | No | Claim Type; claim identifier or claim-related attribute, depending on the name |
| `Due_Amount` | `float` | NULL | No | Due Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |

### `FactClaimCount`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Patient_Account` | `bigint` | NULL | No | Patient Account; patient-related attribute; handle according to applicable privacy controls |
| `Patient_Name` | `varchar(300)` | NULL | No | Patient Name; patient-related attribute; handle according to applicable privacy controls |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Procedure_Code` | `varchar(20)` | NULL | No | Procedure Code |
| `Sequence_Number` | `bigint` | NULL | No | Sequence Number |
| `Practice_Location_Id` | `bigint` | NULL | No | Practice Location Id |
| `Location_Name` | `varchar(100)` | NULL | No | Location Name |
| `Location_Type` | `varchar(100)` | NULL | No | Location Type |
| `Address_Line1` | `varchar(100)` | NULL | No | Address Line1 |
| `Zip` | `varchar(100)` | NULL | No | Zip |
| `City` | `varchar(100)` | NULL | No | City |
| `State` | `varchar(100)` | NULL | No | State |
| `Country` | `varchar(100)` | NULL | No | Country |
| `Ins_Payer_Name` | `varchar(100)` | NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |
| `Financial_Class` | `varchar(100)` | NULL | No | Financial Class |
| `POS` | `varchar(30)` | NULL | No | POS |
| `Claim_Category_Name` | `varchar(300)` | NULL | No | Claim Category Name; claim identifier or claim-related attribute, depending on the name |
| `Billing_Status` | `varchar(30)` | NULL | No | Billing Status |
| `Billing_Provider` | `varchar(130)` | NULL | No | Billing Provider |
| `Rendering_Provider` | `varchar(130)` | NULL | No | Rendering Provider |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `DOE` | `date` | NULL | No | DOE; date/time attribute |
| `POS_Name` | `varchar(200)` | NULL | No | POS Name |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Amount_Paid` | `money` | NULL | No | Amount Paid; financial amount or balance; currency and sign conventions are not specified here |
| `Amount_Adjusted` | `money` | NULL | No | Amount Adjusted; financial amount or balance; currency and sign conventions are not specified here |
| `Withheld_Amount` | `money` | NULL | No | Withheld Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Insurance_Paid` | `money` | NULL | No | Insurance Paid; financial amount or balance; currency and sign conventions are not specified here |
| `Patient_Paid` | `money` | NULL | No | Patient Paid; patient-related attribute; handle according to applicable privacy controls |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Rendering_Provider_ID` | `bigint` | NULL | No | Rendering Provider ID |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |

### `FactClaimCptReport`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Created_Date` | `date` | NULL | No | Created Date; date/time attribute |
| `Practice_Facility_Id` | `bigint` | NULL | No | Practice Facility Id |
| `Location_ID` | `bigint` | NULL | No | Location ID |
| `Billing_Provider` | `bigint` | NULL | No | Billing Provider |
| `Billing_Status` | `varchar(30)` | NULL | No | Billing Status |
| `Is_Draft` | `bit` | NULL | No | Is Draft |
| `Client_Bill` | `bit` | NULL | No | Client Bill |
| `Sales_Rep_id` | `bigint` | NULL | No | Sales Rep id |
| `Sales_Rep` | `varchar(250)` | NULL | No | Sales Rep |
| `Claim_charges_Id` | `bigint` | NULL | No | Claim charges Id; claim identifier or claim-related attribute, depending on the name |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `DOE` | `date` | NULL | No | DOE; date/time attribute |
| `Procedure_Code` | `varchar(50)` | NULL | No | Procedure Code |
| `Sequence_Number` | `bigint` | NULL | No | Sequence Number |
| `P_Sequence_Number` | `bigint` | NULL | No | P Sequence Number |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Financial_Class` | `varchar(100)` | NULL | No | Financial Class |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Expected_Fee` | `money` | NULL | No | Expected Fee |
| `Units` | `decimal(18, 2)` | NULL | No | Units |
| `DOP` | `date` | NULL | No | DOP; date/time attribute |
| `Check_Date` | `date` | NULL | No | Check Date; date/time attribute |
| `Insurance_Addresses_Id` | `bigint` | NULL | No | Insurance Addresses Id |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Insurance_Type` | `varchar(15)` | NULL | No | Insurance Type |
| `Responsible_Party` | `varchar(30)` | NULL | No | Responsible Party |
| `Amount_Approved` | `decimal(38, 2)` | NULL | No | Amount Approved; financial amount or balance; currency and sign conventions are not specified here |
| `Insurance_Payments` | `money` | NULL | No | Insurance Payments |
| `Patient_Payments` | `money` | NULL | No | Patient Payments; patient-related attribute; handle according to applicable privacy controls |
| `Client_Bill_Payments` | `money` | NULL | No | Client Bill Payments |
| `Contractual_Adjustments` | `money` | NULL | No | Contractual Adjustments |
| `Manual_Adjustments` | `money` | NULL | No | Manual Adjustments |
| `Withheld_Amount` | `money` | NULL | No | Withheld Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Copay` | `money` | NULL | No | Copay |
| `Deductible` | `money` | NULL | No | Deductible |
| `Coinsurance` | `money` | NULL | No | Coinsurance |
| `Insurance_Balance` | `money` | NULL | No | Insurance Balance; financial amount or balance; currency and sign conventions are not specified here |
| `Patient_Balance` | `money` | NULL | No | Patient Balance; patient-related attribute; handle according to applicable privacy controls |
| `Paid` | `bigint` | NULL | No | Paid; financial amount or balance; currency and sign conventions are not specified here |
| `ADJ` | `bigint` | NULL | No | ADJ |
| `Billed_Date` | `date` | NULL | No | Billed Date; date/time attribute |
| `Last_Billed_Date` | `date` | NULL | No | Last Billed Date; date/time attribute |
| `Paid_To_Patient` | `money` | NULL | No | Paid To Patient; patient-related attribute; handle according to applicable privacy controls |
| `COGS` | `money` | NULL | No | COGS |
| `Include_in_submission` | `bit` | NULL | No | Include in submission |
| `PCharge_Amount` | `money` | NULL | No | PCharge Amount; financial amount or balance; currency and sign conventions are not specified here |

### `FactClaimInterestReport`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Claim_NO` | `bigint` | NULL | No | Claim NO; claim identifier or claim-related attribute, depending on the name |
| `Practice_Facility_Id` | `bigint` | NULL | No | Practice Facility Id |
| `Created_Date` | `date` | NULL | No | Created Date; date/time attribute |
| `Location_ID` | `bigint` | NULL | No | Location ID |
| `Billing_Provider` | `bigint` | NULL | No | Billing Provider |
| `Billing_Status` | `varchar(30)` | NULL | No | Billing Status |
| `Is_Draft` | `bit` | NULL | No | Is Draft |
| `Client_Bill` | `bit` | NULL | No | Client Bill |
| `Sales_Rep_id` | `bigint` | NULL | No | Sales Rep id |
| `Sales_Rep` | `varchar(104)` | NULL | No | Sales Rep |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `DOE` | `date` | NULL | No | DOE; date/time attribute |
| `DOP` | `date` | NULL | No | DOP; date/time attribute |
| `Check_Date` | `date` | NULL | No | Check Date; date/time attribute |
| `Insurance_Addresses_Id` | `bigint` | NULL | No | Insurance Addresses Id |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Interest_Amount` | `decimal(38, 2)` | NULL | No | Interest Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Financial_Class` | `varchar(55)` | NULL | No | Financial Class |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Billed_Date` | `date` | NULL | No | Billed Date; date/time attribute |
| `Last_Billed_Date` | `date` | NULL | No | Last Billed Date; date/time attribute |

### `FactClaimResubmission`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `ID` | `int` | NOT NULL | Yes | ID |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Billing_Provider_Id` | `bigint` | NULL | No | Billing Provider Id |
| `Billing_Provider` | `varchar(130)` | NULL | No | Billing Provider |
| `Submission_Type` | `varchar(50)` | NULL | No | Submission Type |
| `DOS` | `datetime` | NULL | No | DOS; date/time attribute |
| `Submission_Date` | `datetime` | NULL | No | Submission Date; date/time attribute |
| `Practice_Location_Id` | `bigint` | NULL | No | Practice Location Id |
| `Location_Name` | `varchar(100)` | NULL | No | Location Name |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Ins_Payer_Name` | `varchar(100)` | NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |
| `Insurance_Type` | `varchar(5)` | NULL | No | Insurance Type |
| `RowNumDateOfSubmission` | `int` | NULL | No | Row Num Date Of Submission; date/time attribute |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |

### `FactClaimsSubmissionAccountReceivable`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Date_of_Service` | `datetime` | NULL | No | Date of Service; date/time attribute |
| `Procedure_Code` | `varchar(5)` | NULL | No | Procedure Code |
| `Created_Date` | `datetime` | NULL | No | Created Date; date/time attribute |
| `Deleted` | `bit` | NULL | No | Deleted |

### `FactCPT_KPI`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key detected: `FactCPT_KPI_ID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `FactCPT_KPI_ID` | `int` | NOT NULL | Yes | Fact CPT KPI ID |
| `Accession_No` | `varchar(20)` | NULL | No | Accession No |
| `Withheld_Amount` | `money` | NOT NULL | No | Withheld Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Patient_Account` | `bigint` | NOT NULL | No | Patient Account; patient-related attribute; handle according to applicable privacy controls |
| `Sales_Rep` | `varchar(54)` | NOT NULL | No | Sales Rep |
| `Rejected_By` | `varchar(25)` | NULL | No | Rejected By |
| `Is_Draft` | `bit` | NULL | No | Is Draft |
| `Hold_Submission` | `bit` | NOT NULL | No | Hold Submission |
| `Hold_Submission_Reason` | `varchar(200)` | NOT NULL | No | Hold Submission Reason |
| `Claim_Category_Name` | `varchar(300)` | NOT NULL | No | Claim Category Name; claim identifier or claim-related attribute, depending on the name |
| `Assigned_To` | `varchar(8000)` | NULL | No | Assigned To |
| `Patient` | `varchar(54)` | NOT NULL | No | Patient; patient-related attribute; handle according to applicable privacy controls |
| `P_Alias` | `varchar(100)` | NOT NULL | No | P Alias |
| `L_Alias` | `varchar(20)` | NOT NULL | No | L Alias |
| `DOB` | `datetime` | NOT NULL | No | DOB; patient-related attribute; handle according to applicable privacy controls |
| `Gender` | `varchar(20)` | NOT NULL | No | Gender |
| `Patient_Primary_Phone` | `varchar(15)` | NULL | No | Patient Primary Phone; patient-related attribute; handle according to applicable privacy controls |
| `Patient_Secondary_Phone` | `varchar(15)` | NULL | No | Patient Secondary Phone; patient-related attribute; handle according to applicable privacy controls |
| `Claim_No` | `bigint` | NOT NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `DOS` | `datetime` | NULL | No | DOS; date/time attribute |
| `Start_DOS` | `datetime` | NULL | No | Start DOS |
| `End_DOS` | `datetime` | NULL | No | End DOS |
| `CPT` | `varchar(50)` | NULL | No | CPT |
| `Modifier` | `varchar(14)` | NULL | No | Modifier |
| `ICD1` | `varchar(8)` | NOT NULL | No | ICD1 |
| `Diagnosis` | `varchar(8000)` | NULL | No | Diagnosis |
| `Billing_Provider` | `varchar(233)` | NOT NULL | No | Billing Provider |
| `Rendering_Provider` | `varchar(233)` | NOT NULL | No | Rendering Provider |
| `Location_Name` | `varchar(100)` | NULL | No | Location Name |
| `Practice_Location_Id` | `bigint` | NULL | No | Practice Location Id |
| `Patient_Primary_Location` | `varchar(100)` | NULL | No | Patient Primary Location; patient-related attribute; handle according to applicable privacy controls |
| `Units` | `numeric(9, 2)` | NULL | No | Units |
| `Charge_Amount` | `money` | NOT NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Expected_Fee` | `numeric(38, 2)` | NULL | No | Expected Fee |
| `Patient_Responsibility` | `money` | NULL | No | Patient Responsibility; patient-related attribute; handle according to applicable privacy controls |
| `Sequence_Number` | `int` | NOT NULL | No | Sequence Number |
| `Entered_Date` | `datetime` | NOT NULL | No | Entered Date; date/time attribute |
| `Entered_By` | `varchar(133)` | NULL | No | Entered By |
| `Practice_Name` | `varchar(100)` | NOT NULL | No | Practice Name |
| `Tax_Id` | `varchar(10)` | NOT NULL | No | Tax Id |
| `Authorization_Number` | `varchar(50)` | NULL | No | Authorization Number |
| `Posting_Date` | `date` | NULL | No | Posting Date; date/time attribute |
| `Responsible_Party_Name` | `varchar(100)` | NOT NULL | No | Responsible Party Name |
| `Referring_Physician` | `varchar(153)` | NOT NULL | No | Referring Physician |
| `Supervising_Physician` | `varchar(232)` | NOT NULL | No | Supervising Physician |
| `Primary_Policy_Type` | `varchar(55)` | NOT NULL | No | Primary Policy Type |
| `Place_of_Service` | `varchar(75)` | NOT NULL | No | Place of Service |
| `Insurance_Payments` | `money` | NOT NULL | No | Insurance Payments |
| `Patient_Payments` | `money` | NOT NULL | No | Patient Payments; patient-related attribute; handle according to applicable privacy controls |
| `Contractual_Adjustments` | `money` | NOT NULL | No | Contractual Adjustments |
| `Manual_Adjustments` | `money` | NOT NULL | No | Manual Adjustments |
| `Total_Paid` | `money` | NOT NULL | No | Total Paid; financial amount or balance; currency and sign conventions are not specified here |
| `Due_Amount` | `money` | NULL | No | Due Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Payment_Due_Days` | `int` | NULL | No | Payment Due Days |
| `Referring_Facility` | `varchar(100)` | NULL | No | Referring Facility |
| `Client_Bill_Balance` | `money` | NOT NULL | No | Client Bill Balance; financial amount or balance; currency and sign conventions are not specified here |
| `Case_Number` | `bigint` | NOT NULL | No | Case Number |
| `Primary_Insurance_ID` | `varchar(25)` | NOT NULL | No | Primary Insurance ID |
| `Primary_Subscriber_Number` | `varchar(80)` | NOT NULL | No | Primary Subscriber Number |
| `Secondary_Insurance` | `varchar(100)` | NOT NULL | No | Secondary Insurance |
| `Secondary_Insurance_ID` | `varchar(25)` | NOT NULL | No | Secondary Insurance ID |
| `Secondary_Policy_Type` | `varchar(55)` | NOT NULL | No | Secondary Policy Type |
| `Secondary_Subscriber_Number` | `varchar(80)` | NOT NULL | No | Secondary Subscriber Number |
| `Tertiary_Insurance` | `varchar(100)` | NOT NULL | No | Tertiary Insurance |
| `Tertiary_Insurance_ID` | `varchar(25)` | NOT NULL | No | Tertiary Insurance ID |
| `Tertiary_Policy_Type` | `varchar(55)` | NOT NULL | No | Tertiary Policy Type |
| `Tertiary_Subscriber_Number` | `varchar(80)` | NOT NULL | No | Tertiary Subscriber Number |
| `Billing_Status` | `varchar(50)` | NULL | No | Billing Status |
| `Referral_Source` | `varchar(100)` | NULL | No | Referral Source |
| `Entered_date_by_month` | `varchar(7)` | NULL | No | Entered date by month; date/time attribute |
| `Posting_date_by_month` | `varchar(7)` | NULL | No | Posting date by month; date/time attribute |
| `DOS_date_by_month` | `varchar(7)` | NULL | No | DOS date by month; date/time attribute |
| `Status` | `nvarchar(100)` | NULL | No | Status; status value, allowed values are not specified in the DDL |
| `Charge_Reponsibility` | `nvarchar(100)` | NULL | No | Charge Reponsibility; financial amount or balance; currency and sign conventions are not specified here |
| `Insurance_Balance` | `money` | NULL | No | Insurance Balance; financial amount or balance; currency and sign conventions are not specified here |
| `Patient_Balance` | `money` | NULL | No | Patient Balance; patient-related attribute; handle according to applicable privacy controls |
| `Billed_Date` | `date` | NULL | No | Billed Date; date/time attribute |
| `Last_Billed_Date` | `date` | NULL | No | Last Billed Date; date/time attribute |
| `Billed_date_by_month` | `varchar(7)` | NULL | No | Billed date by month; date/time attribute |
| `Last_billed_date_by_month` | `varchar(7)` | NULL | No | Last billed date by month; date/time attribute |
| `TotalDue` | `money` | NULL | No | Total Due |
| `Phy_Name` | `varchar(75)` | NULL | No | Phy Name |
| `Provider_Code` | `bigint` | NULL | No | Provider Code |
| `rpProviderCode` | `bigint` | NULL | No | rp Provider Code |
| `Supervising_Provider` | `bigint` | NULL | No | Supervising Provider |
| `Ordering_Provider` | `bigint` | NULL | No | Ordering Provider |
| `Ordering_Provider_Name` | `varchar(232)` | NOT NULL | No | Ordering Provider Name |
| `Panel_Name` | `varchar(300)` | NULL | No | Panel Name |
| `Performing_Lab` | `varchar(100)` | NOT NULL | No | Performing Lab |
| `CPT_Description` | `varchar(500)` | NULL | No | CPT Description |
| `Claim_Type` | `varchar(50)` | NULL | No | Claim Type; claim identifier or claim-related attribute, depending on the name |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Practice_Code` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `Revenue_Code` | `varchar(50)` | NULL | No | Revenue Code |
| `Practice_Facility_Id` | `bigint` | NULL | No | Practice Facility Id |
| `Billing_Provider_Code` | `bigint` | NULL | No | Billing Provider Code |
| `Practice_sales_Rep_Id` | `bigint` | NULL | No | Practice sales Rep Id |
| `Expected_Reimbursement` | `money` | NULL | No | Expected Reimbursement |

### `FactDaysARbyPayer`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Ins_Payer_Name` | `varchar(500)` | NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Location_ID` | `bigint` | NULL | No | Location ID |
| `Provider_ID` | `bigint` | NULL | No | Provider ID |
| `Insurance_Type` | `varchar(15)` | NULL | No | Insurance Type |
| `Insurance_Addresses_Id` | `bigint` | NULL | No | Insurance Addresses Id |
| `Procedure_Code` | `varchar(50)` | NULL | No | Procedure Code |
| `Sequence_Number` | `bigint` | NULL | No | Sequence Number |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Billed_Date` | `date` | NULL | No | Billed Date; date/time attribute |
| `Insurance_Balance` | `money` | NULL | No | Insurance Balance; financial amount or balance; currency and sign conventions are not specified here |
| `Patient_Balance` | `money` | NULL | No | Patient Balance; patient-related attribute; handle according to applicable privacy controls |

### `FactDaysToBill`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `First_Submission_Date` | `date` | NULL | No | First Submission Date; date/time attribute |
| `Practice_Location_Id` | `bigint` | NULL | No | Practice Location Id |
| `Billing_Provider_Id` | `bigint` | NULL | No | Billing Provider Id |
| `Billing_Provider` | `varchar(130)` | NULL | No | Billing Provider |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `DOE` | `date` | NULL | No | DOE; date/time attribute |
| `Location_Name` | `varchar(100)` | NULL | No | Location Name |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Ins_Payer_Name` | `varchar(100)` | NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |
| `DOSAge` | `int` | NULL | No | DOSAge |
| `DOEAge` | `int` | NULL | No | DOEAge |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |

### `FactFacilitySalesRep`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `FactFacilitySalesRepID` | `bigint` | NOT NULL | Yes | Fact Facility Sales Rep ID |
| `Practice_Facilities_Sale_Reps_Id` | `bigint` | NULL | No | Practice Facilities Sale Reps Id |
| `Practice_Facility_Id` | `bigint` | NULL | No | Practice Facility Id |
| `Sale_Reps_Id` | `bigint` | NULL | No | Sale Reps Id |
| `Effective_From` | `datetime` | NULL | No | Effective From; likely start of the effective period |
| `Effective_To` | `datetime` | NULL | No | Effective To; likely end of the effective period |
| `First_Name` | `varchar(50)` | NULL | No | First Name |
| `Last_Name` | `varchar(50)` | NULL | No | Last Name |
| `Middle_Initial` | `varchar(1)` | NULL | No | Middle Initial |
| `Date_of_Birth` | `datetime` | NULL | No | Date of Birth; date/time attribute |
| `Email` | `varchar(100)` | NULL | No | Email |
| `Address` | `varchar(500)` | NULL | No | Address |
| `City` | `varchar(50)` | NULL | No | City |
| `State` | `varchar(2)` | NULL | No | State |
| `Zip` | `varchar(10)` | NULL | No | Zip |
| `Country` | `varchar(100)` | NULL | No | Country |
| `StartDate` | `datetime` | NOT NULL | No | Start Date; likely start of the effective period |
| `EndDate` | `datetime` | NOT NULL | No | End Date; likely end of the effective period |
| `Status` | `varchar(1)` | NOT NULL | No | Status; status value, allowed values are not specified in the DDL |

### `FactFinancialClass`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key detected: `FactFinancialClassID`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `FactFinancialClassID` | `int` | NOT NULL | Yes | Fact Financial Class ID |
| `TotalClaims` | `bigint` | NOT NULL | No | Total Claims; claim identifier or claim-related attribute, depending on the name |
| `Totaldenials` | `bigint` | NULL | No | Totaldenials |
| `TotalPayClaims` | `bigint` | NULL | No | Total Pay Claims; claim identifier or claim-related attribute, depending on the name |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `Financial_Class` | `varchar(55)` | NULL | No | Financial Class |
| `Practice_Facility_Id` | `bigint` | NULL | No | Practice Facility Id |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Date_of_Entry` | `date` | NULL | No | Date of Entry; date/time attribute |
| `Date_of_Service` | `date` | NULL | No | Date of Service; date/time attribute |
| `Total_Paid` | `money` | NULL | No | Total Paid; financial amount or balance; currency and sign conventions are not specified here |
| `Location_ID` | `bigint` | NULL | No | Location ID |
| `Billing_Provider` | `bigint` | NULL | No | Billing Provider |
| `Sales_Rep` | `bigint` | NULL | No | Sales Rep |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Check_Date` | `datetime` | NULL | No | Check Date; date/time attribute |

### `FactGlobalKPIClaimData`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Practice_code` | `bigint` | NULL | No | Practice code; practice identifier |
| `Practice_Name` | `varchar(100)` | NULL | No | Practice Name |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Accession_No` | `varchar(50)` | NULL | No | Accession No |
| `Patient_Name` | `varchar(114)` | NULL | No | Patient Name; patient-related attribute; handle according to applicable privacy controls |
| `Date_of_Birth` | `datetime` | NULL | No | Date of Birth; date/time attribute |
| `Date_of_Service` | `datetime` | NULL | No | Date of Service; date/time attribute |
| `Claim_Category_Name` | `varchar(300)` | NULL | No | Claim Category Name; claim identifier or claim-related attribute, depending on the name |
| `Primary_Insurance` | `varchar(100)` | NULL | No | Primary Insurance |
| `Primary_Subscriber_Number` | `varchar(80)` | NULL | No | Primary Subscriber Number |
| `Claim_Type` | `varchar(13)` | NULL | No | Claim Type; claim identifier or claim-related attribute, depending on the name |
| `Claim_Status` | `varchar(50)` | NULL | No | Claim Status; claim identifier or claim-related attribute, depending on the name |
| `Entered_Date` | `datetime` | NULL | No | Entered Date; date/time attribute |
| `Entered_By` | `varchar(55)` | NULL | No | Entered By |
| `First_Billed_Date` | `datetime` | NULL | No | First Billed Date; date/time attribute |
| `Last_Billed_Date` | `datetime` | NULL | No | Last Billed Date; date/time attribute |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Allowed` | `decimal(38, 2)` | NULL | No | Allowed |
| `Insurance_Payments` | `money` | NULL | No | Insurance Payments |
| `Patient_Payments` | `money` | NULL | No | Patient Payments; patient-related attribute; handle according to applicable privacy controls |
| `Contractual_Adjustments` | `money` | NULL | No | Contractual Adjustments |
| `Manual_Adjustments` | `money` | NULL | No | Manual Adjustments |
| `Withheld_Amount` | `money` | NULL | No | Withheld Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Outstanding_Amount` | `money` | NULL | No | Outstanding Amount; financial amount or balance; currency and sign conventions are not specified here |

### `FactGlobalKPIClaimSubmissionsHistory`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |
| `Claim_Type` | `varchar(13)` | NULL | No | Claim Type; claim identifier or claim-related attribute, depending on the name |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Submission_Type` | `varchar(20)` | NULL | No | Submission Type |
| `Insurance_Type` | `varchar(20)` | NULL | No | Insurance Type |
| `Ins_Payer_ID` | `bigint` | NULL | No | Ins Payer ID; insurance payer identifier or name, depending on the data type |
| `Submission_Date_Time` | `date` | NULL | No | Submission Date Time; date/time attribute |
| `Claim_Status` | `varchar(10)` | NULL | No | Claim Status; claim identifier or claim-related attribute, depending on the name |
| `Claim_Status_Date` | `date` | NULL | No | Claim Status Date; claim identifier or claim-related attribute, depending on the name |

### `FactGlobalKPICPTClaimData`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Practice_code` | `bigint` | NULL | No | Practice code; practice identifier |
| `Practice_Name` | `varchar(100)` | NULL | No | Practice Name |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Accession_No` | `varchar(50)` | NULL | No | Accession No |
| `Patient_Name` | `varchar(114)` | NULL | No | Patient Name; patient-related attribute; handle according to applicable privacy controls |
| `Date_of_Birth` | `datetime` | NULL | No | Date of Birth; date/time attribute |
| `Date_of_Service` | `datetime` | NULL | No | Date of Service; date/time attribute |
| `Claim_Category_Name` | `varchar(300)` | NULL | No | Claim Category Name; claim identifier or claim-related attribute, depending on the name |
| `Primary_Insurance` | `varchar(100)` | NULL | No | Primary Insurance |
| `Primary_Subscriber_Number` | `varchar(80)` | NULL | No | Primary Subscriber Number |
| `CPT_Codes` | `varchar(50)` | NULL | No | CPT Codes |
| `CPT_Id` | `bigint` | NULL | No | CPT Id |
| `Diagnosis` | `varchar(8)` | NULL | No | Diagnosis |
| `Claim_Type` | `varchar(13)` | NULL | No | Claim Type; claim identifier or claim-related attribute, depending on the name |
| `Status` | `nvarchar(100)` | NULL | No | Status; status value, allowed values are not specified in the DDL |
| `Charge_Reponsibility` | `nvarchar(100)` | NULL | No | Charge Reponsibility; financial amount or balance; currency and sign conventions are not specified here |
| `Claim_Status` | `varchar(50)` | NULL | No | Claim Status; claim identifier or claim-related attribute, depending on the name |
| `Entered_Date` | `datetime` | NULL | No | Entered Date; date/time attribute |
| `Entered_By` | `varchar(55)` | NULL | No | Entered By |
| `First_Billed_Date` | `datetime` | NULL | No | First Billed Date; date/time attribute |
| `Last_Billed_Date` | `datetime` | NULL | No | Last Billed Date; date/time attribute |
| `Charge_Amount` | `money` | NULL | No | Charge Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Allowed` | `decimal(38, 2)` | NULL | No | Allowed |
| `Insurance_Payments` | `money` | NULL | No | Insurance Payments |
| `Patient_Payments` | `money` | NULL | No | Patient Payments; patient-related attribute; handle according to applicable privacy controls |
| `Contractual_Adjustments` | `money` | NULL | No | Contractual Adjustments |
| `Manual_Adjustments` | `money` | NULL | No | Manual Adjustments |
| `Withheld_Amount` | `money` | NULL | No | Withheld Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Outstanding_Amount` | `money` | NULL | No | Outstanding Amount; financial amount or balance; currency and sign conventions are not specified here |

### `FactPayerRejectionReason`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `ID` | `int` | NOT NULL | Yes | ID |
| `Claim_No` | `bigint` | NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Status_Reason` | `varchar(250)` | NULL | No | Status Reason |
| `Billing_Provider_Id` | `bigint` | NULL | No | Billing Provider Id |
| `Billing_Provider` | `varchar(130)` | NULL | No | Billing Provider |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `DOE` | `date` | NULL | No | DOE; date/time attribute |
| `Location_Name` | `varchar(100)` | NULL | No | Location Name |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Ins_Payer_Name` | `varchar(100)` | NULL | No | Ins Payer Name; insurance payer identifier or name, depending on the data type |
| `Submission_Date` | `date` | NULL | No | Submission Date; date/time attribute |
| `Claim_Status` | `varchar(10)` | NULL | No | Claim Status; claim identifier or claim-related attribute, depending on the name |
| `Practice_Location_Id` | `bigint` | NULL | No | Practice Location Id |
| `Claim_Status_Date` | `date` | NULL | No | Claim Status Date; claim identifier or claim-related attribute, depending on the name |
| `Claim_Status_Reason` | `varchar(max)` | NULL | No | Claim Status Reason; claim identifier or claim-related attribute, depending on the name |
| `Procedure_Code` | `varchar(max)` | NULL | No | Procedure Code |
| `Diagnosis` | `varchar(max)` | NULL | No | Diagnosis |
| `Rendering_Provider_Name` | `varchar(150)` | NULL | No | Rendering Provider Name |
| `Practice_Code` | `bigint` | NULL | No | Practice Code; practice identifier |

### `FactReportClaimPaymentDetails`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key detected: `Claim_Payment_Id`.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `Claim_Payment_Id` | `bigint` | NOT NULL | No | Claim Payment Id; claim identifier or claim-related attribute, depending on the name |
| `Batch_Number` | `bigint` | NULL | No | Batch Number |
| `Claim_No` | `bigint` | NOT NULL | No | Claim No; claim identifier or claim-related attribute, depending on the name |
| `Practice_Code` | `bigint` | NOT NULL | No | Practice Code; practice identifier |
| `Practice` | `varchar(123)` | NULL | No | Practice |
| `Claim_Category_Id` | `bigint` | NULL | No | Claim Category Id; claim identifier or claim-related attribute, depending on the name |
| `Assigned_To_User` | `bigint` | NULL | No | Assigned To User |
| `Claim_Category_Name` | `varchar(300)` | NULL | No | Claim Category Name; claim identifier or claim-related attribute, depending on the name |
| `AssignedTo` | `varchar(133)` | NULL | No | Assigned To |
| `PatientPayment_Faciltiy` | `varchar(300)` | NULL | No | Patient Payment Faciltiy; patient-related attribute; handle according to applicable privacy controls |
| `Practice_Location_Id` | `bigint` | NULL | No | Practice Location Id |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `PatPractice_Facility_Id` | `bigint` | NULL | No | Pat Practice Facility Id |
| `PmntPractice_Facility_Id` | `bigint` | NULL | No | Pmnt Practice Facility Id |
| `Financial_Class_ID` | `bigint` | NULL | No | Financial Class ID |
| `pfacPractice_Facility_Id` | `bigint` | NULL | No | pfac Practice Facility Id |
| `Claim_Modified_Date` | `datetime` | NULL | No | Claim Modified Date; claim identifier or claim-related attribute, depending on the name |
| `Claim_Deleted` | `bit` | NULL | No | Claim Deleted; claim identifier or claim-related attribute, depending on the name |
| `Active_Practice` | `bit` | NULL | No | Active Practice |
| `P_Alias` | `varchar(100)` | NULL | No | P Alias |
| `L_Alias` | `varchar(50)` | NULL | No | L Alias |
| `Referring_Source` | `varchar(300)` | NULL | No | Referring Source |
| `Billing_Status` | `varchar(30)` | NULL | No | Billing Status |
| `Claim_Type` | `varchar(50)` | NULL | No | Claim Type; claim identifier or claim-related attribute, depending on the name |
| `Accession_No` | `varchar(50)` | NULL | No | Accession No |
| `Patient_Account` | `bigint` | NULL | No | Patient Account; patient-related attribute; handle according to applicable privacy controls |
| `Patient` | `varchar(89)` | NULL | No | Patient; patient-related attribute; handle according to applicable privacy controls |
| `Billing_Provider` | `varchar(183)` | NULL | No | Billing Provider |
| `Supervising_Provider` | `varchar(183)` | NULL | No | Supervising Provider |
| `Ordering_Provider_Name` | `varchar(183)` | NULL | No | Ordering Provider Name |
| `Sales_Rep_Name` | `varchar(104)` | NULL | No | Sales Rep Name |
| `Referring_Provider` | `varchar(75)` | NULL | No | Referring Provider |
| `Location` | `varchar(100)` | NULL | No | Location |
| `DOS` | `date` | NULL | No | DOS; date/time attribute |
| `Payer` | `varchar(100)` | NULL | No | Payer; insurance payer identifier or name, depending on the data type |
| `Sales_Rep_Id` | `bigint` | NULL | No | Sales Rep Id |
| `Supervising_Provider_Claim` | `bigint` | NULL | No | Supervising Provider Claim; claim identifier or claim-related attribute, depending on the name |
| `Ordering_Provider` | `bigint` | NULL | No | Ordering Provider |
| `Deposit_Date` | `date` | NULL | No | Deposit Date; date/time attribute |
| `Payment_Description` | `varchar(401)` | NULL | No | Payment Description |
| `Units` | `decimal(9, 2)` | NULL | No | Units |
| `Payment_Method` | `varchar(100)` | NULL | No | Payment Method |
| `Payment_Source` | `varchar(50)` | NULL | No | Payment Source |
| `Entered_By` | `varchar(133)` | NULL | No | Entered By |
| `Entered_Date` | `datetime` | NULL | No | Entered Date; date/time attribute |
| `Posting_Date` | `datetime` | NULL | No | Posting Date; date/time attribute |
| `Responsible_Party` | `varchar(30)` | NULL | No | Responsible Party |
| `Procedure_code` | `varchar(5)` | NULL | No | Procedure code |
| `Sequence_Number` | `bigint` | NULL | No | Sequence Number |
| `Revenue_Code` | `varchar(50)` | NULL | No | Revenue Code |
| `Unapplied` | `int` | NULL | No | Unapplied |
| `Amount_Approved` | `money` | NULL | No | Amount Approved; financial amount or balance; currency and sign conventions are not specified here |
| `Amount_Paid` | `money` | NULL | No | Amount Paid; financial amount or balance; currency and sign conventions are not specified here |
| `Amount_Adjusted` | `money` | NULL | No | Amount Adjusted; financial amount or balance; currency and sign conventions are not specified here |
| `Withheld_Amount` | `money` | NULL | No | Withheld Amount; financial amount or balance; currency and sign conventions are not specified here |
| `CoInsurance` | `money` | NULL | No | Co Insurance |
| `CoPayment` | `money` | NULL | No | Co Payment |
| `Deductible` | `money` | NULL | No | Deductible |
| `Check_Number` | `varchar(150)` | NULL | No | Check Number |
| `Actual_Check_Date` | `date` | NULL | No | Actual Check Date; date/time attribute |
| `Check_Date` | `date` | NULL | No | Check Date; date/time attribute |
| `Batch_Date` | `date` | NULL | No | Batch Date; date/time attribute |
| `Billed_Amount` | `money` | NULL | No | Billed Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Entered_Date_by_month` | `nvarchar(4000)` | NULL | No | Entered Date by month; date/time attribute |
| `DOS_Date_by_month` | `nvarchar(4000)` | NULL | No | DOS Date by month; date/time attribute |
| `Check_Date_by_month` | `nvarchar(4000)` | NULL | No | Check Date by month; date/time attribute |
| `Batch_Date_by_month` | `date` | NULL | No | Batch Date by month; date/time attribute |
| `Modifier1` | `varchar(2)` | NULL | No | Modifier1 |
| `Modifier2` | `varchar(2)` | NULL | No | Modifier2 |
| `Modifier3` | `varchar(2)` | NULL | No | Modifier3 |
| `Modifier4` | `varchar(2)` | NULL | No | Modifier4 |
| `Adjustment_Reason` | `varchar(200)` | NULL | No | Adjustment Reason |
| `BWC_Check_Amount` | `money` | NULL | No | BWC Check Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Payment_Check_Amount` | `money` | NULL | No | Payment Check Amount; financial amount or balance; currency and sign conventions are not specified here |
| `Payment_Faciltiy` | `varchar(300)` | NULL | No | Payment Faciltiy |
| `Comments` | `varchar(200)` | NULL | No | Comments |
| `Batch_wise_checks_id` | `bigint` | NULL | No | Batch wise checks id |
| `Is_Rectified` | `bit` | NULL | No | Is Rectified |
| `Rectified_Entry_ID` | `bigint` | NULL | No | Rectified Entry ID |
| `Modified_Date` | `datetime` | NULL | No | Modified Date; date/time attribute |
| `Row_Version` | `varbinary(8000)` | NULL | No | Row Version |
| `Client_Bill` | `bit` | NOT NULL | No | Client Bill |

### `MAX_GlobalKPIClaimPaymentDenial`

Fact, reporting, or summary table. Its exact grain and calculation rules are not defined by the table DDL alone.

Primary key: no primary-key constraint detected by the parser.

| Column | SQL type | Nullability | Identity | Description / interpretation |
|---|---|---|---|---|
| `practice_code` | `bigint` | NULL | No | practice code; practice identifier |
| `claim_no` | `bigint` | NULL | No | claim no; claim identifier or claim-related attribute, depending on the name |
| `Ins_Payer_Id` | `bigint` | NULL | No | Ins Payer Id; insurance payer identifier or name, depending on the data type |
| `Insurance_Type` | `varchar(20)` | NULL | No | Insurance Type |
| `DOP` | `date` | NULL | No | DOP; date/time attribute |
| `copay` | `money` | NULL | No | copay |
| `coinsurance` | `money` | NULL | No | coinsurance |
| `Deductible` | `money` | NULL | No | Deductible |
| `Combined_Remarks` | `varchar(35)` | NULL | No | Combined Remarks |
| `Combined_Adjustments` | `varchar(71)` | NULL | No | Combined Adjustments |
| `Submission_Date_Time` | `datetime` | NULL | No | Submission Date Time; date/time attribute |

## Data model notes

- Names beginning with `Dim` appear to represent dimensions or reference data, based on naming convention.
- Names beginning with `Fact` appear to represent reporting facts, aggregates, or extracts. Confirm the row grain for each table before joining or aggregating.
- The script contains several similar or overlapping reporting tables. Do not assume they contain equivalent data without comparing load logic and row-level definitions.
- Some tables have no primary key defined in the supplied DDL. Confirm whether this is intentional and whether a business key exists.
- The DDL includes patient and financial fields. Apply appropriate access controls and data-handling rules.

## Recommended follow-up

1. Add confirmed business descriptions and the intended row grain for each table.
2. Document foreign-key or logical join relationships, including key uniqueness and cardinality.
3. Document valid values for status, type, category, and responsibility fields.
4. Document ETL source, refresh frequency, transformation rules, and data owner.
5. Validate the primary-key detection against the full DDL, especially constraints that use named or composite keys.
