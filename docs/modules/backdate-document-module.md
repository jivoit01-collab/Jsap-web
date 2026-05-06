# BackDate Document Module

BackDate Document, called `BKDT` in the codebase, is the financial document-date access workflow hosted inside the Item Master API surface. It does not have a dedicated controller or service class. The module creates a request in SQL Server, moves it through a multi-stage approval workflow, and only after final approval writes the approved access window into SAP HANA by executing the company schema procedure `OPEN_BKDT`.

This document describes the implementation that exists in the source tree. The SQL Server table bodies are not defined in C# migrations or scripts in this repository, so table-level storage is inferred only where the stored procedure names and DTO fields expose it. The authoritative database behavior is inside the `[backdate]` stored procedures.

## Module Architecture

```text
Razor / JavaScript caller
  -> /api/ItemMaster/* BKDT endpoints
  -> Controllers/ItemMasterController.cs
  -> Services/Interfaces/IItemMasterService.cs
  -> Services/Implementation/ItemMasterService.cs
  -> SQL Server [backdate] stored procedures
  -> Approval stage records and HANA status tracking
  -> NotificationService / FCM
  -> SAP HANA company schema procedure OPEN_BKDT
```

The important implementation files are:

| File | Purpose |
|---|---|
| `Controllers/ItemMasterController.cs` | Hosts all BKDT APIs under `/api/ItemMaster`. Handles request null checks, list transformations, approval orchestration, HANA trigger, and response shaping. |
| `Services/Interfaces/IItemMasterService.cs` | Declares all BKDT service contracts between controller and service. |
| `Services/Implementation/ItemMasterService.cs` | Implements SQL Server stored procedure calls, HANA calls, approval actions, HANA status update, and notification dispatch. |
| `Models/ItemMasterModel.cs` | Contains BKDT DTOs such as `BKDTModel`, `CreateDocumentRequest`, `ApproveRequestModel`, `RejectRequestModel`, `BKDTApprovalFlow`, and `UpdateHanaStatusRequest`. |
| `Services/Implementation/NotificationService.cs` | Used indirectly by `ItemMasterService` to fetch FCM tokens, send push notifications, and insert notification records. |
| `Models/NotificationModels.cs` | Contains `InsertNotificationModel` used for persisted approval notifications. |
| `Models/AuthModels.cs` | Contains `UserIdsForNotificationModel`, returned by `[backdate].[jsBackdateNotify]`. |
| `Models/CreditLimitModels.cs` | Contains shared `AfterCreatedRequestSendNotificationToUser`, reused by BackDate current-stage notification lookups. |
| `Program.cs` | Configures SmartAuth, global authorization filters, session, rate limiting, CORS, security headers, and DI registration for `IItemMasterService`. |
| `appsettings.json` | Contains `DefaultConnection`, live HANA connection strings, `HanaSettings`, SAP Service Layer base URL, and notification configuration. |

Searches across `Views` and `wwwroot/js` did not find a dedicated BackDate Razor page or JavaScript module with the BKDT endpoint names. The current source exposes the backend API and likely expects callers from an external/mobile UI, dynamic menu route, or a view not present under an obvious BackDate filename.

## API Surface

All routes are on `ItemMasterController`, whose controller route is `[Route("api/[controller]")]` and whose controller class has `[Authorize]`.

| Route | Method | Controller action | Service method | Purpose |
|---|---|---|---|---|
| `/api/ItemMaster/GetUserDetails?company={id}` | GET | `GetUserDetails` | `GetUserDetailsAsync` | Reads SAP/HANA user catalog using `GETUSERDETAILS`. |
| `/api/ItemMaster/GetMobjDetails?company={id}` | GET | `GetMobjDetails` | `GetMobjDetailsAsync` | Reads SAP/HANA document object metadata using `GETMOBJDETAILS`. |
| `/api/ItemMaster/SaveBKDT` | POST | `SaveBKDT` | `SaveBKDTAsync` | Directly executes HANA `OPEN_BKDT`; bypasses approval if called directly. |
| `/api/ItemMaster/GetBKDTinsights` | GET | `GetBKDTinsights` | `GetBKDTinsightsAsync` | Returns pending/approved/rejected counts. |
| `/api/ItemMaster/GetBKDTPendingDoc` | GET | `GetBKDTPendingDoc` | `GetBKDTPendingDocAsync` | Returns pending requests for user/company/month. |
| `/api/ItemMaster/GetBKDTApprovedDoc` | GET | `GetBKDTApprovedDoc` | `GetBKDTApprovedDocAsync` | Returns approved requests. |
| `/api/ItemMaster/GetBKDTRejectedDoc` | GET | `GetBKDTRejectedDoc` | `GetBKDTRejectedDocAsync` | Returns rejected requests. |
| `/api/ItemMaster/GetBKDTFullDetails` | GET | `GetBKDTFullDetails` | `GetBKDTFullDetailsAsync` | Merges pending, approved, and rejected lists. |
| `/api/ItemMaster/GetBKDTDocumentDetail?documentId={id}` | GET | `GetBKDTDocumentDetail` | `GetBKDTDocumentDetailAsync` | Returns one request by document id. |
| `/api/ItemMaster/GetBKDTDocumentDetailUsingFlowId?flowId={id}` | GET | `GetBKDTDocumentDetailUsingFlowId` | `GetBKDTDocumentDetailBasedOnFlowIdAsync` | Returns request details from approval flow id. |
| `/api/ItemMaster/CreateDocument` | POST | `CreateDocument` | `CreateDocumentAsync` | Creates the SQL Server request and starts current-stage notification. |
| `/api/ItemMaster/ApproveBKDT` | POST | `ApproveBKDT` | `ApproveDocumentAsync` | Approves current workflow stage; triggers HANA if final status is `A`. |
| `/api/ItemMaster/RejectBKDT` | POST | `RejectBKDT` | `RejectDocumentAsync` | Rejects the workflow request. |
| `/api/ItemMaster/GetBackDateApprovalFlow?flowId={id}` | GET | `GetBackDateApprovalFlow` | `GetBackDateApprovalFlowAsync` | Returns stage-level approval history. |
| `/api/ItemMaster/GetUserDocumentInsights` | GET | `GetUserDocumentInsights` | `GetUserDocumentInsightsAsync` | Returns creator-level counts. |
| `/api/ItemMaster/GetUserDocumentsByCreatedByAndMonth` | GET | `GetUserDocumentsByCreatedByAndMonth` | `GetUserDocumentsByCreatedByAndMonthAsync` | Returns creator documents by status and month. |
| `/api/ItemMaster/GetFlowStatus?flowId={id}` | GET | `GetFlowStatus` | `GetFlowStatusAsync` | Reads final/current flow status from SQL Server. |
| `/api/ItemMaster/UpdateHanaStatus` | POST | `UpdateHanaStatus` | `UpdateHanaStatusAsync` | Updates HANA sync status text in SQL Server. |
| `/api/ItemMaster/BackDateSaveInHana?flowId={id}` | POST | `BackDateSaveInHana` | `SaveBKDTAsync`, `UpdateHanaStatusAsync` | Manual/final HANA execution path. |
| `/api/ItemMaster/GetBkdtUserIdsSendNotificatios?flowId={id}` | GET | `GetBkdtUserIdsSendNotificatios` | `GetBkdtUserIdsSendNotificatiosAsync` | Returns next-stage users from `[backdate].[jsBackdateNotify]`. |
| `/api/ItemMaster/SendPendingBkdtCountNotification` | GET | `SendPendingBkdtCountNotification` | `SendPendingBkdtCountNotificationAsync` | Sends pending-count reminders to active users. |
| `/api/ItemMaster/GetBKDTCurrentUsersSendNotification?userDocumentId={id}` | GET | `GetBKDTCurrentUsersSendNotification` | `GetBKDTCurrentUsersSendNotificationAsync` | Returns users in the current stage. |

## Core DTOs

| DTO | File | Main fields |
|---|---|---|
| `CreateDocumentRequest` | `Models/ItemMasterModel.cs` | `Branch`, `Username`, `DocumentType`, `FromDate`, `ToDate`, `TimeLimit`, `Action`, `CompanyId`, `CreatedBy`. |
| `CreateDocumentResponse` | `Models/ItemMasterModel.cs` | `NewDocumentId`, `Success`, `Message`. |
| `BKDTModel` | `Models/ItemMasterModel.cs` | `Branch`, `UserId`, `TransType`, `FromDate`, `ToDate`, `TimeLimit`, `Rights`, `CreatedBy`, `CreatedOn`, `DeletedBy`, `DeletedOn`. |
| `ApproveRequestModel` | `Models/ItemMasterModel.cs` | Required `flowId`, `Company`, `UserId`; optional `remarks`. |
| `RejectRequestModel` | `Models/ItemMasterModel.cs` | Required `flowId`, `Company`, `UserId`, `remarks`. |
| `BKDTGetDocumentsModels` | `Models/ItemMasterModel.cs` | List view fields including `id`, `companyId`, `documentType`, `branch`, `action`, `username`, dates, `flowId`, `Status`, `HanaStatus`. |
| `BKDTDocumentDetailModels` | `Models/ItemMasterModel.cs` | Detail view fields including `id`, `companyId`, `branch`, `username`, `documentType`, dates, creator fields, and `flowId`. |
| `BKDTApprovalFlow` | `Models/ItemMasterModel.cs` | `stageId`, `stageName`, `priority`, `assignedTo`, `actionStatus`, `actionDate`, `description`, `approvalRequired`, `rejectRequired`. |
| `FlowStatus` | `Models/ItemMasterModel.cs` | `Status`. |
| `UpdateHanaStatusRequest` | `Models/ItemMasterModel.cs` | `FlowId`, `Status`, `hanastatusText`. |

## Complete Business Flow

### 1. Metadata Loading

The caller loads supporting values from SAP HANA:

```text
GET /api/ItemMaster/GetUserDetails?company=1
  -> ItemMasterController.GetUserDetails
  -> ItemMasterService.GetUserDetailsAsync
  -> HANA CALL "JIVO_OIL_HANADB"."GETUSERDETAILS"()

GET /api/ItemMaster/GetMobjDetails?company=1
  -> ItemMasterController.GetMobjDetails
  -> ItemMasterService.GetMobjDetailsAsync
  -> HANA CALL "JIVO_OIL_HANADB"."GETMOBJDETAILS"()
```

`company` selects the HANA connection and schema:

| Company id | HANA connection string | Schema |
|---|---|---|
| `1` | `LiveHanaConnection` | `JIVO_OIL_HANADB` |
| `2` | `LiveBevHanaConnection` | `JIVO_BEVERAGES_HANADB` |
| `3` | `LiveMartHanaConnection` | `JIVO_MART_HANADB` |

`GetMobjDetailsAsync` returns `Code`, `ObjType`, and `ObjName`. The controller later maps stored `documentType` values from object type ids to object names for UI responses.

### 2. Request Creation

```text
POST /api/ItemMaster/CreateDocument
  -> CreateDocumentRequest
  -> ItemMasterController.CreateDocument
  -> ItemMasterService.CreateDocumentAsync
  -> SQL Server [backdate].[jsCreateDocument]
  -> @newDocumentId output
  -> [backdate].[GetUsersInCurrentStage]
  -> NotificationService sends FCM and inserts notification rows
```

`CreateDocumentAsync` sends the request into `[backdate].[jsCreateDocument]` with these parameters:

```text
@branch
@username
@documentType
@fromDate
@toDate
@timeLimit
@action
@companyId
@createdBy
@newDocumentId OUTPUT
```

After the procedure returns a valid `@newDocumentId`, the service calls `GetBKDTCurrentUsersSendNotificationAsync(newId.Value)`, which executes `[backdate].[GetUsersInCurrentStage]`. Each returned `userId` is notified by FCM through `NotificationService.SendPushNotificationAsync`, and a database notification is inserted through `NotificationService.InsertNotificationAsync`.

### 3. Pending/Approved/Rejected Listing

The module uses separate SQL Server procedures for each list:

```text
Pending  -> [backdate].[jsGetPendingDocuments]
Approved -> [backdate].[jsGetApprovedDocuments]
Rejected -> [backdate].[jsGetRejectedDocuments]
```

`GetBKDTFullDetailsAsync` calls all three procedures and assigns display statuses in C#:

```text
pending rows  -> Status = "Pending"
approved rows -> Status = "Approved"
rejected rows -> Status = "Rejected"
```

The controller then transforms coded values:

| Stored value | Response value |
|---|---|
| `branch` `1` | `OIL` |
| `branch` `2` | `BEVERAGES` |
| `branch` `3` | `MART` |
| `action` `A` | `ADD` |
| `action` `U` | `UPDATE` |
| `documentType` object id | Mapped to `ObjName` from `GETMOBJDETAILS`. |

### 4. Approval

```text
POST /api/ItemMaster/ApproveBKDT
  -> ApproveRequestModel
  -> ItemMasterController.ApproveBKDT
  -> ItemMasterService.ApproveDocumentAsync
  -> SQL Server [backdate].[jsApproveDocument]
  -> SQL Server [backdate].[jsBackdateNotify]
  -> NotificationService sends next-stage notification
  -> SQL Server [backdate].[jsGetFlowStatus]
  -> If Status == "A", controller calls BackDateSaveInHana(flowId)
```

The final approval trigger is explicit in the controller: HANA execution runs only when `GetFlowStatusAsync(request.flowId)` returns a first row whose `Status` is exactly `A`.

### 5. HANA Posting/Access Update

```text
BackDateSaveInHana(flowId)
  -> [backdate].[jsGetDocumentDetailUsingFlowId]
  -> parse fromDate, toDate, documentType, timeLimit, createdOn
  -> build BKDTModel
  -> SaveBKDTAsync(BKDTModel)
  -> HANA CALL "{schema}"."OPEN_BKDT"(?,?,?,?,?,?,?,?,?,?,?)
  -> [backdate].[updateHanaStatus]
```

This module does not generate a SAP Service Layer HTTP payload for BackDate. The actual HANA update is a SAP HANA stored procedure call through `Sap.Data.Hana.HanaConnection`.

`SaveBKDTAsync` accepts one `BKDTModel`, splits `Branch` by comma, semicolon, or pipe, and executes `OPEN_BKDT` once per distinct branch code.

### 6. Rejection

```text
POST /api/ItemMaster/RejectBKDT
  -> RejectRequestModel
  -> ItemMasterController.RejectBKDT
  -> ItemMasterService.RejectDocumentAsync
  -> SQL Server [backdate].[jsRejectDocument]
```

The C# code does not trigger HANA for rejected requests.

## Database Flow

BackDate persistence is handled by SQL Server stored procedures in the `backdate` schema. The repository does not include the procedure bodies, so developers debugging data issues should inspect these procedures directly in SQL Server.

| Stored procedure | Called from | Purpose | Inputs/outputs visible in C# |
|---|---|---|---|
| `[backdate].[jsCreateDocument]` | `ItemMasterService.CreateDocumentAsync` | Creates the BackDate request and initializes workflow state. | Inputs: `@branch`, `@username`, `@documentType`, `@fromDate`, `@toDate`, `@timeLimit`, `@action`, `@companyId`, `@createdBy`. Output: `@newDocumentId`. |
| `[backdate].[GetUsersInCurrentStage]` | `GetBKDTCurrentUsersSendNotificationAsync` | Returns approvers for the current stage after creation. | Input: `@userDocumentId`. Output maps to `AfterCreatedRequestSendNotificationToUser` with `userId`, `username`. |
| `[backdate].[jsGetDocumentInsight]` | `GetBKDTinsightsAsync` | Returns pending/approved/rejected counts. | Inputs: `@userId`, `@companyId`, `@month`. Output maps to `GetBKDTinsights`. |
| `[backdate].[jsGetPendingDocuments]` | `GetBKDTPendingDocAsync`, `GetBKDTFullDetailsAsync` | Returns pending documents visible to the user. | Inputs: `@userId`, `@company`, `@month`. |
| `[backdate].[jsGetApprovedDocuments]` | `GetBKDTApprovedDocAsync`, `GetBKDTFullDetailsAsync` | Returns approved documents. | Inputs: `@userId`, `@company`, `@month`. |
| `[backdate].[jsGetRejectedDocuments]` | `GetBKDTRejectedDocAsync`, `GetBKDTFullDetailsAsync` | Returns rejected documents. | Inputs: `@userId`, `@company`, `@month`. |
| `[backdate].[jsGetDocumentDetail]` | `GetBKDTDocumentDetailAsync` | Returns detail rows by document id. | Input: `@documentId`. |
| `[backdate].[jsGetDocumentDetailUsingFlowId]` | `GetBKDTDocumentDetailBasedOnFlowIdAsync`, `BackDateSaveInHana` | Returns detail rows by workflow flow id. | Input: `@flowId`. |
| `[backdate].[jsApproveDocument]` | `ApproveDocumentAsync` | Approves current stage or advances the workflow. | Inputs: `@flowId`, `@company`, `@userId`, `@remarks`. Returns scalar message. |
| `[backdate].[jsBackdateNotify]` | `GetBkdtUserIdsSendNotificatiosAsync`, `ApproveDocumentAsync` | Returns next approver user ids after approval. | Input name in C#: `@userDocumentId`, but caller passes `flowId`. Output: `userIdsToApprove` CSV. |
| `[backdate].[jsGetId]` | `GetBackDateUserDocumentIdAsync` | Maps `flowId` to user document id for notification text. | Input: `@flowId`. Output: scalar document id. |
| `[backdate].[jsRejectDocument]` | `RejectDocumentAsync` | Rejects request/stage. | Inputs: `@flowId`, `@company`, `@userId`, `@remarks`. |
| `[backdate].[jsGetBackDateApprovalFlow]` | `GetBackDateApprovalFlowAsync` | Returns approval-stage history. | Input: `@flowId`. Output maps to `BKDTApprovalFlow`. |
| `[backdate].[jsGetUserDocumentInsights]` | `GetUserDocumentInsightsAsync`, pending reminder flow | Returns creator-level counts. | Inputs: `@createdBy`, `@month`. |
| `[backdate].[jsGetUserDocumentsByCreatedByAndMonth]` | `GetUserDocumentsByCreatedByAndMonthAsync` | Returns creator documents by status/month. | Inputs: `@createdBy`, `@monthYear`, `@status`. |
| `[backdate].[jsGetFlowStatus]` | `GetFlowStatusAsync`, `ApproveBKDT` | Reads workflow status. | Input: `@flowId`. Output: `Status`. |
| `[backdate].[updateHanaStatus]` | `UpdateHanaStatusAsync`, `BackDateSaveInHana` | Stores HANA execution status and text after HANA call. | Inputs: `@flowId`, `@status`, `@hanastatusText`. |

Likely SQL Server storage areas, inferred from procedure names and DTOs:

```text
Request header/detail:
  branch, username, documentType, fromDate, toDate, timeLimit, action,
  companyId, createdBy, createdOn, flowId, HanaStatus

Workflow:
  flowId, stageId, stageName, priority, assignedTo, actionStatus,
  actionDate, approvalRequired, rejectRequired, remarks

Notification:
  user id, title, message, page id, data, BudgetId/document id

HANA status:
  flowId, status bit, hana status text
```

## Field Mapping

### Create Request to SQL Server

| Frontend/API field | DTO property | Service parameter | SQL parameter | Stored value / downstream use |
|---|---|---|---|---|
| Branch | `CreateDocumentRequest.Branch` | `request.Branch` | `@branch` | Branch ids such as `1`, `2`, `3`, or combined values. Later transformed to `OIL`, `BEVERAGES`, `MART` for display and split for HANA execution. |
| Username/User | `CreateDocumentRequest.Username` | `request.Username` | `@username` | SAP/HANA user code or login that receives BackDate rights. Later becomes `BKDTModel.UserId`. |
| Document Type | `CreateDocumentRequest.DocumentType` | `request.DocumentType` | `@documentType` | SAP object type id. Later parsed to `int TransType` for `OPEN_BKDT`. |
| From Date | `CreateDocumentRequest.FromDate` | `request.FromDate` | `@fromDate` | Start posting/access date. Later converted to string `dd-MM-yyyy` before HANA execution. |
| To Date | `CreateDocumentRequest.ToDate` | `request.ToDate` | `@toDate` | End posting/access date. Later converted to string `dd-MM-yyyy` before HANA execution. |
| Time Limit | `CreateDocumentRequest.TimeLimit` | `request.TimeLimit` | `@timeLimit` | Optional timestamp limit passed as HANA `timeLimit`. |
| Action | `CreateDocumentRequest.Action` | `request.Action` | `@action` | Request action code. Display mapping: `A` -> `ADD`, `U` -> `UPDATE`. The approved HANA model currently sets `Rights = "NO"` and does not pass `Action` separately. |
| Company | `CreateDocumentRequest.CompanyId` | `request.CompanyId` | `@companyId` | SQL workflow company filter; notification data; HANA company can still be driven by `Branch` during final execution. |
| Created By | `CreateDocumentRequest.CreatedBy` | `request.CreatedBy` | `@createdBy` | Creator user id stored for audit/list filtering and notification context. |
| New Document Id | `CreateDocumentResponse.NewDocumentId` | output parameter | `@newDocumentId OUTPUT` | Used to load current-stage users and notify approvers. |

### Approved Detail to HANA Procedure

`BackDateSaveInHana` reads details using `[backdate].[jsGetDocumentDetailUsingFlowId]`, parses them, and builds a `BKDTModel`.

| SQL detail field | Detail DTO | HANA DTO/property | HANA parameter | Transformation |
|---|---|---|---|---|
| Branch | `BKDTDocumentDetailModels.branch` | `BKDTModel.Branch` | `branch` | Must remain numeric branch code(s) for `SaveBKDTAsync`. `SaveBKDTAsync` converts `1/2/3` to `OIL/BEVERAGES/MART`. |
| Username | `BKDTDocumentDetailModels.username` | `BKDTModel.UserId` | `userId` | Passed to `OPEN_BKDT` as string. |
| Document Type | `BKDTDocumentDetailModels.documentType` | `BKDTModel.TransType` | `transType` | Controller parses string to `int`. Invalid values return 400. |
| From Date | `BKDTDocumentDetailModels.fromDate` | `BKDTModel.FromDate` | `fromDate` | `TryParseDate` accepts multiple formats, then formats as `dd-MM-yyyy`. Service parses exact `dd-MM-yyyy` and sends `DateTime.Date`. |
| To Date | `BKDTDocumentDetailModels.toDate` | `BKDTModel.ToDate` | `toDate` | Same parsing/formatting as from date. |
| Time Limit | `BKDTDocumentDetailModels.timeLimit` | `BKDTModel.TimeLimit` | `timeLimit` | Optional. If empty, passed as `DBNull.Value` through `DateTime.MinValue` convention. |
| Rights | Hard-coded | `BKDTModel.Rights` | `rights` | Controller sets `"NO"`. |
| Created By | `BKDTDocumentDetailModels.createdBy` | `BKDTModel.CreatedBy` | `createdBy` | Passed as string. |
| Created On | `BKDTDocumentDetailModels.createdOn` | `BKDTModel.CreatedOn` | `createdOn` | Optional parse; `DateTime.MinValue` becomes `DBNull.Value`. |
| Deleted By | Hard-coded `null` | `BKDTModel.DeletedBy` | `deletedBy` | Passed as `DBNull.Value`. |
| Deleted On | Hard-coded `DateTime.MinValue` | `BKDTModel.DeletedOn` | `deletedOn` | Passed as `DBNull.Value`. |

Important transformation caveat: list/detail response actions map `A` to `ADD` and `U` to `UPDATE`, but the HANA execution method does not pass action to `OPEN_BKDT`. It passes `Rights = "NO"` instead. If action-specific HANA behavior is expected, inspect the HANA procedure definition and the SQL detail returned by `[backdate].[jsGetDocumentDetailUsingFlowId]`.

## Approval Flow

### Lifecycle

```text
CreateDocument
  -> [backdate].[jsCreateDocument]
  -> pending current stage
  -> notify current approvers
  -> ApproveBKDT
  -> [backdate].[jsApproveDocument]
  -> notify next approvers with [backdate].[jsBackdateNotify]
  -> [backdate].[jsGetFlowStatus]
  -> if Status == "A": BackDateSaveInHana
  -> HANA OPEN_BKDT
  -> [backdate].[updateHanaStatus]
```

The stage engine is procedure-driven. C# does not compute the next stage, stage priority, or final approval status. It delegates that to:

```text
[backdate].[jsCreateDocument]
[backdate].[jsApproveDocument]
[backdate].[jsRejectDocument]
[backdate].[jsGetFlowStatus]
[backdate].[jsGetBackDateApprovalFlow]
[backdate].[GetUsersInCurrentStage]
[backdate].[jsBackdateNotify]
```

### Statuses

The exact database status codes are defined inside SQL procedures. The C# layer exposes or assigns these values:

| Status | Where seen | Meaning in C# |
|---|---|---|
| `Pending` | `GetBKDTFullDetailsAsync` | Display status assigned to rows returned by `[backdate].[jsGetPendingDocuments]`. |
| `Approved` | `GetBKDTFullDetailsAsync` | Display status assigned to rows returned by `[backdate].[jsGetApprovedDocuments]`. |
| `Rejected` | `GetBKDTFullDetailsAsync` | Display status assigned to rows returned by `[backdate].[jsGetRejectedDocuments]`. |
| `A` | `ApproveBKDT` via `GetFlowStatusAsync` | Final approved status that triggers `BackDateSaveInHana`. |
| HANA status `true` | `UpdateHanaStatusRequest.Status` | HANA execution completed and status text stored. |
| `HanaStatus` | `BKDTGetDocumentsModels.HanaStatus` | Returned by SQL list procedures for display/debugging. |

Draft, return/resubmit, expiry, and cancellation are not implemented explicitly in the C# BackDate code. If those states exist in production, they are implemented inside the `[backdate]` stored procedures and consuming UI.

### Final Approval Boundary

`ApproveBKDT` is both an approval endpoint and the automatic sync trigger:

```text
ApproveBKDT
  -> approve SQL stage
  -> query flow status
  -> flowStatus.Status == "A"
      -> BackDateSaveInHana
```

If approval succeeds but `Status` is not `A`, HANA is not called and `BackDateResult` remains `null`.

## SAP/HANA Integration

BackDate uses direct SAP HANA procedures, not the SAP Service Layer HTTP API.

### Metadata Procedures

```text
CALL "{schema}"."GETUSERDETAILS"()
CALL "{schema}"."GETMOBJDETAILS"()
```

These are called by `GetUserDetailsAsync` and `GetMobjDetailsAsync` using `GetLiveHanaSettings(company)`.

### Posting/Access Procedure

`SaveBKDTAsync` executes:

```text
CALL "{schema}"."OPEN_BKDT"(?,?,?,?,?,?,?,?,?,?,?)
```

Parameters are added in this order:

| Order | HANA parameter name in C# | Source |
|---|---|---|
| 1 | `branch` | Branch name derived from branch code: `OIL`, `BEVERAGES`, or `MART`. |
| 2 | `userId` | `BKDTModel.UserId`. |
| 3 | `transType` | `BKDTModel.TransType`. |
| 4 | `fromDate` | Parsed `BKDTModel.FromDate`, exact format `dd-MM-yyyy`. |
| 5 | `toDate` | Parsed `BKDTModel.ToDate`, exact format `dd-MM-yyyy`. |
| 6 | `timeLimit` | `BKDTModel.TimeLimit` or `DBNull.Value`. |
| 7 | `rights` | `BKDTModel.Rights`, default `"NO"`. |
| 8 | `createdBy` | `BKDTModel.CreatedBy`. |
| 9 | `createdOn` | `BKDTModel.CreatedOn` or `DBNull.Value`. |
| 10 | `deletedBy` | `BKDTModel.DeletedBy` or `DBNull.Value`. |
| 11 | `deletedOn` | `BKDTModel.DeletedOn` or `DBNull.Value`. |

Branch execution logic:

```text
Branch = "1,2"
  -> split into ["1", "2"]
  -> 1 maps to OIL and schema JIVO_OIL_HANADB
  -> 2 maps to BEVERAGES and schema JIVO_BEVERAGES_HANADB
  -> OPEN_BKDT executes once per branch
```

Failure behavior:

| Failure | C# behavior |
|---|---|
| Empty branch | `SaveBKDTAsync` returns `Success = false`, `Message = "Error: Branch is required..."`. |
| Invalid branch code | Returns error message with allowed branch codes. |
| Invalid date format inside service | Returns error message. |
| HANA exception | Caught by `SaveBKDTAsync`, returned as `Success = false`. |
| HANA success but SQL HANA status update fails | `BackDateSaveInHana` returns 400 with `BKDT saved but failed to update Hana status.` |

There is no explicit retry queue in C#. A failed HANA update can be retried by calling:

```text
POST /api/ItemMaster/BackDateSaveInHana?flowId={flowId}
```

or by approving again only if the SQL workflow still permits that action. Manual retry through `BackDateSaveInHana` is the safer operational path because it reuses the approved detail rows and updates `[backdate].[updateHanaStatus]`.

## Notification Flow

BackDate uses `NotificationService` from inside `ItemMasterService`.

### On Create

```text
CreateDocumentAsync
  -> [backdate].[jsCreateDocument]
  -> [backdate].[GetUsersInCurrentStage]
  -> _notificationService.GetUserFcmTokenAsync(userId)
  -> _notificationService.SendPushNotificationAsync(...)
  -> _notificationService.InsertNotificationAsync(...)
```

Notification payload:

```json
{
  "userId": "123",
  "company": "1",
  "DocId": "456",
  "screen": "BackDate"
}
```

Database notification fields:

```text
title: BackDate Request
message: A new BackDate document (Doc Id: {newId}) is awaiting your approval.
pageId: 5
data: Document ID: {newId}
BudgetId: {newId}
```

Note: the code comment says page ID for Credit Limit screen, but the title/data are BackDate. Verify the actual page mapping table used by `NotificationService.InsertNotificationAsync`.

### On Approval

```text
ApproveDocumentAsync
  -> [backdate].[jsApproveDocument]
  -> [backdate].[jsBackdateNotify]
  -> split CSV userIdsToApprove
  -> deduplicate users and tokens
  -> send FCM
  -> insert notification
```

Database notification fields on approval:

```text
title: BackDate
message: A new BackDate document (DocId: {docId}) is awaiting your approval.
pageId: 6
data: Flow ID: {flowId}
BudgetId: {flowId}
```

### Pending Reminder

`SendPendingBkdtCountNotificationAsync`:

1. Calls `_userService.GetActiveUser()`.
2. For each active user, calls `GetUserDocumentInsightsAsync(userId.ToString(), DateTime.Now.ToString("MM-yyyy"))`.
3. Sends an FCM notification if `PendingRequests > 0`.
4. Deduplicates tokens for the entire request.

## Frontend to Backend Trace

No dedicated BackDate view or `backdate.js` file was found under `Views` or `wwwroot/js`. The backend contract expected by any frontend is:

```text
Load metadata:
  GET /api/ItemMaster/GetUserDetails?company=1
  GET /api/ItemMaster/GetMobjDetails?company=1

Submit:
  POST /api/ItemMaster/CreateDocument

List:
  GET /api/ItemMaster/GetBKDTFullDetails?userId=101&company=1&month=05-2026
  GET /api/ItemMaster/GetBKDTPendingDoc?userId=101&company=1&month=05-2026

Details:
  GET /api/ItemMaster/GetBKDTDocumentDetail?documentId=456
  GET /api/ItemMaster/GetBackDateApprovalFlow?flowId=9001

Approve:
  POST /api/ItemMaster/ApproveBKDT

Reject:
  POST /api/ItemMaster/RejectBKDT
```

Any UI should keep the stored numeric codes in the create payload. Human-friendly labels are response transformations, not valid HANA input:

```text
Use Branch: "1" or "1,2"
Do not submit Branch: "OIL, BEVERAGES" to CreateDocument unless SQL procedures explicitly support it.
```

## Request and Response Examples

### Create Request

```http
POST /api/ItemMaster/CreateDocument
Content-Type: application/json
```

```json
{
  "branch": "1,2",
  "username": "SAPUSER01",
  "documentType": "13",
  "fromDate": "2026-05-01T00:00:00",
  "toDate": "2026-05-05T00:00:00",
  "timeLimit": "2026-05-05T18:00:00",
  "action": "A",
  "companyId": 1,
  "createdBy": 101
}
```

Typical response:

```json
{
  "newDocumentId": 456,
  "success": true,
  "message": "Document created successfully and notifications sent once per token."
}
```

### Approve Request

```http
POST /api/ItemMaster/ApproveBKDT
Content-Type: application/json
```

```json
{
  "flowId": 9001,
  "company": 1,
  "userId": 205,
  "remarks": "Approved"
}
```

If this is final approval and HANA succeeds:

```json
{
  "success": true,
  "flowId": 9001,
  "message": "BKDT document processed.",
  "approvalResult": {
    "success": true,
    "message": "Approved Document of FlowId 9001"
  },
  "flowStatus": {
    "status": "A"
  },
  "backDateResult": {
    "success": true,
    "flowId": 9001,
    "message": "BKDT saved and Hana status updated successfully.",
    "hanaStatusText": "BKDT executed successfully for: OIL (schema: JIVO_OIL_HANADB)."
  },
  "hanaStatusText": "HANA not triggered"
}
```

Note: `ApproveBKDT` initializes local `hanaStatusText` as `"HANA not triggered"` and does not update it from `BackDateSaveInHana` before returning. Use `backDateResult.hanaStatusText` for the actual HANA result.

### Reject Request

```json
{
  "flowId": 9001,
  "company": 1,
  "userId": 205,
  "remarks": "Date range needs correction"
}
```

### Manual HANA Retry

```http
POST /api/ItemMaster/BackDateSaveInHana?flowId=9001
```

Success response:

```json
{
  "success": true,
  "flowId": 9001,
  "message": "BKDT saved and Hana status updated successfully.",
  "hanaStatusText": "BKDT executed successfully for: OIL (schema: JIVO_OIL_HANADB)."
}
```

## Security Analysis

### Route Protection

`ItemMasterController.cs` has `[Authorize]`, and `Program.cs` adds a global `AuthorizeFilter` using the `SmartAuth` policy. The request authentication path is:

```text
Request
  -> SmartAuth policy scheme
  -> JWT bearer if Authorization starts with "Bearer "
  -> otherwise JSAP.Auth cookie
  -> global authorization filter
  -> AuthenticatedUserBindingFilter
  -> ItemMasterController action
```

Configured security controls:

| Control | Implementation |
|---|---|
| Authentication | `Program.cs` SmartAuth policy scheme with cookie and JWT bearer. |
| Global authorization | `Program.cs` adds `AuthorizeFilter(authenticatedPolicy)` to controllers and MVC. |
| Controller-level authorization | `ItemMasterController.cs` has `[Authorize]`. |
| Rate limiting | `Program.cs` global limiter: 40 mutations/minute for POST/PUT/PATCH/DELETE and 120 GET/default requests/minute per user/IP. |
| Session | `Program.cs` configures 8-hour session; BKDT controller actions do not directly read session keys. |
| SQL injection protection | Dapper parameters, `SqlCommand` parameters, stored procedures, and `HanaParameter` are used for BKDT values. |
| XSS/browser hardening | Security headers are configured in `Program.cs`. |
| CORS | Configured in `Program.cs` from environment/config allow-list. |

### Permission Checks

Several ItemMaster permission attributes near the BKDT section are commented out:

```csharp
// [CheckUserPermission("item_master_creation", "view")]
// [CheckUserPermission("item_master_creation", "create")]
```

As implemented, BKDT endpoints rely on authentication and procedure-level user/company filtering. If BackDate is financially sensitive in production, route-level permission attributes or explicit module permission checks should be reintroduced and tested against `PermissionService`.

### Approval Ownership

The C# approval endpoint accepts `flowId`, `Company`, and `UserId` from the request body and passes them to `[backdate].[jsApproveDocument]`. Any prevention of unauthorized approval, duplicate approval, company mismatch, inactive approver, or wrong-stage approval must be enforced by that stored procedure. The controller does not independently compare `request.UserId` to `HttpContext.User`.

### CSRF

BackDate POST routes can be called with cookie authentication. The app uses `SameSite=Strict` cookies, but the BKDT APIs do not show antiforgery token validation. If these endpoints are used from browser pages, this should be reviewed because posting-date access is financial-control sensitive.

## Audit and Compliance Tracking

BackDate changes affect financial posting access and must be treated as auditable control changes. In C#, audit-relevant values are captured in these fields:

| Audit value | Source |
|---|---|
| Request creator | `CreateDocumentRequest.CreatedBy`, SQL `@createdBy`. |
| Created timestamp | Stored by SQL procedure and returned as `createdOn`; later passed to HANA `createdOn`. |
| Target SAP user | `CreateDocumentRequest.Username`, `BKDTModel.UserId`. |
| Company/branch | `CompanyId` for SQL filtering; `Branch` for HANA company execution. |
| Document type | `DocumentType` / HANA `transType`. |
| Date range | `FromDate`, `ToDate`, HANA `fromDate`, `toDate`. |
| Time limit | `TimeLimit`, HANA `timeLimit`. |
| Approver | `ApproveRequestModel.UserId`, SQL `@userId`. |
| Approver remarks | `ApproveRequestModel.remarks`, `RejectRequestModel.remarks`. |
| Approval flow history | `[backdate].[jsGetBackDateApprovalFlow]` output. |
| HANA sync status | `[backdate].[updateHanaStatus]` through `UpdateHanaStatusRequest`. |

The C# code does not write an explicit audit table directly. Audit/history persistence is expected to be inside the `[backdate]` stored procedures.

Recommended production controls:

| Risk | Recommended control |
|---|---|
| Unauthorized financial-date access | Enforce module permission and approver ownership in both API and `[backdate].[jsApproveDocument]`. |
| Wrong branch/company update | Validate that `CompanyId` and `Branch` are aligned before HANA execution. |
| BackDate beyond allowed accounting period | Add SQL/HANA validation against open financial periods before `OPEN_BKDT`. |
| Silent HANA failure | Monitor rows where `HanaStatus` is false/null after final status `A`. |
| Manual direct HANA opening | Restrict or remove direct `/api/ItemMaster/SaveBKDT` access unless it is an admin-only operational endpoint. |
| Weak audit trace | Persist old/new access windows, approver id, request IP/user agent, HANA response, and retry attempts. |

## Production Debugging Guide

### Request Not Saving

Check in this order:

1. API: `POST /api/ItemMaster/CreateDocument`.
2. Controller: `ItemMasterController.CreateDocument`.
3. DTO: `CreateDocumentRequest` field names and date serialization.
4. Service: `ItemMasterService.CreateDocumentAsync`.
5. SQL: `[backdate].[jsCreateDocument]`.
6. Output: verify `@newDocumentId` is returned and greater than zero.
7. Notification side effect: `[backdate].[GetUsersInCurrentStage]` and FCM token lookup.

Common causes:

| Symptom | Likely cause |
|---|---|
| `Invalid request` | JSON body is null or invalid. |
| `Document creation failed or missing newDocumentId.` | Stored procedure did not set `@newDocumentId`. |
| Request exists but no notification | `[backdate].[GetUsersInCurrentStage]` returned no users or approvers have no FCM tokens. |

### Approval Stuck

Check:

1. API: `POST /api/ItemMaster/ApproveBKDT`.
2. Service: `ItemMasterService.ApproveDocumentAsync`.
3. Procedure: `[backdate].[jsApproveDocument]`.
4. Flow status: `GET /api/ItemMaster/GetFlowStatus?flowId={flowId}`.
5. Approval history: `GET /api/ItemMaster/GetBackDateApprovalFlow?flowId={flowId}`.
6. Next approvers: `[backdate].[jsBackdateNotify]` and `[backdate].[GetUsersInCurrentStage]`.

If `ApproveBKDT` returns success but HANA is not triggered, inspect `[backdate].[jsGetFlowStatus]`. HANA only runs when `Status == "A"`.

### HANA Update Failed

Check:

1. API: `POST /api/ItemMaster/BackDateSaveInHana?flowId={flowId}`.
2. SQL detail: `[backdate].[jsGetDocumentDetailUsingFlowId]`.
3. Date parsing in `ItemMasterController.TryParseDate`.
4. `documentType` parsing to integer.
5. Branch value: must be numeric `1`, `2`, `3`, or delimited numeric list before `SaveBKDTAsync`.
6. HANA procedure: `CALL "{schema}"."OPEN_BKDT"(?,?,?,?,?,?,?,?,?,?,?)`.
7. SQL status update: `[backdate].[updateHanaStatus]`.

Common failures:

| Error | Where it happens | Fix |
|---|---|---|
| `Invalid fromDate format` | `BackDateSaveInHana` | Correct date returned by `[backdate].[jsGetDocumentDetailUsingFlowId]`. |
| `Invalid toDate format` | `BackDateSaveInHana` | Same as above. |
| `Invalid documentType format` | `BackDateSaveInHana` | Ensure SQL detail returns numeric object type, not display name. |
| `Invalid branch code 'OIL'` | `SaveBKDTAsync` | Ensure SQL detail returns branch codes, not transformed names. |
| `Invalid company ID` | `GetLiveHanaSettings` | Branch/company outside 1, 2, 3. |
| `BKDT saved but failed to update Hana status` | SQL status update | HANA succeeded; inspect `[backdate].[updateHanaStatus]` separately. |

### Wrong Document Type Displayed

Display mapping occurs in `ItemMasterController.TransformDocsAsync` and detail methods:

```text
documentType string
  -> int.TryParse(documentType)
  -> lookup in GetMobjDetailsAsync(company)
  -> ObjType maps to ObjName
```

If the UI shows raw object type ids, inspect `GET /api/ItemMaster/GetMobjDetails?company={company}` and HANA `GETMOBJDETAILS`.

### Wrong Branch Displayed or Updated

Display and execution use different branch representations:

```text
Display mapping:
  1 -> OIL
  2 -> BEVERAGES
  3 -> MART

HANA execution:
  input branch codes -> mapped to branch names -> choose schema/connection
```

If HANA updates the wrong branch, compare:

1. `CreateDocumentRequest.Branch`.
2. Stored branch returned by `[backdate].[jsGetDocumentDetailUsingFlowId]`.
3. `BKDTModel.Branch` built in `BackDateSaveInHana`.
4. Branch split/mapping in `SaveBKDTAsync`.

### Date Not Updated or Access Window Not Opened in HANA

Check:

1. Does `[backdate].[jsGetFlowStatus]` return `A`?
2. Did `BackDateSaveInHana` run?
3. Did `OPEN_BKDT` execute for all branch codes?
4. Did `SaveBKDTAsync` return `Success = true`?
5. Did `[backdate].[updateHanaStatus]` record the success text?
6. Does the HANA `OPEN_BKDT` procedure internally validate financial periods or SAP user rights?

### Notifications Not Sent

Check:

1. On create: `[backdate].[GetUsersInCurrentStage]`.
2. On approval: `[backdate].[jsBackdateNotify]`.
3. Token lookup: `_notificationService.GetUserFcmTokenAsync(userId)`.
4. Push send: `_notificationService.SendPushNotificationAsync`.
5. Persisted notification: `_notificationService.InsertNotificationAsync`.
6. Page mapping: `pageId = 5` on create, `pageId = 6` on approval.

The code deduplicates FCM tokens, so a user with duplicated tokens may receive one push even if multiple rows are returned.

### Approval Visible to Wrong User

The most likely source is SQL workflow filtering, because C# does not independently compute visibility. Inspect:

```text
[backdate].[jsGetPendingDocuments]
[backdate].[GetUsersInCurrentStage]
[backdate].[jsBackdateNotify]
[backdate].[jsApproveDocument]
```

Also verify the caller-supplied `userId` and `company` query parameters. These are not read from authenticated claims by the controller.

## File Dependency Map

```text
ItemMasterController.cs
  uses IItemMasterService
  uses ILogger<ItemMasterController>
  transforms branch/action/documentType display values
  orchestrates ApproveBKDT -> GetFlowStatus -> BackDateSaveInHana

IItemMasterService.cs
  declares BKDT service methods

ItemMasterService.cs
  uses IConfiguration
  uses SqlConnection / Dapper / SqlCommand
  uses HanaConnection / HanaParameter
  uses INotificationService
  uses IUserService
  executes [backdate] procedures
  executes HANA GETUSERDETAILS, GETMOBJDETAILS, OPEN_BKDT

Models/ItemMasterModel.cs
  contains BKDT request, response, list, detail, flow, and status DTOs

NotificationService.cs
  fetches FCM tokens
  sends Firebase push notifications
  inserts notification rows

Program.cs
  registers IItemMasterService -> ItemMasterService
  registers INotificationService -> NotificationService
  configures authentication, authorization, session, rate limiting, CORS

appsettings.json
  provides SQL Server DefaultConnection
  provides HANA live connection strings
  provides Firebase/SAP configuration
```

## Performance and Failure Analysis

| Area | Risk | Why it matters | Debug approach |
|---|---|---|---|
| Approval stage procedures | Slow or stuck approvals | C# waits on `[backdate].[jsApproveDocument]`; all stage logic is in SQL. | Check execution plan, locks, and procedure messages. |
| Full details endpoint | Multiple SQL calls | `GetBKDTFullDetailsAsync` executes pending, approved, and rejected procedures separately. | Profile each procedure for the requested user/company/month. |
| HANA final sync | Sequential branch execution | Multi-branch requests execute `OPEN_BKDT` once per branch and stop on first failure. | Retry after fixing branch-specific HANA issue. |
| Date parsing | Format mismatch | Controller accepts many formats, service requires exact `dd-MM-yyyy`. | Inspect returned detail strings and controller conversion. |
| Company/branch mismatch | Wrong schema update | SQL `CompanyId` and HANA `Branch` are separate values. | Validate stored branch codes before final approval. |
| Concurrent approval | Duplicate stage or HANA execution | There is no C# lock for BKDT approval/HANA sync. | Ensure `[backdate].[jsApproveDocument]` is idempotent and protects duplicate approvals. |
| Notification loops | Slow response after create/approve | FCM sends happen inline before response returns. | Check token count and FCM latency. |
| HANA status update | Partial success | HANA can succeed while SQL `[backdate].[updateHanaStatus]` fails. | Re-run only status update if needed, or manual retry after confirming HANA state. |

## Compliance Risks and Recommended Controls

BackDate access changes are financially sensitive because they can allow posting or modifying documents in periods that may otherwise be closed or controlled.

| Risk | Current implementation | Recommended hardening |
|---|---|---|
| Direct HANA access path | `/api/ItemMaster/SaveBKDT` directly executes HANA `OPEN_BKDT`. | Restrict to admin/service role or remove from public controller surface. |
| Caller-supplied approver id | `ApproveBKDT` trusts `UserId` in body. | Compare `UserId` to authenticated claim and reject mismatch. |
| Module permission disabled | BKDT permission attributes are commented. | Add module-specific permission checks for create/view/approve/reject. |
| Financial period validation not visible | C# does not validate periods. | Enforce open-period checks in SQL/HANA and return explicit error text. |
| Limited HANA response detail | `OPEN_BKDT` is `ExecuteNonQueryAsync`; only exceptions are captured. | Capture affected rows/result output if procedure supports it. |
| No explicit retry audit in C# | Manual retry endpoint exists but no retry count is tracked in C#. | Store retry attempts, user, timestamp, and HANA response. |
| Ambiguous page id | BackDate notifications use `pageId` 5 and 6. | Verify notification page mappings and use module-specific constants. |

## Developer Checklist

Before changing BackDate behavior:

1. Confirm whether the change belongs in C# or in `[backdate]` stored procedures.
2. Preserve numeric branch codes through SQL detail reads until HANA execution.
3. Preserve numeric document type ids through SQL detail reads until HANA execution.
4. Test final approval where `[backdate].[jsGetFlowStatus]` returns `A`.
5. Test non-final approval where HANA must not run.
6. Test rejected flow and confirm HANA is not called.
7. Test multi-branch values such as `1,2`.
8. Validate HANA status text in list APIs after final approval.
9. Verify notification page routing for `pageId` 5 and 6.
10. Review permissions before exposing BackDate UI to financial users.
