using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;

namespace CS2Retakes_InstantDefuse.Utils;

public class Output(string prefix)
{
    public void PrintToChat(IGameClient client, string message)
    {
        Config.SharedSystem.GetModSharp().PrintChannelFilter(HudPrintChannel.Chat, $"{prefix} {message}", new RecipientFilter(client));
    }

    public void PrintToChat(IPlayerController controller, string message)
    {
        Config.SharedSystem.GetModSharp().PrintChannelFilter(HudPrintChannel.Chat, $"{prefix} {message}", new RecipientFilter(controller.PlayerSlot));
    }

    public void PrintToChatAll(string message)
    {
        Config.SharedSystem.GetModSharp().PrintChannelAll(HudPrintChannel.Chat, $"{prefix} {message}");
    }
}