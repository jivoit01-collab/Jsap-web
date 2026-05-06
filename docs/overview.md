# Project Overview

JSAPNEW is an ASP.NET Core enterprise workflow application for JSAP business operations. It combines MVC Razor screens with JSON APIs for ERP-style modules such as budgets, BOM creation, item master creation, BP master creation, inventory audit, GIGO, payments, tickets, tasks, dashboards, document dispatch, hierarchy management, QC, PRDO, and notifications.

The application is built as a layered server-side web application:

```text
Browser / Razor views / JavaScript
        |
        v
MVC + API controllers
        |
        v
Authentication, authorization, filters, rate limiting
        |
        v
Service interfaces
        |
        v
Service implementations
        |
        v
SQL Server / SAP HANA / SAP Service Layer / SFTP / Firebase
```

Most business logic lives in `Services/Implementation`. Controllers mostly receive HTTP requests, perform request-level validation and response shaping, then delegate work to service interfaces registered in `Program.cs`.

## What This Project Solves

The system centralizes internal business workflows that would otherwise live in separate tools or manual approval chains:

| Area | Purpose |
|---|---|
| Budget and approval workflows | Create budgets, sub-budgets, monthly allocations, approval templates, and approval/rejection flows. |
| SAP item and BOM operations | Create, approve, update, and sync item master and BOM/product tree data with SAP. |
| Business partner workflows | Create and approve BP master data and related SAP updates. |
| Inventory and GIGO | Manage inventory sessions, stock counts, gate entries, GRPO/PO data, and warehouse-related flows. |
| Payments | Track advance requests, vendor expenses, invoice payments, payment checker activity, and approvals. |
| Tasks and tickets | Manage internal tasks, projects, support tickets, assignment, comments, progress, workload, and attachments. |
| Dashboards and reports | Provide role/company-specific dashboard data, reports, summaries, and approval status views. |
| Hierarchy | Manage employee, department, reporting, sales hierarchy, salary key access, and audit logs. |

# Tech Stack

| Layer | Actual implementation |
|---|---|
| Runtime | .NET 9.0, configured in `JSAPNEW.csproj` with `Microsoft.NET.Sdk.Web`. |
| Backend framework | ASP.NET Core MVC + API controllers. |
| Frontend | Razor views in `Views`, static JavaScript/CSS in `wwwroot`, Bootstrap, jQuery, Font Awesome. |
| API documentation | Swagger/OpenAPI via `Swashbuckle.AspNetCore`, enabled only in development. |
| Data access | Dapper, `SqlConnection`, stored procedures, direct SQL, SAP HANA provider. |
| Primary database | SQL Server through `DefaultConnection`, `AzureConnection`, and `FHConnection`. |
| SAP database/API | SAP HANA through `Sap.Data.Hana.Net.v8.0`; SAP Service Layer through HTTP calls. |
| Authentication | Smart policy scheme that chooses cookie auth or JWT bearer auth per request. |
| Session | ASP.NET Core session with an 8-hour idle timeout. |
| Password security | BCrypt for new passwords in `PasswordHasher.cs`; legacy AES-style encryption migration helper in `Encryption.cs`. |
| Authorization | Global authenticated fallback policy, role policies, session/company permissions, and dynamic menu permissions. |
| Rate limiting | ASP.NET Core fixed-window rate limiting in `Program.cs`. |
| File transfer | Local `wwwroot/Uploads` storage plus SFTP downloads through SSH.NET/Renci.SshNet. |
| Notifications | `NotificationService`, Firebase configuration, notification/token APIs. |
| Logging | Console and debug providers configured in `Program.cs`; global exception logging in `GlobalExceptionMiddleware.cs`. |

# Folder Structure Overview

## `/Controllers`

Contains API controllers and MVC controllers.

API controllers are usually decorated with `[Route("api/[controller]")]` and inherit from `ControllerBase`. MVC/page controllers inherit from `Controller` and return Razor views.

Important API controllers:

| Controller | Main purpose |
|---|---|
| `AuthController.cs` | Login, refresh, logout, current user, companies, and budget report endpoints. |
| `Auth2Controller.cs` | Budget templates, budget creation, monthly allocation, and budget allocation approvals. |
| `PermissionController.cs` | Groups, modules, permissions, effective permissions, and permission checks. |
| `BomController.cs` / `Bom2Controller.cs` | BOM creation, components, approvals, files, SAP product tree sync, and alternate SAP/session flows. |
| `ItemMasterController.cs` | Item master dropdowns, item workflows, approval/rejection, SAP insertion, and back-date document workflows. |
| `BPmasterController.cs` | Business partner creation, approval/rejection, BP metadata, SAP updates, and BP insights. |
| `InventoryAuditController.cs` | Stock count sessions, item/warehouse/location filters, count entry, session status, and reports. |
| `GIGOController.cs` | Gate entry, document attachments, vehicle details, vendor/customer/BST documents, and PO lookup. |
| `AdvanceRequestController.cs` | Vendor expenses, advance payment requests, approval/rejection, PO/vendor lookups, and notifications. |
| `PaymentController.cs` | Pending/approved/rejected/all payment lists, payment details, approval/rejection, and insights. |
| `CreditLimitController.cs` | Credit-limit documents, approvals, HANA status updates, and notification helpers. |
| `QcController.cs` | QC form definitions, production data, document approvals, and QC notifications. |
| `PrdoController.cs` | Production order approval flows and production order insights. |
| `TaskController.cs` | Task create/filter/delete/complete/dashboard/report/progress/reassign APIs. |
| `TicketsController.cs` | Project tickets, assignment, lifecycle, comments, workload, insights, and attachments. |
| `TicketController.cs` | Legacy or alternate ticket APIs with catalogs, comments, status, and insights. |
| `DashboardController.cs` | Dashboard master data, task/project metrics, MoM points, and budget dashboard data. |
| `HierarchyController.cs` | Employee hierarchy, departments, reporting, salary key access, audit logs, and sales hierarchy. |
| `DocumentDispatchController.cs` | GRPO/PO/document dispatch, attachments, HANA save/update operations. |
| `FileController.cs` | Download APIs for SFTP and local upload paths. |
| `NotificationController.cs` | Notifications, device tokens, Firebase tokens, unread counts, and cleanup. |
| `ReportsController.cs` | Realise report, variety/brand lists, approval status report, budget by company. |

Important MVC/web controllers:

| Controller | Views |
|---|---|
| `LoginController.cs` | `Views/Login/Index.cshtml`. |
| `DashboardWebController.cs` | Dashboard pages under `Views/DashboardWeb`. |
| `UserManagementController.cs` | Admin-only user, permission, and document screens under `Views/UserManagement`. |
| `TaskWebController.cs` | Task dashboard and task pages under `Views/TaskWeb`. |
| `TicketsWebController.cs` | Ticket UI pages under `Views/TicketsWeb`. |
| `InventoryAuditWebController.cs` | Inventory audit screens under `Views/InventoryAuditWeb`. |
| `HierarchyWebController.cs` | Hierarchy screens and Excel import flows under `Views/HierarchyWeb`. |
| `BPmasterwebController.cs`, `GIGOwebController.cs`, `QcWebController.cs`, `ReportsWebController.cs` | Module-specific Razor screens. |

## `/Services`

Contains service contracts and implementations.

`Services/Interfaces` defines contracts such as `IUserService`, `IBomService`, `IItemMasterService`, `IPermissionService`, `IHierarchyService`, `ITicketsService`, and `ITaskService`.

`Services/Implementation` contains the actual business and integration logic. Most service methods use Dapper with stored procedures or direct SQL, for example:

```text
Controller -> ITaskService -> TaskService -> SQL stored procedure
Controller -> IBomService -> BomService -> SQL Server + SAP Service Layer
Controller -> IHierarchyService -> HierarchyService -> Hie schema tables/procedures
Controller -> IPermissionService -> PermissionService -> permission/group stored procedures
```

All service registrations are centralized in `Program.cs`, using `AddScoped` for request-scoped services and `AddSingleton` for long-lived services such as `NotificationService`, `SshService`, and `DistributedTokenRevocationService`.

## `/Models`

Contains DTOs, request models, response models, and view models. This project does not use Entity Framework model mapping for most business data; models generally represent API payloads and Dapper result shapes.

Important model files:

| File | Purpose |
|---|---|
| `AuthModels.cs`, `Auth2Models.cs` | Login/user/budget/template/allocation DTOs. |
| `PermissionModels.cs` | User group, module, permission, and effective permission DTOs. |
| `BomModels.cs` | BOM headers, components, SAP product tree payloads, approval flow, file models. |
| `ItemMasterModel.cs` | Item creation, SAP item payloads, approval flow, and back-date document DTOs. |
| `BPmasterModels.cs` | BP master payloads, approval data, SAP metadata. |
| `InventoryAuditModels.cs` | Stock count session, warehouse, item, count entry, and report models. |
| `TaskModels.cs` | Task creation, filters, dashboard, progress, report, and reassignment DTOs. |
| `TicketsModels.cs`, `TicketModels.cs` | Ticket/project/comment/attachment/insight DTOs. |
| `HierarchyModels.cs` | Employee, department, reporting relationship, salary, sales hierarchy, and audit DTOs. |
| `NotificationModels.cs` | Notification and token payloads. |
| `SecuritySettings.cs` | Security settings model. |

## `/Data`

Currently contains `Data/Entities/User.cs`, a simple user entity with fields such as `UserId`, `UserName`, `LoginUser`, `Password`, `FullName`, `Role`, `CreatedDate`, and `IsActive`.

Most database mapping is handled through service-level SQL/Dapper models rather than a full ORM entity layer.

## `/Filters`

Contains reusable MVC/action filters:

| Filter | Purpose |
|---|---|
| `AuthenticatedUserBindingFilter.cs` | Binds authenticated user identity into action arguments/properties named `userId`, `createdBy`, or `updatedBy` for non-admin users. |
| `SensitiveErrorResponseFilter.cs` | Replaces 500-level object responses with `{ success: false, message: "Something went wrong" }`. |
| `CheckUserPermissionAttribute.cs` | Checks a user's module permission through `IPermissionService`. |
| `SessionAuthFilter.cs` | Redirects missing MVC sessions to `/Login` or returns 401 for AJAX requests. |
| `AuthorizeAllFilter.cs` | Simple authorization filter that blocks unauthenticated users. |

## `/Middlewares`

Contains `GlobalExceptionMiddleware.cs`, which catches unhandled exceptions, logs them, and returns JSON errors for common exception categories.

## `/Views`

Contains Razor views for the server-rendered UI. The common shell is `Views/Shared/_Layout.cshtml`.

The layout reads session data such as `companyList`, `selectedCompanyId`, `userId`, `loginUser`, and `userEmail`. It builds the sidebar dynamically by calling:

```text
GET /api/Permission/GetUserEffectivePermissions?UserId={userId}&CompanyId={companyId}
```

It also handles company switching through:

```text
POST /websession/updateSelectedCompany
```

## `/wwwroot`

Contains static frontend assets:

| Path | Purpose |
|---|---|
| `wwwroot/js/auth.js` | Fetch/jQuery authentication refresh wrapper, 401 retry handling, and global `window.APP` helpers. |
| `wwwroot/js/*.js` | Module-specific JavaScript for dashboards, task management, hierarchy popups, budget, BP creation, and site behavior. |
| `wwwroot/css/*.css` | Module and site styles. |
| `wwwroot/lib` | Bootstrap, jQuery, jQuery validation, and unobtrusive validation libraries. |
| `wwwroot/images` | Logos and images. |
| `wwwroot/uploads` / `wwwroot/Uploads` | Upload roots referenced by the project for BOM, BP master, maker, ticket, and advance payment files. |

## Root Configuration Files

| File | Purpose |
|---|---|
| `Program.cs` | Application startup, dependency injection, authentication, authorization, rate limiting, CORS, middleware, routes, Swagger, headers. |
| `appsettings.json` | Connection strings, SAP/HANA settings, JWT settings, CORS, logging, email, cache, upload and Firebase configuration. |
| `appsettings.Development.json` | Development-specific settings. |
| `JSAPNEW.csproj` | Target framework and NuGet dependencies. |
| `bundleconfig.json` | Client bundling/minification configuration. |
| `docs/SAP_ITEM_CREATION_DOCUMENTATION.md` | Existing focused documentation for SAP item creation. |

# Architecture Overview

## Request/Response Lifecycle

```text
Browser or API client
  -> ASP.NET Core routing
  -> Global rate limiter
  -> CORS policy
  -> Session middleware
  -> SmartAuth authentication
  -> Authorization policy/filter
  -> AuthenticatedUserBindingFilter
  -> Controller action
  -> Service interface
  -> Service implementation
  -> SQL Server / SAP HANA / SAP Service Layer / file store
  -> Controller response
  -> SensitiveErrorResponseFilter / GlobalExceptionMiddleware if needed
```

For MVC pages, the response is usually a Razor view. For API controllers, responses are JSON objects with patterns such as:

```json
{
  "success": true,
  "data": []
}
```

or:

```json
{
  "success": false,
  "message": "Something went wrong"
}
```

## Layer Responsibilities

| Layer | Responsibility |
|---|---|
| Controllers | HTTP route binding, request validation, session reads, response formatting. |
| Filters | Cross-cutting authorization, identity binding, and safe error response shaping. |
| Services | Business rules, workflow orchestration, DB/SAP/file/notification integrations. |
| Models | Request/response DTOs and Dapper result shapes. |
| Views/static assets | Server-rendered UI, dynamic menu, company switching, AJAX calls, auth retry. |
| Configuration | Connection strings, JWT settings, CORS origins, HANA/SAP settings, upload and notification settings. |

# Module Explanation

## Authentication and Session Module

Purpose: authenticate users for both Razor UI and API access.

Main files:

| File | Role |
|---|---|
| `Controllers/AuthController.cs` | Login, refresh, logout, current user, company list. |
| `Controllers/LoginController.cs` | Public login page. |
| `Services/Implementation/UserService.cs` | User lookup, credential validation, user/company data, user administration. |
| `Services/Implementation/TokenService.cs` | JWT generation and validation. |
| `Services/Implementation/AuthSecurityService.cs` | Data-protected stateless refresh tokens. |
| `Services/Implementation/PasswordHasher.cs` | BCrypt hashing and verification. |
| `wwwroot/js/auth.js` | Browser-side refresh and retry behavior. |

Workflow:

1. User opens `/Login`.
2. Browser posts credentials to `POST /api/Auth/login`.
3. `AuthController` calls `IUserService.ValidateUserAsync`.
4. On success, the server signs in a cookie (`JSAP.Auth`), stores session values, issues a protected refresh token cookie (`JSAP.Refresh`), and returns a JWT access token.
5. Razor views use cookie/session auth. API clients can use `Authorization: Bearer <token>`.
6. `wwwroot/js/auth.js` retries failed authenticated calls by calling `POST /api/Auth/refresh`.
7. Logout calls `POST /api/Auth/logout`, clears the auth cookie, refresh cookie, and server session.

Important routes:

```text
POST /api/Auth/login
POST /api/Auth/refresh
POST /api/Auth/logout
GET  /api/Auth/me
GET  /api/Auth/getcompanies
POST /websession/set
POST /websession/updateSelectedCompany
GET  /websession/checksession
```

## User Management and Permissions Module

Purpose: manage users, groups, modules, permissions, company selection, and dynamic menu access.

Main files:

| File | Role |
|---|---|
| `Controllers/UserManagementController.cs` | Admin-only Razor user management pages. |
| `Controllers/PermissionController.cs` | API endpoints for groups, modules, permissions, and permission checks. |
| `Services/Implementation/UserService.cs` | User registration, update, password, company, roles, budget/user related queries. |
| `Services/Implementation/PermissionService.cs` | Group membership and effective permission logic. |
| `Models/PermissionModels.cs` | Permission DTOs. |
| `Views/Shared/_Layout.cshtml` | Dynamic sidebar generation from permissions. |

Workflow:

```text
Authenticated user
  -> Session contains userId and selectedCompanyId
  -> Layout calls GetUserEffectivePermissions
  -> API returns moduleName + permissionType rows
  -> Sidebar renders allowed modules/pages
  -> Company dropdown updates selectedCompanyId
  -> Permissions reload for selected company
```

Important routes:

```text
GET  /api/Permission/GetUserEffectivePermissions
POST /api/Permission/CheckUserPermission
POST /api/Permission/AddUserGroup
POST /api/Permission/AddPermissionToGroup
GET  /api/Permission/GetAllGroups
GET  /api/Permission/GetAllModules
GET  /api/Permission/GetAllPermissions
```

## Budget Module

Purpose: handle budget setup, sub-budget structure, monthly allocation, budget approval reports, and approval/rejection workflows.

Main files:

| File | Role |
|---|---|
| `Controllers/AuthController.cs` | Budget report endpoints such as budget insight and pending/approved/rejected details. |
| `Controllers/Auth2Controller.cs` | Budget template, budget, sub-budget, and monthly allocation APIs. |
| `Services/Implementation/UserService.cs` | Existing budget approval/report methods. |
| `Services/Implementation/Auth2Service.cs` | Budget template/allocation workflows. |
| `Models/AuthModels.cs`, `Models/Auth2Models.cs` | Budget DTOs. |

Typical flow:

```text
Budget API request
  -> Auth2Controller or AuthController
  -> IAuth2Service or IUserService
  -> SQL Server stored procedures, mostly in dbo/bud schemas
  -> Approval status and detail DTOs
  -> JSON response
```

Important routes include:

```text
POST /api/Auth2/CreateBudgetWithSubBudgets
POST /api/Auth2/CreateMonthlyAllocations
GET  /api/Auth2/GetAllBudgets
GET  /api/Auth2/GetBudgetWithSubBudgets
POST /api/Auth2/approveBudgetAllocation
POST /api/Auth2/rejectBudgetAllocation
GET  /api/Auth/GetAllBudgetInsight
GET  /api/Auth/GetBudgetApprovalFlow
```

## BOM Module

Purpose: create and approve bill of materials, maintain components/files, and sync approved product trees to SAP.

Main files:

| File | Role |
|---|---|
| `Controllers/BomController.cs` | Main BOM workflow APIs. |
| `Controllers/Bom2Controller.cs` | SAP session and alternate BOM V2 endpoints. |
| `Services/Implementation/BomService.cs` | BOM stored procedure logic, approval flow, file lookup, SAP product tree calls. |
| `Services/Implementation/Bom2Service.cs` | SAP session handling and V2 BOM operations. |
| `Models/BomModels.cs` | BOM, component, file, approval, and SAP product tree DTOs. |

Workflow:

```text
Create BOM
  -> /api/Bom/createbom or /api/Bom/CreateBomWithComponents
  -> BomService persists header/components/files
  -> Approval stages progress through approve/reject endpoints
  -> Pending SAP insertions are fetched
  -> ProductTrees endpoint sends SAP Service Layer payload
  -> API status is updated in SQL Server
```

Important routes:

```text
POST /api/Bom/createbom
POST /api/Bom/CreateBomWithComponents
POST /api/Bom/approvebom
POST /api/Bom/rejectbom
GET  /api/Bom/GetPendingInsertions
POST /api/Bom/ProductTrees
POST /api/Bom/UpdateProductTrees
GET  /api/Bom/GetBomApprovalFlow
GET  /api/Bom/getBomFiles
GET  /api/Bom2/SapOilSession
GET  /api/Bom2/SapBevSession
GET  /api/Bom2/SapMartSession
```

## Item Master Module

Purpose: create item master data, collect dropdown/reference values, run approval workflows, and send item data into SAP/HANA.

Main files:

| File | Role |
|---|---|
| `Controllers/ItemMasterController.cs` | Item master APIs, SAP insertion, back-date document APIs. |
| `Services/Implementation/ItemMasterService.cs` | Main business and SAP integration logic. |
| `Models/ItemMasterModel.cs` | Item, SAP, workflow, and back-date document DTOs. |
| `docs/SAP_ITEM_CREATION_DOCUMENTATION.md` | Existing deeper item creation documentation. |

Important routes:

```text
POST /api/ItemMaster/InsertFullItem
POST /api/ItemMaster/ApproveItem
POST /api/ItemMaster/RejectItem
POST /api/ItemMaster/Items
GET  /api/ItemMaster/GetPendingItemApiInsertions
GET  /api/ItemMaster/GetIMCApprovalFlow
POST /api/ItemMaster/CreateDocument
POST /api/ItemMaster/BackDateSaveInHana
```

## BP Master Module

Purpose: manage SAP business partner creation and approval flows.

Main files:

| File | Role |
|---|---|
| `Controllers/BPmasterController.cs` | BP insert, lookup, approval/rejection, insight, and SAP update APIs. |
| `Controllers/BPmasterwebController.cs` | Razor entry pages. |
| `Services/Implementation/BPmasterService.cs` | BP data access and workflow implementation. |
| `Models/BPmasterModels.cs` | BP request/response DTOs. |

Important routes:

```text
POST /api/BPmaster/InsertBPmasterData
POST /api/BPmaster/ApproveBP
POST /api/BPmaster/RejectBP
POST /api/BPmaster/UpdateBPMaster
POST /api/BPmaster/UpdateSapData
GET  /api/BPmaster/GetBPApprovalFlow
GET  /api/BPmaster/GetBPInsights
```

## Inventory Audit and GIGO Modules

Purpose: manage inventory stock counting and gate/document movement workflows.

Main files:

| File | Role |
|---|---|
| `Controllers/InventoryAuditController.cs` | Stock count sessions, filters, reports, user assignment, count updates. |
| `Controllers/InventoryAuditWebController.cs` | Inventory audit Razor pages. |
| `Services/Implementation/InventoryAuditService.cs` | Inventory audit SQL/HANA queries and stored procedures. |
| `Models/InventoryAuditModels.cs` | Stock count, session, item, warehouse, report DTOs. |
| `Controllers/GIGOController.cs` | Gate entry and document APIs. |
| `Services/Implementation/GIGOService.cs` | GIGO business/data access logic. |
| `Models/GIGOModels.cs` | GIGO DTOs. |

Inventory workflow:

```text
Create session
  -> Assign users
  -> Load warehouses/items/groups/locations
  -> Insert stock count headers/items
  -> Users update physical counts
  -> Reports compare system count, last count, physical count, differences
```

GIGO workflow:

```text
Gate entry request
  -> Insert gate entry/document/vehicle/attachment data
  -> Lookup PO/document data as needed
  -> Persist supporting documents
```

## Payment, Advance Request, Invoice, and Checker Modules

Purpose: handle vendor expense requests, advance payment approvals, payment records, checker activity, and invoice payment views.

Main files:

| File | Role |
|---|---|
| `Controllers/AdvanceRequestController.cs` | Vendor expense, advance request, approval/rejection, PO/vendor lookup APIs. |
| `Services/Implementation/AdvanceRequestService.cs` | Advance request SQL and SAP Service Layer integration. |
| `Controllers/PaymentController.cs` | Payment list/detail/approval APIs. |
| `Services/Implementation/PaymentService.cs` | Payment data access. |
| `Controllers/MakerController.cs`, `Controllers/CheckerController.cs`, `Controllers/PaymentCheckerController.cs` | Maker/checker/admin payment pages and activity. |
| `Controllers/InvoicePaymentController.cs` | Invoice payment Razor page. |
| `Services/Implementation/MakerService.cs`, `CheckerService.cs`, `PaymentCheckerService.cs`, `InvoicePaymentService.cs` | Payment-related service logic. |

Important routes:

```text
POST /api/AdvanceRequest/InsertVendorExpense
POST /api/AdvanceRequest/ApproveAdvPay
POST /api/AdvanceRequest/RejectedAdvPay
GET  /api/AdvanceRequest/GetExpenseApprovalFlow
GET  /api/Payment/getpendingpayments
GET  /api/Payment/getapprovedpayments
POST /api/Payment/approvepayment
POST /api/Payment/rejectpayment
```

## Task and Ticket Modules

Purpose: manage internal tasks, project tickets, assignments, comments, progress, lifecycle, workload, and attachments.

Main files:

| File | Role |
|---|---|
| `Controllers/TaskController.cs` | Task API. |
| `Controllers/TaskWebController.cs` | Task dashboard and task Razor pages. |
| `Services/Implementation/TaskService.cs` | Stored procedure backed task logic. |
| `Models/TaskModels.cs` | Task DTOs. |
| `Controllers/TicketsController.cs` | Newer project/ticket/comment/attachment APIs. |
| `Controllers/TicketController.cs` | Legacy or alternate ticket APIs. |
| `Controllers/TicketsWebController.cs` | Ticket Razor pages. |
| `Services/Implementation/TicketsService.cs`, `TicketService.cs` | Ticket business logic. |
| `Models/TicketsModels.cs`, `TicketModels.cs` | Ticket DTOs. |

Task flow:

```text
CreateTask
  -> TaskController
  -> TaskService
  -> SQL stored procedure
  -> task dashboard/report/progress APIs read resulting data
```

Ticket flow:

```text
CreateProject/CreateTicket
  -> assign/reassign/start/hold/resume/close
  -> comments and timeline added
  -> attachments uploaded/downloaded
  -> insights/workload endpoints summarize state
```

Important routes:

```text
POST /api/Task/CreateTask
POST /api/Task/GetAllTasks
POST /api/Task/CompleteTask
POST /api/Task/AddProgressUpdate
GET  /api/Task/GetTaskDetails/{taskId}
POST /api/Tickets/CreateProject
POST /api/Tickets/CreateTicket
POST /api/Tickets/AssignTicket
POST /api/Tickets/CloseTicket
POST /api/Tickets/UploadAttachment
GET  /api/Tickets/DownloadAttachment/{attachmentId}
```

## Dashboard and Reports Modules

Purpose: provide UI and API data for dashboards, MoM, task/project metrics, approval status, budget data, and reporting.

Main files:

| File | Role |
|---|---|
| `Controllers/DashboardController.cs` | Dashboard data APIs. |
| `Controllers/DashboardWebController.cs` | Razor dashboard pages. |
| `Services/Implementation/DashboardService.cs` | Dashboard data access. |
| `Models/DashboardModels.cs` | Dashboard DTOs. |
| `Controllers/ReportsController.cs` | Report APIs. |
| `Services/Implementation/ReportsService.cs` | Report service logic. |
| `Views/DashboardWeb/*`, `Views/ReportsWeb/*` | UI pages. |

## Hierarchy Module

Purpose: manage employee master records, reporting relationships, HOD/sub-HOD flows, departments/subdepartments, salary visibility, sales hierarchy, custom fields, and audit logs.

Main files:

| File | Role |
|---|---|
| `Controllers/HierarchyController.cs` | API surface for hierarchy operations. |
| `Controllers/HierarchyWebController.cs` | Razor pages and Excel import workflows. |
| `Services/Implementation/HierarchyService.cs` | Large service covering employee hierarchy, salary access, department, audit, custom field, and sales hierarchy logic. |
| `Models/HierarchyModels.cs` | Hierarchy DTOs. |

Database areas used by this service include `Hie.Employees`, `Hie.EmployeeReportingRelationships`, `Hie.SalesHierarchy`, `Hie.Departments`, `Hie.SubDepartments`, `Hie.SalaryConfig`, `Hie.SalaryKeyManager`, and related stored procedures such as `Hie.sp_GetAuditLogs`.

## Document Dispatch Module

Purpose: track document dispatch/receipt, GRPO/PO/AP draft/GR data, attachments, and HANA save/update flows.

Main files:

| File | Role |
|---|---|
| `Controllers/DocumentDispatchController.cs` | Document dispatch APIs. |
| `Services/Implementation/DocumentDispatchService.cs` | SQL/HANA document dispatch logic. |
| `Models/DocumentDispatchModels.cs` | Dispatch DTOs. |
| `Views/UserManagement/DocumentDispatch.cshtml`, `DocumentRecieve.cshtml`, `RejectDocument.cshtml` | UI screens. |

## Notification Module

Purpose: store user/device tokens, create notifications, count unread notifications, mark notifications read, and send workflow notifications from modules.

Main files:

| File | Role |
|---|---|
| `Controllers/NotificationController.cs` | Notification and token APIs. |
| `Services/Implementation/NotificationService.cs` | Notification business logic and Firebase-related operations. |
| `Models/NotificationModels.cs` | Notification DTOs. |
| `appsettings.json` | Firebase service account path. |

Important routes:

```text
GET  /api/Notification/GetUnreadNotificationCount
GET  /api/Notification/GetUserNotifications
POST /api/Notification/InsertNotification
POST /api/Notification/MarkNotificationAsRead
POST /api/Notification/SaveUserToken
POST /api/Notification/InsertDeviceInfo
```

# Security Architecture

## Authentication Scheme

`Program.cs` configures a policy scheme named `SmartAuth`.

```text
If Authorization header starts with "Bearer "
  -> Use JWT bearer authentication
Else
  -> Use cookie authentication
```

This supports both:

| Client type | Auth mechanism |
|---|---|
| Razor UI | `JSAP.Auth` HTTP-only cookie + session. |
| API client/mobile/external integration | JWT bearer token. |
| Browser AJAX | Cookie-based calls with refresh handled by `wwwroot/js/auth.js`. |

## Login and Token Flow

```text
POST /api/Auth/login
  -> IUserService.ValidateUserAsync
  -> BCrypt password validation/migration behavior in UserService/PasswordHasher
  -> Cookie sign-in with claims
  -> Session values written
  -> Data-protected refresh token written to JSAP.Refresh
  -> JWT generated by TokenService
```

JWT claims include:

```text
ClaimTypes.NameIdentifier
userId
ClaimTypes.Name
ClaimTypes.Email
JwtRegisteredClaimNames.Jti
JwtRegisteredClaimNames.Iat
ClaimTypes.Role
role
```

`Program.cs` validates issuer, audience, signing key, lifetime, role claim, and required identity claims. Clock skew is zero.

## Refresh Token Handling

`AuthSecurityService.cs` creates stateless refresh tokens protected by ASP.NET Core Data Protection. The protected payload includes:

```text
UserId
Role
ExpiresUtc
CreatedUtc
Nonce
IpAddress
UserAgentHash
```

The refresh cookie is:

```text
Name: JSAP.Refresh
Path: /api/Auth
HttpOnly: true
SameSite: Strict
Secure: true outside development
Lifetime: 7 days
```

## Session Handling

Session is enabled in `Program.cs`:

```text
IdleTimeout: 8 hours
HttpOnly: true
SameSite: Strict
Secure: Always outside development
```

Common session keys:

```text
userId
username
userName
loginUser
userEmail
companyList
selectedCompanyId
```

Note: `CheckUserPermissionAttribute.cs` reads `companyId`, while the active web session code mostly writes `selectedCompanyId`. If this filter is re-enabled on more actions, verify that the session key names are aligned.

## Authorization and RBAC

The app applies a global authenticated policy to controllers and MVC:

```csharp
options.Filters.Add(new AuthorizeFilter(authenticatedPolicy));
```

Configured policies:

| Policy | Roles |
|---|---|
| `AdminOnly` | `Admin`, `Super User` |
| `SuperAdminOnly` | `SuperAdmin`, `Super User` |

Route-level examples:

| File | Protection |
|---|---|
| `AuthController.cs` | Controller is `[Authorize]`; login, refresh, logout are `[AllowAnonymous]`. |
| `UserManagementController.cs` | `[Authorize(Policy = "AdminOnly")]`. |
| `FileController.cs` | `[Authorize]` plus `FileTransfer` rate limiter. Debug endpoints require `AdminOnly`. |
| Most API controllers | `[Authorize]`. |
| `LoginController.cs` | `[AllowAnonymous]`. |
| `/health` | `AllowAnonymous`. |

Fine-grained permissions use `PermissionController`, `PermissionService`, and the dynamic menu in `_Layout.cshtml`. `CheckUserPermissionAttribute` can enforce module-level permission checks when applied to actions.

## API Validation

Validation is a mix of:

| Type | Where implemented |
|---|---|
| ASP.NET model validation | Example: `AuthController.Login` checks `ModelState.IsValid`. |
| Manual null/ID checks | Many controllers validate IDs, company IDs, and request DTO presence. |
| Stored procedure validation | Much of the business validation appears to be enforced in SQL stored procedures. |
| DTO data annotations | Present in selected model files, especially request/login/change password related DTOs. |

## SQL Injection Protection

Most service code uses Dapper parameters, `SqlCommand` parameters, or stored procedures. This is the primary SQL injection defense.

Examples:

```text
connection.QueryAsync<T>("EXEC [bud].[proc] @userId, @company", new { userId, company })
connection.ExecuteAsync("dbo.procName", parameters, commandType: CommandType.StoredProcedure)
```

Some services also contain direct SQL. New work should continue using parameterized queries and avoid string-concatenated SQL.

## XSS, Clickjacking, MIME, CSP, and Browser Headers

`Program.cs` adds security headers:

```text
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Content-Security-Policy: default-src 'self'; ...
Referrer-Policy: no-referrer
X-XSS-Protection: 0
Permissions-Policy: camera=(), microphone=(), geolocation=()
```

The CSP allows self plus specific script/style/font/image CDNs used by the Razor layout and static pages.

## CORS

`Program.cs` builds the CORS allow-list from:

```text
JSAP_CORS_ORIGINS environment variable
Cors:AllowedOrigins configuration
App:Url configuration
Development additions: http://localhost:3000, http://localhost:5173
```

The policy uses:

```text
WithOrigins(corsOrigins)
AllowAnyMethod
AllowAnyHeader
AllowCredentials
```

## CSRF

The app uses strict SameSite cookies, but API endpoints generally do not show ASP.NET antiforgery token enforcement. Because cookie-authenticated POST APIs are used by browser JavaScript, high-risk state-changing endpoints should be reviewed for CSRF strategy if exposed cross-site.

## Rate Limiting

`Program.cs` configures a global fixed-window limiter plus named policies:

| Limiter | Limit |
|---|---|
| Global GET/default | 120 requests/minute per user/IP. |
| Mutations | 40 requests/minute for POST/PUT/PATCH/DELETE. |
| Payment API | 30 requests/minute. |
| File transfer | 20 requests/minute. |
| Login | 12 requests/minute. |

`AuthController.Login` uses `EnableRateLimiting("Login")`, and `FileController` uses `EnableRateLimiting("FileTransfer")`.

## File Upload and Download Security

File handling appears in several modules:

| Area | Files |
|---|---|
| General downloads | `FileController.cs`. |
| Ticket attachments | `TicketsController.cs`, `TicketsService.cs`, `wwwroot/uploads/Ticket`. |
| BOM files | `BomController.cs`, `BomService.cs`, `wwwroot/uploads/BOM`. |
| BP master files | BP master upload folders. |
| Advance payment files | `AdvanceRequestController.cs`, `AdvanceRequestService.cs`, `wwwroot/Uploads/Advancepayment`. |

Implemented protections:

| Protection | Location |
|---|---|
| File transfer rate limiting | `FileController.cs`. |
| Local path normalization and traversal checks for advance download | `FileController.AdvanceDownloadFile`. |
| Admin-only debug file endpoints | `FileController.cs`. |
| MIME mapping for common document/image extensions | `FileController.GetMimeType`. |

Recommended future hardening:

```text
Centralize upload extension allow-list
Centralize max upload size checks
Store generated server filenames separately from original names
Avoid returning debug path details from production endpoints
Scan or validate uploaded content where required
```

## Secret Management

`appsettings.json` currently contains connection strings, SAP credentials, JWT secret, SMTP password placeholder, and service URLs. For production, these should be moved to environment variables, a secret manager, or deployment-specific protected configuration.

Do not commit real credentials in source control. The project already supports several environment overrides such as:

```text
JSAP_CORS_ORIGINS
JSAP_SFTP_HOST
JSAP_SFTP_USERNAME
JSAP_SFTP_PASSWORD
JSAP_SFTP_PORT
```

# Workflow Documentation

## Login Workflow

```text
User enters login credentials
  -> POST /api/Auth/login
  -> AuthController validates model
  -> UserService validates user/password
  -> AuthController writes JSAP.Auth cookie
  -> AuthController stores user/company data in session
  -> AuthSecurityService creates JSAP.Refresh cookie
  -> TokenService creates JWT
  -> Browser navigates to DashboardWeb
  -> _Layout.cshtml loads dynamic menu from permissions API
```

## Authenticated API Request Lifecycle

```text
Browser AJAX/fetch
  -> auth.js sends credentials: include
  -> SmartAuth chooses cookie auth
  -> Authorization filter checks authenticated user
  -> AuthenticatedUserBindingFilter binds user identity into request DTOs
  -> Controller calls service
  -> Service queries database/SAP/file system
  -> JSON response returned
  -> If 401, auth.js calls /api/Auth/refresh and retries once
```

## Bearer Token API Request Lifecycle

```text
External/API client
  -> Authorization: Bearer <JWT>
  -> SmartAuth chooses JWT bearer
  -> JWT issuer/audience/signature/lifetime/claims validated
  -> Token revocation service checked
  -> Controller executes
```

Note: `DistributedTokenRevocationService` is registered but currently returns `false` for every `IsRevoked` call. The implemented in-memory revocation service exists in `InMemoryTokenRevocationService.cs` but is not the active registration.

## Company Selection Workflow

```text
Login loads user's companies
  -> Session stores companyList and selectedCompanyId
  -> _Layout.cshtml renders company dropdown
  -> User changes company
  -> POST /websession/updateSelectedCompany
  -> Layout reloads effective permissions
  -> Current page is allowed or permission denied overlay is shown
```

## API Request Lifecycle

```text
Frontend -> Route -> Middleware -> Filter -> Controller -> Service -> Database/SAP -> Response
```

Concrete example:

```text
POST /api/Task/CreateTask
  -> TaskController.CreateTask
  -> AuthenticatedUserBindingFilter may bind user fields
  -> ITaskService.CreateTaskAsync
  -> TaskService executes stored procedure
  -> Controller returns success/error JSON
```

## Approval Workflow Pattern

Many modules follow the same approval pattern:

```text
Create document/request
  -> Persist header/details/files
  -> Create or attach approval flow
  -> Show pending request to current approver
  -> Approver calls approve/reject endpoint
  -> Service updates stage/status
  -> Notifications are sent to next user/creator
  -> Final approval triggers SAP/HANA/API status update where applicable
```

Modules using this pattern include budget, BOM, item master, BP master, advance request/payment, credit limit, PRDO, QC, and back-date documents.

# Connection Mapping

## Controller to Service Mapping

| Controller | Service |
|---|---|
| `AuthController` | `IUserService`, `IAuthSecurityService`, `ITokenService` |
| `Auth2Controller` | `IAuth2Service` |
| `PermissionController` | `IPermissionService` |
| `BomController` | `IBomService`, notification-related services through `BomService` |
| `Bom2Controller` | `IBom2Service` |
| `ItemMasterController` | `IItemMasterService` |
| `BPmasterController` | `IBPmasterService` |
| `InventoryAuditController` | `IInventoryAuditService` |
| `GIGOController` | `IGIGOService` |
| `AdvanceRequestController` | `IAdvanceRequestService`, `IBom2Service` |
| `PaymentController` | `IPaymentService` |
| `CreditLimitController` | `ICreditLimitService` |
| `QcController` | `IQcService` |
| `PrdoController` | `IPrdoService` |
| `TaskController` | `ITaskService`, `IHierarchyService`, configuration for user lookup. |
| `TicketsController` | `ITicketsService`, `INotificationService` |
| `TicketController` | `ITicketService` |
| `DashboardController` | `IDashboardService` |
| `ReportsController` | `IReportsService` |
| `HierarchyController` | `IHierarchyService` |
| `DocumentDispatchController` | `IDocumentDispatchService` |
| `NotificationController` | `INotificationService` |
| `AdminController` | `IPaymentCheckerService` plus direct SQL against `FHConnection`. |

## Frontend to API Mapping

| Frontend file/view | API usage |
|---|---|
| `Views/Login/Index.cshtml` | Login page calls auth APIs. |
| `Views/Shared/_Layout.cshtml` | Calls `/api/Permission/GetUserEffectivePermissions`, `/websession/updateSelectedCompany`, `/api/auth/me`, `/api/auth/getcompanies`, `/websession/set`, `/api/Auth/Logout`. |
| `wwwroot/js/auth.js` | Calls `/api/Auth/refresh`; wraps fetch and jQuery AJAX. |
| `Views/TaskWeb/*`, `wwwroot/js/task-management.js`, `wwwroot/js/task-dashboard.js` | Task APIs under `/api/Task` and hierarchy helper data. |
| `Views/TicketsWeb/*` | Ticket/project/comment/attachment APIs under `/api/Tickets` and `/api/Ticket`. |
| `Views/InventoryAuditWeb/*` | Inventory audit APIs under `/api/InventoryAudit`. |
| `Views/HierarchyWeb/*` | Hierarchy APIs under `/api/Hierarchy`. |
| `Views/DashboardWeb/*` | Dashboard APIs under `/api/Dashboard` and module-specific summary APIs. |

## Middleware and Filter Chain

```text
GlobalExceptionMiddleware
Security headers middleware
HTTPS enforcement outside development
Swagger in development
HTTPS redirection
Static files
Routing
Rate limiter
CORS
Session
Authentication
Authorization
Controller filters
Controller action
```

# Database Documentation

## Database Access Pattern

The project uses Dapper and raw ADO.NET rather than EF Core migrations/entities for most data access.

Typical patterns:

```csharp
await connection.QueryAsync<T>(
    "schema.StoredProcedureName",
    parameters,
    commandType: CommandType.StoredProcedure);
```

or:

```csharp
await connection.QueryAsync<T>(
    "EXEC [schema].[StoredProcedureName] @param",
    new { param });
```

## Connection Groups

| Configuration key | Purpose |
|---|---|
| `DefaultConnection` | Main SQL Server database used by most services. |
| `AzureConnection` | Alternate SQL Server/Azure SQL connection. |
| `FHConnection` | Used by admin/payment checker style queries. |
| `LiveHanaConnection`, `LiveBevHanaConnection`, `LiveMartHanaConnection` | SAP HANA company databases. |
| `HanaSettings:{Environment}:{Company}` | Company-specific HANA connection/schema mapping. |
| `SapServiceLayer:BaseUrl` | SAP Service Layer API base URL. |

## Key Database Areas Inferred From Code

| Area | Tables/schemas/procedures seen in code |
|---|---|
| Users/auth | `jsUser`, `JSUser`, `jsUserRole`, `jsRole`, user/company procedures. |
| Permissions | `dbo.jsAddUserToGroup`, `dbo.jsRemoveUserFromGroup`, `dbo.jsAssignPermissionToGroup`, `dbo.jsGetUserEffectivePermissions`, module/group/permission procedures. |
| Budget | Stored procedures in `bud` schema such as `bud.jsGetBudgetInsight`, `bud.jsGetPendingBudgets`, `bud.jsGetApprovedBudgets`, `bud.jsGetRejectedBudgets`, `bud.jsGetNextApprover`. |
| Advance request | Stored procedures in `adv` schema such as `adv.jsGetPendingExpenses`, `adv.jsGetApprovedExpenses`, `adv.jsGetRejectedExpenses`, `adv.jsGetExpenseApprovalFlow`. |
| BOM | Stored procedures in `bom` schema such as `bom.jsCreateBom`, `bom.jsBomReject`, `bom.jsBomNotify`, `bom.jsGetBomFiles`, `bom.jsGetBomApprovalFlow`. |
| Inventory | Inventory stored procedures and item/warehouse/location queries in `InventoryAuditService`. |
| Hierarchy | `Hie.Employees`, `Hie.EmployeeReportingRelationships`, `Hie.SalesHierarchy`, `Hie.Departments`, `Hie.SubDepartments`, `Hie.SalaryConfig`, `Hie.SalaryKeyManager`, `Hie.sp_GetAuditLogs`. |
| Tasks | Task stored procedures called from `TaskService`. |
| Tickets | Ticket/project/comment/attachment stored procedures called from `TicketsService`. |

## ER-Style Concept Map

```text
User
  -> UserRole / Role
  -> Company access
  -> Permission groups
  -> Effective permissions
  -> Creates/updates workflow documents

Workflow document
  -> Header
  -> Detail lines
  -> Files/attachments
  -> Approval flow
  -> Stage users
  -> Notifications

Company
  -> SQL Server business rows
  -> SAP HANA schema/company database
  -> Module permissions
  -> Dashboard/report filters

Task/Ticket
  -> Creator
  -> Assignee/assigner
  -> Status
  -> Comments/progress
  -> Attachments
```

## Primary Keys and Relationships

The code does not define database constraints in C# migrations. Relationships are inferred from model properties and SQL/stored procedure usage:

| Concept | Common key fields |
|---|---|
| User | `userId`, `UserId`, `createdBy`, `updatedBy`. |
| Company | `company`, `companyId`, `selectedCompanyId`. |
| Approval flow | `flowId`, `stageId`, `templateId`, `currentStageId`, `priority`, status fields. |
| BOM | `bomId`, `parentCode`, `componentCode`, `version`, `company`. |
| Budget | `budgetId`, `subBudgetId`, `templateId`, `docEntry`, `company`. |
| Inventory | `sessionId`, `LotNumber`, `ItemId`, `ItemCode`, `WarehouseId`, `Company`. |
| Task | `taskId`, creator/assignee fields, status and progress fields. |
| Ticket | `ticketId`, `projectId`, `attachmentId`, user assignment fields. |

# Frontend Flow

## Routing System

The app uses server-side MVC routing:

```csharp
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=DashboardWeb}/{action=Index}/{id?}");
```

API controllers use attribute routing such as:

```text
/api/Auth/login
/api/Task/CreateTask
/api/Tickets/CreateTicket
/api/Permission/GetUserEffectivePermissions
```

Fallback routing sends unknown routes to `DashboardWebController.Index`.

## State Management

Frontend state is mostly server session plus browser JavaScript state:

| State | Location |
|---|---|
| Auth cookie | `JSAP.Auth`, HTTP-only. |
| Refresh token | `JSAP.Refresh`, HTTP-only, path `/api/Auth`. |
| Company list/current company | ASP.NET session, read by `_Layout.cshtml`. |
| Sidebar collapse state | `localStorage.sidebarCollapsed`. |
| Auth initialization state | Runtime variables in `wwwroot/js/auth.js`. |
| Current user/company globals | `window.APP` in `_Layout.cshtml` and `auth.js`. |

There is no React/Vue/Redux state management in this codebase.

## API Integration

The UI uses jQuery AJAX and `fetch`. `wwwroot/js/auth.js` wraps both:

```text
fetch wrapper
  -> sends credentials
  -> if 401, calls /api/Auth/refresh
  -> retries once

jQuery wrapper
  -> sets xhrFields.withCredentials
  -> initializes auth before non-auth AJAX calls
  -> retries 401 once after refresh
```

## Protected Pages

Most pages are protected globally by the authenticated authorization filter. `LoginController` is `[AllowAnonymous]`.

`_Layout.cshtml` performs a second layer of menu/page permission behavior by comparing the current URL against the permissions returned from `GetUserEffectivePermissions`.

## Component Architecture

This is not a component framework application. Reuse happens through:

| Mechanism | Examples |
|---|---|
| Shared layout | `Views/Shared/_Layout.cshtml`. |
| Razor view folders | One folder per module, such as `TaskWeb`, `TicketsWeb`, `InventoryAuditWeb`. |
| Static JS modules | `task-management.js`, `task-dashboard.js`, `hierarchy-sales-popup.js`, `auth.js`. |
| Static CSS modules | `task-management.css`, `tickets-ui.css`, `inventory.css`, `bp-creation.css`. |

# Backend Flow

## Startup Flow

`Program.cs` performs the backend setup:

1. Configure logging.
2. Create authenticated authorization policy.
3. Register controllers and MVC with global filters.
4. Configure SmartAuth cookie/JWT authentication.
5. Configure session.
6. Configure Swagger.
7. Configure authorization policies.
8. Configure rate limiting.
9. Configure CORS.
10. Register service interfaces and implementations.
11. Build the app.
12. Register middleware pipeline.
13. Map MVC routes, API controllers, fallback route, and health endpoint.

## Controller Logic

Controller actions usually:

```text
Read route/body/query/form data
Validate basic inputs
Read authenticated user or session fields if needed
Call service interface
Return Ok/BadRequest/Unauthorized/NotFound/StatusCode
```

## Service Layer

Services are responsible for:

```text
SQL Server queries/stored procedures
SAP HANA queries
SAP Service Layer calls
Approval workflow updates
Notification dispatch
File metadata persistence
Business response construction
```

## Error Handling

| Mechanism | Behavior |
|---|---|
| `GlobalExceptionMiddleware` | Catches unhandled exceptions, logs them, returns JSON. |
| `SensitiveErrorResponseFilter` | Replaces 500-level object responses with a generic message. |
| Controller try/catch blocks | Many controllers log or return generic 500 responses. |
| `auth.js` AJAX/fetch handling | Displays global API/auth errors for browser pages. |

## Logging

`Program.cs` uses console and debug providers. `GlobalExceptionMiddleware` logs unhandled exceptions. Controllers such as `AuthController` log successful and failed login events.

`appsettings.json` includes a file logging section, but the active startup code only registers console and debug logging providers.

# Deployment Structure

The project is deployed as an ASP.NET Core web app.

Build/publish commands:

```bash
dotnet restore
dotnet build
dotnet publish -c Release
```

Runtime dependencies:

```text
.NET 9 runtime
SQL Server connectivity
SAP HANA provider/runtime connectivity
SAP Service Layer access
SFTP credentials if file download is enabled
Firebase service account file if push notifications are enabled
Production secret storage for credentials/JWT keys
HTTPS termination
```

Swagger is available only in development. HSTS and HTTPS-only enforcement are enabled outside development.

# Realtime/WebSocket Features

No WebSocket, SignalR, or realtime hub implementation was found in the current source tree. Notifications are implemented through API calls and `NotificationService`/Firebase-related configuration rather than an in-process WebSocket hub.

# Public and Private Routes

Public routes:

```text
GET  /Login
POST /api/Auth/login
POST /api/Auth/refresh
POST /api/Auth/logout
GET  /health
Development only: /swagger
```

Private routes:

```text
Most /api/* controllers
Most MVC pages using the shared layout
UserManagementController pages, additionally requiring AdminOnly
File debug endpoints, requiring AdminOnly
```

# Additional Documentation To Create Next

Recommended follow-up files:

```text
docs/authentication.md
docs/api-flow.md
docs/security.md
docs/database.md
docs/frontend-architecture.md
docs/backend-architecture.md
docs/deployment.md
docs/permissions-rbac.md
docs/sap-integration.md
docs/file-upload-download.md
docs/module-map.md
```

