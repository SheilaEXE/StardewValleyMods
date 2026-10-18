using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutfitReactions.Ai
{
    public sealed class CharacterAiProfile
    {
        public string NpcName { get; set; } = "";
        public bool Enabled { get; set; } = true;

        // Narrative v2 profile fields. These are still JSON-friendly for CPs, but the
        // AI prompt builder can turn them into a focused, hierarchical character sheet.
        public int ProfileVersion { get; set; } = 1;
        public string ProfileId { get; set; } = "";
        public string ProfileName { get; set; } = "";
        public string ProfileType { get; set; } = "";
        public string PromptLanguage { get; set; } = "";
        public string TargetGame { get; set; } = "";
        public Dictionary<string, string> NarrativeProfile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CharacterRelationshipScalingProfile> RelationshipScaling { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> DialogueModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CharacterTraitNarrativeProfile> TraitNarratives { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // NarrativeProfile keys listed here are only shown to the model in a romantic
        // relationship (dating/married) AND a private moment. Use this for explicit/physical
        // keys (e.g. "PrivateSpicySide", "PhysicalAffection"). Leaving it empty means nothing
        // is force-gated and the built-in physical/sexual detector still applies as a fallback.
        public List<string> RomanticOnlyNarrativeKeys { get; set; } = new();

        public Dictionary<string, PortraitProfile> Portraits { get; set; } = new();
        public Dictionary<string, PortraitProfile> ExtraPortraits { get; set; } = new();
        public Dictionary<string, CharacterRelationshipProfile> Family { get; set; } = new();

        // CP authors often copy simple-object-style relationship entries as objects.
        // Keep the public format flexible and normalize them to plain text internally.
        [JsonConverter(typeof(RelationshipTextDictionaryConverter))]
        public Dictionary<string, string> Relationships { get; set; } = new();
    }

    [JsonConverter(typeof(CharacterRelationshipScalingProfileJsonConverter))]
    public sealed class CharacterRelationshipScalingProfile
    {
        public string Tone { get; set; } = "";
        public List<string> AllowedBehavior { get; set; } = new();
        public List<string> Avoid { get; set; } = new();
    }

    [JsonConverter(typeof(CharacterTraitNarrativeProfileJsonConverter))]
    public sealed class CharacterTraitNarrativeProfile
    {
        public string Heading { get; set; } = "";
        public string Priority { get; set; } = "";
        public string Context { get; set; } = "";
        public string NarrativePrompt { get; set; } = "";
    }

    [JsonConverter(typeof(PortraitProfileJsonConverter))]
    public sealed class PortraitProfile
    {
        public string Command { get; set; } = "";
        public string Description { get; set; } = "";
    }

    [JsonConverter(typeof(CharacterRelationshipProfileJsonConverter))]
    public sealed class CharacterRelationshipProfile
    {
        public string Heading { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class OutfitAiContext
    {
        public string NpcName { get; set; } = "";
        public string NpcDisplayName { get; set; } = "";
        public bool IsSpouse { get; set; }
        public string DialogueKey { get; set; } = "";
        public string OutfitName { get; set; } = "";
        public string SafeOutfitHint { get; set; } = "";
        public string ThemeContext { get; set; } = "";
        public string ThemePriorityInstruction { get; set; } = "";
        public string LocationName { get; set; } = "";
        public string DetailedLocationName { get; set; } = "";
        public string LocationType { get; set; } = "";
        public bool IsOutdoors { get; set; }
        public bool IsIndoors { get; set; }
        public bool IsNpcRoom { get; set; }
        public bool IsNpcPersonalLocation { get; set; }
        public bool IsBeachOrIsland { get; set; }
        public bool IsFarmHouse { get; set; }
        public bool IsFarm { get; set; }
        public string DayPart { get; set; } = "";
        public string FestivalContext { get; set; } = "";
        public string FarmerBirthdayContext { get; set; } = "";
        public string Season { get; set; } = "";
        public string Weather { get; set; } = "";
        public int Time { get; set; }
        public int DayOfSeason { get; set; }
        public int Year { get; set; }
        public string PlayerName { get; set; } = "";
        public string PlayerGender { get; set; } = "";
        public string PlayerSelfDescription { get; set; } = "";
        public string TargetLanguage { get; set; } = "";
        public bool HasMetPlayer { get; set; }
        public string RelationshipStatus { get; set; } = "";
        public int RelationshipHearts { get; set; }
        public OutfitVisionImage VisionImage { get; set; }
        public bool HasVisionImage => VisionImage != null && VisionImage.IsUsable;
        public string FashionSenseVisualSummary { get; set; } = "";
        public bool HasFashionSenseVisualSummary => !string.IsNullOrWhiteSpace(FashionSenseVisualSummary);
        public string SpecialHatReactionContext { get; set; } = "";
        public bool HasSpecialHatReactionContext => !string.IsNullOrWhiteSpace(SpecialHatReactionContext);
        public string VanillaShirtContext { get; set; } = "";
        public bool HasVanillaShirtContext => !string.IsNullOrWhiteSpace(VanillaShirtContext);
        public bool VanillaHatShirtCombination { get; set; }
        public string SpecialItemReactionContext { get; set; } = "";
        public bool HasSpecialItemReactionContext => !string.IsNullOrWhiteSpace(SpecialItemReactionContext);
        public string SpecialItemEntryId { get; set; } = "";
        public bool IsMayorShortsSpecialItem => string.Equals(SpecialItemEntryId, "MayorsPurpleShorts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(SpecialItemEntryId, "MayorsPurpleShortsHat", StringComparison.OrdinalIgnoreCase);
        public bool SpecialItemWasJustRemoved { get; set; }
        public bool SpecialItemOnlyMode { get; set; }
        // True when SpecialItemOnlyMode is active AND the player chose "Combined" in the config,
        // meaning the NPC should react to the special item AND the current outfit together.
        // When false (default "ItemOnly"), outfit name and visual summary are suppressed.
        public bool SpecialItemCombinedMode { get; set; }
        public string SpecialItemMemoryHint { get; set; } = "";
        public bool HasSpecialItemMemoryHint => !string.IsNullOrWhiteSpace(SpecialItemMemoryHint);
        public string VanillaPantsMemoryHint { get; set; } = "";
        public string VanillaHatMemoryHint { get; set; } = "";
        public bool HasVanillaHatMemoryHint => !string.IsNullOrWhiteSpace(VanillaHatMemoryHint);
        // Number of portraits the NPC actually has in their spritesheet (0 = unknown/not provided).
        // Used to drop AI-chosen portrait indices that don't exist (which render as an empty frame).
        public int AvailablePortraitCount { get; set; } = 0;
        public string NoticedChangeType { get; set; } = "";
        public string NoticedChangeName { get; set; } = "";
        public string SafeNoticedChangeHint { get; set; } = "";
        // A saved outfit can equip a meaningful visible accessory in the same Hand Mirror
        // action. Keep the reaction classified as Outfit, but preserve this fact so the prompt
        // treats both pieces as one combined look instead of silently dropping the accessory.
        public bool SavedOutfitIncludesMeaningfulAccessory { get; set; }
        public string SavedOutfitAccessoryHint { get; set; } = "";
        public bool AccessoryWasRemovedDuringOutfitChange { get; set; }
        public string RemovedAccessoryHint { get; set; } = "";
        // True when the NPC was caught peeking at the farmer while walking (the player looked at them
        // mid-stare) and then the player approached them. Lets the reaction acknowledge being caught.
        public bool WasCaughtPeeking { get; set; }
        public bool IsAccessoryChange => string.Equals(NoticedChangeType, "Accessory", StringComparison.OrdinalIgnoreCase);
        public bool IsHatChange => string.Equals(NoticedChangeType, "Hat", StringComparison.OrdinalIgnoreCase);
        public bool IsHairChange => string.Equals(NoticedChangeType, "Hair", StringComparison.OrdinalIgnoreCase);
        public bool IsOutfitChange => string.Equals(NoticedChangeType, "Outfit", StringComparison.OrdinalIgnoreCase);

        /// <summary>Memory hint injected by OutfitMemoryService, or null if first time.</summary>
        public string OutfitMemoryContext { get; set; } = null;
        public bool HasOutfitMemory => !string.IsNullOrWhiteSpace(OutfitMemoryContext);

        /// <summary>
        /// The farmer's reply text, populated only during follow-up generation.
        /// Used by ResolvePortraitCommand to infer the best portrait from the player's words
        /// when the NPC response has no asterisk actions.
        /// </summary>
        public string PlayerReply { get; set; } = null;

        // Full transcript of the current outfit-reaction back-and-forth (oldest first), built by
        // ModEntry from its per-NPC conversation history. Empty/null when there is no multi-turn
        // history yet (e.g. the very first reply). Used so follow-up replies stay on-topic instead
        // of only seeing the single most recent NPC line and the farmer's newest reply in isolation.
        // When true, the farmer is wearing a vanilla hat AND the player chose the "HatOnly" reaction
        // mode, so the NPC should react EXCLUSIVELY to the vanilla hat and ignore the rest of the
        // outfit/clothes/accessory context. When false, the hat is reacted to together with the
        // current look (the original "Combined" behavior).
        // True only when THIS NPC plausibly saw the farmer wearing the accessory that was just
        // removed (they have prior memory of this look). When false, the NPC must not talk about the
        // accessory as something they remember "from before" — they never witnessed it — and should
        // react only to the current look without referencing a past combination.
        public bool NpcWitnessedPreviousAccessory { get; set; } = false;

        public bool VanillaHatHatOnlyMode { get; set; } = false;

        // Standalone framing text for a vanilla-hat equip/removal, kept separate from the full outfit
        // visual summary so it still reaches the model in HAT-ONLY mode (where the clothes summary is
        // intentionally suppressed). Without this, removing a hat in HAT-ONLY mode lost the "just took
        // it off" framing and the NPC reacted as if the hat was still being worn.
        public string VanillaHatFraming { get; set; } = "";
        public bool HasVanillaHatFraming => !string.IsNullOrWhiteSpace(VanillaHatFraming);

        public string ConversationTranscript { get; set; } = null;
    }

    public sealed class AiComplimentResult
    {
        public string Text { get; set; } = "";

        /// <summary>
        /// Primary portrait key for the dialogue when per-box portrait keys don't override it.
        /// The key may be a vanilla key (h/s/a/l), a custom ExtraPortraits key, or the raw $command.
        /// </summary>
        public string Portrait { get; set; } = "";

        /// <summary>
        /// Optional per-dialogue-box portrait keys, in the same order as text split by #$b#.
        /// This lets the first Stardew dialogue box receive a portrait too, instead of only the final box.
        /// </summary>
        public List<string> Portraits { get; set; } = new();

        public bool NeedsClarification { get; set; } = false;

        /// <summary>
        /// Optional exact excerpt from Text that carries the saved outfit's semantic theme.
        /// It is used only for internal validation of themed outfit + large accessory combinations.
        /// </summary>
        public string ThemeAnchor { get; set; } = "";
    }



    internal sealed class CharacterRelationshipScalingProfileJsonConverter : JsonConverter<CharacterRelationshipScalingProfile>
    {
        public override CharacterRelationshipScalingProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new CharacterRelationshipScalingProfile { Tone = reader.GetString() ?? "" };

            if (reader.TokenType == JsonTokenType.Null)
                return new CharacterRelationshipScalingProfile();

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("RelationshipScaling entries must be either a string or an object.");

            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            CharacterRelationshipScalingProfile result = new()
            {
                Tone = ReadString(root, "Tone") ?? ReadString(root, "tone") ?? ReadString(root, "Description") ?? ReadString(root, "description") ?? ""
            };

            result.AllowedBehavior = ReadStringList(root, "AllowedBehavior")
                ?? ReadStringList(root, "allowedBehavior")
                ?? ReadStringList(root, "Allowed")
                ?? ReadStringList(root, "allowed")
                ?? new List<string>();

            result.Avoid = ReadStringList(root, "Avoid")
                ?? ReadStringList(root, "avoid")
                ?? ReadStringList(root, "ForbiddenBehavior")
                ?? ReadStringList(root, "forbiddenBehavior")
                ?? new List<string>();

            return result;
        }

        public override void Write(Utf8JsonWriter writer, CharacterRelationshipScalingProfile value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(CharacterRelationshipScalingProfile.Tone), value?.Tone ?? "");

            writer.WritePropertyName(nameof(CharacterRelationshipScalingProfile.AllowedBehavior));
            JsonSerializer.Serialize(writer, value?.AllowedBehavior ?? new List<string>(), options);

            writer.WritePropertyName(nameof(CharacterRelationshipScalingProfile.Avoid));
            JsonSerializer.Serialize(writer, value?.Avoid ?? new List<string>(), options);
            writer.WriteEndObject();
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString();
                if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                    return value.ToString();
            }

            return null;
        }

        private static List<string> ReadStringList(JsonElement root, string propertyName)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out JsonElement value))
                return null;

            List<string> result = new();

            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
                return result;
            }

            if (value.ValueKind != JsonValueKind.Array)
                return null;

            foreach (JsonElement item in value.EnumerateArray())
            {
                string text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
            }

            return result;
        }
    }

    internal sealed class CharacterTraitNarrativeProfileJsonConverter : JsonConverter<CharacterTraitNarrativeProfile>
    {
        public override CharacterTraitNarrativeProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new CharacterTraitNarrativeProfile { NarrativePrompt = reader.GetString() ?? "" };

            if (reader.TokenType == JsonTokenType.Null)
                return new CharacterTraitNarrativeProfile();

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("TraitNarratives entries must be either a string or an object.");

            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            return new CharacterTraitNarrativeProfile
            {
                Heading = ReadString(root, "Heading") ?? ReadString(root, "heading") ?? "",
                Priority = ReadString(root, "Priority") ?? ReadString(root, "priority") ?? "",
                Context = ReadString(root, "Context") ?? ReadString(root, "context") ?? "",
                NarrativePrompt = ReadString(root, "NarrativePrompt") ?? ReadString(root, "narrativePrompt") ?? ReadString(root, "Description") ?? ReadString(root, "description") ?? ""
            };
        }

        public override void Write(Utf8JsonWriter writer, CharacterTraitNarrativeProfile value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(CharacterTraitNarrativeProfile.Heading), value?.Heading ?? "");
            writer.WriteString(nameof(CharacterTraitNarrativeProfile.Priority), value?.Priority ?? "");
            writer.WriteString(nameof(CharacterTraitNarrativeProfile.Context), value?.Context ?? "");
            writer.WriteString(nameof(CharacterTraitNarrativeProfile.NarrativePrompt), value?.NarrativePrompt ?? "");
            writer.WriteEndObject();
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString();
                if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                    return value.ToString();
            }

            return null;
        }
    }


    internal sealed class PortraitProfileJsonConverter : JsonConverter<PortraitProfile>
    {
        public override PortraitProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string portraitDescription = reader.GetString() ?? "";
                return new PortraitProfile { Description = portraitDescription };
            }

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Portrait entries must be either a string or an object.");

            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            string command = ReadString(root, "Command") ?? ReadString(root, "command") ?? "";
            string description = ReadString(root, "Description") ?? ReadString(root, "description") ?? ReadString(root, "desc") ?? "";

            // dash-line-style simple objects sometimes only have Heading/Name.
            if (string.IsNullOrWhiteSpace(description))
                description = ReadString(root, "Heading") ?? ReadString(root, "heading") ?? ReadString(root, "Name") ?? ReadString(root, "name") ?? "";

            return new PortraitProfile
            {
                Command = command,
                Description = description
            };
        }

        public override void Write(Utf8JsonWriter writer, PortraitProfile value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(PortraitProfile.Command), value?.Command ?? "");
            writer.WriteString(nameof(PortraitProfile.Description), value?.Description ?? "");
            writer.WriteEndObject();
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();

            return null;
        }
    }

    internal sealed class CharacterRelationshipProfileJsonConverter : JsonConverter<CharacterRelationshipProfile>
    {
        public override CharacterRelationshipProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new CharacterRelationshipProfile { Description = reader.GetString() ?? "" };

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Family entries must be either a string or an object.");

            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            return new CharacterRelationshipProfile
            {
                Heading = ReadString(root, "Heading") ?? ReadString(root, "heading") ?? ReadString(root, "id") ?? "",
                Description = ReadString(root, "Description") ?? ReadString(root, "description") ?? ""
            };
        }

        public override void Write(Utf8JsonWriter writer, CharacterRelationshipProfile value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(CharacterRelationshipProfile.Heading), value?.Heading ?? "");
            writer.WriteString(nameof(CharacterRelationshipProfile.Description), value?.Description ?? "");
            writer.WriteEndObject();
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();

            return null;
        }
    }

    internal sealed class FlexibleTextJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return "";

            if (reader.TokenType == JsonTokenType.String)
                return reader.GetString() ?? "";

            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            return ConvertElementToText(doc.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value ?? "");
        }

        private static string ConvertElementToText(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? "";

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return element.ToString();

                case JsonValueKind.Array:
                    {
                        List<string> parts = new();
                        foreach (JsonElement item in element.EnumerateArray())
                        {
                            string text = ConvertElementToText(item);
                            if (!string.IsNullOrWhiteSpace(text))
                                parts.Add(text);
                        }
                        return string.Join("; ", parts);
                    }

                case JsonValueKind.Object:
                    {
                        // Common object shape: { "Heading": "...", "Description": "..." }.
                        string heading = ReadString(element, "Heading") ?? ReadString(element, "heading") ?? "";
                        string description = ReadString(element, "Description") ?? ReadString(element, "description") ?? "";

                        if (!string.IsNullOrWhiteSpace(heading) || !string.IsNullOrWhiteSpace(description))
                        {
                            if (!string.IsNullOrWhiteSpace(heading) && !string.IsNullOrWhiteSpace(description))
                                return heading + ": " + description;
                            return !string.IsNullOrWhiteSpace(description) ? description : heading;
                        }

                        // Rich map shape, e.g. FamilyNarrative:
                        // { "Robin": "...", "Demetrius": "...", "Maru": "..." }.
                        List<string> parts = new();
                        foreach (JsonProperty property in element.EnumerateObject())
                        {
                            string text = ConvertElementToText(property.Value);
                            if (!string.IsNullOrWhiteSpace(text))
                                parts.Add(property.Name + ": " + text);
                        }
                        return string.Join(" ", parts);
                    }

                default:
                    return "";
            }
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();

            return null;
        }
    }


    internal sealed class RelationshipTextDictionaryConverter : JsonConverter<Dictionary<string, string>>
    {
        public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

            if (reader.TokenType == JsonTokenType.Null)
                return result;

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Relationships must be an object.");

            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                string text = ConvertRelationshipValueToText(property.Value);
                if (!string.IsNullOrWhiteSpace(text))
                    result[property.Name] = text;
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value != null)
            {
                foreach (var pair in value)
                    writer.WriteString(pair.Key, pair.Value ?? "");
            }
            writer.WriteEndObject();
        }

        private static string ConvertRelationshipValueToText(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            if (value.ValueKind != JsonValueKind.Object)
                return "";

            string heading = ReadString(value, "Heading") ?? ReadString(value, "heading") ?? ReadString(value, "id") ?? "";
            string description = ReadString(value, "Description") ?? ReadString(value, "description") ?? "";

            if (!string.IsNullOrWhiteSpace(heading) && !string.IsNullOrWhiteSpace(description))
                return heading + ": " + description;

            return description ?? heading ?? "";
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();

            return null;
        }
    }
}
