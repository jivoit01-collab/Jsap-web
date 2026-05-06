# IMC Item Master Module Architecture

This document explains the actual Item Master Creation implementation in the JSAPNEW codebase. The module is centered on `ItemMasterController`, `IItemMasterService`, `ItemMasterService`, and `ItemMasterModel`. It also depends on SAP session logic from `Bom2Service` and notification delivery from `NotificationService`.

Important scope note: the C# code calls many SQL Server stored procedures and SAP HANA procedures, but the stored procedure source and database DDL are not checked into this repo. Where exact table names are not visible, this document marks table effects as inferred from procedure names, DTO names, and returned fields.

## Module File Map

| File | Purpose |
|---|---|
| `Controllers/ItemMasterController.cs` | Main API controller for IMC dropdowns, create/update item data, approve/reject, SAP post, approval flow, item dashboards, and BKDT endpoints. |
| `Services/Interfaces/IItemMasterService.cs` | Contract for all Item Master and BKDT service methods. |
| `Services/Implementation/ItemMasterService.cs` | Core implementation: HANA dropdown reads, SQL stored procedure calls, approval logic, SAP Service Layer item posting, MART auto-sync, error logging, and notification triggers. |
| `Models/ItemMasterModel.cs` | DTOs for dropdowns, item init data, SAP data, full item insert, pending API insertions, SAP payload, approval flow, and BKDT models. |
| `Services/Implementation/Bom2Service.cs` | Provides SAP Service Layer login sessions for Oil, Beverages, and Mart using `GetSAPSessionOilAsync`, `GetSAPSessionBevAsync`, `GetSAPSessionMartAsync`. |
| `Services/Interfaces/IBom2Service.cs` | Contract used by `ItemMasterService` for SAP sessions. |
| `Services/Implementation/NotificationService.cs` | Sends Firebase push notifications and inserts notification rows through `nt` stored procedures. |
| `Services/Interfaces/INotificationService.cs` | Contract for FCM tokens, push send, and notification persistence. |
| `Services/Interfaces/IUserService.cs` | Used by `SendPendingItemCountNotificationAsync` to load active users. |
| `Program.cs` | Registers `IItemMasterService`, `IBom2Service`, `INotificationService`, authentication, authorization, filters, CORS, and rate limiting. |
| `docs/SAP_ITEM_CREATION_DOCUMENTATION.md` | Existing SAP B1 field rules and company-specific behavior reference. |

No dedicated `Views/ItemMaster*` Razor page or `wwwroot/js/item-master.js` file exists in the current repo. Local searches found only indirect reuse of `/api/ItemMaster/GetGroup` from inventory audit views. The IMC creation UI is likely outside this repo, generated elsewhere, or not committed.

## Dependency Diagram

```text
Frontend or API client
  -> /api/ItemMaster/*
  -> ItemMasterController
  -> IItemMasterService
  -> ItemMasterService
      -> SQL Server DefaultConnection
         -> imc.* stored procedures
      -> SAP HANA company schemas
         -> JsGet* dropdown procedures
      -> IBom2Service
         -> SAP Service Layer Login sessions
      -> SAP Service Layer
         -> POST /b1s/v1/Items
      -> INotificationService
         -> Firebase push + nt.jsInsertNotification
```

# Controller/API Surface

`ItemMasterController.cs` is decorated with:

```csharp
[Route("api/[controller]")]
[ApiController]
[Authorize]
```

All `/api/ItemMaster/*` endpoints require authentication through the global `SmartAuth` scheme unless an action explicitly allows anonymous access. This controller has no `[AllowAnonymous]` actions.

## Dropdown and Reference APIs

These endpoints read from SAP HANA stored procedures through `ItemMasterService`.

| Route | Controller action | Service method | HANA procedure |
|---|---|---|---|
| `GET /api/ItemMaster/GetHSN?company=1` | `GetHSN` | `GetHSNAsync` | `"{schema}"."JsGetHSN"()` |
| `GET /api/ItemMaster/GetTaxRate?company=1` | `GetTaxRate` | `GetTaxRateAsync` | `"{schema}"."JsGetTaxRate"()` |
| `GET /api/ItemMaster/GetInventoryUOM?company=1` | `GetInventoryUOM` | `GetInventoryUOMAsync` | `"{schema}"."JsGetInvntryUom"()` |
| `GET /api/ItemMaster/GetPackingType?GroupCode=102&company=1` | `GetPackingType` | `GetPackingTypeAsync` | `"{schema}"."JsGetPackingType"(?)` |
| `GET /api/ItemMaster/GetPackType?GroupCode=102&company=1` | `GetPackType` | `GetPackTypeAsync` | `"{schema}"."JsGetPackType"(?)` |
| `GET /api/ItemMaster/GetPurPackType?company=1` | `GetPurPackType` | `GetPurPackAsync` | `"{schema}"."JsGetPurPackMsr"()` |
| `GET /api/ItemMaster/GetSalPackType?company=1` | `GetSalPackType` | `GetSalPackAsync` | `"{schema}"."JsGetSalPackMsr"()` |
| `GET /api/ItemMaster/GetSalUnitType?GroupCode=102&company=1` | `GetSalUnitType` | `GetSalUnitAsync` | `"{schema}"."JsGetSalUnitMsr"(?)` |
| `GET /api/ItemMaster/GetSKU?GroupCode=102&company=1` | `GetSKUType` | `GetSKUAsync` | `"{schema}"."JsGetSKU"(?)` |
| `GET /api/ItemMaster/GetVariety?BRAND=JIVO&GroupCode=102&company=1` | `GeVarietyType` | `GetVarietyAsync` | `"{schema}"."JsGetVariety"(?,?)` |
| `GET /api/ItemMaster/GetSubGroup?BRAND=JIVO&VARIETY=CANOLA&GroupCode=102&company=1` | `GetSubGroup` | `GetSubGroupAsync` | `"{schema}"."JsGetSubGroup"(?,?,?)` |
| `GET /api/ItemMaster/GetUnit?GroupCode=102&company=1` | `GetUnitType` | `GetUnitAsync` | `"{schema}"."JsGetUnit"(?)` |
| `GET /api/ItemMaster/GetFA?GroupCode=112&company=1` | `GetFAType` | `GetFaAsync` | `"{schema}"."JsGetFaType"(?)` |
| `GET /api/ItemMaster/GetBuyUnit?company=1` | `GetBuyUnitType` | `GetBuyUnitAsync` | `"{schema}"."JsGetBuyUnitUom"()` |
| `GET /api/ItemMaster/GetGroup?company=1` | `GetGroupType` | `GetGroupAsync` | `"{schema}"."JsGetGroupNameWithCode"()` |
| `GET /api/ItemMaster/GetBrand?GroupCode=102&company=1` | `GetBrandType` | `GetBrandAsync` | `"{schema}"."JsGetBrand"(?)` |
| `GET /api/ItemMaster/GetBuyUnitMsr?GroupCode=102&company=1` | `GetBuyUnitMsr` | `GetBuyUnitMsrAsync` | `"{schema}"."JsGetBuyUnitMsr"(?)` |
| `GET /api/ItemMaster/GetInvUnitMsr?GroupCode=102&company=1` | `GetInvUnitMsr` | `GetInvUnitMsrAsync` | `"{schema}"."JsGetInvUnitMsr"(?)` |
| `GET /api/ItemMaster/JsGetUOMGroup?GroupCode=102&company=1` | `JsGetUOMGroup` | `JsGetUOMGroupAsync` | `"{schema}"."JsGetUOMGroup"(?)` |
| `GET /api/ItemMaster/GetDistinctItemName?company=1` | `GetDistinctItemName` | `GetDistinctItemNameAsync` | `"{schema}"."JSGETDISTINCTITEMNAMES"()` |
| `GET /api/ItemMaster/GetDistinctItemNamesSQL` | `GetDistinctItemNamesSQL` | `GetDistinctItemNameSqlAsync` | SQL Server `imc.jsGetDistinctItemNames` |
| `GET /api/ItemMaster/GetMergedDistinctItemNames?company=1` | `GetMergedDistinctItemNames` | HANA + SQL methods | Merges HANA and SQL item names in controller |

## Write/Workflow APIs

| Route | Controller action | Service method | Stored procedure or integration |
|---|---|---|---|
| `POST /api/ItemMaster/InsertFullItem` | `InsertFullItem` | `InsertFullItemDataAsync` | `imc.jsInsertFullItemData`, then `imc.GetUsersInCurrentStage`, then FCM/notification insert |
| `POST /api/ItemMaster/InsertInitData` | `InsertInitData` | `InsertInitDataAsync` | `imc.jsInsertInitData` |
| `POST /api/ItemMaster/InsertSAPData` | `InsertSAPData` | `InsertSAPDataAsync` | `imc.jsInsertSAPData` |
| `POST /api/ItemMaster/UpdateInitData` | `UpdateInitData` | `UpdateInitDataAsync` | `imc.jsUpdateInitData` |
| `POST /api/ItemMaster/UpdateSAPData` | `UpdateSAPData` | `UpdateSAPDataAsync` | `imc.jsUpdateSAPData` |
| `POST /api/ItemMaster/ApproveItem` | `ApproveItem` | `ApproveItemAsync` | `imc.jsGetItemCurrentStage`, optional SAP post, then `imc.jsApproveItem`, then notifications |
| `POST /api/ItemMaster/RejectItem` | `RejectItem` | `RejectItemAsync` | `imc.jsRejectItem` |
| `GET /api/ItemMaster/GetPendingItems` | `GetPendingItems` | `GetPendingItemsAsync` | `imc.jsGetPendingItems` |
| `GET /api/ItemMaster/GetApprovedItems` | `GetApprovedItems` | `GetApprovedItemsAsync` | `imc.jsGetApprovedItems` |
| `GET /api/ItemMaster/GetRejectedItems` | `GetRejectedItems` | `GetRejectedItemsAsync` | `imc.jsGetRejectedItems` |
| `GET /api/ItemMaster/GetAllItems` | `GetAllItems` | `GetAllItemsAsync` | Calls pending, approved, rejected SPs and merges in memory |
| `GET /api/ItemMaster/GetFullItemDetails` | `GetFullItemDetails` | `GetFullItemDetailsAsync` | `imc.jsGetFullItemDetails` |
| `GET /api/ItemMaster/GetWorkflowInsights` | `GetWorkflowInsights` | `GetWorkflowInsightsAsync` | `imc.jsGetWorkflowInsights` |
| `GET /api/ItemMaster/GetIMCApprovalFlow` | `GetIMCApprovalFlow` | `GetIMCApprovalFlowAsync` | `imc.jsGetIMCApprovalFlow` |
| `GET /api/ItemMaster/GetCreatedByDetail` | `GetCreatedByDetail` | `GetCreatedByDetailAsync` | `imc.jsGetCreatedByDetail` |
| `GET /api/ItemMaster/GetPendingItemApiInsertions` | `GetPendingItemApiInsertions` | `GetPendingItemApiInsertionsAsync` | `imc.jsGetPendingItemApiInsertions` |
| `POST /api/ItemMaster/Items?ItemId=123` | `PostBomsToSAP` | `PostItemsToSAPAsync` | Manual SAP Service Layer post for pending item rows |
| `GET /api/ItemMaster/GetItemByUserId` | `GetItemByUserId` | `GetItemByIdAsync` | `imc.jsGetItemByUserId` |
| `GET /api/ItemMaster/GetItemUserIdsSendNotificatios` | same | `GetItemUserIdsSendNotificatiosAsync` | `imc.jsImcNotify` |
| `GET /api/ItemMaster/SendPendingItemCountNotification` | same | `SendPendingItemCountNotificationAsync` | Reads active users, workflow insights, FCM tokens |
| `GET /api/ItemMaster/GetItemCurrentUsersSendNotification` | same | `GetItemCurrentUsersSendNotificationAsync` | `imc.GetUsersInCurrentStage` |
| `GET /api/ItemMaster/GetRejectedItemsForCreator` | same | `GetRejectedItemsForCreatorAsync` | `imc.jsGetRejectedItemsForCreator` |

# Complete Item Creation Lifecycle

## High-Level Lifecycle

```text
Frontend loads dropdowns
  -> HANA JsGet* procedures return SAP reference values
User submits item payload
  -> POST /api/ItemMaster/InsertFullItem
  -> InsertFullItemDataModel receives all init + SAP fields
  -> ItemMasterController null-checks payload
  -> ItemMasterService.InsertFullItemDataAsync
  -> UI Variety/SubGroup are swapped for DB write
  -> SQL Server SP [imc].[jsInsertFullItemData]
  -> SP returns Message and NewInitId
  -> Service calls [imc].[GetUsersInCurrentStage]
  -> FCM push + nt.jsInsertNotification for current approvers
Approver opens pending item
  -> GetPendingItems/GetFullItemDetails/GetIMCApprovalFlow
Approver approves
  -> POST /api/ItemMaster/ApproveItem
  -> Service checks [imc].[jsGetItemCurrentStage]
  -> If not final stage: call [imc].[jsApproveItem], notify next stage
  -> If final stage: get pending SAP rows, post to SAP first
  -> If SAP succeeds: call [imc].[jsApproveItem]
  -> If SAP fails: DB approval is blocked
  -> Notifications sent
```

## Request Example: Create Full Item

```http
POST /api/ItemMaster/InsertFullItem
Content-Type: application/json
Cookie: JSAP.Auth=...
```

```json
{
  "userId": 27,
  "company": 1,
  "itemName": "JIVO CANOLA OIL 1 LTR",
  "itemGroupCode": 102,
  "itemGroupName": "FINISHED",
  "taxRate": "5",
  "chapterId": "1514",
  "chapterName": "Edible Oil",
  "unit": "OIL",
  "brand": "JIVO",
  "variety": "CANOLA",
  "subGroup": "REFINED",
  "sku": "1 LTR",
  "isLitre": "Y",
  "litre": 1,
  "grossWeight": 0.92,
  "mrp": 250,
  "packType": "CONSUMER PACK",
  "packingType": "BOTTLE",
  "faType": null,
  "uom": "LTR",
  "salesUom": "LTR",
  "invUom": "LTR",
  "purchaseUom": "LTR",
  "boxSize": 12,
  "unitSize": 1,
  "uomGroup": "Manual",
  "franName": 0,
  "prchseItem": "tYES",
  "invItem": "tYES",
  "numInBuy": 1,
  "salUnitMsr": "LTR",
  "numInSale": 1,
  "evalSystem": "bis_FIFO",
  "threeType": "iProductionTree",
  "manSerNum": "tNO",
  "salFactor1": 1,
  "salFactor2": 12,
  "salFactor3": 1,
  "salFactor4": 1,
  "purFactor1": 1,
  "purFactor2": 1,
  "purFactor3": 1,
  "purFactor4": 1,
  "purPackMsr": "LTR",
  "purPackUn": 1,
  "salPackUn": 1,
  "manBtchNum": "tYES",
  "genEntry": "tNO",
  "wtLiable": "tYES",
  "issueMethod": "im_Manual",
  "mngMethod": "bomm_OnEveryTransaction",
  "invntoryUom": "LTR",
  "series": 389,
  "gstRelevant": "tYES",
  "gstTaxCtg": "gtc_Regular"
}
```

Typical success response:

```json
{
  "message": "Item inserted successfully.",
  "success": true,
  "approvalStatus": null,
  "sapStatus": null,
  "martStatus": null
}
```

The actual `message` comes from the first result set row returned by `[imc].[jsInsertFullItemData]`.

# Field Mapping Documentation

## Critical Variety/SubGroup Swap

`ItemMasterService` contains:

```csharp
private static (object? Variety, object? SubGroup) MapToDb(string? uiVariety, string? uiSubGroup)
{
    return (uiSubGroup, uiVariety);
}
```

That means:

```text
Frontend model.Variety  -> DB parameter @subGroup
Frontend model.SubGroup -> DB parameter @variety
```

Reads are swapped back through `MapFromDb`, which mutates returned objects:

```text
DB Variety  -> API SubGroup
DB SubGroup -> API Variety
```

SAP payload reverses again when building `ItemsTree`:

```text
ItemsTree.U_Sub_Group = first.Variety
ItemsTree.U_Variety   = first.SubGroup
```

This is intentional in current code. If the UI shows swapped values, or SAP receives subgroup/variety in the wrong UDF, debug `MapToDb`, `MapFromDb`, `[imc].[jsInsertFullItemData]`, and `[imc].[jsGetPendingItemApiInsertions]` first.

## Full Item Insert Mapping

| Frontend/JSON field | DTO property | Service parameter | DB/SP target | SAP payload field |
|---|---|---|---|---|
| `userId` | `InsertFullItemDataModel.UserId` | `@userId` | `[imc].[jsInsertFullItemData]` user/creator column, exact table hidden | `PendingItemApiInsertionsModel.UserId`; used for error `CreatedBy` |
| `company` | `Company` | `@company` | company/workflow scope | Chooses SAP session: Oil=1, Bev=2, Mart=3 |
| `itemName` | `ItemName` | `@itemName` | item init row | `ItemsTree.ItemName` |
| `itemGroupCode` | `ItemGroupCode` | `@itemGroupCode` | item group code | `ItemsTree.ItemsGroupCode`; drives series, issue method, batch, purchase/sales/inventory flags |
| `itemGroupName` | `itemGroupName` | `@itemGroupName` | item group name | Used to decide MART sync when name contains `FINISHED` or `FG` |
| `taxRate` | `TaxRate` | `@taxRate` | item tax rate | `U_Rev_tax_Rate`, `U_Tax_Rate` |
| `chapterId` | `ChapterId` | `@chapterId` | HSN/chapter ID | `ChapterID`, parsed to int; invalid parse becomes `0` |
| `chapterName` | `ChapterName` | Not passed by active `InsertFullItemDataAsync` | Not written by active full insert code unless SP derives it | Not used in SAP payload |
| `unit` | `Unit` | `@unit` | unit/business unit | `U_Unit` |
| `brand` | `Brand` | `@brand` | brand | `U_Brand` |
| `variety` | `Variety` | `@subGroup` because of `MapToDb` | DB column inferred as subgroup or swapped value | After `MapFromDb`, `ItemsTree.U_Sub_Group = first.Variety` |
| `subGroup` | `SubGroup` | `@variety` because of `MapToDb` | DB column inferred as variety or swapped value | After `MapFromDb`, `ItemsTree.U_Variety = first.SubGroup` |
| `sku` | `Sku` | `@sku` | SKU | `U_SKU` |
| `isLitre` | `IsLitre` | `@isLitre` | litre flag | `U_IsLitre`; also drives `U_TYPE` premium/commodity logic |
| `litre` | `Litre` | `@Litre` | litre quantity | Not directly sent to SAP payload |
| `grossWeight` | `GrossWeight` | `@grossWeight` | gross weight | `U_Gross_Weight` |
| `mrp` | `Mrp` | `@mrp` | MRP | `U_MRP` |
| `packType` | `PackType` | `@packType` | pack type | `U_PACK_TYPE` |
| `packingType` | `PackingType` | `@packingType` | packing type | `U_Packing_Type` for companies 1 and 2 only |
| `faType` | `FaType` | `@faType` | fixed asset type | `U_FA_Type` for companies 1/2, `U_FA_TYPE` for company 3 |
| `uom` | `Uom` | `@uom` | UOM | Not directly sent; SAP uses sales/inventory/purchase UOM fields |
| `salesUom` | `SalesUom` | `@salesUom` | sales UOM | `SalesUnit`, `SalesPackagingUnit` only when `SalesItem=tYES` |
| `invUom` | `InvUom` | `@invUom` | inventory UOM | `InventoryUOM` only when `InventoryItem=tYES` |
| `purchaseUom` | `PurchaseUom` | `@purchaseUom` | purchase UOM | `PurchaseUnit`, `PurchasePackagingUnit` only when `PurchaseItem=tYES` |
| `boxSize` | `BoxSize` | `@boxSize` | box size | `SalesFactor2` |
| `unitSize` | `UnitSize` | `@UnitSize` | unit size | `SalesQtyPerPackUnit` |
| `uomGroup` | `UomGroup` | `@UomGroup` | UOM group | `UoMGroupEntry`: `Manual=-1`, `MTS2LITRE=1`, `KG2LITRE=2`, `MTS2LITRE(OLIVE)=3`, default `0` |
| `franName` | `FranName` | `@franName` | SAP data row | Present in pending model but not used in current `ItemsTree` |
| `prchseItem` | `PrchseItem` | `@prchseItem` | SAP data row | Current SAP payload recalculates `PurchaseItem` by group code instead of using DB value |
| `invItem` | `InvItem` | `@invItem` | SAP data row | Current SAP payload recalculates `InventoryItem` by group code |
| `numInBuy` | `NumInBuy` | `@numInBuy` | SAP data row | Not directly used in current `ItemsTree` |
| `salUnitMsr` | `SalUnitMsr` | `@salUnitMsr` | SAP data row | Pending model has it; current payload uses `SalesUom` instead |
| `numInSale` | `NumInSale` | `@numInSale` | SAP data row | Not directly used in current `ItemsTree` |
| `evalSystem` | `EvalSystem` | `@evalSystem` | SAP data row | Current payload recalculates `CostAccountingMethod` |
| `threeType` | `ThreeType` | `@threeType` | SAP data row | Not currently mapped to `ItemsTree` despite being documented in SAP reference |
| `manSerNum` | `ManSerNum` | `@manSerNum` | SAP data row | Current payload forces `ManageSerialNumbers=tNO` |
| `salFactor1..4` | `SalFactor1..4` | `@salFactor1..4` | SAP data row | Only `BoxSize` maps to `SalesFactor2`; other factors not used in current payload |
| `purFactor1..4` | `PurFactor1..4` | `@purFactor1..4` | SAP data row | Not directly used in current payload |
| `purPackMsr` | `PurPackMsr` | `@purPackMsr` | SAP data row | Not used directly; purchase packaging uses `PurchaseUom` |
| `purPackUn` | `PurPackUn` | `@purPackUn` | SAP data row | Not directly used |
| `salPackUn` | `SalPackUn` | `@salPackUn` | SAP data row | Not directly used |
| `manBtchNum` | `ManBtchNum` | `@manBtchNum` | SAP data row | Current payload recalculates `ManageBatchNumbers` by company/group |
| `genEntry` | `GenEntry` | `@genEntry` | SAP data row | Not directly used |
| `wtLiable` | `WtLiable` | `@wtLiable` | SAP data row | Current payload recalculates `WTLiable`; MART forced `tNO` |
| `issueMethod` | `IssueMethod` | `@issueMethod` | SAP data row | Current payload recalculates `IssueMethod` |
| `mngMethod` | `MngMethod` | `@mngMethod` | SAP data row | Current payload forces `SRIAndBatchManageMethod=bomm_OnEveryTransaction` |
| `invntoryUom` | `InvntoryUom` | `@invntoryUom` | SAP data row | Current payload uses `InvUom` for `InventoryUOM` |
| `series` | `Series` | `@series` | SAP data row | Used only as fallback; code maps series from `(company, groupCode)` first |
| `gstRelevant` | `GstRelevant` | `@gstRelevant` | SAP data row | Current payload forces `GSTRelevnt=tYES` |
| `gstTaxCtg` | `GstTaxCtg` | `@gstTaxCtg` | SAP data row | Current payload forces `GSTTaxCategory=gtc_Regular` |
| `sellItem` | `SellItem` | Not passed by full insert | Not stored by full insert unless SP derives | `SalesItem` is recalculated in SAP post |
| `prcrmntMtd` | `PrcrmntMtd` | Not passed by full insert | Not stored by full insert unless SP derives | Not used in current `ItemsTree` |

## SAP Payload Field Mapping

`PostItemsToSAPAsync` builds an `ItemsTree` object and serializes it to JSON for `POST {SapServiceLayer:BaseUrl}/Items`.

| `ItemsTree` field | Source / rule |
|---|---|
| `ItemName` | `PendingItemApiInsertionsModel.ItemName` |
| `ItemsGroupCode` | `PendingItemApiInsertionsModel.ItemGroupCode` |
| `Series` | Code mapping by company/group; falls back to `first.Series` |
| `ChapterID` | `int.TryParse(first.ChapterId)`, otherwise `0` |
| `PurchaseItem`, `InventoryItem`, `SalesItem` | Recalculated from group code |
| `U_Rev_tax_Rate`, `U_Tax_Rate` | `first.TaxRate` |
| `U_Unit` | `first.Unit` |
| `U_Brand` | `first.Brand` |
| `U_Sub_Group` | `first.Variety` after service read-mapping |
| `U_Variety` | `first.SubGroup` after service read-mapping |
| `U_SKU` | `first.Sku` |
| `U_IsLitre` | `first.IsLitre` |
| `U_Gross_Weight` | `first.GrossWeight` |
| `U_MRP` | `first.Mrp` |
| `U_PACK_TYPE` | `first.PackType` |
| `U_Packing_Type` | `first.PackingType`, only company 1 or 2 |
| `U_FA_Type` | `first.FaType`, only company 1 or 2 |
| `U_FA_TYPE` | `first.FaType`, only company 3 |
| `SalesUnit`, `SalesPackagingUnit` | `first.SalesUom` when sales item |
| `InventoryUOM` | `first.InvUom` when inventory item |
| `PurchaseUnit`, `PurchasePackagingUnit` | `first.PurchaseUom` when purchase item |
| `SalesQtyPerPackUnit` | `first.UnitSize` |
| `SalesFactor2` | `first.BoxSize` |
| `UoMGroupEntry` | mapped from `first.UomGroup` |
| `CostAccountingMethod` | BEV always FIFO; others SNB if batch-managed else FIFO |
| `WTLiable` | `tYES` only for group 102 and company not 3 |
| `IssueMethod` | mapped from group/company rules |
| `ManageBatchNumbers` | mapped from group/company rules |
| `ManageSerialNumbers` | always `tNO` |
| `ForceSelectionOfSerialNumber` | always `tYES` |
| `SRIAndBatchManageMethod` | always `bomm_OnEveryTransaction` |
| `TaxType` | always `tt_Yes` |
| `GSTRelevnt` | always `tYES` |
| `GSTTaxCategory` | always `gtc_Regular` |
| `GLMethod` | always `glm_WH` |
| `U_TYPE` | calculated premium/commodity |

# Dropdown-to-Database Mapping

Dropdown data is loaded from SAP HANA, not from the SQL Server IMC tables.

| UI dropdown | API | HANA procedure | Returned model | Submit field |
|---|---|---|---|---|
| Item group | `GetGroup` | `JsGetGroupNameWithCode` | `GroupModel.ItmsGrpCod`, `ItmsGrpNam` | `ItemGroupCode`, `itemGroupName` |
| Brand | `GetBrand` | `JsGetBrand(GroupCode)` | `BrandModel.Brand` | `Brand` |
| Variety | `GetVariety` | `JsGetVariety(BRAND, GroupCode)` | `GetVarietyModel.Variety` | `Variety` |
| Sub group | `GetSubGroup` | `JsGetSubGroup(BRAND, VARIETY, GroupCode)` | `GetsubgroupModel.SubGroup` | `SubGroup` |
| SKU | `GetSKU` | `JsGetSKU(GroupCode)` | `RecieveSKUmodel.SKU` | `Sku` |
| Unit | `GetUnit` | `JsGetUnit(GroupCode)` | `UnitModel.Unit` | `Unit` |
| HSN | `GetHSN` | `JsGetHSN()` | `HSNModel.AbsEntry`, `ChapterID`, `ChapterName` | `ChapterId`, `ChapterName` |
| Tax rate | `GetTaxRate` | `JsGetTaxRate()` | `TaxRateModel.TAXRATE` | `TaxRate` |
| Pack type | `GetPackType` | `JsGetPackType(GroupCode)` | `PackTypeModel.PackType` | `PackType` |
| Packing type | `GetPackingType` | `JsGetPackingType(GroupCode)` | `PackingTypeModel.PackingType` | `PackingType` |
| FA type | `GetFA` | `JsGetFaType(GroupCode)` | `GetFAModel.FaType` | `FaType` |
| Sales unit | `GetSalUnitType` | `JsGetSalUnitMsr(GroupCode)` | `SalUnitModel.SalUnitMsr` | `SalesUom` or `SalUnitMsr`, depending on client |
| Sales pack | `GetSalPackType` | `JsGetSalPackMsr()` | `SalPackModel.SalPackMsr` | `SalPackUn`/sales pack UI |
| Purchase pack | `GetPurPackType` | `JsGetPurPackMsr()` | `PurPackModel.PurPackMsr` | `PurPackMsr` |
| Buy unit | `GetBuyUnit` | `JsGetBuyUnitUom()` | `BuyUnitModel.BuyUnitMsr` | `PurchaseUom` |
| Buy unit measure | `GetBuyUnitMsr` | `JsGetBuyUnitMsr(GroupCode)` | `BuyUnitMsrModel.BuyUOM` | purchase UOM related fields |
| Inventory UOM | `GetInventoryUOM`, `GetInvUnitMsr` | `JsGetInvntryUom`, `JsGetInvUnitMsr` | `InventoryUOMModel.InvntryUOM` | `InvUom`, `InvntoryUom` |
| UOM group | `JsGetUOMGroup` | `JsGetUOMGroup(GroupCode)` | `UOMgroupModel.UgpEntry` | `UomGroup` |

# Database Flow Documentation

## SQL Server Stored Procedures Used

| Stored procedure | Called from | Purpose | Inputs visible in C# | Output visible in C# |
|---|---|---|---|---|
| `imc.jsInsertFullItemData` | `InsertFullItemDataAsync` | Inserts full item init + SAP data and likely creates workflow flow/stages. | 40+ item/SAP parameters including `@userId`, `@company`, `@itemName`, `@itemGroupCode`, `@variety`, `@subGroup`, UOM and SAP fields. | Reader with `Message`, `NewInitId`. |
| `imc.jsInsertInitData` | `InsertInitDataAsync` | Legacy/two-step insert for item init data only. | User/company/item reference fields. | Scalar new ID. |
| `imc.jsInsertSAPData` | `InsertSAPDataAsync` | Legacy/two-step insert for SAP-specific item data. | `@initId` and SAP fields. | Scalar new ID. |
| `imc.jsUpdateInitData` | `UpdateInitDataAsync` | Updates item init/reference data. | `@id` plus item reference fields. | Reader with `Message`. |
| `imc.jsUpdateSAPData` | `UpdateSAPDataAsync` | Updates SAP-specific item data. | `@initId` plus SAP fields including `@sellItem`, `@PrcrmntMtd`. | Reader with `Message`. |
| `imc.jsGetPendingItems` | list methods | Gets pending items visible to user/company. | `@userId`, `@companyId`. | `PendingItemModel`/`MergedItemModel`. |
| `imc.jsGetApprovedItems` | list methods | Gets approved items. | `@userId`, `@company`. | `ApprovedItemModel`/`MergedItemModel`. |
| `imc.jsGetRejectedItems` | list methods | Gets rejected items. | `@userId`, `@company`. | `RejectedItemModel`/`MergedItemModel`. |
| `imc.jsGetRejectedItemsForCreator` | `GetRejectedItemsForCreatorAsync` | Gets rejected items created by a user. | `@userId`, `@company`. | `RejectedItemsForCreatorModel`. |
| `imc.jsGetFullItemDetails` | `GetFullItemDetailsAsync` | Gets full init + SAP data for one item. | `@itemId`. | `ItemFullDetailModel`. |
| `imc.jsGetWorkflowInsights` | `GetWorkflowInsightsAsync` | Gets pending/approved/rejected counts. | `@userId`, `@companyId`, `@month`. | `WorkflowInsightModel`. |
| `imc.jsGetItemCurrentStage` | `ApproveItemAsync` | Determines current stage and final-stage flag before approval. | `@flowId`. | `FlowId`, `CurrentStage`, `TotalStage`, `CurrentStageId`, `Status`, `IsLastStage`. |
| `imc.jsApproveItem` | `ApproveItemAsync` | Advances/approves workflow after SAP success or intermediate approval. | `@itemId`, `@company`, `@userId`, `@remarks`. | Scalar message. |
| `imc.jsRejectItem` | `RejectItemAsync` | Rejects workflow. | `@itemId`, `@company`, `@userId`. `remarks` exists in model but is not passed in current code. | Scalar message. |
| `imc.jsGetPendingItemApiInsertions` | `GetPendingItemApiInsertionsAsync`, approval final stage, manual `Items` endpoint | Reads item rows to send to SAP Service Layer. | `@itemId`. | `PendingItemApiInsertionsModel`. |
| `imc.jsUpdateItemApiStatus` | `UpdateItemApiStatusAsync` | Marks SAP sync status: processing/success/failure. Also returns previous tag for duplicate check. | `@itemId`, `@apiMessage`, `@tag`. | Previous tag string, expected `Y` for already synced. |
| `imc.LogApiError` | `LogApiErrorAsync` | Persists SAP/API integration failures. | `@ReferenceID`, `@ApiName`, `@ErrorMessage`, `@ErrorCode`, `@Payload`, `@CreatedBy`. | No result set; success set in service. |
| `imc.jsGetIMCApprovalFlow` | `GetIMCApprovalFlowAsync` | Returns stage audit for one flow. | `@flowId`. | `GetIMCApprovalFlowModel`. |
| `imc.jsGetCreatedByDetail` | `GetCreatedByDetailAsync` | Gets detail rows created by a user/company. | `@userId`, `@companyId`. | `CreatedByDetailModel`. |
| `imc.jsGetDistinctItemNames` | `GetDistinctItemNameSqlAsync` | Gets item names already in SQL Server. | none. | `GetDistinctItemName`. |
| `imc.jsGetItemByUserId` | `GetItemByIdAsync` | Gets item summary by creator/user/month. | `@userId`, `@company`, `@month`. | `GetItemByIdModel`. |
| `imc.jsImcNotify` | `GetItemUserIdsSendNotificatiosAsync` | Gets next approver users after approve. | `@imcId`. | `UserIdsForNotificationModel`. |
| `imc.GetUsersInCurrentStage` | `GetItemCurrentUsersSendNotificationAsync` | Gets users in current stage immediately after create. | `@initID`. | `AfterCreatedRequestSendNotificationToUser`. |
| `imc.jsGetId` | `GetItemUserDocumentIdAsync` | Maps flow ID to init/document ID. | `@flowId`. | Scalar init ID. |

## Inferred SQL Tables

The C# source does not expose table names for the IMC module. From procedure names and DTOs, the SQL Server schema likely contains logical tables for:

```text
imc item init/master header data
imc SAP item data
imc approval flow/stages
imc API sync status / API response fields
imc API error log
notification tables under nt schema
```

Use the stored procedure names above to locate actual SQL definitions in the database. If debugging production, the first SQL objects to inspect are:

```sql
EXEC sp_helptext '[imc].[jsInsertFullItemData]';
EXEC sp_helptext '[imc].[jsGetPendingItemApiInsertions]';
EXEC sp_helptext '[imc].[jsGetItemCurrentStage]';
EXEC sp_helptext '[imc].[jsApproveItem]';
EXEC sp_helptext '[imc].[jsUpdateItemApiStatus]';
EXEC sp_helptext '[imc].[LogApiError]';
```

# Approval Flow Analysis

## Create-Time Approval Setup

`POST /api/ItemMaster/InsertFullItem` calls `[imc].[jsInsertFullItemData]`. The stored procedure returns `NewInitId`, then the service calls:

```text
[imc].[GetUsersInCurrentStage] @initID = NewInitId
```

This strongly indicates that the insert procedure creates or links an approval flow immediately. The C# service then notifies current-stage approvers.

## Approval-Time State Machine

```text
ApproveItem request
  -> Acquire per-flow semaphore from _approvalLocks
  -> Query imc.jsGetItemCurrentStage
  -> Validate stage data
  -> If intermediate stage:
       Skip SAP
       Execute imc.jsApproveItem
       Notify next approver(s)
  -> If final stage:
       Read imc.jsGetPendingItemApiInsertions
       POST item to SAP first
       If SAP fails:
           Return BadRequest from controller
           Do NOT execute imc.jsApproveItem
       If SAP succeeds:
           Execute imc.jsApproveItem
           Notify next/final users
```

## Approval API Request

```json
{
  "itemId": 123,
  "company": 1,
  "userId": 27,
  "remarks": "Approved"
}
```

Important detail: `ApproveItemModel.itemId` is treated as a flow ID in many places:

```text
ApproveItemAsync(request.itemId)
  -> imc.jsGetItemCurrentStage @flowId
  -> imc.jsApproveItem @itemId
  -> imc.jsImcNotify @imcId
```

If the UI passes an init/master row ID instead of a flow ID, approval will block or approve the wrong record.

## Rejection Flow

`POST /api/ItemMaster/RejectItem` receives:

```json
{
  "itemId": 123,
  "company": 1,
  "userId": 27,
  "remarks": "Missing HSN"
}
```

The current service passes only:

```text
@itemId
@company
@userId
```

to `[imc].[jsRejectItem]`. `RejectItemModel.remarks` is required by the model but not passed to SQL in the current code. If reject remarks are not stored in production, this is the first implementation gap to check.

## Status Meanings in Current Code

| Status/concept | Where represented | Meaning |
|---|---|---|
| Pending | `jsGetPendingItems`, `WorkflowInsightModel.PendingWorkflows` | Item awaits approval stage action. |
| Approved | `jsGetApprovedItems`, `ApprovalStatus = "Done"` | Workflow advanced/approved in SQL. Final approval only occurs after SAP success. |
| Rejected | `jsGetRejectedItems`, `jsRejectItem` | Workflow rejected. |
| Blocked | `ApproveItemAsync` response only | Approval blocked due to concurrency, invalid stage, no pending SAP rows, or SAP failure. |
| SAP Processing | `jsUpdateItemApiStatus` tag `"P"` | Service is currently attempting SAP creation. |
| SAP Success | `jsUpdateItemApiStatus` tag `"Y"` | Primary SAP item was created or duplicate detected as already created. |
| SAP Failed | `jsUpdateItemApiStatus` tag `"N"` or exception value | SAP primary creation failed; error logged through `imc.LogApiError`. |
| MART skipped/success/failed | `SapItemSyncResult.MartStatus` | Secondary company 3 sync status for eligible FG items. |

# SAP Integration Analysis

## Session Selection

`PostItemsToSAPAsync` selects SAP session by `PendingItemApiInsertionsModel.Company`:

| Company | Session method | SAP CompanyDB source |
|---|---|---|
| `1` | `IBom2Service.GetSAPSessionOilAsync()` | `SapServiceLayer:CompanyDB:{ActiveEnvironment}:Oil` |
| `2` | `IBom2Service.GetSAPSessionBevAsync()` | `SapServiceLayer:CompanyDB:{ActiveEnvironment}:Beverages` |
| `3` | `IBom2Service.GetSAPSessionMartAsync()` | `SapServiceLayer:CompanyDB:{ActiveEnvironment}:Mart` |

`Bom2Service` logs in by posting to `POST {BaseUrl}/Login` and caches `B1SESSION` + `ROUTEID` for 120 minutes.

## Primary SAP POST

```text
POST {SapServiceLayer:BaseUrl}/Items
Cookie: B1SESSION=...; ROUTEID=...
Content-Type: application/json
```

The code disables server certificate validation in `HttpClientHandler`, which avoids TLS validation failures but is a production security risk.

## Duplicate Prevention

There are two in-process concurrency guards:

| Guard | Key | Purpose |
|---|---|---|
| `_approvalLocks` | `request.itemId` / flow ID | Prevents duplicate approval/SAP call for same approval flow. |
| `_sapPostLocks` | `first.InitId` | Prevents duplicate SAP `Items` POST for the same item across final approval, manual `/Items`, or other triggers. |

There is also a DB-level duplicate check:

```text
UpdateItemApiStatusAsync(first.InitId, "Processing SAP creation", "P")
  -> EXEC imc.jsUpdateItemApiStatus
  -> returns previousTag
  -> if previousTag == "Y", skip SAP POST
```

This makes `imc.jsUpdateItemApiStatus` critical. It must be atomic enough to prevent two app instances from double-posting if the app is scaled out.

## Business Rules Applied Before SAP POST

`PostItemsToSAPAsync` recalculates SAP flags from company and group code.

| Rule | Implementation |
|---|---|
| Issue method | Backflush (`B`) for selected group/company combinations; otherwise manual (`M`). |
| Manage batch | `tYES` for 102, 106 in Oil/Bev, 107 in Oil, 115 in Oil; otherwise `tNO`. |
| Cost accounting | Beverages always `bis_FIFO`; Oil/Mart use `bis_SNB` when batch-managed, else `bis_FIFO`. |
| WTLiable | `tYES` only group 102 and company not 3. |
| Sales/Purchase/Inventory flags | Recalculated by group code with special cases for 109, 101, 110, 111, 112, 114. |
| UOM group | `Manual=-1`, `MTS2LITRE=1`, `KG2LITRE=2`, `MTS2LITRE(OLIVE)=3`, else `0`. |
| U_TYPE | `PREMIUM` for selected `IsLitre` + variety combinations; else `COMMODITY`. |
| Series | Mapped by group and company; falls back to stored `Series`. |
| Company-specific UDF casing | `U_FA_Type` for Oil/Bev; `U_FA_TYPE` for Mart. |
| Packing UDF | `U_Packing_Type` only for Oil/Bev. |

## MART Auto-Sync

After successful primary SAP creation, the service may create the same item in MART (`company 3`) when:

```text
primary company is 1 or 2
AND primary SAP creation succeeded
AND item group code is 102 OR group name contains FINISHED or FG
```

Before MART POST, the service adjusts payload:

```text
U_FA_TYPE = null
U_FA_Type = null
U_Packing_Type = null
WTLiable = tNO
CostAccountingMethod = ManageBatchNumbers == tYES ? bis_SNB : bis_FIFO
```

MART errors are logged through `imc.LogApiError` with `ApiName = "SAP/Items/MART"`, but the primary `SapItemSyncResult.IsSuccess` remains based on the primary SAP response.

## Error Parsing and Logging

SAP error bodies are parsed by `ExtractSapErrorCodeAndMessage`:

```text
error.code
error.message.value
```

Failures are logged through:

```text
[imc].[LogApiError]
  @ReferenceID = InitId
  @ApiName = SAP/Items or SAP/Items/MART
  @ErrorMessage
  @ErrorCode
  @Payload = serialized request/response/exception
  @CreatedBy = UserId
```

# Request/Response Flow

## Full Create Flow

```text
Frontend form
  -> dropdowns from /api/ItemMaster/Get*
  -> POST /api/ItemMaster/InsertFullItem
  -> ItemMasterController.InsertFullItem
  -> request null-check
  -> IItemMasterService.InsertFullItemDataAsync
  -> MapToDb swaps Variety/SubGroup
  -> [imc].[jsInsertFullItemData]
  -> read Message + NewInitId
  -> [imc].[GetUsersInCurrentStage]
  -> NotificationService.GetUserFcmTokenAsync
  -> NotificationService.SendPushNotificationAsync
  -> NotificationService.InsertNotificationAsync
  -> JSON response
```

## Final Approval Flow

```text
POST /api/ItemMaster/ApproveItem
  -> ItemMasterController.ApproveItem
  -> IItemMasterService.ApproveItemAsync
  -> acquire _approvalLocks[flowId]
  -> [imc].[jsGetItemCurrentStage]
  -> if IsLastStage:
        [imc].[jsGetPendingItemApiInsertions]
        PostItemsToSAPAsync
          -> acquire _sapPostLocks[InitId]
          -> [imc].[jsUpdateItemApiStatus] tag P
          -> SAP Login session from Bom2Service
          -> POST /Items
          -> [imc].[jsUpdateItemApiStatus] tag Y/N
          -> optional MART POST
          -> [imc].[LogApiError] if needed
     if SAP success or intermediate:
        [imc].[jsApproveItem]
        [imc].[jsImcNotify]
        Firebase + nt.jsInsertNotification
  -> response with ApprovalStatus, SapStatus, MartStatus
```

Failure response when SAP blocks final approval:

```json
{
  "success": false,
  "approvalStatus": "Blocked",
  "sapStatus": "Failed: <SAP message>",
  "martStatus": "Skipped",
  "message": "<SAP message>"
}
```

# File Connection Map

## `ItemMasterController.cs`

Uses:

```text
IConfiguration
IItemMasterService
ILogger<ItemMasterController>
SqlException for selected error handling
[Authorize] and global SmartAuth middleware
```

Responsibilities:

```text
Expose API routes
Null-check request DTOs
Call service methods
Log controller-level failures
Return HTTP status codes
```

## `ItemMasterService.cs`

Uses:

```text
Dapper
Microsoft.Data.SqlClient
Sap.Data.Hana
HttpClient
Newtonsoft.Json / JObject
IBom2Service
INotificationService
IUserService
ConcurrentDictionary<int, SemaphoreSlim>
```

Responsibilities:

```text
Read dropdown/reference data from HANA
Write/read item workflow data through imc stored procedures
Swap Variety/SubGroup for DB compatibility
Construct SAP Items payload
Post to SAP Service Layer
Perform MART auto-sync
Log SAP errors
Send notifications
Protect SAP calls from duplicate in-process execution
```

## `ItemMasterModel.cs`

Important DTOs:

| DTO | Used for |
|---|---|
| `InsertFullItemDataModel` | Main full create payload. |
| `InsertInitDataModel` / `InsertSAPDataModel` | Legacy or two-step create flow. |
| `UpdateInitDataModel` / `UpdateSAPDataModel` | Edit/update flows. |
| `ApproveItemModel` / `RejectItemModel` | Approval actions. |
| `PendingItemApiInsertionsModel` | SQL rows converted into SAP payloads. |
| `ItemsTree` | Serialized SAP Service Layer `Items` payload. |
| `SapItemSyncResult` | Result returned from SAP posting. |
| `GetIMCApprovalFlowModel` | Approval flow display/audit. |
| `ItemMasterModel` | Common success/message/status response. |

## `Bom2Service.cs`

Provides cached SAP sessions. IMC only uses it for login/session cookie acquisition, not BOM logic.

## `NotificationService.cs`

IMC uses:

```text
GetUserFcmTokenAsync
SendPushNotificationAsync
InsertNotificationAsync
```

Notifications are inserted with:

```text
title = "Item Master"
pageId = 6
BudgetId = item/flow/init ID depending on calling path
```

# Frontend-to-Backend Trace

There is no committed IMC-specific frontend page or JS module in this repo. A client should interact with the backend as follows:

1. Load company from current session or user profile.
2. Load item group with `GET /api/ItemMaster/GetGroup`.
3. When group changes, load dependent dropdowns:
   ```text
   GetBrand
   GetSKU
   GetUnit
   GetFA
   GetPackingType
   GetPackType
   GetSalUnitType
   GetBuyUnitMsr
   GetInvUnitMsr
   JsGetUOMGroup
   ```
4. When brand changes, load `GetVariety`.
5. When variety changes, load `GetSubGroup`.
6. Load HSN/tax/UOM reference values:
   ```text
   GetHSN
   GetTaxRate
   GetInventoryUOM
   GetBuyUnit
   GetPurPackType
   GetSalPackType
   ```
7. Submit `InsertFullItemDataModel` to `POST /api/ItemMaster/InsertFullItem`.
8. Show result message.
9. Approval UI calls pending/detail/approval flow endpoints.
10. Approver calls `ApproveItem` or `RejectItem`.

Local repo references:

| File | Local usage |
|---|---|
| `Views/InventoryAuditWeb/AllInventory.cshtml` | Calls `/api/ItemMaster/GetGroup?company=${companyId}` to reuse group dropdown data. |
| `Views/InventoryAuditWeb/AddSession.cshtml` | Calls `/api/ItemMaster/GetGroup?company=${companyId}`. |
| `Views/Shared/_Layout.cshtml` | Can expose menu entries based on permissions, but no committed IMC page mapping was found. |
| `wwwroot/js/auth.js` | Wraps fetch/jQuery calls with cookie refresh logic used by any frontend page. |

# Security Analysis

## Route Protection

`ItemMasterController` uses `[Authorize]`. Therefore every action goes through:

```text
UseSession
UseAuthentication
SmartAuth
UseAuthorization
AuthorizeFilter
AuthenticatedUserBindingFilter
SensitiveErrorResponseFilter
```

Authentication supports:

| Request type | Auth behavior |
|---|---|
| Browser/Razor/AJAX | Cookie auth via `JSAP.Auth`; refresh handled by `wwwroot/js/auth.js`. |
| API client | JWT bearer if `Authorization: Bearer ...` is present. |

## Permission Checks

The controller contains commented permission attributes such as:

```csharp
// [CheckUserPermission("item_master_creation", "view")]
// [CheckUserPermission("item_master_creation", "create")]
// [CheckUserPermission("item_master_creation", "approve")]
```

They are not active in the current code. Practical effect:

```text
Authenticated users can reach ItemMaster routes unless another global policy/menu/UI layer blocks them.
Fine-grained IMC permission enforcement is currently not applied at action level.
```

If the business requires server-side RBAC for IMC, uncomment and fix these attributes. Also verify the session key mismatch: `CheckUserPermissionAttribute` reads `companyId`, while the active web session writes `selectedCompanyId`.

## User and Company Validation

Current controller validation is mostly null checks. It does not consistently validate:

```text
request.UserId matches authenticated user
request.Company matches selectedCompanyId/session company access
itemId/flowId belongs to company
approver is assigned to current stage
```

Some of this may be enforced inside stored procedures, especially `imc.jsApproveItem` and `imc.jsGetItemCurrentStage`. For a security review, inspect those SP definitions.

## SQL Injection

SQL Server calls use `SqlCommand` parameters or Dapper parameters. HANA procedure calls also use `DynamicParameters`.

Risk area: HANA schema names are interpolated into query strings:

```text
CALL "{settings.Schema}"."JsGet..."(...)
```

The schema comes from server configuration, not user input, so this is acceptable if configuration is trusted.

## CSRF and Browser Calls

The controller accepts cookie-authenticated POST requests and does not use ASP.NET antiforgery tokens. SameSite strict cookies reduce CSRF risk, but high-risk mutation endpoints such as `InsertFullItem`, `ApproveItem`, `RejectItem`, `Items`, and update endpoints should be reviewed if any cross-site or embedded client is allowed.

## SAP/TLS Security

Both `ItemMasterService` and `Bom2Service` disable certificate validation for SAP Service Layer HTTP clients:

```csharp
ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
```

This avoids certificate failures but weakens TLS integrity. Production should use a trusted certificate or pinned/validated certificate.

## Rate Limiting

The app has global rate limiting in `Program.cs`, but ItemMaster routes do not have a named limiter. They fall under:

```text
mutation limiter: 40/minute for POST/PUT/PATCH/DELETE
global limiter: 120/minute default
```

`POST /api/ItemMaster/Items` and final `ApproveItem` can trigger SAP calls, so consider a dedicated limiter for SAP mutation endpoints.

# Production Debugging Guide

## Frontend Data Not Saving

Check in order:

1. Browser network request to `POST /api/ItemMaster/InsertFullItem`.
2. Payload field casing and JSON body matches `InsertFullItemDataModel`.
3. Controller response from `ItemMasterController.InsertFullItem`.
4. Service response from `InsertFullItemDataAsync`.
5. SQL procedure `[imc].[jsInsertFullItemData]`.
6. Verify the procedure returns columns exactly named `Message` and `NewInitId`.
7. Inspect SQL row using `NewInitId`.
8. Check notifications only after insert succeeds; notification failure appends to message and does not roll back insert.

Likely failure messages:

| Message | Meaning |
|---|---|
| `Request is null` | Client did not send JSON or content type is wrong. |
| `No data returned from stored procedure.` | SP did not return a row/readable result set. |
| `Error inserting item: ...` | SQL connection/SP exception or service exception. |

## Subgroup or Variety Not Storing Correctly

This is the first place to look:

```text
ItemMasterService.MapToDb
Frontend Variety -> @subGroup
Frontend SubGroup -> @variety
```

Debug checklist:

1. Confirm UI payload values for `variety` and `subGroup`.
2. Confirm `MapToDb(model.Variety, model.SubGroup)` output.
3. Confirm SQL parameters passed to `[imc].[jsInsertFullItemData]`.
4. Inspect DB row before API read mapping.
5. Call `GET /api/ItemMaster/GetPendingItems` or `GetFullItemDetails`.
6. Remember `MapFromDb` swaps `Variety` and `SubGroup` back in API responses.
7. For SAP, inspect pending rows returned by `[imc].[jsGetPendingItemApiInsertions]` and final payload fields `U_Sub_Group`, `U_Variety`.

## SAP Insertion Fails

Check in order:

1. Was approval at final stage?
   ```text
   [imc].[jsGetItemCurrentStage] @flowId
   ```
2. Did pending SAP rows exist?
   ```text
   GET /api/ItemMaster/GetPendingItemApiInsertions?ItemId={flowId}
   [imc].[jsGetPendingItemApiInsertions]
   ```
3. Did `PostItemsToSAPAsync` mark the item as processing?
   ```text
   [imc].[jsUpdateItemApiStatus] @tag = 'P'
   ```
4. Did SAP login work?
   ```text
   Bom2Service.GetSAPSessionOilAsync/BevAsync/MartAsync
   POST /Login
   ```
5. Inspect `imc.LogApiError` rows for:
   ```text
   ReferenceID = InitId
   ApiName = SAP/Items or SAP/Items/MART
   Payload
   ErrorMessage
   ErrorCode
   ```
6. Validate generated payload:
   ```text
   group code
   series
   chapter ID
   UDF names
   UoMGroupEntry
   sales/purchase/inventory flags
   company-specific FA/Packing fields
   ```
7. If primary SAP failed, DB approval is intentionally blocked.

## Approval Stuck

Check:

1. UI is passing flow ID, not init data ID.
2. `imc.jsGetItemCurrentStage` returns:
   ```text
   FlowId == request.itemId
   CurrentStage > 0
   TotalStage > 0
   CurrentStageId > 0
   Status != Invalid FlowId
   IsLastStage not null
   ```
3. If `IsLastStage=true`, ensure `jsGetPendingItemApiInsertions` returns rows.
4. If SAP failed, approval is blocked by design.
5. If `jsApproveItem` returns unexpected messages, inspect stage transition logic in the SP.
6. If notifications fail after approval, approval may still be done because notification sending happens after `jsApproveItem`.

## Duplicate Items Created in SAP

Current code has in-process locks and DB tag checks, but production duplicates can still happen if:

```text
multiple app instances run without a DB-level atomic guard
jsUpdateItemApiStatus does not atomically return previous tag and set P
SAP post succeeds but DB status update fails before tag Y is written
manual /Items endpoint and approval run on different servers
```

Debug:

1. Check `imc.jsUpdateItemApiStatus` implementation.
2. Search API status tag for InitId: `P`, `Y`, `N`, or exception text.
3. Inspect SAP item code/name duplicates.
4. Inspect app logs for `IMC SAP Condition` and `already created in SAP`.

## MART Sync Not Happening

MART sync runs only when:

```text
primary company is 1 or 2
primary SAP response succeeded
groupCode == 102 OR itemGroupName contains FINISHED or FG
```

Debug:

1. Confirm `PendingItemApiInsertionsModel.Company`.
2. Confirm `ItemGroupCode` string is exactly `102` or `itemGroupName` contains expected text.
3. Check `SapItemSyncResult.MartStatus`.
4. Inspect `imc.LogApiError` with `ApiName = SAP/Items/MART`.
5. Validate MART payload removes unsupported fields: `U_FA_Type`, `U_FA_TYPE`, `U_Packing_Type`.

## Notifications Not Sent

Create-time notifications:

```text
InsertFullItemDataAsync
  -> GetItemCurrentUsersSendNotificationAsync(NewInitId)
  -> [imc].[GetUsersInCurrentStage]
```

Approval notifications:

```text
ApproveItemAsync
  -> GetItemUserIdsSendNotificatiosAsync(flowId)
  -> [imc].[jsImcNotify]
```

Debug:

1. Check the SP returns user IDs.
2. Check `NotificationService.GetUserFcmTokenAsync(userId)`.
3. Check Firebase service account path: `Firebase:ServiceAccountPath`.
4. Check `nt.jsInsertNotification` through `NotificationService.InsertNotificationAsync`.
5. FCM send errors may be caught and appended to response message for create flow.

# Performance and Failure Points

| Risk | Why it matters | Where to inspect |
|---|---|---|
| HANA dropdown latency | Every dropdown hits SAP HANA procedures. | `Get*Async` methods and HANA procedure performance. |
| SQL stored procedure bottlenecks | Create/list/approval are SP-heavy. | `imc.jsInsertFullItemData`, `jsGetPendingItems`, `jsApproveItem`. |
| Final approval latency | Final approval synchronously calls SAP before DB approval. | `ApproveItemAsync`, `PostItemsToSAPAsync`. |
| SAP timeout/no retry | `HttpClient` has default timeout and no retry policy. | `client.PostAsync("Items", content)`. |
| Scale-out duplicate risk | In-memory semaphores do not protect across multiple app servers. | `_approvalLocks`, `_sapPostLocks`, `jsUpdateItemApiStatus`. |
| Session/company mismatch | Controller trusts request company/user IDs. | Controller, SP permissions, global auth filter. |
| Variety/SubGroup confusion | Explicit service swapping can hide DB mismatches. | `MapToDb`, `MapFromDb`, SAP payload mapping. |
| TLS validation disabled | SAP calls accept any cert. | `HttpClientHandler` in `ItemMasterService` and `Bom2Service`. |
| Reject remarks lost | `RejectItemModel.remarks` not passed to `jsRejectItem`. | `RejectItemAsync`. |
| Partial MART sync | Primary success can return true even if MART sync fails. | `SapItemSyncResult.MartStatus`, `imc.LogApiError`. |

# Developer Checklist

When changing IMC:

1. Keep DTO, SQL parameters, stored procedure signature, and SAP payload mapping aligned.
2. Re-test the `Variety`/`SubGroup` swap on create, list, detail, pending SAP rows, and SAP payload.
3. Verify final approval cannot approve DB if primary SAP creation fails.
4. Verify `jsUpdateItemApiStatus` is safe for retries and duplicate prevention.
5. Verify company-specific SAP UDFs before adding new fields.
6. Keep notifications non-blocking unless business requires rollback.
7. Add server-side permission enforcement if IMC must be restricted beyond authentication.
8. Do not rely on the external UI for user/company authorization.

