using Discord.Commands;
using System.Threading.Tasks;

[RequireContext(ContextType.Guild)]
[RequireBotAdmin]
public class AdminCommands : ModuleBase<SocketCommandContext>
{
#if DEBUG
    [Command("debug")]
    [Summary("Command enabled for debug purposes. (!debug ...)")]
    [Remarks("skip")]
    public async Task Debug(string param1 = "", string param2 = "", string param3 = "")
    {
        await Context.Message.AddReactionAsync(EmojiList.GreenCheck);
        await SteamWebAPI.GetWorkshopItemDetails(new string[] { });
    }
#endif
}
