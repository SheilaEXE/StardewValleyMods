using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Pathfinding;

namespace NPCComeToMe
{
    internal sealed class ModEntry : Mod
    {
        /// <summary>
        /// De onde um NPC foi chamado (pra devolvê-lo exatamente ali) e o controller de pathfinding que ele
        /// tinha no momento da captura (pra restaurar a rota real da rotina depois que ele andar de volta).
        /// </summary>
        private readonly record struct OriginalSpot(string LocationName, Vector2 Tile, PathFindController? Controller);

        /// <summary>
        /// Um NPC que já foi liberado e está andando até a porta de saída do mapa atual, esperando cruzar pro
        /// mapa de origem (o próprio jogo faz essa travessia sozinho — só ficamos de olho pra saber quando
        /// ele chega, e então mandar a perna final da caminhada). "TicksWaited" é uma rede de segurança: se
        /// ele ficar preso e nunca cruzar, depois de um tempo desistimos de esperar e teleportamos.
        /// </summary>
        private readonly record struct PendingHomecoming(OriginalSpot Spot, string HudMessage, int TicksWaited);

        /// <summary>Depois de quantos ticks sem cruzar o mapa desistimos de esperar e teleportamos direto (~30s a 60 ticks/s).</summary>
        private const int MaxTicksWaitingToCrossMap = 1800;

        private ModConfig Config = null!;

        /// <summary>NPCs atualmente chamados e congelados, com o lugar de onde vieram (esperando serem liberados).</summary>
        private readonly Dictionary<NPC, OriginalSpot> CapturedNpcs = new();

        /// <summary>NPCs já liberados que estão a caminho da porta de saída, esperando cruzar pro mapa de origem.</summary>
        private readonly Dictionary<NPC, PendingHomecoming> WalkingHome = new();

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();

            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Player.Warped += this.OnPlayerWarped;
        }

        /// <summary>
        /// Reforça o congelamento a cada tick, enquanto o NPC estiver capturado.
        /// Necessário porque NPC.update() recalcula "freezeMotion" sozinho toda hora a partir de
        /// "movementPause" — setar uma vez só não segura em saltos grandes de horário (ex: pular pra 9h30).
        /// </summary>
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            foreach (NPC npc in this.CapturedNpcs.Keys)
            {
                npc.movementPause = int.MaxValue;
                this.SetFreezeMotion(npc, true);
            }

            this.CheckWalkingHomeProgress();
        }

        /// <summary>
        /// A cada tick, checa se algum NPC a caminho de casa já cruzou pro mapa de origem (o jogo faz a
        /// travessia sozinho quando ele esbarra na porta/warp — não tem um evento direto pra isso). Se cruzou,
        /// manda a perna final da caminhada; se está demorando demais (preso em algum lugar), desiste e teleporta.
        /// </summary>
        private void CheckWalkingHomeProgress()
        {
            if (this.WalkingHome.Count == 0)
                return;

            foreach (KeyValuePair<NPC, PendingHomecoming> entry in this.WalkingHome.ToList())
            {
                NPC npc = entry.Key;
                PendingHomecoming pending = entry.Value;

                if (npc.currentLocation?.NameOrUniqueName == pending.Spot.LocationName)
                {
                    this.WalkingHome.Remove(npc);
                    this.WalkFinalLegOrTeleport(npc, pending.Spot, pending.HudMessage);
                    continue;
                }

                if (pending.TicksWaited + 1 > MaxTicksWaitingToCrossMap)
                {
                    this.WalkingHome.Remove(npc);
                    this.TeleportNpcHome(npc, pending.Spot, pending.HudMessage); // ficou preso na travessia: desiste e teleporta
                }
                else
                {
                    this.WalkingHome[npc] = pending with { TicksWaited = pending.TicksWaited + 1 };
                }
            }
        }

        /// <summary>Limpa os NPCs capturados/a caminho ao sair pro menu principal (evita lixo entre partidas).</summary>
        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            this.CapturedNpcs.Clear();
            this.WalkingHome.Clear();
        }

        /// <summary>
        /// Libera automaticamente qualquer NPC capturado que ficou pra trás quando o jogador troca de mapa
        /// (ele fica congelado onde estava, já que não segue o jogador sozinho). Sem isso, o NPC ficaria
        /// preso pra sempre caso o jogador esqueça de liberar antes de sair do local.
        /// </summary>
        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer || this.CapturedNpcs.Count == 0)
                return;

            foreach (NPC npc in this.CapturedNpcs.Keys.Where(npc => npc.currentLocation != e.NewLocation).ToList())
                this.ReleaseNpc(npc, $"{npc.displayName} foi liberado automaticamente porque você saiu do local.");
        }

        /// <summary>Registra a tela de configuração no Generic Mod Config Menu, se ele estiver instalado.</summary>
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null)
                return;

            gmcm.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            gmcm.AddKeybindList(
                mod: this.ModManifest,
                name: () => "Tecla para chamar NPC",
                tooltip: () => "Abre uma caixa para você digitar o nome do NPC que quer teleportar até você.",
                getValue: () => this.Config.OpenMenuKey,
                setValue: value => this.Config.OpenMenuKey = value
            );

            gmcm.AddKeybindList(
                mod: this.ModManifest,
                name: () => "Tecla para liberar NPC",
                tooltip: () => "Mire o cursor no NPC chamado e aperte essa tecla pra ele voltar à rotina normal.",
                getValue: () => this.Config.ReleaseNpcKey,
                setValue: value => this.Config.ReleaseNpcKey = value
            );
        }

        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.activeClickableMenu != null)
                return;

            if (this.Config.OpenMenuKey.JustPressed())
            {
                this.Helper.Input.SuppressActiveKeybinds(this.Config.OpenMenuKey);
                this.OpenNpcNamingMenu();
                return;
            }

            if (this.Config.ReleaseNpcKey.JustPressed())
            {
                this.Helper.Input.SuppressActiveKeybinds(this.Config.ReleaseNpcKey);
                this.ReleaseNpcUnderCursor();
            }
        }

        /// <summary>Abre a caixa de texto nativa do jogo (a mesma usada para nomear bichos de estimação) para digitar o nome do NPC.</summary>
        private void OpenNpcNamingMenu()
        {
            Game1.activeClickableMenu = new NamingMenu(this.OnNpcNamed, "Qual NPC você quer chamar?", "");
        }

        /// <summary>Chamado pelo NamingMenu quando o jogador aperta OK/Enter.</summary>
        private void OnNpcNamed(string enteredName)
        {
            Game1.exitActiveMenu();

            enteredName = enteredName?.Trim() ?? "";
            if (enteredName.Length == 0)
                return;

            NPC? npc = this.FindNpc(enteredName);
            if (npc is null)
            {
                Game1.addHUDMessage(new HUDMessage($"Não encontrei nenhum NPC chamado \"{enteredName}\".", HUDMessage.error_type));
                return;
            }

            this.TeleportNpcToPlayer(npc, Game1.player);
        }

        /// <summary>Procura um NPC pelo nome interno ou nome exibido, em qualquer mapa (incluindo NPCs de outros mods).</summary>
        private NPC? FindNpc(string query)
        {
            // 1) tenta o nome interno exato primeiro (mais rápido e não exige vasculhar tudo)
            NPC? npc = Game1.getCharacterFromName(query, mustBeVillager: false);
            if (npc != null)
                return npc;

            // 2) senão, procura por nome interno OU nome exibido, sem diferenciar maiúsculas/minúsculas
            var todosPersonagens = Utility.getAllCharacters();

            npc = todosPersonagens.FirstOrDefault(n =>
                string.Equals(n.Name, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.displayName, query, StringComparison.OrdinalIgnoreCase));
            if (npc != null)
                return npc;

            // 3) por último, aceita correspondência parcial (ex: digitar "seb" encontra "Sebastian")
            return todosPersonagens.FirstOrDefault(n =>
                n.displayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Move o NPC para um tile livre ao lado do jogador e o congela ali, esperando ser liberado.</summary>
        private void TeleportNpcToPlayer(NPC npc, Farmer player)
        {
            // guarda de onde ele veio e a rota que ele tinha, antes de mexer em qualquer coisa
            var originalSpot = new OriginalSpot(npc.currentLocation.NameOrUniqueName, npc.Tile, npc.controller);

            GameLocation targetLocation = player.currentLocation;
            Vector2 targetTile = this.GetSideTile(player);

            Game1.warpCharacter(npc, targetLocation, targetTile);
            npc.faceDirection(player.FacingDirection);

            // Pausa o movimento sem mexer no controller/Schedule: o jogo continua calculando
            // a rotina dele por baixo dos panos, só não deixa ele efetivamente andar enquanto está "congelado".
            // "freezeMotion" é protected em Character, então precisa de reflection pra setar de fora.
            // O OnUpdateTicked reforça isso a cada tick enquanto o NPC estiver na lista de capturados.
            npc.movementPause = int.MaxValue;
            this.SetFreezeMotion(npc, true);
            this.CapturedNpcs[npc] = originalSpot;

            Game1.addHUDMessage(new HUDMessage($"{npc.displayName} veio até você! Mire nele e aperte a tecla de liberar quando terminar."));
        }

        /// <summary>Procura, entre os NPCs capturados no mapa atual, qual está sob o cursor do mouse, e o libera de volta à rotina.</summary>
        private void ReleaseNpcUnderCursor()
        {
            Farmer player = Game1.player;
            Vector2 cursorPixel = this.Helper.Input.GetCursorPosition().AbsolutePixels;

            NPC? target = this.CapturedNpcs.Keys.FirstOrDefault(npc =>
                npc.currentLocation == player.currentLocation &&
                npc.GetBoundingBox().Contains((int)cursorPixel.X, (int)cursorPixel.Y));

            if (target is null)
            {
                Game1.addHUDMessage(new HUDMessage("Mire o cursor em cima de um NPC chamado pra liberar ele.", HUDMessage.error_type));
                return;
            }

            this.ReleaseNpc(target);
        }

        /// <summary>
        /// Devolve o NPC à rotina normal, tentando fazer ele andar de volta a pé primeiro. Se não der
        /// (sem porta direta pro mapa de origem, ou sem caminho andável), teleporta direto pro local original.
        /// </summary>
        private void ReleaseNpc(NPC npc, string? hudMessage = null)
        {
            if (!this.CapturedNpcs.Remove(npc, out OriginalSpot originalSpot))
                return;

            string finalMessage = hudMessage ?? $"{npc.displayName} voltou pra rotina dele.";

            if (this.TryStartWalkingHome(npc, originalSpot, finalMessage))
                return;

            this.TeleportNpcHome(npc, originalSpot, finalMessage);
        }

        /// <summary>
        /// Descongela o NPC e começa a caminhada de volta: se já estiver no mapa de origem, anda direto até o
        /// tile original (perna final). Senão, anda até a porta/warp que leva ao mapa de origem — a travessia
        /// em si é feita pelo próprio jogo (setamos "NPCSchedule" no controller, que é como o jogo detecta
        /// colisão com a porta e troca de mapa sozinho); "CheckWalkingHomeProgress" fica de olho pra saber
        /// quando ele chegou do outro lado e disparar a perna final. Usa "findPathForNPCSchedules", o mesmo
        /// algoritmo de pathfinding que o jogo usa pras rotinas dos NPCs (desvia de prédio direito; o
        /// pathfinding genérico usado antes deixava o NPC cortar caminho por cima de casas).
        /// </summary>
        private bool TryStartWalkingHome(NPC npc, OriginalSpot originalSpot, string hudMessage)
        {
            GameLocation currentLocation = npc.currentLocation;
            if (currentLocation.NameOrUniqueName == originalSpot.LocationName)
                return this.TryWalkFinalLeg(npc, currentLocation, originalSpot, hudMessage);

            Point? exitTile = this.GetExitTileToward(npc, originalSpot.LocationName);
            if (exitTile is null)
                return false;

            Stack<Point> path = PathFindController.findPathForNPCSchedules(npc.TilePoint, exitTile.Value, currentLocation, 10000, npc);
            if (path is null || path.Count == 0)
                return false;

            npc.movementPause = 0;
            this.SetFreezeMotion(npc, false);

            npc.controller = new PathFindController(path, npc, currentLocation); // este construtor liga "NPCSchedule" sozinho
            this.WalkingHome[npc] = new PendingHomecoming(originalSpot, hudMessage, 0);
            return true;
        }

        /// <summary>Anda direto até o tile original dentro do mapa atual (a etapa final, sem trocar de mapa). Retorna false se não achar caminho.</summary>
        private bool TryWalkFinalLeg(NPC npc, GameLocation location, OriginalSpot originalSpot, string hudMessage)
        {
            var targetTile = new Point((int)originalSpot.Tile.X, (int)originalSpot.Tile.Y);

            Stack<Point> path = PathFindController.findPathForNPCSchedules(npc.TilePoint, targetTile, location, 10000, npc);
            if (path is null || path.Count == 0)
                return false;

            npc.movementPause = 0;
            this.SetFreezeMotion(npc, false);

            npc.controller = new PathFindController(path, npc, location)
            {
                finalFacingDirection = 2,
                endBehaviorFunction = (c, l) => this.OnNpcFinishedWalkingHome(c as NPC, originalSpot, hudMessage)
            };
            return true;
        }

        /// <summary>Chamado quando um NPC "a caminho de casa" finalmente aparece no mapa de origem: dispara a perna final da caminhada, ou teleporta se não achar caminho.</summary>
        private void WalkFinalLegOrTeleport(NPC npc, OriginalSpot originalSpot, string hudMessage)
        {
            if (!this.TryWalkFinalLeg(npc, npc.currentLocation, originalSpot, hudMessage))
                this.TeleportNpcHome(npc, originalSpot, hudMessage);
        }

        /// <summary>Teleporta o NPC direto pro local original e restaura a rota que ele tinha antes de ser chamado — usado quando não dá pra andar (sem caminho, sem porta, ou preso demais tempo na travessia).</summary>
        private void TeleportNpcHome(NPC npc, OriginalSpot originalSpot, string hudMessage)
        {
            Game1.warpCharacter(npc, originalSpot.LocationName, originalSpot.Tile);
            npc.movementPause = 0;
            this.SetFreezeMotion(npc, false);
            npc.controller = originalSpot.Controller;
            Game1.addHUDMessage(new HUDMessage(hudMessage));
        }

        /// <summary>
        /// Acha, no local atual do NPC, o tile de saída (porta ou warp) cujo destino é exatamente o local
        /// informado — a saída "certa" pra ele andar até lá. O jogo guarda saídas em dois lugares diferentes:
        /// "warps" (bordas de mapas grandes, tipo Farm ↔ Town) e "doors" (entradas de prédios/casas, como a
        /// porta da mansão do prefeito), então checa os dois antes de desistir.
        /// </summary>
        private Point? GetExitTileToward(NPC npc, string targetLocationName)
        {
            GameLocation? location = npc.currentLocation;
            if (location is null)
                return null;

            Vector2 npcTile = npc.Tile;

            Point? nearestWarp = location.warps
                .Where(w => w.TargetName == targetLocationName)
                .Select(w => new Point(w.X, w.Y))
                .OrderBy(p => Math.Abs(p.X - npcTile.X) + Math.Abs(p.Y - npcTile.Y))
                .Cast<Point?>()
                .FirstOrDefault();
            if (nearestWarp != null)
                return nearestWarp;

            return location.doors.Pairs
                .Where(kvp => kvp.Value == targetLocationName)
                .Select(kvp => kvp.Key)
                .OrderBy(p => Math.Abs(p.X - npcTile.X) + Math.Abs(p.Y - npcTile.Y))
                .Cast<Point?>()
                .FirstOrDefault();
        }

        /// <summary>Chamado quando o NPC termina a perna final da caminhada (já no mapa de origem): restaura o controller real que ele tinha antes de ser chamado, deixando-o continuar a rotina de onde realmente parou.</summary>
        private void OnNpcFinishedWalkingHome(NPC? npc, OriginalSpot originalSpot, string hudMessage)
        {
            if (npc is null)
                return;

            npc.Halt();
            npc.controller = originalSpot.Controller;

            Game1.addHUDMessage(new HUDMessage(hudMessage));
        }

        /// <summary>Liga/desliga o campo protegido "freezeMotion" (herdado de Character) via reflection do SMAPI.</summary>
        private void SetFreezeMotion(NPC npc, bool frozen)
        {
            this.Helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(frozen);
        }

        /// <summary>
        /// Acha o tile exatamente do lado do jogador (direita, ou esquerda se a direita estiver bloqueada).
        /// Se os dois lados estiverem ocupados, cai no fallback de qualquer tile livre ao redor.
        /// </summary>
        private Vector2 GetSideTile(Farmer player)
        {
            Vector2 tile = player.Tile;
            Vector2[] sideOffsets = { new Vector2(1, 0), new Vector2(-1, 0) };

            foreach (Vector2 offset in sideOffsets)
            {
                Vector2 candidate = tile + offset;
                if (player.currentLocation.isTileLocationOpen(candidate))
                    return candidate;
            }

            return this.GetAdjacentFreeTile(player);
        }

        /// <summary>Acha um tile livre e colocável ao redor do jogador (ou o próprio tile do jogador, se não achar nenhum).</summary>
        private Vector2 GetAdjacentFreeTile(Farmer player)
        {
            Vector2 tile = player.Tile;
            Vector2[] offsets =
            {
                new Vector2(0, 1),
                new Vector2(0, -1),
                new Vector2(1, 0),
                new Vector2(-1, 0),
                new Vector2(1, 1),
                new Vector2(-1, -1),
                new Vector2(1, -1),
                new Vector2(-1, 1),
            };

            foreach (Vector2 offset in offsets)
            {
                Vector2 candidate = tile + offset;
                if (player.currentLocation.isTileLocationOpen(candidate))
                    return candidate;
            }

            return tile;
        }
    }
}
