using StardewValley;
using System;
using System.Collections.Generic;

namespace OutfitReactions;

/// <summary>
/// Controls which NPC profiles may participate in AI-generated outfit reactions.
/// Public builds remain restricted unless the local build explicitly defines
/// <c>OUTFIT_REACTIONS_UNRESTRICTED</c>.
/// </summary>
internal static class NpcCompatibilityPolicy
{
#if OUTFIT_REACTIONS_UNRESTRICTED
    private static readonly bool AllowAnyProfile = true;
#else
    private static readonly bool AllowAnyProfile = false;
#endif

    private static readonly HashSet<string> SupportedNpcNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Built-in Outfit Reactions profiles.
        "Abigail", "Alex", "Caroline", "Clint", "Demetrius", "Elliott", "Emily",
        "Evelyn", "George", "Gus", "Haley", "Harvey", "Jas", "Jodi", "Kent",
        "Leah", "Lewis", "Linus", "Marnie", "Maru", "Pam", "Penny", "Pierre",
        "Robin", "Sam", "Sandy", "Sebastian", "Shane", "Vincent", "Willy", "Wizard",

        // NatrollEXE.OutfitReactions.SVEProfiles.
        "Alesia", "Andy", "Apples", "Brianna", "Camilla", "Claire", "Drake", "Edmund",
        "Gale", "Gunther", "Hank", "Isaac", "Jadu", "Jolyne", "Lance", "Magnus",
        "Marlon", "Martin", "Morgan", "Morris", "Olivia", "Scarlett", "Sophia",
        "Susan", "Treyvon", "Victor"
    };

    public static bool IsUnrestricted => AllowAnyProfile;

    public static bool Allows(NPC npc)
    {
        return npc != null && Allows(npc.Name);
    }

    public static bool Allows(string npcName)
    {
        if (AllowAnyProfile)
            return !string.IsNullOrWhiteSpace(npcName);

        return !string.IsNullOrWhiteSpace(npcName)
            && SupportedNpcNames.Contains(npcName.Trim());
    }
}
