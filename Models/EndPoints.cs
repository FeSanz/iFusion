using System;
using System.Collections.Generic;
using System.Linq;

namespace iFusion.Models
{
    /// <summary>
    /// Endpoints de Fusion (misma convención que WOW Scales 3: plantillas con {0},{1}... y Constants.ParamsGet).
    /// Las rutas son relativas a /fscmRestApi/resources/latest, por eso sirven igual para GET y para partes de un batch.
    /// </summary>
    internal static class EndPoints
    {
        public static readonly string ParamsGet = "totalResults=true&onlyData=true&limit=500&links=canonical";

        /// <summary>Valor para usar dentro de q=: escapa comillas simples y codifica la URL (dejando la comilla legible).</summary>
        public static string Q(string value)
        {
            return Uri.EscapeDataString((value ?? string.Empty).Replace("'", "''")).Replace("%27", "'");
        }

        public static string InventoryOrganizations = "/inventoryOrganizations?" + ParamsGet +
            "&fields=OrganizationId,OrganizationCode,OrganizationName,LocationCode,ManagementBusinessUnitId,ManagementBusinessUnitName,InventoryFlag,ManufacturingPlantFlag,Status";

        public static string WorkCenters = "/workCenters?" + ParamsGet +
            "&fields=WorkCenterId,WorkCenterCode,WorkCenterName,WorkAreaId,WorkAreaCode,WorkAreaName" +
            "&q=OrganizationCode='{0}'";

        public static string ProductionResources = "/productionResources?" + ParamsGet +
            "&fields=ResourceId,ResourceCode,ResourceName,UOMCode,ResourceType,ResourceClassCode,Status" +
            "&finder=findByWorkCenterId;WorkCenterId={0}";

        // Manufactura por procesos. Se añaden q= y finder= según los filtros (ver WorkOrderProcessUrl).
        public static string WOProcess = "/processWorkOrders?" + ParamsGet +
            "&fields=WorkOrderId,WorkOrderNumber,OrganizationCode,WorkOrderType,SupplyTypeDescription,WorkOrderStatusCode,WorkOrderStatusName,WorkDefinitionId,WorkDefinitionCode,PrimaryProductId,ItemNumber,Description,PrimaryProductQuantity,CompletedQuantity,ScrappedQuantity,RejectedQuantity,PrimaryProductUOMCode,PlannedStartDate,PlannedCompletionDate,LastUpdateDate;" +
            "Operation:WorkCenterName,WorkOrderOperationId,OperationSequenceNumber,OperationName,ReadyQuantity,CompletedQuantity,ScrappedQuantity,RejectedQuantity,UnitOfMeasure,MaterialsCount,ResourcesCount;" +
            "ProcessWorkOrderMaterial:WorkOrderOperationMaterialId,OperationSequenceNumber,MaterialSequenceNumber,InventoryItemId,ItemNumber,ItemDescription,MaterialType,SupplySubinventory,Quantity,IssuedQuantity,UOMCode,SupplyType,SupplyTypeDescription;" +
            "ProcessWorkOrderResource:WorkOrderOperationResourceId,OperationSequenceNumber,ResourceSequenceNumber,ResourceId,ResourceCode,ResourceName,ResourceType,RequiredUsage,ActualResourceUsage,UOMCode,ChargeType;" +
            "ProcessWorkOrderOutput:WorkOrderOperationOutputId,OperationSequenceNumber,OutputSequenceNumber,InventoryItemId,ItemNumber,ItemDescription,PrimaryFlag,OutputType,OutputQuantity,CompletedQuantity,UOMCode,ComplSubinventoryCode,CompletionLocator,CompletionType;" +
            "ProcessWorkOrderDFF:pedidoDeVenta";

        // Manufactura discreta
        public static string WODiscrete = "/workOrders?" + ParamsGet + "&expand=all" +
            "&fields=WorkOrderId,WorkOrderNumber,OrganizationCode,WorkOrderType,WorkOrderSystemStatusCode,WorkOrderStatusCode,WorkOrderStatusName,WorkDefinitionId,WorkDefinitionCode,InventoryItemId,SupplyTypeDescription,CompletionSubinventoryCode,CompletionLocator,ItemNumber,Description,PlannedStartQuantity,CompletedQuantity,ScrappedQuantity,RejectedQuantity,UOMCode,PlannedStartDate,PlannedCompletionDate,LastUpdateDate;" +
            "WorkOrderOperation:WorkOrderOperationId,WorkCenterId,WorkCenterName,OperationSequenceNumber,OperationName,ReadyQuantity,CompletedQuantity,ScrappedQuantity,RejectedQuantity,UnitOfMeasure;" +
            "WorkOrderMaterial:WorkOrderOperationMaterialId,OperationSequenceNumber,MaterialSequenceNumber,InventoryItemId,ItemNumber,ItemDescription,SupplySubinventory,SupplyTypeDescription,Quantity,IssuedQuantity,UOMCode;" +
            "WorkOrderResource:WorkOrderOperationResourceId,OperationSequenceNumber,ResourceSequenceNumber,ResourceId,ResourceCode,ResourceName,ResourceType,RequiredUsage,ActualResourceUsage,UOMCode";

        // El AND de q= depende de la versión del framework: los GET/POST directos usan la 4 ("and", "in (...)") y los batch la 1 (";").
        // Por eso las rutas que viajan dentro de un batch se piden con batch: true.
        private const string AndV4 = " and ", AndV1 = ";";

        public static string ItemLot = "/itemsV2?" + ParamsGet +
            "&fields=ItemNumber,LotControlValue&q=ItemNumber='{0}'{2}OrganizationCode='{1}'";

        public static string ItemStock = "/itemStockingInquiries?" + ParamsGet +
            "&q=ItemNumber='{0}'{2}OrganizationCode='{1}'";

        public static string InventoryStagedTransactions = "/inventoryStagedTransactions";

        // Transacciones Miscellaneous Receipt que Oracle rechazó (ProcessStatus 3), las más recientes primero: traen el código y el motivo del error.
        // Se consulta con GET directo (framework 4).
        public static string StagedRejectsPath(string organizationId)
        {
            return "/inventoryStagedTransactions?onlyData=true&limit=20&orderBy=CreationDate:desc" +
                "&fields=ItemNumber,SubinventoryCode,ProcessStatus,ErrorCode,ErrorExplanation,CreationDate" +
                "&q=OrganizationId=" + Uri.EscapeDataString(organizationId) + AndV4 + "TransactionTypeId=42" + AndV4 + "ProcessStatus='3'";
        }

        // LotControlCode no es un campo de itemsV2 en esta instancia (HTTP 400); "Full lot control" en LotControlValue = con lote (código 2).
        public static string ItemLotPath(string item, string orgCode, bool batch = false) { return string.Format(ItemLot, Q(item), Q(orgCode), batch ? AndV1 : AndV4); }
        public static string ItemStockPath(string item, string orgCode, bool batch = false) { return string.Format(ItemStock, Q(item), Q(orgCode), batch ? AndV1 : AndV4); }

        // itemsV2 exige al menos 3 caracteres en ItemNumber (EGP-2776148): se busca por prefijo ("like 'PT %'"); no admite comodín al inicio.
        public static string FinishedGoodSearchPath(string orgCode, string prefix, bool finishedGoodOnly)
        {
            List<string> q = new List<string> { "ItemNumber like '" + prefix.Replace("'", "''") + "%'", "OrganizationCode='" + orgCode.Replace("'", "''") + "'" };
            if (finishedGoodOnly) q.Add("UserItemTypeValue='Finished Good'");
            return "/itemsV2?limit=100&onlyData=true&fields=ItemNumber,ItemDescription,UserItemTypeValue,PrimaryUOMValue&q=" +
                Uri.EscapeDataString(string.Join(AndV4, q)).Replace("%27", "'").Replace("%3D", "=");
        }

        // Datos maestros del artículo para la sección de Inventarios (nombres de campo tomados de la respuesta real de itemsV2).
        public const string ItemDetailFields =
            "ItemId,OrganizationCode,ItemClass,ItemNumber,ItemDescription,LongDescription,UserItemTypeValue,ItemStatusValue,ApprovalStatusValue,LifecyclePhaseValue,PrimaryUOMValue," +
            "ReservableFlag,StockEnabledFlag,MaterialTransactionEnabledFlag,CostingEnabledFlag,LotControlValue,StartingLotPrefix,StartingLotNumber,LotExpirationControlValue,ShelfLifeDays," +
            "SerialGenerationValue,SerialStartingPrefix,SerialStartingNumber,CycleCountEnabledFlag,StockLocatorControlValue,RestrictSubinventoriesFlag,RestrictLocatorsFlag," +
            "PlanningMakeBuyValue,Planner,ShippableFlag,PurchasableFlag,CreationDate,LastUpdateDate," +
            "UnitWeightQuantity,WeightUOMValue,UnitVolumeQuantity,VolumeUOMValue,UnitLengthQuantity,UnitWidthQuantity,UnitHeightQuantity,DimensionUOMValue," +
            "ContainerItemFlag,ContainerTypeValue,MaximumLoadWeight,InternalVolume,MinimumFillPercent,PackagingStringValue,PackTypeValue,VehicleItemFlag,WarehouseEquipmentFlag," +
            "ExpirationDateCalculationBasisValue,ExpirationActionValue,ExpirationActionInterval,MaturityDays,HoldDays,RetestInterval,GradeControlFlag,DefaultGradeValue,LotStatusEnabledFlag,DefaultLotStatusValue," +
            "LotDivisibleFlag,LotSplitEnabledFlag,LotMergeEnabledFlag,LotTranslateEnabledFlag,ChildLotEnabledFlag,RevisionQuantityControlFlag,SerialStatusEnabledFlag,PickingRuleValue,BulkPickedFlag," +
            "CheckMaterialShortageFlag,ShrinkageRate,MinimumOrderQuantity,MaximumOrderQuantity,FixedLotSizeMultiplier";

        public static string ItemDetailPath(string orgCode, string item)
        {
            return "/itemsV2?limit=1&onlyData=true&fields=" + ItemDetailFields + "&q=" +
                Uri.EscapeDataString("ItemNumber='" + item.Replace("'", "''") + "'" + AndV4 + "OrganizationCode='" + orgCode.Replace("'", "''") + "'").Replace("%27", "'").Replace("%3D", "=");
        }

        // Búsqueda de artículo por prefijo en todas las organizaciones (una fila por organización que lo tiene)
        public static string ItemSearchAllOrgsPath(string prefix)
        {
            return "/itemsV2?limit=500&onlyData=true&fields=ItemNumber,ItemDescription,OrganizationCode,OrganizationId&q=" +
                Uri.EscapeDataString("ItemNumber like '" + prefix.Replace("'", "''") + "%'").Replace("%27", "'").Replace("%3D", "=");
        }

        // Lotes del artículo con fecha de origen (FIFO) y de caducidad (FEFO)
        public static string ItemLotsInfoPath(string orgCode, string item)
        {
            return "/inventoryItemLots?limit=500&onlyData=true&fields=LotNumber,StatusCode,Grade,OriginationDate,ExpirationDate,MaturityDate,HoldUntilDate,RetestDate,ActiveLot&q=" +
                Uri.EscapeDataString("ItemNumber='" + item.Replace("'", "''") + "'" + AndV4 + "OrganizationCode='" + orgCode.Replace("'", "''") + "'").Replace("%27", "'").Replace("%3D", "=");
        }

        // Conteos cíclicos: cabeceras (definiciones) y secuencias de conteo. Las secuencias exigen nombre del conteo y organización.
        public const string CycleCountDefinitionsPath = "/cycleCountDefinitions?limit=500&onlyData=true";

        private const string CycleSeqFields =
            "CycleCountEntryId,CycleCountHeaderId,CycleCountName,OrganizationId,OrganizationCode,CountSequence,CountSequenceWithPrefix,InventoryItemId,ItemNumber,ItemDescription," +
            "Subinventory,Locator,LocatorId,LotNumber,SerialNumber,Revision,CountQuantity,CountUOM,CountUOMCode,PrimarySuggestedQuantity,PrimaryAdjustmentQuantity," +
            "CountSequenceStatus,CountSequenceStatusCode,CountType,CountDate,CountDueDate,CountedBy,Reason,Comments";

        public static string CycleSequencesPath(string countName, string orgCode, bool withFields)
        {
            return "/cycleCountSequenceDetails?limit=500" + (withFields ? "&fields=" + CycleSeqFields : string.Empty) + "&q=" +
                Uri.EscapeDataString("CycleCountName='" + countName.Replace("'", "''") + "'" + AndV4 + "OrganizationCode='" + orgCode.Replace("'", "''") + "'").Replace("%27", "'").Replace("%3D", "=");
        }

        // Existencia con cantidad reservada por subinventario / localizador / lote (varios niveles de resumen en la misma lista)
        public static string OnhandDetailsPath(string orgCode, string item)
        {
            return "/onhandQuantityDetails?limit=500&onlyData=true&q=" +
                Uri.EscapeDataString("ItemNumber='" + item.Replace("'", "''") + "'" + AndV4 + "OrganizationCode='" + orgCode.Replace("'", "''") + "'").Replace("%27", "'").Replace("%3D", "=");
        }

        // Definiciones de trabajo del artículo en la organización (ORA_MAIN, etc.)
        public static string WorkDefinitionsPath(string orgCode, string item)
        {
            return "/workDefinitions?limit=100&onlyData=true&fields=WorkDefinitionCode,WorkDefinitionName,WorkDefinitionType,VersionNumber,EffectiveToDate&q=" +
                Uri.EscapeDataString("OrganizationCode='" + orgCode.Replace("'", "''") + "'" + AndV4 + "ItemNumber='" + item.Replace("'", "''") + "'").Replace("%27", "'").Replace("%3D", "=");
        }

        public static string WorkCentersPath(string orgCode) { return string.Format(WorkCenters, Q(orgCode)); }

        /// <summary>Filtros de búsqueda de OT (todos opcionales salvo la organización).</summary>
        public class WorkOrderFilter
        {
            public string OrgCode;
            public string WorkOrderNumber;
            public string ItemNumber;
            public long? WorkCenterId;
            public List<string> Statuses = new List<string>();
        }

        /// <summary>URL de búsqueda de OT (GET directo, framework 4): filtros unidos con "and" y estatus con "in (...)", como en la especificación.</summary>
        public static string WorkOrderUrl(bool process, WorkOrderFilter f)
        {
            List<string> q = new List<string>();
            // Con número de OT no se filtra por organización (OrgCode vacío): la OT se busca en todas.
            if (!string.IsNullOrEmpty(f.OrgCode))
                q.Add(process ? "OrganizationCode in ('" + f.OrgCode.Replace("'", "''") + "')" : "OrganizationCode='" + f.OrgCode.Replace("'", "''") + "'");
            if (!string.IsNullOrEmpty(f.WorkOrderNumber)) q.Add("WorkOrderNumber='" + f.WorkOrderNumber.Replace("'", "''") + "'");
            if (!string.IsNullOrEmpty(f.ItemNumber)) q.Add("ItemNumber='" + f.ItemNumber.Replace("'", "''") + "'");
            if (f.Statuses.Count > 0) q.Add("WorkOrderStatusCode in (" + string.Join(",", f.Statuses.Select(s => "'" + s.Replace("'", "''") + "'")) + ")");
            if (!process && f.WorkCenterId.HasValue) q.Add("WorkOrderOperation.WorkCenterId=" + f.WorkCenterId.Value);

            string url = process ? WOProcess : WODiscrete;
            if (process && f.WorkCenterId.HasValue) url += "&finder=WorkOrderByWorkAreaWorkCenter;WorkCenterId=" + f.WorkCenterId.Value;
            // Cada valor ya escapó sus comillas; aquí solo se codifica la expresión completa (sin duplicar comillas).
            return url + "&q=" + Uri.EscapeDataString(string.Join(AndV4, q)).Replace("%27", "'").Replace("%3D", "=");
        }
    }
}
