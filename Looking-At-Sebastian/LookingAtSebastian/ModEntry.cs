using StardewModdingAPI;
using HarmonyLib;

namespace LookingAtSebastian;

public sealed partial class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        Instance = this;

        this.Config = new ModConfig();

        var harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.PatchAll();

        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Player.Warped += this.OnWarped;

        this.Monitor.Log("Looking at Sebastian carregado.", LogLevel.Info);
    }
}

