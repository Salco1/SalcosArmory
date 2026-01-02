using System.Collections.Generic;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;

namespace SalcosArmory;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public sealed class AddCustomTraderHelper(
    ICloner cloner,
    DatabaseService databaseService)
{
    public void SetTraderUpdateTime(TraderConfig traderConfig, TraderBase baseJson, int refreshTimeSecondsMin, int refreshTimeSecondsMax)
    {
        var traderRefreshRecord = new UpdateTime
        {
            TraderId = baseJson.Id,
            Seconds = new MinMax<int>(refreshTimeSecondsMin, refreshTimeSecondsMax)
        };

        traderConfig.UpdateTime.Add(traderRefreshRecord);
    }

    public void AddTraderWithEmptyAssortToDb(TraderBase traderDetailsToAdd)
    {
        var emptyTraderItemAssortObject = new TraderAssort
        {
            Items = [],
            BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(),
            LoyalLevelItems = new Dictionary<MongoId, int>()
        };

        var clonedBase = cloner.Clone(traderDetailsToAdd) ?? traderDetailsToAdd;

        var traderDataToAdd = new Trader
        {
            Assort = emptyTraderItemAssortObject,
            Base = clonedBase,
            QuestAssort = new()
            {
                { "Started", new() },
                { "Success", new() },
                { "Fail", new() }
            },
            Dialogue = []
        };

        databaseService.GetTables().Traders.TryAdd(traderDetailsToAdd.Id, traderDataToAdd);
    }

    public void AddTraderToLocales(TraderBase baseJson, string firstName, string description)
    {
        var newTraderId = baseJson.Id;
        if (string.IsNullOrWhiteSpace(newTraderId))
            return;

        var locales = databaseService.GetTables().Locales.Global;

        var fullName = baseJson.Name ?? "";
        var nickName = baseJson.Nickname ?? "";
        var location = baseJson.Location ?? "";

        var safeFirstName = firstName ?? "";
        var safeDescription = description ?? "";

        foreach (var (_, localeKvP) in locales)
        {
            localeKvP.AddTransformer(lazyloadedLocaleData =>
            {
                lazyloadedLocaleData ??= new Dictionary<string, string>();

                // Use indexer to avoid duplicate-key exceptions if transformer runs more than once
                lazyloadedLocaleData[$"{newTraderId} FullName"] = fullName;
                lazyloadedLocaleData[$"{newTraderId} FirstName"] = safeFirstName;
                lazyloadedLocaleData[$"{newTraderId} Nickname"] = nickName;
                lazyloadedLocaleData[$"{newTraderId} Location"] = location;
                lazyloadedLocaleData[$"{newTraderId} Description"] = safeDescription;

                return lazyloadedLocaleData;
            });
        }
    }

    public void OverwriteTraderAssort(string traderId, TraderAssort newAssorts)
    {
        if (!databaseService.GetTables().Traders.TryGetValue(traderId, out var traderToEdit))
            return;

        traderToEdit.Assort = newAssorts;
    }
}
