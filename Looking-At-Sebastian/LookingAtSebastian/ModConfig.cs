namespace LookingAtSebastian;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;

    // 2 corações = 500 pontos de amizade.
    public int MinHearts { get; set; } = 2;

    // Distância de 2 tiles.
    // 2 tile = 128f.
    public float CloseStareDistance { get; set; } = 128f;

    // Quanto tempo precisa ficar parada encarando antes dele perceber,
    // quando ele está de lado ou de costas.
    // 120 ticks = 2 segundos.
    public int CloseNoticeRequiredStillTicks { get; set; } = 120;

    // Quando ele já está de frente para a jogadora,
    // precisa ficar encarando por mais tempo.
    // 350 ticks = 5,8 segundos.
    public int CloseFrontRequiredStillTicks { get; set; } = 350; // 5,8 segundos

    // Tempo entre ele perceber/virar e soltar o emote tímido.
    // 300 ticks = 5 segundos.
    public int CloseAfterNoticeDelayTicks { get; set; } = 300;

    // Chance da sequência acontecer depois que as condições forem cumpridas.
    // Deixa 100 para sempre acontecer no teste.
    public int CloseSequenceChance { get; set; } = 100;

    // Cooldown depois da sequência.
    public int CloseCooldownSeconds { get; set; } = 10;

    // Emotes.
    public int SideNoticeEmoteA { get; set; } = 40; // reticências
    public int BackNoticeEmote { get; set; } = 16; // exclamação
    public int ShyEmoteId { get; set; } = 60; // tímido
    public int BackNoticeEmoteA { get; set; } = 40; // reticências
    public int BackNoticeEmoteB { get; set; } = 8;  // interrogação
    public int SideNoticeEmoteB { get; set; } = 8;  // interrogação
    public int SideFinalNervousEmote { get; set; } = 28; // nervoso
    public int BackLowHeartSurpriseEmote { get; set; } = 16; // exclamação
    public int BackDatingHeartEmote { get; set; } = 20; // coração

    // Diálogos.
    public int CloseSide2DialogueCount { get; set; } = 10;
    public int CloseBack2DialogueCount { get; set; } = 10;
    public int CloseFront2DialogueCount { get; set; } = 10;

    public int CloseSide4DialogueCount { get; set; } = 10;
    public int CloseBack4DialogueCount { get; set; } = 10;
    public int CloseFront4DialogueCount { get; set; } = 10;

    public int CloseSide6DialogueCount { get; set; } = 10;
    public int CloseBack6DialogueCount { get; set; } = 10;
    public int CloseFront6DialogueCount { get; set; } = 10;

    public int CloseSide8DialogueCount { get; set; } = 10;
    public int CloseBack8DialogueCount { get; set; } = 10;
    public int CloseFront8DialogueCount { get; set; } = 10;

    public int DatingCloseSide8DialogueCount { get; set; } = 10;
    public int DatingCloseBack8DialogueCount { get; set; } = 10;
    public int DatingCloseFront8DialogueCount { get; set; } = 10;

    // Sistema de aproximação, pode continuar reagindo enquanto ele anda.
    public float ApproachDistance { get; set; } = 260f;
    public float ApproachResetDistance { get; set; } = 360f;
    public int ApproachEmoteChance { get; set; } = 100;
    public int ApproachDialogueChance { get; set; } = 20;
    public int ApproachCooldownSeconds { get; set; } = 45;
    public int ApproachDialogueCount { get; set; } = 10;
    public bool ApproachRequiresPlayerFacingSebastian { get; set; } = false;

    // Longe, se ainda formos manter depois.
    public float FarStareMinDistance { get; set; } = 180f;
    public float FarStareMaxDistance { get; set; } = 400f;
    public int FarRequiredStillTicks { get; set; } = 90;
    public int FarEmoteChance { get; set; } = 8;
    public int FarDialogueChance { get; set; } = 30;
    public int FarCooldownSeconds { get; set; } = 90;
    public int FarDialogueCount { get; set; } = 10;

    // Sequência especial quando o Sebastian está de costas.
    public int BackCloseRequiredStillTicks { get; set; } = 450; // 7,5 segundos

    public int BackNoticeEmoteDurationTicks { get; set; } = 90;

    public float BackLeaveDistance { get; set; } = 300f;
    public int BackDialogueOpenTimeoutTicks { get; set; } = 900;

    public bool DebugLogs { get; set; } = true;

    // Sequência especial quando o Sebastian está de lado.
    public int SideFirstStareRequiredTicks { get; set; } = 450; // 7,5 segundos
    public int SideSecondStareRequiredTicks { get; set; } = 300; // 5 segundos


    public int SideShakeDurationMs { get; set; } = 500;
    public int SideShakeToEmoteDelayTicks { get; set; } = 30;

    public float SideLeaveDistance { get; set; } = 300f;
    public int SideDialogueOpenTimeoutTicks { get; set; } = 900;

    // Sequência especial de frente para 2/4 corações.
    public float FrontLowHeartApproachJumpDistance { get; set; } = 250f;
    public float FrontLowHeartCloseDistance { get; set; } = 80f; // mais ou menos 1 tile, com folga

    public int FrontLowHeartFirstStareRequiredTicks { get; set; } = 450; // 7,5 segundos
    public int FrontLowHeartSecondStareRequiredTicks { get; set; } = 300; // 5 segundos

    public int FrontLowHeartShakeDurationMs { get; set; } = 500;
    public int FrontLowHeartShakeToEmoteDelayTicks { get; set; } = 30;
    public float FrontLowHeartLeaveDistance { get; set; } = 300f;
    public int FrontLowHeartDialogueOpenTimeoutTicks { get; set; } = 900;

    // Emote de exclamação quando a jogadora se aproxima em 6 corações.
    public int FrontSixHeartApproachEmote { get; set; } = 16;

    // Balões da sequência de frente para 8 corações.
    public int DatingFrontEightHeartBalloonDialogueCount { get; set; } = 5;

    // Balões depois que a jogadora se afasta.
    public int FrontEightHeartAfterLeaveBalloonDialogueCount { get; set; } = 5;
    public int DatingFrontEightHeartAfterLeaveBalloonDialogueCount { get; set; } = 5;

    // Emote feliz para namoro.
    public int FrontDatingHappyEmote { get; set; } = 32;

    // Permite que a sequência funcione quando o Sebastian está parado no lago fumando.

    // Tempo para restaurar a direção depois do emote final.
    // 300 ticks = 5 segundos.
    public int RestoreFacingAfterEmoteDelayTicks { get; set; } = 300; // 5 segundos

    // Delay entre restaurar a direção original e soltar o emote/balão final.
    // 45 ticks = 0,75 segundos.
    public int FinalLeaveReactionDelayTicks { get; set; } = 45;

    public int PlayerAngryEmoteId { get; set; } = 12; // brava
    public int DatingFrontCounterMusicEmoteId { get; set; } = 56; // notinha de música

    // Bloqueia as reações quando Sebastian está no PC.
    public bool DisableWhenSebastianAtComputer { get; set; } = true;

    // Área aproximada do PC no quarto do Sebastian.
    // Se precisar ajustar, vamos mudar esses números depois.
    public int SebastianComputerMinX { get; set; } = 8;
    public int SebastianComputerMaxX { get; set; } = 9;
    public int SebastianComputerTileY { get; set; } = 4;

    // Sequência especial quando Sebastian está no PC.
    public bool EnablePcSideStareSequence { get; set; } = true;

    // 1 tile com uma folguinha.
    public float PcSideStareDistance { get; set; } = 96f;

    // 6 segundos.
    public int PcSideRequiredStillTicks { get; set; } = 360;

    // Primeiro emote: reticências.
    public int PcSideNoticeEmote { get; set; } = 40;

    // Tempo aproximado para o emote 40 sumir.
    public int PcSideNoticeEmoteDurationTicks { get; set; } = 90;

    // Timeout se a jogadora não clicar nele.
    public int PcSideDialogueOpenTimeoutTicks { get; set; } = 900;

    // Diálogos exclusivos do PC
    public int PcSide2DialogueCount { get; set; } = 10;
    public int PcSide4DialogueCount { get; set; } = 10;
    public int PcSide6DialogueCount { get; set; } = 10;
    public int PcSide8DialogueCount { get; set; } = 10;
    public int DatingPcSide8DialogueCount { get; set; } = 10;
    public float PcSideLeaveDistance { get; set; } = 300f;

    // Balões ao se afastar depois do diálogo no PC.
    public int PcSide6AfterLeaveBalloonCount { get; set; } = 5;
    public int PcSide8AfterLeaveBalloonCount { get; set; } = 5;
    public int DatingPcSide8AfterLeaveBalloonCount { get; set; } = 5;

    // Distância mínima/máxima para iniciar a cena de frente com balão.
    public float FrontBalloonMinDistance { get; set; } = 500f;
    public float FrontBalloonMaxDistance { get; set; } = 700f;

    public int FrontBalloon4_6_8DialogueCount { get; set; } = 5;

    // Permite interromper temporariamente animações especiais estacionárias
    // para a reação acontecer e depois restaurar a animação original.
    public bool RestoreSpecialAnimationsAfterReaction { get; set; } = true;
}