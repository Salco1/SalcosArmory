using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using Range = SemanticVersioning.Range;
using Path = System.IO.Path;

namespace SalcosArmory;

public sealed record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.salco.salcosarmory";
    public override string Name { get; init; } = "Salco's Armory";
    public override string Author { get; init; } = "Salco";
    public override SemanticVersioning.Version Version { get; init; } = new("1.2.0");
    public override Range SptVersion { get; init; } = new("~4.0.0");
    public override string License { get; init; } = "MIT";
    public override bool? IsBundleMod { get; init; } = true;
    public override Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new Range("~2.0.0") }
    };
    public override string? Url { get; init; }
    public override List<string>? Contributors { get; init; }
    public override List<string>? Incompatibilities { get; init; }
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public sealed class SalcosArmoryMod(
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    DatabaseService databaseService,
    ILogger<SalcosArmoryMod> logger
) : IOnLoad
{
    public async Task OnLoad()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var itemFolders = new[]
        {
            "Weapons",
            "Ammo",
            "Attachments",
            "Items"
        };

        foreach (var folder in itemFolders)
        {
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join(folder));
        }

        await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(assembly, Path.Join("Recipes"));

        SalcosCompat.Apply(databaseService, assembly, logger);

        // This line will appear every time the mod has finished loading
        logger.LogInformation("[SALCO'S ARMORY successfully loaded]");
    }
}
