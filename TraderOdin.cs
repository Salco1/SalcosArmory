using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
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
    AddCustomTraderHelper addCustomTraderHelper
) : IOnLoad
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();

    public Task OnLoad()
    {
        _ = timeUtil;

        var assembly = Assembly.GetExecutingAssembly();
        var modRoot = modHelper.GetAbsolutePathToModFolder(assembly);
        var traderDir = Path.Combine(modRoot, "Trader");

        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(traderDir, "base.json");

        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        addCustomTraderHelper.AddTraderWithEmptyAssortToDb(traderBase);

        addCustomTraderHelper.AddTraderToLocales(
            traderBase,
            "Odin",
            "He is a former KSK elite soldier of the German Federal Armed Forces. Odin is known for his discipline, tactical expertise, and a no-nonsense attitude. He has a particularly good relationship with Mechanic, Prapor, and Sanitar."
        );

        addCustomTraderHelper.SetTraderUpdateTime(_traderConfig, traderBase, 7200, 7200);


        var avatarUrl = traderBase.Avatar ?? "";
        var avatarFileName = GetAvatarFileNameFromUrl(avatarUrl);


        if (string.IsNullOrWhiteSpace(avatarFileName))
            avatarFileName = "odin.png";

        var avatarDiskPath = Path.Combine(traderDir, avatarFileName);


        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            imageRouter.AddRoute(avatarUrl, avatarDiskPath);


            var noExt = RemoveFileExtensionFromUrl(avatarUrl);
            if (!string.IsNullOrWhiteSpace(noExt) && !string.Equals(noExt, avatarUrl, StringComparison.Ordinal))
            {
                imageRouter.AddRoute(noExt, avatarDiskPath);
            }
        }


        imageRouter.AddRoute(avatarFileName, avatarDiskPath);


        imageRouter.AddRoute(traderBase.Id, avatarDiskPath);


        JsonObject mergedAssort = OdinAssortLoader.MergeAssortFromSplitFolders(traderDir);


        var tmpFileName = "__merged_assort.tmp.json";
        var tmpPath = Path.Combine(traderDir, tmpFileName);

        try
        {
            File.WriteAllText(tmpPath, mergedAssort.ToJsonString());
            var assort = modHelper.GetJsonDataFromFile<TraderAssort>(traderDir, tmpFileName);
            addCustomTraderHelper.OverwriteTraderAssort(traderBase.Id, assort);
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch
            {
                // ignore temp cleanup errors
            }
        }

        return Task.CompletedTask;
    }

    private static string GetAvatarFileNameFromUrl(string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return "";

        var q = avatarUrl.IndexOf('?');
        if (q >= 0)
            avatarUrl = avatarUrl.Substring(0, q);

        avatarUrl = avatarUrl.Replace('\\', '/');

        var lastSlash = avatarUrl.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == avatarUrl.Length - 1)
            return "";

        return avatarUrl.Substring(lastSlash + 1);
    }

    private static string RemoveFileExtensionFromUrl(string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return "";

        var q = avatarUrl.IndexOf('?');
        if (q >= 0)
            avatarUrl = avatarUrl.Substring(0, q);

        avatarUrl = avatarUrl.Replace('\\', '/');


        var lastDot = avatarUrl.LastIndexOf('.');
        var lastSlash = avatarUrl.LastIndexOf('/');

        if (lastDot <= 0 || lastDot < lastSlash)
            return avatarUrl;

        return avatarUrl.Substring(0, lastDot);
    }
}