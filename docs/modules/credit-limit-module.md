# Credit Limit Module

This document describes the Credit Limit module as implemented in the current JSAPNEW codebase. It focuses on request creation, approval, notification, SAP Service Layer update, HANA customer lookup, attachment handling, security, and production debugging.

The module is implemented primarily as an API and service layer. No dedicated checked-in Razor view or `credit-limit.js` file was found for this module. The UI entry point may be supplied by another project, an uncommitted view, or a dynamic menu route. The backend is complete enough to support a frontend through `/api/CreditLimit/*`.

# Module Architecture

```text
Frontend / API client
  -> CreditLimitController.cs
  -> ICreditLimitService
  -> CreditLimitService.cs
  -> SQL Server cl schema stored procedures
  -> approval flow stored procedures
  -> NotificationService / Firebase / nt schema
  -> Bom2Service SAP session
  -> SAP Service Layer BusinessPartners PATCH
  -> cl.updateHanaStatus
```

## Related Files

| File | Purpose |
|---|---|
| `Controllers/CreditLimitController.cs` | Public API surface for Credit Limit creation, document lists, approval, rejection, HANA status updates, notifications, and detail views. |
| `Services/Interfaces/ICreditLimitService.cs` | Service contract consumed by the controller. |
| `Services/Implementation/CreditLimitService.cs` | Main implementation: SQL Server procedure calls, HANA customer lookups, attachment persistence, notifications, SAP Service Layer PATCH. |
| `Models/CreditLimitModels.cs` | DTOs for create requests, document lists, approval flow, approval/rejection, HANA status, attachments, and details. |
| `Services/Implementation/Bom2Service.cs` | Supplies SAP Service Layer sessions for OIL, BEVERAGES, and MART companies. |
| `Services/Implementation/NotificationService.cs` | Gets FCM tokens, sends Firebase push notifications, inserts notification rows. |
| `Services/Interfaces/INotificationService.cs` | Notification service contract. |
| `Services/Implementation/UserService.cs` | Provides `GetActiveUser()` used by pending reminder notifications. |
| `Program.cs` | Registers `ICreditLimitService`, `SmartAuth`, global authorization, rate limiting, session, and middleware. |
| `Controllers/FileController.cs` | Used indirectly for attachment download URLs through `AdvanceDownloadFile`. |
| `Views/BPmasterweb/Index.cshtml`, `Views/BPmasterweb/Index1.cshtml` | Contain BP master `Credit Limit` form fields, but these are BP creation screens, not the Credit Limit approval module UI. |
| `wwwroot/js/bp-creation.js` | Validates/logs BP credit-limit values for BP master creation; not the Credit Limit approval module client. |

## Dependency Injection

`Program.cs` registers:

```csharp
builder.Services.AddScoped<ICreditLimitService, CreditLimitService>();
builder.Services.AddScoped<IBom2Service, Bom2Service>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserService, UserService>();
```

`CreditLimitService` depends on:

```text
IConfiguration
IBom2Service
INotificationService
IUserService
```

## Configuration Used

| Configuration key | Used by | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `CreditLimitService` | SQL Server database with `cl` schema procedures. |
| `ConnectionStrings:LiveHanaConnection` | `CreditLimitService` | Company 1 OIL live HANA connection. |
| `ConnectionStrings:LiveBevHanaConnection` | `CreditLimitService` | Company 2 BEVERAGES live HANA connection. |
| `ConnectionStrings:LiveMartHanaConnection` | `CreditLimitService` | Company 3 MART live HANA connection. |
| `SapServiceLayer:BaseUrl` | `CreditLimitService` | SAP Service Layer base URL for `BusinessPartners` PATCH. |
| `Firebase:ServiceAccountPath` | `NotificationService` | Firebase service account used for FCM push notifications. |

# API Surface

All routes are under:

```text
/api/CreditLimit
```

The controller is decorated with:

```csharp
[Route("api/[controller]")]
[ApiController]
[Authorize]
```

## Creation and Customer Lookup APIs

| Method | Route | Controller action | Service method | Purpose |
|---|---|---|---|---|
| `POST` | `/OpenCslm` | `OpenCslm` | `OpenCslmAsync` | Calls HANA `OPENCSLM`; appears to create/open a CSLM record directly in HANA. |
| `GET` | `/GetCustomerCards?company={id}` | `GetCustomerCards` | `GetCustomerCardsAsync` | Reads customer cards from live HANA company schema. |
| `POST` | `/CreateCLDocument` | `CreateDocument` | `CreateDocumentAsync` | Creates SQL credit-limit approval document without attachment. |
| `POST` | `/CreateCLDocumentV2` | `CreateDocumentV2` | `CreateDocumentWithAttachmentAsyncV2` | Creates document using multipart form data and optional attachment. |

## Document List, Insight, and Detail APIs

| Method | Route | Service method | Purpose |
|---|---|---|---|
| `POST` | `/GetApprovedDocuments` | `GetApprovedDocumentsAsync` | Approved list for user/company/month. |
| `POST` | `/GetPendingDocuments` | `GetPendingDocumentsAsync` | Pending list for user/company/month. |
| `POST` | `/GetRejectedDocuments` | `GetRejectedDocumentsAsync` | Rejected list for user/company/month. |
| `POST` | `/GetAllDocuments` | `GetAllDocumentsAsync` | Merges pending, approved, rejected lists and assigns `Status`. |
| `POST` | `/GetCreditDocumentInsight` | `GetCreditDocumentInsightAsync` | Count summary for pending/approved/rejected. |
| `POST` | `/GetUserDocumentInsights` | `GetUserDocumentInsightsAsync` | Creator-based summary. |
| `GET` | `/GetDocumentDetail?documentId={id}` | `GetDocumentDetailAsync` | Legacy document detail. |
| `GET` | `/GetDocumentDetailV2?documentId={id}` | `GetCreditDocumentDetailAsyncV2` | Document detail plus attachments and download URLs. |
| `GET` | `/GetDocumentDetailUsingFlowId?flowId={id}` | `GetDocumentDetailUsingFlowIdAsync` | Detail lookup by approval flow id. |
| `POST` | `/GetUserDocumentsByCreatedByAndMonth` | `GetUserDocumentsAsync` | Creator/month/status filtered list. |

## Approval, SAP, and Notification APIs

| Method | Route | Service method | Purpose |
|---|---|---|---|
| `GET` | `/GetApprovalFlow?flowId={id}` | `GetApprovalFlowAsync` | Shows approval stage history for a flow. |
| `POST` | `/ApproveDocument` | `ApproveDocumentAsync`, then `UpdateCreditLimitAsync` | Approves a stage and always attempts HANA/SAP update after service success. |
| `POST` | `/RejectDocument` | `RejectDocumentAsync` | Rejects the approval flow. |
| `GET` | `/GetFlowStatus?flowId={id}` | `GetFlowStatusAsync` | Returns final flow status; SAP update expects `status == "A"`. |
| `POST` | `/UpdateHanaStatus` | `UpdateHanaStatusAsync` | Manually updates SQL HANA status fields. |
| `POST` | `/UpdateCreditLimitInHana?flowId={id}` | `UpdateCreditLimitAsync` | Manual/retry SAP Service Layer update. |
| `GET` | `/GetCLUserIdsSendNotifications?flowId={id}` | `GetCLUserIdsSendNotificatiosAsync` | Debugs notification recipient lookup. |
| `GET` | `/SendPendingCLCountNotification` | `SendPendingCLCountNotificationAsync` | Sends pending-count reminders to active users. |
| `GET` | `/GetCurrentUsersSendNotification?userDocumentId={id}` | `GetCurrentUsersSendNotificationAsync` | Debugs users in current approval stage. |

# Business Flow

## Standard Approval Lifecycle

```text
User selects customer from HANA customer cards
  -> frontend posts CreateCLDocument or CreateCLDocumentV2
  -> CreditLimitController validates non-null request
  -> CreditLimitService calls cl.jsCreateDocument
  -> SQL creates credit document and approval flow
  -> service calls cl.GetUsersInCurrentStage
  -> NotificationService sends FCM to current-stage approvers
  -> approver opens pending list
  -> approver calls ApproveDocument or RejectDocument
  -> cl.jsApproveDocument or cl.jsRejectDocument updates flow state
  -> approval notification recipients resolved by cl.jsCreditLimitNotify
  -> final HANA/SAP update attempted by controller
  -> cl.jsGetFlowStatus must return status A
  -> service patches SAP BusinessPartners('{CustomerCode}')
  -> cl.updateHanaStatus stores SAP success/failure text
```

## Create Without Attachment

```text
POST /api/CreditLimit/CreateCLDocument
  -> CreditLimitController.CreateDocument
  -> CreditLimitService.CreateDocumentAsync
  -> cl.jsCreateDocument
  -> output @newDocumentId
  -> cl.GetUsersInCurrentStage(@userDocumentId = newDocumentId)
  -> Firebase push + nt.jsInsertNotification
```

## Create With Attachment

```text
POST /api/CreditLimit/CreateCLDocumentV2
  -> multipart/form-data
  -> documentData JSON deserialized to CreateDocumentDtoV2
  -> if TotalEntries == 1, attachment is mandatory
  -> CreateDocumentAsyncV2
  -> cl.jsCreateDocument
  -> save file to wwwroot/Uploads/CreditLimit
  -> cl.jsInsertCreditDocumentAttachment
  -> SendNotificationAsync
```

Important implementation detail: `CreateDocumentWithAttachmentAsyncV2` accepts one `IFormFile attachment`, although `CreateDocumentDtoV2` also has `List<IFormFile> Attachments`. The active controller passes only the single form field `attachment`.

## Approval and SAP Update

```text
POST /api/CreditLimit/ApproveDocument
  -> CreditLimitService.ApproveDocumentAsync
  -> cl.jsApproveDocument
  -> cl.jsGetId
  -> cl.jsCreditLimitNotify
  -> NotificationService
  -> CreditLimitController calls UpdateCreditLimitAsync
  -> cl.jsGetFlowStatus
  -> cl.jsGetDocumentDetailUsingFlowId
  -> Bom2Service gets SAP session
  -> PATCH SapServiceLayer/BusinessPartners('{customerCode}')
  -> cl.updateHanaStatus
```

Important behavior: the controller attempts SAP/HANA update after any successful approval service response. `UpdateCreditLimitAsync` then protects itself by requiring `cl.jsGetFlowStatus` to return `status == "A"`. If the flow is not final approved, it records `"Flow is not approved from final stage"` through `cl.updateHanaStatus`.

## Rejection

```text
POST /api/CreditLimit/RejectDocument
  -> CreditLimitService.RejectDocumentAsync
  -> cl.jsRejectDocument
  -> returns "Document rejected successfully." or SQL error
```

No C# notification block exists in `RejectDocumentAsync`. If rejection notifications are required, they must be implemented in `cl.jsRejectDocument`, added to service code, or handled by a separate client-side refresh.

## Unsupported Lifecycle States

The code does not show explicit C# support for:

```text
Draft
Return for correction
Resubmit
Expired request
Cancellation
Escalation
Queued SAP retry
SAP failure notification
```

The active supported statuses exposed by service code are:

```text
Pending
Approved
Rejected
A for final approved flow status
HANA success/failure text
```

If the database procedures support more statuses, they are not represented as separate controller/service paths in the current code.

# Database Flow

The repository does not include SQL DDL or procedure bodies. Table names are therefore not directly visible, except for schema/procedure names. The `cl` schema is the source of truth for documents, approval flow, attachments, and HANA status.

## Stored Procedure Map

| Stored procedure | Called from | Inputs visible in C# | Output visible in C# | Business role |
|---|---|---|---|---|
| HANA `OPENCSLM` | `OpenCslmAsync` | `CardCode`, `CurrentLimit`, `NewLimit`, `ValidTill`, `createdBy`, `Balance` | `result_id` output | Creates/opens a CSLM record directly in live HANA schema. |
| HANA `GetCustomerCards` | `GetCustomerCardsAsync`, `GetCustomerNameByCodeAsync` | none | `CardCode`, `CardName`, `CardType`, `Balance`, `DebtLine`, `CreditLine` | Customer card dropdown/source of customer names. |
| `cl.jsCreateDocument` | `CreateDocumentAsync`, `CreateDocumentAsyncV2` | `@branchId`, `@customerCode`, `@customerValue`, `@currentBalance`, `@currentCreditLimit`, `@newCreditLimit`, `@validTill`, `@companyId`, `@createdBy`, output `@newDocumentId` | new document id | Creates SQL credit limit document and starts approval flow. |
| `cl.jsInsertCreditDocumentAttachment` | `SaveAttachmentAsync` | `@creditDocumentId`, `@fileName`, `@fileExtension`, `@filePath`, `@uploadedBy`, output `@attachmentId` | attachment id | Stores metadata for uploaded attachment. |
| `cl.jsGetApprovedDocuments` | `GetApprovedDocumentsAsync`, `GetAllDocumentsAsync` | `@userId`, `@companyId`, `@month` | approved document DTO rows | Approved list and all-list merge. |
| `cl.jsGetPendingDocuments` | `GetPendingDocumentsAsync`, `GetAllDocumentsAsync`, reminder counts indirectly | `@userId`, `@companyId`, `@month` | pending document DTO rows | Pending approver work queue. |
| `cl.jsGetRejectedDocuments` | `GetRejectedDocumentsAsync`, `GetAllDocumentsAsync` | `@userId`, `@companyId`, `@month` | rejected document DTO rows | Rejected list. |
| `cl.jsGetCreditDocumentInsight` | `GetCreditDocumentInsightAsync`, reminders | `@userId`, `@companyId`, `@month` | total pending/approved/rejected counts | Dashboard and reminder count source. |
| `cl.jsGetUserDocumentInsights` | `GetUserDocumentInsightsAsync` | `@createdBy`, `@monthYear` | creator summary counts | Creator dashboard. |
| `cl.jsGetDocumentDetail` | `GetDocumentDetailAsync`, `GetCreditDocumentDetailAsyncV2` | `@documentId` | document detail; V2 also expects attachments result set | Document detail and attachment details. |
| `cl.jsGetCreditLimitApprovalFlow` | `GetApprovalFlowAsync` | `@flowId` | `CreditLimitApprovalFlowDto` rows | Stage timeline/debugging. |
| `cl.jsGetUserDocumentsByCreatedByAndMonth` | `GetUserDocumentsAsync` | `@createdBy`, `@monthYear`, `@status` | creator document rows | Creator/status history. |
| `cl.jsGetId` | `GetClUserDocumentIdAsync` | `@flowId` | document id | Maps approval flow id to credit document id for notifications. |
| `cl.jsApproveDocument` | `ApproveDocumentAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | scalar message | Approves current stage or final flow according to DB logic. |
| `cl.jsRejectDocument` | `RejectDocumentAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | execute success/error | Rejects the flow. |
| `cl.jsGetDocumentDetailUsingFlowId` | `GetDocumentDetailUsingFlowIdAsync`, `UpdateCreditLimitAsync` | `@flowId` | document detail rows | Fetches customer code, branch/company, and new limit for SAP update. |
| `cl.jsGetFlowStatus` | `GetFlowStatusAsync`, `UpdateCreditLimitAsync` | `@flowId` | `status` | Final-stage gate; SAP update requires `A`. |
| `cl.updateHanaStatus` | `UpdateHanaStatusAsync`, `UpdateCreditLimitAsync` | `@flowId`, `@status`, `@hanaStatusText` | success/error response | Persists SAP/HANA update result. |
| `cl.jsCreditLimitNotify` | `GetCLUserIdsSendNotificatiosAsync` | `@userDocumentId` with value `flowId` | `userIdsToApprove` rows | Finds next/current approvers after approval. |
| `cl.GetUsersInCurrentStage` | `GetCurrentUsersSendNotificationAsync` | `@userDocumentId` | `userId`, `username` rows | Finds approvers immediately after document creation. |
| `nt.jsInsertNotification` | `NotificationService.InsertNotificationAsync` | user/title/message/page/data/budget id | notification response | Persists notification row. |

## Insert and Update Sequence

### New Document

```text
cl.jsCreateDocument
  input document fields
  output @newDocumentId
  expected DB actions:
    create credit document row
    create initial approval flow/stage rows
    set current state to pending
```

The exact tables are hidden behind the procedure. Based on DTOs and procedure names, likely data categories are:

```text
credit document header
approval flow/stage state
attachment metadata
HANA status fields/text
created-by/user activity fields
```

### Approval

```text
cl.jsApproveDocument
  input flow id, company, approver user id, remarks
  expected DB actions:
    validate approver
    update current stage action
    move to next stage or mark final status A
    store remarks/action date
```

### SAP Status

```text
cl.updateHanaStatus
  input flow id, boolean status, hanaStatusText
  expected DB actions:
    set HANA/SAP sync success/failure
    store diagnostic message
```

# Field Mapping

## Create Request Mapping

| Frontend/API field | DTO property | Service variable | SQL parameter | DB column | SAP/HANA field |
|---|---|---|---|---|---|
| Branch/company selector | `BranchId` | `request.BranchId` | `@branchId` | Hidden behind `cl.jsCreateDocument` | Used later as branch selector for SAP session. |
| Customer code | `CustomerCode` | `request.CustomerCode` | `@customerCode` | Hidden behind `cl.jsCreateDocument` | SAP `BusinessPartners('{customerCode}')`; HANA `CardCode`. |
| Customer display value/name | `CustomerValue` | `request.CustomerValue` | `@customerValue` | Hidden behind `cl.jsCreateDocument` | Not sent to SAP PATCH; name is resolved from HANA `GetCustomerCards`. |
| Current balance | `CurrentBalance` | `request.CurrentBalance` | `@currentBalance` | Hidden behind `cl.jsCreateDocument` | Source reference only; not sent in SAP PATCH. |
| Existing limit | `CurrentCreditLimit` | `request.CurrentCreditLimit` | `@currentCreditLimit` | Hidden behind `cl.jsCreateDocument` | Source reference only; not sent in SAP PATCH. |
| Requested new limit | `NewCreditLimit` | `request.NewCreditLimit` | `@newCreditLimit` | Hidden behind `cl.jsCreateDocument` | `MaxCommitment`, `CreditLimit`. |
| Valid until | `ValidTill` | `request.ValidTill` | `@validTill` | Hidden behind `cl.jsCreateDocument` | Not sent in SAP PATCH in current code. |
| Company id | `CompanyId` | `request.CompanyId` | `@companyId` | Hidden behind `cl.jsCreateDocument` | Used for list filters; branch/detail drives SAP session. |
| Created by | `CreatedBy` | `request.CreatedBy` | `@createdBy` | Hidden behind `cl.jsCreateDocument` | Notification/audit source; not sent to SAP. |

## V2 Attachment Mapping

| Frontend/API field | DTO/service field | Storage path | SQL parameter | Notes |
|---|---|---|---|---|
| `attachment` form file | `IFormFile attachment` | `wwwroot/Uploads/CreditLimit/{guid}{ext}` | metadata goes to `cl.jsInsertCreditDocumentAttachment` | Only one file is accepted by controller. |
| Original extension | `Path.GetExtension(attachment.FileName)` | file extension preserved | `@fileExtension` | No extension allow-list is implemented in C#. |
| Generated file name | `Guid.NewGuid() + extension` | server filename | `@fileName` | Original filename is not stored by active code. |
| Relative folder | `"/Uploads/CreditLimit"` | folder path | `@filePath` | Download URL later uses `FileController.AdvanceDownloadFile`. |
| Uploaded by | `request.CreatedBy?.ToString()` | metadata | `@uploadedBy` | Stored as string in DTO. |

## Approval Mapping

| API field | DTO property | SQL parameter | Meaning |
|---|---|---|---|
| Flow id | `ApproveDocumentRequest.FlowId` | `@flowId` | Approval flow/document identifier used by `cl.jsApproveDocument`. |
| Company | `ApproveDocumentRequest.Company` | `@company` | Company validation and flow scope. |
| Approver user | `ApproveDocumentRequest.UserId` | `@userId` | User attempting approval. |
| Remarks | `ApproveDocumentRequest.Remarks` | `@remarks` | Stored as approval remarks; blank becomes single space. |
| Action | `ApproveDocumentRequest.Action` | Not passed | Defaults to `Approve`, currently unused by service. |

## SAP PATCH Mapping

`CreditLimitService.UpdateCreditLimitAsync` builds this payload:

```json
{
  "MaxCommitment": 250000,
  "CreditLimit": 250000
}
```

| Source | Service variable | SAP endpoint/field |
|---|---|---|
| `cl.jsGetDocumentDetailUsingFlowId.CustomerCode` | `customerCode` | `BusinessPartners('{customerCode}')` key. |
| `cl.jsGetDocumentDetailUsingFlowId.NewCreditLimit` | `newCreditLimit` | `MaxCommitment`, `CreditLimit`. |
| `cl.jsGetDocumentDetailUsingFlowId.BranchId` | `doc.BranchId` | Selects SAP session: `1` OIL, `2` BEV, `3` MART. |

Note: code comment says `CreditLimit` was changed from `CreditLine`, but the active payload sends `CreditLimit`, not `CreditLine`.

# Approval Flow Analysis

## Workflow Start

Workflow starts inside `cl.jsCreateDocument`. The service does not separately call a template API. Immediately after creation, it calls:

```text
cl.GetUsersInCurrentStage(@userDocumentId = newDocumentId)
```

That procedure determines the first/current-stage approvers.

## Stage Detail DTO

`CreditLimitApprovalFlowDto` contains:

| Field | Meaning |
|---|---|
| `StageId` | Stage identifier. |
| `StageName` | Stage display name. |
| `Priority` | Stage order. |
| `AssignedTo` | User(s) assigned to stage. |
| `ActionStatus` | Current action status, typically pending/approved/rejected using DB codes. |
| `ActionDate` | When action was taken. |
| `Description` | Stage description/remarks. |
| `ApprovalRequired` | Approval requirement flag/count from DB. |
| `RejectRequired` | Rejection requirement flag/count from DB. |

## Approval Logic

```text
POST /ApproveDocument
  -> cl.jsApproveDocument validates flow/user/company/current stage
  -> service sends next-approver notification based on cl.jsCreditLimitNotify
  -> controller invokes UpdateCreditLimitAsync
  -> UpdateCreditLimitAsync checks cl.jsGetFlowStatus == A
  -> if not final, cl.updateHanaStatus stores failure text
```

Because `ApproveDocument` always attempts `UpdateCreditLimitAsync` after successful stage approval, intermediate approvals can produce a HANA status text of `"Flow is not approved from final stage"`. This is not necessarily a business failure; it is the current implementation's final-stage guard.

## Rejection Logic

```text
POST /RejectDocument
  -> cl.jsRejectDocument
  -> response Success true when procedure executes
```

The service does not pass `RejectDocumentRequest.Action`, even though the DTO contains `Action = "Reject"`.

## Next Approver Logic

Next approvers are not calculated in C#. They come from:

```text
cl.GetUsersInCurrentStage
cl.jsCreditLimitNotify
```

If the wrong approver receives the request, inspect:

```text
cl.jsCreateDocument flow creation logic
cl.GetUsersInCurrentStage result
cl.jsCreditLimitNotify result
template/stage/user/company mappings in DB
delegation rules if shared approval delegation is used
```

# SAP and HANA Integration

## HANA Customer Lookup

Company mapping is hard-coded in `GetLiveHanaSettings`:

| Company | HANA connection string | Schema |
|---|---|---|
| `1` | `LiveHanaConnection` | `JIVO_OIL_HANADB` |
| `2` | `LiveBevHanaConnection` | `JIVO_BEVERAGES_HANADB` |
| `3` | `LiveMartHanaConnection` | `JIVO_MART_HANADB` |

`GetCustomerCardsAsync` calls:

```sql
CALL "{schema}"."GetCustomerCards"()
```

`GetCustomerNameByCodeAsync` calls the same HANA procedure and filters in C#:

```csharp
var customer = result.FirstOrDefault(x => x.CardCode == customerCode);
```

This means document list APIs can become slow if many rows each trigger a full HANA customer-card load.

## Direct HANA CSLM Procedure

`OpenCslmAsync` calls:

```sql
CALL "{schema}"."OPENCSLM"(?,?,?,?,?,?,?)
```

Inputs:

```text
CardCode
CurrentLimit
NewLimit
ValidTill
CreatedBy
Balance
OUT result_id
```

This is separate from the SQL Server approval document flow. Use it carefully: it writes/opens a HANA-side CSLM record and does not use `cl.jsCreateDocument`.

## SAP Service Layer Update

`UpdateCreditLimitAsync` calls SAP after final approval:

```text
PATCH {SapServiceLayer:BaseUrl}/BusinessPartners('{customerCode}')
Cookie: B1SESSION=...; ROUTEID=...
Content-Type: application/json
```

Payload:

```json
{
  "MaxCommitment": 250000,
  "CreditLimit": 250000
}
```

SAP session selection:

| `doc.BranchId` | Session method |
|---|---|
| `"1"` | `Bom2Service.GetSAPSessionOilAsync()` |
| `"2"` | `Bom2Service.GetSAPSessionBevAsync()` |
| `"3"` | `Bom2Service.GetSAPSessionMartAsync()` |

Failure handling:

| Failure | Stored status text |
|---|---|
| Flow not final approved | `Flow is not approved from final stage` |
| Detail missing | `Document detail not found.` |
| Unknown branch | `Exception occurred: Unknown branch type: ...` |
| SAP non-success response | `HANA PATCH failed: {response body}` |
| Exception | `Exception occurred: {ex.Message}` |

All of these call:

```text
cl.updateHanaStatus
```

with `Status = false`.

Success stores:

```text
Credit Limit updated successfully in HANA for Customer: {customerCode}, Branch: {branch}, New Credit Limit: {newCreditLimit}
```

with `Status = true`.

## Retry Flow

There is no queue processor. Retry is manual/API-driven:

```http
POST /api/CreditLimit/UpdateCreditLimitInHana?flowId=123
```

That endpoint reruns `UpdateCreditLimitAsync`. Before retrying, verify:

```text
cl.jsGetFlowStatus returns A
cl.jsGetDocumentDetailUsingFlowId returns correct CustomerCode, BranchId, NewCreditLimit
SAP session endpoint is reachable
Customer exists in the target SAP company
cl.updateHanaStatus previous failure text
```

# Notification Flow

## On Create

```text
CreateDocumentAsync or CreateDocumentWithAttachmentAsyncV2
  -> cl.GetUsersInCurrentStage
  -> group by userId
  -> NotificationService.GetUserFcmTokenAsync
  -> SendPushNotificationAsync
  -> nt.jsInsertNotification
```

Create V1 notification:

```text
title: Credit Limit Request
body: A new credit limit document (Doc Id: {newId}) is awaiting your approval.
pageId: 5
data: Document ID: {newId}
BudgetId: {newId}
```

Create V2 notification:

```text
title: Credit Limit Request
body: Credit Limit document (ID: {creditDocumentId}) is pending for your approval.
pageId: 5
data: Document ID: {creditDocumentId}
BudgetId: {creditDocumentId}
```

## On Approval

```text
ApproveDocumentAsync
  -> cl.jsApproveDocument
  -> cl.jsGetId maps flow id to document id
  -> cl.jsCreditLimitNotify
  -> expand userIdsToApprove
  -> deduplicate user ids and FCM tokens
  -> FCM push
  -> nt.jsInsertNotification
```

Approval notification:

```text
title: Credit Limit Request
body: A new Credit Limit document (Doc Id: {docId}) is awaiting your approval.
pageId: 6
data: Flow ID: {flowId}
BudgetId: {flowId}
```

## Pending Reminder

`SendPendingCLCountNotificationAsync`:

```text
UserService.GetActiveUser()
  -> for each active user:
      CLDocumentRequest(userId, company, current MM-yyyy)
      GetCreditDocumentInsightAsync
      if TotalPending > 0:
        GetUserFcmTokenAsync
        SendPushNotificationAsync
```

Reminder notification is pushed but does not insert an `nt` notification row in the active code.

## Missing Notification Types

No C# implementation was found for:

```text
rejection notification
final approval notification to creator
SAP failure notification
SAP success notification
escalation reminder
```

# Frontend to Backend Trace

## Existing Frontend Evidence

No dedicated Credit Limit Razor view or JavaScript module is checked in.

Found related but not module-specific files:

| File | Finding |
|---|---|
| `Views/BPmasterweb/Index.cshtml` | Contains a `Credit Limit` input for BP master creation. |
| `Views/BPmasterweb/Index1.cshtml` | Contains a `Credit Limit` input for BP master creation. |
| `wwwroot/js/bp-creation.js` | Validates/logs BP credit limit field values for BP master workflows. |
| `Views/Shared/_Layout.cshtml` | No explicit Credit Limit menu entry was found in the searched snippets. |

Therefore a frontend for this module should call the APIs directly:

```text
Load customer cards
  -> GET /api/CreditLimit/GetCustomerCards?company=1

Submit document
  -> POST /api/CreditLimit/CreateCLDocument
  or POST /api/CreditLimit/CreateCLDocumentV2 multipart/form-data

Show pending/approved/rejected
  -> POST /api/CreditLimit/GetPendingDocuments
  -> POST /api/CreditLimit/GetApprovedDocuments
  -> POST /api/CreditLimit/GetRejectedDocuments

Approve/reject
  -> POST /api/CreditLimit/ApproveDocument
  -> POST /api/CreditLimit/RejectDocument

Show flow
  -> GET /api/CreditLimit/GetApprovalFlow?flowId=...
```

# Security Analysis

## Route Protection

`CreditLimitController` has `[Authorize]`, so all endpoints require authentication through `SmartAuth`.

Authentication path:

```text
Authorization: Bearer <JWT>
  -> JWT bearer auth

No Bearer header
  -> JSAP.Auth cookie auth
```

Middleware order from `Program.cs`:

```text
UseRouting
UseRateLimiter
UseCors
UseSession
UseAuthentication
UseAuthorization
MapControllers
```

## Authorization and Permission Checks

The controller does not use `[CheckUserPermission]` on Credit Limit endpoints. Fine-grained approval ownership is expected to be enforced by:

```text
cl.jsGetPendingDocuments
cl.jsApproveDocument
cl.jsRejectDocument
cl.GetUsersInCurrentStage
cl.jsCreditLimitNotify
```

Risk: approval requests accept `UserId` and `Company` from request JSON. The service does not cross-check these values against authenticated claims. The stored procedures must validate that this user is the current-stage approver for the flow and company.

## Company and Branch Validation

Company/HANA mapping only allows:

```text
1 OIL
2 BEVERAGES
3 MART
```

Invalid HANA company throws:

```text
Invalid company ID (only 1, 2, and 3 are allowed).
```

SAP update branch selection checks `doc.BranchId` as string values `"1"`, `"2"`, `"3"`. If a service method maps branch ids to names before SAP update, SAP update will fail with unknown branch. `GetDocumentDetailUsingFlowIdAsync` currently does not call `MapBranchId`, which is correct for SAP session selection.

## SQL Injection Protection

The module uses Dapper parameters and `SqlCommand` parameters for SQL Server and HANA calls. Dynamic HANA SQL includes schema names selected from a hard-coded switch, not raw user input.

## File Upload Security

`CreateCLDocumentV2`:

```text
Consumes multipart/form-data
documentData JSON
attachment IFormFile
```

Implemented:

```text
GUID server filename
stored under wwwroot/Uploads/CreditLimit
metadata stored through cl.jsInsertCreditDocumentAttachment
```

Not implemented in C#:

```text
extension allow-list
content-type validation
file size limit
malware scanning
original filename preservation
multi-file handling despite DTO list
```

Download URLs are generated through:

```text
FileController.AdvanceDownloadFile
```

This naming is reused from another module; it is not Credit-Limit-specific.

## Rate Limiting and CSRF

Approval/create endpoints are covered by global and mutation rate limiting in `Program.cs`. There is no explicit antiforgery token validation on these API endpoints. Cookie-authenticated browser clients rely on SameSite cookie settings and CORS restrictions.

# Request and Response Examples

## Get Customer Cards

```http
GET /api/CreditLimit/GetCustomerCards?company=1
```

Response:

```json
{
  "Success": true,
  "Data": [
    {
      "cardCode": "C000123",
      "cardName": "ABC Traders",
      "cardType": "C",
      "balance": "15000.00",
      "debtLine": "0",
      "creditLine": "100000.00"
    }
  ]
}
```

## Create Document

```http
POST /api/CreditLimit/CreateCLDocument
Content-Type: application/json
```

```json
{
  "branchId": "1",
  "customerCode": "C000123",
  "customerValue": "ABC Traders",
  "currentBalance": 15000,
  "currentCreditLimit": 100000,
  "newCreditLimit": 250000,
  "validTill": "2026-12-31",
  "companyId": 1,
  "createdBy": 42
}
```

Response:

```json
{
  "success": true,
  "message": "Document created successfully and notifications sent.",
  "newDocumentId": 101
}
```

## Create Document V2 With Attachment

```http
POST /api/CreditLimit/CreateCLDocumentV2
Content-Type: multipart/form-data
```

Form fields:

```text
documentData = {"branchId":"1","customerCode":"C000123","customerValue":"ABC Traders","currentBalance":15000,"currentCreditLimit":100000,"newCreditLimit":250000,"validTill":"2026-12-31","companyId":1,"createdBy":42,"totalEntries":1}
attachment = approval.pdf
```

Response:

```json
{
  "success": true,
  "message": "Document created successfully",
  "creditDocumentId": 101
}
```

## Approve Document

```http
POST /api/CreditLimit/ApproveDocument
Content-Type: application/json
```

```json
{
  "flowId": 9001,
  "company": 1,
  "userId": 25,
  "remarks": "Approved",
  "action": "Approve"
}
```

Response when flow is final approved and SAP succeeds:

```json
{
  "Success": true,
  "FlowId": 9001,
  "Message": "Flow approved successfully.",
  "HanaStatusText": "Credit Limit updated successfully in HANA for Customer: C000123, Branch: 1, New Credit Limit: 250000"
}
```

Response when approval stage succeeds but flow is not final:

```json
{
  "Success": true,
  "FlowId": 9001,
  "Message": "Flow approved successfully.",
  "HanaStatusText": "Flow is not approved from final stage"
}
```

## Reject Document

```http
POST /api/CreditLimit/RejectDocument
Content-Type: application/json
```

```json
{
  "flowId": 9001,
  "company": 1,
  "userId": 25,
  "remarks": "Limit increase not justified",
  "action": "Reject"
}
```

Response:

```json
{
  "success": true,
  "message": "Document rejected successfully."
}
```

## Manual SAP Retry

```http
POST /api/CreditLimit/UpdateCreditLimitInHana?flowId=9001
```

Success:

```json
{
  "Success": true,
  "Message": "Credit Limit updated successfully in HANA for Customer: C000123, Branch: 1, New Credit Limit: 250000"
}
```

Failure:

```json
{
  "Success": false,
  "Message": "HANA PATCH failed: {SAP error body}"
}
```

# Production Debugging Guide

## Request Not Saving

Check:

```text
API: POST /api/CreditLimit/CreateCLDocument or CreateCLDocumentV2
Controller: CreditLimitController.CreateDocument/CreateDocumentV2
Service: CreditLimitService.CreateDocumentAsync/CreateDocumentAsyncV2
SP: cl.jsCreateDocument
Output: @newDocumentId
```

Common causes:

| Symptom | Check |
|---|---|
| 400 invalid request | Request body missing or multipart `documentData` not valid JSON. |
| V2 says attachment required | `TotalEntries == 1` and no `attachment` form field. |
| `Document creation failed` | `@newDocumentId` returned null/0 from `cl.jsCreateDocument`. |
| No notification after create | `cl.GetUsersInCurrentStage` returned no users or users have no FCM tokens. |
| Attachment insert failed | `cl.jsInsertCreditDocumentAttachment` returned output attachment id <= 0. |

## Pending List Empty

Check:

```text
API: POST /api/CreditLimit/GetPendingDocuments
SP: cl.jsGetPendingDocuments
Inputs: userId, companyId, month
```

Verify:

```text
month format expected by SP
userId is current-stage approver
companyId matches document company
flow is not already approved/rejected
delegation/active stage mappings
```

## Approval Stuck

Check:

```text
GET /api/CreditLimit/GetApprovalFlow?flowId=...
GET /api/CreditLimit/GetCurrentUsersSendNotification?userDocumentId=...
GET /api/CreditLimit/GetCLUserIdsSendNotifications?flowId=...
POST /api/CreditLimit/GetFlowStatus? no, actual is GET /GetFlowStatus
```

Database procedures:

```sql
EXEC [cl].[jsGetCreditLimitApprovalFlow] @flowId;
EXEC [cl].[GetUsersInCurrentStage] @userDocumentId;
EXEC [cl].[jsCreditLimitNotify] @userDocumentId;
EXEC [cl].[jsGetFlowStatus] @flowId;
```

Likely causes:

```text
approver user not mapped to current stage
company mismatch
stage priority broken
flow status already terminal
approval SP rejected duplicate/current-stage action
frontend sends document id where flow id is required, or vice versa
```

## SAP Update Failed

Check in order:

1. `GET /api/CreditLimit/GetFlowStatus?flowId=...`
   - Must return `status = "A"`.
2. `GET /api/CreditLimit/GetDocumentDetailUsingFlowId?flowId=...`
   - Confirm `CustomerCode`, `BranchId`, `NewCreditLimit`.
3. SAP session:
   - Branch `"1"` -> Oil session
   - Branch `"2"` -> Bev session
   - Branch `"3"` -> Mart session
4. SAP endpoint:
   - `PATCH BusinessPartners('{customerCode}')`
5. SQL status:
   - `cl.updateHanaStatus` should contain the failure/success text.

Common failure messages:

| Message | Meaning |
|---|---|
| `Flow is not approved from final stage` | Approval stage succeeded but DB flow status is not final `A`. |
| `Document detail not found.` | `cl.jsGetDocumentDetailUsingFlowId` returned no rows. |
| `Unknown branch type` | `BranchId` not `"1"`, `"2"`, or `"3"`. |
| `HANA PATCH failed: ...` | SAP Service Layer returned non-success; inspect embedded SAP error. |
| `HANA updated but SQL status update failed` | SAP succeeded, `cl.updateHanaStatus` failed. |

## Wrong Customer Updated

Trace:

```text
Create payload CustomerCode
  -> cl.jsCreateDocument @customerCode
  -> cl.jsGetDocumentDetailUsingFlowId CustomerCode
  -> UpdateCreditLimitAsync customerCode
  -> PATCH BusinessPartners('{customerCode}')
```

If wrong customer is updated:

```text
compare original create payload with SQL detail result
verify frontend selected CardCode, not CardName/CustomerValue
check cl.jsCreateDocument field assignment
check cl.jsGetDocumentDetailUsingFlowId joins/aliases
check branch/company so customer code is patched in correct SAP company
```

## Notifications Not Sent

Check:

```text
cl.GetUsersInCurrentStage after create
cl.jsCreditLimitNotify after approval
NotificationService.GetUserFcmTokenAsync
Firebase service account path
nt.jsInsertNotification
```

Important distinction:

```text
Create/approval notifications insert nt rows.
Pending reminder notifications only push FCM in active code; they do not insert nt rows.
Reject/SAP failure notifications are not implemented in C#.
```

## Attachment Download Broken

Check:

```text
File metadata from cl.jsGetDocumentDetail second result set
FilePath stored as /Uploads/CreditLimit
FileName stored as generated GUID with extension
FileExtension stored with leading dot
DownloadUrl generated with FileController.AdvanceDownloadFile
physical file under wwwroot/Uploads/CreditLimit
```

Potential issue: `fileNameWithoutExt = Path.GetFileNameWithoutExtension(file.FileName)` is used with `fileExt`; if `FileName` or `FileExtension` aliases from the SP differ from saved values, generated URL can point to the wrong file.

# File Connection Map

```text
CreditLimitController.cs
  -> ICreditLimitService
  -> ILogger<CreditLimitController>

ICreditLimitService.cs
  -> CreditLimitService.cs

CreditLimitService.cs
  -> IConfiguration
  -> SqlConnection / Dapper
  -> HanaConnection / Dapper
  -> IBom2Service for SAP sessions
  -> INotificationService for FCM and nt notifications
  -> IUserService.GetActiveUser for reminders
  -> IUrlHelper for attachment download URLs

NotificationService.cs
  -> nt.jsInsertNotification
  -> Firebase FCM

Bom2Service.cs
  -> SAP Service Layer Login
  -> B1SESSION / ROUTEID

FileController.cs
  -> AdvanceDownloadFile used by CreditLimit attachment DownloadUrl
```

# Performance and Failure Analysis

## Known Hot Spots

| Area | Risk |
|---|---|
| Customer name enrichment | Each document row can call HANA `GetCustomerCards` and filter in C#. For large lists this is expensive. |
| Pending reminders | Iterates every active user and calls insight/token methods synchronously. |
| Approval endpoint | Sends notifications synchronously and then attempts SAP update synchronously. |
| SAP PATCH | Network latency/timeouts block API response. |
| Attachment storage | Writes to local webroot; in multi-server deployments files will not automatically replicate. |

## Race and Transaction Risks

| Risk | Explanation |
|---|---|
| Double approval | C# has no semaphore/lock; `cl.jsApproveDocument` must prevent duplicate stage approvals. |
| SAP update attempted on intermediate stage | Current controller always calls `UpdateCreditLimitAsync`; service records failure if status is not `A`. |
| SAP success but SQL status failure | Method returns `HANA updated but SQL status update failed`; retry could patch SAP again. |
| Notification failure after DB success | Approval/create is not rolled back if FCM or notification insert fails. |
| Branch/company mismatch | HANA lookup uses company; SAP update uses `BranchId` from document detail. |
| File upload partial failure | V2 creates document first, then saves attachment; attachment failure throws after document creation. |

# Audit and History Tracking

No explicit audit service or audit table names are visible in C# for this module. Audit/history is expected to be stored inside `cl` procedures:

```text
cl.jsCreateDocument
cl.jsApproveDocument
cl.jsRejectDocument
cl.updateHanaStatus
cl.jsInsertCreditDocumentAttachment
```

Fields that support audit/history in DTOs:

| Field | Purpose |
|---|---|
| `CreatedBy`, `CreatedById`, `CreatedByUser`, `CreatedByUserId` | Creator tracking. |
| `CreatedOn` | Creation timestamp returned in list DTOs. |
| `ActionStatus` | Stage status in approval flow. |
| `ActionDate` | Approval/rejection timestamp. |
| `Description` | Stage description or remarks. |
| `hanaStatusText` | SAP/HANA sync status and error details. |
| `UploadedBy`, `UploadedOn` | Attachment audit metadata. |

For a production audit investigation, pull:

```text
document detail by document id
document detail by flow id
approval flow rows
creator document list
HANA status text
notification table rows for pageId 5/6 and BudgetId = document/flow id
physical attachment metadata
```

# Developer Improvement Notes

These are not required to understand current behavior, but they are high-value hardening items:

1. Derive `UserId` from authenticated claims or validate it against claims before passing approval requests to SQL.
2. Only trigger `UpdateCreditLimitAsync` when the approval SP indicates final approval, instead of always attempting and recording `"Flow is not approved from final stage"`.
3. Cache HANA customer cards per company/request or join customer names in SQL/HANA procedure to avoid per-row HANA calls.
4. Add file extension, size, and content validation to `CreateCLDocumentV2`.
5. Add rejection and SAP failure notifications if business users need proactive updates.
6. Add a Credit-Limit-specific download endpoint or rename the reused `AdvanceDownloadFile` dependency for clarity.
7. Make SAP update idempotent by storing a success marker before allowing retry to PATCH again.

