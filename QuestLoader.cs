using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Routers;
using WTTServerCommonLib;

namespace SalcosArmory;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 4)]
public sealed class SalcosArmoryQuestLoader(
    ModHelper modHelper,
    ImageRouter imageRouter,
    WTTServerCommonLib.WTTServerCommonLib wtt
) : IOnLoad
{
    public async Task OnLoad()
    {
        var asm = Assembly.GetExecutingAssembly();
        var modPath = modHelper.GetAbsolutePathToModFolder(asm);

        RegisterQuestIcons(modPath);

        await wtt.CustomQuestService.CreateCustomQuests(asm);
    }

    private void RegisterQuestIcons(string modPath)
    {
        var customQuestsRoot = Path.Combine(modPath, "db", "CustomQuests");
        if (!Directory.Exists(customQuestsRoot))
        {
            return;
        }

        var allowedExt = new[] { ".png", ".jpg", ".jpeg" };

        var files = Directory.EnumerateFiles(customQuestsRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => allowedExt.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => f.IndexOf($"{Path.DirectorySeparatorChar}Images{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        foreach (var file in files)
        {
            var fileNameNoExt = Path.GetFileNameWithoutExtension(file);
            var routeKey = $"/files/quest/icon/{fileNameNoExt}";

            try
            {
                imageRouter.AddRoute(routeKey, file);
            }
            catch
            {
                // Intentionally ignored (no logging requested).
            }
        }
    }
}
