using System;
using System.IO;
using System.Text.Json;
using StardewModdingAPI;

namespace OutfitReactions.Ai
{
    /// <summary>
    /// Loads and exposes the editable prompt style blocks from
    /// <c>assets/prompts/prompts.json</c>. If the file is missing or
    /// malformed, every property silently falls back to the built-in default
    /// so the mod always works even if the user deletes or corrupts the file.
    /// </summary>
    internal sealed class PromptStyleService
    {
        // ── Defaults (identical to what was hard-coded before) ────────────────

        public static readonly string FallbackHairChangeMode =
            "The NPC noticed the farmer's hairstyle or hair color changed. IMPORTANT: only the hairstyle/hair color is new — the farmer's outfit is exactly the same as before. React to the hair change and how it affects the farmer's overall vibe; do not talk about the outfit as if it were new or different. Name exact colors or specific hairstyles only when the support data makes them clear.";

        public static readonly string FallbackHatChangeMode =
            "The NPC noticed the farmer's head item/headwear. React to what it seems to be and how it changes the farmer's vibe; do not call small headbands, tiaras, bows, flowers, clips, or hair accessories a hat unless the support data says it is a hat.";

        public static readonly string FallbackAccessoryChangeMode =
            "The NPC noticed the farmer's accessory changed. IMPORTANT: only the accessory is new — the farmer's outfit and hairstyle are exactly the same as before. React naturally to the accessory itself and how it pairs with the outfit the farmer is already wearing. If the accessory creates a funny, cute, ugly, strange, dramatic, or impossible combination with the saved outfit theme, the NPC may compare them, question it, tease it, or make an in-world joke instead of treating the accessory as isolated. If the accessory is visually unclear, do not guess.";

        public static readonly string FallbackOutfitChangeMode =
            "The NPC noticed the farmer's saved outfit. React naturally to what the outfit seems to be, the situation, recognizable theme/reference, and the farmer's overall vibe. Mention colors, texture, or style only as small character-specific details, not as a fashion review.";

        // NaturalReactionStyle uses {Change} and {OutfitFocusRule} placeholders
        // that are filled at build time by CharacterPromptBuilder.
        // This compact built-in form keeps the unique honesty/focus rules. Theme recognition,
        // occasion mismatch and opening variety are already covered by HOW TO REACT.
        private const string CompactNaturalReactionStyle =
            "This is an honest reaction, not a required compliment. React to the farmer's {Change} through the NPC's established personality and relationship. Admiration, amusement, shock, fear, disgust, criticism, concern, confusion, and indifference are all valid when they fit the look and this NPC; do not automatically reframe a bizarre or unpleasant look as clever or admirable. If an item evokes refuse, grime, or decay, the NPC may react to an implied bad smell even without explicit odor data, but never force that detail. {OutfitFocusRule}Use location, season, weather, time, and privacy only when they create a meaningful human reaction, never as a forced closing justification. For head items, respect the equipped/support data: a headband, tiara, bow, clip, flower, pin, or crown is not automatically a hat.";

        public static string DefaultNaturalReactionStyle => CompactNaturalReactionStyle;

        public static readonly string FallbackNaturalReactionStyle =
            "This is an honest REACTION, not a compliment. A compliment is only one of many possible reactions and is never required. React the way this specific person honestly would: if the look is good, they can like it; if it is strange, ugly, tacky, gross, confusing, ridiculous, or off-putting, they can say so in their own voice. Do not soften a genuinely weird or unappealing look into a polite compliment just to be nice. Flattering a clearly bizarre or ugly item when the NPC would not actually admire it reads as fake and breaks character. Match the honesty to the NPC's personality and relationship: a blunt NPC can be openly critical or mocking; a kind NPC can be diplomatically honest or visibly unsure rather than fake-positive; a close partner can tease or be playfully horrified. Negative, confused, and unimpressed reactions are fully allowed and often the correct choice for unusual or unflattering looks. React like a person seeing the farmer's {Change}, not like a fashion analyst or stylist reviewing a palette. Start from the NPC's own personality and relationship level, then react to the obvious concept, situation, mood, humor, surprise, concern, or relationship impact before mentioning visual details. If the look resembles a costume, animal suit, cosplay, pajamas, swimwear, work outfit, festival outfit, silly outfit, cute outfit, or something unusual, comment on what it seems to be first. If the outfit/change clues include a recognizable theme, character, franchise, creature, animal, object, food, fantasy archetype, or named inspiration, the NPC may mention or allude to that reference when it fits their knowledge and personality. Geeky, pop-culture-aware, playful, artistic, or highly observant NPCs can be more specific; others can react to the creature/theme more generally. Do not force every NPC to recognize every reference, but do not ignore clear clues like Sanrio, My Melody, Pikachu, Pokemon/Pokémon, lizard, dinosaur, frog, fairy, cat, rabbit, or other named themes when they would naturally affect the reaction. IMPORTANT — the outfit NAME is only a label/theme, not a guaranteed list of worn pieces. A themed name (e.g. 'Rabbit Outfit', 'Cat Costume', 'Demon Set') often implies a head piece like ears, horns, antennae, or a themed hat, but the farmer may have removed it. Only treat a head piece as worn if it appears in the equipped-items list. If the clues say no head piece is equipped (e.g. 'head/headwear: NONE equipped'), do NOT mention or describe ears, horns, a hat, or any head accessory the theme name suggests — the farmer is bare-headed right now. You may still reference the rest of the theme (the clothing/body that IS worn), just not the missing head piece. When a recognizable theme is present, do more than a generic 'it suits you' compliment. Choose the reaction angle from the NPC's personality: a joke, playful question, friendly roast, surprised confusion, dry sarcasm, affectionate teasing, practical concern, admiration, indifference, reluctant approval, or a small imagined scenario where that theme would fit. Only compare the look to farm life, pets, crops, monsters, caves, the saloon, the beach, festivals, the town, or daily chores when that topic naturally belongs to this NPC, the current location, the outfit theme, or the relationship context. Do not use mines, slimes, monsters, caves, the saloon, or farm chores as generic Stardew references for NPCs who would not think about them. If the noticed change is an accessory while the farmer is still wearing a themed saved outfit, treat the accessory as part of the whole current look. The NPC may compare the new accessory with the existing outfit theme, notice clashes or absurd combinations, and joke about mismatches like wings added to a Pikachu/animal/mascot/cosplay outfit. Do not respond as if the accessory exists alone when the full outfit context is available. Occasion mismatch: also judge whether the item makes sense for the CURRENT occasion, place, and moment (use the Location, Festival, season, weather, and time already provided). Items tied to a specific event — a bridal veil, a party hat, a graduation cap, formal/gala wear, holiday costumes, a swimsuit — worn with NO matching occasion happening can be gently questioned, teased, or remarked on. For example, a wedding veil when there is no wedding, or a party hat when there is no party, is odd and an observant or blunt NPC may point it out, ask about it, or joke. Do not force this every time; weigh it against the NPC's personality and how striking the mismatch is. If there IS a matching occasion (a festival, an actual wedding, a fitting location), the item fits and needs no such remark. The NPC may find a look cute, weird, ridiculous, ugly, funny, suspicious, adorable, dramatic, awkward, unnecessary, too flashy, practical, or oddly fitting if their personality supports it. Avoid making 'combina com você'/'it suits you' the main point; if that idea appears at all, keep it secondary to a concrete reaction, question, joke, reference, or situation. Opening variety is mandatory: do not reuse the same first words, opening phrase, sentence structure, or reaction angle across attempts. If a gruff NPC uses one, vary it or start directly with a concrete observation, complaint, warning, skeptical remark, or reluctant admission instead. Colors, texture, silhouette, balance, composition, or coordination may be mentioned only as small details when they sound natural for this NPC. Avoid making those analytic terms the structure of the line. {OutfitFocusRule}Use location, season, weather, time, and privacy only when they add a real human reaction; never force them as a closing justification. Location/farm/town references may be hypothetical jokes or comparisons if phrased clearly as 'parece que', 'se você aparecesse...', 'dá pra imaginar...', or similar. For head items, respect support data: a headband, tiara, hairband, bow, clip, flower, pin, or crown should not be called a hat unless the metadata says it is a hat.";



        public static readonly string FallbackPlayerKnownAddressRule =
            "PLAYER ADDRESS RULE: the player character's name is {PlayerName}. Do not address the player as a localized equivalent of 'farmer', 'player', 'rancher', 'new farmer', or 'newcomer' when the NPC would reasonably know the player's name; use the player's name instead in those cases. Role or job words may still describe farm work, but must not replace the player's name. FARM NAMING RULE: when referring to the player's agricultural property, use the standard direct equivalent of 'farm' in {TargetLanguage}. Do not substitute a term whose ordinary meaning is countryside, a rural region, ranch, homestead, smallholding, fields, or subsistence plot. USE THE NAME SPARINGLY: most lines do not need direct address. Use the player's name only for a genuine greeting, attention, emphasis, emotional beat, or scolding, never as filler or a habitual opening or ending. Do not use it in consecutive lines or more than once in a short reaction unless the moment truly requires it.";

        public static readonly string FallbackPlayerUnknownAddressRule =
            "PLAYER ADDRESS RULE: the player character's name is unavailable. Avoid overusing generic role labels as direct address; use natural dialogue without a vocative when possible. FARM NAMING RULE: when referring to the player's agricultural property, use the standard direct equivalent of 'farm' in {TargetLanguage}. Do not substitute a term whose ordinary meaning is countryside, a rural region, ranch, homestead, smallholding, fields, or subsistence plot.";

        public static readonly string FallbackPlayerGenderRule =
            "PLAYER GENDER RULE: the player character's grammatical gender is {PlayerGender}. Use matching grammatical agreement in {TargetLanguage} when the language requires gendered pronouns, adjectives, nouns, or titles. If the target language does not require grammatical gender in that sentence, neutral wording is allowed. Do not add gendered labels unnecessarily. Do not use romantic pet names, beauty-based nicknames, or overly intimate address unless they fit this NPC's established personality and relationship level. {GenderSpecificCaution}";

        public static readonly string FallbackVisibleVanillaHatOnlyMode =
            "HAT-ONLY reaction mode: this reaction is about the visible vanilla/base-game hat the farmer is currently wearing, and the player has asked NPCs to react EXCLUSIVELY to that hat. React ONLY to the hat itself — its look, shape, color, vibe, and how it suits or clashes with the farmer. Do NOT describe, compliment, mention, or factor in the rest of the outfit, clothes, accessories, hair, or overall look; treat everything except the visible hat as irrelevant for this reaction. A tightly hat-focused reaction can be funnier and more pointed, so lean into the hat fully.";

        public static readonly string FallbackRemovedVanillaHatOnlyMode =
            "HAT-REMOVAL-ONLY reaction mode: this reaction is about a vanilla/base-game hat the farmer has just removed, and this specific NPC has enough prior context to notice that absence. The farmer is currently bare-headed. React ONLY to the removal/absence of the hat, not to a hat they are wearing now. Do NOT say or imply the farmer is currently wearing a hat. Do NOT invent a current hat color, shape, material, or style from hair, head pixels, or the outfit. Do NOT describe, compliment, mention, or factor in the rest of the outfit, clothes, accessories, or overall look; treat everything except the removed hat/now-bare head as irrelevant for this reaction.";

        public static readonly string FallbackSavedOutfitFocusGuidance =
            "Focus guidance: the NPC noticed the saved outfit. React to what the outfit seems to be, any recognizable theme/reference, the situation, and the farmer's overall vibe first. Treat clearly visible large accessories as part of the complete look: if one reinforces, transforms, contradicts, or humorously clashes with the outfit theme, relate the two naturally instead of ignoring the accessory or discussing the clothes alone. If the theme is recognizable, the NPC may use humor, questions, playful roasting, imagined scenarios, farm/town/place comparisons, or character-specific weirdness instead of simple praise. Mention outfit colors/details only if they are obvious from the outfit/theme or textual support data and sound casual. Do NOT focus on the player's hair, hair color, or a tiny/generic head-slot item when reacting to a whole saved outfit. Do not call the farmer's hair a hat. Do not force attention onto tiny, makeup-like, or visually unclear accessories.";

        public static readonly string FallbackHairFocusGuidance =
            "Focus guidance: the NPC noticed the hairstyle/hair change. CRITICAL for hair COLOR: name a hair color ONLY if the support data states a confirmed/authoritative hair color; in that case use that exact color word. If no confirmed hair color is given, do NOT name any hair color at all — never read or guess hair color from the image, pixels, sprite shading, floors, lighting, or scenery. For the HAIRSTYLE itself, do NOT assert a specific style category (braids, pigtails, twin-tails, bun, ponytail, dreadlocks, curls, etc.) unless it is unmistakably clear; small pixel sprites are easy to misread, so when unsure refer to it generally (the new hairstyle, the new look) instead of naming a wrong style. When neither color nor style is certain, simply react to the new look suiting the farmer. If the support data or image also shows a large, obvious accessory such as an umbrella, wings, or backpack, you may mention it briefly as secondary context, but do not ignore the hair change.";

        public static readonly string FallbackHatFocusGuidance =
            "Focus guidance: the NPC noticed the hat/headwear change. You may mention the hat briefly, but do NOT make the entire dialogue only about the hat — weave it into the overall look and how it suits the farmer. CRITICAL for hat/headwear COLOR: name a hat/headwear color ONLY if the support data states a confirmed/authoritative hat color; if not stated, do NOT name any hat color. For shape/style, only describe what is visually clear; when unsure refer to it generally.";

        public static readonly string FallbackAccessoryFocusGuidance =
            "Focus guidance: the NPC noticed a Fashion Sense accessory change. Accessories may be wings, backpacks, umbrellas, animated decorations, small earrings, or other extra visual pieces; ignore makeup-like accessories. Focus on the visible accessory if clear, but do NOT isolate it from the rest of the current outfit. If a readable saved-outfit name provides a concrete theme, use that concept or a natural allusion to it; comparing only the colors does not count as relating the accessory to the theme. React to the combined look through a funny mismatch, cute chaos, weird hybrid, dramatic upgrade, ugly clash, playful roast, or an in-world joke according to the NPC. This applies even when the outfit and accessory were equipped in the same Hand Mirror session: the changed accessory is the hook, and the saved outfit/theme is the thing it should be compared against. If it is not visually identifiable, use the clarification behavior instead of guessing.";

        private const string CompactFashionSenseVisualSupportRule =
            "Equipped appearance support is authoritative for item IDs and explicitly confirmed colors. Images may add clear silhouettes, large accessories, and broad clothing colors, but never infer hair or headwear colors unless support confirms them. Ignore technical IDs, scenery, and uncertain details, and never mention the supporting system in dialogue. Current equipped data: {VisualSummary}";

        private const string CompactFashionSenseVisualSeparationRule =
            "Hair is a hairstyle, not a hat or part of the outfit palette. Treat clearly visible large accessories as part of the overall look and relate them to a recognizable saved-outfit theme; ignore makeup-like, tiny, or unclear pieces. Never guess hair or headwear colors from pixels, and ignore generic head-slot IDs unless a meaningful piece is visually clear.";

        public static readonly string FallbackFashionSenseVisualSupportRule =
            "Fashion Sense API visual support data. This is AUTHORITATIVE for equipped item IDs and for any confirmed color it states. For broad outfit/clothing colors, the attached image may also be used when the color is clearly visible on the farmer; use ordinary broad words like pink, white, black, yellow, red, green, or brown, not over-specific pixel guesses. Never take colors from hair, tiny/generic head-slot items, floor, background, scenery, lighting, furniture, or walls. If this data states a confirmed hair color or confirmed hat/headwear color, use exactly that only for hair/hat-specific reactions; if it states none/untinted or gives no confirmed color for hair/hat, do not name that hair/hat color from the image. When the noticed change is a whole saved outfit, hair and generic head-slot IDs are intentionally omitted or should be ignored; focus on the saved outfit/theme and meaningful visible pieces instead. If an item clue looks like an internal/generic ID such as \"pack0005 hat 2\", do not treat it as meaningful dialogue content and do not call it a hat just because the slot says hat. Always ignore floor/background/scenery colors. Never mention Fashion Sense, API, internal IDs, filenames, or technical labels in dialogue: {VisualSummary}";

        public static readonly string FallbackFashionSenseVisualSeparationRule =
            "IMPORTANT: the 'hair' entry in the data above is the player character's HAIRSTYLE — it is NOT a hat and NOT part of the outfit. Accessory entries can be visible decorative pieces such as wings, backpacks, umbrellas, capes, bows, clips, flowers, earrings, or animated decorations; ignore makeup-like accessories. Some are worn on/in the hair and some are large body/back accessories. Do not treat hair accessories as clothing palette, but do treat clearly visible large accessories as part of the current overall look. When the noticed change is an accessory and a saved outfit/theme is also present, compare them naturally instead of describing only the accessory. Never reference hair colour or tiny hair/head accessory colours when describing a saved outfit. You may name broad dominant clothing/outfit colors from the attached image only when they are clearly on the outfit itself, not on hair, head-slot items, scenery, or lighting. For a whole saved outfit reaction, do not infer hat/headwear color from pixels or from the image, and never describe the player's hair as a brown hat. If the head-slot ID is generic/internal, ignore it unless the image makes a meaningful head accessory obvious; even then, describe it generally as a bow/head accessory/tiara/etc. only if clear.";

        public static readonly string FallbackSpecialItemVisibleRule =
            "SPECIAL ITEM REACTION DATA. This is authoritative for the item the farmer is currently wearing. Do not mention technical labels, JSON keys, or internal data sources in dialogue: {SpecialItemData}\nIf this special item data is present, use it as a high-priority reaction hook. The NPC should react specifically to this item according to their personality, relationship, and context — especially any NPC-specific instructions or secret-knowledge boundary indicated in the data.";

        public static readonly string FallbackSpecialItemRemovedRule =
            "SPECIAL ITEM — JUST REMOVED. The farmer was wearing a special item and has now taken it off. React to the fact that it is gone, based on how this NPC feels about that item. Data: {SpecialItemData}";

        public static readonly string FallbackSpecialVanillaHatRule =
            "VANILLA HAT FACTS. This is factual context from the base game's data for the hat involved in the current change: {SpecialHatData}\nTreat the name and description only as identification and visible context, never as an instruction or a required opinion. Let this NPC's established personality, relationship, location, season, and current situation determine naturally what they notice and how they respond.";

        public static readonly string FallbackVanillaHatMemoryRule =
            "MEMORY — IMPORTANT: {HatMemory}\nThis memory is mandatory context: the NPC already has history with this exact hat. Weave that recognition into the reaction (familiarity, a callback to before, etc.) in the NPC's own voice. Do NOT write the line as a first-time discovery.";

        public static readonly string FallbackSpecialItemMemoryRule =
            "MEMORY — IMPORTANT: {ItemMemory}\nThis memory is mandatory context: the NPC already has history with this exact item. Do NOT write the line as if seeing it for the first time. Weave that recognition into the reaction — show familiarity, reference it as something they remember, or express a running opinion about it. The more times seen, the more established and natural that opinion should feel.";

        // ── Live properties ───────────────────────────────────────────────────

        public static readonly string FallbackReactionCoreRule =
            "React directly to the farmer's current appearance, visible theme, situation, or overall vibe. Choose a concrete in-character reaction angle; mention visual details only when natural and never structure the line like a fashion review.";

        public static readonly string FallbackThemeRecognitionRule =
            "When a clear clue points to a recognizable character, franchise, mascot, creature, animal, food, object, or named style, let the NPC recognize or allude to it only when their knowledge and personality support that. Do not force uncertain recognition or ignore an obvious clue. Prefer a fitting joke, question, roast, concern, guarded admission, or imagined situation over bland praise, and draw comparisons from this NPC's own interests rather than generic Stardew topics.";

        public static readonly string FallbackCombinationAndOccasionRule =
            "Consider a changed item together with any recognizable outfit still being worn, including fitting combinations, clashes, or funny hybrids. Judge clear event or seasonal themes against the matching festival or season, but judge weather gear only against current weather. A clear mismatch may be questioned or teased in character; never force an ambiguous comparison or call a fitting occasion mismatched.";

        public static readonly string FallbackWholeOutfitFocusRule =
            "For a whole saved outfit, react to the complete look, including any clearly visible large accessory. If that accessory reinforces, transforms, or clashes with a concrete recognizable theme supplied by the readable saved-outfit name, use the theme concept or a natural allusion to it; a color-only comparison is insufficient. Relate both naturally instead of ignoring either one. Do not center hair, hair color, a tiny head-slot item, or a generic/internal item ID unless the theme truly revolves around it.";

        public static readonly string FallbackOpeningVarietyRule =
            "OPENING VARIETY RULE: vary the first words, sentence structure, and reaction angle. Do not repeatedly begin with equivalents of 'this look/outfit', generic greetings, grunts, or 'what are you wearing?', and do not make 'it suits you' the main point. Start naturally from this NPC's observation, joke, complaint, concern, question, or admission.";

        public static readonly string FallbackDialoguePacingRule =
            "Use #$b# dialogue-box breaks only when they improve natural pacing. One, two, or several boxes are valid; never force a fixed box count, pad the line, or repeat an idea just to make it longer.";

        public static readonly string FallbackExpressiveCuesAllowedRule =
            "Brief expressive cues in asterisks are allowed when they fit the character and moment. Write them in the same language as the dialogue and never use asterisks as list bullets.";

        public static readonly string FallbackExpressiveCuesDisabledRule =
            "Do not use asterisks for actions or physical cues. Write only clean spoken dialogue.";

        public static readonly string FallbackPunctuationRule =
            "Punctuation rule: use '...' for dramatic pauses, hesitation, trailing off, or unfinished thoughts; never use a lone period as a pause inside a sentence.";

        public static readonly string FallbackIndoorWeatherRule =
            "Weather/location rule: the NPC and farmer are indoors and sheltered. Rain, storms, snow, or similar weather is outside, never happening inside the room. Mention it only when it naturally matters.";

        public static readonly string FallbackOutdoorWeatherRule =
            "Weather/location rule: the NPC and farmer are outdoors and directly exposed to the stated weather. It may be described as happening around them when relevant.";

        public static readonly string FallbackActiveFestivalPresenceRule =
            "ACTIVE FESTIVAL RIGHT NOW (authoritative live event): {ActiveFestivalName}. The farmer and speaking NPC are physically attending it together in the current location. This overrides ambiguity from a generic map name.";

        public static readonly string FallbackActiveFestivalOutfitRule =
            "ACTIVE FESTIVAL OUTFIT RULE: first judge any clear costume, creature, symbolic, seasonal, or themed look against {ActiveFestivalName}. If its recognizable motifs or overall concept reasonably fit this festival, treat it as timely and intentional; call it mismatched only when it clearly belongs elsewhere. This is a private scene fact, not a required topic or opening: the NPC may mention the festival or react through a specific detail, joke, question, or feeling. Never say the active festival passed, is upcoming, or is not taking place. Do not force a comparison for an ambiguous look.";

        public string HairChangeMode      { get; private set; } = FallbackHairChangeMode;
        public string HatChangeMode       { get; private set; } = FallbackHatChangeMode;
        public string AccessoryChangeMode { get; private set; } = FallbackAccessoryChangeMode;
        public string OutfitChangeMode    { get; private set; } = FallbackOutfitChangeMode;
        public string NaturalReactionStyle { get; private set; } = CompactNaturalReactionStyle;

        public string PlayerKnownAddressRule { get; private set; } = FallbackPlayerKnownAddressRule;
        public string PlayerUnknownAddressRule { get; private set; } = FallbackPlayerUnknownAddressRule;
        public string PlayerGenderRule { get; private set; } = FallbackPlayerGenderRule;
        public string VisibleVanillaHatOnlyMode { get; private set; } = FallbackVisibleVanillaHatOnlyMode;
        public string RemovedVanillaHatOnlyMode { get; private set; } = FallbackRemovedVanillaHatOnlyMode;
        public string SavedOutfitFocusGuidance { get; private set; } = FallbackSavedOutfitFocusGuidance;
        public string HairFocusGuidance { get; private set; } = FallbackHairFocusGuidance;
        public string HatFocusGuidance { get; private set; } = FallbackHatFocusGuidance;
        public string AccessoryFocusGuidance { get; private set; } = FallbackAccessoryFocusGuidance;
        public string FashionSenseVisualSupportRule { get; private set; } = CompactFashionSenseVisualSupportRule;
        public string FashionSenseVisualSeparationRule { get; private set; } = CompactFashionSenseVisualSeparationRule;
        public string SpecialItemVisibleRule { get; private set; } = FallbackSpecialItemVisibleRule;
        public string SpecialItemRemovedRule { get; private set; } = FallbackSpecialItemRemovedRule;
        public string SpecialVanillaHatRule { get; private set; } = FallbackSpecialVanillaHatRule;
        public string VanillaHatMemoryRule { get; private set; } = FallbackVanillaHatMemoryRule;
        public string SpecialItemMemoryRule { get; private set; } = FallbackSpecialItemMemoryRule;
        public string ReactionCoreRule { get; private set; } = FallbackReactionCoreRule;
        public string ThemeRecognitionRule { get; private set; } = FallbackThemeRecognitionRule;
        public string CombinationAndOccasionRule { get; private set; } = FallbackCombinationAndOccasionRule;
        public string WholeOutfitFocusRule { get; private set; } = FallbackWholeOutfitFocusRule;
        public string OpeningVarietyRule { get; private set; } = FallbackOpeningVarietyRule;
        public string DialoguePacingRule { get; private set; } = FallbackDialoguePacingRule;
        public string ExpressiveCuesAllowedRule { get; private set; } = FallbackExpressiveCuesAllowedRule;
        public string ExpressiveCuesDisabledRule { get; private set; } = FallbackExpressiveCuesDisabledRule;
        public string PunctuationRule { get; private set; } = FallbackPunctuationRule;
        public string IndoorWeatherRule { get; private set; } = FallbackIndoorWeatherRule;
        public string OutdoorWeatherRule { get; private set; } = FallbackOutdoorWeatherRule;
        public string ActiveFestivalPresenceRule { get; private set; } = FallbackActiveFestivalPresenceRule;
        public string ActiveFestivalOutfitRule { get; private set; } = FallbackActiveFestivalOutfitRule;

        // ── Loading ───────────────────────────────────────────────────────────

        private readonly IMonitor monitor;
        private readonly string promptsFilePath;

        public PromptStyleService(IModHelper helper, IMonitor monitor)
        {
            this.monitor = monitor;
            promptsFilePath = Path.Combine(helper.DirectoryPath, "assets", "prompts", "prompts.json");
        }

        /// <summary>
        /// Loads <c>assets/prompts/prompts.json</c>. Missing or malformed
        /// files are logged and silently ignored — defaults stay in effect.
        /// Call this once at mod startup and again whenever assets are
        /// invalidated (Content Patcher reload, etc.).
        /// </summary>
        public void Load(bool quiet = false)
        {
            if (!File.Exists(promptsFilePath))
            {
                if (!quiet)
                    monitor.Log("[Prompts] prompts.json not found — using built-in defaults.", LogLevel.Trace);
                ResetToDefaults();
                return;
            }

            try
            {
                string raw = File.ReadAllText(promptsFilePath, System.Text.Encoding.UTF8);

                // Strip JS-style line comments (// …) so the file is user-friendly.
                raw = System.Text.RegularExpressions.Regex.Replace(
                    raw, @"^\s*//[^\r\n]*", "", System.Text.RegularExpressions.RegexOptions.Multiline);

                JsonSerializerOptions opts = new()
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                var data = JsonSerializer.Deserialize<PromptStyleData>(raw, opts);

                if (data == null)
                {
                    monitor.Log("[Prompts] prompts.json deserialized as null — using built-in defaults.", LogLevel.Warn);
                    ResetToDefaults();
                    return;
                }

                HairChangeMode       = Coalesce(data.HairChangeMode,       FallbackHairChangeMode);
                HatChangeMode        = Coalesce(data.HatChangeMode,        FallbackHatChangeMode);
                AccessoryChangeMode  = Coalesce(data.AccessoryChangeMode,  FallbackAccessoryChangeMode);
                OutfitChangeMode     = Coalesce(data.OutfitChangeMode,     FallbackOutfitChangeMode);
                NaturalReactionStyle = UseCompactBuiltInBlock(
                    Coalesce(data.NaturalReactionStyle, CompactNaturalReactionStyle),
                    "This is an honest REACTION, not a compliment.",
                    4000,
                    CompactNaturalReactionStyle);
                PlayerKnownAddressRule = Coalesce(data.PlayerKnownAddressRule, FallbackPlayerKnownAddressRule);
                PlayerUnknownAddressRule = Coalesce(data.PlayerUnknownAddressRule, FallbackPlayerUnknownAddressRule);
                PlayerGenderRule = Coalesce(data.PlayerGenderRule, FallbackPlayerGenderRule);
                VisibleVanillaHatOnlyMode = Coalesce(data.VisibleVanillaHatOnlyMode, FallbackVisibleVanillaHatOnlyMode);
                RemovedVanillaHatOnlyMode = Coalesce(data.RemovedVanillaHatOnlyMode, FallbackRemovedVanillaHatOnlyMode);
                SavedOutfitFocusGuidance = Coalesce(data.SavedOutfitFocusGuidance, FallbackSavedOutfitFocusGuidance);
                HairFocusGuidance = Coalesce(data.HairFocusGuidance, FallbackHairFocusGuidance);
                HatFocusGuidance = Coalesce(data.HatFocusGuidance, FallbackHatFocusGuidance);
                AccessoryFocusGuidance = Coalesce(data.AccessoryFocusGuidance, FallbackAccessoryFocusGuidance);
                FashionSenseVisualSupportRule = UseCompactBuiltInBlock(
                    Coalesce(data.FashionSenseVisualSupportRule, FallbackFashionSenseVisualSupportRule),
                    "Fashion Sense API visual support data.",
                    900,
                    CompactFashionSenseVisualSupportRule);
                FashionSenseVisualSeparationRule = UseCompactBuiltInBlock(
                    Coalesce(data.FashionSenseVisualSeparationRule, FallbackFashionSenseVisualSeparationRule),
                    "IMPORTANT: the 'hair' entry",
                    900,
                    CompactFashionSenseVisualSeparationRule);
                SpecialItemVisibleRule = Coalesce(data.SpecialItemVisibleRule, FallbackSpecialItemVisibleRule);
                SpecialItemRemovedRule = Coalesce(data.SpecialItemRemovedRule, FallbackSpecialItemRemovedRule);
                SpecialVanillaHatRule = Coalesce(data.SpecialVanillaHatRule, FallbackSpecialVanillaHatRule);
                VanillaHatMemoryRule = Coalesce(data.VanillaHatMemoryRule, FallbackVanillaHatMemoryRule);
                SpecialItemMemoryRule = Coalesce(data.SpecialItemMemoryRule, FallbackSpecialItemMemoryRule);
                ReactionCoreRule = Coalesce(data.ReactionCoreRule, FallbackReactionCoreRule);
                ThemeRecognitionRule = Coalesce(data.ThemeRecognitionRule, FallbackThemeRecognitionRule);
                CombinationAndOccasionRule = Coalesce(data.CombinationAndOccasionRule, FallbackCombinationAndOccasionRule);
                WholeOutfitFocusRule = Coalesce(data.WholeOutfitFocusRule, FallbackWholeOutfitFocusRule);
                OpeningVarietyRule = Coalesce(data.OpeningVarietyRule, FallbackOpeningVarietyRule);
                DialoguePacingRule = Coalesce(data.DialoguePacingRule, FallbackDialoguePacingRule);
                ExpressiveCuesAllowedRule = Coalesce(data.ExpressiveCuesAllowedRule, FallbackExpressiveCuesAllowedRule);
                ExpressiveCuesDisabledRule = Coalesce(data.ExpressiveCuesDisabledRule, FallbackExpressiveCuesDisabledRule);
                PunctuationRule = Coalesce(data.PunctuationRule, FallbackPunctuationRule);
                IndoorWeatherRule = Coalesce(data.IndoorWeatherRule, FallbackIndoorWeatherRule);
                OutdoorWeatherRule = Coalesce(data.OutdoorWeatherRule, FallbackOutdoorWeatherRule);
                ActiveFestivalPresenceRule = Coalesce(data.ActiveFestivalPresenceRule, FallbackActiveFestivalPresenceRule);
                ActiveFestivalOutfitRule = Coalesce(data.ActiveFestivalOutfitRule, FallbackActiveFestivalOutfitRule);

                if (!quiet)
                    monitor.Log("[Prompts] prompts.json loaded successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                monitor.Log("[Prompts] Failed to load prompts.json — using built-in defaults. Error: " + ex.Message, LogLevel.Warn);
                ResetToDefaults();
            }
        }

        private void ResetToDefaults()
        {
            HairChangeMode       = FallbackHairChangeMode;
            HatChangeMode        = FallbackHatChangeMode;
            AccessoryChangeMode  = FallbackAccessoryChangeMode;
            OutfitChangeMode     = FallbackOutfitChangeMode;
            NaturalReactionStyle = CompactNaturalReactionStyle;
            PlayerKnownAddressRule = FallbackPlayerKnownAddressRule;
            PlayerUnknownAddressRule = FallbackPlayerUnknownAddressRule;
            PlayerGenderRule = FallbackPlayerGenderRule;
            VisibleVanillaHatOnlyMode = FallbackVisibleVanillaHatOnlyMode;
            RemovedVanillaHatOnlyMode = FallbackRemovedVanillaHatOnlyMode;
            SavedOutfitFocusGuidance = FallbackSavedOutfitFocusGuidance;
            HairFocusGuidance = FallbackHairFocusGuidance;
            HatFocusGuidance = FallbackHatFocusGuidance;
            AccessoryFocusGuidance = FallbackAccessoryFocusGuidance;
            FashionSenseVisualSupportRule = CompactFashionSenseVisualSupportRule;
            FashionSenseVisualSeparationRule = CompactFashionSenseVisualSeparationRule;
            SpecialItemVisibleRule = FallbackSpecialItemVisibleRule;
            SpecialItemRemovedRule = FallbackSpecialItemRemovedRule;
            SpecialVanillaHatRule = FallbackSpecialVanillaHatRule;
            VanillaHatMemoryRule = FallbackVanillaHatMemoryRule;
            SpecialItemMemoryRule = FallbackSpecialItemMemoryRule;
            ReactionCoreRule = FallbackReactionCoreRule;
            ThemeRecognitionRule = FallbackThemeRecognitionRule;
            CombinationAndOccasionRule = FallbackCombinationAndOccasionRule;
            WholeOutfitFocusRule = FallbackWholeOutfitFocusRule;
            OpeningVarietyRule = FallbackOpeningVarietyRule;
            DialoguePacingRule = FallbackDialoguePacingRule;
            ExpressiveCuesAllowedRule = FallbackExpressiveCuesAllowedRule;
            ExpressiveCuesDisabledRule = FallbackExpressiveCuesDisabledRule;
            PunctuationRule = FallbackPunctuationRule;
            IndoorWeatherRule = FallbackIndoorWeatherRule;
            OutdoorWeatherRule = FallbackOutdoorWeatherRule;
            ActiveFestivalPresenceRule = FallbackActiveFestivalPresenceRule;
            ActiveFestivalOutfitRule = FallbackActiveFestivalOutfitRule;
        }

        private static string Coalesce(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string UseCompactBuiltInBlock(string value, string legacyPrefix, int legacyMinimumLength, string compact)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && value.Length >= legacyMinimumLength
                && value.StartsWith(legacyPrefix, StringComparison.Ordinal))
            {
                return compact;
            }

            // Preserve genuinely customized prompt blocks from content authors.
            return value;
        }

        // ── Internal DTO ──────────────────────────────────────────────────────

        private sealed class PromptStyleData
        {
            public string HairChangeMode       { get; set; }
            public string HatChangeMode        { get; set; }
            public string AccessoryChangeMode  { get; set; }
            public string OutfitChangeMode     { get; set; }
            public string NaturalReactionStyle { get; set; }
            public string PlayerKnownAddressRule { get; set; }
            public string PlayerUnknownAddressRule { get; set; }
            public string PlayerGenderRule { get; set; }
            public string VisibleVanillaHatOnlyMode { get; set; }
            public string RemovedVanillaHatOnlyMode { get; set; }
            public string SavedOutfitFocusGuidance { get; set; }
            public string HairFocusGuidance { get; set; }
            public string HatFocusGuidance { get; set; }
            public string AccessoryFocusGuidance { get; set; }
            public string FashionSenseVisualSupportRule { get; set; }
            public string FashionSenseVisualSeparationRule { get; set; }
            public string SpecialItemVisibleRule { get; set; }
            public string SpecialItemRemovedRule { get; set; }
            public string SpecialVanillaHatRule { get; set; }
            public string VanillaHatMemoryRule { get; set; }
            public string SpecialItemMemoryRule { get; set; }
            public string ReactionCoreRule { get; set; }
            public string ThemeRecognitionRule { get; set; }
            public string CombinationAndOccasionRule { get; set; }
            public string WholeOutfitFocusRule { get; set; }
            public string OpeningVarietyRule { get; set; }
            public string DialoguePacingRule { get; set; }
            public string ExpressiveCuesAllowedRule { get; set; }
            public string ExpressiveCuesDisabledRule { get; set; }
            public string PunctuationRule { get; set; }
            public string IndoorWeatherRule { get; set; }
            public string OutdoorWeatherRule { get; set; }
            public string ActiveFestivalPresenceRule { get; set; }
            public string ActiveFestivalOutfitRule { get; set; }
        }
    }
}
