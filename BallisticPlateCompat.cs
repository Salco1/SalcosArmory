using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Services;
using Path = System.IO.Path;

namespace SalcosArmory;

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 2)]
public sealed class BallisticPlateCompat(DatabaseService databaseService) : IOnLoad
{
    private static readonly Regex Hex24 = new(@"[0-9a-fA-F]{24}", RegexOptions.Compiled);

    public Task OnLoad()
    {
        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrWhiteSpace(baseDir))
            return Task.CompletedTask;

        var configPath = Path.Combine(baseDir, "Config", "BallisticPlateCompat.jsonc");
        var config = LoadConfig(configPath);
        if (config.Mappings.Count == 0)
            return Task.CompletedTask;

        var itemsDict = TryGetItemsDictionary(databaseService.GetTables());
        if (itemsDict is null)
            return Task.CompletedTask;

        foreach (var m in config.Mappings)
        {
            var source = NormalizeToHex24(m.SourcePlateTpl);
            if (string.IsNullOrWhiteSpace(source))
                continue;

            var clones = (m.ClonePlateTpls ?? new List<string>())
                .Select(NormalizeToHex24)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (clones.Count == 0)
                continue;

            PatchAllItemsThatAcceptSourcePlate(itemsDict, source!, clones!);
        }

        return Task.CompletedTask;
    }

    private static void PatchAllItemsThatAcceptSourcePlate(
        IDictionary itemsDict,
        string sourcePlateTplHex,
        List<string> clonePlateTplHexes)
    {
        foreach (DictionaryEntry entry in itemsDict)
        {
            var templateItem = entry.Value;
            if (templateItem is null)
                continue;

            var props = GetMemberValue(templateItem, "Properties") ?? GetMemberValue(templateItem, "_props") ?? GetMemberValue(templateItem, "props");
            if (props is null)
                continue;

            var slotsAll = new List<object>();
            slotsAll.AddRange(CollectEnumerableElements(GetMemberValue(props, "Slots") ?? GetMemberValue(props, "slots")));
            slotsAll.AddRange(CollectEnumerableElements(GetMemberValue(props, "PlateSlots") ?? GetMemberValue(props, "plateSlots")));
            if (slotsAll.Count == 0)
                continue;

            foreach (var slotRaw in slotsAll)
            {
                var slot = Unwrap(slotRaw);
                if (slot is null)
                    continue;

                var propsOwner = GetMemberValue(slot, "Properties") ??
                                 GetMemberValue(slot, "properties") ??
                                 GetMemberValue(slot, "_props") ??
                                 GetMemberValue(slot, "Props") ??
                                 GetMemberValue(slot, "props") ??
                                 slot;

                var filtersObj = GetMemberValue(propsOwner, "Filters") ?? GetMemberValue(propsOwner, "filters");
                var filters = CollectEnumerableElements(filtersObj);
                if (filters.Count == 0)
                    continue;

                foreach (var filterRaw in filters)
                {
                    var filter = Unwrap(filterRaw);
                    if (filter is null)
                        continue;

                    var filterSetObj = GetMemberValue(filter, "Filter") ?? GetMemberValue(filter, "filter");
                    if (filterSetObj is null)
                        continue;

                    if (!TryAsEnumerable(filterSetObj, out var filterEnumerable))
                        continue;

                    if (!EnumerableContainsHex24(filterEnumerable, sourcePlateTplHex))
                        continue;

                    foreach (var cloneHex in clonePlateTplHexes)
                    {
                        if (EnumerableContainsHex24(filterEnumerable, cloneHex))
                            continue;

                        TryAddToCollection(filterSetObj, cloneHex);
                    }
                }
            }
        }
    }

    private static bool TryAsEnumerable(object obj, out IEnumerable enumerable)
    {
        if (obj is string)
        {
            enumerable = Array.Empty<object>();
            return false;
        }

        if (obj is IEnumerable en)
        {
            enumerable = en;
            return true;
        }

        enumerable = Array.Empty<object>();
        return false;
    }

    private static bool EnumerableContainsHex24(IEnumerable en, string wantedHex24)
    {
        foreach (var el in en)
        {
            var norm = NormalizeToHex24(NormalizeTpl(el));
            if (!string.IsNullOrWhiteSpace(norm) &&
                string.Equals(norm, wantedHex24, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryAddToCollection(object collection, string tplHex24)
    {
        try
        {
            if (collection is IList list)
            {
                list.Add(tplHex24);
                return true;
            }

            var colType = collection.GetType();
            var elementType = GetGenericElementType(colType);

            if (elementType == null)
            {
                var addString = colType.GetMethod("Add", new[] { typeof(string) });
                if (addString != null)
                {
                    addString.Invoke(collection, new object[] { tplHex24 });
                    return true;
                }
                return false;
            }

            var element = CreateElement(elementType, tplHex24);
            if (element == null)
                return false;

            var add = colType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "Add" &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsAssignableFrom(elementType));

            add ??= colType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);

            if (add == null)
                return false;

            add.Invoke(collection, new[] { element });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Type? GetGenericElementType(Type t)
    {
        if (t.IsGenericType)
        {
            var ga = t.GetGenericArguments();
            if (ga.Length == 1)
                return ga[0];
        }

        var ie = t.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return ie?.GetGenericArguments().FirstOrDefault();
    }

    private static object? CreateElement(Type elementType, string tplHex24)
    {
        if (elementType == typeof(string) || elementType == typeof(object))
            return tplHex24;

        var ctor = elementType.GetConstructor(new[] { typeof(string) });
        if (ctor != null)
            return ctor.Invoke(new object[] { tplHex24 });

        var inst = Activator.CreateInstance(elementType);
        if (inst != null)
        {
            var valueProp = elementType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (valueProp != null && valueProp.CanWrite)
            {
                valueProp.SetValue(inst, tplHex24);
                return inst;
            }
        }

        return null;
    }

    private static List<object> CollectEnumerableElements(object? enumerableObj)
    {
        var result = new List<object>();
        if (enumerableObj is null || enumerableObj is string)
            return result;

        if (enumerableObj is IEnumerable en)
        {
            foreach (var el in en)
            {
                var u = Unwrap(el);
                if (u != null)
                    result.Add(u);
            }
        }

        return result;
    }

    private static object? Unwrap(object? el)
    {
        if (el is null)
            return null;

        if (el is DictionaryEntry de)
            return de.Value;

        var t = el.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            var vp = t.GetProperty("Value");
            if (vp != null)
            {
                try { return vp.GetValue(el); } catch { return null; }
            }
        }

        return el;
    }

    private static string? NormalizeTpl(object? obj)
    {
        if (obj is null)
            return null;

        if (obj is string s)
            return s;

        var type = obj.GetType();
        var valueProp = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
        if (valueProp != null)
        {
            try { return valueProp.GetValue(obj)?.ToString(); } catch { return null; }
        }

        return obj.ToString();
    }

    private static string? NormalizeToHex24(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var m = Hex24.Match(input);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private static IDictionary? TryGetItemsDictionary(object? source)
    {
        if (source is null)
            return null;

        var directItems = GetMemberValue(source, "Items") ?? GetMemberValue(source, "items");
        if (directItems is IDictionary directDict)
            return directDict;

        var templates = GetMemberValue(source, "Templates") ?? GetMemberValue(source, "templates");
        if (templates != null)
        {
            var templItems = GetMemberValue(templates, "Items") ?? GetMemberValue(templates, "items");
            if (templItems is IDictionary templDict)
                return templDict;
        }

        var tables = GetMemberValue(source, "Tables") ?? GetMemberValue(source, "tables");
        if (tables != null)
        {
            var templates2 = GetMemberValue(tables, "Templates") ?? GetMemberValue(tables, "templates");
            if (templates2 != null)
            {
                var templItems2 = GetMemberValue(templates2, "Items") ?? GetMemberValue(templates2, "items");
                if (templItems2 is IDictionary templDict2)
                    return templDict2;
            }
        }

        return null;
    }

    private static object? GetMemberValue(object target, string name)
    {
        var type = target.GetType();

        var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
        if (prop != null)
        {
            try { return prop.GetValue(target); } catch { return null; }
        }

        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
        if (field != null)
        {
            try { return field.GetValue(target); } catch { return null; }
        }

        return null;
    }

    private static BallisticPlateCompatConfig LoadConfig(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return new BallisticPlateCompatConfig();

            var jsonOptions = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };

            using var stream = File.OpenRead(configPath);
            using var doc = JsonDocument.Parse(stream, jsonOptions);

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new BallisticPlateCompatConfig();

            if (!root.TryGetProperty("mappings", out var mappingsEl) || mappingsEl.ValueKind != JsonValueKind.Array)
                return new BallisticPlateCompatConfig();

            var cfg = new BallisticPlateCompatConfig();

            foreach (var m in mappingsEl.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object)
                    continue;

                var source = m.TryGetProperty("sourcePlateTpl", out var sEl) ? sEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                var clones = new List<string>();
                if (m.TryGetProperty("clonePlateTpls", out var cEl) && cEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ce in cEl.EnumerateArray())
                    {
                        var v = ce.GetString();
                        if (!string.IsNullOrWhiteSpace(v))
                            clones.Add(v);
                    }
                }

                cfg.Mappings.Add(new BallisticPlateCompatMapping
                {
                    SourcePlateTpl = source!,
                    ClonePlateTpls = clones
                });
            }

            return cfg;
        }
        catch
        {
            return new BallisticPlateCompatConfig();
        }
    }

    private sealed class BallisticPlateCompatConfig
    {
        public List<BallisticPlateCompatMapping> Mappings { get; } = new();
    }

    private sealed class BallisticPlateCompatMapping
    {
        public string SourcePlateTpl { get; set; } = string.Empty;
        public List<string> ClonePlateTpls { get; set; } = new();
    }
}
