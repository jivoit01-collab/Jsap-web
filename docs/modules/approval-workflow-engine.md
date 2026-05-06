# Approval Workflow Engine

This document describes the approval system as it is implemented in the current JSAPNEW codebase. The most important architectural fact is that this is not a single C# `WorkflowEngine` class. It is a database-driven workflow pattern shared by multiple modules:

```text
Razor page / JavaScript / API client
  -> module controller
  -> module service interface
  -> module service implementation
  -> module approval stored procedure
  -> module approval-flow tables and template/stage tables
  -> module notify stored procedure
  -> NotificationService / Firebase / notification tables
  -> optional SAP Service Layer or HANA update
```

The workflow rules that decide stage movement, final approval, rejection, and next approver are mostly inside SQL Server stored procedures. C# services are responsible for request validation, calling the stored procedures, triggering notifications, and in selected modules calling SAP after final approval.

## Architecture

```text
Frontend
  -> /api/{Module}/Approve... or /api/{Module}/Reject...
  -> [Authorize] controller
  -> SmartAuth cookie/JWT authentication
  -> global authorization filter
  -> module service
  -> Dapper / SqlCommand stored procedure call
  -> approval-stage state updated in SQL Server
  -> notification recipients resolved by module notify SP
  -> NotificationService sends FCM and inserts nt notification row
  -> final-stage SAP/HANA trigger when module implements it
```

## Core Files

| File | Role in approval system |
|---|---|
| `Program.cs` | Registers `SmartAuth`, global authorization, rate limiting, session, and all approval-related services. |
| `Controllers/AuthController.cs` | Budget approval/report API surface and `GetBudgetApprovalFlow`. |
| `Controllers/Auth2Controller.cs` | Template cloning plus budget allocation approval APIs. |
| `Controllers/ItemMasterController.cs` | IMC item approval/rejection, back-date approval/rejection, approval-flow APIs, SAP pending insertion APIs. |
| `Controllers/BomController.cs` | BOM approval/rejection, approval-flow, pending SAP insertion, product tree sync APIs. |
| `Controllers/Bom2Controller.cs` | Alternate/HANA-backed BOM approval and SAP session endpoints. |
| `Controllers/BPmasterController.cs` | BP master approval/rejection and approval-flow APIs. |
| `Controllers/AdvanceRequestController.cs` | Advance payment approval/rejection and expense approval-flow APIs. |
| `Controllers/CreditLimitController.cs` | Credit-limit approval/rejection, flow inspection, HANA status update, and SAP update trigger. |
| `Controllers/QcController.cs` | QC document approval/rejection and approval-flow APIs. |
| `Controllers/PrdoController.cs` | Production order approval/rejection and approval-flow APIs. |
| `Controllers/PaymentController.cs` | Payment approval/rejection APIs. |
| `Services/Implementation/UserService.cs` | Budget approval, workflow template/stage administration, delegation, next-approver lookup, budget notification dispatch. |
| `Services/Implementation/Auth2Service.cs` | Budget template cloning and budget allocation workflow procedures. |
| `Services/Implementation/ItemMasterService.cs` | IMC/BKDT approval logic, final-stage SAP item creation, duplicate SAP protection, IMC notifications. |
| `Services/Implementation/BomService.cs` | BOM approval/rejection, BOM notifications, SAP pending queue status updates. |
| `Services/Implementation/Bom2Service.cs` | Alternate BOM approval/rejection using SAP HANA procedures and SAP Service Layer sessions. |
| `Services/Implementation/BPmasterService.cs` | BP approval/rejection and BP SAP update procedure calls. |
| `Services/Implementation/AdvanceRequestService.cs` | Advance approval/rejection, next approver lookup, creator/approver notifications. |
| `Services/Implementation/CreditLimitService.cs` | Credit-limit approval/rejection, flow status lookup, SAP BusinessPartner PATCH, HANA status tracking. |
| `Services/Implementation/QcService.cs` | QC approval/rejection, next approver notifications, pending reminders. |
| `Services/Implementation/PrdoService.cs` | PRDO approval/rejection, multi-doc approval, next approver notifications. |
| `Services/Implementation/PaymentService.cs` | Payment approval/rejection stored procedure calls. |
| `Services/Implementation/NotificationService.cs` | FCM token lookup, push notification send, notification persistence in `nt` schema. |
| `Models/AuthModels.cs` | Template, stage, budget approval, delegation, next-approver, notification DTOs. |
| `Models/Auth2Models.cs` | Budget allocation approval DTOs and template-cloning DTOs. |
| `Models/ItemMasterModel.cs` | IMC/BKDT approval, pending SAP insertion, HANA status, and flow DTOs. |
| `Models/BomModels.cs` | BOM approval, rejection, pending SAP insertion, and flow DTOs. |
| `Models/BPmasterModels.cs` | BP approval/rejection and flow DTOs. |
| `Models/AdvanceRequestModels.cs` | Advance request approval/rejection, stage detail, next approver DTOs. |
| `Models/CreditLimitModels.cs` | Credit-limit approval/rejection, flow, HANA status, notification DTOs. |
| `Models/QcModels.cs` | QC approval/rejection and flow DTOs. |
| `Models/PrdoModels.cs` | Production order approval/rejection and flow DTOs. |
| `Models/PaymentModels.cs` | Payment pending/approved/rejected DTOs. |
| `Models/NotificationModels.cs` | Notification and device-token DTOs. |

## Dependency Map

```text
Template/stage setup
  -> UserService.AddStageAsync
  -> dbo.jsAddStage
  -> UserService.GetAddTemplateAsync
  -> dbo.jsAddTemplate
  -> Auth2Service.CloneTemplateWithNewStagesAsync
  -> dbo.jsCloneTemplateWithNewStages

Budget approval
  -> AuthController
  -> IUserService / UserService
  -> bud.jsApproveBudget / bud.jsRejectBudget
  -> bud.jsBudgetNotify
  -> NotificationService

IMC approval
  -> ItemMasterController
  -> IItemMasterService / ItemMasterService
  -> imc.jsGetItemCurrentStage
  -> imc.jsGetPendingItemApiInsertions if final stage
  -> SAP Service Layer Items if final stage
  -> imc.jsApproveItem or imc.jsRejectItem
  -> imc.jsImcNotify
  -> NotificationService

Credit Limit approval
  -> CreditLimitController
  -> ICreditLimitService / CreditLimitService
  -> cl.jsApproveDocument / cl.jsRejectDocument
  -> cl.jsGetFlowStatus
  -> SAP Service Layer BusinessPartners PATCH
  -> cl.updateHanaStatus
  -> cl.jsCreditLimitNotify
  -> NotificationService
```

# Modules Using Approval Flow

| Module | Controller APIs | Service | Main approval procedures | Flow inspection |
|---|---|---|---|---|
| Budget | `GET /api/Auth/GetBudgetApprovalFlow` plus budget approve/reject actions in `AuthController` | `UserService` | `bud.jsApproveBudget`, `bud.jsRejectBudget`, `bud.jsGetNextApprover`, `bud.jsBudgetNotify` | `bud.jsGetBudgetApprovalFlow` |
| Budget allocation | `POST /api/Auth2/approveBudgetAllocation`, `POST /api/Auth2/rejectBudgetAllocation` | `Auth2Service` | `bud.jsApproveBudgetAllocationRequest`, `bud.jsRejectBudgetAllocationRequest` | `bud.jsGetBudApprovalFlow` |
| IMC Item Master | `POST /api/ItemMaster/ApproveItem`, `POST /api/ItemMaster/RejectItem` | `ItemMasterService` | `imc.jsApproveItem`, `imc.jsRejectItem`, `imc.jsGetItemCurrentStage`, `imc.jsImcNotify` | `imc.jsGetIMCApprovalFlow` |
| Back Date document | `POST /api/ItemMaster/ApproveBKDT`, `POST /api/ItemMaster/RejectBKDT` | `ItemMasterService` | `backdate` schema approval procedures in service | `backdate` approval-flow procedures |
| BOM | `POST /api/Bom/approvebom`, `POST /api/Bom/rejectbom` | `BomService` | `bom.jsBomApprove`, `bom.jsBomReject`, `bom.jsBomNotify` | `bom.jsGetBomApprovalFlow` |
| BOM V2 | `POST /api/Bom2/BomApprove2`, `POST /api/Bom2/BomReject2` | `Bom2Service` | HANA procedures such as `bomjsRejectBom` and approval procedures called through `CALL` | `jsGetBomApprovalFlow` in HANA schema |
| BP Master | `POST /api/BPmaster/ApproveBP`, `POST /api/BPmaster/RejectBP` | `BPmasterService` | `BP.jsApproveBP`, `BP.jsRejectBP` | `BP.jsGetBPApprovalFlow` |
| Advance Request | `POST /api/AdvanceRequest/ApproveAdvPay`, `POST /api/AdvanceRequest/RejectedAdvPay` | `AdvanceRequestService` | `adv.jsApproveAdvPay`, `adv.jsRejectAdvPay`, `adv.jsGetNextApprover` | `adv.jsGetExpenseApprovalFlow` |
| Payment | `POST /api/Payment/approvepayment`, `POST /api/Payment/rejectpayment` | `PaymentService` | `pay.jsPaymentApprove`, `pay.jsPaymentReject` | No dedicated approval-flow endpoint found in controller. |
| Credit Limit | `POST /api/CreditLimit/ApproveDocument`, `POST /api/CreditLimit/RejectDocument` | `CreditLimitService` | `cl.jsApproveDocument`, `cl.jsRejectDocument`, `cl.jsGetFlowStatus`, `cl.updateHanaStatus`, `cl.jsCreditLimitNotify` | `cl.jsGetCreditLimitApprovalFlow` |
| QC | `POST /api/Qc/ApproveDocument`, `POST /api/Qc/RejectDocument` | `QcService` | `qc.jsApproveDocument`, `qc.jsRejectDocument`, `qc.jsQCNotify` | `qc.jsGetQCApprovalFlow` |
| PRDO | `POST /api/Prdo/ApproveProductionOrder`, `POST /api/Prdo/RejectProductionOrder` | `PrdoService` | `PRDO.jsApproveProductionOrder`, `PRDO.jsRejectProductionOrder`, `PRDO.jsPrdoNotify` | `PRDO.jsGetProductionOrderApprovalFlow` |
| Document Dispatch | Rejection/status APIs in `DocumentDispatchController` | `DocumentDispatchService` | `dds.UpdateDocumentStatus` | This is status management rather than the shared staged approval pattern. |

# Template and Stage System

Templates and stages are maintained through `UserService` and `Auth2Service`. The public API surface for these operations is limited in the current controllers, but the service layer contains the stored procedure calls used by administrative screens and older workflow configuration flows.

## Stage Creation

`UserService.AddStageAsync(AddStage StageData)` calls:

```text
dbo.jsAddStage
```

Input parameters:

| DTO property | SQL parameter | Meaning |
|---|---|---|
| `stage` | `@stage` | Stage display name. |
| `approvalId` | `@approvalId` | Approval action mapping/id. |
| `rejectId` | `@rejectId` | Rejection action mapping/id. |
| `userIds` | `@userIds` | Comma-separated users assigned to the stage. |
| `createdBy` | `@createdBy` | Admin/user creating the stage. |
| `company` | `@company` | Company scope. |
| `description` | `@description` | Stage description. |

Error behavior:

| Condition | Observed handling |
|---|---|
| Stored procedure throws SQL error `50004` | Service returns `0`, treated as already exists. |
| Other SQL exception | Rethrown inside method and then converted to `-1`. |
| General exception | Service returns `-1`. |

## Template Creation

`UserService.GetAddTemplateAsync(AddTemplateModel request)` calls:

```text
dbo.jsAddTemplate
```

Input parameters:

| DTO property | SQL parameter | Meaning |
|---|---|---|
| `template` | `@template` | Template name. |
| `createdBy` | `@createdBy` | User creating the template. |
| `stageIds` | `@stageIds` | Ordered/comma-separated stage ids. |
| `priority` | `@priority` | Priority/order values that define stage sequence. |
| `approvalIds` | `@approvalIds` | Approval action ids. |
| `company` | `@company` | Company scope. |
| `queries` | `@queries` | Stored query/condition mapping used by template logic. |

## Template Cloning

`Auth2Service.CloneTemplateWithNewStagesAsync(CloneTemplateModel model)` calls:

```text
dbo.jsCloneTemplateWithNewStages
```

`CloneTemplateModel` includes:

| Property | Meaning |
|---|---|
| `OldTemplateId` | Source template id. |
| `NewTemplateName` | New template name. |
| `StagesJson` | JSON array string containing new stage data. |

## Template Reads and Delegation

| Service method | Stored procedure | Purpose |
|---|---|---|
| `UserService.GetTemplateListAsync` | `dbo.jsGetTemplateList` | Lists templates by company. |
| `UserService.GetOneTemplateDetailAsync` | `dbo.jsGetTemplateDetail` | Returns template detail grouped by stage and users. |
| `UserService.GetStageListAsync` | `dbo.jsGetStageList` | Lists stages by company. |
| `UserService.GetOneStageDetailAsync` | `dbo.jsGetStageDetail` | Returns one stage, rejection flag, and users in the stage. |
| `UserService.GetTemplateListAccordingToUserAsync` | `dbo.jsGetTemplateListAccordingToUser` | Lists templates visible/applicable to a user. |
| `UserService.GetActiveTemplateSync` | `dbo.jsGetActiveTemplate` | Returns active templates by company. |
| `UserService.GetBudgetRelatedToTemplateAsync` | `dbo.jsGetBudgetRelatedToTemplate` | Maps budget data to a template. |
| `UserService.DelegateApprovalStagesTwoAsync` | `dbo.jsDelegateApprovalStagesTwo` | Delegates stages from one user to another. |
| `UserService.UpdateUserStageStatusTwoAsync` | `dbo.jsUpdateUserStageStatusTwo` | Activates/deactivates a user in a stage or delegation context. |
| `UserService.GetDelegatedUserListTwoAsync` | `dbo.jsGetDelegatedUserListTwo` | Lists delegation setup. |
| `UserService.UpdateDelegationDatesTwoAsync` | `dbo.jsUpdateDelegationDatesTwo` | Changes delegation dates. |

## Dynamic vs Static Approvers

The C# code passes stage users, template ids, user ids, company ids, and document ids into stored procedures. The actual approver selection is database-owned.

Patterns visible from code:

| Pattern | Evidence |
|---|---|
| Static stage users | `AddStage.userIds` is passed to `dbo.jsAddStage`. |
| Template ordered stages | `AddTemplate.stageIds` and `AddTemplate.priority` are passed to `dbo.jsAddTemplate`. |
| Company-scoped templates | Most template/stage methods include `company`. |
| Runtime next approvers | Module notify procedures return `userIdsToApprove`. |
| Delegation | `jsDelegateApprovalStagesTwo`, `jsGetDelegatedUserListTwo`, `jsUpdateDelegationDatesTwo`. |
| User active/inactive stage membership | `jsUpdateUserStageStatusTwo`. |

For production debugging, treat the stored procedures as the source of truth for:

```text
current stage
next stage
stage priority
stage user membership
delegated approver
rejection eligibility
final-stage detection
document ownership
```

# Approval Lifecycle

## Common Lifecycle

```text
Create request
  -> module insert procedure creates document and flow rows
  -> first stage is pending
  -> pending API lists document for stage users
  -> approver submits approve/reject
  -> module approval SP validates user/company/stage
  -> current stage is marked approved/rejected
  -> if more stages remain, next stage becomes pending
  -> notify SP returns next approver user ids
  -> NotificationService sends FCM and inserts nt notification rows
  -> if final approved, module may trigger SAP/HANA sync
```

## Rejection Lifecycle

```text
Reject request
  -> module reject endpoint receives flow/document id, user id, company, remarks
  -> reject SP validates current-stage authority
  -> flow/document status becomes rejected
  -> rejection metadata/remarks are stored by DB procedure
  -> pending APIs should no longer return it to next approvers
  -> rejected APIs and creator-specific views may return it
```

Current code has no shared C# implementation for returned/resubmitted/cancelled/expired approvals. Some request models include `Action` fields, but actual support depends on each stored procedure. Treat anything beyond approve/reject/pending/approved/rejected as module-specific.

## State Transition Model

| State/status | Meaning | Where it appears |
|---|---|---|
| `Pending` | Waiting for current approver/stage. | Set in service merge methods after pending SP reads, for example `GetAllItemsAsync`, `GetAllDocumentsAsync`, `GetAllProductionOrdersAsync`. |
| `Approved` | Approved at final stage or in approved list. | Approved SP result sets and service merge methods. |
| `Rejected` | Rejected by an approver. | Rejected SP result sets and service merge methods. |
| `A` | Approved action/status in some stage result sets. | `AdvanceRequestService.GetApprovedStageAsync`, `CreditLimitService.GetFlowStatusAsync`. |
| `R` | Rejected action/status in some stage result sets. | `AdvanceRequestService.RejectAdvancePaymentAsync`. |
| `Done` | C# response status after IMC DB approval succeeds. | `ItemMasterService.ApproveItemAsync`. |
| `Blocked` | C# response status when IMC approval is prevented before DB approval. | `ItemMasterService.ApproveItemAsync`. |
| `P` | SAP item creation processing/pending marker. | `ItemMasterService.UpdateItemApiStatusAsync`. |
| `Y` | SAP item creation already completed marker. | `ItemMasterService.UpdateItemApiStatusAsync`. |
| `N` | SAP item creation failed marker. | `ItemMasterService.UpdateItemApiStatusAsync`. |
| `HanaStatus` true/false | Credit-limit SAP/HANA update result. | `CreditLimitService.UpdateHanaStatusAsync`, `cl.updateHanaStatus`. |

# Database Flow and Stored Procedures

The database schema source files are not included in the repository. The table names below are only listed where code references them directly. For all workflow-stage tables, inspect the stored procedures in SQL Server with `sp_helptext` or the database project if available.

## Template and Workflow Configuration Procedures

| Stored procedure | Called from | Inputs visible in C# | Purpose |
|---|---|---|---|
| `dbo.jsAddStage` | `UserService.AddStageAsync` | `@stage`, `@approvalId`, `@rejectId`, `@userIds`, `@createdBy`, `@company`, `@description` | Creates a reusable approval stage and maps users/actions to it. |
| `dbo.jsAddTemplate` | `UserService.GetAddTemplateAsync` | `@template`, `@createdBy`, `@stageIds`, `@priority`, `@approvalIds`, `@company`, `@queries` | Creates workflow template and ordered stage mapping. |
| `dbo.jsGetTemplateList` | `UserService.GetTemplateListAsync` | `@company` | Lists templates. |
| `dbo.jsGetTemplateDetail` | `UserService.GetOneTemplateDetailAsync` | `@tempId`, `@company` | Returns template detail with stages/users. |
| `dbo.jsGetStageList` | `UserService.GetStageListAsync` | `@company` | Lists stages. |
| `dbo.jsGetStageDetail` | `UserService.GetOneStageDetailAsync` | `@stageId`, `@company` | Returns stage users and rejection flag. |
| `dbo.jsCloneTemplateWithNewStages` | `Auth2Service.CloneTemplateWithNewStagesAsync` | model data including old template, new name, `StagesJson` | Clones a template and replaces or creates stage structure. |
| `dbo.jsDelegateApprovalStagesTwo` | `UserService.DelegateApprovalStagesTwoAsync` | `@userId`, `@delegatedUserId`, `@stages`, `@startDate`, `@endDate` | Delegates approval authority. |
| `dbo.jsUpdateUserStageStatusTwo` | `UserService.UpdateUserStageStatusTwoAsync` | `@StageId`, `@UserId`, `@delegatedUserId`, `@Activate` | Activates/deactivates stage user/delegation mapping. |

## Module Approval Procedures

| Module | Stored procedure | Called from | Inputs visible in C# | Output/behavior visible in C# |
|---|---|---|---|---|
| Budget | `bud.jsApproveBudget` | `UserService.ApproveBudgetAsync` | `@docId`, `@userId`, `@company`, `@remarks` | Scalar message. Notifications fetched after each doc id. |
| Budget | `bud.jsRejectBudget` | `UserService.RejectBudgetAsync` | budget request fields including user/company/doc | Scalar message. |
| Budget | `bud.jsGetNextApprover` | `UserService.GetNextApproverAsync` | `@budgetId` | `NextApproverModel` rows. |
| Budget | `bud.jsGetBudgetApprovalFlow` | `UserService.GetBudgetApprovalFlowAsync` | `@budgetId` | `BudgetApprovalFlowModel` rows. |
| Budget allocation | `bud.jsApproveBudgetAllocationRequest` | `Auth2Service.ApproveBudgetAllocationRequestAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | `ApproveBudgetAllocationResponse`. |
| Budget allocation | `bud.jsRejectBudgetAllocationRequest` | `Auth2Service.RejectBudgetAllocationRequestAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | `RejectBudgetAllocationResponse`. |
| IMC | `imc.jsGetItemCurrentStage` | `ItemMasterService.ApproveItemAsync` | `@flowId` | `CurrentStage`, `TotalStage`, `CurrentStageId`, `Status`, `IsLastStage`. |
| IMC | `imc.jsApproveItem` | `ItemMasterService.ApproveItemAsync` | `@itemId`, `@company`, `@userId`, `@remarks` | Scalar message; only called after SAP succeeds on final stage. |
| IMC | `imc.jsRejectItem` | `ItemMasterService.RejectItemAsync` | `@itemId`, `@company`, `@userId` | String result message. |
| IMC | `imc.jsGetIMCApprovalFlow` | `ItemMasterService.GetIMCApprovalFlowAsync` | `@flowId` | `GetIMCApprovalFlowModel` rows. |
| IMC | `imc.jsGetPendingItemApiInsertions` | `ItemMasterService.GetPendingItemApiInsertionsAsync` | `@itemId` | SAP queue rows. |
| IMC | `imc.jsUpdateItemApiStatus` | `ItemMasterService.UpdateItemApiStatusAsync` | `@itemId`, `@apiMessage`, `@tag` | Previous tag, used for duplicate protection. |
| IMC | `imc.LogApiError` | `ItemMasterService.LogApiErrorAsync` | reference id, API name, error, payload, created by | Persists SAP/API errors. |
| BOM | `bom.jsBomApprove` | `BomService.BomApproveAsync` | `@bomId`, `@userId`, `@description` | `BomResponse` rows. |
| BOM | `bom.jsBomReject` | `BomService.BomRejectAsync` | `@bomId`, `@userId`, `@description` | `BomResponse` rows. |
| BOM | `bom.jsGetBomApprovalFlow` | `BomService.GetBomApprovalFlowAsync` | `@bomId` | `ApprovalFlowRequest` rows. |
| BOM | `bom.jsGetPendingApiInsertions` | `BomService.GetPendingInsertionsAsync` | `@bomId`, `@action` | Pending SAP product tree rows. |
| BOM | `bom.jsUpdateBomApiStatus` | `BomService.UpdateBomApiStatus` / `Bom2Service` | `@bomId`, `@apiMessage`, `@tag` | Updates product tree SAP sync state. |
| BP Master | `BP.jsApproveBP` | `BPmasterService.ApproveBPAsync` | `@flowid`, `@company`, `@userId` | `ApproveOrRejectBpResponse`. |
| BP Master | `BP.jsRejectBP` | `BPmasterService.RejectBPAsync` | `@flowid`, `@company`, `@userId` | `ApproveOrRejectBpResponse`. |
| BP Master | `BP.jsGetBPApprovalFlow` | `BPmasterService.GetBPApprovalFlowAsync` | `@flowId` | `BPApprovalFlowModel` rows. |
| Advance Request | `adv.jsApproveAdvPay` | `AdvanceRequestService.ApproveAdvancePaymentAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | String message; then creator and next approvers notified. |
| Advance Request | `adv.jsRejectAdvPay` | `AdvanceRequestService.RejectAdvancePaymentAsync` | `@flowid`, `@company`, `@userId`, `@remarks`, `@action` | String message; creator notified. |
| Advance Request | `adv.jsGetNextApprover` | `AdvanceRequestService.GetApprovalUserIdsAsync` | `@advPayId` | `ApprovalIdsModel` rows. |
| Advance Request | `adv.jsGetExpenseApprovalFlow` | `AdvanceRequestService.GetExpenseApprovalFlowAsync` | `@flowId` | Stage detail rows. |
| Payment | `pay.jsPaymentApprove` | `PaymentService.ApprovePaymentAsync` | `@paymentId`, `@userId` | Integer result. |
| Payment | `pay.jsPaymentReject` | `PaymentService.RejectPaymentAsync` | `@paymentId`, `@userId`, `@description` | Integer result. |
| Credit Limit | `cl.jsApproveDocument` | `CreditLimitService.ApproveDocumentAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | Scalar message. |
| Credit Limit | `cl.jsRejectDocument` | `CreditLimitService.RejectDocumentAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | Execute success/error. |
| Credit Limit | `cl.jsGetCreditLimitApprovalFlow` | `CreditLimitService.GetApprovalFlowAsync` | `@flowId` | `CreditLimitApprovalFlowDto` rows. |
| Credit Limit | `cl.jsGetFlowStatus` | `CreditLimitService.GetFlowStatusAsync` | `@flowId` | `status`, expected final approved value `A`. |
| Credit Limit | `cl.updateHanaStatus` | `CreditLimitService.UpdateHanaStatusAsync` | `@flowId`, `@status`, `@hanaStatusText` | Persists SAP/HANA sync result. |
| QC | `qc.jsApproveDocument` | `QcService.ApproveDocumentAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | Scalar message. |
| QC | `qc.jsRejectDocument` | `QcService.RejectDocumentAsync` | `@flowId`, `@company`, `@userId`, `@remarks` | Dynamic result message. |
| QC | `qc.jsGetQCApprovalFlow` | `QcService.GetQCApprovalFlowAsync` | `@flowId` | `QCApprovalFlowModel` rows. |
| PRDO | `PRDO.jsApproveProductionOrder` | `PrdoService.ApproveProductionOrderAsync` | `@docId`, `@company`, `@userId`, `@remarks` | Scalar message per doc id. |
| PRDO | `PRDO.jsRejectProductionOrder` | `PrdoService.RejectProductionOrderAsync` | `@docId`, `@company`, `@userId`, `@remarks` | Reader with `ResultMessage`. |
| PRDO | `PRDO.jsGetProductionOrderApprovalFlow` | `PrdoService.GetProductionOrderApprovalFlowAsync` | production order id | `ProductionOrderApprovalFlowModel` rows. |

## Notification Procedures

| Module | Notify procedure | Called from | Returned data |
|---|---|---|---|
| Budget | `bud.jsBudgetNotify` | `UserService.GetUserIdsSendNotificatiosAsync` | `UserIdsForNotificationModel.userIdsToApprove`. |
| IMC | `imc.jsImcNotify` | `ItemMasterService.GetItemUserIdsSendNotificatiosAsync` | `UserIdsForNotificationModel.userIdsToApprove`. |
| IMC current users | `imc.GetUsersInCurrentStage` | `ItemMasterService.GetItemCurrentUsersSendNotificationAsync` | `AfterCreatedRequestSendNotificationToUser`. |
| BOM | `bom.jsBomNotify` | `BomService.GetBomUserIdsSendNotificatiosAsync` | `UserIdsForNotificationModel.userIdsToApprove`. |
| BOM current users | `bom.GetUsersInCurrentStage` | `BomService.GetBomCurrentUsersSendNotificationAsync` | Current-stage users. |
| Credit Limit | `cl.jsCreditLimitNotify` | `CreditLimitService.GetCLUserIdsSendNotificatiosAsync` | `UserIdsForNotificationModel.userIdsToApprove`. |
| Credit Limit current users | `cl.GetUsersInCurrentStage` | `CreditLimitService.GetCurrentUsersSendNotificationAsync` | Current-stage users. |
| QC | `qc.jsQCNotify` | `QcService.GetQcUserIdsSendNotificatiosAsync` | `UserIdsForNotificationModel.userIdsToApprove`. |
| QC current users | `qc.GetUsersInCurrentStage` | `QcService.GetQcCurrentUsersSendNotificationAsync` | Current-stage users. |
| PRDO | `PRDO.jsPrdoNotify` | `PrdoService.GetProductionUserIdsSendNotificatiosAsync` | `UserIdsForNotificationModel.userIdsToApprove`. |

# Next Approver Logic

Next approver resolution is database-first. The C# code generally does this:

```text
Approve current stage
  -> call module approve SP
  -> call module notify SP
  -> parse comma-separated userIdsToApprove
  -> deduplicate user ids
  -> fetch FCM tokens
  -> send notification and insert notification row
```

## Module-Specific Next Approver Lookups

| Module | Next approver source |
|---|---|
| Budget | `bud.jsGetNextApprover` and `bud.jsBudgetNotify`. |
| Advance Request | `adv.jsGetNextApprover`. |
| IMC | `imc.jsImcNotify` after approval, `imc.GetUsersInCurrentStage` for current-stage users. |
| BOM | `bom.jsBomNotify` after approval, `bom.GetUsersInCurrentStage` for current-stage users. |
| Credit Limit | `cl.jsCreditLimitNotify` after approval, `cl.GetUsersInCurrentStage` for current-stage users. |
| QC | `qc.jsQCNotify` after approval, `qc.GetUsersInCurrentStage` for current-stage users. |
| PRDO | `PRDO.jsPrdoNotify`. |
| BP Master | Approval result is DB-driven through `BP.jsApproveBP`; no C# notification block was found in `BPmasterService.ApproveBPAsync`. |

## Edge Cases

| Edge case | Where to debug |
|---|---|
| No next approver found | Module notify SP, template-stage user mapping, delegated user mapping, active/inactive stage user status. |
| Same user appears in multiple stages | C# notification code deduplicates by user/token in many modules, but DB approval ownership must be checked in the module approve SP. |
| Approver inactive | `dbo.jsUpdateUserStageStatusTwo`, user active status, module pending SP filters. |
| User company mismatch | Module approve SP parameters include `company`; inspect procedure logic and user/company mapping. |
| Parallel approvals | Most modules depend on DB protection. IMC additionally uses in-memory semaphore locks for item approval and SAP posting. |
| Duplicate notification | Many services deduplicate `userIdsToApprove` and `fcmToken`; check module service if duplicates persist. |
| Delegation not honored | Inspect `jsDelegateApprovalStagesTwo`, `jsGetDelegatedUserListTwo`, `jsUpdateDelegationDatesTwo`, and module notify/pending SPs. |

# Notification Flow

`NotificationService` owns the shared notification implementation.

## NotificationService Procedures

| Procedure | Called from | Purpose |
|---|---|---|
| `nt.jsGetUnreadNotificationCount` | `NotificationService.GetUnreadNotificationCountAsync` | Returns unread count. |
| `nt.jsGetUserNotifications` | `NotificationService.GetUserNotificationsAsync` | Returns notification list. |
| `nt.jsInsertNotification` | `NotificationService.InsertNotificationAsync` | Persists notification row. |
| `nt.jsMarkAllNotificationsAsRead` | `NotificationService.MarkAllNotificationsAsReadAsync` | Marks all user notifications read. |
| `nt.jsMarkNotificationAsRead` | `NotificationService.MarkNotificationAsReadAsync` | Marks one notification read. |
| `nt.jsSaveUserToken` | `NotificationService.SaveUserToken` | Saves FCM token/device id. |
| `nt.jsDeleteOldNotifications` | `NotificationService.DeleteOldNotificationsAsync` | Cleanup old notifications. |
| `nt.jsDeleteOldUserTokens` | `NotificationService.DeleteOldUserTokensAsync` | Cleanup old user tokens. |

## Common Notification Algorithm

```text
Module approval succeeds
  -> module notify SP returns comma-separated userIdsToApprove
  -> service removes blank rows
  -> service groups duplicate userIdsToApprove strings
  -> service expands to distinct user ids
  -> NotificationService.GetUserFcmTokenAsync(userId)
  -> send FCM once per token
  -> NotificationService.InsertNotificationAsync(...)
```

Modules implementing this pattern:

```text
Budget
BOM
IMC Item Master
Advance Request
Credit Limit
QC
PRDO
```

Pending reminder jobs are exposed as regular APIs, not background workers:

| API | Service method |
|---|---|
| `GET /api/ItemMaster/SendPendingItemCountNotification` | `ItemMasterService.SendPendingItemCountNotificationAsync` |
| `GET /api/Bom/SendPendingBomCountNotification` | `BomService.SendPendingBomCountNotificationAsync` |
| `GET /api/AdvanceRequest/SendPendingPaymentNotify` | `AdvanceRequestService.SendPaymentPendingCountNotificationAsync` |
| `GET /api/CreditLimit/SendPendingCLCountNotification` | `CreditLimitService.SendPendingCLCountNotificationAsync` |
| `GET /api/Qc/SendPendingQcCountNotification` | `QcService.SendPendingQcCountNotificationAsync` |
| `GET /api/Prdo/SendPendingProductionCountNotification` | `PrdoService.SendPendingProductionCountNotificationAsync` |

# SAP and HANA Integration After Approval

## IMC Item Master

IMC is the strictest approval/SAP integration.

```text
POST /api/ItemMaster/ApproveItem
  -> ItemMasterService.ApproveItemAsync
  -> imc.jsGetItemCurrentStage
  -> if IsLastStage = true:
       imc.jsGetPendingItemApiInsertions
       PostItemsToSAPAsync
       imc.jsUpdateItemApiStatus tag P/Y/N
       imc.LogApiError on failures
       block DB approval if SAP fails
  -> imc.jsApproveItem
  -> imc.jsImcNotify
```

Important behavior:

| Behavior | Implementation |
|---|---|
| Final stage detection | `imc.jsGetItemCurrentStage`, not SAP pending rows. |
| SAP before DB final approval | Final stage posts to SAP before `imc.jsApproveItem`. |
| Duplicate approval guard | `_approvalLocks` keyed by item/flow id. |
| Duplicate SAP post guard | `_sapPostLocks` keyed by `InitId`. |
| SAP processing tag | `imc.jsUpdateItemApiStatus(..., "P")`. |
| SAP success tag | `Y`. |
| SAP failure tag | `N`. |
| Unsupported company | Logs `UNSUPPORTED_COMPANY` through `imc.LogApiError`. |
| SAP session | `Bom2Service.GetSAPSessionOilAsync`, `GetSAPSessionBevAsync`, `GetSAPSessionMartAsync`. |

## BOM

BOM has a pending product-tree insertion flow:

```text
Get pending product tree rows
  -> /api/Bom/GetPendingInsertions
  -> BomService.GetPendingInsertionsAsync
  -> bom.jsGetPendingApiInsertions
  -> ProductTrees / UpdateProductTrees APIs post to SAP
  -> bom.jsUpdateBomApiStatus
```

`Bom2Service` also contains HANA-backed calls such as:

```text
CALL "TEST_OIL_11FEB"."bomJsGetPendingApiInsertions"(?)
CALL "TEST_OIL_11FEB"."bomJsUpdateApiStatus"(?, ?)
```

## Credit Limit

Credit-limit SAP sync is explicit and status-tracked:

```text
ApproveDocument
  -> cl.jsApproveDocument
  -> CreditLimitController checks/executes UpdateCreditLimitAsync
  -> cl.jsGetFlowStatus
  -> only status == "A" allows SAP update
  -> cl.jsGetDocumentDetailUsingFlowId
  -> choose SAP session by branch/company
  -> PATCH BusinessPartners('{customerCode}')
  -> cl.updateHanaStatus true/false with message
```

Failure states are persisted through `cl.updateHanaStatus`, including:

```text
Flow is not approved from final stage
Document detail not found
HANA PATCH failed: ...
Exception occurred: ...
```

## BP Master

`BPmasterService` includes `UpdateSapDataAsync`, which calls:

```text
BP.jsUpdateSAPData
```

Approval itself is handled by:

```text
BP.jsApproveBP
BP.jsRejectBP
```

No direct SAP Service Layer call was found inside `ApproveBPAsync`; SAP update is a separate service method/API path.

# Security Analysis

## Route Protection

Most approval controllers are decorated with:

```csharp
[Route("api/[controller]")]
[ApiController]
[Authorize]
```

This applies to:

```text
ItemMasterController
BomController
CreditLimitController
QcController
PrdoController
BPmasterController
AdvanceRequestController
PaymentController
AuthController except [AllowAnonymous] auth endpoints
```

## Authentication and Authorization Pipeline

`Program.cs` configures:

```text
SmartAuth policy scheme
  -> JWT bearer when Authorization header starts with Bearer
  -> cookie auth otherwise

Global AuthorizeFilter
  -> authenticated policy for controllers and MVC views

Session
  -> stores user/company context for Razor flows
```

Approval requests generally pass `userId` and `company` in body/query. The database procedures are expected to verify that the user is allowed to approve the current stage for that company. C# controllers do not consistently derive approver identity from claims for approval requests.

## Permission Checks

Several controllers contain commented permission attributes such as:

```csharp
// [CheckUserPermission("item_master_creation", "view")]
// [CheckUserPermission("ADV", "approver")]
```

The active fine-grained enforcement for approval endpoints is therefore mostly:

```text
[Authorize]
module stored procedure validation
dynamic menu permissions in _Layout.cshtml
```

`CheckUserPermissionAttribute.cs` exists, but it reads `companyId` from session while current web session code commonly uses `selectedCompanyId`. If this attribute is re-enabled on approval endpoints, align these session keys first.

## SQL Injection Protection

Approval services use parameterized Dapper calls or `SqlCommand` parameters:

```text
connection.QueryAsync(..., parameters, commandType: StoredProcedure)
cmd.Parameters.AddWithValue(...)
```

This is the primary SQL injection defense. The main risk is not SQL injection in approval methods; it is missing or incomplete authorization inside stored procedures because many requests accept `userId` from the client.

## CSRF and Browser Requests

The app uses HTTP-only cookies and `SameSite=Strict`, but approval POST APIs do not show antiforgery token validation. Since browser AJAX uses cookie auth, high-risk approval endpoints should be reviewed if cross-site exposure changes.

## Rate Limiting

`Program.cs` adds a global fixed-window rate limiter and a mutation limiter:

```text
POST/PUT/PATCH/DELETE: 40 requests/minute per user/IP
global/default: 120 requests/minute per user/IP
payment routes: 30 requests/minute
```

Approval endpoints are covered by the global/mutation limiter unless a named limiter is applied.

# Request and Response Examples

## Frontend Entry Points

Most approval screens are Razor views with inline JavaScript rather than a centralized frontend approval SDK. `wwwroot/js/auth.js` wraps `fetch` and jQuery AJAX globally, adds `credentials: include`, refreshes authentication on 401, and retries once.

| Frontend file/view | Approval relevance |
|---|---|
| `wwwroot/js/budget.js` | Calls `/api/auth/GetBudgetApprovalFlow?budgetId=...` for budget approval-flow detail. |
| `Views/ReportsWeb/ApprovalStatusReport.cshtml` | Budget approval status report UI with pending/approved/rejected tabs. |
| `Views/Shared/_Layout.cshtml` | Dynamic menu includes `Approval Management` and `Approval Status Report` entries based on permission names returned by `/api/Permission/GetUserEffectivePermissions`. |
| `Views/UserManagement/UserPermission.cshtml` | Lets admins assign `approvalIds` through `/api/auth/addUserPermissions`; approval ids are part of user permission setup. |
| `Views/UserManagement/EditUser.cshtml` | Loads approval dropdowns from `/api/auth/getapprovals` and updates user approval permissions through `/api/auth/updateuserapproval`. |
| `Views/UserManagement/DocumentRecieve.cshtml` | Document receive/reject UI; uses document status operations, not the shared staged approval pattern. |
| `Views/BPmasterweb/Index.cshtml`, `Views/BPmasterweb/Index1.cshtml` | BP creation screens that create BP requests later approved through BP approval APIs. |
| `Views/QcWeb/AddQC.cshtml`, `Views/QcWeb/QualityCheck.cshtml` | QC form/document frontend screens using `/api/QC` endpoints. |
| `Views/PaymentChecker/PaymentCheckerPage.cshtml`, `Views/Invoicepayment/InvoicePaymentPage.cshtml` | Payment/checker status views with approved/rejected states. |

`Views/Shared/_Layout.cshtml` references an `ApprovalManagement` menu with `TemplateManager`, `StageManager`, and `QueryGenerator` actions. No matching `ApprovalManagementController` file was found in the current source tree, so treat this menu area as either pending implementation, removed controller code, or a route supplied outside the checked-in project.

## IMC Approval

```http
POST /api/ItemMaster/ApproveItem
Content-Type: application/json
```

```json
{
  "itemId": 101,
  "company": 1,
  "userId": 25,
  "remarks": "Approved"
}
```

Possible success response:

```json
{
  "success": true,
  "message": "SAP item created successfully | Approved Document of FlowId 101 | API Triggered after final approval",
  "approvalStatus": "Done",
  "sapStatus": "Success",
  "martStatus": "Skipped (not FG item or intermediate stage)"
}
```

Possible blocked response:

```json
{
  "success": false,
  "message": "Final approval stage detected, but no pending SAP item data was found. DB approval was not completed.",
  "approvalStatus": "Blocked",
  "sapStatus": "Skipped - no pending SAP rows",
  "martStatus": "Skipped"
}
```

## IMC Rejection

```http
POST /api/ItemMaster/RejectItem
Content-Type: application/json
```

```json
{
  "itemId": 101,
  "company": 1,
  "userId": 25
}
```

Service call:

```text
ItemMasterController.RejectItem
  -> ItemMasterService.RejectItemAsync
  -> imc.jsRejectItem
```

## Credit Limit Approval

```http
POST /api/CreditLimit/ApproveDocument
Content-Type: application/json
```

```json
{
  "flowId": 5001,
  "company": 1,
  "userId": 25,
  "remarks": "Approved",
  "action": "Approve"
}
```

Service flow:

```text
CreditLimitController.ApproveDocument
  -> CreditLimitService.ApproveDocumentAsync
  -> cl.jsApproveDocument
  -> cl.jsCreditLimitNotify
  -> NotificationService
  -> UpdateCreditLimitAsync when controller triggers HANA update
```

## Advance Request Approval

```http
POST /api/AdvanceRequest/ApproveAdvPay
Content-Type: application/json
```

```json
{
  "flowId": 3001,
  "company": 2,
  "userId": 44,
  "remarks": "Approved",
  "action": "Approve"
}
```

Service flow:

```text
AdvanceRequestController.ApproveAdvancePayment
  -> AdvanceRequestService.ApproveAdvancePaymentAsync
  -> adv.jsApproveAdvPay
  -> adv.jsGetExpenseApprovalFlow
  -> adv.jsGetAdvPayCreatedBy
  -> adv.jsGetNextApprover
  -> NotificationService
```

## PRDO Multi-Approval

```http
POST /api/Prdo/ApproveProductionOrder
Content-Type: application/json
```

```json
{
  "docIds": "9001,9002",
  "company": 1,
  "userId": 12,
  "remarks": "Approved"
}
```

`PrdoService.ApproveProductionOrderAsync` splits `docIds`, calls `PRDO.jsApproveProductionOrder` for each id, merges notification recipients, deduplicates tokens, and inserts one notification per recipient.

# Production Debugging Guide

## Approval Is Stuck

Check in this order:

1. Confirm the pending API returns the record for the expected approver and company.
2. Open the module service and identify the pending SP:
   - IMC: `imc.jsGetPendingItems`
   - BOM: `bom` pending procedures
   - Budget: `bud.jsGetPendingBudgets`
   - Advance: `adv.jsGetPendingExpenses`
   - Credit Limit: `cl.jsGetPendingDocuments`
   - QC: `qc.jsGetPendingDocuments`
   - PRDO: `PRDO.jsGetPendingProductionOrders`
3. Inspect the approval-flow endpoint:
   - IMC: `/api/ItemMaster/GetIMCApprovalFlow?flowId=...`
   - BOM: `/api/Bom/GetBomApprovalFlow?bomId=...`
   - Budget: `/api/Auth/GetBudgetApprovalFlow?budgetId=...`
   - Advance: `/api/AdvanceRequest/GetExpenseApprovalFlow?flowId=...`
   - Credit Limit: `/api/CreditLimit/GetApprovalFlow?flowId=...`
   - QC: `/api/Qc/GetQCApprovalFlow?flowId=...`
   - PRDO: `/api/Prdo/GetProductionOrderApprovalFlow?productionOrderId=...`
4. Inspect the module approve SP and stage tables in SQL Server.
5. Check whether the current stage user is inactive or delegated incorrectly.
6. For IMC final stage, check SAP queue rows before assuming DB approval is broken.

## Next Approver Missing

Likely locations:

| Module | Check |
|---|---|
| Budget | `bud.jsGetNextApprover`, `bud.jsBudgetNotify`, template-stage users. |
| Advance | `adv.jsGetNextApprover`, `adv.jsGetExpenseApprovalFlow`. |
| IMC | `imc.jsImcNotify`, `imc.GetUsersInCurrentStage`, `imc.jsGetItemCurrentStage`. |
| BOM | `bom.jsBomNotify`, `bom.GetUsersInCurrentStage`. |
| Credit Limit | `cl.jsCreditLimitNotify`, `cl.GetUsersInCurrentStage`. |
| QC | `qc.jsQCNotify`, `qc.GetUsersInCurrentStage`. |
| PRDO | `PRDO.jsPrdoNotify`. |

Fast SQL checks:

```sql
EXEC [schema].[jsGet...ApprovalFlow] @flowId_or_docId;
EXEC [schema].[GetUsersInCurrentStage] @id;
EXEC [schema].[js...Notify] @id;
```

Also verify:

```text
company id on document
company id on user/stage mapping
stage priority ordering
delegation date range
active user/stage status
```

## Approval Completed But SAP Did Not Trigger

Check module-specific SAP trigger:

| Module | What to check |
|---|---|
| IMC | `imc.jsGetItemCurrentStage.IsLastStage`, `imc.jsGetPendingItemApiInsertions`, `imc.jsUpdateItemApiStatus`, `imc.LogApiError`. |
| BOM | `bom.jsGetPendingApiInsertions`, `bom.jsUpdateBomApiStatus`, ProductTrees endpoint response. |
| Credit Limit | `cl.jsGetFlowStatus` must return `A`, then `UpdateCreditLimitAsync` must PATCH `BusinessPartners('{customerCode}')`, then `cl.updateHanaStatus`. |
| BP Master | Approval does not directly call SAP in `ApproveBPAsync`; check `BP.jsUpdateSAPData` path. |

IMC-specific rule: final DB approval is intentionally blocked if SAP item creation fails. If users say "approval button does nothing", check the API response body for `ApprovalStatus = Blocked` and the SAP error message.

## Duplicate Approvals or Duplicate SAP Creates

IMC has explicit C# guards:

```text
_approvalLocks keyed by request.itemId
_sapPostLocks keyed by PendingItemApiInsertionsModel.InitId
imc.jsUpdateItemApiStatus previousTag check
```

Other modules rely mostly on stored procedure validation. For duplicate approvals in those modules:

```text
inspect module approve SP for already-approved check
check current-stage row uniqueness
check whether two users can approve same stage in parallel
check transaction isolation in SP
check whether pending list still returns already-approved row
```

## Notifications Not Sent

Check:

1. Module approve service actually calls notify SP after approval.
2. Notify SP returns `userIdsToApprove`.
3. `NotificationService.GetUserFcmTokenAsync` returns tokens.
4. FCM service account path in `appsettings.json` under `Firebase:ServiceAccountPath`.
5. `nt.jsInsertNotification` inserts rows.
6. Deduplication did not remove all recipients because `userIdsToApprove` was blank.

Modules without a visible notification block in approval method, such as `BPmasterService.ApproveBPAsync` and `PaymentService.ApprovePaymentAsync`, may depend entirely on DB-side or frontend behavior for notifications.

## Approval Visible To Wrong User

Check:

```text
pending stored procedure userId filter
company filter
template-stage user mapping
delegation mapping
selectedCompanyId vs companyId session mismatch if CheckUserPermissionAttribute is enabled
frontend current company in _Layout.cshtml
```

## Approval API Returns Unauthorized

Check:

```text
JSAP.Auth cookie or Authorization: Bearer token
SmartAuth scheme in Program.cs
global AuthorizeFilter
auth.js refresh call to /api/Auth/refresh
session expiry: 8-hour idle timeout
JWT expiry/signing settings
```

## SQL Error From Approval

Typical path:

```text
Controller catches exception
  -> logs via ILogger
  -> returns 500 or BadRequest
Service catches SqlException
  -> returns "SQL Error: ..."
GlobalExceptionMiddleware
  -> catches unhandled exception
SensitiveErrorResponseFilter
  -> may replace 500 object response with generic message
```

For production, capture:

```text
route
request body
authenticated user id / submitted userId
company
flow id / document id
stored procedure name
SQL exception number and message
current stage rows
notification SP result
SAP response body if applicable
```

# Performance and Failure Analysis

## Heavy Query Areas

| Area | Risk |
|---|---|
| Pending/approved/rejected merge methods | Many services call three stored procedures and merge in memory. Slow SPs affect dashboard pages. |
| Pending reminder APIs | Iterate active users and query counts/tokens per user. These are request-time jobs and can become slow. |
| Approval-flow detail APIs | Depend on stage joins; missing indexes can make stuck-approval debugging endpoints slow. |
| Notification fan-out | Sends FCM per token synchronously inside approval request. |
| SAP/HANA calls | `HttpClient.SendAsync` to SAP Service Layer can block approval responses and fail due to network/session issues. |

## Transaction and Race Risks

| Risk | Modules |
|---|---|
| Two approvers submit same flow simultaneously | All modules, unless SP locks/validates current stage. IMC has extra C# semaphore. |
| SAP succeeds but SQL status update fails | IMC, BOM, Credit Limit. Check status update SP response/logs. |
| SQL approval succeeds but notification fails | Most modules; approval is not rolled back when notification fails. |
| Notification sent before user can see item | If notify SP returns next users before stage transaction is committed or if SP logic is inconsistent. |
| User changes company during approval | Any browser flow using selected company/session and body `company`. |
| In-memory lock only protects one app instance | IMC duplicate guards do not coordinate across multiple web servers; DB tag check is the cross-instance protection. |

## SAP Timeout Risks

| Module | Failure point |
|---|---|
| IMC | SAP `Items` POST, session acquisition, item duplicate, unsupported company, tag update. |
| BOM | SAP ProductTrees create/update and `bom.jsUpdateBomApiStatus`. |
| Credit Limit | SAP BusinessPartners PATCH and `cl.updateHanaStatus`. |

The SAP session provider is `Bom2Service`. It caches `B1SESSION` and `ROUTEID` for Oil/Bev/Mart and uses SAP Service Layer `Login`.

# Developer Checklist For New Approval Modules

1. Create a request insert SP that creates document data and approval flow rows.
2. Create pending/approved/rejected list SPs with `userId`, `company`, and `month` filters where needed.
3. Create approve/reject SPs that validate:
   - document exists
   - company matches
   - user is current-stage approver or valid delegate
   - stage is still pending
   - duplicate approval is blocked
4. Create approval-flow detail SP for debugging.
5. Create notify SP returning `userIdsToApprove`.
6. In service code, deduplicate users and FCM tokens before sending notifications.
7. If final approval triggers SAP, persist a pending/success/failure status and log raw SAP error payloads.
8. Never trust `userId` from body alone; cross-check with authenticated claims or enforce strongly in the approval SP.
