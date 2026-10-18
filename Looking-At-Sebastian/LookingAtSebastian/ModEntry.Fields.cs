using Microsoft.Xna.Framework;

namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private ModConfig Config = new();

    private Vector2 LastPlayerPosition = Vector2.Zero;
    private int PlayerStillTicks = 0;

    private float LastDistanceToSebastian = float.MaxValue;

    private long LastCloseSequenceTick = -999999;
    private long LastFarTriggerTick = -999999;
    private long LastApproachTriggerTick = -999999;

    private CloseStareStage CloseStage = CloseStareStage.None;
    private CloseStareScenario CloseScenario = CloseStareScenario.None;
    private long CloseStageStartTick = 0;

    // Para controlar as diferentes sequências de costas, que têm reações diferentes dependendo do tier de amizade.
    private BackCloseSequenceKind CurrentBackSequenceKind = BackCloseSequenceKind.None;

    // As sequências de lado têm uma segunda etapa depois do primeiro olhar, onde o Sebastian espera um pouco mais encarando para depois reagir de forma diferente. Essa variável conta os ticks encarando para avançar para essa segunda etapa.
    private SideCloseSequenceKind CurrentSideSequenceKind = SideCloseSequenceKind.None;
    private int SideSecondStareTicks = 0;

    // Sequência especial de frente, para 2 e 4 corações. Ela tem mais etapas e pode ser "pulada" se a jogadora chegar muito perto.
    private FrontCloseSequenceKind CurrentFrontSequenceKind = FrontCloseSequenceKind.None;
    private int FrontLowHeartSecondStareTicks = 0;
    private bool FrontLowHeartJumped = false;
    private bool CurrentDialogueHasDatingFrontCounterEmotes = false;

    // Algumas reações exigem que o Sebastian volte a olhar para a direção original depois, para não ficar estranho. Essa variável controla isso.
    private bool ShouldRestoreSebastianFacing = false;
    private int SavedSebastianFacingDirection = -1;

    private bool PendingFacingRestore = false;
    private long PendingFacingRestoreTick = 0;

    // Snapshot da animação especial que estava tocando antes da reação.
    private SebastianSpecialAnimationSnapshot? SavedSebastianSpriteState = null;

    // Snapshot específico para o Sebastian no PC. Diferente do snapshot normal,
    // este NÃO interrompe a animação quando a cena começa; ele só restaura se o
    // clique/diálogo fizer o jogo tirar o Sebastian da pose do computador.
    private SebastianSpecialAnimationSnapshot? SavedSebastianPcSpriteState = null;

    private PcSideSequenceKind CurrentPcSideSequenceKind = PcSideSequenceKind.None;

    internal static ModEntry? Instance;

    private string? PendingSebastianClickableDialogue = null;
    private bool PendingSebastianClickableDialogueIsPc = false;


}