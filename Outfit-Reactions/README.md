# Outfit Reactions

A **Stardew Valley** mod that makes NPCs dynamically comment on your character's clothes whenever you change your look, designed exclusively for [Fashion Sense](https://www.nexusmods.com/stardewvalley/mods/9969).

> **Outfit Reactions only adds optional reactions to the player's clothes and appearance.** It never replaces, rewrites, or removes dialogue from the base game or from other mods; every conversation the NPC would normally have remains available. Handwritten dialogue is the result of authors investing their time, care, and creativity into bringing these characters to life, and that work deserves to be preserved. Outfit Reactions is designed to sit alongside those conversations with a small extra moment about your current look, never to take their place.

---

## ✨ Features

* **AI-powered reactions**: spouses and nearby NPCs react to your current outfit using AI, generating unique lines whenever you change clothes.
* **Vanilla and modded clothing support**: content packs can add focused reactions for hats, shirts, pants, and shoes from the base game, or other mods.
* **Outfit memory**: NPCs remember outfits and special items they have seen before, reacting with familiarity instead of repeating a first-time reaction.
* **Configurable through Generic Mod Config Menu** (optional), with combined or special-item-focused reaction modes, multiple AI profiles, and more.

## 📋 Requirements

* [SMAPI](https://smapi.io/) 4.0.0+
* [Fashion Sense](https://www.nexusmods.com/stardewvalley/mods/9969) 7.5.0+ (required)
* [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) (optional, but recommended for easy configuration)
* Tile Marker 1.1.0+ (optional, for choosing furniture and map tiles that should not block an NPC's view)
* A personal API key from a compatible AI provider. Profile 1 is enabled by default with Google Gemini and the `gemini-3.1-flash-lite` model, so new players only need to add their own Gemini API key.

## 🔧 Installation

1. Install [SMAPI](https://smapi.io/).
2. Download and install Fashion Sense.
3. Download the latest Outfit Reactions release and extract its folder into your `Mods` directory.
4. Open Generic Mod Config Menu and paste your Gemini API key into Profile 1. The provider and recommended low-cost model are already selected. Advanced users can choose a different provider or model instead.

## ⚙️ Configuration

The mod can be configured through Generic Mod Config Menu, including:

* AI profiles with provider, model, API key, and custom endpoint settings.
* Optional OpenRouter input/output price ceilings and allowed-provider lists per profile. Enter provider slugs separated by commas; requests use only those providers within the chosen USD-per-million-token ceilings, and fail if none qualify. An empty list allows any provider within the ceilings. Legacy blocked-provider lists are discarded, not converted into allowed lists.
* Reaction modes for vanilla hats and special items, either combined with the full outfit or focused only on the special item.
* Visual analysis (vision) for models that support it.
  * **Highly recommended:** Use a vision-capable AI model. This mod can capture images of your farmer so the model can see what you're actually wearing and use those details in its reaction. Text-only models can still work, but they miss that visual context.
  * **NOTE:** My mod pays a lot of attention to names. The saved outfit's name and the names of any vanilla pieces are some of the first clues the AI gets. A red look called **"Apple outfit"** can nudge it toward apples, while a flower-covered look called **"Floral Outfit"** points it toward flowers. Without a clear name, the AI ​​might still get it right depending on details, mood, season, and so on, but there is a significant chance it could fixate on a different detail and end up imagining an entirely different theme.. Remember that an AI doesn't quite see things exactly the same way we do, so a name that conveys your intended concept is a huge help.
* Additional reaction frequency and behavior settings.

> **Dialogue quality depends on the AI model you choose.** My mod provides detailed character profiles, visual context, and writing rules, but the model is ultimately responsible for following them. More capable models generally produce more natural, varied, context-aware, and character-accurate reactions, while smaller or cheaper models may be more repetitive or miss subtle details.


## 🚧 Development Status and Custom NPC Compatibility

This mod is still being actively improved, but it has already reached a stable and enjoyable enough stage to be used normally. New improvements, refinements, and fixes may continue to be added as the mod develops.

The mod supports its included base-game NPC profiles, while compatibility with custom NPCs is currently limited to **Stardew Valley Expanded** through the official Outfit Reactions SVE profile pack.

Other custom NPC mods are intentionally not supported. Many mod authors do not want their characters, writing, or other creative work involved with AI-generated content. In the same spirit of respect for the time and heart authors put into their characters, Outfit Reactions does not automatically generate reactions for NPCs from other mods. This is an intentional restriction, not a compatibility bug. Support for another custom NPC mod would only be considered with clear permission from its author.

## 📜 License

This project is licensed under the [MIT License](LICENSE). You are free to use, modify, and distribute it as long as the original credits are preserved.
