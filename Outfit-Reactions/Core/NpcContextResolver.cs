using System;
using System.Collections.Generic;
using StardewValley;

namespace OutfitReactions;

/// <summary>Resolves the NPC instance that is actually active in the current game context.</summary>
internal static class NpcContextResolver
{
	public static IEnumerable<NPC> GetCurrentLocationNpcs()
	{
		HashSet<string> yieldedNames = new(StringComparer.OrdinalIgnoreCase);

		// Prefer event-owned actors when a festival is active. The location character list can
		// contain a different NPC instance with the same name, and reacting through that copy
		// makes emotes/dialogue invisible on the festival map.
		if (Game1.eventUp && Game1.CurrentEvent?.actors != null)
		{
			foreach (NPC actor in Game1.CurrentEvent.actors)
			{
				if (actor != null && !string.IsNullOrWhiteSpace(actor.Name) && yieldedNames.Add(actor.Name))
				{
					yield return actor;
				}
			}
		}

		if (Game1.currentLocation?.characters == null)
		{
			yield break;
		}

		foreach (NPC npc in Game1.currentLocation.characters)
		{
			if (npc != null && !string.IsNullOrWhiteSpace(npc.Name) && yieldedNames.Add(npc.Name))
			{
				yield return npc;
			}
		}
	}

	public static NPC Resolve(string npcName)
	{
		if (string.IsNullOrWhiteSpace(npcName))
		{
			return null;
		}

		// Festivals use event-owned NPC instances which aren't always the same objects returned by
		// Game1.getCharacterFromName. Prefer the visible event actor so location, emotes, and dialogue
		// are applied to the NPC the player actually clicked.
		if (Game1.eventUp && Game1.CurrentEvent?.actors != null)
		{
			foreach (NPC actor in Game1.CurrentEvent.actors)
			{
				if (actor != null && string.Equals(actor.Name, npcName, StringComparison.OrdinalIgnoreCase))
				{
					return actor;
				}
			}
		}

		return Game1.getCharacterFromName(npcName, true, false);
	}
}
