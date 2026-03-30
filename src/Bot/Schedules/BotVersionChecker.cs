using System.Collections.Generic;
using System.Threading.Tasks;

public static partial class Schedules
{
    public static Task BotVersionChecker(List<object> args)
    {
        return BotUtility.NotifyLatestBotVersion();
    }
}
