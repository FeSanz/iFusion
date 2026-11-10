# iFusion · Endpoints de Oracle Fusion Cloud utilizados

Documento de referencia de todas las llamadas REST que hace iFusion. Las rutas son relativas a
`https://{host}/fscmRestApi/resources/latest` (la instancia se captura solo como host, p. ej. `ehbm-test.fa.us2.oraclecloud.com`; la app antepone `https://`).

> **Origen de los ejemplos.** Los *responses* marcados como **real** se capturaron contra las instancias de prueba (Pinsa Test / Grupo WOW Test / Alvaco Test) con datos abreviados.
> Los marcados como **documentación Oracle** o **ilustrativo** no se ejecutaron contra Oracle (son escrituras que la app envía pero que no se probaron en vivo, o respuestas inferidas del esquema).
> Los nombres de campo pueden variar entre versiones de Fusion; ante un `HTTP 400` por un campo, revisar `last-request.log` (`%LocalAppData%\iFusion\last-request.log`) y la consola de la app.

## Índice

1. [Convenciones generales](#1-convenciones-generales)
2. [Catálogos](#2-catálogos): organizaciones, centros de trabajo
3. [Manufactura](#3-manufactura): OT de procesos, OT discretas, crear OT, definiciones de trabajo
4. [Inventarios · artículos y existencias](#4-inventarios--artículos-y-existencias)
5. [Incremento de stock (Miscellaneous Receipt)](#5-incremento-de-stock-miscellaneous-receipt)
6. [Conteos cíclicos](#6-conteos-cíclicos)
7. [Devoluciones de clientes (RMA)](#7-devoluciones-de-clientes-rma)
8. [Resumen](#8-resumen-de-endpoints)
9. [Errores frecuentes y soluciones](#9-errores-frecuentes-y-soluciones)

---

## 1. Convenciones generales

### 1.1 Autenticación y cabeceras

| Cabecera | Valor |
|---|---|
| `Authorization` | `Basic base64(usuario:contraseña/token)` |
| `REST-framework-version` | `4` para GET/POST/PATCH directos · `1` para el batch (`POST` a la raíz) |
| `Accept-Language` | `en-US` (los valores de lista como `Full lot control` o `Yes` vienen en inglés) |
| `Content-Type` | `application/json` (directos) · `application/vnd.oracle.adf.batch+json` (batch) |
| `Accept-Encoding` | `gzip` (la app descomprime la respuesta) |

Las credenciales se guardan cifradas con DPAPI en `%LocalAppData%\iFusion\instances.json`.

### 1.2 Sintaxis de filtros (`q=`) según la versión del framework

| | Framework **4** (GET/POST/PATCH directos) | Framework **1** (partes de un batch) |
|---|---|---|
| AND | `A='x' and B='y'` | `A='x';B='y'` |
| IN | `StatusCode in ('A','B')` | no funciona |
| Respuesta | hijos como `{ "items": [...], "count": n }` | hijos como arreglo `[...]` |

La expresión completa del `q=` se codifica **una sola vez** (`Uri.EscapeDataString`), dejando la comilla simple legible. Las comillas dentro del valor se duplican (`''`).

### 1.3 Parámetros comunes de lectura

`totalResults=true&onlyData=true&limit=500&links=canonical` · `fields=` para limitar columnas (un campo inexistente devuelve `400 · code 27519`) · `orderBy=Campo:desc` · `expand=all` solo en `workOrders`.

### 1.4 Formato del batch (`POST /` con framework 1)

Request:

```json
{
  "parts": [
    { "id": "1", "path": "/itemStockingInquiries?onlyData=true&q=ItemNumber='FB-00001';OrganizationCode='PLN31'", "operation": "get" },
    { "id": "2", "path": "/inventoryStagedTransactions", "operation": "create", "payload": { } }
  ]
}
```

Response (**real**, abreviado). Las partes `get` devuelven `payload` con `items`; un `create` exitoso devuelve **`"payload": null` sin `status`** (no es un rechazo: el éxito se confirma comparando existencias):

```json
{
  "parts": [
    { "id": "1", "path": ".../itemStockingInquiries", "operation": "get",
      "payload": { "items": [ { "ItemNumber": "FB-00001", "SubinventoryCode": "RESV MTEX", "AvailableToTransact": 20000 } ] } }
  ]
}
```

### 1.5 Formato de error de Oracle (**real**)

```json
{
  "title": "Bad Request",
  "status": "400",
  "o:errorDetails": [
    { "detail": "You must enter a Start Date attribute that's on or after 2026-11-10. (INV-2415595)",
      "o:errorCode": "INV:::INV_COUNT_DATE_GT_TDAY",
      "o:errorPath": "/StartDateTime" }
  ]
}
```

La app muestra el texto de `o:errorDetails[].detail` (el `title` solo dice "Bad Request").

---

## 2. Catálogos

### 2.1 Organizaciones de inventario

`GET /inventoryOrganizations`

```
?totalResults=true&onlyData=true&limit=500&links=canonical
&fields=OrganizationId,OrganizationCode,OrganizationName,LocationCode,ManagementBusinessUnitId,ManagementBusinessUnitName,InventoryFlag,ManufacturingPlantFlag,Status
```

Uso: lista de organizaciones. En **Manufactura** y en *Nueva OT* se filtran las de `ManufacturingPlantFlag = true`; en **Inventarios** y *Nuevo conteo* se usan todas con `InventoryFlag != false`.

Response (**real**, abreviado):

```json
{
  "items": [
    { "OrganizationId": 300000003173662, "OrganizationCode": "OI_PL1", "OrganizationName": "INVENTARIOS PLANTA 1",
      "LocationCode": "PLANTA 1", "ManagementBusinessUnitId": 300000003202100, "ManagementBusinessUnitName": "PLANTA_01",
      "InventoryFlag": true, "ManufacturingPlantFlag": true, "Status": "Active" }
  ]
}
```

### 2.2 Centros de trabajo

`GET /workCenters?...&fields=WorkCenterId,WorkCenterCode,WorkCenterName,WorkAreaId,WorkAreaCode,WorkAreaName&q=OrganizationCode='OI_PL1'`

Response (**real**):

```json
{ "items": [ { "WorkCenterId": 300000003225523, "WorkCenterCode": "INTSP01", "WorkCenterName": "INSTALACIONES",
               "WorkAreaId": 300000003225521, "WorkAreaCode": "INTSP01", "WorkAreaName": "INSTALACIONES" } ] }
```

---

## 3. Manufactura

### 3.1 OT de manufactura por procesos

`GET /processWorkOrders` (framework 4)

Filtros que arma la app (`q=`):

| Filtro | Expresión |
|---|---|
| Organización | `OrganizationCode in ('OI_PL1')` — **se omite si se busca por número de OT** |
| Número de OT | `WorkOrderNumber='1427'` |
| Artículo | `ItemNumber='PCR17-0560WSABAE'` |
| Estatus | `WorkOrderStatusCode in ('ORA_RELEASED','ORA_CLOSED')` |
| Centro de trabajo | `&finder=WorkOrderByWorkAreaWorkCenter;WorkCenterId={id}` |

Estatus: `ORA_UNRELEASED`, `ORA_RELEASED`, `ORA_ON_HOLD`, `ORA_COMPLETED`, `ORA_CLOSED`, `ORA_CANCELED`.

Campos pedidos: cabecera (`WorkOrderId, WorkOrderNumber, OrganizationCode, WorkOrderType, SupplyTypeDescription, WorkOrderStatusCode, WorkDefinitionCode, PrimaryProductId, ItemNumber, Description, PrimaryProductQuantity, CompletedQuantity, ScrappedQuantity, RejectedQuantity, PrimaryProductUOMCode, PlannedStartDate, PlannedCompletionDate, LastUpdateDate`) más hijos `Operation`, `ProcessWorkOrderMaterial`, `ProcessWorkOrderResource`, `ProcessWorkOrderOutput` (con `ComplSubinventoryCode`, `CompletionLocator`, `PrimaryFlag`) y `ProcessWorkOrderDFF:pedidoDeVenta`.

Ejemplo de URL (búsqueda por número):

```
GET /processWorkOrders?totalResults=true&onlyData=true&limit=500&links=canonical&fields=...&q=WorkOrderNumber%3D'1427'
```

Response (**real**, abreviado; OT 1427):

```json
{
  "items": [ {
    "WorkOrderNumber": "1427", "OrganizationCode": "OI_PL2", "WorkOrderType": "STANDARD",
    "WorkOrderStatusCode": "ORA_CLOSED", "WorkDefinitionCode": "PCRIST17-0560WSADJ00503A2",
    "PrimaryProductId": 100000003532679, "ItemNumber": "PCR17-0560WSABAE",
    "PrimaryProductQuantity": 3000, "CompletedQuantity": 2998.6, "PrimaryProductUOMCode": "kg",
    "SupplyTypeDescription": "Based on Work Definition",
    "Operation": { "items": [ { "OperationSequenceNumber": 10, "OperationName": "MF-CORTE GENERICO", "ReadyQuantity": 1.4, "CompletedQuantity": 2998.6 } ] },
    "ProcessWorkOrderMaterial": { "items": [ { "OperationSequenceNumber": 10, "MaterialSequenceNumber": 10, "ItemNumber": "PCR17-11…", "SupplySubinventory": "CPR", "Quantity": 3060, "IssuedQuantity": 3507.8, "UOMCode": "kg" } ] },
    "ProcessWorkOrderOutput": { "items": [ { "ItemNumber": "PCR17-0560WSABAE", "PrimaryFlag": true, "ComplSubinventoryCode": "CT", "CompletionLocator": null } ] }
  } ]
}
```

### 3.2 OT de manufactura discreta

`GET /workOrders?...&expand=all&fields=...` (framework 4)

| Filtro | Expresión |
|---|---|
| Organización | `OrganizationCode='PLN31'` (se omite con número de OT) |
| Número / Artículo / Estatus | igual que procesos |
| Centro de trabajo | `WorkOrderOperation.WorkCenterId={id}` |

Campos propios de discreta: `WorkOrderSystemStatusCode, InventoryItemId, PlannedStartQuantity, UOMCode, CompletionSubinventoryCode, CompletionLocator`, hijos `WorkOrderOperation`, `WorkOrderMaterial` (con `SupplyTypeDescription`) y `WorkOrderResource`.

Response (**real**, abreviado; OT WOT118):

```json
{
  "items": [ {
    "WorkOrderNumber": "WOT118", "OrganizationCode": "PLN31", "WorkOrderType": "STANDARD",
    "InventoryItemId": 100000001340530, "ItemNumber": "PT PEL-XTREM",
    "WorkDefinitionCode": "ORA_MAIN", "SupplyTypeDescription": "Based on Work Definition",
    "CompletionSubinventoryCode": "RECP PTPEL", "CompletionLocator": "RECEP PT.1.",
    "PlannedCompletionDate": "2026-10-23T18:39:12.556+00:00"
  } ]
}
```

### 3.3 Crear OT discreta liberada

`POST /workOrders` (framework 4, `Content-Type: application/json`)

Payload:

```json
{
  "OrganizationCode": "PLN31",
  "ItemNumber": "PT PEL-COLLE",
  "WorkOrderStatusCode": "ORA_RELEASED",
  "WorkDefinitionCode": "ORA_MAIN",
  "PlannedStartQuantity": 1400,
  "PlannedStartDate": "2026-10-16T12:00:00-05:00",
  "ExplosionFlag": true
}
```

`PlannedStartDate` se envía con la zona horaria local de la PC. Response: el registro creado (incluye `WorkOrderNumber` y `WorkOrderStatusCode`); la app muestra el número y lo busca al cerrar el formulario. *(Ilustrativo: no se probó la escritura en vivo.)*

### 3.4 Definiciones de trabajo de un artículo

`GET /workDefinitions?limit=100&onlyData=true&fields=WorkDefinitionCode,WorkDefinitionName,WorkDefinitionType,VersionNumber,EffectiveToDate&q=OrganizationCode='PLN31' and ItemNumber='PT PEL-COLLE'`

Response (**real**):

```json
{ "items": [ { "WorkDefinitionCode": "ORA_MAIN", "WorkDefinitionName": "Main", "WorkDefinitionType": "Standard",
               "ItemNumber": "PT PEL-COLLE", "VersionNumber": "1", "WorkDefinitionVersionId": 300000003717998, "EffectiveToDate": null } ] }
```

---

## 4. Inventarios · artículos y existencias

### 4.1 Búsqueda de artículo en todas las organizaciones

`GET /itemsV2?limit=500&onlyData=true&fields=ItemNumber,ItemDescription,OrganizationCode,OrganizationId&q=ItemNumber like 'PI64003%'`

- `itemsV2` exige **al menos 3 caracteres** en `ItemNumber` (`EGP-2776148`) y no admite comodín al inicio (`%PEL`).
- Devuelve una fila por organización que tiene el artículo (la app agrupa por número y lista las organizaciones).
- En *Nueva OT*, la variante filtrada: `ItemNumber like 'PT P%' and OrganizationCode='PLN31' and UserItemTypeValue='Finished Good'`.

Response (**real**):

```json
{ "items": [
  { "ItemNumber": "PI64003", "ItemDescription": "CAJA ATUN DOLORES ACEITE 48 - 130GR", "OrganizationCode": "PINSA_MASTER", "OrganizationId": 300000006352176 },
  { "ItemNumber": "PI64003", "OrganizationCode": "PPT" },
  { "ItemNumber": "PI64003", "OrganizationCode": "PPR" },
  { "ItemNumber": "PI64003", "OrganizationCode": "PCA" }
] }
```

### 4.2 Datos maestros del artículo (una organización)

`GET /itemsV2?limit=1&onlyData=true&fields={ItemDetailFields}&q=ItemNumber='PI64003' and OrganizationCode='PPT'`

Campos: identificación (`ItemId, ItemClass, ItemNumber, ItemDescription, LongDescription, UserItemTypeValue, ItemStatusValue, ApprovalStatusValue, LifecyclePhaseValue, PrimaryUOMValue`), control (`ReservableFlag, StockEnabledFlag, MaterialTransactionEnabledFlag, CostingEnabledFlag, StockLocatorControlValue, RestrictSubinventoriesFlag, RestrictLocatorsFlag`), lotes y series (`LotControlValue, StartingLotPrefix, StartingLotNumber, LotExpirationControlValue, ShelfLifeDays, SerialGenerationValue, SerialStartingPrefix, SerialStartingNumber, CycleCountEnabledFlag`), **reglas de rotación** (`ExpirationDateCalculationBasisValue, ExpirationActionValue, ExpirationActionInterval, MaturityDays, HoldDays, RetestInterval, GradeControlFlag, DefaultGradeValue, LotStatusEnabledFlag, DefaultLotStatusValue, LotDivisibleFlag, LotSplitEnabledFlag, LotMergeEnabledFlag, LotTranslateEnabledFlag, ChildLotEnabledFlag, RevisionQuantityControlFlag, SerialStatusEnabledFlag, PickingRuleValue, BulkPickedFlag, CheckMaterialShortageFlag, ShrinkageRate, MinimumOrderQuantity, MaximumOrderQuantity, FixedLotSizeMultiplier`), **atributos físicos** (`UnitWeightQuantity, WeightUOMValue, UnitVolumeQuantity, VolumeUOMValue, UnitLengthQuantity, UnitWidthQuantity, UnitHeightQuantity, DimensionUOMValue, ContainerItemFlag, ContainerTypeValue, MaximumLoadWeight, InternalVolume, MinimumFillPercent, PackagingStringValue, PackTypeValue, VehicleItemFlag, WarehouseEquipmentFlag`) y planeación (`PlanningMakeBuyValue, Planner, ShippableFlag, PurchasableFlag, CreationDate, LastUpdateDate`).

Response (**real**, abreviado):

```json
{ "items": [ {
  "ItemId": 100000611205363, "ItemClass": "Root Item Class", "ItemNumber": "PI64003",
  "UserItemTypeValue": "PRODUCTO TERMINADO LATA", "ItemStatusValue": "Activo", "PrimaryUOMValue": "CAJA",
  "LotControlValue": "Full lot control", "StartingLotPrefix": "L", "LotExpirationControlValue": "Item shelf life days", "ShelfLifeDays": 1460,
  "ReservableFlag": true, "StockLocatorControlValue": "Previously specified",
  "UnitWeightQuantity": null, "WeightUOMValue": null, "UnitVolumeQuantity": null, "ContainerItemFlag": null, "PackagingStringValue": null
} ] }
```

> Los **pallets / cajas por tarima** no vienen en `itemsV2`.

### 4.3 Control de lote de un artículo

`GET /itemsV2?...&fields=ItemNumber,LotControlValue&q=ItemNumber='X' and OrganizationCode='Y'` (también como parte de batch con `;`).

`LotControlCode` **no** es un campo válido en esta instancia (HTTP 400). `LotControlValue = "Full lot control"` ⇒ lleva lote (código 2). La app guarda el resultado en caché por instancia + organización + artículo. El batch suele tardar ~11 s en la primera consulta.

### 4.4 Existencias por subinventario / localizador / lote

`GET /itemStockingInquiries?...&q=ItemNumber='X' and OrganizationCode='Y'`
(para artículos con lote se consulta como parte de un **batch** con `;`).

Response (**real**):

```json
{ "items": [ {
  "OrganizationCode": "PPT", "InventoryItemId": 100000611205407, "ItemNumber": "PI64473",
  "SubinventoryCode": "PPT", "SubinventoryName": "PPT", "LocatorId": 300000261275845, "Locator": "D01--", "LotNumber": null,
  "PrimaryUOMCode": "CJ", "PrimaryUnitOfMeasure": "CAJA", "PrimaryOnhandQuantity": 78030, "AvailableToTransact": 78030,
  "LotControlCode": 2, "SerialNumberControlCode": 1
} ] }
```

La app suma por subinventario del material (Disponible = `AvailableToTransact`, Existencia = `PrimaryOnhandQuantity`). Los localizadores que se ofrecen al incrementar salen de este mismo recurso filtrando por organización y subinventario:
`GET /itemStockingInquiries?onlyData=true&limit=500&fields=Locator,LocatorId&q=OrganizationCode='PLN31' and SubinventoryCode='RESV MTEX'`.

### 4.5 Cantidades reservadas

`GET /onhandQuantityDetails?limit=500&onlyData=true&q=ItemNumber='PI64473' and OrganizationCode='PPT'`

Devuelve la existencia en varios niveles de resumen (organización, subinventario, localizador…). La app cruza por `SubinventoryCode|LocatorId|LotNumber` para mostrar `ReservedQuantity`.

Response (**real**, abreviado):

```json
{ "items": [
  { "SubinventoryCode": null, "LocatorId": null, "LotNumber": null, "OnhandQuantity": 78319, "ReservedQuantity": null, "PrimaryUOMCode": "CJ" },
  { "SubinventoryCode": "PPT", "LocatorId": 300000261275845, "LotNumber": null, "OnhandQuantity": 78030, "ReservedQuantity": null }
] }
```

### 4.5.1 Lotes del artículo (FIFO / LIFO / FEFO)

`GET /inventoryItemLots?limit=500&onlyData=true&fields=LotNumber,StatusCode,Grade,OriginationDate,ExpirationDate,MaturityDate,HoldUntilDate,RetestDate,ActiveLot&q=ItemNumber='PI64003' and OrganizationCode='PCA'`

Orden de consumo que calcula la app: **FIFO** = `OriginationDate` ascendente · **LIFO** = inverso de FIFO · **FEFO** = `ExpirationDate` ascendente.
Las fechas vienen a medianoche UTC (`…T00:00:00+00:00`) y se muestran en UTC.

Response (**real**):

```json
{ "items": [ { "LotNumber": "ERD530725", "StatusCode": "APV", "Grade": "OK", "ActiveLot": "Sí",
               "OriginationDate": "2025-11-03T22:38:50+00:00", "ExpirationDate": "2029-11-03T00:00:00+00:00",
               "MaturityDate": null, "HoldUntilDate": null, "RetestDate": null } ] }
```

---

## 5. Incremento de stock (Miscellaneous Receipt)

### 5.1 Enviar el receipt

`POST /` (batch, framework 1) · `operation: "create"` sobre `/inventoryStagedTransactions`, una parte por línea.

Payload (**real**, artículo con lote; los campos en negrita del texto son los que costó acertar):

```json
{
  "parts": [ {
    "id": "1",
    "path": "/inventoryStagedTransactions",
    "operation": "create",
    "payload": {
      "SourceCode": "SERVICE",
      "SourceHeaderId": 1,
      "SourceLineId": "1",
      "TransactionMode": "1",
      "OrganizationId": "300000003514334",
      "SubinventoryCode": "RESV MTEX",
      "LocatorName": "RESV MTEX.1.",
      "ItemNumber": "XTFSHW-00038",
      "TransactionTypeName": "Miscellaneous Receipt",
      "TransactionTypeId": 42,
      "TransactionQuantity": 1000.0,
      "TransactionUOM": "PZA",
      "TransactionDate": "2026-10-22",
      "UseCurrentCostFlag": "true",
      "TransactionReference": "Ingreso manual (1792729425)",
      "lots": [ { "LotNumber": "LOT1792729425", "TransactionQuantity": "1000" } ]
    }
  } ]
}
```

Reglas aprendidas:

- `TransactionUOM` es el **código** de la UOM (`PZA`, `KG`, `CJ`); `TransactionUnitOfMeasure` es el nombre y provoca `INV_INUOM_INVALID_UOM`.
- `LocatorName` (no `LocatorId`) es obligatorio cuando el subinventario exige localizador; sin él: `INV_INT_LOCSEGCODE · FLEX-NO DYNAMIC INSERTS`. Debe ser un localizador **existente**.
- Artículos con lote llevan el arreglo `lots` (la cantidad total es la suma). La app propone `LOT` + epoch en segundos y `Ingreso manual (epoch)` como referencia.
- `OrganizationId` se envía como texto.

Response (**real**): parte aceptada → `payload` vacío (`null`) o el registro de staging; **no hay `TransactionInterfaceId` ni error aunque Oracle lo rechace después**.

### 5.2 Verificar que Oracle lo aplicó

Oracle procesa el receipt de forma asíncrona. La app (a) compara la `PrimaryOnhandQuantity` del subinventario antes y después (consulta 4.4, hasta 6 intentos cada 3 s) y (b) revisa los rechazos:

`GET /inventoryStagedTransactions?onlyData=true&limit=20&orderBy=CreationDate:desc&fields=ItemNumber,SubinventoryCode,ProcessStatus,ErrorCode,ErrorExplanation,CreationDate&q=OrganizationId=300000003514334 and TransactionTypeId=42 and ProcessStatus='3'`

Response (**real**):

```json
{ "items": [ {
  "ItemNumber": "XTFSHW-00038", "SubinventoryCode": "RESV MTEX", "ProcessStatus": "3",
  "ErrorCode": "INV_INT_LOCSEGCODE", "ErrorExplanation": "FLEX-NO DYNAMIC INSERTS",
  "CreationDate": "2026-10-23T04:24:15.001+00:00"
} ] }
```

`ProcessStatus = "3"` ⇒ rechazada. Newtonsoft convierte `CreationDate` a fecha local: se compara como instante, no como texto.

---

## 6. Conteos cíclicos

### 6.1 Cabeceras (definiciones)

`GET /cycleCountDefinitions?limit=500&onlyData=true`

Response (**real**, abreviado):

```json
{ "items": [ {
  "CycleCountHeaderId": 300000585755860, "CycleCountName": "PRUEBA1", "Description": "PRUEBA1",
  "OrganizationId": 300000025203414, "OrganizationCode": "MEX", "OrganizationName": "MEXICO",
  "AutomaticallySchedule": "No", "WorkdaySchedule": "PICO_CAL", "ApprovalRequired": "No",
  "StartingCountSequence": 1, "MaximumDaysBeforeLate": 1, "StartDate": "2026-11-10",
  "SerializedItemsAllowed": "Yes", "SerialCountOption": "One", "IncludedSubinventoriesCount": 1,
  "CountByCategoryFlag": false, "CreatedBy": "nataly.pena@i-condor.com"
} ] }
```

### 6.2 Crear una cabecera

`POST /cycleCountDefinitions` (framework 4)

Payload (formato de la documentación Oracle; `countClasses` y `countItems` solo si la organización tiene clases ABC):

```json
{
  "CycleCountName": "BREST1",
  "Description": "TestPOST",
  "OrganizationCode": "M1",
  "AutomaticallySchedule": "No",
  "WorkdaySchedule": "APS Calendar",
  "ManualCountsAllowed": "Yes",
  "ApprovalRequired": "Yes",
  "ApprovalType": "Always",
  "MaximumDaysBeforeLate": 30,
  "StartDate": "2026-11-11",
  "ManualCountSequencePrefix": "MC",
  "CountZeroQuantity": "No",
  "DisplaySuggestedQuantity": "Yes",
  "SerializedItemsAllowed": "Yes",
  "SerialCountOption": "One",
  "SerialNumberAdjustment": "Adjust if possible",
  "RecordCountSerials": "No",
  "SerialDiscrepancyAllowed": "No",
  "StartingCountSequence": 1,
  "countSubinventories": [ { "Subinventory": "ABCSUB01", "IncludeInCount": "Yes" } ],
  "countClasses": [ { "AbcClass": "Class A", "CountsPerYear": 12 } ],
  "countItems": [ { "ItemNumber": "INV-100", "AbcClass": "Class A", "IncludeInSchedule": "Yes" } ]
}
```

Opcionales que la app agrega si se capturan: `AbcAssigmentGroup` (sic, así se llama), `SynchronizationMode`.

Reglas aprendidas (errores **reales**):

- `StartDate` debe ser **posterior a hoy**: `INV_COUNT_DATE_GT_TDAY` ("on or after" mañana).
- Un artículo/clase exige una **clase ABC válida de la organización**: `INV-2415484 "Provide a valid value for AbcClassId"`. Pinsa Test no tiene clases ABC en PPT ni MEX.
- La app valida lo anterior antes de enviar y consulta `GET /abcClasses?limit=100&onlyData=true&q=OrganizationCode='PPT'` (requiere `OrganizationCode` u `OrganizationId`).

Subinventarios de la organización (para marcar cuáles contar):
`GET /subinventories?limit=500&onlyData=true&q=OrganizationId=300000255252357` → campos `SecondaryInventoryName`, `Description`.

Response (documentación Oracle, abreviado): el registro creado con `CycleCountHeaderId` y los hijos `countSubinventories` (`IncludeInCountCode: 1`), `countClasses`, `countItems`.

### 6.3 Tareas (secuencias) de un conteo

`GET /cycleCountSequenceDetails?limit=500&fields={...}&q=CycleCountName='PRUEBA1' and OrganizationCode='MEX'`

- **Exige** `CycleCountName` y `OrganizationCode`/`OrganizationId` en el `q` (error `27036` si falta).
- Campos pedidos: `CycleCountEntryId, CycleCountHeaderId, CycleCountName, OrganizationId, OrganizationCode, CountSequence, CountSequenceWithPrefix, InventoryItemId, ItemNumber, ItemDescription, Subinventory, Locator, LocatorId, LotNumber, SerialNumber, Revision, CountQuantity, CountUOM, CountUOMCode, PrimarySuggestedQuantity, PrimaryAdjustmentQuantity, CountSequenceStatus, CountSequenceStatusCode, CountType, CountDate, CountDueDate, CountedBy, Reason, Comments`. Si algún campo no existe, la app repite la consulta sin `fields`.
- No se usa `onlyData`: hace falta el arreglo `links` de cada tarea (`rel: self`) para el PATCH.

Response (**real**, vacío — el conteo de prueba no tenía tareas abiertas):

```json
{ "items": [], "totalResults": 0, "count": 0, "hasMore": false, "limit": 2, "offset": 0 }
```

Response esperado por tarea (documentación Oracle, ilustrativo):

```json
{ "items": [ {
  "CycleCountEntryId": 300200, "CycleCountHeaderId": 300100, "CycleCountName": "WEEKLY-A", "CountSequence": 1001,
  "ItemNumber": "AS54888", "Subinventory": "STORES", "Locator": "STORES.A.01", "LotNumber": null,
  "CountQuantity": null, "CountUOMCode": "EA", "PrimarySuggestedQuantity": 42, "CountSequenceStatus": "Uncounted",
  "links": [ { "rel": "self", "href": "https://…/cycleCountSequenceDetails/{id}" } ]
} ] }
```

### 6.4 Registrar la cantidad contada

`PATCH /cycleCountSequenceDetails/{id}` (la ruta se toma del `links[rel=self]` de la tarea)

```json
{ "CountQuantity": 42, "CountDate": "2026-11-10", "CountUOMCode": "EA" }
```

### 6.5 Enviar conteos como transacción directa

`POST /cycleCountTransactions` (framework 4)

> El recurso se llama **`cycleCountTransactions`**; `inventoryCycleCountTransactions` responde 404. Un `GET` devuelve 403, solo admite `POST`.

```json
{
  "countLines": [
    {
      "CountName": "WEEKLY-A",
      "CountSequence": 1001,
      "CycleCountHeaderId": 300100,
      "CycleCountEntryId": 300200,
      "InventoryItemId": 400300,
      "Item": "AS54888",
      "OrganizationCode": "M1",
      "OrganizationId": 204,
      "Subinventory": "STORES",
      "LocatorId": 5001,
      "LocatorName": "STORES.A.01",
      "Lot": "LOT1",
      "CountQuantity": 42,
      "CountUnitOfMeasure": "Each",
      "CountUOM": "EA",
      "CountDate": "2026-11-10"
    }
  ]
}
```

Response (documentación Oracle, ilustrativo): éxito → `ErrorCode` vacío:

```json
{ "ReturnStatus": "SUCCESS", "ErrorCode": "", "ErrorExplanation": "", "ErrorUniqueTransactionId": "",
  "countLines": [ { "CountSequence": 1001, "CountQuantity": 42, "UniqueTransactionId": 987654321 } ] }
```

Si `ErrorCode` viene lleno, la app muestra `ErrorCode · ErrorExplanation`.

---

## 7. Devoluciones de clientes (RMA)

### 7.1 Buscar órdenes de devolución

`GET /salesOrdersForOrderHub?limit=200&totalResults=true&onlyData=true&orderBy=SubmittedDate:desc&fields={...}&q=...`

Campos: `HeaderId, OrderNumber, SourceTransactionSystem, BusinessUnitName, BuyingPartyName, BuyingPartyNumber, TransactionTypeCode, StatusCode, Status, SubmittedDate, RequestedFulfillmentOrganizationCode, TransactionalCurrencyCode, CustomerPONumber`.

Filtros (`q=`):

| Filtro | Expresión |
|---|---|
| Número de orden | `OrderNumber='2821'` |
| Tipo de orden (prefijo, por omisión `DEV`) | `TransactionTypeCode like 'DEV%'` |
| Abiertas | `StatusCode<>'CLOSED' and StatusCode<>'CANCELED'` |
| Cerradas | `StatusCode='CLOSED'` |

Notas: en esta instancia las devoluciones se llaman `DEV_SAR_ENL_SEP_NAC` (no `RMA`), por eso el prefijo es editable. `BuyingPartyName` **no** es filtrable en `q=` (se filtra en la app sobre los resultados). `fields=` con nombres no válidos (p. ej. `OrderTypeCode`) da 400. `expand=none` no es válido aquí.

Response (**real**):

```json
{ "items": [ { "OrderNumber": "2821", "StatusCode": "OPEN", "BuyingPartyName": "PINSA COMERCIAL SA DE CV" } ],
  "totalResults": 26, "count": 3, "hasMore": true }
```

### 7.2 Líneas de la orden

`GET /salesOrdersForOrderHub/{HeaderId}/child/lines?limit=200&onlyData=true&fields=LineId,DisplayLineNumber,SourceTransactionLineNumber,TransactionCategoryCode,ProductNumber,ProductDescription,OrderedQuantity,OrderedUOMCode,RMADeliveredQuantity,FulfilledQuantity,ReturnReason,ReturnReasonCode,StatusCode,Status,RequestedFulfillmentOrganizationCode,TransactionLineType`

La app muestra solo las líneas con `TransactionCategoryCode = "RETURN"`. **Pendiente = `OrderedQuantity − RMADeliveredQuantity`.**

Response (**real**):

```json
{ "items": [ {
  "LineId": 300000333502550, "DisplayLineNumber": "1", "SourceTransactionLineNumber": "1", "TransactionCategoryCode": "RETURN",
  "ProductNumber": "04505", "ProductDescription": "DOLORES OVAL SARDINES IN TOMATO SOUCE 24/425 G (15 OZ)",
  "OrderedQuantity": 1440, "OrderedUOMCode": "CJ", "RMADeliveredQuantity": 1440, "FulfilledQuantity": 1440,
  "ReturnReason": "Reason code not provided", "ReturnReasonCode": "NCP", "StatusCode": "CLOSED",
  "RequestedFulfillmentOrganizationCode": "SEP", "TransactionLineType": "Return for credit and return the item"
} ] }
```

### 7.3 Recibir la devolución

`POST /receivingReceiptRequests` (framework 4)

Payload (campos de la documentación Oracle; **no se probó la escritura en vivo**):

```json
{
  "ReceiptSourceCode": "CUSTOMER",
  "CustomerPartyName": "PINSA COMERCIAL SA DE CV",
  "CustomerPartyNumber": "25",
  "RMASourceSystemName": "OPS",
  "OrganizationCode": "SEP",
  "BusinessUnit": "SARSON_ENLATADORA",
  "EmployeeName": "Nombre del empleado en Oracle",
  "TransactionDate": "2026-11-10T10:00:00-07:00",
  "InsertAndProcessFlag": true,
  "Comments": "Devolución cliente",
  "lines": [
    {
      "ReceiptSourceCode": "CUSTOMER",
      "SourceDocumentCode": "RMA",
      "TransactionType": "RECEIVE",
      "RMANumber": "2821",
      "RMALineNumber": "1",
      "OrganizationCode": "SEP",
      "ItemNumber": "04501.",
      "Quantity": 500,
      "UOMCode": "CJ",
      "Subinventory": "PT",
      "Locator": "PT.1.",
      "lotItemLots": [ { "LotNumber": "L123", "TransactionQuantity": 500 } ]
    }
  ]
}
```

Notas:

- `EmployeeId` o `EmployeeName` es obligatorio en la cabecera; `RMANumber` + `RMALineNumber` son obligatorios en la línea.
- `lotItemLots` / `LotNumber` / `TransactionQuantity` son el nombre esperado para artículos con lote; **no se confirmó el esquema** (si Oracle lo rechaza el error sale en la consola).
- No se sabe si `RECEIVE` + `Subinventory` entrega al stock o deja el material en el muelle; verificar existencias con la primera recepción real.

Response (documentación Oracle, ilustrativo): trae `HeaderInterfaceId`, `ProcessingStatusCode` (`PENDING`) y, al procesarse, `ReceiptNumber`.

### 7.4 Confirmar el procesamiento

`GET /receivingReceiptRequests?onlyData=true&limit=1&q=HeaderInterfaceId=5911302`

La app lee `ProcessingStatusCode` (hasta 5 intentos cada 3 s): `PENDING`/`RUNNING` ⇒ sigue en proceso · `ERROR`/`REJECTED` ⇒ rechazada · otro ⇒ registrada. Fragmento **real** de un registro existente:

```json
{ "HeaderInterfaceId": 5911302, "ASNType": "STD", "ReceiptSourceCode": "TRANSFER ORDER", "ShipmentNumber": "8925024", "ReceiptNumber": null }
```

### 7.5 Endpoints de la lista original no implementados

| Recurso | Estado |
|---|---|
| `POST /salesOrdersForOrderHub` (crear RMA) | No implementado |
| `GET /receivingReceipts` | Existe; exige `ShipmentHeaderId` o `ReceiptNumber` en el filtro. No implementado |
| `POST /receivingInspections` | `404` en Pinsa Test |
| `POST /creditMemos` | `404` en Pinsa Test; existe `GET /receivablesInvoices` |

---

## 8. Resumen de endpoints

| # | Método | Recurso | Módulo |
|---|---|---|---|
| 1 | GET | `/inventoryOrganizations` | Catálogos |
| 2 | GET | `/workCenters` | Manufactura |
| 3 | GET | `/processWorkOrders` | Manufactura (procesos) |
| 4 | GET | `/workOrders` | Manufactura (discreta) |
| 5 | POST | `/workOrders` | Nueva OT liberada |
| 6 | GET | `/workDefinitions` | Nueva OT liberada |
| 7 | GET | `/itemsV2` | Artículos / lotes / Nueva OT |
| 8 | GET | `/itemStockingInquiries` | Existencias, localizadores |
| 9 | GET | `/onhandQuantityDetails` | Reservado |
| 10 | GET | `/inventoryItemLots` | FIFO / LIFO / FEFO |
| 11 | POST (batch) | `/inventoryStagedTransactions` | Incrementar stock |
| 12 | GET | `/inventoryStagedTransactions` | Rechazos del receipt |
| 13 | GET / POST | `/cycleCountDefinitions` | Conteos cíclicos |
| 14 | GET | `/subinventories`, `/abcClasses` | Alta de conteo |
| 15 | GET / PATCH | `/cycleCountSequenceDetails` | Tareas de conteo |
| 16 | POST | `/cycleCountTransactions` | Conteo como transacción |
| 17 | GET | `/salesOrdersForOrderHub` (+ `/child/lines`) | RMA |
| 18 | POST / GET | `/receivingReceiptRequests` | RMA · recepción |

---

## 9. Errores frecuentes y soluciones

| Síntoma | Causa | Solución |
|---|---|---|
| Búsqueda de OT sin resultados | `;` en vez de ` and ` en el `q` directo | Usar framework 4 con ` and ` (ver 1.2) |
| `HTTP 400` con texto ilegible | Respuesta gzip | La app descomprime (`AutomaticDecompression`) |
| `27519 … fields … is not valid` | Campo inexistente en esa versión/recurso | Quitar el campo; revisar `last-request.log` |
| `27036 At least one of the following attributes … is required` | Recurso que exige filtro (`cycleCountSequenceDetails`, `abcClasses`, `receivingReceipts`) | Incluir el atributo en `q` |
| `EGP-2776148` | `itemsV2` con menos de 3 caracteres en `ItemNumber` | Pedir ≥3 caracteres, buscar por prefijo |
| `INV_INUOM_INVALID_UOM` | `TransactionUnitOfMeasure` con un código | Usar `TransactionUOM` |
| `INV_INT_LOCSEGCODE · FLEX-NO DYNAMIC INSERTS` | Subinventario exige localizador / localizador inexistente | Enviar `LocatorName` de uno existente |
| Batch `create` con `payload: null` | Es una parte **aceptada**, no un rechazo | Confirmar por existencias y por `ProcessStatus='3'` |
| `INV_COUNT_DATE_GT_TDAY` | Fecha de inicio de conteo = hoy | Usar mañana o posterior |
| `INV-2415484 AbcClassId` | Clase ABC inexistente en la organización | Crear clases ABC en Oracle o no mandar clases/artículos |
| `401` | Contraseña/token vencido | Actualizar la instancia en *Instancias* |
