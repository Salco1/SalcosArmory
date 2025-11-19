using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Path = System.IO.Path;

namespace SalcosArmory;

internal static class SalcosCompat
{
    public static void Apply(object? databaseService, Assembly assembly, ILogger logger)
    {
        if (databaseService is null)
        {
            return;
        }

        var baseDir = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return;
        }

        var weaponsDir = Path.Combine(baseDir, "Weapons");
        if (!Directory.Exists(weaponsDir))
        {
            return;
        }

        var compatById = LoadCompatFromWeapons(weaponsDir);
        if (compatById.Count == 0)
        {
            return;
        }

        var itemsDict = TryGetItemsDictionary(databaseService);
        if (itemsDict is null)
        {
            return;
        }

        ApplyWeaponCompat(itemsDict, compatById);
    }

    private static Dictionary<string, SalcosCompatConfig> LoadCompatFromWeapons(string weaponsDir)
    {
        var result = new Dictionary<string, SalcosCompatConfig>(StringComparer.Ordinal);
        var files = Directory.GetFiles(weaponsDir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var root = JsonSerializer.Deserialize<Dictionary<string, SalcosCompatItemWrapper>>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                if (root == null)
                {
                    continue;
                }

                foreach (var kvp in root)
                {
                    var id = kvp.Key;
                    var compat = kvp.Value?.SalcosCompat;

                    if (compat == null || compat.SlotOverrides == null || compat.SlotOverrides.Count == 0)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(id) || id.Length != 24)
                    {
                        continue;
                    }

                    result[id] = compat;
                }
            }
            catch
            {
                // ignore malformed files
            }
        }

        return result;
    }

    private static void ApplyWeaponCompat(IDictionary itemsDict, Dictionary<string, SalcosCompatConfig> compatById)
    {
        var itemsById = BuildItemMap(itemsDict);

        foreach (var kvp in compatById)
        {
            var tplId = kvp.Key;
            var compat = kvp.Value;

            if (!itemsById.TryGetValue(tplId, out var item))
            {
                continue;
            }

            if (compat.SlotOverrides == null || compat.SlotOverrides.Count == 0)
            {
                continue;
            }

            ApplySlotOverrides(item, compat.SlotOverrides);
        }
    }

    private static Dictionary<string, object> BuildItemMap(IDictionary itemsDict)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in itemsDict)
        {
            var keyStr = entry.Key as string;
            var item = entry.Value;
            if (item == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(keyStr))
            {
                result[keyStr] = item;
            }

            var idObj = GetMemberValue(item, "Id") ?? GetMemberValue(item, "_id");
            var idStr = idObj?.ToString();
            if (!string.IsNullOrWhiteSpace(idStr))
            {
                result[idStr] = item;
            }
        }

        return result;
    }

    private static IDictionary? TryGetItemsDictionary(object? source)
    {
        if (source is null)
        {
            return null;
        }

        var directItems = GetMemberValue(source, "Items") ?? GetMemberValue(source, "items");
        if (directItems is IDictionary directDict)
        {
            return directDict;
        }

        var templates = GetMemberValue(source, "Templates") ?? GetMemberValue(source, "templates");
        if (templates != null)
        {
            var templItems = GetMemberValue(templates, "Items") ?? GetMemberValue(templates, "items");
            if (templItems is IDictionary templDict)
            {
                return templDict;
            }
        }

        var tables = GetMemberValue(source, "Tables") ?? GetMemberValue(source, "tables");
        if (tables != null)
        {
            var templates2 = GetMemberValue(tables, "Templates") ?? GetMemberValue(tables, "templates");
            if (templates2 != null)
            {
                var templItems2 = GetMemberValue(templates2, "Items") ?? GetMemberValue(templates2, "items");
                if (templItems2 is IDictionary templDict2)
                {
                    return templDict2;
                }
            }
        }

        var tablesFromMethod = GetMemberValue(source, "GetTables") ?? GetMemberValue(source, "getTables");
        if (tablesFromMethod != null && !ReferenceEquals(tablesFromMethod, source))
        {
            return TryGetItemsDictionary(tablesFromMethod);
        }

        return null;
    }

    private static bool ApplySlotOverrides(object templateItem, List<SalcosSlotOverride> overrides)
    {
        if (overrides == null || overrides.Count == 0)
        {
            return false;
        }

        var props = GetMemberValue(templateItem, "Properties") ?? GetMemberValue(templateItem, "_props");
        if (props == null)
        {
            return false;
        }

        var slots = new List<object>();

        var slotsObj = GetMemberValue(props, "Slots") ?? GetMemberValue(props, "slots");
        if (slotsObj is IEnumerable slotsEnum && slotsObj is not string)
        {
            foreach (var s in slotsEnum)
            {
                if (s != null)
                {
                    slots.Add(s);
                }
            }
        }

        var chambersObj = GetMemberValue(props, "Chambers") ?? GetMemberValue(props, "chambers");
        if (chambersObj is IEnumerable chambersEnum && chambersObj is not string)
        {
            foreach (var c in chambersEnum)
            {
                if (c != null)
                {
                    slots.Add(c);
                }
            }
        }

        if (slots.Count == 0)
        {
            return false;
        }

        var changed = false;

        foreach (var ov in overrides)
        {
            if (ov == null || string.IsNullOrWhiteSpace(ov.SlotName) || ov.FilterTpls == null || ov.FilterTpls.Count == 0)
            {
                continue;
            }

            var slotNameTarget = ov.SlotName.Trim();

            var targetSlots = slots
                .Where(slot => string.Equals(GetSlotName(slot), slotNameTarget, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targetSlots.Count == 0)
            {
                var wantChamber = slotNameTarget.Contains("chamber", StringComparison.OrdinalIgnoreCase) ||
                                  slotNameTarget.Contains("patron", StringComparison.OrdinalIgnoreCase);
                var wantMag = slotNameTarget.Contains("mag", StringComparison.OrdinalIgnoreCase);

                foreach (var slot in slots)
                {
                    var name = GetSlotName(slot);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var lower = name.ToLowerInvariant();
                    if (wantMag && lower.Contains("mag"))
                    {
                        targetSlots.Add(slot);
                    }
                    else if (wantChamber && (lower.Contains("chamber") || lower.Contains("patron")))
                    {
                        targetSlots.Add(slot);
                    }
                }
            }

            if (targetSlots.Count == 0)
            {
                continue;
            }

            foreach (var slot in targetSlots)
            {
                if (ApplyToSingleSlot(slot, ov))
                {
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool ApplyToSingleSlot(object slot, SalcosSlotOverride ov)
    {
        var propsOwner = GetMemberValue(slot, "Properties") ??
                         GetMemberValue(slot, "properties") ??
                         GetMemberValue(slot, "_props") ??
                         GetMemberValue(slot, "Props") ??
                         GetMemberValue(slot, "props") ??
                         slot;

        var filtersObj = GetMemberValue(propsOwner, "Filters") ?? GetMemberValue(propsOwner, "filters");
        if (filtersObj is not IEnumerable filtersEnum || filtersObj is string)
        {
            return false;
        }

        object? firstFilter = null;
        foreach (var f in filtersEnum)
        {
            if (f != null)
            {
                firstFilter = f;
                break;
            }
        }

        IList? filtersList = null;
        if (filtersObj is IList list)
        {
            filtersList = list;
        }

        if (firstFilter == null)
        {
            if (filtersList == null)
            {
                return false;
            }

            var elementType = filtersList.GetType().IsGenericType
                ? filtersList.GetType().GetGenericArguments().FirstOrDefault() ?? typeof(object)
                : typeof(object);

            firstFilter = Activator.CreateInstance(elementType);
            if (firstFilter == null)
            {
                return false;
            }

            filtersList.Add(firstFilter);
        }

        var filterListObj = GetMemberValue(firstFilter, "Filter") ?? GetMemberValue(firstFilter, "filter");
        var filterList = EnsureStringList(firstFilter, "Filter", filterListObj);
        if (filterList == null)
        {
            return false;
        }

        filterList.Clear();
        foreach (var tpl in ov.FilterTpls!)
        {
            if (!string.IsNullOrWhiteSpace(tpl))
            {
                filterList.Add(tpl);
            }
        }

        if (ov.ClearExcludedFilter)
        {
            var excludedObj = GetMemberValue(firstFilter, "ExcludedFilter") ?? GetMemberValue(firstFilter, "excludedFilter");
            var excludedList = EnsureStringList(firstFilter, "ExcludedFilter", excludedObj);
            if (excludedList != null)
            {
                excludedList.Clear();
            }
        }

        return true;
    }

    private static object? GetMemberValue(object target, string name)
    {
        var type = target.GetType();

        var prop = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase
        );
        if (prop != null)
        {
            return SafeGet(() => prop.GetValue(target));
        }

        var field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase
        );
        if (field != null)
        {
            return SafeGet(() => field.GetValue(target));
        }

        var method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        if (method != null && method.GetParameters().Length == 0)
        {
            return SafeGet(() => method.Invoke(target, null));
        }

        return null;
    }

    private static string? GetSlotName(object slot)
    {
        var nameObj = GetMemberValue(slot, "Name") ??
                      GetMemberValue(slot, "_name") ??
                      GetMemberValue(slot, "SlotName");

        return nameObj?.ToString();
    }

    private static IList? EnsureStringList(object owner, string propName, object? current)
    {
        if (current is IList list)
        {
            return list;
        }

        var type = owner.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var pi = type.GetProperty(propName, flags);
        if (pi == null)
        {
            foreach (var p in type.GetProperties(flags))
            {
                if (string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase))
                {
                    pi = p;
                    break;
                }
            }
        }

        if (pi == null)
        {
            return null;
        }

        var listInstance = (IList?)Activator.CreateInstance(typeof(List<string>));
        if (listInstance == null)
        {
            return null;
        }

        SafeSet(() => pi.SetValue(owner, listInstance));
        return listInstance;
    }

    private static object? SafeGet(Func<object?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static void SafeSet(Action setter)
    {
        try
        {
            setter();
        }
        catch
        {
        }
    }
}

internal sealed class SalcosCompatItemWrapper
{
    [JsonPropertyName("salcosCompat")]
    public SalcosCompatConfig? SalcosCompat { get; set; }
}

internal sealed class SalcosCompatConfig
{
    [JsonPropertyName("slotOverrides")]
    public List<SalcosSlotOverride>? SlotOverrides { get; set; }
}

internal sealed class SalcosSlotOverride
{
    [JsonPropertyName("slotName")]
    public string? SlotName { get; set; }

    [JsonPropertyName("filterTpls")]
    public List<string>? FilterTpls { get; set; }

    [JsonPropertyName("clearExcludedFilter")]
    public bool ClearExcludedFilter { get; set; } = true;
}
