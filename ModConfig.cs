using StardewModdingAPI.Utilities;

namespace NPCComeToMe
{
    /// <summary>As opções configuráveis do mod (editáveis via GMCM ou config.json).</summary>
    public sealed class ModConfig
    {
        /// <summary>Tecla (ou combinação) que abre a caixa de digitação do nome do NPC.</summary>
        public KeybindList OpenMenuKey { get; set; } = KeybindList.Parse("F6");

        /// <summary>Tecla (ou combinação) que libera o NPC mirado pelo cursor, devolvendo-o à rotina normal.</summary>
        public KeybindList ReleaseNpcKey { get; set; } = KeybindList.Parse("F7");
    }
}
