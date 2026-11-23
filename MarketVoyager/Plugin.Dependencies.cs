using Roxyz0501.Dalamud.CommonUi;
namespace MarketVoyager;
public sealed partial class Plugin
{
    private readonly StandardPluginInfo dependencyInfo = new(
        new("Lifestream", "Lifestream", true, new(
            "Teleport to Limsa and travel between worlds.", "リムサへのテレポとワールド間移動。", "Teleport nach Limsa und Weltenreisen.", "Téléportation à Limsa et voyages entre mondes.", "림사 텔레포 및 월드 이동.", "传送至利姆萨并跨世界移动。", "傳送至利姆薩並跨世界移動。"), Guide: "https://github.com/NightmareXIV/Lifestream"),
        new("vnavmesh", "vnavmesh", true, new(
            "Walk between the market board, aetheryte and vendor.", "マーケット・エーテライト・NPC間の徒歩移動。", "Fußwege zwischen Marktbrett, Ätheryt und Händler.", "Déplacements à pied entre marché, éthérite et marchand.", "장터 게시판, 에테라이트, 상인 사이 도보 이동.", "在市场布告板、以太之光和商人之间步行。", "在市場布告欄、乙太之光和商人之間步行。"), Guide: "https://github.com/awgil/ffxiv_navmesh"));
    private void DrawDependencyInfo() => dependencyInfo.Draw(pi, config.Language!);
    private const string DependencyFontText = " #(),-.1345:=?ABCDEFGHIKLMNOPRSTUVWabcdefghijklmnopqrstuvwyz{}ÄÉÖßàäéü、。きくけしすせたでとなにのはへまみるをんイエグケサッテトドプポマムラリルレワン・ー世並之乙人以件传体作使依信傳光入内再別利刷功动効動南単可启告和商啟在场基報場太姆安導已市布并开徒必态息情態所打指插操新无未本板案機欄步歩法済無状狀独獨理用界确確移立管能至萨薩行装裝要訊認认資赖跨送重開間间需須가게관그기내능니다단도독동됨드라러레로리림미및별보본불비사상설성수습시안업없에열용월으은음이인작장정치태터테텔트판포플필합확활";
}
