using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace NPCComeToMe
{
    /// <summary>
    /// Recorte mínimo da API pública do Generic Mod Config Menu (GMCM), usado como
    /// soft dependency para gerar a tela de configuração da tecla.
    /// Fonte oficial: https://github.com/spacechase0/StardewValleyMods/blob/develop/GenericModConfigMenu/IGenericModConfigMenuApi.cs
    /// </summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        void AddKeybindList(
            IManifest mod,
            Func<KeybindList> getValue,
            Action<KeybindList> setValue,
            Func<string> name,
            Func<string>? tooltip = null,
            string? fieldId = null
        );
    }
}
