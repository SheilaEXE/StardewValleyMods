using System.Collections.Generic;

namespace OutfitReactions
{
    public sealed class OutfitThemeRule
    {
        public string DialogueKey { get; set; } = "";
        public int Priority { get; set; } = 100;
        public List<string> Keywords { get; set; } = new();

        // Optional context for AI generation. This helps custom theme packs explain
        // the vibe of a new theme without hardcoding it inside the DLL.
        public string ThemeName { get; set; } = "";
        public string PromptHint { get; set; } = "";

        // Optional written-dialogue variants. If empty, the mod falls back to suffix keys:
        // DialogueKey + "Inside"    => indoor locations except FarmHouse and NpcRoom.
        // DialogueKey + "Outside"   => outdoor locations only.
        // DialogueKey + "FarmHouse" => FarmHouse only.
        // DialogueKey + "NpcRoom"   => marriage-candidate NPC rooms.
        // DialogueKey + "Beach"     => optional special beach/island outdoor variant.
        public string InsideDialogueKey { get; set; } = "";
        public string OutsideDialogueKey { get; set; } = "";
        public string FarmHouseDialogueKey { get; set; } = "";
        public string NpcRoomDialogueKey { get; set; } = "";
        public string BeachDialogueKey { get; set; } = "";

        // Legacy fields kept for older content packs. They are now treated as FarmHouse.
        public string IndoorsDialogueKey { get; set; } = "";
        public string IndoorDialogueKey { get; set; } = "";

        public bool UseOutsideVariant { get; set; } = true;
    }
}
