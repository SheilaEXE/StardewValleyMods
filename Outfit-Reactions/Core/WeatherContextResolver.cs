using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StardewValley;

namespace OutfitReactions;

internal static class WeatherContextResolver
{
	private static readonly Dictionary<string, string> WeatherWondersDescriptions = new(StringComparer.OrdinalIgnoreCase)
	{
		["Kana.WeatherWonders_AcidRain"] = "acid rain (corrosive green rainfall)",
		["Kana.WeatherWonders_Blizzard"] = "blizzard (heavy wind-driven snow and severe cold)",
		["Kana.WeatherWonders_Cloudy"] = "cloudy / overcast",
		["Kana.WeatherWonders_Deluge"] = "deluge (violent heavy rain, thunder, and strong wind)",
		["Kana.WeatherWonders_Drizzle"] = "light drizzle",
		["Kana.WeatherWonders_DryLightning"] = "dry lightning (thunder and lightning without rain)",
		["Kana.WeatherWonders_Hailstorm"] = "hailstorm (falling hail and cold rain)",
		["Kana.WeatherWonders_Heatwave"] = "heatwave (extreme hot weather)",
		["Kana.WeatherWonders_Mist"] = "dense mist / fog",
		["Kana.WeatherWonders_MuddyRain"] = "muddy rain",
		["Kana.WeatherWonders_RainSnowMix"] = "rain-and-snow mix / sleet",
		["Kana.WeatherWonders_Sandstorm"] = "sandstorm (strong wind and blowing sand)",
		["Kana.WeatherWonders_BloodMoon"] = "blood moon lunar event (an unusual red moon is active tonight)",
		["Kana.WeatherWonders_BlueMoon"] = "blue moon lunar event (an unusual blue moon is active tonight)",
		["Kana.WeatherWonders_HarvestMoon"] = "harvest moon lunar event (an unusual golden harvest moon is active tonight)"
	};

	private static readonly HashSet<string> VanillaWeatherIds = new(StringComparer.OrdinalIgnoreCase)
	{
		"Festival",
		"GreenRain",
		"Rain",
		"Snow",
		"Storm",
		"Sun",
		"Wind"
	};

	public static string GetCurrentWeatherForAiPrompt(GameLocation location)
	{
		string weatherId = location?.GetWeather()?.Weather?.Trim();
		if (!string.IsNullOrWhiteSpace(weatherId))
		{
			if (WeatherWondersDescriptions.TryGetValue(weatherId, out string description))
			{
				return description;
			}

			// Preserve the specific weather name from other custom-weather frameworks instead of
			// collapsing it into the vanilla rain/snow/sun behavior used for NPC schedules.
			if (!VanillaWeatherIds.Contains(weatherId))
			{
				string customWeatherName = HumanizeWeatherId(weatherId);
				if (!string.IsNullOrWhiteSpace(customWeatherName))
				{
					return "custom weather: " + customWeatherName;
				}
			}
		}

		return Game1.IsGreenRainingHere(location) ? "green rain" : (Game1.IsLightningHere(location) ? "storm / thunderstorm" : (Game1.IsRainingHere(location) ? "rain" : (Game1.IsSnowingHere(location) ? "snow" : (Game1.IsDebrisWeatherHere(location) ? "windy / debris weather" : "sunny / clear"))));
	}

	private static string HumanizeWeatherId(string weatherId)
	{
		int separatorIndex = Math.Max(weatherId.LastIndexOf('/'), Math.Max(weatherId.LastIndexOf(':'), weatherId.LastIndexOf('_')));
		string name = separatorIndex >= 0 && separatorIndex + 1 < weatherId.Length ? weatherId[(separatorIndex + 1)..] : weatherId;
		name = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
		name = Regex.Replace(name, "[._\\-]+", " ");
		return Regex.Replace(name, "\\s{2,}", " ").Trim().ToLowerInvariant();
	}
}
