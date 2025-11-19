using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Path = System.IO.Path;

namespace SalcosArmory;

internal static class OdinAssortLoader
{
    public static JsonObject MergeAssortFromDataFolders(JsonObject? baseAssort, string traderFolderPath)
    {
        var items = new List<JsonElement>();
        var barterScheme = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var loyalLevelItems = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        var subFolders = new[]
        {
            "AssortWeapons",
            "AssortAmmo",
            "AssortAttachments",
            "AssortItems"
        };

        var jsonOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        foreach (var sub in subFolders)
        {
            var folder = Path.Combine(traderFolderPath, sub);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory
                         .EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {

                try
                {
                    using var stream = File.OpenRead(file);
                    using var doc = JsonDocument.Parse(stream, jsonOptions);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsElement.EnumerateArray())
                        {
                            items.Add(item.Clone());
                        }
                    }

                    if (root.TryGetProperty("barter_scheme", out var barterElement) && barterElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in barterElement.EnumerateObject())
                        {

                            barterScheme[prop.Name] = prop.Value.Clone();
                        }
                    }

                    if (root.TryGetProperty("loyal_level_items", out var loyalElement) && loyalElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in loyalElement.EnumerateObject())
                        {

                            loyalLevelItems[prop.Name] = prop.Value.Clone();
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }


        if (items.Count == 0 && barterScheme.Count == 0 && loyalLevelItems.Count == 0 && baseAssort is not null)
        {
            return baseAssort;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WritePropertyName("items");
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteRawValue(item.GetRawText());
            }
            writer.WriteEndArray();

            writer.WritePropertyName("barter_scheme");
            writer.WriteStartObject();
            foreach (var kvp in barterScheme)
            {
                writer.WritePropertyName(kvp.Key);
                writer.WriteRawValue(kvp.Value.GetRawText());
            }
            writer.WriteEndObject();

            writer.WritePropertyName("loyal_level_items");
            writer.WriteStartObject();
            foreach (var kvp in loyalLevelItems)
            {
                writer.WritePropertyName(kvp.Key);
                writer.WriteRawValue(kvp.Value.GetRawText());
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        var node = JsonNode.Parse(json) as JsonObject;
        return node ?? new JsonObject
        {
            ["items"] = new JsonArray(),
            ["barter_scheme"] = new JsonObject(),
            ["loyal_level_items"] = new JsonObject()
        };
    }
}