using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Shared._Serenity.Economy;
using Content.Shared.Preferences;
using Content.Shared.Stacks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Serenity.Economy;

/// <summary>
/// Federal Bills belong to characters: each character slot has its own balance, opened with the starting balance
/// the first time it spawns, and a deleted character's money never passes to whoever takes its slot next.
/// The starting balance is starting funds: spending uses it up first, and it is never more than the balance.
/// </summary>
[TestFixture]
public sealed class CharacterBalanceTest : GameTest
{
    private const double Starting = 20000;

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
    }

    private static HumanoidCharacterProfile Named(string name)
        => new() { Name = name };

    [Test]
    public async Task NewCharacterOpensWithStartingFundsOnce()
    {
        var db = GetDb(Pair.Server);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, Named("Ada"));

        var first = await db.EnsureCharacterBalance(user.UserId, 0, Starting, LedgerReasons.StartingBalance);
        var again = await db.EnsureCharacterBalance(user.UserId, 0, Starting, LedgerReasons.StartingBalance);

        Assert.That(first, Is.Not.Null);
        Assert.That(first!.Value.Balance, Is.EqualTo(Starting));
        Assert.That(first.Value.StartingFunds, Is.EqualTo(Starting), "the whole starting balance is starting funds");
        Assert.That(again, Is.EqualTo(first), "opening an account that already exists must not grant the starting balance again");

        var ledger = await db.GetPlayerResourceTransactions(user.UserId, 10, default);
        Assert.That(ledger, Has.Count.EqualTo(1));
        Assert.That(ledger[0].ProfileId, Is.EqualTo(first.Value.ProfileId));
        Assert.That(ledger[0].Reason, Is.EqualTo(LedgerReasons.StartingBalance));
    }

    [Test]
    public async Task EmptySlotHasNoAccount()
    {
        var db = GetDb(Pair.Server);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, Named("Ada"));

        Assert.That(await db.EnsureCharacterBalance(user.UserId, 3, Starting, LedgerReasons.StartingBalance), Is.Null);
    }

    [Test]
    public async Task CharactersHaveSeparateBalances()
    {
        var db = GetDb(Pair.Server);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, Named("Ada"));
        await db.SaveCharacterSlotAsync(user, Named("Bo"), 1);

        var ada = await db.EnsureCharacterBalance(user.UserId, 0, Starting, LedgerReasons.StartingBalance);
        var afterSalary = await db.AdjustCharacterBalance(user.UserId, ada!.Value.ProfileId, 150, 0, LedgerReasons.Salary("Chef"));

        Assert.That(afterSalary!.Value.Balance, Is.EqualTo(Starting + 150));
        Assert.That(afterSalary.Value.StartingFunds, Is.EqualTo(Starting), "income is never starting funds");

        var balances = await db.GetCharacterBalances(user.UserId, default);
        Assert.That(balances.Select(b => (b.Slot, b.Name, b.Balance)), Is.EqualTo(new[]
        {
            (0, "Ada", (double?) (Starting + 150)),
            (1, "Bo", (double?) null), // never spawned
        }));
    }

    [Test]
    public async Task StartingFundsStayWithinTheBalance()
    {
        var db = GetDb(Pair.Server);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, Named("Ada"));
        var ada = (await db.EnsureCharacterBalance(user.UserId, 0, Starting, LedgerReasons.StartingBalance))!.Value;

        // Spending 5,000 of starting funds.
        var spent = await db.AdjustCharacterBalance(user.UserId, ada.ProfileId, -5000, -5000, LedgerReasons.AtmWithdraw);
        Assert.That(spent, Is.EqualTo(new CharacterAccount(ada.ProfileId, Starting - 5000, Starting - 5000)));

        // An admin setting the balance below the starting funds cuts them down with it.
        var set = await db.SetCharacterBalance(user.UserId, ada.ProfileId, 1000, "fine");
        Assert.That(set!.Value.StartingFunds, Is.EqualTo(1000));

        var newest = (await db.GetPlayerResourceTransactions(user.UserId, 1, default)).Single();
        Assert.That((newest.Reason, newest.Delta, newest.BalanceAfter), Is.EqualTo(("fine", 1000 - (Starting - 5000), 1000d)));
    }

    [Test]
    public async Task DeletedCharacterMoneyDoesNotPassToItsSlot()
    {
        var db = GetDb(Pair.Server);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, Named("Ada"));
        await db.SaveCharacterSlotAsync(user, Named("Rich"), 1);

        var rich = await db.EnsureCharacterBalance(user.UserId, 1, Starting, LedgerReasons.StartingBalance);
        await db.AdjustCharacterBalance(user.UserId, rich!.Value.ProfileId, 1_000_000, 0, "jackpot");

        await db.SaveCharacterSlotAsync(user, null, 1);
        await db.SaveCharacterSlotAsync(user, Named("Fresh"), 1);

        var fresh = await db.EnsureCharacterBalance(user.UserId, 1, Starting, LedgerReasons.StartingBalance);
        Assert.That(fresh!.Value.ProfileId, Is.Not.EqualTo(rich.Value.ProfileId));
        Assert.That(fresh.Value.Balance, Is.EqualTo(Starting));
    }

    [Test]
    public async Task EditingACharacterKeepsItsMoney()
    {
        var db = GetDb(Pair.Server);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, Named("Ada"));

        var ada = await db.EnsureCharacterBalance(user.UserId, 0, Starting, LedgerReasons.StartingBalance);
        await db.AdjustCharacterBalance(user.UserId, ada!.Value.ProfileId, 42, 0, "tip");

        await db.SaveCharacterSlotAsync(user, Named("Ada Lovelace"), 0);

        var after = await db.EnsureCharacterBalance(user.UserId, 0, Starting, LedgerReasons.StartingBalance);
        Assert.That(after, Is.EqualTo(new CharacterAccount(ada.Value.ProfileId, Starting + 42, Starting)));
    }

    /// <summary>
    /// Bound bills never merge with another character's, nor with ordinary bills, and splitting a bound stack keeps
    /// both halves bound to the same character.
    /// </summary>
    [Test]
    public async Task BoundBillsStayWithTheirOwner()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var stacks = entMan.System<SharedStackSystem>();
        var bound = entMan.System<SharedBoundCashSystem>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var coords = map.GridCoords;

            EntityUid Bills(string proto, int count, int? owner)
            {
                var ent = entMan.SpawnEntity(proto, coords);
                stacks.SetCount((ent, entMan.GetComponent<StackComponent>(ent)), count);
                if (owner is { } id)
                    bound.Bind(ent, id, null, $"Owner {id}");
                return ent;
            }

            var mine = Bills("SpaceCashBound", 100, 1);
            var alsoMine = Bills("SpaceCashBound", 50, 1);
            var theirs = Bills("SpaceCashBound", 100, 2);
            var plain = Bills("SpaceCash", 100, null);

            Assert.That(stacks.TryMergeStacks(theirs, mine, out _), Is.False, "another character's bills must not merge");
            Assert.That(stacks.TryMergeStacks(plain, mine, out _), Is.False, "ordinary bills are a different stack type");
            Assert.That(stacks.TryMergeStacks(alsoMine, mine, out _), Is.True, "the same character's bills still merge");
            Assert.That(entMan.GetComponent<StackComponent>(mine).Count, Is.EqualTo(150));

            var half = stacks.Split(mine, 75, coords);
            Assert.That(half, Is.Not.Null);
            Assert.That(entMan.TryGetComponent<BoundCashComponent>(half!.Value, out var split), Is.True, "split bills stay bound");
            Assert.That(split!.ProfileId, Is.EqualTo(1));
        });
    }
}
