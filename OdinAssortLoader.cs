using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Path = System.IO.Path;

namespace SalcosArmory;

internal static class OdinAssortLoader
{
    public static JsonObject MergeAssortFromSplitFolders(string traderFolderPath)
    {
        var result = CreateEmptyAssort();

        var roots = new[]
        {
            Path.Combine(traderFolderPath, "AssortAmmo"),
            Path.Combine(traderFolderPath, "AssortArmor"),
            Path.Combine(traderFolderPath, "AssortAttachments"),
            Path.Combine(traderFolderPath, "AssortItems"),
            Path.Combine(traderFolderPath, "AssortWeapons"),
            Path.Combine(traderFolderPath, "data")
        };

        foreach (var root in roots)
        {
            MergeAllJsonFragmentsUnder(root, result);
        }

        NormalizeAndRepairAssortIds(result);

        return result;
    }

    // Legacy API kept for compatibility
    public static JsonObject MergeAssortFromDataFolders(JsonObject? baseAssort, string traderFolderPath)
    {
        var merged = CreateEmptyAssort();

        if (baseAssort != null)
        {
            MergeSingleFragment(baseAssort, merged);
        }

        var dataDir = Path.Combine(traderFolderPath, "data");
        MergeAllJsonFragmentsUnder(dataDir, merged);

        NormalizeAndRepairAssortIds(merged);

        return merged;
    }

    private static void MergeAllJsonFragmentsUnder(string root, JsonObject target)
    {
        if (!Directory.Exists(root))
            return;

        var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                var fragment = JsonNode.Parse(json) as JsonObject;
                if (fragment == null)
                    continue;

                // Only merge if it looks like an assort fragment
                var hasItems = fragment["items"] is JsonArray;
                var hasBarter = fragment["barter_scheme"] is JsonObject;
                var hasLoyal = fragment["loyal_level_items"] is JsonObject;

                if (!hasItems && !hasBarter && !hasLoyal)
                    continue;

                MergeSingleFragment(fragment, target);
            }
            catch
            {
                // intentionally ignored – fragment failure must not block startup
            }
        }
    }

    private static void MergeSingleFragment(JsonObject fragment, JsonObject target)
    {
        var itemsTarget = target["items"] as JsonArray ?? new JsonArray();
        target["items"] = itemsTarget;

        var barterTarget = target["barter_scheme"] as JsonObject ?? new JsonObject();
        target["barter_scheme"] = barterTarget;

        var loyalTarget = target["loyal_level_items"] as JsonObject ?? new JsonObject();
        target["loyal_level_items"] = loyalTarget;

        var fragItems = fragment["items"] as JsonArray;
        if (fragItems != null)
        {
            foreach (var item in fragItems)
            {
                // Only accept objects; always DeepClone to avoid parent ownership issues
                if (item is JsonObject)
                    itemsTarget.Add(item.DeepClone());
            }
        }

        var fragBarter = fragment["barter_scheme"] as JsonObject;
        if (fragBarter != null)
        {
            foreach (var prop in fragBarter)
            {
                if (prop.Value != null)
                    barterTarget[prop.Key] = prop.Value.DeepClone();
            }
        }

        var fragLoyal = fragment["loyal_level_items"] as JsonObject;
        if (fragLoyal != null)
        {
            foreach (var prop in fragLoyal)
            {
                if (prop.Value != null)
                    loyalTarget[prop.Key] = prop.Value.DeepClone();
            }
        }
    }

    /// <summary>
    /// Ensures:
    /// - items[] contains ONLY JsonObject entries
    /// - each item has a valid MongoId _id (24 hex)
    /// - references are consistent:
    ///     items[*].parentId, barter_scheme keys, loyal_level_items keys
    /// </summary>
    private static void NormalizeAndRepairAssortIds(JsonObject assortRoot)
    {
        if (assortRoot["items"] is not JsonArray items)
            return;

        // IMPORTANT: Build a new array with DeepClone() nodes to avoid "node already has a parent"
        var cleanedItems = new JsonArray();
        foreach (var node in items)
        {
            if (node is JsonObject jo)
                cleanedItems.Add(jo.DeepClone());
        }
        assortRoot["items"] = cleanedItems;

        if (cleanedItems.Count == 0)
            return;

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1: repair/ensure _id is valid and unique
        for (var i = 0; i < cleanedItems.Count; i++)
        {
            if (cleanedItems[i] is not JsonObject itemObj)
                continue;

            var rawId = GetNodeAsString(itemObj, "_id");

            // Handle {"$oid":"..."} style if present
            if (!IsValidMongoId(rawId))
            {
                var extracted = ExtractOidIfPresent(itemObj["_id"]);
                if (!string.IsNullOrWhiteSpace(extracted))
                    rawId = extracted;
            }

            if (!IsValidMongoId(rawId))
            {
                var newId = GenerateMongoId(used);
                if (!string.IsNullOrWhiteSpace(rawId))
                    idMap[rawId] = newId;

                itemObj["_id"] = newId;
                used.Add(newId);
            }
            else
            {
                // Ensure uniqueness
                if (!used.Add(rawId))
                {
                    var newId = GenerateMongoId(used);
                    idMap[rawId] = newId;
                    itemObj["_id"] = newId;
                    used.Add(newId);
                }
                else
                {
                    itemObj["_id"] = rawId;
                }
            }

            // Force other critical fields to string tokens (harmless)
            ForceStringToken(itemObj, "_tpl");
            ForceStringToken(itemObj, "parentId");
            ForceStringToken(itemObj, "slotId");
        }

        if (idMap.Count == 0)
            return;

        // Pass 2: rewrite parentId references
        for (var i = 0; i < cleanedItems.Count; i++)
        {
            if (cleanedItems[i] is not JsonObject itemObj)
                continue;

            var parent = GetNodeAsString(itemObj, "parentId");
            if (!string.IsNullOrWhiteSpace(parent) && idMap.TryGetValue(parent, out var mappedParent))
            {
                itemObj["parentId"] = mappedParent;
            }
        }

        // Pass 3: rewrite barter_scheme keys
        if (assortRoot["barter_scheme"] is JsonObject barterObj)
        {
            var rewritten = new JsonObject();
            foreach (var kvp in barterObj)
            {
                var key = kvp.Key;
                var newKey = idMap.TryGetValue(key, out var mapped) ? mapped : key;
                rewritten[newKey] = kvp.Value?.DeepClone();
            }
            assortRoot["barter_scheme"] = rewritten;
        }

        // Pass 4: rewrite loyal_level_items keys
        if (assortRoot["loyal_level_items"] is JsonObject loyalObj)
        {
            var rewritten = new JsonObject();
            foreach (var kvp in loyalObj)
            {
                var key = kvp.Key;
                var newKey = idMap.TryGetValue(key, out var mapped) ? mapped : key;
                rewritten[newKey] = kvp.Value?.DeepClone();
            }
            assortRoot["loyal_level_items"] = rewritten;
        }
    }

    private static void ForceStringToken(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node == null)
            return;

        if (node is JsonValue val && val.TryGetValue<string>(out _))
            return;

        obj[key] = node.ToString() ?? "";
    }

    private static string GetNodeAsString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node == null)
            return "";

        if (node is JsonValue val && val.TryGetValue<string>(out var s))
            return s ?? "";

        return node.ToString() ?? "";
    }

    private static string? ExtractOidIfPresent(JsonNode? node)
    {
        if (node is not JsonObject jo)
            return null;

        if (jo.TryGetPropertyValue("$oid", out var oidNode) &&
            oidNode is JsonValue oidVal &&
            oidVal.TryGetValue<string>(out var oidStr))
        {
            return oidStr;
        }

        return null;
    }

    private static bool IsValidMongoId(string s)
    {
        if (s.Length != 24)
            return false;

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var isHex =
                (c >= '0' && c <= '9') ||
                (c >= 'a' && c <= 'f') ||
                (c >= 'A' && c <= 'F');

            if (!isHex)
                return false;
        }

        return true;
    }

    private static string GenerateMongoId(HashSet<string> used)
    {
        while (true)
        {
            var hex = Guid.NewGuid().ToString("N");
            var mongo = hex.Substring(0, 24);

            if (used.Add(mongo))
                return mongo;
        }
    }

    private static JsonObject CreateEmptyAssort()
    {
        return new JsonObject
        {
            ["items"] = new JsonArray(),
            ["barter_scheme"] = new JsonObject(),
            ["loyal_level_items"] = new JsonObject()
        };
    }
}
