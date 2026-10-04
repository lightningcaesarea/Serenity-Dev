using System.Threading.Tasks;
using Content.Server.Database;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Serenity.Economy;

/// <summary>
/// Federal Bills belong to characters, not accounts. The economy still reads and writes the <c>"credits"</c>
/// resource on a session; this decides which character that resource is, and persists it to that character.
/// </summary>
public interface ICharacterBalanceManager
{
    /// <summary>Fired when the balance of the character a player is playing changes, or they start playing one.</summary>
    event Action<ICommonSession>? ActiveBalanceChanged;

    /// <summary>What a character's account opens with the first time it spawns.</summary>
    double StartingBalance { get; }

    /// <summary>
    /// The player is now playing the character in <paramref name="slot"/>; null for a character that isn't one of
    /// theirs (a randomised one). Their <c>"credits"</c> become that character's balance once it has loaded.
    /// </summary>
    void SetActiveCharacter(ICommonSession session, int? slot);

    /// <summary>The slot of the character the player is playing this round, if any.</summary>
    int? GetActiveSlot(NetUserId user);

    /// <summary>The DB id of the character the player is playing, once its balance has loaded.</summary>
    int? GetActiveProfileId(NetUserId user);

    /// <summary>
    /// How much of the played character's balance is still starting funds: spendable by that character, but not
    /// transferable. 0 if they aren't playing a loaded character.
    /// </summary>
    double GetStartingFunds(NetUserId user);

    /// <summary>
    /// Take <paramref name="amount"/> out of the played character's balance as cash, starting funds first.
    /// <paramref name="fromStartingFunds"/> is how much of it was starting funds, which must be handed out as bills
    /// bound to this character.
    /// </summary>
    bool TryWithdraw(ICommonSession session, double amount, string reason, out double fromStartingFunds);

    /// <summary>Take <paramref name="amount"/> out of earned money only (not starting funds), e.g. to send to someone.</summary>
    bool TrySpendEarned(ICommonSession session, double amount, string reason);

    /// <summary>Put bound bills back into the played character's account as starting funds.</summary>
    bool TryDepositStartingFunds(ICommonSession session, double amount, string reason);

    /// <summary>Round over: nobody is playing a character any more.</summary>
    void ClearActiveCharacters();

    /// <summary>Every character of the player with its balance, live for the one being played.</summary>
    Task<List<CharacterBalanceSummary>> GetBalancesAsync(NetUserId user);

    /// <summary>
    /// Add to the balance of the character in <paramref name="slot"/>, whether or not it is being played.
    /// Null if the slot has no character, or the character is still loading.
    /// </summary>
    Task<double?> AdjustCharacterAsync(NetUserId user, int slot, double delta, string reason);

    /// <summary>Set the balance of the character in <paramref name="slot"/>. Same rules as <see cref="AdjustCharacterAsync"/>.</summary>
    Task<double?> SetCharacterAsync(NetUserId user, int slot, double value, string reason);
}
