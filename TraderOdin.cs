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

        addCustomTraderHelper.SetTraderUpdateTime(_traderConfig, traderBase, 1800, 3600);

        // --- Avatar routing (robust) ---
        // base.json: "avatar": "/files/trader/avatar/odin.png"
        var avatarUrl = traderBase.Avatar ?? "";
        var avatarFileName = GetAvatarFileNameFromUrl(avatarUrl);

        // You decided to use odin.png
        if (string.IsNullOrWhiteSpace(avatarFileName))
            avatarFileName = "odin.png";

        var avatarDiskPath = Path.Combine(traderDir, avatarFileName);

        // 1) Register exact key the client is requesting (your log shows this)
        //    Example: "/files/trader/avatar/odin.png"
        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            imageRouter.AddRoute(avatarUrl, avatarDiskPath);

            // 2) Also register without extension (pattern from your SalcosArmory overview)
            //    Example: "/files/trader/avatar/odin"
            var noExt = RemoveFileExtensionFromUrl(avatarUrl);
            if (!string.IsNullOrWhiteSpace(noExt) && !string.Equals(noExt, avatarUrl, StringComparison.Ordinal))
            {
                imageRouter.AddRoute(noExt, avatarDiskPath);
            }
        }

        // 3) Also register just the filename (some routers resolve this way)
        imageRouter.AddRoute(avatarFileName, avatarDiskPath);

        // 4) And as a last fallback, by traderId (some older implementations do this)
        imageRouter.AddRoute(traderBase.Id, avatarDiskPath);

        // --- Assort merge ---
        JsonObject mergedAssort = OdinAssortLoader.MergeAssortFromSplitFolders(traderDir);

        // Use ModHelper deserialization so SPT converters (MongoId etc.) are applied
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

        // Remove only the last extension segment
        var lastDot = avatarUrl.LastIndexOf('.');
        var lastSlash = avatarUrl.LastIndexOf('/');

        if (lastDot <= 0 || lastDot < lastSlash)
            return avatarUrl;

        return avatarUrl.Substring(0, lastDot);
    }
}