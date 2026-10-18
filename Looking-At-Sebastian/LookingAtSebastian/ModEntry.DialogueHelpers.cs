using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private void SetClickableDialogue(NPC npc, string line)
    {
        if (npc.Name != "Sebastian")
            return;

        // Guarda nosso diálogo para o patch abrir primeiro.
        this.PendingSebastianClickableDialogue = line;
        this.PendingSebastianClickableDialogueIsPc = false;

        // Limpa diálogos antigos para o balãozinho indicar o nosso diálogo.
        npc.CurrentDialogue.Clear();

        // Coloca o diálogo cru na fila do NPC para aparecer o balãozinho.
        // O patch ainda vai interceptar o clique e abrir nosso diálogo primeiro.
        npc.CurrentDialogue.Push(new Dialogue(npc, null, line));

        if (this.Config.DebugLogs)
            this.Monitor.Log("Custom Sebastian dialogue is now pending and visible.", LogLevel.Info);
    }

    private void SetPcClickableDialogue(NPC npc, string line)
    {
        if (npc.Name != "Sebastian")
            return;

        // Guarda nosso diálogo para o patch abrir primeiro.
        this.PendingSebastianClickableDialogue = line;
        this.PendingSebastianClickableDialogueIsPc = true;

        // Mantém o CurrentDialogue só para o balãozinho aparecer, mas o patch
        // intercepta o clique antes do vanilla processar a fala.
        npc.CurrentDialogue.Clear();
        npc.CurrentDialogue.Push(new Dialogue(npc, null, line));

        if (this.Config.DebugLogs)
            this.Monitor.Log("PC Sebastian dialogue is now pending and visible.", LogLevel.Info);
    }
    internal bool TryOpenPendingSebastianDialogue(NPC npc, Farmer who, ref bool result)
    {
        if (npc.Name != "Sebastian")
            return true;

        if (string.IsNullOrWhiteSpace(this.PendingSebastianClickableDialogue))
            return true;

        string line = this.PendingSebastianClickableDialogue;
        bool isPcDialogue = this.PendingSebastianClickableDialogueIsPc;

        this.PendingSebastianClickableDialogue = null;
        this.PendingSebastianClickableDialogueIsPc = false;

        npc.CurrentDialogue.Clear();

        Game1.activeClickableMenu = new DialogueBox(new Dialogue(npc, null, line));

        // Conversar com NPC sentado no PC pode fazer o vanilla tentar levantar/virar
        // a sprite. Se for diálogo especial do PC, restaura a animação imediatamente
        // e o OnUpdateTicked continua monitorando enquanto a sequência estiver ativa.
        if (isPcDialogue && this.IsSebastianAtComputer(npc))
            this.MaintainSebastianPcAnimationIfNeeded(npc);

        result = true;
        return false;
    }
    private string? GetRandomDialogue(string prefix, int count)
    {
        if (count <= 0)
            return null;

        for (int tries = 0; tries < count; tries++)
        {
            int index = Game1.random.Next(1, count + 1);
            string key = $"{prefix}.{index}";

            Translation translation = this.Helper.Translation.Get(key);

            if (!translation.HasValue())
                continue;

            return translation.ToString().Replace("@", Game1.player.Name);
        }

        return null;
    }
    private (string Prefix, int Count) GetCloseDialoguePrefixAndCount(CloseStareScenario scenario, int hearts, bool isDating)
    {
        // Namorando com 8+ corações.
        if (isDating && hearts >= 8)
        {
            return scenario switch
            {
                CloseStareScenario.Back => ("DatingCloseBack8.Sebastian", this.Config.DatingCloseBack8DialogueCount),
                CloseStareScenario.Front => ("DatingCloseFront8.Sebastian", this.Config.DatingCloseFront8DialogueCount),
                _ => ("DatingCloseSide8.Sebastian", this.Config.DatingCloseSide8DialogueCount)
            };
        }

        // Pré-namoro com 8+ corações.
        if (hearts >= 8)
        {
            return scenario switch
            {
                CloseStareScenario.Back => ("preDatingCloseBack8.Sebastian", this.Config.CloseBack8DialogueCount),
                CloseStareScenario.Front => ("preDatingCloseFront8.Sebastian", this.Config.CloseFront8DialogueCount),
                _ => ("preDatingCloseSide8.Sebastian", this.Config.CloseSide8DialogueCount)
            };
        }

        // Pré-namoro com 6 ou 7 corações.
        if (hearts >= 6)
        {
            return scenario switch
            {
                CloseStareScenario.Back => ("preDatingCloseBack6.Sebastian", this.Config.CloseBack6DialogueCount),
                CloseStareScenario.Front => ("preDatingCloseFront6.Sebastian", this.Config.CloseFront6DialogueCount),
                _ => ("preDatingCloseSide6.Sebastian", this.Config.CloseSide6DialogueCount)
            };
        }

        // Pré-namoro com 4 ou 5 corações.
        if (hearts >= 4)
        {
            return scenario switch
            {
                CloseStareScenario.Back => ("preDatingCloseBack4.Sebastian", this.Config.CloseBack4DialogueCount),
                CloseStareScenario.Front => ("preDatingCloseFront4.Sebastian", this.Config.CloseFront4DialogueCount),
                _ => ("preDatingCloseSide4.Sebastian", this.Config.CloseSide4DialogueCount)
            };
        }

        // Pré-namoro com 2 ou 3 corações.
        return scenario switch
        {
            CloseStareScenario.Back => ("preDatingCloseBack2.Sebastian", this.Config.CloseBack2DialogueCount),
            CloseStareScenario.Front => ("preDatingCloseFront2.Sebastian", this.Config.CloseFront2DialogueCount),
            _ => ("preDatingCloseSide2.Sebastian", this.Config.CloseSide2DialogueCount)
        };
    }

    private string? GetCloseDialogueFallback(CloseStareScenario scenario, int hearts, bool isDating)
    {
        // Se estiver namorando e faltar Dating8, tenta preDating8.
        if (isDating && hearts >= 8)
        {
            string? datingFallback = scenario switch
            {
                CloseStareScenario.Back => this.GetRandomDialogue("preDatingCloseBack8.Sebastian", this.Config.CloseBack8DialogueCount),
                CloseStareScenario.Front => this.GetRandomDialogue("preDatingCloseFront8.Sebastian", this.Config.CloseFront8DialogueCount),
                _ => this.GetRandomDialogue("preDatingCloseSide8.Sebastian", this.Config.CloseSide8DialogueCount)
            };

            if (!string.IsNullOrWhiteSpace(datingFallback))
                return datingFallback;
        }
        // Fallback em cascata: 8 → 6 → 4 → 2.
        if (hearts >= 8)
        {
            string? line8 = scenario switch
            {
                CloseStareScenario.Back => this.GetRandomDialogue("preDatingCloseBack8.Sebastian", this.Config.CloseBack8DialogueCount),
                CloseStareScenario.Front => this.GetRandomDialogue("preDatingCloseFront8.Sebastian", this.Config.CloseFront8DialogueCount),
                _ => this.GetRandomDialogue("preDatingCloseSide8.Sebastian", this.Config.CloseSide8DialogueCount)
            };

            if (!string.IsNullOrWhiteSpace(line8))
                return line8;
        }

        if (hearts >= 6)
        {
            string? line6 = scenario switch
            {
                CloseStareScenario.Back => this.GetRandomDialogue("preDatingCloseBack6.Sebastian", this.Config.CloseBack6DialogueCount),
                CloseStareScenario.Front => this.GetRandomDialogue("preDatingCloseFront6.Sebastian", this.Config.CloseFront6DialogueCount),
                _ => this.GetRandomDialogue("preDatingCloseSide6.Sebastian", this.Config.CloseSide6DialogueCount)
            };

            if (!string.IsNullOrWhiteSpace(line6))
                return line6;
        }

        if (hearts >= 4)
        {
            string? line4 = scenario switch
            {
                CloseStareScenario.Back => this.GetRandomDialogue("preDatingCloseBack4.Sebastian", this.Config.CloseBack4DialogueCount),
                CloseStareScenario.Front => this.GetRandomDialogue("preDatingCloseFront4.Sebastian", this.Config.CloseFront4DialogueCount),
                _ => this.GetRandomDialogue("preDatingCloseSide4.Sebastian", this.Config.CloseSide4DialogueCount)
            };

            if (!string.IsNullOrWhiteSpace(line4))
                return line4;
        }

        return scenario switch
        {
            CloseStareScenario.Back => this.GetRandomDialogue("preDatingCloseBack2.Sebastian", this.Config.CloseBack2DialogueCount),
            CloseStareScenario.Front => this.GetRandomDialogue("preDatingCloseFront2.Sebastian", this.Config.CloseFront2DialogueCount),
            _ => this.GetRandomDialogue("preDatingCloseSide2.Sebastian", this.Config.CloseSide2DialogueCount)
        };
    }
    private (string? Line, string? Key) GetRandomDialogueWithKey(string prefix, int count)
    {
        if (count <= 0)
            return (null, null);

        for (int tries = 0; tries < count; tries++)
        {
            int index = Game1.random.Next(1, count + 1);
            string key = $"{prefix}.{index}";

            Translation translation = this.Helper.Translation.Get(key);

            if (!translation.HasValue())
                continue;

            string line = translation.ToString().Replace("@", Game1.player.Name);
            return (line, key);
        }

        return (null, null);
    }

    private bool IsDatingFrontCounterDialogueKey(string? key)
    {
        return key is
            "DatingCloseFront8.Sebastian.1" or
            "DatingCloseFront8.Sebastian.5" or
            "DatingCloseFront8.Sebastian.7" or
            "DatingCloseFront8.Sebastian.10";
    }
}