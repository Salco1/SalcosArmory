using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace SalcosArmory;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 3)]
public sealed class TraderOdin(
    ModHelper modHelper,
    ImageRouter imageRouter,
    ConfigServer configServer,
    TimeUtil timeUtil,
    AddCustomTraderHelper addCustomTraderHelper,
    ILogger<TraderOdin> logger
) : IOnLoad
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();

    public Task OnLoad()
    {
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var traderDir = Path.Combine(pathToMod, "TraderOdin");

        var traderImagePath = Path.Combine(traderDir, "Odin.png");
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(traderDir, "Base.json");

        imageRouter.AddRoute(traderBase.Avatar.Replace(".png", string.Empty), traderImagePath);

        addCustomTraderHelper.SetTraderUpdateTime(_traderConfig, traderBase, timeUtil.GetHoursAsSeconds(1), timeUtil.GetHoursAsSeconds(2));
        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        addCustomTraderHelper.AddTraderWithEmptyAssortToDb(traderBase);
        addCustomTraderHelper.AddTraderToLocales(traderBase, "Odin", "He is a former KSK elite soldier of the German Federal Armed Forces. He lost his right eye in combat, after which he was nicknamed Odin. His real name, origin, and age are unknown. He is an incredibly skilled marksman and weapons specialist. He is also an excellent gunsmith. He is more or less neutral towards all factions in Tarkov, but maintains a particularly good relationship with Mechanic, Prapor, and Sanitar.");

        var baseAssort = new JsonObject
        {
            ["items"] = new JsonArray(),
            ["barter_scheme"] = new JsonObject(),
            ["loyal_level_items"] = new JsonObject()
        };

        var mergedAssort = OdinAssortLoader.MergeAssortFromDataFolders(
            baseAssort,
            traderDir
        );

        var tmpAssortPath = Path.Combine(traderDir, "__merged_assort.tmp.json");
        System.IO.File.WriteAllText(tmpAssortPath, mergedAssort.ToJsonString(), new UTF8Encoding(false));

        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(traderDir, "__merged_assort.tmp.json");

        try
        {
            System.IO.File.Delete(tmpAssortPath);
        }
        catch
        {
        }

        addCustomTraderHelper.OverwriteTraderAssort(traderBase.Id, assort);


        return Task.CompletedTask;
    }
}