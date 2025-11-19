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
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override Range SptVersion { get; init; } = new("~4.0.0");
    public override string License { get; init; } = "MIT";
    public override bool? IsBundleMod { get; init; } = false;
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

        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join("Weapons"));
        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join("Ammo"));
        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join("Attachments"));
        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join("Items"));

        SalcosCompat.Apply(databaseService, assembly, logger);

        logger.LogInformation("[SALCO's Armory loaded successfully]");
    }
}
