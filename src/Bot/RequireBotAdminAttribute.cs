using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Requires guild context and either Discord Administrator permission, guild ownership,
/// or membership in a role listed in <see cref="Settings.BotSettings.PrivilegedRoleIds"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireBotAdminAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        if (context.User is not SocketGuildUser guildUser)
            return Task.FromResult(PreconditionResult.FromError(Localization.Get("disc_precond_guild_only")));

        if (guildUser.Guild.OwnerId == guildUser.Id)
            return Task.FromResult(PreconditionResult.FromSuccess());

        if (guildUser.GuildPermissions.Administrator)
            return Task.FromResult(PreconditionResult.FromSuccess());

        var roleIds = Application.BotSettings?.PrivilegedRoleIds;
        if (roleIds != null && roleIds.Count > 0 && guildUser.Roles.Any(r => roleIds.Contains(r.Id)))
            return Task.FromResult(PreconditionResult.FromSuccess());

        return Task.FromResult(PreconditionResult.FromError(Localization.Get("disc_precond_bot_priv")));
    }
}
