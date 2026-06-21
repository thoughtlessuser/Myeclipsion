using Content.Server._Rat.Bank;
using Content.Server.Cloning;
using Content.Server.Cloning.Components;
using Content.Shared.Bank.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Cloning;

[TestFixture]
public sealed class CloningBankStateTest
{
    [Test]
    public async Task AcceptedCloneKeepsBankState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IServerEntityManager>();

        await server.WaitAssertion(() =>
        {
            var cloning = entMan.System<CloningSystem>();
            var minds = entMan.System<SharedMindSystem>();
            var source = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var clone = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<MindContainerComponent>(source);
            entMan.EnsureComponent<MindContainerComponent>(clone);

            entMan.AddComponent<BankAccountComponent>(source).Balance = 12345;
            entMan.AddComponent<BankTransferHistoryComponent>(source).Entries.Add(new BankTransferHistoryRecord
            {
                Outgoing = true,
                CounterpartyName = "Alice",
                Amount = 500,
                Comment = "Test transfer",
                RoundTimestamp = TimeSpan.FromMinutes(1),
            });

            var mind = minds.CreateMind(null);
            minds.TransferTo(mind, source, mind: mind.Comp);
            entMan.AddComponent<BeingClonedComponent>(clone).BodyToClone = source;
            cloning.ClonesWaitingForMind.Add(mind.Comp, clone);

            cloning.TransferMindToClone(mind, mind.Comp);

            Assert.Multiple(() =>
            {
                Assert.That(mind.Comp.OwnedEntity, Is.EqualTo(clone));
                Assert.That(entMan.HasComponent<BankAccountComponent>(source), Is.False);
                Assert.That(entMan.HasComponent<BankTransferHistoryComponent>(source), Is.False);
                Assert.That(entMan.GetComponent<BankAccountComponent>(clone).Balance, Is.EqualTo(12345));
            });

            var history = entMan.GetComponent<BankTransferHistoryComponent>(clone).Entries;
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(history[0].Outgoing, Is.True);
                Assert.That(history[0].CounterpartyName, Is.EqualTo("Alice"));
                Assert.That(history[0].Amount, Is.EqualTo(500));
                Assert.That(history[0].Comment, Is.EqualTo("Test transfer"));
                Assert.That(history[0].RoundTimestamp, Is.EqualTo(TimeSpan.FromMinutes(1)));
            });
        });

        await pair.CleanReturnAsync();
    }
}
